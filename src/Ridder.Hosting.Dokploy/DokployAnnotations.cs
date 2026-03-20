using Aspire.Hosting.ApplicationModel;

namespace Ridder.Hosting.Dokploy;

/// <summary>
/// Represents a Dokploy publishing annotation attached to an Aspire resource.
/// </summary>
public interface IDokployPublishAnnotation : IResourceAnnotation
{
    /// <summary>
    /// Gets the Dokploy environment that will provision the annotated resource.
    /// </summary>
    DokployProjectEnvironmentResource Environment { get; }
}

internal sealed class DokployEnvironmentConnectionAnnotation : IResourceAnnotation
{
    public DokployEnvironmentConnectionAnnotation(ParameterResource? apiKeyParameter, ParameterResource? apiUrlParameter)
    {
        ApiKeyParameter = apiKeyParameter;
        ApiUrlParameter = apiUrlParameter;
    }

    public ParameterResource? ApiKeyParameter { get; }
    public ParameterResource? ApiUrlParameter { get; }

    public async Task<string> ResolveApiKeyAsync(string environmentName, CancellationToken cancellationToken)
    {
        var apiKey = ApiKeyParameter is null
            ? null
            : await ApiKeyParameter.GetValueAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"API key for project {environmentName} is not set.");
        }

        return apiKey;
    }

    public async Task<string> ResolveApiUrlAsync(string environmentName, CancellationToken cancellationToken)
    {
        var apiUrl = ApiUrlParameter is null
            ? null
            : await ApiUrlParameter.GetValueAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            throw new InvalidOperationException($"Dokploy API URL for project {environmentName} is not set.");
        }

        return apiUrl;
    }
}

internal sealed class DokployRegistryAccessAnnotation : IResourceAnnotation
{
    public DokployRegistryAccessAnnotation(string containerRegistryUrl)
    {
        ContainerRegistryUrl = containerRegistryUrl ?? string.Empty;
    }

    public string ContainerRegistryUrl { get; }
}

internal interface IDokployRegistryAnnotation : IResourceAnnotation
{
    DokployRegistryMode Mode { get; }
    string RegistryType { get; }
    Task<DokployResolvedRegistrySettings> ResolveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Stores Dokploy publishing intent and application-specific options for a compute resource.
/// </summary>
public sealed class DokployPublishApplicationAnnotation : IDokployPublishAnnotation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DokployPublishApplicationAnnotation"/> class.
    /// </summary>
    /// <param name="environment">The Dokploy environment responsible for provisioning the resource.</param>
    /// <param name="options">The Dokploy application options associated with the resource.</param>
    public DokployPublishApplicationAnnotation(DokployProjectEnvironmentResource environment, DokployApplicationOptions? options = null)
    {
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        Options = (options ?? new DokployApplicationOptions()).Clone();
    }

    /// <summary>
    /// Gets the Dokploy environment responsible for provisioning the annotated resource.
    /// </summary>
    public DokployProjectEnvironmentResource Environment { get; }

    /// <summary>
    /// Gets the Dokploy application options applied to the annotated resource.
    /// </summary>
    public DokployApplicationOptions Options { get; }

    /// <summary>
    /// Resolves the Dokploy application name for a compute resource.
    /// </summary>
    /// <param name="resourceName">The Aspire resource name.</param>
    /// <returns>The Dokploy application name to use.</returns>
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

internal abstract class DokployRegistryAnnotationBase : IDokployRegistryAnnotation
{
    private readonly string? _registryUrl;
    private readonly string? _username;
    private readonly string? _password;
    private readonly ParameterResource? _registryUrlParameter;
    private readonly ParameterResource? _usernameParameter;
    private readonly ParameterResource? _passwordParameter;

    protected DokployRegistryAnnotationBase(
        string? registryUrl,
        string? username,
        string? password,
        ParameterResource? registryUrlParameter,
        ParameterResource? usernameParameter,
        ParameterResource? passwordParameter,
        string registryType)
    {
        _registryUrl = registryUrl;
        _username = username;
        _password = password;
        _registryUrlParameter = registryUrlParameter;
        _usernameParameter = usernameParameter;
        _passwordParameter = passwordParameter;
        RegistryType = registryType;
    }

    public abstract DokployRegistryMode Mode { get; }
    public string RegistryType { get; }

    public async Task<DokployResolvedRegistrySettings> ResolveAsync(CancellationToken cancellationToken)
    {
        var registryUrl = _registryUrlParameter is not null
            ? await _registryUrlParameter.GetValueAsync(cancellationToken)
            : _registryUrl;

        var username = _usernameParameter is not null
            ? await _usernameParameter.GetValueAsync(cancellationToken)
            : _username;

        var password = _passwordParameter is not null
            ? await _passwordParameter.GetValueAsync(cancellationToken)
            : _password;

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

internal sealed class DokployHostedRegistryAnnotation : DokployRegistryAnnotationBase
{
    public DokployHostedRegistryAnnotation(string registryUrl, string username, string password)
        : base(registryUrl, username, password, null, null, null, "cloud")
    {
    }

    public DokployHostedRegistryAnnotation(ParameterResource registryUrlParameter, ParameterResource usernameParameter, ParameterResource passwordParameter)
        : base(null, null, null, registryUrlParameter, usernameParameter, passwordParameter, "cloud")
    {
    }

    public override DokployRegistryMode Mode => DokployRegistryMode.Hosted;
}

internal sealed class DokploySelfHostedRegistryAnnotation : DokployRegistryAnnotationBase
{
    public DokploySelfHostedRegistryAnnotation(string registryUrl, string username, string password)
        : base(registryUrl, username, password, null, null, null, "cloud")
    {
    }

    public DokploySelfHostedRegistryAnnotation(ParameterResource registryUrlParameter, string username, string password)
        : base(null, username, password, registryUrlParameter, null, null, "cloud")
    {
    }

    public override DokployRegistryMode Mode => DokployRegistryMode.SelfHosted;
}