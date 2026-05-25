using System.Diagnostics;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;

namespace Aspire.Hosting.Azure.StaticWebsite;

internal static class StaticSiteDeploymentExtensions
{
#pragma warning disable ASPIREPIPELINES001, ASPIREPIPELINES003
    public static IResourceBuilder<StaticSiteDeploymentResource> AddStaticSiteDeployment(
        this IDistributedApplicationBuilder builder,
        string name,
        string siteSourcePath,
        IResourceBuilder<AzureStorageResource> storage,
        IResourceBuilder<AzureFrontDoorResource> frontDoor,
        DnsOptions? dns = null)
    {
        var storageAccountNameRef = storage.GetOutput("storageAccountName");
        var afdEndpointHostnameRef = frontDoor.GetOutput("afdEndpointHostname");
        var resource = new StaticSiteDeploymentResource(name);

        return builder.AddResource(resource)
            .WithPipelineStepFactory(
                stepName:  "build-and-deploy-static-site",
                callback: async context =>
                {
                    var sourcePath = Path.GetFullPath(siteSourcePath);
                    var distPath   = Path.Combine(sourcePath, "dist");

                    // ── 1. Build ──────────────────────────────────────────
                    await using var buildTask = await context.ReportingStep.CreateTaskAsync(
                        "Build Astro site (npm run build)", context.CancellationToken);

                    using var buildProc = Process.Start(new ProcessStartInfo("npm", "run build")
                    {
                        WorkingDirectory = sourcePath,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                    })!;
                    await buildProc.WaitForExitAsync(context.CancellationToken);

                    if (buildProc.ExitCode != 0)
                    {
                        var err = await buildProc.StandardError.ReadToEndAsync(context.CancellationToken);
                        await buildTask.FailAsync(err.Trim());
                        throw new InvalidOperationException("Astro build failed.");
                    }
                    await buildTask.SucceedAsync("Build succeeded");

                    var accountName = await storageAccountNameRef.GetValueAsync(context.CancellationToken);

                    // ── 2. Enable static website ──────────────────────────
                    await using var enableTask = await context.ReportingStep.CreateTaskAsync(
                        $"Enable static website hosting ({accountName})", context.CancellationToken);
                    await RunAzAsync(
                        $"storage blob service-properties update " +
                        $"--account-name {accountName} --static-website " +
                        $"--index-document index.html --404-document 404.html " +
                        $"--auth-mode login",
                        context.CancellationToken);
                    await enableTask.SucceedAsync("Hosting enabled");

                    // ── 3. Upload files ───────────────────────────────────
                    // Use the account key — AllowSharedKeyAccess is enabled
                    // in the storage Bicep, so no RBAC wait required.
                    await using var uploadTask = await context.ReportingStep.CreateTaskAsync(
                        "Upload dist/ to $web container", context.CancellationToken);

                    var accountKey = (await RunAzOutputAsync(
                        $"storage account keys list " +
                        $"--account-name {accountName} --query [0].value -o tsv",
                        context.CancellationToken)).Trim();

                    await RunAzAsync(
                        $"storage blob upload-batch " +
                        $"--account-name {accountName} " +
                        $"--account-key \"{accountKey}\" " +
                        $"--destination \"$web\" " +
                        $"--source \"{distPath}\" --overwrite",
                        context.CancellationToken);
                    await uploadTask.SucceedAsync("Files uploaded");

                    var webEndpoint = (await RunAzOutputAsync(
                        $"storage account show --name {accountName} --query primaryEndpoints.web -o tsv",
                        context.CancellationToken)).Trim();
                    context.Summary.Add("🌐 Static Website", webEndpoint);

                    var fdHostname = await afdEndpointHostnameRef.GetValueAsync(context.CancellationToken);
                    context.Summary.Add("🌐 Front Door", $"https://{fdHostname}/");

                    // ── 4. Configure DNS (optional) ───────────────────────
                    if (dns is not null)
                    {
                        await using var dnsTask = await context.ReportingStep.CreateTaskAsync(
                            $"Configure DNS for {dns.CustomDomain}", context.CancellationToken);

                        var subscriptionId = (await RunAzOutputAsync(
                            "account show --query id -o tsv",
                            context.CancellationToken)).Trim();

                        // Resource ID for the ALIAS A record target
                        var endpointResourceId =
                            $"/subscriptions/{subscriptionId}/resourceGroups/{dns.AzureFrontDoorResourceGroup}" +
                            $"/providers/Microsoft.Cdn/profiles/{dns.AzureFrontDoorProfileName}/afdEndpoints/{dns.AzureFrontDoorEndpointName}";

                        // Validation token Front Door needs to prove we own the domain
                        var validationToken = (await RunAzOutputAsync(
                            $"afd custom-domain show " +
                            $"--resource-group {dns.AzureFrontDoorResourceGroup} --profile-name {dns.AzureFrontDoorProfileName} " +
                            $"--custom-domain-name {dns.AzureFrontDoorCustomDomainArmName} " +
                            $"--query \"validationProperties.validationToken\" -o tsv",
                            context.CancellationToken)).Trim();

                        // Delete existing records first (idempotent re-deploy — ignore not-found)
                        try { await RunAzAsync(
                            $"network dns record-set a delete " +
                            $"--resource-group {dns.ResourceGroup} --zone-name {dns.CustomDomain} " +
                            $"--name \"@\" --yes",
                            context.CancellationToken); } catch { /* not yet present */ }

                        try { await RunAzAsync(
                            $"network dns record-set txt delete " +
                            $"--resource-group {dns.ResourceGroup} --zone-name {dns.CustomDomain} " +
                            $"--name \"_dnsauth\" --yes",
                            context.CancellationToken); } catch { /* not yet present */ }

                        // ALIAS A record — apex domain → Front Door endpoint
                        await RunAzAsync(
                            $"network dns record-set a create " +
                            $"--resource-group {dns.ResourceGroup} --zone-name {dns.CustomDomain} " +
                            $"--name \"@\" --ttl 3600 " +
                            $"--target-resource \"{endpointResourceId}\"",
                            context.CancellationToken);

                        // _dnsauth TXT record — Front Door domain ownership proof
                        await RunAzAsync(
                            $"network dns record-set txt add-record " +
                            $"--resource-group {dns.ResourceGroup} --zone-name {dns.CustomDomain} " +
                            $"--record-set-name \"_dnsauth\" --value \"{validationToken}\"",
                            context.CancellationToken);

                        await dnsTask.SucceedAsync("DNS records configured");
                        context.Summary.Add("🌐 Custom Domain", $"https://{dns.CustomDomain}/");
                    }
                },
                dependsOn: [WellKnownPipelineSteps.DeployPrereq],
                requiredBy: [WellKnownPipelineSteps.Deploy]);
    }
#pragma warning restore ASPIREPIPELINES001, ASPIREPIPELINES003

    static async Task RunAzAsync(string args, CancellationToken ct)
    {
        using var proc = Process.Start(new ProcessStartInfo("az", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        })!;
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"az command failed (exit {proc.ExitCode}): {(await stderrTask).Trim()}");
    }

    static async Task<string> RunAzOutputAsync(string args, CancellationToken ct)
    {
        using var proc = Process.Start(new ProcessStartInfo("az", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        })!;
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"az command failed (exit {proc.ExitCode}): {(await stderrTask).Trim()}");
        return await stdoutTask;
    }
}
