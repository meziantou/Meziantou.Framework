namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="HashCode"/> to efficiently add multiple values.
/// </summary>
/// <example>
/// <code>
/// var hash = new HashCode();
/// hash.AddValues(new[] { 1, 2, 3 });
/// int hashValue = hash.ToHashCode();
/// </code>
/// </example>
public static class HashCodeExtensions
{
    /// <summary>Adds all values from an array to the hash code.</summary>
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

    /// <summary>Adds all values from a span to the hash code.</summary>
    public static void AddValues<T>(this HashCode hashCode, ReadOnlySpan<T> values, IEqualityComparer<T>? equalityComparer = null)
    {
        foreach (var value in values)
        {
            hashCode.Add(value, equalityComparer);
        }
    }

    public static void AddValues<T>(this HashCode hashCode, IEnumerable<T> values, IEqualityComparer<T>? equalityComparer = null)
    {
        foreach (var value in values)
        {
            hashCode.Add(value, equalityComparer);
        }
    }
}
