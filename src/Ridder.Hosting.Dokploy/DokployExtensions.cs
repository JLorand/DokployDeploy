using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Publishing;
using Microsoft.AspNetCore.Builder;

namespace Ridder.Hosting.Dokploy;

public static class DokployExtensions
{
    private const string DefaultRegistryUsername = "docker";
    private const string DefaultRegistryPassword = "password";

    public static IResourceBuilder<DokployProjectEnvironmentResource> AddDokployProject(this IDistributedApplicationBuilder builder, string name)
    {
        return builder.AddDokployProjectSelfHostedRegistry(name);
    }

    public static IResourceBuilder<DokployProjectEnvironmentResource> AddDokployProjectSelfHostedRegistry(this IDistributedApplicationBuilder builder, string name)
    {
        if (builder.ExecutionContext.IsRunMode)
        {
            return AddDokployProjectCore(builder, name, DokployRegistrySettings.CreateSelfHosted(string.Empty, DefaultRegistryUsername, DefaultRegistryPassword));
        }

        var registryDomainParameter = builder.AddParameter($"{name}-registry-domain-url").Resource;
        return AddDokployProjectCore(builder, name, DokployRegistrySettings.CreateSelfHosted(registryDomainParameter, DefaultRegistryUsername, DefaultRegistryPassword));
    }

    public static IResourceBuilder<DokployProjectEnvironmentResource> AddDokployProjectSelfHostedRegistry(this IDistributedApplicationBuilder builder, string name, string registryDomainUrl)
    {
        if (string.IsNullOrWhiteSpace(registryDomainUrl))
        {
            throw new ArgumentException("A registry domain URL is required for a self-hosted Dokploy registry.", nameof(registryDomainUrl));
        }

        return AddDokployProjectCore(builder, name, DokployRegistrySettings.CreateSelfHosted(registryDomainUrl, DefaultRegistryUsername, DefaultRegistryPassword));
    }

#pragma warning disable ASPIREINTERACTION001
    public static IResourceBuilder<DokployProjectEnvironmentResource> AddDokployProjectHostedRegistry(this IDistributedApplicationBuilder builder, string name)
    {
        if (builder.ExecutionContext.IsRunMode)
        {
            return AddDokployProjectCore(builder, name, DokployRegistrySettings.CreateHosted(string.Empty, string.Empty, string.Empty));
        }

        var registryUrlParameter = builder.AddParameter($"{name}-registry-url").Resource;
        var registryUsernameParameter = builder.AddParameter($"{name}-registry-username").Resource;
        var registryPasswordParameter = builder.AddParameter($"{name}-registry-password", secret: true).WithCustomInput(ctx => new()
        {
            InputType = InputType.SecretText,
            Name = "Registry Password",
            Required = true,
            Placeholder = "CoolPassword123"
        }).Resource;

        return AddDokployProjectCore(builder, name, DokployRegistrySettings.CreateHosted(registryUrlParameter, registryUsernameParameter, registryPasswordParameter));
    }
#pragma warning restore ASPIREINTERACTION001

    public static IResourceBuilder<DokployProjectEnvironmentResource> AddDokployProjectHostedRegistry(this IDistributedApplicationBuilder builder, string name, string registryUrl, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(registryUrl))
        {
            throw new ArgumentException("A registry URL is required for a hosted registry.", nameof(registryUrl));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("A registry username is required for a hosted registry.", nameof(username));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("A registry password is required for a hosted registry.", nameof(password));
        }

        return AddDokployProjectCore(builder, name, DokployRegistrySettings.CreateHosted(registryUrl, username, password));
    }

    private static IResourceBuilder<DokployProjectEnvironmentResource> AddDokployProjectCore(this IDistributedApplicationBuilder builder, string name, DokployRegistrySettings registrySettings)
    {
        if (builder.ExecutionContext.IsRunMode)
        {
            return builder.CreateResourceBuilder(new DokployProjectEnvironmentResource(name, null, null, registrySettings));
        }

        var apiUrl = builder.AddParameter($"{name}-api-url").Resource;

#pragma warning disable ASPIREINTERACTION001
        var apiKey = builder.AddParameter($"{name}-apiKey", secret: true)
            .WithCustomInput(ctx => new()
            {
                InputType = InputType.SecretText,
                Name = $"API Key for {name}",
                Required = true,
                Placeholder = "CoolApiKey123"
            });
#pragma warning restore ASPIREINTERACTION001

        var resourceBuilder = builder.AddResource(new DokployProjectEnvironmentResource(name, apiKey.Resource, apiUrl, registrySettings));

        builder.Eventing.Subscribe<BeforeStartEvent>(async (beforeStart, cancellationToken) =>
        {
            foreach (var resource in beforeStart.Model.GetComputeResources())
            {
#pragma warning disable ASPIRECOMPUTE003
                resource.Annotations.Add(new RegistryTargetAnnotation(resourceBuilder.Resource));
#pragma warning restore ASPIRECOMPUTE003
            }

            await Task.CompletedTask;
        });

        return resourceBuilder;
    }
}