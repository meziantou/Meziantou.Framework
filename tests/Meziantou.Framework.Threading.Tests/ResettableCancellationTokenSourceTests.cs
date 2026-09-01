namespace Meziantou.Framework.Threading.Tests;

public sealed class ResettableCancellationTokenSourceTests
{
    [Fact]
    public void Dispose_IsIdempotent()
    {
        var cts = new ResettableCancellationTokenSource(cancelOnResetAndDispose: true);
        cts.Dispose();
        cts.Dispose();
    }

    [Fact]
    public void Dispose_IsIdempotent_WithoutCancelOnDispose()
    {
        var cts = new ResettableCancellationTokenSource(ResettableCancellationTokenSourceOptions.None);
        cts.Dispose();
        cts.Dispose();
    }

    [Fact]
    public void Dispose_CancelsTheToken_WhenCancelOnDisposeIsSet()
    {
        var cts = new ResettableCancellationTokenSource(ResettableCancellationTokenSourceOptions.CancelOnDispose);
        var token = cts.Token;
        cts.Dispose();

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void Dispose_DoesNotCancelTheToken_WhenCancelOnDisposeIsNotSet()
    {
        var cts = new ResettableCancellationTokenSource(ResettableCancellationTokenSourceOptions.None);
        var token = cts.Token;
        cts.Dispose();

        Assert.False(token.IsCancellationRequested);
    }

    [Fact]
    public void Reset_CancelsTheCurrentToken_WhenCancelOnResetIsSet()
    {
        using var cts = new ResettableCancellationTokenSource(ResettableCancellationTokenSourceOptions.CancelOnReset);
        var token = cts.Token;
        cts.Reset();

        Assert.True(token.IsCancellationRequested);
        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public void Reset_ProducesAFreshTokenAfterCancellation()
    {
        using var cts = new ResettableCancellationTokenSource(ResettableCancellationTokenSourceOptions.None);
        cts.Cancel();
        Assert.True(cts.IsCancellationRequested);

        cts.Reset();

        Assert.False(cts.IsCancellationRequested);
        Assert.False(cts.Token.IsCancellationRequested);
    }

    [Fact]
    public void Reset_AfterDispose_Throws()
    {
        var cts = new ResettableCancellationTokenSource(ResettableCancellationTokenSourceOptions.None);
        cts.Dispose();

        Assert.Throws<ObjectDisposedException>(cts.Reset);
    }

    [Fact]
    public async Task ConcurrentResetAndCancel_DoesNotThrow()
    {
        // Reset replaces (and disposes) the underlying source. Without synchronization a concurrent reader observes
        // the disposed instance and throws ObjectDisposedException.
        for (var round = 0; round < 50; round++)
        {
            using var cts = new ResettableCancellationTokenSource(cancelOnResetAndDispose: true);
            using var start = new Barrier(2);

            var resets = Task.Run(() =>
            {
                start.SignalAndWait();
                for (var i = 0; i < 200; i++)
                {
                    cts.Reset();
                }
            });

            var readers = Task.Run(() =>
            {
                start.SignalAndWait();
                for (var i = 0; i < 200; i++)
                {
                    _ = cts.Token;
                    _ = cts.IsCancellationRequested;
                    cts.Cancel();
                }
            });

            await Task.WhenAll(resets, readers).WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task ConcurrentDispose_DisposesOnlyOnce()
    {
        using var cts = new ResettableCancellationTokenSource(cancelOnResetAndDispose: true);
        using var start = new Barrier(4);

        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            start.SignalAndWait();
            cts.Dispose();
        })).ToArray();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
    }
}
