namespace Meziantou.Framework.TemporaryContainers.Strategies;

/// <summary>Runs strategies concurrently and completes once all of them have completed.</summary>
internal sealed class AllWaitStrategy(IWaitStrategy[] strategies) : IWaitStrategy
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

        Task? firstFailure = null;
        while (tasks.Count > 0)
        {
            var task = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(task);
            if (!task.IsCompletedSuccessfully && firstFailure is null)
            {
                // The container cannot be ready anymore: stop the other strategies. Their own failures are only the
                // consequence of this cancellation, so the first failure is the one reported.
                firstFailure = task;
                await cts.CancelAsync().ConfigureAwait(false);
            }
        }

        if (firstFailure is not null)
        {
            await firstFailure.ConfigureAwait(false);
        }
    }

    public override string ToString() => "all of (" + string.Join(", ", (object[])strategies) + ")";
}
