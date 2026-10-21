using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The own properties of an object, in insertion order, stored in one array of entries. Small tables are searched
/// linearly; larger ones have a hash index (open addressing over the positions of the entries: the hash code is stored in
/// the entry, in its padding). A deleted entry leaves a tombstone, removed when the table is compacted.
/// </summary>
internal sealed class PropertyTable : PropertyMap
{
    private const int IndexThreshold = 8;

    private Entry[] _entries;

    // The index: the position of an entry + 1 (0: an empty slot), at the slot of its hash code (linear probing). Its length
    // is a power of 2, at least twice the number of entries (the tombstones stay in the probe sequences).
    private int[]? _index;
    private int _count;
    private int _deletedCount;

    public PropertyTable(int capacity = 4)
    {
        _entries = new Entry[capacity];
    }

    /// <summary>Gets the number of times the table was compacted (the positions of the properties changed).</summary>
    public int Compactions { get; private set; }

    /// <summary>Gets the number of properties.</summary>
    public int Count => _count - _deletedCount;

    /// <summary>Finds the position of a key, or -1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Find(in PropertyKey key)
    {
        var entries = _entries;
        if (_index is { } index)
            return FindIndexed(index, entries, key);

        for (var i = 0; i < _count; i++)
        {
            ref var entry = ref entries[i];
            if (entry.Key.Equals(key) && !entry.IsDeleted)
                return i;
        }

        return -1;
    }

    private static int FindIndexed(int[] index, Entry[] entries, in PropertyKey key)
    {
        var hash = key.GetHashCode();
        var mask = index.Length - 1;
        for (var i = hash & mask; ; i = (i + 1) & mask)
        {
            var slot = index[i];
            if (slot == 0)
                return -1;

            ref var entry = ref entries[slot - 1];
            if (entry.Hash == hash && !entry.IsDeleted && entry.Key.Equals(key))
                return slot - 1;
        }
    }

    public PropertyKey GetKey(int position) => _entries[position].Key;

    public ref JsValue GetValue(int position) => ref _entries[position].Value;

    public ref PropertyFlags GetFlags(int position) => ref _entries[position].Flags;

    public bool IsDeleted(int position) => _entries[position].IsDeleted;

    /// <summary>Gets the number of positions to enumerate (including the deleted ones: check <see cref="IsDeleted"/>).</summary>
    public int Capacity => _count;

    public int Add(in PropertyKey key, JsValue value, PropertyFlags flags)
    {
        if (_count == _entries.Length)
        {
            if (_deletedCount > _count / 2)
            {
                Compact();
                Compactions++;
            }

            if (_count == _entries.Length)
            {
                Array.Resize(ref _entries, _entries.Length * 2);
            }
        }

        var position = _count++;
        ref var entry = ref _entries[position];
        entry.Key = key;
        entry.Value = value;
        entry.Flags = flags;
        entry.IsDeleted = false;
        if (_index is not null)
        {
            entry.Hash = key.GetHashCode();
            if (_count * 2 > _index.Length)
            {
                BuildIndex();
            }
            else
            {
                Insert(_index, entry.Hash, position);
            }
        }
        else if (_count > IndexThreshold)
        {
            BuildIndex();
        }

        return position;
    }

    public void Remove(int position)
    {
        ref var entry = ref _entries[position];
        entry.IsDeleted = true;
        entry.Value = default;
        _deletedCount++;
        if (_deletedCount == _count)
        {
            // The table is empty: start again (keeps the enumeration cheap after many deletions)
            Array.Clear(_entries, 0, _count);
            _count = 0;
            _deletedCount = 0;
            _index = null;
        }
    }

    private void Compact()
    {
        var target = 0;
        for (var i = 0; i < _count; i++)
        {
            if (_entries[i].IsDeleted)
                continue;

            _entries[target] = _entries[i];
            target++;
        }

        Array.Clear(_entries, target, _count - target);
        _count = target;
        _deletedCount = 0;
        _index = null;
        if (_count > IndexThreshold)
        {
            BuildIndex();
        }
    }

    private void BuildIndex()
    {
        var length = 16;
        while (length < _count * 4)
        {
            length *= 2;
        }

        var index = new int[length];
        for (var i = 0; i < _count; i++)
        {
            ref var entry = ref _entries[i];
            if (!entry.IsDeleted)
            {
                entry.Hash = entry.Key.GetHashCode();
                Insert(index, entry.Hash, i);
            }
        }

        _index = index;
    }

    private static void Insert(int[] index, int hash, int position)
    {
        var mask = index.Length - 1;
        var i = hash & mask;
        while (index[i] != 0)
        {
            i = (i + 1) & mask;
        }

        index[i] = position + 1;
    }

    private struct Entry
    {
        public PropertyKey Key;
        public JsValue Value;
        public int Hash;
        public PropertyFlags Flags;
        public bool IsDeleted;
    }
}
