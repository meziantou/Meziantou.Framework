namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>Identifies what a <see cref="PathSegment"/> adds to a path.</summary>
public enum PathSegmentKind
{
    /// <summary>Starts a contour at <see cref="PathSegment.Point"/>.</summary>
    MoveTo = 0,

    /// <summary>A straight segment to <see cref="PathSegment.Point"/>.</summary>
    LineTo = 1,

    /// <summary>A quadratic Bézier curve to <see cref="PathSegment.Point"/>, whose control point is <see cref="PathSegment.Control1"/>.</summary>
    QuadraticBezierTo = 2,

    /// <summary>A cubic Bézier curve to <see cref="PathSegment.Point"/>, whose control points are <see cref="PathSegment.Control1"/> and <see cref="PathSegment.Control2"/>.</summary>
    CubicBezierTo = 3,

    /// <summary>An arc of an ellipse to <see cref="PathSegment.Point"/>, described as the arcs of SVG paths are.</summary>
    ArcTo = 4,

    /// <summary>Closes the contour with a straight segment to its first point, which is <see cref="PathSegment.Point"/>.</summary>
    Close = 5,
}
