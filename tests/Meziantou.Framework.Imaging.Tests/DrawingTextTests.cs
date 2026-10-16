using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Laying out and drawing text. The font of most tests is the boxes font of Common/FontSamples.cs: 1024 units per em and
/// glyphs that are rectangles on multiples of 64 units, so that at 16 pixels per em every glyph is a rectangle of whole
/// pixels, whose position is computed by hand. The ascent is 13 pixels, the descent 3, and there is no line gap.
/// </summary>
public sealed class DrawingTextTests
{
    private static readonly Brush White = new SolidBrush(new Rgba32(255, 255, 255));
    private static readonly Brush Translucent = new SolidBrush(new Rgba32(255, 255, 255, 128));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Font Boxes { get; } = FontTestSupport.Load(FontSamples.Boxes());

    [Fact]
    public void AGlyphIsDrawnAtItsPositionOnTheBaseline()
    {
        // A is the rectangle (128, 0)-(896, 768): 2 to 14 pixels from its origin, 12 pixels up from the baseline, which
        // is 13 pixels below the top of the layout.
        AssertRectangles("A", new PointD(3, 2), new RectangleD(5, 3, 12, 12));

        // C goes from the descent to the ascent: the whole height of the line.
        AssertRectangles("C", new PointD(0, 0), new RectangleD(0, 0, 12, 16));

        // B is (64, 0)-(448, 512), and a space draws nothing: it only moves by its advance, 8 pixels.
        AssertRectangles(" B", new PointD(1, 1), new RectangleD(10, 6, 6, 8));

        // The canvas clips: a text can start outside.
        AssertRectangles("A", new PointD(-4, -3), new RectangleD(-2, -2, 12, 12));
        AssertRectangles("A", new PointD(100, 100));
    }

    [Fact]
    public void GlyphsFollowEachOtherByTheirAdvanceAndTheirKerning()
    {
        // A advances by 16 pixels and B by 8. The kerning table moves B 2 pixels closer after A, and A 1 pixel further
        // after B; it has nothing for the other pairs.
        AssertRectangles("AA", new PointD(0, 0), new RectangleD(2, 1, 12, 12), new RectangleD(18, 1, 12, 12));
        AssertRectangles("AB", new PointD(0, 0), new RectangleD(2, 1, 12, 12), new RectangleD(15, 5, 6, 8));
        AssertRectangles("BA", new PointD(0, 0), new RectangleD(1, 5, 6, 8), new RectangleD(11, 1, 12, 12));
        AssertRectangles("ABA", new PointD(0, 0), new RectangleD(2, 1, 12, 12), new RectangleD(15, 5, 6, 8), new RectangleD(25, 1, 12, 12));

        var layout = TextLayout.Create("AB", Boxes, 16);
        Assert.Equal(22, layout.Width);
        Assert.Equal(16, layout.Height);
        Assert.Equal(13, layout.Baseline);
        Assert.Equal(1, layout.LineCount);
        Assert.Equal(RectangleD.FromLTRB(2, 1, 21, 13), layout.InkBounds);
        Assert.False(layout.HasMissingGlyphs);

        // The size scales everything: at 32 pixels per em, twice.
        layout = TextLayout.Create("AB", Boxes, 32);
        Assert.Equal(44, layout.Width);
        Assert.Equal(32, layout.Height);
        Assert.Equal(26, layout.Baseline);
        Assert.Equal(RectangleD.FromLTRB(4, 2, 42, 26), layout.InkBounds);
    }

    [Fact]
    public void LineBreaksStartANewLine()
    {
        // The second line is one line height, 16 pixels, below the first one.
        foreach (var lineBreak in (string[])["\n", "\r", "\r\n", "\u000B", "\u000C", "\u0085", "\u2028", "\u2029"])
        {
            var text = "A" + lineBreak + "B";
            AssertRectangles(text, new PointD(0, 0), new RectangleD(2, 1, 12, 12), new RectangleD(1, 21, 6, 8));
            var layout = TextLayout.Create(text, Boxes, 16);
            Assert.Equal(2, layout.LineCount);
            Assert.Equal(16, layout.Width);
            Assert.Equal(32, layout.Height);
        }

        // Two line breaks are an empty line; a line break at the end starts an empty line; kerning does not cross lines.
        Assert.Equal(3, TextLayout.Create("A\n\r\nB", Boxes, 16).LineCount);
        Assert.Equal(3, TextLayout.Create("A\n\rB", Boxes, 16).LineCount);
        var trailing = TextLayout.Create("AB\n", Boxes, 16);
        Assert.Equal(2, trailing.LineCount);
        Assert.Equal(32, trailing.Height);
        Assert.Equal(22, trailing.Width);
        AssertRectangles("A\nBA", new PointD(0, 0), new RectangleD(2, 1, 12, 12), new RectangleD(1, 21, 6, 8), new RectangleD(11, 17, 12, 12));

        var empty = TextLayout.Create("", Boxes, 16);
        Assert.Equal(1, empty.LineCount);
        Assert.Equal(0, empty.Width);
        Assert.Equal(16, empty.Height);
        Assert.Equal(RectangleD.Empty, empty.InkBounds);
        Assert.True(empty.ToPath(new PointD(0, 0)).IsEmpty);
        AssertRectangles("", new PointD(0, 0));
        AssertRectangles(" \n ", new PointD(0, 0));
    }

    [Fact]
    public void CharactersWithoutVisibleFormAreNotDrawn()
    {
        // A tabulation moves to the next multiple of eight spaces, 64 pixels.
        AssertRectangles("\tB", new PointD(0, 0), new RectangleD(65, 5, 6, 8));
        AssertRectangles("B\tB", new PointD(0, 0), new RectangleD(1, 5, 6, 8), new RectangleD(65, 5, 6, 8));
        Assert.Equal(128, TextLayout.Create("\t\t", Boxes, 16).Width);

        // Control and format characters take no space, and do not come between the two glyphs of a kerning pair.
        foreach (var invisible in (string[])["\u0001", "\u007F", "\u00AD", "\u200B", "\u200D", "\u200E", "\u2060", "\uFEFF", "\uFE0F", "\U000E0001"])
        {
            var layout = TextLayout.Create("A" + invisible + "B", Boxes, 16);
            Assert.Equal(22, layout.Width);
            Assert.Equal(RectangleD.FromLTRB(2, 1, 21, 13), layout.InkBounds);
            Assert.False(layout.HasMissingGlyphs);
        }
    }

    [Fact]
    public void ACharacterTheFontDoesNotHaveIsDrawnWithTheMissingGlyph()
    {
        // The missing glyph is a frame, (64, 0)-(448, 768) with a hole, which advances by 8 pixels.
        var layout = TextLayout.Create("AZB", Boxes, 16);
        Assert.True(layout.HasMissingGlyphs);
        Assert.Equal(32, layout.Width);
        Assert.Equal(RectangleD.FromLTRB(2, 1, 31, 13), layout.InkBounds);
        Assert.False(TextLayout.Create("AB", Boxes, 16).HasMissingGlyphs);

        // The frame is 1 pixel thick: its hole is not painted. A lone surrogate is not a character.
        foreach (var text in (string[])["Z", "\uD83D", "\U0001F600"])
        {
            using var image = new Image<Gray8>(10, 16, new Gray8(0));
            image.Draw(canvas => canvas.FillText(White, text, Boxes, 16, new PointD(0, 0)), Ct);
            var samples = DrawingTestSupport.ReadSamples(image.Frames[0]);
            Assert.Equal(255, samples[(1 * 10) + 1]);
            Assert.Equal(255, samples[(12 * 10) + 6]);
            Assert.Equal(0, samples[(6 * 10) + 4]);
            Assert.Equal(0, samples[(6 * 10) + 8]);
            Assert.True(TextLayout.Create(text, Boxes, 16).HasMissingGlyphs);
        }
    }

    [Fact]
    public void AVariationSequenceUsesItsGlyphWhenTheFontHasOne()
    {
        var builder = FontSamples.Boxes();
        builder.CmapFormats = FontCmapFormats.Format4 | FontCmapFormats.Format14;
        builder.VariationSequences[(0xFE00, 'A')] = FontSamples.BoxB;
        builder.VariationSequences[(0xFE01, 'A')] = -1;
        var font = FontTestSupport.Load(builder);

        // The sequence the font maps is drawn as B; the one it declares as the default form, and one it does not know,
        // are drawn as A. The selector itself is never drawn.
        Assert.Equal(RectangleD.FromLTRB(1, 5, 7, 13), TextLayout.Create("A\uFE00", font, 16).InkBounds);
        Assert.Equal(RectangleD.FromLTRB(2, 1, 14, 13), TextLayout.Create("A\uFE01", font, 16).InkBounds);
        Assert.Equal(RectangleD.FromLTRB(2, 1, 14, 13), TextLayout.Create("A\uFE02", font, 16).InkBounds);
        Assert.Equal(8, TextLayout.Create("A\uFE00", font, 16).Width);
        Assert.False(TextLayout.Create("A\uFE02\uFE00", font, 16).HasMissingGlyphs);
    }

    [Fact]
    public void GlyphsThatOverlapArePaintedOnce()
    {
        // F is two squares that overlap, (0, 0)-(512, 512) and (256, 256)-(768, 768): with a half-transparent brush,
        // the overlap has the same color as the rest.
        using var image = new Image<Gray8>(16, 16, new Gray8(0));
        image.Draw(canvas => canvas.FillText(Translucent, "F", Boxes, 16, new PointD(0, 0)), Ct);
        var samples = DrawingTestSupport.ReadSamples(image.Frames[0]);
        var once = samples[(12 * 16) + 1];
        Assert.InRange(once, 127, 129);
        Assert.Equal(once, samples[(6 * 16) + 6]);
        Assert.Equal(once, samples[(2 * 16) + 10]);
        Assert.Equal(0, samples[(2 * 16) + 1]);

        // Two glyphs of a text that overlap: the mark of the composite glyph, and A after a kerning that brings it back.
        var builder = FontSamples.Boxes();
        builder.KerningPairs[(FontSamples.BoxA, FontSamples.BoxA)] = -512;
        var font = FontTestSupport.Load(builder);
        using var overlapping = new Image<Gray8>(32, 16, new Gray8(0));
        overlapping.Draw(canvas => canvas.FillText(Translucent, "AA", font, 16, new PointD(0, 0)), Ct);
        samples = DrawingTestSupport.ReadSamples(overlapping.Frames[0]);

        // The first A covers the pixels 2 to 14, the second one 10 to 22.
        Assert.Equal(once, samples[(6 * 32) + 4]);
        Assert.Equal(once, samples[(6 * 32) + 12]);
        Assert.Equal(once, samples[(6 * 32) + 20]);
        Assert.Equal(0, samples[(6 * 32) + 23]);
    }

