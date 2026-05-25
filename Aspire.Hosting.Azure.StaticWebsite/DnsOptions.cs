namespace Aspire.Hosting.Azure.StaticWebsite;

internal sealed record DnsOptions
{
    public required string CustomDomain { get; init; }                // zone name + FQDN, e.g. "knickssweep.com"
    public required string ResourceGroup { get; init; }               // RG hosting the DNS zone
    public required string AzureFrontDoorProfileName { get; init; }   // AFD profile name
    public required string AzureFrontDoorResourceGroup { get; init; } // RG of the AFD profile
    public required string AzureFrontDoorEndpointName { get; init; }  // AFD endpoint ARM name
    public required string AzureFrontDoorCustomDomainName { get; init; } // AFD custom domain ARM resource name
}
