using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Rendering;
using AffineTransform = Meziantou.Framework.Imaging.AffineTransform;
using D = Meziantou.Framework.Imaging.Drawing;
using ImageFrame = Meziantou.Framework.Imaging.ImageFrame;
using PointD = Meziantou.Framework.Imaging.PointD;
using RectangleD = Meziantou.Framework.Imaging.RectangleD;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>
/// Draws a <see cref="DisplayList"/> with the drawing API of Meziantou.Framework.Imaging. The state operations of the display
/// list (save, restore, transforms, clips, layers) are a stack, the ones of a <see cref="D.DrawingCanvas"/> are callbacks: the
/// items that follow a transform, a clip or a layer are drawn by the callback of that scope, up to the restore that ends it.
/// </summary>
internal sealed partial class DisplayListRasterizer
{
    private readonly ImagingRenderingBackend _backend;
    private readonly RenderingColorSpace _colorSpace;
    private readonly bool _antiAliasEnabled;
    private DisplayList _list = DisplayList.Empty;
    private int _index;

    /// <summary>The number of saves (and layers) that are not restored: a restore at the depth 0 is ignored.</summary>
    private int _depth;

    /// <summary>Whether the canvas antialiases what it draws (the state of the scope of the item being drawn).</summary>
    private bool _antiAlias;

    /// <summary>The transform from the coordinates of the items to the pixels of the bitmap (the 2D part of a 3D transform).</summary>
    private DisplayMatrix _matrix = DisplayMatrix.Identity;

    /// <param name="backend">The backend: its options, its fonts and its images.</param>
    /// <param name="colorSpace">The color space of the pixels of the bitmap.</param>
    public DisplayListRasterizer(ImagingRenderingBackend backend, RenderingColorSpace colorSpace)
    {
        _backend = backend;
        _colorSpace = colorSpace;
        _antiAliasEnabled = backend.Options.AntiAlias;
    }

    /// <summary>Draws a display list on a frame (the coordinates of the display list are the pixels of the frame).</summary>
    public static void Rasterize(ImagingRenderingBackend backend, ImageFrame frame, DisplayList displayList, RenderingColorSpace colorSpace)
    {
        frame.Draw((backend, displayList, colorSpace), static (canvas, state) => new DisplayListRasterizer(state.backend, state.colorSpace).Draw(canvas, state.displayList, DisplayMatrix.Identity));
    }

    /// <summary>
    /// Draws a display list on a canvas (in the state of the caller, which is restored at the end). <paramref name="matrix"/> is
    /// the transform of the canvas from the coordinates of the display list to the pixels of the bitmap.
    /// </summary>
    public void Draw(D.DrawingCanvas canvas, DisplayList displayList, DisplayMatrix matrix)
    {
        var (list, index, depth, antiAlias, previousMatrix) = (_list, _index, _depth, _antiAlias, _matrix);
        _list = displayList;
        _index = 0;
        _depth = 0;
        _matrix = matrix;
        _antiAlias = true;
        try
        {
            canvas.Flattening(FlatteningTolerance, ScopeStart, static (c, start) => start.Rewind().RunRoot(c));
        }
        finally
        {
            (_list, _index, _depth, _antiAlias, _matrix) = (list, index, depth, antiAlias, previousMatrix);
        }
    }

    /// <summary>
    /// The largest distance in device pixels between a curve and its chords: the shapes that a test and its reference draw as
    /// different curves differ on their antialiased edges by less than with the default of the drawing API (1/16 px).
    /// </summary>
    private const double FlatteningTolerance = 1d / 64;

    private void RunRoot(D.DrawingCanvas canvas)
    {
        if (_antiAliasEnabled)
        {
            RunScope(canvas);
            return;
        }

        _antiAlias = false;
        canvas.Antialiasing(false, ScopeStart, static (c, start) => start.Rewind().RunScope(c));
    }

    /// <summary>Draws a display list in a new state of the walker (a pattern, an image of a filter).</summary>
    private void DrawNested(D.DrawingCanvas canvas, DisplayList displayList, DisplayMatrix matrix) => new DisplayListRasterizer(_backend, _colorSpace).Draw(canvas, displayList, matrix);

