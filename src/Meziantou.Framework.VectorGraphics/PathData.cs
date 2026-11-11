namespace Meziantou.Framework.VectorGraphics;

/// <summary>The geometry of a <see cref="ScenePath"/>: a list of commands, as in SVG.</summary>
public sealed class PathData
{
    internal PathData(PathCommand[] commands)
    {
        Commands = commands;
    }

    /// <summary>Gets the commands of the path.</summary>
    public IReadOnlyList<PathCommand> Commands { get; }

    /// <summary>Gets a value that indicates whether the path has no commands.</summary>
    public bool IsEmpty => Commands.Count is 0;

    private PathMeasure Measure => field ??= new PathMeasure(this);

    /// <summary>Gets the length of the path: the lengths of its subpaths, without the moves between them.</summary>
    /// <returns>The length.</returns>
    /// <remarks>The curves and the arcs are measured by their chords, within 1/100 of a pixel. A segment with a coordinate that is not finite has no length.</remarks>
    public double GetLength() => Measure.Length;

    /// <summary>Gets the point at a distance along the path, and the direction of the path there, as SVG <c>getPointAtLength</c>.</summary>
    /// <param name="distance">The distance from the start of the path. It is clamped to the path; a value that is not a number is 0.</param>
    /// <returns>
    /// The point. At the end of a segment, the direction is the one of the next segment; at the end of the path, the one
    /// of its last segment. A path without length gives its first point, with a direction of 0.
    /// </returns>
    /// <exception cref="InvalidOperationException">The path is empty.</exception>
    public PathPoint GetPointAtLength(double distance) => Measure.GetPoint(distance) ?? throw new InvalidOperationException("The path is empty: it has no point.");

    /// <summary>
    /// Gets the vertices of the path, where its markers are drawn (<see cref="ScenePath.MarkerStart"/>,
    /// <see cref="ScenePath.MarkerMid"/>, <see cref="ScenePath.MarkerEnd"/>), with the direction of the path there, as SVG
    /// computes them for <c>orient="auto"</c>.
    /// </summary>
    /// <returns>
    /// One vertex per command: the end point of each command, and the start of its subpath for a
    /// <see cref="PathCommandKind.Close"/> command. A path whose first command is not a move starts with a vertex at the
    /// origin. The start marker is on the first vertex, the end marker on the last one, the middle marker on the others.
    /// </returns>
    /// <remarks>
    /// The direction at a vertex is halfway between the directions of the segment that ends there and of the one that
    /// starts there, or the direction of the only one; the tangents of the curves and the arcs. A closed subpath joins
    /// its last segment and its first one at its start. A segment without length keeps the direction of the previous one.
    /// </remarks>
    public IReadOnlyList<PathPoint> GetVertices() => PathVertices.Get(this);

    /// <summary>Returns the path moved by an offset. The radii and the rotations of the arcs do not change.</summary>
    /// <param name="offsetX">The offset along x.</param>
    /// <param name="offsetY">The offset along y.</param>
    /// <returns>The moved path.</returns>
    public PathData Translate(double offsetX, double offsetY)
    {
        var commands = new PathCommand[Commands.Count];
        for (var i = 0; i < commands.Length; i++)
        {
            var command = Commands[i];
            commands[i] = command.Kind switch
            {
                PathCommandKind.MoveTo or PathCommandKind.LineTo or PathCommandKind.ArcTo => command with { X = command.X + offsetX, Y = command.Y + offsetY },
                PathCommandKind.QuadraticTo => command with { X = command.X + offsetX, Y = command.Y + offsetY, X1 = command.X1 + offsetX, Y1 = command.Y1 + offsetY },
                PathCommandKind.CubicTo => command with { X = command.X + offsetX, Y = command.Y + offsetY, X1 = command.X1 + offsetX, Y1 = command.Y1 + offsetY, X2 = command.X2 + offsetX, Y2 = command.Y2 + offsetY },
                _ => command,
            };
        }

        return new(commands);
    }

