using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Gif;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// GIF encoding through the public save and writer APIs. Output is validated with the independent harness reader
/// (<see cref="ReferenceGif"/>: strict block structure, its own LZW decoder and compositor) through
/// <see cref="GifOutputVerifier"/> (structure, full-canvas frames, disposal, transparency, timing, plays, comments, exact
/// pixels for exactly representable palettes, measured error otherwise), and the encoded delay/loop fields with the
/// independent container walker (<see cref="EncodedFieldInspector"/>). The library decoder is a supplementary round trip only;
/// FFmpeg and Apple ImageIO decode the same kinds of output in the interoperability tests.
/// </summary>
public sealed class GifEncoderTests
{
    private static readonly PixelFormat[] AllFormats = [PixelFormat.Rgba32, PixelFormat.Bgra32, PixelFormat.Rgb24, PixelFormat.Rgba64, PixelFormat.Gray8, PixelFormat.Gray16];

    public static TheoryData<PixelFormat, bool, bool> FormatCases()
    {
        var data = new TheoryData<PixelFormat, bool, bool>();
        foreach (var format in AllFormats)
        {
            data.Add(format, false, false);
            data.Add(format, true, false);
            data.Add(format, false, true);
            data.Add(format, true, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FormatCases))]
    public void ExactlyRepresentableAnimationsDecodeExactly(PixelFormat format, bool interlaced, bool dithering)
    {
        // Every frame has at most 255 colors (plus transparency for alpha formats): no quantization, so the displayed frames
        // must be reproduced exactly, including regions that become transparent after an opaque frame
        var encoder = new GifEncoder { Interlaced = interlaced, Dithering = dithering ? GifDithering.FloydSteinberg : GifDithering.None };
        foreach (var (width, height) in new[] { (1, 1), (7, 5), (8, 8), (13, 11), (3, 17), (40, 2) })
        {
            AssertExact(format, width, height, encoder);
        }

        static void AssertExact(PixelFormat format, int width, int height, GifEncoder encoder)
        {
            using var image = CreateAnimation(format, width, height, frameCount: 4, colorsPerFrame: 200, seed: width + (31 * height));
            var gif = Encode(image, encoder);
            var verification = GifOutputVerifier.Verify(gif, image, encoder, context: $"{format} {width}x{height}");
            Assert.All(verification.Errors, error => Assert.Null(error));
            AssertRoundTripsThroughLibraryDecoder(gif, verification);
        }
    }

    [Fact]
    public void PixelsBecomingTransparentNeverShowPreviousContent()
    {
        // Opaque, then partially transparent, then fully opaque, then fully transparent, then transparent where the previous
        // frame was opaque and opaque where it was transparent: each displayed frame is exactly its own pixels
        using var image = new Image<Rgba32>(9, 7);
        Fill(image.Frames[0], (x, y) => new Rgba32((byte)(x * 20), (byte)(y * 30), 200, 255));
        Fill(image.AppendFrame(), (x, y) => x < 4 ? default : new Rgba32(10, 20, 30, 255));
        Fill(image.AppendFrame(), (x, y) => new Rgba32(250, (byte)(x + y), 0, 255));
        Fill(image.AppendFrame(), (x, y) => new Rgba32(1, 2, 3, 0));
        Fill(image.AppendFrame(), (x, y) => ((x + y) & 1) == 0 ? new Rgba32(9, 9, 9, 255) : new Rgba32(255, 255, 255, 10));
        Fill(image.AppendFrame(), (x, y) => ((x + y) & 1) == 1 ? new Rgba32(9, 9, 9, 255) : new Rgba32(255, 255, 255, 10));
        foreach (var interlaced in new[] { false, true })
        {
            var encoder = new GifEncoder { Interlaced = interlaced };
            var gif = Encode(image, encoder);
            var verification = GifOutputVerifier.Verify(gif, image, encoder);
            Assert.Equal([false, true, false, true, true, true], verification.Reference.Images.Select(item => item.Control!.HasTransparency));
            Assert.All(verification.Reference.Images, item => Assert.Equal(2, item.Control!.DisposalMethod));
            AssertRoundTripsThroughLibraryDecoder(gif, verification);
        }
    }

