using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;

namespace Ridder.Hosting.Dokploy;

internal partial class DokployApi
{
    private readonly string apiKey;
    private readonly string url;
    private readonly IHostEnvironment env;
    private readonly ILogger logger;
    private readonly DokployResolvedRegistrySettings registrySettings;

    internal DokployApi(string apiKey, string url, IHostEnvironment env, ILogger logger, DokployResolvedRegistrySettings registrySettings)
    {
        this.apiKey = apiKey;
        this.url = url;
        this.env = env;
        this.logger = logger;
        this.registrySettings = registrySettings;
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

    private static StringContent CreateJsonContent(string body)
    {
        return new StringContent(body, Encoding.UTF8, MediaTypeHeaderValue.Parse("application/json"));
    }
}