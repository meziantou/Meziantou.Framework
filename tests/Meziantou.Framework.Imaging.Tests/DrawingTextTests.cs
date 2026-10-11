using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Drawing;
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

    /// <summary>Asserts that a text of the boxes font at 16 pixels per em paints exactly the given rectangles of whole pixels.</summary>
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
    private static int[] Render(PixelFormat format, int width, int height, DrawingAction action)
    {
        using var image = DrawingTestSupport.Create(format, width, height);
        image.Draw(action, Ct);
        return DrawingTestSupport.ReadSamples(image.Frames[0]);
    }
}
