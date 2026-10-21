using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The own properties of an object, in insertion order, stored in one array of entries. Small tables are searched
/// linearly; larger ones have a hash index (open addressing over the positions of the entries). A deleted entry leaves a
/// tombstone, removed when the table is compacted. An entry is 32 bytes: the fields of its key are stored without the
/// padding of <see cref="PropertyKey"/>, and the hash codes are not stored (the keys are mostly the same string
/// instances, compared by reference first).
/// </summary>
internal sealed class PropertyTable : PropertyMap
{
    private const int IndexThreshold = 8;

    private Entry[] _entries;

    // The index: the position of an entry + 1 (0: an empty slot), at the slot of its hash code (linear probing). Its length
    // is a power of 2, and it is rebuilt when it is two-thirds full (the tombstones stay in the probe sequences).
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
            if (key.EqualsRaw(entry.KeyValue, entry.KeyIndex) && !entry.IsDeleted)
                return i;
        }

        return -1;
    }

    private static int FindIndexed(int[] index, Entry[] entries, in PropertyKey key)
    {
        var mask = index.Length - 1;
        for (var i = key.GetHashCode() & mask; ; i = (i + 1) & mask)
        {
            var slot = index[i];
            if (slot == 0)
                return -1;

            ref var entry = ref entries[slot - 1];
            if (key.EqualsRaw(entry.KeyValue, entry.KeyIndex) && !entry.IsDeleted)
                return slot - 1;
        }
    }

    public PropertyKey GetKey(int position)
    {
        ref var entry = ref _entries[position];
        return PropertyKey.FromRaw(entry.KeyValue, entry.KeyIndex);
    }

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
        entry.KeyValue = key.RawValue;
        entry.KeyIndex = key.RawIndex;
        entry.Value = value;
        entry.Flags = flags;
        entry.IsDeleted = false;
        if (_index is not null)
        {
            if (_count * 3 > _index.Length * 2)
            {
                BuildIndex();
            }
            else
            {
                Insert(_index, key.GetHashCode(), position);
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
        // At most half full after a rebuild
        var length = 16;
        while (length < _count * 2)
        {
            length *= 2;
        }

        var index = new int[length];
        for (var i = 0; i < _count; i++)
        {
            ref var entry = ref _entries[i];
            if (!entry.IsDeleted)
            {
                Insert(index, PropertyKey.FromRaw(entry.KeyValue, entry.KeyIndex).GetHashCode(), i);
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
        public object? KeyValue;
        public uint KeyIndex;
        public PropertyFlags Flags;
        public bool IsDeleted;
        public JsValue Value;
    }
}
