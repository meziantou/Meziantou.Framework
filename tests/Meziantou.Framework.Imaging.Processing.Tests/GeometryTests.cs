namespace Meziantou.Framework.Imaging.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void RectangleBoundsAreExclusive()
    {
        var rectangle = new Rectangle(2, 3, 4, 5);
        Assert.Equal(6, rectangle.Right);
        Assert.Equal(8, rectangle.Bottom);
        Assert.True(rectangle.Contains(new Point(5, 7)));
        Assert.False(rectangle.Contains(new Point(6, 7)));
        Assert.False(rectangle.Contains(new Point(5, 8)));
    }

    [Fact]
    public void RectangleRejectsOverflowingBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rectangle(int.MaxValue, 0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rectangle(0, int.MaxValue - 1, 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rectangle(0, 0, -1, 1));
        Assert.Equal(int.MaxValue, new Rectangle(int.MaxValue - 1, 0, 1, 1).Right);
    }

    [Fact]
    public void FromLtrbRejectsInvertedBounds()
    {
        Assert.Equal(new Rectangle(1, 2, 3, 4), Rectangle.FromLTRB(1, 2, 4, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rectangle.FromLTRB(5, 0, 4, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rectangle.FromLTRB(int.MinValue, 0, int.MaxValue, 1));
    }

    [Fact]
    public void IntersectReturnsEmptyWhenDisjoint()
    {
        Assert.Equal(new Rectangle(2, 2, 2, 2), Rectangle.Intersect(new Rectangle(0, 0, 4, 4), new Rectangle(2, 2, 4, 4)));
        Assert.Equal(Rectangle.Empty, Rectangle.Intersect(new Rectangle(0, 0, 2, 2), new Rectangle(2, 0, 2, 2)));
        Assert.False(new Rectangle(0, 0, 2, 2).IntersectsWith(new Rectangle(2, 0, 2, 2)));
    }

    [Fact]
    public void SizeRejectsNegativeDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Size(-1, 1));
        Assert.True(new Size(0, 5).IsEmpty);
        Assert.Equal(4_294_836_225L, new Size(65535, 65535).Area);
        Assert.Equal("3x4", new Size(3, 4).ToString());
    }

    [Fact]
    public void RectanglesMayStartAtNegativeCoordinates()
    {
        var rectangle = new Rectangle(-5, -2, 10, 4);
        Assert.Equal((5, 2), (rectangle.Right, rectangle.Bottom));
        Assert.Equal(new Rectangle(0, 0, 5, 2), Rectangle.Intersect(rectangle, new Rectangle(0, 0, 100, 100)));
        Assert.Equal(new Rectangle(int.MinValue, 0, int.MaxValue, 1), Rectangle.FromLTRB(int.MinValue, 0, -1, 1));
        Assert.Equal(-1, Rectangle.FromLTRB(int.MinValue, 0, -1, 1).Right);
    }

    [Fact]
    public void RectangleMembers()
    {
        var rectangle = new Rectangle(new Point(1, 2), new Size(3, 4));
        Assert.Equal((1, 2, 3, 4), (rectangle.Left, rectangle.Top, rectangle.Width, rectangle.Height));
        Assert.Equal(new Point(1, 2), rectangle.Location);
        Assert.Equal(new Size(3, 4), rectangle.Size);
        Assert.False(rectangle.IsEmpty);
        Assert.True(new Rectangle(1, 1, 0, 5).IsEmpty);
        Assert.True(Rectangle.Empty.IsEmpty);
        Assert.True(rectangle.Contains(new Rectangle(1, 2, 3, 4)));
        Assert.True(rectangle.Contains(new Rectangle(2, 3, 1, 1)));
        Assert.False(rectangle.Contains(new Rectangle(2, 3, 3, 1)));
        Assert.True(rectangle.IntersectsWith(new Rectangle(3, 5, 10, 10)));
        Assert.False(rectangle.IntersectsWith(new Rectangle(4, 2, 1, 1))); // touching the exclusive right bound
        Assert.False(rectangle.IntersectsWith(new Rectangle(2, 3, 0, 0))); // empty rectangles never intersect
        Assert.Equal("1,2 3x4", rectangle.ToString());
        Assert.True(rectangle == new Rectangle(1, 2, 3, 4));
        Assert.True(rectangle != new Rectangle(1, 2, 3, 5));
        Assert.Equal(rectangle.GetHashCode(), new Rectangle(1, 2, 3, 4).GetHashCode());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rectangle(new Point(int.MaxValue, 0), new Size(1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rectangle(0, 0, 1, -1));
    }

    [Fact]
    public void PointAndSizeMembers()
    {
        var (x, y) = new Point(-3, 4);
        Assert.Equal((-3, 4), (x, y));
        Assert.Equal("(-3, 4)", new Point(-3, 4).ToString());
        Assert.Equal(Point.Empty, default);
        Assert.True(new Point(1, 2) == new Point(1, 2));
        Assert.True(new Point(1, 2) != new Point(2, 1));

        var (width, height) = new Size(5, 6);
        Assert.Equal((5, 6), (width, height));
        Assert.Equal(30, new Size(5, 6).Area);
        Assert.Equal((long)int.MaxValue * int.MaxValue, new Size(int.MaxValue, int.MaxValue).Area);
        Assert.True(Size.Empty.IsEmpty);
        Assert.True(new Size(5, 0).IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Size(1, -1));
        Assert.True(new Size(1, 2) != new Size(2, 1));
    }

    [Fact]
    public void PointDHoldsSubPixelPositions()
    {
        var point = new PointD(1.5, -2.25);
        Assert.Equal(1.5, point.X);
        Assert.Equal(-2.25, point.Y);
        var (x, y) = point;
        Assert.Equal((1.5, -2.25), (x, y));
        Assert.Equal(new PointD(1.5, -2.25), point);
        Assert.True(point == new PointD(1.5, -2.25));
        Assert.True(point != new PointD(1.5, 2.25));
        Assert.Equal(point.GetHashCode(), new PointD(1.5, -2.25).GetHashCode());
        Assert.Equal("(1.5, -2.25)", point.ToString());
        Assert.Equal(default, PointD.Empty);

        // A pixel coordinate is the top-left corner of that pixel
        PointD converted = new Point(3, 4);
        Assert.Equal(new PointD(3, 4), converted);
        Assert.Equal(new PointD(3, 4), PointD.FromPoint(new Point(3, 4)));

        // Equality is the equality of the coordinates as values: a point that is not a number equals itself
        Assert.Equal(new PointD(double.NaN, 0), new PointD(double.NaN, 0));
    }

    [Fact]
    public void RectangleDHoldsSubPixelBounds()
    {
        var rectangle = new RectangleD(0.5, 1.25, 2, 3.5);
        Assert.Equal((0.5, 1.25, 2d, 3.5), (rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
        Assert.Equal((0.5, 1.25, 2.5, 4.75), (rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom));
        Assert.False(rectangle.IsEmpty);
        Assert.True(new RectangleD(1, 1, 0, 5).IsEmpty);
        Assert.True(new RectangleD(1, 1, 5, 0).IsEmpty);
        Assert.Equal(default, RectangleD.Empty);
        Assert.Equal(rectangle, RectangleD.FromLTRB(0.5, 1.25, 2.5, 4.75));
        Assert.True(rectangle == RectangleD.FromLTRB(0.5, 1.25, 2.5, 4.75));
        Assert.True(rectangle != new RectangleD(0.5, 1.25, 2, 3));
        Assert.Equal(rectangle.GetHashCode(), new RectangleD(0.5, 1.25, 2, 3.5).GetHashCode());
        Assert.Equal("0.5,1.25 2x3.5", rectangle.ToString());

        // A rectangle of pixels is the area those pixels cover
        RectangleD converted = new Rectangle(1, 2, 3, 4);
        Assert.Equal(new RectangleD(1, 2, 3, 4), converted);
        Assert.Equal(new RectangleD(1, 2, 3, 4), RectangleD.FromRectangle(new Rectangle(1, 2, 3, 4)));

        Assert.Throws<ArgumentOutOfRangeException>(() => new RectangleD(0, 0, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectangleD(0, 0, 1, -0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectangleD(0, 0, double.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectangleD(0, 0, 1, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => RectangleD.FromLTRB(2, 0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RectangleD.FromLTRB(0, 2, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RectangleD.FromLTRB(0, 0, double.NaN, 1));
    }

    [Fact]
    public void PointDArithmeticWorksOnBothCoordinates()
    {
        var a = new PointD(1.5, -2);
        var b = new PointD(0.25, 4);
        Assert.Equal(new PointD(1.75, 2), a + b);
        Assert.Equal(new PointD(1.25, -6), a - b);
        Assert.Equal(new PointD(-1.5, 2), -a);
        Assert.Equal(new PointD(3, -4), a * 2);
        Assert.Equal(new PointD(3, -4), 2 * a);
        Assert.Equal(new PointD(0.75, -1), a / 2);
        Assert.Equal(a + b, PointD.Add(a, b));
        Assert.Equal(a - b, PointD.Subtract(a, b));
        Assert.Equal(-a, PointD.Negate(a));
        Assert.Equal(a * 2, PointD.Multiply(a, 2));
        Assert.Equal(a / 2, PointD.Divide(a, 2));
        Assert.Equal(new PointD(2.5, 1), a.Offset(1, 3));
    }

    [Fact]
    public void PointDDistanceIsTheLengthOfTheSegment()
    {
        Assert.Equal(5, new PointD(1, 1).DistanceTo(new PointD(4, 5)));
        Assert.Equal(5, new PointD(4, 5).DistanceTo(new PointD(1, 1)));
        Assert.Equal(0, new PointD(2.5, -1).DistanceTo(new PointD(2.5, -1)));
    }

    [Fact]
    public void RectangleDExposesItsLocationAndItsCenter()
    {
        var rectangle = new RectangleD(1, 2, 4, 3);
        Assert.Equal(new PointD(1, 2), rectangle.Location);
        Assert.Equal(new PointD(3, 3.5), rectangle.Center);
    }

    [Fact]
    public void RectangleDContainsItsLeftAndTopSidesOnly()
    {
        var rectangle = new RectangleD(1, 2, 4, 3);
        Assert.True(rectangle.Contains(new PointD(1, 2)));
        Assert.True(rectangle.Contains(new PointD(4.999, 4.999)));
        Assert.False(rectangle.Contains(new PointD(5, 3)));
        Assert.False(rectangle.Contains(new PointD(3, 5)));
        Assert.False(rectangle.Contains(new PointD(0.999, 3)));
        Assert.False(rectangle.Contains(new PointD(double.NaN, 3)));

        Assert.True(rectangle.Contains(rectangle));
        Assert.True(rectangle.Contains(new RectangleD(2, 3, 3, 2)));
        Assert.False(rectangle.Contains(new RectangleD(2, 3, 3.5, 2)));
    }

    [Fact]
    public void RectangleDIntersectAndUnion()
    {
        var a = new RectangleD(0, 0, 4, 4);
        var b = new RectangleD(2.5, 1, 4, 1.5);
        Assert.Equal(new RectangleD(2.5, 1, 1.5, 1.5), RectangleD.Intersect(a, b));
        Assert.Equal(new RectangleD(0, 0, 6.5, 4), RectangleD.Union(a, b));
        Assert.True(a.IntersectsWith(b));

        // Rectangles that only touch have no area in common
        var touching = new RectangleD(4, 0, 2, 2);
        Assert.Equal(RectangleD.Empty, RectangleD.Intersect(a, touching));
        Assert.False(a.IntersectsWith(touching));

        // A rectangle without area still counts for its position in a union
        Assert.Equal(new RectangleD(0, 0, 10, 4), RectangleD.Union(a, new RectangleD(6, 2, 4, 0)));
    }

    [Fact]
    public void RectangleDOffsetAndInflate()
    {
        var rectangle = new RectangleD(1, 2, 4, 3);
        Assert.Equal(new RectangleD(1.5, 0, 4, 3), rectangle.Offset(0.5, -2));
        Assert.Equal(new RectangleD(0, 1.5, 6, 4), rectangle.Inflate(1, 0.5));
        Assert.Equal(new RectangleD(0.5, 1.5, 5, 4), rectangle.Inflate(0.5));
        Assert.Equal(new RectangleD(2, 2.5, 2, 2), rectangle.Inflate(-1, -0.5));

        // A side does not move past the center
        Assert.Equal(new RectangleD(3, 3.5, 0, 0), rectangle.Inflate(-10));
        Assert.Equal(new RectangleD(3, 1, 0, 5), rectangle.Inflate(-2.5, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => rectangle.Inflate(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => rectangle.Inflate(double.NaN, 0));
    }
}
