namespace Meziantou.Framework.Templating;

/// <summary>Represents a span in text, from a start position to an end position (exclusive).</summary>
public readonly struct TextSpan : IEquatable<TextSpan>, IComparable<TextSpan>, IComparable
{
    public TextSpan(TextPosition start, TextPosition end)
    {
        if (end < start)
            throw new ArgumentOutOfRangeException(nameof(end), "End position must be greater than or equal to start position.");

        Start = start;
        End = end;
    }

    /// <summary>Gets the inclusive start position of the span.</summary>
    public TextPosition Start { get; }

    /// <summary>Gets the exclusive end position of the span.</summary>
    public TextPosition End { get; }

    /// <summary>Gets the span length in characters.</summary>
    public int Length => End.Index - Start.Index;

    public bool Equals(TextSpan other)
    {
        return Start.Equals(other.Start) && End.Equals(other.End);
    }

    public override bool Equals(object? obj)
    {
        return obj is TextSpan other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Start, End);
    }

    public int CompareTo(TextSpan other)
    {
        var result = Start.CompareTo(other.Start);
        if (result != 0)
            return result;

        return End.CompareTo(other.End);
    }

    public int CompareTo(object? obj)
    {
        if (obj is null)
            return 1;

        if (obj is TextSpan other)
            return CompareTo(other);

        throw new ArgumentException($"Object must be of type {nameof(TextSpan)}.", nameof(obj));
    }

    public override string ToString()
    {
        return $"{Start}..{End}";
    }

    public static bool operator ==(TextSpan left, TextSpan right) => left.Equals(right);
    public static bool operator !=(TextSpan left, TextSpan right) => !left.Equals(right);
    public static bool operator <(TextSpan left, TextSpan right) => left.CompareTo(right) < 0;
    public static bool operator <=(TextSpan left, TextSpan right) => left.CompareTo(right) <= 0;
    public static bool operator >(TextSpan left, TextSpan right) => left.CompareTo(right) > 0;
    public static bool operator >=(TextSpan left, TextSpan right) => left.CompareTo(right) >= 0;
}
