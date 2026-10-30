using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Hevc;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The writers of the parameter sets, of the slice segment headers and of the decoded picture hash, and the profile, the
/// tier and the level that the encoder announces. The bytes of the shortest headers are worked out by hand from the syntax
/// tables of the specification (Rec. ITU-T H.265, clauses 7.3.2.1, 7.3.2.3.1 and 7.3.3); the other tests read the headers
/// back with the parsers of the decoder, which the conformance streams validate against the reference decoders, and
/// compare the profile and its constraint flags with the tables of Annex A, written out here.
/// </summary>
public sealed class HevcHeaderWriterTests
{
    private const HevcCodingTools NoTools = HevcCodingTools.None;

    // Table A.2: general_max_12bit_constraint_flag, general_max_10bit_constraint_flag, general_max_8bit_constraint_flag,
    // general_max_422chroma_constraint_flag, general_max_420chroma_constraint_flag,
    // general_max_monochrome_constraint_flag, general_intra_constraint_flag, general_one_picture_only_constraint_flag and
    // general_lower_bit_rate_constraint_flag of each format range extensions profile, "x" for a flag that may have both
    // values. Table A.1: the values of chroma_format_idc and the largest bit depth of the profile, and whether it allows
    // the flags of the range extensions that are not constrained to 0 by the 4:4:4 profiles only
    private static readonly Dictionary<string, (string Flags, string ChromaFormats, int MaxBitDepth, bool ExtendedTools)> RangeExtensionsProfiles = new(StringComparer.Ordinal)
    {
        ["Monochrome"] = ("111111001", "0", 8, false),
        ["Monochrome 10"] = ("110111001", "0", 10, false),
        ["Monochrome 12"] = ("100111001", "0", 12, false),
        ["Main 12"] = ("100110001", "01", 12, false),
        ["Main 4:2:2 10"] = ("110100001", "012", 10, false),
        ["Main 4:2:2 12"] = ("100100001", "012", 12, false),
        ["Main 4:4:4"] = ("111000001", "0123", 8, true),
        ["Main 4:4:4 10"] = ("110000001", "0123", 10, true),
        ["Main 4:4:4 12"] = ("100000001", "0123", 12, true),
        ["Main 12 Intra"] = ("10011010x", "01", 12, false),
        ["Main 4:2:2 10 Intra"] = ("11010010x", "012", 10, false),
        ["Main 4:2:2 12 Intra"] = ("10010010x", "012", 12, false),
        ["Main 4:4:4 Intra"] = ("11100010x", "0123", 8, true),
        ["Main 4:4:4 10 Intra"] = ("11000010x", "0123", 10, true),
        ["Main 4:4:4 12 Intra"] = ("10000010x", "0123", 12, true),
        ["Main 4:4:4 Still Picture"] = ("11100011x", "0123", 8, true),
    };

    private enum SequenceKind
    {
        /// <summary>One picture.</summary>
        Still,

        /// <summary>Several pictures that are all key frames.</summary>
        IntraOnly,

        /// <summary>Pictures predicted from the picture before them.</summary>
        Inter,
    }

    [Fact]
    public void AVideoParameterSetHasTheBitsOfTheSyntaxTable()
    {
        // A 16x16 8-bit 4:2:0 still picture: the Main Still Picture profile, level 1
        var parameters = HevcSequenceParameters.Create(CreateSettings(16, 16, 8, 1, SequenceKind.Still));
        var rbsp = Write(output => HevcHeaderWriter.WriteVideoParameterSet(output, parameters));

        // vps_video_parameter_set_id 0000, vps_base_layer_internal_flag 1, vps_base_layer_available_flag 1,
        // vps_max_layers_minus1 000000 (2 bits here)                                                          -> 0000 1100
        // vps_max_layers_minus1 (4 bits) 0000, vps_max_sub_layers_minus1 000, vps_temporal_id_nesting_flag 1  -> 0000 0001
        // vps_reserved_0xffff_16bits                                                                          -> FF FF
        // general_profile_space 00, general_tier_flag 0, general_profile_idc 00011                            -> 0000 0011
        // general_profile_compatibility_flag[ 1 ], [ 2 ] and [ 3 ]: 0111 0000, then 24 flags equal to 0       -> 70 00 00 00
        // general_progressive_source_flag 1, general_interlaced_source_flag 0,
        // general_non_packed_constraint_flag 1, general_frame_only_constraint_flag 1, then 44 bits equal to 0 -> B0 00 00 00 00 00
        // general_level_idc 30                                                                                -> 1E
        // vps_sub_layer_ordering_info_present_flag 1, vps_max_dec_pic_buffering_minus1 1 (ue of 0),
        // vps_max_num_reorder_pics 1, vps_max_latency_increase_plus1 1, vps_max_layer_id 000000 (4 bits here) -> 1111 0000
        // vps_max_layer_id (2 bits) 00, vps_num_layer_sets_minus1 1 (ue of 0), vps_timing_info_present_flag 0,
        // vps_extension_flag 0, the stop bit 1, then 00                                                       -> 0010 0100
        Assert.Equal("0C01FFFF0370000000B000000000001EF024", Convert.ToHexString(rbsp));

        // In a NAL unit: the header of VPS_NUT, and an emulation prevention byte after each pair of zero bytes
        var scope = CreateScope();
        using var output = new PooledPayloadWriter(scope);
        HevcNalWriter.WriteNalUnit(output, HevcConstants.NalVps, 0, rbsp);
        Assert.Equal("40010C01FFFF037000000300B0000003000003001EF024", Convert.ToHexString(output.WrittenSpan));
    }

