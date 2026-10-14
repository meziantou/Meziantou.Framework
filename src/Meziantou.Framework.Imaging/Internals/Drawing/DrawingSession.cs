using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The state of one <c>Draw</c> call: the exclusive leases of the frames drawn on, the scratch storage, and the validity
/// of the <see cref="DrawingCanvas"/> handed to the callback.
/// </summary>
/// <remarks>
/// <para>
/// Every draw call follows the same order: validate the arguments, build the edges of the shape, prepare the paint and
/// the sweep (every rental of the call happens here), then rasterize row by row and composite each row onto every
/// frame. A call that exceeds the allocation limit therefore changes no pixel; a call that is canceled leaves the rows
/// already composited as they are, the same rows on every frame.
/// </para>
/// <para>
/// The scratch that depends on the frame size is rented when the session starts; the scratch that depends on the shapes
/// (edges, sweep, gradient stops) grows to the largest call and is kept until the session ends.
/// </para>
/// </remarks>
internal sealed partial class DrawingSession : IDisposable
{
    private readonly Image _image;
    private readonly PixelStorage[] _leasedStorages;
    private readonly long[] _tokens;

    // What the draw calls paint: the frames of the image, or the image of a layer
    private ImageFrame[] _frames;
    private PixelStorage[] _storages;
    private readonly CancellationToken _cancellationToken;
    private int _leaseCount;
    private EdgeBuilder? _edges;
    private CoverageRasterizer? _rasterizer;
    private FillSink? _fillSink;
    private Stroker? _stroker;
    private Dasher? _dasher;
    private MarkerSink? _markers;
    private PathRecorder? _strokeRecorder;
    private TransformSink? _strokeTransformSink;
    private GradientEvaluator? _gradient;
    private ImageEvaluator? _imagePaint;
    private GlyphPathSink? _glyphSink;
    private GlyphScratch? _glyphScratch;
    private PathMeasure? _pathMeasure;
    private bool _thumbnailRemoved;

    // The draw calls prepare their shapes without painting them: the first pass of a text of several parts
    private bool _isPreparing;

    private DrawingSession(Image image, ImageFrame[] frames, PixelStorage[] storages, CancellationToken cancellationToken)
    {
        _image = image;
        _frames = frames;
        _storages = storages;
        _leasedStorages = storages;
        _tokens = new long[storages.Length];
        _cancellationToken = cancellationToken;
        Width = image.Width;
        Height = image.Height;
    }

    /// <summary>Gets the width of the canvas.</summary>
    public int Width { get; }

    /// <summary>Gets the height of the canvas.</summary>
    public int Height { get; }

