namespace Meziantou.Framework.PostgreSql;

internal sealed class PostgreSqlBackendSession
{
    // A CancelRequest arrives on a different connection, so CancelCurrentCommand runs on another thread than
    // the one executing the command. The lock keeps it from calling Cancel() on a source that EndCommand
    // has already disposed, which would throw on the cancelling connection and silently drop the cancel.
    private readonly Lock _lock = new();
    private CancellationTokenSource? _currentCommandCancellationTokenSource;

    public CancellationTokenSource BeginCommand(CancellationToken connectionToken)
    {
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(connectionToken);
        CancellationTokenSource? previous;
        lock (_lock)
        {
            previous = _currentCommandCancellationTokenSource;
            _currentCommandCancellationTokenSource = cancellationTokenSource;
        }

        previous?.Dispose();
        return cancellationTokenSource;
    }

    public void EndCommand(CancellationTokenSource cancellationTokenSource)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_currentCommandCancellationTokenSource, cancellationTokenSource))
            {
                _currentCommandCancellationTokenSource = null;
            }

            cancellationTokenSource.Dispose();
        }
    }

    public void CancelCurrentCommand()
    {
        lock (_lock)
        {
            try
            {
                _currentCommandCancellationTokenSource?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
