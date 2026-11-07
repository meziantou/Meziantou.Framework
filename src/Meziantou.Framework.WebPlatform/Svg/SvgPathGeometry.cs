using System.Runtime.InteropServices;
using static Meziantou.Framework.WebPlatform.Svg.SvgPathData;

namespace Meziantou.Framework.WebPlatform.Svg;

/// <summary>
/// The distance along a path (https://svgwg.org/svg2-draft/paths.html#DistanceAlongAPath). The path data is normalized to lines,
/// cubic Béziers and elliptical arcs. The length of a cubic Bézier is computed by subdividing it until its control polygon is
/// close to its chord; the length of an arc is the integral of the speed along the ellipse (adaptive Simpson's rule).
/// </summary>
internal sealed class SvgPathGeometry
{
    private const int MaxSubdivisionDepth = 16;
    private const double FlatnessTolerance = 1e-5;
    private const double ArcTolerance = 1e-9;

    private readonly List<Piece> _pieces = [];
    private SvgPoint _current;

    public SvgPathGeometry(List<SvgPathCommand> commands)
    {
        var normalizer = new Normalizer(keepArcs: true);
        for (var i = 0; i < commands.Count; i++)
        {
            normalizer.Emit(commands[i], static (state, segment) => state.Geometry.Add(state.Index, segment), (Geometry: this, Index: i));
        }
    }

    public bool IsEmpty => _pieces.Count == 0;

    /// <summary>
    /// Whether the path is a single closed subpath (https://svgwg.org/svg2-draft/text.html#TextPathElement: the text wraps
    /// around a closed path): its last command is a closepath and it has a single moveto.
    /// </summary>
    public bool IsClosed
    {
        get
        {
            var moves = 0;
            foreach (var piece in _pieces)
            {
                if (piece.Segment.Type == 'M')
                {
                    moves++;
                }
            }

            return moves <= 1 && _pieces.Count > 0 && _pieces[^1].Segment.Type == 'Z';
        }
    }

    public double TotalLength { get; private set; }

    private void Add(int commandIndex, NormalizedSegment segment)
    {
        var start = _current;
        var length = segment.Type switch
        {
            'L' or 'Z' => (segment.Target - start).Length,
            'C' => CubicLength(start, segment.Point1, segment.Point2, segment.Target, 0),
            'A' => ArcLength(segment.Arc, segment.Arc.StartAngle, segment.Arc.StartAngle + segment.Arc.SweepAngle),
            _ => 0, // a moveto has no length
        };

        _pieces.Add(new Piece(commandIndex, segment, start, length));
        TotalLength += length;
        _current = segment.Target;
    }

    /// <summary>Returns the end points of the commands of the path (the start of the subpath for a closepath).</summary>
    public List<SvgPoint> GetVertices()
    {
        var result = new List<SvgPoint>(_pieces.Count);
        foreach (var piece in _pieces)
        {
            result.Add(piece.Segment.Target);
        }

        return result;
    }

    /// <summary>Returns the point at the distance along the path, clamped to [0, total length].</summary>
    public SvgPoint GetPointAtLength(double distance)
    {
        var piece = FindPiece(distance, out var offset);
        var segment = piece.Segment;
        if (offset >= piece.Length)
            return segment.Target;

        return segment.Type switch
        {
            'L' or 'Z' => SvgPoint.Lerp(piece.Start, segment.Target, offset / piece.Length),
            'C' => PointOnCubic(piece.Start, segment.Point1, segment.Point2, segment.Target, offset, 0),
            'A' => PointOnArc(segment.Arc, offset),
            _ => segment.Target,
        };
    }

