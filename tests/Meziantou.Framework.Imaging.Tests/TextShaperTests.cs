using System.Buffers.Binary;
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
        // still a mark: the missing glyph, whose ink is 384 units wide from 64, does not move the pen and is placed like
        // the mark above it stands for, in the middle of the advance of A and 64 units over its top, 768.
        builder.CharacterMap.Remove(0x0301);
        font = FontTestSupport.Load(builder);
        Assert.Equal("0=0+512", Format(Shape(font, "\u00C1")));
        Assert.Equal("2=0+1024|0=0@-768,832+0", Format(Shape(font, "A\u0301")));
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
        // The mark does not come between A and B either: the pair is kerned. The mark stays in the middle of A, whose
        // advance the kerning made 64 units shorter.
        Assert.Equal("2=0+960|9=0@64,0+0|3=2@-64,0+448", Format(Shape(font, "A\u0301B")));
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

        // The space and the characters that are not spaces have no width of their own.
        Assert.True(TextShaper.IsSynthesizedSpace(0x2007));
        Assert.True(TextShaper.IsSynthesizedSpace(0x3000));
        Assert.False(TextShaper.IsSynthesizedSpace(' '));
        Assert.False(TextShaper.IsSynthesizedSpace('A'));
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
    public void AKernedPairIsUnsafeToBreakBetween()
    {
        // A text cut between the glyphs of a kerned pair would lose the adjustment: the second glyph says so.
        var buffer = Shape(Boxes, "ABC");
        Assert.Equal([false, true, false], buffer.Info.ToArray().Select(glyph => glyph.IsUnsafeToBreak));
        buffer = Shape(Boxes, "AB", new ShapingFeature("kern", 0));
        Assert.Equal([false, false], buffer.Info.ToArray().Select(glyph => glyph.IsUnsafeToBreak));
    }

    [Fact]
    public void TheCharactersOfARightToLeftRunAreMirroredUnlessTheCallerMirroredThem()
    {
        var builder = FontSamples.Boxes();
        builder.CharacterMap['('] = FontSamples.BoxA;
        builder.CharacterMap[')'] = FontSamples.BoxB;
        var font = FontTestSupport.Load(builder);

        int Shape(bool isRightToLeft, bool areCharactersMirrored)
        {
            var buffer = new GlyphBuffer(FontResourceLimits.Default);
            TextShaper.AddText(buffer, "(");
            TextShaper.Shape(buffer, font.Face, UnicodeScript.Common, languages: [], isRightToLeft, [], areCharactersMirrored: areCharactersMirrored);
            return buffer.Info[0].Glyph;
        }

        Assert.Equal(FontSamples.BoxA, Shape(isRightToLeft: false, areCharactersMirrored: false));
        Assert.Equal(FontSamples.BoxB, Shape(isRightToLeft: true, areCharactersMirrored: false));
        Assert.Equal(FontSamples.BoxA, Shape(isRightToLeft: true, areCharactersMirrored: true));
    }

    [Fact]
    public void TheLayoutTablesCanBeQueriedWithoutShaping()
    {
        var builder = FontSamples.Boxes();
        builder.RawTables["GSUB"] = FontTestSupport.BuildSingleSubstitutions(("smcp", FontSamples.BoxA, FontSamples.BoxB), ("vert", FontSamples.BoxC, FontSamples.BoxA));
        var face = FontTestSupport.Load(builder).Face;
        var smcp = ShapingFeature.ToTag("smcp");
        Assert.True(LayoutQueries.HasFeature(face, isPositioning: false, smcp));
        Assert.False(LayoutQueries.HasFeature(face, isPositioning: true, smcp));
        Assert.False(LayoutQueries.HasFeature(face, isPositioning: false, ShapingFeature.ToTag("c2sc")));
        Assert.True(LayoutQueries.HasFeature(face, isPositioning: false, UnicodeScript.Latin, smcp));

        // What a feature does to a glyph on its own.
        Assert.True(LayoutQueries.WouldSubstitute(face, UnicodeScript.Latin, smcp, FontSamples.BoxA));
        Assert.False(LayoutQueries.WouldSubstitute(face, UnicodeScript.Latin, smcp, FontSamples.BoxC));
        Assert.Equal(FontSamples.BoxB, LayoutQueries.GetSingleSubstitution(face, smcp, FontSamples.BoxA, LayoutQueries.DefaultScript));
        Assert.Equal(FontSamples.BoxC, LayoutQueries.GetSingleSubstitution(face, smcp, FontSamples.BoxC, LayoutQueries.DefaultScript));
        Assert.True(LayoutQueries.HasVerticalAlternate(face, FontSamples.BoxC));
        Assert.False(LayoutQueries.HasVerticalAlternate(face, FontSamples.BoxA));

        // A font without positioning table has the pairs of its legacy kerning table, on the advance of the first glyph.
        var (first, second) = LayoutQueries.GetPairAdjustment(Boxes.Face, ShapingFeature.ToTag("kern"), FontSamples.BoxA, FontSamples.BoxB, LayoutQueries.DefaultScript);
        Assert.Equal(-128, first.XAdvance);
        Assert.Equal(default, second);
        Assert.Equal(default, LayoutQueries.GetPairAdjustment(Boxes.Face, ShapingFeature.ToTag("kern"), FontSamples.BoxA, FontSamples.BoxC, LayoutQueries.DefaultScript));
    }

    [Fact]
    public void TheGlyphsOfARunAreLimited()
    {
        // The limit is checked when the buffer of a run grows beyond its first size.
        var text = new string('A', 40);
        var buffer = new GlyphBuffer(new FontResourceLimits { MaxTextGlyphs = 40 });
        TextShaper.AddText(buffer, text);
        Assert.Equal(40, buffer.Length);

        buffer = new GlyphBuffer(new FontResourceLimits { MaxTextGlyphs = 39 });
        var exception = Assert.Throws<FontResourceLimitException>(() => TextShaper.AddText(buffer, text));
        Assert.Equal(FontResourceLimitKind.TextGlyphs, exception.Kind);
        Assert.Equal(39, exception.Limit);
        Assert.Equal(40, exception.Requested);
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void TheGlyphsOfABufferStayInOrderWhateverTheEditsAre()
    {
        // Glyphs replaced by more or fewer glyphs, and the components of ligatures removed at once.
        var random = new Random(42);
        var buffer = new GlyphBuffer(FontResourceLimits.Default);
        var expected = new List<int>();
        var next = 0;
        for (var i = 0; i < 20; i++)
        {
            buffer.Add(next, next);
            expected.Add(next++);
        }

        for (var step = 0; step < 2000; step++)
        {
            var index = random.Next(expected.Count + 1);
            if (random.Next(3) == 0 && expected.Count > 4)
            {
                // Removed glyphs at increasing indexes, as the components of a ligature.
                var indexes = Enumerable.Range(0, expected.Count).OrderBy(_ => random.Next()).Take(random.Next(1, 4)).Order().ToArray();
                buffer.Remove(indexes);
                for (var j = indexes.Length - 1; j >= 0; j--)
                {
                    expected.RemoveAt(indexes[j]);
                }
            }
            else
            {
                var oldCount = random.Next(Math.Min(3, expected.Count - Math.Min(index, expected.Count)) + 1);
                var newCount = random.Next(4);
                buffer.Resize(index, oldCount, newCount);
                expected.RemoveRange(index, oldCount);
                for (var j = 0; j < newCount; j++)
                {
                    buffer.Info[index + j].Glyph = next;
                    expected.Insert(index + j, next++);
                }
            }

            Assert.Equal(expected, buffer.Info.ToArray().Select(info => info.Glyph));
        }
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

        // A script from its ISO 15924 code; the code of Hiragana and Katakana together is the one of Katakana.
        Assert.Equal(UnicodeScript.Latin, OpenTypeScript.FromIso15924(ShapingFeature.ToTag("Latn")));
        Assert.Equal(UnicodeScript.Arabic, OpenTypeScript.FromIso15924(ShapingFeature.ToTag("Arab")));
        Assert.Equal(UnicodeScript.Katakana, OpenTypeScript.FromIso15924(ShapingFeature.ToTag("Hrkt")));
        Assert.Equal(UnicodeScript.Unknown, OpenTypeScript.FromIso15924(ShapingFeature.ToTag("Qaaa")));
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
        Assert.Equal(expected, GetForms(ParseCharacters(text), before: [], after: []));
    }

    [Theory]
    [InlineData("", "0628", "", "isol")]
    [InlineData("0628", "0628", "", "fina")]
    [InlineData("", "0628", "0628", "init")]
    [InlineData("0628", "0628", "0628", "medi")]
    [InlineData("064E 0628", "0628", "064E 0628", "medi")]
    [InlineData("0627", "0628", "", "isol")]
    [InlineData("0020", "0628 0627", "", "init fina")]
    [InlineData("0628", "0627", "0628", "fina")]
    public void TheLettersAtTheEndsOfARunJoinTheLettersAroundIt(string before, string text, string after, string expected)
    {
        // The context is the text around the run, from the nearest character: the marks in it are transparent, as in
        // the run, and an alef, which joins on its right side only, does not join the letter after it.
        Assert.Equal(expected, GetForms(ParseCharacters(text), ParseCharacters(before), ParseCharacters(after)));
    }

    [Fact]
    public void JoinedLettersAreUnsafeToBreakBetween()
    {
        var buffer = new GlyphBuffer(FontResourceLimits.Default);
        foreach (var (codePoint, cluster) in ((int, int)[])[(0x0628, 0), (0x0628, 1), (0x0020, 2), (0x0628, 3)])
        {
            buffer.Add(codePoint, cluster);
        }

        ArabicShaper.SetFormMasks(buffer, firstBit: 5);
        Assert.Equal([false, true, false, false], buffer.Info.ToArray().Select(glyph => glyph.IsUnsafeToBreak));
    }

    private static int[] ParseCharacters(string text) => text.Length == 0 ? [] : [.. text.Split(' ').Select(item => int.Parse(item, NumberStyles.HexNumber, CultureInfo.InvariantCulture))];

    private static string GetForms(int[] text, int[] before, int[] after)
    {
        string[] names = ["isol", "fina", "fin2", "fin3", "medi", "med2", "init"];
        var buffer = new GlyphBuffer(FontResourceLimits.Default);
        for (var i = 0; i < text.Length; i++)
        {
            buffer.Add(text[i], i);
        }

        // The context is given from the nearest character
        buffer.SetContext([.. before.Reverse()], after);
        ArabicShaper.SetFormMasks(buffer, firstBit: 5);
        var forms = buffer.Info.ToArray().Select(glyph =>
        {
            var isLetter = UnicodeProperties.GetGeneralCategory(glyph.CodePoint) == UnicodeCategory.OtherLetter;
            var bits = glyph.Mask >> 5;
            return !isLetter ? "." : bits == 0 ? "none" : System.Numerics.BitOperations.IsPow2(bits) ? names[System.Numerics.BitOperations.Log2(bits)] : "several";
        });
        return string.Join(' ', forms);
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
            var buffer = new GlyphBuffer(FontResourceLimits.Default);
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

    [Fact]
    public void TheRingOfASaraAmGoesBeforeTheMarksAboveItsConsonant()
    {
        // Thai: ko kai (2), mai ek (3), nikhahit (4), sara aa (5), sara u (6), sara i (7), phinthu (8). The font does not
        // have the sara am itself, which is always drawn with the nikhahit and the sara aa.
        var builder = FontSamples.Boxes();
        builder.CharacterMap[0x0E01] = FontSamples.BoxA;
        builder.CharacterMap[0x0E48] = FontSamples.BoxB;
        builder.CharacterMap[0x0E4D] = FontSamples.BoxC;
        builder.CharacterMap[0x0E32] = FontSamples.TriangleD;
        builder.CharacterMap[0x0E38] = FontSamples.FrameE;
        builder.CharacterMap[0x0E34] = FontSamples.OverlapF;
        builder.CharacterMap[0x0E3A] = FontSamples.Boxa;
        builder.CharacterMap[0x0E81] = FontSamples.BoxA;
        builder.CharacterMap[0x0EC8] = FontSamples.BoxB;
        builder.CharacterMap[0x0ECD] = FontSamples.BoxC;
        builder.CharacterMap[0x0EB2] = FontSamples.TriangleD;
        var font = FontTestSupport.Load(builder);

        // The two parts belong to the cluster of the consonant.
        Assert.Equal("2=0 4=0 5=0", Clusters(font, "\u0E01\u0E33", UnicodeScript.Thai));
        Assert.Equal("4=0 5=0", Clusters(font, "\u0E33", UnicodeScript.Thai));
        Assert.Equal("2=0 4=0 5=0 4=0 5=0", Clusters(font, "\u0E01\u0E33\u0E33", UnicodeScript.Thai));

        // A tone mark, or a vowel above, is typed before the sara am: the ring is drawn under it, on the consonant.
        Assert.Equal("2=0 4=0 3=0 5=0", Clusters(font, "\u0E01\u0E48\u0E33", UnicodeScript.Thai));
        Assert.Equal("2=0 4=0 7=0 3=0 5=0", Clusters(font, "\u0E01\u0E34\u0E48\u0E33", UnicodeScript.Thai));

        // A vowel below is not above the consonant: the ring stays after it.
        Assert.Equal("2=0 6=0 4=0 3=0 5=0", Clusters(font, "\u0E01\u0E38\u0E48\u0E33", UnicodeScript.Thai));
        Assert.Equal("2=0 6=0 3=0 4=0 5=0", Clusters(font, "\u0E01\u0E48\u0E38\u0E33", UnicodeScript.Thai));

        // The vowel below is closer to the consonant than the phinthu, which Unicode orders first.
        Assert.Equal("2=0 6=0 8=0", Clusters(font, "\u0E01\u0E3A\u0E38", UnicodeScript.Thai));
        Assert.Equal("2=0 6=0 8=0", Clusters(font, "\u0E01\u0E38\u0E3A", UnicodeScript.Thai));

        // Lao has the same vowel; the other scripts leave these characters alone.
        Assert.Equal("2=0 4=0 3=0 5=0", Clusters(font, "\u0E81\u0EC8\u0EB3", UnicodeScript.Lao));
        Assert.Equal("2=0 3=0 0=2", Clusters(font, "\u0E01\u0E48\u0E33", UnicodeScript.Latin));
    }

    [Fact]
    public void LettersOfHangulAreComposedIntoTheSyllablesTheFontHas()
    {
        // A leading consonant (2), a vowel (3), a trailing consonant (4), the syllables they make without (5) and with
        // (6) the trailing consonant, a tone mark (7, with a width), the dotted circle (8) and a leading consonant of
        // Old Hangul (11), which no syllable character includes.
        var builder = FontSamples.Boxes();
        builder.CharacterMap[0x1100] = FontSamples.BoxA;
        builder.CharacterMap[0x1161] = FontSamples.BoxB;
        builder.CharacterMap[0x11A8] = FontSamples.BoxC;
        builder.CharacterMap[0xAC00] = FontSamples.TriangleD;
        builder.CharacterMap[0xAC01] = FontSamples.FrameE;
        builder.CharacterMap[0x302E] = FontSamples.OverlapF;
        builder.CharacterMap[0x25CC] = FontSamples.Boxa;
        builder.CharacterMap[0x1113] = FontSamples.ScaledH;
        var font = FontTestSupport.Load(builder);

        Assert.Equal("5=0", Clusters(font, "\u1100\u1161", UnicodeScript.Hangul));
        Assert.Equal("6=0", Clusters(font, "\u1100\u1161\u11A8", UnicodeScript.Hangul));
        Assert.Equal("6=0", Clusters(font, "\uAC00\u11A8", UnicodeScript.Hangul));
        Assert.Equal("5=0 6=1 5=2", Clusters(font, "\uAC00\uAC01\u1100\u1161", UnicodeScript.Hangul));

        // Letters that do not make a block stay letters: two leading consonants, a vowel after a complete syllable, a
        // letter of Old Hangul (whose block is one cluster), a zero width joiner between two letters.
        Assert.Equal("2=0 5=1", Clusters(font, "\u1100\u1100\u1161", UnicodeScript.Hangul));
        Assert.Equal("6=0 3=1 4=2", Clusters(font, "\uAC01\u1161\u11A8", UnicodeScript.Hangul));
        Assert.Equal("11=0 3=0 4=0", Clusters(font, "\u1113\u1161\u11A8", UnicodeScript.Hangul));
        Assert.Equal("2=0 1=0 3=2", Clusters(font, "\u1100\u200D\u1161", UnicodeScript.Hangul));

        // A tone mark is written after its syllable and drawn before it; without a syllable, it is shown with a dotted
        // circle, where the syllable would be.
        Assert.Equal("7=0 5=0", Clusters(font, "\uAC00\u302E", UnicodeScript.Hangul));
        Assert.Equal("7=0 6=0", Clusters(font, "\u1100\u1161\u11A8\u302E", UnicodeScript.Hangul));
        Assert.Equal("7=0 8=0", Clusters(font, "\u302E", UnicodeScript.Hangul));
        Assert.Equal("2=0 7=0 8=0", Clusters(font, "\u1100\u302E", UnicodeScript.Hangul));

        // A font without the syllable of a block draws it with its letters, however the text has it.
        builder.CharacterMap.Remove(0xAC01);
        font = FontTestSupport.Load(builder);
        Assert.Equal("2=0 3=0 4=0", Clusters(font, "\u1100\u1161\u11A8", UnicodeScript.Hangul));
        Assert.Equal("2=0 3=0 4=0", Clusters(font, "\uAC01", UnicodeScript.Hangul));
        Assert.Equal("2=0 3=0 4=0", Clusters(font, "\uAC00\u11A8", UnicodeScript.Hangul));
        Assert.Equal("5=0", Clusters(font, "\u1100\u1161", UnicodeScript.Hangul));

        // A tone mark without width is a mark the font places: it stays after its syllable, and its circle is before it.
        builder.CharacterMap[0x302E] = FontSamples.AcuteMark;
        font = FontTestSupport.Load(builder);
        Assert.Equal("5=0 9=0", Clusters(font, "\uAC00\u302E", UnicodeScript.Hangul));
        Assert.Equal("8=0 9=0", Clusters(font, "\u302E", UnicodeScript.Hangul));

        // The other scripts do not compose letters.
        Assert.Equal("2=0 3=1", Clusters(font, "\u1100\u1161", UnicodeScript.Latin));
    }

    [Theory]
    [InlineData("A", "A", 1)]
    [InlineData("A", "B", 0)]
    [InlineData("A B", "A B C", 2)]
    [InlineData("A B? C", "A C", 2)]
    [InlineData("A B? C", "A B C", 3)]
    [InlineData("A B? C", "A B B C", 0)]
    [InlineData("A (B | C)* A", "A B C C B A A", 6)]
    [InlineData("A (B | C)* A", "A A", 2)]
    [InlineData("A B*", "A B B B C", 4)]
    [InlineData("A B+", "A C", 0)]
    [InlineData("A B{1,2}", "A B B B", 3)]
    [InlineData("A B{0,2} C", "A B B B C", 0)]
    [InlineData("A B{2,3} C", "A B B B C", 5)]
    [InlineData("A pair*", "A B C B C B", 5)]
    [InlineData("A pair{0,1} B", "A B C B", 4)]
    [InlineData("A | A B | A B C", "A B C", 3)]
    [InlineData("(A | A B) C?", "A B", 2)]
    [InlineData("A? B?", "C", 0)]
    public void ASyllablePatternMatchesTheLongestSequence(string pattern, string text, int expected)
    {
        // The categories A, B and C are the values 1, 2 and 3; "pair" is the pattern "B C".
        var categories = new Dictionary<string, ulong>(StringComparer.Ordinal) { ["A"] = 1UL << 1, ["B"] = 1UL << 2, ["C"] = 1UL << 3 };
        var patterns = new Dictionary<string, string>(StringComparer.Ordinal) { ["pair"] = "B C" };
        var compiled = SyllablePattern.Compile(pattern, categories, patterns);
        var info = text.Split(' ').Select(name => new GlyphInfo { SyllableCategory = (byte)(name[0] - 'A' + 1) }).ToArray();
        Assert.Equal(expected, compiled.Match(info, 0));
    }

    [Theory]
    [InlineData("0915", "C1")]
    [InlineData("0915 093F 0915 093F", "C2 C2")]
    [InlineData("0915 094D 0937 093F", "C4")]
    [InlineData("0915 094D 0937 094D 0924 094D 0930 093F 0902", "C9")]
    [InlineData("0930 094D 0915 093F", "C4")]
    [InlineData("0915 093C 094D 200D 0937", "C5")]
    [InlineData("0915 094D 200C 0937", "C3 C1")]
    [InlineData("0915 094D", "C2")]
    [InlineData("0915 0902 0903 0951 0952", "C5")]
    [InlineData("0905 0902", "V2")]
    [InlineData("0930 094D 0907", "V3")]
    [InlineData("0905 094D 0915", "V3")]
    [InlineData("00A0 093F", "S2")]
    [InlineData("25CC 094D 0930", "S3")]
    [InlineData("093D 0902", "Y2")]
    [InlineData("093F", "B1")]
    [InlineData("094D 0915", "B2")]
    [InlineData("0915 093E 093E", "C3")]
    [InlineData("0915 0902 093E", "C2 B1")]
    [InlineData("0915 0902 0902 0902", "C3 B1")]
    [InlineData("0915 093C 093C 093C", "C3 B1")]
    [InlineData("0041 0915 0020 0964", "O1 C1 O1 O1")]
    [InlineData("0A15 0A02 0A40", "C3")]
    [InlineData("0A15 0A02 0A3E", "C2 B1")]
    [InlineData("0D4E 0D15 0D3F", "C3")]
    public void AnIndicTextIsCutIntoSyllables(string text, string expected)
    {
        // The patterns of the specifications: a consonant syllable (C) is consonants joined by halants, with their
        // nuktas and joiners, then vowel signs, then modifiers and Vedic signs; a vowel syllable (V) starts with a vowel
        // letter; a stand-alone cluster (S) with a generic base; a symbol (Y) is a sign that stands by itself; what
        // has no base is a broken cluster (B), and the rest is not Indic (O). Each syllable is given with its length.
        var info = ParseCodePoints(text);
        IndicShaper.FindSyllables(info);
        Assert.Equal(expected, FormatSyllables(info, "CVSYBO"));
    }

    [Theory]
    [InlineData(UnicodeScript.Devanagari, "0915 093F", "093F 0915")]
    [InlineData(UnicodeScript.Devanagari, "0915 094D 0937 093F", "0915 094D 093F 0937")]
    [InlineData(UnicodeScript.Devanagari, "0915 094D 200D 0937 093F", "093F 0915 094D 200D 0937")]
    [InlineData(UnicodeScript.Devanagari, "0915 094D 0937 094D 0924 093F", "0915 094D 0937 094D 093F 0924")]
    [InlineData(UnicodeScript.Devanagari, "0915 0902 0941", "0915 0902 25CC 0941")]
    [InlineData(UnicodeScript.Devanagari, "0915 093F 093C", "093F 093C 0915")]
    [InlineData(UnicodeScript.Devanagari, "093F", "093F 25CC")]
    [InlineData(UnicodeScript.Devanagari, "0902 0915", "25CC 0902 0915")]
    [InlineData(UnicodeScript.Bengali, "0995 09C7 09BE", "09C7 0995 09BE")]
    [InlineData(UnicodeScript.Bengali, "0995 09CD 09A4 09C7 09BE", "0995 09CD 09C7 09A4 09BE")]
    [InlineData(UnicodeScript.Tamil, "0B95 0BC6 0BBE", "0BC6 0B95 0BBE")]
    [InlineData(UnicodeScript.Tamil, "0B95 0BCD 0BB7 0BC6", "0B95 0BCD 0BC6 0BB7")]
    [InlineData(UnicodeScript.Oriya, "0B15 0B40 0B01", "0B15 0B01 0B40")]
    [InlineData(UnicodeScript.Telugu, "0C15 0C4D 0C15 0C3F", "0C15 0C4D 0C15 0C3F")]
    [InlineData(UnicodeScript.Malayalam, "0D15 0D4D 0D15 0D46", "0D15 0D4D 0D46 0D15")]
    public void TheCharactersOfAnIndicSyllableAreReordered(UnicodeScript script, string text, string expected)
    {
        // A font without any feature of the model: no consonant has a half form, a form below or after the base, or a
        // reph, so the base is the last consonant and the halants stay visible. What is left is the reordering of the
        // specifications: a vowel sign drawn before the syllable goes before it, then next to the base, after the last
        // visible halant (unless a zero width joiner follows that halant, which asks for a half form the sign stays
        // in front of); a nukta stays with what it follows; a vowel sign after a modifier is not of the syllable, and
        // a syllable without base gets a dotted circle; the candrabindu of Oriya is before the vowel signs.
        var codePoints = text.Split(' ').Select(item => int.Parse(item, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
        var builder = FontSamples.Boxes();
        foreach (var codePoint in codePoints.Append(0x25CC).Append(script == UnicodeScript.Devanagari ? 0x094D : 0))
        {
            builder.CharacterMap[codePoint] = FontSamples.BoxA;
        }

        var font = FontTestSupport.Load(builder);
        var buffer = new GlyphBuffer(FontResourceLimits.Default);
        foreach (var codePoint in codePoints)
        {
            buffer.Add(codePoint, 0);
            buffer.Info[^1].Mask = 1;
        }

        IndicShaper.FindSyllables(buffer.Info);
        var shaper = new IndicShaper(font.Face, script, ShapingFeature.ToTag("xxx2"), firstBit: 5, _ => []);
        shaper.ReorderBeforeBasicForms(buffer);
        shaper.ReorderAfterBasicForms(buffer);
        Assert.Equal(expected, string.Join(' ', buffer.Info.ToArray().Select(glyph => glyph.CodePoint.ToString("X4", CultureInfo.InvariantCulture))));
    }

    [Theory]
    [InlineData("1780", "C1")]
    [InlineData("1780 17D2 1798 17C2", "C4")]
    [InlineData("179F 17D2 178F 17D2 179A 17B8", "C6")]
    [InlineData("1780 17D2", "C2")]
    [InlineData("179F 17CA 17B8", "C3")]
    [InlineData("1780 200C 17CA 17B7", "C4")]
    [InlineData("1780 17BB 17C6", "C3")]
    [InlineData("1780 17B6 17C6", "C3")]
    [InlineData("1780 17C7 17B6", "C2 B1")]
    [InlineData("1780 17C1 17C1", "C2 B1")]
    [InlineData("17A5 17C6", "C2")]
    [InlineData("25CC 17C1", "C2")]
    [InlineData("00A0 17B7", "C2")]
    [InlineData("17C1", "B1")]
    [InlineData("17D2 1780", "B2")]
    [InlineData("0041 1780 0020 17D4", "O1 C1 O1 O1")]
    public void AKhmerTextIsCutIntoSyllables(string text, string expected)
    {
        // "Cons + {COENG + (Cons | IndV)} + [PreV | BlwV] + [RegShift] + [AbvV] + {AbvS} + [PstV] + [PstS]": a
        // consonant, an independent vowel or a generic base (C), subscripts written with the coeng, a register
        // shifter, one vowel of each place and the signs, those drawn after the syllable last. What has no base is a
        // broken cluster (B) and the rest is not Khmer (O).
        var info = ParseCodePoints(text);
        KhmerShaper.FindSyllables(info);
        Assert.Equal(expected, FormatSyllables(info, "CBO"));
    }

    [Theory]
    [InlineData("1780 17B8", "1780 17B8")]
    [InlineData("1780 17C1", "17C1 1780")]
    [InlineData("1780 17D2 1780 17C2", "17C2 1780 17D2 1780")]
    [InlineData("1780 17D2 179A", "17D2 179A 1780")]
    [InlineData("1780 17D2 1798 17D2 179A 17C1", "17C1 17D2 179A 1780 17D2 1798")]
    [InlineData("17C1", "17C1 25CC")]
    [InlineData("17D2 1780", "25CC 17D2 1780")]
    public void TheCharactersOfAKhmerSyllableAreReordered(string text, string expected)
    {
        // A vowel drawn before the syllable goes to its start, and so does the subscript ro with its coeng, whatever
        // subscripts are written before it; a syllable without base gets a dotted circle.
        var buffer = CreateBuffer(text, out var font);
        KhmerShaper.FindSyllables(buffer.Info);
        KhmerShaper.Reorder(buffer, font.Face, firstBit: 5, hasCfar: false);
        Assert.Equal(expected, FormatCodePoints(buffer));
    }

    [Theory]
    [InlineData(0x17BE)]
    [InlineData(0x17BF)]
    [InlineData(0x17C0)]
    [InlineData(0x17C4)]
    [InlineData(0x17C5)]
    public void AKhmerVowelWithTwoPartsIsDrawnWithTheVowelBeforeTheSyllable(int vowel)
    {
        // The part on the left is the vowel e; the vowel itself follows the consonant, for a feature of the font to
        // turn it into the part that remains.
        var builder = FontSamples.Boxes();
        builder.CharacterMap[0x1780] = FontSamples.BoxA;
        builder.CharacterMap[vowel] = FontSamples.BoxA;
        builder.CharacterMap[0x17C1] = FontSamples.BoxB;
        var font = FontTestSupport.Load(builder);

        Assert.Equal($"{FontSamples.BoxB}=0 {FontSamples.BoxA}=0 {FontSamples.BoxA}=0", Clusters(font, char.ConvertFromUtf32(0x1780) + char.ConvertFromUtf32(vowel), UnicodeScript.Khmer));
    }

    [Theory]
    [InlineData("1000", "C1")]
    [InlineData("1019 103C 1014 103A", "C2 C2")]
    [InlineData("1004 103A 1039 1002 101C", "C4 C1")]
    [InlineData("1000 1039 1000 1031", "C4")]
    [InlineData("1000 103B 103D 103E 1031 102D 102F 1036 1037 1038", "C10")]
    [InlineData("1000 102C 1038", "C3")]
    [InlineData("1000 103B 103B", "C2 B1")]
    [InlineData("1040 102D", "C2")]
    [InlineData("25CC 1031", "C2")]
    [InlineData("1000 FE00 1031", "C3")]
    [InlineData("1000 200D", "C2")]
    [InlineData("200D 1031", "O1 B1")]
    [InlineData("1031", "B1")]
    [InlineData("1039 1000", "B2")]
    [InlineData("0041 1000 0020 002E", "O1 C1 O1 O1")]
    public void AMyanmarTextIsCutIntoSyllables(string text, string expected)
    {
        // A syllable (C) is an optional kinzi (nga, asat, stacker), a base, the consonants stacked under it, the
        // medials in their order (ya, ra, wa, ha), the vowels (before, above, below), the anusvara, the dot below,
        // the vowels after the base and the tones. What has no base is a broken cluster (B) and the rest is not
        // Myanmar (O); a joiner does not start a syllable.
        var info = ParseCodePoints(text);
        MyanmarShaper.FindSyllables(info);
        Assert.Equal(expected, FormatSyllables(info, "CBO"));
    }

    [Theory]
    [InlineData("1000 102D", "1000 102D")]
    [InlineData("1000 1031", "1031 1000")]
    [InlineData("1000 103C", "103C 1000")]
    [InlineData("1000 103C 1031", "1031 103C 1000")]
    [InlineData("1000 1039 1000 1031", "1031 1000 1039 1000")]
    [InlineData("1004 103A 1039 1000", "1000 1004 103A 1039")]
    [InlineData("1004 103A 1039 1000 103C 1031", "1031 103C 1000 1004 103A 1039")]
    [InlineData("1000 102F 1036", "1000 1036 102F")]
    [InlineData("1000 1036 102F", "1000 1036 25CC 102F")]
    [InlineData("1031", "1031 25CC")]
    public void TheCharactersOfAMyanmarSyllableAreReordered(string text, string expected)
    {
        // The vowel e and the medial ra are drawn on the left of the base, the vowel first; the kinzi is written
        // before the base and drawn above it, so it goes after it; an anusvara written after a vowel below is put
        // before it, and a vowel below written after an anusvara is not of its syllable; a syllable without base gets a
        // dotted circle.
        var buffer = CreateBuffer(text, out var font);
        MyanmarShaper.FindSyllables(buffer.Info);
        MyanmarShaper.Reorder(buffer, font.Face);
        Assert.Equal(expected, FormatCodePoints(buffer));
    }

    [Theory]
    [InlineData("A98F", "S1")]
    [InlineData("A98F A9C0 A98F", "S3")]
    [InlineData("A98F A9C0", "S2")]
    [InlineData("A98F A9BF A9BA A9B6 A981", "S5")]
    [InlineData("A98F A9B6 A9BA", "S2 B1")]
    [InlineData("A98F A983 A981", "S2 B1")]
    [InlineData("A9BA", "B1")]
    [InlineData("A98F 200C A98F", "S2 S1")]
    [InlineData("A98F 200C A9B6", "S3")]
    [InlineData("A98F 200D A9B6", "S3")]
    [InlineData("A98F 034F A9B6", "S3")]
    [InlineData("0020 A9B6", "Y2")]
    [InlineData("0041 A98F", "Y1 S1")]
    [InlineData("2060 A9B6", "O1 B1")]
    [InlineData("A9D1 A9D2", "S1 S1")]
    [InlineData("11052", "N1")]
    [InlineData("11052 1107F 11052", "N3")]
    [InlineData("11052 1107F", "J2")]
    [InlineData("0D9A 0DCA 0DBB", "S3")]
    [InlineData("0D9A 0DD9 0DCA", "S3")]
    [InlineData("1A20 1A60 1A20", "S3")]
    [InlineData("1A20 1A60", "K2")]
    [InlineData("1A20 1A63 1A60 1A20", "S4")]
    [InlineData("11F12 11F42", "V2")]
    [InlineData("11F12 11F42 11F12", "S3")]
    [InlineData("11F02 11F12", "S2")]
    [InlineData("11F02", "B1")]
    [InlineData("111C2 11191", "S2")]
    [InlineData("111C2 2022", "S2")]
    [InlineData("13000", "H1")]
    [InlineData("13000 13430 13001", "H3")]
    [InlineData("00B2", "O1")]
    [InlineData("A98F 00B2", "S2")]
    public void AUniversalModelTextIsCutIntoClusters(string text, string expected)
    {
        // The cluster schemas of the specification of the universal shaping engine. A standard cluster (S) is a base,
        // consonants joined to it by a halant, one medial consonant, the vowels, the vowel modifiers and the final
        // consonants of each place in the order before, above, below, after; it can end with a halant. A cluster
        // that ends with an invisible stacker is a virama terminated cluster (V), with the sakot of Tai Tham a sakot
        // terminated one (K). Numbers joined by a number joiner are a numeral cluster (N), which is number-joiner
        // terminated (J) when it ends with the joiner. A symbol, a space or a letter of another script with the marks
        // that follow it is a symbol cluster (Y); hieroglyphs with their joiners a hieroglyph cluster (H). Marks
        // without a base are a broken cluster (B), and what is of no cluster is alone (O). The zero width joiner and
        // the combining grapheme joiner are of the cluster they are in; a zero width non-joiner ends a cluster,
        // unless a mark follows it.
        var info = ParseCodePoints(text);
        foreach (ref var glyph in info.AsSpan())
        {
            glyph.Category = UnicodeProperties.GetGeneralCategory(glyph.CodePoint);
        }

        UniversalShaper.FindSyllables(info);
        Assert.Equal(expected, FormatSyllables(info, "VKSJNYHBO"));
    }

    [Theory]
    [InlineData("A98F A9B6", "A98F A9B6")]
    [InlineData("A98F A9BA", "A9BA A98F")]
    [InlineData("A98F A9C0 A98F A9BA", "A98F A9C0 A9BA A98F")]
    [InlineData("0020 A9BA", "A9BA 0020")]
    [InlineData("A9BA", "A9BA 25CC")]
    [InlineData("11F02 11F12", "11F12 11F02")]
    [InlineData("11F02 11F12 11F36", "11F12 11F02 11F36")]
    [InlineData("11F02 11F12 11F42 11F12", "11F12 11F02 11F42 11F12")]
    [InlineData("11F02", "25CC 11F02")]
    public void TheGlyphsOfAUniversalModelClusterAreReordered(string text, string expected)
    {
        // "Glyph reordering": a vowel drawn before the base goes to the start of the cluster, or after the last halant
        // that no feature replaced ("reordering components do not reorder past an explicit virama"); a repha goes
        // after the base, before what follows it; a cluster without base gets a dotted circle, after its repha.
        var buffer = CreateBuffer(text, out var font);
        foreach (ref var glyph in buffer.Info)
        {
            glyph.Category = UnicodeProperties.GetGeneralCategory(glyph.CodePoint);
        }

        UniversalShaper.FindSyllables(buffer.Info);
        UniversalShaper.Reorder(buffer, font.Face);
        Assert.Equal(expected, FormatCodePoints(buffer));
    }

    [Theory]

    // A language has the tag the registry of OpenType gives for its ISO 639 code, whatever the case of the subtags and
    // the form of the code; the region and the script say nothing of most languages.
    [InlineData("sr", "SRB ")]
    [InlineData("SR-cyrl-rs", "SRB ")]
    [InlineData("srp", "SRB ")]
    [InlineData("tr", "TRK ")]
    [InlineData("fr-CA", "FRA ")]
    [InlineData("de-CH-1901", "DEU ")]

    // A code that several tags have: the tag of fewer languages first (Irish, then Irish Traditional, which is also
    // Old Irish; Navajo, then the Athapaskan languages).
    [InlineData("ga", "IRI ,IRT ")]
    [InlineData("nv", "NAV ,ATH ")]
    [InlineData("ko", "KOR ,KOH ")]

    // The tags of a written form are used when the language tag asks for the form.
    [InlineData("el", "ELL ")]
    [InlineData("el-polyton", "PGR ,ELL ")]
    [InlineData("ka", "KAT ")]
    [InlineData("ka-Geok", "KGE ,KAT ")]
    [InlineData("syr", "SYR ")]
    [InlineData("syr-Syrj", "SYRJ,SYR ")]
    [InlineData("syc-Syre", "SYRE,SYR ")]
    [InlineData("und-fonipa", "IPPH")]
    [InlineData("en-fonipa", "IPPH,ENG ")]
    [InlineData("fi-fonupa", "UPPH,FIN ")]

    // Chinese: the script, or else the region, chooses.
    [InlineData("zh", "ZHS ")]
    [InlineData("zh-Hans", "ZHS ")]
    [InlineData("zh-SG", "ZHS ")]
    [InlineData("zh-Hant", "ZHT ")]
    [InlineData("zh-TW", "ZHT ")]
    [InlineData("zh-HK", "ZHH ,ZHT ")]
    [InlineData("zh-Hant-MO", "ZHTM,ZHH ,ZHT ")]
    [InlineData("zh-Hans-HK", "ZHS ")]
    [InlineData("zh-Bopo", "ZHP ")]
    [InlineData("cmn", "ZHS ")]
    [InlineData("cmn-Hant", "ZHT ")]
    [InlineData("zh-cmn-Hant", "ZHT ")]
    [InlineData("yue", "ZHH ,ZHT ")]
    [InlineData("zh-yue", "ZHH ,ZHT ")]
    [InlineData("yue-Hans", "ZHS ")]
    [InlineData("lzh", "ZHT ")]

    // A language of a macrolanguage also has the tags of the macrolanguage; a deprecated subtag has the tags of the
    // subtag that replaces it.
    [InlineData("arb", "ARA ")]
    [InlineData("ary", "MOR ,ARA ")]
    [InlineData("zsm", "MLY ")]
    [InlineData("id", "IND ,MLY ")]
    [InlineData("in", "IND ,MLY ")]
    [InlineData("iw", "IWR ")]
    [InlineData("mo", "MOL ,ROM ")]
    [InlineData("ro-MD", "MOL ,ROM ")]
    [InlineData("ro", "ROM ")]
    [InlineData("no", "NOR ")]
    [InlineData("nn", "NYN ")]

    // No tag: an undetermined language, private use, a language that is not an ISO 639 code, a code the registry lacks.
    [InlineData("und", "")]
    [InlineData("x-klingon", "")]
    [InlineData("en-x-zh", "ENG ")]
    [InlineData("english", "")]
    [InlineData("zxx", "")]
    [InlineData("sh", "")]
    public void ALanguageTagGivesTheLanguageSystemTagsOfTheRegistry(string language, string expected)
    {
        Assert.True(OpenTypeLanguage.IsWellFormed(language));
        Assert.Equal(expected, string.Join(',', OpenTypeLanguage.GetTags(language).Select(FormatTag)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("en-")]
    [InlineData("-en")]
    [InlineData("en--US")]
    [InlineData("en_US")]
    [InlineData("1en")]
    [InlineData("abcdefghi")]
    [InlineData("en-abcdefghi")]
    [InlineData("fr\u00E9")]
    [InlineData("en US")]
    public void ALanguageTagIsMadeOfSubtagsOfLettersAndDigits(string language)
    {
        Assert.False(OpenTypeLanguage.IsWellFormed(language));
    }

    [Fact]
    public void EveryCodeOfTheRegistryGivesItsTag()
    {
        // A second reading of the registry (FontSamples.LanguageSystemRegistry) against the table of the library. The
        // tags of a written form are given by a subtag, not by the code alone.
        string[] forms = ["SYRE", "SYRJ", "SYRN", "PGR ", "KGE ", "ZHH ", "ZHP ", "ZHT ", "ZHTM"];
        var codeCount = 0;
        foreach (var item in FontSamples.LanguageSystemRegistry.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = item.Split(':');
            var tag = parts[0].TrimEnd('!').PadRight(4);
            if (parts.Length == 1 || forms.Contains(tag))
                continue;

            foreach (var code in parts[1].Split(','))
            {
                Assert.Contains(tag, OpenTypeLanguage.GetTags(code).Select(FormatTag));
                codeCount++;
            }
        }

        Assert.Equal(1107, codeCount);
    }

    [Fact]
    public void TheMarksOfAFontWithoutPositioningTableArePlacedFromTheShapesOfTheGlyphs()
    {
        // The font draws every mark at one place: the rectangle (-250, 800)-(-150, 900). A is (100, 0)-(700, 700), with
        // an advance of 1000: a mark is placed in the advance, from 0 to 1000, and on the ink, from 0 to 700. The gap
        // is a sixteenth of the em, 62 units.
        var font = FontTestSupport.Load(FontSamples.Marks());

        // Above: centered, its left at 450, so drawn from 700, which is 300 before the pen. It would stand at 762; the
        // font draws it at 800, and it is brought down by half of the 38 units.
        Assert.Equal("2=0+1000|8=0@-300,-19+0", Format(Shape(font, "A\u0300")));

        // Below: its top 62 units under the baseline. Attached below: its top on the baseline.
        Assert.Equal("2=0+1000|13=0@-300,-962+0", Format(Shape(font, "A\u0316")));
        Assert.Equal("2=0+1000|12=0@-300,-900+0", Format(Shape(font, "A\u0321")));

        // On the right, its right at the end of the advance; on the left, its left at the origin; a double mark, in
        // the middle of the end of the advance.
        Assert.Equal("2=0+1000|10=0@150,-19+0", Format(Shape(font, "A\u0315")));
        Assert.Equal("2=0+1000|18=0@-750,-962+0", Format(Shape(font, "A\u302A")));
        Assert.Equal("2=0+1000|17=0@200,-19+0|2=2+1000", Format(Shape(font, "A\u0361A")));

        // A class without place of its own (an overlay): centered, at its height.
        Assert.Equal("2=0+1000|14=0@-300,0+0", Format(Shape(font, "A\u0334")));

        // The marks of a class stack, 62 units from each other: the second one above is at 881 + 62; another class
        // starts from the letter again.
        Assert.Equal("2=0+1000|8=0@-300,-19+0|8=0@-300,143+0", Format(Shape(font, "A\u0300\u0300")));
        Assert.Equal("2=0+1000|13=0@-300,-962+0|13=0@-300,-1124+0|8=0@-300,-19+0", Format(Shape(font, "A\u0300\u0316\u0316")));

        // A mark below that the font draws lower than 62 units under the letter stays there, and the next one hangs
        // under it: 62 units under its bottom, -300. A mark above that the font draws lower is brought up.
        var low = font.Face.MapCodePoint(0x0317);
        Assert.Equal($"2=0+1000|{low}=0@-300,0+0|13=0@-300,-1262+0", Format(Shape(font, "A\u0317\u0316")));
        Assert.Equal($"2=0+1000|{font.Face.MapCodePoint(0x0302)}=0@-300,42+0", Format(Shape(font, "A\u0302")));

        // A mark without letter before it, and the marks of Thai, are left where the font draws them.
        Assert.Equal("8=0+0|2=1+1000", Format(Shape(font, "\u0300A")));
        var buffer = new GlyphBuffer(FontResourceLimits.Default);
        TextShaper.AddText(buffer, "\u0E01\u0E48");
        TextShaper.Shape(buffer, font.Face, UnicodeScript.Thai, languages: [], isRightToLeft: false, []);
        Assert.Equal((0, 0), (buffer.Positions[1].XOffset, buffer.Positions[1].YOffset));

        // From right to left, a mark is drawn before its letter, from the same pen: the Hebrew alef has an advance of
        // 600, and the sheva goes under the middle of it.
        buffer = new GlyphBuffer(FontResourceLimits.Default);
        TextShaper.AddText(buffer, "\u05D0\u05B0");
        TextShaper.Shape(buffer, font.Face, UnicodeScript.Hebrew, languages: [], isRightToLeft: true, []);
        Assert.Equal(font.Face.MapCodePoint(0x05B0), buffer.Info[0].Glyph);
        Assert.Equal((500, -962, 0), (buffer.Positions[0].XOffset, buffer.Positions[0].YOffset, buffer.Positions[0].XAdvance));
        Assert.Equal((0, 600), (buffer.Positions[1].XOffset, buffer.Positions[1].XAdvance));
    }

    [Fact]
    public void TheLettersOfAnArabicFontWithoutSubstitutionTableTakeTheirPresentationForms()
    {
        // The font has the letters and their presentation forms as characters, and no substitution table. The glyphs
        // are given from right to left.
        var font = FontTestSupport.Load(FontSamples.ArabicForms());
        int Glyph(int character) => font.Face.MapCodePoint(character);
        string Shaped(string text)
        {
            var buffer = new GlyphBuffer(FontResourceLimits.Default);
            TextShaper.AddText(buffer, text);
            TextShaper.Shape(buffer, font.Face, UnicodeScript.Arabic, languages: [], isRightToLeft: true, []);
            return string.Join(' ', buffer.Info.ToArray().Select(glyph => glyph.Glyph));
        }

        // Beh alone, then three of them: isolated (U+FE8F); final (U+FE90), medial (U+FE92), initial (U+FE91).
        Assert.Equal($"{Glyph(0xFE8F)}", Shaped("\u0628"));
        Assert.Equal($"{Glyph(0xFE90)} {Glyph(0xFE92)} {Glyph(0xFE91)}", Shaped("\u0628\u0628\u0628"));

        // Alef only joins the letter before it: beh initial then alef final; alef isolated then beh isolated.
        Assert.Equal($"{Glyph(0xFE8E)} {Glyph(0xFE91)}", Shaped("\u0628\u0627"));
        Assert.Equal($"{Glyph(0xFE8F)} {Glyph(0xFE8D)}", Shaped("\u0627\u0628"));

        // A letter that does not join, the hamza, keeps its glyph; a letter of the block Arabic Presentation Forms-A.
        Assert.Equal($"{Glyph(0xFE8F)} {Glyph(0x0621)}", Shaped("\u0621\u0628"));
        Assert.Equal($"{Glyph(0xFB57)} {Glyph(0xFB59)} {Glyph(0xFB58)}", Shaped("\u067E\u067E\u067E"));

        // Lam and alef are their ligature: isolated (U+FEFB), final after a letter that joins (U+FEFC), with the alef
        // with madda (U+FEF5). A mark between the two letters follows the ligature in the text, and is drawn first.
        Assert.Equal($"{Glyph(0xFEFB)}", Shaped("\u0644\u0627"));
        Assert.Equal($"{Glyph(0xFEFC)} {Glyph(0xFE91)}", Shaped("\u0628\u0644\u0627"));
        Assert.Equal($"{Glyph(0xFEF5)}", Shaped("\u0644\u0622"));
        Assert.Equal($"{Glyph(0x064E)} {Glyph(0xFEFB)}", Shaped("\u0644\u064E\u0627"));

        // A zero width joiner between them is passed over, and is drawn as nothing after the ligature; a zero width
        // non-joiner keeps the two letters apart, each in its isolated form.
        Assert.Equal($"{Glyph(' ')} {Glyph(0xFEFB)}", Shaped("\u0644\u200D\u0627"));
        Assert.Equal($"{Glyph(0xFE8D)} {Glyph(' ')} {Glyph(0xFEDD)}", Shaped("\u0644\u200C\u0627"));

        // The other ligatures the font has as characters are not forms a text needs: yeh with hamza and jeem.
        Assert.Equal($"{Glyph(0xFE9E)} {Glyph(0xFE8B)}", Shaped("\u0626\u062C"));

        // A font without the character of a form draws the letter with its own glyph.
        var builder = FontSamples.ArabicForms();
        builder.CharacterMap.Remove(0xFE92);
        builder.CharacterMap.Remove(0xFEFB);
        font = FontTestSupport.Load(builder);
        Assert.Equal($"{Glyph(0xFE90)} {Glyph(0x0628)} {Glyph(0xFE91)}", Shaped("\u0628\u0628\u0628"));
        Assert.Equal($"{Glyph(0xFE8E)} {Glyph(0xFEDF)}", Shaped("\u0644\u0627"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheMarksOfAThaiFontWithoutSubstitutionTableTakeTheirPrivateUseForms(bool useMacCodePoints)
    {
        // The font has the forms of the marks as characters of the private use area, at the code points of the fonts of
        // Windows or of Mac OS, and no substitution table.
        var font = FontTestSupport.Load(FontSamples.ThaiForms(useMacCodePoints));
        int Glyph(int character) => font.Face.MapCodePoint(character);
        int Form(int windows, int mac) => Glyph(useMacCodePoints ? mac : windows);
        string Shaped(string text)
        {
            var buffer = new GlyphBuffer(FontResourceLimits.Default);
            TextShaper.AddText(buffer, text);
            TextShaper.Shape(buffer, font.Face, UnicodeScript.Thai, languages: [], isRightToLeft: false, []);
            return string.Join(' ', buffer.Info.ToArray().Select(glyph => glyph.Glyph));
        }

        // Ko kai, a consonant of the usual shape: a tone mark alone is drawn lower (low mai ek); after a vowel above,
        // it keeps its place.
        Assert.Equal($"{Glyph(0x0E01)} {Form(0xF70A, 0xF88B)}", Shaped("\u0E01\u0E48"));
        Assert.Equal($"{Glyph(0x0E01)} {Glyph(0x0E34)} {Glyph(0x0E48)}", Shaped("\u0E01\u0E34\u0E48"));

        // Po pla has an ascender: the vowel above moves left (left sara i), the tone mark alone is low and left, and
        // the tone mark after a vowel is left.
        Assert.Equal($"{Glyph(0x0E1B)} {Form(0xF701, 0xF885)}", Shaped("\u0E1B\u0E34"));
        Assert.Equal($"{Glyph(0x0E1B)} {Form(0xF705, 0xF88C)}", Shaped("\u0E1B\u0E48"));
        Assert.Equal($"{Glyph(0x0E1B)} {Form(0xF701, 0xF885)} {Form(0xF713, 0xF88A)}", Shaped("\u0E1B\u0E34\u0E48"));

        // Yo ying loses its descender before a vowel below; under do chada, whose descender stays, the vowel is lower.
        Assert.Equal($"{Form(0xF70F, 0xF89A)} {Glyph(0x0E38)}", Shaped("\u0E0D\u0E38"));
        Assert.Equal($"{Glyph(0x0E0E)} {Form(0xF718, 0xF89B)}", Shaped("\u0E0E\u0E38"));

        // The sara am is a nikhahit, which counts as a vowel above, and a sara aa: the tone mark before it keeps its
        // place, on the nikhahit.
        Assert.Equal($"{Glyph(0x0E01)} {Glyph(0x0E4D)} {Glyph(0x0E48)} {Glyph(0x0E32)}", Shaped("\u0E01\u0E48\u0E33"));

        // A second mark below is lower; a mark above something that is no consonant keeps its glyph.
        Assert.Equal($"{Glyph(0x0E01)} {Glyph(0x0E38)} {Form(0xF719, 0xF89C)}", Shaped("\u0E01\u0E38\u0E39"));
        Assert.Equal($"{Glyph(0x0E32)} {Glyph(0x0E48)}", Shaped("\u0E32\u0E48"));

        // A font without the character of a form draws the mark with its own glyph.
        var builder = FontSamples.ThaiForms(useMacCodePoints);
        builder.CharacterMap.Remove(useMacCodePoints ? 0xF88B : 0xF70A);
        font = FontTestSupport.Load(builder);
        Assert.Equal($"{Glyph(0x0E01)} {Glyph(0x0E48)}", Shaped("\u0E01\u0E48"));
    }

    [Fact]
    public void TheSelectorOfAVariationSequenceTheFontHasIsPartOfItsCharacter()
    {
        // A font with two variation sequences of A: one with a glyph of its own (B), one with the glyph of A.
        var builder = FontSamples.Boxes();
        builder.CmapFormats = FontCmapFormats.Format4 | FontCmapFormats.Format14;
        builder.VariationSequences[(0xFE00, 'A')] = FontSamples.BoxB;
        builder.VariationSequences[(0xFE01, 'A')] = -1;
        var font = FontTestSupport.Load(builder);

        // The selector of a sequence the font has leaves no glyph, and what follows keeps its cluster.
        Assert.Equal("3=0+512|4=2+768", Format(Shape(font, "A\uFE00C")));
        Assert.Equal("2=0+1024|4=2+768", Format(Shape(font, "A\uFE01C")));

        // A selector the font has no sequence for is a character without visible form: a glyph that takes no room, in
        // the cluster of its character. Only the first selector after a character is the one of a sequence.
        var unknown = Shape(font, "A\uFE02C");
        Assert.Equal(3, unknown.Length);
        Assert.Equal((2, 0, 1024), (unknown.Info[0].Glyph, unknown.Info[0].Cluster, unknown.Positions[0].XAdvance));
        Assert.Equal((true, 0, 0), (unknown.Info[1].IsIgnorable, unknown.Info[1].Cluster, unknown.Positions[1].XAdvance));
        Assert.Equal(3, Shape(font, "A\uFE00\uFE00C").Length);
        Assert.Equal(FontSamples.BoxB, Shape(font, "A\uFE00\uFE00C").Info[0].Glyph);
    }

    [Fact]
    public void TheLanguageOfARunSelectsALanguageSystemOfTheFont()
    {
        // The language systems of the font, each with a feature that replaces A (glyph 2) by four glyphs, the last of
        // which tells which one it is: ROM (2), SRB (3), ZHT (4). The default language system has no feature.
        var font = FontTestSupport.Load(FontSamples.Languages(["ROM ", "SRB ", "ZHT "]));
        Assert.Equal("2", ShapeInLanguage(font, null));
        Assert.Equal("2 2 2 3", ShapeInLanguage(font, "sr"));
        Assert.Equal("2 2 2 2", ShapeInLanguage(font, "ro"));

        // The first tag of the language the font has: not the one of Moldova nor the one of Hong Kong.
        Assert.Equal("2 2 2 2", ShapeInLanguage(font, "ro-MD"));
        Assert.Equal("2 2 2 4", ShapeInLanguage(font, "zh-HK"));

        // A language the font has no language system for uses the default one, like a text without language.
        Assert.Equal("2", ShapeInLanguage(font, "fr"));
        Assert.Equal("2", ShapeInLanguage(font, "zh"));

        // Each tag of the registry selects its own language system in a font that has them all: the one of index 500 is
        // drawn with the digits 2, 1, 5 and 2 in base 6.
        font = FontTestSupport.Load(FontSamples.Languages());
        var tags = FontSamples.LanguageSystemTags();
        Assert.HasCount(680, tags);
        var buffer = new GlyphBuffer(FontResourceLimits.Default);
        TextShaper.AddText(buffer, "A");
        TextShaper.Shape(buffer, font.Face, UnicodeScript.Latin, [ShapingFeature.ToTag(tags[500])], isRightToLeft: false, []);
        Assert.Equal("4 3 7 4", string.Join(' ', buffer.Info.ToArray().Select(glyph => glyph.Glyph)));

        static string ShapeInLanguage(Font font, string? language)
        {
            var buffer = new GlyphBuffer(FontResourceLimits.Default);
            TextShaper.AddText(buffer, "A");
            TextShaper.Shape(buffer, font.Face, UnicodeScript.Latin, language is null ? [] : OpenTypeLanguage.GetTags(language), isRightToLeft: false, []);
            return string.Join(' ', buffer.Info.ToArray().Select(glyph => glyph.Glyph));
        }
    }

    private static string FormatTag(uint tag) => string.Create(4, tag, (span, value) =>
    {
        for (var i = 0; i < 4; i++)
        {
            span[i] = (char)((value >> (24 - (8 * i))) & 0xFF);
        }
    });

    [Fact]
    public void ALongTextIsShapedWithAFontThatHasManyLookups()
    {
        // Two thousand subtables are tried at each glyph: the work is in proportion with the text, whatever the font
        // has, and is not what the budget of a run bounds.
        var font = FontTestSupport.Load(CreateFontWithSubtables(lookupCount: 1, subtableCount: 2000));
        var buffer = Shape(font, new string('A', 500));
        Assert.Equal(500, buffer.Length);
        Assert.Equal(FontSamples.BoxA, buffer.Info[^1].Glyph);
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void TheDigestOfTheGlyphsALookupAppliesAtContainsEveryOneOfThem()
    {
        // A lookup is passed over at a glyph its digest does not contain: a digest may contain more glyphs than were
        // added, never fewer.
        var random = new Random(1234);
        for (var round = 0; round < 300; round++)
        {
            var digest = default(GlyphDigest);
            var glyphs = new HashSet<int>();
            var count = random.Next(1, 40);
            for (var i = 0; i < count; i++)
            {
                var first = random.Next(0, 65536);
                if (random.Next(3) == 0)
                {
                    var last = Math.Min(65535, first + random.Next(0, round % 2 == 0 ? 20 : 2000));
                    digest.AddRange(first, last);
                    for (var glyph = first; glyph <= last; glyph++)
                    {
                        glyphs.Add(glyph);
                    }
                }
                else
                {
                    digest.Add(first);
                    glyphs.Add(first);
                }
            }

            foreach (var glyph in glyphs)
            {
                Assert.True(digest.MayContain(glyph), $"glyph {glyph}");
            }
        }

        // A few glyphs leave out most of the others; an empty range adds nothing.
        var small = default(GlyphDigest);
        small.Add(5);
        small.Add(300);
        small.AddRange(7, 6);
        Assert.True(Enumerable.Range(0, 65536).Count(small.MayContain) < 200);
        Assert.False(default(GlyphDigest).MayContain(0));
        Assert.All(Enumerable.Range(0, 65536), glyph => Assert.True(GlyphDigest.All.MayContain(glyph)));
    }

    [Fact]
    public void TheSubtablesOfTheLookupsOfARunAreLimited()
    {
        // A font can make three lookups of thirty thousand subtables with a few bytes: every offset is the same one.
        var font = FontTestSupport.Load(CreateFontWithSubtables(lookupCount: 3, subtableCount: 30000));
        var exception = Assert.Throws<InvalidFontException>(() => Shape(font, "A"));
        Assert.Contains("unreasonable amount of work", exception.Message, StringComparison.Ordinal);

        // Two of them are within what a font can have.
        font = FontTestSupport.Load(CreateFontWithSubtables(lookupCount: 2, subtableCount: 30000));
        Assert.Equal(1, Shape(font, "A").Length);
    }

    [Theory]
    [InlineData(new int[] { 1, 0 })]
    [InlineData(new int[] { 1, 5, 3, 7, 255, 256, 600 })]
    [InlineData(new int[] { 1, 3, 9, 4, 12 })]
    [InlineData(new int[] { 1, 3, 9, 9, 12 })]
    [InlineData(new int[] { 2, 3, 0, 4, 0, 250, 300, 5, 1000, 1000, 65534 })]
    [InlineData(new int[] { 2, 2, 10, 20, 0, 15, 30, 11 })]
    [InlineData(new int[] { 2, 2, 20, 10, 0, 40, 50, 11 })]
    [InlineData(new int[] { 2, 2, 40, 50, 0, 10, 20, 11 })]
    [InlineData(new int[] { 1, 4, 3, 7 })]
    [InlineData(new int[] { 2, 2, 10, 20, 0 })]
    [InlineData(new int[] { 3, 1, 5 })]
    public void TheCoverageCacheGivesTheIndexesOfTheSearch(int[] words)
    {
        // The table: its format, its count, then glyphs or ranges, some out of order or overlapping, some truncated
        var table = new byte[(words.Length * 2) + 2];
        for (var i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2 + (i * 2)), (ushort)words[i]);
        }

        var cache = new CoverageCache(offset: 2);
        for (var glyph = 0; glyph < 1100; glyph++)
        {
            Assert.Equal(Find(() => LayoutCommon.GetCoverage(table, 2, glyph)), Find(() => cache.Get(table, glyph)));
        }

        static string Find(Func<int> lookup)
        {
            try
            {
                return lookup().ToString(CultureInfo.InvariantCulture);
            }
            catch (InvalidFontException)
            {
                return "invalid";
            }
        }
    }

    /// <summary>
    /// Creates a font whose <c>ccmp</c> feature has lookups that all are the same table, which has subtables that all
    /// are the same single substitution: it replaces the box B by itself.
    /// </summary>
    private static FontBuilder CreateFontWithSubtables(int lookupCount, int subtableCount)
    {
        var table = new List<byte>();
        void UInt16(int value)
        {
            table.Add((byte)(value >> 8));
            table.Add((byte)value);
        }

        // The header, then the script list: the default script, whose default language system has one feature.
        var featureList = 10 + 20;
        var lookupList = featureList + 12 + (lookupCount * 2);
        UInt16(1);
        UInt16(0);
        UInt16(10);
        UInt16(featureList);
        UInt16(lookupList);
        UInt16(1);
        table.AddRange("DFLT"u8.ToArray());
        UInt16(8);
        UInt16(4);
        UInt16(0);
        UInt16(0);
        UInt16(0xFFFF);
        UInt16(1);
        UInt16(0);

        // The feature list: ccmp, with the lookups.
        UInt16(1);
        table.AddRange("ccmp"u8.ToArray());
        UInt16(8);
        UInt16(0);
        UInt16(lookupCount);
        for (var i = 0; i < lookupCount; i++)
        {
            UInt16(i);
        }

        // The lookup list: every lookup is the same table, and every subtable of it the same one.
        UInt16(lookupCount);
        for (var i = 0; i < lookupCount; i++)
        {
            UInt16(2 + (lookupCount * 2));
        }

        UInt16(1);
        UInt16(0);
        UInt16(subtableCount);
        for (var i = 0; i < subtableCount; i++)
        {
            UInt16(6 + (subtableCount * 2));
        }

        // A single substitution, format 1: the glyphs of the coverage, plus zero.
        UInt16(1);
        UInt16(6);
        UInt16(0);
        UInt16(1);
        UInt16(1);
        UInt16(FontSamples.BoxB);

        var builder = FontSamples.Boxes();
        builder.RawTables["GSUB"] = [.. table];
        return builder;
    }

    private static GlyphInfo[] ParseCodePoints(string text)
    {
        return text.Split(' ').Select(item => new GlyphInfo { CodePoint = int.Parse(item, NumberStyles.HexNumber, CultureInfo.InvariantCulture) }).ToArray();
    }

    /// <summary>Gives each syllable as the letter of its kind and its length.</summary>
    private static string FormatSyllables(GlyphInfo[] info, string kinds)
    {
        var result = new List<string>();
        for (var start = 0; start < info.Length;)
        {
            var end = start + 1;
            while (end < info.Length && info[end].Syllable == info[start].Syllable)
            {
                end++;
            }

            result.Add(string.Create(CultureInfo.InvariantCulture, $"{kinds[info[start].Syllable & 0x0F]}{end - start}"));
            start = end;
        }

        return string.Join(' ', result);
    }

    /// <summary>Creates the glyphs of a text given as code points, with a font that has a box for each and for the dotted circle.</summary>
    private static GlyphBuffer CreateBuffer(string text, out Font font)
    {
        var codePoints = ParseCodePoints(text);
        var builder = FontSamples.Boxes();
        foreach (var item in codePoints)
        {
            builder.CharacterMap[item.CodePoint] = FontSamples.BoxA;
        }

        builder.CharacterMap[0x25CC] = FontSamples.BoxA;
        font = FontTestSupport.Load(builder);
        var buffer = new GlyphBuffer(FontResourceLimits.Default);
        foreach (var item in codePoints)
        {
            buffer.Add(item.CodePoint, 0);
            buffer.Info[^1].Mask = 1;
        }

        return buffer;
    }

    private static string FormatCodePoints(GlyphBuffer buffer)
    {
        return string.Join(' ', buffer.Info.ToArray().Select(glyph => glyph.CodePoint.ToString("X4", CultureInfo.InvariantCulture)));
    }

    /// <summary>Shapes a text in the order of its characters and gives the glyphs with their clusters.</summary>
    private static string Clusters(Font font, string text, UnicodeScript script)
    {
        var buffer = new GlyphBuffer(FontResourceLimits.Default);
        TextShaper.AddText(buffer, text);
        TextShaper.Shape(buffer, font.Face, script, languages: [], isRightToLeft: false, []);
        return string.Join(' ', buffer.Info.ToArray().Select(glyph => string.Create(CultureInfo.InvariantCulture, $"{glyph.Glyph}={glyph.Cluster}")));
    }

    /// <summary>Shapes a text in the order of its characters and gives the identifiers of the glyphs.</summary>
    private static string Glyphs(Font font, string text, UnicodeScript script)
    {
        var buffer = new GlyphBuffer(FontResourceLimits.Default);
        TextShaper.AddText(buffer, text);
        TextShaper.Shape(buffer, font.Face, script, languages: [], isRightToLeft: false, []);
        return string.Join(' ', buffer.Info.ToArray().Select(glyph => glyph.Glyph));
    }

    private static GlyphBuffer Shape(Font font, string text, params ShapingFeature[] features)
    {
        var buffer = new GlyphBuffer(FontResourceLimits.Default);
        TextShaper.AddText(buffer, text);
        TextShaper.Shape(buffer, font.Face, UnicodeScript.Latin, languages: [], isRightToLeft: false, features);
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
