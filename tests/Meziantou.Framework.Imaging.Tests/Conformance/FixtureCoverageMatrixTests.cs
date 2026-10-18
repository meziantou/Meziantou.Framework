using System.Globalization;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Xunit.Sdk;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The fixture-to-feature coverage matrix (tests/Meziantou.Framework.Imaging.Fixtures/README.md) must match the committed manifest: every
/// count is recomputed, every row has fixtures of the expected kind, and every feature key of the manifest is mapped.
/// The tables of the AV1 and of the HEVC conformance vectors are verified the same way against the <c>av1Vectors</c> and
/// the <c>hevcVectors</c> sections.
/// </summary>
public sealed class FixtureCoverageMatrixTests
{
    private static readonly string[] Kinds = [FixtureKinds.Valid, FixtureKinds.Invalid, FixtureKinds.Unsupported, FixtureKinds.Limit];

    private static readonly FullPath MatrixPath = FixtureRoot.GetDirectory() / "README.md";

    [Fact]
    public void FormatCountsMatchTheManifest()
    {
        var rows = ReadTable("## Fixtures per format and kind");
        var fixtures = GoldenCorpus.Default.Fixtures.Select(fixture => fixture.Entry).ToList();

        var expected = fixtures.Select(entry => entry.Format).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(format => FormatRow(format, Count(fixtures.Where(entry => entry.Format == format))))
            .ToList();
        AssertRows(expected, rows.Select(row => FormatRow(row[0], [.. row[1..].Select(ParseCount)])).OrderBy(row => row, StringComparer.Ordinal).ToList());

        static string FormatRow(string format, int[] counts) => $"| {format} | {string.Join(" | ", counts)} |";
    }

    [Fact]
    public void FeatureRowsMatchTheManifest()
    {
        var fixtures = GoldenCorpus.Default.Fixtures.Select(fixture => fixture.Entry).ToList();
        var rows = ReadTable("## Features");
        Assert.NotEmpty(rows);

        var expected = new List<string>();
        var actual = new List<string>();
        foreach (var row in rows)
        {
            var name = row[0];
            var selector = ParseSelector(row[1]);
            var counts = Count(fixtures.Where(entry => Matches(entry, selector)));
            expected.Add(FeatureRow(name, selector, counts));
            actual.Add(FeatureRow(name, selector, [.. row[2..].Select(ParseCount)]));

            var required = name.EndsWith("(rejected)", StringComparison.Ordinal) ? FixtureKinds.Unsupported : FixtureKinds.Valid;
            Assert.True(counts[Array.IndexOf(Kinds, required)] > 0, $"Feature '{name}' has no {required} fixture.");
        }

        AssertRows(expected, actual);

        static string FeatureRow(string name, string[] selector, int[] counts)
            => $"| {name} | {string.Join(" + ", selector.Select(tag => $"`{tag}`"))} | {string.Join(" | ", counts)} |";
    }

