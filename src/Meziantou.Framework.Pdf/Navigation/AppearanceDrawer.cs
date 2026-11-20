using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>Draws the appearance streams of annotations in the content of their pages (ISO 32000-2, 12.5.5), to flatten them.</summary>
internal static class AppearanceDrawer
{
    /// <summary>Gets the normal appearance of an annotation: its stream, or the stream of its appearance state (12.5.5).</summary>
    public static PdfObject? GetNormalAppearance(PdfDictionary annotation)
    {
        var normal = annotation.Get<PdfDictionary>(AnnotationNames.AP)?[PdfNames.N];
        switch (normal is null ? null : PdfReference.ResolveIfReference(normal))
        {
            case PdfStream:
                return normal;
            case PdfDictionary states when annotation.GetName(AnnotationNames.AS) is { } state && states.GetResolved(state) is PdfStream:
                return states[state];
            default:
                return null;
        }
    }

    /// <summary>
    /// Paints an appearance stream as a form XObject in the rectangle of its annotation (12.5.5, the algorithm of the
    /// appearance streams), with the constant opacity of the annotation (<c>/CA</c>, <c>/ca</c>) and in the optional
    /// content of the annotation (<c>/OC</c>).
    /// </summary>
    public static void Draw(PdfDocument document, PdfContentBuilder builder, PdfDictionary annotation, PdfObject appearance)
    {
        var stream = (PdfStream)PdfReference.ResolveIfReference(appearance);
        if (!PdfRectangle.TryRead(annotation.Get<PdfArray>(PdfNames.Rect), out var rectangle) || !PdfRectangle.TryRead(stream.Dictionary.Get<PdfArray>(PdfNames.BBox), out var bbox))
            return;

        var matrix = stream.Dictionary.Get<PdfArray>(PdfNames.Matrix) is { Count: 6 } values
            ? new PdfMatrix(values.GetNumber(0), values.GetNumber(1), values.GetNumber(2), values.GetNumber(3), values.GetNumber(4), values.GetNumber(5))
            : PdfMatrix.Identity;
        var corners = new[] { matrix.Transform(bbox.Left, bbox.Bottom), matrix.Transform(bbox.Right, bbox.Bottom), matrix.Transform(bbox.Right, bbox.Top), matrix.Transform(bbox.Left, bbox.Top) };
        var left = corners.Min(point => point.X);
        var right = corners.Max(point => point.X);
        var bottom = corners.Min(point => point.Y);
        var top = corners.Max(point => point.Y);
        if (right - left <= 0 || top - bottom <= 0)
            return;

        // The form XObject applies its own matrix: the transformation maps its transformed bounding box to the rectangle
        var transform = PdfMatrix.CreateTranslation(-left, -bottom) * PdfMatrix.CreateScale(rectangle.Width / (right - left), rectangle.Height / (top - bottom)) * PdfMatrix.CreateTranslation(rectangle.Left, rectangle.Bottom);
        var reference = appearance as PdfReference ?? document.Objects.Add(stream);
        stream.Dictionary[PdfNames.Type] = PdfNames.XObject;
        stream.Dictionary[PdfNames.Subtype] = PdfNames.Form;
        var name = builder.AddResource(PdfNames.XObject, "Fm", reference);

        // The optional content of the annotation (8.11.3.3) applies to the drawn appearance: a marked-content sequence of
        // the same group or membership dictionary
        var optionalContent = annotation[AnnotationNames.OC];
        var hasOptionalContent = optionalContent is PdfReference && PdfReference.ResolveIfReference(optionalContent) is PdfDictionary;
        if (hasOptionalContent)
        {
            builder.BeginMarkedContentWithResource("OC", optionalContent!);
        }

        builder.SaveGraphicsState();

        // The constant opacity of the annotation (12.5.2) applies to the appearance as a whole, as viewers draw it: the
        // appearance is painted in a transparency group (11.6.6) painted with the opacity, so that its overlapping parts
        // do not darken. The opacity of the non-stroking operations of PDF 2.0 (ca), when it differs, is the alpha of
        // what they paint, as the renderer does.
        var strokeAlpha = ReadAlpha(annotation, AnnotationNames.CA) ?? 1;
        var fillAlpha = ReadAlpha(annotation, AnnotationNames.CALower) ?? strokeAlpha;
        if (strokeAlpha == fillAlpha && strokeAlpha < 1)
        {
            var group = document.CreateForm(rectangle, group: new PdfTransparencyGroup());
            var groupBuilder = group.CreateContentBuilder();
            var groupName = groupBuilder.AddResource(PdfNames.XObject, "Fm", reference);
            groupBuilder.Transform(transform).PaintXObject(groupName).Complete();
            builder.SetGraphicsState(new PdfExtGState { StrokeAlpha = strokeAlpha, FillAlpha = fillAlpha }).DrawForm(group).RestoreGraphicsState();
        }
        else
        {
            if (strokeAlpha < 1 || fillAlpha < 1)
            {
                builder.SetGraphicsState(new PdfExtGState { StrokeAlpha = strokeAlpha, FillAlpha = fillAlpha });
            }

            builder.Transform(transform).PaintXObject(name).RestoreGraphicsState();
        }
        if (hasOptionalContent)
        {
            builder.EndMarkedContent();
        }
    }

    private static double? ReadAlpha(PdfDictionary annotation, PdfName key) => annotation.GetResolved(key) is PdfNumber number && double.IsFinite(number.DoubleValue) ? Math.Clamp(number.DoubleValue, 0, 1) : null;
}
