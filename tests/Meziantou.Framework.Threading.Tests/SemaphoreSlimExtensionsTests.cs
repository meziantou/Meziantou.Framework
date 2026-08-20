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
}
