using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

public sealed class SelectorTests
{
    [Theory]
    [InlineData("div", true)]
    [InlineData("*", true)]
    [InlineData("*|div", true)]
    [InlineData("|div", true)]
    [InlineData("ns|div", false)] // undeclared namespace prefix
    [InlineData("div > p + a ~ span b", true)]
    [InlineData("div >", false)]
    [InlineData("> div", false)]
    [InlineData("", false)]
    [InlineData("div,", false)]
    [InlineData("#id.class[attr=value i]:hover::before", true)]
    [InlineData("::before div", false)]
    [InlineData("::before:hover", true)]
    [InlineData("::before:first-child", false)]
    [InlineData(":before", true)]
    [InlineData("::view-transition-group(*.class):only-child", true)]
    [InlineData("::view-transition-group(*.*)", false)]
    [InlineData("::view-transition-new(name):hover", false)]
    [InlineData("::scroll-button(up):disabled", true)]
    [InlineData("::scroll-button(north)", false)]
    [InlineData("::before::column::scroll-marker", true)]
    [InlineData("::column:hover", false)]
    [InlineData("::-webkit-scrollbar", true)]
    [InlineData("::unknown", false)]
    [InlineData(":unknown", false)]
    [InlineData(":is()", true)]
    [InlineData(":is(::before, div)", true)]
    [InlineData(":not(::before)", false)]
    [InlineData(":not()", false)]
    [InlineData(":has(> a, + b, ~ c, d)", true)]
    [InlineData(":has()", false)]
    [InlineData(":has(:has(a))", false)]
    [InlineData(":is(:has(a))", true)]
    [InlineData(":nth-child(2n+1 of .a, .b)", true)]
    [InlineData(":nth-of-type(2n of .a)", false)]
    [InlineData(":nth-child(+ n)", false)]
    [InlineData(":lang(en, \"fr-CA\", \"*-CH\")", true)]
    [InlineData(":lang(*-CH)", false)]
    [InlineData("#123", false)]
    [InlineData("a||b", false)]
    [InlineData("[attr|=en]", true)]
    [InlineData("[*|attr]", true)]
    [InlineData("[attr=value x]", false)]
    [InlineData("::part(a b)", true)]
    [InlineData("::part()", false)]
    [InlineData("::part(a):hover:state(x):checked", true)]
    [InlineData("::part(a):first-child", false)]
    [InlineData("::part(a):host", false)]
    [InlineData("::part(a):not(:hover)", true)]
    [InlineData("::part(a):not(.b)", false)]
    [InlineData("::part(a)::first-line", true)]
    [InlineData("::part(a)::part(b)", false)]
    [InlineData("::part(a) + ::part(b)", false)]
    [InlineData("::slotted(a):hover", false)]
    [InlineData("::slotted(a)::picker-icon", true)]
    [InlineData("::picker(SELECT)::before", true)]
    [InlineData("::picker(foo)", false)]
    [InlineData("::picker(select)::slotted(a)", false)]
    [InlineData("::selection:hover", false)]
    [InlineData("::highlight(a):is(:hover)", true)]
    [InlineData("::search-text:current", true)]
    [InlineData("::search-text:not(:current)", true)]
    [InlineData("::search-text:past", false)]
    [InlineData(":is(# x[y)", false)] // the invalid selector contains an unterminated block
    [InlineData(".foo-*", true)]
    [InlineData("p.foo-*.bar--*:hover", true)]
    [InlineData(":has(> .foo-*)", true)]
    [InlineData(".foo- *", true)] // a descendant combinator
    [InlineData(".foo*", false)]
    [InlineData(".foo-**", false)]
    public void Validity(string selector, bool valid)
    {
        Assert.Equal(valid, SelectorParser.ParseSelector(selector) is not null);
    }

    [Theory]
    [InlineData("odd", 2, 1)]
    [InlineData("EVEN", 2, 0)]
    [InlineData("5", 0, 5)]
    [InlineData("n", 1, 0)]
    [InlineData("-n+3", -1, 3)]
    [InlineData("+n - 2", 1, -2)]
    [InlineData("2n+1", 2, 1)]
    [InlineData("2n + 1", 2, 1)]
    [InlineData("2n- 1", 2, -1)]
    [InlineData("2n-1", 2, -1)]
    [InlineData("-2n-1", -2, -1)]
    [InlineData("n-1", 1, -1)]
    [InlineData("-n-1", -1, -1)]
    [InlineData("3n +2", 3, 2)]
    public void AnB(string value, int a, int b)
    {
        var list = SelectorParser.ParseSelector($":nth-child({value})");
        Assert.NotNull(list);
        var pseudoClass = (PseudoClassSelector)list.Selectors[0].Compounds[0].Selectors[0];
        Assert.Equal(new AnB(a, b), pseudoClass.Nth);
    }