    /// <summary>Gets a value indicating whether the callback is running.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Leases the frames and rents the scratch of a session.</summary>
    /// <param name="image">The image owning the frames.</param>
    /// <param name="frames">The frames to draw on; all of them receive every draw call.</param>
    /// <param name="storages">The storage of each frame.</param>
    /// <param name="cancellationToken">The token checked by every draw call.</param>
    /// <exception cref="InvalidOperationException">A lease is active on a frame.</exception>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit.</exception>
    public static DrawingSession Begin(Image image, ImageFrame[] frames, PixelStorage[] storages, CancellationToken cancellationToken)
    {
        var session = new DrawingSession(image, frames, storages, cancellationToken);
        try
        {
            for (var i = 0; i < storages.Length; i++)
            {
                session._tokens[i] = storages[i].AcquireLease().Token;
                session._leaseCount++;
            }

            var scope = image.Owner.Scope;
            session._edges = new EdgeBuilder(scope, session.Width, session.Height);
            session._rasterizer = new CoverageRasterizer(scope, session.Width, session.Height);
            session.IsActive = true;
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public void Fill(Brush brush)
    {
        ArgumentNullException.ThrowIfNull(brush);
        AddFrame(BeginCall());
        Render(brush, FillRule.NonZero);
    }

    public void Clear(Rgba64 color)
    {
        AddFrame(BeginCall());
        var converted = _frames[0].ConvertDrawingColor(ToImageColor(color));
        if (_rasterizer!.Begin(_edges!, FillRule.NonZero, _cancellationToken))
        {
            Paint(converted, evaluator: null, CompositeMode.Source, BlendMode.Normal);
        }
    }

    public void DrawImage(ImageFrame source, PointD position, double opacity)
    {
        ArgumentNullException.ThrowIfNull(source);
        DrawingGuard.Point(position, nameof(position));

        // The image starts at a valid position: its far corner may be a few pixels beyond the largest coordinate
        DrawImageCore(source, sourceRegion: null, new RectangleD(position.X, position.Y, source.Width, source.Height), ImageSampling.Bilinear, opacity);
    }

    public void DrawImage(ImageFrame source, Rectangle? sourceRegion, RectangleD destination, ImageSampling sampling, double opacity)
    {
        ArgumentNullException.ThrowIfNull(source);
        DrawingGuard.Rectangle(destination, nameof(destination));
        ValidateSampling(sampling);
        DrawImageCore(source, sourceRegion, destination, sampling, opacity);
    }

    public void DrawImage(ImageFrame source, AffineTransform transform, ImageSampling sampling, double opacity)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateSampling(sampling);
        var coverage = ImageEvaluator.GetOpacityCoverage(opacity, nameof(opacity));

        // The corners of the image, on the canvas
        var width = source.Width;
        var height = source.Height;
        var topLeft = transform.Transform(default);
        var topRight = transform.Transform(new PointD(width, 0));
        var bottomRight = transform.Transform(new PointD(width, height));
        var bottomLeft = transform.Transform(new PointD(0, height));
        DrawingGuard.Point(topLeft, nameof(transform));
        DrawingGuard.Point(topRight, nameof(transform));
        DrawingGuard.Point(bottomRight, nameof(transform));
        DrawingGuard.Point(bottomLeft, nameof(transform));
        var sink = BeginFill();
        sink.MoveTo(topLeft.X, topLeft.Y);
        sink.LineTo(topRight.X, topRight.Y, smooth: false);
        sink.LineTo(bottomRight.X, bottomRight.Y, smooth: false);
        sink.LineTo(bottomLeft.X, bottomLeft.Y, smooth: false);
        sink.Close(smooth: false);
        sink.End();
        PaintImage(source, new Rectangle(0, 0, width, height), transform, sampling, coverage, isEmpty: false, nameof(source));
    }

    public void DrawImage(ImageFrame source, PointD topLeft, PointD topRight, PointD bottomRight, PointD bottomLeft, ImageSampling sampling, double opacity)
    {
        ArgumentNullException.ThrowIfNull(source);
        DrawingGuard.Point(topLeft, nameof(topLeft));
        DrawingGuard.Point(topRight, nameof(topRight));
        DrawingGuard.Point(bottomRight, nameof(bottomRight));
        DrawingGuard.Point(bottomLeft, nameof(bottomLeft));
        ValidateSampling(sampling);
        var coverage = ImageEvaluator.GetOpacityCoverage(opacity, nameof(opacity));

        // The turn at each corner: all the same way around a convex quadrilateral, none when the corners are on a line
        var turn0 = Cross(topLeft, topRight, bottomRight);
        var turn1 = Cross(topRight, bottomRight, bottomLeft);
        var turn2 = Cross(bottomRight, bottomLeft, topLeft);
        var turn3 = Cross(bottomLeft, topLeft, topRight);
        var isFlat = turn0 == 0 && turn1 == 0 && turn2 == 0 && turn3 == 0;
        if (!isFlat && !((turn0 > 0 && turn1 > 0 && turn2 > 0 && turn3 > 0) || (turn0 < 0 && turn1 < 0 && turn2 < 0 && turn3 < 0)))
            throw new ArgumentException("The corners must be those of a convex quadrilateral, in order.", nameof(topLeft));

        var width = source.Width;
        var height = source.Height;
        if (!isFlat && topLeft.X - topRight.X + bottomRight.X - bottomLeft.X == 0 && topLeft.Y - topRight.Y + bottomRight.Y - bottomLeft.Y == 0)
        {
            // A parallelogram: the image is not seen in perspective, and an affine transformation places it
            DrawImage(source, new AffineTransform((topRight.X - topLeft.X) / width, (topRight.Y - topLeft.Y) / width, (bottomLeft.X - topLeft.X) / height, (bottomLeft.Y - topLeft.Y) / height, topLeft.X, topLeft.Y), sampling, opacity);
            return;
        }

        var sink = BeginFill();
        sink.MoveTo(topLeft.X, topLeft.Y);
        sink.LineTo(topRight.X, topRight.Y, smooth: false);
        sink.LineTo(bottomRight.X, bottomRight.Y, smooth: false);
        sink.LineTo(bottomLeft.X, bottomLeft.Y, smooth: false);
        sink.Close(smooth: false);
        sink.End();

        // The corners on the canvas, and the transformation that brings its pixels back to the image
        Span<double> inverse = stackalloc double[9];
        var hasInverse = !isFlat;
        if (hasInverse)
        {
            if (_hasTransform)
            {
                topLeft = _transform.Transform(topLeft);
                topRight = _transform.Transform(topRight);
                bottomRight = _transform.Transform(bottomRight);
                bottomLeft = _transform.Transform(bottomLeft);
            }

            hasInverse = TryGetInverseProjection(topLeft, topRight, bottomRight, bottomLeft, width, height, inverse);
        }

        var evaluator = _imagePaint ??= new ImageEvaluator(_image.Owner.Scope);
        evaluator.PrepareProjective(source, hasInverse ? inverse : default, sampling, _frames[0], Width);
        if (coverage == 0 || !hasInverse || _edges!.Count == 0)
            return;

        if (coverage != CoverageRasterizer.FullCoverage)
        {
            EnsureCoverageRow();
        }

        if (_rasterizer!.Begin(_edges, FillRule.NonZero, _cancellationToken))
        {
            Paint(default, evaluator, CompositeMode.SourceOver, BlendMode.Normal, coverage);
        }

        static double Cross(PointD a, PointD b, PointD c) => ((b.X - a.X) * (c.Y - b.Y)) - ((b.Y - a.Y) * (c.X - b.X));
    }

    /// <summary>
    /// Gets the projective transformation that brings the positions of a quadrilateral back to the image that is drawn
    /// on it: nine numbers, row by row, whose third row is positive on the quadrilateral.
    /// </summary>
    /// <remarks>
    /// The transformation from the unit square to the quadrilateral is the one of "Fundamentals of Texture Mapping and
    /// Image Warping" (Heckbert, 1989); it is scaled to the size of the image, then inverted by its adjugate, which is
    /// its inverse up to a factor that does not change a position.
    /// </remarks>
    /// <returns><see langword="false"/> when the quadrilateral is flattened to a line: nothing brings its positions back.</returns>
    private static bool TryGetInverseProjection(PointD topLeft, PointD topRight, PointD bottomRight, PointD bottomLeft, int width, int height, Span<double> inverse)
    {
        var dx1 = topRight.X - bottomRight.X;
        var dx2 = bottomLeft.X - bottomRight.X;
        var dx3 = topLeft.X - topRight.X + bottomRight.X - bottomLeft.X;
        var dy1 = topRight.Y - bottomRight.Y;
        var dy2 = bottomLeft.Y - bottomRight.Y;
        var dy3 = topLeft.Y - topRight.Y + bottomRight.Y - bottomLeft.Y;
        var denominator = (dx1 * dy2) - (dx2 * dy1);
        if (denominator == 0 || !double.IsFinite(denominator))
            return false;

        var g = ((dx3 * dy2) - (dx2 * dy3)) / denominator;
        var h = ((dx1 * dy3) - (dx3 * dy1)) / denominator;

        // From the pixels of the image to the canvas: (a u + b v + c, d u + e v + f) / (g u + h v + 1)
        var a = (topRight.X - topLeft.X + (g * topRight.X)) / width;
        var b = (bottomLeft.X - topLeft.X + (h * bottomLeft.X)) / height;
        var c = topLeft.X;
        var d = (topRight.Y - topLeft.Y + (g * topRight.Y)) / width;
        var e = (bottomLeft.Y - topLeft.Y + (h * bottomLeft.Y)) / height;
        var f = topLeft.Y;
        g /= width;
        h /= height;
        inverse[0] = e - (f * h);
        inverse[1] = (c * h) - b;
        inverse[2] = (b * f) - (c * e);
        inverse[3] = (f * g) - d;
        inverse[4] = a - (c * g);
        inverse[5] = (c * d) - (a * f);
        inverse[6] = (d * h) - (e * g);
        inverse[7] = (b * g) - (a * h);
        inverse[8] = (a * e) - (b * d);

        // The third row has one sign on the quadrilateral: positive, so that what is beyond the horizon is negative
        var centerX = (topLeft.X + topRight.X + bottomRight.X + bottomLeft.X) / 4;
        var centerY = (topLeft.Y + topRight.Y + bottomRight.Y + bottomLeft.Y) / 4;
        var w = (inverse[6] * centerX) + (inverse[7] * centerY) + inverse[8];
        if (!double.IsFinite(w) || w == 0)
            return false;

        if (w < 0)
        {
            foreach (ref var value in inverse)
            {
                value = -value;
            }
        }

        return true;
    }

    private static void ValidateSampling(ImageSampling sampling)
    {
        // Not Enum.IsDefined: a draw call allocates nothing
        if (sampling is not (ImageSampling.Bilinear or ImageSampling.Nearest or ImageSampling.Smooth))
            throw new ArgumentOutOfRangeException(nameof(sampling), sampling, "The sampling is not valid.");
    }

    private void DrawImageCore(ImageFrame source, Rectangle? sourceRegion, RectangleD destination, ImageSampling sampling, double opacity)
    {
        var coverage = ImageEvaluator.GetOpacityCoverage(opacity, nameof(opacity));
        var region = sourceRegion ?? new Rectangle(0, 0, source.Width, source.Height);
        var sink = BeginFill();
        AddRectangle(sink, destination);
        sink.End();

        // The region of the image is stretched to the rectangle: its pixels on the edges are extended, so that the
        // edges of the rectangle are the only ones that are antialiased. A region without pixels is validated, and
        // paints nothing.
        var transform = default(AffineTransform);
        if (!region.IsEmpty && !destination.IsEmpty)
        {
            var scaleX = destination.Width / region.Width;
            var scaleY = destination.Height / region.Height;
            transform = new AffineTransform(scaleX, 0, 0, scaleY, destination.X - (region.X * scaleX), destination.Y - (region.Y * scaleY));
        }

        PaintImage(source, region, transform, sampling, coverage, region.IsEmpty || destination.IsEmpty, nameof(sourceRegion));
    }

    /// <summary>Paints the edges of the call with a region of an image, placed by a transformation, its pixels on the edges extended.</summary>
    private void PaintImage(ImageFrame source, Rectangle region, AffineTransform transform, ImageSampling sampling, uint coverage, bool isEmpty, string regionParameterName)
    {
        var evaluator = _imagePaint ??= new ImageEvaluator(_image.Owner.Scope);
        evaluator.Prepare(source, region, ToCanvas(transform), ImageWrapMode.Pad, sampling, _frames[0], Width, regionParameterName);
        if (coverage == 0 || isEmpty || _edges!.Count == 0)
            return;

        if (coverage != CoverageRasterizer.FullCoverage)
        {
            EnsureCoverageRow();
        }

        if (_rasterizer!.Begin(_edges, FillRule.NonZero, _cancellationToken))
        {
            Paint(default, evaluator, CompositeMode.SourceOver, BlendMode.Normal, coverage);
        }
    }

    public void Fill(Brush brush, VectorPath path, FillRule fillRule)
    {
        ArgumentNullException.ThrowIfNull(brush);
        ArgumentNullException.ThrowIfNull(path);
        // Not Enum.IsDefined: the runtime keeps what it knows about an enumeration in a cache that a garbage collection
        // may drop, and builds it again on the next call. A draw call allocates nothing.
        if (fillRule is not (FillRule.NonZero or FillRule.EvenOdd))
            throw new ArgumentOutOfRangeException(nameof(fillRule), fillRule, "The fill rule is not valid.");

        PathFlattener.Flatten(path, BeginFill());
        Render(brush, fillRule);
    }

    public void Stroke(Pen pen, VectorPath path)
    {
        ArgumentNullException.ThrowIfNull(pen);
        ArgumentNullException.ThrowIfNull(path);
        PathFlattener.Flatten(path, BeginStroke(pen));
        RenderStroke(pen);
    }

    public void FillRectangle(Brush brush, RectangleD rectangle)
    {
        ArgumentNullException.ThrowIfNull(brush);
        DrawingGuard.Rectangle(rectangle, nameof(rectangle));
        var sink = BeginFill();
        AddRectangle(sink, rectangle);
        sink.End();
        Render(brush, FillRule.NonZero);
    }

    public void StrokeRectangle(Pen pen, RectangleD rectangle)
    {
        ArgumentNullException.ThrowIfNull(pen);
        DrawingGuard.Rectangle(rectangle, nameof(rectangle));
        var sink = BeginStroke(pen);
        AddRectangle(sink, rectangle);
        sink.End();
        RenderStroke(pen);
    }

    public void FillRoundedRectangle(Brush brush, RectangleD rectangle, double radius)
    {
        ArgumentNullException.ThrowIfNull(brush);
        DrawingGuard.Rectangle(rectangle, nameof(rectangle));
        DrawingGuard.Length(radius, nameof(radius));
        var sink = BeginFill();
        var receiver = new SinkPathReceiver(sink);

        // The corners stay circular: the radius is limited by the shortest side
        radius = Math.Min(radius, Math.Min(rectangle.Width, rectangle.Height) / 2);
        ArcGeometry.AppendRoundedRectangle(ref receiver, rectangle, radius, radius);
        sink.End();
        Render(brush, FillRule.NonZero);
    }

    public void StrokeRoundedRectangle(Pen pen, RectangleD rectangle, double radius)
    {
        ArgumentNullException.ThrowIfNull(pen);
        DrawingGuard.Rectangle(rectangle, nameof(rectangle));
        DrawingGuard.Length(radius, nameof(radius));
        var sink = BeginStroke(pen);
        var receiver = new SinkPathReceiver(sink);

        // The corners stay circular: the radius is limited by the shortest side
        radius = Math.Min(radius, Math.Min(rectangle.Width, rectangle.Height) / 2);
        ArcGeometry.AppendRoundedRectangle(ref receiver, rectangle, radius, radius);
        sink.End();
        RenderStroke(pen);
    }

    public void FillRoundedRectangle(Brush brush, RectangleD rectangle, CornerRadii radii)
    {
        ArgumentNullException.ThrowIfNull(brush);
        DrawingGuard.Rectangle(rectangle, nameof(rectangle));
        var sink = BeginFill();
        var receiver = new SinkPathReceiver(sink);
        ArcGeometry.AppendRoundedRectangle(ref receiver, rectangle, radii);
        sink.End();
        Render(brush, FillRule.NonZero);
    }

    public void StrokeRoundedRectangle(Pen pen, RectangleD rectangle, CornerRadii radii)
    {
        ArgumentNullException.ThrowIfNull(pen);
        DrawingGuard.Rectangle(rectangle, nameof(rectangle));
        var sink = BeginStroke(pen);
        var receiver = new SinkPathReceiver(sink);
        ArcGeometry.AppendRoundedRectangle(ref receiver, rectangle, radii);
        sink.End();
        RenderStroke(pen);
    }

    public void FillPolygon(Brush brush, ReadOnlySpan<PointD> points, FillRule fillRule)
    {
        ArgumentNullException.ThrowIfNull(brush);
        ValidatePoints(points, nameof(points));
        if (fillRule is not (FillRule.NonZero or FillRule.EvenOdd))
            throw new ArgumentOutOfRangeException(nameof(fillRule), fillRule, "The fill rule is not valid.");

        var sink = BeginFill();
        AddPoints(sink, points, close: true);
        Render(brush, fillRule);
    }

    public void StrokePoints(Pen pen, ReadOnlySpan<PointD> points, bool close)
    {
        ArgumentNullException.ThrowIfNull(pen);
        ValidatePoints(points, nameof(points));
        var sink = BeginStroke(pen);
        AddPoints(sink, points, close);
        RenderStroke(pen);
    }

    public void FillClosedCurve(Brush brush, ReadOnlySpan<PointD> points, double tension, FillRule fillRule)
    {
        ArgumentNullException.ThrowIfNull(brush);
        CardinalSpline.Validate(points, tension, isClosed: true, nameof(points), nameof(tension));
        if (fillRule is not (FillRule.NonZero or FillRule.EvenOdd))
            throw new ArgumentOutOfRangeException(nameof(fillRule), fillRule, "The fill rule is not valid.");

        var sink = BeginFill();
        AddCurve(sink, points, tension, isClosed: true);
        Render(brush, fillRule);
    }

    public void StrokeCurve(Pen pen, ReadOnlySpan<PointD> points, double tension, bool close)
    {
        ArgumentNullException.ThrowIfNull(pen);
        CardinalSpline.Validate(points, tension, close, nameof(points), nameof(tension));
        var sink = BeginStroke(pen);
        AddCurve(sink, points, tension, close);
        RenderStroke(pen);
    }

    private static void AddCurve(PathSink sink, ReadOnlySpan<PointD> points, double tension, bool isClosed)
    {
        sink.MoveTo(points[0].X, points[0].Y);
        var count = CardinalSpline.GetSegmentCount(points.Length, isClosed);
        var start = points[0];
        for (var i = 0; i < count; i++)
        {
            CardinalSpline.GetSegment(points, i, isClosed, tension, out var control1, out var control2, out var end);
            PathFlattener.Cubic(sink, start.X, start.Y, control1.X, control1.Y, control2.X, control2.Y, end.X, end.Y);
            start = end;
        }

        if (isClosed)
        {
            sink.Close(smooth: false);
        }

        sink.End();
    }

    public void FillPie(Brush brush, PointD center, double radius, double startAngle, double sweepAngle)
    {
        ArgumentNullException.ThrowIfNull(brush);
        ValidateArc(center, radius, startAngle, sweepAngle);
        var sink = BeginFill();
        var receiver = new SinkPathReceiver(sink);
        sink.MoveTo(center.X, center.Y);
        ArcGeometry.AppendCenterArc(ref receiver, center.X, center.Y, radius, radius, startAngle, sweepAngle, lineToStart: true);
        sink.Close(smooth: false);
        sink.End();
        Render(brush, FillRule.NonZero);
    }

    public void StrokeArc(Pen pen, PointD center, double radius, double startAngle, double sweepAngle)
    {
        ArgumentNullException.ThrowIfNull(pen);
        ValidateArc(center, radius, startAngle, sweepAngle);
        var sink = BeginStroke(pen);
        var receiver = new SinkPathReceiver(sink);
        ArcGeometry.AppendCenterArc(ref receiver, center.X, center.Y, radius, radius, startAngle, sweepAngle, lineToStart: false);
        sink.End();
        RenderStroke(pen);
    }

    private static void ValidateArc(PointD center, double radius, double startAngle, double sweepAngle)
    {
        DrawingGuard.Point(center, nameof(center));
        DrawingGuard.Length(radius, nameof(radius));
        DrawingGuard.Angle(startAngle, nameof(startAngle));
        DrawingGuard.Angle(sweepAngle, nameof(sweepAngle));
    }

    private static void ValidatePoints(ReadOnlySpan<PointD> points, string paramName)
    {
        if (points.IsEmpty)
            throw new ArgumentException("At least one point is required.", paramName);

        foreach (var point in points)
        {
            DrawingGuard.Point(point, paramName);
        }
    }

    private static void AddPoints(PathSink sink, ReadOnlySpan<PointD> points, bool close)
    {
        sink.MoveTo(points[0].X, points[0].Y);
        foreach (var point in points[1..])
        {
            sink.LineTo(point.X, point.Y, smooth: false);
        }

        if (close)
        {
            sink.Close(smooth: false);
        }

        sink.End();
    }

    public void FillEllipse(Brush brush, PointD center, double radiusX, double radiusY)
    {
        ArgumentNullException.ThrowIfNull(brush);
        DrawingGuard.Point(center, nameof(center));
        DrawingGuard.Length(radiusX, nameof(radiusX));
        DrawingGuard.Length(radiusY, nameof(radiusY));
        var sink = BeginFill();
        PathFlattener.Ellipse(sink, center.X, center.Y, radiusX, radiusY);
        sink.End();
        Render(brush, FillRule.NonZero);
    }

    public void StrokeEllipse(Pen pen, PointD center, double radiusX, double radiusY)
    {
        ArgumentNullException.ThrowIfNull(pen);
        DrawingGuard.Point(center, nameof(center));
        DrawingGuard.Length(radiusX, nameof(radiusX));
        DrawingGuard.Length(radiusY, nameof(radiusY));
        var sink = BeginStroke(pen);
        PathFlattener.Ellipse(sink, center.X, center.Y, radiusX, radiusY);
        sink.End();
        RenderStroke(pen);
    }

    public void StrokeLine(Pen pen, PointD start, PointD end)
    {
        ArgumentNullException.ThrowIfNull(pen);
        DrawingGuard.Point(start, nameof(start));
        DrawingGuard.Point(end, nameof(end));
        var sink = BeginStroke(pen);
        sink.MoveTo(start.X, start.Y);
        sink.LineTo(end.X, end.Y, smooth: false);
        sink.End();
        RenderStroke(pen);
    }

    public void StrokeBezier(Pen pen, PointD start, PointD control, PointD end)
    {
        ArgumentNullException.ThrowIfNull(pen);
        DrawingGuard.Point(start, nameof(start));
        DrawingGuard.Point(control, nameof(control));
        DrawingGuard.Point(end, nameof(end));
        var sink = BeginStroke(pen);
        sink.MoveTo(start.X, start.Y);
        PathFlattener.Quadratic(sink, start.X, start.Y, control.X, control.Y, end.X, end.Y);
        sink.End();
        RenderStroke(pen);
    }

    public void StrokeBezier(Pen pen, PointD start, PointD control1, PointD control2, PointD end)
    {
        ArgumentNullException.ThrowIfNull(pen);
        DrawingGuard.Point(start, nameof(start));
        DrawingGuard.Point(control1, nameof(control1));
        DrawingGuard.Point(control2, nameof(control2));
        DrawingGuard.Point(end, nameof(end));
        var sink = BeginStroke(pen);
        sink.MoveTo(start.X, start.Y);
        PathFlattener.Cubic(sink, start.X, start.Y, control1.X, control1.Y, control2.X, control2.Y, end.X, end.Y);
        sink.End();
        RenderStroke(pen);
    }

    public void FillText(Brush brush, TextLayout layout, PointD origin)
    {
        ArgumentNullException.ThrowIfNull(brush);
        ArgumentNullException.ThrowIfNull(layout);
        DrawingGuard.Point(origin, nameof(origin));
        if (layout.BrushCount <= 1 && !layout.HasColorGlyphs && !layout.HasBackgrounds && layout.OutlineCount == 0)
        {
            var glyphSink = _glyphSink ??= new GlyphPathSink();
            FillFrameWhenCovered(layout.Emit(BeginFill(), glyphSink, origin.X, origin.Y, _edges, brush: -1, emboldener: GetEmboldener(layout)));
            Render(layout.BrushCount == 0 ? brush : layout.GetBrush(0) ?? brush, FillRule.NonZero);
            return;
        }

        FillTextParts(brush, layout, origin.X, origin.Y, measure: null, options: null);
    }

    /// <summary>
    /// Draws a text that is more than one shape of one paint: the backgrounds of its styles, one shape per paint, the
    /// glyphs a font draws with its own colors, then the strokes of the pens of its styles.
    /// </summary>
    /// <remarks>
    /// Every rental of a call happens before its first pixel write: everything is drawn a first time without being
    /// painted, so that the working storage has grown to the largest of the shapes, of the clips of the pens that
    /// stroke one side of their path and of the color glyphs, then a second time, painted.
    /// </remarks>
    private void FillTextParts(Brush brush, TextLayout layout, double originX, double originY, PathMeasure? measure, TextPathOptions? options)
    {
        _isPreparing = true;
        try
        {
            DrawTextParts(brush, layout, originX, originY, measure, options);
        }
        finally
        {
            _isPreparing = false;
        }

        DrawTextParts(brush, layout, originX, originY, measure, options);
    }

    private void DrawTextParts(Brush brush, TextLayout layout, double originX, double originY, PathMeasure? measure, TextPathOptions? options)
    {
        var glyphSink = _glyphSink ??= new GlyphPathSink();
        var emboldener = GetEmboldener(layout);
        if (layout.HasBackgrounds)
        {
            // Under the whole text: the background of a style never hides the glyphs of another one
            for (var i = 0; i < layout.BrushCount; i++)
            {
                if (layout.GetBrush(i) is not { } background)
                    continue;

                if (measure is null)
                {
                    layout.EmitBackgrounds(BeginFill(), originX, originY, i);
                }
                else
                {
                    layout.EmitBackgroundsOnPath(BeginFill(), measure, options!, i);
                }

                Render(background, FillRule.NonZero);
            }
        }

        for (var i = 0; i < layout.BrushCount; i++)
        {
            FillFrameWhenCovered(measure is null
                ? layout.Emit(BeginFill(), glyphSink, originX, originY, _edges, i, skipColorGlyphs: true, emboldener)
                : layout.EmitOnPath(BeginFill(), glyphSink, measure, options!, _edges, i, skipColorGlyphs: true, emboldener));
            Render(layout.GetBrush(i) ?? brush, FillRule.NonZero);
        }

        DrawColorGlyphs(brush, layout, originX, originY, measure, options, paint: !_isPreparing);
        for (var i = 0; i < layout.OutlineCount; i++)
        {
            var pen = layout.GetOutline(i);
            var sink = BeginStroke(pen);
            if (measure is null)
            {
                layout.Emit(sink, glyphSink, originX, originY, edges: null, brush: -1, skipColorGlyphs: true, outline: i);
            }
            else
            {
                layout.EmitOnPath(sink, glyphSink, measure, options!, edges: null, brush: -1, skipColorGlyphs: true, outline: i);
            }

            RenderStroke(pen);
        }
    }

    public void StrokeText(Pen pen, TextLayout layout, PointD origin)
    {
        ArgumentNullException.ThrowIfNull(pen);
        ArgumentNullException.ThrowIfNull(layout);
        DrawingGuard.Point(origin, nameof(origin));
        var sink = BeginStroke(pen);
        layout.Emit(sink, _glyphSink ??= new GlyphPathSink(), origin.X, origin.Y, edges: null, brush: -1);
        RenderStroke(pen);
    }

    public void FillText(Brush brush, TextLayout layout, VectorPath path, TextPathOptions? options)
    {
        ArgumentNullException.ThrowIfNull(brush);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(path);
        options ??= TextPathOptions.Default;
        var measure = Measure(path);
        if (layout.BrushCount <= 1 && !layout.HasColorGlyphs && !layout.HasBackgrounds && layout.OutlineCount == 0)
        {
            var glyphSink = _glyphSink ??= new GlyphPathSink();
            FillFrameWhenCovered(layout.EmitOnPath(BeginFill(), glyphSink, measure, options, _edges, brush: -1, emboldener: GetEmboldener(layout)));
            Render(layout.BrushCount == 0 ? brush : layout.GetBrush(0) ?? brush, FillRule.NonZero);
            return;
        }

        FillTextParts(brush, layout, 0, 0, measure, options);
    }

    public void StrokeText(Pen pen, TextLayout layout, VectorPath path, TextPathOptions? options)
    {
        ArgumentNullException.ThrowIfNull(pen);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(path);
        var measure = Measure(path);
        var sink = BeginStroke(pen);
        layout.EmitOnPath(sink, _glyphSink ??= new GlyphPathSink(), measure, options ?? TextPathOptions.Default, edges: null, brush: -1);
        RenderStroke(pen);
    }

    /// <summary>
    /// Replaces the edges of a text by the frame when the stroke of a synthetic bold covers it: the stroker then
    /// stopped adding edges, and the text covers every pixel.
    /// </summary>
    private void FillFrameWhenCovered(bool coversFrame)
    {
        if (coversFrame)
        {
            var edges = _edges!;
            edges.Reset();
            AddFrame(edges);
        }
    }

    /// <summary>Gets the stroker that thickens the glyphs of a synthetic bold, which the transformation of the canvas enlarges with them.</summary>
    private Stroker? GetEmboldener(TextLayout layout)
    {
        if (!layout.HasEmboldenedGlyphs)
            return null;

        var stroker = _stroker ??= new Stroker(_edges!);
        stroker.SetOutput(_hasTransform, _transformMatrix, _transformScale);
        return stroker;
    }

    /// <summary>Flattens a path and measures its chords.</summary>
    private PathMeasure Measure(VectorPath path)
    {
        var measure = _pathMeasure ??= new PathMeasure(_image.Owner.Scope);
        measure.Scale = _transformScale;
        measure.Reset();
        PathFlattener.Flatten(path, measure);
        return measure;
    }

    public void Dispose()
    {
        IsActive = false;
        DisposeRendering();
        DisposeLayers();
        DisposeClips();
        DisposePatterns();
        _pathMeasure?.Dispose();
        _pathMeasure = null;
        _dasher?.Dispose();
        _dasher = null;
        _markers?.Dispose();
        _markers = null;
        _strokeRecorder?.Dispose();
        _strokeRecorder = null;
        _colorSurface?.Dispose();
        _colorSurface = null;
        _foregroundGradient?.Dispose();
        _foregroundGradient = null;
        _glyphScratch?.Dispose();
        _glyphScratch = null;
        _gradient?.Dispose();
        _imagePaint?.Dispose();
        _imagePaint = null;
        _rasterizer?.Dispose();
        _edges?.Dispose();
        _gradient = null;
        _rasterizer = null;
        _edges = null;
        for (var i = _leaseCount - 1; i >= 0; i--)
        {
            var storage = _leasedStorages[i];
            storage.Owner.ReleaseLease(storage, _tokens[i]);
        }

        _leaseCount = 0;
    }

    private static void AddRectangle(PathSink sink, RectangleD rectangle)
    {
        sink.MoveTo(rectangle.Left, rectangle.Top);
        sink.LineTo(rectangle.Right, rectangle.Top, smooth: false);
        sink.LineTo(rectangle.Right, rectangle.Bottom, smooth: false);
        sink.LineTo(rectangle.Left, rectangle.Bottom, smooth: false);
        sink.Close(smooth: false);
    }

    /// <summary>Adds the outline of the whole frame: its two vertical sides (horizontal edges enclose nothing by themselves).</summary>
    private void AddFrame(EdgeBuilder edges)
    {
        edges.Add(0, 0, 0, Height);
        edges.Add(Width, Height, Width, 0);
    }

    /// <summary>Starts a draw call: the arguments are valid, nothing was changed yet.</summary>
    private EdgeBuilder BeginCall()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var edges = _edges!;
        edges.Reset();
        return edges;
    }

