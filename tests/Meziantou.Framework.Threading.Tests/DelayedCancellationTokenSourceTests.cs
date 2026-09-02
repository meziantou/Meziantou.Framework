namespace Meziantou.Framework.Threading.Tests;

public sealed class DelayedCancellationTokenSourceTests
{
    [Fact]
    public void Dispose_IsIdempotent()
    {
        var cts = new DelayedCancellationTokenSource(CancellationToken.None, TimeSpan.FromSeconds(1));
        cts.Dispose();
        cts.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var cts = new DelayedCancellationTokenSource(CancellationToken.None, TimeSpan.FromSeconds(1));
        await cts.DisposeAsync();
        await cts.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ThenDispose_IsIdempotent()
    {
        var cts = new DelayedCancellationTokenSource(CancellationToken.None, TimeSpan.FromSeconds(1));
        await cts.DisposeAsync();
        cts.Dispose();
    }

    [Fact]
    public async Task Dispose_ThenDisposeAsync_IsIdempotent()
    {
        var cts = new DelayedCancellationTokenSource(CancellationToken.None, TimeSpan.FromSeconds(1));
        cts.Dispose();
        await cts.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentDispose_DisposesOnlyOnce()
    {
        using var cts = new DelayedCancellationTokenSource(CancellationToken.None, TimeSpan.FromSeconds(1));
        using var start = new Barrier(4);

        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            start.SignalAndWait();
            cts.Dispose();
        })).ToArray();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Token_IsNotCancelled_WhenTheSourceTokenIsNotCancelled()
    {
        using var source = new CancellationTokenSource();
        using var cts = new DelayedCancellationTokenSource(source.Token, TimeSpan.FromMilliseconds(10));

        Assert.False(cts.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task Token_IsCancelled_AfterTheDelayElapses()
    {
        using var source = new CancellationTokenSource();
        using var cts = new DelayedCancellationTokenSource(source.Token, TimeSpan.FromMilliseconds(10));
        using var cancelled = new ManualResetEventSlim(initialState: false);
        using var registration = cts.Token.Register(cancelled.Set);

        await source.CancelAsync();

        Assert.True(cancelled.Wait(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Token_IsNotCancelled_WhenDisposedBeforeTheDelayElapses()
    {
        using var source = new CancellationTokenSource();
        var cts = new DelayedCancellationTokenSource(source.Token, TimeSpan.FromSeconds(10));
        var token = cts.Token;

        await source.CancelAsync();
        cts.Dispose();

        // Disposal cancels the pending Task.Delay, so the delayed cancellation never fires.
        await Task.Delay(200);
        Assert.False(token.IsCancellationRequested);
    }
}
