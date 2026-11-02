using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Pdf.Filters;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Images;
using Meziantou.Framework.Pdf.Rendering.Shadings;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

internal sealed partial class ContentRenderer
{
    private readonly HashSet<PdfStream> _activeForms = new(ReferenceEqualityComparer.Instance);

    /// <summary>Paints an external object (8.8): an image or a form, a transparency group (11.6.6) if it has one.</summary>
    private void PaintXObject(DrawingCanvas canvas, PdfObject? value, PdfDictionary? resources)
    {
        if (value is not PdfStream stream || !_optionalContent.IsVisible)
            return;

        var dictionary = stream.Dictionary;
        if (!_optionalContent.IsVisibleGroup(dictionary.Get<PdfDictionary>(PdfName.Create("OC"))))
            return;

        switch (dictionary.GetName(PdfNames.Subtype)?.Value)
        {
            case "Image":
                DrawImage(canvas, stream, resources);
                break;
            case "Form":
                PaintForm(canvas, stream);
                break;
        }
    }

    /// <summary>Paints the appearance of an annotation (12.5.5): a form, painted as a transparency group when it is one.</summary>
    public void PaintAppearance(DrawingCanvas canvas, PdfStream appearance) => PaintForm(canvas, appearance);

    private void PaintForm(DrawingCanvas canvas, PdfStream form)
    {
        if (_depth >= MaxDepth || !_activeForms.Add(form))
            return;

        try
        {
            var dictionary = form.Dictionary;
            PdfRectangle.TryRead(dictionary.Get<PdfArray>(PdfNames.BBox), out var bbox);
            var formMatrix = ReadMatrix(dictionary.Get<PdfArray>(PdfNames.Matrix));
            var group = dictionary.Get<PdfDictionary>(PdfNames.Group);
            if (group?.GetName(PdfNames.S)?.Value != "Transparency")
            {
                // A form that is not a group is its content, with the current graphics state: in a knockout group, each
                // of its objects is an element of the group
                var state = _state.Clone();
                state.Ctm = formMatrix * _state.Ctm;
                var renderer = CreateChild(state, isKnockout: IsKnockoutContext, element: _element);
                renderer.ClipToRectangle(bbox);
                renderer.Run(canvas, form.GetDecodedData(), dictionary.Get<PdfDictionary>(PdfNames.Resources));
                return;
            }

            // A transparency group is one element of a knockout group
            PaintElement(canvas, _state.FillAlpha, element => PaintGroup(element, form, group, bbox, formMatrix));
        }
        finally
        {
            _activeForms.Remove(form);
        }
    }

