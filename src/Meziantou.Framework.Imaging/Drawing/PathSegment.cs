using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>
/// An element of a <see cref="VectorPath"/>, as <see cref="VectorPath.EnumerateSegments"/> gives it: the start of a
/// contour, a straight segment, a Bézier curve, an arc of an ellipse, or the end of a closed contour.
/// </summary>
/// <remarks>
/// Each kind is what the method of the same name of <see cref="VectorPathBuilder"/> adds, with the same values: a path
/// can be built again from its segments.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct PathSegment : IEquatable<PathSegment>
{
    internal PathSegment(PathSegmentKind kind, PointD point, PointD control1 = default, PointD control2 = default)
    {
        Kind = kind;
        Point = point;
        Control1 = control1;
        Control2 = control2;
    }

    internal PathSegment(PointD point, double radiusX, double radiusY, double rotation, bool isLargeArc, bool isClockwise)
    {
        Kind = PathSegmentKind.ArcTo;
        Point = point;
        RadiusX = radiusX;
        RadiusY = radiusY;
        Rotation = rotation;
        IsLargeArc = isLargeArc;
        IsClockwise = isClockwise;
    }

    /// <summary>Gets what the segment adds to the path.</summary>
    public PathSegmentKind Kind { get; }

    /// <summary>
    /// Gets the point the segment ends at: the first point of the contour for <see cref="PathSegmentKind.MoveTo"/> and
    /// for <see cref="PathSegmentKind.Close"/>.
    /// </summary>
    public PointD Point { get; }

    /// <summary>Gets the control point of a quadratic Bézier curve, or the first control point of a cubic one.</summary>
    public PointD Control1 { get; }

    /// <summary>Gets the second control point of a cubic Bézier curve.</summary>
    public PointD Control2 { get; }

    /// <summary>Gets the first radius of the ellipse of an arc.</summary>
    public double RadiusX { get; }

    /// <summary>Gets the second radius of the ellipse of an arc.</summary>
    public double RadiusY { get; }

    /// <summary>Gets the angle of the first radius of the ellipse of an arc, in degrees from the x axis, clockwise on the image.</summary>
    public double Rotation { get; }

    /// <summary>Gets a value indicating whether an arc is more than half a turn.</summary>
    public bool IsLargeArc { get; }

    /// <summary>Gets a value indicating whether an arc turns clockwise on the image.</summary>
    public bool IsClockwise { get; }

    /// <inheritdoc />
    public bool Equals(PathSegment other)
        => Kind == other.Kind && Point == other.Point && Control1 == other.Control1 && Control2 == other.Control2
        && RadiusX.Equals(other.RadiusX) && RadiusY.Equals(other.RadiusY) && Rotation.Equals(other.Rotation)
        && IsLargeArc == other.IsLargeArc && IsClockwise == other.IsClockwise;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is PathSegment other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Kind);
        hash.Add(Point);
        hash.Add(Control1);
        hash.Add(Control2);
        hash.Add(RadiusX);
        hash.Add(RadiusY);
        hash.Add(Rotation);
        hash.Add(IsLargeArc);
        hash.Add(IsClockwise);
        return hash.ToHashCode();
    }

    /// <summary>Returns the segment as a command of SVG path data, with absolute coordinates.</summary>
    /// <returns>A culture-invariant string.</returns>
    public override string ToString() => Kind switch
    {
        PathSegmentKind.MoveTo => string.Create(CultureInfo.InvariantCulture, $"M{Point.X} {Point.Y}"),
        PathSegmentKind.LineTo => string.Create(CultureInfo.InvariantCulture, $"L{Point.X} {Point.Y}"),
        PathSegmentKind.QuadraticBezierTo => string.Create(CultureInfo.InvariantCulture, $"Q{Control1.X} {Control1.Y} {Point.X} {Point.Y}"),
        PathSegmentKind.CubicBezierTo => string.Create(CultureInfo.InvariantCulture, $"C{Control1.X} {Control1.Y} {Control2.X} {Control2.Y} {Point.X} {Point.Y}"),
        PathSegmentKind.ArcTo => string.Create(CultureInfo.InvariantCulture, $"A{RadiusX} {RadiusY} {Rotation} {(IsLargeArc ? 1 : 0)} {(IsClockwise ? 1 : 0)} {Point.X} {Point.Y}"),
        _ => "Z",
    };

    /// <summary>Compares two segments for equality.</summary>
    /// <param name="left">The first segment.</param>
    /// <param name="right">The second segment.</param>
    /// <returns><see langword="true"/> if every value is equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(PathSegment left, PathSegment right) => left.Equals(right);

    /// <summary>Compares two segments for inequality.</summary>
    /// <param name="left">The first segment.</param>
    /// <param name="right">The second segment.</param>
    /// <returns><see langword="true"/> if a value differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(PathSegment left, PathSegment right) => !left.Equals(right);
}
