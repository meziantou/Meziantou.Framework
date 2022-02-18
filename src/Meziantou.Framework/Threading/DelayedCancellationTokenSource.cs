namespace Meziantou.Framework.Threading
{
    public sealed class DelayedCancellationTokenSource : IDisposable, IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly CancellationTokenRegistration _cancelRegistration;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "")]
        public DelayedCancellationTokenSource(CancellationToken cancellationToken, TimeSpan delay)
        {
            _cts = new CancellationTokenSource();
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
}