    /// <summary>
    /// Draws the items of the current scope, up to the restore that ends it (which is consumed) or to the end of the display
    /// list. A transform or a clip starts a scope of the canvas that draws the rest of the items of the current scope.
    /// </summary>
    private void RunScope(D.DrawingCanvas canvas)
    {
        var items = _list.Items;
        while (_index < items.Length)
        {
            var item = items[_index++];
            switch (item)
            {
                case RestoreItem:
                    // A display list is balanced; a restore of the state of the caller is ignored
                    if (_depth > 0)
                        return;

                    break;

                case SaveItem:
                    _depth++;
                    RunScope(canvas);
                    _depth--;
                    break;

                case SaveLayerItem layer:
                    _depth++;
                    DrawLayer(canvas, layer);
                    _depth--;
                    break;

                case TransformItem transform:
                    if (ApplyTransform(canvas, transform.Matrix))
                        return;

                    break;

                case ClipRectItem clip:
                    ClipRect(canvas, clip.Rect, clip.AntiAlias && _antiAliasEnabled);
                    return;

                case ClipRoundedRectItem clip:
                    if (clip.Rect.IsRect)
                    {
                        ClipRect(canvas, clip.Rect.Rect, _antiAliasEnabled);
                    }
                    else
                    {
                        ClipPath(canvas, ToPath(PathConversions.GetRoundedRectPath(clip.Rect)), D.FillRule.NonZero, _antiAliasEnabled);
                    }

                    return;

                case ClipPathItem clip:
                    if (PathConversions.TryGetRect(clip.Path, out var clipRect))
                    {
                        ClipRect(canvas, clipRect, clip.AntiAlias && _antiAliasEnabled);
                    }
                    else
                    {
                        ClipPath(canvas, ToPath(clip.Path), ToFillRule(clip.Path.FillRule), clip.AntiAlias && _antiAliasEnabled);
                    }

                    return;

                default:
                    try
                    {
                        DrawItem(canvas, item);
                    }
                    catch (ArgumentException)
                    {
                        // A drawing whose geometry the drawing API cannot draw draws nothing
                    }

                    break;
            }
        }
    }

    /// <summary>The start of the rest of the current scope, for the callback of a scope of the canvas that draws it.</summary>
    private ScopeCursor ScopeStart => new(this, _index);

    /// <summary>
    /// The start of the items that the callback of a scope of the canvas draws. The drawing API can run a callback more than once
    /// (a filter draws its items again on the window of a shadow cast from beyond the margin of its layer): each run rewinds to
    /// the start of the scope, so that it draws the same items.
    /// </summary>
    private readonly record struct ScopeCursor(DisplayListRasterizer Rasterizer, int Index)
    {
        /// <summary>Moves the walker to the start of the scope, and returns it.</summary>
        public DisplayListRasterizer Rewind()
        {
            Rasterizer._index = Index;
            return Rasterizer;
        }
    }

    /// <summary>Skips the items of the current scope, up to the restore that ends it (which is consumed).</summary>
    private void SkipScope()
    {
        var items = _list.Items;
        var depth = 0;
        while (_index < items.Length)
        {
            switch (items[_index++])
            {
                case SaveItem or SaveLayerItem:
                    depth++;
                    break;

                case RestoreItem:
                    if (depth == 0 && _depth > 0)
                        return;

                    depth = Math.Max(0, depth - 1);
                    break;
            }
        }
    }

    /// <summary>Applies a transform to the rest of the scope; returns false when the transform is the identity (no scope is started).</summary>
    private bool ApplyTransform(D.DrawingCanvas canvas, DisplayMatrix matrix)
    {
        // The consecutive 2D transforms are one transform
        while (matrix.Matrix3d is null && _index < _list.Count && _list.Items[_index] is TransformItem { Matrix.Matrix3d: null } next)
        {
            matrix = matrix.PreConcat(next.Matrix);
            _index++;
        }

        if (matrix.Matrix3d is { } m)
        {
            if (!m.AsSpan().ContainsAnyExcept(0) || !IsFinite(m))
            {
                SkipScope();
                return true;
            }

            var savedMatrix = _matrix;
            _matrix = _matrix.PreConcat(matrix with { Matrix3d = null });
            var start = _index;
            canvas.Transform(PathConversions.ToTransform3D(m), ScopeStart, static (c, start) => start.Rewind().RunScope(c));
            EnsureConsumed(start);
            _matrix = savedMatrix;
            return true;
        }

        if (matrix == DisplayMatrix.Identity)
            return false;

        // The browsers draw nothing with a transform that is not invertible
        var determinant = (matrix.A * matrix.D) - (matrix.B * matrix.C);
        if (!double.IsFinite(determinant) || determinant == 0 || !double.IsFinite(matrix.E) || !double.IsFinite(matrix.F))
        {
            SkipScope();
            return true;
        }

        var previousMatrix = _matrix;
        _matrix = _matrix.PreConcat(matrix);
        var index = _index;
        canvas.Transform(PathConversions.ToAffine(matrix), ScopeStart, static (c, start) => start.Rewind().RunScope(c));
        EnsureConsumed(index);
        _matrix = previousMatrix;
        return true;

        static bool IsFinite(double[] values)
        {
            foreach (var value in values)
            {
                if (!double.IsFinite(value))
                    return false;
            }

            return true;
        }
    }