    /// <summary>Gets the sink of a fill whose shape is given in the pixels of the canvas, whatever its transformation.</summary>
    private FillSink BeginDeviceFill()
    {
        var edges = BeginCall();
        return _fillSink ??= new FillSink(edges);
    }

    /// <summary>Gets the sink the path of a stroke is sent to; <see cref="RenderStroke"/> then paints the stroke.</summary>
    private PathSink BeginStroke(Pen pen)
    {
        if (pen.Alignment == PenAlignment.Center)
            return BeginStrokeCore(pen, widthFactor: 1);

        // The path is both a clip and a stroke: it is kept, and nothing is built before it is complete
        _cancellationToken.ThrowIfCancellationRequested();
        var recorder = _strokeRecorder ??= new PathRecorder(_image.Owner.Scope);
        recorder.Reset();
        recorder.Scale = _transformScale;
        return recorder;
    }

    private PathSink BeginStrokeCore(Pen pen, double widthFactor)
    {
        var edges = BeginCall();
        var stroker = _stroker ??= new Stroker(edges);

        // A pen the transformation does not apply to strokes the path as it is on the canvas: the vertices of the
        // path are transformed before they reach the pen, and nothing after it
        var isPlacedFirst = IsPlacedBeforeStroke(pen);
        var hasOutputTransform = _hasTransform && !isPlacedFirst;
        var scale = hasOutputTransform ? _transformScale : 1;
        stroker.SetOutput(hasOutputTransform, _transformMatrix, _transformScale);
        stroker.Configure(pen, widthFactor);
        PathSink sink = stroker;
        if (pen.IsDashed)
        {
            var dasher = _dasher ??= new Dasher(_image.Owner.Scope);
            dasher.Scale = scale;
            if (hasOutputTransform)
            {
                // The dashes are cut in the coordinates of the drawing, where the canvas is not a rectangle: all of them
                dasher.Configure(pen, stroker, _cancellationToken);
            }
            else
            {
                dasher.Configure(pen, stroker, Width, Height, _cancellationToken, widthFactor);
            }

            sink = dasher;
        }

        if (pen.HasMarkers)
        {
            var markers = _markers ??= new MarkerSink(_image.Owner.Scope);
            markers.Scale = scale;
            markers.Configure(pen, stroker, sink);
            sink = markers;
        }

        if (isPlacedFirst)
        {
            var placed = _strokeTransformSink ??= new TransformSink();
            placed.Configure(sink, _transformMatrix, _transformScale);
            sink = placed;
        }

        return sink;
    }

