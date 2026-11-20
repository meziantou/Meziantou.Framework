using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The SVG layout: the text layout, the geometry of the shapes, the transforms, the foreignObject elements and the hit testing.</summary>
public sealed class SvgLayoutTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html) => Load("<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);

    private Document Load(string html, string url = "https://example.com/")
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            TimeProvider = _clock,
            Viewport = new ViewportOptions { Width = 800, Height = 600 },
        });

        var document = context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static T Get<T>(Document document, string id) where T : Element => (T)document.GetElementById(id)!;

    [Fact]
    public void Text_PositionsAndAdvances()
    {
        var document = Parse("<svg><text id=t x=10 y=50 style='font: 20px Ahem'>  ab<tspan id=s dx=5>c</tspan> d </text></svg>");
        var text = Get<SvgTextContentElement>(document, "t");
        var tspan = Get<SvgTextContentElement>(document, "s");

        // The leading and trailing spaces are collapsed: "ab", "c", " ", "d"
        Assert.Equal(5, text.GetNumberOfChars());
        Assert.Equal(1, tspan.GetNumberOfChars());
        Assert.Equal(100, text.GetComputedTextLength());
        Assert.Equal(10, text.GetStartPositionOfChar(0).X);
        Assert.Equal(50, text.GetStartPositionOfChar(0).Y);
        Assert.Equal(55, tspan.GetStartPositionOfChar(0).X);
        Assert.Equal(75, tspan.GetEndPositionOfChar(0).X);
        Assert.Equal(40, text.GetSubStringLength(1, 2));

        var extent = text.GetExtentOfChar(1);
        Assert.Equal((30d, 34d, 20d, 20d), (extent.X, extent.Y, extent.Width, extent.Height));
        Assert.Equal(1, text.GetCharNumAtPosition(new DomPointInit { X = 35, Y = 45 }));
        Assert.Equal(-1, text.GetCharNumAtPosition(new DomPointInit { X = 5, Y = 45 }));
        Assert.Throws<DomException>(() => text.GetStartPositionOfChar(5));

        var box = text.GetBBox();
        Assert.Equal((10d, 34d, 105d, 20d), (box.X, box.Y, box.Width, box.Height));
    }

    [Theory]
    [InlineData("start", 100)]
    [InlineData("middle", 80)]
    [InlineData("end", 60)]
    public void Text_Anchor(string anchor, double expectedX)
    {
        var document = Parse($"<svg><text id=t x=100 y=50 text-anchor={anchor} style='font: 20px Ahem'>ab</text></svg>");
        Assert.Equal(expectedX, Get<SvgTextContentElement>(document, "t").GetStartPositionOfChar(0).X);
    }

    [Fact]
    public void Text_TextLengthAndRotate()
    {
        var document = Parse("<svg><text id=a y=20 textLength=100 style='font: 10px Ahem'>abc</text><text id=b y=20 textLength=60 lengthAdjust=spacingAndGlyphs style='font: 10px Ahem'>abc</text><text id=c y=20 rotate='10 20' style='font: 10px Ahem'>abc</text></svg>");
        var spacing = Get<SvgTextContentElement>(document, "a");
        Assert.Equal(45, spacing.GetStartPositionOfChar(1).X);
        Assert.Equal(30, spacing.GetComputedTextLength());

        var glyphs = Get<SvgTextContentElement>(document, "b");
        Assert.Equal(20, glyphs.GetStartPositionOfChar(1).X);
        Assert.Equal(60, glyphs.GetComputedTextLength());

        var rotated = Get<SvgTextContentElement>(document, "c");
        Assert.Equal(10, rotated.GetRotationOfChar(0));
        Assert.Equal(20, rotated.GetRotationOfChar(2));
    }

    [Fact]
    public void Text_OnPath()
    {
        var document = Parse("<svg><path id=p d='M 10 10 L 110 10 L 110 110'/><text style='font: 10px Ahem'><textPath id=t href='#p' startOffset=95>abc</textPath></text></svg>");
        var textPath = Get<SvgTextContentElement>(document, "t");
        // The middle of "a" is at the distance 100 (110, 10), the middle of "b" at 110 (110, 20) on the vertical segment
        Assert.Equal(105, textPath.GetStartPositionOfChar(0).X, 6);
        Assert.Equal(0, textPath.GetRotationOfChar(0));
        Assert.Equal(90, textPath.GetRotationOfChar(1), 6);
        Assert.Equal(110, textPath.GetStartPositionOfChar(1).X, 6);
        Assert.Equal(15, textPath.GetStartPositionOfChar(1).Y, 6);
    }

    [Fact]
    public void Text_OnPathOnTheRightSide()
    {
        // side="right": the text is placed on the path reversed (from its end point), startOffset measured along it, and the
        // characters are rotated by 180 degrees
        var document = Parse("<svg><path id=p d='M 10 10 L 110 10'/><text style='font: 10px Ahem'><textPath id=t href='#p' side=right startOffset=20>ab</textPath></text></svg>");
        var textPath = Get<SvgTextContentElement>(document, "t");
        Assert.Equal(90, textPath.GetStartPositionOfChar(0).X, 6);
        Assert.Equal(80, textPath.GetStartPositionOfChar(1).X, 6);
        Assert.Equal(180, Math.Abs(textPath.GetRotationOfChar(0)), 6);
    }

    [Fact]
    public void Text_BidiReordering()
    {
        // "cd" is in a right-to-left override: it is displayed as "dc" between "ab" and "e" (the control codes are addressable
        // characters without advance); the position of a character of a right-to-left run is its right edge
        var document = Parse("<svg><text id=t x=100 y=50 style='font: 20px Ahem'>ab&#x202E;cd&#x202C;e</text></svg>");
        var text = Get<SvgTextContentElement>(document, "t");
        Assert.Equal(7, text.GetNumberOfChars());
        Assert.Equal(100, text.GetStartPositionOfChar(0).X);
        Assert.Equal(120, text.GetStartPositionOfChar(1).X);
        Assert.Equal(180, text.GetStartPositionOfChar(3).X);
        Assert.Equal(160, text.GetEndPositionOfChar(3).X);
        Assert.Equal(160, text.GetStartPositionOfChar(4).X);
        Assert.Equal(140, text.GetEndPositionOfChar(4).X);
        Assert.Equal(180, text.GetStartPositionOfChar(6).X);
        Assert.Equal(4, text.GetCharNumAtPosition(new DomPointInit { X = 145, Y = 45 }));
    }

    [Theory]
    [InlineData("", "start", 60)]
    [InlineData("", "end", 100)]
    [InlineData("unicode-bidi: bidi-override", "start", 100)]
    [InlineData("unicode-bidi: bidi-override", "middle", 120)]
    public void Text_RightToLeftParagraph(string style, string anchor, double expectedX)
    {
        // The start of a right-to-left paragraph is its right edge; its left-to-right letters progress to the right, unless
        // their direction is overridden
        var document = Parse($"<svg><text id=t x=100 y=50 direction=rtl text-anchor={anchor} style='font: 20px Ahem; {style}'>ab</text></svg>");
        Assert.Equal(expectedX, Get<SvgTextContentElement>(document, "t").GetStartPositionOfChar(0).X);
    }

    [Fact]
    public void Text_UnicodeBidiOfTextContentElement()
    {
        var document = Parse("<svg><text id=t x=100 y=50 style='font: 20px Ahem'>a<tspan style='unicode-bidi: bidi-override; direction: rtl'>bc</tspan>d</text></svg>");
        var text = Get<SvgTextContentElement>(document, "t");
        Assert.Equal(160, text.GetStartPositionOfChar(1).X);
        Assert.Equal(140, text.GetStartPositionOfChar(2).X);
        Assert.Equal(160, text.GetStartPositionOfChar(3).X);
    }

    [Theory]
    [InlineData("start", 100, 100)]
    [InlineData("middle", 70, 80)]
    [InlineData("end", 40, 60)]
    public void Text_InlineSizeWrapsTheLines(string anchor, double expectedSecondLineX, double expectedThirdLineX)
    {
        // "aaa", "bbb" and "cc" are on three lines of 30px (the spaces at the ends of the lines hang), each anchored at x; the
        // positioning attributes other than the first x and y are ignored
        var document = Parse($"<svg><text id=t x=100 y=50 text-anchor={anchor} style='font: 20px/30px Ahem; inline-size: 100px'>aaa <tspan x=0 dy=7>bbb</tspan> cc</text></svg>");
        var text = Get<SvgTextContentElement>(document, "t");
        Assert.Equal(10, text.GetNumberOfChars());
        Assert.Equal(50, text.GetStartPositionOfChar(0).Y);
        Assert.Equal(expectedSecondLineX, text.GetStartPositionOfChar(4).X);
        Assert.Equal(80, text.GetStartPositionOfChar(4).Y);
        Assert.Equal(expectedThirdLineX, text.GetStartPositionOfChar(8).X);
        Assert.Equal(110, text.GetStartPositionOfChar(8).Y);
    }

    [Fact]
    public void Text_InlineSizeLineBoxes()
    {
        // The second line is as tall as its tallest inline box (40px of line height for the 40px text: 32px above the baseline)
        var document = Parse("<svg><text id=t x=10 y=50 style='font: 20px/1 Ahem; inline-size: 100px'>aaaa <tspan style='font-size: 40px'>b</tspan> cccc</text></svg>");
        var text = Get<SvgTextContentElement>(document, "t");
        Assert.Equal(50 + 4 + 32, text.GetStartPositionOfChar(5).Y);
        Assert.Equal(50 + 4 + 40 + 16, text.GetStartPositionOfChar(7).Y);
    }

    [Fact]
    public void Text_InlineSizeInVerticalText()
    {
        // The lines of vertical-rl are stacked to the left; the forced line breaks of the preserved line feeds break the lines
        var document = Parse("<svg><text id=t x=100 y=50 style='font: 20px/30px Ahem; writing-mode: vertical-rl; inline-size: 100px'>aaa bbb</text><text id=u x=100 y=50 style='font: 20px/30px Ahem; inline-size: 500px; white-space: pre'>ab\ncd</text></svg>");
        var vertical = Get<SvgTextContentElement>(document, "t");
        Assert.Equal((100d, 50d), (vertical.GetStartPositionOfChar(0).X, vertical.GetStartPositionOfChar(0).Y));
        Assert.Equal((70d, 50d), (vertical.GetStartPositionOfChar(4).X, vertical.GetStartPositionOfChar(4).Y));

        var preformatted = Get<SvgTextContentElement>(document, "u");
        Assert.Equal((100d, 80d), (preformatted.GetStartPositionOfChar(3).X, preformatted.GetStartPositionOfChar(3).Y));
    }

    [Theory]
    [InlineData("start", 10, 10, 10)]
    [InlineData("center", 30, 20, 40)]
    [InlineData("end", 50, 30, 70)]
    public void Text_ShapeInsideWrapsTheLinesInTheShape(string align, double expectedFirstLineX, double expectedSecondLineX, double expectedThirdLineX)
    {
        // "aaa", "bbbb" and "cc" are on three lines of 30px stacked from the top of the rectangle (21px above the baseline),
        // each aligned in the rectangle by text-align; the x and y attributes are ignored
        var document = Parse($"<svg><rect id=r x=10 y=20 width=100 height=100 /><text id=t x=500 y=500 style='font: 20px/30px Ahem; shape-inside: url(#r); text-align: {align}'>aaa bbbb cc</text></svg>");
        var text = Get<SvgTextContentElement>(document, "t");
        Assert.Equal((expectedFirstLineX, 41d), (text.GetStartPositionOfChar(0).X, text.GetStartPositionOfChar(0).Y));
        Assert.Equal((expectedSecondLineX, 71d), (text.GetStartPositionOfChar(4).X, text.GetStartPositionOfChar(4).Y));
        Assert.Equal((expectedThirdLineX, 101d), (text.GetStartPositionOfChar(9).X, text.GetStartPositionOfChar(9).Y));
    }

    [Fact]
    public void Text_ShapeInsideFlowsIntoTheNextShapeAroundTheSubtractedShapes()
    {
        // The first rectangle holds one line; the text overflows into the second one, where the subtracted rectangle breaks the
        // first line into two segments; "eeeeeeee" does not fit in the shapes and is not rendered
        var document = Parse("""
            <svg>
              <rect id=a x=0 y=0 width=100 height=30 /><rect id=b x=200 y=0 width=100 height=60 /><rect id=hole x=240 y=0 width=20 height=30 />
              <text id=t style='font: 20px/30px Ahem; shape-inside: url(#a) url(#b); shape-subtract: url(#hole)'>aaaa bb cc dddd eeeeeeee</text>
            </svg>
            """);
        var text = Get<SvgTextContentElement>(document, "t");
        Assert.Equal((0d, 21d), (text.GetStartPositionOfChar(0).X, text.GetStartPositionOfChar(0).Y));
        Assert.Equal((200d, 21d), (text.GetStartPositionOfChar(5).X, text.GetStartPositionOfChar(5).Y));
        Assert.Equal((260d, 21d), (text.GetStartPositionOfChar(8).X, text.GetStartPositionOfChar(8).Y));
        Assert.Equal((200d, 51d), (text.GetStartPositionOfChar(11).X, text.GetStartPositionOfChar(11).Y));
        Assert.Equal(55, Get<SvgGraphicsElement>(document, "t").GetBBox().Height + Get<SvgGraphicsElement>(document, "t").GetBBox().Y);
    }

    [Fact]
    public void Text_ShapeInsidePaddingMarginAndJustification()
    {
        // The padding insets the polygon of the viewport (300x150) to [20, 280], the margin of the subtracted rectangle expands it to x=20; the
        // first line is justified: its space is widened so that the second "aa" ends at the end of the segment. The long word
        // fits nowhere, so it and the text after it are not rendered
        var document = Parse("""
            <svg>
              <rect id=hole x=0 y=0 width=10 height=200 style='shape-margin: 10px' />
              <text id=t style='font: 20px/30px Ahem; shape-inside: polygon(0 0, 100% 0, 100% 100%, 0 100%); shape-padding: 20px; shape-subtract: url(#hole); text-align: justify'>aa aa aaaaaaaaaaaaaaaaaa b</text>
              <text id=c style='font: 20px/30px Ahem; shape-inside: circle(50px at 100px 100px)'>aa</text>
            </svg>
            """);
        var text = Get<SvgTextContentElement>(document, "t");
        Assert.Equal((20d, 41d), (text.GetStartPositionOfChar(0).X, text.GetStartPositionOfChar(0).Y));
        Assert.Equal((240d, 41d), (text.GetStartPositionOfChar(3).X, text.GetStartPositionOfChar(3).Y));
        Assert.Equal(45, Get<SvgGraphicsElement>(document, "t").GetBBox().Height + Get<SvgGraphicsElement>(document, "t").GetBBox().Y, 6);

        // The circle: the first line box (40px wide, 30px tall) fits from y=54, where the chord at the middle of its top row
        // (y=54.5) is 41.5px wide, at the left end of that chord (the arcs of the circle are flattened into line segments)
        var circle = Get<SvgTextContentElement>(document, "c").GetStartPositionOfChar(0);
        Assert.Equal(75, circle.Y);
        Assert.Equal(100 - Math.Sqrt((50 * 50) - (45.5 * 45.5)), circle.X, 0.2);
    }

    [Fact]
    public void Shapes_LengthAndPointInFillAndStroke()
    {
        var document = Parse("<svg><rect id=r x=10 y=10 width=100 height=50 stroke-width=10 stroke-dasharray=100 /><circle id=c cx=50 cy=50 r=10 /><polygon id=p points='0,0 100,0 0,100' fill-rule=evenodd /></svg>");
        var rect = Get<SvgGeometryElement>(document, "r");
        Assert.Equal(300, rect.GetTotalLength());
        var point = rect.GetPointAtLength(120);
        Assert.Equal((110d, 30d), (point.X, point.Y));
        Assert.True(rect.IsPointInFill(new DomPointInit { X = 50, Y = 30 }));
        Assert.True(rect.IsPointInFill(new DomPointInit { X = 10, Y = 10 }));
        Assert.False(rect.IsPointInFill(new DomPointInit { X = 5, Y = 30 }));
        Assert.True(rect.IsPointInStroke(new DomPointInit { X = 50, Y = 7 }));

        // The dash pattern: the top edge is a dash, the right edge is in a gap
        Assert.False(rect.IsPointInStroke(new DomPointInit { X = 112, Y = 35 }));
        Assert.False(rect.IsPointInStroke(new DomPointInit { X = 50, Y = 30 }));

        var circle = Get<SvgGeometryElement>(document, "c");
        Assert.Equal(2 * Math.PI * 10, circle.GetTotalLength(), 3);
        Assert.True(circle.IsPointInFill(new DomPointInit { X = 55, Y = 55 }));
        Assert.False(circle.IsPointInFill(new DomPointInit { X = 59, Y = 59 }));

        var polygon = Get<SvgGeometryElement>(document, "p");
        Assert.True(polygon.IsPointInFill(new DomPointInit { X = 10, Y = 10 }));
        Assert.False(polygon.IsPointInFill(new DomPointInit { X = 60, Y = 60 }));
    }

    [Fact]
    public void Ctm_TransformsAndViewBox()
    {
        var document = Parse("<div style='padding: 10px 20px'><svg id=s width=200 height=100 viewBox='0 0 100 50' style='display: block'><g transform='translate(5 6)'><rect id=r transform='scale(2)' width=10 height=10 /></g></svg></div>");
        var rect = Get<SvgGraphicsElement>(document, "r");
        var ctm = rect.GetCtm()!;
        Assert.Equal((4d, 0d, 0d, 4d, 10d, 12d), (ctm.A, ctm.B, ctm.C, ctm.D, ctm.E, ctm.F));
        var screen = rect.GetScreenCtm()!;
        Assert.Equal((4d, 30d, 22d), (screen.A, screen.E, screen.F));

        var rects = rect.GetBoundingClientRect();
        Assert.Equal((30d, 22d, 40d, 40d), (rects.X, rects.Y, rects.Width, rects.Height));
    }

    [Fact]
    public void ForeignObject_LaidOutInUserSpace()
    {
        var document = Parse("<svg viewBox='0 0 150 75' width=300 height=150 style='display: block'><foreignObject x=10 y=20 width=50 height=40><div id=d style='height: 10px'></div></foreignObject></svg>");
        var div = Get<HtmlElement>(document, "d");
        var rect = div.GetBoundingClientRect();

        // The viewBox scales the user space by 2
        Assert.Equal((20d, 40d, 100d, 20d), (rect.X, rect.Y, rect.Width, rect.Height));
        Assert.Same(div, document.ElementFromPoint(50, 50));
    }

    [Fact]
    public void HitTesting_ShapesTextAndUse()
    {
        var document = Parse("<svg width=400 height=200 style='display: block'><defs><circle id=dc r=10 /></defs><rect id=r x=10 y=10 width=50 height=50 fill=none stroke=black stroke-width=4 /><circle id=c cx=100 cy=50 r=20 />"
            + "<text id=t x=200 y=50 style='font: 20px Ahem'>a<tspan id=s>b</tspan></text><use id=u href='#dc' x=300 y=50 /></svg>");
        Assert.Same(document.GetElementById("r"), document.ElementFromPoint(11, 30));
        Assert.Equal("svg", document.ElementFromPoint(35, 35)!.LocalName);
        Assert.Same(document.GetElementById("c"), document.ElementFromPoint(100, 65));
        Assert.Equal("svg", document.ElementFromPoint(117, 67)!.LocalName);
        Assert.Same(document.GetElementById("t"), document.ElementFromPoint(205, 45));
        Assert.Same(document.GetElementById("s"), document.ElementFromPoint(225, 45));
        Assert.Same(document.GetElementById("u"), document.ElementFromPoint(305, 50));
    }

    [Fact]
    public void SvgDocument_HitTestingAndCtm()
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/test.svg", _ => new DocumentResource("<svg xmlns='http://www.w3.org/2000/svg' width='600' height='600' viewBox='0 0 36 36'><circle id='c' cx='6' cy='10' r='5' stroke='blue' stroke-width='1' fill='none'/></svg>") { Headers = [new("Content-Type", "image/svg+xml")] });
        _contexts.EventLoop.RunUntilIdle();
        Assert.IsType<SvgSvgElement>(document.DocumentElement);
        var circle = Get<SvgGraphicsElement>(document, "c");
        Assert.Equal(600 / 36d, circle.GetScreenCtm()!.A, 6);
        Assert.Same(circle, document.ElementFromPoint(11 * 600 / 36d, 10 * 600 / 36d));
    }

    [Fact]
    public void ForeignObject_NestedOutermostSvgWithTransform()
    {
        var document = Parse("<svg width=300 height=300 style='display: block'><foreignObject width=100 height=100><svg id=s width=100 height=100 style='padding-right: 12px; transform: rotate(180deg)'><circle cx=50 r=44 /></svg></foreignObject></svg>");
        var svg = Get<SvgGraphicsElement>(document, "s");
        var ctm = svg.GetScreenCtm()!;
        Assert.Equal((-1d, 0d, 0d, -1d, 112d, 100d), (ctm.A, ctm.B, ctm.C, ctm.D, ctm.E, ctm.F));
    }

    [Fact]
    public void SvgLength_RelativeUnitsUseTheElement()
    {
        var document = Parse("<svg width=400 height=200><rect id=r x='10%' y='50%' width='2em' style='font-size: 20px' /></svg>");
        var rect = Get<SvgRectElement>(document, "r");
        Assert.Equal(40, rect.X.BaseVal.Value);
        Assert.Equal(100, rect.Y.BaseVal.Value);
        Assert.Equal(40, rect.Width.BaseVal.Value);
    }

    [Fact]
    public void SvgLength_SubstitutionFunctionOfAPresentationAttribute()
    {
        var document = Parse("<style>:root { --w: 100px }</style><svg width=400 height=200><rect id=r width='var(--w)' height=50 /></svg>");
        var rect = Get<SvgRectElement>(document, "r");
        var width = rect.Width.BaseVal;

        // https://github.com/w3c/svgwg/issues/1038: the value of a substitution is not resolved
        Assert.Equal(SvgLength.SvgLengthTypeUnknown, width.UnitType);
        Assert.Equal(0, width.Value);
        Assert.Equal(0, width.ValueInSpecifiedUnits);
        Assert.Equal("var(--w)", width.ValueAsString);
        Assert.Throws<DomException>(() => width.ConvertToSpecifiedUnits(SvgLength.SvgLengthTypeCm));
        Assert.Equal("var(--w)", rect.GetAttribute("width"));

        // The substitution is rendered: the rectangle is 100px wide
        Assert.Equal(100, rect.GetBoundingClientRect().Width);

        // Setting the value in user units replaces the substitution
        width.Value = 120;
        Assert.Equal(SvgLength.SvgLengthTypeNumber, width.UnitType);
        Assert.Equal("120", rect.GetAttribute("width"));

        // A standalone length accepts a substitution too, but not a function whose name ends with var
        var standalone = rect.OwnerSvgElement!.CreateSvgLength();
        standalone.ValueAsString = " var(--w) ";
        Assert.Equal(SvgLength.SvgLengthTypeUnknown, standalone.UnitType);
        Assert.Equal("var(--w)", standalone.ValueAsString);
        Assert.Throws<DomException>(() => standalone.ValueAsString = "my-var(--w)");
    }

    [Fact]
    public void SvgLength_PercentageOfADetachedLength()
    {
        var document = Parse("<svg id=s width=400 height=200></svg>");
        var length = Get<SvgSvgElement>(document, "s").CreateSvgLength();
        length.ValueAsString = "2px";
        length.ConvertToSpecifiedUnits(SvgLength.SvgLengthTypePercentage);

        // https://svgwg.org/svg2-draft/types.html#__svg__SVGLength__value: the percentages of a length without element are
        // relative to a viewport size of 100
        Assert.Equal("2%", length.ValueAsString);
        Assert.Equal(2, length.Value);
    }

    [Fact]
    public void IntersectionList_PaintedGeometry()
    {
        var document = Parse("<svg id=s width=400 height=200><rect id=a width=50 height=50 /><circle id=b cx=100 cy=100 r=10 /><path id=c d='M 0 100 L 100 200' fill=none stroke=black /></svg>");
        var svg = Get<SvgSvgElement>(document, "s");
        var rect = svg.CreateSvgRect();
        rect.X = 40;
        rect.Y = 40;
        rect.Width = 55;
        rect.Height = 55;
        var list = svg.GetIntersectionList(rect, referenceElement: null);
        Assert.Equal(["a", "b"], list.OfType<Element>().Select(e => e.Id));
        Assert.True(svg.CheckEnclosure(Get<SvgElement>(document, "a"), new DomRect(-1, -1, 60, 60)));
    }
}
