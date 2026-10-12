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

    [Fact]
    public void TheDefaultAffineTransformIsTheIdentity()
    {
        var identity = default(AffineTransform);
        Assert.True(identity.IsIdentity);
        Assert.Equal(AffineTransform.Identity, identity);
        Assert.Equal(new AffineTransform(1, 0, 0, 1, 0, 0), identity);
        Assert.Equal((1d, 0d, 0d, 1d, 0d, 0d), (identity.M11, identity.M12, identity.M21, identity.M22, identity.OffsetX, identity.OffsetY));
        Assert.Equal(1, identity.Determinant);
        Assert.Equal(new PointD(3, -4), identity.Transform(new PointD(3, -4)));
        Assert.Equal(identity.GetHashCode(), new AffineTransform(1, 0, 0, 1, 0, 0).GetHashCode());
        Assert.False(new AffineTransform(1, 0, 0, 1, 0, 0.5).IsIdentity);
        Assert.Equal("[1 0 0 1 0 0.5]", new AffineTransform(1, 0, 0, 1, 0, 0.5).ToString());
    }

    [Fact]
    public void AffineTransformsMapPointsAsDocumented()
    {
        // (x M11 + y M21 + OffsetX, x M12 + y M22 + OffsetY)
        var transform = new AffineTransform(2, 3, 5, 7, 11, 13);
        Assert.Equal(new PointD(63, 86), transform.Transform(new PointD(1, 10)));
        Assert.Equal(new PointD(52, 73), transform.TransformVector(new PointD(1, 10)));
        Assert.Equal(-1, transform.Determinant);

        Assert.Equal(new PointD(4, 6), AffineTransform.CreateTranslation(3, 4).Transform(new PointD(1, 2)));
        Assert.Equal(new PointD(2, 6), AffineTransform.CreateScale(2, 3).Transform(new PointD(1, 2)));

        // Around a center, the center does not move
        var scale = AffineTransform.CreateScale(2, 3, new PointD(10, 20));
        Assert.Equal(new PointD(10, 20), scale.Transform(new PointD(10, 20)));
        Assert.Equal(new PointD(12, 23), scale.Transform(new PointD(11, 21)));

        // A skew moves a point horizontally in proportion to its y, vertically in proportion to its x
        var skew = AffineTransform.CreateSkew(45, 0);
        Assert.Equal(12, skew.Transform(new PointD(2, 10)).X, 1e-14);
        Assert.Equal(10, skew.Transform(new PointD(2, 10)).Y);
        Assert.Equal(new PointD(2, 10), AffineTransform.CreateSkew(0, 0).Transform(new PointD(2, 10)));
        Assert.Equal(12, AffineTransform.CreateSkew(0, 45).Transform(new PointD(2, 10)).Y, 1e-14);
    }

    [Fact]
    public void RotationsByQuarterTurnsAreExactAndClockwise()
    {
        // Y grows downward: a quarter turn brings the x axis onto the y axis, which is clockwise on the image
        Assert.Equal(new AffineTransform(0, 1, -1, 0, 0, 0), AffineTransform.CreateRotation(90));
        Assert.Equal(new AffineTransform(-1, 0, 0, -1, 0, 0), AffineTransform.CreateRotation(180));
        Assert.Equal(new AffineTransform(0, -1, 1, 0, 0, 0), AffineTransform.CreateRotation(270));
        Assert.Equal(new AffineTransform(0, -1, 1, 0, 0, 0), AffineTransform.CreateRotation(-90));
        Assert.Equal(AffineTransform.Identity, AffineTransform.CreateRotation(360));
        Assert.Equal(AffineTransform.Identity, AffineTransform.CreateRotation(-720));
        Assert.Equal(new PointD(0, 1), AffineTransform.CreateRotation(90).Transform(new PointD(1, 0)));
        Assert.Equal(new PointD(3, 1), AffineTransform.CreateRotation(90, new PointD(2, 2)).Transform(new PointD(1, 1)));

        var eighth = AffineTransform.CreateRotation(45);
        Assert.Equal(Math.Sqrt(0.5), eighth.M11, 1e-15);
        Assert.Equal(Math.Sqrt(0.5), eighth.M12, 1e-15);
        Assert.Equal(-Math.Sqrt(0.5), eighth.M21, 1e-15);
        Assert.Equal(Math.Sqrt(0.5), eighth.M22, 1e-15);
        var third = AffineTransform.CreateRotation(30);
        Assert.Equal(Math.Sqrt(0.75), third.M11, 1e-15);
        Assert.Equal(0.5, third.M12, 1e-15);
    }

    [Fact]
    public void ComposedAffineTransformsApplyTheFirstOneFirst()
    {
        var translate = AffineTransform.CreateTranslation(10, 0);
        var scale = AffineTransform.CreateScale(2, 2);
        Assert.Equal(new PointD(22, 4), translate.Then(scale).Transform(new PointD(1, 2)));
        Assert.Equal(new PointD(12, 4), scale.Then(translate).Transform(new PointD(1, 2)));
        Assert.Equal(translate.Then(scale), translate * scale);
        Assert.Equal(translate.Then(scale), AffineTransform.Multiply(translate, scale));
        Assert.Equal(translate, translate.Then(AffineTransform.Identity));
        Assert.Equal(translate, AffineTransform.Identity.Then(translate));

        // Every value below is exact in binary
        var a = new AffineTransform(2, 3, 5, 7, 11, 13);
        var b = new AffineTransform(0.5, -1, 4, 0.25, -3, 8);
        var point = new PointD(1.5, -2.25);
        Assert.Equal(new PointD(5.375, 5.6875), b.Transform(a.Transform(point)));
        Assert.Equal(new PointD(5.375, 5.6875), a.Then(b).Transform(point));
    }

    [Fact]
    public void AnAffineTransformThatFlattensThePlaneHasNoInverse()
    {
        var transform = new AffineTransform(2, 0, 0, 4, 6, -8);
        Assert.True(transform.TryInvert(out var inverse));
        Assert.Equal(new AffineTransform(0.5, 0, 0, 0.25, -3, 2), inverse);
        Assert.True(transform.Then(inverse).IsIdentity);

        Assert.True(AffineTransform.CreateRotation(90, new PointD(3, 4)).TryInvert(out var back));
        Assert.Equal(AffineTransform.CreateRotation(-90, new PointD(3, 4)), back);

        Assert.False(new AffineTransform(1, 2, 2, 4, 5, 6).TryInvert(out var none));
        Assert.Equal(AffineTransform.Identity, none);
        Assert.False(AffineTransform.CreateScale(0, 1).TryInvert(out _));
        Assert.Equal(0, AffineTransform.CreateScale(0, 1).Determinant);
    }

    [Fact]
    public void AffineTransformCoefficientsMustBeFinite()
    {
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentException>(() => new AffineTransform(invalid, 0, 0, 1, 0, 0));
            Assert.Throws<ArgumentException>(() => new AffineTransform(1, 0, 0, 1, 0, invalid));
            Assert.Throws<ArgumentException>(() => AffineTransform.CreateTranslation(invalid, 0));
            Assert.Throws<ArgumentException>(() => AffineTransform.CreateScale(1, invalid));
            Assert.Throws<ArgumentException>(() => AffineTransform.CreateRotation(invalid));
            Assert.Throws<ArgumentException>(() => AffineTransform.CreateRotation(10, new PointD(invalid, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => AffineTransform.CreateSkew(invalid, 0));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => AffineTransform.CreateSkew(90, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AffineTransform.CreateSkew(0, -90));
        Assert.Throws<ArgumentException>(() => AffineTransform.CreateScale(1e200, 1).Then(AffineTransform.CreateScale(1e200, 1)));
    }
}
