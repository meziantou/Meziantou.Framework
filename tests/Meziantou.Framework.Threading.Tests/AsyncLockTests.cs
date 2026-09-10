namespace Meziantou.Framework.Threading.Tests;

public class AsyncLockTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    // The iterations of the race below only widen the window, so stop early on a machine that is
    // too slow or too loaded to run them all instead of hitting Timeout
    private static readonly TimeSpan RaceBudget = TimeSpan.FromSeconds(5);

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
    public async Task LockAsync_CancelingWaitersAtEveryPosition_PreservesTheOrderOfTheOthers()
    {
        // A canceled waiter is unlinked from the queue. The survivors must keep their FIFO order whether the
        // canceled waiter was the head, the tail, or in the middle, and consecutive cancellations must not
        // corrupt the queue.
        var asyncLock = new AsyncLock();
        var held = await asyncLock.LockAsync();

        var sources = new CancellationTokenSource[10];
        var waiters = new Task<AsyncLock.AsyncLockLease>[sources.Length];
        for (var i = 0; i < sources.Length; i++)
        {
            sources[i] = new CancellationTokenSource();
            waiters[i] = asyncLock.LockAsync(sources[i].Token).AsTask();
        }

        int[] canceled = [0, 3, 4, 7, 9];
        foreach (var index in canceled)
        {
            await sources[index].CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiters[index]);
        }

        var current = held;
        int[] survivors = [1, 2, 5, 6, 8];
        foreach (var index in survivors)
        {
            Assert.False(waiters[index].IsCompleted);
            current.Dispose();
            current = await waiters[index].WaitAsync(Timeout);
        }

        // The queue is empty again, so a waiter that arrives now must still be reachable.
        var late = asyncLock.LockAsync().AsTask();
        current.Dispose();
        (await late.WaitAsync(Timeout)).Dispose();

        foreach (var source in sources)
        {
            source.Dispose();
        }
    }

    [Fact]
    public async Task LockAsync_CancelingEveryWaiter_LeavesTheLockFree()
    {
        var asyncLock = new AsyncLock();
        var held = await asyncLock.LockAsync();

        using var cts = new CancellationTokenSource();
        var waiters = new Task<AsyncLock.AsyncLockLease>[10];
        for (var i = 0; i < waiters.Length; i++)
        {
            waiters[i] = asyncLock.LockAsync(cts.Token).AsTask();
        }

        await cts.CancelAsync();
        foreach (var waiter in waiters)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        }

        // Releasing must not hand the lock to one of the canceled waiters, and a waiter that arrives after the
        // queue emptied must still be reachable.
        var late = asyncLock.LockAsync().AsTask();
        held.Dispose();
        (await late.WaitAsync(Timeout)).Dispose();
        Assert.True(asyncLock.TryLock(out _));
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
    public async Task LockAsync_CancellationRacingWithAcquisition_DoesNotDeadlock()
    {
        // Regression test: LockAsync used to complete a canceled waiter while still holding the
        // internal lock. CancellationTokenRegistration.Dispose blocks until a callback running on
        // another thread completes, and that callback (OnCancellationRequest) takes the same lock,
        // so the two threads deadlocked. LockAsync blocks synchronously in that case, hence the
        // Task.Run: it lets the timeout fail the test instead of hanging the whole run.
        await Task.Run(RaceCancellationAgainstLockAsync).WaitAsync(Timeout);

        static async Task RaceCancellationAgainstLockAsync()
        {
            var deadline = Environment.TickCount64 + (long)RaceBudget.TotalMilliseconds;
            for (var i = 0; i < 20_000 && Environment.TickCount64 < deadline; i++)
            {
                var asyncLock = new AsyncLock();

                // Hold the lock so the acquisition below has to go through the waiter queue.
                var held = await asyncLock.LockAsync();

                using var cts = new CancellationTokenSource();
                using var barrier = new Barrier(2);

                var canceling = Task.Run(() =>
                {
                    barrier.SignalAndWait();

                    // Sweep the cancellation across the window between the token registration and
                    // the IsCancellationRequested check inside LockAsync.
                    Thread.SpinWait(i % 64);
                    cts.Cancel();
                });

                barrier.SignalAndWait();
                var waiting = asyncLock.LockAsync(cts.Token);

                await canceling;

                // Whichever side won, the waiter must reach a terminal state.
                held.Dispose();
                try
                {
                    (await waiting).Dispose();
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }

    [Fact]
    public void DefaultLease_DisposeIsNoop()
    {
        default(AsyncLock.AsyncLockLease).Dispose();
    }

    [Fact]
    public async Task Dispose_LeaseTwice_DoesNotReleaseTheNextAcquisition()
    {
        var asyncLock = new AsyncLock();
        var lease = await asyncLock.LockAsync();
        lease.Dispose();

        using var held = await asyncLock.LockAsync();
        lease.Dispose();

        Assert.False(asyncLock.TryLock(out _), "The second disposal released a lock it no longer owns");
    }

    [Fact]
    public void Dispose_CopyOfAnAlreadyDisposedLease_DoesNotReleaseTheNextAcquisition()
    {
        var asyncLock = new AsyncLock();
        Assert.True(asyncLock.TryLock(out var lease));
        var copy = lease;
        lease.Dispose();

        Assert.True(asyncLock.TryLock(out _));
        copy.Dispose();

        Assert.False(asyncLock.TryLock(out _), "Disposing a stale copy released a lock it no longer owns");
    }

    [Fact]
    public async Task Dispose_LeaseTwice_GrantsTheLockToASingleWaiter()
    {
        var asyncLock = new AsyncLock();
        var lease = await asyncLock.LockAsync();
        var first = asyncLock.LockAsync().AsTask();
        var second = asyncLock.LockAsync().AsTask();

        lease.Dispose();
        lease.Dispose();

        using var granted = await first.WaitAsync(Timeout);
        Assert.False(second.IsCompleted, "The second disposal granted the lock to a second waiter");
    }
}
