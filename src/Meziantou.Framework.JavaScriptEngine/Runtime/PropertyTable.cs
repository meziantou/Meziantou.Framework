using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The own properties of an object, in insertion order, stored in one array of entries. Small tables are searched
/// linearly; larger ones have a hash index (open addressing over the positions of the entries). A deleted entry leaves a
/// tombstone in the entries (not in the index), removed when the table is compacted, or at once when it is the last
/// entry (so that adding and deleting a property again and again does not fill the table). An entry is 32 bytes: the fields of its key are stored without the
/// padding of <see cref="PropertyKey"/>, and the hash codes are not stored (the keys are mostly the same string
/// instances, compared by reference first).
/// </summary>
internal sealed class PropertyTable : PropertyMap
{
    private const int IndexThreshold = 8;

    private Entry[] _entries;

    // The index: the position of an entry + 1 (0: an empty slot), at the slot of its hash code (linear probing). Its length
    // is a power of 2, and it is rebuilt when it is two-thirds full. A removal shifts the next slots of its probe sequence
    // back, so the index has no tombstone.
    private int[]? _index;
    private int _count;
    private int _deletedCount;

    // The keys that are array indices, sorted: built when they are first read in order, then kept up to date (null: not built)
    private uint[]? _sortedIndices;
    private int _sortedIndexCount;

    public PropertyTable(int capacity = 4)
    {
        _entries = new Entry[capacity];
    }

    /// <summary>Gets or sets the table of built-in methods that learns the number of properties of the object (<see cref="BuiltinMethodTable.PropertyCount"/>).</summary>
    public BuiltinMethodTable? CapacityHint { get; set; }

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

    /// <summary>Makes room for <paramref name="capacity"/> properties (the entries and the index are allocated once).</summary>
    public void EnsureCapacity(int capacity)
    {
        if (capacity > _entries.Length)
        {
            Array.Resize(ref _entries, capacity);
        }

        if (capacity > IndexThreshold && (_index is null || _index.Length < capacity * 2))
        {
            BuildIndex(capacity);
        }
    }

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
        if (key.IsArrayIndex && _sortedIndices is not null)
        {
            InsertSortedIndex(key.ArrayIndex);
        }

        if (_index is not null)
        {
            if (Count * 3 > _index.Length * 2)
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

        CapacityHint?.LearnPropertyCount(Count);
        return position;
    }

    public void Remove(int position)
    {
        ref var entry = ref _entries[position];
        var key = PropertyKey.FromRaw(entry.KeyValue, entry.KeyIndex);
        if (_index is not null)
        {
            RemoveFromIndex(_index, key.GetHashCode(), position);
        }

        if (key.IsArrayIndex && _sortedIndices is not null)
        {
            RemoveSortedIndex(key.ArrayIndex);
        }

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
            _sortedIndices = null;
            return;
        }

        // The deleted entries at the end are not in the index: they are removed (the positions of the others do not change)
        while (_entries[_count - 1].IsDeleted)
        {
            _count--;
            _deletedCount--;
            _entries[_count] = default;
        }
    }

    /// <summary>Gets the keys that are array indices, in ascending order.</summary>
    public ReadOnlySpan<uint> GetSortedIndices()
    {
        if (_sortedIndices is null)
        {
            var count = 0;
            for (var i = 0; i < _count; i++)
            {
                ref var entry = ref _entries[i];
                if (!entry.IsDeleted && entry.KeyIndex != uint.MaxValue)
                {
                    count++;
                }
            }

            var indices = new uint[Math.Max(count, 4)];
            count = 0;
            for (var i = 0; i < _count; i++)
            {
                ref var entry = ref _entries[i];
                if (!entry.IsDeleted && entry.KeyIndex != uint.MaxValue)
                {
                    indices[count++] = entry.KeyIndex;
                }
            }

            Array.Sort(indices, 0, count);
            _sortedIndices = indices;
            _sortedIndexCount = count;
        }

        return _sortedIndices.AsSpan(0, _sortedIndexCount);
    }

    private void InsertSortedIndex(uint index)
    {
        var indices = _sortedIndices!;
        var count = _sortedIndexCount;
        var position = count == 0 || indices[count - 1] < index ? count : ~indices.AsSpan(0, count).BinarySearch(index);
        if (count == indices.Length)
        {
            Array.Resize(ref _sortedIndices, count * 2);
            indices = _sortedIndices;
        }

        Array.Copy(indices, position, indices, position + 1, count - position);
        indices[position] = index;
        _sortedIndexCount = count + 1;
    }

    private void RemoveSortedIndex(uint index)
    {
        var indices = _sortedIndices!;
        var position = indices.AsSpan(0, _sortedIndexCount).BinarySearch(index);
        _sortedIndexCount--;
        Array.Copy(indices, position + 1, indices, position, _sortedIndexCount - position);
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

    private void BuildIndex(int capacity = 0)
    {
        // At most half full after a rebuild (or when the table has the expected number of entries)
        var length = 16;
        while (length < Math.Max(_count, capacity) * 2)
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

    /// <summary>Removes the slot of an entry from the index, and moves back the next slots of the probe sequence that may take its place.</summary>
    private void RemoveFromIndex(int[] index, int hash, int position)
    {
        var mask = index.Length - 1;
        var empty = hash & mask;
        while (index[empty] != position + 1)
        {
            empty = (empty + 1) & mask;
        }

        for (var i = (empty + 1) & mask; index[i] != 0; i = (i + 1) & mask)
        {
            ref var entry = ref _entries[index[i] - 1];
            var home = PropertyKey.FromRaw(entry.KeyValue, entry.KeyIndex).GetHashCode() & mask;

            // The slot can move to the empty slot when its home slot is not in (empty, i] (cyclically)
            var stays = empty <= i ? home > empty && home <= i : home > empty || home <= i;
            if (!stays)
            {
                index[empty] = index[i];
                empty = i;
            }
        }

        index[empty] = 0;
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
