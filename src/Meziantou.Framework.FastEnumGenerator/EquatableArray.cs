using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.FastEnumGenerator;

/// <summary>
/// An <see cref="ImmutableArray{T}"/> with structural equality. <see cref="ImmutableArray{T}"/> compares
/// the backing array by reference, which makes any incremental generator model containing one compare
/// unequal on every run and defeats the generator's caching.
/// </summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> values) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _values = values;

    public int Length => _values.IsDefault ? 0 : _values.Length;

    public T this[int index] => _values[index];

    public ImmutableArray<T> AsImmutableArray() => _values.IsDefault ? ImmutableArray<T>.Empty : _values;

    public bool Equals(EquatableArray<T> other)
    {
        if (_values.IsDefault || other._values.IsDefault)
            return _values.IsDefault && other._values.IsDefault;

        if (_values.Length != other._values.Length)
            return false;

        for (var i = 0; i < _values.Length; i++)
        {
            if (!_values[i].Equals(other._values[i]))
                return false;
        }

        return true;
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_values.IsDefault)
            return 0;

        var hash = 17;
        foreach (var value in _values)
        {
            hash = unchecked((hash * 31) + (value?.GetHashCode() ?? 0));
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)AsImmutableArray()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}