    [Fact]
    public void EachFrameGetsItsOwnLocalPalette()
    {
        // Two frames of 200 disjoint colors each (400 in total): both exact, with different local color tables
        using var image = CreateAnimation(PixelFormat.Rgba32, 40, 20, frameCount: 2, colorsPerFrame: 200, seed: 3, transparent: false);
        var gif = Encode(image, new GifEncoder());
        var verification = GifOutputVerifier.Verify(gif, image, new GifEncoder());
        Assert.All(verification.Errors, error => Assert.Null(error));
        var tables = verification.Reference.Images.Select(item => item.LocalColorTable!.Value.ToArray()).ToList();
        Assert.HasCount(256 * 3, tables[0]);
        Assert.NotEqual(tables[0], tables[1]);
        var colors = verification.Intents.SelectMany(Colors).ToHashSet();
        Assert.HasCountGreaterThan(256, colors);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(16)]
    [InlineData(255)]
    [InlineData(256)]
    public void MaxColorsBoundsThePaletteAndKeepsExactPalettesThatFit(int maxColors)
    {
        var encoder = new GifEncoder { MaxColors = maxColors };

        // Exactly maxColors opaque colors: exact; one more: quantized to maxColors entries. With transparency the transparent
        // entry takes a slot: maxColors - 1 colors are exact, maxColors are quantized
        Check(maxColors, transparent: false, exact: true);
        Check(maxColors + 1, transparent: false, exact: false);
        Check(maxColors - 1, transparent: true, exact: true);
        Check(maxColors, transparent: true, exact: false);

        void Check(int colors, bool transparent, bool exact)
        {
            using var image = Distinct(colors, transparent);
            var verification = GifOutputVerifier.Verify(Encode(image, encoder), image, encoder, context: $"{colors} colors, transparency {transparent}");
            Assert.Equal(exact, verification.Errors[0] is null);
            Assert.Equal(transparent, verification.Reference.Images[0].Control?.HasTransparency ?? false);
            Assert.HasCountLessThanOrEqual(maxColors, verification.Reference.Images[0].Indices.ToArray().Distinct());
        }
    }

