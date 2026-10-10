using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Identification of every corpus input through every input variant (span, path, misleading extension,
/// seekable, non-seekable, short-read, nonzero-position streams, asynchronous path and stream) in both modes, compared with
/// the manifest expectations (which are independent of the library), separately from pixel decoding.
/// </summary>
public sealed class IdentifyConformanceTests
{
    public static TheoryData<string, InputVariant, ImageIdentifyMode> ValidCases
    {
        get
        {
            var data = new TheoryData<string, InputVariant, ImageIdentifyMode>();
            foreach (var id in GoldenCorpus.Default.GetIds(kind: FixtureKinds.Valid))
            {
                foreach (var variant in InputVariants.All)
                {
                    data.Add(id, variant, ImageIdentifyMode.Header);
                    data.Add(id, variant, ImageIdentifyMode.FullScan);
                }
            }

            return data;
        }
    }

    public static TheoryData<string, InputVariant, ImageIdentifyMode> ErrorCases
    {
        get
        {
            var data = new TheoryData<string, InputVariant, ImageIdentifyMode>();
            foreach (var fixture in GoldenCorpus.Default.Fixtures.Where(fixture => !fixture.IsValid))
            {
                foreach (var variant in InputVariants.All)
                {
                    data.Add(fixture.Id, variant, ImageIdentifyMode.Header);
                    data.Add(fixture.Id, variant, ImageIdentifyMode.FullScan);
                }
            }

            return data;
        }
    }

    public static TheoryData<string> ValidFixtures => [.. GoldenCorpus.Default.GetIds(kind: FixtureKinds.Valid)];

    [Theory]
    [MemberData(nameof(ValidCases))]
    public async Task IdentifyMatchesManifest(string id, InputVariant variant, ImageIdentifyMode mode)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var format = FixtureOptions.GetFormat(fixture);
        var options = new ImageIdentifyOptions { Mode = mode };
        var info = await InputVariants.IdentifyAsync(variant, fixture.ReadInput(), format, options, XunitCancellationToken);

        AssertCommonFields(fixture, info, mode);
        if (mode == ImageIdentifyMode.FullScan)
        {
            AssertFullScan(fixture, info);
        }
        else
        {
            AssertHeader(fixture, info);
        }

        // Every variant reports exactly the same snapshot as the in-memory span
        var reference = Image.Identify(fixture.ReadInput(), options);
        Assert.Equal(ImageInfoSnapshots.Describe(reference), ImageInfoSnapshots.Describe(info));
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void HeaderMetadataIsTheExaminedPrefixOfTheFullScan(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var data = fixture.ReadInput();
        var header = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.Header }).Metadata;
        var full = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).Metadata;

        AssertSameOrAbsent(full.IccProfile?.Data, header.IccProfile?.Data);
        AssertSameOrAbsent(full.ExifProfile?.Data, header.ExifProfile?.Data);
        AssertSameOrAbsent(full.XmpProfile?.Data, header.XmpProfile?.Data);
        if (header.Resolution is not null)
        {
            Assert.Equal(full.Resolution, header.Resolution);
        }

        Assert.HasCountLessThanOrEqual(full.TextEntries.Count, header.TextEntries);
        Assert.Equal(full.TextEntries.Take(header.TextEntries.Count), header.TextEntries);
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void IdentifyReturnsDetachedMetadata(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var data = fixture.ReadInput();
        var first = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        first.Metadata.TextEntries.Add(new ImageTextEntry("Added", "by the test"));
        first.Metadata.Orientation = ExifOrientation.BottomRight;
        if (first.Animation is not null)
        {
            first.Animation.TotalPlays = 42;
        }

        var second = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.HasCount(fixture.Expected.Text.Count, second.Metadata.TextEntries);
        Assert.Equal((ExifOrientation)fixture.Expected.Orientation, second.Metadata.Orientation);
        Assert.Equal(fixture.Expected.Animation?.TotalPlays, second.Animation?.TotalPlays);
    }

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public async Task ErrorFixturesFailExplicitly(string id, InputVariant variant, ImageIdentifyMode mode)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var format = FixtureOptions.GetFormat(fixture);
        var options = FixtureOptions.CreateIdentifyOptions(fixture, mode);
        var data = fixture.ReadInput();
        if ((mode == ImageIdentifyMode.Header && !ErrorFixtureStages.IsHeaderDefect(fixture)) || ErrorFixtureStages.IsPixelDefect(fixture))
        {
            // The defect lies after the first pixel payload (a header identification does not examine it), or inside the
            // compressed pixel data (a full scan validates the container, not the compressed payloads)
            var info = await InputVariants.IdentifyAsync(variant, data, format, options, XunitCancellationToken);
            Assert.Equal(format, info.Format);
            Assert.Equal(mode, info.IdentifyMode);
            return;
        }

        await GoldenAssert.FailsAsExpectedAsync(fixture, () => InputVariants.IdentifyAsync(variant, data, format, options, XunitCancellationToken));
    }

    [Fact]
    public void HeaderIdentificationNeverGuessesGifFrameCounts()
    {
        foreach (var fixture in GoldenCorpus.Default.Fixtures.Where(fixture => fixture.IsValid && fixture.Entry.Format == "gif"))
        {
            var info = Image.Identify(fixture.ReadInput());
            Assert.Equal(ImageIdentifyMode.Header, info.IdentifyMode);
            Assert.Null(info.FrameCount);
            Assert.NotEqual(false, info.IsAnimated);
        }
    }

    [Fact]
    public void FrameLimitOfAFullScanIsASafetyBound()
    {
        // limit/gif/frames-over-limit: 4 frames, MaxFrames = 3 fails; MaxFrames = 4 succeeds (the limit is inclusive)
        var fixture = GoldenCorpus.Default.Get("limit/gif/frames-over-limit");
        var data = fixture.ReadInput();
        var exception = Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, FixtureOptions.CreateIdentifyOptions(fixture, ImageIdentifyMode.FullScan)));
        Assert.Equal(ImageResourceLimitKind.Frames, exception.Kind);
        Assert.Equal(3, exception.Limit);
        Assert.Equal(4, exception.Requested);

        var exact = new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan, Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxFrames = 4 } } };
        Assert.Equal(4, Image.Identify(data, exact).FrameCount);
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void EncodedByteLimitIsInclusiveAndConsistentAcrossInputs(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var data = fixture.ReadInput();
        var exact = CreateEncodedByteLimitOptions(data.Length);
        var info = Image.Identify(data, exact);
        using (var stream = new MemoryStream(data))
        {
            Assert.Equal(ImageInfoSnapshots.Describe(info), ImageInfoSnapshots.Describe(Image.Identify(stream, exact)));
        }

        var tooSmall = CreateEncodedByteLimitOptions(data.Length - 1);
        var fromSpan = Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, tooSmall));
        using var shortReads = InputVariants.CreateStream(InputVariant.ShortReadStream, data);
        var fromStream = Assert.Throws<ImageResourceLimitException>(() => Image.Identify(shortReads, tooSmall));
        Assert.Equal(ImageResourceLimitKind.EncodedBytes, fromSpan.Kind);
        Assert.Equal(fromSpan.Requested, fromStream.Requested);

        // Bytes that are only delimited by the end of the input cost one discarded byte past the limit, which tells an input
        // that ends there from a longer one; nothing else is ever read past the limit
        var probe = FixtureTraits.IsDelimitedByEndOfInput(fixture) ? 1 : 0;
        Assert.True(shortReads.BytesRead <= data.Length - 1 + probe, $"{shortReads.BytesRead} bytes were read with an encoded-byte limit of {data.Length - 1}.");
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void MayHaveTransparencyCoversTheReferenceAlpha(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var info = Image.Identify(fixture.ReadInput(), new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.NotNull(info.MayHaveTransparency);
        if (HasNonOpaqueReferencePixel(fixture))
        {
            Assert.True(info.MayHaveTransparency, "The references contain non-opaque pixels.");
        }

        if (fixture.Entry.Format == "jpeg" || (!fixture.Entry.HasFeature("apng") && fixture.Expected.ColorModel is "Grayscale" or "Rgb" or "Indexed" && !fixture.Entry.HasFeature("png.chunk=tRNS") && fixture.Entry.Format == "png"))
        {
            Assert.False(info.MayHaveTransparency);
        }
    }

    private static ImageIdentifyOptions CreateEncodedByteLimitOptions(long limit)
        => new() { Mode = ImageIdentifyMode.FullScan, Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxEncodedBytes = limit } } };

    private static void AssertCommonFields(GoldenFixture fixture, ImageInfo info, ImageIdentifyMode mode)
    {
        var expected = fixture.Expected;
        Assert.Equal(FixtureOptions.GetFormat(fixture), info.Format);
        Assert.Equal(mode, info.IdentifyMode);
        Assert.Equal(expected.Width, info.Width);
        Assert.Equal(expected.Height, info.Height);
        Assert.Equal(Enum.Parse<PixelFormat>(expected.PixelFormat), info.PixelFormat);
        Assert.Equal(Enum.Parse<ImageColorModel>(expected.ColorModel!), info.ColorModel);
        Assert.Equal(expected.BitsPerComponent, info.BitsPerComponent);
        Assert.Equal(FixtureOptions.GetFormat(fixture), info.Metadata.SourceFormat);
    }

    private static void AssertFullScan(GoldenFixture fixture, ImageInfo info)
    {
        var expected = fixture.Expected;
        Assert.Equal(expected.FrameCount, info.FrameCount);
        Assert.Equal(expected.Animation is not null, info.IsAnimated);
        Assert.Equal(expected.Poster is not null, info.HasPosterFrame);
        Assert.Equal(expected.Animation is not null, info.Animation is not null);
        Assert.Equal(expected.Animation?.TotalPlays, info.Animation?.TotalPlays);
        AssertMetadata(fixture, info.Metadata);
    }

    private static void AssertHeader(GoldenFixture fixture, ImageInfo info)
    {
        var expected = fixture.Expected;
        Assert.Equal(expected.Poster is not null, info.HasPosterFrame);
        if (fixture.Entry.Format == "gif")
        {
            // A GIF header never yields a frame count; animation is only known from a preceding loop extension
            Assert.Null(info.FrameCount);
            Assert.NotEqual(false, info.IsAnimated);
            if (info.IsAnimated == true)
            {
                Assert.NotNull(expected.Animation);
                Assert.Equal(expected.Animation.TotalPlays, info.Animation?.TotalPlays);
            }
            else
            {
                Assert.Null(info.Animation);
            }

            return;
        }

        if (fixture.Entry.Format == "webp" && expected.Animation is not null)
        {
            // A WebP header (VP8X animation flag, ANIM) declares an animation and its loop count, never its frame count
            Assert.Null(info.FrameCount);
            Assert.True(info.IsAnimated);
            Assert.Equal(expected.Animation.TotalPlays, info.Animation?.TotalPlays);
            return;
        }

        // PNG (IHDR, acTL), JPEG and still WebP headers declare their frame count
        Assert.Equal(expected.FrameCount, info.FrameCount);
        Assert.Equal(expected.Animation is not null, info.IsAnimated);
        Assert.Equal(expected.Animation?.TotalPlays, info.Animation?.TotalPlays);
    }

    private static void AssertMetadata(GoldenFixture fixture, ImageMetadata metadata)
    {
        var expected = fixture.Expected;
        Assert.Equal((ExifOrientation)expected.Orientation, metadata.Orientation);
        if (expected.Resolution is null)
        {
            Assert.Null(metadata.Resolution);
        }
        else
        {
            Assert.NotNull(metadata.Resolution);
            Assert.Equal(expected.Resolution.HorizontalDpi, metadata.Resolution.HorizontalDpi);
            Assert.Equal(expected.Resolution.VerticalDpi, metadata.Resolution.VerticalDpi);
        }

        AssertProfile(expected.Profiles.Icc, metadata.IccProfile?.Data);
        AssertProfile(expected.Profiles.Exif, metadata.ExifProfile?.Data);
        AssertProfile(expected.Profiles.Xmp, metadata.XmpProfile?.Data);
        Assert.Equal(
            expected.Text.Select(entry => (entry.Keyword, entry.Value, entry.LanguageTag, entry.TranslatedKeyword)),
            metadata.TextEntries.Select(entry => (entry.Keyword, entry.Value, entry.LanguageTag, entry.TranslatedKeyword)));
    }

    private static void AssertProfile(ProfileExpectation? expected, MetadataBlob? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        Assert.True(expected.Matches(actual.Span), $"The payload ({actual.Length} bytes) does not match the expected {expected}.");
    }

    private static void AssertSameOrAbsent(MetadataBlob? full, MetadataBlob? header)
    {
        if (header is not null)
        {
            Assert.Equal(full, header);
        }
    }

    private static bool HasNonOpaqueReferencePixel(GoldenFixture fixture)
    {
        var layout = fixture.CanonicalLayout;
        var buffers = Enumerable.Range(0, fixture.Expected.FrameCount).Select(index => fixture.GetFrame(index, layout)).ToList();
        if (fixture.Expected.Poster is not null)
        {
            buffers.Add(fixture.GetPoster(layout));
        }

        foreach (var buffer in buffers)
        {
            var span = buffer.Span;
            var pixelSize = layout.BytesPerPixel;
            var alphaOffset = layout.AlphaChannel * layout.BytesPerSample;
            for (var offset = 0; offset < span.Length; offset += pixelSize)
            {
                var alpha = layout.BytesPerSample == 1 ? span[offset + alphaOffset] : span[offset + alphaOffset] | (span[offset + alphaOffset + 1] << 8);
                if (alpha != layout.MaxSampleValue)
                    return true;
            }
        }

        return false;
    }
}
