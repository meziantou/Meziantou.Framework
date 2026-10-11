namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The pixel storage of a DIB, resolved from the compression field of its header.
/// </summary>
/// <remarks>
/// This is the decoded model, not the stored value: Windows and OS/2 2.x headers assign different meanings to the stored
/// values 3 and 4 (see the constants of <see cref="BmpFormat"/>), and the variants that are never decoded (embedded JPEG or
/// PNG payloads, OS/2 Huffman 1D) are rejected while the header is parsed, so they have no member.
/// </remarks>
internal enum BmpDibCompression
{
    /// <summary>Uncompressed samples (<c>BI_RGB</c>).</summary>
    Rgb,

    /// <summary>8-bit run-length encoding (<c>BI_RLE8</c>).</summary>
    Rle8,

    /// <summary>4-bit run-length encoding (<c>BI_RLE4</c>).</summary>
    Rle4,

    /// <summary>Uncompressed samples with explicit red, green and blue masks (<c>BI_BITFIELDS</c>).</summary>
    BitFields,

    /// <summary>Uncompressed samples with explicit red, green, blue and alpha masks (<c>BI_ALPHABITFIELDS</c>, Windows CE).</summary>
    AlphaBitFields,

    /// <summary>24-bit run-length encoding (<c>BCA_RLE24</c>, OS/2 2.x headers only).</summary>
    Rle24,
}
