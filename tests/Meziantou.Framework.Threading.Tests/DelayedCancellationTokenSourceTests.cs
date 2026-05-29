namespace Meziantou.Framework.Threading.Tests;

public sealed class DelayedCancellationTokenSourceTests
{
    [Fact]
    public void Token_NotCanceled_WhenSourceNotCanceled()
    {
        using var source = new CancellationTokenSource();
        using var delayed = new DelayedCancellationTokenSource(source.Token, TimeSpan.FromMilliseconds(10));
        Assert.False(delayed.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task Token_CanceledAfterDelay_WhenSourceCanceled()
    {
        using var source = new CancellationTokenSource();
        using var delayed = new DelayedCancellationTokenSource(source.Token, TimeSpan.FromMilliseconds(20));

        var tcs = new TaskCompletionSource();
        using (delayed.Token.Register(() => tcs.TrySetResult()))
        {
            await source.CancelAsync();
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }

        Assert.True(delayed.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task Dispose_BeforeDelayElapses_PreventsCancellation()
    {
        using var source = new CancellationTokenSource();
        var delayed = new DelayedCancellationTokenSource(source.Token, TimeSpan.FromMilliseconds(100));

        var canceled = false;
        using var registration = delayed.Token.Register(() => Volatile.Write(ref canceled, true));

        await source.CancelAsync(); // schedules the delayed cancellation
        delayed.Dispose();          // must abort the pending delay

        await Task.Delay(400);
        Assert.False(Volatile.Read(ref canceled));
    }

    [Fact]
    public async Task DisposeAsync_BeforeDelayElapses_PreventsCancellation()
    {
        using var source = new CancellationTokenSource();
        var delayed = new DelayedCancellationTokenSource(source.Token, TimeSpan.FromMilliseconds(100));

        var canceled = false;
        using var registration = delayed.Token.Register(() => Volatile.Write(ref canceled, true));

        await source.CancelAsync();
        await delayed.DisposeAsync();

        await Task.Delay(400);
        Assert.False(Volatile.Read(ref canceled));
    }

    [Fact]
    public void Dispose_WithoutCancellation_DoesNotThrow()
    {
        using var source = new CancellationTokenSource();
        var delayed = new DelayedCancellationTokenSource(source.Token, TimeSpan.FromSeconds(1));
        delayed.Dispose();
    }
}
