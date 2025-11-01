using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>
/// Provides factory methods for creating range instances.
/// </summary>
public static class Range
{
    /// <summary>
    /// Creates a range from the specified start and end values.
    /// </summary>
    /// <typeparam name="T">The type of the range values.</typeparam>
    /// <param name="from">The start of the range.</param>
    /// <param name="to">The end of the range.</param>
    /// <returns>A new range instance.</returns>
    public static Range<T> Create<T>(T from, T to) where T : IComparable<T>
    {
        return new Range<T>(from, to);
    }
}

/// <summary>
/// Represents a range of values with a start and end point.
/// </summary>
/// <typeparam name="T">The type of the range values, which must be comparable.</typeparam>
[StructLayout(LayoutKind.Auto)]
public readonly struct Range<T> : IEquatable<Range<T>>
    where T : IComparable<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Range{T}"/> struct.
    /// </summary>
    /// <param name="from">The start of the range.</param>
    /// <param name="to">The end of the range.</param>
    public Range(T from, T to)
    {
        From = from;
        To = to;
    }

    /// <summary>
    /// Gets the start of the range.
    /// </summary>
    public T From { get; }

    /// <summary>
    /// Gets the end of the range.
    /// </summary>
    public T To { get; }

    /// <summary>
    /// Determines whether the specified value is within the range (inclusive of both boundaries).
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is within the range; otherwise, <see langword="false"/>.</returns>
    public bool IsInRangeInclusive(T value)
    {
        if (From is not null && From.CompareTo(value) > 0)
            return false;

        if (To is not null && To.CompareTo(value) < 0)
            return false;

        return true;
    }

    /// <summary>
    /// Determines whether the specified value is within the range (exclusive of both boundaries).
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is within the range; otherwise, <see langword="false"/>.</returns>
    public bool IsInRangeExclusive(T value)
    {
        if (From is not null && From.CompareTo(value) >= 0)
            return false;

        if (To is not null && To.CompareTo(value) <= 0)
            return false;

        return true;
    }

    /// <summary>
    /// Determines whether the specified value is within the range (inclusive of lower boundary, exclusive of upper boundary).
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is within the range; otherwise, <see langword="false"/>.</returns>
    public bool IsInRangeLowerInclusive(T value)
    {
        if (From is not null && From.CompareTo(value) > 0)
            return false;

        if (To is not null && To.CompareTo(value) <= 0)
            return false;

        return true;
    }

    /// <summary>
    /// Determines whether the specified value is within the range (exclusive of lower boundary, inclusive of upper boundary).
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is within the range; otherwise, <see langword="false"/>.</returns>
    public bool IsInRangeUpperInclusive(T value)
    {
        if (From is not null && From.CompareTo(value) >= 0)
            return false;

        if (To is not null && To.CompareTo(value) < 0)
            return false;

        return true;
    }

    /// <summary>
    /// Determines whether the specified range is entirely within this range (inclusive of both boundaries).
    /// </summary>
    /// <param name="range">The range to check.</param>
    /// <returns><see langword="true"/> if the range is within this range; otherwise, <see langword="false"/>.</returns>
    public bool IsInRangeInclusive(Range<T> range)
    {
        return IsInRangeInclusive(range.From) && IsInRangeInclusive(range.To);
    }

    /// <summary>
    /// Determines whether the specified range is entirely within this range (exclusive of both boundaries).
    /// </summary>
    /// <param name="range">The range to check.</param>
    /// <returns><see langword="true"/> if the range is within this range; otherwise, <see langword="false"/>.</returns>
    public bool IsInRangeExclusive(Range<T> range)
    {
        return IsInRangeExclusive(range.From) && IsInRangeExclusive(range.To);
    }

    /// <summary>
    /// Determines whether the specified range is entirely within this range (inclusive of lower boundary, exclusive of upper boundary).
    /// </summary>
    /// <param name="range">The range to check.</param>
    /// <returns><see langword="true"/> if the range is within this range; otherwise, <see langword="false"/>.</returns>
    public bool IsInRangeLowerInclusive(Range<T> range)
    {
        return IsInRangeLowerInclusive(range.From) && IsInRangeLowerInclusive(range.To);
    }

    /// <summary>
    /// Determines whether the specified range is entirely within this range (exclusive of lower boundary, inclusive of upper boundary).
    /// </summary>
    /// <param name="range">The range to check.</param>
    /// <returns><see langword="true"/> if the range is within this range; otherwise, <see langword="false"/>.</returns>
    public bool IsInRangeUpperInclusive(Range<T> range)
    {
        return IsInRangeUpperInclusive(range.From) && IsInRangeUpperInclusive(range.To);
    }

    /// <summary>
    /// Determines whether this range is equal to another range.
    /// </summary>
    /// <param name="other">The range to compare with.</param>
    /// <returns><see langword="true"/> if the ranges are equal; otherwise, <see langword="false"/>.</returns>
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

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(From, To);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is Range<T> range && Equals(range);
    }

    /// <inheritdoc/>
    public override string? ToString()
    {
        return $"Range {From}-{To}";
    }

    /// <summary>
    /// Determines whether two ranges are equal.
    /// </summary>
    /// <param name="left">The first range.</param>
    /// <param name="right">The second range.</param>
    /// <returns><see langword="true"/> if the ranges are equal; otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(Range<T> left, Range<T> right) => left.Equals(right);

    /// <summary>
    /// Determines whether two ranges are not equal.
    /// </summary>
    /// <param name="left">The first range.</param>
    /// <param name="right">The second range.</param>
    /// <returns><see langword="true"/> if the ranges are not equal; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(Range<T> left, Range<T> right) => !(left == right);
}
