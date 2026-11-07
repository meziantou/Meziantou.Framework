using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf;

/// <summary>A size in user space units: 1/72 inch unless a page sets <c>/UserUnit</c>.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PdfSize : IEquatable<PdfSize>
{
    private const double PointsPerMillimeter = 72 / 25.4;

    /// <summary>Initializes a new instance of the <see cref="PdfSize"/> struct.</summary>
    /// <param name="width">The width, a positive number.</param>
    /// <param name="height">The height, a positive number.</param>
    public PdfSize(double width, double height)
    {
        if (!double.IsFinite(width) || width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be a positive number");

        if (!double.IsFinite(height) || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height must be a positive number");

        Width = width;
        Height = height;
    }

    /// <summary>Gets the ISO 216 A3 size, 297 x 420 mm.</summary>
    public static PdfSize A3 { get; } = FromMillimeters(297, 420);

    /// <summary>Gets the ISO 216 A4 size, 210 x 297 mm.</summary>
    public static PdfSize A4 { get; } = FromMillimeters(210, 297);

    /// <summary>Gets the ISO 216 A5 size, 148 x 210 mm.</summary>
    public static PdfSize A5 { get; } = FromMillimeters(148, 210);

    /// <summary>Gets the US Letter size, 8.5 x 11 inches.</summary>
    public static PdfSize Letter { get; } = new(612, 792);

    /// <summary>Gets the US Legal size, 8.5 x 14 inches.</summary>
    public static PdfSize Legal { get; } = new(612, 1008);

    /// <summary>Gets the width.</summary>
    public double Width { get; }

    /// <summary>Gets the height.</summary>
    public double Height { get; }

    /// <summary>Indicates whether two sizes are equal.</summary>
    /// <param name="left">The first size.</param>
    /// <param name="right">The second size.</param>
    /// <returns><see langword="true"/> when the sizes are equal.</returns>
    public static bool operator ==(PdfSize left, PdfSize right) => left.Equals(right);

    /// <summary>Indicates whether two sizes are different.</summary>
    /// <param name="left">The first size.</param>
    /// <param name="right">The second size.</param>
    /// <returns><see langword="true"/> when the sizes are different.</returns>
    public static bool operator !=(PdfSize left, PdfSize right) => !left.Equals(right);

    /// <summary>Creates a size from millimeters.</summary>
    /// <param name="width">The width in millimeters.</param>
    /// <param name="height">The height in millimeters.</param>
    /// <returns>The size in points.</returns>
    public static PdfSize FromMillimeters(double width, double height) => new(width * PointsPerMillimeter, height * PointsPerMillimeter);

    /// <summary>Gets the same size in landscape orientation: the greater dimension is the width.</summary>
    /// <returns>The size.</returns>
    public PdfSize ToLandscape() => Width >= Height ? this : new PdfSize(Height, Width);

    /// <inheritdoc/>
    public bool Equals(PdfSize other) => Width.Equals(other.Width) && Height.Equals(other.Height);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is PdfSize other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Width, Height);

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Width} x {Height}");
}
