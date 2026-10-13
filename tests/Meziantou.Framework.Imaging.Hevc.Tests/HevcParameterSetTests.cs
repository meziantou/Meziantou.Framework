using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Tests.Conformance.Hardening;
using Meziantou.Framework.Imaging.TestHarness.Hevc;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Parameter sets and slice segment headers (Rec. ITU-T H.265, clauses 7.3.2 and 7.3.6). The expected values of the streams
/// of the encoders are those that the <c>trace_headers</c> bitstream filter of FFmpeg prints for them; the other parameter
/// sets are written bit by bit by the tests.
/// </summary>
public sealed class HevcParameterSetTests
{
    private sealed record SpsOptions
    {
        public int ChromaFormatIdc { get; init; } = 1;

        public bool SeparateColourPlaneFlag { get; init; }

        public uint Width { get; init; } = 64;

        public uint Height { get; init; } = 48;

        public (uint Left, uint Right, uint Top, uint Bottom)? ConformanceWindow { get; init; }

        public uint BitDepthLumaMinus8 { get; init; }

        public uint BitDepthChromaMinus8 { get; init; }

        public uint Log2MinCbMinus3 { get; init; }

        public uint Log2DiffMaxMinCb { get; init; } = 1;

        public uint Log2MinTbMinus2 { get; init; }

        public uint Log2DiffMaxMinTb { get; init; } = 2;

        public uint SeqParameterSetId { get; init; }

        /// <summary>The nine flags of the range extension, in the order of the syntax; or <see langword="null"/>.</summary>
        public string? RangeExtension { get; init; }

        public bool MultilayerExtension { get; init; }

        public bool SccExtension { get; init; }

        public uint Extension4Bits { get; init; }
    }

    private static AllocationScope CreateScope() => AllocationScope.Create(ImageConfiguration.Default, nameof(HevcParameterSetTests));

    // Clause 7.3.2.2.1: a sequence parameter set of one sub-layer, without scaling list, PCM, reference picture set or VUI
    private static byte[] WriteSps(SpsOptions options)
    {
        var writer = new HevcTestBitWriter();
        writer.U(4, 0).U(3, 0).Flag(true);

        // profile_tier_level( 1, 0 ): the Main profile, level 3
        writer.U(2, 0).Flag(false).U(5, 1).U(32, 0x60000000).U(4, 0b1001).U(43, 0).Flag(false).U(8, 90);
        writer.UE(options.SeqParameterSetId).UE((uint)options.ChromaFormatIdc);
        if (options.ChromaFormatIdc is 3)
        {
            writer.Flag(options.SeparateColourPlaneFlag);
        }

        writer.UE(options.Width).UE(options.Height).Flag(options.ConformanceWindow is not null);
        if (options.ConformanceWindow is { } window)
        {
            writer.UE(window.Left).UE(window.Right).UE(window.Top).UE(window.Bottom);
        }

        writer.UE(options.BitDepthLumaMinus8).UE(options.BitDepthChromaMinus8);

        // log2_max_pic_order_cnt_lsb_minus4, the ordering information of the sub-layer
        writer.UE(4).Flag(true).UE(1).UE(0).UE(0);
        writer.UE(options.Log2MinCbMinus3).UE(options.Log2DiffMaxMinCb).UE(options.Log2MinTbMinus2).UE(options.Log2DiffMaxMinTb);

        // max_transform_hierarchy_depth_inter and intra, scaling_list_enabled_flag, amp_enabled_flag,
        // sample_adaptive_offset_enabled_flag, pcm_enabled_flag
        writer.UE(1).UE(2).Flag(false).Flag(true).Flag(true).Flag(false);

        // num_short_term_ref_pic_sets, long_term_ref_pics_present_flag, sps_temporal_mvp_enabled_flag,
        // strong_intra_smoothing_enabled_flag, vui_parameters_present_flag
        writer.UE(0).Flag(false).Flag(true).Flag(true).Flag(false);
        var hasExtension = options.RangeExtension is not null || options.MultilayerExtension || options.SccExtension || options.Extension4Bits is not 0;
        writer.Flag(hasExtension);
        if (hasExtension)
        {
            writer.Flag(options.RangeExtension is not null).Flag(options.MultilayerExtension).Flag(false).Flag(options.SccExtension).U(4, options.Extension4Bits);
        }

        foreach (var flag in options.RangeExtension ?? "")
        {
            writer.Flag(flag is '1');
        }

        if (options.Extension4Bits is not 0)
        {
            // sps_extension_data_flag
            writer.U(13, 0x1ABC);
        }

        return writer.ToNalUnit(HevcConstants.NalSps);
    }