    /// <summary>
    /// Paints a transparency group (11.4.8, 11.6.6): its content composited as one object with the alpha, the blend mode
    /// and the soft mask of the graphics state, isolated or not (<c>/I</c>), knockout or not (<c>/K</c>).
    /// </summary>
    private void PaintGroup(DrawingCanvas canvas, PdfStream form, PdfDictionary group, PdfRectangle bbox, PdfMatrix formMatrix)
    {
        var dictionary = form.Dictionary;
        var resources = dictionary.Get<PdfDictionary>(PdfNames.Resources);
        var isIsolated = group.Get<PdfBoolean>(PdfNames.I)?.Value is true;
        var isKnockout = group.Get<PdfBoolean>(PdfNames.K)?.Value is true;

        // In an element of a knockout group, the element applies the alpha and the soft mask
        var alpha = ElementAlpha(_state.FillAlpha);
        var blend = _state.BlendMode;
        var softMask = (_element is null || _state.AlphaIsShape) && _depth < MaxDepth ? _state.SoftMask : null;
        var ctm = formMatrix * _state.Ctm;

        // The content starts with the graphics state of the Do operator, with the alpha, the blend mode and the soft
        // mask of a new group (11.6.6): the group applies them
        var state = _state.Clone();
        state.Ctm = ctm;
        state.FillAlpha = 1;
        state.StrokeAlpha = 1;
        state.BlendMode = BlendMode.Normal;
        state.SoftMask = null;

        // A group composites as its content does when nothing composites it as a whole: a non-isolated group, or an
        // isolated one whose content blends with nothing
        var isComposited = alpha < 1 || blend != BlendMode.Normal || softMask is not null || _state.Transfer is not null;
        if (!isKnockout && !isComposited && (!isIsolated || !_cache.UsesBlendModes(resources)))
        {
            var direct = CreateChild(state, isKnockout: false, element: null);
            direct.ClipToRectangle(bbox);
            direct.Run(canvas, form.GetDecodedData(), resources);
            return;
        }

        if (!TryGetDeviceTransform(ctm, out var groupTransform))
            return;

        // The group covers its bounding box, which clips it; a box beyond the coordinates of the canvas covers the page
        var bboxPath = bbox.Right > bbox.Left && bbox.Top > bbox.Bottom ? CreateRectanglePath(bbox) : null;
        var groupBounds = (bboxPath is null ? null : TransformPath(bboxPath, groupTransform))?.Bounds ?? new RectangleD(0, 0, DeviceSize.Width, DeviceSize.Height);
        if (ToPixelBounds(groupBounds, margin: 1) is not { } pixelBounds)
            return;

        // The planes of the group: the frames it is drawn on, and the group alone when it is not isolated
        _cache.Charge(((long)pixelBounds.Width * pixelBounds.Height * (isIsolated ? 1 : 2) / 1024) + 1);
        state.Clip = null;
        state.Transfer = null;
        var isLuminosity = softMask?.Mask.GetName(PdfNames.S)?.Value != "Alpha";
        var options = new GroupOptions
        {
            Opacity = alpha,
            BlendMode = blend,
            IsIsolated = isIsolated,
            IsKnockout = isKnockout,
            MaskMode = isLuminosity ? MaskMode.Luminance : MaskMode.Alpha,
        };
        void Content(DrawingCanvas target)
        {
            // The objects of a group painted with an alpha, a blend mode or a soft mask are not opaque (11.7.5.2)
            var renderer = CreateChild(state, isKnockout, element: null, suppressHalftones: alpha < 1 || blend != BlendMode.Normal || softMask is not null);
            renderer.ClipToRectangle(bbox);
            renderer.Run(target, form.GetDecodedData(), resources);
        }

        void Draw(DrawingCanvas target)
        {
            if (softMask is { } mask)
            {
                target.Group(options, content => Content(content), maskCanvas => RenderSoftMask(maskCanvas, mask.Mask, mask.Matrix, isLuminosity));
            }
            else
            {
                target.Group(options, content => Content(content));
            }
        }

        // The clip of the graphics state applies to the group as a whole; the transfer function, to its result
        canvas.Clip(pixelBounds, clipped => ApplyClip(clipped, _state.Clip, target =>
        {
            if (_state.Transfer is { } transfer)
            {
                var filter = new FilterGraph(transfer);
                target.Filter(filter, filtered => Draw(filtered));
            }
            else
            {
                Draw(target);
            }
        }));
    }

    /// <summary>Creates the renderer of the content of a form, a group or a glyph, which paints with this one.</summary>
    private ContentRenderer CreateChild(RenderGraphicsState state, bool isKnockout, double? element, bool suppressHalftones = false)
    {
        var renderer = new ContentRenderer(_cache, _document, state, _depth + 1, _optionalContent)
        {
            SuppressHalftones = SuppressHalftones || suppressHalftones,
            ForcedColor = ForcedColor,
            BaseMatrix = state.Ctm,
            DeviceSize = DeviceSize,
            IsKnockoutContext = isKnockout,
            Element = element,
        };
        renderer._activeForms.UnionWith(_activeForms);
        return renderer;
    }

    /// <summary>Paints a shading over the clipping region, with the current transformation (8.7.4.2).</summary>
    private void PaintShadingOperator(DrawingCanvas canvas, PdfObject? shading)
    {
        if (shading is null || !_optionalContent.IsVisible)
            return;

        var area = ClampToDevice(GetClipBounds());
        if (area.Width <= 0 || area.Height <= 0)
            return;

        var image = ShadingRasterizer.Rasterize(shading, _state.Ctm, area, _cache, includeBackground: false);
        if (image is null)
            return;

        using (image)
        {
            var origin = new PointD(Math.Floor(area.X), Math.Floor(area.Y));
            var bounds = ToPixelBounds(area, margin: 1);
            PaintElement(canvas, _state.FillAlpha, element =>
            {
                var alpha = ElementAlpha(_state.FillAlpha);
                var blend = _state.BlendMode;
                PaintWithState(element, target => DrawBlended(target, blend, c => c.DrawImage(image.Frames[0], origin, alpha)), bounds, GetHalftone(_state.FillAlpha));
            });
        }
    }

    /// <summary>Gets the bounds of the clipping region, in device space.</summary>
    private RectangleD GetClipBounds()
    {
        var bounds = new RectangleD(0, 0, DeviceSize.Width, DeviceSize.Height);
        for (var clip = _state.Clip; clip is not null; clip = clip.Previous)
        {
            bounds = RectangleD.Intersect(bounds, clip.Path.Bounds);
        }

        return bounds;
    }