    [Fact]
    public void APictureParameterSetHasTheBitsOfTheSyntaxTable()
    {
        var sps = ParseSps(HevcSequenceParameters.Create(CreateSettings(16, 16, 8, 1, SequenceKind.Still)));

        // pps_pic_parameter_set_id 1 (ue of 0), pps_seq_parameter_set_id 1, dependent_slice_segments_enabled_flag 0,
        // output_flag_present_flag 0, num_extra_slice_header_bits 000, sign_data_hiding_enabled_flag 0        -> 1100 0000
        // cabac_init_present_flag 0, num_ref_idx_l0_default_active_minus1 1, num_ref_idx_l1_default_active_minus1 1,
        // init_qp_minus26 1 (se of 0), constrained_intra_pred_flag 0, transform_skip_enabled_flag 0,
        // cu_qp_delta_enabled_flag 0, pps_cb_qp_offset 1                                                      -> 0111 0001
        // pps_cr_qp_offset 1, pps_slice_chroma_qp_offsets_present_flag 0, weighted_pred_flag 0,
        // weighted_bipred_flag 0, transquant_bypass_enabled_flag 0, tiles_enabled_flag 0,
        // entropy_coding_sync_enabled_flag 0, pps_loop_filter_across_slices_enabled_flag 0                    -> 1000 0000
        // deblocking_filter_control_present_flag 0, pps_scaling_list_data_present_flag 0,
        // lists_modification_present_flag 0, log2_parallel_merge_level_minus2 1,
        // slice_segment_header_extension_present_flag 0, pps_extension_present_flag 0, the stop bit 1, then 0 -> 0001 0010
        Assert.Equal("C0718012", Convert.ToHexString(Write(output => HevcHeaderWriter.WritePictureParameterSet(output, sps, new HevcPictureParameters(26)))));

        // init_qp_minus26 -4 is the code number 8, 0001001; pps_pic_parameter_set_id 2 is 011; the flags of the sign data
        // hiding, of the transform skip and of the transquant bypass:
        // 011 1 0 0 000 1 0 1 1 0001001 0 1 0 1 1 0 0 0 1 0 0 0 0 0 0 1 0 0 1, then 0
        var changed = new HevcPictureParameters(22) { PicParameterSetId = 2, SignDataHidingEnabled = true, TransformSkipEnabled = true, TransquantBypassEnabled = true };
        Assert.Equal("7058958812", Convert.ToHexString(Write(output => HevcHeaderWriter.WritePictureParameterSet(output, sps, changed))));
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(0, 10)]
    [InlineData(0, 12)]
    [InlineData(1, 8)]
    [InlineData(1, 10)]
    [InlineData(1, 12)]
    [InlineData(2, 8)]
    [InlineData(2, 10)]
    [InlineData(2, 12)]
    [InlineData(3, 8)]
    [InlineData(3, 10)]
    [InlineData(3, 12)]
    public void SequenceHeadersAreReadBackForEverySampleFormat(int chromaFormatIdc, int bitDepth)
    {
        var scope = CreateScope();
        (int Width, int Height)[] sizes = [(1, 1), (8, 8), (5, 7), (17, 255), (64, 64), (176, 144), (1920, 1080), (4097, 2161), (8192, 4320), (16384, 8640), (65472, 8)];
        (int MinCb, int Ctb, int MinTb, int MaxTb, int DepthInter, int DepthIntra)[] structures = [(3, 5, 2, 5, 3, 3), (3, 4, 2, 4, 0, 2), (4, 6, 2, 5, 1, 4), (4, 6, 3, 5, 3, 0), (5, 5, 2, 5, 2, 1), (6, 6, 4, 5, 0, 0)];
        var index = 0;
        foreach (var kind in Enum.GetValues<SequenceKind>())
        {
            foreach (var (width, height) in sizes)
            {
                // The colour description and the coding structure change with the case; the identity matrix needs 4:4:4
                var (primaries, transfer, matrix, fullRange) = (index % 3) switch
                {
                    0 => (1, 13, chromaFormatIdc is 3 ? 0 : 6, true),
                    1 => (9, 16, 9, false),
                    _ => (2, 2, 2, false),
                };
                var settings = CreateSettings(width, height, bitDepth, chromaFormatIdc, kind) with
                {
                    ColourPrimaries = primaries,
                    TransferCharacteristics = transfer,
                    MatrixCoeffs = matrix,
                    FullRange = fullRange,
                    ChromaSampleLocType = index % 6,
                };
                var structure = structures[index % structures.Length];
                var parameters = HevcSequenceParameters.Create(settings) with
                {
                    MinCbLog2SizeY = structure.MinCb,
                    CtbLog2SizeY = structure.Ctb,
                    MinTbLog2SizeY = structure.MinTb,
                    MaxTbLog2SizeY = structure.MaxTb,
                    MaxTransformHierarchyDepthInter = structure.DepthInter,
                    MaxTransformHierarchyDepthIntra = structure.DepthIntra,
                };
                index++;

                // Clause A.4.1: coding tree blocks of 16 samples up to level 4.1 only
                if (structure.Ctb is 4 && (long)width * height > 2228224)
                {
                    Assert.False(parameters.IsValid);
                    parameters = parameters with { CtbLog2SizeY = 5, MaxTbLog2SizeY = 5 };
                }

                Assert.True(parameters.IsValid);
                var name = $"{width}x{height} {kind} structure {structure}";

                // The coded size is a multiple of the minimum coding block size, and the conformance window removes the
                // rest in units of chroma samples
                var minCbSize = 1 << structure.MinCb;
                var subWidthC = chromaFormatIdc is 1 or 2 ? 2 : 1;
                var subHeightC = chromaFormatIdc is 1 ? 2 : 1;
                var codedWidth = (width + minCbSize - 1) / minCbSize * minCbSize;
                var codedHeight = (height + minCbSize - 1) / minCbSize * minCbSize;
                var outputWidth = (width + subWidthC - 1) / subWidthC * subWidthC;
                var outputHeight = (height + subHeightC - 1) / subHeightC * subHeightC;
                Assert.Equal((codedWidth, codedHeight), (parameters.PicWidthInLumaSamples, parameters.PicHeightInLumaSamples), name);
                Assert.Equal((outputWidth, outputHeight), (parameters.OutputWidth, parameters.OutputHeight), name);
                Assert.Equal((width, height), (parameters.Width, parameters.Height), name);

                var vps = HevcVideoParameterSet.Parse(Write(output => HevcHeaderWriter.WriteVideoParameterSet(output, parameters)));
                var spsRbsp = Write(output => HevcHeaderWriter.WriteSequenceParameterSet(output, parameters));
                var sps = HevcSequenceParameterSet.ParseRbsp(spsRbsp);

                // The video parameter set: one layer, one sub-layer, no timing
                Assert.Equal((0, 0, 0, true, false), (vps.VideoParameterSetId, vps.MaxLayersMinus1, vps.MaxSubLayersMinus1, vps.TemporalIdNestingFlag, vps.TimingInfoPresentFlag), name);
                AssertProfileTierLevel(vps.ProfileTierLevel, settings, parameters, kind, name);
                AssertProfileTierLevel(sps.ProfileTierLevel, settings, parameters, kind, name);

                // The sequence parameter set
                Assert.Equal((0, 0, 0, true), (sps.VideoParameterSetId, sps.SeqParameterSetId, sps.MaxSubLayersMinus1, sps.TemporalIdNestingFlag), name);
                Assert.Equal((chromaFormatIdc, bitDepth), (sps.ChromaFormatIdc, sps.BitDepth), name);
                Assert.Equal((codedWidth, codedHeight), (sps.PicWidthInLumaSamples, sps.PicHeightInLumaSamples), name);
                Assert.Equal((0, 0, outputWidth, outputHeight), (sps.OutputLeft, sps.OutputTop, sps.OutputWidth, sps.OutputHeight), name);
                Assert.Equal(codedWidth != outputWidth || codedHeight != outputHeight, sps.ConformanceWindowFlag, name);
                Assert.Equal(HevcSequenceParameters.Log2MaxPicOrderCntLsb, sps.Log2MaxPicOrderCntLsb, name);
                Assert.Equal((parameters.MinCbLog2SizeY, parameters.CtbLog2SizeY, parameters.MinTbLog2SizeY, parameters.MaxTbLog2SizeY), (sps.MinCbLog2SizeY, sps.CtbLog2SizeY, sps.MinTbLog2SizeY, sps.MaxTbLog2SizeY), name);
                Assert.Equal((structure.DepthInter, structure.DepthIntra), (sps.MaxTransformHierarchyDepthInter, sps.MaxTransformHierarchyDepthIntra), name);

                // A picture that refers to the previous one needs two pictures in the buffer; the pictures are in order
                var inter = kind is SequenceKind.Inter;
                Assert.Equal((inter ? 1 : 0, 0, 0u), (sps.MaxDecPicBufferingMinus1[0], sps.MaxNumReorderPics[0], sps.MaxLatencyIncreasePlus1[0]), name);
                Assert.HasCount(inter ? 1 : 0, sps.ShortTermRefPicSets, name);
                if (inter)
                {
                    var set = sps.ShortTermRefPicSets[0];
                    Assert.Equal((1, 0, -1, true), (set.NumNegativePics, set.NumPositivePics, set.DeltaPocS0[0], set.UsedByCurrPicS0[0]), name);
                }

                Assert.False(sps.LongTermRefPicsPresentFlag || sps.ScalingListEnabledFlag || sps.AmpEnabledFlag || sps.SampleAdaptiveOffsetEnabledFlag || sps.PcmEnabledFlag, name);
                Assert.False(sps.SpsTemporalMvpEnabledFlag || sps.StrongIntraSmoothingEnabledFlag, name);

                // The colour of the samples: the code points, and the location of 4:2:0 chroma samples
                var vui = sps.Vui;
                Assert.NotNull(vui, name);
                Assert.Equal((true, 5, fullRange, true), (vui.VideoSignalTypePresentFlag, vui.VideoFormat, vui.VideoFullRangeFlag, vui.ColourDescriptionPresentFlag), name);
                Assert.Equal((primaries, transfer, matrix), (vui.ColourPrimaries, vui.TransferCharacteristics, vui.MatrixCoeffs), name);
                Assert.Equal(chromaFormatIdc is 1, vui.ChromaLocInfoPresentFlag, name);
                Assert.Equal(chromaFormatIdc is 1 ? (settings.ChromaSampleLocType, settings.ChromaSampleLocType) : (0, 0), (vui.ChromaSampleLocTypeTopField, vui.ChromaSampleLocTypeBottomField), name);
                Assert.False(vui.FieldSeqFlag || vui.FrameFieldInfoPresentFlag || vui.DefaultDisplayWindowFlag || vui.TimingInfoPresentFlag || vui.NeutralChromaIndicationFlag, name);
                Assert.Equal(0, vui.AspectRatioIdc, name);

                // In its NAL unit, for the decoder and for the reader of the test harness, which shares no code with it
                using var output = new PooledPayloadWriter(scope);
                HevcNalWriter.WriteNalUnit(output, HevcConstants.NalSps, 0, spsRbsp);
                var fromNalUnit = HevcSequenceParameterSet.Parse(scope, output.WrittenSpan);
                Assert.Equal((sps.PicWidthInLumaSamples, sps.PicHeightInLumaSamples, sps.OutputWidth, sps.OutputHeight), (fromNalUnit.PicWidthInLumaSamples, fromNalUnit.PicHeightInLumaSamples, fromNalUnit.OutputWidth, fromNalUnit.OutputHeight), name);
                var reference = ReferenceHevcStream.ReadSequenceParameterSet(output.WrittenSpan);
                Assert.Equal(new ReferenceHevcSequence(codedWidth, codedHeight, chromaFormatIdc, bitDepth, bitDepth, 0, 0, outputWidth, outputHeight), reference, name);
            }
        }
    }

