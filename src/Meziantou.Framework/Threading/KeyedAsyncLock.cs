using System.Collections.Concurrent;

namespace Meziantou.Framework.Threading;

public sealed class KeyedAsyncLock<TKey> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, AsyncLock> _locks = new();

    public ValueTask<AsyncLockObject> LockAsync(TKey key, CancellationToken cancellationToken = default)
    {
        var instance = _locks.GetOrAdd(key, _ => new AsyncLock());
        return instance.LockAsync(cancellationToken);
    }
}
