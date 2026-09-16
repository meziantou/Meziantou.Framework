namespace Meziantou.Framework.TemporaryContainers;

public partial class TemporaryContainer
{
    /// <summary>Stops the container.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the container has stopped.</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var id = RequireId();
        await StopForwardingLogsAsync().ConfigureAwait(false);
        await Runtime.StopAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Restarts the container and refreshes the published-port mapping.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the container has restarted.</returns>
    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        var id = RequireId();
        await StopForwardingLogsAsync().ConfigureAwait(false);
        await PrepareLogsForStartAsync(restarting: true, cancellationToken).ConfigureAwait(false);
        await Runtime.RestartAsync(id, cancellationToken).ConfigureAwait(false);
        await RefreshStateAsync(cancellationToken).ConfigureAwait(false);
        StartForwardingLogs();
    }

    /// <summary>Pauses the container.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the container is paused.</returns>
    /// <exception cref="NotSupportedException">The runtime does not support pausing containers.</exception>
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        var id = RequireId();
        await Runtime.PauseAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resumes a paused container.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the container is resumed.</returns>
    /// <exception cref="NotSupportedException">The runtime does not support pausing containers.</exception>
    public async Task UnpauseAsync(CancellationToken cancellationToken = default)
    {
        var id = RequireId();
        await Runtime.UnpauseAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends a kill signal to the container.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the container is killed.</returns>
    public async Task KillAsync(CancellationToken cancellationToken = default)
    {
        var id = RequireId();
        await StopForwardingLogsAsync().ConfigureAwait(false);
        await Runtime.KillAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes the container, and the image built for it. A later <see cref="StartAsync"/> or <see cref="EnsureCreatedAsync"/> creates a new container.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the container is removed.</returns>
    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_id is null)
            return;

        await DeleteCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteCoreAsync(CancellationToken cancellationToken)
    {
        await StopForwardingLogsAsync().ConfigureAwait(false);
        await Runtime.DeleteAsync(Id, cancellationToken).ConfigureAwait(false);
        await DeleteBuiltImageAsync(cancellationToken).ConfigureAwait(false);

        // The container is gone, so nothing about it is kept: the next start creates a new one instead of trying to start
        // an id that no longer exists.
        _id = null;
        _name = null;
        _created = false;
        _portMap = null;
        _startedAt = null;
        _logEntriesToSkip = 0;
    }

    /// <summary>Determines whether the container still exists.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> if the container exists; otherwise, <see langword="false"/>.</returns>
    public async Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_id is null)
            return false;

        return await Runtime.ExistsAsync(_id, cancellationToken).ConfigureAwait(false);
    }
}
