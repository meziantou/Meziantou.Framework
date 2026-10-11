namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The <c>FrameHeader</c> bundle (ISO/IEC 18181-1, section C.2), with its <c>Passes</c>, <c>BlendingInfo</c> and
/// <c>RestorationFilter</c> bundles, and the dimensions that derive from it.
/// </summary>
internal sealed class JpegXlFrameHeader
{
    // Values of the frame_type field
    public const int TypeRegular = 0;
    public const int TypeLf = 1;
    public const int TypeReferenceOnly = 2;
    public const int TypeSkipProgressive = 3;

    // Bits of the flags field
    public const ulong FlagNoise = 1;
    public const ulong FlagPatches = 2;
    public const ulong FlagSplines = 16;
    public const ulong FlagUseLfFrame = 32;
    public const ulong FlagSkipAdaptiveLfSmoothing = 128;

    /// <summary>The largest number of passes of a frame.</summary>
    public const int MaxPasses = 11;

    private static readonly int[] DefaultPassShifts = [0];

    private JpegXlFrameHeader()
    {
    }

    /// <summary>Gets the kind of the frame: regular, LF frame, reference only, or regular without progressive rendering.</summary>
    public int Type { get; private init; }

    /// <summary>Gets a value indicating whether the frame is coded with the Modular mode; otherwise with VarDCT.</summary>
    public bool IsModular { get; private init; }

    /// <summary>Gets the flags of the frame.</summary>
    public ulong Flags { get; private init; }

    /// <summary>Gets a value indicating whether the color channels are stored as YCbCr.</summary>
    public bool IsYCbCr { get; private init; }

    /// <summary>Gets the subsampling mode of each of the three channels of a YCbCr frame (0: none, 1: both directions, 2: horizontal, 3: vertical).</summary>
    public (int First, int Second, int Third) ChromaSubsampling { get; private init; }

    /// <summary>Gets the factor by which the color channels are upsampled after decoding (1, 2, 4 or 8).</summary>
    public int Upsampling { get; private init; } = 1;

    /// <summary>Gets the factor by which each extra channel is upsampled after decoding, <c>dim_shift</c> included.</summary>
    public int[] ExtraChannelUpsampling { get; private init; } = [];

    /// <summary>Gets the base-2 logarithm of the side of a group, divided by 128: a group is <c>128 &lt;&lt; shift</c> samples wide.</summary>
    public int GroupSizeShift { get; private init; } = 1;

    /// <summary>Gets the scale of the quantization matrices of the X channel.</summary>
    public int XqmScale { get; private init; } = 3;

    /// <summary>Gets the scale of the quantization matrices of the B channel.</summary>
    public int BqmScale { get; private init; } = 2;

    /// <summary>Gets the number of passes.</summary>
    public int PassCount { get; private init; } = 1;

    /// <summary>Gets the number of bits by which the coefficients of each pass are shifted.</summary>
    public int[] PassShifts { get; private init; } = DefaultPassShifts;

    /// <summary>Gets the base-2 logarithm of each downsampling factor that a pass completes, in decreasing order.</summary>
    public int[] PassDownsampleShifts { get; private init; } = [];

    /// <summary>Gets, for each entry of <see cref="PassDownsampleShifts"/>, the pass that completes it.</summary>
    public int[] PassDownsampleLastPasses { get; private init; } = [];

    /// <summary>Gets the level of an LF frame (1 to 4), or 0.</summary>
    public int LfLevel { get; private init; }

    /// <summary>Gets the horizontal position of the frame on the image.</summary>
    public int X0 { get; private init; }

    /// <summary>Gets the vertical position of the frame on the image.</summary>
    public int Y0 { get; private init; }

    /// <summary>Gets the width of the frame once upsampled: the size it covers on the image (or on the LF image of its level).</summary>
    public uint Width { get; private init; }

    /// <summary>Gets the height of the frame once upsampled.</summary>
    public uint Height { get; private init; }

    /// <summary>Gets a value indicating whether the frame covers the whole image: it has no custom size or origin, or they cover the image.</summary>
    public bool IsFullFrame { get; private init; } = true;

    /// <summary>Gets how the color channels are blended with a reference frame.</summary>
    public JpegXlBlendingInfo Blending { get; private init; }

    /// <summary>Gets how each extra channel is blended with a reference frame.</summary>
    public JpegXlBlendingInfo[] ExtraChannelBlending { get; private init; } = [];

    /// <summary>Gets the duration of the frame, in ticks.</summary>
    public uint Duration { get; private init; }

