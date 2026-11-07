using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Reverses a path: its contours in the opposite order, each one followed from its end.</summary>
/// <remarks>
/// The elements are reversed as they are: a curve keeps its control points, an arc its ellipse with its two ends
/// exchanged, and a whole ellipse turns the other way from the same first point. An open contour starts at its last
/// point. A closed contour keeps its first point: its closing segment comes first, as a segment, and the segment that
/// started the contour ends at its first point (a straight one becomes the closing segment, so that reversing a
/// polygon twice gives the polygon).
/// </remarks>
internal static class PathReverser
{
    public static VectorPath Reverse(ReadOnlySpan<PathCommand> commands, ReadOnlySpan<double> points, RectangleD bounds)
    {
        var resultCommands = new List<PathCommand>(commands.Length + 1);
        var resultPoints = new List<double>(points.Length + 2);

        // The contours, from the last one: each starts at an element that begins a contour
        var end = commands.Length;
        var pointEnd = points.Length;
        while (end > 0)
        {
            var first = end - 1;
            var pointStart = pointEnd - GetCoordinateCount(commands[first]);
            while (commands[first] is not (PathCommand.MoveTo or PathCommand.Ellipse or PathCommand.EllipseAxes))
            {
                first--;
                pointStart -= GetCoordinateCount(commands[first]);
            }

            ReverseContour(commands[first..end], points[pointStart..pointEnd], resultCommands, resultPoints);
            end = first;
            pointEnd = pointStart;
        }

        return new VectorPath([.. resultCommands], [.. resultPoints], bounds);
    }

    private static void ReverseContour(ReadOnlySpan<PathCommand> commands, ReadOnlySpan<double> points, List<PathCommand> resultCommands, List<double> resultPoints)
    {
        switch (commands[0])
        {
            case PathCommand.Ellipse:
                // The same ellipse, from the same point, turning toward the opposite of its second half-diameter
                resultCommands.Add(PathCommand.EllipseAxes);
                resultPoints.AddRange([points[0], points[1], points[2], 0, 0, -points[3]]);
                return;

            case PathCommand.EllipseAxes:
                resultCommands.Add(PathCommand.EllipseAxes);
                resultPoints.AddRange([points[0], points[1], points[2], points[3], -points[4], -points[5]]);
                return;
        }

        var isClosed = commands[^1] == PathCommand.Close;
        var segmentCount = commands.Length - 1 - (isClosed ? 1 : 0);
        var startX = points[0];
        var startY = points[1];
        var lastX = points[^2];
        var lastY = points[^1];
        resultCommands.Add(PathCommand.MoveTo);
        if (isClosed)
        {
            // The closing segment comes first, as a segment
            resultPoints.Add(startX);
            resultPoints.Add(startY);
            if (lastX != startX || lastY != startY)
            {
                resultCommands.Add(PathCommand.LineTo);
                resultPoints.Add(lastX);
                resultPoints.Add(lastY);
            }
        }
        else
        {
            resultPoints.Add(lastX);
            resultPoints.Add(lastY);
        }

        // The segments from the last one. The coordinates of the element before a segment end with the point it starts at.
        var offset = points.Length;
        for (var i = segmentCount; i >= 1; i--)
        {
            var command = commands[i];
            var count = GetCoordinateCount(command);
            offset -= count;
            var segment = points.Slice(offset, count);
            switch (command)
            {
                case PathCommand.LineTo:
                    // The straight segment that started a closed contour is the one that closes its reverse
                    if (i == 1 && isClosed)
                        continue;

                    resultCommands.Add(PathCommand.LineTo);
                    break;

                case PathCommand.QuadraticTo:
                    resultCommands.Add(PathCommand.QuadraticTo);
                    resultPoints.Add(segment[0]);
                    resultPoints.Add(segment[1]);
                    break;

                case PathCommand.CubicTo:
                    resultCommands.Add(PathCommand.CubicTo);
                    resultPoints.AddRange([segment[2], segment[3], segment[0], segment[1]]);
                    break;

                default:
                    // An arc continues the arc before it when, in the path, the arc after it continued it
                    var continues = i < segmentCount && commands[i + 1] == PathCommand.ArcContinuation;
                    resultCommands.Add(continues ? PathCommand.ArcContinuation : PathCommand.Arc);
                    resultPoints.AddRange([segment[0], segment[1], segment[2], segment[3], segment[4], segment[5], segment[8], segment[9], segment[6], segment[7]]);
                    break;
            }

            resultPoints.Add(points[offset - 2]);
            resultPoints.Add(points[offset - 1]);
        }

        if (isClosed)
        {
            resultCommands.Add(PathCommand.Close);
        }
    }

    private static int GetCoordinateCount(PathCommand command) => command switch
    {
        PathCommand.MoveTo or PathCommand.LineTo => 2,
        PathCommand.QuadraticTo or PathCommand.Ellipse => 4,
        PathCommand.CubicTo or PathCommand.EllipseAxes => 6,
        PathCommand.Close => 0,
        _ => ArcPiece.CoordinateCount,
    };
}
