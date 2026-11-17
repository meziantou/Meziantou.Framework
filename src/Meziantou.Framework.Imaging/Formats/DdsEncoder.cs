namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for DDS (DirectDraw Surface) encoding: uncompressed or BC1 to BC7 textures, with their mipmaps.</summary>
/// <remarks>
/// <para>
/// <see cref="Image.Save(string, ImageEncoder?)"/> writes a 2D texture of one surface, and of its mipmaps with
/// <see cref="GenerateMipmaps"/>. <see cref="ImageCollection.Save(string, ImageEncoder?)"/> writes the
/// <see cref="ImageCollectionKind.TextureSurfaces"/> collections: cube maps, texture arrays and arrays of cube maps, whose
/// entries say where they are (<see cref="ImageCollectionEntry.TextureSurface"/>), with the mipmap levels they hold or the
/// ones <see cref="GenerateMipmaps"/> generates. Volume textures are not written.
/// </para>
/// <para>
/// Every loss beyond the block compression of the chosen <see cref="Format"/> needs a setting, checked before anything is
/// written: a format of fewer bits than the pixels (an 8-bit format for 16-bit pixels) needs
/// <see cref="AllowBitDepthReduction"/>, and a format that lacks a channel the pixels use needs
/// <see cref="AllowChannelReduction"/>: alpha that is not opaque in a format without alpha, color in a one-channel
/// format (R8, R16 and BC4 are read as gray), or blue other than 0 in a two-channel format (R8G8 and BC5 are read as red
/// and green, with a blue of 0). Floating-point formats store linear light: the image must be labeled
/// <see cref="Metadata.ColorTransferFunction.Linear"/> (the default of floating-point images), unless
/// <see cref="ImageEncoder.MetadataHandling"/> discards the label. Their values must be finite and, for BC6H, at most
/// 65,504 (not negative for <see cref="DdsTextureFormat.Bc6HUf16"/>); the signed BC4 and BC5 formats store values in
/// [-1, 1]. Other values are rejected with an <see cref="UnsupportedImageFeatureException"/>.
/// </para>
/// <para>
/// The <c>_SRGB</c> formats declare sRGB-encoded colors, and are read back labeled with the CICP description of sRGB; the
/// other integer formats are read back untagged. An image labeled linear, or with another color description, can only be
/// written with a metadata policy that discards the label. DDS stores no other metadata.
/// </para>
/// </remarks>
public sealed class DdsEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Dds;

    /// <summary>
    /// Gets the format of the surfaces, or <see langword="null"/> (the default) for the uncompressed format that stores the
    /// pixels without loss: <see cref="DdsTextureFormat.R8G8B8A8Unorm"/> for 8-bit pixels (the <c>_SRGB</c> form when the
    /// image has the CICP description of sRGB), <see cref="DdsTextureFormat.R8Unorm"/> or
    /// <see cref="DdsTextureFormat.R16Unorm"/> for gray pixels without alpha, <see cref="DdsTextureFormat.R16G16B16A16Unorm"/>
    /// for 16-bit pixels, and the floating-point formats of the same precision for floating-point pixels.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="DdsTextureFormat"/>.</exception>
    public DdsTextureFormat? TextureFormat
    {
        get;
        init
        {
            if (value is { } format && !Enum.IsDefined(format))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The texture format is not valid.");

            field = value;
        }
    }

    /// <summary>Gets how hard the block compressors search. Defaults to <see cref="TextureCompressionQuality.Thorough"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="TextureCompressionQuality"/>.</exception>
    public TextureCompressionQuality Quality
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The quality is not valid.");

            field = value;
        }
    } = TextureCompressionQuality.Thorough;

    /// <summary>Gets the header written after <c>DDS_HEADER</c>. Defaults to <see cref="DdsHeaderVersion.Automatic"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="DdsHeaderVersion"/>.</exception>
    public DdsHeaderVersion HeaderVersion
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The header version is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the full chain of mipmaps is generated, down to 1×1, from the full-size surface of
    /// each face or array element. Defaults to <see langword="false"/>: only the surfaces of the image or of the collection
    /// are written.
    /// </summary>
    /// <remarks>
    /// Each level averages the texels of the previous one that it covers (a box filter, exact for odd sizes), weighted by
    /// their alpha, in linear light for sRGB-encoded colors. A collection that already holds mipmap levels cannot generate
    /// them.
    /// </remarks>
    public bool GenerateMipmaps { get; init; }

    /// <summary>
    /// Gets a value indicating whether pixels of more bits than <see cref="TextureFormat"/> stores (16-bit pixels in an 8-bit
    /// format, which BC1 to BC5 and BC7 are) are reduced to its precision. Defaults to <see langword="false"/>: such pixels are
    /// rejected.
    /// </summary>
    public bool AllowBitDepthReduction { get; init; }

    /// <summary>
    /// Gets a value indicating whether the channels <see cref="TextureFormat"/> does not store are dropped. Defaults to
    /// <see langword="false"/>: a pixel whose alpha is not opaque in a format without alpha, whose colors are not gray in a
    /// one-channel format (which stores red), or whose blue is not 0 in a two-channel format, is rejected.
    /// </summary>
    public bool AllowChannelReduction { get; init; }

    /// <summary>Gets a value indicating whether the output stores floating-point samples: the floating-point formats and the signed ones, and the default format of floating-point pixels.</summary>
    internal override bool StoresFloatingPointSamples => TextureFormat is null or DdsTextureFormat.R16G16B16A16Float or DdsTextureFormat.R32G32B32A32Float or DdsTextureFormat.Bc4Snorm or DdsTextureFormat.Bc5Snorm or DdsTextureFormat.Bc6HUf16 or DdsTextureFormat.Bc6HSf16;
}
