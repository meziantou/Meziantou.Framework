using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Animated cursor (ANI) decoding through the public APIs, on files assembled byte by byte by
/// <see cref="AniFileBuilder"/> (over the icon and cursor files of <see cref="IcoFileBuilder"/>) with literal expected
/// pixels, durations and hotspots: the steps played in order, the rate and sequence tables, the chunk layouts real files
/// use, the representation shown by each frame, the limits, and the malformed files that must be rejected.
/// </summary>
public sealed class AniCodecTests
{
    private static readonly Rgba32[] PixelsA = [new Rgba32(10, 20, 30, 255), new Rgba32(40, 50, 60, 128), new Rgba32(70, 80, 90, 0), new Rgba32(1, 2, 3, 200)];
    private static readonly Rgba32[] PixelsB = [new Rgba32(200, 0, 0, 255), new Rgba32(0, 200, 0, 255), new Rgba32(0, 0, 200, 255), new Rgba32(9, 9, 9, 9)];
    private static readonly Rgba32[] PixelsC = [new Rgba32(5, 5, 5, 255), new Rgba32(6, 6, 6, 255), new Rgba32(7, 7, 7, 255), new Rgba32(8, 8, 8, 255)];

    [Fact]
    public void AnimatedCursorsAreDetectedByTheirRiffFormType()
    {
        var data = TwoFrames().Build();
        Assert.Equal(ImageFormat.Ani, Image.DetectFormat(data));
        Assert.Equal(ImageFormat.Ani, Image.DetectFormat(data.AsSpan(0, 12)));
        Assert.Equal(ImageFormat.Unknown, Image.DetectFormat(data.AsSpan(0, 11)));
    }

    [Fact]
    public void StepsBecomeFramesWithTheirPixelsDurationsAndHotspots()
    {
        var data = TwoFrames().Build();
        using var image = Image.Load<Rgba32>(data);
        Assert.Equal(ImageFormat.Ani, image.Metadata.SourceFormat);
        Assert.Equal(PixelFormat.Rgba32, image.PixelFormat);
        Assert.Equal(new Size(2, 2), image.Size);
        Assert.Equal(2, image.Frames.Count);
        Assert.Equal(PixelsA, Pixels(image, 0));
        Assert.Equal(PixelsB, Pixels(image, 1));

        // The default rate of the header, in jiffies: 10/60 s for every step
        Assert.Equal([new FrameDuration(10, 60), new FrameDuration(1, 6)], Durations(image));
        Assert.Equal([new Point(1, 0), new Point(0, 1)], Hotspots(image));

        // An animated cursor has no play count: it always loops
        Assert.True(image.IsAnimated);
        Assert.Null(image.Animation!.TotalPlays);
        Assert.Null(image.PosterFrame);
    }

    [Fact]
    public void TheRateChunkGivesEachStepItsOwnDuration()
    {
        var data = new AniFileBuilder { DisplayRate = 99, Rates = [0, 7, uint.MaxValue] }
            .AddFrame(Cursor(PixelsA))
            .AddFrame(Cursor(PixelsB))
            .AddFrame(Cursor(PixelsC))
            .Build();

        using var image = Image.Load(data);
        Assert.Equal([FrameDuration.Zero, new FrameDuration(7, 60), new FrameDuration(uint.MaxValue, 60)], Durations(image));
        Assert.Equal(TimeSpan.Zero, image.Frames[0].Metadata.Duration.ToTimeSpan()); // no minimum-delay heuristic
    }

