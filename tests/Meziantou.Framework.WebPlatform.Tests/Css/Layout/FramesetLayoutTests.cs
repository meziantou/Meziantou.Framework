using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The layout of the framesets (https://html.spec.whatwg.org/multipage/rendering.html#frames-and-framesets): the grid of the
/// <c>rows</c> and <c>cols</c> attributes over the viewport, the borders between the rectangles, the nested framesets and the
/// frames, whose content navigables are sized by their rectangles.
/// </summary>
public sealed class FramesetLayoutTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600, ScrollbarThickness = 0 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static PhysicalRect Rect(double x, double y, double width, double height) => new(x, y, width, height);

    private static PhysicalRect GetRect(Document document, string id)
    {
        var rect = document.GetElementById(id)!.GetBoundingClientRect();
        return new PhysicalRect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    [Fact]
    public void Grid_AbsolutePercentageAndRelativeSizesWithBorders()
    {
        // The borders (6px by default) take their space first: the columns share 788px (100px, 25% and the rest), the rows 594px
        var document = Parse("""
            <frameset id=frameset cols="100,*,25%" rows="50%,*">
              <frame id=a><frame id=b><frame id=c>
              <frame id=d><frame id=e><frame id=f>
            </frameset>
            """);
        Assert.Equal(Rect(0, 0, 800, 600), GetRect(document, "frameset"));
        Assert.Equal(Rect(0, 0, 100, 297), GetRect(document, "a"));
        Assert.Equal(Rect(106, 0, 491, 297), GetRect(document, "b"));
        Assert.Equal(Rect(603, 0, 197, 297), GetRect(document, "c"));
        Assert.Equal(Rect(0, 303, 100, 297), GetRect(document, "d"));
        Assert.Equal(Rect(106, 303, 491, 297), GetRect(document, "e"));
        Assert.Equal(Rect(603, 303, 197, 297), GetRect(document, "f"));
    }

    [Fact]
    public void Grid_NestedFramesetWithoutBorder()
    {
        // frameborder=0 is inherited by the nested frameset: no space between the rectangles
        var document = Parse("""
            <frameset cols="*,*" frameborder=0>
              <frame id=a>
              <frameset id=nested rows="1*,3*"><frame id=b><frame id=c></frameset>
            </frameset>
            """);
        Assert.Equal(Rect(0, 0, 400, 600), GetRect(document, "a"));
        Assert.Equal(Rect(400, 0, 400, 600), GetRect(document, "nested"));
        Assert.Equal(Rect(400, 0, 400, 150), GetRect(document, "b"));
        Assert.Equal(Rect(400, 150, 400, 450), GetRect(document, "c"));
    }

    [Fact]
    public void Grid_RemainderGoesToTheLastRelativeEntry()
    {
        var document = Parse("""<frameset cols="*,*,*" frameborder=0><frame id=a><frame id=b><frame id=c></frameset>""");
        Assert.Equal(Rect(0, 0, 266, 600), GetRect(document, "a"));
        Assert.Equal(Rect(266, 0, 266, 600), GetRect(document, "b"));
        Assert.Equal(Rect(532, 0, 268, 600), GetRect(document, "c"));
    }

    [Fact]
    public void Grid_LargeRelativeSizeKeepsTheOtherEntries()
    {
        var document = Parse("""<frameset id=frameset cols="4294967227*,*" frameborder=0><frame id=a><frame id=b></frameset>""");
        Assert.Equal(Rect(0, 0, 799, 600), GetRect(document, "a"));
        Assert.Equal(Rect(799, 0, 1, 600), GetRect(document, "b"));
    }

    [Fact]
    public void Grid_ChildrenBeyondTheGridAreNotRendered()
    {
        var document = Parse("""<frameset cols="*,*" frameborder=0><frame id=a><frame id=b><frame id=c></frameset>""");
        Assert.Equal(Rect(400, 0, 400, 600), GetRect(document, "b"));
        Assert.Equal(0, GetRect(document, "c").Width);
        Assert.Equal(0, GetRect(document, "c").Height);
    }

    [Fact]
    public void Grid_UpdatedWhenTheAttributesChange()
    {
        var document = Parse("""<frameset id=frameset cols="*,*" frameborder=0><frame id=a><frame id=b></frameset>""");
        Assert.Equal(Rect(0, 0, 400, 600), GetRect(document, "a"));
        document.GetElementById("frameset")!.SetAttribute("cols", "100,*");
        Assert.Equal(Rect(0, 0, 100, 600), GetRect(document, "a"));
        document.GetElementById("frameset")!.SetAttribute("frameborder", "1");
        Assert.Equal(Rect(106, 0, 694, 600), GetRect(document, "b"));
    }

    [Fact]
    public void Frame_ViewportIsItsRectangle()
    {
        var document = Parse("""<frameset rows="100,*" frameborder=0><frame id=a><frame id=b></frameset>""");
        var frameDocument = ((HtmlFrameElement)document.GetElementById("b")!).ContentDocument!;
        Assert.Equal(new PhysicalSize(800, 500), frameDocument.GetLayoutEngine().EnsureLayout()!.Size);
    }

    [Fact]
    public void Frame_OutsideOfAFramesetHasAnEmptyViewport()
    {
        var document = Parse("""<body style="margin: 0">""");
        var frame = (HtmlFrameElement)document.CreateElement("frame");
        frame.Id = "frame";
        document.Body!.AppendChild(frame);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(0, GetRect(document, "frame").Width);
        Assert.Equal(0, GetRect(document, "frame").Height);
        Assert.Equal(new PhysicalSize(0, 0), frame.ContentDocument!.GetLayoutEngine().EnsureLayout()!.Size);
    }
}
