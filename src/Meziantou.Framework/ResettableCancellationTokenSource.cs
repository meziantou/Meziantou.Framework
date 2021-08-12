using System;
using System.Threading;

namespace Meziantou.Framework;

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

        // TODO-NET6 Use TryReset https://github.com/dotnet/runtime/issues/48492
#if NET6_0 || NET5_0
        _cts.Dispose();
        _cts = new CancellationTokenSource();
#else
#error Platform not supported
#endif
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
