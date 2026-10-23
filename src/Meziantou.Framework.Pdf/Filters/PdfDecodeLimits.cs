namespace Meziantou.Framework.Pdf.Filters;

/// <summary>The limits of the decoding of streams, from the read options of a document.</summary>
internal sealed class PdfDecodeLimits
{
    public PdfDecodeLimits(long maxDecodedStreamLength)
    {
        MaxDecodedStreamLength = maxDecodedStreamLength;
    }

    public static PdfDecodeLimits Default { get; } = new(PdfReadOptions.DefaultMaxDecodedStreamLength);

    public long MaxDecodedStreamLength { get; }
}
