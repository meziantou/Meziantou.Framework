using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Manifest-driven checks of the timing and metadata model. Raw pixel files carry no timing or metadata, so the
/// expectations come from the manifest (exact rational durations, total plays, orientation, resolution, profile hashes,
/// text, separate posters), and the raw encoded fields are read from the fixture inputs by the independent
/// <see cref="EncodedFieldInspector"/> — never from FFmpeg playback timestamps. The library conversions under test turn
/// those raw fields into the public model and back.
/// </summary>
public sealed class TimingAndMetadataConformanceTests
{
    public static TheoryData<string> ValidFixtures => [.. GoldenCorpus.Default.GetIds(kind: FixtureKinds.Valid)];

    public static TheoryData<string> AnimatedFixtures => [.. GoldenCorpus.Default.Fixtures.Where(fixture => fixture.IsValid && fixture.Expected.Animation is not null).Select(fixture => fixture.Id)];

    public static TheoryData<string> ExifFixtures => [.. GoldenCorpus.Default.Fixtures.Where(fixture => fixture.IsValid && fixture.Expected.Profiles.Exif is not null).Select(fixture => fixture.Id)];

    public static TheoryData<string> IccFixtures => [.. GoldenCorpus.Default.Fixtures.Where(fixture => fixture.IsValid && fixture.Expected.Profiles.Icc is not null).Select(fixture => fixture.Id)];

    public static TheoryData<string> ResolutionFixtures => [.. GoldenCorpus.Default.Fixtures.Where(fixture => fixture.IsValid && fixture.Expected.Resolution is not null).Select(fixture => fixture.Id)];

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void ManifestAgreesWithEncodedFields(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var expected = fixture.Expected;
        var fields = fixture.InspectInput();

        // Timing: every recorded delay field is the one stored in the input, in frame order (a separate poster has none)
        if (expected.Frames.Any(frame => frame.EncodedDelay is not null) || expected.Animation is not null)
        {
            Assert.HasCount(expected.FrameCount, fields.FrameDelays);
            for (var i = 0; i < expected.FrameCount; i++)
            {
                AssertSameDelay(expected.Frames[i].EncodedDelay, fields.FrameDelays[i]);
            }
        }

        if (expected.Animation is { } animation)
        {
            Assert.Equal(animation.EncodedLoopValue, fields.LoopValue);
        }

        // Metadata
        Assert.Equal(expected.Resolution?.Encoded.Unit, fields.Resolution?.Unit);
        Assert.Equal(expected.Resolution?.Encoded.X, fields.Resolution?.X);
        Assert.Equal(expected.Resolution?.Encoded.Y, fields.Resolution?.Y);
        Assert.Equal(expected.EffectiveTransferFunction, fields.TransferFunction ?? "srgb");
        AssertProfile(expected.Profiles.Icc, fields.Icc);
        AssertProfile(expected.Profiles.Exif, fields.Exif);
        AssertProfile(expected.Profiles.Xmp, fields.Xmp);
        Assert.Equal(
            expected.Text.Select(entry => (entry.Keyword, entry.Value, entry.LanguageTag, entry.TranslatedKeyword)),
            fields.Text.Select(entry => (entry.Keyword, entry.Value, entry.LanguageTag, entry.TranslatedKeyword)));

        // The harness metadata comparison accepts the inspected metadata (and pixel references) as a decoded image
        GoldenAssert.ImageMatches(fixture, GoldenCorpusTests.SnapshotFromReferences(fixture, fixture.CanonicalLayout), new GoldenAssertOptions { WritePreviews = false });
    }

