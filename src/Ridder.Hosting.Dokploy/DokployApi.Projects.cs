using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Ridder.Hosting.Dokploy;

internal partial class DokployApi
{
    public async Task<Project> GetProjectOrCreateAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Project id/name must be provided.", nameof(name));
        }

        using var http = CreateHttpClient();
        using var allResponse = await http.GetAsync("api/project.all");

        if (allResponse.IsSuccessStatusCode)
        {
            var existing = await DokployResponseReaders.FindProjectByNameFromResponseAsync(allResponse, name);
            if (existing is not null)
            {
                logger.LogInformation("Project {ProjectName} already exists with id {ProjectId}.", existing.Name, existing.Id);
                return existing;
            }
        }

        logger.LogInformation("Project {ProjectName} not found. Creating new project.", name);

        var createBody = JsonSerializer.Serialize(new
        {
            name,
            description = "Project created from Aspire hosting environment.",
            env = env.EnvironmentName
        }, JsonOptions);

        using var createResponse = await http.PostAsync("api/project.create", CreateJsonContent(createBody));
        logger.LogInformation("Create project response: {StatusCode} - {ReasonPhrase}", createResponse.StatusCode, createResponse.ReasonPhrase);
        createResponse.EnsureSuccessStatusCode();

        return await DokployResponseReaders.ReadProjectFromResponseAsync(createResponse)
            ?? throw new InvalidOperationException($"Dokploy returned success for project '{name}', but no project payload was found.");
    }
}