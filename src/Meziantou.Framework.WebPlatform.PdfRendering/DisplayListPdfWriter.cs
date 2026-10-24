using Meziantou.Framework.Pdf;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.PdfRendering;

/// <summary>
/// Writes a <see cref="DisplayList"/> into a PDF content stream (a page, a form XObject, a tiling pattern). The state
/// operations of the display list map to the graphics state of PDF: a save is <c>q</c>, its restore <c>Q</c>, a transform
/// <c>cm</c>, a clip <c>W n</c>, a layer a transparency group (a form XObject drawn with the opacity and the blend mode of
/// the layer). The coordinates of the items are the coordinates of the content stream: the CSS px of a page are mapped to its
/// points by the first transform of the page.
/// </summary>
/// <remarks>
/// What PDF cannot draw is rasterized (<see cref="Rasterize"/>): the blurred shapes (the shadows), the layers with filters,
/// with compositing operators other than source-over (masks) or containing such drawings, the 3D transforms, the conic
/// gradients and the color glyphs.
/// </remarks>
internal sealed partial class DisplayListPdfWriter
{
    private readonly PdfDocumentWriter _owner;
    private readonly PdfContentBuilder _builder;
    private readonly bool _tagged;
    private readonly Stack<WriterState> _savedStates = new();
    private DisplayList _list = DisplayList.Empty;
    private int _index;

    /// <summary>The number of saves (and layers) that are not restored: a restore at the depth 0 is ignored.</summary>
    private int _depth;

    private WriterState _state;

    // The marked-content sequence of the content being written (tagged pages only)
    private bool _markedContentOpen;
    private PdfStructureElement? _markedElement;
    private PdfArtifactType _markedArtifact;

    /// <param name="owner">The document being written: its caches and its structure.</param>
    /// <param name="builder">The builder of the content stream.</param>
    /// <param name="state">The transforms and the clip at the start of the content stream.</param>
    /// <param name="tagged">Whether the content is marked with the structure elements and the artifacts of the document (the content of a page).</param>
    public DisplayListPdfWriter(PdfDocumentWriter owner, PdfContentBuilder builder, WriterState state, bool tagged)
    {
        _owner = owner;
        _builder = builder;
        _state = state;
        _tagged = tagged;
    }

    /// <summary>The first drawing item the writer wrote (the item whose structure element a transparency group belongs to).</summary>
    public DisplayItem? FirstDrawItem { get; private set; }

    /// <summary>Writes a whole display list.</summary>
    public void Write(DisplayList displayList)
    {
        _list = displayList;
        _index = 0;
        _depth = 0;
        RunScope();
        CloseMarkedContent();
    }

    /// <summary>Writes the items of a scope of a display list, from an index to the restore that ends the scope (consumed).</summary>
    /// <returns>The index after the restore.</returns>
    public int WriteScope(DisplayList displayList, int index)
    {
        _list = displayList;
        _index = index;
        _depth = 1;
        RunScope();
        CloseMarkedContent();
        return _index;
    }

    /// <summary>Writes a single item (a shape filled with a shader in a tile).</summary>
    public void WriteItem(DisplayItem item)
    {
        DrawItem(item);
        CloseMarkedContent();
    }

    private void RunScope()
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
                    CloseMarkedContent();
                    SaveState();
                    _depth++;
                    RunScope();
                    _depth--;
                    CloseMarkedContent();
                    RestoreState();
                    break;

                case SaveLayerItem layer:
                    CloseMarkedContent();
                    _depth++;
                    WriteLayer(layer, _index - 1);
                    _depth--;
                    break;

                case TransformItem transform:
                    ApplyTransform(transform);
                    break;

                case ClipRectItem clip:
                    ClipRect(clip.Rect);
                    break;

                case ClipRoundedRectItem clip:
                    if (clip.Rect.IsRect)
                    {
                        ClipRect(clip.Rect.Rect);
                    }
                    else
                    {
                        ClipPath(DisplayPath.FromFittedRoundedRect(clip.Rect), FillRule.NonZero);
                    }

                    break;

