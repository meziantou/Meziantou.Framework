namespace Meziantou.Framework.Imaging;

/// <summary>The samples that <see cref="ImageSimilarity"/> compares, whatever the pixel formats of the frames.</summary>
public enum SimilarityPrecision
{
    /// <summary>
    /// 8-bit samples (peak 255), as <see cref="Image.CloneAs{TPixel}"/> converts the frames to <see cref="Rgba32"/>: 16-bit
    /// samples are rounded, and floating-point samples must be numbers in [0, 1]. This is the default.
    /// </summary>
    EightBit = 0,

    /// <summary>
    /// 16-bit samples (peak 65,535), as <see cref="Image.CloneAs{TPixel}"/> converts the frames to <see cref="Rgba64"/>: 8-bit
    /// samples are expanded exactly (<c>v × 257</c>), 16-bit samples are kept, and floating-point samples must be numbers in
    /// [0, 1].
    /// </summary>
    SixteenBit = 1,

    /// <summary>
    /// The samples as fractions of their range, in double precision: <c>v / 255</c> and <c>v / 65,535</c> for the integer
    /// formats, the stored value for the floating-point formats, which can be outside [0, 1] (high dynamic range) but must
    /// be finite. The peak is <see cref="ImageSimilarityOptions.DataRange"/>.
    /// </summary>
    FloatingPoint = 2,
}
