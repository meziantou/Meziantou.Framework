using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Colors;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

internal sealed partial class ContentRenderer
{
    // The clipping paths applied one inside the other: beyond, they are intersected into one path
    private const int MaxClipDepth = 16;

    // The side of the image of a cell of a tiling pattern
    private const int MaxTileSide = 2048;

    /// <summary>Gets or initializes the color every painting operator uses, instead of the colors of the content: the stencil of an uncolored pattern or of a <c>d1</c> glyph.</summary>
    public RgbColor? ForcedColor { get; set; }

    /// <summary>Gets or initializes the color of the glyph of a Type 3 font that turns out to be a stencil (<c>d1</c>).</summary>
    public RgbColor? StencilColor { get; init; }

    /// <summary>Gets or initializes the matrix from the default coordinate space of the content (the page, or the form that paints a pattern) to device space: the space of the patterns.</summary>
    public PdfMatrix BaseMatrix { get; init; }

    /// <summary>Gets or initializes the size of the device, in pixels.</summary>
    public (int Width, int Height) DeviceSize { get; init; }

    /// <summary>Paints with the fill or stroke color: the brush is made from the color or the pattern, then the drawing is clipped, masked and blended.</summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="stroke">Whether the stroke color is used.</param>
    /// <param name="draw">The callback that draws with the brush.</param>
    /// <param name="deviceBounds">The bounds of what is painted, in device space.</param>
    /// <param name="margin">The pixels around <paramref name="deviceBounds"/> the drawing can change: the half width of a line.</param>
    private void Paint(DrawingCanvas canvas, bool stroke, Action<DrawingCanvas, Brush> draw, RectangleD deviceBounds, double margin = 0)
    {
        if (ToPixelBounds(deviceBounds, margin + 1) is not { } bounds)
            return;

        // In a knockout group, the painting is an element of the group, of the pixels it can change
        if (IsKnockoutContext && _element is null)
        {
            canvas.Clip(bounds, target => PaintElement(target, stroke ? _state.StrokeAlpha : _state.FillAlpha, element => Paint(element, stroke, draw, deviceBounds, margin)));
            return;
        }

        var mark = _brushImages.Count;
        try
        {
            var brush = CreateBrush(stroke, deviceBounds);
            if (brush is null)
                return;

            PaintWithState(canvas, target => draw(target, brush), bounds);
        }
        finally
        {
            ReleaseBrushImages(mark);
        }
    }

    /// <summary>Disposes of the images of the brushes created since a mark: a pattern does not keep its image for the rest of the page.</summary>
    private void ReleaseBrushImages(int mark)
    {
        for (var i = _brushImages.Count - 1; i >= mark; i--)
        {
            _brushImages[i].Dispose();
            _brushImages.RemoveAt(i);
        }
    }

    /// <summary>Gets the pixels of the device a rectangle covers, with a margin around it; <see langword="null"/> when none is on the device.</summary>
    private Rectangle? ToPixelBounds(RectangleD bounds, double margin)
    {
        var left = Math.Max(0, Math.Floor(bounds.X - margin));
        var top = Math.Max(0, Math.Floor(bounds.Y - margin));
        var right = Math.Min(DeviceSize.Width, Math.Ceiling(bounds.X + bounds.Width + margin));
        var bottom = Math.Min(DeviceSize.Height, Math.Ceiling(bounds.Y + bounds.Height + margin));
        if (!(right > left && bottom > top))
            return null;

        return new Rectangle((int)left, (int)top, (int)(right - left), (int)(bottom - top));
    }

