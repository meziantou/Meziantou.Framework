using System.Collections;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Collections;

public sealed class MultiValueDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, IReadOnlyCollection<TValue>>
    where TKey : notnull
{
    private readonly Dictionary<TKey, ValueCollection> _dictionary;

    public MultiValueDictionary() => _dictionary = [];
    public MultiValueDictionary(int capacity) => _dictionary = new(capacity);
    public MultiValueDictionary(IEqualityComparer<TKey>? comparer) => _dictionary = new(comparer);
    public MultiValueDictionary(int capacity, IEqualityComparer<TKey>? comparer) => _dictionary = new(capacity, comparer);

    public MultiValueDictionary(IEnumerable<KeyValuePair<TKey, IReadOnlyCollection<TValue>>> enumerable)
        : this(enumerable, comparer: null)
    {
    }

    public MultiValueDictionary(IEnumerable<KeyValuePair<TKey, IReadOnlyCollection<TValue>>> enumerable, IEqualityComparer<TKey>? comparer)
    {
        ArgumentNullException.ThrowIfNull(enumerable);

        _dictionary = new(comparer);
        foreach (var pair in enumerable)
        {
            if (pair.Value is null)
                throw new ArgumentException("The value collection cannot be null.", nameof(enumerable));

            AddRange(pair.Key, pair.Value);
        }
    }

    public void Add(TKey key, TValue value) => GetOrAddCollection(key).Add(value);

    public void AddRange(TKey key, IEnumerable<TValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        GetOrAddCollection(key).AddRange(values);
    }

    public bool Remove(TKey key) => _dictionary.Remove(key);

    public bool Remove(TKey key, TValue value)
    {
        if (!_dictionary.TryGetValue(key, out var collection))
            return false;

        if (!collection.Remove(value))
            return false;

        if (collection.Count is 0)
        {
            _dictionary.Remove(key);
        }

        return true;
    }

    public bool Contains(TKey key, TValue value) => _dictionary.TryGetValue(key, out var collection) && collection.Contains(value);

    public bool ContainsValue(TValue value)
    {
        foreach (var collection in _dictionary.Values)
        {
            if (collection.Contains(value))
                return true;
        }

        return false;
    }

    public void Clear() => _dictionary.Clear();

    public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out IReadOnlyCollection<TValue> value)
    {
        var success = _dictionary.TryGetValue(key, out var collection);
        value = collection;
        return success;
    }

    public IEnumerable<TKey> Keys => _dictionary.Keys;
    public IEnumerable<IReadOnlyCollection<TValue>> Values => _dictionary.Values;

    public IReadOnlyCollection<TValue> this[TKey key] => _dictionary[key];
    public int Count => _dictionary.Count;

    public IEnumerator<KeyValuePair<TKey, IReadOnlyCollection<TValue>>> GetEnumerator()
    {
        foreach (var pair in _dictionary)
        {
            yield return new(pair.Key, pair.Value);
        }
    }

    IEnumerator<KeyValuePair<TKey, IReadOnlyCollection<TValue>>> IEnumerable<KeyValuePair<TKey, IReadOnlyCollection<TValue>>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private ValueCollection GetOrAddCollection(TKey key)
    {
        ref var collection = ref CollectionsMarshal.GetValueRefOrAddDefault(_dictionary, key, out _);
        collection ??= new();
        return collection;
    }

    private sealed class ValueCollection : IReadOnlyCollection<TValue>
    {
        private readonly List<TValue> _values = new List<TValue>(1); // Most of the time, there will be only one value per key, so we start with a capacity of 1
        public int Count => _values.Count;
        public void Add(TValue value) => _values.Add(value);
        public void AddRange(IEnumerable<TValue> values) => _values.AddRange(values);
        public bool Remove(TValue value) => _values.Remove(value);
        public bool Contains(TValue value) => _values.Contains(value);
        public List<TValue>.Enumerator GetEnumerator() => _values.GetEnumerator();
        IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => _values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
    }
}
