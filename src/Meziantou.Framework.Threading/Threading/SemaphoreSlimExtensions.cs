namespace Meziantou.Framework.Threading;

/// <summary>Provides extension methods for <see cref="SemaphoreSlim"/> to simplify usage with disposable patterns.</summary>
public static class SemaphoreSlimExtensions
{
    /// <summary>Waits on the semaphore and returns a disposable struct that releases the semaphore when disposed. This method is unsafe because the struct can be copied.</summary>
    /// <param name="semaphore">The semaphore to wait on.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A disposable struct that releases the semaphore when disposed.</returns>
    public static SemaphoreDisposer DisposableUnsafeWait(this SemaphoreSlim semaphore, CancellationToken cancellationToken = default)
    {
        semaphore.Wait(cancellationToken);
        return new SemaphoreDisposer(semaphore);
    }

    /// <summary>Asynchronously waits on the semaphore and returns a disposable struct that releases the semaphore when disposed. This method is unsafe because the struct can be copied.</summary>
    /// <param name="semaphore">The semaphore to wait on.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A task that returns a disposable struct that releases the semaphore when disposed.</returns>
    public static async Task<SemaphoreDisposer> DisposableWaitUnsafeAsync(this SemaphoreSlim semaphore, CancellationToken cancellationToken = default)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SemaphoreDisposer(semaphore);
    }

    /// <summary>Waits on the semaphore and returns a disposable object that releases the semaphore when disposed.</summary>
    /// <param name="semaphore">The semaphore to wait on.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A disposable object that releases the semaphore when disposed.</returns>
    public static IDisposable DisposableWait(this SemaphoreSlim semaphore, CancellationToken cancellationToken = default)
    {
        semaphore.Wait(cancellationToken);
        return new SemaphoreDisposerClass(semaphore);
    }

    /// <summary>Asynchronously waits on the semaphore and returns a disposable object that releases the semaphore when disposed.</summary>
    /// <param name="semaphore">The semaphore to wait on.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A task that returns a disposable object that releases the semaphore when disposed.</returns>
    public static async Task<IDisposable> DisposableWaitAsync(this SemaphoreSlim semaphore, CancellationToken cancellationToken = default)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SemaphoreDisposerClass(semaphore);
    }

    private sealed class SemaphoreDisposerClass : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public SemaphoreDisposerClass(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _semaphore.Release();
                _disposed = true;
            }
        }
    }

    /// <summary>Represents a disposable struct that releases a semaphore when disposed.</summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "<Pending>")]
    public readonly struct SemaphoreDisposer : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;

        public SemaphoreDisposer(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            _semaphore.Release();
        }
    }
}
