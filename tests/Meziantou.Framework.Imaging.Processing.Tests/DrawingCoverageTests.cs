using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Coverage of filled shapes. Every expected sample is a literal derived by hand: the shapes are polygons whose
/// intersection with each pixel square has an area that is computed in the comments, painted in opaque white over
/// opaque black Gray8 pixels, so that a pixel is <c>round(255 * area)</c> with ties upward. Nothing is computed by the
/// code under test; the conformance tests add an exact rational reference (TestHarness/Drawing).
/// </summary>
public sealed class DrawingCoverageTests
{
    private static readonly Brush White = new SolidBrush(new Rgba32(255, 255, 255));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void ARectangleOnPixelBoundariesIsFilledExactly()
    {
        using var image = NewBlack(5, 4);
        image.Frames[0].Draw(canvas => canvas.FillRectangle(White, new Rectangle(1, 1, 3, 2)), Ct);
        AssertGray8(image, [[0, 0, 0, 0, 0], [0, 255, 255, 255, 0], [0, 255, 255, 255, 0], [0, 0, 0, 0, 0]]);
    }

    [Fact]
    public void AFractionalRectangleCoversEachPixelInProportionToTheirIntersection()
    {
        // [0.25, 2.5] x [0.5, 1.75]. Horizontally, the columns are covered by 0.75, 1 and 0.5; vertically, the rows by
        // 0.5 and 0.75.
        //   row 0: 0.375 -> 95.625 -> 96 | 0.5 -> 127.5 -> 128 (tie upward) | 0.25 -> 63.75 -> 64
        //   row 1: 0.5625 -> 143.4375 -> 143 | 0.75 -> 191.25 -> 191 | 0.375 -> 95.625 -> 96
        using var image = NewBlack(4, 2);
        image.Frames[0].Draw(canvas => canvas.FillRectangle(White, RectangleD.FromLTRB(0.25, 0.5, 2.5, 1.75)), Ct);
        AssertGray8(image, [[96, 128, 64, 0], [143, 191, 96, 0]]);
    }

    [Fact]
    public void ATriangleCoversHalfOfThePixelsOnItsDiagonal()
    {
        // (0,0) (4,0) (0,4): the inside is x + y < 4. A pixel with x + y < 3 is inside, the pixels with x + y = 3 are
        // cut along their diagonal (area 1/2 -> 127.5 -> 128), the others are outside.
        var path = new VectorPathBuilder().AddPolygon([new PointD(0, 0), new PointD(4, 0), new PointD(0, 4)]).Build();
        using var image = NewBlack(4, 4);
        image.Frames[0].Draw(canvas => canvas.Fill(White, path), Ct);
        AssertGray8(image, [[255, 255, 255, 128], [255, 255, 128, 0], [255, 128, 0, 0], [128, 0, 0, 0]]);
    }

    [Theory]
    [InlineData(FillRule.NonZero)]
    [InlineData(FillRule.EvenOdd)]
    public void ASelfIntersectingContourIsMeasuredExactlyWhereItCrosses(FillRule fillRule)
    {
        // (0,0) (3,3) (3,0) (0,3): a bow-tie crossing at (1.5, 1.5). The inside is the left triangle (x < y and
        // x < 3 - y, winding +1) and the right triangle (x > y and x > 3 - y, winding -1): the same pixels for both rules.
        //   (0,0) and (0,2): cut along a diagonal by the left triangle -> 1/2. (0,1): inside.
        //   (2,0) and (2,2): cut along a diagonal by the right triangle -> 1/2. (2,1): inside.
        //   (1,0) and (1,2): outside.
        //   (1,1): a quarter from each triangle (the triangles of the pixel that touch its left and right sides) -> 1/2.
        var path = new VectorPathBuilder().AddPolygon([new PointD(0, 0), new PointD(3, 3), new PointD(3, 0), new PointD(0, 3)]).Build();
        using var image = NewBlack(3, 3);
        image.Frames[0].Draw(canvas => canvas.Fill(White, path, fillRule), Ct);
        AssertGray8(image, [[128, 0, 128], [255, 128, 255], [128, 0, 128]]);
    }

    [Fact]
    public void OverlappingContoursCoverTheAreaOfTheirUnionNotTheSumOfTheirAreas()
    {
        // Twice the left half of the pixel, in the same direction: the winding number is 2 there. The union covers
        // half of the pixel (128); the sum of the areas would be the whole pixel. With the even-odd rule, 2 is outside.
        var half = RectangleD.FromLTRB(0, 0, 0.5, 1);
        var path = new VectorPathBuilder().AddRectangle(half).AddRectangle(half).Build();
        using (var image = NewBlack(1, 1))
        {
            image.Frames[0].Draw(canvas => canvas.Fill(White, path), Ct);
            AssertGray8(image, [[128]]);
        }

        using (var image = NewBlack(1, 1))
        {
            image.Frames[0].Draw(canvas => canvas.Fill(White, path, FillRule.EvenOdd), Ct);
            AssertGray8(image, [[0]]);
        }

        // The left half and the top half: the union is three quarters of the pixel (191.25 -> 191); with the even-odd
        // rule the top-left quarter is outside, half of the pixel is left (128)
        var corner = new VectorPathBuilder().AddRectangle(half).AddRectangle(RectangleD.FromLTRB(0, 0, 1, 0.5)).Build();
        using (var image = NewBlack(1, 1))
        {
            image.Frames[0].Draw(canvas => canvas.Fill(White, corner), Ct);
            AssertGray8(image, [[191]]);
        }

        using (var image = NewBlack(1, 1))
        {
            image.Frames[0].Draw(canvas => canvas.Fill(White, corner, FillRule.EvenOdd), Ct);
            AssertGray8(image, [[128]]);
        }
    }

