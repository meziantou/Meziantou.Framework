using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The own properties of an object, in insertion order. Small tables are searched linearly; larger ones have a hash
/// index. A deleted entry leaves a tombstone, removed when the table is compacted.
/// </summary>
internal sealed class PropertyTable
{
    private const int IndexThreshold = 8;

    private PropertyKey[] _keys;
    private JsValue[] _values;
    private PropertyFlags[] _flags;
    private bool[]? _deleted;
    private Dictionary<PropertyKey, int>? _index;
    private int _count;
    private int _deletedCount;

    public PropertyTable(int capacity = 4)
    {
        _keys = new PropertyKey[capacity];
        _values = new JsValue[capacity];
        _flags = new PropertyFlags[capacity];
    }

    /// <summary>Gets the number of properties.</summary>
    public int Count => _count - _deletedCount;

    /// <summary>Finds the position of a key, or -1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Find(in PropertyKey key)
    {
        if (_index is not null)
            return _index.TryGetValue(key, out var position) ? position : -1;

        for (var i = 0; i < _count; i++)
        {
            if (_keys[i].Equals(key) && (_deleted is null || !_deleted[i]))
                return i;
        }

        return -1;
    }

    public PropertyKey GetKey(int position) => _keys[position];

    public ref JsValue GetValue(int position) => ref _values[position];

    public ref PropertyFlags GetFlags(int position) => ref _flags[position];

    public bool IsDeleted(int position) => _deleted is not null && _deleted[position];

    /// <summary>Gets the number of positions to enumerate (including the deleted ones: check <see cref="IsDeleted"/>).</summary>
    public int Capacity => _count;

    public int Add(in PropertyKey key, JsValue value, PropertyFlags flags)
    {
        if (_count == _keys.Length)
        {
            if (_deletedCount > _count / 2)
            {
                Compact();
            }

            if (_count == _keys.Length)
            {
                var newSize = _keys.Length * 2;
                Array.Resize(ref _keys, newSize);
                Array.Resize(ref _values, newSize);
                Array.Resize(ref _flags, newSize);
                if (_deleted is not null)
                {
                    Array.Resize(ref _deleted, newSize);
                }
            }
        }

        var position = _count++;
        _keys[position] = key;
        _values[position] = value;
        _flags[position] = flags;
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
        _deleted ??= new bool[_keys.Length];
        _deleted[position] = true;
        _values[position] = default;
        _deletedCount++;
        _index?.Remove(_keys[position]);
        if (_deletedCount == _count)
        {
            // The table is empty: start again (keeps the enumeration cheap after many deletions)
            Array.Clear(_keys, 0, _count);
            _count = 0;
            _deletedCount = 0;
            _deleted = null;
            _index = null;
        }
    }

    private void Compact()
    {
        var target = 0;
        for (var i = 0; i < _count; i++)
        {
            if (_deleted![i])
                continue;

            _keys[target] = _keys[i];
            _values[target] = _values[i];
            _flags[target] = _flags[i];
            target++;
        }

        Array.Clear(_keys, target, _count - target);
        Array.Clear(_values, target, _count - target);
        _count = target;
        _deletedCount = 0;
        _deleted = null;
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
            if (_deleted is null || !_deleted[i])
            {
                _index[_keys[i]] = i;
            }
        }
    }
}
