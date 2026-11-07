using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.TemporaryContainers.Strategies;

/// <summary>Runs strategies concurrently and completes as soon as one of them completes.</summary>
internal sealed class AnyWaitStrategy(IWaitStrategy[] strategies) : IWaitStrategy
{
    public async Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = new List<Task>(strategies.Length);
        foreach (var strategy in strategies)
        {
            // Task.Run so that a strategy that does synchronous work before its first await does not delay the others.
            tasks.Add(Task.Run(() => strategy.WaitAsync(container, cts.Token), CancellationToken.None));
        }

        var failures = new List<Exception>();
        while (tasks.Count > 0)
        {
            var task = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(task);
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Another strategy may still complete: a failure only counts once all of them failed.
                failures.Add(ex);
                continue;
            }

            // Stop the other strategies, and wait for them so that none keeps probing the container after the wait.
            await cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // They were canceled, or failed while being canceled: the container is ready anyway.
            }

            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Throw(failures[0]);
        }

        throw new AggregateException("None of the wait strategies completed.", failures);
    }

    public override string ToString() => "any of (" + string.Join(", ", (object[])strategies) + ")";
}
