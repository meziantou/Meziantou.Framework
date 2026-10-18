using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The offset transform of the motion path (offset-path, offset-distance, offset-rotate, offset-anchor, offset-position).</summary>
public sealed class MotionPathTests : IDisposable
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

    private static (double X, double Y, double Width, double Height) GetClientRect(Document document, string id)
    {
        var rect = document.GetElementById(id)!.GetBoundingClientRect();
        return (Round(rect.X), Round(rect.Y), Round(rect.Width), Round(rect.Height));

        static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
    }

    [Fact]
    public void Path_IsRelativeToTheBox()
    {
        // The anchor point (the transform origin) is moved to the point at 120px on the path, which is relative to the position
        // of the box; the box follows the direction of the path (downwards: a quarter turn)
        var document = Parse("<div id=a style='position: absolute; left: 300px; top: 0; width: 30px; height: 20px; transform-origin: 0 0; offset-path: path(\"m 0 0 v 200\"); offset-distance: 120px'></div>");
        Assert.Equal((280, 120, 20, 30), GetClientRect(document, "a"));
    }

    [Fact]
    public void Circle_StartsAtItsRightmostPoint()
    {
        // The circle is in the border box of the containing block: the center of the box is at its rightmost point
        var document = Parse("<div style='position: relative; left: 100px; width: 400px; height: 300px'><div id=a style='width: 60px; height: 60px; offset-path: circle(100px at 150px 150px)'></div></div>");
        Assert.Equal((320, 120, 60, 60), GetClientRect(document, "a"));
    }

    [Fact]
    public void ClosedPath_WrapsTheDistance()
    {
        // 125% of the edge of a 200x100 box is its top right corner; offset-rotate: 0deg keeps the box upright
        var document = Parse("<div style='width: 200px; height: 100px'><div id=a style='width: 20px; height: 20px; offset-path: border-box; offset-distance: calc(100% + 200px); offset-rotate: 0deg'></div></div>");
        Assert.Equal((190, -10, 20, 20), GetClientRect(document, "a"));
    }

    [Fact]
    public void Ray_WithContainAndAnchor()
    {
        // The ray starts at the center of the 300x300 containing block (offset-position: normal), downwards; its length is
        // the distance to the closest side (150px) minus the half of the largest side of the box (contain)
        var document = Parse("<div style='width: 300px; height: 300px'><div id=a style='width: 40px; height: 20px; offset-path: ray(180deg closest-side contain); offset-distance: 100%; offset-rotate: 0deg; offset-anchor: 0% 0%'></div></div>");
        Assert.Equal((150, 280, 40, 20), GetClientRect(document, "a"));
    }

    [Fact]
    public void OffsetPath_EstablishesAStackingContextAndAContainingBlock()
    {
        var document = Parse("<div id=a style='margin-left: 50px; width: 100px; height: 100px; offset-path: path(\"m 50 50\")'><div id=b style='position: fixed; left: 10px; top: 0; width: 10px; height: 10px'></div></div>");
        var box = document.GetLayoutEngine().GetFragments(document.GetElementById("a")!)[0];
        Assert.True(box.EstablishesStackingContext);
        Assert.Equal(60, document.GetLayoutEngine().GetBorderBoxRect(document.GetElementById("b")!)!.Value.X);
    }
}
