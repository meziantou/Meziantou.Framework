---
title: Scheduling work
description: Run tasks on a single dedicated thread with MonoThreadedTaskScheduler, and process items in parallel while processing adds new ones with MixedConsumerProducer.
---

## Run tasks on a dedicated thread

[MonoThreadedTaskScheduler](cref:Meziantou.Framework.Threading.MonoThreadedTaskScheduler) is a `TaskScheduler` that runs every task queued to it on one thread it creates, one task at a
time. Use it for code that must always run on the same thread, such as a component that is not thread-safe or that
requires thread affinity:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/MonoThreadedTaskSchedulerTests.cs;region=MonoThreadedTaskScheduler_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
using var scheduler = new MonoThreadedTaskScheduler("MyWorkerThread");
var threadName = await Task.Factory.StartNew(
    () => Thread.CurrentThread.Name,
    CancellationToken.None,
    TaskCreationOptions.None,
    scheduler);
Assert.Equal("MyWorkerThread", threadName);
```
<!-- endref -->

The thread name given to the constructor helps identify the thread in a debugger. Tasks queued to the scheduler run in
order on that thread:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/MonoThreadedTaskSchedulerTests.cs;region=MonoThreadedTaskScheduler_MultipleTasks;format=md-fenced-code-block;dedent=true -->
```csharp
using var scheduler = new MonoThreadedTaskScheduler("MyWorkerThread")
{
    // Run the tasks already queued when the scheduler is disposed
    DequeueOnDispose = true,
};

// Every task queued to the scheduler runs on the same dedicated thread, one at a time
Task<int> GetThreadIdAsync() => Task.Factory.StartNew(
    () => Environment.CurrentManagedThreadId,
    CancellationToken.None,
    TaskCreationOptions.None,
    scheduler);

var threadIds = await Task.WhenAll(GetThreadIdAsync(), GetThreadIdAsync());

