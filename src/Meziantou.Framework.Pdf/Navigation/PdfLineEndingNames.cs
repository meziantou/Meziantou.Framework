using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>Converts the line endings to and from their names (12.5.6.7, table 179).</summary>
internal static class PdfLineEndingNames
{
    public static PdfLineEnding Parse(PdfObject? value) => ((value is null ? null : PdfReference.ResolveIfReference(value)) as PdfName)?.Value switch
    {
        "Square" => PdfLineEnding.Square,
        "Circle" => PdfLineEnding.Circle,
        "Diamond" => PdfLineEnding.Diamond,
        "OpenArrow" => PdfLineEnding.OpenArrow,
        "ClosedArrow" => PdfLineEnding.ClosedArrow,
        "Butt" => PdfLineEnding.Butt,
        "ROpenArrow" => PdfLineEnding.ReversedOpenArrow,
        "RClosedArrow" => PdfLineEnding.ReversedClosedArrow,
        "Slash" => PdfLineEnding.Slash,
        _ => PdfLineEnding.None,
    };

    public static PdfName ToName(PdfLineEnding value) => PdfName.Create(value switch
    {
        PdfLineEnding.None => "None",
        PdfLineEnding.Square => "Square",
        PdfLineEnding.Circle => "Circle",
        PdfLineEnding.Diamond => "Diamond",
        PdfLineEnding.OpenArrow => "OpenArrow",
        PdfLineEnding.ClosedArrow => "ClosedArrow",
        PdfLineEnding.Butt => "Butt",
        PdfLineEnding.ReversedOpenArrow => "ROpenArrow",
        PdfLineEnding.ReversedClosedArrow => "RClosedArrow",
        PdfLineEnding.Slash => "Slash",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The line ending is not valid"),
    });

    /// <summary>Reads the two endings of an <c>/LE</c> array.</summary>
    public static (PdfLineEnding Start, PdfLineEnding End) Read(PdfDictionary annotation)
    {
        var array = annotation.Get<PdfArray>(AnnotationNames.LE);
        return array is { Count: 2 } ? (Parse(array[0]), Parse(array[1])) : (PdfLineEnding.None, PdfLineEnding.None);
    }

    /// <summary>Writes the two endings of an <c>/LE</c> array; none is written when both are <see cref="PdfLineEnding.None"/>.</summary>
    public static void Write(PdfDictionary annotation, PdfLineEnding start, PdfLineEnding end)
    {
        var (startName, endName) = (ToName(start), ToName(end));
        annotation[AnnotationNames.LE] = start == PdfLineEnding.None && end == PdfLineEnding.None ? null : new PdfArray(startName, endName);
    }
}
