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

    [Fact]
    public void ComposingAffineTransformsThatOverflowNamesTheSecondTransform()
    {
        var scale = AffineTransform.CreateScale(1e200, 1);
        var translation = AffineTransform.CreateTranslation(double.MaxValue, 0);
        foreach (var (first, second) in new[] { (scale, scale), (translation, translation) })
        {
            var exception = Assert.Throws<ArgumentException>("next", () => first.Then(second));
            Assert.StartsWith("A coefficient of the combined transformation is not a finite number.", exception.Message);
            Assert.Throws<ArgumentException>("second", () => AffineTransform.Multiply(first, second));
            Assert.Throws<ArgumentException>("right", () => _ = first * second);
        }

        var large = AffineTransform.CreateScale(Math.ScaleB(1, 500), 1);
        Assert.Equal(AffineTransform.CreateScale(Math.ScaleB(1, 1000), 1), large.Then(large));
        Assert.Equal(AffineTransform.CreateScale(Math.ScaleB(1, 1000), 1), large * large);
        Assert.Equal(AffineTransform.Identity, translation.Then(AffineTransform.CreateTranslation(-double.MaxValue, 0)));
    }

    [Fact]
    public void TheDefaultProjectiveTransformIsTheIdentity()
    {
        var identity = default(ProjectiveTransform);
        Assert.True(identity.IsIdentity);
        Assert.True(identity.IsAffine);
        Assert.Equal(ProjectiveTransform.Identity, identity);
        Assert.Equal(new ProjectiveTransform(1, 0, 0, 0, 1, 0, 0, 0, 1), identity);
        Assert.Equal(identity.GetHashCode(), new ProjectiveTransform(1, 0, 0, 0, 1, 0, 0, 0, 1).GetHashCode());
        Assert.Equal(1, identity.Determinant);
        Assert.Equal("[1 0 0 0 1 0 0 0 1]", identity.ToString());
        Assert.True(identity.TryTransform(new PointD(3, -4), out var point));
        Assert.Equal(new PointD(3, -4), point);
    }

    [Fact]
    public void ProjectiveTransformsMapPointsAsDocumented()
    {
        // ((x M11 + y M21 + M31) / W, (x M12 + y M22 + M32) / W), W = x M13 + y M23 + M33
        var transform = new ProjectiveTransform(2, 3, 0.25, 5, 7, 0.5, 11, 13, 1);
        Assert.False(transform.IsAffine);
        Assert.True(transform.TryTransform(new PointD(2, 1), out var point));
        Assert.Equal(new PointD(20.0 / 2, 26.0 / 2), point);

        // Where W is not positive, there is no image
        Assert.False(transform.TryTransform(new PointD(-4, 0), out var none));
        Assert.Equal(default, none);
        Assert.False(transform.TryTransform(new PointD(-8, 0), out _));

        // An affine transformation converts exactly, and maps points as it does
        var affine = new AffineTransform(2, 3, 5, 7, 11, 13);
        ProjectiveTransform converted = affine;
        Assert.Equal(new ProjectiveTransform(2, 3, 0, 5, 7, 0, 11, 13, 1), converted);
        Assert.True(converted.IsAffine);
        Assert.True(converted.TryTransform(new PointD(1, 10), out var mapped));
        Assert.Equal(affine.Transform(new PointD(1, 10)), mapped);
        Assert.Equal(affine.Determinant, converted.Determinant);
        Assert.False(new ProjectiveTransform(1, 0, 0, 0, 1, 0, 0, 0, -1).IsAffine);
    }

    [Fact]
    public void ComposedProjectiveTransformsApplyTheFirstOneFirst()
    {
        // Every value below is exact in binary
        var a = new ProjectiveTransform(2, 0, 0.125, 0, 2, 0.25, 1, -1, 1);
        var b = new ProjectiveTransform(1, 0.5, 0, -0.5, 1, 0.0625, 3, 4, 2);
        var point = new PointD(1, 2);
        Assert.True(a.TryTransform(point, out var first));
        Assert.True(b.TryTransform(first, out var expected));
        Assert.True(a.Then(b).TryTransform(point, out var actual));
        Assert.Equal(expected.X, actual.X, 1e-14);
        Assert.Equal(expected.Y, actual.Y, 1e-14);
        Assert.Equal(a.Then(b), a * b);
        Assert.Equal(a.Then(b), ProjectiveTransform.Multiply(a, b));
        Assert.Equal(a, a.Then(ProjectiveTransform.Identity));

        // An inverse brings every point back, with a positive W
        Assert.True(a.TryInvert(out var inverse));
        Assert.True(inverse.TryTransform(first, out var back));
        Assert.Equal(point.X, back.X, 1e-14);
        Assert.Equal(point.Y, back.Y, 1e-14);
        var identity = a.Then(inverse);
        Assert.Equal(1, identity.M11 / identity.M33, 1e-14);
        Assert.Equal(0, identity.M13 / identity.M33, 1e-14);
        Assert.False(new ProjectiveTransform(1, 2, 3, 2, 4, 6, 5, 6, 7).TryInvert(out var none));
        Assert.Equal(ProjectiveTransform.Identity, none);
        Assert.Equal(0, new ProjectiveTransform(1, 2, 3, 2, 4, 6, 5, 6, 7).Determinant);
    }

    [Fact]
    public void AQuadrilateralIsTheImageOfARectangle()
    {
        var transform = ProjectiveTransform.CreateFromQuadrilateral(new RectangleD(10, 20, 40, 30), new PointD(2, 2), new PointD(12, 3), new PointD(10, 9), new PointD(3, 7));
        AssertMaps(new PointD(10, 20), new PointD(2, 2));
        AssertMaps(new PointD(50, 20), new PointD(12, 3));
        AssertMaps(new PointD(50, 50), new PointD(10, 9));
        AssertMaps(new PointD(10, 50), new PointD(3, 7));

        // A straight line stays straight: the middle of a side of the rectangle is on the side of the quadrilateral
        Assert.True(transform.TryTransform(new PointD(30, 20), out var middle));
        Assert.Equal(0, ((middle.X - 2) * (3 - 2)) - ((middle.Y - 2) * (12 - 2)), 1e-12);

        // The corners must be those of a convex quadrilateral, in order
        Assert.Throws<ArgumentException>(() => ProjectiveTransform.CreateFromQuadrilateral(new RectangleD(0, 0, 1, 1), new PointD(0, 0), new PointD(10, 10), new PointD(10, 0), new PointD(0, 10)));
        Assert.Throws<ArgumentException>(() => ProjectiveTransform.CreateFromQuadrilateral(new RectangleD(0, 0, 1, 1), new PointD(0, 0), new PointD(1, 1), new PointD(2, 2), new PointD(3, 3)));
        Assert.Throws<ArgumentException>(() => ProjectiveTransform.CreateFromQuadrilateral(new RectangleD(0, 0, 0, 1), new PointD(0, 0), new PointD(1, 0), new PointD(1, 1), new PointD(0, 1)));
        Assert.Throws<ArgumentException>(() => ProjectiveTransform.CreateFromQuadrilateral(new RectangleD(0, 0, 1, 1), new PointD(double.NaN, 0), new PointD(1, 0), new PointD(1, 1), new PointD(0, 1)));

        void AssertMaps(PointD source, PointD expected)
        {
            Assert.True(transform.TryTransform(source, out var actual));
            Assert.Equal(expected.X, actual.X, 1e-12);
            Assert.Equal(expected.Y, actual.Y, 1e-12);
        }
    }

    [Fact]
    public void ProjectiveTransformCoefficientsMustBeFinite()
    {
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentException>(() => new ProjectiveTransform(invalid, 0, 0, 0, 1, 0, 0, 0, 1));
            Assert.Throws<ArgumentException>(() => new ProjectiveTransform(1, 0, 0, 0, 1, 0, 0, 0, invalid));
            Assert.Throws<ArgumentException>(() => new Transform3D(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, invalid, 0, 0, 0, 1));
            Assert.Throws<ArgumentException>(() => Transform3D.CreateRotationY(invalid));
            Assert.Throws<ArgumentException>(() => Transform3D.CreateRotation(invalid, 0, 1, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => Transform3D.CreatePerspective(invalid));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => Transform3D.CreatePerspective(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Transform3D.CreatePerspective(-1));
        Assert.Throws<ArgumentException>(() => new ProjectiveTransform(1e200, 0, 0, 0, 1, 0, 0, 0, 1).Then(new ProjectiveTransform(1e200, 0, 0, 0, 1, 0, 0, 0, 1)));
        Assert.Throws<ArgumentException>(() => Transform3D.CreateScale(1e200, 1, 1).Then(Transform3D.CreateScale(1e200, 1, 1)));
    }

    [Fact]
    public void ComposingPerspectiveTransformsThatOverflowNamesTheSecondTransform()
    {
        var plane = new ProjectiveTransform(1e200, 0, 0, 0, 1, 0, 0, 0, 1);
        var exception = Assert.Throws<ArgumentException>("next", () => plane.Then(plane));
        Assert.StartsWith("A coefficient of the combined transformation is not a finite number.", exception.Message);
        Assert.Throws<ArgumentException>("second", () => ProjectiveTransform.Multiply(plane, plane));
        Assert.Throws<ArgumentException>("right", () => _ = plane * plane);

        var space = Transform3D.CreateScale(1e200, 1, 1);
        Assert.Throws<ArgumentException>("next", () => space.Then(space));
        Assert.Throws<ArgumentException>("second", () => Transform3D.Multiply(space, space));
        Assert.Throws<ArgumentException>("right", () => _ = space * space);

        var largePlane = new ProjectiveTransform(Math.ScaleB(1, 500), 0, 0, 0, 1, 0, 0, 0, 1);
        Assert.Equal(new ProjectiveTransform(Math.ScaleB(1, 1000), 0, 0, 0, 1, 0, 0, 0, 1), largePlane * largePlane);
        var largeSpace = Transform3D.CreateScale(Math.ScaleB(1, 500), 1, 1);
        Assert.Equal(Transform3D.CreateScale(Math.ScaleB(1, 1000), 1, 1), largeSpace * largeSpace);
    }

    [Fact]
    public void TransformsOfSpaceAreThoseOfCss()
    {
        var identity = default(Transform3D);
        Assert.True(identity.IsIdentity);
        Assert.Equal(Transform3D.Identity, identity);
        Assert.Equal(new Transform3D(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1), identity);
        Assert.Equal(identity.GetHashCode(), new Transform3D(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1).GetHashCode());
        Assert.Equal(ProjectiveTransform.Identity, identity.ToProjectiveTransform());
        Assert.Equal("[1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1]", identity.ToString());

        // Quarter turns are exact: rotateX brings y toward the viewer (z), rotateY brings x away from it, rotateZ is the
        // rotation of the plane
        Assert.Equal(new Transform3D(1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1), Transform3D.CreateRotationX(90));
        Assert.Equal(new Transform3D(0, 0, -1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1), Transform3D.CreateRotationY(90));
        Assert.Equal((Transform3D)AffineTransform.CreateRotation(90), Transform3D.CreateRotationZ(90));
        AssertClose(Transform3D.CreateRotationY(30), Transform3D.CreateRotation(0, 2, 0, 30));
        AssertClose(Transform3D.CreateRotationX(-40), Transform3D.CreateRotation(-3, 0, 0, 40));
        AssertClose(Transform3D.CreateRotationZ(75), Transform3D.CreateRotation(0, 0, 1, 75));
        Assert.Equal(Transform3D.Identity, Transform3D.CreateRotation(0, 0, 0, 30));

        // A perspective divides by 1 - z / d: what comes closer is larger
        var perspective = Transform3D.CreatePerspective(100);
        Assert.Equal(new Transform3D(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -0.01, 0, 0, 0, 1), perspective);
        Assert.Equal(new Transform3D(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 3, 4, 5, 1), Transform3D.CreateTranslation(3, 4, 5));
        Assert.Equal(new Transform3D(2, 0, 0, 0, 0, 3, 0, 0, 0, 0, 4, 0, 0, 0, 0, 1), Transform3D.CreateScale(2, 3, 4));

        // A rotation around the y axis, then the perspective: on the plane z = 0, W = 1 + x sin(a) / d, so the side
        // that turns away from the viewer is smaller. The plane alone (rotation, then perspective, both flattened)
        // would lose that
        var turned = Transform3D.CreateRotationY(90).Then(perspective);
        Assert.Equal(new ProjectiveTransform(0, 0, 0.01, 0, 1, 0, 0, 0, 1), turned.ToProjectiveTransform());
        Assert.Equal(new ProjectiveTransform(0, 0, 0, 0, 1, 0, 0, 0, 1), Transform3D.CreateRotationY(90).ToProjectiveTransform().Then(perspective.ToProjectiveTransform()));
        Assert.Equal(turned, Transform3D.CreateRotationY(90) * perspective);
        Assert.Equal(turned, Transform3D.Multiply(Transform3D.CreateRotationY(90), perspective));

        // The transformations of the plane convert as the transformations of space they are
        var affine = new AffineTransform(2, 3, 5, 7, 11, 13);
        Assert.Equal((ProjectiveTransform)affine, ((Transform3D)affine).ToProjectiveTransform());
        var projective = new ProjectiveTransform(2, 3, 0.25, 5, 7, 0.5, 11, 13, 1);
        Assert.Equal(projective, ((Transform3D)projective).ToProjectiveTransform());
        Assert.Equal(projective.Then(projective), ((Transform3D)projective).Then(projective).ToProjectiveTransform());

        static void AssertClose(Transform3D expected, Transform3D actual)
        {
            Assert.Equal(expected.M11, actual.M11, 1e-15);
            Assert.Equal(expected.M12, actual.M12, 1e-15);
            Assert.Equal(expected.M13, actual.M13, 1e-15);
            Assert.Equal(expected.M21, actual.M21, 1e-15);
            Assert.Equal(expected.M22, actual.M22, 1e-15);
            Assert.Equal(expected.M23, actual.M23, 1e-15);
            Assert.Equal(expected.M31, actual.M31, 1e-15);
            Assert.Equal(expected.M32, actual.M32, 1e-15);
            Assert.Equal(expected.M33, actual.M33, 1e-15);
            Assert.Equal(expected.M44, actual.M44, 1e-15);
        }
    }

    [Fact]
    public void TransformBoundsContainsTheTransformedCorners()
    {
        var rectangle = new RectangleD(1, 2, 4, 2);
        Assert.Equal(rectangle, AffineTransform.Identity.TransformBounds(rectangle));
        Assert.Equal(new RectangleD(11, 22, 4, 2), AffineTransform.CreateTranslation(10, 20).TransformBounds(rectangle));
        Assert.Equal(new RectangleD(2, 6, 8, 6), AffineTransform.CreateScale(2, 3).TransformBounds(rectangle));

        // A flip swaps the sides: the bounds are the same rectangle on the other side of the axes
        Assert.Equal(new RectangleD(-5, -4, 4, 2), AffineTransform.CreateScale(-1, -1).TransformBounds(rectangle));

        // A quarter turn clockwise takes (x, y) to (-y, x): the rectangle is as tall as it was wide
        Assert.Equal(RectangleD.FromLTRB(-4, 1, -2, 5), AffineTransform.CreateRotation(90).TransformBounds(rectangle));

        // An eighth of a turn makes a square of side 2 a diamond, whose bounds are its diagonal wide and tall
        var square = AffineTransform.CreateRotation(45).TransformBounds(new RectangleD(-1, -1, 2, 2));
        Assert.Equal(-Math.Sqrt(2), square.Left, 1e-12);
        Assert.Equal(-Math.Sqrt(2), square.Top, 1e-12);
        Assert.Equal(2 * Math.Sqrt(2), square.Width, 1e-12);
        Assert.Equal(2 * Math.Sqrt(2), square.Height, 1e-12);

        // A skew that takes (x, y) to (x + y, y) moves the bottom side further right than the top one
        Assert.Equal(RectangleD.FromLTRB(3, 2, 9, 4), new AffineTransform(1, 0, 1, 1, 0, 0).TransformBounds(rectangle));

        // A rectangle without area is a segment or a point
        Assert.Equal(new RectangleD(13, 24, 0, 0), AffineTransform.CreateTranslation(10, 20).TransformBounds(new RectangleD(3, 4, 0, 0)));
        Assert.Equal(RectangleD.FromLTRB(-4, 1, -4, 5), AffineTransform.CreateRotation(90).TransformBounds(new RectangleD(1, 4, 4, 0)));
    }

    #region CorrectPerspective_Document
    [Fact]
    public void CorrectPerspective_Document()
    {
        // A 32x24 page with a checkerboard of 4-pixel squares
        using var page = new Image<Rgb24>(32, 24);
        for (var y = 0; y < 24; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                page.Frames[0][x, y] = (x / 4) % 2 == (y / 4) % 2 ? new Rgb24(250, 250, 240) : new Rgb24(20, 30, 90);
            }
        }

        // Photographed at an angle on a gray desk: the corners of the page land on a quadrilateral of a 64x48 photo
        var topLeft = new PointD(8, 6);
        var topRight = new PointD(56, 10);
        var bottomRight = new PointD(52, 42);
        var bottomLeft = new PointD(12, 40);
        using var photo = page.Clone();
        photo.Transform(
            ProjectiveTransform.CreateFromQuadrilateral(new RectangleD(0, 0, 32, 24), topLeft, topRight, bottomRight, bottomLeft),
            new TransformOptions { CanvasMode = TransformCanvasMode.Explicit, CanvasSize = new Size(64, 48), BackgroundColor = new Rgba64(30000, 30000, 30000, 65535) });

        // Seen from the front again: the quadrilateral becomes the whole image
        photo.CorrectPerspective(topLeft, topRight, bottomRight, bottomLeft, new Size(32, 24), new TransformOptions { BackgroundColor = new Rgba64(30000, 30000, 30000, 65535) });

        Assert.Equal(new Size(32, 24), photo.Size);

        // The middle of every square has its color back, within the blur of the two resamplings
        var largest = 0;
        for (var y = 0; y < 24; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                if (x % 4 is 1 or 2 && y % 4 is 1 or 2)
                {
                    largest = Math.Max(largest, Math.Abs(page.Frames[0][x, y].B - photo.Frames[0][x, y].B));
                }
            }
        }

        Assert.InRange(largest, 0, 16);
    }
    #endregion

    [Theory]
    [InlineData(ResamplingFilter.NearestNeighbor)]
    [InlineData(ResamplingFilter.Bicubic)]
    public void CorrectPerspective_OfAnUprightRectangle_IsACrop(ResamplingFilter filter)
    {
        using var image = new Image<Rgba32>(12, 10);
        for (var y = 0; y < 10; y++)
        {
            for (var x = 0; x < 12; x++)
            {
                image.Frames[0][x, y] = new Rgba32((byte)(x * 20), (byte)(y * 25), (byte)((x * y) % 256), (byte)(255 - x));
            }
        }

        using var expected = image.Clone();
        expected.Crop(new Rectangle(2, 3, 8, 5));

        // The default size is the size of the quadrilateral
        image.CorrectPerspective(new PointD(2, 3), new PointD(10, 3), new PointD(10, 8), new PointD(2, 8), options: new TransformOptions { Filter = filter }, cancellationToken: TestContext.Current.CancellationToken);
        TestImages.AssertSamePixels(expected, image);
    }

    [Fact]
    public void CorrectPerspective_DefaultSize_AndInvalidArguments()
    {
        // The longer of the top and bottom edges (10 and 12) by the longer of the left and right edges (5 and 6)
        using var image = new Image<Rgba32>(20, 20);
        image.CorrectPerspective(new PointD(2, 2), new PointD(12, 2), new PointD(14, 8), new PointD(2, 7));
        Assert.Equal(new Size(12, 6), image.Size);

        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).CorrectPerspective(default, default, default, default));
        Assert.Throws<ArgumentException>("size", () => image.CorrectPerspective(new PointD(0, 0), new PointD(5, 0), new PointD(5, 5), new PointD(0, 5), new Size(0, 3)));
        Assert.Throws<ArgumentException>("topLeft", () => image.CorrectPerspective(new PointD(0, 0), new PointD(5, 5), new PointD(5, 0), new PointD(0, 5)));
        Assert.Throws<ArgumentException>("topLeft", () => image.CorrectPerspective(new PointD(double.NaN, 0), new PointD(5, 0), new PointD(5, 5), new PointD(0, 5)));
        Assert.Equal(new Size(12, 6), image.Size);

        // A background color without alpha needs to be opaque
        using var opaque = new Image<Rgb24>(10, 10);
        Assert.Throws<UnsupportedImageFeatureException>(() => opaque.CorrectPerspective(new PointD(-2, 0), new PointD(5, 0), new PointD(5, 5), new PointD(0, 5)));
    }
}
