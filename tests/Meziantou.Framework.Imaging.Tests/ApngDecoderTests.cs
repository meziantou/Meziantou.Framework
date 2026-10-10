using System.Numerics;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// APNG decoding on synthetic inputs assembled by
/// <see cref="ApngTestBuilder"/>: SOURCE/OVER blending and every disposal with hand-computed displayed frames, the OVER
/// rounding contract (8 and 16 bits) against an independent rational reference, poster layouts, exact timing, compositor
/// independence from caller edits (<c>ReadFrame</c>, <c>ReadFrameInto</c>), control-data errors that fault readers,
/// frame-limit prefixes, budgets, cancellation and decoder-state accounting. The corpus conformance tests compare the same
/// decoder with externally encoded APNGs.
/// </summary>
public sealed class ApngDecoderTests
{
    private static readonly Rgba32 R = new(255, 0, 0, 255);
    private static readonly Rgba32 G = new(0, 255, 0, 255);
    private static readonly Rgba32 B = new(0, 0, 255, 255);
    private static readonly Rgba32 W = new(255, 255, 255, 255);
    private static readonly Rgba32 T = new(0, 0, 0, 0);
    private static readonly Rgba32 H = new(0, 0, 255, 128); // translucent blue
    private static readonly Rgba32 X = new(9, 8, 7, 0);     // transparent with a hidden color

    // Hand-computed OVER results (A = 128 * 255 + 255 * 127 = 65025, so every composite over an opaque pixel is opaque)
    private static readonly Rgba32 HOverG = new(0, 127, 128, 255); // G: 255 * 255 * 127 / 65025 = 127, B: 255 * 128 * 255 / 65025 = 128
    private static readonly Rgba32 HOverR = new(127, 0, 128, 255);
    private static readonly Rgba32 HOverW = new(127, 127, 255, 255); // B: (255 * 128 * 255 + 255 * 255 * 127) / 65025 = 255

    /// <summary>The displayed frames of <see cref="CreateCompositingAnimation"/>, written by hand from the frame instructions.</summary>
    private static readonly Rgba32[][] CompositingFrames =
    [
        [R, G, B, W, H, X],
        [R, HOverG, B, W, H, X],        // OVER: the transparent X keeps the canvas pixel B; then BACKGROUND clears (1,0) and (2,0)
        [R, G, X, W, R, T],             // SOURCE replaces (hidden color kept); then PREVIOUS restores R T T / W H X
        [HOverR, T, T, HOverW, H, X],   // OVER on the restored canvas; the last frame's PREVIOUS has no visible effect
    ];

    private static readonly FrameDuration[] CompositingDurations = [new(1, 10), FrameDuration.Zero, new(7, 100), new(65535, 1)];

    public static TheoryData<InputVariant> Variants => [.. InputVariants.All];

    public static TheoryData<InputVariant> ReaderVariants => [.. InputVariants.ReaderVariants];

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task EagerLoadsCompositeEveryBlendAndDisposal(InputVariant variant)
    {
        using var image = await InputVariants.LoadAsync<Rgba32>(variant, CreateCompositingAnimation(), ImageFormat.Png, options: null, XunitCancellationToken);
        Assert.Equal(4, image.Frames.Count);
        Assert.Null(image.PosterFrame);
        Assert.Null(image.Animation?.TotalPlays);
        Assert.NotNull(image.Animation);
        for (var i = 0; i < CompositingFrames.Length; i++)
        {
            Assert.Equal(CompositingFrames[i], GetPixels(image.Frames[i]));
            Assert.Equal(CompositingDurations[i], image.Frames[i].Metadata.Duration);
        }

        // The untyped load uses the APNG working representation; 16-bit typed loads expand exactly (x257)
        using var untyped = Image.Load(CreateCompositingAnimation());
        Assert.Equal(PixelFormat.Rgba32, untyped.PixelFormat);
        using var wide = Image.Load<Rgba64>(CreateCompositingAnimation());
        var pixels = new Rgba64[6];
        wide.Frames[3].CopyPixelDataTo(pixels);
        Assert.Equal(CompositingFrames[3].Select(p => new Rgba64((ushort)(p.R * 257), (ushort)(p.G * 257), (ushort)(p.B * 257), (ushort)(p.A * 257))), pixels);
    }

