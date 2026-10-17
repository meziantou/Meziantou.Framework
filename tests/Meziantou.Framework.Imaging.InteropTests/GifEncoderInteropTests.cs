using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Gif;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// GIF encoder output decoded by independent decoders. The pinned FFmpeg decodes every displayed frame in one
/// pass (<c>-ignore_loop 1</c>, <c>-fps_mode passthrough</c>, explicit RGBA). Two kinds of checks are kept separate:
/// (1) exact compositing on exactly representable palettes: animations with opaque-to-transparent transitions, frames that
/// become fully transparent or opaque again, and per-frame palettes, every pixel compared with the intended displayed frame;
/// (2) quantized inputs (photo-like frames, with and without Floyd–Steinberg dithering, interlaced or not): FFmpeg's frames
/// must equal the harness reference decoding of the same file exactly (both read the same palette indices), and the
/// quantization error against the source stays within documented bounds.
/// </summary>
/// <remarks>
/// Fully transparent pixels are compared as transparent whatever their hidden color: FFmpeg fills cleared areas with its own
/// transparent color (transparent white or black depending on the version and the frame), which has no visible effect.
/// Timing and plays are not compared with FFmpeg (its demuxer applies a minimum delay and its loop convention differs);
/// the encoded fields are checked by the unit and conformance tests. On macOS, Apple ImageIO
/// (CGImageSource, through <c>tools/Meziantou.Framework.Imaging.CorpusGenerator/imageio_frames.swift --binary-alpha</c>) decodes the exact animations as
/// well; the CI interop job runs on macOS and requires them (MEZIANTOU_FRAMEWORK_IMAGING_IMAGEIO_REQUIRED=true); elsewhere they are skipped.
/// </remarks>
public sealed class GifEncoderInteropTests
{
    private const int Width = 23;
    private const int Height = 17;

    public static TheoryData<bool, bool> ExactCases => new()
    {
        { false, false },
        { true, false },
        { false, true },
        { true, true },
    };

