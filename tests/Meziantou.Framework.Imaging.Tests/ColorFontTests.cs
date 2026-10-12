using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The glyphs a font draws with its own colors: the expected pixels are computed by hand from the description of the
/// synthetic fonts (<see cref="FontSamples.ColorBoxes"/> and <see cref="FontSamples.ColorPaints"/>), whose shapes are
/// rectangles on whole pixels at 16 pixels per em, and drawn with <c>FillRectangle</c>. Real color fonts are compared
/// with the reference renderer in <c>Conformance/ColorFontConformanceTests</c>.
/// </summary>
public sealed class ColorFontTests
{
    private static readonly Rgba32 Red = new(255, 0, 0);
    private static readonly Rgba32 Green = new(0, 255, 0);
    private static readonly Rgba32 Black = new(0, 0, 0);
    private static readonly Rgba32 White = new(255, 255, 255);

    // The three layers of A at 16 pixels per em, when the layout is drawn at (0, 0): its baseline is at 13.
    private static readonly RectangleD Outer = new(2, 1, 12, 12);
    private static readonly RectangleD Middle = new(4, 3, 8, 8);
    private static readonly RectangleD Inner = new(6, 5, 4, 4);

    private static Font ColorBoxes { get; } = FontTestSupport.Load(FontSamples.ColorBoxes());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void AColorGlyphIsItsLayersFromTheBottomToTheTop()
    {
        // A: red, then green, then the color of the text. B: blue at an opacity of 128 on 255, and white at 64 on 255
        // over its upper half. C has no color of its own: it is drawn with the brush of the text.
        foreach (var font in (Font[])[ColorBoxes, FontTestSupport.Load(FontSamples.ColorPaints())])
        {
            Assert.True(font.HasColorGlyphs);
            var actual = Draw(40, 20, canvas => canvas.FillText(Black, "ABC", font, 16, new PointD(0, 0)));
            var expected = Draw(40, 20, canvas =>
            {
                canvas.FillRectangle(Red, Outer);
                canvas.FillRectangle(Green, Middle);
                canvas.FillRectangle(Black, Inner);
                canvas.FillRectangle(new Rgba32(0, 0, 255, 128), new RectangleD(17, 5, 6, 8));
                canvas.FillRectangle(new Rgba32(255, 255, 255, 64), new RectangleD(17, 5, 6, 4));
                canvas.FillRectangle(Black, new RectangleD(24, 0, 12, 16));
            });

            // The layers of a glyph are composed with each other before the glyph is composed with the image: the
            // translucent ones differ by the rounding of one more step.
            AssertClose(expected, actual, tolerance: 1);
            Assert.Equal(expected.AsSpan(0, 40 * 4 * 5).ToArray(), actual.AsSpan(0, 40 * 4 * 5).ToArray());
            Assert.Equal(new Rgba32(255, 0, 0), GetPixel(actual, 40, 2, 1));
            Assert.Equal(new Rgba32(0, 255, 0), GetPixel(actual, 40, 4, 3));
            Assert.Equal(new Rgba32(0, 0, 0), GetPixel(actual, 40, 6, 5));
            Assert.Equal(new Rgba32(127, 127, 255), GetPixel(actual, 40, 17, 12));
        }
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Bgra32)]
    [InlineData(PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Rgba64)]
    [InlineData(PixelFormat.Gray8)]
    [InlineData(PixelFormat.Gray16)]
    public void TheColorsAreConvertedLikeTheColorsOfABrush(int format)
    {
        // Opaque layers on whole pixels: the same samples as the rectangles, in every pixel format.
        var pixelFormat = (PixelFormat)format;
        var blue = new Rgba64(0, 0, 65535, 40000);
        using var actual = DrawingTestSupport.Create(pixelFormat, 20, 16);
        actual.Draw(canvas => canvas.FillText(blue, "A", ColorBoxes, 16, new PointD(0, 0)), Ct);
        using var expected = DrawingTestSupport.Create(pixelFormat, 20, 16);
        expected.Draw(
            canvas =>
            {
                canvas.FillRectangle(Red, Outer);
                canvas.FillRectangle(Green, Middle);
            },
            Ct);

        // The inner rectangle has the color of the text, which is translucent here: over the green of the glyph, not
        // over the image.
        var samples = DrawingTestSupport.ReadSamples(actual.Frames[0]);
        var reference = DrawingTestSupport.ReadSamples(expected.Frames[0]);
        var channels = samples.Length / (20 * 16);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                if (x is >= 6 and < 10 && y is >= 5 and < 9)
                    continue;

                Assert.Equal(reference.AsSpan(((y * 20) + x) * channels, channels).ToArray(), samples.AsSpan(((y * 20) + x) * channels, channels).ToArray());
            }
        }

        using var over = DrawingTestSupport.Create(pixelFormat, 20, 16);
        over.Draw(
            canvas =>
            {
                canvas.FillRectangle(Green, Middle);
                canvas.FillRectangle(blue, Inner);
            },
            Ct);
        var overSamples = DrawingTestSupport.ReadSamples(over.Frames[0]);
        var max = pixelFormat is PixelFormat.Rgba64 or PixelFormat.Gray16 ? 65535 : 255;
        for (var c = 0; c < channels; c++)
        {
            Assert.Equal(overSamples[(((6 * 20) + 7) * channels) + c], samples[(((6 * 20) + 7) * channels) + c], max / 255.0);
        }
    }

    [Fact]
    public void TheTextColorOfAGlyphIsTheBrushOfTheText()
    {
        // A solid brush, the brush of a span, and a gradient: the inner rectangle of A is painted with it, where it is
        // on the image.
        var blue = new SolidBrush(new Rgba32(0, 0, 255));
        var actual = Draw(20, 16, canvas => canvas.FillText(blue, "A", ColorBoxes, 16, new PointD(0, 0)));
        Assert.Equal(new Rgba32(0, 0, 255), GetPixel(actual, 20, 7, 6));

        var layout = TextLayout.Create("A", new TextStyle(ColorBoxes, 16) { Brush = new SolidBrush(Red) });
        actual = Draw(20, 16, canvas => canvas.FillText(blue, layout, new PointD(0, 0)));
        Assert.Equal(new Rgba32(255, 0, 0), GetPixel(actual, 20, 7, 6));

        var gradient = new LinearGradientBrush(new PointD(6, 0), new PointD(10, 0), [new GradientStop(0, new Rgba32(0, 0, 0)), new GradientStop(1, new Rgba32(0, 0, 255))]);
        actual = Draw(20, 16, canvas => canvas.FillText(gradient, "A", ColorBoxes, 16, new PointD(0, 0)));
        var expected = Draw(20, 16, canvas =>
        {
            canvas.FillRectangle(Red, Outer);
            canvas.FillRectangle(Green, Middle);
            canvas.FillRectangle(gradient, Inner);
        });
        AssertClose(expected, actual, tolerance: 1);
        Assert.True(GetPixel(actual, 20, 6, 6).B < GetPixel(actual, 20, 9, 6).B);
    }

    [Fact]
    public void AStyleChoosesThePaletteOfItsColorGlyphs()
    {
        var palettes = ColorBoxes.ColorPalettes;
        Assert.HasCount(2, palettes);
        Assert.Equal([new Rgba32(255, 0, 0), new Rgba32(0, 255, 0), new Rgba32(0, 0, 255, 128), new Rgba32(255, 255, 255, 64)], palettes[0].Colors);
        Assert.Equal([new Rgba32(255, 255, 0), new Rgba32(255, 0, 255), new Rgba32(0, 255, 255), new Rgba32(0, 0, 0, 128)], palettes[1].Colors);
        Assert.Equal((true, false, false, true), (palettes[0].IsForLightBackground, palettes[0].IsForDarkBackground, palettes[1].IsForLightBackground, palettes[1].IsForDarkBackground));

        // The second palette: yellow then magenta. A palette the font does not have is its first one.
        var second = TextLayout.Create("A", new TextStyle(ColorBoxes, 16) { ColorPaletteIndex = 1 });
        var actual = Draw(20, 16, canvas => canvas.FillText(Black, second, new PointD(0, 0)));
        Assert.Equal((new Rgba32(255, 255, 0), new Rgba32(255, 0, 255), new Rgba32(0, 0, 0)), (GetPixel(actual, 20, 2, 1), GetPixel(actual, 20, 4, 3), GetPixel(actual, 20, 6, 5)));
        var missing = TextLayout.Create("A", new TextStyle(ColorBoxes, 16) { ColorPaletteIndex = 7 });
        actual = Draw(20, 16, canvas => canvas.FillText(Black, missing, new PointD(0, 0)));
        Assert.Equal(new Rgba32(255, 0, 0), GetPixel(actual, 20, 2, 1));

        // Two palettes in one text.
        var rich = new RichTextBuilder()
            .Append("A", new TextStyle(ColorBoxes, 16))
            .Append("A", new TextStyle(ColorBoxes, 16) { ColorPaletteIndex = 1 })
            .Build();
        actual = Draw(40, 16, canvas => canvas.FillText(Black, TextLayout.Create(rich), new PointD(0, 0)));
        Assert.Equal((new Rgba32(255, 0, 0), new Rgba32(255, 255, 0)), (GetPixel(actual, 40, 2, 1), GetPixel(actual, 40, 18, 1)));

        Assert.Throws<ArgumentOutOfRangeException>(() => new TextStyle(ColorBoxes, 16) { ColorPaletteIndex = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextStyle(ColorBoxes, 16) { ColorPaletteIndex = 65536 });

        // A font without color.
        var boxes = FontTestSupport.Load(FontSamples.Boxes());
        Assert.False(boxes.HasColorGlyphs);
        Assert.Empty(boxes.ColorPalettes);

        // A font without palette: its layers have the color of the text.
        var builder = FontSamples.ColorBoxes();
        builder.ColorPalettes.Clear();
        var withoutPalette = FontTestSupport.Load(builder);
        Assert.Empty(withoutPalette.ColorPalettes);
        actual = Draw(20, 16, canvas => canvas.FillText(new Rgba32(0, 0, 255), "A", withoutPalette, 16, new PointD(0, 0)));
        Assert.Equal((new Rgba32(0, 0, 255), new Rgba32(0, 0, 255), new Rgba32(255, 255, 255)), (GetPixel(actual, 20, 2, 1), GetPixel(actual, 20, 6, 5), GetPixel(actual, 20, 1, 1)));
    }

    [Fact]
    public void TheOutlineOfAColorGlyphIsTheOneOfItsOwnGlyph()
    {
        // The box of what is drawn is the box of the layers; a stroke and a path use the outline of the glyph itself.
        var layout = TextLayout.Create("A", ColorBoxes, 16);
        Assert.Equal(Outer, layout.InkBounds);
        var path = layout.ToPath(new PointD(0, 0));
        var actual = Draw(20, 16, canvas => canvas.Fill(Black, path));
        var expected = Draw(20, 16, canvas => canvas.FillRectangle(Black, Outer));
        Assert.Equal(expected, actual);

        var pen = new Pen(Black, 1);
        actual = Draw(20, 16, canvas => canvas.StrokeText(pen, layout, new PointD(0, 0)));
        expected = Draw(20, 16, canvas => canvas.StrokeRectangle(pen, Outer));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AColorGlyphFollowsAPathAndIsClippedByTheImage()
    {
        // Down a vertical path from (50, 5): the letter is turned, its top on the right. Its layers are 2, 4 and 6
        // pixels from its origin along the path, and 0, 2 and 4 pixels from the path.
        var layout = TextLayout.Create("A", ColorBoxes, 16);
        var path = new VectorPathBuilder().MoveTo(50, 5).LineTo(50, 60).Build();
        var actual = Draw(70, 30, canvas => canvas.FillText(Black, layout, path));
        var expected = Draw(70, 30, canvas =>
        {
            canvas.FillRectangle(Red, new RectangleD(50, 7, 12, 12));
            canvas.FillRectangle(Green, new RectangleD(52, 9, 8, 8));
            canvas.FillRectangle(Black, new RectangleD(54, 11, 4, 4));
        });
        Assert.Equal(expected, actual);

        // A glyph that is partly, or entirely, outside the image.
        actual = Draw(8, 8, canvas => canvas.FillText(Black, layout, new PointD(-5, -4)));
        expected = Draw(8, 8, canvas =>
        {
            canvas.FillRectangle(Red, new RectangleD(-3, -3, 12, 12));
            canvas.FillRectangle(Green, new RectangleD(-1, -1, 8, 8));
            canvas.FillRectangle(Black, new RectangleD(1, 1, 4, 4));
        });
        Assert.Equal(expected, actual);
        Assert.Equal(Draw(8, 8, static _ => { }), Draw(8, 8, canvas => canvas.FillText(Black, layout, new PointD(100, 100))));
        Assert.Equal(Draw(8, 8, static _ => { }), Draw(8, 8, canvas => canvas.FillText(Black, layout, new PointD(-100, 2))));

        // Between two pixels: the edges of the layers are covered like the edges of the rectangles.
        actual = Draw(20, 16, canvas => canvas.FillText(Black, layout, new PointD(0.25, 0.5)));
        expected = Draw(20, 16, canvas =>
        {
            canvas.FillRectangle(Red, new RectangleD(2.25, 1.5, 12, 12));
            canvas.FillRectangle(Green, new RectangleD(4.25, 3.5, 8, 8));
            canvas.FillRectangle(Black, new RectangleD(6.25, 5.5, 4, 4));
        });
        AssertClose(expected, actual, tolerance: 1);
    }

    [Fact]
    public void ThePaintsOfAGlyphAreTransformedClippedAndComposed()
    {
        // Glyph 3 is the outer rectangle of A, (128, 0)-(896, 768); glyph 4 its middle one, (256, 128)-(768, 640);
        // glyph 5 its inner one, (384, 256)-(640, 512); glyph 8 the upper half of B, (64, 256)-(448, 512).
        var font = FontTestSupport.Load(FontSamples.ColorTransforms());

        // An opacity of one half.
        AssertGlyph(font, "D", Black, 1, (new Rgba32(255, 0, 0, 128), Outer));

        // The inner rectangle moved by (128, 128): two pixels right and two pixels up.
        AssertGlyph(font, "E", Black, 0, (Green, new RectangleD(8, 3, 4, 4)));

        // The inner rectangle one and a half times as large, around its center: 6 pixels instead of 4.
        AssertGlyph(font, "F", Black, 0, (Red, new RectangleD(5, 4, 6, 6)));

        // The upper half of B, 6 pixels wide and 4 pixels tall, turned by a quarter of a turn around its center.
        AssertGlyph(font, "G", Black, 0, (Red, new RectangleD(2, 4, 4, 6)));

        // A fill within two outlines: what they both cover.
        AssertGlyph(font, "H", Black, 0, (Red, Middle));

        // The source where the backdrop is; the backdrop where the source is not; the two colors multiplied where both
        // are, which is black.
        AssertGlyph(font, "I", Black, 0, (Red, Inner));
        AssertGlyph(font, "J", Black, 0, (Green, Outer), (White, Inner));
        AssertGlyph(font, "K", Black, 0, (Green, Outer), (Black, Inner));

        // The color of the text at an opacity of one half.
        AssertGlyph(font, "L", new Rgba32(0, 0, 255), 1, (new Rgba32(0, 0, 255, 128), Outer));

        // A matrix: twice as tall, then moved by (64, -256): the rectangle (448, 256)-(704, 768).
        AssertGlyph(font, "M", Black, 0, (Red, new RectangleD(7, 1, 4, 8)));

        // A skew without angle; a paint of an unknown format, which draws nothing; a scale around the origin.
        AssertGlyph(font, "N", Black, 0, (Red, Inner));
        AssertGlyph(font, "O", Black, 0, (Green, Middle));
        AssertGlyph(font, "P", Black, 0, (Red, new RectangleD(3, 1, 2, 6)));
    }

    [Fact]
    public void GradientsAreEvaluatedAtTheCenterOfEachPixel()
    {
        var font = FontTestSupport.Load(FontSamples.ColorGradients());

        // Linear: from red at the left of the rectangle, x = 2, to green at its right, x = 14.
        var pixels = Draw(20, 16, canvas => canvas.FillText(Black, "D", font, 16, new PointD(0, 0)));
        for (var x = 2; x < 14; x++)
        {
            var t = (x + 0.5 - 2) / 12;
            AssertColor(255 * (1 - t), 255 * t, 0, GetPixel(pixels, 20, x, 6));
            Assert.Equal(GetPixel(pixels, 20, x, 1), GetPixel(pixels, 20, x, 12));
        }

        // The point that turns the gradient: with P2 above P0, a P1 that is also higher changes nothing.
        Assert.Equal(pixels, Draw(20, 16, canvas => canvas.FillText(Black, "L", font, 16, new PointD(0, 0))));

        // Radial: from red at the center, the corner of pixels (8, 7), to green 6 pixels from it.
        pixels = Draw(20, 16, canvas => canvas.FillText(Black, "E", font, 16, new PointD(0, 0)));
        foreach (var (x, y) in ((int, int)[])[(10, 6), (8, 7), (3, 2), (13, 11), (7, 1)])
        {
            var t = Math.Min(1, Math.Sqrt(((x + 0.5 - 8) * (x + 0.5 - 8)) + ((y + 0.5 - 7) * (y + 0.5 - 7))) / 6);
            AssertColor(255 * (1 - t), 255 * t, 0, GetPixel(pixels, 20, x, y));
        }

        // Sweep: from red on the right of the center to green on its left, counter-clockwise: through the top of the
        // image. Under the center, the angle is beyond the end of the color line.
        pixels = Draw(20, 16, canvas => canvas.FillText(Black, "F", font, 16, new PointD(0, 0)));
        foreach (var (x, y) in ((int, int)[])[(10, 6), (8, 2), (3, 6), (12, 3)])
        {
            var t = Math.Atan2(7 - (y + 0.5), x + 0.5 - 8) / Math.PI;
            AssertColor(255 * (1 - t), 255 * t, 0, GetPixel(pixels, 20, x, y));
        }

        Assert.Equal(Green, GetPixel(pixels, 20, 10, 8));
        Assert.Equal(Green, GetPixel(pixels, 20, 4, 11));

        // A color line from 0.25 to 0.75 that repeats: its period is half of the rectangle, 6 pixels, from x = 5.
        pixels = Draw(20, 16, canvas => canvas.FillText(Black, "G", font, 16, new PointD(0, 0)));
        for (var x = 2; x < 14; x++)
        {
            var t = (((x + 0.5 - 2) / 12) - 0.25) / 0.5;
            t -= Math.Floor(t);
            AssertColor(255 * (1 - t), 255 * t, 0, GetPixel(pixels, 20, x, 6));
        }

        // A color line from 0 to 0.5 that reflects: red at 2, green at 8, red at 14.
        pixels = Draw(20, 16, canvas => canvas.FillText(Black, "H", font, 16, new PointD(0, 0)));
        for (var x = 2; x < 14; x++)
        {
            var t = (x + 0.5 - 2) / 6;
            t = t > 1 ? 2 - t : t;
            AssertColor(255 * (1 - t), 255 * t, 0, GetPixel(pixels, 20, x, 6));
        }

        // A gradient without length is not drawn.
        Assert.Equal(Draw(20, 16, static _ => { }), Draw(20, 16, canvas => canvas.FillText(Black, "I", font, 16, new PointD(0, 0))));

        // Stops at one place: the first color before it, the last one from it. The place is the middle, x = 8.
        pixels = Draw(20, 16, canvas => canvas.FillText(Black, "J", font, 16, new PointD(0, 0)));
        Assert.Equal((Red, Red, Green, Green), (GetPixel(pixels, 20, 2, 6), GetPixel(pixels, 20, 7, 6), GetPixel(pixels, 20, 8, 6), GetPixel(pixels, 20, 13, 6)));

        // A sweep that starts and ends at the same angle, a quarter of a turn: with a color line that pads, the first
        // color before the angle and the last one from it; with one that repeats or reflects, nothing. The same holds
        // for a color line whose stops are at one place.
        pixels = Draw(20, 16, canvas => canvas.FillText(Black, "M", font, 16, new PointD(0, 0)));
        Assert.Equal((Red, Green, Green, Green), (GetPixel(pixels, 20, 10, 6), GetPixel(pixels, 20, 3, 6), GetPixel(pixels, 20, 4, 11), GetPixel(pixels, 20, 10, 8)));
        Assert.Equal(Draw(20, 16, static _ => { }), Draw(20, 16, canvas => canvas.FillText(Black, "N", font, 16, new PointD(0, 0))));
        Assert.Equal(Draw(20, 16, static _ => { }), Draw(20, 16, canvas => canvas.FillText(Black, "O", font, 16, new PointD(0, 0))));
        Assert.True(TextLayout.Create("N", font, 16).InkBounds.IsEmpty);

        // A sweep whose start is after its end: the color line goes the other way, from red on the left of the center
        // to green on its right, through the top; under the center, the angle is before the color line.
        pixels = Draw(20, 16, canvas => canvas.FillText(Black, "P", font, 16, new PointD(0, 0)));
        foreach (var (x, y) in ((int, int)[])[(10, 6), (8, 2), (3, 6), (12, 3)])
        {
            var t = 1 - (Math.Atan2(7 - (y + 0.5), x + 0.5 - 8) / Math.PI);
            AssertColor(255 * (1 - t), 255 * t, 0, GetPixel(pixels, 20, x, y));
        }

        Assert.Equal(Red, GetPixel(pixels, 20, 10, 8));

        // A stop that is the color of the text, at half its opacity: the colors are interpolated with their opacity.
        pixels = Draw(20, 16, canvas => canvas.FillText(new Rgba32(0, 0, 255), "K", font, 16, new PointD(0, 0)));
        for (var x = 2; x < 14; x++)
        {
            // Red at an opacity from 1 to 0 and blue at an opacity from 0 to 0.5, over white.
            var t = (x + 0.5 - 2) / 12;
            var alpha = (1 - t) + (0.5 * t);
            AssertColor((255 * (1 - t)) + (255 * (1 - alpha)), 255 * (1 - alpha), (255 * 0.5 * t) + (255 * (1 - alpha)), GetPixel(pixels, 20, x, 6));
        }
    }

    [Fact]
    public void AClipBoxBoundsAGlyphAndAGlyphCanPaintAnotherOne()
    {
        var font = FontTestSupport.Load(FontSamples.ColorClips());

        // The left half of the outer rectangle: the clip box stops at 512 units, 8 pixels.
        AssertGlyph(font, "D", Black, 0, (Red, new RectangleD(2, 1, 6, 12)));
        Assert.Equal(new RectangleD(2, 1, 6, 12), TextLayout.Create("D", font, 16).InkBounds);

        // The paint of D, with its clip box, over the inner rectangle.
        AssertGlyph(font, "E", Black, 0, (Green, Inner), (Red, new RectangleD(2, 1, 6, 12)));

        // A fill without outline fills the clip box.
        AssertGlyph(font, "F", Black, 0, (Green, Middle));

        // A glyph that paints itself: the paint that would never end is left out.
        AssertGlyph(font, "G", Black, 0, (Red, Middle));

        // The paint of D moved by two pixels, with its clip box.
        AssertGlyph(font, "H", Black, 0, (Red, new RectangleD(4, 1, 6, 12)));

        // The paint of D twice, side by side: a glyph that is painted again after it was painted is not a cycle.
        AssertGlyph(font, "I", Black, 0, (Red, Outer));
    }

    [Fact]
    public void TheWorkAndTheMemoryOfAColorGlyphAreLimited()
    {
        // Paints nested beyond the depth a graph can have.
        FontColorPaint deep = new FontColorPaint.Glyph(3, new FontColorPaint.Solid(0));
        for (var i = 0; i < 70; i++)
        {
            deep = new FontColorPaint.Translate(new FontColorPaint.Layers(deep), 1, 0);
        }

        var font = FontTestSupport.Load(FontSamples.ColorPaints(('D', deep)));
        var exception = Assert.Throws<InvalidImageContentException>(() => TextLayout.Create("D", font, 16));
        Assert.Contains("'COLR'", exception.Message, StringComparison.Ordinal);

        // The paints of a glyph count in its complexity: A has three layers of four points.
        var limited = new FontLoadOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxGlyphComplexity = 40 } } };
        var small = Font.Load(FontSamples.ColorBoxes().Build(), limited);
        Assert.Equal(12, TextLayout.Create("C", small, 16).Width);
        var limit = Assert.Throws<ImageResourceLimitException>(() => TextLayout.Create("A", small, 16));
        Assert.Equal(ImageResourceLimitKind.GlyphComplexity, limit.Kind);

        // The surface of a glyph is charged to the image: a text that needs more than the image allows changes no
        // pixel, not even with the glyphs that come before the color glyph.
        var layout = TextLayout.Create("CA", ColorBoxes, 400);
        using var image = new Image<Rgba32>(700, 420, White, new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 2_400_000 } });
        limit = Assert.Throws<ImageResourceLimitException>(() => image.Draw(canvas => canvas.FillText(Black, layout, new PointD(0, 0)), Ct));
        Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, limit.Kind);
        Assert.Equal(White, image.Frames[0][10, 10]);
        Assert.Equal(White, image.Frames[0][400, 200]);

        // The same image without the color glyph has room for the text.
        image.Draw(canvas => canvas.FillText(Black, TextLayout.Create("CC", ColorBoxes, 400), new PointD(0, 0)), Ct);
        Assert.Equal(Black, image.Frames[0][10, 10]);

        // Layers that refer to glyphs the font does not have, and to layers outside the table.
        var builder = FontSamples.ColorBoxes();
        builder.ColorLayers[FontSamples.ColorA] = [(3, 0), (200, 1), (1, 1)];
        AssertGlyph(FontTestSupport.Load(builder), "A", Black, 0, (Red, Outer));
        var tables = FontSamples.ColorBoxes().BuildTables();
        var colr = (byte[])tables["COLR"].Clone();
        colr[13] = 2;
        var truncated = Font.Load(FontBuilder.WriteSfnt(new SortedDictionary<string, byte[]>(tables, StringComparer.Ordinal) { ["COLR"] = colr }));
        Assert.Throws<InvalidImageContentException>(() => TextLayout.Create("B", truncated, 16));

        // A table of a version that is not known: the glyphs are drawn with their outlines.
        colr = (byte[])tables["COLR"].Clone();
        colr[1] = 9;
        var unknown = Font.Load(FontBuilder.WriteSfnt(new SortedDictionary<string, byte[]>(tables, StringComparer.Ordinal) { ["COLR"] = colr }));
        Assert.False(unknown.HasColorGlyphs);
        AssertGlyph(unknown, "A", Black, 0, (Black, Outer));
    }

    [Theory]
    [InlineData(false, 1, 17)]
    [InlineData(true, 1, 17)]
    [InlineData(true, 3, 18)]
    [InlineData(true, 4, 17)]
    [InlineData(true, 5, 19)]
    [InlineData(true, 2, 19)]
    [InlineData(true, 5, 18)]
    public void AGlyphThatIsAnImageIsDrawnPixelForPixelAtTheSizeOfItsStrike(bool useColorBitmapTables, int indexFormat, int imageFormat)
    {
        var font = FontTestSupport.Load(FontSamples.BitmapBoxes(useColorBitmapTables, indexFormat, imageFormat));
        Assert.True(font.HasColorGlyphs);
        Assert.Empty(font.ColorPalettes);

        // At 16 pixels per em: the image of A, 12 by 12 pixels, 2 pixels from the origin and on the baseline, which is
        // at 13. Red, a green square in its middle, and a last row of blue at half opacity over the white image.
        var pixels = Draw(48, 20, canvas => canvas.FillText(Black, "AC", font, 16, new PointD(0, 0)));
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                var expected = x is < 2 or >= 14 || y is < 1 or >= 13 ? White
                    : x is >= 6 and < 10 && y is >= 5 and < 9 ? Green
                    : y == 12 ? new Rgba32(127, 127, 255)
                    : Red;
                Assert.Equal(expected, GetPixel(pixels, 48, x, y));
            }
        }

        // C has no image: its outline, with the brush of the text.
        Assert.Equal((Black, Black, White), (GetPixel(pixels, 48, 16, 0), GetPixel(pixels, 48, 27, 15), GetPixel(pixels, 48, 28, 15)));
        Assert.Equal(new RectangleD(2, 0, 26, 16), TextLayout.Create("AC", font, 16).InkBounds);

        // At 32 pixels per em, the other strike: yellow with a magenta square, 4 pixels from the origin, on the baseline
        // at 26.
        pixels = Draw(48, 40, canvas => canvas.FillText(Black, "A", font, 32, new PointD(0, 0)));
        Assert.Equal((new Rgba32(255, 255, 0), new Rgba32(255, 0, 255), new Rgba32(255, 255, 0), White, White), (GetPixel(pixels, 48, 4, 2), GetPixel(pixels, 48, 12, 10), GetPixel(pixels, 48, 27, 25), GetPixel(pixels, 48, 3, 2), GetPixel(pixels, 48, 28, 25)));

        // Between two pixels: the image is interpolated, and its edges are covered like the edges of a rectangle.
        pixels = Draw(48, 20, canvas => canvas.FillText(Black, "A", font, 16, new PointD(0.5, 0)));
        AssertColor(255, 127.5, 127.5, GetPixel(pixels, 48, 2, 3));
        AssertColor(255, 127.5, 127.5, GetPixel(pixels, 48, 14, 3));
        Assert.Equal((Red, White), (GetPixel(pixels, 48, 4, 3), GetPixel(pixels, 48, 15, 3)));

        // The glyphs of the fonts whose images all have the size of the first one.
        if (useColorBitmapTables && indexFormat is 2 or 5)
        {
            pixels = Draw(48, 20, canvas => canvas.FillText(Black, "B", font, 16, new PointD(0, 0)));
            Assert.Equal((Red, Green), (GetPixel(pixels, 48, 2, 1), GetPixel(pixels, 48, 6, 5)));
            return;
        }

        // B: 6 by 8 pixels of cyan, 1 pixel from the origin, whose first pixel is transparent.
        pixels = Draw(48, 20, canvas => canvas.FillText(Black, "B", font, 16, new PointD(0, 0)));
        Assert.Equal((White, new Rgba32(0, 255, 255), new Rgba32(0, 255, 255), White, White), (GetPixel(pixels, 48, 1, 5), GetPixel(pixels, 48, 2, 5), GetPixel(pixels, 48, 6, 12), GetPixel(pixels, 48, 7, 12), GetPixel(pixels, 48, 1, 4)));

        // D has the image of A.
        Assert.Equal(
            Draw(48, 20, canvas => canvas.FillText(Black, "A", font, 16, new PointD(0, 0))),
            Draw(48, 20, canvas => canvas.FillText(Black, "D", font, 16, new PointD(0, 0))));
    }

    // The image of A of the bitmap boxes font, without its blue row: 12 by 12 red pixels with a green square of 4
    // pixels in the middle, encoded by cjpeg of libjpeg-turbo (-quality 100 -sample 1x1 -optimize). Its djpeg decodes
    // the red as (254, 0, 0) and the green as (0, 255, 1), and one pixel of the square as (1, 255, 2).
    private static ReadOnlySpan<byte> JpegImageOfA =>
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43,
        0x00, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0xFF, 0xDB, 0x00, 0x43, 0x01, 0x01, 0x01,
        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x0C, 0x00, 0x0C, 0x03,
        0x01, 0x11, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01, 0xFF, 0xC4, 0x00, 0x16, 0x00, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x09, 0xFF, 0xC4, 0x00, 0x21, 0x10, 0x00, 0x00, 0x05, 0x02, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x14, 0x15, 0x16, 0x05, 0x13, 0x17, 0x23, 0x26, 0x43, 0x46, 0x64, 0x65, 0xFF, 0xC4, 0x00, 0x17,
        0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x09, 0x07, 0x0A, 0xFF, 0xC4, 0x00,
        0x2A, 0x11, 0x00, 0x00, 0x03, 0x06, 0x01, 0x0D, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x15, 0x17, 0x07, 0x09, 0x13,
        0x14, 0x16, 0x18, 0x45, 0x04, 0x12, 0x19, 0x22, 0x24, 0x25, 0x28, 0x42, 0x47, 0x63, 0x64, 0x65, 0x67, 0xFF, 0xDA, 0x00, 0x0C, 0x03, 0x01, 0x00,
        0x02, 0x11, 0x03, 0x11, 0x00, 0x3F, 0x00, 0xCF, 0x32, 0xF2, 0x5E, 0x71, 0xEA, 0x5F, 0xAB, 0xE2, 0x91, 0x48, 0xFF, 0x00, 0x1F, 0x7D, 0x5E, 0xFA,
        0xF7, 0xED, 0xD1, 0xD2, 0xA5, 0x67, 0xEC, 0xDF, 0x53, 0xB3, 0x67, 0x36, 0x2A, 0xB4, 0xA6, 0x94, 0x9E, 0x12, 0xEE, 0x53, 0x83, 0x83, 0x2C, 0x48,
        0xBE, 0x5C, 0xBE, 0x43, 0xC0, 0xCB, 0x63, 0x46, 0x9D, 0xED, 0x43, 0x85, 0xCF, 0x9F, 0xA9, 0xD1, 0x23, 0xEA, 0x1F, 0x51, 0xA1, 0xFE, 0xDA, 0xB8,
        0x6A, 0xB8, 0x7B, 0x87, 0x59, 0x3A, 0xC8, 0x92, 0xD2, 0x09, 0x2A, 0x57, 0xF2, 0xB6, 0x9B, 0x50, 0x54, 0x0A, 0x6F, 0xA4, 0x2A, 0x24, 0xC4, 0x8C,
        0xB6, 0x09, 0xAC, 0x6A, 0x42, 0xEA, 0x00, 0x00, 0x00, 0x0F, 0xFF, 0xD9,
    ];

    [Fact]
    public void AnImageOfAnSbixTableIsAPngAJpegOrATiffFile()
    {
        // A TIFF image: the pixels of the PNG image of the font, in one uncompressed strip.
        var builder = FontSamples.BitmapBoxes();
        var image = builder.BitmapStrikes[0].Glyphs[0];
        var rgba = new byte[12 * 12 * 4];
        for (var y = 0; y < 12; y++)
        {
            for (var x = 0; x < 12; x++)
            {
                var (red, green, blue, alpha) = x is >= 4 and < 8 && y is >= 4 and < 8 ? (0, 255, 0, 255) : y == 11 ? (0, 0, 255, 128) : (255, 0, 0, 255);
                rgba[(((y * 12) + x) * 4) + 0] = (byte)red;
                rgba[(((y * 12) + x) * 4) + 1] = (byte)green;
                rgba[(((y * 12) + x) * 4) + 2] = (byte)blue;
                rgba[(((y * 12) + x) * 4) + 3] = (byte)alpha;
            }
        }

        builder.BitmapStrikes[0].Glyphs[0] = image with { Type = "tiff", Data = WriteTiff(12, 12, rgba) };
        var expected = Draw(20, 20, canvas => canvas.FillText(Black, "A", FontTestSupport.Load(FontSamples.BitmapBoxes()), 16, new PointD(0, 0)));
        Assert.Equal(expected, Draw(20, 20, canvas => canvas.FillText(Black, "A", FontTestSupport.Load(builder), 16, new PointD(0, 0))));

        // A JPEG image, which has no opacity: 2 pixels from the origin, on the baseline at 13.
        builder.BitmapStrikes[0].Glyphs[0] = image with { Type = "jpg ", Data = JpegImageOfA.ToArray() };
        var font = FontTestSupport.Load(builder);
        Assert.True(font.HasColorGlyphs);
        var pixels = Draw(20, 20, canvas => canvas.FillText(Black, "A", font, 16, new PointD(0, 0)));
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                var actual = GetPixel(pixels, 20, x, y);
                if (x is < 2 or >= 14 || y is < 1 or >= 13)
                {
                    Assert.Equal(White, actual);
                }
                else if (x is >= 6 and < 10 && y is >= 5 and < 9)
                {
                    AssertColor(0.5, 255, 1.5, actual);
                }
                else
                {
                    AssertColor(254, 0, 0, actual);
                }
            }
        }

        // The data is the image the font says it is: a PNG image under the type of a JPEG image is an error, like a
        // JPEG image that is cut.
        builder.BitmapStrikes[0].Glyphs[0] = image with { Type = "jpg " };
        var exception = Assert.Throws<InvalidImageContentException>(() => TextLayout.Create("A", FontTestSupport.Load(builder), 16));
        Assert.Contains("format the font gives", exception.Message, StringComparison.Ordinal);
        builder.BitmapStrikes[0].Glyphs[0] = image with { Type = "jpg ", Data = JpegImageOfA[..200].ToArray() };
        Assert.Throws<InvalidImageContentException>(() => TextLayout.Create("A", FontTestSupport.Load(builder), 16));
    }

    /// <summary>
    /// Writes a TIFF image of 8-bit red, green, blue and alpha samples: "TIFF Revision 6.0", a baseline RGB image in one
    /// uncompressed strip, with one extra sample that is an opacity the colors are not multiplied by.
    /// </summary>
    /// <param name="width">The width, in pixels.</param>
    /// <param name="height">The height, in pixels.</param>
    /// <param name="rgba">The pixels, row by row from the top: four bytes each, with a color that is not multiplied by its opacity.</param>
    private static byte[] WriteTiff(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length != width * height * 4)
            throw new ArgumentException("The pixels are four bytes each", nameof(rgba));

        // The header, the directory of eleven fields, the four values of BitsPerSample, then the pixels.
        const int FieldCount = 11;
        const int BitsOffset = 8 + 2 + (FieldCount * 12) + 4;
        const int PixelsOffset = BitsOffset + 8;
        var data = new List<byte>();
        void UInt16(int value)
        {
            data.Add((byte)value);
            data.Add((byte)(value >> 8));
        }

        void UInt32(int value)
        {
            UInt16(value & 0xFFFF);
            UInt16(value >> 16);
        }

        void Field(int tag, int type, int count, int value)
        {
            UInt16(tag);
            UInt16(type);
            UInt32(count);
            if (type == 3 && count == 1)
            {
                UInt16(value);
                UInt16(0);
            }
            else
            {
                UInt32(value);
            }
        }

        // Little-endian, version 42, the directory at 8.
        data.AddRange("II"u8.ToArray());
        UInt16(42);
        UInt32(8);
        UInt16(FieldCount);
        Field(256, 3, 1, width);
        Field(257, 3, 1, height);
        Field(258, 3, 4, BitsOffset);
        Field(259, 3, 1, 1);
        Field(262, 3, 1, 2);
        Field(273, 4, 1, PixelsOffset);
        Field(277, 3, 1, 4);
        Field(278, 3, 1, height);
        Field(279, 4, 1, rgba.Length);
        Field(284, 3, 1, 1);
        Field(338, 3, 1, 2);
        UInt32(0);
        for (var i = 0; i < 4; i++)
        {
            UInt16(8);
        }

        data.AddRange(rgba);
        return [.. data];
    }

    [Fact]
    public void AnImageIsScaledToTheSizeOfTheText()
    {
        var font = FontTestSupport.Load(FontSamples.BitmapBoxes());

        // At 64 pixels per em, the largest strike is enlarged twice: 48 by 48 pixels, 8 pixels from the origin, on the
        // baseline at 52. Away from its edges and from its square, it is yellow; the middle of its square is magenta.
        var pixels = Draw(72, 70, canvas => canvas.FillText(Black, "A", font, 64, new PointD(0, 0)));
        Assert.Equal((new Rgba32(255, 255, 0), new Rgba32(255, 0, 255), new Rgba32(255, 255, 0), White, White), (GetPixel(pixels, 72, 10, 6), GetPixel(pixels, 72, 32, 28), GetPixel(pixels, 72, 54, 50), GetPixel(pixels, 72, 7, 20), GetPixel(pixels, 72, 56, 20)));

        // At 24 pixels per em, the strike of 32 is reduced when the text is laid out: 18 by 18 pixels, 3 pixels from
        // the origin, on the baseline at 19.5.
        pixels = Draw(72, 70, canvas => canvas.FillText(Black, "A", font, 24, new PointD(0, 0.5)));
        AssertColor(255, 255, 0, GetPixel(pixels, 72, 5, 4));
        AssertColor(255, 255, 0, GetPixel(pixels, 72, 19, 18));
        var square = GetPixel(pixels, 72, 12, 11);
        Assert.True(square.R > 245 && square.G < 12 && square.B > 245, $"The middle is {square}");
        Assert.Equal((White, White), (GetPixel(pixels, 72, 2, 10), GetPixel(pixels, 72, 21, 10)));

        // At 8 pixels per em, the strike of 16 is reduced by half: 6 by 6 pixels, 1 pixel from the origin, on the
        // baseline at 6.5: red around, green in the middle.
        pixels = Draw(72, 70, canvas => canvas.FillText(Black, "A", font, 8, new PointD(0, 0.5)));
        var corner = GetPixel(pixels, 72, 1, 1);
        var middle = GetPixel(pixels, 72, 3, 3);
        Assert.True(corner.R > 200 && corner.G < 60 && corner.B < 60, $"The corner is {corner}");
        Assert.True(middle.G > 150 && middle.R < 110, $"The middle is {middle}");
        Assert.Equal((White, White), (GetPixel(pixels, 72, 0, 3), GetPixel(pixels, 72, 7, 3)));

        // Along a path that goes down, the image is turned by a quarter of a turn: its top is on the right, and each
        // of its pixels is still a pixel. The green square is 4 pixels from its left, which is now its top.
        var layout = TextLayout.Create("A", font, 16);
        var path = new VectorPathBuilder().MoveTo(50, 5).LineTo(50, 60).Build();
        pixels = Draw(72, 70, canvas => canvas.FillText(Black, layout, path));
        Assert.Equal((Red, Red, Green, Green, new Rgba32(127, 127, 255), White, White), (GetPixel(pixels, 72, 61, 7), GetPixel(pixels, 72, 51, 18), GetPixel(pixels, 72, 54, 11), GetPixel(pixels, 72, 57, 14), GetPixel(pixels, 72, 50, 12), GetPixel(pixels, 72, 62, 7), GetPixel(pixels, 72, 49, 12)));

        // A stroke and a path use the outline of the glyph.
        var outline = Draw(72, 70, canvas => canvas.Fill(Black, layout.ToPath(new PointD(0, 0))));
        Assert.Equal(Draw(72, 70, canvas => canvas.FillRectangle(Black, Outer)), outline);
        Assert.Equal(Outer, layout.InkBounds);
    }

    [Fact]
    public void AMalformedImageIsRejectedWhenTheTextIsLaidOut()
    {
        // An image that is cut, and one that is not an image.
        var builder = FontSamples.BitmapBoxes();
        var image = builder.BitmapStrikes[0].Glyphs[0];
        builder.BitmapStrikes[0].Glyphs[0] = image with { Data = image.Data[..40] };
        var font = FontTestSupport.Load(builder);
        Assert.Throws<InvalidImageContentException>(() => TextLayout.Create("A", font, 16));
        Assert.Equal(8, TextLayout.Create("B", font, 16).Width);
        Assert.NotNull(TextLayout.Create("A", font, 32));

        builder.BitmapStrikes[0].Glyphs[0] = image with { Data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12] };
        var exception = Assert.Throws<InvalidImageContentException>(() => TextLayout.Create("A", FontTestSupport.Load(builder), 16));
        Assert.Contains("image of a glyph", exception.Message, StringComparison.Ordinal);

        // An image of a type the specification does not have, a glyph that refers to itself, and a glyph that refers to
        // a glyph the font does not have: the glyph is drawn with its outline.
        foreach (var replacement in (FontBitmapGlyph[])[image with { Type = "pdf " }, image with { DuplicateOf = FontSamples.BitmapA }, image with { DuplicateOf = 200 }])
        {
            builder.BitmapStrikes[0].Glyphs[0] = replacement;
            AssertGlyph(FontTestSupport.Load(builder), "A", Black, 0, (Black, Outer));
        }

        // An image larger than the configuration of the font allows.
        builder = FontSamples.BitmapBoxes();
        var limited = new FontLoadOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxFramePixels = 100 } } };
        var small = Font.Load(builder.Build(), limited);
        Assert.Equal(8, TextLayout.Create("B", small, 16).Width);
        Assert.Throws<ImageResourceLimitException>(() => TextLayout.Create("A", small, 16));

        // Tables of a version that is not known, or without strike: the glyphs are drawn with their outlines.
        var tables = FontSamples.BitmapBoxes().BuildTables();
        var sbix = (byte[])tables["sbix"].Clone();
        sbix[1] = 9;
        var unknown = Font.Load(FontBuilder.WriteSfnt(new SortedDictionary<string, byte[]>(tables, StringComparer.Ordinal) { ["sbix"] = sbix }));
        Assert.False(unknown.HasColorGlyphs);
        AssertGlyph(unknown, "A", Black, 0, (Black, Outer));

        // A table that is cut inside the data of a glyph.
        var truncated = Font.Load(FontBuilder.WriteSfnt(new SortedDictionary<string, byte[]>(tables, StringComparer.Ordinal) { ["sbix"] = tables["sbix"][..80] }));
        Assert.Throws<InvalidImageContentException>(() => TextLayout.Create("A", truncated, 16));
    }

    /// <summary>Asserts that a text of one glyph, drawn at (0, 0) at 16 pixels per em on a white image, is rectangles painted in order.</summary>
    private static void AssertGlyph(Font font, string text, Rgba32 brush, int tolerance, params (Rgba32 Color, RectangleD Rectangle)[] fills)
    {
        var actual = Draw(20, 16, canvas => canvas.FillText(brush, text, font, 16, new PointD(0, 0)));
        var expected = Draw(20, 16, canvas =>
        {
            foreach (var (color, rectangle) in fills)
            {
                canvas.FillRectangle(color, rectangle);
            }
        });
        if (tolerance == 0)
        {
            Assert.Equal(expected, actual);
        }
        else
        {
            AssertClose(expected, actual, tolerance);
        }
    }

    private static void AssertColor(double red, double green, double blue, Rgba32 actual)
    {
        if (Math.Abs(actual.R - red) > 1.01 || Math.Abs(actual.G - green) > 1.01 || Math.Abs(actual.B - blue) > 1.01)
        {
            Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"The pixel is {actual}, expected ({red:0.##}, {green:0.##}, {blue:0.##})"));
        }
    }

    private static void AssertClose(byte[] expected, byte[] actual, int tolerance)
    {
        Assert.HasCount(expected.Length, actual);
        for (var i = 0; i < expected.Length; i++)
        {
            if (Math.Abs(expected[i] - actual[i]) > tolerance)
            {
                Assert.Fail($"The sample {i % 4} of the pixel {i / 4} is {actual[i]}, expected {expected[i]}");
            }
        }
    }

    private static Rgba32 GetPixel(byte[] pixels, int width, int x, int y)
    {
        var offset = ((y * width) + x) * 4;
        return new Rgba32(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    /// <summary>Draws on a white image and returns its samples.</summary>
    private static byte[] Draw(int width, int height, DrawingAction action)
    {
        using var image = new Image<Rgba32>(width, height, White);
        image.Draw(action, Ct);
        var pixels = new Rgba32[width * height];
        image.Frames[0].CopyPixelDataTo(pixels);
        return unsafe(MemoryMarshal.AsBytes(pixels.AsSpan())).ToArray();
    }
}
