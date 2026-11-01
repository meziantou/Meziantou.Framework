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
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Tests/RangeTests.cs" ref:region="Range_IsInRangeInclusive" ref:dedent="true">
/// var range = new Range&lt;int&gt;(1, 10);
/// Assert.True(range.IsInRangeInclusive(5));
/// Assert.False(range.IsInRangeInclusive(15));
/// </code>
/// </example>
[StructLayout(LayoutKind.Auto)]
public readonly struct Range<T> : IEquatable<Range<T>>
    where T : IComparable<T>
{
    /// <summary>Initializes a new instance of the <see cref="Range{T}"/> struct.</summary>
    /// <exception cref="ArgumentException"><paramref name="from"/> is greater than <paramref name="to"/>.</exception>
    public Range(T from, T to)
    {
        From = from;
        To = to;
        if (IsGreaterThan(from, to))
            throw new ArgumentException($"'{nameof(from)}' must be less than or equal to '{nameof(to)}'.", nameof(from));
    }

    private static bool IsGreaterThan(T left, T right) => left is not null && right is not null && left.CompareTo(right) > 0;

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

    /// <summary>Determines whether this range and the specified range have at least one value in common, inclusive of boundaries.</summary>
    /// <remarks>A <see langword="null"/> bound is open: it is lower or greater than any value.</remarks>
    public bool Overlaps(Range<T> range)
    {
        if (From is not null && range.To is not null && From.CompareTo(range.To) > 0)
            return false;

        if (range.From is not null && To is not null && range.From.CompareTo(To) > 0)
            return false;

        return true;
    }

    /// <summary>Returns the values that are in both this range and the specified range, inclusive of boundaries.</summary>
    /// <returns>The intersection of both ranges, or <see langword="null"/> when they do not overlap.</returns>
    /// <remarks>A <see langword="null"/> bound is open: it is lower or greater than any value.</remarks>
    public Range<T>? Intersect(Range<T> range)
    {
        if (!Overlaps(range))
            return null;

        var from = From is null || (range.From is not null && range.From.CompareTo(From) > 0) ? range.From : From;
        var to = To is null || (range.To is not null && range.To.CompareTo(To) < 0) ? range.To : To;
        return new Range<T>(from, to);
    }

    public bool Equals(Range<T> other)
    {
        // Both bounds are always compared: skipping the comparison when a bound is null made a null
        // bound match anything, which is neither symmetric nor consistent with GetHashCode.
        var comparer = EqualityComparer<T>.Default;
        return comparer.Equals(From, other.From) && comparer.Equals(To, other.To);
    }

    public override int GetHashCode() => HashCode.Combine(From, To);

    public override bool Equals([NotNullWhen(true)] object? obj)
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
