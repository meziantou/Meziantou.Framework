namespace Meziantou.Framework.Threading.Tests;

public class AsyncLockTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Lock()
    {
        var asyncLock = new AsyncLock();
        for (var i = 0; i < 2; i++)
        {
            using (await asyncLock.LockAsync())
            {
                if (asyncLock.TryLock(out var lockObject))
                {
                    Assert.Fail("Should not be able to acquire the lock");
                }
            }
        }
    }

    [Fact]
    public void TryLock_OnFreeLock_Succeeds()
    {
        var asyncLock = new AsyncLock();
        Assert.True(asyncLock.TryLock(out var lease));
        Assert.False(asyncLock.TryLock(out _));
        lease.Dispose();
        Assert.True(asyncLock.TryLock(out _));
    }

    [Fact]
    public async Task LockAsync_AlreadyCanceledToken_Throws()
    {
        var asyncLock = new AsyncLock();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await asyncLock.LockAsync(cts.Token));

        // The lock must still be free after a failed acquisition.
        Assert.True(asyncLock.TryLock(out _));
    }

    [Fact]
    public async Task LockAsync_CancelWhileWaiting_ReleasesQueueSlot()
    {
        var asyncLock = new AsyncLock();
        var held = await asyncLock.LockAsync();

        using var cts = new CancellationTokenSource();
        var waiting = asyncLock.LockAsync(cts.Token).AsTask();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);

        // Releasing must not hand the lock to the canceled waiter; a fresh acquisition must succeed.
        held.Dispose();
        using (await asyncLock.LockAsync().AsTask().WaitAsync(Timeout))
        {
        }
    }

    [Fact]
    public async Task LockAsync_WaitersAreServedInOrder()
    {
        var asyncLock = new AsyncLock();
        var held = await asyncLock.LockAsync();

        var order = new List<int>();
        var w1 = AcquireAndRecord(1);
        var w2 = AcquireAndRecord(2);
        var w3 = AcquireAndRecord(3);

        held.Dispose();
        await Task.WhenAll(w1, w2, w3).WaitAsync(Timeout);

        Assert.Equal([1, 2, 3], order);

        async Task AcquireAndRecord(int id)
        {
            using (await asyncLock.LockAsync())
            {
                order.Add(id);
            }
        }
    }

    [Fact]
    public async Task LockAsync_ProvidesMutualExclusion()
    {
        var asyncLock = new AsyncLock();
        var counter = 0;
        var concurrent = 0;

        var tasks = Enumerable.Range(0, 64).Select(_ => Task.Run(async () =>
        {
            using (await asyncLock.LockAsync())
            {
                Assert.Equal(1, Interlocked.Increment(ref concurrent));
                var value = counter;
                await Task.Yield();
                counter = value + 1;
                Interlocked.Decrement(ref concurrent);
            }
        })).ToArray();

        await Task.WhenAll(tasks).WaitAsync(Timeout);
        Assert.Equal(64, counter);
    }

    [Fact]
    public void DefaultLease_DisposeIsNoop()
    {
        default(AsyncLock.AsyncLockLease).Dispose();
    }
}
