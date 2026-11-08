using System.Text.Json;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Jpeg;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Baseline and progressive JPEG encoder output decoded by the pinned FFmpeg. FFmpeg decodes every output to its native planes
/// (no swscale conversion, so nothing depends on FFmpeg's chroma interpolation or color conversion, which vary between
/// versions); ffprobe reports the dimensions, the sampling (pixel format <c>yuvj444p</c>/<c>yuvj422p</c>/<c>yuvj420p</c>/<c>gray</c>),
/// the full-range flag, the ICC profile and the EXIF orientation.
/// </summary>
/// <remarks>
/// <para>
/// Checks per output: (1) every FFmpeg plane sample is within one unit of the harness reference decoding of the same file
/// (<see cref="ReferenceJpeg.DecodePlanes"/>, a direct double-precision IDCT): FFmpeg's IDCT is IEEE 1180 compliant, whose
/// peak error against the exact transform is one unit, so a larger difference means the two decoders read different
/// coefficients from our bitstream; (2) the reconstruction from FFmpeg's planes (chroma replicated, JFIF equations in the
/// test) measured against the source with documented per-setting minimum PSNR and a bound on the per-channel bias, on a
/// photo-like image and on sensitive patterns (solid colors, one-pixel chroma stripes) so that channel swaps, range errors
/// and subsampling defects cannot hide behind global similarity; (3) the solid colors also go through FFmpeg's own RGB
/// conversion, checked on the patch interiors. Output sizes are reported in the test output.
/// </para>
/// </remarks>
public sealed class JpegEncoderInteropTests
{
    /// <summary>
    /// Minimum PSNR (dB) of the 96x64 photo-like source reconstructed from FFmpeg's planes, about 1.5 dB below the values
    /// measured with FFmpeg 9.0 when the encoder was written (the reference reconstruction of the unit tests measures within 0.1 dB of them).
    /// </summary>
    private static readonly Dictionary<(string Sampling, int Quality), double> MinimumPsnr = new()
    {
        [("444", 10)] = 24.5,
        [("444", 50)] = 29.5,
        [("444", 90)] = 35.5,
        [("444", 100)] = 48,
        [("422", 10)] = 24.5,
        [("422", 50)] = 29,
        [("422", 90)] = 34,
        [("422", 100)] = 38,
        [("420", 10)] = 23.5,
        [("420", 50)] = 28,
        [("420", 90)] = 32,
        [("420", 100)] = 34,
        [("gray", 10)] = 27.5,
        [("gray", 50)] = 31.5,
        [("gray", 90)] = 39.5,
        [("gray", 100)] = 55,
    };

