using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the raw byte sequence payloads of the parameter sets, of the slice segment headers and of the decoded picture
/// hash (Rec. ITU-T H.265, clauses 7.3.2.1 to 7.3.2.4, 7.3.3, 7.3.6.1, 7.3.7, E.2.1 and D.2.20): the mirror of
/// <see cref="HevcVideoParameterSet"/>, <see cref="HevcSequenceParameterSet"/>, <see cref="HevcPictureParameterSet"/>,
/// <see cref="HevcSliceSegmentHeader"/> and <see cref="HevcSeiReader"/>, in the order of their syntax tables.
/// <see cref="HevcNalWriter"/> puts a payload in a NAL unit.
/// </summary>
/// <remarks>
/// <para>
/// The encoder never builds the derived variables of a header itself: it writes the syntax elements, then parses the bits
/// it wrote with the parsers of the decoder. The model it encodes with is therefore the one a decoder derives from the
/// stream. A header that depends on another one (a picture parameter set on its sequence parameter set, a slice segment
/// header on both) is written from the parsed form of that one.
/// </para>
/// <para>
/// Only the syntax that the encoder produces is written: one layer and one sub-layer, no timing and no hypothetical
/// reference decoder parameters, no scaling list data (the default lists), no long-term reference pictures, no tiles, no
/// wavefront, no weighted prediction, one slice of one slice segment per picture, and of the extensions, the range
/// extension.
/// </para>
/// </remarks>
internal static class HevcHeaderWriter
{
    /// <summary>The value of <c>video_format</c> of an unspecified video format (Table E.2).</summary>
    private const int VideoFormatUnspecified = 5;

    /// <summary>
    /// Appends the payload of a video parameter set to <paramref name="output"/> (<c>video_parameter_set_rbsp()</c>,
    /// clause 7.3.2.1). Its identifier is 0.
    /// </summary>
    public static void WriteVideoParameterSet(PooledPayloadWriter output, in HevcSequenceParameters parameters)
    {
        Debug.Assert(parameters.IsValid);
        var writer = new HevcBitWriter(output);
        writer.WriteBits(0, 4); // vps_video_parameter_set_id
        writer.WriteFlag(true); // vps_base_layer_internal_flag
        writer.WriteFlag(true); // vps_base_layer_available_flag
        writer.WriteBits(0, 6); // vps_max_layers_minus1
        writer.WriteBits(0, 3); // vps_max_sub_layers_minus1

        // Clause 7.4.3.1: 1 when there is one sub-layer
        writer.WriteFlag(true); // vps_temporal_id_nesting_flag
        writer.WriteBits(0xFFFF, 16); // vps_reserved_0xffff_16bits
        WriteProfileTierLevel(ref writer, parameters);
        writer.WriteFlag(true); // vps_sub_layer_ordering_info_present_flag
        writer.WriteUE((uint)parameters.MaxDecPicBufferingMinus1); // vps_max_dec_pic_buffering_minus1[ 0 ]
        writer.WriteUE(HevcSequenceParameters.MaxNumReorderPics); // vps_max_num_reorder_pics[ 0 ]
        writer.WriteUE(0); // vps_max_latency_increase_plus1[ 0 ]: no limit is expressed
        writer.WriteBits(0, 6); // vps_max_layer_id
        writer.WriteUE(0); // vps_num_layer_sets_minus1
        writer.WriteFlag(false); // vps_timing_info_present_flag
        writer.WriteFlag(false); // vps_extension_flag
        writer.WriteRbspTrailingBits();
    }

