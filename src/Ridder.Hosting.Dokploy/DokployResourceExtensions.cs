using Aspire.Hosting.ApplicationModel;

namespace Ridder.Hosting.Dokploy;

internal static class DokployResourceExtensions
{
    public static bool TryGetDokployPublishAnnotation(this IResource resource, out DokployPublishApplicationAnnotation? annotation)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (resource.TryGetAnnotationsOfType<DokployPublishApplicationAnnotation>(out var annotations))
        {
            annotation = annotations.LastOrDefault();
            return annotation is not null;
        }

        annotation = null;
        return false;
    }

    public static bool TryGetDokployPublishAnnotation(this IResource resource, DokployProjectEnvironmentResource environment, out DokployPublishApplicationAnnotation? annotation)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(environment);

        if (resource.TryGetAnnotationsOfType<DokployPublishApplicationAnnotation>(out var annotations))
        {
            annotation = annotations.LastOrDefault(a => ReferenceEquals(a.Environment, environment) || string.Equals(a.Environment.Name, environment.Name, StringComparison.OrdinalIgnoreCase));
            return annotation is not null;
        }

        annotation = null;
        return false;
    }

    public static IReadOnlyList<IComputeResource> GetDokployComputeResources(this IEnumerable<IComputeResource> computeResources, DokployProjectEnvironmentResource environment)
    {
        ArgumentNullException.ThrowIfNull(computeResources);
        ArgumentNullException.ThrowIfNull(environment);

        var allResources = computeResources.ToList();
        var targetedResources = allResources
            .Where(resource => resource.TryGetDokployPublishAnnotation(environment, out _))
            .ToList();

        return targetedResources.Count > 0 ? targetedResources : allResources;
    }
}