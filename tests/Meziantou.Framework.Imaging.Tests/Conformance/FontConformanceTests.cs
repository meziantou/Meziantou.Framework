using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The fonts of the corpus against the values the corpus generator recorded from two independent font readers,
/// HarfBuzz and FreeType (see the Fonts section of the fixtures README).
/// </summary>
public sealed class FontConformanceTests
{
    /// <summary>
    /// The largest difference between a coordinate of an instance and the one the reference reader writes, in font
    /// units: its numbers have 24 bits of precision and three decimals.
    /// </summary>
    private const double InstanceTolerance = 0.005;

    public static TheoryData<string> FontsWithVectors()
    {
        var data = new TheoryData<string>();
        foreach (var font in LoadFonts())
        {
            if (font.Vectors is not null || font.Original is not null)
            {
                data.Add(font.Id);
            }
        }

        return data;
    }

    public static TheoryData<string> Containers()
    {
        var data = new TheoryData<string>();
        foreach (var font in LoadFonts())
        {
            if (font.Original is not null)
            {
                data.Add(font.Id);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FontsWithVectors))]
    public void TheFacesMatchTheRecordedValues(string id)
    {
        var root = FixtureRoot.GetDirectory();
        var entry = LoadFonts().Single(font => font.Id == id);

        // A WOFF or WOFF2 file is the font it was made from: it has the values recorded for that font.
        var vectorsFile = entry.Vectors ?? LoadFonts().Single(font => font.Id == entry.Original).Vectors;
        var vectors = FontVectors.Load(root / vectorsFile!.Path);
        var collection = FontCollection.Load(root / entry.File.Path);

        Assert.HasCount(entry.FaceCount, collection);
        for (var faceIndex = 0; faceIndex < collection.Count; faceIndex++)
        {
            var font = collection[faceIndex];
            var expected = vectors.Faces[faceIndex];
            // The reference reader gives the names of the family and of the style (the name identifiers 1 and 2); the
            // font prefers the typographic names (16 and 17) when it has them.
            Assert.Equal(expected.Family, font.Face.GetName(NameTable.FamilyName) ?? "");
            Assert.Equal(expected.Subfamily, font.Face.GetName(NameTable.SubfamilyName) ?? "");
            Assert.Equal(font.Face.GetName(NameTable.TypographicFamilyName) ?? expected.Family, font.FamilyName);
            Assert.Equal(font.Face.GetName(NameTable.TypographicSubfamilyName) ?? expected.Subfamily, font.SubfamilyName);
            Assert.Equal(expected.PostScriptName, font.PostScriptName);
            Assert.Equal(expected.UnitsPerEm, font.UnitsPerEm);
            Assert.Equal(expected.GlyphCount, font.GlyphCount);

            // The metrics, by OpenType metric tag; the descent and the underline position are distances below the baseline.
            var metrics = font.Metrics;
            AssertMetric(expected, "hasc", metrics.Ascent);
            AssertMetric(expected, "hdsc", -metrics.Descent);
            AssertMetric(expected, "hlgp", metrics.LineGap);
            AssertMetric(expected, "xhgt", metrics.XHeight);
            AssertMetric(expected, "cpht", metrics.CapHeight);
            AssertMetric(expected, "unds", metrics.UnderlineThickness);
            AssertMetric(expected, "undo", -metrics.UnderlinePosition);
            AssertMetric(expected, "strs", metrics.StrikethroughThickness);
            AssertMetric(expected, "stro", metrics.StrikethroughPosition);

            // The character map: every recorded character, and no other one.
            var mapped = 0;
            for (var codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
            {
                var glyph = font.Face.MapCodePoint(codePoint);
                if (expected.CharacterMap.TryGetValue(codePoint.ToString("X4", CultureInfo.InvariantCulture), out var expectedGlyph))
                {
                    mapped++;
                    if (glyph != expectedGlyph)
                    {
                        Assert.Fail($"{id}: U+{codePoint:X4} maps to the glyph {glyph}, expected {expectedGlyph}");
                    }
                }
                else if (glyph != 0)
                {
                    Assert.Fail($"{id}: U+{codePoint:X4} maps to the glyph {glyph} but the font does not map it");
                }
            }

            Assert.Equal(expected.CharacterMap.Count, mapped);

            for (var glyph = 0; glyph < font.GlyphCount; glyph++)
            {
                if (font.Face.GetAdvanceWidth(glyph) != expected.Advances[glyph])
                {
                    Assert.Fail($"{id}: the glyph {glyph} has the advance {font.Face.GetAdvanceWidth(glyph)}, expected {expected.Advances[glyph]}");
                }

                var outline = FontTestSupport.GetOutline(font, glyph);
                if (!string.Equals(outline, expected.Outlines[glyph], StringComparison.Ordinal))
                {
                    Assert.Fail($"{id}: the outline of the glyph {glyph} differs\n  expected: {expected.Outlines[glyph]}\n  actual:   {outline}");
                }
            }

            // The instances of a variable font: the metrics and the advances are whole font units, which are exact; the
            // points are fractions of font units that the reference reader computes with single precision numbers and
            // writes to three decimals.
            foreach (var instance in expected.Instances ?? [])
            {
                var varied = FontTestSupport.WithVariations(font, instance.Variations);
                var variedMetrics = varied.Metrics;
                AssertInstanceMetric(instance, "hasc", variedMetrics.Ascent);
                AssertInstanceMetric(instance, "hdsc", -variedMetrics.Descent);
                AssertInstanceMetric(instance, "hlgp", variedMetrics.LineGap);
                AssertInstanceMetric(instance, "xhgt", variedMetrics.XHeight);
                AssertInstanceMetric(instance, "cpht", variedMetrics.CapHeight);
                AssertInstanceMetric(instance, "unds", variedMetrics.UnderlineThickness);
                AssertInstanceMetric(instance, "undo", -variedMetrics.UnderlinePosition);
                AssertInstanceMetric(instance, "strs", variedMetrics.StrikethroughThickness);
                AssertInstanceMetric(instance, "stro", variedMetrics.StrikethroughPosition);
                for (var glyph = 0; glyph < varied.GlyphCount; glyph++)
                {
                    if (varied.Face.GetAdvanceWidth(glyph) != instance.Advances[glyph])
                    {
                        Assert.Fail($"{id} [{instance.Variations}]: the glyph {glyph} has the advance {varied.Face.GetAdvanceWidth(glyph)}, expected {instance.Advances[glyph]}");
                    }

                    var outline = FontTestSupport.GetOutline(varied, glyph, decimals: 3);
                    if (FontTestSupport.GetOutlineDifference(instance.Outlines[glyph], outline) is not <= InstanceTolerance)
                    {
                        Assert.Fail($"{id} [{instance.Variations}]: the outline of the glyph {glyph} differs\n  expected: {instance.Outlines[glyph]}\n  actual:   {outline}");
                    }
                }
            }
        }

        static void AssertInstanceMetric(FontInstanceVectors expected, string tag, double actual)
        {
            if (expected.Metrics.TryGetValue(tag, out var value) && value != actual)
            {
                Assert.Fail($"[{expected.Variations}] The metric '{tag}' is {actual}, expected {value}");
            }
        }

        static void AssertMetric(FontFaceVectors expected, string tag, double actual)
        {
            // A metric the font does not declare is absent from the vectors: the library then gives a default.
            if (expected.Metrics.TryGetValue(tag, out var value) && value != actual)
            {
                Assert.Fail($"The metric '{tag}' is {actual}, expected {value}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Containers))]
    public void AContainerHoldsTheTablesOfItsOriginalFont(string id)
    {
        var root = FixtureRoot.GetDirectory();
        var entry = LoadFonts().Single(font => font.Id == id);
        var original = FontCollection.Load(root / LoadFonts().Single(font => font.Id == entry.Original).File.Path);
        var container = FontCollection.Load(root / entry.File.Path);

        Assert.HasCount(original.Count, container);
        for (var faceIndex = 0; faceIndex < original.Count; faceIndex++)
        {
            var expected = original[faceIndex].Face;
            var actual = container[faceIndex].Face;
            Assert.Equal(expected.TableTags.ToArray(), actual.TableTags.ToArray());
            var compared = 0;
            foreach (var tag in expected.TableTags)
            {
                Assert.True(expected.TryGetTable(tag, out var expectedTable));
                Assert.True(actual.TryGetTable(tag, out var actualTable));
                var expectedBytes = expectedTable.ToArray();
                var actualBytes = actualTable.ToArray();
                if (entry.Flavor == "woff2")
                {
                    // WOFF2 stores the glyphs in another form: they are written back with the same points (the outlines
                    // are compared with the recorded ones by the other test) but not the same bytes. Its encoder also
                    // sets a flag of the font header and resets the checksum of the file.
                    if (tag is FontTags.Glyf or FontTags.Loca)
                        continue;

                    if (tag == FontTags.Head)
                    {
                        expectedBytes.AsSpan(8, 4).Clear();
                        actualBytes.AsSpan(8, 4).Clear();
                        expectedBytes[16] |= 0x08;
                        actualBytes[16] |= 0x08;
                    }
                }

                if (!expectedBytes.AsSpan().SequenceEqual(actualBytes))
                {
                    Assert.Fail($"{id}: the '{FontTags.Format(tag)}' table of the face {faceIndex} differs from the one of the original font");
                }

                compared++;
            }

            Assert.True(compared >= 8, $"{id}: only {compared} tables were compared");
        }
    }

    [Fact]
    public void TheCorpusHasFontsWithVectors()
    {
        Assert.True(FontsWithVectors().Count >= 23, "The font corpus was not found");
        Assert.True(Containers().Count >= 14, "The font containers of the corpus were not found");
    }

    private static IReadOnlyList<FontEntry> LoadFonts()
    {
        var root = FixtureRoot.GetDirectory();
        return FixtureManifestLoader.Load(root / FixtureRoot.ManifestFileName).Fonts ?? [];
    }
}
