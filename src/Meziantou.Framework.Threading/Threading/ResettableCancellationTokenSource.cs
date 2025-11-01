namespace Meziantou.Framework.Threading;

/// <summary>Represents a cancellation token source that can be reset to its initial state.</summary>
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
    private CancellationTokenSource _cts = new();

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
    public CancellationToken Token => _cts.Token;

    /// <summary>Gets whether cancellation has been requested for this token source.</summary>
    public bool IsCancellationRequested => _cts.IsCancellationRequested;

    /// <summary>Communicates a request for cancellation.</summary>
    public void Cancel() => _cts.Cancel();

    /// <summary>Schedules a cancel operation on this <see cref="ResettableCancellationTokenSource"/> after the specified time span.</summary>
    /// <param name="delay">The time span to wait before canceling this <see cref="ResettableCancellationTokenSource"/>.</param>
    public void CancelAfter(TimeSpan delay) => _cts.CancelAfter(delay);

    /// <summary>Resets the cancellation token source to its initial state.</summary>
    public void Reset()
    {
        if (_options.HasFlag(ResettableCancellationTokenSourceOptions.CancelOnReset))
        {
            _cts.Cancel();
        }

        if (!_cts.TryReset())
        {
            _cts.Dispose();
            _cts = new CancellationTokenSource();
        }
    }

    public void Dispose()
    {
        if (_options.HasFlag(ResettableCancellationTokenSourceOptions.CancelOnDispose))
        {
            _cts.Cancel();
        }

        _cts.Dispose();
    }
}
