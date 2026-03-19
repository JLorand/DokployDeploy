using Ridder.Hosting.Dokploy;

namespace Ridder.Hosting.Dokploy.Tests;

public class DokployRegistrySettingsTests
{
    [Fact]
    public async Task CreateSelfHosted_WithExplicitValues_ResolvesExpectedSettings()
    {
        var settings = DokployRegistrySettings.CreateSelfHosted("registry.example.com", "docker", "password");

        var resolved = await settings.ResolveAsync(CancellationToken.None);

        Assert.Equal(DokployRegistryMode.SelfHosted, resolved.Mode);
        Assert.Equal("registry.example.com", resolved.RegistryUrl);
        Assert.Equal("docker", resolved.Username);
        Assert.Equal("password", resolved.Password);
        Assert.Equal("cloud", resolved.RegistryType);
    }

    [Fact]
    public async Task CreateHosted_WithExplicitValues_ResolvesExpectedSettings()
    {
        var settings = DokployRegistrySettings.CreateHosted("ghcr.io", "octocat", "token");

        var resolved = await settings.ResolveAsync(CancellationToken.None);

        Assert.Equal(DokployRegistryMode.Hosted, resolved.Mode);
        Assert.Equal("ghcr.io", resolved.RegistryUrl);
        Assert.Equal("octocat", resolved.Username);
        Assert.Equal("token", resolved.Password);
        Assert.Equal("cloud", resolved.RegistryType);
    }

    [Theory]
    [InlineData(null, "user", "password", "Registry URL is required.")]
    [InlineData("", "user", "password", "Registry URL is required.")]
    [InlineData("registry.example.com", null, "password", "Registry username is required.")]
    [InlineData("registry.example.com", "", "password", "Registry username is required.")]
    [InlineData("registry.example.com", "user", null, "Registry password is required.")]
    [InlineData("registry.example.com", "user", "", "Registry password is required.")]
    public async Task CreateHosted_WithMissingRequiredValue_Throws(
        string? registryUrl,
        string? username,
        string? password,
        string expectedMessage)
    {
        var settings = DokployRegistrySettings.CreateHosted(registryUrl ?? string.Empty, username ?? string.Empty, password ?? string.Empty);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => settings.ResolveAsync(CancellationToken.None));

        Assert.Equal(expectedMessage, exception.Message);
    }
}