using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The [[MapData]] / [[SetData]] list of a Map or a Set: an insertion-ordered hash table keyed by SameValueZero.
/// </summary>
/// <remarks>
/// The specification deletes an entry by leaving an empty slot in the list, so that an iterator keeps its index. The
/// table does the same, and compacts the slots when it grows or shrinks: the compaction creates a new storage and links
/// the old one to it with the indices of the removed slots (or a "cleared" marker), so that a cursor on the old storage
/// translates its index when it moves next. Cursors are not registered: an abandoned iterator costs nothing.
/// </remarks>
internal sealed class OrderedHashTable
{
    private const int MinCapacity = 4;

    private readonly bool _hasValues;
    private Storage _storage;

    public OrderedHashTable(bool hasValues)
    {
        _hasValues = hasValues;
        _storage = Storage.Empty(hasValues);
    }

    /// <summary>Gets the number of entries (SetDataSize).</summary>
    public int Count => _storage.Live;

    /// <summary>CanonicalizeKeyedCollectionKey(key): -0 becomes +0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsValue CanonicalizeKey(JsValue key) => key.IsNumber && key.NumberValue == 0 ? new JsValue(0d) : key;

    /// <summary>Gets a cursor at the start of the list.</summary>
    public Cursor GetCursor() => new(_storage, 0);

    public bool ContainsKey(JsValue key) => _storage.Find(CanonicalizeKey(key)) >= 0;

    public bool TryGetValue(JsValue key, out JsValue value)
    {
        var storage = _storage;
        var index = storage.Find(CanonicalizeKey(key));
        if (index < 0)
        {
            value = default;
            return false;
        }

        value = storage.Values![index];
        return true;
    }

    /// <summary>Sets the value of the key, or appends a new entry.</summary>
    public void Set(JsValue key, JsValue value)
    {
        key = CanonicalizeKey(key);
        var storage = _storage;
        var hash = Storage.GetHash(key);
        var index = storage.Find(key, hash);
        if (index >= 0)
        {
            storage.Values![index] = value;
            return;
        }

        Append(key, value, hash);
    }

    /// <summary>Appends the key if it is not in the table; returns false when it is already present.</summary>
    public bool Add(JsValue key)
    {
        key = CanonicalizeKey(key);
        var hash = Storage.GetHash(key);
        if (_storage.Find(key, hash) >= 0)
            return false;

        Append(key, default, hash);
        return true;
    }

    /// <summary>Removes the entry of the key; returns false when it is not in the table.</summary>
    public bool Remove(JsValue key)
    {
        var storage = _storage;
        var index = storage.Find(CanonicalizeKey(key));
        if (index < 0)
            return false;

        storage.Keys[index] = JsValue.Hole;
        if (storage.Values is { } values)
        {
            values[index] = default;
        }

        storage.Live--;

        // Shrink a table that is mostly empty
        if (storage.Keys.Length > 16 && storage.Live < storage.Keys.Length / 4)
        {
            Compact(Math.Max(MinCapacity, storage.Live * 2));
        }

        return true;
    }

    /// <summary>Removes all the entries; the cursors continue with the entries appended later.</summary>
    public void Clear()
    {
        var storage = _storage;
        if (storage.Used == 0)
            return;

        var newStorage = Storage.Empty(_hasValues);
        storage.MarkCleared(newStorage);
        _storage = newStorage;
    }

    /// <summary>Creates a copy of the table (the entries, without the empty slots).</summary>
    public OrderedHashTable Clone()
    {
        var result = new OrderedHashTable(_hasValues);
        var storage = _storage;
        if (storage.Live > 0)
        {
            var newStorage = new Storage(storage.Live, _hasValues);
            for (var i = 0; i < storage.Used; i++)
            {
                if (!storage.Keys[i].IsHole)
                {
                    newStorage.AppendUnchecked(storage.Keys[i], storage.Values is null ? default : storage.Values[i], storage.Hashes[i]);
                }
            }

            result._storage = newStorage;
        }

        return result;
    }

    private void Append(JsValue key, JsValue value, int hash)
    {
        var storage = _storage;
        if (storage.Used == storage.Keys.Length)
        {
            if (storage.Live <= storage.Used / 2 && storage.Used >= MinCapacity)
            {
                // Many empty slots: compact them instead of growing
                Compact(Math.Max(MinCapacity, storage.Keys.Length));
            }
            else
            {
                storage.Grow(Math.Max(MinCapacity, storage.Keys.Length * 2));
            }

            storage = _storage;
        }

        storage.AppendUnchecked(key, value, hash);
    }

    private void Compact(int capacity)
    {
        var storage = _storage;
        var newStorage = new Storage(capacity, _hasValues);
        var removed = new int[storage.Used - storage.Live];
        var removedCount = 0;
        for (var i = 0; i < storage.Used; i++)
        {
            if (storage.Keys[i].IsHole)
            {
                removed[removedCount++] = i;
            }
            else
            {
                newStorage.AppendUnchecked(storage.Keys[i], storage.Values is null ? default : storage.Values[i], storage.Hashes[i]);
            }
        }

        storage.MarkCompacted(newStorage, removed);
        _storage = newStorage;
    }

    /// <summary>A position in the list of entries; it follows the compactions of the table.</summary>
    internal struct Cursor
    {
        private Storage? _storage;
        private int _index;

        internal Cursor(Storage storage, int index)
        {
            _storage = storage;
            _index = index;
        }