    /// <summary>Returns the part of a rectangle of the items in the range of the coordinates of the drawing API.</summary>
    private static RectangleD ToRectangle(PhysicalRect rect) => PathConversions.ToRectangle(rect);

    /// <summary>Returns the path of the drawing API of a path of the items.</summary>
    private static D.VectorPath ToPath(DisplayPath path) => PathConversions.ToVectorPath(path);

    /// <summary>Returns a point of the items in the range of the coordinates of the drawing API.</summary>
    private static PointD ToPoint(PhysicalOffset point) => PathConversions.ToPoint(point);

    /// <summary>Intersects the clip of the rest of the scope with a rectangle (without antialiasing: the pixels whose centers are in it).</summary>
    private void ClipRect(D.DrawingCanvas canvas, PhysicalRect rect, bool antiAlias)
    {
        if (!(rect.Width > 0) || !(rect.Height > 0))
        {
            // Nothing is visible
            SkipScope();
            return;
        }

        var rectangle = ToRectangle(rect);
        if (!(rectangle.Width > 0) || !(rectangle.Height > 0))
        {
            SkipScope();
            return;
        }

        var index = _index;
        if (!antiAlias && _antiAlias)
        {
            canvas.Antialiasing(false, (start: ScopeStart, rectangle), static (c, state) => c.Clip(state.rectangle, state.start, static (clipped, start) => RunWithAntialiasing(clipped, start)));
        }
        else
        {
            canvas.Clip(rectangle, ScopeStart, static (c, start) => start.Rewind().RunScope(c));
        }

        EnsureConsumed(index);
    }

    /// <summary>Intersects the clip of the rest of the scope with a path.</summary>
    private void ClipPath(D.DrawingCanvas canvas, D.VectorPath path, D.FillRule fillRule, bool antiAlias)
    {
        var index = _index;
        if (!antiAlias && _antiAlias)
        {
            canvas.Antialiasing(false, (start: ScopeStart, path, fillRule), static (c, state) => c.Clip(state.path, state.start, static (clipped, start) => RunWithAntialiasing(clipped, start), state.fillRule));
        }
        else
        {
            canvas.Clip(path, ScopeStart, static (c, start) => start.Rewind().RunScope(c), fillRule);
        }

        EnsureConsumed(index);
    }

    /// <summary>Skips the rest of the scope when the callback of a scope of the canvas did not run (nothing it draws would be visible).</summary>
    private void EnsureConsumed(int index)
    {
        if (_index == index)
        {
            SkipScope();
        }
    }

    /// <summary>Draws the rest of the scope with antialiasing (after a clip that is not antialiased).</summary>
    private static void RunWithAntialiasing(D.DrawingCanvas canvas, ScopeCursor start) => canvas.Antialiasing(true, start, static (c, start) => start.Rewind().RunScope(c));

