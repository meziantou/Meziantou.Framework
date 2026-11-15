namespace Meziantou.Framework.Imaging.Formats;

/// <summary>
/// Immutable settings for KTX 2.0 encoding: uncompressed, BCn, ETC2, EAC or ASTC textures, with their mip levels, optionally
/// supercompressed with ZLIB.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Image.Save(string, ImageEncoder?)"/> writes a 2D texture of one level, and its mip levels with
/// <see cref="GenerateMipmaps"/>. <see cref="ImageCollection.Save(string, ImageEncoder?)"/> writes the
/// <see cref="ImageCollectionKind.TextureSurfaces"/> collections: cube maps, array textures and arrays of cube maps, whose
/// entries say where they are (<see cref="ImageCollectionEntry.TextureSurface"/>), with the mip levels they hold or the
/// ones <see cref="GenerateMipmaps"/> generates. 3D textures and Basis Universal (ETC1S, UASTC) are not written.
/// </para>
/// <para>
/// Every loss beyond the block compression of the chosen <see cref="TextureFormat"/> needs a setting, checked before
/// anything is written: a format of fewer bits than the pixels needs <see cref="AllowBitDepthReduction"/> (16-bit pixels in
/// an 8-bit format, which BC1 to BC5, BC7, ETC2 and ASTC are, or in the 11 bits of EAC), and a format that lacks a channel
/// the pixels use needs <see cref="AllowChannelReduction"/>: alpha that is not opaque in a format without alpha, color in
/// a one-channel format (written with the swizzle <c>rrr1</c>, so that it is read as gray), or blue other than 0 in a
/// two-channel format. Floating-point formats store linear light and finite values (at most 65,504 for the halves and
/// BC6H, not negative for <see cref="Ktx2TextureFormat.Bc6HUfloat"/>); the signed formats store values in [-1, 1].
/// </para>
/// <para>
/// The data format descriptor carries the color label of the image: the sRGB or the linear transfer function, and the
/// primaries of its CICP description when they are among those of KTX (BT.709, BT.2020, Display P3, ...). The orientation
/// is written as <c>KTXorientation</c> (the four orientations without a transposition), and the writer as
/// <c>KTXwriter</c>. ICC profiles, EXIF, XMP and text entries follow <see cref="ImageEncoder.MetadataHandling"/>.
/// </para>
/// </remarks>
public sealed class Ktx2Encoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Ktx2;

    /// <summary>
    /// Gets the format of the texels, or <see langword="null"/> (the default) for the uncompressed format that stores the
    /// pixels without loss: <see cref="Ktx2TextureFormat.R8G8B8A8"/> for 8-bit pixels, <see cref="Ktx2TextureFormat.R8"/>
    /// or <see cref="Ktx2TextureFormat.R16"/> for gray pixels without alpha, <see cref="Ktx2TextureFormat.R16G16B16A16"/> for
    /// 16-bit pixels, and the floating-point formats of the same precision for floating-point pixels.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Ktx2TextureFormat"/>.</exception>
    public Ktx2TextureFormat? TextureFormat
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

    /// <summary>Gets the supercompression of the mip levels. Defaults to <see cref="Ktx2Supercompression.None"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Ktx2Supercompression"/>.</exception>
    public Ktx2Supercompression Supercompression
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The supercompression is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the full chain of mip levels is generated, down to 1×1, from the base level of each
    /// face or layer. Defaults to <see langword="false"/>: only the levels of the image or of the collection are written.
    /// </summary>
    /// <remarks>
    /// Each level averages the texels of the previous one that it covers (a box filter, exact for odd sizes), weighted by
    /// their alpha, in linear light for sRGB-encoded colors. A collection that already holds mip levels cannot generate them.
    /// </remarks>
    public bool GenerateMipmaps { get; init; }

    /// <summary>
    /// Gets a value indicating whether pixels of more bits than <see cref="TextureFormat"/> stores are reduced to its
    /// precision. Defaults to <see langword="false"/>: such pixels are rejected.
    /// </summary>
    public bool AllowBitDepthReduction { get; init; }

    /// <summary>
    /// Gets a value indicating whether the channels <see cref="TextureFormat"/> does not store are dropped. Defaults to
    /// <see langword="false"/>: a pixel whose alpha is not opaque in a format without alpha, whose colors are not gray in a
    /// one-channel format (which stores red), or whose blue is not 0 in a two-channel format, is rejected.
    /// </summary>
    public bool AllowChannelReduction { get; init; }

    /// <summary>Gets a value indicating whether the output stores floating-point samples: the floating-point formats and the signed ones, and the default format of floating-point pixels.</summary>
    internal override bool StoresFloatingPointSamples => TextureFormat is null or Ktx2TextureFormat.R16G16B16A16Sfloat or Ktx2TextureFormat.R32G32B32A32Sfloat or Ktx2TextureFormat.Bc4Snorm or Ktx2TextureFormat.Bc5Snorm or Ktx2TextureFormat.Bc6HUfloat or Ktx2TextureFormat.Bc6HSfloat or Ktx2TextureFormat.EacR11Snorm or Ktx2TextureFormat.EacR11G11Snorm;
}