    /// <summary>Gets a value indicating whether the frame is the last one of the codestream.</summary>
    public bool IsLast { get; private init; } = true;

    /// <summary>Gets the reference slot the frame is saved to (0 to 3).</summary>
    public int SaveAsReference { get; private init; }

    /// <summary>Gets a value indicating whether the frame is saved before the color transforms and the image features are applied.</summary>
    public bool SaveBeforeColorTransform { get; private init; }

    /// <summary>Gets the UTF-8 name of the frame.</summary>
    public byte[] Name { get; private init; } = [];

    /// <summary>Gets the parameters of the restoration filters.</summary>
    public JpegXlRestorationFilter RestorationFilter { get; private init; } = JpegXlRestorationFilter.Default;

    /// <summary>Gets a value indicating whether the frame is an image the viewer shows: a regular frame that ends the codestream or that has a duration.</summary>
    public bool IsRegular => Type is TypeRegular or TypeSkipProgressive;

    /// <summary>Gets the width of the frame as it is coded: before upsampling.</summary>
    public uint CodedWidth => DivideCeiling(Width, (uint)Upsampling);

    /// <summary>Gets the height of the frame as it is coded: before upsampling.</summary>
    public uint CodedHeight => DivideCeiling(Height, (uint)Upsampling);

    /// <summary>Gets the side of a group, in samples.</summary>
    public uint GroupDimension => 128u << GroupSizeShift;

    /// <summary>Gets the number of group columns.</summary>
    public uint GroupColumns => DivideCeiling(CodedWidth, GroupDimension);

    /// <summary>Gets the number of group rows.</summary>
    public uint GroupRows => DivideCeiling(CodedHeight, GroupDimension);

    /// <summary>Gets the number of groups.</summary>
    public ulong GroupCount => (ulong)GroupColumns * GroupRows;

    /// <summary>Gets the number of LF group columns: an LF group covers 8 by 8 groups.</summary>
    public uint LfGroupColumns => DivideCeiling(CodedWidth, GroupDimension * 8);

    /// <summary>Gets the number of LF group rows.</summary>
    public uint LfGroupRows => DivideCeiling(CodedHeight, GroupDimension * 8);

    /// <summary>Gets the number of LF groups.</summary>
    public ulong LfGroupCount => (ulong)LfGroupColumns * LfGroupRows;

    /// <summary>
    /// Gets the number of sections of the frame, which is the number of entries of its table of contents: a single one for a
    /// frame of one group and one pass, otherwise <c>LfGlobal</c>, the LF groups, <c>HfGlobal</c> and the groups of each pass.
    /// </summary>
    public ulong SectionCount => GroupCount == 1 && PassCount == 1 ? 1 : 2 + LfGroupCount + (GroupCount * (ulong)PassCount);