    [Theory]
    [MemberData(nameof(ExactCases))]
    public async Task ExactAnimationsDecodeExactlyWithFFmpeg(bool interlaced, bool dithering)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateTransitions(Width, Height, seed: interlaced ? 3 : 4);
        var encoder = new GifEncoder { Interlaced = interlaced, Dithering = dithering ? GifDithering.FloydSteinberg : GifDithering.None };
        var name = $"exact-{(interlaced ? "interlaced" : "progressive")}-{(dithering ? "dither" : "nodither")}";
        var path = await SaveAsync(image, encoder, name);
        var verification = GifOutputVerifier.Verify(await File.ReadAllBytesAsync(path, XunitCancellationToken), image, encoder, context: name);
        Assert.All(verification.Errors, error => Assert.Null(error));
        var decoded = await ffmpeg.DecodeToRawFramesAsync(path, "rgba", Width, Height, ignoreLoop: true, XunitCancellationToken);
        AssertFrames(verification.Intents, decoded, $"{name} decoded by FFmpeg {ffmpeg.Version}");
    }

    [Theory]
    [InlineData(GifDithering.None, false, 256)]
    [InlineData(GifDithering.None, true, 256)]
    [InlineData(GifDithering.FloydSteinberg, false, 256)]
    [InlineData(GifDithering.FloydSteinberg, true, 32)]
    public async Task QuantizedAnimationsDecodeLikeTheReferenceReader(GifDithering dithering, bool interlaced, int maxColors)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreatePhotoAnimation(96, 64);
        var encoder = new GifEncoder { Dithering = dithering, Interlaced = interlaced, MaxColors = maxColors };
        var name = $"quantized-{dithering}-{(interlaced ? "interlaced" : "progressive")}-{maxColors}";
        var path = await SaveAsync(image, encoder, name);
        var verification = GifOutputVerifier.Verify(await File.ReadAllBytesAsync(path, XunitCancellationToken), image, encoder, context: name);
        var decoded = await ffmpeg.DecodeToRawFramesAsync(path, "rgba", image.Width, image.Height, ignoreLoop: true, XunitCancellationToken);
        AssertFrames(verification.Displayed, decoded, $"{name} decoded by FFmpeg {ffmpeg.Version}");

        // Documented quantization bounds (opaque photo-like frames, measured with the reference reader when the encoder was written)
        var minimumPsnr = (maxColors, dithering) switch
        {
            (256, GifDithering.None) => 30d,
            (256, _) => 28d,
            _ => 19.5,
        };

        for (var i = 0; i < decoded.Count; i++)
        {
            var error = verification.Errors[i]!;
            Assert.True(error.Psnr >= minimumPsnr && error.MaxChannelBias < 1.5, $"{name} frame {i}: {error.Describe()}");
        }
    }

    [Fact]
    public async Task UnknownCountWriterOutputDecodesWithFFmpeg()
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateTransitions(Width, Height, seed: 9);
        using var stream = new TestOutputStream { ForbidSynchronousWrites = true }; // non-seekable, asynchronous writes only
        await using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder { Interlaced = true } }))
        {
            foreach (var frame in image.Frames)
            {
                await writer.WriteFrameAsync(frame, XunitCancellationToken);
            }

            await writer.CompleteAsync(XunitCancellationToken);
        }

        var path = InteropSettings.GetArtifactsDirectory(nameof(GifEncoderInteropTests)) / "writer-unknown-count.gif";
        await File.WriteAllBytesAsync(path, stream.ToArray(), XunitCancellationToken);
        var intents = Enumerable.Range(0, image.Frames.Count).Select(i => GifOutputVerifier.ToIntent(image.Frames[i], new GifEncoder())).ToList();
        var decoded = await ffmpeg.DecodeToRawFramesAsync(path, "rgba", Width, Height, ignoreLoop: true, XunitCancellationToken);
        AssertFrames(intents, decoded, $"writer output decoded by FFmpeg {ffmpeg.Version}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactAnimationsDecodeExactlyWithAppleImageIO(bool interlaced)
    {
        // Apple ImageIO exists only on macOS: skipped elsewhere locally; required (never skipped) in the CI interop job (macOS)
        var (swift, script) = RequiredTools.AppleImageIO();

        using var image = CreateTransitions(Width, Height, seed: 21);
        var encoder = new GifEncoder { Interlaced = interlaced };
        var name = $"imageio-{(interlaced ? "interlaced" : "progressive")}";
        var path = await SaveAsync(image, encoder, name);
        var verification = GifOutputVerifier.Verify(await File.ReadAllBytesAsync(path, XunitCancellationToken), image, encoder, context: name);
        var result = await ProcessRunner.RunAsync(swift, [script, "--binary-alpha", path], XunitCancellationToken);
        if (result.ExitCode != 0)
            Assert.Fail($"ImageIO failed (exit code {result.ExitCode}): {result.StandardError}");

        var header = result.StandardError.Trim().Split(' ');
        Assert.Equal(["frames", image.Frames.Count.ToString(CultureInfo.InvariantCulture), Width.ToString(CultureInfo.InvariantCulture), Height.ToString(CultureInfo.InvariantCulture), "8"], header);
        var frameLength = Width * Height * 4;
        var frames = Enumerable.Range(0, image.Frames.Count).Select(i => result.StandardOutput.Slice(i * frameLength, frameLength).ToArray()).ToList();
        AssertFrames(verification.Intents, frames, name + " decoded by Apple ImageIO");
    }

    private static void AssertFrames(IReadOnlyList<RawPixelBuffer> expected, IReadOnlyList<byte[]> decoded, string context)
    {
        Assert.Equal(expected.Count, decoded.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            var actual = RawPixelBuffer.Create(expected[i].Width, expected[i].Height, RawPixelLayout.Rgba8, decoded[i]).WithTransparentColorsCleared();
            var result = PixelBufferComparer.Compare(expected[i].WithTransparentColorsCleared(), actual, ComparisonPolicy.Exact, $"{context}, frame {i}");
            Assert.True(result.IsMatch, result.Describe());
        }
    }

    private static async Task<FullPath> SaveAsync(Image image, GifEncoder encoder, string name)
    {
        var path = InteropSettings.GetArtifactsDirectory(nameof(GifEncoderInteropTests)) / (name + ".gif");
        await image.SaveAsync(path, encoder, XunitCancellationToken);
        return path;
    }

    /// <summary>
    /// Seven frames, each with its own palette of at most 200 seeded colors: opaque; a region becoming transparent; opaque
    /// again with other colors; fully transparent; transparent where the previous frame was opaque and opaque where it was
    /// transparent (checkerboard, then its complement); partially transparent alpha below the threshold.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static Image<Rgba32> CreateTransitions(int width, int height, int seed)
    {
        var random = new Random(seed);
        var image = new Image<Rgba32>(width, height);
        for (var i = 1; i < 7; i++)
        {
            image.AppendFrame();
        }

        for (var i = 0; i < image.Frames.Count; i++)
        {
            var palette = Enumerable.Range(0, 200).Select(_ => new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255)).ToArray();
            var index = i;
            image.Frames[i].Metadata.Duration = new FrameDuration(i + 1, 20);
            image.Frames[i].ProcessPixelRows(pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var color = palette[random.Next(palette.Length)];
                        var transparent = index switch
                        {
                            1 => x >= width / 3 && y >= height / 4,
                            3 => true,
                            4 => ((x + y) & 1) == 0,
                            5 => ((x + y) & 1) == 1,
                            6 => x < width / 2,
                            _ => false,
                        };

                        row[x] = transparent ? new Rgba32(color.R, color.G, color.B, (byte)random.Next(index == 6 ? 128 : 1)) : color;
                    }
                }
            });
        }

        image.Animation = new AnimationMetadata { TotalPlays = 2 };
        return image;
    }

    /// <summary>Three opaque photo-like frames (smooth gradients plus seeded noise, thousands of colors each).</summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static Image<Rgba32> CreatePhotoAnimation(int width, int height)
    {
        var random = new Random(17);
        var image = new Image<Rgba32>(width, height);
        image.AppendFrame();
        image.AppendFrame();
        for (var i = 0; i < image.Frames.Count; i++)
        {
            var phase = i * 5;
            image.Frames[i].Metadata.Duration = new FrameDuration(1, 10);
            image.Frames[i].ProcessPixelRows(pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var r = 128 + (100 * Math.Sin((x + phase) / 9d)) + random.Next(-5, 6);
                        var g = (255d * y / height) + random.Next(-5, 6);
                        var b = 128 + (90 * Math.Cos((x + y + phase) / 13d)) + random.Next(-5, 6);
                        row[x] = new Rgba32((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255), 255);
                    }
                }
            });
        }

        image.Animation = new AnimationMetadata();
        return image;
    }

}