    /// <summary>
    /// Appends the payload of a sequence parameter set to <paramref name="output"/> (<c>seq_parameter_set_rbsp()</c>,
    /// clause 7.3.2.2.1). Its identifier is 0, and it refers to the video parameter set 0.
    /// </summary>
    public static void WriteSequenceParameterSet(PooledPayloadWriter output, in HevcSequenceParameters parameters)
    {
        Debug.Assert(parameters.IsValid);
        var tools = parameters.Tools;
        var writer = new HevcBitWriter(output);
        writer.WriteBits(0, 4); // sps_video_parameter_set_id
        writer.WriteBits(0, 3); // sps_max_sub_layers_minus1
        writer.WriteFlag(true); // sps_temporal_id_nesting_flag
        WriteProfileTierLevel(ref writer, parameters);
        writer.WriteUE(0); // sps_seq_parameter_set_id
        writer.WriteUE((uint)parameters.ChromaFormatIdc);
        if (parameters.ChromaFormatIdc is 3)
        {
            writer.WriteFlag(false); // separate_colour_plane_flag
        }

        writer.WriteUE((uint)parameters.PicWidthInLumaSamples);
        writer.WriteUE((uint)parameters.PicHeightInLumaSamples);
        writer.WriteFlag(parameters.ConformanceWindowFlag);
        if (parameters.ConformanceWindowFlag)
        {
            // The pictures are at the top left of the coded pictures
            writer.WriteUE(0); // conf_win_left_offset
            writer.WriteUE((uint)parameters.ConfWinRightOffset);
            writer.WriteUE(0); // conf_win_top_offset
            writer.WriteUE((uint)parameters.ConfWinBottomOffset);
        }

        writer.WriteUE((uint)(parameters.BitDepth - 8)); // bit_depth_luma_minus8
        writer.WriteUE((uint)(parameters.BitDepth - 8)); // bit_depth_chroma_minus8
        writer.WriteUE(HevcSequenceParameters.Log2MaxPicOrderCntLsb - 4); // log2_max_pic_order_cnt_lsb_minus4
        writer.WriteFlag(true); // sps_sub_layer_ordering_info_present_flag
        writer.WriteUE((uint)parameters.MaxDecPicBufferingMinus1); // sps_max_dec_pic_buffering_minus1[ 0 ]
        writer.WriteUE(HevcSequenceParameters.MaxNumReorderPics); // sps_max_num_reorder_pics[ 0 ]
        writer.WriteUE(0); // sps_max_latency_increase_plus1[ 0 ]
        writer.WriteUE((uint)(parameters.MinCbLog2SizeY - 3)); // log2_min_luma_coding_block_size_minus3
        writer.WriteUE((uint)(parameters.CtbLog2SizeY - parameters.MinCbLog2SizeY)); // log2_diff_max_min_luma_coding_block_size
        writer.WriteUE((uint)(parameters.MinTbLog2SizeY - 2)); // log2_min_luma_transform_block_size_minus2
        writer.WriteUE((uint)(parameters.MaxTbLog2SizeY - parameters.MinTbLog2SizeY)); // log2_diff_max_min_luma_transform_block_size
        writer.WriteUE((uint)parameters.MaxTransformHierarchyDepthInter);
        writer.WriteUE((uint)parameters.MaxTransformHierarchyDepthIntra);
        var scalingListEnabled = tools.HasFlag(HevcCodingTools.ScalingLists);
        writer.WriteFlag(scalingListEnabled); // scaling_list_enabled_flag
        if (scalingListEnabled)
        {
            // Clause 7.4.3.2.1: without data, the lists are the default ones
            writer.WriteFlag(false); // sps_scaling_list_data_present_flag
        }

        writer.WriteFlag(tools.HasFlag(HevcCodingTools.AsymmetricMotionPartitions)); // amp_enabled_flag
        writer.WriteFlag(tools.HasFlag(HevcCodingTools.SampleAdaptiveOffset)); // sample_adaptive_offset_enabled_flag
        var pcmEnabled = tools.HasFlag(HevcCodingTools.Pcm);
        writer.WriteFlag(pcmEnabled); // pcm_enabled_flag
        if (pcmEnabled)
        {
            writer.WriteBits((uint)(parameters.EffectivePcmBitDepth - 1), 4); // pcm_sample_bit_depth_luma_minus1
            writer.WriteBits((uint)(parameters.EffectivePcmBitDepth - 1), 4); // pcm_sample_bit_depth_chroma_minus1
            writer.WriteUE((uint)(parameters.Log2MinIpcmCbSizeY - 3)); // log2_min_pcm_luma_coding_block_size_minus3
            writer.WriteUE((uint)(parameters.Log2MaxIpcmCbSizeY - parameters.Log2MinIpcmCbSizeY)); // log2_diff_max_min_pcm_luma_coding_block_size

            // The samples of a PCM coding unit are the samples that were written
            writer.WriteFlag(true); // pcm_loop_filter_disabled_flag
        }

        writer.WriteUE((uint)parameters.NumShortTermRefPicSets); // num_short_term_ref_pic_sets
        if (parameters.NumShortTermRefPicSets is not 0)
        {
            Debug.Assert(parameters.NumShortTermRefPicSets is 1);
            WritePreviousPictureRefPicSet(ref writer);
        }

        writer.WriteFlag(false); // long_term_ref_pics_present_flag
        writer.WriteFlag(tools.HasFlag(HevcCodingTools.TemporalMvp)); // sps_temporal_mvp_enabled_flag
        writer.WriteFlag(tools.HasFlag(HevcCodingTools.StrongIntraSmoothing)); // strong_intra_smoothing_enabled_flag
        writer.WriteFlag(true); // vui_parameters_present_flag
        WriteVui(ref writer, parameters);
        writer.WriteFlag(parameters.HasRangeExtension); // sps_extension_present_flag
        if (parameters.HasRangeExtension)
        {
            writer.WriteFlag(true); // sps_range_extension_flag
            writer.WriteFlag(false); // sps_multilayer_extension_flag
            writer.WriteFlag(false); // sps_3d_extension_flag
            writer.WriteFlag(false); // sps_scc_extension_flag
            writer.WriteBits(0, 4); // sps_extension_4bits

            // Clause 7.3.2.2.2 "Sequence parameter set range extension syntax"
            writer.WriteFlag(tools.HasFlag(HevcCodingTools.TransformSkipRotation)); // transform_skip_rotation_enabled_flag
            writer.WriteFlag(tools.HasFlag(HevcCodingTools.TransformSkipContext)); // transform_skip_context_enabled_flag
            writer.WriteFlag(tools.HasFlag(HevcCodingTools.ImplicitRdpcm)); // implicit_rdpcm_enabled_flag
            writer.WriteFlag(tools.HasFlag(HevcCodingTools.ExplicitRdpcm)); // explicit_rdpcm_enabled_flag
            writer.WriteFlag(false); // extended_precision_processing_flag
            writer.WriteFlag(tools.HasFlag(HevcCodingTools.IntraSmoothingDisabled)); // intra_smoothing_disabled_flag
            writer.WriteFlag(false); // high_precision_offsets_enabled_flag
            writer.WriteFlag(tools.HasFlag(HevcCodingTools.PersistentRiceAdaptation)); // persistent_rice_adaptation_enabled_flag
            writer.WriteFlag(false); // cabac_bypass_alignment_enabled_flag
        }

        writer.WriteRbspTrailingBits();
    }