    /// <summary>
    /// Returns the path drawn backwards: its subpaths in the reverse order, each from its end to its start, as SVG 2
    /// reverses the path of a <c>textPath</c> whose <c>side</c> is <c>right</c>.
    /// </summary>
    /// <returns>The reversed path, which covers the same points.</returns>
    /// <remarks>
    /// Each subpath starts with a move to its last point, then follows its segments back: the control points of the
    /// curves are swapped, and the arcs turn the other way (their <see cref="PathCommand.Sweep"/> flag is inverted). A
    /// closed subpath starts at the end of its last segment and closes at the same point. A path that does not start with
    /// a move starts at the origin, and a segment that follows a <see cref="PathCommandKind.Close"/> command starts at the
    /// start of the closed subpath, as in SVG: the reversed path has explicit moves to these points. Reversing the
    /// reversed path gives the original geometry.
    /// </remarks>
    public PathData Reverse()
    {
        // The subpaths: their start, their segments, and whether they are closed
        var subpaths = new List<(double X, double Y, List<PathCommand> Segments, bool Closed)>();
        List<PathCommand>? segments = null;
        var (startX, startY) = (0.0, 0.0);
        foreach (var command in Commands)
        {
            switch (command.Kind)
            {
                case PathCommandKind.MoveTo:
                    segments = [];
                    (startX, startY) = (command.X, command.Y);
                    subpaths.Add((startX, startY, segments, false));
                    break;

                case PathCommandKind.Close:
                    if (segments is null)
                    {
                        segments = [];
                        subpaths.Add((startX, startY, segments, false));
                    }

                    subpaths[^1] = subpaths[^1] with { Closed = true };
                    segments = null;
                    break;

                default:
                    if (segments is null)
                    {
                        segments = [];
                        subpaths.Add((startX, startY, segments, false));
                    }

                    segments.Add(command);
                    break;
            }
        }

        var commands = new List<PathCommand>(Commands.Count + subpaths.Count);
        for (var i = subpaths.Count - 1; i >= 0; i--)
        {
            var (x, y, subpathSegments, closed) = subpaths[i];
            var (endX, endY) = subpathSegments.Count is 0 ? (x, y) : (subpathSegments[^1].X, subpathSegments[^1].Y);
            commands.Add(new PathCommand(PathCommandKind.MoveTo, endX, endY));
            for (var j = subpathSegments.Count - 1; j >= 0; j--)
            {
                var segment = subpathSegments[j];
                var (previousX, previousY) = j is 0 ? (x, y) : (subpathSegments[j - 1].X, subpathSegments[j - 1].Y);
                commands.Add(segment.Kind switch
                {
                    PathCommandKind.CubicTo => segment with { X = previousX, Y = previousY, X1 = segment.X2, Y1 = segment.Y2, X2 = segment.X1, Y2 = segment.Y1 },
                    PathCommandKind.ArcTo => segment with { X = previousX, Y = previousY, Sweep = !segment.Sweep },
                    _ => segment with { X = previousX, Y = previousY },
                });
            }

            if (closed)
            {
                commands.Add(new PathCommand(PathCommandKind.Close, 0, 0));
            }
        }

        return new([.. commands]);
    }

    /// <summary>
    /// Returns the path with its quadratic curves and its arcs converted to cubic Bézier curves, for the formats that
    /// have no other curve, such as PDF and PostScript. The other commands are kept.
    /// </summary>
    /// <returns>The path, made of <see cref="PathCommandKind.MoveTo"/>, <see cref="PathCommandKind.LineTo"/>, <see cref="PathCommandKind.CubicTo"/> and <see cref="PathCommandKind.Close"/> commands.</returns>
    /// <remarks>
    /// A quadratic curve becomes the cubic curve of the same shape. An arc is resolved as SVG resolves it (SVG 1.1,
    /// appendix F.6): a radius of 0 gives a line, radii too small for the end points are scaled up, and an arc that ends
    /// where it starts is omitted. It is approximated by one cubic curve per quarter of a turn at most.
    /// </remarks>
    public PathData ToCubicCurves()
    {
        var commands = new List<PathCommand>(Commands.Count);
        var (x, y) = (0.0, 0.0);
        var (startX, startY) = (0.0, 0.0);
        foreach (var command in Commands)
        {
            switch (command.Kind)
            {
                case PathCommandKind.MoveTo:
                    commands.Add(command);
                    (x, y) = (startX, startY) = (command.X, command.Y);
                    break;

                case PathCommandKind.LineTo or PathCommandKind.CubicTo:
                    commands.Add(command);
                    (x, y) = (command.X, command.Y);
                    break;

                case PathCommandKind.QuadraticTo:
                    commands.Add(new PathCommand(
                        PathCommandKind.CubicTo, command.X, command.Y,
                        x + (2.0 / 3 * (command.X1 - x)), y + (2.0 / 3 * (command.Y1 - y)),
                        command.X + (2.0 / 3 * (command.X1 - command.X)), command.Y + (2.0 / 3 * (command.Y1 - command.Y))));
                    (x, y) = (command.X, command.Y);
                    break;

                case PathCommandKind.ArcTo:
                    AddArc(commands, x, y, command);
                    (x, y) = (command.X, command.Y);
                    break;

                case PathCommandKind.Close:
                    commands.Add(command);
                    (x, y) = (startX, startY);
                    break;
            }
        }

        return new([.. commands]);
    }