    [Fact]
    public void QuantizationAndDitheringAreDeterministicAndBounded()
    {
        using var image = Photo(64, 48, seed: 5);
        foreach (var dithering in new[] { GifDithering.None, GifDithering.FloydSteinberg })
        {
            foreach (var interlaced in new[] { false, true })
            {
                var encoder = new GifEncoder { Dithering = dithering, Interlaced = interlaced };
                var gif = Encode(image, encoder);
                Assert.Equal(gif, Encode(image, encoder));

                // A writer to a non-seekable stream produces the same bytes as an eager save
                using var stream = new TestOutputStream();
                using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(image.Width, image.Height) { Encoder = encoder, ExpectedFrameCount = 1 }))
                {
                    writer.WriteFrame(((Image<Rgba32>)image).Frames[0]);
                    writer.Complete();
                }

                Assert.Equal(gif, stream.ToArray());
                var error = GifOutputVerifier.Verify(gif, image, encoder).Errors[0]!;
                Assert.True(error.Psnr > (dithering == GifDithering.None ? 30 : 28) && error.MaxChannelBias < 1, $"{dithering}: {error.Describe()}");
            }
        }

        // Interlacing changes only the row order of the datastream: the same indices are decoded
        var plain = ReferenceGif.Parse(Encode(image, new GifEncoder { Dithering = GifDithering.FloydSteinberg }));
        var interlacedGif = ReferenceGif.Parse(Encode(image, new GifEncoder { Dithering = GifDithering.FloydSteinberg, Interlaced = true }));
        Assert.Equal(plain.Images[0].Indices.ToArray(), interlacedGif.Images[0].Indices.ToArray());
        Assert.True(interlacedGif.Images[0].Interlaced);
    }

    [Fact]
    public void FloydSteinbergDitheringPreservesTheLocalAverageColor()
    {
        // A smooth gradient reduced to 4 colors: without dithering, bands; with error diffusion, the average over 8x8 blocks
        // stays close to the source
        using var image = new Image<Rgba32>(64, 64);
        Fill(image.Frames[0], (x, y) => new Rgba32((byte)(x * 4), (byte)(y * 4), 128, 255));
        var none = BlockError(image, new GifEncoder { MaxColors = 4 });
        var dithered = BlockError(image, new GifEncoder { MaxColors = 4, Dithering = GifDithering.FloydSteinberg });
        Assert.True(dithered < none * 0.7, $"block error {dithered:F2} with dithering, {none:F2} without");

        // Exact palettes are never dithered (the error is zero everywhere)
        using var exact = Distinct(100, transparent: true);
        Assert.Equal(Encode(exact, new GifEncoder()), Encode(exact, new GifEncoder { Dithering = GifDithering.FloydSteinberg }));
    }

    [Fact]
    public void AlphaThresholdSelectsTheTransparentPixels()
    {
        using var image = new Image<Rgba32>(5, 1);
        byte[] alphas = [0, 1, 127, 128, 255];
        Fill(image.Frames[0], (x, _) => new Rgba32(10, 20, 30, alphas[x]));
        Assert.Equal(new byte[] { 0, 0, 0, 255, 255 }, DecodeAlpha(Encode(image, new GifEncoder())));
        Assert.Equal(new byte[] { 0, 255, 255, 255, 255 }, DecodeAlpha(Encode(image, new GifEncoder { AlphaThreshold = 1 })));
        Assert.Equal(new byte[] { 255, 255, 255, 255, 255 }, DecodeAlpha(Encode(image, new GifEncoder { AlphaThreshold = 0 })));
        Assert.Equal(new byte[] { 0, 0, 0, 0, 255 }, DecodeAlpha(Encode(image, new GifEncoder { AlphaThreshold = 255 })));

        // 16-bit alpha is compared after the nearest reduction to 8 bits: 0x7FFF is 127, 0x8000 is 128
        using var wide = new Image<Rgba64>(3, 1);
        ushort[] wideAlphas = [0x7FFF, 0x8000, 0x8080];
        wide.Frames[0].ProcessPixelRows(pixels =>
        {
            var row = pixels.GetRowSpan(0);
            for (var x = 0; x < 3; x++)
            {
                row[x] = new Rgba64(0x1234, 0x5678, 0x9ABC, wideAlphas[x]);
            }
        });
        var gif = Encode(wide, new GifEncoder());
        Assert.Equal(new byte[] { 0, 255, 255 }, DecodeAlpha(gif));
        GifOutputVerifier.Verify(gif, wide, new GifEncoder());
    }

    [Fact]
    public void FlattenCompositesOverTheBackgroundAndRequiresIt()
    {
        var background = new Rgba64(0x2000, 0x8000, 0xFFFF);
        foreach (var format in AllFormats)
        {
            AssertFlattened(format, background);
        }

        // Without a background, Flatten fails before any output (even for opaque pixels)
        using var opaque = Distinct(3, transparent: false);
        var missing = new GifEncoder { AlphaMode = GifAlphaMode.Flatten };
        Assert.Throws<ArgumentException>(() => opaque.Save(new ThrowingStream(), missing));
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgba32>(new ThrowingStream(), new ImageWriterOptions(2, 2) { Encoder = missing }));
        Assert.Throws<ArgumentException>(() => new GifEncoder { BackgroundColor = new Rgba64(1, 2, 3, 4) });

        static void AssertFlattened(PixelFormat format, Rgba64 background)
        {
            // The second frame of the generated animation has a transparent region (alpha formats)
            using var image = CreateAnimation(format, 6, 4, frameCount: 2, colorsPerFrame: 20, seed: 9);
            image.RemoveFrame(0);
            image.Animation = null;
            image.Frames[0].Metadata.Duration = FrameDuration.Zero;
            var encoder = new GifEncoder { AlphaMode = GifAlphaMode.Flatten, BackgroundColor = background };
            var gif = Encode(image, encoder);
            var verification = GifOutputVerifier.Verify(gif, image, encoder, context: format.ToString());
            Assert.Null(verification.Reference.Images[0].Control); // opaque still image, no delay: no Graphic Control Extension

            // The flattened colors are those of the shared converter
            using var flattened = image.CloneAs<Rgb24>(new PixelConversionOptions { BackgroundColor = background });
            using var decoded = Image.Load<Rgb24>(gif);
            Assert.Equal(StreamImages.GetBytes(flattened.Frames[0]), StreamImages.GetBytes(decoded.Frames[0]));
        }
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 100, 1)]
    [InlineData(1, 10, 10)]
    [InlineData(13, 4, 325)]
    [InlineData(65535, 100, 65535)]
    public void ExactDurationsAreWrittenAsHundredths(long numerator, long denominator, int hundredths)
    {
        using var image = CreateAnimation(PixelFormat.Rgba32, 3, 2, frameCount: 2, colorsPerFrame: 4, seed: 1);
        image.Frames[1].Metadata.Duration = new FrameDuration(numerator, denominator);
        foreach (var rounding in new[] { FrameDurationRounding.RequireExact, FrameDurationRounding.RoundToNearest })
        {
            var encoder = new GifEncoder { DurationRounding = rounding };
            var gif = Encode(image, encoder);
            GifOutputVerifier.Verify(gif, image, encoder);
            Assert.Equal(hundredths, EncodedFieldInspector.Inspect("gif", gif).FrameDelays[1]!.Hundredths);
            using var decoded = Image.Load(gif);
            Assert.Equal(new FrameDuration(numerator, denominator), decoded.Frames[1].Metadata.Duration);
        }
    }

    [Theory]
    [InlineData(1, 3, 33)]
    [InlineData(2, 3, 67)]
    [InlineData(1, 200, 1)] // half a hundredth: ties up
    [InlineData(1, 201, 0)]
    [InlineData(1, 1000, 0)]
    [InlineData(655_354, 1000, 65535)]
    public void InexactDurationsRoundToTheNearestHundredthByDefault(long numerator, long denominator, int hundredths)
    {
        using var image = CreateAnimation(PixelFormat.Rgba32, 3, 2, frameCount: 2, colorsPerFrame: 4, seed: 1);
        image.Frames[0].Metadata.Duration = new FrameDuration(numerator, denominator);
        var gif = Encode(image, new GifEncoder());
        Assert.Equal(hundredths, EncodedFieldInspector.Inspect("gif", gif).FrameDelays[0]!.Hundredths);
        Assert.Equal(hundredths, GifOutputVerifier.GetNearestDelay(new FrameDuration(numerator, denominator)));
        GifOutputVerifier.Verify(gif, image, new GifEncoder());

        // RequireExact rejects them before any output
        Assert.Equal("GIF frame duration precision", Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(new ThrowingStream(), new GifEncoder { DurationRounding = FrameDurationRounding.RequireExact })).Feature);
    }

    [Theory]
    [InlineData(655_355, 1000)] // rounds to 65,536 hundredths
    [InlineData(656, 1)]
    [InlineData(long.MaxValue, 1)]
    public void OutOfRangeDurationsThrowInsteadOfClamping(long numerator, long denominator)
    {
        using var image = CreateAnimation(PixelFormat.Rgba32, 3, 2, frameCount: 2, colorsPerFrame: 4, seed: 1);
        image.Frames[1].Metadata.Duration = new FrameDuration(numerator, denominator);
        foreach (var rounding in new[] { FrameDurationRounding.RequireExact, FrameDurationRounding.RoundToNearest })
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(new ThrowingStream(), new GifEncoder { DurationRounding = rounding }));
            Assert.Equal(numerator * 100 % denominator == 0 || rounding == FrameDurationRounding.RoundToNearest ? "GIF frame duration range" : "GIF frame duration precision", exception.Feature);
        }
    }

    [Fact]
    public void UnrepresentableDurationsKeepTheWriterUsable()
    {
        using var frames = CreateAnimation(PixelFormat.Rgba32, 4, 3, frameCount: 2, colorsPerFrame: 8, seed: 2);
        using var stream = new TestOutputStream();
        using var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(4, 3) { Encoder = new GifEncoder { DurationRounding = FrameDurationRounding.RequireExact } });
        var frame = ((Image<Rgba32>)frames).Frames[0];
        frame.Metadata.Duration = new FrameDuration(1, 3);
        Assert.Throws<UnsupportedImageFeatureException>(() => writer.WriteFrame(frame));
        Assert.Equal(0, stream.BytesWritten);
        frame.Metadata.Duration = new FrameDuration(1, 4);
        writer.WriteFrame(frame);
        writer.WriteFrame(((Image<Rgba32>)frames).Frames[1]);
        writer.Complete();
        Assert.Equal([25, (int)GifOutputVerifier.GetNearestDelay(frames.Frames[1].Metadata.Duration)], EncodedFieldInspector.Inspect("gif", stream.ToArray()).FrameDelays.Select(delay => delay!.Hundredths!.Value));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(1, null)]
    [InlineData(2, 1)]
    [InlineData(10, 9)]
    [InlineData(65_536, 65_535)]
    public void TotalPlaysMapToTheNetscapeLoopCount(int? totalPlays, int? loopCount)
    {
        using var image = CreateAnimation(PixelFormat.Rgba32, 3, 2, frameCount: 2, colorsPerFrame: 4, seed: 1);
        image.Animation = new AnimationMetadata { TotalPlays = totalPlays };
        var gif = Encode(image, new GifEncoder());
        Assert.Equal(loopCount, EncodedFieldInspector.Inspect("gif", gif).LoopValue);
        GifOutputVerifier.Verify(gif, image, new GifEncoder());
        using var decoded = Image.Load(gif);
        Assert.Equal(totalPlays, decoded.Animation!.TotalPlays);

        // More than 65,536 plays cannot be represented (never clamped)
        image.Animation.TotalPlays = 65_537;
        Assert.Equal("GIF play count", Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(new ThrowingStream(), new GifEncoder())).Feature);
    }

    [Fact]
    public void StillImagesAndSingleFrameAnimationsAreDistinguished()
    {
        // A still image: no loop extension, disposal 0, a Graphic Control Extension only for transparency or a delay
        using var still = Distinct(5, transparent: false);
        var gif = Encode(still, new GifEncoder());
        var reference = GifOutputVerifier.Verify(gif, still, new GifEncoder()).Reference;
        Assert.Equal(["Image"], reference.Blocks);
        using (var decoded = Image.Load(gif))
        {
            Assert.False(decoded.IsAnimated);
        }

        still.Frames[0].Metadata.Duration = new FrameDuration(1, 2);
        reference = GifOutputVerifier.Verify(Encode(still, new GifEncoder()), still, new GifEncoder()).Reference;
        Assert.Equal(["GCE", "Image"], reference.Blocks);
        Assert.Equal(0, reference.Images[0].Control!.DisposalMethod);

        // A one-frame animation keeps its (infinite) loop extension and is decoded as an animation
        still.Animation = new AnimationMetadata();
        gif = Encode(still, new GifEncoder());
        reference = GifOutputVerifier.Verify(gif, still, new GifEncoder()).Reference;
        Assert.Equal(["Loop", "GCE", "Image"], reference.Blocks);
        using (var decoded = Image.Load(gif))
        {
            Assert.True(decoded.IsAnimated);
            Assert.Null(decoded.Animation!.TotalPlays);
        }

        // An unknown-count writer is an animation (it may receive several frames): default infinite loop
        using var stream = new TestOutputStream();
        using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(still.Width, still.Height) { Encoder = new GifEncoder() }))
        {
            writer.WriteFrame(((Image<Rgba32>)still).Frames[0]);
            writer.Complete();
        }

        Assert.Equal(0, EncodedFieldInspector.Inspect("gif", stream.ToArray()).LoopValue);
    }

    [Fact]
    public void CommentsAreTheOnlyMetadataAndFollowThePolicy()
    {
        using var image = CreateAnimation(PixelFormat.Rgba32, 3, 2, frameCount: 2, colorsPerFrame: 4, seed: 1);
        var longComment = new string('é', 600); // Latin-1, several sub-blocks
        image.Metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, "first"));
        image.Metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, ""));
        image.Metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, longComment));
        var gif = Encode(image, new GifEncoder());
        var reference = GifOutputVerifier.Verify(gif, image, new GifEncoder()).Reference;
        Assert.Equal(["first", "", longComment], reference.Comments);
        Assert.Equal(["Loop", "Comment", "Comment", "Comment", "GCE", "Image", "GCE", "Image"], reference.Blocks);
        using (var decoded = Image.Load(gif))
        {
            Assert.Equal(["first", "", longComment], decoded.Metadata.TextEntries.Select(entry => entry.Value));
        }

        // Comments that would not read back (not Latin-1, keywords other than Comment) and every other kind of metadata
        // follow MetadataHandling: Strict throws before any output, DiscardUnsupported drops them, Strip writes nothing
        image.Metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, "snow ☃"));
        Assert.Equal("Metadata: text entry 'Comment'", Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(new ThrowingStream(), new GifEncoder())).Feature);
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>"u8));
        image.Metadata.Resolution = new ImageResolution(72, 72);
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(new ThrowingStream(), new GifEncoder()));
        Assert.Equal(["first", "", longComment], ReferenceGif.Parse(Encode(image, new GifEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported })).Comments);
        Assert.Empty(ReferenceGif.Parse(Encode(image, new GifEncoder { MetadataHandling = MetadataHandling.Strip })).Comments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownFrameCountsStreamWithMemoryIndependentOfTheFrameCount(bool asynchronous)
    {
        const int Width = 96;
        const int Height = 64;
        var peaks = new List<long>();
        using var frames = CreateAnimation(PixelFormat.Rgba32, Width, Height, frameCount: 3, colorsPerFrame: 255, seed: 4);
        Fill(((Image<Rgba32>)frames).Frames[2], (x, y) => new Rgba32((byte)(x * 2), (byte)(y * 3), (byte)((x * y) & 0xFF), (x & 7) == 0 ? (byte)0 : (byte)255)); // quantized
        foreach (var count in new[] { 2, 30 })
        {
            using var stream = new TestOutputStream { ForbidSynchronousWrites = asynchronous, ForbidAsynchronousWrites = !asynchronous };
            var options = new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder { Dithering = GifDithering.FloydSteinberg, Interlaced = true } };
            using (var writer = Image.CreateWriter<Rgba32>(stream, options))
            {
                var previous = 0L;
                for (var i = 0; i < count; i++)
                {
                    var frame = ((Image<Rgba32>)frames).Frames[i % 3];
                    if (asynchronous)
                    {
                        await writer.WriteFrameAsync(frame, XunitCancellationToken);
                    }
                    else
                    {
                        writer.WriteFrame(frame);
                    }

                    // Each frame reaches the destination when its call returns
                    Assert.True(stream.BytesWritten - previous > 100, $"frame {i}: {stream.BytesWritten - previous} bytes written");
                    previous = stream.BytesWritten;
                }

                if (asynchronous)
                {
                    await writer.CompleteAsync(XunitCancellationToken);
                }
                else
                {
                    writer.Complete();
                }

                peaks.Add(writer.Core.Scope.GetDiagnostics().PeakLiveBytes);
                Assert.Equal(0, writer.Core.Scope.LiveBytes);
            }

            var reference = ReferenceGif.Parse(stream.ToArray());
            Assert.Equal(count, reference.Images.Count);
            var displayed = reference.DecodeDisplayedFrames();
            for (var i = 0; i < count; i++)
            {
                var intent = GifOutputVerifier.ToIntent(frames.Frames[i % 3], new GifEncoder());
                if (i % 3 != 2)
                {
                    Assert.True(PixelBufferComparer.Matches(intent, displayed[i], ComparisonPolicy.Exact), $"frame {i}");
                }
            }
        }

        // The working memory (rows, histogram, string table, index plane) never depends on the number of frames
        Assert.Equal(peaks[0], peaks[1]);
        Assert.True(peaks[1] < 4L * 1024 * 1024, $"Peak writer memory {peaks[1]} bytes.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(40)]
    [InlineData(800)]
    [InlineData(5_000)]
    [InlineData(9_000)]
    public async Task FailingStreamsFaultTheWriterAndReleaseItsState(long failAt)
    {
        using var image = Photo(64, 64, seed: 12);
        image.AppendFrame(image.Frames[0]);
        image.AppendFrame(image.Frames[0]);
        Assert.HasCountGreaterThan(9_000, Encode(image, new GifEncoder()));
        foreach (var asynchronous in new[] { false, true })
        {
            using var stream = new TestOutputStream { FailAtPosition = failAt };
            var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(64, 64) { Encoder = new GifEncoder() });
            try
            {
                if (asynchronous)
                {
                    await Assert.ThrowsAsync<InjectedIOException>(async () =>
                    {
                        foreach (var frame in ((Image<Rgba32>)image).Frames)
                        {
                            await writer.WriteFrameAsync(frame, XunitCancellationToken);
                        }

                        await writer.CompleteAsync(XunitCancellationToken);
                    });
                }
                else
                {
                    Assert.Throws<InjectedIOException>(() =>
                    {
                        foreach (var frame in ((Image<Rgba32>)image).Frames)
                        {
                            writer.WriteFrame(frame);
                        }

                        writer.Complete();
                    });
                }

                Assert.Equal(0, writer.Core.Scope.LiveBytes);
                Assert.Throws<InvalidOperationException>(() => writer.WriteFrame(((Image<Rgba32>)image).Frames[0])); // faulted
                Assert.True(stream.BytesWritten <= failAt);
            }
            finally
            {
                writer.Dispose();
            }

            Assert.False(stream.IsDisposed);
        }
    }

    [Fact]
    public async Task CancellationDuringALaterFrameFaultsTheWriter()
    {
        using var image = Photo(512, 384, seed: 13);
        image.AppendFrame(image.Frames[0]);
        int firstFrameLength;
        using (var first = image.CloneFrame(0))
        {
            firstFrameLength = Encode(first, new GifEncoder()).Length;
        }

        using var source = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        using var stream = new TestOutputStream { CancellationSource = source, CancelAtPosition = firstFrameLength + 1_000 };
        await using var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(512, 384) { Encoder = new GifEncoder() });
        await writer.WriteFrameAsync(((Image<Rgba32>)image).Frames[0], source.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteFrameAsync(((Image<Rgba32>)image).Frames[1], source.Token).AsTask());
        Assert.Equal(0, writer.Core.Scope.LiveBytes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync(XunitCancellationToken).AsTask());
    }

    [Fact]
    public async Task FailedPathSavesPreserveTheDestinationAndPublishNothing()
    {
        var directory = ImageWriterTests.CreateDirectory();
        try
        {
            var path = directory / "out.gif";
            await File.WriteAllBytesAsync(path, "previous"u8.ToArray(), XunitCancellationToken);
            using var image = CreateAnimation(PixelFormat.Rgba32, 8, 8, frameCount: 3, colorsPerFrame: 30, seed: 14);

            // Late failure: frames 0-1 were written to the temporary file, then frame 2 cannot be leased
            image.Frames[2].ProcessPixelBytes(_ => Assert.Throws<InvalidOperationException>(() => image.Save(path)));

            // Early failures: unrepresentable duration, pre-canceled token, missing flatten background
            image.Frames[1].Metadata.Duration = new FrameDuration(700, 1);
            Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(path));
            image.Frames[1].Metadata.Duration = new FrameDuration(1, 100);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.SaveAsync(path, cancellationToken: new CancellationToken(canceled: true)));
            Assert.Throws<ArgumentException>(() => image.Save(path, new GifEncoder { AlphaMode = GifAlphaMode.Flatten }));
            Assert.Equal("previous"u8.ToArray(), await File.ReadAllBytesAsync(path, XunitCancellationToken));
            Assert.Equal(["out.gif"], Directory.GetFileSystemEntries(directory).Select(Path.GetFileName));

            // An uncompleted writer publishes nothing either
            using (var writer = Image.CreateWriter<Rgba32>(path, new ImageWriterOptions(8, 8)))
            {
                writer.WriteFrame(((Image<Rgba32>)image).Frames[0]);
                writer.WriteFrame(((Image<Rgba32>)image).Frames[1]);
            }

            Assert.Equal("previous"u8.ToArray(), await File.ReadAllBytesAsync(path, XunitCancellationToken));
            Assert.Equal(["out.gif"], Directory.GetFileSystemEntries(directory).Select(Path.GetFileName));

            await image.SaveAsync(path, cancellationToken: XunitCancellationToken);
            GifOutputVerifier.Verify(await File.ReadAllBytesAsync(path, XunitCancellationToken), image, new GifEncoder());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EditedDecodedAnimationsSaveTheIntendedDisplayedFrames()
    {
        // A decoded GIF re-encoded after edits: its own frame structure never leaks into the output
        using var source = CreateAnimation(PixelFormat.Rgba32, 10, 6, frameCount: 4, colorsPerFrame: 40, seed: 15);
        using var decoded = Image.Load(Encode(source, new GifEncoder()));
        decoded.MoveFrame(3, 0);
        decoded.RemoveFrame(2);
        Fill(((Image<Rgba32>)decoded).Frames[1], (x, y) => x > 5 ? default : new Rgba32(1, 2, 3, 255));
        decoded.Resize(new ResizeOptions(7, 9) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.NearestNeighbor }, XunitCancellationToken);
        var gif = Encode(decoded, new GifEncoder());
        var verification = GifOutputVerifier.Verify(gif, decoded, new GifEncoder());
        Assert.All(verification.Errors, error => Assert.Null(error));
    }

    [Theory]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    [InlineData(2, 1)]
    [InlineData(2, 70_000)]
    [InlineData(4, 70_000)]
    [InlineData(16, 100_000)]
    [InlineData(256, 100_000)]
    [InlineData(256, 3)]
    public void LzwDatastreamsDecodeWithBothDecoders(int entries, int length)
    {
        // Random and highly repetitive index sequences: code size growth, full tables and clear codes
        var random = new Random(entries + length);
        foreach (var repetitive in new[] { false, true })
        {
            var indices = new byte[length];
            for (var i = 0; i < length; i++)
            {
                indices[i] = (byte)(repetitive ? (i / 37) % entries : random.Next(entries));
            }

            var codeSize = GifLzwEncoder.GetMinimumCodeSize(entries);
            var scope = new AllocationScope(new ImageResourceLimits());
            byte[] blocks;
            using (var output = new ImageOutputBuffer(scope))
            using (var encoder = new GifLzwEncoder(scope))
            {
                encoder.Reset(codeSize);
                for (var offset = 0; offset < length; offset += 1000)
                {
                    encoder.Write(indices.AsSpan(offset, Math.Min(1000, length - offset)), output);
                }

                encoder.Finish(output);
                blocks = output.WrittenMemory.ToArray();
            }

            // The sub-blocks: at most 255 bytes each, then the terminator
            var data = new List<byte>();
            var position = 0;
            while (blocks[position] != 0)
            {
                data.AddRange(blocks.AsSpan(position + 1, blocks[position]).ToArray());
                position += 1 + blocks[position];
            }

            Assert.Equal(blocks.Length - 1, position);

            // The library decoder
            using (var decoder = new GifLzwDecoder(scope))
            {
                decoder.Reset(codeSize);
                ReadOnlySpan<byte> input = data.ToArray();
                var decoded = new byte[length];
                var count = decoder.Decode(ref input, decoded);
                Assert.Equal(length, count);
                Assert.Equal(indices, decoded);
                decoder.ReadTrailer(ref input);
                Assert.True(decoder.IsEnded);
            }

            Assert.Equal(0, scope.LiveBytes);
        }
    }

    [Fact]
    public void InterlacedRowOrderFollowsTheFourPasses()
    {
        for (var height = 1; height <= 20; height++)
        {
            var expected = new[] { (0, 8), (4, 8), (2, 4), (1, 2) }.SelectMany(pass => Enumerable.Range(0, height).Where(y => y >= pass.Item1 && (y - pass.Item1) % pass.Item2 == 0)).ToArray();
            Assert.Equal(expected, Enumerable.Range(0, height).Select(index => GifEncoderCodec.GetInterlacedRow(index, height)));
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void QuantizerPalettesAreSortedBoundedAndExactWhenTheyFit()
    {
        var scope = new AllocationScope(new ImageResourceLimits());
        using var quantizer = new GifQuantizer(scope);
        var random = new Random(7);
        var keys = Enumerable.Range(0, 5000).Select(_ => random.Next(1 << 24)).ToArray();
        foreach (var target in new[] { 1, 2, 17, 255, 256 })
        {
            quantizer.Begin(256);
            quantizer.Add(keys);
            quantizer.BuildPalette(target);
            Assert.False(quantizer.IsExact);
            Assert.InRange(quantizer.PaletteCount, 1, target);
            Assert.Equal(quantizer.Palette.ToArray().Order(), quantizer.Palette.ToArray());
            Assert.HasCount(quantizer.Palette.Length, quantizer.Palette.ToArray().Distinct());
        }

        quantizer.Begin(256);
        quantizer.Add(keys.AsSpan(0, 200));
        quantizer.Add([GifQuantizer.TransparentKey]);
        quantizer.BuildPalette(255);
        Assert.True(quantizer.IsExact && quantizer.HasTransparency);
        Assert.Equal(keys.Take(200).Distinct().Order(), quantizer.Palette.ToArray());
        foreach (var key in keys.Take(200))
        {
            Assert.Equal(key, quantizer.Palette[quantizer.Map(key)]);
        }

        // Too many exactly known colors for the requested palette (the transparent slot): median cut on the exact colors
        quantizer.Begin(256);
        quantizer.Add(keys.AsSpan(0, 256));
        quantizer.BuildPalette(255);
        Assert.False(quantizer.IsExact);
        Assert.InRange(quantizer.PaletteCount, 200, 255);
    }

    private static byte[] Encode(Image image, GifEncoder encoder)
    {
        using var stream = new TestOutputStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    private static void AssertRoundTripsThroughLibraryDecoder(byte[] gif, GifVerification verification)
    {
        // Supplementary only: the library decoder must show the same displayed frames as the reference reader
        using var decoded = Image.Load<Rgba32>(gif);
        Assert.Equal(verification.Displayed.Count, decoded.Frames.Count);
        for (var i = 0; i < decoded.Frames.Count; i++)
        {
            var result = PixelBufferComparer.Compare(verification.Displayed[i], ImageSnapshots.CaptureFrame(decoded.Frames[i]), ComparisonPolicy.Exact, $"library decoder, frame {i}");
            Assert.True(result.IsMatch, result.Describe());
        }
    }

    private static byte[] DecodeAlpha(byte[] gif)
    {
        var frame = ReferenceGif.Parse(gif).DecodeDisplayedFrames()[0];
        return [.. Enumerable.Range(0, frame.Width).Select(x => (byte)frame.GetSample(x, 0, 3))];
    }

    private static IEnumerable<int> Colors(RawPixelBuffer rgba)
    {
        for (var i = 0; i < rgba.Span.Length; i += 4)
        {
            if (rgba.Span[i + 3] != 0)
                yield return (rgba.Span[i] << 16) | (rgba.Span[i + 1] << 8) | rgba.Span[i + 2];
        }
    }

    private static double BlockError(Image<Rgba32> image, GifEncoder encoder)
    {
        var decoded = ReferenceGif.Parse(Encode(image, encoder)).DecodeDisplayedFrames()[0];
        var source = ImageSnapshots.CaptureFrame(image.Frames[0]);
        var total = 0d;
        for (var by = 0; by < image.Height; by += 8)
        {
            for (var bx = 0; bx < image.Width; bx += 8)
            {
                for (var c = 0; c < 3; c++)
                {
                    var difference = 0d;
                    for (var y = by; y < by + 8; y++)
                    {
                        for (var x = bx; x < bx + 8; x++)
                        {
                            difference += decoded.GetSample(x, y, c) - source.GetSample(x, y, c);
                        }
                    }

                    total += Math.Abs(difference / 64);
                }
            }
        }

        return total / (image.Width / 8 * (image.Height / 8) * 3);
    }

    private static void Fill(ImageFrame<Rgba32> frame, Func<int, int, Rgba32> color)
    {
        frame.ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = color(x, y);
                }
            }
        });
    }

    /// <summary>An image with exactly <paramref name="count"/> distinct opaque colors (and transparent pixels when requested).</summary>
    private static Image<Rgba32> Distinct(int count, bool transparent)
    {
        var width = 17;
        var height = (count + (transparent ? 1 : 0) + width - 1) / width;
        var image = new Image<Rgba32>(width, Math.Max(1, height));
        Fill(image.Frames[0], (x, y) =>
        {
            var index = (y * width) + x;
            if (transparent && index == count)
                return default;

            index %= count;
            return new Rgba32((byte)(index * 37), (byte)(index * 11 / 3), (byte)(255 - index), 255);
        });

        return image;
    }

    /// <summary>A photo-like opaque image with many colors (smooth gradients plus seeded noise).</summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static Image<Rgba32> Photo(int width, int height, int seed)
    {
        var random = new Random(seed);
        var image = new Image<Rgba32>(width, height);
        Fill(image.Frames[0], (x, y) =>
        {
            var r = (128 + (100 * Math.Sin((x + seed) / 7d))) + random.Next(-6, 7);
            var g = (255d * y / height) + random.Next(-6, 7);
            var b = (128 + (90 * Math.Cos((x + y) / 11d))) + random.Next(-6, 7);
            return new Rgba32((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255), 255);
        });

        return image;
    }

    /// <summary>
    /// A seeded animation in a pixel format: each frame uses at most <paramref name="colorsPerFrame"/> colors drawn from its
    /// own set (8-bit values, widened exactly for 16-bit formats), and for alpha formats a moving transparent rectangle (alpha
    /// 0 or a value below the default threshold, hidden colors varying) when <paramref name="transparent"/> is set; durations
    /// in hundredths, infinite play.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static Image CreateAnimation(PixelFormat format, int width, int height, int frameCount, int colorsPerFrame, int seed, bool transparent = true)
    {
        var random = new Random(seed);
        using var wide = new Image<Rgba64>(width, height);
        for (var i = 1; i < frameCount; i++)
        {
            wide.AppendFrame();
        }

        var hasAlpha = PixelFormats.HasAlpha(format) && transparent;
        for (var i = 0; i < frameCount; i++)
        {
            var palette = Enumerable.Range(0, colorsPerFrame).Select(_ => (R: random.Next(256), G: random.Next(256), B: random.Next(256))).ToArray();
            var (x0, y0) = (random.Next(width), random.Next(height));
            var (x1, y1) = (x0 + random.Next(1, width + 1), y0 + random.Next(1, height + 1));
            var frame = wide.Frames[i];
            frame.Metadata.Duration = new FrameDuration(random.Next(0, 200), 100);
            frame.ProcessPixelRows(pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var (r, g, b) = palette[random.Next(palette.Length)];
                        var clear = hasAlpha && i % 2 == 1 && x >= x0 && x < x1 && y >= y0 && y < y1;
                        var alpha = clear ? (random.Next(2) == 0 ? 0 : 100) : 255;
                        row[x] = new Rgba64((ushort)(r * 257), (ushort)(g * 257), (ushort)(b * 257), (ushort)(alpha * 257));
                    }
                }
            });
        }

        wide.Animation = new AnimationMetadata();
        return format switch
        {
            PixelFormat.Rgba32 => wide.CloneAs<Rgba32>(),
            PixelFormat.Bgra32 => wide.CloneAs<Bgra32>(),
            PixelFormat.Rgb24 => wide.CloneAs<Rgb24>(),
            PixelFormat.Rgba64 => wide.Clone(),
            PixelFormat.Gray8 => wide.CloneAs<Gray8>(),
            _ => wide.CloneAs<Gray16>(),
        };
    }
}
