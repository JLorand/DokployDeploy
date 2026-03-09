using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Publishing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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

#pragma warning disable ASPIREINTERACTION001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    public static IResourceBuilder<DokployProjectEnvironmentResource> AddDokployProjectHostedRegistry(this IDistributedApplicationBuilder builder, string name)
    {
        var registryUrlParameter = builder.AddParameter($"{name}-registry-url").Resource;
        var registryUsernameParameter = builder.AddParameter($"{name}-registry-username").Resource;
        var registryPasswordParameter = builder.AddParameter($"{name}-registry-password", secret: true).WithCustomInput(ctx => new()
        {
            InputType = InputType.SecretText,
            Name = $"Registry Password",
            Required = true,
            Placeholder = "CoolPassword123"
        }).Resource;

        return AddDokployProjectCore(builder, name, DokployRegistrySettings.CreateHosted(registryUrlParameter, registryUsernameParameter, registryPasswordParameter));
    }
#pragma warning restore ASPIREINTERACTION001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

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
#pragma warning disable ASPIREINTERACTION001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        var apikey = builder.AddParameter($"{name}-apiKey", secret: true)
            .WithCustomInput(ctx => new()
            {
                InputType = InputType.SecretText,
                Name = $"API Key for {name}",
                Required = true,
                Placeholder = "CoolApiKey123"
            });
#pragma warning restore ASPIREINTERACTION001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        var x = builder.AddResource(new DokployProjectEnvironmentResource(name, apikey.Resource, apiUrl, registrySettings));

        builder.Eventing.Subscribe<BeforeStartEvent>(async (e, ct) =>
        {
            var rscs = e.Model.GetComputeResources();
            foreach (var rsc in rscs)
            {
#pragma warning disable ASPIRECOMPUTE003 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                rsc.Annotations.Add(new RegistryTargetAnnotation(x.Resource));
#pragma warning restore ASPIRECOMPUTE003 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.


            }
        });
        
        return x;
    }
}

internal enum DokployRegistryMode
{
    SelfHosted,
    Hosted
}

internal sealed class DokployRegistrySettings
{
    private DokployRegistrySettings(
        DokployRegistryMode mode,
        string? registryUrl,
        string? username,
        string? password,
        ParameterResource? registryUrlParameter,
        ParameterResource? usernameParameter,
        ParameterResource? passwordParameter,
        string registryType)
    {
        Mode = mode;
        RegistryUrl = registryUrl;
        Username = username;
        Password = password;
        RegistryUrlParameter = registryUrlParameter;
        UsernameParameter = usernameParameter;
        PasswordParameter = passwordParameter;
        RegistryType = registryType;
    }

    public DokployRegistryMode Mode { get; }
    private string? RegistryUrl { get; }
    private string? Username { get; }
    private string? Password { get; }
    private ParameterResource? RegistryUrlParameter { get; }
    private ParameterResource? UsernameParameter { get; }
    private ParameterResource? PasswordParameter { get; }
    public string RegistryType { get; }

    public static DokployRegistrySettings CreateSelfHosted(string registryUrl, string username, string password) => new(DokployRegistryMode.SelfHosted, registryUrl, username, password, null, null, null, "cloud");
    public static DokployRegistrySettings CreateSelfHosted(ParameterResource registryUrlParameter, string username, string password) => new(DokployRegistryMode.SelfHosted, null, username, password, registryUrlParameter, null, null, "cloud");
    public static DokployRegistrySettings CreateHosted(string registryUrl, string username, string password) => new(DokployRegistryMode.Hosted, registryUrl, username, password, null, null, null, "cloud");
    public static DokployRegistrySettings CreateHosted(ParameterResource registryUrlParameter, ParameterResource usernameParameter, ParameterResource passwordParameter) => new(DokployRegistryMode.Hosted, null, null, null, registryUrlParameter, usernameParameter, passwordParameter, "cloud");

    internal async Task<DokployResolvedRegistrySettings> ResolveAsync(CancellationToken cancellationToken)
    {
        var registryUrl = RegistryUrlParameter is not null
            ? await RegistryUrlParameter.GetValueAsync(cancellationToken)
            : RegistryUrl;

        var username = UsernameParameter is not null
            ? await UsernameParameter.GetValueAsync(cancellationToken)
            : Username;

        var password = PasswordParameter is not null
            ? await PasswordParameter.GetValueAsync(cancellationToken)
            : Password;

        if (string.IsNullOrWhiteSpace(registryUrl))
        {
            throw new InvalidOperationException("Registry URL is required.");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Registry username is required.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Registry password is required.");
        }

        return new DokployResolvedRegistrySettings(Mode, registryUrl, username, password, RegistryType);
    }
}

internal sealed class DokployResolvedRegistrySettings
{
    public DokployResolvedRegistrySettings(DokployRegistryMode mode, string registryUrl, string username, string password, string registryType)
    {
        Mode = mode;
        RegistryUrl = registryUrl;
        Username = username;
        Password = password;
        RegistryType = registryType;
    }

    public DokployRegistryMode Mode { get; }
    public string RegistryUrl { get; }
    public string Username { get; }
    public string Password { get; }
    public string RegistryType { get; }
}

