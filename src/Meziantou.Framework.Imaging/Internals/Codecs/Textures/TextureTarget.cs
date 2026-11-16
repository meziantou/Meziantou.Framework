namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A format the texture encoders write, independent of the container: how its texels are stored, the size of its blocks,
/// the working samples it is encoded from and the values it can represent.
/// </summary>
internal sealed class TextureTarget
{
    private TextureTarget(TextureEncoding encoding, int blockWidth, int blockHeight, int bytesPerBlock, TexturePrecision precision, int storedBits, TextureChannels channels, bool isSrgb)
    {
        Encoding = encoding;
        BlockWidth = blockWidth;
        BlockHeight = blockHeight;
        BytesPerBlock = bytesPerBlock;
        Precision = precision;
        StoredBits = storedBits;
        Channels = channels;
        IsSrgb = isSrgb;
    }

    public TextureEncoding Encoding { get; }

    public int BlockWidth { get; }

    public int BlockHeight { get; }

    /// <summary>Gets the length of a block (of a texel for the uncompressed formats), in bytes.</summary>
    public int BytesPerBlock { get; }

    /// <summary>Gets the working samples the texels are encoded from.</summary>
    public TexturePrecision Precision { get; }

    /// <summary>Gets the precision of a stored sample, in bits, to which the precision of the pixels is compared (32 for single-precision floats, 16 for halves).</summary>
    public int StoredBits { get; }

    public TextureChannels Channels { get; }

    /// <summary>Gets a value indicating whether the format declares sRGB-encoded colors (an <c>_SRGB</c> format).</summary>
    public bool IsSrgb { get; }

    /// <summary>Gets a value indicating whether the texels are compressed in blocks of several texels.</summary>
    public bool IsBlockCompressed => BlockWidth > 1;

    /// <summary>Gets a value indicating whether the format stores floating-point or signed values, which are linear light.</summary>
    public bool IsFloatingPoint => Precision == TexturePrecision.Float;

    /// <summary>Gets a value indicating whether the format stores signed values in [-1, 1].</summary>
    public bool IsSignedNormalized => Encoding is TextureEncoding.Bc4Signed or TextureEncoding.Bc5Signed or TextureEncoding.EacR11Signed or TextureEncoding.EacRg11Signed;

    /// <summary>Gets a value indicating whether the format stores halves (at most 65,504), and whether it rejects negative values (BC6H unsigned).</summary>
    public bool IsHalf => Encoding is TextureEncoding.RgbaHalf or TextureEncoding.Bc6H or TextureEncoding.Bc6HSigned;

    public static TextureTarget Create(TextureEncoding encoding, bool isSrgb = false, int blockWidth = 4, int blockHeight = 4) => encoding switch
    {
        TextureEncoding.Rgba8 or TextureEncoding.Bgra8 => new(encoding, 1, 1, 4, TexturePrecision.Byte, 8, TextureChannels.Rgba, isSrgb),
        TextureEncoding.R8 => new(encoding, 1, 1, 1, TexturePrecision.Byte, 8, TextureChannels.Gray, isSrgb),
        TextureEncoding.Rg8 => new(encoding, 1, 1, 2, TexturePrecision.Byte, 8, TextureChannels.RedGreen, isSrgb),
        TextureEncoding.R16 => new(encoding, 1, 1, 2, TexturePrecision.UInt16, 16, TextureChannels.Gray, isSrgb),
        TextureEncoding.Rgba16 => new(encoding, 1, 1, 8, TexturePrecision.UInt16, 16, TextureChannels.Rgba, isSrgb),
        TextureEncoding.RgbaHalf => new(encoding, 1, 1, 8, TexturePrecision.Float, 16, TextureChannels.Rgba, isSrgb: false),
        TextureEncoding.RgbaFloat => new(encoding, 1, 1, 16, TexturePrecision.Float, 32, TextureChannels.Rgba, isSrgb: false),
        TextureEncoding.Bc1 => new(encoding, 4, 4, 8, TexturePrecision.Byte, 8, TextureChannels.Rgba, isSrgb),
        TextureEncoding.Bc1Opaque => new(encoding, 4, 4, 8, TexturePrecision.Byte, 8, TextureChannels.Rgb, isSrgb),
        TextureEncoding.Bc2 or TextureEncoding.Bc3 or TextureEncoding.Bc7 => new(encoding, 4, 4, 16, TexturePrecision.Byte, 8, TextureChannels.Rgba, isSrgb),
        TextureEncoding.Bc4 => new(encoding, 4, 4, 8, TexturePrecision.Byte, 8, TextureChannels.Gray, isSrgb),
        TextureEncoding.Bc4Signed => new(encoding, 4, 4, 8, TexturePrecision.Float, 8, TextureChannels.Gray, isSrgb: false),
        TextureEncoding.Bc5 => new(encoding, 4, 4, 16, TexturePrecision.Byte, 8, TextureChannels.RedGreen, isSrgb),
        TextureEncoding.Bc5Signed => new(encoding, 4, 4, 16, TexturePrecision.Float, 8, TextureChannels.RedGreen, isSrgb: false),
        TextureEncoding.Bc6H or TextureEncoding.Bc6HSigned => new(encoding, 4, 4, 16, TexturePrecision.Float, 16, TextureChannels.Rgb, isSrgb: false),
        TextureEncoding.Etc2Rgb => new(encoding, 4, 4, 8, TexturePrecision.Byte, 8, TextureChannels.Rgb, isSrgb),
        TextureEncoding.Etc2RgbA1 => new(encoding, 4, 4, 8, TexturePrecision.Byte, 8, TextureChannels.Rgba, isSrgb),
        TextureEncoding.Etc2Rgba => new(encoding, 4, 4, 16, TexturePrecision.Byte, 8, TextureChannels.Rgba, isSrgb),
        TextureEncoding.EacR11 => new(encoding, 4, 4, 8, TexturePrecision.UInt16, 11, TextureChannels.Gray, isSrgb),
        TextureEncoding.EacR11Signed => new(encoding, 4, 4, 8, TexturePrecision.Float, 11, TextureChannels.Gray, isSrgb: false),
        TextureEncoding.EacRg11 => new(encoding, 4, 4, 16, TexturePrecision.UInt16, 11, TextureChannels.RedGreen, isSrgb),
        TextureEncoding.EacRg11Signed => new(encoding, 4, 4, 16, TexturePrecision.Float, 11, TextureChannels.RedGreen, isSrgb: false),
        TextureEncoding.Astc => new(encoding, blockWidth, blockHeight, 16, TexturePrecision.Byte, 8, TextureChannels.Rgba, isSrgb),
        _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Not a texture encoding."),
    };

    /// <summary>Gets the number of blocks of a surface in one direction.</summary>
    public static int GetBlockCount(int size, int blockSize) => (int)(((long)size + blockSize - 1) / blockSize);

    /// <summary>Gets the length of the texels of a surface, in bytes.</summary>
    public long GetSurfaceLength(int width, int height) => (long)GetBlockCount(width, BlockWidth) * GetBlockCount(height, BlockHeight) * BytesPerBlock;
}
