using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.Tests;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// WebP interoperability with the libwebp reference tools (<c>dwebp</c>, <c>webpmux</c> and <c>anim_dump</c>)
/// and FFmpeg's own WebP decoders (libavcodec <c>webp</c> and <c>vp8</c>, independent of libwebp):
/// <list type="bullet">
/// <item><description>lossless output of every corpus reference decodes exactly in dwebp and FFmpeg, hidden colors included, and so
/// does the output of images that lead the encoder to each of its choices of transforms and to meta prefix codes;</description></item>
/// <item><description>lossy output is decoded to identical YUV planes by dwebp and FFmpeg (two independent VP8 decoders), with an
/// exact alpha plane, and the library decodes it within one unit of dwebp;</description></item>
/// <item><description>animations written with seek-and-patch (path and seekable stream, save and writer) are read by webpmux
/// (frame parameters, loop count) and every frame decodes as intended;</description></item>
/// <item><description>metadata chunks are extracted byte for byte by webpmux;</description></item>
/// <item><description>the streams of the options and fallbacks that ordinary images do not reach (modes under a first-partition
/// budget, with and without the skip flag, reduced alpha levels, near-lossless colors) decode the same way.</description></item>
/// </list>
/// The committed corpus references are re-verified against the installed libwebp by the corpus generator (<c>verify</c>).
/// </summary>
public sealed class WebPInteropTests
{
    public static TheoryData<string> EncoderFixtures => [.. GoldenCorpus.Default.GetIds(kind: FixtureKinds.Valid).Where(id => EncoderSources.GetCases(GoldenCorpus.Default).Any(item => item.Fixture.Id == id && item.Format == PixelFormat.Rgba32))];

    [Theory]
    [MemberData(nameof(EncoderFixtures))]
    public async Task LosslessOutputDecodesExactlyInLibWebPAndFFmpeg(string id)
    {
        var libwebp = await RequiredTools.LibWebPAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var fixture = GoldenCorpus.Default.Get(id);
        foreach (var (name, source) in EncoderSources.GetStills(fixture, PixelFormat.Rgba32))
        {
            using var image = EncoderSources.CreateImage(source, PixelFormat.Rgba32);
            var path = await SaveAsync(image, new WebPEncoder(), $"lossless-{id.Replace('/', '-')}-{name.Replace(' ', '-')}");
            var context = $"{id} {name}";

            var (width, height, rgba) = await libwebp.DecodeAsync(path, XunitCancellationToken);
            Assert.Equal((source.Width, source.Height), (width, height));
            Assert.True(source.Span.SequenceEqual(rgba), $"{context}: dwebp {libwebp.Version} decodes different samples.");

            var frames = await ffmpeg.DecodeToRawFramesAsync(path, "rgba", source.Width, source.Height, XunitCancellationToken);
            Assert.True(source.Span.SequenceEqual(Assert.Single(frames)), $"{context}: FFmpeg {ffmpeg.Version} decodes different samples.");

            var info = await libwebp.GetInfoAsync(path, XunitCancellationToken);
            Assert.Equal((source.Width, source.Height), (info.Width, info.Height));
        }
    }

    [Theory]
    [InlineData("mixed", 3)] // noise beside flat graphics: an entropy image
    [InlineData("mixed", 9)]
    [InlineData("ramp", 5)] // 120 colors: color indexing, predicted or not
    [InlineData("ramp", 9)]
    [InlineData("alpha-ramp", 5)] // 256 alpha levels: the predictor on the indexes or on the colors
    [InlineData("gradient", 3)] // more than 256 colors: the predictor on tiles of 8 to 64 pixels
    [InlineData("gradient", 9)]
    [InlineData("blocks", 5)] // flat blocks of 300 colors: subtract green alone
    [InlineData("blocks", 9)]
    public async Task LosslessEncoderChoicesDecodeExactlyInLibWebPAndFFmpeg(string pattern, int effort)
    {
        // Images that lead the encoder to its alternative transforms, meta prefix codes and cost-model parse
        var libwebp = await RequiredTools.LibWebPAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        const int Width = 203;
        const int Height = 151;
        var pixels = CreatePattern(pattern, Width, Height);
        using var image = Image.ImportPixelData<Rgba32>(pixels, Width, Height);
        var source = GetBytes(image);
        var path = await SaveAsync(image, new WebPEncoder { Effort = effort }, $"lossless-{pattern}-e{effort}");
        var context = $"{pattern} effort {effort}";

        var (width, height, rgba) = await libwebp.DecodeAsync(path, XunitCancellationToken);
        Assert.Equal((Width, Height), (width, height));
        Assert.True(source.AsSpan().SequenceEqual(rgba), $"{context}: dwebp {libwebp.Version} decodes different samples.");

        var frames = await ffmpeg.DecodeToRawFramesAsync(path, "rgba", Width, Height, XunitCancellationToken);
        Assert.True(source.AsSpan().SequenceEqual(Assert.Single(frames)), $"{context}: FFmpeg {ffmpeg.Version} decodes different samples.");
    }

