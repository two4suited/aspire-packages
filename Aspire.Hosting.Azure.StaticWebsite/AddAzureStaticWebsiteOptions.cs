namespace Aspire.Hosting.Azure.StaticWebsite;

public class AddAzureStaticWebsiteOptions
{
    public required string  SiteSourcePath         { get; init; }
    public required string  AzureFrontDoorProfileName         { get; init; }
    public required string  AzureFrontDoorEndpointName        { get; init; }
    public required string  AzureFrontDoorResourceGroup       { get; init; }
    public string?          AzureFrontDoorCustomDomain        { get; init; }
    public string?          AzureFrontDoorCustomDomainName { get; init; }
    public string?          DnsResourceGroup       { get; init; }
}
