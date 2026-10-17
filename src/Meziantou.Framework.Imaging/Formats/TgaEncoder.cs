namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for TGA encoding (Truevision TGA 2.0 true-color and grayscale still images).</summary>
/// <remarks>
/// <para>
/// The output is the conservative true-color and grayscale subset: bottom-up rows with the origin at the bottom left, no
/// image identification field, no color map, and a TGA 2.0 footer with no developer or extension area. Gray pixel formats
/// are written as 8-bit grayscale (image type 3 or 11), pixel formats with alpha as 32-bit BGRA with 8 declared alpha bits,
/// and the others as 24-bit BGR (image type 2 or 10). Color-mapped output, 15/16-bit output, right-to-left storage and
/// top-down storage are never written.
/// </para>
/// <para>
/// The output is lossless for 8-bit pixels, alpha included; 16-bit pixel formats require
/// <see cref="AllowBitDepthReduction"/>.
/// </para>
/// <para>
/// TGA stores exactly one frame: animated images and images with a poster frame are rejected with an
/// <see cref="UnsupportedImageFeatureException"/>; use <see cref="Image.CloneFrame(int)"/> to export one frame explicitly.
/// </para>
/// <para>
/// TGA stores no metadata this version writes: an ICC profile, EXIF, XMP, an orientation other than
/// <see cref="Metadata.ExifOrientation.TopLeft"/>, a resolution and text entries follow
/// <see cref="ImageEncoder.MetadataHandling"/> (rejected by default).
/// </para>
/// </remarks>
public sealed class TgaEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Tga;

    /// <summary>Gets the compression. Defaults to <see cref="TgaCompression.None"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="TgaCompression"/>.</exception>
    public TgaCompression Compression
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The compression is not valid.");

            field = value;
        }
    }

    /// <summary>Gets a value indicating whether 16-bit pixel formats may be reduced to 8 bits per component (nearest rounding). Defaults to <see langword="false"/>.</summary>
    public bool AllowBitDepthReduction { get; init; }
}
