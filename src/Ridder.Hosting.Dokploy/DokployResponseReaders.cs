using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Ridder.Hosting.Dokploy;

internal static class DokployResponseReaders
{
    internal static async Task<DokployApi.Project?> ReadProjectFromResponseAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        return TryExtractProject(json.RootElement);
    }

    internal static async Task<DokployApi.Project?> FindProjectByNameFromResponseAsync(HttpResponseMessage response, string expectedName)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        return FindProjectByName(json.RootElement, expectedName);
    }

    internal static async Task<DokployApi.Compose?> ReadComposeFromResponseAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        return TryExtractCompose(json.RootElement);
    }

    internal static async Task<List<DokployApi.RemoteRegistry>> ReadRegistriesFromResponseAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var output = new List<DokployApi.RemoteRegistry>();
        CollectRegistries(json.RootElement, output);
        return output;
    }

    internal static async Task<DokployApi.RemoteRegistry?> ReadRegistryFromResponseAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        return TryExtractRegistry(json.RootElement);
    }

    internal static async Task<DokployApi.Application?> ReadApplicationFromResponseAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        return TryExtractApplication(json.RootElement);
    }

    internal static async Task<List<DokployApi.Mount>> ReadMountsFromResponseAsync(HttpResponseMessage response, ILogger? logger = null, string source = "mounts.allNamedByApplicationId")
    {
        var content = DokployJsonPayload.Normalize(await response.Content.ReadAsStringAsync());
        logger?.LogInformation("{MountSource} payload: {Payload}", source, DokployJsonPayload.GetPayloadSnippet(content));

        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        using var root = JsonDocument.Parse(content);
        logger?.LogInformation("{MountSource} root kind: {RootKind}", source, root.RootElement.ValueKind);
        if (root.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (TryDeserializeMount(root.RootElement, out var direct))
            {
                return [direct!];
            }

            var wrapped = JsonSerializer.Deserialize<DokployApi.TrpcEnvelope<List<DokployApi.Mount>>>(content, DokployApi.JsonOptions);
            var wrappedMounts = wrapped?.Result?.Data?.Json?.Where(m => !string.IsNullOrWhiteSpace(m.MountPath)).ToList();
            if (wrappedMounts is { Count: > 0 })
            {
                return wrappedMounts;
            }

            var wrappedSingle = JsonSerializer.Deserialize<DokployApi.TrpcEnvelope<DokployApi.Mount>>(content, DokployApi.JsonOptions);
            var single = wrappedSingle?.Result?.Data?.Json;
            if (single is not null && !string.IsNullOrWhiteSpace(single.MountPath))
            {
                return [single];
            }

            return [];
        }

        var directList = JsonSerializer.Deserialize<List<DokployApi.Mount>>(content, DokployApi.JsonOptions);
        if (directList is { Count: > 0 })
        {
            return directList.Where(m => !string.IsNullOrWhiteSpace(m.MountPath)).ToList();
        }

        var wrappedList = JsonSerializer.Deserialize<List<DokployApi.TrpcEnvelope<List<DokployApi.Mount>>>>(content, DokployApi.JsonOptions);
        if (wrappedList is { Count: > 0 })
        {
            var mounts = wrappedList.SelectMany(x => x.Result?.Data?.Json ?? []).Where(m => !string.IsNullOrWhiteSpace(m.MountPath)).ToList();
            if (mounts.Count > 0)
            {
                return mounts;
            }
        }

        var wrappedSingleList = JsonSerializer.Deserialize<List<DokployApi.TrpcEnvelope<DokployApi.Mount>>>(content, DokployApi.JsonOptions);
        if (wrappedSingleList is { Count: > 0 })
        {
            var mounts = wrappedSingleList.Select(x => x.Result?.Data?.Json).Where(m => m is not null && !string.IsNullOrWhiteSpace(m.MountPath)).Select(m => m!).ToList();
            if (mounts.Count > 0)
            {
                return mounts;
            }
        }

        return [];
    }

    internal static async Task<List<DokployApi.Domain>> ReadDomainsFromResponseAsync(HttpResponseMessage response, ILogger? logger = null, string source = "domain.byApplicationId")
    {
        var content = DokployJsonPayload.Normalize(await response.Content.ReadAsStringAsync());
        logger?.LogInformation("{DomainSource} payload: {Payload}", source, DokployJsonPayload.GetPayloadSnippet(content));

        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        using var root = JsonDocument.Parse(content);
        logger?.LogInformation("{DomainSource} root kind: {RootKind}", source, root.RootElement.ValueKind);
        if (root.RootElement.ValueKind == JsonValueKind.Object)
        {
            var direct = JsonSerializer.Deserialize<DokployApi.Domain>(content, DokployApi.JsonOptions);
            if (direct is not null && !string.IsNullOrWhiteSpace(direct.Host))
            {
                return [direct];
            }

            var wrapped = JsonSerializer.Deserialize<DokployApi.TrpcEnvelope<List<DokployApi.Domain>>>(content, DokployApi.JsonOptions);
            var wrappedDomains = wrapped?.Result?.Data?.Json?.Where(d => !string.IsNullOrWhiteSpace(d.Host)).ToList();
            if (wrappedDomains is { Count: > 0 })
            {
                return wrappedDomains;
            }

            var wrappedSingle = JsonSerializer.Deserialize<DokployApi.TrpcEnvelope<DokployApi.Domain>>(content, DokployApi.JsonOptions);
            var single = wrappedSingle?.Result?.Data?.Json;
            if (single is not null && !string.IsNullOrWhiteSpace(single.Host))
            {
                return [single];
            }

            return [];
        }

        var directList = JsonSerializer.Deserialize<List<DokployApi.Domain>>(content, DokployApi.JsonOptions);
        if (directList is { Count: > 0 })
        {
            return directList.Where(d => !string.IsNullOrWhiteSpace(d.Host)).ToList();
        }

        var wrappedList = JsonSerializer.Deserialize<List<DokployApi.TrpcEnvelope<List<DokployApi.Domain>>>>(content, DokployApi.JsonOptions);
        if (wrappedList is { Count: > 0 })
        {
            var domains = wrappedList.SelectMany(x => x.Result?.Data?.Json ?? []).Where(d => !string.IsNullOrWhiteSpace(d.Host)).ToList();
            if (domains.Count > 0)
            {
                return domains;
            }
        }

        var wrappedSingleList = JsonSerializer.Deserialize<List<DokployApi.TrpcEnvelope<DokployApi.Domain>>>(content, DokployApi.JsonOptions);
        if (wrappedSingleList is { Count: > 0 })
        {
            var domains = wrappedSingleList.Select(x => x.Result?.Data?.Json).Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Host)).Select(d => d!).ToList();
            if (domains.Count > 0)
            {
                return domains;
            }
        }

        return [];
    }

    internal static async Task<string?> ReadGeneratedHostFromResponseAsync(HttpResponseMessage response, ILogger? logger = null)
    {
        var content = DokployJsonPayload.Normalize(await response.Content.ReadAsStringAsync());
        logger?.LogInformation("domain.generateDomain payload: {Payload}", DokployJsonPayload.GetPayloadSnippet(content));

        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        using var root = JsonDocument.Parse(content);
        logger?.LogInformation("domain.generateDomain root kind: {RootKind}", root.RootElement.ValueKind);
        if (root.RootElement.ValueKind == JsonValueKind.String)
        {
            return root.RootElement.GetString();
        }

        if (root.RootElement.ValueKind == JsonValueKind.Object)
        {
            string? fromWrappedString = null;
            try
            {
                var wrappedString = JsonSerializer.Deserialize<DokployApi.TrpcEnvelope<string>>(content, DokployApi.JsonOptions);
                fromWrappedString = wrappedString?.Result?.Data?.Json;
            }
            catch (JsonException)
            {
            }

            if (!string.IsNullOrWhiteSpace(fromWrappedString))
            {
                return fromWrappedString;
            }

            var wrappedData = JsonSerializer.Deserialize<DokployApi.TrpcEnvelope<DokployApi.GeneratedDomainData>>(content, DokployApi.JsonOptions);
            var fromWrappedData = wrappedData?.Result?.Data?.Json;
            if (fromWrappedData is not null)
            {
                var host = fromWrappedData.Json ?? fromWrappedData.Host ?? fromWrappedData.Domain;
                if (!string.IsNullOrWhiteSpace(host))
                {
                    return host;
                }
            }

            var directData = JsonSerializer.Deserialize<DokployApi.GeneratedDomainData>(content, DokployApi.JsonOptions);
            return directData?.Json ?? directData?.Host ?? directData?.Domain;
        }

        return null;
    }

    private static DokployApi.Project? TryExtractProject(JsonElement root)
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

    private static bool TryDeserializeProject(JsonElement value, out DokployApi.Project? project)
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

        project = value.Deserialize<DokployApi.Project>(DokployApi.JsonOptions);
        return project is not null;
    }

    private static DokployApi.Project? FindProjectByName(JsonElement value, string expectedName)
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

    private static DokployApi.Compose? TryExtractCompose(JsonElement root)
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

    private static bool TryDeserializeCompose(JsonElement value, out DokployApi.Compose? compose)
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

        compose = value.Deserialize<DokployApi.Compose>(DokployApi.JsonOptions);
        return compose is not null;
    }

    private static DokployApi.RemoteRegistry? TryExtractRegistry(JsonElement element)
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

    private static void CollectRegistries(JsonElement element, List<DokployApi.RemoteRegistry> output)
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

    private static bool TryDeserializeRegistry(JsonElement value, out DokployApi.RemoteRegistry registry)
    {
        registry = new DokployApi.RemoteRegistry();
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!value.TryGetProperty("registryUrl", out _))
        {
            return false;
        }

        var candidate = value.Deserialize<DokployApi.RemoteRegistry>(DokployApi.JsonOptions);
        if (candidate is null)
        {
            return false;
        }

        registry = candidate;
        return true;
    }

    private static DokployApi.Application? TryExtractApplication(JsonElement root)
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

    private static bool TryDeserializeApplication(JsonElement value, out DokployApi.Application? application)
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

        application = value.Deserialize<DokployApi.Application>(DokployApi.JsonOptions);
        return application is not null;
    }

    private static bool TryDeserializeMount(JsonElement value, out DokployApi.Mount? mount)
    {
        mount = null;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!value.TryGetProperty("mountPath", out _) && !value.TryGetProperty("mountId", out _))
        {
            return false;
        }

        mount = value.Deserialize<DokployApi.Mount>(DokployApi.JsonOptions);
        return mount is not null && !string.IsNullOrWhiteSpace(mount.MountPath);
    }
}