    [Theory]
    [MemberData(nameof(ReaderVariants))]
    public async Task ReadersProduceTheSameFramesAsEagerLoads(InputVariant variant)
    {
        var data = CreateCompositingAnimation();
        await InputVariants.ReadAsync<Rgba32, int>(variant, data, ImageFormat.Png, options: null, async (reader, asynchronous) =>
        {
            Assert.Equal(4, reader.Info.FrameCount);
            Assert.False(reader.Info.HasPosterFrame);
            Assert.True(reader.Info.IsAnimated);
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
        // ReadFrame: every returned image is overwritten and disposed before the next read
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

        // ReadFrameInto: one destination reused and scribbled on between reads; its duration is refreshed every time
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(CreateCompositingAnimation())))
        {
            using var destination = new Image<Rgba32>(3, 2);
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
            Assert.All(GetPixels(destination.Frames[0]), pixel => Assert.Equal(W, pixel)); // clean end: untouched
        }

        // Interleaving both kinds of reads on the same reader
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(CreateCompositingAnimation())))
        {
            using var destination = new Image<Rgba32>(3, 2, W);
            Assert.True(reader.ReadFrameInto(destination));
            Fill(destination.Frames[0], T);
            using var second = reader.ReadFrame();
            Assert.Equal(CompositingFrames[1], GetPixels(second!.Frames[0]));
            Fill(second.Frames[0], W);
            Assert.True(reader.ReadFrameInto(destination));
            Assert.Equal(CompositingFrames[2], GetPixels(destination.Frames[0]));
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void OverBlendingFollowsTheExactRoundingContract()
    {
        // Literal values worked by hand
        AssertOver8(new Rgba32(255, 0, 0, 128), new Rgba32(0, 0, 255, 255), new Rgba32(128, 0, 127, 255));
        AssertOver8(new Rgba32(0, 255, 0, 100), new Rgba32(255, 0, 0, 50), new Rgba32(59, 196, 0, 130));
        AssertOver8(new Rgba32(0, 255, 0, 100), new Rgba32(0, 255, 0, 100), new Rgba32(0, 255, 0, 161)); // 41000 / 255 = 160.78
        AssertOver8(new Rgba32(7, 8, 9, 0), new Rgba32(1, 2, 3, 4), new Rgba32(1, 2, 3, 4));             // transparent source: canvas kept
        AssertOver8(new Rgba32(7, 8, 9, 0), new Rgba32(1, 2, 3, 0), new Rgba32(1, 2, 3, 0));             // hidden canvas color kept
        AssertOver8(new Rgba32(7, 8, 9, 1), new Rgba32(0, 0, 0, 0), new Rgba32(7, 8, 9, 1));             // over transparent black: copied
        AssertOver16([65535, 0, 0, 32768], [0, 0, 65535, 65535], [32768, 0, 32767, 65535]);
        AssertOver16([1, 2, 3, 0], [4, 5, 6, 0], [4, 5, 6, 0]);

        // Every alpha pair (8 bits) and seeded 16-bit samples against an independent rational reference
        var random = new Random(1234);
        Span<byte> source = stackalloc byte[4];
        Span<byte> destination = stackalloc byte[4];
        for (var sa = 0; sa <= 255; sa++)
        {
            for (var da = 0; da <= 255; da++)
            {
                random.NextBytes(source[..3]);
                random.NextBytes(destination[..3]);
                if ((sa + da) % 7 == 0)
                {
                    source[..3].Fill((byte)(sa % 2 == 0 ? 0 : 255)); // extremes
                }

                source[3] = (byte)sa;
                destination[3] = (byte)da;
                var expected = ReferenceOver([source[0], source[1], source[2], source[3]], [destination[0], destination[1], destination[2], destination[3]], 255);
                AnimationCompositor.BlendOver8(source, destination);
                Assert.Equal(expected, new ushort[] { destination[0], destination[1], destination[2], destination[3] });
            }
        }

        Span<ushort> source16 = stackalloc ushort[4];
        Span<ushort> destination16 = stackalloc ushort[4];
        ushort[] edges = [0, 1, 2, 127, 128, 255, 256, 32767, 32768, 65533, 65534, 65535];
        for (var i = 0; i < 200_000; i++)
        {
            for (var c = 0; c < 4; c++)
            {
                source16[c] = i % 3 == 0 ? edges[random.Next(edges.Length)] : (ushort)random.Next(65536);
                destination16[c] = i % 5 == 0 ? edges[random.Next(edges.Length)] : (ushort)random.Next(65536);
            }

            var expected = ReferenceOver([source16[0], source16[1], source16[2], source16[3]], [destination16[0], destination16[1], destination16[2], destination16[3]], 65535);
            AnimationCompositor.BlendOver16(source16, destination16);
            Assert.Equal(expected, destination16.ToArray());
        }

        static void AssertOver8(Rgba32 source, Rgba32 canvas, Rgba32 expected)
        {
            Span<byte> s = [source.R, source.G, source.B, source.A];
            Span<byte> d = [canvas.R, canvas.G, canvas.B, canvas.A];
            AnimationCompositor.BlendOver8(s, d);
            Assert.Equal(expected, new Rgba32(d[0], d[1], d[2], d[3]));
        }

        static void AssertOver16(ushort[] source, ushort[] canvas, ushort[] expected)
        {
            AnimationCompositor.BlendOver16(source, canvas);
            Assert.Equal(expected, canvas);
        }
    }

    [Fact]
    public void SixteenBitFramesKeepEveryLowBit()
    {
        Rgba64 p = new(0x0102, 0xFEFD, 0x0001, 0xFFFF), q = new(0x1234, 0x5678, 0x9ABC, 0x8001), z = new(0x0F0E, 0x0D0C, 0x0B0A, 0);
        var data = new ApngTestBuilder(2, 2, 2, plays: 3, bitDepth: 16)
            .Frame(ApngTestBuilder.Rgba(2, 2, p, q, z, p), delayNumerator: 30, delayDenominator: 1000)
            .Frame(ApngTestBuilder.Rgba(1, 2, z, q), x: 1, y: 0, blend: 1)
            .ToArray();
        using var image = Image.Load(data);
        Assert.Equal(PixelFormat.Rgba64, image.PixelFormat);
        Assert.Equal(3, image.Animation?.TotalPlays);
        var typed = Assert.IsType<Image<Rgba64>>(image);
        Assert.Equal(new FrameDuration(3, 100), typed.Frames[0].Metadata.Duration);
        Assert.Equal([p, q, z, p], GetPixels(typed.Frames[0]));

        // OVER at 16 bits: a transparent source keeps q; q over p computed with the contract
        Span<ushort> blended = [p.R, p.G, p.B, p.A];
        AnimationCompositor.BlendOver16([q.R, q.G, q.B, q.A], blended);
        Assert.Equal([p, q, z, new Rgba64(blended[0], blended[1], blended[2], blended[3])], GetPixels(typed.Frames[1]));
        Assert.Equal(ReferenceOver([q.R, q.G, q.B, q.A], [p.R, p.G, p.B, p.A], 65535), blended.ToArray());

        // Typed 8-bit loads reduce to nearest
        using var reduced = Image.Load<Rgba32>(data);
        Assert.Equal(new Rgba32(1, 254, 0, 255), reduced.Frames[0][0, 0]);
    }

    [Fact]
    public void SeparatePostersAreNotFramesAndDefaultImagesAre()
    {
        var poster = ApngTestBuilder.Rgba(3, 2, W, W, W, G, G, G);
        var data = new ApngTestBuilder(3, 2, 2, plays: 2)
            .Poster(poster)
            .Frame(ApngTestBuilder.Rgba(2, 1, R, B), x: 1, y: 1, dispose: 2) // partial first frame; PREVIOUS on the first frame clears
            .Frame(ApngTestBuilder.Rgba(1, 1, H), x: 0, y: 0, blend: 1)
            .ToArray();
        Rgba32[] first = [T, T, T, T, R, B];
        Rgba32[] second = [H, T, T, T, T, T]; // H over the cleared canvas is copied
        Rgba32[] posterPixels = [W, W, W, G, G, G];

        using (var image = Image.Load<Rgba32>(data))
        {
            Assert.Equal(2, image.Frames.Count);
            Assert.NotNull(image.PosterFrame);
            Assert.Equal(posterPixels, GetPixels(image.PosterFrame));
            Assert.Equal(first, GetPixels(image.Frames[0]));
            Assert.Equal(second, GetPixels(image.Frames[1]));
            Assert.Equal(2, image.Animation?.TotalPlays);
            Assert.True(image.IsAnimated);
        }

        var info = Image.Identify(data);
        Assert.True(info.HasPosterFrame);
        Assert.Equal(2, info.FrameCount);

        // Readers: one optional poster read before the frames; skipping it never returns it as a frame
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(data)))
        {
            Assert.True(reader.Info.HasPosterFrame);
            using var posterImage = reader.ReadPosterFrame();
            Assert.Equal(posterPixels, GetPixels(posterImage!.Frames[0]));
            Assert.Throws<InvalidOperationException>(() => reader.ReadPosterFrame());
            using var frame0 = reader.ReadFrame();
            Assert.Equal(first, GetPixels(frame0!.Frames[0]));
            Assert.Equal(1, reader.FramesRead);
        }

        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(data)))
        {
            using var frame0 = reader.ReadFrame();
            Assert.Equal(first, GetPixels(frame0!.Frames[0]));
            using var frame1 = reader.ReadFrame();
            Assert.Equal(second, GetPixels(frame1!.Frames[0]));
            Assert.Null(reader.ReadFrame());
            Assert.Throws<InvalidOperationException>(() => reader.ReadPosterFrame());
        }

        // A default image with an fcTL is frame zero: no poster
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(CreateCompositingAnimation())))
        {
            Assert.False(reader.Info.HasPosterFrame);
            Assert.Null(reader.ReadPosterFrame());
            using var frame0 = reader.ReadFrame();
            Assert.Equal(CompositingFrames[0], GetPixels(frame0!.Frames[0]));
        }
    }

    [Fact]
    public void FirstFramesAreDrawnWithSource()
    {
        // OVER on the first frame displays the same image as SOURCE on the cleared canvas; the encoded hidden color is kept
        var data = new ApngTestBuilder(2, 1, 1).Frame(ApngTestBuilder.Rgba(2, 1, X, H), blend: 1).ToArray();
        using var image = Image.Load<Rgba32>(data);
        Assert.Equal([X, H], GetPixels(image.Frames[0]));
        Assert.Equal(AnimationBlend.Source, ApngDecoder.GetBlend(PngFrameControl.BlendOver, isFirstFrame: true));
        Assert.Equal(AnimationBlend.Over, ApngDecoder.GetBlend(PngFrameControl.BlendOver, isFirstFrame: false));
        Assert.Equal(AnimationDisposal.ClearToTransparent, ApngDecoder.GetDisposal(PngFrameControl.DisposePrevious, isFirstFrame: true, isLastFrame: false));
        Assert.Equal(AnimationDisposal.RestorePrevious, ApngDecoder.GetDisposal(PngFrameControl.DisposePrevious, isFirstFrame: false, isLastFrame: false));
        Assert.Equal(AnimationDisposal.None, ApngDecoder.GetDisposal(PngFrameControl.DisposePrevious, isFirstFrame: false, isLastFrame: true));
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task InterlacedRegionsAndSplitChunksStreamThroughEveryInputVariant(InputVariant variant)
    {
        // Seeded 16-bit RGBA regions at odd offsets (Adam7, several fdAT chunks), SOURCE and dispose NONE: each displayed
        // frame is the previous one with the region replaced (placement written directly in the test)
        const int Width = 41, Height = 29;
        var regions = new (PngTestImage Image, int X, int Y)[]
        {
            (PngTestImage.CreateRandom(Width, Height, 6, 16, transparency: false, seed: 1), 0, 0),
            (PngTestImage.CreateRandom(13, 7, 6, 16, transparency: false, seed: 2), 27, 21),
            (PngTestImage.CreateRandom(1, 29, 6, 16, transparency: false, seed: 3), 40, 0),
            (PngTestImage.CreateRandom(40, 3, 6, 16, transparency: false, seed: 4), 1, 13),
        };

        var builder = new ApngTestBuilder(Width, Height, (uint)regions.Length, bitDepth: 16, interlaced: true);
        foreach (var (image, x, y) in regions)
        {
            builder.Frame(image, x, y, chunks: 5);
        }

        var expected = new Rgba64[Width * Height];
        using var decoded = await InputVariants.LoadAsync<Rgba64>(variant, builder.ToArray(), ImageFormat.Png, options: null, XunitCancellationToken);
        Assert.Equal(regions.Length, decoded.Frames.Count);
        for (var i = 0; i < regions.Length; i++)
        {
            var (image, x0, y0) = regions[i];
            var pixels = image.GetExpectedPixels();
            for (var y = 0; y < image.Height; y++)
            {
                pixels.AsSpan(y * image.Width, image.Width).CopyTo(expected.AsSpan(((y0 + y) * Width) + x0));
            }

            Assert.Equal(expected, GetPixels(decoded.Frames[i]));
        }
    }

    [Fact]
    public void CompositorAndDecoderStateAreBudgetedAndReleased()
    {
        var data = CreateLargeAnimation(out var frameCount);
        using (var image = Image.Load<Rgba32>(data))
        {
            Assert.Equal(frameCount, image.Frames.Count);
            var scope = image.Owner.Scope;
            Assert.Equal(0, scope.GetLiveBytes(AllocationKind.CompositorState));
            Assert.Equal(0, scope.GetLiveBytes(AllocationKind.RestorePreviousState));
            Assert.Equal(0, scope.GetLiveBytes(AllocationKind.DecoderState));
            var peak = scope.GetDiagnostics().PeakLiveBytes;

            // Every buffer (pixels, compositor canvas, restore-previous region, decoder state) counts toward the limit, which is
            // enforced incrementally: the measured peak is accepted and one byte less fails without a partial result
            using (Image.Load<Rgba32>(data, Options(peak)))
            {
            }

            var exception = Assert.Throws<ImageResourceLimitException>(() => Image.Load<Rgba32>(data, Options(peak - 1)));
            Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
        }

        // Readers keep one canvas (and at most one restore-previous region) charged to the reader scope, released at the end
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(data)))
        {
            var scope = reader.Core.Scope;
            using (var frame = reader.ReadFrame())
            {
                Assert.True(scope.GetLiveBytes(AllocationKind.CompositorState) > 0);
            }

            while (true)
            {
                using var frame = reader.ReadFrame();
                if (frame is null)
                    break;

                Assert.Equal(0, scope.GetLiveBytes(AllocationKind.RestorePreviousState)); // restored and released within the frame
            }

            // The clean end releases the decoder and compositor state (the input buffer stays until the reader is disposed)
            Assert.Equal(0, scope.GetLiveBytes(AllocationKind.CompositorState));
        }

        static ImageDecodeOptions Options(long limit) => new() { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = limit } } };
    }

    [Fact]
    public void RestorePreviousStateIsBoundedByOneRegion()
    {
        var scope = new AllocationScope(new ImageResourceLimits());
        using var compositor = new AnimationCompositor(scope, new Size(64, 32), PixelFormat.Rgba32);
        var canvasBytes = scope.GetLiveBytes(AllocationKind.CompositorState);
        Assert.Equal(scope.GetChargedSize(64 * 32 * 4), canvasBytes);

        // Fill the canvas, then a PREVIOUS frame over a 5x3 region: only that region is saved, and released once restored
        using (var canvas = compositor.LeaseCanvas())
        {
            compositor.BeginFrame(new Rectangle(0, 0, 64, 32), AnimationBlend.Source, AnimationDisposal.None);
            var row = Enumerable.Range(0, 64 * 4).Select(i => (byte)i).ToArray();
            for (var y = 0; y < 32; y++)
            {
                compositor.WritePixels(canvas, 0, y, 1, 64, row);
            }
        }

        compositor.EndFrame();
        var region = new Rectangle(7, 11, 5, 3);
        compositor.BeginFrame(region, AnimationBlend.Source, AnimationDisposal.RestorePrevious);
        Assert.True(compositor.HasSavedRegion);
        Assert.Equal(scope.GetChargedSize(5 * 3 * 4), scope.GetLiveBytes(AllocationKind.RestorePreviousState));
        using (var canvas = compositor.LeaseCanvas())
        {
            for (var y = region.Y; y < region.Bottom; y++)
            {
                compositor.WritePixels(canvas, region.X, y, 1, region.Width, new byte[region.Width * 4]);
            }

        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            using var canvas = compositor.LeaseCanvas();
            compositor.WritePixels(canvas, region.Right, region.Y, 1, 1, new byte[4]);
        });

        compositor.EndFrame();
        Assert.False(compositor.HasSavedRegion);
        Assert.Equal(0, scope.GetLiveBytes(AllocationKind.RestorePreviousState));
        using (var canvas = compositor.LeaseCanvas())
        {
            for (var y = 0; y < 32; y++)
            {
                Assert.Equal(Enumerable.Range(0, 64 * 4).Select(i => (byte)i), canvas.GetRowBytes(y).ToArray());
            }
        }

        // Clearing to transparent black touches only the region
        compositor.BeginFrame(region, AnimationBlend.Over, AnimationDisposal.ClearToTransparent);
        compositor.EndFrame();
        using (var canvas = compositor.LeaseCanvas())
        {
            Assert.True(canvas.GetRowBytes(11).Slice(7 * 4, 5 * 4).IndexOfAnyExcept((byte)0) < 0);
            Assert.Equal((byte)(6 * 4 % 256), canvas.GetRowBytes(11)[6 * 4]);
            Assert.Equal((byte)(12 * 4 % 256), canvas.GetRowBytes(11)[12 * 4]);
        }

        // The canvas and the saved region are budgeted
        var small = new AllocationScope(new ImageResourceLimits { MaxLiveAllocationBytes = canvasBytes });
        using var tight = new AnimationCompositor(small, new Size(64, 32), PixelFormat.Rgba32);
        Assert.Throws<ImageResourceLimitException>(() => tight.BeginFrame(region, AnimationBlend.Source, AnimationDisposal.RestorePrevious));
        Assert.Throws<ImageResourceLimitException>(() => new AnimationCompositor(new AllocationScope(new ImageResourceLimits { MaxLiveAllocationBytes = canvasBytes - 1 }), new Size(64, 32), PixelFormat.Rgba32));
    }

    [Fact]
    public void ControlDataErrorsFaultReadersAndNeverEndCleanly()
    {
        var region = ApngTestBuilder.Rgba(1, 1, W);

        // Declared three frames, two present: both frames are returned, then the missing frame is an error (never a clean end)
        var short_ = new ApngTestBuilder(3, 2, 3).Frame(Full(R)).Frame(region, 1, 1).ToArray();
        AssertReaderFails<InvalidImageContentException>(short_, framesBeforeError: 2);

        // An fdAT sequence number out of order in the third frame
        var builder = new ApngTestBuilder(3, 2, 3).Frame(Full(R)).Frame(region, 1, 1);
        var sequenceError = builder.Frame(region, 2, 0, sequence: 7).ToArray();
        AssertReaderFails<InvalidImageContentException>(sequenceError, framesBeforeError: 2);

        // A region outside the canvas, more frames than declared, an fcTL without data
        AssertReaderFails<InvalidImageContentException>(new ApngTestBuilder(3, 2, 2).Frame(Full(R)).Frame(region, 3, 0).ToArray(), framesBeforeError: 1);
        AssertReaderFails<InvalidImageContentException>(new ApngTestBuilder(3, 2, 1).Frame(Full(R)).Frame(region, 1, 1).ToArray(), framesBeforeError: 1);
        AssertReaderFails<InvalidImageContentException>(new ApngTestBuilder(3, 2, 2).Frame(Full(R)).Chunk("fcTL", FrameControl(1, 1, 1, 1, 1)).ToArray(), framesBeforeError: 1);

        // A truncated animation is never a successful still or a clean end
        var truncated = CreateCompositingAnimation()[..^30];
        Assert.Throws<InvalidImageContentException>(() => Image.Load(truncated));
        AssertReaderFails<InvalidImageContentException>(truncated, framesBeforeError: 3);

        static void AssertReaderFails<TException>(byte[] data, int framesBeforeError)
            where TException : Exception
        {
            Assert.Throws<TException>(() => Image.Load(data));
            using var reader = Image.OpenReader<Rgba32>(new MemoryStream(data));
            for (var i = 0; i < framesBeforeError; i++)
            {
                using var frame = reader.ReadFrame();
                Assert.NotNull(frame);
            }

            Assert.Throws<TException>(() => reader.ReadFrame());
            Assert.Throws<InvalidOperationException>(() => reader.ReadFrame()); // faulted
            Assert.Equal(0, reader.Core.Scope.GetLiveBytes(AllocationKind.CompositorState));
        }
    }

    [Fact]
    public void FrameLimitsSelectAPrefixWithoutExaminingTheRest()
    {
        // The input stops 3 bytes into the third fcTL: a two-frame prefix ends at that chunk header and never reads the rest
        var full = CreateCompositingAnimation();
        var builder = new ApngTestBuilder(3, 2, 4).Frame(Full(R)).Frame(ApngTestBuilder.Rgba(1, 1, G), 1, 1);
        var twoFrames = builder.ToArray(end: false).Length;
        var prefix = builder.Frame(ApngTestBuilder.Rgba(1, 1, B)).Frame(ApngTestBuilder.Rgba(1, 1, B)).ToArray()[..(twoFrames + 11)];
        using (var image = Image.Load<Rgba32>(prefix, new ImageDecodeOptions { FrameLimit = 2 }))
        {
            Assert.Equal(2, image.Frames.Count);
            Assert.Equal([R, R, R, R, G, R], GetPixels(image.Frames[1]));
        }

        Assert.Throws<InvalidImageContentException>(() => Image.Load(prefix, new ImageDecodeOptions { FrameLimit = 3 }));
        using (var image = Image.Load<Rgba32>(full, new ImageDecodeOptions { FrameLimit = 3 }))
        {
            Assert.Equal(CompositingFrames[2], GetPixels(image.Frames[2]));
        }

        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(prefix), new ImageReaderOptions { FrameLimit = 2 });
        using (var first = reader.ReadFrame())
        using (var second = reader.ReadFrame())
        {
            Assert.NotNull(second);
        }

        Assert.Null(reader.ReadFrame());
    }

    [Fact]
    public async Task CancellationBetweenRowsAndFramesFaultsWithoutPartialResults()
    {
        var data = CreateLargeAnimation(out _);
        var position = data.Length - 2000; // inside the last frame
        using (var source = new CancellationTokenSource())
        {
            await using var stream = new TestInputStream(data) { CancellationSource = source, CancelAtPosition = position, MaxBytesPerRead = 512 };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Image.LoadAsync<Rgba32>(stream, cancellationToken: source.Token));
        }

        using (var source = new CancellationTokenSource())
        {
            var stream = new TestInputStream(data) { CancellationSource = source, CancelAtPosition = position, MaxBytesPerRead = 512 };
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

            Assert.InRange(read, 1, 7);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadFrameAsync(XunitCancellationToken));
            Assert.Equal(0, reader.Core.Scope.GetLiveBytes(AllocationKind.CompositorState));
        }
    }

    [Fact]
    public void TypedLoadsApplyTheAlphaAndPrecisionPolicies()
    {
        // Gray APNGs composite on RGBA; opaque results convert to gray exactly
        var gray = PngTestImage.Create(2, 1, 0, 8, [10, 200]);
        var opaque = new ApngTestBuilder(2, 1, 2, colorType: 0).Frame(gray).Frame(PngTestImage.Create(1, 1, 0, 8, [77]), 1, 0).ToArray();
        using (var image = Image.Load<Gray8>(opaque))
        {
            Assert.Equal([(byte)10, (byte)77], GetPixels(image.Frames[1]).Select(p => p.Value));
        }

        // Transparent pixels are never dropped silently; an explicit background flattens them
        var translucent = CreateCompositingAnimation();
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load<Rgb24>(translucent));
        using var flattened = Image.Load<Rgb24>(translucent, new ImageDecodeOptions { Conversion = new PixelConversionOptions { BackgroundColor = new Rgba32(255, 255, 255, 255) } });
        Assert.Equal(new Rgb24(255, 255, 255), flattened.Frames[0][2, 1]); // X: hidden color, alpha 0
        Assert.Equal(new Rgb24(127, 127, 255), flattened.Frames[0][1, 1]); // H flattened on white: (0 * 128 + 255 * 127 + 127) / 255
    }

    private static byte[] CreateCompositingAnimation()
        => new ApngTestBuilder(3, 2, 4)
            .Frame(ApngTestBuilder.Rgba(3, 2, R, G, B, W, H, X), delayNumerator: 1, delayDenominator: 10)
            .Frame(ApngTestBuilder.Rgba(2, 1, H, X), x: 1, y: 0, dispose: 1, blend: 1, delayNumerator: 0, delayDenominator: 0)
            .Frame(ApngTestBuilder.Rgba(2, 2, G, X, R, T), x: 1, y: 0, dispose: 2, blend: 0, delayNumerator: 7, delayDenominator: 0)
            .Frame(ApngTestBuilder.Rgba(1, 2, H, H), x: 0, y: 0, dispose: 2, blend: 1, delayNumerator: 65535, delayDenominator: 1)
            .ToArray();

    /// <summary>An 8-frame 96x64 animation with every disposal and blend (random regions, several fdAT chunks).</summary>
    private static byte[] CreateLargeAnimation(out int frameCount)
    {
        frameCount = 8;
        var builder = new ApngTestBuilder(96, 64, (uint)frameCount);
        builder.Frame(PngTestImage.CreateRandom(96, 64, 6, 8, transparency: false, seed: 10), chunks: 3);
        for (var i = 1; i < frameCount; i++)
        {
            builder.Frame(PngTestImage.CreateRandom(30 + i, 20 + i, 6, 8, transparency: false, seed: 10 + i), x: i * 5, y: i * 4, dispose: (byte)(i % 3), blend: (byte)(i % 2), chunks: 2);
        }

        return builder.ToArray();
    }

    private static PngTestImage Full(Rgba32 color) => ApngTestBuilder.Rgba(3, 2, color, color, color, color, color, color);

    private static byte[] FrameControl(uint sequence, int width, int height, int x, int y)
    {
        var data = new byte[26];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data, sequence);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), height);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), x);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), y);
        return data;
    }

    /// <summary>The OVER contract transcribed with arbitrary-precision rationals: round-half-up of the exact straight-alpha composite.</summary>
    private static ushort[] ReferenceOver(ushort[] source, ushort[] canvas, int max)
    {
        if (source[3] == 0)
            return canvas;

        BigInteger m = max, sa = source[3], da = canvas[3];
        var total = (sa * m) + (da * (m - sa));
        var result = new ushort[4];
        for (var c = 0; c < 3; c++)
        {
            var numerator = (source[c] * sa * m) + (canvas[c] * da * (m - sa));
            result[c] = (ushort)BigInteger.Divide((2 * numerator) + total, 2 * total);
        }

        result[3] = (ushort)BigInteger.Divide((2 * total) + m, 2 * m);
        return result;
    }

    private static TPixel[] GetPixels<TPixel>(ImageFrame<TPixel> frame)
        where TPixel : unmanaged
    {
        var pixels = new TPixel[frame.Width * frame.Height];
        frame.CopyPixelDataTo(pixels);
        return pixels;
    }

    private static void Fill(ImageFrame<Rgba32> frame, Rgba32 color) => frame.ProcessPixelRows(color, static (accessor, color) =>
    {
        for (var y = 0; y < accessor.Height; y++)
        {
            accessor.GetRowSpan(y).Fill(color);
        }
    });
}
