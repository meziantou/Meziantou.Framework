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
