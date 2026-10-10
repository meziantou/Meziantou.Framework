using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// ICC color conversion checked against an independent color management system: the <c>colorTransforms</c> of the golden
/// corpus are colors converted by LittleCMS between ICC profiles of the corpus (see the fixtures README, "Color
/// management"). They guard the interpretation of the ICC specification, which the reference of the test harness
/// shares with the library: encodings, table order, tag selection, rendering intents.
/// </summary>
/// <remarks>
/// Tolerance: two color management systems never agree bit for bit. Each conversion declares its measured tolerance in
/// 16-bit units (257 is one level of 255) with its justification; an error of interpretation changes samples by whole
/// levels.
/// </remarks>
public sealed class ColorConversionGoldenTests
{
    private static readonly FixtureManifest Manifest = FixtureManifestLoader.Load(FixtureRoot.GetDirectory() / FixtureRoot.ManifestFileName);

    public static TheoryData<string> Cases() => [.. (Manifest.ColorTransforms ?? []).Select(transform => transform.Id)];

    [Fact]
    public void CorpusCoversTheProfileModelsAndIntents()
    {
        var profiles = Manifest.ColorProfiles ?? [];
        var features = profiles.SelectMany(profile => profile.Features ?? []).ToHashSet(StringComparer.Ordinal);
        foreach (var feature in new[] { "icc.version=2", "icc.version=4", "icc.model=matrix", "icc.model=monochrome", "icc.model=lut16", "icc.curve=parametric", "icc.curve=sampled", "icc.curve=gamma", "icc.space=gray", "icc.space=rgb", "icc.space=cmyk" })
        {
            Assert.Contains(feature, features);
        }

        var transforms = Manifest.ColorTransforms ?? [];
        Assert.Equal(ColorTransformEntry.Intents.Order(StringComparer.Ordinal), transforms.Select(transform => transform.Intent).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Contains((1, 3), transforms.Select(transform => (transform.SourceChannels, transform.DestinationChannels)));
        Assert.Contains((3, 1), transforms.Select(transform => (transform.SourceChannels, transform.DestinationChannels)));
        Assert.Contains((4, 3), transforms.Select(transform => (transform.SourceChannels, transform.DestinationChannels)));
        Assert.Contains((4, 1), transforms.Select(transform => (transform.SourceChannels, transform.DestinationChannels)));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ConversionMatchesTheIndependentColorManagementSystem(string id)
    {
        var root = FixtureRoot.GetDirectory();
        var entry = Manifest.ColorTransforms!.Single(transform => transform.Id == id);
        var source = LoadProfile(root, entry.Source);
        var destination = LoadProfile(root, entry.Destination);
        var (samples, expected) = ColorTransformVectors.Read(root, entry);

        var transform = IccColorTransform.Create(source, destination, new IccColorTransformOptions
        {
            Intent = (IccRenderingIntent)ColorTransformEntry.Intents.ToList().IndexOf(entry.Intent),
            BlackPointCompensation = entry.BlackPointCompensation,
        });
        Assert.Equal(entry.SourceChannels, transform.SourceChannelCount);
        Assert.Equal(entry.DestinationChannels, transform.DestinationChannelCount);

        var actual = new ushort[expected.Length];
        transform.Convert(samples, actual);
        var (maximum, mean, worst) = Measure(expected, actual);
        var description = string.Create(CultureInfo.InvariantCulture, $"{id}: maximum difference {maximum} (limit {entry.Comparison.MaxAbsoluteError}), mean {mean:F3} (limit {entry.Comparison.MaxMeanAbsoluteError}), worst color {worst / entry.DestinationChannels}");
        Assert.True(maximum <= entry.Comparison.MaxAbsoluteError && mean <= entry.Comparison.MaxMeanAbsoluteError, description);

        // The tolerance is not slack: an error of one level of 255 on a tenth of the samples is reported
        Assert.True(entry.Comparison.MaxMeanAbsoluteError < 25.7, description);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ToleranceIsNotWiderThanMeasured(string id)
    {
        // A tolerance several times larger than what the conversion needs would hide regressions: the declared limit must
        // stay within a factor of the measured difference (with a floor for the conversions that agree to the last unit)
        var root = FixtureRoot.GetDirectory();
        var entry = Manifest.ColorTransforms!.Single(transform => transform.Id == id);
        var (samples, expected) = ColorTransformVectors.Read(root, entry);
        var transform = IccColorTransform.Create(LoadProfile(root, entry.Source), LoadProfile(root, entry.Destination), new IccColorTransformOptions
        {
            Intent = (IccRenderingIntent)ColorTransformEntry.Intents.ToList().IndexOf(entry.Intent),
            BlackPointCompensation = entry.BlackPointCompensation,
        });

        var actual = new ushort[expected.Length];
        transform.Convert(samples, actual);
        var (maximum, _, _) = Measure(expected, actual);
        Assert.True(entry.Comparison.MaxAbsoluteError <= Math.Max(4, 2 * maximum), string.Create(CultureInfo.InvariantCulture, $"{id}: the limit {entry.Comparison.MaxAbsoluteError} is more than twice the measured maximum {maximum}"));
    }

    private static (int Maximum, double Mean, int WorstIndex) Measure(ushort[] expected, ushort[] actual)
    {
        var maximum = 0;
        var worst = 0;
        long sum = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            var difference = Math.Abs(expected[i] - actual[i]);
            sum += difference;
            if (difference > maximum)
            {
                maximum = difference;
                worst = i;
            }
        }

        return (maximum, (double)sum / expected.Length, worst);
    }

    private static IccProfile LoadProfile(FullPath root, string id)
    {
        var entry = Manifest.ColorProfiles!.Single(profile => profile.Id == id);
        return new IccProfile(new MetadataBlob(File.ReadAllBytes(root / entry.File.Path)));
    }
}
