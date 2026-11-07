using System.Globalization;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Hevc;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// The HEVC encoder against independent decoders: the native decoder of FFmpeg, dec265 (libde265) and TAppDecoder (HM,
/// which checks what a profile allows) must each output exactly the pictures that the encoder reconstructed, for every
/// chroma format and bit depth, lossless and lossy, for single pictures and for sequences, over a spread of efforts and
/// qualities. The <c>trace_headers</c> bitstream filter of FFmpeg must read the profile and the level that the format
/// needs, and TAppDecoder must find that its pictures have the hash that the stream carries.
/// </summary>
/// <remarks>
/// <para>
/// The pinned dec265 aborts (<c>std::bad_alloc</c>) once it has written the luma samples of a 10-bit or 12-bit monochrome
/// picture that has a conformance cropping window, whatever wrote the stream (see <see cref="HevcHeaderWriterInteropTests"/>):
/// it is not asked for the samples of these streams.
/// </para>
/// <para>
/// The native decoder of the pinned FFmpeg outputs other pictures for the 12-bit streams whose slices have a quantization
/// parameter of 50 or 51 (the qualities 0 to 2 of the encoder), in every chroma format, for single pictures and for
/// sequences. It reports no error. dec265 and TAppDecoder output the pictures of the encoder for these streams, and
/// TAppDecoder finds that they have the hash of the stream: FFmpeg is not asked to agree on them, and the day it does, it
/// counts.
/// </para>
/// </remarks>
public sealed class HevcEncoderInteropTests
{
    private static readonly (int Width, int Height)[] Sizes = [(48, 40), (33, 17), (64, 64), (21, 35), (72, 24), (16, 16), (65, 33), (40, 56), (8, 8), (96, 32), (31, 63), (56, 40)];

