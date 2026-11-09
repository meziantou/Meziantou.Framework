using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>
/// A patch of a <see cref="MeshGradientBrush"/>: a surface bounded by four cubic Bézier curves, with a color at each of its
/// four corners, mixed across the surface. It is a Coons patch (<see cref="CreateCoons"/>, PDF shading type 6, the patches
/// of SVG 2's <c>meshgradient</c>) or a tensor-product patch (<see cref="CreateTensorProduct"/>, PDF shading type 7).
/// </summary>
/// <remarks>
/// <para>
/// <b>Control points.</b> A patch is stored as the 16 control points of a bicubic tensor-product surface, row by row:
/// <c>P[r, c]</c> is <see cref="ControlPoints"/><c>[4 r + c]</c>. The position of the parameters <c>(u, v)</c>, from 0 to
/// 1, is <c>S(u, v) = sum B_r(v) B_c(u) P[r, c]</c>, with the Bernstein polynomials <c>B_0(t) = (1 - t)^3</c>,
/// <c>B_1(t) = 3 t (1 - t)^2</c>, <c>B_2(t) = 3 t^2 (1 - t)</c> and <c>B_3(t) = t^3</c>: <c>u</c> goes along the rows (from
/// the "left" side to the "right" one) and <c>v</c> along the columns (from the "top" side to the "bottom" one). The four
/// sides are the Bézier curves of the outer control points; the four inner ones shape the inside.
/// </para>
/// <para>
/// <b>Colors.</b> The color of <c>(u, v)</c> is the bilinear mix of the corner colors: <c>(1 - u)(1 - v)</c> of the
/// top-left one, <c>u (1 - v)</c> of the top-right one, <c>u v</c> of the bottom-right one and <c>(1 - u) v</c> of the
/// bottom-left one, as PDF interpolates them in the parameter space.
/// </para>
/// </remarks>
public sealed class MeshGradientPatch
{
    // The indexes in the 4x4 grid of the 12 points of the boundary: top row from left to right, right column from top to
    // bottom, bottom row from right to left, left column from bottom to top
    private static ReadOnlySpan<byte> BoundaryIndexes => [0, 1, 2, 3, 7, 11, 15, 14, 13, 12, 8, 4];

    private readonly PointD[] _controlPoints;
    private readonly Rgba64[] _cornerColors;

    private MeshGradientPatch(PointD[] controlPoints, Rgba64[] cornerColors)
    {
        _controlPoints = controlPoints;
        _cornerColors = cornerColors;
    }

    /// <summary>Gets the 16 control points of the tensor-product surface, row by row from the top-left corner.</summary>
    public ReadOnlyMemory<PointD> ControlPoints => _controlPoints;

    /// <summary>Gets the colors of the corners, with straight alpha: top-left (<c>P[0, 0]</c>), top-right (<c>P[0, 3]</c>), bottom-right (<c>P[3, 3]</c>) and bottom-left (<c>P[3, 0]</c>).</summary>
    public ReadOnlyMemory<Rgba64> CornerColors => _cornerColors;

    /// <summary>Gets the control points without copying them.</summary>
    internal ReadOnlySpan<PointD> ControlPointSpan => _controlPoints;

    /// <summary>Gets the corner colors without copying them.</summary>
    internal ReadOnlySpan<Rgba64> CornerColorSpan => _cornerColors;

    /// <summary>
    /// Creates a Coons patch from the four Bézier curves of its boundary: the patch whose inside is shaped by its sides
    /// only, as PDF's shading type 6 and SVG 2's mesh gradients define it.
    /// </summary>
    /// <remarks>
    /// The 12 points go around the patch: the top side from left to right (<c>boundary[0..3]</c>), the right side from top
    /// to bottom (<c>boundary[3..6]</c>), the bottom side from right to left (<c>boundary[6..9]</c>) and the left side from
    /// bottom to top (<c>boundary[9..11]</c> and <c>boundary[0]</c>). Each side is a cubic Bézier curve: its two ends and,
    /// between them, its two control points; a straight side has its control points on the segment, at a third and two
    /// thirds of it. It is the order of the points of a patch in a PDF type 6 stream (<c>p00 p01 p02 p03 p13 p23 p33 p32 p31
    /// p30 p20 p10</c>, with the corner colors <c>c00 c03 c33 c30</c>) and of an SVG <c>meshpatch</c> walked from its top-left
    /// corner. The inner control points of the equivalent tensor-product patch are computed with the formulas of the PDF
    /// specification (section 8.7.4.5.8).
    /// </remarks>
    /// <param name="boundary">The 12 points of the boundary, in canvas coordinates. They are copied.</param>
    /// <param name="cornerColors">The colors of the top-left, top-right, bottom-right and bottom-left corners (<c>boundary[0]</c>, <c>[3]</c>, <c>[6]</c> and <c>[9]</c>), with straight alpha, or one color for every corner.</param>
    /// <returns>The patch.</returns>
    /// <exception cref="ArgumentException">There are not 12 points, the number of colors is neither one nor four, or a coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    public static MeshGradientPatch CreateCoons(ReadOnlySpan<PointD> boundary, ReadOnlySpan<Rgba64> cornerColors)
    {
        if (boundary.Length != 12)
            throw new ArgumentException("A Coons patch has 12 boundary points.", nameof(boundary));

        foreach (var point in boundary)
        {
            DrawingGuard.Point(point, nameof(boundary));
        }

        var colors = GetCornerColors(cornerColors);
        var points = new PointD[16];
        var indexes = BoundaryIndexes;
        for (var i = 0; i < indexes.Length; i++)
        {
            points[indexes[i]] = boundary[i];
        }

        // PDF 32000-1:2008, 8.7.4.5.8: the inner points of the tensor-product patch of a Coons patch
        points[5] = Combine(points, 0, 1, 4, 3, 12, 13, 7, 15);
        points[6] = Combine(points, 3, 2, 7, 0, 15, 14, 4, 12);
        points[9] = Combine(points, 12, 13, 8, 15, 0, 1, 11, 3);
        points[10] = Combine(points, 15, 14, 11, 12, 3, 2, 8, 0);
        return new MeshGradientPatch(points, colors);

        // (-4 corner + 6 (a + b) - 2 (c + d) + 3 (e + f) - opposite) / 9
        static PointD Combine(PointD[] p, int corner, int a, int b, int c, int d, int e, int f, int opposite)
        {
            return new PointD(
                ((-4 * p[corner].X) + (6 * (p[a].X + p[b].X)) - (2 * (p[c].X + p[d].X)) + (3 * (p[e].X + p[f].X)) - p[opposite].X) / 9,
                ((-4 * p[corner].Y) + (6 * (p[a].Y + p[b].Y)) - (2 * (p[c].Y + p[d].Y)) + (3 * (p[e].Y + p[f].Y)) - p[opposite].Y) / 9);
        }
    }

