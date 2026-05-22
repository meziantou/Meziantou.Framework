using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Collections;

public sealed class MultiValueDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, IReadOnlyCollection<TValue>>
    where TKey : notnull
{
    private readonly Dictionary<TKey, ValueCollection> _dictionary;

    public MultiValueDictionary()
    {
        _dictionary = [];
    }

    public MultiValueDictionary(int capacity)
    {
        _dictionary = new(capacity);
    }

    public MultiValueDictionary(IEqualityComparer<TKey>? comparer)
    {
        _dictionary = new(comparer);
    }

    public MultiValueDictionary(int capacity, IEqualityComparer<TKey>? comparer)
    {
        _dictionary = new(capacity, comparer);
    }

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

    public void Add(TKey key, TValue value)
    {
        if (!_dictionary.TryGetValue(key, out var collection))
        {
            collection = new();
            _dictionary.Add(key, collection);
        }

        collection.Add(value);
    }

    public void AddRange(TKey key, IEnumerable<TValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (!_dictionary.TryGetValue(key, out var collection))
        {
            collection = new();
            _dictionary.Add(key, collection);
        }

        collection.AddRange(values);
    }

    public bool Remove(TKey key)
    {
        return _dictionary.Remove(key);
    }

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

    public bool Contains(TKey key, TValue value)
    {
        return _dictionary.TryGetValue(key, out var collection) && collection.Contains(value);
    }

    public bool ContainsValue(TValue value)
    {
        foreach (var collection in _dictionary.Values)
        {
            if (collection.Contains(value))
                return true;
        }

        return false;
    }

    public void Clear()
    {
        _dictionary.Clear();
    }

    public bool ContainsKey(TKey key)
    {
        return _dictionary.ContainsKey(key);
    }

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

    IEnumerator<KeyValuePair<TKey, IReadOnlyCollection<TValue>>> IEnumerable<KeyValuePair<TKey, IReadOnlyCollection<TValue>>>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private sealed class ValueCollection : IReadOnlyCollection<TValue>
    {
        private readonly List<TValue> _values = [];

        public int Count => _values.Count;

        internal void Add(TValue value)
        {
            _values.Add(value);
        }

        internal void AddRange(IEnumerable<TValue> values)
        {
            _values.AddRange(values);
        }

        internal bool Remove(TValue value)
        {
            return _values.Remove(value);
        }

        internal bool Contains(TValue value)
        {
            return _values.Contains(value);
        }

        public List<TValue>.Enumerator GetEnumerator()
        {
            return _values.GetEnumerator();
        }

        IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
        {
            return _values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _values.GetEnumerator();
        }
    }
}
