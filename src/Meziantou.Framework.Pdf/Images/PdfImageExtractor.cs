using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>Finds the images the content of a page paints, and the transformation matrix each is painted with.</summary>
internal static class PdfImageExtractor
{
    private const int MaxFormDepth = 32;
    private const int MaxOccurrences = 100_000;

    public static List<PdfImageOccurrence> Extract(PdfPage page)
    {
        var result = new List<PdfImageOccurrence>();
        var forms = new HashSet<PdfStream>(ReferenceEqualityComparer.Instance);
        Run(page, page.GetContentData(), page.Resources, PdfMatrix.Identity, forms, depth: 0, result);
        return result;
    }

    private static void Run(PdfPage page, ReadOnlyMemory<byte> content, PdfDictionary? resources, PdfMatrix initial, HashSet<PdfStream> forms, int depth, List<PdfImageOccurrence> result)
    {
        var reader = new ContentStreamReader(content);
        var operands = new List<PdfObject>();
        var states = new Stack<PdfMatrix>();
        var ctm = initial;
        while (result.Count < MaxOccurrences && reader.TryRead(operands, out var name))
        {
            switch (name)
            {
                case "q":
                    states.Push(ctm);
                    break;
                case "Q" when states.Count > 0:
                    ctm = states.Pop();
                    break;
                case "cm" when operands.Count == 6 && operands.TrueForAll(operand => operand is PdfNumber):
                    double Get(int index) => ((PdfNumber)operands[index]).DoubleValue;
                    ctm = new PdfMatrix(Get(0), Get(1), Get(2), Get(3), Get(4), Get(5)) * ctm;
                    break;
                case "BI" when operands.Count == 2 && operands[0] is PdfDictionary inline && operands[1] is PdfString data:
                    result.Add(PdfImageOccurrence.FromInline(page, ctm, inline, data.Bytes, resources));
                    break;
                case "Do" when operands.Count == 1 && operands[0] is PdfName xobjectName:
                    var value = resources?.Get<PdfDictionary>(PdfNames.XObject)?[xobjectName];
                    if (value is null || PdfReference.ResolveIfReference(value) is not PdfStream stream)
                        break;

                    var subtype = stream.Dictionary.GetName(PdfNames.Subtype);
                    if (subtype == PdfNames.Image)
                    {
                        result.Add(PdfImageOccurrence.FromXObject(page, ctm, stream, xobjectName.Value, value as PdfReference, resources));
                    }
                    else if (subtype == PdfNames.Form && depth < MaxFormDepth && forms.Add(stream))
                    {
                        // A form paints its content with its matrix, and its resources or those of the page (8.10.1)
                        var matrix = stream.Dictionary.Get<PdfArray>(PdfNames.Matrix) is { Count: 6 } values
                            ? new PdfMatrix(values.GetNumber(0), values.GetNumber(1), values.GetNumber(2), values.GetNumber(3), values.GetNumber(4), values.GetNumber(5))
                            : PdfMatrix.Identity;
                        Run(page, stream.GetDecodedData(), stream.Dictionary.Get<PdfDictionary>(PdfNames.Resources) ?? resources, matrix * ctm, forms, depth + 1, result);
                        forms.Remove(stream);
                    }

                    break;
            }
        }
    }
}
