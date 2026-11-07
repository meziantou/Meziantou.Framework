using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The initial letters (https://drafts.csswg.org/css-inline-3/#initial-letter-styling), measured with Ahem: its glyphs are 1em
/// wide, its ascent and its cap height are 0.8em, its descent 0.2em. With a font size of 20px and a line height of 24px, the
/// baseline of a line is at 18px, and the initial letter of 3 lines has a font size of (2 x 24 + 16) / 0.8 = 80px.
/// </summary>
public sealed class InitialLetterTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 20px/24px Ahem } p { margin: 0; width: 230px }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static PhysicalRect GetRect(Document document, string id) => document.GetLayoutEngine().GetBorderBoxRect(document.GetElementById(id)!) ?? throw new InvalidOperationException("No box");

    /// <summary>The border box of the initial letter box of an element.</summary>
    private static PhysicalRect GetLetterRect(Document document, string id) => document.GetLayoutEngine().GetFragments(document.GetElementById(id)!, "first-letter")[0].GetRectInRoot();

    /// <summary>The rectangles of the text fragments of the text nodes of an element that are not in its initial letter, in tree order.</summary>
    private static List<PhysicalRect> GetTextRects(Document document, string id)
    {
        var result = new List<PhysicalRect>();
        foreach (var node in document.GetElementById(id)!.Descendants())
        {
            if (node is Text text)
            {
                foreach (var fragment in document.GetLayoutEngine().GetTextFragments(text))
                {
                    if (fragment.End > fragment.Start && fragment.Run.Parent is not { PseudoElement: "first-letter" } && fragment.Size.Width > 0)
                    {
                        result.Add(fragment.GetRectInRoot());
                    }
                }
            }
        }

        return result;
    }

    [Theory]
    [InlineData("normal", null, null)]
    [InlineData("3", 3d, 3)]
    [InlineData("2.5", 2.5, 2)]
    [InlineData("3 drop", 3d, 3)]
    [InlineData("3 raise", 3d, 1)]
    [InlineData("3 2", 3d, 2)]
    [InlineData("1.5 4", 1.5, 4)]
    public void Parse_SizeAndSink(string value, double? size, int? sink)
    {
        var result = InitialLetter.Parse(value);
        Assert.Equal(size, result?.Size);
        Assert.Equal(sink, result?.Sink);
    }

    [Fact]
    public void DroppedInitial_SpansTheLinesAndShortensThem()
    {
        var document = Parse("""
            <style>p::first-letter { initial-letter: 3; font-size: 100px; line-height: 50px }</style>
            <p id=p>Abc<br>def<br>ghi<br>jkl<br>mno</p>
            """);

        // The cap height of the letter spans from the cap height of the first line (2px) to the baseline of the third line
        // (66px); the box ends at the descent of the glyph (Ahem: the em box), so the fourth line is shortened too
        Assert.Equal(new PhysicalRect(0, 2, 80, 80), GetLetterRect(document, "p"));
        Assert.Equal([new(80, 2, 40, 20), new(80, 26, 60, 20), new(80, 50, 60, 20), new(80, 74, 60, 20), new(0, 98, 60, 20)], GetTextRects(document, "p"));
        Assert.Equal(120, GetRect(document, "p").Height);

        // The used font size is not the computed font size, and the box is not a float for the computed style
        var style = document.DefaultView!.GetComputedStyle(document.GetElementById("p")!, "::first-letter");
        Assert.Equal("100px", style.GetPropertyValue("font-size"));
        Assert.Equal("none", style.GetPropertyValue("float"));
        Assert.Equal("3", style.GetPropertyValue("initial-letter"));
    }

    [Theory]
    [InlineData("3 2", 24)]
    [InlineData("3 raise", 48)]
    public void RaisedAndSunkenInitials_MoveTheLinesDown(string value, int firstLine)
    {
        var document = Parse($$"""
            <style>p::first-letter { initial-letter: {{value}} }</style>
            <div style='height: 10px'></div>
            <p id=p>Abc<br>def<br>ghi<br>jkl<br>mno</p>
            """);

        // The letter does not extend into the previous block: the lines are moved down by the lines that are not sunk, and
        // the baseline of the letter (64px below the top of its box) is the one of the line of the sink
        Assert.Equal(new PhysicalRect(0, 12, 80, 80), GetLetterRect(document, "p"));
        var texts = GetTextRects(document, "p");
        Assert.Equal(new PhysicalRect(80, 10 + firstLine + 2, 40, 20), texts[0]);
        Assert.Equal(10 + firstLine + (5 * 24), GetRect(document, "p").Bottom);
    }

    [Theory]
    [InlineData("Ébc", 64)] // No glyph outline below the baseline: the box ends at the baseline
    [InlineData("pbc", 80)] // No glyph outline above the baseline: the box still spans the sink
    public void Box_FitsTheSinkAndTheGlyphOutlines(string text, int height)
    {
        var document = Parse($"<style>p::first-letter {{ initial-letter: 3 }}</style><p id=p>{text}<br>def<br>ghi<br>jkl</p>");
        Assert.Equal(new PhysicalRect(0, 2, 80, height), GetLetterRect(document, "p"));

        // The fourth line (72px) is next to the box only when the box ends below its block-start edge
        Assert.Equal(height > 70 ? 80 : 0, GetTextRects(document, "p")[3].X);
    }

    [Fact]
    public void Margins_AreInTheExclusionAndMoveTheBox()
    {
        var document = Parse("""
            <style>p::first-letter { initial-letter: 3; margin: 10px 45px 30px 15px }</style>
            <p id=p>Abc<br>def<br>ghi<br>jkl<br>mno<br>pqr<br>stu</p>
            """);

        // As in Chrome, the block-start margin moves the box; its margin box ends at 122px
        Assert.Equal(new PhysicalRect(15, 12, 80, 80), GetLetterRect(document, "p"));
        var texts = GetTextRects(document, "p");
        Assert.Equal(140, texts[0].X);
        Assert.Equal(140, texts[5].X);
        Assert.Equal(0, texts[6].X);
    }

    [Fact]
    public void RightToLeft_TheBoxIsAtTheInlineStartEdge()
    {
        var document = Parse("<style>p::first-letter { initial-letter: 3 }</style><p id=p dir=rtl>Abc<br>def</p>");
        Assert.Equal(new PhysicalRect(150, 2, 80, 80), GetLetterRect(document, "p"));
        Assert.Equal(110, GetTextRects(document, "p")[0].X);
    }

    [Fact]
    public void VerticalWritingMode()
    {
        // The central baseline of the line is 12px from its block-start edge: the box starts 16px (the cap height) before it
        var document = Parse("<style>p::first-letter { initial-letter: 3 }</style><p id=p style='writing-mode: vertical-rl; width: auto; height: 230px'>Abc<br>def<br>ghi<br>jkl<br>mno</p>");
        var block = GetRect(document, "p");
        var letter = GetLetterRect(document, "p");
        Assert.Equal(new PhysicalSize(80, 80), letter.Size);
        Assert.Equal(block.Right + 4, letter.Right);
        Assert.Equal(0, letter.Y);
        Assert.Equal(80, GetTextRects(document, "p")[0].Y);
    }

    [Fact]
    public void TextIndent_PrecedesTheInitialLetter()
    {
        // The indentation of the line is before the box; as in Chrome, the box inherits text-indent, which also indents its content
        var document = Parse("<style>p { text-indent: 10px } p::first-letter { initial-letter: 3 }</style><p id=p>Abc<br>def</p>");
        Assert.Equal(new PhysicalRect(10, 2, 90, 80), GetLetterRect(document, "p"));
        Assert.Equal([new(100, 2, 40, 20), new(100, 26, 60, 20)], GetTextRects(document, "p"));
    }

    [Fact]
    public void Floats_TheBoxIsNextToTheFloatsAndTheNextFloatsOfItsSideAreBelowIt()
    {
        var document = Parse("""
            <style>p::first-letter { initial-letter: 3 } .float { float: left; width: 50px; height: 50px }</style>
            <p id=p><span id=before class=float></span>Abc<br><span id=after class=float></span>def<br>ghi<br>jkl<br>mno</p>
            """);
        Assert.Equal(new PhysicalRect(50, 2, 80, 80), GetLetterRect(document, "p"));
        Assert.Equal(130, GetTextRects(document, "p")[0].X);

        // https://drafts.csswg.org/css-inline-3/#initial-letter-floats: a float that originates in the lines next to the initial
        // letter "must clear the initial letter"
        Assert.Equal(new PhysicalRect(0, 82, 50, 50), GetRect(document, "after"));
    }

    [Fact]
    public void Clear_DoesNotApplyToTheInitialLetters()
    {
        var document = Parse("""
            <style>p::first-letter { initial-letter: 3 }</style>
            <p id=p>Abc</p>
            <div id=clear style='clear: both; height: 10px'></div>
            """);
        Assert.Equal(24, GetRect(document, "clear").Y);
    }

    [Fact]
    public void ShortParagraph_TheNextBlockWrapsAroundTheInitialLetter()
    {
        var document = Parse("""
            <style>.letter::first-letter { initial-letter: 3 }</style>
            <p id=p class=letter>Abc</p>
            <p id=next>def<br>ghi<br>jkl<br>mno</p>
            """);

        // The block container does not grow to contain the box (unless it is a block formatting context root)
        Assert.Equal(24, GetRect(document, "p").Height);
        Assert.Equal([80d, 80, 80, 0], GetTextRects(document, "next").Select(rect => rect.X));
    }

    [Fact]
    public void ShortParagraph_TheInitialLetterOfTheNextBlockClearsTheInitialLetter()
    {
        var document = Parse("""
            <style>p::first-letter { initial-letter: 3 }</style>
            <p id=p>Abc<br>def</p>
            <p id=next>Ghi<br>jkl</p>
            """);
        Assert.Equal(48, GetRect(document, "next").Y);

        // The first line of the block is moved below the previous initial letter (which ends at 82px)
        Assert.Equal(new PhysicalRect(0, 84, 80, 80), GetLetterRect(document, "next"));
        Assert.Equal(new PhysicalRect(80, 84, 40, 20), GetTextRects(document, "next")[0]);
    }

    [Fact]
    public void BlockFormattingContextRoot_ContainsTheInitialLetter()
    {
        var document = Parse("<style>p::first-letter { initial-letter: 3 }</style><p id=p style='display: flow-root'>Abc</p>");
        Assert.Equal(82, GetRect(document, "p").Height);
    }

    [Fact]
    public void FloatingFirstLetter_IsNotAnInitialLetter()
    {
        var document = Parse("<style>p::first-letter { initial-letter: 3; float: left }</style><p id=p>Abc<br>def</p>");
        Assert.Equal(new PhysicalSize(20, 24), GetLetterRect(document, "p").Size);
    }

    [Fact]
    public void InitialLetter_DoesNotApplyToTheElements()
    {
        // Only the ::first-letter pseudo-element is supported
        var document = Parse("<p id=p><span id=span style='initial-letter: 3'>A</span>bc</p>");
        Assert.Equal(new PhysicalSize(20, 20), GetRect(document, "span").Size);
    }
}
