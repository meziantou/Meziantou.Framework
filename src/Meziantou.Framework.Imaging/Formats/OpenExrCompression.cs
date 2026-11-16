namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The compression of the chunks of an OpenEXR file written by <see cref="OpenExrEncoder"/>.</summary>
/// <remarks>
/// <see cref="None"/>, <see cref="Rle"/>, <see cref="Zips"/>, <see cref="Zip"/> and <see cref="Piz"/> are lossless.
/// <see cref="Pxr24"/>, <see cref="B44"/>, <see cref="B44A"/>, <see cref="Dwaa"/> and <see cref="Dwab"/> lose precision:
/// choosing one of them is the explicit request to lose it. The number of scan lines of a chunk applies to scan-line
/// output; tiled output has one tile per chunk (<see cref="OpenExrEncoder.Tiling"/>).
/// </remarks>
public enum OpenExrCompression
{
    /// <summary>No compression (<c>NO_COMPRESSION</c>): one scan line per chunk; the output does not need a seekable destination.</summary>
    None = 0,

    /// <summary>Run-length coding of byte differences (<c>RLE_COMPRESSION</c>), one scan line per chunk.</summary>
    Rle = 1,

    /// <summary>zlib compression of byte differences (<c>ZIPS_COMPRESSION</c>), one scan line per chunk.</summary>
    Zips = 2,

    /// <summary>zlib compression of byte differences (<c>ZIP_COMPRESSION</c>), 16 scan lines per chunk. This is the default.</summary>
    Zip = 3,

    /// <summary>Wavelet and Huffman coding (<c>PIZ_COMPRESSION</c>), 32 scan lines per chunk: usually the smallest for photographs and noisy images.</summary>
    Piz = 4,

    /// <summary>
    /// zlib compression of sample differences (<c>PXR24_COMPRESSION</c>), 16 scan lines per chunk. Lossy for float samples,
    /// which are rounded to 24 bits (their 8 low mantissa bits are lost); lossless for half samples.
    /// </summary>
    Pxr24 = 5,

    /// <summary>
    /// Fixed-rate coding of 4×4 blocks of half samples (<c>B44_COMPRESSION</c>, 32 bytes to 14), 32 scan lines per chunk. Lossy
    /// for half samples, alpha included (infinities and values that are not numbers become 0); float samples are stored as they are.
    /// </summary>
    B44 = 6,

    /// <summary>
    /// <see cref="B44"/>, with blocks of a single value stored in 3 bytes (<c>B44A_COMPRESSION</c>): smaller for flat areas
    /// such as a constant alpha.
    /// </summary>
    B44A = 7,

    /// <summary>
    /// DCT coding of the colors (<c>DWAA_COMPRESSION</c>), 32 scan lines per chunk, at the quality of
    /// <see cref="OpenExrEncoder.DwaCompressionLevel"/>. Lossy for the colors, which are stored as half values even in float
    /// samples; alpha is run-length coded without loss.
    /// </summary>
    Dwaa = 8,

    /// <summary><see cref="Dwaa"/> with 256 scan lines per chunk (<c>DWAB_COMPRESSION</c>): smaller files, chunks of a larger area.</summary>
    Dwab = 9,
}
