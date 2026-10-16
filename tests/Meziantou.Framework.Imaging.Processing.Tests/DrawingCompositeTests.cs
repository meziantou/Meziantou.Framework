using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Compositing of the paint over the pixels, and conversion of the paint to the pixel format. Every expected sample is a
/// literal derived by hand from the definition of source-over with straight alpha (the derivation is in the comments);
/// the coverages are exact fractions obtained with rectangles on halves and quarters of a pixel. The conformance tests
/// add a rational reference over random pixels, paints and coverages.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class DrawingCompositeTests
{
    private static readonly RectangleD WholePixel = new(0, 0, 1, 1);
    private static readonly RectangleD HalfPixel = new(0, 0, 0.5, 1);
    private static readonly RectangleD QuarterPixel = new(0, 0, 0.5, 0.5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void AnOpaquePaintOnAFullyCoveredPixelIsWrittenExactlyInEveryPixelFormat()
    {
        // 16-bit samples 0x1234, 0x5678, 0x9ABC.
        // To 8 bits, (v * 255 + 32767) / 65535: 4660 -> 18, 22136 -> 86, 39612 -> 154.
        // To gray, (2126 * 4660 + 7152 * 22136 + 722 * 39612 + 5000) / 10000 = 196828696 / 10000 = 19682 at 16 bits, then
        // (19682 * 255 + 32767) / 65535 = 77 at 8 bits.
        var color = new Rgba64(0x1234, 0x5678, 0x9ABC);
        Assert.Equal(new Rgba64(0x1234, 0x5678, 0x9ABC), Paint(new Rgba64(1, 2, 3, 4), color, WholePixel));
        Assert.Equal(new Rgba32(18, 86, 154), Paint(new Rgba32(1, 2, 3, 4), color, WholePixel));
        Assert.Equal(new Bgra32(18, 86, 154), Paint(new Bgra32(1, 2, 3, 4), color, WholePixel));
        Assert.Equal(new Rgb24(18, 86, 154), Paint(new Rgb24(1, 2, 3), color, WholePixel));
        Assert.Equal(new Gray16(19682), Paint(new Gray16(1), color, WholePixel));
        Assert.Equal(new Gray8(77), Paint(new Gray8(1), color, WholePixel));
    }

    [Fact]
    public void ARedPaintIsItsRec709LumaOnGrayscaleFrames()
    {
        // (2126 * 65535 + 5000) / 10000 = 13933 at 16 bits; (13933 * 255 + 32767) / 65535 = 54 at 8 bits
        Assert.Equal(new Gray16(13933), Paint(new Gray16(0), new Rgba32(255, 0, 0), WholePixel));
        Assert.Equal(new Gray8(54), Paint(new Gray8(0), new Rgba32(255, 0, 0), WholePixel));
    }

    [Fact]
    public void AnOpaqueDestinationIsAWeightedMeanRoundedToNearestWithTiesUpward()
    {
        // Half of the pixel, opaque paint: (paint + pixel) / 2.
        //   (201 + 10) / 2 = 105.5 -> 106 (tie upward) | (100 + 20) / 2 = 60 | (51 + 30) / 2 = 40.5 -> 41
        var paint = new Rgba32(201, 100, 51);
        Assert.Equal(new Rgb24(106, 60, 41), Paint(new Rgb24(10, 20, 30), paint, HalfPixel));
        Assert.Equal(new Rgba32(106, 60, 41), Paint(new Rgba32(10, 20, 30), paint, HalfPixel));
        Assert.Equal(new Bgra32(106, 60, 41), Paint(new Bgra32(10, 20, 30), paint, HalfPixel));

        // Gray16, half of the pixel: (13933 + 1000) / 2 = 7466.5 -> 7467
        Assert.Equal(new Gray16(7467), Paint(new Gray16(1000), new Rgba32(255, 0, 0), HalfPixel));
    }

    [Fact]
    public void AFormatWithoutAlphaStaysOpaqueUnderATranslucentPaint()
    {
        // White at 128/255 over black: 255 * 128 / 255 = 128 exactly
        Assert.Equal(new Rgb24(128, 128, 128), Paint(new Rgb24(0, 0, 0), new Rgba32(255, 255, 255, 128), WholePixel));
        Assert.Equal(new Gray8(128), Paint(new Gray8(0), new Rgba32(255, 255, 255, 128), WholePixel));
    }

    [Fact]
    public void ATranslucentPaintOverATranslucentPixelFollowsSourceOver()
    {
        // Red at 128/255 over blue at 128/255, whole pixel. With alphas as fractions: as = ad = 128/255,
        // ao = as + ad (1 - as) = 48896 / 65025 -> 191.75 -> 192; red = 255 as / ao = 170.2 -> 170;
        // blue = 255 ad (1 - as) / ao = 84.8 -> 85
        Assert.Equal(new Rgba32(170, 0, 85, 192), Paint(new Rgba32(0, 0, 255, 128), new Rgba32(255, 0, 0, 128), WholePixel));
        Assert.Equal(new Bgra32(170, 0, 85, 192), Paint(new Bgra32(0, 0, 255, 128), new Rgba32(255, 0, 0, 128), WholePixel));

        // The same at 16 bits (samples * 257): ao * 65535 = 49279.498 -> 49279; red 43747.2 -> 43747; blue 21787.8 -> 21788
        Assert.Equal(new Rgba64(43747, 0, 21788, 49279), Paint(new Rgba64(0, 0, 65535, 32896), new Rgba32(255, 0, 0, 128), WholePixel));
    }

    [Fact]
    public void TheCoverageMultipliesTheAlphaOfThePaint()
    {
        // (250, 10, 90) at 180/255 on a quarter of the pixel, over (40, 80, 120) at 200/255.
        // as = 180/255 / 4 = 3/17; ad = 40/51; ad (1 - as) = 560/867; ao = 713/867 -> 209.7 -> 210.
        // red = (250 * 153 + 40 * 560) / 713 = 85.06 -> 85; green = (10 * 153 + 80 * 560) / 713 = 64.98 -> 65;
        // blue = (90 * 153 + 120 * 560) / 713 = 113.56 -> 114
        Assert.Equal(new Rgba32(85, 65, 114, 210), Paint(new Rgba32(40, 80, 120, 200), new Rgba32(250, 10, 90, 180), QuarterPixel));

        // 16 bits, 3/8 of the pixel, (65535, 300, 12345) at 50000/65535 over (1000, 20000, 65535) at 40000/65535:
        // computed with exact fractions from the same definition
        Assert.Equal(
            new Rgba64(26579, 12192, 44453, 47306),
            Paint(new Rgba64(1000, 20000, 65535, 40000), new Rgba64(65535, 300, 12345, 50000), new RectangleD(0, 0, 0.75, 0.5)));
    }

    [Fact]
    public void ATransparentPixelTakesTheColorOfThePaintAndItsWeightedAlpha()
    {
        // Nothing of the hidden color is left: the color is the paint, the alpha is 100 / 2 = 50
        Assert.Equal(new Rgba32(10, 20, 30, 50), Paint(new Rgba32(1, 2, 3, 0), new Rgba32(10, 20, 30, 100), HalfPixel));
        Assert.Equal(new Rgba64(2570, 5140, 7710, 12850), Paint(new Rgba64(1, 2, 3, 0), new Rgba32(10, 20, 30, 100), HalfPixel));
    }

    [Fact]
    public void APixelThatWouldStayTransparentIsNotWritten()
    {
        // Alpha 1/255 on a quarter of the pixel: the output alpha is 0.25 -> 0. The pixel keeps its hidden color.
        Assert.Equal(new Rgba32(1, 2, 3, 0), Paint(new Rgba32(1, 2, 3, 0), new Rgba32(10, 20, 30, 1), QuarterPixel));

        // On half of the pixel the output alpha is 0.5 -> 1 (tie upward): the pixel is written
        Assert.Equal(new Rgba32(10, 20, 30, 1), Paint(new Rgba32(1, 2, 3, 0), new Rgba32(10, 20, 30, 1), HalfPixel));
    }

    [Fact]
    public void ATransparentPaintWritesNothing()
    {
        Assert.Equal(new Rgba32(1, 2, 3, 4), Paint(new Rgba32(1, 2, 3, 4), new Rgba32(200, 200, 200, 0), WholePixel));
        Assert.Equal(new Rgb24(1, 2, 3), Paint(new Rgb24(1, 2, 3), new Rgba32(200, 200, 200, 0), WholePixel));

        // A 16-bit alpha that rounds to 0 at 8 bits (127 * 255 / 65535 = 0.49) is transparent on an 8-bit frame, and
        // still paints a 16-bit frame
        Assert.Equal(new Rgba32(1, 2, 3, 4), Paint(new Rgba32(1, 2, 3, 4), new Rgba64(65535, 65535, 65535, 127), WholePixel));
        Assert.NotEqual(new Rgba64(1, 2, 3, 4), Paint(new Rgba64(1, 2, 3, 4), new Rgba64(65535, 65535, 65535, 127), WholePixel));
    }

    [Fact]
    public void AFullCoverageIsTheCompositingOfTheAnimationDecoders()
    {
        // Every pair of alphas, one color: the same result as the exact integer source-over used to composite frames
        for (var sourceAlpha = 0; sourceAlpha <= 255; sourceAlpha += 5)
        {
            for (var destinationAlpha = 0; destinationAlpha <= 255; destinationAlpha += 5)
            {
                var destination = new Rgba32(200, 17, 90, (byte)destinationAlpha);
                var paint = new Rgba32(13, 250, 101, (byte)sourceAlpha);
                byte[] expected = [destination.R, destination.G, destination.B, destination.A];
                Internals.AnimationCompositor.BlendOver8([paint.R, paint.G, paint.B, paint.A], expected);
                Assert.Equal(new Rgba32(expected[0], expected[1], expected[2], expected[3]), Paint(destination, paint, WholePixel));
            }
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // The other operators. Alphas as fractions of one: as (paint), ad (pixel). The result has the alpha Fa as + Fb ad
    // and the color (Fa as paint + Fb ad pixel) / alpha.
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void SourceReplacesThePixelAlphaIncluded()
    {
        // Fa = 1, Fb = 0: the paint, exactly, whatever the pixel
        Assert.Equal(new Rgba32(10, 20, 30, 100), Paint(new Rgba32(200, 17, 90, 250), new Rgba32(10, 20, 30, 100), WholePixel, CompositeMode.Source));
        Assert.Equal(new Rgba64(1234, 5678, 9012, 3456), Paint(new Rgba64(60000, 1, 2, 65535), new Rgba64(1234, 5678, 9012, 3456), WholePixel, CompositeMode.Source));

        // A transparent paint erases: the pixel becomes transparent black
        Assert.Equal(new Rgba32(0, 0, 0, 0), Paint(new Rgba32(200, 17, 90, 250), new Rgba32(1, 2, 3, 0), WholePixel, CompositeMode.Source));

        // On half of the pixel: half of the paint and half of what the pixel was.
        //   An opaque paint on a transparent pixel: alpha 1/2 -> 127.5 -> 128, the color of the paint
        Assert.Equal(new Rgba32(200, 100, 50, 128), Paint(new Rgba32(1, 2, 3, 0), new Rgba32(200, 100, 50), HalfPixel, CompositeMode.Source));

        //   A transparent paint on an opaque pixel: half of the pixel is erased
        Assert.Equal(new Rgba32(10, 20, 30, 128), Paint(new Rgba32(10, 20, 30), new Rgba32(200, 100, 50, 0), HalfPixel, CompositeMode.Source));

        //   (200, 100, 50) at 102/255 on half of an opaque (10, 20, 30): alpha (1 + 0.4) / 2 = 0.7 -> 178.5 -> 179;
        //   red = (0.5 * 10 + 0.2 * 200) / 0.7 = 64.29 | green = (10 + 20) / 0.7 = 42.86 | blue = (15 + 10) / 0.7 = 35.71
        Assert.Equal(new Rgba32(64, 43, 36, 179), Paint(new Rgba32(10, 20, 30), new Rgba32(200, 100, 50, 102), HalfPixel, CompositeMode.Source));

        // A format without alpha stays opaque: it gets the color of the result over black, 255 * 102 / 255 = 102
        Assert.Equal(new Rgb24(102, 102, 102), Paint(new Rgb24(10, 20, 30), new Rgba32(255, 255, 255, 102), WholePixel, CompositeMode.Source));
        Assert.Equal(new Gray8(102), Paint(new Gray8(200), new Rgba32(255, 255, 255, 102), WholePixel, CompositeMode.Source));
    }

    [Fact]
    public void DestinationOutErasesThePixelWhereThePaintIsOpaque()
    {
        // Fa = 0, Fb = 1 - as: the alpha of the pixel is multiplied by one minus the alpha of the paint; its color stays
        Assert.Equal(new Rgba32(0, 0, 0, 0), Paint(new Rgba32(10, 20, 30, 200), new Rgba32(255, 0, 0), WholePixel, CompositeMode.DestinationOut));

        // A paint at 51/255 = 0.2: 200 * 0.8 = 160
        Assert.Equal(new Rgba32(10, 20, 30, 160), Paint(new Rgba32(10, 20, 30, 200), new Rgba32(255, 0, 0, 51), WholePixel, CompositeMode.DestinationOut));

        // An opaque paint on half of the pixel: 200 / 2 = 100
        Assert.Equal(new Rgba32(10, 20, 30, 100), Paint(new Rgba32(10, 20, 30, 200), new Rgba32(255, 0, 0), HalfPixel, CompositeMode.DestinationOut));

        // A transparent paint erases nothing, and a transparent pixel stays as it is, hidden color included
        Assert.Equal(new Rgba32(10, 20, 30, 200), Paint(new Rgba32(10, 20, 30, 200), new Rgba32(255, 0, 0, 0), WholePixel, CompositeMode.DestinationOut));
        Assert.Equal(new Rgba32(1, 2, 3, 0), Paint(new Rgba32(1, 2, 3, 0), new Rgba32(255, 0, 0), WholePixel, CompositeMode.DestinationOut));

        // A format without alpha darkens: (100, 150, 255) * 0.8
        Assert.Equal(new Rgb24(80, 120, 204), Paint(new Rgb24(100, 150, 255), new Rgba32(9, 9, 9, 51), WholePixel, CompositeMode.DestinationOut));
    }

    [Fact]
    public void TheOtherOperatorsFollowTheirFactors()
    {
        var red = new Rgba32(255, 0, 0);
        var halfRed = new Rgba32(255, 0, 0, 128);

        // Destination over (Fa = 1 - ad, Fb = 1), opaque red behind blue at 128/255: alpha 1;
        // red = 255 * 127 / 255 = 127 | blue = 255 * 128 / 255 = 128. Behind an opaque pixel, nothing shows.
        Assert.Equal(new Rgba32(127, 0, 128, 255), Paint(new Rgba32(0, 0, 255, 128), red, WholePixel, CompositeMode.DestinationOver));
        Assert.Equal(new Rgba32(0, 0, 255), Paint(new Rgba32(0, 0, 255), red, WholePixel, CompositeMode.DestinationOver));
        Assert.Equal(new Rgba32(255, 0, 0), Paint(new Rgba32(9, 9, 9, 0), red, WholePixel, CompositeMode.DestinationOver));

        // Source in (Fa = ad, Fb = 0): the paint with the alpha of the pixel; nothing where the pixel is transparent
        Assert.Equal(new Rgba32(255, 0, 0, 128), Paint(new Rgba32(0, 0, 255, 128), red, WholePixel, CompositeMode.SourceIn));
        Assert.Equal(new Rgba32(1, 2, 3, 0), Paint(new Rgba32(1, 2, 3, 0), red, WholePixel, CompositeMode.SourceIn));

        // Destination in (Fa = 0, Fb = as): the pixel with its alpha multiplied by the alpha of the paint, 200 * 0.2 = 40
        Assert.Equal(new Rgba32(10, 20, 30, 40), Paint(new Rgba32(10, 20, 30, 200), new Rgba32(255, 0, 0, 51), WholePixel, CompositeMode.DestinationIn));
        Assert.Equal(new Rgba32(10, 20, 30, 200), Paint(new Rgba32(10, 20, 30, 200), red, WholePixel, CompositeMode.DestinationIn));
        Assert.Equal(new Rgba32(0, 0, 0, 0), Paint(new Rgba32(10, 20, 30, 200), new Rgba32(255, 0, 0, 0), WholePixel, CompositeMode.DestinationIn));

        // Source out (Fa = 1 - ad, Fb = 0): the paint where the pixel is not, 255 * 127 / 255 = 127
        Assert.Equal(new Rgba32(255, 0, 0, 127), Paint(new Rgba32(0, 0, 255, 128), red, WholePixel, CompositeMode.SourceOut));
        Assert.Equal(new Rgba32(0, 0, 0, 0), Paint(new Rgba32(0, 0, 255), red, WholePixel, CompositeMode.SourceOut));

        // Source atop (Fa = ad, Fb = 1 - as), red at 128/255 over blue at 200/255: the alpha of the pixel;
        // red = 255 * 128 / 255 = 128 | blue = 255 * 127 / 255 = 127
        Assert.Equal(new Rgba32(128, 0, 127, 200), Paint(new Rgba32(0, 0, 255, 200), halfRed, WholePixel, CompositeMode.SourceAtop));

        // Destination atop (Fa = 1 - ad, Fb = as): the alpha of the paint; red = 255 * 55 / 255 | blue = 255 * 200 / 255
        Assert.Equal(new Rgba32(55, 0, 200, 128), Paint(new Rgba32(0, 0, 255, 200), halfRed, WholePixel, CompositeMode.DestinationAtop));

        // Xor (Fa = 1 - ad, Fb = 1 - as): two opaque colors cancel each other; an opaque paint fills a transparent pixel
        Assert.Equal(new Rgba32(0, 0, 0, 0), Paint(new Rgba32(0, 0, 255), red, WholePixel, CompositeMode.Xor));
        Assert.Equal(new Rgba32(255, 0, 0), Paint(new Rgba32(0, 0, 255, 0), red, WholePixel, CompositeMode.Xor));

        // Clear (Fa = 0, Fb = 0): only the shape counts, not the paint
        Assert.Equal(new Rgba32(0, 0, 0, 0), Paint(new Rgba32(10, 20, 30, 200), new Rgba32(1, 2, 3, 0), WholePixel, CompositeMode.Clear));
        Assert.Equal(new Rgba32(10, 20, 30, 100), Paint(new Rgba32(10, 20, 30, 200), red, HalfPixel, CompositeMode.Clear));
        Assert.Equal(new Rgb24(0, 0, 0), Paint(new Rgb24(10, 20, 30), red, WholePixel, CompositeMode.Clear));
        Assert.Equal(new Rgb24(5, 10, 15), Paint(new Rgb24(10, 20, 30), red, HalfPixel, CompositeMode.Clear));

        // Plus (Fa = 1, Fb = 1, clipped to one): red at 128/255 and blue at 128/255 add up to an opaque pixel whose
        // colors are 255 * 128 / 255; light added to light saturates; a transparent pixel takes the paint as it is
        Assert.Equal(new Rgba32(128, 0, 128, 255), Paint(new Rgba32(0, 0, 255, 128), halfRed, WholePixel, CompositeMode.Plus));
        Assert.Equal(new Rgba32(255, 255, 0), Paint(new Rgba32(0, 255, 0), red, WholePixel, CompositeMode.Plus));
        Assert.Equal(new Rgba32(255, 255, 255), Paint(new Rgba32(100, 100, 100), new Rgba32(200, 200, 200), WholePixel, CompositeMode.Plus));
        Assert.Equal(new Rgba32(255, 0, 0, 128), Paint(new Rgba32(9, 9, 9, 0), halfRed, WholePixel, CompositeMode.Plus));

        // Half covered: the sum for half of the pixel, the pixel for the rest. Red: (255 / 2 + 1/2) rounded upward;
        // blue: 255 in both halves
        Assert.Equal(new Rgba32(128, 0, 255), Paint(new Rgba32(0, 0, 255), red, HalfPixel, CompositeMode.Plus));

        // Without alpha: 100 + 100 * 51 / 255 = 120, 150 + 20 = 170, and 255 + 20 clipped to 255
        Assert.Equal(new Rgb24(120, 170, 255), Paint(new Rgb24(100, 150, 255), new Rgba32(100, 100, 100, 51), WholePixel, CompositeMode.Plus));

        // A layer is added to what is under it as a whole
        using var image = new Image<Rgba32>(2, 1, new Rgba32(0, 200, 0));
        image.Frames[0].Draw(canvas => canvas.Layer(1, layer => layer.FillRectangle(red, new Rectangle(0, 0, 1, 1)), CompositeMode.Plus), Ct);
        Assert.Equal(new Rgba32(255, 200, 0), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(0, 200, 0), image.Frames[0][1, 0]);

        // In linear light, the light is added: 128 is 0.21586 of the light of 255, twice that is encoded as 175.55
        using var gray = new Image<Rgba32>(1, 1, new Rgba32(128, 128, 128));
        gray.Frames[0].Draw(canvas => canvas.Compositing(CompositingSpace.LinearSrgb, linear => linear.Fill(new SolidBrush(new Rgba32(128, 128, 128)) { CompositeMode = CompositeMode.Plus })), Ct);
        Assert.Equal(new Rgba32(176, 176, 176), gray.Frames[0][0, 0]);
    }

    [Fact]
    public void ATransparentPaintWritesNothingWithTheOperatorsThatKeepThePixel()
    {
        var transparent = new Rgba32(200, 200, 200, 0);
        foreach (var mode in new[] { CompositeMode.SourceOver, CompositeMode.DestinationOver, CompositeMode.DestinationOut, CompositeMode.SourceAtop, CompositeMode.Xor, CompositeMode.Plus })
        {
            Assert.Equal(new Rgba32(1, 2, 3, 4), Paint(new Rgba32(1, 2, 3, 4), transparent, WholePixel, mode));
            Assert.Equal(new Rgba32(1, 2, 3, 0), Paint(new Rgba32(1, 2, 3, 0), transparent, WholePixel, mode));
            Assert.Equal(new Rgb24(1, 2, 3), Paint(new Rgb24(1, 2, 3), transparent, WholePixel, mode));
        }

        // With the others, it erases
        foreach (var mode in new[] { CompositeMode.Source, CompositeMode.SourceIn, CompositeMode.DestinationIn, CompositeMode.SourceOut, CompositeMode.DestinationAtop, CompositeMode.Clear })
        {
            Assert.Equal(new Rgba32(0, 0, 0, 0), Paint(new Rgba32(1, 2, 3, 4), transparent, WholePixel, mode));
            Assert.Equal(new Rgba32(1, 2, 3, 0), Paint(new Rgba32(1, 2, 3, 0), transparent, WholePixel, mode));
        }
    }

    [Fact]
    public void TheOperatorIsAPropertyOfTheBrush()
    {
        Assert.Equal(CompositeMode.SourceOver, new SolidBrush(new Rgba32(1, 2, 3)).CompositeMode);
        Assert.Equal(CompositeMode.SourceOver, ((Brush)new Rgba32(1, 2, 3)).CompositeMode);
        Assert.Equal(CompositeMode.Xor, new SolidBrush(new Rgba32(1, 2, 3)) { CompositeMode = CompositeMode.Xor }.CompositeMode);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SolidBrush(new Rgba32(1, 2, 3)) { CompositeMode = (CompositeMode)12 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new SolidBrush(new Rgba32(1, 2, 3)) { CompositeMode = (CompositeMode)(-1) });

        // A gradient and the brush of a pen have one too: a row erased from opaque on its left to untouched on its right.
        //   The gradient has the alphas 32, 96, 159, 223 at the centers of the pixels (see the gradient tests), and the
        //   opaque white pixels keep 255 - alpha
        GradientStop[] stops = [new(0, new Rgba32(0, 0, 0, 0)), new(1, new Rgba32(0, 0, 0))];
        var eraser = new LinearGradientBrush(new PointD(0, 0), new PointD(4, 0), stops) { CompositeMode = CompositeMode.DestinationOut };
        using var image = new Image<Rgba32>(4, 3, new Rgba32(255, 255, 255));
        image.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(eraser, 1), new PointD(0, 1.5), new PointD(4, 1.5)), Ct);
        Assert.Equal(new[] { 223, 159, 96, 32 }, Enumerable.Range(0, 4).Select(x => (int)image.Frames[0][x, 1].A).ToArray());
        Assert.All(Enumerable.Range(0, 4), x => Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][x, 0]));
        Assert.All(Enumerable.Range(0, 4), x => Assert.Equal(new Rgba32(255, 255, 255, image.Frames[0][x, 1].A), image.Frames[0][x, 1]));
    }

    [Fact]
    public void ClearReplacesEveryPixelOfTheCanvas()
    {
        using (var image = new Image<Rgba32>(3, 2, new Rgba32(200, 17, 90, 250)))
        {
            image.Draw(canvas => canvas.Clear(new Rgba32(10, 20, 30, 100)), Ct);
            Assert.All(DrawingTestSupport.ReadSamples(image.Frames[0]).Chunk(4), pixel => Assert.Equal([10, 20, 30, 100], pixel));
            image.Draw(canvas => canvas.Clear(Colors.Transparent), Ct);
            Assert.Equal(new int[24], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        using (var image = new Image<Rgba64>(1, 1, new Rgba64(1, 2, 3, 4)))
        {
            image.Draw(canvas => canvas.Clear(new Rgba64(1234, 5678, 9012, 3456)), Ct);
            Assert.Equal(new Rgba64(1234, 5678, 9012, 3456), image.Frames[0][0, 0]);
        }

        // A format without alpha gets the color multiplied by its alpha
        using (var image = new Image<Gray8>(2, 2, new Gray8(77)))
        {
            image.Draw(canvas => canvas.Clear(new Rgba32(255, 255, 255, 102)), Ct);
            Assert.Equal([102, 102, 102, 102], DrawingTestSupport.ReadSamples(image.Frames[0]));
            image.Draw(canvas => canvas.Clear(Colors.White), Ct);
            Assert.Equal([255, 255, 255, 255], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Blend modes. The colors as fractions of one: s (paint), d (pixel). The paint takes the color
    // (1 - ad) s + ad B(s, d), rounded to a sample, then it is composited as any paint.
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ABlendModeMixesEachChannelOfThePaintWithThePixel()
    {
        // (204, 102, 51) on an opaque (102, 153, 255), that is (0.8, 0.4, 0.2) on (0.4, 0.6, 1)
        var pixel = new Rgb24(102, 153, 255);
        var paint = new Rgba32(204, 102, 51);
        Assert.Equal(new Rgb24(204, 102, 51), Blend(pixel, paint, BlendMode.Normal));

        // s d: 81.6 | 61.2 | 51
        Assert.Equal(new Rgb24(82, 61, 51), Blend(pixel, paint, BlendMode.Multiply));

        // s + d - s d: 224.4 | 193.8 | 255
        Assert.Equal(new Rgb24(224, 194, 255), Blend(pixel, paint, BlendMode.Screen));

        // Multiply (2 s d) where the pixel is dark, screen (1 - 2 (1 - s)(1 - d)) where it is light:
        // 2 * 0.8 * 0.4 = 0.64 -> 163.2 | 1 - 2 * 0.6 * 0.4 = 0.52 -> 132.6 | 1
        Assert.Equal(new Rgb24(163, 133, 255), Blend(pixel, paint, BlendMode.Overlay));

        // The same with the paint and the pixel exchanged: 1 - 2 * 0.2 * 0.6 = 0.76 -> 193.8 | 0.48 -> 122.4 | 0.4 -> 102
        Assert.Equal(new Rgb24(194, 122, 102), Blend(pixel, paint, BlendMode.HardLight));
        Assert.Equal(new Rgb24(102, 102, 51), Blend(pixel, paint, BlendMode.Darken));
        Assert.Equal(new Rgb24(204, 153, 255), Blend(pixel, paint, BlendMode.Lighten));

        // min(1, d / (1 - s)): 0.4 / 0.2 | 0.6 / 0.6 | 1 / 0.8, all at least 1
        Assert.Equal(new Rgb24(255, 255, 255), Blend(pixel, paint, BlendMode.ColorDodge));

        // 1 - min(1, (1 - d) / s): 1 - 0.75 -> 63.75 | 1 - 1 | 1 - 0
        Assert.Equal(new Rgb24(64, 0, 255), Blend(pixel, paint, BlendMode.ColorBurn));

        // A light paint (s > 0.5) moves the pixel toward its square root: 0.4 + 0.6 (0.6325 - 0.4) -> 137.57;
        // a dark one darkens it: 0.6 - 0.2 * 0.6 * 0.4 = 0.552 -> 140.76; white stays white
        Assert.Equal(new Rgb24(138, 141, 255), Blend(pixel, paint, BlendMode.SoftLight));

        // |d - s| and s + d - 2 s d: 0.56 -> 142.8 | 0.52 -> 132.6 | 0.8 -> 204
        Assert.Equal(new Rgb24(102, 51, 204), Blend(pixel, paint, BlendMode.Difference));
        Assert.Equal(new Rgb24(143, 133, 204), Blend(pixel, paint, BlendMode.Exclusion));

        // What changes nothing, and what inverts
        var white = new Rgba32(255, 255, 255);
        var black = new Rgba32(0, 0, 0);
        Assert.Equal(pixel, Blend(pixel, white, BlendMode.Multiply));
        Assert.Equal(pixel, Blend(pixel, black, BlendMode.Screen));
        Assert.Equal(pixel, Blend(pixel, black, BlendMode.ColorDodge));
        Assert.Equal(pixel, Blend(pixel, white, BlendMode.ColorBurn));
        Assert.Equal(pixel, Blend(pixel, black, BlendMode.Difference));
        Assert.Equal(pixel, Blend(pixel, black, BlendMode.Lighten));
        Assert.Equal(pixel, Blend(pixel, white, BlendMode.Darken));
        Assert.Equal(new Rgb24(153, 102, 0), Blend(pixel, white, BlendMode.Difference));
        Assert.Equal(new Rgb24(0, 0, 0), Blend(pixel, black, BlendMode.Multiply));
        Assert.Equal(new Rgb24(255, 255, 255), Blend(pixel, white, BlendMode.Screen));

        // 16-bit samples keep their precision: 40000 * 20000 / 65535 = 12207.2, 60000 + 1000 - 60000 * 1000 / 65535 = 60084.4
        Assert.Equal(new Rgba64(12207, 12207, 12207), Blend(new Rgba64(20000, 20000, 20000), new Rgba64(40000, 40000, 40000), BlendMode.Multiply));
        Assert.Equal(new Rgba64(60084, 60084, 60084), Blend(new Rgba64(1000, 1000, 1000), new Rgba64(60000, 60000, 60000), BlendMode.Screen));

        // A gray level is one channel
        Assert.Equal(new Gray8(82), Blend(new Gray8(102), new Rgba32(204, 204, 204), BlendMode.Multiply));
        Assert.Equal(new Gray8(138), Blend(new Gray8(102), new Rgba32(204, 204, 204), BlendMode.SoftLight));
        Assert.Equal(new Gray16(12207), Blend(new Gray16(20000), new Rgba64(40000, 40000, 40000), BlendMode.Multiply));
    }

    [Fact]
    public void TheLastBlendModesTakeAColorAsAWhole()
    {
        // (200, 90, 40) on (120, 140, 160). The luminosity 0.3 R + 0.59 G + 0.11 B is 117.5 for the paint and 136.2 for the
        // pixel; the saturation, the largest channel less the smallest, is 160 and 40.
        var pixel = new Rgb24(120, 140, 160);
        var paint = new Rgba32(200, 90, 40);

        // The paint with the saturation of the pixel, (v - 40) * 40 / 160 = (40, 12.5, 0), whose luminosity is 19.375,
        // then with its luminosity: + 116.825
        Assert.Equal(new Rgb24(157, 129, 117), Blend(pixel, paint, BlendMode.Hue));

        // The pixel with the saturation of the paint, (v - 120) * 160 / 40 = (0, 80, 160), whose luminosity is 64.8,
        // then with its own luminosity: + 71.4
        Assert.Equal(new Rgb24(71, 151, 231), Blend(pixel, paint, BlendMode.Saturation));

        // The paint with the luminosity of the pixel, + 18.7, and the pixel with the luminosity of the paint, - 18.7
        Assert.Equal(new Rgb24(219, 109, 59), Blend(pixel, paint, BlendMode.Color));
        Assert.Equal(new Rgb24(101, 121, 141), Blend(pixel, paint, BlendMode.Luminosity));

        // A color that leaves the range is brought back around its luminosity: red with the luminosity of white is white
        Assert.Equal(new Rgb24(255, 255, 255), Blend(new Rgb24(255, 255, 255), new Rgba32(255, 0, 0), BlendMode.Color));
        Assert.Equal(new Rgb24(0, 0, 0), Blend(new Rgb24(0, 0, 0), new Rgba32(255, 0, 0), BlendMode.Hue));

        // Red, green and blue are what they are whatever the order of the samples
        Assert.Equal(new Rgba32(157, 129, 117), Blend(new Rgba32(120, 140, 160), paint, BlendMode.Hue));
        Assert.Equal(new Bgra32(157, 129, 117), Blend(new Bgra32(120, 140, 160), paint, BlendMode.Hue));
        Assert.Equal(new Bgra32(71, 151, 231), Blend(new Bgra32(120, 140, 160), paint, BlendMode.Saturation));
        Assert.Equal(new Bgra32(219, 109, 59), Blend(new Bgra32(120, 140, 160), paint, BlendMode.Color));
        Assert.Equal(new Bgra32(101, 121, 141), Blend(new Bgra32(120, 140, 160), paint, BlendMode.Luminosity));

        // Gray levels are colors without saturation: the luminosity of the paint replaces the pixel, and the other three
        // keep the luminosity of the pixel, which is all it has
        Assert.Equal(new Gray8(200), Blend(new Gray8(102), new Rgba32(200, 200, 200), BlendMode.Luminosity));
        Assert.Equal(new Gray8(102), Blend(new Gray8(102), new Rgba32(200, 200, 200), BlendMode.Hue));
        Assert.Equal(new Gray8(102), Blend(new Gray8(102), new Rgba32(200, 200, 200), BlendMode.Saturation));
        Assert.Equal(new Gray8(102), Blend(new Gray8(102), new Rgba32(200, 200, 200), BlendMode.Color));
    }

    [Fact]
    public void ABlendedPaintIsCompositedAsAnyPaint()
    {
        // (204, 102, 51) at 200/255 on half of a pixel (102, 153, 255) at 128/255. The paint first takes the color
        // (1 - ad) s + ad B(s, d) with ad = 128/255: for multiply, 0.498 * (204, 102, 51) + 0.502 * (81.6, 61.2, 51)
        // = (142.56, 81.52, 51) -> (143, 82, 51). It is then composited over the pixel with its alpha and the
        // coverage: as = 200/255 / 2, ao = as + ad (1 - as) = 0.6973 -> 178, each color (143 as + 102 ad (1 - as)) / ao
        var pixel = new Rgba32(102, 153, 255, 128);
        var paint = new Rgba32(204, 102, 51, 200);
        Assert.Equal(new Rgba32(125, 113, 140, 178), Paint(pixel, paint, HalfPixel, blend: BlendMode.Multiply));
        Assert.Equal(new Rgba32(165, 150, 198, 178), Paint(pixel, paint, HalfPixel, blend: BlendMode.Screen));
        Assert.Equal(new Rgba32(141, 135, 198, 178), Paint(pixel, paint, HalfPixel, blend: BlendMode.SoftLight));
        Assert.Equal(new Rgba32(174, 168, 198, 178), Paint(pixel, paint, HalfPixel, blend: BlendMode.ColorDodge));
        Assert.Equal(new Rgba32(124, 133, 191, 178), Paint(pixel, paint, HalfPixel, blend: BlendMode.Luminosity));

        // On a transparent pixel there is nothing to mix with: the paint keeps its color, and a transparent paint
        // paints nothing
        foreach (var mode in Enum.GetValues<BlendMode>())
        {
            Assert.Equal(new Rgba32(204, 102, 51, 200), Paint(new Rgba32(1, 2, 3, 0), paint, WholePixel, blend: mode));
            Assert.Equal(pixel, Paint(pixel, new Rgba32(204, 102, 51, 0), WholePixel, blend: mode));
            Assert.Equal(Paint(pixel, paint, HalfPixel), Paint(pixel, paint, HalfPixel, blend: BlendMode.Normal));
        }

        // The operator of the brush then combines the blended paint with the pixel: multiplied where the pixel is,
        // and nowhere else (the paint (143, 82, 51) at 200/255 atop the pixel)
        Assert.Equal(
            Paint(pixel, new Rgba32(143, 82, 51, 200), WholePixel, CompositeMode.SourceAtop),
            Paint(pixel, paint, WholePixel, CompositeMode.SourceAtop, BlendMode.Multiply));
        Assert.Equal(new Rgba32(1, 2, 3, 0), Paint(new Rgba32(1, 2, 3, 0), paint, WholePixel, CompositeMode.SourceAtop, BlendMode.Multiply));
    }

    [Theory]
    [MemberData(nameof(DrawingTestSupport.AllFormats), MemberType = typeof(DrawingTestSupport))]
    public void EveryBlendModeFollowsItsDefinitionInEveryPixelFormat(PixelFormat format)
    {
        // The definition written again with decimal arithmetic, on random pixels, paints and coverages; a sample may
        // differ by one unit where the two computations do not round the same way
        var layout = TestHarness.Drawing.ReferencePixelLayout.FromPixelFormat(format);
        var random = new Random(8800 + (int)format);
        foreach (var blend in Enum.GetValues<BlendMode>())
        {
            for (var iteration = 0; iteration < 12; iteration++)
            {
                using var image = DrawingTestSupport.CreateRandom(format, 4, 4, random.Next);
                var before = DrawingTestSupport.ReadSamples(image.Frames[0]);
                var color = new Rgba64((ushort)random.Next(65536), (ushort)random.Next(65536), (ushort)random.Next(65536), (ushort)(iteration % 3 == 0 ? 65535 : random.Next(65536)));
                var mode = iteration % 4 == 3 ? CompositeMode.SourceAtop : CompositeMode.SourceOver;
                var brush = new SolidBrush(color) { BlendMode = blend, CompositeMode = mode };

                // Whole pixels, and a column of half pixels
                image.Draw(canvas => canvas.FillRectangle(brush, new RectangleD(0, 0, 3.5, 4)), Ct);
                var after = DrawingTestSupport.ReadSamples(image.Frames[0]);
                var paint = DrawingTestSupport.ToPaint(color, format);
                var channels = layout.ChannelCount;
                for (var pixel = 0; pixel < 16; pixel++)
                {
                    var destination = before.AsSpan(pixel * channels, channels).ToArray();
                    var blended = BlendByDefinition(blend, paint, destination, layout);
                    var coverage = (pixel % 4) switch
                    {
                        3 => TestHarness.Drawing.ReferenceCoverage.FullCoverage / 2,
                        _ => TestHarness.Drawing.ReferenceCoverage.FullCoverage,
                    };
                    var expected = mode == CompositeMode.SourceOver
                        ? TestHarness.Drawing.ReferenceCompositor.Blend(destination, blended, coverage, layout)
                        : TestHarness.Drawing.ReferenceCompositor.Blend(destination, blended, coverage, layout, TestHarness.Drawing.ReferenceCompositeMode.SourceAtop);
                    var actual = after.AsSpan(pixel * channels, channels).ToArray();
                    for (var c = 0; c < channels; c++)
                    {
                        Assert.True(Math.Abs(expected[c] - actual[c]) <= 1, $"{blend} with {mode}, iteration {iteration}, pixel {pixel}: paint [{string.Join(' ', paint.Color)}] {paint.Alpha} on [{string.Join(' ', destination)}], expected [{string.Join(' ', expected)}], actual [{string.Join(' ', actual)}]");
                    }
                }
            }
        }

        static TestHarness.Drawing.ReferencePaint BlendByDefinition(BlendMode blend, TestHarness.Drawing.ReferencePaint paint, int[] destination, TestHarness.Drawing.ReferencePixelLayout layout)
        {
            decimal max = layout.MaxValue;
            var backdrop = layout.HasAlpha ? destination[layout.ColorChannels] / max : 1;
            if (paint.Alpha == 0 || backdrop == 0)
                return paint;

            // Red, green and blue as fractions of one; a gray level three times
            var red = layout.ColorChannels == 1 ? 0 : layout.BlueFirst ? 2 : 0;
            var green = layout.ColorChannels == 1 ? 0 : 1;
            var blue = layout.ColorChannels == 1 ? 0 : 2 - red;
            decimal[] s = [paint.Color[red] / max, paint.Color[green] / max, paint.Color[blue] / max];
            decimal[] d = [destination[red] / max, destination[green] / max, destination[blue] / max];
            var b = blend switch
            {
                BlendMode.Hue => SetLuminosity(SetSaturation(s, Saturation(d)), Luminosity(d)),
                BlendMode.Saturation => SetLuminosity(SetSaturation(d, Saturation(s)), Luminosity(d)),
                BlendMode.Color => SetLuminosity(s, Luminosity(d)),
                BlendMode.Luminosity => SetLuminosity(d, Luminosity(s)),
                _ => [BlendChannel(blend, s[0], d[0]), BlendChannel(blend, s[1], d[1]), BlendChannel(blend, s[2], d[2])],
            };

            var color = new int[layout.ColorChannels];
            foreach (var (index, channel) in new[] { (red, 0), (green, 1), (blue, 2) })
            {
                color[index] = (int)Math.Clamp(Math.Floor(((((1 - backdrop) * s[channel]) + (backdrop * b[channel])) * max) + 0.5m), 0, max);
            }

            return new TestHarness.Drawing.ReferencePaint(color, paint.Alpha);
        }

        static decimal BlendChannel(BlendMode blend, decimal s, decimal d) => blend switch
        {
            BlendMode.Normal => s,
            BlendMode.Multiply => s * d,
            BlendMode.Screen => s + d - (s * d),
            BlendMode.Overlay => d <= 0.5m ? 2 * s * d : 1 - (2 * (1 - s) * (1 - d)),
            BlendMode.HardLight => s <= 0.5m ? 2 * s * d : 1 - (2 * (1 - s) * (1 - d)),
            BlendMode.Darken => Math.Min(s, d),
            BlendMode.Lighten => Math.Max(s, d),
            BlendMode.ColorDodge => d == 0 ? 0 : s == 1 ? 1 : Math.Min(1, d / (1 - s)),
            BlendMode.ColorBurn => d == 1 ? 1 : s == 0 ? 0 : 1 - Math.Min(1, (1 - d) / s),
            BlendMode.SoftLight => s <= 0.5m
                ? d - ((1 - (2 * s)) * d * (1 - d))
                : d + (((2 * s) - 1) * ((d <= 0.25m ? ((((16 * d) - 12) * d) + 4) * d : (decimal)Math.Sqrt((double)d)) - d)),
            BlendMode.Difference => Math.Abs(s - d),
            _ => s + d - (2 * s * d),
        };

        static decimal Luminosity(decimal[] color) => (0.3m * color[0]) + (0.59m * color[1]) + (0.11m * color[2]);

        static decimal Saturation(decimal[] color) => color.Max() - color.Min();

        static decimal[] SetSaturation(decimal[] color, decimal saturation)
        {
            var min = color.Min();
            var max = color.Max();
            return [.. color.Select(value => max > min ? (value - min) * saturation / (max - min) : 0)];
        }

        static decimal[] SetLuminosity(decimal[] color, decimal luminosity)
        {
            var shift = luminosity - Luminosity(color);
            decimal[] result = [.. color.Select(value => value + shift)];
            var l = Luminosity(result);
            var min = result.Min();
            var max = result.Max();
            if (min < 0)
            {
                result = [.. result.Select(value => l + ((value - l) * l / (l - min)))];
            }

            if (max > 1)
            {
                result = [.. result.Select(value => l + ((value - l) * (1 - l) / (max - l)))];
            }

            return result;
        }
    }

    [Fact]
    public void TheOpacityOfALayerIsTheCoverageOfWhatItsOperatorMakes()
    {
        // With an operator other than source over, a layer at an opacity o gives o (the layer combined with the canvas)
        // + (1 - o) (the canvas), as the edge of a shape does: opaque black added to an alpha of 0.6 at an opacity of
        // 0.6 is 0.6 + 0.4 0.6 = 0.84 (214). The group opacity of CSS fades the layer first: 0.6 + 0.6, at most 1. That
        // is a faded layer inside a layer that is combined.
        using var image = new Image<Rgba32>(2, 1, new Rgba32(0, 0, 0, 153));
        image.Draw(canvas => canvas.Layer(0.6, static layer => layer.Fill(new Rgba32(0, 0, 0)), CompositeMode.Plus), Ct);
        Assert.Equal(214, image.Frames[0][0, 0].A);
        image.Draw(canvas => canvas.Clear(new Rgba32(0, 0, 0, 153)), Ct);
        image.Draw(canvas => canvas.Layer(1, static outer => outer.Layer(0.6, static layer => layer.Fill(new Rgba32(0, 0, 0))), CompositeMode.Plus), Ct);
        Assert.Equal(255, image.Frames[0][0, 0].A);

        // A mask at half its opacity keeps half of what it does not cover, and all of what it covers; a faded mask keeps
        // half of what it covers, and nothing else
        image.Draw(canvas => canvas.Clear(new Rgba32(0, 0, 0)), Ct);
        image.Draw(canvas => canvas.Layer(0.5, static layer => layer.FillRectangle(new Rgba32(0, 0, 0), new Rectangle(0, 0, 1, 1)), CompositeMode.DestinationIn), Ct);
        Assert.Equal([255, 128], [image.Frames[0][0, 0].A, image.Frames[0][1, 0].A]);
        image.Draw(canvas => canvas.Clear(new Rgba32(0, 0, 0)), Ct);
        image.Draw(canvas => canvas.Layer(1, static outer => outer.Layer(0.5, static layer => layer.FillRectangle(new Rgba32(0, 0, 0), new Rectangle(0, 0, 1, 1))), CompositeMode.DestinationIn), Ct);
        Assert.Equal([128, 0], [image.Frames[0][0, 0].A, image.Frames[0][1, 0].A]);
    }

    [Fact]
    public void TheBlendModeIsAPropertyOfTheBrushAndOfALayer()
    {
        Assert.Equal(BlendMode.Normal, new SolidBrush(new Rgba32(1, 2, 3)).BlendMode);
        Assert.Equal(BlendMode.Screen, new SolidBrush(new Rgba32(1, 2, 3)) { BlendMode = BlendMode.Screen }.BlendMode);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SolidBrush(new Rgba32(1, 2, 3)) { BlendMode = (BlendMode)16 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new SolidBrush(new Rgba32(1, 2, 3)) { BlendMode = (BlendMode)(-1) });

        // A gradient multiplies the pixels by its colors: from white, which changes nothing, to black (the parameters
        // of the four pixels are 1/8 to 7/8, and the gradient has the grays 223, 159, 96 and 32 there; 200 * 223 / 255
        // = 174.9, 124.7, 75.3 and 25.1)
        GradientStop[] stops = [new(0, new Rgba32(255, 255, 255)), new(1, new Rgba32(0, 0, 0))];
        var shade = new LinearGradientBrush(new PointD(0, 0), new PointD(4, 0), stops) { BlendMode = BlendMode.Multiply };
        using (var image = new Image<Gray8>(4, 1, new Gray8(200)))
        {
            image.Draw(canvas => canvas.Fill(shade), Ct);
            Assert.Equal([175, 125, 75, 25], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // An image is mixed the same way, and so is the brush of a pen
        using (var texture = new Image<Rgba32>(2, 1))
        using (var image = new Image<Rgb24>(2, 1, new Rgb24(102, 153, 255)))
        {
            texture.Frames[0][0, 0] = new Rgba32(204, 102, 51);
            texture.Frames[0][1, 0] = new Rgba32(255, 255, 255);
            image.Draw(canvas => canvas.Fill(new ImageBrush(texture.Frames[0]) { BlendMode = BlendMode.Multiply }), Ct);
            Assert.Equal(new Rgb24(82, 61, 51), image.Frames[0][0, 0]);
            Assert.Equal(new Rgb24(102, 153, 255), image.Frames[0][1, 0]);
            image.Draw(canvas => canvas.StrokeLine(new Pen(new SolidBrush(new Rgba32(255, 255, 255)) { BlendMode = BlendMode.Difference }, 1), new PointD(0, 0.5), new PointD(1, 0.5)), Ct);
            Assert.Equal(new Rgb24(173, 194, 204), image.Frames[0][0, 0]);
        }

        // A layer mixes its drawing as a whole: two shapes that overlap multiply the canvas once
        using (var image = new Image<Rgb24>(3, 1, new Rgb24(102, 153, 255)))
        {
            var paint = new SolidBrush(new Rgba32(204, 102, 51));
            image.Draw(
                canvas => canvas.Layer(
                    1,
                    layer =>
                    {
                        layer.FillRectangle(paint, new Rectangle(0, 0, 2, 1));
                        layer.FillRectangle(paint, new Rectangle(1, 0, 1, 1));
                    },
                    blendMode: BlendMode.Multiply),
                Ct);
            Assert.Equal([82, 61, 51, 82, 61, 51, 102, 153, 255], DrawingTestSupport.ReadSamples(image.Frames[0]));

            // The state overload, and both an operator and a blend mode
            image.Draw(canvas => canvas.Layer(1, paint, static (layer, paint) => layer.Fill(paint), CompositeMode.SourceAtop, BlendMode.Screen), Ct);
            Assert.Equal(new Rgb24(224, 194, 255), image.Frames[0][2, 0]);
            image.Draw(
                canvas =>
                {
                    try
                    {
                        canvas.Layer(1, static _ => Assert.Fail("The callback ran."), blendMode: (BlendMode)16);
                        Assert.Fail("The blend mode was accepted.");
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                    }
                },
                Ct);
        }
    }

    private static TPixel Blend<TPixel>(TPixel pixel, Rgba64 color, BlendMode blend)
        where TPixel : unmanaged
        => Paint(pixel, color, WholePixel, blend: blend);

    /// <summary>Fills a rectangle of a one-pixel image and returns the pixel.</summary>
    private static TPixel Paint<TPixel>(TPixel pixel, Rgba64 color, RectangleD rectangle, CompositeMode mode = CompositeMode.SourceOver, BlendMode blend = BlendMode.Normal)
        where TPixel : unmanaged
    {
        using var image = new Image<TPixel>(1, 1, pixel);
        var brush = new SolidBrush(color) { CompositeMode = mode, BlendMode = blend };
        image.Frames[0].Draw(canvas => canvas.FillRectangle(brush, rectangle), Ct);
        return image.Frames[0][0, 0];
    }
}
