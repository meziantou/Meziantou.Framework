using System.Text;

namespace Meziantou.Framework.Language.Css.Tests;

public sealed class CssSyntaxTreeTests
{
    private const string ModernStyleSheet = """
        @charset "utf-8";
        @import url("theme.css") layer(theme) supports(display: grid) screen and (min-width: 600px);
        @namespace svg url(http://www.w3.org/2000/svg);
        @layer reset, base, components.cards;

        :root {
          --brand: oklch(70% 0.1 200);
          --spacing: { a: b };
          color-scheme: light dark;
        }

        /* Nesting */
        .card {
          padding: var(--spacing, 1rem) !important;
          & > .title { font-weight: bold; }
          &:hover, &:focus-visible { outline: 2px solid currentColor; }
          .dark & { background: black; }
          > img + p ~ span { margin: 0; }
          @media (width >= 600px) and (orientation: landscape) {
            padding: 2rem;
            & .title { font-size: 1.5em; }
          }
          @container sidebar (inline-size > 30em) or style(--compact: true) {
            display: grid;
          }
          @supports not (display: grid) { float: left; }
        }

        @layer components.cards {
          .card:has(> img:not([alt=""])) { border: 1px solid; }
          .card:is(.a, .b):where(:nth-child(2n+1 of .visible)) { color: red; }
        }

        @scope (.card) to (> .content) {
          img { border-radius: 50%; }
        }

        @container (400px <= width < 800px) { .x { y: z; } }

        @media screen and (16 / 9 <= aspect-ratio), print, not all and (monochrome) {
          a[href^="https:" i]::after { content: " \2197"; }
        }

        @supports selector(:has(a)) and font-tech(color-COLRv1) and at-rule(@starting-style) {
          dialog[open] { opacity: 1; @starting-style { opacity: 0; } }
        }

        @keyframes fade { from { opacity: 0; } 50% { opacity: .5; } to { opacity: 1; } }
        @keyframes reveal { entry 0%, exit 100% { opacity: 0; } }

        @font-face { font-family: "Inter"; src: url(inter.woff2) format("woff2"); unicode-range: U+0000-00FF, U+0131, u+4??; }
        @property --angle { syntax: "<angle>"; inherits: false; initial-value: 0deg; }
        @counter-style thumbs { system: cyclic; symbols: "👍"; suffix: " "; }
        @font-feature-values Font One, "Font Two" { font-display: swap; @swash { fancy: 1; } }
        @font-palette-values --identifier { font-family: Bixa; }
        @page wide:first { margin: 1in; @top-left { content: "Title"; } }
        @position-try --custom-bottom { top: anchor(bottom); }
        @view-transition { navigation: auto; }
        ::view-transition-group(*.card) { animation-duration: .5s; }
        ::part(label)::before, ::slotted(span.x), :host(.dark) ::highlight(search) { color: red; }
        svg|circle, *|*, |rect, [svg|href], a || b { fill: none; }
        :lang(en, "fr-*"):dir(rtl):state(checked) {}
        input:user-invalid, video:playing, ::details-content, ::scroll-marker {}
        @custom-media --narrow-window (max-width: 30em);
        @function --double(--x) { result: calc(var(--x) * 2); }
        """;

    public static TheoryData<string> RoundTripSamples =>
    [
        "",
        " ",
        "/* comment */",
        "/* unterminated",
        "\n\r\n\r\f",
        "a{}",
        "a { color: red }",
        ModernStyleSheet,
        "a { content: \"abc",
        "a { content: 'abc\ndef' }",
        "a { b: url(a b) }",
        "a { b: url( a ) url(\"x\") url(a\\)b) }",
        "a { color: }",
        "a { color: red !imp }",
        "a b; c {}",
        "a; b {} ; c {}",
        ".a { foo bar; x: y }",
        "--x:hover { }",
        "a { --x:hover { } }",
        "a {",
        "a { b {",
        "a { b: c } }",
        "}}}",
        "{{{",
        "((([[[",
        ")))]]]",
        "a::before:hover.x {}",
        "#1a {}",
        "a:hoverr {}",
        ":is(a, 1b) {}",
        ":not() {}",
        "a > {}",
        "> a {}",
        "@media screen and (min-width: 1px) or (x) {}",
        "@media (a) and (b) or (c) {}",
        "@supports (display) {}",
        "@layer a b;",
        "a { @font-face { } }",
        "@font-face { a { } }",
        "@foo bar;",
        "@foo { bar",
        "@foo [ bar",
        "@ media screen {}",
        " @charset \"utf-8\";",
        "@import \"a.css\" {}",
        "@media screen",
        "@media (",
        "@media (width",
        "@supports selector(",
        "@container style(",
        ":nth-child(",
        ":is(:is(:is(",
        "a[",
        "a[href",
        "a[href=",
        "a[href='x' i",
        "a:",
        "a::",
        "a|",
        "|",
        "a ||",
        "a { b: c\\",
        "a\\",
        "\\",
        "\\\n",
        "@",
        "@-",
        "#",
        "--",
        "-->",
        "<!--",
        "<!-- a {} -->",
        "a { <!-- b: c }",
        "a { x: {} y }",
        "a { x: { y }; z: w }",
        "a { --x: { a: b }; --y: [ ( ] ); }",
        "a { b: c !important !important }",
        "a { b: ! important }",
        "a { b: c ! /* x */ IMPORTANT ; }",
        ".a{&:hover}",
        "a { & {} } }",
        "@media (400px < width > 800px) {}",
        "@media (width >= ) {}",
        "@media not (color) {}",
        "@media only screen and (color), not print {}",
        "@media (min-resolution: 2dppx), (-webkit-min-device-pixel-ratio: 2) {}",
        "@container card {}",
        "@container {}",
        "@scope {}",
        "@scope to (.a) {}",
        "@scope (.a) to {}",
        "@keyframes x { 120% {} from, to {} }",
        "@page :middle {}",
        "@page { margin: 0 }",
        "@layer {}",
        "@layer a { @layer b { } }",
        "@import url(x.css) layer supports(not (display: grid)) print;",
        "@namespace url(x);",
        "@property --x { syntax: '*'; }",
        "@font-feature-values A { @styleset { x: 1 2; } }",
        "@unicode-range U+0-7F;",
        "a { unicode-range: u+0-7F, U+4??, U+1????? ; }",
        "a{b:c;;;d:e;}",
        "a\u0000b { c: \u0000 }",
        "a { b: \"\uD800\" }",
        "😀 { 😀: 😀 }",
        "a { width: calc(100% - (2 * var(--gap))) }",
        "a{b:c}/**/d{e:f}",
        "a/**/b {}",
        "a/**/.b {}",
        "a\r\n{\r\n  b: c;\r\n}\r\n",
        "a { b: 1e3 1e+3 1e-3 .5 +.5 -.5 1E3 10e3px 1em 1e 1e+ 12%  }",
        "a { b: U+1234 }",
        "@media (aspect-ratio: 16 / 9) {}",
        "@supports (display: flex) and (not (display: inline-grid)) {}",
        "@supports (--x: { a }) {}",
        "@supports font-format(woff2) {}",
        "@container style(--x) and style((--y: 1) or (--z)) {}",
        "@container scroll-state(stuck: top) {}",
        ":host-context(.dark) ::cue(b) ::picker(select) ::scroll-button(*) :heading(1, 2) {}",
        ":nth-col(odd) :nth-last-of-type(3n) :nth-of-type(+n-1) {}",
        "::view-transition-old(card) ::view-transition-new(.a.b) ::view-transition-image-pair(*) {}",
        ":unknown-function(a b c) ::unknown-element {}",
        "a { @unknown { b: c } }",
        "@unknown { a: b; c { d: e } }",
        "@-webkit-keyframes x { from {} }",
        "a::-webkit-scrollbar, :-moz-any(a) {}",
    ];