    [Fact]
    public void GlyphsOfOppositeOrientationsDoNotCancelEachOther()
    {
        // Two squares drawn in opposite directions, the second one half over the first one. With the nonzero rule their
        // overlap would be outside the shape: each glyph is given the same orientation before it is filled.
        var builder = new FontBuilder { CmapFormats = FontCmapFormats.Format4, UnitsPerEm = 1024, Ascender = 832, Descender = -192 };
        var cff = new FontCffWriter();
        cff.CharStrings.Add(new FontCharstring().Op(FontCharstring.EndChar).ToArray());
        cff.CharStrings.Add(new FontCharstring().Op(FontCharstring.RMoveTo, 0, 0).Op(FontCharstring.RLineTo, 512, 0, 0, 512, -512, 0).Op(FontCharstring.EndChar).ToArray());
        cff.CharStrings.Add(new FontCharstring().Op(FontCharstring.RMoveTo, 0, 0).Op(FontCharstring.RLineTo, 0, 512, 512, 0, 0, -512).Op(FontCharstring.EndChar).ToArray());
        builder.AddGlyph(FontGlyph.Empty(256));
        builder.AddGlyph(FontGlyph.Empty(256), 'x');
        builder.AddGlyph(FontGlyph.Empty(256), 'y');
        cff.Apply(builder);
        var font = FontTestSupport.Load(builder);

        foreach (var text in (string[])["xy", "yx", "xx", "yy"])
        {
            using var image = new Image<Gray8>(16, 16, new Gray8(0));
            image.Draw(canvas => canvas.FillText(Translucent, text, font, 16, new PointD(0, 0)), Ct);
            using var expected = new Image<Gray8>(16, 16, new Gray8(0));
            expected.Draw(canvas => canvas.FillRectangle(Translucent, new RectangleD(0, 5, 12, 8)), Ct);
            Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(image.Frames[0]));
        }
    }

    public static TheoryData<string, string, double> PathCases()
    {
        return new TheoryData<string, string, double>
        {
            { "fonts/noto-sans/NotoSans-Regular.subset.ttf", "Hamburgefonstiv AV. Ty\u00E9\n\u00C0 la carte: fi \u20AC42!", 23.7 },
            { "fonts/noto-sans/NotoSans-Regular.subset.woff2", "Wave To.", 61.5 },
            { "fonts/noto-sans-cff/NotoSans-Regular.subset.otf", "Hamburgefonstiv AV. Ty\u00E9\n\u00C0 la carte: fi \u20AC42!", 23.7 },
            { "fonts/noto-sans-cjk/NotoSansCJKjp-Regular.subset.otf", "\u65E5\u672C\u8A9E\u306E\u30C6\u30AD\u30B9\u30C8 Abc", 31.25 },
            { "fonts/synthetic/curves.ttf", "OPQR", 40 },
            { "fonts/synthetic/charstrings.otf", "ABCDEFGI", 7.5 },
        };
    }

    [Theory]
    [MemberData(nameof(PathCases))]
    public void DrawingALayoutIsDrawingItsPath(string fontPath, string text, double size)
    {
        // The path of a layout has the outlines of its glyphs, curves included: filling and stroking it must change
        // the same pixels to the same values as filling and stroking the layout, in every pixel format.
        var font = Font.Load(FixtureRoot.GetDirectory() / fontPath);
        var layout = TextLayout.Create(text, font, size);
        var origin = new PointD(3.3, 5.6);
        var path = layout.ToPath(origin);
        Assert.False(path.IsEmpty);
        Assert.False(layout.HasMissingGlyphs);

        // The bounds of the path are those of its control points: the ink bounds of the layout, moved to the origin.
        var ink = layout.InkBounds;
        Assert.Equal(ink.Left + origin.X, path.Bounds.Left, 1e-9);
        Assert.Equal(ink.Top + origin.Y, path.Bounds.Top, 1e-9);
        Assert.Equal(ink.Right + origin.X, path.Bounds.Right, 1e-9);
        Assert.Equal(ink.Bottom + origin.Y, path.Bounds.Bottom, 1e-9);

        var pen = new Pen(new SolidBrush(new Rgba32(20, 40, 250, 200)), 1.3) { LineJoin = LineJoin.Round };
        var brush = new SolidBrush(new Rgba32(250, 120, 10, 150));
        foreach (var format in (PixelFormat[])[PixelFormat.Rgba32, PixelFormat.Gray16])
        {
            var fromLayout = Render(format, 420, 110, canvas =>
            {
                canvas.FillText(brush, layout, origin);
                canvas.StrokeText(pen, layout, origin);
            });
            var fromPath = Render(format, 420, 110, canvas =>
            {
                canvas.Fill(brush, path);
                canvas.Stroke(pen, path);
            });
            Assert.Contains(fromPath, sample => sample != 0);
            Assert.Equal(fromPath, fromLayout);
        }
    }

    [Fact]
    public void ALayoutIsDrawnOnEveryFrameAndAnyNumberOfTimes()
    {
        var layout = TextLayout.Create("AB", Boxes, 16);
        using var image = new Image<Gray8>(24, 16, new Gray8(0));
        ((Image)image).AppendFrame();
        image.Draw(
            canvas =>
            {
                canvas.FillText(White, layout, new PointD(0, 0));
                canvas.FillText(White, layout, new PointD(0, 0));
            },
            Ct);
        using var expected = new Image<Gray8>(24, 16, new Gray8(0));
        expected.Draw(
            canvas =>
            {
                canvas.FillRectangle(White, new RectangleD(2, 1, 12, 12));
                canvas.FillRectangle(White, new RectangleD(15, 5, 6, 8));
            },
            Ct);
        var samples = DrawingTestSupport.ReadSamples(expected.Frames[0]);
        Assert.Equal(samples, DrawingTestSupport.ReadSamples(image.Frames[0]));
        Assert.Equal(samples, DrawingTestSupport.ReadSamples(image.Frames[1]));

        // The outlines of the rectangles, stroked: the same as stroking the rectangles.
        var pen = new Pen(White, 2);
        using var stroked = new Image<Gray8>(24, 16, new Gray8(0));
        stroked.Draw(canvas => canvas.StrokeText(pen, layout, new PointD(0, 0)), Ct);
        using var strokedRectangles = new Image<Gray8>(24, 16, new Gray8(0));
        strokedRectangles.Draw(
            canvas =>
            {
                var path = new VectorPathBuilder().AddRectangle(new RectangleD(2, 1, 12, 12)).AddRectangle(new RectangleD(15, 5, 6, 8)).Build();
                canvas.Stroke(pen, path);
            },
            Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(strokedRectangles.Frames[0]), DrawingTestSupport.ReadSamples(stroked.Frames[0]));

        // A pen on one side of its path outlines a text without covering it, and a pattern paints a text as any shape
        var rectangles = new VectorPathBuilder().AddRectangle(new RectangleD(2, 1, 12, 12)).AddRectangle(new RectangleD(15, 5, 6, 8)).Build();
        var pattern = new PatternBrush(new Rectangle(0, 0, 2, 2), tile => tile.FillRectangle(White, new Rectangle(0, 0, 1, 1)));
        foreach (var alignment in new[] { PenAlignment.Outside, PenAlignment.Inside })
        {
            var aligned = new Pen(pattern, 2) { Alignment = alignment, LineJoin = LineJoin.Round };
            using var outlined = new Image<Gray8>(24, 16, new Gray8(0));
            outlined.Draw(canvas => canvas.StrokeText(aligned, layout, new PointD(0, 0)), Ct);
            using var outlinedRectangles = new Image<Gray8>(24, 16, new Gray8(0));
            outlinedRectangles.Draw(canvas => canvas.Stroke(aligned, rectangles), Ct);
            Assert.Equal(DrawingTestSupport.ReadSamples(outlinedRectangles.Frames[0]), DrawingTestSupport.ReadSamples(outlined.Frames[0]));
            Assert.Equal(alignment == PenAlignment.Inside ? 255 : 0, outlined.Frames[0][2, 2].Value);
            Assert.Equal(alignment == PenAlignment.Outside ? 255 : 0, outlined.Frames[0][0, 4].Value);
            Assert.Equal(0, outlined.Frames[0][8, 6].Value);
        }

        using var painted = new Image<Gray8>(24, 16, new Gray8(0));
        painted.Draw(canvas => canvas.FillText(pattern, layout, new PointD(0, 0)), Ct);
        using var paintedRectangles = new Image<Gray8>(24, 16, new Gray8(0));
        paintedRectangles.Draw(canvas => canvas.Fill(pattern, rectangles), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(paintedRectangles.Frames[0]), DrawingTestSupport.ReadSamples(painted.Frames[0]));
        Assert.Equal([255, 0, 255, 0], Enumerable.Range(2, 4).Select(x => (int)painted.Frames[0][x, 2].Value));
    }

    [Fact]
    public void TheTextIsLimitedByTheConfigurationOfTheFont()
    {
        static Font Load(ImageResourceLimits limits) => FontTestSupport.Load(FontSamples.Boxes(), new FontLoadOptions { Configuration = new ImageConfiguration { Limits = limits } });

        // The length of the text, in UTF-16 code units, whatever it contains.
        var font = Load(new ImageResourceLimits { MaxTextLength = 3 });
        Assert.Equal(3, TextLayout.Create("A\nB", font, 16).LineCount + 1);
        var exception = Assert.Throws<ImageResourceLimitException>(() => TextLayout.Create("A\n\nB", font, 16));
        Assert.Equal(ImageResourceLimitKind.TextLength, exception.Kind);
        Assert.Equal(3, exception.Limit);
        Assert.Equal(4, exception.Requested);

        // The number of glyphs: a space is a glyph, a line break is not.
        font = Load(new ImageResourceLimits { MaxTextGlyphs = 2 });
        Assert.Equal(2, TextLayout.Create("A\n\nB", font, 16).LineCount - 1);
        exception = Assert.Throws<ImageResourceLimitException>(() => TextLayout.Create("A B", font, 16));
        Assert.Equal(ImageResourceLimitKind.TextGlyphs, exception.Kind);
        Assert.Equal(2, exception.Limit);
        Assert.Equal(3, exception.Requested);

        // The complexity of a glyph: F has 8 points. The layout is not created, so nothing is drawn later.
        font = Load(new ImageResourceLimits { MaxGlyphComplexity = 7 });
        Assert.Equal(16, TextLayout.Create("AB", font, 16).Height);
        exception = Assert.Throws<ImageResourceLimitException>(() => TextLayout.Create("AFB", font, 16));
        Assert.Equal(ImageResourceLimitKind.GlyphComplexity, exception.Kind);
        using var image = new Image<Gray8>(8, 8, new Gray8(0));
        Assert.Throws<ImageResourceLimitException>(() => image.Draw(canvas => canvas.FillText(White, "F", font, 16, new PointD(0, 0)), Ct));
        Assert.Equal(new int[64], DrawingTestSupport.ReadSamples(image.Frames[0]));
    }

    [Fact]
    public void AMalformedGlyphIsReportedWhenTheLayoutIsCreated()
    {
        var builder = new FontBuilder { CmapFormats = FontCmapFormats.Format4 };
        builder.AddGlyph(FontGlyph.Empty(500));
        builder.AddGlyph(FontGlyph.Rectangle(500, 0, 0, 100, 100), 'a');
        builder.AddGlyph(new FontGlyph { AdvanceWidth = 500, RawData = [0, 1], BoundsOverride = new FontBounds(0, 0, 10, 10) }, 'b');
        var font = FontTestSupport.Load(builder);

        Assert.Equal(1, TextLayout.Create("aaa", font, 16).LineCount);
        Assert.Throws<InvalidImageContentException>(() => TextLayout.Create("aab", font, 16));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 6)]
    [InlineData(4, 7)]
    [InlineData(2, 5)]
    [InlineData(5, 5)]
    [InlineData(3, 2)]
    public void ABitmapWithoutColorsIsDrawnPixelForPixelAtItsSize(int indexFormat, int imageFormat)
    {
        var builder = FontSamples.MonochromeBoxes(indexFormat, imageFormat);
        var outlines = FontTestSupport.Load(builder);
        var font = FontTestSupport.Load(builder, new FontLoadOptions { UseEmbeddedBitmaps = true });
        Assert.False(font.HasColorGlyphs);

        // Without the option, a glyph is its outline at every size: A is a full square of 12 pixels.
        Assert.Equal(Fill(new RectangleD(2, 1, 12, 12)), Render(PixelFormat.Gray8, 40, 24, canvas => canvas.FillText(White, "A", outlines, 16, new PointD(0, 0))));

        // With it, at 16 pixels per em: the bitmap of A, the border of that square, 2 pixels from the origin and on the
        // baseline, which is at 13.
        RectangleD[] border = [new(2, 1, 12, 1), new(2, 12, 12, 1), new(2, 2, 1, 10), new(13, 2, 1, 10)];
        Assert.Equal(Fill(border), Render(PixelFormat.Gray8, 40, 24, canvas => canvas.FillText(White, "A", font, 16, new PointD(0, 0))));

        // B: a checkerboard of 6 by 8 pixels, 1 pixel from the origin. The index formats for bitmaps of one size give B
        // the bitmap of A.
        var checkerboard = new List<RectangleD>();
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 6; x++)
            {
                if (((x + y) & 1) == 0)
                {
                    checkerboard.Add(new RectangleD(1 + x, 5 + y, 1, 1));
                }
            }
        }

        Assert.Equal(Fill(indexFormat is 2 or 5 ? border : [.. checkerboard]), Render(PixelFormat.Gray8, 40, 24, canvas => canvas.FillText(White, "B", font, 16, new PointD(0, 0))));

        // C has no bitmap: its outline.
        Assert.Equal(Fill(new RectangleD(0, 0, 12, 16)), Render(PixelFormat.Gray8, 40, 24, canvas => canvas.FillText(White, "C", font, 16, new PointD(0, 0))));

        // At 12 pixels per em, the other strike: the two diagonals of a square of 9 pixels, 1 pixel from the origin.
        // The baseline of the line is at 9.75: the text is drawn a quarter of a pixel lower.
        var diagonals = new List<RectangleD>();
        for (var i = 0; i < 9; i++)
        {
            diagonals.Add(new RectangleD(1 + i, 1 + i, 1, 1));
            if (i != 4)
            {
                diagonals.Add(new RectangleD(1 + i, 9 - i, 1, 1));
            }
        }

        Assert.Equal(Fill([.. diagonals]), Render(PixelFormat.Gray8, 40, 24, canvas => canvas.FillText(White, "A", font, 12, new PointD(0, 0.25))));

        // At a size no strike is made for, and at a size that is not a whole number of pixels: the outlines.
        foreach (var size in (double[])[17, 16.5, 8])
        {
            Assert.Equal(
                Render(PixelFormat.Gray8, 40, 24, canvas => canvas.FillText(White, "AB", outlines, size, new PointD(0, 0))),
                Render(PixelFormat.Gray8, 40, 24, canvas => canvas.FillText(White, "AB", font, size, new PointD(0, 0))));
        }

        // Between two pixels, each pixel of the bitmap covers half of two pixels of the image, like a rectangle.
        Assert.Equal(
            Fill([.. border.Select(rectangle => new RectangleD(rectangle.X + 0.5, rectangle.Y, rectangle.Width, rectangle.Height))]),
            Render(PixelFormat.Gray8, 40, 24, canvas => canvas.FillText(White, "A", font, 16, new PointD(0.5, 0))));

        // The pixels are the outline of the glyph: the path of the layout, its bounds, and what a stroke follows.
        var layout = TextLayout.Create("A", font, 16);
        Assert.Equal(new RectangleD(2, 1, 12, 12), layout.InkBounds);
        Assert.Equal(Fill(border), Render(PixelFormat.Gray8, 40, 24, canvas => canvas.Fill(White, layout.ToPath(new PointD(0, 0)))));
        Assert.Equal(
            Render(PixelFormat.Gray8, 40, 24, canvas => canvas.Stroke(new Pen(White, 1), layout.ToPath(new PointD(4, 4)))),
            Render(PixelFormat.Gray8, 40, 24, canvas => canvas.StrokeText(new Pen(White, 1), layout, new PointD(4, 4))));

        static int[] Fill(params RectangleD[] rectangles)
        {
            return Render(PixelFormat.Gray8, 40, 24, canvas =>
            {
                foreach (var rectangle in rectangles)
                {
                    canvas.FillRectangle(White, rectangle);
                }
            });
        }
    }

    [Fact]
    public void ABitmapThatCannotBeUsedLeavesTheOutline()
    {
        var options = new FontLoadOptions { UseEmbeddedBitmaps = true };
        var outline = Render(PixelFormat.Gray8, 40, 24, canvas => canvas.FillRectangle(White, new RectangleD(2, 1, 12, 12)));

        // Strikes of several bits per pixel, a location table of a version that is not known, and a font without data table.
        var font = FontTestSupport.Load(FontSamples.MonochromeBoxes(bitDepth: 8), options);
        Assert.Equal(outline, Render(PixelFormat.Gray8, 40, 24, canvas => canvas.FillText(White, "A", font, 16, new PointD(0, 0))));
        var tables = FontSamples.MonochromeBoxes().BuildTables();
        var locations = (byte[])tables["EBLC"].Clone();
        locations[1] = 9;
        font = Font.Load(FontBuilder.WriteSfnt(new SortedDictionary<string, byte[]>(tables, StringComparer.Ordinal) { ["EBLC"] = locations }), options);
        Assert.Equal(outline, Render(PixelFormat.Gray8, 40, 24, canvas => canvas.FillText(White, "A", font, 16, new PointD(0, 0))));
        var withoutData = new SortedDictionary<string, byte[]>(tables, StringComparer.Ordinal);
        withoutData.Remove("EBDT");
        font = Font.Load(FontBuilder.WriteSfnt(withoutData), options);
        Assert.Equal(outline, Render(PixelFormat.Gray8, 40, 24, canvas => canvas.FillText(White, "A", font, 16, new PointD(0, 0))));

        // A data table that is cut: an error when a text is laid out at the size of the strike, not at another size.
        var truncated = Font.Load(FontBuilder.WriteSfnt(new SortedDictionary<string, byte[]>(tables, StringComparer.Ordinal) { ["EBDT"] = tables["EBDT"][..^8] }), options);
        Assert.Throws<InvalidImageContentException>(() => TextLayout.Create("AB", truncated, 16));
        Assert.Equal(23.375, TextLayout.Create("AB", truncated, 17).Width);

        // The work of a glyph is bounded: a checkerboard is a rectangle per pixel.
        var limited = FontTestSupport.Load(FontSamples.MonochromeBoxes(), new FontLoadOptions { UseEmbeddedBitmaps = true, Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxGlyphComplexity = 20 } } });
        Assert.Throws<ImageResourceLimitException>(() => TextLayout.Create("B", limited, 16));
    }

    [Fact]
    public void ASyntheticObliqueSlantsTheGlyphsAroundTheBaseline()
    {
        // A is the square from 2 to 14 pixels of its origin, 12 pixels up from the baseline, which is at 13: each point
        // moves right by a quarter of its height, so the top of the square moves by 3 pixels.
        var oblique = new TextStyle(Boxes, 16) { Synthesis = FontSynthesis.Oblique };
        var layout = TextLayout.Create("A", oblique);
        var slanted = new VectorPathBuilder().MoveTo(2, 13).LineTo(5, 1).LineTo(17, 1).LineTo(14, 13).Close().Build();
        var expected = Render(PixelFormat.Gray8, 80, 40, canvas => canvas.Fill(White, slanted));
        Assert.Equal(expected, Render(PixelFormat.Gray8, 80, 40, canvas => canvas.FillText(White, layout, new PointD(0, 0))));

        // The advance does not change, and the bounds are those of the slanted glyph.
        Assert.Equal(16, layout.Width);
        Assert.Equal(new RectangleD(2, 1, 15, 12), layout.InkBounds);

        // The path of the layout and a stroke follow the slanted outline.
        Assert.Equal(expected, Render(PixelFormat.Gray8, 80, 40, canvas => canvas.Fill(White, layout.ToPath(new PointD(0, 0)))));
        var pen = new Pen(White, 1);
        Assert.Equal(
            Render(PixelFormat.Gray8, 80, 40, canvas => canvas.Stroke(pen, slanted)),
            Render(PixelFormat.Gray8, 80, 40, canvas => canvas.StrokeText(pen, layout, new PointD(0, 0))));

        // Along a straight path from left to right, the text is the text drawn at a point: slanted, then placed.
        var path = new VectorPathBuilder().MoveTo(10, 23).LineTo(70, 23).Build();
        Assert.Equal(
            Render(PixelFormat.Gray8, 80, 40, canvas => canvas.FillText(White, layout, new PointD(10, 10))),
            Render(PixelFormat.Gray8, 80, 40, canvas => canvas.FillText(White, layout, path)));

        // A glyph that is placed above the baseline moves with the slant: C is a box from the descent to the ascent,
        // and its part under the baseline moves left.
        var descending = new VectorPathBuilder().MoveTo(-0.75, 16).LineTo(3.25, 0).LineTo(15.25, 0).LineTo(11.25, 16).Close().Build();
        Assert.Equal(
            Render(PixelFormat.Gray8, 80, 40, canvas => canvas.Fill(White, descending)),
            Render(PixelFormat.Gray8, 80, 40, canvas => canvas.FillText(White, TextLayout.Create("C", oblique), new PointD(0, 0))));
    }

    [Fact]
    public void ASyntheticBoldThickensTheGlyphsOfAFilledText()
    {
        // At 48 pixels per em the bold is 2 pixels wide: every edge moves out by 1 pixel, and a glyph takes 2 pixels
        // more, in the middle of which it is drawn. A is the square from 6 to 42 pixels of its origin, 36 pixels up from
        // the baseline, which is at 39: thickened, from 5 to 43 and from -1 to 37, drawn 1 pixel further.
        var bold = new TextStyle(Boxes, 48) { Synthesis = FontSynthesis.Bold };
        var layout = TextLayout.Create("A", bold);
        Assert.Equal(Fill(new RectangleD(6, 2, 38, 38)), Render(PixelFormat.Gray8, 120, 60, canvas => canvas.FillText(White, layout, new PointD(0, 0))));
        Assert.Equal(50, layout.Width);

        // A glyph whose outline starts on the left of the image is thickened like any other, and so is what follows it.
        Assert.Equal(Fill(new RectangleD(-4, 2, 38, 38)), Render(PixelFormat.Gray8, 120, 60, canvas => canvas.FillText(White, layout, new PointD(-10, 0))));
        Assert.Equal(Fill(new RectangleD(-44, 2, 38, 38), new RectangleD(6, 2, 38, 38)), Render(PixelFormat.Gray8, 120, 60, canvas => canvas.FillText(White, TextLayout.Create("AA", bold), new PointD(-50, 0))));

        // The bounds contain the tip of the sharpest corner the stroke keeps, which is one width away from the outline.
        Assert.Equal(new RectangleD(5, 1, 40, 40), layout.InkBounds);

        // Each glyph takes more room: two letters, the second one 50 pixels after the first one.
        Assert.Equal(100, TextLayout.Create("AA", bold).Width);
        Assert.Equal(Fill(new RectangleD(6, 2, 38, 38), new RectangleD(56, 2, 38, 38)), Render(PixelFormat.Gray8, 120, 60, canvas => canvas.FillText(White, TextLayout.Create("AA", bold), new PointD(0, 0))));
        Assert.Equal(2, TextLayout.Create("AA", bold, new TextLayoutOptions { MaxWidth = 99, OverflowWrap = TextOverflowWrap.Anywhere }).LineCount);
        Assert.Equal(1, TextLayout.Create("AA", new TextStyle(Boxes, 48), new TextLayoutOptions { MaxWidth = 99, OverflowWrap = TextOverflowWrap.Anywhere }).LineCount);

        // A mark stays on its letter: the acute accent of a font without the composed character, a box from 30 to 18
        // pixels before the end of the letter and from 39 to 45 pixels above the baseline, moves and grows with it.
        var builder = FontSamples.Boxes();
        builder.CharacterMap.Remove(0x00C1);
        var withMark = TextLayout.Create("A\u0301", new TextStyle(FontTestSupport.Load(builder), 48) { Synthesis = FontSynthesis.Bold });
        Assert.Equal(50, withMark.Width);
        Assert.Equal(Fill(new RectangleD(6, 12, 38, 38), new RectangleD(18, 3, 14, 8)), Render(PixelFormat.Gray8, 120, 60, canvas => canvas.FillText(White, withMark, new PointD(0, 10))));

        // The bold is a way to fill: the path of the layout and a stroke have the outlines of the font, where the
        // glyphs are placed.
        Assert.Equal(Fill(new RectangleD(7, 3, 36, 36)), Render(PixelFormat.Gray8, 120, 60, canvas => canvas.Fill(White, layout.ToPath(new PointD(0, 0)))));
        var pen = new Pen(White, 1);
        Assert.Equal(
            Render(PixelFormat.Gray8, 120, 60, canvas => canvas.StrokeRectangle(pen, new RectangleD(7, 3, 36, 36))),
            Render(PixelFormat.Gray8, 120, 60, canvas => canvas.StrokeText(pen, layout, new PointD(0, 0))));

        // Along a straight path, the thickened text is the one drawn at a point.
        var path = new VectorPathBuilder().MoveTo(10, 49).LineTo(110, 49).Build();
        Assert.Equal(
            Render(PixelFormat.Gray8, 120, 60, canvas => canvas.FillText(White, layout, new PointD(10, 10))),
            Render(PixelFormat.Gray8, 120, 60, canvas => canvas.FillText(White, layout, path)));

        // Both at once: the slanted glyph is thickened. In the row of pixels 18.5 pixels above the baseline, the sides
        // of the slanted square are at 11.625 and 47.625, and the stroke adds about one pixel to each.
        var both = TextLayout.Create("A", new TextStyle(Boxes, 48) { Synthesis = FontSynthesis.Bold | FontSynthesis.Oblique });
        var pixels = Render(PixelFormat.Gray8, 120, 60, canvas => canvas.FillText(White, both, new PointD(0, 0)));
        Assert.Equal(50, both.Width);
        Assert.Equal((0, 255, 255, 0), (pixels[(20 * 120) + 9], pixels[(20 * 120) + 11], pixels[(20 * 120) + 47], pixels[(20 * 120) + 49]));
        Assert.True(pixels[(20 * 120) + 10] is > 0 and < 255 && pixels[(20 * 120) + 48] is > 0 and < 255);

        // A text of two styles: each part has its synthesis, and they are not shaped together (no kerning between them).
        var text = new RichTextBuilder().Append("A", bold).Append("B", new TextStyle(Boxes, 48)).Build();
        Assert.Equal(50 + 24, TextLayout.Create(text).Width);
        Assert.Equal(48 + 24 - 6, TextLayout.Create("AB", new TextStyle(Boxes, 48)).Width);

        Assert.Throws<ArgumentOutOfRangeException>(() => new TextStyle(Boxes, 16) { Synthesis = (FontSynthesis)4 });
        Assert.Equal(FontSynthesis.None, new TextStyle(Boxes, 16).Synthesis);

        static int[] Fill(params RectangleD[] rectangles)
        {
            return Render(PixelFormat.Gray8, 120, 60, canvas =>
            {
                foreach (var rectangle in rectangles)
                {
                    canvas.FillRectangle(White, rectangle);
                }
            });
        }
    }

    [Fact]
    public void ArgumentsAreValidated()
    {
        var layout = TextLayout.Create("A", Boxes, 16);
        var pen = new Pen(White, 1);
        Assert.Throws<ArgumentNullException>("text", () => TextLayout.Create(null!, Boxes, 16));
        Assert.Throws<ArgumentNullException>("font", () => TextLayout.Create("A", null!, 16));
        foreach (var size in (double[])[0, -1, double.NaN, double.PositiveInfinity, 65536.5])
        {
            Assert.Throws<ArgumentOutOfRangeException>("size", () => TextLayout.Create("A", Boxes, size));
        }

        Assert.Equal(65536, TextLayout.Create("A", Boxes, 65536).Height);
        Assert.Throws<ArgumentException>("origin", () => layout.ToPath(new PointD(double.NaN, 0)));
        Assert.Throws<ArgumentOutOfRangeException>("origin", () => layout.ToPath(new PointD(0, 1e9)));

        using var image = new Image<Gray8>(4, 4, new Gray8(0));
        image.Draw(
            canvas =>
            {
                try
                {
                    canvas.FillText(null!, layout, new PointD(0, 0));
                    Assert.Fail("A null brush was accepted");
                }
                catch (ArgumentNullException exception)
                {
                    Assert.Equal("brush", exception.ParamName);
                }

                try
                {
                    canvas.FillText(White, (TextLayout)null!, new PointD(0, 0));
                    Assert.Fail("A null layout was accepted");
                }
                catch (ArgumentNullException exception)
                {
                    Assert.Equal("layout", exception.ParamName);
                }

                try
                {
                    canvas.StrokeText(null!, layout, new PointD(0, 0));
                    Assert.Fail("A null pen was accepted");
                }
                catch (ArgumentNullException exception)
                {
                    Assert.Equal("pen", exception.ParamName);
                }

                try
                {
                    canvas.StrokeText(pen, null!, new PointD(0, 0));
                    Assert.Fail("A null layout was accepted");
                }
                catch (ArgumentNullException exception)
                {
                    Assert.Equal("layout", exception.ParamName);
                }

                try
                {
                    canvas.FillText(White, layout, new PointD(0, double.PositiveInfinity));
                    Assert.Fail("An infinite origin was accepted");
                }
                catch (ArgumentException exception)
                {
                    Assert.Equal("origin", exception.ParamName);
                }

                try
                {
                    canvas.FillText(null!, "A", Boxes, 16, new PointD(0, 0));
                    Assert.Fail("A null brush was accepted");
                }
                catch (ArgumentNullException exception)
                {
                    Assert.Equal("brush", exception.ParamName);
                }

                try
                {
                    canvas.FillText(White, null!, Boxes, 16, new PointD(0, 0));
                    Assert.Fail("A null text was accepted");
                }
                catch (ArgumentNullException exception)
                {
                    Assert.Equal("text", exception.ParamName);
                }

                try
                {
                    canvas.FillText(White, "A", Boxes, 0, new PointD(0, 0));
                    Assert.Fail("A size of zero was accepted");
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    Assert.Equal("size", exception.ParamName);
                }
            },
            Ct);
        Assert.Equal(new int[16], DrawingTestSupport.ReadSamples(image.Frames[0]));
    }

    [Fact]
    public void ALayoutCanBeUsedByManyThreads()
    {
        var layout = TextLayout.Create("ABCDEF\u00C1H", Boxes, 20.5);
        using var reference = new Image<Rgba32>(160, 32);
        reference.Draw(canvas => canvas.FillText(Translucent, layout, new PointD(1.5, 2.25)), Ct);
        var expected = DrawingTestSupport.ReadSamples(reference.Frames[0]);

        Parallel.For(0, 32, _ =>
        {
            using var image = new Image<Rgba32>(160, 32);
            image.Draw(canvas => canvas.FillText(Translucent, layout, new PointD(1.5, 2.25)));
            if (!expected.AsSpan().SequenceEqual(DrawingTestSupport.ReadSamples(image.Frames[0])))
                throw new InvalidOperationException("A layout was drawn differently by another thread.");
        });
    }

    [Fact]
    public void ATextFollowsAStraightPathAsItIsDrawnAtAPoint()
    {
        // A horizontal path from (10, 30): the baseline is on the path, 13 pixels under the top of the layout.
        var layout = TextLayout.Create("AB C", Boxes, 16);
        var path = new VectorPathBuilder().MoveTo(10, 30).LineTo(110, 30).Build();
        var expected = Render(PixelFormat.Rgba32, 120, 60, canvas => canvas.FillText(Translucent, layout, new PointD(10, 17)));
        Assert.Equal(expected, Render(PixelFormat.Rgba32, 120, 60, canvas => canvas.FillText(Translucent, layout, path)));

        var pen = new Pen(White, 1.5);
        expected = Render(PixelFormat.Gray8, 120, 60, canvas => canvas.StrokeText(pen, layout, new PointD(10, 17)));
        Assert.Equal(expected, Render(PixelFormat.Gray8, 120, 60, canvas => canvas.StrokeText(pen, layout, path, TextPathOptions.Default)));

        // The lines of a layout are stacked across the path, and a decoration follows it.
        var rich = TextLayout.Create("A\nB", new TextStyle(Boxes, 16) { Decorations = TextDecorations.Underline });
        expected = Render(PixelFormat.Gray8, 120, 60, canvas => canvas.FillText(White, rich, new PointD(10, 17)));
        Assert.Equal(expected, Render(PixelFormat.Gray8, 120, 60, canvas => canvas.FillText(White, rich, path)));
    }

    [Fact]
    public void ATextIsTurnedToTheDirectionOfItsPath()
    {
        // A is 12 by 12 pixels, 2 pixels after the pen and on the baseline; it advances by 16 pixels.
        var layout = TextLayout.Create("A", Boxes, 16);

        // Downward: the top of the text is on the right. The letter goes from 2 to 14 along the path, and from the path
        // to 12 pixels on its left, which is the right of the image.
        var down = new VectorPathBuilder().MoveTo(50, 5).LineTo(50, 100).Build();
        AssertPathRectangles(layout, down, null, new RectangleD(50, 7, 12, 12));

        // From right to left: the text is upside down, under the path.
        var back = new VectorPathBuilder().MoveTo(100, 30).LineTo(0, 30).Build();
        AssertPathRectangles(layout, back, null, new RectangleD(86, 30, 12, 12));

        // Upward: the top of the text is on the left.
        var up = new VectorPathBuilder().MoveTo(50, 100).LineTo(50, 5).Build();
        AssertPathRectangles(layout, up, null, new RectangleD(38, 86, 12, 12));

        // The other side of a path is the path followed from its end.
        var forward = new VectorPathBuilder().MoveTo(0, 30).LineTo(100, 30).Build();
        AssertPathRectangles(layout, forward, new TextPathOptions { Side = TextPathSide.Right }, new RectangleD(86, 30, 12, 12));
        AssertPathRectangles(layout, down, new TextPathOptions { Side = TextPathSide.Right }, new RectangleD(38, 86, 12, 12));
    }

    [Fact]
    public void ATextIsPlacedAlongItsPathByTheOptions()
    {
        // "AA" is 32 pixels on a path of 100 pixels at y = 30: the letters are drawn from 18 to 30.
        var layout = TextLayout.Create("AA", Boxes, 16);
        var path = new VectorPathBuilder().MoveTo(0, 30).LineTo(100, 30).Build();
        AssertPathRectangles(layout, path, null, new RectangleD(2, 18, 12, 12), new RectangleD(18, 18, 12, 12));
        AssertPathRectangles(layout, path, new TextPathOptions { StartOffset = 10 }, new RectangleD(12, 18, 12, 12), new RectangleD(28, 18, 12, 12));
        AssertPathRectangles(layout, path, new TextPathOptions { Alignment = TextPathAlignment.Center }, new RectangleD(36, 18, 12, 12), new RectangleD(52, 18, 12, 12));
        AssertPathRectangles(layout, path, new TextPathOptions { Alignment = TextPathAlignment.End }, new RectangleD(70, 18, 12, 12), new RectangleD(86, 18, 12, 12));
        AssertPathRectangles(layout, path, new TextPathOptions { Alignment = TextPathAlignment.End, StartOffset = -8 }, new RectangleD(62, 18, 12, 12), new RectangleD(78, 18, 12, 12));

        // Away from the path, on the side of the text, or across it.
        AssertPathRectangles(layout, path, new TextPathOptions { BaselineOffset = 5 }, new RectangleD(2, 13, 12, 12), new RectangleD(18, 13, 12, 12));
        AssertPathRectangles(layout, path, new TextPathOptions { BaselineOffset = -4 }, new RectangleD(2, 22, 12, 12), new RectangleD(18, 22, 12, 12));
        AssertPathRectangles(layout, path, new TextPathOptions { BaselineOffset = 5, Side = TextPathSide.Right }, new RectangleD(86, 35, 12, 12), new RectangleD(70, 35, 12, 12));

        // Beyond the ends of the path, the text continues straight, or is not drawn: a letter is drawn when the middle of
        // its advance is on the path. The path is 20 pixels long; the middles are at 8 and 24.
        var small = new VectorPathBuilder().MoveTo(40, 30).LineTo(60, 30).Build();
        AssertPathRectangles(layout, small, null, new RectangleD(42, 18, 12, 12), new RectangleD(58, 18, 12, 12));
        AssertPathRectangles(layout, small, new TextPathOptions { Overflow = TextPathOverflow.Clip }, new RectangleD(42, 18, 12, 12));
        AssertPathRectangles(layout, small, new TextPathOptions { StartOffset = -10 }, new RectangleD(32, 18, 12, 12), new RectangleD(48, 18, 12, 12));
        AssertPathRectangles(layout, small, new TextPathOptions { StartOffset = -10, Overflow = TextPathOverflow.Clip }, new RectangleD(48, 18, 12, 12));
        AssertPathRectangles(layout, small, new TextPathOptions { StartOffset = 30, Overflow = TextPathOverflow.Clip });
    }

    [Fact]
    public void ATextTurnsWithItsPath()
    {
        // A path that goes right for 20 pixels, then down. The middle of the first A is at 8, on the first segment; the
        // middle of the second one is at 24: 4 pixels down the second segment, where the letter is turned.
        var layout = TextLayout.Create("AA", Boxes, 16);
        var corner = new VectorPathBuilder().MoveTo(10, 40).LineTo(30, 40).LineTo(30, 110).Build();
        AssertPathRectangles(layout, corner, null, new RectangleD(12, 28, 12, 12), new RectangleD(30, 38, 12, 12));

        // The contours of a path follow each other: the second letter is 4 pixels along the second contour.
        var contours = new VectorPathBuilder().MoveTo(10, 40).LineTo(30, 40).MoveTo(60, 20).LineTo(60, 90).Build();
        AssertPathRectangles(layout, contours, null, new RectangleD(12, 28, 12, 12), new RectangleD(60, 18, 12, 12));

        // A closed contour has no end: a square of 16 pixels, clockwise from its top-left corner. The middles of four A
        // are at 8, 24, 40 and 56 of its 64 pixels: one letter per side; a fifth one is where the first one is.
        var square = new VectorPathBuilder().MoveTo(40, 40).LineTo(56, 40).LineTo(56, 56).LineTo(40, 56).Close().Build();
        RectangleD[] around = [new RectangleD(42, 28, 12, 12), new RectangleD(56, 42, 12, 12), new RectangleD(42, 56, 12, 12), new RectangleD(28, 42, 12, 12)];
        AssertPathRectangles(TextLayout.Create("AAAA", Boxes, 16), square, null, around);
        AssertPathRectangles(TextLayout.Create("AAAAA", Boxes, 16), square, new TextPathOptions { Overflow = TextPathOverflow.Clip }, around);
        AssertPathRectangles(TextLayout.Create("A", Boxes, 16), square, new TextPathOptions { StartOffset = -48 }, around[1]);
        AssertPathRectangles(TextLayout.Create("A", Boxes, 16), square, new TextPathOptions { Side = TextPathSide.Right }, new RectangleD(40, 42, 12, 12));

        // A decoration is cut at the corner: 1 pixel thick, 2 pixels on the right of the direction of the path, from the
        // start of the text to its end, 32 pixels further.
        var underlined = TextLayout.Create("AA", new TextStyle(Boxes, 16) { Decorations = TextDecorations.Underline });
        AssertPathRectangles(underlined, corner, null, new RectangleD(12, 28, 12, 12), new RectangleD(30, 38, 12, 12), new RectangleD(10, 42, 20, 1), new RectangleD(27, 40, 1, 12));
    }

    [Fact]
    public void ADecorationGoesAroundAClosedPath()
    {
        // The square of 16 pixels of four letters: the underline is 1 pixel thick, 2 pixels on the right of the direction
        // of the path, which is inside the square. A text longer than the path covers its first letter again.
        var style = new TextStyle(Boxes, 16) { Decorations = TextDecorations.Underline };
        var square = new VectorPathBuilder().MoveTo(40, 40).LineTo(56, 40).LineTo(56, 56).LineTo(40, 56).Close().Build();
        RectangleD[] expected =
        [
            new RectangleD(42, 28, 12, 12), new RectangleD(56, 42, 12, 12), new RectangleD(42, 56, 12, 12), new RectangleD(28, 42, 12, 12),
            new RectangleD(40, 42, 16, 1), new RectangleD(53, 40, 1, 16), new RectangleD(40, 53, 16, 1), new RectangleD(42, 40, 1, 16),
        ];
        AssertPathRectangles(TextLayout.Create("AAAA", style), square, null, expected);
        AssertPathRectangles(TextLayout.Create("AAAAA", style), square, null, expected);
        AssertPathRectangles(TextLayout.Create("AAAAAAAAAAAAA", style), square, new TextPathOptions { StartOffset = -192 }, expected);

        // A part of a turn: one letter that starts 8 pixels before the first corner, where its middle is: it is turned to
        // the direction of the path after the corner, and its underline is on both sides.
        AssertPathRectangles(TextLayout.Create("A", style), square, new TextPathOptions { StartOffset = 8 }, new RectangleD(56, 34, 12, 12), new RectangleD(48, 42, 8, 1), new RectangleD(53, 40, 1, 8));

        // On the other side, the path is followed from its end: the letters are inside the square, where they overlap,
        // and the underline is outside.
        AssertPathRectangles(
            TextLayout.Create("AAAA", style),
            square,
            new TextPathOptions { Side = TextPathSide.Right },
            new RectangleD(40, 42, 12, 12),
            new RectangleD(42, 44, 12, 12),
            new RectangleD(44, 42, 12, 12),
            new RectangleD(42, 40, 12, 12),
            new RectangleD(37, 40, 1, 16),
            new RectangleD(40, 58, 16, 1),
            new RectangleD(58, 40, 1, 16),
            new RectangleD(40, 37, 16, 1));

        // An open path: the underline continues straight beyond its ends, or stops there.
        var open = new VectorPathBuilder().MoveTo(40, 30).LineTo(60, 30).Build();
        var layout = TextLayout.Create("AA", style);
        AssertPathRectangles(layout, open, new TextPathOptions { StartOffset = -8 }, new RectangleD(34, 18, 12, 12), new RectangleD(50, 18, 12, 12), new RectangleD(32, 32, 32, 1));
        AssertPathRectangles(layout, open, new TextPathOptions { StartOffset = -8, Overflow = TextPathOverflow.Clip }, new RectangleD(34, 18, 12, 12), new RectangleD(50, 18, 12, 12), new RectangleD(40, 32, 20, 1));
        AssertPathRectangles(layout, open, new TextPathOptions { StartOffset = -8, Side = TextPathSide.Right }, new RectangleD(54, 30, 12, 12), new RectangleD(38, 30, 12, 12), new RectangleD(36, 27, 32, 1));
        AssertPathRectangles(layout, open, new TextPathOptions { StartOffset = -8, Side = TextPathSide.Right, Overflow = TextPathOverflow.Clip }, new RectangleD(54, 30, 12, 12), new RectangleD(38, 30, 12, 12), new RectangleD(40, 27, 20, 1));
    }

    [Fact]
    public void APathIsMeasuredAlongItsChords()
    {
        using var measure = new PathMeasure(new AllocationScope(ImageResourceLimits.Default));
        Assert.False(measure.TryGetPoint(0, out _, out _, out _, out _));
        Assert.Equal(0, measure.Length);

        // A 3-4-5 triangle left open, then another contour: the distance continues from one to the other.
        var path = new VectorPathBuilder().MoveTo(0, 0).LineTo(3, 4).LineTo(3, 4).LineTo(3, 14).MoveTo(100, 100).LineTo(90, 100).Build();
        PathFlattener.Flatten(path, measure);
        Assert.Equal(25, measure.Length);
        Assert.False(measure.IsClosed);
        AssertPoint(measure, 0, 0, 0, 0.6, 0.8);
        AssertPoint(measure, 2.5, 1.5, 2, 0.6, 0.8);
        AssertPoint(measure, 5, 3, 4, 0, 1);
        AssertPoint(measure, 9, 3, 8, 0, 1);
        AssertPoint(measure, 15, 100, 100, -1, 0);
        AssertPoint(measure, 25, 90, 100, -1, 0);

        // Beyond the ends, the first and the last chords continue.
        AssertPoint(measure, -5, -3, -4, 0.6, 0.8);
        AssertPoint(measure, 30, 85, 100, -1, 0);

        Assert.Equal(5, measure.GetChordEnd(0));
        Assert.Equal(15, measure.GetChordEnd(5));
        Assert.Equal(25, measure.GetChordEnd(20));
        Assert.Equal(double.PositiveInfinity, measure.GetChordEnd(25));
        Assert.Equal(0, measure.GetChordEnd(-1));
        Assert.Equal(0, measure.GetChordStartBefore(5));
        Assert.Equal(5, measure.GetChordStartBefore(5.5));
        Assert.Equal(15, measure.GetChordStartBefore(25));
        Assert.Equal(25, measure.GetChordStartBefore(26));
        Assert.Equal(double.NegativeInfinity, measure.GetChordStartBefore(0));

        // A closed contour includes its closing segment; it is the only one of its path.
        measure.Reset();
        PathFlattener.Flatten(new VectorPathBuilder().MoveTo(0, 0).LineTo(3, 0).LineTo(3, 4).Close().Build(), measure);
        Assert.Equal(12, measure.Length);
        Assert.True(measure.IsClosed);
        AssertPoint(measure, 9.5, 1.5, 2, -0.6, -0.8);

        measure.Reset();
        PathFlattener.Flatten(new VectorPathBuilder().MoveTo(0, 0).LineTo(3, 0).LineTo(3, 4).Close().MoveTo(10, 10).LineTo(20, 10).Build(), measure);
        Assert.Equal(22, measure.Length);
        Assert.False(measure.IsClosed);

        // A curve is the chords of its flattening: a circle of radius 100 is 2 pi r long, within the tolerance of the
        // flattening, and its direction is the tangent of the circle.
        measure.Reset();
        PathFlattener.Flatten(new VectorPathBuilder().AddCircle(new PointD(0, 0), 100).Build(), measure);
        Assert.True(measure.IsClosed);
        Assert.Equal(2 * Math.PI * 100, measure.Length, 0.5);
        for (var i = 0; i < 64; i++)
        {
            Assert.True(measure.TryGetPoint(measure.Length * i / 64, out var x, out var y, out var directionX, out var directionY));
            Assert.Equal(100, Math.Sqrt((x * x) + (y * y)), 0.1);
            Assert.Equal(1, (directionX * directionX) + (directionY * directionY), 1e-12);
            Assert.Equal(0, ((x * directionX) + (y * directionY)) / 100, 0.05);
        }

        static void AssertPoint(PathMeasure measure, double distance, double expectedX, double expectedY, double expectedDirectionX, double expectedDirectionY)
        {
            Assert.True(measure.TryGetPoint(distance, out var x, out var y, out var directionX, out var directionY));
            Assert.Equal(expectedX, x, 1e-12);
            Assert.Equal(expectedY, y, 1e-12);
            Assert.Equal(expectedDirectionX, directionX, 1e-12);
            Assert.Equal(expectedDirectionY, directionY, 1e-12);
        }
    }

    [Fact]
    public void ATextOnASlantedPathIsTheRotatedText()
    {
        // A path along the vector (3, 4): its direction is (0.6, 0.8), without any trigonometric function. The first A
        // has the middle of its advance 8 pixels from the start, at (14.8, 16.4); its corners are 6 pixels on each side
        // along the path, and 0 to 12 pixels on its left, which is the direction (0.8, -0.6).
        var layout = TextLayout.Create("A", Boxes, 16);
        var path = new VectorPathBuilder().MoveTo(10, 10).LineTo(70, 90).Build();
        var polygon = new VectorPathBuilder()
            .AddPolygon([new PointD(11.2, 11.6), new PointD(18.4, 21.2), new PointD(28, 14), new PointD(20.8, 4.4)])
            .Build();

        var expected = Render(PixelFormat.Gray16, 60, 60, canvas => canvas.Fill(White, polygon));
        var actual = Render(PixelFormat.Gray16, 60, 60, canvas => canvas.FillText(White, layout, path));
        Assert.Contains(actual, sample => sample is > 20000 and < 45000);
        for (var i = 0; i < expected.Length; i++)
        {
            // The same shape, to the rounding of its corners: a few steps out of 65535.
            if (Math.Abs(expected[i] - actual[i]) > 8)
            {
                Assert.Fail($"The pixel {i % 60},{i / 60} is {actual[i]}, expected {expected[i]}");
            }
        }
    }

    [Fact]
    public void ATextFollowsACurveAndAPathWithoutSegmentDrawsNothing()
    {
        // Around a circle: every letter is drawn, turned, outside the circle; the image has the symmetry of the text.
        var layout = TextLayout.Create("AAAAAAAA", Boxes, 16);
        var circle = new VectorPathBuilder().AddCircle(new PointD(60, 60), 128 / (2 * Math.PI)).Build();
        var samples = Render(PixelFormat.Gray8, 120, 120, canvas => canvas.FillText(White, layout, circle));
        Assert.True(samples.Count(sample => sample == 255) > 8 * 100, "The letters around the circle are not drawn");
        Assert.Equal(0, samples[(60 * 120) + 60]);

        // The area of eight squares of 12 pixels, whatever their rotation.
        Assert.Equal(8 * 144, samples.Sum(sample => (long)sample) / 255.0, 2.0);

        // Nothing to follow: a path without segment, an empty path.
        Assert.Equal(new int[120 * 120], Render(PixelFormat.Gray8, 120, 120, canvas => canvas.FillText(White, layout, new VectorPathBuilder().Build())));
        Assert.Equal(new int[120 * 120], Render(PixelFormat.Gray8, 120, 120, canvas => canvas.FillText(White, layout, new VectorPathBuilder().MoveTo(5, 5).Build())));
        Assert.Equal(new int[120 * 120], Render(PixelFormat.Gray8, 120, 120, canvas => canvas.StrokeText(new Pen(White, 1), layout, new VectorPathBuilder().MoveTo(5, 5).LineTo(5, 5).Build())));

        var path = new VectorPathBuilder().MoveTo(0, 0).LineTo(10, 0).Build();
        using var image = new Image<Gray8>(4, 4, new Gray8(0));
        image.Draw(
            canvas =>
            {
                try
                {
                    canvas.FillText(White, layout, (VectorPath)null!);
                    Assert.Fail("A null path was accepted");
                }
                catch (ArgumentNullException exception)
                {
                    Assert.Equal("path", exception.ParamName);
                }

                try
                {
                    canvas.StrokeText(new Pen(White, 1), null!, path);
                    Assert.Fail("A null layout was accepted");
                }
                catch (ArgumentNullException exception)
                {
                    Assert.Equal("layout", exception.ParamName);
                }

                try
                {
                    canvas.FillText(null!, layout, path);
                    Assert.Fail("A null brush was accepted");
                }
                catch (ArgumentNullException exception)
                {
                    Assert.Equal("brush", exception.ParamName);
                }
            },
            Ct);

        Assert.Throws<ArgumentOutOfRangeException>(() => new TextPathOptions { StartOffset = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextPathOptions { BaselineOffset = double.PositiveInfinity });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextPathOptions { Alignment = (TextPathAlignment)3 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextPathOptions { Side = (TextPathSide)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextPathOptions { Overflow = (TextPathOverflow)2 });
        Assert.Equal((0, TextPathAlignment.Start, 0, TextPathSide.Left, TextPathOverflow.Extend), (TextPathOptions.Default.StartOffset, TextPathOptions.Default.Alignment, TextPathOptions.Default.BaselineOffset, TextPathOptions.Default.Side, TextPathOptions.Default.Overflow));
    }

    /// <summary>Asserts that a layout drawn along a path paints exactly the given rectangles of whole pixels.</summary>
    private static void AssertPathRectangles(TextLayout layout, VectorPath path, TextPathOptions? options, params RectangleD[] rectangles)
    {
        var expected = Render(PixelFormat.Gray8, 130, 130, canvas =>
        {
            foreach (var rectangle in rectangles)
            {
                canvas.FillRectangle(White, rectangle);
            }
        });
        Assert.Equal(expected, Render(PixelFormat.Gray8, 130, 130, canvas => canvas.FillText(White, layout, path, options)));
    }

    [Fact]
    public void AGlyphThatATransformationSendsBeyondTheRangeOfTheCoordinatesIsNotSeen()
    {
        // A glyph far beyond the canvas, once transformed beyond the range of the coordinates, is cut by it: the run
        // draws what it draws without that glyph, whether its glyphs are masks (axes kept) or outlines (turned)
        foreach (var (transform, origin) in new[] { (AffineTransform.CreateScale(32, 1), new PointD(-1, 15)), (AffineTransform.CreateRotation(30, new PointD(10, 10)).Then(AffineTransform.CreateScale(2, 2)).Then(AffineTransform.CreateTranslation(-10, -10)), new PointD(3, 15)) })
        {
            PositionedGlyph[] near = [new(FontSamples.BoxA, origin)];
            PositionedGlyph[] withFar = [new(FontSamples.BoxA, origin), new(FontSamples.BoxB, new PointD(16777000, 15)), new(FontSamples.BoxA, new PointD(-16777000, 15))];
            var expected = Render(PixelFormat.Rgba64, 40, 20, canvas => canvas.Transform(transform, moved => moved.FillGlyphs(White, Boxes, 16, near)));
            Assert.Contains(expected, static sample => sample != 0);
            Assert.Equal(expected, Render(PixelFormat.Rgba64, 40, 20, canvas => canvas.Transform(transform, moved => moved.FillGlyphs(White, Boxes, 16, withFar))));
        }
    }

    /// <summary>Asserts that a text of the boxes font at 16 pixels per em paints exactly the given rectangles of whole pixels.</summary>
    [Fact]
    public void AGlyphRunDrawsEachGlyphFromItsOriginOnThePixelGrid()
    {
        // A is (128, 0)-(896, 768) and B (64, 0)-(448, 512): at 16 pixels per em, from 2 and 1 pixels right of the
        // origin, 12 and 8 pixels up from it
        PositionedGlyph[] run = [new(FontSamples.BoxA, new PointD(3, 15)), new(FontSamples.BoxB, new PointD(20, 15))];
        foreach (var format in (PixelFormat[])[PixelFormat.Gray8, PixelFormat.Rgba64])
        {
            var rectangles = Render(format, 40, 20, canvas =>
            {
                canvas.FillRectangle(White, new RectangleD(5, 3, 12, 12));
                canvas.FillRectangle(White, new RectangleD(21, 7, 6, 8));
            });
            Assert.Equal(rectangles, Render(format, 40, 20, canvas => canvas.FillGlyphs(White, Boxes, 16, run)));
        }

        // Along the line, an origin goes to the nearest quarter of a pixel, and across it to the nearest pixel: (3.3,
        // 15.4) is (3.25, 15), as in the glyph caches of browsers
        var expected = Render(PixelFormat.Rgba64, 40, 20, canvas => canvas.FillRectangle(White, new RectangleD(5.25, 3, 12, 12)));
        Assert.Equal(expected, Render(PixelFormat.Rgba64, 40, 20, canvas => canvas.FillGlyphs(White, Boxes, 16, [new(FontSamples.BoxA, new PointD(3.3, 15.4))])));

        // The same pixels with a cache, which keeps a coverage per glyph and position in a pixel: A at 3.3 and at 20.2
        // is A a quarter of a pixel in
        PositionedGlyph[] repeated = [new(FontSamples.BoxA, new PointD(3.3, 15.4)), new(FontSamples.BoxA, new PointD(20.2, 15.1)), new(FontSamples.BoxB, new PointD(33, 15))];
        var cache = new GlyphCache(1 << 20);
        var options = new GlyphRunOptions { Cache = cache };
        var uncached = Render(PixelFormat.Rgba64, 40, 20, canvas => canvas.FillGlyphs(White, Boxes, 16, repeated));
        Assert.Equal(uncached, Render(PixelFormat.Rgba64, 40, 20, canvas => canvas.FillGlyphs(White, Boxes, 16, repeated, options)));
        Assert.Equal(2, cache.Count);
        var bytes = cache.Bytes;
        Assert.Equal(uncached, Render(PixelFormat.Rgba64, 40, 20, canvas => canvas.FillGlyphs(White, Boxes, 16, repeated, options)));
        Assert.Equal((2, bytes), (cache.Count, cache.Bytes));

        // A cache too small keeps nothing; the least recently used entries leave first
        var tiny = new GlyphCache(10);
        Assert.Equal(uncached, Render(PixelFormat.Rgba64, 40, 20, canvas => canvas.FillGlyphs(White, Boxes, 16, repeated, new GlyphRunOptions { Cache = tiny })));
        Assert.Equal(0, tiny.Count);
        var small = new GlyphCache(bytes - 1);
        Render(PixelFormat.Rgba64, 40, 20, canvas => canvas.FillGlyphs(White, Boxes, 16, repeated, new GlyphRunOptions { Cache = small }));
        Assert.Equal(1, small.Count);
        cache.Clear();
        Assert.Equal((0, 0L), (cache.Count, cache.Bytes));

        // Each glyph is drawn over the ones before it: two A at half their opacity at the same place
        var half = new SolidBrush(new Rgba32(255, 255, 255, 128));
        expected = Render(PixelFormat.Rgba64, 40, 20, canvas =>
        {
            canvas.FillRectangle(half, new RectangleD(5, 3, 12, 12));
            canvas.FillRectangle(half, new RectangleD(5, 3, 12, 12));
        });
        Assert.Equal(expected, Render(PixelFormat.Rgba64, 40, 20, canvas => canvas.FillGlyphs(half, Boxes, 16, [new(FontSamples.BoxA, new PointD(3, 15)), new(FontSamples.BoxA, new PointD(3, 15))])));

        // Through a clip, with a gradient, and on a scaled canvas, a glyph is the rectangle it is
        var gradient = new LinearGradientBrush(new PointD(0, 0), new PointD(40, 0), [new GradientStop(0, new Rgba32(255, 0, 0)), new GradientStop(1, new Rgba32(0, 0, 255))]);
        expected = Render(PixelFormat.Rgba64, 40, 20, canvas => canvas.Clip(new RectangleD(8, 0, 10.5, 20), clipped => clipped.FillRectangle(gradient, new RectangleD(5, 3, 12, 12))));
        Assert.Equal(expected, Render(PixelFormat.Rgba64, 40, 20, canvas => canvas.Clip(new RectangleD(8, 0, 10.5, 20), clipped => clipped.FillGlyphs(gradient, Boxes, 16, [new(FontSamples.BoxA, new PointD(3, 15))]))));
        expected = Render(PixelFormat.Rgba64, 40, 20, canvas => canvas.FillRectangle(White, new RectangleD(6, 2, 24, 12)));
        Assert.Equal(expected, Render(PixelFormat.Rgba64, 40, 20, canvas => canvas.Transform(AffineTransform.CreateScale(2, 1), scaled => scaled.FillGlyphs(White, Boxes, 16, [new(FontSamples.BoxA, new PointD(1, 14))]))));

        // Without antialiasing, the origin goes to the nearest pixel
        expected = Render(PixelFormat.Gray8, 40, 20, canvas => canvas.FillRectangle(White, new RectangleD(5, 3, 12, 12)));
        Assert.Equal(expected, Render(PixelFormat.Gray8, 40, 20, canvas => canvas.Antialiasing(enabled: false, hard => hard.FillGlyphs(White, Boxes, 16, [new(FontSamples.BoxA, new PointD(3.4, 15.4))]))));

        using var image = new Image<Gray8>(4, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Draw(canvas => canvas.FillGlyphs(White, Boxes, 16, [new(Boxes.GlyphCount, default)]), Ct));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Draw(canvas => canvas.FillGlyphs(White, Boxes, -1, [new(FontSamples.BoxA, default)]), Ct));
        Assert.Throws<ArgumentException>(() => image.Draw(canvas => canvas.FillGlyphs(White, Boxes, 16, [new(FontSamples.BoxA, new PointD(double.NaN, 0))]), Ct));
        Assert.Throws<ArgumentNullException>(() => image.Draw(canvas => canvas.FillGlyphs(null!, Boxes, 16, []), Ct));
        Assert.Throws<ArgumentNullException>(() => image.Draw(canvas => canvas.StrokeGlyphs(new Pen(White, 1), null!, 16, []), Ct));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlyphCache(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlyphRunOptions { Synthesis = (FontSynthesis)4 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlyphRunOptions { Palette = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlyphRunOptions { BoldWidth = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlyphRunOptions { ObliqueShear = 17 });
        Assert.Equal(new PositionedGlyph(2, new PointD(1, 2)), new PositionedGlyph(2, new PointD(1, 2)));
        Assert.NotEqual(new PositionedGlyph(2, new PointD(1, 2)), new PositionedGlyph(3, new PointD(1, 2)));
    }

    [Fact]
    public void AGlyphRunDrawsWhatATextOfItsGlyphsDraws()
    {
        // Under a transformation that turns, a glyph is a shape: the one of the text. A text at (3, 2) has its
        // baseline 13 pixels lower.
        var turn = AffineTransform.CreateRotation(25, new PointD(10, 10));
        Assert.Equal(
            Render(PixelFormat.Rgba64, 40, 30, canvas => canvas.Transform(turn, turned => turned.FillText(White, "A", Boxes, 16, new PointD(3, 2)))),
            Render(PixelFormat.Rgba64, 40, 30, canvas => canvas.Transform(turn, turned => turned.FillGlyphs(White, Boxes, 16, [new(FontSamples.BoxA, new PointD(3, 15))]))));

        // A stroke is the stroke of the outlines, as one shape: the one of the text, kerned
        var pen = new Pen(new SolidBrush(new Rgba32(20, 200, 90)), 1.5);
        Assert.Equal(
            Render(PixelFormat.Rgba64, 40, 30, canvas => canvas.StrokeText(pen, TextLayout.Create("AB", Boxes, 16), new PointD(3, 2))),
            Render(PixelFormat.Rgba64, 40, 30, canvas => canvas.StrokeGlyphs(pen, Boxes, 16, [new(FontSamples.BoxA, new PointD(3, 15)), new(FontSamples.BoxB, new PointD(17, 15))])));

        // A synthetic bold and oblique: those of a text, up to the rounding of the coverages of a glyph moved by whole
        // pixels. At 24 pixels per em, a text at (3, 2.5) has its baseline at 22, and a bold glyph is 1/48 of the size
        // (0.5) further right, in the middle of its wider advance.
        foreach (var synthesis in (FontSynthesis[])[FontSynthesis.Bold, FontSynthesis.Oblique, FontSynthesis.Bold | FontSynthesis.Oblique])
        {
            var shift = (synthesis & FontSynthesis.Bold) == FontSynthesis.None ? 0 : 0.5;
            var text = Render(PixelFormat.Gray8, 40, 30, canvas => canvas.FillText(White, TextLayout.Create("D", new TextStyle(Boxes, 24) { Synthesis = synthesis }), new PointD(3, 2.5)));
            var glyphs = Render(PixelFormat.Gray8, 40, 30, canvas => canvas.FillGlyphs(White, Boxes, 24, [new(FontSamples.TriangleD, new PointD(3 + shift, 22))], new GlyphRunOptions { Synthesis = synthesis }));
            Assert.Contains(255, glyphs);
            for (var i = 0; i < text.Length; i++)
            {
                Assert.InRange(glyphs[i], text[i] - 1, text[i] + 1, message: $"{synthesis}, sample {i}");
            }
        }

        // The glyphs a font draws with its own colors, layers or images, and a variable font
        foreach (var (font, glyph, character) in new (Font, int, string)[]
        {
            (FontTestSupport.Load(FontSamples.ColorBoxes()), FontSamples.ColorA, "A"),
            (FontTestSupport.Load(FontSamples.BitmapBoxes()), FontSamples.BitmapA, "A"),
            (FontTestSupport.Load(FontSamples.BitmapBoxes(useColorBitmapTables: true)), FontSamples.BitmapB, "B"),
        })
        {
            var brush = new SolidBrush(new Rgba32(10, 20, 250));
            var options = new GlyphRunOptions { Cache = new GlyphCache(1 << 20) };
            var text = Render(PixelFormat.Rgba64, 40, 30, canvas => canvas.FillText(brush, character, font, 16, new PointD(3, 2)));
            Assert.Equal(text, Render(PixelFormat.Rgba64, 40, 30, canvas => canvas.FillGlyphs(brush, font, 16, [new(glyph, new PointD(3, 15))])));
            Assert.Equal(text, Render(PixelFormat.Rgba64, 40, 30, canvas => canvas.FillGlyphs(brush, font, 16, [new(glyph, new PointD(3, 15))], options)));
            Assert.Equal(text, Render(PixelFormat.Rgba64, 40, 30, canvas => canvas.FillGlyphs(brush, font, 16, [new(glyph, new PointD(3, 15))], options)));
        }

        var heavy = FontTestSupport.Load(FontSamples.Variable()).WithVariations(new FontVariationSetting("wght", 900));
        var layout = TextLayout.Create("A", heavy, 20);
        // The text placed so that its baseline is on a row of pixels, where the glyph is not moved
        var heavyText = Render(PixelFormat.Gray8, 40, 30, canvas => canvas.FillText(White, layout, new PointD(2, 20 - layout.Baseline)));
        var heavyGlyphs = Render(PixelFormat.Gray8, 40, 30, canvas => canvas.FillGlyphs(White, heavy, 20, [new(FontSamples.VariableA, new PointD(2, 20))]));
        Assert.Contains(255, heavyText);
        for (var i = 0; i < heavyText.Length; i++)
        {
            Assert.InRange(heavyGlyphs[i], heavyText[i] - 1, heavyText[i] + 1, message: $"sample {i}");
        }
    }

    private static void AssertRectangles(string text, PointD origin, params RectangleD[] rectangles)
    {
        foreach (var format in (PixelFormat[])[PixelFormat.Gray8, PixelFormat.Rgba64])
        {
            var expected = Render(format, 80, 40, canvas =>
            {
                foreach (var rectangle in rectangles)
                {
                    canvas.FillRectangle(White, rectangle);
                }
            });
            Assert.Equal(expected, Render(format, 80, 40, canvas => canvas.FillText(White, text, Boxes, 16, origin)));

            // The same through a layout created once.
            var layout = TextLayout.Create(text, Boxes, 16);
            Assert.Equal(expected, Render(format, 80, 40, canvas => canvas.FillText(White, layout, origin)));
        }
    }

    /// <summary>Draws on an image whose pixels are zero and returns its samples.</summary>
    [Fact]
    public void ATransformedTextIsTheTextTransformed()
    {
        // The glyphs of this font have straight sides: a text drawn under a transformation is its path, transformed,
        // bit for bit, and so is its stroke
        var transform = AffineTransform.CreateRotation(25, new PointD(10, 10)).Then(AffineTransform.CreateScale(1.5, 0.8)).Then(AffineTransform.CreateTranslation(20, 4));
        var layout = TextLayout.Create("AB\u00C1H", new TextStyle(Boxes, 14) { Decorations = TextDecorations.Underline });
        var origin = new PointD(3.5, 6.25);
        var path = layout.ToPath(origin);
        var brush = new SolidBrush(new Rgba32(250, 120, 10, 150));
        var pen = new Pen(new SolidBrush(new Rgba32(20, 40, 250, 200)), 1.3);
        Assert.Equal(
            Render(PixelFormat.Rgba32, 120, 90, canvas => canvas.Fill(brush, path.Transform(transform))),
            Render(PixelFormat.Rgba32, 120, 90, canvas => canvas.Transform(transform, inner => inner.FillText(brush, layout, origin))));
        Assert.Equal(
            Render(PixelFormat.Rgba32, 120, 90, canvas => canvas.Fill(pen.Brush, path.Widen(pen).Transform(transform))),
            Render(PixelFormat.Rgba32, 120, 90, canvas => canvas.Transform(transform, inner => inner.StrokeText(pen, layout, origin))));
        Assert.Contains(Render(PixelFormat.Rgba32, 120, 90, canvas => canvas.Transform(transform, inner => inner.FillText(brush, layout, origin))), sample => sample != 0);

        // A text of several paints, with decorations, a synthetic bold, glyphs that have curves and glyphs a font
        // draws with its own colors: turned by a quarter, each pixel (x, y) of the drawing is the pixel
        // (height - 1 - y, x). The colors of a glyph are composed in single precision: a sample can differ by a unit.
        var curves = FontTestSupport.Load(FontSamples.Curves());
        var line = new FontColorLine(1, (0, 0, 1), (0.5, 0xFFFF, 0.5), (1, 1, 1));
        var colors = FontTestSupport.Load(FontSamples.ColorPaints(
            ('D', new FontColorPaint.Composite(
                new FontColorPaint.Glyph(3, new FontColorPaint.Sweep(line, 512, 384, 30, 300)),
                14,
                new FontColorPaint.Glyph(4, new FontColorPaint.Radial(line, 400, 300, 20, 600, 400, 300)))),
            ('E', new FontColorPaint.Glyph(3, new FontColorPaint.Linear(line, 128, 0, 600, 200, 300, 700)))));
        var gradient = new LinearGradientBrush(new PointD(0, 0), new PointD(60, 40), [new GradientStop(0, new Rgba32(0, 0, 255)), new GradientStop(1, new Rgba32(255, 255, 0, 100))]);
        var rich = TextLayout.Create(
            new RichTextBuilder()
                .Append("AB ", new TextStyle(Boxes, 13) { Brush = new SolidBrush(new Rgba32(200, 0, 0, 180)), Decorations = TextDecorations.Underline, Synthesis = FontSynthesis.Bold })
                .Append("OP ", new TextStyle(curves, 12) { Brush = gradient, Decorations = TextDecorations.Strikethrough })
                .Append("DE", new TextStyle(colors, 14) { ColorPaletteIndex = 1 })
                .Append(" QA", new TextStyle(new FontStack(curves, Boxes), 11) { Synthesis = FontSynthesis.Oblique })
                .Build(),
            new TextLayoutOptions { MaxWidth = 70 });
        Assert.True(rich.HasColorGlyphs);
        var black = new SolidBrush(new Rgba32(0, 0, 0));
        const int Width = 80;
        const int Height = 64;
        var upright = Render(PixelFormat.Rgba32, Width, Height, canvas => canvas.FillText(black, rich, new PointD(4.5, 3.25)));
        var quarterTurn = AffineTransform.CreateRotation(90).Then(AffineTransform.CreateTranslation(Height, 0));
        var turned = Render(PixelFormat.Rgba32, Height, Width, canvas => canvas.Transform(quarterTurn, inner => inner.FillText(black, rich, new PointD(4.5, 3.25))));
        Assert.HasCountGreaterThan(20, upright.Chunk(4).Select(pixel => (pixel[0], pixel[1], pixel[2])).Distinct().ToArray());
        AssertTurned(upright, turned);

        // A vertical layout whose text is all on its side is that drawing too: the paints of the text are the ones of
        // the canvas, which are not turned, so that the text has plain ones here
        var styles = new[]
        {
            new TextStyle(Boxes, 13) { Brush = new SolidBrush(new Rgba32(200, 0, 0, 180)), Decorations = TextDecorations.Underline, Synthesis = FontSynthesis.Bold },
            new TextStyle(curves, 12) { Decorations = TextDecorations.Strikethrough },
            new TextStyle(colors, 14) { ColorPaletteIndex = 1 },
            new TextStyle(new FontStack(curves, Boxes), 11) { Synthesis = FontSynthesis.Oblique, Outline = new Pen(new SolidBrush(new Rgba32(0, 90, 0)), 1.5) },
        };
        var text = new RichTextBuilder().Append("AB ", styles[0]).Append("OP ", styles[1]).Append("DE", styles[2]).Append(" QA", styles[3]).Build();
        var horizontal = TextLayout.Create(text, new TextLayoutOptions { MaxWidth = 70 });
        var vertical = TextLayout.Create(text, new TextLayoutOptions { MaxHeight = 70, WritingMode = TextWritingMode.VerticalRightToLeft, Orientation = TextOrientation.Sideways });
        Assert.True(vertical.HasColorGlyphs);
        Assert.Equal(horizontal.Height, vertical.Width, 1e-9);
        upright = Render(PixelFormat.Rgba32, Width, Height, canvas => canvas.FillText(black, horizontal, new PointD(4.5, 3.25)));
        Assert.HasCountGreaterThan(20, upright.Chunk(4).Select(pixel => (pixel[0], pixel[1], pixel[2])).Distinct().ToArray());
        AssertTurned(upright, Render(PixelFormat.Rgba32, Height, Width, canvas => canvas.FillText(black, vertical, new PointD(Height - 3.25 - vertical.Width, 4.5))));

        // And its path is the path of the horizontal layout, turned
        Assert.Equal(
            Render(PixelFormat.Rgba32, Height, Width, canvas => canvas.Fill(black, horizontal.ToPath(new PointD(4.5, 3.25)).Transform(quarterTurn))),
            Render(PixelFormat.Rgba32, Height, Width, canvas => canvas.Fill(black, vertical.ToPath(new PointD(Height - 3.25 - vertical.Width, 4.5)))));

        static void AssertTurned(int[] upright, int[] turned)
        {
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var expected = upright.AsSpan(((y * Width) + x) * 4, 4);
                    var actual = turned.AsSpan(((x * Height) + (Height - 1 - y)) * 4, 4);
                    for (var c = 0; c < 4; c++)
                    {
                        Assert.InRange(actual[c], expected[c] - 2, expected[c] + 2, message: $"pixel ({x}, {y}), sample {c}");
                    }
                }
            }
        }

        // Along a path, the path and the text are transformed together
        var route = new VectorPathBuilder().MoveTo(5, 30).LineTo(40, 12).LineTo(75, 30).Build();
        Assert.Equal(
            Render(PixelFormat.Rgba32, 120, 90, canvas => canvas.FillText(brush, layout, route.Transform(AffineTransform.CreateTranslation(12, 20)))),
            Render(PixelFormat.Rgba32, 120, 90, canvas => canvas.Transform(AffineTransform.CreateTranslation(12, 20), inner => inner.FillText(brush, layout, route))));
    }

    [Fact]
    public void TheBackgroundOfAStyleIsPaintedBehindItsTextOnEachLine()
    {
        // A advances by 16 and B by 8, 2 closer after A (the spans only differ by what is painted: they are shaped
        // together), a space by 8. The kerning of a kern table is shared between the advance of the first glyph and
        // the position of the second one: the pen is at 15 before B, which is drawn 1 before it, 22 before the space,
        // 30 before the second A, and 46 after it. The background of the middle span goes from 15 to 46, the
        // advances of its glyphs, and from the ascent to the descent of the font.
        var plain = new TextStyle(Boxes, 16);
        var red = new SolidBrush(new Rgba32(255, 0, 0));
        var marked = new TextStyle(Boxes, 16) { BackgroundBrush = red };
        var layout = TextLayout.Create(new RichTextBuilder().Append("A", plain).Append("B A", marked).Append("A", plain).Build());
        Assert.Equal(62, layout.Width);
        Assert.Equal(RectangleD.FromLTRB(2, 0, 60, 16), layout.InkBounds);
        var expected = Render(PixelFormat.Rgb24, 80, 24, canvas =>
        {
            canvas.FillRectangle(red, new RectangleD(18, 4, 31, 16));
            canvas.FillRectangle(White, new RectangleD(5, 5, 12, 12));
            canvas.FillRectangle(White, new RectangleD(18, 9, 6, 8));
            canvas.FillRectangle(White, new RectangleD(35, 5, 12, 12));
            canvas.FillRectangle(White, new RectangleD(51, 5, 12, 12));
        });
        Assert.Equal(expected, Render(PixelFormat.Rgb24, 80, 24, canvas => canvas.FillText(White, layout, new PointD(3, 4))));

        // The background is under the whole text: a glyph of another span that reaches over it is not hidden
        var over = TextLayout.Create(new RichTextBuilder().Append("A", new TextStyle(Boxes, 16) { LetterSpacing = -8 }).Append("B", marked).Build());
        Assert.Equal(
            Render(PixelFormat.Rgb24, 40, 24, canvas =>
            {
                canvas.FillRectangle(red, new RectangleD(11, 4, 8, 16));
                canvas.FillRectangle(White, new RectangleD(5, 5, 12, 12));
                canvas.FillRectangle(White, new RectangleD(12, 9, 6, 8));
            }),
            Render(PixelFormat.Rgb24, 40, 24, canvas => canvas.FillText(White, over, new PointD(3, 4))));

        // It is not an outline of the text: a stroke and the path of the layout leave it out
        var pen = new Pen(White, 1);
        Assert.Equal(
            Render(PixelFormat.Rgb24, 80, 24, canvas => canvas.StrokeText(pen, TextLayout.Create("AB AA", Boxes, 16), new PointD(3, 4))),
            Render(PixelFormat.Rgb24, 80, 24, canvas => canvas.StrokeText(pen, layout, new PointD(3, 4))));
        Assert.Equal(TextLayout.Create("AB AA", Boxes, 16).ToPath(default).ToSvgPathData(), layout.ToPath(default).ToSvgPathData());

        // One rectangle per line, without the white space that hangs at the end of a wrapped line; the text of the
        // style is painted with its own brush over it
        var blue = new SolidBrush(new Rgba32(0, 0, 255));
        var both = new TextStyle(Boxes, 16) { BackgroundBrush = red, Brush = blue };
        var wrapped = TextLayout.Create("A A", both, new TextLayoutOptions { MaxWidth = 20 });
        Assert.Equal(2, wrapped.LineCount);
        Assert.Equal(
            Render(PixelFormat.Rgb24, 40, 40, canvas =>
            {
                canvas.FillRectangle(red, new RectangleD(1, 2, 16, 16));
                canvas.FillRectangle(red, new RectangleD(1, 18, 16, 16));
                canvas.FillRectangle(blue, new RectangleD(3, 3, 12, 12));
                canvas.FillRectangle(blue, new RectangleD(3, 19, 12, 12));
            }),
            Render(PixelFormat.Rgb24, 40, 40, canvas => canvas.FillText(White, wrapped, new PointD(1, 2))));

        // A line taller than its font does not make the background taller, and a background alone draws: a space
        var tall = TextLayout.Create(" ", marked, new TextLayoutOptions { LineHeight = 30 });
        Assert.Equal(new RectangleD(0, 7, 8, 16), tall.InkBounds);
        Assert.Equal(
            Render(PixelFormat.Rgb24, 20, 40, canvas => canvas.FillRectangle(red, new RectangleD(2, 9, 8, 16))),
            Render(PixelFormat.Rgb24, 20, 40, canvas => canvas.FillText(White, tall, new PointD(2, 2))));

        // Along a straight path, it is the background of the text drawn at a point
        var baseline = new VectorPathBuilder().MoveTo(3, 17).LineTo(79, 17).Build();
        Assert.Equal(expected, Render(PixelFormat.Rgb24, 80, 24, canvas => canvas.FillText(White, layout, baseline)));
    }

    [Fact]
    public void ThePenOfAStyleStrokesItsGlyphsOnceTheTextIsFilled()
    {
        // The first A has a pen: its outline, the rectangle from (2, 1) to (14, 13), is stroked over the filled text
        var red = new SolidBrush(new Rgba32(255, 0, 0));
        var pen = new Pen(red, 2);
        var outlined = new TextStyle(Boxes, 16) { Outline = pen };
        var plain = new TextStyle(Boxes, 16);
        var layout = TextLayout.Create(new RichTextBuilder().Append("A", outlined).Append("A", plain).Build());
        var origin = new PointD(4, 4);
        var actual = Render(PixelFormat.Rgb24, 48, 24, canvas => canvas.FillText(White, layout, origin));
        Assert.Equal(
            Render(PixelFormat.Rgb24, 48, 24, canvas =>
            {
                canvas.FillRectangle(White, new RectangleD(6, 5, 12, 12));
                canvas.FillRectangle(White, new RectangleD(22, 5, 12, 12));
                canvas.StrokeRectangle(pen, new RectangleD(6, 5, 12, 12));
            }),
            actual);

        // Half of the pen is over the edge of the glyph, half around it; the other glyph has no stroke
        Assert.Equal([255, 0, 0], actual.AsSpan(((10 * 48) + 5) * 3, 3).ToArray());
        Assert.Equal([255, 0, 0], actual.AsSpan(((10 * 48) + 6) * 3, 3).ToArray());
        Assert.Equal([255, 255, 255], actual.AsSpan(((10 * 48) + 7) * 3, 3).ToArray());
        Assert.Equal([0, 0, 0], actual.AsSpan(((10 * 48) + 21) * 3, 3).ToArray());

        // A pen around the glyphs leaves them as they are filled; the stroke is the one of the outline of the glyph
        var around = new Pen(red, 2) { Alignment = PenAlignment.Outside };
        var contour = TextLayout.Create("A", new TextStyle(Boxes, 16) { Outline = around });
        actual = Render(PixelFormat.Rgb24, 48, 24, canvas => canvas.FillText(White, contour, origin));
        Assert.Equal(
            Render(PixelFormat.Rgb24, 48, 24, canvas =>
            {
                canvas.FillRectangle(White, new RectangleD(6, 5, 12, 12));
                canvas.StrokeRectangle(around, new RectangleD(6, 5, 12, 12));
            }),
            actual);
        Assert.Equal([255, 0, 0], actual.AsSpan(((10 * 48) + 4) * 3, 3).ToArray());
        Assert.Equal([255, 255, 255], actual.AsSpan(((10 * 48) + 6) * 3, 3).ToArray());

        // Several pens, a background and brushes of the spans: the backgrounds, the fills, then the strokes
        var blue = new SolidBrush(new Rgba32(0, 0, 255));
        var green = new SolidBrush(new Rgba32(0, 255, 0));
        var thin = new Pen(green, 1);
        var rich = TextLayout.Create(new RichTextBuilder()
            .Append("A", new TextStyle(Boxes, 16) { Outline = pen, BackgroundBrush = blue })
            .Append("C", new TextStyle(Boxes, 16) { Outline = thin, Brush = red })
            .Build());
        Assert.Equal(
            Render(PixelFormat.Rgb24, 48, 24, canvas =>
            {
                canvas.FillRectangle(blue, new RectangleD(4, 4, 16, 16));
                canvas.FillRectangle(White, new RectangleD(6, 5, 12, 12));
                canvas.FillRectangle(red, new RectangleD(20, 4, 12, 16));
                canvas.StrokeRectangle(pen, new RectangleD(6, 5, 12, 12));
                canvas.StrokeRectangle(thin, new RectangleD(20, 4, 12, 16));
            }),
            Render(PixelFormat.Rgb24, 48, 24, canvas => canvas.FillText(White, rich, origin)));

        // The pen of a call strokes every outline, whatever the pens of the styles
        Assert.Equal(
            Render(PixelFormat.Rgb24, 48, 24, canvas => canvas.StrokeText(thin, TextLayout.Create("AA", Boxes, 16), origin)),
            Render(PixelFormat.Rgb24, 48, 24, canvas => canvas.StrokeText(thin, layout, origin)));

        // Along a straight path, and under a transformation, as at a point
        var baseline = new VectorPathBuilder().MoveTo(4, 17).LineTo(47, 17).Build();
        Assert.Equal(
            Render(PixelFormat.Rgb24, 48, 24, canvas => canvas.FillText(White, rich, origin)),
            Render(PixelFormat.Rgb24, 48, 24, canvas => canvas.FillText(White, rich, baseline)));
        Assert.Equal(
            Render(PixelFormat.Rgb24, 48, 24, canvas => canvas.FillText(White, rich, origin)),
            Render(PixelFormat.Rgb24, 48, 24, canvas => canvas.Transform(AffineTransform.CreateTranslation(4, 4), inner => inner.FillText(White, rich, default))));

        // A text whose parts exceed the limit of the image changes no pixel: every part is prepared before the
        // first one is painted
        using var probe = new Image<Rgb24>(48, 24, new Rgb24(0, 0, 0));
        long session = 0;
        probe.Draw(_ => session = probe.Owner.Scope.LiveBytes, Ct);
        using var tight = new Image<Rgb24>(48, 24, new Rgb24(0, 0, 0), new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = session + 2048 } });
        var thrown = false;
        tight.Draw(
            canvas =>
            {
                try
                {
                    canvas.FillText(White, TextLayout.Create(new RichTextBuilder().Append("A", new TextStyle(Boxes, 16) { BackgroundBrush = blue }).Append("C", new TextStyle(Boxes, 16) { Outline = around }).Build()), origin);
                }
                catch (ImageResourceLimitException)
                {
                    thrown = true;
                }
            },
            Ct);
        Assert.True(thrown);
        Assert.All(DrawingTestSupport.ReadSamples(tight.Frames[0]), sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void ATextIsFittedToABoxByTheLargestScaleThatFits()
    {
        var style = new TextStyle(Boxes, 16);

        // A text that fits is not changed: it is never made larger
        var fits = TextLayout.CreateToFit("AAAA", style, 100, 100);
        Assert.Equal(1, fits.Scale);
        Assert.Equal(64, fits.Width);
        Assert.Equal(16, fits.Height);
        Assert.Equal(1, TextLayout.Create("AAAA", style).Scale);

        // One word of 64 pixels in a box of 32: half its size, exactly
        var half = TextLayout.CreateToFit("AAAA", style, 32, 100);
        Assert.Equal(0.5, half.Scale);
        Assert.Equal(32, half.Width);
        Assert.Equal(8, half.Height);
        Assert.Equal(6.5, half.Baseline);
        Assert.False(half.IsTruncated);
        Assert.Equal(
            Render(PixelFormat.Gray8, 40, 12, canvas => canvas.FillText(White, TextLayout.Create("AAAA", Boxes, 8), new PointD(1, 1))),
            Render(PixelFormat.Gray8, 40, 12, canvas => canvas.FillText(White, half, new PointD(1, 1))));

        // The height limits a text too
        var low = TextLayout.CreateToFit("AAAA", style, double.PositiveInfinity, 4);
        Assert.Equal(0.25, low.Scale);
        Assert.Equal(16, low.Width);

        // Lines are wrapped at the width of the box. Four words of 16 and three spaces of 8, in a box of 40 by 20:
        // two lines fit at 5/8 of the size (the first one is three words, 64 * 5 / 8 = 40 wide, and the two are
        // 32 * 5 / 8 = 20 tall), where one line would need 40 / 88
        var wrapped = TextLayout.CreateToFit("A A A A", style, 40, 20);
        Assert.Equal(0.625, wrapped.Scale);
        Assert.Equal(2, wrapped.LineCount);
        Assert.Equal(40, wrapped.Width);
        Assert.Equal(20, wrapped.Height);
        Assert.Equal(1, TextLayout.CreateToFit("A A A A", style, 40, 32).Scale);

        // Without wrapping, or on one line, the text is fitted on the lines it is allowed: 88 pixels in 44
        var single = TextLayout.CreateToFit("A A A A", style, 44, 100, new TextLayoutOptions { Wrapping = TextWrapping.NoWrap });
        Assert.Equal(0.5, single.Scale);
        Assert.Equal(1, single.LineCount);
        var oneLine = TextLayout.CreateToFit("A A A A", style, 44, 100, new TextLayoutOptions { MaxLines = 1 });
        Assert.InRange(oneLine.Scale, 0.5 - (1.0 / 4096), 0.5);
        Assert.Equal(1, oneLine.LineCount);
        Assert.False(oneLine.IsTruncated);
        Assert.InRange(oneLine.Width, 43.9, 44);

        // Every length of the styles has the scale: the sizes of every span, the spacings, the indent, the height
        // of the lines; the box is the box
        var spaced = TextLayout.CreateToFit("AA", new TextStyle(Boxes, 16) { LetterSpacing = 4 }, 20, 100, new TextLayoutOptions { LineHeight = 30, FirstLineIndent = 0 });
        Assert.Equal(0.5, spaced.Scale);
        Assert.Equal(20, spaced.Width);
        Assert.Equal(15, spaced.Height);
        var rich = new RichTextBuilder().Append("A", new TextStyle(Boxes, 16)).Append("A", new TextStyle(Boxes, 32)).Build();
        var richFit = TextLayout.CreateToFit(rich, 24, 100);
        Assert.Equal(0.5, richFit.Scale);
        Assert.Equal(24, richFit.Width);
        Assert.Equal(16, richFit.Height);

        // A text that does not fit at its smallest scale is laid out at it, in the box, and cut as the options say
        var small = TextLayout.CreateToFit("AAAA", style, 4, 4, minimumScale: 0.25);
        Assert.Equal(0.25, small.Scale);
        Assert.Equal(16, small.Width);
        var cut = TextLayout.CreateToFit("A A A A A A", style, 20, 9, new TextLayoutOptions { Trimming = TextTrimming.Character }, minimumScale: 0.5);
        Assert.Equal(0.5, cut.Scale);
        Assert.Equal(1, cut.LineCount);
        Assert.True(cut.IsTruncated);

        // An empty text fits any box
        Assert.Equal(1, TextLayout.CreateToFit("", style, 1, 16).Scale);
        Assert.Equal(0.5, TextLayout.CreateToFit("", style, 1, 8).Scale);

        Assert.Throws<ArgumentNullException>("text", () => TextLayout.CreateToFit(null!, style, 10, 10));
        Assert.Throws<ArgumentNullException>("style", () => TextLayout.CreateToFit("A", null!, 10, 10));
        Assert.Throws<ArgumentNullException>("text", () => TextLayout.CreateToFit((RichText)null!, 10, 10));
        Assert.Throws<ArgumentException>("text", () => TextLayout.CreateToFit(new RichTextBuilder().Build(), 10, 10));
        foreach (var size in (double[])[0, -1, double.NaN, double.NegativeInfinity])
        {
            Assert.Throws<ArgumentOutOfRangeException>("width", () => TextLayout.CreateToFit("A", style, size, 10));
            Assert.Throws<ArgumentOutOfRangeException>("height", () => TextLayout.CreateToFit("A", style, 10, size));
        }

        foreach (var scale in (double[])[0, -0.5, 1.5, double.NaN])
        {
            Assert.Throws<ArgumentOutOfRangeException>("minimumScale", () => TextLayout.CreateToFit("A", style, 10, 10, minimumScale: scale));
        }
    }

    [Fact]
    public void APlaceholderTakesItsRoomInTheLineAndGivesItsRectangle()
    {
        // Between A and B, something 10 wide and 8 tall that stands on the baseline: B is 10 further, without the
        // kerning it has after A, and the line is as tall as the font
        var style = new TextStyle(Boxes, 16);
        var text = new RichTextBuilder().Append("A", style).AppendPlaceholder(10, 8, 8, style).Append("B", style).Build();
        Assert.Equal("A\uFFFCB", text.Text);
        Assert.Equal(3, text.Spans.Count);
        Assert.False(text.Spans[0].IsPlaceholder);
        Assert.True(text.Spans[1].IsPlaceholder);
        Assert.Equal(1, text.Spans[1].Start);
        Assert.Equal(1, text.Spans[1].Length);
        Assert.NotEqual(text.Spans[1], new RichTextBuilder().Append("A", style).AppendPlaceholder(10, 9, 8, style).Build().Spans[1]);
        var layout = TextLayout.Create(text);
        Assert.Equal(34, layout.Width);
        Assert.Equal(16, layout.Height);
        Assert.False(layout.HasMissingGlyphs);
        Assert.Equal([new RectangleD(16, 5, 10, 8)], layout.Placeholders);

        // Nothing is drawn there: the caller does
        Assert.Equal(
            Render(PixelFormat.Gray8, 48, 20, canvas =>
            {
                canvas.FillRectangle(White, new RectangleD(3, 3, 12, 12));
                canvas.FillRectangle(White, new RectangleD(28, 7, 6, 8));
            }),
            Render(PixelFormat.Gray8, 48, 20, canvas => canvas.FillText(White, layout, new PointD(1, 2))));

        // It is a character of the text: a place of a caret, a part of a selection, and what a click finds
        Assert.Equal(new RectangleD(16, 0, 0, 16), layout.GetCaretRectangle(1));
        Assert.Equal(new RectangleD(26, 0, 0, 16), layout.GetCaretRectangle(2));
        Assert.Equal([new RectangleD(16, 0, 10, 16)], layout.GetSelectionRectangles(1, 1));
        Assert.Equal(1, layout.HitTest(new PointD(18, 8)).TextIndex);

        // The line is as tall as what stands on its baseline and as deep as what goes below it: 20 above, 10 below
        var tall = TextLayout.Create(new RichTextBuilder().Append("A", style).AppendPlaceholder(10, 30, 20, style).Build());
        Assert.Equal(30, tall.Height);
        Assert.Equal(20, tall.Baseline);
        Assert.Equal([new RectangleD(16, 0, 10, 30)], tall.Placeholders);
        Assert.Equal(
            Render(PixelFormat.Gray8, 48, 34, canvas => canvas.FillRectangle(White, new RectangleD(3, 10, 12, 12))),
            Render(PixelFormat.Gray8, 48, 34, canvas => canvas.FillText(White, tall, new PointD(1, 2))));

        // A line can end before and after a placeholder, and two that follow each other are two, in the order of the text
        var two = new RichTextBuilder().Append("A", style).AppendPlaceholder(10, 8, 8, style).AppendPlaceholder(12, 4, 4, style).Append("A", style).Build();
        Assert.Equal(4, two.Spans.Count);
        var wrapped = TextLayout.Create(two, new TextLayoutOptions { MaxWidth = 24 });
        Assert.Equal(3, wrapped.LineCount);
        Assert.Equal([new RectangleD(0, 16 + 5, 10, 8), new RectangleD(10, 16 + 9, 12, 4)], wrapped.Placeholders);

        // A placeholder the layout does not hold has no rectangle
        var cutLayout = TextLayout.Create(two, new TextLayoutOptions { MaxWidth = 24, MaxLines = 1 });
        Assert.True(cutLayout.IsTruncated);
        Assert.Equal([RectangleD.Empty, RectangleD.Empty], cutLayout.Placeholders);
        Assert.Empty(TextLayout.Create("A", style).Placeholders);

        // The background and the decorations of its style go behind and under it, and it is centered and scaled
        // with the text
        var red = new SolidBrush(new Rgba32(255, 0, 0));
        var marked = new TextStyle(Boxes, 16) { BackgroundBrush = red };
        var badge = TextLayout.Create(new RichTextBuilder().AppendPlaceholder(10, 8, 8, marked).Build(), new TextLayoutOptions { MaxWidth = 30, Alignment = TextAlignment.Center });
        Assert.Equal([new RectangleD(10, 5, 10, 8)], badge.Placeholders);
        Assert.Equal(new RectangleD(10, 0, 10, 16), badge.InkBounds);
        var fitted = TextLayout.CreateToFit(text, 17, 100, new TextLayoutOptions { Wrapping = TextWrapping.NoWrap });
        Assert.Equal(0.5, fitted.Scale);
        Assert.Equal([new RectangleD(8, 2.5, 5, 4)], fitted.Placeholders);

        // A text that follows a placeholder with the same style is another span
        var after = new RichTextBuilder().AppendPlaceholder(1, 1, 1, style).Append("A", style).Append("A", style).Build();
        Assert.Equal(2, after.Spans.Count);
        Assert.Equal(2, after.Spans[1].Length);

        var builder = new RichTextBuilder();
        Assert.Throws<ArgumentNullException>("style", () => builder.AppendPlaceholder(1, 1, 1, null!));
        Assert.Throws<ArgumentOutOfRangeException>("width", () => builder.AppendPlaceholder(-1, 1, 1, style));
        Assert.Throws<ArgumentOutOfRangeException>("width", () => builder.AppendPlaceholder(double.NaN, 1, 1, style));
        Assert.Throws<ArgumentOutOfRangeException>("height", () => builder.AppendPlaceholder(1, 65537, 1, style));
        Assert.Throws<ArgumentOutOfRangeException>("baseline", () => builder.AppendPlaceholder(1, 1, double.PositiveInfinity, style));
        Assert.Throws<ArgumentOutOfRangeException>("baseline", () => builder.AppendPlaceholder(1, 1, -65537, style));
        Assert.Empty(builder.Build().Spans);
    }

    private static int[] Render(PixelFormat format, int width, int height, DrawingAction action)
    {
        using var image = DrawingTestSupport.Create(format, width, height);
        image.Draw(action, Ct);
        return DrawingTestSupport.ReadSamples(image.Frames[0]);
    }
}
