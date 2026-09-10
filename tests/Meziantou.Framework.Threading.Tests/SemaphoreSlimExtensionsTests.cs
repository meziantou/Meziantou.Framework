using System.Collections.Concurrent;

namespace Meziantou.Framework.Threading.Tests;

public class SemaphoreSlimExtensionsTests
{
    [Fact]
    public void SemaphoreDisposer_Default_DisposeDoesNothing()
    {
        var disposer = default(SemaphoreSlimExtensions.SemaphoreDisposer);
        disposer.Dispose();
    }

    [Fact]
    public void DisposableUnsafeWait_ReleasesOnDispose()
    {
        using var semaphore = new SemaphoreSlim(1, 1);

        using (semaphore.DisposableUnsafeWait())
        {
            Assert.Equal(0, semaphore.CurrentCount);
        }

        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task DisposableWaitUnsafeAsync_ReleasesOnDispose()
    {
        using var semaphore = new SemaphoreSlim(1, 1);

        using (await semaphore.DisposableWaitUnsafeAsync())
        {
            Assert.Equal(0, semaphore.CurrentCount);
        }

        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public void DisposableWait_ReleasesOnDispose()
    {
        using var semaphore = new SemaphoreSlim(1, 1);

        using (semaphore.DisposableWait())
        {
            Assert.Equal(0, semaphore.CurrentCount);
        }

        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task DisposableWaitAsync_ReleasesOnDispose()
    {
        using var semaphore = new SemaphoreSlim(1, 1);

        using (await semaphore.DisposableWaitAsync())
        {
            Assert.Equal(0, semaphore.CurrentCount);
        }

        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public void DisposableWait_DisposeIsIdempotent()
    {
        using var semaphore = new SemaphoreSlim(1, 1);

        var disposer = semaphore.DisposableWait();
        disposer.Dispose();
        disposer.Dispose();

        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public void DisposableWait_ConcurrentDisposeReleasesOnlyOnce()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            using var semaphore = new SemaphoreSlim(1, 1);
            var disposer = semaphore.DisposableWait();

            var exceptions = new ConcurrentQueue<Exception>();
            using var barrier = new Barrier(participantCount: 4);
            var threads = new Thread[barrier.ParticipantCount];
            for (var i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    try
                    {
                        disposer.Dispose();
                    }
                    catch (Exception ex)
                    {
                        exceptions.Enqueue(ex);
                    }
                });

                threads[i].Start();
            }

            foreach (var thread in threads)
            {
                thread.Join();
            }

            Assert.Empty(exceptions);
            Assert.Equal(1, semaphore.CurrentCount);
        }
    }
}
