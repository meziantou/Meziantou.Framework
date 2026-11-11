namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// The vertices of a path, where the markers are drawn, with the direction of the path there, as SVG computes them
/// for <c>orient="auto"</c> (SVG 2, 11.6.2): the end point of each command, the start of a closed subpath again where
/// it closes.
/// </summary>
internal static class PathVertices
{
    public static PathPoint[] Get(PathData path)
    {
        var vertices = new List<(double X, double Y)>();

        // The directions of the segment that ends at each vertex, and of the one that starts there; null when there is none
        var incoming = new List<double?>();
        var outgoing = new List<double?>();
        var subpathStart = -1;
        foreach (var command in path.Commands)
        {
            if (command.Kind is PathCommandKind.MoveTo)
            {
                AddVertex(command.X, command.Y, incomingAngle: null);
                subpathStart = vertices.Count - 1;
                continue;
            }

            // A segment without a move starts at the origin; after a closed subpath, where it closed
            if (vertices.Count is 0)
            {
                AddVertex(0, 0, incomingAngle: null);
                subpathStart = 0;
            }
            else if (subpathStart < 0)
            {
                subpathStart = vertices.Count - 1;
            }

            var (x, y) = vertices[^1];
            var (endX, endY) = command.Kind is PathCommandKind.Close ? vertices[subpathStart] : (command.X, command.Y);
            var (start, end) = GetDirections(command, x, y, endX, endY);
            if (start is not null)
            {
                outgoing[^1] = start;
            }

            // A segment without length keeps the direction of the previous one
            AddVertex(endX, endY, end ?? incoming[^1]);
            if (command.Kind is PathCommandKind.Close)
            {
                // The vertex where a subpath closes joins its last segment and its first one, and so does its start
                outgoing[^1] = outgoing[subpathStart];
                incoming[subpathStart] = incoming[^1];
                subpathStart = -1;
            }
        }

        var result = new PathPoint[vertices.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var angle = (incoming[i], outgoing[i]) switch
            {
                ({ } inAngle, { } outAngle) => Bisector(inAngle, outAngle),
                ({ } inAngle, null) => inAngle,
                (null, { } outAngle) => outAngle,
                _ => 0,
            };

            result[i] = new PathPoint(vertices[i].X, vertices[i].Y, angle);
        }

        return result;

        void AddVertex(double vertexX, double vertexY, double? incomingAngle)
        {
            vertices.Add((vertexX, vertexY));
            incoming.Add(incomingAngle);
            outgoing.Add(null);
        }
    }

    // The direction of a segment at its start and at its end: the tangents of a curve, the direction of a line; null
    // for a segment without length
    private static (double? Start, double? End) GetDirections(PathCommand command, double x0, double y0, double x1, double y1)
    {
        switch (command.Kind)
        {
            case PathCommandKind.CubicTo:
                return (Direction(x0, y0, command.X1, command.Y1) ?? Direction(x0, y0, command.X2, command.Y2) ?? Direction(x0, y0, x1, y1),
                    Direction(command.X2, command.Y2, x1, y1) ?? Direction(command.X1, command.Y1, x1, y1) ?? Direction(x0, y0, x1, y1));

            case PathCommandKind.QuadraticTo:
                return (Direction(x0, y0, command.X1, command.Y1) ?? Direction(x0, y0, x1, y1), Direction(command.X1, command.Y1, x1, y1) ?? Direction(x0, y0, x1, y1));

            case PathCommandKind.ArcTo:
                // The tangents of the cubic curves the arc is made of
                var curves = new PathData([new PathCommand(PathCommandKind.MoveTo, x0, y0), command]).ToCubicCurves().Commands;
                if (curves.Count < 2)
                    return (null, null);

                var (first, last) = (curves[1], curves[^1]);
                var lastStart = curves.Count > 2 ? (curves[^2].X, curves[^2].Y) : (x0, y0);
                return first.Kind is PathCommandKind.CubicTo
                    ? (GetDirections(first, x0, y0, first.X, first.Y).Start, GetDirections(last, lastStart.Item1, lastStart.Item2, last.X, last.Y).End)
                    : (Direction(x0, y0, x1, y1), Direction(x0, y0, x1, y1));

            default:
                var direction = Direction(x0, y0, x1, y1);
                return (direction, direction);
        }
    }

    private static double? Direction(double x0, double y0, double x1, double y1)
    {
        var (dx, dy) = (x1 - x0, y1 - y0);
        if ((dx == 0 && dy == 0) || !double.IsFinite(dx) || !double.IsFinite(dy))
            return null;

        return Math.Atan2(dy, dx) * 180 / Math.PI;
    }

    // The direction halfway between two directions, by the shorter turn
    private static double Bisector(double first, double second) => Math.IEEERemainder(first + (Math.IEEERemainder(second - first, 360) / 2), 360);
}