    // SVG 1.1, F.6.5 "Conversion from endpoint to center parameterization", then one cubic curve per quarter of a turn at most
    private static void AddArc(List<PathCommand> commands, double x1, double y1, PathCommand arc)
    {
        var (x2, y2) = (arc.X, arc.Y);
        if (x1 == x2 && y1 == y2)
            return;

        var rx = Math.Abs(arc.X1);
        var ry = Math.Abs(arc.Y1);
        if (rx == 0 || ry == 0)
        {
            commands.Add(new PathCommand(PathCommandKind.LineTo, x2, y2));
            return;
        }

        var phi = arc.X2 * Math.PI / 180;
        var (sin, cos) = Math.SinCos(phi);
        var dx = (x1 - x2) / 2;
        var dy = (y1 - y2) / 2;
        var x1p = (cos * dx) + (sin * dy);
        var y1p = (-sin * dx) + (cos * dy);

        // F.6.6: radii too small to join the end points are scaled up
        var lambda = (x1p * x1p / (rx * rx)) + (y1p * y1p / (ry * ry));
        if (lambda > 1)
        {
            var scale = Math.Sqrt(lambda);
            rx *= scale;
            ry *= scale;
        }

        var numerator = (rx * rx * ry * ry) - (rx * rx * y1p * y1p) - (ry * ry * x1p * x1p);
        var denominator = (rx * rx * y1p * y1p) + (ry * ry * x1p * x1p);
        var coefficient = Math.Sqrt(Math.Max(0, numerator / denominator)) * (arc.LargeArc == arc.Sweep ? -1 : 1);
        var cxp = coefficient * rx * y1p / ry;
        var cyp = -coefficient * ry * x1p / rx;
        var cx = (cos * cxp) - (sin * cyp) + ((x1 + x2) / 2);
        var cy = (sin * cxp) + (cos * cyp) + ((y1 + y2) / 2);

        var theta = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
        var delta = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx) - theta;
        if (arc.Sweep && delta < 0)
        {
            delta += 2 * Math.PI;
        }
        else if (!arc.Sweep && delta > 0)
        {
            delta -= 2 * Math.PI;
        }

        if (!double.IsFinite(delta) || !double.IsFinite(cx) || !double.IsFinite(cy))
        {
            commands.Add(new PathCommand(PathCommandKind.LineTo, x2, y2));
            return;
        }

        var count = Math.Max(1, (int)Math.Ceiling((Math.Abs(delta) / (Math.PI / 2)) - 1e-9));
        var step = delta / count;
        var t = 4.0 / 3 * Math.Tan(step / 4);
        for (var i = 0; i < count; i++)
        {
            var (sin0, cos0) = Math.SinCos(theta + (i * step));
            var (sin1, cos1) = Math.SinCos(theta + ((i + 1) * step));
            var (c1x, c1y) = Map(cos0 - (t * sin0), sin0 + (t * cos0));
            var (c2x, c2y) = Map(cos1 + (t * sin1), sin1 - (t * cos1));
            var (ex, ey) = i == count - 1 ? (x2, y2) : Map(cos1, sin1);
            commands.Add(new PathCommand(PathCommandKind.CubicTo, ex, ey, c1x, c1y, c2x, c2y));
        }

        (double X, double Y) Map(double ux, double uy) => (cx + (rx * cos * ux) - (ry * sin * uy), cy + (rx * sin * ux) + (ry * cos * uy));
    }

    /// <summary>Returns the path as the value of the <c>d</c> attribute of an SVG <c>path</c>.</summary>
    /// <returns>The path data.</returns>
    public override string ToString() => SvgNumber.FormatPath(this, SvgRenderOptions.DefaultPrecision);
}
