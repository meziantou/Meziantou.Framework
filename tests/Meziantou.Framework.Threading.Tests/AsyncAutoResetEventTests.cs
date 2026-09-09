namespace Meziantou.Framework.Threading.Tests;

public class AsyncAutoResetEventTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    // The iterations of the race below only widen the window, so stop early on a machine that is
    // too slow or too loaded to run them all instead of hitting Timeout
    private static readonly TimeSpan RaceBudget = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task WaitAsync_InitiallySignaled_CompletesImmediatelyOnce()
    {
        var e = new AsyncAutoResetEvent(initialState: true);

        await e.WaitAsync().WaitAsync(Timeout);

        var second = e.WaitAsync();
        Assert.False(second.IsCompleted);

        e.Set();
        await second.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Set_ReleasesASingleWaiter()
    {
        var e = new AsyncAutoResetEvent(initialState: false);
        var first = e.WaitAsync();
        var second = e.WaitAsync();

        e.Set();
        await first.WaitAsync(Timeout);
        Assert.False(second.IsCompleted);

        e.Set();
        await second.WaitAsync(Timeout);
    }

    [Fact]
    public async Task WaitAsync_AlreadyCanceledToken_Throws()
    {
        var e = new AsyncAutoResetEvent(initialState: false);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => e.WaitAsync(cts.Token));
    }

    [Fact]
    public async Task WaitAsync_CancelWhileWaiting_DoesNotConsumeTheSignal()
    {
        var e = new AsyncAutoResetEvent(initialState: false);
        using var cts = new CancellationTokenSource();
        var canceled = e.WaitAsync(cts.Token);
        var other = e.WaitAsync();

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);

        // The signal must go to the remaining waiter, not to the canceled one.
        e.Set();
        await other.WaitAsync(Timeout);
    }

    [Fact]
    public async Task WaitAsync_CancelingWaitersAtEveryPosition_PreservesTheOrderOfTheOthers()
    {
        // A canceled waiter is unlinked from the queue. The survivors must keep their FIFO order whether the
        // canceled waiter was the head, the tail, or in the middle, and consecutive cancellations must not
        // corrupt the queue.
        var e = new AsyncAutoResetEvent(initialState: false);
        var sources = new CancellationTokenSource[10];
        var waiters = new Task[sources.Length];
        for (var i = 0; i < sources.Length; i++)
        {
            sources[i] = new CancellationTokenSource();
            waiters[i] = e.WaitAsync(sources[i].Token);
        }

        int[] canceled = [0, 3, 4, 7, 9];
        foreach (var index in canceled)
        {
            await sources[index].CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiters[index]);
        }

        int[] survivors = [1, 2, 5, 6, 8];
        foreach (var index in survivors)
        {
            Assert.False(waiters[index].IsCompleted);
            e.Set();
            await waiters[index].WaitAsync(Timeout);
        }

        // The queue is empty again, so a waiter that arrives now must still be reachable.
        var late = e.WaitAsync();
        e.Set();
        await late.WaitAsync(Timeout);

        foreach (var source in sources)
        {
            source.Dispose();
        }
    }

    [Fact]
    public async Task WaitAsync_CancelingEveryWaiter_LeavesTheQueueEmpty()
    {
        var e = new AsyncAutoResetEvent(initialState: false);
        using var cts = new CancellationTokenSource();

        var waiters = new Task[10];
        for (var i = 0; i < waiters.Length; i++)
        {
            waiters[i] = e.WaitAsync(cts.Token);
        }

        await cts.CancelAsync();
        foreach (var waiter in waiters)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        }

        // The signal must go to a waiter that arrives afterwards, not to one of the canceled ones.
        var late = e.WaitAsync();
        e.Set();
        await late.WaitAsync(Timeout);
    }

    [Fact]
    public async Task WaitAsync_CancellationRacingWithWait_DoesNotDeadlock()
    {
        // Regression test: WaitAsync used to complete a canceled waiter while still holding the
        // internal lock. CancellationTokenRegistration.Dispose blocks until a callback running on
        // another thread completes, and that callback (OnCancellationRequest) takes the same lock,
        // so the two threads deadlocked. WaitAsync blocks synchronously in that case, hence the
        // Task.Run: it lets the timeout fail the test instead of hanging the whole run.
        await Task.Run(RaceCancellationAgainstWaitAsync).WaitAsync(Timeout);

        static async Task RaceCancellationAgainstWaitAsync()
        {
            var deadline = Environment.TickCount64 + (long)RaceBudget.TotalMilliseconds;
            for (var i = 0; i < 20_000 && Environment.TickCount64 < deadline; i++)
            {
                var e = new AsyncAutoResetEvent(initialState: false);
                using var cts = new CancellationTokenSource();
                using var barrier = new Barrier(2);

                var canceling = Task.Run(() =>
                {
                    barrier.SignalAndWait();

                    // Sweep the cancellation across the window between the token registration and
                    // the IsCancellationRequested check inside WaitAsync.
                    Thread.SpinWait(i % 64);
                    cts.Cancel();
                });

                barrier.SignalAndWait();
                var waiting = e.WaitAsync(cts.Token);

                await canceling;

                // Whichever side won, the waiter must reach a terminal state.
                e.Set();
                try
                {
                    await waiting;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }
}
