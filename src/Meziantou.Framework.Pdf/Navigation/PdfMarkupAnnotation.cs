using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A markup annotation (ISO 32000-2, 12.5.6.2): a note, a text markup, a free text, a stamp, a shape, an ink drawing...
/// the annotations a reviewer adds to a document. Create them with the <c>Add*</c> methods of <see cref="PdfAnnotationCollection"/>.
/// </summary>
/// <remarks>
/// The markup annotations of the types the specification defines are instances of the derived classes
/// (<see cref="PdfTextAnnotation"/>, <see cref="PdfFreeTextAnnotation"/>, <see cref="PdfLineAnnotation"/>,
/// <see cref="PdfSquareAnnotation"/>, <see cref="PdfCircleAnnotation"/>, <see cref="PdfPolygonAnnotation"/>,
/// <see cref="PdfPolyLineAnnotation"/>, <see cref="PdfTextMarkupAnnotation"/>, <see cref="PdfStampAnnotation"/>,
/// <see cref="PdfCaretAnnotation"/>, <see cref="PdfInkAnnotation"/>, <see cref="PdfRedactAnnotation"/>); the sound
/// annotations are instances of this class.
/// </remarks>
public class PdfMarkupAnnotation : PdfAnnotation
{
    internal static readonly HashSet<string> Subtypes = new(StringComparer.Ordinal)
    {
        "Text", "FreeText", "Line", "Square", "Circle", "Polygon", "PolyLine", "Highlight", "Underline", "Squiggly", "StrikeOut", "Caret", "Stamp", "Ink", "Sound", "Redact",
    };

    private static readonly PdfName TName = PdfName.Create("T");
    private static readonly PdfName SubjName = PdfName.Create("Subj");
    private static readonly PdfName CreationDateName = PdfName.Create("CreationDate");

    internal PdfMarkupAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
        : base(page, dictionary, reference)
    {
    }

    /// <summary>Gets or sets the author of the annotation (<c>/T</c>), which viewers show in the title bar of its pop-up window.</summary>
    public string? Author
    {
        get => Dictionary.GetText(TName);
        set => Dictionary.SetText(TName, value);
    }

    /// <summary>Gets or sets the subject of the annotation (<c>/Subj</c>).</summary>
    public string? Subject
    {
        get => Dictionary.GetText(SubjName);
        set => Dictionary.SetText(SubjName, value);
    }

    /// <summary>Gets or sets the date the annotation was created (<c>/CreationDate</c>).</summary>
    public DateTimeOffset? CreationDate
    {
        get => PdfDate.TryParse(Dictionary.GetText(CreationDateName), out var value) ? value : null;
        set => Dictionary[CreationDateName] = value is { } date ? PdfString.FromText(PdfDate.Format(date)) : null;
    }

    /// <summary>Gets or sets the opacity of the annotation (<c>/CA</c>), from 0 to 1. The default is 1. The appearances the library creates are drawn with it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1.</exception>
    public double Opacity
    {
        get => Math.Clamp(Dictionary.GetNumber(AnnotationNames.CA, 1), 0, 1);
        set
        {
            if (!(value is >= 0 and <= 1))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The opacity must be between 0 and 1");

            Dictionary[AnnotationNames.CA] = value == 1 ? null : PdfObjectFactory.Number(value);
        }
    }

    /// <summary>Gets or sets the rich text of the annotation (<c>/RC</c>, 12.7.4.3): an XHTML fragment that viewers show instead of <see cref="PdfAnnotation.Contents"/>.</summary>
    public string? RichContents
    {
        get
        {
            try
            {
                return Dictionary.GetResolved(AnnotationNames.RC) switch
                {
                    PdfString text => text.ToText(),
                    PdfStream stream => System.Text.Encoding.UTF8.GetString(stream.GetDecodedData()),
                    _ => null,
                };
            }
            catch (PdfException)
            {
                return null;
            }
        }

        set => Dictionary.SetText(AnnotationNames.RC, value);
    }

    /// <summary>Gets or sets the pop-up annotation that shows the text of this annotation (<c>/Popup</c>), or <see langword="null"/>.</summary>
    /// <exception cref="ArgumentException">The pop-up annotation belongs to another document.</exception>
    public PdfPopupAnnotation? Popup
    {
        get => GetRelated(AnnotationNames.Popup) as PdfPopupAnnotation;
        set => SetRelated(AnnotationNames.Popup, value);
    }

    /// <summary>Gets or sets the annotation this annotation is a reply to (<c>/IRT</c>), or <see langword="null"/>.</summary>
    /// <exception cref="ArgumentException">The annotation belongs to another document.</exception>
    public PdfAnnotation? InReplyTo
    {
        get => GetRelated(AnnotationNames.IRT);
        set => SetRelated(AnnotationNames.IRT, value);
    }

    /// <summary>Gets or sets the relationship with <see cref="InReplyTo"/> (<c>/RT</c>). The default is <see cref="PdfAnnotationReplyType.Reply"/>.</summary>
    public PdfAnnotationReplyType ReplyType
    {
        get => Dictionary.GetName(AnnotationNames.RT)?.Value == "Group" ? PdfAnnotationReplyType.Group : PdfAnnotationReplyType.Reply;
        set => Dictionary[AnnotationNames.RT] = value switch
        {
            PdfAnnotationReplyType.Reply => null,
            PdfAnnotationReplyType.Group => PdfName.Create("Group"),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The reply type is not valid"),
        };
    }

    /// <summary>Gets the replies to this annotation: the markup annotations of its page whose <see cref="InReplyTo"/> is this annotation.</summary>
    /// <returns>The replies, in the order of the annotations of the page.</returns>
    public IReadOnlyList<PdfMarkupAnnotation> GetReplies()
    {
        var result = new List<PdfMarkupAnnotation>();
        foreach (var annotation in Page.Annotations)
        {
            if (annotation is PdfMarkupAnnotation markup && ReferenceEquals(markup.Dictionary.GetResolved(AnnotationNames.IRT), Dictionary))
            {
                result.Add(markup);
            }
        }

        return result;
    }
}
