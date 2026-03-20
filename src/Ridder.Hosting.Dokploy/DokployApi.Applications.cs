using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Docker;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ridder.Hosting.Dokploy;

internal partial class DokployApi
{
    internal async Task<Application> GetOrCreateApplication(string appName, string projectName)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            throw new ArgumentException("Application name must be provided.", nameof(appName));
        }

        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new ArgumentException("Project name must be provided.", nameof(projectName));
        }

        using var http = CreateHttpClient();

        var project = await GetProjectOrCreateAsync(projectName);
        if (string.IsNullOrWhiteSpace(project.Id))
        {
            throw new InvalidOperationException($"Project '{projectName}' does not have a projectId.");
        }

        using var projectResponse = await http.GetAsync($"api/project.one?projectId={Uri.EscapeDataString(project.Id)}");
        projectResponse.EnsureSuccessStatusCode();

        var refreshedProject = await DokployResponseReaders.ReadProjectFromResponseAsync(projectResponse)
            ?? throw new InvalidOperationException($"Could not parse project.one response for project '{projectName}'.");

        var targetEnvironment = refreshedProject.Environments
            .FirstOrDefault(e => string.Equals(e.Name, "production", StringComparison.OrdinalIgnoreCase))
            ?? refreshedProject.Environments.FirstOrDefault();

        if (targetEnvironment is null || string.IsNullOrWhiteSpace(targetEnvironment.Id))
        {
            throw new InvalidOperationException($"Project '{refreshedProject.Name}' has no usable environment for application deployment.");
        }

        var existing = targetEnvironment.Applications.FirstOrDefault(a =>
            string.Equals(a.Name, appName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.AppName, appName, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            logger.LogInformation("Application {AppName} already exists in project {ProjectName}.", appName, refreshedProject.Name);
            return existing;
        }

        var createBody = JsonSerializer.Serialize(new
        {
            name = appName,
            appName = appName,
            description = "Created by Aspire deploy pipeline.",
            environmentId = targetEnvironment.Id
        }, JsonOptions);

        using var createResponse = await http.PostAsync("api/application.create", CreateJsonContent(createBody));
        createResponse.EnsureSuccessStatusCode();

        var created = await DokployResponseReaders.ReadApplicationFromResponseAsync(createResponse);
        logger.LogInformation("Created application {AppName} in project {ProjectName}.", appName, refreshedProject.Name);

        using var verifyResponse = await http.GetAsync($"api/project.one?projectId={Uri.EscapeDataString(project.Id)}");
        verifyResponse.EnsureSuccessStatusCode();

        var verifiedProject = await DokployResponseReaders.ReadProjectFromResponseAsync(verifyResponse)
            ?? throw new InvalidOperationException($"Application create succeeded but project '{projectName}' could not be reloaded.");

        var verifiedEnvironment = verifiedProject.Environments
            .FirstOrDefault(e => string.Equals(e.Name, targetEnvironment.Name, StringComparison.OrdinalIgnoreCase));

        var verified = verifiedEnvironment?.Applications.FirstOrDefault(a =>
            string.Equals(a.Name, appName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.AppName, appName, StringComparison.OrdinalIgnoreCase));

        if (verified is null && created is not null)
        {
            verified = verifiedEnvironment?.Applications.FirstOrDefault(a =>
                string.Equals(a.Name, created.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.AppName, created.AppName, StringComparison.OrdinalIgnoreCase));
        }

        if (verified is null)
        {
            throw new InvalidOperationException($"Application '{appName}' was not found after create in project '{projectName}'.");
        }

        return verified;
    }

    internal async Task ConfigureApplicationAsync(
        Application application,
        string projectName,
        IComputeResource rsc,
        DistributedApplicationExecutionContext executionContext,
        IReadOnlyDictionary<string, string> applicationHostsByResource,
        CancellationToken cancellationToken)
    {
        using var http = CreateHttpClient();

        rsc.TryGetDokployPublishAnnotation(out var publishAnnotation);

        if (string.IsNullOrWhiteSpace(application.Id))
        {
            throw new InvalidOperationException($"Application '{rsc.Name}' has no applicationId, so provider cannot be configured.");
        }

        var registryUrl = GetRegistryUrl();

        string? dockerImage = null;

        if (rsc.TryGetLastAnnotation<DockerfileBuildAnnotation>(out _) || rsc is ProjectResource)
        {
            var imageRef = new ContainerImageReference(rsc);
            dockerImage = await ((IValueProvider)imageRef).GetValueAsync();
        }
        else if (rsc.TryGetContainerImageName(out var imageName))
        {
            dockerImage = imageName;
        }
        else
        {
            throw new InvalidOperationException($"Compute resource '{rsc.Name}' does not have Docker image information in annotations or properties.");
        }

        var saveDockerProviderBody = JsonSerializer.Serialize(new
        {
            applicationId = application.Id,
            registryUrl,
            dockerImage,
            username = registrySettings.Username,
            password = registrySettings.Password
        }, JsonOptions);

        using var saveDockerProviderResponse = await http.PostAsync("api/application.saveDockerProvider", CreateJsonContent(saveDockerProviderBody));
        saveDockerProviderResponse.EnsureSuccessStatusCode();
        logger.LogInformation("Saved docker provider for application {AppName}.", rsc.Name);

        if (publishAnnotation?.Options.ConfigureEnvironmentVariables ?? true)
        {
            await SaveApplicationEnvironmentAsync(http, application, projectName, rsc, executionContext, applicationHostsByResource, cancellationToken);
        }

        if (publishAnnotation?.Options.ConfigureMounts ?? true)
        {
            await EnsureApplicationMountsAsync(http, application, rsc);
        }

        if (publishAnnotation?.Options.CreateDomainsForExternalEndpoints ?? true)
        {
            await EnsureApplicationDomainAsync(http, application, rsc);
        }
    }

    internal async Task DeployApplicationAsync(Application application, IComputeResource rsc)
    {
        using var http = CreateHttpClient();

        if (string.IsNullOrWhiteSpace(application.Id))
        {
            throw new InvalidOperationException($"Application '{rsc.Name}' has no applicationId, so deployment cannot be triggered.");
        }

        var deployBody = JsonSerializer.Serialize(new
        {
            applicationId = application.Id,
            title = $"Aspire deployment for {rsc.Name}",
            description = $"Automated deploy for resource '{rsc.Name}' in project '{env.ApplicationName}'."
        }, JsonOptions);

        using var deployResponse = await http.PostAsync("api/application.deploy", CreateJsonContent(deployBody));
        deployResponse.EnsureSuccessStatusCode();
        logger.LogInformation("Triggered application deploy for {AppName}.", rsc.Name);
    }

    private async Task SaveApplicationEnvironmentAsync(
        HttpClient http,
        Application application,
        string projectName,
        IComputeResource resource,
        DistributedApplicationExecutionContext executionContext,
        IReadOnlyDictionary<string, string> applicationHostsByResource,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(application.Id))
        {
            throw new InvalidOperationException("Application id is required to save environment variables.");
        }

        var environmentVariables = await ResolveResourceEnvironmentAsync(resource, projectName, executionContext, applicationHostsByResource, cancellationToken);
        if (environmentVariables.Count == 0)
        {
            logger.LogInformation("No Aspire environment variables found for resource {ResourceName}.", resource.Name);
            return;
        }

        var envPayload = string.Join(
            '\n',
            environmentVariables
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => $"{kv.Key}={EscapeEnvValue(kv.Value)}"));

        var envKeys = environmentVariables.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

        logger.LogInformation("Preparing to save {Count} environment variable(s) for resource {ResourceName}. Keys: {EnvironmentKeys}", envKeys.Length, resource.Name, string.Join(", ", envKeys));
        logger.LogInformation("Environment payload size for resource {ResourceName}: {PayloadLength} characters.", resource.Name, envPayload.Length);

        var saveEnvironmentBody = JsonSerializer.Serialize(new
        {
            applicationId = application.Id,
            env = envPayload,
            createEnvFile = true,
            buildArgs = "",
            buildSecrets = ""
        }, JsonOptions);

        using var saveEnvironmentResponse = await http.PostAsync("api/application.saveEnvironment", CreateJsonContent(saveEnvironmentBody));
        saveEnvironmentResponse.EnsureSuccessStatusCode();
        logger.LogInformation("Saved {Count} environment variable(s) for application {AppName}.", environmentVariables.Count, resource.Name);
    }

    private async Task EnsureApplicationMountsAsync(HttpClient http, Application application, IComputeResource resource)
    {
        if (string.IsNullOrWhiteSpace(application.Id))
        {
            throw new InvalidOperationException("Application id is required to verify mounts.");
        }

        if (!resource.TryGetContainerMounts(out var containerMounts))
        {
            logger.LogInformation("Application {AppName} has no Aspire container mounts. Skipping mount creation.", resource.Name);
            return;
        }

        var desiredMounts = containerMounts
            .Select(mount => ToDesiredMount(resource, mount))
            .DistinctBy(GetMountIdentity, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (desiredMounts.Count == 0)
        {
            logger.LogInformation("Application {AppName} has no supported Aspire container mounts after normalization. Skipping mount creation.", resource.Name);
            return;
        }

        using var existingMountsResponse = await http.GetAsync($"api/mounts.allNamedByApplicationId?applicationId={Uri.EscapeDataString(application.Id)}");
        existingMountsResponse.EnsureSuccessStatusCode();

        var existingMounts = await DokployResponseReaders.ReadMountsFromResponseAsync(existingMountsResponse, logger);
        var unmatchedExistingMounts = new List<Mount>(existingMounts);

        foreach (var desiredMount in desiredMounts)
        {
            var exactMatch = unmatchedExistingMounts.FirstOrDefault(existing => MountIdentityMatches(existing, desiredMount));
            if (exactMatch is not null)
            {
                unmatchedExistingMounts.Remove(exactMatch);
                logger.LogInformation("Mount {MountPath} for application {AppName} already exists as {MountType}.", desiredMount.MountPath, resource.Name, desiredMount.Type);
                continue;
            }

            var targetMatch = unmatchedExistingMounts.FirstOrDefault(existing => MountLocationMatches(existing, desiredMount));
            if (targetMatch is not null)
            {
                unmatchedExistingMounts.Remove(targetMatch);

                if (string.IsNullOrWhiteSpace(targetMatch.Id))
                {
                    throw new InvalidOperationException($"Mount '{targetMatch.MountPath}' exists for application '{resource.Name}' but no mountId was returned.");
                }

                var updateBody = JsonSerializer.Serialize(new
                {
                    mountId = targetMatch.Id,
                    type = desiredMount.Type,
                    hostPath = desiredMount.HostPath,
                    volumeName = desiredMount.VolumeName,
                    mountPath = desiredMount.MountPath,
                    serviceType = "application",
                    applicationId = application.Id
                }, JsonOptions);

                using var updateResponse = await http.PostAsync("api/mounts.update", CreateJsonContent(updateBody));
                updateResponse.EnsureSuccessStatusCode();

                logger.LogInformation("Updated mount {MountPath} for application {AppName} to {MountType}.", desiredMount.MountPath, resource.Name, desiredMount.Type);
                continue;
            }

            var createBody = JsonSerializer.Serialize(new
            {
                type = desiredMount.Type,
                hostPath = desiredMount.HostPath,
                volumeName = desiredMount.VolumeName,
                mountPath = desiredMount.MountPath,
                serviceType = "application",
                serviceId = application.Id
            }, JsonOptions);

            using var createResponse = await http.PostAsync("api/mounts.create", CreateJsonContent(createBody));
            createResponse.EnsureSuccessStatusCode();
            logger.LogInformation("Created mount {MountPath} for application {AppName} as {MountType}.", desiredMount.MountPath, resource.Name, desiredMount.Type);
        }

        if (unmatchedExistingMounts.Count > 0)
        {
            logger.LogInformation("Application {AppName} has {ExtraCount} extra existing Dokploy mount(s) beyond the {ExpectedCount} Aspire mount(s); leaving them unchanged.", resource.Name, unmatchedExistingMounts.Count, desiredMounts.Count);
        }
    }

    private DokployMountSpec ToDesiredMount(IComputeResource resource, ContainerMountAnnotation mount)
    {
        if (mount.IsReadOnly)
        {
            throw new InvalidOperationException($"Resource '{resource.Name}' declares read-only mount '{mount.Target}', but Dokploy mount APIs do not expose read-only semantics.");
        }

        if (string.IsNullOrWhiteSpace(mount.Target))
        {
            throw new InvalidOperationException($"Resource '{resource.Name}' has a container mount with no target path.");
        }

        return mount.Type switch
        {
            ContainerMountType.Volume when !string.IsNullOrWhiteSpace(mount.Source) => new DokployMountSpec("volume", mount.Target, null, mount.Source),
            ContainerMountType.BindMount when !string.IsNullOrWhiteSpace(mount.Source) => new DokployMountSpec("bind", mount.Target, mount.Source, null),
            ContainerMountType.Volume => throw new InvalidOperationException($"Resource '{resource.Name}' has a volume mount for '{mount.Target}' without a volume name."),
            ContainerMountType.BindMount => throw new InvalidOperationException($"Resource '{resource.Name}' has a bind mount for '{mount.Target}' without a host path."),
            _ => throw new InvalidOperationException($"Resource '{resource.Name}' uses unsupported container mount type '{mount.Type}'.")
        };
    }

    private static string GetMountIdentity(DokployMountSpec mount)
    {
        return DokployMountReconciler.GetMountIdentity(mount.Type, mount.MountPath, mount.HostPath, mount.VolumeName);
    }

    private static bool MountIdentityMatches(Mount existingMount, DokployMountSpec desiredMount)
    {
        return DokployMountReconciler.MountIdentityMatches(existingMount, desiredMount.Type, desiredMount.MountPath, desiredMount.HostPath, desiredMount.VolumeName);
    }

    private static bool MountLocationMatches(Mount existingMount, DokployMountSpec desiredMount)
    {
        return DokployMountReconciler.MountLocationMatches(existingMount, desiredMount.MountPath);
    }

    private async Task<Dictionary<string, string>> ResolveResourceEnvironmentAsync(
        IComputeResource resource,
        string projectName,
        DistributedApplicationExecutionContext executionContext,
        IReadOnlyDictionary<string, string> applicationHostsByResource,
        CancellationToken cancellationToken)
    {
        var environmentVariables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (resource.TryGetAnnotationsOfType<EnvironmentCallbackAnnotation>(out var environmentCallbacks))
        {
            var callbackContext = new EnvironmentCallbackContext(executionContext, resource, environmentVariables, cancellationToken: cancellationToken);

            foreach (var callback in environmentCallbacks)
            {
                await callback.Callback(callbackContext).ConfigureAwait(false);
            }
        }

        var materialized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in environmentVariables)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }

            var value = await MaterializeEnvironmentValueAsync(kv.Value, projectName, applicationHostsByResource, cancellationToken).ConfigureAwait(false);
            materialized[kv.Key] = value;
        }

        var normalized = NormalizeDokployEnvironmentVariables(materialized, applicationHostsByResource);

        if (normalized.Count != materialized.Count)
        {
            logger.LogInformation(
                "Removed {RemovedCount} invalid internal HTTPS environment variable(s) for resource {ResourceName} because Dokploy service-to-service traffic is published as HTTP.",
                materialized.Count - normalized.Count,
                resource.Name);
        }

        return normalized;
    }

    internal static Dictionary<string, string> NormalizeDokployEnvironmentVariables(
        IReadOnlyDictionary<string, string> environmentVariables,
        IReadOnlyDictionary<string, string> applicationHostsByResource)
    {
        var internalHosts = applicationHostsByResource.Values
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Select(host => host.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (internalHosts.Count == 0)
        {
            return new Dictionary<string, string>(environmentVariables, StringComparer.OrdinalIgnoreCase);
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var environmentVariable in environmentVariables)
        {
            if (ShouldSuppressInternalHttpsEnvironmentVariable(environmentVariable.Key, environmentVariable.Value, internalHosts))
            {
                continue;
            }

            normalized[environmentVariable.Key] = environmentVariable.Value;
        }

        return normalized;
    }

    private static bool ShouldSuppressInternalHttpsEnvironmentVariable(string key, string value, HashSet<string> internalHosts)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !internalHosts.Contains(uri.Host))
        {
            return false;
        }

        return key.Contains("__https__", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("_HTTPS", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> MaterializeEnvironmentValueAsync(
        object? value,
        string projectName,
        IReadOnlyDictionary<string, string> applicationHostsByResource,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            switch (value)
            {
                case null:
                    return string.Empty;
                case string s:
                    return s;
                case bool b:
                    return b ? "true" : "false";
                case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                case EndpointReference endpointReference:
                    return ResolveEndpointValue(endpointReference, EndpointProperty.Url, projectName, applicationHostsByResource);
                case EndpointReferenceExpression endpointReferenceExpression:
                    return ResolveEndpointValue(endpointReferenceExpression.Endpoint, endpointReferenceExpression.Property, projectName, applicationHostsByResource);
                case ParameterResource parameter:
                    return await parameter.GetValueAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
                case ConnectionStringReference connectionStringReference:
                    value = connectionStringReference.Resource.ConnectionStringExpression;
                    continue;
                case IResourceWithConnectionString resourceWithConnectionString:
                    value = resourceWithConnectionString.ConnectionStringExpression;
                    continue;
                case ReferenceExpression referenceExpression:
                    return await FormatReferenceExpressionAsync(referenceExpression, projectName, applicationHostsByResource, cancellationToken).ConfigureAwait(false);
                case IValueProvider valueProvider:
                    return await valueProvider.GetValueAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
                case IManifestExpressionProvider manifestExpressionProvider:
                    return manifestExpressionProvider.ValueExpression;
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }
    }

    private async Task<string> FormatReferenceExpressionAsync(
        ReferenceExpression expression,
        string projectName,
        IReadOnlyDictionary<string, string> applicationHostsByResource,
        CancellationToken cancellationToken)
    {
        if (expression is { Format: "{0}", ValueProviders.Count: 1 })
        {
            return await MaterializeEnvironmentValueAsync(expression.ValueProviders[0], projectName, applicationHostsByResource, cancellationToken).ConfigureAwait(false);
        }

        var args = new object[expression.ValueProviders.Count];
        for (var i = 0; i < expression.ValueProviders.Count; i++)
        {
            args[i] = await MaterializeEnvironmentValueAsync(expression.ValueProviders[i], projectName, applicationHostsByResource, cancellationToken).ConfigureAwait(false);
        }

        return string.Format(CultureInfo.InvariantCulture, expression.Format, args);
    }

    private static string ResolveEndpointValue(
        EndpointReference endpointReference,
        EndpointProperty property,
        string projectName,
        IReadOnlyDictionary<string, string> applicationHostsByResource)
    {
        var referencedResource = endpointReference.Resource;
        var host = GetApplicationServiceName(projectName, referencedResource?.Name ?? "unknown-service", applicationHostsByResource).ToLowerInvariant();

        if (referencedResource is null)
        {
            return property switch
            {
                EndpointProperty.Host => host,
                EndpointProperty.IPV4Host => host,
                EndpointProperty.Port => "8080",
                EndpointProperty.TargetPort => "8080",
                EndpointProperty.HostAndPort => $"{host}:8080",
                EndpointProperty.Scheme => "http",
                _ => $"http://{host}:8080"
            };
        }

        var resolved = referencedResource.ResolveEndpoints().FirstOrDefault(e => string.Equals(e.Endpoint?.Name, endpointReference.EndpointName, StringComparison.OrdinalIgnoreCase));

        var scheme = "http";
        var port = 8080;

        if (resolved?.Endpoint is { } endpoint)
        {
            scheme = endpoint.UriScheme ?? "http";
            port = resolved.TargetPort.Value ?? resolved.ExposedPort.Value ?? 8080;
        }

        return property switch
        {
            EndpointProperty.Url => $"{scheme}://{host}:{port}",
            EndpointProperty.Host => host,
            EndpointProperty.IPV4Host => host,
            EndpointProperty.Port => port.ToString(CultureInfo.InvariantCulture),
            EndpointProperty.TargetPort => port.ToString(CultureInfo.InvariantCulture),
            EndpointProperty.HostAndPort => $"{host}:{port}",
            EndpointProperty.Scheme => scheme,
            _ => $"{scheme}://{host}:{port}"
        };
    }

    private static string GetApplicationServiceName(
        string projectName,
        string resourceName,
        IReadOnlyDictionary<string, string>? applicationHostsByResource = null)
    {
        if (applicationHostsByResource is not null
            && applicationHostsByResource.TryGetValue(resourceName, out var applicationHost)
            && !string.IsNullOrWhiteSpace(applicationHost))
        {
            return applicationHost;
        }

        const string projectSuffix = "-project";
        var prefix = projectName.EndsWith(projectSuffix, StringComparison.OrdinalIgnoreCase)
            ? projectName[..^projectSuffix.Length]
            : projectName;

        return $"{prefix}-app-{resourceName}";
    }

    private static string EscapeEnvValue(string value)
    {
        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        if (normalized.Length == 0)
        {
            return normalized;
        }

        var needsQuotes = normalized.Any(char.IsWhiteSpace)
            || normalized.Contains('#', StringComparison.Ordinal)
            || normalized.Contains('"', StringComparison.Ordinal)
            || normalized.Contains('=', StringComparison.Ordinal);

        if (!needsQuotes)
        {
            return normalized;
        }

        return $"\"{normalized.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private async Task EnsureApplicationDomainAsync(HttpClient http, Application application, IComputeResource resource)
    {
        if (string.IsNullOrWhiteSpace(application.Id))
        {
            throw new InvalidOperationException("Application id is required to verify domains.");
        }

        var externalEndpoints = GetExternalEndpoints(resource);
        if (externalEndpoints.Count == 0)
        {
            logger.LogInformation("Application {AppName} has no external Aspire endpoints. Skipping domain creation.", application.AppName);
            return;
        }

        using var byAppResponse = await http.GetAsync($"api/domain.byApplicationId?applicationId={Uri.EscapeDataString(application.Id)}");
        byAppResponse.EnsureSuccessStatusCode();

        var existingDomains = await DokployResponseReaders.ReadDomainsFromResponseAsync(byAppResponse, logger, "domain.byApplicationId");
        var appNameForDomain = string.IsNullOrWhiteSpace(application.AppName) ? application.Name : application.AppName;
        if (string.IsNullOrWhiteSpace(appNameForDomain))
        {
            throw new InvalidOperationException("Application name is required to generate a domain.");
        }

        var generateBody = JsonSerializer.Serialize(new { appName = appNameForDomain }, JsonOptions);

        using var generateResponse = await http.PostAsync("api/domain.generateDomain", CreateJsonContent(generateBody));
        generateResponse.EnsureSuccessStatusCode();

        var generatedHost = await DokployResponseReaders.ReadGeneratedHostFromResponseAsync(generateResponse, logger)
            ?? throw new InvalidOperationException($"Could not parse generated domain host for application '{appNameForDomain}'.");

        var unmatchedDomains = new List<Domain>(existingDomains);

        foreach (var endpoint in externalEndpoints)
        {
            var endpointUrl = BuildApplicationEndpointUrl(generatedHost, endpoint);
            var existingDomain = unmatchedDomains
                .FirstOrDefault(d => string.Equals(d.Host, generatedHost, StringComparison.OrdinalIgnoreCase) && d.Port == endpoint.Port)
                ?? unmatchedDomains.FirstOrDefault(d => d.Port == endpoint.Port)
                ?? unmatchedDomains.FirstOrDefault();

            if (existingDomain is not null)
            {
                unmatchedDomains.Remove(existingDomain);
                if (string.IsNullOrWhiteSpace(existingDomain.Id))
                {
                    throw new InvalidOperationException($"Application domain '{existingDomain.Host}' exists for application '{appNameForDomain}' but no domainId was returned.");
                }

                var updateBody = JsonSerializer.Serialize(new
                {
                    domainId = existingDomain.Id,
                    host = generatedHost,
                    port = endpoint.Port,
                    https = endpoint.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase),
                    domainType = "application"
                }, JsonOptions);

                using var updateResponse = await http.PostAsync("api/domain.update", CreateJsonContent(updateBody));
                updateResponse.EnsureSuccessStatusCode();

                logger.LogInformation("Updated deployed application URL {EndpointUrl} for application {AppName} using endpoint {EndpointName}.", endpointUrl, appNameForDomain, endpoint.Name);
                continue;
            }

            var createBody = JsonSerializer.Serialize(new
            {
                applicationId = application.Id,
                host = generatedHost,
                port = endpoint.Port,
                https = endpoint.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase),
                domainType = "application"
            }, JsonOptions);

            using var createResponse = await http.PostAsync("api/domain.create", CreateJsonContent(createBody));
            createResponse.EnsureSuccessStatusCode();

            logger.LogInformation("Created deployed application URL {EndpointUrl} for application {AppName} using endpoint {EndpointName}.", endpointUrl, appNameForDomain, endpoint.Name);
        }

        if (unmatchedDomains.Count > 0)
        {
            logger.LogInformation("Application {AppName} has {ExtraCount} extra existing domain(s) beyond the {ExpectedCount} external Aspire endpoint(s); leaving them unchanged.", appNameForDomain, unmatchedDomains.Count, externalEndpoints.Count);
        }
    }

    private static string BuildApplicationEndpointUrl(string host, ExternalEndpointConfig endpoint)
    {
        return $"{endpoint.Scheme}://{host}:{endpoint.Port}";
    }

    private static List<ExternalEndpointConfig> GetExternalEndpoints(IComputeResource resource)
    {
        return resource.ResolveEndpoints()
            .Where(e => e.Endpoint.IsExternal)
            .Select(e => new ExternalEndpointConfig(
                e.Endpoint.Name,
                e.Endpoint.UriScheme ?? "http",
                e.TargetPort.Value ?? e.ExposedPort.Value ?? 8080))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Scheme, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Port)
            .DistinctBy(e => $"{e.Name}|{e.Scheme}|{e.Port}")
            .ToList();
    }

    private sealed record ExternalEndpointConfig(string Name, string Scheme, int Port);
    private sealed record DokployMountSpec(string Type, string MountPath, string? HostPath, string? VolumeName);
}