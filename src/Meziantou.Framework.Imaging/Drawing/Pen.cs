using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>Immutable description of a stroke: its paint, its width and the shape of its corners and ends.</summary>
/// <remarks>
/// A stroke is centered on its path: it covers the points within half <see cref="Width"/> of the path, completed by the
/// joins at the vertices and by the caps at the ends of open contours. The vertices that the library creates when it
/// approximates a curve are always joined with <see cref="Drawing.LineJoin.Round"/>, so that a wide curved stroke stays
/// smooth; <see cref="LineJoin"/> applies to the vertices of the path itself. <see cref="Alignment"/> moves the stroke
/// to one side of the path, <see cref="StartMarker"/> and <see cref="EndMarker"/> draw a shape at the ends of open
/// contours, and <see cref="ScalesWithTransform"/> says whether a transformation of the canvas enlarges the pen with
/// the path.
/// </remarks>
public sealed class Pen
{
    /// <summary>The shortest pattern of dashes and gaps: 1/1024 of a pixel.</summary>
    internal const double MinDashPatternLength = 1.0 / 1024;

    private readonly double[] _dashPattern = [];

    /// <summary>Initializes a new instance of the <see cref="Pen"/> class.</summary>
    /// <param name="brush">The paint of the stroke. A color converts implicitly.</param>
    /// <param name="width">The width of the stroke, in pixels, or 0 for a hairline: a stroke one pixel of the canvas wide, whatever the transformation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="brush"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="width"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is negative, or exceeds 16777216.</exception>
    public Pen(Brush brush, double width)
    {
        ArgumentNullException.ThrowIfNull(brush);
        DrawingGuard.Length(width, nameof(width));
        Brush = brush;
        Width = width;
    }

    /// <summary>Gets the paint of the stroke.</summary>
    public Brush Brush { get; }

    /// <summary>Gets the width of the stroke, in pixels, or 0 for a hairline.</summary>
    /// <remarks>
    /// A hairline, as the hairlines of Skia, is the stroke of a pen one pixel wide that the transformation of the canvas
    /// does not apply to (<see cref="ScalesWithTransform"/> off, whatever it says): the path is placed on the canvas,
    /// then stroked one pixel wide, its dashes and its markers measured in pixels of the canvas.
    /// </remarks>
    public double Width { get; }

    /// <summary>Gets the width the stroke has: <see cref="Width"/>, or 1 for a hairline.</summary>
    internal double StrokeWidth => Width == 0 ? 1 : Width;

    /// <summary>Gets a value indicating whether the transformation of the canvas applies to the pen: not to a hairline.</summary>
    internal bool IsScaled => ScalesWithTransform && Width != 0;

    /// <summary>Gets the shape of the corners. Defaults to <see cref="Drawing.LineJoin.Miter"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Drawing.LineJoin"/>.</exception>
    public LineJoin LineJoin
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The line join is not valid.");