    /// <summary>Gets a value indicating whether the path of a stroke is placed on the canvas before the pen strokes it: the pen is then not transformed with the path.</summary>
    private bool IsPlacedBeforeStroke(Pen pen) => _hasTransform && !pen.ScalesWithTransform;

    /// <summary>Renders the pieces the stroker emitted, or the whole frame when one of them contains it.</summary>
    private void RenderStroke(Pen pen)
    {
        if (pen.Alignment == PenAlignment.Center)
        {
            RenderStrokeCore(pen);
            return;
        }

        // The side of the path the stroke is on: a clip made of what the path fills, or of what it does not. Then
        // the stroke of a pen twice as wide, of which the clip keeps one half.
        var recorder = _strokeRecorder!;
        recorder.Replay(BeginFill());
        var isOutside = pen.Alignment == PenAlignment.Outside;
        var bounds = recorder.Bounds;
        if (isOutside)
        {
            // What a stroke reaches around its path: its joins and its caps, and its markers anywhere
            var reach = pen.HasMarkers ? DrawingGuard.MaxCoordinate : pen.Width * Math.Max(pen.MiterLimit, 2);
            if (IsPlacedBeforeStroke(pen))
            {
                // The reach is a distance of the canvas, and the bounds are those of the drawing: the distance of
                // the drawing that the transformation shortens the most to it. Written so that a transformation
                // that flattens the plane gives the largest one.
                var shortest = Math.Abs(_transform.Determinant) / _transformScale;
                reach = shortest > 0 && reach / shortest <= DrawingGuard.MaxCoordinate ? reach / shortest : DrawingGuard.MaxCoordinate;
            }

            bounds = RectangleD.FromLTRB(
                Math.Max(bounds.Left - reach, -DrawingGuard.MaxCoordinate),
                Math.Max(bounds.Top - reach, -DrawingGuard.MaxCoordinate),
                Math.Min(bounds.Right + reach, DrawingGuard.MaxCoordinate),
                Math.Min(bounds.Bottom + reach, DrawingGuard.MaxCoordinate));
        }

        PushClipFromEdges(FillRule.NonZero, bounds, isOutside);
        try
        {
            recorder.Replay(BeginStrokeCore(pen, widthFactor: 2));
            RenderStrokeCore(pen);
        }
        finally
        {
            PopClip();
        }
    }

