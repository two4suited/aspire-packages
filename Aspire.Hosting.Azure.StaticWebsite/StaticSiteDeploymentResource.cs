using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Azure;

internal class StaticSiteDeploymentResource : IResource
{
    public string Name { get; }
    public ResourceAnnotationCollection Annotations { get; } = new();
    public StaticSiteDeploymentResource(string name) => Name = name;
}
