using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ridder.Hosting.Dokploy;

#pragma warning disable ASPIREPIPELINES001
internal interface IDokployEnvironmentProvisioner
{
    Task PrepareRegistryAsync(DokployProjectEnvironmentResource resource, PipelineStepContext context);
    Task ProvisionApplicationsAsync(DokployProjectEnvironmentResource resource, PipelineStepContext context);
}

internal sealed class DokployEnvironmentProvisioner : IDokployEnvironmentProvisioner
{
    public async Task PrepareRegistryAsync(DokployProjectEnvironmentResource resource, PipelineStepContext context)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(context);

        var apiKeyValue = await resource.ResolveApiKeyAsync(context.CancellationToken);
        var apiUrlValue = await resource.ResolveApiUrlAsync(context.CancellationToken);
        var resolvedRegistrySettings = await resource.ResolveRegistrySettingsAsync(context.CancellationToken);

        context.Logger.LogInformation(
            "Deploying project {ProjectName} with API key {ApiKey}",
            resource.Name,
            apiKeyValue.Substring(0, 4) + "****" + apiKeyValue.Substring(apiKeyValue.Length - 4));

        var api = new DokployApi(
            apiKeyValue,
            apiUrlValue,
            context.Services.GetRequiredService<IHostEnvironment>(),
            context.Logger,
            resolvedRegistrySettings);

        var projectName = resource.GetProjectName();
        var project = await api.GetProjectOrCreateAsync(projectName);
        context.Logger.LogInformation("Project {ProjectName} exists.", project.Name);

        var registry = await api.GetOrCreateRegistryAsync(project);
        resource.SetContainerRegistryUrl(string.IsNullOrWhiteSpace(registry.PushPrefix) ? registry.RegistryUrl : registry.PushPrefix);
        context.Logger.LogInformation("Registry for project {ProjectName} is ready.", project.Name);

        var resources = context.Model.GetComputeResources().OfType<IComputeResource>().GetDokployComputeResources(resource);
        if (resources.Count == 0)
        {
            context.Logger.LogInformation("No compute resources were selected for Dokploy environment {EnvironmentName}.", resource.Name);
            return;
        }

#pragma warning disable ASPIRECONTAINERRUNTIME001
        var containerRuntime = context.Services.GetRequiredService<IContainerRuntime>();
#pragma warning restore ASPIRECONTAINERRUNTIME001
        await containerRuntime.LoginToRegistryAsync(
            registry.RegistryUrl,
            registry.Username ?? resolvedRegistrySettings.Username,
            registry.Password ?? resolvedRegistrySettings.Password,
            context.CancellationToken);
    }

    public async Task ProvisionApplicationsAsync(DokployProjectEnvironmentResource resource, PipelineStepContext context)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(context);

        var apiKeyValue = await resource.ResolveApiKeyAsync(context.CancellationToken);
        var apiUrlValue = await resource.ResolveApiUrlAsync(context.CancellationToken);
        var resolvedRegistrySettings = await resource.ResolveRegistrySettingsAsync(context.CancellationToken);
        var api = new DokployApi(
            apiKeyValue,
            apiUrlValue,
            context.Services.GetRequiredService<IHostEnvironment>(),
            context.Logger,
            resolvedRegistrySettings);

        var resources = context.Model.GetComputeResources().OfType<IComputeResource>().GetDokployComputeResources(resource);
        if (resources.Count == 0)
        {
            context.Logger.LogInformation("No compute resources were selected for Dokploy environment {EnvironmentName}.", resource.Name);
            return;
        }

        var applications = new List<(IComputeResource Resource, DokployApi.Application Application)>();
        var projectName = resource.GetProjectName();

        foreach (var computeResource in resources)
        {
            var applicationName = computeResource.TryGetDokployPublishAnnotation(resource, out var annotation)
                ? annotation!.ResolveApplicationName(computeResource.Name)
                : $"{resource.Name}-app-{computeResource.Name}";

            var application = await api.GetOrCreateApplication(applicationName, projectName);
            applications.Add((computeResource, application));
        }

        var applicationHostsByResource = applications.ToDictionary(
            app => app.Resource.Name,
            app => string.IsNullOrWhiteSpace(app.Application.AppName) ? app.Application.Name : app.Application.AppName,
            StringComparer.OrdinalIgnoreCase);

        foreach (var app in applications)
        {
            await api.ConfigureApplicationAsync(app.Application, projectName, app.Resource, context.ExecutionContext, applicationHostsByResource, context.CancellationToken);
        }

        foreach (var app in applications)
        {
            await api.DeployApplicationAsync(app.Application, app.Resource);
        }
    }
}
#pragma warning restore ASPIREPIPELINES001