using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A free text annotation (ISO 32000-2, 12.5.6.6): a text drawn directly on the page, in a box, possibly with a callout line.</summary>
public sealed class PdfFreeTextAnnotation : PdfMarkupAnnotation
{
    internal PdfFreeTextAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
        : base(page, dictionary, reference)
    {
    }

    /// <summary>Gets or sets the default appearance string of the text (<c>/DA</c>, 12.7.4.3): the operators that set its font, size and color, such as <c>/Helvetica 12 Tf 0 g</c>.</summary>
    public string? DefaultAppearance
    {
        get => Dictionary.GetText(AnnotationNames.DA);
        set => Dictionary[AnnotationNames.DA] = value is null ? null : PdfString.FromBytes(System.Text.Encoding.Latin1.GetBytes(value));
    }

    /// <summary>Gets or sets the alignment of the text (<c>/Q</c>): <see cref="PdfTextAlignment.Left"/>, <see cref="PdfTextAlignment.Center"/> or <see cref="PdfTextAlignment.Right"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is <see cref="PdfTextAlignment.Justify"/>, which a free text cannot have.</exception>
    public PdfTextAlignment Alignment
    {
        get => Dictionary.GetInteger(AnnotationNames.Q, 0) switch
        {
            1 => PdfTextAlignment.Center,
            2 => PdfTextAlignment.Right,
            _ => PdfTextAlignment.Left,
        };
        set => Dictionary[AnnotationNames.Q] = value switch
        {
            PdfTextAlignment.Left => null,
            PdfTextAlignment.Center => PdfInteger.Get(1),
            PdfTextAlignment.Right => PdfInteger.Get(2),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "A free text is aligned to the left, the center or the right"),
        };
    }

    /// <summary>Gets or sets the points of the callout line (<c>/CL</c>): two or three points, from the point the callout designates to the box; an empty list when the annotation has none.</summary>
    /// <exception cref="ArgumentException">The list does not have 0, 2 or 3 points.</exception>
    public IReadOnlyList<PdfPoint> CalloutLine
    {
        get
        {
            var points = ReadPoints(Dictionary.Get<PdfArray>(AnnotationNames.CL));
            return points.Count is 2 or 3 ? points : [];
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Count is not (0 or 2 or 3))
                throw new ArgumentException("A callout line has 2 or 3 points", nameof(value));

            Dictionary[AnnotationNames.CL] = value.Count == 0 ? null : WritePoints(value);
        }
    }

    /// <summary>Gets or sets the ending of the callout line at the point it designates (<c>/LE</c>).</summary>
    public PdfLineEnding CalloutLineEnding
    {
        get => PdfLineEndingNames.Parse(Dictionary[AnnotationNames.LE]);
        set => Dictionary[AnnotationNames.LE] = value == PdfLineEnding.None ? null : PdfLineEndingNames.ToName(value);
    }

    /// <summary>Gets or sets the intent of the annotation (<c>/IT</c>): <c>FreeText</c>, <c>FreeTextCallout</c> or <c>FreeTextTypeWriter</c>.</summary>
    public string? Intent
    {
        get => Dictionary.GetName(AnnotationNames.IT)?.Value;
        set => Dictionary[AnnotationNames.IT] = value is null ? null : PdfName.Create(value);
    }
}
