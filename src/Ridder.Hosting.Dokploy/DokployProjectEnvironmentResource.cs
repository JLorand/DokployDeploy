using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ridder.Hosting.Dokploy;

public class DokployProjectEnvironmentResource : Resource, IContainerRegistry
{
    private readonly string _name;
    private readonly ParameterResource? _apiKeyParameter;
    private readonly ParameterResource? _apiUrlParameter;
    private readonly DokployRegistrySettings _registrySettings;

    internal DokployProjectEnvironmentResource(string name, ParameterResource? apiKey, ParameterResource? apiUrlParameter, DokployRegistrySettings registrySettings) : base(name)
    {
        _name = name;
        _apiKeyParameter = apiKey;
        _apiUrlParameter = apiUrlParameter;
        _registrySettings = registrySettings;

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
                        var apiKeyValue = await ResolveApiKeyAsync(context.CancellationToken);
                        var apiUrlValue = await ResolveApiUrlAsync(context.CancellationToken);
                        var resolvedRegistrySettings = await _registrySettings.ResolveAsync(context.CancellationToken);
                        context.Logger.LogInformation("Deploying project {ProjectName} with API key {ApiKey}", name, apiKeyValue.Substring(0, 4) + "****" + apiKeyValue.Substring(apiKeyValue.Length - 4));

                        var api = new DokployApi(apiKeyValue, apiUrlValue, context.Services.GetRequiredService<IHostEnvironment>(), context.Logger, resolvedRegistrySettings);
                        var projectName = $"{name}-project";
                        var project = await api.GetProjectOrCreateAsync(projectName);
                        context.Logger.LogInformation("Project {ProjectName} exists.", project.Name);

                        var registry = await api.GetOrCreateRegistryAsync(project);
                        ContainerRegistryUrl = registry.RegistryUrl;
                        context.Logger.LogInformation("Registry for project {ProjectName} is ready.", project.Name);

#pragma warning disable ASPIRECONTAINERRUNTIME001
                        var containerRuntime = context.Services.GetRequiredService<IContainerRuntime>();
#pragma warning restore ASPIRECONTAINERRUNTIME001
                        await containerRuntime.LoginToRegistryAsync(
                            registry.RegistryUrl,
                            registry.Username ?? resolvedRegistrySettings.Username,
                            registry.Password ?? resolvedRegistrySettings.Password,
                            context.CancellationToken);
                    },
                    RequiredBySteps = [WellKnownPipelineSteps.Deploy],
                    DependsOnSteps = [WellKnownPipelineSteps.DeployPrereq]
                },
                new PipelineStep()
                {
                    Name = $"provision-apps-{name}",
                    Action = async context =>
                    {
                        var apiKeyValue = await ResolveApiKeyAsync(context.CancellationToken);
                        var apiUrlValue = await ResolveApiUrlAsync(context.CancellationToken);
                        var resolvedRegistrySettings = await _registrySettings.ResolveAsync(context.CancellationToken);
                        var api = new DokployApi(apiKeyValue, apiUrlValue, context.Services.GetRequiredService<IHostEnvironment>(), context.Logger, resolvedRegistrySettings);
                        var resources = context.Model.GetComputeResources();

                        var applications = new List<(IComputeResource Resource, DokployApi.Application Application)>();

                        foreach (var resource in resources)
                        {
                            var computeResource = (IComputeResource)resource;
                            var application = await api.GetOrCreateApplication($"{name}-app-{resource.Name}", $"{name}-project");
                            applications.Add((computeResource, application));
                        }

                        var applicationHostsByResource = applications.ToDictionary(
                            app => app.Resource.Name,
                            app => string.IsNullOrWhiteSpace(app.Application.AppName) ? app.Application.Name : app.Application.AppName,
                            StringComparer.OrdinalIgnoreCase);

                        foreach (var app in applications)
                        {
                            await api.ConfigureApplicationAsync(app.Application, $"{name}-project", app.Resource, context.ExecutionContext, applicationHostsByResource, context.CancellationToken);
                        }

                        foreach (var app in applications)
                        {
                            await api.DeployApplicationAsync(app.Application, app.Resource);
                        }
                    },
                    DependsOnSteps = [WellKnownPipelineSteps.Push, $"prepare-registry-{name}"],
                    RequiredBySteps = [WellKnownPipelineSteps.Deploy]
                }
            ];
        }));
#pragma warning restore ASPIREPIPELINES001
    }

    private string? ContainerRegistryUrl { get; set; }

    public ReferenceExpression Endpoint => ReferenceExpression.Create($"{ContainerRegistryUrl}");

    ReferenceExpression IContainerRegistry.Name => ReferenceExpression.Create($"{_name}-registry");

    private async Task<string> ResolveApiUrlAsync(CancellationToken cancellationToken)
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

    private async Task<string> ResolveApiKeyAsync(CancellationToken cancellationToken)
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