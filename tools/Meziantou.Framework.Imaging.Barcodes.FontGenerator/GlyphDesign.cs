using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Barcodes.FontGenerator;

/// <summary>
/// The design of a glyph: the centerlines of its strokes, painted with a pen of the nominal stroke width, and the areas
/// it fills. The coordinates are font units with the y axis up: the origin is on the baseline at the left of the advance.
/// </summary>
/// <remarks>
/// The OCR standards define a character by its stroke centerlines and the nominal stroke width: the character is the area
/// a circle of that diameter covers when its center follows the centerlines (round ends, round joins), except where the
/// drawings show sharp corners, which a mitered join gives.
/// </remarks>
internal sealed class GlyphDesign
{
    private readonly List<GlyphStroke> _strokes = [];
    private readonly List<VectorPath> _fills = [];

    public GlyphDesign(string name, IReadOnlyList<int> codePoints, double strokeWidth)
    {
        Name = name;
        CodePoints = codePoints;
        StrokeWidth = strokeWidth;
    }

    /// <summary>Gets the PostScript name of the glyph.</summary>
    public string Name { get; }

    /// <summary>Gets the code points the glyph is mapped to.</summary>
    public IReadOnlyList<int> CodePoints { get; }

    /// <summary>Gets or sets the horizontal offset of the whole glyph, applied when it is built.</summary>
    public double OffsetX { get; set; }

    /// <summary>Gets or sets the width of the strokes that follow.</summary>
    public double StrokeWidth { get; set; }

    public IReadOnlyList<GlyphStroke> Strokes => _strokes;

    public IReadOnlyList<VectorPath> Fills => _fills;

    /// <summary>Adds an open centerline through points, with round ends and round joins.</summary>
    public GlyphDesign Line(params PointD[] points) => Polyline(LineJoin.Round, LineCap.Round, closed: false, points);

    /// <summary>Adds an open centerline through points, with round ends and sharp (mitered) joins.</summary>
    public GlyphDesign SharpLine(params PointD[] points) => Polyline(LineJoin.Miter, LineCap.Round, closed: false, points);

    /// <summary>Adds a closed centerline through points, with round joins.</summary>
    public GlyphDesign Loop(params PointD[] points) => Polyline(LineJoin.Round, LineCap.Round, closed: true, points);

    /// <summary>Adds a closed centerline through points, with sharp (mitered) joins.</summary>
    public GlyphDesign SharpLoop(params PointD[] points) => Polyline(LineJoin.Miter, LineCap.Round, closed: true, points);

    /// <summary>Adds an open centerline through vertices, whose corners are arcs of the radius of each vertex (0 for a corner), with round ends.</summary>
    /// <param name="join">The join of the corners without radius.</param>
    /// <param name="vertices">The vertices.</param>
    public GlyphDesign Curve(LineJoin join, params Vertex[] vertices) => Path(builder => AddFilleted(builder, vertices, closed: false), join);

    /// <summary>Adds a closed centerline through vertices, whose corners are arcs of the radius of each vertex (0 for a corner).</summary>
    public GlyphDesign ClosedCurve(LineJoin join, params Vertex[] vertices) => Path(builder => AddFilleted(builder, vertices, closed: true), join);

    /// <summary>Adds a centerline that is an arc of a circle, with round ends.</summary>
    /// <param name="center">The center of the circle.</param>
    /// <param name="radius">The radius of the circle.</param>
    /// <param name="startDegrees">The angle of the first point, counterclockwise from the x axis (y up).</param>
    /// <param name="sweepDegrees">The angle of the arc, counterclockwise when positive.</param>
    public GlyphDesign Arc(PointD center, double radius, double startDegrees, double sweepDegrees)
        => Path(builder => builder.AddArc(center, radius, radius, startDegrees, sweepDegrees));

    /// <summary>Adds a centerline that is an arc of an ellipse, with round ends.</summary>
    public GlyphDesign EllipseArc(PointD center, double radiusX, double radiusY, double startDegrees, double sweepDegrees)
        => Path(builder => builder.AddArc(center, radiusX, radiusY, startDegrees, sweepDegrees));

