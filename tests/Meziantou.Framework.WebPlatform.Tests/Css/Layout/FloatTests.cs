using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The floats, the clearance and the boxes that avoid the floats (CSS 2.1 §9.5), measured with Ahem (1em glyphs).</summary>
public sealed class FloatTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html) => Load("<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static PhysicalRect GetRect(Document document, string id) => document.GetLayoutEngine().GetBorderBoxRect(document.GetElementById(id)!) ?? throw new InvalidOperationException("No box");

    /// <summary>The rectangles of the text fragments of the text nodes of an element, in tree order.</summary>
    private static List<PhysicalRect> GetTextRects(Document document, string id)
    {
        var result = new List<PhysicalRect>();
        foreach (var node in document.GetElementById(id)!.Descendants())
        {
            if (node is Text text)
            {
                foreach (var fragment in document.GetLayoutEngine().GetTextFragments(text))
                {
                    if (fragment.End > fragment.Start)
                    {
                        result.Add(fragment.GetRectInRoot());
                    }
                }
            }
        }

        return result;
    }

    [Fact]
    public void Floats_PlacedSideBySide()
    {
        var document = Parse("""
            <div style='width: 300px'>
              <div id=a style='float: left; width: 100px; height: 50px'></div>
              <div id=b style='float: left; width: 100px; height: 30px; margin-left: 10px'></div>
              <div id=c style='float: right; width: 50px; height: 20px'></div>
              <div id=d style='float: right; width: 30px; height: 20px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(0, 0, 100, 50), GetRect(document, "a"));
        Assert.Equal(new PhysicalRect(110, 0, 100, 30), GetRect(document, "b"));
        Assert.Equal(new PhysicalRect(250, 0, 50, 20), GetRect(document, "c"));
        Assert.Equal(new PhysicalRect(220, 0, 30, 20), GetRect(document, "d"));
    }

    [Fact]
    public void Floats_MovedDownWhenTheyDoNotFit()
    {
        // The third float does not fit next to the first two: it is placed below the shortest one, next to the tallest one
        var document = Parse("""
            <div style='width: 300px'>
              <div id=a style='float: left; width: 100px; height: 50px'></div>
              <div id=b style='float: left; width: 150px; height: 30px'></div>
              <div id=c style='float: left; width: 100px; height: 10px'></div>
              <div id=d style='float: left; width: 250px; height: 10px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(100, 30, 100, 10), GetRect(document, "c"));
        Assert.Equal(new PhysicalRect(0, 50, 250, 10), GetRect(document, "d"));
    }

    [Fact]
    public void Floats_FitWithinTheLayoutUnitOfTheBrowsers()
    {
        // The six floats of www.python.org: 16.6666665% of 1200px is not exactly 200px, but they fit on one line in the browsers,
        // whose sizes are multiples of 1/64px
        var document = Parse("<div style='width: 1200px'>" + string.Concat(Enumerable.Range(0, 6).Select(i => $"<div id=f{i} style='float: left; width: 16.6666665%; height: 10px'></div>")) + "</div>");
        Assert.Equal(0, GetRect(document, "f5").Y);
        Assert.Equal(1000, GetRect(document, "f5").X, 0.01);
    }

    [Fact]
    public void Floats_DoNotAffectTheFlow()
    {
        var document = Parse("""
            <div id=parent style='width: 300px'>
              <div id=f style='float: left; width: 100px; height: 50px'></div>
              <div id=b style='height: 10px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(0, 0, 300, 10), GetRect(document, "b"));
        Assert.Equal(10, GetRect(document, "parent").Height);
    }

    [Theory]
    [InlineData("display: flow-root")]
    [InlineData("overflow: hidden")]
    [InlineData("display: inline-block")]
    [InlineData("float: left")]
    public void FormattingContextRoot_ContainsItsFloats(string style)
    {
        var document = Parse($"""
            <div id=root style='{style}; width: 300px'>
              <div style='float: left; width: 100px; height: 50px; margin-bottom: 5px'></div>
              <div style='height: 10px'></div>
            </div>
            """);
        Assert.Equal(55, GetRect(document, "root").Height);
    }

    [Fact]
    public void Clearance_MovesTheBoxBelowTheFloats()
    {
        var document = Parse("""
            <div style='width: 300px'>
              <div style='float: left; width: 100px; height: 50px'></div>
              <div style='float: right; width: 100px; height: 80px'></div>
              <div id=left style='clear: left; height: 10px; margin-top: 20px'></div>
              <div id=both style='clear: both; height: 10px'></div>
            </div>
            """);
        Assert.Equal(50, GetRect(document, "left").Y);
        Assert.Equal(80, GetRect(document, "both").Y);
        Assert.NotNull(document.GetLayoutEngine().GetFragments(document.GetElementById("left")!)[0].CollapsedMargins.Clearance);
    }

    [Fact]
    public void Clearance_HypotheticalPositionPastTheFloats()
    {
        // The hypothetical position (after the collapsed margins) is already below the float: no clearance (the border of the
        // parent separates the margin from the margin of the parent, see Clearance_SeparatesTheMarginFromTheParent otherwise)
        var document = Parse("""
            <div style='width: 300px; border-top: 1px solid'>
              <div style='float: left; width: 100px; height: 20px'></div>
              <div id=cleared style='clear: left; height: 10px; margin-top: 30px'></div>
            </div>
            """);
        Assert.Equal(31, GetRect(document, "cleared").Y);
        Assert.Null(document.GetLayoutEngine().GetFragments(document.GetElementById("cleared")!)[0].CollapsedMargins.Clearance);
    }

    [Fact]
    public void Clearance_FloatClearsPreviousFloats()
    {
        var document = Parse("""
            <div style='width: 300px'>
              <div style='float: left; width: 100px; height: 50px'></div>
              <div id=f style='float: left; clear: left; width: 100px; height: 10px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(0, 50, 100, 10), GetRect(document, "f"));
    }

    [Fact]
    public void FloatPosition_IncludesTheMarginsCollapsingWithTheParent()
    {
        // The margin of the second child collapses with the margin of the parent: the parent and the float are below it
        var document = Parse("""
            <div style='display: flow-root'>
              <div id=parent>
                <div id=f style='float: left; width: 10px; height: 10px'></div>
                <div style='margin-top: 30px; height: 10px'></div>
              </div>
            </div>
            """);
        Assert.Equal(30, GetRect(document, "parent").Y);
        Assert.Equal(30, GetRect(document, "f").Y);
    }

    [Fact]
    public void FormattingContextRoot_PlacedNextToTheFloats()
    {
        var document = Parse("""
            <div style='width: 300px'>
              <div style='float: left; width: 100px; height: 50px'></div>
              <div id=auto style='overflow: hidden; height: 20px'></div>
              <div id=fixed style='display: flow-root; width: 250px; height: 10px'></div>
              <div id=margin style='display: flow-root; height: 10px; margin-left: 30px'></div>
            </div>
            """);

        // An auto width shrinks to the space next to the float; a fixed width that does not fit moves the box below the float
        Assert.Equal(new PhysicalRect(100, 0, 200, 20), GetRect(document, "auto"));
        Assert.Equal(new PhysicalRect(0, 50, 250, 10), GetRect(document, "fixed"));
        Assert.Equal(new PhysicalRect(30, 60, 270, 10), GetRect(document, "margin"));
    }

    [Fact]
    public void FormattingContextRoot_MarginsOverlapTheFloats()
    {
        // A margin narrows the space only when it is larger than the distance of the float from the edge
        var document = Parse("""
            <div style='width: 300px'>
              <div style='float: left; width: 100px; height: 50px'></div>
              <div id=small style='display: flow-root; height: 10px; margin-left: 50px'></div>
              <div id=large style='display: flow-root; height: 10px; margin-left: 150px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(100, 0, 200, 10), GetRect(document, "small"));
        Assert.Equal(new PhysicalRect(150, 10, 150, 10), GetRect(document, "large"));
    }

    [Fact]
    public void LineBoxes_ShortenedByTheFloats()
    {
        var document = Parse("""
            <div id=d style='width: 100px'>
              <div style='float: left; width: 30px; height: 15px'></div>
              <div style='float: right; width: 20px; height: 25px'></div>
              XX XX XX XX XX
            </div>
            """);

        // The first two lines are between the floats (50px), the third one next to the right float only (80px)
        var texts = GetTextRects(document, "d");
        Assert.Equal(new PhysicalRect(30, 0, 50, 10), texts[0]);
        Assert.Equal(new PhysicalRect(30, 10, 50, 10), texts[1]);
        Assert.Equal(new PhysicalRect(0, 20, 20, 10), texts[2]);
    }

    [Fact]
    public void LineBoxes_MovedDownWhenTheContentDoesNotFit()
    {
        var document = Parse("""
            <div id=d style='width: 100px'>
              <div style='float: left; width: 80px; height: 15px'></div>
              XXXXX
            </div>
            """);
        Assert.Equal(new PhysicalRect(0, 15, 50, 10), Assert.Single(GetTextRects(document, "d")));
    }

    [Fact]
    public void LineBoxes_RightToLeft()
    {
        var document = Parse("""
            <div id=d dir=rtl style='width: 100px'>
              <div style='float: left; width: 30px; height: 10px'></div>
              <div style='float: inline-start; width: 20px; height: 10px'></div>
              XX
            </div>
            """);

        // inline-start is the right in rtl: the line is between the floats, aligned to its start (the right)
        Assert.Equal(new PhysicalRect(60, 0, 20, 10), Assert.Single(GetTextRects(document, "d")));
    }

    [Fact]
    public void FloatInInlineContent_PlacedOnTheCurrentLine()
    {
        var document = Parse("""
            <div id=d style='width: 100px'>XX <span id=f style='float: left; width: 20px; height: 10px'></span>XX</div>
            """);

        // The float fits next to the content of the line: it is at the top of the line, the content moves after it
        Assert.Equal(new PhysicalRect(0, 0, 20, 10), GetRect(document, "f"));
        var texts = GetTextRects(document, "d");
        Assert.Equal(20, texts[0].X);
    }

    [Fact]
    public void FloatInInlineContent_PlacedBelowTheLineWhenItDoesNotFit()
    {
        var document = Parse("""
            <div id=d style='width: 100px'>XXXXX <span id=f style='float: right; width: 60px; height: 10px'></span>XX</div>
            """);
        Assert.Equal(new PhysicalRect(40, 10, 60, 10), GetRect(document, "f"));
        var texts = GetTextRects(document, "d");
        Assert.Equal(0, texts[0].X);
        Assert.Equal(0, texts[0].Y);
    }

    [Fact]
    public void LineBreak_Clear()
    {
        var document = Parse("""
            <div id=d style='width: 100px'><span style='float: left; width: 20px; height: 35px'></span>X<br style='clear: left'>X</div>
            """);
        var texts = GetTextRects(document, "d");
        Assert.Equal(new PhysicalRect(20, 0, 10, 10), texts[0]);
        Assert.Equal(new PhysicalRect(0, 35, 10, 10), texts[1]);
        Assert.Equal(45, GetRect(document, "d").Height);
    }

    [Fact]
    public void ShrinkToFit_BlockLevelChildren()
    {
        var document = Parse("""
            <div style='width: 300px'>
              <div id=f style='float: left'>
                <div style='width: 50px; height: 10px; margin-right: 5px'></div>
                <div style='padding: 0 3px'>XX XXX</div>
              </div>
              <div id=narrow style='float: left; clear: left; width: 40px'>
                <div id=inner style='float: left'>XXXXXX XX</div>
              </div>
            </div>
            """);

        // max(55, 3 + 60 + 3) = 66
        Assert.Equal(66, GetRect(document, "f").Width);

        // The available width is 40: the min-content width (60) is used
        Assert.Equal(60, GetRect(document, "inner").Width);
    }

    [Fact]
    public void ShapeOutside_Circle()
    {
        var document = Parse("""
            <div id=d style='width: 200px'>
              <div style='float: left; width: 100px; height: 100px; shape-outside: circle(50%)'></div>
              <span id=a>X</span><br><span id=b>X</span><br><span id=c>X</span>
            </div>
            """);

        // The first line (y 0-10) is shortened by the extent of the circle in its band: x = 50 + sqrt(50² - 40²) = 80
        Assert.Equal(80, GetRect(document, "a").X, 0.01);
        Assert.Equal(50 + Math.Sqrt((50 * 50) - (30 * 30)), GetRect(document, "b").X, 0.01);
        Assert.Equal(50 + Math.Sqrt((50 * 50) - (20 * 20)), GetRect(document, "c").X, 0.01);
    }

    [Fact]
    public void ShapeOutside_PolygonWithMargin()
    {
        var document = Parse("""
            <div id=d style='width: 300px; font-size: 20px; line-height: 40px'>
              <div style='float: left; width: 200px; height: 200px; shape-outside: polygon(20px 40px, 160px 180px, 20px 180px); shape-margin: 20px'></div>
              <span id=a>X</span><br><span id=b>X</span><br><span id=c>X</span><br><span id=d4>X</span><br><span id=e>X</span><br><span id=f>X</span>
            </div>
            """);

        // WPT css/css-shapes/spec-examples/shape-outside-018.html
        Assert.Equal(48, GetRect(document, "a").X, 1d);
        Assert.Equal(88, GetRect(document, "b").X, 1d);
        Assert.Equal(128, GetRect(document, "c").X, 1d);
        Assert.Equal(168, GetRect(document, "d4").X, 1d);
        Assert.Equal(180, GetRect(document, "e").X, 1d);
        Assert.Equal(0, GetRect(document, "f").X, 1d);
    }

    [Fact]
    public void ShapeOutside_PolygonEdgesThatTouchTheLine()
    {
        // WPT css/css-shapes/shape-outside/supported-shapes/polygon/shape-outside-polygon-007.html: the line at y 20-30 is under
        // the wide part of the polygon and above its last part: only the narrow part (40px) is in its band; the line at y 40
        // is below the polygon
        var document = Parse("""
            <div style='width: 100px'>
              <div style='float: left; width: 100px; height: 100px; shape-outside: polygon(0px 0px, 80px 0px, 80px 20px, 40px 20px, 40px 30px, 70px 30px, 70px 40px, 0px 40px)'></div>
              <span id=a>XX</span> <span id=b>XX</span> <span id=c>XXXXXX</span> <span id=d>XXX</span> <span id=e>XXXXXXXXXX</span>
            </div>
            """);
        Assert.Equal(new PhysicalRect(80, 0, 20, 10), GetRect(document, "a"));
        Assert.Equal(new PhysicalRect(80, 10, 20, 10), GetRect(document, "b"));
        Assert.Equal(new PhysicalRect(40, 20, 60, 10), GetRect(document, "c"));
        Assert.Equal(new PhysicalRect(70, 30, 30, 10), GetRect(document, "d"));
        Assert.Equal(new PhysicalRect(0, 40, 100, 10), GetRect(document, "e"));
    }

    [Fact]
    public void ShapeOutside_LineMovesDownAlongTheShape()
    {
        // WPT css/css-shapes/shape-outside/assorted/float-retry-push-circle.html: the box does not fit next to the top of the
        // circle; it is moved down pixel by pixel until it fits (the circle is 50px wide at y 50)
        var document = Parse("""
            <div style='width: 300px'>
              <div style='float: left; width: 100px; height: 100px; shape-outside: circle(70.710678px at 0px 0px)'></div>
              <span id=a style='display: inline-block; width: 250px; height: 20px'></span>
            </div>
            """);
        Assert.Equal(50, GetRect(document, "a").X, 0.01);
        Assert.Equal(50, GetRect(document, "a").Y);
    }

    [Fact]
    public void ShapeOutside_LineIsBrokenAgainInItsOwnBand()
    {
        // WPT css/css-shapes/shape-outside/shape-box/shape-outside-border-box-border-radius-002.html: two boxes fit at the top of
        // the circle, but not in the band of the taller one; the first box alone is laid out in its own band (24px tall)
        var document = Parse("""
            <div style='width: 200px; line-height: 0'>
              <div style='float: left; width: 120px; height: 120px; border-radius: 50%; shape-outside: border-box'></div>
              <div id=a style='display: inline-block; width: 60px; height: 24px'></div>
              <div id=b style='display: inline-block; width: 60px; height: 36px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(108, 0, 60, 24), GetRect(document, "a"));
        Assert.Equal(new PhysicalRect(120, 24, 60, 36), GetRect(document, "b"));
    }

    [Fact]
    public void ShapeOutside_EmptyShapeAndNoRenderingBackend()
    {
        // A shape without area does not shorten the lines; a shape from an image is the margin box without rendering backend
        var document = Parse("""
            <div style='width: 200px'>
              <div style='float: left; width: 100px; height: 20px; shape-outside: inset(50%)'></div>
              <span id=a>X</span>
            </div>
            <div style='width: 200px; clear: both'>
              <div style='float: left; width: 100px; height: 20px; shape-outside: linear-gradient(to right, black 50%, transparent 50%)'></div>
              <span id=b>X</span>
            </div>
            """);
        Assert.Equal(0, GetRect(document, "a").X);
        Assert.Equal(100, GetRect(document, "b").X);
    }

    [Fact]
    public void ShapeOutside_DoesNotAffectFormattingContextRoots()
    {
        var document = Parse("""
            <div style='width: 200px'>
              <div style='float: left; width: 100px; height: 100px; shape-outside: inset(0 80px 0 0)'></div>
              <div id=root style='display: flow-root; height: 10px'></div>
            </div>
            """);
        Assert.Equal(100, GetRect(document, "root").X);
    }

    [Fact]
    public void FormattingContextRoot_MovedBelowFloat_SeparatesItsMarginFromItsParent()
    {
        // WPT css/CSS2/floats/new-fc-separates-from-float-3.html: the root does not fit next to the float, its margin does not
        // collapse with the margin of its parent (whose float stays at the top), it is placed below the float
        var document = Parse("""
            <div id=root style='display: flow-root; width: 100px'>
              <div id=parent>
                <div id=float style='float: right; width: 50px; height: 30px'></div>
                <div id=fc style='overflow: hidden; width: 100px; height: 70px; margin-top: 100px'></div>
              </div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(0, 0, 100, 100), GetRect(document, "parent"));
        Assert.Equal(new PhysicalRect(50, 0, 50, 30), GetRect(document, "float"));
        Assert.Equal(new PhysicalRect(0, 30, 100, 70), GetRect(document, "fc"));
        Assert.Equal(100, GetRect(document, "root").Height);
    }

    [Fact]
    public void FormattingContextRoot_NextToFloat_MarginCollapsesWithItsParent()
    {
        // WPT css/css-sizing/stretch/block-height-005.html: the root fits next to the float, the margins collapse through the parent
        var document = Parse("""
            <div id=parent style='width: 100px'>
              <div id=float style='float: left; width: 30px; height: 30px'></div>
              <div id=fc style='display: flow-root; width: 30px; height: 10px; margin-top: 50px'></div>
            </div>
            """);
        Assert.Equal(50, GetRect(document, "parent").Y);
        Assert.Equal(new PhysicalRect(0, 50, 30, 30), GetRect(document, "float"));
        Assert.Equal(new PhysicalRect(30, 50, 30, 10), GetRect(document, "fc"));
    }

    [Fact]
    public void Clearance_SeparatesTheMarginFromTheParent()
    {
        // WPT css/css-sizing/stretch/block-height-005.html: with its margin collapsed with the one of its parent, the box would
        // be next to the float: it has clearance, its margin no longer collapses with the parent, the float stays at the top of
        // the parent and the box is placed below it
        var document = Parse("""
            <div id=parent style='width: 100px'>
              <div id=float style='float: left; width: 30px; height: 30px'></div>
              <div id=clear style='clear: both; height: 10px; margin-top: 50px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(0, 0, 100, 40), GetRect(document, "parent"));
        Assert.Equal(new PhysicalRect(0, 0, 30, 30), GetRect(document, "float"));
        Assert.Equal(new PhysicalRect(0, 30, 100, 10), GetRect(document, "clear"));
    }

    [Fact]
    public void Clearance_MarginsCollapsingThroughTheClearedBox_StayInTheParent()
    {
        // WPT css/CSS2/floats-clear/margin-collapse-033.xht: the margins that collapse through a box with clearance do not collapse
        // with the block-end margin of the parent
        var document = Parse("""
            <div id=parent style='width: 100px'>
              <div style='float: left; width: 10px; height: 1px'></div>
              <div style='clear: left'></div>
              <div style='margin-top: 99px'></div>
            </div>
            <div id=next style='height: 10px; margin-top: 10px'></div>
            """);
        Assert.Equal(new PhysicalRect(0, 0, 100, 100), GetRect(document, "parent"));
        Assert.Equal(110, GetRect(document, "next").Y);
    }

    [Fact]
    public void ShapeOutside_InsetAndPath()
    {
        var document = Parse("""
            <div style='display: flow-root; width: 200px'>
              <div style='float: left; width: 100px; height: 20px; shape-outside: inset(0 80px 0 0)'></div>
              <span id=a>X</span>
            </div>
            <div style='display: flow-root; width: 200px'>
              <div style='float: left; width: 100px; height: 20px; shape-outside: path("M 0 0 L 40 0 L 40 20 L 0 20 Z")'></div>
              <span id=b>X</span>
            </div>
            <div style='display: flow-root; width: 200px'>
              <div style='float: left; width: 100px; height: 100px; shape-outside: circle(50px at 0 50px)'></div>
              <span id=c>X</span>
            </div>
            """);
        Assert.Equal(20, GetRect(document, "a").X);
        Assert.Equal(40, GetRect(document, "b").X);
        Assert.Equal(Math.Sqrt((50 * 50) - (40 * 40)), GetRect(document, "c").X, 0.01);
    }

    [Fact]
    public void ExclusionSpace_PlaceFloat()
    {
        var space = new ExclusionSpace();
        Assert.Equal(new LogicalOffset(0, 5), space.PlaceFloat(FloatSide.LineLeft, new LogicalSize(50, 10), ceiling: 5, lineLeft: 0, inlineSize: 100));
        space.Add(FloatSide.LineLeft, new LogicalRect(new LogicalOffset(0, 5), new LogicalSize(50, 10)));

        // Rule 5: a float is not higher than the previous floats
        Assert.Equal(new LogicalOffset(70, 5), space.PlaceFloat(FloatSide.LineRight, new LogicalSize(30, 10), ceiling: 0, lineLeft: 0, inlineSize: 100));

        // Too wide next to the first float: below it
        Assert.Equal(new LogicalOffset(0, 15), space.PlaceFloat(FloatSide.LineLeft, new LogicalSize(60, 10), ceiling: 0, lineLeft: 0, inlineSize: 100));

        var opportunity = space.GetOpportunity(10, 0, 0, 100, useFloatAreas: false);
        Assert.True(opportunity.IsShortened);
        Assert.Equal((50d, 100d, 15d), (opportunity.LineLeft, opportunity.LineRight, opportunity.NextBlockOffset));
        Assert.False(space.GetOpportunity(15, 10, 0, 100, useFloatAreas: false).IsShortened);
    }

    [Fact]
    public void BlockFormattingContextRoot_MarginLargerThanTheSpaceBesideTheFloat_MovesBelow()
    {
        // The border box would start in the float: the box is moved below it
        var document = Parse("""
            <div style='width: 100px'>
              <div style='float: right; width: 50px; height: 40px'></div>
              <div id=a style='display: flow-root; height: 60px; margin-left: 51px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(51, 40, 49, 60), GetRect(document, "a"));
    }

    [Fact]
    public void BlockFormattingContextRoot_EndMarginOverflowsBesideTheFloat()
    {
        // The margin of the end side does not make the box overlap the float: it overflows the containing block
        var document = Parse("""
            <div style='width: 100px'>
              <div style='float: left; width: 50px; height: 100px'></div>
              <div id=a style='overflow: hidden; margin-right: 1px; width: 50px; height: 100px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(50, 0, 50, 100), GetRect(document, "a"));
    }

    [Fact]
    public void BlockFormattingContextRoot_NegativeMarginBesideAFloat_MovesBelow()
    {
        // The negative margin makes the border box at least 30px wide: it does not fit in the 16px beside the float
        var document = Parse("""
            <div style='width: 30px'>
              <div style='float: left; width: 14px; height: 10px'></div>
              <div id=a style='display: flow-root; height: 15px; margin-left: -30px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(-30, 10, 60, 15), GetRect(document, "a"));
    }

    [Fact]
    public void BlockFormattingContextRoot_ZeroWidthFloats_DoNotNarrowTheSpace()
    {
        var document = Parse("""
            <div style='margin-left: 50px; width: 100px'>
              <div style='float: left; width: 0; height: 50px'></div>
              <div style='float: right; clear: left; width: 0; height: 150px'></div>
              <div id=a style='overflow: hidden; margin: 0 -50px; height: 100px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(0, 0, 200, 100), GetRect(document, "a"));
    }

    [Fact]
    public void BlockFormattingContextRoot_RightToLeft_StartsAtTheInlineStartEdgeOfTheSpace()
    {
        var document = Parse("""
            <div style='width: 100px; direction: rtl'>
              <div style='float: left; width: 50px; height: 40px'></div>
              <div id=a style='display: flow-root; height: 60px; margin-right: 51px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(0, 40, 49, 60), GetRect(document, "a"));
    }

    [Fact]
    public void Float_WiderThanItsContainingBlock_NotNarrowedByAFloatOutsideOfIt()
    {
        // Rule 3: the right float is outside of the containing block of the left float, which does not overlap it
        var document = Parse("""
            <div style='width: 500px'>
              <div style='float: right; width: 50px; height: 300px'></div>
              <div style='margin-right: 100px'><div id=a style='float: left; width: 425px; height: 10px'></div></div>
            </div>
            <div style='clear: both; width: 500px'>
              <div style='float: left; width: 50px; height: 300px'></div>
              <div style='margin-left: 100px'><div id=b style='float: left; width: 425px; height: 10px'></div></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(0, 0, 425, 10), GetRect(document, "a"));

        // Rule 7: a left float that has another left float before it does not stick out of its containing block
        Assert.Equal(new PhysicalRect(100, 600, 425, 10), GetRect(document, "b"));
    }

    [Fact]
    public void Float_AfterAFloatThatDoesNotFitOnTheLine_IsNotPlacedAboveIt()
    {
        // Rule 5: the first float is placed below the line, the next ones are not placed before it
        var document = Parse("""
            <div style='width: 100px'>H<div id=a style='float: left; width: 100px; height: 100px'></div><div id=b style='float: left; width: 30px; height: 30px'></div><div id=c style='float: right; width: 30px; height: 30px'></div></div>
            """);
        Assert.Equal(new PhysicalRect(0, 10, 100, 100), GetRect(document, "a"));
        Assert.Equal(new PhysicalRect(0, 110, 30, 30), GetRect(document, "b"));
        Assert.Equal(new PhysicalRect(70, 110, 30, 30), GetRect(document, "c"));
    }

    [Fact]
    public void Floats_SumOfFloatingPointSizes_FitInTheContainingBlock()
    {
        var floats = string.Concat(Enumerable.Repeat("<span style='float: left; width: 0.87em; height: 0.87em'></span>", 9));
        var document = Load("<!DOCTYPE html><style>body { margin: 0; font-size: 16px }</style><div style='width: 8.7em'>" + floats + "<span id=last style='float: left; width: 0.87em; height: 0.87em'></span></div>");
        Assert.Equal(0, GetRect(document, "last").Y);
    }
}