    private static void DrawBlended(DrawingCanvas canvas, BlendMode blend, Action<DrawingCanvas> draw)
    {
        if (blend == BlendMode.Normal)
        {
            draw(canvas);
            return;
        }

        canvas.Layer(1, layer => draw(layer), CompositeMode.SourceOver, blend);
    }

    private void DrawImage(DrawingCanvas canvas, PdfStream stream, PdfDictionary? resources)
    {
        // The color space of the image may be a default color space of the resources (8.6.5.6)
        var image = _cache.GetOrCreate(stream, resources, () => PdfImageDecoder.Decode(stream, resources, _cache));

        // An image with a soft mask is not opaque, for the halftones (11.7.5.2)
        var hasSoftMask = stream.Dictionary.GetResolved(PdfNames.SMask) is PdfStream || stream.Dictionary.GetInteger(PdfName.Create("SMaskInData"), 0) != 0;
        DrawDecodedImage(canvas, image, hasSoftMask);
    }

    private void DrawInlineImage(DrawingCanvas canvas, PdfDictionary dictionary, ReadOnlyMemory<byte> data, PdfDictionary? resources)
    {
        if (!_optionalContent.IsVisible)
            return;

        DrawDecodedImage(canvas, PdfImageDecoder.DecodeInline(dictionary, data, resources, _document.Reader?.DecodeLimits ?? PdfDecodeLimits.Default, _cache));
    }

    /// <summary>Draws an image in the unit square of user space (8.9.4); a stencil mask is painted with the fill color.</summary>
    private void DrawDecodedImage(DrawingCanvas canvas, DecodedImage? image, bool hasSoftMask = false)
    {
        if (image is null || !_optionalContent.IsVisible)
            return;

        // The image space: the first row at the top of the unit square
        var imageMatrix = new PdfMatrix(1.0 / image.Width, 0, 0, -1.0 / image.Height, 0, 1) * _state.Ctm;
        if (!TryGetDeviceTransform(imageMatrix, out var transform))
            return;

        if (GetImageBounds(transform, image.Width, image.Height) is not { } bounds)
            return;

        if (IsKnockoutContext && _element is null)
        {
            // In a knockout group, the image is an element of the group, of the pixels it covers
            canvas.Clip(bounds, target => PaintElement(target, _state.FillAlpha, element => DrawDecodedImage(element, image, hasSoftMask)));
            return;
        }

        _cache.Charge(((long)image.Width * image.Height / 1024) + 1);

        var sampling = image.Interpolate || IsDownscaled(transform) ? ImageSampling.Bilinear : ImageSampling.Nearest;
        if (image.IsStencil)
        {
            // A stencil mask: the fill color (or pattern) painted through the alpha of the mask
            var mark = _brushImages.Count;
            try
            {
                var brush = CreateBrush(stroke: false, deviceBounds: null);
                if (brush is null)
                    return;

                var frame = image.Image.Frames[0];
                PaintWithState(canvas, target => target.Mask(mask => mask.DrawImage(frame, transform, sampling), content => content.Fill(brush), MaskMode.Alpha), bounds, GetHalftone(_state.FillAlpha));
            }
            finally
            {
                ReleaseBrushImages(mark);
            }

            return;
        }

        var alpha = ElementAlpha(_state.FillAlpha);
        var blend = _state.BlendMode;
        var source = image.Image.Frames[0];
        PaintWithState(canvas, target => DrawBlended(target, blend, c => c.DrawImage(source, transform, sampling, alpha)), bounds, GetHalftone(_state.FillAlpha, hasSoftMask));
    }

    /// <summary>Gets the pixels an image covers once transformed, with a pixel around them for the filtering; <see langword="null"/> when none is on the device.</summary>
    private Rectangle? GetImageBounds(AffineTransform transform, int width, int height)
    {
        var p0 = transform.Transform(new PointD(0, 0));
        var p1 = transform.Transform(new PointD(width, 0));
        var p2 = transform.Transform(new PointD(0, height));
        var p3 = transform.Transform(new PointD(width, height));
        var left = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        var top = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        var right = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        var bottom = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
        return ToPixelBounds(new RectangleD(left, top, right - left, bottom - top), margin: 1);
    }

    private static bool IsDownscaled(AffineTransform transform) => Math.Abs(transform.Determinant) < 1;
}
