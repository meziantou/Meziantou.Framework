using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Png;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// APNG encoder output decoded by independent decoders. The pinned FFmpeg decodes every displayed frame in one
/// pass (<c>-ignore_loop 1</c>, <c>-fps_mode passthrough</c>, explicit raw layout), compared exactly with the intended
/// full-canvas frames, for every storage type, both poster layouts and Adam7, including frames whose opaque regions become
/// transparent and edited (reordered, removed, resized) decoded animations. FFmpeg does not expose a separate poster: the
/// poster is checked by decoding the file without its APNG chunks (what a decoder without APNG support shows) as a static
/// PNG. Durations, plays, the frame count, sequence numbers and control fields are checked on the encoded data with the
/// independent harness reader (<see cref="ApngOutputVerifier"/>), never with FFmpeg timestamps. On macOS, Apple ImageIO
/// (CGImageSource, through <c>tools/Meziantou.Framework.Imaging.CorpusGenerator/imageio_frames.swift</c>) decodes the 8- and 16-bit RGBA outputs as
/// well; the CI interop job runs on macOS and requires them (MEZIANTOU_FRAMEWORK_IMAGING_IMAGEIO_REQUIRED=true); elsewhere they are skipped.
/// </summary>
public sealed class ApngEncoderInteropTests
{
    private const int Width = 13;
    private const int Height = 9;
    private static readonly PixelFormat[] AllFormats = [PixelFormat.Rgba32, PixelFormat.Bgra32, PixelFormat.Rgb24, PixelFormat.Rgba64, PixelFormat.Gray8, PixelFormat.Gray16];

    public static TheoryData<PixelFormat, bool, bool> Cases()
    {
        var data = new TheoryData<PixelFormat, bool, bool>();
        foreach (var format in AllFormats)
        {
            foreach (var poster in new[] { false, true })
            {
                data.Add(format, poster, false);
                data.Add(format, poster, true);
            }
        }

        return data;
    }

