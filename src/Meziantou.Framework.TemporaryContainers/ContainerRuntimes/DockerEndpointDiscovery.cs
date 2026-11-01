using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Finds the Docker daemon the way the docker CLI does: <c>DOCKER_HOST</c>, then <c>DOCKER_CONTEXT</c>, then the current context of <c>config.json</c>, then the sockets that the usual Docker distributions create.</summary>
/// <remarks>The environment, the home directory and the file system are injected so that the discovery can be tested without changing the machine.</remarks>
internal sealed class DockerEndpointDiscovery
{
    private const string DefaultContextName = "default";

    public required Func<string, string?> GetEnvironmentVariable { get; init; }

    public required FullPath HomeDirectory { get; init; }

    public required DockerHostPlatform Platform { get; init; }

    /// <summary>Whether a socket exists. <see cref="File.Exists(string?)"/> is true for a unix socket.</summary>
    public Func<FullPath, bool> SocketExists { get; init; } = path => File.Exists(path);

    public static DockerEndpointDiscovery CreateForCurrentProcess()
    {
        return new DockerEndpointDiscovery
        {
            GetEnvironmentVariable = Environment.GetEnvironmentVariable,
            HomeDirectory = FullPath.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Platform = OperatingSystem.IsWindows() ? DockerHostPlatform.Windows : OperatingSystem.IsMacOS() ? DockerHostPlatform.MacOS : DockerHostPlatform.Linux,
        };
    }

    /// <summary>The directory of <c>config.json</c> and of the contexts: <c>DOCKER_CONFIG</c> when it is set, <c>~/.docker</c> otherwise.</summary>
    public FullPath ConfigurationDirectory
    {
        get
        {
            var dockerConfig = GetEnvironmentVariable("DOCKER_CONFIG");
            return string.IsNullOrWhiteSpace(dockerConfig) ? HomeDirectory / ".docker" : FullPath.FromPath(dockerConfig);
        }
    }

    /// <summary>Lists the endpoints to try, in order. An explicit configuration (<c>DOCKER_HOST</c>, a context) yields a single endpoint, or none and the reason when it cannot be used: the docker CLI does not fall back to another daemon either.</summary>
    public DockerEndpointDiscoveryResult Discover()
    {
        var dockerHost = GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(dockerHost))
        {
            if (DockerApiTransport.TryParseDockerHost(dockerHost, GetTlsSettingsFromEnvironment(), out var endpoint, source: "DOCKER_HOST"))
                return new DockerEndpointDiscoveryResult([endpoint], []);

            return new DockerEndpointDiscoveryResult([], [$"DOCKER_HOST '{dockerHost}' is not supported: use unix://, npipe://, tcp://, http:// or https://."]);
        }

        var contextName = GetEnvironmentVariable("DOCKER_CONTEXT");
        var contextSource = "DOCKER_CONTEXT";
        if (string.IsNullOrWhiteSpace(contextName))
        {
            contextName = ReadCurrentContext(out var error);
            contextSource = "config.json";
            if (error is not null)
                return new DockerEndpointDiscoveryResult([], [error]);
        }

        if (!string.IsNullOrWhiteSpace(contextName) && !string.Equals(contextName, DefaultContextName, StringComparison.Ordinal))
        {
            if (TryLoadContext(contextName, contextSource, out var endpoint, out var error))
                return new DockerEndpointDiscoveryResult([endpoint], []);

            return new DockerEndpointDiscoveryResult([], [error]);
        }

