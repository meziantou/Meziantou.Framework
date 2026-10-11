using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.TestHarness.WebP;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// WebP decoding (still images and animations) of hand-assembled containers: canvas composition (alpha blending,
/// disposal, partial rectangles, an ANIM background color that is never painted), readers and caller edits, frame-limit
/// prefixes, truncation, limits, bounded sequential memory and cancellation. Frame payloads are VP8L bitstreams encoded by
/// the library and checked with the independent harness reader; the libwebp corpus covers the bitstream features
/// (Tests/Conformance).
/// </summary>
public sealed class WebPDecoderTests
{
    private static readonly Rgba32 Transparent = new(0, 0, 0, 0);

    [Fact]
    public void CanvasCompositionFollowsTheContainerSpecification()
    {
        // Frame 0: partial (2,0) 2x2 opaque; the rest of the canvas stays transparent black (the ANIM background is never painted)
        // Frame 1: full 4x3 opaque, no blend, disposed to background after display
        // Frame 2: (0,0) 2x2 translucent, alpha-blended over the cleared canvas
        // Frame 3: (2,2) 2x1 without blending: replaces the canvas pixels, a hidden color included
        var red = new Rgba32(255, 0, 0, 255);
        var green = new Rgba32(0, 255, 0, 255);
        var half = new Rgba32(0, 0, 255, 128);
        var frame0 = Fill(2, 2, red);
        var frame1 = Fill(4, 3, green);
        var frame2 = Fill(2, 2, half);
        var frame3 = new[] { new Rgba32(10, 20, 30, 0), new Rgba32(200, 100, 50, 64) };
        var data = Animation(4, 3, background: [0x00, 0xFF, 0xFF, 0xFF], loopCount: 2,
        [
            (2, 0, 2, 2, frame0, Blend: true, Dispose: false, Duration: 40),
            (0, 0, 4, 3, frame1, Blend: false, Dispose: true, Duration: 0),
            (0, 0, 2, 2, frame2, Blend: true, Dispose: false, Duration: 7),
            (2, 2, 2, 1, frame3, Blend: false, Dispose: false, Duration: 1000),
        ]);

        using var image = Image.Load<Rgba32>(data);
        Assert.Equal(4, image.Frames.Count);
        Assert.Equal(2, image.Animation!.TotalPlays);
        Assert.Equal([new FrameDuration(40, 1000), FrameDuration.Zero, new FrameDuration(7, 1000), new FrameDuration(1, 1)], Enumerable.Range(0, image.Frames.Count).Select(i => image.Frames[i].Metadata.Duration));
        Assert.Equal(
        [
            Transparent, Transparent, red, red,
            Transparent, Transparent, red, red,
            Transparent, Transparent, Transparent, Transparent,
        ], GetPixels(image.Frames[0]));
        Assert.All(GetPixels(image.Frames[1]), pixel => Assert.Equal(green, pixel));

        // Disposal cleared the whole canvas to transparent black: blending onto it keeps the source pixel
        Assert.Equal(
        [
            half, half, Transparent, Transparent,
            half, half, Transparent, Transparent,
            Transparent, Transparent, Transparent, Transparent,
        ], GetPixels(image.Frames[2]));

        // No blending: the frame replaces the canvas, hidden colors of transparent pixels included
        var last = GetPixels(image.Frames[3]);
        Assert.Equal(frame3[0], last[10]);
        Assert.Equal(frame3[1], last[11]);
    }

    [Fact]
    public void AlphaBlendingRoundsTheExactCompositeToNearest()
    {
        var background = new Rgba32(10, 200, 30, 255);
        var source = new Rgba32(250, 0, 101, 77);
        var data = Animation(1, 1, background: [0, 0, 0, 0], loopCount: 0,
        [
            (0, 0, 1, 1, new[] { background }, Blend: false, Dispose: false, Duration: 10),
            (0, 0, 1, 1, new[] { source }, Blend: true, Dispose: false, Duration: 10),
        ]);

        using var image = Image.Load<Rgba32>(data);
        var actual = GetPixels(image.Frames[1])[0];

        // Over an opaque pixel: color = round((s * a + d * (255 - a)) / 255), opaque result
        static byte Over(byte s, byte d, int a) => (byte)Math.Floor((((s * a) + (d * (255 - a))) / 255.0) + 0.5);
        Assert.Equal(new Rgba32(Over(250, 10, 77), Over(0, 200, 77), Over(101, 30, 77), 255), actual);
    }

