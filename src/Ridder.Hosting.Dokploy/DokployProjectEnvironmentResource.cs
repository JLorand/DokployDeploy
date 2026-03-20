using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Publishing;
using Microsoft.Extensions.DependencyInjection;

namespace Ridder.Hosting.Dokploy;

public class DokployProjectEnvironmentResource : Resource, IContainerRegistry
{
    private readonly string _name;
    private readonly ParameterResource? _apiKeyParameter;
    private readonly ParameterResource? _apiUrlParameter;
    private DokployRegistrySettings? _registrySettings;

    internal DokployProjectEnvironmentResource(string name, ParameterResource? apiKey, ParameterResource? apiUrlParameter) : base(name)
    {
        _name = name;
        _apiKeyParameter = apiKey;
        _apiUrlParameter = apiUrlParameter;

        Annotations.Add(new ManifestPublishingCallbackAnnotation(context => DokployManifestPublishing.WriteEnvironmentManifest(context, this)));

#pragma warning disable ASPIREPIPELINES001
        Annotations.Add(new PipelineStepAnnotation(ctx =>
        {
            return
            [
                new PipelineStep()
                {
                    Name = $"prepare-registry-{name}",
                    Action = async context =>
                    {
                        var provisioner = context.Services.GetRequiredService<IDokployEnvironmentProvisioner>();
                        await provisioner.PrepareRegistryAsync(this, context);
                    },
                    RequiredBySteps = [WellKnownPipelineSteps.Deploy],
                    DependsOnSteps = [WellKnownPipelineSteps.DeployPrereq]
                },
                new PipelineStep()
                {
                    Name = $"provision-apps-{name}",
                    Action = async context =>
                    {
                        var provisioner = context.Services.GetRequiredService<IDokployEnvironmentProvisioner>();
                        await provisioner.ProvisionApplicationsAsync(this, context);
                    },
                    DependsOnSteps = [WellKnownPipelineSteps.Push, $"prepare-registry-{name}"],
                    RequiredBySteps = [WellKnownPipelineSteps.Deploy]
                }
            ];
        }));
#pragma warning restore ASPIREPIPELINES001
    }

    private string? ContainerRegistryUrl { get; set; }

    internal DokployRegistryMode? RegistryMode => _registrySettings?.Mode;
    internal string? RegistryType => _registrySettings?.RegistryType;
    internal string? ApiUrlParameterName => _apiUrlParameter?.Name;
    internal string? ApiKeyParameterName => _apiKeyParameter?.Name;
    internal bool HasRegistryConfiguration => _registrySettings is not null;

    public ReferenceExpression Endpoint => ReferenceExpression.Create($"{ContainerRegistryUrl}");

    ReferenceExpression IContainerRegistry.Name => ReferenceExpression.Create($"{_name}-registry");

    internal void ConfigureRegistry(DokployRegistrySettings registrySettings)
    {
        _registrySettings = registrySettings ?? throw new ArgumentNullException(nameof(registrySettings));
    }

    internal string GetProjectName() => $"{_name}-project";

    internal void SetContainerRegistryUrl(string? containerRegistryUrl)
    {
        ContainerRegistryUrl = containerRegistryUrl;
    }

    internal async Task<DokployResolvedRegistrySettings> ResolveRegistrySettingsAsync(CancellationToken cancellationToken)
    {
        if (_registrySettings is null)
        {
            throw new InvalidOperationException($"Dokploy environment '{_name}' does not have a registry configuration. Call WithHostedRegistry(...) or WithSelfHostedRegistry(...).");
        }

        return await _registrySettings.ResolveAsync(cancellationToken);
    }

    internal async Task<string> ResolveApiUrlAsync(CancellationToken cancellationToken)
    {
        var apiUrl = _apiUrlParameter is null
            ? null
            : await _apiUrlParameter.GetValueAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            throw new InvalidOperationException($"Dokploy API URL for project {_name} is not set.");
        }

        return apiUrl;
    }

    internal async Task<string> ResolveApiKeyAsync(CancellationToken cancellationToken)
    {
        var apiKey = _apiKeyParameter is null
            ? null
            : await _apiKeyParameter.GetValueAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"API key for project {_name} is not set.");
        }

        return apiKey;
    }
}