    /// <summary>
    /// Appends the payload of a picture parameter set to <paramref name="output"/> (<c>pic_parameter_set_rbsp()</c>,
    /// clause 7.3.2.3.1).
    /// </summary>
    /// <param name="output">The destination.</param>
    /// <param name="sps">The sequence parameter set that the picture parameter set refers to, as a decoder reads it.</param>
    /// <param name="parameters">The values of the syntax elements.</param>
    public static void WritePictureParameterSet(PooledPayloadWriter output, HevcSequenceParameterSet sps, in HevcPictureParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(sps);
        Debug.Assert(parameters.PicParameterSetId is >= 0 and < HevcConstants.MaxPpsCount);
        Debug.Assert(parameters.InitQp >= -sps.QpBdOffset && parameters.InitQp <= 51);
        Debug.Assert(!parameters.CuQpDeltaEnabled || parameters.DiffCuQpDeltaDepth <= sps.CtbLog2SizeY - sps.MinCbLog2SizeY);
        Debug.Assert(parameters.Log2MaxTransformSkipBlockSizeMinus2 is 0 || (parameters.TransformSkipEnabled && parameters.Log2MaxTransformSkipBlockSizeMinus2 <= sps.MaxTbLog2SizeY - 2));
        Debug.Assert(!parameters.CrossComponentPredictionEnabled || sps.ChromaArrayType is 3);

        // Annex A: only the format range extensions profiles have a range extension, and only their 4:4:4 profiles, which
        // do not have general_max_422chroma_constraint_flag, have transform skip blocks of more than 4x4 samples
        Debug.Assert(!parameters.HasRangeExtension || sps.ProfileTierLevel.GeneralProfileIdc is 4);
        Debug.Assert(parameters.Log2MaxTransformSkipBlockSizeMinus2 is 0 || (sps.ProfileTierLevel.GeneralConstraintIndicatorFlags & (1UL << 40)) is 0);

        var writer = new HevcBitWriter(output);
        writer.WriteUE((uint)parameters.PicParameterSetId); // pps_pic_parameter_set_id
        writer.WriteUE((uint)sps.SeqParameterSetId); // pps_seq_parameter_set_id
        writer.WriteFlag(false); // dependent_slice_segments_enabled_flag
        writer.WriteFlag(false); // output_flag_present_flag
        writer.WriteBits(0, 3); // num_extra_slice_header_bits
        writer.WriteFlag(parameters.SignDataHidingEnabled); // sign_data_hiding_enabled_flag
        writer.WriteFlag(parameters.CabacInitPresent); // cabac_init_present_flag

        // A P slice has one reference picture, the picture that precedes it
        writer.WriteUE(0); // num_ref_idx_l0_default_active_minus1
        writer.WriteUE(0); // num_ref_idx_l1_default_active_minus1
        writer.WriteSE(parameters.InitQp - 26); // init_qp_minus26
        writer.WriteFlag(parameters.ConstrainedIntraPred); // constrained_intra_pred_flag
        writer.WriteFlag(parameters.TransformSkipEnabled); // transform_skip_enabled_flag
        writer.WriteFlag(parameters.CuQpDeltaEnabled); // cu_qp_delta_enabled_flag
        if (parameters.CuQpDeltaEnabled)
        {
            writer.WriteUE((uint)parameters.DiffCuQpDeltaDepth); // diff_cu_qp_delta_depth
        }

        writer.WriteSE(parameters.CbQpOffset); // pps_cb_qp_offset
        writer.WriteSE(parameters.CrQpOffset); // pps_cr_qp_offset
        writer.WriteFlag(parameters.SliceChromaQpOffsetsPresent); // pps_slice_chroma_qp_offsets_present_flag
        writer.WriteFlag(false); // weighted_pred_flag
        writer.WriteFlag(false); // weighted_bipred_flag
        writer.WriteFlag(parameters.TransquantBypassEnabled); // transquant_bypass_enabled_flag
        writer.WriteFlag(false); // tiles_enabled_flag
        writer.WriteFlag(parameters.EntropyCodingSyncEnabled); // entropy_coding_sync_enabled_flag

        // A picture is one slice: no in-loop filter crosses a slice boundary
        writer.WriteFlag(false); // pps_loop_filter_across_slices_enabled_flag
        writer.WriteFlag(parameters.DeblockingFilterControlPresent); // deblocking_filter_control_present_flag
        if (parameters.DeblockingFilterControlPresent)
        {
            writer.WriteFlag(parameters.DeblockingFilterOverrideEnabled); // deblocking_filter_override_enabled_flag
            writer.WriteFlag(parameters.DeblockingFilterDisabled); // pps_deblocking_filter_disabled_flag
            if (!parameters.DeblockingFilterDisabled)
            {
                writer.WriteSE(parameters.BetaOffsetDiv2); // pps_beta_offset_div2
                writer.WriteSE(parameters.TcOffsetDiv2); // pps_tc_offset_div2
            }
        }

        writer.WriteFlag(false); // pps_scaling_list_data_present_flag
        writer.WriteFlag(false); // lists_modification_present_flag
        writer.WriteUE(0); // log2_parallel_merge_level_minus2
        writer.WriteFlag(false); // slice_segment_header_extension_present_flag
        writer.WriteFlag(parameters.HasRangeExtension); // pps_extension_present_flag
        if (parameters.HasRangeExtension)
        {
            writer.WriteFlag(true); // pps_range_extension_flag
            writer.WriteFlag(false); // pps_multilayer_extension_flag
            writer.WriteFlag(false); // pps_3d_extension_flag
            writer.WriteFlag(false); // pps_scc_extension_flag
            writer.WriteBits(0, 4); // pps_extension_4bits

            // Clause 7.3.2.3.2 "Picture parameter set range extension syntax"
            if (parameters.TransformSkipEnabled)
            {
                writer.WriteUE((uint)parameters.Log2MaxTransformSkipBlockSizeMinus2); // log2_max_transform_skip_block_size_minus2
            }

            writer.WriteFlag(parameters.CrossComponentPredictionEnabled); // cross_component_prediction_enabled_flag
            writer.WriteFlag(false); // chroma_qp_offset_list_enabled_flag
            writer.WriteUE((uint)parameters.Log2SaoOffsetScaleLuma); // log2_sao_offset_scale_luma
            writer.WriteUE((uint)parameters.Log2SaoOffsetScaleChroma); // log2_sao_offset_scale_chroma
        }

        writer.WriteRbspTrailingBits();
    }

