using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// What the shaper does to a text before and after the lookups of the font: the form of the characters, clusters,
/// invisible characters, the features that are applied, and the limits. The lookups themselves are covered by the
/// suites of <c>Conformance/ShapingSuiteTests</c> and <c>Conformance/ShapingConformanceTests</c>.
/// </summary>
public sealed class TextShaperTests
{
    private static Font Boxes { get; } = FontTestSupport.Load(FontSamples.Boxes());

    [Fact]
    public void CharactersAreMappedToGlyphsWithTheirAdvances()
    {
        var buffer = Shape(Boxes, "AB C");

        // A (glyph 2, 1024 units), B (3, 512), space (1, 512), C (4, 768). The kerning of the pair A B, -128, is split
        // between the advance of A and the advance and offset of B.
        Assert.Equal("2=0+960|3=1@-64,0+448|1=2+512|4=3+768", Format(buffer));
    }

    [Fact]
    public void ACharacterAndItsMarkUseTheComposedGlyphWhenTheFontHasIt()
    {
        // The font has U+00C1, a composite glyph (10), and the combining acute accent (9).
        Assert.Equal("10=0+1024", Format(Shape(Boxes, "\u00C1")));
        Assert.Equal("10=0+1024", Format(Shape(Boxes, "A\u0301")));

        // A font without the composed character: the character is decomposed into what the font has. The mark has no
        // advance in this font, and is drawn where it is.
        var builder = FontSamples.Boxes();
        builder.CharacterMap.Remove(0x00C1);
        var font = FontTestSupport.Load(builder);
        Assert.Equal("2=0+1024|9=0+0", Format(Shape(font, "\u00C1")));
        Assert.Equal("2=0+1024|9=0+0", Format(Shape(font, "A\u0301")));

        // Neither the composed character nor its mark: the character is missing, as a whole. A mark that is missing is
        // still a mark: the missing glyph, 512 units wide, does not move the pen and is drawn over what precedes it.
        builder.CharacterMap.Remove(0x0301);
        font = FontTestSupport.Load(builder);
        Assert.Equal("0=0+512", Format(Shape(font, "\u00C1")));
        Assert.Equal("2=0+1024|0=0@-512,0+0", Format(Shape(font, "A\u0301")));
    }

    [Fact]
    public void ACharacterAndWhatContinuesItAreOneCluster()
    {
        var builder = FontSamples.Boxes();
        builder.CharacterMap.Remove(0x00C1);
        builder.CharacterMap[0x1F600] = FontSamples.BoxA;
        builder.CharacterMap[0x1F3FB] = FontSamples.BoxB;
        builder.CharacterMap[0x1F1E6] = FontSamples.BoxC;
        var font = FontTestSupport.Load(builder);

        // A mark belongs to the cluster of its base; so do the modifier of an emoji, what a zero width joiner joins, and
        // the second regional indicator of a pair. The clusters are indices in the UTF-16 text.
        // The mark does not come between A and B either: the pair is kerned.
        Assert.Equal("2=0+960|9=0+0|3=2@-64,0+448", Format(Shape(font, "A\u0301B")));
        Assert.Equal("2=0+960|3=0@-64,0+480|2=4@32,0+1056", Format(Shape(font, "\U0001F600\U0001F3FB\U0001F600")));
        Assert.Equal("4=0+768|4=0+768|4=4+768|4=4+768|4=8+768", Format(Shape(font, "\U0001F1E6\U0001F1E6\U0001F1E6\U0001F1E6\U0001F1E6")));
        Assert.Equal("2=0+1024|1=0+0|2=0+1024", Format(Shape(font, "\U0001F600\u200D\U0001F600")));
    }

    [Fact]
    public void CharactersWithoutVisibleFormBecomeInvisibleGlyphs()
    {
        // The glyph of the space, without advance: nothing is drawn, and the cluster is still there. The kerning of the
        // pair applies across it.
        Assert.Equal("2=0+960|1=1+0|3=2@-64,0+448", Format(Shape(Boxes, "A\u200BB")));
        Assert.Equal("2=0+960|1=1+0|1=2+0|3=3@-64,0+448", Format(Shape(Boxes, "A\u00AD\u2060B")));

        // A font without space: the glyphs are removed, and their characters join a neighboring cluster.
        var builder = FontSamples.Boxes();
        builder.CharacterMap.Remove(' ');
        builder.CharacterMap.Remove(0xA0);
        var font = FontTestSupport.Load(builder);
        Assert.Equal("2=0+960|3=2@-64,0+448", Format(Shape(font, "A\u200BB")));
        Assert.Equal("2=0+1024", Format(Shape(font, "A\u200B")));
        Assert.Equal("2=0+1024", Format(Shape(font, "\u200BA")));
    }