    /// <summary>
    /// Reads a frame header. The reader may overrun (<see cref="JpegXlBitReader.IsOverrun"/>): the result is then
    /// meaningless, and an exception that this method throws may be a consequence of the missing bits.
    /// </summary>
    /// <param name="reader">The reader, at the first bit of the header (a byte boundary).</param>
    /// <param name="image">The image header.</param>
    /// <param name="isPreview">Whether the frame is the preview frame, whose default size is the size of the preview.</param>
    /// <returns>The frame header; the reader is after its last bit.</returns>
    public static JpegXlFrameHeader Read(JpegXlBitReader reader, JpegXlImageHeader image, bool isPreview)
    {
        var extraChannelCount = image.ExtraChannels.Length;
        var (defaultWidth, defaultHeight) = isPreview ? image.PreviewSize!.Value : (image.Width, image.Height);
        if (reader.ReadBool())
        {
            var defaultUpsampling = new int[extraChannelCount];
            Array.Fill(defaultUpsampling, 1);
            return new JpegXlFrameHeader
            {
                Width = defaultWidth,
                Height = defaultHeight,
                ExtraChannelUpsampling = defaultUpsampling,
                ExtraChannelBlending = new JpegXlBlendingInfo[extraChannelCount],
                XqmScale = image.IsXybEncoded ? 3 : 2,
            };
        }

        var type = (int)reader.ReadBits(2);
        if (isPreview && type != TypeRegular)
            throw JpegXlFormat.Invalid("the preview is not a regular frame.");

        var isModular = reader.ReadBool();
        var flags = reader.ReadU64();
        var useLfFrame = (flags & FlagUseLfFrame) != 0;
        var isYCbCr = !image.IsXybEncoded && reader.ReadBool();
        (int First, int Second, int Third) subsampling = default;
        var upsampling = 1;
        var extraChannelUpsampling = new int[extraChannelCount];
        Array.Fill(extraChannelUpsampling, 1);
        if (!useLfFrame)
        {
            if (isYCbCr)
            {
                subsampling = ((int)reader.ReadBits(2), (int)reader.ReadBits(2), (int)reader.ReadBits(2));
            }

            upsampling = 1 << (int)reader.ReadBits(2);
            for (var i = 0; i < extraChannelUpsampling.Length; i++)
            {
                var value = (1 << (int)reader.ReadBits(2)) << image.ExtraChannels[i].DimShift;
                if (value < upsampling)
                    throw JpegXlFormat.Invalid("an extra channel is upsampled less than the color channels.");

                if (value > 8)
                    throw JpegXlFormat.Invalid("an extra channel is upsampled by more than 8.");

                extraChannelUpsampling[i] = value;
            }
        }

        var groupSizeShift = isModular ? (int)reader.ReadBits(2) : 1;
        // The scales of the quantization of X and B: a VarDCT frame that is not stored in XYB uses the neutral value
        var xqmScale = 2;
        var bqmScale = 2;
        if (!isModular && image.IsXybEncoded)
        {
            xqmScale = (int)reader.ReadBits(3);
            bqmScale = (int)reader.ReadBits(3);
        }

        var passCount = 1;
        var passShifts = DefaultPassShifts;
        int[] downsampleShifts = [];
        int[] downsampleLastPasses = [];
        if (type != TypeReferenceOnly)
        {
            (passCount, passShifts, downsampleShifts, downsampleLastPasses) = ReadPasses(reader);
        }

        var lfLevel = type == TypeLf ? (int)reader.ReadBits(2) + 1 : 0;
        var x0 = 0;
        var y0 = 0;
        var width = defaultWidth;
        var height = defaultHeight;
        var hasCustomSize = false;
        var isPartialFrame = false;
        if (type != TypeLf && reader.ReadBool())
        {
            hasCustomSize = true;
            if (type is TypeRegular or TypeSkipProgressive)
            {
                x0 = JpegXlBitReader.UnpackSigned(reader.ReadU32(0, 8, 256, 11, 2304, 14, 18688, 30));
                y0 = JpegXlBitReader.UnpackSigned(reader.ReadU32(0, 8, 256, 11, 2304, 14, 18688, 30));
            }

            width = reader.ReadU32(0, 8, 256, 11, 2304, 14, 18688, 30);
            height = reader.ReadU32(0, 8, 256, 11, 2304, 14, 18688, 30);
            if (width == 0 || height == 0)
                throw JpegXlFormat.Invalid("a frame has no width or no height.");

            if (type is TypeRegular or TypeSkipProgressive)
            {
                isPartialFrame = x0 > 0 || y0 > 0 || width + (long)x0 < defaultWidth || height + (long)y0 < defaultHeight;
            }
        }

        JpegXlBlendingInfo blending = default;
        var extraChannelBlending = new JpegXlBlendingInfo[extraChannelCount];
        uint duration = 0;
        var isLast = false;
        if (type is TypeRegular or TypeSkipProgressive)
        {
            blending = JpegXlBlendingInfo.Read(reader, extraChannelCount, isPartialFrame);
            var replaceAll = blending.Mode == JpegXlBlendingInfo.ModeReplace;
            for (var i = 0; i < extraChannelBlending.Length; i++)
            {
                extraChannelBlending[i] = JpegXlBlendingInfo.Read(reader, extraChannelCount, isPartialFrame);
                replaceAll &= extraChannelBlending[i].Mode == JpegXlBlendingInfo.ModeReplace;
            }

            if (isPreview && (!replaceAll || hasCustomSize))
                throw JpegXlFormat.Invalid("the preview is blended or has a custom size.");

            if (image.HasAnimation)
            {
                duration = reader.ReadU32(0, 0, 1, 0, 0, 8, 0, 32);
                if (image.HasTimecodes)
                {
                    _ = reader.ReadBits(32);
                }
            }

            isLast = reader.ReadBool();
        }

        var saveAsReference = type != TypeLf && !isLast ? (int)reader.ReadBits(2) : 0;
        bool saveBeforeColorTransform;
        if (type == TypeLf)
        {
            saveBeforeColorTransform = true;
        }
        else if (type == TypeReferenceOnly)
        {
            saveBeforeColorTransform = reader.ReadBool();
            if (!saveBeforeColorTransform && (width < image.Width || height < image.Height || x0 != 0 || y0 != 0))
                throw JpegXlFormat.Invalid("a reference frame that is not a source of patches does not cover the image.");
        }
        else
        {
            // A frame that can be referenced: not the last one, and either a layer or a frame saved to a slot other than 0
            var canBeReferenced = !isLast && (duration == 0 || saveAsReference != 0);
            saveBeforeColorTransform = canBeReferenced && blending.Mode == JpegXlBlendingInfo.ModeReplace && !isPartialFrame && reader.ReadBool();
        }

        var name = JpegXlImageHeader.ReadName(reader);
        var restorationFilter = JpegXlRestorationFilter.Read(reader, isModular);
        JpegXlImageHeader.SkipExtensions(reader);

        if (lfLevel != 0)
        {
            // An LF frame of level n is the image downsampled by 8^n
            var divisor = 1u << (3 * lfLevel);
            width = DivideCeiling(width, divisor);
            height = DivideCeiling(height, divisor);
        }

        return new JpegXlFrameHeader
        {
            Type = type,
            IsModular = isModular,
            Flags = flags,
            IsYCbCr = isYCbCr,
            ChromaSubsampling = subsampling,
            Upsampling = upsampling,
            ExtraChannelUpsampling = extraChannelUpsampling,
            GroupSizeShift = groupSizeShift,
            XqmScale = xqmScale,
            BqmScale = bqmScale,
            PassCount = passCount,
            PassShifts = passShifts,
            PassDownsampleShifts = downsampleShifts,
            PassDownsampleLastPasses = downsampleLastPasses,
            LfLevel = lfLevel,
            X0 = x0,
            Y0 = y0,
            Width = width,
            Height = height,
            IsFullFrame = !isPartialFrame,
            Blending = blending,
            ExtraChannelBlending = extraChannelBlending,
            Duration = duration,
            IsLast = isLast,
            SaveAsReference = saveAsReference,
            SaveBeforeColorTransform = saveBeforeColorTransform,
            Name = name,
            RestorationFilter = restorationFilter,
        };
    }

