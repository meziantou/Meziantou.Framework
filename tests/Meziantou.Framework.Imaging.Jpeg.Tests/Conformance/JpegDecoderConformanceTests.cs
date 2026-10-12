using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Measured disagreement of the sequential and progressive JPEG decoder with the libjpeg-turbo djpeg references of the corpus. The
/// eager and sequential conformance suites already compare every fixture under its policy; this test pins the policies to
/// the measurement: each tolerance must be exactly the one the generator derives from the
/// library's measured maximum and mean errors (<c>LibraryJpegMeasurements</c> in the generator: max(1, measured max),
/// measured mean + 0.01 rounded up to 0.01, never above what the independent cross-checks justify), so a numerical change of
/// the decoder that loosens or tightens its agreement is noticed and reviewed instead of drifting silently.
/// </summary>
/// <remarks>
/// Four-component files (CMYK and YCCK inks) are only loaded with a color conversion: the generic suites check that they
/// are identified and rejected by a load without conversion, and this class compares the pixels of the conversion each
/// fixture declares (<c>inkConversion</c>) with their reference: the samples decoded by djpeg, converted by the decimal
/// reference of the test harness, with LittleCMS and Apple ColorSync as cross-checks. Their tolerance is pinned to the
/// measurement in the same way (<c>LibraryInkMeasurements</c> in the generator).
/// </remarks>
public sealed class JpegDecoderConformanceTests
{
    public static TheoryData<string> DecodedFixtures => [.. BaselineIds, .. ProgressiveIds];

    public static TheoryData<string> InkFixtures => [.. InkIds];

    private static IEnumerable<string> InkIds => GoldenCorpus.Default.GetIds(format: "jpeg", kind: FixtureKinds.Unsupported).Where(id => GoldenCorpus.Default.Get(id).Entry.InkConversion is not null);

    private static IEnumerable<string> BaselineIds => GetIds(progressive: false);

    private static IEnumerable<string> ProgressiveIds => GetIds(progressive: true);

    [Fact]
    public void BaselineFixturesCoverTheDecoderFeatures()
    {
        var features = BaselineIds.SelectMany(id => GoldenCorpus.Default.Get(id).Entry.Features ?? []).ToHashSet(StringComparer.Ordinal);
        foreach (var feature in new[]
        {
            "jpeg.process=baseline", "jpeg.process=extended", "jpeg.components=1", "jpeg.app14=Adobe", "jpeg.app2=ICC_PROFILE", "jpeg.app1=Exif",
            "jpeg.sampling=4:4:4", "jpeg.sampling=4:2:2", "jpeg.sampling=4:2:0", "jpeg.sampling=4:4:0", "jpeg.sampling=4:1:1",
            "jpeg.quantization=16-bit", "jpeg.scanComponents=1,1,1", "jpeg.scanComponents=1,2",
        })
        {
            Assert.Contains(feature, features);
        }

        Assert.Contains(BaselineIds, id => GoldenCorpus.Default.Get(id).Entry.HasFeature("jpeg.restartInterval=3"));

        // 12-bit samples: gray and color, with and without chroma subsampling, sequential and progressive, restart intervals
        var deep = GoldenCorpus.Default.GetIds(format: "jpeg", kind: FixtureKinds.Valid).Select(GoldenCorpus.Default.Get).Where(fixture => fixture.Entry.HasFeature("jpeg.precision=12")).ToList();
        Assert.All(deep, fixture => Assert.Equal(12, fixture.Expected.BitsPerComponent));
        foreach (var feature in new[] { "jpeg.process=extended", "jpeg.process=progressive", "jpeg.components=1", "jpeg.components=3", "jpeg.sampling=4:4:4", "jpeg.sampling=4:2:2", "jpeg.sampling=4:2:0" })
        {
            Assert.Contains(deep, fixture => fixture.Entry.HasFeature(feature));
        }

        Assert.Contains(deep, fixture => fixture.Entry.Features!.Any(feature => feature.StartsWith("jpeg.restartInterval=", StringComparison.Ordinal)));
    }

