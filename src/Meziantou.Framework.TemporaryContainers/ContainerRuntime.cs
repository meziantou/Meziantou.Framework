using Meziantou.Framework.TemporaryContainers.Internals;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Identifies the container runtime CLI used to manage containers.</summary>
public abstract class ContainerRuntime
{
    private sealed class AutoContainerRuntime : ContainerRuntime
    {
        public AutoContainerRuntime()
            : base(nameof(Auto))
        {
        }
    }

    private readonly string _name;

    private protected ContainerRuntime(string name)
    {
        _name = name;
    }

    /// <summary>Automatically detect an available runtime (docker, then podman, then a platform-specific runtime).</summary>
    public static ContainerRuntime Auto { get; } = new AutoContainerRuntime();

    /// <summary>Use the <c>docker</c> CLI.</summary>
    public static ContainerRuntime Docker { get; } = new DockerContainerRuntime(nameof(Docker), DockerContainerRuntime.Flavor.Docker);

    /// <summary>Use the <c>podman</c> CLI.</summary>
    public static ContainerRuntime Podman { get; } = new DockerContainerRuntime(nameof(Podman), DockerContainerRuntime.Flavor.Podman);

    /// <summary>Use Apple's <c>container</c> CLI (macOS).</summary>
    public static ContainerRuntime AppleContainer { get; } = new AppleContainerRuntime(nameof(AppleContainer));

    /// <summary>Use the WSL container CLI (<c>wslc</c>, Windows).</summary>
    public static ContainerRuntime Wslc { get; } = new DockerContainerRuntime(nameof(Wslc), DockerContainerRuntime.Flavor.Wslc);

    /// <summary>Determines whether this runtime can be resolved.</summary>
    /// <returns><see langword="true"/> if the runtime executable is available and operational; otherwise, <see langword="false"/>.</returns>
    public bool IsSupported()
    {
        return IsSupported(logger: null);
    }

    /// <summary>Gets the runtime that would be used when <see cref="Auto"/> is requested.</summary>
    /// <param name="runtime">When this method returns, contains the resolved runtime if one was found.</param>
    /// <returns><see langword="true"/> if a runtime was found; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetAvailableRuntime(out ContainerRuntime runtime)
    {
        return ContainerRuntimeResolver.TryResolve(Auto, out runtime, out _, logger: null);
    }

    internal bool IsSupported(ILogger? logger)
    {
        return ContainerRuntimeResolver.TryResolve(this, out _, out _, logger);
    }

    internal virtual ContainerRuntime Bind(string executable, ILogger? logger) => this;

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

    private NotSupportedException CreateNotSupportedException()
    {
        return new NotSupportedException($"The '{this}' runtime cannot execute container operations.");
    }

    public override string ToString() => _name;
}
