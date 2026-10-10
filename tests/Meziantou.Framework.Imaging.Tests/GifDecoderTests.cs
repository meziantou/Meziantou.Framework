using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// GIF decoding on synthetic inputs assembled by
/// <see cref="GifTestBuilder"/> with an independent test LZW encoder: hand-computed displayed frames for transparency, every
/// disposal (including the undefined values), partial and clipped rectangles and palette changes; LZW clear/end codes, code
/// growth, deferred clears and malformed streams; loop and timing conventions; compositor independence from caller edits;
/// faults, frame-limit prefixes, budgets, bounded sequential memory and cancellation. The corpus conformance tests compare the
/// same decoder with the externally cross-checked fixtures.
/// </summary>
public sealed class GifDecoderTests
{
    private static readonly Rgba32 R = new(255, 0, 0, 255);
    private static readonly Rgba32 G = new(0, 255, 0, 255);
    private static readonly Rgba32 B = new(0, 0, 255, 255);
    private static readonly Rgba32 W = new(255, 255, 255, 255);
    private static readonly Rgba32 K = new(0, 0, 0, 255);
    private static readonly Rgba32 Y = new(255, 255, 0, 255);
    private static readonly Rgba32 M = new(255, 0, 255, 255);
    private static readonly Rgba32 C = new(0, 255, 255, 255);
    private static readonly Rgba32 O = new(255, 128, 0, 255);
    private static readonly Rgba32 T = new(0, 0, 0, 0);

    /// <summary>Global palette: index 7 is the transparent index of the compositing animation (its color is never shown).</summary>
    private static readonly Rgba32[] Palette = [R, G, B, W, K, Y, M, new(1, 2, 3, 255)];

    /// <summary>The displayed frames of <see cref="CreateCompositingAnimation"/>, written by hand from the image instructions.</summary>
    private static readonly Rgba32[][] CompositingFrames =
    [
        [R, G, B, W, K, Y, M, R, G, B, W, K],
        [R, G, R, W, K, W, M, R, G, B, W, K], // transparent pixels keep G and M; then disposal 2 clears the 2x2 rectangle
        [R, T, T, W, K, T, C, O, G, B, O, C], // local palette, clipped at the right edge; then disposal 3 restores the rectangle
        [R, T, T, W, K, T, T, R, W, B, W, K], // no Graphic Control Extension: zero duration, no disposal
        [R, T, T, W, K, T, T, R, W, B, W, R], // full-canvas transparent image; the undefined disposal 4 restores the previous canvas
        [R, T, T, M, K, T, T, R, W, B, W, K], // the last disposal (2) has no visible effect
    ];

    private static readonly FrameDuration[] CompositingDurations = [new(1, 10), FrameDuration.Zero, new(7, 100), FrameDuration.Zero, new(1, 50), new(65535, 100)];

    public static TheoryData<InputVariant> Variants => [.. InputVariants.All];

    public static TheoryData<InputVariant> ReaderVariants => [.. InputVariants.ReaderVariants];

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task EagerLoadsCompositeTransparencyDisposalAndPalettes(InputVariant variant)
    {
        using var image = await InputVariants.LoadAsync<Rgba32>(variant, CreateCompositingAnimation(), ImageFormat.Gif, options: null, XunitCancellationToken);
        Assert.Equal(CompositingFrames.Length, image.Frames.Count);
        Assert.Null(image.PosterFrame);
        Assert.NotNull(image.Animation);
        Assert.Null(image.Animation.TotalPlays);
        for (var i = 0; i < CompositingFrames.Length; i++)
        {
            Assert.Equal(CompositingFrames[i], GetPixels(image.Frames[i]));
            Assert.Equal(CompositingDurations[i], image.Frames[i].Metadata.Duration);
        }

        using var untyped = Image.Load(CreateCompositingAnimation());
        Assert.Equal(PixelFormat.Rgba32, untyped.PixelFormat);
        Assert.Equal(ImageFormat.Gif, untyped.Metadata.SourceFormat);
        using var wide = Image.Load<Rgba64>(CreateCompositingAnimation());
        var pixels = new Rgba64[12];
        wide.Frames[2].CopyPixelDataTo(pixels);
        Assert.Equal(CompositingFrames[2].Select(p => new Rgba64((ushort)(p.R * 257), (ushort)(p.G * 257), (ushort)(p.B * 257), (ushort)(p.A * 257))), pixels);
    }

