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
internal sealed class DrawingSession : IDisposable
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
    private GradientEvaluator? _gradient;
    private GlyphPathSink? _glyphSink;
    private GlyphScratch? _glyphScratch;
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
        if (layout.BrushCount <= 1)
        {
            layout.Emit(BeginFill(), glyphSink, scratch, origin.X, origin.Y, _edges, brush: -1);
            Render(layout.BrushCount == 0 ? brush : layout.GetBrush(0) ?? brush, FillRule.NonZero);
            return;
        }

        // A text of several paints is one shape per paint. Every rental of a call happens before its first pixel
        // write: each shape is prepared once without being painted, so that the working storage has grown to the
        // largest of them, then the shapes are painted in the order of the paints.
        for (var i = 0; i < layout.BrushCount; i++)
        {
            layout.Emit(BeginFill(), glyphSink, scratch, origin.X, origin.Y, _edges, i);
            Prepare(layout.GetBrush(i) ?? brush, FillRule.NonZero, out _, out _);
        }

        for (var i = 0; i < layout.BrushCount; i++)
        {
            layout.Emit(BeginFill(), glyphSink, scratch, origin.X, origin.Y, _edges, i);
            Render(layout.GetBrush(i) ?? brush, FillRule.NonZero);
        }
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

    public void Dispose()
    {
        IsActive = false;
        _glyphScratch?.Dispose();
        _glyphScratch = null;
        _gradient?.Dispose();
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

    private Stroker BeginStroke(Pen pen)
    {
        var edges = BeginCall();
        var stroker = _stroker ??= new Stroker(edges);
        stroker.Configure(pen);
        return stroker;
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
        if (!Prepare(brush, fillRule, out var color, out var gradient))
            return;

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
            var source = gradient is null ? new DrawingSource(color) : new DrawingSource(gradient.Evaluate(y, x, coverage.Length));
            for (var i = 0; i < _frames.Length; i++)
            {
                _frames[i].CompositePixels(_storages[i], y, x, coverage, source);
            }
        }
    }

    /// <summary>Prepares the paint and the sweep of the edges of the call: every rental happens here, and no pixel is changed.</summary>
    /// <returns><see langword="false"/> when there is nothing to paint.</returns>
    private bool Prepare(Brush brush, FillRule fillRule, out DrawingColor color, out GradientEvaluator? gradient)
    {
        color = default;
        gradient = null;
        var edges = _edges!;
        if (edges.Count == 0)
            return false;

        if (brush is SolidBrush solid)
        {
            color = _frames[0].ConvertDrawingColor(solid.Color);
            if (color.Alpha == 0)
                return false;
        }
        else
        {
            gradient = _gradient ??= new GradientEvaluator(_image.Owner.Scope);
            gradient.Prepare((GradientBrush)brush, _frames[0], Width);
            if (gradient.IsTransparent)
                return false;
        }

        return _rasterizer!.Begin(edges, fillRule, _cancellationToken);
    }
}
