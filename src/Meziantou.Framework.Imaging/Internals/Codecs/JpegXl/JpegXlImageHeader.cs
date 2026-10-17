namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The headers at the start of a JPEG XL codestream (ISO/IEC 18181-1, annex A): the signature, the <c>SizeHeader</c>, the
/// <c>ImageMetadata</c> bundle and the <c>CustomTransformData</c> bundle. The ICC profile, when the color encoding
/// announces one, and the frames follow.
/// </summary>
internal sealed class JpegXlImageHeader
{
    private JpegXlImageHeader()
    {
    }

    /// <summary>Gets the width of the image, before the orientation is applied.</summary>
    public uint Width { get; private init; }

    /// <summary>Gets the height of the image, before the orientation is applied.</summary>
    public uint Height { get; private init; }

    /// <summary>Gets the orientation, from 1 to 8 with the meaning of the EXIF orientation tag.</summary>
    public int Orientation { get; private init; } = 1;

    /// <summary>Gets the size the image is meant to be shown at, when the header gives one.</summary>
    public (uint Width, uint Height)? IntrinsicSize { get; private init; }

    /// <summary>Gets the size of the preview frame that precedes the frames of the image, when there is one.</summary>
    public (uint Width, uint Height)? PreviewSize { get; private init; }

    /// <summary>Gets a value indicating whether the frames form an animation: they then have a duration.</summary>
    public bool HasAnimation { get; private init; }

    /// <summary>Gets the numerator of the number of ticks per second of the animation.</summary>
    public uint TicksPerSecondNumerator { get; private init; }

    /// <summary>Gets the denominator of the number of ticks per second of the animation.</summary>
    public uint TicksPerSecondDenominator { get; private init; }

    /// <summary>Gets the number of times the animation is played; 0 means forever.</summary>
    public uint LoopCount { get; private init; }

    /// <summary>Gets a value indicating whether every frame of the animation carries a time code.</summary>
    public bool HasTimecodes { get; private init; }

    /// <summary>Gets the bit depth of the color channels.</summary>
    public JpegXlBitDepth BitDepth { get; private init; } = JpegXlBitDepth.Default;

    /// <summary>Gets a value indicating whether every sample of the Modular coding fits a 16-bit signed integer.</summary>
    public bool Modular16BitBuffers { get; private init; } = true;

    /// <summary>Gets the extra channels, in order.</summary>
    public JpegXlExtraChannelInfo[] ExtraChannels { get; private init; } = [];

    /// <summary>Gets a value indicating whether the color channels are stored in the XYB color space.</summary>
    public bool IsXybEncoded { get; private init; } = true;

    /// <summary>Gets the color encoding of the image.</summary>
    public JpegXlColorEncoding ColorEncoding { get; private init; } = JpegXlColorEncoding.Default;

    /// <summary>Gets the luminance, in candelas per square meter, of the largest sample value.</summary>
    public float IntensityTarget { get; private init; } = 255f;

    /// <summary>Gets the parameters of the color transforms and of the upsampling filters.</summary>
    public JpegXlTransformData TransformData { get; private init; } = JpegXlTransformData.Default;

    /// <summary>Gets the number of color channels: 1 for a grayscale image, otherwise 3.</summary>
    public int ColorChannelCount => ColorEncoding.IsGray ? 1 : 3;

    /// <summary>Gets the index of the first alpha channel among the extra channels, or -1.</summary>
    public int AlphaChannelIndex
    {
        get
        {
            for (var i = 0; i < ExtraChannels.Length; i++)
            {
                if (ExtraChannels[i].Type == JpegXlExtraChannelInfo.TypeAlpha)
                    return i;
            }

            return -1;
        }
    }

