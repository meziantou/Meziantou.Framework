namespace Meziantou.Framework.Templating;

/// <summary>Represents a position in text using line, column, and absolute character index.</summary>
public readonly struct TextPosition : IEquatable<TextPosition>, IComparable<TextPosition>, IComparable
{
    public TextPosition(int line, int column, int index)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 0);

        Line = line;
        Column = column;
        Index = index;
    }

    /// <summary>Gets the 1-based line number.</summary>
    public int Line { get; }

    /// <summary>Gets the 1-based column number.</summary>
    public int Column { get; }

    /// <summary>Gets the 0-based character index from the start of the source text.</summary>
    public int Index { get; }

    public bool Equals(TextPosition other)
    {
        return Line == other.Line && Column == other.Column && Index == other.Index;
    }

    public override bool Equals(object? obj)
    {
        return obj is TextPosition other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Line, Column, Index);
    }

    public int CompareTo(TextPosition other)
    {
        var result = Index.CompareTo(other.Index);
        if (result != 0)
            return result;

        result = Line.CompareTo(other.Line);
        if (result != 0)
            return result;

        return Column.CompareTo(other.Column);
    }

    public int CompareTo(object? obj)
    {
        if (obj is null)
            return 1;

        if (obj is TextPosition other)
            return CompareTo(other);

        throw new ArgumentException($"Object must be of type {nameof(TextPosition)}.", nameof(obj));
    }

    public override string ToString()
    {
        return $"(L{Line}, C{Column}, I{Index})";
    }

    public static bool operator ==(TextPosition left, TextPosition right) => left.Equals(right);
    public static bool operator !=(TextPosition left, TextPosition right) => !left.Equals(right);
    public static bool operator <(TextPosition left, TextPosition right) => left.CompareTo(right) < 0;
    public static bool operator <=(TextPosition left, TextPosition right) => left.CompareTo(right) <= 0;
    public static bool operator >(TextPosition left, TextPosition right) => left.CompareTo(right) > 0;
    public static bool operator >=(TextPosition left, TextPosition right) => left.CompareTo(right) >= 0;
}