    public static TheoryData<string> ApngFixtures => [.. GoldenCorpus.Default.GetIds(format: "png", kind: "valid", feature: "apng")];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task AnimationsDecodeExactlyWithFFmpeg(PixelFormat format, bool poster, bool interlaced)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateAnimation(format, poster, seed: ((int)format * 7) + (poster ? 1 : 0));
        var name = $"{format}-{(poster ? "poster" : "frame0")}-{(interlaced ? "adam7" : "progressive")}";
        var path = await SaveAsync(image, new PngEncoder { Interlaced = interlaced }, name);
        ApngOutputVerifier.Verify(await File.ReadAllBytesAsync(path, XunitCancellationToken), image, context: name);
        await AssertFFmpegDecodesAsync(ffmpeg, path, image, name);
    }

    [Theory]
    [MemberData(nameof(ApngFixtures))]
    public async Task EditedCorpusAnimationsDecodeExactlyWithFFmpeg(string id)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var fixture = GoldenCorpus.Default.Get(id);
        using var image = Image.Load(fixture.ReadInput());

        // Reorder, remove, then make the first frame's opaque pixels transparent in a region: the delta, disposal and blend
        // choices of the source file must not survive (every output frame is the complete displayed image)
        if (image.Frames.Count > 1)
        {
            image.MoveFrame(image.Frames.Count - 1, 0);
        }

        if (image.Frames.Count > 2)
        {
            image.RemoveFrame(1);
        }

        ClearAlpha(image.Frames[0], image.Width / 2, image.Height / 2);
        image.Resize(new ResizeOptions(image.Width + 3, image.Height + 1) { Mode = ResizeMode.Stretch }, XunitCancellationToken);
        var name = "edited-" + id.Replace('/', '-');
        var path = await SaveAsync(image, new PngEncoder(), name);
        ApngOutputVerifier.Verify(await File.ReadAllBytesAsync(path, XunitCancellationToken), image, context: name);
        await AssertFFmpegDecodesAsync(ffmpeg, path, image, name);
    }

    [Fact]
    public async Task NonSeekableWriterOutputDecodesWithFFmpeg()
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateAnimation(PixelFormat.Rgba64, poster: true, seed: 5);
        image.Animation = new AnimationMetadata { TotalPlays = 1 };
        using var stream = new TestOutputStream { ForbidSynchronousWrites = true };
        await using (var writer = Image.CreateWriter<Rgba64>(stream, new ImageWriterOptions(Width, Height) { Encoder = new PngEncoder { AnimationMode = PngAnimationMode.Animated }, ExpectedFrameCount = image.Frames.Count, Animation = image.Animation }))
        {
            await writer.WritePosterFrameAsync(((Image<Rgba64>)image).PosterFrame!, XunitCancellationToken);
            foreach (var frame in ((Image<Rgba64>)image).Frames)
            {
                await writer.WriteFrameAsync(frame, XunitCancellationToken);
            }

            await writer.CompleteAsync(XunitCancellationToken);
        }

        var path = InteropSettings.GetArtifactsDirectory(nameof(ApngEncoderInteropTests)) / "writer-rgba64.apng";
        await File.WriteAllBytesAsync(path, stream.ToArray(), XunitCancellationToken);
        ApngOutputVerifier.Verify(stream.ToArray(), image, context: "writer output");
        await AssertFFmpegDecodesAsync(ffmpeg, path, image, "writer output");
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32, false, false)]
    [InlineData(PixelFormat.Rgba32, true, false)]
    [InlineData(PixelFormat.Rgba32, true, true)]
    [InlineData(PixelFormat.Rgba64, false, false)]
    [InlineData(PixelFormat.Rgba64, true, false)]
    [InlineData(PixelFormat.Rgba64, true, true)]
    public async Task AnimationsDecodeExactlyWithAppleImageIO(PixelFormat format, bool poster, bool singleFrame)
    {
        // Apple ImageIO exists only on macOS: skipped elsewhere locally; required (never skipped) in the CI interop job (macOS)
        var (swift, script) = RequiredTools.AppleImageIO();

        using var image = CreateAnimation(format, poster, seed: 40 + (int)format);
        while (singleFrame && image.Frames.Count > 1)
        {
            image.RemoveFrame(image.Frames.Count - 1);
        }

        var name = $"imageio-{format}-{(poster ? "poster" : "frame0")}{(singleFrame ? "-single" : "")}";
        var path = await SaveAsync(image, new PngEncoder(), name);
        var result = await ProcessRunner.RunAsync(swift, [script, path], XunitCancellationToken);
        if (result.ExitCode != 0)
            Assert.Fail($"ImageIO failed (exit code {result.ExitCode}): {result.StandardError}");

        // "frames <count> <width> <height> <bits>": ImageIO exposes the displayed frames, not the separate poster
        var header = result.StandardError.Trim().Split(' ');
        Assert.Equal(["frames", image.Frames.Count.ToString(CultureInfo.InvariantCulture), Width.ToString(CultureInfo.InvariantCulture), Height.ToString(CultureInfo.InvariantCulture)], header[..4]);
        var layout = header[4] == "16" ? RawPixelLayout.Rgba16Le : RawPixelLayout.Rgba8;
        Assert.Equal(format == PixelFormat.Rgba64 ? RawPixelLayout.Rgba16Le : RawPixelLayout.Rgba8, layout);
        var frameLength = layout.GetByteLength(Width, Height);
        Assert.Equal(image.Frames.Count * frameLength, result.StandardOutput.Length);
        // Known ImageIO behavior (corpus notes of apng/poster-rgba16-dispose and apng/separate-poster): with a separate poster
        // and several frames, ImageIO returns the second animation frame at index 0; every later frame is compared, and the
        // single-frame poster layout confirms frame 0 after a poster
        var first = poster && image.Frames.Count > 1 ? 1 : 0;
        for (var i = first; i < image.Frames.Count; i++)
        {
            // ImageIO may store another color in fully transparent pixels (corpus cross-check notes): compare them as transparent
            var expected = ImageSnapshots.CaptureFrame(image.Frames[i]).WithTransparentColorsCleared();
            var actual = RawPixelBuffer.Create(Width, Height, layout, result.StandardOutput.Slice(i * frameLength, frameLength).Span).WithTransparentColorsCleared();
            var comparison = PixelBufferComparer.Compare(expected, actual, ComparisonPolicy.Exact, $"{name} frame {i} decoded by Apple ImageIO");
            Assert.True(comparison.IsMatch, comparison.Describe());
        }
    }

    private static async Task AssertFFmpegDecodesAsync(FFmpegTool ffmpeg, FullPath path, Image image, string name)
    {
        var rawFormat = PngEncoderInteropTests.GetRawFormat(image.PixelFormat);
        var decoded = await ffmpeg.DecodeToRawFramesAsync(path, rawFormat, image.Width, image.Height, ignoreLoop: true, XunitCancellationToken);
        if (decoded.Count != image.Frames.Count)
            Assert.Fail($"{name}: FFmpeg {ffmpeg.Version} decoded {decoded.Count} frames, {image.Frames.Count} displayed frames expected (a poster is not displayed).");
        for (var i = 0; i < decoded.Count; i++)
        {
            AssertSame(ImageSnapshots.CaptureFrame(image.Frames[i]), decoded[i], $"{name} frame {i} decoded by ffmpeg {ffmpeg.Version}");
        }

        // A decoder without APNG support shows the IDAT image: the separate poster, or frame zero
        var staticPath = path.ChangeExtension(".static.png");
        await File.WriteAllBytesAsync(staticPath, ReferencePng.RemoveAnimationChunks(await File.ReadAllBytesAsync(path, XunitCancellationToken)), XunitCancellationToken);
        var still = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(staticPath, rawFormat, image.Width, image.Height, XunitCancellationToken));
        AssertSame(ImageSnapshots.CaptureFrame(image.PosterFrame ?? image.Frames[0]), still, $"{name} {(image.PosterFrame is null ? "frame 0" : "poster")} as a static PNG decoded by ffmpeg {ffmpeg.Version}");
    }

    private static void AssertSame(RawPixelBuffer expected, byte[] decoded, string context)
    {
        var result = PixelBufferComparer.Compare(expected, RawPixelBuffer.Create(expected.Width, expected.Height, expected.Layout, decoded), ComparisonPolicy.Exact, context);
        Assert.True(result.IsMatch, result.Describe());
    }

    private static async Task<FullPath> SaveAsync(Image image, PngEncoder encoder, string name)
    {
        var path = InteropSettings.GetArtifactsDirectory(nameof(ApngEncoderInteropTests)) / (name + ".apng");
        await image.SaveAsync(path, encoder, XunitCancellationToken);
        return path;
    }

    /// <summary>
    /// Five seeded random frames with durations 1/10, 1/3, 0, 7/9, 2 s and infinite play: frame 1 is fully opaque and frame
    /// 2 equals it except for a region that becomes fully transparent (with its colors kept), frame 4 equals frame 0.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static Image CreateAnimation(PixelFormat format, bool poster, int seed)
    {
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
        var random = new Random(seed);
        var frames = new byte[5][];
        for (var i = 0; i < frames.Length; i++)
        {
            frames[i] = new byte[Width * Height * bytesPerPixel];
            random.NextBytes(frames[i]);
        }

        var hasAlpha = format is PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgba64;
        if (hasAlpha)
        {
            SetAlpha(frames[1], bytesPerPixel, 0, 0, Width, Height, 0xFF);
            frames[2] = (byte[])frames[1].Clone();
            SetAlpha(frames[2], bytesPerPixel, 3, 2, 7, 5, 0);
        }

        frames[4] = (byte[])frames[0].Clone();
        FrameDuration[] durations = [new(1, 10), new(1, 3), FrameDuration.Zero, new(7, 9), new(2, 1)];
        var image = Import(format, frames[0]);
        image.Frames[0].Metadata.Duration = durations[0];
        for (var i = 1; i < frames.Length; i++)
        {
            using var frame = Import(format, frames[i]);
            image.AppendFrame(frame.Frames[0]).Metadata.Duration = durations[i];
        }

        if (poster)
        {
            var posterBytes = new byte[Width * Height * bytesPerPixel];
            random.NextBytes(posterBytes);
            using var posterImage = Import(format, posterBytes);
            image.SetPosterFrame(posterImage.Frames[0]);
        }

        image.Animation = new AnimationMetadata();
        return image;
    }

    private static void SetAlpha(byte[] pixels, int bytesPerPixel, int x0, int y0, int x1, int y1, byte value)
    {
        var alphaBytes = bytesPerPixel / 4;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                pixels.AsSpan((((y * Width) + x) * bytesPerPixel) + (3 * alphaBytes), alphaBytes).Fill(value);
            }
        }
    }

    private static void ClearAlpha(ImageFrame frame, int width, int height)
    {
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(frame.PixelFormat);
        var alphaBytes = bytesPerPixel / 4;
        frame.ProcessPixelBytes(pixels =>
        {
            for (var y = 0; y < Math.Max(1, height); y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < Math.Max(1, width); x++)
                {
                    row.Slice((x * bytesPerPixel) + (3 * alphaBytes), alphaBytes).Clear();
                }
            }
        });
    }

    private static Image Import(PixelFormat format, byte[] data) => format switch
    {
        PixelFormat.Rgba32 => Image.ImportPixelBytes<Rgba32>(data, Width, Height),
        PixelFormat.Bgra32 => Image.ImportPixelBytes<Bgra32>(data, Width, Height),
        PixelFormat.Rgb24 => Image.ImportPixelBytes<Rgb24>(data, Width, Height),
        PixelFormat.Rgba64 => Image.ImportPixelBytes<Rgba64>(data, Width, Height),
        PixelFormat.Gray8 => Image.ImportPixelBytes<Gray8>(data, Width, Height),
        _ => Image.ImportPixelBytes<Gray16>(data, Width, Height),
    };

}
