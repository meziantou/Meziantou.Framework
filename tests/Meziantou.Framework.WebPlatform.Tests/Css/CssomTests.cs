using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

public sealed class CssomTests
{
    private static HtmlElement CreateElement(string style)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div style='" + style + "'></div>");
        return (HtmlElement)document.QuerySelector("div")!;
    }

    [Theory]
    [InlineData("color: RED", "color: red;")]
    [InlineData("color: #FF0000", "color: rgb(255, 0, 0);")]
    [InlineData("color: rgba(0, 0, 255, 0.5) !important", "color: rgba(0, 0, 255, 0.5) !important;")]
    [InlineData("margin: 1px", "margin: 1px;")]
    [InlineData("margin: 1px 2px", "margin: 1px 2px;")]
    [InlineData("margin: 1px 2px 3px", "margin: 1px 2px 3px;")]
    [InlineData("margin: 1px 2px 1px 2px", "margin: 1px 2px;")]
    [InlineData("margin-top: 1px; margin-right: 1px; margin-bottom: 1px; margin-left: 1px", "margin: 1px;")]
    [InlineData("margin-top: 1px; margin-right: 1px; margin-bottom: 1px", "margin-top: 1px; margin-right: 1px; margin-bottom: 1px;")]
    [InlineData("border-top: 1px solid red", "border-top: 1px solid red;")]
    [InlineData("border: 1px solid red", "border: 1px solid red;")]
    [InlineData("color: red; color: blue", "color: blue;")]
    [InlineData("color: red !important; color: blue", "color: red !important;")]
    [InlineData("color: foo", "")]
    [InlineData("unknown: red", "")]
    [InlineData("--foo:  bar baz ", "--foo: bar baz;")]
    [InlineData("width: calc(1px + 2%)", "width: calc(2% + 1px);")]
    [InlineData("margin: var(--x)", "margin: var(--x);")]
    [InlineData("margin-top: var(--x); margin-right: 1px; margin-bottom: 1px; margin-left: 1px", "margin-top: var(--x); margin-right: 1px; margin-bottom: 1px; margin-left: 1px;")]
    [InlineData("margin-top: v\\61 r(--x); margin-right: 1px; margin-bottom: 1px; margin-left: 1px", "margin-top: v\\61 r(--x); margin-right: 1px; margin-bottom: 1px; margin-left: 1px;")]
    [InlineData("color: inherit", "color: inherit;")]
    [InlineData("margin: initial", "margin: initial;")]
    [InlineData("background-image: url(foo.png)", "background-image: url(\"foo.png\");")]
    [InlineData("font-family: Arial, \"Times New Roman\"", "font-family: Arial, Times New Roman;")]
    [InlineData("font-family: \"serif\", \"34J\", \"A  B\", sans-serif", "font-family: \"serif\", \"34J\", \"A  B\", sans-serif;")]
    [InlineData("content: counter(a, decimal) counters(b, \".\", DECIMAL) counter(c, upper-roman)", "content: counter(a) counters(b, \".\") counter(c, upper-roman);")]
    [InlineData("margin: 10px; margin-inline: 10px; margin-block: 10px; margin-inline-end: 10px; margin-bottom: 10px", "margin-top: 10px; margin-right: 10px; margin-left: 10px; margin-inline-start: 10px; margin-block: 10px; margin-inline-end: 10px; margin-bottom: 10px;")]
    [InlineData("border: 1px; border-top: 1px !important", "border-right: 1px; border-bottom: 1px; border-left: 1px; border-image: none; border-top: 1px !important;")]
    [InlineData("width: 1px; height: 2px", "width: 1px; height: 2px;")]
    [InlineData("width: var({--x})", "width: var({--x});")]
    [InlineData("width: var(--x", "width: var(--x);")]
    [InlineData("width: calc(var(--x)", "width: calc(var(--x));")]
    [InlineData("width: calc(1px + (var(--x) * 2)", "width: calc(1px + (var(--x) * 2));")]
    [InlineData("width: calc(var(--x) )", "width: calc(var(--x) );")]
    public void CssText(string style, string expected)
    {
        var element = CreateElement(style);
        Assert.Equal(expected, element.Style.CssText);
    }

    [Theory]
    [InlineData("@counter-style foo { range: infinite 0, 5 10, 100 infinite; }", "@counter-style foo { range: infinite 0, 5 10, 100 infinite; }")]
    [InlineData("@counter-style foo { range: 0 -1; }", "@counter-style foo { }")]
    [InlineData("@counter-style foo { pad: \"X\" 10; }", "@counter-style foo { pad: 10 \"X\"; }")]
    [InlineData("@counter-style foo { additive-symbols: \"V\" 5, 1 \"I\"; }", "@counter-style foo { additive-symbols: 5 \"V\", 1 \"I\"; }")]
    [InlineData("@counter-style foo { additive-symbols: 1 \"I\", 5 \"V\"; }", "@counter-style foo { }")]
    [InlineData("@counter-style foo { additive-symbols: 1 \"I\", 1 \"V\"; }", "@counter-style foo { }")]
    [InlineData("@counter-style foo { fallback: none; speak-as: none; system: extends none; }", "@counter-style foo { }")]
    [InlineData("@font-face { font-weight: 500 500; font-stretch: 50% 50%; font-style: oblique 0rad; }", "@font-face { font-weight: 500; font-width: 50%; font-style: normal; }")]
    [InlineData("@font-face { font-weight: 400 500; font-style: oblique 10deg 20deg; }", "@font-face { font-weight: 400 500; font-style: oblique 10deg 20deg; }")]
    public void DescriptorValues(string css, string expected)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><style>" + css + "</style>");
        var sheet = (CssStyleSheet)document.StyleSheets[0]!;
        Assert.Equal(expected, sheet.CssRules[0]!.CssText);
    }

    [Theory]
    // Math functions are simplified, the relative units are kept (https://drafts.csswg.org/css-values-4/#serialize-a-math-function)
    [InlineData("width", "calc(2em * 3)", "calc(6em)")]
    [InlineData("width", "calc(10px - 0.5em)", "calc(-0.5em + 10px)")]
    [InlineData("width", "calc(1in)", "calc(96px)")]
    [InlineData("width", "min(1px, 2px)", "calc(1px)")]
    [InlineData("font-weight", "calc(100 + (sign(20cqw - 10px) * 5))", "calc(100 + (5 * sign(20cqw - 10px)))")]
    [InlineData("width", "calc(10px + 1deg)", "")]
    [InlineData("background-image", "conic-gradient(from calc(50% + 30deg), red, blue)", "")]

    // Gradients (https://drafts.csswg.org/css-images-4/#gradients): the default interpolation method is omitted
    [InlineData("background-image", "linear-gradient(in lab 30deg, red, blue)", "linear-gradient(30deg in lab, red, blue)")]
    [InlineData("background-image", "linear-gradient(in srgb, red, blue)", "linear-gradient(red, blue)")]
    [InlineData("background-image", "linear-gradient(in oklab, color(srgb 1 0 0), blue)", "linear-gradient(color(srgb 1 0 0), blue)")]
    [InlineData("background-image", "radial-gradient(at bottom 10% right 20%, red, blue)", "radial-gradient(at right 20% bottom 10%, red, blue)")]
    [InlineData("background-image", "conic-gradient(, red, blue)", "")]

    // Colors (https://drafts.csswg.org/css-color-4/#serializing-color-values, https://drafts.csswg.org/css-color-5/)
    [InlineData("color", "hwb(120 30% 50%)", "rgb(77, 128, 77)")]
    [InlineData("color", "hsl(120deg 500% 50%)", "rgb(0, 255, 0)")]
    [InlineData("color", "hsl(120 100 150)", "rgb(255, 255, 255)")]
    [InlineData("color", "rgb(128 none none)", "color(srgb 0.501961 none none)")]
    [InlineData("color", "hsl(120 none 50)", "hsl(120 none 50%)")]
    [InlineData("color", "color-mix(in srgb, 70% red, 50% blue)", "color-mix(in srgb, red 70%, blue 50%)")]
    [InlineData("color", "color-mix(in hsl, red 50%, blue)", "color-mix(in hsl, red, blue)")]
    [InlineData("color", "color-mix(in srgb, red 150%, blue)", "")]
    [InlineData("color", "rgba(from rebeccapurple r calc(g * 2) 10)", "rgb(from rebeccapurple r calc(2 * g) 10)")]
    [InlineData("color", "hsl(from rebeccapurple calc(h + 1deg) s l)", "")]
    [InlineData("color", "color-layers(normal, red, blue)", "color-layers(red, blue)")]

    // Shorthands and canonical order (https://drafts.csswg.org/cssom/#serialize-a-css-value)
    [InlineData("font", "normal normal normal small-caps 20%/1.2 serif", "small-caps 20% / 1.2 serif")]
    [InlineData("text-decoration-line", "overline underline", "underline overline")]
    [InlineData("grid-row-start", "8 auto", "")]
    [InlineData("grid-row-start", "span 2", "span 2")]
    [InlineData("text-spacing", "space-all no-autospace", "none")]

    // https://drafts.csswg.org/css-flexbox-1/#flex-property: "A unitless zero that is not already preceded by two flex factors
    // must be interpreted as a flex factor"; none is serialized as 0 0 auto, as in the browsers
    [InlineData("flex", "0 1 1", "")]
    [InlineData("flex", "0 0 4", "")]
    [InlineData("flex", "1 2 0", "1 2 0px")]
    [InlineData("flex", "0 1 10px", "0 1 10px")]
    [InlineData("flex", "none", "0 0 auto")]

    // https://drafts.csswg.org/css-flexbox-2/#flex-wrap-property: balance behaves as wrap, which is omitted next to it
    [InlineData("flex-wrap", "wrap balance", "balance")]
    [InlineData("flex-wrap", "BALANCE wrap", "balance")]
    [InlineData("flex-wrap", "balance wrap-reverse", "wrap-reverse balance")]
    [InlineData("flex-wrap", "nowrap balance", "")]
    [InlineData("-webkit-flex-wrap", "balance wrap", "balance")]
    [InlineData("flex-flow", "balance wrap row", "balance")]
    [InlineData("flex-flow", "balance wrap-reverse column", "column wrap-reverse balance")]
    [InlineData("flex-flow", "wrap row balance", "")]
    [InlineData("flex-line-count", "3", "3")]
    [InlineData("flex-line-count", "0", "")]

    // Custom properties (https://drafts.csswg.org/css-variables/#defining-variables)
    [InlineData("--x", " a /* c */ ", "a")]
    [InlineData("--x", "a/* c */b", "a/* c */b")]
    [InlineData("--x", " ", "")]
    [InlineData("--x y", "a", "")]
    public void SpecifiedValue(string property, string value, string expected)
    {
        var element = CreateElement("");
        element.Style.SetProperty(property, value);
        Assert.Equal(expected, element.Style.GetPropertyValue(property));
    }

    [Fact]
    public void SetPropertyUpdatesAttribute()
    {
        var element = CreateElement("color: red");
        element.Style.SetProperty("margin", "1px 2px");
        Assert.Equal("color: red; margin: 1px 2px;", element.GetAttribute("style"));
        Assert.Equal("2px", element.Style.GetPropertyValue("margin-left"));
        Assert.Equal("2px", element.Style.MarginRight);
        Assert.Equal(5u, element.Style.Length);

        element.Style.MarginLeft = "3px";
        Assert.Equal("1px 2px 1px 3px", element.Style.Margin);

        Assert.Equal("1px 2px 1px 3px", element.Style.RemoveProperty("margin"));
        Assert.Equal("color: red;", element.GetAttribute("style"));

        element.SetAttribute("style", "float: left");
        Assert.Equal("left", element.Style.CssFloat);
        Assert.Equal(1u, element.Style.Length);
        Assert.Equal("float", element.Style.Item(0));
    }

    [Fact]
    public void Priority()
    {
        var element = CreateElement("");
        element.Style.SetProperty("color", "red", "IMPORTANT");
        Assert.Equal("important", element.Style.GetPropertyPriority("color"));
        element.Style.SetProperty("color", "red", "invalid");
        Assert.Equal("important", element.Style.GetPropertyPriority("color"));
        element.Style.SetProperty("margin", "0", "important");
        Assert.Equal("important", element.Style.GetPropertyPriority("margin"));
        Assert.Equal("color: red !important; margin: 0px !important;", element.Style.CssText);
    }

    [Fact]
    public void StyleSheets()
    {
        var document = HtmlDocumentParser.ParseDocument("""
            <!DOCTYPE html>
            <style media="screen">
            @import url(foo.css) screen;
            @namespace svg url(http://www.w3.org/2000/svg);
            div > p, .a:hover { color: red; margin: 0 }
            @media (min-width:100px) and (max-width: 200PX) { a { color: blue } }
            @supports (display:flex) { b { display: flex } }
            @font-face { font-family: foo; src: url(foo.woff) }
            @keyframes spin { from { opacity: 0 } 50% { opacity: 0.5 } to { opacity: 1 } }
            .parent { color: red; & .child { color: blue } > .x { color: green } }
            @layer a, b.c;
            </style>
            """);
        Assert.Equal(1u, document.StyleSheets.Length);
        var sheet = document.StyleSheets[0];
        Assert.Equal("screen", sheet.Media.MediaText);
        var rules = sheet.CssRules;
        Assert.Equal(9u, rules.Length);
        Assert.Equal("@import url(\"foo.css\") screen;", rules[0].CssText);
        Assert.Equal("@namespace svg url(\"http://www.w3.org/2000/svg\");", rules[1].CssText);
        Assert.Equal("div > p, .a:hover { color: red; margin: 0px; }", rules[2].CssText);
        Assert.Equal("@media (min-width: 100px) and (max-width: 200px) {\n  a { color: blue; }\n}", rules[3].CssText);
        Assert.Equal("@supports (display:flex) {\n  b { display: flex; }\n}", rules[4].CssText);
        Assert.Equal("@font-face { font-family: foo; src: url(\"foo.woff\"); }", rules[5].CssText);
        Assert.Equal("@keyframes spin {\n  0% { opacity: 0; }\n  50% { opacity: 0.5; }\n  100% { opacity: 1; }\n}", rules[6].CssText);
        Assert.Equal(".parent {\n  color: red;\n  & .child { color: blue; }\n  & > .x { color: green; }\n}", rules[7].CssText);
        Assert.Equal("@layer a, b.c;", rules[8].CssText);

        var styleRule = (CssStyleRule)rules[2];
        styleRule.SelectorText = "SPAN.b";
        Assert.Equal("SPAN.b", styleRule.SelectorText);
        styleRule.SelectorText = "invalid[";
        Assert.Equal("SPAN.b", styleRule.SelectorText);
        Assert.Same(sheet, styleRule.ParentStyleSheet);
    }

    [Theory]
    [InlineData("(width:100px)", "(width: 100px)", "", "(width: 100px)")]
    [InlineData("NAMe   (inline-sizE  < 1300px )", "NAMe (inline-size < 1300px)", "NAMe", "(inline-size < 1300px)")]
    [InlineData("(100px>WIDTH>10px)", "(100px > width > 10px)", "", "(100px > width > 10px)")]
    [InlineData("card", "card", "card", "")]
    [InlineData("(width),(height) ,--foo", "(width), (height), --foo", "", "")]
    [InlineData("STyle(--foo:bar   baz)", "style(--foo: bar   baz)", "", "style(--foo: bar   baz)")]
    [InlineData("style(( (--a: b) or ( prop: val  ) ))", "style(((--a: b) or ( prop: val  )))", "", "style(((--a: b) or ( prop: val  )))")]
    public void ContainerRule(string prelude, string conditionText, string containerName, string containerQuery)
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync("@container " + prelude + " { a { color: red } }");
        var rule = Assert.IsType<CssContainerRule>(sheet.CssRules[0]);
        Assert.Equal(conditionText, rule.ConditionText);
        Assert.Equal(containerName, rule.ContainerName);
        Assert.Equal(containerQuery, rule.ContainerQuery);
        Assert.Equal("@container " + conditionText + " {\n  a { color: red; }\n}", rule.CssText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("and (width)")]
    [InlineData("name name")]
    [InlineData("(width),")]
    [InlineData("screen and (width: 100px)")]
    [InlineData("not (width) and (height)")]
    [InlineData("(width) and (height) or (inline-size)")]
    public void ContainerRule_Invalid(string prelude)
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync("@container " + prelude + " { a { color: red } }");
        Assert.Equal(0u, sheet.CssRules.Length);
    }

    [Theory]
    [InlineData("@scope (.a) to (.b) { div { color: red } }", "@scope (.a) to (.b) {\n  div { color: red; }\n}")]
    [InlineData("@scope to (> .b) { > .c { color: red } }", "@scope to (> .b) {\n  > .c { color: red; }\n}")]
    [InlineData("@scope (.a) { @scope (> .b) { :scope { color: red } } }", "@scope (.a) {\n  @scope (> .b) {\n  :scope { color: red; }\n}\n}")]
    [InlineData(".a { @scope (.b) { & { color: red } } }", ".a {\n  @scope (.b) {\n  & { color: red; }\n}\n}")]
    public void ScopeRule(string text, string expected)
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync(text);
        Assert.Equal(expected, sheet.CssRules[0].CssText);
    }

    [Theory]
    [InlineData("override-colors: 0 red", "0 red")]
    [InlineData("override-colors: 0 currentcolor", "")]
    [InlineData("override-colors: 0 color-mix(in lch, red, canvas)", "")]
    [InlineData("override-colors: 0 light-dark(white, black)", "")]
    public void FontPaletteValuesOverrideColors(string declaration, string expected)
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync("@font-palette-values --a { " + declaration + " }");
        Assert.Equal(expected, ((CssFontPaletteValuesRule)sheet.CssRules[0]).OverrideColors);
    }

    [Theory]
    [InlineData("A4", "a4")]
    [InlineData("letter portrait", "letter")]
    [InlineData("landscape JIS-B5", "jis-b5 landscape")]
    public void PageSize(string value, string expected)
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync("@page { size: " + value + "; @top-center { content: \"a\" } }");
        var rule = (CssPageRule)sheet.CssRules[0];
        Assert.Equal(expected, rule.Style.GetPropertyValue("size"));
        Assert.Equal("\"a\"", ((CssMarginRule)rule.CssRules[0]).Style.GetPropertyValue("content"));
    }

    [Theory]
    [InlineData("@scope (div::before) {}")]
    [InlineData("@scope (.a) to (div::after) {}")]
    public void ScopeRule_Invalid(string text)
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync(text);
        Assert.Equal(0u, sheet.CssRules.Length);
    }

    [Fact]
    public void ScopeRule_InsertRuleParsesScopedSelectors()
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync("@scope (.a) {}");
        var scope = (CssScopeRule)sheet.CssRules[0];
        scope.InsertRule("> .b { color: red }");
        Assert.Equal("> .b", ((CssStyleRule)scope.CssRules[0]).SelectorText);
    }

    [Fact]
    public void ImportRule_InvalidLayerIsAMediaQuery()
    {
        var document = HtmlDocumentParser.ParseDocument("<style>@import url(a.css) layer(A B);</style>");
        var rule = (CssImportRule)document.StyleSheets[0].CssRules[0];
        Assert.Null(rule.LayerName);
        Assert.Equal(1u, rule.Media.Length);
    }

    [Fact]
    public void InsertAndDeleteRules()
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync("a { color: red }");
        Assert.Equal(1u, sheet.InsertRule("b { color: blue }", 1));
        Assert.Equal(2u, sheet.CssRules.Length);
        Assert.Throws<DomException>(() => sheet.InsertRule("a {} b {}", 0));
        Assert.Throws<DomException>(() => sheet.InsertRule("c {}", 5));
        sheet.DeleteRule(0);
        Assert.Equal("b { color: blue; }", sheet.CssRules[0].CssText);
        Assert.Null(sheet.OwnerNode);
    }

    [Fact]
    public void MediaListParsing()
    {
        var list = new MediaList("  SCREEN  ,  print and (COLOR), bad$, (width >= 100px), not all and (monochrome)");
        Assert.Equal("screen, print and (color), not all, (width >= 100px), not all and (monochrome)", list.MediaText);
        list.AppendMedium("screen");
        Assert.Equal(5u, list.Length);
        list.DeleteMedium("SCREEN");
        Assert.Equal(4u, list.Length);
        Assert.Throws<DomException>(() => list.DeleteMedium("tv"));
    }

    // An unknown feature, an invalid value or a function is a <general-enclosed>: it is serialized as specified
    [Theory]
    [InlineData("(--FOO: bar)", "(--FOO: bar)")]
    [InlineData("\\{screen and (--\\(FOO: bar)", "\\{screen and (--\\(FOO: bar)")]
    [InlineData("(prefers-color-scheme: light/dark)", "(prefers-color-scheme: light/dark)")]
    [InlineData("(prefers-contrast > increase)", "(prefers-contrast > increase)")]
    [InlineData("(min-orientation:  portrait)", "(min-orientation: portrait)")]
    [InlineData("(color: 1.0)", "(color: 1.0)")]
    [InlineData("(display-mode: 1%)", "(display-mode: 1%)")]
    [InlineData("(resolution: -3dpi)", "(resolution: -3dpi)")]
    [InlineData("not unknown(width)", "not unknown(width)")]
    [InlineData("(unknown) or (COLOR)", "(unknown) or (color)")]
    [InlineData("(width < env(safe-area-inset-top, 1px))", "(width < env(safe-area-inset-top, 1px))")]
    [InlineData("(a #.5)", "(a #/**/0.5)")]
    [InlineData("(a: #0.5)", "(a: #\\30 /**/0.5)")]
    [InlineData("(a/**/b)", "(a/**/b)")]
    [InlineData("(color]", "not all")]
    [InlineData("unknown(\"a\nb)", "not all")]
    [InlineData("(orientation) and not (orientation)", "not all")]
    public void MediaListGeneralEnclosed(string mediaText, string expected) => Assert.Equal(expected, new MediaList(mediaText).MediaText);

    [Fact]
    public void CustomMediaRule()
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync("@custom-media --narrow (max-width: 30em), (hover); @custom-media --yes TRUE; @custom-media --empty; @custom-media --a true!; @custom-media b (color); @custom-media --c(color);");
        Assert.Equal(3u, sheet.CssRules.Length);

        var narrow = Assert.IsType<CssCustomMediaRule>(sheet.CssRules[0]);
        Assert.Equal("--narrow", narrow.Name);
        Assert.Equal("(max-width: 30em), (hover)", narrow.Query.MediaList!.MediaText);
        Assert.Equal("@custom-media --narrow (max-width: 30em), (hover);", narrow.CssText);

        var yes = Assert.IsType<CssCustomMediaRule>(sheet.CssRules[1]);
        Assert.True(yes.Query.BooleanValue);
        Assert.Equal("@custom-media --yes true;", yes.CssText);

        var empty = Assert.IsType<CssCustomMediaRule>(sheet.CssRules[2]);
        Assert.Equal(0u, empty.Query.MediaList!.Length);
        Assert.Equal("@custom-media --empty;", empty.CssText);
    }

    [Theory]
    [InlineData("display", "flex", true)]
    [InlineData("display", "nope", false)]
    [InlineData("--x", "anything", true)]
    [InlineData("color", "red !important", false)]

    // The -webkit- prefixed gradients (https://compat.spec.whatwg.org/#css-gradient-functions) and -webkit-gradient()
    [InlineData("background-image", "-webkit-linear-gradient(left, red, blue)", true)]
    [InlineData("background-image", "-webkit-linear-gradient(top left, red 10%, blue)", true)]
    [InlineData("background-image", "-webkit-linear-gradient(45deg, red, blue)", true)]
    [InlineData("background-image", "-webkit-repeating-linear-gradient(red, blue 20px)", true)]
    [InlineData("background-image", "-webkit-linear-gradient(to left, red, blue)", false)]
    [InlineData("background-image", "-webkit-linear-gradient(red)", false)]
    [InlineData("background-image", "-webkit-radial-gradient(center, circle cover, red, blue)", true)]
    [InlineData("background-image", "-webkit-radial-gradient(1px 2px, 3% 4%, red, blue)", true)]
    [InlineData("background-image", "-webkit-repeating-radial-gradient(ellipse, red, blue)", true)]
    [InlineData("background-image", "-webkit-radial-gradient(1px 2px, 3% 4% red, blue)", false)]
    [InlineData("background-image", "-webkit-radial-gradient(1px 2px, -3% 4%, red, blue)", false)]
    [InlineData("background-image", "-webkit-radial-gradient(circle at center, red, blue)", false)]
    [InlineData("background-image", "-webkit-gradient(linear, left top, left bottom, from(red), to(blue))", true)]
    [InlineData("background-image", "-webkit-gradient(radial, 50% 50%, 0, 50% 50%, 10, color-stop(0.5, red))", true)]
    [InlineData("background-image", "-webkit-gradient(linear, left top, left bottom, color-stop(calc(0.5 + 0.001 * sign(1em - 1px)), blue))", true)]
    [InlineData("background-image", "-webkit-gradient(linear, left top, left bottom)", true)]
    [InlineData("background-image", "-webkit-gradient(conic, left top, left bottom)", false)]
    [InlineData("background-image", "-webkit-gradient(radial, 50% 50%, -1, 50% 50%, 10)", false)]
    public void SupportsDeclaration(string property, string value, bool expected) => Assert.Equal(expected, WebPlatform.Css.Om.Css.Supports(property, value));

    [Theory]
    [InlineData("(display: flex)", true)]
    [InlineData("display: flex", true)]
    [InlineData("not (display: nope)", true)]
    [InlineData("(display: flex) and (color: nope)", false)]
    [InlineData("(display: flex) or (color: nope)", true)]
    [InlineData("selector(a > b)", true)]
    [InlineData("selector(a >)", false)]
    [InlineData("(display: flex) and (color: red) or (width: 1px)", false)]
    [InlineData("selector(:is(.a, :unknown))", false)]
    [InlineData("selector(:where(.a))", true)]
    [InlineData("at-rule(@media)", true)]
    [InlineData("at-rule( @supports )", true)]
    [InlineData("at-rule(@charset)", false)]
    [InlineData("at-rule(@unknown) or (color: red)", true)]
    [InlineData("font-format(woff2)", true)]
    [InlineData("font-format(TrueType)", true)]
    [InlineData("font-format(svg)", false)]
    [InlineData("font-format('woff')", false)]
    [InlineData("font-tech(color-COLRv1)", true)]
    [InlineData("font-tech(features-graphite)", false)]
    [InlineData("font-tech(variations, palettes)", false)]
    public void SupportsCondition(string condition, bool expected) => Assert.Equal(expected, WebPlatform.Css.Om.Css.Supports(condition));

    [Theory]
    [InlineData("a", "a")]
    [InlineData("0a", "\\30 a")]
    [InlineData("-", "\\-")]
    [InlineData("a b", "a\\ b")]
    public void Escape(string value, string expected) => Assert.Equal(expected, WebPlatform.Css.Om.Css.Escape(value));

    [Fact]
    public void AllShorthand()
    {
        var element = CreateElement("");
        element.Style.CssText = "margin: 10px; all: revert";
        Assert.Equal("revert", element.Style.GetPropertyValue("margin-top"));
        Assert.Equal("revert", element.Style.GetPropertyValue("margin"));
        Assert.Equal("revert", element.Style.GetPropertyValue("font"));
        Assert.Equal("all: revert;", element.Style.CssText);
    }

    [Theory]
    [InlineData("background-position: top", "background-position: center top;")]
    [InlineData("background-position: top 10px left", "background-position: left top 10px;")]
    [InlineData("background-repeat: repeat no-repeat", "background-repeat: repeat-x;")]
    [InlineData("background: url(a.png) red", "background: url(\"a.png\") red;")]
    [InlineData("flex: 0", "flex: 0 1 0%;")]
    [InlineData("flex: none", "flex: 0 0 auto;")]
    [InlineData("white-space: pre", "white-space: pre;")]
    [InlineData("font: 10px/1 Ahem", "font: 10px / 1 Ahem;")]
    [InlineData("border-top: 1px", "border-top: 1px;")]
    [InlineData("border: 1px solid red", "border: 1px solid red;")]
    public void ShorthandSerialization(string style, string expected)
    {
        var element = CreateElement(style);
        Assert.Equal(expected, element.Style.CssText);
    }

    [Theory]
    [InlineData("transform: scale(250%) scaleX(720%) scale3d(50%, 250%, 300%)", "transform: scale(2.5) scalex(7.2) scale3d(0.5, 2.5, 3);")]
    [InlineData("transform: rotate(0) skew(0, -90deg) skewX(0) rotate3d(0, 0, 1, 0)", "transform: rotate(0deg) skew(0deg, -90deg) skewx(0deg) rotate3d(0, 0, 1, 0deg);")]
    [InlineData("transform: translate3d(0, 0, -50px) perspective(0)", "transform: translate3d(0px, 0px, -50px) perspective(0px);")]
    [InlineData("scale: 100% 100% 1", "scale: 1;")]
    [InlineData("scale: 100% 200% 1", "scale: 1 2;")]
    [InlineData("scale: 100 100 2", "scale: 100 100 2;")]
    [InlineData("scale: calc(200%) calc(3)", "scale: calc(200%) calc(3);")]
    [InlineData("translate: 100px 0px 0px", "translate: 100px;")]
    [InlineData("translate: 100px 0% 0", "translate: 100px 0%;")]
    [InlineData("translate: 0em 0em 100px", "translate: 0em 0em 100px;")]
    [InlineData("rotate: 400grad 100 200 300", "rotate: 100 200 300 400grad;")]
    [InlineData("rotate: -0.5 0 0 400grad", "rotate: x -400grad;")]
    [InlineData("rotate: 0 1 0 400grad", "rotate: y 400grad;")]
    [InlineData("rotate: 400grad z", "rotate: 400grad;")]
    [InlineData("rotate: 0 0 0 400grad", "rotate: 0 0 0 400grad;")]
    [InlineData("transform-origin: top", "transform-origin: center top;")]
    [InlineData("transform-origin: -4%", "transform-origin: -4% center;")]
    [InlineData("perspective-origin: left", "perspective-origin: left center;")]
    public void TransformSerialization(string style, string expected)
    {
        var element = CreateElement(style);
        Assert.Equal(expected, element.Style.CssText);
    }

    [Fact]
    public void Hue_MathFunctionWithPercentage_IsInvalid()
    {
        // A percentage cannot be resolved in the hue (a <number> or an <angle>), even in sign()
        Assert.Equal("", CreateElement("color: hsl(calc(sign(50%) * 1deg) 82% 43%)").Style.Color);
        Assert.NotEqual("", CreateElement("color: hsl(calc(sign(1em - 1px) * 1deg) 82% 43%)").Style.Color);
        Assert.NotEqual("", CreateElement("color: hsl(120deg calc(50% * sign(1em - 1px)) 43%)").Style.Color);
    }

    [Fact]
    public void ShorthandWithVariableReference_OverriddenLonghand_SerializesTheEmptyLonghands()
    {
        var element = CreateElement("margin: var(--a); margin-top: 10px");
        Assert.Equal("margin-right: ; margin-bottom: ; margin-left: ; margin-top: 10px;", element.Style.CssText);
    }

    [Fact]
    public void NestedRule_RelativeSelectorStartingWithACombinator()
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync(".a { > & .b { color: green } }");
        Assert.Equal("& > & .b", ((CssStyleRule)((CssStyleRule)sheet.CssRules[0]!).CssRules[0]!).SelectorText);
    }

    [Fact]
    public void NestedRule_InsertRuleValidAtTopLevel_ThrowsHierarchyRequestError()
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync(".a { }");
        var exception = Assert.Throws<DomException>(() => ((CssStyleRule)sheet.CssRules[0]!).InsertRule("@font-face {}", 0));
        Assert.Equal("HierarchyRequestError", exception.Name);
    }

    [Theory]
    [InlineData("@function --f() { }", "@function --f() { }")]
    [InlineData("@function --f(--x, --y <length>: 1px) returns type(<length> | auto) { result: var(--x); }", "@function --f(--x, --y <length>: 1px) returns type(<length> | auto) { result: var(--x); }")]
    [InlineData("@function --f(--x type(*)) returns type(*) { --a: 1; result: 2; result: 3; }", "@function --f(--x) { --a: 1; result: 3; }")]
    [InlineData("@function --f() { color: red; result: 1 !important; --a: 2; }", "@function --f() { --a: 2; }")]
    public void FunctionRule(string text, string expected)
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync(text);
        Assert.Equal(expected, sheet.CssRules[0].CssText);
    }

    [Theory]
    [InlineData("@function --f () {}")]
    [InlineData("@function f() {}")]
    [InlineData("@function --f(--x, --x) {}")]
    [InlineData("@function --f(--x <length>: red) {}")]
    [InlineData("@function --f(--x <length> | auto) {}")]
    [InlineData("@function --f(--x <transform-list>+) {}")]
    [InlineData("@function --f(--x) returns * {}")]
    [InlineData(".a { @function --f() {} }")]
    public void FunctionRule_Invalid(string text)
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync(text);
        Assert.True(sheet.CssRules.Length == 0 || sheet.CssRules[0] is CssStyleRule { CssRules.Length: 0 });
    }

    [Theory]
    [InlineData("@mixin --m { }", "@mixin --m {\n}")]
    [InlineData("@mixin --m() { --a: 1; @result { color: green; & { --b: 2; } } }", "@mixin --m {\n  --a: 1;\n  @result {\n  color: green;\n  & { --b: 2; }\n}\n}")]
    [InlineData("@mixin --m(--x type(<length>): 1em, --y) { @result { @contents } }", "@mixin --m(--x <length>: 1em, --y) {\n  @result {\n  @contents;\n}\n}")]
    [InlineData("@mixin --m { color: red !important; @layer a; .a { } @result { @layer a; @contents { color: red; } } }", "@mixin --m {\n  @result {\n  @contents { color: red; }\n}\n}")]
    [InlineData("#a { color: red; @apply --m; @apply --n(1, { 2, 3 }) { color: green; } }", "#a {\n  color: red;\n  @apply --m;\n  @apply --n(1, { 2, 3 }) { color: green; }\n}")]
    [InlineData("#a { @apply --m { } }", "#a {\n  @apply --m { }\n}")]
    [InlineData("#a { @private { --a: 1px; --b <color>: red; --c type(<length>): 2px; --d; color: red; --e: 1 !important; } }", "#a {\n  @private { --a: 1px; --b <color>: red; --c <length>: 2px; }\n}")]
    public void MixinRules(string text, string expected)
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync(text);
        Assert.Equal(expected, sheet.CssRules[sheet.CssRules.Length - 1]!.CssText);
    }

    [Theory]
    [InlineData("@mixin m() { }")]
    [InlineData("@mixin --m(x) { }")]
    [InlineData("@mixin --m(--x: red !important) { }")]
    [InlineData("@apply --m;")]
    [InlineData("@private { --x: 1; }")]
    [InlineData("@media all { @apply --m; }")]
    [InlineData("#a { @mixin --m { } }")]
    [InlineData("#a { @apply --m(red !important); }")]
    [InlineData("#a { @apply m; }")]
    [InlineData("#a { @contents; }")]
    public void MixinRules_Invalid(string text)
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync(text);
        Assert.True(sheet.CssRules.Length == 0 || sheet.CssRules[0] is CssGroupingRule { CssRules.Length: 0 });
    }

    [Fact]
    public void MixinRules_ObjectModel()
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync("@mixin --m(--x <length>: 1px, --y) { @result { @contents; } } #a { @apply --m(1px, { a, b }) { color: red; } @apply --m; }");
        var mixin = Assert.IsType<CssMixinRule>(sheet.CssRules[0]);
        Assert.Equal("--m", mixin.Name);
        Assert.True(mixin.Contents);
        Assert.Equal(("--x", "<length>", "1px"), (mixin.GetParameters()[0].Name, mixin.GetParameters()[0].Type, mixin.GetParameters()[0].DefaultValue));
        var rule = Assert.IsType<CssStyleRule>(sheet.CssRules[1]);
        var block = Assert.IsType<CssApplyBlockRule>(rule.CssRules[0]);
        Assert.Equal("--m", block.Name);
        Assert.Equal(["1px", "a, b"], block.GetArguments());
        Assert.IsType<CssNestedDeclarations>(block.CssRules[0]);
        var statement = Assert.IsType<CssApplyStatementRule>(rule.CssRules[1]);
        Assert.Empty(statement.GetArguments());
        Assert.Throws<DomException>(() => sheet.InsertRule("@apply --m;"));
        Assert.Throws<DomException>(() => mixin.InsertRule("#a { }"));
        mixin.InsertRule("--z: 1");
        Assert.IsType<CssNestedDeclarations>(mixin.CssRules[0]);
        ((CssMixinResultRule)mixin.CssRules[1]!).InsertRule("@contents { color: red; }", 1);
        Assert.IsType<CssContentsBlockRule>(((CssMixinResultRule)mixin.CssRules[1]!).CssRules[1]);
        rule.InsertRule("@private { --p: 1; }");
        Assert.IsType<CssPrivateRule>(rule.CssRules[0]);
    }

    [Fact]
    public void FunctionRule_ObjectModel()
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync("@function --f(--x type(<length> | auto): 10px, --y) returns <length>+ { --a: 1; @media (width > 0px) { result: 2; } result: 3; }");
        var rule = Assert.IsType<CssFunctionRule>(sheet.CssRules[0]);
        Assert.Equal("--f", rule.Name);
        Assert.Equal("<length>+", rule.ReturnType);
        var parameters = rule.GetParameters();
        Assert.Equal(2, parameters.Count);
        Assert.Equal(("--x", "<length> | auto", "10px"), (parameters[0].Name, parameters[0].Type, parameters[0].DefaultValue));
        Assert.Equal(("--y", "*", null), (parameters[1].Name, parameters[1].Type, parameters[1].DefaultValue));
        Assert.Equal(3u, rule.CssRules.Length);
        var declarations = Assert.IsType<CssFunctionDeclarations>(rule.CssRules[0]);
        Assert.Equal("1", declarations.Style.GetPropertyValue("--a"));
        Assert.Equal("", declarations.Style.Result);
        Assert.IsType<CssMediaRule>(rule.CssRules[1]);
        var last = Assert.IsType<CssFunctionDeclarations>(rule.CssRules[2]);
        Assert.Equal("3", last.Style.Result);
        last.Style.Result = "4";
        last.Style.CssText = "color: red; --b: 5; result: 6";
        Assert.Equal("--b: 5; result: 6;", last.Style.CssText);
    }

    [Fact]
    public void SupportsRule_ConditionTextNormalizesAtRuleFunctions()
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync("@supports at-rule( @--\\31 23 ) and (color: red) { }");
        Assert.Equal("at-rule(@--123) and (color: red)", ((CssSupportsRule)sheet.CssRules[0]!).ConditionText);
    }

    // A bad string, a bad URL or an unmatched closing bracket is not an <any-value>: the condition is not a <general-enclosed>
    [Theory]
    [InlineData("@supports (a \"b\n) { }")]
    [InlineData("@supports (display: grid) or (a ]) { }")]
    [InlineData("@supports unknown(url(a b)) { }")]
    [InlineData("@supports selector(\"a\n) { }")]
    public void SupportsRule_InvalidGeneralEnclosed(string css)
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync(css);
        Assert.Equal(0u, sheet.CssRules.Length);
    }

    [Fact]
    public void SetProperty_MovesTheDeclarationAfterTheDeclarationsOfItsLogicalGroupWithAnotherMappingLogic()
    {
        var element = CreateElement("padding-top: 1px; padding-left: 2px; padding-block-start: 3px");
        element.Style.SetProperty("padding-top", "1px");
        Assert.Equal("padding-left: 2px; padding-block-start: 3px; padding-top: 1px;", element.Style.CssText);

        // The declaration is updated in place when it is the last one of its group with its mapping logic
        element.Style.SetProperty("padding-top", "4px");
        Assert.Equal("padding-left: 2px; padding-block-start: 3px; padding-top: 4px;", element.Style.CssText);
    }

    [Fact]
    public void FontFeatureValuesRule()
    {
        var sheet = new CssStyleSheet(HtmlDocumentParser.ParseDocument("<!DOCTYPE html>"));
        sheet.ReplaceSync("""
            @font-feature-values Font One, "Font Two" {
              font-display: swap;
              @styleset { nice-style: 12 4; invalid: -1; }
              @swash { fancy: 1; too-many: 1 2; }
              @styleset { other: 3; }
              @unknown { a: 1; }
            }
            """);

        var rule = Assert.IsType<CssFontFeatureValuesRule>(sheet.CssRules[0]);
        Assert.Equal(CssRule.FontFeatureValuesRule, rule.Type);
        Assert.Equal("Font One, Font Two", rule.FontFamily);
        Assert.Equal([12u, 4u], rule.Styleset.Get("nice-style"));
        Assert.Equal(["nice-style", "other"], rule.Styleset.Select(entry => entry.Key));
        Assert.Equal(["fancy"], rule.Swash.Select(entry => entry.Key));
        Assert.Empty(rule.Annotation);
        Assert.Equal("@font-feature-values Font One, Font Two {\n  font-display: swap;\n  @styleset { nice-style: 12 4; other: 3; }\n  @swash { fancy: 1; }\n}", rule.CssText);

        rule.FontFamily = "serif";
        Assert.Equal("Font One, Font Two", rule.FontFamily);
        rule.FontFamily = "'Other'";
        Assert.Equal("Other", rule.FontFamily);

        rule.Swash.Set("fancy", 2);
        Assert.Equal([2u], rule.Swash.Get("fancy"));
        Assert.Equal("InvalidAccessError", Assert.Throws<DomException>(() => rule.Swash.Set("fancy", [1u, 2u])).Name);
        rule.CharacterVariant.Set("cv", [1u, 2u]);
        Assert.True(rule.Styleset.Delete("other"));
        rule.Swash.Clear();
        Assert.Equal("@font-feature-values Other {\n  font-display: swap;\n  @styleset { nice-style: 12 4; }\n  @character-variant { cv: 1 2; }\n}", rule.CssText);
    }

    [Fact]
    public void Documentation_StyleSheets()
    {
        #region WebPlatform_StyleSheets
        var document = HtmlDocumentParser.ParseDocument("""
            <style>
              .button { color: white; background-color: #2563eb }
              @media (min-width: 800px) { .button { padding: 8px 16px } }
            </style>
            """);

        var sheet = document.StyleSheets[0];
        var button = sheet.CssRules.OfType<CssStyleRule>().Single(rule => rule.SelectorText == ".button");
        Assert.Equal("rgb(37, 99, 235)", button.Style.GetPropertyValue("background-color"));

        var media = sheet.CssRules.OfType<CssMediaRule>().Single();
        Assert.Equal("(min-width: 800px)", media.Media.MediaText);

        // Edit the rules: the documents that use the style sheet see the changes
        button.Style.SetProperty("border-radius", "6px");
        sheet.InsertRule(".button:hover { opacity: 0.8 }", index: 2); // at the end
        Assert.Equal(".button { color: white; background-color: rgb(37, 99, 235); border-radius: 6px; }", button.CssText);

        // A style sheet without document (a constructable style sheet)
        var standalone = new CssStyleSheet();
        standalone.ReplaceSync("p { margin: 0 } h1 { font-size: 2em }");
        Assert.Equal(2, standalone.CssRules.Count);
        #endregion
    }
}