    [Fact]
    public async Task ReadersProduceTheSameFramesAsEagerLoadsAndIgnoreCallerEdits()
    {
        var data = CreateLargeAnimation(frameCount: 5, width: 6, height: 4);
        using var eager = Image.Load<Rgba32>(data);
        await using (var reader = await Image.OpenReaderAsync<Rgba32>(new MemoryStream(data), cancellationToken: XunitCancellationToken))
        {
            for (var i = 0; i < eager.Frames.Count; i++)
            {
                using var frame = await reader.ReadFrameAsync(XunitCancellationToken);
                Assert.NotNull(frame);
                Assert.Equal(GetPixels(eager.Frames[i]), GetPixels(frame.Frames[0]));
                Assert.Equal(eager.Frames[i].Metadata.Duration, frame.Frames[0].Metadata.Duration);
                frame.Frames[0].ProcessPixelRows(rows =>
                {
                    for (var y = 0; y < rows.Height; y++)
                    {
                        rows.GetRowSpan(y).Fill(new Rgba32(1, 2, 3, 4));
                    }
                });
            }

            Assert.Null(await reader.ReadFrameAsync(XunitCancellationToken));
        }

        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(data)))
        {
            using var destination = new Image<Rgba32>(6, 4);
            var storage = destination.Frames[0];
            for (var i = 0; i < eager.Frames.Count; i++)
            {
                Assert.True(reader.ReadFrameInto(destination));
                Assert.Same(storage, destination.Frames[0]);
                Assert.Equal(GetPixels(eager.Frames[i]), GetPixels(destination.Frames[0]));
            }

            Assert.False(reader.ReadFrameInto(destination));
        }
    }

    [Fact]
    public void FrameLimitsSelectAPrefixWithoutExaminingTheRest()
    {
        var full = CreateLargeAnimation(frameCount: 4, width: 8, height: 8);
        var reader = ReferenceWebP.Parse(full);
        var third = reader.Chunks.Where(chunk => chunk.FourCC == "ANMF").ElementAt(2);
        var prefix = full[..(third.Offset + 20)]; // ends inside the third frame
        using (var image = Image.Load<Rgba32>(prefix, new ImageDecodeOptions { FrameLimit = 2 }))
        {
            Assert.Equal(2, image.Frames.Count);
        }

        Assert.Throws<InvalidImageContentException>(() => Image.Load(prefix, new ImageDecodeOptions { FrameLimit = 3 }));
        using var limited = Image.OpenReader<Rgba32>(new MemoryStream(prefix), new ImageReaderOptions { FrameLimit = 2 });
        using (limited.ReadFrame())
        using (limited.ReadFrame())
        {
        }

        Assert.Null(limited.ReadFrame());
    }

    [Fact]
    public void TruncationIsNeverACleanEnd()
    {
        var data = CreateLargeAnimation(frameCount: 3, width: 8, height: 8);
        foreach (var length in new[] { data.Length - 1, data.Length - 7, data.Length / 2, 40, 20 })
        {
            var truncated = data[..length];
            Assert.Throws<InvalidImageContentException>(() => Image.Load(truncated));
            Assert.Throws<InvalidImageContentException>(() =>
            {
                using var reader = Image.OpenReader<Rgba32>(new MemoryStream(truncated));
                while (reader.ReadFrame() is { } frame)
                {
                    frame.Dispose();
                }
            });
        }
    }

    [Fact]
    public void LimitsChargeFullCanvasFramesBeforeDecoding()
    {
        var data = CreateLargeAnimation(frameCount: 6, width: 4, height: 3);
        Assert.Equal(ImageResourceLimitKind.Frames, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Limits(new ImageResourceLimits { MaxFrames = 5 }))).Kind);
        using (Image.Load(data, Limits(new ImageResourceLimits { MaxTotalPixels = 72 })))
        {
        }

        Assert.Equal(ImageResourceLimitKind.TotalPixels, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Limits(new ImageResourceLimits { MaxTotalPixels = 71 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Limits(new ImageResourceLimits { MaxWidth = 3 }))).Kind);

        static ImageDecodeOptions Limits(ImageResourceLimits limits) => new() { Configuration = new ImageConfiguration { Limits = limits } };
    }

    [Fact]
    public void SequentialMemoryStaysBoundedWhenFramesAreDisposed()
    {
        var data = CreateLargeAnimation(frameCount: 60, width: 48, height: 32);
        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(data));
        var scope = reader.Core.Scope;
        long? firstLevel = null;
        long peakAfterFirst = 0;
        var count = 0;
        while (true)
        {
            using var frame = reader.ReadFrame();
            if (frame is null)
                break;

            count++;
            if (firstLevel is null)
            {
                firstLevel = scope.LiveBytes;
            }
            else
            {
                peakAfterFirst = Math.Max(peakAfterFirst, scope.LiveBytes);
            }
        }

        Assert.Equal(60, count);
        Assert.InRange(peakAfterFirst, 0, firstLevel!.Value + scope.GetChargedSize(48L * 32 * 4));
        Assert.Equal(0, scope.GetLiveBytes(AllocationKind.CompositorState)); // released at the clean end
    }

    [Fact]
    public async Task CancellationFaultsWithoutPartialResults()
    {
        var data = CreateLargeAnimation(frameCount: 12, width: 48, height: 32);
        using var source = new CancellationTokenSource();
        await using var stream = new TestInputStream(data) { CancellationSource = source, CancelAtPosition = data.Length - 300, MaxBytesPerRead = 64 };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Image.LoadAsync<Rgba32>(stream, cancellationToken: source.Token));
    }

    [Fact]
    public void LosslessImagesWithoutTheAlphaHintAreOpaque()
    {
        // A conforming encoder clears the hint only when every alpha is 255; the hint is authoritative otherwise
        var pixels = new[] { new Rgba32(1, 2, 3, 0), new Rgba32(4, 5, 6, 128) };
        using var source = Image.ImportPixelData<Rgba32>(pixels, 2, 1);
        var data = Encode(source);
        var hint = 20 + 4; // RIFF header, chunk header, signature, then 28 bits of size: the hint is bit 4 of byte 4
        Assert.Equal(0x10, data[hint] & 0x10);
        data[hint] &= 0xEF;

        var info = Image.Identify(data);
        Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);
        Assert.False(info.MayHaveTransparency);
        using var image = Image.Load<Rgba32>(data);
        Assert.Equal([new Rgba32(1, 2, 3, 255), new Rgba32(4, 5, 6, 255)], GetPixels(image.Frames[0]));
        using var untyped = Image.Load(data);
        Assert.Equal(PixelFormat.Rgb24, untyped.PixelFormat);
    }

    [Fact]
    public void SimpleFilesIgnoreChunksAfterTheImage()
    {
        using var source = Image.ImportPixelData<Rgba32>([new Rgba32(9, 8, 7, 255)], 1, 1);
        var still = Encode(source);
        var exif = Chunk("EXIF", [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00]);
        var data = still.Concat(exif).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)(data.Length - 8));

        using var image = Image.Load<Rgba32>(data);
        Assert.Equal([new Rgba32(9, 8, 7, 255)], GetPixels(image.Frames[0]));
        Assert.Null(image.Metadata.ExifProfile);
    }

    [Fact]
    public void AnimatedFilesAreNotPresentedAsStills()
    {
        var data = CreateLargeAnimation(frameCount: 3, width: 4, height: 4);
        var info = Image.Identify(data);
        Assert.True(info.IsAnimated);
        Assert.Null(info.FrameCount); // the header declares an animation, not its frame count
        var full = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal(3, full.FrameCount);
        Assert.Equal(PixelFormat.Rgba32, full.PixelFormat);
        Assert.Equal(ImageColorModel.Rgba, full.ColorModel);
    }

    private static byte[] Encode(Image image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, new WebPEncoder { Effort = 0 });
        return stream.ToArray();
    }

    /// <summary>The VP8L chunk payload of a still image encoded by the library (checked by the reference reader).</summary>
    private static byte[] Vp8L(Rgba32[] pixels, int width, int height)
    {
        using var image = Image.ImportPixelData<Rgba32>(pixels, width, height);
        var reader = ReferenceWebP.Parse(Encode(image));
        return Assert.Single(reader.Chunks, chunk => chunk.FourCC == "VP8L").Data.ToArray();
    }

    private static byte[] Animation(int width, int height, byte[] background, int loopCount, (int X, int Y, int Width, int Height, Rgba32[] Pixels, bool Blend, bool Dispose, int Duration)[] frames)
    {
        var chunks = new List<byte[]>
        {
            Chunk("VP8X", [0x12, 0, 0, 0, .. UInt24(width - 1), .. UInt24(height - 1)]),
            Chunk("ANIM", [.. background, (byte)loopCount, (byte)(loopCount >> 8)]),
        };
        foreach (var frame in frames)
        {
            var flags = (byte)((frame.Blend ? 0 : 0x02) | (frame.Dispose ? 0x01 : 0));
            chunks.Add(Chunk("ANMF", [.. UInt24(frame.X / 2), .. UInt24(frame.Y / 2), .. UInt24(frame.Width - 1), .. UInt24(frame.Height - 1), .. UInt24(frame.Duration), flags, .. Chunk("VP8L", Vp8L(frame.Pixels, frame.Width, frame.Height))]));
        }

        var body = chunks.SelectMany(chunk => chunk).ToArray();
        return [.. "RIFF"u8, .. BitConverter.GetBytes((uint)(body.Length + 4)), .. "WEBP"u8, .. body];
    }

    private static byte[] Chunk(string fourCC, byte[] payload)
        => [.. Encoding.ASCII.GetBytes(fourCC), .. BitConverter.GetBytes((uint)payload.Length), .. payload, .. (payload.Length % 2 == 1 ? new byte[] { 0 } : [])];

    private static byte[] UInt24(int value) => [(byte)value, (byte)(value >> 8), (byte)(value >> 16)];

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static byte[] CreateLargeAnimation(int frameCount, int width, int height)
    {
        var random = new Random(frameCount * 1000 + width);
        using var image = new Image<Rgba32>(width, height);
        for (var i = 0; i < frameCount; i++)
        {
            var pixels = new Rgba32[width * height];
            for (var p = 0; p < pixels.Length; p++)
            {
                pixels[p] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)(p % 3 == 0 ? 128 : 255));
            }

            using var frame = Image.ImportPixelData<Rgba32>(pixels, width, height);
            frame.Frames[0].Metadata.Duration = new FrameDuration(i + 1, 100);
            if (i == 0)
            {
                frame.Frames[0].CopyPixelDataTo(pixels);
                image.Frames[0].ProcessPixelRows(rows =>
                {
                    for (var y = 0; y < height; y++)
                    {
                        pixels.AsSpan(y * width, width).CopyTo(rows.GetRowSpan(y));
                    }
                });
                image.Frames[0].Metadata.Duration = frame.Frames[0].Metadata.Duration;
            }
            else
            {
                image.AppendFrame(frame.Frames[0]);
            }
        }

        using var stream = new MemoryStream();
        image.Save(stream, new WebPEncoder { Effort = 0 });
        return stream.ToArray();
    }

    private static Rgba32[] Fill(int width, int height, Rgba32 value) => Enumerable.Repeat(value, width * height).ToArray();

    private static TPixel[] GetPixels<TPixel>(ImageFrame<TPixel> frame)
        where TPixel : unmanaged
    {
        var pixels = new TPixel[frame.Width * frame.Height];
        frame.CopyPixelDataTo(pixels);
        return pixels;
    }
}
