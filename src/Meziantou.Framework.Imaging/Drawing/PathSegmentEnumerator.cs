using System.Collections;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>Enumerates the segments of a <see cref="VectorPath"/>, without allocating. See <see cref="VectorPath.EnumerateSegments"/>.</summary>
[StructLayout(LayoutKind.Auto)]
public struct PathSegmentEnumerator : IEnumerable<PathSegment>, IEnumerator<PathSegment>
{
    private readonly VectorPath? _path;
    private int _command;
    private int _point;
    private int _step;
    private PointD _start;
    private PathSegment _current;

    internal PathSegmentEnumerator(VectorPath path)
    {
        _path = path;
    }

    /// <summary>Gets the segment the enumerator is on.</summary>
    public readonly PathSegment Current => _current;

    readonly object IEnumerator.Current => _current;

    /// <summary>Returns the enumerator itself, so that it can be used with <see langword="foreach"/>.</summary>
    /// <returns>A copy of this enumerator.</returns>
    public readonly PathSegmentEnumerator GetEnumerator() => this;

    readonly IEnumerator<PathSegment> IEnumerable<PathSegment>.GetEnumerator() => this;

    readonly IEnumerator IEnumerable.GetEnumerator() => this;

    /// <summary>Moves to the next segment.</summary>
    /// <returns><see langword="false"/> when the path has no more segment.</returns>
    public bool MoveNext()
    {
        if (_path is null)
            return false;

        var commands = _path.Commands;
        if (_command >= commands.Length)
            return false;

        var points = _path.Points[_point..];
        switch (commands[_command])
        {
            case PathCommand.MoveTo:
                _start = new PointD(points[0], points[1]);
                _current = new PathSegment(PathSegmentKind.MoveTo, _start);
                Advance(2);
                break;

            case PathCommand.LineTo:
                _current = new PathSegment(PathSegmentKind.LineTo, new PointD(points[0], points[1]));
                Advance(2);
                break;

            case PathCommand.QuadraticTo:
                _current = new PathSegment(PathSegmentKind.QuadraticBezierTo, new PointD(points[2], points[3]), new PointD(points[0], points[1]));
                Advance(4);
                break;

            case PathCommand.CubicTo:
                _current = new PathSegment(PathSegmentKind.CubicBezierTo, new PointD(points[4], points[5]), new PointD(points[0], points[1]), new PointD(points[2], points[3]));
                Advance(6);
                break;

            case PathCommand.Close:
                _current = new PathSegment(PathSegmentKind.Close, _start);
                Advance(0);
                break;

            case PathCommand.Ellipse:
                MoveNextOfEllipse(points[0], points[1], points[2], 0, 0, points[3], 4);
                break;

            case PathCommand.EllipseAxes:
                MoveNextOfEllipse(points[0], points[1], points[2], points[3], points[4], points[5], 6);
                break;

            default:
                var piece = new ArcPiece(points[..ArcPiece.CoordinateCount], isContinuation: false);
                var end = new PointD(piece.EndX, piece.EndY);
                var isClockwise = (((piece.Ax * piece.By) - (piece.Ay * piece.Bx)) * ((piece.Ux * piece.Vy) - (piece.Uy * piece.Vx))) > 0;
                _current = CreateArc(end, piece.Ux, piece.Uy, piece.Vx, piece.Vy, isClockwise);
                Advance(ArcPiece.CoordinateCount);
                break;
        }

        return true;
    }

    /// <summary>Moves back before the first segment.</summary>
    public void Reset()
    {
        _command = 0;
        _point = 0;
        _step = 0;
        _start = default;
        _current = default;
    }

    /// <summary>Does nothing: the enumerator holds no resource.</summary>
    public readonly void Dispose()
    {
    }

    private void Advance(int coordinates)
    {
        _command++;
        _point += coordinates;
        _step = 0;
    }

    /// <summary>A whole ellipse is the start of a contour, two arcs of half a turn and the end of the contour.</summary>
    private void MoveNextOfEllipse(double centerX, double centerY, double ux, double uy, double vx, double vy, int coordinates)
    {
        var first = new PointD(centerX + ux, centerY + uy);
        var isClockwise = ((ux * vy) - (uy * vx)) > 0;
        switch (_step++)
        {
            case 0:
                _start = first;
                _current = new PathSegment(PathSegmentKind.MoveTo, first);
                break;

            case 1:
                _current = CreateArc(new PointD(centerX - ux, centerY - uy), ux, uy, vx, vy, isClockwise);
                break;

            case 2:
                _current = CreateArc(first, ux, uy, vx, vy, isClockwise);
                break;

            default:
                _current = new PathSegment(PathSegmentKind.Close, first);
                Advance(coordinates);
                break;
        }
    }

    private static PathSegment CreateArc(PointD end, double ux, double uy, double vx, double vy, bool isClockwise)
    {
        ArcGeometry.GetAxes(ux, uy, vx, vy, out var radiusX, out var radiusY, out var rotation);

        // An ellipse without area is a segment
        return radiusX > 0 && radiusY > 0
            ? new PathSegment(end, radiusX, radiusY, rotation, isLargeArc: false, isClockwise)
            : new PathSegment(PathSegmentKind.LineTo, end);
    }
}