    private void DrawItem(D.DrawingCanvas canvas, DisplayItem item)
    {
        switch (item)
        {
            case DrawPaintItem draw:
                if (CreateBrush(draw.Paint) is { } paintBrush)
                {
                    Paint(canvas, draw.Paint, paintBrush, static (c, brush) => c.Fill(brush));
                }

                break;

            case DrawRectItem draw:
                if (draw.Paint.Style == PaintStyle.Fill)
                {
                    if (draw.Rect.Width > 0 && draw.Rect.Height > 0 && CreateBrush(draw.Paint) is { } brush)
                    {
                        Paint(canvas, draw.Paint, (brush, ToRectangle(draw.Rect)), static (c, state) => c.FillRectangle(state.brush, state.Item2));
                    }
                }
                else if (draw.Rect.Width != 0 || draw.Rect.Height != 0)
                {
                    // The stroke of a rectangle without width or without height is the stroke of a line, with its joins
                    // (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-strokerect); nothing is drawn for a
                    // rectangle that has neither
                    var rect = draw.Rect;
                    var path = new D.VectorPathBuilder()
                        .MoveTo(ToPoint(new PhysicalOffset(rect.Left, rect.Top)))
                        .LineTo(ToPoint(new PhysicalOffset(rect.Right, rect.Top)))
                        .LineTo(ToPoint(new PhysicalOffset(rect.Right, rect.Bottom)))
                        .LineTo(ToPoint(new PhysicalOffset(rect.Left, rect.Bottom)))
                        .Close()
                        .Build();
                    Stroke(canvas, draw.Paint, path);
                }

                break;

            case DrawRoundedRectItem draw:
                if (draw.Rect.IsRect && draw.Paint.Style == PaintStyle.Fill)
                {
                    if (draw.Rect.Rect.Width > 0 && draw.Rect.Rect.Height > 0 && CreateBrush(draw.Paint) is { } brush)
                    {
                        Paint(canvas, draw.Paint, (brush, ToRectangle(draw.Rect.Rect)), static (c, state) => c.FillRectangle(state.brush, state.Item2));
                    }
                }
                else
                {
                    DrawPath(canvas, PathConversions.GetRoundedRectPath(draw.Rect), draw.Paint);
                }

                break;

            case DrawRingItem draw:
            {
                // The area between the two contours: the inner contour is a hole of the outer one
                var outer = PathConversions.GetRoundedRectPath(draw.Outer);
                var inner = PathConversions.GetRoundedRectPath(draw.Inner);
                var ring = new DisplayPath();
                Append(ring, outer);
                Append(ring, inner);
                if (CreateBrush(draw.Paint) is { } brush)
                {
                    Paint(canvas, draw.Paint, (brush, ToPath(ring)), static (c, state) => c.Fill(state.brush, state.Item2, D.FillRule.EvenOdd));
                }

                break;
            }

            case DrawPathItem draw:
                DrawPath(canvas, draw.Path, draw.Paint);
                break;

            case DrawLineItem draw:
            {
                // A line is always stroked (a hairline for a paint that is not a stroke)
                var stroke = draw.Paint.Style == PaintStyle.Stroke ? draw.Paint.Stroke ?? new StrokeStyle(1) : new StrokeStyle(0);
                var path = new D.VectorPathBuilder().MoveTo(ToPoint(draw.Start)).LineTo(ToPoint(draw.End)).Build();
                Stroke(canvas, draw.Paint, path, stroke);
                break;
            }

            case DrawGlyphsItem draw:
                DrawGlyphs(canvas, draw);
                break;

            case DrawImageItem draw:
                DrawImage(canvas, draw);
                break;
        }
    }