    private void RenderStrokeCore(Pen pen)
    {
        if (_stroker!.CoversFrame)
        {
            var edges = _edges!;
            edges.Reset();
            AddFrame(edges);
        }

        Render(pen.Brush, FillRule.NonZero);
    }

    /// <summary>Rasterizes the edges of the call and composites every row onto every frame.</summary>
    private void Render(Brush brush, FillRule fillRule)
    {
        // A shape that is only prepared rents what it needs, and changes no pixel
        if (Prepare(brush, fillRule, out var color, out var evaluator) && !_isPreparing)
        {
            Paint(color, evaluator, brush.CompositeMode, brush.BlendMode, brush is ImageBrush image ? image.OpacityCoverage : CoverageRasterizer.FullCoverage);
        }
    }

    /// <summary>Composites every row of the sweep that was prepared onto every frame.</summary>
    /// <param name="color">The color of the paint, when it has a single one.</param>
    /// <param name="evaluator">What gives the color of each pixel, or <see langword="null"/> for a single color.</param>
    /// <param name="mode">The operator that combines the paint with the pixels.</param>
    /// <param name="blend">The blend mode that mixes the color of the paint with the color of the pixels.</param>
    /// <param name="opacity">The coverage every coverage is multiplied by.</param>
    private void Paint(DrawingColor color, PaintEvaluator? evaluator, CompositeMode mode, BlendMode blend, uint opacity = CoverageRasterizer.FullCoverage)
    {
        // What the paint reads is held before the first pixel is changed
        evaluator?.Begin();
        try
        {
            PaintRows(color, evaluator, mode, blend, opacity);
        }
        finally
        {
            evaluator?.End();
        }
    }

