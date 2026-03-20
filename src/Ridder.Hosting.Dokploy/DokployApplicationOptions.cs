namespace Ridder.Hosting.Dokploy;

public sealed class DokployApplicationOptions
{
    public string? ApplicationName { get; set; }
    public bool ConfigureEnvironmentVariables { get; set; } = true;
    public bool ConfigureMounts { get; set; } = true;
    public bool CreateDomainsForExternalEndpoints { get; set; } = true;

    internal DokployApplicationOptions Clone() => new()
    {
        ApplicationName = ApplicationName,
        ConfigureEnvironmentVariables = ConfigureEnvironmentVariables,
        ConfigureMounts = ConfigureMounts,
        CreateDomainsForExternalEndpoints = CreateDomainsForExternalEndpoints
    };
}