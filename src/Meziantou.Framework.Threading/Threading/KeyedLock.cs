using System.Collections.Concurrent;

namespace Meziantou.Framework.Threading;

/// <summary>Provides a synchronous lock mechanism that locks based on a key, allowing concurrent operations on different keys.</summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <example>
/// <code><![CDATA[
/// var keyedLock = new KeyedLock<string>();
/// 
/// void ProcessUser(string userId)
/// {
///     using (keyedLock.Lock(userId))
///     {
///         // Only one operation per userId at a time
///         // Multiple different userIds can be processed concurrently
///         UpdateUserData(userId);
///     }
/// }
/// ]]></code>
/// </example>
public sealed class KeyedLock<TKey> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Lock> _locks;

    /// <summary>Initializes a new instance of the <see cref="KeyedLock{TKey}"/> class.</summary>
    public KeyedLock()
        : this(comparer: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="KeyedLock{TKey}"/> class with the specified equality comparer.</summary>
    /// <param name="comparer">The equality comparer to use when comparing keys.</param>
    public KeyedLock(IEqualityComparer<TKey>? comparer)
    {
        _locks = new ConcurrentDictionary<TKey, Lock>(comparer);
    }

    /// <summary>Acquires the lock for the specified key.</summary>
    /// <param name="key">The key to lock on.</param>
    /// <returns>A disposable object. Disposing the object releases the lock.</returns>
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