    [Theory]
    [InlineData("2n + +1")]
    [InlineData("n +")]
    [InlineData("1.5")]
    [InlineData("+ 2n")]
    [InlineData("2 n")]
    public void AnB_Invalid(string value)
    {
        Assert.Null(SelectorParser.ParseSelector($":nth-child({value})"));
    }

    [Theory]
    [InlineData("*", 0, 0, 0)]
    [InlineData("li", 0, 0, 1)]
    [InlineData("ul li", 0, 0, 2)]
    [InlineData("ul ol+li", 0, 0, 3)]
    [InlineData("h1 + *[rel=up]", 0, 1, 1)]
    [InlineData("ul ol li.red", 0, 1, 3)]
    [InlineData("li.red.level", 0, 2, 1)]
    [InlineData("#x34y", 1, 0, 0)]
    [InlineData("#s12:not(FOO)", 1, 0, 1)]
    [InlineData(".foo :is(.bar, #baz)", 1, 1, 0)]
    [InlineData(":where(#a, .b) span", 0, 0, 1)]
    [InlineData(":nth-child(2 of #a, .b)", 1, 1, 0)]
    [InlineData("div::before", 0, 0, 2)]
    public void Specificity(string selector, int a, int b, int c)
    {
        var list = SelectorParser.ParseSelector(selector);
        Assert.NotNull(list);
        Assert.Equal(new Specificity(a, b, c), list.Selectors[0].Specificity);
    }

    [Fact]
    public void Tokenizer_Escapes()
    {
        var tokens = CssTokenizer.Tokenize("#\\31 23 \\10FFFF\\0  url( a\\)b ) 'str\\\nx' 1.5e2px -.5% +3 u+1F?");
        Assert.Equal(CssTokenType.Hash, tokens[0].Type);
        Assert.Equal("123", tokens[0].Value);
        Assert.True(tokens[0].IsIdHash); // an escape starts an identifier
        Assert.Equal(char.ConvertFromUtf32(0x10FFFF) + (char)0xFFFD, tokens[2].Value);
        Assert.Equal(CssTokenType.Url, tokens[4].Type);
        Assert.Equal("a)b", tokens[4].Value);
        Assert.Equal("strx", tokens[6].Value);
        Assert.Equal(CssTokenType.Dimension, tokens[8].Type);
        Assert.Equal(150, tokens[8].NumericValue);
        Assert.Equal("px", tokens[8].Value);
        Assert.Equal(CssTokenType.Percentage, tokens[10].Type);
        Assert.Equal(-0.5, tokens[10].NumericValue);
        Assert.Equal('+', tokens[12].Sign);
        Assert.Equal(CssTokenType.Ident, tokens[14].Type); // unicode-range tokens are not allowed by default
    }

    private static Document Parse(string html) => HtmlDocumentParser.ParseDocument(html);

    [Fact]
    public void QuerySelector_CaseSensitivity()
    {
        var document = Parse("<!DOCTYPE html><div id=a class=Foo lang=EN-us type=TEXT><svg><foreignObject/></svg></div>");
        Assert.NotNull(document.QuerySelector("DIV"));
        Assert.Null(document.QuerySelector(".foo"));
        Assert.NotNull(document.QuerySelector(".Foo"));
        Assert.NotNull(document.QuerySelector("[TYPE=text]")); // "type" values are case-insensitive in HTML
        Assert.Null(document.QuerySelector("[type=text s]"));
        Assert.NotNull(document.QuerySelector("[class=foo i]"));
        Assert.Null(document.QuerySelector("[class=foo]"));
        Assert.NotNull(document.QuerySelector("foreignObject"));
        Assert.Null(document.QuerySelector("foreignobject")); // SVG element names are case-sensitive
        Assert.NotNull(document.QuerySelector(":lang(en)"));
        Assert.NotNull(document.QuerySelector(":lang(\"*-US\")"));
    }

    [Fact]
    public void QuerySelector_Quirks()
    {
        var document = Parse("<div id=Foo class=Bar></div>");
        Assert.Equal(DocumentMode.Quirks, document.Mode);
        Assert.NotNull(document.QuerySelector("#foo"));
        Assert.NotNull(document.QuerySelector(".bar"));
        Assert.Null(document.QuerySelector("[class=bar]"));
    }

