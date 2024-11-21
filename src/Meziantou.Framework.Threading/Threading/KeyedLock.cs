using System.Collections.Concurrent;

namespace Meziantou.Framework.Threading;
public sealed class KeyedLock<TKey> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Lock> _locks;

    public KeyedLock()
        : this(comparer: null)
    {
    }

    public KeyedLock(IEqualityComparer<TKey>? comparer)
    {
        _locks = new ConcurrentDictionary<TKey, Lock>(comparer);
    }

    public IDisposable Lock(TKey key)
    {
        var instance = _locks.GetOrAdd(key, _ => new Lock());
        instance.Enter();
        return new LockLease(instance);
    }

    private sealed class LockLease : IDisposable
    {
        private readonly Lock _lockedInstance;
        private bool _disposed;

        public LockLease(Lock lockedInstance)
        {
            _lockedInstance = lockedInstance;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _lockedInstance.Exit();
                _disposed = true;
            }
        }
    }
}
