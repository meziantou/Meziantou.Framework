namespace Meziantou.Framework.Threading.Tests;

public class AsyncAutoResetEventTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

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
            for (var i = 0; i < 20_000; i++)
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
