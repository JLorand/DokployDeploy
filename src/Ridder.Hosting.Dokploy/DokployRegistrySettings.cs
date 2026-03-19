using Aspire.Hosting.ApplicationModel;

namespace Ridder.Hosting.Dokploy;

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