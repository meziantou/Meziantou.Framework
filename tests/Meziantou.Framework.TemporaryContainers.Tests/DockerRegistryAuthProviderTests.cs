using System.Buffers.Text;
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

    [Fact]
    public async Task GetRegistryAuthHeaderValueAsync_EncodesTheHeaderWithBase64Url()
    {
        // This password makes the standard base64 alphabet emit '+' or '/', which base64url rejects.
        var config = new DockerApiModels.AuthConfigFile
        {
            Auths = new Dictionary<string, DockerApiModels.AuthEntry>(StringComparer.Ordinal)
            {
                ["https://index.docker.io/v1/"] = new DockerApiModels.AuthEntry
                {
                    Username = "john",
                    Password = "abc>>>def",
                },
            },
        };

        var provider = new DockerRegistryAuthProvider(overrideConfiguration: config);
        var header = await provider.GetRegistryAuthHeaderValueAsync("redis:8", CancellationToken.None);

        Assert.NotNull(header);
        Assert.DoesNotContain('+', header);
        Assert.DoesNotContain('/', header);
        Assert.Equal("abc>>>def", DecodeHeader(header).Password);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    public async Task GetRegistryAuthHeaderValueAsync_PadsTheHeader(string password)
    {
        // The daemon decodes the header with Go's padded base64url decoder, which drops a last block that has no padding.
        // One password in three makes a JSON payload whose length is a multiple of 3, the others need padding.
        var config = new DockerApiModels.AuthConfigFile
        {
            Auths = new Dictionary<string, DockerApiModels.AuthEntry>(StringComparer.Ordinal)
            {
                ["https://index.docker.io/v1/"] = new DockerApiModels.AuthEntry { Username = "john", Password = password },
            },
        };

        var provider = new DockerRegistryAuthProvider(overrideConfiguration: config);
        var header = await provider.GetRegistryAuthHeaderValueAsync("redis:8", CancellationToken.None);

        Assert.NotNull(header);
        Assert.Equal(0, header.Length % 4);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(header.Replace('-', '+').Replace('_', '/')));
        Assert.Equal(password, JsonSerializer.Deserialize(json, DockerApiJsonContext.Default.RegistryAuthHeader)!.Password);
    }

    [Fact]
    public async Task GetRegistryAuthHeaderValueAsync_SendsTheIdentityTokenOfATokenLogin()
    {
        var config = new DockerApiModels.AuthConfigFile
        {
            Auths = new Dictionary<string, DockerApiModels.AuthEntry>(StringComparer.Ordinal)
            {
                ["myregistry.azurecr.io"] = new DockerApiModels.AuthEntry
                {
                    Auth = Convert.ToBase64String(Encoding.UTF8.GetBytes("00000000-0000-0000-0000-000000000000:")),
                    IdentityToken = "refresh-token",
                },
            },
        };

        var provider = new DockerRegistryAuthProvider(overrideConfiguration: config);
        var header = await provider.GetRegistryAuthHeaderValueAsync("myregistry.azurecr.io/app:1", CancellationToken.None);

        Assert.NotNull(header);
        Assert.Equal("refresh-token", DecodeHeader(header).IdentityToken);
    }

    [Fact]
    public async Task GetRegistryAuthHeaderValueAsync_IgnoresAnAuthThatIsNotBase64()
    {
        var config = new DockerApiModels.AuthConfigFile
        {
            Auths = new Dictionary<string, DockerApiModels.AuthEntry>(StringComparer.Ordinal)
            {
                ["https://index.docker.io/v1/"] = new DockerApiModels.AuthEntry { Auth = "not base64!", Username = "john", Password = "secret" },
            },
        };

        var provider = new DockerRegistryAuthProvider(overrideConfiguration: config);
        var header = await provider.GetRegistryAuthHeaderValueAsync("redis:8", CancellationToken.None);

        Assert.NotNull(header);
        Assert.Equal("john", DecodeHeader(header).Username);
    }

    [Fact]
    public void CreateHelperCredentials_SendsTheSecretOfATokenLoginAsAnIdentityToken()
    {
        var credentials = DockerRegistryAuthProvider.CreateHelperCredentials("myregistry.azurecr.io", new DockerApiModels.CredentialHelperGetResponse { Username = "<token>", Secret = "refresh-token" });

        Assert.NotNull(credentials);
        Assert.Null(credentials.Username);
        Assert.Null(credentials.Password);
        Assert.Equal("refresh-token", credentials.IdentityToken);
    }

    [Fact]
    public void CreateHelperCredentials_SendsAUsernameAndAPassword()
    {
        var credentials = DockerRegistryAuthProvider.CreateHelperCredentials("index.docker.io", new DockerApiModels.CredentialHelperGetResponse { Username = "john", Secret = "secret" });

        Assert.NotNull(credentials);
        Assert.Equal("john", credentials.Username);
        Assert.Equal("secret", credentials.Password);
        Assert.Equal("https://index.docker.io/v1/", credentials.ServerAddress);
    }

    [Fact]
    public async Task GetRegistryAuthHeaderValueAsync_ReturnsNullWhenTheCredentialHelperIsMissing()
    {
        var config = new DockerApiModels.AuthConfigFile
        {
            CredsStore = MissingHelperName(),
        };

        var provider = new DockerRegistryAuthProvider(overrideConfiguration: config);

        Assert.Null(await provider.GetRegistryAuthHeaderValueAsync("redis:8", CancellationToken.None));
    }

    [Fact]
    public async Task GetRegistryAuthHeaderValueAsync_FallsBackToAuthsWhenTheCredentialHelperIsMissing()
    {
        var config = new DockerApiModels.AuthConfigFile
        {
            CredsStore = MissingHelperName(),
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
        Assert.Equal("john", DecodeHeader(header!).Username);
    }

    /// <summary>A helper name that cannot resolve to an executable, so 'docker-credential-&lt;name&gt;' is guaranteed to be missing.</summary>
    private static string MissingHelperName() => "meziantou-tc-missing-" + Guid.NewGuid().ToString("N");

    [Theory]
    [InlineData("redis:8", "index.docker.io")]
    [InlineData("library/redis:8", "index.docker.io")]
    [InlineData("docker.io/library/redis:8", "index.docker.io")]
    [InlineData("index.docker.io/library/redis:8", "index.docker.io")]
    [InlineData("registry-1.docker.io/myorg/private:1", "index.docker.io")]
    [InlineData("registry.example.com/my/image:1", "registry.example.com")]
    [InlineData("localhost:5000/my/image:1", "localhost:5000")]
    public void GetRegistryFromImageName_ResolvesExpectedHost(string imageName, string expectedRegistry)
    {
        Assert.Equal(expectedRegistry, DockerRegistryAuthProvider.GetRegistryFromImageName(imageName));
    }

    [Fact]
    public void GetConfigurationDirectory_UsesDockerConfigWhenSet()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meziantou-tc-docker-config");

        Assert.Equal(FullPath.FromPath(directory), DockerRegistryAuthProvider.GetConfigurationDirectory(directory));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetConfigurationDirectory_FallsBackToTheUserProfileWhenDockerConfigIsNotSet(string? dockerConfig)
    {
        var expected = FullPath.GetFolderPath(Environment.SpecialFolder.UserProfile) / ".docker";

        Assert.Equal(expected, DockerRegistryAuthProvider.GetConfigurationDirectory(dockerConfig));
    }

    private static DockerApiModels.RegistryAuthHeader DecodeHeader(string value)
    {
        var json = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value));
        var payload = JsonSerializer.Deserialize(json, DockerApiJsonContext.Default.RegistryAuthHeader);
        return payload ?? throw new InvalidOperationException("Invalid Docker auth header payload.");
    }
}
