namespace Meziantou.Framework.Pdf;

/// <summary>How the print dialog prints the pages on the sides of the sheets (<c>/Duplex</c>, ISO 32000-2, 12.2, table 147).</summary>
public enum PdfDuplexMode
{
    /// <summary>The document does not say: the print dialog uses its default.</summary>
    Unspecified,

    /// <summary>One side of each sheet (<c>Simplex</c>).</summary>
    Simplex,

    /// <summary>Both sides, the sheet flipped on its short edge (<c>DuplexFlipShortEdge</c>).</summary>
    DuplexFlipShortEdge,

    /// <summary>Both sides, the sheet flipped on its long edge (<c>DuplexFlipLongEdge</c>).</summary>
    DuplexFlipLongEdge,
}
