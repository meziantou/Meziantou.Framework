using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>An ink annotation (ISO 32000-2, 12.5.6.13): freehand strokes, as drawn with a pen.</summary>
public sealed class PdfInkAnnotation : PdfMarkupAnnotation
{
    internal PdfInkAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
        : base(page, dictionary, reference)
    {
    }

    /// <summary>Gets or sets the strokes (<c>/InkList</c>): each one a list of points, in default user space, joined by straight lines.</summary>
    /// <exception cref="ArgumentException">A stroke is empty.</exception>
    public IReadOnlyList<IReadOnlyList<PdfPoint>> Strokes
    {
        get
        {
            var result = new List<IReadOnlyList<PdfPoint>>();
            var total = 0;
            if (Dictionary.Get<PdfArray>(AnnotationNames.InkList) is { } strokes)
            {
                foreach (var item in strokes)
                {
                    if (PdfReference.ResolveIfReference(item) is not PdfArray stroke)
                        continue;

                    var points = ReadPoints(stroke);
                    total += points.Count;
                    if (total > MaxPoints)
                        break;

                    result.Add(points);
                }
            }

            return result;
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Any(stroke => stroke is null || stroke.Count == 0))
                throw new ArgumentException("A stroke has at least one point", nameof(value));

            Dictionary[AnnotationNames.InkList] = new PdfArray(value.Select(stroke => (PdfObject)WritePoints(stroke)));
        }
    }
}
