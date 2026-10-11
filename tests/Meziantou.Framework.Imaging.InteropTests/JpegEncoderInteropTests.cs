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
/// Baseline JPEG encoder output decoded by the pinned FFmpeg. FFmpeg decodes every output to its native planes
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

    public static TheoryData<string, int, int, int> Cases()
    {
        var data = new TheoryData<string, int, int, int>();
        foreach (var sampling in new[] { "444", "422", "420", "gray" })
        {
            foreach (var quality in new[] { 10, 50, 90, 100 })
            {
                data.Add(sampling, quality, 96, 64);
                data.Add(sampling, quality, 37, 23);
            }
        }

        data.Add("420", 75, 1, 1);
        data.Add("422", 75, 17, 2);
        data.Add("444", 75, 2, 17);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EncodedImagesDecodeWithFFmpeg(string sampling, int quality, int width, int height)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var source = JpegTestPatterns.PhotoLike(width, height, seed: 7);
        var gray = sampling == "gray";
        if (gray)
        {
            source = JpegTestPatterns.ToGray(source);
        }

        var encoder = new JpegEncoder { Quality = quality, ChromaSubsampling = GetSubsampling(sampling) };
        var name = string.Create(CultureInfo.InvariantCulture, $"photo-{sampling}-q{quality}-{width}x{height}");
        var path = await EncodeToFileAsync(source, gray ? PixelFormat.Gray8 : PixelFormat.Rgb24, encoder, name);
        var error = await AssertDecodesWithFFmpegAsync(ffmpeg, path, source, sampling);
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: {new FileInfo(path).Length} bytes, {error.Describe()} (FFmpeg {ffmpeg.Version})"));
        if (width == 96 && MinimumPsnr.TryGetValue((sampling, quality), out var minimum))
        {
            Assert.True(error.Psnr >= minimum && (quality < 50 || error.MaxChannelBias < 0.75), string.Create(CultureInfo.InvariantCulture, $"{name}: {error.Describe()} (minimum {minimum} dB)."));
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

    [Fact]
    public async Task NonSeekableAsynchronousWriterOutputDecodesWithFFmpeg()
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var source = JpegTestPatterns.PhotoLike(640, 360, seed: 5);
        using var image = Image.ImportPixelBytes<Rgb24>(source.Span, 640, 360);
        var path = InteropSettings.GetArtifactsDirectory(nameof(JpegEncoderInteropTests)) / "large-writer.jpg";
        using (var stream = new TestOutputStream { ForbidSynchronousWrites = true })
        {
            await using (var writer = Image.CreateWriter<Rgb24>(stream, new ImageWriterOptions(640, 360) { Encoder = new JpegEncoder { Quality = 100, ChromaSubsampling = JpegChromaSubsampling.Ratio444 } }))
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