    public static TheoryData<string> ValidSamples =>
    [
        "",
        "a{}",
        "a { color: red }",
        ModernStyleSheet,
        "a { b: c; ; ; }",
        "<!-- a {} -->",
        "@media (min-width: 1px) and ((x) or (y)) {}",
        "@media {}",
        "@-webkit-keyframes x {}",
        "a::-webkit-scrollbar {}",
        ":-moz-any(a) {}",
        "a { --x: ; --y: {}; --z: { a } b; }",
        "a { & {} }",
        "a { > b {} + c {} ~ d {} || e {} }",
        "@scope { color: red; a { b: c } }",
        "a { @scope (.x) { b: c } }",
        "@layer a { @layer b { } }",
        "@page { margin: 0; @bottom-center { content: counter(page) } }",
        "@container card {}",
        "@container card (width > 1px), (height > 1px), other {}",
        "a/**/.b {}",
        "a { width: calc(100% - (2 * var(--gap))) }",
        "@media (min-resolution: 2dppx), (-webkit-min-device-pixel-ratio: 2) {}",
        "@import 'a.css'; @import url(b.css) layer; @layer x; @import \"c.css\";",
        "a { unicode-range: u+0-7F, U+4??, U+1????? ; }",
        "a{b:c}/**/d{e:f}",
        "@supports (--x: { a }) {}",
        "@supports font-format(woff2) {}",
        "@container style(--x) and style((--y: 1) or (--z)) {}",
        "@container scroll-state(stuck: top) {}",
        ":host-context(.dark) ::cue(b) ::picker(select) ::scroll-button(*) :heading(1, 2) {}",
        ":nth-col(odd) :nth-last-of-type(3n) :nth-of-type(+n-1) {}",
        "::view-transition-old(card) ::view-transition-new(.a.b) ::view-transition-image-pair(*) {}",
        "div& {}",
        "&.a {}",
        "a { color: red !important }",
        ":is() :where() {}",
    ];

    [Theory]
    [MemberData(nameof(RoundTripSamples))]
    public void ParseText_KeepsEveryCharacter(string text)
    {
        var tree = ParseWithTimeout(text);

        CssSyntaxAssert.TextIsFaithful(text, tree);
        Assert.True(tree.GetDiagnostics().Count <= (text.Length * 2) + 1, $"Expected a bounded number of diagnostics, got {tree.GetDiagnostics().Count}.");
    }

    [Theory]
    [MemberData(nameof(RoundTripSamples))]
    public void ParseText_DeclarationList_KeepsEveryCharacter(string text)
    {
        var options = new CssParseOptions { SourceKind = CssSourceKind.DeclarationList };
        var tree = CssSyntaxTree.ParseText(text, options);

        CssSyntaxAssert.TextIsFaithful(text, tree);
    }

    [Theory]
    [MemberData(nameof(ValidSamples))]
    public void ParseText_Valid_HasNoDiagnostics(string text)
    {
        var tree = CssSyntaxAssert.TextIsFaithful(text);

        Assert.Equal("", CssSyntaxAssert.Diagnostics(tree));
        Assert.False(tree.GetRoot().ContainsSkippedText);
    }

    [Theory]
    [InlineData("/* unterminated", "CSS0001@0:15")]
    [InlineData("a { content: \"abc", "CSS0002@13:4 CSS0010@17:0")]
    [InlineData("a { content: \"abc\ndef\" }", "CSS0002@13:4 CSS0002@21:3 CSS0010@24:0")]
    [InlineData("a { b: url(a b) }", "CSS0003@13:1")]
    [InlineData("a { b: url(a", "CSS0003@7:5 CSS0010@12:0")]
    [InlineData("a { b: \\\n }", "CSS0004@7:1")]
    [InlineData("a { color: }", "CSS0011@4:5")]
    [InlineData("a { color: red !imp }", "CSS0018@15:1")]
    [InlineData("a b; c {}", "CSS0021@3:1")]
    [InlineData(".a { foo bar; x: y }", "CSS0011@5:7")]
    [InlineData(".a{&:hover}", "CSS0011@3:7")]
    [InlineData("--x:hover { }", "CSS0011@0:9")]
    [InlineData("a { x: {} y }", "CSS0010@6:0 CSS0011@10:1")]
    [InlineData("a {", "CSS0010@3:0")]
    [InlineData("a", "CSS0010@1:0")]
    [InlineData("a { b: c } }", "CSS0012@11:1")]
    [InlineData("a::before:hover.x {}", "CSS0023@15:1")]
    [InlineData(".a div& {}", "")]
    [InlineData("&div {}", "CSS0023@1:3")]
    [InlineData("#1a {}", "CSS0026@0:3")]
    [InlineData("a:hoverr {}", "CSS0028@2:6")]
    [InlineData("a::befor {}", "CSS0028@3:5")]
    [InlineData(":is(a, 1b) {}", "CSS0030@7:2")]
    [InlineData(":is(:is(a b!)) {}", "CSS0030@8:4")]
    [InlineData(":not() {}", "CSS0020@5:0")]
    [InlineData("a > {}", "CSS0020@3:0")]
    [InlineData("> a {}", "CSS0027@0:1")]
    [InlineData("a: hover {}", "CSS0022@3:5")]
    [InlineData("a. b {}", "CSS0022@3:1")]
    [InlineData("a[href=] {}", "CSS0024@7:0")]
    [InlineData("a[x y] {}", "CSS0024@4:1")]
    [InlineData(":nth-child(2n+) {}", "CSS0025@11:2")]
    [InlineData(":nth-child(2n+1 of) {}", "CSS0020@18:0")]
    [InlineData(":nth-of-type(2n of a) {}", "CSS0029@16:2")]
    [InlineData(":dir(ltr rtl) {}", "CSS0029@9:3")]
    [InlineData("@media screen and (min-width: 1px) or (x) {}", "CSS0041@7:34")]
    [InlineData("@media (a) and (b) or (c) {}", "CSS0041@7:18")]
    [InlineData("@media foo bar {}", "CSS0041@7:7")]
    [InlineData("@media screen, {}", "CSS0041@14:0")]
    [InlineData("@media (width >= ) {}", "CSS0045@7:11")]
    [InlineData("@media (400px < width > 800px) {}", "CSS0045@7:23")]
    [InlineData("@supports (display) {}", "CSS0045@10:9")]
    [InlineData("@supports display: grid {}", "CSS0044@10:7")]
    [InlineData("@layer a b;", "CSS0046@9:1")]
    [InlineData("@layer a, b {}", "CSS0040@8:1")]
    [InlineData("@layer;", "CSS0040@6:0")]
    [InlineData("@import \"a.css\"; @namespace svg url(x); a{} @import \"b.css\";", "CSS0017@44:7")]
    [InlineData("a{} @namespace svg url(x);", "CSS0017@4:10")]
    [InlineData("a { @font-face { } }", "CSS0014@4:10")]
    [InlineData("@font-face { a { } }", "CSS0014@13:1")]
    [InlineData("@media screen { @import 'a'; }", "CSS0014@16:7")]
    [InlineData("@keyframes x { a: b }", "CSS0013@15:4")]
    [InlineData("@keyframes none {}", "CSS0050@11:4")]
    [InlineData("@keyframes x { 120% {} }", "CSS0047@15:4")]
    [InlineData("@property color {}", "CSS0050@10:5")]
    [InlineData("@counter-style decimal {}", "CSS0050@15:7")]
    [InlineData("@container none {}", "CSS0050@11:4")]
    [InlineData("@container {}", "CSS0040@10:0")]
    [InlineData("@page :middle {}", "CSS0048@7:6")]
    [InlineData("@scope (.a) to {}", "CSS0010@14:0")]
    [InlineData("@starting-style foo {}", "CSS0040@16:3")]
    [InlineData("@foo bar;", "CSS0019@0:4")]
    [InlineData("@import \"a.css\" {}", "CSS0015@0:7")]
    [InlineData("@media screen", "CSS0015@13:0")]
    [InlineData("ns|a {}", "CSS0052@0:2")]
    [InlineData("[ns|a] {}", "CSS0052@1:2")]
    [InlineData("@namespace NS url(x); ns|a {}", "CSS0052@22:2")]
    [InlineData("a {} @namespace ns url(x); ns|a {}", "CSS0017@5:10 CSS0052@27:2")]
    [InlineData(":is(ns|a, b) {}", "CSS0030@4:4")]
    [InlineData("@namespace ns url(x); ns|a, *|b, |c, [ns|d] {} @media all { ns|e {} }", "")]
    [InlineData(" @charset \"utf-8\";", "CSS0051@1:8")]
    [InlineData("@charset 'utf-8';", "CSS0051@0:8")]
    [InlineData("a { @charset \"utf-8\"; }", "CSS0014@4:8")]
    public void ParseText_ReportsDiagnostics(string text, string expected)
    {
        var tree = CssSyntaxAssert.TextIsFaithful(text);

        Assert.Equal(expected, CssSyntaxAssert.Diagnostics(tree));
    }