public class DokployProjectEnvironmentResource : Resource, IContainerRegistry
{
    private readonly string _name;
    private readonly ParameterResource? _apiKeyParameter;
    private readonly ParameterResource? _apiUrlParameter;
    private readonly DokployRegistrySettings _registrySettings;
    internal DokployProjectEnvironmentResource(string name, ParameterResource? apikey, ParameterResource? apiUrlParameter, DokployRegistrySettings registrySettings) : base(name)
    {
        _name = name;
        _apiKeyParameter = apikey;
        _apiUrlParameter = apiUrlParameter;
        _registrySettings = registrySettings;

#pragma warning disable ASPIREPIPELINES001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        Annotations.Add(new PipelineStepAnnotation(ctx =>
        {
            return [new PipelineStep() {
               Name = $"prepare-registry-{name}",
               Action = async ctx => {
                var apiKeyVal = await ResolveApiKeyAsync(ctx.CancellationToken);
                var apiUrl = await ResolveApiUrlAsync(ctx.CancellationToken);
                var resolvedRegistrySettings = await _registrySettings.ResolveAsync(ctx.CancellationToken);
                ctx.Logger.LogInformation("Deploying project {ProjectName} with API key {ApiKey}", name, apiKeyVal.Substring(0, 4) + "****" + apiKeyVal.Substring(apiKeyVal.Length - 4));
                var api = new DokployApi(apiKeyVal, apiUrl, ctx.Services.GetRequiredService<IHostEnvironment>(), ctx.Logger, resolvedRegistrySettings);

                // We create a project for the given apphost
                var projectName = $"{name}-project";
                var proj = await api.GetProjectOrCreateAsync(projectName);
                ctx.Logger.LogInformation("Project {ProjectName} exists.", proj.Name);

                // We register a docker registry and wire it up
                var registry = await api.GetOrCreateRegistryAsync(proj);
                ContainerRegistryUrl = registry.RegistryUrl;
                ctx.Logger.LogInformation("Registry for project {ProjectName} is ready.", proj.Name);

                // We login with the container runtime
#pragma warning disable ASPIRECONTAINERRUNTIME001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                var containerRuntime = ctx.Services.GetRequiredService<IContainerRuntime>();
#pragma warning restore ASPIRECONTAINERRUNTIME001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                await containerRuntime.LoginToRegistryAsync(registry.RegistryUrl, resolvedRegistrySettings.Username, resolvedRegistrySettings.Password, ctx.CancellationToken);
                


                // 

               },
               RequiredBySteps = [WellKnownPipelineSteps.Deploy],
               DependsOnSteps = [WellKnownPipelineSteps.DeployPrereq]
            }, new PipelineStep() {
               Name = $"provision-apps-{name}",
               Action = async ctx => {
                     var apiKeyVal = await ResolveApiKeyAsync(ctx.CancellationToken);
                    var apiUrl = await ResolveApiUrlAsync(ctx.CancellationToken);
                     var resolvedRegistrySettings = await _registrySettings.ResolveAsync(ctx.CancellationToken);
                     var api = new DokployApi(apiKeyVal, apiUrl, ctx.Services.GetRequiredService<IHostEnvironment>(), ctx.Logger, resolvedRegistrySettings);
                     var rscs = ctx.Model.GetComputeResources();

                    var applications = new List<(IComputeResource Resource, DokployApi.Application Application)>();

                    foreach (var rsc in rscs)
                    {
                        // Get or create call
                        var computeResource = (IComputeResource)rsc;
                        var application = await api.GetOrCreateApplication($"{name}-app-{rsc.Name}", $"{name}-project");
                        applications.Add((computeResource, application));
                    }

                    var applicationHostsByResource = applications.ToDictionary(
                        app => app.Resource.Name,
                        app => string.IsNullOrWhiteSpace(app.Application.AppName) ? app.Application.Name : app.Application.AppName,
                        StringComparer.OrdinalIgnoreCase);

                    // Phase 2: update docker provider, domains, and env after all applications exist.
                    foreach (var app in applications)
                    {
                        await api.ConfigureApplicationAsync(app.Application, $"{name}-project", app.Resource, ctx.ExecutionContext, applicationHostsByResource, ctx.CancellationToken);
                    }

                    // Phase 3: start/deploy applications only after all configuration is applied.
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
#pragma warning restore ASPIREPIPELINES001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    }

    private string? ContainerRegistryUrl = null;
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

internal class DokployApi(string apiKey, string url, IHostEnvironment env, ILogger logger, DokployResolvedRegistrySettings registrySettings)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<Project> GetProjectOrCreateAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Project id/name must be provided.", nameof(name));
        }

        using var http = CreateHttpClient();

        // Query all projects and match by name so we can keep the real project id from Dokploy.
        using var allResponse = await http.GetAsync("api/project.all");
        if (allResponse.IsSuccessStatusCode)
        {
            var existing = await FindProjectByNameFromResponseAsync(allResponse, name);
            if (existing is not null)
            {
                logger.LogInformation("Project {ProjectName} already exists with id {ProjectId}.", existing.Name, existing.Id);
                return existing;
            }
        }

        logger.LogInformation("Project {ProjectName} not found. Creating new project.", name);

        var createBody = JsonSerializer.Serialize(new
        {
            name = name,
            description = "Project created from Aspire hosting environment.",
            env = env.EnvironmentName
        }, JsonOptions);

        // TODO: Use Httpclient json extension methods
        using var createResponse = await http.PostAsync("api/project.create", new StringContent(createBody, Encoding.UTF8, "application/json"));

        logger.LogInformation("Create project response: {StatusCode} - {ReasonPhrase}", createResponse.StatusCode, createResponse.ReasonPhrase);

        createResponse.EnsureSuccessStatusCode();

        return await ReadProjectFromResponseAsync(createResponse)
            ?? throw new InvalidOperationException($"Dokploy returned success for project '{name}', but no project payload was found.");
    }

    private HttpClient CreateHttpClient()
    {
        var baseUrl = url.EndsWith("/", StringComparison.Ordinal) ? url : $"{url}/";
        var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute)
        };

        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        return http;
    }

    private static async Task<Project?> ReadProjectFromResponseAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        return TryExtractProject(json.RootElement);
    }