    // Clause 7.3.2.3.1: a picture parameter set with tiles that are not uniformly spaced
    private static byte[] WritePps(uint[] columnWidthMinus1, uint[] rowHeightMinus1)
    {
        var writer = new HevcTestBitWriter();
        writer.UE(3).UE(0).Flag(true).Flag(false).U(3, 0).Flag(true).Flag(false).UE(0).UE(0).SE(-4).Flag(false).Flag(true);

        // cu_qp_delta_enabled_flag and diff_cu_qp_delta_depth, the chroma offsets and the flags up to tiles_enabled_flag
        writer.Flag(true).UE(1).SE(-3).SE(5).Flag(true).Flag(false).Flag(false).Flag(true).Flag(true).Flag(false);
        writer.UE((uint)columnWidthMinus1.Length).UE((uint)rowHeightMinus1.Length).Flag(false);
        foreach (var value in columnWidthMinus1)
        {
            writer.UE(value);
        }

        foreach (var value in rowHeightMinus1)
        {
            writer.UE(value);
        }

        // loop_filter_across_tiles_enabled_flag, pps_loop_filter_across_slices_enabled_flag, the deblocking control
        writer.Flag(false).Flag(true).Flag(true).Flag(true).Flag(false).SE(2).SE(-6);

        // pps_scaling_list_data_present_flag, lists_modification_present_flag, log2_parallel_merge_level_minus2,
        // slice_segment_header_extension_present_flag, pps_extension_present_flag
        writer.Flag(false).Flag(false).UE(0).Flag(false).Flag(false);
        return writer.ToNalUnit(HevcConstants.NalPps);
    }

