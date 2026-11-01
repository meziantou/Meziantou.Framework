using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;

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
    [MemberData(nameof(DrawingTestSupport.IntegerFormats), MemberType = typeof(DrawingTestSupport))]
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

            // An alpha mask has no color to blend
            if (paint.Alpha == 0 || backdrop == 0 || layout.ColorChannels == 0)
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
            // A gray color (l = min = max, to the rounding of the decimals) needs no clipping
            if (min < 0 && l != min)
            {
                result = [.. result.Select(value => l + ((value - l) * l / (l - min)))];
            }

            if (max > 1 && l != max)
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
    public void TheGroupOpacityOfALayerFadesItBeforeItsOperator()
    {
        // The alpha of the layer is multiplied by the opacity first, as the opacity of a brush: floor(255 0.6 + 1/2) =
        // 153, added to 153 is 306, at most 255; added to 51, it is 204
        var black = new Rgba32(0, 0, 0);
        using var image = new Image<Rgba32>(2, 1, new Rgba32(0, 0, 0, 153));
        image.Draw(canvas => canvas.Layer(0.6, static layer => layer.Fill(new Rgba32(0, 0, 0)), CompositeMode.Plus, BlendMode.Normal, LayerOpacity.Group), Ct);
        Assert.Equal(255, image.Frames[0][0, 0].A);
        image.Draw(canvas => canvas.Clear(new Rgba32(0, 0, 0, 51)), Ct);
        image.Draw(canvas => canvas.Layer(0.6, black, static (layer, black) => layer.Fill(black), CompositeMode.Plus, BlendMode.Normal, LayerOpacity.Group), Ct);
        Assert.Equal(204, image.Frames[0][0, 0].A);

        // A mask at half its opacity keeps half of what it covers (floor(255 0.5 + 1/2) = 128) and nothing else; at an
        // opacity of 0, it keeps nothing at all, where the coverage keeps everything
        image.Draw(canvas => canvas.Clear(black), Ct);
        image.Draw(canvas => canvas.Layer(0.5, static layer => layer.FillRectangle(new Rgba32(0, 0, 0), new Rectangle(0, 0, 1, 1)), CompositeMode.DestinationIn, BlendMode.Normal, LayerOpacity.Group), Ct);
        Assert.Equal([128, 0], [image.Frames[0][0, 0].A, image.Frames[0][1, 0].A]);
        image.Draw(canvas => canvas.Clear(black), Ct);
        image.Draw(canvas => canvas.Layer(0, static layer => layer.Fill(new Rgba32(0, 0, 0)), CompositeMode.DestinationIn, BlendMode.Normal, LayerOpacity.Coverage), Ct);
        Assert.Equal([255, 255], [image.Frames[0][0, 0].A, image.Frames[0][1, 0].A]);
        image.Draw(canvas => canvas.Layer(0, static layer => layer.Fill(new Rgba32(0, 0, 0)), CompositeMode.DestinationIn, BlendMode.Normal, LayerOpacity.Group), Ct);
        Assert.Equal([0, 0], [image.Frames[0][0, 0].A, image.Frames[0][1, 0].A]);

        // On 16-bit samples: floor(65535 0.6 + 1/2) = 39321, added to 13107 (0.2) is 52428
        using var wide = new Image<Rgba64>(1, 1, new Rgba64(0, 0, 0, 13107));
        wide.Draw(canvas => canvas.Layer(0.6, static layer => layer.Fill(new Rgba32(0, 0, 0)), CompositeMode.Plus, BlendMode.Normal, LayerOpacity.Group), Ct);
        Assert.Equal(52428, wide.Frames[0][0, 0].A);

        // A layer of a rectangle and a backdrop layer: opaque red erased by a backdrop that is itself, at half its
        // opacity, keeps 255 (1 - 128 / 255) = 127, where the coverage keeps 255 / 2, rounded up to 128
        var red = new Rgba32(255, 0, 0);
        using var bounded = new Image<Rgba32>(3, 1, red);
        bounded.Draw(canvas => canvas.Layer(new RectangleD(0, 0, 2, 1), 0.5, static layer => layer.Fill(new Rgba32(0, 0, 0)), CompositeMode.DestinationOut, BlendMode.Normal, LayerOpacity.Group), Ct);
        Assert.Equal([127, 127, 255], [bounded.Frames[0][0, 0].A, bounded.Frames[0][1, 0].A, bounded.Frames[0][2, 0].A]);
        bounded.Draw(canvas => canvas.Clear(red), Ct);
        bounded.Draw(canvas => canvas.Layer(new RectangleD(0, 0, 1, 1), 0.5, black, static (layer, black) => layer.Fill(black), CompositeMode.DestinationOut, BlendMode.Normal, LayerOpacity.Group), Ct);
        Assert.Equal([127, 255, 255], [bounded.Frames[0][0, 0].A, bounded.Frames[0][1, 0].A, bounded.Frames[0][2, 0].A]);
        bounded.Draw(canvas => canvas.Clear(red), Ct);
        bounded.Draw(canvas => canvas.BackdropLayer(null, new RectangleD(0, 0, 3, 1), 0.5, static _ => { }, CompositeMode.DestinationOut, BlendMode.Normal, LayerOpacity.Group), Ct);
        Assert.Equal(new Rgba32(255, 0, 0, 127), bounded.Frames[0][0, 0]);
        bounded.Draw(canvas => canvas.Clear(red), Ct);
        bounded.Draw(canvas => canvas.BackdropLayer(null, new RectangleD(0, 0, 3, 1), 0.5, 0, static (_, _) => { }, CompositeMode.DestinationOut, BlendMode.Normal, LayerOpacity.Group), Ct);
        Assert.Equal(new Rgba32(255, 0, 0, 127), bounded.Frames[0][0, 0]);
        bounded.Draw(canvas => canvas.Clear(red), Ct);
        bounded.Draw(canvas => canvas.BackdropLayer(null, new RectangleD(0, 0, 3, 1), 0.5, static _ => { }, CompositeMode.DestinationOut), Ct);
        Assert.Equal(new Rgba32(255, 0, 0, 128), bounded.Frames[0][0, 0]);

        image.Draw(
            canvas =>
            {
                try
                {
                    canvas.Layer(1, static _ => Assert.Fail("The callback ran."), CompositeMode.Plus, BlendMode.Normal, (LayerOpacity)2);
                    Assert.Fail("The opacity mode was accepted.");
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            },
            Ct);
    }

    [Theory]
    [MemberData(nameof(DrawingTestSupport.AllFormats), MemberType = typeof(DrawingTestSupport))]
    public void WithSourceOverTheGroupOpacityOfALayerIsItsCoverage(PixelFormat format)
    {
        // Fading the layer, then drawing it, is covering what it makes: the pixels are the same, whatever the blend mode
        var random = new Random(1789 + (int)format);
        var gradient = new LinearGradientBrush(new PointD(0, 0), new PointD(9, 7), [new GradientStop(0, new Rgba32(250, 20, 100, 220)), new GradientStop(1, new Rgba32(20, 200, 90, 70))]);
        foreach (var blend in new[] { BlendMode.Normal, BlendMode.Multiply, BlendMode.Luminosity })
        {
            using var expected = DrawingTestSupport.CreateRandom(format, 9, 7, random.Next);
            using var actual = expected.Clone();
            void Draw(DrawingCanvas canvas, LayerOpacity mode) => canvas.Layer(
                0.37,
                layer =>
                {
                    layer.Fill(gradient);
                    layer.FillCircle(new Rgba32(255, 255, 255, 140), new PointD(4.3, 3.1), 2.6);
                },
                CompositeMode.SourceOver,
                blend,
                mode);
            expected.Draw(canvas => Draw(canvas, LayerOpacity.Coverage), Ct);
            actual.Draw(canvas => Draw(canvas, LayerOpacity.Group), Ct);
            Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(actual.Frames[0]));
        }
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

    [Fact]
    public void ANonIsolatedGroupMixesWithItsBackdropAndIsCompositedOnce()
    {
        // On red, green multiplied by red is black: a non-isolated group at an opacity of 0.5 is half black over red,
        // (128, 0, 0); an isolated group multiplies green by its transparent backdrop, which keeps green: (128, 128, 0)
        var green = new SolidBrush(new Rgba32(0, 255, 0)) { BlendMode = BlendMode.Multiply };
        using var image = new Image<Rgba32>(2, 1, new Rgba32(255, 0, 0));
        image.Draw(canvas => canvas.Group(new GroupOptions { Opacity = 0.5, IsIsolated = false }, layer => layer.FillRectangle(green, new RectangleD(0, 0, 1, 1))), Ct);
        image.Draw(canvas => canvas.Group(new GroupOptions { Opacity = 0.5 }, layer => layer.FillRectangle(green, new RectangleD(1, 0, 1, 1))), Ct);
        AssertNear(image.Frames[0][0, 0], 128, 0, 0, 255);
        AssertNear(image.Frames[0][1, 0], 128, 128, 0, 255);

        // The backdrop is removed before the group is composited (ISO 32000-2, 11.4.8): a non-isolated group of a drawing
        // of the normal blend mode is that drawing, on a half-transparent backdrop too
        var blue = new SolidBrush(new Rgba32(0, 0, 255)) { Opacity = 0.5 };
        using var grouped = new Image<Rgba32>(1, 1, new Rgba32(255, 0, 0, 128));
        using var direct = new Image<Rgba32>(1, 1, new Rgba32(255, 0, 0, 128));
        grouped.Draw(canvas => canvas.Group(new GroupOptions { IsIsolated = false }, layer => layer.Fill(blue)), Ct);
        direct.Draw(canvas => canvas.Fill(blue), Ct);
        var expected = direct.Frames[0][0, 0];
        AssertNear(grouped.Frames[0][0, 0], expected.R, expected.G, expected.B, expected.A);
    }

    [Fact]
    public void TheElementsOfAKnockoutGroupKnockOutOneAnother()
    {
        // Red then blue, each at an opacity of 0.5, on white. In an isolated knockout group, blue replaces red where it
        // covers it: (128, 128, 255), where a group that is not knockout gives blue over red over white, (128, 64, 191)
        static void Draw(DrawingCanvas group)
        {
            group.KnockoutElement(0.5, static element => element.FillRectangle(new Rgba32(255, 0, 0), new RectangleD(0, 0, 2, 1)));
            group.KnockoutElement(0.5, static element => element.FillRectangle(new Rgba32(0, 0, 255), new RectangleD(1, 0, 2, 1)));
        }

        using var knockout = new Image<Rgba32>(3, 1, new Rgba32(255, 255, 255));
        knockout.Draw(canvas => canvas.Group(new GroupOptions { IsKnockout = true }, Draw), Ct);
        AssertNear(knockout.Frames[0][0, 0], 255, 128, 128, 255);
        AssertNear(knockout.Frames[0][1, 0], 128, 128, 255, 255);
        AssertNear(knockout.Frames[0][2, 0], 128, 128, 255, 255);

        using var normal = new Image<Rgba32>(3, 1, new Rgba32(255, 255, 255));
        normal.Draw(canvas => canvas.Group(new GroupOptions(), Draw), Ct);
        AssertNear(normal.Frames[0][1, 0], 128, 64, 191, 255);

        // In a non-isolated knockout group on yellow, each element is composited with yellow: blue over yellow,
        // (128, 128, 128), where it covers red over yellow, (255, 128, 0)
        using var yellow = new Image<Rgba32>(3, 1, new Rgba32(255, 255, 0));
        yellow.Draw(canvas => canvas.Group(new GroupOptions { IsIsolated = false, IsKnockout = true }, Draw), Ct);
        AssertNear(yellow.Frames[0][0, 0], 255, 128, 0, 255);
        AssertNear(yellow.Frames[0][1, 0], 128, 128, 128, 255);

        // With the group at an opacity of 0.5, the result of the group is half over yellow: (191, 191, 64)
        yellow.Draw(canvas => canvas.Clear(new Rgba32(255, 255, 0)), Ct);
        yellow.Draw(canvas => canvas.Group(new GroupOptions { IsIsolated = false, IsKnockout = true, Opacity = 0.5 }, Draw), Ct);
        AssertNear(yellow.Frames[0][1, 0], 191, 191, 64, 255);
    }

    [Fact]
    public void AnElementOutsideAKnockoutGroupIsDrawnWithItsOpacity()
    {
        // Red multiplied with green at an opacity of 0.5: the element is the drawing of the brush of that opacity
        using var element = new Image<Rgba32>(1, 1, new Rgba32(0, 255, 0));
        using var direct = new Image<Rgba32>(1, 1, new Rgba32(0, 255, 0));
        element.Draw(canvas => canvas.KnockoutElement(0.5, static layer => layer.Fill(new SolidBrush(new Rgba32(255, 0, 0)) { BlendMode = BlendMode.Multiply })), Ct);
        direct.Draw(canvas => canvas.Fill(new SolidBrush(new Rgba32(255, 0, 0)) { BlendMode = BlendMode.Multiply, Opacity = 0.5 }), Ct);
        var expected = direct.Frames[0][0, 0];
        AssertNear(element.Frames[0][0, 0], expected.R, expected.G, expected.B, expected.A);
        AssertNear(element.Frames[0][0, 0], 0, 128, 0, 255);
    }

    [Fact]
    public void TheMaskOfAGroupMultipliesItsAlpha()
    {
        // A luminance mask, white on the left pixel and gray (128) on the right one: red is drawn fully on the left,
        // at about half on the right, over white: (255, 127, 127)
        using var image = new Image<Rgba32>(2, 1, new Rgba32(255, 255, 255));
        image.Draw(
            canvas => canvas.Group(
                new GroupOptions { IsIsolated = false, MaskMode = MaskMode.Luminance },
                static content => content.Fill(new Rgba32(255, 0, 0)),
                static mask =>
                {
                    mask.FillRectangle(new Rgba32(255, 255, 255), new RectangleD(0, 0, 1, 1));
                    mask.FillRectangle(new Rgba32(128, 128, 128), new RectangleD(1, 0, 1, 1));
                }),
            Ct);
        AssertNear(image.Frames[0][0, 0], 255, 0, 0, 255);
        AssertNear(image.Frames[0][1, 0], 255, 127, 127, 255);

        // The state overloads draw the same
        image.Draw(canvas => canvas.Clear(new Rgba32(255, 255, 255)), Ct);
        image.Draw(
            canvas => canvas.Group(
                new GroupOptions { MaskMode = MaskMode.Alpha, Bounds = new RectangleD(0, 0, 1, 1) },
                new Rgba32(0, 0, 255),
                static (content, color) => content.Fill(color),
                static (mask, _) => mask.Fill(new Rgba32(0, 0, 0))),
            Ct);
        AssertNear(image.Frames[0][0, 0], 0, 0, 255, 255);
        AssertNear(image.Frames[0][1, 0], 255, 255, 255, 255);
    }

    [Fact]
    public void TheOptionsOfAGroupAreValidated()
    {
        GroupOptions[] invalid =
        [
            new GroupOptions { Opacity = 2 },
            new GroupOptions { BlendMode = (BlendMode)16 },
            new GroupOptions { MaskMode = (MaskMode)2 },
            new GroupOptions { Bounds = new RectangleD(double.NaN, 0, 1, 1) },
        ];
        using var image = new Image<Rgba32>(1, 1);
        foreach (var options in invalid)
        {
            image.Draw(
                canvas =>
                {
                    try
                    {
                        canvas.Group(options, static _ => Assert.Fail("The callback ran."));
                        Assert.Fail("The options were accepted.");
                    }
                    catch (ArgumentException)
                    {
                    }
                },
                Ct);
        }

        image.Draw(
            canvas =>
            {
                try
                {
                    canvas.KnockoutElement(-1, static _ => Assert.Fail("The callback ran."));
                    Assert.Fail("The opacity was accepted.");
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            },
            Ct);
    }

    [Fact]
    public void AFloatingPointCanvasCompositesInDoublePrecisionAndKeepsItsHighlights()
    {
        // Source-over with k the coverage, the paint (s, sa) as 16-bit fractions and the pixel (d, da) as stored:
        // S = k sa, D = (1 - k) da + k (1 - sa) da, A = S + D and c = (s S + d D) / A
        RgbaFloat[] pixels = [new(2.5f, -0.5f, 0.25f), new(0.5f, 0.5f, 0.5f, 0.5f), new(8, 8, 8), new(0, 0, 0, 0)];
        var paint = new Rgba64(65535, 0, 32768, 32768);
        using var image = new Image<RgbaFloat>(4, 1);
        for (var x = 0; x < pixels.Length; x++)
        {
            image.Frames[0][x, 0] = pixels[x];
        }

        image.Draw(canvas => canvas.FillRectangle(paint, new RectangleD(0, 0, 2.5, 1)), Ct);
        var (s, sa) = ((1.0, 0.0, 32768 / 65535.0), 32768 / 65535.0);
        for (var x = 0; x < 3; x++)
        {
            var k = x == 2 ? 0.5 : 1;
            var d = pixels[x];
            var sourceWeight = k * 1 * sa;
            var destinationWeight = ((1 - k) * d.A) + (k * (1 - sa) * d.A);
            var total = sourceWeight + destinationWeight;
            var expected = new RgbaFloat((float)(((s.Item1 * sourceWeight) + (d.R * destinationWeight)) / total), (float)(((s.Item2 * sourceWeight) + (d.G * destinationWeight)) / total), (float)(((s.Item3 * sourceWeight) + (d.B * destinationWeight)) / total), (float)total);
            Assert.Equal(expected, image.Frames[0][x, 0]);
        }

        // The highlight stays above the white (half covered by a paint of half opacity: 8 * 3/4), and the pixel the shape
        // does not reach is not written
        Assert.Equal(6f, image.Frames[0][2, 0].G, 1e-3f);
        Assert.Equal(new RgbaFloat(0, 0, 0, 0), image.Frames[0][3, 0]);

        // An opaque paint replaces a pixel covered entirely with its fractions, exactly
        image.Draw(canvas => canvas.FillRectangle(new Rgba64(1, 32768, 65535), new RectangleD(0, 0, 1, 1)), Ct);
        Assert.Equal(new RgbaFloat(1 / 65535f, 32768 / 65535f, 1), image.Frames[0][0, 0]);
    }

    [Fact]
    public void AHalfPrecisionCanvasStoresTheNearestHalf()
    {
        using var image = new Image<RgbaHalf>(2, 1, new RgbaHalf((Half)3, (Half)0.25, Half.Zero));
        image.Draw(canvas => canvas.FillRectangle(new Rgba64(0, 65535, 0, 16384), new RectangleD(0, 0, 1, 1)), Ct);
        var sa = 16384 / 65535.0;
        Assert.Equal(new RgbaHalf((Half)(3 * (1 - sa)), (Half)((sa + (0.25 * (1 - sa))) / 1), Half.Zero, Half.One), image.Frames[0][0, 0]);
        Assert.Equal(new RgbaHalf((Half)3, (Half)0.25, Half.Zero), image.Frames[0][1, 0]);
    }

    [Theory]
    [MemberData(nameof(DrawingTestSupport.FloatFormats), MemberType = typeof(DrawingTestSupport))]
    public void OnStandardRangePixelsAFloatingPointCanvasDrawsWhatASixteenBitCanvasDraws(PixelFormat format)
    {
        // Opaque pixels: the 16-bit canvas rounds each result, the floating-point one keeps it; both are within one unit
        var random = new Random(97 + (int)format);
        using var wide = new Image<Rgba64>(24, 16);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 24; x++)
            {
                wide.Frames[0][x, y] = new Rgba64((ushort)random.Next(65536), (ushort)random.Next(65536), (ushort)random.Next(65536));
            }
        }

        using var floats = format == PixelFormat.RgbaFloat ? (Image)wide.CloneAs<RgbaFloat>() : wide.CloneAs<RgbaHalf>();
        using var reference = floats.CloneAs<Rgba64>();
        var path = new VectorPathBuilder().MoveTo(1.25, 2.5).CubicBezierTo(9, -4, 18, 20, 22.75, 3.5).LineTo(12, 15.5).Close().AddCircle(new PointD(8, 9), 5.3).Build();
        var gradient = new LinearGradientBrush(new PointD(0, 0), new PointD(24, 16), [new GradientStop(0, new Rgba32(255, 40, 0, 200)), new GradientStop(1, new Rgba32(0, 90, 255, 120))]);
        void Draw(DrawingCanvas canvas)
        {
            canvas.Fill(gradient, path);
            canvas.FillEllipse(new Rgba64(12345, 54321, 999, 40000), new PointD(14, 6), 7.5, 4.25);
            canvas.Fill(new SolidBrush(new Rgba32(10, 200, 30, 90)) { CompositeMode = CompositeMode.SourceAtop, BlendMode = BlendMode.Multiply }, new VectorPathBuilder().AddCircle(new PointD(5, 5), 4.5).Build());
            canvas.StrokeLine(new Pen(new Rgba32(250, 250, 10, 180), 2.5), new PointD(0, 15), new PointD(23, 0.5));
        }

        reference.Draw(Draw, Ct);
        floats.Draw(Draw, Ct);
        using var result = format == PixelFormat.RgbaFloat ? ((Image<RgbaFloat>)floats).CloneAs<Rgba64>() : ((Image<RgbaHalf>)floats).CloneAs<Rgba64>();
        var tolerance = format == PixelFormat.RgbaFloat ? 1 : 32; // A half keeps 11 significant bits: 32 units of 65535 at the white
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 24; x++)
            {
                var (expected, actual) = (reference.Frames[0][x, y], result.Frames[0][x, y]);
                Assert.True(Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance && expected.A == actual.A, $"({x}, {y}): {expected} on the 16-bit canvas, {actual} on the {format} canvas");
            }
        }
    }

    [Fact]
    public void OnAFloatingPointCanvasTheOperatorsWorkOnTheValuesAndPlusAddsLight()
    {
        static RgbaFloat Paint(RgbaFloat pixel, Rgba64 color, CompositeMode mode, double width = 1)
        {
            using var image = new Image<RgbaFloat>(1, 1, pixel);
            var brush = new SolidBrush(color) { CompositeMode = mode };
            image.Draw(canvas => canvas.FillRectangle(brush, new RectangleD(0, 0, width, 1)), Ct);
            return image.Frames[0][0, 0];
        }

        // Plus adds the light without clipping it; the alpha is at most 1
        Assert.Equal(new RgbaFloat(2.75f, 1, 0.5f), Paint(new RgbaFloat(1.75f, 0, 0.5f), new Rgba64(65535, 65535, 0), CompositeMode.Plus));

        // The other operators: the factors of Porter and Duff, on the values as they are
        Assert.Equal(new RgbaFloat(0, 0, 0, 0), Paint(new RgbaFloat(5, 5, 5), new Rgba64(1, 2, 3), CompositeMode.Clear));
        Assert.Equal(new RgbaFloat(5, 4, 3), Paint(new RgbaFloat(5, 4, 3), new Rgba64(0, 0, 65535), CompositeMode.DestinationOver));
        Assert.Equal(new RgbaFloat(5, 4, 3, 0.5f), Paint(new RgbaFloat(5, 4, 3), new Rgba64(0, 0, 0, 32768), CompositeMode.DestinationIn), new FloatPixelComparer(1e-4f));
        Assert.Equal(new RgbaFloat(5, 4, 3, 0.5f), Paint(new RgbaFloat(5, 4, 3), new Rgba64(0, 0, 0, 65535), CompositeMode.DestinationOut, width: 0.5));

        // A transparent paint with source erases what it covers entirely
        Assert.Equal(new RgbaFloat(0, 0, 0, 0), Paint(new RgbaFloat(5, 4, 3), new Rgba64(0, 0, 0, 0), CompositeMode.Source));
    }

    [Fact]
    public void AColorMatrixOnAFloatingPointCanvasKeepsTheHighlights()
    {
        using var image = new Image<RgbaFloat>(3, 1, new RgbaFloat(4, -1, 0.5f, 0.75f));
        image.ApplyColorMatrix(new ColorMatrix([0.5, 0, 0, 0, 0, 0, 1, 0, 0, 0.25, 0, 0, 2, 0, 0, 0, 0, 0, 2, 0]), Ct);
        Assert.Equal(new RgbaFloat(2, -0.75f, 1, 1), image.Frames[0][0, 0]);

        // The identity changes nothing, bit for bit, values out of range included
        var before = DrawingTestSupport.ReadSamples(image.Frames[0]);
        image.ApplyColorMatrix(ColorMatrix.Identity, Ct);
        Assert.Equal(before, DrawingTestSupport.ReadSamples(image.Frames[0]));

        // A pixel the clip covers in part is replaced in part, as with the source operator
        using var clipped = new Image<RgbaFloat>(1, 1, new RgbaFloat(4, 2, 0, 1));
        clipped.Draw(canvas => canvas.Clip(new RectangleD(0, 0, 0.5, 1), inner => inner.FilterBackdrop(new ColorMatrix([0.5, 0, 0, 0, 0, 0, 0.5, 0, 0, 0, 0, 0, 0.5, 0, 0, 0, 0, 0, 1, 0]))), Ct);
        Assert.Equal(new RgbaFloat(3, 1.5f, 0, 1), clipped.Frames[0][0, 0]);
    }

    [Fact]
    public void LinearLightOnAFloatingPointCanvasDecodesOnlyEncodedSamples()
    {
        // An image labeled as linear light is combined as it is stored, whatever the range of its samples
        using var linear = new Image<RgbaFloat>(1, 1, new RgbaFloat(3, 0, 0));
        using var plain = linear.Clone();
        void Half(DrawingCanvas canvas) => canvas.FillRectangle(new Rgba64(0, 65535, 0), new RectangleD(0, 0, 0.5, 1));
        linear.Draw(canvas => canvas.Compositing(CompositingSpace.LinearSrgb, Half), Ct);
        plain.Draw(Half, Ct);
        Assert.Equal(new RgbaFloat(1.5f, 0.5f, 0), linear.Frames[0][0, 0]);
        Assert.Equal(plain.Frames[0][0, 0], linear.Frames[0][0, 0]);

        // Encoded sRGB samples are decoded and encoded again: half of white over black is the sRGB value of 0.5
        using var encoded = new Image<RgbaFloat>(1, 1, new RgbaFloat(0, 0, 0));
        encoded.Metadata.TransferFunction = Metadata.ColorTransferFunction.Srgb;
        encoded.Draw(canvas => canvas.Compositing(CompositingSpace.LinearSrgb, inner => inner.FillRectangle(new Rgba64(65535, 65535, 65535), new RectangleD(0, 0, 0.5, 1))), Ct);
        Assert.Equal(0.735357, encoded.Frames[0][0, 0].R, 1e-5);

        // An encoded sample outside [0, 1] has no linear value
        encoded.Frames[0][0, 0] = new RgbaFloat(2, 0, 0);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => encoded.Draw(canvas => canvas.Compositing(CompositingSpace.LinearSrgb, inner => inner.FillRectangle(new Rgba64(65535, 0, 0, 32768), new RectangleD(0, 0, 1, 1))), Ct));
        Assert.Contains("linear-light", exception.Message);
    }

    [Fact]
    public void FloatingPointSolidPaintsAndImagesKeepTheirSamples()
    {
        var color = new RgbaFloat(2, -1, 1e-7f, 1);
        using var source = new Image<RgbaFloat>(1, 1, color);
        using var target = new Image<RgbaFloat>(2, 1);
        target.Draw(canvas =>
        {
            canvas.FillRectangle(new SolidBrush(color), new RectangleD(0, 0, 1, 1));
            canvas.DrawImage(source.Frames[0], new PointD(1, 0));
        }, Ct);
        Assert.Equal(color, target.Frames[0][0, 0]);
        Assert.Equal(color, target.Frames[0][1, 0]);

        target.Draw(canvas => canvas.FillRectangle(new SolidBrush(new RgbaFloat(4, -2, 0, 0.5f)), new RectangleD(0, 0, 1, 1)), Ct);
        Assert.Equal(new RgbaFloat(3, -1.5f, 0.5e-7f, 1), target.Frames[0][0, 0]);

        using var translucent = new Image<RgbaFloat>(1, 1);
        translucent.Draw(canvas => canvas.FillRectangle(new SolidBrush(color) { Opacity = 0.25 }, WholePixel), Ct);
        Assert.Equal(new RgbaFloat(2, -1, 1e-7f, 0.25f), translucent.Frames[0][0, 0]);
    }

    [Theory]
    [InlineData(ImageSampling.Bilinear)]
    [InlineData(ImageSampling.Bicubic)]
    [InlineData(ImageSampling.Smooth)]
    [InlineData(ImageSampling.Box)]
    public void FloatingPointImageInterpolationKeepsExtendedAndSmallSamples(ImageSampling sampling)
    {
        using var source = new Image<RgbaFloat>(2, 1, new RgbaFloat(2, -1, 1e-7f, 1));
        source.Frames[0][1, 0] = new RgbaFloat(4, -3, 3e-7f, 1);
        using var target = new Image<RgbaFloat>(1, 1);
        target.Draw(canvas => canvas.DrawImage(source.Frames[0], new Rectangle(0, 0, 1, 1), sampling), Ct);
        var pixel = target.Frames[0][0, 0];
        Assert.Equal(3, pixel.R, 1e-6);
        Assert.Equal(-2, pixel.G, 1e-6);
        Assert.Equal(2e-7f, pixel.B, 1e-13);
        Assert.Equal(1, pixel.A, 1e-6);
    }

    [Fact]
    public void AFloatingPointImageIsDrawnThroughSixteenBitColors()
    {
        // In [0, 1], its samples are 16-bit colors like those of any image
        using var source = new Image<RgbaFloat>(1, 1, new RgbaFloat(1, 0.5f, 0));
        using var canvas = new Image<Rgba32>(1, 1);
        canvas.Draw(c => c.DrawImage(source.Frames[0], new Point(0, 0)), Ct);
        Assert.Equal(new Rgba32(255, 128, 0), canvas.Frames[0][0, 0]);

        // Beyond, they have none: they are rejected, never clipped
        source.Frames[0][0, 0] = new RgbaFloat(1.5f, 0.5f, 0);
        Assert.Throws<UnsupportedImageFeatureException>(() => canvas.Draw(c => c.DrawImage(source.Frames[0], new Point(0, 0)), Ct));
        using var floats = new Image<RgbaFloat>(1, 1);
        floats.Draw(c => c.DrawImage(source.Frames[0], new Point(0, 0)), Ct);
        Assert.Equal(source.Frames[0][0, 0], floats.Frames[0][0, 0]);
    }

    [Fact]
    public void AFloatingPointCanvasRejectsTheHighlightsItReadsBackAsSixteenBitColors()
    {
        RejectsHighlights<RgbaFloat>();
        RejectsHighlights<RgbaHalf>();

        static void RejectsHighlights<TPixel>()
            where TPixel : unmanaged
        {
            // In [0, 1], a backdrop is read as 16-bit colors
            using var image = new Image<TPixel>(2, 1);
            var bounds = new RectangleD(0, 0, 2, 1);
            image.Draw(canvas =>
            {
                canvas.Clear(new Rgba64(65535, 32768, 0));
                canvas.BackdropLayer(new BlurFilter(1), bounds, 1, static _ => { });
                canvas.Filter(new BlurFilter(1), inner => inner.FillRectangle(new SolidBrush(new Rgba64(0, 0, 65535)), bounds));
            }, Ct);

            // An additive composite keeps a highlight above 1, which has no 16-bit color: it is rejected, never clipped,
            // with a remedy that a drawing can apply
            var plus = new SolidBrush(new Rgba64(65535, 0, 0)) { CompositeMode = CompositeMode.Plus };
            image.Draw(canvas =>
            {
                canvas.Clear(new Rgba64(65535, 0, 0));
                canvas.FillRectangle(plus, bounds);
            }, Ct);
            Assert.Equal(2, ReadRed(image));
            var backdrop = Assert.Throws<UnsupportedImageFeatureException>(() => image.Draw(canvas => canvas.BackdropLayer(new BlurFilter(1), bounds, 1, static _ => { }), Ct));
            Assert.Equal(DrawingKernels.OutOfRangeColorFeature, backdrop.Feature);
            Assert.Contains($"Drawing reads the {typeof(TPixel).Name} pixels", backdrop.Message, StringComparison.Ordinal);
            Assert.Contains("Image.CloneAs<Rgba64>", backdrop.Message, StringComparison.Ordinal);
            var blur = Assert.Throws<UnsupportedImageFeatureException>(() => image.Draw(canvas => canvas.BlurBackdrop(1), Ct));
            Assert.Equal(DrawingKernels.OutOfRangeColorFeature, blur.Feature);

            // A filter reads only its layer, whose samples are 16-bit: it runs over the highlight
            image.Draw(canvas => canvas.Filter(new BlurFilter(1), inner => inner.FillRectangle(new SolidBrush(new Rgba64(0, 0, 65535, 0)), bounds)), Ct);

            // A color matrix applies to the stored values, in place: the highlight stays
            image.Draw(canvas => canvas.FilterBackdrop(ColorMatrix.Identity), Ct);
            Assert.Equal(2, ReadRed(image));
        }

        static double ReadRed<TPixel>(Image<TPixel> image)
            where TPixel : unmanaged
        {
            var pixel = image.Frames[0][0, 0];
            PixelConverter.ReadFloat(ref pixel, out var r, out _, out _, out _);
            return r;
        }
    }

    private sealed class FloatPixelComparer(float tolerance) : IEqualityComparer<RgbaFloat>
    {
        public bool Equals(RgbaFloat x, RgbaFloat y) => Math.Abs(x.R - y.R) <= tolerance && Math.Abs(x.G - y.G) <= tolerance && Math.Abs(x.B - y.B) <= tolerance && Math.Abs(x.A - y.A) <= tolerance;

        public int GetHashCode(RgbaFloat obj) => 0;
    }

    private static void AssertNear(Rgba32 actual, int r, int g, int b, int a)
    {
        Assert.True(Math.Abs(actual.R - r) <= 1 && Math.Abs(actual.G - g) <= 1 && Math.Abs(actual.B - b) <= 1 && Math.Abs(actual.A - a) <= 1, $"Expected ({r}, {g}, {b}, {a}), got ({actual.R}, {actual.G}, {actual.B}, {actual.A})");
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
