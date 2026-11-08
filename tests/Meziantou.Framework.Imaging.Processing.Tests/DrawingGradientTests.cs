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
    public void ASweepGradientFollowsTheAngleAroundItsCenter()
    {
        // Around (2, 2) on a 4x4 canvas, from black to white over a full turn that starts on the right of the center and
        // goes clockwise on the image (toward the bottom first). The centers of the pixels are at (+-0.5 or +-1.5) from
        // the center on each axis; their angles, in degrees, and 255 * angle / 360:
        //   row 0: 225 -> 159.375 | 251.565 -> 178.19 | 288.435 -> 204.31 | 315 -> 223.125
        //   row 1: 198.435 -> 140.56 | 225 -> 159.375 | 315 -> 223.125 | 341.565 -> 241.94
        //   row 2: 161.565 -> 114.44 | 135 -> 95.625 | 45 -> 31.875 | 18.435 -> 13.06
        //   row 3: 135 -> 95.625 | 108.435 -> 76.81 | 71.565 -> 50.69 | 45 -> 31.875
        AssertFill(4, 4, new SweepGradientBrush(new PointD(2, 2), BlackToWhite), [[159, 178, 204, 223], [141, 159, 223, 242], [114, 96, 32, 13], [96, 77, 51, 32]]);

        // On a 2x2 canvas around (1, 1), the four pixels are at 225, 315 (top) and 135, 45 (bottom) degrees
        AssertFill(2, 2, new SweepGradientBrush(new PointD(1, 1), BlackToWhite), [[159, 223], [96, 32]]);

        // From 90 degrees (under the center): the angles become 135, 225 and 45, 315
        AssertFill(2, 2, new SweepGradientBrush(new PointD(1, 1), 90, 450, BlackToWhite), [[96, 159], [32, 223]]);

        // From -90 degrees (over the center): 315, 45 and 225, 135
        AssertFill(2, 2, new SweepGradientBrush(new PointD(1, 1), -90, 270, BlackToWhite), [[223, 32], [159, 96]]);

        // Over half a turn, the parameters are 1.25, 1.75 and 0.75, 0.25: the spread mode gives the colors beyond 1
        AssertFill(2, 2, new SweepGradientBrush(new PointD(1, 1), 0, 180, BlackToWhite), [[255, 255], [191, 64]]);
        AssertFill(2, 2, new SweepGradientBrush(new PointD(1, 1), 0, 180, BlackToWhite) { SpreadMode = GradientSpreadMode.Repeat }, [[64, 191], [191, 64]]);
        AssertFill(2, 2, new SweepGradientBrush(new PointD(1, 1), 0, 180, BlackToWhite) { SpreadMode = GradientSpreadMode.Reflect }, [[191, 64], [191, 64]]);

        // Over two turns, the parameters are half of what they are over one
        AssertFill(2, 2, new SweepGradientBrush(new PointD(1, 1), 0, 720, BlackToWhite), [[80, 112], [48, 16]]);

        // Without angle between its ends, the gradient has the color of its last stop
        AssertFill(2, 2, new SweepGradientBrush(new PointD(1, 1), 30, 30, BlackToWhite), [[255, 255], [255, 255]]);

        var brush = new SweepGradientBrush(new PointD(1, 2), 30, 90, BlackToWhite);
        Assert.Equal((new PointD(1, 2), 30d, 90d, 2), (brush.Center, brush.StartAngle, brush.EndAngle, brush.Stops.Length));
        Assert.Equal((0d, 360d), (new SweepGradientBrush(default, BlackToWhite).StartAngle, new SweepGradientBrush(default, BlackToWhite).EndAngle));
        Assert.Throws<ArgumentException>(() => new SweepGradientBrush(new PointD(double.NaN, 0), BlackToWhite));
        Assert.Throws<ArgumentException>(() => new SweepGradientBrush(default, double.NaN, 90, BlackToWhite));
        Assert.Throws<ArgumentException>(() => new SweepGradientBrush(default, 0, double.PositiveInfinity, BlackToWhite));
        Assert.Throws<ArgumentException>(() => new SweepGradientBrush(default, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SweepGradientBrush(default, 90, 89, BlackToWhite));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SweepGradientBrush(new PointD(16777217, 0), BlackToWhite));
    }

    [Fact]
    public void AGradientWithATransparentSpreadPaintsNothingOutsideItsRange()
    {
        // From x = 1 to x = 5, the centers of the pixels have the parameters (x - 1/2) / 4: -1/8 (nothing), 1/8, 3/8, 5/8,
        // 7/8, then 9/8 and 11/8 (nothing). The pixels that are not painted keep 50.
        Assert.Equal([50, 32, 96, 159, 223, 50, 50], Fill(new LinearGradientBrush(new PointD(1, 0), new PointD(5, 0), BlackToWhite) { SpreadMode = GradientSpreadMode.Transparent }));

        // Around (3.5, 0.5) up to 2: the parameters 3/2, 1, 1/2, 0, 1/2, 1, 3/2; 0 and 1 are in the range
        Assert.Equal([50, 255, 128, 0, 128, 255, 50], Fill(new RadialGradientBrush(new PointD(3.5, 0.5), 2, BlackToWhite) { SpreadMode = GradientSpreadMode.Transparent }));

        // A gradient of one stop is that color in its range only
        Assert.Equal([50, 100, 100, 100, 100, 50, 50], Fill(new LinearGradientBrush(new PointD(1, 0), new PointD(5, 0), [new GradientStop(0.5, new Rgba32(100, 100, 100))]) { SpreadMode = GradientSpreadMode.Transparent }));

        // The same in the other color spaces
        Assert.Equal([50, 0, 0, 0, 0, 50, 50], Fill(new LinearGradientBrush(new PointD(1, 0), new PointD(5, 0), [new GradientStop(0, new Rgba32(0, 0, 0)), new GradientStop(1, new Rgba32(0, 0, 0))]) { SpreadMode = GradientSpreadMode.Transparent, ColorSpace = GradientColorSpace.Oklab }));

        static int[] Fill(Brush brush)
        {
            using var image = new Image<Gray8>(7, 1, new Gray8(50));
            image.Frames[0].Draw(canvas => canvas.Fill(brush), Ct);
            return DrawingTestSupport.ReadSamples(image.Frames[0]);
        }
    }

    [Fact]
    public void ARadialGradientSpreadsFromItsFocalPoint()
    {
        // The circle of center (4.5, 0.5) and radius 6 goes from x = -1.5 to x = 10.5 on the row; from the focal point
        // (2.5, 0.5), it is 4 away on the left and 8 away on the right. The centers of the pixels are at x - 2 from the
        // focal point: the parameter is (2 - x) / 4 on its left and (x - 2) / 8 on its right.
        //   x = 0: 1/2 -> 127.5 -> 128 | x = 1: 1/4 -> 63.75 -> 64 | x = 2: 0 | x = 3..10: (x - 2) / 8
        var brush = new RadialGradientBrush(new PointD(4.5, 0.5), 6, BlackToWhite) { FocalPoint = new PointD(2.5, 0.5) };
        AssertFill(11, 1, brush, [[128, 64, 0, 32, 64, 96, 128, 159, 191, 223, 255]]);
        Assert.Equal(new PointD(2.5, 0.5), brush.FocalPoint);
        Assert.Equal(new PointD(4.5, 0.5), brush.Center);

        // The focal point is the center unless it is set, and setting it to the center changes nothing
        var plain = new RadialGradientBrush(new PointD(3.3, 4.1), 5, BlackToWhite);
        Assert.Equal(plain.Center, plain.FocalPoint);
        using var expected = Fill(8, 8, plain);
        using var same = Fill(8, 8, new RadialGradientBrush(new PointD(3.3, 4.1), 5, BlackToWhite) { FocalPoint = new PointD(3.3, 4.1) });
        Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(same.Frames[0]));

        // The focal point can be anywhere: outside the circle, the gradient is a cone (see the tests of cones)
        Assert.Equal(new PointD(6, 4), new RadialGradientBrush(new PointD(4, 4), 2, BlackToWhite) { FocalPoint = new PointD(6, 4) }.FocalPoint);
        Assert.Equal(new PointD(4, 4.5), new RadialGradientBrush(new PointD(4, 4), 0, BlackToWhite) { FocalPoint = new PointD(4, 4.5) }.FocalPoint);
        Assert.Throws<ArgumentException>(() => new RadialGradientBrush(new PointD(4, 4), 2, BlackToWhite) { FocalPoint = new PointD(double.NaN, 4) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new RadialGradientBrush(new PointD(4, 4), 2, BlackToWhite) { FocalPoint = new PointD(16777217, 4) });
        Assert.Equal(new PointD(4, 4), new RadialGradientBrush(new PointD(4, 4), 0, BlackToWhite) { FocalPoint = new PointD(4, 4) }.FocalPoint);
    }

    [Fact]
    public void ARadialGradientStartsOnTheCircleOfItsFocalRadius()
    {
        // Around the same point, the gradient goes from the circle of radius 2 to the circle of radius 10: the centers
        // of the pixels of the row are at x + 1/2 - 1/2 = x from the center, and the parameter is (x - 2) / 8.
        //   x = 0, 1: inside the first circle, padded to 0 | x = 2: 0 | x = 3: 1/8 -> 31.875 -> 32 | x = 6: 1/2 -> 128
        var brush = new RadialGradientBrush(new PointD(0.5, 0.5), 10, BlackToWhite) { FocalRadius = 2 };
        AssertFill(12, 1, brush, [[0, 0, 0, 32, 64, 96, 128, 159, 191, 223, 255, 255]]);
        Assert.Equal(2, brush.FocalRadius);
        Assert.Equal(0, new RadialGradientBrush(new PointD(0.5, 0.5), 10, BlackToWhite).FocalRadius);

        // Inside the first circle the parameter is negative: the gradient goes on with the other spread modes
        //   x = 1: -1/8, reflected to 1/8 -> 32, repeated to 7/8 -> 223 | x = 0: -1/4 -> 64, or 3/4 -> 191
        AssertFill(4, 1, new RadialGradientBrush(new PointD(0.5, 0.5), 10, BlackToWhite) { FocalRadius = 2, SpreadMode = GradientSpreadMode.Reflect }, [[64, 32, 0, 32]]);
        AssertFill(4, 1, new RadialGradientBrush(new PointD(0.5, 0.5), 10, BlackToWhite) { FocalRadius = 2, SpreadMode = GradientSpreadMode.Repeat }, [[191, 223, 0, 32]]);

        // The circle of the focal point can be anywhere, in whichever order they are set; its radius is a length
        Assert.Equal(2, new RadialGradientBrush(new PointD(4, 4), 2, BlackToWhite) { FocalRadius = 2 }.FocalRadius);
        Assert.Equal(new PointD(7, 4), new RadialGradientBrush(new PointD(4, 4), 5, BlackToWhite) { FocalRadius = 2, FocalPoint = new PointD(7, 4) }.FocalPoint);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RadialGradientBrush(new PointD(4, 4), 2, BlackToWhite) { FocalRadius = -1 });
        Assert.Throws<ArgumentException>(() => new RadialGradientBrush(new PointD(4, 4), 5, BlackToWhite) { FocalRadius = double.NaN });
        Assert.Equal(1.5, new RadialGradientBrush(new PointD(4, 4), 5, BlackToWhite) { FocalRadius = 1.5, FocalPoint = new PointD(7, 4) }.FocalRadius);
        Assert.Equal(0, new RadialGradientBrush(new PointD(4, 4), 0, BlackToWhite) { FocalRadius = 0 }.FocalRadius);
    }

    [Fact]
    public void APositionOfAGradientBetweenTwoCirclesIsOnTheCircleOfItsParameter()
    {
        // The definition: the position of parameter t is on the circle centered t of the way from the focal point to the
        // center, whose radius goes from the focal radius to the radius. A black to white gradient on 16-bit gray pixels
        // gives t within 1/131070.
        GradientStop[] stops = [new(0, new Rgba64(0, 0, 0)), new(1, new Rgba64(65535, 65535, 65535))];
        var random = new Random(97531);
        for (var index = 0; index < 40; index++)
        {
            var center = new PointD(random.NextDouble() * 16, random.NextDouble() * 16);
            var radius = 4 + (random.NextDouble() * 16);
            var focalRadius = (0.05 + (random.NextDouble() * 0.5)) * radius;
            var angle = random.NextDouble() * 2 * Math.PI;
            var distance = random.NextDouble() * 0.9 * (radius - focalRadius);
            var focal = new PointD(center.X + (distance * Math.Cos(angle)), center.Y + (distance * Math.Sin(angle)));
            var brush = new RadialGradientBrush(center, radius, stops) { FocalPoint = focal, FocalRadius = focalRadius };
            using var image = new Image<Gray16>(16, 16, new Gray16(0));
            image.Frames[0].Draw(canvas => canvas.Fill(brush), Ct);
            var between = 0;
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    var value = image.Frames[0][x, y].Value;
                    var position = new PointD(x + 0.5, y + 0.5);
                    if (value == 65535)
                    {
                        // Clamped: on the circle of the gradient or outside it
                        Assert.True(position.DistanceTo(center) >= radius - 1e-3, $"case {index}, pixel ({x}, {y})");
                        continue;
                    }

                    if (value == 0)
                    {
                        // Clamped: on the circle of the focal point or inside it
                        Assert.True(position.DistanceTo(focal) <= focalRadius + 1e-3, $"case {index}, pixel ({x}, {y})");
                        continue;
                    }

                    var t = value / 65535.0;
                    var circleCenter = focal + ((center - focal) * t);
                    var circleRadius = focalRadius + (t * (radius - focalRadius));
                    Assert.True(Math.Abs(position.DistanceTo(circleCenter) - circleRadius) <= 1e-3, $"case {index}, pixel ({x}, {y}): t {t}, {position.DistanceTo(circleCenter)} from the center of a circle of radius {circleRadius}");
                    between++;
                }
            }

            Assert.True(between > 0, $"case {index}");
        }
    }

    [Fact]
    public void TheCirclesOfTwoCirclesThatAreNotInsideEachOtherMakeACone()
    {
        // From the point (0.5, 5.5) to the circle of center (10.5, 5.5) and radius 2: on the row of the axis, the
        // position x from the point is on the circles of the parameters x / 8 and x / 12 (with e = (10, 0) and g = 2,
        // a = 96, b = 10 x, c = x^2, the roots are (10 x +- 2 x) / 96), and the larger one is the parameter. Outside the
        // cone, a pixel has no color: it keeps the gray 50.
        var brush = new RadialGradientBrush(new PointD(10.5, 5.5), 2, BlackToWhite) { FocalPoint = new PointD(0.5, 5.5) };
        using var image = new Image<Gray8>(12, 11, new Gray8(50));
        image.Frames[0].Draw(canvas => canvas.Fill(brush), Ct);
        var frame = image.Frames[0];
        Assert.Equal(new[] { 0, 32, 64, 96, 128, 159, 191, 223, 255, 255, 255, 255 }, Enumerable.Range(0, 12).Select(x => (int)frame[x, 5].Value).ToArray());
        Assert.Equal(new Gray8(50), frame[1, 0]);
        Assert.Equal(new Gray8(50), frame[0, 4]);
        Assert.Equal(new Gray8(50), frame[0, 6]);

        // The same with the repeated spread: the cone only, the parameters beyond 1 repeat
        using var repeated = new Image<Gray8>(12, 1, new Gray8(50));
        repeated.Frames[0].Draw(canvas => canvas.Fill(new RadialGradientBrush(new PointD(10.5, 0.5), 2, BlackToWhite) { FocalPoint = new PointD(0.5, 0.5), SpreadMode = GradientSpreadMode.Repeat }), Ct);
        Assert.Equal(new[] { 0, 32, 64, 96, 128, 159, 191, 223, 0, 32, 64, 96 }, Enumerable.Range(0, 12).Select(x => (int)repeated.Frames[0][x, 0].Value).ToArray());

        // The same circle twice: the first stop strictly inside, the last one elsewhere, and nothing without padding.
        // From the center (3.5, 0.5), the pixels 2, 3 and 4 are less than 2 away.
        AssertFill(7, 1, new RadialGradientBrush(new PointD(3.5, 0.5), 2, [new GradientStop(0, new Rgba32(100, 100, 100)), new GradientStop(1, new Rgba32(255, 255, 255))]) { FocalRadius = 2 }, [[255, 255, 100, 100, 100, 255, 255]]);
        AssertFill(7, 1, new RadialGradientBrush(new PointD(3.5, 0.5), 2, BlackToWhite) { FocalRadius = 2, SpreadMode = GradientSpreadMode.Reflect }, [[0, 0, 0, 0, 0, 0, 0]]);

        // Two circles touching inside each other at the left of both: a = 0, one root c / (2 b). From the first circle
        // (center (2.5, 0.5), radius 2) to the second one (center (4.5, 0.5), radius 4), the position x - 2 from the
        // first center on the axis has b = 2 (x - 2) + 4 and c = (x - 2)^2 - 4: t = (x - 4) x / (4 x) = (x - 4) / 4 for
        // x > 0 (at x = 0, b = 0: no circle)
        using var touching = new Image<Gray8>(9, 1, new Gray8(50));
        touching.Frames[0].Draw(canvas => canvas.Fill(new RadialGradientBrush(new PointD(4.5, 0.5), 4, BlackToWhite) { FocalPoint = new PointD(2.5, 0.5), FocalRadius = 2 }), Ct);
        Assert.Equal(new[] { 50, 0, 0, 0, 0, 64, 128, 191, 255 }, Enumerable.Range(0, 9).Select(x => (int)touching.Frames[0][x, 0].Value).ToArray());
    }

    [Fact]
    public void APositionOfAConeIsOnTheCircleOfItsLargestParameter()
    {
        // Two circles anywhere: a painted position has the largest parameter whose circle goes through it with a radius
        // that is not negative, and a position that is not painted has none. A black to white gradient on 16-bit gray
        // pixels gives t within 1/131070; the pixels that are not painted keep 12345.
        GradientStop[] stops = [new(0, new Rgba64(0, 0, 0)), new(1, new Rgba64(65535, 65535, 65535))];
        var random = new Random(24680);
        var painted = 0;
        var unpainted = 0;
        for (var index = 0; index < 80; index++)
        {
            var center = new PointD(random.NextDouble() * 16, random.NextDouble() * 16);
            var radius = random.NextDouble() * 12;
            var focal = new PointD(random.NextDouble() * 16, random.NextDouble() * 16);
            var focalRadius = random.Next(4) == 0 ? 0 : random.NextDouble() * 12;
            var brush = new RadialGradientBrush(center, radius, stops) { FocalPoint = focal, FocalRadius = focalRadius };
            using var image = new Image<Gray16>(16, 16, new Gray16(12345));
            image.Frames[0].Draw(canvas => canvas.Fill(brush), Ct);
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    // |p - c(t)|^2 - r(t)^2, with c(t) = focal + t (center - focal) and r(t) = focalRadius + t (radius -
                    // focalRadius), is a polynomial of degree 2 in t: its three coefficients from its values at -1, 0 and 1
                    var position = new PointD(x + 0.5, y + 0.5);
                    var atZero = Gap(position, 0);
                    var quadratic = ((Gap(position, 1) + Gap(position, -1)) / 2) - atZero;
                    var linear = (Gap(position, 1) - Gap(position, -1)) / 2;
                    var scale = Math.Abs(quadratic) + Math.Abs(linear) + Math.Abs(atZero);
                    var discriminant = (linear * linear) - (4 * quadratic * atZero);
                    if (Math.Abs(quadratic) <= 1e-9 * scale || Math.Abs(discriminant) <= 1e-9 * ((linear * linear) + Math.Abs(4 * quadratic * atZero)))
                        continue;

                    // The parameters whose radius is not negative; a radius of almost zero is a tie that is not decided here
                    var parameters = new List<double>();
                    var isTie = false;
                    if (discriminant > 0)
                    {
                        foreach (var root in (double[])[(-linear + Math.Sqrt(discriminant)) / (2 * quadratic), (-linear - Math.Sqrt(discriminant)) / (2 * quadratic)])
                        {
                            var circleRadius = focalRadius + (root * (radius - focalRadius));
                            isTie |= Math.Abs(circleRadius) <= 1e-9 * (radius + focalRadius + 1);
                            if (circleRadius >= 0)
                            {
                                parameters.Add(root);
                            }
                        }
                    }

                    if (isTie)
                        continue;

                    var value = image.Frames[0][x, y].Value;
                    if (parameters.Count == 0)
                    {
                        Assert.Equal(12345, value, $"case {index}, pixel ({x}, {y}): painted without a parameter");
                        unpainted++;
                        continue;
                    }

                    var expected = Math.Clamp(parameters.Max(), 0, 1) * 65535;
                    Assert.True(Math.Abs(value - expected) <= 1, $"case {index}, pixel ({x}, {y}): {value}, expected {expected}");
                    painted++;
                }
            }

            double Gap(PointD position, double t)
            {
                var circleCenter = focal + ((center - focal) * t);
                var circleRadius = focalRadius + (t * (radius - focalRadius));
                var dx = position.X - circleCenter.X;
                var dy = position.Y - circleCenter.Y;
                return (dx * dx) + (dy * dy) - (circleRadius * circleRadius);
            }
        }

        Assert.True(painted > 1000 && unpainted > 1000, $"{painted} painted, {unpainted} not painted");
    }

    [Fact]
    public void APositionOfAFocalGradientIsOnTheCircleOfItsParameter()
    {
        // The definition: the position of parameter t is on the circle centered t of the way from the focal point to the
        // center, of radius t times the radius. A black to white gradient on 16-bit gray pixels gives t within 1/131070.
        GradientStop[] stops = [new(0, new Rgba64(0, 0, 0)), new(1, new Rgba64(65535, 65535, 65535))];
        var random = new Random(8642);
        for (var index = 0; index < 40; index++)
        {
            var center = new PointD(random.NextDouble() * 16, random.NextDouble() * 16);
            var radius = 2 + (random.NextDouble() * 18);
            var angle = random.NextDouble() * 2 * Math.PI;
            var distance = random.NextDouble() * 0.95 * radius;
            var focal = new PointD(center.X + (distance * Math.Cos(angle)), center.Y + (distance * Math.Sin(angle)));
            var brush = new RadialGradientBrush(center, radius, stops) { FocalPoint = focal };
            using var image = new Image<Gray16>(16, 16, new Gray16(0));
            image.Frames[0].Draw(canvas => canvas.Fill(brush), Ct);
            var inside = 0;
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    var value = image.Frames[0][x, y].Value;
                    var position = new PointD(x + 0.5, y + 0.5);
                    if (value == 65535)
                    {
                        // Clamped: on the circle of the gradient or outside it
                        Assert.True(position.DistanceTo(center) >= radius - 1e-3, $"case {index}, pixel ({x}, {y})");
                        continue;
                    }

                    var t = value / 65535.0;
                    var circleCenter = focal + ((center - focal) * t);
                    Assert.True(Math.Abs(position.DistanceTo(circleCenter) - (t * radius)) <= 1e-3, $"case {index}, pixel ({x}, {y}): t {t}, {position.DistanceTo(circleCenter)} from the center of a circle of radius {t * radius}");
                    inside++;
                }
            }

            Assert.True(inside > 0 || radius < 3, $"case {index}");
        }
    }

    [Fact]
    public void ATransformPlacesAGradientOnTheCanvas()
    {
        // A linear gradient of length 4 made twice as wide is the gradient of length 8
        using (var expected = Fill(8, 2, new LinearGradientBrush(new PointD(0, 0), new PointD(8, 0), BlackToWhite)))
        using (var actual = Fill(8, 2, new LinearGradientBrush(new PointD(0, 0), new PointD(4, 0), BlackToWhite) { Transform = AffineTransform.CreateScale(2, 1) }))
        {
            Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(actual.Frames[0]));
        }

        // Turned by a quarter turn, a horizontal gradient is a vertical one
        AssertFill(1, 4, new LinearGradientBrush(new PointD(0, 0), new PointD(4, 0), BlackToWhite) { Transform = AffineTransform.CreateRotation(90) }, [[32], [96], [159], [223]]);

        // Moved: the gradient from x = 0 to x = 4, two pixels to the right, starts at x = 2
        AssertFill(6, 1, new LinearGradientBrush(new PointD(0, 0), new PointD(4, 0), BlackToWhite) { Transform = AffineTransform.CreateTranslation(2, 0) }, [[0, 0, 32, 96, 159, 223]]);

        // A radial gradient of radius 2 made twice as wide around its center (4, 0.5) is an ellipse: on its row, the
        // parameter is |x + 0.5 - 4| / 4 instead of |x + 0.5 - 4| / 2
        AssertFill(8, 1, new RadialGradientBrush(new PointD(4, 0.5), 2, BlackToWhite), [[255, 255, 191, 64, 64, 191, 255, 255]]);
        AssertFill(8, 1, new RadialGradientBrush(new PointD(4, 0.5), 2, BlackToWhite) { Transform = AffineTransform.CreateScale(2, 1, new PointD(4, 0.5)) }, [[223, 159, 96, 32, 32, 96, 159, 223]]);

        // A sweep gradient turned by a quarter turn starts under its center
        AssertFill(2, 2, new SweepGradientBrush(new PointD(1, 1), BlackToWhite) { Transform = AffineTransform.CreateRotation(90, new PointD(1, 1)) }, [[96, 159], [32, 223]]);

        // A transformation that flattens the plane leaves the color of the last stop
        AssertFill(2, 2, new LinearGradientBrush(new PointD(0, 0), new PointD(4, 0), BlackToWhite) { Transform = AffineTransform.CreateScale(0, 1) }, [[255, 255], [255, 255]]);
        AssertFill(2, 2, new RadialGradientBrush(new PointD(1, 1), 4, BlackToWhite) { Transform = new AffineTransform(1, 2, 2, 4, 0, 0) }, [[255, 255], [255, 255]]);

        Assert.True(new LinearGradientBrush(default, new PointD(1, 0), BlackToWhite).Transform.IsIdentity);
        Assert.Equal(AffineTransform.CreateScale(2, 1), new LinearGradientBrush(default, new PointD(1, 0), BlackToWhite) { Transform = AffineTransform.CreateScale(2, 1) }.Transform);
    }

    [Fact]
    public void AnEllipticRadialGradientFollowsItsTwoHalfAxes()
    {
        // Half-axes 4 and 1 around (4, 0.5): on its row, the parameter is |x + 0.5 - 4| / 4, as a circle of radius 4
        AssertFill(8, 1, new RadialGradientBrush(new PointD(4, 0.5), 4, 1, BlackToWhite), [[223, 159, 96, 32, 32, 96, 159, 223]]);

        // Half-axes 1 and 4 around (0.5, 4): on its column, the parameter is |y + 0.5 - 4| / 4
        AssertFill(1, 8, new RadialGradientBrush(new PointD(0.5, 4), 1, 4, BlackToWhite), [[223], [159], [96], [32], [32], [96], [159], [223]]);

        // Half-axes 10 and 5 around (0.5, 0.5): the parameter is sqrt(dx^2 + (2 dy)^2) / 10.
        //   (6, 4): sqrt(36 + 64) / 10 = 1 -> 255 | (3, 2): sqrt(9 + 16) / 10 = 1/2 -> 127.5 -> 128
        //   (0, 3): 6 / 10 -> 153 | (8, 0): 8 / 10 -> 204
        var brush = new RadialGradientBrush(new PointD(0.5, 0.5), 10, 5, BlackToWhite);
        Assert.Equal(10, brush.Radius);
        Assert.Equal(5, brush.RadiusY);
        using (var image = Fill(9, 9, brush))
        {
            Assert.Equal(0, image.Frames[0][0, 0].Value);
            Assert.Equal(255, image.Frames[0][6, 4].Value);
            Assert.Equal(128, image.Frames[0][3, 2].Value);
            Assert.Equal(153, image.Frames[0][0, 3].Value);
            Assert.Equal(204, image.Frames[0][8, 0].Value);
        }

        // The focal point is where the gradient starts, on the canvas. Half-axes 4 and 2 around (4.5, 4.5), focal point
        // (4.5, 3.5): back on the circle of radius 4, the focal point is at (4.5, 2.5) and the pixel (4, y) at
        // (4.5, 4.5 + 2 (y - 4)). With e = (0, 2), a = 16 - 4 = 12 and d the position from the focal point, the
        // parameter is (sqrt((d . e)^2 + 12 |d|^2) - d . e) / 12:
        //   y = 3: d = 0 -> 0 | y = 4: d = (0, 2) -> (8 - 4) / 12 = 1/3 -> 85 | y = 5: d = (0, 4) -> (16 - 8) / 12 -> 170
        //   y = 6: d = (0, 6) -> (24 - 12) / 12 = 1 -> 255 | y = 2: d = (0, -2) -> (8 + 4) / 12 = 1 -> 255
        using (var focal = Fill(9, 8, new RadialGradientBrush(new PointD(4.5, 4.5), 4, 2, BlackToWhite) { FocalPoint = new PointD(4.5, 3.5) }))
        {
            Assert.Equal([255, 255, 255, 0, 85, 170, 255, 255], Enumerable.Range(0, 8).Select(y => (int)focal.Frames[0][4, y].Value));
        }

        // A circular gradient has the same half-axes; one half-axis of zero and not the other leaves the color of the
        // last stop
        Assert.Equal(3, new RadialGradientBrush(new PointD(1, 1), 3, BlackToWhite).RadiusY);
        AssertFill(3, 1, new RadialGradientBrush(new PointD(1, 1), 0, 3, BlackToWhite), [[255, 255, 255]]);
        AssertFill(3, 1, new RadialGradientBrush(new PointD(1, 1), 3, 0, BlackToWhite), [[255, 255, 255]]);
        Assert.Throws<ArgumentOutOfRangeException>("radiusY", () => new RadialGradientBrush(default, 1, -1, BlackToWhite));
        Assert.Throws<ArgumentException>("radiusX", () => new RadialGradientBrush(default, double.NaN, 1, BlackToWhite));
    }

    [Fact]
    public void APathGradientMixesTheColorsOfItsPointsWithTheColorOfItsCenter()
    {
        // White corners of a square of 8 and a black center: in each triangle, the weight of the center is how far the
        // position is from the edge, as a fraction of the half side. The colors form a pyramid: 255 d / 4, d being the
        // largest distance to the center along x or y, 0.5, 1.5, 2.5 or 3.5 at the centers of the pixels.
        //   31.875 -> 32 | 95.625 -> 96 | 159.375 -> 159 | 223.125 -> 223
        var brush = new PathGradientBrush([new PointD(0, 0), new PointD(8, 0), new PointD(8, 8), new PointD(0, 8)], [new Rgba32(255, 255, 255)], new Rgba32(0, 0, 0));
        Assert.Equal(new PointD(4, 4), brush.Center);
        Assert.Equal(4, brush.PointColors.Length);
        Assert.True(brush.Transform.IsIdentity);
        int[] outer = [223, 223, 223, 223, 223, 223, 223, 223];
        int[] second = [223, 159, 159, 159, 159, 159, 159, 223];
        int[] third = [223, 159, 96, 96, 96, 96, 159, 223];
        int[] inner = [223, 159, 96, 32, 32, 96, 159, 223];
        AssertFill(8, 8, brush, [outer, second, third, inner, inner, third, second, outer]);
    }

    [Fact]
    public void APathGradientInterpolatesPremultipliedColorsAndGivesEachPointItsColor()
    {
        // The corners of the square are at the centers of the pixels (0, 0), (8, 0), (8, 8) and (0, 8), the center at
        // the one of (4, 4)
        PointD[] points = [new(0.5, 0.5), new(8.5, 0.5), new(8.5, 8.5), new(0.5, 8.5)];
        Rgba64[] colors = [new Rgba32(255, 0, 0), new Rgba32(0, 255, 0), new Rgba32(0, 0, 255), new Rgba32(255, 255, 255)];
        var brush = new PathGradientBrush(points, colors, new PointD(4.5, 4.5), new Rgba32(0, 0, 0, 0));
        using var image = new Image<Rgba32>(9, 9);
        image.Frames[0].Draw(canvas => canvas.Fill(brush), Ct);

        // Each point has its color, and the transparent center leaves its pixel as it is
        Assert.Equal(new Rgba32(255, 0, 0), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(0, 255, 0), image.Frames[0][8, 0]);
        Assert.Equal(new Rgba32(0, 0, 255), image.Frames[0][8, 8]);
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][0, 8]);
        Assert.Equal(default, image.Frames[0][4, 4]);

        // Half way from red to green on the edge, the center has no weight: 127.5 -> 128 for both
        Assert.Equal(new Rgba32(128, 128, 0), image.Frames[0][4, 0]);

        // Half way from the center to red: alpha 127.5 -> 128, and red 255 255 / 2 / 127.5 = 255. The transparent
        // center does not darken the color (a straight interpolation would give 128).
        Assert.Equal(new Rgba32(255, 0, 0, 128), image.Frames[0][2, 2]);
    }

    [Fact]
    public void APathGradientPaintsNothingOutsideItsTriangles()
    {
        // A red triangle on blue pixels: the centers with x + y + 1 <= 4.25 are inside it; the shape that is painted is
        // the whole canvas, but nothing is painted outside the triangle
        var brush = new PathGradientBrush([new PointD(0, 0), new PointD(4.25, 0), new PointD(0, 4.25)], [new Rgba32(255, 0, 0)], new Rgba32(255, 0, 0));
        using var image = new Image<Rgba32>(4, 4, new Rgba32(0, 0, 255));
        image.Frames[0].Draw(canvas => canvas.Fill(brush), Ct);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                Assert.Equal(x + y <= 3 ? new Rgba32(255, 0, 0) : new Rgba32(0, 0, 255), image.Frames[0][x, y], $"pixel ({x}, {y})");
            }
        }

        // A transformation places the points: moved by (2, 1), the triangle is the one of the moved points
        PointD[] moved = [new(2, 1), new(6.25, 1), new(2, 5.25)];
        using var expected = Fill(8, 8, new PathGradientBrush(moved, [new Rgba32(255, 255, 255)], new Rgba32(0, 0, 0)));
        using var actual = Fill(8, 8, new PathGradientBrush([new PointD(0, 0), new PointD(4.25, 0), new PointD(0, 4.25)], [new Rgba32(255, 255, 255)], new Rgba32(0, 0, 0)) { Transform = AffineTransform.CreateTranslation(2, 1) });
        Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(actual.Frames[0]));
        Assert.Contains(DrawingTestSupport.ReadSamples(actual.Frames[0]), sample => sample != 0);

        // A transformation that flattens the plane paints nothing, and neither do points on one line
        AssertFill(2, 2, new PathGradientBrush(moved, [new Rgba32(255, 255, 255)], new Rgba32(255, 255, 255)) { Transform = AffineTransform.CreateScale(0, 1) }, [[0, 0], [0, 0]]);
        AssertFill(2, 2, new PathGradientBrush([new PointD(0, 0), new PointD(1, 1), new PointD(2, 2)], [new Rgba32(255, 255, 255)], new Rgba32(255, 255, 255)), [[0, 0], [0, 0]]);
    }

    [Fact]
    public void APathGradientTakesTheColorOfTheFirstTriangleThatContainsAPosition()
    {
        // The points go back and forth: the triangle 0 (center, (8, 0), (0, 8)) is white everywhere, and the triangle 1
        // (center, (0, 8), (8, 0)) covers the same pixels with the black of its point (8, 0). The first one wins.
        PointD[] points = [new(8, 0), new(0, 8), new(8, 0), new(8, 8)];
        Rgba64[] colors = [new Rgba32(255, 255, 255), new Rgba32(255, 255, 255), new Rgba32(0, 0, 0), new Rgba32(0, 0, 0)];
        using var image = Fill(8, 8, new PathGradientBrush(points, colors, new PointD(0, 0), new Rgba32(255, 255, 255)));
        Assert.Equal(255, image.Frames[0][4, 1].Value);

        // (7.5, 3.5) is in the triangle 2 (center, (8, 0), (8, 8)): weights 1/2 for (8, 0) and 7/16 for (8, 8), both
        // black, 1/16 for the white center -> 15.9375 -> 16
        Assert.Equal(16, image.Frames[0][7, 3].Value);
    }

    [Fact]
    public void APathGradientValidatesItsPointsAndItsColors()
    {
        Rgba64[] one = [new Rgba32(1, 2, 3)];
        Assert.Throws<ArgumentException>("points", () => new PathGradientBrush([new PointD(0, 0), new PointD(1, 0)], one, default));
        Assert.Throws<ArgumentException>("points", () => new PathGradientBrush([new PointD(0, 0), new PointD(1, 0), new PointD(double.NaN, 1)], one, default));
        Assert.Throws<ArgumentOutOfRangeException>("points", () => new PathGradientBrush([new PointD(0, 0), new PointD(1, 0), new PointD(1e9, 1)], one, default));
        Assert.Throws<ArgumentException>("pointColors", () => new PathGradientBrush([new PointD(0, 0), new PointD(1, 0), new PointD(1, 1)], [new Rgba32(1, 2, 3), new Rgba32(1, 2, 3)], default));
        Assert.Throws<ArgumentException>("center", () => new PathGradientBrush([new PointD(0, 0), new PointD(1, 0), new PointD(1, 1)], one, new PointD(double.PositiveInfinity, 0), default));

        // The points and the colors are copied; one color is the color of every point; the center is the mean of the points
        PointD[] points = [new(0, 0), new(3, 0), new(3, 6)];
        Rgba64[] colors = [new Rgba32(1, 2, 3), new Rgba32(4, 5, 6), new Rgba32(7, 8, 9)];
        var brush = new PathGradientBrush(points, colors, new Rgba32(10, 11, 12));
        points[0] = new PointD(5, 5);
        colors[0] = default;
        Assert.Equal(new PointD(2, 2), brush.Center);
        Assert.Equal([new PointD(0, 0), new PointD(3, 0), new PointD(3, 6)], brush.Points.ToArray());
        Assert.Equal([(Rgba64)new Rgba32(1, 2, 3), new Rgba32(4, 5, 6), new Rgba32(7, 8, 9)], brush.PointColors.ToArray());
        Assert.Equal(new Rgba32(10, 11, 12), brush.CenterColor);
        Assert.Equal([(Rgba64)new Rgba32(1, 2, 3), new Rgba32(1, 2, 3), new Rgba32(1, 2, 3)], new PathGradientBrush(points, one, default).PointColors.ToArray());
    }

    [Fact]
    public void AHorizontalGradientPaintsEveryRowWithTheColorsOfItsFormula()
    {
        // The colors of a row of a horizontal gradient are computed once; stretched vertically, the gradient takes the
        // path of a transformed brush, whose parameter is the same: the inverse brings x back as it is
        var random = new Random(20261018);
        GradientStop[] stops = [new(0, new Rgba64(65535, 0, 0, 65535)), new(0.3, new Rgba64(0, 65535, 0, 65535)), new(0.7, new Rgba64(0, 0, 65535, 20000)), new(1, new Rgba64(65535, 65535, 0, 0))];
        GradientSpreadMode[] spreads = [GradientSpreadMode.Pad, GradientSpreadMode.Repeat, GradientSpreadMode.Reflect, GradientSpreadMode.Transparent];
        GradientColorSpace[] spaces = [GradientColorSpace.Encoded, GradientColorSpace.LinearSrgb, GradientColorSpace.Oklab, GradientColorSpace.Oklch];
        foreach (var format in new[] { PixelFormat.Rgba32, PixelFormat.Rgba64, PixelFormat.Rgb24 })
        {
            foreach (var spread in spreads)
            {
                foreach (var space in spaces)
                {
                    var start = new PointD((random.NextDouble() * 20) - 5, random.NextDouble() * 10);
                    var end = new PointD(start.X + (random.Next(2) == 0 ? -1 : 1) * ((random.NextDouble() * 15) + 1), start.Y);
                    var brush = new LinearGradientBrush(start, end, stops) { SpreadMode = spread, ColorSpace = space };
                    var stretched = new LinearGradientBrush(start, end, stops) { SpreadMode = spread, ColorSpace = space, Transform = AffineTransform.CreateScale(1, 2) };
                    var center = new PointD(random.NextDouble() * 24, random.NextDouble() * 16);
                    Assert.Equal(FillEllipse(format, stretched, center), FillEllipse(format, brush, center));
                }
            }
        }

        static int[] FillEllipse(PixelFormat format, Brush brush, PointD center)
        {
            using var image = DrawingTestSupport.CreateRandom(format, 24, 16, new Random(1).Next);
            image.Frames[0].Draw(canvas => canvas.FillEllipse(brush, center, 10, 6), Ct);
            return DrawingTestSupport.ReadSamples(image.Frames[0]);
        }
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

    [Fact]
    public void TheColorSpaceDecidesHowTheColorsOfTwoStopsAreMixed()
    {
        // From black to white over 2 pixels, then over 1: the parameters are 1/4 and 3/4, then 1/2.
        // Encoded: the sample values themselves, 63.75 -> 64, 191.25 -> 191 and 127.5 -> 128
        var encoded = new LinearGradientBrush(new PointD(0, 0), new PointD(2, 0), BlackToWhite);
        Assert.Equal(GradientColorSpace.Encoded, encoded.ColorSpace);
        AssertFill(2, 1, encoded, [[64, 191]]);
        AssertFill(1, 1, new LinearGradientBrush(new PointD(0, 0), new PointD(1, 0), BlackToWhite), [[128]]);

        // Linear light: the light itself is 1/4, 3/4 and 1/2, which sRGB encodes as 1.055 v^(1 / 2.4) - 0.055:
        // 0.5371 -> 137, 0.8808 -> 225 and 0.7354 -> 188
        AssertFill(2, 1, new LinearGradientBrush(new PointD(0, 0), new PointD(2, 0), BlackToWhite) { ColorSpace = GradientColorSpace.LinearSrgb }, [[137, 225]]);
        AssertFill(1, 1, new LinearGradientBrush(new PointD(0, 0), new PointD(1, 0), BlackToWhite) { ColorSpace = GradientColorSpace.LinearSrgb }, [[188]]);

        // Oklab: the lightness is 1/4, 3/4 and 1/2, and a gray of lightness L has the light L^3: 1/64, 27/64 and 1/8,
        // encoded as 0.1315 -> 34, 0.6813 -> 174 and 0.3886 -> 99
        AssertFill(2, 1, new LinearGradientBrush(new PointD(0, 0), new PointD(2, 0), BlackToWhite) { ColorSpace = GradientColorSpace.Oklab }, [[34, 174]]);
        AssertFill(1, 1, new LinearGradientBrush(new PointD(0, 0), new PointD(1, 0), BlackToWhite) { ColorSpace = GradientColorSpace.Oklab }, [[99]]);

        // Half way from red to green: a dark olive with the encoded values, the yellow that is as bright as both in
        // linear light (half the light of each), and in Oklab the color whose lightness and hue are between theirs
        GradientStop[] redToGreen = [new(0, new Rgba32(255, 0, 0)), new(1, new Rgba32(0, 255, 0))];
        Assert.Equal(new Rgba32(128, 128, 0), Middle(redToGreen, GradientColorSpace.Encoded));
        Assert.Equal(new Rgba32(188, 188, 0), Middle(redToGreen, GradientColorSpace.LinearSrgb));
        Assert.Equal(new Rgba32(208, 168, 0), Middle(redToGreen, GradientColorSpace.Oklab));

        // Half way from blue to yellow, complementary colors: gray, or a color in Oklab
        GradientStop[] blueToYellow = [new(0, new Rgba32(0, 0, 255)), new(1, new Rgba32(255, 255, 0))];
        Assert.Equal(new Rgba32(128, 128, 128), Middle(blueToYellow, GradientColorSpace.Encoded));
        Assert.Equal(new Rgba32(188, 188, 188), Middle(blueToYellow, GradientColorSpace.LinearSrgb));
        Assert.Equal(new Rgba32(108, 171, 199), Middle(blueToYellow, GradientColorSpace.Oklab));

        // The colors are mixed multiplied by their alpha in every space: a transparent stop gives no color, and a
        // stop at half its opacity counts for half. In linear light, from red at 128/255 to opaque blue: the alpha is
        // (0.502 + 1) / 2 = 0.751 -> 192, the red light 0.502 / 2 / 0.751 = 0.334 -> 156, the blue 0.5 / 0.751 -> 213
        GradientStop[] fromTransparent = [new(0, new Rgba32(255, 0, 0, 0)), new(1, new Rgba32(0, 0, 255))];
        GradientStop[] fromTranslucent = [new(0, new Rgba32(255, 0, 0, 128)), new(1, new Rgba32(0, 0, 255))];
        foreach (var space in Enum.GetValues<GradientColorSpace>())
        {
            Assert.Equal(new Rgba32(0, 0, 255, 128), Middle(fromTransparent, space));
        }

        Assert.Equal(new Rgba32(85, 0, 170, 192), Middle(fromTranslucent, GradientColorSpace.Encoded));
        Assert.Equal(new Rgba32(156, 0, 213, 192), Middle(fromTranslucent, GradientColorSpace.LinearSrgb));
        Assert.Equal(new Rgba32(101, 78, 194, 192), Middle(fromTranslucent, GradientColorSpace.Oklab));

        // At 16 bits, the colors keep their precision
        using (var image = new Image<Rgba64>(1, 1))
        {
            image.Draw(canvas => canvas.Fill(new LinearGradientBrush(new PointD(0, 0), new PointD(1, 0), redToGreen) { ColorSpace = GradientColorSpace.Oklab }), Ct);
            Assert.Equal(new Rgba64(53496, 43277, 116), image.Frames[0][0, 0]);
        }

        // At a stop and beyond the stops, the color is the color of the stop, exactly, whatever the space; so is the
        // color between two stops of the same color
        var color = new Rgba32(201, 37, 119, 77);
        GradientStop[] stops = [new(0, new Rgba32(3, 250, 17)), new(0.5, color), new(0.75, color), new(1, new Rgba32(90, 5, 211, 9))];
        foreach (var space in Enum.GetValues<GradientColorSpace>())
        {
            // The centers of the 8 pixels have the parameters -3/8 to 11/8, by steps of 1/4
            using var image = new Image<Rgba32>(8, 1);
            image.Draw(canvas => canvas.Fill(new LinearGradientBrush(new PointD(2, 0), new PointD(6, 0), stops) { ColorSpace = space }), Ct);
            Assert.Equal(new Rgba32(3, 250, 17), image.Frames[0][0, 0]);
            Assert.Equal(new Rgba32(3, 250, 17), image.Frames[0][1, 0]);
            Assert.Equal(color, image.Frames[0][4, 0]);
            Assert.Equal(new Rgba32(90, 5, 211, 9), image.Frames[0][6, 0]);
            Assert.Equal(new Rgba32(90, 5, 211, 9), image.Frames[0][7, 0]);
        }

        // Radial and sweep gradients, and the gradient a text is given, take their colors the same way
        AssertFill(1, 1, new RadialGradientBrush(new PointD(0.5, 1.5), 2, BlackToWhite) { ColorSpace = GradientColorSpace.LinearSrgb }, [[188]]);
        AssertFill(1, 1, new SweepGradientBrush(new PointD(0.5, 1.5), 0, 360, BlackToWhite) { ColorSpace = GradientColorSpace.Oklab }, [[174]]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new LinearGradientBrush(new PointD(0, 0), new PointD(1, 0), BlackToWhite) { ColorSpace = (GradientColorSpace)4 });

        static Rgba32 Middle(GradientStop[] stops, GradientColorSpace space)
        {
            using var image = new Image<Rgba32>(1, 1);
            image.Draw(canvas => canvas.Fill(new LinearGradientBrush(new PointD(0, 0), new PointD(1, 0), stops) { ColorSpace = space }), Ct);
            return image.Frames[0][0, 0];
        }
    }

    [Fact]
    public void OklchMixesTheHueAlongTheShorterArc()
    {
        // Half way between two 16-bit sRGB stops, computed outside the library from the Oklab matrices of Ottosson: the
        // lightness and the chroma are halved, the hue goes along the shorter arc (red 29 to green 142 degrees: an orange;
        // blue 264 to yellow 110 degrees: through cyan, where Oklab goes through a gray), then the color is clipped to sRGB
        Assert.Equal(new Rgba64(63876, 38211, 0, 65535), Middle([new(0, new Rgba32(255, 0, 0)), new(1, new Rgba32(0, 255, 0))]));
        Assert.Equal(new Rgba64(0, 53161, 48471, 65535), Middle([new(0, new Rgba32(0, 0, 255)), new(1, new Rgba32(255, 255, 0))]));

        // A gray stop has no hue: the gradient keeps the hue of the other stop, from black to a dark red, from white to a light blue
        Assert.Equal(new Rgba64(25465, 0, 0, 65535), Middle([new(0, new Rgba32(0, 0, 0)), new(1, new Rgba32(255, 0, 0))]));
        Assert.Equal(new Rgba64(29688, 41817, 65535, 65535), Middle([new(0, new Rgba32(255, 255, 255)), new(1, new Rgba32(0, 0, 255))]));

        // Between grays, Oklch is Oklab: the lightness is 1/4, 3/4 and 1/2
        AssertFill(2, 1, new LinearGradientBrush(new PointD(0, 0), new PointD(2, 0), BlackToWhite) { ColorSpace = GradientColorSpace.Oklch }, [[34, 174]]);
        AssertFill(1, 1, new LinearGradientBrush(new PointD(0, 0), new PointD(1, 0), BlackToWhite) { ColorSpace = GradientColorSpace.Oklch }, [[99]]);

        static Rgba64 Middle(GradientStop[] stops)
        {
            using var image = new Image<Rgba64>(1, 1);
            image.Draw(canvas => canvas.Fill(new LinearGradientBrush(new PointD(0, 0), new PointD(1, 0), stops) { ColorSpace = GradientColorSpace.Oklch }), Ct);
            return image.Frames[0][0, 0];
        }
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
                new RadialGradientBrush(new PointD(3.3, 4.1), 2.5, stops) { SpreadMode = GradientSpreadMode.Reflect, FocalPoint = new PointD(4.5, 3) },
                new SweepGradientBrush(new PointD(3.3, 4.1), 20, 75, stops) { SpreadMode = GradientSpreadMode.Repeat },
                new LinearGradientBrush(new PointD(1, 2), new PointD(6.5, 3.25), stops) { Transform = new AffineTransform(0.5, 1.25, -2, 0.75, 3, -1) },
                new LinearGradientBrush(new PointD(1, 2), new PointD(6.5, 3.25), stops) { SpreadMode = GradientSpreadMode.Reflect, ColorSpace = GradientColorSpace.LinearSrgb },
                new RadialGradientBrush(new PointD(3.3, 4.1), 2.5, stops) { SpreadMode = GradientSpreadMode.Repeat, ColorSpace = GradientColorSpace.Oklab },
                new RadialGradientBrush(new PointD(3.3, 4.1), 2.5, 1.25, stops) { SpreadMode = GradientSpreadMode.Reflect, FocalPoint = new PointD(3.5, 4) },

                // The triangles of a path gradient cover the canvas
                new PathGradientBrush([new PointD(-1, -1), new PointD(9.5, -1), new PointD(9, 9.25), new PointD(-1, 9)], [color], new PointD(3.3, 4.1), color),
                new PathGradientBrush([new PointD(-40, -40), new PointD(50, -40), new PointD(50, 50), new PointD(-40, 50)], [color], color) { Transform = new AffineTransform(1.25, 0.25, -0.5, 1, 1, 0.5) },
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

    [Fact]
    public void ADitheredGradientAveragesToItsColorsWithoutBands()
    {
        // From 100 to 104 between x = 64 and x = 192: the color grows by 1/32 per column, so rounding to the nearest
        // makes bands of 32 columns of one value
        GradientStop[] stops = [new(0, new Rgba32(100, 100, 100)), new(1, new Rgba32(104, 104, 104))];
        var plain = new LinearGradientBrush(new PointD(64, 0), new PointD(192, 0), stops);
        var dithered = new LinearGradientBrush(new PointD(64, 0), new PointD(192, 0), stops) { Dither = true };
        Assert.False(plain.Dither);
        using var banded = Fill(256, 16, plain);
        using var image = Fill(256, 16, dithered);
        using var again = Fill(256, 16, dithered);
        var bandedSamples = DrawingTestSupport.ReadSamples(banded.Frames[0]);
        var samples = DrawingTestSupport.ReadSamples(image.Frames[0]);
        Assert.Equal(samples, DrawingTestSupport.ReadSamples(again.Frames[0]));

        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 256; x++)
            {
                // Each sample is one of the two nearest to the color of the gradient; the stops are kept where the
                // gradient pads them
                var exact = 100 + (4 * Math.Clamp((x + 0.5 - 64) / 128, 0, 1));
                var sample = samples[(y * 256) + x];
                Assert.InRange(sample, Math.Floor(exact), Math.Ceiling(exact));
                if (x < 64 || x >= 192)
                {
                    Assert.Equal(bandedSamples[(y * 256) + x], sample);
                }
            }
        }

        // Over each block of 8x8 pixels, the dithered samples average to the color of the gradient; the bands are
        // half a step away from it at their ends, and give few averages
        var ditheredError = 0d;
        var bandedError = 0d;
        var ditheredAverages = new HashSet<double>();
        var bandedAverages = new HashSet<double>();
        for (var blockY = 0; blockY < 16; blockY += 8)
        {
            for (var blockX = 64; blockX < 192; blockX += 8)
            {
                var exact = 0d;
                var dither = 0d;
                var band = 0d;
                for (var y = blockY; y < blockY + 8; y++)
                {
                    for (var x = blockX; x < blockX + 8; x++)
                    {
                        exact += 100 + (4 * ((x + 0.5 - 64) / 128));
                        dither += samples[(y * 256) + x];
                        band += bandedSamples[(y * 256) + x];
                    }
                }

                ditheredError = Math.Max(ditheredError, Math.Abs(dither - exact) / 64);
                bandedError = Math.Max(bandedError, Math.Abs(band - exact) / 64);
                ditheredAverages.Add(dither / 64);
                bandedAverages.Add(band / 64);
            }
        }

        Assert.InRange(ditheredError, 0, 0.05);
        Assert.InRange(bandedError, 0.3, 0.5);
        Assert.HasCount(16, ditheredAverages);
        Assert.HasCount(5, bandedAverages);

        // A vertical gradient is dithered along its rows too
        var vertical = new LinearGradientBrush(new PointD(0, 64), new PointD(0, 192), stops) { Dither = true };
        using var verticalImage = Fill(16, 256, vertical);
        var verticalSamples = DrawingTestSupport.ReadSamples(verticalImage.Frames[0]);
        Assert.Contains(Enumerable.Range(64, 128), y => verticalSamples.Skip(y * 16).Take(16).Distinct().Count() == 2);
    }

    [Fact]
    public void DitheringChangesNothingOnAFrameOfMoreThanEightBits()
    {
        GradientStop[] stops = [new(0, new Rgba32(100, 100, 100)), new(1, new Rgba32(104, 104, 104))];
        var plain = new LinearGradientBrush(new PointD(4, 0), new PointD(60, 3), stops);
        var dithered = new LinearGradientBrush(new PointD(4, 0), new PointD(60, 3), stops) { Dither = true };
        foreach (var format in new[] { PixelFormat.Rgba64, PixelFormat.Gray16, PixelFormat.RgbaFloat })
        {
            Assert.Equal(Draw(format, plain), Draw(format, dithered));
        }

        static int[] Draw(PixelFormat format, Brush brush)
        {
            using var image = DrawingTestSupport.CreateRandom(format, 64, 8, new Random(1).Next);
            image.Frames[0].Draw(canvas => canvas.Fill(brush), Ct);
            return DrawingTestSupport.ReadSamples(image.Frames[0]);
        }
    }

    [Theory]
    [InlineData(GradientColorSpace.LinearSrgb)]
    [InlineData(GradientColorSpace.Oklab)]
    public void ADitheredGradientMixedInAnotherSpaceIsWithinAStepOfItsColors(GradientColorSpace colorSpace)
    {
        // The 16-bit colors of the gradient are moved by less than half a step of 8 bits before they are rounded: each
        // sample is the nearest one or its neighbor, and a color of 8 bits is kept
        GradientStop[] stops = [new(0, new Rgba32(20, 60, 100)), new(1, new Rgba32(40, 64, 90, 200))];
        var plain = new LinearGradientBrush(new PointD(8, 0), new PointD(120, 0), stops) { ColorSpace = colorSpace, CompositeMode = CompositeMode.Source };
        var dithered = new LinearGradientBrush(new PointD(8, 0), new PointD(120, 0), stops) { ColorSpace = colorSpace, CompositeMode = CompositeMode.Source, Dither = true };
        using var expected = new Image<Rgba32>(128, 8, default);
        using var actual = new Image<Rgba32>(128, 8, default);
        expected.Draw(canvas => canvas.Fill(plain), Ct);
        actual.Draw(canvas => canvas.Fill(dithered), Ct);
        var expectedSamples = DrawingTestSupport.ReadSamples(expected.Frames[0]);
        var actualSamples = DrawingTestSupport.ReadSamples(actual.Frames[0]);
        var changed = 0;
        for (var i = 0; i < expectedSamples.Length; i++)
        {
            Assert.InRange(actualSamples[i], expectedSamples[i] - 1, expectedSamples[i] + 1);
            var x = i / 4 % 128;
            if (x < 8 || x >= 120)
            {
                Assert.Equal(expectedSamples[i], actualSamples[i]);
            }

            changed += actualSamples[i] != expectedSamples[i] ? 1 : 0;
        }

        Assert.InRange(changed, 100, expectedSamples.Length);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Mesh gradients
    // -----------------------------------------------------------------------------------------------------------------

    // The 12 boundary points of a square patch with straight sides, the control points at a third and two thirds
    private static PointD[] SquareBoundary(double left, double top, double size)
    {
        var third = size / 3;
        return
        [
            new(left, top), new(left + third, top), new(left + (2 * third), top), new(left + size, top),
            new(left + size, top + third), new(left + size, top + (2 * third)), new(left + size, top + size),
            new(left + (2 * third), top + size), new(left + third, top + size), new(left, top + size),
            new(left, top + (2 * third)), new(left, top + third),
        ];
    }

    private static int RoundPaint(double value) => (int)Math.Floor(value + 0.5 + (1.0 / 1048576));

    [Fact]
    public void AMeshGradientPatchWithStraightSidesMixesItsCornerColorsBilinearly()
    {
        // The parameters of a square patch with evenly spaced control points are the position: u = x / 8, v = y / 8. The
        // color of a pixel is the bilinear mix of the corners at its center.
        int[] corners = [0, 100, 255, 50];
        var patch = MeshGradientPatch.CreateCoons(SquareBoundary(0, 0, 8), [.. corners.Select(c => (Rgba64)new Rgba32((byte)c, (byte)c, (byte)c))]);
        var brush = new MeshGradientBrush([patch]);
        Assert.Single(brush.Patches.ToArray());
        Assert.True(brush.Transform.IsIdentity);
        Assert.Equal(16, patch.ControlPoints.Length);
        Assert.Equal(new PointD(8, 8), patch.GetPoint(1, 1));
        Assert.Equal(new PointD(2, 6), patch.GetPoint(0.25, 0.75));

        var expected = new int[8][];
        for (var y = 0; y < 8; y++)
        {
            expected[y] = new int[8];
            for (var x = 0; x < 8; x++)
            {
                var u = (x + 0.5) / 8;
                var v = (y + 0.5) / 8;
                expected[y][x] = RoundPaint(((1 - u) * (1 - v) * corners[0]) + (u * (1 - v) * corners[1]) + (u * v * corners[2]) + ((1 - u) * v * corners[3]));
            }
        }

        AssertFill(8, 8, brush, expected);

        // A horizontal ramp from 0 to 255: 255 (x + 0.5) / 8
        var ramp = MeshGradientPatch.CreateCoons(SquareBoundary(0, 0, 8), [new Rgba32(0, 0, 0), new Rgba32(255, 255, 255), new Rgba32(255, 255, 255), new Rgba32(0, 0, 0)]);
        int[] row = [16, 48, 80, 112, 143, 175, 207, 239];
        AssertFill(8, 2, new MeshGradientBrush([ramp]), [row, row]);
    }

    [Fact]
    public void ACoonsPatchIsTheTensorProductPatchOfItsBoundary()
    {
        // The Coons surface, the sum of the two ruled surfaces of opposite sides minus the bilinear surface of the corners,
        // is the tensor-product surface whose inner points CreateCoons computes
        PointD[] boundary =
        [
            new(0, 0), new(30, -10), new(70, 15), new(100, 0),
            new(110, 40), new(90, 70), new(105, 100),
            new(60, 120), new(35, 80), new(-5, 95),
            new(10, 60), new(-15, 30),
        ];
        var patch = MeshGradientPatch.CreateCoons(boundary, [new Rgba32(1, 2, 3)]);
        for (var i = 0; i <= 10; i++)
        {
            for (var j = 0; j <= 10; j++)
            {
                var u = i / 10.0;
                var v = j / 10.0;
                var top = Bezier(boundary[0], boundary[1], boundary[2], boundary[3], u);
                var bottom = Bezier(boundary[9], boundary[8], boundary[7], boundary[6], u);
                var left = Bezier(boundary[0], boundary[11], boundary[10], boundary[9], v);
                var right = Bezier(boundary[3], boundary[4], boundary[5], boundary[6], v);
                var x = ((1 - v) * top.X) + (v * bottom.X) + ((1 - u) * left.X) + (u * right.X)
                    - (((1 - u) * (1 - v) * boundary[0].X) + (u * (1 - v) * boundary[3].X) + (u * v * boundary[6].X) + ((1 - u) * v * boundary[9].X));
                var y = ((1 - v) * top.Y) + (v * bottom.Y) + ((1 - u) * left.Y) + (u * right.Y)
                    - (((1 - u) * (1 - v) * boundary[0].Y) + (u * (1 - v) * boundary[3].Y) + (u * v * boundary[6].Y) + ((1 - u) * v * boundary[9].Y));
                var point = patch.GetPoint(u, v);
                Assert.Equal(x, point.X, 1e-9);
                Assert.Equal(y, point.Y, 1e-9);
            }
        }

        static PointD Bezier(PointD a, PointD b, PointD c, PointD d, double t)
        {
            var s = 1 - t;
            return new PointD(
                (s * s * s * a.X) + (3 * s * s * t * b.X) + (3 * s * t * t * c.X) + (t * t * t * d.X),
                (s * s * s * a.Y) + (3 * s * s * t * b.Y) + (3 * s * t * t * c.Y) + (t * t * t * d.Y));
        }
    }

    [Fact]
    public void ACurvedMeshGradientPaintsTheParametersOfEachPixel()
    {
        // A patch with curved sides and a horizontal ramp (u from 0 to 1): every pixel inside it has the color 255 u of the
        // parameters of its center, found by inverting the surface with Newton's method
        PointD[] boundary =
        [
            new(2, 3), new(12, -2), new(22, 6), new(30, 2),
            new(33, 12), new(27, 20), new(31, 30),
            new(21, 33), new(11, 26), new(1, 31),
            new(6, 21), new(-1, 11),
        ];
        var patch = MeshGradientPatch.CreateCoons(boundary, [new Rgba32(0, 0, 0), new Rgba32(255, 255, 255), new Rgba32(255, 255, 255), new Rgba32(0, 0, 0)]);
        using var image = Fill(32, 32, new MeshGradientBrush([patch]));
        var checkedPixels = 0;
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                if (!TryInvert(patch, x + 0.5, y + 0.5, out var u, out _))
                    continue;

                checkedPixels++;
                Assert.True(Math.Abs((255 * u) - image.Frames[0][x, y].Value) <= 1, $"pixel ({x}, {y}): {255 * u} != {image.Frames[0][x, y].Value}");
            }
        }

        Assert.True(checkedPixels > 500);

        // Newton's method from the middle of the patch; the parameters must stay inside the patch with a margin, so
        // that the pixels near the sides (where the triangles may differ by a tenth of a pixel) are skipped
        static bool TryInvert(MeshGradientPatch patch, double x, double y, out double u, out double v)
        {
            u = 0.5;
            v = 0.5;
            for (var i = 0; i < 50; i++)
            {
                var p = patch.GetPoint(u, v);
                var du = patch.GetPoint(u + 1e-7, v);
                var dv = patch.GetPoint(u, v + 1e-7);
                var a = (du.X - p.X) / 1e-7;
                var b = (dv.X - p.X) / 1e-7;
                var c = (du.Y - p.Y) / 1e-7;
                var d = (dv.Y - p.Y) / 1e-7;
                var determinant = (a * d) - (b * c);
                var ex = x - p.X;
                var ey = y - p.Y;
                u += ((d * ex) - (b * ey)) / determinant;
                v += ((a * ey) - (c * ex)) / determinant;
            }

            var reached = patch.GetPoint(u, v);
            return Math.Abs(reached.X - x) < 1e-6 && Math.Abs(reached.Y - y) < 1e-6 && u is > 0.05 and < 0.95 && v is > 0.05 and < 0.95;
        }
    }

    [Fact]
    public void AMeshGradientPaintsItsPatchesInOrderAndNothingOutsideThem()
    {
        // Two overlapping squares, 0 to 6 then 3 to 9 along x: the second one, white, covers the first one, gray; the
        // pixels outside both keep their black
        var first = MeshGradientPatch.CreateCoons(SquareBoundary(0, 0, 6), [new Rgba32(100, 100, 100)]);
        var second = MeshGradientPatch.CreateCoons(SquareBoundary(3, 0, 6), [new Rgba32(255, 255, 255)]);
        int[] row = [100, 100, 100, 255, 255, 255, 255, 255, 255, 0];
        AssertFill(10, 7, new MeshGradientBrush([first, second]), [row, row, row, row, row, row, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]]);
        AssertFill(10, 1, new MeshGradientBrush([second, first]), [[100, 100, 100, 100, 100, 100, 255, 255, 255, 0]]);

        // A patch of one color paints what a solid brush paints, inside the patch
        var solid = MeshGradientPatch.CreateTensorProduct(
            [new(0, 0), new(2, 0), new(4, 0), new(6, 0), new(0, 2), new(2, 2), new(4, 2), new(6, 2), new(0, 4), new(2, 4), new(4, 4), new(6, 4), new(0, 6), new(2, 6), new(4, 6), new(6, 6)],
            [new Rgba64(10000, 20000, 30000, 40000)]);
        using var meshImage = new Image<Rgba64>(6, 6);
        using var solidImage = new Image<Rgba64>(6, 6);
        meshImage.Frames[0].Draw(canvas => canvas.Fill(new MeshGradientBrush([solid])), Ct);
        solidImage.Frames[0].Draw(canvas => canvas.Fill(new Rgba64(10000, 20000, 30000, 40000)), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(solidImage.Frames[0]), DrawingTestSupport.ReadSamples(meshImage.Frames[0]));
    }

    [Fact]
    public void AFoldedMeshGradientShowsTheLargestParameters()
    {
        // The top and bottom sides go out to the right and come back to the left: the patch covers its area twice
        // The x of a top point is 72 u (1 - u)^2 - 48 u^2 (1 - u) + 0 u^3 = 24 u (1 - u)(3 - 5 u): it goes out to the right,
        // then back through 0 at u = 0.6 to the left. A pixel center x > 0 is reached twice for u < 0.6; the largest
        // one is painted. A ramp from black (u = 0) to white (u = 1) is then brighter than for the first u.
        var patch = MeshGradientPatch.CreateCoons(
            [new(0, 0), new(24, 0), new(-16, 0), new(0, 0), new(0, 2), new(0, 4), new(0, 6), new(-16, 6), new(24, 6), new(0, 6), new(0, 4), new(0, 2)],
            [new Rgba32(0, 0, 0), new Rgba32(255, 255, 255), new Rgba32(255, 255, 255), new Rgba32(0, 0, 0)]);
        using var image = Fill(8, 6, new MeshGradientBrush([patch]));
        for (var x = 0; x < 5; x++)
        {
            var center = x + 0.5;

            // The largest root below 0.6 of 24 u (1 - u)(3 - 5 u) = center, by bisection on the decreasing part
            double low = 0.25;
            double high = 0.6;
            for (var i = 0; i < 100; i++)
            {
                var middle = (low + high) / 2;
                if (24 * middle * (1 - middle) * (3 - (5 * middle)) > center)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            Assert.True(Math.Abs((255 * low) - image.Frames[0][x, 3].Value) <= 1, $"pixel {x}: {255 * low} != {image.Frames[0][x, 3].Value}");
        }
    }

    [Fact]
    public void AMeshGradientFollowsItsTransformation()
    {
        var moved = MeshGradientPatch.CreateCoons(SquareBoundary(2, 1, 4), [new Rgba32(0, 0, 0), new Rgba32(255, 255, 255), new Rgba32(80, 80, 80), new Rgba32(30, 30, 30)]);
        var original = MeshGradientPatch.CreateCoons(SquareBoundary(0, 0, 4), [new Rgba32(0, 0, 0), new Rgba32(255, 255, 255), new Rgba32(80, 80, 80), new Rgba32(30, 30, 30)]);
        using var expected = Fill(8, 8, new MeshGradientBrush([moved]));
        using var actual = Fill(8, 8, new MeshGradientBrush([original]) { Transform = AffineTransform.CreateTranslation(2, 1) });
        Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(actual.Frames[0]));
        Assert.Contains(DrawingTestSupport.ReadSamples(actual.Frames[0]), sample => sample != 0);

        // A transformation that flattens the plane paints nothing
        AssertFill(2, 2, new MeshGradientBrush([original]) { Transform = AffineTransform.CreateScale(0, 1) }, [[0, 0], [0, 0]]);
    }

    [Fact]
    public void AMeshGradientInterpolatesPremultipliedColors()
    {
        // Red to transparent along u: the transparent corners do not darken the red
        var patch = MeshGradientPatch.CreateCoons(SquareBoundary(0, 0, 4), [new Rgba32(255, 0, 0), new Rgba32(0, 0, 0, 0), new Rgba32(0, 0, 0, 0), new Rgba32(255, 0, 0)]);
        using var image = new Image<Rgba32>(4, 1);
        image.Frames[0].Draw(canvas => canvas.Fill(new MeshGradientBrush([patch])), Ct);
        Assert.Equal(new Rgba32(255, 0, 0, 223), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(255, 0, 0, 32), image.Frames[0][3, 0]);
    }

    // Ghostscript 10.06.0, "gs -sDEVICE=ppmraw -r72", of a 40x40 pt white page whose content is
    // "1 0 0 -1 0 40 cm /Sh0 sh": the PDF shading type 6 (Coons) or 7 (tensor product) of the patches of
    // MeshGradientsMatchGhostscript, as 40 x 40 RGB pixels
    private const string GhostscriptCoons = "////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////2QAl0QAtygA0vgE/tQBIqwBTowBamwBilABqjABx////////////////////////////////////////////////////AAD//////////////////////////////////////////////wAA9wAH7wAP6QAV4AMd2AQlzwcuxgk2vgs9tQlGrAhPpAdYmwZgkgVpiQRyfQCBeAKEbwCOZQCZXQChVQCpTQCx////NADKKwDTIgDcGQDlDwHtBQL2AAb3/////////////////////////////////////////wAA+gYE8QgN6AoV3wwd1w4lzhAtxhIzvhQ6tRNErBFMoxBUmw9dkg1liQxugQt2eAp/bwmJZgiTXQacVAWmSgSwPQDBNwPDLQXMIwbUGQfcEAjkBQntAAzv/////////////////////////////////////////xEA+hAE8BIN5xQV3xYc1hgkzRorxRwyvB06tBtBqxpJoxlRmhdZkRZiiRVqgRRyeBJ7bxGEZhCNXQ+WVA2gSgypQQqzOAu8LgzEIw3MGg7UEQ/bBhDjABLn/////////////////////////////////////////xoA+BoG7xwN5h4V3iAc1SIjzSQqxCUxvCY3syU/qyNHoiJOmiBWkR9eiR1mgBxudxt2bxl+ZhiHXReQVBWZSxSiQRKsOBO0LxS7JRXDGxbLEhbSCBfZABjf/////////////////////////////////////////yIA9yQG7iYN5igU3iob1SsizC0oxC4uvC81sy48qyxEoitLmilSkSdaiCZigCVpeCNxbyJ5ZiCCXR+KVB2SSxybQhqkORqsLxuzJhy7HB3CEx3JCR7QAB7X/////////////////////////////////////////i0A9i8H7TAN5jIT3jMZ1DUhzDYnwzgtuzgzsjc6qjVBojRHmjJPkTBWiC9dgC1keCxsbyp0Zil8XSeEVSWMTCSVQiKdOSKlMCKsJiOzHCS6EyTACiXGACTQ/////////////////////////////////////////TcA9TkH7ToN5DwT3D0a0z4fy0Alw0Eru0Exsj83qj4+oT1FmTtLkDlSiDhZgDZgdzRobzJvZjF2XS9/VS2GTCyNQiqWOSmdMCqkJiuqHSuxFCy3Cyy9ACrI/////////////////////////////////////////UEB9UIH7UQM5UUS3EYY1EcdzEgjw0opu0kus0g0qkc7oUVBmUNHkUJOiEBVgD5bdz1ibztqZjlxXjd4VTWATDOHQzGPOjGWMTGcKDKiHzOoFTOuDDO0ADe4/////////////////////////////////////////koA9kwF7k0L5U4R3U8W1VAczVEhxFImu1Iss1Exqk83oU4+mUxEkUpKiEhQgEdXd0Vdb0NlZkFsXj9zVT16TDuBQzmIOjmOMTmUJzqaHzqgFDumCzusAD2w/////////////////////////////////////////1UA91UF71YK5lcP3lgU1lkazVoexVsjvFsotFouq1g0olY6mVVAkVNGiFFMgE9Sd01ZbktfZklmXkdtVEV0TEN7Q0GBOkGHMEKNJ0KSHkKXFEOeCkKjAESn/////////////////////////////////////////10A+F4E8F8J52AO32ET1mIXzmMcxWQhvGQmtGMrq2Exol82mV08kVtCiFlIf1dNd1VUblNaZVFhXVBnVE1uS0t1Q0l7OUmBL0mGJkqLHUqQE0qVCUqaAEqf/////////////////////////////////////////2UA+WcD8WgI6GkN32oS12sWz2waxW0fvG0jtGwoq2otomgzmWY4kWQ+iGJDf2BJd15PblxVZVpbXFdhU1VoSlNvQVF1OFF6LlF/JVGEG1GIElGNCFGSAFCX/////////////////////////////////////////20A+W8D8nAH6XIL33MR2HQUz3UYx3YcvXchtXUlrHIqo3Avmm41kWw6iGo/f2lEd2ZKbmRQZWJVXF9cU11iSltoQVlvN1lzLll4JVl8G1mAEVmFCFmJAFeP////////////////////////////////////////////+3gC83kG6noK4nsO2X0S0H4Wx38avn8etX0irHsno3ksmncwkXU1iHM6f3E/dm9FbWxLZWlQXGdWUmVcSWNiQGFoN2FtLWBxJGB0GmB5EGB9BmCAAGR//////////////////////////////////////////////IEA9IIF64MJ4oQN2YUR0IYUx4cYv4gbtYYgrIQko4Epmn8tkX0yiHs2f3k7dndAbXRFZHJLW29QUm1WSWtbQGlhNmhlLGhpImhtGWdxD2d1BWd4AGp2/////////////////////////////////////////////YoA9IsE64wI4o0M2Y4P0Y8TyJAWv5AZto4drIwho4klmocqkYUuiIMyf4E3dn87bXxAZHpFW3dKUXVQSHNVP3BaNXBeK3BiIm9lGG9pDm9sAHBu/////////////////////////////////////////////////pIA9ZQE7JUH45UK2pYO0ZcRyJgUv5kXtpYarJQeo5Iimo8mkY0qiIsufokydoc3bYQ7ZIJAW39EUX1KSHpOP3hTNXhXK3daInddGHZhDXZkAHdm/////////////////////////////////////////////////5sA9pwD7Z0G5J4J2p8M0Z8PyKASv6EVtp8YrZwbo5ofmpgikZYmiJQqfpEtdY8ybIw2Y4o6Woc/UIVDR4JIP4BMNIBQKn9TIX5VFn5YDX5bAH1e/////////////////////////////////////////////////6MA9qUC7aYF5KcI26cL0qgOyakQv6oStqcVraUYo6IbmqAfkZ4ih5wlfpopdZctbJQxY5I1WY85UIw9R4pCPYhGM4dIKYdLIIZOFoZQDIVSAIRW/////////////////////////////////////////////////6sA9q4C7q4F5K8H27AK0rAMybEOv7IQtrATra0Vo6sYmqkbkaceh6QhfqIkdZ8obJwrY5kvWZczT5Q3RpE7PZA/M49BKY9DH45GFY1IC41JAIpO/////////////////////////////////////////////////7MA+LYC77cE5bgG3LgI0rkKyboMv7oOtrgQrLYTo7MVmrEYkK8ah6wdfqogdKcja6QmYqEqWJ4uT5sxRpk0PZc4Mpc6KJY7HpU9FJU/CpRBAJc+/////////////////////////////////////////////////8IA+L8B7r8D5MAG2sEH0cIJx8ILvcIMtMAOq70Qo7sSmrkUkLcXhrQZfrIbdK8ea6whYqkkWKYnT6MrRaAuO58xMZ4yKJ00Hp01FZw3C5s4AJ02/////////////////////////////////////////////////8oA9scB7MgD4skF2MkGz8oIxcoJvMoKs8cMqsUNosMPmcERj74ThrwVfroXdLYaa7McYrEfWK4hT6skRagoPKYqMqYrKaUsH6QtFqMuDKIvAKMu/////////////////////////////////////////////////c8A89AC6tAD4NEE19IGzdIGxNIHu9IIss8Jqc0LocsMl8gOjsYPhcMRfcETc74Va7sXYrgZWLUcT7MeRbAhPK4jM60kKqwkIaslF6omDqknAagnAKkn////////////////////////////////////////////+9gA8dkB6NkC3toD1doEy9oFwtsFudkGsdcHqNUIn9IJltAKjc4MhMsNfMgOc8YQasMSYsAUWb0WT7oYRrgaPLUcNLUdK7QdIrMdGbIeELEeB7AfAK8f/////////////////////////////////////////+AA+eEA7+EB5uIC3uIC0+IDyuMDweMDuOEEr98Ep9wFntoGldgHjdYIhNMJfNAKc80LassNYsgOWcUQUMISR8AUPr0VNbwVLLsWIroWGrkWEbgWCLcWALUX/////////////////////////////////////////+gA9uoA7eoB5uoB3uoB0esByOsBv+sBt+kBrucCpuQCneIDleAEjN0EhNsEfNgFc9UHatIIYtAJWc0KUMoLR8cNPsQONcMOLMIOIsEOGsAOEsAOCr8NALsP/////////////////////////////////////////fIA9PIA6/IA4/MA2fMA0PMAyvMAu/AAs+4ArOwApOsAnekAlOcAi+QAf+MAet8Bct0CatoCYtcDWdUFUNIFR88GPswINssILsoHI8kGG8gGFMcGC8YFAMEH//////////////////////////////////////8A+/oA8vsA6PoA4PgA////////////////////////////////////////////////////aN4AYdwAWdsAUtkASNcAP9QBN9MALtIAJdAAHs4AFswAD8sAB8kA//////////////////////////////////////8A////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////";

    private const string GhostscriptTensor = "////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////8HgA////////////////////////////////////////////////////////////TC0OUzAOWjMNYTcMaDoMbj0LeEELg0YJh0gKjUwKlE8Jm1IIolUIqVgHr1sHul8EvWEGw2QGyWcGz2oF1W0F23AF4XMF53UE7XgE8HwI////////////////////////////////////////////////NCIRPicQRy0STzITVzYTXzoTZz4Tb0ITd0YTfkkThk0TjVATk1MSmlYRoVoRqF0Qr2AQtmMPvGYPw2kPyWwPz28O1XEO23QO4XcO53oO7X0N8YAQ////////////////////////////////FBQUHBcTIhoSKyAVNCYXPiwZRzIbTzcbVjocXj4cZkIcbkYcdkocfU4chFEcjFUbk1gbmVsaoF4ap2EZrmUZtWgZvGsYwm0YyHAYz3MY1XYX23kX4XwX538X7oIX8YQY////////////////////////////////FBQUGBkZIR8bKiUdNCsfPTEhRzcjTzskVj8kXUMkZUckbUskdE4kfFIlg1YkilkkklwkmWAjoGMjp2YirmkitWwiu28hwnIhyHUhzngh1Xsh234g4YEg6IQg7ocg8okg////////////////////////////////Eh0kFx8iISQkKiomMzEoPTYqRjwsTkAsVUQsXUgtZEstbE8tc1Mte1YtglotiV0tkGEsmGQsn2cspmssrW4rtHEru3QrwXcqx3oqznwq1H8q24Iq4oYp6Ikp74wp8o0o////////////////////////////////ESIsFiQsICotKTAvMzUxPDsyRkE0TUU1VEk1W0w1Y1A1alM2clc2els2gV42iGI2j2U1l2g1nmw1pW81rHI0s3Y0unk0wXs0x34zzoEz1IQz24cz4ooz6I0y75Ey85Ew////////////////////////////////ESc0Fio1IDA3KTU4MTo5Oj86RUY8TEo9U009WlA+YlQ+aVg+cVs+eF8/gWM/h2Y+jmo+lm0+nXA+pHM+q3c9s3o9un09wIA9x4M9zoY81Ik824w84o886JI875U79JpA////////////////////////////////ECs8FjA+IDZAKDtBMT9COURDQUlES05GUVFGWVVGYFlGaFxHcGBHd2RHf2dHhmtHjm5HlXFHnHRGo3hGq3tGsn5GuYJGwIVGx4hFzotF1I5F25FF4pRF6JdE75pE9J5H////////////////////////////////DzBEFzZIHztJKEBKMERLOElMQE5NSFJOUFVOWFlOX11OZ2BOb2RPdmhPf2xPhm9PjXJPlHVPm3lOo3xOqn9OsYNOuYZOwIlOx4xOzo9N1JJN25VN4phN6JtN755N9KJP////////////////////////////////DjlTFjtRHkBRJkRSL0lTN01UP1JVR1ZWT1lWV11WX2FWZmRWbmhWdmxXf3BXhnNXjXZXlHpWm31WooBWqYNWsodWuopWwI1Wx5BWzpNW1JZV25lV4pxV6aBV8KNV9aVW////////////////////////////////DT5aFUBZHURaJUlbLU1bNlJcPlZdRlpeTl5eVmFeXmVeZWhebWxednBef3RehnhejXtek35emoFeooReqohesoteuo9ewZJex5Vezphe1Jte255e4qFe6aRe8Kde9ale////////////////////////////////DUJiFEVhHEliJE1jLFJkNFZkPVplRV5lTWJmVWVmXWlmZGxmbXFmdnVmf3lmhnxmjX9mk4JmmoVmoYhmqoxmso9mupNmwZZmx5lmzpxm1J9m3KJm46Vm6qlm9a1l////////////////////////////////////DEZpFEppG05qI1JrK1ZsM1psO15tRGJtTGZuU2puW21uY3FubHRudXluf31uhoBujINukoZumYluoY1uqZBusZRuupduwJpux51uzaBu1KNu3Kdu5Kpu661v9rFt////////////////////////////////////DEtwEk9xGlJyIVZzKVp0Ml50OmJ1QmZ1Smp1Um52WnF2YnV1anh1c3x1foF2hYV2iod2kYp2mY12oJF2qJR2sJd2upt2wJ52xqF2zaR21ah23Kt25K537LJ39rV0////////////////////////////////////////EVN6F1d6H1p7KF57MGJ8OWZ9QWp9SW59UXJ9WXV9YXl9aX19coB9eoR9hIh9iYt9kY59mJF+oJV+qJh+r5t+t59+vqJ+xqV+zal+1ax+3a9/5LN/7LZ/972D////////////////////////////////////////D1eCFVuCHl+DJmODL2eEN2uFQG+FSHKFUHaFWHqFYH2FaIGFcYSFeYiFgYyFiI+FkJKFmJWFn5mFp5yFr5+FtqOFvqaGxqmGzq2G1bCG3bSG5LeH7LqH98GK////////////////////////////////////////DFuKFF+KHWOLJWeLLmuMNm+MP3ONR3eNT3qNWH6NYIGNaIWNcImNeIyNgJCNiJONkJaNl5mNn52Np6CNrqONtqaNvqqNxq2OzrGO1bSO3biO5LuO7L6P+MSR////////////////////////////////////////C2CRE2SSHGiTJGyTLXCUNXSUPneVR3uVT3+VV4KVX4aVZ4mVb42Vd5CVf5SVh5eVj5qUl52Un6GVp6SVrqeVtqqUvq6UxrGVzrWV1biW3byW5b+W7cOX+MiY////////////////////////////////////////CmSZEmiaG2ybJHCbLHScNXicPXydRoCdToOdVoedXoqdZo6dbpGdd5Sdf5ich5ucj56cl6Gcn6Wcpqicrquctq6cvrKcxrWczbmd1byd3cCe5cOe7cef9suf+cyf////////////////////////////////////CWihEW2iGnGiI3WjK3mkNHykPYClRYSlTYelVYulXY6lZpKlbpWkdpikfpykhp+kjqKklqWknqmkpqykrq+jtrKjvrWjxbmkzbyk1cCl3cSm5cem7sun9s+n+dCn////////////////////////////////////B22pEHGpGXWqInmrK32rM4GsPIWsRIitTIytVY+tXZOtZZasbZmsdZysfqCshqOsjqaslqmsnq2rprCrrrOrtrarvbmrxb2rzcCs1cSs3cit5cuu7s+v9tOv+dSu////////////////////////////////////BnGwD3WxGHqyIX2yKoGzMoW0O4m0RI21TJC1VJS1XJe0ZJq0bZ20daC0faS0hae0jaqzla2znbGzpbSzrbeytbqyvb2yxcGzzcSz1ci03cy15c+27dO39te3+te1////////////////////////////////////BnW2Dnq5F366IIK6KYa7MYq8Oo28Q5G9S5W9U5i8W5u8ZJ68bKG8dKW8fKi7hKu7jK67lbG7nbS6pbi6rbu6tb66vcG6xcW6zci71cy83dC95dO97de+9du/+9/D////////////////////////////////////BH3EDX7BFoLBH4bCKIrDMY7DOpLEQpbFS5nFU5zEW5/EY6LEa6bEdKnDfKzDhK/DjLLDlLXCnLnCpLzCrb/CtcLBvcXBxcnCzczC1dDD3dPE5NfF7NvG9N7G++LJ////////////////////////////////////BX/IDYPIFofJH4vKKI/KMZPLOpfMQpvNSp7NUqHNW6TMY6fMa6rMc67MfLHMhLTLjLbLlLrKnL3KpMDKrMPKtMbJvcnJxMzJzNDK1NPL3NfL5NrM7N7N8+HO/ObQ////////////////////////////////////BoTPDojQF4zRIJDSKZTSMpjTOpzUQqDVSqPVUqbVWqnVY6zVa6/Uc7LUe7XUhLjTi7vTk77Sm8HSo8TSrMfStMrRvc3RxNDRy9TR09fS29rT497U6+HU8uXV/OnX////////////////////////////////////B4nXD43YGJHZIZXZKZnaMp3bOqHdQqXeSqjeUqvdWq3dYrHda7Tdc7fce7rcg73bi7/bk8Lbm8Xao8jarMvZtM7ZvdLYxNXZzNjZ09va2t7a4uHb6eXc8ejc+Ovd/O3d////////////////////////////AozeCI7eEJLfGZbgIZrhKZ7jMqLkOqblQ6rmSq3mUrDmWrLlYrXlarjlc7vke77kg8Hki8Tjk8fjm8nio8ziq8/htNPhvdbgxNngy9zh0t/h2eLi4eXi6Ojj8Ovj9+7k/fDk////////////////////////////ApDkCZPmEZfnGpvoIp/pKqPrMqfsOqvtQ6/uSrLuUrXuWrfuYrrtar3tcsDte8Psg8bsi8jrksvrms7qotDqqtPptNbovdroxN3oy+Dp0eLp2OXp4Ojq5+vq7+7r9vLr/fTr////////////////////////////AZXsCpntEpzuGqDwIqTxKqjzMqz0OrD2Q7T3Srf2Urn2Wrz2Yr/1asH1csT1e8f0g8r0is30ks/zmtLyodXyqdfxstrwvd7wxOHwyuPw0Obx2Onx3+zx5u/y7vLy9fXz/vfx////////////////////////////Apr0C571E6L2G6b4I6r5K637M7H9O7X+Rrr/Tbz/Ur7+WsH+YsT+asb9csn9esz9gs78idH8kdT7mdb6odn6qdv5sd74vOL3wuT4yOf4z+r41+z53u/55fL57fX69Pj6/vv4////////////////////////AKD/A5/7C6P9E6f+HKr/I63/////////////////////////////////////////////////////////mdn/oNv/p97/ruD/teP/w+j/yuv/0O3/1/D/3fL/5PX/6/f/8fr/+Pz/////////////////////////AKD/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeshGradientsMatchGhostscript(bool tensor)
    {
        MeshGradientPatch patch;
        if (tensor)
        {
            // The order of a PDF type 7 stream is p00 p01 p02 p03 p13 p23 p33 p32 p31 p30 p20 p10 p11 p12 p22 p21
            patch = MeshGradientPatch.CreateTensorProduct(
                [new(5, 8), new(12, 4), new(22, 6), new(35, 5), new(3, 16), new(14, 15), new(24, 12), new(36, 14), new(8, 26), new(14, 26), new(26, 28), new(33, 24), new(4, 37), new(15, 33), new(25, 38), new(37, 36)],
                [new Rgba32(20, 20, 20), new Rgba32(240, 120, 0), new Rgba32(255, 255, 255), new Rgba32(0, 160, 255)]);
        }
        else
        {
            // The order of a PDF type 6 stream: p00 p01 p02 p03 p13 p23 p33 p32 p31 p30 p20 p10, colors c00 c03 c33 c30
            patch = MeshGradientPatch.CreateCoons(
                [new(2, 3), new(12, -2), new(22, 6), new(30, 2), new(33, 12), new(27, 20), new(31, 30), new(21, 33), new(11, 26), new(1, 31), new(6, 21), new(-1, 11)],
                [new Rgba32(255, 0, 0), new Rgba32(0, 0, 255), new Rgba32(0, 200, 0), new Rgba32(255, 255, 0)]);
        }

        using var image = new Image<Rgba32>(40, 40, new Rgba32(255, 255, 255));
        image.Frames[0].Draw(canvas => canvas.Fill(new MeshGradientBrush([patch])), Ct);
        var expected = Convert.FromBase64String(tensor ? GhostscriptTensor : GhostscriptCoons);
        var white = new Rgba32(255, 255, 255);
        bool IsPainted(int x, int y) => (uint)x < 40 && (uint)y < 40 && new Rgba32(expected[3 * ((40 * y) + x)], expected[(3 * ((40 * y) + x)) + 1], expected[(3 * ((40 * y) + x)) + 2]) != white;

        // Ghostscript paints the pixels a patch touches, this brush the pixels whose center it covers: the pixels only
        // Ghostscript paints are next to a painted pixel, and this brush paints no pixel Ghostscript does not
        var largest = 0;
        var interior = 0;
        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 40; x++)
            {
                var actual = image.Frames[0][x, y];
                if (actual != white)
                {
                    Assert.True(IsPainted(x, y), $"pixel ({x}, {y}) is not painted by Ghostscript");
                }
                else if (IsPainted(x, y))
                {
                    var neighbor = false;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            neighbor |= (uint)(x + dx) < 40 && (uint)(y + dy) < 40 && image.Frames[0][x + dx, y + dy] != white;
                        }
                    }

                    Assert.True(neighbor, $"pixel ({x}, {y}) is painted by Ghostscript far from the patch");
                    continue;
                }

                // The colors of the pixels away from the sides differ by the subdivisions of each rasterizer
                if (!(IsPainted(x - 1, y) && IsPainted(x + 1, y) && IsPainted(x, y - 1) && IsPainted(x, y + 1) && IsPainted(x, y)))
                    continue;

                interior++;
                var offset = 3 * ((40 * y) + x);
                largest = Math.Max(largest, Math.Max(Math.Abs(actual.R - expected[offset]), Math.Max(Math.Abs(actual.G - expected[offset + 1]), Math.Abs(actual.B - expected[offset + 2]))));
            }
        }

        Assert.True(interior > 600, $"{interior} interior pixels");
        Assert.True(largest <= 8, $"largest difference {largest}");
    }

    [Fact]
    public void MeshGradientArgumentsAreValidated()
    {
        Assert.Throws<ArgumentException>(() => MeshGradientPatch.CreateCoons(new PointD[11], [default]));
        Assert.Throws<ArgumentException>(() => MeshGradientPatch.CreateCoons(new PointD[12], [default, default]));
        Assert.Throws<ArgumentException>(() => MeshGradientPatch.CreateTensorProduct(new PointD[12], [default]));
        Assert.Throws<ArgumentException>(() => MeshGradientPatch.CreateTensorProduct([.. Enumerable.Repeat(new PointD(double.NaN, 0), 16)], [default]));
        Assert.Throws<ArgumentException>(() => new MeshGradientBrush([]));
        Assert.Throws<ArgumentException>(() => new MeshGradientBrush([null!]));
        var colors = MeshGradientPatch.CreateCoons(new PointD[12], [new Rgba32(1, 2, 3)]).CornerColors.ToArray();
        Assert.HasCount(4, colors);
        Assert.All(colors, color => Assert.Equal((Rgba64)new Rgba32(1, 2, 3), color));
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
