namespace Meziantou.Framework.Imaging.Formats;

/// <summary>How <see cref="TiffEncoder"/> compresses the strips or tiles of a page.</summary>
/// <remarks>
/// Every compression but <see cref="Jpeg"/> is lossless: the decoded pixels are identical whichever one is chosen. The
/// CCITT compressions code bilevel data and require <see cref="TiffPixelLayout.Bilevel"/>; <see cref="Jpeg"/> requires
/// <see cref="TiffPixelLayout.Auto"/>. Saving with another combination fails with an <see cref="ArgumentException"/>.
/// </remarks>
public enum TiffCompression
{
    /// <summary>No compression: the samples are stored as they are (<c>Compression</c> 1).</summary>
    None = 0,

    /// <summary>Deflate, as Adobe's <c>Compression</c> 8 (a zlib datastream per strip). The default.</summary>
    Deflate = 1,

    /// <summary>
    /// LZW (<c>Compression</c> 5), as TIFF 6.0 defines it. No predictor is written for integer samples unless
    /// <see cref="TiffEncoder.Predictor"/> selects one.
    /// </summary>
    Lzw = 2,

    /// <summary>PackBits run lengths (<c>Compression</c> 32773): each row is packed separately.</summary>
    PackBits = 3,

    /// <summary>
    /// CCITT modified Huffman run lengths (<c>Compression</c> 2): every row is coded in one dimension and starts on a byte
    /// boundary. Requires <see cref="TiffPixelLayout.Bilevel"/>.
    /// </summary>
    CcittModifiedHuffman = 4,

    /// <summary>
    /// CCITT Group 3 (T.4) facsimile coding (<c>Compression</c> 3), in one dimension, with an end-of-line code before each
    /// row and no fill bits. Requires <see cref="TiffPixelLayout.Bilevel"/>.
    /// </summary>
    CcittGroup3 = 5,

    /// <summary>
    /// CCITT Group 4 (T.6) facsimile coding (<c>Compression</c> 4): the smallest lossless output for bilevel documents.
    /// Requires <see cref="TiffPixelLayout.Bilevel"/>.
    /// </summary>
    CcittGroup4 = 6,

    /// <summary>
    /// Baseline JPEG (<c>Compression</c> 7, TIFF Technical Note 2): <strong>lossy</strong>. Each page is one strip that
    /// holds a complete JPEG datastream, grayscale for the gray pixel formats and YCbCr for the others (see
    /// <see cref="TiffEncoder.JpegQuality"/> and <see cref="TiffEncoder.JpegChromaSubsampling"/>). JPEG stores 8-bit
    /// samples without alpha: a 16-bit pixel format or a pixel that is not opaque is rejected with an
    /// <see cref="UnsupportedImageFeatureException"/>. Requires <see cref="TiffPixelLayout.Auto"/>.
    /// </summary>
    Jpeg = 7,
}
