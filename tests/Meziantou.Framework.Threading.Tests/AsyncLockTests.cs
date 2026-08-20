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
            for (var i = 0; i < 20_000; i++)
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
}
