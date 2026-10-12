using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Gradient brushes. Every expected sample is a literal derived by hand from the definition: the parameter of the
/// center of the pixel, the two stops around it, and the interpolation of alpha-premultiplied values (the derivation is
/// in the comments). The conformance tests add a rational reference over random gradients.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class DrawingGradientTests
{
    private static readonly GradientStop[] BlackToWhite = [new(0, new Rgba32(0, 0, 0)), new(1, new Rgba32(255, 255, 255))];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void ALinearGradientIsEvaluatedAtTheCenterOfEachPixel()
    {
        // From x = 0 to x = 4: the centers 0.5, 1.5, 2.5, 3.5 have the parameters 1/8, 3/8, 5/8, 7/8.
        //   255 / 8 = 31.875 -> 32 | 95.625 -> 96 | 159.375 -> 159 | 223.125 -> 223
        var brush = new LinearGradientBrush(new PointD(0, 0), new PointD(4, 0), BlackToWhite);
        AssertFill(4, 2, brush, [[32, 96, 159, 223], [32, 96, 159, 223]]);

        // Vertically, and from the end to the start
        var vertical = new LinearGradientBrush(new PointD(0, 4), new PointD(0, 0), BlackToWhite);
        AssertFill(1, 4, vertical, [[223], [159], [96], [32]]);

        // Along the diagonal of a 2x2 canvas, from (0,0) to (2,2): the parameter is (x + y) / 4 at the centers, that is
        // 1/4, 1/2, 1/2, 3/4 -> 63.75 -> 64 | 127.5 -> 128 (tie upward) | 191.25 -> 191
        var diagonal = new LinearGradientBrush(new PointD(0, 0), new PointD(2, 2), BlackToWhite);
        AssertFill(2, 2, diagonal, [[64, 128], [128, 191]]);
    }

    [Fact]
    public void ARadialGradientFollowsTheDistanceToItsCenter()
    {
        // Center (0.5, 0.5), radius 10. The center of the pixel (3, 4) is at (3, 4) from it: distance 5, parameter 1/2,
        // 127.5 -> 128. The pixel (0, 0) is at the center (0); (6, 8) is at distance 10 (255); (3, 0) at 3 -> 76.5 -> 77
        var brush = new RadialGradientBrush(new PointD(0.5, 0.5), 10, BlackToWhite);
        using var image = Fill(9, 9, brush);
        Assert.Equal(0, image.Frames[0][0, 0].Value);
        Assert.Equal(128, image.Frames[0][3, 4].Value);
        Assert.Equal(128, image.Frames[0][4, 3].Value);
        Assert.Equal(77, image.Frames[0][3, 0].Value);
        Assert.Equal(255, image.Frames[0][6, 8].Value);
        Assert.Equal(255, image.Frames[0][8, 8].Value);
    }

    [Fact]
    public void TheSpreadModeDecidesTheColorsOutsideTheGradient()
    {
        // From x = 0 to x = 2 on six pixels: the parameters are 0.25, 0.75, 1.25, 1.75, 2.25, 2.75.
        //   0.25 -> 63.75 -> 64 | 0.75 -> 191.25 -> 191
        (GradientSpreadMode Mode, int[] Expected)[] cases =
        [
            // Clamped to 1
            (GradientSpreadMode.Pad, [64, 191, 255, 255, 255, 255]),

            // The fractional part: 0.25, 0.75, 0.25, 0.75, 0.25, 0.75
            (GradientSpreadMode.Repeat, [64, 191, 64, 191, 64, 191]),

            // Mirrored: 0.25, 0.75, 0.75, 0.25, 0.25, 0.75
            (GradientSpreadMode.Reflect, [64, 191, 191, 64, 64, 191]),
        ];
        foreach (var (mode, expected) in cases)
        {
            var brush = new LinearGradientBrush(new PointD(0, 0), new PointD(2, 0), BlackToWhite) { SpreadMode = mode };
            AssertFill(6, 1, brush, [expected]);

            // The same before the start: from x = 4 to x = 6, the parameters are -1.75, -1.25, -0.75, -0.25, 0.25, 0.75.
            //   pad: 0 | repeat: 0.25, 0.75, 0.25, 0.75 | reflect: 0.25 (-1.75 + 2), 0.75, 0.75, 0.25
            int[] before = mode switch
            {
                GradientSpreadMode.Pad => [0, 0, 0, 0, 64, 191],
                GradientSpreadMode.Repeat => [64, 191, 64, 191, 64, 191],
                _ => [64, 191, 191, 64, 64, 191],
            };
            var shifted = new LinearGradientBrush(new PointD(4, 0), new PointD(6, 0), BlackToWhite) { SpreadMode = mode };
            AssertFill(6, 1, shifted, [before]);
        }
    }

    [Fact]
    public void StopsAreInterpolatedBetweenTheirOffsets()
    {
        // Black at 0, white at 0.25, black at 1, from x = 0 to x = 8. Parameters 1/16, 3/16, ..., 15/16.
        //   1/16: a quarter of the way to white -> 63.75 -> 64 | 3/16: three quarters -> 191.25 -> 191
        //   then from white (0.25) to black (1): u = (t - 0.25) / 0.75, 255 (1 - u):
        //   5/16 -> 233.75 -> 234 | 7/16 -> 191.25 -> 191 | 9/16 -> 148.75 -> 149 | 11/16 -> 106.25 -> 106
        //   13/16 -> 63.75 -> 64 | 15/16 -> 21.25 -> 21
        GradientStop[] stops = [new(0, new Rgba32(0, 0, 0)), new(0.25, new Rgba32(255, 255, 255)), new(1, new Rgba32(0, 0, 0))];
        AssertFill(8, 1, new LinearGradientBrush(new PointD(0, 0), new PointD(8, 0), stops), [[64, 191, 234, 191, 149, 106, 64, 21]]);

        // Before the first stop and after the last one, the color is constant: white from 0.25, black from 0.75
        GradientStop[] inner = [new(0.25, new Rgba32(255, 255, 255)), new(0.75, new Rgba32(0, 0, 0))];
        AssertFill(8, 1, new LinearGradientBrush(new PointD(0, 0), new PointD(8, 0), inner), [[255, 255, 223, 159, 96, 32, 0, 0]]);
    }

    [Fact]
    public void TwoStopsWithTheSameOffsetMakeAHardEdge()
    {
        // Black up to 0.5, white from 0.5: at the offset itself the later stop wins
        GradientStop[] stops = [new(0, new Rgba32(0, 0, 0)), new(0.5, new Rgba32(0, 0, 0)), new(0.5, new Rgba32(255, 255, 255)), new(1, new Rgba32(255, 255, 255))];
        AssertFill(4, 1, new LinearGradientBrush(new PointD(0, 0), new PointD(4, 0), stops), [[0, 0, 255, 255]]);

        // The center of the pixel 1 is exactly at 0.5 (from x = 0.5 to x = 2.5): white
        AssertFill(3, 1, new LinearGradientBrush(new PointD(0.5, 0), new PointD(2.5, 0), stops), [[0, 255, 255]]);
    }

    [Fact]
    public void ColorsAreInterpolatedPremultipliedByAlpha()
    {
        // Opaque red to transparent blue, from x = 0 to x = 2 on transparent pixels. At 1/4: alpha 191.25 -> 191 and
        // red = 255 * 255 * 0.75 / 191.25 = 255: the transparent blue stop does not tint its neighbors (a straight
        // interpolation would give (191, 0, 64)). At 3/4: alpha 63.75 -> 64, still pure red.
        GradientStop[] stops = [new(0, new Rgba32(255, 0, 0)), new(1, new Rgba32(0, 0, 255, 0))];
        var brush = new LinearGradientBrush(new PointD(0, 0), new PointD(2, 0), stops);
        using var image = new Image<Rgba32>(2, 1);
        image.Frames[0].Draw(canvas => canvas.Fill(brush), Ct);
        Assert.Equal(new Rgba32(255, 0, 0, 191), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(255, 0, 0, 64), image.Frames[0][1, 0]);

        // Two translucent colors: (200, 0, 0) at 100/255 to (0, 100, 0) at 200/255, at 1/2: alpha 150;
        // red = 200 * 100 * 0.5 / 150 = 66.67 -> 67; green = 100 * 200 * 0.5 / 150 = 66.67 -> 67
        GradientStop[] translucent = [new(0, new Rgba32(200, 0, 0, 100)), new(1, new Rgba32(0, 100, 0, 200))];
        using var single = new Image<Rgba32>(1, 1);
        single.Frames[0].Draw(canvas => canvas.Fill(new LinearGradientBrush(new PointD(0, 0), new PointD(1, 0), translucent)), Ct);
        Assert.Equal(new Rgba32(67, 67, 0, 150), single.Frames[0][0, 0]);
    }

    [Fact]
    public void ADegenerateGradientHasTheColorOfItsLastStop()
    {
        AssertFill(3, 1, new LinearGradientBrush(new PointD(1, 1), new PointD(1, 1), BlackToWhite), [[255, 255, 255]]);
        AssertFill(3, 1, new RadialGradientBrush(new PointD(1, 1), 0, BlackToWhite), [[255, 255, 255]]);
        AssertFill(3, 1, new LinearGradientBrush(new PointD(0, 0), new PointD(3, 0), [new GradientStop(0.5, new Rgba32(128, 128, 128))]), [[128, 128, 128]]);
    }

    [Fact]
    public void AGradientIsExpressedInCanvasCoordinatesWhateverTheShape()
    {
        // The gradient of the first test, seen through a rectangle that covers the pixels 1 and 2 only
        var brush = new LinearGradientBrush(new PointD(0, 0), new PointD(4, 0), BlackToWhite);
        using var image = new Image<Gray8>(4, 1, new Gray8(7));
        image.Frames[0].Draw(canvas => canvas.FillRectangle(brush, new Rectangle(1, 0, 2, 1)), Ct);
        AssertGray8(image, [[7, 96, 159, 7]]);
    }

    [Fact]
    public void AGradientIsWeightedByTheCoverage()
    {
        // White on the left half of each pixel of a black row: half of the gradient color.
        //   32 / 2 = 16 | 96 / 2 = 48 | 159 / 2 = 79.5 -> 80 | 223 / 2 = 111.5 -> 112
        var brush = new LinearGradientBrush(new PointD(0, 0), new PointD(4, 0), BlackToWhite);
        using var image = new Image<Gray8>(4, 1, new Gray8(0));
        image.Frames[0].Draw(canvas => canvas.FillRectangle(brush, new RectangleD(0, 0, 4, 0.5)), Ct);
        AssertGray8(image, [[16, 48, 80, 112]]);
    }

    [Fact]
    public void AStrokeCanBePaintedWithAGradient()
    {
        var brush = new LinearGradientBrush(new PointD(0, 0), new PointD(4, 0), BlackToWhite);
        using var image = new Image<Gray8>(4, 3, new Gray8(0));
        image.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(brush, 1), new PointD(0, 1.5), new PointD(4, 1.5)), Ct);
        AssertGray8(image, [[0, 0, 0, 0], [32, 96, 159, 223], [0, 0, 0, 0]]);
    }

    [Theory]
    [MemberData(nameof(DrawingTestSupport.AllFormats), MemberType = typeof(DrawingTestSupport))]
    public void AGradientOfIdenticalStopsPaintsWhatASolidBrushPaints(PixelFormat format)
    {
        var random = new Random(4321);
        for (var i = 0; i < 20; i++)
        {
            var color = new Rgba64((ushort)random.Next(65536), (ushort)random.Next(65536), (ushort)random.Next(65536), (ushort)random.Next(65536));
            var path = new VectorPathBuilder()
                .AddPolygon([new PointD(random.NextDouble() * 8, random.NextDouble() * 8), new PointD(random.NextDouble() * 8, random.NextDouble() * 8), new PointD(random.NextDouble() * 8, random.NextDouble() * 8)])
                .AddCircle(new PointD(4, 4), random.NextDouble() * 4)
                .Build();
            GradientStop[] stops = [new(0, color), new(random.NextDouble(), color), new(1, color)];
            Brush[] gradients =
            [
                new LinearGradientBrush(new PointD(1, 2), new PointD(6.5, 3.25), stops) { SpreadMode = GradientSpreadMode.Reflect },
                new RadialGradientBrush(new PointD(3.3, 4.1), 2.5, stops) { SpreadMode = GradientSpreadMode.Repeat },
            ];

            using var solid = DrawingTestSupport.CreateRandom(format, 8, 8, new Random(i).Next);
            solid.Frames[0].Draw(canvas => canvas.Fill(new SolidBrush(color), path), Ct);
            foreach (var gradient in gradients)
            {
                using var image = DrawingTestSupport.CreateRandom(format, 8, 8, new Random(i).Next);
                image.Frames[0].Draw(canvas => canvas.Fill(gradient, path), Ct);
                Assert.Equal(DrawingTestSupport.ReadSamples(solid.Frames[0]), DrawingTestSupport.ReadSamples(image.Frames[0]));
            }
        }
    }

    private static Image<Gray8> Fill(int width, int height, Brush brush)
    {
        var image = new Image<Gray8>(width, height, new Gray8(0));
        image.Frames[0].Draw(canvas => canvas.Fill(brush), Ct);
        return image;
    }

    private static void AssertFill(int width, int height, Brush brush, int[][] expected)
    {
        using var image = Fill(width, height, brush);
        AssertGray8(image, expected);
    }

    private static void AssertGray8(Image<Gray8> image, int[][] expected)
    {
        var frame = image.Frames[0];
        var actual = new int[expected.Length][];
        for (var y = 0; y < expected.Length; y++)
        {
            actual[y] = new int[expected[y].Length];
            for (var x = 0; x < expected[y].Length; x++)
            {
                actual[y][x] = frame[x, y].Value;
            }
        }

        Assert.Equal(Format(expected), Format(actual));
    }

    private static string Format(int[][] rows) => string.Join(" | ", rows.Select(row => string.Join(' ', row)));
}
