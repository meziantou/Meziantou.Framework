namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The predictor <see cref="TiffEncoder"/> applies to integer samples before compressing them.</summary>
/// <remarks>
/// A predictor never changes the pixels: it replaces each sample with its difference from a neighbor, which the reader
/// adds back. Floating-point samples compressed with Deflate or LZW always use the floating-point predictor of TIFF
/// Technical Note 3 (<c>Predictor = 3</c>), whatever this setting.
/// </remarks>
public enum TiffPredictor
{
    /// <summary>No predictor for integer samples (<c>Predictor = 1</c>, the tag is not written). The default.</summary>
    None = 0,

    /// <summary>
    /// Horizontal differencing (<c>Predictor = 2</c>, TIFF 6.0 section 14): each 8- or 16-bit sample of a row is
    /// replaced with its difference from the sample of the same channel on its left, modulo 2^8 or 2^16. Smooth images,
    /// such as photographs and gradients, usually compress to fewer bytes; noisy or flat synthetic images may not. Requires
    /// <see cref="TiffCompression.Deflate"/> or <see cref="TiffCompression.Lzw"/> and <see cref="TiffPixelLayout.Auto"/>:
    /// saving fails with an <see cref="ArgumentException"/> otherwise.
    /// </summary>
    HorizontalDifferencing = 1,
}
