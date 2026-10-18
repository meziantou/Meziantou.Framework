using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>The geometry and the hit testing of CSSOM View (https://drafts.csswg.org/cssom-view/), from the layout, measured with Ahem.</summary>
public sealed class CssomViewTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html, ViewportOptions? viewport = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions { Viewport = viewport, TimeProvider = _clock });
        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Loads a no-quirks document whose body has no margin and the 10px/1 Ahem font (1em glyphs), in a 800x600 viewport.</summary>
    private Document Parse(string html) => Load("<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html, new ViewportOptions { Width = 800, Height = 600 });

    private static (double X, double Y, double Width, double Height) ToTuple(DomRect rect) => (rect.X, rect.Y, rect.Width, rect.Height);

    [Fact]
    public void GetClientRects_BorderBoxOfEachFragment()
    {
        var document = Parse("<div id=b style='width: 100px; height: 50px; margin: 10px; border: 2px solid'></div><div style='width: 50px'><span id=s>XX XX XX</span></div>");
        var block = document.GetElementById("b")!;
        Assert.Equal((10d, 10d, 104d, 54d), ToTuple(Assert.Single(block.GetClientRects())));
        Assert.Equal((10d, 10d, 104d, 54d), ToTuple(block.GetBoundingClientRect()));

        // An inline box split over two lines has a box per line
        var span = document.GetElementById("s")!;
        var rects = span.GetClientRects();
        Assert.Equal(2, rects.Count);
        Assert.Equal((0d, 74d, 50d, 10d), ToTuple(rects[0]));
        Assert.Equal((0d, 84d, 20d, 10d), ToTuple(rects[1]));
        Assert.Equal((0d, 74d, 50d, 20d), ToTuple(span.GetBoundingClientRect()));
    }

    [Fact]
    public void GetClientRects_TransformsAndScrolling()
    {
        var document = Parse("<div id=t style='width: 100px; height: 50px; transform: rotate(90deg)'></div><div style='height: 2000px'></div>");
        var target = document.GetElementById("t")!;

        // The bounding box of the rotated border box (rotated around its center)
        Assert.Equal((25d, -25d, 50d, 100d), ToTuple(target.GetBoundingClientRect()), new RectComparer());

        // The rectangles are relative to the viewport
        _ = document.DefaultView!.ScrollTo(0, 100);
        Assert.Equal(-125, target.GetBoundingClientRect().Y, 6);
        Assert.Equal(0, ((HtmlElement)target).OffsetTop);
    }

    [Theory]
    [InlineData("<div id=target style='display:none'></div>")]
    [InlineData("<div style='display:none'><span id=target></span></div>")]
    [InlineData("<div id=target style='display:contents'></div>")]
    [InlineData("<head><meta id=target></head>")]
    public void GetClientRects_ElementNotBeingRendered_IsEmpty(string html)
    {
        var document = Load(html);
        var target = document.GetElementById("target")!;
        Assert.Empty(target.GetClientRects());
        var rect = target.GetBoundingClientRect();
        Assert.Equal((0d, 0d, 0d, 0d), (rect.X, rect.Y, rect.Width, rect.Height));
    }

    [Fact]
    public void GetClientRects_DisconnectedElementOrDocumentWithoutBrowsingContext_IsEmpty()
    {
        var document = Load("<p>");
        Assert.Empty(document.CreateElement("div").GetClientRects());

        var standalone = new HtmlDocumentParser().Parse("<p id=p>");
        Assert.Empty(standalone.GetElementById("p")!.GetClientRects());
    }

    [Fact]
    public void OffsetProperties_AreRelativeToThePaddingEdgeOfTheOffsetParent()
    {
        var document = Parse("<div style='margin: 5px'><div id=p style='position: relative; padding: 7px; border: 3px solid'><span id=s style='margin-left: 4px'>XX</span></div></div>");
        var span = (HtmlElement)document.GetElementById("s")!;
        var parent = (HtmlElement)document.GetElementById("p")!;
        Assert.Same(parent, span.OffsetParent);
        Assert.Equal((11, 7, 20, 10), (span.OffsetLeft, span.OffsetTop, span.OffsetWidth, span.OffsetHeight));

        // The offsets relative to the body are relative to the initial containing block (as in the browsers)
        Assert.Same(document.Body, parent.OffsetParent);
        Assert.Equal((5, 5, 790, 30), (parent.OffsetLeft, parent.OffsetTop, parent.OffsetWidth, parent.OffsetHeight));
    }

    [Fact]
    public void ElementsFromPoint_HitTestsTheBoxesInTheReverseOfThePaintingOrder()
    {
        var document = Parse("""
            <div id=a style='width: 100px; height: 100px'>
              <div id=b style='position: absolute; top: 0; left: 0; width: 50px; height: 50px; z-index: 1'></div>
              <div id=c style='position: absolute; top: 0; left: 0; width: 60px; height: 60px; z-index: 2; pointer-events: none'></div>
              <div id=d style='position: absolute; top: 0; left: 0; width: 70px; height: 70px; visibility: hidden'></div>
            </div>
            <div id=clip style='overflow: hidden; width: 20px; height: 20px'><div id=big style='width: 200px; height: 20px'></div></div>
            """);
        Assert.Equal(["b", "a", "", ""], document.ElementsFromPoint(10, 10).Select(e => e.Id));
        Assert.Equal(["a", "", ""], document.ElementsFromPoint(55, 55).Select(e => e.Id));
        Assert.Equal("b", document.ElementFromPoint(10, 10)!.Id);

        // The overflow clip edge of an ancestor clips the hit testing of its descendants
        Assert.Equal(["big", "clip", "", ""], document.ElementsFromPoint(10, 110).Select(e => e.Id));
        Assert.Equal(["", ""], document.ElementsFromPoint(100, 110).Select(e => e.Id));
    }

    [Fact]
    public void ElementFromPoint_TextHitsItsParentElement()
    {
        var document = Parse("<div id=d><span id=s>XX</span></div>");
        Assert.Equal("s", document.ElementFromPoint(5, 5)!.Id);

        // The texts are not listed by elementsFromPoint(), only the boxes
        Assert.Equal(["s", "d", "", ""], document.ElementsFromPoint(5, 5).Select(e => e.Id));
    }

    [Fact]
    public void ElementFromPoint_RoundedCornersAndImageMaps()
    {
        var document = Parse("""
            <div id=r style='width: 100px; height: 100px; border-radius: 50px'></div>
            <map name=m><area id=area shape=rect coords='0,0,10,10' href='#'></map>
            <img id=i usemap='#m' style='display: block; width: 50px; height: 50px'>
            """);
        Assert.Equal("r", document.ElementFromPoint(50, 50)!.Id);
        Assert.Same(document.Body, document.ElementFromPoint(5, 5));
        Assert.Equal("area", document.ElementFromPoint(5, 105)!.Id);
        Assert.Equal("i", document.ElementFromPoint(30, 130)!.Id);
    }

    [Fact]
    public void ElementFromPoint_RoundedOverflowClip()
    {
        // The content of a box that clips its overflow is clipped to the curve of its padding edge: the child is not hit in the
        // rounded corners (inner radii of 40px: the border of 10px is outside of the clip), also through a scrolled offset
        var document = Parse("""
            <div id=clip style='overflow: hidden; width: 100px; height: 100px; border: 10px solid; border-radius: 50px'><div id=child style='width: 100px; height: 300px'></div></div>
            <div id=axis style='overflow-x: clip; width: 100px; height: 100px; border-radius: 50px'><div id=wide style='width: 100px; height: 100px'></div></div>
            """);
        Assert.Equal("child", document.ElementFromPoint(60, 60)!.Id);
        Assert.Equal("child", document.ElementFromPoint(23, 23)!.Id);
        Assert.Equal("clip", document.ElementFromPoint(20, 20)!.Id);
        Assert.Equal("clip", document.ElementFromPoint(5, 60)!.Id);
        Assert.Same(document.Body, document.ElementFromPoint(2, 2));
        Assert.Equal(["clip", "", ""], document.ElementsFromPoint(101, 101).Select(e => e.Id));

        // A box that clips a single axis clips to the rectangle of its overflow clip edge
        Assert.Equal("wide", document.ElementFromPoint(3, 123)!.Id);
    }

    [Fact]
    public void ElementFromPoint_RootOfA3DRenderingContextIsAPlane()
    {
        // The plane of the root of the 3D rendering context is in front of the child translated behind it, and behind the
        // child translated towards the viewer
        var document = Parse("""
            <div id=root style='width: 100px; height: 100px; transform-style: preserve-3d'>
              <div id=behind style='width: 50px; height: 40px; transform: translateZ(-10px)'></div>
              <div id=front style='width: 50px; height: 40px; transform: translateZ(10px)'></div>
            </div>
            """);
        Assert.Equal("root", document.ElementFromPoint(10, 10)!.Id);
        Assert.Equal("front", document.ElementFromPoint(10, 50)!.Id);
        Assert.Equal("root", document.ElementFromPoint(10, 90)!.Id);
    }

    [Fact]
    public void ElementFromPoint_PerspectiveThroughAnUntransformedBoxOfA3DRenderingContext()
    {
        // The perspective of the scene applies to the container, which has no transform: the card, translated towards the
        // viewer, is seen twice as big around the center of the scene (from 0 to 200px instead of 50px to 150px)
        var document = Parse("""
            <div id=scene style='width: 200px; height: 200px; perspective: 100px; transform-style: preserve-3d'>
              <div id=container style='width: 200px; height: 200px; transform-style: preserve-3d'>
                <div id=card style='position: absolute; left: 50px; top: 50px; width: 100px; height: 100px; transform: translateZ(50px)'></div>
              </div>
            </div>
            """);
        Assert.Equal("card", document.ElementFromPoint(10, 10)!.Id);
        Assert.Equal("card", document.ElementFromPoint(190, 190)!.Id);
        var rect = document.GetElementById("card")!.GetBoundingClientRect();
        Assert.Equal((0d, 0d, 200d, 200d), ToTuple(rect));
    }

    [Fact]
    public void ElementFromPoint_AnonymousTableCellHitsItsRowGroup()
    {
        // The anonymous cell of the block in the header group covers the width of the table: beside the block, the header
        // group is hit (the rows and the row groups are not hit otherwise)
        var document = Parse("""
            <div id=table style='display: table; width: 100px'>
              <div id=header style='display: table-header-group'><div id=block style='width: 20px; height: 20px'></div></div>
              <div id=row style='display: table-row'><div id=cell style='display: table-cell; height: 20px'></div></div>
            </div>
            """);
        Assert.Equal("block", document.ElementFromPoint(10, 10)!.Id);
        Assert.Equal("header", document.ElementFromPoint(50, 10)!.Id);
        Assert.Equal(["header", "table", "", ""], document.ElementsFromPoint(50, 10).Select(e => e.Id));
        Assert.Equal("cell", document.ElementFromPoint(50, 30)!.Id);
    }

    [Fact]
    public void ElementsFromPoint_BlockSplitOutOfAnInlineBoxIsInTheInlineBox()
    {
        var document = Parse("<div id=c><a id=a href='#'><div id=b style='height: 20px; margin: 0 100px'></div></a></div>");

        // The anonymous block box of the block is in the link, across the block container (its margins included)
        Assert.Equal("a", document.ElementFromPoint(50, 10)!.Id);
        Assert.Equal(["b", "a", "c", "", ""], document.ElementsFromPoint(150, 10).Select(e => e.Id));
    }

    [Fact]
    public void ElementsFromPoint_FixedDescendantOfATopLayerElement()
    {
        var document = Parse("""
            <dialog id=d1 style='top: 0'>A
              <div id=p popover style='top: 100px'>P
                <dialog id=d2 style='position: fixed; top: 200px; width: 100px; height: 50px; padding: 0'>B</dialog>
              </div>
            </dialog>
            """);
        ((HtmlDialogElement)document.GetElementById("d1")!).ShowModal();
        ((HtmlElement)document.GetElementById("p")!).ShowPopover();
        ((HtmlDialogElement)document.GetElementById("d2")!).Show();

        // The non-modal dialog is a fixed positioned descendant of the popover: it is painted with the popover, above the
        // ::backdrop of the modal dialog
        var rect = document.GetElementById("d2")!.GetBoundingClientRect();
        Assert.Equal("d2", document.ElementFromPoint(rect.X + 10, rect.Y + 10)!.Id);
    }

    [Fact]
    public void CaretPositionFromPoint_TextControls()
    {
        var document = Parse("<textarea id=t rows=3 cols=4 style='font: 10px/10px Ahem; padding: 0; border: 0'>12345678&#10;90</textarea>");
        var textArea = document.GetElementById("t")!;

        // The lines of the inner editor: the value wraps after 5 characters (the content box is 4em wide, and the space reserved
        // for the scrollbar, which is not displayed)
        var position = document.CaretPositionFromPoint(1, 15)!;
        Assert.Same(textArea, position.OffsetNode);
        Assert.Equal(5u, position.Offset);
        Assert.Equal(10u, document.CaretPositionFromPoint(11, 25)!.Offset);
        Assert.Equal((10d, 20d, 0d, 10d), ToTuple(document.CaretPositionFromPoint(11, 25)!.GetClientRect()!));
    }

    [Fact]
    public void ElementsFromPoint_RetargetsTheElementsOfShadowTrees()
    {
        var document = Parse("<div id=host></div>");
        var host = document.GetElementById("host")!;
        var shadowRoot = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        var div = document.CreateElement("div");
        div.SetAttribute("style", "height: 20px");
        shadowRoot.AppendChild(div);

        Assert.Equal(["div", "body", "html"], document.ElementsFromPoint(5, 5).Select(e => e.LocalName));
    }

    [Fact]
    public void ElementFromPoint_OtherPoints()
    {
        var document = Load("<p>a</p>", new ViewportOptions { Width = 100, Height = 50 });
        Assert.Same(document.DocumentElement, document.ElementFromPoint(10, 49));
        Assert.Null(document.ElementFromPoint(-1, 10));
        Assert.Null(document.ElementFromPoint(10, 51));
        Assert.Empty(document.ElementsFromPoint(101, 0));
    }

    [Fact]
    public void Range_RectanglesOfTheSelectedText()
    {
        var document = Parse("<div id=d>abcd</div><div id=e>xy</div>");
        var text = (Text)document.GetElementById("d")!.FirstChild!;
        var range = document.CreateRange();
        range.SetStart(text, 1);
        range.SetEnd(text, 3);
        Assert.Equal((10d, 0d, 20d, 10d), ToTuple(Assert.Single(range.GetClientRects())));

        // A collapsed range has a rectangle without width
        range.Collapse(toStart: true);
        Assert.Equal((10d, 0d, 0d, 10d), ToTuple(range.GetBoundingClientRect()));

        // The elements contained in the range give their boxes, the texts the rectangles of their characters
        range.SelectNodeContents(document.Body!);
        Assert.Equal(4, range.GetClientRects().Count);
        Assert.Equal((0d, 0d, 800d, 20d), ToTuple(range.GetBoundingClientRect()));
    }

    [Fact]
    public void CaretPositionFromPoint_ReturnsThePositionInTheText()
    {
        var document = Parse("<div id=d>abcd</div><img id=i style='display: block; width: 20px; height: 20px'>");
        var text = document.GetElementById("d")!.FirstChild!;
        var position = document.CaretPositionFromPoint(21, 5)!;
        Assert.Same(text, position.OffsetNode);
        Assert.Equal(2u, position.Offset);
        Assert.Equal((20d, 0d, 0d, 10d), ToTuple(position.GetClientRect()!));

        // At the right of the text on the line
        Assert.Equal(4u, document.CaretPositionFromPoint(300, 5)!.Offset);

        // Before or after a replaced element
        var image = document.GetElementById("i")!;
        Assert.Equal((document.Body!, (uint)image.GetIndex() + 1), (document.CaretPositionFromPoint(15, 15)!.OffsetNode, document.CaretPositionFromPoint(15, 15)!.Offset));
        Assert.Null(document.CaretPositionFromPoint(-1, 5));
    }

    [Fact]
    public void GetBoxQuads_AndConvertPointFromNode()
    {
        var document = Parse("<div id=a style='margin: 10px; padding: 5px; border: 2px solid; width: 50px; height: 20px'></div>");
        var element = document.GetElementById("a")!;
        var quad = Assert.Single(element.GetBoxQuads()).GetBounds();
        Assert.Equal((10d, 10d, 64d, 34d), ToTuple(quad));

        var content = Assert.Single(element.GetBoxQuads(new BoxQuadOptions { Box = CssBoxType.Content })).GetBounds();
        Assert.Equal((17d, 17d, 50d, 20d), ToTuple(content));

        // A point of the content box of the element in the coordinates of the viewport, and back
        var point = document.ConvertPointFromNode(new DomPointInit { X = 1, Y = 2 }, element, new ConvertCoordinateOptions { FromBox = CssBoxType.Content });
        Assert.Equal((18d, 19d), (point.X, point.Y));
        var local = element.ConvertPointFromNode(new DomPointInit { X = 18, Y = 19 }, document);
        Assert.Equal((8d, 9d), (local.X, local.Y));
    }

    [Fact]
    public void MouseEventOffset_IsRelativeToThePaddingEdgeOfTheTarget()
    {
        var document = Parse("<div id=t style='margin: 10px; border: 5px solid; width: 100px; height: 100px'></div>");
        var target = document.GetElementById("t")!;
        (double X, double Y, double PageY)? offset = null;
        target.AddEventListener("mousedown", e => offset = (((MouseEvent)e).OffsetX, ((MouseEvent)e).OffsetY, ((MouseEvent)e).PageY));
        var @event = new MouseEvent("mousedown", new MouseEventInit { ClientX = 30, ClientY = 40, Bubbles = true });
        target.DispatchEvent(@event);
        Assert.Equal((15d, 25d, 40d), offset);

        // The offsets computed while the event was dispatched are kept
        Assert.Equal((15d, 25d), (@event.OffsetX, @event.OffsetY));
    }

    [Fact]
    public void CheckVisibility_ContentVisibilityAutoOutsideOfTheViewport()
    {
        var document = Parse("<div style='content-visibility: auto'><div id=a>a</div></div><div style='height: 5000px'></div><div style='content-visibility: auto'><div id=b>b</div></div>");

        // The proximity to the viewport is determined by the rendering update
        _clock.Advance(TimeSpan.FromMilliseconds(20));
        _contexts.EventLoop.RunUntilIdle();
        Assert.True(document.GetElementById("a")!.CheckVisibility(new CheckVisibilityOptions { ContentVisibilityAuto = true }));
        Assert.False(document.GetElementById("b")!.CheckVisibility(new CheckVisibilityOptions { ContentVisibilityAuto = true }));
        Assert.True(document.GetElementById("b")!.CheckVisibility());
    }

    private sealed class RectComparer : IEqualityComparer<(double X, double Y, double Width, double Height)>
    {
        public bool Equals((double X, double Y, double Width, double Height) x, (double X, double Y, double Width, double Height) y)
            => Math.Abs(x.X - y.X) < 1e-6 && Math.Abs(x.Y - y.Y) < 1e-6 && Math.Abs(x.Width - y.Width) < 1e-6 && Math.Abs(x.Height - y.Height) < 1e-6;

        public int GetHashCode((double X, double Y, double Width, double Height) obj) => 0;
    }

    [Fact]
    public void InnerWidthAndInnerHeight_AreTheViewport()
    {
        var document = Load("<iframe width=120></iframe>", new ViewportOptions { Width = 1280, Height = 720 });
        Assert.Equal(1280, document.DefaultView!.InnerWidth);
        Assert.Equal(720, document.DefaultView.InnerHeight);

        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        Assert.Equal(120, iframe.ContentWindow!.InnerWidth);
        Assert.Equal(150, iframe.ContentWindow.InnerHeight);
    }

    [Fact]
    public void MatchMedia_EvaluatesTheMediaQueryListAgainstTheViewport()
    {
        var document = Load("<p>", new ViewportOptions { Width = 1280, Height = 720, DevicePixelRatio = 2 });
        var window = document.DefaultView!;

        var list = window.MatchMedia("(min-width: 1000px), all and (max-width: 10px)");
        Assert.Equal("(min-width: 1000px), (max-width: 10px)", list.Media);
        Assert.True(list.Matches);
        Assert.False(window.MatchMedia("(max-width: 1000px)").Matches);
        Assert.True(window.MatchMedia("(resolution: 2dppx)").Matches);
        Assert.True(window.MatchMedia("").Matches);

        var invalid = window.MatchMedia("::");
        Assert.Equal("not all", invalid.Media);
        Assert.False(invalid.Matches);
    }

    [Fact]
    public void MatchMedia_FiresChangeEventsWhenTheRenderingIsUpdated()
    {
        var document = Load("<iframe width=200 height=100></iframe>");
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        var list = iframe.ContentWindow!.MatchMedia("(max-width: 150px)");
        var events = new List<MediaQueryListEvent>();
        list.AddListener(e => events.Add((MediaQueryListEvent)e));
        Assert.False(list.Matches);

        iframe.SetAttribute("width", "100");
        Assert.True(list.Matches);
        Assert.Empty(events);

        // The changes are reported before the animation frame callbacks
        var matchesInFrame = new List<int>();
        document.DefaultView!.RequestAnimationFrame(_ => matchesInFrame.Add(events.Count));
        _clock.Advance(TimeSpan.FromMilliseconds(20));
        _contexts.EventLoop.RunUntilIdle();
        var e = Assert.Single(events);
        Assert.Equal([1], matchesInFrame);
        Assert.Equal("change", e.Type);
        Assert.Equal("(max-width: 150px)", e.Media);
        Assert.True(e.Matches);
        Assert.True(e.IsTrusted);
    }

    [Fact]
    public void InnerWidthAndInnerHeight_AreTheContentBoxOfTheIframe()
    {
        var document = Load("<iframe width=120 style='width: 100px'></iframe><iframe style='width: 100px; height: 50px; padding: 5px; border: 1px solid; box-sizing: border-box'></iframe><iframe style='width: 50%'></iframe>");
        var iframes = document.QuerySelectorAll("iframe").Cast<HtmlIFrameElement>().ToArray();
        Assert.Equal(100, iframes[0].ContentWindow!.InnerWidth);
        Assert.Equal(150, iframes[0].ContentWindow!.InnerHeight);
        Assert.Equal(88, iframes[1].ContentWindow!.InnerWidth);
        Assert.Equal(38, iframes[1].ContentWindow!.InnerHeight);

        // A percentage is resolved by the layout of the parent document (the body is 784px wide)
        Assert.Equal(392, iframes[2].ContentWindow!.InnerWidth);
    }

    [Fact]
    public void Resize_IsFiredBeforeTheMediaQueryListChangeEvents()
    {
        var document = Load("<iframe style='width: 200px; height: 100px'></iframe>");
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        var window = iframe.ContentWindow!;
        var events = new List<string>();
        window.MatchMedia("(max-width: 150px)").AddListener(e => events.Add(e.Type));
        window.AddEventListener("resize", e => events.Add(e.Type + " " + window.InnerWidth));

        iframe.Style.SetProperty("width", "100px");
        document.DefaultView!.RequestAnimationFrame(_ => events.Add("frame"));
        _clock.Advance(TimeSpan.FromMilliseconds(20));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["resize 100", "change", "frame"], events);

        // The viewport did not change
        document.DefaultView!.RequestAnimationFrame(_ => events.Add("frame"));
        _clock.Advance(TimeSpan.FromMilliseconds(20));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["resize 100", "change", "frame", "frame"], events);
    }

    [Fact]
    public void MatchContainer_EvaluatesTheContainerConditionsForTheElement()
    {
        var document = Load("<style>#c { container: box / inline-size; width: 200px; --theme: dark }</style><div id=c><div id=target></div></div>");
        var target = document.GetElementById("target")!;

        var list = target.MatchContainer("(width<50px),box (min-width:100px)");
        Assert.Equal("(width < 50px), box (min-width: 100px)", list.Query);
        Assert.True(list.Matches);
        Assert.True(target.MatchContainer("style(--theme: dark)").Matches);
        Assert.False(target.MatchContainer("other (width > 0px)").Matches);
        Assert.False(target.MatchContainer("(unknown-feature)").Matches);

        var invalid = target.MatchContainer("(width > 100px),");
        Assert.Equal("", invalid.Query);
        Assert.False(invalid.Matches);

        target.Remove();
        Assert.False(list.Matches);
    }

    [Fact]
    public void MatchContainer_FiresChangeEventsWhenTheRenderingIsUpdated()
    {
        var document = Load("<div id=c style='container-type: inline-size; width: 200px'><div id=target></div></div>");
        var container = document.GetElementById("c")!;
        var list = document.GetElementById("target")!.MatchContainer("(width > 100px)");
        var events = new List<Event>();
        list.OnChange = events.Add;
        Assert.True(list.Matches);

        // A change that is reverted before the rendering is updated is not reported
        container.SetAttribute("style", "container-type: inline-size; width: 50px");
        Assert.False(list.Matches);
        container.SetAttribute("style", "container-type: inline-size; width: 200px");
        document.DefaultView!.RequestAnimationFrame(_ => { });
        _clock.Advance(TimeSpan.FromMilliseconds(20));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(events);

        container.SetAttribute("style", "container-type: inline-size; width: 50px");
        document.DefaultView!.RequestAnimationFrame(_ => { });
        _clock.Advance(TimeSpan.FromMilliseconds(20));
        _contexts.EventLoop.RunUntilIdle();
        var e = Assert.Single(events);
        Assert.Equal("change", e.Type);
        Assert.Same(list, e.Target);
        Assert.False(e.Bubbles);
        Assert.True(e.IsTrusted);
    }

    [Fact]
    public void MatchMedia_IframeNotBeingRenderedHasAnEmptyViewport()
    {
        var document = Load("<iframe style='display:none'></iframe>");
        var window = ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentWindow!;
        Assert.True(window.MatchMedia("(max-width: 0px)").Matches);
        Assert.Equal(0, window.InnerWidth);
    }

    [Fact]
    public void Screen_IsTheViewportOfTheTopLevelBrowsingContext()
    {
        var document = Load("<iframe width=120></iframe>", new ViewportOptions { Width = 1280, Height = 720, DevicePixelRatio = 1.5 });
        var window = document.DefaultView!;
        Assert.Same(window.Screen, window.Screen);
        Assert.Equal((1280, 720, 1280, 720), (window.Screen.Width, window.Screen.Height, window.Screen.AvailWidth, window.Screen.AvailHeight));
        Assert.Equal(24u, window.Screen.ColorDepth);
        Assert.Equal(24u, window.Screen.PixelDepth);
        Assert.Equal(1.5, window.DevicePixelRatio);

        var iframeWindow = ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentWindow!;
        Assert.Equal(1280, iframeWindow.Screen.Width);
        Assert.Equal(1.5, iframeWindow.DevicePixelRatio);
    }

    [Fact]
    public void OffsetParent_IsTheNearestPositionedAncestorOrTheBody()
    {
        var document = Load("<div id=positioned style='position:relative'><table><tr><td id=cell><span id=a></span></td></tr></table></div><div id=b></div><div id=hidden style='display:none'></div>");
        Assert.Same(document.GetElementById("cell"), ((HtmlElement)document.GetElementById("a")!).OffsetParent);
        Assert.Same(document.Body, ((HtmlElement)document.GetElementById("b")!).OffsetParent);
        Assert.Same(document.QuerySelector("table"), ((HtmlElement)document.GetElementById("cell")!).OffsetParent);
        Assert.Same(document.GetElementById("positioned"), ((HtmlElement)document.QuerySelector("table")!).OffsetParent);
        Assert.Null(((HtmlElement)document.GetElementById("hidden")!).OffsetParent);
        Assert.Null(document.Body!.OffsetParent);
    }

    [Fact]
    public void OffsetParent_SkipsTheShadowTreeOfTheSlotOfTheElement()
    {
        var document = Parse("<div id=container style='position: relative'><div id=host><div id=static></div><div id=fixed style='position: fixed'></div></div></div>");
        var shadowRoot = document.GetElementById("host")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadowRoot.InnerHtml = "<div style='transform: translate(10px, 10px)'><div style='position: fixed; padding-left: 85px; padding-top: 45px'><slot></slot></div></div>";

        // The positioned ancestors and the fixed containing block of the slotted elements are in a shadow tree they are not
        // in: the offset parent is the nearest one outside of it
        var container = document.GetElementById("container");
        foreach (var id in new[] { "static", "fixed" })
        {
            var element = (HtmlElement)document.GetElementById(id)!;
            Assert.Same(container, element.OffsetParent);
            Assert.Equal(85, element.OffsetLeft);
            Assert.Equal(45, element.OffsetTop);
        }
    }

    [Fact]
    public void ShadowRoot_ElementFromPoint_IsRetargetedAgainstTheShadowRoot()
    {
        var document = Parse("<div id=host style='width: 100px; height: 100px'><span id=slotted>XX</span></div><div id=other style='width: 100px; height: 100px'></div>");
        var host = document.GetElementById("host")!;
        var shadowRoot = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Closed });
        shadowRoot.InnerHtml = "<div id=inner style='height: 50px'><slot></slot></div><div id=below style='height: 50px'></div>";
        var inner = shadowRoot.QuerySelector("#inner")!;
        var below = shadowRoot.QuerySelector("#below")!;
        var slotted = document.GetElementById("slotted")!;

        // The elements of the shadow tree are not replaced by the shadow host
        Assert.Same(host, document.ElementFromPoint(5, 75));
        Assert.Same(below, shadowRoot.ElementFromPoint(5, 75));
        Assert.Same(slotted, shadowRoot.ElementFromPoint(5, 5));
        Assert.Equal(new Element[] { slotted, host, document.Body!, document.DocumentElement! }, document.ElementsFromPoint(5, 5));
        Assert.Equal(new Element[] { slotted, inner, host, document.Body!, document.DocumentElement! }, shadowRoot.ElementsFromPoint(5, 5));

        // The elements outside of the shadow tree are returned as well
        Assert.Same(document.GetElementById("other"), shadowRoot.ElementFromPoint(5, 150));
        Assert.Null(shadowRoot.ElementFromPoint(-1, 5));
        Assert.Empty(shadowRoot.ElementsFromPoint(-1, 5));
    }

    [Fact]
    public void OffsetClientAndScrollSizes_AreTheBoxesWhenTheyAreKnownWithoutLayout()
    {
        var document = Load("""
            <style>#a { width: 100px; height: 20px; padding: 5px; border: 2px solid } #a:lang(fr) { width: 50px }</style>
            <div id=a lang=fr-CA></div>
            <div id=auto></div>
            <span id=inline style='width: 100px'></span>
            <div id=hidden style='display: none; width: 100px'></div>
            """);
        var a = (HtmlElement)document.GetElementById("a")!;
        Assert.Equal(64, a.OffsetWidth);
        Assert.Equal(34, a.OffsetHeight);
        Assert.Equal(60, a.ClientWidth);
        Assert.Equal(30, a.ScrollHeight);

        // The height of a block depends on its content, the inline boxes are sized by their content, and the elements that
        // are not being rendered have no box
        Assert.Equal(0, ((HtmlElement)document.GetElementById("auto")!).OffsetHeight);
        Assert.Equal(0, ((HtmlElement)document.GetElementById("inline")!).OffsetWidth);
        Assert.Equal(0, ((HtmlElement)document.GetElementById("hidden")!).OffsetWidth);
    }

    [Fact]
    public void ScrollingElementAndClientSize_UseTheViewport()
    {
        var document = Load("<!DOCTYPE html><p id=p>", new ViewportOptions { Width = 1280, Height = 720 });
        Assert.Same(document.DocumentElement, document.ScrollingElement);
        Assert.Equal((1280, 720), (document.DocumentElement!.ClientWidth, document.DocumentElement.ClientHeight));
        Assert.Equal((1280, 720), (document.DocumentElement.ScrollWidth, document.DocumentElement.ScrollHeight));
        // The width of a block fills the body (without its margins); its height depends on the layout
        Assert.Equal((1264, 0), (document.GetElementById("p")!.ClientWidth, document.GetElementById("p")!.ScrollHeight));

        var quirks = Load("<p>");
        Assert.Same(quirks.Body, quirks.ScrollingElement);
        Assert.Equal(800, quirks.Body!.ClientWidth);

        // In quirks mode, the root element is not the viewport: its client area is its padding box (it fills the viewport)
        Assert.Equal(800, quirks.DocumentElement!.ClientWidth);
    }

    [Fact]
    public void ClientSize_ReplacedElements()
    {
        var document = Load("<!DOCTYPE html><iframe id=a></iframe><iframe id=b width=44 height=33 style='padding: 2px'></iframe><canvas id=c width=40 height=20 style='width: 80px'></canvas><canvas id=d style='display: none'></canvas><video id=e style='box-sizing: border-box; width: 100px; height: 50px; padding: 5px; border: 1px solid'></video>");
        Assert.Equal((300, 150), (document.GetElementById("a")!.ClientWidth, document.GetElementById("a")!.ClientHeight));
        Assert.Equal((48, 37), (document.GetElementById("b")!.ClientWidth, document.GetElementById("b")!.ClientHeight));
        // Without scripting, a canvas is not replaced: it renders its fallback content in an inline box
        Assert.Equal((0, 0), (document.GetElementById("c")!.ClientWidth, document.GetElementById("c")!.ClientHeight));
        Assert.Equal((0, 0), (document.GetElementById("d")!.ClientWidth, document.GetElementById("d")!.ClientHeight));
        Assert.Equal((98, 48), (document.GetElementById("e")!.ClientWidth, document.GetElementById("e")!.ClientHeight));
    }

    [Fact]
    public void CheckVisibility_UsesTheComputedStyles()
    {
        var document = Load("<div id=a style='opacity:0'><span id=b style='visibility:hidden'>b</span></div><div style='content-visibility:hidden'><p id=c>c</p></div>");
        var b = document.GetElementById("b")!;
        Assert.True(b.CheckVisibility());
        Assert.False(b.CheckVisibility(new CheckVisibilityOptions { OpacityProperty = true }));
        Assert.False(b.CheckVisibility(new CheckVisibilityOptions { VisibilityProperty = true }));
        Assert.True(document.GetElementById("a")!.CheckVisibility(new CheckVisibilityOptions { VisibilityProperty = true }));
        Assert.False(document.GetElementById("c")!.CheckVisibility());
    }
}