                case ClipPathItem clip:
                    if (clip.Path.TryGetRect(out var clipRect))
                    {
                        ClipRect(clipRect);
                    }
                    else
                    {
                        ClipPath(clip.Path, clip.Path.FillRule);
                    }

                    break;

                default:
                    FirstDrawItem ??= item;
                    DrawItem(item);
                    break;
            }
        }
    }

    private void SaveState()
    {
        _builder.SaveGraphicsState();
        _savedStates.Push(_state);
    }

    private void RestoreState()
    {
        _builder.RestoreGraphicsState();
        _state = _savedStates.Pop();
    }

    /// <summary>Skips the rest of the current scope, up to the restore that ends it (which is not consumed).</summary>
    private void SkipScope()
    {
        _index = FindScopeEnd(_index);
    }

    /// <summary>Returns the index of the restore that ends the scope that contains an index (the end of the list at the depth 0).</summary>
    private int FindScopeEnd(int index)
    {
        if (_depth == 0)
            return _list.Count;

        var items = _list.Items;
        var depth = 0;
        for (var i = index; i < items.Length; i++)
        {
            switch (items[i])
            {
                case SaveItem or SaveLayerItem:
                    depth++;
                    break;

                case RestoreItem:
                    if (depth == 0)
                        return i;

                    depth--;
                    break;
            }
        }

        return items.Length;
    }

    /// <summary>Returns the index after the restore that ends the scope started by the save or the layer at an index.</summary>
    private int FindRestore(int saveIndex)
    {
        var items = _list.Items;
        var depth = 0;
        for (var i = saveIndex; i < items.Length; i++)
        {
            switch (items[i])
            {
                case SaveItem or SaveLayerItem:
                    depth++;
                    break;

                case RestoreItem:
                    depth--;
                    if (depth == 0)
                        return i + 1;

                    break;
            }
        }

        return items.Length;
    }

    private void ApplyTransform(TransformItem item)
    {
        var matrix = item.Matrix;
        if (matrix.Matrix3d is { } matrix3d)
        {
            if (!matrix3d.AsSpan().ContainsAnyExcept(0) || matrix3d.Any(value => !double.IsFinite(value)))
            {
                SkipScope();
                return;
            }

            // PDF has no 3D transform: the rest of the scope is rasterized with it
            CloseMarkedContent();
            var end = FindScopeEnd(_index);
            var items = new List<DisplayItem>(end - _index + 1) { item };
            items.AddRange(_list.Items[_index..end]);
            _index = end;
            Rasterize(items, localBounds: null, GetFirstDrawItem(items));
            return;
        }

        if (matrix == DisplayMatrix.Identity)
            return;

        // The browsers draw nothing with a transform that is not invertible
        if (!PdfGeometry.IsInvertible(matrix))
        {
            SkipScope();
            return;
        }

        _builder.Transform(PdfGeometry.ToPdf(matrix));
        _state = _state with { ToStream = _state.ToStream.PreConcat(matrix), ToPage = _state.ToPage.PreConcat(matrix) };
    }

    private void ClipRect(PhysicalRect rect)
    {
        if (!(rect.Width > 0) || !(rect.Height > 0))
        {
            SkipScope();
            return;
        }

        CloseMarkedContent();
        _builder.Rectangle(rect.X, rect.Y, rect.Width, rect.Height).Clip().EndPath();
        IntersectClip(rect);
    }

    private void ClipPath(DisplayPath path, FillRule fillRule)
    {
        if (path.IsEmpty)
        {
            SkipScope();
            return;
        }

        CloseMarkedContent();
        WritePath(path);
        _builder.Clip(ToPdf(fillRule)).EndPath();
        IntersectClip(path.GetBounds());
    }

    private void IntersectClip(PhysicalRect localBounds)
    {
        var clip = PdfGeometry.Intersect(_state.Clip, PdfGeometry.Transform(_state.ToPage, localBounds));
        _state = _state with { Clip = clip };
        if (clip.IsEmpty)
        {
            SkipScope();
        }
    }

    /// <summary>Returns the clip in the coordinates of the items (the bounds of the clip of the page, transformed back).</summary>
    private PhysicalRect? GetLocalClip()
    {
        if (PdfGeometry.Invert(_state.ToPage) is not { } inverse)
            return null;

        return PdfGeometry.Transform(inverse, _state.Clip);
    }

    /// <summary>
    /// Writes a layer: a transparency group when it has an opacity or a blend mode, or when its drawings blend (the group
    /// is isolated); a save and a restore otherwise. A layer that PDF cannot composite is rasterized.
    /// </summary>
    private void WriteLayer(SaveLayerItem layer, int start)
    {
        var end = FindRestore(start);
        if (NeedsRasterization(layer, start, end))
        {
            _index = end;
            var items = _list.Items[start..end].ToArray();
            Rasterize(items, localBounds: null, GetFirstDrawItem(items));
            return;
        }

        var opacity = double.IsNaN(layer.Opacity) ? 1 : Math.Clamp(layer.Opacity, 0, 1);
        if (opacity == 0)
        {
            _index = end;
            return;
        }

        if (opacity >= 1 && layer.BlendMode == BlendMode.Normal && !HasBlendedDrawings(start, end))
        {
            SaveState();
            RunScope();
            CloseMarkedContent();
            RestoreState();
            return;
        }

        if (GetLocalClip() is not { } bounds)
        {
            _index = end;
            return;
        }

        if (layer.Bounds is { } layerBounds)
        {
            bounds = PdfGeometry.Intersect(bounds, PdfGeometry.Inflate(layerBounds, 1));
        }

        if (bounds.IsEmpty)
        {
            _index = end;
            return;
        }

        var form = _owner.Pdf.CreateForm(ToPdfRectangle(bounds), matrix: null, new PdfTransparencyGroup { ColorSpace = PdfDeviceColorSpace.Rgb, Isolated = true });
        var writer = new DisplayListPdfWriter(_owner, form.CreateContentBuilder(), _state with { ToStream = DisplayMatrix.Identity }, tagged: false);
        _index = writer.WriteScope(_list, start + 1);
        if (writer.FirstDrawItem is not { } first)
            return;

        FirstDrawItem ??= first;
        Mark(GetFirstDrawItem(_list.Items[start..end].ToArray()) ?? first);
        _builder.SaveGraphicsState();
        _builder.SetGraphicsState(_owner.GetGraphicsState(opacity, ToPdf(layer.BlendMode)));
        _builder.DrawForm(form);
        _builder.RestoreGraphicsState();
    }

    /// <summary>
    /// Whether a layer is rasterized: it has filters or backdrop filters, it is composited with a Porter-Duff operator, or
    /// its drawings are (e.g. a mask drawn with destination-in).
    /// </summary>
    private bool NeedsRasterization(SaveLayerItem layer, int start, int end)
    {
        if (layer.Filters is { Length: > 0 } || layer.BackdropFilters is { Length: > 0 } || IsPorterDuff(layer.BlendMode))
            return true;

        foreach (var mode in GetDirectBlendModes(start, end))
        {
            if (IsPorterDuff(mode))
                return true;
        }

        return false;
    }

    /// <summary>Whether drawings of a layer (not of its nested layers) have a blend mode: the layer is an isolated group.</summary>
    private bool HasBlendedDrawings(int start, int end)
    {
        foreach (var mode in GetDirectBlendModes(start, end))
        {
            if (mode != BlendMode.Normal)
                return true;
        }

        return false;
    }

    /// <summary>The blend modes of the drawings and of the nested layers of a layer, without the content of the nested layers.</summary>
    private List<BlendMode> GetDirectBlendModes(int start, int end)
    {
        var result = new List<BlendMode>();
        var items = _list.Items;
        var layerDepth = 0;
        for (var i = start + 1; i < end - 1; i++)
        {
            switch (items[i])
            {
                case SaveLayerItem nested:
                    if (layerDepth == 0)
                    {
                        result.Add(nested.BlendMode);
                    }

                    layerDepth++;
                    break;

                case SaveItem:
                    if (layerDepth > 0)
                    {
                        layerDepth++;
                    }

                    break;

                case RestoreItem:
                    if (layerDepth > 0)
                    {
                        layerDepth--;
                    }

                    break;

                default:
                    if (layerDepth == 0 && PdfGeometry.GetPaint(items[i]) is { } paint)
                    {
                        result.Add(paint.BlendMode);
                    }

                    break;
            }
        }

        return result;
    }

    /// <summary>Whether a blend mode is a Porter-Duff operator other than source-over, which PDF does not have.</summary>
    private static bool IsPorterDuff(BlendMode mode) => mode is not BlendMode.Normal && mode < BlendMode.Multiply;

    /// <summary>
    /// Returns the item whose structure element a group of items (a transparency group, a rasterized scope) is marked with:
    /// the first item that is content (a text, an image), else the first drawing item.
    /// </summary>
    private DisplayItem? GetFirstDrawItem(IEnumerable<DisplayItem> items)
    {
        DisplayItem? first = null;
        foreach (var item in items)
        {
            if (item is SaveItem or SaveLayerItem or RestoreItem or TransformItem or ClipRectItem or ClipRoundedRectItem or ClipPathItem)
                continue;

            if (!_tagged || _owner.Structure.GetContentElement(item, out _) is not null)
                return item;

            first ??= item;
        }

        return first;
    }

    /// <summary>
    /// Starts the marked-content sequence of the structure element or of the artifact an item belongs to (on a tagged page):
    /// the consecutive items of a structure element are one sequence.
    /// </summary>
    private void Mark(DisplayItem item)
    {
        if (!_tagged)
            return;

        var element = _owner.Structure.GetContentElement(item, out var artifact);
        Mark(element, artifact);
    }

    private void MarkArtifact(PdfArtifactType artifact)
    {
        if (_tagged)
        {
            Mark(element: null, artifact);
        }
    }

    private void Mark(PdfStructureElement? element, PdfArtifactType artifact)
    {
        if (_markedContentOpen && element == _markedElement && (element is not null || artifact == _markedArtifact))
            return;

        CloseMarkedContent();
        if (element is not null)
        {
            _builder.BeginStructureElement(element);
        }
        else
        {
            _builder.BeginArtifact(artifact);
        }

        _markedContentOpen = true;
        _markedElement = element;
        _markedArtifact = artifact;
    }

    private void CloseMarkedContent()
    {
        if (!_markedContentOpen)
            return;

        _builder.EndMarkedContent();
        _markedContentOpen = false;
        _markedElement = null;
    }

    private void DrawItem(DisplayItem item)
    {
        var paint = PdfGeometry.GetPaint(item);
        if (paint is { BlurSigma: > 0 and var sigma } && double.IsFinite(sigma))
        {
            // A blurred shape (a shadow) is an image
            MarkArtifact(PdfArtifactType.Background);
            Rasterize([item], PdfGeometry.GetBounds(item), firstItem: null);
            return;
        }

        if (paint is { Shader: { } shader } && !CanPaint(shader))
        {
            Mark(item);
            Rasterize([item], PdfGeometry.GetBounds(item), firstItem: null);
            return;
        }

        switch (item)
        {
            case DrawPaintItem draw:
                if (GetLocalClip() is { } clip)
                {
                    FillShape(draw.Paint, clip, b => b.Rectangle(clip.X, clip.Y, clip.Width, clip.Height), PdfFillRule.NonZero, item);
                }

                break;

            case DrawRectItem draw:
                if (draw.Paint.Style == PaintStyle.Fill)
                {
                    if (draw.Rect.Width > 0 && draw.Rect.Height > 0)
                    {
                        FillShape(draw.Paint, draw.Rect, b => b.Rectangle(draw.Rect.X, draw.Rect.Y, draw.Rect.Width, draw.Rect.Height), PdfFillRule.NonZero, item);
                    }
                }
                else if (draw.Rect.Width != 0 || draw.Rect.Height != 0)
                {
                    var rect = draw.Rect;
                    StrokeShape(draw.Paint, draw.Paint.Stroke ?? new StrokeStyle(1), PdfGeometry.GetBounds(item), b => b.MoveTo(rect.Left, rect.Top).LineTo(rect.Right, rect.Top).LineTo(rect.Right, rect.Bottom).LineTo(rect.Left, rect.Bottom).ClosePath(), item);
                }

                break;

            case DrawRoundedRectItem draw:
                if (draw.Rect.IsRect && draw.Paint.Style == PaintStyle.Fill)
                {
                    var rect = draw.Rect.Rect;
                    if (rect.Width > 0 && rect.Height > 0)
                    {
                        FillShape(draw.Paint, rect, b => b.Rectangle(rect.X, rect.Y, rect.Width, rect.Height), PdfFillRule.NonZero, item);
                    }
                }
                else
                {
                    DrawPath(DisplayPath.FromFittedRoundedRect(draw.Rect), draw.Paint, item);
                }

                break;

            case DrawRingItem draw:
            {
                // The area between the two contours: the inner contour is a hole of the outer one
                var outer = DisplayPath.FromFittedRoundedRect(draw.Outer);
                var inner = DisplayPath.FromFittedRoundedRect(draw.Inner);
                FillShape(draw.Paint, draw.Outer.Rect, _ =>
                {
                    WritePath(outer);
                    WritePath(inner);
                }, PdfFillRule.EvenOdd, item);
                break;
            }

            case DrawPathItem draw:
                DrawPath(draw.Path, draw.Paint, item);
                break;

            case DrawLineItem draw:
            {
                // A line is always stroked (a hairline for a paint that is not a stroke)
                var stroke = draw.Paint.Style == PaintStyle.Stroke ? draw.Paint.Stroke ?? new StrokeStyle(1) : new StrokeStyle(0);
                StrokeShape(draw.Paint, stroke, PdfGeometry.GetBounds(item), b => b.MoveTo(draw.Start.X, draw.Start.Y).LineTo(draw.End.X, draw.End.Y), item);
                break;
            }

            case DrawGlyphsItem draw:
                DrawGlyphs(draw);
                break;

            case DrawImageItem draw:
                DrawImage(draw);
                break;
        }
    }

    private void DrawPath(DisplayPath path, DisplayPaint paint, DisplayItem item)
    {
        if (path.IsEmpty)
            return;

        if (paint.Style == PaintStyle.Stroke)
        {
            StrokeShape(paint, paint.Stroke ?? new StrokeStyle(1), PdfGeometry.GetBounds(item), _ => WritePath(path), item);
            return;
        }

        FillShape(paint, path.GetBounds(), _ => WritePath(path), ToPdf(path.FillRule), item);
    }

    private void FillShape(DisplayPaint paint, PhysicalRect bounds, Action<PdfContentBuilder> writePath, PdfFillRule fillRule, DisplayItem item)
    {
        Mark(item);
        if (!BeginPaint(paint, fill: true, stroke: false, bounds, out var restore))
            return;

        writePath(_builder);
        _builder.Fill(fillRule);
        if (restore)
        {
            _builder.RestoreGraphicsState();
        }
    }

    private void StrokeShape(DisplayPaint paint, StrokeStyle stroke, PhysicalRect? bounds, Action<PdfContentBuilder> writePath, DisplayItem item)
    {
        Mark(item);
        if (!BeginPaint(paint, fill: false, stroke: true, bounds, out var restore))
            return;

        if (!restore)
        {
            _builder.SaveGraphicsState();
        }

        SetStroke(stroke);
        writePath(_builder);
        _builder.Stroke();
        _builder.RestoreGraphicsState();
    }

    private void SetStroke(StrokeStyle stroke)
    {
        var width = double.IsFinite(stroke.Width) && stroke.Width > 0 ? stroke.Width : 0;
        _builder.SetLineWidth(width)
            .SetLineCap(stroke.Cap switch
            {
                LineCap.Round => PdfLineCap.Round,
                LineCap.Square => PdfLineCap.ProjectingSquare,
                _ => PdfLineCap.Butt,
            })
            .SetLineJoin(stroke.Join switch
            {
                LineJoin.Round => PdfLineJoin.Round,
                LineJoin.Bevel => PdfLineJoin.Bevel,
                _ => PdfLineJoin.Miter,
            })
            .SetMiterLimit(double.IsFinite(stroke.MiterLimit) ? Math.Max(1, stroke.MiterLimit) : 4);

        if (stroke.Dashes is { Length: > 0 } dashes && dashes.All(value => double.IsFinite(value) && value >= 0) && dashes.Sum() > 0)
        {
            _builder.SetDashPattern(dashes, double.IsFinite(stroke.DashOffset) ? stroke.DashOffset : 0);
        }
    }

    /// <summary>Writes the subpaths of a path (the quadratic curves become cubic curves).</summary>
    private void WritePath(DisplayPath path)
    {
        var points = path.Points;
        var index = 0;
        var current = new PhysicalOffset(0, 0);
        var start = current;
        foreach (var verb in path.Verbs)
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                    current = start = points[index++];
                    _builder.MoveTo(current.X, current.Y);
                    break;

                case PathVerb.LineTo:
                    current = points[index++];
                    _builder.LineTo(current.X, current.Y);
                    break;

                case PathVerb.QuadTo:
                {
                    var control = points[index];
                    var end = points[index + 1];
                    index += 2;
                    _builder.CurveTo(
                        current.X + (2d / 3 * (control.X - current.X)),
                        current.Y + (2d / 3 * (control.Y - current.Y)),
                        end.X + (2d / 3 * (control.X - end.X)),
                        end.Y + (2d / 3 * (control.Y - end.Y)),
                        end.X,
                        end.Y);
                    current = end;
                    break;
                }

                case PathVerb.CubicTo:
                    _builder.CurveTo(points[index].X, points[index].Y, points[index + 1].X, points[index + 1].Y, points[index + 2].X, points[index + 2].Y);
                    current = points[index + 2];
                    index += 3;
                    break;

                case PathVerb.Close:
                    _builder.ClosePath();
                    current = start;
                    break;
            }
        }
    }

    private static PdfFillRule ToPdf(FillRule fillRule) => fillRule == FillRule.EvenOdd ? PdfFillRule.EvenOdd : PdfFillRule.NonZero;

    private static PdfRectangle ToPdfRectangle(PhysicalRect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    /// <summary>The blend mode of PDF of a blend mode (the Porter-Duff operators are rasterized).</summary>
    internal static PdfBlendMode ToPdf(BlendMode mode) => mode switch
    {
        BlendMode.Multiply => PdfBlendMode.Multiply,
        BlendMode.Screen => PdfBlendMode.Screen,
        BlendMode.Overlay => PdfBlendMode.Overlay,
        BlendMode.Darken => PdfBlendMode.Darken,
        BlendMode.Lighten => PdfBlendMode.Lighten,
        BlendMode.ColorDodge => PdfBlendMode.ColorDodge,
        BlendMode.ColorBurn => PdfBlendMode.ColorBurn,
        BlendMode.HardLight => PdfBlendMode.HardLight,
        BlendMode.SoftLight => PdfBlendMode.SoftLight,
        BlendMode.Difference => PdfBlendMode.Difference,
        BlendMode.Exclusion => PdfBlendMode.Exclusion,
        BlendMode.Hue => PdfBlendMode.Hue,
        BlendMode.Saturation => PdfBlendMode.Saturation,
        BlendMode.Color => PdfBlendMode.Color,
        BlendMode.Luminosity => PdfBlendMode.Luminosity,
        _ => PdfBlendMode.Normal,
    };
}

/// <summary>The state of a <see cref="DisplayListPdfWriter"/> that a save keeps.</summary>
/// <param name="ToStream">The transform from the coordinates of the items to the default space of the content stream (the space of the patterns).</param>
/// <param name="ToPage">The transform from the coordinates of the items to the CSS px of the page.</param>
/// <param name="Clip">The bounds of the clip, in the CSS px of the page.</param>
internal readonly record struct WriterState(DisplayMatrix ToStream, DisplayMatrix ToPage, PhysicalRect Clip);
