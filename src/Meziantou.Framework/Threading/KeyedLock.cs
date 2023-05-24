using System.Collections.Concurrent;

namespace Meziantou.Framework.Threading;
public sealed class KeyedLock<TKey> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, object> _locks = new();

    public IDisposable Lock(TKey key)
    {
        var instance = _locks.GetOrAdd(key, _ => new object());
        Monitor.Enter(instance);
        return new LockLease(instance);
    }

    private sealed class LockLease : IDisposable
    {
        private readonly object _lockedInstance;
        private bool _disposed;

        public LockLease(object lockedInstance)
        {
            _lockedInstance = lockedInstance;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Monitor.Exit(_lockedInstance);
                _disposed = true;
            }
        }
    }
}
