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
    public void ATruncatedLossyImageIsShownAsFarAsItWasDecodedThenReported()
    {
        // Prefixes that end in the bitstream of a still lossy image: the rendering has the macroblock rows whose data was
        // received, as the complete file decodes them (but the last rows of the last one, which the loop filter and the
        // chroma upsampling of the next one change), the other rows being zero; the next read reports the truncation
        var pixels = new Rgba32[24 * 160];
        for (var i = 0; i < pixels.Length; i++)
        {
            var noise = (uint)i * 2654435761u;
            pixels[i] = new Rgba32((byte)(noise >> 24), (byte)(i / 24), (byte)(noise >> 16), 255);
        }

        using var source = Image.ImportPixelData<Rgba32>(pixels, 24, 160);
        using var stream = new MemoryStream();
        source.Save(stream, new WebPEncoder { Compression = WebPCompression.Lossy, Quality = 90 });
        var data = stream.ToArray();
        using var complete = Image.Load<Rgba32>(data);
        var expected = new Rgba32[24 * 160];
        complete.Frames[0].CopyPixelDataTo(expected);
        var partials = 0;
        for (var length = 40; length < data.Length - 1; length += 5)
        {
            using var reader = Image.OpenReader<Rgba32>(new MemoryStream(data[..length]));
            using var destination = new Image<Rgba32>(24, 160, new Rgba32(1, 2, 3, 4));
            FrameRefinement? refinement;
            try
            {
                refinement = reader.ReadFrameRefinementInto(destination);
            }
            catch (InvalidImageContentException)
            {
                continue;
            }

            Assert.NotNull(refinement);
            Assert.False(refinement.IsFinal);
            var actual = new Rgba32[24 * 160];
            destination.Frames[0].CopyPixelDataTo(actual);
            var rows = Enumerable.Range(0, 160).TakeWhile(y => actual.AsSpan(y * 24, 24).IndexOfAnyExcept(Transparent) >= 0).Count();
            Assert.True(rows % 16 == 0 && rows > 0, $"length {length}: {rows} rows");
            Assert.True(actual.AsSpan(rows * 24).IndexOfAnyExcept(Transparent) < 0, $"length {length}");
            var exact = rows == 160 ? 160 : rows - 4;
            Assert.True(actual.AsSpan(0, exact * 24).SequenceEqual(expected.AsSpan(0, exact * 24)), $"length {length}");
            partials++;

            Assert.Throws<InvalidImageContentException>(() => reader.ReadFrameRefinementInto(destination));
            Assert.Throws<InvalidOperationException>(() => reader.ReadFrameRefinementInto(destination));
        }

        Assert.True(partials > 20, $"{partials} of {data.Length} bytes");
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
    public void EagerLossyLoadsConvertRowsAsTheyAreFinalLikeReaders()
    {
        // An eager load of a lossy image without alpha decodes it through a window of macroblock rows that moves every
        // Vp8Decoder.WindowMacroblockRows rows; a reader decodes the whole frame first: both give the same pixels, for
        // heights around the window and the moves, in RGB and RGBA
        foreach (var (width, height) in new[] { (37, 150), (37, 152), (21, 153), (40, 167), (33, 280), (19, 409), (16, 16) })
        {
            var pixels = new Rgba32[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                var noise = (uint)i * 2654435761u;
                pixels[i] = new Rgba32((byte)(noise >> 24), (byte)((i / width) + (i % width)), (byte)((noise >> 16) & 0x3F), 255);
            }

            using var source = Image.ImportPixelData<Rgba32>(pixels, width, height);
            using var stream = new MemoryStream();
            source.Save(stream, new WebPEncoder { Compression = WebPCompression.Lossy, Quality = 60 });
            var data = stream.ToArray();
            using (var eager = Image.Load<Rgb24>(data))
            using (var reader = Image.OpenReader<Rgb24>(new MemoryStream(data)))
            using (var read = reader.ReadFrame())
            {
                Assert.NotNull(read);
                Assert.Equal(GetBytes(read), GetBytes(eager), $"{width}x{height} RGB");
            }

            using (var eager = Image.Load<Rgba32>(data))
            using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(data)))
            using (var read = reader.ReadFrame())
            {
                Assert.NotNull(read);
                Assert.Equal(GetBytes(read), GetBytes(eager), $"{width}x{height} RGBA");
            }
        }

        static byte[] GetBytes(Image image)
        {
            var bytes = new byte[image.Width * image.Height * 4];
            image.Frames[0].CopyPixelBytesTo(bytes);
            return bytes;
        }
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

    public static TheoryData<string> MetadataChunkTypes => ["ICCP", "EXIF", "XMP "];

    [Theory]
    [MemberData(nameof(MetadataChunkTypes))]
    public async Task MetadataChunksLargerThanTheMetadataBudgetFailBeforeTheyAreBuffered(string fourCC)
    {
        // A 1 MB chunk with a 4 KiB budget fails from its header: the stream is not read past the first buffer
        var data = ExtendedStill(Chunk(fourCC, new byte[1_000_000]));
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxMetadataBytes = 4096 } };
        var identify = new ImageIdentifyOptions { Configuration = configuration };
        var load = new ImageDecodeOptions { Configuration = configuration };

        AssertMetadataLimit(4096, 1_000_000, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, identify)));
        AssertMetadataLimit(4096, 1_000_000, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, load)));

        await using (var stream = new TestInputStream(data) { Seekable = false, ForbidAsynchronousReads = true })
        {
            AssertMetadataLimit(4096, 1_000_000, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(stream, identify)));
            Assert.True(stream.BytesRead < 64 * 1024, $"{stream.BytesRead} bytes were read.");
        }

        await using (var stream = new TestInputStream(data) { Seekable = false, ForbidSynchronousReads = true })
        {
            AssertMetadataLimit(4096, 1_000_000, await Assert.ThrowsAsync<ImageResourceLimitException>(() => Image.LoadAsync(stream, load, XunitCancellationToken)));
            Assert.True(stream.BytesRead < 64 * 1024, $"{stream.BytesRead} bytes were read.");
        }

        // The payload is checked before it is read: a declared size fails even when the input is truncated
        byte[] declared = [.. "RIFF"u8, .. BitConverter.GetBytes(uint.MaxValue - 8), .. "WEBP"u8, .. Chunk("VP8X", new byte[10]), .. Encoding.ASCII.GetBytes(fourCC), .. BitConverter.GetBytes(1u << 30)];
        AssertMetadataLimit(ImageResourceLimits.DefaultMaxMetadataBytes, 1 << 30, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(declared)));
    }

    [Fact]
    public void MetadataChunksAreBoundedByWhatIsLeftOfTheBudgetAndRepeatedChunksAreNotBuffered()
    {
        // The XMP packet is checked against what the profile left (4096 - 3000), before it is buffered
        var icc = Chunk("ICCP", new byte[3000]);
        var data = ExtendedStill(icc, Chunk("XMP ", new byte[2000]));
        var options = new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxMetadataBytes = 4096 } } };
        AssertMetadataLimit(4096, 5000, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, options)));

        // A second EXIF chunk is not adopted: it is streamed, never buffered, so it does not need the budget
        byte[] exif = [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var repeated = ExtendedStill(Chunk("EXIF", exif), Chunk("EXIF", new byte[1_000_000]));
        var metadata = Image.Identify(repeated, new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxMetadataBytes = exif.Length } } }).Metadata;
        Assert.Equal(exif, metadata.ExifProfile?.Data.ToArray());
    }

    [Fact]
    public void OnlyTheDefinedBytesOfTheAnimChunkAreBuffered()
    {
        // 1 MB after the 6 defined bytes of ANIM: buffering the chunk would exceed the 256 KiB live allocation limit
        var data = Animation(1, 1, background: [1, 2, 3, 4], loopCount: 3, [(0, 0, 1, 1, new[] { new Rgba32(1, 2, 3, 255) }, Blend: false, Dispose: false, Duration: 10)], animationTrailingBytes: 1_000_000);
        var options = new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan, Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 256 * 1024 } } };

        using var stream = new TestInputStream(data) { Seekable = false };
        var info = Image.Identify(stream, options);
        Assert.Equal(1, info.FrameCount);
        Assert.Equal(3, info.Animation?.TotalPlays);
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

    [Fact]
    public void SparseMetaPrefixCodesBuildTablesOnlyForTheUsedGroups()
    {
        // The entropy image uses the meta codes 65535 and 3 only: the 65,534 other groups are read, but their tables are
        // not built (all the groups take 2.6 MB of handles and 1.3 MB of tables)
        var data = SparseMetaPrefixCodeStream(invalidUnusedGroup: false);
        var scope = new AllocationScope(new ImageResourceLimits());
        using (var pixels = Vp8LDecoder.DecodeImageStream(scope, data, 0, data.Length, width: 8, height: 1, XunitCancellationToken))
        {
            Assert.Equal(
                [0x80AABBCCu, 0x80AABBCCu, 0x80AABBCCu, 0x80AABBCCu, 0xFF112233u, 0xFF112233u, 0xFF112233u, 0xFF112233u],
                Enumerable.Range(0, 8).Select(i => BitConverter.ToUInt32(pixels.Memory.Span.Slice(i * sizeof(uint), sizeof(uint)))));
        }

        Assert.InRange(scope.GetDiagnostics().PeakLiveBytes, 0, 1024 * 1024);
    }

    [Fact]
    public void UnusedMetaPrefixCodeGroupsAreValidated()
    {
        var data = SparseMetaPrefixCodeStream(invalidUnusedGroup: true);
        var scope = new AllocationScope(new ImageResourceLimits());
        var exception = Assert.Throws<InvalidImageContentException>(() => Vp8LDecoder.DecodeImageStream(scope, data, 0, data.Length, width: 8, height: 1, XunitCancellationToken).Dispose());
        Assert.Contains("not complete", exception.Message);
    }

    [Fact]
    public void ColorCacheCodesInsertTheirPixelIntoTheCache()
    {
        // Pixels: literal V (cached in slot 0), cache key 0 (V), cache key 1 (never written: 0, which belongs to slot 0),
        // cache key 0 (0 since the previous pixel was inserted, as libwebp does)
        const uint V = 0xFF110033u;
        Assert.Equal(0u, Vp8LDecoder.ColorCacheHash(V, 1));
        var data = ColorCacheStream();
        uint[] expected = [V, V, 0u, 0u];
        Assert.Equal(expected, ReferenceVp8L.DecodeImageStream(data, width: 4, height: 1));

        var scope = new AllocationScope(new ImageResourceLimits());
        using var pixels = Vp8LDecoder.DecodeImageStream(scope, data, 0, data.Length, width: 4, height: 1, XunitCancellationToken);
        Assert.Equal(expected, Enumerable.Range(0, 4).Select(i => BitConverter.ToUInt32(pixels.Memory.Span.Slice(i * sizeof(uint), sizeof(uint)))));
    }

    /// <summary>
    /// A headerless 4x1 VP8L image stream with a 2-entry color cache: the literal <c>0xFF110033</c>, then the cache keys 0,
    /// 1 and 0. Green is a normal code (symbol 0: <c>0</c>, cache key 0: <c>10</c>, cache key 1: <c>11</c>); red, blue,
    /// alpha and distance are single-symbol codes.
    /// </summary>
    private static byte[] ColorCacheStream()
    {
        using var writer = new LsbBitWriter(new AllocationScope(new ImageResourceLimits()));
        writer.WriteBits(0, 1); // no transform
        writer.WriteBits(1, 1); // color cache
        writer.WriteBits(1, 4); // 1 bit: 2 entries
        writer.WriteBits(0, 1); // no meta prefix codes

        // Green (282 symbols): the code-length code gives the lengths 1, 2, 17 and 18 two bits each (00, 01, 10, 11)
        writer.WriteBits(0, 1);
        writer.WriteBits(1, 4); // 5 code-length code lengths, for the symbols 17, 18, 0, 1 and 2
        writer.WriteBits(2, 3);
        writer.WriteBits(2, 3);
        writer.WriteBits(0, 3);
        writer.WriteBits(2, 3);
        writer.WriteBits(2, 3);
        writer.WriteBits(0, 1); // no maximum symbol count
        WriteCode(writer, "00"); // symbol 0: length 1
        WriteCode(writer, "11"); // symbols 1-138: zero (18, 11 + 127)
        writer.WriteBits(127, 7);
        WriteCode(writer, "11"); // symbols 139-276: zero
        writer.WriteBits(127, 7);
        WriteCode(writer, "10"); // symbols 277-279: zero (17, 3 + 0)
        writer.WriteBits(0, 3);
        WriteCode(writer, "01"); // symbol 280 (cache key 0): length 2
        WriteCode(writer, "01"); // symbol 281 (cache key 1): length 2

        WriteSimpleCode(writer, 0x11); // red
        WriteSimpleCode(writer, 0x33); // blue
        WriteSimpleCode(writer, 0xFF); // alpha
        WriteSimpleCode(writer, 0); // distance

        WriteCode(writer, "0"); // literal
        WriteCode(writer, "10"); // cache key 0
        WriteCode(writer, "11"); // cache key 1
        WriteCode(writer, "10"); // cache key 0
        writer.WriteBits(0, 32);
        writer.Finish();
        return writer.WrittenSpan.ToArray();

        static void WriteCode(LsbBitWriter writer, string bits)
        {
            foreach (var bit in bits)
            {
                writer.WriteBits(bit == '1' ? 1u : 0u, 1);
            }
        }

        static void WriteSimpleCode(LsbBitWriter writer, int symbol)
        {
            writer.WriteBits(1, 1);
            writer.WriteBits(0, 1);
            writer.WriteBits(1, 1); // 8-bit symbol
            writer.WriteBits((uint)symbol, 8);
        }
    }

    /// <summary>
    /// A headerless 8x1 VP8L image stream with a 2x1 entropy image (blocks of 4 pixels) holding the meta codes 65535 and 3:
    /// group 65535 is <c>0x80AABBCC</c>, group 3 <c>0xFF112233</c>, and the other groups are single-symbol codes, or, for
    /// group 0, an incomplete distance code.
    /// </summary>
    private static byte[] SparseMetaPrefixCodeStream(bool invalidUnusedGroup)
    {
        using var writer = new LsbBitWriter(new AllocationScope(new ImageResourceLimits()));
        writer.WriteBits(0, 1); // no transform
        writer.WriteBits(0, 1); // no color cache
        writer.WriteBits(1, 1); // meta prefix codes
        writer.WriteBits(0, 3); // 4x4 blocks

        // Entropy image: no color cache; green {3, 255} and red {0, 255} are one-bit codes (the smaller symbol is bit 0)
        writer.WriteBits(0, 1);
        WriteSimpleCode(writer, 3, 255);
        WriteSimpleCode(writer, 0, 255);
        WriteSimpleCode(writer, 0);
        WriteSimpleCode(writer, 0);
        WriteSimpleCode(writer, 0);
        writer.WriteBits(1, 1); // pixel 0: green 255
        writer.WriteBits(1, 1); // pixel 0: red 255
        writer.WriteBits(0, 1); // pixel 1: green 3
        writer.WriteBits(0, 1); // pixel 1: red 0

        for (var group = 0; group <= 0xFFFF; group++)
        {
            var (alpha, red, green, blue) = group switch
            {
                0xFFFF => (0x80, 0xAA, 0xBB, 0xCC),
                3 => (0xFF, 0x11, 0x22, 0x33),
                _ => (0, 0, 0, 0),
            };
            WriteSimpleCode(writer, green);
            WriteSimpleCode(writer, red);
            WriteSimpleCode(writer, blue);
            WriteSimpleCode(writer, alpha);
            if (invalidUnusedGroup && group == 0)
            {
                // Normal code: the code-length code gives the lengths 0 and 1 one bit each, then the 40 distance symbols
                // all get the length 1 (40 one-bit codes do not make a complete tree)
                writer.WriteBits(0, 1);
                writer.WriteBits(0, 4); // 4 code-length code lengths, for the symbols 17, 18, 0 and 1
                writer.WriteBits(0, 3);
                writer.WriteBits(0, 3);
                writer.WriteBits(1, 3);
                writer.WriteBits(1, 3);
                writer.WriteBits(0, 1); // no maximum symbol count
                for (var symbol = 0; symbol < 40; symbol++)
                {
                    writer.WriteBits(1, 1);
                }
            }
            else
            {
                WriteSimpleCode(writer, 0);
            }
        }

        // The 8 pixels use single-symbol codes only: they take no bit
        writer.WriteBits(0, 32);
        writer.Finish();
        return writer.WrittenSpan.ToArray();

        static void WriteSimpleCode(LsbBitWriter writer, params ReadOnlySpan<int> symbols)
        {
            writer.WriteBits(1, 1);
            writer.WriteBits((uint)symbols.Length - 1, 1);
            writer.WriteBits(1, 1); // 8-bit first symbol
            foreach (var symbol in symbols)
            {
                writer.WriteBits((uint)symbol, 8);
            }
        }
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

    private static byte[] Animation(int width, int height, byte[] background, int loopCount, (int X, int Y, int Width, int Height, Rgba32[] Pixels, bool Blend, bool Dispose, int Duration)[] frames, int animationTrailingBytes = 0)
    {
        var chunks = new List<byte[]>
        {
            Chunk("VP8X", [0x12, 0, 0, 0, .. UInt24(width - 1), .. UInt24(height - 1)]),
            Chunk("ANIM", [.. background, (byte)loopCount, (byte)(loopCount >> 8), .. new byte[animationTrailingBytes]]),
        };
        foreach (var frame in frames)
        {
            var flags = (byte)((frame.Blend ? 0 : 0x02) | (frame.Dispose ? 0x01 : 0));
            chunks.Add(Chunk("ANMF", [.. UInt24(frame.X / 2), .. UInt24(frame.Y / 2), .. UInt24(frame.Width - 1), .. UInt24(frame.Height - 1), .. UInt24(frame.Duration), flags, .. Chunk("VP8L", Vp8L(frame.Pixels, frame.Width, frame.Height))]));
        }

        var body = chunks.SelectMany(chunk => chunk).ToArray();
        return [.. "RIFF"u8, .. BitConverter.GetBytes((uint)(body.Length + 4)), .. "WEBP"u8, .. body];
    }

    /// <summary>An extended 1x1 still image (VP8X, the given chunks, VP8L); the VP8X flags declare the metadata chunks.</summary>
    private static byte[] ExtendedStill(params byte[][] chunks)
    {
        var flags = 0;
        foreach (var chunk in chunks)
        {
            flags |= Encoding.ASCII.GetString(chunk, 0, 4) switch
            {
                "ICCP" => WebPStructureParser.IccFlag,
                "EXIF" => WebPStructureParser.ExifFlag,
                "XMP " => WebPStructureParser.XmpFlag,
                _ => 0,
            };
        }

        byte[] body = [.. Chunk("VP8X", [(byte)flags, 0, 0, 0, .. UInt24(0), .. UInt24(0)]), .. chunks.SelectMany(chunk => chunk), .. Chunk("VP8L", Vp8L([new Rgba32(1, 2, 3, 255)], 1, 1))];
        return [.. "RIFF"u8, .. BitConverter.GetBytes((uint)(body.Length + 4)), .. "WEBP"u8, .. body];
    }

    private static void AssertMetadataLimit(long limit, long requested, ImageResourceLimitException exception)
    {
        Assert.Equal(ImageResourceLimitKind.MetadataBytes, exception.Kind);
        Assert.Equal(limit, exception.Limit);
        Assert.Equal(requested, exception.Requested);
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
