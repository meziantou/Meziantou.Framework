namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The <c>ExtraChannelInfo</c> bundle of the image header (ISO/IEC 18181-1, section A.9): one channel besides the color channels.</summary>
internal sealed class JpegXlExtraChannelInfo
{
    // Values of the type field
    public const int TypeAlpha = 0;
    public const int TypeDepth = 1;
    public const int TypeSpotColor = 2;
    public const int TypeSelectionMask = 3;
    public const int TypeBlack = 4;
    public const int TypeCfa = 5;
    public const int TypeThermal = 6;
    public const int TypeNonOptional = 15;
    public const int TypeOptional = 16;

    private JpegXlExtraChannelInfo()
    {
    }

    /// <summary>Gets the kind of the channel.</summary>
    public int Type { get; private init; }

    /// <summary>Gets the bit depth of the samples.</summary>
    public JpegXlBitDepth BitDepth { get; private init; }

    /// <summary>Gets the base-2 logarithm of the factor by which the channel is smaller than the image.</summary>
    public int DimShift { get; private init; }

    /// <summary>Gets the UTF-8 name of the channel (empty when it has none).</summary>
    public byte[] Name { get; private init; } = [];

    /// <summary>Gets a value indicating whether the color channels are premultiplied by this alpha channel.</summary>
    public bool IsAlphaAssociated { get; private init; }

    /// <summary>Gets the color (red, green, blue) and the solidity of a spot color channel.</summary>
    public (float Red, float Green, float Blue, float Solidity) SpotColor { get; private init; }

    /// <summary>Gets the index of the color filter array channel.</summary>
    public uint CfaChannel { get; private init; } = 1;

    public static JpegXlExtraChannelInfo Read(JpegXlBitReader reader)
    {
        if (reader.ReadBool())
            return new JpegXlExtraChannelInfo { Type = TypeAlpha, BitDepth = JpegXlBitDepth.Default };

        var type = reader.ReadEnum();
        var bitDepth = JpegXlBitDepth.Read(reader);
        var dimShift = (int)reader.ReadU32(0, 0, 3, 0, 4, 0, 1, 3);
        if (dimShift > 3)
            throw JpegXlFormat.Invalid("an extra channel is subsampled by more than 8.");

        var name = JpegXlImageHeader.ReadName(reader);
        var alphaAssociated = type == TypeAlpha && reader.ReadBool();
        (float Red, float Green, float Blue, float Solidity) spot = default;
        if (type == TypeSpotColor)
        {
            spot = (reader.ReadF16(), reader.ReadF16(), reader.ReadF16(), reader.ReadF16());
        }

        var cfaChannel = type == TypeCfa ? reader.ReadU32(1, 0, 0, 2, 3, 4, 19, 8) : 1;
        if (type is (> TypeThermal and < TypeNonOptional) or TypeNonOptional or > TypeOptional)
            throw JpegXlFormat.Invalid("an extra channel has a type that this version of the format does not define.");

        return new JpegXlExtraChannelInfo
        {
            Type = type,
            BitDepth = bitDepth,
            DimShift = dimShift,
            Name = name,
            IsAlphaAssociated = alphaAssociated,
            SpotColor = spot,
            CfaChannel = cfaChannel,
        };
    }
}
