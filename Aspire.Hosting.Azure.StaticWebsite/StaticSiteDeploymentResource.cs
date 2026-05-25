using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Azure;

class StaticSiteDeploymentResource : IResource
{
    public string Name { get; }
    public ResourceAnnotationCollection Annotations { get; } = new();
    public StaticSiteDeploymentResource(string name) => Name = name;
}
