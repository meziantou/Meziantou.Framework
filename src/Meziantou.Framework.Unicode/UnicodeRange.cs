using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>Represents a range of Unicode code points.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct UnicodeRange : IEquatable<UnicodeRange>
{
    /// <summary>Gets the first code point in the range (inclusive).</summary>
    public int Start { get; }

    /// <summary>Gets the last code point in the range (inclusive).</summary>
    public int End { get; }

    /// <summary>Gets the number of code points in the range.</summary>
    public int Length => End - Start + 1;

    /// <summary>Initializes a new instance of the <see cref="UnicodeRange"/> struct.</summary>
    /// <param name="start">The first code point in the range (inclusive).</param>
    /// <param name="end">The last code point in the range (inclusive).</param>
    public UnicodeRange(int start, int end)
    {
        if (start < 0 || start > 0x10FFFF)
            throw new ArgumentOutOfRangeException(nameof(start));

        if (end < 0 || end > 0x10FFFF)
            throw new ArgumentOutOfRangeException(nameof(end));

        if (start > end)
            throw new ArgumentException("Start must be less than or equal to end.", nameof(end));

        Start = start;
        End = end;
    }

    /// <summary>Determines whether the specified code point is in this range.</summary>
    /// <param name="codePoint">The code point to check.</param>
    /// <returns><see langword="true"/> if the code point is in the range; otherwise, <see langword="false"/>.</returns>
    public bool Contains(int codePoint) => codePoint >= Start && codePoint <= End;

    /// <summary>Determines whether the specified rune is in this range.</summary>
    /// <param name="rune">The rune to check.</param>
    /// <returns><see langword="true"/> if the rune is in the range; otherwise, <see langword="false"/>.</returns>
    public bool Contains(Rune rune) => Contains(rune.Value);

    public bool Equals(UnicodeRange other) => Start == other.Start && End == other.End;

    public override bool Equals(object? obj) => obj is UnicodeRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Start, End);

    public static bool operator ==(UnicodeRange left, UnicodeRange right) => left.Equals(right);

    public static bool operator !=(UnicodeRange left, UnicodeRange right) => !left.Equals(right);

    public override string ToString() => $"U+{Start:X4}..U+{End:X4}";
}
