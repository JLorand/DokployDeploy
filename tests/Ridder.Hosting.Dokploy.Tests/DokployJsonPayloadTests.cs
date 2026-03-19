using System.Net;
using System.Text;
using System.Text.Json;
using Ridder.Hosting.Dokploy;

namespace Ridder.Hosting.Dokploy.Tests;

public class DokployJsonPayloadTests
{
    [Fact]
    public void Normalize_UnwrapsSerializedJsonString()
    {
        var payload = "\"{\\\"host\\\":\\\"dokploy.example.com\\\"}\"";

        var normalized = DokployJsonPayload.Normalize(payload);

        Assert.Equal("{\"host\":\"dokploy.example.com\"}", normalized);
    }

    [Fact]
    public void ExtractLinkState_ReturnsTrueForNestedSuccess()
    {
        using var document = JsonDocument.Parse("""
            {
              "result": {
                "data": {
                  "success": true
                }
              }
            }
            """);

        var linked = DokployJsonPayload.ExtractLinkState(document.RootElement);

        Assert.True(linked);
    }

    [Fact]
    public async Task ReadGeneratedHostFromResponseAsync_ReadsWrappedHost()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "result": {
                    "data": {
                      "json": {
                        "host": "demo.example.com"
                      }
                    }
                  }
                }
                """, Encoding.UTF8, "application/json")
        };

        var host = await DokployResponseReaders.ReadGeneratedHostFromResponseAsync(response);

        Assert.Equal("demo.example.com", host);
    }

  [Fact]
  public void GetRegistryUsernameFromCompose_ReadsEnvBackedUsername()
  {
    var compose = new DokployApi.Compose
    {
      Name = "registry",
      Env = "REGISTRY_HTPASSWD_USERNAME=docker\nREGISTRY_HTPASSWD_PASSWORD=supersecret"
    };

    var username = DokployApi.GetRegistryUsernameFromCompose(compose);

    Assert.Equal("docker", username);
  }

  [Fact]
  public void GetRegistryPasswordFromCompose_ReadsEnvBackedPassword()
  {
    var compose = new DokployApi.Compose
    {
      Name = "registry",
      Env = "REGISTRY_HTPASSWD_USERNAME=docker\nREGISTRY_HTPASSWD_PASSWORD=supersecret"
    };

    var password = DokployApi.GetRegistryPasswordFromCompose(compose);

    Assert.Equal("supersecret", password);
  }

  [Fact]
  public async Task ReadMountsFromResponseAsync_ReturnsEmptyListForEmptyPayload()
  {
    using var response = new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
    };

    var mounts = await DokployResponseReaders.ReadMountsFromResponseAsync(response);

    Assert.Empty(mounts);
  }
}