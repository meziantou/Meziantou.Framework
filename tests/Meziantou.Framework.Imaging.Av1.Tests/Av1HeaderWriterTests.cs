using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The writers of the sequence header and of the frame header. The first bytes of a sequence header are worked out by hand
/// from the syntax table of the specification (section 5.5.1); the other tests read the headers back with the parsers of
/// the decoder, which the conformance vectors validate against the reference decoders.
/// </summary>
public sealed class Av1HeaderWriterTests
{
    [Fact]
    public void AReducedStillPictureSequenceHeaderHasTheBitsOfTheSyntaxTable()
    {
        // 8-bit 4:2:0, 3x5, BT.601 full range, 64x64 superblocks, CDEF and restoration enabled, level 2.0
        var parameters = new Av1SequenceParameters(3, 5, 8, Monochrome: false, 1, 1, ColorPrimaries: 1, TransferCharacteristics: 13, MatrixCoefficients: 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: true, Use128x128Superblock: false, EnableCdef: true, EnableRestoration: true, LevelIndex: 0);
        var bytes = WriteSequenceHeader(parameters);

        // seq_profile 000, still_picture 1, reduced_still_picture_header 1, seq_level_idx 00000 (3 bits here)   -> 0001 1000
        // seq_level_idx (2 bits) 00, frame_width_bits_minus_1 0001, frame_height_bits_minus_1 (2 bits) 00      -> 0000 0100
        // frame_height_bits_minus_1 (2 bits) 10, max_frame_width_minus_1 10, max_frame_height_minus_1 100,
        // use_128x128_superblock 0                                                                            -> 1010 1000
        // enable_filter_intra 1, enable_intra_edge_filter 1, enable_superres 0, enable_cdef 1,
        // enable_restoration 1, high_bitdepth 0, mono_chrome 0, color_description_present_flag 1              -> 1101 1001
        // color_primaries 1, transfer_characteristics 13, matrix_coefficients 6                               -> 01 0D 06
        // color_range 1, chroma_sample_position 00, separate_uv_delta_q 0, film_grain_params_present 0,
        // trailing one bit, 00                                                                                -> 1000 0100
        Assert.Equal([0x18, 0x04, 0xA8, 0xD9, 0x01, 0x0D, 0x06, 0x84], bytes);
    }

