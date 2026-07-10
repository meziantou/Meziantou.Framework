namespace Meziantou.Framework.TemporaryContainers.Strategies;

internal sealed class DelayWaitStrategy(TimeSpan delay) : IWaitStrategy
{
    public Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, cancellationToken);
    }
}
