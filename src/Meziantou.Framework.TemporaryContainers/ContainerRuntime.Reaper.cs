namespace Meziantou.Framework.TemporaryContainers;

public partial class ContainerRuntime
{
    /// <summary>Starts a watchdog container that removes the containers, images and volumes created by this process once the process is gone, even when it is killed before it can dispose them.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The watchdog. Keep it alive for as long as the containers it watches, and dispose it to stop it.</returns>
    /// <exception cref="NotSupportedException">The runtime does not expose a socket the watchdog can drive.</exception>
    public Task<ContainerReaper> StartReaperAsync(CancellationToken cancellationToken = default)
        => StartReaperAsync(new ContainerReaperOptions(), cancellationToken);

    /// <summary>Starts a watchdog container that removes the containers, images and volumes created by this process once the process is gone, even when it is killed before it can dispose them.</summary>
    /// <param name="options">The watchdog configuration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The watchdog. Keep it alive for as long as the containers it watches, and dispose it to stop it.</returns>
    /// <exception cref="NotSupportedException">The runtime does not expose a socket the watchdog can drive.</exception>
    /// <remarks>The watchdog only removes the resources this process created; the resources of a run that has a <see cref="ContainerDefinition.ReuseId"/> are not part of a session and are left alone.</remarks>
    public async Task<ContainerReaper> StartReaperAsync(ContainerReaperOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        await EnsureSupportedAsync(cancellationToken).ConfigureAwait(false);
        var runtime = await GetEffectiveRuntimeAsync(cancellationToken).ConfigureAwait(false);
        if (!runtime.SupportsReaper)
            throw new NotSupportedException($"The '{runtime}' runtime does not support the reaper: it has no docker-compatible socket to drive.");

        var socketPath = options.SocketPath ?? await runtime.GetReaperSocketPathAsync(cancellationToken).ConfigureAwait(false);
        return await ContainerReaper.StartAsync(runtime, options, socketPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether the runtime is driven by a docker-compatible socket, which the watchdog needs to remove the resources.</summary>
    internal virtual bool SupportsReaper => false;

    /// <summary>The socket the watchdog mounts, as the daemon sees it: a bind mount is resolved by the daemon, which runs in a virtual machine on macOS and Windows.</summary>
    internal virtual Task<string> GetReaperSocketPathAsync(CancellationToken cancellationToken)
        => Task.FromResult(DefaultReaperSocketPath);

    /// <summary>The socket of a docker daemon, as the containers of that daemon see it. Docker Desktop for Windows exposes the Linux socket at the same path; the leading slash keeps the CLI from reading it as a Windows path.</summary>
    private protected static string DefaultReaperSocketPath => OperatingSystem.IsWindows() ? "//var/run/docker.sock" : "/var/run/docker.sock";
}