    public static TheoryData<int, int, bool, string, int, int, int, int> Cases()
    {
        var data = new TheoryData<int, int, bool, string, int, int, int, int>();
        var index = 0;
        foreach (var kind in new[] { "still", "intra", "sequence" })
        {
            foreach (var lossless in new[] { true, false })
            {
                for (var chromaFormatIdc = 0; chromaFormatIdc < 4; chromaFormatIdc++)
                {
                    for (var bitDepth = 8; bitDepth <= 12; bitDepth += 2)
                    {
                        // The sizes, the efforts and the qualities rotate; the highest efforts get the smallest pictures
                        var (width, height) = Sizes[index % Sizes.Length];
                        var effort = (index * 7) % 10;
                        if (effort >= 8)
                        {
                            (width, height) = (Math.Min(width, 33), Math.Min(height, 24));
                        }

                        data.Add(chromaFormatIdc, bitDepth, lossless, kind, width, height, effort, 15 + ((index * 23) % 80));
                        index++;
                    }
                }
            }
        }

        // The smallest picture, and a picture of one sample more than a coding tree block
        data.Add(1, 8, true, "still", 1, 1, 5, 50);
        data.Add(1, 8, false, "still", 1, 1, 9, 50);
        data.Add(3, 10, true, "still", 33, 33, 3, 50);
        data.Add(2, 8, false, "still", 33, 33, 6, 95);

        // The two ends of the qualities at the two ends of the efforts
        foreach (var effort in new[] { 0, 9 })
        {
            data.Add(1, 8, false, "still", 40, 24, effort, 0);
            data.Add(1, 8, false, "still", 40, 24, effort, 100);
            data.Add(3, 12, false, "still", 24, 24, effort, 100);
        }

        // The largest quantization parameters of 12-bit streams (51, and 50 for the quality 2), which FFmpeg does not
        // decode as the other decoders do, and the next one (49, for the quality 3), which it does
        foreach (var quality in new[] { 0, 2, 3 })
        {
            data.Add(1, 12, false, "still", 24, 24, 1, quality);
            data.Add(3, 12, false, "sequence", 24, 24, 1, quality);
        }

        data.Add(0, 12, false, "still", 24, 24, 4, 1);
        data.Add(2, 12, false, "still", 24, 24, 4, 0);

        // The same quantization parameters at 10 bits, which every decoder decodes
        data.Add(3, 10, false, "still", 24, 24, 1, 0);

        // Sequences at the two ends of the efforts, and one of several rows of coding tree units
        foreach (var effort in new[] { 0, 9 })
        {
            data.Add(1, 8, false, "sequence", 40, 24, effort, 30);
            data.Add(3, 10, true, "sequence", 24, 24, effort, 100);
        }

        data.Add(1, 8, false, "sequence", 200, 136, 6, 55);
        data.Add(2, 10, true, "sequence", 136, 72, 4, 100);

        // Five pictures with a key frame every two pictures: a picture refers to a key frame that is not the first one
        data.Add(1, 8, false, "interval", 72, 40, 5, 60);
        data.Add(3, 10, true, "interval", 40, 24, 2, 100);
        data.Add(0, 12, false, "interval", 56, 56, 7, 35);

        // A picture of several rows of coding tree units, with every tool of the lossy pictures
        data.Add(1, 8, false, "still", 200, 136, 7, 45);
        data.Add(1, 10, true, "still", 136, 104, 6, 100);

        // Pictures of level 5 and of level 6 by their width, which is more than Sqrt( 8 * MaxLumaPs ) of the level before
        // (clause A.4.1, item b): TAppDecoder checks that their coding tree blocks have 32 samples at least (item d)
        data.Add(1, 8, false, "still", 4224, 9, 2, 60);
        data.Add(3, 8, true, "sequence", 8448, 8, 1, 100);

        // Rows of coding tree blocks that are substreams of the wavefront, with their entry points ("-wpp"): lossy (written
        // after the picture is filtered) and lossless (written as they are searched), one and two coding tree blocks wide
        data.Add(1, 8, false, "still-wpp", 200, 136, 7, 45);
        data.Add(3, 10, true, "still-wpp", 136, 104, 6, 100);
        data.Add(2, 8, false, "sequence-wpp", 72, 200, 3, 50);
        data.Add(0, 12, true, "intra-wpp", 64, 136, 2, 100);
        data.Add(1, 8, false, "still-wpp", 120, 300, 0, 85);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public Task StreamsAreDecodedByTheReferenceDecoders(int chromaFormatIdc, int bitDepth, bool lossless, string kind, int width, int height, int effort, int quality)
        => CheckReferenceDecoders(chromaFormatIdc, bitDepth, lossless, kind, width, height, effort, quality, optimizeQuantization: false);

    [Theory]
    [InlineData(0, 8, "still", 0, 0)]
    [InlineData(1, 8, "still", 2, 50)]
    [InlineData(1, 10, "still-wpp", 5, 75)]
    [InlineData(2, 12, "sequence", 6, 40)]
    [InlineData(3, 10, "interval", 7, 100)]
    [InlineData(3, 8, "sequence-wpp", 6, 25)]
    public Task OptimizedQuantizationIsDecodedByTheReferenceDecoders(int chromaFormatIdc, int bitDepth, string kind, int effort, int quality)
        => CheckReferenceDecoders(chromaFormatIdc, bitDepth, lossless: false, kind, width: 64, height: 64, effort, quality, optimizeQuantization: true);

    private static async Task CheckReferenceDecoders(int chromaFormatIdc, int bitDepth, bool lossless, string kind, int width, int height, int effort, int quality, bool optimizeQuantization)
    {
        var cancellationToken = XunitCancellationToken;
        var decoders = await RequiredTools.HevcReferenceDecodersAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var format = chromaFormatIdc switch
        {
            0 => "400",
            1 => "420",
            2 => "422",
            _ => "444",
        };
        var name = string.Create(CultureInfo.InvariantCulture, $"{format}-{bitDepth}bit-{width}x{height}-{kind}-{(lossless ? "lossless" : "q" + quality.ToString(CultureInfo.InvariantCulture))}-e{effort}{(optimizeQuantization ? "-optimized" : "")}");
        var wavefront = kind.EndsWith("-wpp", StringComparison.Ordinal);
        kind = wavefront ? kind[..^4] : kind;
        var artifacts = InteropSettings.GetArtifactsDirectory(nameof(HevcEncoderInteropTests));
        var work = artifacts / "work" / name;
        Directory.CreateDirectory(work);
        try
        {
            var pictureCount = kind switch
            {
                "still" => 1,
                "interval" => 5,
                _ => 3,
            };
            var keyFrameInterval = kind switch
            {
                "intra" => 1,
                "interval" => 2,
                _ => 0,
            };
            var settings = new HevcEncoderSettings(width, height, bitDepth, chromaFormatIdc, ColourPrimaries: 1, TransferCharacteristics: 13, MatrixCoeffs: chromaFormatIdc is 3 ? 0 : 6, FullRange: true, ChromaSampleLocType: 0, StillPicture: kind is "still", lossless, quality, effort, keyFrameInterval, wavefront, optimizeQuantization);
            var encoded = Encode(name, settings, pictureCount, cancellationToken);
            var stream = work / "stream.hevc";
            await File.WriteAllBytesAsync(stream, encoded.ByteStream, cancellationToken);
            var problems = new List<string>();
            var sps = encoded.Sps;
            foreach (var decoder in decoders)
            {
                var result = await decoder.DecodeAsync(stream, work / decoder.Name, cancellationToken);
                if (decoder.Name is HevcReferenceDecoder.Dec265 && sps is { ChromaFormatIdc: 0, BitDepth: > 8, ConformanceWindowFlag: true })
                {
                    // A known defect of the pinned dec265, which is not about these streams. The day it decodes one of
                    // them, it counts
                    if (result.Succeeded)
                    {
                        problems.Add("dec265 decodes a monochrome stream of more than 8 bits with a conformance window, which it did not: remove its exception from the test");
                    }

                    continue;
                }

                if (decoder.Name is HevcReferenceDecoder.FFmpeg && bitDepth is 12 && !lossless && quality <= 2)
                {
                    // A known defect of the pinned FFmpeg at the largest quantization parameters of 12-bit streams (see
                    // the remarks of the class). The day it decodes one of these streams, it counts
                    if (result.Succeeded && result.Planes.Span.SequenceEqual(encoded.Expected))
                    {
                        problems.Add("ffmpeg decodes a 12-bit stream whose quantization parameter is 50 or 51, which it did not: remove its exception from the test");
                    }

                    continue;
                }

                Compare(decoder.Name, result.Succeeded ? result.Planes : default, result.Error);
                if (decoder.Name is not HevcReferenceDecoder.Hm || !result.Succeeded)
                    continue;

                // TAppDecoder checks the hash of each picture itself, and says so in its report
                var report = result.StandardOutput;
                var verified = report.Split("[MD5:", StringSplitOptions.None).Skip(1).Count(part => part.Contains(",(OK)]", StringComparison.Ordinal));
                if (verified != pictureCount || report.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add(string.Create(CultureInfo.InvariantCulture, $"TAppDecoder verifies the hash of {verified} picture(s) out of {pictureCount}"));
                }
            }

            // The headers as a third implementation reads them
            var trace = await ffmpeg.TraceHeadersAsync(stream, "hevc", cancellationToken);
            var profile = GetProfile(chromaFormatIdc, bitDepth, kind);
            CheckTrace(trace, profile, sps, kind, wavefront, problems);
            if (GetProfileName(encoded.Profile) != profile)
            {
                problems.Add($"the encoder announces the profile '{GetProfileName(encoded.Profile)}' instead of '{profile}'");
            }

            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: {profile}, {encoded.ByteStream.Length} bytes, {pictureCount} picture(s)"));
            if (problems.Count is 0)
                return;

            // The stream and its headers are kept for the diagnosis
            var copy = artifacts / (name + ".hevc");
            File.Copy(stream, copy, overwrite: true);
            await File.WriteAllTextAsync(artifacts / (name + ".trace.txt"), trace, cancellationToken);
            Assert.Fail($"{name} ({profile}): {string.Join("; ", problems)}. Stream: '{copy}'.");

            void Compare(string decoderName, ReadOnlyMemory<byte> planes, string? error)
            {
                if (error is not null)
                {
                    problems.Add($"{decoderName} does not decode the stream: {error}");
                }
                else if (!planes.Span.SequenceEqual(encoded.Expected))
                {
                    problems.Add(string.Create(CultureInfo.InvariantCulture, $"{decoderName} outputs {planes.Length} bytes that differ from the {encoded.Expected.Length} bytes of the pictures of the encoder at the byte {planes.Span.CommonPrefixLength(encoded.Expected)}"));
                }
            }
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    /// <param name="ByteStream">The stream, in the byte stream format: the parameter sets, then the slice segment and the hash of each picture.</param>
    /// <param name="Expected">The planes of every picture that the encoder reconstructed, cropped to the conformance window: what a decoder outputs.</param>
    /// <param name="Sps">The sequence parameter set, as the library reads it.</param>
    /// <param name="Profile">The profile that the encoder announces.</param>
    private sealed record EncodedStream(byte[] ByteStream, byte[] Expected, HevcSequenceParameterSet Sps, HevcProfile Profile);

    [Theory]
    [InlineData("photo")]
    [InlineData("edges")]
    [InlineData("texture")]
    public async Task QuantizationRateDistortionIsMeasuredAgainstX265(string content)
    {
        var cancellationToken = XunitCancellationToken;
        var reference = await RequiredTools.HevcReferenceEncoderAsync(HevcReferenceEncoder.X265);
        var work = InteropSettings.GetArtifactsDirectory(nameof(QuantizationRateDistortionIsMeasuredAgainstX265)) / content;
        Directory.CreateDirectory(work);
        try
        {
            const int Size = 64;
            using var input = new MemoryStream();
            for (var plane = 0; plane < 3; plane++)
            {
                var size = plane is 0 ? Size : Size / 2;
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        input.WriteByte((byte)Sample(0, plane, x, y));
                    }
                }
            }

            var source = input.ToArray();
            var inputPath = work / "input.yuv";
            await File.WriteAllBytesAsync(inputPath, source, cancellationToken);
            foreach (var quality in new[] { 25, 50, 75 })
            {
                var settings = new HevcEncoderSettings(Size, Size, BitDepth: 8, ChromaFormatIdc: 1, ColourPrimaries: 1, TransferCharacteristics: 13, MatrixCoeffs: 6, FullRange: true, ChromaSampleLocType: 0, StillPicture: true, Lossless: false, quality, Effort: 5);
                var baseline = Encode(content, settings, 1, cancellationToken, Sample);
                var optimized = Encode(content, settings with { OptimizeQuantization = true }, 1, cancellationToken, Sample);
                var qp = HevcEncoderDecisions.Create(settings).SliceQp;
                var stream = work / "reference.hevc";
                var reconstruction = work / "reference.yuv";
                var arguments = reference.GetArguments(inputPath, stream, Size, Size, chromaFormatIdc: 1, bitDepth: 8, frameCount: 1,
                    ["--preset", "medium", "--profile", "mainstillpicture", "--qp", qp.ToString(CultureInfo.InvariantCulture), "--recon", reconstruction]);
                Assert.Null(await reference.EncodeAsync(arguments, TimeSpan.FromMinutes(1), cancellationToken));
                var referencePixels = await File.ReadAllBytesAsync(reconstruction, cancellationToken);
                Assert.HasCount(source.Length, referencePixels);
                Assert.HasCount(source.Length, baseline.Expected);
                Assert.HasCount(source.Length, optimized.Expected);
                TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{content} q{quality} (QP {qp}): baseline {baseline.ByteStream.Length} B/{Psnr(baseline.Expected):F2} dB; optimized {optimized.ByteStream.Length} B/{Psnr(optimized.Expected):F2} dB; x265 {new FileInfo(stream).Length} B/{Psnr(referencePixels):F2} dB"));
            }

            ushort Sample(int index, int plane, int x, int y) => content switch
            {
                "edges" => (ushort)((((x / 8) + (y / 8) + plane) & 1) is 0 ? 32 : 224),
                "texture" => (ushort)(32 + ((((x * 31) ^ (y * 17) ^ (plane * 97)) & 127) * 3 / 2)),
                _ => GetSample(index, plane, x, y, bitDepth: 8, subWidth: plane is 0 ? 1 : 2),
            };

            double Psnr(byte[] pixels)
            {
                long error = 0;
                for (var i = 0; i < pixels.Length; i++)
                {
                    var difference = pixels[i] - source[i];
                    error += difference * difference;
                }

                return error is 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255 * source.Length / error);
            }
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    private static EncodedStream Encode(string name, HevcEncoderSettings settings, int pictureCount, CancellationToken cancellationToken, Func<int, int, int, int, ushort>? getSample = null)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        using var encoder = new HevcEncoder(scope, settings);
        var nalUnits = new List<ReadOnlyMemory<byte>>
        {
            encoder.VideoParameterSet.ToArray(),
            encoder.SequenceParameterSet.ToArray(),
            encoder.PictureParameterSet.ToArray(),
        };
        var sps = HevcSequenceParameterSet.ParseRbsp(ReferenceHevcStream.GetRbsp(encoder.SequenceParameterSet.Span));
        var planeCount = sps.ComponentCount;
        var bytesPerSample = sps.BitDepth > 8 ? 2 : 1;
        using var expected = new MemoryStream();
        using var rbsp = new PooledPayloadWriter(scope);
        using var nalUnit = new PooledPayloadWriter(scope);
        for (var index = 0; index < pictureCount; index++)
        {
            for (var plane = 0; plane < planeCount; plane++)
            {
                var buffer = encoder.GetSourcePlane(plane);
                for (var y = 0; y < buffer.Height; y++)
                {
                    var row = buffer.GetRow(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x] = getSample is null ? GetSample(index, plane, x, y, settings.BitDepth, plane is 0 ? 1 : sps.SubWidthC) : getSample(index, plane, x, y);
                    }
                }
            }

            encoder.BeginFrame();
            while (!encoder.Encode(cancellationToken))
            {
            }

            nalUnits.AddRange(ReferenceHevcStream.SplitLengthPrefixed(encoder.Payload.ToArray()));

            // The hash of the picture of the encoder, in a suffix SEI message, and the samples that a decoder outputs
            var picture = encoder.Reconstruction;
            var md5 = new byte[planeCount * 16];
            for (var plane = 0; plane < planeCount; plane++)
            {
                var reconstructed = picture.GetPlane(plane);
                ReferenceHevcPictureHash.ComputeMd5(reconstructed.Samples, reconstructed.Width, reconstructed.Height, reconstructed.Stride, settings.BitDepth).CopyTo(md5, plane * 16);
                var subWidth = plane is 0 ? 1 : sps.SubWidthC;
                var subHeight = plane is 0 ? 1 : sps.SubHeightC;
                for (var y = 0; y < sps.OutputHeight / subHeight; y++)
                {
                    foreach (var sample in reconstructed.GetRow(y)[..(sps.OutputWidth / subWidth)])
                    {
                        expected.WriteByte((byte)sample);
                        if (bytesPerSample is 2)
                        {
                            expected.WriteByte((byte)(sample >> 8));
                        }
                    }
                }
            }

            rbsp.Reset();
            nalUnit.Reset();
            HevcHeaderWriter.WriteDecodedPictureHash(rbsp, new HevcDecodedPictureHash(HevcDecodedPictureHash.Md5, planeCount, md5, new uint[planeCount]));
            HevcNalWriter.WriteNalUnit(nalUnit, HevcConstants.NalSuffixSei, 0, rbsp.WrittenSpan);
            nalUnits.Add(nalUnit.WrittenSpan.ToArray());
        }

        return new EncodedStream(ReferenceHevcStream.ToAnnexB(nalUnits), expected.ToArray(), sps, encoder.Parameters.Profile);
    }

