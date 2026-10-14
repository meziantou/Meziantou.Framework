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

    /// <summary>Creates the span of a placeholder: one character, which takes the room of what the caller draws in the line.</summary>
    internal TextSpan(int start, TextStyle style, double width, double height, double baseline)
    {
        Start = start;
        Length = 1;
        Style = style;
        IsPlaceholder = true;
        PlaceholderWidth = width;
        PlaceholderHeight = height;
        PlaceholderBaseline = baseline;
    }

    /// <summary>Gets the index of the first character of the span in the text.</summary>
    public int Start { get; }

    /// <summary>Gets the number of characters of the span, in UTF-16 code units.</summary>
    public int Length { get; }

    /// <summary>Gets the style of the span.</summary>
    public TextStyle Style { get; }

    /// <summary>
    /// Gets a value indicating whether the span is a placeholder: one character (U+FFFC) that takes, in its line, the
    /// room of what the caller draws there. See <see cref="RichTextBuilder.AppendPlaceholder(double, double, double, TextStyle)"/>.
    /// </summary>
    public bool IsPlaceholder { get; }

    /// <summary>Gets the width of the placeholder, in pixels; 0 for a span of text.</summary>
    internal double PlaceholderWidth { get; }

    /// <summary>Gets the height of the placeholder, in pixels.</summary>
    internal double PlaceholderHeight { get; }

    /// <summary>Gets the distance from the top of the placeholder to the baseline of its line, in pixels.</summary>
    internal double PlaceholderBaseline { get; }

    /// <inheritdoc />
    public bool Equals(TextSpan other)
        => Start == other.Start && Length == other.Length && ReferenceEquals(Style, other.Style) && IsPlaceholder == other.IsPlaceholder
            && PlaceholderWidth == other.PlaceholderWidth && PlaceholderHeight == other.PlaceholderHeight && PlaceholderBaseline == other.PlaceholderBaseline;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TextSpan other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Start, Length, Style);

    /// <summary>Determines whether two spans are the same part of a text with the same style.</summary>
    public static bool operator ==(TextSpan left, TextSpan right) => left.Equals(right);

    /// <summary>Determines whether two spans differ.</summary>
    public static bool operator !=(TextSpan left, TextSpan right) => !left.Equals(right);
}
