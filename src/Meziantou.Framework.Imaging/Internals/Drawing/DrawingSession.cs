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
    private readonly ImageFrame[] _frames;
    private readonly PixelStorage[] _storages;
    private readonly long[] _tokens;
    private readonly CancellationToken _cancellationToken;
    private int _leaseCount;
    private EdgeBuilder? _edges;
    private CoverageRasterizer? _rasterizer;
    private FillSink? _fillSink;
    private Stroker? _stroker;
    private Dasher? _dasher;
    private GradientEvaluator? _gradient;
    private ImageEvaluator? _imagePaint;
    private GlyphPathSink? _glyphSink;
    private GlyphScratch? _glyphScratch;
    private PathMeasure? _pathMeasure;
    private bool _thumbnailRemoved;

    private DrawingSession(Image image, ImageFrame[] frames, PixelStorage[] storages, CancellationToken cancellationToken)
    {
        _image = image;
        _frames = frames;
        _storages = storages;
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
        var converted = _frames[0].ConvertDrawingColor(color);
        if (_rasterizer!.Begin(_edges!, FillRule.NonZero, _cancellationToken))
        {
            Paint(converted, evaluator: null, CompositeMode.Source);
        }
    }

    public void DrawImage(ImageFrame source, PointD position, double opacity)
    {
        ArgumentNullException.ThrowIfNull(source);
        DrawingGuard.Point(position, nameof(position));

        // The image starts at a valid position: its far corner may be a few pixels beyond the largest coordinate
        DrawImageCore(source, sourceRegion: null, new RectangleD(position.X, position.Y, source.Width, source.Height), opacity);
    }

    public void DrawImage(ImageFrame source, Rectangle? sourceRegion, RectangleD destination, double opacity)
    {
        ArgumentNullException.ThrowIfNull(source);
        DrawingGuard.Rectangle(destination, nameof(destination));
        DrawImageCore(source, sourceRegion, destination, opacity);
    }

    private void DrawImageCore(ImageFrame source, Rectangle? sourceRegion, RectangleD destination, double opacity)
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

        var evaluator = _imagePaint ??= new ImageEvaluator(_image.Owner.Scope);
        evaluator.Prepare(source, region, transform, ImageWrapMode.Pad, ImageSampling.Bilinear, _frames[0], Width, nameof(sourceRegion));
        if (coverage == 0 || region.IsEmpty || destination.IsEmpty || _edges!.Count == 0)
            return;

        if (coverage != CoverageRasterizer.FullCoverage)
        {
            EnsureCoverageRow();
        }

        if (_rasterizer!.Begin(_edges, FillRule.NonZero, _cancellationToken))
        {
            Paint(default, evaluator, CompositeMode.SourceOver, coverage);
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
        var glyphSink = _glyphSink ??= new GlyphPathSink();
        var scratch = _glyphScratch ??= new GlyphScratch(_image.Owner.Scope);
        var emboldener = layout.HasEmboldenedGlyphs ? _stroker ??= new Stroker(_edges!) : null;
        if (layout.BrushCount <= 1 && !layout.HasColorGlyphs)
        {
            FillFrameWhenCovered(layout.Emit(BeginFill(), glyphSink, scratch, origin.X, origin.Y, _edges, brush: -1, emboldener: emboldener));
            Render(layout.BrushCount == 0 ? brush : layout.GetBrush(0) ?? brush, FillRule.NonZero);
            return;
        }

        // A text of several paints is one shape per paint, and each color glyph is drawn on its own. Every rental of
        // a call happens before its first pixel write: each shape is prepared once without being painted, so that the
        // working storage has grown to the largest of them, then the shapes are painted in the order of the paints,
        // and the color glyphs after them.
        for (var i = 0; i < layout.BrushCount; i++)
        {
            FillFrameWhenCovered(layout.Emit(BeginFill(), glyphSink, scratch, origin.X, origin.Y, _edges, i, skipColorGlyphs: true, emboldener));
            Prepare(layout.GetBrush(i) ?? brush, FillRule.NonZero, out _, out _);
        }

        DrawColorGlyphs(brush, layout, origin.X, origin.Y, measure: null, options: null, paint: false);
        for (var i = 0; i < layout.BrushCount; i++)
        {
            FillFrameWhenCovered(layout.Emit(BeginFill(), glyphSink, scratch, origin.X, origin.Y, _edges, i, skipColorGlyphs: true, emboldener));
            Render(layout.GetBrush(i) ?? brush, FillRule.NonZero);
        }

        DrawColorGlyphs(brush, layout, origin.X, origin.Y, measure: null, options: null, paint: true);
    }

    public void StrokeText(Pen pen, TextLayout layout, PointD origin)
    {
        ArgumentNullException.ThrowIfNull(pen);
        ArgumentNullException.ThrowIfNull(layout);
        DrawingGuard.Point(origin, nameof(origin));
        var sink = BeginStroke(pen);
        layout.Emit(sink, _glyphSink ??= new GlyphPathSink(), _glyphScratch ??= new GlyphScratch(_image.Owner.Scope), origin.X, origin.Y, edges: null, brush: -1);
        RenderStroke(pen);
    }

    public void FillText(Brush brush, TextLayout layout, VectorPath path, TextPathOptions? options)
    {
        ArgumentNullException.ThrowIfNull(brush);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(path);
        options ??= TextPathOptions.Default;
        var measure = Measure(path);
        var glyphSink = _glyphSink ??= new GlyphPathSink();
        var scratch = _glyphScratch ??= new GlyphScratch(_image.Owner.Scope);
        var emboldener = layout.HasEmboldenedGlyphs ? _stroker ??= new Stroker(_edges!) : null;
        if (layout.BrushCount <= 1 && !layout.HasColorGlyphs)
        {
            FillFrameWhenCovered(layout.EmitOnPath(BeginFill(), glyphSink, scratch, measure, options, _edges, brush: -1, emboldener: emboldener));
            Render(layout.BrushCount == 0 ? brush : layout.GetBrush(0) ?? brush, FillRule.NonZero);
            return;
        }

        // As for a text at a point: every shape is prepared before the first one is painted.
        for (var i = 0; i < layout.BrushCount; i++)
        {
            FillFrameWhenCovered(layout.EmitOnPath(BeginFill(), glyphSink, scratch, measure, options, _edges, i, skipColorGlyphs: true, emboldener));
            Prepare(layout.GetBrush(i) ?? brush, FillRule.NonZero, out _, out _);
        }

        DrawColorGlyphs(brush, layout, 0, 0, measure, options, paint: false);
        for (var i = 0; i < layout.BrushCount; i++)
        {
            FillFrameWhenCovered(layout.EmitOnPath(BeginFill(), glyphSink, scratch, measure, options, _edges, i, skipColorGlyphs: true, emboldener));
            Render(layout.GetBrush(i) ?? brush, FillRule.NonZero);
        }

        DrawColorGlyphs(brush, layout, 0, 0, measure, options, paint: true);
    }

    public void StrokeText(Pen pen, TextLayout layout, VectorPath path, TextPathOptions? options)
    {
        ArgumentNullException.ThrowIfNull(pen);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(path);
        var measure = Measure(path);
        var sink = BeginStroke(pen);
        layout.EmitOnPath(sink, _glyphSink ??= new GlyphPathSink(), _glyphScratch ??= new GlyphScratch(_image.Owner.Scope), measure, options ?? TextPathOptions.Default, edges: null, brush: -1);
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

    /// <summary>Flattens a path and measures its chords.</summary>
    private PathMeasure Measure(VectorPath path)
    {
        var measure = _pathMeasure ??= new PathMeasure(_image.Owner.Scope);
        measure.Reset();
        PathFlattener.Flatten(path, measure);
        return measure;
    }

    public void Dispose()
    {
        IsActive = false;
        DisposeClips();
        _pathMeasure?.Dispose();
        _pathMeasure = null;
        _dasher?.Dispose();
        _dasher = null;
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
            var storage = _storages[i];
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

    private FillSink BeginFill()
    {
        var edges = BeginCall();
        return _fillSink ??= new FillSink(edges);
    }

    private PathSink BeginStroke(Pen pen)
    {
        var edges = BeginCall();
        var stroker = _stroker ??= new Stroker(edges);
        stroker.Configure(pen);
        if (!pen.IsDashed)
            return stroker;

        var dasher = _dasher ??= new Dasher(_image.Owner.Scope);
        dasher.Configure(pen, stroker, Width, Height, _cancellationToken);
        return dasher;
    }

    /// <summary>Renders the pieces the stroker emitted, or the whole frame when one of them contains it.</summary>
    private void RenderStroke(Pen pen)
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
        if (Prepare(brush, fillRule, out var color, out var evaluator))
        {
            Paint(color, evaluator, brush.CompositeMode, brush is ImageBrush image ? image.OpacityCoverage : CoverageRasterizer.FullCoverage);
        }
    }

    /// <summary>Composites every row of the sweep that was prepared onto every frame.</summary>
    /// <param name="color">The color of the paint, when it has a single one.</param>
    /// <param name="evaluator">What gives the color of each pixel, or <see langword="null"/> for a single color.</param>
    /// <param name="mode">The operator that combines the paint with the pixels.</param>
    /// <param name="opacity">The coverage every coverage is multiplied by.</param>
    private void Paint(DrawingColor color, PaintEvaluator? evaluator, CompositeMode mode, uint opacity = CoverageRasterizer.FullCoverage)
    {
        // What the paint reads is held before the first pixel is changed
        evaluator?.Begin();
        try
        {
            PaintRows(color, evaluator, mode, opacity);
        }
        finally
        {
            evaluator?.End();
        }
    }

    private void PaintRows(DrawingColor color, PaintEvaluator? evaluator, CompositeMode mode, uint opacity)
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
            if (_clipDepth != 0 && !ApplyClip(y, ref x, ref coverage, out _))
                continue;

            if (opacity != CoverageRasterizer.FullCoverage)
            {
                coverage = ApplyOpacity(coverage, opacity);
            }

            var source = evaluator is null ? new DrawingSource(color, mode) : new DrawingSource(evaluator.Evaluate(y, x, coverage.Length), mode);
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
            color = _frames[0].ConvertDrawingColor(solid.Color);
            if (color.Alpha == 0 && isInert)
                return false;
        }
        else if (brush is ImageBrush image)
        {
            var imagePaint = _imagePaint ??= new ImageEvaluator(_image.Owner.Scope);
            imagePaint.Prepare(image.Source, image.SourceRegion, image.Transform, image.WrapMode, image.Sampling, _frames[0], Width, nameof(brush));
            if (image.OpacityCoverage == 0)
                return false;

            if (image.OpacityCoverage != CoverageRasterizer.FullCoverage)
            {
                EnsureCoverageRow();
            }

            evaluator = imagePaint;
        }
        else
        {
            var gradient = _gradient ??= new GradientEvaluator(_image.Owner.Scope);
            gradient.Prepare((GradientBrush)brush, _frames[0], Width);
            if (gradient.IsTransparent && isInert)
                return false;

            evaluator = gradient;
        }

        return _rasterizer!.Begin(edges, fillRule, _cancellationToken);
    }
}
