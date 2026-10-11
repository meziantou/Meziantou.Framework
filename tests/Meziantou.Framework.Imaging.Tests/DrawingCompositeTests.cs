using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Compositing of the paint over the pixels, and conversion of the paint to the pixel format. Every expected sample is a
/// literal derived by hand from the definition of source-over with straight alpha (the derivation is in the comments);
/// the coverages are exact fractions obtained with rectangles on halves and quarters of a pixel. The conformance tests
/// add a rational reference over random pixels, paints and coverages.
/// </summary>
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

    /// <summary>Fills a rectangle of a one-pixel image and returns the pixel.</summary>
    private static TPixel Paint<TPixel>(TPixel pixel, Rgba64 color, RectangleD rectangle)
        where TPixel : unmanaged
    {
        using var image = new Image<TPixel>(1, 1, pixel);
        var brush = new SolidBrush(color);
        image.Frames[0].Draw(canvas => canvas.FillRectangle(brush, rectangle), Ct);
        return image.Frames[0][0, 0];
    }
}
