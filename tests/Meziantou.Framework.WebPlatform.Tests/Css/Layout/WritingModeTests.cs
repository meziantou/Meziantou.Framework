using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The writing modes in the layout (vertical lines, baselines, orthogonal flows, principal writing mode), measured with Ahem (1em glyphs, ascent 0.8em).</summary>
public sealed class WritingModeTests : IDisposable
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

    [Theory]
    [InlineData(WritingMode.HorizontalTb, PhysicalSide.Top, false)]
    [InlineData(WritingMode.VerticalRl, PhysicalSide.Right, false)]
    [InlineData(WritingMode.VerticalLr, PhysicalSide.Right, true)]
    [InlineData(WritingMode.SidewaysRl, PhysicalSide.Right, false)]
    [InlineData(WritingMode.SidewaysLr, PhysicalSide.Left, false)]
    internal void LineOver(WritingMode writingMode, PhysicalSide lineOver, bool isLineOverBlockEnd)
    {
        var writingDirection = new WritingDirection(writingMode, Direction.Ltr);
        Assert.Equal(lineOver, writingDirection.LineOver);
        Assert.Equal(WritingDirection.Opposite(lineOver), writingDirection.LineUnder);
        Assert.Equal(isLineOverBlockEnd, writingDirection.IsLineOverBlockEnd);
    }

    [Theory]
    [InlineData("vertical-rl", 2)]
    [InlineData("vertical-lr", 2)]
    [InlineData("sideways-lr", 8)]
    public void AlphabeticBaseline_AscentOnTheLineOverSide(string writingMode, double x)
    {
        // A 10px text and a 20px text on the alphabetic baseline: the line is 20px wide (ascent 16, descent 4), the baseline is 16px
        // from the line-over side (the right side in both vertical modes, the left side in sideways-lr), the 10px text is 8px from it
        var document = Parse($"<div id=d style='writing-mode: {writingMode}; text-orientation: sideways; height: 100px'>XX <span style='font-size: 20px'>X</span></div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(20, GetRect(document, "d").Width);
        Assert.Equal(x, texts[0].GetRectInRoot().X);
        Assert.Equal(10, texts[0].GetRectInRoot().Width);
    }

    [Fact]
    public void CentralBaseline_VerticalText()
    {
        // The 10px text and the 20px text are centered on the central baseline; an image without baseline is centered too
        var document = Parse("<div id=d style='writing-mode: vertical-lr; height: 100px'>XX <span style='font-size: 20px'>X</span><img id=i style='width: 6px; height: 6px'></div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(20, GetRect(document, "d").Width);
        Assert.Equal(5, texts[0].GetRectInRoot().X);
        Assert.Equal(7, GetRect(document, "i").X);

        // The baseline of the block container is the central baseline of its first line
        var fragment = document.GetLayoutEngine().GetFragments(document.GetElementById("d")!)[0];
        Assert.Equal(10, fragment.FirstBaseline);
    }

    [Fact]
    public void CentralBaseline_InlineBlockInVerticalLine()
    {
        // The vertical-rl inline-block (two 10px lines) is aligned by the central baseline of its first line (its right line)
        var document = Parse("<div style='writing-mode: vertical-lr; height: 100px'>X<div id=b style='display: inline-block; writing-mode: vertical-rl'>X<br>X</div></div>");
        var rect = GetRect(document, "b");
        Assert.Equal(new PhysicalRect(0, 10, 20, 10), rect);
    }

    [Fact]
    public void TextCombineUpright_OneEmComposition()
    {
        var document = Parse("<div style='writing-mode: vertical-rl; height: 100px'>A<span id=s style='text-combine-upright: all'>1234</span>B</div>");
        var text = Assert.Single(GetTextFragments(document, "s"));
        Assert.Equal(new PhysicalRect(0, 10, 10, 10), text.GetRectInRoot());
        Assert.True(text.ShapedText!.IsCombinedUpright);
        Assert.Equal(0.25, text.ShapedText.CombinedScale);
        Assert.False(text.ShapedText.IsVertical);
    }

    [Fact]
    public void TextCombineUpright_NoEffectInHorizontalText()
    {
        var document = Parse("<div>A<span id=s style='text-combine-upright: all'>1234</span>B</div>");
        var text = Assert.Single(GetTextFragments(document, "s"));
        Assert.Equal(40, text.GetRectInRoot().Width);
        Assert.False(text.ShapedText!.IsCombinedUpright);
    }

    [Fact]
    public void PrincipalWritingMode_PropagatedFromBody()
    {
        // The root element is laid out in the writing mode of the body: its block size (its width) is the one of its contents,
        // at the right of the initial containing block
        var document = Load("<!DOCTYPE html><html id=h><body style='writing-mode: vertical-rl; margin: 0; font: 10px/1 Ahem'>X</body></html>");
        Assert.Equal(new PhysicalRect(790, 0, 10, 600), GetRect(document, "h"));

        // The computed value of the root element does not change
        Assert.Equal("horizontal-tb", document.DefaultView!.GetComputedStyle(document.DocumentElement!).WritingMode);
    }

    [Fact]
    public void OrthogonalFlow_AvailableInlineSizeFromMaxBlockSize()
    {
        // The available inline space of the orthogonal child of a box with an auto height is its max-height (else the 600px of the ICB)
        var document = Parse("<div style='max-height: 50px'><div id=o style='writing-mode: vertical-rl'>XXX XXX XXX</div></div><div><div id=p style='writing-mode: vertical-rl'>XXX XXX XXX</div></div>");
        Assert.Equal(new PhysicalSize(30, 50), GetRect(document, "o").Size);
        Assert.Equal(new PhysicalSize(10, 110), GetRect(document, "p").Size);
    }

    [Fact]
    public void InlineWithAnotherWritingMode_ComputesToInlineBlock()
    {
        var document = Parse("<div><span id=s style='writing-mode: vertical-rl'>X</span><span id=t>X</span></div>");
        var window = document.DefaultView!;
        Assert.Equal("inline-block", window.GetComputedStyle(document.GetElementById("s")!).Display);
        Assert.Equal("inline", window.GetComputedStyle(document.GetElementById("t")!).Display);
    }

    [Theory]
    [InlineData("writing-mode: tb", "vertical-rl")]
    [InlineData("writing-mode: tb-rl", "vertical-rl")]
    [InlineData("writing-mode: lr-tb", "horizontal-tb")]
    [InlineData("text-orientation: sideways-right", "sideways")]
    [InlineData("text-combine-upright: digits", "digits 2")]
    [InlineData("text-combine-upright: digits calc(2 + 3)", "none")]
    public void LegacyValues_Computed(string declaration, string expected)
    {
        var document = Parse($"<div id=d style='{declaration}'></div>");
        var style = document.DefaultView!.GetComputedStyle(document.GetElementById("d")!);
        var property = declaration[..declaration.IndexOf(':', StringComparison.Ordinal)];
        Assert.Equal(expected, style.GetPropertyValue(property));
    }

    [Fact]
    public void RootAnonymousBox_UsesThePrincipalWritingMode()
    {
        // WPT css/css-writing-modes/wm-propagation-001.html: the text of the root element is in an anonymous block box that
        // inherits the used writing mode of the root (propagated from the body)
        var document = Parse("<style>html { font: 10px/1 Ahem } body { writing-mode: vertical-rl; width: 0; height: 0 }</style>");
        document.DocumentElement!.AppendChild(document.CreateTextNode("XX"));
        var text = (Text)document.DocumentElement.LastChild!;
        var fragment = Assert.Single(document.GetLayoutEngine().GetTextFragments(text));
        Assert.Equal(10, fragment.GetRectInRoot().Width);
        Assert.Equal(20, fragment.GetRectInRoot().Height);
    }

    [Fact]
    public void OrthogonalFlow_PercentageInlineSize_ResolvesAgainstTheFallbackSize()
    {
        // The block size of the containing block is indefinite: the percentage resolves against the fallback of the available
        // inline space (the 600px of the initial containing block, or the max-height of the containing block)
        var document = Parse("<div><div id=o style='writing-mode: vertical-rl; height: 50%'>X</div></div><div style='max-height: 100px'><div id=p style='writing-mode: vertical-rl; height: 50%'>X</div></div>");
        Assert.Equal(new PhysicalSize(10, 300), GetRect(document, "o").Size);
        Assert.Equal(new PhysicalSize(10, 50), GetRect(document, "p").Size);
    }

    [Theory]
    [InlineData("height: 80px", 80)]
    [InlineData("height: 40px; min-height: 80px", 80)]
    [InlineData("max-height: 80px", 80)]
    [InlineData("height: 100px; padding: 10px 0; box-sizing: border-box", 80)]
    [InlineData("min-height: 80px", 150)]
    public void OrthogonalFlow_AvailableInlineSizeFromTheNearestScrollport(string scrollerStyle, double expected)
    {
        // The parent of the orthogonal flow has an auto height: the fixed inner size of the nearest scroll container (capped by its
        // max size and floored by its min size) is the available inline space, else the initial containing block is
        var document = Parse($"<div style='overflow: hidden; {scrollerStyle}'><div><div id=o style='writing-mode: vertical-rl'>XXX XXX XXX XXX</div></div></div>");
        Assert.Equal(expected, GetRect(document, "o").Height);
    }

    [Fact]
    public void TextOrientationUpright_UsedDirectionIsLtr()
    {
        // The inline base direction of the upright vertical text is ltr, and the flow-relative properties are mapped with it;
        // text-orientation does not apply to the sideways writing modes
        var document = Parse("""
            <div dir=rtl style='writing-mode: vertical-rl; height: 100px'><span id=a>A</span><span>B</span></div>
            <div id=u dir=rtl style='writing-mode: vertical-rl; text-orientation: upright; height: 100px; border-inline-start: 3px solid'><span id=b>A</span><span>B</span></div>
            <div id=s dir=rtl style='writing-mode: sideways-rl; text-orientation: upright; height: 100px; border-inline-start: 3px solid'><span id=c>A</span><span>B</span></div>
            """);
        // (the left-to-right run "AB" is at the end of the right-to-left lines: at their top, less the border, when the base direction is ltr)
        Assert.Equal(80, GetRect(document, "a").Y);
        Assert.Equal(3, GetRect(document, "b").Y - GetRect(document, "u").Y);
        Assert.Equal(80, GetRect(document, "c").Y - GetRect(document, "s").Y);
        var window = document.DefaultView!;
        Assert.Equal("3px", window.GetComputedStyle(document.GetElementById("u")!).BorderTopWidth);
        Assert.Equal("3px", window.GetComputedStyle(document.GetElementById("s")!).BorderBottomWidth);
        Assert.Equal("rtl", window.GetComputedStyle(document.GetElementById("u")!).Direction);
    }

    [Fact]
    public void TextCombineUpright_Digits()
    {
        // The sequences of at most 2 digits are compositions of 1em; the other characters are typeset vertically
        var document = Parse("<div style='writing-mode: vertical-rl; height: 100px'><span id=s style='text-combine-upright: digits 2'>A12B345</span></div>");
        var texts = GetTextFragments(document, "s");
        Assert.Equal(3, texts.Count);
        Assert.False(texts[0].ShapedText!.IsCombinedUpright);
        Assert.True(texts[1].ShapedText!.IsCombinedUpright);
        Assert.Equal(new PhysicalRect(0, 10, 10, 10), texts[1].GetRectInRoot());
        Assert.Equal(0.5, texts[1].ShapedText!.CombinedScale);
        Assert.False(texts[2].ShapedText!.IsCombinedUpright);
        Assert.Equal(new PhysicalRect(0, 20, 10, 40), texts[2].GetRectInRoot());
    }

    [Fact]
    public void TextCombineUpright_NoEffectInSidewaysWritingModes()
    {
        var document = Parse("<div style='writing-mode: sideways-rl; height: 100px'>A<span id=s style='text-combine-upright: all'>1234</span>B</div>");
        var text = Assert.Single(GetTextFragments(document, "s"));
        Assert.False(text.ShapedText!.IsCombinedUpright);
        Assert.Equal(40, text.GetRectInRoot().Height);
    }

    [Fact]
    public void TextCombineUpright_WhiteSpaceAndLetterSpacing()
    {
        // The white space at the start and at the end of the composition is removed, its forced line breaks and its letter
        // spacing are ignored: "1 2" is scaled to 1em; a composition of white space is an empty square of 1em
        var document = Parse("""
            <div style='writing-mode: vertical-rl; height: 100px'>A<span id=s style='text-combine-upright: all; white-space: pre-line; letter-spacing: 5px'>  1
            2  </span>B</div>
            <div id=d style='writing-mode: vertical-rl; display: inline-block'>A<span style='text-combine-upright: all'> </span></div>
            """);
        var text = Assert.Single(GetTextFragments(document, "s"));
        Assert.Equal(3, text.ShapedText!.Length);
        Assert.Equal(1 / 3d, text.ShapedText.CombinedScale, 6);
        Assert.Equal(20, GetRect(document, "d").Height);
    }

    [Fact]
    public void Marker_TextOrientationApplies()
    {
        var document = Parse("<style>li::marker { text-orientation: upright }</style><ol style='writing-mode: vertical-rl'><li id=l>X</li></ol>");
        Assert.Equal("upright", document.DefaultView!.GetComputedStyle(document.GetElementById("l")!, "::marker").TextOrientation);
    }

    [Fact]
    public void AnonymousBlock_UsesTheUnicodeBidiOfItsBlockContainer()
    {
        // unicode-bidi is not inherited: the text between the block-level children of a block container with an override is
        // in anonymous block boxes, which use the override of the block container
        var document = Parse("<div id=d style='direction: rtl; unicode-bidi: bidi-override; width: 100px'>AB<div id=c>CD</div></div>");
        var texts = GetTextFragments(document, "d");
        Assert.True(texts[0].ShapedText!.Options.IsRightToLeft);
        Assert.False(texts[1].ShapedText!.Options.IsRightToLeft);
    }

    [Fact]
    public void StaticPosition_EmptyRightToLeftBlock()
    {
        // The static position of an inline-level box in a right-to-left block without text is at its inline-start (right) edge
        var document = Parse("<div style='direction: rtl; width: 100px; position: relative'> <span id=a style='position: absolute; width: 10px; height: 10px'></span> </div>");
        Assert.Equal(90, GetRect(document, "a").X);
    }

    [Fact]
    public void VerticalTable_BorderSpacingAndOrthogonalCellAlignment()
    {
        // The first value of border-spacing is the spacing between the columns (the vertical axis of a vertical table), the
        // second one the spacing between the rows; the contents of an orthogonal cell are aligned in its own block axis
        var document = Parse("""
            <table style='writing-mode: vertical-lr; border-spacing: 4px 6px'>
              <tr><td id=a style='padding: 0; width: 10px; height: 10px'></td><td id=b style='padding: 0; width: 10px; height: 10px'></td></tr>
              <tr><td id=c style='padding: 0; width: 10px; height: 10px'></td>
                <td id=d style='padding: 0; writing-mode: horizontal-tb; height: 30px; vertical-align: bottom'><div id=e style='width: 10px; height: 10px'></div></td></tr>
            </table>
            """);
        Assert.Equal(new PhysicalRect(6, 4, 10, 10), GetRect(document, "a"));
        Assert.Equal(18, GetRect(document, "b").Y);
        Assert.Equal(22, GetRect(document, "c").X);
        Assert.Equal(new PhysicalRect(22, 18, 10, 30), GetRect(document, "d"));
        Assert.Equal(38, GetRect(document, "e").Y);
    }

    [Fact]
    public void VerticalTable_CollapsedBordersArePainted()
    {
        // The border between the two cells of the row (stacked vertically) is a horizontal rule centered on their common edge
        var document = Parse("""
            <table style='writing-mode: vertical-lr; border-collapse: collapse'>
              <tr><td style='padding: 0; width: 20px; height: 10px; border-bottom: 4px solid rgb(0, 128, 0)'></td><td style='padding: 0; width: 20px; height: 10px'></td></tr>
            </table>
            """);
        var rule = Assert.Single(document.GetDisplayList()!.Items.ToArray().OfType<Meziantou.Framework.WebPlatform.Rendering.DrawRectItem>());
        Assert.Equal(new PhysicalRect(0, 10, 20, 4), rule.Rect);
    }

    [Fact]
    public void FormControls_VerticalAndRightToLeft()
    {
        // The value of a right-to-left progress bar grows from the right; a vertical one is painted in its rotated content box
        var document = Parse("""
            <progress value=0.25 dir=rtl style='display: block; width: 100px; height: 10px'></progress>
            <progress value=0.25 style='display: block; writing-mode: vertical-rl; width: 10px; height: 100px'></progress>
            """);
        var items = document.GetDisplayList()!.Items.ToArray();
        var bars = items.OfType<Meziantou.Framework.WebPlatform.Rendering.DrawRectItem>().ToArray();
        Assert.Equal(new PhysicalRect(0, 0, 100, 10), bars[0].Rect);
        Assert.Equal(new PhysicalRect(75, 0, 25, 10), bars[1].Rect);

        // (the rotated box is at the origin: the value grows from the top of the control)
        Assert.Equal(new PhysicalRect(0, 0, 25, 10), bars[3].Rect);
        var transform = Assert.Single(items.OfType<Meziantou.Framework.WebPlatform.Rendering.TransformItem>());
        Assert.Equal(10, transform.Matrix.E, 6);
        Assert.Equal(10, transform.Matrix.F, 6);
        Assert.Equal(1, transform.Matrix.B, 6);
    }
}
