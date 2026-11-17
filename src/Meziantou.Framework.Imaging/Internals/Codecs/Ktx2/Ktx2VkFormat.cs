namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A <c>VkFormat</c> the decoder reads: the size of its texel blocks, the color model and the samples its data format
/// descriptor must have (KTX section 3.10.1), and how its texels are decoded.
/// </summary>
internal sealed class Ktx2VkFormat
{
    public Ktx2VkFormat(int value, string name, Ktx2Encoding encoding, int blockWidth, int blockHeight, int bytesPerBlock, int typeSize, int colorModel, bool isSrgb, Ktx2NumericType numericType, Ktx2OutputPrecision precision, bool isSigned, int channelMask, Ktx2Sample[] samples)
    {
        Value = value;
        Name = name;
        Encoding = encoding;
        BlockWidth = blockWidth;
        BlockHeight = blockHeight;
        BytesPerBlock = bytesPerBlock;
        TypeSize = typeSize;
        ColorModel = colorModel;
        IsSrgb = isSrgb;
        NumericType = numericType;
        Precision = precision;
        IsSigned = isSigned;
        ChannelMask = channelMask;
        Samples = samples;
    }

    /// <summary>Gets the value of the <c>VkFormat</c> enumeration (0 for the formats of Basis Universal).</summary>
    public int Value { get; }

    /// <summary>Gets the name of the format, without its <c>VK_FORMAT_</c> prefix.</summary>
    public string Name { get; }

    public Ktx2Encoding Encoding { get; }

    public int BlockWidth { get; }

    public int BlockHeight { get; }

    /// <summary>Gets the length of a texel block (a texel for uncompressed formats), in bytes.</summary>
    public int BytesPerBlock { get; }

    /// <summary>Gets the value <c>typeSize</c> must have (section 3.3).</summary>
    public int TypeSize { get; }

    public int ColorModel { get; }

    /// <summary>Gets a value indicating whether the format is an <c>_SRGB</c> format, whose transfer function must be sRGB.</summary>
    public bool IsSrgb { get; }

    public Ktx2NumericType NumericType { get; }

    public Ktx2OutputPrecision Precision { get; }

    /// <summary>Gets a value indicating whether the samples are signed (the signed EAC formats).</summary>
    public bool IsSigned { get; }

    /// <summary>Gets the channels the texels have: bit 0 red, 1 green, 2 blue, 3 alpha.</summary>
    public int ChannelMask { get; }

    /// <summary>Gets the samples the data format descriptor must have, in their order.</summary>
    public Ktx2Sample[] Samples { get; }
}
