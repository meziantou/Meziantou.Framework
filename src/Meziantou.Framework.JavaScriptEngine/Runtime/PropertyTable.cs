using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The own properties of an object, in insertion order, stored in one array of entries. Small tables are searched
/// linearly; larger ones have a hash index. A deleted entry leaves a tombstone, removed when the table is compacted.
/// </summary>
internal sealed class PropertyTable : PropertyMap
{
    private const int IndexThreshold = 8;

    private Entry[] _entries;
    private Dictionary<PropertyKey, int>? _index;
    private int _count;
    private int _deletedCount;

    public PropertyTable(int capacity = 4)
    {
        _entries = new Entry[capacity];
    }

    /// <summary>Gets the number of properties.</summary>
    public int Count => _count - _deletedCount;

    /// <summary>Finds the position of a key, or -1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Find(in PropertyKey key)
    {
        if (_index is not null)
            return _index.TryGetValue(key, out var position) ? position : -1;

        var entries = _entries;
        for (var i = 0; i < _count; i++)
        {
            ref var entry = ref entries[i];
            if (entry.Key.Equals(key) && !entry.IsDeleted)
                return i;
        }

        return -1;
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
            _index[key] = position;
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
        _index?.Remove(entry.Key);
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
        _index = new Dictionary<PropertyKey, int>(_count * 2);
        for (var i = 0; i < _count; i++)
        {
            if (!_entries[i].IsDeleted)
            {
                _index[_entries[i].Key] = i;
            }
        }
    }

    private struct Entry
    {
        public PropertyKey Key;
        public JsValue Value;
        public PropertyFlags Flags;
        public bool IsDeleted;
    }
}