    /// <summary>
    /// Appends the header of the slice segment of a picture to <paramref name="output"/> (<c>slice_segment_header()</c>,
    /// clause 7.3.6.1), up to its byte alignment: the slice segment data follow. The slice segment is the first and the
    /// only one of its picture.
    /// </summary>
    /// <param name="output">The destination: the payload of the NAL unit of the slice segment.</param>
    /// <param name="sps">The active sequence parameter set, as a decoder reads it.</param>
    /// <param name="pps">The active picture parameter set, as a decoder reads it.</param>
    /// <param name="slice">The values of the syntax elements.</param>
    /// <param name="entryPointOffsetsMinus1">
    /// <c>entry_point_offset_minus1</c> of the rows of coding tree blocks of the wavefront that follow the first one, when
    /// <c>entropy_coding_sync_enabled_flag</c> is 1.
    /// </param>
    public static void WriteSliceSegmentHeader(PooledPayloadWriter output, HevcSequenceParameterSet sps, HevcPictureParameterSet pps, in HevcSliceParameters slice, ReadOnlySpan<int> entryPointOffsetsMinus1 = default)
    {
        ArgumentNullException.ThrowIfNull(sps);
        ArgumentNullException.ThrowIfNull(pps);
        Debug.Assert(slice.NalUnitType is HevcConstants.NalIdrNLp or HevcConstants.NalIdrWRadl or HevcConstants.NalTrailR or HevcConstants.NalTrailN);
        Debug.Assert(slice.SliceType is HevcConstants.SliceI or HevcConstants.SliceP);
        Debug.Assert(slice.SliceQp >= -sps.QpBdOffset && slice.SliceQp <= 51);

        // The syntax that the encoder does not produce
        Debug.Assert(!pps.DependentSliceSegmentsEnabledFlag && !pps.OutputFlagPresentFlag && pps.NumExtraSliceHeaderBits is 0 && !pps.ListsModificationPresentFlag);
        Debug.Assert(!pps.WeightedPredFlag && !pps.ChromaQpOffsetListEnabledFlag && !pps.LoopFilterAcrossSlicesEnabledFlag && !pps.TilesEnabledFlag);
        Debug.Assert(pps.EntropyCodingSyncEnabledFlag || entryPointOffsetsMinus1.IsEmpty);
        Debug.Assert(!pps.SliceSegmentHeaderExtensionPresentFlag && !sps.LongTermRefPicsPresentFlag && pps.NumRefIdxL0DefaultActiveMinus1 is 0);

        var isIdr = slice.NalUnitType is HevcConstants.NalIdrNLp or HevcConstants.NalIdrWRadl;
        var writer = new HevcBitWriter(output);
        writer.WriteFlag(true); // first_slice_segment_in_pic_flag
        if (isIdr)
        {
            // The pictures that precede a key frame are output: they are in output order
            writer.WriteFlag(false); // no_output_of_prior_pics_flag
        }

        writer.WriteUE((uint)pps.PicParameterSetId); // slice_pic_parameter_set_id
        writer.WriteUE((uint)slice.SliceType); // slice_type
        if (!isIdr)
        {
            // The picture refers to the picture before it: the only candidate set of the sequence parameter set, whose
            // index has no bit
            Debug.Assert(sps.ShortTermRefPicSets.Length is 1);
            writer.WriteBits((uint)slice.PicOrderCnt & ((1u << sps.Log2MaxPicOrderCntLsb) - 1), sps.Log2MaxPicOrderCntLsb); // slice_pic_order_cnt_lsb
            writer.WriteFlag(true); // short_term_ref_pic_set_sps_flag
            if (sps.SpsTemporalMvpEnabledFlag)
            {
                writer.WriteFlag(slice.SliceTemporalMvpEnabled); // slice_temporal_mvp_enabled_flag
            }
        }
        else
        {
            Debug.Assert(slice.SliceType is HevcConstants.SliceI && slice.PicOrderCnt is 0);
        }

        if (sps.SampleAdaptiveOffsetEnabledFlag)
        {
            writer.WriteFlag(slice.SliceSaoLuma); // slice_sao_luma_flag
            if (sps.ChromaArrayType is not 0)
            {
                writer.WriteFlag(slice.SliceSaoChroma); // slice_sao_chroma_flag
            }
        }

        if (slice.SliceType is HevcConstants.SliceP)
        {
            // One reference picture, the default of the picture parameter set; collocated_ref_idx is not present for one
            // reference picture, and the collocated picture of a P slice is in the list 0
            writer.WriteFlag(false); // num_ref_idx_active_override_flag
            if (pps.CabacInitPresentFlag)
            {
                writer.WriteFlag(slice.CabacInit); // cabac_init_flag
            }

            Debug.Assert(slice.FiveMinusMaxNumMergeCand is >= 0 and <= 4);
            writer.WriteUE((uint)slice.FiveMinusMaxNumMergeCand); // five_minus_max_num_merge_cand
        }

        writer.WriteSE(slice.SliceQp - (26 + pps.InitQpMinus26)); // slice_qp_delta
        if (pps.SliceChromaQpOffsetsPresentFlag)
        {
            writer.WriteSE(slice.SliceCbQpOffset); // slice_cb_qp_offset
            writer.WriteSE(slice.SliceCrQpOffset); // slice_cr_qp_offset
        }

        var deblockingFilterOverride = false;
        if (pps.DeblockingFilterOverrideEnabledFlag)
        {
            deblockingFilterOverride = slice.DeblockingFilterOverride;
            writer.WriteFlag(deblockingFilterOverride); // deblocking_filter_override_flag
        }

        if (deblockingFilterOverride)
        {
            writer.WriteFlag(slice.SliceDeblockingFilterDisabled); // slice_deblocking_filter_disabled_flag
            if (!slice.SliceDeblockingFilterDisabled)
            {
                writer.WriteSE(slice.SliceBetaOffsetDiv2); // slice_beta_offset_div2
                writer.WriteSE(slice.SliceTcOffsetDiv2); // slice_tc_offset_div2
            }
        }

        if (pps.EntropyCodingSyncEnabledFlag)
        {
            writer.WriteUE((uint)entryPointOffsetsMinus1.Length); // num_entry_point_offsets
            if (!entryPointOffsetsMinus1.IsEmpty)
            {
                // Each offset has the bits of the largest one
                var max = 0;
                foreach (var offset in entryPointOffsetsMinus1)
                {
                    Debug.Assert(offset >= 0);
                    max = Math.Max(max, offset);
                }

                var length = Math.Max(1, 32 - int.LeadingZeroCount(max));
                writer.WriteUE((uint)(length - 1)); // offset_len_minus1
                foreach (var offset in entryPointOffsetsMinus1)
                {
                    writer.WriteBits((uint)offset, length); // entry_point_offset_minus1
                }
            }
        }

        writer.WriteByteAlignment();
    }