    [Theory]
    [MemberData(nameof(AnimatedFixtures))]
    public void EncodedDelaysImportExactly(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var fields = fixture.InspectInput();
        for (var i = 0; i < fixture.Expected.FrameCount; i++)
        {
            var expected = ToFrameDuration(fixture.GetDuration(i));
            var raw = fields.FrameDelays[i];
            FrameDuration actual;
            if (raw is null)
            {
                actual = default; // a GIF frame without Graphic Control Extension has no delay: valid zero duration
            }
            else if (fixture.Entry.Format == "png")
            {
                actual = AnimationTiming.FromApngDelay((ushort)raw.Numerator!.Value, (ushort)raw.Denominator!.Value);
                Assert.Equal(expected, actual);

                // Exact export: the normalized fraction always fits because it is not larger than the encoded one
                var (numerator, denominator) = AnimationTiming.ToApngDelay(actual, FrameDurationRounding.RequireExact);
                Assert.Equal(actual, new FrameDuration(numerator, denominator));
                Assert.NotEqual(0, denominator);
            }
            else if (fixture.Entry.Format == "webp")
            {
                actual = AnimationTiming.FromWebPDuration(raw.Milliseconds!.Value);
                Assert.Equal(expected, actual);
                Assert.Equal(raw.Milliseconds.Value, AnimationTiming.ToWebPDuration(actual, FrameDurationRounding.RequireExact));

                // Milliseconds are whole ticks: the TimeSpan view is exact
                Assert.Equal(TimeSpan.FromMilliseconds(raw.Milliseconds.Value), actual.ToTimeSpan());
            }
            else
            {
                actual = AnimationTiming.FromGifDelay((ushort)raw.Hundredths!.Value);
                Assert.Equal(expected, actual);
                Assert.Equal(raw.Hundredths.Value, AnimationTiming.ToGifDelay(actual, FrameDurationRounding.RequireExact));

                // Hundredths are whole ticks: the TimeSpan view is exact
                Assert.Equal(TimeSpan.FromMilliseconds(raw.Hundredths.Value * 10), actual.ToTimeSpan());
            }

            Assert.Equal(expected, actual);
            Assert.Equal(fixture.GetDuration(i).Numerator, actual.Numerator);
            Assert.Equal(fixture.GetDuration(i).Denominator, actual.Denominator);
        }
    }

    [Theory]
    [MemberData(nameof(AnimatedFixtures))]
    public void EncodedLoopFieldsImportAsTotalPlays(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var animation = fixture.Expected.Animation!;
        var loop = fixture.InspectInput().LoopValue;
        if (fixture.Entry.Format == "png")
        {
            Assert.NotNull(loop);
            Assert.Equal(animation.TotalPlays, AnimationTiming.FromApngNumPlays((uint)loop.Value));
            Assert.Equal((uint)loop.Value, AnimationTiming.ToApngNumPlays(animation.TotalPlays));
        }
        else if (fixture.Entry.Format == "webp")
        {
            Assert.NotNull(loop);
            Assert.Equal(animation.TotalPlays, AnimationTiming.FromWebPLoopCount((ushort)loop.Value));
            Assert.Equal((ushort)loop.Value, AnimationTiming.ToWebPLoopCount(animation.TotalPlays));
        }
        else
        {
            Assert.Equal(animation.TotalPlays, AnimationTiming.FromGifLoopCount(loop is null ? null : (ushort)loop.Value));
            Assert.Equal(loop is null ? null : (ushort)loop.Value, AnimationTiming.ToGifLoopCount(animation.TotalPlays));
        }

        // The public container accepts every imported value (null = infinite, positive counts include the first play)
        var metadata = new AnimationMetadata { TotalPlays = animation.TotalPlays };
        Assert.Equal(animation.TotalPlays, metadata.Clone().TotalPlays);
    }

    [Fact]
    public void SeparatePosterIsNotAnAnimationFrame()
    {
        var fixture = GoldenCorpus.Default.Get("apng/separate-poster");
        Assert.True(fixture.Entry.HasFeature("apng.poster=separate"));
        Assert.NotNull(fixture.Expected.Poster);
        Assert.Null(fixture.Expected.Poster!.Duration);
        Assert.Null(fixture.Expected.Poster.EncodedDelay);

        // Only displayed frames have fcTL delays; the poster (IDAT without fcTL) is excluded from the frame count
        Assert.HasCount(fixture.Expected.FrameCount, fixture.InspectInput().FrameDelays);
        Assert.NotEqual(fixture.GetPoster(RawPixelLayout.Rgba8).ToArray(), fixture.GetFrame(0, RawPixelLayout.Rgba8).ToArray());
    }

