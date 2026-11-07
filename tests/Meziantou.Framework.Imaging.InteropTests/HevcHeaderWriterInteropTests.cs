using System.Collections.Concurrent;
using System.Globalization;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Hevc;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// The building blocks of the HEVC encoder against independent decoders, before any block is searched: complete bitstreams
/// written with the header writer, the NAL unit writer and the arithmetic encoder, whose pictures are made of PCM coding
/// units (Rec. ITU-T H.265, clauses 7.3.8.4, 7.3.8.5 and 7.3.8.7), the samples of the pictures as they are. The native
/// decoder of FFmpeg, dec265 (libde265) and TAppDecoder (HM, which checks what a profile allows) must each output exactly
/// the samples that were written, and the <c>trace_headers</c> bitstream filter of FFmpeg must read the profile, the level
/// and the constraint flags that the encoder announces.
/// </summary>
/// <remarks>
/// <para>
/// The pinned dec265 aborts (<c>std::bad_alloc</c>) once it has written the luma samples of a 10-bit monochrome picture
/// that has a conformance cropping window, whatever wrote the stream: the same happens with a stream of x265
/// (<c>--input-csp i400 --output-depth 10</c>, 36x20), and not with an 8-bit one or without the window. It is not asked
/// for the samples of these streams; the two other decoders and the library are.
/// </para>
/// <para>
/// A PCM coding unit is lossless and needs no prediction, no transform and no search, so that the streams validate the NAL
/// unit framing, the parameter sets for every sample format, the slice segment headers of the I slices and of the P slices
/// (whose coding units are all intra coding units here), the arithmetic encoding engine with its terminations, and the
/// decoded picture hash, and nothing else.
/// </para>
/// </remarks>
public sealed class HevcHeaderWriterInteropTests
{
    private static readonly (int Width, int Height)[] Sizes = [(16, 16), (33, 17), (64, 48), (5, 7), (40, 24), (97, 35), (8, 8), (130, 66), (1, 1), (20, 12), (48, 32), (19, 65)];

    private enum SequenceKind
    {
        /// <summary>One picture.</summary>
        Still,

        /// <summary>Two pictures that are both key frames.</summary>
        IntraOnly,

        /// <summary>A key frame, then two pictures in P slices that refer to the picture before them.</summary>
        Inter,
    }

    /// <param name="Name">The name of the stream in the report and in the artifacts.</param>
    /// <param name="Sequence">The parameters of the sequence.</param>
    /// <param name="Picture">The parameters of the picture parameter set.</param>
    /// <param name="Kind">The pictures of the sequence.</param>
    /// <param name="Profile">The name of the profile that the sequence is expected to announce (Annex A).</param>
    /// <param name="Seed">The seed of the samples and of the choices of the coding quadtree.</param>
    private sealed record StreamCase(string Name, HevcSequenceParameters Sequence, HevcPictureParameters Picture, SequenceKind Kind, string Profile, int Seed);

    /// <param name="ByteStream">The stream, in the byte stream format.</param>
    /// <param name="Expected">The planes of every picture cropped to the conformance window: what a decoder outputs.</param>
    /// <param name="Sps">The sequence parameter set, as the library reads it.</param>
    /// <param name="HasInterSlices">Whether a picture is in a P slice.</param>
    private sealed record WrittenStream(byte[] ByteStream, byte[] Expected, HevcSequenceParameterSet Sps, bool HasInterSlices);