    [Fact]
    public void TheDirectionOfANestedContourDecidesWhetherItIsAHole()
    {
        PointD[] outer = [new(0, 0), new(4, 0), new(4, 4), new(0, 4)];
        PointD[] innerSame = [new(1, 1), new(3, 1), new(3, 3), new(1, 3)];
        PointD[] innerOpposite = [new(1, 1), new(1, 3), new(3, 3), new(3, 1)];
        int[][] full = [[255, 255, 255, 255], [255, 255, 255, 255], [255, 255, 255, 255], [255, 255, 255, 255]];
        int[][] ring = [[255, 255, 255, 255], [255, 0, 0, 255], [255, 0, 0, 255], [255, 255, 255, 255]];

        // Same direction: winding 2 inside the inner square (inside for non-zero, outside for even-odd).
        // Opposite directions: winding 0 there, a hole for both rules.
        (PointD[] Inner, FillRule Rule, int[][] Expected)[] cases =
        [
            (innerSame, FillRule.NonZero, full),
            (innerSame, FillRule.EvenOdd, ring),
            (innerOpposite, FillRule.NonZero, ring),
            (innerOpposite, FillRule.EvenOdd, ring),
        ];
        foreach (var (inner, rule, expected) in cases)
        {
            var path = new VectorPathBuilder().AddPolygon(outer).AddPolygon(inner).Build();
            using var image = NewBlack(4, 4);
            image.Frames[0].Draw(canvas => canvas.Fill(White, path, rule), Ct);
            AssertGray8(image, expected);
        }
    }

    [Fact]
    public void ShapesAreClippedToTheCanvas()
    {
        // A rectangle around the whole canvas, and beyond it on every side: every pixel is inside
        using (var image = NewBlack(3, 2))
        {
            image.Frames[0].Draw(canvas => canvas.FillRectangle(White, RectangleD.FromLTRB(-10, -20, 30, 40)), Ct);
            AssertGray8(image, [[255, 255, 255], [255, 255, 255]]);
        }

        // Half a pixel inside on the left and at the top, beyond the canvas on the other sides
        using (var image = NewBlack(2, 2))
        {
            image.Frames[0].Draw(canvas => canvas.FillRectangle(White, RectangleD.FromLTRB(0.5, 0.5, 100, 100)), Ct);
            AssertGray8(image, [[64, 128], [128, 255]]);
        }

        // Coming from the left and from the top
        using (var image = NewBlack(2, 2))
        {
            image.Frames[0].Draw(canvas => canvas.FillRectangle(White, RectangleD.FromLTRB(-100, -100, 1.5, 1.5)), Ct);
            AssertGray8(image, [[255, 128], [128, 64]]);
        }

        // Entirely outside, on each side
        using (var image = NewBlack(2, 2))
        {
            image.Frames[0].Draw(
                canvas =>
                {
                    canvas.FillRectangle(White, RectangleD.FromLTRB(-5, 0, 0, 2));
                    canvas.FillRectangle(White, RectangleD.FromLTRB(2, 0, 7, 2));
                    canvas.FillRectangle(White, RectangleD.FromLTRB(0, -5, 2, 0));
                    canvas.FillRectangle(White, RectangleD.FromLTRB(0, 2, 2, 7));
                },
                Ct);
            AssertGray8(image, [[0, 0], [0, 0]]);
        }

        // A triangle whose slanted side crosses the left side of the canvas: (-1,0) (1,0) (-1,2) is x + y < 1 right of
        // x = -1. Pixel (0,0) is cut along its diagonal (128); nothing else of the canvas is inside.
        using (var image = NewBlack(2, 2))
        {
            var path = new VectorPathBuilder().AddPolygon([new PointD(-1, 0), new PointD(1, 0), new PointD(-1, 2)]).Build();
            image.Frames[0].Draw(canvas => canvas.Fill(White, path), Ct);
            AssertGray8(image, [[128, 0], [0, 0]]);
        }
    }

    [Fact]
    public void FillPaintsTheWholeCanvas()
    {
        using var image = NewBlack(3, 2);
        image.Frames[0].Draw(canvas => canvas.Fill(White), Ct);
        AssertGray8(image, [[255, 255, 255], [255, 255, 255]]);
    }

    [Fact]
    public void AnOpenContourIsClosedWhenItIsFilled()
    {
        // The triangle of ATriangleCoversHalfOfThePixelsOnItsDiagonal, without its closing segment
        var path = new VectorPathBuilder().MoveTo(0, 0).LineTo(4, 0).LineTo(0, 4).Build();
        using var image = NewBlack(4, 4);
        image.Frames[0].Draw(canvas => canvas.Fill(White, path), Ct);
        AssertGray8(image, [[255, 255, 255, 128], [255, 255, 128, 0], [255, 128, 0, 0], [128, 0, 0, 0]]);
    }

    [Fact]
    public void ACoverageBelowHalfARoundingStepLeavesThePixelUntouched()
    {
        // A sliver 2^-18 wide covers 2^-18 of the pixel: less than half of 1/65536, so the coverage is 0 and the pixel
        // is not written, whatever the paint
        using var image = NewBlack(1, 1);
        image.Frames[0].Draw(canvas => canvas.FillRectangle(White, RectangleD.FromLTRB(0, 0, 1.0 / 262144, 1)), Ct);
        AssertGray8(image, [[0]]);
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