    // A scene of smooth shapes, sharp edges and a little texture, which moves by a luma sample and a quarter to the right
    // and by half a luma sample down from a picture to the next, with a square that appears in the second picture
    private static ushort GetSample(int index, int plane, int x, int y, int bitDepth, int subWidth)
    {
        var max = (1 << bitDepth) - 1;
        var sceneX = x + (1.25 * index / subWidth);
        var sceneY = y + (0.5 * index);
        var value = (max / 2.0) + (max / 4.0 * Math.Sin((sceneX + (plane * 5)) / 7.0)) + (max / 6.0 * Math.Cos(sceneY / 5.0)) + ((((int)sceneX / 19) + ((int)sceneY / 13)) % 2 * max / 5.0) + ((((x + index) * 31) ^ (y * 17)) % 7 * max / 96.0);
        if (index >= 1 && x >= 10 / subWidth && x < 22 / subWidth && y >= 6 && y < 14)
        {
            value = max / 5.0 * (plane + 1);
        }

        return (ushort)Math.Clamp((int)value, 0, max);
    }

    /// <summary>
    /// The profile, the tier, the level and the constraint flags that <c>trace_headers</c> reads in the video parameter set
    /// and in the sequence parameter set, against Annex A; the NAL unit types; and the sample format of the pictures.
    /// </summary>
    private static void CheckTrace(string trace, string profile, HevcSequenceParameterSet sps, string kind, bool wavefront, List<string> problems)
    {
        var (profileIdc, compatibility, flags) = GetProfileIndication(profile);
        Expect("general_profile_space", 0);
        Expect("general_tier_flag", 0);
        Expect("general_profile_idc", profileIdc);
        for (var j = 0; j < 32; j++)
        {
            Expect(string.Create(CultureInfo.InvariantCulture, $"general_profile_compatibility_flag[{j}]"), compatibility.Contains(j) ? 1 : 0);
        }

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

        Expect("general_level_idc", GetLevelIdc(sps.PicWidthInLumaSamples, sps.PicHeightInLumaSamples));

        // The NAL units: the three parameter sets, the slice segment of a key frame (IDR_N_LP) and the suffix SEI NAL unit
        // of its hash, then for each picture that follows a key frame, a slice segment (TRAIL_R) and its hash
        long[] nalUnitTypes = kind is "sequence" or "interval"
            ? [HevcConstants.NalTrailR, HevcConstants.NalIdrNLp, HevcConstants.NalVps, HevcConstants.NalSps, HevcConstants.NalPps, HevcConstants.NalSuffixSei]
            : [HevcConstants.NalIdrNLp, HevcConstants.NalVps, HevcConstants.NalSps, HevcConstants.NalPps, HevcConstants.NalSuffixSei];
        var tracedTypes = GetTraceValues(trace, "nal_unit_type").Distinct().Order().ToArray();
        if (!tracedTypes.SequenceEqual(nalUnitTypes))
        {
            problems.Add($"trace_headers reads the NAL unit types [{string.Join(", ", tracedTypes)}] instead of [{string.Join(", ", nalUnitTypes)}]");
        }

        Expect("chroma_format_idc", sps.ChromaFormatIdc);
        Expect("pic_width_in_luma_samples", sps.PicWidthInLumaSamples);
        Expect("pic_height_in_luma_samples", sps.PicHeightInLumaSamples);
        Expect("bit_depth_luma_minus8", sps.BitDepth - 8);
        Expect("bit_depth_chroma_minus8", sps.BitDepth - 8);
        Expect("sps_max_dec_pic_buffering_minus1[0]", kind is "sequence" or "interval" ? 1 : 0);
        Expect("video_full_range_flag", 1);
        Expect("entropy_coding_sync_enabled_flag", wavefront ? 1 : 0);

        // None of the tools that a decoder in use may not have
        Expect("pcm_enabled_flag", 0);
        Expect("scaling_list_enabled_flag", 0);
        if (GetTraceValues(trace, "sps_range_extension_flag").Contains(1) || GetTraceValues(trace, "pps_range_extension_flag").Contains(1))
        {
            problems.Add("the stream has a range extension");
        }

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
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 && parts[^2] is "=" && parts[^4] == element && long.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>
    /// The narrowest profile of Annex A that has a format: for one picture, for a sequence of key frames ("intra"), or for
    /// a sequence whose pictures are predicted from each other ("sequence", and "interval" for key frames every two pictures).
    /// </summary>
    private static string GetProfile(int chromaFormatIdc, int bitDepth, string kind)
    {
        var intra = kind is "still" or "intra";
        return (chromaFormatIdc, bitDepth) switch
        {
            (0, 8) => "Monochrome",
            (0, 10) => "Monochrome 10",
            (0, _) => "Monochrome 12",
            (1, 8) => kind is "still" ? "Main Still Picture" : "Main",
            (1, 10) => kind is "still" ? "Main 10 Still Picture" : "Main 10",
            (1, _) => intra ? "Main 12 Intra" : "Main 12",
            (2, 8 or 10) => intra ? "Main 4:2:2 10 Intra" : "Main 4:2:2 10",
            (2, _) => intra ? "Main 4:2:2 12 Intra" : "Main 4:2:2 12",
            (_, 8) => kind is "still" ? "Main 4:4:4 Still Picture" : intra ? "Main 4:4:4 Intra" : "Main 4:4:4",
            (_, 10) => intra ? "Main 4:4:4 10 Intra" : "Main 4:4:4 10",
            _ => intra ? "Main 4:4:4 12 Intra" : "Main 4:4:4 12",
        };
    }

    /// <summary>
    /// Returns <c>general_profile_idc</c>, the indices of the compatibility flags that are set, and the nine flags of
    /// Table A.2 from <c>general_max_12bit_constraint_flag</c> to <c>general_lower_bit_rate_constraint_flag</c>.
    /// </summary>
    private static (int ProfileIdc, int[] CompatibilityFlags, string ConstraintFlags) GetProfileIndication(string name) => name switch
    {
        "Main" => (1, [1, 2], "000000000"),
        "Main 10" => (2, [2], "000000000"),
        "Main 10 Still Picture" => (2, [2], "000000010"),
        "Main Still Picture" => (3, [1, 2, 3], "000000000"),
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

    /// <summary>Returns <c>general_level_idc</c> of the lowest level whose <c>MaxLumaPs</c> holds a picture (Table A.8, levels 1 to 6).</summary>
    private static int GetLevelIdc(int width, int height)
    {
        (int LevelIdc, int MaxLumaPs)[] levels = [(30, 36864), (60, 122880), (63, 245760), (90, 552960), (93, 983040), (120, 2228224), (150, 8912896), (180, 35651584)];
        foreach (var (levelIdc, maxLumaPs) in levels)
        {
            if ((long)width * height <= maxLumaPs && (long)width * width <= 8L * maxLumaPs && (long)height * height <= 8L * maxLumaPs)
                return levelIdc;
        }

        throw new ArgumentOutOfRangeException(nameof(width), "The picture needs a level above 6.");
    }
}
