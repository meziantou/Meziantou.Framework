using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>Provides factory methods for creating typed range instances.</summary>
public static class Range
{
    /// <summary>Creates a range with the specified start and end values.</summary>
    public static Range<T> Create<T>(T from, T to) where T : IComparable<T>
    {
        return new Range<T>(from, to);
    }
}

/// <summary>Represents a range of comparable values with support for various inclusion/exclusion boundary checks.</summary>
/// <example>
/// <code>
/// var range = new Range&lt;int&gt;(1, 10);
/// bool isInRange = range.IsInRangeInclusive(5); // true
/// bool isOutOfRange = range.IsInRangeInclusive(15); // false
/// </code>
/// </example>
[StructLayout(LayoutKind.Auto)]
public readonly struct Range<T> : IEquatable<Range<T>>
    where T : IComparable<T>
{
    public Range(T from, T to)
    {
        From = from;
        To = to;
    }

    /// <summary>Gets the start value of the range.</summary>
    public T From { get; }

    /// <summary>Gets the end value of the range.</summary>
    public T To { get; }

    /// <summary>Determines whether the specified value is within this range, inclusive of boundaries.</summary>
    public bool IsInRangeInclusive(T value)
    {
        if (From is not null && From.CompareTo(value) > 0)
            return false;

        if (To is not null && To.CompareTo(value) < 0)
            return false;

        return true;
    }

    /// <summary>Determines whether the specified value is within this range, exclusive of boundaries.</summary>
    public bool IsInRangeExclusive(T value)
    {
        if (From is not null && From.CompareTo(value) >= 0)
            return false;

        if (To is not null && To.CompareTo(value) <= 0)
            return false;

        return true;
    }

    /// <summary>Determines whether the specified value is within this range, inclusive of the lower bound and exclusive of the upper bound.</summary>
    public bool IsInRangeLowerInclusive(T value)
    {
        if (From is not null && From.CompareTo(value) > 0)
            return false;

        if (To is not null && To.CompareTo(value) <= 0)
            return false;

        return true;
    }

    /// <summary>Determines whether the specified value is within this range, exclusive of the lower bound and inclusive of the upper bound.</summary>
    public bool IsInRangeUpperInclusive(T value)
    {
        if (From is not null && From.CompareTo(value) >= 0)
            return false;

        if (To is not null && To.CompareTo(value) < 0)
            return false;

        return true;
    }

    /// <summary>Determines whether the specified range is fully contained within this range, inclusive of boundaries.</summary>
    public bool IsInRangeInclusive(Range<T> range)
    {
        return IsInRangeInclusive(range.From) && IsInRangeInclusive(range.To);
    }

    /// <summary>Determines whether the specified range is fully contained within this range, exclusive of boundaries.</summary>
    public bool IsInRangeExclusive(Range<T> range)
    {
        return IsInRangeExclusive(range.From) && IsInRangeExclusive(range.To);
    }

    /// <summary>Determines whether the specified range is fully contained within this range, inclusive of the lower bound and exclusive of the upper bound.</summary>
    public bool IsInRangeLowerInclusive(Range<T> range)
    {
        return IsInRangeLowerInclusive(range.From) && IsInRangeLowerInclusive(range.To);
    }

    /// <summary>Determines whether the specified range is fully contained within this range, exclusive of the lower bound and inclusive of the upper bound.</summary>
    public bool IsInRangeUpperInclusive(Range<T> range)
    {
        return IsInRangeUpperInclusive(range.From) && IsInRangeUpperInclusive(range.To);
    }

    public bool Equals(Range<T> other)
    {
        if (From is not null)
        {
            if (From.CompareTo(other.From) != 0)
                return false;

            if (other.From is not null && other.From.CompareTo(From) != 0)
                return false;
        }

        if (To is not null)
        {
            if (To.CompareTo(other.To) != 0)
                return false;

            if (other.To is not null && other.To.CompareTo(To) != 0)
                return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(From, To);
    }

    public override bool Equals(object? obj)
    {
        return obj is Range<T> range && Equals(range);
    }

    public override string? ToString()
    {
        return $"Range {From}-{To}";
    }

    public static bool operator ==(Range<T> left, Range<T> right) => left.Equals(right);
    public static bool operator !=(Range<T> left, Range<T> right) => !(left == right);
}