    [Fact]
    public void QuerySelectorAll_StructuralAndLogical()
    {
        var document = Parse("<!DOCTYPE html><ul><li>1<li class=x>2<li>3<li class=x>4<li>5</ul><p></p><p> </p>");
        Assert.Equal(["1", "3", "5"], document.QuerySelectorAll("li:nth-child(odd)").Select(n => n.TextContent));
        Assert.Equal(["4"], document.QuerySelectorAll("li:nth-child(2 of .x)").Select(n => n.TextContent));
        Assert.Equal(["2"], document.QuerySelectorAll("li:nth-last-child(4)").Select(n => n.TextContent));
        Assert.Equal(["1", "3"], document.QuerySelectorAll("li:has(+ .x)").Select(n => n.TextContent));
        Assert.Equal(["ul"], document.QuerySelectorAll(":has(> li.x)").Select(n => ((Element)n).LocalName));
        Assert.Equal(["3", "5"], document.QuerySelectorAll("li.x ~ li:not(.x)").Select(n => n.TextContent));
        Assert.Single(document.QuerySelectorAll("p:empty"));
        Assert.Equal(5, document.QuerySelectorAll(":is(ul, ol) > :where(li)").Count);
    }

    [Theory]
    [InlineData(":is(,,,)", ":is(, , , )")]
    [InlineData(":is(::before:HOVER  , .a)", ":is(::before:HOVER, .a)")]
    [InlineData(":where(.a, .b+.c, :nonsense)", ":where(.a, .b + .c, :nonsense)")]
    [InlineData(":host(:is(.a, .b+.c))", ":host(:is(.a, .b+.c))")]
    [InlineData("::part(a):is(:hover, [attr='value'])", "::part(a):is(:hover, [attr='value'])")]
    [InlineData("::slotted(a):is(:hover)", "::slotted(a):is(:hover)")]
    public void ForgivingSelectorList_InvalidSelectorsAreSerializedAsWritten(string selector, string expected)
    {
        Assert.Equal(expected, SelectorParser.ParseSelector(selector)!.ToString());
    }

    [Fact]
    public void Muted_MatchesTheMutedMediaElements()
    {
        var document = Parse("<!DOCTYPE html><audio muted></audio><video></video>");
        var audio = (HtmlMediaElement)document.QuerySelector("audio")!;
        var video = (HtmlMediaElement)document.QuerySelector("video")!;
        Assert.Equal([audio], document.QuerySelectorAll(":muted"));

        // The muted state replaces the muted content attribute, and an extreme playbackRate mutes the element
        audio.Muted = false;
        video.PlaybackRate = 16;
        Assert.Equal([video], document.QuerySelectorAll(":muted"));
        Assert.True(video.Muted);
        video.PlaybackRate = 0.0625;
        Assert.True(video.Muted);
        video.PlaybackRate = 1;
        Assert.False(video.Muted);
        Assert.Empty(document.QuerySelectorAll(":muted"));
    }

    [Fact]
    public void ForgivingSelectorList_InvalidSelectorsDoNotMatch()
    {
        var document = Parse("<!DOCTYPE html><div class=a></div><div class=b></div>");
        Assert.Single(document.QuerySelectorAll(":is(:nonsense, .a)"));
        Assert.Empty(document.QuerySelectorAll(":is(:nonsense)"));
    }

    [Fact]
    public void Host_MatchesInTheShadowTree()
    {
        // https://drafts.csswg.org/css-shadow-1/#host-element-in-tree: the DOM APIs match the shadow host in its shadow tree
        var document = Parse("<!DOCTYPE html><div id=host></div>");
        var host = document.GetElementById("host")!;
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadow.SetHtmlUnsafe("<p></p>");
        var p = shadow.QuerySelector("p")!;
        Assert.False(host.Matches(":host"));
        Assert.True(p.Matches(":host > p"));
        Assert.False(p.Matches("body p"));
        Assert.Same(p, shadow.QuerySelector(":host p"));
    }