    /// <summary>Draws through the clipping path and the soft mask of the graphics state.</summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="draw">The callback that draws.</param>
    /// <param name="bounds">
    /// The pixels the drawing can change, when they are known: the layers of the clip and of the masks are limited to
    /// them, which matters for the many small images of a page of scanned glyphs.
    /// </param>
    private void PaintWithState(DrawingCanvas canvas, Action<DrawingCanvas> draw, Rectangle? bounds = null)
    {
        if (bounds is { } area)
        {
            canvas.Clip(area, target => PaintWithState(target, draw));
            return;
        }

        var softMask = _state.SoftMask;
        var masked = draw;
        if (_state.Transfer is { } transfer)
        {
            var filter = new FilterGraph(transfer);
            masked = target => target.Filter(filter, filtered => draw(filtered));
        }

        // The soft mask of an element of a knockout group is the opacity of the element
        var transferred = masked;
        if (softMask is { } mask && _depth < MaxDepth && _element is null)
        {
            var isLuminosity = mask.Mask.GetName(PdfNames.S)?.Value != "Alpha";
            var maskMode = isLuminosity ? MaskMode.Luminance : MaskMode.Alpha;
            if (_state.BlendMode == BlendMode.Normal)
            {
                masked = target => target.Mask(maskCanvas => RenderSoftMask(maskCanvas, mask.Mask, mask.Matrix, isLuminosity), content => transferred(content), maskMode);
            }
            else
            {
                // The drawing blends with what is under it, then the mask is applied: a non-isolated group of the drawing,
                // whose mask multiplies its alpha (11.6.4.3), mixes with the backdrop where a mask layer would not
                masked = target => target.Group(new GroupOptions { IsIsolated = false, MaskMode = maskMode }, content => transferred(content), maskCanvas => RenderSoftMask(maskCanvas, mask.Mask, mask.Matrix, isLuminosity));
            }
        }

        ApplyClip(canvas, _state.Clip, masked);
    }

    private static void ApplyClip(DrawingCanvas canvas, ClipPath? clip, Action<DrawingCanvas> draw)
    {
        if (clip is null)
        {
            draw(canvas);
            return;
        }

        ApplyClip(canvas, clip.Previous, outer => outer.Clip(clip.Path, inner => draw(inner), clip.FillRule));
    }

    /// <summary>Creates the brush of the fill or stroke color; <see langword="null"/> when nothing is painted.</summary>
    private Brush? CreateBrush(bool stroke, RectangleD? deviceBounds)
    {
        var alpha = ElementAlpha(stroke ? _state.StrokeAlpha : _state.FillAlpha);
        var blend = _state.BlendMode;
        if (ForcedColor is { } forced)
            return new SolidBrush(ToRgba64(forced)) { Opacity = alpha, BlendMode = blend };

        var space = stroke ? _state.StrokeColorSpace : _state.FillColorSpace;
        if (space.PaintsNothing)
            return null;

        if (space is RenderColorSpace.PatternSpace patternSpace)
        {
            var pattern = stroke ? _state.StrokePattern : _state.FillPattern;
            var components = stroke ? _state.StrokeColor : _state.FillColor;
            if (pattern is null)
                return null;

            return CreatePatternBrush(pattern, patternSpace, components, deviceBounds, alpha, blend);
        }

        var color = space.ToRgb(stroke ? _state.StrokeColor : _state.FillColor);
        return new SolidBrush(ToRgba64(color)) { Opacity = alpha, BlendMode = blend };
    }

    internal static Rgba64 ToRgba64(RgbColor color) => new(ToUInt16(color.R), ToUInt16(color.G), ToUInt16(color.B));

    private static ushort ToUInt16(double value) => (ushort)Math.Clamp((int)Math.Round(value * 65535, MidpointRounding.AwayFromZero), 0, 65535);