    /// <summary>Creates a tensor-product patch from its 16 control points, as PDF's shading type 7 defines it.</summary>
    /// <remarks>
    /// The points are given row by row: <c>controlPoints[4 r + c]</c> is <c>P[r, c]</c>, the 12 outer ones forming the four
    /// sides and the four inner ones shaping the inside. A PDF type 7 stream gives the points of a patch in the order
    /// <c>p00 p01 p02 p03 p13 p23 p33 p32 p31 p30 p20 p10 p11 p12 p22 p21</c>, where <c>pij</c> is <c>P[i, j]</c>.
    /// </remarks>
    /// <param name="controlPoints">The 16 control points, in canvas coordinates. They are copied.</param>
    /// <param name="cornerColors">The colors of the top-left, top-right, bottom-right and bottom-left corners (<c>P[0, 0]</c>, <c>P[0, 3]</c>, <c>P[3, 3]</c> and <c>P[3, 0]</c>), with straight alpha, or one color for every corner.</param>
    /// <returns>The patch.</returns>
    /// <exception cref="ArgumentException">There are not 16 points, the number of colors is neither one nor four, or a coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    public static MeshGradientPatch CreateTensorProduct(ReadOnlySpan<PointD> controlPoints, ReadOnlySpan<Rgba64> cornerColors)
    {
        if (controlPoints.Length != 16)
            throw new ArgumentException("A tensor-product patch has 16 control points.", nameof(controlPoints));

        foreach (var point in controlPoints)
        {
            DrawingGuard.Point(point, nameof(controlPoints));
        }

        return new MeshGradientPatch(controlPoints.ToArray(), GetCornerColors(cornerColors));
    }

    /// <summary>Gets the position of the parameters <c>(u, v)</c> on the surface.</summary>
    /// <param name="u">The parameter along the rows, from 0 (left side) to 1 (right side).</param>
    /// <param name="v">The parameter along the columns, from 0 (top side) to 1 (bottom side).</param>
    /// <returns>The position, in canvas coordinates (before <see cref="MeshGradientBrush.Transform"/>).</returns>
    public PointD GetPoint(double u, double v)
    {
        Span<double> bu = stackalloc double[4];
        Span<double> bv = stackalloc double[4];
        Bernstein(u, bu);
        Bernstein(v, bv);
        var x = 0.0;
        var y = 0.0;
        for (var r = 0; r < 4; r++)
        {
            var rowX = 0.0;
            var rowY = 0.0;
            for (var c = 0; c < 4; c++)
            {
                rowX += bu[c] * _controlPoints[(4 * r) + c].X;
                rowY += bu[c] * _controlPoints[(4 * r) + c].Y;
            }

            x += bv[r] * rowX;
            y += bv[r] * rowY;
        }

        return new PointD(x, y);
    }

    /// <summary>Computes the four cubic Bernstein polynomials at <paramref name="t"/>.</summary>
    internal static void Bernstein(double t, Span<double> values)
    {
        var s = 1 - t;
        values[0] = s * s * s;
        values[1] = 3 * t * s * s;
        values[2] = 3 * t * t * s;
        values[3] = t * t * t;
    }

    private static Rgba64[] GetCornerColors(ReadOnlySpan<Rgba64> cornerColors)
    {
        return cornerColors.Length switch
        {
            1 => [cornerColors[0], cornerColors[0], cornerColors[0], cornerColors[0]],
            4 => cornerColors.ToArray(),
            _ => throw new ArgumentException("There must be one color, or one color per corner.", nameof(cornerColors)),
        };
    }
}
