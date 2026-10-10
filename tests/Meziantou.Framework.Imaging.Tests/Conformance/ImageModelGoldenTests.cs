using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The image model checked against the independent raw references of the golden corpus: every reference frame
/// and poster is imported with copied raw import (padded caller rows), assembled into an image with the fixture timing,
/// play count and orientation, then read back through the untyped rows, the typed rows and the strided copies, and compared
/// with <see cref="GoldenAssert"/>. No codec is involved and the image is never encoded to compare it. Every pixel type is
/// covered, with the default storage and with segmented, padded storage; clones, reordered and extracted frames and posters
/// must keep matching the fixed references while ownership is preserved.
/// </summary>
public sealed class ImageModelGoldenTests
{
    private static readonly GoldenAssertOptions NoPreviews = new() { WritePreviews = false };


    public static TheoryData<string, string, bool> RoundTripCases()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var format in PixelFormats.All)
        {
            foreach (var fixture in GoldenCorpus.Default.Fixtures.Where(fixture => fixture.IsValid && HasReference(fixture, format)))
            {
                data.Add(format.ToString(), fixture.Id, false);
                data.Add(format.ToString(), fixture.Id, true);
            }
        }

        return data;
    }

    public static TheoryData<string> AnimatedFixtures => [.. AnimatedFixtureIds];

    private static IEnumerable<string> AnimatedFixtureIds => GoldenCorpus.Default.Fixtures.Where(fixture => fixture.IsValid && (fixture.Expected.FrameCount > 1 || fixture.Expected.Poster is not null)).Select(fixture => fixture.Id);

    [Fact]
    public void CorpusCoversEveryPixelTypeAnimationsAndPosters()
    {
        foreach (var format in PixelFormats.All)
        {
            Assert.Contains(GoldenCorpus.Default.Fixtures, fixture => fixture.IsValid && HasReference(fixture, format));
        }

        Assert.Contains("apng/separate-poster", AnimatedFixtureIds);
        Assert.Contains("apng/ffmpeg-rgba16", AnimatedFixtureIds);
        Assert.Contains("gif/ffmpeg-animated", AnimatedFixtureIds);
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void ImportedReferencesReadBackExactly(string format, string id, bool segmented)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        switch (Enum.Parse<PixelFormat>(format))
        {
            case PixelFormat.Rgba32: RoundTrip<Rgba32>(fixture, segmented); break;
            case PixelFormat.Bgra32: RoundTrip<Bgra32>(fixture, segmented); break;
            case PixelFormat.Rgb24: RoundTrip<Rgb24>(fixture, segmented); break;
            case PixelFormat.Rgba64: RoundTrip<Rgba64>(fixture, segmented); break;
            case PixelFormat.Gray8: RoundTrip<Gray8>(fixture, segmented); break;
            case PixelFormat.Gray16: RoundTrip<Gray16>(fixture, segmented); break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    [Theory]
    [MemberData(nameof(AnimatedFixtures))]
    public void ClonesReorderedAndExtractedFramesKeepMatchingTheReferences(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        if (fixture.CanonicalLayout == RawPixelLayout.Rgba16Le)
        {
            EditsKeepMatching<Rgba64>(fixture);
        }
        else
        {
            EditsKeepMatching<Rgba32>(fixture);
        }
    }

    [Fact]
    public void CorruptedSampleIsReported()
    {
        var fixture = GoldenCorpus.Default.Get("png/rgba8-corner-markers");
        using var image = BuildImage<Rgba32>(fixture, segmented: true);
        var original = image.Frames[0][3, 2];
        image.Frames[0][3, 2] = new Rgba32(original.R, (byte)(original.G ^ 0x01), original.B, original.A);

        var message = Assert.Throws<GoldenAssertionException>(() => GoldenAssert.ImageMatches(fixture, ImageSnapshots.Capture(image), NoPreviews)).Message;
        Assert.Contains("Fixture 'png/rgba8-corner-markers' does not match its reference", message, StringComparison.Ordinal);
        Assert.Contains("(3,2) G: expected " + original.G.ToString(CultureInfo.InvariantCulture), message, StringComparison.Ordinal);
    }

    [Fact]
    public void CorruptedSixteenBitLowBitIsReported()
    {
        var fixture = GoldenCorpus.Default.Get("png/rgba16-low-bit-gradient");
        using var image = BuildImage<Rgba64>(fixture, segmented: false);
        var original = image.Frames[0][1, 2];
        image.Frames[0][1, 2] = new Rgba64(original.R, original.G, (ushort)(original.B ^ 1), original.A);

        var message = Assert.Throws<GoldenAssertionException>(() => GoldenAssert.FrameMatches(fixture, 0, ImageSnapshots.CaptureFrameRows(image.Frames[0]), NoPreviews)).Message;
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"0x{original.B:X4}"), message, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatedRowDataIsReported()
    {
        var fixture = GoldenCorpus.Default.Get("png/rgb8-odd-width");
        using var image = BuildImage<Rgba32>(fixture, segmented: true);
        var bytes = new byte[image.Width * image.Height * 4];
        image.Frames[0].CopyPixelBytesTo(bytes);
        GoldenAssert.FrameBytesMatch(fixture, 0, RawPixelLayout.Rgba8, bytes, NoPreviews);

        var message = Assert.Throws<GoldenAssertionException>(() => GoldenAssert.FrameBytesMatch(fixture, 0, RawPixelLayout.Rgba8, bytes.AsSpan(0, bytes.Length - 1), NoPreviews)).Message;
        Assert.Contains("truncated by 1 bytes", message, StringComparison.Ordinal);

        // A row of the wrong length cannot even be captured
        var builder = new RawPixelBufferBuilder(image.Width, image.Height, RawPixelLayout.Rgba8);
        Assert.Throws<ArgumentException>(() => builder.SetRow(0, bytes.AsSpan(0, (image.Width * 4) - 1)));
    }

    [Fact]
    public void WrongChannelOrderIsReported()
    {
        // Reading the BGRA bytes of a Bgra32 image as if they were RGBA must be caught (the adapter reorders them)
        var fixture = GoldenCorpus.Default.Get("png/rgba8-corner-markers");
        using var image = BuildImage<Bgra32>(fixture, segmented: false);
        var bytes = new byte[image.Width * image.Height * 4];
        image.Frames[0].CopyPixelBytesTo(bytes);
        var misread = RawPixelBuffer.Create(image.Width, image.Height, RawPixelLayout.Rgba8, bytes);

        var message = Assert.Throws<GoldenAssertionException>(() => GoldenAssert.FrameMatches(fixture, 0, misread, NoPreviews)).Message;
        Assert.Contains("channels R and B swapped", message, StringComparison.Ordinal);
        GoldenAssert.FrameMatches(fixture, 0, ImageSnapshots.CaptureFrame(image.Frames[0]), NoPreviews);
    }

    [Fact]
    public void MissingPosterAndWrongTimingAreReported()
    {
        var fixture = GoldenCorpus.Default.Get("apng/separate-poster");
        using var image = BuildImage<Rgba32>(fixture, segmented: false);
        image.RemovePosterFrame();
        image.Frames[1].Metadata.Duration = new FrameDuration(1, 7);

        var message = Assert.Throws<GoldenAssertionException>(() => GoldenAssert.ImageMatches(fixture, ImageSnapshots.Capture(image), NoPreviews)).Message;
        Assert.Contains("a separate poster is expected", message, StringComparison.Ordinal);
        Assert.Contains("Frame 1 duration", message, StringComparison.Ordinal);
    }

    private static void RoundTrip<TPixel>(GoldenFixture fixture, bool segmented)
        where TPixel : unmanaged
    {
        var format = PixelFormats.GetPixelFormat<TPixel>();
        using var image = BuildImage<TPixel>(fixture, segmented);
        var storage = image.Frames[0].Storage;
        if (segmented)
        {
            Assert.True(storage.SlabCount > 1 || image.Height <= 2, "The storage must be segmented");
            Assert.True(storage.Layout.Stride > storage.RowLength || storage.RowLength % 16 == 0 || image.Height == 1, "The rows must be padded");
        }
        else
        {
            Assert.Equal(1, storage.SlabCount);
        }

        var expected = fixture.Expected;
        var defaultFormat = string.Equals(expected.PixelFormat, format.ToString(), StringComparison.Ordinal);
        if (ImageSnapshots.GetLayout(format) != RawPixelLayout.Rgb8)
        {
            // Whole-image comparison: frame count, poster, animation settings, durations, orientation, pixels
            GoldenAssert.ImageMatches(fixture, ImageSnapshots.Capture(image, includePixelFormat: defaultFormat), NoPreviews);
            GoldenAssert.ImageMatches(fixture, ImageSnapshots.CaptureTyped(image, includePixelFormat: defaultFormat), NoPreviews);
        }

        // Every frame and the poster through the three read paths, exactly (no tolerance: nothing is decoded here)
        for (var i = 0; i < expected.FrameCount; i++)
        {
            var reference = GetReference(fixture, format, i);
            var frame = image.Frames[i];
            Assert.Equal(fixture.GetDuration(i), ImageSnapshots.ToRational(frame.Metadata.Duration));
            AssertExact(reference, ImageSnapshots.CaptureFrame(frame), fixture, i);
            AssertExact(reference, ImageSnapshots.CaptureFrameRows(frame), fixture, i);
            AssertExact(reference, ImageSnapshots.CaptureFrameCopy(frame, reference.RowBytes + 3), fixture, i);
            AssertExact(reference, ImageSnapshots.CaptureFrameDataCopy(frame, reference.Width + 1), fixture, i);
        }

        Assert.Equal(expected.Poster is not null, image.PosterFrame is not null);
        if (image.PosterFrame is { } poster)
        {
            AssertExact(GetReference(fixture, format, frameIndex: null), ImageSnapshots.CaptureFrameRows(poster), fixture, frameIndex: null);
        }
    }

    private static void EditsKeepMatching<TPixel>(GoldenFixture fixture)
        where TPixel : unmanaged
    {
        var format = PixelFormats.GetPixelFormat<TPixel>();
        var count = fixture.Expected.FrameCount;
        var source = BuildImage<TPixel>(fixture, segmented: true);
        try
        {
            // Clones are deep, own their storage and survive the disposal of the source
            using var clone = source.Clone();
            using var still = source.CloneFrame(count - 1);
            using var poster = source.PosterFrame is null ? null : source.ClonePosterFrame();
            using var converted = source.CloneAs<Rgba64>();

            // Reverse the frames: MoveFrame uses the final index and keeps frame identity
            var original = ((IEnumerable<ImageFrame<TPixel>>)source.Frames).ToArray();
            for (var i = 0; i < count; i++)
            {
                source.MoveFrame(count - 1, i);
            }

            for (var i = 0; i < count; i++)
            {
                Assert.Same(original[count - 1 - i], source.Frames[i]);
                GoldenAssert.FrameMatches(fixture, count - 1 - i, ImageSnapshots.CaptureFrame(source.Frames[i]), NoPreviews);
                Assert.Equal(fixture.GetDuration(count - 1 - i), ImageSnapshots.ToRational(source.Frames[i].Metadata.Duration));
            }

            // Removing a frame invalidates only that reference
            if (count > 1)
            {
                var removed = source.Frames[0];
                source.RemoveFrame(0);
                Assert.Throws<ObjectDisposedException>(() => removed.Metadata);
                GoldenAssert.FrameMatches(fixture, count - 2, ImageSnapshots.CaptureFrame(source.Frames[0]), NoPreviews);
            }

            source.Dispose();
            Assert.Throws<ObjectDisposedException>(() => original[0].Size);

            GoldenAssert.ImageMatches(fixture, ImageSnapshots.Capture(clone, includePixelFormat: false), NoPreviews);

            Assert.Single(still.Frames);
            Assert.Null(still.Animation);
            Assert.Null(still.PosterFrame);
            GoldenAssert.FrameMatches(fixture, count - 1, ImageSnapshots.CaptureFrameRows(still.Frames[0]), NoPreviews);
            Assert.Equal(fixture.GetDuration(count - 1), ImageSnapshots.ToRational(still.Frames[0].Metadata.Duration));

            if (poster is not null)
            {
                Assert.Null(poster.Animation);
                GoldenAssert.PosterMatches(fixture, ImageSnapshots.CaptureFrame(poster.Frames[0]), NoPreviews);
            }

            // The 8-bit to 16-bit conversion is exact (v * 257): compare with the widened reference
            for (var i = 0; i < count; i++)
            {
                var reference = fixture.GetFrame(i, fixture.CanonicalLayout);
                var widened = reference.Layout == RawPixelLayout.Rgba16Le ? reference : Widen(reference);
                AssertExact(widened, ImageSnapshots.CaptureFrame(converted.Frames[i]), fixture, i);
            }
        }
        finally
        {
            source.Dispose();
        }
    }

    /// <summary>Builds an image from the references: raw byte import (padded rows) for frame 0, typed import plus a cross-image copy for the other frames, then the poster.</summary>
    private static Image<TPixel> BuildImage<TPixel>(GoldenFixture fixture, bool segmented)
        where TPixel : unmanaged
    {
        var format = PixelFormats.GetPixelFormat<TPixel>();
        var expected = fixture.Expected;
        var first = GetReference(fixture, format, 0);
        var stride = first.RowBytes + 5;

        // Segmented and padded storage: rows aligned to 16 bytes, two rows per slab
        var layoutOptions = segmented ? new PixelStorageLayoutOptions { RowAlignment = 16, TargetSlabBytes = 2 * ((first.RowBytes + 15) / 16 * 16) } : null;
        var image = Image.ImportPixelBytesCore<TPixel>(RawImport.ToPixelBytes(first, format, stride), first.Width, first.Height, stride, ImageConfiguration.Default, layoutOptions);
        try
        {
            Assert.Equal(new Size(expected.Width, expected.Height), image.Size);
            image.Frames[0].Metadata.Duration = ToFrameDuration(fixture.GetDuration(0));
            for (var i = 1; i < expected.FrameCount; i++)
            {
                var reference = GetReference(fixture, format, i);
                using var single = Image.ImportPixelData<TPixel>(RawImport.ToPixelData<TPixel>(reference, reference.Width + 2), reference.Width, reference.Height, reference.Width + 2);
                single.Frames[0].Metadata.Duration = ToFrameDuration(fixture.GetDuration(i));
                image.AppendFrame(single.Frames[0]);
            }

            if (expected.Poster is not null)
            {
                using var poster = Image.ImportPixelBytes<TPixel>(RawImport.ToPixelBytes(GetReference(fixture, format, frameIndex: null), format), expected.Width, expected.Height);
                image.SetPosterFrame(poster.Frames[0]);
            }

            if (expected.Animation is not null)
            {
                image.Animation = new AnimationMetadata { TotalPlays = expected.Animation.TotalPlays };
            }

            image.Metadata.Orientation = (ExifOrientation)expected.Orientation;
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static bool HasReference(GoldenFixture fixture, PixelFormat format)
    {
        var layout = ImageSnapshots.GetLayout(format);
        if (layout == RawPixelLayout.Rgb8 && !fixture.Layouts.Contains(layout))
        {
            // Fixtures without an rgb8 reference: opaque rgba8 references are reordered (alpha byte dropped, see RawImport)
            return fixture.Layouts.Contains(RawPixelLayout.Rgba8) && fixture.Expected.Frames.Select((_, i) => i).All(i => RawImport.IsOpaque(fixture.GetFrame(i, RawPixelLayout.Rgba8)));
        }

        return fixture.Layouts.Contains(layout);
    }

    private static RawPixelBuffer GetReference(GoldenFixture fixture, PixelFormat format, int? frameIndex)
    {
        var layout = ImageSnapshots.GetLayout(format);
        var derived = layout == RawPixelLayout.Rgb8 && !fixture.Layouts.Contains(layout);
        var source = derived ? RawPixelLayout.Rgba8 : layout;
        var reference = frameIndex is { } index ? fixture.GetFrame(index, source) : fixture.GetPoster(source);
        return derived ? RawImport.DropOpaqueAlpha(reference) : reference;
    }

    private static void AssertExact(RawPixelBuffer expected, RawPixelBuffer actual, GoldenFixture fixture, int? frameIndex)
    {
        var context = frameIndex is { } index ? string.Create(CultureInfo.InvariantCulture, $"Fixture '{fixture.Id}', frame {index}") : $"Fixture '{fixture.Id}', poster";
        var result = PixelBufferComparer.Compare(expected, actual, ComparisonPolicy.Exact, context);
        if (!result.IsMatch)
            throw new GoldenAssertionException(result.Describe());
    }

    private static RawPixelBuffer Widen(RawPixelBuffer reference)
    {
        var builder = new RawPixelBufferBuilder(reference.Width, reference.Height, RawPixelLayout.Rgba16Le);
        for (var y = 0; y < reference.Height; y++)
        {
            for (var x = 0; x < reference.Width; x++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    builder.SetSample(x, y, channel, reference.GetSample(x, y, channel) * 257);
                }
            }
        }

        return builder.Build();
    }

    private static FrameDuration ToFrameDuration(RationalDuration duration) => duration.Numerator == 0 ? FrameDuration.Zero : new FrameDuration(duration.Numerator, duration.Denominator);
}