    [Fact]
    public void SpacesAndHyphensTheFontDoesNotHaveAreMadeFromTheOnesItHas()
    {
        // The font has the space (512 units), the no-break space, the hyphen-minus and the hyphen; its em is 1024 units.
        var builder = FontSamples.Boxes();
        builder.CharacterMap.Remove(0xA0);
        var font = FontTestSupport.Load(builder);
        Assert.Equal("1=0+512", Format(Shape(font, "\u00A0")));
        Assert.Equal("1=0+512|1=1+1024|1=2+341|1=3+256|1=4+171|1=5+205|1=6+64|1=7+256|1=8+227|1=9+1024", Format(Shape(font, "\u2002\u2003\u2004\u2005\u2006\u2009\u200A\u202F\u205F\u3000")));

        // The figure space is as wide as a digit and the punctuation space as a period, when the font has them.
        Assert.Equal("1=0+512|1=1+512", Format(Shape(font, "\u2007\u2008")));
        builder.CharacterMap['0'] = FontSamples.BoxC;
        builder.CharacterMap['.'] = FontSamples.BoxA;
        font = FontTestSupport.Load(builder);
        Assert.Equal("1=0+768|1=1+1024", Format(Shape(font, "\u2007\u2008")));

        // The non-breaking hyphen is drawn as the hyphen.
        Assert.Equal("12=0+512", Format(Shape(font, "\u2011")));
    }

    [Fact]
    public void LegacyKerningCanBeTurnedOff()
    {
        Assert.Equal("2=0+960|3=1@-64,0+448", Format(Shape(Boxes, "AB")));
        Assert.Equal("2=0+1024|3=1+512", Format(Shape(Boxes, "AB", new ShapingFeature("kern", 0))));
        Assert.Equal("2=0+960|3=1@-64,0+448", Format(Shape(Boxes, "AB", new ShapingFeature("kern", 1))));

        // B then A: 64 more units, 32 on each side. An odd adjustment gives the larger half to the second glyph.
        Assert.Equal("3=0+544|2=1@32,0+1056", Format(Shape(Boxes, "BA")));
        var builder = FontSamples.Boxes();
        builder.KerningPairs[(FontSamples.BoxA, FontSamples.BoxA)] = -3;
        Assert.Equal("2=0+1022|2=1@-1,0+1023", Format(Shape(FontTestSupport.Load(builder), "AA")));
    }

    [Fact]
    public void TheGlyphsOfARunAreLimited()
    {
        // The limit is checked when the buffer of a run grows beyond its first size.
        var text = new string('A', 40);
        var buffer = new GlyphBuffer(new ImageResourceLimits { MaxTextGlyphs = 40 });
        TextShaper.AddText(buffer, text);
        Assert.Equal(40, buffer.Length);

        buffer = new GlyphBuffer(new ImageResourceLimits { MaxTextGlyphs = 39 });
        var exception = Assert.Throws<ImageResourceLimitException>(() => TextShaper.AddText(buffer, text));
        Assert.Equal(ImageResourceLimitKind.TextGlyphs, exception.Kind);
        Assert.Equal(39, exception.Limit);
        Assert.Equal(40, exception.Requested);
    }

    [Fact]
    public void ScriptTagsFollowTheOpenTypeRegistry()
    {
        static string[] Tags(UnicodeScript script)
        {
            Span<uint> tags = stackalloc uint[OpenTypeScript.MaxTagCount];
            var count = OpenTypeScript.GetTags(script, tags);
            return [.. tags[..count].ToArray().Select(FontTags.Format)];
        }

        // The tag of the script, then the default script and the tags fonts use in its place.
        Assert.Equal(new[] { "latn", "DFLT", "dflt", "latn" }, Tags(UnicodeScript.Latin));
        Assert.Equal(new[] { "arab", "DFLT", "dflt", "latn" }, Tags(UnicodeScript.Arabic));
        Assert.Equal(new[] { "DFLT", "dflt", "latn" }, Tags(UnicodeScript.Common));
        Assert.Equal(new[] { "DFLT", "dflt", "latn" }, Tags(UnicodeScript.Inherited));

        // The scripts whose tag is not their ISO 15924 code, and the scripts with several versions of their shaping model.
        Assert.Equal("kana", Tags(UnicodeScript.Hiragana)[0]);
        Assert.Equal("kana", Tags(UnicodeScript.Katakana)[0]);
        Assert.Equal("lao ", Tags(UnicodeScript.Lao)[0]);
        Assert.Equal("nko ", Tags(UnicodeScript.Nko)[0]);
        Assert.Equal("yi  ", Tags(UnicodeScript.Yi)[0]);
        Assert.Equal("vai ", Tags(UnicodeScript.Vai)[0]);
        Assert.Equal(new[] { "dev3", "dev2", "deva", "DFLT", "dflt", "latn" }, Tags(UnicodeScript.Devanagari));
        Assert.Equal(new[] { "mym2", "mymr", "DFLT", "dflt", "latn" }, Tags(UnicodeScript.Myanmar));
    }

