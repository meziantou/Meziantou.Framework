using Meziantou.Framework.WebPlatform.Css.Animations;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The corners shaped by <c>corner-shape</c> (https://drafts.csswg.org/css-borders-4/#corner-shaping): the contour paths, the
/// painting of the boxes, the hit testing, the corner shorthands and the interpolation of the superellipse parameters.
/// </summary>
public sealed class CornerShapeTests : IDisposable
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

    private static CornerContour CreateContour(double width, double height, double radius, double shape)
        => new(new PhysicalRect(0, 0, width, height), new BorderRadii((radius, radius), (radius, radius), (radius, radius), (radius, radius)), new CornerShapes(shape, shape, shape, shape));

    private static void AssertPoints(List<PhysicalOffset> actual, params (double X, double Y)[] expected)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].X, actual[i].X, 3);
            Assert.Equal(expected[i].Y, actual[i].Y, 3);
        }
    }

    [Theory]
    [InlineData("superellipse(1)", 1d)]
    [InlineData("superellipse(0)", 0d)]
    [InlineData("superellipse(-1.5)", -1.5)]
    [InlineData("superellipse(infinity)", double.PositiveInfinity)]
    [InlineData("superellipse(-infinity)", double.NegativeInfinity)]
    public void CornerShapes_ParsesTheComputedValues(string value, double expected)
    {
        Assert.True(CornerShapes.TryParse(value, out var parameter));
        Assert.Equal(expected, parameter);
        Assert.Equal(value, CornerShapes.Format(parameter));
    }

    [Fact]
    public void Contour_Bevel_IsAnOctagon()
    {
        var contour = CreateContour(100, 100, 25, shape: 0);
        var shape = contour.GetShape(new PhysicalRect(0, 0, 100, 100));
        Assert.Null(shape.Cutouts);
        AssertPoints(shape.Polygon, (25, 0), (75, 0), (100, 25), (100, 75), (75, 100), (25, 100), (0, 75), (0, 25));
    }

    [Fact]
    public void Contour_Bevel_InnerContourIsAtTheDistanceOfTheInsets()
    {
        // The bevel of the padding edge is parallel to the one of the border edge, 5px inside: 25 + 5 * (sqrt(2) - 1)
        var contour = CreateContour(100, 100, 25, shape: 0);
        var shape = contour.GetShape(new PhysicalRect(5, 5, 90, 90));
        Assert.Null(shape.Cutouts);
        AssertPoints(shape.Polygon, (27.071, 5), (72.929, 5), (95, 27.071), (95, 72.929), (72.929, 95), (27.071, 95), (5, 72.929), (5, 27.071));
    }

    [Fact]
    public void Contour_Notch_InnerContourKeepsTheSizeOfTheNotch()
    {
        // The sides of a notch move inwards with the two sides of the corner: the notch of the padding edge is 25px wide too
        var contour = CreateContour(100, 100, 25, shape: double.NegativeInfinity);
        AssertPoints(contour.GetShape(new PhysicalRect(0, 0, 100, 100)).Polygon,
            (25, 0), (75, 0), (75, 25), (100, 25), (100, 75), (75, 75), (75, 100), (25, 100), (25, 75), (0, 75), (0, 25), (25, 25));
        AssertPoints(contour.GetShape(new PhysicalRect(5, 5, 90, 90)).Polygon,
            (30, 5), (70, 5), (70, 30), (95, 30), (95, 70), (70, 70), (70, 95), (30, 95), (30, 70), (5, 70), (5, 30), (30, 30));
    }

    [Fact]
    public void Contour_Round_IsTheEllipseOfTheRadii()
    {
        var contour = new CornerContour(new PhysicalRect(0, 0, 100, 100), new BorderRadii((50, 50), default, default, default), new CornerShapes(1, 1, 1, 1));
        foreach (var point in contour.GetShape(new PhysicalRect(0, 0, 100, 100)).Polygon)
        {
            if (point.X < 50 && point.Y < 50)
            {
                Assert.Equal(50, Math.Sqrt(((50 - point.X) * (50 - point.X)) + ((50 - point.Y) * (50 - point.Y))), 6);
            }
        }
    }

    [Fact]
    public void Contour_Scoop_IsConcave()
    {
        // The curve of a scoop is the quarter of the ellipse centered on the corner of the box
        var contour = new CornerContour(new PhysicalRect(0, 0, 100, 100), new BorderRadii((40, 40), default, default, default), new CornerShapes(-1, 1, 1, 1));
        var polygon = contour.GetShape(new PhysicalRect(0, 0, 100, 100)).Polygon;
        Assert.Contains(polygon, point => Math.Abs(point.X - (40 * Math.Sqrt(0.5))) < 0.5 && Math.Abs(point.Y - (40 * Math.Sqrt(0.5))) < 0.5);
        Assert.False(contour.Contains(new PhysicalRect(0, 0, 100, 100), new PhysicalOffset(20, 20)));
        Assert.True(contour.Contains(new PhysicalRect(0, 0, 100, 100), new PhysicalOffset(35, 35)));
    }

    [Fact]
    public void Contour_OverlappingCornerAreas_AreCutouts()
    {
        // A box of 200x100 with notches of 30px and a border of 30px: the notches of the padding edge (140x40) are 60px high from
        // the border edge, so the ones of the two corners of a side overlap, and the whole side is clipped out
        var contour = CreateContour(200, 100, 30, shape: double.NegativeInfinity);
        var target = new PhysicalRect(30, 30, 140, 40);
        var shape = contour.GetShape(target);
        Assert.NotNull(shape.Cutouts);
        Assert.Equal(4, shape.Cutouts.Count);
        Assert.False(contour.Contains(target, new PhysicalOffset(45, 50)));
        Assert.True(contour.Contains(target, new PhysicalOffset(100, 50)));
        Assert.False(contour.Contains(target, new PhysicalOffset(165, 50)));
    }

    [Fact]
    public void Contour_OutsetAdjustsTheSmallRadii()
    {
        // The shadow of a box with a small radius: the corner grows less than the spread (outset-adjusted border radius)
        var adjusted = BorderRadii.GetOutsetAdjustedRadius(new PhysicalSize(100, 100), (5, 5), 50, 50);
        Assert.InRange(adjusted.X, 18, 19); // 5 + 50 * (1 - 0.9^3 * (1 - 0.1^3))
        Assert.Equal((70d, 70d), BorderRadii.GetOutsetAdjustedRadius(new PhysicalSize(100, 100), (60, 60), 10, 10));

        // A circle stays a circle (the corner covers the whole edge)
        Assert.Equal((100d, 100d), BorderRadii.GetOutsetAdjustedRadius(new PhysicalSize(100, 100), (50, 50), 50, 50));

        // An inset reduces the radius
        Assert.Equal((2d, 2d), BorderRadii.GetOutsetAdjustedRadius(new PhysicalSize(100, 100), (5, 5), -3, -3));
    }

    [Fact]
    public void Painting_Bevel_BackgroundAndBorderFollowTheContour()
    {
        var document = Parse("<div id=box style='width: 100px; height: 100px; box-sizing: border-box; border: 5px solid rgb(255, 0, 0); border-radius: 25px; corner-shape: bevel; background: rgb(0, 128, 0)'></div>");
        var dump = document.GetDisplayList()!.Dump();

        // The background stops in the middle of the opaque borders (the background bleed avoidance): the contour inset by 2.5px
        Assert.Contains("draw-path M26.036 2.5L73.964 2.5L97.5 26.036L97.5 73.964L73.964 97.5L26.036 97.5L2.5 73.964L2.5 26.036Z fill #008000", dump, StringComparison.Ordinal);
        Assert.Contains("clip-path M25 0L75 0L100 25L100 75L75 100L25 100L0 75L0 25Z", dump, StringComparison.Ordinal);
        Assert.Contains("M27.071 5L72.929 5L95 27.071L95 72.929L72.929 95L27.071 95L5 72.929L5 27.071Z evenodd fill #ff0000", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("rounded-rect", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Painting_RoundAndSquareCorners_AreRoundedRectangles()
    {
        // corner-shape: round is the initial value, and a square corner is a corner without radius
        var document = Parse("""
            <div style='width: 100px; height: 100px; border-radius: 25px; corner-shape: round; background: rgb(0, 128, 0)'></div>
            <div style='width: 100px; height: 100px; border-radius: 25px; corner-shape: square; background: rgb(0, 0, 255)'></div>
            <div style='width: 100px; height: 100px; border-radius: 25px; corner-shape: square round; background: rgb(255, 0, 0)'></div>
            """);
        var dump = document.GetDisplayList()!.Dump();
        Assert.Contains("draw-rounded-rect (0, 0, 100x100) radii=25,25,25,25 fill #008000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (0, 100, 100x100) fill #0000ff", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rounded-rect (0, 200, 100x100) radii=0,25,0,25 fill #ff0000", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Painting_BorderShape_IgnoresTheRadii()
    {
        // The background is clipped by the shape (a rectangle here), not by the radii
        var document = Parse("<div style='width: 100px; height: 100px; border-radius: 25px; border-shape: inset(0); background: rgb(0, 128, 0)'></div>");
        Assert.Contains("draw-rect (0, 0, 100x100) fill #008000", document.GetDisplayList()!.Dump(), StringComparison.Ordinal);
    }

    [Fact]
    public void Radii_ConcaveOppositeCornersAreReduced()
    {
        // The hulls of two scoops of 80% in opposite corners would intersect: the radii are reduced
        var document = Parse("<div id=box style='width: 100px; height: 100px; corner-shape: scoop; border-top-left-radius: 80%; border-bottom-right-radius: 80%; background: green'></div>");
        var style = document.GetStyleEngine().GetStyle(document.GetElementById("box")!);
        var radii = BorderRadii.Get(style, new PhysicalSize(100, 100));
        Assert.NotNull(radii);

        // The hull of a scoop is cut by the tangent of the middle of its curve (x + y = sqrt(2) r from the corner): the two
        // tangents meet on the diagonal of the box when r = 100 / sqrt(2)
        Assert.Equal(100 / Math.Sqrt(2), radii.Value.TopLeft.X, 3);
        Assert.Equal(100 / Math.Sqrt(2), radii.Value.BottomRight.Y, 3);

        // Two round corners of 80% are only reduced by the corners of the same side (none here)
        document = Parse("<div id=box style='width: 100px; height: 100px; border-top-left-radius: 80%; border-bottom-right-radius: 80%'></div>");
        radii = BorderRadii.Get(document.GetStyleEngine().GetStyle(document.GetElementById("box")!), new PhysicalSize(100, 100));
        Assert.Equal(80, radii!.Value.TopLeft.X);
    }

    [Fact]
    public void HitTesting_FollowsTheContour()
    {
        var document = Parse("<div id=box style='width: 100px; height: 100px; border-radius: 50px; corner-shape: bevel; background: green'></div>");
        var box = document.GetElementById("box");
        Assert.Same(box, document.ElementFromPoint(50, 50));
        Assert.Same(box, document.ElementFromPoint(30, 30));

        // In the ellipse of the corner, but outside of the bevel
        Assert.NotSame(box, document.ElementFromPoint(20, 20));
        Assert.NotSame(box, document.ElementFromPoint(85, 85));
    }

    [Theory]
    [InlineData("corner", "10px bevel", "10px bevel")]
    [InlineData("corner", "round 30%", "30% round")]
    [InlineData("corner", "normal", "normal")]
    [InlineData("corner", "0px round", "normal")]
    [InlineData("corner", "0px notch", "0px notch")]
    [InlineData("corner", "4px 2% round / bevel 1em / normal", "4px 2% round / 1em bevel / normal")]
    [InlineData("corner", "10px round / 10px round", "10px round")]
    [InlineData("corner-top", "round 10% / 2rem squircle", "10% round / 2rem squircle")]
    [InlineData("corner-top-left", "superellipse(-0.5) 30% 10px", "30% 10px superellipse(-0.5)")]
    [InlineData("corner", "10px", "")]
    [InlineData("corner", "scoop", "")]
    [InlineData("corner", "3% normal", "")]
    [InlineData("corner-top-left", "round / 4px", "")]
    public void CornerShorthands_AreParsedAndSerialized(string property, string value, string expected)
    {
        var document = Parse("<div id=box></div>");
        var style = ((HtmlElement)document.GetElementById("box")!).Style;
        style.SetProperty(property, value);
        Assert.Equal(expected, style.GetPropertyValue(property));
    }

    [Fact]
    public void CornerShorthands_SetTheRadiiAndTheShapes()
    {
        var document = Parse("<div id=box style='corner: 4px 2% round / 1em bevel / normal'></div>");
        var style = ((HtmlElement)document.GetElementById("box")!).Style;
        Assert.Equal("4px 2%", style.GetPropertyValue("border-top-left-radius"));
        Assert.Equal("round", style.GetPropertyValue("corner-top-left-shape"));
        Assert.Equal("1em", style.GetPropertyValue("border-top-right-radius"));
        Assert.Equal("bevel", style.GetPropertyValue("corner-top-right-shape"));
        Assert.Equal("0px", style.GetPropertyValue("border-bottom-right-radius"));
        Assert.Equal("round", style.GetPropertyValue("corner-bottom-right-shape"));
        Assert.Equal("1em", style.GetPropertyValue("border-bottom-left-radius"));
        Assert.Equal("bevel", style.GetPropertyValue("corner-bottom-left-shape"));

        var computed = document.DefaultView!.GetComputedStyle(document.GetElementById("box")!);
        Assert.Equal("4px 2% superellipse(1) / 16px superellipse(0) / normal", computed.GetPropertyValue("corner"));
    }

    [Theory]
    [InlineData("round", "bevel", 0.6, "superellipse(0.36)")]
    [InlineData("round", "bevel", -0.3, "superellipse(1.4)")]
    [InlineData("round", "bevel", 1.5, "superellipse(-0.46)")]
    [InlineData("round", "scoop", 0.5, "superellipse(0)")]
    [InlineData("notch", "square", 0.5, "superellipse(0)")]
    [InlineData("notch", "square", 0.8, "superellipse(1.64)")]
    [InlineData("bevel", "squircle", 1.5, "superellipse(infinity)")]
    [InlineData("bevel", "notch", 1.5, "superellipse(-infinity)")]
    public void Interpolation_UsesTheNormalizedHalfCorner(string from, string to, double progress, string expected)
    {
        var document = Parse("<div id=box></div>");
        var style = document.GetStyleEngine().GetStyle(document.GetElementById("box")!);
        var context = new CssInterpolationContext(style);
        var result = CssInterpolation.Prepare(context, "corner-top-left-shape", Meziantou.Framework.WebPlatform.Css.Values.CssPropertyRules.ComputeCornerShape(from), Meziantou.Framework.WebPlatform.Css.Values.CssPropertyRules.ComputeCornerShape(to)).Interpolate(progress);
        Assert.True(CornerShapes.TryParse(result!, out var actual));
        Assert.True(CornerShapes.TryParse(expected, out var expectedParameter));
        if (double.IsInfinity(expectedParameter))
        {
            Assert.Equal(expectedParameter, actual);
        }
        else
        {
            Assert.Equal(expectedParameter, actual, 2);
        }
    }

    [Fact]
    public void OffsetPath_CoordBoxFollowsTheContour()
    {
        // The path of the border box starts after its top left corner: 12.5% of the path of a diamond is the middle of its
        // first bevel
        var document = Parse("""
            <div style='position: absolute; width: 100px; height: 100px; border-radius: 50px; corner-shape: bevel'>
              <div id=target style='position: relative; width: 10px; height: 10px; offset-path: border-box; offset-rotate: 0deg; offset-distance: 12.5%'></div>
            </div>
            """);
        var rect = document.GetElementById("target")!.GetBoundingClientRect();
        Assert.Equal(70, rect.X, 1);
        Assert.Equal(20, rect.Y, 1);
    }

    [Theory]
    [InlineData("bevel", 70, 100)]
    [InlineData("notch", 50, 100)]
    [InlineData("round", 90, 100)]
    [InlineData("square", 100, 100)]
    public void ShapeOutside_BoxValueFollowsTheContour(string shape, double firstLine, double secondLine)
    {
        // The float area of a box value is the contour of the box: the first line (from 0 to 20px) is next to the top right
        // corner, whose bevel goes from (50, 0) to (100, 50); the second line (from 20px to 80px) is next to the right side
        var document = Parse($"""
            <div style='width: 300px; line-height: 0'>
              <div style='float: left; shape-outside: border-box; border-radius: 50px; corner-shape: {shape}; width: 100px; height: 100px'></div>
              <span id=a style='display: inline-block; width: 200px; height: 20px'></span>
              <span id=b style='display: inline-block; width: 150px; height: 60px'></span>
            </div>
            """);
        Assert.Equal(firstLine, document.GetElementById("a")!.GetBoundingClientRect().X, 0);
        Assert.Equal(secondLine, document.GetElementById("b")!.GetBoundingClientRect().X, 0);
    }
}
