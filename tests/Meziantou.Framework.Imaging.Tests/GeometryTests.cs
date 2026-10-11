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
}
