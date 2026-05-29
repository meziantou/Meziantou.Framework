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

    private Task EnqueueTask()
    {
        return Task.Factory.StartNew(() =>
        {
            Assert.Equal(ThreadName, Thread.CurrentThread.Name);
            _count++;
        }, CancellationToken.None, TaskCreationOptions.None, _taskScheduler);
    }
}
