using Aspire.Hosting.ApplicationModel;
using Azure.Core;
using Azure.Provisioning;
using Azure.Provisioning.Cdn;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.Primitives;
using Azure.Provisioning.Storage;

namespace Aspire.Hosting.Azure;

public static class AzureStaticWebsiteExtensions
{
    public static IDistributedApplicationBuilder AddAzureStaticWebsite(
        this IDistributedApplicationBuilder builder,
        string name,
        AddAzureStaticWebsiteOptions options)
    {
        var siteSourcePath        = options.SiteSourcePath;
        var afdProfileName        = options.AfdProfileName;
        var afdEndpointName       = options.AfdEndpointName;
        var afdCustomDomain       = options.AfdCustomDomain;
        var afdResourceGroup      = options.AfdResourceGroup;
        var afdCustomDomainArmName = options.AfdCustomDomainArmName;
        var dnsResourceGroup      = options.DnsResourceGroup;

        var storage = builder.AddAzureStorage("storage")
            .ConfigureInfrastructure(options =>
            {
                var storageAccount = options.GetProvisionableResources()
                    .OfType<StorageAccount>()
                    .Single();

                storageAccount.Sku = new StorageSku { Name = StorageSkuName.StandardLrs };
                storageAccount.Kind = StorageKind.StorageV2;
                storageAccount.AllowBlobPublicAccess = true;
                storageAccount.AllowSharedKeyAccess = true;

                options.Add(new ProvisioningOutput("storageAccountName", typeof(string))
                {
                    Value = storageAccount.Name
                });
                options.Add(new ProvisioningOutput("storageWebUrl", typeof(string))
                {
                    Value = storageAccount.PrimaryEndpoints.WebUri
                });
            })
            // No managed identity for a static site — suppress the auto-generated
            // provision-storage-roles Bicep step that needs a compute principal.
            .WithAnnotation(
                new DefaultRoleAssignmentsAnnotation(new HashSet<RoleDefinition>()),
                ResourceAnnotationMutationBehavior.Replace);

        var storageWebUrlRef = storage.GetOutput("storageWebUrl");

        var frontDoor = builder.AddAzureFrontDoor("azure-shared")
            .AsExisting(
                builder.AddParameter("frontdoor-name", afdProfileName),
                builder.AddParameter("frontdoor-rg",   afdResourceGroup))
            .ConfigureInfrastructure(infra =>
            {
                // The auto-generated CdnProfile would CREATE a brand-new Front Door instance.
                // Remove it and replace with an existing resource reference so all sub-resources
                // (endpoint, origin group, origin, custom domain, route) are added to the
                // REAL existing profile instead of a freshly created one.
                var generatedProfile = infra.GetProvisionableResources()
                    .OfType<CdnProfile>()
                    .Single();
                infra.Remove(generatedProfile);

                var profile = CdnProfile.FromExisting("azure_shared");
                profile.Name = afdProfileName;
                infra.Add(profile);

                // Bring the storage web URL from the storage Bicep output into this template.
                // AsProvisioningParameter registers the cross-template dependency in the manifest.
                var storageWebUrlParam = storageWebUrlRef.AsProvisioningParameter(infra, "storageWebUrl");

                // Bicep var: strip 'https://' prefix and trailing '/' to get just the hostname.
                var paramRef = new IdentifierExpression(storageWebUrlParam.BicepIdentifier);
                var hostnameVar = new ProvisioningVariable("storageWebHost", typeof(string))
                {
                    Value = new FunctionCallExpression(
                        new IdentifierExpression("replace"),
                        new FunctionCallExpression(
                            new IdentifierExpression("replace"),
                            paramRef,
                            new StringLiteralExpression("https://"),
                            new StringLiteralExpression("")),
                        new StringLiteralExpression("/"),
                        new StringLiteralExpression(""))
                };
                infra.Add(hostnameVar);

                // ── AFD endpoint ─────────────────────────────────────────────
                var endpoint = new FrontDoorEndpoint("staticSiteEp")
                {
                    Parent = profile,
                    Name = afdEndpointName,
                    EnabledState = EnabledState.Enabled,
                    Location = new AzureLocation("global"),
                };
                infra.Add(endpoint);

                // ── Origin group ─────────────────────────────────────────────
                var originGroup = new FrontDoorOriginGroup("staticSiteOg")
                {
                    Parent = profile,
                    Name = $"{afdEndpointName}-og",
                    HealthProbeSettings = new HealthProbeSettings
                    {
                        ProbePath = "/",
                        ProbeProtocol = HealthProbeProtocol.Https,
                        ProbeRequestType = HealthProbeRequestType.Get,
                        ProbeIntervalInSeconds = 100,
                    },
                    LoadBalancingSettings = new LoadBalancingSettings
                    {
                        SampleSize = 4,
                        SuccessfulSamplesRequired = 3,
                        AdditionalLatencyInMilliseconds = 50,
                    },
                };
                infra.Add(originGroup);

                // ── Origin — hostname resolved at deploy time from storage output ──
                var origin = new FrontDoorOrigin("staticSiteOrigin")
                {
                    Parent = originGroup,
                    Name = "static-site-origin",
                    HostName = hostnameVar,
                    OriginHostHeader = hostnameVar,
                    HttpPort = 80,
                    HttpsPort = 443,
                    Priority = 1,
                    Weight = 1000,
                    EnabledState = EnabledState.Enabled,
                    EnforceCertificateNameCheck = true,
                };
                infra.Add(origin);

                // ── Custom domain ─────────────────────────────────────────────
                var customDomain = new FrontDoorCustomDomain("knickssweepDomain")
                {
                    Parent = profile,
                    Name = afdCustomDomainArmName,
                    HostName = afdCustomDomain,
                    TlsSettings = new FrontDoorCustomDomainHttpsContent
                    {
                        CertificateType = FrontDoorCertificateType.ManagedCertificate,
                        // Tls1_2 enum serializes as 'TLS 1.2' but ARM expects 'TLS12'
                        MinimumTlsVersion = new BicepValue<FrontDoorMinimumTlsVersion>(
                            new StringLiteralExpression("TLS12")),
                    },
                };
                infra.Add(customDomain);

                // ── Route ────────────────────────────────────────────────────
                var route = new FrontDoorRoute("defaultRoute")
                {
                    Parent = endpoint,
                    Name = "default",
                    OriginGroupId = ((IBicepValue)originGroup.Id).Compile(),
                    ForwardingProtocol = ForwardingProtocol.HttpsOnly,
                    HttpsRedirect = HttpsRedirect.Enabled,
                    LinkToDefaultDomain = LinkToDefaultDomain.Enabled,
                    EnabledState = EnabledState.Enabled,
                    PatternsToMatch = { "/*" },
                    SupportedProtocols = { FrontDoorEndpointProtocol.Http, FrontDoorEndpointProtocol.Https },
                    CacheConfiguration = new FrontDoorRouteCacheConfiguration
                    {
                        CompressionSettings = new RouteCacheCompressionSettings
                        {
                            IsCompressionEnabled = true,
                            ContentTypesToCompress =
                            {
                                "text/plain", "text/html", "text/css",
                                "application/javascript", "application/json",
                                "image/svg+xml"
                            },
                        },
                        QueryStringCachingBehavior = FrontDoorQueryStringCachingBehavior.IgnoreQueryString,
                    },
                };
                route.CustomDomains.Add(new FrontDoorActivatedResourceInfo
                {
                    Id = ((IBicepValue)customDomain.Id).Compile(),
                });
                infra.Add(route);

                infra.Add(new ProvisioningOutput("afdEndpointHostname", typeof(string))
                {
                    Value = endpoint.HostName,
                });
            });

        builder.AddStaticSiteDeployment(
            name,
            siteSourcePath,
            storage,
            frontDoor,
            new DnsOptions
            {
                CustomDomain           = afdCustomDomain,
                ResourceGroup          = dnsResourceGroup,
                AfdProfileName         = afdProfileName,
                AfdResourceGroup       = afdResourceGroup,
                AfdEndpointName        = afdEndpointName,
                AfdCustomDomainArmName = afdCustomDomainArmName,
            });

        return builder;
    }
}
