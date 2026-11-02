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
    private readonly IEqualityComparer<TKey> _comparer;

    public UnsafeListDictionary()
        : this(comparer: null)
    {
    }

    public UnsafeListDictionary(int capacity)
        : this(capacity, comparer: null)
    {
    }

    public UnsafeListDictionary(IEnumerable<KeyValuePair<TKey, TValue>> items)
        : this(items, comparer: null)
    {
    }

    public UnsafeListDictionary(IEqualityComparer<TKey>? comparer)
    {
        _items = [];
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
    }

    public UnsafeListDictionary(int capacity, IEqualityComparer<TKey>? comparer)
    {
        _items = new(capacity);
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
    }

    public UnsafeListDictionary(IEnumerable<KeyValuePair<TKey, TValue>> items, IEqualityComparer<TKey>? comparer)
    {
        _items = new(items);
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
    }

    /// <summary>Gets the comparer used to compare keys.</summary>
    public IEqualityComparer<TKey> Comparer => _comparer;

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

    /// <summary>Gets a read-only view of the keys, in insertion order. The view reflects later changes to the dictionary.</summary>
    public KeyCollection Keys => new(this);

    /// <summary>Gets a read-only view of the values, in insertion order. The view reflects later changes to the dictionary.</summary>
    public ValueCollection Values => new(this);

    ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;
    ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;
    public int Count => _items.Count;
    public void Add(TKey key, TValue value) => _items.Add(new(key, value));
    public void Add(KeyValuePair<TKey, TValue> item) => _items.Add(item);
    public void AddRange(IEnumerable<KeyValuePair<TKey, TValue>> items) => _items.AddRange(items);

    public void Clear() => _items.Clear();
    public bool Contains(KeyValuePair<TKey, TValue> item) => _items.Contains(item);
    public bool ContainsKey(TKey key) => FindIndex(key) >= 0;

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
            if (_comparer.Equals(_items[i].Key, key))
                return i;
        }

        return -1;
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

    public sealed class KeyCollection : ICollection<TKey>, IReadOnlyCollection<TKey>
    {
        private readonly UnsafeListDictionary<TKey, TValue> _dictionary;

        internal KeyCollection(UnsafeListDictionary<TKey, TValue> dictionary) => _dictionary = dictionary;

        public int Count => _dictionary.Count;

        bool ICollection<TKey>.IsReadOnly => true;

        public bool Contains(TKey item) => _dictionary.ContainsKey(item);

        public void CopyTo(TKey[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(arrayIndex, array.Length);
            if (array.Length - arrayIndex < Count)
                throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.", nameof(array));

            foreach (var item in _dictionary._items)
            {
                array[arrayIndex++] = item.Key;
            }
        }

        public Enumerator GetEnumerator() => new(_dictionary._items.GetEnumerator());
        IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        void ICollection<TKey>.Add(TKey item) => throw new NotSupportedException("The collection is read-only.");
        void ICollection<TKey>.Clear() => throw new NotSupportedException("The collection is read-only.");
        bool ICollection<TKey>.Remove(TKey item) => throw new NotSupportedException("The collection is read-only.");

        public struct Enumerator : IEnumerator<TKey>
        {
            private List<KeyValuePair<TKey, TValue>>.Enumerator _enumerator;

            internal Enumerator(List<KeyValuePair<TKey, TValue>>.Enumerator enumerator) => _enumerator = enumerator;

            public readonly TKey Current => _enumerator.Current.Key;

            readonly object IEnumerator.Current => Current;

            public bool MoveNext() => _enumerator.MoveNext();

            void IEnumerator.Reset() => throw new NotSupportedException();

            public void Dispose() => _enumerator.Dispose();
        }
    }

    public sealed class ValueCollection : ICollection<TValue>, IReadOnlyCollection<TValue>
    {
        private readonly UnsafeListDictionary<TKey, TValue> _dictionary;

        internal ValueCollection(UnsafeListDictionary<TKey, TValue> dictionary) => _dictionary = dictionary;

        public int Count => _dictionary.Count;

        bool ICollection<TValue>.IsReadOnly => true;

        public bool Contains(TValue item)
        {
            var comparer = EqualityComparer<TValue>.Default;
            foreach (var pair in _dictionary._items)
            {
                if (comparer.Equals(pair.Value, item))
                    return true;
            }

            return false;
        }

        public void CopyTo(TValue[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(arrayIndex, array.Length);
            if (array.Length - arrayIndex < Count)
                throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.", nameof(array));

            foreach (var item in _dictionary._items)
            {
                array[arrayIndex++] = item.Value;
            }
        }

        public Enumerator GetEnumerator() => new(_dictionary._items.GetEnumerator());
        IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        void ICollection<TValue>.Add(TValue item) => throw new NotSupportedException("The collection is read-only.");
        void ICollection<TValue>.Clear() => throw new NotSupportedException("The collection is read-only.");
        bool ICollection<TValue>.Remove(TValue item) => throw new NotSupportedException("The collection is read-only.");

        public struct Enumerator : IEnumerator<TValue>
        {
            private List<KeyValuePair<TKey, TValue>>.Enumerator _enumerator;

            internal Enumerator(List<KeyValuePair<TKey, TValue>>.Enumerator enumerator) => _enumerator = enumerator;

            public readonly TValue Current => _enumerator.Current.Value;

            readonly object? IEnumerator.Current => Current;

            public bool MoveNext() => _enumerator.MoveNext();

            void IEnumerator.Reset() => throw new NotSupportedException();

            public void Dispose() => _enumerator.Dispose();
        }
    }
}
