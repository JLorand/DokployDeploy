using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;

namespace Ridder.Hosting.Dokploy.Tests;

public class DokployPublishTests
{
    [Fact]
    public void PublishToDokploy_AddsPublishAndRegistryAnnotations()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var dokploy = builder.AddDokployEnvironment("dokploy")
            .WithHostedRegistry();
        var api = builder.AddContainer("api", "nginx:latest");

        api.PublishToDokploy(dokploy, options =>
        {
            options.ApplicationName = "custom-api";
            options.ConfigureMounts = false;
        });

        var publishAnnotation = Assert.Single(api.Resource.Annotations.OfType<DokployPublishApplicationAnnotation>());
        Assert.Equal("custom-api", publishAnnotation.ResolveApplicationName(api.Resource.Name));
        Assert.False(publishAnnotation.Options.ConfigureMounts);

        #pragma warning disable ASPIRECOMPUTE003
        var registryAnnotation = Assert.Single(api.Resource.Annotations.OfType<RegistryTargetAnnotation>());
        #pragma warning restore ASPIRECOMPUTE003
        Assert.Same(dokploy.Resource, registryAnnotation.Registry);
    }

    [Fact]
    public async Task PublishToDokploy_ManifestCallbackWritesDokployMetadata()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var dokploy = builder.AddDokployEnvironment("dokploy")
            .WithHostedRegistry();
        var api = builder.AddContainer("api", "nginx:latest");

        api.Resource.Annotations.Add(new ContainerMountAnnotation("api-data", "/data", ContainerMountType.Volume, isReadOnly: false));

        api.PublishToDokploy(dokploy, options =>
        {
            options.ApplicationName = "custom-api";
            options.ConfigureEnvironmentVariables = false;
        });

        var manifestAnnotation = api.Resource.Annotations.OfType<ManifestPublishingCallbackAnnotation>().Last();

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();

        var context = new ManifestPublishingContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
            "manifest.json",
            writer,
            CancellationToken.None);

        var callback = manifestAnnotation.Callback;
        Assert.NotNull(callback);
        await callback!(context);

        writer.WriteEndObject();
        await writer.FlushAsync();

        var json = Encoding.UTF8.GetString(stream.ToArray());
        using var document = JsonDocument.Parse(json);

        var dokployNode = document.RootElement.GetProperty("dokploy");
        Assert.Equal("application.v0", dokployNode.GetProperty("type").GetString());
        Assert.Equal("dokploy", dokployNode.GetProperty("environment").GetString());
        Assert.Equal("custom-api", dokployNode.GetProperty("applicationName").GetString());
        Assert.False(dokployNode.GetProperty("configureEnvironmentVariables").GetBoolean());

        var mounts = dokployNode.GetProperty("mounts").EnumerateArray().ToArray();
        var mount = Assert.Single(mounts);
        Assert.Equal("/data", mount.GetProperty("target").GetString());
    }

    [Fact]
    public async Task DokployEnvironment_ManifestReflectsRegistrySelection()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var dokploy = builder.AddDokployEnvironment("dokploy")
            .WithSelfHostedRegistry("registry.example.com");

        var manifestAnnotation = dokploy.Resource.Annotations.OfType<ManifestPublishingCallbackAnnotation>().Last();

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();

        var context = new ManifestPublishingContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
            "manifest.json",
            writer,
            CancellationToken.None);

        var callback = manifestAnnotation.Callback;
        Assert.NotNull(callback);
        await callback!(context);

        writer.WriteEndObject();
        await writer.FlushAsync();

        using var document = JsonDocument.Parse(stream.ToArray());
        var dokployNode = document.RootElement.GetProperty("dokploy");

        Assert.True(dokployNode.GetProperty("hasRegistryConfiguration").GetBoolean());
        Assert.Equal("selfhosted", dokployNode.GetProperty("registryMode").GetString());
    }

    [Fact]
    public void WithHostedRegistry_AddsHostedRegistryAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var dokploy = builder.AddDokployEnvironment("dokploy")
            .WithHostedRegistry("ghcr.io", "octocat", "token");

        var annotation = Assert.Single(dokploy.Resource.Annotations.OfType<DokployHostedRegistryAnnotation>());

        Assert.Equal(DokployRegistryMode.Hosted, annotation.Mode);
        Assert.Equal("cloud", annotation.RegistryType);
    }

    [Fact]
    public void WithSelfHostedRegistry_ReplacesPreviousRegistryAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var dokploy = builder.AddDokployEnvironment("dokploy")
            .WithHostedRegistry("ghcr.io", "octocat", "token")
            .WithSelfHostedRegistry("registry.example.com");

        Assert.Empty(dokploy.Resource.Annotations.OfType<DokployHostedRegistryAnnotation>());

        var annotation = Assert.Single(dokploy.Resource.Annotations.OfType<DokploySelfHostedRegistryAnnotation>());
        Assert.Equal(DokployRegistryMode.SelfHosted, annotation.Mode);
    }

    [Fact]
    public void AddDokployEnvironment_AddsConnectionAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var dokploy = builder.AddDokployEnvironment("dokploy");

        var annotation = Assert.Single(dokploy.Resource.Annotations.OfType<DokployEnvironmentConnectionAnnotation>());
        Assert.Null(annotation.ApiKeyParameter);
        Assert.Null(annotation.ApiUrlParameter);
    }

    [Fact]
    public void NormalizeDokployEnvironmentVariables_RemovesInvalidInternalHttpsEndpoints()
    {
        var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["APISERVICE_HTTP"] = "http://dokploydeploy-app-apiservice-dvbjux:8080",
            ["APISERVICE_HTTPS"] = "https://dokploydeploy-app-apiservice-dvbjux:8080",
            ["services__apiservice__http__0"] = "http://dokploydeploy-app-apiservice-dvbjux:8080",
            ["services__apiservice__https__0"] = "https://dokploydeploy-app-apiservice-dvbjux:8080",
            ["services__publicapi__https__0"] = "https://api.example.com"
        };

        var applicationHostsByResource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["apiservice"] = "dokploydeploy-app-apiservice-dvbjux"
        };

        var normalized = DokployApi.NormalizeDokployEnvironmentVariables(environmentVariables, applicationHostsByResource);

        Assert.Equal("http://dokploydeploy-app-apiservice-dvbjux:8080", normalized["APISERVICE_HTTP"]);
        Assert.Equal("http://dokploydeploy-app-apiservice-dvbjux:8080", normalized["services__apiservice__http__0"]);
        Assert.DoesNotContain("APISERVICE_HTTPS", normalized.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("services__apiservice__https__0", normalized.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("https://api.example.com", normalized["services__publicapi__https__0"]);
    }
}