    [Fact]
    public void QuerySelectorAll_NthChild_LargeParent()
    {
        var document = Parse("<!DOCTYPE html><ul>" + string.Concat(Enumerable.Range(1, 100).Select(i => $"<li>{i}</li> ")) + "</ul>");
        var ul = document.QuerySelector("ul")!;
        Assert.Equal(["1", "4", "7"], document.QuerySelectorAll("li:nth-child(3n+1)").Take(3).Select(n => n.TextContent));
        Assert.Equal(["100"], document.QuerySelectorAll("li:nth-last-child(1)").Select(n => n.TextContent));
        Assert.Equal("50", document.QuerySelector("li:nth-child(50)")!.TextContent);

        // The indexes follow the mutations of the children
        ul.RemoveChild(ul.FirstElementChild!);
        Assert.Equal("51", document.QuerySelector("li:nth-child(50)")!.TextContent);
        ul.InsertBefore(document.CreateElement("li"), ul.FirstChild);
        Assert.Equal("51", document.QuerySelector("li:nth-child(51)")!.TextContent);
        Assert.Equal("2", document.QuerySelector("li:nth-last-child(99)")!.TextContent);
        Assert.Equal("", document.QuerySelector("li:nth-child(1)")!.TextContent);
        Assert.True(ul.LastElementChild!.Matches(":nth-last-child(1)"));
        Assert.True(ul.LastElementChild!.Matches(":nth-child(100)"));
    }

    [Fact]
    public void QuerySelectorAll_Combinators()
    {
        var document = Parse("<!DOCTYPE html><section><p class=a></p><div class=b><span class=c id=s1></span></div><div class=b><i><span class=c id=s2></span></i></div></section><div class=b><span class=c id=s3></span></div>");
        Assert.Equal(["s1", "s2"], document.QuerySelectorAll(".a ~ .b .c").Select(n => ((Element)n).Id));
        Assert.Equal(["s1"], document.QuerySelectorAll(".a + .b > .c").Select(n => ((Element)n).Id));
        Assert.Equal(["s2"], document.QuerySelectorAll(".a ~ .b ~ .b .c").Select(n => ((Element)n).Id));
        Assert.Equal(["s1", "s2"], document.QuerySelectorAll("section .b .c").Select(n => ((Element)n).Id));
        Assert.Equal(["s2"], document.QuerySelectorAll("section > .b > i > .c").Select(n => ((Element)n).Id));
        Assert.Equal(["s3"], document.QuerySelectorAll("section ~ .b .c").Select(n => ((Element)n).Id));
        Assert.Equal(["s1", "s2", "s3"], document.QuerySelectorAll("body .b .c").Select(n => ((Element)n).Id));
        Assert.Empty(document.QuerySelectorAll("p .b .c"));
        Assert.Empty(document.QuerySelectorAll(".a > .b .c"));

        // Many descendant combinators in a deep tree (the failures are not retried for each ancestor)
        var deep = Parse("<!DOCTYPE html>" + string.Concat(Enumerable.Repeat("<div>", 60)) + "<span></span>");
        Assert.Empty(deep.QuerySelectorAll("p div div div div div div div div div div span"));
        Assert.Single(deep.QuerySelectorAll("body div div div div div div div div div div span"));
        Assert.Empty(deep.QuerySelectorAll("div ~ div div div div div div div div div div span"));
    }

    [Fact]
    public void QuerySelectorAll_Id()
    {
        var document = Parse("<!DOCTYPE html><div id=a><p id=x class=c>1</p><div id=b><p id=x>2</p></div></div><p id=x class=c>3</p>");
        var a = document.GetElementById("a")!;
        var b = document.GetElementById("b")!;
        Assert.Equal(["1", "2", "3"], document.QuerySelectorAll("#x").Select(n => n.TextContent));
        Assert.Equal(["1", "3"], document.QuerySelectorAll("p#x.c").Select(n => n.TextContent));
        Assert.Equal(["1", "2"], a.QuerySelectorAll("#x").Select(n => n.TextContent));
        Assert.Equal(["2"], b.QuerySelectorAll("#x").Select(n => n.TextContent));
        Assert.Equal(["2"], document.QuerySelectorAll("#b > #x").Select(n => n.TextContent));
        Assert.Empty(a.QuerySelectorAll("#a"));
        Assert.Empty(document.QuerySelectorAll("#X"));

        // The ID map follows the mutations
        b.AppendChild(document.GetElementsByTagName("p")[2]!);
        Assert.Equal(["1", "2", "3"], a.QuerySelectorAll("#x").Select(n => n.TextContent));
        Assert.Equal(["2", "3"], b.QuerySelectorAll("#x").Select(n => n.TextContent));
        b.GetElementsByTagName("p")[0]!.Id = "y";
        Assert.Equal(["1", "3"], document.QuerySelectorAll("#x").Select(n => n.TextContent));

        // In quirks mode, the IDs match ASCII case-insensitively
        var quirks = Parse("<div id=Q></div>");
        Assert.Single(quirks.QuerySelectorAll("#q"));
    }

