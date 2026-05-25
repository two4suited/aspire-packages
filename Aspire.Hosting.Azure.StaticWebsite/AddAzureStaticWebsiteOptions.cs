namespace Aspire.Hosting.Azure;

public class AddAzureStaticWebsiteOptions
{
    public required string SiteSourcePath          { get; init; }
    public required string AfdProfileName          { get; init; }
    public required string AfdEndpointName         { get; init; }
    public required string AfdCustomDomain         { get; init; }
    public required string AfdResourceGroup        { get; init; }
    public required string AfdCustomDomainArmName  { get; init; }
    public required string DnsResourceGroup        { get; init; }
}