    /// <summary>Adds a centerline built with a path builder.</summary>
    public GlyphDesign Path(Action<VectorPathBuilder> build, LineJoin join = LineJoin.Round, LineCap cap = LineCap.Round)
    {
        var builder = new VectorPathBuilder();
        build(builder);
        _strokes.Add(new GlyphStroke(builder.Build(), StrokeWidth, join, cap));
        return this;
    }

    /// <summary>Adds an area the glyph fills, built with a path builder.</summary>
    public GlyphDesign Fill(Action<VectorPathBuilder> build)
    {
        var builder = new VectorPathBuilder();
        build(builder);
        _fills.Add(builder.Build());
        return this;
    }

    // Each corner with a radius becomes the arc of that radius tangent to both of its segments
    private static void AddFilleted(VectorPathBuilder builder, Vertex[] vertices, bool closed)
    {
        var count = vertices.Length;
        if (count < 2)
            throw new ArgumentException("A centerline needs at least two vertices.", nameof(vertices));

        for (var i = 0; i < count; i++)
        {
            var vertex = vertices[i];
            var hasNeighbors = closed || (i > 0 && i < count - 1);
            if (vertex.Radius <= 0 || !hasNeighbors)
            {
                if (i == 0)
                {
                    builder.MoveTo(vertex.Point);
                }
                else
                {
                    builder.LineTo(vertex.Point);
                }

                continue;
            }

            var previous = vertices[(i + count - 1) % count].Point;
            var next = vertices[(i + 1) % count].Point;
            var u = Normalize(previous.X - vertex.Point.X, previous.Y - vertex.Point.Y);
            var v = Normalize(next.X - vertex.Point.X, next.Y - vertex.Point.Y);
            var angle = Math.Acos(Math.Clamp((u.X * v.X) + (u.Y * v.Y), -1, 1));
            var distance = vertex.Radius / Math.Tan(angle / 2);
            var start = new PointD(vertex.Point.X + (u.X * distance), vertex.Point.Y + (u.Y * distance));
            var end = new PointD(vertex.Point.X + (v.X * distance), vertex.Point.Y + (v.Y * distance));
            if (i == 0)
            {
                builder.MoveTo(start);
            }
            else
            {
                builder.LineTo(start);
            }

            // Turning from the incoming direction (-u) to the outgoing one (v): the arc turns the same way
            var cross = (-u.X * v.Y) - (-u.Y * v.X);
            builder.ArcTo(vertex.Radius, vertex.Radius, 0, largeArc: false, clockwise: cross > 0, end);
        }

        if (closed)
        {
            builder.Close();
        }

        static (double X, double Y) Normalize(double x, double y)
        {
            var length = Math.Sqrt((x * x) + (y * y));
            return (x / length, y / length);
        }
    }

    /// <summary>Adds a rectangle the glyph fills.</summary>
    public GlyphDesign Fill(RectangleD rectangle) => Fill(builder => builder.AddRectangle(rectangle));

    /// <summary>Adds an area the glyph fills.</summary>
    public GlyphDesign Fill(VectorPath path)
    {
        _fills.Add(path);
        return this;
    }

    private GlyphDesign Polyline(LineJoin join, LineCap cap, bool closed, PointD[] points)
    {
        if (points.Length == 0)
            throw new ArgumentException("A centerline needs at least one point.", nameof(points));

        if (points.Length == 1)
        {
            // A dot: the disc the pen covers at one point
            var radius = StrokeWidth / 2;
            return Fill(builder => builder.AddEllipse(points[0], radius, radius));
        }

        return Path(builder =>
        {
            builder.MoveTo(points[0]);
            foreach (var point in points.AsSpan(1))
            {
                builder.LineTo(point);
            }

            if (closed)
            {
                builder.Close();
            }
        }, join, cap);
    }
}

/// <summary>A vertex of a centerline, and the radius of the arc that rounds its corner (0 for a corner).</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct Vertex(PointD Point, double Radius = 0)
{
    public static implicit operator Vertex(PointD point) => new(point);
}

/// <summary>A stroke of a glyph: a centerline and the pen that paints it.</summary>
internal sealed record GlyphStroke(VectorPath Centerline, double Width, LineJoin Join, LineCap Cap);