    private void PaintRows(DrawingColor color, PaintEvaluator? evaluator, CompositeMode mode, BlendMode blend, uint opacity)
    {
        if (!_thumbnailRemoved)
        {
            // The pixels are about to change: the EXIF thumbnail no longer shows them
            _image.Metadata.RemoveStaleThumbnail();
            _thumbnailRemoved = true;
        }

        var rasterizer = _rasterizer!;
        while (rasterizer.MoveNext())
        {
            var y = rasterizer.Y;
            var x = rasterizer.X;
            var coverage = rasterizer.Coverage;
            if (_isAliased && !RemoveAntialiasing(ref coverage))
                continue;

            if (_clipDepth != 0 && !ApplyClip(y, ref x, ref coverage, out _))
                continue;

            if (opacity != CoverageRasterizer.FullCoverage)
            {
                coverage = ApplyOpacity(coverage, opacity);
            }

            // A paint that gives the whole row one color is composited as a single color: the same formulas
            var source = evaluator is null ? new DrawingSource(color, mode, blend, _transfer, _curves)
                : evaluator.TryGetRowColor(y, x, out var rowColor) ? new DrawingSource(rowColor, mode, blend, _transfer, _curves)
                : new DrawingSource(evaluator.Evaluate(y, x, coverage.Length), mode, blend, _transfer, _curves);
            for (var i = 0; i < _frames.Length; i++)
            {
                _frames[i].CompositePixels(_storages[i], y, x, coverage, source);
            }
        }
    }