    /// <summary>Creates the brush of a pattern (8.7): an image of the shading over the area painted, or an image of the cell repeated over the plane.</summary>
    private ImageBrush? CreatePatternBrush(PdfObject pattern, RenderColorSpace.PatternSpace space, double[] components, RectangleD? deviceBounds, double alpha, BlendMode blend)
    {
        var dictionary = pattern is PdfStream stream ? stream.Dictionary : pattern as PdfDictionary;
        if (dictionary is null)
            return null;

        var matrix = ReadMatrix(dictionary.Get<PdfArray>(PdfNames.Matrix)) * BaseMatrix;
        switch (dictionary.GetInteger(PdfName.Create("PatternType"), 0))
        {
            case 2:
                {
                    // A shading pattern: the shading in pattern space, rendered over the area painted
                    var area = ClampToDevice(deviceBounds ?? new RectangleD(0, 0, DeviceSize.Width, DeviceSize.Height));
                    if (area.Width <= 0 || area.Height <= 0 || dictionary.GetResolved(PdfNames.Shading) is not { } shading)
                        return null;

                    var image = Shadings.ShadingRasterizer.Rasterize(shading, matrix, area, _cache, includeBackground: true);
                    if (image is null)
                        return null;

                    _brushImages.Add(image);
                    return new ImageBrush(image.Frames[0]) { Transform = AffineTransform.CreateTranslation(Math.Floor(area.X), Math.Floor(area.Y)), WrapMode = ImageWrapMode.Transparent, Opacity = alpha, BlendMode = blend };
                }

            case 1 when pattern is PdfStream cell:
                {
                    RgbColor? color = null;
                    if (dictionary.GetInteger(PdfName.Create("PaintType"), 1) == 2)
                    {
                        // An uncolored pattern: a stencil painted with the color of the underlying color space
                        if (space.Underlying is null)
                            return null;

                        color = space.Underlying.ToRgb(components);
                    }

                    return CreateTilingBrush(cell, matrix, color, alpha, blend);
                }

            default:
                return null;
        }
    }

    /// <summary>
    /// Renders the cell of a tiling pattern (8.7.3) to an image of one step by one step, at the resolution of the
    /// device, and repeats it: the brush maps the pixels of the image to device space through the pattern matrix.
    /// </summary>
    private ImageBrush? CreateTilingBrush(PdfStream cell, PdfMatrix patternMatrix, RgbColor? color, double alpha, BlendMode blend)
    {
        var dictionary = cell.Dictionary;
        var xStep = dictionary.GetNumber(PdfName.Create("XStep"), 0);
        var yStep = dictionary.GetNumber(PdfName.Create("YStep"), 0);
        if (!PdfRectangle.TryRead(dictionary.Get<PdfArray>(PdfNames.BBox), out var bbox) || !double.IsFinite(xStep) || !double.IsFinite(yStep) || xStep == 0 || yStep == 0 || _depth >= MaxDepth)
            return null;

        // The size of the image: the step vectors measured in device pixels
        var (ax, ay) = patternMatrix.TransformVector(xStep, 0);
        var (bx, by) = patternMatrix.TransformVector(0, yStep);
        var width = (int)Math.Clamp(Math.Ceiling(Math.Sqrt((ax * ax) + (ay * ay))), 1, MaxTileSide);
        var height = (int)Math.Clamp(Math.Ceiling(Math.Sqrt((bx * bx) + (by * by))), 1, MaxTileSide);
        var scaleX = width / Math.Abs(xStep);
        var scaleY = height / Math.Abs(yStep);

        // Pattern space to the pixels of the tile: the step that starts at the origin of the cell; negative steps are reversed
        var originX = xStep > 0 ? 0 : xStep;
        var originY = yStep > 0 ? 0 : yStep;
        var toTile = PdfMatrix.CreateTranslation(-originX, -originY) * PdfMatrix.CreateScale(scaleX, scaleY);
        _cache.Charge(((long)width * height / 256) + 1);
        var tile = new Image<Rgba32>(width, height, _cache.Options.Configuration);
        _brushImages.Add(tile);
        tile.Draw(canvas =>
        {
            // The cell is painted at each step that overlaps the tile, so that a cell larger than the step wraps around
            for (var i = -1; i <= 1; i++)
            {
                for (var j = -1; j <= 1; j++)
                {
                    var offset = PdfMatrix.CreateTranslation(i * Math.Abs(xStep), j * Math.Abs(yStep)) * toTile;
                    RenderForm(canvas, cell, offset, bbox, color, offset, (width, height), applyFormMatrix: false);
                }
            }
        });

        // The pixels of the tile to device space
        if (!TryGetDeviceTransform(PdfMatrix.CreateScale(1 / scaleX, 1 / scaleY) * PdfMatrix.CreateTranslation(originX, originY) * patternMatrix, out var transform))
            return null;

        return new ImageBrush(tile.Frames[0]) { Transform = transform, WrapMode = ImageWrapMode.Repeat, Opacity = alpha, BlendMode = blend };
    }

