using System.Threading.Channels;

namespace Meziantou.Framework.Threading;

/// <summary>Provides a parallel processing utility that allows consumers to dynamically add new items to be processed.</summary>
public static class MixedConsumerProducer
{
    /// <summary>Processes items in parallel, where each processing action can enqueue additional items to be processed.</summary>
    /// <typeparam name="T">The type of items to process.</typeparam>
    /// <param name="initialItems">The initial collection of items to process.</param>
    /// <param name="options">Options that configure the parallel processing. <see cref="ParallelOptions.MaxDegreeOfParallelism"/>, <see cref="ParallelOptions.CancellationToken"/>, and <see cref="ParallelOptions.TaskScheduler"/> are honored.</param>
    /// <param name="action">The action to perform on each item. The action receives a context to enqueue new items, the current item, and a cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="AggregateException">One or more invocations of <paramref name="action"/> threw an exception. Remaining items are still processed, and all exceptions are reported once processing completes.</exception>
    /// <exception cref="OperationCanceledException"><see cref="ParallelOptions.CancellationToken"/> was canceled.</exception>
    /// <example>
    /// <code><![CDATA[
    /// var initialUrls = new[] { "https://example.com" };
    /// await MixedConsumerProducer.Process(
    ///     initialUrls,
    ///     new ParallelOptions { MaxDegreeOfParallelism = 4 },
    ///     async (context, url, ct) =>
    ///     {
    ///         var links = await CrawlPageAsync(url, ct);
    ///         foreach (var link in links)
    ///         {
    ///             context.Enqueue(link);
    ///         }
    ///     });
    /// ]]></code>
    /// </example>
    public static async Task Process<T>(IEnumerable<T> initialItems, ParallelOptions options, Func<MixedConsumerProducerContext<T>, T, CancellationToken, ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(initialItems);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(action);

        if (Enumerable.TryGetNonEnumeratedCount(initialItems, out var count) && count == 0)
            return;

        var pendingItems = Channel.CreateUnbounded<T>();
        var context = new MixedConsumerProducerContext<T>(pendingItems.Writer);
        var hasItem = false;
        foreach (var item in initialItems)
        {
            context.Enqueue(item);
            hasItem = true;
        }

        if (!hasItem)
            return;

        var degreeOfParallelism = options.MaxDegreeOfParallelism;
        if (degreeOfParallelism <= 0)
        {
            degreeOfParallelism = Environment.ProcessorCount;
        }

        var cancellationToken = options.CancellationToken;
        // ParallelOptions treats a null scheduler as "the current one", so mirror that behavior.
        var scheduler = options.TaskScheduler ?? TaskScheduler.Current;

        var exceptionsLock = new Lock();
        List<Exception>? exceptions = null;

        // Consumers are long-lived: each one drains the channel until it is completed, which happens
        // once no item is pending. This bounds the concurrency to degreeOfParallelism without having
        // to throttle the loop that dispatches the items.
        var consume = ConsumeAsync;
        var consumers = new Task[degreeOfParallelism];
        for (var i = 0; i < consumers.Length; i++)
        {
            consumers[i] = Task.Factory.StartNew(consume, cancellationToken, TaskCreationOptions.DenyChildAttach, scheduler).Unwrap();
        }

        await Task.WhenAll(consumers).ConfigureAwait(false);

        // All consumers have completed, so nothing can be mutating the list anymore.
        if (exceptions is not null)
            throw new AggregateException(exceptions);

        async Task ConsumeAsync()
        {
            var reader = pendingItems.Reader;
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (!cancellationToken.IsCancellationRequested && reader.TryRead(out var item))
                {
                    try
                    {
                        await action(context, item, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Record the exception and keep processing the remaining items. Letting it
                        // escape would take a consumer down and leave the item accounted for as
                        // pending, which would prevent the channel from ever being completed.
                        lock (exceptionsLock)
                        {
                            exceptions ??= [];
                            exceptions.Add(ex);
                        }
                    }
                    finally
                    {
                        context.OnItemProcessed();
                    }
                }
            }
        }
    }
}
