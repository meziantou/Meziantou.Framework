using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The borders shaped by <c>border-shape</c> (https://drafts.csswg.org/css-borders-4/#border-shape): the operations on the
/// areas of the paths, the geometry of the shaped borders, their painting and their hit testing.
/// </summary>
public sealed class BorderShapeTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0 }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private BorderShapeGeometry GetGeometry(string style)
    {
        var document = Parse("<div id=box style='" + style + "'></div>");
        var fragment = Assert.IsType<BoxFragment>(document.GetLayoutEngine().GetFragments(document.GetElementById("box")!)[0]);
        var geometry = BorderShapeGeometry.Get(fragment);
        Assert.NotNull(geometry);
        return geometry;
    }

    private static DisplayPath CreatePolygon(params (double X, double Y)[] points)
    {
        var path = new DisplayPath();
        path.MoveTo(points[0].X, points[0].Y);
        for (var i = 1; i < points.Length; i++)
        {
            path.LineTo(points[i].X, points[i].Y);
        }

        path.Close();
        return path;
    }

    private static DisplayPath CreateRect(double x, double y, double width, double height)
        => CreatePolygon((x, y), (x + width, y), (x + width, y + height), (x, y + height));

    private static bool Contains(DisplayPath path, double x, double y) => PathOperations.Contains(path, new PhysicalOffset(x, y));

    [Fact]
    public void Combine_ComputesTheBooleanOperations()
    {
        var first = CreateRect(0, 0, 100, 100);
        var second = CreateRect(50, 50, 100, 100);

        var union = PathOperations.Combine(first, second, PathOperation.Union);
        Assert.True(Contains(union, 25, 25));
        Assert.True(Contains(union, 75, 75));
        Assert.True(Contains(union, 125, 125));
        Assert.False(Contains(union, 125, 25));
        Assert.Equal(new PhysicalRect(0, 0, 150, 150), union.GetBounds());

        var difference = PathOperations.Combine(first, second, PathOperation.Difference);
        Assert.True(Contains(difference, 25, 25));
        Assert.True(Contains(difference, 75, 25));
        Assert.False(Contains(difference, 75, 75));
        Assert.False(Contains(difference, 125, 125));

        var intersection = PathOperations.Combine(first, second, PathOperation.Intersection);
        Assert.True(Contains(intersection, 75, 75));
        Assert.False(Contains(intersection, 25, 25));
        Assert.Equal(new PhysicalRect(50, 50, 50, 50), intersection.GetBounds());
    }

    [Fact]
    public void Combine_AppliesTheFillRules()
    {
        // Two squares in the same direction, one inside of the other: the inner one is a hole with the even-odd rule only
        var squares = new DisplayPath();
        squares.AddRect(new PhysicalRect(0, 0, 100, 100));
        squares.AddRect(new PhysicalRect(25, 25, 50, 50));
        var all = CreateRect(-10, -10, 120, 120);
        Assert.True(Contains(PathOperations.Combine(squares, all, PathOperation.Intersection), 50, 50));

        squares.FillRule = FillRule.EvenOdd;
        var ring = PathOperations.Combine(squares, all, PathOperation.Intersection);
        Assert.False(Contains(ring, 50, 50));
        Assert.True(Contains(ring, 10, 50));
    }

    [Fact]
    public void Combine_MergesTheSpansOfTheSameEdges()
    {
        // The intersection of two rectangles is one trapezoid: the slabs cut at the vertices of the other rectangle are merged
        var intersection = PathOperations.Combine(CreateRect(0, 0, 100, 100), CreateRect(20, -50, 30, 200), PathOperation.Intersection);
        Assert.Equal("M20 0L50 0L50 100L20 100Z", intersection.ToString());
    }

    [Fact]
    public void StrokeOutline_HasMiterJoinsWithinTheLimit()
    {
        // A right angle: the miter is the corner of the square (its ratio is sqrt(2))
        var corner = new DisplayPath();
        corner.MoveTo(0, 100);
        corner.LineTo(0, 0);
        corner.LineTo(100, 0);
        var miter = PathOperations.CreateStrokeOutline(corner, new StrokeStyle(20, Join: LineJoin.Miter, MiterLimit: 4));
        Assert.True(Contains(miter, -9, -9));
        Assert.True(Contains(miter, 50, 9));
        Assert.False(Contains(miter, 50, 11));

        // The open ends have butt caps
        Assert.False(Contains(miter, 101, 0));
        Assert.False(Contains(miter, 0, 101));

        // Beyond the limit, the join is a bevel
        var bevel = PathOperations.CreateStrokeOutline(corner, new StrokeStyle(20, Join: LineJoin.Miter, MiterLimit: 1.2));
        Assert.False(Contains(bevel, -9, -9));
        Assert.True(Contains(bevel, -4, -4));

        var round = PathOperations.CreateStrokeOutline(corner, new StrokeStyle(20, Join: LineJoin.Round));
        Assert.False(Contains(round, -9, -9));
        Assert.True(Contains(round, -6.5, -6.5));
    }

    [Fact]
    public void Offset_MovesTheEdgesOfAPath()
    {
        var diamond = CreatePolygon((50, 0), (100, 50), (50, 100), (0, 50));
        var stroke = new StrokeStyle(0, Join: LineJoin.Miter, MiterLimit: 1e10);

        // Outwards: the edges are 10px further, the corners stay sharp (the miters are at 10 * sqrt(2) from the vertices)
        var outer = PathOperations.Offset(diamond, 10, stroke);
        Assert.True(Contains(outer, 50, -13));
        Assert.False(Contains(outer, 50, -15));
        Assert.True(Contains(outer, 20, 16));
        Assert.False(Contains(outer, 20, 14));

        // Inwards: the area without the stroke of twice the distance
        var inner = PathOperations.Offset(diamond, -10, stroke);
        Assert.True(Contains(inner, 50, 16));
        Assert.False(Contains(inner, 50, 12));
        Assert.True(Contains(inner, 50, 50));

        // A rounded rectangle stays one
        var circle = DisplayPath.FromRoundedRect(new RoundedRect(new PhysicalRect(0, 0, 100, 100), new BorderRadii((50, 50), (50, 50), (50, 50), (50, 50))));
        var grown = PathOperations.Offset(circle, 5, stroke).RoundedRect;
        Assert.NotNull(grown);
        Assert.Equal(new PhysicalRect(-5, -5, 110, 110), grown.Value.Rect);
        Assert.Equal((55d, 55d), grown.Value.Radii.TopLeft);
        Assert.True(PathOperations.Offset(circle, -50, stroke).IsEmpty);
    }

    [Fact]
    public void Geometry_OneShapeIsStrokedInTheHalfBorderBox()
    {
        // The half-border-box is 110x110 at (5, 5): the circle of its closest side has a radius of 55
        var geometry = GetGeometry("width: 100px; height: 100px; border: 10px solid; border-shape: circle()");
        Assert.Null(geometry.InnerShape);
        Assert.Equal(10, geometry.StrokeWidth);
        Assert.Equal(new PhysicalRect(5, 5, 110, 110), geometry.Shape.GetBounds());

        // The outer and the inner edges of the stroke are the border edge and the padding edge of the box
        Assert.Equal(new PhysicalRect(0, 0, 120, 120), geometry.OuterPath.RoundedRect!.Value.Rect);
        Assert.Equal(new PhysicalRect(10, 10, 100, 100), geometry.InnerPath.RoundedRect!.Value.Rect);
        Assert.Equal((50d, 50d), geometry.InnerPath.RoundedRect!.Value.Radii.TopLeft);
        Assert.Same(geometry.Shape, geometry.BackgroundPath);
    }

    [Fact]
    public void Geometry_TwoShapesAreInTheBorderBoxAndInThePaddingBox()
    {
        var geometry = GetGeometry("width: 100px; height: 100px; border: 10px solid; border-shape: inset(0) inset(0)");
        Assert.NotNull(geometry.InnerShape);
        Assert.Equal(0, geometry.StrokeWidth);
        Assert.Equal(new PhysicalRect(0, 0, 120, 120), geometry.OuterPath.GetBounds());
        Assert.Equal(new PhysicalRect(10, 10, 100, 100), geometry.InnerPath.GetBounds());
        Assert.Same(geometry.InnerShape, geometry.BackgroundPath);

        // The reference boxes of the shapes
        geometry = GetGeometry("width: 100px; height: 100px; padding: 5px; margin: 20px; border: 10px solid; border-shape: inset(0) margin-box inset(0) content-box");
        Assert.Equal(new PhysicalRect(-20, -20, 170, 170), geometry.OuterPath.GetBounds());
        Assert.Equal(new PhysicalRect(15, 15, 100, 100), geometry.InnerPath.GetBounds());
    }

    [Theory]
    [InlineData("border-style: solid; border-width: 2px 4px 6px 8px", 2)]
    [InlineData("border-style: none solid solid solid; border-width: 2px 4px 6px 8px", 8)]
    [InlineData("border-style: none solid solid none; border-width: 2px 4px 6px 8px", 6)]
    [InlineData("border-style: none solid none none; border-width: 2px 4px 6px 8px", 4)]
    [InlineData("writing-mode: vertical-rl; border-style: solid; border-width: 2px 4px 6px 8px", 4)]
    [InlineData("writing-mode: vertical-lr; border-style: solid; border-width: 2px 4px 6px 8px", 8)]
    [InlineData("direction: rtl; border-style: none solid solid solid; border-width: 2px 4px 6px 8px", 4)]
    [InlineData("border-style: none; border-width: 2px 4px 6px 8px", 0)]
    public void Geometry_StrokeHasTheWidthOfTheRelevantSide(string style, double expected)
    {
        // The first side that has a border style, in the order block-start, inline-start, block-end, inline-end
        var geometry = GetGeometry("width: 100px; height: 100px; border-shape: circle(40px); " + style);
        Assert.Equal(expected, geometry.StrokeWidth);
    }

    [Fact]
    public void Geometry_JoinsHaveNoMiterLimitForStraightSegments()
    {
        Assert.Equal(1e10, GetGeometry("width: 100px; height: 100px; border: 10px solid; border-shape: polygon(0 0, 100% 100%, 100% 0)").Stroke.MiterLimit);
        Assert.Equal(1e10, GetGeometry("width: 100px; height: 100px; border: 10px solid; border-shape: path(\"M0 0L100 100L100 0Z\")").Stroke.MiterLimit);
        Assert.Equal(4, GetGeometry("width: 100px; height: 100px; border: 10px solid; border-shape: shape(from 0 0, curve to 100px 100px with 50px 0, close)").Stroke.MiterLimit);
        Assert.Equal(LineJoin.Miter, GetGeometry("width: 100px; height: 100px; border: 10px solid; border-shape: circle()").Stroke.Join);
    }

    [Fact]
    public void Geometry_PolygonEdgesAreTheShapeAndItsStroke()
    {
        // A diamond in the half-border-box (100x100 at (10, 10)), stroked with 20px
        var geometry = GetGeometry("width: 80px; height: 80px; border: 20px solid; border-shape: polygon(50% 0, 100% 50%, 50% 100%, 0 50%)");
        Assert.True(Contains(geometry.Shape, 60, 12));
        Assert.False(Contains(geometry.Shape, 60, 8));

        // The outer edge is 10px outside of the diamond (its miters are 10 * sqrt(2) outside), the inner edge 10px inside
        Assert.True(Contains(geometry.OuterPath, 60, -3));
        Assert.False(Contains(geometry.OuterPath, 60, -5));
        Assert.False(Contains(geometry.InnerPath, 60, 23));
        Assert.True(Contains(geometry.InnerPath, 60, 26));
        Assert.True(Contains(geometry.InnerPath, 60, 60));
    }

    [Fact]
    public void Geometry_ShapeWithoutAreaHasNoEdge()
    {
        var geometry = GetGeometry("width: 100px; height: 100px; border: 10px solid; border-shape: circle(0%)");
        Assert.True(geometry.OuterPath.IsEmpty);
        Assert.True(geometry.InnerPath.IsEmpty);
    }

    [Fact]
    public void Painting_StrokedCircleIsARing()
    {
        // The background is clipped to the stroked circle (the middle of the border), the stroke is the ring of the border
        var document = Parse("<div style='width: 100px; height: 100px; border: 10px solid rgb(0, 0, 255); border-radius: 25px; border-shape: circle(); background: rgb(0, 128, 0)'></div>");
        var dump = document.GetDisplayList()!.Dump();
        Assert.Contains("draw-rounded-rect (5, 5, 110x110) radii=55,55,55,55 fill #008000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-ring (0, 0, 120x120) radii=60,60,60,60 - (10, 10, 100x100) radii=50,50,50,50 fill #0000ff", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Painting_StrokedPolygonIsAStrokedPath()
    {
        // The stroke has the width and the color of the relevant side (the top one), whatever the border style
        var document = Parse("<div style='width: 100px; height: 100px; border: 10px dotted rgb(0, 0, 255); border-left: 4px solid red; border-shape: polygon(0 0, 100% 0, 50% 100%) border-box; background: rgb(0, 128, 0)'></div>");
        var dump = document.GetDisplayList()!.Dump();
        Assert.Contains("clip-path M0 0L114 0L57 120Z", dump, StringComparison.Ordinal);
        Assert.Contains("draw-path M0 0L114 0L57 120Z stroke #0000ff width=10", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Painting_TwoShapesFillTheAreaBetweenThem()
    {
        // The area is filled without border style, in the color of the block-start side; the background is clipped to the inner shape
        var document = Parse("<div style='width: 100px; height: 100px; border-color: rgb(0, 0, 255); border-shape: inset(0) circle(25%); background: rgb(0, 128, 0)'></div>");
        var dump = document.GetDisplayList()!.Dump();
        Assert.Contains("draw-rounded-rect (25, 25, 50x50) radii=25,25,25,25 fill #008000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-ring (0, 0, 100x100) - (25, 25, 50x50) radii=25,25,25,25 fill #0000ff", dump, StringComparison.Ordinal);

        // A transparent border paints nothing, and the background stays in the inner shape
        document = Parse("<div style='width: 100px; height: 100px; border: 10px solid transparent; border-shape: inset(0) circle(0%); background: rgb(0, 128, 0)'></div>");
        Assert.DoesNotContain("#008000", document.GetDisplayList()!.Dump(), StringComparison.Ordinal);
    }

    [Fact]
    public void Painting_BorderImageIsNotPainted()
    {
        var document = Parse("<div style='width: 100px; height: 100px; border: 10px solid rgb(0, 0, 255); border-image: linear-gradient(red, red) 1; border-shape: inset(0)'></div>");
        var dump = document.GetDisplayList()!.Dump();
        Assert.DoesNotContain("linear-gradient", dump, StringComparison.Ordinal);
        Assert.Contains("#0000ff", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Painting_ShadowsFollowTheEdges()
    {
        // The outer shadow is the outer edge expanded by the spread, the inset shadow is clipped to the inner edge
        var document = Parse("<div style='margin: 50px; width: 100px; height: 100px; border: 10px solid blue; border-shape: circle(); box-shadow: 0 0 0 5px rgb(255, 0, 0), inset 0 0 0 5px rgb(0, 255, 0)'></div>");
        var dump = document.GetDisplayList()!.Dump();
        Assert.Contains("draw-rounded-rect (45, 45, 130x130) radii=65,65,65,65 fill #ff0000", dump, StringComparison.Ordinal);
        Assert.Contains("clip-rounded-rect (60, 60, 100x100) radii=50,50,50,50", dump, StringComparison.Ordinal);
        Assert.Contains("fill #00ff00", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Painting_OutlineFollowsTheOuterEdge()
    {
        var document = Parse("<div style='margin: 50px; width: 100px; height: 100px; border: 10px solid blue; border-shape: circle(); outline: 4px solid rgb(255, 0, 0); outline-offset: 2px'></div>");
        Assert.Contains("draw-ring (44, 44, 132x132) radii=66,66,66,66 - (48, 48, 124x124) radii=62,62,62,62 fill #ff0000", document.GetDisplayList()!.Dump(), StringComparison.Ordinal);

        // The outline of a polygon is the area between its outer edge moved by the offset and by the offset and the width
        document = Parse("<div style='margin: 50px; width: 100px; height: 100px; border-shape: polygon(50% 0, 100% 50%, 50% 100%, 0 50%); outline: 4px solid rgb(255, 0, 0)'></div>");
        Assert.Contains("fill #ff0000", document.GetDisplayList()!.Dump(), StringComparison.Ordinal);
    }

    [Fact]
    public void Painting_OverflowIsClippedToTheInnerEdge()
    {
        var document = Parse("""
            <div style='width: 100px; height: 100px; border: 10px solid blue; border-shape: circle(); overflow: hidden'>
              <div style='width: 200px; height: 200px; background: rgb(0, 128, 0)'></div>
            </div>
            """);
        var dump = document.GetDisplayList()!.Dump();
        Assert.Contains("clip-rect (10, 10, 100x100)", dump, StringComparison.Ordinal);
        Assert.Contains("clip-rounded-rect (10, 10, 100x100) radii=50,50,50,50", dump, StringComparison.Ordinal);

        // overflow-clip-margin moves the edge of its box (the outer edge for the border box) outwards
        document = Parse("""
            <div style='width: 100px; height: 100px; border: 10px solid blue; border-shape: circle(); overflow: clip; overflow-clip-margin: border-box 20px'>
              <div style='width: 200px; height: 200px; background: rgb(0, 128, 0)'></div>
            </div>
            """);
        Assert.Contains("clip-rounded-rect (-20, -20, 160x160) radii=80,80,80,80", document.GetDisplayList()!.Dump(), StringComparison.Ordinal);
    }

    [Fact]
    public void Painting_BorderAreaIsTheShapedBorder()
    {
        // The color is painted in the area of the border, also outside of the border box
        var document = Parse("<div style='margin: 50px; width: 100px; height: 100px; border: 10px solid transparent; border-shape: polygon(-10% -10%, 110% -10%, 50% 110%); background-color: rgb(0, 128, 0); background-clip: border-area'></div>");
        Assert.Contains("stroke #008000 width=10", document.GetDisplayList()!.Dump(), StringComparison.Ordinal);

        // An image is masked by the border
        document = Parse("<div style='margin: 50px; width: 100px; height: 100px; border: 10px solid transparent; border-shape: circle(); background-image: linear-gradient(green, green); background-clip: border-area'></div>");
        var dump = document.GetDisplayList()!.Dump();
        Assert.Contains("save-layer blend=destinationin", dump, StringComparison.Ordinal);
        Assert.Contains("draw-ring (50, 50, 120x120) radii=60,60,60,60 - (60, 60, 100x100) radii=50,50,50,50 fill #000000", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void HitTesting_FollowsTheOuterEdge()
    {
        var document = Parse("<div id=box style='margin: 50px; width: 100px; height: 100px; border: 10px solid blue; border-shape: circle(); background: green'></div>");
        var box = document.GetElementById("box");
        Assert.Same(box, document.ElementFromPoint(110, 110));
        Assert.Same(box, document.ElementFromPoint(110, 52));

        // In the border box, but outside of the circle
        Assert.NotSame(box, document.ElementFromPoint(55, 55));

        // A shape outside of the border box is hit too
        document = Parse("<div id=box style='margin: 50px; width: 100px; height: 100px; border: 10px solid blue; border-shape: circle(100px)'></div>");
        Assert.Same(document.GetElementById("box"), document.ElementFromPoint(20, 110));
    }

    [Fact]
    public void HitTesting_ContentsAreClippedByTheInnerEdge()
    {
        var document = Parse("""
            <div id=box style='width: 100px; height: 100px; border: 10px solid blue; border-shape: circle(); overflow: hidden'>
              <div id=child style='width: 200px; height: 200px; background: green'></div>
            </div>
            """);
        Assert.Same(document.GetElementById("child"), document.ElementFromPoint(60, 60));

        // In the child, in the stroke of the box
        Assert.Same(document.GetElementById("box"), document.ElementFromPoint(60, 5));

        // In the child, outside of the circle
        var hit = document.ElementFromPoint(12, 12);
        Assert.NotSame(document.GetElementById("child"), hit);
        Assert.NotSame(document.GetElementById("box"), hit);
    }

    [Fact]
    public void DecorationBounds_IncludeTheShapedBorderAndTheShadows()
    {
        // The area that occludes the boxes below (IntersectionObserver): the shape, not the border box, and the outer shadows
        var document = Parse("<div id=box style='width: 10px; height: 10px; border-shape: circle(5px at 50% 50%); box-shadow: -150px 0 0 100px black; background: red'></div>");
        var fragment = Assert.IsType<BoxFragment>(document.GetLayoutEngine().GetFragments(document.GetElementById("box")!)[0]);
        Assert.Equal(new PhysicalRect(-250, -100, 260, 210), Painter.GetDecorationBounds(fragment));

        document = Parse("<div id=box style='width: 10px; height: 10px'></div>");
        fragment = Assert.IsType<BoxFragment>(document.GetLayoutEngine().GetFragments(document.GetElementById("box")!)[0]);
        Assert.Null(Painter.GetDecorationBounds(fragment));
    }

    [Fact]
    public void Ellipse_CornerKeywordsAreTheDistanceToTheCorner()
    {
        // 300x400, the center at (60, 80): the closest corner is at 100, the farthest one at 400
        var geometry = GetGeometry("width: 300px; height: 400px; border-shape: ellipse(closest-corner farthest-corner at 60px 80px)");
        Assert.Equal(new PhysicalRect(-40, -320, 200, 800), geometry.Shape.GetBounds());

        geometry = GetGeometry("width: 300px; height: 400px; border-shape: circle(farthest-corner at 60px 80px)");
        Assert.Equal(new PhysicalRect(-340, -320, 800, 800), geometry.Shape.GetBounds());
    }
}
