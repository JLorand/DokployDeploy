using System.Text.Json.Serialization;

namespace Ridder.Hosting.Dokploy;

internal partial class DokployApi
{
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
        public List<Environment> Environments { get; init; } = [];
    }

    public class Compose
    {
        [JsonPropertyName("composeId")]
        public string? Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("domains")]
        public List<Domain> Domains { get; init; } = [];

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
        public List<Compose> Compose { get; init; } = [];

        [JsonPropertyName("applications")]
        public List<Application> Applications { get; init; } = [];
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

        [JsonPropertyName("username")]
        public string? Username { get; init; }

        [JsonPropertyName("password")]
        public string? Password { get; init; }
    }

    internal sealed class RemoteRegistry
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

    public class Mount
    {
        [JsonPropertyName("mountId")]
        public string? Id { get; init; }

        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("hostPath")]
        public string? HostPath { get; init; }

        [JsonPropertyName("volumeName")]
        public string? VolumeName { get; init; }

        [JsonPropertyName("mountPath")]
        public string MountPath { get; init; } = string.Empty;

        [JsonPropertyName("serviceType")]
        public string? ServiceType { get; init; }
    }

    internal sealed class TrpcEnvelope<T>
    {
        [JsonPropertyName("result")]
        public TrpcResult<T>? Result { get; init; }
    }

    internal sealed class TrpcResult<T>
    {
        [JsonPropertyName("data")]
        public TrpcData<T>? Data { get; init; }
    }

    internal sealed class TrpcData<T>
    {
        [JsonPropertyName("json")]
        public T? Json { get; init; }
    }

    internal sealed class GeneratedDomainData
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