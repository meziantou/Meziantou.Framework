using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Animated cursor (ANI) encoding through the public APIs. The written bytes are inspected with a minimal RIFF walk and a
/// hand-parsed cursor directory local to these tests, independently of the decoder, before any round trip: the patched
/// counts and sizes, the rate and sequence tables, the frames stored once, the hotspots, and everything that must be
/// rejected before the first byte is written.
/// </summary>
public sealed class AniEncoderTests
{
    private static readonly Rgba32[] PixelsA = [new Rgba32(10, 20, 30, 255), new Rgba32(40, 50, 60, 128), new Rgba32(70, 80, 90, 0), new Rgba32(1, 2, 3, 200)];
    private static readonly Rgba32[] PixelsB = [new Rgba32(200, 0, 0, 255), new Rgba32(0, 200, 0, 255), new Rgba32(0, 0, 200, 255), new Rgba32(9, 9, 9, 9)];

    [Fact]
    public void SettingsHaveDocumentedDefaults()
    {
        var encoder = new AniEncoder();
        Assert.Equal(ImageFormat.Ani, encoder.Format);
        Assert.Equal(IconPayloadFormat.Auto, encoder.PayloadFormat);
        Assert.Equal(FrameDurationRounding.RoundToNearest, encoder.DurationRounding);
        Assert.Equal(MetadataHandling.Strict, encoder.MetadataHandling);
        Assert.Equal(256, AniEncoder.MaxDimension);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AniEncoder { PayloadFormat = (IconPayloadFormat)42 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AniEncoder { DurationRounding = (FrameDurationRounding)42 });
    }