    [Fact]
    public void EachCodingToolSetsItsFlagAndTheProfileThatAllowsIt()
    {
        var tools = Enum.GetValues<HevcCodingTools>().Where(tool => tool is not (HevcCodingTools.None or HevcCodingTools.ExtendedTools)).ToList();
        Assert.HasCount(13, tools);
        var all = tools.Aggregate(NoTools, (accumulator, tool) => accumulator | tool);
        foreach (var enabled in tools.Append(NoTools).Append(all).Append(HevcCodingTools.Pcm | HevcCodingTools.SampleAdaptiveOffset))
        {
            foreach (var (chromaFormatIdc, bitDepth, kind) in new[] { (1, 8, SequenceKind.Still), (1, 8, SequenceKind.Inter), (0, 10, SequenceKind.IntraOnly), (2, 12, SequenceKind.Inter), (3, 8, SequenceKind.Still), (3, 10, SequenceKind.Inter), (1, 10, SequenceKind.Still) })
            {
                var settings = CreateSettings(40, 24, bitDepth, chromaFormatIdc, kind);
                var parameters = HevcSequenceParameters.Create(settings, enabled);
                Assert.True(parameters.IsValid);
                var name = $"{enabled} 4:{chromaFormatIdc} {bitDepth} bits {kind}";
                var sps = ParseSps(parameters);
                AssertProfileTierLevel(sps.ProfileTierLevel, settings, parameters, kind, name);
                Assert.Equal(enabled.HasFlag(HevcCodingTools.ScalingLists), sps.ScalingListEnabledFlag, name);

                // No scaling list data: the default lists
                Assert.Null(sps.ScalingList, name);
                Assert.Equal(enabled.HasFlag(HevcCodingTools.AsymmetricMotionPartitions), sps.AmpEnabledFlag, name);
                Assert.Equal(enabled.HasFlag(HevcCodingTools.SampleAdaptiveOffset), sps.SampleAdaptiveOffsetEnabledFlag, name);
                Assert.Equal(enabled.HasFlag(HevcCodingTools.Pcm), sps.PcmEnabledFlag, name);
                Assert.Equal(enabled.HasFlag(HevcCodingTools.TemporalMvp), sps.SpsTemporalMvpEnabledFlag, name);
                Assert.Equal(enabled.HasFlag(HevcCodingTools.StrongIntraSmoothing), sps.StrongIntraSmoothingEnabledFlag, name);
                Assert.Equal(enabled.HasFlag(HevcCodingTools.TransformSkipRotation), sps.TransformSkipRotationEnabledFlag, name);
                Assert.Equal(enabled.HasFlag(HevcCodingTools.TransformSkipContext), sps.TransformSkipContextEnabledFlag, name);
                Assert.Equal(enabled.HasFlag(HevcCodingTools.ImplicitRdpcm), sps.ImplicitRdpcmEnabledFlag, name);
                Assert.Equal(enabled.HasFlag(HevcCodingTools.ExplicitRdpcm), sps.ExplicitRdpcmEnabledFlag, name);
                Assert.Equal(enabled.HasFlag(HevcCodingTools.IntraSmoothingDisabled), sps.IntraSmoothingDisabledFlag, name);
                Assert.Equal(enabled.HasFlag(HevcCodingTools.PersistentRiceAdaptation), sps.PersistentRiceAdaptationEnabledFlag, name);
                Assert.False(sps.HighPrecisionOffsetsEnabledFlag, name);
                if (sps.PcmEnabledFlag)
                {
                    // PCM samples of the bit depth of the samples, in coding units of every size up to 32 samples, which
                    // the in-loop filters do not change
                    Assert.Equal((bitDepth, bitDepth, 3, 5, true), (sps.PcmBitDepthY, sps.PcmBitDepthC, sps.Log2MinIpcmCbSizeY, sps.Log2MaxIpcmCbSizeY, sps.PcmLoopFilterDisabledFlag), name);
                    var reduced = ParseSps(parameters with { PcmBitDepth = 5, MinCbLog2SizeY = 4, CtbLog2SizeY = 4, MaxTbLog2SizeY = 4, MaxTransformHierarchyDepthInter = 1, MaxTransformHierarchyDepthIntra = 1 });
                    Assert.Equal((5, 5, 4, 4, true), (reduced.PcmBitDepthY, reduced.PcmBitDepthC, reduced.Log2MinIpcmCbSizeY, reduced.Log2MaxIpcmCbSizeY, reduced.PcmLoopFilterDisabledFlag), name);
                }
            }
        }
    }

