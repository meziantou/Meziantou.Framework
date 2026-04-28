namespace Meziantou.Framework.PostgreSql;

internal sealed class PostgreSqlBackendSession
{
    private CancellationTokenSource? _currentCommandCancellationTokenSource;

    public CancellationTokenSource BeginCommand(CancellationToken connectionToken)
    {
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(connectionToken);
        var previous = Interlocked.Exchange(ref _currentCommandCancellationTokenSource, cancellationTokenSource);
        previous?.Dispose();
        return cancellationTokenSource;
    }

    public void EndCommand(CancellationTokenSource cancellationTokenSource)
    {
        var previous = Interlocked.CompareExchange(ref _currentCommandCancellationTokenSource, null, cancellationTokenSource);
        if (ReferenceEquals(previous, cancellationTokenSource))
        {
            cancellationTokenSource.Dispose();
            return;
        }

        cancellationTokenSource.Dispose();
    }

    public void CancelCurrentCommand()
    {
        Volatile.Read(ref _currentCommandCancellationTokenSource)?.Cancel();
    }
}
