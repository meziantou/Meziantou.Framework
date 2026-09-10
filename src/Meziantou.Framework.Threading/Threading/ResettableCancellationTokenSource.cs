namespace Meziantou.Framework.Threading;

/// <summary>Represents a cancellation token source that can be reset to its initial state.</summary>
/// <remarks>
/// All members are safe to call concurrently. Note that the callbacks registered on <see cref="Token"/> run while the
/// internal lock is held, so a callback must not block waiting on another thread that uses the same instance.
/// <see cref="DisposeAsync"/> is the exception: it runs them on another thread and does not hold the lock while they
/// complete.
/// <para>
/// <see cref="Reset"/> reuses the underlying <see cref="CancellationTokenSource"/> when it can, so whether a
/// <see cref="CancellationToken"/> obtained before the reset belongs to the previous generation depends on the state
/// of that source:
/// </para>
/// <list type="bullet">
/// <item>
/// When cancellation has already been requested - through <see cref="Cancel"/>, through an elapsed
/// <see cref="CancelAfter"/> delay, or because <see cref="ResettableCancellationTokenSourceOptions.CancelOnReset"/>
/// is set - the source cannot be reused and is replaced. The previous token stays canceled and never reacts to a
/// later <see cref="Cancel"/>.
/// </item>
/// <item>
/// Otherwise the same source is reused, so the previous token is the very token <see cref="Token"/> returns
/// afterwards and it does react to a later <see cref="Cancel"/>. The reset also removes the callbacks already
/// registered on that token, which are never invoked, and disarms a pending <see cref="CancelAfter"/> delay.
/// </item>
/// </list>
/// <para>Read <see cref="Token"/> again after resetting rather than relying on either behavior.</para>
/// </remarks>
/// <example>
/// <code><![CDATA[
/// var cts = new ResettableCancellationTokenSource(cancelOnResetAndDispose: true);
/// 
/// // Use the token
/// await DoWorkAsync(cts.Token);
/// 
/// // Reset to reuse
/// cts.Reset();
/// await DoWorkAsync(cts.Token);
/// ]]></code>
/// </example>
public sealed class ResettableCancellationTokenSource : IDisposable, IAsyncDisposable
{
    private readonly ResettableCancellationTokenSourceOptions _options;
    private readonly Lock _lock = new();
    private CancellationTokenSource _cts = new();
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ResettableCancellationTokenSource"/> class with the specified options.</summary>
    /// <param name="options">Options that control the behavior when resetting or disposing.</param>
    public ResettableCancellationTokenSource(ResettableCancellationTokenSourceOptions options)
    {
        _options = options;
    }

    /// <summary>Initializes a new instance of the <see cref="ResettableCancellationTokenSource"/> class.</summary>
    /// <param name="cancelOnResetAndDispose"><see langword="true"/> to cancel the token when resetting or disposing; otherwise, <see langword="false"/>.</param>
    public ResettableCancellationTokenSource(bool cancelOnResetAndDispose)
    {
        if (cancelOnResetAndDispose)
        {
            _options = ResettableCancellationTokenSourceOptions.CancelOnDispose | ResettableCancellationTokenSourceOptions.CancelOnReset;
        }
    }

    /// <summary>Gets the cancellation token associated with this <see cref="ResettableCancellationTokenSource"/>.</summary>
    public CancellationToken Token
    {
        get
        {
            lock (_lock)
            {
                return _cts.Token;
            }
        }
    }

    /// <summary>Gets whether cancellation has been requested for this token source.</summary>
    public bool IsCancellationRequested
    {
        get
        {
            lock (_lock)
            {
                return _cts.IsCancellationRequested;
            }
        }
    }

    /// <summary>Communicates a request for cancellation.</summary>
    public void Cancel()
    {
        lock (_lock)
        {
            _cts.Cancel();
        }
    }

    /// <summary>Schedules a cancel operation on this <see cref="ResettableCancellationTokenSource"/> after the specified time span.</summary>
    /// <param name="delay">The time span to wait before canceling this <see cref="ResettableCancellationTokenSource"/>.</param>
    public void CancelAfter(TimeSpan delay)
    {
        lock (_lock)
        {
            _cts.CancelAfter(delay);
        }
    }

