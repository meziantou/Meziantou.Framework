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
    // The table reference-counts and evicts entries so it doesn't grow without bound when used with
    // high-cardinality keys; the per-key AsyncLock provides the actual mutual exclusion.
    private readonly KeyedEntryTable<TKey, Entry> _locks;

    /// <summary>Initializes a new instance of the <see cref="KeyedAsyncLock{TKey}"/> class.</summary>
    public KeyedAsyncLock()
        : this(comparer: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="KeyedAsyncLock{TKey}"/> class with the specified equality comparer.</summary>
    /// <param name="comparer">The equality comparer to use when comparing keys.</param>
    public KeyedAsyncLock(IEqualityComparer<TKey>? comparer)
    {
        _locks = new KeyedEntryTable<TKey, Entry>(comparer, () => new Entry());
    }

    /// <summary>Gets the number of keys currently tracked. Used by tests to assert that released keys are evicted.</summary>
    internal int EntryCount => _locks.Count;

    /// <summary>Asynchronously acquires the lock for the specified key.</summary>
    /// <param name="key">The key to lock on.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the lock.</param>
    /// <returns>A task that returns a disposable lease. Disposing the lease releases the lock.</returns>
    public ValueTask<KeyedAsyncLockLease> LockAsync(TKey key, CancellationToken cancellationToken = default)
    {
        // Checked before reserving: an already-canceled token would otherwise create an entry and the per-key
        // lock behind it only to evict them again once the cancellation surfaces.
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<KeyedAsyncLockLease>(cancellationToken);

        var entry = _locks.Reserve(key);
        ValueTask<AsyncLock.AsyncLockLease> pending;
        try
        {
            pending = entry.Lock.LockAsync(cancellationToken);
        }
        catch
        {
            // The lock was not acquired, so undo the reservation.
            _locks.Release(key, entry);
            throw;
        }

        // An uncontended acquisition completes synchronously, so hand the lease back without going through the
        // async state machine.
        if (pending.IsCompletedSuccessfully)
            return new ValueTask<KeyedAsyncLockLease>(new KeyedAsyncLockLease(this, key, entry, pending.Result));

        return AwaitLockAsync(key, entry, pending);
    }

    private async ValueTask<KeyedAsyncLockLease> AwaitLockAsync(TKey key, Entry entry, ValueTask<AsyncLock.AsyncLockLease> pending)
    {
        try
        {
            var lease = await pending.ConfigureAwait(false);
            return new KeyedAsyncLockLease(this, key, entry, lease);
        }
        catch
        {
            // The lock was not acquired (e.g. cancellation), so undo the reservation.
            _locks.Release(key, entry);
            throw;
        }
    }

    private void Release(TKey key, Entry entry, AsyncLock.AsyncLockLease lease)
    {
        // The per-key lock only lets the first release of an acquisition through, so a lease disposed twice
        // cannot drop the entry's reference count twice and evict an entry somebody else is still using.
        if (lease.TryRelease())
        {
            _locks.Release(key, entry);
        }
    }

    internal sealed class Entry : KeyedEntry
    {
        public AsyncLock Lock { get; } = new();
    }

    /// <summary>Represents a disposable lease for a <see cref="KeyedAsyncLock{TKey}"/>. Disposing the lease releases the lock for the key.</summary>
    /// <remarks>Only the first disposal of a lease releases the key. Disposing the same lease again, or disposing a
    /// copy of an already-disposed lease, does nothing instead of releasing an acquisition made in the meantime.</remarks>
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
