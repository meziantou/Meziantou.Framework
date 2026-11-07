namespace Meziantou.Framework.TemporaryContainers.Strategies;

/// <summary>Bounds another strategy in time, within the startup timeout of the container.</summary>
internal sealed class TimeoutWaitStrategy(IWaitStrategy strategy, TimeSpan timeout) : IWaitStrategy
{
    public async Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            await strategy.WaitAsync(container, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"The wait strategy '{strategy}' did not complete within {timeout}."), ex);
        }
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{strategy} (timeout {timeout})");
}
