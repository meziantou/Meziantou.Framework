using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

public sealed class BoxTreeTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html, bool scripting = false) => _contexts.Create(new BrowsingContextOptions { ScriptingEnabled = scripting, Viewport = new ViewportOptions { Width = 800, Height = 600 } }).Navigate(_ => html);

    /// <summary>Builds the box tree of the document and returns the dump of the body box (or of the viewport when there is no body box).</summary>
    private string DumpBody(string html, bool scripting = false)
    {
        var document = Parse("<!DOCTYPE html><style>body { margin: 0 }</style>" + html, scripting);
        var tree = BoxTree.Build(document);
        var body = document.Body is { } b ? tree.GetPrincipalBox(b) : null;
        return body is null ? tree.Dump() : BoxTree.Dump(body);
    }

    private void AssertBody(string html, string expected, bool scripting = false) => Assert.Equal(expected.ReplaceLineEndings("\n").TrimEnd('\n') + "\n", DumpBody(html, scripting));

    [Fact]
    public void Viewport_RootElement()
    {
        var document = Parse("<!DOCTYPE html><html><body>a</body></html>");
        Assert.Equal("""
            viewport
              block <html> block-children
                block <body> inline-children
                  text "a"

            """.ReplaceLineEndings("\n"), BoxTree.Build(document).Dump());
    }

    [Fact]
    public void NoBrowsingContext_EmptyTree()
    {
        var document = HtmlDocumentParser.ParseDocument("<p>a</p>");
        var tree = BoxTree.Build(document);
        Assert.Null(tree.Viewport);
        Assert.Equal("", tree.Dump());
    }

    [Fact]
    public void BlockInInline() => AssertBody("<span id=s>a<div id=d>b</div>c</span>", """
        block <body> block-children
          block anonymous inline-children
            inline <span#s>
              text "a"
          block <div#d> inline-children
            text "b"
          block anonymous inline-children
            inline <span#s> continuation
              text "c"
        """);

    [Fact]
    public void BlockInInline_Nested() => AssertBody("<span id=a>1<em id=b>2<p id=p>3</p>4</em>5</span>", """
        block <body> block-children
          block anonymous inline-children
            inline <span#a>
              text "1"
              inline <em#b>
                text "2"
          block <p#p> inline-children
            text "3"
          block anonymous inline-children
            inline <span#a> continuation
              inline <em#b> continuation
                text "4"
              text "5"
        """);

    [Fact]
    public void BlockInInline_EmptyParts() => AssertBody("<span id=s><div id=d1>a</div> <div id=d2>b</div></span>", """
        block <body> block-children
          block anonymous inline-children
            inline <span#s>
          block <div#d1> inline-children
            text "a"
          block <div#d2> inline-children
            text "b"
          block anonymous inline-children
            inline <span#s> continuation
        """);

    [Fact]
    public void AnonymousBlocks_WhiteSpaceBetweenBlocks() => AssertBody("\n<div id=a>a</div>\n text \n<div id=b>b</div>\n", """
        block <body> block-children
          block <div#a> inline-children
            text "a"
          block anonymous inline-children
            text "\n text \n"
          block <div#b> inline-children
            text "b"
        """);

    [Fact]
    public void AnonymousBlocks_PreservedWhiteSpace() => AssertBody("<div id=a>a</div><span style='white-space: pre'> </span><div id=b>b</div>", """
        block <body> block-children
          block <div#a> inline-children
            text "a"
          block anonymous inline-children
            inline <span>
              text " "
          block <div#b> inline-children
            text "b"
        """);

    [Fact]
    public void OutOfFlow_BetweenBlocks() => AssertBody("<div id=a></div> <div id=f style='float: left'>f</div> <div id=b></div>", """
        block <body> block-children
          block <div#a>
          block <div#f> inline-children float
            text "f"
          block <div#b>
        """);

    [Fact]
    public void OutOfFlow_InInlineContent() => AssertBody("a<div id=f style='position: absolute'>f</div>b", """
        block <body> inline-children
          text "a"
          block <div#f> inline-children absolute
            text "f"
          text "b"
        """);

    [Fact]
    public void DisplayNone_And_Contents() => AssertBody("<div id=n style='display: none'>x</div><div id=c style='display: contents'>a<span>b</span></div>", """
        block <body> inline-children
          text "a"
          inline <span>
            text "b"
        """);

    [Fact]
    public void DisplayContents_PseudoElements() => AssertBody("<style>#c::before { content: 'B' } #c::after { content: 'A'; display: block }</style><div id=c style='display: contents'>x</div>", """
        block <body> block-children
          block anonymous inline-children
            inline <div#c>::before
              generated-text "B"
            text "x"
          block <div#c>::after inline-children
            generated-text "A"
        """);

    [Fact]
    public void PseudoElements_BeforeAfter() => AssertBody("<style>#p::before { content: 'a' counter(c) } #p::after { content: url(x.png) ' z' } #p { counter-reset: c 3 }</style><p id=p>t</p>", """
        block <body> block-children
          block <p#p> inline-children
            inline <p#p>::before
              generated-text "a3"
            text "t"
            inline <p#p>::after
              replaced inline <p#p>::after url("x.png")
              generated-text " z"
        """);

    [Fact]
    public void PseudoElements_NoneContent_NotGenerated() => AssertBody("<style>#p::before { color: red } #p::after { content: none }</style><p id=p>t</p>", """
        block <body> block-children
          block <p#p> inline-children
            text "t"
        """);

    [Theory]
    [InlineData("fr", "\u00AB", "\u00AB")]
    [InlineData("fr-CH", "\u00AB", "\u2039")]
    [InlineData("de-DE", "\u201E", "\u201A")]
    [InlineData("ja", "\u300C", "\u300E")]
    [InlineData("zz", "\u201C", "\u2018")]
    public void Quotes_AutoUsesTheQuotationMarksOfTheLanguage(string language, string open, string nestedOpen)
    {
        // https://drafts.csswg.org/css-content-3/#valdef-quotes-auto: the marks of the longest prefix of the language tag (CLDR)
        Assert.Equal([open, nestedOpen], Meziantou.Framework.WebPlatform.Css.Layout.LanguageQuotes.Get(language)[..3].Where((_, index) => index != 1));
    }

    [Fact]
    public void Quotes() => AssertBody("<q>a<q>b</q></q><q style='quotes: \"<\" \">\"'>c</q>", """
        block <body> inline-children
          inline <q>
            inline <q>::before
              generated-text "\201C"
            text "a"
            inline <q>
              inline <q>::before
                generated-text "\2018"
              text "b"
              inline <q>::after
                generated-text "\2019"
            inline <q>::after
              generated-text "\201D"
          inline <q>
            inline <q>::before
              generated-text "<"
            text "c"
            inline <q>::after
              generated-text ">"
        """);

    [Fact]
    public void ListItems_Markers() => AssertBody("<ol><li id=a>a</li><li id=b style='list-style-position: inside'><p>b</p></li></ol><ul><li id=c>c</li></ul><ul style='list-style: none'><li id=d>d</li></ul>", """
        block <body> block-children
          block <ol> block-children
            block list-item <li#a> inline-children
              inline-block <li#a>::marker inline-children outside
                generated-text "1. "
              text "a"
            block list-item <li#b> block-children
              block anonymous inline-children
                inline <li#b>::marker
                  generated-text "2. "
              block <p> inline-children
                text "b"
          block <ul> block-children
            block list-item <li#c> inline-children
              inline-block <li#c>::marker inline-children outside
                generated-text "\2022 "
              text "c"
          block <ul> block-children
            block list-item <li#d> inline-children
              text "d"
        """);

    [Fact]
    public void ListItems_OutsideMarkerWithBlocks() => AssertBody("<ul><li id=a><p>a</p></li></ul>", """
        block <body> block-children
          block <ul> block-children
            block list-item <li#a> block-children
              inline-block <li#a>::marker inline-children outside
                generated-text "\2022 "
              block <p> inline-children
                text "a"
        """);

    [Fact]
    public void ListItems_MarkerContent() => AssertBody("<style>li::marker { content: '-' }</style><ul><li>a</li></ul>", """
        block <body> block-children
          block <ul> block-children
            block list-item <li> inline-children
              inline-block <li>::marker inline-children outside
                generated-text "-"
              text "a"
        """);

    [Fact]
    public void ListItems_PseudoElementThatIsAListItemHasAMarker() => AssertBody("<style>div::before { content: 'a'; display: list-item }</style><div id=d>b</div>", """
        block <body> block-children
          block <div#d> block-children
            block list-item <div#d>::before inline-children
              inline-block <div#d>::before inline-children outside
                generated-text "\2022 "
              generated-text "a"
            block anonymous inline-children
              text "b"
        """);

    [Fact]
    public void ListItems_MarkerOfAPseudoElementIsStyled() => AssertBody("<style>div::before { content: 'a'; display: list-item; list-style-position: inside } div::before::marker { content: 'm' }</style><div id=d>b</div>", """
        block <body> block-children
          block <div#d> block-children
            block list-item <div#d>::before inline-children
              inline <div#d>::before
                generated-text "m"
              generated-text "a"
            block anonymous inline-children
              text "b"
        """);

    [Fact]
    public void Counters_PseudoElementWithoutBoxDoesNotChangeTheCounters() => AssertBody("<style>#d::before { content: 'x'; display: none; counter-increment: c 10 } p::before { content: counter(c) }</style><div id=d><p></p></div>", """
        block <body> block-children
          block <div#d> block-children
            block <p> inline-children
              inline <p>::before
                generated-text "0"
        """);

    [Fact]
    public void Counters_ReversedInitialValueCountsPseudoElements() => AssertBody("<style>#d { counter-reset: reversed(c); counter-increment: c -1 } #d::before { content: counter(c); counter-increment: c -2 } p::after { content: counter(c); counter-increment: c -3 }</style><div id=d><p></p></div>", """
        block <body> block-children
          block <div#d> block-children
            block anonymous inline-children
              inline <div#d>::before
                generated-text "6"
            block <p> inline-children
              inline <p>::after
                generated-text "3"
        """);

    [Fact]
    public void FirstLetter() => AssertBody("<style>p::first-letter { color: red }</style><p id=p> \"Hello</p><p id=q><span>Wo</span>rld</p>", """
        block <body> block-children
          block <p#p> inline-children
            text " " [0, 1)
            inline <p#p>::first-letter
              text "\"H" [1, 3)
            text "ello" [3, 7)
          block <p#q> inline-children
            inline <span>
              inline <p#q>::first-letter
                text "W" [0, 1)
              text "o" [1, 2)
            text "rld"
        """);

    [Fact]
    public void FirstLetter_InDescendantBlockAndBefore() => AssertBody("<style>div::first-letter { color: red } p::before { content: 'xy' }</style><div id=d><p>ab</p></div>", """
        block <body> block-children
          block <div#d> block-children
            block <p> inline-children
              inline <p>::before
                inline <div#d>::first-letter
                  generated-text "x" ::before
                generated-text "y"
              text "ab"
        """);

    [Fact]
    public void FirstLetter_Float() => AssertBody("<style>p::first-letter { float: left }</style><p>ab</p>", """
        block <body> block-children
          block <p> inline-children
            block <p>::first-letter inline-children float
              text "a" [0, 1)
            text "b" [1, 2)
        """);

    [Fact]
    public void FirstLetter_AtomicInlineFirst_NoBox() => AssertBody("<style>p::first-letter { color: red }</style><p><img>ab</p>", """
        block <body> block-children
          block <p> inline-children
            replaced inline <img>
            text "ab"
        """);

    [Fact]
    public void FirstLine_Style()
    {
        var document = Parse("<!DOCTYPE html><style>p::first-line { color: red }</style><p id=p>a</p><div id=d>b</div>");
        var tree = BoxTree.Build(document);
        Assert.NotNull(tree.GetPrincipalBox(document.GetElementById("p")!)!.FirstLineStyle);
        Assert.Null(tree.GetPrincipalBox(document.GetElementById("d")!)!.FirstLineStyle);
    }

    [Fact]
    public void Table_Elements() => AssertBody("<table id=t>\n <caption>c</caption>\n <tr><td>a</td> <td>b</td></tr>\n</table>", """
        block <body> block-children
          table-wrapper block <table#t>
            table-caption <caption> inline-children
              text "c"
            table <table#t>
              table-row-group <tbody>
                table-row <tr>
                  table-cell <td> inline-children
                    text "a"
                  table-cell <td> inline-children
                    text "b"
        """);

    [Fact]
    public void Table_CaptionSideBottom() => AssertBody("<table><caption style='caption-side: bottom'>c</caption><tr><td>a</td></tr></table>", """
        block <body> block-children
          table-wrapper block <table>
            table <table>
              table-row-group <tbody>
                table-row <tr>
                  table-cell <td> inline-children
                    text "a"
            table-caption <caption> inline-children
              text "c"
        """);

    [Fact]
    public void Table_MissingParents() => AssertBody("<div id=r style='display: table-row'><div style='display: table-cell'>a</div><span>b</span></div>", """
        block <body> block-children
          table-wrapper block anonymous
            table anonymous
              table-row <div#r>
                table-cell <div> inline-children
                  text "a"
                table-cell anonymous inline-children
                  inline <span>
                    text "b"
        """);

    [Fact]
    public void Table_CellsInBlock() => AssertBody("x<div style='display: table-cell'>a</div> <div style='display: table-cell'>b</div>y", """
        block <body> block-children
          block anonymous inline-children
            text "x"
          table-wrapper block anonymous
            table anonymous
              table-row anonymous
                table-cell <div> inline-children
                  text "a"
                table-cell <div> inline-children
                  text "b"
          block anonymous inline-children
            text "y"
        """);

    [Fact]
    public void Table_CellsInInline_InlineTable() => AssertBody("<span>x<b style='display: table-cell'>a</b>y</span>", """
        block <body> inline-children
          inline <span>
            text "x"
            table-wrapper inline anonymous
              table anonymous
                table-row anonymous
                  table-cell <b> inline-children
                    text "a"
            text "y"
        """);

    [Fact]
    public void Table_ChildWrappers() => AssertBody("<div id=t style='display: table'>text<div style='display: table-row-group'><span>r</span></div></div>", """
        block <body> block-children
          table-wrapper block <div#t>
            table <div#t>
              table-row anonymous
                table-cell anonymous inline-children
                  text "text"
              table-row-group <div>
                table-row anonymous
                  table-cell anonymous inline-children
                    inline <span>
                      text "r"
        """);

    [Fact]
    public void Table_Columns() => AssertBody("<div style='display: table'><div id=g style='display: table-column-group'>x<div id=c style='display: table-column'>y</div></div><div style='display: table-cell'>a</div></div>", """
        block <body> block-children
          table-wrapper block <div>
            table <div>
              table-column-group <div#g>
                table-column <div#c>
              table-row anonymous
                table-cell <div> inline-children
                  text "a"
        """);

    [Fact]
    public void Flex_AnonymousItems() => AssertBody("<div id=f style='display: flex'> a <span>b</span> \n <div style='position: absolute'>c</div> </div>", """
        block <body> block-children
          flex <div#f>
            block anonymous inline-children
              text " a "
            block <span> inline-children
              text "b"
            block <div> inline-children absolute
              text "c"
        """);

    [Fact]
    public void Grid_ContentsChildrenAreItems() => AssertBody("<div style='display: grid'><div style='display: contents'><span>a</span>b</div></div>", """
        block <body> block-children
          grid <div>
            block <span> inline-children
              text "a"
            block anonymous inline-children
              text "b"
        """);

    [Fact]
    public void Blockification_FloatsAndAbsolute() => AssertBody("<span style='float: right'>a</span><span style='position: fixed'>b</span>", """
        block <body> block-children
          block <span> inline-children float
            text "a"
          block <span> inline-children absolute
            text "b"
        """);

    [Fact]
    public void ReplacedElements() => AssertBody("<img><video><p>fallback</p></video><input><select><option>o</option></select><canvas><b>c</b></canvas>", """
        block <body> inline-children
          replaced inline <img>
          replaced inline <video>
          replaced inline <input>
            flow-root anonymous inline-children
              line-break
          replaced inline <select>
          inline <canvas>
            inline <b>
              text "c"
        """);

    [Fact]
    public void TextControls_InnerEditorAndPlaceholder() => AssertBody("<input value=abc><input type=password value=ab><input type=checkbox><textarea>a\nb\n</textarea><input placeholder=p><textarea placeholder=q>\n</textarea>", """
        block <body> inline-children
          replaced inline <input>
            flow-root anonymous inline-children
              generated-text "abc"
          replaced inline <input>
            flow-root anonymous inline-children
              generated-text "\2022\2022"
          replaced inline <input>
          replaced inline <textarea>
            flow-root anonymous inline-children
              generated-text "a\nb\n"
              line-break
          replaced inline <input>
            flow-root anonymous inline-children
              line-break
            flow-root <input>::placeholder inline-children
              generated-text "p"
          replaced inline <textarea>
            flow-root anonymous inline-children
              line-break
            flow-root <textarea>::placeholder inline-children
              generated-text "q"
        """);

    [Fact]
    public void ReplacedElements_CanvasWithScripting() => AssertBody("<canvas><p>c</p></canvas>", """
        block <body> inline-children
          replaced inline <canvas>
        """, scripting: true);

    [Fact]
    public void ReplacedElements_ContentImage() => AssertBody("<div style='content: url(a.png)'>x</div>", """
        block <body> block-children
          replaced block <div> url("a.png")
        """);

    [Fact]
    public void LineBreaks() => AssertBody("a<br>b<wbr>c", """
        block <body> inline-children
          text "a"
          inline <br>
            line-break
          text "b"
          inline <wbr>
            word-break-opportunity
          text "c"
        """);

    [Fact]
    public void ShadowTree_FlatTree()
    {
        var document = Parse("<!DOCTYPE html><style>body { margin: 0 }</style><div id=host><span slot=s id=a>a</span><span id=b>b</span></div>");
        var host = document.GetElementById("host")!;
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadow.SetHtmlUnsafe("<p>[<slot name=s></slot>]</p><slot>fallback</slot>");
        var tree = BoxTree.Build(document);
        Assert.Equal("""
            block <div#host> block-children
              block <p> inline-children
                text "["
                inline <span#a>
                  text "a"
                text "]"
              block anonymous inline-children
                inline <span#b>
                  text "b"

            """.ReplaceLineEndings("\n"), BoxTree.Dump(tree.GetPrincipalBox(host)!));
    }

    [Fact]
    public void ContentVisibilityHidden_SkipsContents() => AssertBody("<div style='content-visibility: hidden'><p>a</p></div><details><summary>s</summary>d</details>", """
        block <body> block-children
          block <div> block-children skips-contents
            block <p> inline-children
              text "a"
          block <details> block-children
            block list-item <summary> inline-children
              inline <summary>::marker
                generated-text "\25B8 "
              text "s"
            block <details>::details-content inline-children skips-contents
              text "d"
        """);

    [Fact]
    public void Details_ContentPseudoElement()
    {
        // The box of ::details-content has the display of the pseudo-element, and its ::before and ::after
        var document = Parse("<!DOCTYPE html><style>#a::details-content { display: contents } #b::details-content { display: flex } details::details-content::before { content: 'b' } details::details-content::after { content: 'a' }</style><details id=a open><summary>s</summary>d</details><details id=b open><summary>s</summary>d</details>");
        var tree = BoxTree.Build(document);
        var a = document.GetElementById("a")!;
        var b = document.GetElementById("b")!;
        Assert.Null(tree.GetPseudoElementBox(a, "details-content"));
        Assert.NotNull(tree.GetPseudoElementBox(a, "details-content::before"));
        Assert.NotNull(tree.GetPseudoElementBox(a, "details-content::after"));

        var content = tree.GetPseudoElementBox(b, "details-content")!;
        Assert.Equal(DisplayInside.Flex, content.Display.Inside);
        Assert.False(content.SkipsContents);
    }

    [Fact]
    public void Checkbox_AppearanceNone_HasPseudoElementBoxes()
    {
        // As in the browsers, a checkbox without its native appearance is a regular box with its ::before
        var document = Parse("<!DOCTYPE html><style>input { appearance: none } input::before { content: 'x' }</style><input id=a type=checkbox><input id=b type=checkbox style='appearance: auto'>");
        var tree = BoxTree.Build(document);
        var a = document.GetElementById("a")!;
        var b = document.GetElementById("b")!;
        Assert.Equal(ContainerBoxKind.Normal, tree.GetPrincipalBox(a)!.Kind);
        Assert.NotNull(tree.GetPseudoElementBox(a, "before"));
        Assert.Equal(ContainerBoxKind.Replaced, tree.GetPrincipalBox(b)!.Kind);
        Assert.Null(tree.GetPseudoElementBox(b, "before"));
    }

    [Fact]
    public void Svg_RootAndForeignObject() => AssertBody("<svg><rect/><defs><foreignObject><p>no</p></foreignObject></defs><g><foreignObject><p>yes</p></foreignObject></g></svg>", """
        block <body> inline-children
          replaced inline <svg>
            flow-root <foreignObject> block-children
              block <p> inline-children
                text "yes"
        """);

    [Fact]
    public void MathML_Root() => AssertBody("<math><mi>x</mi></math>", """
        block <body> inline-children
          math <math>
            block math <mi>
              text "x"
        """);

    [Fact]
    public void RunIn_InsertedIntoFollowingBlock() => AssertBody("<span id=r style='display: run-in'>R</span> <p id=p>text</p>", """
        block <body> block-children
          block <p#p> inline-children
            inline <span#r>
              text "R"
            text "text"
        """);

    [Fact]
    public void RunIn_NotFollowedByBlock() => AssertBody("<div><span id=r style='display: run-in'>R</span></div>", """
        block <body> block-children
          block <div> block-children
            block anonymous inline-children
              inline <span#r>
                text "R"
        """);

    [Fact]
    public void RunIn_AnonymousBlockAroundTheSequenceAndTheInlineContent() => AssertBody("a<b id=r style='display: run-in'>R</b> text <i>i</i><b id=s style='display: run-in'>S</b>b<p>p</p>", """
        block <body> block-children
          block anonymous inline-children
            text "a"
          block anonymous inline-children
            inline <b#r>
              text "R"
            text " text "
            inline <i>
              text "i"
          block anonymous inline-children
            inline <b#s>
              text "S"
            text "b"
          block <p> inline-children
            text "p"
        """);

    [Fact]
    public void RunIn_SequenceWithOutOfFlowBoxes() => AssertBody("<b id=r style='display: run-in'>R</b> <i id=a style='position: absolute'></i> <b id=s style='display: run-in'>S</b><u id=f style='float: left'></u> <p id=p>text</p>", """
        block <body> block-children
          block <u#f> float
          block <p#p> inline-children
            inline <b#r>
              text "R"
            text " "
            block <i#a> absolute
            text " "
            inline <b#s>
              text "S"
            text "text"
        """);

    [Fact]
    public void RunIn_InlinifiesItsContents() => AssertBody("<div id=r style='display: run-in'>R<div id=b>b</div><span id=s><div id=c>c</div></span><div id=n style='display: run-in'>n</div></div><p id=p>text</p>", """
        block <body> block-children
          block <p#p> inline-children
            inline <div#r>
              text "R"
              inline-block <div#b> inline-children
                text "b"
              inline <span#s>
                inline-block <div#c> inline-children
                  text "c"
              inline <div#n>
                text "n"
            text "text"
        """);

    [Fact]
    public void RunIn_NotIntoFormattingContextRoot() => AssertBody("<b id=r style='display: run-in'>R</b><p id=p style='overflow: hidden'>text</p>", """
        block <body> block-children
          block anonymous inline-children
            inline <b#r>
              text "R"
          block <p#p> inline-children
            text "text"
        """);

    [Fact]
    public void RunIn_IntoBlockThatStartsWithRunIn() => AssertBody("<b id=r style='display: run-in'>R</b><div id=d><b id=s style='display: run-in'>S</b></div>", """
        block <body> block-children
          block <div#d> block-children
            block anonymous inline-children
              inline <b#r>
                text "R"
            block anonymous inline-children
              inline <b#s>
                text "S"
        """);

    [Fact]
    public void RunIn_AfterTheMarkerOfAListItem() => AssertBody("<ol><b id=r style='display: run-in'>R</b><li id=l style='list-style-position: inside'>text</li></ol>", """
        block <body> block-children
          block <ol> block-children
            block list-item <li#l> inline-children
              inline <li#l>::marker
                generated-text "1. "
              inline <b#r>
                text "R"
              text "text"
        """);

    [Fact]
    public void Fieldset_RenderedLegendIsTheFirstChildBox() => AssertBody("<fieldset id=f><div style='display: contents'><legend id=a style='display: contents'>not legend</legend></div><legend id=l>legend</legend></fieldset>", """
        block <body> block-children
          flow-root <fieldset#f> block-children
            flow-root <legend#l> inline-children
              text "legend"
            flow-root anonymous inline-children
              text "not legend"
        """);

    [Fact]
    public void Fieldset_ContentsAreInTheAnonymousFieldsetContentBox() => AssertBody("<fieldset id=f style='display: inline-flex'><div id=a>a</div><legend id=l>legend</legend><div id=b>b</div></fieldset>", """
        block <body> inline-children
          inline-block <fieldset#f> block-children
            flow-root <legend#l> inline-children
              text "legend"
            flex anonymous
              block <div#a> inline-children
                text "a"
              block <div#b> inline-children
                text "b"
        """);

    [Fact]
    public void FirstLetter_InheritsFromTheElementOfTheText()
    {
        var document = Parse("<!DOCTYPE html><style>p { color: red } p::first-letter { background: transparent } span { color: green } i { display: contents; color: blue }</style><p id=p><span>ab</span></p><p id=q><i>ab</i></p><p id=r>ab</p>");
        var tree = BoxTree.Build(document);
        Assert.Equal("rgb(0, 128, 0)", tree.GetPseudoElementBox(document.GetElementById("p")!, "first-letter")!.Style.GetComputedValue("color"));
        Assert.Equal("rgb(0, 0, 255)", tree.GetPseudoElementBox(document.GetElementById("q")!, "first-letter")!.Style.GetComputedValue("color"));
        Assert.Equal("rgb(255, 0, 0)", tree.GetPseudoElementBox(document.GetElementById("r")!, "first-letter")!.Style.GetComputedValue("color"));
    }

    [Fact]
    public void Ruby_InlinificationAndAnonymousBases() => AssertBody("<ruby>a<div id=d>b</div><rt>x</rt></ruby>", """
        block <body> inline-children
          ruby <ruby>
            ruby-base anonymous
              text "a"
              inline-block <div#d> inline-children
                text "b"
            ruby-text <rt>
              text "x"
        """);

    [Fact]
    public void Ruby_BlockRuby() => AssertBody("<ruby style='display: block ruby'><rb>a</rb><rt>x</rt></ruby>", """
        block <body> block-children
          block <ruby> inline-children
            ruby anonymous
              ruby-base <rb>
                text "a"
              ruby-text <rt>
                text "x"
        """);

    [Fact]
    public void Ruby_MisparentedAnnotation() => AssertBody("<span>a<rt>x</rt></span>", """
        block <body> inline-children
          inline <span>
            text "a"
            ruby anonymous
              ruby-text <rt>
                text "x"
        """);

    [Fact]
    public void Lookups()
    {
        var document = Parse("<!DOCTYPE html><style>#p::before { content: 'b' } #p::first-letter { color: red }</style><p id=p>text</p><span id=s>a<div>b</div>c</span><table id=t></table>");
        var tree = BoxTree.Build(document);
        var p = document.GetElementById("p")!;
        var box = tree.GetPrincipalBox(p)!;
        Assert.Same(p, box.Element);
        Assert.Null(box.PseudoElement);

        var before = tree.GetPseudoElementBox(p, "before")!;
        Assert.Same(p, before.Element);
        Assert.Equal("before", before.PseudoElement);
        Assert.Same(box, before.Parent);

        // The first letter is in ::before: the text node has one run
        var text = (Text)p.FirstChild!;
        var run = tree.GetFirstTextRun(text)!;
        Assert.Same(text, run.Node);
        Assert.Equal(0, run.Start);
        Assert.Equal(4, run.Length);
        Assert.Null(run.NextRunOfNode);
        Assert.Equal("first-letter", tree.GetPseudoElementBox(p, "first-letter")!.PseudoElement);

        var span = tree.GetPrincipalBox(document.GetElementById("s")!)!;
        Assert.Null(span.PreviousContinuation);
        Assert.NotNull(span.Continuation);
        Assert.Same(span, span.Continuation!.PreviousContinuation);
        Assert.True(span.Parent!.IsAnonymous);

        var table = tree.GetPrincipalBox(document.GetElementById("t")!)!;
        Assert.Equal(ContainerBoxKind.TableWrapper, table.Kind);
        Assert.Equal(ContainerBoxKind.TableGrid, ((ContainerBox)table.FirstChild!).Kind);
    }

    [Fact]
    public void AnonymousStyle_InheritsFromParent()
    {
        var document = Parse("<!DOCTYPE html><div id=d style='color: rgb(1, 2, 3); padding: 5px'>a<p>b</p></div>");
        var tree = BoxTree.Build(document);
        var anonymous = (ContainerBox)tree.GetPrincipalBox(document.GetElementById("d")!)!.FirstChild!;
        Assert.True(anonymous.IsAnonymous);
        Assert.Equal("rgb(1, 2, 3)", anonymous.Style.GetComputedValue("color"));
        Assert.Equal("0px", anonymous.Style.GetComputedValue("padding-left"));
    }

    [Theory]
    [InlineData("<div id=t>a</div>", "Box", false)]
    [InlineData("<div style='display: none'><p id=t>a</p></div>", "DisplayNone", false)]
    [InlineData("<div id=t style='display: contents'>a</div>", "Contents", false)]
    [InlineData("<video><p id=t>a</p></video>", "NoStyle", false)]
    [InlineData("<select><option id=t>a</option></select>", "ReplacedContents", false)]
    [InlineData("<select style='appearance: base-select'><option id=t>a</option></select>", "DisplayNone", false)]
    [InlineData("<select multiple style='appearance: base-select'><option id=t>a</option></select>", "Box", false)]
    [InlineData("<select style='appearance: base-select'><button id=t>a</button><option>a</option></select>", "Contents", false)]
    [InlineData("<canvas><p id=t>a</p></canvas>", "Box", false)]
    [InlineData("<svg><rect id=t /></svg>", "Box", false)]
    [InlineData("<svg><defs><rect id=t /></defs></svg>", "NotRenderedSvg", false)]
    [InlineData("<svg><foreignObject><div id=t></div></foreignObject></svg>", "Box", false)]
    [InlineData("<div style='content-visibility: hidden'><p id=t>a</p></div>", "Box", true)]
    [InlineData("<details><summary id=s>s</summary><p id=t>a</p></details>", "Box", true)]
    [InlineData("<details open><summary>s</summary><p id=t>a</p></details>", "Box", false)]
    [InlineData("<style>details::details-content { content-visibility: visible }</style><details><summary>s</summary><p id=t>a</p></details>", "Box", false)]
    [InlineData("<style>details::details-content { content-visibility: hidden }</style><details open><summary>s</summary><p id=t>a</p></details>", "Box", true)]
    [InlineData("<style>details::details-content { display: none }</style><details open><summary>s</summary><p id=t>a</p></details>", "DisplayNone", false)]
    [InlineData("<style>details::details-content { display: contents }</style><details open><summary>s</summary><p id=t>a</p></details>", "Box", false)]
    [InlineData("<embed id=t>", "NotGenerated", false)]
    [InlineData("<embed id=t type=text/plain>", "Box", false)]
    [InlineData("<input id=t type=checkbox style='appearance: none'>", "Box", false)]
    public void BoxGeneration_Element(string html, string kindName, bool skipped)
    {
        var kind = Enum.Parse<BoxGenerationKind>(kindName);
        var document = Parse("<!DOCTYPE html>" + html);
        var element = document.GetElementById("t")!;
        var info = BoxGeneration.Get(element);
        Assert.Equal(kind, info.Kind);
        Assert.Equal(skipped, info.InSkippedContents);

        // The query and the box tree agree (the SVG elements other than the outermost svg have SVG boxes, not CSS boxes)
        var tree = BoxTree.Build(document);
        Assert.Equal(kind == BoxGenerationKind.Box && !BoxGeneration.IsSvgElement(element), tree.GetPrincipalBox(element) is not null);
    }

    [Fact]
    public void BoxGeneration_HtmlElementInSvg_NotRendered()
    {
        var document = Parse("<!DOCTYPE html><svg><g id=g></g><foreignObject id=f></foreignObject></svg>");
        var inGroup = document.CreateElement("div");
        document.GetElementById("g")!.AppendChild(inGroup);
        var inForeignObject = document.CreateElement("div");
        document.GetElementById("f")!.AppendChild(inForeignObject);
        Assert.Equal(BoxGenerationKind.NotRenderedSvg, BoxGeneration.Get(inGroup).Kind);
        Assert.Equal(BoxGenerationKind.Box, BoxGeneration.Get(inForeignObject).Kind);
    }

    [Fact]
    public void BoxGeneration_Disconnected_NoStyle()
    {
        var document = Parse("<!DOCTYPE html><div></div>");
        Assert.Equal(BoxGenerationKind.NoStyle, BoxGeneration.Get(document.CreateElement("div")).Kind);
    }

    [Theory]
    [InlineData("<p id=t>a</p>", "before", "NotGenerated")]
    [InlineData("<style>#t::before { content: 'x' }</style><p id=t>a</p>", "before", "Box")]
    [InlineData("<style>#t::before { content: 'x' }</style><input id=t>", "before", "ReplacedContents")]
    [InlineData("<style>#t::before { content: 'x' }</style><input id=t type=checkbox style='appearance: none'>", "before", "Box")]
    [InlineData("<ul><li id=t>a</li></ul>", "marker", "Box")]
    [InlineData("<p id=t>a</p>", "marker", "NotGenerated")]
    public void BoxGeneration_PseudoElement(string html, string pseudoElement, string kindName)
    {
        var kind = Enum.Parse<BoxGenerationKind>(kindName);
        var document = Parse("<!DOCTYPE html>" + html);
        var element = document.GetElementById("t")!;
        Assert.Equal(kind, BoxGeneration.Get(document.GetStyleEngine().GetStyle(element, pseudoElement)).Kind);
    }

    [Fact]
    public void Focus_ReplacedContentsAndClosedDetails()
    {
        var document = Parse("<!DOCTYPE html><video><button id=v>v</button></video><canvas><button id=c>c</button></canvas><details><summary>s</summary><button id=d>d</button></details>", scripting: true);
        ((HtmlElement)document.GetElementById("v")!).Focus();
        Assert.NotSame(document.GetElementById("v"), document.ActiveElement);
        ((HtmlElement)document.GetElementById("d")!).Focus();
        Assert.NotSame(document.GetElementById("d"), document.ActiveElement);

        // The relevant canvas fallback content is focusable
        ((HtmlElement)document.GetElementById("c")!).Focus();
        Assert.Same(document.GetElementById("c"), document.ActiveElement);
    }

    [Theory]
    [InlineData("a", 0, 1)]
    [InlineData("  ab", 2, 3)]
    [InlineData("\"(a)\" b", 0, 5)]
    [InlineData("1st", 0, 1)]
    [InlineData("...", null, null)]
    [InlineData("", null, null)]
    [InlineData("e\U00000301x", 0, 2)]
    [InlineData("\U0001F600!", 0, 3)]
    [InlineData("_T_est", 0, 3)] // The connector punctuation precedes and follows the letter
    [InlineData("-(Test", 0, 3)] // The dashes and the opening punctuation only precede it
    [InlineData("T(rail", 0, 1)]
    [InlineData("T-rail", 0, 1)]
    [InlineData("\" Test", 0, 3)] // The spaces between the punctuation and the letter
    [InlineData("T\U00002003.est", 0, 3)] // A typographic space before the punctuation that follows
    [InlineData("T .est", 0, 1)] // But not a word separator
    [InlineData("\U0000104E\U00000300T\U00000300\U0000104E\U00000300est", 0, 6)] // The punctuation with its combining marks
    [InlineData("\U0000000BTest", 0, 1)] // A control character is rendered as a symbol
    [InlineData("ijsselmeer", 0, 1)]
    public void FirstLetterRange(string text, int? start, int? end)
    {
        var range = BoxTreeBuilder.GetFirstLetterRange(text);
        Assert.Equal(start, range?.Start);
        Assert.Equal(end, range?.End);
    }

    [Theory]
    [InlineData("ijsselmeer", 2)]
    [InlineData("IJsselmeer", 2)]
    [InlineData("Ijotalen", 1)]
    [InlineData("\"IJ\"sselmeer", 4)]
    public void FirstLetterRange_DutchDigraph(string text, int end)
        => Assert.Equal((0, end), BoxTreeBuilder.GetFirstLetterRange(text, isDutch: true));

    [Fact]
    public void FirstLetter_PunctuationInAnotherTextRun() => AssertBody("<style>p::first-letter { color: red }</style><p id=p><span>\"</span>abc</p><p id=q><span>a</span>\"bc</p><p id=r><span>\"</span><img>abc</p>", """
        block <body> block-children
          block <p#p> inline-children
            inline <span>
              inline <p#p>::first-letter
                text "\""
            text "abc"
          block <p#q> inline-children
            inline <span>
              inline <p#q>::first-letter
                text "a"
            text "\"bc"
          block <p#r> inline-children
            inline <span>
              text "\""
            replaced inline <img>
            text "abc"
        """);

    [Fact]
    public void FirstLetter_OnlyOnTheFirstFormattedLine() => AssertBody("<style>div::first-letter { color: red }</style><div id=a><p></p>ab</div><div id=b style='white-space: pre'>\nab</div><div id=c lang=nl>ijs</div>", """
        block <body> block-children
          block <div#a> block-children
            block <p>
            block anonymous inline-children
              text "ab"
          block <div#b> inline-children
            text "\nab"
          block <div#c> inline-children
            inline <div#c>::first-letter
              text "ij" [0, 2)
            text "s" [2, 3)
        """);
}
