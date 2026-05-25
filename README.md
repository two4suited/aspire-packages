# aspire-packages

Custom .NET Aspire hosting extensions for Azure.

## Packages

| Package | Description |
|---|---|
| [Aspire.Hosting.Azure.StaticWebsite](Aspire.Hosting.Azure.StaticWebsite/README.md) | Provisions Azure Blob Storage static website hosting behind an existing Azure Front Door profile, with optional custom domain and DNS configuration. |

On `azd up` / publish it will also:
1. Run `npm run build` in your site's source directory
2. Enable static website hosting on the storage account
3. Upload the `dist/` output to the `$web` blob container
4. Optionally create or update Azure DNS records for a custom domain

## Prerequisites

- An **existing Azure Front Door Premium profile** (Premium is required for managed TLS certificates on custom domains)
- **Azure CLI** (`az`) in your PATH and logged in (`az login`)
- **Node.js / npm** in your PATH

## Installation

```shell
dotnet add package Aspire.Hosting.Azure.StaticWebsite --version 0.0.10
```

This package is hosted on GitHub Packages. Add the source to your `nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="github" value="https://nuget.pkg.github.com/two4suited/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github>
      <add key="Username" value="YOUR_GITHUB_USERNAME" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </github>
  </packageSourceCredentials>
</configuration>
```

> Your `GITHUB_TOKEN` (or PAT) needs the `read:packages` scope. In GitHub Actions, set `GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}` and ensure `packages: read` is in your workflow permissions.

## Usage

Call `AddAzureStaticWebsite` from your Aspire `AppHost` `Program.cs`:

```csharp
builder.AddAzureStaticWebsite("my-site", new AddAzureStaticWebsiteOptions
{
    SiteSourcePath                = "../MyAstroSite",
    AzureFrontDoorProfileName     = "my-afd-profile",
    AzureFrontDoorEndpointName    = "my-site-endpoint",
    AzureFrontDoorResourceGroup   = "my-afd-resource-group",
});
```

### With a custom domain and DNS

Providing all three optional properties enables managed TLS on the custom domain and automatically creates the required Azure DNS records:

```csharp
builder.AddAzureStaticWebsite("my-site", new AddAzureStaticWebsiteOptions
{
    SiteSourcePath                  = "../MyAstroSite",
    AzureFrontDoorProfileName       = "my-afd-profile",
    AzureFrontDoorEndpointName      = "my-site-endpoint",
    AzureFrontDoorResourceGroup     = "my-afd-resource-group",

    // Optional — custom domain
    AzureFrontDoorCustomDomain      = "www.example.com",
    AzureFrontDoorCustomDomainName  = "www-example-com",   // ARM resource name (no dots)

    // Required when using a custom domain — RG that contains the Azure DNS zone
    DnsResourceGroup                = "my-dns-resource-group",
});
```

## Options reference

| Property | Required | Description |
|---|---|---|
| `SiteSourcePath` | ✅ | Relative or absolute path to the site source directory. `npm run build` is run here and `dist/` is uploaded. |
| `AzureFrontDoorProfileName` | ✅ | Name of the **existing** Azure Front Door profile to attach to. |
| `AzureFrontDoorEndpointName` | ✅ | Name for the new AFD endpoint that will be created inside the profile. |
| `AzureFrontDoorResourceGroup` | ✅ | Resource group that contains the Front Door profile. |
| `AzureFrontDoorCustomDomain` | ☑️ | Fully-qualified custom domain name, e.g. `www.example.com`. Enables managed TLS. Requires `AzureFrontDoorCustomDomainName`. |
| `AzureFrontDoorCustomDomainName` | ☑️ | ARM resource name for the AFD custom domain resource (no dots), e.g. `www-example-com`. |
| `DnsResourceGroup` | ☑️ | Resource group containing the Azure DNS zone for `AzureFrontDoorCustomDomain`. Required when using a custom domain. |

## What gets provisioned

| Resource | Notes |
|---|---|
| Azure Storage Account (Standard LRS, StorageV2) | Static website hosting enabled; public blob access on |
| Front Door endpoint | Created inside your existing profile |
| Front Door origin group + origin | Points to the storage static website hostname |
| Front Door route | HTTPS-only, caching enabled for common web asset types |
| Front Door custom domain | Managed TLS certificate — optional |
| Azure DNS A record + TXT validation record | Optional, created when `DnsResourceGroup` is supplied |