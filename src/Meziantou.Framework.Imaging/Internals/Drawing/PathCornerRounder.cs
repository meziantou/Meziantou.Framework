using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Replaces the corners between two straight segments of a path by arcs of a circle.</summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> A corner is a vertex of a contour between two straight segments that have a length and do not
/// continue each other: the vertices of an open contour between its ends, and every vertex of a closed contour, the
/// first one and the closing segment included. With <c>d0</c> and <c>d1</c> the unit vectors of the segment that
/// arrives and of the one that leaves, the circle of radius <c>r</c> that touches both does so at the distance
/// <c>t = r |d0 x d1| / (1 + d0.d1)</c> from the vertex (<c>r tan(turn / 2)</c>). That distance is limited to half the
/// length of the shorter of the two segments, and the radius is then the one that distance gives: two corners never
/// meet on a segment. The corner becomes the arc from <c>V - t d0</c> to <c>V + t d1</c>, which turns the way the
/// contour turns there.
/// </para>
/// <para>
/// A segment that is a curve or an arc is kept as it is, and so is a vertex it starts or ends at; a vertex where the
/// contour goes back on itself has no circle that touches both segments, and is kept too. A point that two corners
/// share, the middle of a segment, is computed once: no segment of a few units of the last place is left between two
/// arcs, which a stroke would turn into a spike.
/// </para>
/// </remarks>
internal static class PathCornerRounder
{
    public static VectorPath Round(VectorPath path, double radius)
    {
        var commands = path.Commands;
        var points = path.Points;
        var builder = new VectorPathBuilder();
        var sides = new List<Side>();
        var index = 0;
        var i = 0;
        while (i < commands.Length)
        {
            var command = commands[i];
            if (command is PathCommand.Ellipse or PathCommand.EllipseAxes)
            {
                var count = GetCoordinateCount(command);
                builder.AppendElement(command, points.Slice(index, count));
                index += count;
                i++;
                continue;
            }

            // A contour: its first point, its elements, and whether it is closed
            var startX = points[index];
            var startY = points[index + 1];
            index += 2;
            i++;
            sides.Clear();
            var x = startX;
            var y = startY;
            var isClosed = false;
            for (; i < commands.Length; i++)
            {
                command = commands[i];
                if (command is PathCommand.MoveTo or PathCommand.Ellipse or PathCommand.EllipseAxes)
                    break;

                if (command == PathCommand.Close)
                {
                    isClosed = true;
                    i++;
                    break;
                }

                var count = GetCoordinateCount(command);
                var endX = points[index + count - 2];
                var endY = points[index + count - 1];

                // A straight segment without length is no side of a corner
                if (command != PathCommand.LineTo || endX != x || endY != y)
                {
                    sides.Add(new Side(command, index, x, y, endX, endY));
                }

                index += count;
                x = endX;
                y = endY;
            }

            var hasClosingSide = isClosed && (x != startX || y != startY);
            if (hasClosingSide)
            {
                sides.Add(new Side(PathCommand.LineTo, -1, x, y, startX, startY));
            }

            AppendContour(builder, points, sides, startX, startY, isClosed, hasClosingSide, radius);
        }

        return builder.Build();
    }

    private static void AppendContour(VectorPathBuilder builder, ReadOnlySpan<double> points, List<Side> sides, double startX, double startY, bool isClosed, bool hasClosingSide, double radius)
    {
        var count = sides.Count;
        if (count == 0)
        {
            builder.AppendMoveTo(startX, startY);
            if (isClosed)
            {
                builder.Close();
            }

            return;
        }

        // The corner at the first point of a closed contour: the contour then starts where its arc ends
        var first = default(Fillet);
        var hasFirst = isClosed && TryGetFillet(sides[count - 1], sides[0], radius, out first);
        if (hasFirst)
        {
            builder.AppendMoveTo(first.EndX, first.EndY);
        }
        else
        {
            builder.AppendMoveTo(startX, startY);
        }

        var currentX = hasFirst ? first.EndX : startX;
        var currentY = hasFirst ? first.EndY : startY;
        for (var j = 0; j < count; j++)
        {
            var side = sides[j];
            var isLast = j == count - 1;
            var fillet = default(Fillet);
            var hasFillet = isLast ? hasFirst : TryGetFillet(side, sides[j + 1], radius, out fillet);
            if (isLast && hasFirst)
            {
                fillet = first;
            }

            if (side.Command != PathCommand.LineTo)
            {
                builder.AppendElement(side.Command, points.Slice(side.Index, GetCoordinateCount(side.Command)));
                currentX = side.EndX;
                currentY = side.EndY;
                continue;
            }

            var targetX = hasFillet ? fillet.StartX : side.EndX;
            var targetY = hasFillet ? fillet.StartY : side.EndY;

            // The segment that closes a contour is added by the builder when it ends where the contour started
            var isImplicit = isLast && hasClosingSide && !hasFillet;
            if (!isImplicit && (targetX != currentX || targetY != currentY))
            {
                builder.AppendLineTo(targetX, targetY);
            }

            if (hasFillet)
            {
                builder.ArcTo(fillet.Radius, fillet.Radius, rotation: 0, largeArc: false, fillet.IsClockwise, fillet.EndX, fillet.EndY);
                currentX = fillet.EndX;
                currentY = fillet.EndY;
            }
            else
            {
                currentX = targetX;
                currentY = targetY;
            }
        }

        if (isClosed)
        {
            builder.Close();
        }
    }

