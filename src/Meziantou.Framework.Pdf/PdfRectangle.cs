using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A rectangle in user space units (ISO 32000-2, 7.9.5): its lower-left and upper-right corners.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PdfRectangle : IEquatable<PdfRectangle>
{
    /// <summary>Initializes a new instance of the <see cref="PdfRectangle"/> struct. The corners are normalized: <see cref="Left"/> is the smaller x coordinate and <see cref="Bottom"/> the smaller y coordinate.</summary>
    /// <param name="x1">The x coordinate of a corner.</param>
    /// <param name="y1">The y coordinate of a corner.</param>
    /// <param name="x2">The x coordinate of the opposite corner.</param>
    /// <param name="y2">The y coordinate of the opposite corner.</param>
    public PdfRectangle(double x1, double y1, double x2, double y2)
    {
        Left = Math.Min(x1, x2);
        Right = Math.Max(x1, x2);
        Bottom = Math.Min(y1, y2);
        Top = Math.Max(y1, y2);
    }

    /// <summary>Gets the smaller x coordinate.</summary>
    public double Left { get; }

    /// <summary>Gets the smaller y coordinate.</summary>
    public double Bottom { get; }

    /// <summary>Gets the greater x coordinate.</summary>
    public double Right { get; }

    /// <summary>Gets the greater y coordinate.</summary>
    public double Top { get; }

    /// <summary>Gets the width.</summary>
    public double Width => Right - Left;

    /// <summary>Gets the height.</summary>
    public double Height => Top - Bottom;

    /// <summary>Indicates whether two rectangles are equal.</summary>
    /// <param name="left">The first rectangle.</param>
    /// <param name="right">The second rectangle.</param>
    /// <returns><see langword="true"/> when the rectangles are equal.</returns>
    public static bool operator ==(PdfRectangle left, PdfRectangle right) => left.Equals(right);

    /// <summary>Indicates whether two rectangles are different.</summary>
    /// <param name="left">The first rectangle.</param>
    /// <param name="right">The second rectangle.</param>
    /// <returns><see langword="true"/> when the rectangles are different.</returns>
    public static bool operator !=(PdfRectangle left, PdfRectangle right) => !left.Equals(right);

    /// <summary>Creates a rectangle from its lower-left corner and its size.</summary>
    /// <param name="x">The x coordinate of the lower-left corner.</param>
    /// <param name="y">The y coordinate of the lower-left corner.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The rectangle.</returns>
    public static PdfRectangle FromSize(double x, double y, double width, double height) => new(x, y, x + width, y + height);

    /// <summary>Reads a rectangle from an array of four numbers.</summary>
    /// <param name="array">The array.</param>
    /// <param name="rectangle">The rectangle.</param>
    /// <returns><see langword="true"/> when the array has four numbers.</returns>
    public static bool TryRead(PdfArray? array, out PdfRectangle rectangle)
    {
        rectangle = default;
        if (array is null || array.Count < 4)
            return false;

        for (var i = 0; i < 4; i++)
        {
            if (!array.TryGetResolved<PdfNumber>(i, out _))
                return false;
        }

        rectangle = new PdfRectangle(array.GetNumber(0), array.GetNumber(1), array.GetNumber(2), array.GetNumber(3));
        return true;
    }

    /// <summary>Converts the rectangle to an array of four numbers.</summary>
    /// <returns>The array <c>[left bottom right top]</c>.</returns>
    public PdfArray ToArray() => PdfArray.FromNumbers(Left, Bottom, Right, Top);

    /// <inheritdoc/>
    public bool Equals(PdfRectangle other) => Left.Equals(other.Left) && Bottom.Equals(other.Bottom) && Right.Equals(other.Right) && Top.Equals(other.Top);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is PdfRectangle other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Left, Bottom, Right, Top);

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"[{Left} {Bottom} {Right} {Top}]");
}