    public static TheoryData<string, int, int, int, JpegEncodingProcess> Cases()
    {
        var data = new TheoryData<string, int, int, int, JpegEncodingProcess>();
        foreach (var process in new[] { JpegEncodingProcess.Baseline, JpegEncodingProcess.Progressive })
        {
            foreach (var sampling in new[] { "444", "422", "420", "gray" })
            {
                foreach (var quality in new[] { 10, 50, 90, 100 })
                {
                    data.Add(sampling, quality, 96, 64, process);
                    data.Add(sampling, quality, 37, 23, process);
                }
            }

            data.Add("420", 75, 1, 1, process);
            data.Add("422", 75, 17, 2, process);
            data.Add("444", 75, 2, 17, process);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EncodedImagesDecodeWithFFmpeg(string sampling, int quality, int width, int height, JpegEncodingProcess process)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var source = JpegTestPatterns.PhotoLike(width, height, seed: 7);
        var gray = sampling == "gray";
        if (gray)
        {
            source = JpegTestPatterns.ToGray(source);
        }

        // Both processes code the same coefficients: the same checks and thresholds apply
        var encoder = new JpegEncoder { Quality = quality, ChromaSubsampling = GetSubsampling(sampling), Process = process };
        var name = string.Create(CultureInfo.InvariantCulture, $"photo-{sampling}-q{quality}-{width}x{height}-{process}");
        var path = await EncodeToFileAsync(source, gray ? PixelFormat.Gray8 : PixelFormat.Rgb24, encoder, name);
        var error = await AssertDecodesWithFFmpegAsync(ffmpeg, path, source, sampling);
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: {new FileInfo(path).Length} bytes, {error.Describe()} (FFmpeg {ffmpeg.Version})"));
        if (width == 96 && MinimumPsnr.TryGetValue((sampling, quality), out var minimum))
        {
            Assert.True(error.Psnr >= minimum && (quality < 50 || error.MaxChannelBias < 0.75), string.Create(CultureInfo.InvariantCulture, $"{name}: {error.Describe()} (minimum {minimum} dB)."));
        }
    }

    [Theory]
    [InlineData("444", 50)]
    [InlineData("444", 90)]
    [InlineData("422", 90)]
    [InlineData("420", 50)]
    [InlineData("420", 90)]
    [InlineData("gray", 90)]
    public async Task EncodedImagesDecodeWithTheIsoReferenceDecoder(string sampling, int quality)
    {
        // The ISO JPEG reference codec shares no code with libjpeg-turbo, whose tools checked the encoder when it was written
        var isoJpeg = await RequiredTools.IsoJpegAsync();
        var gray = sampling == "gray";
        var source = JpegTestPatterns.PhotoLike(96, 64, seed: 7);
        if (gray)
        {
            source = JpegTestPatterns.ToGray(source);
        }

        int[]? baselinePixels = null;
        foreach (var process in new[] { JpegEncodingProcess.Baseline, JpegEncodingProcess.Progressive })
        {
            var encoder = new JpegEncoder { Quality = quality, ChromaSubsampling = GetSubsampling(sampling), Process = process };
            var name = string.Create(CultureInfo.InvariantCulture, $"iso-photo-{sampling}-q{quality}-{process}");
            var path = await EncodeToFileAsync(source, gray ? PixelFormat.Gray8 : PixelFormat.Rgb24, encoder, name);
            var reference = JpegDecoderDifferentialTests.GetSamples(await isoJpeg.DecodeAsync(path, XunitCancellationToken), 255);

            // Both processes code the same coefficients: the reference decoder returns the same pixels
            baselinePixels ??= reference;
            Assert.Equal(baselinePixels, reference);

            using var image = Image.Load(path);
            var (max, mean, excluded) = JpegDecoderDifferentialTests.Compare(JpegDecoderDifferentialTests.GetSamples(image, 255), reference, 255);
            var description = string.Create(CultureInfo.InvariantCulture, $"{name}: library vs reference decoder max {max}, mean {mean:F4}, {excluded} pixels at an end of the range left out");
            TestContext.Current.TestOutputHelper?.WriteLine(description);
            // The measured maximum plus one unit: chroma upsampling filters differ most
            var allowed = sampling switch
            {
                "gray" => 2,
                "444" => 3,
                "420" => 4,
                _ => 6,
            };
            Assert.True(max <= allowed && mean <= 0.8 && excluded <= 96 * 64 / 20, description);
        }
    }

    [Theory]
    [InlineData("444", JpegEncodingProcess.Baseline)]
    [InlineData("422", JpegEncodingProcess.Baseline)]
    [InlineData("420", JpegEncodingProcess.Baseline)]
    [InlineData("gray", JpegEncodingProcess.Baseline)]
    [InlineData("420", JpegEncodingProcess.Progressive)]
    [InlineData("gray", JpegEncodingProcess.Progressive)]
    public async Task OptimizedTablesAndRestartMarkersDecodeToThePixelsOfTheDefaultOutput(string sampling, JpegEncodingProcess process)
    {
        // The options change the entropy coding only: FFmpeg and the ISO reference decoder (each its own Huffman decoder and
        // restart handling) return exactly the pixels of the default output. Intervals of one MCU, that do not divide the
        // number of MCUs, of one MCU row (37 pixels: 5 MCUs of 8 pixels, 3 of 16) and longer than the scan
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var isoJpeg = await RequiredTools.IsoJpegAsync();
        var gray = sampling == "gray";
        var source = JpegTestPatterns.PhotoLike(37, 23, seed: 7);
        if (gray)
        {
            source = JpegTestPatterns.ToGray(source);
        }

        var format = gray ? PixelFormat.Gray8 : PixelFormat.Rgb24;
        var mcusPerRow = sampling is "444" or "gray" ? 5 : 3;
        var name = string.Create(CultureInfo.InvariantCulture, $"options-{sampling}-{process}");
        var defaultPath = await EncodeToFileAsync(source, format, new JpegEncoder { Quality = 85, ChromaSubsampling = GetSubsampling(sampling), Process = process }, name + "-default");
        var expectedError = (await AssertDecodesWithFFmpegAsync(ffmpeg, defaultPath, source, sampling)).Describe();
        var expected = JpegDecoderDifferentialTests.GetSamples(await isoJpeg.DecodeAsync(defaultPath, XunitCancellationToken), 255);
        foreach (var (optimize, restartInterval) in new[] { (true, 0), (false, 1), (true, 2), (false, mcusPerRow), (true, 1000) })
        {
            var encoder = new JpegEncoder { Quality = 85, ChromaSubsampling = GetSubsampling(sampling), Process = process, OptimizeHuffmanTables = optimize, RestartInterval = restartInterval };
            var variant = string.Create(CultureInfo.InvariantCulture, $"{name}-optimized{optimize}-restart{restartInterval}");
            var path = await EncodeToFileAsync(source, format, encoder, variant);
            Assert.Equal(expectedError, (await AssertDecodesWithFFmpegAsync(ffmpeg, path, source, sampling)).Describe());
            Assert.Equal(expected, JpegDecoderDifferentialTests.GetSamples(await isoJpeg.DecodeAsync(path, XunitCancellationToken), 255));
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{variant}: {new FileInfo(path).Length} bytes, {new FileInfo(defaultPath).Length} by default"));
        }
    }

    [Theory]
    [InlineData("444")]
    [InlineData("422")]
    [InlineData("420")]
    public async Task SolidColorsKeepTheirChannelsAndRangeInFFmpeg(string sampling)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var source = JpegTestPatterns.ColorPatches();
        var path = await EncodeToFileAsync(source, PixelFormat.Rgb24, new JpegEncoder { Quality = 95, ChromaSubsampling = GetSubsampling(sampling) }, "patches-" + sampling);
        await AssertDecodesWithFFmpegAsync(ffmpeg, path, source, sampling);

        // FFmpeg's own YCbCr to RGB conversion: patch interiors (away from interpolated chroma edges) keep their color
        var rgb = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, "rgb24", source.Width, source.Height, XunitCancellationToken));
        var decoded = RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgb8, rgb);
        for (var patch = 0; patch < JpegTestPatterns.PatchColors.Count; patch++)
        {
            var (r, g, b) = JpegTestPatterns.PatchColors[patch];
            int[] expected = [r, g, b];
            for (var c = 0; c < 3; c++)
            {
                double sum = 0;
                for (var y = 4; y < 12; y++)
                {
                    for (var x = 4; x < 12; x++)
                    {
                        sum += decoded.GetSample((patch % 4 * 16) + x, (patch / 4 * 16) + y, c);
                    }
                }

                var mean = sum / 64;
                Assert.True(Math.Abs(mean - expected[c]) <= 3, string.Create(CultureInfo.InvariantCulture, $"{sampling} patch {patch} ({r}, {g}, {b}) channel {c}: FFmpeg mean {mean:F2}."));
            }
        }
    }

    [Theory]
    [InlineData("444", true, true)]
    [InlineData("444", false, true)]
    [InlineData("422", true, true)]
    [InlineData("422", false, false)]
    [InlineData("420", true, false)]
    [InlineData("420", false, false)]
    public async Task ChromaStripesFollowTheSubsamplingInFFmpeg(string sampling, bool horizontalStripes, bool preserved)
    {
        // Rows of one color survive 4:2:2 (horizontal halving) but not 4:2:0; columns of one color only survive 4:4:4
        var ffmpeg = await RequiredTools.FFmpegAsync();
        (byte, byte, byte) red = (230, 20, 30);
        (byte, byte, byte) blue = (20, 40, 230);
        var source = JpegTestPatterns.TwoColors(32, 24, red, blue, horizontalStripes ? (_, y) => y % 2 == 1 : (x, _) => x % 2 == 1);
        var path = await EncodeToFileAsync(source, PixelFormat.Rgb24, new JpegEncoder { Quality = 100, ChromaSubsampling = GetSubsampling(sampling) }, $"stripes-{sampling}-{(horizontalStripes ? "rows" : "columns")}");
        var error = await AssertDecodesWithFFmpegAsync(ffmpeg, path, source, sampling);
        Assert.True(preserved ? error.Psnr > 38 : error.Psnr < 20, $"{sampling}, preserved {preserved}: {error.Describe()}");
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Rgba64)]
    public async Task FlattenedAlphaAndReducedPrecisionDecodeWithFFmpeg(PixelFormat format)
    {
        // Alpha ramps and hidden colors flattened onto the background at source precision, then 16 bits reduced to 8
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var max = format == PixelFormat.Rgba64 ? 65535 : 255;
        var source = JpegTestPatterns.ToFormat(JpegTestPatterns.PhotoLike(64, 40, seed: 11), format, (x, y, c) => ((x * 37) + (y * 11) + (c * 3)) % 301 - 150);
        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var alpha = y < 20 ? max * x / 63 : (x / 8 % 2 == 0 ? 0 : max);
                source = source.WithSample(x, y, 3, alpha);
            }
        }

        var background = new Rgba64(0x2000, 0xF000, 0x8080);
        var expected = JpegTestPatterns.ToEncodedSamples(source, (background.R, background.G, background.B));
        var encoder = new JpegEncoder { Quality = 95, ChromaSubsampling = JpegChromaSubsampling.Ratio444, BackgroundColor = background, AllowBitDepthReduction = true };
        var path = await EncodeToFileAsync(source, format, encoder, "flattened-" + format);
        var error = await AssertDecodesWithFFmpegAsync(ffmpeg, path, expected, "444");
        Assert.True(error.Psnr > 37 && error.MaxChannelBias < 0.5, error.Describe());
    }

    [Fact]
    public async Task MetadataIsReadByFFmpegAndTheIndependentWalker()
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = Image.ImportPixelBytes<Rgb24>(JpegTestPatterns.PhotoLike(24, 16, seed: 3).Span, 24, 16);
        var icc = CreateIccProfile(100_000); // two APP2 chunks
        image.Metadata.IccProfile = new IccProfile(new MetadataBlob(icc));
        image.Metadata.Orientation = ExifOrientation.RightTop;
        image.Metadata.Resolution = new ImageResolution(300, 300);
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>"u8.ToArray()));
        image.Metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, "a comment"));
        var path = InteropSettings.GetArtifactsDirectory(nameof(JpegEncoderInteropTests)) / "metadata.jpg";
        await image.SaveAsync(path, new JpegEncoder(), XunitCancellationToken);

        using var probe = await ffmpeg.ProbeAsync(path, XunitCancellationToken);
        var stream = probe.RootElement.GetProperty("streams")[0];
        Assert.Equal("mjpeg", stream.GetProperty("codec_name").GetString());
        Assert.Equal(24, stream.GetProperty("width").GetInt32());
        Assert.Equal(16, stream.GetProperty("height").GetInt32());
        var frame = probe.RootElement.GetProperty("frames")[0];
        Assert.Equal("1:1", frame.GetProperty("sample_aspect_ratio").GetString()); // JFIF density 300x300
        var sideData = frame.TryGetProperty("side_data_list", out var list) ? list.EnumerateArray().ToList() : [];
        var iccData = Assert.Single(sideData, item => item.GetProperty("side_data_type").GetString() == "ICC profile");
        if (iccData.TryGetProperty("size", out var size))
        {
            Assert.Equal(icc.Length, size.GetInt32()); // reassembled from both APP2 chunks
        }

        // EXIF orientation 6: exported as a display matrix (|rotation| = 90) or as an Orientation frame tag, depending on the
        // FFmpeg version; at least one of them must be present
        var rotation = sideData.Where(item => item.TryGetProperty("rotation", out _)).Select(item => item.GetProperty("rotation").GetDouble()).ToList();
        var tag = frame.TryGetProperty("tags", out var tags) && tags.TryGetProperty("Orientation", out var value) ? value.GetString() : null;
        Assert.True(rotation.Any(angle => Math.Abs(Math.Abs(angle) - 90) < 0.01) || tag == "6", $"No EXIF orientation 6 reported by FFmpeg {ffmpeg.Version}: {frame}");

        // Pixels are unaffected by the metadata segments
        await AssertDecodesWithFFmpegAsync(ffmpeg, path, JpegTestPatterns.PhotoLike(24, 16, seed: 3), "420");

        // Every field, independently of FFmpeg's metadata export
        var fields = EncodedFieldInspector.Inspect("jpeg", await File.ReadAllBytesAsync(path, XunitCancellationToken));
        Assert.Equal(icc, fields.Icc!.Value.ToArray());
        Assert.NotNull(fields.Exif);
        Assert.NotNull(fields.Xmp);
        Assert.Equal(300, fields.Resolution!.X);
        Assert.Equal(["Comment: a comment"], fields.Text.Select(entry => entry.ToString()));
    }

    [Theory]
    [InlineData(JpegEncodingProcess.Baseline)]
    [InlineData(JpegEncodingProcess.Progressive)]
    public async Task NonSeekableAsynchronousWriterOutputDecodesWithFFmpeg(JpegEncodingProcess process)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var source = JpegTestPatterns.PhotoLike(640, 360, seed: 5);
        using var image = Image.ImportPixelBytes<Rgb24>(source.Span, 640, 360);
        var path = InteropSettings.GetArtifactsDirectory(nameof(JpegEncoderInteropTests)) / $"large-writer-{process}.jpg";
        using (var stream = new TestOutputStream { ForbidSynchronousWrites = true })
        {
            await using (var writer = Image.CreateWriter<Rgb24>(stream, new ImageWriterOptions(640, 360) { Encoder = new JpegEncoder { Quality = 100, ChromaSubsampling = JpegChromaSubsampling.Ratio444, Process = process } }))
            {
                await writer.WriteFrameAsync(image.Frames[0], XunitCancellationToken);
                await writer.CompleteAsync(XunitCancellationToken);
            }

            Assert.InRange(stream.AsynchronousWriteCount, 4, int.MaxValue);
            await File.WriteAllBytesAsync(path, stream.ToArray(), XunitCancellationToken);
        }

        var error = await AssertDecodesWithFFmpegAsync(ffmpeg, path, source, "444");
        Assert.True(error.Psnr > 45, error.Describe());
    }

    /// <summary>
    /// The largest mean difference of the reference decoding of the 37x23 deep source at quality 85 with the source, in
    /// units of the precision: the measured values plus 25%. 12-bit output is several times more accurate than 8-bit
    /// output relative to the sample range (the quantization tables are the same), up to the chroma subsampling.
    /// </summary>
    private static readonly Dictionary<(string Sampling, int Precision), double> MaximumSourceError = new()
    {
        [("gray", 8)] = 3.7,
        [("gray", 12)] = 5.6,
        [("444", 8)] = 5.6,
        [("444", 12)] = 11.8,
        [("422", 8)] = 5.9,
        [("422", 12)] = 30.6,
        [("420", 8)] = 7.2,
        [("420", 12)] = 66,
    };

    public static TheoryData<string, JpegEncodingProcess, int> ExtendedDctCases()
    {
        var data = new TheoryData<string, JpegEncodingProcess, int>();
        foreach (var precision in new[] { 8, 12 })
        {
            foreach (var process in new[] { JpegEncodingProcess.Baseline, JpegEncodingProcess.Progressive })
            {
                foreach (var sampling in new[] { "444", "422", "420", "gray" })
                {
                    data.Add(sampling, process, precision);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ExtendedDctCases))]
    public async Task ArithmeticAndTwelveBitOutputDecodesWithTheIsoReferenceDecoder(string sampling, JpegEncodingProcess process, int precision)
    {
        // Arithmetic coding codes the coefficients of Huffman coding, and the progressive process those of the sequential
        // one: the ISO reference decoder (its own arithmetic decoder and restart handling) and the library return exactly
        // the pixels of the sequential Huffman output for every variant, with restart intervals of one MCU, of one MCU row
        // and longer than the scan. The library then matches the reference decoder within the tolerances of its decoder tests
        var isoJpeg = await RequiredTools.IsoJpegAsync();
        var gray = sampling == "gray";
        var maxValue = (1 << precision) - 1;
        using var source = CreateDeepSource(37, 23, gray);
        var name = string.Create(CultureInfo.InvariantCulture, $"extended-{sampling}-{process}-{precision}");
        var sequentialPath = await EncodeImageToFileAsync(source, new JpegEncoder { Quality = 85, ChromaSubsampling = GetSubsampling(sampling), BitDepth = precision, AllowBitDepthReduction = true }, name + "-sequential");
        var expected = JpegDecoderDifferentialTests.GetSamples(await isoJpeg.DecodeAsync(sequentialPath, XunitCancellationToken), maxValue);
        var (_, sourceError, _) = JpegDecoderDifferentialTests.Compare(expected, JpegDecoderDifferentialTests.GetSamples(source, maxValue), maxValue);
        Assert.True(sourceError <= MaximumSourceError[(sampling, precision)], string.Create(CultureInfo.InvariantCulture, $"{name}: mean error {sourceError:F3} against the source."));
        using (var image = Image.Load(sequentialPath))
        {
            var (max, mean, excluded) = JpegDecoderDifferentialTests.Compare(JpegDecoderDifferentialTests.GetSamples(image, maxValue), expected, maxValue);
            var (allowedMax, allowedMean) = JpegDecoderDifferentialTests.GetTolerance(sampling, precision);
            Assert.True(max <= allowedMax && mean <= allowedMean && excluded <= 37 * 23 / 20, string.Create(CultureInfo.InvariantCulture, $"{name}: library vs reference decoder max {max}, mean {mean:F4}, {excluded} pixels left out."));
        }

        var mcusPerRow = sampling is "444" or "gray" ? 5 : 3;
        foreach (var (coding, restartInterval) in new[] { (JpegEntropyCoding.Huffman, 0), (JpegEntropyCoding.Arithmetic, 0), (JpegEntropyCoding.Arithmetic, 1), (JpegEntropyCoding.Arithmetic, mcusPerRow), (JpegEntropyCoding.Arithmetic, 1000) })
        {
            var encoder = new JpegEncoder { Quality = 85, ChromaSubsampling = GetSubsampling(sampling), Process = process, EntropyCoding = coding, RestartInterval = restartInterval, BitDepth = precision, AllowBitDepthReduction = true };
            var variant = string.Create(CultureInfo.InvariantCulture, $"{name}-{coding}-restart{restartInterval}");
            var path = await EncodeImageToFileAsync(source, encoder, variant);
            Assert.Equal(expected, JpegDecoderDifferentialTests.GetSamples(await isoJpeg.DecodeAsync(path, XunitCancellationToken), maxValue));
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{variant}: {new FileInfo(path).Length} bytes, {new FileInfo(sequentialPath).Length} for the sequential Huffman output"));
        }
    }

    public static TheoryData<PixelFormat, int, int, JpegEntropyCoding> LosslessCases()
    {
        var data = new TheoryData<PixelFormat, int, int, JpegEntropyCoding>();
        foreach (var coding in new[] { JpegEntropyCoding.Huffman, JpegEntropyCoding.Arithmetic })
        {
            for (var predictor = 1; predictor <= 7; predictor++)
            {
                data.Add(PixelFormat.Rgb24, 8, predictor, coding);
                data.Add(PixelFormat.Gray16, 16, predictor, coding);
            }

            foreach (var (format, precision) in new[] { (PixelFormat.Gray8, 8), (PixelFormat.Rgb48, 16), (PixelFormat.Gray16, 12), (PixelFormat.Rgb48, 10), (PixelFormat.Gray16, 2) })
            {
                data.Add(format, precision, 4, coding);
                data.Add(format, precision, 7, coding);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LosslessCases))]
    public async Task LosslessOutputDecodesExactlyWithTheIsoReferenceDecoder(PixelFormat format, int precision, int predictor, JpegEntropyCoding coding)
    {
        // The ISO reference decoder returns the samples reduced to the precision exactly, with every predictor, without
        // and with restart intervals (of one line and of three lines)
        var isoJpeg = await RequiredTools.IsoJpegAsync();
        var gray = format is PixelFormat.Gray8 or PixelFormat.Gray16;
        using var deep = CreateDeepSource(37, 23, gray);
        using var source = CloneAs(deep, format);
        var expected = GetReducedSamples(source, precision);
        foreach (var restartInterval in new[] { 0, 37, 3 * 37 })
        {
            var encoder = new JpegEncoder { Process = JpegEncodingProcess.Lossless, BitDepth = precision, LosslessPredictor = predictor, EntropyCoding = coding, RestartInterval = restartInterval };
            var name = string.Create(CultureInfo.InvariantCulture, $"lossless-{format}-{precision}-p{predictor}-{coding}-restart{restartInterval}");
            var path = await EncodeImageToFileAsync(source, encoder, name);
            var output = path + ".pnm";
            await isoJpeg.DecodeToFileAsync(path, output, XunitCancellationToken);
            var (maxValue, samples) = ReadPnm(await File.ReadAllBytesAsync(output, XunitCancellationToken));
            Assert.Equal((1 << precision) - 1, maxValue);
            Assert.Equal(expected, samples);
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: {new FileInfo(path).Length} bytes"));
        }
    }

    /// <summary>
    /// Creates a 16-bit image whose samples use all their bits: the photo-like pattern scaled to 16 bits, with noise in the
    /// low bits so that 12-bit and 16-bit output differ from 8-bit output.
    /// </summary>
    private static Image CreateDeepSource(int width, int height, bool gray)
    {
        var pattern = JpegTestPatterns.PhotoLike(width, height, seed: 7);
        if (gray)
        {
            pattern = JpegTestPatterns.ToGray(pattern);
        }

        var channels = gray ? 1 : 3;
        var bytes = pattern.Span;
        var samples = new ushort[width * height * channels];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (ushort)Math.Min(65535, (bytes[i] * 257) + (int)((uint)(i * 2654435761u) >> 24));
        }

        var data = System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples.AsSpan());
        return gray ? Image.ImportPixelBytes<Gray16>(data, width, height) : Image.ImportPixelBytes<Rgb48>(data, width, height);
    }

    private static Image CloneAs(Image image, PixelFormat format) => format switch
    {
        PixelFormat.Gray8 => image.CloneAs<Gray8>(),
        PixelFormat.Gray16 => image.CloneAs<Gray16>(),
        PixelFormat.Rgb24 => image.CloneAs<Rgb24>(),
        _ => image.CloneAs<Rgb48>(),
    };

    /// <summary>Gets the samples of an image reduced to a precision with nearest rounding, as the encoder reduces them.</summary>
    private static int[] GetReducedSamples(Image image, int precision)
    {
        ushort[] samples;
        if (image.PixelFormat is PixelFormat.Gray8 or PixelFormat.Gray16)
        {
            using var gray = image.CloneAs<Gray16>();
            var pixels = new Gray16[image.Width * image.Height];
            gray.Frames[0].CopyPixelDataTo(pixels);
            samples = [.. pixels.Select(pixel => pixel.Value)];
        }
        else
        {
            using var color = image.CloneAs<Rgb48>();
            var pixels = new Rgb48[image.Width * image.Height];
            color.Frames[0].CopyPixelDataTo(pixels);
            samples = [.. pixels.SelectMany(pixel => new[] { pixel.R, pixel.G, pixel.B })];
        }

        var maximum = (1L << precision) - 1;
        return [.. samples.Select(sample => (int)(((sample * maximum) + 32767) / 65535))];
    }

    /// <summary>Reads the samples of a binary PGM or PPM file as they are stored (big-endian above 8 bits).</summary>
    private static (int MaxValue, int[] Samples) ReadPnm(byte[] data)
    {
        var offset = 0;
        var tokens = new int[4];
        for (var token = 0; token < 4; token++)
        {
            while (data[offset] is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t')
            {
                offset++;
            }

            var start = offset;
            while (data[offset] is not ((byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t'))
            {
                offset++;
            }

            tokens[token] = token == 0 ? data[start + 1] - '0' : int.Parse(data.AsSpan(start, offset - start), CultureInfo.InvariantCulture);
        }

        offset++;
        var channels = tokens[0] == 6 ? 3 : 1;
        var maxValue = tokens[3];
        var samples = new int[tokens[1] * tokens[2] * channels];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = maxValue > 255 ? (data[offset + (2 * i)] << 8) | data[offset + (2 * i) + 1] : data[offset + i];
        }

        return (maxValue, samples);
    }

    private static async Task<FullPath> EncodeImageToFileAsync(Image image, JpegEncoder encoder, string name)
    {
        var path = InteropSettings.GetArtifactsDirectory(nameof(JpegEncoderInteropTests)) / (name + ".jpg");
        await image.SaveAsync(path, encoder, XunitCancellationToken);
        return path;
    }

    private static JpegChromaSubsampling GetSubsampling(string sampling) => sampling switch
    {
        "444" => JpegChromaSubsampling.Ratio444,
        "422" => JpegChromaSubsampling.Ratio422,
        _ => JpegChromaSubsampling.Ratio420,
    };

    private static async Task<FullPath> EncodeToFileAsync(RawPixelBuffer pixels, PixelFormat format, JpegEncoder encoder, string name)
    {
        var path = InteropSettings.GetArtifactsDirectory(nameof(JpegEncoderInteropTests)) / (name + ".jpg");
        using var image = EncoderSources.CreateImage(pixels, format);
        await image.SaveAsync(path, encoder, XunitCancellationToken);
        return path;
    }

    /// <summary>
    /// Probes the stream (dimensions, pixel format, full range), decodes FFmpeg's native planes, requires every sample within
    /// one unit of the reference decoding, and measures the reconstruction from FFmpeg's planes against <paramref name="expected"/>.
    /// </summary>
    private static async Task<ReconstructionError> AssertDecodesWithFFmpegAsync(FFmpegTool ffmpeg, FullPath path, RawPixelBuffer expected, string sampling)
    {
        var width = expected.Width;
        var height = expected.Height;
        var gray = expected.Layout == RawPixelLayout.Gray8;
        var pixelFormat = gray ? "gray" : "yuvj" + sampling + "p";
        using (var probe = await ffmpeg.ProbeAsync(path, XunitCancellationToken))
        {
            var stream = probe.RootElement.GetProperty("streams")[0];
            Assert.Equal("mjpeg", stream.GetProperty("codec_name").GetString());
            Assert.Equal(width, stream.GetProperty("width").GetInt32());
            Assert.Equal(height, stream.GetProperty("height").GetInt32());
            Assert.Equal(pixelFormat, stream.GetProperty("pix_fmt").GetString());
            if (!gray && stream.TryGetProperty("color_range", out var range))
            {
                Assert.Equal("pc", range.GetString()); // JFIF full range
            }
        }

        var (h, v) = gray ? (1, 1) : sampling switch { "444" => (1, 1), "422" => (2, 1), _ => (2, 2) };
        var chromaWidth = (width + h - 1) / h;
        var chromaHeight = (height + v - 1) / v;
        var decode = await ffmpeg.RunAsync(["-noautorotate", "-i", path, "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", pixelFormat, "-"], XunitCancellationToken);
        Assert.Equal(0, decode.ExitCode);
        var planes = decode.StandardOutput.ToArray();
        Assert.HasCount((width * height) + (gray ? 0 : 2 * chromaWidth * chromaHeight), planes);

        // (1) The same coefficients: unit plane differences at most (IEEE 1180 IDCT accuracy)
        var reference = ReferenceJpeg.Parse(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        var referencePlanes = reference.DecodePlanes();
        var offset = 0;
        for (var c = 0; c < referencePlanes.Count; c++)
        {
            var plane = referencePlanes[c];
            Assert.HasCount(c == 0 ? width * height : chromaWidth * chromaHeight, plane);
            var worst = 0;
            for (var i = 0; i < plane.Length; i++)
            {
                worst = Math.Max(worst, Math.Abs(plane[i] - planes[offset + i]));
            }

            Assert.True(worst <= 1, string.Create(CultureInfo.InvariantCulture, $"{path.Name} plane {c}: FFmpeg {ffmpeg.Version} differs from the reference decoding by {worst}."));
            offset += plane.Length;
        }

        // (2) Reconstruction from FFmpeg's planes (chroma replicated, JFIF equations) against the source
        RawPixelBuffer decoded;
        if (gray)
        {
            decoded = RawPixelBuffer.Create(width, height, RawPixelLayout.Gray8, planes);
        }
        else
        {
            var rgb = new byte[width * height * 3];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    double luma = planes[(y * width) + x];
                    var chroma = ((y / v) * chromaWidth) + (x / h);
                    var cb = planes[(width * height) + chroma] - 128d;
                    var cr = planes[(width * height) + (chromaWidth * chromaHeight) + chroma] - 128d;
                    var index = ((y * width) + x) * 3;
                    rgb[index] = ToByte(luma + (1.402 * cr));
                    rgb[index + 1] = ToByte(luma - (0.344136286 * cb) - (0.714136286 * cr));
                    rgb[index + 2] = ToByte(luma + (1.772 * cb));
                }
            }

            decoded = RawPixelBuffer.Create(width, height, RawPixelLayout.Rgb8, rgb);
        }

        return ReconstructionError.Measure(expected, decoded);

        static byte ToByte(double value) => (byte)Math.Clamp(Math.Floor(value + 0.5), 0, 255);
    }

    private static byte[] CreateIccProfile(int length)
    {
        var data = new byte[length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data, (uint)data.Length);
        "mntr"u8.CopyTo(data.AsSpan(12));
        "RGB "u8.CopyTo(data.AsSpan(16));
        "XYZ "u8.CopyTo(data.AsSpan(20));
        "acsp"u8.CopyTo(data.AsSpan(36));
        for (var i = 132; i < length; i++)
        {
            data[i] = (byte)(i * 13);
        }

        return data;
    }
}
