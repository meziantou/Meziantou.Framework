#pragma warning disable CA1861 // Avoid constant arrays as arguments
using System.Collections.Concurrent;

namespace Meziantou.Framework.Threading.Tests;
public sealed class MixedConsumerProducerTests
{
    [Fact]
    public async Task MixedConsumerProducer_Example()
    {
        #region MixedConsumerProducer_Process
        // A fake website where each page links to other pages
        var website = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["https://example.com/"] = ["https://example.com/a", "https://example.com/b"],
            ["https://example.com/a"] = ["https://example.com/a/1"],
        };
        var crawledPages = new ConcurrentBag<string>();

        async Task<string[]> CrawlPageAsync(string url, CancellationToken cancellationToken)
        {
            await Task.Delay(1, cancellationToken);
            crawledPages.Add(url);
            return website.GetValueOrDefault(url, []);
        }

        var initialUrls = new[] { "https://example.com/" };
        await MixedConsumerProducer.Process(
            initialUrls,
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (context, url, ct) =>
            {
                var links = await CrawlPageAsync(url, ct);
                foreach (var link in links)
                {
                    context.Enqueue(link);
                }
            });

        Assert.Equal(["https://example.com/", "https://example.com/a", "https://example.com/a/1", "https://example.com/b"], crawledPages.Order(StringComparer.Ordinal));
        #endregion
    }

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
    public async Task Process_PreCanceledToken_DoesNotEnumerateInitialItems()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var enumerated = false;
        var options = new ParallelOptions() { MaxDegreeOfParallelism = 2, CancellationToken = cts.Token };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MixedConsumerProducer.Process(GetItems(), options, (context, item, cancellationToken) => ValueTask.CompletedTask));

        Assert.False(enumerated);

        IEnumerable<int> GetItems()
        {
            enumerated = true;
            yield return 1;
        }
    }

    [Fact]
    public async Task Process_StreamsInitialItemsWhileConsumersRun()
    {
        var firstItemProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processed = new ConcurrentBag<int>();

        await MixedConsumerProducer.Process(GetItems(), new ParallelOptions() { MaxDegreeOfParallelism = 2 }, (context, item, cancellationToken) =>
        {
            processed.Add(item);
            if (item == 0)
            {
                firstItemProcessed.TrySetResult();
            }

            return ValueTask.CompletedTask;
        });

        Assert.HasCount(2, processed);
        Assert.Contains(0, processed);
        Assert.Contains(1, processed);

        IEnumerable<int> GetItems()
        {
            yield return 0;

            // The consumers must already be draining the channel while the initial items are being
            // enumerated, otherwise the first item is never processed and this never completes.
            Assert.True(firstItemProcessed.Task.Wait(TimeSpan.FromSeconds(60)), "The first item was not processed while the initial items were still being enumerated");
            yield return 1;
        }
    }

    [Fact]
    public async Task Process_CancellationDuringEnumeration_StopsEnumeratingInitialItems()
    {
        const int TotalItems = 100_000;

        using var cts = new CancellationTokenSource();
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enumerated = 0;
        var options = new ParallelOptions() { MaxDegreeOfParallelism = 2, CancellationToken = cts.Token };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MixedConsumerProducer.Process(GetItems(), options, async (context, item, cancellationToken) =>
        {
            await cts.CancelAsync();
            canceled.TrySetResult();
        }));

        Assert.True(enumerated < TotalItems, $"The whole sequence was enumerated ({enumerated} items) instead of stopping once the token was canceled");

        IEnumerable<int> GetItems()
        {
            for (var i = 0; i < TotalItems; i++)
            {
                enumerated++;
                yield return i;

                if (i == 0)
                {
                    // The first item must reach a consumer while the sequence is still being
                    // enumerated, otherwise the cancellation never happens during the enumeration.
                    canceled.Task.Wait(TimeSpan.FromSeconds(60));
                }
            }
        }
    }

    [Fact]
    public async Task Process_InitialItemsEnumerationThrows_ProcessesEnqueuedItemsAndPropagates()
    {
        var processed = new ConcurrentBag<int>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => MixedConsumerProducer.Process(GetItems(), new ParallelOptions() { MaxDegreeOfParallelism = 2 }, (context, item, cancellationToken) =>
        {
            processed.Add(item);
            return ValueTask.CompletedTask;
        }));

        Assert.Equal("boom", exception.Message);
        Assert.Contains(1, processed);

        static IEnumerable<int> GetItems()
        {
            yield return 1;
            throw new InvalidOperationException("boom");
        }
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
    public async Task Process_UsesTaskSchedulerFromOptions_AfterActionSuspends()
    {
        using var scheduler = new MonoThreadedTaskScheduler(nameof(Process_UsesTaskSchedulerFromOptions_AfterActionSuspends));
        var options = new ParallelOptions() { MaxDegreeOfParallelism = 1, TaskScheduler = scheduler };
        var observedSchedulers = new ConcurrentBag<TaskScheduler>();

        await MixedConsumerProducer.Process([1, 2, 3], options, async (context, item, cancellationToken) =>
        {
            observedSchedulers.Add(TaskScheduler.Current);
            await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
        });

        Assert.HasCount(3, observedSchedulers);
        Assert.All(observedSchedulers, current => Assert.Same(scheduler, current));
    }

    [Fact]
    public async Task Process_UsesTaskSchedulerFromOptions_AfterEmptyChannelSuspends()
    {
        var scheduler = new CountingTaskScheduler();
        var options = new ParallelOptions() { MaxDegreeOfParallelism = 2, TaskScheduler = scheduler };
        var observedSchedulers = new ConcurrentDictionary<int, TaskScheduler>();
        var secondItemProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await MixedConsumerProducer.Process([0], options, async (context, item, cancellationToken) =>
        {
            observedSchedulers[item] = TaskScheduler.Current;
            if (item is 0)
            {
                // The second consumer has nothing to read, so it is parked on the empty channel. Keep this
                // consumer busy until the item it enqueues has been processed, so the item can only be
                // picked up by the consumer resuming from that suspension.
                context.Enqueue(1);
                await secondItemProcessed.Task;
            }
            else
            {
                secondItemProcessed.SetResult();
            }
        });

        Assert.Same(scheduler, observedSchedulers[0]);
        Assert.Same(scheduler, observedSchedulers[1]);
    }

    [Fact]
    public async Task Process_LimitsConsumersToSchedulerMaximumConcurrencyLevel()
    {
        using var scheduler = new MonoThreadedTaskScheduler(nameof(Process_LimitsConsumersToSchedulerMaximumConcurrencyLevel));
        var options = new ParallelOptions() { MaxDegreeOfParallelism = 4, TaskScheduler = scheduler };
        var current = 0;
        var observedConcurrency = new ConcurrentBag<int>();

        await MixedConsumerProducer.Process(Enumerable.Range(0, 8), options, async (context, item, cancellationToken) =>
        {
            observedConcurrency.Add(Interlocked.Increment(ref current));
            await Task.Yield();
            Interlocked.Decrement(ref current);
        });

        Assert.HasCount(8, observedConcurrency);
        Assert.Equal(1, observedConcurrency.Max());
    }

    [Fact]
    public async Task Process_SchedulerRejectsConsumer_CompletesStartedConsumersAndThrows()
    {
        var scheduler = new RejectingTaskScheduler(acceptedTaskCount: 1);
        var options = new ParallelOptions() { MaxDegreeOfParallelism = 2, TaskScheduler = scheduler };
        var enumerated = false;

        var exception = await Assert.ThrowsAsync<TaskSchedulerException>(() => MixedConsumerProducer.Process(GetItems(), options, (context, item, cancellationToken) => ValueTask.CompletedTask));
        Assert.IsType<InvalidOperationException>(exception.InnerException);

        var startedConsumer = Assert.Single(scheduler.AcceptedTasks);
        var consumer = ((Task<Task>)startedConsumer).Unwrap();
        Assert.True(consumer.IsCompletedSuccessfully);
        Assert.False(enumerated);

        IEnumerable<int> GetItems()
        {
            enumerated = true;
            yield return 1;
        }
    }

    [Fact]
    public async Task Process_MaxPendingItems_Example()
    {
        #region MixedConsumerProducer_MaxPendingItems
        var processedItems = new ConcurrentBag<int>();
        var options = new MixedConsumerProducerOptions
        {
            // Up to 4 items are processed at the same time...
            MaxDegreeOfParallelism = 4,
            // ...and up to 100 items wait to be processed
            MaxPendingItems = 100,
        };

        await MixedConsumerProducer.Process(
            Enumerable.Range(0, 1000),
            options,
            async (context, item, ct) =>
            {
                processedItems.Add(item);
                if (item < 1000)
                {
                    // Waits while 100 items are pending, so the backlog stays bounded
                    await context.EnqueueAsync(item + 1000, ct);
                }
            });

        Assert.HasCount(2000, processedItems);
        #endregion
    }

    [Fact]
    public void MixedConsumerProducerOptions_MaxPendingItems_Validation()
    {
        var options = new MixedConsumerProducerOptions();
        Assert.Equal(-1, options.MaxPendingItems);

        options.MaxPendingItems = 1;
        Assert.Equal(1, options.MaxPendingItems);
        options.MaxPendingItems = -1;
        Assert.Equal(-1, options.MaxPendingItems);

        Assert.Throws<ArgumentOutOfRangeException>("value", () => options.MaxPendingItems = 0);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => options.MaxPendingItems = -2);
    }

    [Fact]
    public async Task Process_MaxPendingItems_BoundsInitialEnumeration()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var yielded = 0;
        var processed = 0;
        var maxQueued = 0;
        var options = new MixedConsumerProducerOptions() { MaxDegreeOfParallelism = 1, MaxPendingItems = 2 };

        var processing = MixedConsumerProducer.Process(GetItems(), options, async (context, item, cancellationToken) =>
        {
            InterlockedMax(ref maxQueued, context.QueuedItemCount);
            if (item is 0)
            {
                await gate.Task;
            }

            Interlocked.Increment(ref processed);
        });

        // Item 0 is being processed, items 1 and 2 are pending, and item 3 waits for room
        while (Volatile.Read(ref yielded) < 4)
        {
            await Task.Delay(10);
        }

        await Task.Delay(100);
        Assert.Equal(4, Volatile.Read(ref yielded));
        Assert.Equal(0, Volatile.Read(ref processed));

        gate.SetResult();
        await processing.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(100, processed);
        Assert.Equal(100, yielded);
        Assert.True(maxQueued <= 2, $"Queued {maxQueued} items");

        IEnumerable<int> GetItems()
        {
            for (var i = 0; i < 100; i++)
            {
                Interlocked.Increment(ref yielded);
                yield return i;
            }
        }
    }

    [Fact]
    public async Task Process_MaxPendingItems_HighFanOutWithSlowConsumers()
    {
        const int MaxPendingItems = 5;
        var processed = 0;
        var maxQueued = 0;
        var options = new MixedConsumerProducerOptions() { MaxDegreeOfParallelism = 3, MaxPendingItems = MaxPendingItems };

        // Items are depths in a tree: the items of depth 0 and 1 have 10 children, so 10 + 100 + 1000 items are processed
        await MixedConsumerProducer.Process(Enumerable.Repeat(0, 10), options, async (context, depth, cancellationToken) =>
        {
            Interlocked.Increment(ref processed);
            InterlockedMax(ref maxQueued, context.QueuedItemCount);
            await Task.Delay(1, cancellationToken);

            if (depth < 2)
            {
                for (var i = 0; i < 10; i++)
                {
                    await context.EnqueueAsync(depth + 1, cancellationToken);
                    InterlockedMax(ref maxQueued, context.QueuedItemCount);
                }
            }
        }).WaitAsync(TimeSpan.FromSeconds(120));

        Assert.Equal(1110, processed);
        Assert.True(maxQueued <= MaxPendingItems, $"Queued {maxQueued} items");
    }

    [Fact]
    public async Task Process_MaxPendingItems_AllConsumersEnqueueIntoFullQueue()
    {
        var parentsStarted = 0;
        var parentsReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processed = new ConcurrentBag<int>();
        var maxQueued = 0;
        var options = new MixedConsumerProducerOptions() { MaxDegreeOfParallelism = 2, MaxPendingItems = 1 };

        await MixedConsumerProducer.Process([0, 1, 2], options, async (context, item, cancellationToken) =>
        {
            processed.Add(item);
            InterlockedMax(ref maxQueued, context.QueuedItemCount);
            if (item is 0 or 1)
            {
                // Both consumers process an item while item 2 fills the queue, then both enqueue children
                if (Interlocked.Increment(ref parentsStarted) == 2)
                {
                    parentsReady.SetResult();
                }

                await parentsReady.Task;
                while (context.QueuedItemCount < 1)
                {
                    await Task.Delay(1, cancellationToken);
                }

                for (var i = 0; i < 5; i++)
                {
                    await context.EnqueueAsync(10 + (item * 10) + i, cancellationToken);
                }
            }
        }).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal([0, 1, 2, 10, 11, 12, 13, 14, 20, 21, 22, 23, 24], processed.Order());
        Assert.Equal(1, maxQueued);
    }

    [Fact]
    public async Task Process_MaxPendingItems_ExceptionOfItemProcessedByEnqueueAsyncIsAggregated()
    {
        var options = new MixedConsumerProducerOptions() { MaxDegreeOfParallelism = 1, MaxPendingItems = 1 };

        var exception = await Assert.ThrowsAsync<AggregateException>(() => MixedConsumerProducer.Process([0], options, async (context, item, cancellationToken) =>
        {
            if (item is 0)
            {
                // With a single consumer, the second item cannot wait for room and is processed by this call
                await context.EnqueueAsync(1, cancellationToken);
                await context.EnqueueAsync(2, cancellationToken);
                return;
            }

            throw new InvalidOperationException("boom " + item);
        }));

        Assert.Equal(["boom 1", "boom 2"], exception.InnerExceptions.Select(e => e.Message).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Process_MaxPendingItems_EnqueueRejectsItemsWhenFull()
    {
        var options = new MixedConsumerProducerOptions() { MaxDegreeOfParallelism = 1, MaxPendingItems = 1 };
        var processed = new ConcurrentBag<int>();

        await MixedConsumerProducer.Process([0], options, (context, item, cancellationToken) =>
        {
            processed.Add(item);
            if (item is 0)
            {
                Assert.True(context.TryEnqueue(1));
                Assert.False(context.TryEnqueue(2));
                Assert.Throws<InvalidOperationException>(() => context.Enqueue(2));
            }

            return ValueTask.CompletedTask;
        });

        Assert.Equal([0, 1], processed.Order());
    }

    [Fact]
    public async Task Process_MaxPendingItems_CancellationWhileEnumerationWaits()
    {
        using var cts = new CancellationTokenSource();
        var yielded = 0;
        var options = new MixedConsumerProducerOptions() { MaxDegreeOfParallelism = 1, MaxPendingItems = 1, CancellationToken = cts.Token };

        var processing = MixedConsumerProducer.Process(GetItems(), options, async (context, item, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        });

        // Item 0 is being processed, item 1 is pending, and item 2 waits for room
        while (Volatile.Read(ref yielded) < 3)
        {
            await Task.Delay(10);
        }

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing.WaitAsync(TimeSpan.FromSeconds(60)));
        Assert.Equal(3, yielded);

        IEnumerable<int> GetItems()
        {
            while (true)
            {
                Interlocked.Increment(ref yielded);
                yield return 0;
            }
        }
    }

    [Fact]
    public async Task Process_MaxPendingItems_CancellationWhileEnqueueAsyncWaits()
    {
        using var cts = new CancellationTokenSource();
        var enqueueWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new MixedConsumerProducerOptions() { MaxDegreeOfParallelism = 2, MaxPendingItems = 1, CancellationToken = cts.Token };

        var processing = MixedConsumerProducer.Process([0, 1], options, async (context, item, cancellationToken) =>
        {
            if (item is 0)
            {
                // The other consumer is busy, so this call waits for room instead of processing the item itself
                await context.EnqueueAsync(10, cancellationToken);
                var pending = context.EnqueueAsync(11, cancellationToken);
                enqueueWaiting.SetResult();
                await pending;
            }
            else if (item is 1)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        });

        await enqueueWaiting.Task.WaitAsync(TimeSpan.FromSeconds(60));
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing.WaitAsync(TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public async Task Process_MixedConsumerProducerOptions_DefaultIsUnbounded()
    {
        var options = new MixedConsumerProducerOptions() { MaxDegreeOfParallelism = 1 };
        var processed = 0;

        await MixedConsumerProducer.Process([0], options, (context, item, cancellationToken) =>
        {
            if (item is 0)
            {
                for (var i = 1; i <= 1000; i++)
                {
                    context.Enqueue(i);
                }
            }

            Interlocked.Increment(ref processed);
            return ValueTask.CompletedTask;
        });

        Assert.Equal(1001, processed);
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

    private static void InterlockedMax(ref int location, int value)
    {
        var current = Volatile.Read(ref location);
        while (value > current)
        {
            var previous = Interlocked.CompareExchange(ref location, value, current);
            if (previous == current)
                return;

            current = previous;
        }
    }

    // Runs the first tasks on the thread pool and rejects the next ones, like a scheduler that is shutting down
    private sealed class RejectingTaskScheduler(int acceptedTaskCount) : TaskScheduler
    {
        private readonly ConcurrentQueue<Task> _acceptedTasks = new();
        private int _queuedTaskCount;

        public IReadOnlyCollection<Task> AcceptedTasks => _acceptedTasks;

        protected override IEnumerable<Task> GetScheduledTasks() => [];

        protected override void QueueTask(Task task)
        {
            if (Interlocked.Increment(ref _queuedTaskCount) > acceptedTaskCount)
                throw new InvalidOperationException("The scheduler is shutting down");

            _acceptedTasks.Enqueue(task);
            _ = Task.Run(() => TryExecuteTask(task));
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
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
