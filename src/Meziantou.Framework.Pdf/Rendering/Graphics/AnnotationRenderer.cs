using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>Draws the normal appearances of the annotations of a page (ISO 32000-2, 12.5.5).</summary>
internal static class AnnotationRenderer
{
    // The flags of an annotation (12.5.3)
    private const int Hidden = 2;
    private const int Print = 4;
    private const int NoZoom = 8;
    private const int NoRotate = 16;
    private const int NoView = 32;

    public static void Render(PdfPage page, DrawingCanvas canvas, RenderCache cache, PdfMatrix pageMatrix, (int Width, int Height) deviceSize, OptionalContent optionalContent)
    {
        if (page.Dictionary.Get<PdfArray>(PdfNames.Annots) is not { } annotations)
            return;

        for (var i = 0; i < annotations.Count; i++)
        {
            if (annotations.GetResolved(i) is not PdfDictionary annotation)
                continue;

            try
            {
                RenderAnnotation(page, annotation, canvas, cache, pageMatrix, deviceSize, optionalContent);
            }
            catch (Exception ex) when (RenderCache.IsContentError(ex))
            {
                // An annotation whose appearance cannot be read is not drawn
            }
        }
    }

    /// <summary>Indicates whether the flags of an annotation let it be drawn for an intent (12.5.3): printed annotations have the Print flag, and NoView applies to the screen only.</summary>
    internal static bool IsVisible(long flags, PdfRenderIntent intent) => intent switch
    {
        PdfRenderIntent.Print => (flags & (Hidden | Print)) == Print,
        _ => (flags & (Hidden | NoView)) == 0,
    };

    private static void RenderAnnotation(PdfPage page, PdfDictionary annotation, DrawingCanvas canvas, RenderCache cache, PdfMatrix pageMatrix, (int Width, int Height) deviceSize, OptionalContent optionalContent)
    {
        var flags = annotation.GetInteger(PdfName.Create("F"), 0);
        if (!IsVisible(flags, cache.Options.Intent) || !optionalContent.IsVisibleGroup(annotation.Get<PdfDictionary>(PdfName.Create("OC"))))
            return;

        if (!PdfRectangle.TryRead(annotation.Get<PdfArray>(PdfName.Create("Rect")), out var rectangle))
            return;

        // The normal appearance: a stream, or the stream of the appearance state; one generated for an annotation that
        // has none, or for a field of a form that asks for new appearances
        var appearance = AppearanceGenerator.NeedsAppearance(page, annotation) ? null : annotation.Get<PdfDictionary>(PdfName.Create("AP"))?.GetResolved(PdfNames.N) switch
        {
            PdfStream stream => stream,
            PdfDictionary states when annotation.GetName(PdfName.Create("AS")) is { } appearanceState => states.Get<PdfStream>(appearanceState),
            _ => null,
        };
        appearance ??= AppearanceGenerator.Generate(page, annotation, rectangle);
        if (appearance is null || !PdfRectangle.TryRead(appearance.Dictionary.Get<PdfArray>(PdfNames.BBox), out var bbox))
            return;

        // The bounding box transformed by the matrix of the appearance is mapped to the rectangle of the annotation (12.5.5, algorithm)
        var matrix = ContentRenderer.ReadMatrix(appearance.Dictionary.Get<PdfArray>(PdfNames.Matrix));
        var corners = new[] { matrix.Transform(bbox.Left, bbox.Bottom), matrix.Transform(bbox.Right, bbox.Bottom), matrix.Transform(bbox.Right, bbox.Top), matrix.Transform(bbox.Left, bbox.Top) };
        var left = corners.Min(point => point.X);
        var right = corners.Max(point => point.X);
        var bottom = corners.Min(point => point.Y);
        var top = corners.Max(point => point.Y);
        if (right - left <= 0 || top - bottom <= 0)
            return;

        var scaleX = (rectangle.Right - rectangle.Left) / (right - left);
        var scaleY = (rectangle.Top - rectangle.Bottom) / (top - bottom);
        var toRectangle = PdfMatrix.CreateTranslation(-left, -bottom) * PdfMatrix.CreateScale(scaleX, scaleY) * PdfMatrix.CreateTranslation(rectangle.Left, rectangle.Bottom);
        var placement = GetPlacement(page, flags, rectangle, pageMatrix);
        var ctm = toRectangle * placement;

        // The constant opacity of the annotation (12.5.2): the appearance is a group of that opacity; the opacity of the
        // non-stroking operations of PDF 2.0 (ca), when it differs, is the alpha of what they paint
        var strokeAlpha = ReadAlpha(annotation, "CA") ?? 1;
        var fillAlpha = ReadAlpha(annotation, "ca") ?? strokeAlpha;
        var groupAlpha = strokeAlpha == fillAlpha ? strokeAlpha : 1;
        var state = new RenderGraphicsState { Ctm = ctm };
        if (groupAlpha == 1)
        {
            state.StrokeAlpha = strokeAlpha;
            state.FillAlpha = fillAlpha;
        }

        var renderer = new ContentRenderer(cache, page.Document, state, depth: 0, optionalContent)
        {
            BaseMatrix = ctm,
            DeviceSize = deviceSize,
        };
        if (groupAlpha >= 1)
        {
            renderer.PaintAppearance(canvas, appearance);
            return;
        }

        // The group is as large as the pixels of the rectangle of the annotation
        if (!ContentRenderer.TryGetDeviceTransform(placement, out var transform) || ContentRenderer.CreateRectanglePath(rectangle) is not { } rectanglePath || ContentRenderer.TransformPath(rectanglePath, transform) is not { } devicePath)
            return;

        var bounds = devicePath.Bounds;
        var x0 = Math.Max(0, (int)Math.Floor(bounds.X) - 1);
        var y0 = Math.Max(0, (int)Math.Floor(bounds.Y) - 1);
        var x1 = Math.Min(deviceSize.Width, (int)Math.Ceiling(bounds.X + bounds.Width) + 1);
        var y1 = Math.Min(deviceSize.Height, (int)Math.Ceiling(bounds.Y + bounds.Height) + 1);
        if (x1 <= x0 || y1 <= y0)
            return;

        canvas.Clip(new Rectangle(x0, y0, x1 - x0, y1 - y0), clipped => clipped.Group(new GroupOptions { Opacity = groupAlpha, IsIsolated = true }, content => renderer.PaintAppearance(content, appearance)));
    }