    [Fact]
    public void CorpusCoversTimingEdgeCases()
    {
        var delays = GoldenCorpus.Default.Fixtures.Where(fixture => fixture.IsValid)
            .SelectMany(fixture => fixture.Expected.Frames.Select(frame => (fixture.Entry.Format, frame.EncodedDelay, Duration: RationalDuration.Parse(frame.Duration!))))
            .ToList();
        Assert.Contains(delays, item => item.Format == "png" && item.EncodedDelay is { Numerator: 0, Denominator: 0 }); // 0/0: den 0 means /100
        Assert.Contains(delays, item => item.Format == "png" && item.EncodedDelay is { Numerator: 3, Denominator: 30 }); // reducible fraction
        Assert.Contains(delays, item => item.Format == "png" && item.Duration == RationalDuration.Create(7, 1000));
        Assert.Contains(delays, item => item.Format == "png" && item.Duration == RationalDuration.Create(1, 3)); // not a whole number of ticks
        Assert.Contains(delays, item => item.Format == "gif" && item.EncodedDelay is { Hundredths: 0 }); // zero delay kept
        Assert.Contains(delays, item => item.Format == "gif" && item.EncodedDelay is { Hundredths: 7 });
        Assert.Contains(delays, item => item.Format == "webp" && item.EncodedDelay is { Milliseconds: 0 });
        Assert.Contains(delays, item => item.Format == "webp" && item.EncodedDelay is { Milliseconds: 7 });
        Assert.Contains(delays, item => item.Format == "webp" && item.EncodedDelay is { Milliseconds: 655350 });

        var plays = GoldenCorpus.Default.Fixtures.Where(fixture => fixture.IsValid && fixture.Expected.Animation is not null).Select(fixture => (fixture.Entry.Format, fixture.Expected.Animation!.TotalPlays)).ToList();
        Assert.Contains(plays, item => item is ("png", null));
        Assert.Contains(plays, item => item.Format == "png" && item.TotalPlays > 1);
        Assert.Contains(plays, item => item is ("gif", null));
        Assert.Contains(plays, item => item.Format == "gif" && item.TotalPlays > 1);
        Assert.Contains(plays, item => item is ("webp", null));
        Assert.Contains(plays, item => item.Format == "webp" && item.TotalPlays > 1);
    }

    [Fact]
    public void NonTickDurationRoundsToNearestTick()
    {
        var fixture = GoldenCorpus.Default.Get("apng/dispose-background-previous");
        var oneThird = ToFrameDuration(fixture.GetDuration(2));
        Assert.Equal(new FrameDuration(1, 3), oneThird);
        Assert.Equal(TimeSpan.FromTicks(3_333_333), oneThird.ToTimeSpan()); // 3,333,333.33 ticks: nearest
        Assert.NotEqual(oneThird, FrameDuration.FromTimeSpan(oneThird.ToTimeSpan())); // the TimeSpan view is lossy; FrameDuration is not
    }

    [Theory]
    [MemberData(nameof(ExifFixtures))]
    public void ExifOrientationAndPayloadArePreserved(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var exif = fixture.InspectInput().Exif!.Value.ToArray();
        Assert.True(MetadataValidation.TryValidateExif(exif, out var error), error);
        Assert.Equal((ExifOrientation)fixture.Expected.Orientation, ExifTiff.ReadOrientation(exif));

        // The container preserves the exact source bytes; the typed orientation imported from them is authoritative
        var profile = new ExifProfile(new MetadataBlob(exif));
        Assert.True(fixture.Expected.Profiles.Exif!.Matches(profile.Data.Span));
        var metadata = new ImageMetadata { ExifProfile = profile, Orientation = (ExifOrientation)fixture.Expected.Orientation };
        var clone = metadata.Clone();
        Assert.Same(profile, clone.ExifProfile);
        Assert.True(fixture.Expected.Profiles.Exif.Matches(clone.ExifProfile!.Data.Span));
    }

    [Theory]
    [MemberData(nameof(ExifFixtures))]
    public void SerializationRewritesOrientationFromTypedValue(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var exif = fixture.InspectInput().Exif!.Value.ToArray();
        var size = new Size(fixture.Expected.Width, fixture.Expected.Height);
        var metadata = new ImageMetadata { ExifProfile = new ExifProfile(new MetadataBlob(exif)), Orientation = ExifOrientation.BottomRight };

        foreach (var format in new[] { ImageFormat.Png, ImageFormat.Jpeg })
        {
            var plan = MetadataWritePlan.Create(metadata, format, MetadataHandling.Strict, size);
            Assert.Equal(ExifOrientation.BottomRight, ExifTiff.ReadOrientation(plan.Exif));
        }

        // The source profile is immutable: serialization never changes it
        Assert.True(fixture.Expected.Profiles.Exif!.Matches(metadata.ExifProfile!.Data.Span));
        Assert.Equal((ExifOrientation)fixture.Expected.Orientation, ExifTiff.ReadOrientation(metadata.ExifProfile.Data.Span));
    }