            field = value;
        }
    }

    /// <summary>Gets the shape of the ends of open contours. Defaults to <see cref="Drawing.LineCap.Butt"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Drawing.LineCap"/>.</exception>
    public LineCap LineCap
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The line cap is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the limit of <see cref="Drawing.LineJoin.Miter"/> joins: the largest allowed ratio between the length of the
    /// miter (from the inner corner to the tip) and the width of the stroke. A sharper corner is joined with a
    /// <see cref="Drawing.LineJoin.Bevel"/>. Defaults to 4 (corners sharper than about 29 degrees are beveled); 1 bevels
    /// every corner.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1, greater than 1048576, or not a number.</exception>
    public double MiterLimit
    {
        get;
        init
        {
            if (!(value >= 1 && value <= 1048576))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The miter limit must be between 1 and 1048576.");

            field = value;
        }
    } = 4;

    /// <summary>
    /// Gets the lengths, in pixels, of the dashes and of the gaps of the stroke, alternately and starting with a dash.
    /// Empty (the default) for a continuous stroke.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pattern is repeated along each contour of the path, which starts <see cref="DashOffset"/> into it; lengths are
    /// measured along the path as it is drawn. A pattern of an odd number of lengths is used twice, so that <c>[3]</c>
    /// is dashes of 3 and gaps of 3. Each dash is stroked as an open contour: it gets <see cref="LineCap"/> at both ends
    /// and <see cref="LineJoin"/> at the vertices of the path it goes through. A dash of length zero is drawn by its
    /// caps alone: a dot with <see cref="Drawing.LineCap.Round"/>, a square with <see cref="Drawing.LineCap.Square"/>.
    /// </para>
    /// <para>
    /// When a closed contour ends in the middle of a dash, that dash continues with the first dash of the contour, as one
    /// dash. The time to draw a dashed stroke grows with the number of dashes on the canvas.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">A length is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A length is negative or exceeds 16777216, or the dashes and the gaps of a pattern that is not empty add up to less than 1/1024.</exception>
    public ReadOnlySpan<double> DashPattern
    {
        get => _dashPattern;
        init
        {
            double total = 0;
            foreach (var length in value)
            {
                DrawingGuard.Length(length, nameof(value));
                total += length;
            }

            if (value.IsEmpty)
            {
                _dashPattern = [];
                Dashes = [];
                DashLength = 0;
                return;
            }

            // A pattern of an odd number of lengths is used twice
            var isOdd = (value.Length & 1) != 0;
            if (isOdd)
            {
                total *= 2;
            }

            if (total < MinDashPatternLength)
                throw new ArgumentOutOfRangeException(nameof(value), total, "The dashes and the gaps of a dash pattern must add up to 1/1024 at least.");

            _dashPattern = value.ToArray();
            Dashes = isOdd ? [.. value, .. value] : _dashPattern;
            DashLength = total;
        }
    }

    /// <summary>Gets the distance into <see cref="DashPattern"/> at which each contour starts. Defaults to 0; a negative distance starts before the pattern.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public double DashOffset
    {
        get;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentException("The dash offset must be a finite number.", nameof(value));

            field = value;
        }
    }

    /// <summary>Gets where the stroke is, relative to the path. Defaults to <see cref="PenAlignment.Center"/>.</summary>
    /// <remarks>
    /// <para>
    /// A stroke that is not centered is the stroke of a pen twice as wide, of which only one side is kept: what is
    /// inside what the path fills (with <see cref="FillRule.NonZero"/>, an open contour being closed by a straight
    /// segment), or what is outside. An inside stroke of the width <see cref="Width"/> is therefore exactly as wide
    /// along the straight sides of a shape; in a corner sharper than what <see cref="MiterLimit"/> allows, an outside
    /// stroke is beveled as any stroke is.
    /// </para>
    /// <para>
    /// The side is kept by a clip, antialiased like any clip (see <see cref="DrawingCanvas.Clip(VectorPath, DrawingAction, FillRule)"/>):
    /// a draw call with such a pen keeps one value per pixel of the bounds of its path (of the clip that is active,
    /// for an outside stroke), rented when it starts.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="PenAlignment"/>.</exception>
    public PenAlignment Alignment
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The alignment is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the shape drawn at the start of each open contour, or <see langword="null"/> (the default) for none.
    /// </summary>
    /// <remarks>
    /// The shape is scaled by <see cref="Width"/>, turned to the direction of the contour, and painted with
    /// <see cref="Brush"/>, as one shape with the stroke: see <see cref="LineMarker"/>. A closed contour has no
    /// marker, and neither has a contour without length. With <see cref="DashPattern"/>, the markers are at the ends
    /// of the contour, not of each dash.
    /// </remarks>
    public LineMarker? StartMarker { get; init; }

    /// <summary>
    /// Gets the shape drawn at the end of each open contour, or <see langword="null"/> (the default) for none: the head
    /// of an arrow with <see cref="LineMarker.Arrow"/>.
    /// </summary>
    /// <remarks>See <see cref="StartMarker"/>.</remarks>
    public LineMarker? EndMarker { get; init; }

    /// <summary>
    /// Gets a value indicating whether the transformation of the canvas applies to the pen as it applies to the path.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With <see langword="true"/>, a stroke drawn inside <see cref="DrawingCanvas.Transform(AffineTransform, DrawingAction)"/>
    /// is the stroke of the path as it is given, transformed: a scaling makes the stroke wider, and a scaling that is
    /// not the same along both axes makes it wider in one direction than in the other.
    /// </para>
    /// <para>
    /// With <see langword="false"/>, the path is transformed first and the pen strokes the result: <see cref="Width"/>,
    /// the lengths of <see cref="DashPattern"/> and <see cref="DashOffset"/> are pixels of the canvas whatever the
    /// transformation, the joins and the caps keep their shape, and the markers keep their size and are turned to the
    /// direction the path has on the canvas. This is the pen of a chart, of a map or of an icon that is drawn in
    /// coordinates of its own: its lines are as wide at every scale.
    /// </para>
    /// <para>
    /// Without transformation of the canvas the value changes nothing, and <see cref="VectorPath.Widen"/>, which has
    /// no canvas, does not use it.
    /// </para>
    /// </remarks>
    public bool ScalesWithTransform { get; init; } = true;

    /// <summary>Gets a value indicating whether a shape is drawn at an end of the open contours.</summary>
    internal bool HasMarkers => StartMarker is not null || EndMarker is not null;

    /// <summary>
    /// Gets the distance from its path a stroke can reach: half its width, its miters (only those of a pen that miters
    /// its corners), the corners of its square caps, or anywhere with markers.
    /// </summary>
    /// <param name="widthFactor">What the width is multiplied by: 2 for a stroke of which one side is kept.</param>
    internal double GetStrokeReach(double widthFactor)
    {
        if (HasMarkers)
            return DrawingGuard.MaxCoordinate;

        var factor = Math.Max(LineJoin == LineJoin.Miter ? MiterLimit : 1, LineCap == LineCap.Square ? Math.Sqrt(2) : 1);
        return StrokeWidth * widthFactor * factor / 2;
    }

    /// <summary>
    /// Gets the distance from the inside of a curve a stroke can reach: half its width, or the corners of the square caps
    /// of its dashes. Its vertices there are smooth: they have round joins, never a miter.
    /// </summary>
    /// <param name="widthFactor">What the width is multiplied by: 2 for a stroke of which one side is kept.</param>
    internal double GetCurveReach(double widthFactor)
    {
        if (HasMarkers)
            return DrawingGuard.MaxCoordinate;

        var factor = LineCap == LineCap.Square ? Math.Sqrt(2) : 1;
        return StrokeWidth * widthFactor * factor / 2;
    }

    /// <summary>
    /// Gets a rectangle that contains what the pen paints when it strokes a path that a rectangle contains, without
    /// markers: the rectangle spread by the reach of the stroke on the side of the path it is on.
    /// </summary>
    internal RectangleD GetStrokeBounds(RectangleD pathBounds) => Alignment switch
    {
        PenAlignment.Inside => pathBounds,
        PenAlignment.Outside => pathBounds.Inflate(GetStrokeReach(2)),
        _ => pathBounds.Inflate(GetStrokeReach(1)),
    };

    /// <summary>Gets the pattern as a whole number of dashes and gaps; empty for a continuous stroke.</summary>
    internal double[] Dashes { get; private init; } = [];

    /// <summary>Gets the sum of the lengths of <see cref="Dashes"/>.</summary>
    internal double DashLength { get; private init; }

    /// <summary>Gets a value indicating whether the stroke is cut into dashes.</summary>
    internal bool IsDashed => Dashes.Length != 0;
}