    [Theory]
    [InlineData("0628 0633 0645", "init medi fina")]
    [InlineData("0627 0628", "isol isol")]
    [InlineData("0628 0627 0628", "init fina isol")]
    [InlineData("0628 064E 0651 0628", "init . . fina")]
    [InlineData("0628 200C 0628", "isol . isol")]
    [InlineData("0628 200D 0020 200D 0628", "init . . . fina")]
    [InlineData("0628 0640 0628 0020 0640 0627", "init . fina . . fina")]
    [InlineData("0628 200F 0628 061C 0628", "init . medi . fina")]
    [InlineData("0020 0628 0020 0627 0020", ". isol . isol .")]
    [InlineData("0710", "isol")]
    [InlineData("0712 0710", "init fina")]
    [InlineData("0712 0710 0712", "init med2 isol")]
    [InlineData("0715 0710", "isol fin3")]
    [InlineData("072A 0710 0020 0716 0710", "isol fin3 . isol fin3")]
    [InlineData("0717 0710", "isol fin2")]
    [InlineData("0715 0710 0712", "isol isol isol")]
    [InlineData("0710 0710", "isol fin2")]
    [InlineData("0717 0710 0710", "isol isol fin2")]
    [InlineData("0712 0710 0710", "init med2 fin2")]
    [InlineData("0715 0730 0710", "isol . fin3")]
    [InlineData("0717 200C 0710", "isol . isol")]
    public void LettersTakeTheFormTheirNeighborsAskFor(string text, string expected)
    {
        // The joining types are the ones of Unicode: beh, seen and meem join on both sides, alef on its right side
        // only; marks and format characters are transparent, a zero width joiner and a tatweel join on both sides, a
        // zero width non-joiner and a space on none. In Syriac, beth joins on both sides; he, dalath, rish and alaph on
        // their right side only, and an alaph has a form of its own in the middle of a word (med2), at the end of a word
        // after a letter that does not join it (fin2) and after dalath or rish (fin3).
        string[] names = ["isol", "fina", "fin2", "fin3", "medi", "med2", "init"];
        var info = text.Split(' ').Select(item => new GlyphInfo { CodePoint = int.Parse(item, NumberStyles.HexNumber, CultureInfo.InvariantCulture) }).ToArray();
        ArabicShaper.SetFormMasks(info, firstBit: 5);
        var forms = info.Select(glyph =>
        {
            var isLetter = UnicodeProperties.GetGeneralCategory(glyph.CodePoint) == UnicodeCategory.OtherLetter;
            var bits = glyph.Mask >> 5;
            return !isLetter ? "." : bits == 0 ? "none" : System.Numerics.BitOperations.IsPow2(bits) ? names[System.Numerics.BitOperations.Log2(bits)] : "several";
        });
        Assert.Equal(expected, string.Join(' ', forms));
    }