    [Theory]
    [MemberData(nameof(ReaderVariants))]
    public async Task ReadersProduceTheSameFramesAsEagerLoads(InputVariant variant)
    {
        await InputVariants.ReadAsync<Rgba32, int>(variant, CreateCompositingAnimation(), ImageFormat.Gif, options: null, async (reader, asynchronous) =>
        {
            Assert.Null(reader.Info.FrameCount); // never guessed from a GIF header
            Assert.True(reader.Info.IsAnimated); // a loop extension precedes the first image
            Assert.Null(reader.Info.Animation?.TotalPlays);
            Assert.Null(asynchronous ? await reader.ReadPosterFrameAsync(XunitCancellationToken) : reader.ReadPosterFrame());
            for (var i = 0; i < CompositingFrames.Length; i++)
            {
                using var frame = asynchronous ? await reader.ReadFrameAsync(XunitCancellationToken) : reader.ReadFrame();
                Assert.NotNull(frame);
                Assert.Single(frame.Frames);
                Assert.Null(frame.Animation);
                Assert.Equal(CompositingFrames[i], GetPixels(frame.Frames[0]));
                Assert.Equal(CompositingDurations[i], frame.Frames[0].Metadata.Duration);
            }

            Assert.Null(asynchronous ? await reader.ReadFrameAsync(XunitCancellationToken) : reader.ReadFrame());
            return 0;
        }, XunitCancellationToken);
    }

