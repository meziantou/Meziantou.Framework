using System.Numerics;

namespace Meziantou.Framework.Threading;

/// <summary>Reference-counted per-key state shared by <see cref="KeyedLock{TKey}"/> and <see cref="KeyedAsyncLock{TKey}"/>.</summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <typeparam name="TEntry">The type of the per-key state.</typeparam>
/// <remarks>
/// Entries are reference-counted and evicted once nobody holds or waits for a key, so the table doesn't grow
/// without bound when used with high-cardinality keys.
/// <para>
/// The bookkeeping sits on the critical path of every acquisition, so it is sharded across several dictionaries:
/// a single dictionary means a single monitor, which serializes acquisitions of unrelated keys and so defeats the
/// point of a keyed lock. Sharding keeps unrelated keys on different monitors most of the time.
/// </para>
/// </remarks>
internal sealed class KeyedEntryTable<TKey, TEntry>
    where TKey : notnull
    where TEntry : KeyedEntry
{
    // Enough shards to spread the load over the machine's cores without allocating an unreasonable number of
    // dictionaries per instance. Each shard is a dictionary that stays empty until one of its keys is used.
    private const int MaxShardCount = 32;

    private readonly Dictionary<TKey, TEntry>[] _shards;
    private readonly IEqualityComparer<TKey>? _comparer;
    private readonly Func<TEntry> _entryFactory;
    private readonly uint _shardMask;

    public KeyedEntryTable(IEqualityComparer<TKey>? comparer, Func<TEntry> entryFactory)
    {
        _comparer = comparer;
        _entryFactory = entryFactory;

        var shardCount = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Min(Environment.ProcessorCount, MaxShardCount));
        _shardMask = (uint)(shardCount - 1);
        _shards = new Dictionary<TKey, TEntry>[shardCount];
        for (var i = 0; i < _shards.Length; i++)
        {
            _shards[i] = new Dictionary<TKey, TEntry>(comparer);
        }
    }

    /// <summary>Gets the number of keys currently tracked.</summary>
    /// <remarks>The count is a snapshot: shards are sampled one after another, not atomically.</remarks>
    public int Count
    {
        get
        {
            var count = 0;
            foreach (var shard in _shards)
            {
                lock (shard)
                {
                    count += shard.Count;
                }
            }

            return count;
        }
    }

    /// <summary>Gets the entry for <paramref name="key"/>, creating it when needed, and adds a reference to it.</summary>
    /// <param name="key">The key to reserve.</param>
    /// <returns>The reserved entry.</returns>
    /// <remarks>
    /// The reference is taken before the bookkeeping lock is released so a concurrent release cannot evict the
    /// entry from under the caller while it waits to acquire the per-key lock.
    /// </remarks>
    public TEntry Reserve(TKey key)
    {
        var shard = GetShard(key);
        lock (shard)
        {
            if (!shard.TryGetValue(key, out var entry))
            {
                entry = _entryFactory();
                shard.Add(key, entry);
            }

            entry.ReferenceCount++;
            return entry;
        }
    }

    /// <summary>Removes a reference added by <see cref="Reserve"/>, evicting the entry along with the last reference.</summary>
    /// <param name="key">The key the entry was reserved for.</param>
    /// <param name="entry">The entry returned by <see cref="Reserve"/>.</param>
    public void Release(TKey key, TEntry entry)
    {
        var shard = GetShard(key);
        lock (shard)
        {
            if (--entry.ReferenceCount == 0)
            {
                shard.Remove(key);
            }
        }
    }

    private Dictionary<TKey, TEntry> GetShard(TKey key)
    {
        // A null comparer means the shards use EqualityComparer<TKey>.Default, whose hash code is the key's own;
        // calling it directly lets the JIT devirtualize it instead of going through an interface call.
        var hash = (uint)(_comparer is null ? key.GetHashCode() : _comparer.GetHashCode(key));

        // The shard index comes from the low bits of the hash code, and those are often poorly distributed
        // (sequential integers being the obvious case), so mix the high bits down before masking.
        hash *= 2654435769u;
        hash ^= hash >> 16;
        return _shards[hash & _shardMask];
    }
}
