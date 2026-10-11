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