    [Fact]
    public void TheParametersOfASequenceThatCannotBeWrittenAreNotValid()
    {
        var valid = HevcSequenceParameters.Create(CreateSettings(1920, 1080, 8, 1, SequenceKind.Inter), HevcCodingTools.Pcm);
        Assert.True(valid.IsValid);

        // The default coding structure: coding tree blocks of 32 samples, which every level allows, coding blocks of 8 to
        // 32 samples and transform blocks of 4 to 32 samples
        Assert.Equal((3, 5, 2, 5, 3, 3), (valid.MinCbLog2SizeY, valid.CtbLog2SizeY, valid.MinTbLog2SizeY, valid.MaxTbLog2SizeY, valid.MaxTransformHierarchyDepthInter, valid.MaxTransformHierarchyDepthIntra));
        Assert.Equal((false, false, 1, 1), (valid.StillPicture, valid.IntraOnly, valid.MaxDecPicBufferingMinus1, valid.NumShortTermRefPicSets));
        Assert.True(HevcSequenceParameters.Create(CreateSettings(8, 8, 8, 1, SequenceKind.IntraOnly)) is { StillPicture: false, IntraOnly: true, MaxDecPicBufferingMinus1: 0, NumShortTermRefPicSets: 0 });
        Assert.True(HevcSequenceParameters.Create(CreateSettings(8, 8, 8, 1, SequenceKind.Still)) is { StillPicture: true, IntraOnly: true, MaxDecPicBufferingMinus1: 0, NumShortTermRefPicSets: 0 });

        // The sample formats of the decoder: 8, 10 and 12 bits, the four chroma formats
        Assert.False((valid with { BitDepth = 9 }).IsValid);
        Assert.False((valid with { BitDepth = 16 }).IsValid);
        Assert.False((valid with { ChromaFormatIdc = 4 }).IsValid);
        Assert.False((valid with { ChromaSampleLocType = 6 }).IsValid);
        Assert.False((valid with { Width = 0 }).IsValid);
        Assert.False((valid with { Height = -1 }).IsValid);

        // Clause 7.4.3.2.1 and Annex A: coding tree blocks of 16 to 64 samples, a minimum transform block smaller than the
        // minimum coding block, a maximum transform block of at most 32 samples and at most the coding tree block
        Assert.False((valid with { CtbLog2SizeY = 3, MinCbLog2SizeY = 3, MaxTbLog2SizeY = 2 }).IsValid);
        Assert.False((valid with { CtbLog2SizeY = 7 }).IsValid);
        Assert.False((valid with { MinCbLog2SizeY = 6 }).IsValid);
        Assert.False((valid with { MinTbLog2SizeY = 3 }).IsValid);
        Assert.False((valid with { CtbLog2SizeY = 6, MaxTbLog2SizeY = 6 }).IsValid);
        Assert.False((valid with { CtbLog2SizeY = 4 }).IsValid);
        Assert.True((valid with { CtbLog2SizeY = 4, MaxTbLog2SizeY = 4, MaxTransformHierarchyDepthInter = 2, MaxTransformHierarchyDepthIntra = 2 }).IsValid);
        Assert.False((valid with { MaxTransformHierarchyDepthIntra = 4 }).IsValid);
        Assert.False((valid with { MaxTransformHierarchyDepthInter = -1 }).IsValid);

        // Clause A.4.1, item d: level 5 and above need coding tree blocks of 32 or 64 samples; level 8.5 has no limit
        var large = valid with { Width = 4096, Height = 2160 };
        Assert.Equal(150, large.GeneralLevelIdc);
        Assert.True(large.IsValid);
        Assert.False((large with { CtbLog2SizeY = 4, MaxTbLog2SizeY = 4, MaxTransformHierarchyDepthInter = 2, MaxTransformHierarchyDepthIntra = 2 }).IsValid);
        var narrow = valid with { Width = 40000, Height = 8, CtbLog2SizeY = 4, MaxTbLog2SizeY = 4, MaxTransformHierarchyDepthInter = 2, MaxTransformHierarchyDepthIntra = 2 };
        Assert.Equal((255, true), (narrow.GeneralLevelIdc, narrow.GeneralTierFlag));
        Assert.True(narrow.IsValid);

        // The coded size, a multiple of the minimum coding block size, is at most the largest size of the decoder
        Assert.True((valid with { Width = 65528 }).IsValid);
        Assert.False((valid with { Width = 65529 }).IsValid);
        Assert.False((valid with { Height = 65535 }).IsValid);

        // A PCM coding unit has at most 32 samples and at most the bits of the samples
        Assert.False((valid with { CtbLog2SizeY = 6, MinCbLog2SizeY = 6 }).IsValid);
        Assert.True((valid with { CtbLog2SizeY = 6, MinCbLog2SizeY = 6, Tools = NoTools }).IsValid);
        Assert.False((valid with { PcmBitDepth = 9 }).IsValid);
        Assert.True((valid with { PcmBitDepth = 1 }).IsValid);

        // A still picture refers to no other picture
        Assert.False((valid with { StillPicture = true }).IsValid);
    }

    [Fact]
    public void TheLevelIsTheLowestOneOfTableA8ThatHoldsThePictures()
    {
        // Table A.8, MaxLumaPs: 36 864 (level 1), 122 880 (2), 245 760 (2.1), 552 960 (3), 983 040 (3.1), 2 228 224 (4),
        // 8 912 896 (5), 35 651 584 (6), 80 216 064 (6.3) and 142 606 336 (7); general_level_idc is 30 times the level.
        // The width and the height are also at most Sqrt( MaxLumaPs * 8 ): 543, 991, 1402, 2103, 2804, 4222, 8444, 16888,
        // 25332 and 33776
        (int Width, int Height, int LevelIdc)[] cases =
        [
            (8, 8, 30), (176, 144, 30), (192, 192, 30), (200, 192, 60), (352, 288, 60), (640, 360, 63), (960, 540, 90), (1280, 720, 93),
            (1920, 1080, 120), (2048, 1088, 120), (2056, 1088, 150), (4096, 2160, 150), (4096, 2176, 150), (4104, 2176, 180),
            (8192, 4320, 180), (8192, 4352, 180), (8192, 4360, 189), (11264, 7120, 189), (11264, 7128, 210), (16384, 8640, 210),
            (16384, 8704, 210), (16384, 8712, 255), (65528, 65528, 255),

            // A narrow picture is limited by its largest dimension
            (536, 8, 30), (544, 8, 60), (8, 544, 60), (984, 8, 60), (992, 8, 63), (4216, 8, 120), (4224, 8, 150), (8, 8448, 180),
            (16888, 8, 180), (16896, 8, 189), (25328, 8, 189), (25336, 8, 210), (33776, 8, 210), (33784, 8, 255), (8, 65528, 255),
        ];
        foreach (var (width, height, levelIdc) in cases)
        {
            Assert.Equal(levelIdc, HevcLevels.GetLevelIdc(width, height), $"{width}x{height}");

            // The Main tier, and the value 1 that level 8.5 requires (clause A.4.1)
            Assert.Equal(levelIdc is 255, HevcLevels.GetTierFlag(levelIdc));

            // Clause A.4.1, item d: no coding tree block of 16 samples from level 5
            Assert.Equal(levelIdc is >= 150 and < 255, HevcLevels.RequiresLargeCodingTreeBlocks(levelIdc));
        }

        // Equation A-2, maxDpbPicBuf being 6: the pictures of a quarter of the size of the level have 16 buffers, those of
        // half the size 12, those of three quarters 8, and the largest ones 6
        Assert.Equal(16, HevcLevels.GetMaxDpbSize(120, 2228224 / 4));
        Assert.Equal(12, HevcLevels.GetMaxDpbSize(120, (2228224 / 4) + 1));
        Assert.Equal(12, HevcLevels.GetMaxDpbSize(120, 2228224 / 2));
        Assert.Equal(8, HevcLevels.GetMaxDpbSize(120, (2228224 / 2) + 1));
        Assert.Equal(8, HevcLevels.GetMaxDpbSize(120, 2228224 / 4 * 3));
        Assert.Equal(6, HevcLevels.GetMaxDpbSize(120, (2228224 / 4 * 3) + 1));
        Assert.Equal(6, HevcLevels.GetMaxDpbSize(123, 2228224));
        Assert.Equal(16, HevcLevels.GetMaxDpbSize(153, 2228224));
        Assert.Equal(16, HevcLevels.GetMaxDpbSize(255, 1L << 32));
    }

