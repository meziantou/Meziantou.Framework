using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Fonts;

/// <summary>
/// The text shaping and measurement (<see cref="TextShaper"/>): font runs, glyphs, advances, kerning, spacing, clusters and the
/// vertical metrics. Ahem has 1em square glyphs, so its advances are exact; Tinos (serif) kerns pairs such as "AV".
/// </summary>
public sealed class TextShapingTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions());
        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private ComputedStyle Style(string declarations)
    {
        var document = Load($"<div id=target style=\"{declarations}\"></div>");
        return document.GetStyleEngine().GetStyle(document.QuerySelector("#target")!);
    }

    private FontFallbackList Fonts(string family)
    {
        var document = Load("<div></div>");
        return document.FontSelector.GetFonts(FontDescription.Initial with { Family = family });
    }

    private static TextShapingOptions Options(double fontSize = 20, bool kerning = true) => new() { FontSize = fontSize, Kerning = kerning };

    [Fact]
    public void Ahem_Advances()
    {
        var shaped = TextShaper.Shape("Xp\U000000C9", Fonts("Ahem"), Options());
        Assert.Equal(60, shaped.Width);
        Assert.Equal(3, shaped.Glyphs.Length);
        Assert.Single(shaped.Runs.ToArray());
        Assert.Equal("Ahem", shaped.Runs[0].Font.Font.FullName);
        Assert.Equal(20, shaped.Runs[0].EmSize);
        for (var i = 0; i < 3; i++)
        {
            var glyph = shaped.Glyphs[i];
            Assert.Equal(i, glyph.Cluster);
            Assert.Equal(20, glyph.Advance);
            Assert.NotEqual(0, glyph.GlyphId);
            Assert.True(glyph.Flags.HasFlag(ShapedGlyphFlags.ClusterStart));
        }

        Assert.Equal([0d, 20, 40, 60], Enumerable.Range(0, 4).Select(shaped.GetOffset));
        Assert.Equal(40, shaped.GetWidth(1, 3));
    }

    [Fact]
    public void MeasureWidth_EmptyText()
    {
        Assert.Equal(0, TextShaper.MeasureWidth("", Fonts("Ahem"), Options()));
        var shaped = TextShaper.Shape("", Fonts("Ahem"), Options());
        Assert.Equal(0, shaped.Width);
        Assert.Empty(shaped.Glyphs.ToArray());
        Assert.Equal(0, shaped.GetIndexAtOffset(10));
    }

    [Fact]
    public void MeasureWidth_MatchesShape()
    {
        var fonts = Fonts("serif");
        const string Text = "The quick brown fox jumps over the lazy dog. AVATAR";
        var options = Options(16);
        var width = TextShaper.MeasureWidth(Text, fonts, options);
        Assert.Equal(TextShaper.ShapeUncached(Text, fonts, options).Width, width, 9);
        Assert.InRange(width, 300, 400);
    }

    [Fact]
    public void Kerning()
    {
        var fonts = Fonts("serif");
        var kerned = TextShaper.Shape("AV", fonts, Options(100));
        var unkerned = TextShaper.Shape("AV", fonts, Options(100, kerning: false));
        Assert.True(kerned.Width < unkerned.Width, $"{kerned.Width} < {unkerned.Width}");

        // The adjustment is applied to the advance of the first glyph
        Assert.Equal(unkerned.Glyphs[1].Advance, kerned.Glyphs[1].Advance);
        Assert.True(kerned.Glyphs[0].Advance < unkerned.Glyphs[0].Advance);
        var font = kerned.Runs[0].Font;
        var expected = font.Font.GetKerning(font.GetGlyph('A'), font.GetGlyph('V')) * 100 / font.Font.UnitsPerEm;
        Assert.Equal(unkerned.Width + expected, kerned.Width, 9);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("font-kerning: auto", true)]
    [InlineData("font-kerning: normal", true)]
    [InlineData("font-kerning: none", false)]
    [InlineData("text-rendering: optimizeSpeed", false)]
    [InlineData("text-rendering: optimizeSpeed; font-kerning: normal", true)]
    [InlineData("font-feature-settings: 'kern' 0", false)]
    [InlineData("font-feature-settings: 'kern' off", false)]
    [InlineData("font-kerning: none; font-feature-settings: 'kern'", true)]
    [InlineData("font-kerning: none; font-feature-settings: 'liga' 0, 'kern' on", true)]
    [InlineData("font-feature-settings: 'liga' 0", true)]
    public void Kerning_FromStyle(string declarations, bool expected)
    {
        Assert.Equal(expected, Style(declarations).ShapingOptions.Kerning);
    }

    [Fact]
    public void Kerning_StyleMeasurement()
    {
        var kerned = Style("font: 100px serif").MeasureText("AV");
        var unkerned = Style("font: 100px serif; font-kerning: none").MeasureText("AV");
        Assert.True(kerned < unkerned);
    }

    [Theory]
    [InlineData("letter-spacing: 5px", "XX X", 4 * 25)]
    [InlineData("letter-spacing: 10%", "XX X", 4 * 22)]
    [InlineData("letter-spacing: calc(10% + 1px)", "XX X", 4 * 23)]
    [InlineData("letter-spacing: -5px", "XX", 2 * 15)]
    [InlineData("word-spacing: 10px", "XX X\U000000A0X", (6 * 20) + 20)]
    [InlineData("word-spacing: 50%", "X X", (3 * 20) + 10)]
    [InlineData("word-spacing: 10px; letter-spacing: 1px", "X X", (3 * 21) + 10)]
    public void Spacing(string declarations, string text, double expected)
    {
        var style = Style("font: 20px Ahem; " + declarations);
        Assert.Equal(expected, style.MeasureText(text), 9);
        Assert.Equal(expected, style.ShapeText(text).Width, 9);
    }

    [Fact]
    public void Spacing_WordSeparatorFlag()
    {
        var shaped = Style("font: 20px Ahem; word-spacing: 4px").ShapeText("X X");
        Assert.True(shaped.Glyphs[1].Flags.HasFlag(ShapedGlyphFlags.WordSeparator));
        Assert.Equal(24, shaped.Glyphs[1].Advance);
        Assert.False(shaped.Glyphs[0].Flags.HasFlag(ShapedGlyphFlags.WordSeparator));
    }

    [Fact]
    public void FontFallback_PerCharacter()
    {
        // Ahem has no Cyrillic glyph: the installed font fallback (Tinos) renders it
        var shaped = TextShaper.Shape("X\U00000416X", Fonts("Ahem"), Options());
        Assert.Equal(3, shaped.Runs.Length);
        Assert.Equal("Ahem", shaped.Runs[0].Font.Font.FullName);
        Assert.Equal("Tinos Regular", shaped.Runs[1].Font.Font.FullName);
        Assert.Equal("Ahem", shaped.Runs[2].Font.Font.FullName);
        Assert.Equal((0, 1), (shaped.Runs[0].GlyphStart, shaped.Runs[0].GlyphCount));
        Assert.Equal((1, 1), (shaped.Runs[1].GlyphStart, shaped.Runs[1].GlyphCount));
        var tinos = shaped.Runs[1].Font;
        Assert.Equal(40 + tinos.GetAdvance(tinos.GetGlyph('\U00000416'), 20, vertical: false), shaped.Width, 9);
    }

    [Fact]
    public void MissingGlyph_UsesPrimaryFont()
    {
        // No bundled font has Thai letters: the missing glyph of the first available font
        var shaped = TextShaper.Shape("\U00000E01", Fonts("Ahem"), Options());
        Assert.Equal(0, shaped.Glyphs[0].GlyphId);
        Assert.Equal("Ahem", shaped.Runs[0].Font.Font.FullName);
        Assert.Equal(20, shaped.Width);
    }

    [Fact]
    public void CombiningMarks_Cluster()
    {
        // The bundled fonts have the Cyrillic combining marks (the GDEF class of the titlo is mark: it has no advance)
        var shaped = TextShaper.Shape("\U00000435\U00000483x", Fonts("serif"), Options());
        Assert.Equal(3, shaped.Glyphs.Length);
        Assert.Equal([0, 0, 2], shaped.Glyphs.ToArray().Select(glyph => glyph.Cluster));
        Assert.True(shaped.Glyphs[1].Flags.HasFlag(ShapedGlyphFlags.Mark));
        Assert.False(shaped.Glyphs[1].Flags.HasFlag(ShapedGlyphFlags.ClusterStart));
        Assert.Equal(0, shaped.Glyphs[1].Advance);
        Assert.NotEqual(0, shaped.Glyphs[1].GlyphId);
        Assert.True(shaped.IsClusterBoundary(0));
        Assert.False(shaped.IsClusterBoundary(1));
        Assert.True(shaped.IsClusterBoundary(2));
        Assert.True(shaped.IsClusterBoundary(3));
        Assert.Equal(0, shaped.GetClusterStart(1));

        // The offset inside a cluster is snapped to its start
        Assert.Equal(0, shaped.GetOffset(1));
        Assert.Equal(shaped.Glyphs[0].Advance, shaped.GetOffset(2));
    }

    [Fact]
    public void CombiningMarks_ComposedWithTheirBase()
    {
        // The serif font has no glyph for U+0301, but it has the precomposed U+00E9: the mark stays in the run of its base, and
        // the shaping composes them (the same glyph as the precomposed character, as in the browsers)
        var fonts = Fonts("serif");
        var decomposed = TextShaper.Shape("e\U00000301x", fonts, Options());
        var precomposed = TextShaper.Shape("\U000000E9x", fonts, Options());
        Assert.Equal(2, decomposed.Glyphs.Length);
        Assert.Equal(precomposed.Glyphs[0].GlyphId, decomposed.Glyphs[0].GlyphId);
        Assert.Equal([0, 2], decomposed.Glyphs.ToArray().Select(glyph => glyph.Cluster));
        Assert.Single(decomposed.Runs.ToArray());
    }

    [Fact]
    public void LetterSpacing_PerCluster()
    {
        // The letter spacing is added once per typographic character unit
        var shaped = TextShaper.Shape("\U00000435\U00000483\U00000435", Fonts("serif"), Options() with { LetterSpacing = 3 });
        var unspaced = TextShaper.Shape("\U00000435\U00000483\U00000435", Fonts("serif"), Options());
        Assert.Equal(unspaced.Width + 6, shaped.Width, 9);
        Assert.Equal(3, shaped.Glyphs[1].Advance);
    }

    [Fact]
    public void DefaultIgnorable_Invisible()
    {
        var shaped = TextShaper.Shape("X\U0000200BX\U0000200DX\U000000ADX", Fonts("Ahem"), Options() with { LetterSpacing = 1 });
        Assert.Equal(4 * 21, shaped.Width);
        Assert.True(shaped.Glyphs[1].IsInvisible);
        Assert.Equal(0, shaped.Glyphs[1].Advance);

        // The joiner extends the cluster of the previous character
        Assert.Equal(2, shaped.Glyphs[3].Cluster);
        Assert.True(shaped.Glyphs[3].IsInvisible);
        Assert.True(shaped.Glyphs[5].IsInvisible);
    }

    [Fact]
    public void ControlCharacters()
    {
        var fonts = Fonts("Ahem");

        // A tab and a carriage return are spaces, a line feed is invisible, the other controls have a visible glyph (U+FFFD
        // REPLACEMENT CHARACTER of a fallback font: no bundled font has the Control Pictures)
        Assert.Equal(20, TextShaper.MeasureWidth("\t", fonts, Options()));
        Assert.Equal(20, TextShaper.MeasureWidth("\r", fonts, Options()));
        Assert.Equal(0, TextShaper.MeasureWidth("\n", fonts, Options()));
        var control = TextShaper.Shape("\U00000001", fonts, Options());
        Assert.NotEqual(0, control.Glyphs[0].GlyphId);
        Assert.False(control.Glyphs[0].IsInvisible);
        Assert.True(control.Width > 0);
        Assert.Equal(control.Width, TextShaper.MeasureWidth("\U0000FFFD", fonts, Options()));
    }

    [Fact]
    public void Surrogates()
    {
        // A supplementary character is one glyph and one cluster of two UTF-16 code units; a lone surrogate is U+FFFD
        var shaped = TextShaper.Shape("\U0001D400X\uD800", Fonts("Ahem"), Options());
        Assert.Equal(4, shaped.Length);
        Assert.Equal([0, 2, 3], shaped.Glyphs.ToArray().Select(glyph => glyph.Cluster));
        Assert.False(shaped.IsClusterBoundary(1));
        Assert.Equal(shaped.Glyphs[0].Advance + shaped.Glyphs[1].Advance + shaped.Glyphs[2].Advance, shaped.Width);
        Assert.Equal(20, shaped.Glyphs[1].Advance);
    }

    [Fact]
    public void VariationSequence()
    {
        // The variation selector is ignored when the font does not support the sequence
        var shaped = TextShaper.Shape("X\U0000FE0F", Fonts("Ahem"), Options());
        Assert.Equal(20, shaped.Width);
        Assert.Equal(2, shaped.Glyphs.Length);
        Assert.Equal(0, shaped.Glyphs[1].Cluster);
        Assert.True(shaped.Glyphs[1].IsInvisible);
    }

    [Fact]
    public void RightToLeft_LogicalOrder()
    {
        var options = Options() with { IsRightToLeft = true };
        var shaped = TextShaper.Shape("X\U00000416", Fonts("Ahem"), options);
        Assert.True(shaped.IsRightToLeft);
        Assert.Equal([0, 1], shaped.Glyphs.ToArray().Select(glyph => glyph.Cluster));
        Assert.Equal(20, shaped.GetOffset(1));
    }

    [Fact]
    public void GetIndexAtOffset()
    {
        var shaped = TextShaper.Shape("XXX", Fonts("Ahem"), Options());
        Assert.Equal(0, shaped.GetIndexAtOffset(-5));
        Assert.Equal(0, shaped.GetIndexAtOffset(9));
        Assert.Equal(1, shaped.GetIndexAtOffset(11));
        Assert.Equal(1, shaped.GetIndexAtOffset(29));
        Assert.Equal(2, shaped.GetIndexAtOffset(31));
        Assert.Equal(3, shaped.GetIndexAtOffset(55));
        Assert.Equal(3, shaped.GetIndexAtOffset(100));

        var marks = TextShaper.Shape("\U00000435\U00000483X", Fonts("serif"), Options());
        Assert.Equal(2, marks.GetIndexAtOffset(marks.Glyphs[0].Advance * 0.6));
        Assert.Equal(0, marks.GetIndexAtOffset(marks.Glyphs[0].Advance * 0.4));
    }

    [Fact]
    public void Vertical_Upright()
    {
        // Ahem has no vmtx table: the vertical advance is its ascent plus its descent (1em)
        var shaped = TextShaper.Shape("XX", Fonts("Ahem"), Options() with { Orientation = TextShapingOrientation.VerticalUpright });
        Assert.True(shaped.IsVertical);
        Assert.All(shaped.Glyphs.ToArray(), glyph => Assert.True(glyph.IsUpright));
        Assert.Equal(40, shaped.Width);
        Assert.True(shaped.Runs[0].IsUpright);
    }

    [Fact]
    public void Vertical_Mixed()
    {
        // The Latin letters are rotated, the ideographs and the kana are upright (Vertical_Orientation U)
        var options = Options() with { Orientation = TextShapingOrientation.VerticalMixed };
        var shaped = TextShaper.Shape("X\U00003042\U00006C34", Fonts("serif"), options);
        Assert.False(shaped.Glyphs[0].IsUpright);
        Assert.True(shaped.Glyphs[1].IsUpright);
        Assert.True(shaped.Glyphs[2].IsUpright);
        Assert.Equal(2, shaped.Runs.Length);

        var sideways = TextShaper.Shape("X\U00003042\U00006C34", Fonts("serif"), options with { Orientation = TextShapingOrientation.VerticalSideways });
        Assert.All(sideways.Glyphs.ToArray(), glyph => Assert.False(glyph.IsUpright));
    }

    [Theory]
    [InlineData("", "Horizontal")]
    [InlineData("writing-mode: vertical-rl", "VerticalMixed")]
    [InlineData("writing-mode: vertical-lr; text-orientation: upright", "VerticalUpright")]
    [InlineData("writing-mode: vertical-rl; text-orientation: sideways", "VerticalSideways")]
    [InlineData("writing-mode: sideways-rl; text-orientation: upright", "VerticalSideways")]
    [InlineData("text-orientation: upright", "Horizontal")]
    public void Orientation_FromStyle(string declarations, string expected)
    {
        Assert.Equal(Enum.Parse<TextShapingOrientation>(expected), Style(declarations).ShapingOptions.Orientation);
    }

    [Fact]
    public void Options_FromStyle()
    {
        var options = Style("font: 24px Ahem; direction: rtl; letter-spacing: 0.5em; word-spacing: 3px").ShapingOptions;
        Assert.Equal(24, options.FontSize);
        Assert.True(options.IsRightToLeft);
        Assert.Equal(12, options.LetterSpacing);
        Assert.Equal(3, options.WordSpacing);
    }

    [Fact]
    public void SizeAdjust_EmSize()
    {
        // The em size of the font applies font-size-adjust (Ahem's x-height is 0.8em: 0.4 gives an em size of 10px at 20px)
        var style = Style("font: 20px Ahem; font-size-adjust: 0.4");
        Assert.Equal(20, style.MeasureText("XX"), 9);
        Assert.Equal(10, style.ShapeText("X").Runs[0].EmSize, 9);
    }

    [Fact]
    public void Cache()
    {
        var fonts = Fonts("Ahem");
        var first = TextShaper.Shape("Hello".AsSpan(0, 4), fonts, Options());
        Assert.Same(first, TextShaper.Shape("Hell", fonts, Options()));
        Assert.NotSame(first, TextShaper.Shape("Hell", fonts, Options(10)));
        Assert.Equal(40, TextShaper.MeasureWidth("Hell", fonts, Options(10)));
        Assert.Equal(40, TextShaper.MeasureWidth("Hel", fonts, Options(10)) + 10);
        Assert.Equal(3, fonts.ShapeCache.Count);

        // The cache is bounded
        for (var i = 0; i < TextShapeCache.MaxEntries + 10; i++)
        {
            TextShaper.MeasureWidth(i.ToString(System.Globalization.CultureInfo.InvariantCulture), fonts, Options());
        }

        Assert.InRange(fonts.ShapeCache.Count, 1, TextShapeCache.MaxEntries);
    }

    [Fact]
    public void Cache_InvalidatedWithTheFonts()
    {
        var document = Load("<div></div>");
        var fonts = document.FontSelector.GetFonts(FontDescription.Initial with { Family = "Ahem" });
        TextShaper.Shape("X", fonts, Options());

        // A change of the font faces creates new font lists (and new caches)
        document.FontSelector.Invalidate();
        var updated = document.FontSelector.GetFonts(FontDescription.Initial with { Family = "Ahem" });
        Assert.NotSame(fonts, updated);
        Assert.Equal(0, updated.ShapeCache.Count);
    }

    [Theory]
    [InlineData("small-caps", "Ab", 20 + 14)]
    [InlineData("all-small-caps", "Ab", 14 + 14)]
    [InlineData("petite-caps", "Ab", 20 + 14)]
    [InlineData("all-petite-caps", "Ab", 14 + 14)]
    [InlineData("unicase", "Ab", 14 + 20)]
    [InlineData("titling-caps", "Ab", 20 + 20)]
    [InlineData("small-caps", "1.", 20 + 20)]
    public void SmallCaps_Synthesized(string caps, string text, double width)
    {
        // Ahem has no smcp feature: the lowercase letters are uppercase letters at 70% of the font size (as Chrome), the
        // caseless characters are not changed
        var style = Style($"font: 20px Ahem; font-variant-caps: {caps}");
        var shaped = TextShaper.ShapeUncached(text, style.Fonts, style.ShapingOptions);
        Assert.Equal(width, shaped.Width, 9);
    }

    [Fact]
    public void SmallCaps_SynthesizedGlyphs()
    {
        var style = Style("font: 20px serif; font-variant-caps: small-caps");
        var shaped = TextShaper.ShapeUncached("Ab", style.Fonts, style.ShapingOptions);
        var font = shaped.Runs[0].Font;

        // The two runs have the glyphs of the capital letters, at 20px and at 14px
        Assert.Equal([font.GetGlyph('A'), font.GetGlyph('B')], shaped.Glyphs.ToArray().Select(glyph => glyph.GlyphId));
        Assert.Equal(2, shaped.Runs.Length);
        Assert.Equal(20, shaped.Runs[0].EmSize, 9);
        Assert.Equal(14, shaped.Runs[1].EmSize, 9);
        Assert.Equal(0.7, shaped.Runs[1].SynthesisScale, 6);
        Assert.Equal(20, shaped.Runs[1].BaseEmSize, 5);
        Assert.Equal(font.GetAdvance(font.GetGlyph('B'), 14, vertical: false), shaped.Glyphs[1].Advance, 9);

        // The clusters stay the ones of the text
        Assert.Equal(1, shaped.Glyphs[1].Cluster);
        Assert.True(shaped.IsCaretPosition(1));
    }

    [Theory]
    [InlineData("font-synthesis-small-caps: none")]
    [InlineData("font-synthesis: weight style")]
    public void SmallCaps_SynthesisDisabled(string declaration)
    {
        var style = Style($"font: 20px Ahem; font-variant-caps: small-caps; {declaration}");
        var shaped = TextShaper.ShapeUncached("ab", style.Fonts, style.ShapingOptions);
        Assert.Equal(40, shaped.Width, 9);
        Assert.Single(shaped.Runs.ToArray());
    }

    [Fact]
    public void SmallCaps_CaseMappingExpansion_LetterSpacing()
    {
        // The sharp s is SS and the ffi ligature is FFI (SpecialCasing.txt): the letter spacing is applied between their glyphs
        // (small-caps-letter-spacing-001/002)
        const string Text = "\U000000DF\U0000FB03";
        var style = Style("font: 20px Ahem; font-variant-caps: small-caps; letter-spacing: 10px");
        var shaped = TextShaper.ShapeUncached(Text, style.Fonts, style.ShapingOptions);
        Assert.Equal(5, shaped.Glyphs.Length);
        Assert.Equal((5 * 14) + (5 * 10), shaped.Width, 9);
        Assert.All(shaped.Glyphs.ToArray(), glyph => Assert.Equal(24, glyph.Advance, 9));
        Assert.Equal([0, 0, 1, 1, 1], shaped.Glyphs.ToArray().Select(glyph => glyph.Cluster));
        Assert.Equal(shaped.Width, TextShaper.MeasureWidth(Text, style.Fonts, style.ShapingOptions), 9);
    }

    [Fact]
    public void SmallCaps_TurkicCasing()
    {
        // The case mappings of text-transform: i is a dotted capital I in Turkish
        var document = Load("<div id=target lang=tr style=\"font: 20px serif; font-variant-caps: small-caps\"></div>");
        var style = document.GetStyleEngine().GetStyle(document.QuerySelector("#target")!);
        var shaped = TextShaper.ShapeUncached("i", style.Fonts, style.ShapingOptions);
        Assert.Equal(shaped.Runs[0].Font.GetGlyph(0x130), shaped.Glyphs[0].GlyphId);
    }

    [Theory]
    [InlineData("super", 16, -5.44)]
    [InlineData("sub", 16, 3.2)]
    [InlineData("super", 60, -20.4)]
    public void Position_Synthesized(string position, double fontSize, double shift)
    {
        // The bundled fonts have no sups/subs feature: the glyphs are reduced (0.82 up to 20px, 0.667 from 45px, as Firefox) and
        // their baseline is shifted (0.34em up, 0.2em down)
        var style = Style($"font: {fontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}px Ahem; font-variant-position: {position}");
        var shaped = TextShaper.ShapeUncached("x1", style.Fonts, style.ShapingOptions);
        var scale = fontSize <= 20 ? 0.82 : 0.667;
        Assert.Equal(fontSize * scale, shaped.Runs[0].EmSize, 9);
        Assert.Equal(2 * fontSize * scale, shaped.Width, 9);
        Assert.All(shaped.Glyphs.ToArray(), glyph => Assert.Equal(shift, glyph.OffsetY, 9));
    }

    [Fact]
    public void Position_Scale()
    {
        Assert.Equal(0.82, TextShaper.GetPositionScale(12), 9);
        Assert.Equal((0.82 + 0.667) / 2, TextShaper.GetPositionScale(32.5), 9);
        Assert.Equal(0.667, TextShaper.GetPositionScale(100), 9);
    }

    [Fact]
    public void Position_SynthesisDisabled()
    {
        var style = Style("font: 20px Ahem; font-variant-position: super; font-synthesis-position: none");
        var shaped = TextShaper.ShapeUncached("x1", style.Fonts, style.ShapingOptions);
        Assert.Equal(40, shaped.Width, 9);
        Assert.All(shaped.Glyphs.ToArray(), glyph => Assert.Equal(0, glyph.OffsetY));
    }
}
