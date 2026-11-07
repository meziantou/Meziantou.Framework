using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>
/// A brush painting the colors of the vertices of a polygon and of a center point, mixed across the triangles between
/// them: the colors of a shape described by its vertices (Gouraud shading).
/// </summary>
/// <remarks>
/// <para>
/// <b>Triangles.</b> The polygon of <see cref="Points"/> is cut into triangles from <see cref="Center"/>: the triangle
/// <c>i</c> has the vertices <see cref="Center"/>, <c>Points[i]</c> and <c>Points[(i + 1) mod n]</c>. A position takes
/// the color of the first of these triangles that contains it, edges included; a position no triangle contains has no
/// color, and nothing is painted there, whatever the shape that is painted. When the center sees every edge of the
/// polygon (a convex polygon around its center, a star around its middle), the triangles cover the polygon exactly once.
/// A triangle whose three vertices are on one line contains nothing.
/// </para>
/// <para>
/// <b>Colors.</b> The color of a pixel is the color at the center of that pixel. In the triangle of the center
/// <c>c</c> and of the points <c>a</c> and <c>b</c>, with the cross product <c>u × v = ux vy - uy vx</c>, the position
/// <c>p</c> has the weights <c>s = ((p - c) × (b - c)) / ((a - c) × (b - c))</c> for <c>a</c> and
/// <c>t = ((a - c) × (p - c)) / ((a - c) × (b - c))</c> for <c>b</c>, and is in the triangle when <c>s &gt;= 0</c>,
/// <c>t &gt;= 0</c> and <c>s + t &lt;= 1</c>. The colors are converted to the pixel format of the frame first, then
/// interpolated on alpha-premultiplied values, as the stops of a gradient: <c>a = ac + s (aa - ac) + t (ab - ac)</c>
/// for alpha and the same for each premultiplied sample, divided by <c>a</c>, each rounded as
/// <c>floor(v + 1/2 + 2^-20)</c>. A point has exactly its color, and a brush whose colors are all the same paints
/// exactly what a <see cref="SolidBrush"/> of that color paints inside the triangles.
/// </para>
/// <para>
/// <b>Cost.</b> Each pixel tests the triangles in order until one contains it (those that cannot, from their bounds,
/// are skipped): the cost of a pixel grows with the number of points. The arithmetic uses <c>+ - * /</c> only: the
/// pixels are the same on every platform.
/// </para>
/// </remarks>
public sealed class PathGradientBrush : Brush
{
    private readonly PointD[] _points;
    private readonly Rgba64[] _pointColors;

    /// <summary>Initializes a new instance of the <see cref="PathGradientBrush"/> class whose center is the mean of its points.</summary>
    /// <param name="points">The vertices of the polygon, in canvas coordinates; at least three. They are copied.</param>
    /// <param name="pointColors">The color of each point, with straight alpha, or one color for every point. They are copied.</param>
    /// <param name="centerColor">The color of the center, with straight alpha.</param>
    /// <exception cref="ArgumentException">There are fewer than three points, the number of colors is neither one nor the number of points, or a coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    public PathGradientBrush(ReadOnlySpan<PointD> points, ReadOnlySpan<Rgba64> pointColors, Rgba64 centerColor)
        : this(points, pointColors, GetMean(points), centerColor)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PathGradientBrush"/> class.</summary>
    /// <param name="points">The vertices of the polygon, in canvas coordinates; at least three. They are copied.</param>
    /// <param name="pointColors">The color of each point, with straight alpha, or one color for every point. They are copied.</param>
    /// <param name="center">The point all the triangles share, in canvas coordinates.</param>
    /// <param name="centerColor">The color of the center, with straight alpha.</param>
    /// <exception cref="ArgumentException">There are fewer than three points, the number of colors is neither one nor the number of points, or a coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    public PathGradientBrush(ReadOnlySpan<PointD> points, ReadOnlySpan<Rgba64> pointColors, PointD center, Rgba64 centerColor)
    {
        ValidatePoints(points);
        if (pointColors.Length != 1 && pointColors.Length != points.Length)
            throw new ArgumentException("There must be one color, or one color per point.", nameof(pointColors));

        DrawingGuard.Point(center, nameof(center));
        _points = points.ToArray();
        _pointColors = new Rgba64[points.Length];
        if (pointColors.Length == 1)
        {
            _pointColors.AsSpan().Fill(pointColors[0]);
        }
        else
        {
            pointColors.CopyTo(_pointColors);
        }

        Center = center;
        CenterColor = centerColor;
    }

    /// <summary>Gets the vertices of the polygon.</summary>
    public ReadOnlyMemory<PointD> Points => _points;

    /// <summary>Gets the color of each point, with straight alpha: as many as there are points.</summary>
    public ReadOnlyMemory<Rgba64> PointColors => _pointColors;

    /// <summary>Gets the point all the triangles share.</summary>
    public PointD Center { get; }

    /// <summary>Gets the color of the center, with straight alpha.</summary>
    public Rgba64 CenterColor { get; }

    /// <summary>
    /// Gets the transformation that places the polygon on the canvas: the points are given before it is applied.
    /// Defaults to the identity.
    /// </summary>
    /// <remarks>
    /// The color of a pixel is the color the brush has, before the transformation, at the position the center of the
    /// pixel comes from. With a transformation that flattens the plane to a line or a point, the brush paints nothing.
    /// </remarks>
    public AffineTransform Transform { get; init; }

    /// <summary>Gets the points without copying them.</summary>
    internal ReadOnlySpan<PointD> PointSpan => _points;

    /// <summary>Gets the colors of the points without copying them.</summary>
    internal ReadOnlySpan<Rgba64> PointColorSpan => _pointColors;

    private static PointD GetMean(ReadOnlySpan<PointD> points)
    {
        ValidatePoints(points);
        var x = 0.0;
        var y = 0.0;
        foreach (var point in points)
        {
            x += point.X;
            y += point.Y;
        }

        return new PointD(x / points.Length, y / points.Length);
    }

    private static void ValidatePoints(ReadOnlySpan<PointD> points)
    {
        if (points.Length < 3)
            throw new ArgumentException("A path gradient needs at least three points.", nameof(points));

        foreach (var point in points)
        {
            DrawingGuard.Point(point, nameof(points));
        }
    }
}