    [Fact]
    public void TheMarksOfALetterAreInTheOrderTheyAreDrawnIn()
    {
        // A font without layout tables: the glyphs are the characters, in the order shaping gives them.
        var builder = FontSamples.Boxes();
        builder.CharacterMap[0x0628] = FontSamples.BoxA;
        builder.CharacterMap[0x064E] = FontSamples.BoxB;
        builder.CharacterMap[0x0650] = FontSamples.BoxC;
        builder.CharacterMap[0x0651] = FontSamples.TriangleD;
        builder.CharacterMap[0x0654] = FontSamples.FrameE;
        builder.CharacterMap[0x0655] = FontSamples.OverlapF;
        builder.CharacterMap[0x0652] = FontSamples.Boxa;
        var font = FontTestSupport.Load(builder);

        // Arabic: the shadda (class 33) comes before the vowel marks (fatha 30, kasra 32), whatever the order of the
        // text; the canonical order of Unicode puts it after them.
        Assert.Equal("2 5 3", Glyphs(font, "\u0628\u064E\u0651", UnicodeScript.Arabic));
        Assert.Equal("2 5 3", Glyphs(font, "\u0628\u0651\u064E", UnicodeScript.Arabic));
        Assert.Equal("2 5 3 4 8", Glyphs(font, "\u0628\u0652\u0650\u064E\u0651", UnicodeScript.Arabic));

        // The hamza below (class 220) and the hamza above (230) modify the letter: they come first, in that order
        // (Unicode Technical Report #53). Fatha, kasra, shadda, hamza below, hamza above is the canonical order.
        Assert.Equal("2 6 3", Glyphs(font, "\u0628\u064E\u0654", UnicodeScript.Arabic));
        Assert.Equal("2 7 4", Glyphs(font, "\u0628\u0650\u0655", UnicodeScript.Arabic));
        Assert.Equal("2 7 6 5 3 4", Glyphs(font, "\u0628\u0651\u0650\u0655\u064E\u0654", UnicodeScript.Arabic));

        // The marks of two letters are not mixed.
        Assert.Equal("2 5 3 2 6 3", Glyphs(font, "\u0628\u064E\u0651\u0628\u064E\u0654", UnicodeScript.Arabic));
    }

    [Fact]
    public void HebrewPointsAreOrderedAndComposedForAFontThatCannotPlaceThem()
    {
        var builder = FontSamples.Boxes();
        builder.CharacterMap[0x05E9] = FontSamples.BoxA;
        builder.CharacterMap[0x05C1] = FontSamples.BoxB;
        builder.CharacterMap[0x05BC] = FontSamples.BoxC;
        builder.CharacterMap[0x05B7] = FontSamples.TriangleD;
        builder.CharacterMap[0x05B0] = FontSamples.FrameE;
        builder.CharacterMap[0x05BD] = FontSamples.OverlapF;
        var font = FontTestSupport.Load(builder);

        // The dot of the shin, then the dagesh, then the vowel: the order fonts expect. The canonical order of Unicode
        // is the vowel (patah, class 17), the dagesh (21), the dot (24).
        Assert.Equal("2 3 4 5", Glyphs(font, "\u05E9\u05BC\u05C1\u05B7", UnicodeScript.Hebrew));
        Assert.Equal("2 3 4 5", Glyphs(font, "\u05E9\u05B7\u05C1\u05BC", UnicodeScript.Hebrew));

        // A meteg (7) after the two points of a vowel, patah (5) and sheva (6), is drawn between them.
        Assert.Equal("2 5 7 6", Glyphs(font, "\u05E9\u05B7\u05B0\u05BD", UnicodeScript.Hebrew));
        Assert.Equal("2 5 7", Glyphs(font, "\u05E9\u05B7\u05BD", UnicodeScript.Hebrew));

        // A precomposed letter the font does not have is its letter and its points, in the order of its
        // decomposition: nothing orders the marks of a text that has none.
        Assert.Equal("2 4 3", Glyphs(font, "\uFB2C", UnicodeScript.Hebrew));
        Assert.Equal("2 3 4 5", Glyphs(font, "\uFB2C\u05B7", UnicodeScript.Hebrew));

        // A font that has the precomposed letters and no table to place a point: the letter and its points are drawn
        // with the precomposed letter. Unicode never composes these letters, which is why a text does not have them.
        builder.CharacterMap[0xFB2A] = FontSamples.Boxa;
        builder.CharacterMap[0xFB2C] = FontSamples.ScaledH;
        font = FontTestSupport.Load(builder);
        Assert.Equal("8", Glyphs(font, "\u05E9\u05C1", UnicodeScript.Hebrew));
        Assert.Equal("11", Glyphs(font, "\u05E9\u05BC\u05C1", UnicodeScript.Hebrew));
        Assert.Equal("11 5", Glyphs(font, "\u05E9\u05B7\u05C1\u05BC", UnicodeScript.Hebrew));
        Assert.Equal("8 11", Glyphs(font, "\uFB2A\uFB2C", UnicodeScript.Hebrew));

        // The other scripts do not compose them, and a caller that turns the placement of marks off gets them as a font
        // without that feature does.
        Assert.Equal("2 3", Glyphs(font, "\u05E9\u05C1", UnicodeScript.Latin));
    }

