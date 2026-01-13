using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Collections;

[DebuggerDisplay("Count = {Count}")]
[DebuggerTypeProxy(typeof(ImmutableEquatableDictionary<,>.DebugView))]
public sealed class ImmutableEquatableDictionary<TKey, TValue> : IEquatable<ImmutableEquatableDictionary<TKey, TValue>>, IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, IDictionary
    where TKey : IEquatable<TKey>
    where TValue : IEquatable<TValue>
{
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types")]
    public static ImmutableEquatableDictionary<TKey, TValue> Empty { get; } = new([]);

    private readonly Dictionary<TKey, TValue> _values;

    internal ImmutableEquatableDictionary(Dictionary<TKey, TValue> values)
    {
        Debug.Assert(values.Comparer == EqualityComparer<TKey>.Default);
        _values = values;
    }

    public int Count => _values.Count;
    public bool ContainsKey(TKey key) => _values.ContainsKey(key);
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => _values.TryGetValue(key, out value);
    public TValue this[TKey key] => _values[key];

    public bool Equals(ImmutableEquatableDictionary<TKey, TValue>? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        var thisDict = _values;
        var otherDict = other._values;
        if (thisDict.Count != otherDict.Count)
            return false;

        foreach (var entry in thisDict)
        {
            if (!otherDict.TryGetValue(entry.Key, out var otherValue) || !entry.Value.Equals(otherValue))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is ImmutableEquatableDictionary<TKey, TValue> other && Equals(other);

    public override int GetHashCode() => _values.Count;

    public static bool operator ==(ImmutableEquatableDictionary<TKey, TValue> left, ImmutableEquatableDictionary<TKey, TValue> right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        return left.Equals(right);
    }

    public static bool operator !=(ImmutableEquatableDictionary<TKey, TValue> left, ImmutableEquatableDictionary<TKey, TValue> right)
    {
        if (ReferenceEquals(left, right))
            return false;
        if (left is null || right is null)
            return true;
        return !left.Equals(right);
    }

    public Dictionary<TKey, TValue>.Enumerator GetEnumerator() => _values.GetEnumerator();
    public Dictionary<TKey, TValue>.KeyCollection Keys => _values.Keys;
    public Dictionary<TKey, TValue>.ValueCollection Values => _values.Values;

    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
    IDictionaryEnumerator IDictionary.GetEnumerator() => ((IDictionary)_values).GetEnumerator();
    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => _values.Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => _values.Values;
    ICollection<TKey> IDictionary<TKey, TValue>.Keys => _values.Keys;
    ICollection<TValue> IDictionary<TKey, TValue>.Values => _values.Values;
    ICollection IDictionary.Keys => _values.Keys;
    ICollection IDictionary.Values => _values.Values;

    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => true;
    bool IDictionary.IsReadOnly => true;
    bool IDictionary.IsFixedSize => true;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    TValue IDictionary<TKey, TValue>.this[TKey key] { get => _values[key]; set => throw new InvalidOperationException(); }
    object? IDictionary.this[object key] { get => ((IDictionary)_values)[key]; set => throw new InvalidOperationException(); }
    bool IDictionary.Contains(object key) => ((IDictionary)_values).Contains(key);
    void ICollection.CopyTo(Array array, int index) => ((IDictionary)_values).CopyTo(array, index);
    bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) => _values.Contains(item);
    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        => ((ICollection<KeyValuePair<TKey, TValue>>)_values).CopyTo(array, arrayIndex);

    void IDictionary<TKey, TValue>.Add(TKey key, TValue value) => throw new InvalidOperationException();
    bool IDictionary<TKey, TValue>.Remove(TKey key) => throw new InvalidOperationException();
    void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => throw new InvalidOperationException();
    bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) => throw new InvalidOperationException();
    void ICollection<KeyValuePair<TKey, TValue>>.Clear() => throw new InvalidOperationException();
    void IDictionary.Add(object key, object? value) => throw new InvalidOperationException();
    void IDictionary.Remove(object key) => throw new InvalidOperationException();
    void IDictionary.Clear() => throw new InvalidOperationException();

    private sealed class DebugView(ImmutableEquatableDictionary<TKey, TValue> collection)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public DebugViewDictionaryItem[] Entries => [.. collection._values.Select(kvp => new DebugViewDictionaryItem(kvp))];
    }

    [DebuggerDisplay("{Value}", Name = "[{Key}]")]
    [StructLayout(LayoutKind.Auto)]
    private readonly struct DebugViewDictionaryItem(KeyValuePair<TKey, TValue> keyValuePair)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Collapsed)]
        public TKey Key { get; } = keyValuePair.Key;

        [DebuggerBrowsable(DebuggerBrowsableState.Collapsed)]
        public TValue Value { get; } = keyValuePair.Value;
    }
}

public static class ImmutableEquatableDictionary
{
    public static ImmutableEquatableDictionary<TKey, TValue> Empty<TKey, TValue>()
        where TKey : IEquatable<TKey>
        where TValue : IEquatable<TValue>
        => ImmutableEquatableDictionary<TKey, TValue>.Empty;

    public static ImmutableEquatableDictionary<TKey, TValue> ToImmutableEquatableDictionary<TKey, TValue>(this IEnumerable<TValue> values, Func<TValue, TKey> keySelector)
        where TKey : IEquatable<TKey>
        where TValue : IEquatable<TValue>
    {
        return values is ICollection<KeyValuePair<TKey, TValue>> { Count: 0 }
            ? ImmutableEquatableDictionary<TKey, TValue>.Empty
            : new ImmutableEquatableDictionary<TKey, TValue>(values.ToDictionary(keySelector));
    }

    public static ImmutableEquatableDictionary<TKey, TValue> ToImmutableEquatableDictionary<TSource, TKey, TValue>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TValue> valueSelector)
        where TKey : IEquatable<TKey>
        where TValue : IEquatable<TValue>
    {
        return source is ICollection<TSource> { Count: 0 }
            ? ImmutableEquatableDictionary<TKey, TValue>.Empty
            : new ImmutableEquatableDictionary<TKey, TValue>(source.ToDictionary(keySelector, valueSelector));
    }

    public static ImmutableEquatableDictionary<TKey, TValue> ToImmutableEquatableDictionary<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> values)
        where TKey : IEquatable<TKey>
        where TValue : IEquatable<TValue>
    {
        return values switch
        {
            ICollection<KeyValuePair<TKey, TValue>> { Count: 0 } => ImmutableEquatableDictionary<TKey, TValue>.Empty,
            IDictionary<TKey, TValue> dict => new ImmutableEquatableDictionary<TKey, TValue>(new(dict)),
            _ => new ImmutableEquatableDictionary<TKey, TValue>(values.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)),
        };
    }

    public static ImmutableEquatableDictionary<TKey, TValue> ToImmutableEquatableDictionary<TKey, TValue>(this IEnumerable<(TKey, TValue)> values)
        where TKey : IEquatable<TKey>
        where TValue : IEquatable<TValue>
    {
        return values switch
        {
            ICollection<KeyValuePair<TKey, TValue>> { Count: 0 } => ImmutableEquatableDictionary<TKey, TValue>.Empty,
            IDictionary<TKey, TValue> dict => new ImmutableEquatableDictionary<TKey, TValue>(new(dict)),
            _ => new ImmutableEquatableDictionary<TKey, TValue>(values.ToDictionary(kvp => kvp.Item1, kvp => kvp.Item2)),
        };
    }
}