    /// <summary>
    /// Returns the point at the distance along the path (clamped to [0, total length]) and the unit tangent of the path at that
    /// point: the direction of a line, the derivative of an arc or of a curve (the one of a Bézier curve is computed from a
    /// nearby point), the direction of the start of the next segment with a length at a moveto.
    /// </summary>
    public (SvgPoint Point, SvgPoint Tangent) GetPointAndTangentAtLength(double distance)
    {
        distance = Math.Clamp(distance, 0, TotalLength);
        var point = GetPointAtLength(distance);
        var piece = FindPiece(distance, out var offset);
        if (piece.Length <= 0)
        {
            // At a moveto (or a segment without length): the start of the next segment that has a length
            var index = _pieces.IndexOf(piece);
            for (var i = index + 1; i < _pieces.Count; i++)
            {
                if (_pieces[i].Length > 0)
                {
                    (piece, offset) = (_pieces[i], 0);
                    break;
                }
            }
        }

        if (piece.Length > 0 && piece.Segment.Type is 'L' or 'Z')
            return (point, Normalize(piece.Segment.Target - piece.Start));

        // The derivative of an elliptical arc
        if (piece.Length > 0 && piece.Segment.Type == 'A')
        {
            var arc = piece.Segment.Arc;
            var derivative = arc.DerivativeAt(AngleOnArc(arc, offset)) * Math.Sign(arc.SweepAngle);
            if (derivative.Length > 0)
                return (point, Normalize(derivative));
        }

        // A nearby point along the path (before the point at the end of the path)
        var step = Math.Max(1e-4, TotalLength * 1e-7);
        var tangent = distance + step <= TotalLength ? GetPointAtLength(distance + step) - point : point - GetPointAtLength(distance - step);
        return (point, tangent.Length > 0 ? Normalize(tangent) : new SvgPoint(1, 0));

        static SvgPoint Normalize(SvgPoint vector) => vector * (1 / vector.Length);
    }

    /// <summary>Returns the index of the path command at the distance along the path, clamped to [0, total length].</summary>
    public int GetCommandIndexAtLength(double distance) => FindPiece(distance, out _).CommandIndex;

    /// <summary>The first piece whose end is at or after the distance, and the distance from the start of the piece.</summary>
    private Piece FindPiece(double distance, out double offset)
    {
        distance = Math.Clamp(distance, 0, TotalLength);
        var accumulated = 0d;
        foreach (var piece in _pieces)
        {
            if (accumulated + piece.Length >= distance)
            {
                offset = distance - accumulated;
                return piece;
            }

            accumulated += piece.Length;
        }

        var last = _pieces[^1];
        offset = last.Length;
        return last;
    }

    private static double CubicLength(SvgPoint p0, SvgPoint p1, SvgPoint p2, SvgPoint p3, int depth)
    {
        var chord = (p3 - p0).Length;
        var polygon = (p1 - p0).Length + (p2 - p1).Length + (p3 - p2).Length;
        if (depth >= MaxSubdivisionDepth || polygon - chord <= FlatnessTolerance * Math.Max(1, polygon))
            return (chord + polygon) / 2; // Gravesen's approximation of the length of a cubic Bézier

        Split(p0, p1, p2, p3, out var left, out var right);
        return CubicLength(left.P0, left.P1, left.P2, left.P3, depth + 1) + CubicLength(right.P0, right.P1, right.P2, right.P3, depth + 1);
    }

