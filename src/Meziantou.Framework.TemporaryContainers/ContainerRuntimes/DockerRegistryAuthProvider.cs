using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Meziantou.Framework;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class DockerRegistryAuthProvider
{
    private const string DockerHubRegistry = "index.docker.io";
    private const string TokenUsername = "<token>";
    private readonly Lazy<DockerApiModels.AuthConfigFile?> _configuration;

    internal DockerRegistryAuthProvider()
        : this(overrideConfiguration: null)
    {
    }

    internal DockerRegistryAuthProvider(DockerApiModels.AuthConfigFile? overrideConfiguration)
    {
        // A failure to read the configuration is not kept: 'docker login' rewriting the file while it is read must not
        // break every pull for the rest of the process.
        _configuration = new Lazy<DockerApiModels.AuthConfigFile?>(() => overrideConfiguration ?? LoadConfiguration(), LazyThreadSafetyMode.PublicationOnly);
    }

    public async Task<string?> GetRegistryAuthHeaderValueAsync(string imageName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);

        var configuration = _configuration.Value;
        if (configuration is null)
            return null;

        var registry = GetRegistryFromImageName(imageName);
        var helper = GetCredentialHelper(configuration, registry);
        if (!string.IsNullOrEmpty(helper))
        {
            var credentials = await ResolveCredentialsFromHelperAsync(registry, helper, cancellationToken).ConfigureAwait(false);
            if (credentials is not null)
                return BuildRegistryAuthHeader(credentials);
        }

        if (TryGetCredentialsFromAuths(configuration, registry, out var authCredentials))
            return BuildRegistryAuthHeader(authCredentials);

        return null;
    }

    internal static string GetRegistryFromImageName(string imageName)
    {
        var slashIndex = imageName.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex <= 0)
            return DockerHubRegistry;

        var firstSegment = imageName[..slashIndex];
        if (firstSegment.Contains('.', StringComparison.Ordinal) ||
            firstSegment.Contains(':', StringComparison.Ordinal) ||
            string.Equals(firstSegment, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeDockerHub(firstSegment);
        }

        return DockerHubRegistry;
    }

    private static DockerApiModels.AuthConfigFile? LoadConfiguration()
    {
        var configPath = GetConfigurationDirectory(Environment.GetEnvironmentVariable("DOCKER_CONFIG")) / "config.json";
        if (!File.Exists(configPath))
            return null;

        using var stream = File.OpenRead(configPath);
        return JsonSerializer.Deserialize(stream, DockerApiJsonContext.Default.AuthConfigFile);
    }

    /// <summary>The directory the credentials are read from: <c>DOCKER_CONFIG</c> when it is set, <c>~/.docker</c> otherwise. CI systems that isolate credentials into a per-job directory rely on the environment variable.</summary>
    internal static FullPath GetConfigurationDirectory(string? dockerConfigEnvironmentVariable)
    {
        return string.IsNullOrWhiteSpace(dockerConfigEnvironmentVariable)
            ? FullPath.GetFolderPath(Environment.SpecialFolder.UserProfile) / ".docker"
            : FullPath.FromPath(dockerConfigEnvironmentVariable);
    }

    private static string? GetCredentialHelper(DockerApiModels.AuthConfigFile config, string registry)
    {
        if (config.CredHelpers is { Count: > 0 })
        {
            foreach (var (key, value) in config.CredHelpers)
            {
                if (string.Equals(NormalizeRegistry(key), registry, StringComparison.OrdinalIgnoreCase))
                    return value;
            }
        }

        return config.CredsStore;
    }

    private static bool TryGetCredentialsFromAuths(DockerApiModels.AuthConfigFile config, string registry, out DockerApiModels.RegistryAuthHeader credentials)
    {
        if (config.Auths is not { Count: > 0 })
        {
            credentials = null!;
            return false;
        }

        foreach (var (registryKey, authEntry) in config.Auths)
        {
            if (!string.Equals(NormalizeRegistry(registryKey), registry, StringComparison.OrdinalIgnoreCase))
                continue;

            // 'auth' holds the username and the password, encoded. An entry can also carry an identity token (a token
            // login), which the daemon uses instead of the password, so it is always sent along.
            if (TryDecodeAuth(authEntry.Auth, out var username, out var password))
            {
                credentials = new DockerApiModels.RegistryAuthHeader
                {
                    ServerAddress = GetHelperServerAddress(registry),
                    Username = username,
                    Password = password,
                    IdentityToken = authEntry.IdentityToken,
                };
                return true;
            }

            if (!string.IsNullOrEmpty(authEntry.Username) || !string.IsNullOrEmpty(authEntry.IdentityToken))
            {
                credentials = new DockerApiModels.RegistryAuthHeader
                {
                    ServerAddress = GetHelperServerAddress(registry),
                    Username = authEntry.Username,
                    Password = authEntry.Password,
                    IdentityToken = authEntry.IdentityToken,
                };
                return true;
            }
        }

        credentials = null!;
        return false;
    }

    private static async Task<DockerApiModels.RegistryAuthHeader?> ResolveCredentialsFromHelperAsync(string registry, string helperName, CancellationToken cancellationToken)
    {
        var helperExecutable = "docker-credential-" + helperName;
        try
        {
            var result = await ProcessWrapper.Create(helperExecutable)
                .WithArguments(["get"])
                .WithValidation(ProcessValidationMode.None)
                .WithInputStream(InputSource.FromText(GetHelperServerAddress(registry) + "\n"))
                .ExecuteBufferedAsync(cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
                return null;

            var output = string.Join('\n', result.Output.StandardOutput.Select(item => item.Text));
            var credentials = JsonSerializer.Deserialize(output, DockerApiJsonContext.Default.CredentialHelperGetResponse);
            return credentials is null ? null : CreateHelperCredentials(registry, credentials);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or JsonException)
        {
            // The helper is an optimization: a config naming one that is not installed (a config copied between
            // machines, or Docker Desktop uninstalled but its config left behind) must fall through to 'auths'
            // rather than fail the pull. Starting a missing executable raises Win32Exception, not FileNotFoundException.
            return null;
        }
    }

    /// <summary>Turns the answer of a credential helper into the credentials the daemon expects.</summary>
    internal static DockerApiModels.RegistryAuthHeader? CreateHelperCredentials(string registry, DockerApiModels.CredentialHelperGetResponse credentials)
    {
        if (credentials.Secret is null)
            return null;

        // A helper answers a token login ('az acr login', for instance) with the username '<token>'. The secret is then an
        // identity token, which the daemon rejects as a password.
        if (string.Equals(credentials.Username, TokenUsername, StringComparison.Ordinal))
        {
            return new DockerApiModels.RegistryAuthHeader
            {
                ServerAddress = GetHelperServerAddress(registry),
                IdentityToken = credentials.Secret,
            };
        }

        return new DockerApiModels.RegistryAuthHeader
        {
            ServerAddress = GetHelperServerAddress(registry),
            Username = credentials.Username,
            Password = credentials.Secret,
        };
    }

    private static string BuildRegistryAuthHeader(DockerApiModels.RegistryAuthHeader credentials)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(credentials, DockerApiJsonContext.Default.RegistryAuthHeader);

        // The daemon decodes X-Registry-Auth with Go's padded base64url encoding: '+' and '/' from the standard alphabet
        // are rejected, and so is a value without its '=' padding, whose last block the decoder drops.
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_');
    }

    private static string NormalizeRegistry(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DockerHubRegistry;

        var normalized = value.Trim();
        if (normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["https://".Length..];
        else if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["http://".Length..];

        var slashIndex = normalized.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex >= 0)
            normalized = normalized[..slashIndex];

        return string.IsNullOrEmpty(normalized) ? DockerHubRegistry : NormalizeDockerHub(normalized);
    }

    /// <summary>Docker Hub answers to several names, and the docker CLI stores its credentials under 'https://index.docker.io/v1/' whichever one the image uses.</summary>
    private static string NormalizeDockerHub(string registry)
        => registry.ToUpperInvariant() is "DOCKER.IO" or "INDEX.DOCKER.IO" or "REGISTRY-1.DOCKER.IO" ? DockerHubRegistry : registry;

    private static bool TryDecodeAuth(string? auth, out string username, out string password)
    {
        username = "";
        password = "";
        if (string.IsNullOrEmpty(auth))
            return false;

        var buffer = new byte[auth.Length];
        if (!Convert.TryFromBase64String(auth, buffer, out var written))
            return false;

        var decoded = Encoding.UTF8.GetString(buffer, 0, written);
        var separator = decoded.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
            return false;

        username = decoded[..separator];
        password = decoded[(separator + 1)..];
        return true;
    }

    private static string GetHelperServerAddress(string registry)
    {
        return string.Equals(registry, DockerHubRegistry, StringComparison.OrdinalIgnoreCase)
            ? "https://index.docker.io/v1/"
            : registry;
    }
}
