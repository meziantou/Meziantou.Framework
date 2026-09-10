using System.Runtime.ExceptionServices;
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

        var cancellationToken = options.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();

        if (Enumerable.TryGetNonEnumeratedCount(initialItems, out var count) && count == 0)
            return;

        // ParallelOptions treats a null scheduler as "the current one", so mirror that behavior.
        var scheduler = options.TaskScheduler ?? TaskScheduler.Current;
        var isDefaultScheduler = scheduler == TaskScheduler.Default;

        var degreeOfParallelism = options.MaxDegreeOfParallelism;
        if (degreeOfParallelism <= 0)
        {
            degreeOfParallelism = Environment.ProcessorCount;
        }

        // Extra consumers cannot buy any parallelism when the scheduler itself caps how many tasks it
        // runs at a time, so combine both limits the way ParallelOptions does.
        var maximumConcurrencyLevel = scheduler.MaximumConcurrencyLevel;
        if (maximumConcurrencyLevel > 0 && maximumConcurrencyLevel < degreeOfParallelism)
        {
            degreeOfParallelism = maximumConcurrencyLevel;
        }

        var pendingItems = Channel.CreateUnbounded<T>();
        var context = new MixedConsumerProducerContext<T>(pendingItems.Writer);

        // The initial items are streamed to the consumers while they run, so the enumeration itself
        // is a producer and must be accounted for until it completes.
        context.ReserveProducer();

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

        ExceptionDispatchInfo? enumerationFailure = null;
        try
        {
            foreach (var item in initialItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                context.Enqueue(item);
            }
        }
        catch (Exception ex)
        {
            enumerationFailure = ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
            // Must run even when the enumeration fails, otherwise the channel is never completed and
            // the consumers never stop.
            context.ReleaseProducer();
        }

        try
        {
            await Task.WhenAll(consumers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (enumerationFailure is not null)
        {
            // The enumeration failed first, so report that failure instead of the cancellation it caused.
        }

        // A failure while enumerating the initial items cuts the processing short, so it takes
        // precedence over the exceptions thrown by the actions that did run.
        enumerationFailure?.Throw();

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
                        await InvokeAsync(item).ConfigureAwait(false);
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

        // The consumer loop awaits with ConfigureAwait(false), so it resumes on the default scheduler as
        // soon as it actually suspends, be it on an empty channel or on the action itself. Starting every
        // action explicitly keeps the requested scheduler honored for all the items, and not only for the
        // ones processed before the first suspension.
        ValueTask InvokeAsync(T item)
        {
            if (isDefaultScheduler)
                return action(context, item, cancellationToken);

            // The token is deliberately not passed to StartNew: a token canceled between the check made by
            // the loop and this call would make the task transition to Canceled, and the loop would record
            // the resulting exception instead of letting the cancellation surface as such.
            return new ValueTask(Task.Factory.StartNew(() => action(context, item, cancellationToken).AsTask(), CancellationToken.None, TaskCreationOptions.DenyChildAttach, scheduler).Unwrap());
        }
    }
}
