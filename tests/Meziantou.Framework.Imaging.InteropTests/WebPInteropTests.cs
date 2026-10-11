using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// WebP interoperability with the libwebp reference tools (<c>dwebp</c>, <c>webpmux</c> and <c>anim_dump</c>)
/// and FFmpeg's own WebP decoders (libavcodec <c>webp</c> and <c>vp8</c>, independent of libwebp):
/// <list type="bullet">
/// <item><description>lossless output of every corpus reference decodes exactly in dwebp and FFmpeg, hidden colors included;</description></item>
/// <item><description>lossy output is decoded to identical YUV planes by dwebp and FFmpeg (two independent VP8 decoders), with an
/// exact alpha plane, and the library decodes it within one unit of dwebp;</description></item>
/// <item><description>animations written with seek-and-patch (path and seekable stream, save and writer) are read by webpmux
/// (frame parameters, loop count) and every frame decodes as intended;</description></item>
/// <item><description>metadata chunks are extracted byte for byte by webpmux.</description></item>
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

    private static byte[] GetBytes(Image<Rgba32> image)
    {
        var bytes = new byte[image.Width * image.Height * 4];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }
}