    [Theory]
    [InlineData(8, false, 1, 1, 0)]
    [InlineData(10, false, 1, 1, 0)]
    [InlineData(12, false, 1, 1, 2)]
    [InlineData(8, false, 0, 0, 1)]
    [InlineData(10, false, 0, 0, 1)]
    [InlineData(12, false, 0, 0, 2)]
    [InlineData(8, false, 1, 0, 2)]
    [InlineData(10, false, 1, 0, 2)]
    [InlineData(12, false, 1, 0, 2)]
    [InlineData(8, true, 1, 1, 0)]
    [InlineData(10, true, 1, 1, 0)]
    [InlineData(12, true, 1, 1, 2)]
    public void SequenceHeadersAreReadBackForEverySampleFormat(int bitDepth, bool monochrome, int subsamplingX, int subsamplingY, int expectedProfile)
    {
        foreach (var still in new[] { true, false })
        {
            foreach (var (width, height) in new[] { (1, 1), (2, 1), (17, 255), (256, 257), (4097, 3000), (65536, 65536) })
            {
                // The identity matrix requires 4:4:4 samples; sRGB primaries and transfer with it are the special case of
                // the color config syntax that has no range and no subsampling
                var is444 = !monochrome && subsamplingX is 0;
                foreach (var (primaries, transfer, matrix, fullRange) in is444 ? new[] { (1, 13, 0, true), (2, 2, 0, true), (9, 16, 9, false) } : [(1, 13, 6, true), (2, 2, 1, false)])
                {
                    var level = Av1Levels.GetLevelIndex(width, height);
                    var parameters = new Av1SequenceParameters(width, height, bitDepth, monochrome, subsamplingX, subsamplingY, primaries, transfer, matrix, fullRange, ChromaSamplePosition: subsamplingX is 1 && subsamplingY is 1 && !monochrome ? 2 : 0, still, Use128x128Superblock: width > 1000, EnableCdef: height > 1, EnableRestoration: width > 1, level);
                    Assert.Equal(expectedProfile, parameters.Profile);

                    var header = Av1SequenceHeader.Parse(WriteSequenceHeader(parameters), operatingPoint: 0);
                    Assert.Equal(expectedProfile, header.Profile);
                    Assert.Equal((still, still), (header.StillPicture, header.ReducedStillPictureHeader));
                    Assert.Equal((width, height), (header.MaxFrameWidth, header.MaxFrameHeight));
                    Assert.Equal((bitDepth, monochrome), (header.BitDepth, header.Monochrome));
                    Assert.Equal((primaries, transfer, matrix, fullRange), (header.ColorPrimaries, header.TransferCharacteristics, header.MatrixCoefficients, header.FullRange));
                    Assert.Equal(level, header.GetOperatingPoint(0).LevelIndex);
                    Assert.Equal(1, header.OperatingPointCount);
                    Assert.Equal(0, header.OperatingPointIdc);
                    if (monochrome)
                    {
                        // Monochrome samples are described as 4:2:0 without chroma planes
                        Assert.Equal((1, 1, 0), (header.SubsamplingX, header.SubsamplingY, header.ChromaSamplePosition));
                    }
                    else
                    {
                        Assert.Equal((subsamplingX, subsamplingY, parameters.ChromaSamplePosition), (header.SubsamplingX, header.SubsamplingY, header.ChromaSamplePosition));
                    }

                    Assert.Equal((parameters.Use128x128Superblock, parameters.EnableCdef, parameters.EnableRestoration), (header.Use128x128Superblock, header.EnableCdef, header.EnableRestoration));
                    Assert.True(header.EnableFilterIntra);
                    Assert.True(header.EnableIntraEdgeFilter);
                    Assert.False(header.EnableSuperres);
                    Assert.False(header.FilmGrainParamsPresent);
                    Assert.False(header.SeparateUvDeltaQ);
                    Assert.False(header.FrameIdNumbersPresent);
                    Assert.Equal(Av1Constants.SelectScreenContentTools, header.ForceScreenContentTools);
                    Assert.Equal(Av1Constants.SelectIntegerMv, header.ForceIntegerMv);
                    Assert.Equal((!still, !still, still ? 0 : Av1SequenceParameters.OrderHintBits), (header.EnableOrderHint, header.EnableRefFrameMvs, header.OrderHintBits));
                    Assert.False(header.EnableInterIntraCompound || header.EnableMaskedCompound || header.EnableWarpedMotion || header.EnableDualFilter || header.EnableJntComp);
                }
            }
        }
    }