    [Fact]
    public void Scope()
    {
        var document = Parse("<!DOCTYPE html><div id=a><div id=b><div id=c></div></div></div>");
        var a = document.GetElementById("a")!;
        Assert.Equal(["b"], a.QuerySelectorAll(":scope > div").Select(n => ((Element)n).Id));
        Assert.Empty(a.QuerySelectorAll(":scope"));
        Assert.Same(document.DocumentElement, document.QuerySelector(":scope"));
        Assert.True(a.Matches(":scope"));
        Assert.Same(a, document.GetElementById("c")!.Closest("#a"));
    }

    [Fact]
    public void InvalidSelector_ThrowsSyntaxError()
    {
        var document = Parse("<!DOCTYPE html>");
        var exception = Assert.Throws<DomException>(() => document.QuerySelector("div["));
        Assert.Equal("SyntaxError", exception.Name);
    }

    [Fact]
    public void FormPseudoClasses()
    {
        var document = Parse("<!DOCTYPE html><form><fieldset disabled><legend><input id=a></legend><input id=b></fieldset><input id=c required><input id=d type=checkbox checked><button id=e>x</button><button id=f>y</button></form>");
        Assert.Equal(["a", "c", "d", "e", "f"], document.QuerySelectorAll("input:enabled, button:enabled").Select(n => ((Element)n).Id));
        Assert.Equal(["b"], document.QuerySelectorAll("input:disabled").Select(n => ((Element)n).Id));
        Assert.Equal(["c"], document.QuerySelectorAll(":required").Select(n => ((Element)n).Id));
        Assert.Equal(["d", "e"], document.QuerySelectorAll(":default").Select(n => ((Element)n).Id));
        Assert.Equal(["d"], document.QuerySelectorAll(":checked").Select(n => ((Element)n).Id));
    }

    [Fact]
    public void Disabled_OptionsOfADisabledSelect()
    {
        // An optgroup or option is disabled when its nearest ancestor select is disabled (by a fieldset too)
        var document = Parse("<!DOCTYPE html><fieldset disabled><select><optgroup id=a><option id=b></optgroup><option id=c></select></fieldset><select><option id=d></select>");
        Assert.Equal(["a", "b", "c"], document.QuerySelectorAll("optgroup:disabled, option:disabled").Select(n => ((Element)n).Id));
    }

    [Fact]
    public void Optional_InputToWhichRequiredDoesNotApply()
    {
        var document = Parse("<!DOCTYPE html><input id=a type=hidden required><input id=b required><input id=c>");
        Assert.Equal(["a", "c"], document.QuerySelectorAll(":optional").Select(n => ((Element)n).Id));
        Assert.Equal(["b"], document.QuerySelectorAll(":required").Select(n => ((Element)n).Id));
    }

    [Fact]
    public void ClassPrefix()
    {
        // https://drafts.csswg.org/selectors-5/#class-prefix (tentative): a class that starts with the prefix, followed by a
        // character that is not a hyphen
        var document = Parse("<!DOCTYPE html><div id=a class='x foo-bar'></div><div id=b class=foo--bar></div><div id=c class=foo-></div><div id=d class=FOO-bar></div>");
        Assert.Equal(["a"], document.QuerySelectorAll(".foo-*").Select(n => ((Element)n).Id));
        Assert.Equal(["b"], document.QuerySelectorAll(".foo--*").Select(n => ((Element)n).Id));

        var selector = SelectorParser.ParseSelector(".foo-*")!;
        Assert.Equal(".foo-*", selector.ToString());
        Assert.Equal(new Specificity(0, 1, 0), selector.Selectors[0].Specificity);
        Assert.Equal("foo-", Assert.IsType<ClassPrefixSelector>(selector.Selectors[0].Compounds[0].Selectors[0]).Prefix);

        // In quirks mode, the classes are ASCII lowercased, but the prefix is matched case-sensitively
        var quirks = Parse("<div class=FOO-bar></div>");
        Assert.Single(quirks.QuerySelectorAll(".foo-*"));
        Assert.Empty(quirks.QuerySelectorAll(".FOO-*"));
    }

