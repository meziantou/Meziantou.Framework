using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Graphics;

namespace Meziantou.Framework.Pdf;

public sealed partial class PdfPage
{
    /// <summary>
    /// Flattens annotations of the page (ISO 32000-2, 12.5.5): the normal appearance of each one is drawn in the content of
    /// the page, then the annotation, its pop-up and the references to it (replies, structure tree, form field) are
    /// removed, so that it becomes content that cannot be changed.
    /// </summary>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfAnnotationFlattenOptions.Default"/>.</param>
    /// <returns>The number of annotations flattened, the pop-ups left out.</returns>
    /// <remarks>
    /// An annotation without an appearance gets the one <see cref="PdfAnnotation.UpdateAppearance"/> draws; when the
    /// library does not draw its type, it is removed without being drawn. The appearance is drawn with the opacity
    /// (<c>/CA</c>) and in the optional content (<c>/OC</c>) of the annotation, without the effect of the
    /// <see cref="PdfAnnotationFlags.NoZoom"/> and <see cref="PdfAnnotationFlags.NoRotate"/> flags. To flatten the form
    /// fields with their values, use <see cref="PdfInteractiveForm.Flatten"/>.
    /// </remarks>
    public int FlattenAnnotations(PdfAnnotationFlattenOptions? options = null)
    {
        options ??= PdfAnnotationFlattenOptions.Default;
        var selected = new List<PdfAnnotation>();
        foreach (var annotation in Annotations)
        {
            if (annotation is PdfPopupAnnotation || IsSignedSignatureWidget(annotation.Dictionary))
                continue;

            if (options.Filter is { } filter ? filter(annotation) : annotation is PdfMarkupAnnotation and not PdfRedactAnnotation)
            {
                selected.Add(annotation);
            }
        }

        if (selected.Count == 0)
            return 0;

        PdfContentBuilder? builder = null;
        foreach (var annotation in selected)
        {
            if (!AnnotationRenderer.IsVisible(annotation.Dictionary.GetInteger(PdfNames.F, 0), options.Intent))
                continue;

            var appearance = AppearanceDrawer.GetNormalAppearance(annotation.Dictionary);
            if (appearance is null && AppearanceGenerator.Generate(this, annotation.Dictionary, annotation.Rectangle, includeIcons: true) is { } generated)
            {
                appearance = Document.Objects.Add(generated);
            }

            if (appearance is null)
                continue;

            builder ??= CreateContentBuilder();
            AppearanceDrawer.Draw(Document, builder, annotation.Dictionary, appearance);
        }

        builder?.Complete();
        AnnotationRemover.Remove(this, selected.Select(annotation => ((PdfObject?)annotation.Reference ?? annotation.Dictionary, annotation.Dictionary)));
        return selected.Count;
    }

    /// <summary>Indicates whether an annotation is the widget of a signature field that is signed: flattening it would remove the signature (12.7.5.5).</summary>
    private static bool IsSignedSignatureWidget(PdfDictionary annotation)
    {
        if (annotation.GetName(PdfNames.Subtype) != PdfFormField.WidgetName)
            return false;

        // The type and the value are inherited separately (12.7.4.1): a terminal field can have its value and inherit its type
        PdfName? type = null;
        PdfObject? value = null;
        var current = annotation;
        for (var depth = 0; current is not null && depth < 32 && (type is null || value is null); depth++)
        {
            type ??= current.GetName(PdfFormField.FTName);
            value ??= current.GetResolved(PdfFormField.VName);
            current = current.Get<PdfDictionary>(PdfNames.Parent);
        }

        return type?.Value == "Sig" && value is PdfDictionary;
    }
}
