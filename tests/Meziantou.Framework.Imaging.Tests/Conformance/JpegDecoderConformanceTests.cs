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
public sealed class JpegDecoderConformanceTests
{
    public static TheoryData<string> DecodedFixtures => [.. BaselineIds, .. ProgressiveIds];

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
        using var image = Image.Load(fixture.ReadInput()); // Gray8 or Rgb24: the profile of the fixture labels it
        var actual = Assert.Single(ImageSnapshots.Capture(image).Frames).Pixels;
        var expected = fixture.GetFrame(0, actual.Layout);
        var (max, mean, location) = Measure(expected, actual);
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

        // Alpha is exact and the decoder never exceeds three units: one IDCT rounding unit on each of the luma and chroma
        // samples, amplified by the color conversion (e.g. 1 + 1.772 for B = Y + 1.772 Cb)
        Assert.True(max <= 3, $"Maximum error {max} at {location}.");
    }

    private static IEnumerable<string> GetIds(bool progressive) => GoldenCorpus.Default.GetIds(format: "jpeg", kind: FixtureKinds.Valid).Where(id => GoldenCorpus.Default.Get(id).Entry.HasFeature("jpeg.process=progressive") == progressive);

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