    [Fact]
    public void ParseText_ErrorsAreErrorsAndWarningsAreWarnings()
    {
        var tree = CssSyntaxTree.ParseText("a:hoverr {} @foo; @media (foo bar) {} a { b: }");

        Assert.Equal([DiagnosticSeverity.Warning, DiagnosticSeverity.Warning, DiagnosticSeverity.Warning, DiagnosticSeverity.Error], tree.GetDiagnostics().Select(diagnostic => diagnostic.Severity).ToArray());
    }

    [Fact]
    public void ParseText_SelectorStructure()
    {
        Assert.Equal(
            "SelectorList[ComplexSelector[ComplexSelectorPart[CompoundSelector[TypeSelector[a] ClassSelector[. b]]] ComplexSelectorPart[ChildCombinator[>] CompoundSelector[TypeSelector[c] PseudoClassSelector[: hover]]]] , ComplexSelector[ComplexSelectorPart[CompoundSelector[TypeSelector[d]]]]]",
            DumpPrelude("a.b > c:hover, d {}"));
        Assert.Equal(
            "SelectorList[ComplexSelector[ComplexSelectorPart[CompoundSelector[TypeSelector[NamespacePrefix[ns |] a]]] ComplexSelectorPart[CompoundSelector[UniversalSelector[NamespacePrefix[* |] *]]] ComplexSelectorPart[CompoundSelector[TypeSelector[NamespacePrefix[|] b]]] ComplexSelectorPart[CompoundSelector[AttributeSelector[[ NamespacePrefix[svg |] href ]]]]]]",
            DumpPrelude("@namespace ns url(x); @namespace svg url(y); ns|a *|* |b [svg|href] {}", statementIndex: 2));
        Assert.Equal(
            "SelectorList[ComplexSelector[ComplexSelectorPart[CompoundSelector[TypeSelector[a]]] ComplexSelectorPart[NextSiblingCombinator[+] CompoundSelector[TypeSelector[b]]] ComplexSelectorPart[SubsequentSiblingCombinator[~] CompoundSelector[TypeSelector[c]]] ComplexSelectorPart[ColumnCombinator[| |] CompoundSelector[TypeSelector[d]]] ComplexSelectorPart[CompoundSelector[TypeSelector[e]]]]]",
            DumpPrelude("a + b ~ c || d e {}"));
        Assert.Equal(
            "SelectorList[ComplexSelector[ComplexSelectorPart[CompoundSelector[FunctionalPseudoClassSelector[: is( SelectorList[ComplexSelector[ComplexSelectorPart[CompoundSelector[TypeSelector[a]]]] , ComplexSelector[ComplexSelectorPart[CompoundSelector[ClassSelector[. b]]]]] )]]] ComplexSelectorPart[CompoundSelector[FunctionalPseudoClassSelector[: where( )]]] ComplexSelectorPart[CompoundSelector[FunctionalPseudoClassSelector[: has( SelectorList[ComplexSelector[ComplexSelectorPart[ChildCombinator[>] CompoundSelector[TypeSelector[img]]]] , ComplexSelector[ComplexSelectorPart[NextSiblingCombinator[+] CompoundSelector[TypeSelector[p]]]]] )]]]]]",
            DumpPrelude(":is(a, .b) :where() :has(> img, + p) {}"));
        Assert.Equal(
            "SelectorList[ComplexSelector[ComplexSelectorPart[CompoundSelector[TypeSelector[li] FunctionalPseudoClassSelector[: nth-last-child( NthArgument[AnPlusB[-n +3] of SelectorList[ComplexSelector[ComplexSelectorPart[CompoundSelector[ClassSelector[. x]]]]]] )]]]]]",
            DumpPrelude("li:nth-last-child(-n+3 of .x) {}"));
        Assert.Equal(
            "SelectorList[ComplexSelector[ComplexSelectorPart[CompoundSelector[FunctionalPseudoElementSelector[: : part( NameList[label active] )] FunctionalPseudoElementSelector[: : slotted( CompoundSelector[TypeSelector[span] ClassSelector[. x]] )] FunctionalPseudoClassSelector[: host( CompoundSelector[ClassSelector[. dark]] )]]]]]",
            DumpPrelude("::part(label active)::slotted(span.x):host(.dark) {}"));
        Assert.Equal(
            "SelectorList[ComplexSelector[ComplexSelectorPart[CompoundSelector[FunctionalPseudoElementSelector[: : view-transition-group( ViewTransitionPartSelector[* ClassSelector[. card]] )]]] ComplexSelectorPart[CompoundSelector[FunctionalPseudoClassSelector[: lang( NameList[en , \"fr\"] )]]] ComplexSelectorPart[CompoundSelector[FunctionalPseudoClassSelector[: dir( NameList[rtl] )]]] ComplexSelectorPart[CompoundSelector[FunctionalPseudoClassSelector[: state( NameList[checked] )]]]]]",
            DumpPrelude("::view-transition-group(*.card) :lang(en, \"fr\") :dir(rtl) :state(checked) {}"));
    }

