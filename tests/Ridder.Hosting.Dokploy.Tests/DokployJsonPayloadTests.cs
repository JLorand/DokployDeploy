using System.Net;
using System.Text;
using System.Text.Json;
using Ridder.Hosting.Dokploy.Models;
using Ridder.Hosting.Dokploy.Services;
using Ridder.Hosting.Dokploy.Utilities;

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
    var compose = new DokployCompose
    {
      Name = "registry",
      Env = "REGISTRY_HTPASSWD_USERNAME=docker\nREGISTRY_HTPASSWD_PASSWORD=supersecret"
    };

    var username = DokployRegistryService.GetRegistryUsernameFromCompose(compose);

    Assert.Equal("docker", username);
  }

  [Fact]
  public void GetRegistryPasswordFromCompose_ReadsEnvBackedPassword()
  {
    var compose = new DokployCompose
    {
      Name = "registry",
      Env = "REGISTRY_HTPASSWD_USERNAME=docker\nREGISTRY_HTPASSWD_PASSWORD=supersecret"
    };

    var password = DokployRegistryService.GetRegistryPasswordFromCompose(compose);

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

  [Fact]
  public async Task ReadMountsFromResponseAsync_ReadsNestedMountsFromWrappedPayload()
  {
    using var response = new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StringContent("""
        {
          "result": {
            "data": {
              "json": {
                "mounts": [
                  {
                    "mountId": "m-1",
                    "type": "volume-mount",
                    "volumeName": "cache-data",
                    "mountPath": "/data"
                  }
                ]
              }
            }
          }
        }
        """, Encoding.UTF8, "application/json")
    };

    var mounts = await DokployResponseReaders.ReadMountsFromResponseAsync(response);

    var mount = Assert.Single(mounts);
    Assert.Equal("m-1", mount.Id);
    Assert.Equal("cache-data", mount.VolumeName);
    Assert.Equal("/data", mount.MountPath);
  }

  [Fact]
  public async Task ReadMountsFromResponseAsync_ReadsDockerStyleMountPayload()
  {
    using var response = new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StringContent("""
        [
          {
            "Type": "volume",
            "Name": "cache-data",
            "Source": "/var/lib/docker/volumes/cache-data/_data",
            "Destination": "/data",
            "Driver": "local",
            "Mode": "z",
            "RW": true,
            "Propagation": ""
          }
        ]
        """, Encoding.UTF8, "application/json")
    };

    var mounts = await DokployResponseReaders.ReadMountsFromResponseAsync(response);

    var mount = Assert.Single(mounts);
    Assert.Equal("volume", mount.Type);
    Assert.Equal("cache-data", mount.VolumeName);
    Assert.Equal("/data", mount.MountPath);
  }
}