    /// <summary>Prepares the paint and the sweep of the edges of the call: every rental happens here, and no pixel is changed.</summary>
    /// <returns><see langword="false"/> when there is nothing to paint.</returns>
    private bool Prepare(Brush brush, FillRule fillRule, out DrawingColor color, out PaintEvaluator? evaluator)
    {
        color = default;
        evaluator = null;
        var edges = _edges!;
        if (edges.Count == 0)
            return false;

        // A paint without opacity changes nothing with most operators; with the others, it erases
        var isInert = DrawingKernels.IsTransparentPaintInert(brush.CompositeMode);
        if (brush is SolidBrush solid)
        {
            color = _frames[0].ConvertDrawingColor(ToImageColor(solid.Color));
            if (color.Alpha == 0 && isInert)
                return false;
        }
        else if (brush is ImageBrush image)
        {
            var imagePaint = _imagePaint ??= new ImageEvaluator(_image.Owner.Scope);
            imagePaint.Prepare(image.Source, image.SourceRegion, ToCanvas(image.Transform), image.WrapMode, image.Sampling, _frames[0], Width, nameof(brush));
            if (image.OpacityCoverage == 0)
                return false;

            if (image.OpacityCoverage != CoverageRasterizer.FullCoverage)
            {
                EnsureCoverageRow();
            }

            evaluator = imagePaint;
        }
        else if (brush is PatternBrush pattern)
        {
            // The tile is drawn first: it is an image when the shape is painted
            if (PreparePattern(pattern, out var placement) is not { } tile)
                return false;

            var imagePaint = _imagePaint ??= new ImageEvaluator(_image.Owner.Scope);
            imagePaint.Prepare(tile, region: null, placement, ImageWrapMode.Repeat, ImageSampling.Bilinear, _frames[0], Width, nameof(brush));
            evaluator = imagePaint;
        }
        else
        {
            var gradient = _gradient ??= new GradientEvaluator(_image.Owner.Scope);
            var gradientBrush = (GradientBrush)brush;
            gradient.Prepare(gradientBrush, ToCanvas(gradientBrush.Transform), _frames[0], Width, _paintPipeline);
            if (gradient.IsTransparent && isInert)
                return false;

            evaluator = gradient;
        }

        return _rasterizer!.Begin(edges, fillRule, _cancellationToken);
    }
}