    [Fact]
    public void ParseText_AtRulePreludeStructure()
    {
        Assert.Equal(
            "MediaQueryList[MediaTypeQuery[not print and BooleanFeature[( color )]] , MediaConditionQuery[AndCondition[PlainFeature[( orientation : FeatureValue[TokenValue[landscape]] )] and RangeFeature[( FeatureValue[TokenValue[16] TokenValue[/] TokenValue[9]] LessThanOrEqualComparison[< =] FeatureValue[TokenValue[aspect-ratio]] )]]]]",
            DumpPrelude("@media not print and (color), (orientation: landscape) and (16/9 <= aspect-ratio) {}"));
        Assert.Equal(
            "ContainerPrelude[ContainerCondition[sidebar AndCondition[StyleQuery[style( DeclarationCondition[Declaration[--theme : TokenValue[dark]]] )] and ScrollStateQuery[scroll-state( PlainFeature[stuck : FeatureValue[TokenValue[top]]] )]]] , ContainerCondition[RangeFeature[( FeatureValue[TokenValue[inline-size]] GreaterThanComparison[>] FeatureValue[TokenValue[30em]] )]]]",
            DumpPrelude("@container sidebar style(--theme: dark) and scroll-state(stuck: top), (inline-size > 30em) {}"));
        Assert.Equal(
            "ConditionPrelude[OrCondition[SelectorFunction[selector( ComplexSelector[ComplexSelectorPart[CompoundSelector[FunctionalPseudoClassSelector[: has( SelectorList[ComplexSelector[ComplexSelectorPart[CompoundSelector[TypeSelector[a]]]]] )]]]] )] or FontTechFunction[font-tech( NameList[color-COLRv1] )] or AtRuleFunction[at-rule( NameList[@starting-style] )]]]",
            DumpPrelude("@supports selector(:has(a)) or font-tech(color-COLRv1) or at-rule(@starting-style) {}"));
        Assert.Equal("PageSelectorList[PageSelector[wide PseudoPage[: left]] , PageSelector[PseudoPage[: first]]]", DumpPrelude("@page wide:left, :first {}"));
        Assert.Equal("NameList[Font One , \"Two\"]", DumpPrelude("@font-feature-values Font One, \"Two\" { @swash { fancy: 1 } }"));
        Assert.Equal("CustomMediaPrelude[--narrow MediaQueryList[MediaConditionQuery[RangeFeature[( FeatureValue[TokenValue[width]] LessThanComparison[<] FeatureValue[TokenValue[30em]] )]]]]", DumpPrelude("@custom-media --narrow (width < 30em);"));
        Assert.Equal("NamespacePrelude[svg TokenValue[url(http://www.w3.org/2000/svg)]]", DumpPrelude("@namespace svg url(http://www.w3.org/2000/svg);"));
        Assert.Equal("ImportPrelude[TokenValue[url(x.css)] ImportLayer[layer]]", DumpPrelude("@import url(x.css) layer;"));
        Assert.Equal("ScopePrelude[ScopeBoundary[( SelectorList[ComplexSelector[ComplexSelectorPart[CompoundSelector[ClassSelector[. a]]]]] )] to ScopeBoundary[( SelectorList[ComplexSelector[ComplexSelectorPart[ChildCombinator[>] CompoundSelector[ClassSelector[. b]]]]] )]]", DumpPrelude("@scope (.a) to (> .b) {}"));
        Assert.Equal("LayerPrelude[LayerName[a] , LayerName[b . c]]", DumpPrelude("@layer a, b.c;"));
        Assert.Equal("null", DumpPrelude("@layer {}"));
        Assert.Equal("GenericPrelude[TokenValue[bar] ParenthesizedBlock[( TokenValue[baz] )]]", DumpPrelude("@foo bar (baz);"));
    }

    [Fact]
    public void ParseText_RuleKinds()
    {
        var root = CssSyntaxTree.ParseText("""
            @charset "utf-8"; @import "a"; @namespace x "b"; @media x {} @supports (a: b) {} @container x {} @layer x; @scope {}
            @starting-style {} @keyframes x { from {} } @font-face {} @page { @top-left {} } @property --x {} @counter-style x {}
            @font-feature-values x { @swash {} } @font-palette-values --x {} @position-try --x {} @view-transition {}
            @custom-media --x x; @function --x() {} @top-left {} @swash {} a {}
            """).GetRoot();

        Assert.Equal(
            [
                SyntaxKind.CharsetRule, SyntaxKind.ImportRule, SyntaxKind.NamespaceRule, SyntaxKind.MediaRule, SyntaxKind.SupportsRule, SyntaxKind.ContainerRule, SyntaxKind.LayerRule, SyntaxKind.ScopeRule,
                SyntaxKind.StartingStyleRule, SyntaxKind.KeyframesRule, SyntaxKind.FontFaceRule, SyntaxKind.PageRule, SyntaxKind.PropertyRule, SyntaxKind.CounterStyleRule,
                SyntaxKind.FontFeatureValuesRule, SyntaxKind.FontPaletteValuesRule, SyntaxKind.PositionTryRule, SyntaxKind.ViewTransitionRule,
                SyntaxKind.CustomMediaRule, SyntaxKind.FunctionRule, SyntaxKind.UnknownAtRule, SyntaxKind.UnknownAtRule, SyntaxKind.StyleRule,
            ],
            root.Rules.Select(rule => rule.Kind()).ToArray());

        var keyframes = root.Rules.OfType<CssAtRuleSyntax>().Single(rule => rule.Kind() == SyntaxKind.KeyframesRule);
        Assert.Equal(SyntaxKind.KeyframeRule, Assert.Single(keyframes.Block!.Rules).Kind());

        var page = root.Rules.OfType<CssAtRuleSyntax>().Single(rule => rule.Kind() == SyntaxKind.PageRule);
        Assert.Equal(SyntaxKind.PageMarginRule, Assert.Single(page.Block!.Rules).Kind());

        var featureValues = root.Rules.OfType<CssAtRuleSyntax>().Single(rule => rule.Kind() == SyntaxKind.FontFeatureValuesRule);
        Assert.Equal(SyntaxKind.FontFeatureValueBlockRule, Assert.Single(featureValues.Block!.Rules).Kind());
    }

