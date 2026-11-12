using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The ruby layout (https://drafts.csswg.org/css-ruby-1/#ruby-layout), measured with Ahem (1em glyphs, ascent 0.8em).</summary>
public sealed class RubyLayoutTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 20px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 20px/1 Ahem }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static PhysicalRect GetRect(Document document, string id) => document.GetLayoutEngine().GetBorderBoxRect(document.GetElementById(id)!) ?? throw new InvalidOperationException("No box");

    /// <summary>The text fragments of the text nodes of an element, in tree order.</summary>
    private static List<TextFragment> GetTextFragments(Document document, string id)
    {
        var result = new List<TextFragment>();
        foreach (var node in document.GetElementById(id)!.Descendants())
        {
            if (node is Text text)
            {
                result.AddRange(document.GetLayoutEngine().GetTextFragments(text));
            }
        }

        return result;
    }

    [Fact]
    public void Annotation_OverTheBase_ExtendsTheFirstLine()
    {
        // The column is as wide as the annotation (60px); ruby-align: space-around distributes the 20px of extra space of the base:
        // the Latin letters are not justification opportunities, so the base is centered. The annotation (10px) overflows the line
        // box (line-height: 1): the line is moved down by its overflow
        var document = Parse("<div id=d><ruby id=r>XX<rt id=t>XXXXXX</rt></ruby></div>");
        Assert.Equal(new PhysicalRect(0, 0, 60, 10), GetRect(document, "t"));
        Assert.Equal(new PhysicalRect(0, 10, 60, 20), GetRect(document, "r"));
        Assert.Equal(30, GetRect(document, "d").Height);
        Assert.Equal(new PhysicalRect(10, 10, 40, 20), GetTextFragments(document, "r")[0].GetRectInRoot());
    }

    [Fact]
    public void Annotation_SpaceAround_WordSeparatorsExpandOnBothSides()
    {
        // ruby-align: space-around distributes the 120px of extra space of the base "X X" to the boundaries around its word
        // separator (2 opportunities) and half an opportunity at each edge: 40px per opportunity, 20px at the edges
        var document = Parse("<div id=d><ruby id=r>X X<rt id=t>XXXXXXXXXXXXXXXXXX</rt></ruby></div>");
        Assert.Equal(new PhysicalRect(0, 0, 180, 10), GetRect(document, "t"));
        Assert.Equal(new PhysicalRect(20, 10, 140, 20), GetTextFragments(document, "r")[0].GetRectInRoot());
    }

    [Fact]
    public void Annotation_Under()
    {
        // The annotation box spans the slot of its base, its content is centered in it
        var document = Parse("<div id=d><ruby id=r style='ruby-position: under'>XX<rt id=t>X</rt></ruby></div>");
        Assert.Equal(new PhysicalRect(0, 20, 40, 10), GetRect(document, "t"));
        Assert.Equal(15, GetTextFragments(document, "t")[0].GetRectInRoot().X);
        Assert.Equal(new PhysicalRect(0, 0, 40, 20), GetRect(document, "r"));
        Assert.Equal(30, GetRect(document, "d").Height);
    }

    [Fact]
    public void Annotation_AlternatesBetweenLevels()
    {
        var document = Parse("<ruby id=r>X<rtc><rt id=t1>X</rt></rtc><rtc><rt id=t2>X</rt></rtc><rtc><rt id=t3>X</rt></rtc></ruby>");
        var ruby = GetRect(document, "r");
        Assert.Equal(ruby.Y - 10, GetRect(document, "t1").Y);
        Assert.Equal(ruby.Bottom, GetRect(document, "t2").Y);
        Assert.Equal(ruby.Y - 20, GetRect(document, "t3").Y);
    }

    [Fact]
    public void Annotation_UsesTheHalfLeadingOfThePreviousLine()
    {
        // The half-leading (5px) of the previous line leaves room for the annotation that overflows the second line by 5px
        var document = Parse("<div id=d style='line-height: 30px'>X<br><ruby id=r>X<rt id=t>X</rt></ruby></div>");
        Assert.Equal(25, GetRect(document, "t").Y);
        Assert.Equal(35, GetRect(document, "r").Y);
        Assert.Equal(60, GetRect(document, "d").Height);
    }

    [Fact]
    public void Pairing_AnnotationWithTheFirstBase()
    {
        var document = Parse("<ruby><rb id=b1>X</rb><rb id=b2>X</rb><rt id=t>Y</rt></ruby>");
        Assert.Equal(new PhysicalRect(0, 10, 20, 20), GetRect(document, "b1"));
        Assert.Equal(new PhysicalRect(20, 10, 20, 20), GetRect(document, "b2"));
        Assert.Equal(new PhysicalRect(0, 0, 20, 10), GetRect(document, "t"));
        Assert.Equal(5, GetTextFragments(document, "t")[0].GetRectInRoot().X);
    }

    [Fact]
    public void Pairing_SpanningAnnotation()
    {
        // The single anonymous annotation of an annotation container spans the bases, which share its extra width
        var document = Parse("<ruby id=r><rb id=b1>X</rb><rb id=b2>X</rb><rtc id=c>YYYYYYYY</rtc></ruby>");
        Assert.Equal(new PhysicalRect(0, 0, 80, 10), GetRect(document, "c"));
        Assert.Equal(new PhysicalRect(0, 10, 40, 20), GetRect(document, "b1"));
        Assert.Equal(new PhysicalRect(40, 10, 40, 20), GetRect(document, "b2"));
        Assert.Equal(10, GetTextFragments(document, "b1")[0].GetRectInRoot().X);
        Assert.Equal(50, GetTextFragments(document, "b2")[0].GetRectInRoot().X);
    }

    [Theory]
    [InlineData("start", 0)]
    [InlineData("center", 30)]
    [InlineData("space-around", 15)]
    public void RubyAlign(string align, double x)
    {
        var document = Parse($"<ruby style='ruby-align: {align}; text-justify: inter-character'>XXXX<rt id=t>XX</rt></ruby>");
        Assert.Equal(x, GetTextFragments(document, "t")[0].GetRectInRoot().X);
    }

    [Fact]
    public void RubyAlign_SpaceAround_JustifiesTheContent()
    {
        // 130px of extra space, 2 opportunities between the 3 characters (text-justify: inter-character): 130 / 3 between them, half
        // of it at the edges
        var document = Parse("<ruby>XXXXXXXX<rt id=t style='text-justify: inter-character'>XXX</rt></ruby>");
        var text = GetTextFragments(document, "t")[0];
        Assert.Equal(130 / 6d, text.GetRectInRoot().X, 6);
        Assert.Equal(30 + (2 * 130 / 3d), text.GetRectInRoot().Width, 6);
    }

    [Fact]
    public void LineBreaking_BetweenTheColumns()
    {
        var document = Parse("<div id=d style='width: 70px'><ruby id=r1>XX<rt>A</rt></ruby> <ruby id=r2>XX<rt>B</rt></ruby></div>");
        Assert.Equal(10, GetRect(document, "r1").Y);
        Assert.Equal(40, GetRect(document, "r2").Y);
        Assert.Equal(60, GetRect(document, "d").Height);
    }

    [Fact]
    public void LineBreaking_BeforeTheRubyContainer_MovesItsStartEdgesToTheNextLine()
    {
        // The last opportunity that fits is the one before the first column: the line ends before the ruby container, which starts
        // the next line with its padding (it has no fragment on the first line)
        var document = Parse("<div id=d style='width: 70px'>XX <ruby id=r style='padding-left: 10px'><rb>XX</rb><rb>XX</rb></ruby></div>");
        var fragment = Assert.Single(document.GetLayoutEngine().GetFragments(document.GetElementById("r")!));
        Assert.Equal(new PhysicalRect(0, 20, 90, 20), fragment.GetRectInRoot());
    }

    [Fact]
    public void LineBreaking_NotInsideAColumn()
    {
        var document = Parse("<div id=d style='width: 10px'><ruby>X X<rt>A</rt></ruby></div>");
        Assert.Equal(30, GetRect(document, "d").Height);
    }

    [Fact]
    public void IntrinsicSize_IsTheWidthOfTheColumns()
    {
        var document = Parse("<div id=d style='display: inline-block'><ruby>X<rt>AAAAAAAA</rt></ruby></div>");
        Assert.Equal(80, GetRect(document, "d").Width);
    }

    [Fact]
    public void Overhang_OverTheAdjacentText()
    {
        // The column (60px) is 40px wider than its base: it overhangs the text before and after it by half the font size of the
        // annotation (5px)
        var document = Parse("<div id=d style='display: inline-block'>X<ruby id=r>X<rt id=t>AAAAAA</rt></ruby>X</div>");
        Assert.Equal(90, GetRect(document, "d").Width);
        Assert.Equal(15, GetRect(document, "t").X);
    }

    [Fact]
    public void Overhang_SpacesOnly()
    {
        var document = Parse("<div id=d style='display: inline-block'>X<ruby style='ruby-overhang: spaces'>X<rt>AAAAAA</rt></ruby>X</div>");
        Assert.Equal(100, GetRect(document, "d").Width);
    }

    [Theory]
    [InlineData("", 110)]
    [InlineData("font-feature-settings: 'halt'", 120)]
    public void Overhang_SpacesOverTheBlankHalfOfABracket(string bracketStyle, double width)
    {
        // The column (60px) overhangs the blank half of the fullwidth closing bracket before it (10px), but when a font feature
        // that changes the widths of the glyphs is enabled
        var document = Parse($"<div id=d style='display: inline-block' lang=ja>X<span style=\"{bracketStyle}\">）</span><ruby style='ruby-overhang: spaces'>X<rt>AAAAAA</rt></ruby>X</div>");
        Assert.Equal(width, GetRect(document, "d").Width);
    }

    [Fact]
    public void BlockRuby()
    {
        var document = Parse("<ruby id=r style='display: block ruby'>X<rt id=t>A</rt></ruby>");
        Assert.Equal(new PhysicalRect(0, 0, 800, 30), GetRect(document, "r"));
        Assert.Equal(new PhysicalRect(0, 0, 20, 10), GetRect(document, "t"));
    }

    [Fact]
    public void Annotations_DoNotBreakLines()
    {
        // https://drafts.csswg.org/css-ruby-1/#white-space: the forced line breaks in the annotations are segment breaks
        var document = Parse("<div id=d><ruby style='white-space: pre'>X<rt>A\nB</rt></ruby></div>");
        Assert.Equal(30, GetRect(document, "d").Height);
    }

    [Fact]
    public void Annotation_UnderThenOver_DoNotOverlap()
    {
        // The second line is placed after the annotation under the first line, then moved down by its own annotation
        var document = Parse("<div id=d><ruby style='ruby-position: under'>X<rt id=t1>A</rt></ruby><br><ruby id=r2>X<rt id=t2>B</rt></ruby></div>");
        Assert.Equal(20, GetRect(document, "t1").Y);
        Assert.Equal(30, GetRect(document, "t2").Y);
        Assert.Equal(40, GetRect(document, "r2").Y);
        Assert.Equal(60, GetRect(document, "d").Height);
    }

    [Fact]
    public void Annotation_UnderTheLastLine_OverflowsIntoThePaddingNotIntoTheBorder()
    {
        var padding = Parse("<div id=d style='padding-bottom: 10px'><ruby style='ruby-position: under'>X<rt>A</rt></ruby></div>");
        Assert.Equal(30, GetRect(padding, "d").Height);

        var border = Parse("<div id=d style='border-bottom: 5px solid'><ruby style='ruby-position: under'>X<rt>A</rt></ruby></div>");
        Assert.Equal(35, GetRect(border, "d").Height);
    }

    [Fact]
    public void Autohide_AnnotationWithTheTextOfItsBase()
    {
        // https://drafts.csswg.org/css-ruby-1/#autohide: the second annotation is hidden, its column is as wide as its base
        var document = Parse("<ruby id=r><rb>X</rb><rb>YY</rb><rt id=t1>A</rt><rt id=t2>YY</rt></ruby>");
        Assert.Single(GetTextFragments(document, "t1"));
        Assert.Empty(GetTextFragments(document, "t2"));
        Assert.Equal(60, GetRect(document, "r").Width);
    }

    [Fact]
    public void Hiding_VisibilityCollapse()
    {
        var document = Parse("<ruby id=r>X<rt id=t style='visibility: collapse'>AAAAAA</rt></ruby>");
        Assert.Empty(GetTextFragments(document, "t"));
        Assert.Equal(20, GetRect(document, "r").Width);
    }

    [Fact]
    public void Pairing_IntraLevelWhiteSpace()
    {
        // The white space between the bases is paired with the white space between the annotations: the second annotation is
        // paired with the second base
        var document = Parse("<ruby><rb>X</rb> <rb id=b2>X</rb><rt>A</rt> <rt id=t2>B</rt></ruby>");
        Assert.Equal(40, GetRect(document, "b2").X);
        Assert.Equal(40, GetRect(document, "t2").X);
    }

    [Fact]
    public void LineBreaking_TheSpacesAtTheEndOfAColumnAreRemoved()
    {
        // The trailing space of the base of the first column is removed at the end of the line, where the column fits
        var document = Parse("<div style='width: 40px'><ruby id=r1>XX <rt>A</rt></ruby><ruby id=r2>X<rt>B</rt></ruby></div>");
        Assert.Equal(new PhysicalRect(0, 10, 40, 20), GetRect(document, "r1"));
        Assert.Equal(40, GetRect(document, "r2").Y);
    }

    [Fact]
    public void Bidi_BaseContainerOrdersItsColumnsByItsDirection()
    {
        // https://drafts.csswg.org/css-ruby-1/#bidi: a base container (rbc) is a bidi isolate: the columns of its segment are ordered
        // by its direction, and the annotations follow their bases; the segments are ordered by the direction of the ruby container
        var document = Parse("""
            <style>rb { margin: 0 5px }</style>
            <p style='margin: 0'><ruby><rb id=b1>A</rb><rb id=b2>B</rb><rt>a</rt><rt>b</rt><rbc dir=rtl><rb id=b3>C</rb><rb id=b4>D</rb></rbc><rt id=t3>c</rt><rt id=t4>d</rt></ruby></p>
            <p style='margin: 0'><ruby dir=rtl><rb id=b5>E</rb><rb id=b6>F</rb><rt>e</rt><rt>f</rt><rbc dir=ltr><rb id=b7>G</rb><rb id=b8>H</rb></rbc><rt>g</rt><rt>h</rt></ruby></p>
            """);
        Assert.Equal([5, 35, 95, 65], [GetRect(document, "b1").X, GetRect(document, "b2").X, GetRect(document, "b3").X, GetRect(document, "b4").X]);
        Assert.Equal(new PhysicalRect(90, 0, 30, 10), GetRect(document, "t3"));
        Assert.Equal(new PhysicalRect(60, 0, 30, 10), GetRect(document, "t4"));
        Assert.Equal([95, 65, 5, 35], [GetRect(document, "b5").X, GetRect(document, "b6").X, GetRect(document, "b7").X, GetRect(document, "b8").X]);
    }
}