    private static SvgPoint PointOnCubic(SvgPoint p0, SvgPoint p1, SvgPoint p2, SvgPoint p3, double distance, int depth)
    {
        var chord = (p3 - p0).Length;
        var polygon = (p1 - p0).Length + (p2 - p1).Length + (p3 - p2).Length;
        if (depth >= MaxSubdivisionDepth || polygon - chord <= FlatnessTolerance * Math.Max(1, polygon))
        {
            // The curve is almost straight. The parameter is not proportional to the distance (the control points can be
            // unevenly spaced), but the control points are in order along the chord, so the projection of the curve on
            // the chord is monotonic: the parameter is found by bisection on that projection.
            var length = (chord + polygon) / 2;
            if (length == 0 || chord == 0)
                return p3;

            var direction = p3 - p0;
            var target = Math.Clamp(distance / length, 0, 1) * chord * chord;
            var low = 0d;
            var high = 1d;
            for (var i = 0; i < 64 && low != high; i++)
            {
                var middle = (low + high) / 2;
                var offset = EvaluateCubic(p0, p1, p2, p3, middle) - p0;
                if ((offset.X * direction.X) + (offset.Y * direction.Y) < target)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            return EvaluateCubic(p0, p1, p2, p3, (low + high) / 2);
        }

        Split(p0, p1, p2, p3, out var left, out var right);
        var leftLength = CubicLength(left.P0, left.P1, left.P2, left.P3, depth + 1);
        return distance <= leftLength
            ? PointOnCubic(left.P0, left.P1, left.P2, left.P3, distance, depth + 1)
            : PointOnCubic(right.P0, right.P1, right.P2, right.P3, distance - leftLength, depth + 1);
    }

    private static double ArcLength(SvgArc arc, double from, double to)
    {
        var speedFrom = arc.SpeedAt(from);
        var speedTo = arc.SpeedAt(to);
        var middle = (from + to) / 2;
        var speedMiddle = arc.SpeedAt(middle);
        var estimate = Math.Abs(to - from) / 6 * (speedFrom + (4 * speedMiddle) + speedTo);
        return AdaptiveSimpson(arc, from, to, speedFrom, speedMiddle, speedTo, estimate, ArcTolerance * Math.Max(1, estimate), 0);
    }

    private static double AdaptiveSimpson(SvgArc arc, double from, double to, double speedFrom, double speedMiddle, double speedTo, double whole, double tolerance, int depth)
    {
        var middle = (from + to) / 2;
        var leftMiddle = (from + middle) / 2;
        var rightMiddle = (middle + to) / 2;
        var speedLeft = arc.SpeedAt(leftMiddle);
        var speedRight = arc.SpeedAt(rightMiddle);
        var left = Math.Abs(middle - from) / 6 * (speedFrom + (4 * speedLeft) + speedMiddle);
        var right = Math.Abs(to - middle) / 6 * (speedMiddle + (4 * speedRight) + speedTo);
        var difference = left + right - whole;
        if (depth >= MaxSubdivisionDepth || Math.Abs(difference) <= 15 * tolerance)
            return left + right + (difference / 15);

        return AdaptiveSimpson(arc, from, middle, speedFrom, speedLeft, speedMiddle, left, tolerance / 2, depth + 1)
            + AdaptiveSimpson(arc, middle, to, speedMiddle, speedRight, speedTo, right, tolerance / 2, depth + 1);
    }

    /// <summary>The point of the arc at the distance from its start: the angle is found by bisection.</summary>
    private static SvgPoint PointOnArc(SvgArc arc, double distance) => arc.PointAt(AngleOnArc(arc, distance));

    /// <summary>Returns the angle of the point of an arc at a distance from its start.</summary>
    private static double AngleOnArc(SvgArc arc, double distance)
    {
        if (distance <= 0)
            return arc.StartAngle;

        var low = arc.StartAngle;
        var high = arc.StartAngle + arc.SweepAngle;
        for (var i = 0; i < 64 && low != high; i++)
        {
            var middle = (low + high) / 2;
            if (ArcLength(arc, arc.StartAngle, middle) < distance)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return (low + high) / 2;
    }

    private static SvgPoint EvaluateCubic(SvgPoint p0, SvgPoint p1, SvgPoint p2, SvgPoint p3, double t)
    {
        var mt = 1 - t;
        var a = mt * mt * mt;
        var b = 3 * mt * mt * t;
        var c = 3 * mt * t * t;
        var d = t * t * t;
        return new SvgPoint((a * p0.X) + (b * p1.X) + (c * p2.X) + (d * p3.X), (a * p0.Y) + (b * p1.Y) + (c * p2.Y) + (d * p3.Y));
    }

    /// <summary>Splits a cubic Bézier in two halves (de Casteljau's algorithm).</summary>
    private static void Split(SvgPoint p0, SvgPoint p1, SvgPoint p2, SvgPoint p3, out Cubic left, out Cubic right)
    {
        var p01 = SvgPoint.Lerp(p0, p1, 0.5);
        var p12 = SvgPoint.Lerp(p1, p2, 0.5);
        var p23 = SvgPoint.Lerp(p2, p3, 0.5);
        var p012 = SvgPoint.Lerp(p01, p12, 0.5);
        var p123 = SvgPoint.Lerp(p12, p23, 0.5);
        var middle = SvgPoint.Lerp(p012, p123, 0.5);
        left = new Cubic(p0, p01, p012, middle);
        right = new Cubic(middle, p123, p23, p3);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Cubic(SvgPoint P0, SvgPoint P1, SvgPoint P2, SvgPoint P3);

    /// <param name="CommandIndex">The index of the path command that produced the piece (an arc produces several pieces).</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Piece(int CommandIndex, NormalizedSegment Segment, SvgPoint Start, double Length);
}