    private static void Append(DisplayPath target, DisplayPath source)
    {
        var points = source.Points;
        var index = 0;
        foreach (var verb in source.Verbs)
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                    target.MoveTo(points[index].X, points[index].Y);
                    index++;
                    break;

                case PathVerb.LineTo:
                    target.LineTo(points[index].X, points[index].Y);
                    index++;
                    break;

                case PathVerb.QuadTo:
                    target.QuadTo(points[index].X, points[index].Y, points[index + 1].X, points[index + 1].Y);
                    index += 2;
                    break;

                case PathVerb.CubicTo:
                    target.CubicTo(points[index].X, points[index].Y, points[index + 1].X, points[index + 1].Y, points[index + 2].X, points[index + 2].Y);
                    index += 3;
                    break;

                case PathVerb.Close:
                    target.Close();
                    break;
            }
        }
    }

    private void DrawPath(D.DrawingCanvas canvas, DisplayPath path, DisplayPaint paint)
    {
        if (paint.Style == PaintStyle.Stroke)
        {
            Stroke(canvas, paint, ToPath(path));
            return;
        }

        if (CreateBrush(paint) is not { } brush)
            return;

        // A rectangle (e.g. an SVG rect) is filled as one
        if (PathConversions.TryGetRect(path, out var rect))
        {
            Paint(canvas, paint, (brush, ToRectangle(rect)), static (c, state) => c.FillRectangle(state.brush, state.Item2));
            return;
        }

        Paint(canvas, paint, (brush, ToPath(path), ToFillRule(path.FillRule)), static (c, state) => c.Fill(state.brush, state.Item2, state.Item3));
    }

    /// <summary>Strokes a path with the stroke of a paint.</summary>
    private void Stroke(D.DrawingCanvas canvas, DisplayPaint paint, D.VectorPath path, StrokeStyle? stroke = null)
    {
        if (CreateBrush(paint) is not { } brush)
            return;

        var pen = CreatePen(brush, stroke ?? paint.Stroke ?? new StrokeStyle(1));
        Paint(canvas, paint, (pen, path), static (c, state) => c.Stroke(state.pen, state.path));
    }

    /// <summary>
    /// Draws with the effects of a paint that are scopes of the canvas: the blur of the drawing (<see cref="DisplayPaint.BlurSigma"/>,
    /// in the coordinates of the drawing) and the antialiasing.
    /// </summary>
    private void Paint<TState>(D.DrawingCanvas canvas, DisplayPaint paint, TState state, D.DrawingAction<TState> draw)
        => Paint(canvas, paint.AntiAlias, double.IsFinite(paint.BlurSigma) && paint.BlurSigma > 0 ? paint.BlurSigma : 0, state, draw);

    private void Paint<TState>(D.DrawingCanvas canvas, bool antiAlias, double blur, TState state, D.DrawingAction<TState> draw)
    {
        if (blur > 0)
        {
            canvas.Filter(new D.BlurFilter(Math.Min(blur, MaxDeviation)), (this, antiAlias, state, draw), static (c, s) => s.Item1.Paint(c, s.antiAlias, 0, s.state, s.draw));
            return;
        }

        if (!antiAlias && _antiAlias)
        {
            canvas.Antialiasing(false, (state, draw), static (c, s) => s.draw(c, s.state));
            return;
        }

        draw(canvas, state);
    }

    /// <summary>The largest standard deviation of a blur of the drawing API.</summary>
    private const double MaxDeviation = 10_000;

    private static D.FillRule ToFillRule(FillRule fillRule) => fillRule == FillRule.EvenOdd ? D.FillRule.EvenOdd : D.FillRule.NonZero;

    /// <summary>
    /// Draws a layer (https://drafts.fxtf.org/compositing-1/#isolation): the items of its scope are drawn on a transparent layer,
    /// which is filtered, then composited with the opacity and the blend mode of the group. A layer with a backdrop filter
    /// starts with the filtered pixels under the border box of its element.
    /// </summary>
    private void DrawLayer(D.DrawingCanvas canvas, SaveLayerItem item)
    {
        var index = _index;
        DrawLayerCore(canvas, item);
        EnsureConsumed(index);
    }

    private void DrawLayerCore(D.DrawingCanvas canvas, SaveLayerItem item)
    {
        var opacity = double.IsNaN(item.Opacity) ? 1 : Math.Clamp(item.Opacity, 0, 1);
        var (composite, blend) = GetModes(item.BlendMode);
        var filter = item.Filters is { Length: > 0 } filters ? CreateFilter(filters) : null;
        if (item.BackdropFilters is { Length: > 0 } backdropFilters)
        {
            // The filters of the element apply to its filtered backdrop too (the bottom of its content): they are applied after
            // the backdrop filters (the content is filtered on its own, which is the same for the filters of the colors over
            // an opaque backdrop)
            var backdropFilter = CreateFilter(backdropFilters);
            if (filter is not null)
            {
                backdropFilter = backdropFilter is null ? filter : new D.FilterChain([backdropFilter, filter]);
            }

            var clip = item.BackdropClip;
            var rect = clip is { } rounded ? ToRectangle(rounded.Rect) : RectangleD.FromLTRB(-PathConversions.MaxCoordinate, -PathConversions.MaxCoordinate, PathConversions.MaxCoordinate, PathConversions.MaxCoordinate);
            canvas.BackdropLayer(backdropFilter, rect, opacity, (start: ScopeStart, clip, filter), static (c, state) =>
            {
                var rasterizer = state.start.Rewind();
                rasterizer.ClipBackdrop(c, state.clip);
                rasterizer.RunFiltered(c, state.filter);
            }, composite, blend, D.LayerOpacity.Group);
            return;
        }

        if (filter is not null && opacity >= 1 && composite == D.CompositeMode.SourceOver && blend == D.BlendMode.Normal)
        {
            // The filter draws its items on a layer of its own
            RunFiltered(canvas, filter);
            return;
        }

        // The opacity of a group applies to the layer before it is composited (https://drafts.fxtf.org/compositing-1/#groupcompositing)
        // A group is isolated even without effect: the operators of its items composite with the layer only (a mask). The
        // drawings of a group without filter stay in its bounds (with a margin for the antialiasing); the operators that change
        // the backdrop where the layer is transparent composite the whole clip.
        if (filter is null && item.Bounds is { } bounds && _matrix.Matrix3d is null && !ChangesBackdropWithTransparentSource(composite) && bounds.Width > 0 && bounds.Height > 0)
        {
            var margin = GetPixelLength();
            var rect = ToRectangle(new PhysicalRect(bounds.Left - margin, bounds.Top - margin, bounds.Width + (2 * margin), bounds.Height + (2 * margin)));
            canvas.Layer(rect, opacity, (start: ScopeStart, filter), static (c, state) => state.start.Rewind().RunFiltered(c, state.filter), composite, blend, D.LayerOpacity.Group);
            return;
        }

        canvas.Layer(opacity, (start: ScopeStart, filter), static (c, state) => state.start.Rewind().RunFiltered(c, state.filter), composite, blend, D.LayerOpacity.Group);
    }

    /// <summary>Draws the rest of the scope filtered by a filter (unfiltered when it is null).</summary>
    private void RunFiltered(D.DrawingCanvas canvas, D.Filter? filter)
    {
        if (filter is null)
        {
            RunScope(canvas);
            return;
        }

        var index = _index;
        canvas.Filter(filter, ScopeStart, static (c, start) => start.Rewind().RunScope(c));
        EnsureConsumed(index);
    }

    /// <summary>
    /// Clips the backdrop of a layer to the rounded border box of its element: what is outside of it is erased (the backdrop is
    /// the pixels of the box of the border box on the canvas: its corners, and its sides under a rotation).
    /// </summary>
    private void ClipBackdrop(D.DrawingCanvas canvas, RoundedRect? clip)
    {
        if (clip is not { } box)
            return;

        var axisAligned = _matrix.Matrix3d is null && ((_matrix.B == 0 && _matrix.C == 0) || (_matrix.A == 0 && _matrix.D == 0));
        if (box.IsRect && axisAligned)
            return;

        // The area between the box and a rectangle around the pixels of its backdrop
        var rect = box.Rect;
        var margin = Math.Max(rect.Width, rect.Height) + 1;
        var outside = new DisplayPath();
        outside.AddRect(new PhysicalRect(rect.X - margin, rect.Y - margin, rect.Width + (2 * margin), rect.Height + (2 * margin)));
        Append(outside, PathConversions.GetRoundedRectPath(box));
        var eraser = new D.SolidBrush(new Meziantou.Framework.Imaging.Rgba64(0, 0, 0, ushort.MaxValue)) { CompositeMode = D.CompositeMode.DestinationOut };
        canvas.Fill(eraser, ToPath(outside), D.FillRule.EvenOdd);
    }

    /// <summary>Whether a transparent source changes the backdrop with an operator (the operators that keep the backdrop only where the source is).</summary>
    private static bool ChangesBackdropWithTransparentSource(D.CompositeMode mode)
        => mode is D.CompositeMode.Source or D.CompositeMode.Clear or D.CompositeMode.SourceIn or D.CompositeMode.SourceOut or D.CompositeMode.DestinationIn or D.CompositeMode.DestinationAtop;

    /// <summary>The length in the coordinates of the items of a pixel of the bitmap (at least the length of a pixel).</summary>
    private double GetPixelLength()
    {
        var scale = Math.Min(Math.Sqrt((_matrix.A * _matrix.A) + (_matrix.B * _matrix.B)), Math.Sqrt((_matrix.C * _matrix.C) + (_matrix.D * _matrix.D)));
        return double.IsFinite(scale) && scale > 1e-6 ? Math.Max(1, 1 / scale) : 1;
    }

    /// <summary>The scale factors of the transform from the coordinates of the items to the pixels of the bitmap.</summary>
    private (double X, double Y) GetScaleFactors()
        => (Math.Sqrt((_matrix.A * _matrix.A) + (_matrix.B * _matrix.B)), Math.Sqrt((_matrix.C * _matrix.C) + (_matrix.D * _matrix.D)));

    private void DrawImage(D.DrawingCanvas canvas, DrawImageItem draw)
    {
        if (draw.SourceRect.Width <= 0 || draw.SourceRect.Height <= 0 || draw.DestinationRect.Width <= 0 || draw.DestinationRect.Height <= 0)
            return;

        var image = _backend.GetImage(draw.Image);
        if (image is null)
            return;

        var opacity = 1d;
        var (composite, blend) = (D.CompositeMode.SourceOver, D.BlendMode.Normal);
        if (draw.Paint is { } displayPaint)
        {
            opacity = float.IsNaN(displayPaint.Color.A) ? 0 : Math.Clamp(displayPaint.Color.A, 0, 1);
            (composite, blend) = GetModes(displayPaint.BlendMode);
        }

        // The source rectangle is in the natural pixels of the image, which are scaled to its decoded pixels
        var (pixelScaleX, pixelScaleY) = GetPixelScale(draw.Image, image);
        var source = draw.SourceRect;
        var destination = draw.DestinationRect;
        var left = source.Left * pixelScaleX;
        var top = source.Top * pixelScaleY;
        var right = source.Right * pixelScaleX;
        var bottom = source.Bottom * pixelScaleY;
        var scaleX = destination.Width / (right - left);
        var scaleY = destination.Height / (bottom - top);

        // https://html.spec.whatwg.org/multipage/canvas.html#drawing-images: the part of the source rectangle outside of the image
        // is not drawn (the destination rectangle is reduced in proportion)
        var clippedLeft = Math.Max(left, 0);
        var clippedTop = Math.Max(top, 0);
        var clippedRight = Math.Min(right, image.Width);
        var clippedBottom = Math.Min(bottom, image.Height);
        if (!(clippedRight > clippedLeft) || !(clippedBottom > clippedTop))
            return;

        var destinationLeft = destination.Left + ((clippedLeft - left) * scaleX);
        var destinationTop = destination.Top + ((clippedTop - top) * scaleY);
        var destinationRight = destination.Right - ((right - clippedRight) * scaleX);
        var destinationBottom = destination.Bottom - ((bottom - clippedBottom) * scaleY);
        // The part of the destination in the range of the drawing API (the source rectangle is reduced in proportion)
        var destinationRect = ToRectangle(new PhysicalRect(destinationLeft, destinationTop, destinationRight - destinationLeft, destinationBottom - destinationTop));
        if (!(destinationRect.Width > 0) || !(destinationRect.Height > 0))
            return;

        clippedLeft += (destinationRect.X - destinationLeft) / scaleX;
        clippedTop += (destinationRect.Y - destinationTop) / scaleY;
        clippedRight -= (destinationRight - (destinationRect.X + destinationRect.Width)) / scaleX;
        clippedBottom -= (destinationBottom - (destinationRect.Y + destinationRect.Height)) / scaleY;
        (destinationLeft, destinationTop) = (destinationRect.X, destinationRect.Y);

        var (deviceScaleX, deviceScaleY) = GetScaleFactors();
        var frame = image.GetFrame(_colorSpace);
        var sampling = GetSampling(draw.Sampling, scaleX * deviceScaleX, scaleY * deviceScaleY);
        var sourceRect = RectangleD.FromLTRB(clippedLeft, clippedTop, clippedRight, clippedBottom);
        if (composite == D.CompositeMode.SourceOver && blend == D.BlendMode.Normal)
        {
            Paint(canvas, _antiAliasEnabled, 0, (frame, sourceRect, destinationRect, sampling, opacity), static (c, state) => c.DrawImage(state.frame, state.sourceRect, state.destinationRect, state.sampling, state.opacity));
            return;
        }

        // The other operators paint the image with a brush: the source rectangle is rounded to whole pixels
        var region = new Meziantou.Framework.Imaging.Rectangle((int)Math.Floor(clippedLeft), (int)Math.Floor(clippedTop), (int)Math.Ceiling(clippedRight) - (int)Math.Floor(clippedLeft), (int)Math.Ceiling(clippedBottom) - (int)Math.Floor(clippedTop));
        var brush = new D.ImageBrush(frame)
        {
            Transform = new AffineTransform(scaleX, 0, 0, scaleY, destinationLeft - (clippedLeft * scaleX), destinationTop - (clippedTop * scaleY)),
            SourceRegion = region,
            WrapMode = D.ImageWrapMode.Pad,
            Sampling = sampling,
            Opacity = opacity,
            CompositeMode = composite,
            BlendMode = blend,
        };

        Paint(canvas, _antiAliasEnabled, 0, (brush, destinationRect), static (c, state) => c.FillRectangle(state.brush, state.destinationRect));
    }

    /// <summary>
    /// The interpolation of an image (https://drafts.csswg.org/css-images/#the-image-rendering): bilinear, and the average of
    /// the pixels a pixel of the canvas covers when the image is scaled down (as the box-filtered mipmaps of the browsers);
    /// bicubic for the high quality when the image is enlarged, and the nearest pixel for the pixelated images.
    /// </summary>
    private static D.ImageSampling GetSampling(ImageSampling sampling, double scaleX, double scaleY) => sampling switch
    {
        ImageSampling.Pixelated => D.ImageSampling.Nearest,
        ImageSampling.HighQuality when scaleX >= 1 && scaleY >= 1 => D.ImageSampling.Bicubic,
        _ => D.ImageSampling.Box,
    };

    /// <summary>
    /// The number of decoded pixels per natural pixel of an image: 1, unless the natural size of the image is not the size of
    /// its encoded pixels (the density-corrected dimensions of its EXIF resolution, see <c>ImageMetadata.GetDensityCorrectedSize</c>).
    /// </summary>
    private static (double X, double Y) GetPixelScale(PaintImage image, ImagingRenderingImage decoded)
    {
        if (image.Width <= 0 || image.Height <= 0 || (image.Width == decoded.Width && image.Height == decoded.Height))
            return (1, 1);

        return (decoded.Width / image.Width, decoded.Height / image.Height);
    }

    /// <summary>The operator and the blend mode of the drawing API of a blend mode of a display list.</summary>
    private static (D.CompositeMode Composite, D.BlendMode Blend) GetModes(BlendMode mode) => mode switch
    {
        BlendMode.Copy => (D.CompositeMode.Source, D.BlendMode.Normal),
        BlendMode.Clear => (D.CompositeMode.Clear, D.BlendMode.Normal),
        BlendMode.SourceIn => (D.CompositeMode.SourceIn, D.BlendMode.Normal),
        BlendMode.SourceOut => (D.CompositeMode.SourceOut, D.BlendMode.Normal),
        BlendMode.SourceAtop => (D.CompositeMode.SourceAtop, D.BlendMode.Normal),
        BlendMode.DestinationOver => (D.CompositeMode.DestinationOver, D.BlendMode.Normal),
        BlendMode.DestinationIn => (D.CompositeMode.DestinationIn, D.BlendMode.Normal),
        BlendMode.DestinationOut => (D.CompositeMode.DestinationOut, D.BlendMode.Normal),
        BlendMode.DestinationAtop => (D.CompositeMode.DestinationAtop, D.BlendMode.Normal),
        BlendMode.Xor => (D.CompositeMode.Xor, D.BlendMode.Normal),
        BlendMode.Lighter or BlendMode.PlusLighter => (D.CompositeMode.Plus, D.BlendMode.Normal),
        BlendMode.Multiply => (D.CompositeMode.SourceOver, D.BlendMode.Multiply),
        BlendMode.Screen => (D.CompositeMode.SourceOver, D.BlendMode.Screen),
        BlendMode.Overlay => (D.CompositeMode.SourceOver, D.BlendMode.Overlay),
        BlendMode.Darken => (D.CompositeMode.SourceOver, D.BlendMode.Darken),
        BlendMode.Lighten => (D.CompositeMode.SourceOver, D.BlendMode.Lighten),
        BlendMode.ColorDodge => (D.CompositeMode.SourceOver, D.BlendMode.ColorDodge),
        BlendMode.ColorBurn => (D.CompositeMode.SourceOver, D.BlendMode.ColorBurn),
        BlendMode.HardLight => (D.CompositeMode.SourceOver, D.BlendMode.HardLight),
        BlendMode.SoftLight => (D.CompositeMode.SourceOver, D.BlendMode.SoftLight),
        BlendMode.Difference => (D.CompositeMode.SourceOver, D.BlendMode.Difference),
        BlendMode.Exclusion => (D.CompositeMode.SourceOver, D.BlendMode.Exclusion),
        BlendMode.Hue => (D.CompositeMode.SourceOver, D.BlendMode.Hue),
        BlendMode.Saturation => (D.CompositeMode.SourceOver, D.BlendMode.Saturation),
        BlendMode.Color => (D.CompositeMode.SourceOver, D.BlendMode.Color),
        BlendMode.Luminosity => (D.CompositeMode.SourceOver, D.BlendMode.Luminosity),
        _ => (D.CompositeMode.SourceOver, D.BlendMode.Normal),
    };

}
