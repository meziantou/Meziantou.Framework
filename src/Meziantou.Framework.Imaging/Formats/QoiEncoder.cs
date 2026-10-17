namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for QOI encoding (lossless 8-bit RGB and RGBA still images).</summary>
/// <remarks>
/// <para>
/// The output is lossless for 8-bit pixels: every sample is preserved, including the colors of fully transparent pixels. Pixel
/// formats with alpha are written with 4 channels (RGBA), the others with 3 (RGB). Gray pixel formats are written as RGB
/// (R = G = B, decoded as RGB), and 16-bit pixel formats require <see cref="AllowBitDepthReduction"/>.
/// </para>
/// <para>
/// QOI stores exactly one frame: animated images and images with a poster frame are rejected with an
/// <see cref="UnsupportedImageFeatureException"/>; use <see cref="Image.CloneFrame(int)"/> to export one frame explicitly.
/// </para>
/// <para>
/// The header colorspace field is written from <see cref="Metadata.ImageMetadata.TransferFunction"/>: 0 (sRGB with linear
/// alpha) or 1 (linear). QOI stores no other metadata: an ICC profile, EXIF, XMP, an orientation other than
/// <see cref="Metadata.ExifOrientation.TopLeft"/>, a resolution or text entries follow <see cref="ImageEncoder.MetadataHandling"/>
/// (rejected by default). <see cref="MetadataHandling.Strip"/> writes colorspace 0.
/// </para>
/// </remarks>
public sealed class QoiEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Qoi;

    /// <summary>Gets a value indicating whether 16-bit pixel formats may be reduced to 8 bits per component (nearest rounding). Defaults to <see langword="false"/>.</summary>
    public bool AllowBitDepthReduction { get; init; }
}