    [Fact]
    public void TheLevelHoldsTheLargestCodedPicture()
    {
        // The limits of the edition V11 (01/2026), computed by hand. Clause A.4.1, item g: a picture fits in the coded
        // picture buffer, CpbVclFactor (Table A.10) x MaxCPB (Table A.8) bits. Clause A.4.2, item g, for the profiles that
        // are not still picture profiles: at most FormatCapabilityFactor x Max( PicSizeInSamplesY, MaxLumaSr / 300 ) /
        // ( MinCrBase x MinCrScaleFactor ) bytes (Tables A.9 and A.10). The Main tier first, then the High tier
        (HevcProfile Profile, int Width, int Height, long Length, int LevelIdc, bool TierFlag)[] cases =
        [
            // Nothing coded yet: the size of the pictures only
            (HevcProfile.Main, 192, 192, 0, 30, false),
            (HevcProfile.MainStillPicture, 8192, 4320, 0, 180, false),

            // Main Still Picture has no minimum compression ratio: 1,000 x 350 bits at level 1, 1,000 x 1,500 at level 2
            (HevcProfile.MainStillPicture, 192, 192, 43_750, 30, false),
            (HevcProfile.MainStillPicture, 192, 192, 43_751, 60, false),

            // Main: 1.5 x 36,864 / 2 bytes at levels 1, 2 and 2.1, 1.5 x 55,296 / 2 at level 3, 1.5 x 110,592 / 2 at level 3.1
            (HevcProfile.Main, 192, 192, 27_648, 30, false),
            (HevcProfile.Main, 192, 192, 27_649, 90, false),
            (HevcProfile.Main, 192, 192, 41_472, 90, false),
            (HevcProfile.Main, 192, 192, 41_473, 93, false),

            // 1920x1080 at level 4 and 4.1: 1.5 x 2,073,600 / 4 bytes. Levels 5 to 6 have a larger MinCrBase (6 and 8) for
            // the same picture: level 6.1 (2,139,095,040 / 300 samples, MinCrBase 8) is the next one that holds more
            (HevcProfile.Main, 1920, 1080, 777_600, 120, false),
            (HevcProfile.Main, 1920, 1080, 777_601, 183, false),
            (HevcProfile.Main, 1920, 1080, 1_336_934, 183, false),
            (HevcProfile.Main, 1920, 1080, 1_336_935, 186, false),

            // Level 7.2 of the Main tier holds 1.5 x 17,112,760,320 / 300 / 6 bytes. The High tier has a smaller
            // MinCrBase (4), but fR is 1 / 960 from level 7: its largest picture is at level 7.2, 1.5 x 17,112,760,320 /
            // 960 / 4 bytes
            (HevcProfile.Main, 1920, 1080, 14_260_633, 216, false),
            (HevcProfile.Main, 1920, 1080, 14_260_634, 255, true),

            // Main 4:4:4 Intra: CpbVclFactor 2,000, FormatCapabilityFactor 3.0, MinCrScaleFactor 0.5
            (HevcProfile.Main444Intra, 192, 192, 87_500, 30, false),
            (HevcProfile.Main444Intra, 192, 192, 87_501, 60, false),
            (HevcProfile.Main444Intra, 192, 192, 110_592, 60, false),
            (HevcProfile.Main444Intra, 192, 192, 110_593, 90, false),

            // Monochrome: CpbVclFactor 667, FormatCapabilityFactor 1.0
            (HevcProfile.Monochrome, 192, 192, 18_432, 30, false),
            (HevcProfile.Monochrome, 192, 192, 18_433, 90, false),

            // A still picture larger than the coded picture buffer of every level of the Main tier (960,000,000 bits at
            // level 7.2): the High tier, 1,600,000,000 bits at level 6.3, then level 8.5
            (HevcProfile.MainStillPicture, 8192, 4320, 120_000_000, 216, false),
            (HevcProfile.MainStillPicture, 8192, 4320, 120_000_001, 189, true),
            (HevcProfile.MainStillPicture, 8192, 4320, 800_000_000, 216, true),
            (HevcProfile.MainStillPicture, 8192, 4320, 800_000_001, 255, true),
        ];
        foreach (var (profile, width, height, length, levelIdc, tierFlag) in cases)
        {
            Assert.Equal((levelIdc, tierFlag), HevcLevels.GetLevel(profile, width, height, length), $"{profile} {width}x{height}, {length} bytes");
        }

        // The parameters of a sequence announce it
        var parameters = HevcSequenceParameters.Create(CreateSettings(192, 192, 8, 1, SequenceKind.Still));
        Assert.Equal(HevcProfile.MainStillPicture, parameters.Profile);
        Assert.Equal((30, false), (parameters.GeneralLevelIdc, parameters.GeneralTierFlag));
        Assert.Equal((60, false), ((parameters with { MaxPictureLength = 43_751 }).GeneralLevelIdc, (parameters with { MaxPictureLength = 43_751 }).GeneralTierFlag));
    }

    [Fact]
    public void PictureParameterSetsAreReadBack()
    {
        var sps420 = ParseSps(HevcSequenceParameters.Create(CreateSettings(64, 64, 8, 1, SequenceKind.Inter)));
        var sps444 = ParseSps(HevcSequenceParameters.Create(CreateSettings(64, 64, 12, 3, SequenceKind.Inter), HevcCodingTools.LargeTransformSkipBlocks));
        (HevcSequenceParameterSet Sps, HevcPictureParameters Parameters)[] cases =
        [
            (sps420, new HevcPictureParameters(26)),
            (sps420, new HevcPictureParameters(0) { PicParameterSetId = 63 }),
            (sps420, new HevcPictureParameters(51) { SignDataHidingEnabled = true, CabacInitPresent = true, ConstrainedIntraPred = true, TransformSkipEnabled = true, TransquantBypassEnabled = true }),
            (sps420, new HevcPictureParameters(30) { CuQpDeltaEnabled = true, DiffCuQpDeltaDepth = 2, CbQpOffset = -12, CrQpOffset = 12, SliceChromaQpOffsetsPresent = true }),
            (sps420, new HevcPictureParameters(37) { CuQpDeltaEnabled = true, CbQpOffset = 5, CrQpOffset = -7 }),
            (sps420, new HevcPictureParameters(22) { DeblockingFilterDisabled = true }),
            (sps420, new HevcPictureParameters(22) { DeblockingFilterOverrideEnabled = true }),
            (sps420, new HevcPictureParameters(22) { BetaOffsetDiv2 = -6, TcOffsetDiv2 = 6, DeblockingFilterOverrideEnabled = true }),
            (sps420, new HevcPictureParameters(22) { BetaOffsetDiv2 = 3 }),
            (sps420, new HevcPictureParameters(22) { TcOffsetDiv2 = -2 }),
            (sps444, new HevcPictureParameters(-24)),
            (sps444, new HevcPictureParameters(10) { TransformSkipEnabled = true, Log2MaxTransformSkipBlockSizeMinus2 = 3 }),
            (sps444, new HevcPictureParameters(10) { CrossComponentPredictionEnabled = true }),
            (sps444, new HevcPictureParameters(10) { Log2SaoOffsetScaleLuma = 2, Log2SaoOffsetScaleChroma = 1 }),
            (sps444, new HevcPictureParameters(10) { TransformSkipEnabled = true, Log2MaxTransformSkipBlockSizeMinus2 = 1, CrossComponentPredictionEnabled = true, Log2SaoOffsetScaleChroma = 2, TransquantBypassEnabled = true }),
        ];
        var scope = CreateScope();
        foreach (var (sps, parameters) in cases)
        {
            var pps = HevcPictureParameterSet.ParseRbsp(Write(output => HevcHeaderWriter.WritePictureParameterSet(output, sps, parameters)));
            var name = parameters.ToString();
            Assert.Equal((parameters.PicParameterSetId, sps.SeqParameterSetId, parameters.InitQp - 26), (pps.PicParameterSetId, pps.SeqParameterSetId, pps.InitQpMinus26), name);
            Assert.Equal((parameters.SignDataHidingEnabled, parameters.CabacInitPresent, parameters.ConstrainedIntraPred), (pps.SignDataHidingEnabledFlag, pps.CabacInitPresentFlag, pps.ConstrainedIntraPredFlag), name);
            Assert.Equal((parameters.TransformSkipEnabled, parameters.Log2MaxTransformSkipBlockSizeMinus2 + 2), (pps.TransformSkipEnabledFlag, pps.Log2MaxTransformSkipSize), name);
            Assert.Equal((parameters.CuQpDeltaEnabled, parameters.DiffCuQpDeltaDepth), (pps.CuQpDeltaEnabledFlag, pps.DiffCuQpDeltaDepth), name);
            Assert.Equal((parameters.CbQpOffset, parameters.CrQpOffset, parameters.SliceChromaQpOffsetsPresent), (pps.CbQpOffset, pps.CrQpOffset, pps.SliceChromaQpOffsetsPresentFlag), name);
            Assert.Equal(parameters.TransquantBypassEnabled, pps.TransquantBypassEnabledFlag, name);
            Assert.Equal(parameters.DeblockingFilterControlPresent, pps.DeblockingFilterControlPresentFlag, name);
            Assert.Equal((parameters.DeblockingFilterOverrideEnabled, parameters.DeblockingFilterDisabled), (pps.DeblockingFilterOverrideEnabledFlag, pps.DeblockingFilterDisabledFlag), name);
            Assert.Equal(parameters.DeblockingFilterDisabled ? (0, 0) : (parameters.BetaOffsetDiv2, parameters.TcOffsetDiv2), (pps.BetaOffsetDiv2, pps.TcOffsetDiv2), name);
            Assert.Equal((parameters.CrossComponentPredictionEnabled, parameters.Log2SaoOffsetScaleLuma, parameters.Log2SaoOffsetScaleChroma), (pps.CrossComponentPredictionEnabledFlag, pps.Log2SaoOffsetScaleLuma, pps.Log2SaoOffsetScaleChroma), name);

            // What the encoder never writes: one reference picture, no tiles, no wavefront, no weighted prediction, one
            // slice of one slice segment, the default scaling lists
            Assert.Equal((0, 0, 1, 1, 2), (pps.NumRefIdxL0DefaultActiveMinus1, pps.NumRefIdxL1DefaultActiveMinus1, pps.NumTileColumns, pps.NumTileRows, pps.Log2ParMrgLevel), name);
            Assert.False(pps.DependentSliceSegmentsEnabledFlag || pps.OutputFlagPresentFlag || pps.WeightedPredFlag || pps.WeightedBipredFlag || pps.TilesEnabledFlag || pps.EntropyCodingSyncEnabledFlag, name);
            Assert.False(pps.LoopFilterAcrossSlicesEnabledFlag || pps.ListsModificationPresentFlag || pps.SliceSegmentHeaderExtensionPresentFlag || pps.ChromaQpOffsetListEnabledFlag, name);
            Assert.Null(pps.ScalingList, name);
            Assert.Equal(0, pps.NumExtraSliceHeaderBits, name);

            // The two parameter sets fit each other
            using var layout = HevcPictureLayout.Create(scope, sps, pps);
            Assert.Equal(sps.CtbLog2SizeY - parameters.DiffCuQpDeltaDepth, layout.Log2MinCuQpDeltaSize, name);
        }
    }

