using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;

namespace Ridder.Hosting.Dokploy;

internal partial class DokployApi : IDisposable
{
    private readonly IHostEnvironment env;
    private readonly ILogger logger;
    private readonly DokployResolvedRegistrySettings registrySettings;
    private readonly HttpClient http;

    internal DokployApi(string apiKey, string url, IHostEnvironment env, ILogger logger, DokployResolvedRegistrySettings registrySettings)
    {
        this.env = env;
        this.logger = logger;
        this.registrySettings = registrySettings;

        var baseUrl = url.EndsWith("/", StringComparison.Ordinal) ? url : $"{url}/";
        http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromMinutes(5)
        };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        http.DefaultRequestHeaders.Add("x-api-key", apiKey);
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public void Dispose()
    {
        http.Dispose();
        GC.SuppressFinalize(this);
    }

    private static StringContent CreateJsonContent(string body)
    {
        return new StringContent(body, Encoding.UTF8, MediaTypeHeaderValue.Parse("application/json"));
    }
}