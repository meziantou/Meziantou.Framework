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

    public double TransformX(double x, double y) => ((A * x) + (C * y)) + Tx;

    public double TransformY(double x, double y) => ((B * x) + (D * y)) + Ty;
}