    [Fact]
    public async Task StreamsOfPcmCodingUnitsAreDecodedByTheReferenceDecoders()
    {
        var decoders = await RequiredTools.HevcReferenceDecodersAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var output = TestContext.Current.TestOutputHelper;
        var artifacts = InteropSettings.GetArtifactsDirectory(nameof(HevcHeaderWriterInteropTests));
        var cases = CreateCases();
        var failures = new ConcurrentBag<string>();
        var reports = new ConcurrentBag<string>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 4, 1, 4), CancellationToken = XunitCancellationToken };
        await Parallel.ForEachAsync(cases, options, async (streamCase, cancellationToken) =>
        {
            var (report, failure) = await RunAsync(decoders, ffmpeg, streamCase, artifacts, cancellationToken);
            reports.Add(report);
            if (failure is not null)
            {
                failures.Add(failure);
            }
        });

        foreach (var report in reports.Order(StringComparer.Ordinal))
        {
            output?.WriteLine(report);
        }

        if (!failures.IsEmpty)
            Assert.Fail($"{failures.Count} stream(s) out of {cases.Count} are not decoded as they were written:\n" + string.Join("\n", failures.Order(StringComparer.Ordinal).Select(failure => "- " + failure)));

        // Every profile that the encoder announces is one of a stream
        Assert.Equal(Enum.GetValues<HevcProfile>().Order().ToArray(), cases.Select(streamCase => streamCase.Sequence.Profile).Distinct().Order().ToArray());
    }

    /// <summary>
    /// The streams: every chroma format with every bit depth and every kind of sequence, which together announce every
    /// profile of <see cref="HevcProfile"/>; sizes that need a conformance window, the odd ones included; every size of
    /// coding tree block; each coding tool of the sequence and of the picture parameter sets.
    /// </summary>
    private static List<StreamCase> CreateCases()
    {
        var cases = new List<StreamCase>();

        // The twelve sample formats, with a kind of sequence that changes from one to the next
        (SequenceKind Kind, string Profile)[][] formats =
        [
            [(SequenceKind.Still, "Monochrome"), (SequenceKind.IntraOnly, "Monochrome 10"), (SequenceKind.Inter, "Monochrome 12")],
            [(SequenceKind.IntraOnly, "Main"), (SequenceKind.Inter, "Main 10"), (SequenceKind.Still, "Main 12 Intra")],
            [(SequenceKind.Inter, "Main 4:2:2 10"), (SequenceKind.Still, "Main 4:2:2 10 Intra"), (SequenceKind.IntraOnly, "Main 4:2:2 12 Intra")],
            [(SequenceKind.Still, "Main 4:4:4 Still Picture"), (SequenceKind.IntraOnly, "Main 4:4:4 10 Intra"), (SequenceKind.Inter, "Main 4:4:4 12")],
        ];
        for (var chromaFormatIdc = 0; chromaFormatIdc < 4; chromaFormatIdc++)
        {
            for (var depth = 0; depth < 3; depth++)
            {
                var (kind, profile) = formats[chromaFormatIdc][depth];
                Add(chromaFormatIdc, 8 + (2 * depth), kind, profile);
            }
        }

        // The other profiles
        Add(1, 8, SequenceKind.Still, "Main Still Picture");
        Add(1, 10, SequenceKind.Still, "Main 10 Still Picture");
        Add(1, 12, SequenceKind.Inter, "Main 12");
        Add(2, 12, SequenceKind.Inter, "Main 4:2:2 12");
        Add(3, 8, SequenceKind.Inter, "Main 4:4:4");
        Add(3, 8, SequenceKind.IntraOnly, "Main 4:4:4 Intra");
        Add(3, 10, SequenceKind.Inter, "Main 4:4:4 10");
        Add(3, 12, SequenceKind.Still, "Main 4:4:4 12 Intra");

        // The tools of the range extensions need a 4:4:4 profile whatever the chroma format (Table A.1)
        Add(1, 8, SequenceKind.Still, "Main 4:4:4 Still Picture", HevcCodingTools.ImplicitRdpcm | HevcCodingTools.ExplicitRdpcm | HevcCodingTools.IntraSmoothingDisabled);
        Add(0, 10, SequenceKind.Inter, "Main 4:4:4 10", HevcCodingTools.PersistentRiceAdaptation);
        Add(2, 8, SequenceKind.IntraOnly, "Main 4:4:4 Intra", HevcCodingTools.TransformSkipRotation | HevcCodingTools.TransformSkipContext | HevcCodingTools.LargeTransformSkipBlocks);
        Add(3, 12, SequenceKind.IntraOnly, "Main 4:4:4 12 Intra", HevcCodingTools.ExtendedTools);

        // Larger pictures: levels 2 and 3
        Add(1, 8, SequenceKind.Inter, "Main", size: (416, 240));
        Add(1, 10, SequenceKind.Still, "Main 10 Still Picture", size: (720, 576));
        return cases;

        void Add(int chromaFormatIdc, int bitDepth, SequenceKind kind, string profile, HevcCodingTools tools = HevcCodingTools.None, (int Width, int Height)? size = null)
        {
            var index = cases.Count;
            var (width, height) = size ?? Sizes[index % Sizes.Length];
            var settings = new HevcEncoderSettings(
                width,
                height,
                bitDepth,
                chromaFormatIdc,
                ColourPrimaries: index % 2 is 0 ? 1 : 9,
                TransferCharacteristics: index % 2 is 0 ? 13 : 16,
                MatrixCoeffs: index % 2 is 0 ? (chromaFormatIdc is 3 ? 0 : 6) : 9,
                FullRange: index % 2 is 0,
                ChromaSampleLocType: index % 6,
                StillPicture: kind is SequenceKind.Still,
                Lossless: true,
                Quality: 100,
                Effort: 0,
                KeyFrameInterval: kind is SequenceKind.Inter ? 0 : 1);

            // PCM coding units, and the tools of the sequence parameter set in turn
            tools |= HevcCodingTools.Pcm | (index % 4) switch
            {
                1 => HevcCodingTools.SampleAdaptiveOffset,
                2 => HevcCodingTools.ScalingLists | HevcCodingTools.StrongIntraSmoothing | HevcCodingTools.AsymmetricMotionPartitions | HevcCodingTools.TemporalMvp,
                3 => HevcCodingTools.SampleAdaptiveOffset | HevcCodingTools.TemporalMvp,
                _ => HevcCodingTools.None,
            };

            // Coding tree blocks of 16, 32 and 64 samples, and coding blocks of at least 8 or 16 samples
            var ctbLog2SizeY = 4 + (index % 3);
            var sequence = HevcSequenceParameters.Create(settings, tools) with
            {
                MinCbLog2SizeY = 3 + ((index / 2) % 2),
                CtbLog2SizeY = ctbLog2SizeY,
                MaxTbLog2SizeY = Math.Min(ctbLog2SizeY, 5),
                MaxTransformHierarchyDepthInter = index % 3,
                MaxTransformHierarchyDepthIntra = (index + 1) % 3,
                PcmBitDepth = index % 4 is 2 ? bitDepth - 3 : 0,
            };
            Assert.True(sequence.IsValid);

            // The tools of the picture parameter set in turn
            var initQp = 26 - (index % 7);
            var picture = (index % 5) switch
            {
                1 => new HevcPictureParameters(initQp) { TransquantBypassEnabled = true, SignDataHidingEnabled = true, TransformSkipEnabled = true },
                2 => new HevcPictureParameters(initQp) { DeblockingFilterDisabled = true, CuQpDeltaEnabled = true, DiffCuQpDeltaDepth = 1, CbQpOffset = -3, CrQpOffset = 5 },
                3 => new HevcPictureParameters(initQp) { DeblockingFilterOverrideEnabled = true, BetaOffsetDiv2 = -2, TcOffsetDiv2 = 3, SliceChromaQpOffsetsPresent = true, CabacInitPresent = true },
                4 => new HevcPictureParameters(initQp) { ConstrainedIntraPred = true, TransquantBypassEnabled = true, PicParameterSetId = 3 },
                _ => new HevcPictureParameters(initQp),
            };
            if (tools.HasFlag(HevcCodingTools.LargeTransformSkipBlocks))
            {
                picture = picture with { TransformSkipEnabled = true, Log2MaxTransformSkipBlockSizeMinus2 = sequence.MaxTbLog2SizeY - 2, CrossComponentPredictionEnabled = chromaFormatIdc is 3, Log2SaoOffsetScaleLuma = bitDepth - 10 > 0 ? 1 : 0 };
            }

            var format = chromaFormatIdc switch
            {
                0 => "400",
                1 => "420",
                2 => "422",
                _ => "444",
            };
            var name = string.Create(CultureInfo.InvariantCulture, $"case{index:D2}-{format}-{bitDepth}bit-{width}x{height}-{kind}-ctb{1 << ctbLog2SizeY}");
            cases.Add(new StreamCase(name, sequence, picture, kind, profile, Seed: 1000 + index));
        }
    }

    private static async Task<(string Report, string? Failure)> RunAsync(IReadOnlyList<HevcReferenceDecoder> decoders, FFmpegTool ffmpeg, StreamCase streamCase, FullPath artifacts, CancellationToken cancellationToken)
    {
        var work = artifacts / "work" / streamCase.Name;
        Directory.CreateDirectory(work);
        try
        {
            var written = WriteStream(streamCase);
            var stream = work / "stream.hevc";
            await File.WriteAllBytesAsync(stream, written.ByteStream, cancellationToken);
            var problems = new List<string>();
            if (Annex.GetProfileName(streamCase.Sequence.Profile) != streamCase.Profile)
            {
                problems.Add($"the sequence announces the profile '{Annex.GetProfileName(streamCase.Sequence.Profile)}' instead of '{streamCase.Profile}'");
            }

            // The three decoders: exactly the samples that were written, cropped to the conformance window
            foreach (var decoder in decoders)
            {
                var result = await decoder.DecodeAsync(stream, work / decoder.Name, cancellationToken);
                if (decoder.Name is HevcReferenceDecoder.Dec265 && written.Sps is { ChromaFormatIdc: 0, BitDepth: 10, ConformanceWindowFlag: true })
                {
                    // A known defect of the pinned dec265, which is not about these streams (see the remarks of the
                    // class). The day it decodes one of them, it counts
                    if (result.Succeeded)
                    {
                        problems.Add("dec265 decodes a 10-bit monochrome stream with a conformance window, which it did not: remove its exception from the test");
                    }

                    continue;
                }

                if (!result.Succeeded)
                {
                    problems.Add($"{decoder.Name} does not decode the stream: {result.Error}");
                }
                else if (!result.Planes.Span.SequenceEqual(written.Expected))
                {
                    var actual = result.Planes.Span;
                    problems.Add(string.Create(CultureInfo.InvariantCulture, $"{decoder.Name} outputs {actual.Length} bytes that differ from the {written.Expected.Length} bytes that were written at the byte {actual.CommonPrefixLength(written.Expected)}"));
                }
            }

            // The headers as a third implementation reads them
            var trace = await ffmpeg.TraceHeadersAsync(stream, "hevc", cancellationToken);
            problems.AddRange(CheckTrace(trace, streamCase, written));

            // The decoder of the library
            if (DecodeWithLibrary(streamCase.Name, written) is { } libraryProblem)
            {
                problems.Add(libraryProblem);
            }

            var description = string.Create(CultureInfo.InvariantCulture, $"{streamCase.Name}: {streamCase.Profile}, level {streamCase.Sequence.GeneralLevelIdc / 30.0:F1}, {written.ByteStream.Length} bytes");
            if (problems.Count is 0)
                return (description + ": identical", null);

            // The stream and its headers are kept for the diagnosis
            var copy = artifacts / (streamCase.Name + ".hevc");
            File.Copy(stream, copy, overwrite: true);
            await File.WriteAllTextAsync(artifacts / (streamCase.Name + ".trace.txt"), trace, cancellationToken);
            return (description + ": FAILED", $"{streamCase.Name} ({streamCase.Profile}): {string.Join("; ", problems)}. Stream: '{copy}'.");
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    /// <summary>
    /// Writes a stream as the encoder will: each parameter set is written, then read back by the parser of the decoder, and
    /// what follows it is written from what was read.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static WrittenStream WriteStream(StreamCase streamCase)
    {
        var random = new Random(streamCase.Seed);
        var scope = AllocationScope.Create(ImageConfiguration.Default, streamCase.Name);
        using var rbsp = new PooledPayloadWriter(scope);
        using var byteStream = new PooledPayloadWriter(scope);
        HevcHeaderWriter.WriteVideoParameterSet(rbsp, streamCase.Sequence);
        _ = HevcVideoParameterSet.Parse(rbsp.WrittenSpan);
        HevcNalWriter.WriteByteStream(byteStream, HevcConstants.NalVps, 0, rbsp.WrittenSpan);
        rbsp.Reset();
        HevcHeaderWriter.WriteSequenceParameterSet(rbsp, streamCase.Sequence);
        var sps = HevcSequenceParameterSet.ParseRbsp(rbsp.WrittenSpan);
        HevcNalWriter.WriteByteStream(byteStream, HevcConstants.NalSps, 0, rbsp.WrittenSpan);
        rbsp.Reset();
        HevcHeaderWriter.WritePictureParameterSet(rbsp, sps, streamCase.Picture);
        var pps = HevcPictureParameterSet.ParseRbsp(rbsp.WrittenSpan);
        HevcNalWriter.WriteByteStream(byteStream, HevcConstants.NalPps, 0, rbsp.WrittenSpan);
        using var layout = HevcPictureLayout.Create(scope, sps, pps);

        var pictureCount = streamCase.Kind switch
        {
            SequenceKind.Still => 1,
            SequenceKind.IntraOnly => 2,
            _ => 3,
        };
        var planeCount = sps.ComponentCount;
        var bytesPerSample = sps.BitDepth > 8 ? 2 : 1;
        using var expected = new MemoryStream();
        for (var pictureIndex = 0; pictureIndex < pictureCount; pictureIndex++)
        {
            // Random samples of the coded size, the part outside the conformance window included. A PCM sample of less
            // bits than the samples is their most significant bits
            var planes = new ushort[planeCount][];
            var md5 = new byte[planeCount * 16];
            for (var cIdx = 0; cIdx < planeCount; cIdx++)
            {
                var subWidth = cIdx is 0 ? 1 : sps.SubWidthC;
                var subHeight = cIdx is 0 ? 1 : sps.SubHeightC;
                var planeWidth = sps.PicWidthInLumaSamples / subWidth;
                var planeHeight = sps.PicHeightInLumaSamples / subHeight;
                var plane = new ushort[planeWidth * planeHeight];
                var pcmBitDepth = cIdx is 0 ? sps.PcmBitDepthY : sps.PcmBitDepthC;
                for (var i = 0; i < plane.Length; i++)
                {
                    plane[i] = (ushort)(random.Next(1 << pcmBitDepth) << (sps.BitDepth - pcmBitDepth));
                }

                planes[cIdx] = plane;
                ReferenceHevcPictureHash.ComputeMd5(plane, planeWidth, planeHeight, planeWidth, sps.BitDepth).CopyTo(md5, cIdx * 16);
                for (var y = 0; y < sps.OutputHeight / subHeight; y++)
                {
                    for (var x = 0; x < sps.OutputWidth / subWidth; x++)
                    {
                        var sample = plane[(y * planeWidth) + x];
                        expected.WriteByte((byte)sample);
                        if (bytesPerSample is 2)
                        {
                            expected.WriteByte((byte)(sample >> 8));
                        }
                    }
                }
            }

            // A key frame, then pictures in P slices; the syntax elements of the slice change with the picture
            var sliceQp = Math.Clamp(26 + pps.InitQpMinus26 + (3 * pictureIndex) - 4, -sps.QpBdOffset, 51);
            var slice = (pictureIndex is 0 || streamCase.Kind is not SequenceKind.Inter ? HevcSliceParameters.CreateKeyFrame(sliceQp) : HevcSliceParameters.CreateInterFrame(pictureIndex, sliceQp)) with
            {
                SliceSaoLuma = pictureIndex % 2 is 0,
                SliceSaoChroma = streamCase.Seed % 4 is 3,
                SliceTemporalMvpEnabled = pictureIndex is 2,
                CabacInit = pictureIndex is 1,
                FiveMinusMaxNumMergeCand = streamCase.Seed % 5,
                SliceCbQpOffset = pictureIndex - 1,
                SliceCrQpOffset = 1 - pictureIndex,
                DeblockingFilterOverride = pictureIndex is not 1,
                SliceDeblockingFilterDisabled = pictureIndex is 0,
                SliceBetaOffsetDiv2 = 4,
                SliceTcOffsetDiv2 = -5,
            };
            rbsp.Reset();
            HevcHeaderWriter.WriteSliceSegmentHeader(rbsp, sps, pps, slice);
            var nal = HevcNalUnitHeader.Read([(byte)(slice.NalUnitType << 1), 1]);
            var header = HevcSliceSegmentHeader.Parse(rbsp.WrittenSpan, nal, layout, sliceHeader: null);
            Assert.Equal(rbsp.Length, header.DataOffset);
            new PcmSliceWriter(rbsp, layout, header, planes, random).Write();
            HevcNalWriter.WriteByteStream(byteStream, slice.NalUnitType, 0, rbsp.WrittenSpan);

            // The hash of the picture, computed by the test harness
            rbsp.Reset();
            HevcHeaderWriter.WriteDecodedPictureHash(rbsp, new HevcDecodedPictureHash(HevcDecodedPictureHash.Md5, planeCount, md5, new uint[planeCount]));
            HevcNalWriter.WriteByteStream(byteStream, HevcConstants.NalSuffixSei, 0, rbsp.WrittenSpan);
        }

        return new WrittenStream(byteStream.WrittenSpan.ToArray(), expected.ToArray(), sps, HasInterSlices: streamCase.Kind is SequenceKind.Inter);
    }

    /// <summary>
    /// The profile, the tier, the level and the constraint flags that <c>trace_headers</c> reads in the video parameter set
    /// and in the sequence parameter set, against Annex A; and the sample format and the size of the pictures.
    /// </summary>
    private static List<string> CheckTrace(string trace, StreamCase streamCase, WrittenStream written)
    {
        var problems = new List<string>();
        var sequence = streamCase.Sequence;
        var (profileIdc, compatibility, flags) = Annex.GetProfile(streamCase.Profile);

        // profile_tier_level( ) of the video parameter set and of the sequence parameter set
        Expect("general_profile_space", 0);
        Expect("general_tier_flag", 0);
        Expect("general_profile_idc", profileIdc);
        for (var j = 0; j < 32; j++)
        {
            Expect(string.Create(CultureInfo.InvariantCulture, $"general_profile_compatibility_flag[{j}]"), compatibility.Contains(j) ? 1 : 0);
        }

        Expect("general_progressive_source_flag", 1);
        Expect("general_interlaced_source_flag", 0);
        Expect("general_non_packed_constraint_flag", 1);
        Expect("general_frame_only_constraint_flag", 1);
        if (profileIdc is 4)
        {
            // Table A.2
            string[] names = ["general_max_12bit_constraint_flag", "general_max_10bit_constraint_flag", "general_max_8bit_constraint_flag", "general_max_422chroma_constraint_flag", "general_max_420chroma_constraint_flag", "general_max_monochrome_constraint_flag", "general_intra_constraint_flag", "general_one_picture_only_constraint_flag", "general_lower_bit_rate_constraint_flag"];
            for (var i = 0; i < names.Length; i++)
            {
                Expect(names[i], flags[i] is '0' ? 0 : 1);
            }
        }
        else if (profileIdc is 2)
        {
            // Clause A.3.3: the flag of the Main 10 Still Picture profile
            Expect("general_one_picture_only_constraint_flag", flags[7] is '1' ? 1 : 0);
        }

        Expect("general_level_idc", Annex.GetLevelIdc(written.Sps.PicWidthInLumaSamples, written.Sps.PicHeightInLumaSamples));

        // The NAL units: the three parameter sets, a slice segment of a key frame (IDR_N_LP) and the suffix SEI NAL unit
        // of its hash, then for each picture that follows a key frame, a slice segment (TRAIL_R) and its hash
        long[] nalUnitTypes = streamCase.Kind is SequenceKind.Inter
            ? [HevcConstants.NalTrailR, HevcConstants.NalIdrNLp, HevcConstants.NalVps, HevcConstants.NalSps, HevcConstants.NalPps, HevcConstants.NalSuffixSei]
            : [HevcConstants.NalIdrNLp, HevcConstants.NalVps, HevcConstants.NalSps, HevcConstants.NalPps, HevcConstants.NalSuffixSei];
        var tracedTypes = GetTraceValues(trace, "nal_unit_type").Distinct().Order().ToArray();
        if (!tracedTypes.SequenceEqual(nalUnitTypes))
        {
            problems.Add($"trace_headers reads the NAL unit types [{string.Join(", ", tracedTypes)}] instead of [{string.Join(", ", nalUnitTypes)}]");
        }

        Expect("nuh_layer_id", 0);
        Expect("nuh_temporal_id_plus1", 1);

        // The sequence parameter set
        Expect("chroma_format_idc", sequence.ChromaFormatIdc);
        Expect("pic_width_in_luma_samples", written.Sps.PicWidthInLumaSamples);
        Expect("pic_height_in_luma_samples", written.Sps.PicHeightInLumaSamples);
        Expect("bit_depth_luma_minus8", sequence.BitDepth - 8);
        Expect("bit_depth_chroma_minus8", sequence.BitDepth - 8);
        Expect("conformance_window_flag", sequence.OutputWidth != written.Sps.PicWidthInLumaSamples || sequence.OutputHeight != written.Sps.PicHeightInLumaSamples ? 1 : 0);
        Expect("sps_max_dec_pic_buffering_minus1[0]", streamCase.Kind is SequenceKind.Inter ? 1 : 0);
        Expect("video_full_range_flag", sequence.FullRange ? 1 : 0);
        Expect("colour_primaries", sequence.ColourPrimaries);
        Expect("transfer_characteristics", sequence.TransferCharacteristics);
        Expect("matrix_coefficients", sequence.MatrixCoeffs);
        Expect("pcm_enabled_flag", 1);
        Expect("pcm_loop_filter_disabled_flag", 1);
        return problems;

        // The parameter sets are traced each time FFmpeg meets them: in the stream, and in what it keeps of its start
        void Expect(string element, long value)
        {
            var values = GetTraceValues(trace, element);
            if (values.Count is 0 || values.Any(actual => actual != value))
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"trace_headers reads {element} = [{string.Join(", ", values)}] instead of {value}"));
            }
        }
    }

    /// <summary>Returns the values that a <c>trace_headers</c> log gives to a syntax element, in order.</summary>
    private static List<long> GetTraceValues(string trace, string element)
    {
        var values = new List<long>();
        foreach (var line in trace.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            // "[trace_headers @ 0x...] 40          general_profile_idc                                     00100 = 4"
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 && parts[^2] is "=" && parts[^4] == element && long.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>
    /// Decodes a stream with the library and compares its pictures with the samples that were written; the hash that the
    /// stream carries for each picture is checked with the functions of the test harness. A stream with P slices only
    /// counts once the decoder decodes them.
    /// </summary>
    private static string? DecodeWithLibrary(string name, WrittenStream written)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        using var decoder = new HevcDecoder(scope);
        using var actual = new MemoryStream();
        var pictures = 0;
        try
        {
            foreach (var accessUnit in ReferenceHevcStream.GroupAccessUnits(ReferenceHevcStream.SplitAnnexB(written.ByteStream)))
            {
                var input = ReferenceHevcStream.ToLengthPrefixed(accessUnit);
                decoder.SetInput(input, 0, input.Length, nalLengthSize: 4);
                if (Drain() is { } problem)
                    return problem;
            }

            decoder.EndOfSequence();
            if (Drain() is { } lastProblem)
                return lastProblem;
        }
        catch (UnsupportedImageFeatureException) when (written.HasInterSlices)
        {
            return null;
        }
        catch (Exception exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException)
        {
            return $"the library does not decode the stream: {exception.Message}";
        }

        if (!actual.GetBuffer().AsSpan(0, (int)actual.Length).SequenceEqual(written.Expected))
            return string.Create(CultureInfo.InvariantCulture, $"the library outputs {pictures} picture(s) that differ from the samples that were written");

        return null;

        string? Drain()
        {
            while (decoder.DecodeNextOutputPicture(XunitCancellationToken) is { } picture)
            {
                try
                {
                    var sps = picture.Sps;
                    var hash = picture.DecodedPictureHash;
                    if (hash is null || hash.HashType is not HevcDecodedPictureHash.Md5)
                        return "a picture has no MD5 hash";

                    for (var cIdx = 0; cIdx < picture.PlaneCount; cIdx++)
                    {
                        var subWidth = cIdx is 0 ? 1 : sps.SubWidthC;
                        var subHeight = cIdx is 0 ? 1 : sps.SubHeightC;
                        var plane = picture.GetPlane(cIdx);
                        var md5 = ReferenceHevcPictureHash.ComputeMd5(plane.Samples, sps.PicWidthInLumaSamples / subWidth, sps.PicHeightInLumaSamples / subHeight, plane.Stride, sps.BitDepth);
                        if (!hash.GetMd5(cIdx).SequenceEqual(md5))
                            return string.Create(CultureInfo.InvariantCulture, $"the plane {cIdx} of the picture {pictures} that the library decodes does not have the hash of the stream");

                        for (var y = 0; y < sps.OutputHeight / subHeight; y++)
                        {
                            foreach (var sample in plane.GetRow(y)[..(sps.OutputWidth / subWidth)])
                            {
                                actual.WriteByte((byte)sample);
                                if (sps.BitDepth > 8)
                                {
                                    actual.WriteByte((byte)(sample >> 8));
                                }
                            }
                        }
                    }

                    pictures++;
                }
                finally
                {
                    picture.Release();
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Writes the slice segment data of a picture (clause 7.3.8.1) whose coding units are all PCM coding units, of sizes
    /// that a seed chooses: the syntax elements of the coding quadtree and of the coding units with the context variables
    /// that their parsing reads (clause 9.3.4.2), written here from the syntax tables.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private sealed class PcmSliceWriter
    {
        private readonly PooledPayloadWriter _output;
        private readonly HevcSequenceParameterSet _sps;
        private readonly HevcPictureParameterSet _pps;
        private readonly HevcSliceSegmentHeader _header;
        private readonly ushort[][] _planes;
        private readonly Random _random;
        private readonly HevcCabacDecoder _contexts = new();
        private readonly HevcCabacEncoder _encoder = new();

        // CtDepth of each minimum coding block: the context of split_cu_flag
        private readonly int[] _ctDepth;
        private readonly int _widthInMinCbs;

        public PcmSliceWriter(PooledPayloadWriter output, HevcPictureLayout layout, HevcSliceSegmentHeader header, ushort[][] planes, Random random)
        {
            _output = output;
            _sps = layout.Sps;
            _pps = layout.Pps;
            _header = header;
            _planes = planes;
            _random = random;
            _widthInMinCbs = _sps.PicWidthInLumaSamples >> _sps.MinCbLog2SizeY;
            _ctDepth = new int[_widthInMinCbs * (_sps.PicHeightInLumaSamples >> _sps.MinCbLog2SizeY)];
        }

        private Span<byte> Contexts => _contexts.Contexts;

        public void Write()
        {
            // Clause 9.3.2.2: the context variables of the slice, those of a decoder
            _contexts.InitializeContexts(_header.InitType, _header.SliceQpY);
            _encoder.Initialize(_output);
            for (var ctbAddr = 0; ctbAddr < _sps.PicSizeInCtbsY; ctbAddr++)
            {
                // Clause 7.3.8.2 "Coding tree unit syntax"
                var rx = ctbAddr % _sps.PicWidthInCtbsY;
                var ry = ctbAddr / _sps.PicWidthInCtbsY;
                if (_header.SliceSaoLumaFlag || _header.SliceSaoChromaFlag)
                {
                    WriteSao(rx, ry);
                }

                WriteCodingQuadtree(rx << _sps.CtbLog2SizeY, ry << _sps.CtbLog2SizeY, _sps.CtbLog2SizeY, 0);

                // end_of_slice_segment_flag: the codeword ends with rbsp_slice_segment_trailing_bits( )
                _encoder.EncodeTerminate(ctbAddr == _sps.PicSizeInCtbsY - 1 ? 1 : 0);
            }

            Assert.False(_encoder.IsActive);
        }

        // Clause 7.3.8.3 "Sample adaptive offset syntax": the offsets are not applied (SaoTypeIdx 0), or are those of the
        // coding tree block to the left or above, which are not applied either
        private void WriteSao(int rx, int ry)
        {
            var saoMergeLeftFlag = false;
            var saoMergeUpFlag = false;
            if (rx > 0)
            {
                saoMergeLeftFlag = _random.Next(3) is 0;
                _encoder.EncodeDecision(ref Contexts[HevcContextLayout.SaoMergeFlag], saoMergeLeftFlag ? 1 : 0);
            }

            if (ry > 0 && !saoMergeLeftFlag)
            {
                saoMergeUpFlag = _random.Next(3) is 0;
                _encoder.EncodeDecision(ref Contexts[HevcContextLayout.SaoMergeFlag], saoMergeUpFlag ? 1 : 0);
            }

            if (saoMergeLeftFlag || saoMergeUpFlag)
                return;

            // sao_type_idx_luma, then sao_type_idx_chroma for both chroma components: the first bin of a truncated Rice
            // binarization, equal to 0
            if (_header.SliceSaoLumaFlag)
            {
                _encoder.EncodeDecision(ref Contexts[HevcContextLayout.SaoTypeIdx], 0);
            }

            if (_header.SliceSaoChromaFlag)
            {
                _encoder.EncodeDecision(ref Contexts[HevcContextLayout.SaoTypeIdx], 0);
            }
        }

        // Clause 7.3.8.4 "Coding quadtree syntax"
        private void WriteCodingQuadtree(int x0, int y0, int log2CbSize, int cqtDepth)
        {
            var size = 1 << log2CbSize;
            bool splitCuFlag;
            if (x0 + size <= _sps.PicWidthInLumaSamples && y0 + size <= _sps.PicHeightInLumaSamples && log2CbSize > _sps.MinCbLog2SizeY)
            {
                // A coding unit of more than the largest PCM size cannot be a PCM coding unit
                splitCuFlag = log2CbSize > _sps.Log2MaxIpcmCbSizeY || _random.Next(2) is 0;

                // Clause 9.3.4.2.2: the coding quadtree depth of the blocks to the left and above, which are available
                // when they are in the picture: one slice, no tile
                var ctxInc = 0;
                if (x0 > 0 && GetCtDepth(x0 - 1, y0) > cqtDepth)
                {
                    ctxInc++;
                }

                if (y0 > 0 && GetCtDepth(x0, y0 - 1) > cqtDepth)
                {
                    ctxInc++;
                }

                _encoder.EncodeDecision(ref Contexts[HevcContextLayout.SplitCuFlag + ctxInc], splitCuFlag ? 1 : 0);
            }
            else
            {
                // Clause 7.4.9.4: inferred for a block that crosses the edge of the picture, and for the minimum size
                splitCuFlag = log2CbSize > _sps.MinCbLog2SizeY;
            }

            if (!splitCuFlag)
            {
                WriteCodingUnit(x0, y0, log2CbSize, cqtDepth);
                return;
            }

            var x1 = x0 + (size >> 1);
            var y1 = y0 + (size >> 1);
            WriteCodingQuadtree(x0, y0, log2CbSize - 1, cqtDepth + 1);
            if (x1 < _sps.PicWidthInLumaSamples)
            {
                WriteCodingQuadtree(x1, y0, log2CbSize - 1, cqtDepth + 1);
            }

            if (y1 < _sps.PicHeightInLumaSamples)
            {
                WriteCodingQuadtree(x0, y1, log2CbSize - 1, cqtDepth + 1);
            }

            if (x1 < _sps.PicWidthInLumaSamples && y1 < _sps.PicHeightInLumaSamples)
            {
                WriteCodingQuadtree(x1, y1, log2CbSize - 1, cqtDepth + 1);
            }
        }

        private int GetCtDepth(int x, int y) => _ctDepth[((y >> _sps.MinCbLog2SizeY) * _widthInMinCbs) + (x >> _sps.MinCbLog2SizeY)];

        // Clause 7.3.8.5 "Coding unit syntax", for a PCM coding unit
        private void WriteCodingUnit(int x0, int y0, int log2CbSize, int cqtDepth)
        {
            Assert.InRange(log2CbSize, _sps.Log2MinIpcmCbSizeY, _sps.Log2MaxIpcmCbSizeY);
            if (_pps.TransquantBypassEnabledFlag)
            {
                _encoder.EncodeDecision(ref Contexts[HevcContextLayout.CuTransquantBypassFlag], _random.Next(2));
            }

            if (_header.SliceType is not HevcConstants.SliceI)
            {
                // cu_skip_flag 0: its context is the number of skipped coding units to the left and above, none here.
                // pred_mode_flag 1: MODE_INTRA
                _encoder.EncodeDecision(ref Contexts[HevcContextLayout.CuSkipFlag], 0);
                _encoder.EncodeDecision(ref Contexts[HevcContextLayout.PredModeFlag], 1);
            }

            if (log2CbSize == _sps.MinCbLog2SizeY)
            {
                // part_mode PART_2Nx2N: the first bin of an intra coding unit, equal to 1 (Table 9-43)
                _encoder.EncodeDecision(ref Contexts[HevcContextLayout.PartMode], 1);
            }

            // pcm_flag, which terminates the codeword; pcm_alignment_zero_bit: the encoder completed the byte
            _encoder.EncodeTerminate(1);

            // Clause 7.3.8.7 "PCM sample syntax"
            var writer = new HevcBitWriter(_output);
            var nCbS = 1 << log2CbSize;
            WriteSamples(ref writer, _planes[0], _sps.PicWidthInLumaSamples, x0, y0, nCbS, nCbS, _sps.PcmBitDepthY);
            for (var cIdx = 1; cIdx < _planes.Length; cIdx++)
            {
                WriteSamples(ref writer, _planes[cIdx], _sps.PicWidthInLumaSamples / _sps.SubWidthC, x0 / _sps.SubWidthC, y0 / _sps.SubHeightC, nCbS / _sps.SubWidthC, nCbS / _sps.SubHeightC, _sps.PcmBitDepthC);
            }

            // Clause 9.3.2.6: the arithmetic coding starts again after the samples, with the same context variables
            Assert.True(writer.IsByteAligned);
            _encoder.Initialize(_output);
            for (var y = y0 >> _sps.MinCbLog2SizeY; y < (y0 + nCbS) >> _sps.MinCbLog2SizeY; y++)
            {
                _ctDepth.AsSpan((y * _widthInMinCbs) + (x0 >> _sps.MinCbLog2SizeY), nCbS >> _sps.MinCbLog2SizeY).Fill(cqtDepth);
            }
        }

        private void WriteSamples(ref HevcBitWriter writer, ushort[] plane, int stride, int x0, int y0, int width, int height, int pcmBitDepth)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    writer.WriteBits((uint)(plane[((y0 + y) * stride) + x0 + x] >> (_sps.BitDepth - pcmBitDepth)), pcmBitDepth);
                }
            }
        }
    }

    /// <summary>
    /// Annex A of Rec. ITU-T H.265, written out from the specification: what each profile announces (clauses A.3.2 to
    /// A.3.5, Table A.2) and the picture sizes of the levels (Table A.8).
    /// </summary>
    private static class Annex
    {
        /// <summary>
        /// Returns <c>general_profile_idc</c>, the indices of the compatibility flags that are set, and the nine flags of
        /// Table A.2 from <c>general_max_12bit_constraint_flag</c> to <c>general_lower_bit_rate_constraint_flag</c>.
        /// </summary>
        public static (int ProfileIdc, int[] CompatibilityFlags, string ConstraintFlags) GetProfile(string name) => name switch
        {
            // The profiles of the first version: the flags of Table A.2 are reserved bits equal to 0, but the flag of a
            // single picture for the profile 2
            "Main" => (1, [1, 2], "000000000"),
            "Main 10" => (2, [2], "000000000"),
            "Main 10 Still Picture" => (2, [2], "000000010"),
            "Main Still Picture" => (3, [1, 2, 3], "000000000"),

            // Table A.2; general_lower_bit_rate_constraint_flag may be 0 or 1 for the intra profiles: the encoder sets it
            "Monochrome" => (4, [4], "111111001"),
            "Monochrome 10" => (4, [4], "110111001"),
            "Monochrome 12" => (4, [4], "100111001"),
            "Main 12" => (4, [4], "100110001"),
            "Main 4:2:2 10" => (4, [4], "110100001"),
            "Main 4:2:2 12" => (4, [4], "100100001"),
            "Main 4:4:4" => (4, [4], "111000001"),
            "Main 4:4:4 10" => (4, [4], "110000001"),
            "Main 4:4:4 12" => (4, [4], "100000001"),
            "Main 12 Intra" => (4, [4], "100110101"),
            "Main 4:2:2 10 Intra" => (4, [4], "110100101"),
            "Main 4:2:2 12 Intra" => (4, [4], "100100101"),
            "Main 4:4:4 Intra" => (4, [4], "111000101"),
            "Main 4:4:4 10 Intra" => (4, [4], "110000101"),
            "Main 4:4:4 12 Intra" => (4, [4], "100000101"),
            "Main 4:4:4 Still Picture" => (4, [4], "111000111"),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown profile."),
        };

        public static string GetProfileName(HevcProfile profile) => profile switch
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

        /// <summary>Returns <c>general_level_idc</c> of the lowest level whose <c>MaxLumaPs</c> holds a picture (Table A.8, levels 1 to 4).</summary>
        public static int GetLevelIdc(int width, int height)
        {
            (int LevelIdc, int MaxLumaPs)[] levels = [(30, 36864), (60, 122880), (63, 245760), (90, 552960), (93, 983040), (120, 2228224)];
            foreach (var (levelIdc, maxLumaPs) in levels)
            {
                if ((long)width * height <= maxLumaPs && (long)width * width <= 8L * maxLumaPs && (long)height * height <= 8L * maxLumaPs)
                    return levelIdc;
            }

            throw new ArgumentOutOfRangeException(nameof(width), "The picture needs a level above 4.");
        }
    }
}