    [Fact]
    public void ArithmeticFixturesCoverTheDecoderFeatures()
    {
        // Sequential (SOF9) and progressive (SOF10) frames, gray and color, with and without chroma subsampling, restart
        // intervals (the statistics start again), first scans and refinements of the DC and AC coefficients
        var fixtures = GoldenCorpus.Default.GetIds(format: "jpeg", kind: FixtureKinds.Valid).Select(GoldenCorpus.Default.Get).Where(fixture => fixture.Entry.Features!.Any(feature => feature.StartsWith("jpeg.process=arithmetic-", StringComparison.Ordinal))).ToList();
        foreach (var process in new[] { "jpeg.process=arithmetic-sequential", "jpeg.process=arithmetic-progressive" })
        {
            var ofProcess = fixtures.Where(fixture => fixture.Entry.HasFeature(process)).ToList();
            Assert.Contains(ofProcess, fixture => fixture.Entry.HasFeature("jpeg.components=1"));
            Assert.Contains(ofProcess, fixture => fixture.Entry.HasFeature("jpeg.sampling=4:2:0"));
            Assert.Contains(ofProcess, fixture => fixture.Entry.Features!.Any(feature => feature.StartsWith("jpeg.restartInterval=", StringComparison.Ordinal)));
        }

        var features = fixtures.SelectMany(fixture => fixture.Entry.Features ?? []).ToHashSet(StringComparer.Ordinal);
        foreach (var feature in new[] { "jpeg.sampling=4:4:4", "jpeg.sampling=4:2:2", "jpeg.progressive=dc-refinement", "jpeg.progressive=ac-refinement", "jpeg.progressive=spectral-selection", "jpeg.progressive=successive-approximation" })
        {
            Assert.Contains(feature, features);
        }

        // An arithmetic-coded file has no Huffman table, and no fixture is rejected for its entropy coding any more
        Assert.DoesNotContain(GoldenCorpus.Default.GetIds(format: "jpeg", kind: FixtureKinds.Unsupported).Select(GoldenCorpus.Default.Get), fixture => fixture.Entry.Features!.Any(feature => feature.StartsWith("jpeg.process=arithmetic-", StringComparison.Ordinal)));
    }

    [Fact]
    public void ProgressiveFixturesCoverTheDecoderFeatures()
    {
        var fixtures = ProgressiveIds.Select(id => GoldenCorpus.Default.Get(id)).ToList();
        var features = fixtures.SelectMany(fixture => fixture.Entry.Features ?? []).ToHashSet(StringComparer.Ordinal);
        foreach (var feature in new[]
        {
            "jpeg.progressive=dc-refinement", "jpeg.progressive=ac-refinement", "jpeg.progressive=spectral-selection", "jpeg.progressive=successive-approximation",
            "jpeg.progressive=dc-separate", "jpeg.progressive=partial", "jpeg.components=1", "jpeg.components=3", "jpeg.app14=Adobe",
            "jpeg.sampling=4:4:4", "jpeg.sampling=4:2:2", "jpeg.sampling=4:2:0", "jpeg.sampling=4:4:0", "jpeg.sampling=4:1:1",
        })
        {
            Assert.Contains(feature, features);
        }

        // Restart intervals with color and grayscale frames, odd dimensions (partial MCUs) with and without subsampling
        Assert.Contains(fixtures, fixture => fixture.Entry.HasFeature("jpeg.components=1") && fixture.Entry.Features!.Any(feature => feature.StartsWith("jpeg.restartInterval=", StringComparison.Ordinal)));
        Assert.Contains(fixtures, fixture => fixture.Entry.HasFeature("jpeg.components=3") && fixture.Entry.Features!.Any(feature => feature.StartsWith("jpeg.restartInterval=", StringComparison.Ordinal)));
        Assert.Contains(fixtures, fixture => fixture.Entry.Expected!.Width % 2 == 1 && fixture.Entry.HasFeature("jpeg.sampling=4:2:0"));
        Assert.Contains(fixtures, fixture => fixture.Entry.Expected!.Width % 8 != 0 && fixture.Entry.Expected.Height % 8 != 0 && fixture.Entry.HasFeature("jpeg.components=1"));
    }