    /// <summary>
    /// Renders the group of a soft mask on the canvas of the mask: over its backdrop color for a luminosity mask, then
    /// through its transfer function (11.6.5.2).
    /// </summary>
    private void RenderSoftMask(DrawingCanvas canvas, PdfDictionary mask, PdfMatrix matrix, bool isLuminosity)
    {
        if (mask.GetResolved(PdfNames.G) is not PdfStream group)
            return;

        var transfer = mask.GetResolved(PdfName.Create("TR")) is { } transferObject and not PdfName ? TryCreateTransfer(transferObject) : null;
        if (transfer is not null)
        {
            // The luminosity of a gray group is its gray: the table is applied to the color components, or to the alpha
            var primitive = isLuminosity
                ? new ComponentTransferPrimitive { Red = transfer, Green = transfer, Blue = transfer }
                : new ComponentTransferPrimitive { Alpha = transfer };
            canvas.Filter(new FilterGraph(primitive), filtered => RenderSoftMaskGroup(filtered, mask, group, matrix, isLuminosity));
            return;
        }

        RenderSoftMaskGroup(canvas, mask, group, matrix, isLuminosity);
    }

    /// <summary>
    /// Reads the transfer functions of a graphics state (10.5): one function for every component, or one per component of
    /// the device (red, green, blue, then gray); <see langword="null"/> for the identity.
    /// </summary>
    private ComponentTransferPrimitive? ReadTransfer(PdfObject value)
    {
        switch (value)
        {
            case PdfArray { Count: >= 3 } array:
                {
                    var red = ReadComponentTransfer(array.GetResolved(0));
                    var green = ReadComponentTransfer(array.GetResolved(1));
                    var blue = ReadComponentTransfer(array.GetResolved(2));
                    return red is null && green is null && blue is null ? null : new ComponentTransferPrimitive { Red = red ?? TransferFunction.Identity, Green = green ?? TransferFunction.Identity, Blue = blue ?? TransferFunction.Identity };
                }

            case PdfArray:
                return null;
            default:
                {
                    var transfer = ReadComponentTransfer(value);
                    return transfer is null ? null : new ComponentTransferPrimitive { Red = transfer, Green = transfer, Blue = transfer };
                }
        }
    }

    private TransferFunction? ReadComponentTransfer(PdfObject? value) => value is null or PdfName ? null : TryCreateTransfer(value);

    /// <summary>Samples a transfer function of one input and one output into a table; <see langword="null"/> when it cannot be read.</summary>
    private TransferFunction? TryCreateTransfer(PdfObject value)
    {
        try
        {
            var function = _cache.GetFunction(value);
            var table = new double[256];
            Span<double> output = stackalloc double[Math.Max(1, function.OutputCount)];
            for (var i = 0; i < table.Length; i++)
            {
                Span<double> input = [i / 255.0];
                function.Evaluate(input, output);
                table[i] = Math.Clamp(double.IsFinite(output[0]) ? output[0] : 0, 0, 1);
            }

            return TransferFunction.CreateTable(table);
        }
        catch (Exception ex) when (RenderCache.IsContentError(ex))
        {
            return null;
        }
    }

