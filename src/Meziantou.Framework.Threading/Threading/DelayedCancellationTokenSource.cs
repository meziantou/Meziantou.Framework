namespace Meziantou.Framework.Threading;

/// <summary>Represents a cancellation token source that delays the cancellation signal by a specified time span.</summary>
/// <example>
/// <code><![CDATA[
/// var cts = new CancellationTokenSource();
/// var delayedCts = new DelayedCancellationTokenSource(cts.Token, TimeSpan.FromSeconds(5));
///
/// // When cts is cancelled, delayedCts will be cancelled 5 seconds later
/// cts.Cancel();
/// await Task.Delay(TimeSpan.FromSeconds(6));
/// // delayedCts.Token is now cancelled
/// ]]></code>
/// </example>
public sealed class DelayedCancellationTokenSource : IDisposable, IAsyncDisposable
{
    /// <summary>The longest delay accepted by <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private readonly CancellationTokenSource _cts;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly CancellationTokenRegistration _cancelRegistration;
    private Task _delayedCancellation = Task.CompletedTask;
    private int _disposed;

    /// <summary>Initializes a new instance of the <see cref="DelayedCancellationTokenSource"/> class that will be cancelled after the specified delay when the source token is cancelled.</summary>
    /// <param name="cancellationToken">The source cancellation token to monitor.</param>
    /// <param name="delay">The time span to wait before cancelling this token after the source token is cancelled. Use <see cref="Timeout.InfiniteTimeSpan"/> to never cancel.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> is less than -1 millisecond (<see cref="Timeout.InfiniteTimeSpan"/>) or greater than 4294967294 milliseconds.</exception>
    [SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "")]
    public DelayedCancellationTokenSource(CancellationToken cancellationToken, TimeSpan delay)
    {
        // Validate eagerly: the delay is only consumed once the source token is cancelled, at which point
        // there is no caller left to report the error to.
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, Timeout.InfiniteTimeSpan);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delay, MaxDelay);

        _cts = new CancellationTokenSource();

        // The callback is synchronous on purpose. An "async void" callback would rethrow its failures on
        // the thread pool and terminate the process, so it only starts the asynchronous work and publishes
        // it to _delayedCancellation, where DisposeAsync can observe the outcome.
        _cancelRegistration = cancellationToken.Register(() => Volatile.Write(ref _delayedCancellation, CancelAfterDelayAsync(delay)));
    }

    /// <summary>Gets the cancellation token associated with this <see cref="DelayedCancellationTokenSource"/>.</summary>
    public CancellationToken Token => _cts.Token;

    private async Task CancelAfterDelayAsync(TimeSpan delay)
    {
        try
        {
            // Flow a token tied to disposal so the underlying timer is released promptly when
            // this instance is disposed before the delay elapses, instead of staying alive for
            // the full delay.
            await Task.Delay(delay, _disposeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            // Any exception thrown by a callback registered on Token faults this task instead of escaping
            // to the thread pool. DisposeAsync rethrows it; Dispose cannot, so it is then reported through
            // TaskScheduler.UnobservedTaskException.
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The instance was disposed while the delay was elapsing.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _cancelRegistration.Dispose();
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        _cts.Dispose();
    }

    /// <summary>Releases the resources used by this instance and waits for a pending delayed cancellation to complete.</summary>
    /// <exception cref="AggregateException">A callback registered on <see cref="Token"/> threw an exception while the delayed cancellation was being signalled.</exception>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Disposing the registration waits for a running callback to complete, so the delayed cancellation
        // task, if any, is published by the time this returns.
        await _cancelRegistration.DisposeAsync().ConfigureAwait(false);
        await _disposeCts.CancelAsync().ConfigureAwait(false);

        var delayedCancellation = Volatile.Read(ref _delayedCancellation);
        try
        {
            await delayedCancellation.ConfigureAwait(false);
        }
        finally
        {
            _disposeCts.Dispose();
            _cts.Dispose();
        }
    }
}