    [Theory]
    [MemberData(nameof(DecodedFixtures))]
    public void ToleranceIsTheMeasuredDisagreementWithTheReference(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        using var image = Image.Load(fixture.ReadInput()); // Gray8 or Rgb24, Gray16 or Rgba64 for 12-bit samples: the profile of the fixture labels it
        var actual = Assert.Single(ImageSnapshots.Capture(image).Frames).Pixels;
        var expected = fixture.GetFrame(0, actual.Layout);
        var (sampleMax, sampleMean, location) = Measure(expected, actual);

        // Like every tolerance of the corpus, the errors are on the 8-bit scale: 257 units of a 16-bit sample, the maximum
        // rounded up so that any difference counts
        var deep = fixture.Expected.BitsPerComponent == 12;
        Assert.Equal(deep, actual.Layout.BytesPerSample == 2);
        var max = deep ? (sampleMax + 256) / 257 : sampleMax;
        var mean = deep ? sampleMean / 257 : sampleMean;
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{id}: max {max} at {location}, mean {mean:F4}"));

        var policy = fixture.Policy;
        var checks = fixture.Entry.Reference?.CrossChecks?.Where(check => check.ExcludedFromTolerance != true && check.MaxAbsoluteError is not null).ToList() ?? [];
        if (policy.IsExact)
        {
            // Every independent decoder agrees exactly with the reference (e.g. a single-block frame), so must the library
            Assert.All(checks, check => Assert.Equal(0, check.MaxAbsoluteError));
            Assert.Equal(0, max);
            return;
        }

        var (justifiedMax, justifiedMean) = ComparisonPolicy.GetMaximumJustifiedTolerance(checks.Max(check => check.MaxAbsoluteError!.Value), checks.Max(check => check.MeanAbsoluteError!.Value));
        var expectedMax = Math.Min(justifiedMax, Math.Max(1, max));
        var expectedMean = Math.Min(justifiedMean, Math.Ceiling(Math.Round((Math.Round(mean, 4, MidpointRounding.AwayFromZero) + 0.01) * 100, 6, MidpointRounding.AwayFromZero)) / 100);
        Assert.True(policy.MaxAbsoluteError == expectedMax && Math.Abs(policy.MaxMeanAbsoluteError - expectedMean) < 1e-9,
            string.Create(CultureInfo.InvariantCulture, $"The policy of '{id}' (max {policy.MaxAbsoluteError}, mean {policy.MaxMeanAbsoluteError}) is not the one derived from the measured disagreement (max {max} at {location}, mean {mean:F4}: max {expectedMax}, mean {expectedMean}). Update LibraryJpegMeasurements in tools/Meziantou.Framework.Imaging.CorpusGenerator/GoldenCorpus.Jpeg.cs after reviewing the change."));

        // Alpha is exact and the decoder never exceeds three units of a sample: one IDCT rounding unit on each of the luma and
        // chroma samples, amplified by the color conversion (e.g. 1 + 1.772 for B = Y + 1.772 Cb). A unit of a 12-bit sample
        // is 65535 / 4095 units of the 16-bit sample it is scaled to
        Assert.True(sampleMax <= (deep ? (3 * 65535 / 4095) + 1 : 3), $"Maximum error {sampleMax} at {location}.");
    }

