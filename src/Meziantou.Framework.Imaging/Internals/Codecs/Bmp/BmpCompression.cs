namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The <c>biCompression</c> values of a DIB header.</summary>
internal enum BmpCompression : uint
{
    /// <summary>Uncompressed samples (<c>BI_RGB</c>).</summary>
    Rgb = 0,

    /// <summary>8-bit run-length encoding (<c>BI_RLE8</c>), recognized and rejected.</summary>
    Rle8 = 1,

    /// <summary>4-bit run-length encoding (<c>BI_RLE4</c>), recognized and rejected.</summary>
    Rle4 = 2,

    /// <summary>Explicit red, green and blue masks (<c>BI_BITFIELDS</c>).</summary>
    BitFields = 3,

    /// <summary>An embedded JPEG payload (<c>BI_JPEG</c>), recognized and rejected.</summary>
    Jpeg = 4,

    /// <summary>An embedded PNG payload (<c>BI_PNG</c>), recognized and rejected.</summary>
    Png = 5,

    /// <summary>Explicit red, green, blue and alpha masks (<c>BI_ALPHABITFIELDS</c>, Windows CE).</summary>
    AlphaBitFields = 6,
}