    /// <summary>Resets the cancellation token source to its initial state.</summary>
    /// <remarks>
    /// The underlying <see cref="CancellationTokenSource"/> is reused when cancellation has not been requested, so the
    /// token read before the reset can be the very token <see cref="Token"/> returns afterwards. See the remarks on
    /// <see cref="ResettableCancellationTokenSource"/> for the exact behavior.
    /// <para>
    /// The instance is reset even when a cancellation callback throws: the exception raised by
    /// <see cref="CancellationTokenSource.Cancel()"/> propagates to the caller only after a fresh
    /// <see cref="Token"/> is available. A callback that disposes this instance wins, and the reset is abandoned.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    /// <exception cref="AggregateException">A callback registered on the current <see cref="Token"/> threw.</exception>
    public void Reset()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            try
            {
                if (_options.HasFlag(ResettableCancellationTokenSourceOptions.CancelOnReset))
                {
                    _cts.Cancel();
                }
            }
            finally
            {
                // A callback may have disposed this instance reentrantly, in which case the source is already gone.
                if (!_disposed)
                {
                    // Replacing the source is only safe while the lock is held: every other member reads _cts under the
                    // same lock, so no caller can be using the instance that is about to be disposed. _cts is read here
                    // rather than before the cancellation because a callback may have replaced it reentrantly.
                    var cts = _cts;
                    if (!cts.TryReset())
                    {
                        cts.Dispose();
                        _cts = new CancellationTokenSource();
                    }
                }
            }
        }
    }

    /// <summary>Releases the resources used by this <see cref="ResettableCancellationTokenSource"/>.</summary>
    /// <remarks>
    /// The underlying <see cref="CancellationTokenSource"/> is disposed even when a cancellation callback throws: the
    /// exception raised by <see cref="CancellationTokenSource.Cancel()"/> propagates to the caller only after the
    /// resources are released. Subsequent calls do nothing.
    /// </remarks>
    /// <exception cref="AggregateException">A callback registered on the current <see cref="Token"/> threw.</exception>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                if (_options.HasFlag(ResettableCancellationTokenSourceOptions.CancelOnDispose))
                {
                    _cts.Cancel();
                }
            }
            finally
            {
                _cts.Dispose();
            }
        }
    }

    /// <summary>Releases the resources used by this <see cref="ResettableCancellationTokenSource"/>, without running the cancellation callbacks on the calling thread.</summary>
    /// <remarks>
    /// Unlike <see cref="Dispose"/>, this method relies on <see cref="CancellationTokenSource.CancelAsync"/>, so the
    /// callbacks registered on <see cref="Token"/> run on another thread and are awaited instead of blocking the
    /// caller. A callback is therefore free to use this instance, which is not the case with <see cref="Dispose"/>.
    /// <para>
    /// The underlying <see cref="CancellationTokenSource"/> is disposed even when a callback throws: the exception
    /// raised by <see cref="CancellationTokenSource.CancelAsync"/> propagates to the caller only after the resources
    /// are released. Subsequent calls do nothing, whether they go through <see cref="Dispose"/> or this method, but a
    /// call made while this one is still awaiting the callbacks returns before the resources are released.
    /// </para>
    /// </remarks>
    /// <exception cref="AggregateException">A callback registered on the current <see cref="Token"/> threw.</exception>
    public ValueTask DisposeAsync()
    {
        Task cancellation;
        CancellationTokenSource cts;
        lock (_lock)
        {
            if (_disposed)
                return ValueTask.CompletedTask;

            _disposed = true;
            cts = _cts;

            if (!_options.HasFlag(ResettableCancellationTokenSourceOptions.CancelOnDispose))
            {
                cts.Dispose();
                return ValueTask.CompletedTask;
            }

            // CancelAsync never runs the callbacks on the calling thread, so starting it while the lock is held cannot
            // deadlock, and it orders the cancellation before any member that is waiting for the lock.
            cancellation = cts.CancelAsync();
        }

        return DisposeAsyncCore(cancellation, cts);
    }

    private async ValueTask DisposeAsyncCore(Task cancellation, CancellationTokenSource cts)
    {
        try
        {
            await cancellation.ConfigureAwait(false);
        }
        finally
        {
            // Disposing while the lock is held keeps a concurrent member from using the source as it is disposed, the
            // same way Reset does when it replaces the source.
            lock (_lock)
            {
                cts.Dispose();
            }
        }
    }
}