    [Fact]
    public void LevelsAreTheLowestOnesThatHoldThePicture()
    {
        // Table of Annex A.3: MaxPicSize, MaxHSize and MaxVSize
        Assert.Equal(0, Av1Levels.GetLevelIndex(426, 240)); // 2.0
        Assert.Equal(0, Av1Levels.GetLevelIndex(2048, 72)); // 2.0: 147456 samples
        Assert.Equal(1, Av1Levels.GetLevelIndex(2049, 71)); // 2.1: wider than 2048
        Assert.Equal(1, Av1Levels.GetLevelIndex(640, 360)); // 2.1
        Assert.Equal(4, Av1Levels.GetLevelIndex(854, 480)); // 3.0
        Assert.Equal(5, Av1Levels.GetLevelIndex(1280, 720)); // 3.1
        Assert.Equal(8, Av1Levels.GetLevelIndex(1920, 1080)); // 4.0
        Assert.Equal(12, Av1Levels.GetLevelIndex(3840, 2160)); // 5.0
        Assert.Equal(16, Av1Levels.GetLevelIndex(7680, 4320)); // 6.0
        Assert.Equal(16, Av1Levels.GetLevelIndex(1, 8704)); // 6.0: taller than 4352
        Assert.Equal(Av1Levels.MaximumParameters, Av1Levels.GetLevelIndex(1, 8705));
        Assert.Equal(Av1Levels.MaximumParameters, Av1Levels.GetLevelIndex(16385, 1));
        Assert.Equal(Av1Levels.MaximumParameters, Av1Levels.GetLevelIndex(8192, 8192));
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(64, 64, false)]
    [InlineData(65, 130, false)]
    [InlineData(4097, 31, false)]
    [InlineData(5000, 5000, true)]
    [InlineData(9000, 70, true)]
    public void StillFrameHeadersAreReadBack(int width, int height, bool use128)
    {
        foreach (var monochrome in new[] { false, true })
        {
            var sequence = CreateSequence(width, height, monochrome, still: true, use128);
            var limits = Av1FrameHeaderParser.ComputeTileLimits(use128, 2 * ((width + 7) >> 3), 2 * ((height + 7) >> 3));

            // Lossless: no quantizer delta, no filter, no transform mode
            var lossless = new Av1FrameHeader { FrameType = Av1Constants.KeyFrame, ShowFrame = true, AllowScreenContentTools = true, AllowIntrabc = true, TileColsLog2 = limits.MinLog2TileCols, TileRowsLog2 = Math.Max(limits.MinLog2Tiles - limits.MinLog2TileCols, 0), TileSizeBytes = 4 };
            var parsed = RoundTrip(sequence, lossless);
            Assert.Equal((Av1Constants.KeyFrame, true, true, true), (parsed.FrameType, parsed.FrameIsIntra, parsed.ShowFrame, parsed.ErrorResilientMode));
            Assert.Equal((width, height, width), (parsed.FrameWidth, parsed.FrameHeight, parsed.UpscaledWidth));
            Assert.Equal((true, true, true, true), (parsed.CodedLossless, parsed.AllLossless, parsed.AllowScreenContentTools, parsed.AllowIntrabc));
            Assert.Equal((Av1Constants.Only4x4, Av1Constants.PrimaryRefNone, true), (parsed.TxMode, parsed.PrimaryRefFrame, parsed.DisableFrameEndUpdateCdf));
            Assert.Equal((lossless.TileColsLog2, lossless.TileRowsLog2), (parsed.TileColsLog2, parsed.TileRowsLog2));
            if (parsed.TileCount > 1)
                Assert.Equal(4, parsed.TileSizeBytes);

            // Lossy with every filter, more tiles than required when the frame allows it
            var lossy = new Av1FrameHeader
            {
                FrameType = Av1Constants.KeyFrame,
                ShowFrame = true,
                DisableCdfUpdate = false,
                TileColsLog2 = Math.Min(limits.MinLog2TileCols + 1, limits.MaxLog2TileCols),
                TileSizeBytes = 3,
                BaseQIdx = 137,
                DeltaQYDc = -5,
                DeltaQUDc = monochrome ? 0 : 7,
                DeltaQUAc = monochrome ? 0 : -64,
                DeltaQVDc = monochrome ? 0 : 7,
                DeltaQVAc = monochrome ? 0 : -64,
                UsingQmatrix = true,
                QmY = 9,
                QmU = 3,
                QmV = 3,
                DeltaQPresent = true,
                DeltaQRes = 2,
                LoopFilterSharpness = 5,
                LoopFilterDeltaEnabled = true,
                CdefDamping = 5,
                CdefBits = 2,
                TxMode = Av1Constants.TxModeSelect,
                ReducedTxSet = true,
            };

            lossy.TileRowsLog2 = Math.Min(Math.Max(limits.MinLog2Tiles - lossy.TileColsLog2, 0) + 1, limits.MaxLog2TileRows);
            lossy.ContextUpdateTileId = (1 << (lossy.TileColsLog2 + lossy.TileRowsLog2)) > 1 ? 1 : 0;
            lossy.LoopFilterLevel[0] = 12;
            lossy.LoopFilterLevel[1] = 63;
            lossy.LoopFilterLevel[2] = 1;
            lossy.LoopFilterLevel[3] = 33;
            for (var i = 0; i < 4; i++)
            {
                lossy.CdefYPriStrength[i] = (byte)(15 - i);
                lossy.CdefYSecStrength[i] = (byte)(i is 3 ? 4 : i);
                lossy.CdefUvPriStrength[i] = (byte)i;
                lossy.CdefUvSecStrength[i] = (byte)(i is 0 ? 4 : i - 1);
            }

            lossy.FrameRestorationType[0] = Av1Constants.RestoreSwitchable;
            lossy.FrameRestorationType[1] = Av1Constants.RestoreWiener;
            lossy.FrameRestorationType[2] = Av1Constants.RestoreSgrproj;
            lossy.LoopRestorationSize[0] = 256;
            lossy.LoopRestorationSize[1] = 128;
            lossy.LoopRestorationSize[2] = 128;

            parsed = RoundTrip(sequence, lossy);
            Assert.False(parsed.CodedLossless);
            Assert.Equal((137, -5, lossy.DeltaQUDc, lossy.DeltaQUAc, lossy.DeltaQVDc, lossy.DeltaQVAc), (parsed.BaseQIdx, parsed.DeltaQYDc, parsed.DeltaQUDc, parsed.DeltaQUAc, parsed.DeltaQVDc, parsed.DeltaQVAc));
            Assert.Equal((true, 9, 3, 3), (parsed.UsingQmatrix, parsed.QmY, parsed.QmU, parsed.QmV));
            Assert.Equal((true, 2, false), (parsed.DeltaQPresent, parsed.DeltaQRes, parsed.DeltaLfPresent));
            Assert.Equal((12, 63, 5, true, false), (parsed.LoopFilterLevel[0], parsed.LoopFilterLevel[1], parsed.LoopFilterSharpness, parsed.LoopFilterDeltaEnabled, parsed.LoopFilterDeltaUpdate));
            Assert.Equal((5, 2), (parsed.CdefDamping, parsed.CdefBits));
            for (var i = 0; i < 4; i++)
            {
                Assert.Equal((lossy.CdefYPriStrength[i], lossy.CdefYSecStrength[i]), (parsed.CdefYPriStrength[i], parsed.CdefYSecStrength[i]));
                if (!monochrome)
                    Assert.Equal((lossy.CdefUvPriStrength[i], lossy.CdefUvSecStrength[i]), (parsed.CdefUvPriStrength[i], parsed.CdefUvSecStrength[i]));
            }

            Assert.Equal((Av1Constants.RestoreSwitchable, 256, true), (parsed.FrameRestorationType[0], parsed.LoopRestorationSize[0], parsed.UsesLr));
            if (!monochrome)
            {
                Assert.Equal((1, 33), (parsed.LoopFilterLevel[2], parsed.LoopFilterLevel[3]));
                Assert.Equal((Av1Constants.RestoreWiener, Av1Constants.RestoreSgrproj, 128, 128), (parsed.FrameRestorationType[1], parsed.FrameRestorationType[2], parsed.LoopRestorationSize[1], parsed.LoopRestorationSize[2]));
            }

            Assert.Equal((Av1Constants.TxModeSelect, true), (parsed.TxMode, parsed.ReducedTxSet));
            Assert.Equal((lossy.TileColsLog2, lossy.TileRowsLog2, lossy.ContextUpdateTileId), (parsed.TileColsLog2, parsed.TileRowsLog2, parsed.ContextUpdateTileId));
            if (parsed.TileCount > 1)
                Assert.Equal(3, parsed.TileSizeBytes);

            // The sizes of the loop restoration units that have their own syntax
            foreach (var size in use128 ? new[] { 128, 256 } : [64, 128, 256])
            {
                lossy.LoopRestorationSize[0] = size;
                lossy.LoopRestorationSize[1] = size;
                lossy.LoopRestorationSize[2] = size;
                parsed = RoundTrip(sequence, lossy);
                Assert.Equal(size, parsed.LoopRestorationSize[0]);
                if (!monochrome)
                    Assert.Equal(size, parsed.LoopRestorationSize[1]);
            }
        }
    }

