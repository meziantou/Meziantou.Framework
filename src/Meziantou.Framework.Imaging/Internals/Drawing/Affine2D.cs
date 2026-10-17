using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// An affine transformation of the plane: <c>x' = (A x) + (C y) + Tx</c> and <c>y' = (B x) + (D y) + Ty</c>.
/// </summary>
/// <remarks>
/// The sums are evaluated in this order, with these parentheses, wherever a point is transformed: a glyph drawn by
/// <c>FillText</c> and the same glyph in the path of <c>TextLayout.ToPath</c> have the same points, bit for bit.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct Affine2D
{
    public Affine2D(double a, double b, double c, double d, double tx, double ty)
    {
        A = a;
        B = b;
        C = c;
        D = d;
        Tx = tx;
        Ty = ty;
    }

    public double A { get; }

    public double B { get; }

    public double C { get; }

    public double D { get; }

    public double Tx { get; }

    public double Ty { get; }

    /// <summary>
    /// Creates the transformation of an upright glyph: font units with the y axis up to pixels with the y axis down, the
    /// origin of the glyph at a point of its baseline.
    /// </summary>
    public static Affine2D ForGlyph(double scale, double originX, double originY) => new(scale, 0, 0, -scale, originX, originY);

    /// <summary>
    /// Creates the transformation of a slanted glyph: an upright glyph whose points move right by <paramref name="shear"/>
    /// times their height above the baseline. Without shear, it is the transformation of an upright glyph, bit for bit.
    /// </summary>
    public static Affine2D ForGlyph(double scale, double originX, double originY, double shear) => new(scale, 0, shear * scale, -scale, originX, originY);

    /// <summary>
    /// Creates the transformation of a slanted glyph that is narrowed: its points also move toward its origin, to
    /// <paramref name="widthScale"/> times their distance from it along the baseline. With a width scale of 1, it is the
    /// transformation of a slanted glyph, bit for bit.
    /// </summary>
    public static Affine2D ForGlyph(double scale, double originX, double originY, double shear, double widthScale) => new(scale * widthScale, 0, shear * scale, -scale, originX, originY);

    /// <summary>
    /// Creates the transformation of a glyph that is on its side in a vertical line: the glyph of a horizontal line,
    /// slanted or not, turned a quarter of a turn clockwise around its origin. Its baseline goes down from the origin,
    /// and what is above the baseline is on its right.
    /// </summary>
    public static Affine2D ForSidewaysGlyph(double scale, double originX, double originY, double shear) => new(0, scale, scale, shear * scale, originX, originY);

    /// <summary>Gets the transformation that applies six numbers (xx, yx, xy, yy, dx, dy) first, then this one.</summary>
    public Affine2D Then(ReadOnlySpan<double> inner) => new(
        (A * inner[0]) + (C * inner[1]),
        (B * inner[0]) + (D * inner[1]),
        (A * inner[2]) + (C * inner[3]),
        (B * inner[2]) + (D * inner[3]),
        ((A * inner[4]) + (C * inner[5])) + Tx,
        ((B * inner[4]) + (D * inner[5])) + Ty);

    /// <summary>Gets the transformation that undoes this one.</summary>
    /// <returns><see langword="false"/> when the transformation flattens the plane: nothing undoes it.</returns>
    public bool TryInvert(out Affine2D inverse)
    {
        var determinant = (A * D) - (B * C);
        if (determinant == 0 || !double.IsFinite(determinant))
        {
            inverse = default;
            return false;
        }

        inverse = new Affine2D(D / determinant, -B / determinant, -C / determinant, A / determinant, ((C * Ty) - (D * Tx)) / determinant, ((B * Tx) - (A * Ty)) / determinant);
        return true;
    }

    public double TransformX(double x, double y) => ((A * x) + (C * y)) + Tx;

    public double TransformY(double x, double y) => ((B * x) + (D * y)) + Ty;
}
