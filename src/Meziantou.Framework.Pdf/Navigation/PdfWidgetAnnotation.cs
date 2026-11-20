using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A widget annotation (ISO 32000-2, 12.5.6.19): the appearance of a form field on a page. Its field is in
/// <see cref="PdfDocument.Form"/>; <see cref="PdfFormField.Widgets"/> lists the widgets of a field.
/// </summary>
public sealed class PdfWidgetAnnotation : PdfAnnotation
{
    private PdfAppearanceCharacteristics? _characteristics;

    internal PdfWidgetAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
        : base(page, dictionary, reference)
    {
    }

    /// <summary>Gets or sets the appearance state of the widget (<c>/AS</c>, 12.5.5): the name of its appearance in the dictionaries of <c>/AP</c>, such as <c>Off</c> or the export value of a check box.</summary>
    public string? AppearanceState
    {
        get => Dictionary.GetName(AnnotationNames.AS)?.Value;
        set => Dictionary[AnnotationNames.AS] = value is null ? null : PdfName.Create(value);
    }

    /// <summary>Gets or sets how the widget looks while the mouse button is pressed on it (<c>/H</c>). The default is <see cref="PdfWidgetHighlightMode.Invert"/>.</summary>
    public PdfWidgetHighlightMode HighlightMode
    {
        get => Dictionary.GetName(AnnotationNames.H)?.Value switch
        {
            "N" => PdfWidgetHighlightMode.None,
            "O" => PdfWidgetHighlightMode.Outline,
            "P" => PdfWidgetHighlightMode.Push,
            "T" => PdfWidgetHighlightMode.Toggle,
            _ => PdfWidgetHighlightMode.Invert,
        };
        set => Dictionary[AnnotationNames.H] = value switch
        {
            PdfWidgetHighlightMode.None => PdfName.Create("N"),
            PdfWidgetHighlightMode.Invert => null,
            PdfWidgetHighlightMode.Outline => PdfName.Create("O"),
            PdfWidgetHighlightMode.Push => PdfName.Create("P"),
            PdfWidgetHighlightMode.Toggle => PdfName.Create("T"),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The highlight mode is not valid"),
        };
    }

    /// <summary>Gets the appearance characteristics of the widget (<c>/MK</c>): its rotation, its colors and the captions of a button.</summary>
    public PdfAppearanceCharacteristics AppearanceCharacteristics => _characteristics ??= new PdfAppearanceCharacteristics(Dictionary);
}