    [Fact]
    public void EveryManifestFeatureKeyIsMapped()
    {
        var mapped = ReadTable("## Features").SelectMany(row => ParseSelector(row[1])).Select(GetKey).ToHashSet(StringComparer.Ordinal);
        var keys = GoldenCorpus.Default.Fixtures.SelectMany(fixture => fixture.Entry.Features ?? []).Select(GetKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        Assert.All(keys, key => Assert.Contains(key, mapped)); // a manifest feature key is missing from the fixtures README
    }

    [Fact]
    public void Av1VectorFeatureRowsMatchTheManifest()
    {
        var vectors = GoldenCorpus.Default.Av1Vectors.ToList();
        var rows = ReadTable("### AV1 vector features");
        Assert.NotEmpty(rows);

        var expected = new List<string>();
        var actual = new List<string>();
        foreach (var row in rows)
        {
            var selector = ParseSelector(row[1]);
            var count = vectors.Count(vector => Matches(format: null, vector.Features, selector));
            expected.Add(VectorRow(row[0], selector, count));
            actual.Add(VectorRow(row[0], selector, ParseCount(row[2])));
            Assert.True(count > 0, $"AV1 feature '{row[0]}' has no vector.");
        }

        AssertRows(expected, actual);

        static string VectorRow(string name, string[] selector, int count)
            => $"| {name} | {string.Join(" + ", selector.Select(tag => $"`{tag}`"))} | {count} |";
    }

    [Fact]
    public void EveryAv1VectorFeatureKeyIsMapped()
    {
        var mapped = ReadTable("### AV1 vector features").SelectMany(row => ParseSelector(row[1])).Select(GetKey).ToHashSet(StringComparer.Ordinal);
        var keys = GoldenCorpus.Default.Av1Vectors.SelectMany(vector => vector.Features).Select(GetKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        Assert.All(keys, key => Assert.Contains(key, mapped)); // a feature key of the av1Vectors section is missing from the fixtures README
    }

    [Fact]
    public void HevcVectorFeatureRowsMatchTheManifest()
    {
        var vectors = GoldenCorpus.Default.HevcVectors.ToList();
        var rows = ReadTable("### HEVC vector features");
        Assert.NotEmpty(rows);

        var expected = new List<string>();
        var actual = new List<string>();
        foreach (var row in rows)
        {
            var selector = ParseSelector(row[1]);
            var count = vectors.Count(vector => Matches(format: null, vector.Features, selector));
            expected.Add(VectorRow(row[0], selector, count));
            actual.Add(VectorRow(row[0], selector, ParseCount(row[2])));
            Assert.True(count > 0, $"HEVC feature '{row[0]}' has no vector.");
        }

        AssertRows(expected, actual);

        static string VectorRow(string name, string[] selector, int count)
            => $"| {name} | {string.Join(" + ", selector.Select(tag => $"`{tag}`"))} | {count} |";
    }

    [Fact]
    public void EveryHevcVectorFeatureKeyIsMapped()
    {
        var mapped = ReadTable("### HEVC vector features").SelectMany(row => ParseSelector(row[1])).Select(GetKey).ToHashSet(StringComparer.Ordinal);
        var keys = GoldenCorpus.Default.HevcVectors.SelectMany(vector => vector.Features).Select(GetKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        Assert.All(keys, key => Assert.Contains(key, mapped)); // a feature key of the hevcVectors section is missing from the fixtures README

        // The table only uses the vocabulary of the validator, and every key of the vocabulary that a vector has is in it
        Assert.All(mapped, key => Assert.Contains(key, FixtureManifestValidator.AllowedHevcFeatureKeys));
        Assert.All(keys, key => Assert.Contains(key, FixtureManifestValidator.AllowedHevcFeatureKeys));
    }

    private static void AssertRows(List<string> expected, List<string> actual)
    {
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
            throw new XunitException($"The coverage matrix of tests/Meziantou.Framework.Imaging.Fixtures/README.md is stale. Expected rows:{Environment.NewLine}{string.Join(Environment.NewLine, expected)}");
    }

    private static int[] Count(IEnumerable<FixtureEntry> entries)
    {
        var list = entries.ToList();
        return [.. Kinds.Select(kind => list.Count(entry => entry.Kind == kind))];
    }

    private static bool Matches(FixtureEntry entry, string[] selector) => Matches(entry.Format, entry.Features ?? [], selector);

    private static bool Matches(string? format, IReadOnlyList<string> features, string[] selector)
        => selector.All(tag =>
        {
            var key = GetKey(tag);
            var separator = tag.IndexOf('=', StringComparison.Ordinal);
            if (key == "format")
                return format == tag[(separator + 1)..];

            return features.Any(feature =>
            {
                if (GetKey(feature) != key)
                    return false;

                if (separator < 0)
                    return true;

                var featureSeparator = feature.IndexOf('=', StringComparison.Ordinal);
                return featureSeparator >= 0 && feature[(featureSeparator + 1)..].Split(',').Contains(tag[(separator + 1)..], StringComparer.Ordinal);
            });
        });

    private static string GetKey(string tag)
    {
        var separator = tag.IndexOf('=', StringComparison.Ordinal);
        return separator < 0 ? tag : tag[..separator];
    }

    private static string[] ParseSelector(string cell)
        => [.. cell.Split(" + ", StringSplitOptions.TrimEntries).Select(tag => tag.Trim('`'))];

    private static int ParseCount(string cell) => int.Parse(cell, NumberStyles.None, CultureInfo.InvariantCulture);

    /// <summary>Reads the body rows of the first table after a heading, as trimmed cells.</summary>
    private static List<string[]> ReadTable(string heading)
    {
        var lines = File.ReadAllLines(MatrixPath);
        var start = Array.IndexOf(lines, heading);
        Assert.True(start >= 0, $"Heading '{heading}' not found in {MatrixPath}.");

        var rows = new List<string[]>();
        var index = start + 1;
        while (index < lines.Length && !lines[index].StartsWith("|", StringComparison.Ordinal))
            index++;

        for (index += 2; index < lines.Length && lines[index].StartsWith("|", StringComparison.Ordinal); index++) // skip the header and separator rows
        {
            rows.Add([.. lines[index].Trim().Trim('|').Split('|').Select(cell => cell.Trim())]);
        }

        return rows;
    }
}
