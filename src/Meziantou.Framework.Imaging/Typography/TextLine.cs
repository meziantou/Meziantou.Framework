using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>A line of a <see cref="TextLayout"/>: the part of the text it holds and where it is.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct TextLine : IEquatable<TextLine>
{
    internal TextLine(int start, int length, double x, double y, double width, double height, double baseline, bool isRightToLeft)
    {
        Start = start;
        Length = length;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Baseline = baseline;
        IsRightToLeft = isRightToLeft;
    }

    /// <summary>Gets the index of the first character of the line in the text.</summary>
    public int Start { get; }

    /// <summary>
    /// Gets the number of characters of the line, in UTF-16 code units, with the spaces and the line break that end it.
    /// The lines of a layout follow each other in the text.
    /// </summary>
    public int Length { get; }

    /// <summary>Gets the distance from the left of the layout to the left of the text of the line, in pixels: where the alignment puts it.</summary>
    public double X { get; }

    /// <summary>Gets the distance from the top of the layout to the top of the line, in pixels.</summary>
    public double Y { get; }

    /// <summary>Gets the width of the text of the line, in pixels, without the spaces that end it.</summary>
    public double Width { get; }

    /// <summary>Gets the height of the line, in pixels.</summary>
    public double Height { get; }

    /// <summary>Gets the distance from the top of the line to its baseline, in pixels.</summary>
    public double Baseline { get; }

    /// <summary>Gets a value indicating whether the paragraph of the line is laid out from right to left.</summary>
    public bool IsRightToLeft { get; }

    /// <inheritdoc />
    public bool Equals(TextLine other)
        => Start == other.Start && Length == other.Length && X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height) && Baseline.Equals(other.Baseline) && IsRightToLeft == other.IsRightToLeft;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TextLine other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Start, Length, X, Y, Width, Height, Baseline, IsRightToLeft);

    /// <summary>Determines whether two lines are the same.</summary>
    public static bool operator ==(TextLine left, TextLine right) => left.Equals(right);

    /// <summary>Determines whether two lines differ.</summary>
    public static bool operator !=(TextLine left, TextLine right) => !left.Equals(right);
}
