using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The inline layout (line boxes, line breaking, inline box fragments, alignment), measured with Ahem (1em glyphs, ascent 0.8em).</summary>
public sealed class InlineFormattingContextTests : IDisposable
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

    private static IReadOnlyList<BoxFragment> GetFragments(Document document, string id) => document.GetLayoutEngine().GetFragments(document.GetElementById(id)!);

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
    public void Wrap_AtSoftWrapOpportunities()
    {
        var document = Parse("<div id=d style='width: 55px'>XX XX XX XX</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(2, texts.Count);
        Assert.Equal((0, 5), (texts[0].Start, texts[0].End));
        Assert.Equal(new PhysicalRect(0, 0, 50, 10), texts[0].GetRectInRoot());
        Assert.Equal((6, 11), (texts[1].Start, texts[1].End));
        Assert.Equal(new PhysicalRect(0, 10, 50, 10), texts[1].GetRectInRoot());
        Assert.Equal(20, GetRect(document, "d").Height);
    }

    [Fact]
    public void WhiteSpace_CollapsesAcrossInlineBoxes()
    {
        var document = Parse("<div id=d>A <span id=s>  B</span>   C </div>");
        Assert.Equal(new PhysicalRect(20, 0, 10, 10), GetRect(document, "s"));
        var texts = GetTextFragments(document, "d");
        Assert.Equal(3, texts.Count);
        Assert.Equal(new PhysicalRect(0, 0, 20, 10), texts[0].GetRectInRoot());

        // The collapsed spaces of the last text node are part of its fragment (in the DOM), the trailing space is removed
        Assert.Equal(new PhysicalRect(30, 0, 20, 10), texts[2].GetRectInRoot());
        Assert.Equal("   C", GetText(texts[2]));
    }

    [Fact]
    public void WhiteSpace_LeadingAndTrailingSpacesRemoved()
    {
        var document = Parse("<div id=d style='width: 100px; text-align: right'>  XX  <span> </span></div>");
        var text = Assert.Single(GetTextFragments(document, "d"));
        Assert.Equal(new PhysicalRect(80, 0, 20, 10), text.GetRectInRoot());
    }

    [Fact]
    public void WhiteSpace_SegmentBreaksAreSpaces()
    {
        var document = Parse("<div id=d>XX\n  XX</div>");
        Assert.Equal(new PhysicalRect(0, 0, 50, 10), Assert.Single(GetTextFragments(document, "d")).GetRectInRoot());
    }

    [Fact]
    public void ForcedBreaks_LineBreakElements()
    {
        var document = Parse("<div id=d>X<br><br>Y</div>");
        Assert.Equal(30, GetRect(document, "d").Height);
        var texts = GetTextFragments(document, "d");
        Assert.Equal(new PhysicalRect(0, 20, 10, 10), texts[^1].GetRectInRoot());

        // The line break of a br element is a text fragment without glyph, on the line it ends
        var br = document.GetElementById("d")!.Children[0];
        var brFragment = Assert.Single(document.GetLayoutEngine().GetFragments(br));
        Assert.Equal(new PhysicalRect(10, 0, 0, 10), brFragment.GetRectInRoot());
    }

    [Fact]
    public void Pre_PreservesSpacesAndBreaks()
    {
        var document = Parse("<div id=d style='white-space: pre; width: 20px'>XX  XX\nYY</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(new PhysicalRect(0, 0, 60, 10), texts[0].GetRectInRoot());
        Assert.Equal(new PhysicalRect(0, 10, 20, 10), texts[1].GetRectInRoot());
    }

    [Fact]
    public void PreWrap_TrailingSpacesHang()
    {
        var document = Parse("<div id=d style='white-space: pre-wrap; width: 25px; text-align: right'>XX  XX</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(2, texts.Count);

        // The hanging spaces do not count for the alignment
        Assert.Equal(new PhysicalRect(5, 0, 40, 10), texts[0].GetRectInRoot());
        Assert.Equal(new PhysicalRect(5, 10, 20, 10), texts[1].GetRectInRoot());
    }

    [Theory]
    [InlineData("white-space: nowrap", 1)]
    [InlineData("text-wrap-mode: nowrap", 1)]
    [InlineData("white-space: normal", 3)]
    public void NoWrap(string style, int lines)
    {
        var document = Parse($"<div id=d style='width: 25px; {style}'>XX XX XX</div>");
        Assert.Equal(lines * 10, GetRect(document, "d").Height);
    }

    [Fact]
    public void NoWrap_CommonAncestorControlsTheBreak()
    {
        // The break between the characters of the two spans is controlled by their common ancestor, which wraps
        var document = Parse("<div id=d style='width: 25px'><span style='white-space: nowrap'>\U000056FD\U000056FD</span><span style='white-space: nowrap'>\U000056FD</span></div>");
        Assert.Equal(20, GetRect(document, "d").Height);

        // The break after a space is controlled by the box of the space, which does not wrap
        document = Parse("<div id=d style='width: 25px'><span style='white-space: nowrap'>XX XX </span><span style='white-space: nowrap'>XX</span></div>");
        Assert.Equal(10, GetRect(document, "d").Height);
    }

    [Theory]
    [InlineData("word-break: break-all", 3)]
    [InlineData("overflow-wrap: anywhere", 3)]
    [InlineData("overflow-wrap: break-word", 3)]
    [InlineData("word-break: break-word", 3)]
    [InlineData("overflow-wrap: normal", 1)]
    public void LongWords(string style, int lines)
    {
        var document = Parse($"<div id=d style='width: 25px; {style}'>XXXXXX</div>");
        Assert.Equal(lines * 10, GetRect(document, "d").Height);
    }

    [Fact]
    public void SoftHyphen_DisplaysAHyphen()
    {
        var document = Parse("<div id=d style='width: 35px'>XXX&shy;XXX</div>");
        Assert.Equal(20, GetRect(document, "d").Height);
        var texts = GetTextFragments(document, "d");
        var hyphen = Assert.Single(texts, text => text.IsHyphen);
        Assert.Equal(new PhysicalRect(30, 0, 10, 10), hyphen.GetRectInRoot());
        Assert.Equal(4, hyphen.Start);
    }

    [Fact]
    public void SoftHyphen_HyphensNone()
    {
        var document = Parse("<div id=d style='width: 35px; hyphens: none'>XXX&shy;XXX</div>");
        Assert.Equal(10, GetRect(document, "d").Height);
    }

    // The lines of the text fragments of an element: the text of each line, with "-" for the hyphens
    private static List<string> GetLines(Document document, string id)
    {
        var lines = new SortedDictionary<double, string>();
        foreach (var text in GetTextFragments(document, id))
        {
            var top = text.GetRectInRoot().Y;
            lines[top] = lines.GetValueOrDefault(top, "") + (text.IsHyphen ? "-" : GetText(text));
        }

        return [.. lines.Values];
    }

    [Fact]
    public void HyphensAuto_BreaksWords()
    {
        var document = Parse("<div id=d lang=en style='width: 60px; hyphens: auto'>implementation now</div>");
        Assert.Equal(["imple-", "menta-", "tion", "now"], GetLines(document, "d"));
        var hyphens = GetTextFragments(document, "d").Where(text => text.IsHyphen).ToList();
        Assert.Equal(2, hyphens.Count);
        Assert.Equal(new PhysicalRect(50, 0, 10, 10), hyphens[0].GetRectInRoot());
        Assert.Equal(5, hyphens[0].Start);
    }

    [Theory]
    [InlineData("hyphens: auto")] // no language
    [InlineData("hyphens: auto' lang='zh")] // no dictionary
    [InlineData("hyphens: manual' lang='en")]
    [InlineData("hyphens: auto; text-wrap-mode: nowrap' lang='en")]
    public void HyphensAuto_NotHyphenated(string style)
    {
        var document = Parse($"<div id=d style='width: 60px; {style}'>implementation now</div>");
        Assert.DoesNotContain(GetTextFragments(document, "d"), text => text.IsHyphen);
    }

    [Fact]
    public void HyphensAuto_AutoPhrase()
    {
        // https://drafts.csswg.org/css-text-4/#valdef-word-break-auto-phrase: the hyphenation opportunities are only taken when no
        // other opportunity fits the line (as in Chrome, WPT css/css-text/word-break/auto-phrase/word-break-auto-phrase-008.html)
        var document = Parse("<div id=a lang=en style='width: 90px; hyphens: auto'>an implementation now</div><div id=b lang=en style='width: 90px; hyphens: auto; word-break: auto-phrase'>an implementation now</div>");
        Assert.Equal(["an imple-", "mentation", "now"], GetLines(document, "a"));
        Assert.Equal(["an", "implemen-", "tation", "now"], GetLines(document, "b"));
    }

    [Theory]
    [InlineData("nl", "cafee&shy;tje", "caf-|tje", 20)] // the Dutch diminutive of café: café-tje
    [InlineData("hu", "&#xD6;s&shy;szeg", "\U000000D6s\U000000AD-|szeg", 20)] // the long double consonants of Hungarian: Ösz-szeg
    [InlineData("zh-Latn-pinyin", "t&#xFA;&shy;&#x2019;&#xE0;n", "t\U000000FA\U000000AD-|\U000000E0n", 10)] // the apostrophe between the syllables of Pinyin
    [InlineData("en", "cafee&shy;tje", "cafee\U000000AD-|tje", 10)]
    public void HyphensManual_SpellingChanges(string language, string text, string expected, double hyphenWidth)
    {
        // https://drafts.csswg.org/css-text-4/#valdef-hyphens-manual: "should apply any appropriate spelling changes just as for
        // automatic hyphenation at the same point" (the hyphen fragment has the letters of the spelling change)
        var document = Parse($"<div id=d lang={language} style='width: 10px; hyphens: manual'>{text}</div>");
        Assert.Equal(expected, string.Join("|", GetLines(document, "d")));
        Assert.Equal(hyphenWidth, Assert.Single(GetTextFragments(document, "d"), fragment => fragment.IsHyphen).GetRectInRoot().Width);
    }

    [Fact]
    public void HyphensAuto_LanguageOfTheInlineBox()
    {
        var document = Parse("<div id=d lang=zh style='width: 60px; hyphens: auto'><span lang=en-US>implementation</span> implementation</div>");
        Assert.Equal(["imple-", "menta-", "tion", "implementation"], GetLines(document, "d"));
    }

    [Fact]
    public void HyphensAuto_SoftHyphensSuppressAutomaticHyphenation()
    {
        // https://drafts.csswg.org/css-text-4/#valdef-hyphens-auto: "Automatic hyphenation opportunities within a word must be
        // ignored if the word contains a conditional hyphen"
        var document = Parse("<div id=d lang=en style='width: 60px; hyphens: auto'>implemen&shy;tation now</div>");
        Assert.Equal(["implemen­-", "tation", "now"], GetLines(document, "d"));
    }

    [Fact]
    public void HyphensAuto_LastWord()
    {
        // As in Chrome, the last word is only hyphenated when it starts a line (or is the only word)
        var document = Parse("<div id=d lang=en style='width: 100px; hyphens: auto'>Test hyphenation</div>");
        Assert.Equal(["Test", "hyphen-", "ation"], GetLines(document, "d"));

        document = Parse("<div id=d lang=en style='width: 60px; hyphens: auto'>regulation</div>");
        Assert.Equal(["regu-", "lation"], GetLines(document, "d"));
    }

    [Fact]
    public void HyphensAuto_CapitalizedEnglishWords()
    {
        var document = Parse("<div id=d lang=en style='width: 60px; hyphens: auto'>Implementation now</div>");
        Assert.Equal(["Implementation", "now"], GetLines(document, "d"));

        document = Parse("<div id=d lang=de style='width: 60px; hyphens: auto'>Wiedervereinigung jetzt</div>");
        Assert.Equal(["Wie-", "der-", "verei-", "nigung", "jetzt"], GetLines(document, "d"));
    }

    [Fact]
    public void HyphensAuto_HyphenateLimitChars()
    {
        var document = Parse("<div id=d lang=en style='width: 10px; hyphens: auto; hyphenate-limit-chars: auto 3 2'>example</div>");
        Assert.Equal(["exam-", "ple"], GetLines(document, "d"));
    }

    [Fact]
    public void HyphensAuto_HyphenateCharacter()
    {
        var document = Parse("<div id=d lang=en style='width: 60px; hyphens: auto; hyphenate-character: \"=\"'>regulation</div>");
        var hyphen = Assert.Single(GetTextFragments(document, "d"), text => text.IsHyphen);
        Assert.Equal(new PhysicalRect(40, 0, 10, 10), hyphen.GetRectInRoot());
    }

    [Fact]
    public void HyphensAuto_WordAcrossInlineBoxes()
    {
        var document = Parse("<div id=d lang=en style='width: 60px; hyphens: auto'>high<b>way</b></div>");
        Assert.Equal(["high-", "way"], GetLines(document, "d"));
    }

    [Fact]
    public void HyphensAuto_WordBreakBreakAll()
    {
        // Every letter is a soft wrap opportunity: the words are not hyphenated
        var document = Parse("<div id=d lang=en style='width: 60px; hyphens: auto; word-break: break-all'>implementation</div>");
        Assert.Equal(["implem", "entati", "on"], GetLines(document, "d"));
    }

    [Fact]
    public void HyphensAuto_MinContent()
    {
        // The min-content size is the widest part of the hyphenated words, with its hyphen: "ple-", "men-" and "tion"
        var document = Parse("<div id=d lang=en style='width: min-content; hyphens: auto'>implementation</div>");
        Assert.Equal(40, GetRect(document, "d").Width);
        Assert.Equal(["im-", "ple-", "men-", "ta-", "tion"], GetLines(document, "d"));
    }

    [Fact]
    public void HyphensAuto_VerticalText()
    {
        var document = Parse("<div id=d lang=en style='writing-mode: vertical-rl; height: 60px; hyphens: auto'>regulation</div>");
        Assert.Equal(20, GetRect(document, "d").Width);
        Assert.Contains(GetTextFragments(document, "d"), text => text.IsHyphen);
    }

    [Fact]
    public void HyphensAuto_HyphenateLimitLines()
    {
        var document = Parse("<div id=d lang=en style='width: 60px; hyphens: auto; hyphenate-limit-lines: 1'>implementation</div>");
        Assert.Equal(["imple-", "mentation"], GetLines(document, "d"));
    }

    [Fact]
    public void InlineBox_SplitAcrossLines()
    {
        // box-decoration-break: slice: the inline-start edges are on the first fragment, the inline-end edges on the last one
        var document = Parse("<div style='width: 45px'>a<span id=s style='padding: 0 5px; border-left: 2px solid'>bb cc dd</span>e</div>");
        var fragments = GetFragments(document, "s");
        Assert.Equal(3, fragments.Count);
        Assert.Equal(new PhysicalRect(10, 0, 27, 10), fragments[0].GetRectInRoot());
        Assert.True(fragments[0].IsFirstFragment);
        Assert.False(fragments[0].IsLastFragment);
        Assert.Equal(new Sides<double>(0, 0, 0, 2), fragments[0].Border);
        Assert.Equal(new Sides<double>(0, 0, 0, 5), fragments[0].Padding);
        Assert.Equal(new PhysicalRect(0, 10, 20, 10), fragments[1].GetRectInRoot());
        Assert.False(fragments[1].IsFirstFragment);
        Assert.False(fragments[1].IsLastFragment);
        Assert.Equal(new PhysicalRect(0, 20, 25, 10), fragments[2].GetRectInRoot());
        Assert.True(fragments[2].IsLastFragment);

        // The text is a child of the fragment of the inline box
        var text = Assert.IsType<TextFragment>(Assert.Single(fragments[0].Children));
        Assert.Equal(new PhysicalRect(17, 0, 20, 10), text.GetRectInRoot());
    }

    [Fact]
    public void TextFit_Consistent()
    {
        // The lines are scaled by the smallest factor: 120 / 60 = 2 (the second line could grow by 4). The line height of 1
        // follows the used font size
        var document = Parse("<div id=d style='width: 120px; white-space: pre; text-fit: grow consistent'>ABCDEF\nGHI</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(new PhysicalRect(0, 0, 120, 20), texts[0].GetRectInRoot());
        Assert.Equal(new PhysicalRect(0, 20, 60, 20), texts[1].GetRectInRoot());
        Assert.Equal(40, GetRect(document, "d").Height);
        Assert.Equal(2, texts[0].Style.UsedFontScale);
        Assert.Equal(20, texts[0].Style.Layout.FontSize);
        Assert.Equal(10, texts[0].Style.FontSize);

        // shrink: the lines are broken with the computed font size, then scaled down to fit (160 -> 80)
        document = Parse("<div id=d style='width: 80px; white-space: pre; text-fit: shrink'>ABCDEFGHABCDEFGH\nABCD</div>");
        texts = GetTextFragments(document, "d");
        Assert.Equal(new PhysicalRect(0, 0, 80, 5), texts[0].GetRectInRoot());
        Assert.Equal(new PhysicalRect(0, 5, 20, 5), texts[1].GetRectInRoot());

        // grow does not scale down, and the lines without scalable part do not count
        document = Parse("<div id=d style='width: 80px; white-space: pre; text-fit: grow'>ABCDEFGHABCDEFGH</div><div id=e style='width: 80px; text-fit: grow'><span style='display: inline-block; width: 80px; height: 5px'></span> ABCD</div>");
        Assert.Equal(new PhysicalRect(0, 0, 160, 10), Assert.Single(GetTextFragments(document, "d")).GetRectInRoot());
        Assert.Equal(new PhysicalSize(80, 20), GetTextFragments(document, "e")[^1].Size);
    }

    [Fact]
    public void TextFit_PerLine()
    {
        // per-line: each line has its factor, but the last line and the lines that end with a forced break are not scaled
        var document = Parse("<div id=d style='width: 60px; text-fit: grow per-line'>ABCD EFGHI JK<br>LM NO</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(4, texts.Count);
        Assert.Equal(new PhysicalRect(0, 0, 60, 15), texts[0].GetRectInRoot());
        Assert.Equal(new PhysicalRect(0, 15, 60, 12), texts[1].GetRectInRoot());
        Assert.Equal(new PhysicalRect(0, 27, 20, 10), texts[2].GetRectInRoot());
        Assert.Equal(new PhysicalRect(0, 37, 50, 10), texts[3].GetRectInRoot());

        // per-line-all: also the last line; the percentage limits the factor
        document = Parse("<div id=d style='width: 60px; text-fit: grow per-line-all'>ABCD EF</div><div id=e style='width: 60px; text-fit: grow per-line-all 200%'>ABCD EF</div>");
        texts = GetTextFragments(document, "d");
        Assert.Equal(new PhysicalRect(0, 0, 60, 15), texts[0].GetRectInRoot());
        Assert.Equal(new PhysicalRect(0, 15, 60, 30), texts[1].GetRectInRoot());
        Assert.Equal(new PhysicalSize(40, 20), GetTextFragments(document, "e")[1].Size);
    }

    [Fact]
    public void TextFit_ScalablePartsOfTheLine()
    {
        // The edges of the inline boxes and the spacing that is a length are not scaled; the spacing that is a percentage is.
        // 122 = 2 * 60 + 2: the factor is 2
        var document = Parse("<div id=d style='width: 122px; text-fit: grow'>AB<span id=s style='border: 1px solid'>CD</span>EF</div>");
        Assert.Equal(new PhysicalRect(40, -1, 42, 22), GetRect(document, "s"));
        Assert.Equal(20, GetRect(document, "d").Height);

        // 2 characters and a letter spacing of 10px: (110 - 10) / 20 = 5
        document = Parse("<div id=d style='width: 110px; text-fit: grow; letter-spacing: 10px'>A<span style='letter-spacing: 0'>A</span></div>");
        Assert.Equal(50, GetRect(document, "d").Height);

        // a letter spacing of 50% of the font size: 100 / (20 + 5) = 4
        document = Parse("<div id=d style='width: 100px; text-fit: grow; letter-spacing: 50%'>A<span style='letter-spacing: 0'>A</span></div>");
        Assert.Equal(40, GetRect(document, "d").Height);

        // The font-relative lengths are the ones of the computed font size: a line-height of 2em is not scaled, a padding neither
        document = Parse("<div id=d style='width: 100px; line-height: 2em; text-fit: grow'><span id=s style='padding-left: 1em'>ABC</span></div>");
        Assert.Equal(20, GetRect(document, "d").Height);
        Assert.Equal(new PhysicalSize(100, 30), GetRect(document, "s").Size);
    }

    [Fact]
    public void TextFit_NotBesideFloats()
    {
        // "If a feature such as float [...] would make the line's inline size depend on its block position, scaling is
        // disabled for the block"
        var document = Parse("<div id=d style='width: 120px; text-fit: grow'><span style='float: left; width: 10px; height: 10px'></span>ABCDEF</div>");
        Assert.Equal(new PhysicalRect(10, 0, 60, 10), Assert.Single(GetTextFragments(document, "d")).GetRectInRoot());
    }

    [Fact]
    public void InlineBox_BoxDecorationBreakClone_TheLineBreakingReservesTheRepeatedEdges()
    {
        // The inline-end border is repeated on each line: "x x x x x" (90px) fits in 100px, but not with the 40px border
        var document = Parse("<div id=d style='width: 100px'><span id=s style='border-right: 40px solid; box-decoration-break: clone'>x x x x x</span></div>");
        Assert.Equal(["x x x", "x x"], GetLines(document, "d"));
        var fragments = GetFragments(document, "s");
        Assert.Equal(2, fragments.Count);
        Assert.Equal(new PhysicalRect(0, 0, 90, 10), fragments[0].GetRectInRoot());
        Assert.Equal(new PhysicalRect(0, 10, 70, 10), fragments[1].GetRectInRoot());
        Assert.Equal(40, fragments[0].Border.Right);
        Assert.Equal(40, fragments[1].Border.Right);

        // The inline-start padding is repeated at the start of the lines where the box continues
        document = Parse("<div id=d style='width: 75px'><span id=s style='padding-left: 30px; box-decoration-break: clone'>xx xx xx</span></div>");
        Assert.Equal(["xx", "xx", "xx"], GetLines(document, "d"));
        Assert.All(GetFragments(document, "s"), fragment => Assert.Equal(new PhysicalSize(50, 10), fragment.Size));

        // slice: the edges are at the start and at the end of the box only
        document = Parse("<div id=d style='width: 75px'><span id=s style='padding-left: 30px'>xx xx xx</span></div>");
        Assert.Equal(["xx", "xx xx"], GetLines(document, "d"));
    }

    [Fact]
    public void InlineBox_BoxDecorationBreakClone_IntrinsicSizes()
    {
        // The min-content size has the edges repeated on each line; on a single line, the edges are there once
        var document = Parse("<div id=d style='width: min-content'><span style='border-right: 40px solid; box-decoration-break: clone'>x xx x</span></div><div id=e style='width: max-content'><span style='border-right: 40px solid; box-decoration-break: clone'>x xx x</span></div>");
        Assert.Equal(60, GetRect(document, "d").Width);
        Assert.Equal(100, GetRect(document, "e").Width);
    }

    [Fact]
    public void InlineBox_BlockAxisEdgesDoNotChangeTheLineHeight()
    {
        var document = Parse("<div id=d>X<span id=s style='padding: 5px 0; border-top: 2px solid; margin: 7px 0'>Y</span></div>");
        Assert.Equal(10, GetRect(document, "d").Height);
        Assert.Equal(new PhysicalRect(10, -7, 10, 22), GetRect(document, "s"));
    }

    [Fact]
    public void InlineBox_Empty()
    {
        var document = Parse("<div id=d>A<span id=s style='padding-right: 10px'></span>B</div>");
        Assert.Equal(new PhysicalRect(10, 0, 10, 10), GetRect(document, "s"));
        Assert.Equal(new PhysicalRect(20, 0, 10, 10), GetTextFragments(document, "d")[^1].GetRectInRoot());
    }

    [Fact]
    public void LineHeight_OfTheInlineBoxes()
    {
        // The layout bounds of the span (line-height 30px) are 18px above the baseline and 12px below
        var document = Parse("<div id=d>X<span id=s style='line-height: 30px'>Y</span></div>");
        Assert.Equal(30, GetRect(document, "d").Height);
        Assert.Equal(new PhysicalRect(0, 10, 10, 10), GetTextFragments(document, "d")[0].GetRectInRoot());
        var line = Assert.IsType<LineBoxFragment>(GetTextFragments(document, "d")[0].Parent);
        Assert.Equal(18, line.Baseline);
        Assert.Equal(18, GetFragments(document, "d")[0].FirstBaseline);
    }

    [Fact]
    public void FirstLine_OfAnAncestorIsNestedInTheInnermostBlock()
    {
        // The ::first-line of #a is inside #c: its color wins over the color that #c inherits from #b, and the span inherits it
        var document = Parse("<style>#a::first-line { color: rgb(0, 128, 0) } #b { color: rgb(0, 0, 255) }</style><div id=a><div id=b><div id=c><span>X</span><br>Y</div></div></div>");
        var texts = GetTextFragments(document, "c");
        Assert.Equal("rgb(0, 128, 0)", texts[0].Style.GetComputedValue("color"));
        Assert.Equal("rgb(0, 0, 255)", texts[^1].Style.GetComputedValue("color"));
    }

    [Fact]
    public void OutsideMarker_MakesTheLineOfAnInlineListItemTaller()
    {
        // The marker (20px, ascent 16px) is on the baseline of the line: 16px above it, and the text is 2px below it
        var document = Parse("<style>li::marker { font-size: 20px }</style><ul style='margin: 0'><li id=a>X</li><li id=b><div>X</div></li></ul>");
        Assert.Equal(20, GetRect(document, "a").Height);
        Assert.Equal(new PhysicalRect(40, 8, 10, 10), GetTextFragments(document, "a")[0].GetRectInRoot());

        // The marker placed next to the line of a descendant block does not change the line
        Assert.Equal(10, GetRect(document, "b").Height);
    }

    [Fact]
    public void LineHeight_OfTheTextOfDisplayContentsElements()
    {
        // The text of a display: contents element has its font and its line height: 16px above the baseline, 4px below
        var document = Parse("<div id=d><span style='display: contents; font-size: 20px'>X</span></div><div id=e><span style='display: contents; line-height: 30px'>X</span></div>");
        Assert.Equal(20, GetRect(document, "d").Height);
        Assert.Equal(new PhysicalRect(0, 0, 20, 20), GetTextFragments(document, "d")[0].GetRectInRoot());
        Assert.Equal(16, Assert.IsType<LineBoxFragment>(GetTextFragments(document, "d")[0].Parent).Baseline);
        Assert.Equal(30, GetRect(document, "e").Height);
    }

    [Theory]
    [InlineData("baseline", 22, 12)]
    [InlineData("middle", 20, 6)]
    [InlineData("text-top", 20, 0)]
    [InlineData("text-bottom", 20, 10)]
    [InlineData("top", 20, 0)]
    [InlineData("bottom", 20, 10)]
    [InlineData("5px", 27, 17)]
    [InlineData("50%", 27, 17)]
    [InlineData("sub", 20, 9)]
    [InlineData("super", 26.333, 16.333)]

    // The baselines of an image are synthesized from its margin box: its bottom edge is on the alphabetic and ideographic-under
    // (the descent of Ahem, which has no BASE table) baselines of the parent, its middle on the central (halfway between the
    // ascent and the descent) and math (half the ascent) baselines, its top edge on the hanging baseline (80% of the ascent)
    [InlineData("alphabetic", 22, 12)]
    [InlineData("ideographic", 20, 10)]
    [InlineData("central", 20, 5)]
    [InlineData("mathematical", 20, 6)]
    [InlineData("hanging", 21.6, 0)]
    public void VerticalAlign(string verticalAlign, double lineHeight, double textTop)
    {
        var document = Parse($"<div id=d>X<img style='width: 10px; height: 20px; vertical-align: {verticalAlign}'></div>");
        Assert.Equal(lineHeight, Math.Round(GetRect(document, "d").Height, 3, MidpointRounding.AwayFromZero));
        Assert.Equal(textTop, Math.Round(GetTextFragments(document, "d")[0].GetRectInRoot().Y, 3, MidpointRounding.AwayFromZero));
    }

    [Theory]

    // The corresponding baselines of the box (20px) and of its parent (10px) match: the alphabetic baselines (the tops are 8px
    // apart), the hanging baselines (80% of the ascents: 6.4px and 12.8px above the baselines), the central baselines (3px and
    // 6px above the baselines), the ideographic-under baselines (the descents), the math baselines (half the ascents)
    [InlineData("baseline", 8, 0)]
    [InlineData("alphabetic", 8, 0)]
    [InlineData("hanging", 1.6, 0)]
    [InlineData("central", 5, 0)]
    [InlineData("ideographic", 10, 0)]
    [InlineData("mathematical", 4, 0)]
    [InlineData("text-top", 0, 0)]
    [InlineData("text-bottom", 10, 0)]
    public void AlignmentBaseline_InlineBox(string alignmentBaseline, double textTop, double boxTop)
    {
        var document = Parse($"<div id=d>X<span id=s style='font-size: 20px; alignment-baseline: {alignmentBaseline}'>Y</span></div>");
        Assert.Equal(textTop, Math.Round(GetTextFragments(document, "d")[0].GetRectInRoot().Y, 3, MidpointRounding.AwayFromZero));
        Assert.Equal(boxTop, Math.Round(GetRect(document, "s").Y, 3, MidpointRounding.AwayFromZero));
    }

    [Theory]
    [InlineData("auto", 8, 0, 14)]
    [InlineData("alphabetic", 8, 0, 14)]
    [InlineData("hanging", 1.6, 0, 3.2)]
    [InlineData("central", 5, 0, 9)]
    [InlineData("text-top", 0, 0, 0)]
    [InlineData("text-bottom", 10, 0, 18)]
    public void DominantBaseline_IsTheDefaultAlignmentBaseline(string dominantBaseline, double textTop, double boxTop, double imageTop)
    {
        // The children of the root inline box are aligned by its dominant baseline; the baselines of the image (2px tall) are
        // synthesized from its margin box (its bottom edge, its middle or its top edge)
        var document = Parse($"<div id=d style='dominant-baseline: {dominantBaseline}'>X<span id=s style='font-size: 20px'>Y</span><img id=i style='width: 10px; height: 2px'></div>");
        Assert.Equal(textTop, Math.Round(GetTextFragments(document, "d")[0].GetRectInRoot().Y, 3, MidpointRounding.AwayFromZero));
        Assert.Equal(boxTop, Math.Round(GetRect(document, "s").Y, 3, MidpointRounding.AwayFromZero));
        Assert.Equal(imageTop, Math.Round(GetRect(document, "i").Y, 3, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void DominantBaseline_IsInherited()
    {
        // The inner box is aligned in its parent by the dominant baseline inherited from the block container
        var document = Parse("<div id=d style='dominant-baseline: text-top'><span>X<span id=s style='font-size: 20px'>Y</span></span></div>");
        Assert.Equal(0, GetTextFragments(document, "d")[0].GetRectInRoot().Y);
        Assert.Equal(0, GetRect(document, "s").Y);
    }

    [Fact]
    public void VerticalAlign_TopAlignsTheSubtree()
    {
        var document = Parse("<div id=d>X<span id=s style='vertical-align: top; font-size: 30px'>Y</span><span style='vertical-align: super'>s</span></div>");
        Assert.Equal(30, GetRect(document, "d").Height);
        Assert.Equal(new PhysicalRect(10, 0, 30, 30), GetRect(document, "s"));
    }

    [Theory]
    [InlineData("ltr", "start", 0)]
    [InlineData("ltr", "end", 80)]
    [InlineData("ltr", "left", 0)]
    [InlineData("ltr", "right", 80)]
    [InlineData("ltr", "center", 40)]
    [InlineData("rtl", "start", 80)]
    [InlineData("rtl", "end", 0)]
    [InlineData("rtl", "left", 0)]
    [InlineData("rtl", "center", 40)]
    public void TextAlign(string direction, string textAlign, double x)
    {
        var document = Parse($"<div id=d style='width: 100px; direction: {direction}; text-align: {textAlign}'>XX</div>");
        Assert.Equal(x, Assert.Single(GetTextFragments(document, "d")).GetRectInRoot().X);
    }

    [Fact]
    public void TextAlign_OverflowingLineIsStartAligned()
    {
        var document = Parse("<div id=d style='width: 30px; text-align: center; white-space: nowrap'>XXXXX</div>");
        Assert.Equal(0, Assert.Single(GetTextFragments(document, "d")).GetRectInRoot().X);
    }

    [Fact]
    public void TextAlign_Justify()
    {
        var document = Parse("<div id=d style='width: 45px; text-align: justify'>XX X XX X</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(2, texts.Count);
        Assert.Equal(new PhysicalRect(0, 0, 45, 10), texts[0].GetRectInRoot());
        Assert.Equal(15, texts[0].Glyphs[2].Advance);

        // The last line is not justified (text-align-last: auto)
        Assert.Equal(new PhysicalRect(0, 10, 40, 10), texts[1].GetRectInRoot());
        Assert.Equal(10, texts[1].Glyphs[2].Advance);
    }

    [Fact]
    public void TextAlignLast()
    {
        var document = Parse("<div id=d style='width: 100px; text-align-last: right'>XX<br>XX</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(80, texts[0].GetRectInRoot().X);
        Assert.Equal(80, texts[1].GetRectInRoot().X);
    }

    [Theory]
    [InlineData("20px", 20, 0)]
    [InlineData("20%", 10, 0)]
    [InlineData("20px hanging", 0, 20)]
    [InlineData("-10px", -10, 0)]
    public void TextIndent(string indent, double firstLine, double secondLine)
    {
        var document = Parse($"<div id=d style='width: 50px; text-indent: {indent}'>XX XX XX XX</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(firstLine, texts[0].GetRectInRoot().X);
        Assert.Equal(secondLine, texts[1].GetRectInRoot().X);
    }

    [Fact]
    public void TextIndent_EachLine()
    {
        var document = Parse("<div id=d style='width: 100px; text-indent: 20px each-line'>XX<br>XX</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(20, texts[0].GetRectInRoot().X);
        Assert.Equal(20, texts[1].GetRectInRoot().X);
    }

    [Fact]
    public void Bidi_ReordersTheFragments()
    {
        // With bidi-override, the characters are right-to-left: the inline box b is between D and AB
        var document = Parse("<div id=d style='width: 100px'><span style='unicode-bidi: bidi-override; direction: rtl'>AB<b id=b>C</b>D</span></div>");
        Assert.Equal(new PhysicalRect(10, 0, 10, 10), GetRect(document, "b"));
        var texts = GetTextFragments(document, "d");
        Assert.Equal(20, texts[0].GetRectInRoot().X);
        Assert.True(texts[0].ShapedText!.IsRightToLeft);
        Assert.Equal(0, texts[2].GetRectInRoot().X);
    }

    [Fact]
    public void Bidi_InlineBoxSplitByTheReordering()
    {
        // "A B &#x5d0;&#x5d1; C": the Hebrew letters are reordered, so the fragment of b ("B " and alef) is split around bet
        var document = Parse("<div id=d style='width: 300px'>A <b id=b>B &#x5d0;</b>&#x5d1; C</div>");
        var fragments = GetFragments(document, "b");
        Assert.Equal(2, fragments.Count);
        Assert.Equal(20, fragments[0].GetRectInRoot().X);
        Assert.True(fragments[0].GetRectInRoot().Right < fragments[1].GetRectInRoot().X);
        Assert.True(fragments[0].IsFirstFragment);
        Assert.False(fragments[0].IsLastFragment);
        Assert.False(fragments[1].IsFirstFragment);
        Assert.True(fragments[1].IsLastFragment);
    }

    [Theory]
    [InlineData("<span id=s style='unicode-bidi: isolate'>!!</span>")]
    [InlineData("<bdi id=s>!!</bdi>")]
    [InlineData("<bdi id=s dir=rtl>!!</bdi>")]
    public void Bidi_IsolateAfterOppositeStrongCharacterAtTheEndOfTheParagraph(string isolate)
    {
        // The isolate initiator and the PDI are neutrals between L ("XXX") and eos (R, the paragraph level): they resolve to R
        // (rules N1 and N2 of UAX #9), so the isolate is on the left of "XXX"
        var document = Parse($"<div id=d dir=rtl style='width: 300px'>XXX{isolate}</div>");
        Assert.Equal(new PhysicalRect(250, 0, 20, 10), GetRect(document, "s"));
        Assert.Equal(270, GetTextFragments(document, "d")[0].GetRectInRoot().X);
    }

    [Fact]
    public void InlineBox_LargerFont()
    {
        var document = Parse("<div id=d>X<span id=s style='font-size: 20px'>Y</span></div>");
        Assert.Equal(20, GetRect(document, "d").Height);
        Assert.Equal(new PhysicalRect(10, 0, 20, 20), GetRect(document, "s"));
        Assert.Equal(8, GetTextFragments(document, "d")[0].GetRectInRoot().Y);
    }

    [Fact]
    public void Bidi_RightToLeftInlineBoxEdges()
    {
        // In a right-to-left inline box, the inline-start edges are on the right
        var document = Parse("<div id=d style='width: 100px'><span id=s style='direction: rtl; unicode-bidi: isolate; padding-left: 2px; padding-right: 5px'>XX</span></div>");
        var fragment = Assert.Single(GetFragments(document, "s"));
        Assert.Equal(new PhysicalRect(0, 0, 27, 10), fragment.GetRectInRoot());
        Assert.Equal(new Sides<double>(0, 5, 0, 2), fragment.Padding);
    }

    [Fact]
    public void AtomicInline_InlineBlock_LastBaselineAndShrinkToFit()
    {
        var document = Parse("<div id=d>X<span id=s style='display: inline-block'>AB<br>C</span>Y</div>");
        Assert.Equal(new PhysicalRect(10, 0, 20, 20), GetRect(document, "s"));
        Assert.Equal(new PhysicalRect(0, 10, 10, 10), GetTextFragments(document, "d")[0].GetRectInRoot());
        Assert.Equal(20, GetRect(document, "d").Height);
    }

    [Fact]
    public void AtomicInline_ShrinkToFitIsLimitedByTheAvailableSpace()
    {
        var document = Parse("<div style='width: 45px'><span id=s style='display: inline-block'>XX XX XX</span></div>");
        Assert.Equal(new PhysicalRect(0, 0, 45, 30), GetRect(document, "s"));
    }

    [Fact]
    public void AtomicInline_ScrollContainerBaselineIsTheBottomMarginEdge()
    {
        var document = Parse("<div id=d>X<span id=s style='display: inline-block; overflow: hidden; margin-bottom: 5px'>A<br>B</span></div>");
        Assert.Equal(new PhysicalRect(10, 0, 10, 20), GetRect(document, "s"));
        Assert.Equal(17, GetTextFragments(document, "d")[0].GetRectInRoot().Y);
    }

    [Fact]
    public void AtomicInline_BreakOpportunitiesAround()
    {
        // There is a soft wrap opportunity before an atomic inline, even after a no-break space
        var document = Parse("<div id=d style='width: 25px'>XX&nbsp;<img id=i style='width: 10px; height: 10px'></div>");
        Assert.Equal(new PhysicalRect(0, 10, 10, 10), GetRect(document, "i"));
    }

    [Fact]
    public void OutsideMarker_NextToTheFirstLine()
    {
        var document = Parse("""
            <ul style='margin: 0; padding-left: 40px'>
              <li id=a>item</li>
              <li id=b><div style='padding-top: 5px'>block</div></li>
              <li id=c></li>
            </ul>
            """);
        var engine = document.GetLayoutEngine();
        Assert.Equal(new PhysicalRect(20, 0, 20, 10), Assert.Single(engine.GetFragments(document.GetElementById("a")!, "marker")).GetRectInRoot());
        Assert.Equal(new PhysicalRect(20, 15, 20, 10), Assert.Single(engine.GetFragments(document.GetElementById("b")!, "marker")).GetRectInRoot());

        // A list item without line box is as tall as its marker
        Assert.Equal(new PhysicalRect(20, 25, 20, 10), Assert.Single(engine.GetFragments(document.GetElementById("c")!, "marker")).GetRectInRoot());
        Assert.Equal(10, GetRect(document, "c").Height);
    }

    [Fact]
    public void OutsideMarker_RightToLeft()
    {
        var document = Parse("<ul dir=rtl style='margin: 0; padding-right: 40px; width: 100px'><li id=a>item</li></ul>");
        var marker = Assert.Single(document.GetLayoutEngine().GetFragments(document.GetElementById("a")!, "marker"));
        Assert.Equal(new PhysicalRect(100, 0, 20, 10), marker.GetRectInRoot());
    }

    [Theory]
    [InlineData("auto", 30)]
    [InlineData("first", 8)]
    [InlineData("last", 28)]
    public void AtomicInline_BaselineSourceOfAScrollContainer(string baselineSource, double baseline)
    {
        // https://drafts.csswg.org/css-inline-3/#baseline-source: the block-end margin edge is only the baseline of an
        // inline-block that scrolls when its baseline-source is auto; first and last use the baselines of its contents
        // (three lines of 10px: the baselines are at 8px, 18px and 28px; the baseline of the square is its block-end edge)
        var document = Parse($"<div id=d style='line-height: 0'><span id=s style='display: inline-block; width: 10px; height: 10px'></span><div id=b style='display: inline-block; overflow: hidden; line-height: 1; baseline-source: {baselineSource}'>X<br>X<br>X</div></div>");
        var block = GetRect(document, "b");
        var square = GetRect(document, "s");
        Assert.Equal(30, block.Height);
        Assert.Equal(baseline, square.Y + 10 - block.Y);
    }

    [Theory]
    [InlineData("auto", 8)]
    [InlineData("first", 8)]
    [InlineData("last", 38)]
    public void AtomicInline_BaselineSourceWithABlockLevelTable(string baselineSource, double baseline)
    {
        // A block-level table gives no last baseline to an inline-block whose baseline-source is auto (the last line box is
        // the one before the table); with last, the baseline is the one of the last row of the table
        var document = Parse($"<div id=d style='line-height: 0'><span id=s style='display: inline-block; width: 10px; height: 10px'></span><div id=b style='display: inline-block; line-height: 1; baseline-source: {baselineSource}'>X<table style='border-spacing: 0'><tr><td style='padding: 0; vertical-align: baseline'>X</td></tr><tr><td style='padding: 0; vertical-align: baseline'>X</td></tr><tr><td style='padding: 0; vertical-align: baseline'>X</td></tr></table></div></div>");
        var block = GetRect(document, "b");
        var square = GetRect(document, "s");
        Assert.Equal(40, block.Height);
        Assert.Equal(baseline, square.Y + 10 - block.Y);
    }

    [Fact]
    public void AtomicInline_TextAreaHasNoLastBaseline()
    {
        // As in Gecko and WebKit: baseline-source: last aligns a textarea by the block-end edge of its margin box
        var document = Parse("<div id=d style='line-height: 0'><span id=s style='display: inline-block; width: 10px; height: 10px'></span><textarea id=t style='margin: 0 0 3px; border: 0; padding: 0; height: 40px; font: inherit; line-height: 1; baseline-source: last'>X</textarea></div>");
        var area = GetRect(document, "t");
        var square = GetRect(document, "s");
        Assert.Equal(area.Y + 43, square.Y + 10);
    }

    [Fact]
    public void OutsideMarker_AlignedWithTheFirstBaselineOfTheContent()
    {
        // No line box of the block formatting context of the list item places the marker: it is aligned with the first
        // baseline of the content, next to the content box of the block container that has this baseline
        var document = Parse("""
            <ul style='margin: 0; padding-left: 40px'>
              <li id=a><div style='display: flex; align-items: flex-end; height: 50px'><span>flex</span></div></li>
              <li id=b><div style='border-left: 3px solid'><div style='display: grid; grid-template-rows: 30px; align-items: end'><div>grid</div></div></div></li>
              <li id=c><div id=clip style='overflow: hidden; padding-top: 5px'>clip</div></li>
            </ul>
            """);
        var engine = document.GetLayoutEngine();
        Assert.Equal(new PhysicalRect(20, 40, 20, 10), Assert.Single(engine.GetFragments(document.GetElementById("a")!, "marker")).GetRectInRoot());
        Assert.Equal(new PhysicalRect(23, 70, 20, 10), Assert.Single(engine.GetFragments(document.GetElementById("b")!, "marker")).GetRectInRoot());

        // The marker is not a descendant of a box that clips its overflow
        var marker = Assert.Single(engine.GetFragments(document.GetElementById("c")!, "marker"));
        Assert.Equal(new PhysicalRect(20, 85, 20, 10), marker.GetRectInRoot());
        Assert.DoesNotContain(marker, Assert.Single(engine.GetFragments(document.GetElementById("clip")!)).Children);
    }

    [Fact]
    public void InsideMarker_IsAnInlineBox()
    {
        var document = Parse("<ul style='margin: 0; padding: 0; list-style-position: inside'><li id=a>item</li></ul>");
        Assert.Equal(new PhysicalRect(20, 0, 40, 10), GetTextFragments(document, "a")[0].GetRectInRoot());
    }

    [Fact]
    public void FirstLine_StyleAppliesToTheFirstLine()
    {
        var document = Parse("<style>#d::first-line { font-size: 20px }</style><div id=d style='width: 50px'>XX XX XX</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(new PhysicalRect(0, 0, 40, 20), texts[0].GetRectInRoot());
        Assert.Equal(new PhysicalRect(0, 20, 50, 10), texts[1].GetRectInRoot());
        Assert.Equal(30, GetRect(document, "d").Height);
    }

    [Fact]
    public void FirstLine_InheritedByTheInlineBoxes()
    {
        var document = Parse("<style>#d::first-line { font-size: 20px }</style><div id=d><span id=s>X</span><br><span id=t>X</span></div>");
        Assert.Equal(new PhysicalRect(0, 0, 20, 20), GetRect(document, "s"));
        Assert.Equal(new PhysicalRect(0, 20, 10, 10), GetRect(document, "t"));
    }

    [Fact]
    public void FirstLine_OfTheFirstChildBlock()
    {
        var document = Parse("<style>#d::first-line { font-size: 20px }</style><div id=d><p id=p style='margin: 0'>X</p></div>");
        Assert.Equal(20, GetRect(document, "p").Height);
    }

    [Fact]
    public void EmptyLines_HaveNoHeightAndCollapseThrough()
    {
        var document = Parse("""
            <div style='height: 10px; margin-bottom: 10px'></div>
            <div id=e> <span> </span> </div>
            <div id=n style='margin-top: 10px'>X</div>
            """);
        Assert.Equal(0, GetRect(document, "e").Height);
        Assert.Equal(20, GetRect(document, "n").Y);
    }

    [Fact]
    public void Lines_SeparateTheMargins()
    {
        var document = Parse("<div id=a style='margin-top: 10px'><div id=b style='margin-top: 20px'>X</div></div><div id=c style='margin-top: 5px'>X</div>");
        Assert.Equal(20, GetRect(document, "a").Y);
        Assert.Equal(35, GetRect(document, "c").Y);
    }

    [Fact]
    public void Lines_ContentFitsWithinTheLayoutUnitOfTheBrowsers()
    {
        // Six inline-blocks of 16.66666667% are a little wider than their container, and fit on one line in the browsers, whose
        // sizes are multiples of 1/64px
        var document = Parse("<div style='width: 1132px; font-size: 0'>" + string.Concat(Enumerable.Range(0, 6).Select(i => $"<div id=i{i} style='display: inline-block; width: 16.66666667%; height: 10px'></div>")) + "</div>");
        Assert.Equal(0, GetRect(document, "i5").Y);
    }

    [Theory]
    [InlineData("<!DOCTYPE html>", 22)]
    [InlineData("", 20)]
    public void Quirks_LineHeightCalculation(string doctype, double height)
    {
        // https://quirks.spec.whatwg.org/#the-line-height-calculation-quirk: the strut of a line without text has no height
        var document = Load(doctype + "<style>body { margin: 0; font: 10px/1 Ahem }</style><div id=d><img style='width: 10px; height: 20px'></div>");
        Assert.Equal(height, GetRect(document, "d").Height);
    }

    [Theory]
    [InlineData("<!DOCTYPE html>", "<span></span>", 0)] // An empty line
    [InlineData("<!DOCTYPE html>", "<span style='margin: 0 1px'></span>", 10)]
    [InlineData("", "<span style='margin: 0 1px'></span>", 0)] // The margins do not prevent the quirk (as in the browsers)
    [InlineData("<!DOCTYPE html PUBLIC \"-//W3C//DTD HTML 4.01 Transitional//EN\" \"http://www.w3.org/TR/html4/loose.dtd\">", "<span style='margin: 0 1px'></span>", 0)] // Limited-quirks mode
    [InlineData("", "<span style='padding-left: 1px'></span>", 10)]
    [InlineData("", "<span style='margin: 0 1px; padding: 1px 0'></span>", 0)]
    [InlineData("", "<span style='font-size: 5px'>X</span>", 5)] // The blocks ignore line-height quirk: the strut of the block has no text
    [InlineData("", "<span style='font-size: 5px'>X</span><br><span style='font-size: 5px'>X</span>", 10)] // A forced line break is not a text
    [InlineData("", "<br>", 10)] // Except on a line without text
    [InlineData("", "<img style='width: 10px; height: 3px; vertical-align: top'><br><img style='width: 10px; height: 3px; vertical-align: top'>", 6)] // That has no atomic inline either
    [InlineData("", "<li style='list-style: none'><img style='width: 10px; height: 3px; vertical-align: top'></li>", 10)] // The quirks do not apply to the lines of a list item
    [InlineData("", "<img style='width: 10px; height: 3px; vertical-align: top'>", 3)]
    [InlineData("", "<span style='white-space: pre'>a\n\nb</span>", 30)] // A box that contains preserved white space is not subject to the quirk: the empty line of a preserved segment break
    [InlineData("", "<span style='white-space: pre; font-size: 5px'>a\n\nb</span>", 15)]
    [InlineData("<!DOCTYPE html>", "<span style='white-space: pre; font-size: 20px'>a\n\nb</span>", 60)] // The span has a fragment on its empty line
    public void Quirks_LineHeightCalculation_EmptyBoxes(string doctype, string html, double height)
    {
        var document = Load(doctype + "<style>body { margin: 0; font: 10px/1 Ahem }</style><div id=d>" + html + "</div>");
        Assert.Equal(height, GetRect(document, "d").Height);
    }

    [Theory]
    [InlineData("uppercase", "ß", 20)]
    [InlineData("full-width", "a b", 30)]
    [InlineData("none", "ß", 10)]
    public void TextTransform(string transform, string text, double width)
    {
        var document = Parse($"<div id=d style='text-transform: {transform}'>{text}</div>");
        Assert.Equal(width, Assert.Single(GetTextFragments(document, "d")).Size.Width);
    }

    [Fact]
    public void WordBreakOpportunity()
    {
        var document = Parse("<div id=d style='width: 35px'>XXX<wbr>XXX</div>");
        Assert.Equal(20, GetRect(document, "d").Height);
    }

    [Fact]
    public void Relayout_IsStable()
    {
        var document = Parse("<div id=d style='width: 55px'>XX <b>XX</b> <i dir=rtl>XX</i> XX</div>");
        var engine = document.GetLayoutEngine();
        var first = engine.Dump();
        engine.SetNeedsLayout();
        Assert.Equal(first, engine.Dump());
        Assert.Equal(2, engine.LayoutCount);
    }

    [Fact]
    public void VerticalWritingMode_Lines()
    {
        var document = Parse("<div id=d style='writing-mode: vertical-rl; height: 25px'>XX XX</div>");
        Assert.Equal(new PhysicalRect(0, 0, 20, 25), GetRect(document, "d"));
        var texts = GetTextFragments(document, "d");
        Assert.Equal(10, texts[0].GetRectInRoot().X);
        Assert.Equal(0, texts[1].GetRectInRoot().X);
    }

    [Fact]
    public void LineWithInlineBoxEdgesThatAddUpToZero_IsNotAZeroHeightLine()
    {
        // The line after the block has the border and the negative margin of the end edge of the inline box
        var document = Parse("""
            <div id=c style='width: 200px'><span style='font-size: 50px; border-right: 200px solid; margin-right: -200px'><div></div></span></div>
            """);
        Assert.Equal(50, GetRect(document, "c").Height);
    }

    [Fact]
    public void InlineBlockBaseline_ScrollContainerChild_IsItsBlockEndMarginEdge()
    {
        // The baseline of an inline-block is the one of its last line box, or the bottom margin edge of a child that is a scroll container
        var document = Parse("""
            <div><div id=a style='display: inline-block'><div style='width: 30px; height: 30px; overflow: hidden; margin-bottom: 5px'>X</div></div><span id=s>X</span></div>
            <div><div id=b style='display: inline-block; baseline-source: last'><div style='width: 30px; height: 30px; overflow: hidden'>X</div></div><span id=t>X</span></div>
            """);

        // The baseline of the 10px Ahem text is 8px below its top
        Assert.Equal(GetRect(document, "a").Y + 35 - 8, GetRect(document, "s").Y);
        Assert.Equal(GetRect(document, "b").Y, GetRect(document, "t").Y);
    }

    [Fact]
    public void InlineFlexScrollContainer_IsAlignedByTheBaselineOfItsContents()
    {
        // Only an inline-block scroll container is aligned by its bottom margin edge
        var document = Parse("<div id=d>X<span id=s style='display: inline-flex; overflow: hidden; height: 30px'>X</span><span id=t style='display: inline-block; overflow: hidden; height: 30px'>X</span></div>");
        Assert.Equal(22, GetRect(document, "s").Y);
        Assert.Equal(0, GetRect(document, "t").Y);
        Assert.Equal(52, GetRect(document, "d").Height);
    }

    [Fact]
    public void NegativeInlineEndMargin_DoesNotBringBackTheContentThatOverflows()
    {
        // WPT css/css-grid/firefox-bug-1881495.html: as in the browsers, the last word (which ends at 30px) does not fit in the
        // 29px of the line, although the negative margin of its inline box makes the content 29px wide
        var document = Parse("<div id=d style='width: 29px'>X <span style='margin-right: -1px'>X</span></div><div id=e style='width: 30px'>X <span style='margin-right: -1px'>X</span></div>");
        Assert.Equal(20, GetRect(document, "d").Height);
        Assert.Equal(10, GetRect(document, "e").Height);
    }

    [Fact]
    public void Float_DeferredBelowALineThatIsBrokenBeforeItIsAFloatOfTheNextLine()
    {
        // WPT css/CSS2/floats/floats-line-wrap-shifted-001.html: the float does not fit beside "1111 2222" and is deferred below the
        // line, then "3333" makes the line break before the nobr element: the float is below the second line, not between the lines
        var document = Parse("<div id=d style='width: 120px'>1111 <nobr><span id=s>2222</span> <div id=f style='float: left; width: 120px; height: 10px'></div>3333</nobr></div>");
        Assert.Equal(10, GetRect(document, "s").Y);
        Assert.Equal(20, GetRect(document, "f").Y);
    }

    [Fact]
    public void FirstLetter_InheritsFromTheBeforePseudoElementThatContainsIt()
    {
        // WPT css/CSS2/selectors/before-first-letter-selector-001.xht: the letter has the font size of the ::before pseudo-element
        var document = Parse("<style>#d::before { content: 'Ab'; font-size: 30px } #d::first-letter { color: green }</style><div id=d style='display: inline-block'></div>");
        Assert.Equal(60, GetRect(document, "d").Width);
    }

    [Fact]
    public void FloatingRootElement_IsAtTheEdgeOfTheInitialContainingBlock()
    {
        // WPT css/CSS2/floats/float-root.html
        var document = Load("<!DOCTYPE html><style>:root { float: right } body { margin: 0; font: 10px/1 Ahem }</style><p id=p style='margin: 0'>foo</p>");
        Assert.Equal(new PhysicalRect(770, 0, 30, 10), GetRect(document, "p"));
    }

    [Fact]
    public void OutsideMarker_IsBesideTheFloatThatShortensItsLine()
    {
        // WPT css/CSS2/lists/list-style-applies-to-017.html: the markers are next to the line boxes, which start after the float
        var document = Parse("""
            <div style='float: left; width: 96px; height: 96px'></div>
            <div id=a style='display: list-item; list-style: square'>text</div>
            <div id=b style='display: list-item; list-style: square'></div>
            """);
        var first = GetFragments(document, "a")[0].Children.OfType<BoxFragment>().Single(f => f.Box.IsOutsideMarker);
        var second = GetFragments(document, "b")[0].Children.OfType<BoxFragment>().Single(f => f.Box.IsOutsideMarker);
        Assert.Equal(96, first.Offset.X + first.Size.Width, 3);
        Assert.Equal(96, second.Offset.X + second.Size.Width, 3);
    }

    [Fact]
    public void NegativeMarginOfAnAtomicInline_BringsBackOnlyTheContentThatCannotBeBrokenEarlier()
    {
        // WPT css/CSS2/linebox/inline-negative-margin-001.html: the second box overflows the line, which is broken before it
        // (the negative margin of the third box does not bring it back); the first box of the second container overflows
        // alone, and the negative margin of the box after it makes the line fit
        var document = Parse("""
            <style>b { display: inline-block; height: 6px }</style>
            <div id=d style='width: 40px'><b style='width: 30px'></b><b style='width: 20px'></b><b style='width: 10px; margin-left: -20px'></b></div>
            <div id=e style='width: 40px'><b style='width: 50px'></b><b style='width: 10px; margin-left: -20px'></b></div>
            """);
        Assert.Equal(20, GetRect(document, "d").Height);
        Assert.Equal(10, GetRect(document, "e").Height);
    }

    [Fact]
    public void ShrinkToFit_LayoutOfTheMeasuredItems_IsTheLayoutOfTheItems()
    {
        // The layout of a float reuses the items collected to measure it: it is the same as the layout of a block of its width
        const string Content = "XX&shy;XXX <span style='white-space: nowrap'>X X</span><span id=i1 style='display: inline-block; width: 15px; height: 5px'></span>"
            + "<span id=i2 style='display: inline-block; width: 5px; height: 5px'></span> XX&nbsp;X <span style='white-space: pre-wrap'>X\tX</span>"
            + " <span style='overflow-wrap: anywhere'>XXXXXXXXXX</span>   XX\n  XX";
        var floated = Parse($"<div style='width: 75px; letter-spacing: 1px'><div id=d style='float: left'>{Content}</div></div>");
        var width = GetRect(floated, "d").Width;
        var block = Parse($"<div style='width: 75px; letter-spacing: 1px'><div id=d style='width: {width.ToString(CultureInfo.InvariantCulture)}px'>{Content}</div></div>");
        Assert.Equal(GetLayout(block), GetLayout(floated));

        static List<string> GetLayout(Document document)
        {
            var origin = GetRect(document, "d");
            var result = new List<string> { origin.Size.ToString(), GetRect(document, "i1").ToString(), GetRect(document, "i2").ToString() };
            foreach (var text in GetTextFragments(document, "d"))
            {
                var rect = text.GetRectInRoot();
                result.Add($"{text.Start}-{text.End}: {rect.X - origin.X},{rect.Y - origin.Y} {rect.Width}x{rect.Height}");
            }

            return result;
        }
    }

    [Fact]
    public void PlainText_MandatoryBreakCharacters()
    {
        var document = Parse("<div id=d style='width: 200px'>XX\u2028XX\u2029XX</div>");
        Assert.Equal(30, GetRect(document, "d").Height);
    }

    [Fact]
    public void PlainText_RightToLeftCharactersInsideAWord()
    {
        var document = Parse("<div id=d style='width: 200px'>XX\u05D0\u05D1XX</div>");
        var texts = GetTextFragments(document, "d");
        Assert.Equal(3, texts.Count);
        Assert.True(texts.Single(text => text.Start == 2).ShapedText!.IsRightToLeft);
    }
}
