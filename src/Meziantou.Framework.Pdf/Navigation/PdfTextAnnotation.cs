using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A text annotation (ISO 32000-2, 12.5.6.4): a note, shown as an icon that opens its text.</summary>
public sealed class PdfTextAnnotation : PdfMarkupAnnotation
{
    internal PdfTextAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
        : base(page, dictionary, reference)
    {
    }

    /// <summary>Gets or sets the name of the icon of the note (<c>/Name</c>): <c>Comment</c>, <c>Key</c>, <c>Note</c>, <c>Help</c>, <c>NewParagraph</c>, <c>Paragraph</c>, <c>Insert</c>, or a name a viewer knows. The default is <c>Note</c>.</summary>
    /// <exception cref="ArgumentException">The value is empty.</exception>
    public string Icon
    {
        get => Dictionary.GetName(PdfNames.Name)?.Value ?? "Note";
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            Dictionary.SetName(PdfNames.Name, value);
        }
    }

    /// <summary>Gets or sets a value indicating whether the note is initially open (<c>/Open</c>).</summary>
    public bool IsOpen
    {
        get => Dictionary.GetBoolean(AnnotationNames.Open);
        set => Dictionary[AnnotationNames.Open] = value ? PdfBoolean.True : null;
    }

    /// <summary>Gets or sets the state of the note (<c>/State</c>, 12.5.6.3), such as <c>Accepted</c>, <c>Rejected</c>, <c>Completed</c> or <c>Marked</c>: a note with a state is a reply that records the state of the annotation it replies to.</summary>
    public string? State
    {
        get => Dictionary.GetText(AnnotationNames.State);
        set => Dictionary.SetText(AnnotationNames.State, value);
    }

    /// <summary>Gets or sets the model of <see cref="State"/> (<c>/StateModel</c>): <c>Marked</c> or <c>Review</c>.</summary>
    public string? StateModel
    {
        get => Dictionary.GetText(AnnotationNames.StateModel);
        set => Dictionary.SetText(AnnotationNames.StateModel, value);
    }
}