    [Theory]
    [InlineData("a:hover { }", SyntaxKind.StyleRule)]
    [InlineData("a:hover b { }", SyntaxKind.StyleRule)]
    [InlineData("font: bold;", SyntaxKind.Declaration)]
    [InlineData("font: { a };", SyntaxKind.Declaration)]
    [InlineData("font: { a } !important;", SyntaxKind.Declaration)]
    [InlineData("font: { a } b;", SyntaxKind.StyleRule)]
    [InlineData("--x: { a } b;", SyntaxKind.Declaration)]
    [InlineData("--x:hover { }", SyntaxKind.Declaration)]
    [InlineData("foo bar;", SyntaxKind.BadDeclaration)]
    [InlineData("& > a { }", SyntaxKind.StyleRule)]
    [InlineData("div { }", SyntaxKind.StyleRule)]
    [InlineData("@media x { }", SyntaxKind.MediaRule)]
    [InlineData(";", SyntaxKind.IgnoredToken)]
    public void ParseText_NestedBlockItem_IsDeclarationOrRule(string item, SyntaxKind expected)
    {
        var rule = Assert.IsType<CssQualifiedRuleSyntax>(CssSyntaxAssert.TextIsFaithful(".parent { " + item + " }").GetRoot().Statements[0]);

        Assert.Equal(expected, rule.Block.Statements[0].Kind());
    }

    [Fact]
    public void ParseText_TopLevelSemicolon_IsPartOfTheNextRule()
    {
        var root = CssSyntaxAssert.TextIsFaithful("a; b {} c {}").GetRoot();

        Assert.Equal(2, root.Statements.Count);
        var dropped = Assert.IsType<CssQualifiedRuleSyntax>(root.Statements[0]);
        Assert.IsType<CssGenericPreludeSyntax>(dropped.Prelude);
        Assert.True(dropped.ContainsSkippedText);
        Assert.IsType<CssSelectorListSyntax>(((CssQualifiedRuleSyntax)root.Statements[1]).Prelude);
    }

    [Fact]
    public void ParseText_InvalidSelector_KeepsThePreludeAsComponentValues()
    {
        var tree = CssSyntaxAssert.TextIsFaithful("a!b { color: red } c { }");
        var rules = tree.GetRoot().Rules.Cast<CssQualifiedRuleSyntax>().ToArray();

        Assert.IsType<CssGenericPreludeSyntax>(rules[0].Prelude);
        Assert.Null(rules[0].Selectors);
        Assert.Equal("color", Assert.Single(rules[0].Block.Declarations).Name);
        Assert.NotNull(rules[1].Selectors);
        Assert.Equal("CSS0021@1:1", CssSyntaxAssert.Diagnostics(tree));
    }

    [Fact]
    public void ParseText_InvalidMediaQuery_LeavesTheOtherQueriesAlone()
    {
        var tree = CssSyntaxAssert.TextIsFaithful("@media screen, foo bar, print {}");
        var queries = Assert.IsType<CssMediaQueryListSyntax>(Assert.IsType<CssAtRuleSyntax>(tree.GetRoot().Statements[0]).Prelude).Queries;

        Assert.Equal([SyntaxKind.MediaTypeQuery, SyntaxKind.InvalidMediaQuery, SyntaxKind.MediaTypeQuery], queries.Select(query => query.Kind()).ToArray());
    }

