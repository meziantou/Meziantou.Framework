namespace Meziantou.Framework;
public static class HashCodeExtensions
{
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