    [Fact]
    public void TheSequenceIsExpandedIntoIndependentFramesInPlaybackOrder()
    {
        var data = new AniFileBuilder { Sequence = [1, 0, 0, 1], Rates = [1, 2, 3, 4] }
            .AddFrame(Cursor(PixelsA, hotspotX: 1, hotspotY: 1))
            .AddFrame(Cursor(PixelsB))
            .Build();

        using var image = Image.Load<Rgba32>(data);
        Assert.Equal(4, image.Frames.Count);
        Assert.Equal(PixelsB, Pixels(image, 0));
        Assert.Equal(PixelsA, Pixels(image, 1));
        Assert.Equal(PixelsA, Pixels(image, 2));
        Assert.Equal(PixelsB, Pixels(image, 3));
        Assert.Equal([new FrameDuration(1, 60), new FrameDuration(2, 60), new FrameDuration(3, 60), new FrameDuration(4, 60)], Durations(image));
        Assert.Equal([new Point(0, 0), new Point(1, 1), new Point(1, 1), new Point(0, 0)], Hotspots(image));

        // Two steps showing the same stored frame are two frames of the image: editing one leaves the other alone
        image.Frames[1][0, 0] = new Rgba32(255, 255, 255, 255);
        Assert.Equal(PixelsA, Pixels(image, 2));
    }

    [Fact]
    public void FewerStepsThanFramesPlayTheFirstFrames()
    {
        var data = new AniFileBuilder { StepCount = 1 }
            .AddFrame(Cursor(PixelsA))
            .AddFrame([1, 2, 3]) // never shown, so never read as a cursor
            .Build();

        using var image = Image.Load<Rgba32>(data);
        Assert.Equal(1, image.Frames.Count);
        Assert.Equal(PixelsA, Pixels(image, 0));
        Assert.True(image.IsAnimated);
    }

    [Fact]
    public void TablesMayFollowTheFramesAndUnknownChunksAreSkipped()
    {
        var data = new AniFileBuilder
        {
            TablesAfterFrames = true,
            Sequence = [1, 0],
            Rates = [3, 5],
            Flags = 1, // the sequence chunk is used even when the header does not announce it
            LeadingChunks = AniFileBuilder.Chunk("JUNK", [1, 2, 3]), // an odd size, followed by its pad byte
            FrameListLeadingChunks = AniFileBuilder.Chunk("note", [4]),
        }
            .AddFrame(Cursor(PixelsA))
            .AddFrame(Cursor(PixelsB))
            .Build();

        using var image = Image.Load<Rgba32>(data);
        Assert.Equal(PixelsB, Pixels(image, 0));
        Assert.Equal(PixelsA, Pixels(image, 1));
        Assert.Equal([new FrameDuration(3, 60), new FrameDuration(5, 60)], Durations(image));
    }

    [Fact]
    public void BytesAfterTheRiffContainerAreIgnoredAndTheLastPadByteMayBeMissing()
    {
        var data = TwoFrames().Build();
        using (var image = Image.Load([.. data, 1, 2, 3, 4, 5]))
        {
            Assert.Equal(2, image.Frames.Count);
        }

        // An odd-sized last chunk without its pad byte, as some tools write it
        var unpadded = new AniFileBuilder { RiffSize = null, TrailingBytes = AniFileBuilder.Chunk("JUNK", [1, 2, 3])[..^1] }
            .AddFrame(Cursor(PixelsA))
            .Build();

        using var tolerated = Image.Load(unpadded);
        Assert.Equal(1, tolerated.Frames.Count);
    }

    [Fact]
    public void IconTypedFramesHaveNoHotspot()
    {
        var data = new AniFileBuilder().AddFrame(Icon(PixelsA)).AddFrame(Cursor(PixelsB, hotspotX: 1, hotspotY: 1)).Build();
        using var image = Image.Load(data);
        Assert.Null(image.Frames[0].Metadata.Hotspot);
        Assert.Equal(new Point(1, 1), image.Frames[1].Metadata.Hotspot);
    }