    [Fact]
    public void ParseText_ForgivingSelectorList_KeepsTheValidSelectors()
    {
        var tree = CssSyntaxAssert.TextIsFaithful(":is(a, !, .b) {}");
        var pseudo = tree.GetRoot().DescendantNodes().OfType<CssFunctionalPseudoSelectorSyntax>().Single();

        Assert.Equal([SyntaxKind.ComplexSelector, SyntaxKind.InvalidSelector, SyntaxKind.ComplexSelector], pseudo.Selectors!.Selectors.Select(selector => selector.Kind()).ToArray());
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(tree.GetDiagnostics()).Severity);
    }

    [Fact]
    public void ParseText_Declaration()
    {
        var tree = CssSyntaxAssert.TextIsFaithful("a { --Main-Color : rgb( 0 0 0 ) /* x */ ! IMPORTANT ; c\\6f lor: red }");
        var declarations = tree.GetRoot().DescendantNodes().OfType<CssDeclarationSyntax>().ToArray();

        Assert.Equal("--Main-Color", declarations[0].Name);
        Assert.True(declarations[0].IsCustomProperty);
        Assert.True(declarations[0].IsImportant);
        Assert.Equal("rgb( 0 0 0 )", declarations[0].GetValueText());
        Assert.Equal("rgb", Assert.IsType<CssFunctionSyntax>(Assert.Single(declarations[0].Values)).Name);
        Assert.Equal("color", declarations[1].Name);
        Assert.Equal("c\\6f lor", declarations[1].NameToken.Text);
        Assert.False(declarations[1].IsImportant);
        Assert.False(declarations[1].SemicolonToken.IsPresent());
    }

    [Fact]
    public void ParseText_CommentsAreTrivia()
    {
        var tree = CssSyntaxAssert.TextIsFaithful("/* a */\na { /* b */ color: red; /* c */\n}");
        var comments = tree.GetRoot().DescendantComments().Select(comment => comment.ToFullString()).ToArray();

        Assert.Equal(["/* a */", "/* b */", "/* c */"], comments);
    }

    [Fact]
    public void ParseText_TrailingTriviaEndsAtTheLineBreak()
    {
        var tree = CssSyntaxAssert.TextIsFaithful("a { color: red; /* same line */\n  /* next line */ b: c }");
        var declarations = tree.GetRoot().DescendantNodes().OfType<CssDeclarationSyntax>().ToArray();

        Assert.Equal(" /* same line */\n", declarations[0].SemicolonToken.TrailingTrivia.ToFullString());
        Assert.Equal("  /* next line */ ", declarations[1].NameToken.LeadingTrivia.ToFullString());
    }

    [Fact]
    public void ParseText_Tokens()
    {
        var tree = CssSyntaxAssert.TextIsFaithful("a { b: 10px 1.5e2% -3 #fff #1a \"\\41 b\" url( x\\)y ) \\@x @y }");
        var tokens = tree.GetRoot().DescendantTokens().ToArray();

        var dimension = tokens.Single(token => token.IsKind(SyntaxKind.DimensionToken));
        Assert.Equal(10, dimension.GetNumericValue());
        Assert.Equal("px", dimension.GetUnit());
        Assert.True(dimension.IsInteger());

        var percentage = tokens.Single(token => token.IsKind(SyntaxKind.PercentageToken));
        Assert.Equal(150, percentage.GetNumericValue());
        Assert.False(percentage.IsInteger());

        Assert.Equal(-3, tokens.Single(token => token.IsKind(SyntaxKind.NumberToken)).GetNumericValue());

        var hashes = tokens.Where(token => token.IsKind(SyntaxKind.HashToken)).ToArray();
        Assert.True(hashes[0].IsIdHash());
        Assert.Equal("fff", hashes[0].ValueText);
        Assert.False(hashes[1].IsIdHash());

        Assert.Equal("Ab", tokens.Single(token => token.IsKind(SyntaxKind.StringToken)).ValueText);
        Assert.Equal("x)y", tokens.Single(token => token.IsKind(SyntaxKind.UrlToken)).ValueText);
        Assert.Equal("@x", tokens.Single(token => token.IsKind(SyntaxKind.IdentToken) && token.Text == "\\@x").ValueText);
        Assert.Equal("y", tokens.Single(token => token.IsKind(SyntaxKind.AtKeywordToken)).ValueText);

        // Outside the value of unicode-range, U+1F?? is an identifier, a dimension, and question marks.
        Assert.Equal(
            [SyntaxKind.IdentToken, SyntaxKind.DimensionToken, SyntaxKind.DelimToken, SyntaxKind.DelimToken],
            CssSyntaxTree.ParseText("a { b: U+1F?? }").GetRoot().DescendantTokens().Skip(4).Take(4).Select(token => token.Kind()).ToArray());
    }

    [Fact]
    public void ParseText_UnicodeRange()
    {
        var tree = CssSyntaxAssert.TextIsFaithful("@font-face { unicode-range: U+0-7F, u+4??, U+1F600; }");
        var ranges = tree.GetRoot().DescendantTokens().Where(token => token.IsKind(SyntaxKind.UnicodeRangeToken)).Select(token => token.GetUnicodeRange()).ToArray();

        Assert.Equal([(0x0, 0x7F), (0x400, 0x4FF), (0x1F600, 0x1F600)], ranges.Select(range => range!.Value).ToArray());
        Assert.Equal("", CssSyntaxAssert.Diagnostics(tree));
    }

    [Theory]
    [InlineData("odd", 2, 1)]
    [InlineData("EVEN", 2, 0)]
    [InlineData("3", 0, 3)]
    [InlineData("-n+3", -1, 3)]
    [InlineData("2n + 1", 2, 1)]
    [InlineData("2n- 1", 2, -1)]
    [InlineData("+n-1", 1, -1)]
    [InlineData("-2n -1", -2, -1)]
    public void AnPlusB_ComputesAAndB(string expression, int a, int b)
    {
        var tree = CssSyntaxAssert.TextIsFaithful(":nth-child(" + expression + ") {}");
        var anPlusB = tree.GetRoot().DescendantNodes().OfType<CssAnPlusBSyntax>().Single();

        Assert.True(anPlusB.IsValid);
        Assert.Equal(a, anPlusB.A);
        Assert.Equal(b, anPlusB.B);
    }

    [Fact]
    public void Selectors_ComputedMembers()
    {
        var tree = CssSyntaxAssert.TextIsFaithful("a.b > c + d ~ e || f g::before:hover, [x~='y' I], [x], [x|=y s] {}");
        var root = tree.GetRoot();
        var parts = root.DescendantNodes().OfType<CssComplexSelectorPartSyntax>().Select(part => part.CombinatorKind).ToArray();

        Assert.Equal(
            [CssCombinatorKind.None, CssCombinatorKind.Child, CssCombinatorKind.NextSibling, CssCombinatorKind.SubsequentSibling, CssCombinatorKind.Column, CssCombinatorKind.Descendant, CssCombinatorKind.None, CssCombinatorKind.None, CssCombinatorKind.None],
            parts);

        var attributes = root.DescendantNodes().OfType<CssAttributeSelectorSyntax>().ToArray();
        Assert.Equal([CssAttributeOperator.Includes, CssAttributeOperator.None, CssAttributeOperator.DashMatch], attributes.Select(attribute => attribute.Operator).ToArray());
        Assert.Equal(["y", null, "y"], attributes.Select(attribute => attribute.Value).ToArray());
        Assert.Equal([true, null, false], attributes.Select(attribute => attribute.IsCaseInsensitive).ToArray());

        var pseudo = root.DescendantNodes().OfType<CssPseudoSelectorSyntax>().ToArray();
        Assert.Equal(["before", "hover"], pseudo.Select(selector => selector.Name).ToArray());
        Assert.Equal([true, false], pseudo.Select(selector => selector.IsPseudoElement).ToArray());

        var list = Assert.IsType<CssSelectorListSyntax>(((CssQualifiedRuleSyntax)root.Statements[0]).Prelude);
        Assert.False(list.IsRelative);
        Assert.Equal(["b"], root.DescendantNodes().OfType<CssClassSelectorSyntax>().Select(selector => selector.Name).ToArray());
    }

    [Fact]
    public void Selectors_RelativeInNestedRule()
    {
        var root = CssSyntaxAssert.TextIsFaithful("a { > b {} }").GetRoot();
        var nested = root.DescendantNodes().OfType<CssQualifiedRuleSyntax>().Last();

        Assert.True(nested.Selectors!.IsRelative);
    }

    [Fact]
    public void Selectors_LegacyPseudoElementsWithOneColon()
    {
        var root = CssSyntaxAssert.TextIsFaithful("a:before, a:first-line, a:hover {}").GetRoot();

        Assert.Equal([true, true, false], root.DescendantNodes().OfType<CssPseudoSelectorSyntax>().Select(selector => selector.IsPseudoElement).ToArray());
    }

    [Fact]
    public void AtRules_ComputedMembers()
    {
        var root = CssSyntaxAssert.TextIsFaithful("@layer a.b.c; @keyframes x { from, 50%, to {} } @MEDIA print {} @container (width > 1px) { }").GetRoot();
        var rules = root.Rules.Cast<CssAtRuleSyntax>().ToArray();

        Assert.Equal(["a", "b", "c"], Assert.Single(Assert.IsType<CssLayerPreludeSyntax>(rules[0].Prelude).Names).Parts);
        Assert.Equal("a.b.c", Assert.IsType<CssLayerPreludeSyntax>(rules[0].Prelude).Names[0].Name);
        Assert.Equal("x", Assert.IsType<CssNamePreludeSyntax>(rules[1].Prelude).Name);
        Assert.Equal([0, 50, 100], root.DescendantNodes().OfType<CssKeyframeSelectorSyntax>().Select(selector => selector.Offset).ToArray());
        Assert.Equal("MEDIA", rules[2].Name);
        Assert.Equal(SyntaxKind.MediaRule, rules[2].Kind());
        Assert.Equal("width", root.DescendantNodes().OfType<CssRangeFeatureSyntax>().Single().Name);
    }

    [Fact]
    public void ParseText_DeclarationList()
    {
        var tree = CssSyntaxAssert.TextIsFaithful("color: red; margin: 0 auto !important", new CssParseOptions { SourceKind = CssSourceKind.DeclarationList });

        Assert.Equal(["color", "margin"], tree.GetRoot().Declarations.Select(declaration => declaration.Name).ToArray());
        Assert.Equal("", CssSyntaxAssert.Diagnostics(tree));
        Assert.Equal(CssSourceKind.DeclarationList, tree.Options.SourceKind);
    }

    [Fact]
    public void ParseText_DeclarationList_ReportsRulesAndStrayBraces()
    {
        var tree = CssSyntaxAssert.TextIsFaithful("a: b; } c { }", new CssParseOptions { SourceKind = CssSourceKind.DeclarationList });

        Assert.Equal("CSS0012@6:1 CSS0014@8:1", CssSyntaxAssert.Diagnostics(tree));
    }

    [Fact]
    public void ParseText_DeepNesting_ReportsInsteadOfOverflowingTheStack()
    {
        var text = new string('{', 100_000) + new string('}', 100_000) + string.Concat(Enumerable.Repeat("a{", 100_000)) + string.Concat(Enumerable.Repeat(":is(", 50_000)) + "@media " + new string('(', 50_000);
        CssSyntaxTree? tree = null;

        var thread = new Thread(() => tree = CssSyntaxTree.ParseText(text, new CssParseOptions { MaxDepth = int.MaxValue }), maxStackSize: 1024 * 1024);
        thread.Start();
        thread.Join();

        Assert.NotNull(tree);
        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Contains(tree.GetDiagnostics(), diagnostic => diagnostic.Id == "CSS0016");
    }

    [Theory]
    [InlineData("a{b{c{d{e{}}}}}")]
    [InlineData("a{b:f(g(h(i(j()))))}")]
    [InlineData(":is(:is(:is(:is(:is(a))))){}")]
    [InlineData("@media ((((((a)))))){}")]
    public void ParseText_MaxDepth_KeepsTheRestAsSkippedText(string text)
    {
        var tree = CssSyntaxAssert.TextIsFaithful(text, new CssParseOptions { MaxDepth = 3 });

        Assert.Contains(tree.GetDiagnostics(), diagnostic => diagnostic.Id == "CSS0016");
    }

    [Fact]
    public void ParseOptions_Validate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CssParseOptions { MaxDepth = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CssParseOptions { SourceKind = (CssSourceKind)42 });
        Assert.Equal(CssParseOptions.DefaultMaxDepth, CssParseOptions.Default.MaxDepth);
    }

    [Fact]
    public void ParseText_EveryPrefixAndEveryDeletion_StillRoundTrips()
    {
        const string Text = """
            @import url("a.css") layer(x) supports(display: grid) screen;
            @media (400px <= width < 800px), print { .a:is(.b, #c) > d::before { e: f(g) !important; } }
            .x { &:hover { y: z } @container s style(--v: 1) { w: "q\"" } }
            @keyframes k { from { o: 0 } 50% {} } :nth-child(2n+1 of [a|=b i]) {}
            """;

        for (var length = 0; length <= Text.Length; length++)
        {
            Check(Text[..length]);
        }

        for (var index = 0; index < Text.Length; index++)
        {
            Check(Text.Remove(index, 1));
        }

        static void Check(string candidate)
        {
            var tree = ParseWithTimeout(candidate);
            CssSyntaxAssert.TextIsFaithful(candidate, tree);
        }
    }

    [Fact]
    public void ParseText_RandomFragments_StillRoundTrip()
    {
        string[] fragments =
        [
            "{", "}", "(", ")", "[", "]", ";", ":", "::", ",", ">", "+", "~", "||", "|", "&", "*", ".", "#", "#a", "@media", "@supports",
            "@container", "@layer", "@scope", "@import", "@keyframes", "@page", "@font-face", "@foo", "and", "or", "not", "only", "of", "to",
            "url(", "url(a)", "\"", "'", "\"s\"", "/*", "*/", "<!--", "-->", "\\", "\n", " ", "!important", "!", "--x", "2n+1", "odd", "a",
            "div", ".b", ":hover", "::before", ":is(", ":not(", ":has(", ":nth-child(", "selector(", "style(", "(width", ">=", "<", "=", "600px",
            "16/9", "50%", "from", "U+0-7F", "unicode-range:", "calc(", "var(--x)", "😀", "\u0000",
        ];

        var random = new DeterministicRandom(42);
        var builder = new StringBuilder();
        for (var iteration = 0; iteration < 2000; iteration++)
        {
            builder.Clear();
            var count = random.Next(30);
            for (var i = 0; i < count; i++)
            {
                builder.Append(fragments[random.Next(fragments.Length)]);
                if (random.Next(3) == 0)
                {
                    builder.Append(' ');
                }
            }

            var text = builder.ToString();
            var tree = ParseWithTimeout(text);
            CssSyntaxAssert.TextIsFaithful(text, tree);
            CssSyntaxAssert.TextIsFaithful(text, CssSyntaxTree.ParseText(text, new CssParseOptions { SourceKind = CssSourceKind.DeclarationList }));
        }
    }

    [Fact]
    public void ReplaceNode_KeepsTheRestOfTheText()
    {
        var tree = CssSyntaxTree.ParseText("a {\n  color: red; /* keep */\n  margin: 0;\n}\n");
        var root = tree.GetRoot();
        var color = root.DescendantNodes().OfType<CssDeclarationSyntax>().First();
        var value = Assert.IsType<CssTokenValueSyntax>(Assert.Single(color.Values));

        var newRoot = root.ReplaceNode(value, value.WithToken(SyntaxFactory.Identifier("blue").WithTriviaFrom(value.Token)));

        Assert.Equal("a {\n  color: blue; /* keep */\n  margin: 0;\n}\n", newRoot.ToFullString());
        Assert.Equal("", CssSyntaxAssert.Diagnostics(tree.WithRoot(newRoot)));
    }

    [Fact]
    public void RemoveNode_RemovesTheDeclaration()
    {
        var root = CssSyntaxTree.ParseText("a {\n  color: red;\n  margin: 0;\n}\n").GetRoot();
        var color = root.DescendantNodes().OfType<CssDeclarationSyntax>().First();

        var newRoot = root.RemoveNode(color, SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("a {\n  margin: 0;\n}\n", newRoot.ToFullString());
    }

    [Fact]
    public void InsertNodesAfter_AddsADeclaration()
    {
        var root = CssSyntaxTree.ParseText("a { color: red; }").GetRoot();
        var color = root.DescendantNodes().OfType<CssDeclarationSyntax>().First();

        var newRoot = root.InsertNodesAfter(color, [SyntaxFactory.Declaration("margin", SyntaxFactory.CssTokenValue(SyntaxFactory.Number(0)))]);

        // The space after "red;" is the trailing trivia of its semicolon, so it stays where it is.
        Assert.Equal("a { color: red; margin: 0;}", newRoot.ToFullString());
        Assert.Equal("", CssSyntaxAssert.Diagnostics(CssSyntaxTree.Create(newRoot)));
    }

    [Theory]
    [InlineData("@media (a) and (b) {}", "@media (a) and (b) and (c) {}")]
    [InlineData("@media (a) or (b) {}", "@media (a) or (b) or (c) {}")]
    public void InsertNodesAfter_SeparatesConditionsWithTheKeywordOfTheChain(string text, string expected)
    {
        var root = CssSyntaxTree.ParseText(text).GetRoot();
        var chain = root.DescendantNodes().OfType<CssConditionChainSyntax>().Single();
        var condition = CssSyntaxTree.ParseText("@media (c) {}").GetRoot().DescendantNodes().OfType<CssBooleanFeatureSyntax>().Single();

        var newRoot = root.InsertNodesAfter(chain.Operands[1], [condition]);

        Assert.Equal(expected, newRoot.ToFullString());
        Assert.Equal("", CssSyntaxAssert.Diagnostics(CssSyntaxTree.Create(newRoot)));
    }

    [Fact]
    public void SyntaxFactory_TokensReadBackAsThemselves()
    {
        var tokens = new[]
        {
            SyntaxFactory.Identifier("a b"),
            SyntaxFactory.Identifier("1st"),
            SyntaxFactory.Identifier("-"),
            SyntaxFactory.Identifier("--x"),
            SyntaxFactory.StringToken("it's \"quoted\"\n", '"'),
            SyntaxFactory.StringToken("it's", '\''),
            SyntaxFactory.Number(-1.5),
            SyntaxFactory.Number(1e21),
            SyntaxFactory.Percentage(50),
            SyntaxFactory.Dimension(10, "px"),
            SyntaxFactory.Dimension(1, "e3"),
            SyntaxFactory.Hash("1a"),
            SyntaxFactory.AtKeyword("media"),
            SyntaxFactory.FunctionToken("rgb"),
        };

        foreach (var token in tokens)
        {
            var reparsed = CssSyntaxTree.ParseText("a { b: " + token.Text + (token.IsKind(SyntaxKind.FunctionToken) ? ")" : "") + " }").GetRoot().DescendantTokens().ElementAt(4);

            Assert.Equal(token.Kind(), reparsed.Kind());
            Assert.Equal(token.ValueText, reparsed.ValueText);
            Assert.Equal(token.Value, reparsed.Value);
        }

        Assert.Equal("e3", SyntaxFactory.Dimension(1, "e3").GetUnit());
        Assert.Throws<ArgumentOutOfRangeException>(() => SyntaxFactory.Number(double.NaN));
        Assert.Throws<ArgumentException>(() => SyntaxFactory.Comment("a */ b"));
        Assert.Equal("/* note */", SyntaxFactory.Comment("note").ToFullString());
    }

    [Fact]
    public void Walker_VisitsEveryNode()
    {
        var root = CssSyntaxTree.ParseText(ModernStyleSheet).GetRoot();
        var walker = new CountingWalker();

        walker.Visit(root);

        Assert.Equal(root.DescendantNodes().OfType<CssDeclarationSyntax>().Count(), walker.Declarations);
        Assert.Equal(root.DescendantTrivia().Count(trivia => trivia.IsComment()), walker.Comments);
    }

    [Fact]
    public void Rewriter_RenamesClasses()
    {
        var root = CssSyntaxTree.ParseText(".old, .other > .old { } :is(.old) { }").GetRoot();

        var rewritten = (CssStyleSheetSyntax)new RenameClassRewriter().Visit(root)!;

        Assert.Equal(".new, .other > .new { } :is(.new) { }", rewritten.ToFullString());
    }

    [Fact]
    public void Rewriter_ChangingNothing_ReturnsTheSameNodes()
    {
        var root = CssSyntaxTree.ParseText(ModernStyleSheet).GetRoot();

        Assert.Same(root, new CssSyntaxRewriter().Visit(root));
    }

    [Fact]
    public void Rewriter_ThrowsRatherThanOverflowingTheStack()
    {
        var text = string.Concat(Enumerable.Repeat("a{", 20_000));
        var root = CssSyntaxTree.ParseText(text, new CssParseOptions { MaxDepth = int.MaxValue }).GetRoot();
        Exception? exception = null;

        var thread = new Thread(() => exception = Record.Exception(() => new ChangingRewriter().Visit(root)), maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();

        Assert.IsType<InsufficientExecutionStackException>(exception);
    }

    [Fact]
    public void Create_ReportsTheDiagnosticsOfTheText()
    {
        var root = CssSyntaxTree.ParseText("a { } b { }").GetRoot();
        var first = (CssQualifiedRuleSyntax)root.Statements[0];
        var type = first.DescendantNodes().OfType<CssTypeSelectorSyntax>().Single();

        // The factory escapes what an identifier cannot hold, so the edit reads back as the name it was given.
        var edited = root.ReplaceNode(type, type.WithNameToken(SyntaxFactory.Identifier("a!").WithTriviaFrom(type.NameToken)));
        var tree = CssSyntaxTree.Create(edited);

        Assert.Equal("a\\! { } b { }", tree.GetText().Text);
        Assert.Equal("", CssSyntaxAssert.Diagnostics(tree));
        Assert.Equal("CSS0021@1:1", CssSyntaxAssert.Diagnostics(CssSyntaxTree.ParseText("a! { } b { }")));
    }

    [Fact]
    public void WithChanges_Reparses()
    {
        var tree = CssSyntaxTree.ParseText("a { color: red }");

        var changed = tree.WithChanges(new TextChange(new TextSpan(11, 3), "blue"));

        Assert.Equal("a { color: blue }", changed.GetRoot().ToFullString());
        Assert.Equal("blue", changed.GetRoot().DescendantNodes().OfType<CssDeclarationSyntax>().Single().GetValueText());
    }

    [Fact]
    public void GetDiagnostics_OfANode_FindsTheDiagnosticsWithinIt()
    {
        var tree = CssSyntaxTree.ParseText("a { b: } c { d: }");
        var rules = tree.GetRoot().Rules.ToArray();

        Assert.Single(tree.GetDiagnostics(rules[0]));
        Assert.Single(tree.GetDiagnostics(rules[1]));
        Assert.Equal(2, tree.GetDiagnostics().Count);
    }

    private static string DumpPrelude(string text, int statementIndex = 0)
    {
        var rule = (CssRuleSyntax)CssSyntaxAssert.TextIsFaithful(text).GetRoot().Statements[statementIndex];

        return CssSyntaxAssert.Dump(rule.GetPrelude());
    }

    /// <summary>Parses <paramref name="text"/> on a dedicated thread so a parser that fails to make progress fails the test instead of hanging the test run.</summary>
    private static CssSyntaxTree ParseWithTimeout(string text)
    {
        CssSyntaxTree? tree = null;
        var thread = new Thread(() => tree = CssSyntaxTree.ParseText(text)) { IsBackground = true };
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), $"Parsing '{text}' did not complete; the parser is likely stuck on a token it never consumes.");

        return tree!;
    }

    private sealed class CountingWalker() : CssSyntaxWalker(SyntaxWalkerDepth.Trivia)
    {
        public int Declarations { get; private set; }

        public int Comments { get; private set; }

        public override void VisitDeclaration(CssDeclarationSyntax node)
        {
            Declarations++;
            base.VisitDeclaration(node);
        }

        public override void VisitTrivia(SyntaxTrivia trivia)
        {
            if (trivia.IsComment())
            {
                Comments++;
            }
        }
    }

    private sealed class RenameClassRewriter : CssSyntaxRewriter
    {
        public override SyntaxNode? VisitClassSelector(CssClassSelectorSyntax node)
            => node.Name == "old" ? node.WithNameToken(SyntaxFactory.Identifier("new").WithTriviaFrom(node.NameToken)) : base.VisitClassSelector(node);
    }

    private sealed class ChangingRewriter : CssSyntaxRewriter
    {
        public override SyntaxNode? VisitTypeSelector(CssTypeSelectorSyntax node) => node.WithNameToken(SyntaxFactory.Identifier("b").WithTriviaFrom(node.NameToken));
    }

    /// <summary>A small generator whose sequence does not change between runtimes, so a failure can be reproduced from its seed.</summary>
    private sealed class DeterministicRandom(int seed)
    {
        private uint _state = (uint)seed | 1;

        public int Next(int exclusiveMax)
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;

            return exclusiveMax <= 0 ? 0 : (int)(_state % (uint)exclusiveMax);
        }
    }
}
