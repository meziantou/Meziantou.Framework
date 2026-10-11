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
