using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// ICC color conversion checked against an independent color management system: the <c>colorTransforms</c> of the golden
/// corpus are colors converted by LittleCMS between ICC profiles of the corpus (see the fixtures README, "Color
/// management"). They guard the interpretation of the ICC specification, which the reference of the test harness
/// shares with the library: encodings, table order and element order, tag selection, rendering intents, media white
/// points, black point compensation.
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
        foreach (var feature in new[] { "icc.version=2", "icc.version=4", "icc.model=matrix", "icc.model=monochrome", "icc.model=lut8", "icc.model=lut16", "icc.model=lutAToB", "icc.pcs=lab", "icc.pcs=xyz", "icc.curve=parametric", "icc.curve=sampled", "icc.curve=gamma", "icc.space=gray", "icc.space=rgb", "icc.space=cmyk" })
        {
            Assert.Contains(feature, features);
        }

        var transforms = Manifest.ColorTransforms ?? [];
        Assert.Equal(ColorTransformEntry.Intents.Order(StringComparer.Ordinal), transforms.Select(transform => transform.Intent).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        foreach (var channels in new[] { (1, 1), (1, 3), (3, 1), (3, 3), (3, 4), (4, 1), (4, 3) })
        {
            Assert.Contains(channels, transforms.Select(transform => (transform.SourceChannels, transform.DestinationChannels)));
        }

        // Every table type is used in both directions: from the device (source) and to the device (destination)
        bool Has(string profileId, string feature) => profiles.Single(profile => profile.Id == profileId).Features!.Contains(feature, StringComparer.Ordinal);
        foreach (var model in new[] { "icc.model=lut8", "icc.model=lut16", "icc.model=lutAToB" })
        {
            Assert.Contains(transforms, transform => Has(transform.Source, model));
            Assert.Contains(transforms, transform => Has(transform.Destination, model));
        }

        // To CMYK with the table of each intent; the absolute colorimetric intent with a media white point that is not the
        // illuminant on either side; black point compensation with a black point that is not black on either side
        foreach (var intent in ColorTransformEntry.Intents)
        {
            Assert.Contains(transforms, transform => transform.Intent == intent && transform.DestinationChannels == 4);
        }

        Assert.Contains(transforms, transform => transform.Intent == ColorTransformEntry.Intents[3] && Has(transform.Source, "icc.media-white"));
        Assert.Contains(transforms, transform => transform.Intent == ColorTransformEntry.Intents[3] && Has(transform.Destination, "icc.media-white"));
        Assert.Contains(transforms, transform => transform.BlackPointCompensation && Has(transform.Source, "icc.black-point"));
        Assert.Contains(transforms, transform => transform.BlackPointCompensation && Has(transform.Destination, "icc.black-point"));
        Assert.Contains(transforms, transform => transform.BlackPointCompensation && Has(transform.Destination, "icc.black-point") && Has(transform.Destination, "icc.direction=both"));
    }

    [Fact]
    public void SyntheticProfilesOfTheCorpusAreTheOnesTheTestsBuild()
    {
        // The generator and the tests compile the same builders: a change of a builder without regenerating the corpus
        // (or the reverse) is reported here, before any vector is compared with a profile it was not made for
        var builders = new Dictionary<string, Func<IccProfile>>(StringComparer.Ordinal)
        {
            ["icc/synthetic-rgb-lab-lut8"] = IccTestProfiles.RgbLabLut8,
            ["icc/synthetic-rgb-xyz-lut16"] = IccTestProfiles.RgbXyzLut16,
            ["icc/synthetic-rgb-lab-mab"] = IccTestProfiles.RgbLabLutAToB,
            ["icc/synthetic-rgb-xyz-mab"] = IccTestProfiles.RgbXyzMatrixLutAToB,
            ["icc/synthetic-cmyk-lab-lut16"] = () => IccTestProfiles.CmykLabLut16(),
            ["icc/synthetic-cmyk-lab-mab"] = IccTestProfiles.CmykLabLutAToB,
            ["icc/synthetic-gray-lab-lut8"] = IccTestProfiles.GrayLabLut8,
            ["icc/synthetic-gray-paper"] = IccTestProfiles.GrayPaper,
            ["icc/synthetic-rgb-scanner"] = IccTestProfiles.RgbScanner,
            ["icc/synthetic-gray-printer-lut8"] = IccTestProfiles.GrayPrinterLut8,
        };

        var root = FixtureRoot.GetDirectory();
        var generated = (Manifest.ColorProfiles ?? []).Where(profile => profile.Provenance.Origin == "generated").ToList();
        Assert.Equal(builders.Keys.Order(StringComparer.Ordinal), generated.Select(profile => profile.Id).Order(StringComparer.Ordinal));
        foreach (var profile in generated)
        {
            Assert.True(builders[profile.Id]().Data.Span.SequenceEqual(File.ReadAllBytes(root / profile.File.Path)), $"{profile.Id} differs from the profile built by the tests: regenerate the corpus with the icc generator.");
        }
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
