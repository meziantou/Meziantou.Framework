namespace Meziantou.Framework.Pdf;

/// <summary>
/// A patch of a Coons or tensor-product patch mesh (ISO 32000-2, 8.7.4.5.7 and 8.7.4.5.8): four cubic Bézier curves
/// around the patch and a color at each corner.
/// </summary>
/// <remarks>
/// The twelve boundary points go around the patch: the corner <c>p00</c>, then the curve up to the corner <c>p03</c>,
/// the curve across to <c>p33</c>, the curve down to <c>p30</c> and the curve back to <c>p00</c> (figure 49 of the
/// specification), each curve given by its two control points and its end. The colors are those of the corners
/// <c>p00</c>, <c>p03</c>, <c>p33</c> and <c>p30</c>.
/// </remarks>
public sealed class PdfPatch
{
    private readonly PdfPoint[] _boundary;
    private readonly PdfPoint[]? _inner;
    private readonly PdfColor[] _colors;

    /// <summary>Initializes a new instance of the <see cref="PdfPatch"/> class.</summary>
    /// <param name="boundaryPoints">The twelve points of the boundary, from the corner <c>p00</c>.</param>
    /// <param name="cornerColors">The colors of the four corners, in the order of the boundary.</param>
    /// <param name="innerPoints">The four inner control points of a tensor-product patch, <c>p11</c>, <c>p12</c>, <c>p22</c>, <c>p21</c>; <see langword="null"/> to compute them as a Coons patch does.</param>
    public PdfPatch(ReadOnlySpan<PdfPoint> boundaryPoints, ReadOnlySpan<PdfColor> cornerColors, ReadOnlySpan<PdfPoint> innerPoints = default)
    {
        if (boundaryPoints.Length != 12)
            throw new ArgumentException("A patch has twelve boundary points", nameof(boundaryPoints));

        if (cornerColors.Length != 4)
            throw new ArgumentException("A patch has four corner colors", nameof(cornerColors));

        if (!innerPoints.IsEmpty && innerPoints.Length != 4)
            throw new ArgumentException("A tensor-product patch has four inner points", nameof(innerPoints));

        foreach (var color in cornerColors)
        {
            if (color.ColorSpace != cornerColors[0].ColorSpace)
                throw new ArgumentException("The colors must be in the same color space", nameof(cornerColors));
        }

        _boundary = boundaryPoints.ToArray();
        _colors = cornerColors.ToArray();
        _inner = innerPoints.IsEmpty ? null : innerPoints.ToArray();
    }

    /// <summary>Gets the twelve points of the boundary.</summary>
    public ReadOnlySpan<PdfPoint> BoundaryPoints => _boundary;

    /// <summary>Gets the four inner control points, or an empty span when they are those of a Coons patch.</summary>
    public ReadOnlySpan<PdfPoint> InnerPoints => _inner;

    /// <summary>Gets the colors of the four corners.</summary>
    public ReadOnlySpan<PdfColor> Colors => _colors;

    /// <summary>Gets the points in the order of the data of the mesh: the boundary, then for a tensor-product patch the inner points.</summary>
    internal IEnumerable<PdfPoint> GetPoints(bool includeInnerPoints)
    {
        foreach (var point in _boundary)
        {
            yield return point;
        }

        if (!includeInnerPoints)
            yield break;

        if (_inner is not null)
        {
            foreach (var point in _inner)
            {
                yield return point;
            }

            yield break;
        }

        // The inner points of the Coons patch with this boundary (8.7.4.5.8, the equations of p11, p12, p22, p21)
        var p = GetGrid();
        yield return Inner(p, 1, 1);
        yield return Inner(p, 1, 2);
        yield return Inner(p, 2, 2);
        yield return Inner(p, 2, 1);
    }

    /// <summary>Gets the boundary points in the grid <c>p[i][j]</c> of the specification.</summary>
    private PdfPoint[][] GetGrid()
    {
        PdfPoint[][] p = [new PdfPoint[4], new PdfPoint[4], new PdfPoint[4], new PdfPoint[4]];
        p[0][0] = _boundary[0];
        p[0][1] = _boundary[1];
        p[0][2] = _boundary[2];
        p[0][3] = _boundary[3];
        p[1][3] = _boundary[4];
        p[2][3] = _boundary[5];
        p[3][3] = _boundary[6];
        p[3][2] = _boundary[7];
        p[3][1] = _boundary[8];
        p[3][0] = _boundary[9];
        p[2][0] = _boundary[10];
        p[1][0] = _boundary[11];
        return p;
    }

    private static PdfPoint Inner(PdfPoint[][] p, int i, int j)
    {
        // p11 = (-4 p00 + 6 (p01 + p10) - 2 (p03 + p30) + 3 (p31 + p13) - p33) / 9, and the symmetric equations
        var i0 = i == 1 ? 0 : 3;
        var i3 = 3 - i0;
        var j0 = j == 1 ? 0 : 3;
        var j3 = 3 - j0;
        double Combine(Func<PdfPoint, double> axis)
        {
            return ((-4 * axis(p[i0][j0]))
                + (6 * (axis(p[i0][j]) + axis(p[i][j0])))
                - (2 * (axis(p[i0][j3]) + axis(p[i3][j0])))
                + (3 * (axis(p[i3][j]) + axis(p[i][j3])))
                - axis(p[i3][j3])) / 9;
        }

        return new PdfPoint(Combine(point => point.X), Combine(point => point.Y));
    }
}
