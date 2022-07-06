namespace Meziantou.Framework.Threading;

public sealed class ResettableCancellationTokenSource : IDisposable
{
    private readonly ResettableCancellationTokenSourceOptions _options;
    private CancellationTokenSource _cts = new();

    public ResettableCancellationTokenSource(ResettableCancellationTokenSourceOptions options)
    {
        _options = options;
    }

    public ResettableCancellationTokenSource(bool cancelOnResetAndDispose)
    {
        if (cancelOnResetAndDispose)
        {
            _options = ResettableCancellationTokenSourceOptions.CancelOnDispose | ResettableCancellationTokenSourceOptions.CancelOnReset;
        }
    }

    public CancellationToken Token => _cts.Token;

    public bool IsCancellationRequested => _cts.IsCancellationRequested;

    public void Cancel() => _cts.Cancel();

    public void CancelAfter(TimeSpan delay) => _cts.CancelAfter(delay);

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
