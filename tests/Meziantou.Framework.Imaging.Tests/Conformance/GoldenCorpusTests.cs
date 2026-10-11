using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Self-consistency of the committed references and of the harness, plus independent spot checks of hand-defined
/// expectations. Decoder comparisons (Image.Load + GoldenAssert) build on top of these fixtures.
/// </summary>
public sealed class GoldenCorpusTests
{
    public static TheoryData<string> ValidFixtures => [.. GoldenCorpus.Default.GetIds(kind: FixtureKinds.Valid)];

    public static TheoryData<string> ErrorFixtures => [.. GoldenCorpus.Default.Fixtures.Where(fixture => !fixture.IsValid).Select(fixture => fixture.Id)];

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void ReferencesMatchThemselvesThroughTheHarness(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        foreach (var layout in fixture.Layouts)
        {
            for (var i = 0; i < fixture.Expected.FrameCount; i++)
            {
                var reference = fixture.GetFrame(i, layout);
                GoldenAssert.FrameMatches(fixture, i, reference);
                GoldenAssert.FrameBytesMatch(fixture, i, layout, reference.Span);
            }

            if (fixture.Expected.Poster is not null)
            {
                GoldenAssert.PosterMatches(fixture, fixture.GetPoster(layout));
            }

            GoldenAssert.ImageMatches(fixture, SnapshotFromReferences(fixture, layout));
        }
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void NativeReferencesAgreeWithCanonicalReferences(string id)
    {
        // Native layouts isolate sample decoding from conversion; they must describe the same pixels as the canonical layout
        var fixture = GoldenCorpus.Default.Get(id);
        for (var i = 0; i < fixture.Expected.FrameCount; i++)
        {
            var canonical = fixture.GetFrame(i, fixture.CanonicalLayout);
            foreach (var layout in fixture.Layouts.Where(layout => layout != fixture.CanonicalLayout))
            {
                var native = fixture.GetFrame(i, layout);
                Assert.Equal(layout.BytesPerSample == 1 ? RawPixelLayout.Rgba8 : RawPixelLayout.Rgba16Le, canonical.Layout);
                var isGray = layout.Name is "gray8" or "gray16le";
                for (var y = 0; y < native.Height; y++)
                {
                    for (var x = 0; x < native.Width; x++)
                    {
                        // Gray references replicate into RGB; rgb8 references are the opaque RGBA references without alpha
                        int[] expected = isGray
                            ? [native.GetSample(x, y, 0), native.GetSample(x, y, 0), native.GetSample(x, y, 0), canonical.Layout.MaxSampleValue]
                            : [native.GetSample(x, y, 0), native.GetSample(x, y, 1), native.GetSample(x, y, 2), canonical.Layout.MaxSampleValue];
                        Assert.Equal(expected, [canonical.GetSample(x, y, 0), canonical.GetSample(x, y, 1), canonical.GetSample(x, y, 2), canonical.GetSample(x, y, 3)]);
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(ErrorFixtures))]
    public void ErrorFixturesDeclareOutcomesNotPixels(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        Assert.False(fixture.IsValid);
        Assert.Null(fixture.Entry.Expected);
        Assert.NotEmpty(fixture.ReadInput());
        Assert.Throws<InvalidOperationException>(() => fixture.Expected);
        Assert.False(string.IsNullOrEmpty(fixture.ExpectedError.Exception));
    }

    [Fact]
    public void CornerMarkersMatchTheDocumentedPattern()
    {
        // Independent restatement of pattern_corner_markers (tests/Meziantou.Framework.Imaging.Fixtures/README.md)
        var fixture = GoldenCorpus.Default.Get("png/rgba8-corner-markers");
        var frame = fixture.GetFrame(0, RawPixelLayout.Rgba8);
        Assert.Equal((5, 4), (frame.Width, frame.Height));
        Assert.Equal([255, 0, 0, 255], Pixel(frame, 0, 0));
        Assert.Equal([0, 255, 0, 255], Pixel(frame, 4, 0));
        Assert.Equal([0, 0, 255, 255], Pixel(frame, 0, 3));
        Assert.Equal([255, 255, 255, 128], Pixel(frame, 4, 3));
        Assert.Equal([(2 * 40) + 7, 50 + 3, (3 * 17) + 90, 255 - 20 - 3], Pixel(frame, 2, 1));
    }

    [Fact]
    public void Gray16ReferenceKeepsLowBits()
    {
        var fixture = GoldenCorpus.Default.Get("png/gray16-low-bit-gradient");
        var native = fixture.GetFrame(0, RawPixelLayout.Gray16Le);
        for (var y = 0; y < native.Height; y++)
        {
            for (var x = 0; x < native.Width; x++)
            {
                var value = native.GetSample(x, y, 0);
                Assert.Equal((x * 0x2003) + (y * 0x0102) + 0x0111, value);
                Assert.NotEqual(value >> 8, value & 0xFF); // not representable as v8 * 257: low bits matter
            }
        }
    }

    [Fact]
    public void SeparatePosterAnimationIsDescribedIndependently()
    {
        var fixture = GoldenCorpus.Default.Get("apng/separate-poster");
        Assert.Equal(2, fixture.Expected.FrameCount);
        Assert.Equal(3, fixture.Expected.Animation!.TotalPlays);
        Assert.Equal(RationalDuration.Create(1, 10), fixture.GetDuration(0));
        Assert.Equal(RationalDuration.Create(1, 5), fixture.GetDuration(1));

        var poster = fixture.GetPoster(RawPixelLayout.Rgba8);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                Assert.Equal([255, 255, 0, 255], Pixel(poster, x, y));
            }
        }

        // The first frame uses dispose PREVIOUS, which the APNG specification treats as BACKGROUND (transparent black)
        var second = fixture.GetFrame(1, RawPixelLayout.Rgba8);
        Assert.Equal([0, 0, 255, 255], Pixel(second, 2, 1));
        Assert.Equal([0, 0, 0, 0], Pixel(second, 0, 0));
    }

    [Theory]
    [InlineData("apng/blend-source-over", null, 3)]
    [InlineData("apng/dispose-background-previous", 2, 4)]
    [InlineData("gif/disposal-transparency", null, 4)]
    [InlineData("gif/ffmpeg-animated", 3, 3)]
    public void AnimationTimingAndPlaysAreExact(string id, int? totalPlays, int frameCount)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        Assert.Equal(frameCount, fixture.Expected.FrameCount);
        Assert.Equal(totalPlays, fixture.Expected.Animation!.TotalPlays);
        for (var i = 0; i < frameCount; i++)
        {
            Assert.True(RationalDuration.TryParse(fixture.Expected.Frames[i].Duration, out _));
        }
    }

    [Fact]
    public void DurationsAreNormalizedRationals()
    {
        var fixture = GoldenCorpus.Default.Get("apng/dispose-background-previous");
        Assert.Equal(["1/2", "1/4", "1/3", "7/1000"], fixture.Expected.Frames.Select(frame => frame.Duration!).ToArray());
        Assert.Equal(RationalDuration.Create(50, 100), fixture.GetDuration(0));
        Assert.Equal(RationalDuration.Zero, GoldenCorpus.Default.Get("apng/blend-source-over").GetDuration(2));
    }

    [Fact]
    public void ExifOrientationIsMetadataOnly()
    {
        var fixture = GoldenCorpus.Default.Get("jpeg/exif-orientation-6");
        Assert.Equal(6, fixture.Expected.Orientation);
        var frame = fixture.GetFrame(0, RawPixelLayout.Rgba8);
        Assert.Equal((8, 6), (frame.Width, frame.Height)); // stored pixels, never rotated
    }

    [Fact]
    public void LossyFixturesUseJustifiedTolerances()
    {
        foreach (var fixture in GoldenCorpus.Default.Fixtures.Where(fixture => fixture.IsValid))
        {
            var policy = fixture.Policy;

            // A lossy fixture is compared exactly only when every independent decoder agrees exactly (e.g. a single-block frame)
            var decodedIdentically = fixture.Entry.Reference?.CrossChecks?.Where(check => check.ExcludedFromTolerance != true).All(check => check.MaxAbsoluteError == 0) == true;
            Assert.Equal((fixture.Entry.Format == "jpeg" || fixture.Entry.HasFeature("webp.bitstream=vp8")) && !decodedIdentically, !policy.IsExact);
            if (!policy.IsExact)
            {
                Assert.True(policy.MaxAbsoluteError <= ComparisonPolicy.AbsoluteErrorCeiling);
                Assert.Contains("measured", policy.Justification!, StringComparison.Ordinal);
                Assert.Empty(FixtureManifestValidator.FindUndetectableDefects(fixture.GetFrame(0, RawPixelLayout.Rgba8), policy));
            }
        }
    }

    internal static DecodedImageSnapshot SnapshotFromReferences(GoldenFixture fixture, RawPixelLayout layout) => new()
    {
        Frames = [.. Enumerable.Range(0, fixture.Expected.FrameCount).Select(i => new DecodedFrameSnapshot(fixture.GetFrame(i, layout), fixture.GetDuration(i)))],
        Poster = fixture.Expected.Poster is null ? null : fixture.GetPoster(layout),
        HasAnimation = fixture.Expected.Animation is not null,
        TotalPlays = fixture.Expected.Animation?.TotalPlays,
        Orientation = fixture.Expected.Orientation,
        PixelFormat = fixture.Expected.PixelFormat,
        Metadata = MetadataFromInput(fixture),
    };

    /// <summary>Metadata read from the encoded input by the independent inspector (stands in for a decoder until the codecs exist).</summary>
    internal static DecodedMetadataSnapshot MetadataFromInput(GoldenFixture fixture)
    {
        var fields = fixture.InspectInput();
        return new DecodedMetadataSnapshot
        {
            HorizontalDpi = fields.Resolution is { } resolution ? ResolutionExpectation.ToDpi(resolution.Unit, resolution.X) : null,
            VerticalDpi = fields.Resolution is { } resolution2 ? ResolutionExpectation.ToDpi(resolution2.Unit, resolution2.Y) : null,
            TransferFunction = fields.TransferFunction ?? "srgb",
            IccProfile = fields.Icc,
            ExifProfile = fields.Exif,
            XmpProfile = fields.Xmp,
            TextEntries = [.. fields.Text.Select(entry => new DecodedTextEntry(entry.Keyword, entry.Value, entry.LanguageTag, entry.TranslatedKeyword))],
        };
    }

    private static int[] Pixel(RawPixelBuffer buffer, int x, int y) => [.. Enumerable.Range(0, buffer.Layout.ChannelCount).Select(c => buffer.GetSample(x, y, c))];
}