Assert.Equal(threadIds[0], threadIds[1]);
```
<!-- endref -->

### Dispose the scheduler

Dispose the scheduler to stop its thread. No task is accepted after [Dispose](cref:Meziantou.Framework.Threading.MonoThreadedTaskScheduler.Dispose) is called.

| Property | Default | Effect |
| --- | --- | --- |
| [DequeueOnDispose](cref:Meziantou.Framework.Threading.MonoThreadedTaskScheduler.DequeueOnDispose) | `false` | `true` runs every task already queued before the thread exits. `false` abandons the tasks that have not started: they never complete |
| [DisposeThreadJoinTimeout](cref:Meziantou.Framework.Threading.MonoThreadedTaskScheduler.DisposeThreadJoinTimeout) | 1 second | How long [Dispose](cref:Meziantou.Framework.Threading.MonoThreadedTaskScheduler.Dispose) waits for the thread to finish. The wait is skipped when [Dispose](cref:Meziantou.Framework.Threading.MonoThreadedTaskScheduler.Dispose) is called from the scheduler's own thread |
| [WaitTimeout](cref:Meziantou.Framework.Threading.MonoThreadedTaskScheduler.WaitTimeout) | `Timeout.InfiniteTimeSpan` | How long the idle thread waits before waking up. Queuing a task or disposing the scheduler wakes it anyway, so set a finite value only to wake it periodically |

- [QueueCount](cref:Meziantou.Framework.Threading.MonoThreadedTaskScheduler.QueueCount) gives the number of tasks waiting to run.
- `MaximumConcurrencyLevel` is always 1.
- When an exception terminates the thread, [WorkerException](cref:Meziantou.Framework.Threading.MonoThreadedTaskScheduler.WorkerException) holds it and the scheduler refuses new tasks. The thread is
  not replaced, because that would break the guarantee that every task runs on the same thread.

## Process items that produce more items

[MixedConsumerProducer.Process](cref:Meziantou.Framework.Threading.MixedConsumerProducer.Process) processes items in parallel, and the processing of an item can add new items to process,
for instance the links found on a crawled page. It completes once every item, including the added ones, is processed:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/MixedConsumerProducerTests.cs;region=MixedConsumerProducer_Process;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

- The action receives a [MixedConsumerProducerContext\<T\>](cref:Meziantou.Framework.Threading.MixedConsumerProducerContext{T}), whose [Enqueue](cref:Meziantou.Framework.Threading.MixedConsumerProducerContext{T}.Enqueue) method adds an item, the item to process, and a
  `CancellationToken`.
- `ParallelOptions.MaxDegreeOfParallelism`, `CancellationToken` and `TaskScheduler` are honored. The default
  `MaxDegreeOfParallelism`, -1, uses one consumer per processor.
- An exception thrown by the action does not stop the processing: the other items are still processed, and [Process](cref:Meziantou.Framework.Threading.MixedConsumerProducer.Process)
  then throws an `AggregateException` with all the exceptions.
- Canceling `ParallelOptions.CancellationToken` makes [Process](cref:Meziantou.Framework.Threading.MixedConsumerProducer.Process) throw an `OperationCanceledException`.
- Items are not deduplicated: an item enqueued twice is processed twice. Track the processed items yourself, for instance
  with a [`ConcurrentHashSet<T>`](06-ConcurrentCollections.md#a-thread-safe-set-with-concurrenthashset), when the same
  item can be found several times.
- A `TaskScheduler` that rejects a consumer, for instance because it is shutting down, makes [Process](cref:Meziantou.Framework.Threading.MixedConsumerProducer.Process) throw the
  `TaskSchedulerException` once the consumers that did start are stopped. No item is processed.

### Limit the pending items

`MaxDegreeOfParallelism` limits the number of items processed at the same time, not the number of items waiting to be
processed: by default, a fast enumeration of the initial items or actions that enqueue many items can accumulate a
large backlog in memory. To limit it, pass a [MixedConsumerProducerOptions](cref:Meziantou.Framework.Threading.MixedConsumerProducerOptions) and set its
[MaxPendingItems](cref:Meziantou.Framework.Threading.MixedConsumerProducerOptions.MaxPendingItems), then enqueue the items with
[EnqueueAsync](cref:Meziantou.Framework.Threading.MixedConsumerProducerContext{T}.EnqueueAsync):

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/MixedConsumerProducerTests.cs;region=MixedConsumerProducer_MaxPendingItems;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

The two limits are distinct: here up to 4 items are processed and up to 100 other items wait at the same time.
When [MaxPendingItems](cref:Meziantou.Framework.Threading.MixedConsumerProducerOptions.MaxPendingItems) items are waiting:

- The enumeration of the initial items waits until an item is dequeued, so it does not run ahead of the consumers.
- [EnqueueAsync](cref:Meziantou.Framework.Threading.MixedConsumerProducerContext{T}.EnqueueAsync) waits as well. When every consumer would wait, no consumer would be left to dequeue an item, so
  the last one processes its item itself before [EnqueueAsync](cref:Meziantou.Framework.Threading.MixedConsumerProducerContext{T}.EnqueueAsync) returns. The processing never deadlocks, and an
  exception thrown by that item is reported by [Process](cref:Meziantou.Framework.Threading.MixedConsumerProducer.Process) with the others.
- [Enqueue](cref:Meziantou.Framework.Threading.MixedConsumerProducerContext{T}.Enqueue) cannot wait: it throws an `InvalidOperationException`.
  [TryEnqueue](cref:Meziantou.Framework.Threading.MixedConsumerProducerContext{T}.TryEnqueue) returns `false` instead, to let you decide what to do with the item. No item is
  dropped silently.
- Canceling `CancellationToken` stops the waits, and [Process](cref:Meziantou.Framework.Threading.MixedConsumerProducer.Process) throws an `OperationCanceledException`.

The default value of [MaxPendingItems](cref:Meziantou.Framework.Threading.MixedConsumerProducerOptions.MaxPendingItems), -1, does not limit the pending items.
