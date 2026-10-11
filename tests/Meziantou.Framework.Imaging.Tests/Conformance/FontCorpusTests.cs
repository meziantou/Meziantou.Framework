using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>The font part of the corpus: the files, and the values the corpus generator recorded from independent font readers.</summary>
public sealed class FontCorpusTests
{
    [Fact]
    public void TheSyntheticFontsOfTheCorpusAreTheOutputOfTheBuilder()
    {
        // The tests build their fonts with the same builder: the reference values recorded for the committed files
        // hold for the fonts the tests build.
        Assert.Equal(FontSamples.Boxes().Build(), ReadFont("fonts/synthetic-boxes"));
        Assert.Equal(FontSamples.Curves().Build(), ReadFont("fonts/synthetic-curves"));
        Assert.Equal(FontBuilder.WriteCollection([FontSamples.Boxes().BuildTables(), FontSamples.Curves().BuildTables()]), ReadFont("fonts/synthetic-collection"));
    }

    [Fact]
    public void TheVectorsDescribeEveryGlyphOfEveryFace()
    {
        var root = FixtureRoot.GetDirectory();
        var count = 0;
        foreach (var font in LoadFonts())
        {
            if (font.Vectors is not { } vectorsFile)
            {
                // Another container of a font that has vectors.
                Assert.Equal("container", font.Modification);
                continue;
            }

            var vectors = FontVectors.Load(root / vectorsFile.Path);
            Assert.NotEmpty(vectors.Tools);
            Assert.HasCount(font.FaceCount, vectors.Faces);
            foreach (var face in vectors.Faces)
            {
                count++;
                Assert.True(face.UnitsPerEm is >= 16 and <= 16384, $"{font.Id}: {face.UnitsPerEm} units per em");
                Assert.True(face.GlyphCount > 0, $"{font.Id}: no glyph");
                Assert.HasCount(face.GlyphCount, face.Advances);
                Assert.HasCount(face.GlyphCount, face.Outlines);
                Assert.NotEmpty(face.Family);
                Assert.NotEmpty(face.CharacterMap);
                foreach (var (codePoint, glyph) in face.CharacterMap)
                {
                    var value = int.Parse(codePoint, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    Assert.True(value is >= 0 and <= 0x10FFFF && glyph >= 0 && glyph < face.GlyphCount, $"{font.Id}: U+{codePoint} maps to the glyph {glyph}");
                }

                // An outline is closed contours of absolute commands.
                foreach (var outline in face.Outlines)
                {
                    Assert.True(outline.Length == 0 || (outline[0] == 'M' && outline[^1] == 'Z'), $"{font.Id}: unexpected outline '{outline}'");
                }
            }
        }

        Assert.True(count >= 5, $"Only {count} faces have vectors");
    }

    [Fact]
    public void TheVectorsOfTheSyntheticFontsAreTheDescriptionOfTheBuilder()
    {
        // What the two independent readers say of the synthetic font is what Common/FontSamples.cs declares.
        var root = FixtureRoot.GetDirectory();
        var font = LoadFonts().Single(font => font.Id == "fonts/synthetic-boxes");
        var face = Assert.Single(FontVectors.Load(root / font.Vectors!.Path).Faces);

        Assert.Equal("Meziantou Boxes", face.Family);
        Assert.Equal("Regular", face.Subfamily);
        Assert.Equal("MeziantouBoxes-Regular", face.PostScriptName);
        Assert.Equal(1024, face.UnitsPerEm);
        Assert.Equal(13, face.GlyphCount);
        Assert.Equal(832, face.Metrics["hasc"]);
        Assert.Equal(-192, face.Metrics["hdsc"]);
        Assert.Equal(0, face.Metrics["hlgp"]);
        Assert.Equal(768, face.Metrics["cpht"]);
        Assert.Equal(512, face.Metrics["xhgt"]);
        Assert.Equal(-128, face.Metrics["undo"]);
        Assert.Equal(64, face.Metrics["unds"]);
        Assert.Equal(256, face.Metrics["stro"]);
        Assert.Equal(64, face.Metrics["strs"]);
        Assert.Equal(FontSamples.BoxA, face.CharacterMap["0041"]);
        Assert.Equal(FontSamples.SpaceGlyph, face.CharacterMap["00A0"]);
        Assert.Equal(FontSamples.CompositeAacute, face.CharacterMap["00C1"]);
        Assert.Equal(FontSamples.HyphenGlyph, face.CharacterMap["2010"]);
        Assert.Equal(new[] { 512, 512, 1024, 512, 768, 512, 1024, 768, 512, 0, 1024, 1024, 512 }, face.Advances.ToArray());
        Assert.Equal("M128,0L128,768L896,768L896,0L128,0Z", face.Outlines[FontSamples.BoxA]);
        Assert.Equal("", face.Outlines[FontSamples.SpaceGlyph]);

        // The composite: A, then the mark moved by (1152, 0). The scaled composite: B scaled by 1.5 and moved by (64, 0).
        Assert.Equal("M128,0L128,768L896,768L896,0L128,0ZM512,832L512,960L768,960L768,832L512,832Z", face.Outlines[FontSamples.CompositeAacute]);
        Assert.Equal("M160,0L160,768L736,768L736,0L160,0Z", face.Outlines[FontSamples.ScaledH]);
    }

    [Fact]
    public void ThirdPartyFontsHaveALicenseThatAllowsTheirForm()
    {
        var fonts = LoadFonts();
        Assert.Contains(fonts, font => font.Provenance.License == FixtureManifestValidator.FontLicense);
        foreach (var font in fonts)
        {
            if (font.Provenance.Origin != "external")
                continue;

            // A third-party font names its source, and is only changed by the pinned subsetter or by a change of container.
            Assert.False(string.IsNullOrEmpty(font.Provenance.Source), $"{font.Id}: no source");
            if (font.Modification == "subset")
            {
                Assert.Null(font.ReservedFontName);
                Assert.Contains(font.Provenance.Tools ?? [], tool => tool.StartsWith("hb-subset ", StringComparison.Ordinal));
            }
        }
    }

    private static IReadOnlyList<FontEntry> LoadFonts()
    {
        var root = FixtureRoot.GetDirectory();
        return FixtureManifestLoader.Load(root / FixtureRoot.ManifestFileName).Fonts ?? [];
    }

    private static byte[] ReadFont(string id)
    {
        var font = LoadFonts().Single(font => font.Id == id);
        return File.ReadAllBytes(FixtureRoot.GetDirectory() / font.File.Path);
    }
}
