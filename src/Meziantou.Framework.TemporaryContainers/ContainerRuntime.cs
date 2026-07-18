using Meziantou.Framework.TemporaryContainers.Internals;

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

        internal override bool IsSupportedCore()
        {
            if (DockerApiRuntime.TryProbe())
                return true;

            foreach (var candidate in GetCliCandidates())
            {
                if (candidate.IsSupported())
                    return true;
            }

            return false;
        }

        internal override ContainerRuntime? TryResolve()
        {
            if (DockerApiRuntime.TryCreate(out var apiRuntime))
                return apiRuntime;

            foreach (var candidate in GetCliCandidates())
            {
                if (candidate.TryResolve() is { } resolved)
                    return resolved;
            }

            return null;
        }

        private static IEnumerable<ExecutableContainerRuntime> GetCliCandidates()
        {
            yield return (ExecutableContainerRuntime)Docker;
            yield return (ExecutableContainerRuntime)Podman;

            if (OperatingSystem.IsMacOS())
                yield return (ExecutableContainerRuntime)AppleContainer;

            if (OperatingSystem.IsWindows())
                yield return (ExecutableContainerRuntime)Wslc;
        }
    }

    private readonly string _name;

    private protected ContainerRuntime(string name) => _name = name;

    /// <summary>Automatically detect an available runtime.</summary>
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
        return IsSupportedCore();
    }

    internal virtual bool IsSupportedCore() => TryResolve() is not null;

    /// <summary>Resolves this runtime into a fully-bound, ready-to-use instance, or returns <see langword="null"/> if the runtime is unavailable.</summary>
    internal virtual ContainerRuntime? TryResolve() => null;

    internal ContainerRuntime Resolve()
    {
        return TryResolve() ?? throw new InvalidOperationException(this == Auto
            ? "No supported container runtime (Docker Engine API, 'docker', 'podman', 'container', or 'wslc') is available."
            : $"The '{this}' runtime is not available.");
    }

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

    private NotSupportedException CreateNotSupportedException() => new NotSupportedException($"The '{this}' runtime cannot execute container operations.");

    public override string ToString() => _name;
}