    /// <summary>
    /// Appends the payload of a supplemental enhancement information NAL unit that has one message, the hash of the
    /// decoded picture (<c>sei_rbsp()</c>, clauses 7.3.2.4, 7.3.5 and D.2.20). The message is a suffix message: its NAL
    /// unit follows the slice segment of the picture.
    /// </summary>
    public static void WriteDecodedPictureHash(PooledPayloadWriter output, HevcDecodedPictureHash hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        Debug.Assert(hash.HashType is HevcDecodedPictureHash.Md5 or HevcDecodedPictureHash.Crc or HevcDecodedPictureHash.Checksum);
        Debug.Assert(hash.ComponentCount is 1 or 3);
        var valueLength = hash.HashType switch
        {
            HevcDecodedPictureHash.Md5 => 16,
            HevcDecodedPictureHash.Crc => 2,
            _ => 4,
        };
        var writer = new HevcBitWriter(output);

        // sei_message( ): the type and the size are less than 255, which is one byte each
        writer.WriteBits(HevcConstants.SeiDecodedPictureHash, 8); // last_payload_type_byte
        writer.WriteBits((uint)(1 + (hash.ComponentCount * valueLength)), 8); // last_payload_size_byte
        writer.WriteBits((uint)hash.HashType, 8); // hash_type
        for (var cIdx = 0; cIdx < hash.ComponentCount; cIdx++)
        {
            if (hash.HashType is HevcDecodedPictureHash.Md5)
            {
                foreach (var value in hash.GetMd5(cIdx))
                {
                    writer.WriteBits(value, 8); // picture_md5[ cIdx ][ i ]
                }
            }
            else
            {
                writer.WriteBits(hash.GetValue(cIdx), valueLength * 8); // picture_crc[ cIdx ], picture_checksum[ cIdx ]
            }
        }

        writer.WriteRbspTrailingBits();
    }

