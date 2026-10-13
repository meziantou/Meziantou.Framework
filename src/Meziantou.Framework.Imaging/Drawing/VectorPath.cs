using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>
/// An immutable sequence of contours made of straight segments, Bézier curves and arcs of ellipses, built with a <see cref="VectorPathBuilder"/>
/// and drawn with <see cref="DrawingCanvas.Fill(Brush, VectorPath, FillRule)"/> or <see cref="DrawingCanvas.Stroke(Pen, VectorPath)"/>.
/// </summary>
/// <remarks>
/// A path is thread-safe and independent of any image: create it once and draw it as many times as needed. Coordinates
/// are canvas coordinates (see <see cref="PointD"/>).
/// </remarks>
public sealed class VectorPath
{
    private readonly PathCommand[] _commands;
    private readonly double[] _points;

    internal VectorPath(PathCommand[] commands, double[] points, RectangleD bounds)
    {
        _commands = commands;
        _points = points;
        Bounds = bounds;
    }

    /// <summary>Gets a value indicating whether the path has no element.</summary>
    public bool IsEmpty => _commands.Length == 0;

    /// <summary>
    /// Gets the smallest rectangle containing every point and control point of the path (a curve lies inside the box of
    /// its control points; the control point of an arc of a quarter turn at most is where the tangents at its ends
    /// meet). It does not account for the width of a stroke. Empty for an empty path.
    /// </summary>
    public RectangleD Bounds { get; }

    /// <summary>Creates the path every point of this one becomes when a transformation is applied to it.</summary>
    /// <remarks>
    /// The result is exact: curves are transformed as curves, and an ellipse stays an ellipse, whatever the
    /// transformation. Flattening happens when the result is drawn, so a path that is enlarged stays smooth.
    /// </remarks>
    /// <param name="transform">The transformation.</param>
    /// <returns>The transformed path; this instance when the transformation is the identity.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate of the result exceeds 16777216.</exception>
    public VectorPath Transform(AffineTransform transform)
    {
        if (transform.IsIdentity || IsEmpty)
            return this;

        // An ellipse keeps its compact form when its axes stay horizontal and vertical and it is not mirrored
        var keepsEllipses = transform.M12 == 0 && transform.M21 == 0 && transform.M11 > 0 && transform.M22 > 0;
        var commands = (PathCommand[])_commands.Clone();
        var points = new List<double>(_points.Length);
        var source = _points;
        var index = 0;
        for (var i = 0; i < commands.Length; i++)
        {
            switch (commands[i])
            {
                case PathCommand.MoveTo or PathCommand.LineTo:
                    AddPoint(points, transform, source[index], source[index + 1]);
                    index += 2;
                    break;

                case PathCommand.QuadraticTo:
                    AddPoint(points, transform, source[index], source[index + 1]);
                    AddPoint(points, transform, source[index + 2], source[index + 3]);
                    index += 4;
                    break;

                case PathCommand.CubicTo:
                    AddPoint(points, transform, source[index], source[index + 1]);
                    AddPoint(points, transform, source[index + 2], source[index + 3]);
                    AddPoint(points, transform, source[index + 4], source[index + 5]);
                    index += 6;
                    break;

                case PathCommand.Ellipse:
                    AddPoint(points, transform, source[index], source[index + 1]);
                    if (keepsEllipses)
                    {
                        points.Add(transform.M11 * source[index + 2]);
                        points.Add(transform.M22 * source[index + 3]);
                    }
                    else
                    {
                        commands[i] = PathCommand.EllipseAxes;
                        AddVector(points, transform, source[index + 2], 0);
                        AddVector(points, transform, 0, source[index + 3]);
                    }

                    index += 4;
                    break;

                case PathCommand.EllipseAxes:
                    AddPoint(points, transform, source[index], source[index + 1]);
                    AddVector(points, transform, source[index + 2], source[index + 3]);
                    AddVector(points, transform, source[index + 4], source[index + 5]);
                    index += 6;
                    break;

                case PathCommand.Arc or PathCommand.ArcContinuation:
                    // The unit vectors of the ends of the arc are those of the circle the ellipse is the image of
                    AddPoint(points, transform, source[index], source[index + 1]);
                    AddVector(points, transform, source[index + 2], source[index + 3]);
                    AddVector(points, transform, source[index + 4], source[index + 5]);
                    points.Add(source[index + 6]);
                    points.Add(source[index + 7]);
                    points.Add(source[index + 8]);
                    points.Add(source[index + 9]);
                    AddPoint(points, transform, source[index + 10], source[index + 11]);
                    index += ArcPiece.CoordinateCount;
                    break;
            }
        }

        var result = points.ToArray();
        foreach (var value in result)
        {
            // Written so that a value that is not a number is rejected too
            if (!(Math.Abs(value) <= DrawingGuard.MaxCoordinate))
                throw new ArgumentOutOfRangeException(nameof(transform), "The magnitude of a coordinate of the transformed path exceeds 16777216.");
        }

        return new VectorPath(commands, result, ComputeBounds(commands, result));

        static void AddPoint(List<double> points, AffineTransform transform, double x, double y)
        {
            points.Add(transform.TransformX(x, y));
            points.Add(transform.TransformY(x, y));
        }

        static void AddVector(List<double> points, AffineTransform transform, double x, double y)
        {
            points.Add((transform.M11 * x) + (transform.M21 * y));
            points.Add((transform.M12 * x) + (transform.M22 * y));
        }

    }

