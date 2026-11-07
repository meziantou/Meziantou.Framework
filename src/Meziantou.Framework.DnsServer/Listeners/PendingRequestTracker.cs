namespace Meziantou.Framework.DnsServer.Listeners;

/// <summary>
/// Counts the requests a listener has in flight so shutdown can wait for them instead of tearing
/// their sockets down mid-response.
/// </summary>
internal sealed class PendingRequestTracker
{
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _count;
    private bool _draining;

    public void Begin() => Interlocked.Increment(ref _count);

    public void End()
    {
        if (Interlocked.Decrement(ref _count) is 0 && Volatile.Read(ref _draining))
        {
            _drained.TrySetResult();
        }
    }

    /// <summary>Waits for the in-flight requests to finish. Returns <see langword="false"/> if they did not complete in time.</summary>
    public async Task<bool> DrainAsync(TimeSpan timeout)
    {
        Volatile.Write(ref _draining, true);

        // Re-check after publishing the flag: the last request may have completed just before it was set.
        if (Volatile.Read(ref _count) is 0)
        {
            _drained.TrySetResult();
        }

        try
        {
            await _drained.Task.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
