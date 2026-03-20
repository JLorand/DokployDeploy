using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Publishing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ridder.Hosting.Dokploy;

public static class DokployExtensions
{
    private const string DefaultRegistryUsername = "docker";
    private const string DefaultRegistryPassword = "password";

    public static IResourceBuilder<DokployProjectEnvironmentResource> AddDokployEnvironment(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        builder.Services.TryAddSingleton<IDokployEnvironmentProvisioner, DokployEnvironmentProvisioner>();

        IResourceBuilder<DokployProjectEnvironmentResource> resourceBuilder;

        if (builder.ExecutionContext.IsRunMode)
        {
            resourceBuilder = builder.CreateResourceBuilder(new DokployProjectEnvironmentResource(name, null, null));
        }
        else
        {
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

            resourceBuilder = builder.AddResource(new DokployProjectEnvironmentResource(name, apiKey.Resource, apiUrl));
        }

        builder.Eventing.Subscribe<BeforeStartEvent>(async (beforeStart, cancellationToken) =>
        {
            var computeResources = beforeStart.Model.GetComputeResources().OfType<IComputeResource>().GetDokployComputeResources(resourceBuilder.Resource);

            foreach (var resource in computeResources)
            {
#pragma warning disable ASPIRECOMPUTE003
                if (resource.TryGetAnnotationsOfType<RegistryTargetAnnotation>(out var annotations)
                    && annotations.Any(annotation => ReferenceEquals(annotation.Registry, resourceBuilder.Resource)))
                {
                    continue;
                }

                resource.Annotations.Add(new RegistryTargetAnnotation(resourceBuilder.Resource));
#pragma warning restore ASPIRECOMPUTE003
            }

            await Task.CompletedTask;
        });

        return resourceBuilder;
    }

    public static IResourceBuilder<DokployProjectEnvironmentResource> WithSelfHostedRegistry(this IResourceBuilder<DokployProjectEnvironmentResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            builder.Resource.ConfigureRegistry(DokployRegistrySettings.CreateSelfHosted(string.Empty, DefaultRegistryUsername, DefaultRegistryPassword));
            return builder;
        }

        var registryDomainParameter = builder.ApplicationBuilder.AddParameter($"{builder.Resource.Name}-registry-domain-url").Resource;
        builder.Resource.ConfigureRegistry(DokployRegistrySettings.CreateSelfHosted(registryDomainParameter, DefaultRegistryUsername, DefaultRegistryPassword));
        return builder;
    }

    public static IResourceBuilder<DokployProjectEnvironmentResource> WithSelfHostedRegistry(this IResourceBuilder<DokployProjectEnvironmentResource> builder, string registryDomainUrl)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(registryDomainUrl))
        {
            throw new ArgumentException("A registry domain URL is required for a self-hosted Dokploy registry.", nameof(registryDomainUrl));
        }

        builder.Resource.ConfigureRegistry(DokployRegistrySettings.CreateSelfHosted(registryDomainUrl, DefaultRegistryUsername, DefaultRegistryPassword));
        return builder;
    }

#pragma warning disable ASPIREINTERACTION001
    public static IResourceBuilder<DokployProjectEnvironmentResource> WithHostedRegistry(this IResourceBuilder<DokployProjectEnvironmentResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            builder.Resource.ConfigureRegistry(DokployRegistrySettings.CreateHosted(string.Empty, string.Empty, string.Empty));
            return builder;
        }

        var registryUrlParameter = builder.ApplicationBuilder.AddParameter($"{builder.Resource.Name}-registry-url").Resource;
        var registryUsernameParameter = builder.ApplicationBuilder.AddParameter($"{builder.Resource.Name}-registry-username").Resource;
        var registryPasswordParameter = builder.ApplicationBuilder.AddParameter($"{builder.Resource.Name}-registry-password", secret: true).WithCustomInput(ctx => new()
        {
            InputType = InputType.SecretText,
            Name = "Registry Password",
            Required = true,
            Placeholder = "CoolPassword123"
        }).Resource;

        builder.Resource.ConfigureRegistry(DokployRegistrySettings.CreateHosted(registryUrlParameter, registryUsernameParameter, registryPasswordParameter));
        return builder;
    }
#pragma warning restore ASPIREINTERACTION001

    public static IResourceBuilder<DokployProjectEnvironmentResource> WithHostedRegistry(this IResourceBuilder<DokployProjectEnvironmentResource> builder, string registryUrl, string username, string password)
    {
        ArgumentNullException.ThrowIfNull(builder);

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

        builder.Resource.ConfigureRegistry(DokployRegistrySettings.CreateHosted(registryUrl, username, password));
        return builder;
    }

    public static IResourceBuilder<T> PublishToDokploy<T>(this IResourceBuilder<T> builder, IResourceBuilder<DokployProjectEnvironmentResource> environmentBuilder)
        where T : IResource, IComputeResource
    {
        return PublishToDokploy(builder, environmentBuilder, static _ => { });
    }

    public static IResourceBuilder<T> PublishToDokploy<T>(this IResourceBuilder<T> builder, IResourceBuilder<DokployProjectEnvironmentResource> environmentBuilder, Action<DokployApplicationOptions> configure)
        where T : IResource, IComputeResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(environmentBuilder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new DokployApplicationOptions();
        configure(options);

        builder.WithAnnotation(new DokployPublishApplicationAnnotation(environmentBuilder.Resource, options), ResourceAnnotationMutationBehavior.Replace);

#pragma warning disable ASPIRECOMPUTE003
    builder.WithAnnotation(new RegistryTargetAnnotation(environmentBuilder.Resource), ResourceAnnotationMutationBehavior.Replace);
#pragma warning restore ASPIRECOMPUTE003

        if (!builder.Resource.TryGetAnnotationsOfType<DokployManifestAnnotation>(out _))
        {
            builder.WithAnnotation(new DokployManifestAnnotation());
            builder.WithManifestPublishingCallback(context => DokployManifestPublishing.WriteApplicationManifest(context, builder.Resource));
        }

        return builder;
    }

    public static IResourceBuilder<DokployProjectEnvironmentResource> AddDokployProject(this IDistributedApplicationBuilder builder, string name)
    {
        return builder.AddDokployEnvironment(name).WithSelfHostedRegistry();
    }

    public static IResourceBuilder<DokployProjectEnvironmentResource> AddDokployProjectSelfHostedRegistry(this IDistributedApplicationBuilder builder, string name)
    {
        return builder.AddDokployEnvironment(name).WithSelfHostedRegistry();
    }

    public static IResourceBuilder<DokployProjectEnvironmentResource> AddDokployProjectSelfHostedRegistry(this IDistributedApplicationBuilder builder, string name, string registryDomainUrl)
    {
        if (string.IsNullOrWhiteSpace(registryDomainUrl))
        {
            throw new ArgumentException("A registry domain URL is required for a self-hosted Dokploy registry.", nameof(registryDomainUrl));
        }

        return builder.AddDokployEnvironment(name).WithSelfHostedRegistry(registryDomainUrl);
    }

#pragma warning disable ASPIREINTERACTION001
    public static IResourceBuilder<DokployProjectEnvironmentResource> AddDokployProjectHostedRegistry(this IDistributedApplicationBuilder builder, string name)
    {
        return builder.AddDokployEnvironment(name).WithHostedRegistry();
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

        return builder.AddDokployEnvironment(name).WithHostedRegistry(registryUrl, username, password);
    }
}