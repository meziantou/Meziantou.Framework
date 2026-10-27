namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>Identifies how the stored color samples of an image relate to light intensity (the transfer function).</summary>
/// <remarks>
/// The value labels the stored samples, like an ICC profile: pixels are never converted between transfer functions
/// implicitly. <see cref="ImageProcessingExtensions.ConvertColorProfile"/> reads the label of an image without profile and
/// resets it to <see cref="Srgb"/>. Alpha is a linear coverage value in both cases. Only QOI (its header colorspace
/// field), JPEG XL (its color encoding) and TIFF pages of floating-point samples (linear light unless an ICC profile
/// describes them) store the label; saving <see cref="Linear"/> pixels to another format, or <see cref="Srgb"/>
/// floating-point pixels to TIFF without an ICC profile, follows <see cref="Formats.ImageEncoder.MetadataHandling"/>, so
/// they are never silently relabeled.
/// </remarks>
public enum ColorTransferFunction
{
    /// <summary>
    /// The color samples are sRGB encoded (gamma compressed), or described by the ICC profile when there is one. This is the
    /// default: untagged pixels are assumed sRGB (gray: sGray).
    /// </summary>
    Srgb = 0,

    /// <summary>
    /// The color samples are linear light (sRGB primaries, no transfer curve), as declared by a QOI colorspace field of 1, or
    /// as the floating-point samples of a TIFF page without ICC profile are.
    /// </summary>
    Linear = 1,
}
