namespace Meziantou.Framework.Assertions;

/// <summary>Compares <see cref="NullableKey{T}"/> instances using the comparer supplied by the caller.</summary>
internal sealed class NullableKeyComparer<T>(IEqualityComparer<T> comparer) : IEqualityComparer<NullableKey<T>>
{
    /// <summary>
    /// Returns the comparer to pass to a dictionary of <see cref="NullableKey{T}"/>, or <see langword="null"/> when
    /// <paramref name="comparer"/> is the default one. A null comparer lets the dictionary use
    /// <see cref="EqualityComparer{T}.Default"/>, which the JIT devirtualizes.
    /// </summary>
    public static IEqualityComparer<NullableKey<T>>? Create(IEqualityComparer<T> comparer)
    {
        return ReferenceEquals(comparer, EqualityComparer<T>.Default) ? null : new NullableKeyComparer<T>(comparer);
    }

    public bool Equals(NullableKey<T> x, NullableKey<T> y) => comparer.Equals(x.Value, y.Value);

    public int GetHashCode(NullableKey<T> obj) => obj.Value is null ? 0 : comparer.GetHashCode(obj.Value);
}
