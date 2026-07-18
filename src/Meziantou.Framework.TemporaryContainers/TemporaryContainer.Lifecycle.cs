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
        await Runtime.RestartAsync(id, cancellationToken).ConfigureAwait(false);
        StartForwardingLogs();
        await RefreshPortsAsync(cancellationToken).ConfigureAwait(false);
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

    /// <summary>Removes the container.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the container is removed.</returns>
    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_id is null)
            return;

        await StopForwardingLogsAsync().ConfigureAwait(false);
        await Runtime.DeleteAsync(_id, cancellationToken).ConfigureAwait(false);
        _portMap = null;
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
