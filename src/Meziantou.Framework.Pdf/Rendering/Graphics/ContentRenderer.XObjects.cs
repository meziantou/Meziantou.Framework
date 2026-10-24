using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Drawing;
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

    private void PaintForm(DrawingCanvas canvas, PdfStream form)
    {
        if (_depth >= MaxDepth || !_activeForms.Add(form))
            return;

        try
        {
            var dictionary = form.Dictionary;
            PdfRectangle.TryRead(dictionary.Get<PdfArray>(PdfNames.BBox), out var bbox);
            var formMatrix = ReadMatrix(dictionary.Get<PdfArray>(PdfNames.Matrix));
            var isGroup = dictionary.Get<PdfDictionary>(PdfNames.Group)?.GetName(PdfNames.S)?.Value == "Transparency";
            var hasTransparency = isGroup && (_state.FillAlpha < 1 || _state.BlendMode != BlendMode.Normal || _state.SoftMask is not null);
            if (!hasTransparency)
            {
                // A form without transparency is its content, with the current graphics state
                var state = _state.Clone();
                state.Ctm = formMatrix * _state.Ctm;
                var renderer = new ContentRenderer(_cache, _document, state, _depth + 1, _optionalContent)
                {
                    ForcedColor = ForcedColor,
                    BaseMatrix = state.Ctm,
                    DeviceSize = DeviceSize,
                };
                renderer._activeForms.UnionWith(_activeForms);
                renderer.ClipToRectangle(bbox);
                renderer.Run(canvas, form.GetDecodedData(), dictionary.Get<PdfDictionary>(PdfNames.Resources));
                return;
            }

            // A transparency group: its content is painted on a layer, which the alpha, the blend mode and the soft
            // mask of the graphics state composite as one object (11.4)
            var alpha = _state.FillAlpha;
            var blend = _state.BlendMode;
            var ctm = formMatrix * _state.Ctm;
            if (!TryGetDeviceTransform(ctm, out var groupTransform))
                return;

            // The layer covers the bounding box of the group, which clips it
            var groupBounds = bbox.Right > bbox.Left && bbox.Top > bbox.Bottom
                ? new VectorPathBuilder().AddRectangle(new RectangleD(bbox.Left, bbox.Bottom, bbox.Right - bbox.Left, bbox.Top - bbox.Bottom)).Build().Transform(groupTransform).Bounds
                : new RectangleD(0, 0, DeviceSize.Width, DeviceSize.Height);
            if (ToPixelBounds(groupBounds, margin: 1) is not { } pixelBounds)
                return;

            PaintWithState(canvas, target => target.Layer(alpha, layer =>
            {
                var state = new RenderGraphicsState { Ctm = ctm };
                var renderer = new ContentRenderer(_cache, _document, state, _depth + 1, _optionalContent)
                {
                    ForcedColor = ForcedColor,
                    BaseMatrix = ctm,
                    DeviceSize = DeviceSize,
                };
                renderer._activeForms.UnionWith(_activeForms);
                renderer.ClipToRectangle(bbox);
                renderer.Run(layer, form.GetDecodedData(), dictionary.Get<PdfDictionary>(PdfNames.Resources));
            }, CompositeMode.SourceOver, blend), pixelBounds);
        }
        finally
        {
            _activeForms.Remove(form);
        }
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
            var alpha = _state.FillAlpha;
            var blend = _state.BlendMode;
            var origin = new PointD(Math.Floor(area.X), Math.Floor(area.Y));
            PaintWithState(canvas, target => DrawBlended(target, blend, c => c.DrawImage(image.Frames[0], origin, alpha)), ToPixelBounds(area, margin: 1));
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
        var image = _cache.GetOrCreate(stream, () => PdfImageDecoder.Decode(stream, resources, _cache));
        DrawDecodedImage(canvas, image);
    }

    private void DrawInlineImage(DrawingCanvas canvas, PdfDictionary dictionary, ReadOnlyMemory<byte> data, PdfDictionary? resources)
    {
        if (!_optionalContent.IsVisible)
            return;

        DrawDecodedImage(canvas, PdfImageDecoder.DecodeInline(dictionary, data, resources, _cache));
    }

    /// <summary>Draws an image in the unit square of user space (8.9.4); a stencil mask is painted with the fill color.</summary>
    private void DrawDecodedImage(DrawingCanvas canvas, DecodedImage? image)
    {
        if (image is null || !_optionalContent.IsVisible)
            return;

        // The image space: the first row at the top of the unit square
        var imageMatrix = new PdfMatrix(1.0 / image.Width, 0, 0, -1.0 / image.Height, 0, 1) * _state.Ctm;
        if (!TryGetDeviceTransform(imageMatrix, out var transform))
            return;

        if (GetImageBounds(transform, image.Width, image.Height) is not { } bounds)
            return;

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
                PaintWithState(canvas, target => target.Mask(mask => mask.DrawImage(frame, transform, sampling), content => content.Fill(brush), MaskMode.Alpha), bounds);
            }
            finally
            {
                ReleaseBrushImages(mark);
            }

            return;
        }

        var alpha = _state.FillAlpha;
        var blend = _state.BlendMode;
        var source = image.Image.Frames[0];
        PaintWithState(canvas, target => DrawBlended(target, blend, c => c.DrawImage(source, transform, sampling, alpha)), bounds);
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
