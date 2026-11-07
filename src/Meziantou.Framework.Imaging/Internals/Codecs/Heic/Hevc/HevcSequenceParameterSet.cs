namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A sequence parameter set (Rec. ITU-T H.265, clauses 7.3.2.2 "Sequence parameter set RBSP syntax" and 7.4.3.2 "Sequence
/// parameter set RBSP semantics"), with the variables that the semantics derive from it. The names are those of the
/// specification.
/// </summary>
internal sealed class HevcSequenceParameterSet
{
    /// <summary>The largest width and height of a picture that the decoder accepts, in luma samples.</summary>
    public const int MaxPictureDimension = 65535;

    private readonly int[] _maxDecPicBufferingMinus1 = new int[7];
    private readonly int[] _maxNumReorderPics = new int[7];
    private readonly uint[] _maxLatencyIncreasePlus1 = new uint[7];
    private HevcShortTermRefPicSet[] _shortTermRefPicSets = [];
    private int[] _ltRefPicPocLsbSps = [];
    private bool[] _usedByCurrPicLtSpsFlag = [];

    private HevcSequenceParameterSet()
    {
    }

    public int VideoParameterSetId { get; private set; }

    public int MaxSubLayersMinus1 { get; private set; }

    public bool TemporalIdNestingFlag { get; private set; }

    public HevcProfileTierLevel ProfileTierLevel { get; private set; } = null!;

    public int SeqParameterSetId { get; private set; }

    /// <summary>Gets <c>chroma_format_idc</c>: 0 for monochrome, 1 for 4:2:0, 2 for 4:2:2, 3 for 4:4:4.</summary>
    public int ChromaFormatIdc { get; private set; }

    /// <summary>
    /// Gets <c>ChromaArrayType</c>: <see cref="ChromaFormatIdc"/>, since the decoder does not support
    /// <c>separate_colour_plane_flag</c> equal to 1.
    /// </summary>
    public int ChromaArrayType => ChromaFormatIdc;

    /// <summary>Gets <c>SubWidthC</c> (Table 6-1): 1 for a monochrome picture.</summary>
    public int SubWidthC => ChromaFormatIdc is 1 or 2 ? 2 : 1;

    /// <summary>Gets <c>SubHeightC</c> (Table 6-1): 1 for a monochrome picture.</summary>
    public int SubHeightC => ChromaFormatIdc is 1 ? 2 : 1;

    /// <summary>Gets the number of sample arrays of a picture: 1 for a monochrome picture, otherwise 3.</summary>
    public int ComponentCount => ChromaFormatIdc is 0 ? 1 : 3;

    public int PicWidthInLumaSamples { get; private set; }

    public int PicHeightInLumaSamples { get; private set; }

    public bool ConformanceWindowFlag { get; private set; }

    /// <summary>Gets <c>conf_win_left_offset</c>, in units of <see cref="SubWidthC"/> luma samples.</summary>
    public int ConfWinLeftOffset { get; private set; }

    public int ConfWinRightOffset { get; private set; }

    /// <summary>Gets <c>conf_win_top_offset</c>, in units of <see cref="SubHeightC"/> luma samples.</summary>
    public int ConfWinTopOffset { get; private set; }

    public int ConfWinBottomOffset { get; private set; }

    /// <summary>Gets the horizontal position of the conformance cropping window in the picture, in luma samples.</summary>
    public int OutputLeft => SubWidthC * ConfWinLeftOffset;

    /// <summary>Gets the vertical position of the conformance cropping window in the picture, in luma samples.</summary>
    public int OutputTop => SubHeightC * ConfWinTopOffset;

    /// <summary>Gets the width of the conformance cropping window, the pictures that are output, in luma samples.</summary>
    public int OutputWidth => PicWidthInLumaSamples - (SubWidthC * (ConfWinLeftOffset + ConfWinRightOffset));

    /// <summary>Gets the height of the conformance cropping window, in luma samples.</summary>
    public int OutputHeight => PicHeightInLumaSamples - (SubHeightC * (ConfWinTopOffset + ConfWinBottomOffset));

    /// <summary>
    /// Gets <c>BitDepthY</c>, which is also <c>BitDepthC</c>: the decoder does not support sample arrays of two bit
    /// depths.
    /// </summary>
    public int BitDepth { get; private set; }

    /// <summary>Gets <c>QpBdOffsetY</c>, which is also <c>QpBdOffsetC</c>: <c>6 * bit_depth_luma_minus8</c>.</summary>
    public int QpBdOffset => 6 * (BitDepth - 8);

    /// <summary>Gets <c>log2_max_pic_order_cnt_lsb_minus4 + 4</c>.</summary>
    public int Log2MaxPicOrderCntLsb { get; private set; }

    /// <summary>Gets <c>sps_max_dec_pic_buffering_minus1[ i ]</c>.</summary>
    public ReadOnlySpan<int> MaxDecPicBufferingMinus1 => _maxDecPicBufferingMinus1;

    /// <summary>Gets <c>sps_max_num_reorder_pics[ i ]</c>.</summary>
    public ReadOnlySpan<int> MaxNumReorderPics => _maxNumReorderPics;

    /// <summary>Gets <c>sps_max_latency_increase_plus1[ i ]</c>.</summary>
    public ReadOnlySpan<uint> MaxLatencyIncreasePlus1 => _maxLatencyIncreasePlus1;

    public int MinCbLog2SizeY { get; private set; }

    public int CtbLog2SizeY { get; private set; }

    public int CtbSizeY => 1 << CtbLog2SizeY;

    public int MinTbLog2SizeY { get; private set; }

    public int MaxTbLog2SizeY { get; private set; }

    public int MaxTransformHierarchyDepthInter { get; private set; }

    public int MaxTransformHierarchyDepthIntra { get; private set; }

    public bool ScalingListEnabledFlag { get; private set; }

    /// <summary>Gets the scaling lists of the parameter set, or <see langword="null"/> when <c>sps_scaling_list_data_present_flag</c> is 0.</summary>
    public HevcScalingList? ScalingList { get; private set; }

    public bool AmpEnabledFlag { get; private set; }

    public bool SampleAdaptiveOffsetEnabledFlag { get; private set; }

    public bool PcmEnabledFlag { get; private set; }

    /// <summary>Gets <c>PcmBitDepthY</c>.</summary>
    public int PcmBitDepthY { get; private set; }

    /// <summary>Gets <c>PcmBitDepthC</c>.</summary>
    public int PcmBitDepthC { get; private set; }

    public int Log2MinIpcmCbSizeY { get; private set; }

    public int Log2MaxIpcmCbSizeY { get; private set; }

    public bool PcmLoopFilterDisabledFlag { get; private set; }

    /// <summary>Gets the candidate short-term reference picture sets: <c>num_short_term_ref_pic_sets</c> sets.</summary>
    public ReadOnlySpan<HevcShortTermRefPicSet> ShortTermRefPicSets => _shortTermRefPicSets;

    public bool LongTermRefPicsPresentFlag { get; private set; }

    /// <summary>Gets <c>lt_ref_pic_poc_lsb_sps[ i ]</c>: <c>num_long_term_ref_pics_sps</c> values.</summary>
    public ReadOnlySpan<int> LtRefPicPocLsbSps => _ltRefPicPocLsbSps;

    /// <summary>Gets <c>used_by_curr_pic_lt_sps_flag[ i ]</c>.</summary>
    public ReadOnlySpan<bool> UsedByCurrPicLtSpsFlag => _usedByCurrPicLtSpsFlag;

    public bool SpsTemporalMvpEnabledFlag { get; private set; }

    public bool StrongIntraSmoothingEnabledFlag { get; private set; }

    /// <summary>Gets the video usability information, or <see langword="null"/> when <c>vui_parameters_present_flag</c> is 0.</summary>
    public HevcVui? Vui { get; private set; }

    // sps_range_extension( )
    public bool TransformSkipRotationEnabledFlag { get; private set; }

    public bool TransformSkipContextEnabledFlag { get; private set; }

    public bool ImplicitRdpcmEnabledFlag { get; private set; }

    public bool ExplicitRdpcmEnabledFlag { get; private set; }

    public bool IntraSmoothingDisabledFlag { get; private set; }

    public bool HighPrecisionOffsetsEnabledFlag { get; private set; }

    public bool PersistentRiceAdaptationEnabledFlag { get; private set; }

    public int PicWidthInCtbsY { get; private set; }

    public int PicHeightInCtbsY { get; private set; }

    public int PicSizeInCtbsY => PicWidthInCtbsY * PicHeightInCtbsY;

    /// <summary>Gets the highest sub-layer of the sequence, <c>HighestTid</c> of a decoder that decodes every sub-layer.</summary>
    public int HighestTid => MaxSubLayersMinus1;

    /// <summary>
    /// Reads a sequence parameter set from its NAL unit: the two bytes of the NAL unit header, then the payload with its
    /// emulation prevention bytes.
    /// </summary>
    /// <param name="scope">The allocation scope that the copy of a payload with emulation prevention bytes is rented from.</param>
    /// <param name="nalUnit">The NAL unit, whose <c>nal_unit_type</c> is <c>SPS_NUT</c>.</param>
    /// <exception cref="InvalidImageContentException">The NAL unit is not a sequence parameter set, or it is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The sequence uses a feature that the decoder does not support.</exception>
    public static HevcSequenceParameterSet Parse(AllocationScope scope, ReadOnlySpan<byte> nalUnit)
    {
        var header = HevcNalUnitHeader.Read(nalUnit);
        if (header.NalUnitType is not HevcConstants.NalSps)
            throw HevcErrors.Invalid("a NAL unit is not a sequence parameter set.");

        using var buffer = new HevcRbspBuffer(scope);
        return ParseRbsp(buffer.Load(nalUnit[HevcNalUnitHeader.Length..]));
    }

    /// <summary>Reads a sequence parameter set from the payload of its NAL unit, without emulation prevention bytes.</summary>
    /// <exception cref="InvalidImageContentException">The payload is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The sequence uses a feature that the decoder does not support.</exception>
    public static HevcSequenceParameterSet ParseRbsp(ReadOnlySpan<byte> rbsp)
    {
        var reader = new HevcBitReader(rbsp);
        var sps = new HevcSequenceParameterSet();
        sps.VideoParameterSetId = (int)reader.ReadBits(4);
        sps.MaxSubLayersMinus1 = (int)reader.ReadBits(3);
        if (sps.MaxSubLayersMinus1 > 6)
            throw HevcErrors.Invalid("sps_max_sub_layers_minus1 is out of range.");

        sps.TemporalIdNestingFlag = reader.ReadFlag();
        sps.ProfileTierLevel = HevcProfileTierLevel.Parse(ref reader, profilePresentFlag: true, sps.MaxSubLayersMinus1);
        sps.SeqParameterSetId = reader.ReadUE(HevcConstants.MaxSpsCount - 1, "sps_seq_parameter_set_id");
        sps.ChromaFormatIdc = reader.ReadUE(3, "chroma_format_idc");
        if (sps.ChromaFormatIdc is 3 && reader.ReadFlag())
            throw HevcErrors.Unsupported("separate colour planes");

        var width = reader.ReadUE();
        var height = reader.ReadUE();
        if (width is 0 || height is 0)
            throw HevcErrors.Invalid("the size of the pictures is zero.");

        if (width > MaxPictureDimension || height > MaxPictureDimension)
            throw HevcErrors.Unsupported("pictures of more than 65535 samples in a dimension");

        sps.PicWidthInLumaSamples = (int)width;
        sps.PicHeightInLumaSamples = (int)height;
        sps.ConformanceWindowFlag = reader.ReadFlag();
        if (sps.ConformanceWindowFlag)
        {
            sps.ConfWinLeftOffset = reader.ReadUE(MaxPictureDimension, "conf_win_left_offset");
            sps.ConfWinRightOffset = reader.ReadUE(MaxPictureDimension, "conf_win_right_offset");
            sps.ConfWinTopOffset = reader.ReadUE(MaxPictureDimension, "conf_win_top_offset");
            sps.ConfWinBottomOffset = reader.ReadUE(MaxPictureDimension, "conf_win_bottom_offset");

            // Clause 7.4.3.2.1: the window has at least one sample
            if (sps.OutputWidth <= 0 || sps.OutputHeight <= 0)
                throw HevcErrors.Invalid("the conformance cropping window is empty.");
        }

        var bitDepthLumaMinus8 = reader.ReadUE(8, "bit_depth_luma_minus8");
        var bitDepthChromaMinus8 = reader.ReadUE(8, "bit_depth_chroma_minus8");
        if (bitDepthLumaMinus8 + 8 > HevcConstants.MaxBitDepth || bitDepthChromaMinus8 + 8 > HevcConstants.MaxBitDepth)
            throw HevcErrors.Unsupported("a bit depth of more than 12 bits");

        if (sps.ChromaFormatIdc is not 0 && bitDepthLumaMinus8 != bitDepthChromaMinus8)
            throw HevcErrors.Unsupported("luma and chroma samples of different bit depths");

        sps.BitDepth = bitDepthLumaMinus8 + 8;
        sps.Log2MaxPicOrderCntLsb = reader.ReadUE(12, "log2_max_pic_order_cnt_lsb_minus4") + 4;
        var subLayerOrderingInfoPresentFlag = reader.ReadFlag();
        for (var i = subLayerOrderingInfoPresentFlag ? 0 : sps.MaxSubLayersMinus1; i <= sps.MaxSubLayersMinus1; i++)
        {
            sps._maxDecPicBufferingMinus1[i] = reader.ReadUE(HevcConstants.MaxDpbSize - 1, "sps_max_dec_pic_buffering_minus1");
            sps._maxNumReorderPics[i] = reader.ReadUE(sps._maxDecPicBufferingMinus1[i], "sps_max_num_reorder_pics");
            sps._maxLatencyIncreasePlus1[i] = reader.ReadUE();
        }

        if (!subLayerOrderingInfoPresentFlag)
        {
            // The values of the highest sub-layer apply to every sub-layer
            for (var i = 0; i < sps.MaxSubLayersMinus1; i++)
            {
                sps._maxDecPicBufferingMinus1[i] = sps._maxDecPicBufferingMinus1[sps.MaxSubLayersMinus1];
                sps._maxNumReorderPics[i] = sps._maxNumReorderPics[sps.MaxSubLayersMinus1];
                sps._maxLatencyIncreasePlus1[i] = sps._maxLatencyIncreasePlus1[sps.MaxSubLayersMinus1];
            }
        }

        // Equations 7-10 to 7-22
        sps.MinCbLog2SizeY = reader.ReadUE(3, "log2_min_luma_coding_block_size_minus3") + 3;
        sps.CtbLog2SizeY = sps.MinCbLog2SizeY + reader.ReadUE(3, "log2_diff_max_min_luma_coding_block_size");

        // Annex A: every profile limits the coding tree blocks to 16x16, 32x32 and 64x64 samples
        if (sps.CtbLog2SizeY is < 4 or > 6)
            throw HevcErrors.Unsupported("coding tree blocks of " + (1 << sps.CtbLog2SizeY).ToString(System.Globalization.CultureInfo.InvariantCulture) + " samples");

        var minCbSizeY = 1 << sps.MinCbLog2SizeY;
        if (sps.PicWidthInLumaSamples % minCbSizeY is not 0 || sps.PicHeightInLumaSamples % minCbSizeY is not 0)
            throw HevcErrors.Invalid("the size of the pictures is not a multiple of the minimum coding block size.");

        sps.MinTbLog2SizeY = reader.ReadUE(3, "log2_min_luma_transform_block_size_minus2") + 2;
        if (sps.MinTbLog2SizeY >= sps.MinCbLog2SizeY)
            throw HevcErrors.Invalid("the minimum transform block size is not less than the minimum coding block size.");

        sps.MaxTbLog2SizeY = sps.MinTbLog2SizeY + reader.ReadUE(3, "log2_diff_max_min_luma_transform_block_size");
        if (sps.MaxTbLog2SizeY > Math.Min(sps.CtbLog2SizeY, 5))
            throw HevcErrors.Invalid("the maximum transform block size is out of range.");

        sps.MaxTransformHierarchyDepthInter = reader.ReadUE(sps.CtbLog2SizeY - sps.MinTbLog2SizeY, "max_transform_hierarchy_depth_inter");
        sps.MaxTransformHierarchyDepthIntra = reader.ReadUE(sps.CtbLog2SizeY - sps.MinTbLog2SizeY, "max_transform_hierarchy_depth_intra");
        sps.ScalingListEnabledFlag = reader.ReadFlag();
        if (sps.ScalingListEnabledFlag && reader.ReadFlag())
        {
            sps.ScalingList = HevcScalingList.Parse(ref reader);
        }

        sps.AmpEnabledFlag = reader.ReadFlag();
        sps.SampleAdaptiveOffsetEnabledFlag = reader.ReadFlag();
        sps.PcmEnabledFlag = reader.ReadFlag();
        if (sps.PcmEnabledFlag)
        {
            sps.PcmBitDepthY = (int)reader.ReadBits(4) + 1;
            sps.PcmBitDepthC = (int)reader.ReadBits(4) + 1;
            if (sps.PcmBitDepthY > sps.BitDepth || sps.PcmBitDepthC > sps.BitDepth)
                throw HevcErrors.Invalid("the bit depth of the PCM samples is larger than the bit depth of the samples.");

            sps.Log2MinIpcmCbSizeY = reader.ReadUE(2, "log2_min_pcm_luma_coding_block_size_minus3") + 3;
            if (sps.Log2MinIpcmCbSizeY < Math.Min(sps.MinCbLog2SizeY, 5) || sps.Log2MinIpcmCbSizeY > Math.Min(sps.CtbLog2SizeY, 5))
                throw HevcErrors.Invalid("the minimum PCM coding block size is out of range.");

            sps.Log2MaxIpcmCbSizeY = sps.Log2MinIpcmCbSizeY + reader.ReadUE(2, "log2_diff_max_min_pcm_luma_coding_block_size");
            if (sps.Log2MaxIpcmCbSizeY > Math.Min(sps.CtbLog2SizeY, 5))
                throw HevcErrors.Invalid("the maximum PCM coding block size is out of range.");

            sps.PcmLoopFilterDisabledFlag = reader.ReadFlag();
        }

        var numShortTermRefPicSets = reader.ReadUE(HevcConstants.MaxShortTermRefPicSets, "num_short_term_ref_pic_sets");
        sps._shortTermRefPicSets = new HevcShortTermRefPicSet[numShortTermRefPicSets];
        for (var i = 0; i < numShortTermRefPicSets; i++)
        {
            sps._shortTermRefPicSets[i] = HevcShortTermRefPicSet.Parse(ref reader, i, numShortTermRefPicSets, sps._shortTermRefPicSets.AsSpan(0, i), sps._maxDecPicBufferingMinus1[sps.MaxSubLayersMinus1]);
        }

        sps.LongTermRefPicsPresentFlag = reader.ReadFlag();
        if (sps.LongTermRefPicsPresentFlag)
        {
            var numLongTermRefPicsSps = reader.ReadUE(HevcConstants.MaxLongTermRefPicsSps, "num_long_term_ref_pics_sps");
            sps._ltRefPicPocLsbSps = new int[numLongTermRefPicsSps];
            sps._usedByCurrPicLtSpsFlag = new bool[numLongTermRefPicsSps];
            for (var i = 0; i < numLongTermRefPicsSps; i++)
            {
                sps._ltRefPicPocLsbSps[i] = (int)reader.ReadBits(sps.Log2MaxPicOrderCntLsb);
                sps._usedByCurrPicLtSpsFlag[i] = reader.ReadFlag();
            }
        }

        sps.SpsTemporalMvpEnabledFlag = reader.ReadFlag();
        sps.StrongIntraSmoothingEnabledFlag = reader.ReadFlag();
        if (reader.ReadFlag())
        {
            sps.Vui = HevcVui.Parse(ref reader, sps.MaxSubLayersMinus1);
        }

        var spsRangeExtensionFlag = false;
        var spsExtension4Bits = 0u;
        if (reader.ReadFlag())
        {
            spsRangeExtensionFlag = reader.ReadFlag();
            if (reader.ReadFlag())
                throw HevcErrors.Unsupported("multilayer extension");

            if (reader.ReadFlag())
                throw HevcErrors.Unsupported("3D extension");

            if (reader.ReadFlag())
                throw HevcErrors.Unsupported("screen content coding extension");

            spsExtension4Bits = reader.ReadBits(4);
        }

        if (spsRangeExtensionFlag)
        {
            // Clause 7.3.2.2.2 "Sequence parameter set range extension syntax"
            sps.TransformSkipRotationEnabledFlag = reader.ReadFlag();
            sps.TransformSkipContextEnabledFlag = reader.ReadFlag();
            sps.ImplicitRdpcmEnabledFlag = reader.ReadFlag();
            sps.ExplicitRdpcmEnabledFlag = reader.ReadFlag();
            if (reader.ReadFlag())
                throw HevcErrors.Unsupported("extended precision processing");

            sps.IntraSmoothingDisabledFlag = reader.ReadFlag();
            sps.HighPrecisionOffsetsEnabledFlag = reader.ReadFlag();
            sps.PersistentRiceAdaptationEnabledFlag = reader.ReadFlag();
            if (reader.ReadFlag())
                throw HevcErrors.Unsupported("CABAC bypass alignment");
        }

        // sps_extension_data_flag: the data of the extensions that this version of the specification does not define
        if (spsExtension4Bits is not 0)
        {
            reader.SkipExtensionData();
        }

        reader.ReadRbspTrailingBits();

        // Equations 7-15 to 7-17
        var ctbSizeY = 1 << sps.CtbLog2SizeY;
        sps.PicWidthInCtbsY = (sps.PicWidthInLumaSamples + ctbSizeY - 1) >> sps.CtbLog2SizeY;
        sps.PicHeightInCtbsY = (sps.PicHeightInLumaSamples + ctbSizeY - 1) >> sps.CtbLog2SizeY;
        return sps;
    }
}
