namespace Meziantou.Framework.Threading;

public sealed class DelayedCancellationTokenSource : IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly CancellationTokenRegistration _cancelRegistration;

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
