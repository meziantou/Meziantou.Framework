using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A shape drawn at an end of the open contours a pen strokes: the head of an arrow, a dot, a square.</summary>
/// <remarks>
/// <para>
/// <b>Placement.</b> The shape is given in units of the width of the pen, around the end of the contour: its origin is
/// that end, and its positive x axis points the way the contour leaves it (away from the path, at both ends). It is
/// scaled by the width of the pen, turned to the direction of the contour at that end, and filled with the brush of
/// the pen together with the stroke: where it overlaps the stroke, a translucent paint is not painted twice.
/// </para>
/// <para>
/// <b>Shape.</b> A marker is the union of the contours of its shape, whichever way they turn: a contour inside another
/// one does not make a hole. Open contours are closed by a straight segment.
/// </para>
/// <para>
/// <b>Inset.</b> The stroke stops <see cref="Inset"/> times the width of the pen before the end of the contour, so
/// that it does not show beyond a marker that narrows to a point: the tip of <see cref="Arrow"/> is exactly at the end
/// of the path. A contour that is not longer than its insets is drawn by its markers alone.
/// </para>
/// <para>Markers are immutable and thread-safe.</para>
/// </remarks>
public sealed class LineMarker
{
    /// <summary>Initializes a new instance of the <see cref="LineMarker"/> class.</summary>
    /// <param name="shape">The shape, in units of the width of the pen: its origin is the end of the contour, and its positive x axis the direction the contour leaves it by.</param>
    /// <param name="inset">The length the stroke is shortened by at that end, in units of the width of the pen. Must be zero or positive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shape"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="inset"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="inset"/> is negative or exceeds 16777216.</exception>
    public LineMarker(VectorPath shape, double inset = 0)
    {
        ArgumentNullException.ThrowIfNull(shape);
        DrawingGuard.Length(inset, nameof(inset));
        Shape = shape;
        Inset = inset;
    }

    /// <summary>Gets the head of an arrow: a triangle five widths long and four widths wide, whose tip is the end of the contour. The stroke stops two widths before the tip.</summary>
    public static LineMarker Arrow { get; } = new(new VectorPathBuilder().AddPolygon([new PointD(0, 0), new PointD(-5, -2), new PointD(-5, 2)]).Build(), inset: 2);

    /// <summary>Gets a disk of three widths across, centered on the end of the contour.</summary>
    public static LineMarker Circle { get; } = new(new VectorPathBuilder().AddCircle(default, 1.5).Build());

    /// <summary>Gets a square of three widths, centered on the end of the contour, with two sides along the direction of the contour.</summary>
    public static LineMarker Square { get; } = new(new VectorPathBuilder().AddRectangle(new RectangleD(-1.5, -1.5, 3, 3)).Build());

    /// <summary>Gets a square of four widths across its corners, centered on the end of the contour, with a corner in the direction of the contour.</summary>
    public static LineMarker Diamond { get; } = new(new VectorPathBuilder().AddPolygon([new PointD(2, 0), new PointD(0, 2), new PointD(-2, 0), new PointD(0, -2)]).Build());

    /// <summary>Gets the shape, in units of the width of the pen.</summary>
    public VectorPath Shape { get; }

    /// <summary>Gets the length the stroke is shortened by at the end of the contour, in units of the width of the pen.</summary>
    public double Inset { get; }
}