    [Fact]
    public void CallerEditsOfReturnedFramesNeverReachTheCompositor()
    {
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(CreateCompositingAnimation())))
        {
            for (var i = 0; i < CompositingFrames.Length; i++)
            {
                using var frame = reader.ReadFrame();
                Assert.NotNull(frame);
                Assert.Equal(CompositingFrames[i], GetPixels(frame.Frames[0]));
                Fill(frame.Frames[0], new Rgba32(1, 2, 3, 4));
            }
        }

        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(CreateCompositingAnimation())))
        {
            using var destination = new Image<Rgba32>(4, 3);
            var storage = destination.Frames[0];
            for (var i = 0; i < CompositingFrames.Length; i++)
            {
                Fill(destination.Frames[0], new Rgba32(200, 100, 50, 25));
                destination.Frames[0].Metadata.Duration = new FrameDuration(123, 1);
                Assert.True(reader.ReadFrameInto(destination));
                Assert.Same(storage, destination.Frames[0]);
                Assert.Equal(CompositingFrames[i], GetPixels(destination.Frames[0]));
                Assert.Equal(CompositingDurations[i], destination.Frames[0].Metadata.Duration);
            }

            Fill(destination.Frames[0], W);
            Assert.False(reader.ReadFrameInto(destination));
            Assert.All(GetPixels(destination.Frames[0]), pixel => Assert.Equal(W, pixel));
        }

        // Eager frames are independent copies too
        using var image = Image.Load<Rgba32>(CreateCompositingAnimation());
        Fill(image.Frames[0], W);
        Assert.Equal(CompositingFrames[1], GetPixels(image.Frames[1]));
    }

    [Theory]
    [InlineData(0, false, false, (int)AnimationDisposal.None)]
    [InlineData(1, false, false, (int)AnimationDisposal.None)]
    [InlineData(2, false, false, (int)AnimationDisposal.ClearToTransparent)]
    [InlineData(2, true, false, (int)AnimationDisposal.ClearToTransparent)]
    [InlineData(3, false, false, (int)AnimationDisposal.RestorePrevious)]
    [InlineData(3, true, false, (int)AnimationDisposal.ClearToTransparent)] // the initial canvas is transparent black
    [InlineData(4, false, false, (int)AnimationDisposal.RestorePrevious)]   // undefined: Chromium, Firefox and ImageIO restore
    [InlineData(5, false, false, (int)AnimationDisposal.None)]
    [InlineData(6, false, false, (int)AnimationDisposal.None)]
    [InlineData(7, false, false, (int)AnimationDisposal.None)]
    [InlineData(2, false, true, (int)AnimationDisposal.None)]               // nothing is displayed after the last frame
    [InlineData(3, false, true, (int)AnimationDisposal.None)]
    public void DisposalMethodsMapToCompositorDisposals(int method, bool isFirstFrame, bool isLastFrame, int expected)
    {
        Assert.Equal((AnimationDisposal)expected, GifDecoder.GetDisposal(method, isFirstFrame, isLastFrame));
        Assert.Equal(method is 4 ? 3 : method is > 4 ? 0 : method, GifGraphicControl.GetEffectiveDisposalMethod(method));
    }

    [Fact]
    public void BackgroundColorIndexIsNeverPainted()
    {
        // The background index (2, blue) and the palette color of the transparent index are never shown: the initial canvas
        // and "restore to background" are transparent black, with or without a transparent index
        var data = new GifTestBuilder(3, 1, Palette, backgroundIndex: 2)
            .GraphicControl(disposal: 2, delay: 1).Image(1, 0, 1, 1, [0], minimumCodeSize: 3)
            .GraphicControl(disposal: 3, delay: 1).Image(0, 0, 1, 1, [1], minimumCodeSize: 3)
            .GraphicControl(disposal: 0, delay: 1, transparentIndex: 7).Image(0, 0, 3, 1, [7, 3, 7], minimumCodeSize: 3)
            .ToArray();
        using var image = Image.Load<Rgba32>(data);
        Assert.Equal([T, R, T], GetPixels(image.Frames[0]));
        Assert.Equal([G, T, T], GetPixels(image.Frames[1]));
        Assert.Equal([T, W, T], GetPixels(image.Frames[2]));
        Assert.Equal(1, image.Animation!.TotalPlays); // several images without loop extension: played once
    }

    [Fact]
    public void RectanglesAreClippedToTheLogicalScreen()
    {
        var data = new GifTestBuilder(2, 2, Palette)
            .Image(0, 0, 3, 3, [0, 1, 2, 3, 4, 5, 6, 0, 1], minimumCodeSize: 3)          // larger than the screen
            .GraphicControl(disposal: 2, delay: 0).Image(1, 1, 2, 2, [2, 2, 2, 2], minimumCodeSize: 3) // partly outside
            .GraphicControl(disposal: 3, delay: 0).Image(9, 9, 1, 1, [1], minimumCodeSize: 3)       // entirely outside
            .Image(0, 0, 1, 1, [6], minimumCodeSize: 3)
            .ToArray();
        using var image = Image.Load<Rgba32>(data);
        Assert.Equal(4, image.Frames.Count);
        Assert.Equal([R, G, W, K], GetPixels(image.Frames[0]));
        Assert.Equal([R, G, W, B], GetPixels(image.Frames[1]));
        Assert.Equal([R, G, W, T], GetPixels(image.Frames[2])); // displayed frame of an image outside the screen: the canvas
        Assert.Equal([M, G, W, T], GetPixels(image.Frames[3]));
    }

    [Fact]
    public void InterlacedRowsArePlacedByPass()
    {
        for (var height = 1; height <= 20; height++)
        {
            int pass = 0, passRow = 0;
            var rows = Enumerable.Range(0, height).Select(_ => GifDecoder.GetInterlacedRow(height, ref pass, ref passRow)).ToArray();
            Assert.Equal(GifTestBuilder.InterlacedRowOrder(height), rows);
            Assert.Equal(Enumerable.Range(0, height), rows.Order());
        }

        // Each row has its own color: any misplaced row is visible
        foreach (var height in new[] { 1, 2, 3, 4, 5, 8, 9, 17 })
        {
            var indices = Enumerable.Range(0, height * 3).Select(i => (byte)(i / 3 % 7)).ToArray();
            var data = new GifTestBuilder(3, height, Palette).Image(0, 0, 3, height, indices, minimumCodeSize: 3, interlaced: true).ToArray();
            using var image = Image.Load<Rgba32>(data);
            Assert.Equal(indices.Select(i => Palette[i]), GetPixels(image.Frames[0]));
        }
    }

    public static TheoryData<int, int, string> LzwCases()
    {
        var data = new TheoryData<int, int, string>();
        foreach (var minimumCodeSize in new[] { 1, 2, 3, 4, 7, 8 })
        {
            foreach (var mode in new[] { "default", "no-initial-clear", "double-clear", "deferred-clear", "clear-every-5", "clear-every-254", "no-end-code", "codes-after-end", "byte-sub-blocks", "runs" })
            {
                data.Add(minimumCodeSize, minimumCodeSize == 1 ? 2 : 1 << Math.Min(minimumCodeSize, 8), mode);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LzwCases))]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void LzwStreamsDecodeToTheEncodedIndices(int minimumCodeSize, int colors, string mode)
    {
        // 160x120 random indices fill the 4096-entry table several times at every minimum code size
        const int Width = 160;
        const int Height = 120;
        var random = new Random(minimumCodeSize * 31 + mode.Length);
        var indices = new byte[Width * Height];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = mode == "runs" && i % 997 < 500 ? (byte)(i / 997 % colors) : (byte)random.Next(colors);
        }

        var options = mode switch
        {
            "no-initial-clear" => new GifLzwTestOptions { LeadingClearCodes = 0 },
            "double-clear" => new GifLzwTestOptions { LeadingClearCodes = 2, ClearInterval = 100 },
            "deferred-clear" => new GifLzwTestOptions { ClearWhenFull = false },
            "clear-every-5" => new GifLzwTestOptions { ClearInterval = 5 },
            "clear-every-254" => new GifLzwTestOptions { ClearInterval = 254 },
            "no-end-code" => new GifLzwTestOptions { EndCode = false },
            "codes-after-end" => new GifLzwTestOptions { CodesAfterEnd = [0, 1, 0, 1] },
            _ => new GifLzwTestOptions(),
        };

        var palette = Enumerable.Range(0, colors).Select(i => new Rgba32((byte)i, (byte)(255 - i), (byte)(i * 7), 255)).ToArray();
        var data = new GifTestBuilder(Width, Height, palette).Image(0, 0, Width, Height, indices, minimumCodeSize, lzw: options, subBlockSize: mode == "byte-sub-blocks" ? 1 : 255).ToArray();
        using var image = Image.Load<Rgba32>(data);
        Assert.Equal(indices.Select(i => palette[i]), GetPixels(image.Frames[0]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4096)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void LzwDecoderResumesStringsAcrossOutputBuffersAndInputPieces(int outputChunk)
    {
        // Long KwKwK strings (constant runs) cross output buffers; the input arrives one byte at a time
        var random = new Random(outputChunk);
        var indices = Enumerable.Range(0, 20000).Select(i => i < 9000 ? (byte)0 : (byte)random.Next(4)).ToArray();
        var encoded = GifLzwTestEncoder.Encode(indices, 2, new GifLzwTestOptions { ClearWhenFull = false });
        using var decoder = new GifLzwDecoder(new AllocationScope(new ImageResourceLimits()));
        decoder.Reset(2);
        var output = new byte[indices.Length];
        var written = 0;
        for (var i = 0; i < encoded.Length; i++)
        {
            ReadOnlySpan<byte> piece = encoded.AsSpan(i, 1);
            while (written < output.Length)
            {
                var count = decoder.Decode(ref piece, output.AsSpan(written, Math.Min(outputChunk, output.Length - written)));
                written += count;
                if (count == 0)
                    break;
            }

            if (written == output.Length)
            {
                // The rest of the stream (from the first unconsumed byte) holds only the end code
                ReadOnlySpan<byte> rest = encoded.AsSpan(i + 1 - piece.Length);
                decoder.ReadTrailer(ref rest);
                break;
            }
        }

        Assert.Equal(indices, output);
        Assert.True(decoder.IsEnded);
    }

    public static TheoryData<string, int, int, (int Code, int Size)[]> MalformedLzwStreams => new()
    {
        // Minimum code size 2: clear 4, end 5, first table code 6, 3-bit codes
        { "code above the next table code", 2, 1, [(4, 3), (0, 3), (7, 3), (5, 3)] },
        { "table code right after a clear code", 2, 1, [(4, 3), (6, 3), (5, 3)] },
        { "end code before the last pixel", 2, 2, [(4, 3), (0, 3), (1, 3), (5, 3)] },
        { "end of data before the last pixel", 10, 1, [(4, 3), (0, 3), (1, 3)] },
        { "more indices than pixels", 2, 1, [(4, 3), (0, 3), (1, 3), (2, 3), (5, 3)] },
        { "string longer than the remaining pixels", 2, 1, [(4, 3), (0, 3), (6, 3), (5, 3)] },
        { "data code after the last pixel then more data", 2, 1, [(4, 3), (0, 3), (1, 3), (0, 3), (0, 3), (0, 3), (1, 3)] },
        { "index outside the color table", 2, 1, [(4, 3), (0, 3), (3, 3), (5, 3)] },
    };

    [Theory]
    [MemberData(nameof(MalformedLzwStreams))]
    public void MalformedLzwStreamsAreInvalidAndFaultReaders(string description, int width, int height, (int Code, int Size)[] codes)
    {
        var palette = new[] { R, G };
        var data = new GifTestBuilder(width, height, palette).Loop(0).Image(0, 0, width, height, new byte[width * height], minimumCodeSize: 2).ImageCodes(0, 0, width, height, 2, codes).ToArray();
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        Assert.Equal(ImageFormat.Gif, exception.Format);

        // Full scans validate the container only: the datastream is never decoded
        Assert.Equal(2, Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).FrameCount);

        // Readers return the valid first frame, then fault: never a clean end
        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(data));
        using (var first = reader.ReadFrame())
        {
            Assert.NotNull(first);
        }

        Assert.Throws<InvalidImageContentException>(() => reader.ReadFrame());
        Assert.Throws<InvalidOperationException>(() => reader.ReadFrame());
        Assert.Equal(0, reader.Core.Scope.GetLiveBytes(AllocationKind.CompositorState));
        Assert.NotEmpty(description);
    }

    [Fact]
    public void EndCodesPaddingAndTrailingDataAreTolerated()
    {
        var palette = new[] { R, G };

        // No end code: the zero bits padding the last byte form a data code, which is padding when nothing follows
        var missingEnd = new GifTestBuilder(2, 1, palette).ImageCodes(0, 0, 2, 1, 2, (4, 3), (0, 3), (1, 3)).ToArray();
        using (var image = Image.Load<Rgba32>(missingEnd))
        {
            Assert.Equal([R, G], GetPixels(image.Frames[0]));
        }

        // Data after the end code (same byte and later sub-blocks) is ignored; clear codes may precede the end code
        var trailing = new GifTestBuilder(2, 1, palette).ImageCodes(0, 0, 2, 1, 2, (4, 3), (0, 3), (1, 3), (4, 3), (4, 3), (5, 3), (7, 3), (7, 3), (2, 3)).ToArray();
        using (var image = Image.Load<Rgba32>(trailing))
        {
            Assert.Equal([R, G], GetPixels(image.Frames[0]));
        }

        // An empty image rectangle: only a clear and an end code
        var empty = new GifTestBuilder(2, 1, palette).Image(0, 0, 2, 1, [1, 0], 2).Image(1, 0, 0, 0, [], 2).ToArray();
        using (var image = Image.Load<Rgba32>(empty))
        {
            Assert.Equal([G, R], GetPixels(image.Frames[1]));
        }

        // The transparent index needs no color table entry
        var transparent = new GifTestBuilder(2, 1, palette).GraphicControl(0, 0, transparentIndex: 3).ImageCodes(0, 0, 2, 1, 2, (4, 3), (3, 3), (1, 3), (5, 3)).ToArray();
        using (var image = Image.Load<Rgba32>(transparent))
        {
            Assert.Equal([T, G], GetPixels(image.Frames[0]));
        }

        // An image without any color table is invalid
        var noTable = new GifTestBuilder(2, 1).Image(0, 0, 2, 1, [0, 1], 2).ToArray();
        Assert.Throws<InvalidImageContentException>(() => Image.Load(noTable));
    }

    [Fact]
    public void LoopExtensionsNormalizeToTotalPlays()
    {
        static byte[] Animation(Action<GifTestBuilder> before, Action<GifTestBuilder>? between = null, int images = 2)
        {
            var builder = new GifTestBuilder(1, 1, Palette);
            before(builder);
            for (var i = 0; i < images; i++)
            {
                builder.GraphicControl(1, 3).Image(0, 0, 1, 1, [(byte)i], 3);
                if (i == 0)
                {
                    between?.Invoke(builder);
                }
            }

            return builder.ToArray();
        }

        // NETSCAPE2.0 stores repetitions: 0 = infinite, L = L + 1 plays; no extension = 1 play (Apple ImageIO reports the same)
        Assert.Null(Load(Animation(b => b.Loop(0))).TotalPlays);
        Assert.Equal(2, Load(Animation(b => b.Loop(1))).TotalPlays);
        Assert.Equal(65536, Load(Animation(b => b.Loop(65535))).TotalPlays);
        Assert.Equal(1, Load(Animation(_ => { })).TotalPlays);
        Assert.Equal(5, Load(Animation(b => b.Loop(4, "ANIMEXTS1.0"))).TotalPlays);

        // A single image with a loop extension is a one-frame animation; without one it is a still image
        Assert.Equal(4, Load(Animation(b => b.Loop(3), images: 1)).TotalPlays);
        using (var still = Image.Load(Animation(_ => { }, images: 1)))
        {
            Assert.Null(still.Animation);
            Assert.False(still.IsAnimated);
        }

        // The last loop extension wins (FFmpeg, ImageIO), also after the first image; readers only know the header
        var late = Animation(b => b.Loop(2), b => b.Loop(6));
        Assert.Equal(7, Load(late).TotalPlays);
        Assert.Equal(3, Image.Identify(late).Animation?.TotalPlays);
        Assert.Equal(7, Image.Identify(late, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).Animation?.TotalPlays);
        var onlyLate = Animation(_ => { }, b => b.Loop(6));
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(onlyLate)))
        {
            Assert.Null(reader.Info.IsAnimated);
            Assert.Null(reader.Info.Animation);
        }

        Assert.Equal(7, Load(onlyLate).TotalPlays);

        // Other NETSCAPE2.0 sub-blocks (buffering, id 2) are not loop counts
        using (var buffering = Image.Load(Animation(b => b.Application("NETSCAPE2.0", [2, 0, 1, 0, 0]), images: 1)))
        {
            Assert.Null(buffering.Animation);
        }

        static AnimationMetadata Load(byte[] data)
        {
            using var image = Image.Load(data);
            return image.Animation ?? throw new InvalidOperationException("Not animated.");
        }
    }

    [Fact]
    public void DurationsAreExactHundredths()
    {
        var data = new GifTestBuilder(1, 1, Palette)
            .GraphicControl(1, 0).Image(0, 0, 1, 1, [0], 3)
            .GraphicControl(1, 1).Image(0, 0, 1, 1, [1], 3)
            .GraphicControl(1, 7).Image(0, 0, 1, 1, [2], 3)
            .GraphicControl(1, 65535).Image(0, 0, 1, 1, [3], 3)
            .Image(0, 0, 1, 1, [4], 3)
            .ToArray();
        using var image = Image.Load(data);
        Assert.Equal([FrameDuration.Zero, new(1, 100), new(7, 100), new(65535, 100), FrameDuration.Zero], image.Frames.Select(f => f.Metadata.Duration));
        Assert.Equal(TimeSpan.FromMilliseconds(70), image.Frames[2].Metadata.Duration.ToTimeSpan()); // no minimum-delay heuristic
    }

    [Fact]
    public void ExtensionsAreSkippedOrRejectedAndCommentsBecomeMetadata()
    {
        var data = new GifTestBuilder(2, 1, Palette)
            .Comment("before")
            .Extension(0x99, [1, 2, 3], [4])
            .Application("XMP DataXMP", [(byte)'<', (byte)'x', (byte)'/', (byte)'>'])
            .Image(0, 0, 2, 1, [0, 1], 3)
            .Comment("between")
            .Extension(0x42)
            .Image(0, 0, 1, 1, [2], 3)
            .Comment("after")
            .ToArray();
        using (var image = Image.Load<Rgba32>(data))
        {
            Assert.Equal(["before", "between", "after"], image.Metadata.TextEntries.Select(entry => entry.Value));
            Assert.All(image.Metadata.TextEntries, entry => Assert.Equal(ImageTextEntry.CommentKeyword, entry.Keyword));
            Assert.Equal([B, G], GetPixels(image.Frames[1]));
        }

        // Readers carry the header metadata (comments before the first image)
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(data)))
        {
            using var frame = reader.ReadFrame();
            Assert.Equal(["before"], frame!.Metadata.TextEntries.Select(entry => entry.Value));
        }

        // A plain-text extension needs text rendering: unsupported, also after the first image (readers fault there)
        var plainText = new GifTestBuilder(2, 1, Palette).Image(0, 0, 2, 1, [0, 1], 3).Extension(0x01, new byte[12], [(byte)'h', (byte)'i']).Image(0, 0, 2, 1, [1, 0], 3).ToArray();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(plainText));
        Assert.Equal("GIF plain text extension", exception.Feature);
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(plainText)))
        {
            using (var first = reader.ReadFrame())
            {
                Assert.NotNull(first);
            }

            Assert.Throws<UnsupportedImageFeatureException>(() => reader.ReadFrame());
            Assert.Throws<InvalidOperationException>(() => reader.ReadFrame());
        }
    }

    [Fact]
    public void TruncationIsNeverACleanEnd()
    {
        var data = CreateCompositingAnimation();
        foreach (var length in new[] { data.Length - 1, data.Length - 3, data.Length / 2, 40 })
        {
            var truncated = data[..length];
            Assert.Throws<InvalidImageContentException>(() => Image.Load(truncated));
            Assert.Throws<InvalidImageContentException>(() => ReadAll(truncated));
        }

        static void ReadAll(byte[] data)
        {
            using var reader = Image.OpenReader<Rgba32>(new MemoryStream(data));
            while (reader.ReadFrame() is { } frame)
            {
                frame.Dispose();
            }

            Assert.Fail("The truncated input ended cleanly.");
        }
    }

    [Fact]
    public void FrameLimitsSelectAPrefixWithoutExaminingTheRest()
    {
        // The input stops in the middle of the third image: a two-frame prefix never reads it
        var full = CreateCompositingAnimation();
        var builder = CreateCompositingBuilder(images: 2);
        var twoImages = builder.ToArray(trailer: false).Length;
        var prefix = full[..(twoImages + 12)];
        using (var image = Image.Load<Rgba32>(prefix, new ImageDecodeOptions { FrameLimit = 2 }))
        {
            Assert.Equal(2, image.Frames.Count);
            Assert.Equal(CompositingFrames[1], GetPixels(image.Frames[1]));
        }

        Assert.Throws<InvalidImageContentException>(() => Image.Load(prefix, new ImageDecodeOptions { FrameLimit = 3 }));
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(prefix), new ImageReaderOptions { FrameLimit = 2 }))
        {
            using (var first = reader.ReadFrame())
            using (var second = reader.ReadFrame())
            {
                Assert.Equal(CompositingFrames[1], GetPixels(second!.Frames[0]));
            }

            Assert.Null(reader.ReadFrame());
        }

        // The last frame of a prefix keeps no restore-previous state (its disposal has no visible effect)
        using (var image = Image.Load<Rgba32>(full, new ImageDecodeOptions { FrameLimit = 3 }))
        {
            Assert.Equal(CompositingFrames[2], GetPixels(image.Frames[2]));
        }
    }

    [Fact]
    public void LimitsChargeFullCanvasFramesBeforeDecoding()
    {
        var data = CreateCompositingAnimation();
        var frames = Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Limits(new ImageResourceLimits { MaxFrames = 5 })));
        Assert.Equal(ImageResourceLimitKind.Frames, frames.Kind);

        // 6 frames x 12 canvas pixels, whatever the encoded rectangles
        using (Image.Load(data, Limits(new ImageResourceLimits { MaxTotalPixels = 72 })))
        {
        }

        var pixels = Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Limits(new ImageResourceLimits { MaxTotalPixels = 71 })));
        Assert.Equal(ImageResourceLimitKind.TotalPixels, pixels.Kind);
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Limits(new ImageResourceLimits { MaxWidth = 3 }))).Kind);

        static ImageDecodeOptions Limits(ImageResourceLimits limits) => new() { Configuration = new ImageConfiguration { Limits = limits } };
    }

    [Fact]
    public void DecoderStateIsBudgetedAndReleased()
    {
        var data = CreateLargeAnimation(frameCount: 12);
        using (var image = Image.Load<Rgba32>(data))
        {
            Assert.Equal(12, image.Frames.Count);
            var scope = image.Owner.Scope;
            Assert.Equal(0, scope.GetLiveBytes(AllocationKind.CompositorState));
            Assert.Equal(0, scope.GetLiveBytes(AllocationKind.RestorePreviousState));
            Assert.Equal(0, scope.GetLiveBytes(AllocationKind.DecoderState));
            var peak = scope.GetDiagnostics().PeakLiveBytes;
            using (Image.Load<Rgba32>(data, Options(peak)))
            {
            }

            var exception = Assert.Throws<ImageResourceLimitException>(() => Image.Load<Rgba32>(data, Options(peak - 1)));
            Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
        }

        static ImageDecodeOptions Options(long limit) => new() { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = limit } } };
    }

    [Fact]
    public void SequentialMemoryStaysBoundedWhenFramesAreDisposed()
    {
        // 60 frames: the reader's live allocations never exceed the first frame's level (one canvas, at most one
        // restore-previous rectangle, the LZW table, rows, the input buffer and the caller's single live frame)
        var data = CreateLargeAnimation(frameCount: 60);
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

            Assert.True(scope.GetLiveBytes(AllocationKind.RestorePreviousState) <= scope.GetChargedSize(48L * 32 * 4));
        }

        Assert.Equal(60, count);
        Assert.InRange(peakAfterFirst, 0, firstLevel!.Value + scope.GetChargedSize(48L * 32 * 4));
        Assert.Equal(0, scope.GetLiveBytes(AllocationKind.CompositorState)); // released at the clean end
    }

    [Fact]
    public async Task CancellationFaultsWithoutPartialResults()
    {
        var data = CreateLargeAnimation(frameCount: 12);
        var position = data.Length - 300;
        using (var source = new CancellationTokenSource())
        {
            await using var stream = new TestInputStream(data) { CancellationSource = source, CancelAtPosition = position, MaxBytesPerRead = 64 };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Image.LoadAsync<Rgba32>(stream, cancellationToken: source.Token));
        }

        using (var source = new CancellationTokenSource())
        {
            var stream = new TestInputStream(data) { CancellationSource = source, CancelAtPosition = position, MaxBytesPerRead = 64 };
            await using var reader = await Image.OpenReaderAsync<Rgba32>(stream, cancellationToken: XunitCancellationToken);
            var read = 0;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                while (true)
                {
                    using var frame = await reader.ReadFrameAsync(source.Token);
                    Assert.NotNull(frame);
                    read++;
                }
            });

            Assert.InRange(read, 1, 11);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadFrameAsync(XunitCancellationToken));
            Assert.Equal(0, reader.Core.Scope.GetLiveBytes(AllocationKind.CompositorState));
        }
    }

    [Fact]
    public void TypedLoadsApplyTheAlphaPolicy()
    {
        // Opaque frames convert to formats without alpha exactly; transparent pixels are never dropped silently
        var opaque = new GifTestBuilder(2, 1, Palette).Image(0, 0, 2, 1, [3, 4], 3).ToArray();
        using (var gray = Image.Load<Gray8>(opaque))
        {
            Assert.Equal([(byte)255, (byte)0], GetPixels(gray.Frames[0]).Select(p => p.Value));
        }

        var transparent = CreateCompositingAnimation();
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load<Rgb24>(transparent));
        using var flattened = Image.Load<Rgb24>(transparent, new ImageDecodeOptions { Conversion = new PixelConversionOptions { BackgroundColor = new Rgba32(10, 20, 30, 255) } });
        Assert.Equal(new Rgb24(10, 20, 30), flattened.Frames[2][1, 0]);
        Assert.Equal(new Rgb24(0, 255, 255), flattened.Frames[2][2, 1]);
    }

    /// <summary>
    /// A 4x3 animation (loop 0): full image; 2x2 transparent-index image with disposal 2; 3x2 local-palette image clipped at
    /// the right edge with disposal 3; a 1x1 image without Graphic Control Extension; a full transparent image with the
    /// undefined disposal 4; a last 1x1 image with disposal 2.
    /// </summary>
    private static byte[] CreateCompositingAnimation() => CreateCompositingBuilder(images: 6).ToArray();

    private static GifTestBuilder CreateCompositingBuilder(int images)
    {
        var builder = new GifTestBuilder(4, 3, Palette, backgroundIndex: 2).Loop(0);
        var steps = new Action<GifTestBuilder>[]
        {
            b => b.GraphicControl(1, 10).Image(0, 0, 4, 3, [0, 1, 2, 3, 4, 5, 6, 0, 1, 2, 3, 4], 3),
            b => b.GraphicControl(2, 0, transparentIndex: 7).Image(1, 0, 2, 2, [7, 0, 3, 7], 3),
            b => b.GraphicControl(3, 7).Image(2, 1, 3, 2, [0, 1, 0, 1, 0, 1], 2, localPalette: [C, O]),
            b => b.Image(0, 2, 1, 1, [3], 3),
            b => b.GraphicControl(4, 2, transparentIndex: 7).Image(0, 0, 4, 3, [7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 0], 3),
            b => b.GraphicControl(2, 65535).Image(3, 0, 1, 1, [6], 3),
        };

        foreach (var step in steps.Take(images))
        {
            step(builder);
        }

        return builder;
    }

    /// <summary>A 48x32 animation of random partial images with every disposal and transparency (8-bit LZW, 4 sub-block sizes).</summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static byte[] CreateLargeAnimation(int frameCount)
    {
        var random = new Random(16);
        var palette = Enumerable.Range(0, 256).Select(i => new Rgba32((byte)i, (byte)(i * 3), (byte)(i * 7), 255)).ToArray();
        var builder = new GifTestBuilder(48, 32, palette).Loop(0);
        builder.Image(0, 0, 48, 32, Enumerable.Range(0, 48 * 32).Select(_ => (byte)random.Next(256)).ToArray());
        for (var i = 1; i < frameCount; i++)
        {
            var x = random.Next(40);
            var y = random.Next(24);
            var width = random.Next(1, 48 - x + 4);
            var height = random.Next(1, 32 - y + 4);
            var indices = Enumerable.Range(0, width * height).Select(_ => (byte)random.Next(256)).ToArray();
            builder.GraphicControl(i % 4, (ushort)i, transparentIndex: i % 2 == 0 ? 17 : null).Image(x, y, width, height, indices, interlaced: i % 3 == 0, subBlockSize: 1 + (i % 4 * 80));
        }

        return builder.ToArray();
    }

    private static TPixel[] GetPixels<TPixel>(ImageFrame<TPixel> frame)
        where TPixel : unmanaged
    {
        var pixels = new TPixel[frame.Width * frame.Height];
        frame.CopyPixelDataTo(pixels);
        return pixels;
    }

    private static void Fill(ImageFrame<Rgba32> frame, Rgba32 value)
    {
        frame.ProcessPixelRows(value, static (accessor, color) =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                accessor.GetRowSpan(y).Fill(color);
            }
        });
    }
}