    private static async Task<Project?> FindProjectByNameFromResponseAsync(HttpResponseMessage response, string expectedName)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        return FindProjectByName(json.RootElement, expectedName);
    }

    private static Project? TryExtractProject(JsonElement root)
    {
        if (TryDeserializeProject(root, out var directProject))
        {
            return directProject;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "project", "data", "result" })
            {
                if (root.TryGetProperty(key, out var nested) && TryDeserializeProject(nested, out var nestedProject))
                {
                    return nestedProject;
                }
            }
        }

        return null;
    }

    private static bool TryDeserializeProject(JsonElement value, out Project? project)
    {
        project = null;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!value.TryGetProperty("name", out _))
        {
            return false;
        }

        project = value.Deserialize<Project>(JsonOptions);
        return project is not null;
    }

    private static Project? FindProjectByName(JsonElement value, string expectedName)
    {
        if (TryDeserializeProject(value, out var candidate) && string.Equals(candidate?.Name, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var match = FindProjectByName(item, expectedName);
                if (match is not null)
                {
                    return match;
                }
            }
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in value.EnumerateObject())
            {
                var match = FindProjectByName(prop.Value, expectedName);
                if (match is not null)
                {
                    return match;
                }
            }
        }

        return null;
    }


    internal async Task<Registry> GetOrCreateRegistryAsync(Project proj)
    {
        ArgumentNullException.ThrowIfNull(proj);
        if (string.IsNullOrWhiteSpace(proj.Id))
        {
            throw new InvalidOperationException($"Project '{proj.Name}' has no id. Cannot create registry compose resource without project id.");
        }

        using var http = CreateHttpClient();

        // Refresh project details so we inspect the latest environments and compose resources.
        using var projectResponse = await http.GetAsync($"api/project.one?projectId={Uri.EscapeDataString(proj.Id)}");
        projectResponse.EnsureSuccessStatusCode();

        var refreshedProject = await ReadProjectFromResponseAsync(projectResponse)
            ?? throw new InvalidOperationException($"Could not parse project details for project id '{proj.Id}'.");

        var targetEnvironment = refreshedProject.Environments
            .FirstOrDefault(e => string.Equals(e.Name, "production", StringComparison.OrdinalIgnoreCase))
            ?? refreshedProject.Environments.FirstOrDefault();

        if (targetEnvironment is null || string.IsNullOrWhiteSpace(targetEnvironment.Id))
        {
            throw new InvalidOperationException($"Project '{refreshedProject.Name}' has no usable environment (expected one named 'production').");
        }

        if (registrySettings.Mode == DokployRegistryMode.Hosted)
        {
            var hostedRegistryName = $"{refreshedProject.Name}-registry";
            var linkedHostedRegistry = await EnsureRegistryLinkedAsync(http, hostedRegistryName);
            return new Registry
            {
                RegistryId = linkedHostedRegistry?.RegistryId,
                RegistryUrl = linkedHostedRegistry?.RegistryUrl ?? GetRegistryUrl(),
                ProjectId = refreshedProject.Id,
                EnvironmentId = targetEnvironment.Id,
                Name = linkedHostedRegistry?.RegistryName ?? hostedRegistryName
            };
        }

        var existingRegistry = targetEnvironment.Compose
            .FirstOrDefault(c => string.Equals(c.Name, "registry", StringComparison.OrdinalIgnoreCase));

        if (existingRegistry is not null)
        {
            var existingRegistryDetails = await GetComposeDetailsAsync(http, existingRegistry);
            await DeployComposeAsync(http, existingRegistryDetails);
            await EnsureRegistryComposeDomainAsync(http, existingRegistryDetails);
            var linkedRegistry = await EnsureRegistryLinkedAsync(http, existingRegistryDetails.Name);

            logger.LogInformation("Registry compose already exists for project {ProjectName} in environment {EnvironmentName}.", refreshedProject.Name, targetEnvironment.Name);
            return new Registry
            {
                RegistryId = linkedRegistry?.RegistryId,
                RegistryUrl = linkedRegistry?.RegistryUrl ?? GetRegistryUrl(),
                ProjectId = refreshedProject.Id,
                EnvironmentId = targetEnvironment.Id,
                ComposeId = existingRegistryDetails.Id ?? existingRegistry.Id,
                Name = existingRegistryDetails.Name
            };
        }

        var deployBody = JsonSerializer.Serialize(new
        {
            environmentId = targetEnvironment.Id,
            id = "registry"
        }, JsonOptions);

        using var deployResponse = await http.PostAsync("api/compose.deployTemplate", new StringContent(deployBody, Encoding.UTF8, "application/json"));
        deployResponse.EnsureSuccessStatusCode();

        var deployedCompose = await ReadComposeFromResponseAsync(deployResponse);
        if (deployedCompose is not null)
        {
            logger.LogInformation("Registry compose deployment accepted for project {ProjectName}. Verifying via project.one.", refreshedProject.Name);
        }

        // Fallback for APIs that return ack-only payloads: refetch project and locate compose there.
        using var verifyResponse = await http.GetAsync($"api/project.one?projectId={Uri.EscapeDataString(proj.Id)}");
        verifyResponse.EnsureSuccessStatusCode();

        var verifiedProject = await ReadProjectFromResponseAsync(verifyResponse)
            ?? throw new InvalidOperationException($"Registry deploy completed but project '{proj.Name}' could not be reloaded for verification.");

        var verifiedEnvironment = verifiedProject.Environments
            .FirstOrDefault(e => string.Equals(e.Name, targetEnvironment.Name, StringComparison.OrdinalIgnoreCase));

        var verifiedRegistry = verifiedEnvironment?.Compose
            .FirstOrDefault(c => string.Equals(c.Name, "registry", StringComparison.OrdinalIgnoreCase));

        if (verifiedRegistry is null)
        {
            throw new InvalidOperationException($"Registry compose deploy returned success but no 'registry' compose was found for project '{verifiedProject.Name}'.");
        }

        var verifiedRegistryDetails = await GetComposeDetailsAsync(http, verifiedRegistry);
        await DeployComposeAsync(http, verifiedRegistryDetails);
        await EnsureRegistryComposeDomainAsync(http, verifiedRegistryDetails);
        var linkedVerifiedRegistry = await EnsureRegistryLinkedAsync(http, verifiedRegistryDetails.Name);

        return new Registry
        {
            RegistryId = linkedVerifiedRegistry?.RegistryId,
            RegistryUrl = linkedVerifiedRegistry?.RegistryUrl ?? GetRegistryUrl(),
            ProjectId = verifiedProject.Id,
            EnvironmentId = verifiedEnvironment?.Id,
            ComposeId = verifiedRegistryDetails.Id ?? verifiedRegistry.Id,
            Name = verifiedRegistryDetails.Name
        };
    }

    private async Task EnsureRegistryComposeDomainAsync(HttpClient http, Compose compose)
    {
        if (string.IsNullOrWhiteSpace(compose.Id))
        {
            throw new InvalidOperationException($"Compose '{compose.Name}' has no composeId, so compose domain cannot be verified.");
        }

        var registryHost = GetRegistryUrl();
        using var byComposeResponse = await http.GetAsync($"api/domain.byComposeId?composeId={Uri.EscapeDataString(compose.Id)}");
        byComposeResponse.EnsureSuccessStatusCode();

        var existingDomains = await ReadDomainsFromResponseAsync(byComposeResponse, logger, "domain.byComposeId");
        var existingDomain = existingDomains.FirstOrDefault(d => string.Equals(d.Host, registryHost, StringComparison.OrdinalIgnoreCase));
        if (existingDomain is not null)
        {
            if (string.IsNullOrWhiteSpace(existingDomain.Id))
            {
                throw new InvalidOperationException($"Compose domain '{registryHost}' exists for compose '{compose.Name}' but no domainId was returned.");
            }

            var updateBody = JsonSerializer.Serialize(new
            {
                domainId = existingDomain.Id,
                host = registryHost,
                port = 5000,
                https = true,
                certificateType = "letsencrypt",
                serviceName = "registry",
                domainType = "compose"
            }, JsonOptions);

            using var updateResponse = await http.PostAsync("api/domain.update", new StringContent(updateBody, Encoding.UTF8, "application/json"));
            updateResponse.EnsureSuccessStatusCode();
            logger.LogInformation("Updated compose domain {DomainHost} for registry compose {ComposeName}.", registryHost, compose.Name);
            return;
        }

        var createBody = JsonSerializer.Serialize(new
        {
            composeId = compose.Id,
            host = registryHost,
            port = 5000,
            https = true,
            certificateType = "letsencrypt",
            serviceName = "registry",
            domainType = "compose"
        }, JsonOptions);

        using var createResponse = await http.PostAsync("api/domain.create", new StringContent(createBody, Encoding.UTF8, "application/json"));
        createResponse.EnsureSuccessStatusCode();
        logger.LogInformation("Created compose domain {DomainHost} for registry compose {ComposeName}.", registryHost, compose.Name);
    }

    private async Task<Compose> GetComposeDetailsAsync(HttpClient http, Compose compose)
    {
        if (string.IsNullOrWhiteSpace(compose.Id))
        {
            throw new InvalidOperationException($"Compose '{compose.Name}' does not have composeId, so compose.one cannot be called.");
        }

        using var composeResponse = await http.GetAsync($"api/compose.one?composeId={Uri.EscapeDataString(compose.Id)}");
        composeResponse.EnsureSuccessStatusCode();

        var fullCompose = await ReadComposeFromResponseAsync(composeResponse)
            ?? throw new InvalidOperationException($"compose.one returned success but no compose payload for composeId '{compose.Id}'.");

        return fullCompose;
    }

    private async Task DeployComposeAsync(HttpClient http, Compose compose)
    {
        if (string.IsNullOrWhiteSpace(compose.Id))
        {
            throw new InvalidOperationException($"Compose '{compose.Name}' does not have composeId, so compose.deploy cannot be called.");
        }

        var deployBody = JsonSerializer.Serialize(new
        {
            composeId = compose.Id,
            title = $"Aspire deploy for {compose.Name}",
            description = "Started automatically before registry link verification."
        }, JsonOptions);

        using var deployResponse = await http.PostAsync("api/compose.deploy", new StringContent(deployBody, Encoding.UTF8, "application/json"));
        logger.LogInformation("Compose deploy response for {ComposeName}: {StatusCode} - {ReasonPhrase}", compose.Name, deployResponse.StatusCode, deployResponse.ReasonPhrase);
        deployResponse.EnsureSuccessStatusCode();
    }

    private static async Task<Compose?> ReadComposeFromResponseAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        return TryExtractCompose(json.RootElement);
    }

    private static Compose? TryExtractCompose(JsonElement root)
    {
        if (TryDeserializeCompose(root, out var directCompose))
        {
            return directCompose;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "compose", "data", "result" })
            {
                if (root.TryGetProperty(key, out var nested) && TryDeserializeCompose(nested, out var nestedCompose))
                {
                    return nestedCompose;
                }
            }
        }

        return null;
    }

    private static bool TryDeserializeCompose(JsonElement value, out Compose? compose)
    {
        compose = null;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!value.TryGetProperty("name", out _))
        {
            return false;
        }

        compose = value.Deserialize<Compose>(JsonOptions);
        return compose is not null;
    }

    private async Task<RemoteRegistry?> EnsureRegistryLinkedAsync(HttpClient http, string registryName)
    {
        var registryUrl = GetRegistryUrl();
        var username = registrySettings.Username;
        var password = registrySettings.Password;

        var testInput = new RegistryRequestPayload
        {
            RegistryName = registryName,
            Username = username,
            Password = password,
            RegistryUrl = registryUrl,
            RegistryType = registrySettings.RegistryType
        };

        var existingRegistry = await FindExistingRegistryAsync(http, testInput);
        if (existingRegistry is not null)
        {
            logger.LogInformation("Registry URL {RegistryUrl} already exists as registryId {RegistryId}.", registryUrl, existingRegistry.RegistryId);

            

            return existingRegistry;
        }

        var works = await TestRegistryLinkedAsync(http, testInput, logger);
        if (!works)
        {
            logger.LogInformation("Registry URL {RegistryUrl} is not yet valid in Dokploy. Will attempt creation.", registryUrl);
        }

        var createBody = JsonSerializer.Serialize(new
        {
            registryName = registryName,
            username,
            password,
            registryUrl,
            registryType = registrySettings.RegistryType,
            imagePrefix = registryUrl
        }, JsonOptions);

        using var createResponse = await http.PostAsync("api/registry.create", new StringContent(createBody, Encoding.UTF8, "application/json"));

        logger.LogInformation("Create registry response: {StatusCode} - {ReasonPhrase}", createResponse.StatusCode, createResponse.ReasonPhrase);

        createResponse.EnsureSuccessStatusCode();
        logger.LogInformation("Created Dokploy registry entry for {RegistryUrl}.", registryUrl);

        var createdRegistry = await ReadRegistryFromResponseAsync(createResponse);
        if (createdRegistry is not null)
        {
            return createdRegistry;
        }

        // Some APIs return ack-only payloads; resolve ID via a fresh list query.
        return await FindExistingRegistryAsync(http, testInput);
    }

    private static async Task<RemoteRegistry?> FindExistingRegistryAsync(HttpClient http, RegistryRequestPayload payload)
    {
        using var allResponse = await http.GetAsync("api/registry.all");
        allResponse.EnsureSuccessStatusCode();

        var registries = await ReadRegistriesFromResponseAsync(allResponse);
        return registries.FirstOrDefault(r =>
            string.Equals(r.RegistryUrl, payload.RegistryUrl, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Username, payload.Username, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<List<RemoteRegistry>> ReadRegistriesFromResponseAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var output = new List<RemoteRegistry>();
        CollectRegistries(json.RootElement, output);
        return output;
    }

    private static async Task<RemoteRegistry?> ReadRegistryFromResponseAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        return TryExtractRegistry(json.RootElement);
    }

    private static RemoteRegistry? TryExtractRegistry(JsonElement element)
    {
        if (TryDeserializeRegistry(element, out var registry))
        {
            return registry;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "registry", "data", "result" })
            {
                if (element.TryGetProperty(key, out var nested))
                {
                    var nestedRegistry = TryExtractRegistry(nested);
                    if (nestedRegistry is not null)
                    {
                        return nestedRegistry;
                    }
                }
            }
        }

        return null;
    }

    private static void CollectRegistries(JsonElement element, List<RemoteRegistry> output)
    {
        if (TryDeserializeRegistry(element, out var registry))
        {
            output.Add(registry);
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                CollectRegistries(prop.Value, output);
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectRegistries(item, output);
            }
        }
    }

    private static bool TryDeserializeRegistry(JsonElement value, out RemoteRegistry registry)
    {
        registry = new RemoteRegistry();
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!value.TryGetProperty("registryUrl", out _))
        {
            return false;
        }

        var candidate = value.Deserialize<RemoteRegistry>(JsonOptions);
        if (candidate is null)
        {
            return false;
        }

        registry = candidate;
        return true;
    }

    private static async Task<bool> TestRegistryLinkedAsync(HttpClient http, RegistryRequestPayload payload, ILogger? logger = null)
    {
        var testBody = JsonSerializer.Serialize(new
        {
            registryName = payload.RegistryName,
            username = payload.Username,
            password = payload.Password,
            registryUrl = payload.RegistryUrl,
            registryType = payload.RegistryType
        }, JsonOptions);

        // TODO: BTW we should probably check if compose is running before trying to start it I GUESS

        logger?.LogInformation("Testing if registry URL {payload} is linked in Dokploy.", testBody);
        using var testResponse = await http.PostAsync("api/registry.testRegistry", new StringContent(testBody, Encoding.UTF8, "application/json"));
        logger?.LogInformation("Test registry response: {StatusCode} - {ReasonPhrase}", testResponse.StatusCode, await testResponse.Content.ReadAsStringAsync());
        
        testResponse.EnsureSuccessStatusCode();

        await using var stream = await testResponse.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        return ExtractLinkState(json.RootElement);
    }

    private static bool ExtractLinkState(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "linked", "isLinked", "exists", "registered", "success", "data" })
            {
                if (element.TryGetProperty(key, out var nested))
                {
                    var nestedResult = ExtractLinkState(nested);
                    if (nestedResult)
                    {
                        return true;
                    }

                    if (nested.ValueKind == JsonValueKind.False)
                    {
                        return false;
                    }
                }
            }

            foreach (var prop in element.EnumerateObject())
            {
                var nestedResult = ExtractLinkState(prop.Value);
                if (nestedResult)
                {
                    return true;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ExtractLinkState(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private sealed class RegistryRequestPayload
    {
        public string RegistryName { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string RegistryUrl { get; init; } = string.Empty;
        public string RegistryType { get; init; } = string.Empty;
    }

    private string GetRegistryUrl()
    {
        return registrySettings.RegistryUrl;
    }

    private static string GetRegistryUsernameFromCompose(Compose compose)
    {
        var composeFile = NormalizeTemplateText(compose.ComposeFile);
        if (string.IsNullOrWhiteSpace(composeFile))
        {
            throw new InvalidOperationException($"Compose '{compose.Name}' has no compose file content to extract registry username from.");
        }

        // Try common YAML keys that store a registry username.
        var keyMatch = Regex.Match(composeFile, @"(?im)^\s*(REGISTRY_USERNAME|REGISTRY_USER|USERNAME)\s*[:=]\s*""?([^""\r\n#]+)");
        if (keyMatch.Success)
        {
            return keyMatch.Groups[2].Value.Trim();
        }

        // Some Dokploy templates expose auth mode as REGISTRY_AUTH (for example: htpasswd).
        var authModeMatch = Regex.Match(composeFile, @"(?im)^\s*REGISTRY_AUTH\s*[:=]\s*""?([^""\r\n#]+)");
        if (authModeMatch.Success)
        {
            return authModeMatch.Groups[1].Value.Trim();
        }

        // Fallback: look for basic auth url patterns like user@host.
        var userAtHost = Regex.Match(composeFile, @"(?i)\b([a-z0-9._-]+)@[a-z0-9.-]+\b");
        if (userAtHost.Success)
        {
            return userAtHost.Groups[1].Value.Trim();
        }

        throw new InvalidOperationException($"Could not extract registry username from compose file for compose '{compose.Name}'.");
    }

    private static string NormalizeTemplateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        // Dokploy may return template content with escaped newlines.
        return text.Replace("\\r\\n", "\n", StringComparison.Ordinal)
                   .Replace("\\n", "\n", StringComparison.Ordinal)
                   .Replace("\\r", "\n", StringComparison.Ordinal);
    }

    private static string GetRequiredEnvValue(string envText, string key)
    {
        if (string.IsNullOrWhiteSpace(envText))
        {
            throw new InvalidOperationException($"Compose env is empty; required key '{key}' was not found.");
        }

        var lines = envText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal) || !trimmed.Contains('='))
            {
                continue;
            }

            var idx = trimmed.IndexOf('=');
            var lineKey = trimmed[..idx].Trim();
            if (!string.Equals(lineKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = trimmed[(idx + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim('"');
            }
        }

        throw new InvalidOperationException($"Required env key '{key}' was not found in compose env values.");
    }

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

        var refreshedProject = await ReadProjectFromResponseAsync(projectResponse)
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
            appName,
            description = "Created by Aspire deploy pipeline.",
            environmentId = targetEnvironment.Id
        }, JsonOptions);

        using var createResponse = await http.PostAsync("api/application.create", new StringContent(createBody, Encoding.UTF8, "application/json"));
        createResponse.EnsureSuccessStatusCode();

        var created = await ReadApplicationFromResponseAsync(createResponse);
        logger.LogInformation("Created application {AppName} in project {ProjectName}.", appName, refreshedProject.Name);

        // Fallback for APIs that return ack-only payloads.
        using var verifyResponse = await http.GetAsync($"api/project.one?projectId={Uri.EscapeDataString(project.Id)}");
        verifyResponse.EnsureSuccessStatusCode();

        var verifiedProject = await ReadProjectFromResponseAsync(verifyResponse)
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

        if (string.IsNullOrWhiteSpace(application.Id))
        {
            throw new InvalidOperationException($"Application '{rsc.Name}' has no applicationId, so provider cannot be configured.");
        }

        var registryUrl = GetRegistryUrl();

        string? dockerImage = null;

        if (rsc.TryGetLastAnnotation<DockerfileBuildAnnotation>(out var buildAnnotation) || rsc is ProjectResource pr)
        {
            var imageref = new ContainerImageReference(rsc);
            dockerImage = await ((IValueProvider)imageref).GetValueAsync();
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

        using var saveDockerProviderResponse = await http.PostAsync("api/application.saveDockerProvider", new StringContent(saveDockerProviderBody, Encoding.UTF8, "application/json"));
        saveDockerProviderResponse.EnsureSuccessStatusCode();

        logger.LogInformation("Saved docker provider for application {AppName}.", rsc.Name);

        await SaveApplicationEnvironmentAsync(http, application, projectName, rsc, executionContext, applicationHostsByResource, cancellationToken);

        await EnsureApplicationDomainAsync(http, application, rsc);
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

        using var deployResponse = await http.PostAsync("api/application.deploy", new StringContent(deployBody, Encoding.UTF8, "application/json"));
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

        var envKeys = environmentVariables.Keys
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        logger.LogInformation(
            "Preparing to save {Count} environment variable(s) for resource {ResourceName}. Keys: {EnvironmentKeys}",
            envKeys.Length,
            resource.Name,
            string.Join(", ", envKeys));
        logger.LogInformation(
            "Environment payload size for resource {ResourceName}: {PayloadLength} characters.",
            resource.Name,
            envPayload.Length);

        var saveEnvironmentBody = JsonSerializer.Serialize(new
        {
            applicationId = application.Id,
            env = envPayload,
            createEnvFile = true,
            buildArgs = "",
            buildSecrets = ""
        }, JsonOptions);

        using var saveEnvironmentResponse = await http.PostAsync(
            "api/application.saveEnvironment",
            new StringContent(saveEnvironmentBody, Encoding.UTF8, "application/json"));

        saveEnvironmentResponse.EnsureSuccessStatusCode();

        logger.LogInformation(
            "Saved {Count} environment variable(s) for application {AppName}.",
            environmentVariables.Count,
            resource.Name);
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
            var callbackContext = new EnvironmentCallbackContext(
                executionContext,
                resource,
                environmentVariables,
                cancellationToken: cancellationToken);

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

        return materialized;
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

        var resolved = referencedResource
            .ResolveEndpoints()
            .FirstOrDefault(e => string.Equals(e.Endpoint?.Name, endpointReference.EndpointName, StringComparison.OrdinalIgnoreCase));

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

        var existingDomains = await ReadDomainsFromResponseAsync(byAppResponse, logger, "domain.byApplicationId");
        var appNameForDomain = string.IsNullOrWhiteSpace(application.AppName) ? application.Name : application.AppName;
        if (string.IsNullOrWhiteSpace(appNameForDomain))
        {
            throw new InvalidOperationException("Application name is required to generate a domain.");
        }

        var generateBody = JsonSerializer.Serialize(new
        {
            appName = appNameForDomain
        }, JsonOptions);

        using var generateResponse = await http.PostAsync("api/domain.generateDomain", new StringContent(generateBody, Encoding.UTF8, "application/json"));
        generateResponse.EnsureSuccessStatusCode();

        var generatedHost = await ReadGeneratedHostFromResponseAsync(generateResponse, logger)
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

                using var updateResponse = await http.PostAsync("api/domain.update", new StringContent(updateBody, Encoding.UTF8, "application/json"));
                updateResponse.EnsureSuccessStatusCode();

                logger.LogInformation(
                    "Updated deployed application URL {EndpointUrl} for application {AppName} using endpoint {EndpointName}.",
                    endpointUrl,
                    appNameForDomain,
                    endpoint.Name);
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

            using var createResponse = await http.PostAsync("api/domain.create", new StringContent(createBody, Encoding.UTF8, "application/json"));
            createResponse.EnsureSuccessStatusCode();

            logger.LogInformation(
                "Created deployed application URL {EndpointUrl} for application {AppName} using endpoint {EndpointName}.",
                endpointUrl,
                appNameForDomain,
                endpoint.Name);
        }

        if (unmatchedDomains.Count > 0)
        {
            logger.LogInformation(
                "Application {AppName} has {ExtraCount} extra existing domain(s) beyond the {ExpectedCount} external Aspire endpoint(s); leaving them unchanged.",
                appNameForDomain,
                unmatchedDomains.Count,
                externalEndpoints.Count);
        }
    }

    private static string BuildApplicationEndpointUrl(string host, ExternalEndpointConfig endpoint)
    {
        return $"{endpoint.Scheme}://{host}:{endpoint.Port}";
    }

    private static List<ExternalEndpointConfig> GetExternalEndpoints(IComputeResource resource)
    {
        var endpoints = resource.ResolveEndpoints();

        return endpoints
            .Where(e => e.Endpoint.IsExternal)
            .Select(e => new ExternalEndpointConfig(
                e.Endpoint.Name,
                e.Endpoint.UriScheme ?? "http",
                e.TargetPort.Value
                    ?? e.ExposedPort.Value
                    ?? 8080))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Scheme, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Port)
            .DistinctBy(e => $"{e.Name}|{e.Scheme}|{e.Port}")
            .ToList();
    }

    private sealed record ExternalEndpointConfig(string Name, string Scheme, int Port);

    private static async Task<List<Domain>> ReadDomainsFromResponseAsync(HttpResponseMessage response, ILogger? logger = null, string source = "domain.byApplicationId")
    {
        var content = NormalizeJsonPayload(await response.Content.ReadAsStringAsync());
        logger?.LogInformation("{DomainSource} payload: {Payload}", source, GetPayloadSnippet(content));

        using var root = JsonDocument.Parse(content);
        logger?.LogInformation("{DomainSource} root kind: {RootKind}", source, root.RootElement.ValueKind);
        if (root.RootElement.ValueKind == JsonValueKind.Object)
        {
            var direct = JsonSerializer.Deserialize<Domain>(content, JsonOptions);
            if (direct is not null && !string.IsNullOrWhiteSpace(direct.Host))
            {
                return new List<Domain> { direct };
            }

            var wrapped = JsonSerializer.Deserialize<TrpcEnvelope<List<Domain>>>(content, JsonOptions);
            var wrappedDomains = wrapped?.Result?.Data?.Json?.Where(d => !string.IsNullOrWhiteSpace(d.Host)).ToList();
            if (wrappedDomains is { Count: > 0 })
            {
                return wrappedDomains;
            }

            var wrappedSingle = JsonSerializer.Deserialize<TrpcEnvelope<Domain>>(content, JsonOptions);
            var single = wrappedSingle?.Result?.Data?.Json;
            if (single is not null && !string.IsNullOrWhiteSpace(single.Host))
            {
                return new List<Domain> { single };
            }

            return [];
        }

        var directList = JsonSerializer.Deserialize<List<Domain>>(content, JsonOptions);
        if (directList is { Count: > 0 })
        {
            return directList.Where(d => !string.IsNullOrWhiteSpace(d.Host)).ToList();
        }

        var wrappedList = JsonSerializer.Deserialize<List<TrpcEnvelope<List<Domain>>>>(content, JsonOptions);
        if (wrappedList is { Count: > 0 })
        {
            var domains = wrappedList
                .SelectMany(x => x.Result?.Data?.Json ?? new List<Domain>())
                .Where(d => !string.IsNullOrWhiteSpace(d.Host))
                .ToList();

            if (domains.Count > 0)
            {
                return domains;
            }
        }

        var wrappedSingleList = JsonSerializer.Deserialize<List<TrpcEnvelope<Domain>>>(content, JsonOptions);
        if (wrappedSingleList is { Count: > 0 })
        {
            var domains = wrappedSingleList
                .Select(x => x.Result?.Data?.Json)
                .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Host))
                .Select(d => d!)
                .ToList();

            if (domains.Count > 0)
            {
                return domains;
            }
        }

        return new List<Domain>();
    }

    private static async Task<string?> ReadGeneratedHostFromResponseAsync(HttpResponseMessage response, ILogger? logger = null)
    {
        var content = NormalizeJsonPayload(await response.Content.ReadAsStringAsync());
        logger?.LogInformation("domain.generateDomain payload: {Payload}", GetPayloadSnippet(content));

        using var root = JsonDocument.Parse(content);
        logger?.LogInformation("domain.generateDomain root kind: {RootKind}", root.RootElement.ValueKind);
        if (root.RootElement.ValueKind == JsonValueKind.String)
        {
            return root.RootElement.GetString();
        }

        if (root.RootElement.ValueKind == JsonValueKind.Object)
        {
            var wrappedString = JsonSerializer.Deserialize<TrpcEnvelope<string>>(content, JsonOptions);
            var fromWrappedString = wrappedString?.Result?.Data?.Json;
            if (!string.IsNullOrWhiteSpace(fromWrappedString))
            {
                return fromWrappedString;
            }

            var wrappedData = JsonSerializer.Deserialize<TrpcEnvelope<GeneratedDomainData>>(content, JsonOptions);
            var fromWrappedData = wrappedData?.Result?.Data?.Json;
            if (fromWrappedData is not null)
            {
                var host = fromWrappedData.Json ?? fromWrappedData.Host ?? fromWrappedData.Domain;
                if (!string.IsNullOrWhiteSpace(host))
                {
                    return host;
                }
            }

            var directString = JsonSerializer.Deserialize<string>(content, JsonOptions);
            if (!string.IsNullOrWhiteSpace(directString))
            {
                return directString;
            }

            var directData = JsonSerializer.Deserialize<GeneratedDomainData>(content, JsonOptions);
            if (directData is not null)
            {
                return directData.Json ?? directData.Host ?? directData.Domain;
            }

            return null;
        }

        var wrappedStringList = JsonSerializer.Deserialize<List<TrpcEnvelope<string>>>(content, JsonOptions);
        var fromWrappedStringList = wrappedStringList?.Select(x => x.Result?.Data?.Json).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        if (!string.IsNullOrWhiteSpace(fromWrappedStringList))
        {
            return fromWrappedStringList;
        }

        var wrappedDomainDataList = JsonSerializer.Deserialize<List<TrpcEnvelope<GeneratedDomainData>>>(content, JsonOptions);
        var fromWrappedDataList = wrappedDomainDataList
            ?.Select(x => x.Result?.Data?.Json)
            .FirstOrDefault(x => x is not null && (!string.IsNullOrWhiteSpace(x.Json) || !string.IsNullOrWhiteSpace(x.Host) || !string.IsNullOrWhiteSpace(x.Domain)));
        if (fromWrappedDataList is not null)
        {
            return fromWrappedDataList.Json ?? fromWrappedDataList.Host ?? fromWrappedDataList.Domain;
        }

        var directDomainData = JsonSerializer.Deserialize<GeneratedDomainData>(content, JsonOptions);
        if (directDomainData is not null)
        {
            return directDomainData.Json ?? directDomainData.Host ?? directDomainData.Domain;
        }

        return null;
    }

    private static string NormalizeJsonPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return payload;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                var inner = doc.RootElement.GetString();
                if (!string.IsNullOrWhiteSpace(inner))
                {
                    var trimmed = inner.Trim();
                    if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
                    {
                        return trimmed;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Leave payload unchanged; callers will surface proper parse errors if needed.
        }

        return payload;
    }

    private static string GetPayloadSnippet(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return payload;
        }

        return payload.Length <= 500 ? payload : payload[..500];
    }

    private static async Task<Application?> ReadApplicationFromResponseAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        return TryExtractApplication(json.RootElement);
    }

    private static Application? TryExtractApplication(JsonElement root)
    {
        if (TryDeserializeApplication(root, out var directApp))
        {
            return directApp;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "application", "data", "result" })
            {
                if (root.TryGetProperty(key, out var nested) && TryDeserializeApplication(nested, out var nestedApp))
                {
                    return nestedApp;
                }
            }
        }

        return null;
    }

    private static bool TryDeserializeApplication(JsonElement value, out Application? application)
    {
        application = null;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!value.TryGetProperty("name", out _) && !value.TryGetProperty("appName", out _))
        {
            return false;
        }

        application = value.Deserialize<Application>(JsonOptions);
        return application is not null;
    }

    public class Project
    {
        [JsonPropertyName("projectId")]
        public string? Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("env")]
        public string? Env { get; init; }

        [JsonPropertyName("environments")]
        public List<Environment> Environments { get; init; } = new List<Environment>();

    }
    public class Compose
    {
        [JsonPropertyName("composeId")]
        public string? Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("domains")]
        public List<Domain> Domains { get; init; } = new List<Domain>();

        [JsonPropertyName("env")]
        public string Env { get; set; } = string.Empty;

        [JsonPropertyName("composeFile")]
        public string ComposeFile { get; set; } = string.Empty;

    }

    public class Environment
    {
        [JsonPropertyName("environmentId")]
        public string? Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        
        [JsonPropertyName("compose")]
        public List<Compose> Compose {get; init;} = new List<Compose>();

        [JsonPropertyName("applications")]
        public List<Application> Applications { get; init; } = new List<Application>();

    }

    public class Registry
    {
        [JsonPropertyName("registryId")]
        public string? RegistryId { get; init; }

        [JsonPropertyName("registryUrl")]
        public string RegistryUrl { get; init; } = string.Empty;

        [JsonPropertyName("projectId")]
        public string? ProjectId { get; init; }

        [JsonPropertyName("environmentId")]
        public string? EnvironmentId { get; init; }

        [JsonPropertyName("composeId")]
        public string? ComposeId { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = "registry";
    }

    private sealed class RemoteRegistry
    {
        [JsonPropertyName("registryId")]
        public string? RegistryId { get; init; }

        [JsonPropertyName("registryName")]
        public string RegistryName { get; init; } = string.Empty;

        [JsonPropertyName("imagePrefix")]
        public string? ImagePrefix { get; init; }

        [JsonPropertyName("username")]
        public string Username { get; init; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; init; } = string.Empty;

        [JsonPropertyName("registryUrl")]
        public string RegistryUrl { get; init; } = string.Empty;

        [JsonPropertyName("registryType")]
        public string RegistryType { get; init; } = string.Empty;
    }

    public class Domain
    {
        [JsonPropertyName("domainId")]
        public string? Id { get; init; }

        [JsonPropertyName("host")]
        public string Host { get; init; } = string.Empty;

        [JsonPropertyName("port")]
        public int? Port { get; init; }
    }

    private sealed class TrpcEnvelope<T>
    {
        [JsonPropertyName("result")]
        public TrpcResult<T>? Result { get; init; }
    }

    private sealed class TrpcResult<T>
    {
        [JsonPropertyName("data")]
        public TrpcData<T>? Data { get; init; }
    }

    private sealed class TrpcData<T>
    {
        [JsonPropertyName("json")]
        public T? Json { get; init; }
    }

    private sealed class GeneratedDomainData
    {
        [JsonPropertyName("json")]
        public string? Json { get; init; }

        [JsonPropertyName("host")]
        public string? Host { get; init; }

        [JsonPropertyName("domain")]
        public string? Domain { get; init; }
    }

    public class Application
    {
        [JsonPropertyName("applicationId")]
        public string? Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("appName")]
        public string AppName { get; init; } = string.Empty;

        [JsonPropertyName("dockerImage")]
        public string? DockerImage { get; init; } = string.Empty;

        [JsonPropertyName("registryUrl")]
        public string? RegistryUrl { get; init; } = string.Empty;

        [JsonPropertyName("username")]
        public string? Username { get; init; } = string.Empty;

        [JsonPropertyName("password")]
        public string? Password { get; init; } = string.Empty;
    }

}