    private static List<ReadOnlyMemory<byte>> GetNalUnits(byte[] stream, int nalUnitType)
    {
        return [.. ReferenceHevcStream.SplitAnnexB(stream).Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) == nalUnitType)];
    }

    [Fact]
    public void ASequenceParameterSetWrittenByTheTestIsRead()
    {
        var sps = HevcSequenceParameterSet.Parse(CreateScope(), WriteSps(new SpsOptions { ConformanceWindow = (1, 2, 3, 4), SeqParameterSetId = 5 }));
        Assert.Equal((5, 1, 8, 64, 48), (sps.SeqParameterSetId, sps.ChromaFormatIdc, sps.BitDepth, sps.PicWidthInLumaSamples, sps.PicHeightInLumaSamples));
        Assert.Equal((2, 2, 3, 0), (sps.SubWidthC, sps.SubHeightC, sps.ComponentCount, sps.QpBdOffset));

        // The offsets of the window are in chroma samples: 2 * 1 and 2 * 3 luma samples, 64 - 2 * 3 by 48 - 2 * 7
        Assert.Equal((2, 6, 58, 34), (sps.OutputLeft, sps.OutputTop, sps.OutputWidth, sps.OutputHeight));
        Assert.Equal((3, 4, 2, 4), (sps.MinCbLog2SizeY, sps.CtbLog2SizeY, sps.MinTbLog2SizeY, sps.MaxTbLog2SizeY));
        Assert.Equal((4, 3, 12), (sps.PicWidthInCtbsY, sps.PicHeightInCtbsY, sps.PicSizeInCtbsY));
        Assert.Equal((1, 2, 8), (sps.MaxTransformHierarchyDepthInter, sps.MaxTransformHierarchyDepthIntra, sps.Log2MaxPicOrderCntLsb));
        Assert.Equal((1, 0), (sps.MaxDecPicBufferingMinus1[0], sps.MaxNumReorderPics[0]));
        Assert.True(sps is { AmpEnabledFlag: true, SampleAdaptiveOffsetEnabledFlag: true, PcmEnabledFlag: false, ScalingListEnabledFlag: false, StrongIntraSmoothingEnabledFlag: true, SpsTemporalMvpEnabledFlag: true });
        Assert.Null(sps.Vui);
        Assert.Equal((1, 90, 0x60000000u), (sps.ProfileTierLevel.GeneralProfileIdc, sps.ProfileTierLevel.GeneralLevelIdc, sps.ProfileTierLevel.GeneralProfileCompatibilityFlags));
        Assert.True(sps.ProfileTierLevel is { GeneralProgressiveSourceFlag: true, GeneralInterlacedSourceFlag: false });

        // Monochrome, 12 bits, coding tree blocks of 64 samples; the bit depth of the chroma samples it does not have is ignored
        var monochrome = HevcSequenceParameterSet.Parse(CreateScope(), WriteSps(new SpsOptions { ChromaFormatIdc = 0, BitDepthLumaMinus8 = 4, BitDepthChromaMinus8 = 1, Log2DiffMaxMinCb = 3, Width = 72, Height = 8 }));
        Assert.Equal((0, 12, 1, 1, 1, 24, 6), (monochrome.ChromaFormatIdc, monochrome.BitDepth, monochrome.SubWidthC, monochrome.SubHeightC, monochrome.ComponentCount, monochrome.QpBdOffset, monochrome.CtbLog2SizeY));
        Assert.Equal((2, 1), (monochrome.PicWidthInCtbsY, monochrome.PicHeightInCtbsY));

        // The range extension, and extension data that a decoder skips
        var extended = HevcSequenceParameterSet.Parse(CreateScope(), WriteSps(new SpsOptions { ChromaFormatIdc = 3, RangeExtension = "101101110", Extension4Bits = 0b0101 }));
        Assert.True(extended is { TransformSkipRotationEnabledFlag: true, TransformSkipContextEnabledFlag: false, ImplicitRdpcmEnabledFlag: true, ExplicitRdpcmEnabledFlag: true });
        Assert.True(extended is { IntraSmoothingDisabledFlag: true, HighPrecisionOffsetsEnabledFlag: true, PersistentRiceAdaptationEnabledFlag: true, SubWidthC: 1, SubHeightC: 1 });
    }

    [Fact]
    public void FeaturesThatAreNotSupportedAreNamed()
    {
        AssertUnsupported(new SpsOptions { ChromaFormatIdc = 3, SeparateColourPlaneFlag = true }, "separate colour planes");
        AssertUnsupported(new SpsOptions { BitDepthLumaMinus8 = 5, BitDepthChromaMinus8 = 5 }, "a bit depth of more than 12 bits");
        AssertUnsupported(new SpsOptions { BitDepthLumaMinus8 = 2 }, "luma and chroma samples of different bit depths");
        AssertUnsupported(new SpsOptions { RangeExtension = "000010000" }, "extended precision processing");
        AssertUnsupported(new SpsOptions { RangeExtension = "000000001" }, "CABAC bypass alignment");
        AssertUnsupported(new SpsOptions { MultilayerExtension = true }, "multilayer extension");
        AssertUnsupported(new SpsOptions { SccExtension = true }, "screen content coding extension");
        AssertUnsupported(new SpsOptions { Log2DiffMaxMinCb = 0 }, "coding tree blocks of 8 samples");
        AssertUnsupported(new SpsOptions { Log2MinCbMinus3 = 3, Log2DiffMaxMinCb = 1, Width = 128, Height = 128 }, "coding tree blocks of 128 samples");
        AssertUnsupported(new SpsOptions { Width = 65536 }, "pictures of more than 65535 samples in a dimension");

        static void AssertUnsupported(SpsOptions options, string feature)
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => HevcSequenceParameterSet.Parse(CreateScope(), WriteSps(options)));
            Assert.Equal((ImageFormat.Heic, feature), (exception.Format, exception.Feature));
        }
    }

    [Fact]
    public void ASequenceParameterSetThatBreaksAConstraintIsInvalid()
    {
        AssertInvalid(WriteSps(new SpsOptions { Width = 0 }));
        AssertInvalid(WriteSps(new SpsOptions { Width = 60 }));
        AssertInvalid(WriteSps(new SpsOptions { SeqParameterSetId = 16 }));
        AssertInvalid(WriteSps(new SpsOptions { ChromaFormatIdc = 4 }));
        AssertInvalid(WriteSps(new SpsOptions { BitDepthLumaMinus8 = 9, BitDepthChromaMinus8 = 9 }));

        // The minimum transform block is as large as the minimum coding block; the maximum one is larger than 32 samples
        AssertInvalid(WriteSps(new SpsOptions { Log2MinTbMinus2 = 1, Log2DiffMaxMinTb = 0 }));
        AssertInvalid(WriteSps(new SpsOptions { Log2MinCbMinus3 = 1, Log2DiffMaxMinCb = 2, Log2MinTbMinus2 = 0, Log2DiffMaxMinTb = 4 }));

        // The conformance cropping window leaves no sample: 2 * ( 16 + 16 ) columns of 64
        AssertInvalid(WriteSps(new SpsOptions { ConformanceWindow = (16, 16, 0, 0) }));

        // Another type of NAL unit
        var valid = WriteSps(new SpsOptions());
        var pps = (byte[])valid.Clone();
        pps[0] = HevcConstants.NalPps << 1;
        AssertInvalid(pps);

        // Every truncation, and the trailing bits: a missing stop bit, a byte after them
        for (var length = 0; length < valid.Length; length++)
        {
            AssertInvalid(valid[..length]);
        }

        AssertInvalid([.. valid, 0x80]);
        AssertInvalid([.. valid[..^1], (byte)(valid[^1] & (valid[^1] - 1))]);

        static void AssertInvalid(byte[] nalUnit)
        {
            var exception = Assert.Throws<InvalidImageContentException>(() => HevcSequenceParameterSet.Parse(CreateScope(), nalUnit));
            Assert.Equal(ImageFormat.Heic, exception.Format);
        }
    }

    [Fact]
    public void TheParameterSetsOfAnEncoderAreRead()
    {
        var stream = HevcTestStreams.X265Rext444TenBits;
        var vps = HevcVideoParameterSet.Parse(ReferenceHevcStream.GetRbsp(GetNalUnits(stream, HevcConstants.NalVps)[0].Span));
        Assert.Equal((0, 0, 0), (vps.VideoParameterSetId, vps.MaxLayersMinus1, vps.MaxSubLayersMinus1));
        Assert.Equal((4, 30), (vps.ProfileTierLevel.GeneralProfileIdc, vps.ProfileTierLevel.GeneralLevelIdc));

        var spsNalUnit = GetNalUnits(stream, HevcConstants.NalSps)[0].ToArray();
        var sps = HevcSequenceParameterSet.Parse(CreateScope(), spsNalUnit);
        Assert.Equal((0, 3, 10, 24, 24), (sps.SeqParameterSetId, sps.ChromaFormatIdc, sps.BitDepth, sps.PicWidthInLumaSamples, sps.PicHeightInLumaSamples));
        Assert.Equal((0, 5, 0, 7), (sps.ConfWinLeftOffset, sps.ConfWinRightOffset, sps.ConfWinTopOffset, sps.ConfWinBottomOffset));
        Assert.Equal((0, 0, 19, 17), (sps.OutputLeft, sps.OutputTop, sps.OutputWidth, sps.OutputHeight));
        Assert.Equal((3, 4, 2, 4), (sps.MinCbLog2SizeY, sps.CtbLog2SizeY, sps.MinTbLog2SizeY, sps.MaxTbLog2SizeY));
        Assert.Equal((0, 0, 8, 2, 0), (sps.MaxTransformHierarchyDepthInter, sps.MaxTransformHierarchyDepthIntra, sps.Log2MaxPicOrderCntLsb, sps.MaxDecPicBufferingMinus1[0], sps.MaxNumReorderPics[0]));
        Assert.True(sps is { ScalingListEnabledFlag: false, AmpEnabledFlag: false, SampleAdaptiveOffsetEnabledFlag: true, PcmEnabledFlag: false, StrongIntraSmoothingEnabledFlag: true });
        Assert.True(sps.ShortTermRefPicSets.IsEmpty);
        Assert.Equal((4, 30, false), (sps.ProfileTierLevel.GeneralProfileIdc, sps.ProfileTierLevel.GeneralLevelIdc, sps.ProfileTierLevel.GeneralTierFlag));
        Assert.NotNull(sps.Vui);
        Assert.False(sps.Vui.VideoFullRangeFlag);

        // The independent reader of the test harness reads the same sample arrays
        Assert.Equal(new ReferenceHevcSequence(24, 24, 3, 10, 10, 0, 0, 19, 17), ReferenceHevcStream.ReadSequenceParameterSet(spsNalUnit));

        var pps = HevcPictureParameterSet.ParseRbsp(ReferenceHevcStream.GetRbsp(GetNalUnits(stream, HevcConstants.NalPps)[0].Span));
        Assert.Equal((0, 0, 0, 6), (pps.PicParameterSetId, pps.SeqParameterSetId, pps.InitQpMinus26, pps.CbQpOffset));
        Assert.True(pps is { SignDataHidingEnabledFlag: true, TransformSkipEnabledFlag: true, CuQpDeltaEnabledFlag: false, TilesEnabledFlag: false, EntropyCodingSyncEnabledFlag: false, NumTileColumns: 1, NumTileRows: 1 });

        // The slice segment header: SliceQpY is 26 + init_qp_minus26 + slice_qp_delta, and slice_qp_delta is -5
        using var layout = HevcPictureLayout.Create(CreateScope(), sps, pps);
        var slice = GetNalUnits(stream, HevcConstants.NalIdrNLp).Concat(GetNalUnits(stream, HevcConstants.NalIdrWRadl)).Single();
        var header = HevcSliceSegmentHeader.Parse(ReferenceHevcStream.GetRbsp(slice.Span), HevcNalUnitHeader.Read(slice.Span), layout, sliceHeader: null);
        Assert.Equal((HevcConstants.SliceI, 21, 0, 0), (header.SliceType, header.SliceQpY, header.SliceSegmentAddress, header.InitType));
        Assert.True(header is { FirstSliceSegmentInPicFlag: true, DependentSliceSegmentFlag: false, SliceSaoLumaFlag: true, SliceSaoChromaFlag: true, PicOutputFlag: true, ShortTermRefPicSet: null });
        Assert.True(header.EntryPointOffsetMinus1.IsEmpty);
    }

    [Fact]
    public void TheSliceSegmentsOfAPictureAreRead()
    {
        // Two slices of three coding tree blocks, each one in slice segments of two coding tree blocks
        var stream = HevcTestStreams.HmSlices420;
        var sps = HevcSequenceParameterSet.Parse(CreateScope(), GetNalUnits(stream, HevcConstants.NalSps)[0].Span);
        var pps = HevcPictureParameterSet.ParseRbsp(ReferenceHevcStream.GetRbsp(GetNalUnits(stream, HevcConstants.NalPps)[0].Span));
        Assert.True(pps.DependentSliceSegmentsEnabledFlag);
        using var layout = HevcPictureLayout.Create(CreateScope(), sps, pps);
        var headers = new List<HevcSliceSegmentHeader>();
        foreach (var unit in ReferenceHevcStream.SplitAnnexB(stream).Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) < 32))
        {
            headers.Add(HevcSliceSegmentHeader.Parse(ReferenceHevcStream.GetRbsp(unit.Span), HevcNalUnitHeader.Read(unit.Span), layout, headers.LastOrDefault()));
        }

        Assert.Equal(new[] { 0, 2, 3, 5 }, headers.Select(header => header.SliceSegmentAddress).ToArray());
        Assert.Equal(new[] { 0, 0, 3, 3 }, headers.Select(header => header.SliceAddrRs).ToArray());
        Assert.Equal(new[] { false, true, false, true }, headers.Select(header => header.DependentSliceSegmentFlag).ToArray());
        Assert.All(headers, header => Assert.Equal((40, HevcConstants.SliceI, true), (header.SliceQpY, header.SliceType, header.SliceSaoLumaFlag)));

        // A dependent slice segment needs the slice it belongs to
        var dependent = ReferenceHevcStream.SplitAnnexB(stream).Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) < 32).ElementAt(1);
        Assert.Throws<InvalidImageContentException>(() => HevcSliceSegmentHeader.Parse(ReferenceHevcStream.GetRbsp(dependent.Span), HevcNalUnitHeader.Read(dependent.Span), layout, sliceHeader: null));

        // The wavefront: one entry point, for the second row of coding tree blocks
        stream = HevcTestStreams.HmWavefront420;
        sps = HevcSequenceParameterSet.Parse(CreateScope(), GetNalUnits(stream, HevcConstants.NalSps)[0].Span);
        pps = HevcPictureParameterSet.ParseRbsp(ReferenceHevcStream.GetRbsp(GetNalUnits(stream, HevcConstants.NalPps)[0].Span));
        Assert.True(pps.EntropyCodingSyncEnabledFlag);
        using var wavefront = HevcPictureLayout.Create(CreateScope(), sps, pps);
        var slice = ReferenceHevcStream.SplitAnnexB(stream).Single(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) < 32);
        var header = HevcSliceSegmentHeader.Parse(ReferenceHevcStream.GetRbsp(slice.Span), HevcNalUnitHeader.Read(slice.Span), wavefront, sliceHeader: null);
        Assert.Equal(new[] { 186 }, header.EntryPointOffsetMinus1.ToArray());
    }

    [Fact]
    public void TilesDefineTheScanOrderOfTheCodingTreeBlocks()
    {
        // 4x3 coding tree blocks of 16 samples; tile columns of 1 and 3 blocks, tile rows of 2 and 1 blocks
        using var audit = new PoolAudit();
        var scope = new AllocationScope(ImageResourceLimits.Default, audit.Pool, nameof(TilesDefineTheScanOrderOfTheCodingTreeBlocks));
        var sps = HevcSequenceParameterSet.Parse(scope, WriteSps(new SpsOptions()));
        var pps = HevcPictureParameterSet.ParseRbsp(ReferenceHevcStream.GetRbsp(WritePps([0], [1])));
        Assert.Equal((3, 0, -4, -3, 5, 1), (pps.PicParameterSetId, pps.SeqParameterSetId, pps.InitQpMinus26, pps.CbQpOffset, pps.CrQpOffset, pps.DiffCuQpDeltaDepth));
        Assert.True(pps is { DependentSliceSegmentsEnabledFlag: true, SignDataHidingEnabledFlag: true, TransformSkipEnabledFlag: true, CuQpDeltaEnabledFlag: true, SliceChromaQpOffsetsPresentFlag: true, TransquantBypassEnabledFlag: true });
        Assert.True(pps is { TilesEnabledFlag: true, EntropyCodingSyncEnabledFlag: false, NumTileColumns: 2, NumTileRows: 2, UniformSpacingFlag: false, LoopFilterAcrossTilesEnabledFlag: false, LoopFilterAcrossSlicesEnabledFlag: true });
        Assert.True(pps is { DeblockingFilterOverrideEnabledFlag: true, DeblockingFilterDisabledFlag: false, BetaOffsetDiv2: 2, TcOffsetDiv2: -6, Log2ParMrgLevel: 2, Log2MaxTransformSkipSize: 2 });
        using (var layout = HevcPictureLayout.Create(scope, sps, pps))
        {
            // The tiles in raster scan: the first one has the blocks 0 and 4, the second one the blocks 1, 2, 3, 5, 6 and 7
            Assert.Equal(new[] { 0, 1, 4 }, layout.ColBd.ToArray());
            Assert.Equal(new[] { 0, 2, 3 }, layout.RowBd.ToArray());
            Assert.Equal(new[] { 0, 2, 3, 4, 1, 5, 6, 7, 8, 9, 10, 11 }, layout.CtbAddrRsToTs.ToArray());
            Assert.Equal(new[] { 0, 4, 1, 2, 3, 5, 6, 7, 8, 9, 10, 11 }, layout.CtbAddrTsToRs.ToArray());
            Assert.Equal(new[] { 0, 0, 1, 1, 1, 1, 1, 1, 2, 3, 3, 3 }, layout.TileId.ToArray());
            Assert.Equal(3, layout.Log2MinCuQpDeltaSize);
            Assert.True(layout.ScalingFactors.IsEmpty);

            // Equation 6-10: the minimum transform block ( 5, 2 ) is in the block 1 of the raster scan, the block 2 of
            // the tile scan, at ( 1, 2 ) in it: 2 * 16 + 1 + 2 * 4
            Assert.Equal(41, layout.GetMinTbAddrZs(5, 2));
            Assert.Equal(0, layout.GetMinTbAddrZs(0, 0));
            Assert.Equal((1 * 16) + 15, layout.GetMinTbAddrZs(3, 7));
        }

        // The tiles do not fit the picture: 4 columns of coding tree blocks cannot hold tile columns of 4 and 1
        var wide = HevcPictureParameterSet.ParseRbsp(ReferenceHevcStream.GetRbsp(WritePps([3], [0])));
        Assert.Throws<InvalidImageContentException>(() => HevcPictureLayout.Create(scope, sps, wide));
        Assert.Null(audit.Verify());

        // More tile columns than a level allows
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => HevcPictureParameterSet.ParseRbsp(ReferenceHevcStream.GetRbsp(WritePps(new uint[20], [0]))));
        Assert.Equal("more than 20 tile columns or 22 tile rows", exception.Feature);
    }
}
