using System.Text;
using System.Text.Json;
using Meziantou.Framework;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class DockerRegistryAuthProvider
{
    private const string DockerHubRegistry = "index.docker.io";
    private readonly Lazy<DockerApiModels.AuthConfigFile?> _configuration;

    internal DockerRegistryAuthProvider()
        : this(overrideConfiguration: null)
    {
    }

    internal DockerRegistryAuthProvider(DockerApiModels.AuthConfigFile? overrideConfiguration)
    {
        _configuration = new Lazy<DockerApiModels.AuthConfigFile?>(() => overrideConfiguration ?? LoadConfiguration(), LazyThreadSafetyMode.ExecutionAndPublication);
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
            return firstSegment;
        }

        return DockerHubRegistry;
    }

    private static DockerApiModels.AuthConfigFile? LoadConfiguration()
    {
        var configPath = FullPath.GetFolderPath(Environment.SpecialFolder.UserProfile) / ".docker" / "config.json";
        if (!File.Exists(configPath))
            return null;

        using var stream = File.OpenRead(configPath);
        return JsonSerializer.Deserialize(stream, DockerApiJsonContext.Default.AuthConfigFile);
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

            if (!string.IsNullOrEmpty(authEntry.Auth))
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authEntry.Auth));
                var separator = decoded.IndexOf(':', StringComparison.Ordinal);
                if (separator > 0)
                {
                    credentials = new DockerApiModels.RegistryAuthHeader
                    {
                        ServerAddress = GetHelperServerAddress(registry),
                        Username = decoded[..separator],
                        Password = decoded[(separator + 1)..],
                    };
                    return true;
                }
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
            if (credentials?.Secret is null)
                return null;

            return new DockerApiModels.RegistryAuthHeader
            {
                ServerAddress = GetHelperServerAddress(registry),
                Username = credentials.Username,
                Password = credentials.Secret,
            };
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static string BuildRegistryAuthHeader(DockerApiModels.RegistryAuthHeader credentials)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(credentials, DockerApiJsonContext.Default.RegistryAuthHeader);
        return Convert.ToBase64String(bytes);
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

        return string.IsNullOrEmpty(normalized) ? DockerHubRegistry : normalized;
    }

    private static string GetHelperServerAddress(string registry)
    {
        return string.Equals(registry, DockerHubRegistry, StringComparison.OrdinalIgnoreCase)
            ? "https://index.docker.io/v1/"
            : registry;
    }
}
