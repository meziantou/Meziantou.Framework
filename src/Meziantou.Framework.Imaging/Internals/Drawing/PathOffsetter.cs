using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Computes the outline of the area a path fills, moved outward or inward by a distance.</summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> With <c>A</c> the area the path fills and <c>S</c> the stroke of the boundary of <c>A</c> by a pen
/// twice as wide as the distance, with the join that is asked for, the result is the outline of <c>A</c> and <c>S</c>
/// together for a positive distance, and of <c>A</c> without <c>S</c> for a negative one. With round joins, these are
/// the points within the distance of the area, and the points of the area further than the distance from what is
/// outside it.
/// </para>
/// <para>
/// <b>Method.</b> The boundary of the area is its outline (<see cref="PathCombiner"/>): the contours of a path can
/// overlap, and those that are inside the area are no part of its boundary. The outline is made of the chords of the
/// path: a vertex that the flattening of a curve created is stroked with a round join, as the stroke of the path
/// itself is, so that a curve that is moved stays a curve. Those vertices are recognized by their position on the
/// grid of the outline; a vertex that is also a corner of the path is a corner.
/// </para>
/// </remarks>
internal static class PathOffsetter
{
    public static VectorPath Offset(VectorPath path, double distance, LineJoin join, double miterLimit, FillRule fillRule, CancellationToken cancellationToken)
    {
        var outline = PathCombiner.Combine(path, second: null, PathCombineMode.Union, fillRule, cancellationToken);
        if (distance == 0 || outline.IsEmpty)
            return outline;

        var vertices = new VertexCollector();
        PathFlattener.Flatten(path, vertices);

        // The stroke of the outline: every contour is closed, and has no cap
        var builder = new VectorPathBuilder();
        var stroker = new Stroker(builder);
        stroker.Configure(2 * Math.Abs(distance), join, miterLimit);
        var commands = outline.Commands;
        var points = outline.Points;
        var index = 0;
        var isFirstSegment = false;
        double startX = 0, startY = 0, previousX = 0, previousY = 0;
        foreach (var command in commands)
        {
            switch (command)
            {
                case PathCommand.MoveTo:
                    startX = previousX = points[index];
                    startY = previousY = points[index + 1];
                    index += 2;
                    stroker.MoveTo(startX, startY);
                    isFirstSegment = true;
                    break;

                case PathCommand.LineTo:
                    // What is smooth is the vertex the segment starts at; the first one is joined when the contour closes
                    stroker.LineTo(points[index], points[index + 1], !isFirstSegment && vertices.IsSmooth(previousX, previousY));
                    previousX = points[index];
                    previousY = points[index + 1];
                    index += 2;
                    isFirstSegment = false;
                    break;

                case PathCommand.Close:
                    stroker.LineTo(startX, startY, !isFirstSegment && vertices.IsSmooth(previousX, previousY));
                    stroker.Close(vertices.IsSmooth(startX, startY));
                    break;
            }
        }

        stroker.End();
        var band = builder.Build();
        foreach (var value in band.Points)
        {
            // Written so that a value that is not a number is rejected too
            if (!(Math.Abs(value) <= DrawingGuard.MaxCoordinate))
                throw new ArgumentOutOfRangeException(nameof(distance), distance, "The path goes beyond the largest coordinate (16777216) once it is moved.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return PathCombiner.Combine(outline, band, distance > 0 ? PathCombineMode.Union : PathCombineMode.Exclude, FillRule.NonZero, cancellationToken);
    }

    /// <summary>Records, on the grid of the outline, the vertices that the flattening of a curve created and those that are corners of the path.</summary>
    private sealed class VertexCollector : PathSink
    {
        private readonly HashSet<(long X, long Y)> _smooth = [];
        private readonly HashSet<(long X, long Y)> _corners = [];
        private bool _hasCurrent;
        private double _x;
        private double _y;
        private double _startX;
        private double _startY;

        public bool IsSmooth(double x, double y)
        {
            var key = (PathCombiner.ToGrid(x), PathCombiner.ToGrid(y));
            return _smooth.Contains(key) && !_corners.Contains(key);
        }

        public override void MoveTo(double x, double y)
        {
            Add(x, y, smooth: false);
            _x = _startX = x;
            _y = _startY = y;
            _hasCurrent = true;
        }

        public override void LineTo(double x, double y, bool smooth)
        {
            if (!_hasCurrent)
            {
                MoveTo(x, y);
                return;
            }

            // The flag of a segment is about the vertex it starts at
            Add(_x, _y, smooth);
            _x = x;
            _y = y;
        }

        public override void Close(bool smooth)
        {
            if (!_hasCurrent)
                return;

            // Neither end of the segment that closes a curve is a corner; the first point was recorded as one
            Add(_x, _y, smooth);
            if (smooth)
            {
                var key = (PathCombiner.ToGrid(_startX), PathCombiner.ToGrid(_startY));
                _corners.Remove(key);
                _smooth.Add(key);
            }

            // The contour is complete: it has no end that a straight segment would close
            _hasCurrent = false;
        }

        public override void End()
        {
            if (_hasCurrent)
            {
                // The end of an open contour is a corner of the area: a straight segment closes it
                Add(_x, _y, smooth: false);
            }

            _hasCurrent = false;
        }

        private void Add(double x, double y, bool smooth)
        {
            var key = (PathCombiner.ToGrid(x), PathCombiner.ToGrid(y));
            if (smooth)
            {
                _smooth.Add(key);
            }
            else
            {
                _corners.Add(key);
            }
        }
    }
}
