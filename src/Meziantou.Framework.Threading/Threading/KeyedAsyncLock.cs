using System.Runtime.InteropServices;

namespace Meziantou.Framework.Threading;

/// <summary>Provides an asynchronous lock mechanism that locks based on a key, allowing concurrent operations on different keys.</summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <example>
/// <code><![CDATA[
/// var keyedLock = new KeyedAsyncLock<string>();
/// 
/// async Task ProcessUserAsync(string userId)
/// {
///     using (await keyedLock.LockAsync(userId))
///     {
///         // Only one operation per userId at a time
///         // Multiple different userIds can be processed concurrently
///         await UpdateUserDataAsync(userId);
///     }
/// }
/// ]]></code>
/// </example>
public sealed class KeyedAsyncLock<TKey> where TKey : notnull
{
    // Entries are reference-counted and removed once no one holds or waits for a key's lock, so the
    // dictionary doesn't grow without bound when used with high-cardinality keys. The dictionary
    // bookkeeping (add/remove + ref count) is guarded by locking on the dictionary itself; the
    // per-key AsyncLock provides the actual mutual exclusion.
    private readonly Dictionary<TKey, Entry> _locks;

    /// <summary>Initializes a new instance of the <see cref="KeyedAsyncLock{TKey}"/> class.</summary>
    public KeyedAsyncLock()
        : this(comparer: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="KeyedAsyncLock{TKey}"/> class with the specified equality comparer.</summary>
    /// <param name="comparer">The equality comparer to use when comparing keys.</param>
    public KeyedAsyncLock(IEqualityComparer<TKey>? comparer)
    {
        _locks = new Dictionary<TKey, Entry>(comparer);
    }

    /// <summary>Asynchronously acquires the lock for the specified key.</summary>
    /// <param name="key">The key to lock on.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the lock.</param>
    /// <returns>A task that returns a disposable lease. Disposing the lease releases the lock.</returns>
    public async ValueTask<KeyedAsyncLockLease> LockAsync(TKey key, CancellationToken cancellationToken = default)
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
            // cannot remove it from under us while we wait to acquire the per-key lock.
            entry.ReferenceCount++;
        }

        try
        {
            var lease = await entry.Lock.LockAsync(cancellationToken).ConfigureAwait(false);
            return new KeyedAsyncLockLease(this, key, entry, lease);
        }
        catch
        {
            // The lock was not acquired (e.g. cancellation), so undo the reservation.
            ReleaseReference(key, entry);
            throw;
        }
    }

    private void ReleaseReference(TKey key, Entry entry)
    {
        lock (_locks)
        {
            if (--entry.ReferenceCount == 0)
            {
                _locks.Remove(key);
            }
        }
    }

    private void Release(TKey key, Entry entry, AsyncLock.AsyncLockLease lease)
    {
        lease.Dispose();
        ReleaseReference(key, entry);
    }

    internal sealed class Entry
    {
        public AsyncLock Lock { get; } = new();
        public int ReferenceCount { get; set; }
    }

    /// <summary>Represents a disposable lease for a <see cref="KeyedAsyncLock{TKey}"/>. Disposing the lease releases the lock for the key.</summary>
    [StructLayout(LayoutKind.Auto)]
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Not meant to be used directly")]
    public readonly struct KeyedAsyncLockLease : IDisposable
    {
        private readonly KeyedAsyncLock<TKey>? _owner;
        private readonly TKey _key;
        private readonly Entry _entry;
        private readonly AsyncLock.AsyncLockLease _lease;

        internal KeyedAsyncLockLease(KeyedAsyncLock<TKey> owner, TKey key, Entry entry, AsyncLock.AsyncLockLease lease)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
            _lease = lease;
        }

        public void Dispose()
        {
            _owner?.Release(_key, _entry, _lease);
        }
    }
}