    // Clause 7.3.3 "Profile, tier and level syntax", for one sub-layer: profile_tier_level( 1, 0 )
    private static void WriteProfileTierLevel(ref HevcBitWriter writer, in HevcSequenceParameters parameters)
    {
        writer.WriteBits(0, 2); // general_profile_space
        writer.WriteFlag(parameters.GeneralTierFlag); // general_tier_flag
        writer.WriteBits((uint)parameters.GeneralProfileIdc, 5); // general_profile_idc
        writer.WriteBits(parameters.GeneralProfileCompatibilityFlags, 32); // general_profile_compatibility_flag[ j ]

        // general_progressive_source_flag to general_inbld_flag: 48 bits whatever the profile
        var constraints = parameters.GeneralConstraintIndicatorFlags;
        writer.WriteBits((uint)(constraints >> 32), 16);
        writer.WriteBits((uint)constraints, 32);
        writer.WriteBits((uint)parameters.GeneralLevelIdc, 8); // general_level_idc
    }

    // Clause 7.3.7 "Short-term reference picture set syntax", for the first set of a sequence parameter set, which has no
    // inter_ref_pic_set_prediction_flag: the picture that precedes the current one in output order, which it refers to
    private static void WritePreviousPictureRefPicSet(ref HevcBitWriter writer)
    {
        writer.WriteUE(1); // num_negative_pics
        writer.WriteUE(0); // num_positive_pics
        writer.WriteUE(0); // delta_poc_s0_minus1[ 0 ]
        writer.WriteFlag(true); // used_by_curr_pic_s0_flag[ 0 ]
    }