    /// <summary>Gets a value indicating whether a point is inside the path: whether a fill of the path paints it.</summary>
    /// <remarks>
    /// The path is the one that is drawn: its curves are their chords, within 1/16 of a pixel of the curve, and an open
    /// contour is closed by a straight segment. A point on the outline itself may be reported either way.
    /// </remarks>
    /// <param name="point">The point.</param>
    /// <param name="fillRule">The rule deciding what is inside when contours overlap.</param>
    /// <returns><see langword="true"/> if the point is inside; <see langword="false"/> for a point that is not a finite position.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fillRule"/> is not a defined <see cref="FillRule"/>.</exception>
    public bool Contains(PointD point, FillRule fillRule = FillRule.NonZero)
    {
        if (fillRule is not (FillRule.NonZero or FillRule.EvenOdd))
            throw new ArgumentOutOfRangeException(nameof(fillRule), fillRule, "The fill rule is not valid.");

        // Written so that a coordinate that is not a number is outside
        var bounds = Bounds;
        if (!(point.X >= bounds.Left && point.X <= bounds.Right && point.Y >= bounds.Top && point.Y <= bounds.Bottom))
            return false;

        var sink = new WindingSink(point.X, point.Y);
        PathFlattener.Flatten(this, sink);
        return fillRule == FillRule.EvenOdd ? (sink.Winding & 1) != 0 : sink.Winding != 0;
    }

    /// <summary>Gets the length of the path: the sum of the lengths of its contours.</summary>
    /// <remarks>
    /// The path is measured as it is drawn: by the chords of its curves, within 1/16 of a pixel of the curve. A closed
    /// contour includes its closing segment; the gap between two contours is not counted. Each call walks the whole path.
    /// </remarks>
    /// <returns>The length, in pixels.</returns>
    public double GetLength()
    {
        if (IsEmpty)
            return 0;

        var sink = new DistanceSink(double.NaN);
        PathFlattener.Flatten(this, sink);
        return sink.Length;
    }

    /// <summary>Finds the point at a distance from the start of the path, and the direction of the path there.</summary>
    /// <remarks>
    /// The distance is measured as <see cref="GetLength"/> measures the path: the contours follow each other, without
    /// the gap between them. A distance before the start gives the first point, a distance beyond the end the last
    /// one. At a corner, the direction is the one of the segment that starts there. Each call walks the path from its
    /// start.
    /// </remarks>
    /// <param name="distance">The distance from the start of the path, in pixels.</param>
    /// <param name="point">The point of the path at that distance.</param>
    /// <param name="tangent">The direction of the path at that point, as a vector of length 1.</param>
    /// <returns><see langword="false"/> when the path has no length: there is no point to find, and no direction.</returns>
    /// <exception cref="ArgumentException"><paramref name="distance"/> is not a number.</exception>
    public bool TryGetPointAtDistance(double distance, out PointD point, out PointD tangent)
    {
        if (double.IsNaN(distance))
            throw new ArgumentException("The distance must be a number.", nameof(distance));

        if (!IsEmpty)
        {
            var sink = new DistanceSink(distance);
            PathFlattener.Flatten(this, sink);
            if (sink.IsFound)
            {
                point = new PointD(sink.PointX, sink.PointY);
                tangent = new PointD(sink.DirectionX, sink.DirectionY);
                return true;
            }
        }

        point = default;
        tangent = default;
        return false;
    }

