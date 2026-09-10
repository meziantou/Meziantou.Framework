using System.Runtime.InteropServices;

namespace Meziantou.Framework.Language;

/// <summary>Represents a contiguous character range in text.</summary>
/// <example>
/// <code>
/// var span = new TextSpan(start: 5, length: 3);
/// var sameSpan = TextSpan.FromBounds(5, 8);
/// </code>
/// </example>
[StructLayout(LayoutKind.Auto)]
public readonly struct TextSpan : IEquatable<TextSpan>, IComparable<TextSpan>
{
    public TextSpan(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        Start = start;
        Length = length;
    }

    public int Start { get; }
    public int Length { get; }
    public int End => Start + Length;

    /// <summary>Gets a value indicating whether the span is zero-length.</summary>
    public bool IsEmpty => Length == 0;

    public static TextSpan FromBounds(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);

        return new TextSpan(start, end - start);
    }

    /// <summary>Determines whether the span contains <paramref name="position"/>. The end of the span is exclusive.</summary>
    public bool Contains(int position) => (uint)(position - Start) < (uint)Length;

    /// <summary>Determines whether the span contains all of <paramref name="span"/>.</summary>
    public bool Contains(TextSpan span) => span.Start >= Start && span.End <= End;

    /// <summary>Determines whether the two spans share at least one character. Empty spans never overlap.</summary>
    public bool OverlapsWith(TextSpan span) => Math.Max(Start, span.Start) < Math.Min(End, span.End);

    /// <summary>Determines whether the two spans share at least one character or touch at an endpoint.</summary>
    public bool IntersectsWith(TextSpan span) => span.Start <= End && span.End >= Start;

    /// <summary>Determines whether <paramref name="position"/> is inside the span or at its end.</summary>
    public bool IntersectsWith(int position) => (uint)(position - Start) <= (uint)Length;

    /// <summary>Returns the overlap of the two spans, or <see langword="null"/> when they do not overlap.</summary>
    public TextSpan? Overlap(TextSpan span)
    {
        var start = Math.Max(Start, span.Start);
        var end = Math.Min(End, span.End);

        return start < end ? FromBounds(start, end) : null;
    }

    /// <summary>Returns the intersection of the two spans, or <see langword="null"/> when they do not intersect. Two spans that only touch intersect in an empty span.</summary>
    public TextSpan? Intersection(TextSpan span)
    {
        var start = Math.Max(Start, span.Start);
        var end = Math.Min(End, span.End);

        return start <= end ? FromBounds(start, end) : null;
    }

    /// <summary>Orders spans by <see cref="Start"/>, then by <see cref="Length"/>.</summary>
    public int CompareTo(TextSpan other)
    {
        var result = Start.CompareTo(other.Start);
        if (result != 0)
            return result;

        return Length.CompareTo(other.Length);
    }

    public bool Equals(TextSpan other) => Start == other.Start && Length == other.Length;
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TextSpan other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Start, Length);
    public static bool operator ==(TextSpan left, TextSpan right) => left.Equals(right);
    public static bool operator !=(TextSpan left, TextSpan right) => !left.Equals(right);
    public static bool operator <(TextSpan left, TextSpan right) => left.CompareTo(right) < 0;
    public static bool operator <=(TextSpan left, TextSpan right) => left.CompareTo(right) <= 0;
    public static bool operator >(TextSpan left, TextSpan right) => left.CompareTo(right) > 0;
    public static bool operator >=(TextSpan left, TextSpan right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"[{Start}..{End})";
}