    [Fact]
    public void SliceSegmentHeadersAreReadBack()
    {
        var scope = CreateScope();
        var tools = HevcCodingTools.SampleAdaptiveOffset | HevcCodingTools.TemporalMvp;
        (HevcSequenceParameters Sequence, HevcPictureParameters Picture)[] parameterSets =
        [
            (HevcSequenceParameters.Create(CreateSettings(64, 64, 8, 1, SequenceKind.Inter)), new HevcPictureParameters(26)),
            (HevcSequenceParameters.Create(CreateSettings(40, 24, 10, 0, SequenceKind.Inter), tools), new HevcPictureParameters(30) { CabacInitPresent = true, DeblockingFilterOverrideEnabled = true }),
            (HevcSequenceParameters.Create(CreateSettings(200, 100, 12, 3, SequenceKind.Inter), tools), new HevcPictureParameters(-10) { SliceChromaQpOffsetsPresent = true, CbQpOffset = 3, CrQpOffset = -3, DeblockingFilterOverrideEnabled = true, DeblockingFilterDisabled = true }),
            (HevcSequenceParameters.Create(CreateSettings(16, 16, 8, 2, SequenceKind.Inter), HevcCodingTools.SampleAdaptiveOffset), new HevcPictureParameters(40) { PicParameterSetId = 5, SliceChromaQpOffsetsPresent = true, BetaOffsetDiv2 = 2, TcOffsetDiv2 = -1 }),
            (HevcSequenceParameters.Create(CreateSettings(16, 16, 8, 1, SequenceKind.Still)), new HevcPictureParameters(26)),
            (HevcSequenceParameters.Create(CreateSettings(33, 17, 10, 3, SequenceKind.IntraOnly), tools), new HevcPictureParameters(26) { DeblockingFilterOverrideEnabled = true }),
        ];
        foreach (var (sequence, picture) in parameterSets)
        {
            var sps = ParseSps(sequence);
            var pps = HevcPictureParameterSet.ParseRbsp(Write(output => HevcHeaderWriter.WritePictureParameterSet(output, sps, picture)));
            using var layout = HevcPictureLayout.Create(scope, sps, pps);
            var minQp = -sps.QpBdOffset;
            var slices = new List<HevcSliceParameters>
            {
                HevcSliceParameters.CreateKeyFrame(26),
                HevcSliceParameters.CreateKeyFrame(minQp) with { SliceSaoLuma = true },
                HevcSliceParameters.CreateKeyFrame(51) with { SliceSaoChroma = true, SliceCbQpOffset = -4, SliceCrQpOffset = 4, DeblockingFilterOverride = true, SliceDeblockingFilterDisabled = true },
                HevcSliceParameters.CreateKeyFrame(33) with { SliceSaoLuma = true, SliceSaoChroma = true, DeblockingFilterOverride = true, SliceBetaOffsetDiv2 = -6, SliceTcOffsetDiv2 = 6 },
            };
            if (sps.ShortTermRefPicSets.Length is not 0)
            {
                slices.Add(HevcSliceParameters.CreateInterFrame(1, 26));
                slices.Add(HevcSliceParameters.CreateInterFrame(255, minQp) with { SliceSaoLuma = true, SliceTemporalMvpEnabled = true, CabacInit = true, FiveMinusMaxNumMergeCand = 4 });
                slices.Add(HevcSliceParameters.CreateInterFrame(256, 51) with { SliceSaoChroma = true, FiveMinusMaxNumMergeCand = 2, SliceCbQpOffset = 2, SliceCrQpOffset = -9, DeblockingFilterOverride = true, SliceBetaOffsetDiv2 = 1, SliceTcOffsetDiv2 = -1 });
                slices.Add(HevcSliceParameters.CreateInterFrame(100000, 20) with { CabacInit = true, DeblockingFilterOverride = true, SliceDeblockingFilterDisabled = true });

                // A picture in an I slice that is not a key frame still describes its reference pictures
                slices.Add(new HevcSliceParameters(HevcConstants.NalTrailR, HevcConstants.SliceI, 7, 26));
            }

            foreach (var slice in slices)
            {
                var rbsp = Write(output => HevcHeaderWriter.WriteSliceSegmentHeader(output, sps, pps, slice));
                var nal = HevcNalUnitHeader.Read([(byte)(slice.NalUnitType << 1), 1]);
                Assert.Equal(pps.PicParameterSetId, HevcSliceSegmentHeader.ReadPicParameterSetId(rbsp, nal, out var firstSliceSegmentInPicFlag));
                Assert.True(firstSliceSegmentInPicFlag);
                var header = HevcSliceSegmentHeader.Parse(rbsp, nal, layout, sliceHeader: null);
                var name = $"{sequence.Width}x{sequence.Height} 4:{sequence.ChromaFormatIdc} {slice}";

                // The header ends on a byte, where the slice segment data start
                Assert.Equal(rbsp.Length, header.DataOffset, name);
                Assert.Equal((true, false, false, 0), (header.FirstSliceSegmentInPicFlag, header.NoOutputOfPriorPicsFlag, header.DependentSliceSegmentFlag, header.SliceSegmentAddress), name);
                Assert.Equal((pps.PicParameterSetId, slice.SliceType, slice.SliceQp), (header.SlicePicParameterSetId, header.SliceType, header.SliceQpY), name);
                Assert.True(header.PicOutputFlag, name);
                var isIdr = slice.NalUnitType is HevcConstants.NalIdrNLp;
                if (isIdr)
                {
                    Assert.Null(header.ShortTermRefPicSet, name);
                    Assert.Equal((0, 0), (header.SlicePicOrderCntLsb, header.NumPicTotalCurr), name);
                }
                else
                {
                    // The picture before the current one, from the sequence parameter set
                    Assert.Equal((slice.PicOrderCnt & 255, true, 0, 1, 0), (header.SlicePicOrderCntLsb, header.ShortTermRefPicSetSpsFlag, header.ShortTermRefPicSetIdx, header.NumPicTotalCurr, header.NumLongTermRefPics), name);
                    Assert.Same(sps.ShortTermRefPicSets[0], header.ShortTermRefPicSet, name);
                }

                var hasSao = sps.SampleAdaptiveOffsetEnabledFlag;
                Assert.Equal((hasSao && slice.SliceSaoLuma, hasSao && sps.ChromaArrayType is not 0 && slice.SliceSaoChroma), (header.SliceSaoLumaFlag, header.SliceSaoChromaFlag), name);
                Assert.Equal(!isIdr && sps.SpsTemporalMvpEnabledFlag && slice.SliceTemporalMvpEnabled, header.SliceTemporalMvpEnabledFlag, name);
                if (slice.SliceType is HevcConstants.SliceP)
                {
                    var cabacInit = pps.CabacInitPresentFlag && slice.CabacInit;
                    Assert.Equal((0, cabacInit, cabacInit ? 2 : 1, 5 - slice.FiveMinusMaxNumMergeCand), (header.NumRefIdxL0ActiveMinus1, header.CabacInitFlag, header.InitType, header.MaxNumMergeCand), name);
                    Assert.Equal((true, 0), (header.CollocatedFromL0Flag, header.CollocatedRefIdx), name);
                    Assert.False(header.RefPicListModificationFlagL0, name);
                    Assert.Null(header.PredWeightTable, name);
                }
                else
                {
                    Assert.Equal(0, header.InitType, name);
                }

                Assert.Equal(pps.SliceChromaQpOffsetsPresentFlag ? (slice.SliceCbQpOffset, slice.SliceCrQpOffset) : (0, 0), (header.SliceCbQpOffset, header.SliceCrQpOffset), name);

                // The deblocking filter: the values of the picture parameter set, unless the slice overrides them
                var overrides = pps.DeblockingFilterOverrideEnabledFlag && slice.DeblockingFilterOverride;
                Assert.Equal(overrides, header.DeblockingFilterOverrideFlag, name);
                Assert.Equal(overrides ? slice.SliceDeblockingFilterDisabled : pps.DeblockingFilterDisabledFlag, header.SliceDeblockingFilterDisabledFlag, name);
                var offsets = !overrides ? (pps.BetaOffsetDiv2, pps.TcOffsetDiv2) : slice.SliceDeblockingFilterDisabled ? (pps.BetaOffsetDiv2, pps.TcOffsetDiv2) : (slice.SliceBetaOffsetDiv2, slice.SliceTcOffsetDiv2);
                Assert.Equal(offsets, (header.SliceBetaOffsetDiv2, header.SliceTcOffsetDiv2), name);
                Assert.Empty(header.EntryPointOffsetMinus1, name);
            }
        }
    }

