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
