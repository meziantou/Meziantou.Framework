using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The texts of the corpus, shaped with its fonts, against the glyphs and the positions the corpus generator recorded
/// from the OpenType shaper of HarfBuzz (see the Fonts section of the fixtures README).
/// </summary>
/// <remarks>
/// What is compared is what is drawn and what a caller can observe: the glyphs, their clusters, the place each glyph is
/// drawn at and the advance of the whole text. How an adjustment is split between the advance of a glyph and the
/// offset of the next one is not compared: the two shapers store kerning differently, with the same result.
/// </remarks>
public sealed class ShapingConformanceTests
{
    public static TheoryData<string> FontsWithShaping()
    {
        var data = new TheoryData<string>();
        foreach (var font in LoadFonts())
        {
            if (font.Shaping is not null)
            {
                data.Add(font.Id);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FontsWithShaping))]
    public void TheGlyphsAndPositionsAreTheOnesOfTheReferenceShaper(string id)
    {
        var root = FixtureRoot.GetDirectory();
        var entry = LoadFonts().Single(font => font.Id == id);
        var font = Font.Load(root / entry.File.Path);
        var vectors = ShapingVectors.Load(root / entry.Shaping!.Path);
        var failures = new List<string>();
        foreach (var testCase in vectors.Cases)
        {
            var text = testCase.Characters;
            var key = id + ";" + testCase.Text + ";" + testCase.Features;
            var expected = Format(testCase.Glyphs.Select(glyph => (glyph[0], glyph[1], glyph[2], glyph[3], glyph[4], glyph[5])));

            // The reference clusters are indices of characters; the ones of the library are indices of UTF-16 code units.
            var characterIndices = new int[text.Length + 1];
            for (int i = 0, character = 0; i < text.Length; character++)
            {
                characterIndices[i] = character;
                i += char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
            }

            var buffer = new GlyphBuffer(ImageResourceLimits.Default);
            TextShaper.AddText(buffer, text);
            var script = GetScript(text);
            TextShaper.Shape(buffer, font.Face, script, language: 0, isRightToLeft: false, ParseFeatures(testCase.Features));
            var glyphs = new List<(int, int, int, int, int, int)>();
            for (var i = 0; i < buffer.Length; i++)
            {
                var position = buffer.Positions[i];
                glyphs.Add((buffer.Info[i].Glyph, characterIndices[buffer.Info[i].Cluster], position.XOffset, position.YOffset, position.XAdvance, position.YAdvance));
            }

            var actual = Format(glyphs);
            var isKnown = KnownDivergences.ContainsKey(key);
            if (!string.Equals(expected, actual, StringComparison.Ordinal) && !isKnown)
            {
                failures.Add($"{testCase.Text} [{testCase.Features}] \"{text}\"\n    expected {expected}\n    actual   {actual}");
            }
            else if (string.Equals(expected, actual, StringComparison.Ordinal) && isKnown)
            {
                failures.Add($"{testCase.Text} [{testCase.Features}] is listed as a known divergence and no longer diverges");
            }
        }

        Assert.Empty(failures, $"{failures.Count} of {vectors.Cases.Count} texts differ:\n  " + string.Join("\n  ", failures));
    }

    [Fact]
    public void TheCorpusHasShapingVectors()
    {
        Assert.True(FontsWithShaping().Count >= 4, "The shaping vectors of the corpus were not found");
    }

    /// <summary>
    /// The texts the library shapes differently from the reference shaper, on purpose, with the reason. A text that no
    /// longer differs fails the test: the list stays what the library actually does.
    /// </summary>
    private static readonly Dictionary<string, string> KnownDivergences = new(StringComparer.Ordinal);

    /// <summary>
    /// Formats glyphs as a caller sees them: each glyph with its cluster and the place it is drawn at from the start of
    /// the text, then the advance of the text.
    /// </summary>
    private static string Format(IEnumerable<(int Glyph, int Cluster, int XOffset, int YOffset, int XAdvance, int YAdvance)> glyphs)
    {
        var result = new StringBuilder();
        var x = 0;
        var y = 0;
        foreach (var glyph in glyphs)
        {
            result.Append(CultureInfo.InvariantCulture, $"{glyph.Glyph}={glyph.Cluster}@{x + glyph.XOffset},{y + glyph.YOffset}|");
            x += glyph.XAdvance;
            y += glyph.YAdvance;
        }

        return result.Append(CultureInfo.InvariantCulture, $"+{x},{y}").ToString();
    }

    /// <summary>Gets the script the reference shaper guesses for a text: the script of its first character that has one.</summary>
    private static UnicodeScript GetScript(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var script = UnicodeScripts.GetScript(rune.Value);
            if (script is not (UnicodeScript.Common or UnicodeScript.Inherited or UnicodeScript.Unknown))
                return script;
        }

        return UnicodeScript.Common;
    }

    /// <summary>Reads features in the syntax of the reference shaper: <c>liga</c>, <c>-kern</c>, <c>frac=1</c>.</summary>
    private static ShapingFeature[] ParseFeatures(string? features)
    {
        if (features is null)
            return [];

        var result = new List<ShapingFeature>();
        foreach (var item in features.Split(','))
        {
            var text = item.AsSpan();
            var value = 1;
            if (text[0] == '-')
            {
                value = 0;
                text = text[1..];
            }

            var equals = text.IndexOf('=');
            if (equals >= 0)
            {
                value = int.Parse(text[(equals + 1)..], CultureInfo.InvariantCulture);
                text = text[..equals];
            }

            result.Add(new ShapingFeature(ShapingFeature.ToTag(text.ToString()), value));
        }

        return [.. result];
    }

    private static IReadOnlyList<FontEntry> LoadFonts()
    {
        var root = FixtureRoot.GetDirectory();
        return FixtureManifestLoader.Load(root / FixtureRoot.ManifestFileName).Fonts ?? [];
    }
}