    [Fact]
    public void TheDecodedPictureHashIsReadBack()
    {
        var md5 = Enumerable.Range(0, 48).Select(i => (byte)((i * 37) + 1)).ToArray();

        // payloadType 132, payloadSize 1 + 3 * 16 = 49, hash_type 0, the three digests, then the stop bit on its byte
        var rbsp = Write(output => HevcHeaderWriter.WriteDecodedPictureHash(output, new HevcDecodedPictureHash(HevcDecodedPictureHash.Md5, 3, md5, new uint[3])));
        Assert.Equal("843100" + Convert.ToHexString(md5) + "80", Convert.ToHexString(rbsp));
        var messages = new HevcSeiMessages();
        HevcSeiReader.Read(rbsp, isPrefix: false, componentCount: 3, messages);
        var hash = messages.DecodedPictureHash;
        Assert.NotNull(hash);
        Assert.Equal((HevcDecodedPictureHash.Md5, 3), (hash.HashType, hash.ComponentCount));
        for (var cIdx = 0; cIdx < 3; cIdx++)
        {
            Assert.Equal(md5[(cIdx * 16)..((cIdx + 1) * 16)], hash.GetMd5(cIdx).ToArray());
        }

        // One component, and the two other kinds of hash: 16 bits and 32 bits per component
        rbsp = Write(output => HevcHeaderWriter.WriteDecodedPictureHash(output, new HevcDecodedPictureHash(HevcDecodedPictureHash.Md5, 1, md5[..16], new uint[1])));
        Assert.Equal("841100" + Convert.ToHexString(md5.AsSpan(0, 16)) + "80", Convert.ToHexString(rbsp));
        rbsp = Write(output => HevcHeaderWriter.WriteDecodedPictureHash(output, new HevcDecodedPictureHash(HevcDecodedPictureHash.Crc, 3, [], [0xABCD, 0x0001, 0xFFFF])));
        Assert.Equal("840701ABCD0001FFFF80", Convert.ToHexString(rbsp));
        rbsp = Write(output => HevcHeaderWriter.WriteDecodedPictureHash(output, new HevcDecodedPictureHash(HevcDecodedPictureHash.Checksum, 1, [], [0xDEADBEEF])));
        Assert.Equal("840502DEADBEEF80", Convert.ToHexString(rbsp));
        messages = new HevcSeiMessages();
        HevcSeiReader.Read(rbsp, isPrefix: false, componentCount: 1, messages);
        Assert.Equal((HevcDecodedPictureHash.Checksum, 0xDEADBEEF), (messages.DecodedPictureHash!.HashType, messages.DecodedPictureHash.GetValue(0)));
    }