    [Fact]
    public void GeometryChangeReconcilesFixtureExifAndRemovesStaleThumbnail()
    {
        var fixture = GoldenCorpus.Default.Get("png/metadata-rgb8-profiles");
        var exif = fixture.InspectInput().Exif!.Value.ToArray();
        Assert.True(ExifTiff.HasThumbnail(exif));
        Assert.Equal((3u, 2u), ExifTiff.ReadPixelDimensions(exif));
        Assert.Contains("stale-thumbnail-placeholder!", Encoding.ASCII.GetString(exif), StringComparison.Ordinal);

        var metadata = new ImageMetadata { ExifProfile = new ExifProfile(new MetadataBlob(exif)), Orientation = ExifOrientation.LeftBottom };
        var original = metadata.ExifProfile;
        metadata.ReconcileGeometry(new Size(70_000, 2)); // the LONG/SHORT dimension tags must hold values above 65535

        Assert.NotSame(original, metadata.ExifProfile);
        var updated = metadata.ExifProfile!.Data.ToArray();
        Assert.False(ExifTiff.HasThumbnail(updated));
        Assert.DoesNotContain("stale-thumbnail-placeholder!", Encoding.ASCII.GetString(updated), StringComparison.Ordinal);
        Assert.Equal((70_000u, 2u), ExifTiff.ReadPixelDimensions(updated));
        Assert.Equal(ExifOrientation.LeftBottom, ExifTiff.ReadOrientation(updated)); // the geometry change keeps the orientation
        Assert.Contains("corpus generator", Encoding.ASCII.GetString(updated), StringComparison.Ordinal); // unrelated tags are kept
        Assert.True(MetadataValidation.TryValidateExif(updated, out _));
        Assert.True(fixture.Expected.Profiles.Exif!.Matches(original!.Data.Span)); // the previous blob is untouched
    }

    [Theory]
    [MemberData(nameof(IccFixtures))]
    public void IccProfileIsPreservedAndLabeled(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var icc = fixture.InspectInput().Icc!.Value.ToArray();
        Assert.True(MetadataValidation.TryValidateIccProfile(icc, out var error), error);
        var profile = new IccProfile(new MetadataBlob(icc));
        Assert.True(fixture.Expected.Profiles.Icc!.Matches(profile.Data.Span));
        Assert.Equal(fixture.Expected.IccProfile switch { "gray" => IccProfileColorSpace.Gray, "rgb" => IccProfileColorSpace.Rgb, _ => IccProfileColorSpace.Unknown }, profile.ColorSpace);
    }

    [Theory]
    [MemberData(nameof(ResolutionFixtures))]
    public void ResolutionConvertsExactlyFromEncodedFields(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var expected = fixture.Expected.Resolution!;
        var encoded = expected.Encoded;
        var actual = encoded.Unit == ResolutionExpectation.Meter
            ? ResolutionConversion.FromPngPhys((uint)encoded.X, (uint)encoded.Y, 1)
            : ResolutionConversion.FromJfifDensity(encoded.Unit == ResolutionExpectation.Inch ? (byte)1 : (byte)2, (ushort)encoded.X, (ushort)encoded.Y);
        Assert.NotNull(actual);
        Assert.Equal(expected.HorizontalDpi, actual.HorizontalDpi);
        Assert.Equal(expected.VerticalDpi, actual.VerticalDpi);

        // Export reproduces the encoded integers
        if (encoded.Unit == ResolutionExpectation.Meter)
        {
            Assert.Equal(((uint)encoded.X, (uint)encoded.Y), ResolutionConversion.ToPngPhys(actual));
        }
        else
        {
            Assert.Equal((encoded.Unit == ResolutionExpectation.Inch ? (byte)1 : (byte)2, (ushort)encoded.X, (ushort)encoded.Y), ResolutionConversion.ToJfifDensity(actual));
        }
    }

    [Fact]
    public void AspectRatioOnlyResolutionIsUnknown()
    {
        var fixture = GoldenCorpus.Default.Get("png/metadata-gray8-icc");
        Assert.Null(fixture.Expected.Resolution);
        Assert.True(fixture.Entry.HasFeature("png.chunk=pHYs"));
        Assert.Null(ResolutionConversion.FromPngPhys(2, 1, 0));
    }

