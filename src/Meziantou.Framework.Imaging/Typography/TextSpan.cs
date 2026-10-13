using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>A part of a <see cref="RichText"/> that has one style.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct TextSpan : IEquatable<TextSpan>
{
    internal TextSpan(int start, int length, TextStyle style)
    {
        Start = start;
        Length = length;
        Style = style;
    }

    /// <summary>Gets the index of the first character of the span in the text.</summary>
    public int Start { get; }

    /// <summary>Gets the number of characters of the span, in UTF-16 code units.</summary>
    public int Length { get; }

    /// <summary>Gets the style of the span.</summary>
    public TextStyle Style { get; }

    /// <inheritdoc />
    public bool Equals(TextSpan other) => Start == other.Start && Length == other.Length && ReferenceEquals(Style, other.Style);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TextSpan other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Start, Length, Style);

    /// <summary>Determines whether two spans are the same part of a text with the same style.</summary>
    public static bool operator ==(TextSpan left, TextSpan right) => left.Equals(right);

    /// <summary>Determines whether two spans differ.</summary>
    public static bool operator !=(TextSpan left, TextSpan right) => !left.Equals(right);
}
