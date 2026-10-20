using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Strokes. Every expected sample is a literal derived by hand: the paths are horizontal and vertical, so that the stroke
/// is a union of rectangles, triangles and squares whose intersection with each pixel is computed in the comments,
/// painted in opaque white over opaque black Gray8 pixels (<c>round(255 * area)</c>, ties upward). Round joins and caps
/// are regular polygons whose number of sides follows from the flattening tolerance of 1/16 pixel. The conformance tests
/// add a membership predicate and an exact rational area over arbitrary paths.
/// </summary>
public sealed class DrawingStrokeTests
{
    private static readonly Brush White = new SolidBrush(new Rgba32(255, 255, 255));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void AStrokeIsCenteredOnItsPath()
    {
        // Width 1 through the middle of row 1, from x = 1 to x = 4: exactly three pixels
        using (var image = NewBlack(5, 3))
        {
            image.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(White, 1), new PointD(1, 1.5), new PointD(4, 1.5)), Ct);
            AssertGray8(image, [[0, 0, 0, 0, 0], [0, 255, 255, 255, 0], [0, 0, 0, 0, 0]]);
        }

        // Width 1 along the boundary between rows 0 and 1: half of each row
        using (var image = NewBlack(5, 3))
        {
            image.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(White, 1), new PointD(1, 1), new PointD(4, 1)), Ct);
            AssertGray8(image, [[0, 128, 128, 128, 0], [0, 128, 128, 128, 0], [0, 0, 0, 0, 0]]);
        }

        // Width 2 along the same boundary: both rows
        using (var image = NewBlack(5, 3))
        {
            image.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(White, 2), new PointD(1, 1), new PointD(4, 1)), Ct);
            AssertGray8(image, [[0, 255, 255, 255, 0], [0, 255, 255, 255, 0], [0, 0, 0, 0, 0]]);
        }

        // A vertical line, from bottom to top: the direction does not matter
        using (var image = NewBlack(3, 5))
        {
            image.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(White, 1), new PointD(1.5, 4), new PointD(1.5, 1)), Ct);
            AssertGray8(image, [[0, 0, 0], [0, 255, 0], [0, 255, 0], [0, 255, 0], [0, 0, 0]]);
        }
    }

    [Fact]
    public void CapsExtendTheEndsOfAnOpenContour()
    {
        // Butt: nothing beyond the ends
        using (var image = NewBlack(5, 3))
        {
            image.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(White, 1) { LineCap = LineCap.Butt }, new PointD(1, 1.5), new PointD(4, 1.5)), Ct);
            AssertGray8(image, [[0, 0, 0, 0, 0], [0, 255, 255, 255, 0], [0, 0, 0, 0, 0]]);
        }

        // Square: half the width beyond each end, x from 0.5 to 4.5
        using (var image = NewBlack(5, 3))
        {
            image.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(White, 1) { LineCap = LineCap.Square }, new PointD(1, 1.5), new PointD(4, 1.5)), Ct);
            AssertGray8(image, [[0, 0, 0, 0, 0], [128, 255, 255, 255, 128], [0, 0, 0, 0, 0]]);
        }

        // Round: a half disk of radius 1/2 beyond each end. A quarter turn of radius 1/2 is cut in two chords (one chord
        // would be 0.146 away from the arc, two are 0.038 away), so the half disk is four triangles of angle 45 degrees:
        // 4 * (1/2) * (1/2)^2 * sin(45) = 0.35355 -> 90.16 -> 90
        using (var image = NewBlack(5, 3))
        {
            image.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(White, 1) { LineCap = LineCap.Round }, new PointD(1, 1.5), new PointD(4, 1.5)), Ct);
            AssertGray8(image, [[0, 0, 0, 0, 0], [90, 255, 255, 255, 90], [0, 0, 0, 0, 0]]);
        }
    }

    [Fact]
    public void AHairlineIsOnePixelOfTheCanvasWideWhateverTheTransformation()
    {
        // A pen of width 0 strokes one pixel of the canvas: the line at 2.5 is at 10 once scaled by 4, and covers half of
        // the rows 9 and 10, as wide as without the transformation
        using (var image = NewBlack(16, 16))
        {
            image.Frames[0].Draw(static canvas => canvas.Transform(AffineTransform.CreateScale(4, 4), static scaled => scaled.StrokeLine(new Pen(White, 0), new PointD(0, 2.5), new PointD(4, 2.5))), Ct);
            Assert.Equal(128, image.Frames[0][5, 9].Value);
            Assert.Equal(128, image.Frames[0][5, 10].Value);
            Assert.Equal(0, image.Frames[0][5, 8].Value);
        }

        // It is the pen one pixel wide the transformation does not apply to, under any transformation, and without one
        var path = new VectorPathBuilder().MoveTo(1, 1).CubicBezierTo(9, -2, 3, 12, 11, 8).LineTo(2, 9).Build();
        var transforms = new[] { AffineTransform.Identity, AffineTransform.CreateScale(0.5, 3).Then(AffineTransform.CreateRotation(30, new PointD(6, 6))) };
        foreach (var transform in transforms)
        {
            Assert.Equal(Render(transform, new Pen(White, 1) { ScalesWithTransform = false, LineCap = LineCap.Round }), Render(transform, new Pen(White, 0) { LineCap = LineCap.Round }));
            Assert.Equal(Render(transform, new Pen(White, 1) { ScalesWithTransform = false, DashPattern = [2, 1.5] }), Render(transform, new Pen(White, 0) { DashPattern = [2, 1.5] }));
        }

        int[] Render(AffineTransform transform, Pen pen)
        {
            using var image = NewBlack(24, 24);
            image.Frames[0].Draw(canvas => canvas.Transform(transform, inner => inner.Stroke(pen, path)), Ct);
            return DrawingTestSupport.ReadSamples(image.Frames[0]);
        }
    }

    [Fact]
    public void DashesWithoutLengthAreTheCapsOfTheirPoint()
    {
        // Dots: dashes of length 0 every 4 pixels with round caps are disks of the width of the pen at those points
        using var dots = NewBlack(16, 8);
        dots.Frames[0].Draw(static canvas => canvas.StrokeLine(new Pen(White, 3) { DashPattern = [0, 4], LineCap = LineCap.Round }, new PointD(2, 4), new PointD(14.5, 4)), Ct);
        using var disks = NewBlack(16, 8);
        disks.Frames[0].Draw(static canvas =>
        {
            foreach (var x in (double[])[2, 6, 10, 14])
            {
                canvas.FillCircle(White, new PointD(x, 4), 1.5);
            }
        }, Ct);
        AssertClose(disks, dots);

        // Squares turned to the direction of the path: on a diagonal, squares of 2 turned by 45 degrees
        using var squares = NewBlack(16, 16);
        squares.Frames[0].Draw(static canvas => canvas.StrokeLine(new Pen(White, 2) { DashPattern = [0, 5 * Math.Sqrt(2)], LineCap = LineCap.Square }, new PointD(3, 3), new PointD(13.5, 13.5)), Ct);
        using var turned = NewBlack(16, 16);
        turned.Frames[0].Draw(static canvas =>
        {
            foreach (var center in (double[])[3, 8, 13])
            {
                var d = Math.Sqrt(2) / 2;
                canvas.FillPolygon(White, [new(center + (2 * d), center), new(center, center + (2 * d)), new(center - (2 * d), center), new(center, center - (2 * d))]);
            }
        }, Ct);
        AssertClose(turned, squares);

        // Butt caps draw nothing
        using var nothing = NewBlack(8, 8);
        nothing.Frames[0].Draw(static canvas => canvas.StrokeLine(new Pen(White, 3) { DashPattern = [0, 2] }, new PointD(1, 4), new PointD(7, 4)), Ct);
        Assert.All(DrawingTestSupport.ReadSamples(nothing.Frames[0]), sample => Assert.Equal(0, sample));

        static void AssertClose(Image<Gray8> expected, Image<Gray8> actual)
        {
            var left = DrawingTestSupport.ReadSamples(expected.Frames[0]);
            var right = DrawingTestSupport.ReadSamples(actual.Frames[0]);
            for (var i = 0; i < left.Length; i++)
            {
                Assert.InRange(right[i], left[i] - 2, left[i] + 2, message: $"sample {i}");
            }
        }
    }

    [Fact]
    public void AContourReducedToAPointIsDrawnByItsCaps()
    {
        var point = new PointD(2, 2);
        (LineCap Cap, int[][] Expected)[] cases =
        [
            (LineCap.Butt, [[0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0]]),

            // A square of side 2 around the point
            (LineCap.Square, [[0, 0, 0, 0], [0, 255, 255, 0], [0, 255, 255, 0], [0, 0, 0, 0]]),

            // A disk of radius 1: a quarter turn is cut in four chords (two would be 0.076 away from the arc), so the
            // disk is a regular polygon of 16 sides and each pixel around the point holds four triangles of angle 22.5
            // degrees: 4 * (1/2) * sin(22.5) = 0.76537 -> 195.17 -> 195
            (LineCap.Round, [[0, 0, 0, 0], [0, 195, 195, 0], [0, 195, 195, 0], [0, 0, 0, 0]]),
        ];
        foreach (var (cap, expected) in cases)
        {
            using (var image = NewBlack(4, 4))
            {
                image.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(White, 2) { LineCap = cap }, point, point), Ct);
                AssertGray8(image, expected);
            }

            // The same for a path with a single point, open or closed
            foreach (var path in new[] { new VectorPathBuilder().MoveTo(point).Build(), new VectorPathBuilder().MoveTo(point).Close().Build(), new VectorPathBuilder().MoveTo(point).LineTo(point).Build() })
            {
                using var image = NewBlack(4, 4);
                image.Frames[0].Draw(canvas => canvas.Stroke(new Pen(White, 2) { LineCap = cap }, path), Ct);
                AssertGray8(image, expected);
            }
        }
    }

    [Fact]
    public void JoinsFillTheOuterSideOfACorner()
    {
        // (1,1) -> (4,1) -> (4,4), width 2. The first segment covers x 1..4, y 0..2; the second x 3..5, y 1..4. The outer
        // side of the corner is the pixel (4,0), between the corner (4,1) and the outer corners (4,0) and (5,1).
        var path = new VectorPathBuilder().MoveTo(1, 1).LineTo(4, 1).LineTo(4, 4).Build();
        (Pen Pen, int Corner)[] cases =
        [
            // Miter: the outer edges meet at (5,0), the whole pixel
            (new Pen(White, 2) { LineJoin = LineJoin.Miter }, 255),

            // Bevel: the triangle (4,1) (4,0) (5,1), half of the pixel
            (new Pen(White, 2) { LineJoin = LineJoin.Bevel }, 128),

            // Round: a quarter disk of radius 1, cut in four chords: 4 * (1/2) * sin(22.5) = 0.76537 -> 195
            (new Pen(White, 2) { LineJoin = LineJoin.Round }, 195),

            // The miter of a right angle is sqrt(2) = 1.414 times the width: kept with a limit of 1.5, beveled with 1.4
            (new Pen(White, 2) { LineJoin = LineJoin.Miter, MiterLimit = 1.5 }, 255),
            (new Pen(White, 2) { LineJoin = LineJoin.Miter, MiterLimit = 1.4 }, 128),
            (new Pen(White, 2) { LineJoin = LineJoin.Miter, MiterLimit = 1 }, 128),
        ];
        foreach (var (pen, corner) in cases)
        {
            using var image = NewBlack(6, 5);
            image.Frames[0].Draw(canvas => canvas.Stroke(pen, path), Ct);
            AssertGray8(image, [[0, 255, 255, 255, corner, 0], [0, 255, 255, 255, 255, 0], [0, 0, 0, 255, 255, 0], [0, 0, 0, 255, 255, 0], [0, 0, 0, 0, 0, 0]]);
        }

        // The other way round, (4,4) -> (4,1) -> (1,1): the turn has the other sign and the same outer side
        var reversed = new VectorPathBuilder().MoveTo(4, 4).LineTo(4, 1).LineTo(1, 1).Build();
        foreach (var (pen, corner) in cases)
        {
            using var image = NewBlack(6, 5);
            image.Frames[0].Draw(canvas => canvas.Stroke(pen, reversed), Ct);
            AssertGray8(image, [[0, 255, 255, 255, corner, 0], [0, 255, 255, 255, 255, 0], [0, 0, 0, 255, 255, 0], [0, 0, 0, 255, 255, 0], [0, 0, 0, 0, 0, 0]]);
        }
    }

    [Fact]
    public void ARectangleIsStrokedOnBothSidesOfItsOutline()
    {
        // The rectangle (1,1)-(4,4) with a width of 2: everything from 0 to 5 except the inside from 2 to 3
        using (var image = NewBlack(5, 5))
        {
            image.Frames[0].Draw(canvas => canvas.StrokeRectangle(new Pen(White, 2), new Rectangle(1, 1, 3, 3)), Ct);
            AssertGray8(image, [[255, 255, 255, 255, 255], [255, 255, 255, 255, 255], [255, 255, 0, 255, 255], [255, 255, 255, 255, 255], [255, 255, 255, 255, 255]]);
        }

        // With a width of 1: the frame from 0.5 to 4.5 without the inside from 1.5 to 3.5.
        //   corners: a quarter of the pixel -> 64 | sides: half -> 128
        //   (1,1): the whole pixel but the quarter from 1.5 to 2 -> 0.75 -> 191 | (2,1): the half above 1.5 -> 128
        using (var image = NewBlack(5, 5))
        {
            image.Frames[0].Draw(canvas => canvas.StrokeRectangle(new Pen(White, 1), new Rectangle(1, 1, 3, 3)), Ct);
            AssertGray8(image, [[64, 128, 128, 128, 64], [128, 191, 128, 191, 128], [128, 128, 0, 128, 128], [128, 191, 128, 191, 128], [64, 128, 128, 128, 64]]);
        }
    }

    [Fact]
    public void AClosedContourIsJoinedAtItsFirstPointAndAnOpenOneIsCapped()
    {
        int[][] closedExpected = [[255, 255, 255, 255, 255], [255, 255, 255, 255, 255], [255, 255, 0, 255, 255], [255, 255, 255, 255, 255], [255, 255, 255, 255, 255]];
        var closed = new VectorPathBuilder().MoveTo(1, 1).LineTo(4, 1).LineTo(4, 4).LineTo(1, 4).Close().Build();
        using (var image = NewBlack(5, 5))
        {
            image.Frames[0].Draw(canvas => canvas.Stroke(new Pen(White, 2), closed), Ct);
            AssertGray8(image, closedExpected);
        }

        // Closed by a segment that comes back to the first point, then by Close: the same
        var closedTwice = new VectorPathBuilder().MoveTo(1, 1).LineTo(4, 1).LineTo(4, 4).LineTo(1, 4).LineTo(1, 1).Close().Build();
        using (var image = NewBlack(5, 5))
        {
            image.Frames[0].Draw(canvas => canvas.Stroke(new Pen(White, 2), closedTwice), Ct);
            AssertGray8(image, closedExpected);
        }

        // Coming back to the first point without closing: no join there, and butt caps add nothing, so the outer side
        // of that corner, the pixel (0,0), is not covered
        var open = new VectorPathBuilder().MoveTo(1, 1).LineTo(4, 1).LineTo(4, 4).LineTo(1, 4).LineTo(1, 1).Build();
        using (var image = NewBlack(5, 5))
        {
            image.Frames[0].Draw(canvas => canvas.Stroke(new Pen(White, 2), open), Ct);
            AssertGray8(image, [[0, 255, 255, 255, 255], [255, 255, 255, 255, 255], [255, 255, 0, 255, 255], [255, 255, 255, 255, 255], [255, 255, 255, 255, 255]]);
        }
    }

    [Fact]
    public void APathThatTurnsBackOnlyGainsARoundJoin()
    {
        // (1, 1.5) -> (4, 1.5) -> (1, 1.5): the same pixels as the line, twice. Miter and bevel joins add nothing at
        // the turn; a round join adds the half disk beyond it (0.35355 -> 90, see the round cap)
        var path = new VectorPathBuilder().MoveTo(1, 1.5).LineTo(4, 1.5).LineTo(1, 1.5).Build();
        (LineJoin Join, int Beyond)[] cases = [(LineJoin.Miter, 0), (LineJoin.Bevel, 0), (LineJoin.Round, 90)];
        foreach (var (join, beyond) in cases)
        {
            using var image = NewBlack(5, 3);
            image.Frames[0].Draw(canvas => canvas.Stroke(new Pen(White, 1) { LineJoin = join }, path), Ct);
            AssertGray8(image, [[0, 0, 0, 0, 0], [0, 255, 255, 255, beyond], [0, 0, 0, 0, 0]]);
        }
    }

    [Fact]
    public void OverlappingPartsOfAStrokeAreNotPaintedTwice()
    {
        // A translucent stroke that crosses itself: (1,2.5) -> (4,2.5) -> (4,0.5) -> (2.5,0.5) -> (2.5,4), width 1. Every
        // covered pixel has the same value as a pixel covered once, also where the two bars cross and at the joins:
        // white at 128/255 over black is 128
        var path = new VectorPathBuilder().MoveTo(1, 2.5).LineTo(4, 2.5).LineTo(4, 0.5).LineTo(2.5, 0.5).LineTo(2.5, 4).Build();
        using var image = NewBlack(6, 5);
        image.Frames[0].Draw(canvas => canvas.Stroke(new Pen(new Rgba32(255, 255, 255, 128), 1), path), Ct);
        var frame = image.Frames[0];
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 6; x++)
            {
                Assert.True(frame[x, y].Value <= 128, $"({x}, {y}) is {frame[x, y].Value}");
            }
        }

        // The crossing of the horizontal bar (row 2) and of the vertical bar (x 2 to 3)
        Assert.Equal(128, frame[2, 2].Value);
        Assert.Equal(128, frame[1, 2].Value);
    }

    [Fact]
    public void AStrokeWiderThanTheCanvasCoversIt()
    {
        // A pen much wider than the canvas: every pixel is within half the width of the path, whatever the path. A
        // translucent paint shows that the many pieces of such a stroke are painted once.
        var paint = new SolidBrush(new Rgba32(255, 255, 255, 128));
        var curve = new VectorPathBuilder().MoveTo(-300, 2).CubicBezierTo(100, -4000, 3, 5000, 400, 3).QuadraticBezierTo(2, 2, 3, 3).AddCircle(new PointD(2, 2), 50).Build();
        (double Width, LineJoin Join, LineCap Cap)[] pens = [(1000, LineJoin.Miter, LineCap.Butt), (1000, LineJoin.Round, LineCap.Round), (16777216, LineJoin.Bevel, LineCap.Square), (16777216, LineJoin.Round, LineCap.Round)];
        foreach (var (width, join, cap) in pens)
        {
            using var image = NewBlack(6, 4);
            image.Frames[0].Draw(canvas => canvas.Stroke(new Pen(paint, width) { LineJoin = join, LineCap = cap }, curve), Ct);
            AssertGray8(image, [[128, 128, 128, 128, 128, 128], [128, 128, 128, 128, 128, 128], [128, 128, 128, 128, 128, 128], [128, 128, 128, 128, 128, 128]]);
        }

        // Wide, but not enough to reach the far side from where the path is: a horizontal line 6 pixels wide along the
        // top of a canvas 4 pixels high covers its first three rows only
        using (var image = NewBlack(6, 4))
        {
            image.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(paint, 6), new PointD(-10, 0), new PointD(20, 0)), Ct);
            AssertGray8(image, [[128, 128, 128, 128, 128, 128], [128, 128, 128, 128, 128, 128], [128, 128, 128, 128, 128, 128], [0, 0, 0, 0, 0, 0]]);
        }

        // A dot whose square cap is exactly the canvas, and one whose round cap is too small for the corners
        using (var image = NewBlack(4, 4))
        {
            image.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(paint, 4) { LineCap = LineCap.Square }, new PointD(2, 2), new PointD(2, 2)), Ct);
            AssertGray8(image, [[128, 128, 128, 128], [128, 128, 128, 128], [128, 128, 128, 128], [128, 128, 128, 128]]);
        }

        using (var image = NewBlack(4, 4))
        {
            image.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(paint, 4) { LineCap = LineCap.Round }, new PointD(2, 2), new PointD(2, 2)), Ct);
            Assert.True(image.Frames[0][0, 0].Value < 128);
            Assert.Equal(128, image.Frames[0][1, 1].Value);
        }
    }

    [Fact]
    public void ACircleIsStrokedSymmetrically()
    {
        using var image = NewBlack(16, 16);
        image.Frames[0].Draw(canvas => canvas.StrokeCircle(new Pen(White, 2), new PointD(8, 8), 5), Ct);
        var frame = image.Frames[0];
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                var value = frame[x, y].Value;
                Assert.Equal(value, frame[15 - x, y].Value);
                Assert.Equal(value, frame[x, 15 - y].Value);
                Assert.Equal(value, frame[y, x].Value);
            }
        }

        // The ring lies between the radii 4 and 6: the center and the corners are outside, the pixels half way are inside
        Assert.Equal(0, frame[8, 8].Value);
        Assert.Equal(0, frame[0, 0].Value);
        Assert.Equal(0, frame[8, 5].Value);
        Assert.Equal(255, frame[8, 3].Value);
        Assert.Equal(255, frame[12, 8].Value);
        Assert.Equal(0, frame[8, 1].Value);
    }

    [Fact]
    public void APenWiderThanACurveSweepsThePerpendicularToTheCurve()
    {
        // The arcs of html/canvas/element/path-objects of the web-platform-tests, on a canvas of 100 by 50. A stroke is
        // what the segment of the width of the pen perpendicular to the path sweeps: on the other side of the center of
        // an arc narrower than the pen, it reaches the width minus the radius, and its ends are perpendicular to the arc.
        // 2d.path.arc.shape.3: a pen of 100 on an arc of radius 50 around (0, 50), the long way from 0 to -90 degrees,
        // leaves the quarter between those angles unpainted, up to the center
        AssertStroke(new Pen(White, 100), path => path.AddArc(new PointD(0, 50), 50, 50, 0, 270), painted: [], unpainted: [(50, 25), (1, 1), (98, 1), (1, 48), (98, 48)]);

        // 2d.path.arc.shape.4: a pen of 150 on the quarter of radius 100 around (-50, 50) covers the canvas
        AssertStroke(new Pen(White, 150), path => path.AddArc(new PointD(-50, 50), 100, 100, 0, -90), painted: [(50, 25), (1, 1), (98, 1), (1, 48), (98, 48)], unpainted: []);

        // 2d.path.arc.selfintersect.1 and .2: pens of 200 and 180 on quarters of radius 25
        AssertStroke(new Pen(White, 200), path => path.AddArc(new PointD(100, 50), 25, 25, 0, -90).AddArc(new PointD(0, 0), 25, 25, 0, -90), painted: [], unpainted: [(1, 1), (50, 25)]);
        AssertStroke(new Pen(White, 180), path => path.AddArc(new PointD(-50, 50), 25, 25, 0, -90).AddArc(new PointD(100, 0), 25, 25, 0, -90), painted: [(50, 25), (90, 10), (97, 1), (97, 2), (97, 3), (2, 48)], unpainted: []);

        // 2d.path.arc.scale.2: scaled by 100, a pen of 1.2 on a quarter of radius 0.6 around the origin covers the canvas
        using var image = NewBlack(100, 50);
        image.Frames[0].Draw(canvas => canvas.Transform(AffineTransform.CreateScale(100, 100), scaled => scaled.StrokeArc(new Pen(White, 1.2), default, 0.6, 0, 90)), Ct);
        foreach (var (x, y) in new[] { (1, 1), (50, 1), (98, 1), (1, 25), (50, 25), (98, 25), (1, 48), (50, 48), (98, 48) })
        {
            Assert.Equal((x, y, 255), (x, y, (int)image.Frames[0][x, y].Value));
        }

        static void AssertStroke(Pen pen, Func<VectorPathBuilder, VectorPathBuilder> build, (int X, int Y)[] painted, (int X, int Y)[] unpainted)
        {
            using var image = NewBlack(100, 50);
            var path = build(new VectorPathBuilder()).Build();
            image.Frames[0].Draw(canvas => canvas.Stroke(pen, path), Ct);
            foreach (var (x, y) in painted)
            {
                Assert.Equal((x, y, 255), (x, y, (int)image.Frames[0][x, y].Value));
            }

            foreach (var (x, y) in unpainted)
            {
                Assert.Equal((x, y, 0), (x, y, (int)image.Frames[0][x, y].Value));
            }
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Dashes
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ADashPatternAlternatesDashesAndGapsAlongThePath()
    {
        // Width 1 through the middle of a row of 9 pixels, from x = 0 to x = 9
        // Dashes of 2 and gaps of 1
        AssertDashedRow([2, 1], 0, LineCap.Butt, [255, 255, 0, 255, 255, 0, 255, 255, 0]);

        // A pattern of an odd number of lengths is used twice: dashes of 2, gaps of 2
        AssertDashedRow([2], 0, LineCap.Butt, [255, 255, 0, 0, 255, 255, 0, 0, 255]);
        AssertDashedRow([1, 2, 3], 0, LineCap.Butt, [255, 0, 0, 255, 255, 255, 0, 255, 255]);

        // Lengths are not rounded: dashes and gaps of 1.5 (half a pixel is 127.5 -> 128)
        AssertDashedRow([1.5, 1.5], 0, LineCap.Butt, [255, 128, 0, 255, 128, 0, 255, 128, 0]);

        // The offset is where the path starts in the pattern: 1 into a dash of 2; 2.5 is half way through the gap
        AssertDashedRow([2, 1], 1, LineCap.Butt, [255, 0, 255, 255, 0, 255, 255, 0, 255]);
        AssertDashedRow([2, 1], 2.5, LineCap.Butt, [128, 255, 128, 128, 255, 128, 128, 255, 128]);

        // A negative offset starts before the pattern, and whole patterns change nothing
        AssertDashedRow([2, 1], -1, LineCap.Butt, [0, 255, 255, 0, 255, 255, 0, 255, 255]);
        AssertDashedRow([2, 1], 301, LineCap.Butt, [255, 0, 255, 255, 0, 255, 255, 0, 255]);
        AssertDashedRow([2, 1], -299, LineCap.Butt, [255, 0, 255, 255, 0, 255, 255, 0, 255]);

        // A pattern longer than the path
        AssertDashedRow([20, 5], 0, LineCap.Butt, [255, 255, 255, 255, 255, 255, 255, 255, 255]);
        AssertDashedRow([20, 5], 21, LineCap.Butt, [0, 0, 0, 0, 255, 255, 255, 255, 255]);

        // A gap without length: the dashes touch
        AssertDashedRow([2, 0], 0, LineCap.Butt, [255, 255, 255, 255, 255, 255, 255, 255, 255]);
    }

    [Fact]
    public void EveryDashHasTheCapsOfThePen()
    {
        // Dashes of 2 and gaps of 3, square caps of half the width (1/2): each dash covers half a pixel more on each side
        AssertDashedRow([2, 3], 0, LineCap.Square, [255, 255, 128, 0, 128, 255, 255, 128, 0]);

        // Round caps: the half disk of radius 1/2 is 4 triangles, 0.35355 of a pixel -> 90
        AssertDashedRow([2, 3], 0, LineCap.Round, [255, 255, 90, 0, 90, 255, 255, 90, 0]);

        // A dash without length is its two caps: nothing, a square of the width of the pen, a disk (two half disks)
        AssertDashedRow([0, 3], 0, LineCap.Butt, [0, 0, 0, 0, 0, 0, 0, 0, 0]);
        AssertDashedRow([0, 3], 1.5, LineCap.Square, [0, 255, 0, 0, 255, 0, 0, 255, 0]);
        AssertDashedRow([0, 3], 1.5, LineCap.Round, [0, 180, 0, 0, 180, 0, 0, 180, 0]);

        // A square dot is turned to the direction of the path: on a diagonal, a square of side 2 is a diamond that reaches
        // sqrt(2) from its center along the axes. It covers the pixel around its center; of a pixel next to that one, the
        // trapezoid of area sqrt(2) - 1/2 - 1/4 = 0.6642 (-> 169); of a pixel in diagonal, the triangle of legs
        // sqrt(2) - 1, of area 0.0858 (-> 22)
        using var image = NewBlack(5, 5);
        var pen = new Pen(White, 2) { DashPattern = [0, 100], DashOffset = -Math.Sqrt(12.5), LineCap = LineCap.Square };
        image.Frames[0].Draw(canvas => canvas.StrokeLine(pen, new PointD(0, 0), new PointD(5, 5)), Ct);
        AssertGray8(image, [[0, 0, 0, 0, 0], [0, 22, 169, 22, 0], [0, 169, 255, 169, 0], [0, 22, 169, 22, 0], [0, 0, 0, 0, 0]]);
    }

    [Fact]
    public void ADashFollowsThePathThroughItsVertices()
    {
        // A dash from 2 to 6 along a path that turns at 4: the stroke of the same polyline, with its join
        var corner = new VectorPathBuilder().MoveTo(0.5, 1.5).LineTo(4.5, 1.5).LineTo(4.5, 6.5).Build();
        var dash = new VectorPathBuilder().MoveTo(2.5, 1.5).LineTo(4.5, 1.5).LineTo(4.5, 3.5).Build();
        foreach (var join in new[] { LineJoin.Miter, LineJoin.Round, LineJoin.Bevel })
        {
            var dashed = new Pen(White, 1.5) { DashPattern = [4, 100], DashOffset = -2, LineJoin = join, LineCap = LineCap.Square };
            var solid = new Pen(White, 1.5) { LineJoin = join, LineCap = LineCap.Square };
            AssertSamePixels(canvas => canvas.Stroke(dashed, corner), canvas => canvas.Stroke(solid, dash));
        }

        // Along a curve: the vertices the flattening creates stay smooth inside a dash
        var curve = new VectorPathBuilder().MoveTo(1, 7).CubicBezierTo(1, 1, 7, 1, 7, 7).Build();
        var whole = new Pen(White, 2.5) { DashPattern = [1000, 1] };
        AssertSamePixels(canvas => canvas.Stroke(whole, curve), canvas => canvas.Stroke(new Pen(White, 2.5), curve));

        // Every contour starts the pattern over
        var two = new VectorPathBuilder().MoveTo(0, 1.5).LineTo(8, 1.5).MoveTo(0, 4.5).LineTo(8, 4.5).Build();
        using var image = NewBlack(8, 6);
        image.Frames[0].Draw(canvas => canvas.Stroke(new Pen(White, 1) { DashPattern = [3, 2] }, two), Ct);
        AssertGray8(image, [[0, 0, 0, 0, 0, 0, 0, 0], [255, 255, 255, 0, 0, 255, 255, 255], [0, 0, 0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0, 0, 0], [255, 255, 255, 0, 0, 255, 255, 255], [0, 0, 0, 0, 0, 0, 0, 0]]);
    }

    [Fact]
    public void TheLastDashOfAClosedContourContinuesWithTheFirstOne()
    {
        // A square of side 4 (16 around), dashes of 6 and gaps of 2, starting 3 into a dash: the dashes are 0-3, 5-11 and
        // 13-16, and the last one continues with the first one through the first corner, which is joined, not capped
        var square = new VectorPathBuilder().MoveTo(2, 2).LineTo(6, 2).LineTo(6, 6).LineTo(2, 6).Close().Build();
        var dashes = new VectorPathBuilder()
            .MoveTo(2, 5).LineTo(2, 2).LineTo(5, 2)
            .MoveTo(6, 3).LineTo(6, 6).LineTo(3, 6)
            .Build();
        foreach (var join in new[] { LineJoin.Miter, LineJoin.Round, LineJoin.Bevel })
        {
            var dashed = new Pen(White, 1.5) { DashPattern = [6, 2], DashOffset = 3, LineJoin = join };
            AssertSamePixels(canvas => canvas.Stroke(dashed, square), canvas => canvas.Stroke(new Pen(White, 1.5) { LineJoin = join }, dashes));
            AssertSamePixels(canvas => canvas.StrokeRectangle(dashed, new RectangleD(2, 2, 4, 4)), canvas => canvas.Stroke(new Pen(White, 1.5) { LineJoin = join }, dashes));
        }

        // The corner is joined: a miter fills the pixel outside the corner, which two butt caps would leave empty
        using (var image = NewBlack(8, 8))
        {
            image.Frames[0].Draw(canvas => canvas.Stroke(new Pen(White, 2) { DashPattern = [6, 2], DashOffset = 3 }, square), Ct);
            Assert.Equal(new Gray8(255), image.Frames[0][1, 1]);
        }

        // When the contour ends in a gap, the first dash has a cap at the first point
        using (var image = NewBlack(8, 8))
        {
            image.Frames[0].Draw(canvas => canvas.Stroke(new Pen(White, 2) { DashPattern = [6, 2] }, square), Ct);
            Assert.Equal(new Gray8(0), image.Frames[0][1, 1]);
            Assert.Equal(new Gray8(255), image.Frames[0][2, 1]);
        }

        // A closed contour shorter than its first dash is stroked whole
        AssertSamePixels(canvas => canvas.Stroke(new Pen(White, 1.5) { DashPattern = [100, 5] }, square), canvas => canvas.Stroke(new Pen(White, 1.5), square));
        var circle = new VectorPathBuilder().AddCircle(new PointD(4, 4), 2.5).Build();
        AssertSamePixels(canvas => canvas.Stroke(new Pen(White, 1.5) { DashPattern = [100, 5] }, circle), canvas => canvas.Stroke(new Pen(White, 1.5), circle));

        // An open contour that comes back to its first point has caps there
        var open = new VectorPathBuilder().MoveTo(2, 2).LineTo(6, 2).LineTo(6, 6).LineTo(2, 6).LineTo(2, 2).Build();
        AssertSamePixels(canvas => canvas.Stroke(new Pen(White, 1.5) { DashPattern = [100, 5] }, open), canvas => canvas.Stroke(new Pen(White, 1.5), open));
    }

    [Fact]
    public void ADashedCircleHasItsDashesWhereThePatternSaysAlongIt()
    {
        // Eight dashes and eight gaps of the same length around a circle, which starts at the right of its center and
        // turns clockwise on the image: the middle of dash k is at 11.25 + 45 k degrees, the middle of gap k 22.5 further
        var circle = new VectorPathBuilder().AddCircle(new PointD(32, 32), 24).Build();
        var length = circle.GetLength();
        var pen = new Pen(White, 4) { DashPattern = [length / 16] };
        using var image = NewBlack(64, 64);
        image.Frames[0].Draw(canvas => canvas.Stroke(pen, circle), Ct);
        long painted = 0;
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                foreach (var pixel in pixels.GetRowSpan(y))
                {
                    painted += pixel.Value;
                }
            }
        });

        // Half of the stroke of the circle
        Assert.Equal(length * 4 / 2, painted / 255.0, 3.0);
        for (var k = 0; k < 8; k++)
        {
            var (sin, cos) = Math.SinCos((11.25 + (45 * k)) * Math.PI / 180);
            Assert.Equal(new Gray8(255), image.Frames[0][(int)(32 + (24 * cos)), (int)(32 + (24 * sin))]);
            (sin, cos) = Math.SinCos((33.75 + (45 * k)) * Math.PI / 180);
            Assert.Equal(new Gray8(0), image.Frames[0][(int)(32 + (24 * cos)), (int)(32 + (24 * sin))]);
        }
    }

    [Fact]
    public void WhatIsOutsideTheCanvasIsNotCutIntoDashesAndThePatternGoesOn()
    {
        // A line of two million pixels through a canvas of 10: the pattern is where the length of the line puts it.
        // The pixel 0 is 1000000 - 0.25 from the start, which is 4.75 into a pattern of 5: the dashes of 3 start at
        // x = 0.25, 5.25, ...
        using (var image = NewBlack(10, 3))
        {
            var pen = new Pen(White, 1) { DashPattern = [3, 2] };
            image.Frames[0].Draw(canvas => canvas.StrokeLine(pen, new PointD(-1000000 + 0.25, 1.5), new PointD(1000000, 1.5)), Ct);
            AssertGray8(image, [[0, 0, 0, 0, 0, 0, 0, 0, 0, 0], [191, 255, 255, 64, 0, 191, 255, 255, 64, 0], [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]]);
        }

        // The shortest pattern on the longest line: billions of dashes outside the canvas, a few thousand inside
        using (var image = NewBlack(4, 3))
        {
            var pen = new Pen(White, 1) { DashPattern = [1.0 / 2048, 1.0 / 2048] };
            image.Frames[0].Draw(canvas => canvas.StrokeLine(pen, new PointD(-16777216, 1.5), new PointD(16777216, 1.5)), Ct);
            AssertGray8(image, [[0, 0, 0, 0], [128, 128, 128, 128], [0, 0, 0, 0]]);
        }

        // A path that leaves the canvas and comes back: the same pixels as the same path on a canvas that shows all of it
        var path = new VectorPathBuilder().MoveTo(2, 2).LineTo(300, 2).LineTo(300, 300).LineTo(5.5, 300).LineTo(5.5, -200).LineTo(2, 7).Close().Build();
        var dashed = new Pen(White, 1.5) { DashPattern = [2.5, 1.25, 0.5, 1.25], LineCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var small = NewBlack(8, 8);
        using var large = NewBlack(320, 320);
        small.Frames[0].Draw(canvas => canvas.Stroke(dashed, path), Ct);
        large.Frames[0].Draw(canvas => canvas.Stroke(dashed, path), Ct);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                Assert.True(Math.Abs(small.Frames[0][x, y].Value - large.Frames[0][x, y].Value) <= 1, $"pixel ({x}, {y}): {small.Frames[0][x, y].Value} and {large.Frames[0][x, y].Value}");
            }
        }
    }

    [Fact]
    public void AWidePenIsNotCutIntoTheDashesTheCanvasDoesNotShow()
    {
        // The time a stroke takes if it is cut into every dash of its path: more than the test allows
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));

        // A pen a million pixels wide, the shortest pattern, the longest line: the dashes are slivers across the whole
        // canvas, which cover half of it. Only those the canvas shows are cut.
        using (var image = NewBlack(4, 3))
        {
            var pen = new Pen(White, 1000000) { DashPattern = [1.0 / 2048] };
            image.Frames[0].Draw(canvas => canvas.StrokeLine(pen, new PointD(-16777216, 1.5), new PointD(16777216, 1.5)), timeout.Token);
            AssertGray8(image, [[128, 128, 128, 128], [128, 128, 128, 128], [128, 128, 128, 128]]);
        }

        // With round caps, each dash is a disk a million pixels wide: the first one that covers the canvas ends the stroke
        using (var image = NewBlack(4, 3))
        {
            var pen = new Pen(White, 1000000) { DashPattern = [1.0 / 2048], LineCap = LineCap.Round };
            image.Frames[0].Draw(canvas => canvas.StrokeLine(pen, new PointD(-16777216, 1.5), new PointD(16777216, 1.5)), timeout.Token);
            AssertGray8(image, [[255, 255, 255, 255], [255, 255, 255, 255], [255, 255, 255, 255]]);
        }

        // Round caps 16 million pixels wide on the dashes that reach the canvas, their centers far from it: only the
        // parts of their circles near the canvas are cut finely (cut finely everywhere, the 20,000 dashes before the
        // one that covers the canvas take minutes)
        using (var image = NewBlack(4, 3))
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            budget.CancelAfter(TimeSpan.FromSeconds(60));
            var pen = new Pen(White, 16777216) { DashPattern = [400, 20], LineCap = LineCap.Round };
            image.Frames[0].Draw(canvas => canvas.StrokeLine(pen, new PointD(-16777216, 8388598), new PointD(16777216, 8388598)), budget.Token);
            AssertGray8(image, [[255, 255, 255, 255], [255, 255, 255, 255], [255, 255, 255, 255]]);
        }

        // Dots 4 million pixels wide, one every half pixel: the first one that covers the canvas ends the stroke. A disk ends on
        // its first point, and that closing side without length must not keep it from covering the canvas: otherwise
        // the millions of dots that reach the canvas exceed the allocation limit.
        using (var image = NewBlack(4, 3))
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            budget.CancelAfter(TimeSpan.FromSeconds(60));
            var pen = new Pen(White, 4000000) { DashPattern = [0, 0.5], LineCap = LineCap.Round };
            image.Frames[0].Draw(canvas => canvas.StrokeLine(pen, new PointD(-4000000, 1.5), new PointD(2, 1.5)), budget.Token);
            AssertGray8(image, [[255, 255, 255, 255], [255, 255, 255, 255], [255, 255, 255, 255]]);
        }

        // Far from the canvas across the line, nothing is cut at all
        using (var image = NewBlack(4, 3))
        {
            var pen = new Pen(White, 10) { DashPattern = [1.0 / 2048] };
            image.Frames[0].Draw(canvas => canvas.StrokeLine(pen, new PointD(-16777216, 100), new PointD(16777216, 100.5)), timeout.Token);
            AssertGray8(image, [[0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0]]);
        }
    }

    [Fact]
    public void TheMiterLimitDoesNotChangeHowFinelyAnEllipseIsCutInPerspective()
    {
        // An ellipse narrower than the pen, on a plane whose horizon is about 100 pixels away from it. The miters of the
        // pen once made the whole curve reach that horizon, where it was cut 65,536 times finer: 160,000 crossing
        // pieces, 33 s per stroke (fuzzing), even with bevel joins. An ellipse has no corner, so no miter.
        var transform = new ProjectiveTransform(0.5, 0, 0, -0.4854, -0.4854, -0.00735, 66.24, 66.24, 1.0037);
        var center = new PointD(23.2, -1);
        foreach (var join in (LineJoin[])[LineJoin.Miter, LineJoin.Bevel, LineJoin.Round])
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            budget.CancelAfter(TimeSpan.FromSeconds(10));
            using var limited = NewBlack(133, 132);
            using var unlimited = NewBlack(133, 132);
            limited.Frames[0].Draw(canvas => canvas.Transform(transform, plane => plane.StrokeEllipse(new Pen(White, 57.6) { LineJoin = join, MiterLimit = 1 }, center, 9.4, 33.4)), budget.Token);
            unlimited.Frames[0].Draw(canvas => canvas.Transform(transform, plane => plane.StrokeEllipse(new Pen(White, 57.6) { LineJoin = join, MiterLimit = 588 }, center, 9.4, 33.4)), budget.Token);
            Assert.Equal(DrawingTestSupport.ReadSamples(limited.Frames[0]), DrawingTestSupport.ReadSamples(unlimited.Frames[0]));
            Assert.Contains(DrawingTestSupport.ReadSamples(limited.Frames[0]), sample => sample != 0);
        }
    }

    [Fact]
    public void AJoinOutsideTheCanvasStillShowsWhereItReachesIt()
    {
        // A sharp corner on the right of the canvas, pointing to the left: its two segments stay outside, and only the
        // tip of its miter crosses the canvas. A dash that goes through the corner draws it like a stroke without dashes.
        var corner = new VectorPathBuilder().MoveTo(40, 2).LineTo(12, 4).LineTo(40, 6).Build();
        var solid = new Pen(White, 2) { MiterLimit = 20 };
        using (var image = NewBlack(8, 8))
        {
            image.Draw(canvas => canvas.Stroke(solid, corner), Ct);
            Assert.True(image.Frames[0][4, 3].Value > 0 && image.Frames[0][4, 4].Value > 0);
            Assert.Equal(new Gray8(0), image.Frames[0][4, 1]);
        }

        AssertSamePixels(canvas => canvas.Stroke(new Pen(White, 2) { MiterLimit = 20, DashPattern = [1000, 1] }, corner), canvas => canvas.Stroke(solid, corner));
        AssertSamePixels(canvas => canvas.Stroke(new Pen(White, 2) { MiterLimit = 20, DashPattern = [20, 1], DashOffset = 10 }, corner), canvas => canvas.Stroke(solid, corner));

        // A dash that ends before the corner leaves no miter: two butt caps near the corner, outside the canvas
        using (var image = NewBlack(8, 8))
        {
            image.Draw(canvas => canvas.Stroke(new Pen(White, 2) { MiterLimit = 20, DashPattern = [27, 2] }, corner), Ct);
            Assert.Equal(new int[64], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // The cap of an open contour that ends outside the canvas, a square cap of half the width 6
        var line = new VectorPathBuilder().MoveTo(40, 4).LineTo(12, 4).Build();
        var capped = new Pen(White, 12) { LineCap = LineCap.Square };
        AssertSamePixels(canvas => canvas.Stroke(new Pen(White, 12) { LineCap = LineCap.Square, DashPattern = [1000, 1] }, line), canvas => canvas.Stroke(capped, line));
    }

    [Fact]
    public void ADashedStrokeIsBoundedByTheAllocationLimitOfTheImage()
    {
        // Sixty-four thousand dashes on the canvas need more edges than the limit allows: nothing is drawn
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 1024 * 1024 } };
        using var image = new Image<Gray8>(64, 4, new Gray8(0), configuration);
        var pen = new Pen(White, 1) { DashPattern = [1.0 / 2048] };
        Assert.Throws<ImageResourceLimitException>(() => image.Frames[0].Draw(canvas => canvas.StrokeLine(pen, new PointD(0, 1.5), new PointD(64, 1.5)), Ct));
        Assert.Equal(new int[256], DrawingTestSupport.ReadSamples(image.Frames[0]));

        // The image can still be drawn on
        image.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(White, 1) { DashPattern = [1, 1] }, new PointD(0, 1.5), new PointD(4, 1.5)), Ct);
        Assert.Equal([255, 0, 255, 0], DrawingTestSupport.ReadSamples(image.Frames[0])[64..68]);
    }

    [Fact]
    public void ADashPatternIsValidatedAndCopied()
    {
        Assert.True(new Pen(White, 1).DashPattern.IsEmpty);
        Assert.Equal(0, new Pen(White, 1).DashOffset);
        double[] lengths = [3, 1.5, 0];
        var pen = new Pen(White, 1) { DashPattern = lengths, DashOffset = -2.5 };
        lengths[0] = 100;
        Assert.Equal([3, 1.5, 0], pen.DashPattern.ToArray());
        Assert.Equal(-2.5, pen.DashOffset);
        Assert.True(new Pen(White, 1) { DashPattern = [] }.DashPattern.IsEmpty);

        Assert.Throws<ArgumentException>(() => new Pen(White, 1) { DashPattern = [1, double.NaN] });
        Assert.Throws<ArgumentException>(() => new Pen(White, 1) { DashPattern = [double.PositiveInfinity] });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pen(White, 1) { DashPattern = [1, -1] });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pen(White, 1) { DashPattern = [16777217] });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pen(White, 1) { DashPattern = [0, 0] });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pen(White, 1) { DashPattern = [1.0 / 4096, 1.0 / 4096] });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pen(White, 1) { DashPattern = [1.0 / 4096] });
        Assert.HasCount(1, new Pen(White, 1) { DashPattern = [1.0 / 2048] }.DashPattern);
        Assert.HasCount(2, new Pen(White, 1) { DashPattern = [1.0 / 2048, 1.0 / 2048] }.DashPattern);
        Assert.Throws<ArgumentException>(() => new Pen(White, 1) { DashOffset = double.NaN });
        Assert.Throws<ArgumentException>(() => new Pen(White, 1) { DashOffset = double.NegativeInfinity });
    }

    [Fact]
    public void FillingTheWidenedPathOfADashedPenIsStrokingWithIt()
    {
        var path = new VectorPathBuilder()
            .MoveTo(1, 1).LineTo(6.5, 1.25).LineTo(2, 3).QuadraticBezierTo(7, 4, 6, 7).Close()
            .MoveTo(0.75, 5).CubicBezierTo(2, 3.5, 3, 8.5, 4.5, 6.25)
            .AddCircle(new PointD(4, 4), 2.75)
            .Build();
        foreach (var pen in new[]
        {
            new Pen(White, 1) { DashPattern = [1.5, 1] },
            new Pen(White, 0.75) { DashPattern = [0, 1.25], LineCap = LineCap.Round },
            new Pen(White, 1.25) { DashPattern = [2, 0.5, 0, 0.5], DashOffset = 0.75, LineCap = LineCap.Square, LineJoin = LineJoin.Round },
            new Pen(White, 1) { DashPattern = [7], DashOffset = 3, LineJoin = LineJoin.Bevel },
        })
        {
            var widened = path.Widen(pen);
            AssertSamePixels(canvas => canvas.Stroke(pen, path), canvas => canvas.Fill(White, widened));
        }
    }

    [Fact]
    public void AnInsideOrAnOutsideStrokeIsOnOneSideOfItsPath()
    {
        // The square from 4 to 12, and a pen of width 2: inside, the stroke is the ring from 4 to 6; outside, the ring
        // from 2 to 4, with the corners of its miter joins. Everything is on whole pixels: the pixels are all or nothing.
        var square = new Rectangle(4, 4, 8, 8);
        foreach (var (alignment, outer, inner) in new[] { (PenAlignment.Inside, 4, 6), (PenAlignment.Outside, 2, 4), (PenAlignment.Center, 3, 5) })
        {
            var pen = new Pen(White, 2) { Alignment = alignment };
            Assert.Equal(alignment, pen.Alignment);
            using var image = NewBlack(16, 16);
            image.Frames[0].Draw(canvas => canvas.StrokeRectangle(pen, square), Ct);
            using var path = NewBlack(16, 16);
            path.Frames[0].Draw(canvas => canvas.Stroke(pen, new VectorPathBuilder().AddRectangle(square).Build()), Ct);
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    var isInRing = Math.Min(x, y) >= outer && Math.Max(x, y) < 16 - outer && !(Math.Min(x, y) >= inner && Math.Max(x, y) < 16 - inner);
                    Assert.Equal(isInRing ? 255 : 0, image.Frames[0][x, y].Value, $"{alignment}, pixel ({x}, {y})");
                    Assert.Equal(image.Frames[0][x, y], path.Frames[0][x, y]);
                }
            }
        }

        // Around a disk of radius 5: an inside stroke changes nothing beyond the circle, an outside stroke nothing within
        // it, and each covers entirely the pixels that are in its ring of width 2 (the diagonal of a pixel is 1.42 long)
        var center = new PointD(8, 8);
        foreach (var alignment in new[] { PenAlignment.Inside, PenAlignment.Outside })
        {
            using var image = NewBlack(16, 16);
            image.Frames[0].Draw(canvas => canvas.StrokeCircle(new Pen(White, 2) { Alignment = alignment }, center, 5), Ct);
            var (from, to) = alignment == PenAlignment.Inside ? (3d, 5d) : (5d, 7d);
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    var distance = new PointD(x + 0.5, y + 0.5).DistanceTo(center);
                    var value = image.Frames[0][x, y].Value;
                    if (distance < from - 0.8 || distance > to + 0.8)
                    {
                        Assert.Equal(0, value, $"{alignment}, pixel ({x}, {y})");
                    }
                    else if (distance > from + 0.8 && distance < to - 0.8)
                    {
                        Assert.Equal(255, value, $"{alignment}, pixel ({x}, {y})");
                    }
                }
            }
        }

        // The widened path of such a pen is the outline of the side that is kept
        var box = new VectorPathBuilder().AddRectangle(new Rectangle(2, 2, 4, 4)).Build();
        foreach (var alignment in new[] { PenAlignment.Inside, PenAlignment.Outside })
        {
            var pen = new Pen(White, 1) { Alignment = alignment };
            var widened = box.Widen(pen);
            AssertSamePixels(canvas => canvas.Stroke(pen, box), canvas => canvas.Fill(White, widened));
            Assert.Equal(alignment == PenAlignment.Inside, widened.Contains(new PointD(2.5, 4)));
            Assert.Equal(alignment == PenAlignment.Outside, widened.Contains(new PointD(1.5, 4)));
        }

        // A dashed pen, a round join and a text are stroked on one side the same way
        var dashed = new Pen(White, 2) { Alignment = PenAlignment.Inside, DashPattern = [2, 2], LineJoin = LineJoin.Round };
        using var dashes = NewBlack(16, 16);
        dashes.Frames[0].Draw(canvas => canvas.StrokeRectangle(dashed, square), Ct);
        Assert.Equal(255, dashes.Frames[0][4, 4].Value);
        Assert.Equal(0, dashes.Frames[0][6, 4].Value);
        Assert.Equal(0, dashes.Frames[0][3, 4].Value);
        Assert.Equal(0, dashes.Frames[0][8, 8].Value);

        Assert.Throws<ArgumentOutOfRangeException>(() => new Pen(White, 1) { Alignment = (PenAlignment)3 });
    }

    [Fact]
    public void AMarkerIsDrawnAtTheEndOfAnOpenContour()
    {
        // A line of width 2 from (2, 8) to (22, 8) and the head of an arrow at its end: a triangle whose tip is the end
        // of the line, 10 long and 8 wide (5 and 4 times the width). The line stops 4 before the tip.
        //   The upper side of the triangle is y = 8 - 0.4 (22 - x): 4 at its base (x = 12), 7.6 at x = 21.
        //   pixel (12, 4): between y = 4 and 4.4 above, and 5: 1 at its left, 0.6 at its right -> 0.8 -> 204
        //   pixel (21, 7): between 7.6 and 8, and 8: 0.4 at its left, 0 at its right -> 0.2 -> 51
        var pen = new Pen(White, 2) { EndMarker = LineMarker.Arrow };
        Assert.Same(LineMarker.Arrow, pen.EndMarker);
        Assert.Null(pen.StartMarker);
        using var image = NewBlack(24, 16);
        image.Frames[0].Draw(canvas => canvas.StrokeLine(pen, new PointD(2, 8), new PointD(22, 8)), Ct);
        var frame = image.Frames[0];
        Assert.Equal([0, 255, 255, 0], new[] { (int)frame[1, 7].Value, frame[2, 7].Value, frame[5, 8].Value, frame[5, 6].Value });
        Assert.Equal([0, 204, 255, 255, 204, 0], new[] { 3, 4, 5, 10, 11, 12 }.Select(y => (int)frame[12, y].Value));
        Assert.Equal(0, frame[11, 5].Value);
        Assert.Equal(51, frame[21, 7].Value);
        Assert.Equal(51, frame[21, 8].Value);
        Assert.Equal(0, frame[22, 7].Value);
        Assert.Equal(0, frame[22, 8].Value);

        // The widened path has the marker: the tip is the end of the line, exactly
        var line = new VectorPathBuilder().MoveTo(2, 8).LineTo(22, 8).Build();
        Assert.Equal(RectangleD.FromLTRB(2, 4, 22, 12), line.Widen(pen).GetTightBounds());
        using var filled = NewBlack(24, 16);
        filled.Frames[0].Draw(canvas => canvas.Fill(White, line.Widen(pen)), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(frame), DrawingTestSupport.ReadSamples(filled.Frames[0]));

        // At the start, a marker points the other way: away from the line. The line starts 4 after the tip.
        var both = new Pen(White, 2) { StartMarker = LineMarker.Arrow, EndMarker = LineMarker.Arrow };
        Assert.Equal(RectangleD.FromLTRB(2, 4, 22, 12), line.Widen(both).GetTightBounds());
        using var mirrored = NewBlack(24, 16);
        mirrored.Frames[0].Draw(canvas => canvas.Stroke(both, line), Ct);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 24; x++)
            {
                Assert.Equal(mirrored.Frames[0][x, y], mirrored.Frames[0][23 - x, y]);
            }
        }

        Assert.Equal(51, mirrored.Frames[0][2, 7].Value);
        Assert.Equal(0, mirrored.Frames[0][1, 7].Value);

        // The other markers are centered on the end of the contour: 3 widths across, 4 for the diamond
        Assert.Equal(RectangleD.FromLTRB(2, 5, 25, 11), line.Widen(new Pen(White, 2) { EndMarker = LineMarker.Square }).GetTightBounds());
        Assert.Equal(RectangleD.FromLTRB(2, 4, 26, 12), line.Widen(new Pen(White, 2) { EndMarker = LineMarker.Diamond }).GetTightBounds());
        var disk = line.Widen(new Pen(White, 2) { StartMarker = LineMarker.Circle }).GetTightBounds();
        Assert.Equal(-1, disk.Left, 0.05);
        Assert.Equal(5, disk.Top, 0.05);
        Assert.Equal(22, disk.Right);
    }

    [Fact]
    public void AMarkerFollowsTheDirectionOfItsContourAndIsPaintedWithItsStroke()
    {
        // A marker that the stroke goes through, with a translucent paint: what they both cover is painted once
        var triangle = new VectorPathBuilder().AddPolygon([new PointD(0, 0), new PointD(-5, -2), new PointD(-5, 2)]).Build();
        var forward = new LineMarker(triangle);
        var backward = new LineMarker(triangle.Reverse());
        Assert.Same(triangle, forward.Shape);
        Assert.Equal(0, forward.Inset);
        var half = new SolidBrush(new Rgba32(255, 255, 255, 128));
        using var image = NewBlack(24, 16);
        image.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(half, 2) { EndMarker = forward }, new PointD(2, 8), new PointD(22, 8)), Ct);
        Assert.Equal(128, image.Frames[0][5, 7].Value);
        Assert.Equal(128, image.Frames[0][13, 6].Value);
        Assert.Equal(128, image.Frames[0][14, 7].Value);

        // The contours of a marker are filled whichever way they turn
        using var reversed = NewBlack(24, 16);
        reversed.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(half, 2) { EndMarker = backward }, new PointD(2, 8), new PointD(22, 8)), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(image.Frames[0]), DrawingTestSupport.ReadSamples(reversed.Frames[0]));

        // The marker is turned to the direction of the last segment: downward here, the tip at the end of the path
        var pen = new Pen(White, 1) { EndMarker = LineMarker.Arrow, StartMarker = LineMarker.Arrow };
        var corner = new VectorPathBuilder().MoveTo(4, 10).LineTo(14, 10).LineTo(14, 20).Build();
        var bounds = corner.Widen(pen).GetTightBounds();
        Assert.Equal(RectangleD.FromLTRB(4, 8, 16, 20), bounds);

        // A closed contour has no end, and no marker; neither has a contour without length
        var closed = new VectorPathBuilder().MoveTo(4, 4).LineTo(12, 4).LineTo(12, 12).Close().Build();
        Assert.Equal(closed.Widen(new Pen(White, 1)).ToSvgPathData(), closed.Widen(pen).ToSvgPathData());
        var point = new VectorPathBuilder().MoveTo(4, 4).Build();
        var round = new Pen(White, 2) { LineCap = LineCap.Round };
        Assert.Equal(point.Widen(round).ToSvgPathData(), point.Widen(new Pen(White, 2) { LineCap = LineCap.Round, EndMarker = LineMarker.Arrow }).ToSvgPathData());

        // A contour that is not longer than its insets is drawn by its markers alone
        var stub = new VectorPathBuilder().MoveTo(10, 8).LineTo(12, 8).Build();
        Assert.Equal(1, CountContours(stub.Widen(new Pen(White, 2) { EndMarker = LineMarker.Arrow })));
        Assert.Equal(2, CountContours(stub.Widen(new Pen(White, 2) { EndMarker = LineMarker.Arrow, StartMarker = LineMarker.Arrow })));
        Assert.Equal(1, CountContours(stub.Widen(new Pen(White, 2) { EndMarker = LineMarker.Arrow, LineCap = LineCap.Round })));
        Assert.Equal(1, CountContours(stub.Widen(new Pen(White, 2) { EndMarker = LineMarker.Arrow, LineCap = LineCap.Square, DashPattern = [1, 1] })));
        Assert.Equal(2, CountContours(new VectorPathBuilder().MoveTo(2, 8).LineTo(22, 8).Build().Widen(new Pen(White, 2) { EndMarker = LineMarker.Arrow })));

        // With dashes, the markers are at the ends of the contour, and the dashes are those of what is left of it
        var dashed = new VectorPathBuilder().MoveTo(2, 8).LineTo(22, 8).Build().Widen(new Pen(White, 2) { EndMarker = LineMarker.Arrow, DashPattern = [4, 4] });
        Assert.Equal(RectangleD.FromLTRB(2, 4, 22, 12), dashed.GetTightBounds());
        Assert.Equal(3, CountContours(dashed));

        // A transformation of the canvas transforms the markers with the stroke
        using var direct = NewBlack(24, 16);
        direct.Frames[0].Draw(canvas => canvas.StrokeLine(new Pen(White, 2) { EndMarker = LineMarker.Arrow }, new PointD(2, 8), new PointD(22, 8)), Ct);
        using var scaled = NewBlack(24, 16);
        scaled.Frames[0].Draw(canvas => canvas.Transform(AffineTransform.CreateScale(2, 2), inner => inner.StrokeLine(new Pen(White, 1) { EndMarker = LineMarker.Arrow }, new PointD(1, 4), new PointD(11, 4))), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(direct.Frames[0]), DrawingTestSupport.ReadSamples(scaled.Frames[0]));

        Assert.Throws<ArgumentNullException>(() => new LineMarker(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LineMarker(triangle, -1));
        Assert.Throws<ArgumentException>(() => new LineMarker(triangle, double.NaN));
        Assert.Equal(2, LineMarker.Arrow.Inset);

        static int CountContours(VectorPath path)
        {
            var count = 0;
            foreach (var segment in path.EnumerateSegments())
            {
                if (segment.Kind == PathSegmentKind.MoveTo)
                {
                    count++;
                }
            }

            return count;
        }
    }

    [Fact]
    public void ACurveThroughPointsIsStrokedAndFilledAsItsPath()
    {
        PointD[] points = [new(1, 6), new(3, 2), new(5, 5), new(7, 1)];
        var pen = new Pen(White, 1) { LineCap = LineCap.Round };
        AssertSamePixels(canvas => canvas.StrokeCurve(pen, points), canvas => canvas.Stroke(pen, new VectorPathBuilder().AddCurve(points).Build()));
        AssertSamePixels(canvas => canvas.StrokeCurve(pen, points, 0.8), canvas => canvas.Stroke(pen, new VectorPathBuilder().AddCurve(points, 0.8).Build()));
        AssertSamePixels(canvas => canvas.StrokeClosedCurve(pen, points), canvas => canvas.Stroke(pen, new VectorPathBuilder().AddClosedCurve(points).Build()));
        AssertSamePixels(canvas => canvas.FillClosedCurve(White, points), canvas => canvas.Fill(White, new VectorPathBuilder().AddClosedCurve(points).Build()));
        AssertSamePixels(canvas => canvas.FillClosedCurve(White, points, 1, FillRule.EvenOdd), canvas => canvas.Fill(White, new VectorPathBuilder().AddClosedCurve(points, 1).Build(), FillRule.EvenOdd));

        // Without tension, the curve is the straight segments between its points
        AssertSamePixels(canvas => canvas.StrokeCurve(pen, points, 0), canvas => canvas.StrokePolyline(pen, points));
        AssertSamePixels(canvas => canvas.FillClosedCurve(White, points, 0), canvas => canvas.FillPolygon(White, points));

        using var image = NewBlack(8, 8);
        image.Frames[0].Draw(
            canvas =>
            {
                AssertThrows<ArgumentNullException>(canvas, inner => inner.StrokeCurve(null!, points));
                AssertThrows<ArgumentNullException>(canvas, inner => inner.FillClosedCurve(null!, points));
                AssertThrows<ArgumentException>(canvas, inner => inner.StrokeCurve(pen, []));
                AssertThrows<ArgumentException>(canvas, inner => inner.StrokeCurve(pen, points, double.NaN));
                AssertThrows<ArgumentOutOfRangeException>(canvas, inner => inner.StrokeCurve(pen, points, 1.5));
                AssertThrows<ArgumentOutOfRangeException>(canvas, inner => inner.StrokeClosedCurve(pen, points, -0.1));
                AssertThrows<ArgumentException>(canvas, inner => inner.FillClosedCurve(White, [new PointD(double.NaN, 0)]));
                AssertThrows<ArgumentOutOfRangeException>(canvas, inner => inner.FillClosedCurve(White, points, 0.5, (FillRule)2));
                AssertThrows<ArgumentOutOfRangeException>(canvas, inner => inner.StrokeCurve(pen, [new PointD(-16777216, 0), new PointD(16777216, 0), new PointD(16777216, 5)], 1));
            },
            Ct);
        Assert.All(DrawingTestSupport.ReadSamples(image.Frames[0]), sample => Assert.Equal(0, sample));
    }

    private static void AssertThrows<TException>(DrawingCanvas canvas, DrawingAction action)
        where TException : Exception
    {
        try
        {
            action(canvas);
        }
        catch (TException exception) when (exception.GetType() == typeof(TException) || typeof(TException) == typeof(ArgumentException))
        {
            return;
        }

        Assert.Fail($"{typeof(TException).Name} was not thrown.");
    }

    /// <summary>Strokes a horizontal line of width 1 through the middle of a row of 9 pixels and checks that row.</summary>
    private static void AssertDashedRow(double[] pattern, double offset, LineCap cap, int[] expected)
    {
        using var image = NewBlack(9, 3);
        var pen = new Pen(White, 1) { DashPattern = pattern, DashOffset = offset, LineCap = cap };
        image.Frames[0].Draw(canvas => canvas.StrokeLine(pen, new PointD(0, 1.5), new PointD(9, 1.5)), Ct);
        AssertGray8(image, [new int[9], expected, new int[9]]);
    }

    private static void AssertSamePixels(DrawingAction first, DrawingAction second)
    {
        using var left = NewBlack(8, 8);
        using var right = NewBlack(8, 8);
        left.Draw(first, Ct);
        right.Draw(second, Ct);
        var expected = DrawingTestSupport.ReadSamples(left.Frames[0]);
        Assert.Contains(expected, sample => sample != 0);
        Assert.Equal(expected, DrawingTestSupport.ReadSamples(right.Frames[0]));
    }

    private static Image<Gray8> NewBlack(int width, int height) => new(width, height, new Gray8(0));

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
