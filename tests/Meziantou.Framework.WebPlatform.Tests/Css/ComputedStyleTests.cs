using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

public sealed class ComputedStyleTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html, ViewportOptions? viewport = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions { Viewport = viewport });
        return context.Navigate(_ => html);
    }

    private static CssStyleProperties GetStyle(Document document, string selector, string? pseudoElement = null) =>
        document.DefaultView!.GetComputedStyle(document.QuerySelector(selector)!, pseudoElement);

    private string Get(string html, string property, string selector = "#target", string? pseudoElement = null, ViewportOptions? viewport = null) =>
        GetStyle(Parse(html, viewport), selector, pseudoElement).GetPropertyValue(property);

    // ----------------------------------------------------------------------------------------------------------------
    // Cascade
    // ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("<style>div { color: red } #target { color: green }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#target { color: green } div { color: red }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>div { color: red } div { color: green }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>div { color: green !important } #target { color: red }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>div { color: red }</style><div id=target style='color: green'></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>div { color: green !important }</style><div id=target style='color: red'></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>div { color: red !important }</style><div id=target style='color: green !important'></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>.a.b { color: green } .c { color: red }</style><div id=target class='a b c'></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>:where(#target) { color: red } div { color: green }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#target { color: red } :is(#target, div) { color: green }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>:is(#target, div) { color: green } div.a { color: red }</style><div id=target class=a></div>", "rgb(0, 128, 0)")]
    public void Cascade_SpecificityImportanceAndOrder(string html, string expected) => Assert.Equal(expected, Get(html, "color"));

    // The rules whose subject has one of several tags are candidates of the elements of each tag, and the ancestor filter
    // matches the ancestors case-insensitively where the selectors do
    [Theory]
    [InlineData("<!DOCTYPE html><style>:is(h1, p) { color: green }</style><p id=target></p>", "rgb(0, 128, 0)")]
    [InlineData("<!DOCTYPE html><style>:where(:is(h1, P), span) { color: green }</style><p id=target></p>", "rgb(0, 128, 0)")]
    [InlineData("<!DOCTYPE html><style>:is(h1, p) { color: red }</style><div id=target></div>", "rgb(0, 0, 0)")]
    [InlineData("<!DOCTYPE html><style>:heading { color: green }</style><h3 id=target></h3>", "rgb(0, 128, 0)")]
    [InlineData("<!DOCTYPE html><style>:heading(2) { color: green }</style><div headingoffset=1><h1 id=target></h1></div>", "rgb(0, 128, 0)")]
    [InlineData("<!DOCTYPE html><style>.a > div #target { color: green }</style><section class=a><div><p><span id=target></span></p></div></section>", "rgb(0, 128, 0)")]
    [InlineData("<!DOCTYPE html><style>.b .a #target { color: red }</style><section class=a><div class=b><span id=target></span></div></section>", "rgb(0, 0, 0)")]
    [InlineData("<style>.A #B span { color: green }</style><div class=a><p id=b><span id=target></span></p></div>", "rgb(0, 128, 0)")] // quirks mode
    [InlineData("<!DOCTYPE html><style>SECTION span { color: green }</style><section><span id=target></span></section>", "rgb(0, 128, 0)")]
    [InlineData("<!DOCTYPE html><style>.a span::before { content: 'x'; color: green }</style><div class=a><span id=target></span></div>", "rgb(0, 128, 0)", "::before")]
    public void Cascade_CandidateRules(string html, string expected, string? pseudoElement = null) => Assert.Equal(expected, Get(html, "color", pseudoElement: pseudoElement));

    [Fact]
    public void Cascade_AncestorFilterFollowsTheMutations()
    {
        var document = Parse("<!DOCTYPE html><style>.a .b span { color: green }</style><div id=outer><div class=b><span id=target></span></div></div>");
        Assert.Equal("rgb(0, 0, 0)", GetStyle(document, "#target").GetPropertyValue("color"));
        document.GetElementById("outer")!.ClassName = "a";
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "#target").GetPropertyValue("color"));
    }

    // https://drafts.csswg.org/css-overflow-3/#valdef-overflow-overlay: overlay is a legacy value alias of auto
    [Theory]
    [InlineData("overflow: overlay", "overflow-x", "auto")]
    [InlineData("overflow: overlay", "overflow-y", "auto")]
    [InlineData("overflow-y: overlay", "overflow-y", "auto")]
    [InlineData("overflow: hidden overlay", "overflow", "hidden auto")]
    public void Overflow_OverlayIsAnAliasOfAuto(string declaration, string property, string expected)
        => Assert.Equal(expected, Get("<div id=target style='" + declaration + "'></div>", property));

    // As in Gecko and WebKit, the MathML links have the default style of the HTML links
    [Fact]
    public void MathMLLink_HasTheColorAndTheUnderlineOfTheLinks()
    {
        var style = GetStyle(Parse("<math><a id=target href='https://example.com/'><mtext>a</mtext></a></math>"), "#target");
        Assert.Equal("rgb(0, 0, 238)", style.GetPropertyValue("color"));
        Assert.Equal("underline", style.GetPropertyValue("text-decoration-line"));
    }

    // WPT css/css-syntax/missing-semicolon.html (a reftest): the last declaration of a block needs no semicolon
    [Fact]
    public void Cascade_LastDeclarationWithoutSemicolon()
        => Assert.Equal("rgb(0, 128, 0)", Get("<style>#target {\n  /* This { needs to be there */\n  color: red;\n  color: green\n}\n</style><div id=target></div>", "color"));

    // https://drafts.csswg.org/css-text-4/#letter-spacing-property: "a computed letter-spacing of zero yields a resolved value
    // (getComputedStyle() return value) of normal"; the default fitting target of text-fit is omitted
    [Theory]
    [InlineData("letter-spacing: 0px", "letter-spacing", "normal")]
    [InlineData("letter-spacing: calc(1px - 1px)", "letter-spacing", "normal")]
    [InlineData("letter-spacing: normal", "letter-spacing", "normal")]
    [InlineData("letter-spacing: 2px", "letter-spacing", "2px")]
    [InlineData("text-fit: grow consistent 300%", "text-fit", "grow 300%")]
    [InlineData("text-fit: shrink per-line", "text-fit", "shrink per-line")]
    public void ResolvedValue_TextProperties(string declaration, string property, string expected) => Assert.Equal(expected, Get($"<div id=target style='{declaration}'></div>", property));

    [Theory]
    [InlineData("border-spacing: 5px 5px", "border-spacing", "5px")]
    [InlineData("border-spacing: 5px 6px", "border-spacing", "5px 6px")]
    [InlineData("border-spacing: var(--undefined)px", "border-spacing", "0px")]
    [InlineData("transform-origin: 1px 2px 0px", "transform-origin", "1px 2px")]
    [InlineData("transform-origin: 1px 2px 3px", "transform-origin", "1px 2px 3px")]
    [InlineData("stroke-dashoffset: calc(2 + 3)", "stroke-dashoffset", "5px")]
    [InlineData("stroke-dasharray: calc(-5%)", "stroke-dasharray", "0%")]
    [InlineData("grid-auto-rows: calc(-5%) calc(-5px) 10%", "grid-auto-rows", "0% 0px 10%")]
    [InlineData("image-resolution: calc(100dpi - 20dpi * sign(1px))", "image-resolution", "0.833333dppx")]
    [InlineData("aspect-ratio: 16 auto", "aspect-ratio", "auto 16 / 1")]
    [InlineData("stroke-dasharray: 10px 20% 30px", "stroke-dasharray", "10px, 20%, 30px")]
    [InlineData("flex-basis: calc(10% + 0px)", "flex-basis", "10%")]
    [InlineData("text-shadow: 1px 1px calc(-5px)", "text-shadow", "rgb(0, 0, 0) 1px 1px 0px")]
    public void ComputedValue_ShortestFormsAndMathFunctions(string declaration, string property, string expected) => Assert.Equal(expected, Get($"<div id=target style='{declaration}'></div>", property));

    // https://drafts.csswg.org/css-backgrounds/#border-width: a border that is not drawn has a used width of 0, but its
    // computed value (the inherited value) keeps the length
    [Theory]
    [InlineData("<div style='border-top: 5px none'><div id=target style='border-top: solid; border-top-width: inherit'></div></div>", "5px")]
    [InlineData("<div style='border-top: thick hidden'><div id=target style='border-top: solid; border-top-width: inherit'></div></div>", "5px")]
    [InlineData("<div style='border-top: 5px none'><div id=target style='border-top-width: inherit'></div></div>", "0px")]
    public void Cascade_InheritedBorderWidth(string html, string expected) => Assert.Equal(expected, Get(html, "border-top-width"));

    [Theory]
    [InlineData("<style>@layer a, b; @layer b { div { color: green } } @layer a { div { color: red } }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@layer a { #target { color: red } } div { color: green }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@layer a { div { color: green !important } } div { color: red !important }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@layer a { @layer b { div { color: red } } div { color: green } }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@layer { div { color: red } } @layer { div { color: green } }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@layer a.b { div { color: red } } @layer a { div { color: green } }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@property --c { syntax: '<color>'; inherits: true; initial-value: green } @layer { @property --c { syntax: '<color>'; inherits: true; initial-value: red } } div { color: var(--c) }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@layer a, b; @layer b { @property --c { syntax: '<color>'; inherits: true; initial-value: green } } @layer a { @property --c { syntax: '<color>'; inherits: true; initial-value: red } } div { color: var(--c) }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@import url(a.css) layer(b); @import url(b.css) layer(a); @layer a { div { color: green } } @layer b { div { color: red } }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@import url(a.css) layer(b) print; @import url(b.css) layer(a); @layer a { div { color: red } } @layer b { div { color: green } }</style><div id=target></div>", "rgb(0, 128, 0)")]
    public void Cascade_Layers(string html, string expected) => Assert.Equal(expected, Get(html, "color"));

    [Theory]
    [InlineData("<style>@namespace url(http://www.w3.org/1999/xhtml); [title] { color: red }</style><svg><g id=target title=a></g></svg>", "rgb(0, 0, 0)")]
    [InlineData("<style>@namespace url(http://www.w3.org/1999/xhtml); .a { color: green }</style><div id=target class=a></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@namespace url(http://www.w3.org/2000/svg); *|div:is(.a) { color: green }</style><div id=target class=a></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@namespace url(http://www.w3.org/2000/svg); .a { color: red } *|*.a { color: green }</style><div id=target class=a></div>", "rgb(0, 128, 0)")]
    public void Cascade_DefaultNamespace(string html, string expected) => Assert.Equal(expected, Get(html, "color"));

    [Theory]
    [InlineData("<style>@media (min-width: 500px) { div { color: green } }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>div { color: green } @media (max-width: 500px) { div { color: red } }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>div { color: green } @media print { div { color: red } }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style media='print'>div { color: red }</style><style>div { color: green } @supports (display: grid) { div { background-color: green } }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>div { color: green } @supports (display: nonsense) { div { color: red } }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@container (min-width: 1px) { div { color: red } }</style><div id=target style='color: green'></div>", "rgb(0, 128, 0)")]
    public void Cascade_ConditionalRules(string html, string expected) => Assert.Equal(expected, Get(html, "color"));

    [Theory]
    [InlineData("<style>#c { container-type: inline-size; width: 400px } div { color: red } @container (min-width: 300px) { #target { color: green } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { container-type: inline-size; width: 200px } div { color: green } @container (width > 300px) { #target { color: red } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { container-type: size; width: 100px; height: 50px } @container (orientation: landscape) and (aspect-ratio: 2) { #target { color: green } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { container-type: inline-size; width: 400px } div { color: green } @container (height > 0px) { #target { color: red } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#a { container: a / inline-size; width: 400px } #b { container-type: inline-size; width: 100px } @container a (width = 400px) { #target { color: green } }</style><div id=a><div id=b><div id=target></div></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { container-type: inline-size } @container (width = 784px) { #target { color: green } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { container-type: inline-size; width: 50%; padding: 0 10px; box-sizing: border-box } @container (width = 372px) { #target { color: green } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { container-type: size; width: 100px } @container (height = 0px) { #target { color: green } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { container-type: inline-size; width: 400px; display: inline } div { color: green } @container (width >= 0px) { #target { color: red } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { container-type: inline-size; width: 400px; --x: 300px } @container (width > var(--x)) { #target { color: green } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { container-type: inline-size; width: 400px; font-size: 20px } @container (width = 20em) { #target { color: green } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { container-type: inline-size; width: 400px } @container (width > 100px) { @container (width < 500px) { #target { color: green } } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { container-type: inline-size; width: 400px } div { color: green } @container (width > 100px) or unknown(x) { #target { color: red } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { --theme: dark } @container style(--theme: dark) { #target { color: green } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { --theme: dark } div { color: green } @container style(not (--theme: dark)) { #target { color: red } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { --n: 5 } @container style(3 < --n <= 5) { #target { color: green } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { font-size: 20px } @container style(font-size: 20px) { #target { color: green } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { container-type: inline-size; width: 400px } @container (width = 400px) { #target::before { color: green } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)", "::before")]
    [InlineData("<style>#target { container-type: inline-size; width: 400px } @container (width = 400px) { #target::before { color: green } }</style><div id=target></div>", "rgb(0, 128, 0)", "::before")]
    [InlineData("<style>@property --c { syntax: '<color>'; inherits: false; initial-value: transparent } #c { --c: oklch(50% 0 40) } @container style(--c: oklch(50% 0 200)) { #target { color: green } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@property --c { syntax: '<color>'; inherits: false; initial-value: transparent } #c { --c: lch(50% 0.0016 30) } div { color: green } @container style(--c: lch(50% 0.0016 200)) { #target { color: red } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { container-type: scroll-state } @container scroll-state(stuck: none) and scroll-state(not (scrollable)) { #target { color: green } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#c { container-type: scroll-state } div { color: green } @container scroll-state((snapped) or (scrolled: top)) { #target { color: red } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>div { color: green } @container scroll-state(stuck: none) { #target { color: red } }</style><div id=c><div id=target></div></div>", "rgb(0, 128, 0)")] // No scroll-state container
    public void Cascade_ContainerQueries(string html, string expected, string? pseudoElement = null) => Assert.Equal(expected, Get(html, "color", pseudoElement: pseudoElement));

    [Theory]
    [InlineData("<style>#c { container-type: inline-size; width: 400px } #target { width: 10cqw }</style><div id=c><div id=target></div></div>", "40px")]
    [InlineData("<style>#c { container-type: inline-size; width: 400px } #target { width: 10cqh }</style><div id=c><div id=target></div></div>", "60px")]
    [InlineData("<style>#c { container-type: size; width: 400px; height: 200px } #target { width: 10cqmin }</style><div id=c><div id=target></div></div>", "20px")]
    [InlineData("<style>#c { container-type: inline-size; width: 400px; display: none } #target { width: 10cqw }</style><div id=c><div id=target></div></div>", "80px")]
    [InlineData("<style>#target { width: 10cqi }</style><div id=target></div>", "80px")]
    public void ContainerQueryLengthUnits(string html, string expected) => Assert.Equal(expected, Get(html, "width"));

    [Fact]
    public void Cascade_MediaQueriesUseTheViewport()
    {
        const string Html = "<style>div { width: 1px } @media (max-width: 600px) { div { width: 2px } } @media (prefers-color-scheme: dark) { div { color: white } }</style><div id=target></div>";
        Assert.Equal("1px", Get(Html, "width"));
        Assert.Equal("2px", Get(Html, "width", viewport: new ViewportOptions { Width = 400 }));
        Assert.Equal("rgb(255, 255, 255)", Get(Html, "color", viewport: new ViewportOptions { PreferredColorScheme = PreferredColorScheme.Dark }));
    }

    [Theory]
    [InlineData("<style>.a { .b { color: green } }</style><div class=a><div id=target class=b></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>.a { &.b { color: green } }</style><div id=target class='a b'></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>.a { & > .b { color: red } color: green }</style><div id=target class=a></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>.a { .c & { color: green } }</style><div class=c><div id=target class=a></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>.a { @media (min-width: 1px) { color: green } }</style><div id=target class=a></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#target { .x { } color: red; color: green }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#nomatch, .a { color: red; :is(&, div.nomatch) { color: green } color: red }</style><div id=target class=a></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>.a { @layer second, first; } @layer first, second; @layer second { #target { color: green } } @layer first { #target { color: red } }</style><div id=target class=a></div>", "rgb(0, 128, 0)")]
    public void Cascade_Nesting(string html, string expected) => Assert.Equal(expected, Get(html, "color"));

    [Theory]
    [InlineData("<style>@scope (.card) { span { color: green } }</style><div class=card><span id=target></span></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@scope (.card) { span { color: red } } span { color: green }</style><span id=target></span>", "rgb(0, 128, 0)")]
    [InlineData("<style>@scope (.card) to (.content) { span { color: red } } span { color: green }</style><div class=card><div class=content><span id=target></span></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@scope (.a) { span { color: red } } @scope (.b) { span { color: green } }</style><div class=b><div class=a><div class=b><span id=target></span></div></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@scope (.b) { span { color: green } } @scope (.a) { span { color: red } }</style><div class=a><div class=b><span id=target></span></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@scope (.card) { :scope { color: green } }</style><div class=card id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<div><style>@scope { span { color: green } }</style><span id=target></span></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@scope (.a) to (:scope) { div { color: red } } div { color: green }</style><div class=a><div id=target></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>.a { @scope (.b) { &.c { color: green } } }</style><div class=a><div class=b><div id=target class='b c'></div></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>.a { @scope (.b) { &.c { color: red } } } div { color: green }</style><div class=b><div id=target class='b c'></div></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@scope (.a) { @scope (> .b) { :scope { color: green } } }</style><div class=a><div id=target class=b></div></div>", "rgb(0, 128, 0)")]
    public void Cascade_Scope(string html, string expected) => Assert.Equal(expected, Get(html, "color"));

    [Theory]
    [InlineData("<style>@scope (:host) { .a { color: green } }</style><div class=a></div>", ".a")]
    [InlineData("<style>@scope (:host) { :scope { color: green } }</style>", null)]
    [InlineData("<style>@scope { :scope { color: green } :scope > .a { color: green } }</style><div class=a></div>", ".a")]
    [InlineData("<style>@scope { :scope { color: green } }</style>", null)]
    public void Cascade_ScopeInShadowTree(string shadowHtml, string? selector)
    {
        var document = Parse("<div id=host><template shadowrootmode=open>" + shadowHtml + "</template></div>");
        var host = document.GetElementById("host")!;
        var element = selector is null ? host : host.ShadowRoot!.QuerySelector(selector)!;
        Assert.Equal("rgb(0, 128, 0)", document.DefaultView!.GetComputedStyle(element).Color);
    }

    // The featureless shadow host (https://drafts.csswg.org/css-scoping/#host-element-in-tree, https://drafts.csswg.org/selectors-4/#featureless)
    [Theory]
    [InlineData("<style>:is(:host) { color: green }</style>", null)]
    [InlineData("<style>:host { color: red } :is(div, :host) { color: green }</style>", null)]
    [InlineData("<style>:host { color: red } :not(div, :not(:host)) { color: green }</style>", null)]
    [InlineData("<style>:host { color: green } :host:not(div) { color: red } :host:not(:hover) { color: red } :not(div):host { color: red }</style>", null)]
    [InlineData("<style>@namespace url(http://www.w3.org/2000/svg); :host { color: green }</style>", null)]
    [InlineData("<style>:host { &::before { content: 'a'; color: green } }</style>", "::before")]
    [InlineData("<style>:host div { color: green } :not(:defined) div { color: red } :has(div) div { color: red }</style><div class=a></div>", ".a")]
    [InlineData("<style>div { color: red } :host:has(.a) .a { color: green }</style><div class=a></div>", ".a")]
    public void Cascade_FeaturelessShadowHost(string shadowHtml, string? selector)
    {
        var document = Parse("<div id=host><template shadowrootmode=open>" + shadowHtml + "</template></div>");
        var host = document.GetElementById("host")!;
        var window = document.DefaultView!;
        var style = selector switch
        {
            null => window.GetComputedStyle(host),
            "::before" => window.GetComputedStyle(host, "::before"),
            _ => window.GetComputedStyle(host.ShadowRoot!.QuerySelector(selector)!),
        };
        Assert.Equal("rgb(0, 128, 0)", style.Color);
    }

    [Theory]
    // A parent selector with a pseudo-element has no specificity in the nesting selector
    [InlineData("<style>div { color: green } *, ::before { & * { color: red } }</style><div id=target></div>")]
    // :has() does not match in the argument of :has(), also through the nesting selector
    [InlineData("<style>ul { color: green } li:has(b) { :has(> &) { color: red } }</style><ul id=target><li><b></b></li></ul>")]
    // The fallback of var() is a <declaration-value>: a '!' or a ';' at its top level is invalid
    [InlineData("<style>div { --a: green; --a: var(--b,!); color: var(--a) }</style><div id=target></div>")]
    [InlineData("<style>div { color: green; color: var(--a,;) }</style><div id=target></div>")]
    // A default namespace applies to the compound selectors of the argument of :is() that are not its subject
    [InlineData("<style>@namespace 'http://www.w3.org/1999/xhtml'; *|* { color: green } *|*:is(.container .a) { color: red }</style><svg><g class=container><text id=target class=a></text></g></svg>")]
    // @font-face is not the only at-rule of a conditional group rule: @supports selector() resolves the declared prefixes
    [InlineData("<style>@namespace x 'http://www.w3.org/1999/xlink'; div { color: red } @supports selector(x|y) { div { color: green } }</style><div id=target></div>")]
    [InlineData("<style>div { color: red } @supports selector(a::details-content::before) { div { color: green } }</style><div id=target></div>")]
    // An important revert-layer rolls back to the layers below its own, whatever the importance of the following layers
    [InlineData("<style>@layer { div { color: green } } @layer { div { color: red; color: revert-layer !important } } @layer { div { color: red; color: red !important } }</style><div id=target></div>")]
    // The children of a details element inherit from its ::details-content pseudo-element
    [InlineData("<style>details::details-content { color: green }</style><details open><div id=target></div></details>")]
    public void Cascade_IsGreen(string html) => Assert.Equal("rgb(0, 128, 0)", Get(html, "color"));

    [Theory]
    [InlineData("<style>div { color: green } div { color: revert }</style><div id=target></div>", "rgb(0, 0, 0)")]
    [InlineData("<style>@layer a { div { color: green } } @layer b { div { color: revert-layer } }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>div { color: green } div { color: revert-layer }</style><div id=target></div>", "rgb(0, 0, 0)")]
    [InlineData("<style>div { display: revert }</style><div id=target></div>", "block")]
    public void Cascade_Revert(string html, string expected)
    {
        var property = html.Contains("display", StringComparison.Ordinal) ? "display" : "color";
        Assert.Equal(expected, Get(html, property));
    }

    [Theory]
    [InlineData("<style>div { color: green } div { color: red; color: revert-rule }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>#target#target { color: red; color: revert-rule } #target { color: green }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>div { color: green }</style><div id=target style='color: revert-rule'></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@layer { div { color: green } } @layer { div { color: revert-rule } }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>div { color: revert-rule !important } div { color: green }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>div { color: green } div { color: revert-rule } div { color: revert-rule }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>@layer a { div { color: green } } @layer b { div { color: red } div { color: revert-layer } } @layer c { div { color: revert-rule } }</style><div id=target></div>", "rgb(0, 128, 0)")]
    [InlineData("<style>body { color: green } div { color: red; color: revert-rule }</style><div id=target></div>", "rgb(0, 128, 0)")]
    public void Cascade_RevertRule(string html, string expected) => Assert.Equal(expected, Get(html, "color"));

    [Fact]
    public void Cascade_RevertRule_Cycle()
    {
        // revert-rule !important rolls back to revert-layer, which rolls back to the lower layers, including revert-rule !important
        var html = "<style>body { color: green } @layer base { div { color: red } } @layer a { div { color: revert-rule !important } } @layer b { div { color: revert-layer } }</style><div id=target></div>";
        Assert.Equal("rgb(0, 128, 0)", Get(html, "color"));
    }

    [Theory]
    [InlineData("<!DOCTYPE html><style>h1 { margin: 0; margin-block-start: revert }</style><h1 id=target></h1>", "margin-top", "21.44px")]
    [InlineData("<!DOCTYPE html><style>h1 { margin: 0; margin: revert }</style><h1 id=target></h1>", "margin-top", "21.44px")]

    // In quirks mode, revert rolls back to the user agent rule of the margin collapsing quirks (https://html.spec.whatwg.org/multipage/rendering.html#margin-collapsing-quirks)
    [InlineData("<style>h1 { margin: 0; margin: revert }</style><h1 id=target></h1>", "margin-top", "0px")]
    [InlineData("<style>@layer a { div { margin-block-start: 5px } } @layer b { div { all: revert-layer } }</style><div id=target></div>", "margin-top", "5px")]
    [InlineData("<style>body { all: revert }</style><body id=target></body>", "margin-bottom", "8px")]
    [InlineData("<style>img { width: revert-layer }</style><img id=target width=44>", "width", "44px")]
    [InlineData("<style>@layer { img { width: revert-layer } }</style><img id=target width=44>", "width", "44px")]
    public void Cascade_RevertLogicalPropertiesAndPresentationalHints(string html, string property, string expected) => Assert.Equal(expected, Get(html, property));

    // The computed values (the resolved values of width and height are the used values, from the layout)
    [Theory]
    [InlineData("<style>img { width: revert }</style><img id=target width=44>", "width", "auto")]
    [InlineData("<!DOCTYPE html><body><table id=target width=50%></table>", "width", "50%")]
    [InlineData("<!DOCTYPE html><body><img id=target width=200.%>", "width", "200%")]
    [InlineData("<!DOCTYPE html><body><picture><source srcset=a.png height=10><img id=target width=1 height=1></picture>", "width", "auto")]
    [InlineData("<!DOCTYPE html><body><table><tr><td id=target height=0></td></tr></table>", "height", "auto")]
    [InlineData("<math><mspace id=target width=50%></mspace></math>", "width", "auto")]
    [InlineData("<!DOCTYPE html><div id=target style='width: fit-content'></div>", "width", "fit-content")]
    [InlineData("<!DOCTYPE html><div id=target style='width: calc-size(any, 50px + 30%)'></div>", "width", "calc-size(any, 30% + 50px)")]
    [InlineData("<!DOCTYPE html><div id=target style='height: calc-size(min-content, size * 2)'></div>", "height", "calc-size(min-content, 2 * size)")]
    public void PresentationalHintsAndSizes_ComputedValues(string html, string property, string expected)
    {
        var document = Parse(html);
        Assert.Equal(expected, document.GetStyleEngine().GetStyle(document.QuerySelector("#target")!).GetComputedValue(property));
    }

    [Theory]
    [InlineData("scope((.scope))", ".scope > .x", "rgb(0, 128, 0)")]
    [InlineData("scope((.scope))", "main > .x", "rgb(0, 0, 0)")]
    [InlineData("scope((.scope) to (.limit))", ".limit > .x", "rgb(0, 0, 0)")]
    [InlineData("scope()", ".scope > .x", "rgb(0, 128, 0)")]
    public void Cascade_ImportScope(string scope, string selector, string expected)
    {
        var html = "<main><div class=scope><style>@import url(\"data:text/css,.x%7Bcolor:green%7D\") " + scope + ";</style><div class=x></div><div class=limit><div class=x></div></div></div><div class=x></div></main>";
        var document = Parse(html);
        Assert.Equal(expected, GetStyle(document, selector).Color);
        Assert.Contains(" " + scope, ((CssImportRule)document.StyleSheets[0].CssRules[0]).CssText, StringComparison.Ordinal);
    }

    [Fact]
    public void Cascade_AdoptedStyleSheetsAfterDocumentStyleSheets()
    {
        var document = Parse("<style>div { color: red }</style><div id=target></div>");
        var sheet = new CssStyleSheet(document);
        sheet.ReplaceSync("div { color: green }");
        document.AdoptedStyleSheets = [sheet];
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "#target").GetPropertyValue("color"));

        sheet.Disabled = true;
        Assert.Equal("rgb(255, 0, 0)", GetStyle(document, "#target").GetPropertyValue("color"));
    }

    [Fact]
    public void Cascade_AdoptedStyleSheets_ConstructorDocument()
    {
        var document = Parse("<div id=target></div>");
        var sheet = new CssStyleSheet(document);
        sheet.ReplaceSync("div { color: green }");
        document.AdoptedStyleSheets.Add(sheet);
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "#target").GetPropertyValue("color"));

        // The shadow roots of the document can adopt the sheet, but not another document
        var host = document.QuerySelector("#target")!;
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadow.AdoptedStyleSheets.Add(sheet);
        var other = Parse("<div></div>");
        Assert.Equal("NotAllowedError", Assert.Throws<DomException>(() => other.AdoptedStyleSheets.Add(sheet)).Name);
        Assert.Equal("NotAllowedError", Assert.Throws<DomException>(() => other.AdoptedStyleSheets.Add(new CssStyleSheet())).Name);
    }

    [Fact]
    public void Cascade_AdoptedStyleSheets_RemovedWhenTheShadowRootIsAdoptedByAnotherDocument()
    {
        var document = Parse("<div id=host></div><template></template>");
        var sheet = new CssStyleSheet(document);
        var host = document.QuerySelector("#host")!;
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadow.AdoptedStyleSheets.Add(sheet);

        // The content of a template is not another document for the adopted style sheets
        ((HtmlTemplateElement)document.QuerySelector("template")!).Content.AppendChild(host);
        document.Body!.AppendChild(host);
        Assert.Same(sheet, Assert.Single(shadow.AdoptedStyleSheets));

        Parse("<div></div>").Body!.AppendChild(host);
        Assert.Empty(shadow.AdoptedStyleSheets);
    }

    [Fact]
    public void Cascade_AlternativeStyleSheets()
    {
        Assert.Equal("rgb(0, 128, 0)", Get("<style title=a>div { color: green }</style><style title=b>div { color: red }</style><div id=target></div>", "color"));
    }

    [Fact]
    public void Cascade_PreferredStyleSheetSetIsTheFirstInsertedTitle()
    {
        var document = Parse("<div id=target></div>");
        var preferred = document.CreateElement("style");
        preferred.SetAttribute("title", "preferred");
        preferred.TextContent = "div { color: green }";
        document.Head!.InsertBefore(preferred, document.Head.FirstChild);
        var alternate = document.CreateElement("style");
        alternate.SetAttribute("title", "alternate");
        alternate.TextContent = "div { color: red }";
        document.Head.InsertBefore(alternate, document.Head.FirstChild);
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "#target").Color);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Shadow DOM
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Shadow_StylesAreEncapsulated()
    {
        var document = Parse("<style>span { color: red }</style><div id=host></div>");
        var host = document.GetElementById("host")!;
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadow.SetHtmlUnsafe("<style>span { color: green } :host { color: blue } p { color: inherit }</style><span></span><p></p>");
        var window = document.DefaultView!;
        Assert.Equal("rgb(0, 128, 0)", window.GetComputedStyle(shadow.QuerySelector("span")!).Color);
        Assert.Equal("rgb(0, 0, 255)", window.GetComputedStyle(host).Color);
        Assert.Equal("rgb(0, 0, 255)", window.GetComputedStyle(shadow.QuerySelector("p")!).Color);
    }

    [Fact]
    public void Shadow_HostRulesLoseAgainstOuterRules()
    {
        var document = Parse("<style>#host { color: green }</style><div id=host></div>");
        var host = document.GetElementById("host")!;
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadow.SetHtmlUnsafe("<style>:host { color: red; background-color: green !important }</style>");
        var style = document.DefaultView!.GetComputedStyle(host);
        Assert.Equal("rgb(0, 128, 0)", style.Color);
        Assert.Equal("rgb(0, 128, 0)", style.BackgroundColor);
    }

    [Fact]
    public void Shadow_SlottedElementsInheritFromTheSlot()
    {
        var document = Parse("<div id=host><span id=target></span></div>");
        var host = document.GetElementById("host")!;
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadow.SetHtmlUnsafe("<style>slot { color: green } ::slotted(span) { background-color: green }</style><div><slot></slot></div>");
        var style = document.DefaultView!.GetComputedStyle(document.GetElementById("target")!);
        Assert.Equal("rgb(0, 128, 0)", style.Color);
        Assert.Equal("rgb(0, 128, 0)", style.BackgroundColor);
    }

    [Fact]
    public void Shadow_SlottedRulesDoNotMatchSlots()
    {
        var document = Parse("<div id=host style='color: green'></div>");
        var host = document.GetElementById("host")!;
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadow.SetHtmlUnsafe("<div><slot id=slot></slot></div>");
        var innerShadow = shadow.QuerySelector("div")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        innerShadow.SetHtmlUnsafe("<style>::slotted(#slot) { color: red }</style><slot></slot>");
        Assert.Equal("rgb(0, 128, 0)", document.DefaultView!.GetComputedStyle(shadow.GetElementById("slot")!).Color);
    }

    [Fact]
    public void Shadow_SlottedRulesWinOverHostRules()
    {
        // The shadow tree of the element comes after the shadow trees of its slots in the tree of trees
        var document = Parse("<div id=list><span id=item></span></div>");
        var list = document.GetElementById("list")!;
        list.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open }).SetHtmlUnsafe("<style>::slotted(span) { color: green; background-color: red !important }</style><slot></slot>");
        var item = document.GetElementById("item")!;
        item.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open }).SetHtmlUnsafe("<style>:host { color: red; background-color: green !important }</style>");
        var style = document.DefaultView!.GetComputedStyle(item);
        Assert.Equal("rgb(0, 128, 0)", style.Color);
        Assert.Equal("rgb(0, 128, 0)", style.BackgroundColor);
    }

    [Fact]
    public void Shadow_Part()
    {
        var document = Parse("<style>#host::part(label) { color: green } #host::part(label):focus { color: red } ::part(label icon) { background-color: green }</style><div id=host></div>");
        var host = document.GetElementById("host")!;
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadow.SetHtmlUnsafe("<style>span { color: red; background-color: red }</style><span part='label icon'></span><span part=label></span><p></p>");
        var window = document.DefaultView!;
        var spans = shadow.QuerySelectorAll("span");
        Assert.Equal("rgb(0, 128, 0)", window.GetComputedStyle((Element)spans[0]).Color);
        Assert.Equal("rgb(0, 128, 0)", window.GetComputedStyle((Element)spans[0]).BackgroundColor);
        Assert.Equal("rgb(0, 128, 0)", window.GetComputedStyle((Element)spans[1]).Color);
        Assert.Equal("rgb(255, 0, 0)", window.GetComputedStyle((Element)spans[1]).BackgroundColor);

        // The part attribute is the part name list
        ((Element)spans[1]).Part.Value = "other";
        Assert.Equal("rgb(255, 0, 0)", window.GetComputedStyle((Element)spans[1]).Color);
    }

    [Fact]
    public void Shadow_Part_TreeCountingFunctions()
    {
        var document = Parse("<style>#host::part(p) { z-index: sibling-index(); order: calc(sibling-count() + 1) } span { z-index: sibling-index() }</style><div id=host><span></span><span id=slotted></span></div>");
        var host = document.GetElementById("host")!;
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadow.SetHtmlUnsafe("<style>:host { order: sibling-count() } ::slotted(span) { order: sibling-index() }</style><div></div><div></div><div part=p></div><slot></slot>");
        var window = document.DefaultView!;

        // A ::part() rule of the outer tree does not see the position of the part in the shadow tree: the functions are 0
        var part = window.GetComputedStyle(shadow.QuerySelector("[part]")!);
        Assert.Equal("0", part.ZIndex);
        Assert.Equal("1", part.Order);

        // The rules of the shadow tree see the elements of the outer trees
        Assert.Equal("1", window.GetComputedStyle(host).Order);
        var slotted = window.GetComputedStyle(document.GetElementById("slotted")!);
        Assert.Equal("2", slotted.Order);
        Assert.Equal("2", slotted.ZIndex);
    }

    [Fact]
    public void Shadow_Part_Exportparts()
    {
        var document = Parse("<style>#outer::part(fwd) { color: green } #outer::part(inner) { background-color: red } #outer::part(same) { background-color: green }</style><div id=outer></div>");
        var outerShadow = document.GetElementById("outer")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        outerShadow.SetHtmlUnsafe("<style>#middle::part(inner) { border-top-style: solid } ::part(inner) { border-left-style: solid }</style><div id=middle exportparts='inner : fwd, same'></div>");
        var innerShadow = outerShadow.GetElementById("middle")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        innerShadow.SetHtmlUnsafe("<style>::part(inner) { border-bottom-style: solid }</style><span part='inner same'></span>");
        var style = document.DefaultView!.GetComputedStyle(innerShadow.QuerySelector("span")!);
        Assert.Equal("rgb(0, 128, 0)", style.Color);
        Assert.Equal("rgb(0, 128, 0)", style.BackgroundColor);
        Assert.Equal("solid", style.BorderTopStyle);
        Assert.Equal("solid", style.BorderLeftStyle);

        // The originating element of ::part() is a shadow host of the tree of the style sheet: the featureless host of the
        // shadow tree only matches :host
        Assert.Equal("none", style.BorderBottomStyle);
    }

    [Theory]
    [InlineData("a", "a:a")]
    [InlineData(" a , b : c ,, d:e:f, g h, ::before: i, j: ::k", "a:a b:c ::before:i")]
    [InlineData("", "")]
    public void Shadow_Part_ParsePartMappingList(string value, string expected)
    {
        var mappings = Meziantou.Framework.WebPlatform.Css.Cascade.PartElementMap.ParsePartMappingList(value);
        Assert.Equal(expected, string.Join(' ', mappings.Select(m => m.InnerName + ":" + m.OuterName)));
    }

    [Fact]
    public void Shadow_Part_HostAndPseudoElements()
    {
        var document = Parse("<style>::part(label)::before { content: 'x'; color: green } #host::part(label) { color: green !important }</style><div id=host></div>");
        var host = document.GetElementById("host")!;
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadow.SetHtmlUnsafe("<style>:host::part(label) { color: red !important; background-color: green }</style><span part=label></span>");
        var span = shadow.QuerySelector("span")!;
        var window = document.DefaultView!;

        // The important declarations of the inner context win over the ones of the outer context
        Assert.Equal("rgb(255, 0, 0)", window.GetComputedStyle(span).Color);
        Assert.Equal("rgb(0, 128, 0)", window.GetComputedStyle(span).BackgroundColor);
        Assert.Equal("rgb(0, 128, 0)", window.GetComputedStyle(span, "::before").Color);
    }

    [Fact]
    public void Shadow_SuppressedSlotFallbackContentIsNotRendered()
    {
        var document = Parse("<div id=host><span id=assigned>assigned</span></div>");
        var host = document.GetElementById("host")!;
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadow.SetHtmlUnsafe("<slot><b style='color: red'>fallback <i>nested</i></b></slot>");
        var window = document.DefaultView!;
        var fallback = window.GetComputedStyle(shadow.QuerySelector("b")!);
        var nested = window.GetComputedStyle(shadow.QuerySelector("i")!);
        Assert.Equal(0u, fallback.Length);
        Assert.Equal("", fallback.Color);
        Assert.Equal(0u, nested.Length);

        // Without assigned nodes, the fallback content is rendered
        document.GetElementById("assigned")!.Remove();
        Assert.Equal("rgb(255, 0, 0)", fallback.Color);
        Assert.Equal("rgb(255, 0, 0)", nested.Color);

        host.AppendChild(document.CreateTextNode("text"));
        Assert.Equal("", fallback.Color);
        Assert.Equal("", nested.Color);
    }

    [Fact]
    public void Shadow_ElementsAssignedToASuppressedSlotAreNotRendered()
    {
        // The inner slot is fallback content of the outer slot, which has assigned nodes
        var document = Parse("<div id=host><span id=a slot=a></span><span id=b slot=b></span></div>");
        var host = document.GetElementById("host")!;
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadow.SetHtmlUnsafe("<slot name=a><slot name=b></slot></slot>");
        var window = document.DefaultView!;
        Assert.NotEqual(0u, window.GetComputedStyle(document.GetElementById("a")!).Length);
        Assert.Equal(0u, window.GetComputedStyle(document.GetElementById("b")!).Length);

        document.GetElementById("a")!.Remove();
        Assert.NotEqual(0u, window.GetComputedStyle(document.GetElementById("b")!).Length);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Inheritance and defaulting
    // ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("<div style='color: green'><span id=target></span></div>", "color", "rgb(0, 128, 0)")]
    [InlineData("<div style='background-color: green'><span id=target></span></div>", "background-color", "rgba(0, 0, 0, 0)")]
    [InlineData("<div style='background-color: green'><span id=target style='background-color: inherit'></span></div>", "background-color", "rgb(0, 128, 0)")]
    [InlineData("<div style='color: green'><span id=target style='color: initial'></span></div>", "color", "rgb(0, 0, 0)")]
    [InlineData("<div style='color: green'><span id=target style='color: unset'></span></div>", "color", "rgb(0, 128, 0)")]
    [InlineData("<div style='visibility: hidden'><span id=target></span></div>", "visibility", "hidden")]
    [InlineData("<div style='visibility: hidden'><span id=target style='visibility: visible'></span></div>", "visibility", "visible")]
    [InlineData("<div id=target style='all: initial'></div>", "display", "inline")]
    [InlineData("<div id=target></div>", "pointer-events", "auto")]
    public void Inheritance(string html, string property, string expected) => Assert.Equal(expected, Get(html, property));

    // ----------------------------------------------------------------------------------------------------------------
    // Custom properties
    // ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("<style>:root { --x: red }</style><div id=target></div>", "--x", "red")]
    [InlineData("<div style='--x: 1px'><div id=target></div></div>", "--x", "1px")]
    [InlineData("<div id=target style='--x: 1px; --y: var(--x) 2px'></div>", "--y", "1px 2px")]
    [InlineData("<div id=target style='--x: var(--y); --y: var(--x); --z: var(--x, 3px)'></div>", "--x", "")]
    [InlineData("<div id=target style='--x: var(--y); --y: var(--x); --z: var(--x, 3px)'></div>", "--z", "3px")]
    [InlineData("<div id=target style='--y: var(--undefined, 4px)'></div>", "--y", "4px")]
    [InlineData("<div id=target style='--x: 10px; margin-left: var(--x)'></div>", "margin-left", "10px")]
    [InlineData("<div id=target style='--x: 10px; margin: var(--x) 0'></div>", "margin-left", "0px")]
    [InlineData("<div id=target style='--x: 10px; margin: var(--x) 0'></div>", "margin-top", "10px")]
    [InlineData("<div id=target style='--gap: 20px; margin-left: calc(var(--gap) - 4px)'></div>", "margin-left", "16px")]
    [InlineData("<div style='color: green'><div id=target style='color: red; color: var(--undefined)'></div></div>", "color", "rgb(0, 128, 0)")]
    [InlineData("<div id=target style='display: none; width: var(--undefined)'></div>", "width", "auto")]
    [InlineData("<div id=target style='--name: --x; --x: 10px; width: var(var(--name), 20px)'></div>", "width", "10px")] // The name is provided by var()
    [InlineData("<div id=target style='--x: 10px; width: var({--x})'></div>", "width", "10px")]
    [InlineData("<div id=target style='--name: x; --x: 10px; width: var(var(--name), 20px)'></div>", "width", "20px")] // An invalid name uses the fallback
    [InlineData("<div id=target style='display: none; width: 1px; width: var(prop)'></div>", "width", "auto")] // Valid at parse time, invalid at computed-value time
    [InlineData("<div id=target style='margin-left: calc(608px - env(safe-area-inset-top))'></div>", "margin-left", "608px")]
    [InlineData("<div id=target style='width: env(unknown 1 2, 10px)'></div>", "width", "10px")] // The indices of the two-dimensional variables
    [InlineData("<div id=target style='display: none; width: env(unknown -1, 10px)'></div>", "width", "auto")] // Invalid: an index is a non-negative integer
    [InlineData("<div id=target style='display: none; width: env(unknown 0.5, 10px)'></div>", "width", "auto")]
    [InlineData("<div id=target style='display: none; width: env(safe-area-inset-top ())'></div>", "width", "auto")]
    [InlineData("<div id=target style='--a: 10px; --b: v\\61 r(--a); width: var(--b)'></div>", "--b", "10px")] // Escaped function names
    [InlineData("<div id=target style='--a: 10px; --b: v\\61 r(--a); width: var(--b)'></div>", "width", "10px")]
    [InlineData("<div id=target style='--a: 10px; width: \\76 ar(--a)'></div>", "width", "10px")]
    [InlineData("<div id=target style='--a: 10px; margin: V\\41R(--a) 0'></div>", "margin-top", "10px")]
    [InlineData("<div id=target style='margin-left: calc(608px - \\65 nv(safe-area-inset-top))'></div>", "margin-left", "608px")]
    [InlineData("<div id=target data-x=abc style='--b: \\61 ttr(data-x)'></div>", "--b", "\"abc\"")]
    [InlineData("<div id=target style='--b: \"v\\61 r(--a)\"'></div>", "--b", "\"v\\61 r(--a)\"")]
    [InlineData("<div id=target style='--a: ident(\"x\")'></div>", "--a", "x")]
    [InlineData("<div id=target style='--a3: 10px; font-size: 10px; width: var(ident(--a calc(3 * sign(1em - 1px))))'></div>", "width", "10px")]
    [InlineData("<div id=target style='container-name: --c ident(\"--my\" ident 42)'></div>", "container-name", "--c --myident42")]
    [InlineData("<div id=target style='container-name: ident(\"--x\" 1.5)'></div>", "container-name", "none")] // Not an integer: invalid at computed-value time
    [InlineData("<style>@property --len { syntax: '<length>'; inherits: false; initial-value: 5px }</style><div style='--len: 3px'><div id=target></div></div>", "--len", "5px")]
    [InlineData("<style>@property --len { syntax: '<length>'; inherits: false; initial-value: 5px }</style><div id=target style='--len: 2em; font-size: 10px'></div>", "--len", "20px")]
    [InlineData("<style>@property --len { syntax: '<length>'; inherits: false; initial-value: 5px }</style><div id=target style='--len: red'></div>", "--len", "5px")]
    [InlineData("<div id=target style='--x:;'></div>", "--x", " ")] // An empty value is a space
    [InlineData("<div id=target style='--e:; --x: var(--e)'></div>", "--x", " ")]
    [InlineData("<style>@function --f() { result:; } #target { --x: --f(); }</style><div id=target></div>", "--x", " ")]
    [InlineData("<div id=target style='--x: if(else: ;)'></div>", "--x", "")] // An empty if() branch is not a space
    [InlineData("<div style='--x:  '><div id=target></div></div>", "--x", " ")]
    [InlineData("<div id=target style='--a: a; --b: b; --x: var(--a)var(--b)'></div>", "--x", "a/**/b")] // Tokens that would merge are separated by a comment
    [InlineData("<div id=target style='--a: 1; --x: var(--a)%'></div>", "--x", "1/**/%")]
    [InlineData("<div id=target style='--a: /; --x: var(--a)*'></div>", "--x", "//**/*")]
    [InlineData("<div id=target style='--a: 1; --b: px; --x: var(--a)var(--b)'></div>", "--x", "1/**/px")]
    [InlineData("<div id=target style='--a: a; --x: var(--a) b'></div>", "--x", "a b")]
    [InlineData("<div id=target style='--a: a/* c */; --x: var(--a)b'></div>", "--x", "a/**/b")]
    [InlineData("<div id=target style='--a: 1px; --b: 2px; border-spacing: var(--a)var(--b)'></div>", "border-spacing", "1px 2px")]
    [InlineData("<style>@layer { #target { --x: a; width: 1px } } @layer { #target { --x: var(--u, revert-layer); width: var(--u, revert-layer) } }</style><div id=target></div>", "--x", "a")]
    [InlineData("<style>@layer { #target { --x: a; width: 1px } } @layer { #target { --x: var(--u, revert-layer); width: var(--u, revert-layer) } }</style><div id=target></div>", "width", "1px")]
    [InlineData("<style>#target { width: 1px } #target { width: var(--u, revert-rule) }</style><div id=target></div>", "width", "1px")]
    [InlineData("<div id=target data-n='--i' style='--i: url(\"a.png\"); background-image: var(attr(data-n type(*)))'></div>", "background-image", "none")] // attr()-taint in a URL
    [InlineData("<div id=target data-u='a.png' style='--i: attr(data-u); background-image: image-set(var(--i))'></div>", "background-image", "none")]
    [InlineData("<div id=target data-n='--i' style='--i: url(\"https://a.test/a.png\"); --j: var(attr(data-n type(*)))'></div>", "--j", "url(\"https://a.test/a.png\")")]
    [InlineData("<div id=target data-x='10px' style='width: attr(data-x type(<length>))'></div>", "width", "10px")]
    [InlineData("<div id=target data-x='3' style='--c: 3; background-image: if(style(--c: attr(data-x type(*))): url(https://a.test/a.png))'></div>", "background-image", "none")] // An if() condition that uses a tainted value
    [InlineData("<div id=target data-x='3' style='--c: attr(data-x type(*)); background-image: if(style(--c >= 2): url(https://a.test/a.png))'></div>", "background-image", "none")]
    [InlineData("<div id=target data-x='3' style='--c: 3; --d: attr(data-x); background-image: if(style(--c: 3): url(https://a.test/a.png); style(--d): none)'></div>", "background-image", "url(\"https://a.test/a.png\")")]
    [InlineData("<style>@function --f() { result: attr(data-u type(*)); } #target { list-style-image: --f() }</style><div id=target data-u='url(https://a.test/a.png)'></div>", "list-style-image", "none")] // The result of a custom function
    [InlineData("<style>@function --f(--a: attr(data-u type(*))) { --l: var(--a); result: --g(); } @function --g() { result: var(--l); } #target { list-style-image: --f() }</style><div id=target data-u='url(https://a.test/a.png)'></div>", "list-style-image", "none")]
    [InlineData("<style>@function --f(--a) { result: url(https://a.test/a.png); } #target { list-style-image: --f(attr(data-u)) }</style><div id=target data-u='x'></div>", "list-style-image", "url(\"https://a.test/a.png\")")]
    [InlineData("<div id=target data-foo='attr(data-bar type(*), 2px)' data-bar='attr(data-foo type(*), 3px)' style='--x: attr(data-foo type(*), 1px)'></div>", "--x", "1px")] // An attribute cycle: the fallback of the outermost attr()
    [InlineData("<div id=target data-foo='attr(data-foo type(*))' style='--x: attr(data-foo type(*))'></div>", "--x", "")]
    [InlineData("<div id=target data-foo='attr(data-bar type(*))' data-bar='attr(data-foo)' style='--x: attr(data-foo type(*))'></div>", "--x", "")] // A raw attr() of an attribute being substituted
    [InlineData("<div id=target data-foo='attr(data-bar, 1) var(--y)' data-bar='var(--y)' style='--y: attr(data-foo); --x: attr(data-foo type(*))'></div>", "--x", "\"var(--y)\" \"attr(data-bar, 1) var(--y)\"")] // A referenced custom property is computed on its own
    [InlineData("<div id=target data-foo='3' style='--x: attr(data-foo number)'></div>", "--x", "3")]
    [InlineData("<div id=target data-foo='3' style='--x: attr(data-foo xx, 1px)'></div>", "--x", "1px")] // An unknown unit
    [InlineData("<div id=target data-foo='3EM' style='--x: attr(data-foo type(<length>))'></div>", "--x", "3em")]
    [InlineData("<div id=target data-foo='lighter' style='--x: attr(data-foo type(<number> | lighter), 1)'></div>", "--x", "lighter")]
    [InlineData("<div id=target data-foo='3px' style='width: attr(data-foo &, 1px)'></div>", "width", "784px")] // Invalid arguments: the fallback is not used
    [InlineData("<div id=target style='--t: type(<length>); --x: attr(data-foo var(--t), 2px)' data-foo='1px'></div>", "--x", "1px")]
    [InlineData("<div id=target style='font-family: attr(data-foo)'></div>", "font-family", "\"\"")] // A missing attribute without type is an empty string
    [InlineData("<style>@property --b { syntax: '<length>'; inherits: false; initial-value: 4px }</style><div style='font-size: 10px'><div id=target data-foo='var(--b)' style='--b: 3em; font-size: attr(data-foo type(*))'></div></div>", "font-size", "10px")] // A cycle via the font-relative units through an attribute
    [InlineData("<style>@property --b { syntax: '<length>'; inherits: false; initial-value: 4px }</style><div id=target data-foo='5px' style='--b: 3em; font-size: attr(data-foo type(<length>), var(--b))'></div>", "font-size", "5px")] // The fallback is not used
    [InlineData("<div data-u='url(https://a.test/a.png)' style='--i: attr(data-u type(*))'><p><span id=target style='list-style-image: var(--i)'></span></p></div>", "list-style-image", "none")] // The attr()-taint of an inherited value
    [InlineData("<div style='--x: 1px'><p style='--y: 2px'><span><b id=target style='display: block; width: calc(var(--x) + var(--y))'></b></span></p></div>", "width", "3px")]
    [InlineData("<style>@property --r { syntax: '*'; inherits: false }</style><div style='--r: 1px'><p><span id=target style='--x: var(--r, 2px)'></span></p></div>", "--x", "2px")] // Not inherited
    [InlineData("<style>@property --r { syntax: '*'; inherits: false }</style><div style='--r: 1px'><p id=target style='--r: inherit'></p></div>", "--r", "1px")]
    public void CustomProperties(string html, string property, string expected) => Assert.Equal(expected, Get(html, property));

    // The elements that declare no custom property have the custom properties of their parent, with its values
    [Fact]
    public void CustomProperties_Names()
    {
        var document = Parse("<div id=a style='--x: 1'><p id=b><span id=c style='--y: 2'><b id=d style='--x: 3'></b></span></p></div>");

        Assert.Equal(["--x"], GetCustomPropertyNames("#a"));
        Assert.Equal(["--x"], GetCustomPropertyNames("#b"));
        Assert.Equal(["--x", "--y"], GetCustomPropertyNames("#c"));
        Assert.Equal(["--x", "--y"], GetCustomPropertyNames("#d"));
        Assert.Equal("1", GetStyle(document, "#b").GetPropertyValue("--x"));
        Assert.Equal("1", GetStyle(document, "#c").GetPropertyValue("--x"));
        Assert.Equal("3", GetStyle(document, "#d").GetPropertyValue("--x"));
        Assert.Equal("2", GetStyle(document, "#d").GetPropertyValue("--y"));

        string[] GetCustomPropertyNames(string selector)
        {
            var style = GetStyle(document, selector);
            return [.. Enumerable.Range(0, (int)style.Length).Select(i => style.Item((uint)i)).Where(name => name.StartsWith("--", StringComparison.Ordinal))];
        }
    }

    // A registered property that is not inherited has no value in the elements that do not declare it
    [Fact]
    public void CustomProperties_NamesOfRegisteredProperties()
    {
        var document = Parse("<style>@property --r { syntax: '*'; inherits: false }</style><div id=a style='--r: 1; --x: 2'><p id=b></p></div>");

        Assert.Equal(["--r", "--x"], GetCustomPropertyNames("#a"));
        Assert.Equal(["--x"], GetCustomPropertyNames("#b"));

        string[] GetCustomPropertyNames(string selector)
        {
            var style = GetStyle(document, selector);
            return [.. Enumerable.Range(0, (int)style.Length).Select(i => style.Item((uint)i)).Where(name => name.StartsWith("--", StringComparison.Ordinal))];
        }
    }

    // The styles share the computed values (see ComputedValueTable): the values that are not shared (e.g. long values) are kept by the style
    [Fact]
    public void ComputedValues_SharedBetweenStyles()
    {
        var url = "https://a.test/" + new string('a', 200) + ".png";
        var document = Parse($"<div id=a style='margin-top: 1px'></div><div id=b style='margin-top: 1px; list-style-image: url({url})'></div>");
        var engine = document.GetStyleEngine();
        var a = engine.GetStyle(document.QuerySelector("#a")!);
        var b = engine.GetStyle(document.QuerySelector("#b")!);

        // (more than a few values: the values are packed)
        foreach (var property in Meziantou.Framework.WebPlatform.Css.Values.CssPropertyRegistry.Properties.Where(property => !property.IsShorthand && property.Name != "list-style-image").Take(100))
        {
            Assert.Same(a.GetComputedValue(property.Name), b.GetComputedValue(property.Name));
        }

        Assert.Same(a.GetComputedValue("margin-top"), b.GetComputedValue("margin-top"));
        Assert.Equal("url(\"" + url + "\")", b.GetComputedValue("list-style-image"));
        Assert.Equal("none", a.GetComputedValue("list-style-image"));
    }

    // A full table of shared values is replaced: the styles keep the values of the previous table
    [Fact]
    public void ComputedValues_MoreDistinctValuesThanTheTable()
    {
        var count = Meziantou.Framework.WebPlatform.Css.Cascade.ComputedValueTable.MaxCount + 100;
        var document = Parse("<!DOCTYPE html>" + string.Concat(Enumerable.Range(0, count).Select(i => $"<div style='width: {i}px; margin-left: {i}.5px'></div>")));
        var engine = document.GetStyleEngine();
        var elements = document.QuerySelectorAll("div").Cast<Element>().ToArray();
        var properties = Meziantou.Framework.WebPlatform.Css.Values.CssPropertyRegistry.Properties.Where(property => !property.IsShorthand).Take(20).Select(property => property.Name).ToArray();
        foreach (var element in elements)
        {
            var style = engine.GetStyle(element);
            foreach (var property in properties)
            {
                _ = style.GetComputedValue(property);
            }
        }

        for (var i = 0; i < count; i++)
        {
            var style = engine.GetStyle(elements[i]);
            Assert.Equal($"{i}px", style.GetComputedValue("width"));
            Assert.Equal($"{i}.5px", style.GetComputedValue("margin-left"));
        }
    }

    [Theory]
    [InlineData("@function --f() { result: 12px; } #target { --a: --f(); }", "--a", "12px")]
    [InlineData("@function --f() { result: 12px; } #target { width: --f(); }", "width", "12px")]
    [InlineData("@function --f() { result: 12px; } #target { margin: --f() 0; }", "margin-top", "12px")]
    [InlineData("@function --f() returns <length> { result: calc(1em + 1px); } #target { font-size: 10px; --a: --f(); }", "--a", "11px")]
    [InlineData("@function --f() returns <length> { result: red; } #target { --a: --f(); }", "--a", "")]
    [InlineData("@function --f() { } #target { --a: --f(); }", "--a", "")]
    [InlineData("#target { --a: --unknown(); }", "--a", "")]
    [InlineData("@function --f(--x, --y: 2px) { result: var(--x) var(--y); } #target { --a: --f(1px); }", "--a", "1px 2px")]
    [InlineData("@function --f(--x <length>) { result: var(--x); } #target { --a: --f(calc(1px + 2px)); }", "--a", "3px")]
    [InlineData("@function --f(--x <length>: 5px) { result: var(--x); } #target { --a: --f(red); }", "--a", "5px")]
    [InlineData("@function --f(--x) { result: var(--x); } #target { --a: --f({1px, 2px}); }", "--a", "1px, 2px")]
    [InlineData("@function --f(--x) { result: 1; } #target { --a: --f(); }", "--a", "")] // Missing argument without default value
    [InlineData("@function --f(--x) { result: 1; } #target { --a: --f(1, 2); }", "--a", "")] // Too many arguments
    [InlineData("@function --f(--x) { --x: 3; --y: var(--x); result: var(--y) var(--z); } #target { --x: 1; --z: 4; --a: --f(2); }", "--a", "3 4")]
    [InlineData("@function --f(--x: 1) { --x: 2; --x: initial; result: var(--x); } #target { --a: --f(); }", "--a", "1")]
    [InlineData("@function --f(--x: 1) { --x: inherit; result: var(--x); } #target { --x: 2; --a: --f(); }", "--a", "2")]
    [InlineData("@function --f() { --x: 1; result: --g(); } @function --g() { result: var(--x) inherit(--x); } #target { --x: 0; --a: --f(); }", "--a", "1 1")]
    [InlineData("@function --f() { result: 1; @media (width > 0px) { result: 2; } @supports (color: nonsense) { result: 3; } } #target { --a: --f(); }", "--a", "2")]
    [InlineData("@function --f() { result: 1; } @layer { @function --f() { result: 2; } } #target { --a: --f(); }", "--a", "1")]
    [InlineData("@function --f() { result: --f(); } #target { --a: --f(); }", "--a", "")] // Cycle
    [InlineData("@function --f() { --x: var(--x); result: var(--x, 1); } #target { --a: --f(); }", "--a", "1")] // Cycle of a local variable
    [InlineData("@function --f() { --unused: --f(); result: 1; } #target { --a: --f(); }", "--a", "")] // Cycle through an unused local variable
    [InlineData("@function --f() { result: var(--b); } #target { --b: --f(); --a: --f(); }", "--a", "")] // Cycle through a custom property
    [InlineData("@function --f() { result: inherit; } #target { --a: --f(); }", "--a", "2")]
    public void CustomFunctions(string css, string property, string expected) => Assert.Equal(expected, Get("<style>" + css + "</style><div style='--a: 2'><div id=target></div></div>", property));

    [Fact]
    public void CustomFunctions_TreeScoped()
    {
        var document = Parse("<style>@function --f() { result: 1; } @function --g() { result: --f(); }</style><div id=host></div>");
        var shadowRoot = document.GetElementById("host")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadowRoot.SetHtmlUnsafe("<style>@function --f() { result: 2; } #target { --a: --f() --g(); }</style><div id=target></div>");
        Assert.Equal("2 1", document.DefaultView!.GetComputedStyle(shadowRoot.GetElementById("target")!).GetPropertyValue("--a"));
    }

    [Theory]
    [InlineData("random(property-index-scoped, 1, 1000000000)", true)] // The caching key of a typed result is the function and the result descriptor
    [InlineData("random(element-scoped, 1, 1000000000)", false)] // Each invocation is a distinct "hypothetical element"
    public void CustomFunctions_RandomInTypedResult(string random, bool shared)
    {
        var html = "<style>@property --n { syntax: '<number>+'; inherits: false; initial-value: 0 } @function --f() returns <number> { result: " + random + "; } #target { --n: --f() --f(); --a: --f(); }</style><div id=target></div>";
        var style = GetStyle(Parse(html), "#target");
        var values = style.GetPropertyValue("--n").Split(' ');
        Assert.Equal(shared, values[0] == values[1]);
        Assert.Equal(shared, values[0] == style.GetPropertyValue("--a"));
    }

    [Theory]
    [InlineData("<div id=target style='--x: if(style(random(0, 1) = random(0, 1)): same; else: different)'></div>", "different")] // The random() functions of an if() condition are keyed by property and index
    [InlineData("<div id=target style='--x: if(style(random(property-scoped, 0, 1) = random(property-scoped, 0, 1)): same; else: different)'></div>", "same")]
    [InlineData("<div id=target style='--x: if(supports(width: 30): yes; else: no)'></div>", "yes")] // The quirky lengths are valid in quirks mode
    [InlineData("<!doctype html><div id=target style='--x: if(supports(width: 30): yes; else: no)'></div>", "no")]
    [InlineData("<div id=target style='--x: if(supports(color: ff0000): yes; else: no)'></div>", "yes")]
    public void IfConditions(string html, string expected) => Assert.Equal(expected, Get(html, "--x"));

    [Theory]
    [InlineData("ident(\"a\" random(1, 1000000000))", false)] // The random() functions of an ident() are keyed by element, property and index
    [InlineData("ident(\"a\" random(property-index-scoped, 1, 1000000000))", true)]
    public void Ident_Random(string value, bool shared)
    {
        var document = Parse("<style>div { view-transition-name: " + value + " }</style><div id=a></div><div id=b></div>");
        var a = GetStyle(document, "#a").GetPropertyValue("view-transition-name");
        var b = GetStyle(document, "#b").GetPropertyValue("view-transition-name");
        Assert.StartsWith("a", a, StringComparison.Ordinal);
        Assert.Equal(shared, a == b);
    }

    [Theory]
    [InlineData("random(1, 30)", "")] // The random base value depends on the element: the initial value is not computationally independent
    [InlineData("random(fixed 0.5, 0, 30)", "15")]
    public void RegisteredProperty_RandomInitialValue(string initialValue, string expected)
        => Assert.Equal(expected, Get("<style>@property --x { syntax: '<number>'; inherits: false; initial-value: " + initialValue + " }</style><div id=target></div>", "--x"));

    [Fact]
    public void Supports_QuirkyLengthsAreNotValid() => Assert.False(Meziantou.Framework.WebPlatform.Css.Om.Css.Supports("width: 30"));

    // https://quirks.spec.whatwg.org/#the-unitless-length-quirk, https://quirks.spec.whatwg.org/#the-hashless-hex-color-quirk
    [Theory]
    [InlineData("<style>#target { margin-left: 5 }</style><div id=target></div>", "margin-left", "5px")]
    [InlineData("<!DOCTYPE html><style>#target { margin-left: 5 }</style><div id=target></div>", "margin-left", "0px")]
    [InlineData("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.01 Transitional//EN\" \"http://www.w3.org/TR/html4/loose.dtd\"><style>#target { margin-left: 5 }</style><div id=target></div>", "margin-left", "0px")] // Limited-quirks mode
    [InlineData("<style>#target { margin: 1 2px 3 -4 }</style><div id=target></div>", "margin-left", "-4px")]
    [InlineData("<style>#target { margin-left: 1.5 }</style><div id=target></div>", "margin-left", "1.5px")]
    [InlineData("<style>#target { margin-left: calc(5) }</style><div id=target></div>", "margin-left", "0px")] // Not in functions
    [InlineData("<style>#target { outline: solid 5 }</style><div id=target></div>", "outline-width", "3px")] // Not in the other properties
    [InlineData("<div id=target style='padding-top: 7'></div>", "padding-top", "7px")]
    [InlineData("<style>@supports (padding-top: 7) { #target { padding-top: 7px } }</style><div id=target></div>", "padding-top", "7px")]
    [InlineData("<style>@supports (color: ff0000) { #target { color: green } }</style><div id=target></div>", "color", "rgb(0, 128, 0)")]
    [InlineData("<style>#target { color: ff0000 }</style><div id=target></div>", "color", "rgb(255, 0, 0)")]
    [InlineData("<style>#target { color: abc }</style><div id=target></div>", "color", "rgb(170, 187, 204)")]
    [InlineData("<style>#target { color: 123 }</style><div id=target></div>", "color", "rgb(0, 1, 35)")]
    [InlineData("<style>#target { color: +12a }</style><div id=target></div>", "color", "rgb(0, 1, 42)")]
    [InlineData("<style>#target { color: 12345678 }</style><div id=target></div>", "color", "rgb(0, 0, 0)")] // Not 8 digits
    [InlineData("<style>#target { color: -1 }</style><div id=target></div>", "color", "rgb(0, 0, 0)")]
    [InlineData("<style>#target { color: abcd }</style><div id=target></div>", "color", "rgb(0, 0, 0)")]
    [InlineData("<style>#target { border-color: 123 abc }</style><div id=target></div>", "border-right-color", "rgb(170, 187, 204)")]
    [InlineData("<style>#target { background: abc }</style><div id=target></div>", "background-color", "rgba(0, 0, 0, 0)")] // Not in the shorthands
    [InlineData("<!DOCTYPE html><style>#target { color: ff0000 }</style><div id=target></div>", "color", "rgb(0, 0, 0)")]
    public void QuirkyValues(string html, string property, string expected) => Assert.Equal(expected, Get(html, property));

    [Theory]
    [InlineData("", "5px")]
    [InlineData("<!DOCTYPE html>", "")]
    public void QuirkyValues_SetProperty(string doctype, string expected)
    {
        var document = Parse(doctype + "<div id=target></div>");
        var element = (HtmlElement)document.GetElementById("target")!;
        element.Style.SetProperty("margin-left", "5");
        Assert.Equal(expected, element.Style.GetPropertyValue("margin-left"));
    }

    [Fact]
    public void QuirkyValues_InsertRule()
    {
        var document = Parse("<style></style><div id=target></div>");
        ((CssStyleSheet)document.StyleSheets[0]!).InsertRule("#target { margin-left: 5 }", 0);
        Assert.Equal("5px", GetStyle(document, "#target").GetPropertyValue("margin-left"));
    }

    // https://quirks.spec.whatwg.org/#the-tables-inherit-color-from-body-quirk
    [Theory]
    [InlineData("<body style='color: green'><div style='color: red'><table id=target></table></div>", "rgb(0, 128, 0)")]
    [InlineData("<!DOCTYPE html><body style='color: green'><div style='color: red'><table id=target></table></div>", "rgb(255, 0, 0)")]
    [InlineData("<style>table { color: blue }</style><body style='color: green'><table id=target></table>", "rgb(0, 0, 255)")]
    [InlineData("<body style='color: green'><div style='color: red'><table style='color: inherit'><tr><td id=target></td></tr></table></div>", "rgb(255, 0, 0)")]
    public void TablesInheritColorFromBodyQuirk(string html, string expected) => Assert.Equal(expected, Get(html, "color"));

    [Fact]
    public void TablesInheritColorFromBodyQuirk_BodyRemoved()
    {
        var document = Parse("<body style='color: red'></body>");
        var firstBody = document.Body!;
        var table = document.CreateElement("table");
        var secondBody = (HtmlElement)document.CreateElement("body");
        secondBody.Style.SetProperty("color", "green");
        document.DocumentElement!.AppendChild(table);
        document.DocumentElement.AppendChild(secondBody);
        Assert.Equal("rgb(255, 0, 0)", document.DefaultView!.GetComputedStyle(table).GetPropertyValue("color"));

        firstBody.Remove();
        Assert.Equal("rgb(0, 128, 0)", document.DefaultView!.GetComputedStyle(table).GetPropertyValue("color"));

        secondBody.Remove();
        Assert.Equal("rgb(0, 0, 0)", document.DefaultView!.GetComputedStyle(table).GetPropertyValue("color"));
    }

    [Theory]
    [InlineData("@mixin --m { @result { color: green; } } #target { color: red; @apply --m; }", "color", "rgb(0, 128, 0)")]
    [InlineData("@mixin --m { @result { color: red; } } #target { @apply --m; color: green; }", "color", "rgb(0, 128, 0)")]
    [InlineData("#target { color: green; @apply --unknown; }", "color", "rgb(0, 128, 0)")]
    [InlineData("@mixin --m { @result { .a { color: green; } } } div { @apply --m; }", "color", "rgb(0, 128, 0)")] // A nested rule is relative to the rule that applies the mixin
    [InlineData("@mixin --m { @result { &.a { color: green; } } } #target { @apply --m; }", "color", "rgb(0, 128, 0)")]
    [InlineData("@mixin --m { @result { color: green; } } @mixin --m { @result { color: red; } } @layer { @mixin --m { @result { color: blue; } } } #target { @apply --m; }", "color", "rgb(255, 0, 0)")]
    [InlineData("@mixin --m(--c, --d: green) { @result { color: var(--c); background-color: var(--d); } } #target { @apply --m(blue); }", "background-color", "rgb(0, 128, 0)")]
    [InlineData("@mixin --m(--c, --d: green) { @result { color: var(--c); background-color: var(--d); } } #target { @apply --m(blue); }", "color", "rgb(0, 0, 255)")]
    [InlineData("@mixin --m(--c) { @result { color: var(--c, green); } } #target { color: red; @apply --m; }", "color", "rgb(0, 128, 0)")] // A missing argument is the guaranteed-invalid value
    [InlineData("@mixin --m() { @result { color: red; } } #target { color: green; @apply --m(1); }", "color", "rgb(0, 128, 0)")] // Too many arguments
    [InlineData("@mixin --m(--l <length>) { @result { font-size: 10px; --a: var(--l); } } #target { @apply --m(2em); }", "--a", "20px")]
    [InlineData("@mixin --m { --c: var(--d); --d: green; font-size: 99px; @result { color: var(--c); } } #target { @apply --m; }", "color", "rgb(0, 128, 0)")] // Local variables
    [InlineData("@mixin --m { --c: green; @result { --a: var(--c); } } #target { @apply --m; }", "--c", "")] // The local variables are not custom properties of the element
    [InlineData("@mixin --m { @media (width > 0px) { @result { color: green; } } @media (width < 0px) { @result { color: red; } } } #target { @apply --m; }", "color", "rgb(0, 128, 0)")]
    [InlineData("@mixin --m { @result { @contents; } } #target { @apply --m { color: green; } }", "color", "rgb(0, 128, 0)")]
    [InlineData("@mixin --m { @result { @contents { color: green; } } } #target { @apply --m; }", "color", "rgb(0, 128, 0)")] // The fallback block
    [InlineData("@mixin --m { @result { @contents { color: red; } } } #target { color: green; @apply --m { } }", "color", "rgb(0, 128, 0)")]
    [InlineData("@mixin --i(--x) { @result { color: var(--y); } } @mixin --o(--y) { @result { @apply --i(red); } } #target { @apply --o(green); }", "color", "rgb(0, 128, 0)")] // The variables of the outer mixin
    [InlineData("@mixin --i(--x) { @result { color: var(--x); } } @mixin --o(--y) { @result { @apply --i(var(--y)); } } #target { @apply --o(green); }", "color", "rgb(0, 128, 0)")]
    [InlineData("@function --f() { result: var(--c); } @mixin --m(--c) { @result { color: --f(); } } #target { color: green; @apply --m(red); }", "color", "rgb(0, 0, 0)")] // A function does not see the variables of a mixin
    [InlineData("@mixin --a { @result { --a: a; @apply --b; } } @mixin --b { @result { --a: b; @apply --a; } } #target { @apply --a; }", "--a", "b")] // Cycle
    public void Mixins(string css, string property, string expected) => Assert.Equal(expected, Get("<style>" + css + "</style><div><div id=target class=a></div></div>", property));

    [Fact]
    public void Mixins_TreeScoped()
    {
        var document = Parse("<style>@mixin --m { @result { color: green; } } @mixin --n { @result { color: green; } }</style><div id=host></div>");
        var shadowRoot = document.GetElementById("host")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadowRoot.SetHtmlUnsafe("<style>@mixin --n { @result { color: blue; } } #a { @apply --m; } #b { @apply --n; }</style><div id=a></div><div id=b></div>");
        Assert.Equal("rgb(0, 128, 0)", document.DefaultView!.GetComputedStyle(shadowRoot.GetElementById("a")!).GetPropertyValue("color"));
        Assert.Equal("rgb(0, 0, 255)", document.DefaultView!.GetComputedStyle(shadowRoot.GetElementById("b")!).GetPropertyValue("color"));
    }

    [Fact]
    public void Mixins_TreeScoped_OuterMixinAdded()
    {
        // The @apply rules of a shadow tree are resolved again when a @mixin rule is added to the tree of a shadow host
        var document = Parse("<style>@mixin --m { @result { color: red; } }</style><div id=host></div>");
        var outerRoot = document.GetElementById("host")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        outerRoot.SetHtmlUnsafe("<div id=inner-host></div>");
        var innerRoot = outerRoot.GetElementById("inner-host")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        innerRoot.SetHtmlUnsafe("<style>#a { @apply --m; }</style><div id=a></div>");
        var target = innerRoot.GetElementById("a")!;
        Assert.Equal("rgb(255, 0, 0)", document.DefaultView!.GetComputedStyle(target).GetPropertyValue("color"));

        var style = document.CreateElement("style");
        style.TextContent = "@mixin --m { @result { color: green; } }";
        document.Body!.AppendChild(style);
        Assert.Equal("rgb(0, 128, 0)", document.DefaultView!.GetComputedStyle(target).GetPropertyValue("color"));
    }

    [Fact]
    public void Mixins_CssomChanges()
    {
        var document = Parse("<style>@mixin --m { @result { color: red; } }</style><style>#target { }</style><div id=target></div>");
        var target = document.GetElementById("target")!;
        var sheets = document.StyleSheets;
        Assert.Equal("rgb(0, 0, 0)", document.DefaultView!.GetComputedStyle(target).GetPropertyValue("color"));
        ((CssStyleRule)sheets[1]!.CssRules[0]!).InsertRule("@apply --m;");
        Assert.Equal("rgb(255, 0, 0)", document.DefaultView!.GetComputedStyle(target).GetPropertyValue("color"));
        var result = (CssMixinResultRule)((CssMixinRule)sheets[0]!.CssRules[0]!).CssRules[0]!;
        ((CssNestedDeclarations)result.CssRules[0]!).Style.SetProperty("color", "green");
        Assert.Equal("rgb(0, 128, 0)", document.DefaultView!.GetComputedStyle(target).GetPropertyValue("color"));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Computed values
    // ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("width: 1in", "width", "96px")]
    [InlineData("width: 2em; font-size: 10px", "width", "20px")]
    [InlineData("width: 2rem", "width", "32px")]
    [InlineData("display: none; width: 50%", "width", "50%")]
    [InlineData("display: none; width: calc(100% - 1em)", "width", "calc(100% - 16px)")]
    [InlineData("width: 50%", "width", "392px")] // The resolved value is the used value, from the layout
    [InlineData("width: calc(100% - 1em)", "width", "768px")]
    [InlineData("width: 50%; padding: 1px; border: 1px solid; box-sizing: border-box", "width", "392px")]
    [InlineData("width: fit-content", "width", "0px")]
    [InlineData("display: inline; width: 50%", "width", "50%")]
    [InlineData("width: calc(2px * 3)", "width", "6px")]
    [InlineData("width: 10vw", "width", "80px")]
    [InlineData("height: 10vh", "height", "60px")]
    [InlineData("width: auto", "width", "784px")]
    [InlineData("width: auto; margin: 0 10px; padding: 5%", "width", "685.6px")]
    [InlineData("display: none; width: auto", "width", "auto")]
    [InlineData("height: auto", "height", "0px")]
    [InlineData("margin: 1px 2px", "margin-left", "2px")]
    [InlineData("margin: 1px 2px", "margin", "1px 2px")]
    [InlineData("font-size: 20px", "font-size", "20px")]
    [InlineData("font-size: large", "font-size", "18px")]
    [InlineData("font-size: 150%", "font-size", "24px")]
    [InlineData("font-size: 2em", "font-size", "32px")]
    [InlineData("font-weight: bold", "font-weight", "700")]
    [InlineData("font-weight: normal", "font-weight", "400")]
    [InlineData("font-stretch: condensed", "font-stretch", "75%")]
    [InlineData("line-height: normal", "line-height", "normal")]
    [InlineData("line-height: 1.5", "line-height", "24px")]
    [InlineData("line-height: 10px", "line-height", "10px")]
    [InlineData("line-height: 10%", "line-height", "1.6px")]
    [InlineData("border-top-width: 5px", "border-top-width", "0px")]
    [InlineData("border-top: 5px solid", "border-top-width", "5px")]
    [InlineData("border-top: medium solid", "border-top-width", "3px")]
    [InlineData("border: thin solid red", "border-left-width", "1px")]
    [InlineData("border: 1.5px solid red", "border-left-width", "1px")]
    [InlineData("border: 0.5px solid red", "border-left-width", "1px")]
    [InlineData("outline: 2px solid", "outline-width", "2px")]
    [InlineData("color: red", "color", "rgb(255, 0, 0)")]
    [InlineData("color: #00ff0080", "color", "rgba(0, 255, 0, 0.5)")]
    [InlineData("color: hsl(120 100% 50%)", "color", "rgb(0, 255, 0)")]
    [InlineData("color: rgb(calc(255 / 2) 0 0)", "color", "rgb(128, 0, 0)")]
    [InlineData("color: transparent", "color", "rgba(0, 0, 0, 0)")]
    [InlineData("color: CanvasText", "color", "rgb(0, 0, 0)")]
    [InlineData("color: light-dark(green, red)", "color", "rgb(0, 128, 0)")]
    [InlineData("color-scheme: dark; color: light-dark(red, green)", "color", "rgb(0, 128, 0)")]
    [InlineData("color: green; border-color: currentcolor", "border-left-color", "rgb(0, 128, 0)")]
    [InlineData("color: green", "border-top-color", "rgb(0, 128, 0)")]
    [InlineData("color: green", "caret-color", "rgb(0, 128, 0)")]
    [InlineData("background-color: red", "background-color", "rgb(255, 0, 0)")]
    [InlineData("background-color: color-mix(in srgb, red, blue)", "background-color", "color(srgb 0.5 0 0.5)")]
    [InlineData("color: color-mix(in srgb, red, green, blue)", "color", "color(srgb 0.333333 0.16732 0.333333)")]
    [InlineData("color: color-mix(in rec2020, color(rec2020 0.1 0.2 0.3), color(rec2020 0.5 0.6 0.7))", "color", "color(rec2020 0.3 0.4 0.5)")]
    [InlineData("color: color-mix(in hsl, white, blue)", "color", "color(srgb 0.625 0.625 0.875)")]
    [InlineData("color: color-mix(in hsl, lch(100% 0 0), blue)", "color", "color(srgb 0.625 0.625 0.875)")]
    [InlineData("color: color-mix(in hsl, oklch(100% 0 0), blue)", "color", "color(srgb 0.625 0.625 0.875)")]
    [InlineData("color: color-mix(in hwb, oklch(100% 0 0), blue)", "color", "color(srgb 0.5 0.5 1)")]
    [InlineData("color: color-mix(in oklch, hsl(0 0 0))", "color", "oklch(0 0 none)")]
    [InlineData("color: rgb(from rebeccapurple r calc(g * 2) b)", "color", "color(srgb 0.4 0.4 0.6)")]
    [InlineData("color: hsl(from hsl(none 50% 50%) h s l)", "color", "hsl(none 50% 50%)")]
    [InlineData("color: red; background-color: alpha(from currentcolor / 0.5)", "background-color", "color(srgb 1 0 0 / 0.5)")]
    [InlineData("opacity: 1.5", "opacity", "1")]
    [InlineData("opacity: 50%", "opacity", "0.5")]
    [InlineData("z-index: calc(1.4 + 1)", "z-index", "2")]
    [InlineData("transition-duration: 100ms", "transition-duration", "0.1s")]
    [InlineData("rotate: 0.5turn", "rotate", "180deg")]
    [InlineData("transform: none", "transform", "none")] // The resolved value of transform is a matrix
    [InlineData("transform: translate(10px, 2em)", "transform", "matrix(1, 0, 0, 1, 10, 32)")]
    [InlineData("transform: rotate(90deg)", "transform", "matrix(0, 1, -1, 0, 0, 0)")]
    [InlineData("transform: scale(2) translateX(5px)", "transform", "matrix(2, 0, 0, 2, 10, 0)")]
    [InlineData("transform: rotate(calc(infinity * 1deg))", "transform", "matrix(1, 0, 0, 1, 0, 0)")]
    [InlineData("transform: perspective(none)", "transform", "matrix(1, 0, 0, 1, 0, 0)")]
    [InlineData("transform: perspective(10px)", "transform", "matrix3d(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -0.1, 0, 0, 0, 1)")]
    [InlineData("transform: rotateX(0deg) translateZ(0)", "transform", "matrix(1, 0, 0, 1, 0, 0)")]
    [InlineData("display: none; transform: translate(10px)", "transform", "matrix(1, 0, 0, 1, 10, 0)")]
    [InlineData("width: 100px; height: 50px; padding: 5px; transform: translate(10%, calc(50% + 1px))", "transform", "matrix(1, 0, 0, 1, 11, 31)")] // The percentages refer to the border box
    [InlineData("height: auto; transform: translate(10%, 50%)", "transform", "matrix(1, 0, 0, 1, 78.4, 0)")]
    [InlineData("width: 100px; height: 50px; padding: 10px; transform-box: content-box; transform: translate(10%)", "transform", "matrix(1, 0, 0, 1, 10, 0)")]
    [InlineData("width: 100px; height: 50px; padding: 10px; transform-box: border-box; transform: translate(10%)", "transform", "matrix(1, 0, 0, 1, 12, 0)")]
    [InlineData("display: inline; transform: translate(10%)", "transform", "translate(10%)")] // The inline boxes are not transformable
    [InlineData("word-spacing: normal", "word-spacing", "0px")]
    [InlineData("letter-spacing: normal", "letter-spacing", "normal")]
    [InlineData("background-image: url(img.png)", "background-image", "url(\"https://example.com/dir/img.png\")")]
    [InlineData("position: absolute; display: inline", "display", "block")]
    [InlineData("float: left; display: inline-table", "display", "table")]
    [InlineData("position: absolute; float: left", "float", "none")]
    [InlineData("display: inline flow-root", "display", "inline-block")]
    [InlineData("display: block flex", "display", "flex")]
    [InlineData("display: list-item block flow", "display", "list-item")]
    [InlineData("background-position: center top", "background-position", "50% 0%")]
    [InlineData("background-position: right 10px bottom", "background-position", "calc(100% - 10px) 100%")]
    [InlineData("object-position: top", "object-position", "50% 0%")]
    [InlineData("object-position: left 10px", "object-position", "0% 10px")]
    [InlineData("object-position: x-start", "object-position", "0% 50%")]
    [InlineData("direction: rtl; object-position: x-start 10px", "object-position", "100% 10px")] // The logical keywords are resolved against the writing mode
    [InlineData("writing-mode: vertical-rl; direction: rtl; object-position: x-end 10% y-start 20%", "object-position", "10% 80%")]
    [InlineData("writing-mode: sideways-lr; object-position: y-start", "object-position", "50% 100%")]
    [InlineData("min-width: auto", "min-width", "0px")]
    [InlineData("min-width: auto; aspect-ratio: 1", "min-width", "auto")]
    [InlineData("font-family: initial", "font-family", "serif")]
    public void ComputedValues(string style, string property, string expected)
    {
        var document = Parse("<div id=target style='" + style + "'></div>");
        document.QuerySelector("base")?.Remove();
        var baseElement = document.CreateElement("base");
        baseElement.SetAttribute("href", "https://example.com/dir/index.html");
        document.Head!.AppendChild(baseElement);
        Assert.Equal(expected, GetStyle(document, "#target").GetPropertyValue(property));
    }

    [Theory]
    [InlineData("<div id=target></div>", "display", "block")]
    [InlineData("<span id=target></span>", "display", "inline")]
    [InlineData("<script id=target></script>", "display", "none")]
    [InlineData("<ul><li id=target></li></ul>", "display", "list-item")]
    [InlineData("<table><tr><td id=target></td></tr></table>", "display", "table-cell")]
    [InlineData("<p id=target></p>", "margin-top", "16px")]
    [InlineData("<h1 id=target></h1>", "font-size", "32px")]
    [InlineData("<h1 id=target></h1>", "font-weight", "700")]
    [InlineData("<div id=target hidden></div>", "display", "none")]
    [InlineData("<a id=target href=x></a>", "color", "rgb(0, 0, 238)")]
    [InlineData("<a id=target href=x style='color-scheme: dark'></a>", "color", "rgb(158, 158, 255)")]
    [InlineData("<mark id=target style='color-scheme: dark'></mark>", "background-color", "rgb(102, 92, 0)")]
    [InlineData("<button id=target></button>", "background-color", "rgb(239, 239, 239)")]
    [InlineData("<input id=target type=submit>", "border-top-color", "rgb(118, 118, 118)")]
    [InlineData("<input id=target>", "background-color", "rgb(255, 255, 255)")]
    [InlineData("<textarea id=target style='color-scheme: dark'></textarea>", "background-color", "rgb(59, 59, 59)")]
    [InlineData("<meta name=color-scheme content=dark><textarea id=target></textarea>", "background-color", "rgb(59, 59, 59)")]
    [InlineData("<meta name=color-scheme content='not valid!'><meta name=COLOR-SCHEME content='Dark'><textarea id=target></textarea>", "background-color", "rgb(59, 59, 59)")]
    [InlineData("<meta name=color-scheme content=dark><textarea id=target style='color-scheme: light'></textarea>", "background-color", "rgb(255, 255, 255)")]
    [InlineData("<input id=target type=checkbox>", "background-color", "rgba(0, 0, 0, 0)")]
    [InlineData("<b><b id=target></b></b>", "font-weight", "900")]
    [InlineData("<p id=target dir=rtl></p>", "direction", "rtl")]
    [InlineData("<ul><li id=target></li></ul>", "text-align", "left")]
    [InlineData("<div id=target style='display: contents'></div>", "display", "contents")]
    [InlineData("<div style='display: flex'><span id=target></span></div>", "display", "block")]
    [InlineData("<div style='display: grid'><span id=target style='display: inline-flex'></span></div>", "display", "flex")]
    [InlineData("<div style='display: grid'><div style='display: contents'><span id=target></span></div></div>", "display", "block")]
    [InlineData("<input id=target style='display: contents'>", "display", "none")]
    [InlineData("<button id=target style='display: contents'></button>", "display", "contents")]
    [InlineData("<meter id=target></meter>", "appearance", "auto")]
    [InlineData("<p style='line-height: 2; font-size: 12px'><sub id=target></sub></p>", "line-height", "20px")]
    [InlineData("<p style='line-height: 30px'><sup id=target></sup></p>", "line-height", "30px")]
    public void UserAgentStyleSheet(string html, string property, string expected) => Assert.Equal(expected, Get("<!DOCTYPE html><body>" + html, property));

    [Theory]
    [InlineData("<body id=target>", "margin-top", "8px")]
    [InlineData("<body id=target marginheight=3>", "margin-top", "3px")]
    [InlineData("<body id=target bgcolor=red>", "background-color", "rgb(255, 0, 0)")]
    [InlineData("<body><img id=target width=100 height=50>", "width", "100px")]
    [InlineData("<body><img id=target width=100 height=50>", "aspect-ratio", "auto 100 / 50")]
    [InlineData("<body><font id=target color=#0f0 size=5></font>", "color", "rgb(0, 255, 0)")]
    [InlineData("<body><font id=target color=#0f0 size=5></font>", "font-size", "24px")]
    [InlineData("<body><font id=target color=chucknorris></font>", "color", "rgb(192, 0, 0)")]
    [InlineData("<body><p id=target align=center style='text-align: left'></p>", "text-align", "left")]
    [InlineData("<body><p id=target align=center></p>", "text-align", "center")]
    [InlineData("<body><ol id=target type=A></ol>", "list-style-type", "upper-alpha")]
    [InlineData("<body><svg><rect id=target fill=red stroke-width=2 width=10></rect></svg>", "fill", "rgb(255, 0, 0)")]
    [InlineData("<body><svg><rect id=target fill=red stroke-width=2 width=10></rect></svg>", "stroke-width", "2px")]
    [InlineData("<body><svg><rect id=target fill=red stroke-width=2 width=10></rect></svg>", "width", "10px")]
    [InlineData("<body><svg><rect id=target fill=red style='fill: green'></rect></svg>", "fill", "rgb(0, 128, 0)")]
    [InlineData("<body><svg><circle id=target width=10></circle></svg>", "width", "auto")]
    [InlineData("<body id=target rightmargin=3 bottommargin=3>", "margin-right", "8px")]
    [InlineData("<body id=target marginheight=3>", "margin-bottom", "3px")]
    [InlineData("<body><img id=target width=200.%>", "width", "1568px")]
    [InlineData("<body><img id=target width=0 height=1>", "aspect-ratio", "auto 0 / 1")]
    [InlineData("<body><img id=target width=10% height=1>", "aspect-ratio", "auto")]
    [InlineData("<body><canvas id=target width=10% height=20></canvas>", "aspect-ratio", "auto 10 / 20")]
    [InlineData("<body><picture><source srcset=a.png width=30 height=10><img id=target width=1 height=1></picture>", "aspect-ratio", "auto 30 / 10")]
    [InlineData("<body><picture><source srcset=a.png media='(max-width: 0px)' width=30><img id=target width=1></picture>", "width", "1px")]
    [InlineData("<body><picture><source srcset=a.png width=30><img id=target></picture>", "width", "30px")]
    [InlineData("<body><video id=target hspace=5></video>", "margin-left", "0px")]
    [InlineData("<body><input id=target type=image border=3>", "border-top-width", "3px")]
    [InlineData("<body><img id=target border=3>", "border-top-style", "solid")]
    [InlineData("<body><center id=target></center>", "text-align", "center")]
    [InlineData("<body><div id=target align=middle></div>", "text-align", "center")]
    [InlineData("<body><div id=target align=right></div>", "text-align", "right")]
    [InlineData("<body><div id=target align=justify></div>", "text-align", "justify")]
    [InlineData("<body><div id=target align=x></div>", "text-align", "start")]
    [InlineData("<body><iframe id=target frameborder=no></iframe>", "border-top-width", "0px")]
    [InlineData("<body><iframe id=target frameborder=no></iframe>", "border-top-style", "inset")]
    [InlineData("<body><iframe id=target frameborder=1></iframe>", "border-top-width", "2px")]
    [InlineData("<body><table><col id=target width=0></table>", "width", "0px")]
    [InlineData("<body><table><tbody id=target height=10 align=middle></tbody></table>", "height", "10px")]
    [InlineData("<body><table><tbody id=target height=10 align=middle></tbody></table>", "text-align", "center")]
    [InlineData("<body><table><tr><td id=target align=RIGHT></td></tr></table>", "text-align", "right")]
    [InlineData("<body><table id=target bordercolor=red></table>", "border-left-color", "rgb(255, 0, 0)")]
    [InlineData("<body><table><tr><th id=target></th></tr></table>", "text-align", "center")]
    [InlineData("<body><table><tr style='text-align: left'><th id=target></th></tr></table>", "text-align", "left")]
    [InlineData("<body><table><tr><th id=target align=left></th></tr></table>", "text-align", "left")]
    [InlineData("<body><ol><li id=target value=10xyz></li></ol>", "counter-set", "list-item 10")]
    [InlineData("<body><ol id=target start=10></ol>", "counter-reset", "list-item 9")]
    [InlineData("<body><ol id=target reversed start=20></ol>", "counter-reset", "reversed(list-item) 21")]
    [InlineData("<body><ol id=target reversed></ol>", "counter-reset", "reversed(list-item)")]
    [InlineData("<body link=red><a id=target href=a>a</a>", "color", "rgb(255, 0, 0)")]
    [InlineData("<body link=red><a id=target>a</a>", "color", "rgb(0, 0, 0)")]
    [InlineData("<body><hr id=target size=5>", "height", "3px")]
    [InlineData("<body><hr id=target size=1>", "border-bottom-width", "0px")]
    [InlineData("<body><hr id=target size=6 noshade>", "border-top-width", "3px")]
    public void PresentationalHints(string html, string property, string expected) => Assert.Equal(expected, Get("<!DOCTYPE html>" + html, property));

    [Theory]
    [InlineData("<div id=target style='color: green; outline: 2px solid'></div>", "outline-color", "rgb(0, 128, 0)")]
    [InlineData("<div id=target style='color: green; outline: auto'></div>", "outline-color", "auto")]
    [InlineData("<div id=target style='color: green; outline: auto red'></div>", "outline-color", "rgb(255, 0, 0)")]
    // A drop-down box ignores line-height in its layout (its resolved value is normal, as in the browsers), not in the lh unit
    [InlineData("<select id=target style='line-height: 30px'></select>", "line-height", "normal")]
    [InlineData("<select id=target style='line-height: 30px; appearance: none'></select>", "line-height", "30px")]
    [InlineData("<select id=target style='line-height: 30px; width: 2lh'></select>", "width", "60px")]
    public void ComputedValues_OutlineColorAndSelectLineHeight(string html, string property, string expected) => Assert.Equal(expected, Get("<!DOCTYPE html>" + html, property));

    [Theory]
    [InlineData("<input id=target placeholder=x style='color: red'>", "rgb(117, 117, 117)")]
    [InlineData("<style>::placeholder { color: inherit }</style><input id=target placeholder=x style='color: red'>", "rgb(255, 0, 0)")]
    public void Placeholder_Color(string html, string expected) => Assert.Equal(expected, Get("<!DOCTYPE html>" + html, "color", pseudoElement: "::placeholder"));

    [Theory]
    [InlineData("<details id=target><summary>s</summary></details>", "display", "block")]
    [InlineData("<details id=target><summary>s</summary></details>", "content-visibility", "hidden")]
    [InlineData("<details id=target open><summary>s</summary></details>", "content-visibility", "visible")]
    [InlineData("<style>details::details-content { display: grid; content-visibility: visible }</style><details id=target><summary>s</summary></details>", "display", "grid")]
    [InlineData("<style>details::details-content { display: grid; content-visibility: visible }</style><details id=target><summary>s</summary></details>", "content-visibility", "visible")]
    public void DetailsContent_UserAgentStyle(string html, string property, string expected) => Assert.Equal(expected, Get("<!DOCTYPE html>" + html, property, pseudoElement: "::details-content"));

    [Theory]
    [InlineData("<table><tr><td id=target nowrap width=50></td></tr></table>", "normal")]
    [InlineData("<table><tr><td id=target nowrap width=50%></td></tr></table>", "nowrap")]
    [InlineData("<table><tr><td id=target nowrap></td></tr></table>", "nowrap")]
    public void PresentationalHints_QuirksNowrap(string html, string expected) => Assert.Equal(expected, Get(html, "white-space"));

    [Theory]
    [InlineData("<path id=target></path>", "none")]
    [InlineData("<path id=target d='m 10 20 h 10'></path>", "path(\"M 10 20 H 20\")")]
    [InlineData("<path id=target d='M10,20L30,40z'></path>", "path(\"M 10 20 L 30 40 Z\")")]
    [InlineData("<path id=target d='M 10 20 L 30 40 L 50'></path>", "path(\"M 10 20 L 30 40\")")] // The path data up to the first error
    [InlineData("<path id=target d='L 10 20'></path>", "none")]
    [InlineData("<path id=target d='\"M 10 20\"'></path>", "none")]
    [InlineData("<rect id=target d='M 10 20'></rect>", "none")] // Only the path element maps the d attribute
    [InlineData("<path id=target d='M 10 20' style='d: path(\"m 1 2 v 3\")'></path>", "path(\"M 1 2 V 5\")")]
    [InlineData("<path id=target d='M 10 20' style='d: none'></path>", "none")]
    [InlineData("<path id=target d='M 10 20' style='d: path(\"\")'></path>", "none")]
    [InlineData("<g style='d: path(\"M 1 2\")'><path id=target></path></g>", "none")] // Not inherited
    [InlineData("<g style='d: path(\"M 1 2\")'><path id=target style='d: inherit'></path></g>", "path(\"M 1 2\")")]
    public void DProperty(string svg, string expected) => Assert.Equal(expected, Get("<!DOCTYPE html><body><svg>" + svg + "</svg>", "d"));

    [Theory]
    [InlineData("path(\"m 10.0 20 l 20 30   z\")", "path(\"m 10 20 l 20 30 Z\")")]
    [InlineData("  path(  'M1,2L3,4'  )", "path(\"M 1 2 L 3 4\")")]
    [InlineData("path(\"M 1 2 L\")", "")]
    [InlineData("none", "none")]
    [InlineData("path('')", "none")] // An empty path is none
    [InlineData("path('  ')", "none")]
    [InlineData("path(evenodd, '')", "")]
    public void DProperty_SpecifiedValue(string value, string expected)
    {
        var document = Parse("<!DOCTYPE html><body><svg><path id=target></path></svg>");
        var path = (SvgElement)document.GetElementById("target")!;
        path.Style.SetProperty("d", value);
        Assert.Equal(expected, path.Style.GetPropertyValue("d"));
    }

    [Theory]
    [InlineData("margin-inline-start: 5px", "margin-left", "5px")]
    [InlineData("margin-inline-start: 5px; direction: rtl", "margin-right", "5px")]
    [InlineData("margin-left: 5px", "margin-inline-start", "5px")]
    [InlineData("margin-inline-start: 5px; margin-left: 6px", "margin-left", "6px")]
    [InlineData("margin-left: 6px; margin-inline-start: 5px", "margin-left", "5px")]
    [InlineData("writing-mode: vertical-rl; inline-size: 10px", "height", "10px")]
    [InlineData("writing-mode: vertical-rl; margin-block-start: 3px", "margin-right", "3px")]
    [InlineData("inset-inline-end: 7px", "right", "7px")]
    [InlineData("border-start-end-radius: 4px", "border-top-right-radius", "4px")]
    public void LogicalProperties(string style, string property, string expected) => Assert.Equal(expected, Get("<div id=target style='" + style + "'></div>", property));

    // ----------------------------------------------------------------------------------------------------------------
    // getComputedStyle
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void GetComputedStyle_IsReadOnly()
    {
        var style = GetStyle(Parse("<div id=target></div>"), "#target");
        Assert.Equal("NoModificationAllowedError", Assert.Throws<DomException>(() => style.SetProperty("color", "red")).Name);
        Assert.Equal("NoModificationAllowedError", Assert.Throws<DomException>(() => style.CssText = "color: red").Name);
        Assert.Equal("NoModificationAllowedError", Assert.Throws<DomException>(() => style.Color = "red").Name);
        Assert.Equal("NoModificationAllowedError", Assert.Throws<DomException>(() => style.RemoveProperty("color")).Name);
        Assert.Null(style.ParentRule);
        Assert.Equal("", style.CssText);
    }

    [Fact]
    public void GetComputedStyle_ListsTheLonghandsInOrder()
    {
        var style = GetStyle(Parse("<style>@property --r { syntax: '*'; inherits: false; initial-value: a }</style><div style='--y: 2px'><div id=target style='--x: 1px'></div></div>"), "#target");
        Assert.True(style.Length > 300);
        var names = style.ToList();
        Assert.Contains("color", names);
        Assert.DoesNotContain("margin", names);
        Assert.Equal(["--r", "--x", "--y"], names.TakeLast(3));
        Assert.Equal(names.Where(n => !n.StartsWith('-', StringComparison.Ordinal)).Order(StringComparer.Ordinal), names.TakeWhile(n => !n.StartsWith('-', StringComparison.Ordinal)));
        Assert.Equal("", style.GetPropertyPriority("color"));
    }

    [Fact]
    public void GetComputedStyle_IsLive()
    {
        var document = Parse("<style>div { color: red }</style><div id=target></div>");
        var style = GetStyle(document, "#target");
        Assert.Equal("rgb(255, 0, 0)", style.Color);

        document.GetElementById("target")!.SetAttribute("style", "color: green");
        Assert.Equal("rgb(0, 128, 0)", style.Color);

        ((HtmlElement)document.GetElementById("target")!).Style.RemoveProperty("color");
        Assert.Equal("rgb(255, 0, 0)", style.Color);

        var sheet = ((HtmlStyleElement)document.QuerySelector("style")!).Sheet!;
        ((CssStyleRule)sheet.CssRules[0]).Style.SetProperty("color", "blue");
        Assert.Equal("rgb(0, 0, 255)", style.Color);

        sheet.InsertRule("#target { color: green }", 1);
        Assert.Equal("rgb(0, 128, 0)", style.Color);

        document.QuerySelector("style")!.Remove();
        Assert.Equal("rgb(0, 0, 0)", style.Color);
    }

    [Theory]
    [InlineData("<input id=control>")]
    [InlineData("<textarea id=control></textarea>")]
    [InlineData("<select id=control></select>")]
    [InlineData("<button id=control></button>")]
    public void GetComputedStyle_IsLive_CustomValidity(string control)
    {
        var document = Parse("<style>:valid { color: green } :invalid { color: red }</style><form id=form><fieldset id=fieldset>" + control + "</fieldset></form>");
        var element = document.GetElementById("control")!;
        var styles = new[] { GetStyle(document, "#control"), GetStyle(document, "#fieldset"), GetStyle(document, "#form") };
        Assert.All(styles, style => Assert.Equal("rgb(0, 128, 0)", style.Color));

        SetCustomValidity(element, "invalid");
        Assert.True(element.Matches(":invalid"));
        Assert.All(styles, style => Assert.Equal("rgb(255, 0, 0)", style.Color));

        SetCustomValidity(element, "");
        Assert.All(styles, style => Assert.Equal("rgb(0, 128, 0)", style.Color));

        static void SetCustomValidity(Element element, string error)
        {
            switch (element)
            {
                case HtmlInputElement input:
                    input.SetCustomValidity(error);
                    break;
                case HtmlTextAreaElement textArea:
                    textArea.SetCustomValidity(error);
                    break;
                case HtmlSelectElement select:
                    select.SetCustomValidity(error);
                    break;
                case HtmlButtonElement button:
                    button.SetCustomValidity(error);
                    break;
            }
        }
    }

    [Fact]
    public void GetComputedStyle_IsLive_MoveBeforeInStyleElements()
    {
        var document = Parse("<style id=a></style><style id=b></style><div id=target></div>");
        var styleA = (HtmlStyleElement)document.GetElementById("a")!;
        var styleB = (HtmlStyleElement)document.GetElementById("b")!;
        var red = document.CreateTextNode("div { color: red }");
        var blue = document.CreateTextNode("div { color: blue }");
        styleA.AppendChild(red);
        styleA.AppendChild(blue);
        var style = GetStyle(document, "#target");
        Assert.Equal("rgb(0, 0, 255)", style.Color);

        // Reorder within a style element
        styleA.MoveBefore(red, null);
        Assert.Equal("div { color: blue }div { color: red }", styleA.TextContent);
        Assert.Equal("rgb(255, 0, 0)", style.Color);

        // Move between style elements: both style blocks are updated
        styleB.MoveBefore(blue, null);
        Assert.Equal(1u, styleA.Sheet!.CssRules.Length);
        Assert.Equal(1u, styleB.Sheet!.CssRules.Length);
        Assert.Equal("rgb(0, 0, 255)", style.Color);

        styleA.MoveBefore(blue, red);
        Assert.Equal(0u, styleB.Sheet!.CssRules.Length);
        Assert.Equal("rgb(255, 0, 0)", style.Color);
    }

    [Fact]
    public void GetComputedStyle_RemovedShadowTreesAreNotRetained()
    {
        var document = Parse("<body></body>");
        var styled = CreateAndRemoveShadowHost(document, computeStyle: true);
        var control = CreateAndRemoveShadowHost(document, computeStyle: false);

        Assert.Equal("rgb(0, 0, 0)", GetStyle(document, "body").Color);
        _contexts.EventLoop.RunUntilIdle();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(control.TryGetTarget(out _));
        Assert.False(styled.TryGetTarget(out _));
        GC.KeepAlive(document);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference<Element> CreateAndRemoveShadowHost(Document document, bool computeStyle)
    {
        var host = document.CreateElement("div");
        document.Body!.AppendChild(host);
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadow.InnerHtml = "<style>span { color: green }</style><span></span>";
        if (computeStyle)
        {
            Assert.Equal("rgb(0, 128, 0)", document.DefaultView!.GetComputedStyle(shadow.QuerySelector("span")!).Color);
        }

        host.Remove();
        return new WeakReference<Element>(host);
    }

    [Fact]
    public void GetComputedStyle_IsEmptyForElementsThatAreNotRendered()
    {
        var document = Parse("<div id=target></div>");
        var window = document.DefaultView!;
        var detached = document.CreateElement("div");
        Assert.Equal(0u, window.GetComputedStyle(detached).Length);
        Assert.Equal("", window.GetComputedStyle(detached).Color);

        var withoutBrowsingContext = HtmlDocumentParser.ParseDocument("<div></div>");
        Assert.Equal(0u, window.GetComputedStyle(withoutBrowsingContext.QuerySelector("div")!).Length);
    }

    [Theory]
    [InlineData("::before", "none")]
    [InlineData(":before", "none")]
    [InlineData("::after", "\"after\"")]
    [InlineData("before", "normal")]
    public void GetComputedStyle_PseudoElements(string pseudoElement, string expected)
    {
        var document = Parse("<style>#target::after { content: 'after'; color: green }</style><div id=target></div>");
        Assert.Equal(expected, GetStyle(document, "#target", pseudoElement).Content);
    }

    [Theory]
    [InlineData("::after")]
    [InlineData("::marker")]
    public void GetComputedStyle_PseudoElementsInherit(string pseudoElement)
    {
        var document = Parse("<style>#target { color: green }</style><div id=target></div>");
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "#target", pseudoElement).Color);
    }

    [Theory]
    [InlineData("color", "rgb(0, 128, 0)")]
    [InlineData("font-size", "20px")]
    [InlineData("letter-spacing", "2px")]
    [InlineData("--x", "1")]
    [InlineData("display", "inline-block")] // display does not apply: an outside marker is a block container
    [InlineData("position", "static")]
    [InlineData("list-style-type", "square")]
    public void GetComputedStyle_MarkerSupportedProperties(string property, string expected)
    {
        var document = Parse("""
            <style>
              li { list-style-type: square }
              li::marker { color: green; font-size: 20px; letter-spacing: 2px; --x: 1; display: block; position: absolute; list-style-type: decimal }
            </style>
            <ul><li id=target>a</li></ul>
            """);
        Assert.Equal(expected, GetStyle(document, "#target", "::marker").GetPropertyValue(property));
    }

    [Theory]
    [InlineData("unicode-bidi", "isolate")]
    [InlineData("font-variant-numeric", "tabular-nums")]
    [InlineData("white-space-collapse", "preserve")]
    [InlineData("text-transform", "none")]
    public void GetComputedStyle_MarkerDefaultStyles(string property, string expected)
    {
        var document = Parse("<style>li { text-transform: lowercase }</style><ul><li id=target>a</li></ul>");
        Assert.Equal(expected, GetStyle(document, "#target", "::marker").GetPropertyValue(property));
    }

    [Theory]
    [InlineData("::first-letter", "color", "rgb(0, 128, 0)")]
    [InlineData("::first-letter", "margin-top", "1px")]
    [InlineData("::first-letter", "display", "block")]
    [InlineData("::first-letter", "position", "static")]
    [InlineData("::first-letter", "word-break", "keep-all")]
    [InlineData("::first-letter", "--x", "1")]
    [InlineData("::first-line", "color", "rgb(0, 128, 0)")]
    [InlineData("::first-line", "margin-top", "0px")]
    [InlineData("::first-line", "display", "inline")]
    [InlineData("::first-line", "float", "none")]
    [InlineData("::first-line", "word-break", "keep-all")]
    public void GetComputedStyle_FirstLetterAndFirstLineSupportedProperties(string pseudoElement, string property, string expected)
    {
        var document = Parse("""
            <style>
              #target { word-break: keep-all }
              #target::first-letter, #target::first-line { color: green; margin-top: 1px; display: flex; float: left; position: absolute; word-break: break-all; --x: 1 }
            </style>
            <div id=target>a</div>
            """);
        Assert.Equal(expected, GetStyle(document, "#target", pseudoElement).GetPropertyValue(property));
    }

    [Theory]
    [InlineData("<div id=target style='display: math'></div>", "inline")]
    [InlineData("<div id=target style='display: block math'></div>", "block")]
    [InlineData("<math id=target style='display: block math'></math>", "block math")]
    [InlineData("<textarea id=target></textarea>", "inline-block")]
    public void GetComputedStyle_Display(string html, string expected)
    {
        var document = Parse(html);
        Assert.Equal(expected, GetStyle(document, "#target").Display);
    }

    [Theory]
    [InlineData("::slotted(div)")]
    [InlineData("::part(foo)")]
    [InlineData("::unknown")]
    [InlineData(":marker")]
    [InlineData("::before(test)")]
    [InlineData("::before ")]
    [InlineData("::picker(div)")]
    public void GetComputedStyle_InvalidPseudoElementsAreEmpty(string pseudoElement)
    {
        Assert.Equal(0u, GetStyle(Parse("<div id=target></div>"), "#target", pseudoElement).Length);
    }

    [Fact]
    public void GetComputedStyle_Iframe()
    {
        var document = Parse("<iframe width=200 height=100 srcdoc='<style>@media (max-width: 250px) { div { color: green } }</style><div></div>'></iframe>");
        _contexts.EventLoop.RunUntilIdle();
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        var frameDocument = iframe.ContentDocument!;
        Assert.Equal("rgb(0, 128, 0)", frameDocument.DefaultView!.GetComputedStyle(frameDocument.QuerySelector("div")!).Color);
    }

    [Fact]
    public void StyleElement_MediaAttributeChangeUpdatesTheStyleSheet()
    {
        var document = Parse("<style media=print>#target { color: green }</style><div id=target></div>");
        var style = (HtmlStyleElement)document.QuerySelector("style")!;
        var target = document.GetElementById("target")!;
        Assert.Equal("rgb(0, 0, 0)", document.DefaultView!.GetComputedStyle(target).Color);

        style.SetAttribute("media", "screen");
        Assert.Equal("screen", style.Sheet!.Media.MediaText);
        Assert.Equal("rgb(0, 128, 0)", document.DefaultView.GetComputedStyle(target).Color);
    }

    // https://drafts.csswg.org/css-viewport/#zoom-property: a number; the legacy normal keyword and 0 compute to 1
    [Theory]
    [InlineData("normal", "1")]
    [InlineData("0.75", "0.75")]
    [InlineData("150%", "1.5")]
    [InlineData("calc(100% - 50%)", "0.5")]
    [InlineData("calc(1 + (sign(1em - 30px) * 0.5))", "0.5")]
    [InlineData("0", "1")]
    [InlineData("0%", "1")]
    public void Zoom_ComputedValueIsANumber(string value, string expected) => Assert.Equal(expected, Get($"<div id=target style='zoom: {value}'></div>", "zoom"));

    // ----------------------------------------------------------------------------------------------------------------
    // MathML
    // ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("<html dir=rtl><math id=target></math>", "direction", "ltr")]
    [InlineData("<html dir=rtl><math id=target dir=invalid></math>", "direction", "ltr")]
    [InlineData("<math id=target dir=RTL></math>", "direction", "rtl")]
    [InlineData("<math id=target></math>", "display", "math")]
    [InlineData("<math id=target display=BLOCK></math>", "display", "block math")]
    [InlineData("<math><mrow id=target></mrow></math>", "display", "block math")]
    [InlineData("<math><mtable id=target></mtable></math>", "display", "inline-table")]
    [InlineData("<math><mfrac id=target></mfrac></math>", "padding-inline-start", "1px")]
    [InlineData("<math><merror id=target></merror></math>", "border-top-color", "rgb(255, 0, 0)")]
    [InlineData("<math><mphantom id=target></mphantom></math>", "visibility", "hidden")]
    [InlineData("<math><semantics><mi></mi><mi id=target></mi></semantics></math>", "display", "none")]
    [InlineData("<math><mi id=target></mi></math>", "text-transform", "math-auto")]
    [InlineData("<math><mi id=target mathvariant=Normal></mi></math>", "text-transform", "none")]
    [InlineData("<math><mrow id=target style='display: contents'></mrow></math>", "display", "none")]
    [InlineData("<div id=target style='display: contents'></div>", "display", "contents")]
    public void MathML_UserAgentStyleSheet(string html, string property, string expected) => Assert.Equal(expected, Get(html, property));

    [Theory]
    [InlineData("<math><mrow id=target mathcolor=red></mrow></math>", "color", "rgb(255, 0, 0)")]
    [InlineData("<math><mrow id=target mathcolor=inherit></mrow></math>", "color", "rgb(0, 0, 0)")]
    [InlineData("<math><mrow id=target mathbackground=#00ff00></mrow></math>", "background-color", "rgb(0, 255, 0)")]
    [InlineData("<math style='font-size: 10px'><mrow id=target mathsize=200%></mrow></math>", "font-size", "20px")]
    [InlineData("<math><mrow id=target mathsize=small></mrow></math>", "font-size", "16px")]
    [InlineData("<math><mrow id=target displaystyle=TRUE></mrow></math>", "math-style", "normal")]
    [InlineData("<math display=block><mrow id=target displaystyle=false></mrow></math>", "math-style", "compact")]
    [InlineData("<math><mrow id=target scriptlevel=3></mrow></math>", "math-depth", "3")]
    [InlineData("<math><mrow scriptlevel=3><mrow id=target scriptlevel=+2></mrow></mrow></math>", "math-depth", "5")]
    [InlineData("<math><mrow scriptlevel=3><mrow id=target scriptlevel=-1></mrow></mrow></math>", "math-depth", "2")]
    [InlineData("<math><mrow id=target scriptlevel=1.5></mrow></math>", "math-depth", "0")]
    [InlineData("<math><mspace id=target width=10px></mspace></math>", "width", "10px")]
    [InlineData("<math><mspace id=target height=5px depth=3px></mspace></math>", "height", "8px")]
    [InlineData("<math><mspace id=target depth=3px></mspace></math>", "height", "3px")]
    public void MathML_PresentationalHints(string html, string property, string expected) => Assert.Equal(expected, Get(html, property));

    [Theory]
    [InlineData("<math><msup><mi></mi><mi id=target></mi></msup></math>", "math-depth", "1")]
    [InlineData("<math><mfrac><mi id=target></mi><mi></mi></mfrac></math>", "math-depth", "1")]
    [InlineData("<math display=block><mfrac><mi id=target></mi><mi></mi></mfrac></math>", "math-depth", "0")]
    [InlineData("<math><mroot><mi></mi><mi id=target></mi></mroot></math>", "math-depth", "2")]
    // The math font (STIX Two Math) has a scriptPercentScaleDown of 70 and a scriptScriptPercentScaleDown of 55
    [InlineData("<math style='font-size: 100px'><msup><mi></mi><mi id=target></mi></msup></math>", "font-size", "70px")]
    [InlineData("<math style='font-size: 100px'><mrow scriptlevel=2><mrow id=target scriptlevel=1></mrow></mrow></math>", "font-size", "70px")]
    [InlineData("<math style='font-size: 100px'><mrow id=target scriptlevel=2></mrow></math>", "font-size", "55px")]
    [InlineData("<math style='font-size: 100px; font-family: Ahem'><msup><mi></mi><mi id=target></mi></msup></math>", "font-size", "71px")]
    [InlineData("<math style='font-size: 100px'><mover accent=true><mi></mi><mi id=target></mi></mover></math>", "font-size", "100px")]
    public void MathML_MathDepth(string html, string property, string expected) => Assert.Equal(expected, Get(html, property));

    // ----------------------------------------------------------------------------------------------------------------
    // Specified and computed values of the properties with specific rules
    // ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("display: flow list-item", "display", "list-item", "list-item")]
    [InlineData("display: list-item flow-root block", "display", "flow-root list-item", "flow-root list-item")]
    [InlineData("display: inline flow-root", "display", "inline-block", "inline-block")]
    [InlineData("display: -webkit-flex", "display", "flex", "flex")]
    [InlineData("display: inline grid-lanes; float: left", "display", "inline grid-lanes", "grid-lanes")]
    [InlineData("display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 3", "display", "-webkit-box", "flow-root")]
    [InlineData("display: -webkit-inline-box; -webkit-box-orient: horizontal; line-clamp: 3", "display", "-webkit-inline-box", "-webkit-inline-box")]
    [InlineData("line-clamp: ellipsis 11", "line-clamp", "11", "11")]
    [InlineData("line-clamp: no-ellipsis -webkit-legacy", "line-clamp", "auto no-ellipsis -webkit-legacy", "auto no-ellipsis -webkit-legacy")]
    [InlineData("line-clamp: 2", "continue", "collapse", "collapse")]
    [InlineData("-webkit-line-clamp: 2", "-webkit-line-clamp", "2", "2")]
    [InlineData("-webkit-line-clamp: 2; continue: discard", "-webkit-line-clamp", "", "")]
    [InlineData("-webkit-box-align: center", "-webkit-box-align", "center", "center")]
    [InlineData("-webkit-box-flex: calc(1.5 * 2)", "-webkit-box-flex", "calc(3)", "3")]
    [InlineData("-webkit-box-ordinal-group: calc(2.6)", "-webkit-box-ordinal-group", "calc(2.6)", "3")]
    [InlineData("-webkit-box-ordinal-group: 0", "-webkit-box-ordinal-group", "", "1")]
    [InlineData("-webkit-box-pack: justify", "-webkit-box-pack", "justify", "justify")]
    [InlineData("-webkit-box-direction: reverse", "-webkit-box-direction", "reverse", "reverse")]
    [InlineData("background-image: -webkit-linear-gradient(top left, red 10%, blue)", "background-image", "-webkit-linear-gradient(top left, red 10%, blue)", "-webkit-linear-gradient(top left, rgb(255, 0, 0) 10%, rgb(0, 0, 255))")]
    [InlineData("background-image: -webkit-gradient(linear, left top, left bottom, from(red), to(blue))", "background-image", "-webkit-gradient(linear, left top, left bottom, from(red), to(blue))", "-webkit-gradient(linear, left top, left bottom, from(rgb(255, 0, 0)), to(rgb(0, 0, 255)))")]
    [InlineData("overflow-clip-margin: 10px content-box", "overflow-clip-margin", "content-box 10px", "content-box 10px")]
    [InlineData("overflow-clip-margin: padding-box calc(0.5em + 100px)", "overflow-clip-margin", "calc(0.5em + 100px)", "108px")]
    [InlineData("overflow-clip-margin: border-box calc(10px - 10px)", "overflow-clip-margin", "border-box calc(0px)", "border-box")]
    [InlineData("overflow: auto visible", "overflow", "auto visible", "auto")]
    [InlineData("overflow: clip scroll", "overflow", "clip scroll", "clip scroll")]
    [InlineData("place-content: first baseline", "place-content", "baseline start", "baseline start")]
    [InlineData("place-items: center center", "place-items", "center", "center")]
    [InlineData("place-items: stretch right legacy", "justify-items", "legacy right", "legacy right")]
    [InlineData("justify-items: legacy", "justify-items", "legacy", "normal")]
    [InlineData("place-self: safe flow-start", "place-self", "safe flow-start", "safe flow-start")]
    [InlineData("gap: 10px 10px", "gap", "10px", "10px")]
    [InlineData("filter: blur(0) grayscale(300%) drop-shadow(1px 2px red)", "filter", "blur(0px) grayscale(100%) drop-shadow(red 1px 2px)", "blur(0px) grayscale(1) drop-shadow(rgb(255, 0, 0) 1px 2px 0px)")]
    [InlineData("backdrop-filter: brightness() hue-rotate(0)", "backdrop-filter", "brightness() hue-rotate(0deg)", "brightness(1) hue-rotate(0deg)")]
    [InlineData("filter: blur(-1px)", "filter", "", "none")]
    [InlineData("background-image: radial-gradient(farthest-corner at center, red, blue)", "background-image", "radial-gradient(red, blue)", "radial-gradient(rgb(255, 0, 0), rgb(0, 0, 255))")]
    [InlineData("background-image: conic-gradient(in lab at left 10px top 50%, red, blue)", "background-image", "conic-gradient(at left 10px top 50% in lab, red, blue)", "conic-gradient(at 10px 50% in lab, rgb(255, 0, 0), rgb(0, 0, 255))")]
    [InlineData("background-image: radial-gradient(circle 10px at bottom 10% right 20%, red, blue)", "background-image", "radial-gradient(circle 10px at right 20% bottom 10%, red, blue)", "radial-gradient(10px at 80% 90%, rgb(255, 0, 0), rgb(0, 0, 255))")]
    [InlineData("background-position: right 11% bottom", "background-position", "right 11% bottom", "89% 100%")]
    [InlineData("white-space: wrap preserve", "white-space", "pre-wrap", "pre-wrap")]
    [InlineData("box-shadow: inset 4px -4px 0 green", "box-shadow", "green 4px -4px 0px inset", "rgb(0, 128, 0) 4px -4px 0px 0px inset")]
    [InlineData("border-radius: 1px 1px 1px 1px / 1px 1px 2% 1px", "border-radius", "1px / 1px 1px 2%", "1px / 1px 1px 2%")]
    [InlineData("caret-color: auto auto", "caret-color", "", "rgb(0, 0, 0)")]
    [InlineData("outline: 4px", "outline", "4px", "4px none rgb(0, 0, 0)")]
    [InlineData("outline: 4px auto", "outline", "4px auto", "4px auto auto")]
    [InlineData("border-image: 1 2% 3 4% / / 2", "border-image", "1 2% 3 4% / / 2", "1 2% 3 4% / / 2")]
    [InlineData("border-image: none space space", "border-image", "space", "space")]
    [InlineData("white-space: nowrap preserve-breaks", "white-space", "preserve-breaks nowrap", "preserve-breaks nowrap")]
    [InlineData("vertical-align: super middle first", "vertical-align", "first middle super", "first middle super")]
    [InlineData("vertical-align: 0px baseline", "vertical-align", "baseline", "baseline")]
    [InlineData("text-decoration: solid", "text-decoration", "none", "none")]
    [InlineData("text-emphasis-style: open", "text-emphasis-style", "open", "open circle")]
    [InlineData("text-emphasis-position: right under", "text-emphasis-position", "under", "under")]
    [InlineData("background-position: top 17px right -18px", "background-position", "right -18px top 17px", "calc(100% + 18px) 17px")]
    [InlineData("gap: calc(10px - 1em) 1em", "gap", "calc(-1em + 10px) 1em", "0px 16px")]
    [InlineData("grid-template-columns: [a] repeat(2, [] 1em [b]) 10%", "grid-template-columns", "[a] repeat(2, 1em [b]) 10%", "[a] repeat(2, 16px [b]) 10%")]
    [InlineData("grid-template-rows: 1px repeat(auto-fill, min-content) 3px", "grid-template-rows", "1px repeat(auto-fill, min-content) 3px", "none")]
    [InlineData("display: grid-lanes; grid-template-rows: repeat(auto-fit, auto)", "grid-template-rows", "repeat(auto-fit, auto)", "0px")]
    [InlineData("display: grid; width: 100px; column-gap: 10px; grid-template-columns: [a] 20px repeat(auto-fill, [b] 10%) [c]", "grid-template-columns", "[a] 20px repeat(auto-fill, [b] 10%) [c]", "[a] 20px [b] 10px [b] 10px [b] 10px [b] 10px [c]")]
    [InlineData("display: grid; width: 100px; grid-template-columns: repeat(auto-fit, [a] 30px [b]) repeat(2, 1em)", "grid-template-columns", "repeat(auto-fit, [a] 30px [b]) repeat(2, 1em)", "[a] 0px [b a] 0px [b] 16px 16px")]
    [InlineData("display: grid; width: 100px; grid-template-columns: 1fr repeat(2, 20px)", "grid-template-columns", "1fr repeat(2, 20px)", "60px 20px 20px")]
    [InlineData("transition-timing-function: step-end, steps(2, jump-end), STEP-START", "transition-timing-function", "steps(1), steps(2), steps(1, start)", "steps(1), steps(2), steps(1, start)")]
    [InlineData("animation-timing-function: steps(calc(2 * sign(1em - 1000px)), jump-none)", "animation-timing-function", "steps(calc(2 * sign(1em - 1000px)), jump-none)", "steps(2, jump-none)")]
    [InlineData("animation-timing-function: steps(1, jump-none)", "animation-timing-function", "", "ease")]
    [InlineData("animation-timing-function: steps(calc(0 / 0))", "animation-timing-function", "", "ease")]
    [InlineData("transition-timing-function: cubic-bezier(calc(-1), -2, calc(3 / 2), 2)", "transition-timing-function", "cubic-bezier(calc(-1), -2, calc(1.5), 2)", "cubic-bezier(0, -2, 1, 2)")]
    [InlineData("animation-timing-function: linear(0, 0.5 25% 75%, 1), linear(0 50%, 1 10%, 0, 1)", "animation-timing-function", "linear(0, 0.5 25% 75%, 1), linear(0 50%, 1 10%, 0, 1)", "linear(0 0%, 0.5 25%, 0.5 75%, 1 100%), linear(0 50%, 1 50%, 0 75%, 1 100%)")]
    [InlineData("animation-name: \"foo bar\", \"none\"", "animation-name", "foo\\ bar, \"none\"", "foo\\ bar, \"none\"")]
    [InlineData("animation-range: entry 0% exit 100%", "animation-range", "entry exit", "entry exit")]
    [InlineData("animation-range: cover 10% 1em", "animation-range", "cover 10% 1em", "cover 10% 16px")]
    [InlineData("animation-range-end: COVER 100%", "animation-range-end", "cover", "cover")]
    [InlineData("animation: anim paused 2 1s -3s ease-in", "animation", "1s ease-in -3s 2 paused anim", "1s ease-in -3s 2 paused anim")]
    [InlineData("animation: none, \"reverse\" 1s", "animation", "none, 1s \"reverse\"", "none, 1s \"reverse\"")]
    [InlineData("animation: none; animation-delay: 1s", "animation", "auto 1s", "0s 1s")]
    [InlineData("animation: 1s; animation-timeline: --t", "animation", "", "")]
    [InlineData("animation-duration: auto", "animation-duration", "auto", "0s")]
    [InlineData("transition: allow-discrete display 3s, normal opacity", "transition", "display 3s allow-discrete, opacity", "display 3s allow-discrete, opacity")]
    [InlineData("transition: 0s; transition-delay: 1s", "transition", "0s 1s", "0s 1s")]
    [InlineData("clip-path: border-box circle(closest-side at top right)", "clip-path", "circle(at right top)", "circle(at 100% 0%)")]
    [InlineData("clip-path: circle(-1px)", "clip-path", "", "none")]
    [InlineData("clip-path: ellipse(calc(10px - 1em) 2em at bottom 10% right 20px)", "clip-path", "ellipse(calc(-1em + 10px) 2em at right 20px bottom 10%)", "ellipse(0px 32px at calc(100% - 20px) 90%)")]
    [InlineData("clip-path: inset(0 1px 0 1px round 0 0)", "clip-path", "inset(0px 1px)", "inset(0px 1px)")]
    [InlineData("clip-path: xywh(0 1% 2px 1em round 0 1px)", "clip-path", "xywh(0px 1% 2px 1em round 0px 1px)", "inset(1% calc(100% - 2px) calc(99% - 16px) 0px round 0px 1px)")]
    [InlineData("clip-path: rect(auto 1% auto calc(100% - 2px)) content-box", "clip-path", "rect(auto 1% auto calc(100% - 2px)) content-box", "inset(0% 99% 0% calc(100% - 2px)) content-box")]
    [InlineData("clip-path: polygon(nonzero round 0, 0 1px)", "clip-path", "polygon(0px 1px)", "polygon(0px 1px)")]
    [InlineData("shape-outside: circle() margin-box", "shape-outside", "circle()", "circle()")]
    [InlineData("offset-path: ray(at left 0deg closest-side contain)", "offset-path", "ray(0deg contain at left center)", "ray(0deg contain at 0% 50%)")]
    [InlineData("offset-path: shape(nonzero from 0 0, arc to center 1em of 1px small ccw rotate 0deg)", "offset-path", "shape(from 0px 0px, arc to center 1em of 1px)", "shape(from 0px 0px, arc to 50% 16px of 1px)")]
    [InlineData("offset-position: right 10% bottom 1em", "offset-position", "right 10% bottom 1em", "90% calc(100% - 16px)")]
    [InlineData("offset-rotate: 0deg reverse", "offset-rotate", "reverse", "auto 180deg")]
    [InlineData("mask-position: top, 10%", "mask-position", "center top, 10% center", "50% 0%, 10% 50%")]
    [InlineData("object-position: y-end", "object-position", "center y-end", "50% 100%")]
    [InlineData("mask-size: auto auto, 1px, calc(1px - 1em) 1px", "mask-size", "auto, 1px auto, calc(-1em + 1px) 1px", "auto, 1px auto, 0px 1px")]
    [InlineData("mask: no-clip content-box url(a.png) top", "mask-clip", "no-clip", "no-clip")]
    [InlineData("mask: no-clip content-box url(a.png) top", "mask-origin", "content-box", "content-box")]
    [InlineData("mask: no-clip content-box url(a.png) top", "mask-position", "center top", "50% 0%")]
    [InlineData("mask: border-box padding-box", "mask", "border-box padding-box", "border-box padding-box")]
    [InlineData("mask: alpha, border-box no-clip", "mask", "alpha, no-clip", "alpha, no-clip")]
    [InlineData("mask: none", "mask-border-repeat", "stretch", "stretch")]
    [InlineData("width: calc-size(any, 50px + 30%)", "width", "calc-size(any, 30% + 50px)", "285.2px")] // The resolved value is the used value
    [InlineData("height: calc-size(min-content, size * 2)", "height", "calc-size(min-content, 2 * size)", "0px")]
    [InlineData("max-width: calc-size(auto, size)", "max-width", "", "none")]
    [InlineData("width: random(fixed 0.5, 0px, 100px, 3px)", "width", "calc(51px)", "51px")]
    [InlineData("width: random(fixed random(1, 2), 10px, 100px)", "width", "random(fixed random(element-scoped ua-width-2, 1, 2), 10px, 100px)", "99.99999px")] // The base value is clamped below 1
    [InlineData("translate: random(fixed random(-2, -1), 10%, 100%)", "translate", "random(fixed random(element-scoped ua-translate-2, -2, -1), 10%, 100%)", "random(fixed 0, 10%, 100%)")]
    [InlineData("color: rgb(from blue random(g + 51, g) random(b, b) 0)", "color", "rgb(from blue random(element-scoped ua-color-1, 51 + g, g) random(element-scoped ua-color-2, b, b) 0)", "color(srgb 0.2 1 0)")] // The channel keywords are not caching keys
    [InlineData("width: calc(4em + 2.6458333cm)", "width", "calc(4em + 99.999999px)", "164px")] // The lengths have the precision of the browsers (single-precision floats)
    [InlineData("opacity: progress(50px, 0px, 200px)", "opacity", "calc(0.25)", "0.25")]
    [InlineData("margin-left: clamp(none, 2px, 3em)", "margin-left", "min(2px, 3em)", "2px")]
    [InlineData("scale: calc-mix(1 25%, 3 25%, 5, 7)", "scale", "calc(4)", "4")]
    [InlineData("z-index: sibling-index()", "z-index", "sibling-index()", "1")]
    [InlineData("width: calc(NaN * 1px)", "width", "calc(NaN * 1px)", "0px")]
    [InlineData("width: min(1% + 1px)", "width", "calc(1% + 1px)", "8.84px")]
    [InlineData("font-weight: sign(10%)", "font-weight", "", "400")]
    [InlineData("color: random-item(fixed 0.5, red, green, blue)", "color", "random-item(fixed 0.5, red, green, blue)", "rgb(0, 128, 0)")]
    [InlineData("--x: 3; --y: if(style(--x: 3): yes; else: no)", "--y", "if(style(--x: 3): yes; else: no)", "yes")]
    [InlineData("background-image: url(\"a.png\" referrer-policy(no-referrer) cross-origin(anonymous))", "background-image", "url(\"a.png\" cross-origin(anonymous) referrer-policy(no-referrer))", "url(\"a.png\" cross-origin(anonymous) referrer-policy(no-referrer))")]
    [InlineData("clip: rect(1px 1em auto 2px)", "clip", "rect(1px, 1em, auto, 2px)", "rect(1px, 16px, auto, 2px)")]
    [InlineData("background-image: image-set(\"https://a.test/a.png\", url(https://a.test/b.png) 2x type(\"image/png\"))", "background-image", "image-set(url(\"https://a.test/a.png\") 1x, url(\"https://a.test/b.png\") 2x type(\"image/png\"))", "image-set(url(\"https://a.test/a.png\") 1dppx, url(\"https://a.test/b.png\") 2dppx type(\"image/png\"))")]
    [InlineData("position-visibility: no-overflow anchors-valid", "position-visibility", "anchor-valid no-overflow", "anchor-valid no-overflow")]
    [InlineData("position-try-fallbacks: start start, flip-block --foo", "position-try-fallbacks", "start, --foo flip-block", "start, --foo flip-block")]
    [InlineData("counter-reset: a b 2 reversed(c)", "counter-reset", "a 0 b 2 reversed(c)", "a 0 b 2 reversed(c)")]
    [InlineData("counter-increment: a", "counter-increment", "a 1", "a 1")]
    [InlineData("list-style: none inside", "list-style", "inside none", "inside none")]
    [InlineData("list-style: outside outside", "list-style", "outside outside", "outside outside")]
    [InlineData("list-style-type: symbols(symbolic \"a\")", "list-style-type", "symbols(\"a\")", "symbols(\"a\")")]
    [InlineData("background-image: linear-gradient(red 10% 20%, blue)", "background-image", "linear-gradient(red 10% 20%, blue)", "linear-gradient(rgb(255, 0, 0) 10%, rgb(255, 0, 0) 20%, rgb(0, 0, 255))")]
    [InlineData("columns: auto 2 / auto", "columns", "2", "2")]
    [InlineData("border-block: 1px dotted red", "border-block", "1px dotted red", "1px dotted rgb(255, 0, 0)")]
    [InlineData("position: absolute; padding-block-start: calc(10% - 20px)", "padding-block-start", "calc(10% - 20px)", "60px")]
    [InlineData("position: absolute; margin-inline-end: 10%", "margin-inline-end", "10%", "80px")]
    [InlineData("color-scheme: only light purple", "color-scheme", "light purple only", "light purple only")]
    [InlineData("color-scheme: light normal", "color-scheme", "", "normal")]
    [InlineData("scroll-snap-type: inline proximity", "scroll-snap-type", "inline", "inline")]
    [InlineData("scroll-snap-align: end end", "scroll-snap-align", "end", "end")]
    [InlineData("font-synthesis: position oblique-only", "font-synthesis", "oblique-only position", "oblique-only position")]
    [InlineData("font-synthesis: position oblique-only", "font-synthesis-style", "oblique-only", "oblique-only")]
    [InlineData("font-family: cursive serif", "font-family", "", "serif")]
    [InlineData("font-language-override: \"turkish\"", "font-language-override", "", "normal")]
    [InlineData("reading-order: calc(34.1)", "reading-order", "calc(34.1)", "34")]
    [InlineData("text-justify: distribute", "text-justify", "inter-character", "inter-character")]
    [InlineData("text-justify: no-compress distribute", "text-justify", "inter-character no-compress", "inter-character no-compress")]
    [InlineData("border-top: hairline solid", "border-top-width", "hairline", "1px")]
    [InlineData("page-break-before: always", "break-before", "page", "page")]
    [InlineData("page-break-before: always", "page-break-before", "always", "always")]
    [InlineData("break-after: column", "page-break-after", "", "")]
    [InlineData("page-break-inside: avoid", "break-inside", "avoid", "avoid")]
    [InlineData("font-kerning: none; font: 16px serif", "font-kerning", "auto", "auto")]
    [InlineData("font: 16px serif; font-kerning: none", "font", "", "")]
    [InlineData("--x: 1", "text-emphasis-position", "", "auto")]
    [InlineData("background-size: 1px", "background-size", "1px auto", "1px auto")]
    [InlineData("background-size: auto auto", "background-size", "auto", "auto")]
    [InlineData("background-size: calc(10px - 1em) 2px", "background-size", "calc(-1em + 10px) 2px", "0px 2px")]
    [InlineData("background: text border-area", "background-clip", "border-area text", "border-area text")]
    [InlineData("background: border-area", "background-origin", "border-box", "border-box")]
    [InlineData("background-image: conic-gradient(from 2rad, red, blue)", "background-image", "conic-gradient(from 2rad, red, blue)", "conic-gradient(from 114.592deg, rgb(255, 0, 0), rgb(0, 0, 255))")]
    [InlineData("background-image: cross-fade(1% red, green)", "background-image", "cross-fade(red 1%, green)", "cross-fade(rgb(255, 0, 0) 1%, rgb(0, 128, 0))")]
    [InlineData("border-image-slice: random(fixed 0.5, 10%, 30%)", "border-image-slice", "random(fixed 0.5, 10%, 30%)", "random(fixed 0.5, 10%, 30%)")]
    [InlineData("border-image-slice: calc(10% + 20%)", "border-image-slice", "calc(30%)", "30%")]
    [InlineData("background-image: radial-gradient(at 30% random(fixed 0.5, 33%, 1%), silver random(fixed 0.5, 30%, 10%, 1%), gold random(fixed 0.5, 70%, 10%, 1%))", "background-image", "radial-gradient(at 30% random(fixed 0.5, 33%, 1%), silver random(fixed 0.5, 30%, 10%, 1%), gold random(fixed 0.5, 70%, 10%, 1%))", "radial-gradient(at 30% random(fixed 0.5, 33%, 1%), rgb(192, 192, 192) 30%, rgb(255, 215, 0) 70%)")]
    public void SpecifiedAndComputedValues(string style, string property, string specified, string computed)
    {
        var document = Parse("<div id=target style='" + style + "'></div>");
        var element = (HtmlElement)document.GetElementById("target")!;
        Assert.Equal(specified, element.Style.GetPropertyValue(property));
        Assert.Equal(computed, GetStyle(document, "#target").GetPropertyValue(property));
    }

    // https://drafts.csswg.org/css-backgrounds-4/#background: border-area and text imply background-origin: border-box
    [Theory]
    [InlineData("background: url(a.png) 0% 0% / 10px", "url(\"a.png\") 0% 0% / 10px auto")]
    [InlineData("background: border-area border-box", "border-area")]
    [InlineData("background: border-area padding-box", "padding-box border-area")]
    [InlineData("background: text border-area", "border-area text")]
    public void SpecifiedBackground(string style, string expected)
    {
        var document = Parse("<div id=target style='" + style + "'></div>");
        Assert.Equal(expected, ((HtmlElement)document.GetElementById("target")!).Style.GetPropertyValue("background"));
    }

    [Theory]
    [InlineData("<pre id=target>x</pre>", "13px")]
    [InlineData("<code id=target>x</code>", "13px")]
    [InlineData("<span id=target style='font-family: monospace, monospace'>x</span>", "16px")]
    [InlineData("<span id=target style='font-family: monospace, serif'>x</span>", "16px")]
    [InlineData("<div style='font-family: monospace'><div id=target style='font-size: 2em'>x</div></div>", "26px")]
    [InlineData("<div style='font-family: monospace'><div style='font-size: 2em'><span id=target style='font-family: serif'>x</span></div></div>", "32px")]
    [InlineData("<div style='font-size: 2em'><code id=target>x</code></div>", "26px")]
    [InlineData("<div style='font-size: 20px'><code id=target>x</code></div>", "20px")]
    [InlineData("<div style='font-size: 20px'><span style='font-size: 150%'><code id=target>x</code></span></div>", "30px")]
    [InlineData("<div style='font-size: large'><code id=target>x</code></div>", "16px")]
    [InlineData("<code id=target style='font-size: x-large'>x</code>", "20px")]
    [InlineData("<div style='font-family: monospace; font-size: 1.5em'><div id=target style='font-family: serif; font-size: 1rem'>x</div></div>", "16px")]
    [InlineData("<code><span id=target style='font-family: serif'>x</span></code>", "16px")]
    public void FontSize_DefaultSizeOfTheMonospaceFamily(string html, string expected)
    {
        // The default font size of the monospace generic family is 13px (as in the browsers): the keywords and the sizes relative
        // to the parent follow it, the absolute lengths do not
        Assert.Equal(expected, Get(html, "font-size"));
    }

    [Theory]
    [InlineData("<table cellpadding=7><tr><td id=target>x</td></tr></table>")]
    [InlineData("<table cellpadding=7><tr><td id=target class=c>x</td></tr></table>")]
    [InlineData("<table cellpadding=7><tr><th id=target>x</th></tr></table>")]
    public void CellPadding_AppliesToTheCellsWithAndWithoutAttributes(string html)
    {
        Assert.Equal("7px", Get(html, "padding-left"));
    }

    [Theory]
    // The table and its cells have a border when the border attribute is not zero (1px when it cannot be parsed)
    [InlineData("<table border=1><tr><td id=target>x</td></tr></table>", "1px", "inset")]
    [InlineData("<table border=5><tbody><tr><th id=target>x</th></tr></tbody></table>", "1px", "inset")]
    [InlineData("<table border><tr><td id=target>x</td></tr></table>", "1px", "inset")]
    [InlineData("<table border=x><tr><td id=target>x</td></tr></table>", "1px", "inset")]
    [InlineData("<table border=5 id=target><tr><td>x</td></tr></table>", "5px", "outset")]
    [InlineData("<table border=x id=target><tr><td>x</td></tr></table>", "1px", "outset")]
    // https://html.spec.whatwg.org/multipage/rendering.html#magic-border-selector: "only if border is not equivalent to zero"
    [InlineData("<table border=0><tr><td id=target>x</td></tr></table>", "0px", "none")]
    [InlineData("<table border=' +00px'><tr><td id=target>x</td></tr></table>", "0px", "none")]
    [InlineData("<table border=0><thead><tr><th id=target>x</th></tr></thead></table>", "0px", "none")]
    [InlineData("<table border=0 id=target><tr><td>x</td></tr></table>", "0px", "none")]
    // The rules of the other attributes still apply
    [InlineData("<table border=0 rules=all><tr><td id=target>x</td></tr></table>", "1px", "solid")]
    [InlineData("<table border=0 frame=box id=target><tr><td>x</td></tr></table>", "0px", "outset")]
    public void TableBorder_AppliesToTheTableAndItsCellsUnlessItIsZero(string html, string expectedWidth, string expectedStyle)
    {
        var style = GetStyle(Parse(html), "#target");
        Assert.Equal(expectedStyle, style.GetPropertyValue("border-top-style"));
        Assert.Equal(expectedWidth, style.GetPropertyValue("border-top-width"));
    }

    [Fact]
    public void TableBorder_TheCellsFollowTheChangesOfTheAttribute()
    {
        var document = Parse("<table border=1><tr><td id=target>x</td></tr></table>");
        var table = document.QuerySelector("table")!;
        Assert.Equal("inset", GetStyle(document, "#target").GetPropertyValue("border-top-style"));

        table.SetAttribute("border", "0");
        Assert.Equal("none", GetStyle(document, "#target").GetPropertyValue("border-top-style"));
        Assert.Equal("none", GetStyle(document, "table").GetPropertyValue("border-top-style"));

        table.SetAttribute("border", "2");
        Assert.Equal("inset", GetStyle(document, "#target").GetPropertyValue("border-top-style"));
        Assert.Equal("outset", GetStyle(document, "table").GetPropertyValue("border-top-style"));

        table.RemoveAttribute("border");
        Assert.Equal("none", GetStyle(document, "#target").GetPropertyValue("border-top-style"));
    }

    [Theory]
    [InlineData("align-self", "safe anchor-center")]
    [InlineData("justify-self", "unsafe anchor-center")]
    public void AnchorCenter_AcceptsAnOverflowPosition(string property, string value)
    {
        Assert.Equal(value, Get($"<div id=target style='{property}: {value}'></div>", property));
    }

    [Fact]
    public void Documentation_ComputedStyleFollowsTheViewport()
    {
        #region WebPlatform_ComputedStyleViewport
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            Viewport = new ViewportOptions { Width = 1280, Height = 720 },
        });

        var document = context.Navigate("https://example.com/", _ => """
            <!DOCTYPE html>
            <style>
              body { margin: 0 }
              nav { display: flex; width: 50% }
              nav::before { content: "Menu" }
              @media (max-width: 600px) { nav { display: none } }
            </style>
            <nav></nav>
            """);

        var nav = document.QuerySelector("nav")!;
        var style = context.Window.GetComputedStyle(nav);
        Assert.Equal("flex", style.Display);
        Assert.Equal("640px", style.Width); // a used value, computed by the layout
        Assert.Equal("\"Menu\"", context.Window.GetComputedStyle(nav, "::before").GetPropertyValue("content"));

        // The styles follow the viewport: the media queries and the layout use its new size
        context.Viewport = new ViewportOptions { Width = 400, Height = 800 };
        Assert.Equal("none", context.Window.GetComputedStyle(nav).Display);
        #endregion
    }
}
