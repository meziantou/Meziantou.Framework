using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Identifies the runtime used to manage containers.</summary>
public abstract partial class ContainerRuntime
{
    private readonly string _name;

    private protected ContainerRuntime(string name) => _name = name;

    /// <summary>Automatically detect an available runtime.</summary>
    public static ContainerRuntime Auto { get; } = new AutoContainerRuntime();

    /// <summary>Use the <c>docker</c> CLI.</summary>
    public static ContainerRuntime Docker { get; } = new DockerContainerRuntime(nameof(Docker), DockerContainerRuntime.Flavor.Docker);

    /// <summary>Use the Docker Engine API, over the unix socket or the named pipe of the daemon, without going through the <c>docker</c> CLI.</summary>
    public static ContainerRuntime DockerApi { get; } = new DockerApiRuntime();

    /// <summary>Use the <c>podman</c> CLI.</summary>
    public static ContainerRuntime Podman { get; } = new DockerContainerRuntime(nameof(Podman), DockerContainerRuntime.Flavor.Podman);

    /// <summary>Use Apple's <c>container</c> CLI (macOS).</summary>
    public static ContainerRuntime AppleContainer { get; } = new AppleContainerRuntime(nameof(AppleContainer));

    /// <summary>Use the WSL container CLI (<c>wslc</c>, Windows).</summary>
    public static ContainerRuntime Wslc { get; } = new DockerContainerRuntime(nameof(Wslc), DockerContainerRuntime.Flavor.Wslc);

    /// <summary>Determines whether this runtime can be resolved.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <remarks>The runtime is not considered available until its daemon answers, so the first call runs a command or opens a connection. Concurrent calls share that probe. A success is cached; a failure is not, so a daemon that starts later is still detected.</remarks>
    /// <returns><see langword="true"/> if the runtime executable is available and operational; otherwise, <see langword="false"/>.</returns>
    public virtual Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    internal async Task EnsureSupportedAsync(CancellationToken cancellationToken)
    {
        if (!await IsSupportedAsync(cancellationToken).ConfigureAwait(false))
            throw CreateUnavailableRuntimeException(this);
    }

    /// <summary>The runtime that actually runs the commands. Only <see cref="Auto"/> differs from the instance itself, and resolving it may run a command or open a connection.</summary>
    internal virtual Task<ContainerRuntime> GetEffectiveRuntimeAsync(CancellationToken cancellationToken) => Task.FromResult(this);

    /// <summary>Whether every log entry carries the timestamp the runtime recorded it at, which is how the logs of an earlier run of a container are told apart from the current one.</summary>
    internal virtual bool LogsIncludeTimestamps => false;

    internal virtual Task<string> EnsureCreatedAsync(ContainerDefinition definition, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task StartAsync(string id, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task StopAsync(string id, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task RestartAsync(string id, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task PauseAsync(string id, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task UnpauseAsync(string id, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task KillAsync(string id, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    /// <summary>Removes a container. A container that does not exist is not an error; a runtime that cannot remove it is.</summary>
    internal virtual Task DeleteAsync(string id, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    /// <summary>Determines whether a container exists. A runtime that cannot answer throws rather than reporting the container as missing.</summary>
    internal virtual Task<bool> ExistsAsync(string id, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task<ContainerInfo> InspectAsync(string id, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    /// <summary>Streams the logs of a container from its creation.</summary>
    /// <param name="id">The container.</param>
    /// <param name="follow">Whether the stream keeps following new entries, or ends with the entries written so far.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    internal virtual IAsyncEnumerable<LogEntry> GetLogsAsync(string id, bool follow, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task<ExecResult> ExecAsync(string id, ExecOptions options, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task<Stream> OpenReadAsync(string id, string path, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task WriteFileAsync(string id, string path, Stream content, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task CopyToContainerAsync(string id, string source, string destination, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task CopyFromContainerAsync(string id, string source, string destination, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual IReadOnlyDictionary<int, int> ResolvePortMap(ContainerInfo info, ContainerDefinition definition)
        => throw CreateNotSupportedException();

    /// <summary>Determines whether a failure to start a container comes from a host port another process took, which a new container with other random ports can avoid.</summary>
    internal virtual bool IsHostPortConflict(Exception exception) => false;

    /// <summary>Creates a volume.</summary>
    /// <param name="definition">The volume definition.</param>
    /// <param name="name">The volume name.</param>
    /// <param name="instanceId">The identifier of the <see cref="TemporaryVolume"/> creating it, recorded in its labels.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    internal virtual Task CreateVolumeAsync(VolumeDefinition definition, string name, string instanceId, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task DeleteVolumeAsync(string name, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    /// <summary>Determines whether a volume exists. A runtime that cannot answer throws rather than reporting the volume as missing.</summary>
    internal virtual Task<bool> VolumeExistsAsync(string name, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    /// <summary>Reads the labels of a volume, or <see langword="null"/> when it does not exist.</summary>
    internal virtual Task<IReadOnlyDictionary<string, string>?> GetVolumeLabelsAsync(string name, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    /// <summary>Removes an image the library built. An image that does not exist is not an error.</summary>
    internal virtual Task DeleteImageAsync(string image, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    private NotSupportedException CreateNotSupportedException() => new($"The '{this}' runtime cannot execute container operations.");

    private protected static InvalidOperationException CreateUnavailableRuntimeException(ContainerRuntime runtime)
        => new(runtime == Auto
            ? "No supported container runtime (Docker Engine API, 'docker', 'podman', 'container', or 'wslc') is available."
            : $"The '{runtime}' runtime is not available.");

    public override string ToString() => _name;
}
