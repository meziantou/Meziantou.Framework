using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A markup annotation (ISO 32000-2, 12.5.6.2): a note, a text markup, a free text, a stamp, a shape, an ink drawing...
/// the annotations a reviewer adds to a document. Create them with the <c>Add*</c> methods of <see cref="PdfAnnotationCollection"/>.
/// </summary>
public sealed class PdfMarkupAnnotation : PdfAnnotation
{
    internal static readonly HashSet<string> Subtypes = new(StringComparer.Ordinal)
    {
        "Text", "FreeText", "Line", "Square", "Circle", "Polygon", "PolyLine", "Highlight", "Underline", "Squiggly", "StrikeOut", "Caret", "Stamp", "Ink", "Sound", "Redact",
    };

    private static readonly PdfName TName = PdfName.Create("T");
    private static readonly PdfName SubjName = PdfName.Create("Subj");
    private static readonly PdfName CreationDateName = PdfName.Create("CreationDate");
    private static readonly PdfName CAName = PdfName.Create("CA");

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
        get => Math.Clamp(Dictionary.GetNumber(CAName, 1), 0, 1);
        set
        {
            if (!(value is >= 0 and <= 1))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The opacity must be between 0 and 1");

            Dictionary[CAName] = value == 1 ? null : PdfObjectFactory.Number(value);
        }
    }
}
