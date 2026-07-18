using System.Text.Json.Serialization;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal static class DockerApiModels
{
    internal sealed class Version
    {
        public string? ApiVersion { get; set; }
        public string? MinAPIVersion { get; set; }
    }

    internal sealed class ErrorResponse
    {
        public string? Message { get; set; }
    }

    internal sealed class CreateContainerResponse
    {
        public string? Id { get; set; }
    }

    internal sealed class ContainerSummary
    {
        public string? Id { get; set; }
        public Dictionary<string, string>? Labels { get; set; }
    }

    internal sealed class PullProgress
    {
        public string? Error { get; set; }
        public ErrorDetail? ErrorDetail { get; set; }
    }

    internal sealed class ErrorDetail
    {
        public string? Message { get; set; }
    }

    internal sealed class ExecCreateResponse
    {
        public string? Id { get; set; }
    }

    internal sealed class ExecInspectResponse
    {
        public int ExitCode { get; set; }
    }

    internal sealed class ExecCreateRequest
    {
        public bool AttachStdout { get; set; }
        public bool AttachStderr { get; set; }
        public bool AttachStdin { get; set; }
        public bool Tty { get; set; }
        public string[]? Cmd { get; set; }
        public string[]? Env { get; set; }
        public string? User { get; set; }
        public string? WorkingDir { get; set; }
    }

    internal sealed class ExecStartRequest
    {
        public bool Detach { get; set; }
        public bool Tty { get; set; }
    }

    internal sealed class CreateContainerRequest
    {
        public string? Image { get; set; }
        public string? Hostname { get; set; }
        public string? User { get; set; }
        public string? WorkingDir { get; set; }
        public Dictionary<string, string>? Labels { get; set; }
        public string[]? Env { get; set; }
        public string[]? Entrypoint { get; set; }
        public string[]? Cmd { get; set; }
        public Dictionary<string, object?>? ExposedPorts { get; set; }
        public HostConfig? HostConfig { get; set; }
        public NetworkingConfig? NetworkingConfig { get; set; }
    }

    internal sealed class HostConfig
    {
        public bool ReadonlyRootfs { get; set; }
        public long? Memory { get; set; }
        public long? NanoCpus { get; set; }
        public string? NetworkMode { get; set; }
        public Dictionary<string, List<DockerPortBindingDto>?>? PortBindings { get; set; }
        public List<Mount>? Mounts { get; set; }
        public Dictionary<string, string>? Tmpfs { get; set; }
    }

    internal sealed class Mount
    {
        public string? Type { get; set; }
        public string? Source { get; set; }
        public string? Target { get; set; }

        [JsonPropertyName("ReadOnly")]
        public bool ReadOnly { get; set; }
    }

    internal sealed class NetworkingConfig
    {
        public Dictionary<string, EndpointSettings>? EndpointsConfig { get; set; }
    }

    internal sealed class EndpointSettings
    {
        public string[]? Aliases { get; set; }
    }

    internal sealed class AuthConfigFile
    {
        public Dictionary<string, AuthEntry>? Auths { get; set; }
        public string? CredsStore { get; set; }
        public Dictionary<string, string>? CredHelpers { get; set; }
    }

    internal sealed class AuthEntry
    {
        public string? Auth { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? IdentityToken { get; set; }
    }

    internal sealed class CredentialHelperGetResponse
    {
        public string? ServerURL { get; set; }
        public string? Username { get; set; }
        public string? Secret { get; set; }
    }

    internal sealed class RegistryAuthHeader
    {
        public string? ServerAddress { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? IdentityToken { get; set; }
    }
}