    /// <summary>
    /// Gets the smallest rectangle containing the path itself: unlike <see cref="Bounds"/>, a curve counts for the points
    /// it reaches, not for its control points.
    /// </summary>
    /// <remarks>
    /// The rectangle is computed from the curves, not from their chords: it contains everything a fill of the path
    /// paints. It does not account for the width of a stroke: see <see cref="Widen"/>. Empty for an empty path.
    /// </remarks>
    /// <returns>The rectangle.</returns>
    public RectangleD GetTightBounds() => PathBounds.ComputeTight(_commands, _points);

    /// <summary>Creates the path a pen draws when it strokes this path: filling the result paints what the stroke paints.</summary>
    /// <remarks>
    /// <para>
    /// The result is the stroke as the library draws it: one closed convex contour for each segment, each join and
    /// each cap. The contours overlap and all turn the same way: fill them with <see cref="FillRule.NonZero"/>. It is not
    /// the outline of the stroke as a single contour, so stroking the result outlines every piece.
    /// </para>
    /// <para>
    /// Curves are widened along their chords, which stay within 1/16 of a pixel of the curve at the size the path has:
    /// transform a path before widening it, not after. The brush of the pen is not used.
    /// </para>
    /// <para>
    /// A dashed pen gives the contours of every dash: the size of the result grows with the length of the path divided by
    /// the length of the pattern, and nothing limits it.
    /// </para>
    /// </remarks>
    /// <param name="pen">The stroke.</param>
    /// <returns>The path of the stroke; an empty path when this path is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The stroke goes beyond the largest coordinate (16777216).</exception>
    public VectorPath Widen(Pen pen)
    {
        ArgumentNullException.ThrowIfNull(pen);
        var builder = new VectorPathBuilder();
        var stroker = new Stroker(builder);
        stroker.Configure(pen);
        if (pen.IsDashed)
        {
            using var dasher = new Dasher(scope: null);
            dasher.Configure(pen, stroker);
            PathFlattener.Flatten(this, dasher);
        }
        else
        {
            PathFlattener.Flatten(this, stroker);
        }

        var result = builder.Build();
        foreach (var value in result._points)
        {
            // Written so that a value that is not a number is rejected too
            if (!(Math.Abs(value) <= DrawingGuard.MaxCoordinate))
                throw new ArgumentOutOfRangeException(nameof(pen), "The stroke goes beyond the largest coordinate (16777216).");
        }

        return result;
    }

    /// <summary>Creates a path from the path data of SVG: the <c>d</c> attribute of a <c>path</c> element.</summary>
    /// <remarks>
    /// <para>
    /// Every command of SVG is read (<c>M L H V C S Q T A Z</c> and their relative forms), with the grammar of SVG:
    /// numbers separated by white space, by a comma or by nothing where there is no doubt, and the arguments of a
    /// command repeated without its letter. The coordinates are those of the canvas: Y grows downward, as in SVG.
    /// </para>
    /// <para>
    /// The whole text must be valid: nothing is drawn of path data that has an error, where a browser draws what comes
    /// before it. Every coordinate must be within the limit of a path (16777216 in magnitude).
    /// </para>
    /// </remarks>
    /// <param name="svgPathData">The path data. An empty text is an empty path.</param>
    /// <returns>The path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="svgPathData"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The text is not valid path data, or a coordinate is beyond the limit of a path.</exception>
    public static VectorPath ParseSvgPathData(string svgPathData)
    {
        ArgumentNullException.ThrowIfNull(svgPathData);
        return ParseSvgPathData(svgPathData.AsSpan());
    }

