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
    // Entries are reference-counted and removed once no one holds or waits for a key's lock, so the
    // dictionary doesn't grow without bound when used with high-cardinality keys. The dictionary
    // bookkeeping (add/remove + ref count) is guarded by locking on the dictionary itself; the
    // per-key Lock provides the actual mutual exclusion.
    private readonly Dictionary<TKey, Entry> _locks;

    /// <summary>Initializes a new instance of the <see cref="KeyedLock{TKey}"/> class.</summary>
    public KeyedLock()
        : this(comparer: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="KeyedLock{TKey}"/> class with the specified equality comparer.</summary>
    /// <param name="comparer">The equality comparer to use when comparing keys.</param>
    public KeyedLock(IEqualityComparer<TKey>? comparer)
    {
        _locks = new Dictionary<TKey, Entry>(comparer);
    }

    /// <summary>Acquires the lock for the specified key.</summary>
    /// <param name="key">The key to lock on.</param>
    /// <returns>A disposable object. Disposing the object releases the lock.</returns>
    public IDisposable Lock(TKey key)
    {
        Entry entry;
        lock (_locks)
        {
            if (!_locks.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _locks.Add(key, entry);
            }

            // Reserve the entry before releasing the bookkeeping lock so a concurrent release
            // cannot remove it from under us while we wait to enter the per-key lock.
            entry.ReferenceCount++;
        }

        entry.Lock.Enter();
        return new LockLease(this, key, entry);
    }

    private void Release(TKey key, Entry entry)
    {
        entry.Lock.Exit();
        lock (_locks)
        {
            if (--entry.ReferenceCount == 0)
            {
                _locks.Remove(key);
            }
        }
    }

    private sealed class Entry
    {
        public Lock Lock { get; } = new();
        public int ReferenceCount { get; set; }
    }

    private sealed class LockLease : IDisposable
    {
        private readonly KeyedLock<TKey> _owner;
        private readonly TKey _key;
        private readonly Entry _entry;
        private bool _disposed;

        public LockLease(KeyedLock<TKey> owner, TKey key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _owner.Release(_key, _entry);
            }
        }
    }
}