    private static double? ReadAlpha(PdfDictionary annotation, string key) => annotation.GetResolved(PdfName.Create(key)) is PdfNumber number && double.IsFinite(number.DoubleValue) ? Math.Clamp(number.DoubleValue, 0, 1) : null;

    /// <summary>
    /// Gets the matrix from the default user space of the page to device space for an annotation: the matrix of the page,
    /// except that the NoZoom and NoRotate flags keep the upper-left corner of the rectangle in place and draw the
    /// annotation at the magnification of 72 dpi or without the rotation of the page (12.5.3).
    /// </summary>
    private static PdfMatrix GetPlacement(PdfPage page, long flags, PdfRectangle rectangle, PdfMatrix pageMatrix)
    {
        if ((flags & (NoZoom | NoRotate)) == 0)
            return pageMatrix;

        var scale = Math.Sqrt(Math.Abs((pageMatrix.A * pageMatrix.D) - (pageMatrix.B * pageMatrix.C)));
        if (!(scale > 0) || !double.IsFinite(scale))
            return pageMatrix;

        var userUnit = page.UserUnit is > 0 and var unit && double.IsFinite(unit) ? unit : 1;
        var target = (flags & NoZoom) != 0 ? userUnit : scale;
        var linear = (flags & NoRotate) != 0
            ? new PdfMatrix(target, 0, 0, -target, 0, 0)
            : new PdfMatrix(pageMatrix.A * target / scale, pageMatrix.B * target / scale, pageMatrix.C * target / scale, pageMatrix.D * target / scale, 0, 0);
        var (x, y) = pageMatrix.Transform(rectangle.Left, rectangle.Top);
        return PdfMatrix.CreateTranslation(-rectangle.Left, -rectangle.Top) * linear * PdfMatrix.CreateTranslation(x, y);
    }
}
