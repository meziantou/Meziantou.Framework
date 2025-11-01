namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="HashCode"/>.
/// </summary>
public static class HashCodeExtensions
{
    /// <summary>
    /// Adds a sequence of values from an array to the hash code.
    /// </summary>
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <param name="hashCode">The hash code to add to.</param>
    /// <param name="values">The array of values to add, or <see langword="null"/> to add a zero hash.</param>
    /// <param name="equalityComparer">The equality comparer to use, or <see langword="null"/> to use the default comparer.</param>
    public static void AddValues<T>(this HashCode hashCode, T[] values, IEqualityComparer<T>? equalityComparer = null)
    {
        if (values is null)
        {
            hashCode.Add(0);
        }
        else
        {
            hashCode.AddValues(new ReadOnlySpan<T>(values), equalityComparer);
        }
    }

    /// <summary>
    /// Adds a sequence of values from a span to the hash code.
    /// </summary>
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <param name="hashCode">The hash code to add to.</param>
    /// <param name="values">The span of values to add.</param>
    /// <param name="equalityComparer">The equality comparer to use, or <see langword="null"/> to use the default comparer.</param>
    public static void AddValues<T>(this HashCode hashCode, ReadOnlySpan<T> values, IEqualityComparer<T>? equalityComparer = null)
    {
        foreach (var value in values)
        {
            hashCode.Add(value, equalityComparer);
        }
    }

    /// <summary>
    /// Adds a sequence of values from an enumerable to the hash code.
    /// </summary>
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <param name="hashCode">The hash code to add to.</param>
    /// <param name="values">The enumerable of values to add.</param>
    /// <param name="equalityComparer">The equality comparer to use, or <see langword="null"/> to use the default comparer.</param>
    public static void AddValues<T>(this HashCode hashCode, IEnumerable<T> values, IEqualityComparer<T>? equalityComparer = null)
    {
        foreach (var value in values)
        {
            hashCode.Add(value, equalityComparer);
        }
    }
}
