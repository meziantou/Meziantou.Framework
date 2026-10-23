using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The CSS Text layout features (tab stops, letter-spacing at the end of the lines, justification, text-spacing-trim,
/// text-autospace, hanging-punctuation, hyphenation limits, text-wrap-style, white-space-trim, word-space-transform,
/// text-group-align) and text-box-trim, measured with Ahem (1em glyphs, ascent 0.8em).
/// </summary>
public sealed class TextLayoutTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html) => Load("<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);

    /// <summary>
    /// A font with fullwidth punctuation and the <c>halt</c> feature (a subset of Noto Sans CJK JP of the web-platform-tests),
    /// installed as the fallback font of the CJK punctuation: the punctuation of a font without the feature is not trimmed.
    /// </summary>
    private static readonly Lazy<InstalledFontCollection> PunctuationFonts = new(() =>
    {
        var fonts = new InstalledFontCollection();
        Assert.True(fonts.AddFallback(File.ReadAllBytes(TestPaths.External("wpt", "fonts", "noto", "cjk", "NotoSansCJKjp-Regular-subset-halt.otf")), "U+3000-303F"));
        return fonts;
    });

    /// <summary>The subset of Noto Sans CJK JP of the web-platform-tests (with the halt feature) as an installed font.</summary>
    private static readonly Lazy<InstalledFontCollection> CjkFonts = new(() =>
    {
        var fonts = new InstalledFontCollection();
        Assert.True(fonts.Add(File.ReadAllBytes(TestPaths.External("wpt", "fonts", "noto", "cjk", "NotoSansCJKjp-Regular-subset-halt.otf"))));
        return fonts;
    });

    private Document Load(string html, bool punctuationFont = true, bool cjkFont = false)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 }, InstalledFonts = cjkFont ? CjkFonts.Value : punctuationFont ? PunctuationFonts.Value : null });
        var document = context.Navigate("https://example.com/", _ => html);
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

    private static string GetText(TextFragment fragment) => fragment.Run.Data[fragment.Start..fragment.End];

    [Fact]
    public void TextBoxTrim_BlockContainer()
    {
        // The line is 30px tall (half-leading 10px); trim-both with text edges trims the half-leading on both sides
        var document = Parse("<div id=d style='line-height: 30px; text-box: trim-both text'>X</div>");
        Assert.Equal(10, GetRect(document, "d").Height);
        Assert.Equal(0, GetTextFragments(document, "d")[0].GetRectInRoot().Y);
    }

    [Theory]
    [InlineData("4", 40)]
    [InlineData("25px", 25)]

    // The next tab stop is less than 0.5ch away: the subsequent one is used
    [InlineData("12px", 24)]
    [InlineData("0", 10)]
    public void TabSize(string tabSize, double x)
    {
        var document = Parse($"<div id=d style='white-space: pre; tab-size: {tabSize}'>X\tX</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(x, texts[^1].GetRectInRoot().X);
    }

    [Fact]
    public void TabSize_StopsFromTheStartOfTheLine()
    {
        // The tab stops are multiples of the tab size from the start edge of the block container (the indentation included)
        var document = Parse("<div id=d style='white-space: pre; tab-size: 20px; text-indent: 5px'>\tX</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(20, texts[^1].GetRectInRoot().X);
    }

    [Fact]
    public void LetterSpacing_NotAppliedAtTheEndOfTheLine()
    {
        var document = Parse("<div id=a style='width: max-content; letter-spacing: 5px'>XXX</div><div id=b style='width: 100px; letter-spacing: 5px; text-align: right'>XXX</div>");
        Assert.Equal(40, GetRect(document, "a").Width);
        var text = Assert.Single(GetTextFragments(document, "b"));
        Assert.Equal(new PhysicalRect(60, 10, 40, 10), text.GetRectInRoot());
    }

    [Fact]
    public void LetterSpacing_TheLineBreaksWithoutTheTrailingSpacing()
    {
        // "XX XX" is 58px with the spacing after each character but the last one
        var document = Parse("<div id=d style='width: 58px; letter-spacing: 2px'>XX XX</div>");
        Assert.Equal(10, GetRect(document, "d").Height);
    }

    [Theory]
    [InlineData("inter-character", 0, 20, 40)]
    [InlineData("auto", 0, 10, 20)]
    public void TextJustify(string textJustify, double first, double second, double third)
    {
        var document = Parse($"<div id=d style='width: 50px; text-align: justify; text-align-last: justify; text-justify: {textJustify}'>XXX</div>");
        var text = Assert.Single(GetTextFragments(document, "d"));
        var glyphs = text.Glyphs;
        Assert.Equal(0, first);
        Assert.Equal(second, glyphs[0].Advance + first);
        Assert.Equal(third, glyphs[0].Advance + glyphs[1].Advance);
    }

    [Fact]
    public void TextJustify_CjkCharactersAreJustificationOpportunities()
    {
        var document = Parse("<div id=d style='width: 100px; text-align: justify; text-align-last: justify'>\u6C34\u6C34\u6C34</div>");
        var text = Assert.Single(GetTextFragments(document, "d"));
        Assert.Equal(100, text.Size.Width);
        Assert.True(text.Glyphs[0].Advance > text.Glyphs[2].Advance);
    }

    [Fact]
    public void TextJustify_None()
    {
        var document = Parse("<div id=d style='width: 100px; text-align: justify; text-align-last: justify; text-justify: none'>XX XX</div>");
        Assert.Equal(50, Assert.Single(GetTextFragments(document, "d")).Size.Width);
    }

    [Theory]
    [InlineData("normal", 15)]
    [InlineData("space-all", 20)]
    [InlineData("trim-start", 10)]
    [InlineData("trim-all", 10)]
    public void TextSpacingTrim_AdjacentOpeningPunctuation(string trim, double width)
    {
        // The second opening bracket is set half-width after the first one (the first one at the start of the line with trim-start)
        var document = Parse($"<div id=d style='text-spacing-trim: {trim}'><span id=s>\u300C\u300C</span></div>");
        Assert.Equal(width, GetRect(document, "s").Width);
    }

    [Fact]
    public void MaxContent_KerningWithARemovedSpace()
    {
        // "A" is kerned with the space in the default font: the collapsible space removed at the end of the line is not shaped
        // with the text of the line, whose width is the max-content size
        var document = Load("<!DOCTYPE html><body style='margin: 0'><span id=a style='display: inline-block'>PA </span><br><span id=b style='display: inline-block'>PA</span><br><span id=c style='display: inline-block'>PA <br>PA</span>");
        Assert.Equal(GetRect(document, "b").Width, GetRect(document, "a").Width);
        Assert.Equal(GetRect(document, "b").Width, GetRect(document, "c").Width);
        Assert.Equal(GetRect(document, "a").Width, Assert.Single(GetTextFragments(document, "a")).Size.Width);
    }

    [Fact]
    public void MaxContent_JoinerBeforeARemovedSpace()
    {
        // The collapsible spaces removed at the end of the line follow a zero width joiner, which is not the content: the line
        // is as wide as without them
        var document = Load("<!DOCTYPE html><body style='margin: 0; font: 24px monospace'><span id=a style='display: inline-block'>[200d]\U0000200D  </span><br><span id=b style='display: inline-block'>[200d]</span>");
        Assert.Equal(GetRect(document, "b").Width, GetRect(document, "a").Width);
    }

    [Fact]
    public void TextSpacingTrim_NotWithoutTheHaltFeature()
    {
        // The brackets are the missing glyph of Ahem (1em): a font without the halt feature is not trimmed
        var document = Load("<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style><div id=d style='text-spacing-trim: trim-all'><span id=s>\U0000300C\U0000300C</span></div>", punctuationFont: false);
        Assert.Equal(20, GetRect(document, "s").Width);
    }

    [Theory]
    [InlineData("normal", 20)]
    [InlineData("trim-both", 15)]
    public void TextSpacingTrim_ClosingPunctuationAtTheEndOfTheLine(string trim, double width)
    {
        var document = Parse($"<div id=d style='text-spacing-trim: {trim}'><span id=s>X\u300D</span></div>");
        Assert.Equal(width, GetRect(document, "s").Width);
    }

    [Fact]
    public void TextSpacingTrim_ClosingPunctuationTrimmedWhenItDoesNotFit()
    {
        // normal: the closing punctuation at the end of the line is half-width when it does not fit otherwise
        var document = Parse("<div id=d style='width: 25px'>XX\u300D</div>");
        Assert.Equal(10, GetRect(document, "d").Height);
        Assert.Equal(25, Assert.Single(GetTextFragments(document, "d")).Size.Width);
    }

    [Theory]
    [InlineData("normal", 21.25)]
    [InlineData("no-autospace", 20)]
    public void TextAutospace(string autospace, double width)
    {
        // 1/8ic between an ideograph and a letter (the ic of the font without U+6C34 is 1em)
        var document = Parse($"<div style='text-autospace: {autospace}'><span id=s>\u6C34X</span></div>");
        Assert.Equal(width, GetRect(document, "s").Width);
    }

    [Fact]
    public void TextAutospace_NotAtTheEndOfTheLine()
    {
        var document = Parse("<div id=d style='width: 25px'>X\u6C34 \u6C34X</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(2, texts.Count);
        Assert.Equal(21.25, texts[0].Size.Width);
        Assert.Equal(21.25, texts[1].Size.Width);
    }

    [Fact]
    public void HangingPunctuation_First()
    {
        var document = Parse("<div id=d style='width: 100px; hanging-punctuation: first'>\"XX</div>");
        Assert.Equal(-10, Assert.Single(GetTextFragments(document, "d")).GetRectInRoot().X);
    }

    [Fact]
    public void HangingPunctuation_ForceEnd()
    {
        // The stop hangs outside the line box: the content is aligned without it
        var document = Parse("<div id=d style='width: 50px; hanging-punctuation: force-end; text-align: right'>XX.</div>");
        Assert.Equal(30, Assert.Single(GetTextFragments(document, "d")).GetRectInRoot().X);
    }

    [Fact]
    public void HangingPunctuation_AllowEnd()
    {
        // The stop hangs only when the line does not fit otherwise
        var document = Parse("<div id=a style='width: 30px; hanging-punctuation: allow-end'>XX. XX</div><div id=b style='width: 30px'>XX. XX</div>");
        Assert.Equal(20, GetRect(document, "a").Height);
        var texts = GetTextFragments(document, "a");
        Assert.Equal("XX.", GetText(texts[0]));
        Assert.Equal(20, GetRect(document, "b").Height);
    }

    [Theory]
    [InlineData("balance")]
    [InlineData("pretty")]
    public void TextWrapStyle(string wrapStyle)
    {
        // The greedy line breaking gives "XX XX XX" and "XX"
        var document = Parse($"<div id=d style='width: 80px; text-wrap-style: {wrapStyle}'>XX XX XX XX</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(2, texts.Count);
        Assert.Equal("XX XX", GetText(texts[0]));
        Assert.Equal("XX XX", GetText(texts[1]));
        Assert.Equal(80, GetRect(document, "d").Width);
    }

    [Fact]
    public void TextWrapStyle_BalanceKeepsTheNumberOfLines()
    {
        var document = Parse("<div id=d style='width: 100px; text-wrap: balance'>XX XX XX XX XX XX XX</div>");
        Assert.Equal(30, GetRect(document, "d").Height);
    }

    [Theory]
    [InlineData("<div id=d style='width: 235px; text-wrap: balance'><div style='float: left; width: 70px; height: 15px'></div>XXX XX XX XXX XXX XXX XXXX XXX XXXX</div>")]
    [InlineData("<div id=d style='width: 235px; text-wrap: balance'>XXX XX<div style='float: left; width: 70px; height: 15px'></div> XX XXX XXX XXX XXXX XXX XXXX</div>")]
    [InlineData("<div style='float: left; width: 70px; height: 15px'></div><div id=d style='width: 235px; text-wrap: balance'>XXX XX XX XXX XXX XXX XXXX XXX XXXX</div>")]
    public void TextWrapStyle_BalanceNextToAFloat(string html)
    {
        // The two first lines are next to the float (165px), the third one is not (235px). The greedy line breaking gives
        // "XXX XX XX XXX", "XXX XXX XXXX XXX" and "XXXX"; the space of each line is reduced by the same amount (65px)
        var document = Parse(html);
        var lines = GetTextFragments(document, "d").GroupBy(text => text.GetRectInRoot().Y).OrderBy(line => line.Key).Select(line => string.Join("", line.Select(GetText)).Trim()).ToArray();
        Assert.Equal(["XXX XX XX", "XXX XXX", "XXX XXXX XXX XXXX"], lines);
    }

    [Fact]
    public void TextWrapStyle_BalanceWithAFloatPlacedOnALine()
    {
        // The greedy line breaking gives "XXXXX XXXX XXXX XXX", "XXXX XXX" (between the two floats) and "XXXX". The second
        // float stays on the line of the content before it, which is not broken again in the space that remains
        var document = Parse("<div id=d style='width: 265px; text-wrap: balance'><div style='float: left; width: 70px; height: 15px'></div>XXXXX XXXX XXXX XXX<div id=f style='float: right; width: 70px; height: 15px'></div> XXXX XXX XXXX</div>");
        var lines = GetTextFragments(document, "d").GroupBy(text => text.GetRectInRoot().Y).OrderBy(line => line.Key).Select(line => string.Join("", line.Select(GetText)).Trim()).ToArray();
        Assert.Equal(["XXXXX XXXX", "XXXX XXX", "XXXX XXX XXXX"], lines);
        Assert.Equal(new PhysicalRect(195, 10, 70, 15), GetRect(document, "f"));
    }

    [Fact]
    public void HyphenateLimitLines()
    {
        // Without limit, each line ends with a hyphen; with a limit of 1, a hyphenated line is followed by a line that is not
        var html = "<div id=d style='width: 30px; {0}'>XX\u00ADXX\u00ADXX\u00ADXX</div>";
        var unlimited = Parse(string.Format(CultureInfo.InvariantCulture, html, ""));
        Assert.Equal(40, GetRect(unlimited, "d").Height);
        var limited = Parse(string.Format(CultureInfo.InvariantCulture, html, "hyphenate-limit-lines: 0"));
        Assert.Equal(10, GetRect(limited, "d").Height);
    }

    [Fact]
    public void HyphenateLimitChars()
    {
        var document = Parse("<div id=a style='width: 30px'>XX\u00ADXXX</div><div id=b style='width: 30px; hyphenate-limit-chars: 5 3 3'>XX\u00ADXXX</div>");
        Assert.Equal(20, GetRect(document, "a").Height);
        Assert.Equal(10, GetRect(document, "b").Height);
    }

    [Fact]
    public void HyphenateLimitLast()
    {
        // The line before the last line is not hyphenated: "XX XX-" fits, but the next line is the last line
        var html = "<div id=d style='width: 60px; {0}'>XX XX\u00ADXX</div>";
        Assert.Equal("XX XX\u00AD", GetText(GetTextFragments(Parse(string.Format(CultureInfo.InvariantCulture, html, "")), "d")[0]));
        Assert.Equal("XX", GetText(GetTextFragments(Parse(string.Format(CultureInfo.InvariantCulture, html, "hyphenate-limit-last: always")), "d")[0]));
    }

    [Fact]
    public void HangingPunctuation_FirstNotAfterTheEdgeOfAnInlineBox()
    {
        // The bracket is not at the start of the line: the border of its inline box precedes it
        var document = Parse("<div id=d style='width: 100px; hanging-punctuation: first'><span style='border-left: 10px solid'>(</span>XX</div>");
        Assert.Equal(10, GetTextFragments(document, "d")[0].GetRectInRoot().X);
    }

    [Fact]
    public void HangingPunctuation_FirstIdeographicSpace()
    {
        var document = Parse("<div id=d style='width: 100px; hanging-punctuation: first'>\U00003000XX</div>");
        Assert.Equal(-10, Assert.Single(GetTextFragments(document, "d")).GetRectInRoot().X);
    }

    [Fact]
    public void BreakSpaces_OverflowWrapBreaksBeforeTheSpace()
    {
        // The space does not hang and does not fit after the word: the line breaks before it rather than inside the word
        var document = Parse("<div id=d style='width: 40px; white-space: break-spaces; overflow-wrap: break-word'>XXXX XX</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(["XXXX", " XX"], texts.Select(GetText));
    }

    [Fact]
    public void BreakSpaces_IdeographicSpaces()
    {
        // The other space separators do not hang and are followed by a soft wrap opportunity
        var document = Parse("<div id=d style='width: 40px; white-space: break-spaces'>XX\U00003000\U00003000\U00003000XX</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(["XX\U00003000\U00003000", "\U00003000XX"], texts.Select(GetText));
    }

    [Fact]
    public void PreservedSpaces_HangingSpacesAreNotScrollableOverflow()
    {
        // The spaces that hang at the end of the line do not create a scrollbar
        var document = Parse("<div id=d style='width: 30px; white-space: pre-wrap; overflow: auto'>XXX   </div>");
        Assert.Equal(10, GetRect(document, "d").Height);
    }

    [Fact]
    public void PreservedSpace_AloneOnALine()
    {
        // The preserved space is on the line: the collapsible spaces after it are not at the start of the line
        var document = Parse("<div id=d> <span id=s style='white-space: pre-wrap'> </span> </div>");
        Assert.Equal(10, GetRect(document, "d").Height);
        Assert.Equal(10, GetRect(document, "s").Width);
    }

    [Fact]
    public void NoWrap_TheBoxOfASpaceControlsTheWrappingAfterIt()
    {
        // The space is in a box that wraps; the collapsed space after the non-wrapping box keeps its opportunity too
        var document = Parse("""
            <div id=a style='width: 50px; white-space: nowrap'><span style='white-space: normal'><span style='white-space: nowrap'>XXX</span> </span>XXX</div>
            <div id=b style='width: 50px'><span style='white-space: nowrap'>XXX </span> XXX</div>
            <div id=c style='width: 50px'><span style='white-space: nowrap'>XXX XXX</span></div>
            """);
        Assert.Equal(20, GetRect(document, "a").Height);
        Assert.Equal(20, GetRect(document, "b").Height);
        Assert.Equal(10, GetRect(document, "c").Height);
    }

    [Fact]
    public void AtomicInline_NoBreakNextToAWordJoiner()
    {
        // There is no soft wrap opportunity between an atomic inline and a character of the classes GL, WJ and ZWJ (but U+00A0)
        var document = Parse("<div id=d style='width: 25px'>XX\U00002060<img id=i style='width: 10px; height: 10px'></div>");
        Assert.Equal(new PhysicalRect(20, 0, 10, 10), GetRect(document, "i"));
    }

    [Fact]
    public void TextJustify_TheContentBeforeATabIsNotExpanded()
    {
        var document = Parse("<div id=d style='width: 100px; white-space: pre-wrap; tab-size: 4; text-align: justify'>X X\U00000009X X XXXXXXX</div>");
        // The text after the tab is expanded to the end of the line (its last space hangs after it)
        var texts = GetTextFragments(document, "d");
        Assert.Equal(30, texts[0].Size.Width);
        Assert.Equal(new PhysicalRect(40, 0, 70, 10), texts[2].GetRectInRoot());
    }

    [Fact]
    public void TextWrapStyle_BalanceDoesNotBreakTheWords()
    {
        // overflow-wrap is only used when the greedy line breaking needs it
        var document = Parse("<div id=d style='width: 150px; text-wrap: balance; overflow-wrap: break-word'>XXXXXXXXXXX XXXX XX</div>");
        Assert.Equal(["XXXXXXXXXXX", "XXXX XX"], GetTextFragments(document, "d").Select(GetText));
    }

    [Fact]
    public void TextIndent_AnonymousFlexItem()
    {
        // The anonymous flex item is a block container of its own: its first line is indented
        var document = Parse("<div id=d style='display: flex; text-indent: 20px'><div style='width: 0'></div>XX</div>");
        Assert.Equal(20, Assert.Single(GetTextFragments(document, "d")).GetRectInRoot().X);
    }

    [Fact]
    public void TextIndent_StaticPositionOfAnInlineLevelAbsolutelyPositionedBox()
    {
        var document = Parse("<div style='position: relative; text-indent: 20px'><span id=a style='position: absolute; width: 5px; height: 5px'></span></div>");
        Assert.Equal(20, GetRect(document, "a").X);
    }

    [Fact]
    public void WhiteSpaceTrim()
    {
        var document = Parse("<div id=a>[<span style='white-space-trim: discard-inner'> X </span>]</div><div id=b>A <span style='white-space-trim: discard-before discard-after'>B</span> C</div>");
        Assert.Equal("[X]", string.Concat(GetTextFragments(document, "a").Select(t => t.Run.Data[t.Start..t.End].Trim())));
        Assert.Equal(30, GetTextFragments(document, "b").Sum(t => t.Size.Width));
    }

    [Fact]
    public void TextBoxTrim_InlineBox()
    {
        // Ahem: the cap height is 0.8em, the descent 0.2em
        var document = Parse("<div><span id=s style='text-box: trim-both cap alphabetic'>X</span></div>");
        var rect = GetRect(document, "s");
        Assert.Equal(8, rect.Height);
        Assert.Equal(0, rect.Y);
    }

    [Fact]
    public void TextBoxTrim_PropagatesToTheFirstAndLastLines()
    {
        var document = Parse("<div id=d style='line-height: 30px; text-box: trim-both text'><p id=p style='margin: 0'>X<br>X</p></div>");
        Assert.Equal(40, GetRect(document, "d").Height);
    }

    [Fact]
    public void TextBoxTrim_MovesTheFloatsOfTheFirstFormattedLine()
    {
        // The first line box starts 10px before the content box (its half-leading is trimmed): the top of the margin box of the
        // float is at the top of the line box
        var document = Parse("<div id=d style='width: 100px; line-height: 30px; text-box: trim-both text'><div id=f style='float: left; width: 10px; height: 10px; margin-top: 10px'></div>XX</div>");
        Assert.Equal(new PhysicalRect(0, 0, 10, 10), GetRect(document, "f"));
        Assert.Equal(new PhysicalRect(10, 0, 20, 10), GetTextFragments(document, "d")[0].GetRectInRoot());
        Assert.Equal(10, GetRect(document, "d").Height);
    }

    [Fact]
    public void TextBoxTrim_DoesNotMoveTheFloatsWithoutFormattedLine()
    {
        var document = Parse("<div id=d style='width: 100px; line-height: 30px; text-box: trim-both text'><div id=f style='float: left; width: 10px; height: 10px'></div></div>");
        Assert.Equal(new PhysicalRect(0, 0, 10, 10), GetRect(document, "f"));
    }

    [Fact]
    public void TextBoxTrim_PaddingPreventsThePropagation()
    {
        var document = Parse("<div id=d style='line-height: 30px; text-box: trim-both text'><p style='margin: 0; padding-top: 1px'>X</p></div>");
        Assert.Equal(21, GetRect(document, "d").Height);
    }

    [Theory]

    // The U+200B of the text and the wbr element are replaced by spaces
    [InlineData("X<wbr>X&#x200B;X", "pre", 50)]

    // The space is inserted after the collapsing of the white space: it does not collapse with the space after it
    [InlineData("X<wbr> X", "normal", 40)]

    // A wbr element whose value is none is not replaced, nor the U+200B of an inline box whose value is none
    [InlineData("X<wbr style='word-space-transform: none'>X", "pre", 20)]
    [InlineData("X<span style='word-space-transform: none'>&#x200B;</span>X", "pre", 20)]

    // The separators next to the start and the end of the block container and next to a forced line break are not replaced
    [InlineData("<wbr>X<wbr>X&#x200B;", "pre", 30)]
    [InlineData("<span style='padding: 0 5px'><wbr></span>X<wbr>X<wbr><wbr><br><wbr>X", "pre", 40)]
    public void WordSpaceTransform_Space(string html, string whiteSpace, double width)
    {
        var document = Parse($"<div id=d style='width: max-content; white-space: {whiteSpace}; word-space-transform: space'>{html}</div>");
        Assert.Equal(width, GetRect(document, "d").Width);
    }

    [Fact]
    public void WordSpaceTransform_TheSpaceIsASoftWrapOpportunity()
    {
        // The line breaks after the inserted space, which is removed at the end of the line
        var document = Parse("<div id=d style='width: 45px; word-break: keep-all; word-space-transform: space'>XX<wbr>XX</div>");
        Assert.Equal(20, GetRect(document, "d").Height);
        var keepsAll = Parse("<div id=d style='width: 45px; white-space: pre; word-space-transform: space'>XX<wbr>XX</div>");
        Assert.Equal(10, GetRect(keepsAll, "d").Height);
    }

    [Fact]
    public void WordSpaceTransform_TheSpaceOfAWbrIsNotInTheDocument()
    {
        // The space of a wbr element has no text node: the geometry of the texts, the hit testing, the selection and innerText
        // work on the texts around it
        var document = Parse("<div id=d style='width: max-content; word-space-transform: space'>X<wbr id=w>X</div>");
        var element = (HtmlElement)document.GetElementById("d")!;
        Assert.Equal(30, GetRect(document, "d").Width);
        Assert.Equal(10, GetRect(document, "w").Width);
        Assert.Equal("XX", element.InnerText);
        Assert.Equal(20, GetTextFragments(document, "d")[1].GetRectInRoot().X);
        Assert.NotNull(document.CaretPositionFromPoint(15, 5));
        Assert.Same(document.GetElementById("w"), document.ElementFromPoint(15, 5));
        var selection = document.GetSelection()!;
        selection.SelectAllChildren(element);
        Assert.Equal("XX", selection.ToString());
    }

    [Theory]
    [InlineData("center", "start", 30, 30)]
    [InlineData("center", "right", 30, 50)]
    [InlineData("end", "start", 60, 60)]
    [InlineData("start", "right", 0, 20)]
    [InlineData("right", "center", 60, 70)]
    [InlineData("left", "end", 0, 20)]
    [InlineData("none", "right", 60, 80)]
    public void TextGroupAlign(string groupAlign, string textAlign, double firstLine, double secondLine)
    {
        // The longest line leaves 60px: that space is added as padding to the line boxes, and the lines are aligned in the 40px that remain
        var document = Parse($"<div id=d style='width: 100px; white-space: pre; text-group-align: {groupAlign}; text-align: {textAlign}'>XXXX\nXX</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(2, texts.Count);
        Assert.Equal(firstLine, texts[0].GetRectInRoot().X);
        Assert.Equal(secondLine, texts[1].GetRectInRoot().X);
    }

    [Fact]
    public void TextGroupAlign_RightToLeft()
    {
        // start pads the inline-end side of the line boxes: the left side in a right-to-left block container
        var document = Parse("<div id=d dir=rtl style='width: 100px; white-space: pre; text-group-align: start; text-align: left'>XXXX\nXX</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(60, texts[0].GetRectInRoot().X);
        Assert.Equal(60, texts[1].GetRectInRoot().X);
    }

    [Fact]
    public void TextGroupAlign_LineBoxesOfTheDescendantsInTheSameBlockFormattingContext()
    {
        // The lines of the child blocks are in the group; the lines of the inline-block (an independent formatting context) are not
        var document = Parse("""
            <div id=d style='width: 100px; text-group-align: end'>
              <p style='margin: 0'>XXXX</p>
              <p style='margin: 0 0 0 10px'>XX <span id=ib style='display: inline-block; width: 30px'>X</span></p>
              <p style='margin: 0; text-align: justify'>XX XXX XXXXX</p>
            </div>
            """);
        var texts = GetTextFragments(document, "d");

        // The shortest remaining space is the one of the second paragraph: 90px - (20px + 10px + 30px) = 30px
        Assert.Equal(30, texts[0].GetRectInRoot().X);
        Assert.Equal(40, texts[1].GetRectInRoot().X);
        Assert.Equal(70, GetRect(document, "ib").X);

        // The first line of the last paragraph is justified in the 70px that remain
        var justified = texts.First(text => GetText(text).StartsWith("XX XXX", StringComparison.Ordinal)).GetRectInRoot();
        Assert.Equal(30, justified.X);
        Assert.Equal(70, justified.Width);
    }

    [Fact]
    public void LetterSpacing_RunOfAtomicInlines()
    {
        // A run of atomic inlines is a typographic character unit: the spacing is after the run, and not at the end of a line
        const string Atomic = "<span style='display: inline-block; width: 10px; height: 10px'></span>";
        var document = Parse($"<div id=a style='width: max-content; letter-spacing: 5px'>X{Atomic}<span>{Atomic}</span><span id=x>X</span></div><div id=b style='width: max-content; letter-spacing: 5px'>X{Atomic}</div>");
        Assert.Equal(50, GetRect(document, "a").Width);
        Assert.Equal(40, GetRect(document, "x").X);
        Assert.Equal(25, GetRect(document, "b").Width);
    }

    [Fact]
    public void TextWrapStyle_BalanceEachGroupOfLinesBetweenForcedLineBreaks()
    {
        // The second group has a line as long as the line box: the lines of the first group are balanced with their own size
        var document = Parse("<div id=d style='width: 80px; text-wrap-style: balance'>XX XX XX XX<br>XXXXXXXX</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(3, texts.Count);
        Assert.Equal("XX XX", GetText(texts[0]));
        Assert.Equal("XX XX", GetText(texts[1]));
        Assert.Equal("XXXXXXXX", GetText(texts[2]));
    }

    [Fact]
    public void TextAutospace_UprightCharacterOfAnotherElement()
    {
        // In vertical text, a letter that is upright by the text-orientation of its own element has no inter-script spacing
        var document = Parse("<div style='writing-mode: vertical-rl'><span id=a>\U00006C34</span><span style='text-orientation: upright'>X</span><span id=b>\U00006C34</span><span>X</span></div>");
        Assert.Equal(10, GetRect(document, "a").Height);
        Assert.Equal(11.25, GetRect(document, "b").Height);
    }

    [Fact]
    public void TextJustify_InterCharacterRunOfAtomicInlines()
    {
        // A run of atomic inlines is a typographic character unit: 40px are distributed to the two opportunities, after the first
        // X and after the run
        const string Atomic = "<span style='display: inline-block; width: 20px; height: 10px'></span>";
        var document = Parse($"<div style='width: 100px; text-align: justify; text-align-last: justify; text-justify: inter-character'>X<span id=a style='display: inline-block; width: 20px; height: 10px'></span>{Atomic}<span id=x>X</span></div>");
        Assert.Equal(30, GetRect(document, "a").X);
        Assert.Equal(90, GetRect(document, "x").X);
    }

    [Theory]

    // The preserved spaces followed by an ideographic space are not followed by the forced line break: they hang unconditionally
    [InlineData("<span style='white-space: pre-wrap'>X  </span>\U00003000<br>X", 50, 40)]

    // The ideographic spaces before preserved spaces that conditionally hang only hang when none of the preserved spaces fits
    [InlineData("X\U00003000\U00003000<span style='white-space: pre-wrap'>  </span>", 40, 0)]
    [InlineData("XX\U00003000\U00003000<span style='white-space: pre-wrap'>  </span>", 40, 20)]
    public void HangingWhiteSpace_ConditionalOnlyAtTheEndOfTheLine(string html, double width, double x)
    {
        var document = Parse($"<div id=d style='width: {width}px; text-align: right'>{html}</div>");
        Assert.Equal(x, GetTextFragments(document, "d")[0].GetRectInRoot().X);
    }

    [Fact]
    public void LineBreaking_EmptyInlineBoxAfterCollapsibleSpacesStaysOnTheLine()
    {
        var document = Parse("<div style='width: 50px'>1234 <span id=s> </span>567</div>");
        Assert.Equal(0, GetRect(document, "s").Y);
    }

    [Fact]
    public void LineHeightNormal_IncludesTheFallbackFonts()
    {
        // The ideographic comma is rendered by the fallback font (Noto Sans CJK), whose normal line height is larger than the
        // one of Ahem (10px): the line is taller than the line of the text rendered by Ahem only
        var document = Parse("<div id=a style='line-height: normal'>X</div><div id=b style='line-height: normal'>X<span>\U00003001</span></div><div id=c>X\U00003001</div>");
        Assert.Equal(10, GetRect(document, "a").Height);
        Assert.True(GetRect(document, "b").Height > 10);
        Assert.Equal(10, GetRect(document, "c").Height);
    }

    [Theory]
    [InlineData("ja", 20)]
    [InlineData("zh-hans", 10)]
    public void TextSpacingTrim_ProportionalClosingQuoteDoesNotTrimTheNextOpening(string language, double width)
    {
        // The quotes of the Japanese convention are proportional, the ones of the Chinese convention are fullwidth
        var document = Load($"<!DOCTYPE html><style>body {{ margin: 0; font: 20px/1 'Noto Sans CJK JP' }}</style><div lang={language}>\u56FD\u2019<span id=s>\uFF08</span>\u56FD</div>", cjkFont: true);
        Assert.Equal(width, GetRect(document, "s").Width);
    }

    [Fact]
    public void TextSpacingTrim_PunctuationTakesTheScriptOfTheParagraph()
    {
        // The quote alone in its element takes the script of the text around it (Han): the language system of Chinese selects its
        // fullwidth glyph, whose blank half is trimmed after the opening parenthesis
        var document = Load("<!DOCTYPE html><style>body { margin: 0; font: 20px/1 'Noto Sans CJK JP' }</style><div lang=zh-hans>\u56FD\uFF08<span id=s>\u2018</span>\u56FD</div>", cjkFont: true);
        Assert.Equal(10, GetRect(document, "s").Width);
    }
}