    private void RenderSoftMaskGroup(DrawingCanvas canvas, PdfDictionary mask, PdfStream group, PdfMatrix matrix, bool isLuminosity)
    {
        if (isLuminosity)
        {
            // The backdrop, in the color space of the group; black by default
            var backdrop = new RgbColor(0, 0, 0);
            if (mask.Get<PdfArray>(PdfName.Create("BC")) is { } components && group.Dictionary.Get<PdfDictionary>(PdfNames.Group)?.GetResolved(PdfNames.CS) is { } colorSpace)
            {
                try
                {
                    var space = RenderColorSpace.Read(colorSpace, group.Dictionary.Get<PdfDictionary>(PdfNames.Resources), _cache);
                    backdrop = space.ToRgb([.. components.Select(item => PdfReference.ResolveIfReference(item) is PdfNumber n ? n.DoubleValue : 0)]);
                }
                catch (Exception ex) when (RenderCache.IsContentError(ex))
                {
                }
            }

            canvas.Clear(ToRgba64(backdrop));
        }

        var formMatrix = ReadMatrix(group.Dictionary.Get<PdfArray>(PdfNames.Matrix));
        PdfRectangle.TryRead(group.Dictionary.Get<PdfArray>(PdfNames.BBox), out var bbox);

        // The group of the mask composites with the backdrop of the mask: an isolated group starts transparent, and a
        // knockout group knocks out its elements (11.4.8)
        var attributes = group.Dictionary.Get<PdfDictionary>(PdfNames.Group);
        var isIsolated = attributes?.Get<PdfBoolean>(PdfNames.I)?.Value is true;
        var isKnockout = attributes?.Get<PdfBoolean>(PdfNames.K)?.Value is true;
        if (isKnockout || (isIsolated && _cache.UsesBlendModes(group.Dictionary.Get<PdfDictionary>(PdfNames.Resources))))
        {
            canvas.Group(new GroupOptions { IsIsolated = isIsolated, IsKnockout = isKnockout }, content => RenderForm(content, group, matrix, bbox, forcedColor: null, formMatrix * matrix, DeviceSize, applyFormMatrix: true, isKnockout));
            return;
        }

        RenderForm(canvas, group, matrix, bbox, forcedColor: null, formMatrix * matrix, DeviceSize, applyFormMatrix: true);
    }

    /// <summary>
    /// Renders a form or a pattern cell with a new graphics state: the matrix (with the <c>/Matrix</c> of a form, which
    /// is the pattern matrix of a cell), the bounding box as clipping path.
    /// </summary>
    private void RenderForm(DrawingCanvas canvas, PdfStream form, PdfMatrix matrix, PdfRectangle bbox, RgbColor? forcedColor, PdfMatrix baseMatrix, (int Width, int Height) deviceSize, bool applyFormMatrix, bool isKnockout = false)
    {
        var formMatrix = applyFormMatrix ? ReadMatrix(form.Dictionary.Get<PdfArray>(PdfNames.Matrix)) : PdfMatrix.Identity;
        var state = new RenderGraphicsState { Ctm = formMatrix * matrix };
        var renderer = new ContentRenderer(_cache, _document, state, _depth + 1, _optionalContent)
        {
            ForcedColor = forcedColor,
            BaseMatrix = baseMatrix,
            DeviceSize = deviceSize,
            IsKnockoutContext = isKnockout,
        };
        renderer.ClipToRectangle(bbox);
        renderer.Run(canvas, form.GetDecodedData(), form.Dictionary.Get<PdfDictionary>(PdfNames.Resources));
    }

    /// <summary>Clips to a rectangle of user space: the bounding box of a form, a pattern cell or a glyph.</summary>
    internal void ClipToRectangle(PdfRectangle rectangle)
    {
        if (rectangle.Right <= rectangle.Left || rectangle.Top <= rectangle.Bottom || !TryGetDeviceTransform(_state.Ctm, out var transform))
            return;

        // A rectangle beyond the coordinates of the canvas does not clip
        if (CreateRectanglePath(rectangle) is { } path && TransformPath(path, transform) is { } devicePath)
        {
            AddClip(devicePath, FillRule.NonZero);
        }
    }

    private RectangleD ClampToDevice(RectangleD bounds)
    {
        var left = Math.Max(0, Math.Floor(bounds.X));
        var top = Math.Max(0, Math.Floor(bounds.Y));
        var right = Math.Min(DeviceSize.Width, Math.Ceiling(bounds.X + bounds.Width));
        var bottom = Math.Min(DeviceSize.Height, Math.Ceiling(bounds.Y + bounds.Height));
        return right > left && bottom > top ? new RectangleD(left, top, right - left, bottom - top) : default;
    }
}
