using System.Buffers.Binary;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
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

    /// <summary>
    /// The boxes font with characters that are upright in a vertical line: the ideographs one, two and three drawn as
    /// A, B and C, and the ideographic comma as the hyphen. The left corner bracket, drawn as B, is upright only with
    /// a vertical form, which the font does not have. The Hebrew letters alef and bet are drawn as C and A. The font
    /// has no vertical metrics: an upright glyph advances by the ascent and the descent, 16 pixels, and is centered
    /// in that advance and on its line.
    /// </summary>
    private static Font Ideographs { get; } = CreateIdeographs();

    private static TextStyle Style { get; } = new(Boxes, 16);

    private static Font CreateIdeographs(Action<FontBuilder>? configure = null)
    {
        var builder = FontSamples.Boxes();
        builder.CharacterMap[0x4E00] = FontSamples.BoxA;
        builder.CharacterMap[0x4E8C] = FontSamples.BoxB;
        builder.CharacterMap[0x4E09] = FontSamples.BoxC;
        builder.CharacterMap[0x3001] = FontSamples.HyphenGlyph;
        builder.CharacterMap[0x300C] = FontSamples.BoxB;
        builder.CharacterMap[0x05D0] = FontSamples.BoxC;
        builder.CharacterMap[0x05D1] = FontSamples.BoxA;
        configure?.Invoke(builder);
        return FontTestSupport.Load(builder);
    }

    [Fact]
    public void HintingIsOffByDefault()
    {
        Assert.Equal(TextHinting.None, TextLayoutOptions.Default.Hinting);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { Hinting = (TextHinting)3 });

        // A font without instructions, at a size where everything is a whole number of pixels, is not changed.
        foreach (var hinting in (TextHinting[])[TextHinting.Light, TextHinting.Full])
        {
            var hinted = TextLayout.Create("ABC", new TextStyle(Boxes, 16), new TextLayoutOptions { Hinting = hinting });
            var unhinted = TextLayout.Create("ABC", new TextStyle(Boxes, 16));
            Assert.Equal(unhinted.Width, hinted.Width);
            Assert.Equal(unhinted.InkBounds, hinted.InkBounds);
        }

        // A vertical layout is not hinted.
        var vertical = new TextLayoutOptions { WritingMode = TextWritingMode.VerticalRightToLeft, Hinting = TextHinting.Full };
        var font = Font.Load(HintedFontPath("DroidSans"));
        Assert.Equal(TextLayout.Create("Hello", new TextStyle(font, 13), new TextLayoutOptions { WritingMode = TextWritingMode.VerticalRightToLeft }).InkBounds, TextLayout.Create("Hello", new TextStyle(font, 13), vertical).InkBounds);
    }

    [Theory]
    [InlineData("DroidSans")]
    [InlineData("OpenSans-Regular")]
    [InlineData("Roboto-Regular")]
    public void FullHintingPlacesTheGlyphsOnWholePixels(string name)
    {
        var font = Font.Load(HintedFontPath(name));
        for (var size = 9; size <= 16; size++)
        {
            const string Text = "Hamburgefonstiv AVATAR 0123";
            var layout = TextLayout.Create(Text, new TextStyle(font, size), new TextLayoutOptions { Hinting = TextHinting.Full, Alignment = TextAlignment.Center, MaxWidth = 333.3 });

            // The advances are whole pixels, the lines start on a whole pixel and the baselines are on whole pixels:
            // every caret is on the grid.
            Assert.Equal(Math.Round(layout.Lines[0].Width, MidpointRounding.AwayFromZero), layout.Lines[0].Width);
            for (var i = 0; i <= Text.Length; i++)
            {
                var caret = layout.GetCaretRectangle(i);
                Assert.Equal(Math.Round(caret.X, MidpointRounding.AwayFromZero), caret.X);
            }

            // What is measured is what is drawn: the path of the layout fills the pixels the layout fills.
            Assert.Equal(Render(layout, path: false), Render(layout, path: true));
        }

        static int[] Render(TextLayout layout, bool path)
        {
            using var image = new Image<Gray8>(340, 24, new Gray8(0));
            image.Draw(canvas =>
            {
                if (path)
                {
                    canvas.Fill(White, layout.ToPath(new PointD(2, 2)));
                }
                else
                {
                    canvas.FillText(White, layout, new PointD(2, 2));
                }
            }, Ct);
            return DrawingTestSupport.ReadSamples(image.Frames[0]);
        }
    }

    [Fact]
    public void LightHintingOnlyMovesTheOutlinesVertically()
    {
        var font = Font.Load(HintedFontPath("OpenSans-Regular"));
        for (var size = 10; size <= 16; size++)
        {
            var unhinted = TextLayout.Create("Hamburgefonstiv", new TextStyle(font, size));
            var light = TextLayout.Create("Hamburgefonstiv", new TextStyle(font, size), new TextLayoutOptions { Hinting = TextHinting.Light });
            Assert.Equal(unhinted.Width, light.Width);
            Assert.Equal(unhinted.InkBounds.Left, light.InkBounds.Left);
            Assert.Equal(unhinted.InkBounds.Right, light.InkBounds.Right);

            // The top of H (the cap height) and the baseline are on whole pixels.
            var capital = TextLayout.Create("H", new TextStyle(font, size + 0.3), new TextLayoutOptions { Hinting = TextHinting.Light });
            Assert.Equal(Math.Round(capital.InkBounds.Top, MidpointRounding.AwayFromZero), capital.InkBounds.Top, 1e-9);
            Assert.Equal(Math.Round(capital.InkBounds.Bottom, MidpointRounding.AwayFromZero), capital.InkBounds.Bottom, 1e-9);
        }
    }

    [Theory]
    [InlineData("DroidSans")]
    [InlineData("OpenSans-Regular")]
    [InlineData("Roboto-Regular")]
    public void HintingMakesSmallTextSharper(string name)
    {
        // The horizontal edges (the baseline, the x-height, the horizontal stems) are sharper at every size with light
        // hinting, and on average with the instructions of the font, which also fit the vertical edges.
        var font = Font.Load(HintedFontPath(name));
        var none = 0.0;
        var full = 0.0;
        for (var size = 10; size <= 16; size++)
        {
            var unhinted = MeasureContrast(font, size, TextHinting.None, vertical: true);
            Assert.True(MeasureContrast(font, size, TextHinting.Light, vertical: true) > unhinted * 1.02, $"{size} px");
            none += unhinted;
            full += MeasureContrast(font, size, TextHinting.Full, vertical: true);
        }

        Assert.True(full > none * 1.05);
    }

    /// <summary>
    /// Gets how sharp the edges of a text are along an axis: the sum of the differences between neighbor pixels across
    /// that axis, divided by the sum of the coverage. Edges across pixels are spread over two pixels of smaller
    /// differences; the coverage of the text hardly depends on hinting.
    /// </summary>
    internal static double MeasureContrast(Font font, double size, TextHinting hinting, bool vertical)
    {
        var layout = TextLayout.Create("Hamburgefonstiv quickly jumps over 0123456789", new TextStyle(font, size), new TextLayoutOptions { Hinting = hinting });
        var width = (int)layout.Width + 4;
        var height = (int)layout.Height + 4;
        using var image = new Image<Gray8>(width, height, new Gray8(0));
        image.Draw(canvas => canvas.FillText(White, layout, new PointD(2, 2)), Ct);
        var samples = DrawingTestSupport.ReadSamples(image.Frames[0]);
        long variation = 0;
        long coverage = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sample = samples[(y * width) + x];
                coverage += sample;
                if (vertical && y > 0)
                {
                    variation += Math.Abs(sample - samples[((y - 1) * width) + x]);
                }
                else if (!vertical && x > 0)
                {
                    variation += Math.Abs(sample - samples[(y * width) + x - 1]);
                }
            }
        }

        return (double)variation / coverage;
    }

    private static FullPath HintedFontPath(string name) => FixtureRoot.GetDirectory() / "font-suites" / "truetype-hinting" / (name + ".subset.ttf");

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
    public void TextWithoutSpacesIsBrokenBetweenTheWordsOfADictionary()
    {
        // Six Thai consonants, each drawn as a box of 16 pixels: ko kai, kho khai, kho khuat, kho khwai, kho khon and
        // kho rakhang. The dictionary has the words made of the first two, of the next three and of the last one.
        var builder = FontSamples.Boxes();
        for (var character = 0x0E01; character <= 0x0E06; character++)
        {
            builder.CharacterMap[character] = FontSamples.BoxA;
        }

        var style = new TextStyle(FontTestSupport.Load(builder), 16);
        const string Text = "\u0E01\u0E02\u0E03\u0E04\u0E05\u0E06";
        var dictionary = LineBreakDictionary.Parse("\u0E01\u0E02\n\u0E03\u0E04\u0E05\n\u0E06\n");

        // Without a dictionary, the text has no place to break at: it overflows, or is cut anywhere.
        Assert.Equal(1, TextLayout.Create(Text, style, new TextLayoutOptions { MaxWidth = 50 }).LineCount);
        Assert.Equal(2, TextLayout.Create(Text, style, new TextLayoutOptions { MaxWidth = 50, OverflowWrap = TextOverflowWrap.Anywhere }).LineCount);

        // With it, a line ends before a word: two words do not fit 50 pixels, a word of three letters fits.
        var layout = TextLayout.Create(Text, style, new TextLayoutOptions { MaxWidth = 50, LineBreakDictionary = dictionary });
        Assert.Equal(3, layout.LineCount);
        Assert.Equal((0, 2), (layout.Lines[0].Start, layout.Lines[0].Length));
        Assert.Equal((2, 3), (layout.Lines[1].Start, layout.Lines[1].Length));
        Assert.Equal((5, 1), (layout.Lines[2].Start, layout.Lines[2].Length));
        Assert.Equal(48, layout.Width);

        // Wider: the first two words on a line.
        layout = TextLayout.Create(Text, style, new TextLayoutOptions { MaxWidth = 80, LineBreakDictionary = dictionary });
        Assert.Equal(2, layout.LineCount);
        Assert.Equal((0, 5), (layout.Lines[0].Start, layout.Lines[0].Length));

        // The dictionary changes nothing to a text of another script, nor to a text that is not wrapped.
        Assert.Equal(1, TextLayout.Create("AAAAAA", style, new TextLayoutOptions { MaxWidth = 50, LineBreakDictionary = dictionary }).LineCount);
        Assert.Equal(1, TextLayout.Create(Text, style, new TextLayoutOptions { LineBreakDictionary = dictionary }).LineCount);
        Assert.Null(TextLayoutOptions.Default.LineBreakDictionary);
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
    public void GetOriginPutsAPointOfTheTextAtAnAnchor()
    {
        // "A" is 16 pixels wide and tall, its baseline is at 13, and its ink goes from (2, 1) to (14, 13)
        var layout = TextLayout.Create("A", Boxes, 16);
        var anchor = new PointD(100, 50);
        Assert.Equal(new PointD(100, 50), layout.GetOrigin(anchor, TextHorizontalAnchor.Left, TextVerticalAnchor.Top));
        Assert.Equal(new PointD(92, 42), layout.GetOrigin(anchor, TextHorizontalAnchor.Center, TextVerticalAnchor.Center));
        Assert.Equal(new PointD(84, 34), layout.GetOrigin(anchor, TextHorizontalAnchor.Right, TextVerticalAnchor.Bottom));
        Assert.Equal(new PointD(84, 37), layout.GetOrigin(anchor, TextHorizontalAnchor.Right, TextVerticalAnchor.Baseline));

        // The ink box puts the visible edges at the anchor; the baseline is the same
        Assert.Equal(new PointD(98, 49), layout.GetOrigin(anchor, TextHorizontalAnchor.Left, TextVerticalAnchor.Top, TextBounds.Ink));
        Assert.Equal(new PointD(92, 43), layout.GetOrigin(anchor, TextHorizontalAnchor.Center, TextVerticalAnchor.Center, TextBounds.Ink));
        Assert.Equal(new PointD(86, 37), layout.GetOrigin(anchor, TextHorizontalAnchor.Right, TextVerticalAnchor.Bottom, TextBounds.Ink));
        Assert.Equal(new PointD(86, 37), layout.GetOrigin(anchor, TextHorizontalAnchor.Right, TextVerticalAnchor.Baseline, TextBounds.Ink));

        // In a rectangle, the point of the text goes on the same point of the rectangle; a larger text overflows it
        var box = new RectangleD(10, 20, 100, 40);
        Assert.Equal(new PointD(52, 32), layout.GetOrigin(box, TextHorizontalAnchor.Center, TextVerticalAnchor.Center));
        Assert.Equal(new PointD(10, 44), layout.GetOrigin(box, TextHorizontalAnchor.Left, TextVerticalAnchor.Bottom));
        Assert.Equal(new PointD(94, 20), layout.GetOrigin(box, TextHorizontalAnchor.Right, TextVerticalAnchor.Top));
        Assert.Equal(new PointD(96, 19), layout.GetOrigin(box, TextHorizontalAnchor.Right, TextVerticalAnchor.Top, TextBounds.Ink));
        Assert.Equal(new PointD(10, 32), TextLayout.Create("AAAAAAAAAAAAAAAAAAAA", Boxes, 16).GetOrigin(new RectangleD(20, 20, 300, 40), TextHorizontalAnchor.Center, TextVerticalAnchor.Center));

        // The ink of "A" drawn in the middle of a rectangle is in its middle
        Assert.Equal(
            Render(120, 80, canvas => canvas.FillRectangle(White, new RectangleD(54, 34, 12, 12))),
            Render(120, 80, canvas => canvas.FillText(White, layout, layout.GetOrigin(box, TextHorizontalAnchor.Center, TextVerticalAnchor.Center, TextBounds.Ink))));

        // A layout that draws nothing is placed by its layout box
        var space = TextLayout.Create(" ", Boxes, 16);
        Assert.Equal(space.GetOrigin(anchor, TextHorizontalAnchor.Center, TextVerticalAnchor.Center), space.GetOrigin(anchor, TextHorizontalAnchor.Center, TextVerticalAnchor.Center, TextBounds.Ink));

        // A rectangle has no baseline, and neither has a vertical layout along y
        Assert.Throws<ArgumentException>("vertical", () => layout.GetOrigin(box, TextHorizontalAnchor.Left, TextVerticalAnchor.Baseline));
        var vertical = TextLayout.Create("\u4E00\u4E8C", new TextStyle(Ideographs, 16), Vertical());
        Assert.Throws<ArgumentException>("vertical", () => vertical.GetOrigin(anchor, TextHorizontalAnchor.Left, TextVerticalAnchor.Baseline));
        Assert.Equal(new PointD(92, 34), vertical.GetOrigin(anchor, TextHorizontalAnchor.Center, TextVerticalAnchor.Center));

        Assert.Throws<ArgumentOutOfRangeException>("horizontal", () => layout.GetOrigin(anchor, (TextHorizontalAnchor)3, TextVerticalAnchor.Top));
        Assert.Throws<ArgumentOutOfRangeException>("vertical", () => layout.GetOrigin(anchor, TextHorizontalAnchor.Left, (TextVerticalAnchor)4));
        Assert.Throws<ArgumentOutOfRangeException>("bounds", () => layout.GetOrigin(box, TextHorizontalAnchor.Left, TextVerticalAnchor.Top, (TextBounds)2));
        Assert.Throws<ArgumentException>("anchor", () => layout.GetOrigin(new PointD(double.NaN, 0), TextHorizontalAnchor.Left, TextVerticalAnchor.Top));
        Assert.Throws<ArgumentException>("box", () => layout.GetOrigin(new RectangleD(double.PositiveInfinity, 0, 1, 1), TextHorizontalAnchor.Left, TextVerticalAnchor.Top));
    }

    [Fact]
    public void GetInkBoundsContainsTheStrokeOfThePen()
    {
        // The ink of "A" goes from (2, 1) to (14, 13): a stroke reaches half its width further with round joins, and
        // as far as the limit of its miters (4 by default) with miters
        var layout = TextLayout.Create("A", Boxes, 16);
        Assert.Equal(RectangleD.FromLTRB(1, 0, 15, 14), layout.GetInkBounds(new Pen(White, 2) { LineJoin = LineJoin.Round }));
        Assert.Equal(RectangleD.FromLTRB(-2, -3, 18, 17), layout.GetInkBounds(new Pen(White, 2)));
        Assert.Equal(layout.InkBounds, layout.GetInkBounds(new Pen(White, 2) { Alignment = PenAlignment.Inside }));
        Assert.Equal(RectangleD.FromLTRB(0, -1, 16, 15), layout.GetInkBounds(new Pen(White, 2) { Alignment = PenAlignment.Outside, LineJoin = LineJoin.Round }));

        // Markers are drawn at the ends of open contours, which glyphs do not have: the path is stroked to know it
        var arrow = new Pen(White, 2) { LineJoin = LineJoin.Round, EndMarker = LineMarker.Arrow };
        Assert.Equal(RectangleD.FromLTRB(1, 0, 15, 14), layout.GetInkBounds(arrow));

        Assert.Equal(RectangleD.Empty, TextLayout.Create(" ", Boxes, 16).GetInkBounds(new Pen(White, 2)));
        Assert.Throws<ArgumentNullException>("pen", () => layout.GetInkBounds(null!));

        // What the pen paints is inside: nothing is painted around the bounds
        var pen = new Pen(White, 3);
        var origin = new PointD(10, 10);
        var bounds = layout.GetInkBounds(pen).Offset(origin.X, origin.Y);
        Assert.Equal(
            Render(40, 40, canvas => canvas.Clip(bounds, clipped => clipped.StrokeText(pen, layout, origin))),
            Render(40, 40, canvas => canvas.StrokeText(pen, layout, origin)));
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
    public void TheLanguageOfAStyleSelectsTheLanguageSystemOfTheFont()
    {
        // The font has a language system for Serbian, whose feature replaces A by four A; its default language system
        // has no feature.
        var font = FontTestSupport.Load(FontSamples.Languages(["SRB "]));
        Assert.Equal(16, TextLayout.Create("A", new TextStyle(font, 16)).Width);
        Assert.Equal(64, TextLayout.Create("A", new TextStyle(font, 16) { Language = "sr" }).Width);
        Assert.Equal(64, TextLayout.Create("A", new TextStyle(font, 16) { Language = "SR-Cyrl-RS" }).Width);
        Assert.Equal("sr-Latn", new TextStyle(font, 16) { Language = "sr-Latn" }.Language);

        // A language the font has no language system for, or the registry of OpenType no tag for, changes nothing.
        Assert.Equal(16, TextLayout.Create("A", new TextStyle(font, 16) { Language = "fr" }).Width);
        Assert.Equal(16, TextLayout.Create("A", new TextStyle(font, 16) { Language = "und" }).Width);

        // Each span is shaped in its own language.
        var text = new RichTextBuilder()
            .Append("A", new TextStyle(font, 16) { Language = "sr" })
            .Append("A", new TextStyle(font, 16))
            .Append("A", new TextStyle(font, 16) { Language = "srp" })
            .Build();
        Assert.Equal(64 + 16 + 64, TextLayout.Create(text).Width);
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
    public void AnEmojiIsDrawnWithAFontOfItsPresentation()
    {
        // Two fonts that have the same characters: a font without colors, whose glyphs are 8 pixels wide, and a font
        // with color glyphs, 16 pixels wide. U+2764 (heavy black heart), U+261D (index pointing up), the copyright sign
        // and the digits are emoji characters that are text by default; U+1F600 (grinning face) is an emoji by default.
        int[] characters = [0x2764, 0x261D, 0x00A9, '1', 0x1F600, 0x1F3FD, 0x1F468, 0x1F469];
        var textBuilder = new FontBuilder { CmapFormats = FontCmapFormats.Format12, UnitsPerEm = 1024, Ascender = 832, Descender = -192, FamilyName = "Text" };
        textBuilder.AddGlyph(FontGlyph.Rectangle(256, 0, 0, 128, 128));
        textBuilder.AddGlyph(FontGlyph.Rectangle(512, 0, 0, 512, 512), 'A');
        var colorBuilder = FontSamples.ColorBoxes();
        colorBuilder.CmapFormats = FontCmapFormats.Format12;
        foreach (var character in characters)
        {
            textBuilder.CharacterMap[character] = 1;
            colorBuilder.CharacterMap[character] = FontSamples.ColorA;
        }

        var text = FontTestSupport.Load(textBuilder);
        var color = FontTestSupport.Load(colorBuilder);
        Assert.False(text.HasColorGlyphs);
        Assert.True(color.HasColorGlyphs);
        var textFirst = new TextStyle(new FontStack(text, color), 16);
        var colorFirst = new TextStyle(new FontStack(color, text), 16);

        // Without a selector, a character that is text by default is drawn with the first font that has it.
        Assert.Equal(8, TextLayout.Create("\u2764", textFirst).Width);
        Assert.Equal(16, TextLayout.Create("\u2764", colorFirst).Width);
        Assert.Equal(8, TextLayout.Create("\u00A9", textFirst).Width);
        Assert.Equal(8, TextLayout.Create("1", textFirst).Width);

        // The emoji presentation selector asks for a font with colors, the text presentation selector for one without.
        Assert.Equal(16, TextLayout.Create("\u2764\uFE0F", textFirst).Width);
        Assert.Equal(16, TextLayout.Create("\u00A9\uFE0F", textFirst).Width);
        Assert.Equal(8, TextLayout.Create("\u2764\uFE0E", colorFirst).Width);
        Assert.Equal(8, TextLayout.Create("\U0001F600\uFE0E", colorFirst).Width);

        // A character that is an emoji by default, and the emoji sequences (a skin tone, characters joined by a zero
        // width joiner), are drawn with a font that has colors. These fonts have no ligature: each character of a
        // sequence has its glyph.
        Assert.Equal(16, TextLayout.Create("\U0001F600", textFirst).Width);
        Assert.Equal(32, TextLayout.Create("\u261D\U0001F3FD", textFirst).Width);
        Assert.Equal(8, TextLayout.Create("\u261D", textFirst).Width);
        Assert.Equal(32, TextLayout.Create("\U0001F468\u200D\U0001F469", textFirst).Width);

        // Each cluster has its font: a letter, an emoji, a letter.
        Assert.Equal(8 + 16 + 8, TextLayout.Create("A\U0001F600A", textFirst).Width);

        // The presentation is a preference, not a requirement: without a font of that kind that has the character, the
        // stack is searched in its order.
        Assert.Equal(8, TextLayout.Create("\U0001F600", new TextStyle(new FontStack(text, Boxes), 16)).Width);
        Assert.Equal(16, TextLayout.Create("\u2764\uFE0E", new TextStyle(new FontStack(color, Boxes), 16)).Width);

        // A selector after a character that is no emoji says nothing.
        Assert.Equal(8, TextLayout.Create("A\uFE0F", textFirst).Width);
    }

    [Fact]
    public void ACharacterNoFontOfTheStackHasIsLookedForAmongTheInstalledFonts()
    {
        // An emoji, a Han ideograph and a symbol that the boxes font does not have. Each installed font draws its
        // characters with one glyph whose advance says which font was chosen.
        const int Emoji = 0x1F600;
        const int Han = 0x4E00;
        const int Star = 0x2605;
        using var directory = new TemporaryFontDirectory();
        directory.Write("everything.ttf", CreateCoveringFont("AAA Everything", advance: 320, characters: [Emoji, Han]));
        directory.Write("emoji.ttf", CreateCoveringFont("Noto Color Emoji", advance: 1280, characters: [Emoji]));
        directory.Write("cjk.ttf", CreateCoveringFont("Noto Sans CJK SC", advance: 768, characters: [Han]));
        directory.Write("light.ttf", CreateCoveringFont("Aaa Symbols", advance: 448, characters: [Star], weight: 300));
        var removedPath = directory.Write("removed.ttf", CreateCoveringFont("Abc Symbols", advance: 128, characters: [Star]));
        directory.Write("symbols.ttf", CreateCoveringFont("Bbb Symbols", advance: 640, characters: [Star]));
        directory.Write("symbols-bold.ttf", CreateCoveringFont("Bbb Symbols", advance: 896, characters: [Star], weight: 700));

        // A file whose character map is malformed (its number of subtables goes past the end of the table), and a file
        // removed after the scan, are skipped.
        directory.Write("broken.ttf", FontTestSupport.PatchTable(CreateCoveringFont("AAA Broken", advance: 64, characters: [Star]), "cmap", table => BinaryPrimitives.WriteUInt16BigEndian(table[2..], 0xFFFF)));
        var fonts = SystemFontCollection.Create([directory.Path]);
        File.Delete(removedPath);

        var stack = new FontStack(Boxes).WithInstalledFallback(fonts);
        Assert.True(stack.HasInstalledFallback);
        Assert.False(new FontStack(Boxes).HasInstalledFallback);
        Assert.Equal(new[] { Boxes }, stack.Fonts);
        var style = new TextStyle(stack, 16);

        // The ideograph is drawn with the font the platforms use for the script; the characters of the fonts are read
        // when they are needed, not when the fonts are scanned, and only from the fonts that are looked in.
        Assert.Equal(0, fonts.CoverageCache.ReadCount);
        var layout = TextLayout.Create("A\u4E00\u4E00", style);
        Assert.False(layout.HasMissingGlyphs);
        Assert.Equal(16 + 12 + 12, layout.Width);
        Assert.Equal(1, fonts.CoverageCache.ReadCount);

        // The emoji is drawn with the color emoji font of the platforms, the emoji in its text presentation with the
        // first family that has it.
        Assert.Equal(20, TextLayout.Create("\U0001F600", style).Width);
        Assert.Equal(5, TextLayout.Create("\U0001F600\uFE0E", style).Width);

        // The symbol is in no family of the platforms: the families whose face has the style of the primary font come
        // first, by name.
        layout = TextLayout.Create("\u2605A", style);
        Assert.False(layout.HasMissingGlyphs);
        Assert.Equal(10 + 16, layout.Width);
        var boldBoxes = FontSamples.Boxes();
        boldBoxes.WeightClass = 700;
        boldBoxes.IsBold = true;
        Assert.Equal(14, TextLayout.Create("\u2605", new TextStyle(new FontStack(FontTestSupport.Load(boldBoxes)).WithInstalledFallback(fonts), 16)).Width);
        var lightBoxes = FontSamples.Boxes();
        lightBoxes.WeightClass = 300;
        Assert.Equal(7, TextLayout.Create("\u2605", new TextStyle(new FontStack(FontTestSupport.Load(lightBoxes)).WithInstalledFallback(fonts), 16)).Width);
        Assert.False(fonts.Fonts.Single(font => font.FamilyName == "AAA Broken").Covers(new Rune(Star)));

        // The preferred families come first.
        var preferred = new FontStack(Boxes).WithInstalledFallback(fonts, new InstalledFontFallbackOptions { PreferredFamilies = ["AAA Everything"] });
        Assert.Equal(5, TextLayout.Create("\u4E00", new TextStyle(preferred, 16)).Width);

        // A character no installed font has is the missing glyph of the primary font.
        layout = TextLayout.Create("\U00010400", style);
        Assert.True(layout.HasMissingGlyphs);
        Assert.Equal(8, layout.Width);

        // Without fallback, the text is drawn with the fonts of the stack only.
        layout = TextLayout.Create("\u2605\u4E00", Style);
        Assert.True(layout.HasMissingGlyphs);
        Assert.Equal(16, layout.Width);
        Assert.Throws<ArgumentNullException>("value", () => new InstalledFontFallbackOptions { LoadOptions = null! });
        Assert.Throws<ArgumentNullException>("value", () => new InstalledFontFallbackOptions { PreferredFamilies = null! });

        static byte[] CreateCoveringFont(string family, int advance, int[] characters, int weight = 400)
        {
            var builder = new FontBuilder { CmapFormats = FontCmapFormats.Format12, UnitsPerEm = 1024, Ascender = 832, Descender = -192, FamilyName = family, PostScriptName = null, WeightClass = weight, IsBold = weight >= 700 };
            builder.SubfamilyName = weight >= 700 ? "Bold" : "Regular";
            builder.AddGlyph(FontGlyph.Rectangle(256, 0, 0, 128, 128));
            builder.AddGlyph(FontGlyph.Rectangle(advance, 0, 0, advance, 512));
            foreach (var character in characters)
            {
                builder.CharacterMap[character] = 1;
            }

            return builder.Build();
        }
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
        Assert.Throws<ArgumentException>(() => new TextStyle(Boxes, 16) { Language = "" });
        Assert.Throws<ArgumentException>(() => new TextStyle(Boxes, 16) { Language = "en_US" });
        Assert.Throws<ArgumentException>(() => new TextStyle(Boxes, 16) { Language = "en-" });
        var style = new TextStyle(Boxes, 16);
        Assert.Null(style.Language);
        Assert.Null(new TextStyle(Boxes, 16) { Language = null }.Language);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { WritingMode = (TextWritingMode)3 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { WritingMode = (TextWritingMode)(-1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { Orientation = (TextOrientation)3 });
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

    [Fact]
    public void APointOfALayoutIsOnACharacterOfTheText()
    {
        // At 16 pixels, A is 16 pixels wide, B and the space 8, C 12. "AB C" then, on a second line, "A":
        // A from 0 to 16, B to 24, the space to 32, C to 44.
        var style = new TextStyle(Extended, 16);
        var layout = TextLayout.Create("AB C\nA", style);
        Assert.Equal(new TextHit(0, 0, 1, isTrailing: false, isInside: true), layout.HitTest(new PointD(1, 1)));
        Assert.Equal(new TextHit(0, 0, 1, isTrailing: true, isInside: true), layout.HitTest(new PointD(9, 5)));
        Assert.Equal(new TextHit(0, 1, 1, isTrailing: false, isInside: true), layout.HitTest(new PointD(17, 3)));
        Assert.Equal(new TextHit(0, 1, 1, isTrailing: true, isInside: true), layout.HitTest(new PointD(22, 15.9)));
        Assert.Equal(new TextHit(0, 2, 1, isTrailing: false, isInside: true), layout.HitTest(new PointD(27, 3)));
        Assert.Equal(new TextHit(0, 3, 1, isTrailing: true, isInside: true), layout.HitTest(new PointD(43, 3)));
        Assert.Equal(new TextHit(1, 5, 1, isTrailing: false, isInside: true), layout.HitTest(new PointD(5, 20)));

        // The middle of a character is its second half: a click there puts the caret after it
        Assert.False(layout.HitTest(new PointD(7.9, 3)).IsTrailing);
        Assert.True(layout.HitTest(new PointD(8, 3)).IsTrailing);
        Assert.Equal(0, layout.HitTest(new PointD(7.9, 3)).CaretIndex);
        Assert.Equal(1, layout.HitTest(new PointD(8, 3)).CaretIndex);
        Assert.Equal(4, layout.HitTest(new PointD(43, 3)).CaretIndex);

        // Beside, above or below the text: the nearest character, and the point is not on it
        Assert.Equal(new TextHit(0, 3, 1, isTrailing: true, isInside: false), layout.HitTest(new PointD(100, 3)));
        Assert.Equal(new TextHit(0, 0, 1, isTrailing: false, isInside: false), layout.HitTest(new PointD(-5, 3)));
        Assert.Equal(new TextHit(0, 1, 1, isTrailing: false, isInside: false), layout.HitTest(new PointD(17, -10)));
        Assert.Equal(new TextHit(1, 5, 1, isTrailing: true, isInside: false), layout.HitTest(new PointD(50, 20)));
        Assert.Equal(new TextHit(1, 5, 1, isTrailing: false, isInside: false), layout.HitTest(new PointD(5, 100)));
        Assert.Equal(6, layout.HitTest(new PointD(50, 100)).CaretIndex);
        Assert.Equal(new TextHit(1, 5, 1, isTrailing: false, isInside: false), layout.HitTest(new PointD(double.NegativeInfinity, double.PositiveInfinity)));
        Assert.Throws<ArgumentException>(() => layout.HitTest(new PointD(double.NaN, 0)));
        Assert.Throws<ArgumentException>(() => layout.HitTest(new PointD(0, double.NaN)));
        Assert.Equal("line 0, character 3+1, trailing, outside", layout.HitTest(new PointD(100, 3)).ToString());
        Assert.NotEqual(layout.HitTest(new PointD(1, 1)), layout.HitTest(new PointD(9, 5)));
        Assert.Equal(layout.HitTest(new PointD(1, 1)).GetHashCode(), layout.HitTest(new PointD(2, 2)).GetHashCode());

        // A letter and its mark are one character: the font draws them as one glyph, and a caret cannot go between them
        var accented = TextLayout.Create("A\u0301B", style);
        Assert.Equal(new TextHit(0, 0, 2, isTrailing: true, isInside: true), accented.HitTest(new PointD(12, 3)));
        Assert.Equal(2, accented.HitTest(new PointD(12, 3)).CaretIndex);
        Assert.Equal(new TextHit(0, 2, 1, isTrailing: false, isInside: true), accented.HitTest(new PointD(17, 3)));
        Assert.Equal(new RectangleD(0, 0, 0, 16), accented.GetCaretRectangle(1));
        Assert.Equal(new RectangleD(16, 0, 0, 16), accented.GetCaretRectangle(2));

        // The space that ends a wrapped line hangs beyond its width, and is still a character of that line; an
        // aligned line has its characters where they are drawn
        var wrapped = TextLayout.Create("AA AA", style, new TextLayoutOptions { MaxWidth = 40 });
        Assert.Equal(new TextHit(0, 2, 1, isTrailing: false, isInside: true), wrapped.HitTest(new PointD(35, 3)));
        Assert.Equal(new TextHit(1, 4, 1, isTrailing: true, isInside: true), wrapped.HitTest(new PointD(30, 17)));
        var centered = TextLayout.Create("AB", style, new TextLayoutOptions { MaxWidth = 100, Alignment = TextAlignment.Center });
        Assert.Equal(new TextHit(0, 1, 1, isTrailing: false, isInside: true), centered.HitTest(new PointD(55, 3)));
        Assert.Equal(new TextHit(0, 0, 1, isTrailing: false, isInside: false), centered.HitTest(new PointD(10, 3)));
        Assert.Equal(new RectangleD(54, 0, 0, 16), centered.GetCaretRectangle(1));

        // A line without character, and a text without character
        var blank = TextLayout.Create("A\n\nA", style);
        Assert.Equal(new TextHit(1, 2, 0, isTrailing: false, isInside: false), blank.HitTest(new PointD(5, 20)));
        Assert.Equal(2, blank.HitTest(new PointD(5, 20)).CaretIndex);
        Assert.Equal(new RectangleD(0, 16, 0, 16), blank.GetCaretRectangle(2));
        var empty = TextLayout.Create(string.Empty, style);
        Assert.Equal(new TextHit(0, 0, 0, isTrailing: false, isInside: false), empty.HitTest(new PointD(3, 3)));
        Assert.Equal(new RectangleD(0, 0, 0, 16), empty.GetCaretRectangle(0));
        Assert.Empty(empty.GetSelectionRectangles(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.GetCaretRectangle(1));
    }

    [Fact]
    public void ACaretAndASelectionAreWhereTheirCharactersAre()
    {
        var style = new TextStyle(Extended, 16);
        var layout = TextLayout.Create("AB C\nA", style);

        // Before each character; at a line break, after the last character of its line; then the second line
        Assert.Equal(
            [new RectangleD(0, 0, 0, 16), new RectangleD(16, 0, 0, 16), new RectangleD(24, 0, 0, 16), new RectangleD(32, 0, 0, 16), new RectangleD(44, 0, 0, 16), new RectangleD(0, 16, 0, 16), new RectangleD(16, 16, 0, 16)],
            Enumerable.Range(0, 7).Select(layout.GetCaretRectangle));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetCaretRectangle(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetCaretRectangle(7));

        // A caret is where a click puts it
        foreach (var x in new[] { -3.0, 1, 9, 17, 23, 30, 43, 60 })
        {
            var hit = layout.HitTest(new PointD(x, 3));
            var caret = layout.GetCaretRectangle(hit.CaretIndex);
            Assert.InRange(caret.X, Math.Clamp(x, 0, 44) - 8, Math.Clamp(x, 0, 44) + 8);
        }

        // The characters of a range that follow each other on a line are one rectangle, as tall as the line
        Assert.Equal([new RectangleD(16, 0, 28, 16)], layout.GetSelectionRectangles(1, 3));
        Assert.Equal([new RectangleD(32, 0, 12, 16), new RectangleD(0, 16, 16, 16)], layout.GetSelectionRectangles(3, 3));
        Assert.Equal([new RectangleD(0, 0, 44, 16), new RectangleD(0, 16, 16, 16)], layout.GetSelectionRectangles(0, 6));
        Assert.Equal([new RectangleD(0, 16, 16, 16)], layout.GetSelectionRectangles(4, 2));
        Assert.Empty(layout.GetSelectionRectangles(4, 1));
        Assert.Empty(layout.GetSelectionRectangles(2, 0));
        Assert.Empty(layout.GetSelectionRectangles(6, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetSelectionRectangles(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetSelectionRectangles(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetSelectionRectangles(5, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetSelectionRectangles(7, 0));

        // A wrapped paragraph: the position between two lines is the start of the second one, and the space that
        // hangs at the end of a line is selected with it
        var wrapped = TextLayout.Create("AA AA", style, new TextLayoutOptions { MaxWidth = 40 });
        Assert.Equal(new RectangleD(32, 0, 0, 16), wrapped.GetCaretRectangle(2));
        Assert.Equal(new RectangleD(0, 16, 0, 16), wrapped.GetCaretRectangle(3));
        Assert.Equal([new RectangleD(16, 0, 24, 16), new RectangleD(0, 16, 16, 16)], wrapped.GetSelectionRectangles(1, 3));

        // Justified, the characters are where the wider spaces put them: the first line holds "A A", whose space is
        // stretched from 8 to 28 pixels to fill the 60, and the space that hangs after it
        var justified = TextLayout.Create("A A A A", style, new TextLayoutOptions { MaxWidth = 60, Alignment = TextAlignment.Justify });
        Assert.Equal(new TextLine(0, 4, 0, 0, 60, 16, 13, false), justified.Lines[0]);
        Assert.Equal(new RectangleD(44, 0, 0, 16), justified.GetCaretRectangle(2));
        Assert.Equal([new RectangleD(16, 0, 52, 16)], justified.GetSelectionRectangles(1, 3));
        Assert.Equal(new TextHit(0, 1, 1, isTrailing: false, isInside: true), justified.HitTest(new PointD(28, 3)));
        Assert.Equal(new TextHit(0, 1, 1, isTrailing: true, isInside: true), justified.HitTest(new PointD(31, 3)));
    }

    [Fact]
    public void TheCharactersOfRightToLeftTextStartOnTheirRight()
    {
        // Alef then bet, drawn as C (12 pixels) then A (16): bet is from 0 to 16 and alef from 16 to 28
        var style = new TextStyle(Extended, 16);
        var layout = TextLayout.Create("\u05D0\u05D1", style);
        Assert.Equal(new TextHit(0, 0, 1, isTrailing: false, isInside: true), layout.HitTest(new PointD(27, 3)));
        Assert.Equal(new TextHit(0, 0, 1, isTrailing: true, isInside: true), layout.HitTest(new PointD(17, 3)));
        Assert.Equal(new TextHit(0, 1, 1, isTrailing: false, isInside: true), layout.HitTest(new PointD(15, 3)));
        Assert.Equal(new TextHit(0, 1, 1, isTrailing: true, isInside: true), layout.HitTest(new PointD(1, 3)));
        Assert.Equal(new TextHit(0, 1, 1, isTrailing: true, isInside: false), layout.HitTest(new PointD(-4, 3)));
        Assert.Equal(new TextHit(0, 0, 1, isTrailing: false, isInside: false), layout.HitTest(new PointD(40, 3)));

        // The caret goes from the right to the left as it goes through the text
        Assert.Equal([28.0, 16, 0], Enumerable.Range(0, 3).Select(index => layout.GetCaretRectangle(index).X));
        Assert.Equal([new RectangleD(16, 0, 12, 16)], layout.GetSelectionRectangles(0, 1));
        Assert.Equal([new RectangleD(0, 0, 28, 16)], layout.GetSelectionRectangles(0, 2));

        // A line of a right-to-left paragraph that has no character starts on its right
        var aligned = TextLayout.Create("\u05D0\n", style, new TextLayoutOptions { MaxWidth = 40 });
        Assert.Equal(new RectangleD(40, 0, 0, 16), aligned.GetCaretRectangle(0));
        Assert.Equal(new RectangleD(28, 0, 0, 16), aligned.GetCaretRectangle(1));

        // Hebrew in a left-to-right paragraph: B from 0 to 8, bet to 24, alef to 36, B to 44. The text is B, alef,
        // bet, B: what follows in the text does not follow on the line, and a range can be two rectangles
        var mixed = TextLayout.Create("B\u05D0\u05D1B", style);
        Assert.Equal([0.0, 36, 24, 36, 44], Enumerable.Range(0, 5).Select(index => mixed.GetCaretRectangle(index).X));
        Assert.Equal([new RectangleD(0, 0, 8, 16), new RectangleD(24, 0, 12, 16)], mixed.GetSelectionRectangles(0, 2));
        Assert.Equal([new RectangleD(8, 0, 28, 16)], mixed.GetSelectionRectangles(1, 2));
        Assert.Equal([new RectangleD(8, 0, 16, 16), new RectangleD(36, 0, 8, 16)], mixed.GetSelectionRectangles(2, 2));
        Assert.Equal(new TextHit(0, 2, 1, isTrailing: true, isInside: true), mixed.HitTest(new PointD(9, 3)));
        Assert.Equal(new TextHit(0, 1, 1, isTrailing: false, isInside: true), mixed.HitTest(new PointD(35, 3)));
        Assert.Equal(new TextHit(0, 3, 1, isTrailing: false, isInside: true), mixed.HitTest(new PointD(37, 3)));

        // Lam and alef are drawn as one glyph, 8 pixels wide: each letter has half of it, lam on the right
        var ligature = TextLayout.Create("\u0644\u0627", new TextStyle(FontTestSupport.Load(FontSamples.ArabicForms()), 16));
        Assert.Equal(8, ligature.Width);
        Assert.Equal(new TextHit(0, 0, 1, isTrailing: false, isInside: true), ligature.HitTest(new PointD(7, 3)));
        Assert.Equal(new TextHit(0, 0, 1, isTrailing: true, isInside: true), ligature.HitTest(new PointD(5, 3)));
        Assert.Equal(new TextHit(0, 1, 1, isTrailing: false, isInside: true), ligature.HitTest(new PointD(3, 3)));
        Assert.Equal(new TextHit(0, 1, 1, isTrailing: true, isInside: true), ligature.HitTest(new PointD(1, 3)));
        Assert.Equal([8.0, 4, 0], Enumerable.Range(0, 3).Select(index => ligature.GetCaretRectangle(index).X));
        Assert.Equal([new RectangleD(4, 0, 4, 16)], ligature.GetSelectionRectangles(0, 1));
        Assert.Equal([new RectangleD(0, 0, 4, 16)], ligature.GetSelectionRectangles(1, 1));
    }

    [Fact]
    public void AVerticalLineSetsUprightCharactersOneUnderTheOther()
    {
        // The line is 16 pixels wide, the ascent and the descent of the font, and its baseline is in its middle.
        // Each ideograph advances by 16 pixels and is centered in them: there is no kerning between upright glyphs.
        var style = new TextStyle(Ideographs, 16);
        var layout = TextLayout.Create("\u4E00\u4E8C\u4E09", style, Vertical());
        Assert.Equal(16, layout.Width);
        Assert.Equal(48, layout.Height);
        Assert.Equal(8, layout.Baseline);
        Assert.Equal(new[] { new TextLine(0, 3, 0, 0, 16, 48, 8, false) }, layout.Lines);
        Assert.Equal(RectangleD.FromLTRB(2, 2, 14, 48), layout.InkBounds);
        AssertRectangles(layout, new PointD(3, 1), UprightA(0, 0), UprightB(0, 16), UprightC(0, 32));

        // The ideographic comma is upright too, and a line of one column is the same whatever side lines go to
        layout = TextLayout.Create("\u4E00\u3001", style, Vertical(mode: TextWritingMode.VerticalLeftToRight));
        Assert.Equal(new[] { new TextLine(0, 2, 0, 0, 16, 32, 8, false) }, layout.Lines);
        AssertRectangles(layout, new PointD(0, 0), UprightA(0, 0), (new RectangleD(5, 23, 6, 2), null));

        // The path of the text is what is drawn
        var path = TextLayout.Create("\u4E00\u4E8C", style, Vertical()).ToPath(new PointD(3, 1));
        Assert.Equal(
            Render(120, 70, canvas =>
            {
                canvas.FillRectangle(White, new RectangleD(5, 3, 12, 12));
                canvas.FillRectangle(White, new RectangleD(8, 21, 6, 8));
            }),
            Render(120, 70, canvas => canvas.Fill(White, path)));
    }

    [Fact]
    public void TheCharactersOfAVerticalLineAreUprightOrOnTheirSide()
    {
        // Latin letters are on their side: the line "AB" of a horizontal layout, with its kerning, turned a quarter
        // of a turn clockwise, its baseline 5 pixels on the left of the middle of the line
        var style = new TextStyle(Ideographs, 16);
        var layout = TextLayout.Create("AB", style, Vertical());
        Assert.Equal(16, layout.Width);
        Assert.Equal(22, layout.Height);
        Assert.Equal(RectangleD.FromLTRB(3, 2, 15, 21), layout.InkBounds);
        AssertRectangles(layout, new PointD(0, 0), SidewaysA(0, 0), SidewaysB(0, 14));

        // Both in one line: each one advances down the line
        layout = TextLayout.Create("\u4E00A\u4E8C", style, Vertical());
        Assert.Equal(48, layout.Height);
        AssertRectangles(layout, new PointD(2, 1), UprightA(0, 0), SidewaysA(0, 16), UprightB(0, 32));

        // A bracket is upright with the vertical form of its font: without one, it is on its side
        layout = TextLayout.Create("\u300C", style, Vertical());
        Assert.Equal(8, layout.Height);
        AssertRectangles(layout, new PointD(0, 0), SidewaysB(0, 0));

        // Every character upright, or every character on its side
        layout = TextLayout.Create("AB", style, Vertical(orientation: TextOrientation.Upright));
        Assert.Equal(32, layout.Height);
        AssertRectangles(layout, new PointD(0, 0), UprightA(0, 0), UprightB(0, 16));
        layout = TextLayout.Create("\u4E00\u4E8C", style, Vertical(orientation: TextOrientation.Sideways));
        Assert.Equal(22, layout.Height);
        AssertRectangles(layout, new PointD(0, 0), SidewaysA(0, 0), SidewaysB(0, 14));

        // Right-to-left text is on its side and reads from the bottom of the line; upright, it reads from the top
        layout = TextLayout.Create("\u05D0\u05D1", style, Vertical());
        AssertRectangles(layout, new PointD(0, 0), SidewaysA(0, 0), SidewaysC(0, 16));
        layout = TextLayout.Create("\u05D0\u05D1", style, Vertical(orientation: TextOrientation.Upright));
        AssertRectangles(layout, new PointD(0, 0), UprightC(0, 0), UprightA(0, 16));

        // The orientation is not used in a horizontal layout
        layout = TextLayout.Create("\u4E00\u4E8C", style, new TextLayoutOptions { Orientation = TextOrientation.Sideways });
        Assert.Equal(22, layout.Width);
        Assert.Equal(16, layout.Height);
        AssertRectangles(layout, new PointD(0, 0), A(0, 0), B(14, 0));
    }

    [Fact]
    public void VerticalLinesFollowEachOtherToTheLeftOrToTheRight()
    {
        // The first line is the rightmost one, and the baseline of the layout is the one of that line
        var style = new TextStyle(Ideographs, 16);
        var layout = TextLayout.Create("\u4E00\u4E8C\n\u4E09", style, Vertical());
        Assert.Equal(32, layout.Width);
        Assert.Equal(32, layout.Height);
        Assert.Equal(24, layout.Baseline);
        Assert.Equal(new[] { new TextLine(0, 3, 16, 0, 16, 32, 8, false), new TextLine(3, 1, 0, 0, 16, 16, 8, false) }, layout.Lines);
        AssertRectangles(layout, new PointD(1, 2), UprightA(16, 0), UprightB(16, 16), UprightC(0, 0));

        // Or the leftmost one
        layout = TextLayout.Create("\u4E00\u4E8C\n\u4E09", style, Vertical(mode: TextWritingMode.VerticalLeftToRight));
        Assert.Equal(32, layout.Width);
        Assert.Equal(8, layout.Baseline);
        Assert.Equal(new[] { new TextLine(0, 3, 0, 0, 16, 32, 8, false), new TextLine(3, 1, 16, 0, 16, 16, 8, false) }, layout.Lines);
        AssertRectangles(layout, new PointD(1, 2), UprightA(0, 0), UprightB(0, 16), UprightC(16, 0));

        // Lines are wrapped at the height of the layout: two ideographs and a space are 40 pixels. The space that ends
        // a wrapped line hangs.
        layout = TextLayout.Create("\u4E00\u4E00 \u4E00\u4E00 \u4E00\u4E00", style, Vertical(maxHeight: 40));
        Assert.Equal(3, layout.LineCount);
        Assert.Equal(48, layout.Width);
        Assert.Equal(32, layout.Height);
        Assert.Equal(new TextLine(0, 3, 32, 0, 16, 32, 8, false), layout.Lines[0]);
        Assert.Equal(new TextLine(6, 2, 0, 0, 16, 32, 8, false), layout.Lines[2]);
        AssertRectangles(layout, new PointD(0, 0), UprightA(32, 0), UprightA(32, 16), UprightA(16, 0), UprightA(16, 16), UprightA(0, 0), UprightA(0, 16));
        Assert.Equal(2, TextLayout.Create("\u4E00\u4E00 \u4E00\u4E00 \u4E00\u4E00", style, Vertical(maxHeight: 72)).LineCount);

        // And aligned in it, from their top
        layout = TextLayout.Create("\u4E00\u4E00\n\u4E00", style, Vertical(maxHeight: 40, alignment: TextAlignment.End));
        Assert.Equal(new[] { new TextLine(0, 3, 16, 8, 16, 32, 8, false), new TextLine(3, 1, 0, 24, 16, 16, 8, false) }, layout.Lines);
        AssertRectangles(layout, new PointD(0, 0), UprightA(16, 8), UprightA(16, 24), UprightA(0, 24));
        layout = TextLayout.Create("\u4E00", style, Vertical(maxHeight: 40, alignment: TextAlignment.Center));
        AssertRectangles(layout, new PointD(0, 0), UprightA(0, 12));

        // The width is the room the lines must fit: the text is cut after the last line that does
        layout = TextLayout.Create("\u4E00\n\u4E8C\n\u4E09", style, Vertical(maxWidth: 40));
        Assert.Equal(2, layout.LineCount);
        Assert.True(layout.IsTruncated);
        Assert.Equal(32, layout.Width);

        // A line that is not wrapped is trimmed at the height of the layout: the ellipsis is set as the text it ends
        var trimmed = new TextLayoutOptions { WritingMode = TextWritingMode.VerticalRightToLeft, MaxHeight = 40, Wrapping = TextWrapping.NoWrap, Trimming = TextTrimming.Character, Ellipsis = "\u3001" };
        layout = TextLayout.Create("\u4E00\u4E00\u4E00\u4E00", style, trimmed);
        Assert.True(layout.IsTruncated);
        Assert.Equal(32, layout.Height);
        AssertRectangles(layout, new PointD(0, 0), UprightA(0, 0), (new RectangleD(5, 23, 6, 2), null));

        // A text is scaled to fit a box: four ideographs are 16 by 64 pixels
        layout = TextLayout.CreateToFit("\u4E00\u4E00\u4E00\u4E00", style, 16, 32, Vertical());
        Assert.Equal(0.5, layout.Scale);
        Assert.Equal(8, layout.Width);
        Assert.Equal(32, layout.Height);
    }

    [Fact]
    public void AVerticalLineOfTextOnItsSideIsAHorizontalLineTurned()
    {
        // Everything a line has is turned with it: its glyphs, its decorations, its background, its alignment, the
        // order of right-to-left text. The lines of the horizontal layout go down and those of the vertical layout
        // to the left: the vertical layout is the horizontal one turned a quarter of a turn clockwise.
        var decorated = new TextStyle(Ideographs, 16) { Decorations = TextDecorations.Underline | TextDecorations.Overline | TextDecorations.Strikethrough, Brush = Red, BackgroundBrush = Blue };
        var plain = new TextStyle(Ideographs, 16);
        var large = new TextStyle(Ideographs, 32) { Outline = new Pen(Blue, 2) };
        var text = new RichTextBuilder().Append("AB BA ", plain).Append("AC", decorated).Append(" \u05D0\u05D1 B\nA", plain).Append("B", large).AppendPlaceholder(10, 6, 4, plain).Append(" \u4E00\u4E8C", plain).Build();
        foreach (var (length, alignment, indent, lineHeight, direction) in ((double, TextAlignment, double, double?, TextDirection)[])
            [
                (double.PositiveInfinity, TextAlignment.Start, 0, null, TextDirection.Auto),
                (60, TextAlignment.Start, 0, null, TextDirection.Auto),
                (80, TextAlignment.End, 0, null, TextDirection.Auto),
                (80, TextAlignment.Center, 8, null, TextDirection.Auto),
                (80, TextAlignment.Justify, 0, 40, TextDirection.Auto),
                (60, TextAlignment.Start, 0, null, TextDirection.RightToLeft),
            ])
        {
            var horizontal = TextLayout.Create(text, new TextLayoutOptions { MaxWidth = length, Alignment = alignment, FirstLineIndent = indent, LineHeight = lineHeight, Direction = direction });
            var vertical = TextLayout.Create(text, new TextLayoutOptions
            {
                WritingMode = TextWritingMode.VerticalRightToLeft,
                Orientation = TextOrientation.Sideways,
                MaxHeight = length,
                Alignment = alignment,
                FirstLineIndent = indent,
                LineHeight = lineHeight,
                Direction = direction,
            });
            Assert.Equal(horizontal.Height, vertical.Width);
            Assert.Equal(horizontal.Width, vertical.Height);
            Assert.Equal(horizontal.LineCount, vertical.LineCount);
            Assert.Equal(RectangleD.FromLTRB(horizontal.Height - horizontal.InkBounds.Bottom, horizontal.InkBounds.Left, horizontal.Height - horizontal.InkBounds.Top, horizontal.InkBounds.Right), vertical.InkBounds);
            for (var i = 0; i < horizontal.LineCount; i++)
            {
                var line = horizontal.Lines[i];
                Assert.Equal(new TextLine(line.Start, line.Length, horizontal.Height - line.Y - line.Height, line.X, line.Height, line.Width, vertical.Lines[i].Baseline, line.IsRightToLeft), vertical.Lines[i]);
            }

            Assert.Equal(
                horizontal.Placeholders.Select(rectangle => new RectangleD(horizontal.Height - rectangle.Bottom, rectangle.X, rectangle.Height, rectangle.Width)),
                vertical.Placeholders);

            const int Width = 200;
            const int Height = 180;
            var expected = TurnClockwise(Render(Width, Height, canvas => canvas.FillText(White, horizontal, new PointD(4, 6))), Width, Height);
            Assert.Contains(expected, sample => sample != 0);
            Assert.Equal(expected, Render(Height, Width, canvas => canvas.FillText(White, vertical, new PointD(Height - 6 - vertical.Width, 4))));

            // The positions of the text are turned too
            for (var index = 0; index <= text.Text.Length; index++)
            {
                var caret = horizontal.GetCaretRectangle(index);
                Assert.Equal(new RectangleD(horizontal.Height - caret.Bottom, caret.X, caret.Height, 0), vertical.GetCaretRectangle(index));
            }

            Assert.Equal(
                horizontal.GetSelectionRectangles(2, text.Text.Length - 4).Select(rectangle => new RectangleD(horizontal.Height - rectangle.Bottom, rectangle.X, rectangle.Height, rectangle.Width)),
                vertical.GetSelectionRectangles(2, text.Text.Length - 4));
            for (var y = -3.5; y < horizontal.Height + 4; y += 5)
            {
                for (var x = -3.5; x < horizontal.Width + 4; x += 5)
                {
                    Assert.Equal(horizontal.HitTest(new PointD(x, y)), vertical.HitTest(new PointD(horizontal.Height - y, x)));
                }
            }
        }
    }

    [Fact]
    public void ThePositionsOfAVerticalTextAreDownItsLines()
    {
        var style = new TextStyle(Ideographs, 16);
        var layout = TextLayout.Create("\u4E00\u4E8C\n\u4E09", style, Vertical());

        // The first line is the column from 16 to 32, and the second one the column from 0 to 16
        Assert.Equal(new TextHit(0, 0, 1, isTrailing: false, isInside: true), layout.HitTest(new PointD(31, 0)));
        Assert.Equal(new TextHit(0, 1, 1, isTrailing: false, isInside: true), layout.HitTest(new PointD(24, 20)));
        Assert.Equal(new TextHit(0, 1, 1, isTrailing: true, isInside: true), layout.HitTest(new PointD(16, 28)));
        Assert.Equal(new TextHit(1, 3, 1, isTrailing: false, isInside: true), layout.HitTest(new PointD(15.9, 4)));
        Assert.Equal(new TextHit(1, 3, 1, isTrailing: true, isInside: false), layout.HitTest(new PointD(8, 30)));
        Assert.Equal(new TextHit(0, 0, 1, isTrailing: false, isInside: false), layout.HitTest(new PointD(40, 4)));
        Assert.Equal(new TextHit(1, 3, 1, isTrailing: false, isInside: false), layout.HitTest(new PointD(-5, 4)));
        Assert.Equal(new TextHit(0, 0, 1, isTrailing: false, isInside: false), layout.HitTest(new PointD(24, -2)));

        // A caret lies across its line, and a selection covers its characters down the line
        Assert.Equal(new RectangleD(16, 0, 16, 0), layout.GetCaretRectangle(0));
        Assert.Equal(new RectangleD(16, 16, 16, 0), layout.GetCaretRectangle(1));
        Assert.Equal(new RectangleD(16, 32, 16, 0), layout.GetCaretRectangle(2));
        Assert.Equal(new RectangleD(0, 0, 16, 0), layout.GetCaretRectangle(3));
        Assert.Equal(new RectangleD(0, 16, 16, 0), layout.GetCaretRectangle(4));
        Assert.Equal([new RectangleD(16, 0, 16, 32)], layout.GetSelectionRectangles(0, 2));
        Assert.Equal([new RectangleD(16, 16, 16, 16), new RectangleD(0, 0, 16, 16)], layout.GetSelectionRectangles(1, 3));
        Assert.Empty(layout.GetSelectionRectangles(1, 0));

        // With lines that go to the right, the first line is the column from 0 to 16
        layout = TextLayout.Create("\u4E00\u4E8C\n\u4E09", style, Vertical(mode: TextWritingMode.VerticalLeftToRight));
        Assert.Equal(new TextHit(0, 1, 1, isTrailing: false, isInside: true), layout.HitTest(new PointD(8, 20)));
        Assert.Equal(new TextHit(1, 3, 1, isTrailing: false, isInside: true), layout.HitTest(new PointD(16, 4)));
        Assert.Equal(new TextHit(1, 3, 1, isTrailing: false, isInside: false), layout.HitTest(new PointD(40, 4)));
        Assert.Equal(new RectangleD(0, 16, 16, 0), layout.GetCaretRectangle(1));
        Assert.Equal(new RectangleD(16, 0, 16, 0), layout.GetCaretRectangle(3));
        Assert.Equal([new RectangleD(0, 16, 16, 16), new RectangleD(16, 0, 16, 16)], layout.GetSelectionRectangles(1, 3));
    }

    [Fact]
    public void AVerticalLineHasDecorationsBackgroundsAndPlaceholders()
    {
        // The underline is on the left of the line, where it is under the text that is on its side: 2 pixels after
        // the baseline of that text, which is 5 pixels on the left of the middle of the line. The strikethrough of
        // upright text is in the middle of the line. The background is the whole line.
        var layout = TextLayout.Create("\u4E8C", new TextStyle(Ideographs, 16) { Decorations = TextDecorations.Underline }, Vertical());
        AssertRectangles(layout, new PointD(4, 2), UprightB(0, 0), (new RectangleD(0, 0, 1, 16), null));
        layout = TextLayout.Create("\u4E8C", new TextStyle(Ideographs, 16) { Decorations = TextDecorations.Strikethrough }, Vertical());
        AssertRectangles(layout, new PointD(4, 2), UprightB(0, 0), (new RectangleD(7.5, 0, 1, 16), null));
        layout = TextLayout.Create("\u4E8C\u4E00", new TextStyle(Ideographs, 16) { BackgroundBrush = Blue, Brush = Red }, Vertical());
        AssertRectangles(layout, new PointD(4, 2), (new RectangleD(0, 0, 16, 32), Blue), (UprightB(0, 0).Rectangle, Red), (UprightA(0, 16).Rectangle, Red));

        // A placeholder takes its height down the line and is centered on it; a wide one widens its line
        var style = new TextStyle(Ideographs, 16);
        var text = new RichTextBuilder().Append("\u4E00", style).AppendPlaceholder(10, 6, 0, style).Append("\u4E8C", style).Build();
        layout = TextLayout.Create(text, Vertical());
        Assert.Equal(16, layout.Width);
        Assert.Equal(38, layout.Height);
        Assert.Equal([new RectangleD(3, 16, 10, 6)], layout.Placeholders);
        AssertRectangles(layout, new PointD(0, 0), UprightA(0, 0), UprightB(0, 22));
        text = new RichTextBuilder().Append("\u4E00", style).AppendPlaceholder(24, 6, 0, style).Build();
        layout = TextLayout.Create(text, Vertical());
        Assert.Equal(24, layout.Width);
        Assert.Equal(12, layout.Baseline);
        Assert.Equal([new RectangleD(0, 16, 24, 6)], layout.Placeholders);
        AssertRectangles(layout, new PointD(0, 0), UprightA(4, 0));
    }

    [Fact]
    public void AVerticalTextFollowsAPathFromItsTop()
    {
        // Down a vertical line, the text is the one drawn at a point: the path is the baseline of the first line,
        // in the middle of it, and the lines that follow are on its left
        var style = new TextStyle(Ideographs, 16) { Decorations = TextDecorations.Underline, BackgroundBrush = Blue };
        var down = new VectorPathBuilder().MoveTo(60, 5).LineTo(60, 70).Build();
        foreach (var orientation in (TextOrientation[])[TextOrientation.Mixed, TextOrientation.Upright, TextOrientation.Sideways])
        {
            var layout = TextLayout.Create("\u4E00A\u4E8C\n\u4E09B", style, Vertical(orientation: orientation));
            var expected = Render(120, 70, canvas => canvas.FillText(White, layout, new PointD(60 - layout.Baseline, 5)));
            Assert.Contains(expected, sample => sample != 0);
            Assert.Equal(expected, Render(120, 70, canvas => canvas.FillText(White, layout, down)));

            // Aligned at the end of the path, the text ends there
            var end = Render(120, 70, canvas => canvas.FillText(White, layout, new PointD(60 - layout.Baseline, 70 - layout.Height)));
            Assert.Equal(end, Render(120, 70, canvas => canvas.FillText(White, layout, down, new TextPathOptions { Alignment = TextPathAlignment.End })));
        }

        // Along a line that goes to the right, the text is turned with its line: a quarter of a turn counterclockwise
        var upright = TextLayout.Create("\u4E00\u4E8C", new TextStyle(Ideographs, 16), Vertical());
        var right = new VectorPathBuilder().MoveTo(10, 30).LineTo(110, 30).Build();
        Assert.Equal(
            Render(120, 70, canvas =>
            {
                canvas.FillRectangle(White, new RectangleD(12, 24, 12, 12));
                canvas.FillRectangle(White, new RectangleD(30, 27, 8, 6));
            }),
            Render(120, 70, canvas => canvas.FillText(White, upright, right)));
    }

    [Fact]
    public void AFontSetsItsVerticalFormsAndItsVerticalMetrics()
    {
        // In this font, the brackets, the comma, the full stop and the long vowel mark have vertical forms: they
        // are upright, as the ideographs are, and its Latin letters are on their side
        var font = Font.Load(FixtureRoot.GetDirectory() / "fonts" / "noto-sans-cjk" / "NotoSansCJKjp-Regular.subset.otf");
        var style = new TextStyle(font, 16);
        const string Japanese = "\u300C\u65E5\u672C\u300D\u3001\u30FC\u3002";
        var mixed = RenderVertical(TextLayout.Create(Japanese, style, Vertical()));
        Assert.Contains(mixed, sample => sample != 0);
        Assert.Equal(RenderVertical(TextLayout.Create(Japanese, style, Vertical(orientation: TextOrientation.Upright))), mixed);
        Assert.NotEqual(RenderVertical(TextLayout.Create(Japanese, style, Vertical(orientation: TextOrientation.Sideways))), mixed);

        // On their side, the Latin letters have the middle of the ideographic em box of the font (BASE: 380 units
        // above the baseline) on the central baseline, not the middle of its ascent and its descent (436 units): they
        // are 0.896 pixels on the right of where a line whose text is all on its side has them
        var mixedLayout = TextLayout.Create("ABC", style, Vertical());
        var sideways = TextLayout.Create("ABC", style, Vertical(orientation: TextOrientation.Sideways));
        Assert.Equal(sideways.Width, mixedLayout.Width, 1e-9);
        Assert.Equal(sideways.InkBounds.X + 0.896, mixedLayout.InkBounds.X, 1e-9);
        Assert.Equal(sideways.InkBounds.Width, mixedLayout.InkBounds.Width, 1e-9);
        Assert.Equal(sideways.InkBounds.Y, mixedLayout.InkBounds.Y, 1e-9);
        Assert.Equal(sideways.InkBounds.Height, mixedLayout.InkBounds.Height, 1e-9);
        Assert.NotEqual(RenderVertical(TextLayout.Create("ABC", style, Vertical(orientation: TextOrientation.Upright))), RenderVertical(mixedLayout));

        // The vertical forms are a feature of the font, which a style can turn off: the glyphs of a horizontal line
        // are then set upright
        var withoutForms = TextLayout.Create(Japanese, new TextStyle(font, 16) { Features = [new FontFeature("vert", 0)] }, Vertical(orientation: TextOrientation.Upright));
        Assert.NotEqual(RenderVertical(TextLayout.Create(Japanese, style, Vertical(orientation: TextOrientation.Upright))), RenderVertical(withoutForms));

        // The rotated forms of this font are its vertical forms: it has no rotated glyph for the Latin letters
        var rotated = new TextStyle(font, 16) { Features = [new FontFeature("vrt2")] };
        Assert.Equal(RenderVertical(TextLayout.Create(Japanese, style, Vertical())), RenderVertical(TextLayout.Create(Japanese, rotated, Vertical())));
        Assert.Equal(RenderVertical(mixedLayout), RenderVertical(TextLayout.Create("ABC", rotated, Vertical())));

        // Two digits set in the room of one character take the forms of the font that are half an em wide (hwid);
        // three are narrowed, since the font has no forms a third of an em wide
        var combined = new TextStyle(font, 16) { IsCombinedUpright = true };
        Assert.Equal(1, GetWidthScale(TextLayout.Create("12", combined, Vertical())));
        Assert.Equal(1000.0 / 1665, GetWidthScale(TextLayout.Create("123", combined, Vertical())), 1e-9);

        // Every glyph advances by one em, the advance height of the font, and the line is as wide as its line height
        var layout = TextLayout.Create(Japanese, style, Vertical());
        var horizontal = TextLayout.Create(Japanese, style);
        Assert.Equal(7 * 16, layout.Height);
        Assert.Equal(horizontal.Height, layout.Width);
        Assert.Equal(horizontal.Height / 2, layout.Baseline);
        Assert.False(layout.HasMissingGlyphs);

        static int[] RenderVertical(TextLayout layout) => Render(60, 140, canvas => canvas.FillText(White, layout, new PointD(10, 5)));

        static double GetWidthScale(TextLayout layout)
        {
            Assert.Equal(1, layout.RunCount);
            layout.GetRun(0, out _, out _, out _, out _, out _, out var isSideways, out var widthScale);
            Assert.False(isSideways);
            return widthScale;
        }
    }

    [Fact]
    public void ABaselineTablePlacesTheTextOnItsSideInAVerticalLine()
    {
        // The ideographic em box of the font is from -128 to 896 units: its middle, 6 pixels above the baseline, is
        // on the central baseline of the line, where the middle of the ascent and the descent, 5 pixels above it, is
        // without the table. The text on its side is 1 pixel further to the left, and so is the underline.
        var font = CreateIdeographs(builder => builder.RawTables["BASE"] = BuildBaseTable(("ideo", -128), ("idtp", 896)));
        var style = new TextStyle(font, 16);
        var layout = TextLayout.Create("AB", style, Vertical());
        Assert.Equal(16, layout.Width);
        AssertRectangles(layout, new PointD(0, 0), SidewaysA(-1, 0), SidewaysB(-1, 14));
        layout = TextLayout.Create("\u4E00A", style, Vertical());
        AssertRectangles(layout, new PointD(2, 1), UprightA(0, 0), SidewaysA(-1, 16));
        layout = TextLayout.Create("\u4E8C", new TextStyle(font, 16) { Decorations = TextDecorations.Underline }, Vertical());
        AssertRectangles(layout, new PointD(4, 2), UprightB(0, 0), (new RectangleD(-1, 0, 1, 16), null));

        // The top of the box is one em above its bottom when the table only gives the bottom
        font = CreateIdeographs(builder => builder.RawTables["BASE"] = BuildBaseTable(("ideo", -128)));
        AssertRectangles(TextLayout.Create("AB", new TextStyle(font, 16), Vertical()), new PointD(0, 0), SidewaysA(-1, 0), SidewaysB(-1, 14));

        // A horizontal line, and a vertical line whose text is all on its side, have the baseline of their text
        Assert.Equal(13, TextLayout.Create("AB", new TextStyle(font, 16)).Baseline);
        AssertRectangles(TextLayout.Create("AB", new TextStyle(font, 16), Vertical(orientation: TextOrientation.Sideways)), new PointD(0, 0), SidewaysA(0, 0), SidewaysB(0, 14));
    }

    [Fact]
    public void AVerticalLineAppliesTheFeaturesOfItsOrientation()
    {
        // The glyphs on their side have the forms their font has for that (vrtr): B in the place of A
        var font = CreateIdeographs(builder => builder.RawTables["GSUB"] = FontTestSupport.BuildSingleSubstitutions(("vrtr", FontSamples.BoxA, FontSamples.BoxB)));
        AssertRectangles(TextLayout.Create("A", new TextStyle(font, 16), Vertical()), new PointD(0, 0), SidewaysB(0, 0));
        AssertRectangles(TextLayout.Create("A", new TextStyle(font, 16), Vertical(orientation: TextOrientation.Upright)), new PointD(0, 0), UprightA(0, 0));
        AssertRectangles(TextLayout.Create("A", new TextStyle(font, 16)), new PointD(0, 0), A(0, 0));
        AssertRectangles(TextLayout.Create("A", new TextStyle(font, 16) { Features = [new FontFeature("vrtr", 0)] }, Vertical()), new PointD(0, 0), SidewaysA(0, 0));

        // A feature of one direction is applied as its counterpart where the glyphs go the other way: the
        // horizontal kana alternates of the style are the vertical ones of the font for upright text
        font = CreateIdeographs(builder => builder.RawTables["GSUB"] = FontTestSupport.BuildSingleSubstitutions(("vkna", FontSamples.BoxA, FontSamples.BoxC)));
        var style = new TextStyle(font, 16) { Features = [new FontFeature("hkna")] };
        AssertRectangles(TextLayout.Create("\u4E00", style, Vertical()), new PointD(0, 0), UprightC(0, 0));
        AssertRectangles(TextLayout.Create("\u4E00", style), new PointD(0, 0), A(0, 0));
        AssertRectangles(TextLayout.Create("\u4E00", new TextStyle(font, 16), Vertical()), new PointD(0, 0), UprightA(0, 0));

        // With the rotated forms of the font (vrt2), a character the font has a rotated glyph for is upright with
        // that glyph, the frame E as C here; the vertical forms are not applied, and the other characters are turned
        font = CreateIdeographs(builder => builder.RawTables["GSUB"] = FontTestSupport.BuildSingleSubstitutions(("vert", FontSamples.BoxA, FontSamples.BoxB), ("vrt2", 6, FontSamples.BoxC)));
        style = new TextStyle(font, 16) { Features = [new FontFeature("vrt2")] };
        var layout = TextLayout.Create("E\u4E00B", style, Vertical());
        Assert.Equal(40, layout.Height);
        AssertRectangles(layout, new PointD(0, 0), UprightC(0, 0), UprightA(0, 16), SidewaysB(0, 32));
        layout = TextLayout.Create("E\u4E00", new TextStyle(font, 16), Vertical());
        Assert.Equal(32, layout.Height);
        AssertRectangles(layout, new PointD(0, 0), (new RectangleD(3, 0, 12, 2), null), (new RectangleD(3, 14, 12, 2), null), (new RectangleD(3, 0, 2, 16), null), (new RectangleD(13, 0, 2, 16), null), UprightB(0, 16));
    }

    [Fact]
    public void ATextIsSetHorizontallyInTheRoomOfOneUprightCharacter()
    {
        // B and B are 16 pixels wide, one em: across the line, centered on it, and down the line in the middle of
        // the em, with the middle of the font (5 pixels above the baseline) there. The unit advances by one em.
        var style = new TextStyle(Ideographs, 16);
        var combined = new TextStyle(Ideographs, 16) { IsCombinedUpright = true };
        var text = new RichTextBuilder().Append("\u4E00", style).Append("BB", combined).Append("\u4E00", style).Build();
        var layout = TextLayout.Create(text, Vertical());
        Assert.Equal(16, layout.Width);
        Assert.Equal(48, layout.Height);
        AssertRectangles(layout, new PointD(0, 0), UprightA(0, 0), CombinedB(0, 16), CombinedB(8, 16), UprightA(0, 32));

        // It is one cluster down the line, whose characters share the em
        Assert.Equal(new RectangleD(0, 16, 16, 0), layout.GetCaretRectangle(1));
        Assert.Equal(new RectangleD(0, 24, 16, 0), layout.GetCaretRectangle(2));
        Assert.Equal(new RectangleD(0, 32, 16, 0), layout.GetCaretRectangle(3));
        Assert.Equal([new RectangleD(0, 16, 16, 16)], layout.GetSelectionRectangles(1, 2));

        // A wider text is narrowed to one em: A and A, 32 pixels, are drawn at half their width
        layout = TextLayout.Create("AA", combined, Vertical());
        Assert.Equal(16, layout.Height);
        Assert.Equal(RectangleD.FromLTRB(1, 1, 15, 13), layout.InkBounds);
        AssertRectangles(layout, new PointD(0, 0), (new RectangleD(1, 1, 6, 12), null), (new RectangleD(9, 1, 6, 12), null));
        var down = new VectorPathBuilder().MoveTo(60, 5).LineTo(60, 70).Build();
        Assert.Equal(
            Render(120, 70, canvas => canvas.FillText(White, layout, new PointD(60 - layout.Baseline, 5))),
            Render(120, 70, canvas => canvas.FillText(White, layout, down)));

        // Unless its font has forms half an em wide for two characters (hwid): B in the place of A
        var withWidths = new TextStyle(CreateIdeographs(builder => builder.RawTables["GSUB"] = FontTestSupport.BuildSingleSubstitutions(("hwid", FontSamples.BoxA, FontSamples.BoxB))), 16) { IsCombinedUpright = true };
        AssertRectangles(TextLayout.Create("AA", withWidths, Vertical()), new PointD(0, 0), CombinedB(0, 0), CombinedB(8, 0));

        // A line never ends inside it: B, a space and B are 24 pixels on their side, but one unit of 16 pixels
        var spaced = new RichTextBuilder().Append("\u4E00", style).Append("B B", combined).Append("\u4E00", style).Build();
        layout = TextLayout.Create(spaced, Vertical(maxHeight: 20));
        Assert.Equal(3, layout.LineCount);
        Assert.Equal(new TextLine(1, 3, 16, 0, 16, 16, 8, false), layout.Lines[1]);
        layout = TextLayout.Create(spaced, new TextLayoutOptions { WritingMode = TextWritingMode.VerticalRightToLeft, MaxHeight = 10, OverflowWrap = TextOverflowWrap.Anywhere });
        Assert.Equal(3, layout.LineCount);

        // Nothing changes in a horizontal line, or in a vertical line whose text is all on its side
        Assert.Equal(16, TextLayout.Create("BB", combined).Width);
        Assert.Equal(16, TextLayout.Create("BB", combined, Vertical(orientation: TextOrientation.Sideways)).Height);
        Assert.Equal(8, TextLayout.Create("B", combined, Vertical(orientation: TextOrientation.Sideways)).Height);
        Assert.Equal(16, TextLayout.Create("B", combined, Vertical()).Height);
    }

    [Fact]
    public void ShortRunsOfDigitsAreSetInTheRoomOfOneUprightCharacter()
    {
        // The digits are drawn as B. Two digits are one unit, three are on their side, each one 8 pixels down the line.
        var font = CreateIdeographs(builder =>
        {
            builder.CharacterMap['1'] = FontSamples.BoxB;
            builder.CharacterMap['2'] = FontSamples.BoxB;
            builder.CharacterMap['3'] = FontSamples.BoxB;
        });
        var style = new TextStyle(font, 16);
        var digits = new TextLayoutOptions { WritingMode = TextWritingMode.VerticalRightToLeft, CombineUprightDigits = 2 };
        var layout = TextLayout.Create("\u4E0012\u4E00", style, digits);
        Assert.Equal(48, layout.Height);
        AssertRectangles(layout, new PointD(0, 0), UprightA(0, 0), CombinedB(0, 16), CombinedB(8, 16), UprightA(0, 32));
        Assert.Equal(56, TextLayout.Create("\u4E00123\u4E00", style, digits).Height);
        Assert.Equal(48, TextLayout.Create("\u4E00123\u4E00", style, new TextLayoutOptions { WritingMode = TextWritingMode.VerticalRightToLeft, CombineUprightDigits = 3 }).Height);
        Assert.Equal(48, TextLayout.Create("\u4E001\u4E00", style, digits).Height);
        Assert.Equal(40, TextLayout.Create("\u4E001\u4E00", style, Vertical()).Height);

        // A run of digits that continues in another span is not combined
        var split = new RichTextBuilder().Append("1", style).Append("2", new TextStyle(font, 16) { Brush = Red }).Build();
        Assert.Equal(16, TextLayout.Create(split, digits).Height);
        Assert.Equal(32, TextLayout.Create(new RichTextBuilder().Append("1", style).Append("\u4E00", style).Build(), digits).Height);

        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { CombineUprightDigits = 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { CombineUprightDigits = 5 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextLayoutOptions { CombineUprightDigits = -1 });
    }

    /// <summary>The glyph B of a text set in the room of one upright character, from the left of the line and from where the unit starts down the line.</summary>
    private static (RectangleD Rectangle, Brush? Brush) CombinedB(double x, double y) => (new RectangleD(x + 1, y + 5, 6, 8), null);

    /// <summary>Builds a BASE table whose horizontal axis gives baselines to the default script.</summary>
    private static byte[] BuildBaseTable(params (string Tag, int Coordinate)[] baselines)
    {
        var table = new List<byte>();
        var count = baselines.Length;
        UInt16(table, 1);
        UInt16(table, 0);
        UInt16(table, 8);
        UInt16(table, 0);

        // The axis: its baselines, sorted by tag, then its one script, whose values are in their order
        UInt16(table, 4);
        UInt16(table, 4 + 2 + (4 * count));
        UInt16(table, count);
        foreach (var (tag, _) in baselines)
        {
            Tag(table, tag);
        }

        UInt16(table, 1);
        Tag(table, "DFLT");
        UInt16(table, 8);
        UInt16(table, 6);
        UInt16(table, 0);
        UInt16(table, 0);
        UInt16(table, 0);
        UInt16(table, count);
        for (var i = 0; i < count; i++)
        {
            UInt16(table, 4 + (2 * count) + (4 * i));
        }

        foreach (var (_, coordinate) in baselines)
        {
            UInt16(table, 1);
            UInt16(table, coordinate);
        }

        return [.. table];
    }

    private static void UInt16(List<byte> table, int value)
    {
        table.Add((byte)(value >> 8));
        table.Add((byte)value);
    }

    private static void Tag(List<byte> table, string tag)
    {
        foreach (var character in tag)
        {
            table.Add((byte)character);
        }
    }

    private static (RectangleD Rectangle, Brush? Brush) A(double x, double y) => (new RectangleD(x + 2, y + 1, 12, 12), null);

    private static (RectangleD Rectangle, Brush? Brush) B(double x, double y) => (new RectangleD(x + 1, y + 5, 6, 8), null);

    private static (RectangleD Rectangle, Brush? Brush) C(double x, double y) => (new RectangleD(x, y, 12, 16), null);

    private static (RectangleD Rectangle, Brush? Brush) Hyphen(double x, double y) => (new RectangleD(x + 1, y + 8, 6, 2), null);

    private static TextLayoutOptions Vertical(
        TextWritingMode mode = TextWritingMode.VerticalRightToLeft,
        TextOrientation orientation = TextOrientation.Mixed,
        double maxHeight = double.PositiveInfinity,
        double maxWidth = double.PositiveInfinity,
        TextAlignment alignment = TextAlignment.Start)
        => new() { WritingMode = mode, Orientation = orientation, MaxHeight = maxHeight, MaxWidth = maxWidth, Alignment = alignment };

    /// <summary>The glyph A upright in a vertical line, from the left of the line and from where the pen is down the line.</summary>
    private static (RectangleD Rectangle, Brush? Brush) UprightA(double x, double y) => (new RectangleD(x + 2, y + 2, 12, 12), null);

    private static (RectangleD Rectangle, Brush? Brush) UprightB(double x, double y) => (new RectangleD(x + 5, y + 4, 6, 8), null);

    private static (RectangleD Rectangle, Brush? Brush) UprightC(double x, double y) => (new RectangleD(x + 2, y, 12, 16), null);

    /// <summary>The glyph A on its side in a vertical line, from the left of the line and from where the pen is down the line.</summary>
    private static (RectangleD Rectangle, Brush? Brush) SidewaysA(double x, double y) => (new RectangleD(x + 3, y + 2, 12, 12), null);

    private static (RectangleD Rectangle, Brush? Brush) SidewaysB(double x, double y) => (new RectangleD(x + 3, y + 1, 8, 6), null);

    private static (RectangleD Rectangle, Brush? Brush) SidewaysC(double x, double y) => (new RectangleD(x, y, 16, 12), null);

    /// <summary>Turns the samples of an image a quarter of a turn clockwise: the pixel (x, y) becomes the pixel (height - 1 - y, x).</summary>
    private static int[] TurnClockwise(int[] samples, int width, int height)
    {
        var channels = samples.Length / (width * height);
        var result = new int[samples.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                samples.AsSpan(((y * width) + x) * channels, channels).CopyTo(result.AsSpan(((x * height) + (height - 1 - y)) * channels, channels));
            }
        }

        return result;
    }

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

    [Fact]
    public void ALineThatEndsWithManySpacesIsCutOnceItsSpacesAreKnown()
    {
        // The first line is "AB" (22 pixels: 16 and 8, kerned by 2) and 200 spaces that hang; the part of the text the
        // line is cut from is long enough for the start of the next part to be measured before it is shaped again.
        var text = "AB" + new string(' ', 200) + "AB AB";
        var layout = TextLayout.Create(text, new TextStyle(Boxes, 16), new TextLayoutOptions { MaxWidth = 1000 });
        Assert.Equal(
            new[] { new TextLine(0, 202, 0, 0, 22, 16, 13, false), new TextLine(202, 5, 0, 16, 52, 16, 13, false) },
            layout.Lines);
    }
}
