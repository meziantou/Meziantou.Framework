using System.Threading.Channels;

namespace Meziantou.Framework.Threading;

public static class MixedConsumerProducer
{
    public static Task Process<T>(T initialItem, ParallelOptions options, Func<MixedConsumerProducerContext<T>, T, CancellationToken, ValueTask> action)
    {
        return Process(new[] { initialItem }, options, action);
    }

    public static async Task Process<T>(T[] initialItems, ParallelOptions options, Func<MixedConsumerProducerContext<T>, T, CancellationToken, ValueTask> action)
    {
        var degreeOfParallelism = options.MaxDegreeOfParallelism;
        if (degreeOfParallelism <= 0)
        {
            degreeOfParallelism = Environment.ProcessorCount;
        }

        var pendingItems = Channel.CreateUnbounded<T>();
        foreach (var item in initialItems)
        {
            _ = pendingItems.Writer.TryWrite(item);
        }

        var context = new MixedConsumerProducerContext<T>(pendingItems.Writer);
        var tasks = new List<Task>(degreeOfParallelism);
        var remainingConcurrency = degreeOfParallelism;
        while (await pendingItems.Reader.WaitToReadAsync(options.CancellationToken).ConfigureAwait(false))
        {
            while (TryGetItem(out var item))
            {
                // If we reach the maximum number of concurrent tasks, wait for one to finish
                while (Volatile.Read(ref remainingConcurrency) < 0)
                {
                    // The tasks collection can change while Task.WhenAny enumerates the collection
                    // so, we need to clone the collection to avoid issues
                    Task[]? clone = null;
                    lock (tasks)
                    {
                        if (tasks.Count > 0)
                            clone = tasks.ToArray();
                    }

                    if (clone != null)
                        await Task.WhenAny(clone).ConfigureAwait(false);
                }

                var task = Task.Run(async () => await action(context, item, options.CancellationToken).ConfigureAwait(false), options.CancellationToken);

                lock (tasks)
                {
                    tasks.Add(task);
                    _ = task.ContinueWith(task => OnTaskCompleted(task), options.CancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);
                }
            }

            bool TryGetItem([MaybeNullWhen(false)] out T result)
            {
                lock (tasks)
                {
                    if (pendingItems.Reader.TryRead(out var item))
                    {
                        remainingConcurrency--;
                        result = item;
                        return true;
                    }

                    result = default;
                    return false;
                }
            }

            void OnTaskCompleted(Task completedTask)
            {
                lock (tasks)
                {
                    if (!tasks.Remove(completedTask))
                        throw new InvalidOperationException("An unexpected error occurred");

                    remainingConcurrency++;

                    // There is no active tasks, so we are sure we are at the end
                    if (degreeOfParallelism == remainingConcurrency && !pendingItems.Reader.TryPeek(out _))
                        pendingItems.Writer.Complete();
                }
            }
        }
    }
}
