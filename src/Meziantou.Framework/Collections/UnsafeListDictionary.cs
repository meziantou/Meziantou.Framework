using System.Collections;

namespace Meziantou.Framework.Collections;

/// <summary>
/// This dictionary doesn't ensure the items are unique.
/// For instance, you can use it to create a list of values to serialize without spending time validating the items.
/// </summary>
public sealed class UnsafeListDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private readonly List<KeyValuePair<TKey, TValue>> _items;

    public UnsafeListDictionary() => _items = new();
    public UnsafeListDictionary(int capacity) => _items = new(capacity);
    public UnsafeListDictionary(IEnumerable<KeyValuePair<TKey, TValue>> items) => _items = new(items);

    public TValue this[TKey key]
    {
        get
        {
            var index = FindIndex(key);
            if (index == -1)
                throw new KeyNotFoundException($"Key '{key}' not found");

            return _items[index].Value;
        }
        set
        {
            var index = FindIndex(key);
            if (index == -1)
            {
                Add(key, value);
            }
            else
            {
                _items[index] = new KeyValuePair<TKey, TValue>(_items[index].Key, value);
            }
        }
    }

    public ICollection<TKey> Keys => _items.Select(item => item.Key).ToList();
    public ICollection<TValue> Values => _items.Select(item => item.Value).ToList();
    public int Count => _items.Count;
    public void Add(TKey key, TValue value) => _items.Add(new(key, value));
    public void Add(KeyValuePair<TKey, TValue> item) => _items.Add(item);
    public void AddRange(IEnumerable<KeyValuePair<TKey, TValue>> items) => _items.AddRange(items);

    public void Clear() => _items.Clear();
    public bool Contains(KeyValuePair<TKey, TValue> item) => _items.Contains(item);
    public bool ContainsKey(TKey key) => _items.Exists(item => KeyEqual(item.Key, key));

    public bool Remove(TKey key)
    {
        var index = FindIndex(key);
        if (index == -1)
            return false;

        _items.RemoveAt(index);
        return true;
    }

    public bool Remove(KeyValuePair<TKey, TValue> item) => _items.Remove(item);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        var index = FindIndex(key);
        if (index == -1)
        {
            value = default;
            return false;
        }

        value = _items[index].Value;
        return true;
    }

    private int FindIndex(TKey key)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (KeyEqual(_items[i].Key, key))
                return i;
        }

        return -1;
    }

    private static bool KeyEqual(TKey item1, TKey item2)
    {
        return EqualityComparer<TKey>.Default.Equals(item1, item2);
    }

    public List<KeyValuePair<TKey, TValue>>.Enumerator GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => ((ICollection<KeyValuePair<TKey, TValue>>)_items).CopyTo(array, arrayIndex);
    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;
    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;
}