    [Fact]
    public void TheFileIsARiffContainerWhoseCountsAndSizesArePatched()
    {
        using var image = Animation([PixelsA, PixelsB], jiffies: [6, 6]);
        var data = Encode(image);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(data, 0, 4));
        Assert.Equal(data.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)));
        Assert.Equal("ACON", Encoding.ASCII.GetString(data, 8, 4));

        // Every step has the same rate and no frame is repeated: the header and the frame list are the whole file
        var chunks = Chunks(data.AsSpan(12));
        Assert.Equal(["anih", "LIST"], chunks.Select(chunk => chunk.Id));
        Assert.Equal([36u, 2u, 2u, 0u, 0u, 0u, 0u, 6u, 1u], UInt32s(chunks[0].Data));

        var list = chunks[1].Data;
        Assert.Equal("fram", Encoding.ASCII.GetString(list, 0, 4));
        var frames = Chunks(list.AsSpan(4));
        Assert.Equal(["icon", "icon"], frames.Select(chunk => chunk.Id));
        Assert.Equal(PixelsA, CursorPixels(frames[0].Data, 2, 2));
        Assert.Equal(PixelsB, CursorPixels(frames[1].Data, 2, 2));

        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(2, decoded.Frames.Count);
        Assert.Equal(PixelsA, Pixels(decoded, 0));
        Assert.Equal(PixelsB, Pixels(decoded, 1));
        Assert.Equal([new FrameDuration(1, 10), new FrameDuration(1, 10)], Durations(decoded));
        Assert.Null(decoded.Animation!.TotalPlays);
    }

    [Fact]
    public void DifferentDurationsAreWrittenAsARateChunkAfterTheFrames()
    {
        using var image = Animation([PixelsA, PixelsB, PixelsA], jiffies: [3, 0, 3]);
        image.Frames[2][0, 0] = new Rgba32(1, 1, 1, 255); // three distinct frames
        var chunks = Chunks(Encode(image).AsSpan(12));
        Assert.Equal(["anih", "LIST", "rate"], chunks.Select(chunk => chunk.Id));
        Assert.Equal([3u, 0u, 3u], UInt32s(chunks[2].Data));

        // The default rate of the header is the rate of the first step; the sequence flag is not set
        Assert.Equal([36u, 3u, 3u, 0u, 0u, 0u, 0u, 3u, 1u], UInt32s(chunks[0].Data));
    }

    [Theory]
    [InlineData(1, 10, 6u)] // 100 ms
    [InlineData(1, 30, 2u)]
    [InlineData(1, 25, 2u)] // 2.4 jiffies
    [InlineData(1, 24, 3u)] // 2.5 jiffies: ties up
    [InlineData(1, 1000, 0u)] // 0.06 jiffies
    [InlineData(0, 1, 0u)]
    [InlineData(71582788, 1, 4294967280u)] // close to the largest rate
    public void DurationsAreRoundedToTheNearestJiffy(long numerator, long denominator, uint expected)
    {
        using var image = Animation([PixelsA], jiffies: [0]);
        image.Frames[0].Metadata.Duration = new FrameDuration(numerator, denominator);
        var chunks = Chunks(Encode(image).AsSpan(12));
        Assert.Equal(expected, UInt32s(chunks[0].Data)[7]);
    }

    [Fact]
    public void UnrepresentableDurationsAreRejectedBeforeAnyOutput()
    {
        using var image = Animation([PixelsA, PixelsB], jiffies: [6, 6]);
        image.Frames[1].Metadata.Duration = new FrameDuration(1, 25);
        using var stream = new MemoryStream();
        var precision = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new AniEncoder { DurationRounding = FrameDurationRounding.RequireExact }));
        Assert.Equal("ANI frame duration precision", precision.Feature);
        Assert.Equal(0, stream.Length);

        // 2^32 jiffies do not fit the 32-bit rate, with either policy: never clamped
        image.Frames[1].Metadata.Duration = new FrameDuration(1L << 32, 60);
        var range = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new AniEncoder()));
        Assert.Equal("ANI frame duration range", range.Feature);
        Assert.Equal(0, stream.Length);

        image.Frames[1].Metadata.Duration = new FrameDuration(uint.MaxValue, 60);
        image.Save(stream, new AniEncoder { DurationRounding = FrameDurationRounding.RequireExact });
        Assert.Equal(uint.MaxValue, UInt32s(Chunks(stream.ToArray().AsSpan(12))[2].Data)[1]);
    }

    [Fact]
    public void IdenticalFramesAreStoredOnceAndReplayedThroughTheSequence()
    {
        using var image = Animation([PixelsA, PixelsB, PixelsA, PixelsA], jiffies: [1, 2, 3, 4]);
        var data = Encode(image);
        var chunks = Chunks(data.AsSpan(12));
        Assert.Equal(["anih", "LIST", "rate", "seq "], chunks.Select(chunk => chunk.Id));

        // Two stored frames, four steps, and the sequence flag
        Assert.Equal([36u, 2u, 4u, 0u, 0u, 0u, 0u, 1u, 3u], UInt32s(chunks[0].Data));
        Assert.Equal([1u, 2u, 3u, 4u], UInt32s(chunks[2].Data));
        Assert.Equal([0u, 1u, 0u, 0u], UInt32s(chunks[3].Data));
        var frames = Chunks(chunks[1].Data.AsSpan(4));
        Assert.HasCount(2, frames);
        Assert.Equal(PixelsA, CursorPixels(frames[0].Data, 2, 2));
        Assert.Equal(PixelsB, CursorPixels(frames[1].Data, 2, 2));

        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(4, decoded.Frames.Count);
        Assert.Equal(PixelsA, Pixels(decoded, 0));
        Assert.Equal(PixelsB, Pixels(decoded, 1));
        Assert.Equal(PixelsA, Pixels(decoded, 2));
        Assert.Equal(PixelsA, Pixels(decoded, 3));
        Assert.Equal([new FrameDuration(1, 60), new FrameDuration(2, 60), new FrameDuration(3, 60), new FrameDuration(4, 60)], Durations(decoded));
    }

    [Fact]
    public void TheSamePixelsWithAnotherHotspotAreAnotherFrame()
    {
        using var image = Animation([PixelsA, PixelsA, PixelsA], jiffies: [1, 1, 1]);
        image.Frames[1].Metadata.Hotspot = new Point(1, 1);
        var chunks = Chunks(Encode(image).AsSpan(12));
        Assert.Equal(["anih", "LIST", "seq "], chunks.Select(chunk => chunk.Id));
        Assert.Equal([0u, 1u, 0u], UInt32s(chunks[2].Data));
        Assert.HasCount(2, Chunks(chunks[1].Data.AsSpan(4)));
    }

    [Fact]
    public void EveryFrameIsACursorFileWithItsOwnHotspot()
    {
        using var image = Animation([PixelsA, PixelsB], jiffies: [1, 1]);
        image.Frames[1].Metadata.Hotspot = new Point(1, 0);
        var data = Encode(image);
        var frames = Chunks(Chunks(data.AsSpan(12))[1].Data.AsSpan(4));

        // ICONDIR: reserved, type 2 (cursor), one entry; then width, height, colors, reserved, hotspot X and Y
        Assert.Equal([0, 0, 2, 0, 1, 0, 2, 2, 0, 0, 0, 0, 0, 0], frames[0].Data[..14]);
        Assert.Equal([0, 0, 2, 0, 1, 0, 2, 2, 0, 0, 1, 0, 0, 0], frames[1].Data[..14]);

        // The payload fills the rest of its chunk, right after the 22-byte directory
        Assert.Equal(frames[0].Data.Length - 22, BinaryPrimitives.ReadInt32LittleEndian(frames[0].Data.AsSpan(14)));
        Assert.Equal(22, BinaryPrimitives.ReadInt32LittleEndian(frames[0].Data.AsSpan(18)));

        using var decoded = Image.Load(data);
        Assert.Equal([new Point(0, 0), new Point(1, 0)], Hotspots(decoded));

        // A cursor always has a hotspot, so it is written whatever the metadata policy is
        using var stripped = Image.Load(Encode(image, new AniEncoder { MetadataHandling = MetadataHandling.Strip }));
        Assert.Equal(new Point(1, 0), stripped.Frames[1].Metadata.Hotspot);
    }

    [Fact]
    public void AStillImageIsAnAnimationOfOneStep()
    {
        using var image = Image.ImportPixelData<Rgba32>(PixelsA, 2, 2);
        Assert.False(image.IsAnimated);
        var data = Encode(image);
        Assert.Equal([36u, 1u, 1u, 0u, 0u, 0u, 0u, 0u, 1u], UInt32s(Chunks(data.AsSpan(12))[0].Data));

        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(1, decoded.Frames.Count);
        Assert.True(decoded.IsAnimated);
        Assert.Equal(PixelsA, Pixels(decoded, 0));
    }

    [Fact]
    public void AFinitePlayCountAndAPosterAreRejectedBeforeAnyOutput()
    {
        using var image = Animation([PixelsA, PixelsB], jiffies: [1, 1]);
        image.Animation = new AnimationMetadata { TotalPlays = 3 };
        using var stream = new MemoryStream();
        var plays = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new AniEncoder()));
        Assert.Equal(ImageFormat.Ani, plays.Format);
        Assert.Equal("ANI play count", plays.Feature);
        Assert.Equal(0, stream.Length);
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(2, 2) { Encoder = new AniEncoder(), Animation = new AnimationMetadata { TotalPlays = 1 } }));

        image.Animation = new AnimationMetadata();
        image.SetPosterFrame(image.Frames[0]);
        var poster = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new AniEncoder()));
        Assert.Equal("Poster frame", poster.Feature);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void AFrameLargerThan256PixelsIsRejected()
    {
        using var image = TiffEncoderTests.CreateRgba(257, 16);
        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new AniEncoder()));
        Assert.Contains("256", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, stream.Length);

        using var largest = TiffEncoderTests.CreateRgba(256, 3);
        using var decoded = (Image<Rgba32>)Image.Load(Encode(largest));
        TiffEncoderTests.AssertSamePixels(largest, decoded);
    }

    [Theory]
    [InlineData(64, 64, IconPayloadFormat.Auto, false)]
    [InlineData(65, 8, IconPayloadFormat.Auto, true)]
    [InlineData(8, 65, IconPayloadFormat.Auto, true)]
    [InlineData(8, 8, IconPayloadFormat.Png, true)]
    [InlineData(128, 128, IconPayloadFormat.Dib, false)]
    public void ThePayloadOfAFrameIsADibOrAPng(int width, int height, IconPayloadFormat payloadFormat, bool expectPng)
    {
        using var image = TiffEncoderTests.CreateRgba(width, height);
        image.Frames[0].Metadata.Hotspot = new Point(width - 1, height - 1);
        var data = Encode(image, new AniEncoder { PayloadFormat = payloadFormat });
        var frame = Chunks(Chunks(data.AsSpan(12))[1].Data.AsSpan(4))[0].Data;
        var payload = frame.AsSpan(22);
        if (expectPng)
        {
            Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], payload[..8].ToArray());
        }
        else
        {
            // BITMAPINFOHEADER: 40 bytes, the width, the doubled height, one plane, 32 bits per pixel
            Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(payload));
            Assert.Equal(width, BinaryPrimitives.ReadInt32LittleEndian(payload[4..]));
            Assert.Equal(height * 2, BinaryPrimitives.ReadInt32LittleEndian(payload[8..]));
            Assert.Equal(32, BinaryPrimitives.ReadInt16LittleEndian(payload[14..]));
        }

        using var decoded = Image.Load<Rgba32>(data);
        TiffEncoderTests.AssertSamePixels(image, decoded);
        Assert.Equal(new Point(width - 1, height - 1), decoded.Frames[0].Metadata.Hotspot);
    }

    [Fact]
    public void SixteenBitPixelsAreWrittenLosslesslyOrRejected()
    {
        Rgba64[] pixels = [new Rgba64(1, 2, 3, 65535), new Rgba64(65535, 32768, 257, 4660)];
        using var image = Image.ImportPixelData<Rgba64>(pixels, 2, 1);

        // Auto never narrows: 16-bit samples go to a PNG payload, whatever the size
        var data = Encode(image);
        var frame = Chunks(Chunks(data.AsSpan(12))[1].Data.AsSpan(4))[0].Data;
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], frame[22..26]);
        using var decoded = Image.Load(data);
        var typed = Assert.IsType<Image<Rgba64>>(decoded);
        Assert.Equal(pixels, Pixels(typed, 0));

        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new AniEncoder { PayloadFormat = IconPayloadFormat.Dib }));
        Assert.Equal("Bit depth reduction", exception.Feature);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void GrayAndOpaquePixelFormatsAreWrittenThroughRgba()
    {
        Gray8[] gray = [new Gray8(0), new Gray8(128), new Gray8(255), new Gray8(7)];
        using var image = Image.ImportPixelData<Gray8>(gray, 2, 2);
        using var decoded = Image.Load<Rgba32>(Encode(image));
        Assert.Equal([new Rgba32(0, 0, 0, 255), new Rgba32(128, 128, 128, 255), new Rgba32(255, 255, 255, 255), new Rgba32(7, 7, 7, 255)], Pixels(decoded, 0));
    }

    [Fact]
    public async Task TheDestinationMustBeSeekable()
    {
        using var image = Animation([PixelsA, PixelsB], jiffies: [1, 1]);
        using var stream = new TestOutputStream();
        var exception = Assert.Throws<ArgumentException>(() => image.Save(stream, new AniEncoder()));
        Assert.Contains("seekable", exception.Message, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<ArgumentException>(async () => await image.SaveAsync(stream, new AniEncoder(), XunitCancellationToken));
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(2, 2) { Encoder = new AniEncoder(), ExpectedFrameCount = 1 }));
        Assert.Equal(0, stream.BytesWritten);
    }

    [Fact]
    public void PatchesAreRelativeToTheStartOfTheOutput()
    {
        using var image = Animation([PixelsA, PixelsB, PixelsA], jiffies: [1, 2, 3]);
        using var stream = new MemoryStream();
        stream.Write([1, 2, 3, 4, 5]);
        image.Save(stream, new AniEncoder());
        var written = stream.ToArray();
        Assert.Equal([1, 2, 3, 4, 5], written[..5]);
        Assert.Equal(Encode(image), written[5..]);
    }

    [Fact]
    public async Task AWriterWithoutAFrameCountWritesTheSameBytesAsAnEagerSave()
    {
        using var image = Animation([PixelsA, PixelsB, PixelsA], jiffies: [1, 2, 3]);
        image.Frames[1].Metadata.Hotspot = new Point(1, 1);
        var expected = Encode(image);

        using var stream = new MemoryStream();
        using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(2, 2) { Encoder = new AniEncoder(), LeaveOpen = true }))
        {
            foreach (var frame in image.Frames)
            {
                writer.WriteFrame(frame);
            }

            writer.Complete();
        }

        Assert.Equal(expected, stream.ToArray());

        using var asyncStream = new TestOutputStream { Seekable = true, AllowPatching = true, ForbidSynchronousWrites = true };
        await using (var writer = Image.CreateWriter<Rgba32>(asyncStream, new ImageWriterOptions(2, 2) { Encoder = new AniEncoder(), ExpectedFrameCount = 3 }))
        {
            foreach (var frame in image.Frames)
            {
                await writer.WriteFrameAsync(frame, XunitCancellationToken);
            }

            await writer.CompleteAsync(XunitCancellationToken);
        }

        Assert.Equal(expected, asyncStream.ToArray());
    }

    [Fact]
    public void MemoryDoesNotGrowWithTheNumberOfFrames()
    {
        // 400 distinct 64x64 frames are 6.5 MB of DIB payloads; the writer may keep 1 MB alive
        const int FrameCount = 400;
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 1024 * 1024 } };
        using var frame = new Image<Rgba32>(64, 64, new Rgba32(1, 2, 3, 255));
        using var stream = new MemoryStream();
        using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(64, 64) { Encoder = new AniEncoder(), Configuration = configuration, LeaveOpen = true }))
        {
            for (var i = 0; i < FrameCount; i++)
            {
                frame.Frames[0][i % 64, i / 64] = new Rgba32((byte)i, (byte)(i >> 8), 0, 255);
                frame.Frames[0].Metadata.Duration = new FrameDuration(i % 5, 60);
                writer.WriteFrame(frame.Frames[0]);
            }

            writer.Complete();
        }

        var chunks = Chunks(stream.ToArray().AsSpan(12));
        Assert.Equal(["anih", "LIST", "rate"], chunks.Select(chunk => chunk.Id));
        Assert.Equal((uint)FrameCount, UInt32s(chunks[0].Data)[1]);
        Assert.HasCount(FrameCount, Chunks(chunks[1].Data.AsSpan(4)));
        Assert.HasCount(FrameCount * 4, chunks[2].Data);
    }

    [Fact]
    public void TheTitleAndTheAuthorAreStoredInTheInfoList()
    {
        using var image = Animation([PixelsA], jiffies: [1]);
        image.Metadata.TextEntries.Add(new ImageTextEntry("Author", "Gérald"));
        image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "Busy"));
        var data = Encode(image);
        var chunks = Chunks(data.AsSpan(12));
        Assert.Equal(["LIST", "anih", "LIST"], chunks.Select(chunk => chunk.Id));
        Assert.Equal("INFO", Encoding.ASCII.GetString(chunks[0].Data, 0, 4));

        // NUL-terminated Latin-1 strings, in the order of the entries; an odd size is padded
        var info = Chunks(chunks[0].Data.AsSpan(4));
        Assert.Equal(["IART", "INAM"], info.Select(chunk => chunk.Id));
        Assert.Equal([(byte)'G', 0xE9, (byte)'r', (byte)'a', (byte)'l', (byte)'d', 0], info[0].Data);
        Assert.Equal([(byte)'B', (byte)'u', (byte)'s', (byte)'y', 0], info[1].Data);
        Assert.Equal(data.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)));

        using var decoded = Image.Load(data);
        Assert.Equal([new ImageTextEntry("Author", "Gérald"), new ImageTextEntry("Title", "Busy")], decoded.Metadata.TextEntries);
    }

    [Theory]
    [MemberData(nameof(UnsupportedMetadata))]
    public void MetadataTheFormatCannotStoreFollowsThePolicy(string feature, Action<ImageMetadata> configure)
    {
        using var image = Animation([PixelsA], jiffies: [1]);
        image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "kept"));
        configure(image.Metadata);
        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new AniEncoder()));
        Assert.Equal(ImageFormat.Ani, exception.Format);
        Assert.Equal("Metadata: " + feature, exception.Feature);
        Assert.Equal(0, stream.Length);

        using var discarded = Image.Load(Encode(image, new AniEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported }));
        Assert.Equal([new ImageTextEntry("Title", "kept")], discarded.Metadata.TextEntries);
        Assert.Null(discarded.Metadata.Resolution);

        var stripped = Encode(image, new AniEncoder { MetadataHandling = MetadataHandling.Strip });
        Assert.Equal(["anih", "LIST"], Chunks(stripped.AsSpan(12)).Select(chunk => chunk.Id));
    }

    public static TheoryData<string, Action<ImageMetadata>> UnsupportedMetadata() => new()
    {
        { "text entry 'Comment'", metadata => metadata.TextEntries.Add(new ImageTextEntry("Comment", "c")) },
        { "text entry 'Title'", metadata => metadata.TextEntries.Add(new ImageTextEntry("Title", "a second title")) },
        { "text entry 'Author'", metadata => metadata.TextEntries.Add(new ImageTextEntry("Author", "not Latin-1: €")) },
        { "text entry 'Author'", metadata => metadata.TextEntries.Add(new ImageTextEntry("Author", "a\0b")) },
        { "text entry 'Author'", metadata => metadata.TextEntries.Add(new ImageTextEntry("Author", "x", languageTag: "fr")) },
        { "resolution", metadata => metadata.Resolution = new ImageResolution(96, 96) },
        { "EXIF orientation other than TopLeft", metadata => metadata.Orientation = ExifOrientation.RightTop },
    };

    [Fact]
    public async Task EncodingIsDeterministicAndCancelable()
    {
        using var image = Animation([PixelsA, PixelsB, PixelsA], jiffies: [1, 2, 3]);
        Assert.Equal(Encode(image), Encode(image));

        using var stream = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.SaveAsync(stream, new AniEncoder(), new CancellationToken(canceled: true)));
    }

    private static Image<Rgba32> Animation(Rgba32[][] frames, int[] jiffies)
    {
        var image = Image.ImportPixelData<Rgba32>(frames[0], 2, 2);
        image.Frames[0].Metadata.Duration = new FrameDuration(jiffies[0], 60);
        for (var i = 1; i < frames.Length; i++)
        {
            var frame = image.AppendFrame();
            frame.Metadata.Duration = new FrameDuration(jiffies[i], 60);
            var pixels = frames[i];
            frame.ProcessPixelRows(pixels, static (accessor, source) =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    source.AsSpan(y * accessor.Width, accessor.Width).CopyTo(accessor.GetRowSpan(y));
                }
            });
        }

        return image;
    }

    private static byte[] Encode(Image image, AniEncoder? encoder = null)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder ?? new AniEncoder());
        return stream.ToArray();
    }

    /// <summary>Walks a list of RIFF chunks: a four-character code, a little-endian size, the data and a pad byte after an odd size.</summary>
    private static List<(string Id, byte[] Data)> Chunks(ReadOnlySpan<byte> data)
    {
        var chunks = new List<(string Id, byte[] Data)>();
        var position = 0;
        while (position < data.Length)
        {
            var size = BinaryPrimitives.ReadInt32LittleEndian(data[(position + 4)..]);
            chunks.Add((Encoding.ASCII.GetString(data.Slice(position, 4)), data.Slice(position + 8, size).ToArray()));
            position += 8 + size + (size & 1);
        }

        Assert.Equal(data.Length, position);
        return chunks;
    }

    private static uint[] UInt32s(ReadOnlySpan<byte> data)
    {
        var values = new uint[data.Length / 4];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt32LittleEndian(data[(i * 4)..]);
        }

        return values;
    }

    /// <summary>Reads the pixels of a one-entry cursor file whose payload is a 32-bit DIB: bottom-up BGRA rows after the 40-byte header.</summary>
    private static Rgba32[] CursorPixels(ReadOnlySpan<byte> cursor, int width, int height)
    {
        var rows = cursor[(22 + 40)..];
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = rows[((height - 1 - y) * width * 4)..];
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = new Rgba32(row[(x * 4) + 2], row[(x * 4) + 1], row[x * 4], row[(x * 4) + 3]);
            }
        }

        return pixels;
    }

    private static FrameDuration[] Durations(Image image) => [.. image.Frames.Select(frame => frame.Metadata.Duration)];

    private static Point?[] Hotspots(Image image) => [.. image.Frames.Select(frame => frame.Metadata.Hotspot)];

    private static TPixel[] Pixels<TPixel>(Image<TPixel> image, int frame)
        where TPixel : unmanaged
    {
        var pixels = new TPixel[image.Width * image.Height];
        image.Frames[frame].CopyPixelDataTo(pixels);
        return pixels;
    }
}
