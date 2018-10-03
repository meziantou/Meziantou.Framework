using System;
using System.Threading;
using System.Threading.Tasks;

namespace Meziantou.Framework.Threading
{
    public class AsyncLock : IDisposable
    {
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

        public IDisposable Lock(CancellationToken cancellationToken)
        {
            _semaphoreSlim.Wait(cancellationToken);
            return new LockObject(this);
        }

        public async Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
        {
            await _semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new LockObject(this);
        }

        public bool TryLock()
        {
            return _semaphoreSlim.Wait(TimeSpan.Zero);
        }

        public bool TryLock(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return _semaphoreSlim.Wait(timeout, cancellationToken);
        }

        public Task<bool> TryLockAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return _semaphoreSlim.WaitAsync(timeout, cancellationToken);
        }

        public void Release()
        {
            _semaphoreSlim.Release();
        }

        public void Dispose()
        {
            _semaphoreSlim.Dispose();
        }

        private readonly struct LockObject : IDisposable
        {
            private readonly AsyncLock _parent;

            public LockObject(AsyncLock parent)
            {
                _parent = parent;
            }

            public void Dispose()
            {
                _parent.Release();
            }
        }
    }
}
