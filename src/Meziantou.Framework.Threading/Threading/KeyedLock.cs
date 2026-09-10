namespace Meziantou.Framework.Threading;

/// <summary>Provides a synchronous lock mechanism that locks based on a key, allowing concurrent operations on different keys.</summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <remarks>
/// The lock is thread-affine: it is built on <see cref="System.Threading.Lock"/>, which requires the lease to be
/// released on the thread that acquired it. The critical section must therefore stay on one thread and must not
/// <see langword="await"/>. Use <see cref="KeyedAsyncLock{TKey}"/> for asynchronous code.
/// </remarks>
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
    // The table reference-counts and evicts entries so it doesn't grow without bound when used with
    // high-cardinality keys; the per-key Lock provides the actual mutual exclusion.
    private readonly KeyedEntryTable<TKey, Entry> _locks;

    /// <summary>Initializes a new instance of the <see cref="KeyedLock{TKey}"/> class.</summary>
    public KeyedLock()
        : this(comparer: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="KeyedLock{TKey}"/> class with the specified equality comparer.</summary>
    /// <param name="comparer">The equality comparer to use when comparing keys.</param>
    public KeyedLock(IEqualityComparer<TKey>? comparer)
    {
        _locks = new KeyedEntryTable<TKey, Entry>(comparer, () => new Entry());
    }

    /// <summary>Gets the number of keys currently tracked. Used by tests to assert that released keys are evicted.</summary>
    internal int EntryCount => _locks.Count;

    /// <summary>Acquires the lock for the specified key.</summary>
    /// <param name="key">The key to lock on.</param>
    /// <returns>A disposable object. Disposing the object releases the lock.</returns>
    /// <remarks>
    /// The returned object must be disposed on the thread that called this method. Disposing it on another thread
    /// throws <see cref="SynchronizationLockException"/> and leaves the underlying lock held.
    /// </remarks>
    public IDisposable Lock(TKey key)
    {
        var entry = _locks.Reserve(key);
        entry.Lock.Enter();
        return new LockLease(this, key, entry);
    }

    private void Release(TKey key, Entry entry)
    {
        try
        {
            entry.Lock.Exit();
        }
        finally
        {
            // The reference count must be released even when Exit throws, which happens when the lease is
            // disposed on a thread that does not own the lock. Skipping it would leave the entry in the
            // table forever and permanently deadlock every later acquisition of the same key.
            _locks.Release(key, entry);
        }
    }

    private sealed class Entry : KeyedEntry
    {
        public Lock Lock { get; } = new();
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