    [Fact]
    public void AFrameWithSeveralRepresentationsShowsTheOneOfTheCanvasSize()
    {
        // The first step fixes the canvas with the rule of icon files: the largest representation
        var first = new IcoFileBuilder { Type = 2 }
            .Add(new IcoEntrySpec { Width = 1, Height = 1, PlanesOrHotspotX = 0, BitCountOrHotspotY = 0, Payload = IcoFileBuilder.Dib32(1, 1, [new Rgba32(1, 1, 1, 255)]) })
            .Add(new IcoEntrySpec { Width = 2, Height = 2, PlanesOrHotspotX = 1, BitCountOrHotspotY = 0, Payload = IcoFileBuilder.Dib32(2, 2, PixelsA) })
            .Build();

        // The other frames show their deepest representation of that size, whatever else they hold
        Rgb24[] opaque = [new Rgb24(1, 2, 3), new Rgb24(4, 5, 6), new Rgb24(7, 8, 9), new Rgb24(10, 11, 12)];
        var second = new IcoFileBuilder { Type = 2 }
            .Add(new IcoEntrySpec { Width = 4, Height = 4, PlanesOrHotspotX = 3, BitCountOrHotspotY = 3, Payload = IcoFileBuilder.Dib32(4, 4, new Rgba32[16]) })
            .Add(new IcoEntrySpec { Width = 2, Height = 2, PlanesOrHotspotX = 0, BitCountOrHotspotY = 0, Payload = IcoFileBuilder.Dib24(2, 2, opaque) })
            .Add(new IcoEntrySpec { Width = 2, Height = 2, PlanesOrHotspotX = 0, BitCountOrHotspotY = 1, Payload = IcoFileBuilder.Dib32(2, 2, PixelsB) })
            .Build();

        var data = new AniFileBuilder().AddFrame(first).AddFrame(second).Build();
        using var image = Image.Load<Rgba32>(data);
        Assert.Equal(new Size(2, 2), image.Size);
        Assert.Equal(PixelsA, Pixels(image, 0));
        Assert.Equal(PixelsB, Pixels(image, 1));
        Assert.Equal([new Point(1, 0), new Point(0, 1)], Hotspots(image));

        // A frame without a representation of the canvas size cannot be a frame of the animation
        var other = new AniFileBuilder().AddFrame(Cursor(PixelsA)).AddFrame(new IcoFileBuilder { Type = 2 }
            .Add(new IcoEntrySpec { Width = 1, Height = 1, PlanesOrHotspotX = 0, BitCountOrHotspotY = 0, Payload = IcoFileBuilder.Dib32(1, 1, [new Rgba32(1, 1, 1, 255)]) })
            .Build()).Build();

        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(other));
        Assert.Equal(ImageFormat.Ani, exception.Format);
        Assert.Equal("Frame size", exception.Feature);
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(other));
    }

    [Fact]
    public void PngFramesAreDecodedByThePngCodec()
    {
        var png = SyntheticImages.Png(2, 2);
        var data = new AniFileBuilder().AddFrame(Cursor(PixelsA)).AddFrame(PngCursor(png)).Build();
        using var direct = Image.Load<Rgba32>(png);
        using var image = Image.Load<Rgba32>(data);
        Assert.Equal(PixelFormat.Rgba32, Image.Identify(data).PixelFormat);
        Assert.Equal(PixelsA, Pixels(image, 0));
        Assert.Equal(Pixels(direct, 0), Pixels(image, 1));

        var animated = new AniFileBuilder().AddFrame(PngCursor(SyntheticImages.Apng(2, 2, frames: 2))).Build();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(animated));
        Assert.Equal(ImageFormat.Ani, exception.Format);
        Assert.Contains("frame 0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASixteenBitFrameWidensTheWholeAnimationInsteadOfBeingNarrowed()
    {
        var png = SyntheticImages.Png(2, 2, colorType: 6, bitDepth: 16);
        var data = new AniFileBuilder().AddFrame(Cursor(PixelsA)).AddFrame(PngCursor(png)).Build();
        var info = Image.Identify(data);
        Assert.Equal(PixelFormat.Rgba64, info.PixelFormat);
        Assert.Equal(16, info.BitsPerComponent);

        using var direct = Image.Load<Rgba64>(png);
        using var image = Image.Load(data);
        var typed = Assert.IsType<Image<Rgba64>>(image);

        // 8-bit samples are widened exactly: v * 257
        Assert.Equal([.. PixelsA.Select(p => new Rgba64((ushort)(p.R * 257), (ushort)(p.G * 257), (ushort)(p.B * 257), (ushort)(p.A * 257)))], Pixels(typed, 0));
        Assert.Equal(Pixels(direct, 0), Pixels(typed, 1));
    }

    [Fact]
    public void TheTitleAndTheAuthorAreReadAsLatin1Text()
    {
        var data = new AniFileBuilder { Title = "Curseur animé", Author = "Gérald" }.AddFrame(Cursor(PixelsA)).Build();
        using var image = Image.Load(data);
        Assert.Equal([new ImageTextEntry("Title", "Curseur animé"), new ImageTextEntry("Author", "Gérald")], image.Metadata.TextEntries);
        Assert.Equal(image.Metadata.TextEntries, Image.Identify(data).Metadata.TextEntries);

        using var none = Image.Load(TwoFrames().Build());
        Assert.Empty(none.Metadata.TextEntries);
    }

    [Theory]
    [InlineData(ImageIdentifyMode.Header)]
    [InlineData(ImageIdentifyMode.FullScan)]
    public void IdentifyDescribesTheAnimationWithoutDecodingIt(ImageIdentifyMode mode)
    {
        var data = new AniFileBuilder { Sequence = [0, 1, 0] }.AddFrame(Cursor(PixelsA)).AddFrame(Cursor(PixelsB)).Build();
        var info = Image.Identify(data, new ImageIdentifyOptions { Mode = mode });
        Assert.Equal(ImageFormat.Ani, info.Format);
        Assert.Equal(new Size(2, 2), info.Size);
        Assert.Equal(PixelFormat.Rgba32, info.PixelFormat);
        Assert.Equal(ImageColorModel.Rgba, info.ColorModel);
        Assert.Equal(8, info.BitsPerComponent);
        Assert.Equal(3, info.FrameCount);
        Assert.True(info.IsAnimated);
        Assert.False(info.HasPosterFrame);
        Assert.True(info.MayHaveTransparency);
        Assert.Null(info.Animation!.TotalPlays);
        Assert.Null(info.CollectionEntryCount);
        Assert.Equal(mode, info.IdentifyMode);
    }

    [Fact]
    public async Task EveryInputVariantDecodesTheSameAnimation()
    {
        var data = new AniFileBuilder { Sequence = [1, 0, 1], Rates = [1, 2, 3], Title = "t" }
            .AddFrame(Cursor(PixelsA, hotspotX: 1, hotspotY: 1))
            .AddFrame(Cursor(PixelsB))
            .Build();

        foreach (var variant in InputVariants.All)
        {
            using var image = await InputVariants.LoadAsync<Rgba32>(variant, data, ImageFormat.Ani, options: null, XunitCancellationToken);
            Assert.Equal(3, image.Frames.Count);
            Assert.Equal(PixelsB, Pixels(image, 0));
            Assert.Equal(PixelsA, Pixels(image, 1));
            Assert.Equal(PixelsB, Pixels(image, 2));
            Assert.Equal([new FrameDuration(1, 60), new FrameDuration(2, 60), new FrameDuration(3, 60)], Durations(image));
            Assert.Equal(new Point(1, 1), image.Frames[1].Metadata.Hotspot);

            var info = await InputVariants.IdentifyAsync(variant, data, ImageFormat.Ani, options: null, XunitCancellationToken);
            Assert.Equal(3, info.FrameCount);
        }
    }

    [Fact]
    public void ATypedLoadConvertsEveryStepOnce()
    {
        var data = new AniFileBuilder { Sequence = [0, 0] }.AddFrame(Cursor(PixelsB)).Build();
        using var image = Image.Load<Bgra32>(data);
        var expected = PixelsB.Select(p => new Bgra32(p.R, p.G, p.B, p.A)).ToArray();
        Assert.Equal(expected, Pixels(image, 0));
        Assert.Equal(expected, Pixels(image, 1));

        // Alpha is never dropped silently, for a repeated step either
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load<Rgb24>(data));
    }

    [Fact]
    public void AFrameLimitDecodesAPrefixWithoutReadingLaterPayloads()
    {
        var large = LargeCursor(seed: 1);
        var data = new AniFileBuilder().AddFrame(Cursor(PixelsA)).AddFrame(large).AddFrame(large).Build();

        // The cursor frames are 2x2 and 64x64: they cannot belong to one animation, which a full load reports
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));

        // Four 256x256 frames of 262 KiB each: decoding the first one reads its payload and the structure, not the others.
        // The detection of the format reads one input buffer first, so the bound is what reading every frame would exceed.
        var frame = LargeCursor(seed: 1, side: 256);
        var sameSize = new AniFileBuilder().AddFrame(frame).AddFrame(LargeCursor(seed: 2, side: 256)).AddFrame(LargeCursor(seed: 3, side: 256)).AddFrame(LargeCursor(seed: 5, side: 256)).Build();
        using var stream = new CountingStream(sameSize);
        using var image = Image.Load(stream, new ImageDecodeOptions { FrameLimit = 1 });
        Assert.Equal(1, image.Frames.Count);
        Assert.True(image.IsAnimated);
        Assert.True(stream.BytesRead < sameSize.Length - frame.Length, $"Decoding one of four {frame.Length}-byte frames read {stream.BytesRead} of {sameSize.Length} bytes.");
    }

    [Fact]
    public void AFrameShownBySeveralStepsIsReadAndDecodedOnce()
    {
        var frame = LargeCursor(seed: 4, side: 256);
        var data = new AniFileBuilder { Sequence = [0, 0, 0, 0, 0, 0, 0, 0] }.AddFrame(frame).Build();
        using var stream = new CountingStream(data);
        using var image = Image.Load<Rgba32>(stream);
        Assert.Equal(8, image.Frames.Count);
        Assert.Equal(Pixels(image, 0), Pixels(image, 7));

        // The detection of the format reads one input buffer, then the payload is read once: far from eight times
        Assert.True(stream.BytesRead < frame.Length * 3, $"Decoding eight steps of one {frame.Length}-byte frame read {stream.BytesRead} bytes.");
    }

    [Fact]
    public void EveryStepIsChargedToTheLimitsExactlyOnce()
    {
        // One stored PNG frame shown three times: three frames of four pixels, and not one more for the embedded decode
        var data = new AniFileBuilder { Sequence = [0, 0, 0] }.AddFrame(PngCursor(SyntheticImages.Png(2, 2))).Build();
        using (var image = Image.Load(data, Limits(new ImageResourceLimits { MaxFrames = 3, MaxTotalPixels = 12 })))
        {
            Assert.Equal(3, image.Frames.Count);
        }

        Assert.Equal(ImageResourceLimitKind.Frames, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Limits(new ImageResourceLimits { MaxFrames = 2 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.TotalPixels, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Limits(new ImageResourceLimits { MaxTotalPixels = 11 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Limits(new ImageResourceLimits { MaxWidth = 1 }))).Kind);

        // The declared counts are checked before anything sized by them is read, by identification too
        var identify = new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxFrames = 2 } } };
        Assert.Equal(ImageResourceLimitKind.Frames, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, identify)).Kind);
        var frames = new AniFileBuilder { FrameCount = 1_000_000 }.AddFrame(Cursor(PixelsA)).Build();
        Assert.Equal(ImageResourceLimitKind.Frames, Assert.Throws<ImageResourceLimitException>(() => Image.Load(frames)).Kind);

        // A prefix selected on purpose is not an error
        using var prefix = Image.Load(data, new ImageDecodeOptions { FrameLimit = 2, Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxFrames = 3, MaxTotalPixels = 8 } } });
        Assert.Equal(2, prefix.Frames.Count);
    }

    [Fact]
    public void MetadataAndEncodedBytesAreBounded()
    {
        var data = new AniFileBuilder { Title = "0123456789" }.AddFrame(Cursor(PixelsA)).Build();

        // The string and its terminator
        using (Image.Load(data, Limits(new ImageResourceLimits { MaxMetadataBytes = 11 })))
        {
        }

        Assert.Equal(ImageResourceLimitKind.MetadataBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Limits(new ImageResourceLimits { MaxMetadataBytes = 10 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Limits(new ImageResourceLimits { MaxEncodedBytes = 64 }))).Kind);
    }

    [Fact]
    public void MalformedContainersAreRejected()
    {
        // No header chunk, or two of them
        AssertInvalid(new AniFileBuilder { HeaderChunks = 0 }.AddFrame(Cursor(PixelsA)));
        AssertInvalid(new AniFileBuilder { HeaderChunks = 2 }.AddFrame(Cursor(PixelsA)));

        // A header of the wrong size, in its chunk or in its own length field
        AssertInvalid(new AniFileBuilder { HeaderChunkLength = 32 }.AddFrame(Cursor(PixelsA)));
        AssertInvalid(new AniFileBuilder { HeaderChunkLength = 40 }.AddFrame(Cursor(PixelsA)));
        AssertInvalid(new AniFileBuilder { HeaderLengthField = 40 }.AddFrame(Cursor(PixelsA)));

        // No frame or no step
        AssertInvalid(new AniFileBuilder { FrameCount = 0 }.AddFrame(Cursor(PixelsA)));
        AssertInvalid(new AniFileBuilder { StepCount = 0 }.AddFrame(Cursor(PixelsA)));

        // No frame list, or two of them
        AssertInvalid(new AniFileBuilder { FrameLists = 0 }.AddFrame(Cursor(PixelsA)));
        AssertInvalid(new AniFileBuilder { FrameLists = 2 }.AddFrame(Cursor(PixelsA)));

        // Fewer or more stored frames than declared
        AssertInvalid(new AniFileBuilder { FrameCount = 2 }.AddFrame(Cursor(PixelsA)));
        AssertInvalid(new AniFileBuilder { FrameCount = 1 }.AddFrame(Cursor(PixelsA)).AddFrame(Cursor(PixelsB)));

        // More steps than frames, with nothing saying which frame the last step shows
        AssertInvalid(new AniFileBuilder { StepCount = 2 }.AddFrame(Cursor(PixelsA)));

        // Tables whose size does not match the number of steps
        AssertInvalid(new AniFileBuilder { Rates = [1, 2], StepCount = 1 }.AddFrame(Cursor(PixelsA)));
        AssertInvalid(new AniFileBuilder { Sequence = [0], StepCount = 2 }.AddFrame(Cursor(PixelsA)));

        // A step showing a frame that does not exist
        AssertInvalid(new AniFileBuilder { Sequence = [0, 1] }.AddFrame(Cursor(PixelsA)));

        // Duplicate tables
        AssertInvalid(new AniFileBuilder { Rates = [1], LeadingChunks = AniFileBuilder.Chunk("rate", [1, 0, 0, 0]) }.AddFrame(Cursor(PixelsA)));
        AssertInvalid(new AniFileBuilder { Sequence = [0], LeadingChunks = AniFileBuilder.Chunk("seq ", [0, 0, 0, 0]) }.AddFrame(Cursor(PixelsA)));

        // A chunk or a list that runs past its container, and bytes too short to be a chunk
        AssertInvalid(new AniFileBuilder { FrameListSize = 1_000_000 }.AddFrame(Cursor(PixelsA)));
        AssertInvalid(new AniFileBuilder { FrameListSize = 2 }.AddFrame(Cursor(PixelsA)));
        AssertInvalid(new AniFileBuilder { TrailingBytes = [1, 2, 3] }.AddFrame(Cursor(PixelsA)));
        AssertInvalid(new AniFileBuilder { RiffSize = 2 }.AddFrame(Cursor(PixelsA)));
        AssertInvalid(new AniFileBuilder { FrameListLeadingChunks = [(byte)'x', (byte)'x', (byte)'x', (byte)'x', 0xFF, 0xFF, 0xFF, 0x7F] }.AddFrame(Cursor(PixelsA)));

        static void AssertInvalid(AniFileBuilder builder)
        {
            var data = builder.Build();
            Assert.Equal(ImageFormat.Ani, Assert.Throws<InvalidImageContentException>(() => Image.Load(data)).Format);
            Assert.Equal(ImageFormat.Ani, Assert.Throws<InvalidImageContentException>(() => Image.Identify(data)).Format);
        }
    }

    [Fact]
    public void ATruncatedFileIsNeverACleanEnd()
    {
        var data = new AniFileBuilder { Sequence = [1, 0], Rates = [1, 2], Title = "t" }.AddFrame(Cursor(PixelsA)).AddFrame(Cursor(PixelsB)).Build();
        for (var length = 12; length < data.Length; length++)
        {
            var truncated = data.AsSpan(0, length).ToArray();
            Assert.Throws<InvalidImageContentException>(() => Image.Load(truncated));
            Assert.Throws<InvalidImageContentException>(() => Image.Identify(truncated));
        }

        using var stream = new MemoryStream(data.AsSpan(0, data.Length - 1).ToArray());
        Assert.Throws<InvalidImageContentException>(() => Image.Load(stream));
    }

    [Fact]
    public void RawBitmapFramesAreRecognizedAndRejected()
    {
        var data = new AniFileBuilder { Flags = 0 }.AddFrame(Cursor(PixelsA)).Build();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
        Assert.Equal(ImageFormat.Ani, exception.Format);
        Assert.Equal("Raw bitmap frames", exception.Feature);
    }

    [Fact]
    public void TheErrorsOfAFrameAreErrorsOfTheAnimatedCursorAndNameTheFrame()
    {
        // A hotspot outside its own cursor image
        var outside = new AniFileBuilder().AddFrame(Cursor(PixelsA)).AddFrame(Cursor(PixelsB, hotspotX: 2, hotspotY: 0)).Build();
        var hotspot = Assert.Throws<InvalidImageContentException>(() => Image.Load(outside));
        Assert.Equal(ImageFormat.Ani, hotspot.Format);
        Assert.Contains("frame 1", hotspot.Message, StringComparison.Ordinal);
        Assert.Contains("hotspot", hotspot.Message, StringComparison.OrdinalIgnoreCase);

        // An embedded directory whose payload would be outside its own chunk, even though the bytes exist in the file
        var escaping = new IcoFileBuilder { Type = 2 }.Add(new IcoEntrySpec
        {
            Width = 2,
            Height = 2,
            PlanesOrHotspotX = 0,
            BitCountOrHotspotY = 0,
            LengthOverride = 200,
            Payload = IcoFileBuilder.Dib32(2, 2, PixelsA),
        }).Build();

        var data = new AniFileBuilder().AddFrame(escaping).AddFrame(LargeCursor(seed: 1)).Build();
        var range = Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        Assert.Equal(ImageFormat.Ani, range.Format);
        Assert.Contains("frame 0", range.Message, StringComparison.Ordinal);

        // A frame that is not an icon or cursor file at all
        var garbage = new AniFileBuilder().AddFrame([9, 9, 9, 9, 9, 9, 9, 9]).Build();
        Assert.Equal(ImageFormat.Ani, Assert.Throws<InvalidImageContentException>(() => Image.Load(garbage)).Format);
        Assert.Equal(ImageFormat.Ani, Assert.Throws<InvalidImageContentException>(() => Image.Load(new AniFileBuilder().AddFrame([]).Build())).Format);
    }

    [Fact]
    public void AnAnimatedCursorIsNeitherStreamedNorACollection()
    {
        var data = TwoFrames().Build();
        using var stream = new MemoryStream(data);
        var reader = Assert.Throws<UnsupportedImageFeatureException>(() => Image.OpenReader<Rgba32>(stream));
        Assert.Equal(ImageFormat.Ani, reader.Format);
        Assert.Contains("Image.Load", reader.Message, StringComparison.Ordinal);

        var collection = Assert.Throws<UnsupportedImageFeatureException>(() => ImageCollection.Load(data));
        Assert.Equal(ImageFormat.Ani, collection.Format);
        Assert.Contains("animation", collection.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadingIsCancelable()
    {
        var data = TwoFrames().Build();
        using var stream = new MemoryStream(data);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Image.LoadAsync(stream, cancellationToken: new CancellationToken(canceled: true)));
    }

    private static AniFileBuilder TwoFrames() => new AniFileBuilder()
        .AddFrame(Cursor(PixelsA, hotspotX: 1, hotspotY: 0))
        .AddFrame(Cursor(PixelsB, hotspotX: 0, hotspotY: 1));

    private static byte[] Cursor(Rgba32[] pixels, int hotspotX = 0, int hotspotY = 0) => new IcoFileBuilder { Type = 2 }.Add(new IcoEntrySpec
    {
        Width = 2,
        Height = 2,
        PlanesOrHotspotX = (ushort)hotspotX,
        BitCountOrHotspotY = (ushort)hotspotY,
        Payload = IcoFileBuilder.Dib32(2, 2, pixels),
    }).Build();

    private static byte[] Icon(Rgba32[] pixels) => new IcoFileBuilder().Add(new IcoEntrySpec
    {
        Width = 2,
        Height = 2,
        Payload = IcoFileBuilder.Dib32(2, 2, pixels),
    }).Build();

    private static byte[] PngCursor(byte[] png) => new IcoFileBuilder { Type = 2 }.Add(new IcoEntrySpec
    {
        Width = 2,
        Height = 2,
        PlanesOrHotspotX = 0,
        BitCountOrHotspotY = 0,
        Payload = png,
    }).Build();

    private static byte[] LargeCursor(int seed, int side = 64)
    {
        var pixels = new Rgba32[side * side];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32((byte)(i * seed), (byte)(i >> 4), (byte)seed, 255);
        }

        return new IcoFileBuilder { Type = 2 }.Add(new IcoEntrySpec
        {
            Width = side,
            Height = side,
            PlanesOrHotspotX = 0,
            BitCountOrHotspotY = 0,
            Payload = IcoFileBuilder.Dib32(side, side, pixels),
        }).Build();
    }

    private static ImageDecodeOptions Limits(ImageResourceLimits limits) => new() { Configuration = new ImageConfiguration { Limits = limits } };

    private static FrameDuration[] Durations(Image image) => [.. image.Frames.Select(frame => frame.Metadata.Duration)];

    private static Point?[] Hotspots(Image image) => [.. image.Frames.Select(frame => frame.Metadata.Hotspot)];

    private static TPixel[] Pixels<TPixel>(Image<TPixel> image, int frame)
        where TPixel : unmanaged
    {
        var pixels = new TPixel[image.Width * image.Height];
        image.Frames[frame].CopyPixelDataTo(pixels);
        return pixels;
    }

    /// <summary>A seekable stream that counts the bytes actually read, to show what a load does not read.</summary>
    private sealed class CountingStream(byte[] data) : MemoryStream(data, writable: false)
    {
        public long BytesRead { get; private set; }

        public override int Read(Span<byte> buffer)
        {
            var read = base.Read(buffer);
            BytesRead += read;
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
    }
}
