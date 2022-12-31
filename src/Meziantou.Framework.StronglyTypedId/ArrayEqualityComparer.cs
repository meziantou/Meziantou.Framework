using System.Runtime.CompilerServices;

namespace Meziantou.Framework.StronglyTypedId;

internal sealed class ArrayEqualityComparer<T> : IEqualityComparer<T[]>
{
    public static IEqualityComparer<T[]> Default { get; } = new ArrayEqualityComparer<T>();

    private readonly IEqualityComparer<T> _elementComparer;

    public ArrayEqualityComparer(IEqualityComparer<T>? elementComparer = null)
    {
        _elementComparer = elementComparer ?? EqualityComparer<T>.Default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(T[] x, T[]? y)
    {
        if (x is null)
            return y is null;
        if (y is null)
            return false;

        return SequenceEquals(x, y);
    }

    private bool SequenceEquals(T[] first, T[] second)
    {
        if (first.Length != second.Length)
            return false;

        for (var i = 0; i < first.Length; i++)
        {
            if (!_elementComparer.Equals(first[i], second[i]))
                return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetHashCode([DisallowNull] T[] obj) => obj switch
    {
        null => 0,
        _ => 1 + obj.Length,
    };

    // Equals method for the comparer itself.
    public override bool Equals([NotNullWhen(true)] object? obj) =>
        obj is not null &&
        obj is ArrayEqualityComparer<T> other &&
        _elementComparer == other._elementComparer;

    public override int GetHashCode() => GetType().GetHashCode() ^ _elementComparer.GetHashCode();
}