using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>Draws the normal appearances of the annotations of a page (ISO 32000-2, 12.5.5).</summary>
internal static class AnnotationRenderer
{
    // The flags of an annotation (12.5.3)
    private const int Hidden = 2;
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

    private static void RenderAnnotation(PdfPage page, PdfDictionary annotation, DrawingCanvas canvas, RenderCache cache, PdfMatrix pageMatrix, (int Width, int Height) deviceSize, OptionalContent optionalContent)
    {
        var flags = annotation.GetInteger(PdfName.Create("F"), 0);
        if ((flags & (Hidden | NoView)) != 0 || !optionalContent.IsVisibleGroup(annotation.Get<PdfDictionary>(PdfName.Create("OC"))))
            return;

        if (!PdfRectangle.TryRead(annotation.Get<PdfArray>(PdfName.Create("Rect")), out var rectangle))
            return;

        // The normal appearance: a stream, or the stream of the appearance state
        var appearance = annotation.Get<PdfDictionary>(PdfName.Create("AP"))?.GetResolved(PdfNames.N) switch
        {
            PdfStream stream => stream,
            PdfDictionary states when annotation.GetName(PdfName.Create("AS")) is { } state => states.Get<PdfStream>(state),
            _ => null,
        };
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
        var ctm = matrix * toRectangle * pageMatrix;
        var renderer = new ContentRenderer(cache, page.Document, new RenderGraphicsState { Ctm = ctm }, depth: 1, optionalContent)
        {
            BaseMatrix = ctm,
            DeviceSize = deviceSize,
        };
        renderer.ClipToRectangle(bbox);
        renderer.Run(canvas, appearance.GetDecodedData(), appearance.Dictionary.Get<PdfDictionary>(PdfNames.Resources));
    }
}