    [Theory]
    [InlineData("png/metadata-rgb8-profiles")]
    [InlineData("jpeg/metadata-density-xmp-comment")]
    [InlineData("jpeg/exif-orientation-6")]
    public void SaveTimeMetadataPolicyUsesFixtureMetadata(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var metadata = MetadataFromFixture(fixture);
        var size = new Size(fixture.Expected.Width, fixture.Expected.Height);

        // PNG stores everything
        var png = MetadataWritePlan.Create(metadata, ImageFormat.Png, MetadataHandling.Strict, size);
        Assert.Equal(metadata.TextEntries, png.TextEntries);
        Assert.Same(metadata.IccProfile, png.IccProfile);
        Assert.Same(metadata.XmpProfile, png.XmpProfile);
        Assert.Equal(metadata.Resolution, png.Resolution);
        Assert.Equal(metadata.ExifProfile is not null || metadata.Orientation != ExifOrientation.TopLeft, png.Exif is not null);

        // GIF stores only comments: strict throws, DiscardUnsupported drops, Strip writes nothing
        var hasUnsupportedForGif = metadata.IccProfile is not null || metadata.ExifProfile is not null || metadata.XmpProfile is not null || metadata.Resolution is not null
            || metadata.Orientation != ExifOrientation.TopLeft || metadata.TextEntries.Any(entry => entry.Keyword != ImageTextEntry.CommentKeyword || entry.LanguageTag is not null);
        Assert.True(hasUnsupportedForGif);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => MetadataWritePlan.Create(metadata, ImageFormat.Gif, MetadataHandling.Strict, size));
        Assert.Equal(ImageFormat.Gif, exception.Format);
        Assert.StartsWith("Metadata: ", exception.Feature, StringComparison.Ordinal);
        var gif = MetadataWritePlan.Create(metadata, ImageFormat.Gif, MetadataHandling.DiscardUnsupported, size);
        Assert.Null(gif.IccProfile);
        Assert.Null(gif.Exif);
        Assert.Null(gif.XmpProfile);
        Assert.Null(gif.Resolution);
        Assert.All(gif.TextEntries, entry => Assert.Equal(ImageTextEntry.CommentKeyword, entry.Keyword));
        var stripped = MetadataWritePlan.Create(metadata, ImageFormat.Png, MetadataHandling.Strip, size);
        Assert.Null(stripped.Exif);
        Assert.Empty(stripped.TextEntries);

        // The source format never selects anything
        metadata.SourceFormat = ImageFormat.Gif;
        Assert.Equal(png.TextEntries, MetadataWritePlan.Create(metadata, ImageFormat.Png, MetadataHandling.Strict, size).TextEntries);
    }

    private static ImageMetadata MetadataFromFixture(GoldenFixture fixture)
    {
        var fields = fixture.InspectInput();
        var metadata = new ImageMetadata
        {
            SourceFormat = Enum.Parse<ImageFormat>(fixture.Entry.Format, ignoreCase: true),
            Orientation = (ExifOrientation)fixture.Expected.Orientation,
            IccProfile = fields.Icc is { } icc ? new IccProfile(new MetadataBlob(icc.Span)) : null,
            ExifProfile = fields.Exif is { } exif ? new ExifProfile(new MetadataBlob(exif.Span)) : null,
            XmpProfile = fields.Xmp is { } xmp ? new XmpProfile(new MetadataBlob(xmp.Span)) : null,
            Resolution = fixture.Expected.Resolution is { } resolution ? new ImageResolution(resolution.HorizontalDpi, resolution.VerticalDpi) : null,
        };
        foreach (var entry in fields.Text)
        {
            metadata.TextEntries.Add(new ImageTextEntry(entry.Keyword, entry.Value, entry.LanguageTag, entry.TranslatedKeyword));
        }

        return metadata;
    }

    private static FrameDuration ToFrameDuration(RationalDuration duration) => new(duration.Numerator, duration.Denominator);

    private static void AssertSameDelay(EncodedDelayExpectation? expected, EncodedDelayExpectation? actual)
    {
        Assert.Equal(expected?.Numerator, actual?.Numerator);
        Assert.Equal(expected?.Denominator, actual?.Denominator);
        Assert.Equal(expected?.Hundredths, actual?.Hundredths);
        Assert.Equal(expected?.Milliseconds, actual?.Milliseconds);
    }

    private static void AssertProfile(ProfileExpectation? expected, ReadOnlyMemory<byte>? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
        }
        else
        {
            Assert.NotNull(actual);
            Assert.True(expected.Matches(actual.Value.Span), $"Expected {expected}, actual {actual.Value.Length} bytes.");
        }
    }
}