    // Annex A: the profile, the tier and the level of a sequence, from the tables of the specification
    private static void AssertProfileTierLevel(HevcProfileTierLevel actual, in HevcEncoderSettings settings, in HevcSequenceParameters parameters, SequenceKind kind, string name)
    {
        var extendedTools = (parameters.Tools & HevcCodingTools.ExtendedTools) is not HevcCodingTools.None;
        var still = kind is SequenceKind.Still;
        var intra = kind is not SequenceKind.Inter;
        var expected = (settings.ChromaFormatIdc, settings.BitDepth) switch
        {
            // Clause A.3.5: the 4:4:4 profiles are the only ones for 4:4:4 samples and for the tools of the range
            // extensions; only the 8-bit one has a still picture profile
            _ when extendedTools || settings.ChromaFormatIdc is 3 => settings.BitDepth switch
            {
                8 => still ? "Main 4:4:4 Still Picture" : intra ? "Main 4:4:4 Intra" : "Main 4:4:4",
                10 => intra ? "Main 4:4:4 10 Intra" : "Main 4:4:4 10",
                _ => intra ? "Main 4:4:4 12 Intra" : "Main 4:4:4 12",
            },
            (0, 8) => "Monochrome",
            (0, 10) => "Monochrome 10",
            (0, _) => "Monochrome 12",

            // Clauses A.3.2 to A.3.4
            (1, 8) => still ? "Main Still Picture" : "Main",
            (1, 10) => still ? "Main 10 Still Picture" : "Main 10",
            (1, _) => intra ? "Main 12 Intra" : "Main 12",
            (_, 12) => intra ? "Main 4:2:2 12 Intra" : "Main 4:2:2 12",
            _ => intra ? "Main 4:2:2 10 Intra" : "Main 4:2:2 10",
        };
        name += ": " + expected;
        Assert.Equal(expected, GetProfileName(parameters.Profile), name);
        Assert.Equal(0, actual.GeneralProfileSpace, name);

        // The source is progressive frames, without frame packing: 1 0 1 1; general_inbld_flag is 0
        var constraints = actual.GeneralConstraintIndicatorFlags;
        Assert.Equal(0b1011UL, constraints >> 44, name);
        Assert.True(actual.GeneralProgressiveSourceFlag && !actual.GeneralInterlacedSourceFlag, name);
        Assert.Equal(0UL, constraints & 1, name);
        var flags = Convert.ToString((long)((constraints >> 35) & 0x1FF), 2).PadLeft(9, '0');
        switch (expected)
        {
            case "Main":
                // Clause A.3.2: general_profile_idc 1; a Main bitstream is also a Main 10 bitstream
                Assert.Equal((1, 0b0110u << 28), (actual.GeneralProfileIdc, actual.GeneralProfileCompatibilityFlags), name);
                Assert.Equal(0UL, constraints & ((1UL << 44) - 1), name);
                break;

            case "Main 10":
                Assert.Equal((2, 0b0010u << 28), (actual.GeneralProfileIdc, actual.GeneralProfileCompatibilityFlags), name);
                Assert.Equal(0UL, constraints & ((1UL << 44) - 1), name);
                break;

            case "Main 10 Still Picture":
                // Clause A.3.3: Main 10 with general_one_picture_only_constraint_flag, the eighth bit after the four
                // source flags
                Assert.Equal((2, 0b0010u << 28), (actual.GeneralProfileIdc, actual.GeneralProfileCompatibilityFlags), name);
                Assert.Equal(1UL << 36, constraints & ((1UL << 44) - 1), name);
                break;

            case "Main Still Picture":
                // Clause A.3.4: general_profile_idc 3; also a Main and a Main 10 bitstream
                Assert.Equal((3, 0b0111u << 28), (actual.GeneralProfileIdc, actual.GeneralProfileCompatibilityFlags), name);
                Assert.Equal(0UL, constraints & ((1UL << 44) - 1), name);
                break;

            default:
                // Clause A.3.5: general_profile_idc 4 and one row of Table A.2; the 34 bits that follow are reserved
                var row = RangeExtensionsProfiles[expected];
                Assert.Equal((4, 0b00001u << 27), (actual.GeneralProfileIdc, actual.GeneralProfileCompatibilityFlags), name);
                Assert.Equal(row.Flags.Replace('x', '1'), flags, name);
                Assert.Equal(0UL, constraints & ((1UL << 35) - 1), name);

                // Table A.1: what the profile allows
                Assert.Contains((char)('0' + settings.ChromaFormatIdc), row.ChromaFormats, message: name);
                Assert.True(settings.BitDepth <= row.MaxBitDepth, name);
                Assert.True(row.ExtendedTools || !extendedTools, name);
                break;
        }

        // The profiles of the first version have 8-bit or 10-bit 4:2:0 samples and no range extension
        if (actual.GeneralProfileIdc is not 4)
        {
            Assert.True(settings.ChromaFormatIdc is 1 && settings.BitDepth <= (actual.GeneralProfileIdc is 2 ? 10 : 8) && !extendedTools, name);
        }

        // A still picture profile holds one picture, and an intra profile holds no picture that refers to another one:
        // sps_max_dec_pic_buffering_minus1 is 0
        if (expected.Contains("Still", StringComparison.Ordinal))
        {
            Assert.True(still && parameters.MaxDecPicBufferingMinus1 is 0, name);
        }

        if (expected.Contains("Intra", StringComparison.Ordinal))
        {
            Assert.True(intra && parameters.MaxDecPicBufferingMinus1 is 0, name);
        }

        // The level of the coded picture size, in the Main tier
        var levelIdc = HevcLevels.GetLevelIdc(parameters.PicWidthInLumaSamples, parameters.PicHeightInLumaSamples);
        Assert.Equal((levelIdc, levelIdc is 255), (actual.GeneralLevelIdc, actual.GeneralTierFlag), name);
        Assert.True(parameters.MaxDecPicBufferingMinus1 + 1 <= HevcLevels.GetMaxDpbSize(levelIdc, (long)parameters.PicWidthInLumaSamples * parameters.PicHeightInLumaSamples), name);
    }

    private static string GetProfileName(HevcProfile profile) => profile switch
    {
        HevcProfile.Main => "Main",
        HevcProfile.Main10 => "Main 10",
        HevcProfile.Main10StillPicture => "Main 10 Still Picture",
        HevcProfile.MainStillPicture => "Main Still Picture",
        HevcProfile.Monochrome => "Monochrome",
        HevcProfile.Monochrome10 => "Monochrome 10",
        HevcProfile.Monochrome12 => "Monochrome 12",
        HevcProfile.Main12 => "Main 12",
        HevcProfile.Main42210 => "Main 4:2:2 10",
        HevcProfile.Main42212 => "Main 4:2:2 12",
        HevcProfile.Main444 => "Main 4:4:4",
        HevcProfile.Main44410 => "Main 4:4:4 10",
        HevcProfile.Main44412 => "Main 4:4:4 12",
        HevcProfile.Main12Intra => "Main 12 Intra",
        HevcProfile.Main42210Intra => "Main 4:2:2 10 Intra",
        HevcProfile.Main42212Intra => "Main 4:2:2 12 Intra",
        HevcProfile.Main444Intra => "Main 4:4:4 Intra",
        HevcProfile.Main44410Intra => "Main 4:4:4 10 Intra",
        HevcProfile.Main44412Intra => "Main 4:4:4 12 Intra",
        HevcProfile.Main444StillPicture => "Main 4:4:4 Still Picture",
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };

    private static HevcEncoderSettings CreateSettings(int width, int height, int bitDepth, int chromaFormatIdc, SequenceKind kind)
    {
        return new HevcEncoderSettings(width, height, bitDepth, chromaFormatIdc, ColourPrimaries: 1, TransferCharacteristics: 13, MatrixCoeffs: 6, FullRange: true, ChromaSampleLocType: 0, StillPicture: kind is SequenceKind.Still, Lossless: false, Quality: 75, Effort: 5, KeyFrameInterval: kind switch
        {
            SequenceKind.IntraOnly => 1,
            SequenceKind.Inter => 30,
            _ => 0,
        });
    }

    private static HevcSequenceParameterSet ParseSps(in HevcSequenceParameters parameters)
    {
        var copy = parameters;
        return HevcSequenceParameterSet.ParseRbsp(Write(output => HevcHeaderWriter.WriteSequenceParameterSet(output, copy)));
    }

    private static AllocationScope CreateScope() => AllocationScope.Create(new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 1L << 30 } }, nameof(HevcHeaderWriterTests));

    private static byte[] Write(Action<PooledPayloadWriter> action)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(HevcHeaderWriterTests));
        using var output = new PooledPayloadWriter(scope);
        action(output);
        return output.WrittenSpan.ToArray();
    }
}
