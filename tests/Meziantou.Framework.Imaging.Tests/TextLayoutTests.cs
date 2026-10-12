using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Paragraph layout: lines, wrapping, hyphenation, alignment, direction, styles, decorations and trimming. The font is
/// the boxes font of Common/FontSamples.cs at 16 pixels per em, where everything is a whole number of pixels: A
/// advances by 16 pixels and is drawn from 2 to 14, B advances by 8 (1 to 7), C by 12 (0 to 12), a space and a hyphen
/// by 8; the ascent is 13 pixels and the descent 3, so a line is 16 pixels tall with its baseline at 13. The pair A B is
/// kerned by -2 pixels and B A by +1.
/// </summary>
public sealed class TextLayoutTests
{
    private static readonly SolidBrush White = new(new Rgba32(255, 255, 255));
    private static readonly SolidBrush Red = new(new Rgba32(255, 0, 0));
    private static readonly SolidBrush Blue = new(new Rgba32(0, 0, 255));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Font Boxes { get; } = FontTestSupport.Load(FontSamples.Boxes());

    /// <summary>
    /// The boxes font with a few more characters: the Hebrew letters alef, bet and gimel drawn as C, A and B, the
    /// parentheses as B and C, and the period as the hyphen.
    /// </summary>
    private static Font Extended { get; } = CreateExtended();

    private static TextStyle Style { get; } = new(Boxes, 16);

    private static Font CreateExtended()
    {
        var builder = FontSamples.Boxes();
        builder.CharacterMap[0x05D0] = FontSamples.BoxC;
        builder.CharacterMap[0x05D1] = FontSamples.BoxA;
        builder.CharacterMap[0x05D2] = FontSamples.BoxB;
        builder.CharacterMap['('] = FontSamples.BoxB;
        builder.CharacterMap[')'] = FontSamples.BoxC;
        builder.CharacterMap['.'] = FontSamples.HyphenGlyph;
        builder.KerningPairs.Clear();
        return FontTestSupport.Load(builder);
    }

