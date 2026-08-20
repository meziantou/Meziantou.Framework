#pragma warning disable CA1861 // Avoid constant arrays as arguments
using System.Collections.Concurrent;

namespace Meziantou.Framework.Threading.Tests;
public sealed class MixedConsumerProducerTests
{
    [Fact]
    public async Task Process_EmptyData()
    {
        await MixedConsumerProducer.Process(Array.Empty<int>(), new ParallelOptions() { MaxDegreeOfParallelism = 1 }, (context, item, cancellationToken) => ValueTask.CompletedTask);
    }

    [Fact]
    public async Task Process_NoParallelism()
    {
        var count = 0;
        await MixedConsumerProducer.Process([1], new ParallelOptions() { MaxDegreeOfParallelism = 1 }, (context, item, cancellationToken) =>
        {
            if (item < 100)
            {
                context.Enqueue(item + 1);
            }

            Interlocked.Increment(ref count);
            return ValueTask.CompletedTask;
        });

        Assert.Equal(100, count);
    }

    [Fact]
    public async Task Process()
    {
        var count = 0;
        await MixedConsumerProducer.Process([0], new ParallelOptions() { MaxDegreeOfParallelism = 16 }, (context, item, cancellationToken) =>
        {
            if (item < 15)
            {
                context.Enqueue(item + 1);
                context.Enqueue(item + 2);
            }

            Interlocked.Increment(ref count);
            return ValueTask.CompletedTask;
        });

        Assert.Equal(3193, count);
    }

    [Fact]
    public async Task Process_PropagatesActionExceptions()
    {
        var exception = await Assert.ThrowsAsync<AggregateException>(async () =>
        {
            await MixedConsumerProducer.Process([1, 2, 3], new ParallelOptions() { MaxDegreeOfParallelism = 2 }, (context, item, cancellationToken) =>
            {
                throw new InvalidOperationException("boom " + item);
            });
        });

        Assert.HasCount(3, exception.InnerExceptions);
        Assert.All(exception.InnerExceptions, e => Assert.IsType<InvalidOperationException>(e));
    }

    [Fact]
    public async Task Process_PartialFailure_ProcessesAllAndAggregatesOnlyFailures()
    {
        var processed = 0;
        var exception = await Assert.ThrowsAsync<AggregateException>(async () =>
        {
            await MixedConsumerProducer.Process([1, 2, 3, 4], new ParallelOptions() { MaxDegreeOfParallelism = 1 }, (context, item, cancellationToken) =>
            {
                Interlocked.Increment(ref processed);
                if (item % 2 == 0)
                    throw new InvalidOperationException("even " + item);

                return ValueTask.CompletedTask;
            });
        });

        Assert.Equal(4, processed); // every item was attempted even though some failed
        Assert.HasCount(2, exception.InnerExceptions); // items 2 and 4
    }

    [Fact]
    public async Task Process_PreCanceledToken_DoesNotProcessAndThrows()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ran = false;
        var options = new ParallelOptions() { MaxDegreeOfParallelism = 2, CancellationToken = cts.Token };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await MixedConsumerProducer.Process([1, 2, 3], options, (context, item, cancellationToken) =>
            {
                ran = true;
                return ValueTask.CompletedTask;
            });
        });

        Assert.False(ran);
    }

    [Fact]
    public async Task Process_SingleItemWithoutEnqueue()
    {
        var processed = 0;
        await MixedConsumerProducer.Process([42], new ParallelOptions() { MaxDegreeOfParallelism = 4 }, (context, item, cancellationToken) =>
        {
            Assert.Equal(42, item);
            Interlocked.Increment(ref processed);
            return ValueTask.CompletedTask;
        });

        Assert.Equal(1, processed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Process_NeverExceedsMaxDegreeOfParallelism(int maxDegreeOfParallelism)
    {
        var current = 0;
        var observedConcurrency = new ConcurrentBag<int>();

        await MixedConsumerProducer.Process(Enumerable.Range(0, 20), new ParallelOptions() { MaxDegreeOfParallelism = maxDegreeOfParallelism }, async (context, item, cancellationToken) =>
        {
            observedConcurrency.Add(Interlocked.Increment(ref current));
            if (item < 100)
            {
                context.Enqueue(item + 20);
            }

            await Task.Yield();
            Interlocked.Decrement(ref current);
        });

        Assert.HasCount(120, observedConcurrency);
        Assert.True(observedConcurrency.Max() <= maxDegreeOfParallelism, $"Observed {observedConcurrency.Max()} concurrent actions for a max degree of parallelism of {maxDegreeOfParallelism}");
    }

    [Fact]
    public async Task Process_CancellationDuringProcessing_WaitsForRunningActions()
    {
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;
        var options = new ParallelOptions() { MaxDegreeOfParallelism = 4, CancellationToken = cts.Token };

        var task = MixedConsumerProducer.Process(Enumerable.Range(0, 100), options, async (context, item, cancellationToken) =>
        {
            Interlocked.Increment(ref running);
            try
            {
                started.TrySetResult();
                await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken.None);
            }
            finally
            {
                Interlocked.Decrement(ref running);
            }
        });

        await started.Task;
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(0, running);
    }

    [Fact]
    public async Task Process_UsesTaskSchedulerFromOptions()
    {
        var scheduler = new CountingTaskScheduler();
        var options = new ParallelOptions() { MaxDegreeOfParallelism = 2, TaskScheduler = scheduler };
        var observedSchedulers = new ConcurrentBag<TaskScheduler>();

        await MixedConsumerProducer.Process([1, 2, 3, 4], options, (context, item, cancellationToken) =>
        {
            observedSchedulers.Add(TaskScheduler.Current);
            return ValueTask.CompletedTask;
        });

        Assert.HasCount(4, observedSchedulers);
        Assert.All(observedSchedulers, current => Assert.Same(scheduler, current));
        Assert.True(scheduler.QueuedTaskCount > 0);
    }

    [Fact]
    public async Task Process_NullArguments()
    {
        var options = new ParallelOptions();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => MixedConsumerProducer.Process<int>(initialItems: null!, options, (context, item, cancellationToken) => ValueTask.CompletedTask));
        Assert.Equal("initialItems", exception.ParamName);

        exception = await Assert.ThrowsAsync<ArgumentNullException>(() => MixedConsumerProducer.Process([1], options: null!, (context, item, cancellationToken) => ValueTask.CompletedTask));
        Assert.Equal("options", exception.ParamName);

        exception = await Assert.ThrowsAsync<ArgumentNullException>(() => MixedConsumerProducer.Process([1], options, action: null!));
        Assert.Equal("action", exception.ParamName);
    }

    private sealed class CountingTaskScheduler : TaskScheduler
    {
        private int _queuedTaskCount;

        public int QueuedTaskCount => Volatile.Read(ref _queuedTaskCount);

        protected override IEnumerable<Task> GetScheduledTasks() => [];

        protected override void QueueTask(Task task)
        {
            Interlocked.Increment(ref _queuedTaskCount);
            _ = Task.Run(() => TryExecuteTask(task));
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
    }
}
