namespace Meziantou.Framework.Threading.Tests;

public sealed class AsyncAutoResetEventTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task InitialStateSignaled_FirstWaitCompletes_ThenResets()
    {
        var e = new AsyncAutoResetEvent(initialState: true);
        await e.WaitAsync().WaitAsync(Timeout);

        var second = e.WaitAsync();
        Assert.False(second.IsCompleted);
        e.Set();
        await second.WaitAsync(Timeout);
    }

    [Fact]
    public async Task InitialStateNonSignaled_WaitBlocksUntilSet()
    {
        var e = new AsyncAutoResetEvent(initialState: false);
        var wait = e.WaitAsync();
        Assert.False(wait.IsCompleted);
        e.Set();
        await wait.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Set_ReleasesExactlyOneWaiter()
    {
        var e = new AsyncAutoResetEvent(initialState: false);
        var w1 = e.WaitAsync();
        var w2 = e.WaitAsync();

        e.Set();
        var completed = await Task.WhenAny(w1, w2).WaitAsync(Timeout);
        await completed;

        Assert.True(w1.IsCompleted ^ w2.IsCompleted);

        e.Set();
        await Task.WhenAll(w1, w2).WaitAsync(Timeout);
    }

    [Fact]
    public async Task Set_IsNotCounted()
    {
        var e = new AsyncAutoResetEvent(initialState: false);
        e.Set();
        e.Set();

        await e.WaitAsync().WaitAsync(Timeout);

        var second = e.WaitAsync();
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await e.WaitAsync(cts.Token));
    }

    [Fact]
    public async Task CancelWhileWaiting_DoesNotStealSignalFromOtherWaiter()
    {
        var e = new AsyncAutoResetEvent(initialState: false);
        using var cts = new CancellationTokenSource();

        var canceledWait = e.WaitAsync(cts.Token);
        var goodWait = e.WaitAsync();

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceledWait);

        // The signal must go to the still-pending waiter, not be consumed by the canceled one.
        e.Set();
        await goodWait.WaitAsync(Timeout);
    }

    [Fact]
    public async Task CanceledWait_DoesNotConsumeSignal_WhenSetRacesCancellation()
    {
        var e = new AsyncAutoResetEvent(initialState: false);
        using var cts = new CancellationTokenSource();

        var wait = e.WaitAsync(cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await wait);

        // After a canceled wait, a Set with no waiter must leave the event signaled.
        e.Set();
        await e.WaitAsync().WaitAsync(Timeout);
    }
}
