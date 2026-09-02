namespace Meziantou.Framework.Threading;

/// <summary>Represents a cancellation token source that can be reset to its initial state.</summary>
/// <remarks>
/// All members are safe to call concurrently. Note that the callbacks registered on <see cref="Token"/> run while the
/// internal lock is held, so a callback must not block waiting on another thread that uses the same instance.
/// <para>
/// <see cref="Reset"/> replaces the underlying <see cref="CancellationTokenSource"/>, so a <see cref="CancellationToken"/>
/// obtained before the reset belongs to the previous generation: it never reacts to a later <see cref="Cancel"/>. Read
/// <see cref="Token"/> again after resetting.
/// </para>
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
public sealed class ResettableCancellationTokenSource : IDisposable
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
    public void Reset()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_options.HasFlag(ResettableCancellationTokenSourceOptions.CancelOnReset))
            {
                _cts.Cancel();
            }

            // Replacing the source is only safe while the lock is held: every other member reads _cts under the same
            // lock, so no caller can be using the instance that is about to be disposed.
            if (!_cts.TryReset())
            {
                _cts.Dispose();
                _cts = new CancellationTokenSource();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_options.HasFlag(ResettableCancellationTokenSourceOptions.CancelOnDispose))
            {
                _cts.Cancel();
            }

            _cts.Dispose();
        }
    }
}