    /// <summary>
    /// Gets the range of channel shifts whose Modular channels are coded in the groups of a pass: a channel that is
    /// subsampled by 8 or more is in the LF groups, and each pass adds the channels of the next finer resolutions.
    /// </summary>
    public (int MinShift, int MaxShift) GetModularShiftBracket(int pass)
    {
        var maxShift = 2;
        var minShift = 3;
        for (var i = 0; ; i++)
        {
            for (var j = 0; j < PassDownsampleShifts.Length; j++)
            {
                if (PassDownsampleLastPasses[j] == i)
                {
                    minShift = PassDownsampleShifts[j];
                }
            }

            if (i == PassCount - 1)
            {
                minShift = 0;
            }

            if (i == pass)
                return (minShift, maxShift);

            maxShift = minShift - 1;
        }
    }

    private static (int Count, int[] Shifts, int[] DownsampleShifts, int[] LastPasses) ReadPasses(JpegXlBitReader reader)
    {
        var count = (int)reader.ReadU32(1, 0, 2, 0, 3, 0, 4, 3);
        if (count == 1)
            return (1, DefaultPassShifts, [], []);

        var downsampleCount = (int)reader.ReadU32(0, 0, 1, 0, 2, 0, 3, 1);
        if (downsampleCount > count)
            throw JpegXlFormat.Invalid("a frame has more downsampling steps than passes.");

        var shifts = new int[count];
        for (var i = 0; i < count - 1; i++)
        {
            shifts[i] = (int)reader.ReadBits(2);
        }

        var downsampleShifts = new int[downsampleCount];
        for (var i = 0; i < downsampleCount; i++)
        {
            downsampleShifts[i] = (int)reader.ReadBits(2);
            if (i > 0 && downsampleShifts[i] >= downsampleShifts[i - 1])
                throw JpegXlFormat.Invalid("the downsampling factors of the passes do not decrease.");
        }

        var lastPasses = new int[downsampleCount];
        for (var i = 0; i < downsampleCount; i++)
        {
            lastPasses[i] = (int)reader.ReadU32(0, 0, 1, 0, 2, 0, 0, 3);
            if ((i > 0 && lastPasses[i] <= lastPasses[i - 1]) || lastPasses[i] >= count)
                throw JpegXlFormat.Invalid("the passes of the downsampling steps are not valid.");
        }

        return (count, shifts, downsampleShifts, lastPasses);
    }

    private static uint DivideCeiling(uint value, uint divisor) => (uint)(((ulong)value + divisor - 1) / divisor);
}
