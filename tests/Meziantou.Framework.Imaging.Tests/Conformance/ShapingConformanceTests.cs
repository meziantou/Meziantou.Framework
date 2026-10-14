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
            var key = id + ";" + testCase.Text + ";" + testCase.Features + (testCase.Variations is null ? "" : ";" + testCase.Variations) + (testCase.Language is null ? "" : ";" + testCase.Language) + (testCase.Direction is null ? "" : ";" + testCase.Direction);
            var face = (testCase.Variations is null ? font : FontTestSupport.WithVariations(font, testCase.Variations)).Face;
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

            // The reference shaper lays a text out in the direction of its script, and gives the glyphs from left to
            // right; a vertical line goes from top to bottom whatever the script.
            Assert.True(testCase.Direction is null or "ttb", $"Unknown direction '{testCase.Direction}'");
            var isVertical = testCase.Direction is not null;
            var isRightToLeft = !isVertical && script is UnicodeScript.Arabic or UnicodeScript.Hebrew or UnicodeScript.Syriac or UnicodeScript.Nko or UnicodeScript.Adlam;
            using var scratch = new GlyphScratch(AllocationScope.Create(ImageConfiguration.Default, "ShapingConformanceTests"));
            TextShaper.Shape(buffer, face, script, testCase.Language is null ? [] : OpenTypeLanguage.GetTags(testCase.Language), isRightToLeft, ParseFeatures(testCase.Features), isVertical, scratch);
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
                failures.Add($"{testCase.Text} [{testCase.Features}] [{testCase.Variations}] [{testCase.Language}] [{testCase.Direction}] \"{text}\"\n    expected {expected}\n    actual   {actual}");
            }
            else if (string.Equals(expected, actual, StringComparison.Ordinal) && isKnown)
            {
                failures.Add($"{testCase.Text} [{testCase.Features}] [{testCase.Variations}] [{testCase.Language}] is listed as a known divergence and no longer diverges");
            }
        }

        Assert.Empty(failures, $"{failures.Count} of {vectors.Cases.Count} texts differ:\n  " + string.Join("\n  ", failures));
    }

    [Theory]
    [InlineData("fonts/noto-naskh-arabic", UnicodeScript.Arabic)]
    [InlineData("fonts/noto-nastaliq-urdu", UnicodeScript.Arabic)]
    [InlineData("fonts/noto-sans-hebrew", UnicodeScript.Hebrew)]
    [InlineData("fonts/noto-sans-syriac", UnicodeScript.Syriac)]
    [InlineData("fonts/noto-sans-thai", UnicodeScript.Thai)]
    [InlineData("fonts/noto-sans-lao", UnicodeScript.Lao)]
    [InlineData("fonts/noto-sans-cjk-kr-regular", UnicodeScript.Hangul)]
    [InlineData("fonts/noto-sans-devanagari", UnicodeScript.Devanagari)]
    [InlineData("fonts/noto-sans-devanagari-first-version", UnicodeScript.Devanagari)]
    [InlineData("fonts/noto-sans-bengali", UnicodeScript.Bengali)]
    [InlineData("fonts/noto-sans-gurmukhi", UnicodeScript.Gurmukhi)]
    [InlineData("fonts/noto-sans-gujarati", UnicodeScript.Gujarati)]
    [InlineData("fonts/noto-sans-oriya", UnicodeScript.Oriya)]
    [InlineData("fonts/noto-sans-tamil", UnicodeScript.Tamil)]
    [InlineData("fonts/noto-sans-telugu", UnicodeScript.Telugu)]
    [InlineData("fonts/noto-sans-kannada", UnicodeScript.Kannada)]
    [InlineData("fonts/noto-sans-malayalam", UnicodeScript.Malayalam)]
    [InlineData("fonts/noto-sans-khmer", UnicodeScript.Khmer)]
    [InlineData("fonts/noto-sans-myanmar", UnicodeScript.Myanmar)]
    [InlineData("fonts/noto-serif-tibetan", UnicodeScript.Tibetan)]
    [InlineData("fonts/noto-sans-sinhala", UnicodeScript.Sinhala)]
    [InlineData("fonts/noto-sans-javanese", UnicodeScript.Javanese)]
    [InlineData("fonts/noto-sans-balinese", UnicodeScript.Balinese)]
    [InlineData("fonts/noto-sans-tai-tham", UnicodeScript.TaiTham)]
    [InlineData("fonts/noto-sans-nko", UnicodeScript.Nko)]
    [InlineData("fonts/noto-sans-mongolian", UnicodeScript.Mongolian)]
    [InlineData("fonts/noto-sans-adlam", UnicodeScript.Adlam)]
    [InlineData("fonts/noto-sans-cham", UnicodeScript.Cham)]
    [InlineData("fonts/noto-sans-newa", UnicodeScript.Newa)]
    [InlineData("fonts/noto-sans-kaithi", UnicodeScript.Kaithi)]
    [InlineData("fonts/noto-sans-lepcha", UnicodeScript.Lepcha)]
    [InlineData("fonts/noto-sans-duployan", UnicodeScript.Duployan)]
    public void ALayoutIsAsWideAsTheReferenceShaperMakesIt(string id, UnicodeScript script)
    {
        // The whole way from a string to a line: the direction, the script and the runs are found by the layout, where
        // the other test gives them to the shaper. At the size of the em, the width of the line is the advance of the
        // text in font units.
        var root = FixtureRoot.GetDirectory();
        var entry = LoadFonts().Single(font => font.Id == id);
        var font = Font.Load(root / entry.File.Path);
        var vectors = ShapingVectors.Load(root / entry.Shaping!.Path);
        var count = 0;
        foreach (var testCase in vectors.Cases)
        {
            // The texts of one run: letters of the script with their marks, and spaces between words.
            var text = testCase.Characters;
            var runes = text.EnumerateRunes().ToArray();
            if (testCase.Features is not null || testCase.Variations is not null || testCase.Direction is not null
                || UnicodeScripts.GetScript(runes[0].Value) != script || UnicodeScripts.GetScript(runes[^1].Value) is var last && last != script && last != UnicodeScript.Inherited
                || runes.Any(rune => rune.Value != ' ' && UnicodeScripts.GetScript(rune.Value) is var runeScript && runeScript != script && runeScript != UnicodeScript.Inherited))
            {
                continue;
            }

            var layout = TextLayout.Create(text, font, font.UnitsPerEm);
            Assert.Equal(testCase.Glyphs.Sum(glyph => glyph[4]), layout.Width, 1e-6, testCase.Text);
            count++;
        }

        Assert.True(count >= 4, $"Only {count} texts of {id} are one run of {script}");
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
    private static readonly Dictionary<string, string> KnownDivergences = new(StringComparer.Ordinal)
    {
        // "Creating and supporting OpenType fonts for the Universal Shaping Engine", character classification: the
        // class BASE_OTHER is the consonant placeholders and "U+2015, U+2022, U+25FB-25FE". The reference shaper
        // takes these six characters as symbols: a mark can follow one, but not a letter that takes a stacked
        // consonant or a reph before it, which it shows on a dotted circle of its own.
        ["fonts/noto-sans-newa;11460 2022 11436;"] = "the bullet is a generic base of the universal model",
    };

    static ShapingConformanceTests()
    {
        // The language systems of a language (OpenTypeLanguage): the font has one per tag of the registry of OpenType,
        // so what differs is the first tag of a language, never whether a font with one tag is drawn with it.
        AddLanguages(
            "crm krc",
            "the registry gives two tags the code and nothing else (L-Cree and Moose Cree, Balkar and Karachay): the library tries them in the order of the registry, the reference shaper in the other one");
        AddLanguages(
            "ike",
            "the tag of fewer languages comes first: the tag of the Inuktitut of Nunavik, which the registry gives this code, before the tag of Inuktitut");
        AddLanguages(
            "max msi xmm pga",
            "the registry has these languages under the tag of the creoles, which the library tries before the tag of the macrolanguage BCP 47 gives them (Malay, Arabic); the reference shaper only has the second one");
        AddLanguages(
            "qub qud quf qug quk qul qup qur qus quw qux quy qva qvc qve qvh qvj qvl qvm qvn qvo qvp qvs qvw qvz qwa qwc qws qxa qxc qxh qxl qxn qxo qxp qxr qxt qxu qxw",
            "the registry has no tag for these languages nor for Quechua, their macrolanguage: the reference shaper gives each one the tag of one of the four Quechua languages that have one, from what it knows of them");
        AddLanguages(
            "yue-CN",
            "Cantonese has the forms of Hong Kong when its language tag has no region and no script; with the region of mainland China, the library gives the simplified forms and the reference shaper the ones of Hong Kong");
        AddLanguages(
            "zh-Bopo zh-Latn",
            "the registry has a tag for the phonetic transcriptions of Chinese, which the library gives to a text in Bopomofo or in Latin letters; the reference shaper does not look at these scripts");
        AddLanguages(
            "zh-cmn-Hant",
            "an extended language is the language of the text (BCP 47): this is Mandarin in traditional characters, as cmn-Hant is for both shapers; the reference shaper does not look at the script of this form");

        static void AddLanguages(string languages, string reason)
        {
            foreach (var language in languages.Split(' '))
            {
                KnownDivergences.Add("fonts/synthetic-languages;0041;;" + language, reason);
            }
        }
    }

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
