using System.Text;
using System.Text.Json;
using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class DockerRegistryAuthProviderTests
{
    [Fact]
    public async Task GetRegistryAuthHeaderValueAsync_UsesAuthsForDockerHub()
    {
        var config = new DockerApiModels.AuthConfigFile
        {
            Auths = new Dictionary<string, DockerApiModels.AuthEntry>(StringComparer.Ordinal)
            {
                ["https://index.docker.io/v1/"] = new DockerApiModels.AuthEntry
                {
                    Auth = Convert.ToBase64String(Encoding.UTF8.GetBytes("john:secret")),
                },
            },
        };

        var provider = new DockerRegistryAuthProvider(overrideConfiguration: config);
        var header = await provider.GetRegistryAuthHeaderValueAsync("redis:8", CancellationToken.None);

        Assert.NotNull(header);
        var payload = DecodeHeader(header!);
        Assert.Equal("john", payload.Username);
        Assert.Equal("secret", payload.Password);
        Assert.Equal("https://index.docker.io/v1/", payload.ServerAddress);
    }

    [Fact]
    public async Task GetRegistryAuthHeaderValueAsync_UsesAuthsForCustomRegistry()
    {
        var config = new DockerApiModels.AuthConfigFile
        {
            Auths = new Dictionary<string, DockerApiModels.AuthEntry>(StringComparer.Ordinal)
            {
                ["https://registry.example.com/v2/"] = new DockerApiModels.AuthEntry
                {
                    Username = "alice",
                    Password = "password",
                },
            },
        };

        var provider = new DockerRegistryAuthProvider(overrideConfiguration: config);
        var header = await provider.GetRegistryAuthHeaderValueAsync("registry.example.com/app/image:latest", CancellationToken.None);

        Assert.NotNull(header);
        var payload = DecodeHeader(header!);
        Assert.Equal("alice", payload.Username);
        Assert.Equal("password", payload.Password);
        Assert.Equal("registry.example.com", payload.ServerAddress);
    }

    [Theory]
    [InlineData("redis:8", "index.docker.io")]
    [InlineData("library/redis:8", "index.docker.io")]
    [InlineData("registry.example.com/my/image:1", "registry.example.com")]
    [InlineData("localhost:5000/my/image:1", "localhost:5000")]
    public void GetRegistryFromImageName_ResolvesExpectedHost(string imageName, string expectedRegistry)
    {
        Assert.Equal(expectedRegistry, DockerRegistryAuthProvider.GetRegistryFromImageName(imageName));
    }

    private static DockerApiModels.RegistryAuthHeader DecodeHeader(string value)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(value));
        var payload = JsonSerializer.Deserialize(json, DockerApiJsonContext.Default.RegistryAuthHeader);
        return payload ?? throw new InvalidOperationException("Invalid Docker auth header payload.");
    }
}
