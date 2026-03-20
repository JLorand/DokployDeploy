using Aspire.Hosting.ApplicationModel;

namespace Ridder.Hosting.Dokploy;

public interface IDokployPublishAnnotation : IResourceAnnotation
{
    DokployProjectEnvironmentResource Environment { get; }
}

public sealed class DokployPublishApplicationAnnotation : IDokployPublishAnnotation
{
    public DokployPublishApplicationAnnotation(DokployProjectEnvironmentResource environment, DokployApplicationOptions? options = null)
    {
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        Options = (options ?? new DokployApplicationOptions()).Clone();
    }

    public DokployProjectEnvironmentResource Environment { get; }
    public DokployApplicationOptions Options { get; }

    public string ResolveApplicationName(string resourceName)
    {
        if (!string.IsNullOrWhiteSpace(Options.ApplicationName))
        {
            return Options.ApplicationName;
        }

        return $"{Environment.Name}-app-{resourceName}";
    }
}

internal sealed class DokployManifestAnnotation : IResourceAnnotation;