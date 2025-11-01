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
    private readonly CancellationTokenSource _cts;
    private readonly CancellationTokenRegistration _cancelRegistration;

    /// <summary>Initializes a new instance of the <see cref="DelayedCancellationTokenSource"/> class that will be cancelled after the specified delay when the source token is cancelled.</summary>
    /// <param name="cancellationToken">The source cancellation token to monitor.</param>
    /// <param name="delay">The time span to wait before cancelling this token after the source token is cancelled.</param>
    [SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "")]
    public DelayedCancellationTokenSource(CancellationToken cancellationToken, TimeSpan delay)
    {
        _cts = new CancellationTokenSource();

#pragma warning disable MA0147 // Avoid async void method for delegate
        _cancelRegistration = cancellationToken.Register(async () =>
        {
            try
            {
#pragma warning disable MA0040 // Flow the cancellation token
                await Task.Delay(delay).ConfigureAwait(false);
#pragma warning restore MA0040

                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        });
#pragma warning restore MA0147
    }

    /// <summary>Gets the cancellation token associated with this <see cref="DelayedCancellationTokenSource"/>.</summary>
    public CancellationToken Token => _cts.Token;

    public void Dispose()
    {
        _cancelRegistration.Dispose();
        _cts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _cancelRegistration.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
