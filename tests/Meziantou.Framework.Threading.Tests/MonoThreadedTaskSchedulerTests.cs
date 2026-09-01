namespace Meziantou.Framework.Threading.Tests;

public sealed class MonoThreadedTaskSchedulerTests : IDisposable
{
    private const string ThreadName = "Test";

    private readonly MonoThreadedTaskScheduler _taskScheduler;
    private int _count;

    public MonoThreadedTaskSchedulerTests()
    {
        _taskScheduler = new MonoThreadedTaskScheduler(ThreadName);
    }

    public void Dispose()
    {
        _taskScheduler.Dispose();
    }

    [Fact]
    public void MaximumConcurrencyLevel_IsOne()
    {
        // The TPL reads this to size partitions; the base TaskScheduler value is int.MaxValue, which is the
        // wrong answer for a scheduler that runs everything on a single thread.
        Assert.Equal(1, _taskScheduler.MaximumConcurrencyLevel);
    }

    [Fact]
    public async Task SequentialEnqueue()
    {
        const int Count = 1000;
        for (var i = 0; i < Count; i++)
        {
            await EnqueueTask();
        }

        Assert.Equal(Count, _count);
    }

    [Fact]
    public async Task ParallelEnqueue()
    {
        const int Count = 1000;
        var tasks = new Task[Count];
        for (var i = 0; i < Count; i++)
        {
            tasks[i] = EnqueueTask();
        }

        await Task.WhenAll(tasks);
        Assert.Equal(Count, _count);
    }

    [Fact]
    public async Task AllTasksRunOnTheSameThread()
    {
        using var scheduler = new MonoThreadedTaskScheduler("single");
        var ids = new int[200];
        var tasks = new Task[ids.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            var index = i;
            tasks[i] = Task.Factory.StartNew(
                () => ids[index] = Environment.CurrentManagedThreadId,
                CancellationToken.None,
                TaskCreationOptions.None,
                scheduler);
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(ids.Distinct());
    }

    [Fact]
    public async Task FaultedTask_PropagatesException()
    {
        using var scheduler = new MonoThreadedTaskScheduler("fault");
        var task = Task.Factory.StartNew(
            () => throw new InvalidOperationException("boom"),
            CancellationToken.None,
            TaskCreationOptions.None,
            scheduler);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var scheduler = new MonoThreadedTaskScheduler("idempotent");
        scheduler.Dispose();
        scheduler.Dispose();
    }

    [Fact]
    public void DequeueOnDispose_RunsPendingTasks()
    {
        using var started = new ManualResetEventSlim(initialState: false);
        using var release = new ManualResetEventSlim(initialState: false);
        var executed = 0;

        var scheduler = new MonoThreadedTaskScheduler("dequeue") { DequeueOnDispose = true };

        // Occupy the single worker thread so the following tasks stay queued.
        _ = Task.Factory.StartNew(
            () =>
            {
                started.Set();
                release.Wait();
            },
            CancellationToken.None,
            TaskCreationOptions.None,
            scheduler);

        started.Wait();

        for (var i = 0; i < 5; i++)
        {
            _ = Task.Factory.StartNew(
                () => Interlocked.Increment(ref executed),
                CancellationToken.None,
                TaskCreationOptions.None,
                scheduler);
        }

        release.Set();
        scheduler.Dispose();

        Assert.Equal(5, executed);
    }

    [Fact]
    public async Task Dispose_DoesNotRunTasksOnTheDisposingThread()
    {
        // The join below times out while the first task is still running. Draining on the disposing thread
        // would then execute the queued tasks concurrently with the still-live worker.
        using var started = new ManualResetEventSlim(initialState: false);
        var threadIds = new System.Collections.Concurrent.ConcurrentBag<int>();

        var scheduler = new MonoThreadedTaskScheduler("shutdown")
        {
            DequeueOnDispose = true,
            DisposeThreadJoinTimeout = TimeSpan.FromMilliseconds(50),
        };

        _ = Task.Factory.StartNew(
            () =>
            {
                started.Set();
                Thread.Sleep(500);
                threadIds.Add(Environment.CurrentManagedThreadId);
            },
            CancellationToken.None,
            TaskCreationOptions.None,
            scheduler);

        started.Wait();

        var queued = new Task[5];
        for (var i = 0; i < queued.Length; i++)
        {
            queued[i] = Task.Factory.StartNew(
                () => threadIds.Add(Environment.CurrentManagedThreadId),
                CancellationToken.None,
                TaskCreationOptions.None,
                scheduler);
        }

        scheduler.Dispose();

        await Task.WhenAll(queued).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.HasCount(6, threadIds);
        Assert.Single(threadIds.Distinct());
        Assert.DoesNotContain(Environment.CurrentManagedThreadId, threadIds);
    }

    [Fact]
    public void QueueTask_AfterDispose_Throws()
    {
        var scheduler = new MonoThreadedTaskScheduler("disposed");
        scheduler.Dispose();

        var exception = Assert.Throws<TaskSchedulerException>(() =>
        {
            _ = Task.Factory.StartNew(
                () => { },
                CancellationToken.None,
                TaskCreationOptions.None,
                scheduler);
        });

        Assert.IsType<ObjectDisposedException>(exception.InnerException);
    }

    private Task EnqueueTask()
    {
        return Task.Factory.StartNew(() =>
        {
            Assert.Equal(ThreadName, Thread.CurrentThread.Name);
            _count++;
        }, CancellationToken.None, TaskCreationOptions.None, _taskScheduler);
    }
}
