namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The recipes of the vectors HM encodes (TAppEncoder): what neither x265 nor kvazaar writes (PCM, the coding tools of the
// range extensions, reference picture sets in the sequence parameter set, slice-level deblocking and chroma QP offsets,
// conformance windows with a left and a top offset), the reference encoder's choices for the common tools, and the
// streams the library rejects.
internal static partial class HevcCorpus
{
    /// <summary>The block structure: the CTB size, the depth of the coding quadtree (the minimum coding block is the CTB
    /// size shifted by the depth minus one) and the transform block sizes (logarithms).</summary>
    private static string[] HmBlocks(int ctb, int depth, int maxTb = 5, int minTb = 2, int tuDepth = 3) =>
    [
        Inv($"--MaxCUWidth={ctb}"), Inv($"--MaxCUHeight={ctb}"), Inv($"--MaxPartitionDepth={depth}"), Inv($"--QuadtreeTULog2MaxSize={maxTb}"), Inv($"--QuadtreeTULog2MinSize={minTb}"),
        Inv($"--QuadtreeTUMaxDepthInter={tuDepth}"), Inv($"--QuadtreeTUMaxDepthIntra={tuDepth}"),
    ];

    /// <summary>PCM coding units of 8x8 to 32x32 samples: at a very low QP the encoder prefers the raw samples of a
    /// noisy block to its coded residual.</summary>
    private static string[] HmPcm(bool inputBitDepth, bool loopFilterDisabled, int maxSize = 5) =>
        ["--PCMEnabledFlag=1", Inv($"--PCMLog2MaxSize={maxSize}"), "--PCMLog2MinSize=3", Inv($"--PCMInputBitDepthFlag={(inputBitDepth ? 1 : 0)}"), Inv($"--PCMFilterDisableFlag={(loopFilterDisabled ? 1 : 0)}"), "--QP=1"];

    /// <summary>Every coding unit bypasses the transform, the quantization and the filters.</summary>
    private static readonly string[] HmLossless = ["--TransquantBypassEnable=1", "--CUTransquantBypassFlagForce=1", "--CostMode=lossless"];

    /// <summary>The coding tools of the range extensions that apply to every chroma format.</summary>
    private static readonly string[] HmRangeExtensionTools =
    [
        "--TransformSkip=1", "--TransformSkipFast=0", "--TransformSkipLog2MaxSize=4", "--ImplicitResidualDPCM=1", "--ExplicitResidualDPCM=1", "--ResidualRotation=1", "--SingleSignificanceMapContext=1",
        "--GolombRiceParameterAdaptation=1",
    ];

    /// <summary>VUI parameters; TAppEncoder only writes them with a target bit rate.</summary>
    private static string[] HmVui(params string[] options) => ["--VuiParametersPresent=1", "--TargetBitrate=100000", .. options];

    private static IEnumerable<HevcVector> HmVectors()
    {
        // The profiles of version 1
        yield return HmIntra("hm-intra-main-68x52", Noise(TestSrc2("68x52"), 8, 201), "yuv420p", 2, [], "hevc.profile=main", "hevc.encoder=hm", "hevc.crop=bottom", "hevc.crop=right");
        yield return HmIntra("hm-intra-main10-68x52", Noise(TestSrc2("68x52"), 8, 202), "yuv420p10le", 1, [], "hevc.profile=main10", "hevc.bitDepth=10").Hash("crc");
        yield return HmIntra("hm-intra-main-still-picture-66x50", Noise(Mandelbrot("66x50"), 8, 203), "yuv420p", 1, ["--Profile=main-still-picture"], "hevc.profile=mainStillPicture").Hash("checksum");
        yield return Hm("hm-low-delay-p-main-64x64", Moving("64x64"), "yuv420p", 4, HmLowDelayP, "hevc.profile=main", "hevc.sliceTypes=P", "hevc.rps=sps");
        yield return Hm("hm-low-delay-b-main10-64x64", Rotating("64x64"), "yuv420p10le", 5, HmLowDelayB, "hevc.profile=main10", "hevc.sliceTypes=B", "hevc.refs=2").Hash("crc");

        // Block structure: CTBs of 16, 32 and 64 samples, minimum coding blocks of 8 to 64, transform blocks of 4 to 32
        yield return HmIntra("hm-intra-ctb16-cb8-72x56", Noise(TestSrc2("72x56"), 8, 204), "yuv420p", 1, HmBlocks(16, 2, maxTb: 4), "hevc.ctb=16", "hevc.minCb=8", "hevc.maxTb=16");
        yield return HmIntra("hm-intra-ctb16-cb16-64x48", Noise(Mandelbrot("64x48"), 8, 205), "yuv420p", 1, HmBlocks(16, 1, maxTb: 4, minTb: 3, tuDepth: 2), "hevc.ctb=16", "hevc.minCb=16", "hevc.minTb=8").Hash("checksum");
        yield return HmIntra("hm-intra-ctb32-cb8-100x76", Noise(TestSrc2("100x76"), 8, 206), "yuv420p", 1, HmBlocks(32, 3), "hevc.ctb=32", "hevc.minCb=8", "hevc.maxTb=32");
        yield return HmIntra("hm-intra-ctb32-cb32-96x64", Noise(Mandelbrot("96x64"), 8, 207), "yuv420p", 1, HmBlocks(32, 1, maxTb: 5, minTb: 4, tuDepth: 2), "hevc.ctb=32", "hevc.minCb=32", "hevc.minTb=16").Hash("crc");
        yield return HmIntra("hm-intra-ctb64-cb16-136x72", Noise(TestSrc2("136x72"), 8, 208), "yuv420p", 1, HmBlocks(64, 3, tuDepth: 4), "hevc.ctb=64", "hevc.minCb=16", "hevc.tuDepthIntra=3");
        yield return HmIntra("hm-intra-ctb64-cb64-128x128", Noise(Mandelbrot("128x128"), 8, 209), "yuv420p", 1, HmBlocks(64, 1, maxTb: 5, minTb: 5, tuDepth: 1), "hevc.ctb=64", "hevc.minCb=64", "hevc.minTb=32", "hevc.tuDepthIntra=0");
        yield return HmIntra("hm-intra-tb8-64x48", Noise(TestSrc("64x48"), 8, 210), "yuv420p", 1, HmBlocks(16, 2, maxTb: 3, minTb: 2, tuDepth: 2), "hevc.maxTb=8", "hevc.minTb=4");
        yield return HmIntra("hm-intra-tb4-only-48x40", Noise(TestSrc("48x40"), 8, 211), "yuv420p", 1, HmBlocks(16, 2, maxTb: 2, minTb: 2, tuDepth: 1), "hevc.maxTb=4", "hevc.minTb=4");
        yield return Hm("hm-inter-ctb16-80x56", Rotating("80x56"), "yuv420p", 4, [.. HmLowDelayB, .. HmBlocks(16, 2, maxTb: 4)], "hevc.ctb=16", "hevc.sliceTypes=B");
        yield return Hm("hm-inter-ctb32-tu-depth1-96x64", Moving("96x64"), "yuv420p", 4, [.. HmLowDelayP, .. HmBlocks(32, 2, tuDepth: 1)], "hevc.ctb=32", "hevc.tuDepthInter=0", "hevc.minCb=16");
        yield return Hm("hm-inter-ctb64-tu-depth4-136x72", Rotating("136x72"), "yuv420p", 4, [.. HmLowDelayB, .. HmBlocks(64, 4, tuDepth: 4)], "hevc.ctb=64", "hevc.tuDepthInter=3", "hevc.amp");

        // The intra and inter tools the parameter sets switch
        yield return HmIntra("hm-intra-strong-smoothing-off-128x64", Mandelbrot("128x64"), "yuv420p", 1, ["--StrongIntraSmoothing=0", "--QP=36"], "hevc.strongIntraSmoothing=0");
        yield return HmIntra("hm-intra-strong-smoothing-on-10bit-128x64", Mandelbrot("128x64"), "yuv420p10le", 1, ["--StrongIntraSmoothing=1", "--QP=38"], "hevc.strongIntraSmoothing=1", "hevc.bitDepth=10");
        yield return HmIntra("hm-intra-tskip-signhide-off-64x48", Noise(TestSrc("64x48"), 25, 212), "yuv420p", 1, ["--TransformSkip=1", "--TransformSkipFast=0", "--SignHideFlag=0", "--QP=24"], "hevc.transformSkip", "hevc.signDataHiding=0");
        yield return Hm("hm-inter-constrained-intra-64x64", Rotating("64x64"), "yuv420p", 5, [.. HmLowDelayP, "--ConstrainedIntraPred=1"], "hevc.constrainedIntraPred");
        yield return Hm("hm-inter-no-amp-merge1-64x64", Moving("64x64"), "yuv420p", 4, [.. HmLowDelayB, "--AMP=0", "--MaxNumMergeCand=1"], "hevc.mergeCand=1");
        yield return Hm("hm-inter-amp-merge2-par-merge-136x72", Rotating("136x72"), "yuv420p", 4, [.. HmLowDelayB, "--AMP=1", "--MaxNumMergeCand=2", "--Log2ParallelMergeLevel=4"], "hevc.amp", "hevc.mergeCand=2", "hevc.parallelMergeLevel=4");
        yield return Hm("hm-inter-par-merge-ctb-128x64", Moving("128x64"), "yuv420p", 4, [.. HmLowDelayB, "--MaxNumMergeCand=5", "--Log2ParallelMergeLevel=6"], "hevc.mergeCand=5", "hevc.parallelMergeLevel=6");
        yield return Hm("hm-inter-par-merge-8-444-64x64", Rotating("64x64"), "yuv444p", 4, [.. HmLowDelayB, "--MaxNumMergeCand=3", "--Log2ParallelMergeLevel=3"], "hevc.mergeCand=3", "hevc.parallelMergeLevel=3", "hevc.chroma=4:4:4");
        yield return Hm("hm-inter-tmvp-off-64x64", Moving("64x64"), "yuv420p", 5, [.. HmLowDelayB, "--TMVPMode=0"], "hevc.temporalMvp=0");
        yield return Hm("hm-inter-ref4-64x64", Rotating("64x64"), "yuv420p", 7, ["--IntraPeriod=-1", "--GOPSize=1", "--Frame1=B 1 0 0.0 0.0 0 0 1.0 0 0 0 4 4 -1 -2 -3 -4 0"], "hevc.refs=4", "hevc.sliceTypes=B");
        yield return Hm("hm-weighted-p-64x64", Fading("64x64"), "yuv420p", 5, [.. HmLowDelayP, "--WeightedPredP=1"], "hevc.weightedPred=p", "hevc.weights=luma", "hevc.weights=chroma");
        yield return Hm("hm-weighted-b-10bit-64x64", Fading("64x64"), "yuv420p10le", 5, [.. HmLowDelayB, "--WeightedPredB=1"], "hevc.weightedPred=b", "hevc.weights=luma", "hevc.bitDepth=10");
        yield return Hm("hm-weighted-p-422-10bit-64x64", Fading("64x64"), "yuv422p10le", 5, [.. HmLowDelayP, "--WeightedPredP=1"], "hevc.weightedPred=p", "hevc.weights=chroma", "hevc.chroma=4:2:2");
        yield return Hm("hm-weighted-b-444-12bit-64x64", Fading("64x64"), "yuv444p12le", 5, [.. HmLowDelayB, "--WeightedPredB=1"], "hevc.weightedPred=b", "hevc.weights=chroma", "hevc.chroma=4:4:4");
        yield return Hm("hm-weighted-p-400-8bit-64x64", Fading("64x64"), "gray", 5, [.. HmLowDelayP, "--WeightedPredP=1"], "hevc.weightedPred=p", "hevc.weights=luma", "hevc.chroma=4:0:0");

        // Groups of pictures: hierarchical B pictures in temporal layers, with CRA and RASL pictures or with IDR and RADL
        // pictures; the reference picture sets of the sequence parameter set, one predicted from the other
        yield return Hm("hm-random-access-cra-64x64", Moving("64x64"), "yuv420p", 13, HmRandomAccess(1), "hevc.nal=CRA", "hevc.nal=RASL_N", "hevc.nal=RASL_R", "hevc.nal=TSA_N", "hevc.temporalLayers=3", "hevc.rps.interPrediction", "hevc.rps=sps", "hevc.collocatedFromL1");
        yield return Hm("hm-random-access-idr-64x64", Moving("64x64"), "yuv420p", 13, HmRandomAccess(2), "hevc.nal=IDR_W_RADL", "hevc.nal=RADL_N", "hevc.nal=RADL_R", "hevc.temporalLayers=3", "hevc.sequences=2").Hash("crc");
        yield return Hm("hm-random-access-cra-422-10bit-64x64", Rotating("64x64"), "yuv422p10le", 13, HmRandomAccess(1), "hevc.nal=CRA", "hevc.nal=RASL_N", "hevc.chroma=4:2:2", "hevc.temporalLayers=3");
        yield return Hm("hm-random-access-cra-444-12bit-64x64", Rotating("64x64"), "yuv444p12le", 9, HmRandomAccess(1), "hevc.nal=CRA", "hevc.chroma=4:4:4", "hevc.bitDepth=12").Hash("checksum");
        yield return Hm("hm-random-access-qcif-176x144", Moving("176x144"), "yuv420p", 17, [.. HmRandomAccess(1), "--QP=34"], "hevc.frames=17", "hevc.nal=CRA", "hevc.ctbs=3x3");

        // Slices, dependent slice segments, wavefronts
        yield return Hm("hm-slices-64x64", Moving("64x64"), "yuv420p", 3, [.. HmLowDelayP, .. HmBlocks(16, 2, maxTb: 4), "--SliceMode=1", "--SliceArgument=6"], "hevc.slices=3");
        yield return Hm("hm-slices-no-filter-across-64x64", Moving("64x64"), "yuv420p", 3, [.. HmLowDelayP, .. HmBlocks(16, 2, maxTb: 4), "--SliceMode=1", "--SliceArgument=5", "--LFCrossSliceBoundaryFlag=0", "--SAO=0"], "hevc.slices=4", "hevc.loopFilterAcrossSlices=0", "hevc.sao=off");
        yield return Hm("hm-slices-no-filter-across-sao-64x64", Noise(Moving("64x64"), 12, 213), "yuv420p", 3, [.. HmLowDelayP, .. HmBlocks(16, 2, maxTb: 4), "--SliceMode=1", "--SliceArgument=5", "--LFCrossSliceBoundaryFlag=0", "--QP=38"], "hevc.slices=4", "hevc.loopFilterAcrossSlices=0", "hevc.sao=luma");
        yield return Hm("hm-slices-bytes-64x64", Noise(Moving("64x64"), 10, 214), "yuv420p", 3, [.. HmLowDelayP, .. HmBlocks(16, 2, maxTb: 4), "--SliceMode=2", "--SliceArgument=120"], "hevc.slices");
        yield return Hm("hm-dependent-slices-64x64", Moving("64x64"), "yuv420p", 3, [.. HmLowDelayP, .. HmBlocks(16, 2, maxTb: 4), "--SliceMode=1", "--SliceArgument=8", "--SliceSegmentMode=1", "--SliceSegmentArgument=3"], "hevc.slices=2", "hevc.dependentSlices");
        yield return Hm("hm-dependent-slices-444-10bit-64x64", Rotating("64x64"), "yuv444p10le", 3, [.. HmLowDelayB, .. HmBlocks(16, 2, maxTb: 4), "--SliceSegmentMode=1", "--SliceSegmentArgument=5"], "hevc.dependentSlices", "hevc.chroma=4:4:4");
        yield return Hm("hm-wpp-ctb16-64x64", Moving("64x64"), "yuv420p", 3, [.. HmLowDelayP, .. HmBlocks(16, 2, maxTb: 4), "--WaveFrontSynchro=1"], "hevc.wpp", "hevc.ctbs=4x4");
        yield return Hm("hm-wpp-one-ctb-wide-16x64", Moving("16x64"), "yuv420p", 3, [.. HmLowDelayP, .. HmBlocks(16, 2, maxTb: 4), "--WaveFrontSynchro=1"], "hevc.wpp", "hevc.ctbs=1x4");
        yield return Hm("hm-wpp-two-ctbs-wide-422-10bit-32x64", Rotating("32x64"), "yuv422p10le", 3, [.. HmLowDelayB, .. HmBlocks(16, 2, maxTb: 4), "--WaveFrontSynchro=1"], "hevc.wpp", "hevc.ctbs=2x4", "hevc.chroma=4:2:2");
        yield return Hm("hm-wpp-dependent-slices-64x64", Moving("64x64"), "yuv420p", 3, [.. HmLowDelayP, .. HmBlocks(16, 2, maxTb: 4), "--WaveFrontSynchro=1", "--SliceSegmentMode=1", "--SliceSegmentArgument=4"], "hevc.wpp", "hevc.dependentSlices=4");
        yield return Hm("hm-wpp-slices-ctb32-444-96x96", Rotating("96x96"), "yuv444p", 3, [.. HmLowDelayB, .. HmBlocks(32, 3), "--WaveFrontSynchro=1", "--SliceMode=1", "--SliceArgument=4"], "hevc.wpp", "hevc.slices=3", "hevc.chroma=4:4:4");

        // Tiles (at least 256x64 luma samples each), with and without filtering across their boundaries, with a slice
        // per tile, with slice segments that end inside a tile
        yield return HmIntra("hm-tiles-2x1-ctb16-512x64", Noise(TestSrc2("512x64"), 6, 215), "yuv420p", 1, [.. HmBlocks(16, 2, maxTb: 4), "--TileUniformSpacing=1", "--NumTileColumnsMinus1=1", "--LFCrossTileBoundaryFlag=1", "--QP=34"],
            "hevc.tiles=2x1", "hevc.tiles.loopFilterAcross=1", "hevc.ctb=16");
        yield return Hm("hm-tiles-2x2-inter-512x128", Moving("512x128"), "yuv420p", 3, [.. HmLowDelayP, "--TileUniformSpacing=1", "--NumTileColumnsMinus1=1", "--NumTileRowsMinus1=1", "--LFCrossTileBoundaryFlag=0", "--QP=36"],
            "hevc.tiles=2x2", "hevc.tiles.loopFilterAcross=0", "hevc.frameTypes=inter");
        yield return Hm("hm-tiles-explicit-ctb32-832x64", Noise(Moving("832x64"), 6, 216), "yuv420p", 2, [.. HmLowDelayB, .. HmBlocks(32, 3), "--TileUniformSpacing=0", "--NumTileColumnsMinus1=2", "--TileColumnWidthArray=8,10", "--QP=36"],
            "hevc.tiles=3x1", "hevc.tiles.spacing=explicit", "hevc.ctb=32");
        yield return Hm("hm-tiles-slices-512x128", Moving("512x128"), "yuv420p", 2, [.. HmLowDelayP, "--TileUniformSpacing=1", "--NumTileColumnsMinus1=1", "--NumTileRowsMinus1=1", "--SliceMode=3", "--SliceArgument=1", "--QP=36"],
            "hevc.tiles=2x2", "hevc.slices=4");
        yield return Hm("hm-tiles-dependent-slices-ctb16-512x64", Moving("512x64"), "yuv420p", 2, [.. HmLowDelayP, .. HmBlocks(16, 2, maxTb: 4), "--TileUniformSpacing=1", "--NumTileColumnsMinus1=1", "--SliceMode=3", "--SliceArgument=1", "--SliceSegmentMode=1", "--SliceSegmentArgument=24", "--QP=36"],
            "hevc.tiles=2x1", "hevc.slices=2", "hevc.dependentSlices");

        // Quantization: negative QPs above 8 bits, QP deltas, chroma QP offsets in the picture parameter set and in the
        // slice headers
        yield return HmIntra("hm-intra-qp-minus12-10bit-48x48", Noise(TestSrc2("48x48"), 10, 217), "yuv420p10le", 1, ["--QP=-12"], "hevc.sliceQp=-12", "hevc.bitDepth=10");
        yield return HmIntra("hm-intra-qp-minus24-444-12bit-48x48", Noise(TestSrc2("48x48"), 10, 218), "yuv444p12le", 1, ["--QP=-24"], "hevc.sliceQp=-24", "hevc.bitDepth=12");
        yield return HmIntra("hm-intra-qp51-422-64x64", Noise(TestSrc2("64x64"), 10, 219), "yuv422p", 1, ["--QP=51"], "hevc.sliceQp=51", "hevc.chroma=4:2:2");
        yield return Hm("hm-inter-qp-offsets-64x64", Moving("64x64"), "yuv420p", 4, ["--IntraPeriod=-1", "--GOPSize=1", "--Frame1=P 1 3 0.0 0.0 0 0 1.0 0 0 0 1 1 -1 0", "--QP=28"], "hevc.sliceQp=28", "hevc.sliceQp=31");
        yield return Hm("hm-cu-qp-delta-depth0-136x72", Noise(Moving("136x72"), 10, 220), "yuv420p", 3, [.. HmLowDelayP, "--MaxDeltaQP=3", "--MaxCuDQPDepth=0"], "hevc.cuQpDelta", "hevc.qgSize=64");
        yield return Hm("hm-cu-qp-delta-depth1-136x72", Noise(Moving("136x72"), 10, 221), "yuv420p", 3, [.. HmLowDelayB, "--MaxDeltaQP=3", "--MaxCuDQPDepth=1"], "hevc.cuQpDelta", "hevc.qgSize=32");
        yield return Hm("hm-cu-qp-delta-depth2-422-10bit-72x72", Noise(Moving("72x72"), 10, 222), "yuv422p10le", 3, [.. HmLowDelayP, "--MaxDeltaQP=4", "--MaxCuDQPDepth=2"], "hevc.cuQpDelta", "hevc.qgSize=16", "hevc.chroma=4:2:2");
        yield return Hm("hm-cu-qp-delta-depth3-444-12bit-72x72", Noise(Moving("72x72"), 10, 223), "yuv444p12le", 3, [.. HmLowDelayP, "--MaxDeltaQP=4", "--MaxCuDQPDepth=3"], "hevc.cuQpDelta", "hevc.qgSize=8", "hevc.chroma=4:4:4");
        yield return HmIntra("hm-adaptive-qp-136x72", Noise(TestSrc2("136x72"), 14, 224), "yuv420p", 1, ["--AdaptiveQP=1", "--MaxQPAdaptationRange=6", "--MaxCuDQPDepth=2"], "hevc.cuQpDelta", "hevc.qgSize=16");
        yield return Hm("hm-chroma-qp-offsets-slice-64x64", Moving("64x64"), "yuv420p", 4, [.. HmLowDelayP, "--CbQpOffset=3", "--CrQpOffset=-3", "--SliceChromaQPOffsetPeriodicity=1", "--SliceCbQpOffsetIntraOrPeriodic=2", "--SliceCrQpOffsetIntraOrPeriodic=-2"],
            "hevc.chromaQpOffset=3/-3", "hevc.sliceChromaQpOffset");
        yield return Hm("hm-chroma-qp-offsets-gop-422-64x64", Moving("64x64"), "yuv422p", 4, ["--IntraPeriod=-1", "--GOPSize=1", "--Frame1=P 1 1 0.0 0.0 -4 5 1.0 0 0 0 1 1 -1 0", "--CbQpOffset=-5", "--CrQpOffset=4"], "hevc.chromaQpOffset=-5/4", "hevc.chroma=4:2:2");

        // Scaling lists: the default lists, and lists of a file in the sequence and in the picture parameter set
        yield return HmIntra("hm-scaling-default-64x64", Noise(TestSrc2("64x64"), 10, 225), "yuv420p", 1, ["--ScalingList=1"], "hevc.scalingList=default");
        yield return Hm("hm-scaling-default-444-10bit-64x64", Noise(Moving("64x64"), 10, 226), "yuv444p10le", 3, [.. HmLowDelayP, "--ScalingList=1"], "hevc.scalingList=default", "hevc.chroma=4:4:4");
        yield return HmIntra("hm-scaling-file-64x64", Noise(TestSrc2("64x64"), 10, 227), "yuv420p", 1, ["--ScalingList=2", "--ScalingListFile={file}"], "hevc.scalingList", "hevc.scalingList.coding=explicit") with { File = ScalingLists(5, copies: false) };
        yield return Hm("hm-scaling-file-copies-64x64", Noise(Moving("64x64"), 10, 228), "yuv420p", 3, [.. HmLowDelayB, "--ScalingList=2", "--ScalingListFile={file}"], "hevc.scalingList", "hevc.scalingList.coding=copy") with { File = ScalingLists(6, copies: true) };
        yield return Hm("hm-scaling-file-default-4x4-444-12bit-64x64", Noise(Moving("64x64"), 10, 294), "yuv444p12le", 3, [.. HmLowDelayP, "--ScalingList=2", "--ScalingListFile={file}"], "hevc.scalingList", "hevc.scalingList.coding=default", "hevc.chroma=4:4:4")
            with { File = ScalingLists(10, copies: true, defaults: true) };
        yield return HmIntra("hm-scaling-file-422-10bit-64x64", Noise(TestSrc2("64x64"), 10, 229), "yuv422p10le", 1, ["--ScalingList=2", "--ScalingListFile={file}"], "hevc.scalingList", "hevc.chroma=4:2:2") with { File = ScalingLists(7, copies: false) };

        // In-loop filters: deblocking off, with offsets in the picture parameter set, with offsets and a switch in the
        // slice headers; sample adaptive offset off, and with its offsets scaled (range extensions)
        yield return Hm("hm-deblock-off-64x64", Moving("64x64"), "yuv420p", 3, [.. HmLowDelayP, "--LoopFilterDisable=1", "--QP=36"], "hevc.deblocking=0");
        yield return Hm("hm-deblock-pps-offsets-64x64", Moving("64x64"), "yuv420p", 3, [.. HmLowDelayP, "--LoopFilterOffsetInPPS=1", "--LoopFilterBetaOffset_div2=3", "--LoopFilterTcOffset_div2=-2", "--QP=38"], "hevc.deblocking.offsets=3/-2");
        yield return Hm("hm-deblock-slice-offsets-64x64", Moving("64x64"), "yuv420p", 4, ["--IntraPeriod=-1", "--GOPSize=1", "--Frame1=P 1 0 0.0 0.0 0 0 1.0 3 -4 0 1 1 -1 0", "--LoopFilterOffsetInPPS=0", "--LoopFilterBetaOffset_div2=2", "--LoopFilterTcOffset_div2=1", "--QP=38"],
            "hevc.deblocking.sliceOverride", "hevc.deblocking.offsets=-2/4", "hevc.deblocking.offsets=2/1");
        yield return Hm("hm-sao-off-64x64", Moving("64x64"), "yuv420p", 3, [.. HmLowDelayP, "--SAO=0", "--QP=38"], "hevc.sao=off");
        yield return HmIntra("hm-sao-offset-scale-12bit-72x56", Noise(TestSrc2("72x56"), 18, 230), "yuv420p12le", 1, ["--SaoLumaOffsetBitShift=2", "--SaoChromaOffsetBitShift=1", "--QP=40"], "hevc.saoOffsetScale=2/1", "hevc.bitDepth=12");
        yield return Hm("hm-sao-offset-scale-444-12bit-72x56", Noise(Moving("72x56"), 18, 231), "yuv444p12le", 3, [.. HmLowDelayP, "--SaoLumaOffsetBitShift=1", "--SaoChromaOffsetBitShift=2", "--QP=40"], "hevc.saoOffsetScale=1/2", "hevc.chroma=4:4:4");

        // PCM: raw samples in the stream, at the bit depth of the stream or below it, filtered or not
        yield return HmIntra("hm-pcm-8bit-34x38", Noise(TestSrc2("34x38"), 30, 232), "yuv420p", 1, HmPcm(inputBitDepth: true, loopFilterDisabled: true), "hevc.pcm", "hevc.pcm.loopFilterDisabled", "hevc.pcm.bitDepth=8/8", "hevc.pcm.size=8-32");
        yield return HmIntra("hm-pcm-filtered-8bit-64x48", Noise(TestSrc2("64x48"), 30, 233), "yuv420p", 1, HmPcm(inputBitDepth: true, loopFilterDisabled: false), "hevc.pcm", "hevc.pcm.bitDepth=8/8").Hash("crc");
        yield return HmIntra("hm-pcm-8bit-in-10bit-48x48", Noise(TestSrc2("48x48"), 30, 234), "yuv420p10le", 1, [.. HmPcm(inputBitDepth: true, loopFilterDisabled: false), "--InputBitDepth=8", "--InternalBitDepth=10", "--Profile=main10"], "hevc.pcm", "hevc.pcm.bitDepth=8/8", "hevc.bitDepth=10")
            with { InputPixelFormat = "yuv420p" };
        yield return HmIntra("hm-pcm-10bit-422-48x48", Noise(TestSrc2("48x48"), 30, 235), "yuv422p10le", 1, HmPcm(inputBitDepth: true, loopFilterDisabled: true, maxSize: 4), "hevc.pcm", "hevc.pcm.bitDepth=10/10", "hevc.pcm.size=8-16", "hevc.chroma=4:2:2");
        yield return HmIntra("hm-pcm-8bit-in-12bit-444-48x48", Noise(TestSrc2("48x48"), 30, 236), "yuv444p12le", 1, [.. HmPcm(inputBitDepth: true, loopFilterDisabled: true), "--InputBitDepth=8", "--InternalBitDepth=12"], "hevc.pcm", "hevc.pcm.bitDepth=8/8", "hevc.bitDepth=12", "hevc.chroma=4:4:4")
            with { InputPixelFormat = "yuv444p" };
        yield return HmIntra("hm-pcm-12bit-400-48x48", Noise(TestSrc2("48x48"), 30, 237), "gray12le", 1, HmPcm(inputBitDepth: true, loopFilterDisabled: false, maxSize: 3), "hevc.pcm", "hevc.pcm.size=8-8", "hevc.chroma=4:0:0").Hash("checksum");
        yield return Hm("hm-pcm-inter-64x64", Noise(Moving("64x64"), 30, 238), "yuv420p", 3, [.. HmLowDelayP, .. HmPcm(inputBitDepth: true, loopFilterDisabled: true)], "hevc.pcm", "hevc.frameTypes=inter");
        yield return Hm("hm-pcm-inter-ctb16-444-48x48", Noise(Rotating("48x48"), 30, 239), "yuv444p", 3, [.. HmLowDelayB, .. HmBlocks(16, 2, maxTb: 4), .. HmPcm(inputBitDepth: true, loopFilterDisabled: false, maxSize: 4)], "hevc.pcm", "hevc.ctb=16", "hevc.chroma=4:4:4");

        // Lossless, for every coding unit and for the ones the encoder chooses
        yield return HmIntra("hm-lossless-intra-34x38", Noise(TestSrc2("34x38"), 10, 240), "yuv420p", 1, HmLossless, "hevc.lossless");
        yield return Hm("hm-lossless-inter-64x48", Noise(Moving("64x48"), 6, 241), "yuv420p", 3, [.. HmLowDelayB, .. HmLossless], "hevc.lossless", "hevc.frameTypes=inter");
        yield return Hm("hm-lossless-inter-422-10bit-64x48", Noise(Moving("64x48"), 6, 242), "yuv422p10le", 3, [.. HmLowDelayP, .. HmLossless], "hevc.lossless", "hevc.chroma=4:2:2");
        yield return HmIntra("hm-lossless-intra-ctb16-400-12bit-50x50", Noise(TestSrc2("50x50"), 10, 243), "gray12le", 1, [.. HmLossless, .. HmBlocks(16, 2, maxTb: 4)], "hevc.lossless", "hevc.chroma=4:0:0", "hevc.ctb=16");
        yield return Hm("hm-lossless-mixed-64x64", Noise(Moving("64x64"), 10, 244), "yuv420p", 3, [.. HmLowDelayP, "--TransquantBypassEnable=1", "--CostMode=mixed_lossless_lossy"], "hevc.transquantBypass");
        yield return HmIntra("hm-lossless-mixed-444-10bit-64x64", Noise(TestSrc("64x64"), 6, 245), "yuv444p10le", 1, ["--TransquantBypassEnable=1", "--CostMode=mixed_lossless_lossy"], "hevc.transquantBypass", "hevc.chroma=4:4:4");

        // The coding tools of the range extensions, one at a time, then together
        yield return HmIntra("hm-rext-implicit-rdpcm-444-10bit-48x48", Noise(TestSrc("48x48"), 20, 246), "yuv444p10le", 1, ["--TransformSkip=1", "--TransformSkipFast=0", "--ImplicitResidualDPCM=1", "--QP=20"], "hevc.implicitRdpcm", "hevc.transformSkip");
        yield return HmIntra("hm-rext-implicit-rdpcm-lossless-444-48x48", Noise(TestSrc("48x48"), 20, 247), "yuv444p", 1, [.. HmLossless, "--ImplicitResidualDPCM=1"], "hevc.implicitRdpcm", "hevc.lossless").Without(DecoderFfmpeg, FfmpegBypassResidualTools);
        yield return HmIntra("hm-rext-implicit-rdpcm-lossless-420-48x48", Noise(TestSrc("48x48"), 20, 248), "yuv420p", 1, [.. HmLossless, "--ImplicitResidualDPCM=1", "--Profile=main-RExt"], "hevc.implicitRdpcm", "hevc.lossless", "hevc.chroma=4:2:0").Without(DecoderFfmpeg, FfmpegBypassResidualTools);
        yield return Hm("hm-rext-explicit-rdpcm-444-10bit-48x48", Noise(Moving("48x48"), 20, 249), "yuv444p10le", 3, [.. HmLowDelayP, "--TransformSkip=1", "--TransformSkipFast=0", "--ExplicitResidualDPCM=1", "--QP=20"], "hevc.explicitRdpcm");
        yield return Hm("hm-rext-explicit-rdpcm-lossless-422-48x48", Noise(Moving("48x48"), 20, 250), "yuv422p", 3, [.. HmLowDelayB, .. HmLossless, "--ExplicitResidualDPCM=1", "--ImplicitResidualDPCM=1"], "hevc.explicitRdpcm", "hevc.implicitRdpcm", "hevc.lossless").Without(DecoderFfmpeg, FfmpegBypassResidualTools);
        yield return HmIntra("hm-rext-rotation-444-10bit-48x48", Noise(TestSrc("48x48"), 20, 251), "yuv444p10le", 1, ["--TransformSkip=1", "--TransformSkipFast=0", "--ResidualRotation=1", "--QP=20"], "hevc.transformSkipRotation");
        yield return HmIntra("hm-rext-rotation-lossless-444-48x48", Noise(TestSrc("48x48"), 20, 252), "yuv444p", 1, [.. HmLossless, "--ResidualRotation=1"], "hevc.transformSkipRotation", "hevc.lossless").Without(DecoderFfmpeg, FfmpegBypassResidualTools);
        yield return HmIntra("hm-rext-tskip-context-444-10bit-48x48", Noise(TestSrc("48x48"), 20, 253), "yuv444p10le", 1, ["--TransformSkip=1", "--TransformSkipFast=0", "--SingleSignificanceMapContext=1", "--QP=20"], "hevc.transformSkipContext");
        yield return HmIntra("hm-rext-tskip-8x8-444-48x48", Noise(TestSrc("48x48"), 20, 254), "yuv444p", 1, ["--TransformSkip=1", "--TransformSkipFast=0", "--TransformSkipLog2MaxSize=3", "--QP=20"], "hevc.transformSkip.maxSize=8");
        yield return HmIntra("hm-rext-tskip-16x16-422-10bit-48x48", Noise(TestSrc("48x48"), 20, 255), "yuv422p10le", 1, ["--TransformSkip=1", "--TransformSkipFast=0", "--TransformSkipLog2MaxSize=4", "--QP=20"], "hevc.transformSkip.maxSize=16", "hevc.chroma=4:2:2");
        yield return Hm("hm-rext-tskip-32x32-444-12bit-64x64", Noise(Moving("64x64"), 20, 256), "yuv444p12le", 3, [.. HmLowDelayP, "--TransformSkip=1", "--TransformSkipFast=0", "--TransformSkipLog2MaxSize=5", "--QP=18"], "hevc.transformSkip.maxSize=32", "hevc.bitDepth=12");
        yield return HmIntra("hm-rext-implicit-rdpcm-422-10bit-48x48", Noise(TestSrc("48x48"), 20, 289), "yuv422p10le", 1, ["--TransformSkip=1", "--TransformSkipFast=0", "--ImplicitResidualDPCM=1", "--QP=20"], "hevc.implicitRdpcm", "hevc.chroma=4:2:2");
        yield return Hm("hm-rext-explicit-rdpcm-422-10bit-48x48", Noise(Moving("48x48"), 20, 290), "yuv422p10le", 3, [.. HmLowDelayP, "--TransformSkip=1", "--TransformSkipFast=0", "--ExplicitResidualDPCM=1", "--QP=20"], "hevc.explicitRdpcm", "hevc.chroma=4:2:2");
        yield return HmIntra("hm-rext-rotation-422-10bit-48x48", Noise(TestSrc("48x48"), 20, 291), "yuv422p10le", 1, ["--TransformSkip=1", "--TransformSkipFast=0", "--ResidualRotation=1", "--QP=20"], "hevc.transformSkipRotation", "hevc.chroma=4:2:2");
        yield return HmIntra("hm-rext-tskip-context-420-48x48", Noise(TestSrc("48x48"), 20, 292), "yuv420p", 1, ["--TransformSkip=1", "--TransformSkipFast=0", "--SingleSignificanceMapContext=1", "--Profile=main-RExt", "--QP=20"], "hevc.transformSkipContext", "hevc.chroma=4:2:0");
        yield return HmIntra("hm-rext-persistent-rice-422-12bit-48x48", Noise(TestSrc("48x48"), 30, 293), "yuv422p12le", 1, ["--GolombRiceParameterAdaptation=1", "--QP=4"], "hevc.persistentRice", "hevc.chroma=4:2:2");
        yield return HmIntra("hm-rext-cross-component-444-8bit-48x48", Noise(TestSrc("48x48"), 8, 257), "yuv444p", 1, ["--CrossComponentPrediction=1", "--QP=22"], "hevc.crossComponentPrediction");
        yield return Hm("hm-rext-cross-component-444-12bit-48x48", Rotating("48x48"), "yuv444p12le", 3, [.. HmLowDelayB, "--CrossComponentPrediction=1", "--QP=22"], "hevc.crossComponentPrediction", "hevc.bitDepth=12", "hevc.frameTypes=inter");
        yield return HmIntra("hm-rext-cross-component-gbr-10bit-48x48", Noise(TestSrc("48x48"), 8, 258), "gbrp10le", 1, [.. HmVui("--VideoSignalTypePresent=1", "--ColourDescriptionPresent=1", "--MatrixCoefficients=0"), "--CrossComponentPrediction=1", "--QP=22"], "hevc.crossComponentPrediction", "hevc.matrix=identity");
        yield return HmIntra("hm-rext-chroma-qp-offset-list-444-10bit-64x64", Noise(TestSrc2("64x64"), 10, 259), "yuv444p10le", 1, ["--MaxCUChromaQpAdjustmentDepth=0"], "hevc.chromaQpOffsetList", "hevc.cuChromaQpOffset");
        yield return Hm("hm-rext-chroma-qp-offset-list-422-64x64", Noise(Moving("64x64"), 10, 260), "yuv422p", 3, [.. HmLowDelayP, "--MaxCUChromaQpAdjustmentDepth=1"], "hevc.chromaQpOffsetList", "hevc.cuChromaQpOffset", "hevc.chroma=4:2:2");
        yield return HmIntra("hm-rext-persistent-rice-444-12bit-48x48", Noise(TestSrc("48x48"), 30, 261), "yuv444p12le", 1, ["--GolombRiceParameterAdaptation=1", "--QP=4"], "hevc.persistentRice", "hevc.bitDepth=12");
        yield return Hm("hm-rext-persistent-rice-400-10bit-48x48", Noise(Moving("48x48"), 30, 262), "gray10le", 3, [.. HmLowDelayP, "--GolombRiceParameterAdaptation=1", "--QP=6"], "hevc.persistentRice", "hevc.chroma=4:0:0");
        yield return HmIntra("hm-rext-intra-smoothing-disabled-444-64x64", Mandelbrot("64x64"), "yuv444p", 1, ["--IntraReferenceSmoothing=0"], "hevc.intraSmoothingDisabled");
        yield return Hm("hm-rext-high-precision-weights-444-12bit-64x64", Fading("64x64"), "yuv444p12le", 5, [.. HmLowDelayP, "--WeightedPredP=1", "--HighPrecisionPredictionWeighting=1"], "hevc.highPrecisionOffsets", "hevc.weights=luma", "hevc.bitDepth=12").Without(DecoderFfmpeg, FfmpegHighPrecisionOffsets);
        yield return Hm("hm-rext-high-precision-weights-b-422-10bit-64x64", Fading("64x64"), "yuv422p10le", 5, [.. HmLowDelayB, "--WeightedPredB=1", "--HighPrecisionPredictionWeighting=1"], "hevc.highPrecisionOffsets", "hevc.weightedPred=b", "hevc.chroma=4:2:2").Without(DecoderFfmpeg, FfmpegHighPrecisionOffsets);
        foreach (var (depth, suffix) in new[] { (8, ""), (10, "10le"), (12, "12le") })
        {
            yield return Hm($"hm-rext-all-tools-444-{depth}bit-64x64", Noise(Rotating("64x64"), 12, 263 + depth), "yuv444p" + suffix, 4,
                [.. HmLowDelayB, .. HmRangeExtensionTools, "--CrossComponentPrediction=1", "--MaxCUChromaQpAdjustmentDepth=1", "--IntraReferenceSmoothing=0", "--QP=22"],
                "hevc.implicitRdpcm", "hevc.explicitRdpcm", "hevc.transformSkipRotation", "hevc.transformSkipContext", "hevc.transformSkip.maxSize=16", "hevc.crossComponentPrediction", "hevc.chromaQpOffsetList", "hevc.persistentRice",
                "hevc.intraSmoothingDisabled", $"hevc.bitDepth={depth}");
        }

        yield return Hm("hm-rext-all-tools-422-12bit-64x64", Noise(Rotating("64x64"), 12, 276), "yuv422p12le", 4, [.. HmLowDelayB, .. HmRangeExtensionTools, "--MaxCUChromaQpAdjustmentDepth=1", "--QP=22"],
            "hevc.implicitRdpcm", "hevc.explicitRdpcm", "hevc.transformSkipRotation", "hevc.transformSkipContext", "hevc.persistentRice", "hevc.chroma=4:2:2", "hevc.bitDepth=12")
            .Without(DecoderLibde265, Libde265RangeExtensionsSubsampled);
        yield return Hm("hm-rext-all-tools-400-10bit-64x64", Noise(Rotating("64x64"), 12, 277), "gray10le", 4, [.. HmLowDelayP, .. HmRangeExtensionTools, "--QP=22"],
            "hevc.implicitRdpcm", "hevc.explicitRdpcm", "hevc.transformSkipRotation", "hevc.persistentRice", "hevc.chroma=4:0:0");
        yield return Hm("hm-rext-all-tools-420-8bit-64x64", Noise(Rotating("64x64"), 12, 278), "yuv420p", 4, [.. HmLowDelayP, .. HmRangeExtensionTools, "--Profile=main-RExt", "--QP=22"],
            "hevc.implicitRdpcm", "hevc.explicitRdpcm", "hevc.transformSkipRotation", "hevc.persistentRice", "hevc.chroma=4:2:0")
            .Without(DecoderLibde265, Libde265RangeExtensionsSubsampled);
        yield return Hm("hm-rext-all-tools-lossless-444-10bit-64x64", Noise(Rotating("64x64"), 12, 279), "yuv444p10le", 3, [.. HmLowDelayP, .. HmRangeExtensionTools, .. HmLossless, "--CrossComponentPrediction=1"],
            "hevc.implicitRdpcm", "hevc.explicitRdpcm", "hevc.transformSkipRotation", "hevc.lossless")
            .Without(DecoderFfmpeg, FfmpegBypassResidualTools);

        // The intra profiles of the range extensions
        yield return HmIntra("hm-intra-profile-422-10-intra-50x38", Noise(TestSrc2("50x38"), 8, 280), "yuv422p10le", 1, ["--Profile=main_422_10_intra"], "hevc.profile=rext-422-10-intra");
        yield return HmIntra("hm-intra-profile-444-intra-49x37", Noise(TestSrc("49x37"), 8, 281), "yuv444p", 1, ["--Profile=main_444_intra"], "hevc.profile=rext-444-8-intra");
        yield return HmIntra("hm-intra-profile-monochrome12-49x37", Noise(TestSrc("49x37"), 8, 282), "gray12le", 1, ["--Profile=monochrome12"], "hevc.profile=rext-400-12");
        yield return HmIntra("hm-intra-profile-444-still-picture-49x37", Noise(TestSrc("49x37"), 8, 283), "yuv444p", 1, ["--Profile=main_444_still_picture"], "hevc.profile=rext-444-8-still");

        // Conformance windows with an offset on every side, video usability information, SEI messages
        yield return HmIntra("hm-crop-all-sides-420-64x64", Noise(TestSrc2("64x64"), 8, 284), "yuv420p", 1, ["--ConformanceWindowMode=3", "--ConfWinLeft=4", "--ConfWinRight=10", "--ConfWinTop=6", "--ConfWinBottom=2"], "hevc.crop=bottom,left,right,top")
            with { OutputSize = "50x56" };
        yield return HmIntra("hm-crop-all-sides-422-10bit-64x64", Noise(TestSrc2("64x64"), 8, 285), "yuv422p10le", 1, ["--ConformanceWindowMode=3", "--ConfWinLeft=6", "--ConfWinRight=2", "--ConfWinTop=3", "--ConfWinBottom=5"], "hevc.crop=bottom,left,right,top", "hevc.chroma=4:2:2")
            with { OutputSize = "56x56", Excluded = [(DecoderLibde265, Libde265HighBitDepthCrop)] };
        yield return HmIntra("hm-crop-all-sides-444-12bit-64x64", Noise(TestSrc2("64x64"), 8, 286), "yuv444p12le", 1, ["--ConformanceWindowMode=3", "--ConfWinLeft=3", "--ConfWinRight=5", "--ConfWinTop=1", "--ConfWinBottom=7"], "hevc.crop=bottom,left,right,top", "hevc.chroma=4:4:4")
            with { OutputSize = "56x56", Excluded = [(DecoderLibde265, Libde265HighBitDepthCrop)] };
        yield return HmIntra("hm-crop-all-sides-420-10bit-64x64", Noise(TestSrc2("64x64"), 8, 288), "yuv420p10le", 1, ["--ConformanceWindowMode=3", "--ConfWinLeft=2", "--ConfWinRight=4", "--ConfWinTop=8", "--ConfWinBottom=6"], "hevc.crop=bottom,left,right,top", "hevc.bitDepth=10")
            with { OutputSize = "58x50", Excluded = [(DecoderLibde265, Libde265HighBitDepthCrop)] };
        yield return Hm("hm-crop-all-sides-400-inter-64x64", Moving("64x64"), "gray", 3, [.. HmLowDelayP, "--ConformanceWindowMode=3", "--ConfWinLeft=5", "--ConfWinRight=1", "--ConfWinTop=7", "--ConfWinBottom=3"], "hevc.crop=bottom,left,right,top", "hevc.chroma=4:0:0")
            with { OutputSize = "58x54" };
        yield return HmIntra("hm-vui-display-window-chroma-loc-64x64", TestSrc2("64x64"), "yuv420p", 1,
            HmVui("--DefaultDisplayWindowFlag=1", "--DefDispWinLeftOffset=8", "--DefDispWinRightOffset=4", "--DefDispWinTopOffset=2", "--DefDispWinBottomOffset=6", "--ChromaLocInfoPresent=1", "--ChromaSampleLocTypeTopField=4", "--ChromaSampleLocTypeBottomField=4"),
            "hevc.defaultDisplayWindow", "hevc.chromaLoc=4");
        yield return HmIntra("hm-vui-colour-full-range-444-64x64", TestSrc2("64x64"), "yuv444p", 1,
            HmVui("--VideoSignalTypePresent=1", "--VideoFullRange=1", "--ColourDescriptionPresent=1", "--ColourPrimaries=12", "--TransferCharacteristics=13", "--MatrixCoefficients=1", "--ChromaLocInfoPresent=1", "--ChromaSampleLocTypeTopField=5", "--ChromaSampleLocTypeBottomField=5"),
            "hevc.fullRange", "hevc.colourDescription=12/13/1", "hevc.chromaLoc=5");
        yield return HmIntra("hm-gbr-8bit-36x34", Noise(TestSrc("36x34"), 8, 287), "gbrp", 1, HmVui("--VideoSignalTypePresent=1", "--VideoFullRange=1", "--ColourDescriptionPresent=1", "--ColourPrimaries=1", "--TransferCharacteristics=13", "--MatrixCoefficients=0"), "hevc.matrix=identity", "hevc.fullRange");
        yield return Hm("hm-gbr-12bit-inter-64x48", Moving("64x48"), "gbrp12le", 3, [.. HmLowDelayB, .. HmVui("--VideoSignalTypePresent=1", "--ColourDescriptionPresent=1", "--MatrixCoefficients=0")], "hevc.matrix=identity", "hevc.bitDepth=12", "hevc.frameTypes=inter");
        yield return Hm("hm-sei-aud-parameter-sets-64x64", Moving("64x64"), "yuv420p", 5,
            ["--IntraPeriod=2", "--GOPSize=1", "--Frame1=P 1 0 0.0 0.0 0 0 1.0 0 0 0 1 1 -1 0", "--DecodingRefreshType=2", "--AccessUnitDelimiter=1", "--SEIActiveParameterSets=1", "--SEIRecoveryPoint=1", "--SEIMasteringDisplayColourVolume=1", "--SEICLLEnabled=1",
                "--SEICLLMaxContentLightLevel=1000", "--SEICLLMaxPicAvgLightLevel=300", "--ReWriteParamSetsFlag=1"],
            "hevc.nal=AUD", "hevc.sei=activeParameterSets", "hevc.sei=recoveryPoint", "hevc.sei=masteringDisplayColourVolume", "hevc.sei=contentLightLevel");
        yield return Hm("hm-sei-temporal-level0-index-64x64", Moving("64x64"), "yuv420p", 9, [.. HmRandomAccess(1), "--SEITemporalLevel0Index=1", "--AccessUnitDelimiter=1"], "hevc.sei=temporalSubLayerZeroIndex", "hevc.nal=AUD");
    }

    private static IEnumerable<HevcVector> UnsupportedVectors()
    {
        // What the library rejects (hevc.unsupported): bit depths above 12, luma and chroma planes of different bit
        // depths, extended precision processing, CABAC bypass alignment, field sequences. The digests are recorded when
        // two decoders agree
        yield return HmIntra("hm-unsupported-16bit-444-32x32", Noise(TestSrc2("32x32"), 8, 301), "yuv444p16le", 1, ["--Profile=main_444_16_intra"], "hevc.unsupported=bitDepth", "hevc.bitDepth=16")
            .Without(DecoderFfmpeg, FfmpegHighBitDepth);
        yield return HmIntra("hm-unsupported-16bit-400-32x32", Noise(TestSrc2("32x32"), 8, 302), "gray16le", 1, ["--Profile=monochrome16"], "hevc.unsupported=bitDepth", "hevc.bitDepth=16", "hevc.chroma=4:0:0")
            .Without(DecoderFfmpeg, FfmpegHighBitDepth);
        yield return HmIntra("hm-unsupported-luma10-chroma8-32x32", Noise(TestSrc2("32x32"), 8, 303), "yuv420p-y10-c8", 1, ["--Profile=main-RExt", "--InputBitDepth=10", "--InternalBitDepth=10", "--InternalBitDepthC=8"], "hevc.unsupported=unequalBitDepth", "hevc.bitDepth=10", "hevc.bitDepthChroma=8")
            with { InputPixelFormat = "yuv420p10le", Excluded = [(DecoderFfmpeg, FfmpegUnequalBitDepth)] };
        yield return HmIntra("hm-unsupported-luma8-chroma10-32x32", Noise(TestSrc2("32x32"), 8, 304), "yuv420p-y8-c10", 1, ["--Profile=main-RExt", "--InternalBitDepth=8", "--InternalBitDepthC=10"], "hevc.unsupported=unequalBitDepth", "hevc.bitDepth=8", "hevc.bitDepthChroma=10")
            with { InputPixelFormat = "yuv420p", Excluded = [(DecoderFfmpeg, FfmpegUnequalBitDepth)] };
        yield return HmIntra("hm-unsupported-extended-precision-12bit-32x32", Noise(TestSrc2("32x32"), 8, 305), "yuv444p12le", 1, ["--Profile=main_444_16_intra", "--ExtendedPrecision=1"], "hevc.unsupported=extendedPrecision")
            with { WithoutDigests = true, Excluded = [(DecoderFfmpeg, FfmpegExtendedPrecision), (DecoderLibde265, Libde265ExtendedPrecision)] };
        yield return HmIntra("hm-unsupported-extended-precision-16bit-32x32", Noise(TestSrc2("32x32"), 8, 306), "yuv444p16le", 1, ["--Profile=main_444_16_intra", "--ExtendedPrecision=1"], "hevc.unsupported=extendedPrecision", "hevc.unsupported=bitDepth")
            with { WithoutDigests = true, Excluded = [(DecoderFfmpeg, FfmpegHighBitDepth), (DecoderLibde265, Libde265ExtendedPrecision)] };
        yield return HmIntra("hm-unsupported-bypass-alignment-444-8bit-32x32", Noise(TestSrc2("32x32"), 8, 307), "yuv444p", 1, ["--Profile=high_throughput_444_16_intra", "--AlignCABACBeforeBypass=1", "--InternalBitDepth=8"], "hevc.unsupported=cabacBypassAlignment", "hevc.profile=highThroughput-444-16-intra")
            .Without(DecoderFfmpeg, FfmpegBypassAlignment);
        yield return X265("x265-unsupported-field-sequence-64x64", Moving("64x64"), "yuv420p", 4, "interlace=tff:bframes=0", "hevc.unsupported=fieldSeq", "hevc.fieldSeq")
            .Without(DecoderHm, HmFieldOutput);
    }
}