    [Theory]
    [MemberData(nameof(EncoderFixtures))]
    public async Task LossyOutputDecodesIdenticallyInIndependentDecoders(string id)
    {
        var libwebp = await RequiredTools.LibWebPAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var fixture = GoldenCorpus.Default.Get(id);
        foreach (var (name, source) in EncoderSources.GetStills(fixture, PixelFormat.Rgba32))
        {
            using var image = EncoderSources.CreateImage(source, PixelFormat.Rgba32);
            foreach (var quality in new[] { 30, 90 })
            {
                var path = await SaveAsync(image, new WebPEncoder { Compression = WebPCompression.Lossy, Quality = quality }, $"lossy{quality}-{id.Replace('/', '-')}-{name.Replace(' ', '-')}");
                var context = $"{id} {name} (quality {quality})";

                // Two independent VP8 decoders reconstruct the same planes (the reconstruction is exactly specified)
                var planes = await libwebp.DecodeYuvAsync(path, XunitCancellationToken);
                var ffmpegPlanes = await DecodeYuvWithFFmpegAsync(ffmpeg, path, source.Width, source.Height);
                Assert.True(planes.AsSpan(0, ffmpegPlanes.Length).SequenceEqual(ffmpegPlanes), $"{context}: dwebp {libwebp.Version} and FFmpeg {ffmpeg.Version} reconstruct different planes.");

                // Alpha is lossless; the library agrees with dwebp within the rounding of the color conversion
                var (_, _, rgba) = await libwebp.DecodeAsync(path, XunitCancellationToken);
                for (var i = 3; i < rgba.Length; i += 4)
                {
                    Assert.Equal(source.Span[i], rgba[i]);
                }

                using var decoded = Image.Load<Rgba32>(path);
                AssertVisibleWithin(rgba, ImageSnapshots.CaptureFrame(decoded.Frames[0]).Span, maxError: 1, $"{context}: library vs dwebp");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModeBudgetedOutputDecodesIdenticallyInIndependentDecoders(bool cheapestModes)
    {
        // The first-partition limit is scaled down so that a 512x512 frame needs the budget; the smallest limit leaves the skip
        // flag out
        var libwebp = await RequiredTools.LibWebPAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        const int Width = 512;
        const int Height = 512;
        var rgba = CreateTexture(Width, Height);
        var macroblocks = (Width / 16) * (Height / 16);
        var smallest = 1;
        while (!Vp8Encoder.TryGetModeBitBudget(macroblocks, smallest, out _, out _))
        {
            smallest++;
        }

        var scope = new AllocationScope(ImageResourceLimits.Default);
        using var free = Vp8Encoder.Encode(scope, rgba, Width, Height, quality: 75, effort: 3, Vp8Encoder.MaxFirstPartitionSize, XunitCancellationToken);
        var freePartition = FirstPartitionSize(free.WrittenMemory.Span);
        var limit = cheapestModes ? smallest : (smallest + freePartition) / 2;
        Assert.True(Vp8Encoder.TryGetModeBitBudget(macroblocks, limit, out _, out var skipFlag));
        Assert.Equal(!cheapestModes, skipFlag);
        Assert.True(freePartition > limit);
        using var payload = Vp8Encoder.Encode(scope, rgba, Width, Height, quality: 75, effort: 3, limit, XunitCancellationToken);
        var vp8 = payload.WrittenMemory.ToArray();
        Assert.True(FirstPartitionSize(vp8) <= limit);

        var path = InteropSettings.GetArtifactsDirectory("WebPInteropTests") / $"mode-budget-{(cheapestModes ? "cheapest" : "half")}.webp";
        var file = new byte[20 + vp8.Length + (vp8.Length & 1)];
        "RIFF"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)(file.Length - 8));
        "WEBPVP8 "u8.CopyTo(file.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(16), (uint)vp8.Length);
        vp8.CopyTo(file, 20);
        await File.WriteAllBytesAsync(path, file, XunitCancellationToken);

        var planes = await libwebp.DecodeYuvAsync(path, XunitCancellationToken);
        var ffmpegPlanes = await DecodeYuvWithFFmpegAsync(ffmpeg, path, Width, Height);
        Assert.True(planes.AsSpan(0, ffmpegPlanes.Length).SequenceEqual(ffmpegPlanes), $"dwebp {libwebp.Version} and FFmpeg {ffmpeg.Version} reconstruct different planes.");
        var (_, _, decoded) = await libwebp.DecodeAsync(path, XunitCancellationToken);
        using var image = Image.Load<Rgba32>(path);
        AssertVisibleWithin(decoded, ImageSnapshots.CaptureFrame(image.Frames[0]).Span, maxError: 1, "library vs dwebp");

        static int FirstPartitionSize(ReadOnlySpan<byte> frame) => (frame[0] | (frame[1] << 8) | (frame[2] << 16)) >> 5;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    public async Task ReducedAlphaAndNearLosslessColorsDecodeInLibWebP(int level)
    {
        var libwebp = await RequiredTools.LibWebPAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var bytes = CreateTexture(160, 96);
        var pixels = new Rgba32[160 * 96];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32(bytes[i * 4], bytes[(i * 4) + 1], bytes[(i * 4) + 2], bytes[(i * 4) + 3]);
        }

        using var image = Image.ImportPixelData<Rgba32>(pixels, 160, 96);

        // Lossy output with reduced alpha levels (kept when smaller): the stored values are decoded exactly
        var lossyPath = await SaveAsync(image, new WebPEncoder { Compression = WebPCompression.Lossy, AlphaQuality = level }, $"alpha-quality-{level}");
        var (_, _, lossy) = await libwebp.DecodeAsync(lossyPath, XunitCancellationToken);
        using var lossyImage = Image.Load<Rgba32>(lossyPath);
        AssertVisibleWithin(lossy, ImageSnapshots.CaptureFrame(lossyImage.Frames[0]).Span, maxError: 1, $"alpha quality {level}: library vs dwebp");
        if (level == 0)
        {
            Assert.HasCountLessThanOrEqual(2, lossy.Where((_, index) => index % 4 == 3).Distinct());
        }

        // Near-lossless output is lossless output of changed colors: every decoder returns the same samples
        var nearPath = await SaveAsync(image, new WebPEncoder { NearLossless = level }, $"near-lossless-{level}");
        var (_, _, near) = await libwebp.DecodeAsync(nearPath, XunitCancellationToken);
        using var nearImage = Image.Load<Rgba32>(nearPath);
        Assert.True(ImageSnapshots.CaptureFrame(nearImage.Frames[0]).Span.SequenceEqual(near), $"near-lossless {level}: dwebp {libwebp.Version} decodes different samples.");
        var frames = await ffmpeg.DecodeToRawFramesAsync(nearPath, "rgba", 160, 96, XunitCancellationToken);
        Assert.True(near.AsSpan().SequenceEqual(Assert.Single(frames)), $"near-lossless {level}: FFmpeg {ffmpeg.Version} decodes different samples.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SeekAndPatchAnimationsAreReadByLibWebP(bool lossy, bool writer)
    {
        var libwebp = await RequiredTools.LibWebPAsync();
        int[] durations = [100, 7, 0, 1000];
        var frames = CreateFrames(durations.Length, 18, 10);
        try
        {
            var encoder = new WebPEncoder { Compression = lossy ? WebPCompression.Lossy : WebPCompression.Lossless };
            var directory = InteropSettings.GetArtifactsDirectory(nameof(SeekAndPatchAnimationsAreReadByLibWebP));
            var path = directory / $"animation-{(lossy ? "lossy" : "lossless")}-{(writer ? "writer" : "save")}.webp";
            if (writer)
            {
                // A seekable FileStream: frames are written as they arrive, the RIFF size and VP8X flags are patched at completion
                await using var stream = File.Create(path);
                var options = new ImageWriterOptions(18, 10) { Encoder = encoder, Animation = new AnimationMetadata { TotalPlays = 4 } };
                await using var imageWriter = Image.CreateWriter<Rgba32>(stream, options);
                for (var i = 0; i < frames.Length; i++)
                {
                    frames[i].Frames[0].Metadata.Duration = new FrameDuration(durations[i], 1000);
                    await imageWriter.WriteFrameAsync((ImageFrame<Rgba32>)frames[i].Frames[0], XunitCancellationToken);
                }

                await imageWriter.CompleteAsync(XunitCancellationToken);
            }
            else
            {
                using var image = frames[0].Clone();
                image.Frames[0].Metadata.Duration = new FrameDuration(durations[0], 1000);
                for (var i = 1; i < frames.Length; i++)
                {
                    image.AppendFrame(frames[i].Frames[0]);
                    image.Frames[i].Metadata.Duration = new FrameDuration(durations[i], 1000);
                }

                image.Animation = new AnimationMetadata { TotalPlays = 4 };
                await image.SaveAsync(path, encoder, XunitCancellationToken); // atomically published path (seekable temporary file)
            }

            var info = await libwebp.GetInfoAsync(path, XunitCancellationToken);
            Assert.Equal((18, 10), (info.Width, info.Height));
            Assert.Contains("animation", info.Features, StringComparison.Ordinal);
            Assert.Contains("transparency", info.Features, StringComparison.Ordinal);
            Assert.Equal(4, info.LoopCount);
            Assert.Equal(durations, info.Frames.Select(frame => frame.DurationMilliseconds));
            Assert.All(info.Frames, frame =>
            {
                Assert.Equal((18, 10, 0, 0, "none", false), (frame.Width, frame.Height, frame.X, frame.Y, frame.Dispose, frame.Blend));
                Assert.Equal(lossy ? "lossy" : "lossless", frame.Compression);
            });

            using var decoded = Image.Load<Rgba32>(path);
            for (var i = 0; i < frames.Length; i++)
            {
                // Full-canvas frames without blending: the displayed frame is the frame image, decoded by dwebp
                var (width, height, rgba) = await libwebp.DecodeAsync(await libwebp.ExtractFrameAsync(path, i + 1, XunitCancellationToken), XunitCancellationToken);
                Assert.Equal((18, 10), (width, height));
                var source = GetBytes(frames[i]);
                if (lossy)
                {
                    AssertVisibleWithin(rgba, ImageSnapshots.CaptureFrame(decoded.Frames[i]).Span, maxError: 1, $"frame {i}: library vs dwebp");
                    for (var p = 3; p < rgba.Length; p += 4)
                    {
                        Assert.Equal(source[p], rgba[p]);
                    }
                }
                else
                {
                    Assert.Equal(source, rgba);
                }
            }

            var dumped = await libwebp.DumpAnimationAsync(path, XunitCancellationToken);
            Assert.Equal(frames.Length, dumped.Count);
            for (var i = 0; i < frames.Length; i++)
            {
                AssertVisibleWithin(ImageSnapshots.CaptureFrame(decoded.Frames[i]).Span, dumped[i].Rgba, maxError: lossy ? 1 : 0, $"frame {i}: library vs anim_dump");
            }
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    public static TheoryData<string, bool> OptimizedFrameAnimations()
    {
        var data = new TheoryData<string, bool>();
        foreach (var name in DeltaFrameAnimations.Names)
        {
            data.Add(name, false);
            data.Add(name, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(OptimizedFrameAnimations))]
    public async Task OptimizedFramesAreComposedByLibWebP(string animation, bool lossy)
    {
        // Regions at even offsets, alpha blending of opaque pixels over the canvas, and disposal to the background color
        var libwebp = await RequiredTools.LibWebPAsync();
        using var image = DeltaFrameAnimations.Create(animation);
        var encoder = new WebPEncoder { OptimizeFrames = true, Compression = lossy ? WebPCompression.Lossy : WebPCompression.Lossless };
        var path = await SaveAsync(image, encoder, $"optimized-{animation}-{(lossy ? "lossy" : "lossless")}");
        var info = await libwebp.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal(image.Frames.Count, info.Frames.Count);
        Assert.Equal(3, info.LoopCount);
        Assert.Equal((image.Width, image.Height, 0, 0), (info.Frames[0].Width, info.Frames[0].Height, info.Frames[0].X, info.Frames[0].Y));
        Assert.All(info.Frames, frame => Assert.True(frame.X % 2 == 0 && frame.Y % 2 == 0, $"frame at ({frame.X}, {frame.Y})"));
        Assert.Equal(Enumerable.Range(1, image.Frames.Count).Select(i => 50 * i), info.Frames.Select(frame => frame.DurationMilliseconds));

        using var decoded = Image.Load<Rgba32>(path);
        var dumped = await libwebp.DumpAnimationAsync(path, XunitCancellationToken);
        Assert.Equal(image.Frames.Count, dumped.Count);
        for (var i = 0; i < image.Frames.Count; i++)
        {
            var source = DeltaFrameAnimations.GetPixels(image.Frames[i]);
            if (lossy)
            {
                AssertVisibleWithin(ImageSnapshots.CaptureFrame(decoded.Frames[i]).Span, dumped[i].Rgba, maxError: 1, $"{animation} frame {i}: library vs anim_dump");
                for (var p = 3; p < source.Length; p += 4)
                {
                    Assert.Equal(source[p], dumped[i].Rgba[p]);
                }
            }
            else
            {
                Assert.Equal(source, dumped[i].Rgba);
            }
        }
    }

    [Theory]
    [MemberData(nameof(DeltaFrameCases))]
    public async Task OptimizedFramesAreComposedByAppleImageIO(string animation)
    {
        // Apple ImageIO exists only on macOS: skipped elsewhere locally; required (never skipped) in the CI interop job (macOS)
        _ = RequiredTools.AppleImageIO();
        using var image = DeltaFrameAnimations.Create(animation);
        var path = await SaveAsync(image, new WebPEncoder { OptimizeFrames = true }, $"imageio-optimized-{animation}");
        var decoded = await RequiredTools.DecodeWithAppleImageIOAsync(path, image.Width, image.Height, binaryAlpha: false);
        Assert.Equal(image.Frames.Count, decoded.Count);
        for (var i = 0; i < decoded.Count; i++)
        {
            var comparison = PixelBufferComparer.Compare(ImageSnapshots.CaptureFrame(image.Frames[i]).WithTransparentColorsCleared(), decoded[i], ComparisonPolicy.Exact, $"{animation} frame {i} decoded by Apple ImageIO");
            Assert.True(comparison.IsMatch, comparison.Describe());
        }
    }

    public static TheoryData<string> DeltaFrameCases => DeltaFrameAnimations.Cases;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataChunksAreReadByWebPMux(bool lossy)
    {
        var libwebp = await RequiredTools.LibWebPAsync();
        using var image = CreateFrames(1, 7, 5)[0];
        var icc = new byte[200];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(icc, 200);
        "mntr"u8.CopyTo(icc.AsSpan(12));
        "RGB "u8.CopyTo(icc.AsSpan(16));
        "XYZ "u8.CopyTo(icc.AsSpan(20));
        "acsp"u8.CopyTo(icc.AsSpan(36));
        var xmp = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><lorem>ipsum</lorem></x:xmpmeta>"u8.ToArray();
        image.Metadata.IccProfile = new IccProfile(new MetadataBlob(icc));
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob(xmp));
        image.Metadata.Orientation = ExifOrientation.BottomRight;
        var path = await SaveAsync(image, new WebPEncoder { Compression = lossy ? WebPCompression.Lossy : WebPCompression.Lossless }, $"metadata-{(lossy ? "lossy" : "lossless")}");

        Assert.Equal(icc, await libwebp.GetMetadataAsync(path, "icc", XunitCancellationToken));
        Assert.Equal(xmp, await libwebp.GetMetadataAsync(path, "xmp", XunitCancellationToken));
        var exif = await libwebp.GetMetadataAsync(path, "exif", XunitCancellationToken);
        Assert.NotNull(exif);
        Assert.Equal(EncodedFieldInspector.Inspect("webp", await File.ReadAllBytesAsync(path, XunitCancellationToken)).Exif!.Value.ToArray(), exif);
        var info = await libwebp.GetInfoAsync(path, XunitCancellationToken);
        Assert.Contains("ICC profile", info.Features, StringComparison.Ordinal);
        Assert.Contains("EXIF metadata", info.Features, StringComparison.Ordinal);
        Assert.Contains("XMP metadata", info.Features, StringComparison.Ordinal);
        await libwebp.DecodeAsync(path, XunitCancellationToken);
    }

    private static async Task<byte[]> DecodeYuvWithFFmpegAsync(FFmpegTool ffmpeg, FullPath path, int width, int height)
    {
        // Raw 4:2:0 planes without any color conversion (alpha, when present, is dropped by the plane selection)
        var result = await ffmpeg.RunAsync(["-i", path, "-f", "rawvideo", "-pix_fmt", "yuv420p", "-"], XunitCancellationToken);
        Assert.Equal(0, result.ExitCode);
        var length = (width * height) + (2 * ((width + 1) / 2) * ((height + 1) / 2));
        Assert.Equal(length, result.StandardOutput.Length);
        return result.StandardOutput.ToArray();
    }

    private static void AssertVisibleWithin(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, int maxError, string context)
    {
        if (expected.Length != actual.Length)
            Assert.Fail($"{context}: {actual.Length} bytes, expected {expected.Length}.");

        for (var i = 0; i < expected.Length; i += 4)
        {
            if (expected[i + 3] != actual[i + 3])
                Assert.Fail($"{context}: alpha of pixel {i / 4} is {actual[i + 3]}, expected {expected[i + 3]}.");

            if (expected[i + 3] == 0)
                continue; // hidden colors have no visible effect

            for (var c = 0; c < 3; c++)
            {
                Assert.True(Math.Abs(expected[i + c] - actual[i + c]) <= maxError, $"{context}: pixel {i / 4} channel {c} is {actual[i + c]}, expected {expected[i + c]} (max error {maxError}).");
            }
        }
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static Rgba32[] CreatePattern(string pattern, int width, int height)
    {
        var random = new Random(42);
        var colors = new Rgba32[300];
        for (var i = 0; i < colors.Length; i++)
        {
            colors[i] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
        }

        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var level = (byte)(((x + (y / 3)) / 6 % 120) * 2);
                pixels[(y * width) + x] = pattern switch
                {
                    "mixed" => x < width / 2 ? new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255) : (x / 9 + y / 24) % 3 == 0 ? new Rgba32(30, 30, 30, 255) : new Rgba32(250, 250, 250, 255),
                    "ramp" => new Rgba32(level, (byte)(255 - level), (byte)(64 + (level / 2)), 255),
                    "alpha-ramp" => new Rgba32(30, 120, 220, (byte)(x * 255 / (width - 1))),
                    "gradient" => new Rgba32((byte)(x * 255 / (width - 1)), (byte)(y * 255 / (height - 1)), (byte)(x + y), 255),
                    "blocks" => colors[(((x / 16) * 7) + ((y / 16) * 13)) % colors.Length],
                    _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
                };
            }
        }

        return pixels;
    }

    private static async Task<FullPath> SaveAsync(Image image, WebPEncoder encoder, string name)
    {
        var path = InteropSettings.GetArtifactsDirectory("WebPInteropTests") / (name + ".webp");
        await image.SaveAsync(path, encoder, XunitCancellationToken);
        return path;
    }

    private static Image<Rgba32>[] CreateFrames(int count, int width, int height)
    {
        var frames = new Image<Rgba32>[count];
        for (var i = 0; i < count; i++)
        {
            var image = new Image<Rgba32>(width, height);
            var seed = i;
            image.Frames[0].ProcessPixelRows(pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        // Smooth colors with an alpha transition: opaque, translucent and transparent (hidden color) areas
                        var alpha = (x + seed) % 6 switch { 0 => 0, 1 => 96, _ => 255 };
                        row[x] = new Rgba32((byte)(20 + (x * 12)), (byte)(200 - (y * 15)), (byte)(60 + (seed * 40)), (byte)alpha);
                    }
                }
            });
            frames[i] = image;
        }

        return frames;
    }

    /// <summary>Oriented stripes of varying angle and period with soft circles, and an alpha ramp: detail that 4x4 prediction codes well.</summary>
    private static byte[] CreateTexture(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var angle = (x / 64 * 0.7) + (y / 64 * 1.3);
                var stripes = Math.Sin(((x * Math.Cos(angle)) + (y * Math.Sin(angle))) * (0.3 + ((x + y) % 5 * 0.1)));
                var circles = Math.Cos(Math.Sqrt(((x - 200) * (x - 200)) + ((y - 150) * (y - 150))) * 0.15);
                var i = ((y * width) + x) * 4;
                rgba[i] = (byte)(128 + (90 * stripes));
                rgba[i + 1] = (byte)(128 + (60 * circles) + (30 * stripes));
                rgba[i + 2] = (byte)(128 - (70 * stripes * circles));
                rgba[i + 3] = (byte)Math.Clamp((x - 20) * 3, 0, 255);
            }
        }

        return rgba;
    }

    private static byte[] GetBytes(Image<Rgba32> image)
    {
        var bytes = new byte[image.Width * image.Height * 4];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }
}
