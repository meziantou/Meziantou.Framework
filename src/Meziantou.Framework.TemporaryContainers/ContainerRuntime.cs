using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Identifies the container runtime CLI used to manage containers.</summary>
public abstract class ContainerRuntime
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
    /// <remarks>The runtime is not considered available until its daemon answers, so the first call runs a command or opens a connection. A success is cached; a failure is not, so a daemon that starts later is still detected.</remarks>
    /// <returns><see langword="true"/> if the runtime executable is available and operational; otherwise, <see langword="false"/>.</returns>
    public virtual Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    internal async Task EnsureSupportedAsync(CancellationToken cancellationToken)
    {
        if (!await IsSupportedAsync(cancellationToken).ConfigureAwait(false))
            throw CreateUnavailableRuntimeException(this);
    }

    /// <summary>The runtime that actually runs the commands. Only <see cref="Auto"/> differs from the instance itself, and resolving it may run a command or open a connection.</summary>
    internal virtual Task<ContainerRuntime> GetEffectiveRuntimeAsync(CancellationToken cancellationToken) => Task.FromResult(this);

    internal virtual bool SupportsPause => false;

    internal virtual bool SupportsRestart => false;

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

    internal virtual Task DeleteAsync(string id, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task<bool> ExistsAsync(string id, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task<ContainerInfo> InspectAsync(string id, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual IAsyncEnumerable<LogEntry> GetLogsAsync(string id, CancellationToken cancellationToken)
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

    internal virtual Task CreateVolumeAsync(VolumeDefinition definition, string name, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task DeleteVolumeAsync(string name, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    internal virtual Task<bool> VolumeExistsAsync(string name, CancellationToken cancellationToken)
        => throw CreateNotSupportedException();

    private NotSupportedException CreateNotSupportedException() => new($"The '{this}' runtime cannot execute container operations.");

    private protected static InvalidOperationException CreateUnavailableRuntimeException(ContainerRuntime runtime)
        => new(runtime == Auto
            ? "No supported container runtime (Docker Engine API, 'docker', 'podman', 'container', or 'wslc') is available."
            : $"The '{runtime}' runtime is not available.");

    public override string ToString() => _name;
}
