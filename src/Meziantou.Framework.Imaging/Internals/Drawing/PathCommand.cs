namespace Meziantou.Framework.Imaging.Internals;

/// <summary>An element of a <see cref="Drawing.VectorPath"/>. Each element consumes a fixed number of coordinate pairs.</summary>
internal enum PathCommand : byte
{
    /// <summary>Starts a contour (1 point).</summary>
    MoveTo = 0,

    /// <summary>A straight segment to a point (1 point).</summary>
    LineTo = 1,

    /// <summary>A quadratic Bézier segment (control point, end point).</summary>
    QuadraticTo = 2,

    /// <summary>A cubic Bézier segment (two control points, end point).</summary>
    CubicTo = 3,

    /// <summary>Closes the contour with a straight segment to its first point (no point).</summary>
    Close = 4,

    /// <summary>A complete closed ellipse (center, then the two radii as a pair).</summary>
    Ellipse = 5,

    /// <summary>
    /// A complete closed ellipse whose axes are not horizontal and vertical: its center, then two conjugate
    /// half-diameters as vectors. The contour starts at the end of the first one and turns toward the second one.
    /// </summary>
    EllipseAxes = 6,

    /// <summary>
    /// An arc of an ellipse, of at most a quarter turn, from the current point (the coordinates of an
    /// <see cref="ArcPiece"/>: center, two conjugate half-diameters, the unit vectors of both ends, end point).
    /// </summary>
    Arc = 7,

    /// <summary>The same, continuing the arc of the element before it: the vertex between them is not a corner of the path.</summary>
    ArcContinuation = 8,
}
