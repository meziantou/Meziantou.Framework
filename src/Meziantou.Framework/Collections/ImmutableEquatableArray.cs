using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Collections;

[DebuggerDisplay("Length = {Length}")]
[DebuggerTypeProxy(typeof(ImmutableEquatableArray<>.DebugView))]
[CollectionBuilder(typeof(ImmutableEquatableArray), nameof(ImmutableEquatableArray.Create))]
public sealed class ImmutableEquatableArray<T> : IEquatable<ImmutableEquatableArray<T>>, IReadOnlyList<T>, IList<T>, IList
    where T : IEquatable<T>
{
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types")]
    public static ImmutableEquatableArray<T> Empty { get; } = new([]);

    private readonly T[] _values;
    public ref readonly T this[int index] => ref _values[index];
    public int Length => _values.Length;

    internal ImmutableEquatableArray(T[] values) => _values = values;
    public bool Equals(ImmutableEquatableArray<T>? other)
    {
        if (other is null)
            return false;

        return ReferenceEquals(this, other) || ((ReadOnlySpan<T>)_values).SequenceEqual(other._values);
    }

    public override bool Equals(object? obj) => obj is ImmutableEquatableArray<T> other && Equals(other);

    public override int GetHashCode() => _values.Length;

    public Enumerator GetEnumerator() => new(_values);

    public static bool operator ==(ImmutableEquatableArray<T> left, ImmutableEquatableArray<T> right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        return left.Equals(right);
    }

    public static bool operator !=(ImmutableEquatableArray<T> left, ImmutableEquatableArray<T> right)
    {
        if (ReferenceEquals(left, right))
            return false;
        if (left is null || right is null)
            return true;
        return !left.Equals(right);
    }

    public struct Enumerator
    {
        private readonly T[] _values;
        private int _index;

        internal Enumerator(T[] values)
        {
            _values = values;
            _index = -1;
        }

        public bool MoveNext() => ++_index < _values.Length;
        public readonly ref T Current => ref _values[_index];
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)_values).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)_values).GetEnumerator();
    bool ICollection<T>.IsReadOnly => true;
    bool IList.IsFixedSize => true;
    bool IList.IsReadOnly => true;
    T IReadOnlyList<T>.this[int index] => _values[index];
    T IList<T>.this[int index] { get => _values[index]; set => throw new InvalidOperationException(); }
    object? IList.this[int index] { get => _values[index]; set => throw new InvalidOperationException(); }
    void ICollection<T>.CopyTo(T[] array, int arrayIndex) => _values.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => _values.CopyTo(array, index);
    int IList<T>.IndexOf(T item) => _values.AsSpan().IndexOf(item);
    int IList.IndexOf(object? value) => ((IList)_values).IndexOf(value);
    bool ICollection<T>.Contains(T item) => _values.AsSpan().IndexOf(item) >= 0;
    bool IList.Contains(object? value) => ((IList)_values).Contains(value);
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    int IReadOnlyCollection<T>.Count => Length;
    int ICollection<T>.Count => Length;
    int ICollection.Count => Length;

    void ICollection<T>.Add(T item) => throw new InvalidOperationException();
    bool ICollection<T>.Remove(T item) => throw new InvalidOperationException();
    void ICollection<T>.Clear() => throw new InvalidOperationException();
    void IList<T>.Insert(int index, T item) => throw new InvalidOperationException();
    void IList<T>.RemoveAt(int index) => throw new InvalidOperationException();
    int IList.Add(object? value) => throw new InvalidOperationException();
    void IList.Clear() => throw new InvalidOperationException();
    void IList.Insert(int index, object? value) => throw new InvalidOperationException();
    void IList.Remove(object? value) => throw new InvalidOperationException();
    void IList.RemoveAt(int index) => throw new InvalidOperationException();

    private sealed class DebugView(ImmutableEquatableArray<T> array)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public T[] Items => [.. array];
    }
}

public static class ImmutableEquatableArray
{
    public static ImmutableEquatableArray<T> ToImmutableEquatableArray<T>(this IEnumerable<T> values) where T : IEquatable<T>
        => values is ICollection<T> { Count: 0 } ? ImmutableEquatableArray<T>.Empty : new ImmutableEquatableArray<T>(values.ToArray());

    public static ImmutableEquatableArray<T> Create<T>(ReadOnlySpan<T> values) where T : IEquatable<T>
        => values.IsEmpty ? ImmutableEquatableArray<T>.Empty : new ImmutableEquatableArray<T>(values.ToArray());

    public static ImmutableEquatableArray<T> Create<T>(ImmutableArray<T> values) where T : IEquatable<T>
        => values.IsEmpty ? ImmutableEquatableArray<T>.Empty : new ImmutableEquatableArray<T>(ImmutableCollectionsMarshal.AsArray(values) ?? []);
}