        return GetDefaultEndpoints();
    }

    /// <summary>Reads the TLS configuration of the docker CLI: <c>DOCKER_TLS_VERIFY</c> turns on TLS and the verification of the daemon certificate, <c>DOCKER_TLS</c> turns on TLS alone. The certificates are read from <c>DOCKER_CERT_PATH</c>, the configuration directory by default.</summary>
    internal DockerApiTransport.TlsSettings? GetTlsSettingsFromEnvironment()
    {
        var verify = !string.IsNullOrEmpty(GetEnvironmentVariable("DOCKER_TLS_VERIFY"));
        if (!verify && string.IsNullOrEmpty(GetEnvironmentVariable("DOCKER_TLS")))
            return null;

        var certificateDirectory = GetEnvironmentVariable("DOCKER_CERT_PATH");
        var directory = string.IsNullOrWhiteSpace(certificateDirectory) ? ConfigurationDirectory : FullPath.FromPath(certificateDirectory);
        return new DockerApiTransport.TlsSettings(directory, verify);
    }

    private string? ReadCurrentContext(out string? error)
    {
        error = null;
        var configPath = ConfigurationDirectory / "config.json";
        if (!File.Exists(configPath))
            return null;

        try
        {
            using var stream = File.OpenRead(configPath);
            using var document = JsonDocument.Parse(stream, CreateDocumentOptions());
            if (document.RootElement.ValueKind is JsonValueKind.Object &&
                document.RootElement.TryGetProperty("currentContext", out var currentContext) &&
                currentContext.ValueKind is JsonValueKind.String)
            {
                return currentContext.GetString();
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = $"Cannot read the current docker context from '{configPath}': {ex.Message}";
            return null;
        }
    }

    private bool TryLoadContext(string contextName, string contextSource, [NotNullWhen(true)] out DockerApiTransport.Endpoint? endpoint, [NotNullWhen(false)] out string? error)
    {
        endpoint = null;
        var contextsDirectory = ConfigurationDirectory / "contexts";

        // The docker CLI stores a context in a directory named after the SHA-256 of its name.
        var contextDirectoryName = GetContextDirectoryName(contextName);
        var metaPath = contextsDirectory / "meta" / contextDirectoryName / "meta.json";
        if (!File.Exists(metaPath))
        {
            error = $"The docker context '{contextName}' (from {contextSource}) does not exist: '{metaPath}' was not found.";
            return false;
        }

        string? host;
        bool skipTlsVerify;
        try
        {
            using var stream = File.OpenRead(metaPath);
            using var document = JsonDocument.Parse(stream, CreateDocumentOptions());
            (host, skipTlsVerify) = ReadDockerEndpoint(document.RootElement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = $"Cannot read the docker context '{contextName}' from '{metaPath}': {ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            error = $"The docker context '{contextName}' (from {contextSource}) has no Docker endpoint.";
            return false;
        }

        // The context stores its own TLS material; the DOCKER_TLS* variables only apply to DOCKER_HOST.
        DockerApiTransport.TlsSettings? tls = null;
        var tlsDirectory = contextsDirectory / "tls" / contextDirectoryName / "docker";
        if (skipTlsVerify || File.Exists(tlsDirectory / "ca.pem") || File.Exists(tlsDirectory / "cert.pem"))
            tls = new DockerApiTransport.TlsSettings(tlsDirectory, VerifyServerCertificate: !skipTlsVerify);

        if (DockerApiTransport.TryParseDockerHost(host, tls, out endpoint, source: $"docker context '{contextName}'"))
        {
            error = null;
            return true;
        }

        error = $"The docker context '{contextName}' (from {contextSource}) uses '{host}', which the Docker Engine API runtime does not support: use unix://, npipe://, tcp://, http:// or https://.";
        return false;
    }

    private static (string? Host, bool SkipTlsVerify) ReadDockerEndpoint(JsonElement root)
    {
        if (root.ValueKind is JsonValueKind.Object &&
            root.TryGetProperty("Endpoints", out var endpoints) && endpoints.ValueKind is JsonValueKind.Object &&
            endpoints.TryGetProperty("docker", out var docker) && docker.ValueKind is JsonValueKind.Object)
        {
            var host = docker.TryGetProperty("Host", out var hostElement) && hostElement.ValueKind is JsonValueKind.String ? hostElement.GetString() : null;
            var skipTlsVerify = docker.TryGetProperty("SkipTLSVerify", out var skipElement) && skipElement.ValueKind is JsonValueKind.True;
            return (host, skipTlsVerify);
        }

        return (null, false);
    }

    internal static string GetContextDirectoryName(string contextName)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(contextName)));

    private DockerEndpointDiscoveryResult GetDefaultEndpoints()
    {
        if (Platform is DockerHostPlatform.Windows)
            return new DockerEndpointDiscoveryResult([DockerApiTransport.Endpoint.ForNamedPipe(".", "docker_engine", "default named pipe")], []);

        var endpoints = new List<DockerApiTransport.Endpoint>();
        var searched = new List<string>();
        foreach (var (path, source) in GetWellKnownSockets())
        {
            if (SocketExists(path))
            {
                if (!endpoints.Exists(endpoint => endpoint.UnixSocketPath == path))
                    endpoints.Add(DockerApiTransport.Endpoint.ForUnixSocket(path, source));
            }
            else
            {
                searched.Add(path.Value);
            }
        }

        if (endpoints.Count > 0)
            return new DockerEndpointDiscoveryResult(endpoints, []);

        return new DockerEndpointDiscoveryResult([], ["No Docker socket was found. Searched: " + string.Join(", ", searched) + ". Set DOCKER_HOST or select a docker context to use another daemon."]);
    }

    private IEnumerable<(FullPath Path, string Source)> GetWellKnownSockets()
    {
        yield return (FullPath.FromPath("/var/run/docker.sock"), "default socket");
        yield return (HomeDirectory / ".docker" / "run" / "docker.sock", "Docker Desktop");
        if (Platform is DockerHostPlatform.Linux)
            yield return (HomeDirectory / ".docker" / "desktop" / "docker.sock", "Docker Desktop");

        yield return (HomeDirectory / ".colima" / "default" / "docker.sock", "Colima");
        yield return (HomeDirectory / ".colima" / "docker.sock", "Colima");
        yield return (HomeDirectory / ".orbstack" / "run" / "docker.sock", "OrbStack");
        yield return (HomeDirectory / ".rd" / "docker.sock", "Rancher Desktop");

        if (GetRuntimeDirectory() is { } runtimeDirectory)
        {
            yield return (runtimeDirectory / "docker.sock", "rootless Docker");
            yield return (runtimeDirectory / "podman" / "podman.sock", "Podman");
        }
    }

    /// <summary>The directory of the sockets of a rootless daemon: <c>XDG_RUNTIME_DIR</c>, or <c>/run/user/$UID</c> on Linux.</summary>
    private FullPath? GetRuntimeDirectory()
    {
        var xdgRuntimeDirectory = GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(xdgRuntimeDirectory))
            return FullPath.FromPath(xdgRuntimeDirectory);

        var uid = GetEnvironmentVariable("UID");
        if (Platform is DockerHostPlatform.Linux && !string.IsNullOrEmpty(uid))
            return FullPath.FromPath("/run/user/" + uid);

        return null;
    }

    private static JsonDocumentOptions CreateDocumentOptions()
        => new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
}