    private static byte[] WriteSequenceHeader(in Av1SequenceParameters parameters)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(Av1HeaderWriterTests));
        using var output = new PooledPayloadWriter(scope);
        Av1HeaderWriter.WriteSequenceHeader(output, parameters);
        return output.WrittenSpan.ToArray();
    }

    private static Av1SequenceHeader CreateSequence(int width, int height, bool monochrome, bool still, bool use128)
    {
        var parameters = new Av1SequenceParameters(width, height, 8, monochrome, 1, 1, 1, 13, 6, FullRange: true, ChromaSamplePosition: 0, still, use128, EnableCdef: true, EnableRestoration: true, Av1Levels.GetLevelIndex(width, height));
        return Av1SequenceHeader.Parse(WriteSequenceHeader(parameters), operatingPoint: 0);
    }

    // Writes the header, then parses it like the header of a frame OBU: the header ends where the parser stops
    private static Av1FrameHeader RoundTrip(Av1SequenceHeader sequence, Av1FrameHeader header)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(Av1HeaderWriterTests));
        using var output = new PooledPayloadWriter(scope);
        var writer = new Av1BitWriter(output);
        Av1HeaderWriter.WriteFrameHeader(ref writer, sequence, header);
        var bits = writer.BitPosition;
        writer.WriteTrailingBits();

        var data = output.WrittenSpan.ToArray();
        var reader = new Av1BitReader(data);
        var slots = new Av1FrameBuffer?[Av1Constants.NumRefFrames];
        var parsed = Av1FrameHeaderParser.Parse(ref reader, sequence, new Av1ObuHeader(Av1Constants.ObuFrameHeader, HasExtension: false, 0, 0, 0, data.Length), slots, new bool[Av1Constants.NumRefFrames]);
        Assert.Equal(bits, reader.BitPosition);
        reader.ReadTrailingBits();
        return parsed;
    }
}
