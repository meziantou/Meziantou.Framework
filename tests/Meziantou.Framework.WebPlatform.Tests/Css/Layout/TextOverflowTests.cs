using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The rendering of text-overflow (https://drafts.csswg.org/css-overflow-4/#text-overflow): the truncation of the lines and their ellipses, checked with the dumps of the display lists.</summary>
public sealed class TextOverflowTests : IDisposable
{
    private const string Ellipsis = "…";

    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem } .t { width: 55px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Returns the lines of the dump of the display list that draw glyphs or clip, without the indentation.</summary>
    private static List<string> GetDrawings(Document document)
        => [.. document.GetDisplayList()!.Dump().Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("draw-glyphs", StringComparison.Ordinal) || line.StartsWith("draw-rect", StringComparison.Ordinal) || line.StartsWith("clip-rect", StringComparison.Ordinal))];

    private static LineBoxFragment GetFirstLine(Document document, string id)
        => document.GetLayoutEngine().GetFragments(document.GetElementById(id)!)[0].Children.OfType<LineBoxFragment>().First();

    [Fact]
    public void Ellipsis_ReplacesTheOverflowingCharacters()
    {
        // The clusters that end after 55 - 10 = 45px are hidden: the text is clipped at 40px, the ellipsis is next to it
        var document = Parse("<div class=t>abcdefghij</div>");
        Assert.Equal(
            [
                "clip-rect (0, 0, 55x10)",
                "clip-rect (-10000000, -10000000, 10000040x20000000)",
                "draw-glyphs \"abcdefghij\" Ahem 10px at (0, 8) glyphs=10 fill #000000  [#text]",
                "draw-glyphs \"" + Ellipsis + "\" Ahem 10px at (40, 8) glyphs=1 fill #000000  [div]",
            ],
            GetDrawings(document));
    }

    [Theory]
    [InlineData("<div class=t>abcde</div>")]
    [InlineData("<div class=t style='overflow: visible'>abcdefghij</div>")]
    [InlineData("<div class=t style='text-overflow: clip'>abcdefghij</div>")]
    [InlineData("<div class=t style='text-overflow: fade'>abcdefghij</div>")]
    [InlineData("<div class=t style='overflow-x: visible; overflow-y: clip'>abcdefghij</div>")]
    [InlineData("<div class=t style='display: flex'>abcdefghij</div>")]
    [InlineData("<div class=t><p style='margin: 0'>abcdefghij</p></div>")]
    public void NoEllipsis(string html)
    {
        var document = Parse(html);
        Assert.DoesNotContain(GetDrawings(document), line => line.Contains(Ellipsis, StringComparison.Ordinal));
    }

    [Fact]
    public void AnonymousBlock_UsesTheTextOverflowOfItsParent()
    {
        var document = Parse("<div class=t>abcdefghij<div>block</div></div>");
        Assert.Contains(GetDrawings(document), line => line.StartsWith("draw-glyphs \"" + Ellipsis + "\" Ahem 10px at (40, 8) glyphs=1 fill #000000", StringComparison.Ordinal));
    }

    [Fact]
    public void String_AndTheFirstCharacterIsClipped()
    {
        // "The first character or atomic inline-level element on a line must be clipped rather than ellipsed": the string is
        // placed after it (and clipped by the overflow clip)
        var document = Parse("<div class=t style='text-overflow: \"[...]\"'>abcdefghij</div>");
        Assert.Contains("draw-glyphs \"[...]\" Ahem 10px at (10, 8) glyphs=5 fill #000000  [div]", GetDrawings(document));
        Assert.Contains("clip-rect (-10000000, -10000000, 10000010x20000000)", GetDrawings(document));
    }

    [Fact]
    public void String_SegmentBreaksCollapse()
    {
        // "a\A\A b" is "a", two line feeds and "b" (the space ends the escape): the line feeds collapse to one space
        var document = Parse("<div class=t style='width: 85px; text-overflow: \"a\\A\\A b\"'>abcdefghij</div>");
        Assert.Contains("draw-glyphs \"a b\" Ahem 10px at (50, 8) glyphs=3 fill #000000  [div]", GetDrawings(document));
    }

    [Fact]
    public void AtomicInline_OverlappingTheEllipsisIsHidden()
    {
        var document = Parse("<div class=t>abc<span id=ib style='display: inline-block; width: 20px; height: 10px; background: rgb(255, 0, 0)'></span>defg</div>");
        var drawings = GetDrawings(document);
        Assert.DoesNotContain(drawings, line => line.Contains("#ff0000", StringComparison.Ordinal));
        Assert.Contains("draw-glyphs \"" + Ellipsis + "\" Ahem 10px at (30, 10) glyphs=1 fill #000000  [div]", drawings);
    }

    [Fact]
    public void RightToLeft_EllipsisAtTheLineLeftEdge()
    {
        var document = Parse("<div class=t dir=rtl>abcdefghij</div>");
        var drawings = GetDrawings(document);
        Assert.Contains("draw-glyphs \"" + Ellipsis + "\" Ahem 10px at (5, 8) glyphs=1 fill #000000  [div]", drawings);
        Assert.Contains("clip-rect (15, -10000000, 9999985x20000000)", drawings);
    }

    [Fact]
    public void TwoValues_EllipsesAtBothEdgesOfTheScrolledLine()
    {
        var document = Parse("<div class=t id=d style='text-overflow: \"[\" \"]\"'>abcdefghijklmnop</div>");
        var element = document.GetElementById("d")!;
        element.ScrollLeft = 50;
        var drawings = GetDrawings(document);

        // The visible part of the line is [50, 105]: the characters before 60 and after 95 are hidden
        Assert.Equal(
            [
                "clip-rect (0, 0, 55x10)",
                "clip-rect (10, -10000000, 30x20000000)",
                "draw-glyphs \"abcdefghijklmnop\" Ahem 10px at (-50, 8) glyphs=16 fill #000000  [#text]",
                "draw-glyphs \"[\" Ahem 10px at (0, 8) glyphs=1 fill #000000  [div#d]",
                "draw-glyphs \"]\" Ahem 10px at (40, 8) glyphs=1 fill #000000  [div#d]",
            ],
            drawings);
    }

    [Fact]
    public void Scrolling_MovesTheEllipsis()
    {
        var document = Parse("<div class=t id=d>abcdefghij</div>");
        document.GetElementById("d")!.ScrollLeft = 45;
        Assert.DoesNotContain(GetDrawings(document), line => line.Contains(Ellipsis, StringComparison.Ordinal));
    }

    [Fact]
    public void Geometry_IsNotChanged()
    {
        var document = Parse("<div class=t id=d><span id=s>abcdefghij</span></div>");
        _ = document.GetDisplayList();
        var span = document.GetElementById("s")!;
        Assert.Equal(100, span.GetBoundingClientRect().Width);
        Assert.Equal(100, document.GetElementById("d")!.ScrollWidth);
        Assert.Same(span, document.ElementFromPoint(50, 5));
    }

    [Fact]
    public void FirstLine_EllipsisHasTheFirstLineStyle()
    {
        var document = Parse("<style>.t::first-line { color: rgb(0, 128, 0) }</style><div class=t style='white-space: normal'>abcdefghij<br>abcdefghij</div>");
        var drawings = GetDrawings(document);
        Assert.Contains("draw-glyphs \"abcdefghij\" Ahem 10px at (0, 8) glyphs=10 fill #008000  [#text]", drawings);
        Assert.Contains("draw-glyphs \"" + Ellipsis + "\" Ahem 10px at (40, 8) glyphs=1 fill #008000  [div]", drawings);
        Assert.Contains("draw-glyphs \"" + Ellipsis + "\" Ahem 10px at (40, 18) glyphs=1 fill #000000  [div]", drawings);
    }

    [Fact]
    public void Editable_WithTheCaretIsClipped()
    {
        var document = Parse("<div class=t id=d contenteditable>abcdefghij</div>");
        var element = document.GetElementById("d")!;
        document.GetSelection()!.Collapse(element.FirstChild, 2);
        Assert.DoesNotContain(GetDrawings(document), line => line.Contains(Ellipsis, StringComparison.Ordinal));
    }

    [Fact]
    public void TextField_Ellipsis()
    {
        var document = Parse("<input style='width: 55px; padding: 0; border: 0; font: 10px/1 Ahem; text-overflow: ellipsis' value=abcdefghij>");
        var drawings = GetDrawings(document);

        // The line of the inner editor is truncated as the lines of the block containers: the text is clipped before the ellipsis
        Assert.Contains("clip-rect (-10000000, -10000000, 10000040x20000000)", drawings);
        Assert.Contains(drawings, line => line.StartsWith("draw-glyphs \"abcdefghij\" Ahem 10px at (0, ", StringComparison.Ordinal));
        Assert.Contains(drawings, line => line.StartsWith("draw-glyphs \"" + Ellipsis + "\" Ahem 10px at (40, ", StringComparison.Ordinal));
    }

    [Fact]
    public void Truncate_AtAnEdgeOfTheLayout()
    {
        // The component used for an ellipsis that is not placed by text-overflow (e.g. block-ellipsis): truncation at an edge
        var document = Parse("<div id=d style='width: 100px'>abcdefghij</div>");
        var line = GetFirstLine(document, "d");
        var ellipsis = LineEllipsis.Create(line, "..");
        Assert.Equal(20, ellipsis.InlineSize);
        var truncation = LineTruncator.Truncate(line, 0, 60, leftEllipsis: null, ellipsis)!;
        Assert.Equal(40, truncation.ClipRight);
        Assert.Same(ellipsis, truncation.RightEllipsis);
        Assert.Equal(40, ellipsis.LineLeft);
        Assert.Equal(new PhysicalOffset(40, 0), ellipsis.Fragments[0].Offset);
        Assert.Same(line, ellipsis.Fragments[0].Parent);
        Assert.Null(LineTruncator.Truncate(line, 0, 100, leftEllipsis: null, LineEllipsis.Create(line, "..")));
    }
}
