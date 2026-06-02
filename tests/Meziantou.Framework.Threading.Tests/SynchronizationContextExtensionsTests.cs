using System.Collections.Concurrent;

namespace Meziantou.Framework.Threading.Tests;

public sealed class SynchronizationContextExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void GetAwaiter_NullSynchronizationContext_Throws()
    {
        SynchronizationContext? synchronizationContext = null;

        Assert.Throws<ArgumentNullException>(() => synchronizationContext!.GetAwaiter());
    }

    [Fact]
    public async Task GetAwaiter_PostsContinuationToSynchronizationContext()
    {
        using var synchronizationContext = new DedicatedThreadSynchronizationContext();

        var result = await Task.Run(async () =>
        {
            var beforeAwaitThreadId = Environment.CurrentManagedThreadId;
            await synchronizationContext;

            return new AwaitResult(
                beforeAwaitThreadId,
                Environment.CurrentManagedThreadId,
                SynchronizationContext.Current,
                synchronizationContext.PostCount);
        }).WaitAsync(Timeout);

        Assert.NotEqual(synchronizationContext.ManagedThreadId, result.BeforeAwaitThreadId);
        Assert.Equal(synchronizationContext.ManagedThreadId, result.AfterAwaitThreadId);
        Assert.Same(synchronizationContext, result.CurrentSynchronizationContext);
        Assert.Equal(1, result.PostCount);
    }

    [Fact]
    public async Task GetAwaiter_OnTargetSynchronizationContext_CompletesSynchronously()
    {
        using var synchronizationContext = new DedicatedThreadSynchronizationContext();

        var result = await synchronizationContext.InvokeAsync(() =>
        {
            var beforeAwaitThreadId = Environment.CurrentManagedThreadId;
            var postCountBeforeAwait = synchronizationContext.PostCount;
            var awaiter = synchronizationContext.GetAwaiter();

            Assert.True(awaiter.IsCompleted);
            awaiter.GetResult();

            return new AwaitResult(
                beforeAwaitThreadId,
                Environment.CurrentManagedThreadId,
                SynchronizationContext.Current,
                synchronizationContext.PostCount - postCountBeforeAwait);
        }).WaitAsync(Timeout);

        Assert.Equal(synchronizationContext.ManagedThreadId, result.BeforeAwaitThreadId);
        Assert.Equal(synchronizationContext.ManagedThreadId, result.AfterAwaitThreadId);
        Assert.Same(synchronizationContext, result.CurrentSynchronizationContext);
        Assert.Equal(0, result.PostCount);
    }

    private readonly record struct AwaitResult(int BeforeAwaitThreadId, int AfterAwaitThreadId, SynchronizationContext? CurrentSynchronizationContext, int PostCount);

    private sealed class DedicatedThreadSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<WorkItem> _workItems = new();
        private readonly Thread _thread;
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _postCount;

        public DedicatedThreadSynchronizationContext()
        {
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = nameof(DedicatedThreadSynchronizationContext),
            };

            _thread.Start();
            _started.Task.GetAwaiter().GetResult();
        }

        public int ManagedThreadId => _thread.ManagedThreadId;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);

            Interlocked.Increment(ref _postCount);
            _workItems.Add(new WorkItem(d, state));
        }

        public Task<T> InvokeAsync<T>(Func<T> func)
        {
            ArgumentNullException.ThrowIfNull(func);

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(_ =>
            {
                try
                {
                    tcs.SetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);

            return tcs.Task;
        }

        private void Run()
        {
            SetSynchronizationContext(this);
            _started.SetResult();

            foreach (var workItem in _workItems.GetConsumingEnumerable())
            {
                workItem.Callback(workItem.State);
            }
        }

        public void Dispose()
        {
            _workItems.CompleteAdding();
            Assert.True(_thread.Join(Timeout), "Failed to stop synchronization context thread");
            _workItems.Dispose();
        }

        private readonly record struct WorkItem(SendOrPostCallback Callback, object? State);
    }
}