    /// <summary>Gets the arc that replaces the vertex between two sides, when both are straight and make a corner.</summary>
    private static bool TryGetFillet(in Side arriving, in Side leaving, double radius, out Fillet fillet)
    {
        fillet = default;
        if (arriving.Command != PathCommand.LineTo || leaving.Command != PathCommand.LineTo)
            return false;

        var d0x = arriving.EndX - arriving.StartX;
        var d0y = arriving.EndY - arriving.StartY;
        var d1x = leaving.EndX - leaving.StartX;
        var d1y = leaving.EndY - leaving.StartY;
        var length0 = Math.Sqrt((d0x * d0x) + (d0y * d0y));
        var length1 = Math.Sqrt((d1x * d1x) + (d1y * d1y));
        d0x /= length0;
        d0y /= length0;
        d1x /= length1;
        d1y /= length1;
        var cross = (d0x * d1y) - (d0y * d1x);
        var dot = (d0x * d1x) + (d0y * d1y);

        // No turn, or a turn back: no circle touches both segments. Written so that a value that is not a number
        // makes no corner.
        var tangent = Math.Abs(cross) / (1 + dot);
        if (!(tangent > 0) || !double.IsFinite(tangent))
            return false;

        // The distance from the vertex to the points where the circle touches the segments: half of the shorter
        // segment at most, and the radius that goes with it
        var distance = radius * tangent;
        var limit = Math.Min(length0, length1) / 2;
        var arcRadius = radius;
        if (!(distance <= limit))
        {
            distance = limit;
            arcRadius = distance / tangent;
        }

        if (!(distance > 0) || !(arcRadius > 0))
            return false;

        // Half of a segment is its middle, whichever of its ends the distance is counted from
        var vertexX = arriving.EndX;
        var vertexY = arriving.EndY;
        var startX = distance == length0 / 2 ? (arriving.StartX + arriving.EndX) / 2 : vertexX - (distance * d0x);
        var startY = distance == length0 / 2 ? (arriving.StartY + arriving.EndY) / 2 : vertexY - (distance * d0y);
        var endX = distance == length1 / 2 ? (leaving.StartX + leaving.EndX) / 2 : vertexX + (distance * d1x);
        var endY = distance == length1 / 2 ? (leaving.StartY + leaving.EndY) / 2 : vertexY + (distance * d1y);
        fillet = new Fillet(startX, startY, endX, endY, arcRadius, cross > 0);
        return true;
    }

    private static int GetCoordinateCount(PathCommand command) => command switch
    {
        PathCommand.MoveTo or PathCommand.LineTo => 2,
        PathCommand.QuadraticTo or PathCommand.Ellipse => 4,
        PathCommand.CubicTo or PathCommand.EllipseAxes => 6,
        PathCommand.Arc or PathCommand.ArcContinuation => ArcPiece.CoordinateCount,
        _ => 0,
    };

    /// <summary>An element of a contour: a straight segment (the one that closes the contour has no coordinates of its own), a curve or an arc.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Side(PathCommand Command, int Index, double StartX, double StartY, double EndX, double EndY);

    /// <summary>The arc of a corner: where it starts and ends, its radius and the way it turns.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Fillet(double StartX, double StartY, double EndX, double EndY, double Radius, bool IsClockwise);
}