    [Fact]
    public void AStretchingGlyphFillsTheWidthOfTheWordItEncloses()
    {
        // In the order the glyphs are drawn in, from left to right: the letters of a word, then the parts of the glyph
        // that encloses it, the mark that precedes the word in the text. B (512 units) is a part drawn once, C (768)
        // a part that repeats.
        static GlyphBuffer Create(int letters)
        {
            var buffer = new GlyphBuffer(ImageResourceLimits.Default);
            for (var i = 0; i < letters; i++)
            {
                buffer.Add(0x0712, letters - i);
                buffer.Info[i].Glyph = FontSamples.BoxA;
                buffer.Info[i].Category = UnicodeCategory.OtherLetter;
            }

            foreach (var (glyph, data) in new[] { (FontSamples.BoxB, ArabicShaper.StretchFixed), (FontSamples.BoxC, ArabicShaper.StretchRepeating), (FontSamples.BoxB, ArabicShaper.StretchFixed) })
            {
                buffer.Add(0x070F, 0);
                buffer.Info[^1].Glyph = glyph;
                buffer.Info[^1].ShaperData = data;
            }

            buffer.CreatePositions();
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer.Positions[i].XAdvance = i < letters ? 1024 : 0;
            }

            return buffer;
        }

        // Two letters, 2048 units: the parts drawn once take 1024 of them, and the repeating part, 768 units wide,
        // fills the 1024 that remain with two copies that overlap by 512. From the end of the word: B at -512, C at
        // -1280 and -1536, B at -2048, the start of the word.
        var buffer = Create(letters: 2);
        ArabicShaper.ApplyStretching(buffer, Boxes.Face);
        Assert.Equal("2=2+1024|2=1+1024|3=0@-2048,0+0|4=0@-1536,0+0|4=0@-1280,0+0|3=0@-512,0+0", Format(buffer));

        // Five letters, 5120 units: 4096 remain, which six copies fill (five would leave 256 units empty); they
        // overlap by (6 x 768 - 4096) / 5 = 102 units each, the division rounded down: each copy is 666 units after the
        // one before it, and the glyph is 2 units wider than the word.
        buffer = Create(letters: 5);
        ArabicShaper.ApplyStretching(buffer, Boxes.Face);
        Assert.Equal(
            "2=5+1024|2=4+1024|2=3+1024|2=2+1024|2=1+1024|3=0@-5122,0+0|4=0@-4610,0+0|4=0@-3944,0+0|4=0@-3278,0+0|4=0@-2612,0+0|4=0@-1946,0+0|4=0@-1280,0+0|3=0@-512,0+0",
            Format(buffer));

        // No letter: the parts are drawn once. The glyph is 2304 units wide and half of the 1024 units the parts drawn
        // once lack are left after it.
        buffer = Create(letters: 0);
        ArabicShaper.ApplyStretching(buffer, Boxes.Face);
        Assert.Equal("3=0@-2304,0+0|4=0@-1792,0+0|3=0@-1024,0+0", Format(buffer));
    }

    /// <summary>Shapes a text in the order of its characters and gives the identifiers of the glyphs.</summary>
    private static string Glyphs(Font font, string text, UnicodeScript script)
    {
        var buffer = new GlyphBuffer(ImageResourceLimits.Default);
        TextShaper.AddText(buffer, text);
        TextShaper.Shape(buffer, font.Face, script, language: 0, isRightToLeft: false, []);
        return string.Join(' ', buffer.Info.ToArray().Select(glyph => glyph.Glyph));
    }

    private static GlyphBuffer Shape(Font font, string text, params ShapingFeature[] features)
    {
        var buffer = new GlyphBuffer(ImageResourceLimits.Default);
        TextShaper.AddText(buffer, text);
        TextShaper.Shape(buffer, font.Face, UnicodeScript.Latin, language: 0, isRightToLeft: false, features);
        return buffer;
    }

    /// <summary>Formats glyphs as <c>glyph=cluster@xOffset,yOffset+advance</c>; the offset is omitted when it is zero.</summary>
    private static string Format(GlyphBuffer buffer)
    {
        var result = new StringBuilder();
        for (var i = 0; i < buffer.Length; i++)
        {
            var position = buffer.Positions[i];
            result.Append(i > 0 ? "|" : "").Append(CultureInfo.InvariantCulture, $"{buffer.Info[i].Glyph}={buffer.Info[i].Cluster}");
            if (position.XOffset != 0 || position.YOffset != 0)
            {
                result.Append(CultureInfo.InvariantCulture, $"@{position.XOffset},{position.YOffset}");
            }

            result.Append(CultureInfo.InvariantCulture, $"+{position.XAdvance}");
        }

        return result.ToString();
    }
}
