using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Dom;
using static Meziantou.Framework.WebPlatform.Svg.SvgPathData;

namespace Meziantou.Framework.WebPlatform.Svg;

/// <summary>
/// A 2D affine transform [a c e; b d f] (https://svgwg.org/svg2-draft/coords.html#TransformMatrixDefined): the transforms of
/// the SVG user spaces.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SvgAffine(double A, double B, double C, double D, double E, double F)
{
    public static SvgAffine Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public bool IsIdentity => this == Identity;

    public double Determinant => (A * D) - (B * C);

    /// <summary>Whether the transform maps a non-empty area to a non-empty area (its determinant is not zero).</summary>
    public bool IsInvertible => Determinant is not 0 && double.IsFinite(Determinant);

    public static SvgAffine Translation(double x, double y) => new(1, 0, 0, 1, x, y);

    public static SvgAffine Scale(double x, double y) => new(x, 0, 0, y, 0, 0);

    /// <summary>A rotation of an angle in degrees around the origin.</summary>
    public static SvgAffine Rotation(double degrees)
    {
        var (sin, cos) = SinCosDegrees(degrees);
        return new SvgAffine(cos, sin, -sin, cos, 0, 0);
    }

    /// <summary>
    /// The sine and the cosine of an angle in degrees, exact for the multiples of 90 degrees (as in the browsers, a rotation of
    /// 90 degrees maps the axes to the axes).
    /// </summary>
    public static (double Sin, double Cos) SinCosDegrees(double degrees)
    {
        var reduced = degrees % 360;
        if (reduced < 0)
        {
            reduced += 360;
        }

        return reduced switch
        {
            0 => (0, 1),
            90 => (1, 0),
            180 => (0, -1),
            270 => (-1, 0),
            _ => Math.SinCos(reduced * Math.PI / 180),
        };
    }

    /// <summary>Converts a 4x4 matrix in column-major order (see <c>CssTransforms</c>) to its 2D part (the plane z = 0, without the perspective).</summary>
    public static SvgAffine FromMatrix(ReadOnlySpan<double> m) => new(m[0], m[1], m[4], m[5], m[12], m[13]);

    /// <summary>Converts the transform to a 4x4 matrix in column-major order (see <c>CssTransforms</c>).</summary>
    public double[] ToMatrix() => [A, B, 0, 0, C, D, 0, 0, 0, 0, 1, 0, E, F, 0, 1];

    /// <summary>Returns <paramref name="outer"/> × this: this transform is applied first.</summary>
    public SvgAffine Then(SvgAffine outer) => new(
        (outer.A * A) + (outer.C * B),
        (outer.B * A) + (outer.D * B),
        (outer.A * C) + (outer.C * D),
        (outer.B * C) + (outer.D * D),
        (outer.A * E) + (outer.C * F) + outer.E,
        (outer.B * E) + (outer.D * F) + outer.F);

    public SvgPoint Apply(double x, double y) => new((A * x) + (C * y) + E, (B * x) + (D * y) + F);

    public SvgPoint Apply(SvgPoint point) => Apply(point.X, point.Y);

    /// <summary>Applies the linear part of the transform (a vector: the translation is ignored).</summary>
    public SvgPoint ApplyToVector(SvgPoint vector) => new((A * vector.X) + (C * vector.Y), (B * vector.X) + (D * vector.Y));

    /// <summary>Returns the inverse transform, or null when the transform is not invertible.</summary>
    public SvgAffine? Invert()
    {
        var determinant = Determinant;
        if (determinant == 0 || !double.IsFinite(determinant))
            return null;

        return new SvgAffine(D / determinant, -B / determinant, -C / determinant, A / determinant, ((C * F) - (D * E)) / determinant, ((B * E) - (A * F)) / determinant);
    }

    /// <summary>
    /// Returns the matrix as a <c>DOMMatrix</c> (an <c>SVGMatrix</c>). The components that differ from a whole number by a
    /// rounding error of the trigonometric functions are snapped to it (the browsers compute the matrices in single precision,
    /// so that e.g. two rotations of 180 degrees give the identity).
    /// </summary>
    public DomMatrix ToDomMatrix() => new([Snap(A), Snap(B), Snap(C), Snap(D), Snap(E), Snap(F)]);

    private static double Snap(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return Math.Abs(value - rounded) < 1e-9 ? rounded + 0 : value;
    }
}