        /// <summary>Moves to the next entry; returns false at the end of the list (the cursor can move again later, when entries are appended).</summary>
        public bool MoveNext(out JsValue key, out JsValue value)
        {
            var storage = _storage;
            if (storage is null)
            {
                key = default;
                value = default;
                return false;
            }

            var index = _index;
            while (storage.Next is { } next)
            {
                index = storage.TranslateIndex(index);
                storage = next;
            }

            var keys = storage.Keys;
            while (index < storage.Used)
            {
                var entryKey = keys[index];
                if (!entryKey.IsHole)
                {
                    key = entryKey;
                    value = storage.Values is { } values ? values[index] : entryKey;
                    _storage = storage;
                    _index = index + 1;
                    return true;
                }

                index++;
            }

            _storage = storage;
            _index = index;
            key = default;
            value = default;
            return false;
        }

        /// <summary>Releases the table: the cursor stays at the end.</summary>
        public void Close() => _storage = null;
    }

    /// <summary>The arrays of the table. An obsolete storage (replaced by a compaction or a clear) only keeps the translation of the indices.</summary>
    internal sealed class Storage
    {
        private static readonly int[] EmptyBuckets = new int[1];

        // The buckets and the chains store index + 1 (0 is the end of a chain)
        private int[] _buckets;
        private int[] _chains;
        private int[]? _removedIndices;
        private bool _cleared;

        public Storage(int capacity, bool hasValues)
        {
            Keys = new JsValue[capacity];
            Values = hasValues ? new JsValue[capacity] : null;
            Hashes = new int[capacity];
            _chains = new int[capacity];
            _buckets = new int[GetBucketCount(capacity)];
        }

        private Storage(bool hasValues)
        {
            Keys = [];
            Values = hasValues ? [] : null;
            Hashes = [];
            _chains = [];
            _buckets = EmptyBuckets;
        }

        /// <summary>The keys; a deleted entry is <see cref="JsValue.Hole"/>.</summary>
        public JsValue[] Keys { get; private set; }

        /// <summary>The values (null for a Set).</summary>
        public JsValue[]? Values { get; private set; }

        public int[] Hashes { get; private set; }

        /// <summary>The number of slots used: the length of the list, including the empty slots.</summary>
        public int Used { get; private set; }

        /// <summary>The number of entries.</summary>
        public int Live { get; set; }

        /// <summary>The storage that replaced this one, or null when this one is current.</summary>
        public Storage? Next { get; private set; }

        public static Storage Empty(bool hasValues) => new(hasValues);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetHash(JsValue key)
        {
            var hash = key.GetHashCode();
            return hash ^ (hash >>> 16);
        }

        private static int GetBucketCount(int capacity)
        {
            var count = 1;
            while (count < capacity)
            {
                count <<= 1;
            }

            return count;
        }

        public int Find(JsValue key) => Find(key, GetHash(key));

        /// <summary>Finds the index of a canonicalized key, or -1.</summary>
        public int Find(JsValue key, int hash)
        {
            var keys = Keys;
            var hashes = Hashes;
            var chains = _chains;
            for (var i = _buckets[hash & (_buckets.Length - 1)] - 1; i >= 0; i = chains[i] - 1)
            {
                if (hashes[i] == hash && JsOperations.SameValueZero(keys[i], key) && !keys[i].IsHole)
                    return i;
            }

            return -1;
        }

        public void AppendUnchecked(JsValue key, JsValue value, int hash)
        {
            var index = Used++;
            Keys[index] = key;
            if (Values is { } values)
            {
                values[index] = value;
            }

            Hashes[index] = hash;
            ref var bucket = ref _buckets[hash & (_buckets.Length - 1)];
            _chains[index] = bucket;
            bucket = index + 1;
            Live++;
        }

        /// <summary>Grows the arrays in place: the indices do not change.</summary>
        public void Grow(int capacity)
        {
            var keys = Keys;
            Array.Resize(ref keys, capacity);
            Keys = keys;
            if (Values is { } values)
            {
                Array.Resize(ref values, capacity);
                Values = values;
            }

            var hashes = Hashes;
            Array.Resize(ref hashes, capacity);
            Hashes = hashes;
            _chains = new int[capacity];
            _buckets = new int[GetBucketCount(capacity)];
            for (var i = 0; i < Used; i++)
            {
                ref var bucket = ref _buckets[Hashes[i] & (_buckets.Length - 1)];
                _chains[i] = bucket;
                bucket = i + 1;
            }
        }

        public void MarkCompacted(Storage next, int[] removedIndices)
        {
            Next = next;
            _removedIndices = removedIndices;
            Release();
        }

        public void MarkCleared(Storage next)
        {
            Next = next;
            _cleared = true;
            Release();
        }

        /// <summary>Translates an index of this (obsolete) storage to the index of the same position in <see cref="Next"/>.</summary>
        public int TranslateIndex(int index)
        {
            if (_cleared)
                return 0;

            // The number of removed slots before the index
            var removed = _removedIndices!;
            var position = Array.BinarySearch(removed, index);
            if (position < 0)
            {
                position = ~position;
            }

            return index - position;
        }

        private void Release()
        {
            Keys = [];
            Values = null;
            Hashes = [];
            _chains = [];
            _buckets = EmptyBuckets;
            Used = 0;
            Live = 0;
        }
    }
}
