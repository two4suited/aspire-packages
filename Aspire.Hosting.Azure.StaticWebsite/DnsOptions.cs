namespace Aspire.Hosting.Azure.StaticWebsite;

internal record DnsOptions
{
    public required string CustomDomain           { get; init; } // zone name + FQDN, e.g. "knickssweep.com"
    public required string ResourceGroup          { get; init; } // RG hosting the DNS zone
    public required string AfdProfileName         { get; init; } // AFD profile name
    public required string AfdResourceGroup       { get; init; } // RG of the AFD profile
    public required string AfdEndpointName        { get; init; } // AFD endpoint ARM name
    public required string AfdCustomDomainArmName { get; init; } // AFD custom domain ARM resource name
}