    // Clause E.2.1 "VUI parameters syntax": the description of the colour of the samples
    private static void WriteVui(ref HevcBitWriter writer, in HevcSequenceParameters parameters)
    {
        writer.WriteFlag(false); // aspect_ratio_info_present_flag
        writer.WriteFlag(false); // overscan_info_present_flag
        writer.WriteFlag(true); // video_signal_type_present_flag
        writer.WriteBits(VideoFormatUnspecified, 3); // video_format
        writer.WriteFlag(parameters.FullRange); // video_full_range_flag
        writer.WriteFlag(true); // colour_description_present_flag
        writer.WriteBits((uint)parameters.ColourPrimaries, 8); // colour_primaries
        writer.WriteBits((uint)parameters.TransferCharacteristics, 8); // transfer_characteristics
        writer.WriteBits((uint)parameters.MatrixCoeffs, 8); // matrix_coeffs

        // Clause E.3.1: the location of the chroma samples is only the one of 4:2:0 samples; the pictures are frames, so
        // that the location of both fields is the same
        var chromaLocInfoPresent = parameters.ChromaFormatIdc is 1;
        writer.WriteFlag(chromaLocInfoPresent); // chroma_loc_info_present_flag
        if (chromaLocInfoPresent)
        {
            writer.WriteUE((uint)parameters.ChromaSampleLocType); // chroma_sample_loc_type_top_field
            writer.WriteUE((uint)parameters.ChromaSampleLocType); // chroma_sample_loc_type_bottom_field
        }

        writer.WriteFlag(false); // neutral_chroma_indication_flag
        writer.WriteFlag(false); // field_seq_flag
        writer.WriteFlag(false); // frame_field_info_present_flag
        writer.WriteFlag(false); // default_display_window_flag
        writer.WriteFlag(false); // vui_timing_info_present_flag
        writer.WriteFlag(false); // bitstream_restriction_flag
    }
}