    [Fact]
    public void LinesAreWrappedAtTheWidthOfTheLayout()
    {
        // "AA " is 40 pixels and "AA AA" 72: each line holds one word. The space that ends a wrapped line hangs.
        var layout = TextLayout.Create("AA AA AA", Style, new TextLayoutOptions { MaxWidth = 40 });
        Assert.Equal(3, layout.LineCount);
        Assert.Equal(32, layout.Width);
        Assert.Equal(48, layout.Height);
        Assert.Equal(13, layout.Baseline);
        Assert.False(layout.IsTruncated);
        Assert.Equal(
            new[] { new TextLine(0, 3, 0, 0, 32, 16, 13, false), new TextLine(3, 3, 0, 16, 32, 16, 13, false), new TextLine(6, 2, 0, 32, 32, 16, 13, false) },
            layout.Lines);
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), A(16, 0), A(0, 16), A(16, 16), A(0, 32), A(16, 32));

        // A line that fits exactly is not broken; one pixel less and it is.
        Assert.Equal(1, TextLayout.Create("AA AA", Style, new TextLayoutOptions { MaxWidth = 72 }).LineCount);
        Assert.Equal(2, TextLayout.Create("AA AA", Style, new TextLayoutOptions { MaxWidth = 71 }).LineCount);

        // Without wrapping, and without width, lines only break where the text says.
        layout = TextLayout.Create("AA AA\nA", Style, new TextLayoutOptions { MaxWidth = 40, Wrapping = TextWrapping.NoWrap });
        Assert.Equal(2, layout.LineCount);
        Assert.Equal(72, layout.Width);
        Assert.Equal(new TextLine(0, 6, 0, 0, 72, 16, 13, false), layout.Lines[0]);
        Assert.Equal(new TextLine(6, 1, 0, 16, 16, 16, 13, false), layout.Lines[1]);
        Assert.Equal(2, TextLayout.Create("AA AA\nA", Style).LineCount);

        // The spaces that end a paragraph are part of its width: only the spaces of a wrapped line hang.
        Assert.Equal(40, TextLayout.Create("AA ", Style, new TextLayoutOptions { MaxWidth = 100 }).Width);
        Assert.Equal(32, TextLayout.Create("AA   AA", Style, new TextLayoutOptions { MaxWidth = 40 }).Width);
    }

    [Fact]
    public void AWordThatIsTooWideOverflowsOrIsCut()
    {
        // Four A are 64 pixels. By default a word is not cut; it can be, where no other break is possible.
        var layout = TextLayout.Create("AAAA", Style, new TextLayoutOptions { MaxWidth = 40 });
        Assert.Equal(1, layout.LineCount);
        Assert.Equal(64, layout.Width);

        layout = TextLayout.Create("AAAA", Style, new TextLayoutOptions { MaxWidth = 40, OverflowWrap = TextOverflowWrap.Anywhere });
        Assert.Equal(2, layout.LineCount);
        Assert.Equal(new TextLine(0, 2, 0, 0, 32, 16, 13, false), layout.Lines[0]);
        Assert.Equal(new TextLine(2, 2, 0, 16, 32, 16, 13, false), layout.Lines[1]);

        // A line has at least one character, however narrow the layout.
        layout = TextLayout.Create("AAA", Style, new TextLayoutOptions { MaxWidth = 1, OverflowWrap = TextOverflowWrap.Anywhere });
        Assert.Equal(3, layout.LineCount);

        // A word that fits is still kept whole: it goes to the next line.
        layout = TextLayout.Create("B AAA", Style, new TextLayoutOptions { MaxWidth = 50, OverflowWrap = TextOverflowWrap.Anywhere });
        Assert.Equal(new TextLine(0, 2, 0, 0, 8, 16, 13, false), layout.Lines[0]);
        Assert.Equal(new TextLine(2, 3, 0, 16, 48, 16, 13, false), layout.Lines[1]);

        // Breaking between any two letters is also a rule of line breaking.
        Assert.Equal(2, TextLayout.Create("AAAA", Style, new TextLayoutOptions { MaxWidth = 40, WordBreak = TextWordBreak.BreakAll }).LineCount);
    }

    [Fact]
    public void AWordIsHyphenatedAtASoftHyphen()
    {
        // "AA", a soft hyphen, "AA": 64 pixels. Cut at the soft hyphen, the first line ends with a hyphen of 8 pixels.
        const string Text = "AA\u00ADAA";
        var layout = TextLayout.Create(Text, Style, new TextLayoutOptions { MaxWidth = 44 });
        Assert.Equal(2, layout.LineCount);
        Assert.Equal(new TextLine(0, 3, 0, 0, 40, 16, 13, false), layout.Lines[0]);
        Assert.Equal(new TextLine(3, 2, 0, 16, 32, 16, 13, false), layout.Lines[1]);
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), A(16, 0), Hyphen(32, 0), A(0, 16), A(16, 16));

        // The hyphen must fit too. A soft hyphen where the line is not cut draws nothing.
        Assert.Equal(1, TextLayout.Create(Text, Style, new TextLayoutOptions { MaxWidth = 39 }).LineCount);
        layout = TextLayout.Create(Text, Style, new TextLayoutOptions { MaxWidth = 100 });
        Assert.Equal(64, layout.Width);
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), A(16, 0), A(32, 0), A(48, 0));

        // Hyphenation can be turned off, and the hyphen can be another text.
        Assert.Equal(1, TextLayout.Create(Text, Style, new TextLayoutOptions { MaxWidth = 44, Hyphenation = TextHyphenation.None }).LineCount);
        layout = TextLayout.Create(Text, Style, new TextLayoutOptions { MaxWidth = 44, HyphenCharacter = "B" });
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), A(16, 0), B(30, 0), A(0, 16), A(16, 16));
    }

    [Fact]
    public void AWordIsHyphenatedByThePatternsOfItsStyle()
    {
        // Patterns that allow a break between any two letters, at least one letter from each end of the word.
        var hyphenator = Hyphenator.Parse("a1a", options: new HyphenatorOptions { LeftHyphenMin = 1, RightHyphenMin = 1 });
        var style = new TextStyle(Boxes, 16) { Hyphenator = hyphenator };
        var options = new TextLayoutOptions { MaxWidth = 60, Hyphenation = TextHyphenation.Automatic };

        // Six A are 96 pixels. Three A and a hyphen are 56 pixels, four are 72: the word is cut after the third.
        var layout = TextLayout.Create("AAAAAA", style, options);
        Assert.Equal(2, layout.LineCount);
        Assert.Equal(new TextLine(0, 3, 0, 0, 56, 16, 13, false), layout.Lines[0]);
        Assert.Equal(new TextLine(3, 3, 0, 16, 48, 16, 13, false), layout.Lines[1]);
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), A(16, 0), A(32, 0), Hyphen(48, 0), A(0, 16), A(16, 16), A(32, 16));

        // After a word that fits: "B " is 16 pixels, and 44 are left for two A and a hyphen.
        layout = TextLayout.Create("B AAAA", style, options);
        Assert.Equal(new TextLine(0, 4, 0, 0, 56, 16, 13, false), layout.Lines[0]);
        Assert.Equal(new TextLine(4, 2, 0, 16, 32, 16, 13, false), layout.Lines[1]);

        // Not without patterns, and not unless the layout asks for it.
        Assert.Equal(1, TextLayout.Create("AAAAAA", Style, options).LineCount);
        Assert.Equal(1, TextLayout.Create("AAAAAA", style, new TextLayoutOptions { MaxWidth = 60 }).LineCount);
    }

    [Fact]
    public void LinesAreAlignedInTheWidthOfTheLayout()
    {
        // Two lines of 32 and 8 pixels, in 40 pixels.
        static TextLayout Create(TextAlignment alignment, double maxWidth = 40)
            => TextLayout.Create("AA\nB", Style, new TextLayoutOptions { MaxWidth = maxWidth, Alignment = alignment });

        var layout = Create(TextAlignment.Start);
        Assert.Equal((0, 0), (layout.Lines[0].X, layout.Lines[1].X));
        layout = Create(TextAlignment.Center);
        Assert.Equal((4, 16), (layout.Lines[0].X, layout.Lines[1].X));
        AssertRectangles(layout, new PointD(0, 0), A(4, 0), A(20, 0), B(16, 16));
        layout = Create(TextAlignment.End);
        Assert.Equal((8, 32), (layout.Lines[0].X, layout.Lines[1].X));
        AssertRectangles(layout, new PointD(0, 0), A(8, 0), A(24, 0), B(32, 16));

        // Without a width, lines are aligned in the width of the widest one.
        layout = Create(TextAlignment.Center, double.PositiveInfinity);
        Assert.Equal((0, 12), (layout.Lines[0].X, layout.Lines[1].X));
        layout = Create(TextAlignment.End, double.PositiveInfinity);
        Assert.Equal((0, 24), (layout.Lines[0].X, layout.Lines[1].X));
        Assert.Equal(32, layout.Width);
    }

    [Fact]
    public void JustifiedLinesAreStretchedByTheirSpaces()
    {
        // "A A A A" in 60 pixels: "A A A" is 64, so the first line is "A A", 40 pixels, and its space gets the 20 that
        // are left. The last line of a paragraph is not stretched.
        var layout = TextLayout.Create("A A A A", Style, new TextLayoutOptions { MaxWidth = 60, Alignment = TextAlignment.Justify });
        Assert.Equal(2, layout.LineCount);
        Assert.Equal(new TextLine(0, 4, 0, 0, 60, 16, 13, false), layout.Lines[0]);
        Assert.Equal(new TextLine(4, 3, 0, 16, 40, 16, 13, false), layout.Lines[1]);
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), A(44, 0), A(0, 16), A(24, 16));

        // Three words: each of the two spaces gets half. A line without space is not stretched.
        layout = TextLayout.Create("B B B A", Style, new TextLayoutOptions { MaxWidth = 50, Alignment = TextAlignment.Justify });
        Assert.Equal(new TextLine(0, 6, 0, 0, 50, 16, 13, false), layout.Lines[0]);
        AssertRectangles(layout, new PointD(0, 0), B(0, 0), B(21, 0), B(42, 0), A(0, 16));
        layout = TextLayout.Create("AA AA", Style, new TextLayoutOptions { MaxWidth = 40, Alignment = TextAlignment.Justify });
        Assert.Equal(32, layout.Lines[0].Width);
    }

    [Fact]
    public void RightToLeftTextIsOrderedForDisplay()
    {
        var style = new TextStyle(Extended, 16);

        // Alef then bet, drawn as C (12 pixels) then A (16): bet is on the left of alef. The paragraph starts from the
        // right, where its lines are aligned.
        var layout = TextLayout.Create("\u05D0\u05D1", style);
        Assert.Equal(28, layout.Width);
        Assert.True(layout.Lines[0].IsRightToLeft);
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), C(16, 0));
        layout = TextLayout.Create("\u05D0\u05D1", style, new TextLayoutOptions { MaxWidth = 40 });
        Assert.Equal(12, layout.Lines[0].X);
        AssertRectangles(layout, new PointD(0, 0), A(12, 0), C(28, 0));
        layout = TextLayout.Create("\u05D0\u05D1", style, new TextLayoutOptions { MaxWidth = 40, Alignment = TextAlignment.End });
        Assert.Equal(0, layout.Lines[0].X);

        // Hebrew inside a left-to-right paragraph: B, then the Hebrew letters from right to left, then B.
        layout = TextLayout.Create("B\u05D0\u05D1B", style);
        Assert.False(layout.Lines[0].IsRightToLeft);
        Assert.Equal(44, layout.Width);
        AssertRectangles(layout, new PointD(0, 0), B(0, 0), A(8, 0), C(24, 0), B(36, 0));

        // Latin inside a right-to-left paragraph keeps its order, and comes after (on the left of) the Hebrew before it.
        layout = TextLayout.Create("\u05D0 BA", style);
        Assert.True(layout.Lines[0].IsRightToLeft);
        AssertRectangles(layout, new PointD(0, 0), B(0, 0), A(8, 0), C(32, 0));

        // The direction of a paragraph can be imposed: the text keeps its order, the line starts from the right.
        layout = TextLayout.Create("BA", style, new TextLayoutOptions { Direction = TextDirection.RightToLeft, MaxWidth = 40 });
        Assert.True(layout.Lines[0].IsRightToLeft);
        AssertRectangles(layout, new PointD(0, 0), B(16, 0), A(24, 0));
        layout = TextLayout.Create("\u05D0\u05D1", style, new TextLayoutOptions { Direction = TextDirection.LeftToRight, MaxWidth = 40 });
        Assert.False(layout.Lines[0].IsRightToLeft);
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), C(16, 0));
    }

    [Fact]
    public void MirroredCharactersAreMirroredInRightToLeftText()
    {
        var style = new TextStyle(Extended, 16);

        // The parentheses are drawn as B (opening) and C (closing). Around a Hebrew letter, the opening parenthesis is on
        // the right and is drawn as a closing one.
        var layout = TextLayout.Create("(\u05D1)", style);
        Assert.Equal(36, layout.Width);
        AssertRectangles(layout, new PointD(0, 0), B(0, 0), A(8, 0), C(24, 0));

        // In left-to-right text, they are what they are.
        AssertRectangles(TextLayout.Create("(A)", style), new PointD(0, 0), B(0, 0), A(8, 0), C(24, 0));
    }

    [Fact]
    public void RightToLeftLinesAreWrappedAndTheirSpacesHangOnTheLeft()
    {
        var style = new TextStyle(Extended, 16);

        // Two words of two bet (drawn as A) with a space: 32, 8 and 32 pixels, in 40.
        var layout = TextLayout.Create("\u05D1\u05D1 \u05D1\u05D1", style, new TextLayoutOptions { MaxWidth = 40 });
        Assert.Equal(2, layout.LineCount);
        Assert.Equal(new TextLine(0, 3, 8, 0, 32, 16, 13, true), layout.Lines[0]);
        Assert.Equal(new TextLine(3, 2, 8, 16, 32, 16, 13, true), layout.Lines[1]);
        AssertRectangles(layout, new PointD(0, 0), A(8, 0), A(24, 0), A(8, 16), A(24, 16));
    }

    [Fact]
    public void LinesFollowEachOtherWhateverCutsThem()
    {
        // Cases found by TextLayoutFuzzTests.
        var style = new TextStyle(Extended, 16);

        // An ellipsis on a line that has no character: the empty second paragraph, when the third one does not fit.
        var layout = TextLayout.Create("A\n\nA", style, new TextLayoutOptions { MaxLines = 2, Trimming = TextTrimming.Character, Ellipsis = "B" });
        Assert.True(layout.IsTruncated);
        Assert.Equal(new[] { new TextLine(0, 2, 0, 0, 16, 16, 13, false), new TextLine(2, 1, 0, 16, 8, 16, 13, false) }, layout.Lines);
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), B(0, 16));

        // The same at the end of the text, where the empty line follows a line break, with spacing.
        layout = TextLayout.Create("A\n", new TextStyle(Extended, 16) { LetterSpacing = 2 }, new TextLayoutOptions { MaxHeight = 40, MaxWidth = 4, Wrapping = TextWrapping.NoWrap, Trimming = TextTrimming.Character, Ellipsis = "B" });
        Assert.Equal(2, layout.LineCount);
        Assert.Equal((0, 2, 2, 0), (layout.Lines[0].Start, layout.Lines[0].Length, layout.Lines[1].Start, layout.Lines[1].Length));

        // A trimmed line still holds the line break of its paragraph: the next line starts after it.
        layout = TextLayout.Create("AAAA\r\nA", style, new TextLayoutOptions { MaxWidth = 40, Wrapping = TextWrapping.NoWrap, Trimming = TextTrimming.Character, Ellipsis = "B" });
        Assert.Equal(new[] { new TextLine(0, 6, 0, 0, 40, 16, 13, false), new TextLine(6, 1, 0, 16, 16, 16, 13, false) }, layout.Lines);

        // A character and its mark in two spans that have their own fonts: each part uses the fonts of its span.
        var builder = new FontBuilder { CmapFormats = FontCmapFormats.Format4, UnitsPerEm = 1024, Ascender = 832, Descender = -192 };
        builder.AddGlyph(FontGlyph.Rectangle(256, 0, 0, 128, 128));
        builder.AddGlyph(FontGlyph.Rectangle(512, 0, 0, 512, 512), 'x');
        var other = FontTestSupport.Load(builder);
        var text = new RichTextBuilder()
            .Append("A", new TextStyle(new FontStack(other, other, Boxes), 16))
            .Append("\u0301", new TextStyle(Boxes, 16))
            .Build();
        layout = TextLayout.Create(text);
        Assert.Equal(1, layout.LineCount);
        Assert.Equal(16, layout.Width);
        Assert.False(layout.HasMissingGlyphs);
    }

    [Fact]
    public void TheSpansOfARichTextHaveTheirOwnPaintAndAreShapedTogether()
    {
        // Two letters of two colors: they are still a pair, kerned by -2 pixels.
        var text = new RichTextBuilder()
            .Append("A", new TextStyle(Boxes, 16) { Brush = Red })
            .Append("B", new TextStyle(Boxes, 16) { Brush = Blue })
            .Append("A", new TextStyle(Boxes, 16))
            .Build();
        Assert.Equal("ABA", text.Text);
        Assert.Equal(3, text.Spans.Count);
        Assert.Equal((1, 1), (text.Spans[1].Start, text.Spans[1].Length));

        var layout = TextLayout.Create(text);
        Assert.Equal(39, layout.Width);
        AssertRectangles(layout, new PointD(0, 0), (A(0, 0).Rectangle, Red), (B(14, 0).Rectangle, Blue), (A(23, 0).Rectangle, null));

        // Sizes: a line is as tall as its tallest font, and every glyph is on its baseline.
        text = new RichTextBuilder().Append("A", new TextStyle(Boxes, 16)).Append("A", new TextStyle(Boxes, 32)).Build();
        layout = TextLayout.Create(text);
        Assert.Equal(48, layout.Width);
        Assert.Equal(32, layout.Height);
        Assert.Equal(26, layout.Baseline);
        AssertRectangles(layout, new PointD(0, 0), (new RectangleD(2, 14, 12, 12), null), (new RectangleD(20, 2, 24, 24), null));

        // The texts of one style that follow each other are one span; an empty text adds nothing.
        var style = new TextStyle(Boxes, 16);
        text = new RichTextBuilder().Append("A", style).Append("", Style).Append("B", style).Build();
        Assert.Single(text.Spans);
        Assert.Equal(2, text.Spans[0].Length);
    }

    [Fact]
    public void DecorationsAreDrawnWithTheMetricsOfTheFont()
    {
        // The underline of the font is 1 pixel thick, 2 pixels under the baseline; its strikethrough is 1 pixel thick,
        // 4 pixels above; the line over the text is at the ascent. The baseline is at 13.
        var layout = TextLayout.Create("A A", new TextStyle(Boxes, 16) { Decorations = TextDecorations.Underline });
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), A(24, 0), (new RectangleD(0, 15, 40, 1), null));
        Assert.Equal(RectangleD.FromLTRB(0, 1, 40, 16), layout.InkBounds);

        layout = TextLayout.Create("A", new TextStyle(Boxes, 16) { Decorations = TextDecorations.Strikethrough | TextDecorations.Overline, DecorationBrush = Red });
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), (new RectangleD(0, 9, 16, 1), Red), (new RectangleD(0, 0, 16, 1), Red));

        // A decoration follows its span, on every line, without the spaces that hang at the end of a wrapped line.
        var text = new RichTextBuilder()
            .Append("B", new TextStyle(Boxes, 16))
            .Append("B B", new TextStyle(Boxes, 16) { Decorations = TextDecorations.Underline, Brush = Blue })
            .Build();
        layout = TextLayout.Create(text, new TextLayoutOptions { MaxWidth = 20 });
        Assert.Equal(2, layout.LineCount);
        AssertRectangles(
            layout,
            new PointD(0, 0),
            (B(0, 0).Rectangle, null),
            (B(8, 0).Rectangle, Blue),
            (new RectangleD(8, 15, 8, 1), Blue),
            (B(0, 16).Rectangle, Blue),
            (new RectangleD(0, 31, 8, 1), Blue));

        // The path of a layout has its decorations.
        layout = TextLayout.Create("A", new TextStyle(Boxes, 16) { Decorations = TextDecorations.Underline });
        Assert.Equal(RectangleD.FromLTRB(0, 1, 16, 16), layout.ToPath(new PointD(0, 0)).Bounds);
        Assert.Equal(Render(24, 20, canvas => canvas.FillText(White, layout, new PointD(1, 2))), Render(24, 20, canvas => canvas.Fill(White, layout.ToPath(new PointD(1, 2)))));
    }

    [Fact]
    public void SpacingIsAddedToLettersAndWords()
    {
        // 4 pixels after each letter: the second A is 20 pixels after the first, and the text is 40 pixels wide.
        var layout = TextLayout.Create("AA", new TextStyle(Boxes, 16) { LetterSpacing = 4 });
        Assert.Equal(40, layout.Width);
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), A(20, 0));

        // 8 pixels more for each space between words; negative values tighten.
        layout = TextLayout.Create("A A", new TextStyle(Boxes, 16) { WordSpacing = 8 });
        Assert.Equal(48, layout.Width);
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), A(32, 0));
        layout = TextLayout.Create("AA", new TextStyle(Boxes, 16) { LetterSpacing = -2 });
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), A(14, 0));

        // Spacing counts when lines are wrapped.
        Assert.Equal(2, TextLayout.Create("AA", new TextStyle(Boxes, 16) { LetterSpacing = 4 }, new TextLayoutOptions { MaxWidth = 39, OverflowWrap = TextOverflowWrap.Anywhere }).LineCount);
    }

    [Fact]
    public void FeaturesOfTheStyleAreApplied()
    {
        // The kerning of the pair A B, turned off; the last setting of a feature wins.
        Assert.Equal(22, TextLayout.Create("AB", new TextStyle(Boxes, 16)).Width);
        Assert.Equal(24, TextLayout.Create("AB", new TextStyle(Boxes, 16) { Features = [new FontFeature("kern", 0)] }).Width);
        Assert.Equal(22, TextLayout.Create("AB", new TextStyle(Boxes, 16) { Features = [new FontFeature("kern", 0), new FontFeature("kern")] }).Width);

        var feature = new FontFeature("liga", 0);
        Assert.Equal(("liga", 0), (feature.Tag, feature.Value));
        Assert.Equal("liga=0", feature.ToString());
        Assert.Equal(new FontFeature("liga", 0), feature);
        Assert.True(feature != new FontFeature("liga"));
        Assert.Equal(feature.GetHashCode(), new FontFeature("liga", 0).GetHashCode());
        Assert.Throws<ArgumentNullException>("tag", () => new FontFeature(null!));
        Assert.Throws<ArgumentException>("tag", () => new FontFeature("lig"));
        Assert.Throws<ArgumentException>("tag", () => new FontFeature("lig\u00E9"));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new FontFeature("liga", -1));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new FontFeature("liga", 65536));
    }

    [Fact]
    public void TextThatDoesNotFitIsCutAndTrimmed()
    {
        var style = new TextStyle(Extended, 16);

        // Six A on a line of 60 pixels that is not wrapped. The font has no ellipsis: three periods, drawn as hyphens of
        // 8 pixels. Two A and the ellipsis are 56 pixels.
        var layout = TextLayout.Create("AAAAAA", style, new TextLayoutOptions { MaxWidth = 60, Wrapping = TextWrapping.NoWrap, Trimming = TextTrimming.Character });
        Assert.True(layout.IsTruncated);
        Assert.Equal(56, layout.Width);
        Assert.Equal(new TextLine(0, 6, 0, 0, 56, 16, 13, false), layout.Lines[0]);
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), A(16, 0), Hyphen(32, 0), Hyphen(40, 0), Hyphen(48, 0));

        // Without trimming, the line overflows and nothing is cut.
        layout = TextLayout.Create("AAAAAA", style, new TextLayoutOptions { MaxWidth = 60, Wrapping = TextWrapping.NoWrap });
        Assert.False(layout.IsTruncated);
        Assert.Equal(96, layout.Width);

        // The ellipsis can be another text, here a B of 8 pixels, and the line can be cut after a word. "A AA" is 56
        // pixels: with the B it does not fit, so the line is cut after the first word, or, by character, after "A A"
        // (40 pixels). The space before the ellipsis is dropped.
        layout = TextLayout.Create("A AA AA", style, new TextLayoutOptions { MaxWidth = 60, Wrapping = TextWrapping.NoWrap, Trimming = TextTrimming.Word, Ellipsis = "B" });
        Assert.True(layout.IsTruncated);
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), B(16, 0));
        layout = TextLayout.Create("A AA AA", style, new TextLayoutOptions { MaxWidth = 60, Wrapping = TextWrapping.NoWrap, Trimming = TextTrimming.Character, Ellipsis = "B" });
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), A(24, 0), B(40, 0));

        // A number of lines: the text after is not in the layout, and the last line can end with the ellipsis.
        layout = TextLayout.Create("A\nA\nA", style, new TextLayoutOptions { MaxLines = 2 });
        Assert.True(layout.IsTruncated);
        Assert.Equal(2, layout.LineCount);
        Assert.Equal(32, layout.Height);
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), A(0, 16));
        layout = TextLayout.Create("A\nA\nA", style, new TextLayoutOptions { MaxLines = 2, Trimming = TextTrimming.Character, Ellipsis = "B" });
        Assert.Equal(2, layout.LineCount);
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), A(0, 16), B(16, 16));
        Assert.False(TextLayout.Create("A\nA", style, new TextLayoutOptions { MaxLines = 2, Trimming = TextTrimming.Character }).IsTruncated);

        // A height: the lines that do not fit are left out, except the first one.
        layout = TextLayout.Create("A\nA\nA", style, new TextLayoutOptions { MaxHeight = 40 });
        Assert.True(layout.IsTruncated);
        Assert.Equal(2, layout.LineCount);
        layout = TextLayout.Create("A\nA", style, new TextLayoutOptions { MaxHeight = 5 });
        Assert.Equal(1, layout.LineCount);
        Assert.True(layout.IsTruncated);
        Assert.False(TextLayout.Create("A\nA", style, new TextLayoutOptions { MaxHeight = 32 }).IsTruncated);

        // Wrapped lines and a number of lines: "A A A" in 30 pixels is three lines; two are kept.
        layout = TextLayout.Create("A A A", style, new TextLayoutOptions { MaxWidth = 30, MaxLines = 2, Trimming = TextTrimming.Character, Ellipsis = "B" });
        Assert.Equal(2, layout.LineCount);
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), A(0, 16), B(16, 16));
    }

    [Fact]
    public void ACharacterIsDrawnWithTheFirstFontOfTheStackThatHasIt()
    {
        // A font that only has A, as a square of 8 pixels on the baseline, before the boxes font.
        var builder = new FontBuilder { CmapFormats = FontCmapFormats.Format4, UnitsPerEm = 1024, Ascender = 832, Descender = -192, FamilyName = "Squares" };
        builder.AddGlyph(FontGlyph.Rectangle(256, 0, 0, 128, 128));
        builder.AddGlyph(FontGlyph.Rectangle(512, 0, 0, 512, 512), 'A');
        var squares = FontTestSupport.Load(builder);
        var stack = new FontStack(squares, Boxes);
        Assert.Same(squares, stack.Primary);
        Assert.Equal(new[] { squares, Boxes }, stack.Fonts);

        var layout = TextLayout.Create("AB", new TextStyle(stack, 16));
        Assert.False(layout.HasMissingGlyphs);
        Assert.Equal(16, layout.Width);
        AssertRectangles(layout, new PointD(0, 0), (new RectangleD(0, 5, 8, 8), null), (B(8, 0).Rectangle, null));

        // A character no font has: the missing glyph of the primary font, a square of 2 pixels.
        layout = TextLayout.Create("AZ", new TextStyle(stack, 16));
        Assert.True(layout.HasMissingGlyphs);
        AssertRectangles(layout, new PointD(0, 0), (new RectangleD(0, 5, 8, 8), null), (new RectangleD(8, 11, 2, 2), null));

        // A mark goes with its base: the font of a character and its marks is one font. The first font has neither the
        // composed character nor the mark; the second one has the composed character.
        layout = TextLayout.Create("A\u0301", new TextStyle(stack, 16));
        Assert.False(layout.HasMissingGlyphs);
        Assert.Equal(16, layout.Width);

        // A font converts to a stack of one font.
        FontStack single = Boxes;
        Assert.Same(Boxes, single.Primary);
        Assert.Single(single.Fonts);
        Assert.Same(Boxes, FontStack.FromFont(Boxes).Primary);
        Assert.Throws<ArgumentNullException>("primary", () => new FontStack(null!));
        Assert.Throws<ArgumentNullException>("fallbacks", () => new FontStack(Boxes, squares, null!));
    }

    [Fact]
    public void TheOptionsShapeTheLinesOfTheLayout()
    {
        // The distance between tabulation stops, in spaces of 8 pixels.
        AssertRectangles(TextLayout.Create("\tB", Style, new TextLayoutOptions { TabSize = 4 }), new PointD(0, 0), B(32, 0));
        AssertRectangles(TextLayout.Create("B\tB", Style, new TextLayoutOptions { TabSize = 2 }), new PointD(0, 0), B(0, 0), B(16, 0));

        // The indent of the first line of each paragraph, which counts in the width that is left for it.
        var layout = TextLayout.Create("A A\nA", Style, new TextLayoutOptions { MaxWidth = 45, FirstLineIndent = 10 });
        Assert.Equal(3, layout.LineCount);
        Assert.Equal((10, 0, 10), (layout.Lines[0].X, layout.Lines[1].X, layout.Lines[2].X));
        Assert.Equal(26, layout.Width);
        AssertRectangles(layout, new PointD(0, 0), A(10, 0), A(0, 16), A(10, 32));

        // The height of the lines: what it adds to the height of the font is split above and below.
        layout = TextLayout.Create("A\nA", Style, new TextLayoutOptions { LineHeight = 32 });
        Assert.Equal(64, layout.Height);
        Assert.Equal(21, layout.Baseline);
        Assert.Equal(new TextLine(2, 1, 0, 32, 16, 32, 21, false), layout.Lines[1]);
        AssertRectangles(layout, new PointD(0, 0), A(0, 8), A(0, 40));
        layout = TextLayout.Create("A\nA", Style, new TextLayoutOptions { LineHeight = 12 });
        Assert.Equal(24, layout.Height);
        Assert.Equal(11, layout.Baseline);
    }

    [Fact]
    public void TheNumberOfLinesIsLimitedByTheConfigurationOfTheFont()
    {
        var font = FontTestSupport.Load(FontSamples.Boxes(), new FontLoadOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxTextLines = 2 } } });
        var style = new TextStyle(font, 16);
        Assert.Equal(2, TextLayout.Create("A\nA", style).LineCount);
        var exception = Assert.Throws<ImageResourceLimitException>(() => TextLayout.Create("A\nA\nA", style));
        Assert.Equal(ImageResourceLimitKind.TextLines, exception.Kind);
        Assert.Equal(2, exception.Limit);
        Assert.Equal(3, exception.Requested);
        Assert.Throws<ImageResourceLimitException>(() => TextLayout.Create("A A A", style, new TextLayoutOptions { MaxWidth = 20 }));

        // A number of lines the caller asks for cuts the text instead.
        var layout = TextLayout.Create("A\nA\nA", style, new TextLayoutOptions { MaxLines = 2 });
        Assert.True(layout.IsTruncated);
        Assert.Equal(2, layout.LineCount);
    }

    [Fact]
    public void SettingsAreValidated()
    {
        Assert.Throws<ArgumentNullException>("fonts", () => new TextStyle(null!, 16));
        Assert.Throws<ArgumentOutOfRangeException>("size", () => new TextStyle(Boxes, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextStyle(Boxes, 16) { LetterSpacing = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextStyle(Boxes, 16) { WordSpacing = 1e9 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextStyle(Boxes, 16) { Decorations = (TextDecorations)8 });
        Assert.Throws<ArgumentNullException>(() => new TextStyle(Boxes, 16) { Features = null! });
        var style = new TextStyle(Boxes, 16);
        Assert.Empty(style.Features);
        Assert.Null(style.Brush);
        Assert.Equal(TextDecorations.None, style.Decorations);

        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { MaxWidth = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { MaxWidth = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { MaxHeight = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { MaxLines = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { TabSize = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { TabSize = 1025 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { LineHeight = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { FirstLineIndent = double.PositiveInfinity });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { Alignment = (TextAlignment)4 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { Direction = (TextDirection)3 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { Wrapping = (TextWrapping)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { WordBreak = (TextWordBreak)3 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { LineBreak = (TextLineBreak)3 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { OverflowWrap = (TextOverflowWrap)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { Hyphenation = (TextHyphenation)3 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { Trimming = (TextTrimming)3 });
        Assert.Throws<ArgumentException>(() => new TextLayoutOptions { Ellipsis = new string('.', 17) });
        Assert.Throws<ArgumentException>(() => new TextLayoutOptions { HyphenCharacter = new string('-', 17) });
        var options = TextLayoutOptions.Default;
        Assert.Equal((double.PositiveInfinity, double.PositiveInfinity, int.MaxValue, 8), (options.MaxWidth, options.MaxHeight, options.MaxLines, options.TabSize));
        Assert.Equal((TextWrapping.Wrap, TextAlignment.Start, TextDirection.Auto, TextHyphenation.Manual, TextTrimming.None), (options.Wrapping, options.Alignment, options.Direction, options.Hyphenation, options.Trimming));

        Assert.Throws<ArgumentNullException>("text", () => TextLayout.Create(null!, Style));
        Assert.Throws<ArgumentNullException>("style", () => TextLayout.Create("A", (TextStyle)null!));
        Assert.Throws<ArgumentNullException>("text", () => TextLayout.Create((RichText)null!));
        Assert.Throws<ArgumentException>("text", () => TextLayout.Create(new RichTextBuilder().Build()));
        Assert.Throws<ArgumentNullException>("text", () => new RichTextBuilder().Append(null!, Style));
        Assert.Throws<ArgumentNullException>("style", () => new RichTextBuilder().Append("A", null!));
        var builder = new RichTextBuilder().Append("A", Style);
        var first = builder.Build();
        builder.Clear();
        Assert.Empty(builder.Build().Spans);
        Assert.Equal("A", first.Text);
    }

    [Fact]
    public void ATextOfSeveralPaintsIsDrawnWithEachOfThem()
    {
        // Three paints, a gradient among them, with decorations: every pixel is what drawing each part alone gives.
        var gradient = new LinearGradientBrush(new PointD(0, 0), new PointD(60, 0), [new GradientStop(0, new Rgba32(255, 255, 0)), new GradientStop(1, new Rgba32(0, 255, 255))]);
        var text = new RichTextBuilder()
            .Append("AB", new TextStyle(Boxes, 16) { Brush = Red, Decorations = TextDecorations.Underline })
            .Append("C", new TextStyle(Boxes, 16) { Brush = gradient })
            .Append("A", new TextStyle(Boxes, 16) { Decorations = TextDecorations.Strikethrough, DecorationBrush = Blue })
            .Build();
        var layout = TextLayout.Create(text);

        var actual = Render(64, 20, canvas => canvas.FillText(White, layout, new PointD(1, 2)), PixelFormat.Rgba32);
        var expected = Render(
            64,
            20,
            canvas =>
            {
                canvas.FillRectangle(Red, new RectangleD(3, 3, 12, 12));
                canvas.FillRectangle(Red, new RectangleD(16, 7, 6, 8));
                canvas.FillRectangle(Red, new RectangleD(1, 17, 22, 1));
                canvas.FillRectangle(gradient, new RectangleD(23, 2, 12, 16));
                canvas.FillRectangle(White, new RectangleD(37, 3, 12, 12));
                canvas.FillRectangle(Blue, new RectangleD(35, 11, 16, 1));
            },
            PixelFormat.Rgba32);
        Assert.Equal(expected, actual);
    }

    private static (RectangleD Rectangle, Brush? Brush) A(double x, double y) => (new RectangleD(x + 2, y + 1, 12, 12), null);

    private static (RectangleD Rectangle, Brush? Brush) B(double x, double y) => (new RectangleD(x + 1, y + 5, 6, 8), null);

    private static (RectangleD Rectangle, Brush? Brush) C(double x, double y) => (new RectangleD(x, y, 12, 16), null);

    private static (RectangleD Rectangle, Brush? Brush) Hyphen(double x, double y) => (new RectangleD(x + 1, y + 8, 6, 2), null);

    /// <summary>
    /// Asserts that a layout paints exactly the given rectangles of whole pixels, each with its brush or, without one,
    /// with the brush of the call. The arguments are the position of the pen and of the top of the line for the letters.
    /// </summary>
    private static void AssertRectangles(TextLayout layout, PointD origin, params (RectangleD Rectangle, Brush? Brush)[] rectangles)
    {
        var expected = Render(120, 70, canvas =>
        {
            foreach (var (rectangle, brush) in rectangles)
            {
                canvas.FillRectangle(brush ?? White, new RectangleD(origin.X + rectangle.X, origin.Y + rectangle.Y, rectangle.Width, rectangle.Height));
            }
        });
        Assert.Equal(expected, Render(120, 70, canvas => canvas.FillText(White, layout, origin)));
    }

    private static int[] Render(int width, int height, DrawingAction action, PixelFormat format = PixelFormat.Rgb24)
    {
        using var image = DrawingTestSupport.Create(format, width, height);
        image.Draw(action, Ct);
        return DrawingTestSupport.ReadSamples(image.Frames[0]);
    }
    [Fact]
    public void ALongParagraphIsBrokenLineAfterLine()
    {
        // 600 words of 22 pixels (A is 16 pixels wide, B is 8, and the pair is kerned by 2), with spaces of 8 pixels:
        // five words and the four spaces between them are 142 pixels wide. After each line, only the start of the
        // text that follows is shaped again, far enough for the next line: every line is the same, to the last one.
        var text = string.Concat(Enumerable.Repeat("AB ", 599)) + "AB";
        var layout = TextLayout.Create(text, new TextStyle(Boxes, 16), new TextLayoutOptions { MaxWidth = 142 });

        Assert.HasCount(120, layout.Lines);
        for (var i = 0; i < layout.Lines.Count; i++)
        {
            Assert.Equal(i * 15, layout.Lines[i].Start);
            Assert.Equal(i == 119 ? 14 : 15, layout.Lines[i].Length);
            Assert.Equal(142, layout.Lines[i].Width);
        }

        // One pixel less, and a line has four words.
        layout = TextLayout.Create(text, new TextStyle(Boxes, 16), new TextLayoutOptions { MaxWidth = 141 });
        Assert.HasCount(150, layout.Lines);
        Assert.Equal(112, layout.Lines[149].Width);
        Assert.Equal(149 * 12, layout.Lines[149].Start);
    }
}
