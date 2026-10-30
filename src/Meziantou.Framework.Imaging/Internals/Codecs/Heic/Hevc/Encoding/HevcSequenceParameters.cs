using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What the encoder decides for a whole coded video sequence: the values of the syntax elements of the video parameter set
/// and of the sequence parameter set that are not constant in the streams it writes (Rec. ITU-T H.265, clauses 7.3.2.1,
/// 7.3.2.2 and 7.3.3), with what Annex A derives from them: the profile, the tier and the level.
/// </summary>
/// <param name="Width">The width of the pictures that were asked for, in luma samples.</param>
/// <param name="Height">The height of the pictures that were asked for, in luma samples.</param>
/// <param name="BitDepth">The bit depth of the luma and chroma samples: 8, 10 or 12.</param>
/// <param name="ChromaFormatIdc"><c>chroma_format_idc</c>: 0 for monochrome, 1 for 4:2:0, 2 for 4:2:2, 3 for 4:4:4.</param>
/// <param name="ColourPrimaries">The <c>colour_primaries</c> code point.</param>
/// <param name="TransferCharacteristics">The <c>transfer_characteristics</c> code point.</param>
/// <param name="MatrixCoeffs">The <c>matrix_coeffs</c> code point.</param>
/// <param name="FullRange">The value of <c>video_full_range_flag</c>.</param>
/// <param name="ChromaSampleLocType">The location of the chroma samples of 4:2:0 pictures: 0 to 5.</param>
/// <param name="StillPicture">Whether the bitstream is a single picture.</param>
/// <param name="IntraOnly">Whether every picture is an instantaneous decoding refresh picture: no picture refers to another one.</param>
/// <param name="Tools">The coding tools that are enabled.</param>
/// <param name="MinCbLog2SizeY">The base 2 logarithm of the minimum coding block size: 3 to 6.</param>
/// <param name="CtbLog2SizeY">The base 2 logarithm of the coding tree block size: 4 to 6.</param>
/// <param name="MinTbLog2SizeY">The base 2 logarithm of the minimum transform block size: 2 to 5, less than <paramref name="MinCbLog2SizeY"/>.</param>
/// <param name="MaxTbLog2SizeY">The base 2 logarithm of the maximum transform block size: at most 5 and <paramref name="CtbLog2SizeY"/>.</param>
/// <param name="MaxTransformHierarchyDepthInter"><c>max_transform_hierarchy_depth_inter</c>.</param>
/// <param name="MaxTransformHierarchyDepthIntra"><c>max_transform_hierarchy_depth_intra</c>.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct HevcSequenceParameters(
    int Width,
    int Height,
    int BitDepth,
    int ChromaFormatIdc,
    int ColourPrimaries,
    int TransferCharacteristics,
    int MatrixCoeffs,
    bool FullRange,
    int ChromaSampleLocType,
    bool StillPicture,
    bool IntraOnly,
    HevcCodingTools Tools,
    int MinCbLog2SizeY,
    int CtbLog2SizeY,
    int MinTbLog2SizeY,
    int MaxTbLog2SizeY,
    int MaxTransformHierarchyDepthInter,
    int MaxTransformHierarchyDepthIntra)
{
    /// <summary>
    /// <c>log2_max_pic_order_cnt_lsb_minus4 + 4</c>: the pictures follow each other in output order, so that the picture
    /// order count of a picture is the one of the picture before it plus one, whatever the number of bits.
    /// </summary>
    public const int Log2MaxPicOrderCntLsb = 8;

    /// <summary><c>sps_max_num_reorder_pics</c>: the pictures are output in the order they are decoded.</summary>
    public const int MaxNumReorderPics = 0;

    // Bits of general_progressive_source_flag to general_inbld_flag, the first one being the bit 47 (clause 7.3.3)
    private const int ProgressiveSourceBit = 47;
    private const int NonPackedConstraintBit = 45;
    private const int FrameOnlyConstraintBit = 44;
    private const int Max12BitConstraintBit = 43;
    private const int Max10BitConstraintBit = 42;
    private const int Max8BitConstraintBit = 41;
    private const int Max422ChromaConstraintBit = 40;
    private const int Max420ChromaConstraintBit = 39;
    private const int MaxMonochromeConstraintBit = 38;
    private const int IntraConstraintBit = 37;
    private const int OnePictureOnlyConstraintBit = 36;
    private const int LowerBitRateConstraintBit = 35;

    /// <summary>
    /// Gets the bit depth of the PCM samples, <c>PcmBitDepthY</c> and <c>PcmBitDepthC</c>, when
    /// <see cref="HevcCodingTools.Pcm"/> is enabled: 0, the default, for the bit depth of the samples.
    /// </summary>
    public int PcmBitDepth { get; init; }

    /// <summary>Gets <c>SubWidthC</c> (Table 6-1): 1 for a monochrome picture.</summary>
    public int SubWidthC => ChromaFormatIdc is 1 or 2 ? 2 : 1;

    /// <summary>Gets <c>SubHeightC</c> (Table 6-1): 1 for a monochrome picture.</summary>
    public int SubHeightC => ChromaFormatIdc is 1 ? 2 : 1;

    /// <summary>
    /// Gets <c>pic_width_in_luma_samples</c>: <see cref="Width"/> rounded up to a multiple of the minimum coding block
    /// size (clause 7.4.3.2.1).
    /// </summary>
    public int PicWidthInLumaSamples => ((Width + (1 << MinCbLog2SizeY) - 1) >> MinCbLog2SizeY) << MinCbLog2SizeY;

    /// <summary>Gets <c>pic_height_in_luma_samples</c>: <see cref="Height"/> rounded up to a multiple of the minimum coding block size.</summary>
    public int PicHeightInLumaSamples => ((Height + (1 << MinCbLog2SizeY) - 1) >> MinCbLog2SizeY) << MinCbLog2SizeY;

    /// <summary>
    /// Gets <c>conf_win_right_offset</c>. The offsets of the conformance cropping window are in units of
    /// <see cref="SubWidthC"/> luma samples: the window cannot remove an odd number of columns of a picture whose chroma
    /// is subsampled, and <see cref="OutputWidth"/> is then one more than <see cref="Width"/>.
    /// </summary>
    public int ConfWinRightOffset => (PicWidthInLumaSamples - Width) / SubWidthC;

    /// <summary>Gets <c>conf_win_bottom_offset</c>, in units of <see cref="SubHeightC"/> luma samples.</summary>
    public int ConfWinBottomOffset => (PicHeightInLumaSamples - Height) / SubHeightC;

    /// <summary>Gets <c>conformance_window_flag</c>.</summary>
    public bool ConformanceWindowFlag => ConfWinRightOffset is not 0 || ConfWinBottomOffset is not 0;

    /// <summary>
    /// Gets the width of the pictures that a decoder outputs, the conformance cropping window: <see cref="Width"/>, or
    /// one more when the width of a picture with subsampled chroma is odd. The container describes the difference (a
    /// clean aperture).
    /// </summary>
    public int OutputWidth => PicWidthInLumaSamples - (SubWidthC * ConfWinRightOffset);

    /// <summary>Gets the height of the pictures that a decoder outputs: <see cref="Height"/>, or one more.</summary>
    public int OutputHeight => PicHeightInLumaSamples - (SubHeightC * ConfWinBottomOffset);

    /// <summary>Gets <c>PcmBitDepthY</c> and <c>PcmBitDepthC</c>.</summary>
    public int EffectivePcmBitDepth => PcmBitDepth is 0 ? BitDepth : PcmBitDepth;

    /// <summary>
    /// Gets <c>Log2MinIpcmCbSizeY</c>: the smallest value of its range, <c>Min( MinCbLog2SizeY, 5 )</c> (clause
    /// 7.4.3.2.1), so that a coding unit of any size up to <see cref="Log2MaxIpcmCbSizeY"/> can be a PCM coding unit.
    /// </summary>
    public int Log2MinIpcmCbSizeY => Math.Min(MinCbLog2SizeY, 5);

    /// <summary>Gets <c>Log2MaxIpcmCbSizeY</c>: the largest value of its range, <c>Min( CtbLog2SizeY, 5 )</c>.</summary>
    public int Log2MaxIpcmCbSizeY => Math.Min(CtbLog2SizeY, 5);

    /// <summary>
    /// Gets <c>sps_max_dec_pic_buffering_minus1</c>: a picture that refers to the picture before it needs both in the
    /// decoded picture buffer; a picture that refers to none is alone in it. The still picture profiles require 0.
    /// </summary>
    public int MaxDecPicBufferingMinus1 => StillPicture || IntraOnly ? 0 : 1;


    /// <summary>
    /// Gets <c>num_short_term_ref_pic_sets</c>: one candidate set, the picture that precedes the current one, for the
    /// sequences that have pictures predicted from another one.
    /// </summary>
    public int NumShortTermRefPicSets => StillPicture || IntraOnly ? 0 : 1;

    /// <summary>Gets whether the sequence parameter set has a range extension: one of its flags is set.</summary>
    public bool HasRangeExtension => (Tools & (HevcCodingTools.ExtendedTools & ~HevcCodingTools.LargeTransformSkipBlocks)) is not HevcCodingTools.None;

    /// <summary>
    /// Gets the profile that the sequence announces (clause A.3): the profile with the smallest capabilities that holds the
    /// sample format, the tools and the kind of pictures of the sequence, among those of <see cref="HevcProfile"/>.
    /// </summary>
    public HevcProfile Profile
    {
        get
        {
            // The still picture profiles hold one picture; the intra profiles hold intra random access point pictures
            // only, output in decoding order (clause A.3.5), which is what a sequence of key frames is
            var intra = StillPicture || IntraOnly;
            if ((Tools & HevcCodingTools.ExtendedTools) is HevcCodingTools.None)
            {
                switch (ChromaFormatIdc)
                {
                    case 0:
                        // Table A.1: the Monochrome profiles have chroma_format_idc equal to 0. They have no intra or
                        // still picture variant
                        return BitDepth switch
                        {
                            8 => HevcProfile.Monochrome,
                            10 => HevcProfile.Monochrome10,
                            _ => HevcProfile.Monochrome12,
                        };

                    case 1:
                        // Clauses A.3.2 to A.3.4: the profiles of the first version hold 4:2:0 samples of 8 bits (Main,
                        // Main Still Picture) and of at most 10 bits (Main 10, Main 10 Still Picture)
                        return BitDepth switch
                        {
                            8 => StillPicture ? HevcProfile.MainStillPicture : HevcProfile.Main,
                            10 => StillPicture ? HevcProfile.Main10StillPicture : HevcProfile.Main10,
                            _ => intra ? HevcProfile.Main12Intra : HevcProfile.Main12,
                        };

                    case 2:
                        // Table A.1: no 4:2:2 profile is limited to 8 bits
                        return BitDepth is 12
                            ? (intra ? HevcProfile.Main42212Intra : HevcProfile.Main42212)
                            : (intra ? HevcProfile.Main42210Intra : HevcProfile.Main42210);
                }
            }

            // Table A.1: the 4:4:4 profiles do not constrain chroma_format_idc, and they are the only ones that do not
            // require the flags of the range extensions to be 0. Only the 8-bit one has a still picture variant
            return BitDepth switch
            {
                8 => StillPicture ? HevcProfile.Main444StillPicture : intra ? HevcProfile.Main444Intra : HevcProfile.Main444,
                10 => intra ? HevcProfile.Main44410Intra : HevcProfile.Main44410,
                _ => intra ? HevcProfile.Main44412Intra : HevcProfile.Main44412,
            };
        }
    }

    /// <summary>
    /// Gets <c>general_profile_idc</c>: 1 for Main, 2 for Main 10, 3 for Main Still Picture, 4 for the format range
    /// extensions profiles.
    /// </summary>
    public int GeneralProfileIdc => Profile switch
    {
        HevcProfile.Main => 1,
        HevcProfile.Main10 or HevcProfile.Main10StillPicture => 2,
        HevcProfile.MainStillPicture => 3,
        _ => 4,
    };

    /// <summary>
    /// Gets <c>general_profile_compatibility_flag[ j ]</c>, the flag <c>j</c> being the bit <c>31 - j</c>: the flag of
    /// <see cref="GeneralProfileIdc"/> (clause 7.4.4), and the flags of the profiles of the first version that hold the
    /// bitstreams of the profile.
    /// </summary>
    public uint GeneralProfileCompatibilityFlags => Profile switch
    {
        // Clause A.3.2, note: a bitstream of the Main profile is one of the Main 10 profile
        HevcProfile.Main => CompatibilityFlag(1) | CompatibilityFlag(2),
        HevcProfile.Main10 or HevcProfile.Main10StillPicture => CompatibilityFlag(2),

        // Clause A.3.4, note: a bitstream of the Main Still Picture profile is one of the Main and Main 10 profiles
        HevcProfile.MainStillPicture => CompatibilityFlag(1) | CompatibilityFlag(2) | CompatibilityFlag(3),
        _ => CompatibilityFlag(4),
    };

    /// <summary>
    /// Gets the 48 bits that follow the compatibility flags, from <c>general_progressive_source_flag</c> (the bit 47) to
    /// <c>general_inbld_flag</c> (the bit 0).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pictures are frames of a progressive source (<c>general_progressive_source_flag</c> 1,
    /// <c>general_interlaced_source_flag</c> 0, <c>general_frame_only_constraint_flag</c> 1) and the encoder writes no
    /// frame packing or projection message (<c>general_non_packed_constraint_flag</c> 1).
    /// </para>
    /// <para>
    /// A format range extensions profile is one row of Table A.2: every other combination of its nine flags is reserved.
    /// <c>general_lower_bit_rate_constraint_flag</c> is 1 for the intra and still picture profiles too, which allow both
    /// values: a decoder of a profile that is not an intra profile is only required to decode the bitstreams that have it
    /// (clause A.3.5, each flag being at least the one of the row of the decoder).
    /// </para>
    /// <para>
    /// The Main 10 Still Picture profile is the Main 10 profile with <c>general_one_picture_only_constraint_flag</c>
    /// (clause A.3.3). The flag stays 0 for the Main Still Picture profile, which <c>general_profile_idc</c> identifies.
    /// </para>
    /// </remarks>
    public ulong GeneralConstraintIndicatorFlags
    {
        get
        {
            var source = Bit(ProgressiveSourceBit) | Bit(NonPackedConstraintBit) | Bit(FrameOnlyConstraintBit);
            return source | Profile switch
            {
                HevcProfile.Main or HevcProfile.Main10 or HevcProfile.MainStillPicture => 0,
                HevcProfile.Main10StillPicture => Bit(OnePictureOnlyConstraintBit),

                // Table A.2: max 12, 10 and 8 bits, max 4:2:2, 4:2:0 and monochrome, intra, one picture only
                HevcProfile.Monochrome => RangeExtensionFlags(true, true, true, true, true, true, intra: false, onePictureOnly: false),
                HevcProfile.Monochrome10 => RangeExtensionFlags(true, true, false, true, true, true, intra: false, onePictureOnly: false),
                HevcProfile.Monochrome12 => RangeExtensionFlags(true, false, false, true, true, true, intra: false, onePictureOnly: false),
                HevcProfile.Main12 => RangeExtensionFlags(true, false, false, true, true, false, intra: false, onePictureOnly: false),
                HevcProfile.Main42210 => RangeExtensionFlags(true, true, false, true, false, false, intra: false, onePictureOnly: false),
                HevcProfile.Main42212 => RangeExtensionFlags(true, false, false, true, false, false, intra: false, onePictureOnly: false),
                HevcProfile.Main444 => RangeExtensionFlags(true, true, true, false, false, false, intra: false, onePictureOnly: false),
                HevcProfile.Main44410 => RangeExtensionFlags(true, true, false, false, false, false, intra: false, onePictureOnly: false),
                HevcProfile.Main44412 => RangeExtensionFlags(true, false, false, false, false, false, intra: false, onePictureOnly: false),
                HevcProfile.Main12Intra => RangeExtensionFlags(true, false, false, true, true, false, intra: true, onePictureOnly: false),
                HevcProfile.Main42210Intra => RangeExtensionFlags(true, true, false, true, false, false, intra: true, onePictureOnly: false),
                HevcProfile.Main42212Intra => RangeExtensionFlags(true, false, false, true, false, false, intra: true, onePictureOnly: false),
                HevcProfile.Main444Intra => RangeExtensionFlags(true, true, true, false, false, false, intra: true, onePictureOnly: false),
                HevcProfile.Main44410Intra => RangeExtensionFlags(true, true, false, false, false, false, intra: true, onePictureOnly: false),
                HevcProfile.Main44412Intra => RangeExtensionFlags(true, false, false, false, false, false, intra: true, onePictureOnly: false),
                _ => RangeExtensionFlags(true, true, true, false, false, false, intra: true, onePictureOnly: true),
            };
        }
    }

    /// <summary>
    /// Gets the number of bytes of the largest coded picture of the bitstream: the sum of <c>NumBytesInNalUnit</c> of the
    /// NAL units that a decoder is given for it. 0, the default, before any picture is coded: the level then only holds
    /// the size of the pictures.
    /// </summary>
    public long MaxPictureLength { get; init; }

    /// <summary>
    /// Gets <c>general_level_idc</c>: 30 times the number of the lowest level of Tables A.8 and A.9 that holds the pictures
    /// and their coded sizes (<see cref="HevcLevels.GetLevel"/>), or 255 (level 8.5) when no level does.
    /// </summary>
    public int GeneralLevelIdc => HevcLevels.GetLevel(Profile, PicWidthInLumaSamples, PicHeightInLumaSamples, MaxPictureLength).LevelIdc;

    /// <summary>
    /// Gets <c>general_tier_flag</c>: the Main tier, the High tier when the coded pictures are too large for every level of
    /// the Main tier, and 1 with level 8.5 (clause A.4.1).
    /// </summary>
    public bool GeneralTierFlag => HevcLevels.GetLevel(Profile, PicWidthInLumaSamples, PicHeightInLumaSamples, MaxPictureLength).TierFlag;

    /// <summary>
    /// Gets whether the values are those of a bitstream that the encoder can write and that conforms to the profile and
    /// the level it announces.
    /// </summary>
    public bool IsValid
    {
        get
        {
            if (Width <= 0 || Height <= 0 || BitDepth is not (8 or 10 or 12) || ChromaFormatIdc is < 0 or > 3 || ChromaSampleLocType is < 0 or > 5)
                return false;

            // Annex A: every profile limits the coding tree blocks to 16, 32 and 64 samples; clause 7.4.3.2.1 for the others
            if (CtbLog2SizeY is < 4 or > 6 || MinCbLog2SizeY < 3 || MinCbLog2SizeY > CtbLog2SizeY)
                return false;

            if (MinTbLog2SizeY < 2 || MinTbLog2SizeY >= MinCbLog2SizeY || MaxTbLog2SizeY < MinTbLog2SizeY || MaxTbLog2SizeY > Math.Min(CtbLog2SizeY, 5))
                return false;

            var maxDepth = CtbLog2SizeY - MinTbLog2SizeY;
            if (MaxTransformHierarchyDepthInter < 0 || MaxTransformHierarchyDepthInter > maxDepth || MaxTransformHierarchyDepthIntra < 0 || MaxTransformHierarchyDepthIntra > maxDepth)
                return false;

            if (PicWidthInLumaSamples > HevcSequenceParameterSet.MaxPictureDimension || PicHeightInLumaSamples > HevcSequenceParameterSet.MaxPictureDimension)
                return false;

            // Clause A.4.1, item d
            if (HevcLevels.RequiresLargeCodingTreeBlocks(GeneralLevelIdc) && CtbLog2SizeY < 5)
                return false;

            // A minimum coding block of 64 samples leaves no size for a PCM coding unit, which has at most 32 samples
            if ((Tools & HevcCodingTools.Pcm) is not HevcCodingTools.None && (MinCbLog2SizeY > 5 || EffectivePcmBitDepth < 1 || EffectivePcmBitDepth > BitDepth))
                return false;

            return !StillPicture || IntraOnly;
        }
    }

    /// <summary>
    /// Creates the parameters of the sequence that <paramref name="settings"/> describe, with the coding tools
    /// <paramref name="tools"/> and a coding structure that every level allows: coding tree blocks of 32 samples, coding
    /// blocks of 8 to 32 samples and transform blocks of 4 to 32 samples.
    /// </summary>
    public static HevcSequenceParameters Create(in HevcEncoderSettings settings, HevcCodingTools tools = HevcCodingTools.None)
    {
        return new HevcSequenceParameters(
            settings.Width,
            settings.Height,
            settings.BitDepth,
            settings.ChromaFormatIdc,
            settings.ColourPrimaries,
            settings.TransferCharacteristics,
            settings.MatrixCoeffs,
            settings.FullRange,
            settings.ChromaSampleLocType,
            settings.StillPicture,
            IntraOnly: settings.StillPicture || settings.KeyFrameInterval is 1,
            tools,
            MinCbLog2SizeY: 3,
            CtbLog2SizeY: 5,
            MinTbLog2SizeY: 2,
            MaxTbLog2SizeY: 5,
            MaxTransformHierarchyDepthInter: 3,
            MaxTransformHierarchyDepthIntra: 3);
    }

    private static uint CompatibilityFlag(int profileIdc) => 1u << (31 - profileIdc);

    private static ulong Bit(int position) => 1UL << position;

    private static ulong RangeExtensionFlags(bool max12Bit, bool max10Bit, bool max8Bit, bool max422Chroma, bool max420Chroma, bool maxMonochrome, bool intra, bool onePictureOnly)
    {
        return (max12Bit ? Bit(Max12BitConstraintBit) : 0)
            | (max10Bit ? Bit(Max10BitConstraintBit) : 0)
            | (max8Bit ? Bit(Max8BitConstraintBit) : 0)
            | (max422Chroma ? Bit(Max422ChromaConstraintBit) : 0)
            | (max420Chroma ? Bit(Max420ChromaConstraintBit) : 0)
            | (maxMonochrome ? Bit(MaxMonochromeConstraintBit) : 0)
            | (intra ? Bit(IntraConstraintBit) : 0)
            | (onePictureOnly ? Bit(OnePictureOnlyConstraintBit) : 0)
            | Bit(LowerBitRateConstraintBit);
    }
}
