using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// An image stored next to the primary image of a file: a thumbnail, a depth map, a gain map, a matte. It is borrowed
/// from its <see cref="AuxiliaryImageCollection"/>, and decoded on demand by <see cref="Decode(ImageDecodeOptions?)"/>.
/// </summary>
public sealed class AuxiliaryImage
{
    /// <summary>The auxiliary type of the gain maps of Apple devices.</summary>
    internal const string AppleGainMapType = "urn:com:apple:photo:2020:aux:hdrgainmap";

    private readonly AuxiliaryImageCollection _collection;

    internal AuxiliaryImage(AuxiliaryImageCollection collection, AuxiliaryImageKind kind, string? auxiliaryType, Size size)
    {
        _collection = collection;
        Kind = kind;
        AuxiliaryType = auxiliaryType;
        Size = size;
    }

    /// <summary>Gets what the image is.</summary>
    public AuxiliaryImageKind Kind { get; internal set; }

    /// <summary>
    /// Gets the type of an auxiliary image of a HEIC or AVIF file (the URN of its <c>auxC</c> property, such as
    /// <c>urn:mpeg:hevc:2015:auxid:2</c> for a depth map), or <see langword="null"/> for an image that has none (a
    /// thumbnail, the gain map of a tone map item, an image of a JPEG file).
    /// </summary>
    public string? AuxiliaryType { get; internal set; }

    /// <summary>
    /// Gets the size of the image: for a HEIF item, after its clean aperture and before its orientation, as
    /// <see cref="ImageInfo.Size"/> (its <c>ispe</c> property when the item cannot be decoded); for a JPEG image, the size
    /// of its frame header.
    /// </summary>
    public Size Size { get; }

    /// <summary>
    /// Gets the parameters of a gain map, or <see langword="null"/> when the image is not a gain map, when the file does
    /// not give them (a gain map of Apple without ISO 21496-1 metadata), or when they cannot be read.
    /// </summary>
    public GainMapMetadata? GainMapMetadata { get; internal set; }

    internal uint? HeifItemId { get; init; }

    internal (int Offset, int Length)? JpegRange { get; init; }

    internal byte? AlternatePrimaries { get; set; }

    internal InvalidImageContentException? MetadataError { get; set; }

    /// <summary>Decodes the image.</summary>
    /// <param name="options">The decoding options, or <see langword="null"/> for the configuration the collection was loaded with.</param>
    /// <returns>A new image owned by the caller: a gain map, a depth map or a matte is usually a grayscale image, whose samples are the coded values.</returns>
    /// <exception cref="InvalidImageContentException">The image is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The image uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a resource limit.</exception>
    public Image Decode(ImageDecodeOptions? options = null) => _collection.DecodeEntry(this, options, pixelFormat: null, CancellationToken.None);

    /// <summary>Decodes the image into a pixel format.</summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="options">The decoding options, or <see langword="null"/> for the configuration the collection was loaded with.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct.</exception>
    /// <exception cref="InvalidImageContentException">The image is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The image uses a feature that is not supported, or cannot be converted to <typeparamref name="TPixel"/>.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a resource limit.</exception>
    public Image<TPixel> Decode<TPixel>(ImageDecodeOptions? options = null)
        where TPixel : unmanaged
        => (Image<TPixel>)_collection.DecodeEntry(this, options, PixelFormats.GetPixelFormat<TPixel>(), CancellationToken.None);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Kind} {Size.Width}x{Size.Height}{(AuxiliaryType is null ? "" : " " + AuxiliaryType)}");
}