    [Fact]
    public void InkFixturesCoverTheDecoderFeatures()
    {
        var fixtures = InkIds.Select(GoldenCorpus.Default.Get).ToList();
        Assert.Contains(fixtures, fixture => fixture.InkConversion.ColorModel == "Ycck" && fixture.Entry.HasFeature("jpeg.app14=Adobe"));
        Assert.Contains(fixtures, fixture => fixture.InkConversion.ColorModel == "Cmyk" && fixture.Entry.HasFeature("jpeg.app14=Adobe"));
        Assert.Contains(fixtures, fixture => fixture.InkConversion.ColorModel == "Cmyk" && !fixture.Entry.HasFeature("jpeg.app14=Adobe"));
        Assert.Contains(fixtures, fixture => fixture.InkConversion.SourceProfile == InkConversionDefinition.Embedded && fixture.Entry.HasFeature("jpeg.app2=ICC_PROFILE"));
        Assert.Contains(fixtures, fixture => fixture.InkConversion.SourceProfile == InkConversionDefinition.Assumed && !fixture.Entry.HasFeature("jpeg.app2=ICC_PROFILE"));
        Assert.Contains(fixtures, fixture => fixture.Entry.HasFeature("jpeg.process=baseline"));
        Assert.Contains(fixtures, fixture => fixture.Entry.HasFeature("jpeg.process=progressive"));
        Assert.Contains(fixtures, fixture => fixture.Entry.Features!.Any(feature => feature.StartsWith("jpeg.restartInterval=", StringComparison.Ordinal)));
        Assert.Contains(fixtures, fixture => fixture.InkConversion.BlackPointCompensation);
        Assert.HasCountGreaterThan(1, fixtures.Select(fixture => fixture.InkConversion.Intent).Distinct(StringComparer.Ordinal).ToList());

        // Every four-component file of the corpus declares the conversion that loads it
        var inks = GoldenCorpus.Default.Fixtures.Where(fixture => fixture.Entry.HasFeature("jpeg.components=4")).Select(fixture => fixture.Id);
        Assert.Equal(InkIds.Order(StringComparer.Ordinal), inks.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheProfileAssumedForInksIsTheOneOfTheCorpus()
    {
        // The library transcribes the bytes of this file (the icc-builtin generator): the references of the fixtures
        // without profile were converted from it
        var profile = LoadProfile(InkConversionDefinition.AssumedProfileId);
        Assert.True(IccProfile.Cgats001CompatibleCmyk.Data.Span.SequenceEqual(profile.Data.Span), "IccProfile.Cgats001CompatibleCmyk differs from the profile of the corpus: run the icc-builtin generator.");
        Assert.Same(IccProfile.Cgats001CompatibleCmyk, CmykConversionOptions.Default.AssumedSourceProfile);
    }

    [Theory]
    [MemberData(nameof(InkFixtures))]
    public void InksAreConvertedLikeTheReference(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var conversion = fixture.InkConversion;
        var data = fixture.ReadInput();
        var info = Image.Identify(data);
        Assert.Equal(conversion.ColorModel, info.ColorModel.ToString());
        Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);
        Assert.Equal(conversion.SourceProfile == InkConversionDefinition.Embedded ? IccProfileColorSpace.Cmyk : null, info.Metadata.IccProfile?.ColorSpace);

        var destination = LoadProfile(conversion.DestinationProfile);
        var intent = (IccRenderingIntent)ColorTransformEntry.Intents.ToList().IndexOf(conversion.Intent);
        var options = new CmykConversionOptions { DestinationProfile = destination, Transform = new IccColorTransformOptions { Intent = intent, BlackPointCompensation = conversion.BlackPointCompensation } };
        using var image = Image.Load(data, new ImageDecodeOptions { CmykConversion = options });
        Assert.True(destination.Data.Span.SequenceEqual(image.Metadata.IccProfile!.Data.Span));
        var actual = Assert.Single(ImageSnapshots.Capture(image).Frames).Pixels;
        var expected = fixture.GetInkConversionReference();
        var (max, mean, location) = Measure(expected, actual);
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{id}: max {max} at {location}, mean {mean:F4}"));

        // The tolerance is the measured disagreement, as for the other JPEG fixtures; it stays far below the disagreement
        // of the independent color management systems recorded as cross-checks
        var policy = fixture.InkConversionPolicy;
        var expectedMean = Math.Ceiling(Math.Round((Math.Round(mean, 4, MidpointRounding.AwayFromZero) + 0.01) * 100, 6, MidpointRounding.AwayFromZero)) / 100;
        var pinned = max == 0 ? policy.IsExact : !policy.IsExact && policy.MaxAbsoluteError == max && Math.Abs(policy.MaxMeanAbsoluteError - expectedMean) < 1e-9;
        Assert.True(pinned, string.Create(CultureInfo.InvariantCulture, $"The policy of '{id}' is not the one derived from the measured disagreement (max {max} at {location}, mean {mean:F4}). Update LibraryInkMeasurements in tools/Meziantou.Framework.Imaging.CorpusGenerator/GoldenCorpus.JpegInks.cs after reviewing the change."));
        Assert.True(PixelBufferComparer.Matches(expected, actual, policy));
        Assert.True(max <= 3, $"Maximum error {max} at {location}.");

        // A sequential reader converts the rows like an eager load
        using var reader = Image.OpenReader<Rgb24>(fixture.OpenInput(), new ImageReaderOptions { CmykConversion = options, LeaveOpen = false });
        Assert.Equal(conversion.ColorModel, reader.Info.ColorModel.ToString());
        using var frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.True(destination.Data.Span.SequenceEqual(frame.Metadata.IccProfile!.Data.Span));
        Assert.Equal(actual.ToArray(), Assert.Single(ImageSnapshots.Capture(frame).Frames).Pixels.ToArray());
    }

    private static IccProfile LoadProfile(string id)
    {
        var entry = GoldenCorpus.Default.Manifest.ColorProfiles!.Single(profile => profile.Id == id);
        return new IccProfile(new MetadataBlob(File.ReadAllBytes(FixtureRoot.GetDirectory() / entry.File.Path)));
    }

    // Progressive frames, Huffman coded (SOF2) or arithmetic coded (SOF10); the others are sequential
    private static IEnumerable<string> GetIds(bool progressive) => GoldenCorpus.Default.GetIds(format: "jpeg", kind: FixtureKinds.Valid).Where(id => (GoldenCorpus.Default.Get(id).Entry.HasFeature("jpeg.process=progressive") || GoldenCorpus.Default.Get(id).Entry.HasFeature("jpeg.process=arithmetic-progressive")) == progressive);

    private static (int Max, double Mean, string Location) Measure(RawPixelBuffer expected, RawPixelBuffer actual)
    {
        Assert.Equal(expected.Layout, actual.Layout);
        Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height));
        var channels = expected.Layout.ChannelCount;
        var max = 0;
        var sum = 0L;
        var count = 0;
        var location = "(none)";
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                for (var c = 0; c < channels; c++)
                {
                    var difference = Math.Abs(expected.GetSample(x, y, c) - actual.GetSample(x, y, c));
                    if (c == 3)
                    {
                        Assert.Equal(0, difference);
                        continue;
                    }

                    sum += difference;
                    count++;
                    if (difference > max)
                    {
                        max = difference;
                        location = string.Create(CultureInfo.InvariantCulture, $"({x}, {y}) channel {c}");
                    }
                }
            }
        }

        return (max, (double)sum / count, location);
    }
}