    /// <summary>
    /// Reads the signature and the headers. The reader may overrun (<see cref="JpegXlBitReader.IsOverrun"/>): the result is
    /// then meaningless, and an exception that this method throws may be a consequence of the missing bits.
    /// </summary>
    /// <param name="reader">The reader, at the first byte of the codestream.</param>
    /// <returns>The headers; the reader is after the last bit of the <c>CustomTransformData</c> bundle.</returns>
    public static JpegXlImageHeader Read(JpegXlBitReader reader)
    {
        if (reader.ReadBits(16) != 0x0AFF && !reader.IsOverrun)
            throw JpegXlFormat.Invalid("the codestream does not start with its signature.");

        var (width, height) = ReadSize(reader);
        if (reader.ReadBool())
        {
            return new JpegXlImageHeader
            {
                Width = width,
                Height = height,
                TransformData = JpegXlTransformData.Read(reader, xybEncoded: true),
            };
        }

        var orientation = 1;
        (uint Width, uint Height)? intrinsicSize = null;
        (uint Width, uint Height)? previewSize = null;
        var hasAnimation = false;
        uint ticksNumerator = 0;
        uint ticksDenominator = 0;
        uint loopCount = 0;
        var hasTimecodes = false;
        var extraFields = reader.ReadBool();
        if (extraFields)
        {
            orientation = (int)reader.ReadBits(3) + 1;
            if (reader.ReadBool())
            {
                intrinsicSize = ReadSize(reader);
            }

            if (reader.ReadBool())
            {
                previewSize = ReadPreviewSize(reader);
            }

            hasAnimation = reader.ReadBool();
            if (hasAnimation)
            {
                ticksNumerator = reader.ReadU32(100, 0, 1000, 0, 1, 10, 1, 30);
                ticksDenominator = reader.ReadU32(1, 0, 1001, 0, 1, 8, 1, 10);
                loopCount = reader.ReadU32(0, 0, 0, 3, 0, 16, 0, 32);
                hasTimecodes = reader.ReadBool();
            }
        }

        var bitDepth = JpegXlBitDepth.Read(reader);
        var modular16BitBuffers = reader.ReadBool();
        var extraChannelCount = (int)reader.ReadU32(0, 0, 1, 0, 2, 4, 1, 12);
        var extraChannels = new JpegXlExtraChannelInfo[extraChannelCount];
        for (var i = 0; i < extraChannels.Length; i++)
        {
            extraChannels[i] = JpegXlExtraChannelInfo.Read(reader);
        }

        var xybEncoded = reader.ReadBool();
        var colorEncoding = JpegXlColorEncoding.Read(reader);
        var intensityTarget = 255f;
        if (extraFields && !reader.ReadBool())
        {
            // ToneMapping: only the intensity target takes part in decoding
            intensityTarget = reader.ReadF16();
            var minNits = reader.ReadF16();
            var relativeToMaxDisplay = reader.ReadBool();
            var linearBelow = reader.ReadF16();
            if (!reader.IsOverrun && (!(intensityTarget > 0) || minNits < 0 || minNits > intensityTarget || linearBelow < 0 || (relativeToMaxDisplay && linearBelow > 1)))
                throw JpegXlFormat.Invalid("the tone mapping fields are out of range.");
        }

        SkipExtensions(reader);
        return new JpegXlImageHeader
        {
            Width = width,
            Height = height,
            Orientation = orientation,
            IntrinsicSize = intrinsicSize,
            PreviewSize = previewSize,
            HasAnimation = hasAnimation,
            TicksPerSecondNumerator = ticksNumerator,
            TicksPerSecondDenominator = ticksDenominator,
            LoopCount = loopCount,
            HasTimecodes = hasTimecodes,
            BitDepth = bitDepth,
            Modular16BitBuffers = modular16BitBuffers,
            ExtraChannels = extraChannels,
            IsXybEncoded = xybEncoded,
            ColorEncoding = colorEncoding,
            IntensityTarget = intensityTarget,
            TransformData = JpegXlTransformData.Read(reader, xybEncoded),
        };
    }

    /// <summary>Reads a name: a length followed by that many bytes of UTF-8 text.</summary>
    public static byte[] ReadName(JpegXlBitReader reader)
    {
        var length = (int)reader.ReadU32(0, 0, 0, 4, 16, 5, 48, 10);
        if (length == 0)
            return [];

        var name = new byte[length];
        for (var i = 0; i < name.Length; i++)
        {
            name[i] = (byte)reader.ReadBits(8);
        }

        return name;
    }

    /// <summary>Skips the extensions of a bundle: a mask of the extensions present, then the size in bits of each of them, then their bits.</summary>
    public static void SkipExtensions(JpegXlBitReader reader)
    {
        var extensions = reader.ReadU64();
        if (extensions == 0)
            return;

        ulong total = 0;
        for (var i = 0; i < 64; i++)
        {
            if ((extensions & (1UL << i)) == 0)
                continue;

            var bits = reader.ReadU64();
            if (bits > long.MaxValue - total)
            {
                if (reader.IsOverrun)
                    return;

                throw JpegXlFormat.Invalid("the extensions of a header are too large.");
            }

            total += bits;
        }

        // A skip larger than the data makes the reader overrun: the caller sees it
        reader.SkipBits((long)Math.Min(total, (ulong)reader.TotalBits + 64));
    }

    private static (uint Width, uint Height) ReadSize(JpegXlBitReader reader)
    {
        var small = reader.ReadBool();
        var height = small ? (reader.ReadBits(5) + 1) * 8 : reader.ReadU32(1, 9, 1, 13, 1, 18, 1, 30);
        var ratio = reader.ReadBits(3);
        ulong width = ratio switch
        {
            0 => small ? (reader.ReadBits(5) + 1) * 8 : reader.ReadU32(1, 9, 1, 13, 1, 18, 1, 30),
            1 => height,
            2 => (ulong)height * 6 / 5,
            3 => (ulong)height * 4 / 3,
            4 => (ulong)height * 3 / 2,
            5 => (ulong)height * 16 / 9,
            6 => (ulong)height * 5 / 4,
            _ => (ulong)height * 2,
        };

        // A side is at most 2^30 (level 10); the limits of the caller are checked on the parsed header
        if (width > (1u << 30) || height > (1u << 30))
        {
            if (reader.IsOverrun)
                return (1, 1);

            throw JpegXlFormat.Invalid("a side of the image is larger than 2^30.");
        }

        return ((uint)width, height);
    }

    private static (uint Width, uint Height) ReadPreviewSize(JpegXlBitReader reader)
    {
        var small = reader.ReadBool();
        var height = small ? reader.ReadU32(16, 0, 32, 0, 1, 5, 33, 9) * 8 : reader.ReadU32(1, 6, 65, 8, 321, 10, 1345, 12);
        var ratio = reader.ReadBits(3);
        ulong width = ratio switch
        {
            0 => small ? reader.ReadU32(16, 0, 32, 0, 1, 5, 33, 9) * 8 : reader.ReadU32(1, 6, 65, 8, 321, 10, 1345, 12),
            1 => height,
            2 => (ulong)height * 6 / 5,
            3 => (ulong)height * 4 / 3,
            4 => (ulong)height * 3 / 2,
            5 => (ulong)height * 16 / 9,
            6 => (ulong)height * 5 / 4,
            _ => (ulong)height * 2,
        };

        if (width > 4096 || height > 4096)
        {
            if (reader.IsOverrun)
                return (1, 1);

            throw JpegXlFormat.Invalid("a side of the preview is larger than 4096.");
        }

        return ((uint)width, height);
    }
}