    /// <summary>Creates a path from the path data of SVG: the <c>d</c> attribute of a <c>path</c> element.</summary>
    /// <param name="svgPathData">The path data. An empty text is an empty path.</param>
    /// <returns>The path.</returns>
    /// <remarks>See <see cref="ParseSvgPathData(string)"/>.</remarks>
    /// <exception cref="FormatException">The text is not valid path data, or a coordinate is beyond the limit of a path.</exception>
    public static VectorPath ParseSvgPathData(ReadOnlySpan<char> svgPathData)
    {
        if (!SvgPathData.TryParse(svgPathData, out var path, out var position, out var error))
            throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"The path data is not valid at index {position}: {error}"));

        return path;
    }

    /// <summary>Creates a path from the path data of SVG, without throwing when the text is not valid.</summary>
    /// <param name="svgPathData">The path data. An empty text is an empty path.</param>
    /// <param name="path">The path, or <see langword="null"/> when the text is not valid.</param>
    /// <returns><see langword="false"/> when the text is <see langword="null"/>, is not valid path data, or has a coordinate beyond the limit of a path.</returns>
    /// <remarks>See <see cref="ParseSvgPathData(string)"/>.</remarks>
    public static bool TryParseSvgPathData([NotNullWhen(true)] string? svgPathData, [NotNullWhen(true)] out VectorPath? path)
    {
        if (svgPathData is null)
        {
            path = null;
            return false;
        }

        return SvgPathData.TryParse(svgPathData.AsSpan(), out path, out _, out _);
    }

    /// <summary>Creates a path from the path data of SVG, without throwing when the text is not valid.</summary>
    /// <param name="svgPathData">The path data. An empty text is an empty path.</param>
    /// <param name="path">The path, or <see langword="null"/> when the text is not valid.</param>
    /// <returns><see langword="false"/> when the text is not valid path data, or has a coordinate beyond the limit of a path.</returns>
    /// <remarks>See <see cref="ParseSvgPathData(string)"/>.</remarks>
    public static bool TryParseSvgPathData(ReadOnlySpan<char> svgPathData, [NotNullWhen(true)] out VectorPath? path)
        => SvgPathData.TryParse(svgPathData, out path, out _, out _);

    /// <summary>Writes the path as the path data of SVG: the <c>d</c> attribute of a <c>path</c> element.</summary>
    /// <remarks>
    /// The text has the segments of <see cref="EnumerateSegments"/>, with absolute coordinates, and numbers written so
    /// that reading them gives the same values, whatever the culture. Lines and Bézier curves are read back exactly;
    /// an arc is read back as the same arc to a few units of the last place.
    /// </remarks>
    /// <returns>The path data; an empty text for an empty path.</returns>
    public string ToSvgPathData() => SvgPathData.Format(this);

    /// <summary>Enumerates the elements of the path, in the order they were added to it.</summary>
    /// <remarks>
    /// <para>
    /// A segment is what a method of <see cref="VectorPathBuilder"/> adds: the path can be written in another format,
    /// or built again, from its segments. Every contour starts with a <see cref="PathSegmentKind.MoveTo"/>.
    /// </para>
    /// <para>
    /// An arc is given as the arc of SVG paths that <see cref="VectorPathBuilder.ArcTo(double, double, double, bool, bool, PointD)"/>
    /// takes, in pieces of at most a quarter turn, and an ellipse as two arcs of half a turn in a closed contour: the
    /// radii and the rotation are computed from the ellipse the path stores, and are those that were given to the
    /// builder to a few units of the last place only (exactly for an ellipse that is not turned). An arc of an ellipse
    /// that a transformation flattened to a segment is a <see cref="PathSegmentKind.LineTo"/>.
    /// </para>
    /// </remarks>
    /// <returns>An enumerator, to use with <see langword="foreach"/>. It does not allocate.</returns>
    public PathSegmentEnumerator EnumerateSegments() => new(this);

    /// <summary>Creates the path that goes through the points of this one from its end to its start.</summary>
    /// <remarks>
    /// The contours come in the opposite order, and each one is followed the other way: an open contour starts where
    /// it ended, and a closed contour keeps its first point. The result paints the same pixels when it is filled or
    /// stroked; what follows a path in its direction changes side: the dashes of a pen, the points at a distance, a
    /// text on the path, and what <see cref="FillRule.NonZero"/> counts when the path is combined with another one.
    /// </remarks>
    /// <returns>The reversed path; this instance when the path is empty.</returns>
    public VectorPath Reverse() => IsEmpty ? this : PathReverser.Reverse(_commands, _points, Bounds);

    /// <summary>Creates the part of the path between two distances from its start: what is left of a stroke that is drawn progressively.</summary>
    /// <remarks>
    /// <para>
    /// The distances are those of <see cref="GetLength"/> and <see cref="TryGetPointAtDistance"/>: the contours follow
    /// each other, without the gap between them. A distance before the start of the path is its start, a distance
    /// beyond its end is its end.
    /// </para>
    /// <para>
    /// The result is made of the chords the path is drawn with, which stay within 1/16 of a pixel of its curves at the
    /// size the path has: transform a path before taking a part of it, not after. The vertices of these chords are
    /// vertices of the result: a pen joins them as it joins any corner, where the stroke of the path itself is always
    /// round (the two only differ on curves a few pixels wide; <see cref="LineJoin.Round"/> makes them the same). A
    /// contour that is cut becomes an open contour; a closed contour that is kept entirely stays closed. A contour
    /// without length is not kept.
    /// </para>
    /// </remarks>
    /// <param name="start">The distance from the start of the path at which the part starts, in pixels.</param>
    /// <param name="end">The distance from the start of the path at which the part ends, in pixels.</param>
    /// <returns>The part of the path; an empty path when <paramref name="end"/> is not after <paramref name="start"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="start"/> or <paramref name="end"/> is not a number.</exception>
    public VectorPath Trim(double start, double end)
    {
        if (double.IsNaN(start))
            throw new ArgumentException("The distance must be a number.", nameof(start));

        if (double.IsNaN(end))
            throw new ArgumentException("The distance must be a number.", nameof(end));

        var builder = new VectorPathBuilder();
        if (!IsEmpty && start < end)
        {
            PathFlattener.Flatten(this, new TrimSink(builder, start, end));
        }

        return builder.Build();
    }

    /// <summary>Gets the smallest rectangle containing every point and control point of a list of elements.</summary>
    internal static RectangleD ComputeBounds(ReadOnlySpan<PathCommand> commands, ReadOnlySpan<double> points)
    {
        if (commands.IsEmpty)
            return RectangleD.Empty;

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        var index = 0;
        foreach (var command in commands)
        {
            switch (command)
            {
                case PathCommand.MoveTo or PathCommand.LineTo:
                    Include(points[index], points[index + 1], 0, 0);
                    index += 2;
                    break;

                case PathCommand.QuadraticTo:
                    Include(points[index], points[index + 1], 0, 0);
                    Include(points[index + 2], points[index + 3], 0, 0);
                    index += 4;
                    break;

                case PathCommand.CubicTo:
                    Include(points[index], points[index + 1], 0, 0);
                    Include(points[index + 2], points[index + 3], 0, 0);
                    Include(points[index + 4], points[index + 5], 0, 0);
                    index += 6;
                    break;

                case PathCommand.Ellipse:
                    Include(points[index], points[index + 1], points[index + 2], points[index + 3]);
                    index += 4;
                    break;

                case PathCommand.EllipseAxes:
                    // The ellipse is inside the parallelogram of its two half-diameters
                    Include(points[index], points[index + 1], Math.Abs(points[index + 2]) + Math.Abs(points[index + 4]), Math.Abs(points[index + 3]) + Math.Abs(points[index + 5]));
                    index += 6;
                    break;

                case PathCommand.Arc or PathCommand.ArcContinuation:
                    // An arc is inside the triangle of its ends and of the point where their tangents meet
                    new ArcPiece(points.Slice(index, ArcPiece.CoordinateCount), isContinuation: false).GetCorner(out var cornerX, out var cornerY);
                    Include(cornerX, cornerY, 0, 0);
                    Include(points[index + 10], points[index + 11], 0, 0);
                    index += ArcPiece.CoordinateCount;
                    break;
            }
        }

        return RectangleD.FromLTRB(minX, minY, maxX, maxY);

        void Include(double x, double y, double extentX, double extentY)
        {
            minX = Math.Min(minX, x - extentX);
            minY = Math.Min(minY, y - extentY);
            maxX = Math.Max(maxX, x + extentX);
            maxY = Math.Max(maxY, y + extentY);
        }
    }

    /// <summary>Gets the elements.</summary>
    internal ReadOnlySpan<PathCommand> Commands => _commands;

    /// <summary>Gets the coordinates consumed by the elements, as consecutive x and y values.</summary>
    internal ReadOnlySpan<double> Points => _points;
}
