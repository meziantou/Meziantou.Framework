namespace Meziantou.Framework.TemporaryContainers;

public partial class ContainerRuntime
{
    /// <summary>Starts a watchdog container that removes the containers and volumes created by this process once the process is gone, even when it is killed before it can dispose them.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The watchdog. Keep it alive for as long as the containers it watches, and dispose it to stop it.</returns>
    /// <exception cref="NotSupportedException">The runtime does not expose a socket the watchdog can drive.</exception>
    public Task<ContainerReaper> StartReaperAsync(CancellationToken cancellationToken = default)
        => StartReaperAsync(new ContainerReaperOptions(), cancellationToken);

    /// <summary>Starts a watchdog container that removes the containers and volumes created by this process once the process is gone, even when it is killed before it can dispose them.</summary>
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

        return await ContainerReaper.StartAsync(runtime, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether the runtime is driven by a docker-compatible socket, which the watchdog needs to remove the resources.</summary>
    internal virtual bool SupportsReaper => false;
}
