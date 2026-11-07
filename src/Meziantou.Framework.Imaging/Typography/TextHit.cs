using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>The character of a <see cref="TextLayout"/> a point is on, or the nearest one: what <see cref="TextLayout.HitTest"/> finds.</summary>
/// <remarks>
/// A character is what a reader takes for one: a letter with its marks, an emoji sequence (a grapheme cluster). Its
/// leading edge is where it starts in the direction of its text: its left in a left-to-right text, its right in a
/// right-to-left one.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct TextHit : IEquatable<TextHit>
{
    internal TextHit(int lineIndex, int textIndex, int length, bool isTrailing, bool isInside)
    {
        LineIndex = lineIndex;
        TextIndex = textIndex;
        Length = length;
        IsTrailing = isTrailing;
        IsInside = isInside;
    }

    /// <summary>Gets the index of the line the point is on, or of the nearest line, in <see cref="TextLayout.Lines"/>.</summary>
    public int LineIndex { get; }

    /// <summary>Gets the index in the text of the first UTF-16 code unit of the character.</summary>
    public int TextIndex { get; }

    /// <summary>Gets the number of UTF-16 code units of the character: 0 on a line that has no character.</summary>
    public int Length { get; }

    /// <summary>Gets a value indicating whether the point is nearer to the end of the character than to its start, in the direction of its text.</summary>
    public bool IsTrailing { get; }

    /// <summary>Gets a value indicating whether the point is on the character: <see langword="false"/> for a point above, below or beside the text, which gives the nearest character.</summary>
    public bool IsInside { get; }

    /// <summary>
    /// Gets the position in the text a caret takes when the point is clicked: before the character, or after it when
    /// the point is nearer to its end. It is an index <see cref="TextLayout.GetCaretRectangle"/> accepts.
    /// </summary>
    public int CaretIndex => IsTrailing ? TextIndex + Length : TextIndex;

    /// <inheritdoc />
    public bool Equals(TextHit other)
        => LineIndex == other.LineIndex && TextIndex == other.TextIndex && Length == other.Length && IsTrailing == other.IsTrailing && IsInside == other.IsInside;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TextHit other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(LineIndex, TextIndex, Length, IsTrailing, IsInside);

    /// <summary>Returns the line, the character and the side of the hit.</summary>
    /// <returns>A culture-invariant string.</returns>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"line {LineIndex}, character {TextIndex}+{Length}, {(IsTrailing ? "trailing" : "leading")}{(IsInside ? string.Empty : ", outside")}");

    /// <summary>Compares two hits for equality.</summary>
    /// <param name="left">The first hit.</param>
    /// <param name="right">The second hit.</param>
    /// <returns><see langword="true"/> if every value is equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(TextHit left, TextHit right) => left.Equals(right);

    /// <summary>Compares two hits for inequality.</summary>
    /// <param name="left">The first hit.</param>
    /// <param name="right">The second hit.</param>
    /// <returns><see langword="true"/> if a value differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(TextHit left, TextHit right) => !left.Equals(right);
}
