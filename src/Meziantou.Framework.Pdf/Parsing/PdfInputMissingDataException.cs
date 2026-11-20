namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>Thrown by <see cref="HttpRangePdfInput"/> while its work is deferred: the ranges of the file the work needs that are not downloaded yet.</summary>
#pragma warning disable CA1032, CA1064 // A signal between the input and the code that defers its work: it never escapes the library
internal sealed class PdfInputMissingDataException : Exception
#pragma warning restore CA1032, CA1064
{
    public PdfInputMissingDataException(List<(long Offset, int Length)> ranges)
        : base("Bytes of the file are not downloaded yet")
    {
        Ranges = ranges;
    }

    public List<(long Offset, int Length)> Ranges { get; }
}