    [Fact]
    public void Heading_UsesTheHeadingOffset()
    {
        // https://html.spec.whatwg.org/multipage/sections.html#get-an-element's-computed-heading-level
        var document = Parse("<!DOCTYPE html><h1 id=a></h1><section headingoffset=2><h1 id=b></h1><div headingreset headingoffset=1><h2 id=c></h2></div></section><div headingoffset=8><h6 id=d></h6></div>");
        Assert.Equal(["a"], document.QuerySelectorAll(":heading(1)").Select(n => ((Element)n).Id));
        Assert.Equal(["b", "c"], document.QuerySelectorAll(":heading(3)").Select(n => ((Element)n).Id));
        Assert.Equal(["d"], document.QuerySelectorAll(":heading(9)").Select(n => ((Element)n).Id)); // the level is at most 9
        Assert.Equal(4, document.QuerySelectorAll(":heading").Count);
    }

    [Fact]
    public void AttributeCaseInsensitivity_ComparesTheNonAsciiCharactersExactly()
    {
        // The i flag compares the values ASCII case-insensitively: the other characters (here, a combining diaeresis) are
        // compared exactly
        var diaeresis = (char)0x0308;
        var document = Parse("<!DOCTYPE html><div id=a></div><div id=b></div>");
        document.GetElementById("a")!.SetAttribute("foo", "A" + diaeresis);
        document.GetElementById("b")!.SetAttribute("foo", ((char)0x00C4).ToString());
        Assert.Equal(["a"], document.QuerySelectorAll("[foo='a" + diaeresis + "' i]").Select(n => ((Element)n).Id));
        Assert.Empty(document.QuerySelectorAll("[foo='" + (char)0x00E4 + "' i]"));
        Assert.Equal("[att]", SelectorParser.ParseSelector("[|att]")!.ToString());
    }

    [Fact]
    public void AttributeName_IsCaseInsensitiveOnHtmlElementsWhateverTheNamespace()
    {
        var document = Parse("<!DOCTYPE html><div id=a></div><div id=b></div>");
        document.GetElementById("a")!.SetAttributeNS("http://www.example.org/ns", "title", "");
        document.GetElementById("b")!.SetAttributeNS("http://www.example.org/ns", "TITLE", "");
        Assert.Equal(["a"], document.QuerySelectorAll("[*|TiTlE]").Select(n => ((Element)n).Id));
    }

    [Fact]
    public void Lang_SubtagsLongerThan8CharactersAreNotLanguageTags()
    {
        var document = Parse("<!DOCTYPE html><div id=a lang=fr-standard></div><div id=b lang=fr-ninechars></div>");
        Assert.Equal(["a"], document.QuerySelectorAll(":lang(fr)").Select(n => ((Element)n).Id));
    }

    [Fact]
    public void PlaceholderShown_EmptyPlaceholder()
    {
        var document = Parse("<!DOCTYPE html><input id=a placeholder><input id=b placeholder value=x><input id=c>");
        Assert.Equal(["a"], document.QuerySelectorAll(":placeholder-shown").Select(n => ((Element)n).Id));
    }

    [Fact]
    public void Dir()
    {
        var document = Parse("<!DOCTYPE html><div id=a dir=rtl><span id=b></span></div><div id=c dir=auto>" + (char)0x05D0 + "</div><bdi id=d>x</bdi><input id=e type=tel>");
        Assert.Equal(["a", "b", "c"], document.QuerySelectorAll(":dir(rtl)").Select(n => ((Element)n).Id));
        Assert.True(document.GetElementById("d")!.Matches(":dir(ltr)"));
        Assert.True(document.GetElementById("e")!.Matches(":dir(ltr)"));
    }

    [Fact]
    public void Dir_AutoUsesTheValueOfTheControl()
    {
        // https://html.spec.whatwg.org/multipage/dom.html#auto-directionality: the value, not the value content attribute
        var document = Parse("<!DOCTYPE html><input id=input dir=auto value=ltr><textarea id=textarea dir=auto>ltr</textarea>");
        var input = (HtmlInputElement)document.GetElementById("input")!;
        var textArea = (HtmlTextAreaElement)document.GetElementById("textarea")!;
        input.Value = ((char)0x05D0).ToString();
        textArea.Value = ((char)0x05D0).ToString();
        Assert.True(input.Matches(":dir(rtl)"));
        Assert.True(textArea.Matches(":dir(rtl)"));
        Assert.Equal("ltr", input.GetAttribute("value"));
    }
}
