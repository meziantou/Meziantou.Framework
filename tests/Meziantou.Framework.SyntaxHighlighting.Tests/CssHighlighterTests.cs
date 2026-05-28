namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class CssHighlighterTests
{

    [Fact]
    public void Selector_Universal()
    {
        AssertHighlighter("css",
"""
* { margin: 0; }
""",
"""
* { <span class="hljs-attribute">margin</span>: <span class="hljs-number">0</span>; }
""");
    }

    [Fact]
    public void Selector_Type()
    {
        AssertHighlighter("css",
"""
div { color: red; }
""",
"""
<span class="hljs-selector-tag">div</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Selector_Class()
    {
        AssertHighlighter("css",
"""
.foo { color: red; }
""",
"""
<span class="hljs-selector-class">.foo</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Selector_Id()
    {
        AssertHighlighter("css",
"""
#foo { color: red; }
""",
"""
<span class="hljs-selector-id">#foo</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Selector_Attribute()
    {
        AssertHighlighter("css",
"""
[type="text"] { color: red; }
""",
"""
<span class="hljs-selector-attr">[type=<span class="hljs-string">&quot;text&quot;</span>]</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Selector_AttributeContains()
    {
        AssertHighlighter("css",
"""
[class*="foo"] { color: red; }
""",
"""
<span class="hljs-selector-attr">[class*=<span class="hljs-string">&quot;foo&quot;</span>]</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Selector_AttributeStarts()
    {
        AssertHighlighter("css",
"""
[href^="https"] { color: red; }
""",
"""
<span class="hljs-selector-attr">[href^=<span class="hljs-string">&quot;https&quot;</span>]</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Selector_AttributeEnds()
    {
        AssertHighlighter("css",
"""
[src$=".png"] { color: red; }
""",
"""
<span class="hljs-selector-attr">[src$=<span class="hljs-string">&quot;.png&quot;</span>]</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Selector_Multiple()
    {
        AssertHighlighter("css",
"""
h1, h2, h3 { color: red; }
""",
"""
<span class="hljs-selector-tag">h1</span>, <span class="hljs-selector-tag">h2</span>, <span class="hljs-selector-tag">h3</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Selector_DescendantCombinator()
    {
        AssertHighlighter("css",
"""
div p { color: red; }
""",
"""
<span class="hljs-selector-tag">div</span> <span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Selector_ChildCombinator()
    {
        AssertHighlighter("css",
"""
div > p { color: red; }
""",
"""
<span class="hljs-selector-tag">div</span> &gt; <span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Selector_AdjacentSibling()
    {
        AssertHighlighter("css",
"""
h1 + p { color: red; }
""",
"""
<span class="hljs-selector-tag">h1</span> + <span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Selector_GeneralSibling()
    {
        AssertHighlighter("css",
"""
h1 ~ p { color: red; }
""",
"""
<span class="hljs-selector-tag">h1</span> ~ <span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Selector_ClassChain()
    {
        AssertHighlighter("css",
"""
.a.b.c { color: red; }
""",
"""
<span class="hljs-selector-class">.a</span><span class="hljs-selector-class">.b</span><span class="hljs-selector-class">.c</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Selector_TypeWithClass()
    {
        AssertHighlighter("css",
"""
a.btn { color: red; }
""",
"""
<span class="hljs-selector-tag">a</span><span class="hljs-selector-class">.btn</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Pseudo_Hover()
    {
        AssertHighlighter("css",
"""
a:hover { color: red; }
""",
"""
<span class="hljs-selector-tag">a</span><span class="hljs-selector-pseudo">:hover</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Pseudo_Focus()
    {
        AssertHighlighter("css",
"""
input:focus { outline: 2px solid blue; }
""",
"""
<span class="hljs-selector-tag">input</span><span class="hljs-selector-pseudo">:focus</span> { <span class="hljs-attribute">outline</span>: <span class="hljs-number">2px</span> solid blue; }
""");
    }

    [Fact]
    public void Pseudo_FocusVisible()
    {
        AssertHighlighter("css",
"""
button:focus-visible { outline: 2px solid blue; }
""",
"""
<span class="hljs-selector-tag">button</span><span class="hljs-selector-pseudo">:focus-visible</span> { <span class="hljs-attribute">outline</span>: <span class="hljs-number">2px</span> solid blue; }
""");
    }

    [Fact]
    public void Pseudo_FocusWithin()
    {
        AssertHighlighter("css",
"""
form:focus-within { background: yellow; }
""",
"""
<span class="hljs-selector-tag">form</span><span class="hljs-selector-pseudo">:focus-within</span> { <span class="hljs-attribute">background</span>: yellow; }
""");
    }

    [Fact]
    public void Pseudo_Active()
    {
        AssertHighlighter("css",
"""
a:active { color: red; }
""",
"""
<span class="hljs-selector-tag">a</span><span class="hljs-selector-pseudo">:active</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Pseudo_FirstChild()
    {
        AssertHighlighter("css",
"""
li:first-child { color: red; }
""",
"""
<span class="hljs-selector-tag">li</span><span class="hljs-selector-pseudo">:first-child</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Pseudo_LastChild()
    {
        AssertHighlighter("css",
"""
li:last-child { color: red; }
""",
"""
<span class="hljs-selector-tag">li</span><span class="hljs-selector-pseudo">:last-child</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Pseudo_NthChild()
    {
        AssertHighlighter("css",
"""
li:nth-child(2n+1) { color: red; }
""",
"""
<span class="hljs-selector-tag">li</span><span class="hljs-selector-pseudo">:nth-child</span>(<span class="hljs-number">2</span>n+<span class="hljs-number">1</span>) { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Pseudo_NthOfType()
    {
        AssertHighlighter("css",
"""
li:nth-of-type(odd) { color: red; }
""",
"""
<span class="hljs-selector-tag">li</span><span class="hljs-selector-pseudo">:nth-of-type</span>(odd) { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Pseudo_Not()
    {
        AssertHighlighter("css",
"""
li:not(.active) { color: red; }
""",
"""
<span class="hljs-selector-tag">li</span><span class="hljs-selector-pseudo">:not</span>(<span class="hljs-selector-class">.active</span>) { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Pseudo_Has()
    {
        AssertHighlighter("css",
"""
article:has(img) { padding: 1rem; }
""",
"""
<span class="hljs-selector-tag">article</span><span class="hljs-selector-pseudo">:has</span>(<span class="hljs-selector-tag">img</span>) { <span class="hljs-attribute">padding</span>: <span class="hljs-number">1rem</span>; }
""");
    }

    [Fact]
    public void Pseudo_HasComplex()
    {
        AssertHighlighter("css",
"""
div:has(> img + p) { padding: 1rem; }
""",
"""
<span class="hljs-selector-tag">div</span><span class="hljs-selector-pseudo">:has</span>(&gt; <span class="hljs-selector-tag">img</span> + <span class="hljs-selector-tag">p</span>) { <span class="hljs-attribute">padding</span>: <span class="hljs-number">1rem</span>; }
""");
    }

    [Fact]
    public void Pseudo_Is()
    {
        AssertHighlighter("css",
"""
:is(h1, h2, h3) { font-weight: bold; }
""",
"""
<span class="hljs-selector-pseudo">:is</span>(<span class="hljs-selector-tag">h1</span>, <span class="hljs-selector-tag">h2</span>, <span class="hljs-selector-tag">h3</span>) { <span class="hljs-attribute">font-weight</span>: bold; }
""");
    }

    [Fact]
    public void Pseudo_Where()
    {
        AssertHighlighter("css",
"""
:where(h1, h2) { margin: 0; }
""",
"""
<span class="hljs-selector-pseudo">:where</span>(<span class="hljs-selector-tag">h1</span>, <span class="hljs-selector-tag">h2</span>) { <span class="hljs-attribute">margin</span>: <span class="hljs-number">0</span>; }
""");
    }

    [Fact]
    public void Pseudo_Empty()
    {
        AssertHighlighter("css",
"""
p:empty { display: none; }
""",
"""
<span class="hljs-selector-tag">p</span><span class="hljs-selector-pseudo">:empty</span> { <span class="hljs-attribute">display</span>: none; }
""");
    }

    [Fact]
    public void Pseudo_Root()
    {
        AssertHighlighter("css",
"""
:root { --primary: blue; }
""",
"""
<span class="hljs-selector-pseudo">:root</span> { <span class="hljs-attr">--primary</span>: blue; }
""");
    }

    [Fact]
    public void Pseudo_DefaultStyle()
    {
        AssertHighlighter("css",
"""
option:default { font-weight: bold; }
""",
"""
<span class="hljs-selector-tag">option</span><span class="hljs-selector-pseudo">:default</span> { <span class="hljs-attribute">font-weight</span>: bold; }
""");
    }

    [Fact]
    public void Pseudo_PlaceholderShown()
    {
        AssertHighlighter("css",
"""
input:placeholder-shown { color: gray; }
""",
"""
<span class="hljs-selector-tag">input</span><span class="hljs-selector-pseudo">:placeholder-shown</span> { <span class="hljs-attribute">color</span>: gray; }
""");
    }

    [Fact]
    public void Pseudo_Dir()
    {
        AssertHighlighter("css",
"""
p:dir(rtl) { text-align: right; }
""",
"""
<span class="hljs-selector-tag">p</span><span class="hljs-selector-pseudo">:dir</span>(rtl) { <span class="hljs-attribute">text-align</span>: right; }
""");
    }

    [Fact]
    public void Pseudo_Lang()
    {
        AssertHighlighter("css",
"""
p:lang(en) { font-family: serif; }
""",
"""
<span class="hljs-selector-tag">p</span><span class="hljs-selector-pseudo">:lang</span>(en) { <span class="hljs-attribute">font-family</span>: serif; }
""");
    }

    [Fact]
    public void PseudoElement_Before()
    {
        AssertHighlighter("css",
"""
a::before { content: ">"; }
""",
"""
<span class="hljs-selector-tag">a</span><span class="hljs-selector-pseudo">::before</span> { <span class="hljs-attribute">content</span>: <span class="hljs-string">&quot;&gt;&quot;</span>; }
""");
    }

    [Fact]
    public void PseudoElement_After()
    {
        AssertHighlighter("css",
"""
a::after { content: "<"; }
""",
"""
<span class="hljs-selector-tag">a</span><span class="hljs-selector-pseudo">::after</span> { <span class="hljs-attribute">content</span>: <span class="hljs-string">&quot;&lt;&quot;</span>; }
""");
    }

    [Fact]
    public void PseudoElement_FirstLetter()
    {
        AssertHighlighter("css",
"""
p::first-letter { font-size: 2em; }
""",
"""
<span class="hljs-selector-tag">p</span><span class="hljs-selector-pseudo">::first-letter</span> { <span class="hljs-attribute">font-size</span>: <span class="hljs-number">2em</span>; }
""");
    }

    [Fact]
    public void PseudoElement_FirstLine()
    {
        AssertHighlighter("css",
"""
p::first-line { font-weight: bold; }
""",
"""
<span class="hljs-selector-tag">p</span><span class="hljs-selector-pseudo">::first-line</span> { <span class="hljs-attribute">font-weight</span>: bold; }
""");
    }

    [Fact]
    public void PseudoElement_Placeholder()
    {
        AssertHighlighter("css",
"""
input::placeholder { color: gray; }
""",
"""
<span class="hljs-selector-tag">input</span><span class="hljs-selector-pseudo">::placeholder</span> { <span class="hljs-attribute">color</span>: gray; }
""");
    }

    [Fact]
    public void PseudoElement_Selection()
    {
        AssertHighlighter("css",
"""
p::selection { background: yellow; }
""",
"""
<span class="hljs-selector-tag">p</span><span class="hljs-selector-pseudo">::selection</span> { <span class="hljs-attribute">background</span>: yellow; }
""");
    }

    [Fact]
    public void PseudoElement_Marker()
    {
        AssertHighlighter("css",
"""
li::marker { color: red; }
""",
"""
<span class="hljs-selector-tag">li</span><span class="hljs-selector-pseudo">::marker</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void PseudoElement_Backdrop()
    {
        AssertHighlighter("css",
"""
dialog::backdrop { background: rgba(0,0,0,.5); }
""",
"""
dialog<span class="hljs-selector-pseudo">::backdrop</span> { <span class="hljs-attribute">background</span>: <span class="hljs-built_in">rgba</span>(<span class="hljs-number">0</span>,<span class="hljs-number">0</span>,<span class="hljs-number">0</span>,.<span class="hljs-number">5</span>); }
""");
    }

    [Fact]
    public void PseudoElement_Part()
    {
        AssertHighlighter("css",
"""
custom-element::part(button) { color: red; }
""",
"""
custom-element<span class="hljs-selector-pseudo">::part</span>(<span class="hljs-selector-tag">button</span>) { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void PseudoElement_Slotted()
    {
        AssertHighlighter("css",
"""
::slotted(span) { color: red; }
""",
"""
<span class="hljs-selector-pseudo">::slotted</span>(<span class="hljs-selector-tag">span</span>) { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void PseudoElement_ViewTransition()
    {
        AssertHighlighter("css",
"""
::view-transition-old(root) { animation: fade-out 0.3s; }
""",
"""
::<span class="hljs-built_in">view-transition-old</span>(root) { <span class="hljs-attribute">animation</span>: fade-out <span class="hljs-number">0.3s</span>; }
""");
    }

    [Fact]
    public void Property_Color()
    {
        AssertHighlighter("css",
"""
p { color: red; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Property_Background()
    {
        AssertHighlighter("css",
"""
p { background: blue; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">background</span>: blue; }
""");
    }

    [Fact]
    public void Property_FontFamily()
    {
        AssertHighlighter("css",
"""
p { font-family: sans-serif; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">font-family</span>: sans-serif; }
""");
    }

    [Fact]
    public void Property_FontSize()
    {
        AssertHighlighter("css",
"""
p { font-size: 16px; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">font-size</span>: <span class="hljs-number">16px</span>; }
""");
    }

    [Fact]
    public void Property_FontWeight()
    {
        AssertHighlighter("css",
"""
p { font-weight: bold; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">font-weight</span>: bold; }
""");
    }

    [Fact]
    public void Property_Margin()
    {
        AssertHighlighter("css",
"""
p { margin: 10px; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">margin</span>: <span class="hljs-number">10px</span>; }
""");
    }

    [Fact]
    public void Property_Padding()
    {
        AssertHighlighter("css",
"""
p { padding: 10px 20px; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">padding</span>: <span class="hljs-number">10px</span> <span class="hljs-number">20px</span>; }
""");
    }

    [Fact]
    public void Property_Display()
    {
        AssertHighlighter("css",
"""
p { display: flex; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">display</span>: flex; }
""");
    }

    [Fact]
    public void Property_Position()
    {
        AssertHighlighter("css",
"""
p { position: absolute; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">position</span>: absolute; }
""");
    }

    [Fact]
    public void Property_Important()
    {
        AssertHighlighter("css",
"""
p { color: red !important; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red <span class="hljs-meta">!important</span>; }
""");
    }

    [Fact]
    public void Property_CustomProp()
    {
        AssertHighlighter("css",
"""
p { --primary-color: blue; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attr">--primary-color</span>: blue; }
""");
    }

    [Fact]
    public void Property_VendorPrefix()
    {
        AssertHighlighter("css",
"""
p { -webkit-appearance: none; }
""",
"""
<span class="hljs-selector-tag">p</span> { -webkit-<span class="hljs-attribute">appearance</span>: none; }
""");
    }

    [Fact]
    public void Property_LogicalInline()
    {
        AssertHighlighter("css",
"""
p { margin-inline: 1rem; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">margin-inline</span>: <span class="hljs-number">1rem</span>; }
""");
    }

    [Fact]
    public void Property_LogicalBlock()
    {
        AssertHighlighter("css",
"""
p { padding-block: 1rem; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">padding-block</span>: <span class="hljs-number">1rem</span>; }
""");
    }

    [Fact]
    public void Property_AspectRatio()
    {
        AssertHighlighter("css",
"""
div { aspect-ratio: 16 / 9; }
""",
"""
<span class="hljs-selector-tag">div</span> { <span class="hljs-attribute">aspect-ratio</span>: <span class="hljs-number">16</span> / <span class="hljs-number">9</span>; }
""");
    }

    [Fact]
    public void Property_Gap()
    {
        AssertHighlighter("css",
"""
div { gap: 1rem; }
""",
"""
<span class="hljs-selector-tag">div</span> { <span class="hljs-attribute">gap</span>: <span class="hljs-number">1rem</span>; }
""");
    }

    [Fact]
    public void Property_Subgrid()
    {
        AssertHighlighter("css",
"""
div { grid-template-columns: subgrid; }
""",
"""
<span class="hljs-selector-tag">div</span> { <span class="hljs-attribute">grid-template-columns</span>: subgrid; }
""");
    }

    [Fact]
    public void Property_ContentVisibility()
    {
        AssertHighlighter("css",
"""
div { content-visibility: auto; }
""",
"""
<span class="hljs-selector-tag">div</span> { <span class="hljs-attribute">content-visibility</span>: auto; }
""");
    }

    [Fact]
    public void Property_Overscroll()
    {
        AssertHighlighter("css",
"""
div { overscroll-behavior: contain; }
""",
"""
<span class="hljs-selector-tag">div</span> { <span class="hljs-attribute">overscroll-behavior</span>: contain; }
""");
    }

    [Fact]
    public void Property_ScrollSnap()
    {
        AssertHighlighter("css",
"""
div { scroll-snap-type: y mandatory; }
""",
"""
<span class="hljs-selector-tag">div</span> { <span class="hljs-attribute">scroll-snap-type</span>: y mandatory; }
""");
    }

    [Fact]
    public void Property_Accent()
    {
        AssertHighlighter("css",
"""
input { accent-color: tomato; }
""",
"""
<span class="hljs-selector-tag">input</span> { <span class="hljs-attribute">accent-color</span>: tomato; }
""");
    }

    [Fact]
    public void Property_ColorScheme()
    {
        AssertHighlighter("css",
"""
:root { color-scheme: light dark; }
""",
"""
<span class="hljs-selector-pseudo">:root</span> { <span class="hljs-attribute">color-scheme</span>: light dark; }
""");
    }

    [Fact]
    public void Color_NamedRed()
    {
        AssertHighlighter("css",
"""
p { color: red; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Color_NamedTrans()
    {
        AssertHighlighter("css",
"""
p { background: transparent; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">background</span>: transparent; }
""");
    }

    [Fact]
    public void Color_CurrentColor()
    {
        AssertHighlighter("css",
"""
p { border-color: currentColor; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">border-color</span>: currentColor; }
""");
    }

    [Fact]
    public void Color_Hex3()
    {
        AssertHighlighter("css",
"""
p { color: #f00; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-number">#f00</span>; }
""");
    }

    [Fact]
    public void Color_Hex6()
    {
        AssertHighlighter("css",
"""
p { color: #ff0000; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-number">#ff0000</span>; }
""");
    }

    [Fact]
    public void Color_Hex8Alpha()
    {
        AssertHighlighter("css",
"""
p { color: #ff0000aa; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-number">#ff0000aa</span>; }
""");
    }

    [Fact]
    public void Color_Rgb()
    {
        AssertHighlighter("css",
"""
p { color: rgb(255, 0, 0); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">rgb</span>(<span class="hljs-number">255</span>, <span class="hljs-number">0</span>, <span class="hljs-number">0</span>); }
""");
    }

    [Fact]
    public void Color_RgbModern()
    {
        AssertHighlighter("css",
"""
p { color: rgb(255 0 0); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">rgb</span>(<span class="hljs-number">255</span> <span class="hljs-number">0</span> <span class="hljs-number">0</span>); }
""");
    }

    [Fact]
    public void Color_Rgba()
    {
        AssertHighlighter("css",
"""
p { color: rgba(255, 0, 0, 0.5); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">rgba</span>(<span class="hljs-number">255</span>, <span class="hljs-number">0</span>, <span class="hljs-number">0</span>, <span class="hljs-number">0.5</span>); }
""");
    }

    [Fact]
    public void Color_RgbModernAlpha()
    {
        AssertHighlighter("css",
"""
p { color: rgb(255 0 0 / 50%); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">rgb</span>(<span class="hljs-number">255</span> <span class="hljs-number">0</span> <span class="hljs-number">0</span> / <span class="hljs-number">50%</span>); }
""");
    }

    [Fact]
    public void Color_Hsl()
    {
        AssertHighlighter("css",
"""
p { color: hsl(0, 100%, 50%); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">hsl</span>(<span class="hljs-number">0</span>, <span class="hljs-number">100%</span>, <span class="hljs-number">50%</span>); }
""");
    }

    [Fact]
    public void Color_Hsla()
    {
        AssertHighlighter("css",
"""
p { color: hsla(0, 100%, 50%, 0.5); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">hsla</span>(<span class="hljs-number">0</span>, <span class="hljs-number">100%</span>, <span class="hljs-number">50%</span>, <span class="hljs-number">0.5</span>); }
""");
    }

    [Fact]
    public void Color_Hwb()
    {
        AssertHighlighter("css",
"""
p { color: hwb(0 0% 0%); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">hwb</span>(<span class="hljs-number">0</span> <span class="hljs-number">0%</span> <span class="hljs-number">0%</span>); }
""");
    }

    [Fact]
    public void Color_Lab()
    {
        AssertHighlighter("css",
"""
p { color: lab(52.2% 40.16 59.99); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">lab</span>(<span class="hljs-number">52.2%</span> <span class="hljs-number">40.16</span> <span class="hljs-number">59.99</span>); }
""");
    }

    [Fact]
    public void Color_Lch()
    {
        AssertHighlighter("css",
"""
p { color: lch(52.2% 72.2 50); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">lch</span>(<span class="hljs-number">52.2%</span> <span class="hljs-number">72.2</span> <span class="hljs-number">50</span>); }
""");
    }

    [Fact]
    public void Color_Oklab()
    {
        AssertHighlighter("css",
"""
p { color: oklab(0.59 0.1 0.1); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">oklab</span>(<span class="hljs-number">0.59</span> <span class="hljs-number">0.1</span> <span class="hljs-number">0.1</span>); }
""");
    }

    [Fact]
    public void Color_Oklch()
    {
        AssertHighlighter("css",
"""
p { color: oklch(0.6 0.15 30); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">oklch</span>(<span class="hljs-number">0.6</span> <span class="hljs-number">0.15</span> <span class="hljs-number">30</span>); }
""");
    }

    [Fact]
    public void Color_ColorFunc()
    {
        AssertHighlighter("css",
"""
p { color: color(display-p3 1 0 0); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">color</span>(display-p3 <span class="hljs-number">1</span> <span class="hljs-number">0</span> <span class="hljs-number">0</span>); }
""");
    }

    [Fact]
    public void Color_ColorMix()
    {
        AssertHighlighter("css",
"""
p { color: color-mix(in oklch, red, blue); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">color-mix</span>(in oklch, red, blue); }
""");
    }

    [Fact]
    public void Color_LightDark()
    {
        AssertHighlighter("css",
"""
p { color: light-dark(black, white); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">light-dark</span>(black, white); }
""");
    }

    [Fact]
    public void Unit_Px()
    {
        AssertHighlighter("css",
"""
p { width: 100px; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">100px</span>; }
""");
    }

    [Fact]
    public void Unit_Em()
    {
        AssertHighlighter("css",
"""
p { font-size: 1.5em; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">font-size</span>: <span class="hljs-number">1.5em</span>; }
""");
    }

    [Fact]
    public void Unit_Rem()
    {
        AssertHighlighter("css",
"""
p { font-size: 1rem; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">font-size</span>: <span class="hljs-number">1rem</span>; }
""");
    }

    [Fact]
    public void Unit_Percent()
    {
        AssertHighlighter("css",
"""
p { width: 50%; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">50%</span>; }
""");
    }

    [Fact]
    public void Unit_Vw()
    {
        AssertHighlighter("css",
"""
p { width: 100vw; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">100vw</span>; }
""");
    }

    [Fact]
    public void Unit_Vh()
    {
        AssertHighlighter("css",
"""
p { height: 100vh; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">height</span>: <span class="hljs-number">100vh</span>; }
""");
    }

    [Fact]
    public void Unit_Dvh()
    {
        AssertHighlighter("css",
"""
p { height: 100dvh; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">height</span>: <span class="hljs-number">100dvh</span>; }
""");
    }

    [Fact]
    public void Unit_Svh()
    {
        AssertHighlighter("css",
"""
p { height: 100svh; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">height</span>: <span class="hljs-number">100svh</span>; }
""");
    }

    [Fact]
    public void Unit_Lvh()
    {
        AssertHighlighter("css",
"""
p { height: 100lvh; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">height</span>: <span class="hljs-number">100lvh</span>; }
""");
    }

    [Fact]
    public void Unit_Cqw()
    {
        AssertHighlighter("css",
"""
p { width: 50cqw; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">50cqw</span>; }
""");
    }

    [Fact]
    public void Unit_Cqh()
    {
        AssertHighlighter("css",
"""
p { height: 50cqh; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">height</span>: <span class="hljs-number">50cqh</span>; }
""");
    }

    [Fact]
    public void Unit_Cqi()
    {
        AssertHighlighter("css",
"""
p { width: 50cqi; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">50cqi</span>; }
""");
    }

    [Fact]
    public void Unit_Cqb()
    {
        AssertHighlighter("css",
"""
p { height: 50cqb; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">height</span>: <span class="hljs-number">50cqb</span>; }
""");
    }

    [Fact]
    public void Unit_Ch()
    {
        AssertHighlighter("css",
"""
p { width: 20ch; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">20ch</span>; }
""");
    }

    [Fact]
    public void Unit_Ex()
    {
        AssertHighlighter("css",
"""
p { height: 1ex; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">height</span>: <span class="hljs-number">1ex</span>; }
""");
    }

    [Fact]
    public void Unit_Fr()
    {
        AssertHighlighter("css",
"""
.grid { grid-template-columns: 1fr 2fr; }
""",
"""
<span class="hljs-selector-class">.grid</span> { <span class="hljs-attribute">grid-template-columns</span>: <span class="hljs-number">1fr</span> <span class="hljs-number">2fr</span>; }
""");
    }

    [Fact]
    public void Unit_Deg()
    {
        AssertHighlighter("css",
"""
p { transform: rotate(45deg); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">transform</span>: <span class="hljs-built_in">rotate</span>(<span class="hljs-number">45deg</span>); }
""");
    }

    [Fact]
    public void Unit_Rad()
    {
        AssertHighlighter("css",
"""
p { transform: rotate(1rad); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">transform</span>: <span class="hljs-built_in">rotate</span>(<span class="hljs-number">1rad</span>); }
""");
    }

    [Fact]
    public void Unit_Turn()
    {
        AssertHighlighter("css",
"""
p { transform: rotate(0.5turn); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">transform</span>: <span class="hljs-built_in">rotate</span>(<span class="hljs-number">0.5turn</span>); }
""");
    }

    [Fact]
    public void Unit_S()
    {
        AssertHighlighter("css",
"""
p { transition-duration: 1s; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">transition-duration</span>: <span class="hljs-number">1s</span>; }
""");
    }

    [Fact]
    public void Unit_Ms()
    {
        AssertHighlighter("css",
"""
p { transition-duration: 500ms; }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">transition-duration</span>: <span class="hljs-number">500ms</span>; }
""");
    }

    [Fact]
    public void Function_Calc()
    {
        AssertHighlighter("css",
"""
p { width: calc(100% - 20px); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">width</span>: <span class="hljs-built_in">calc</span>(<span class="hljs-number">100%</span> - <span class="hljs-number">20px</span>); }
""");
    }

    [Fact]
    public void Function_CalcNested()
    {
        AssertHighlighter("css",
"""
p { width: calc((100% - 20px) / 2); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">width</span>: <span class="hljs-built_in">calc</span>((<span class="hljs-number">100%</span> - <span class="hljs-number">20px</span>) / <span class="hljs-number">2</span>); }
""");
    }

    [Fact]
    public void Function_Var()
    {
        AssertHighlighter("css",
"""
p { color: var(--primary); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">var</span>(--primary); }
""");
    }

    [Fact]
    public void Function_VarFallback()
    {
        AssertHighlighter("css",
"""
p { color: var(--primary, red); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">var</span>(--primary, red); }
""");
    }

    [Fact]
    public void Function_Min()
    {
        AssertHighlighter("css",
"""
p { width: min(100%, 800px); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">width</span>: <span class="hljs-built_in">min</span>(<span class="hljs-number">100%</span>, <span class="hljs-number">800px</span>); }
""");
    }

    [Fact]
    public void Function_Max()
    {
        AssertHighlighter("css",
"""
p { width: max(50%, 300px); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">width</span>: <span class="hljs-built_in">max</span>(<span class="hljs-number">50%</span>, <span class="hljs-number">300px</span>); }
""");
    }

    [Fact]
    public void Function_Clamp()
    {
        AssertHighlighter("css",
"""
p { width: clamp(200px, 50%, 800px); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">width</span>: <span class="hljs-built_in">clamp</span>(<span class="hljs-number">200px</span>, <span class="hljs-number">50%</span>, <span class="hljs-number">800px</span>); }
""");
    }

    [Fact]
    public void Function_Env()
    {
        AssertHighlighter("css",
"""
p { padding-top: env(safe-area-inset-top); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">padding-top</span>: <span class="hljs-built_in">env</span>(safe-area-inset-top); }
""");
    }

    [Fact]
    public void Function_Url()
    {
        AssertHighlighter("css",
"""
p { background: url("bg.png"); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">background</span>: <span class="hljs-built_in">url</span>(<span class="hljs-string">&quot;bg.png&quot;</span>); }
""");
    }

    [Fact]
    public void Function_LinearGradient()
    {
        AssertHighlighter("css",
"""
p { background: linear-gradient(to right, red, blue); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">background</span>: <span class="hljs-built_in">linear-gradient</span>(to right, red, blue); }
""");
    }

    [Fact]
    public void Function_RadialGradient()
    {
        AssertHighlighter("css",
"""
p { background: radial-gradient(circle, red, blue); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">background</span>: <span class="hljs-built_in">radial-gradient</span>(circle, red, blue); }
""");
    }

    [Fact]
    public void Function_ConicGradient()
    {
        AssertHighlighter("css",
"""
p { background: conic-gradient(red, blue); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">background</span>: <span class="hljs-built_in">conic-gradient</span>(red, blue); }
""");
    }

    [Fact]
    public void Function_Attr()
    {
        AssertHighlighter("css",
"""
a::after { content: attr(href); }
""",
"""
<span class="hljs-selector-tag">a</span><span class="hljs-selector-pseudo">::after</span> { <span class="hljs-attribute">content</span>: <span class="hljs-built_in">attr</span>(href); }
""");
    }

    [Fact]
    public void Function_Counter()
    {
        AssertHighlighter("css",
"""
li::before { content: counter(items); }
""",
"""
<span class="hljs-selector-tag">li</span><span class="hljs-selector-pseudo">::before</span> { <span class="hljs-attribute">content</span>: <span class="hljs-built_in">counter</span>(items); }
""");
    }

    [Fact]
    public void Function_Translate()
    {
        AssertHighlighter("css",
"""
p { transform: translate(10px, 20px); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">transform</span>: <span class="hljs-built_in">translate</span>(<span class="hljs-number">10px</span>, <span class="hljs-number">20px</span>); }
""");
    }

    [Fact]
    public void Function_Rotate()
    {
        AssertHighlighter("css",
"""
p { transform: rotate(45deg); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">transform</span>: <span class="hljs-built_in">rotate</span>(<span class="hljs-number">45deg</span>); }
""");
    }

    [Fact]
    public void Function_Scale()
    {
        AssertHighlighter("css",
"""
p { transform: scale(1.5); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">transform</span>: <span class="hljs-built_in">scale</span>(<span class="hljs-number">1.5</span>); }
""");
    }

    [Fact]
    public void Function_Matrix()
    {
        AssertHighlighter("css",
"""
p { transform: matrix(1, 0, 0, 1, 0, 0); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">transform</span>: <span class="hljs-built_in">matrix</span>(<span class="hljs-number">1</span>, <span class="hljs-number">0</span>, <span class="hljs-number">0</span>, <span class="hljs-number">1</span>, <span class="hljs-number">0</span>, <span class="hljs-number">0</span>); }
""");
    }

    [Fact]
    public void Function_Pow()
    {
        AssertHighlighter("css",
"""
p { width: pow(2, 3px); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">width</span>: <span class="hljs-built_in">pow</span>(<span class="hljs-number">2</span>, <span class="hljs-number">3px</span>); }
""");
    }

    [Fact]
    public void Function_Sqrt()
    {
        AssertHighlighter("css",
"""
p { width: sqrt(16px); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">width</span>: <span class="hljs-built_in">sqrt</span>(<span class="hljs-number">16px</span>); }
""");
    }

    [Fact]
    public void Function_Sin()
    {
        AssertHighlighter("css",
"""
p { transform: rotate(sin(45deg)); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">transform</span>: <span class="hljs-built_in">rotate</span>(<span class="hljs-built_in">sin</span>(<span class="hljs-number">45deg</span>)); }
""");
    }

    [Fact]
    public void Function_AnchorFn()
    {
        AssertHighlighter("css",
"""
p { top: anchor(top); }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">top</span>: <span class="hljs-built_in">anchor</span>(top); }
""");
    }

    [Fact]
    public void AtRule_Import()
    {
        AssertHighlighter("css",
"""
@import url("style.css");
""",
"""
<span class="hljs-keyword">@import</span> url(<span class="hljs-string">&quot;style.css&quot;</span>);
""");
    }

    [Fact]
    public void AtRule_ImportLayer()
    {
        AssertHighlighter("css",
"""
@import url("style.css") layer(base);
""",
"""
<span class="hljs-keyword">@import</span> url(<span class="hljs-string">&quot;style.css&quot;</span>) layer(base);
""");
    }

    [Fact]
    public void AtRule_ImportSupports()
    {
        AssertHighlighter("css",
"""
@import url("style.css") supports(display: grid);
""",
"""
<span class="hljs-keyword">@import</span> url(<span class="hljs-string">&quot;style.css&quot;</span>) supports(<span class="hljs-attribute">display</span>: <span class="hljs-attribute">grid</span>);
""");
    }

    [Fact]
    public void AtRule_Charset()
    {
        AssertHighlighter("css",
"""
@charset "utf-8";
""",
"""
<span class="hljs-keyword">@charset</span> <span class="hljs-string">&quot;utf-8&quot;</span>;
""");
    }

    [Fact]
    public void AtRule_Namespace()
    {
        AssertHighlighter("css",
"""
@namespace svg url(http://www.w3.org/2000/svg);
""",
"""
<span class="hljs-keyword">@namespace</span> svg url(<span class="hljs-attribute">http</span>://www.w3.org/<span class="hljs-number">2000</span>/svg);
""");
    }

    [Fact]
    public void AtRule_FontFace()
    {
        AssertHighlighter("css",
"""
@font-face {
  font-family: "MyFont";
  src: url("font.woff2");
}
""",
"""
<span class="hljs-keyword">@font-face</span> {
  <span class="hljs-attribute">font-family</span>: <span class="hljs-string">&quot;MyFont&quot;</span>;
  <span class="hljs-attribute">src</span>: <span class="hljs-built_in">url</span>(<span class="hljs-string">&quot;font.woff2&quot;</span>);
}
""");
    }

    [Fact]
    public void AtRule_Page()
    {
        AssertHighlighter("css",
"""
@page { margin: 1cm; }
""",
"""
<span class="hljs-keyword">@page</span> { <span class="hljs-attribute">margin</span>: <span class="hljs-number">1cm</span>; }
""");
    }

    [Fact]
    public void AtRule_PageNamed()
    {
        AssertHighlighter("css",
"""
@page :first { margin-top: 2cm; }
""",
"""
<span class="hljs-keyword">@page</span> :first { <span class="hljs-attribute">margin-top</span>: <span class="hljs-number">2cm</span>; }
""");
    }

    [Fact]
    public void AtRule_Keyframes()
    {
        AssertHighlighter("css",
"""
@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}
""",
"""
<span class="hljs-keyword">@keyframes</span> spin {
  <span class="hljs-selector-tag">from</span> { <span class="hljs-attribute">transform</span>: <span class="hljs-built_in">rotate</span>(<span class="hljs-number">0deg</span>); }
  <span class="hljs-selector-tag">to</span> { <span class="hljs-attribute">transform</span>: <span class="hljs-built_in">rotate</span>(<span class="hljs-number">360deg</span>); }
}
""");
    }

    [Fact]
    public void AtRule_KeyframesPercent()
    {
        AssertHighlighter("css",
"""
@keyframes pulse {
  0% { opacity: 1; }
  50% { opacity: 0.5; }
  100% { opacity: 1; }
}
""",
"""
<span class="hljs-keyword">@keyframes</span> pulse {
  <span class="hljs-number">0%</span> { <span class="hljs-attribute">opacity</span>: <span class="hljs-number">1</span>; }
  <span class="hljs-number">50%</span> { <span class="hljs-attribute">opacity</span>: <span class="hljs-number">0.5</span>; }
  <span class="hljs-number">100%</span> { <span class="hljs-attribute">opacity</span>: <span class="hljs-number">1</span>; }
}
""");
    }

    [Fact]
    public void AtRule_Media()
    {
        AssertHighlighter("css",
"""
@media (min-width: 768px) {
  p { font-size: 18px; }
}
""",
"""
<span class="hljs-keyword">@media</span> (<span class="hljs-attribute">min-width</span>: <span class="hljs-number">768px</span>) {
  <span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">font-size</span>: <span class="hljs-number">18px</span>; }
}
""");
    }

    [Fact]
    public void AtRule_MediaAnd()
    {
        AssertHighlighter("css",
"""
@media (min-width: 768px) and (max-width: 1200px) {
  p { font-size: 18px; }
}
""",
"""
<span class="hljs-keyword">@media</span> (<span class="hljs-attribute">min-width</span>: <span class="hljs-number">768px</span>) <span class="hljs-keyword">and</span> (<span class="hljs-attribute">max-width</span>: <span class="hljs-number">1200px</span>) {
  <span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">font-size</span>: <span class="hljs-number">18px</span>; }
}
""");
    }

    [Fact]
    public void AtRule_MediaPrefers()
    {
        AssertHighlighter("css",
"""
@media (prefers-color-scheme: dark) {
  body { background: black; }
}
""",
"""
<span class="hljs-keyword">@media</span> (<span class="hljs-attribute">prefers-color-scheme</span>: dark) {
  <span class="hljs-selector-tag">body</span> { <span class="hljs-attribute">background</span>: black; }
}
""");
    }

    [Fact]
    public void AtRule_MediaPrefersMotion()
    {
        AssertHighlighter("css",
"""
@media (prefers-reduced-motion: reduce) {
  * { animation: none !important; }
}
""",
"""
<span class="hljs-keyword">@media</span> (<span class="hljs-attribute">prefers-reduced-motion</span>: reduce) {
  * { <span class="hljs-attribute">animation</span>: none <span class="hljs-meta">!important</span>; }
}
""");
    }

    [Fact]
    public void AtRule_MediaRange()
    {
        AssertHighlighter("css",
"""
@media (768px <= width <= 1200px) {
  p { font-size: 18px; }
}
""",
"""
<span class="hljs-keyword">@media</span> (<span class="hljs-number">768px</span> &lt;= <span class="hljs-attribute">width</span> &lt;= <span class="hljs-number">1200px</span>) {
  <span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">font-size</span>: <span class="hljs-number">18px</span>; }
}
""");
    }

    [Fact]
    public void AtRule_Supports()
    {
        AssertHighlighter("css",
"""
@supports (display: grid) {
  div { display: grid; }
}
""",
"""
<span class="hljs-keyword">@supports</span> (<span class="hljs-attribute">display</span>: <span class="hljs-attribute">grid</span>) {
  <span class="hljs-selector-tag">div</span> { <span class="hljs-attribute">display</span>: grid; }
}
""");
    }

    [Fact]
    public void AtRule_SupportsNot()
    {
        AssertHighlighter("css",
"""
@supports not (display: grid) {
  div { display: flex; }
}
""",
"""
<span class="hljs-keyword">@supports</span> <span class="hljs-keyword">not</span> (<span class="hljs-attribute">display</span>: <span class="hljs-attribute">grid</span>) {
  <span class="hljs-selector-tag">div</span> { <span class="hljs-attribute">display</span>: flex; }
}
""");
    }

    [Fact]
    public void AtRule_SupportsSelector()
    {
        AssertHighlighter("css",
"""
@supports selector(:has(img)) {
  article:has(img) { padding: 1rem; }
}
""",
"""
<span class="hljs-keyword">@supports</span> selector(:has(img)) {
  <span class="hljs-selector-tag">article</span><span class="hljs-selector-pseudo">:has</span>(<span class="hljs-selector-tag">img</span>) { <span class="hljs-attribute">padding</span>: <span class="hljs-number">1rem</span>; }
}
""");
    }

    [Fact]
    public void AtRule_Container()
    {
        AssertHighlighter("css",
"""
@container (min-width: 400px) {
  p { font-size: 18px; }
}
""",
"""
<span class="hljs-keyword">@container</span> (<span class="hljs-attribute">min-width</span>: <span class="hljs-number">400px</span>) {
  <span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">font-size</span>: <span class="hljs-number">18px</span>; }
}
""");
    }

    [Fact]
    public void AtRule_ContainerNamed()
    {
        AssertHighlighter("css",
"""
@container card (min-width: 400px) {
  p { font-size: 18px; }
}
""",
"""
<span class="hljs-keyword">@container</span> card (<span class="hljs-attribute">min-width</span>: <span class="hljs-number">400px</span>) {
  <span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">font-size</span>: <span class="hljs-number">18px</span>; }
}
""");
    }

    [Fact]
    public void AtRule_ContainerStyle()
    {
        AssertHighlighter("css",
"""
@container style(--theme: dark) {
  p { color: white; }
}
""",
"""
<span class="hljs-keyword">@container</span> style(<span class="hljs-attribute">--theme</span>: dark) {
  <span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: white; }
}
""");
    }

    [Fact]
    public void AtRule_Layer()
    {
        AssertHighlighter("css",
"""
@layer base, components, utilities;
""",
"""
<span class="hljs-keyword">@layer</span> base, components, utilities;
""");
    }

    [Fact]
    public void AtRule_LayerBlock()
    {
        AssertHighlighter("css",
"""
@layer base {
  p { color: red; }
}
""",
"""
<span class="hljs-keyword">@layer</span> base {
  <span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red; }
}
""");
    }

    [Fact]
    public void AtRule_LayerNested()
    {
        AssertHighlighter("css",
"""
@layer framework.base {
  p { color: red; }
}
""",
"""
<span class="hljs-keyword">@layer</span> framework.base {
  <span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red; }
}
""");
    }

    [Fact]
    public void AtRule_Scope()
    {
        AssertHighlighter("css",
"""
@scope (.card) {
  p { color: red; }
}
""",
"""
<span class="hljs-keyword">@scope</span> (.card) {
  <span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red; }
}
""");
    }

    [Fact]
    public void AtRule_ScopeTo()
    {
        AssertHighlighter("css",
"""
@scope (.card) to (.card-body) {
  p { color: red; }
}
""",
"""
<span class="hljs-keyword">@scope</span> (.card) to (.card-body) {
  <span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red; }
}
""");
    }

    [Fact]
    public void AtRule_Property()
    {
        AssertHighlighter("css",
"""
@property --primary {
  syntax: "<color>";
  inherits: true;
  initial-value: blue;
}
""",
"""
<span class="hljs-keyword">@property</span> --primary {
  syntax: <span class="hljs-string">&quot;&lt;color&gt;&quot;</span>;
  inherits: true;
  initial-value: blue;
}
""");
    }

    [Fact]
    public void AtRule_StartingStyle()
    {
        AssertHighlighter("css",
"""
@starting-style {
  div { opacity: 0; }
}
""",
"""
<span class="hljs-keyword">@starting-style</span> {
  <span class="hljs-selector-tag">div</span> { <span class="hljs-attribute">opacity</span>: <span class="hljs-number">0</span>; }
}
""");
    }

    [Fact]
    public void AtRule_PositionTry()
    {
        AssertHighlighter("css",
"""
@position-try --fallback {
  top: 0;
  left: 0;
}
""",
"""
<span class="hljs-keyword">@position-try</span> --fallback {
  <span class="hljs-attribute">top</span>: <span class="hljs-number">0</span>;
  <span class="hljs-attribute">left</span>: <span class="hljs-number">0</span>;
}
""");
    }

    [Fact]
    public void AtRule_CounterStyle()
    {
        AssertHighlighter("css",
"""
@counter-style thumbs {
  system: cyclic;
  symbols: "\1F44D";
  suffix: " ";
}
""",
"""
<span class="hljs-keyword">@counter-style</span> thumbs {
  system: cyclic;
  symbols: <span class="hljs-string">&quot;\1F44D&quot;</span>;
  suffix: <span class="hljs-string">&quot; &quot;</span>;
}
""");
    }

    [Fact]
    public void AtRule_FontPaletteValues()
    {
        AssertHighlighter("css",
"""
@font-palette-values --palette {
  font-family: "MyFont";
  base-palette: 0;
}
""",
"""
<span class="hljs-keyword">@font-palette-values</span> --palette {
  <span class="hljs-attribute">font-family</span>: <span class="hljs-string">&quot;MyFont&quot;</span>;
  base-palette: <span class="hljs-number">0</span>;
}
""");
    }

    [Fact]
    public void AtRule_ContainerSize()
    {
        AssertHighlighter("css",
"""
div { container-type: inline-size; container-name: card; }
""",
"""
<span class="hljs-selector-tag">div</span> { <span class="hljs-attribute">container-type</span>: inline-size; <span class="hljs-attribute">container-name</span>: card; }
""");
    }

    [Fact]
    public void Nesting_Simple()
    {
        AssertHighlighter("css",
"""
.card {
  color: red;
  & p { color: blue; }
}
""",
"""
<span class="hljs-selector-class">.card</span> {
  <span class="hljs-attribute">color</span>: red;
  &amp; <span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: blue; }
}
""");
    }

    [Fact]
    public void Nesting_NoAmpersand()
    {
        AssertHighlighter("css",
"""
.card {
  color: red;
  p { color: blue; }
}
""",
"""
<span class="hljs-selector-class">.card</span> {
  <span class="hljs-attribute">color</span>: red;
  <span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: blue; }
}
""");
    }

    [Fact]
    public void Nesting_PseudoNested()
    {
        AssertHighlighter("css",
"""
.btn {
  color: red;
  &:hover { color: blue; }
}
""",
"""
<span class="hljs-selector-class">.btn</span> {
  <span class="hljs-attribute">color</span>: red;
  &amp;<span class="hljs-selector-pseudo">:hover</span> { <span class="hljs-attribute">color</span>: blue; }
}
""");
    }

    [Fact]
    public void Nesting_CompoundNested()
    {
        AssertHighlighter("css",
"""
.card {
  &.active { color: red; }
  &:hover { color: blue; }
}
""",
"""
<span class="hljs-selector-class">.card</span> {
  &amp;<span class="hljs-selector-class">.active</span> { <span class="hljs-attribute">color</span>: red; }
  &amp;<span class="hljs-selector-pseudo">:hover</span> { <span class="hljs-attribute">color</span>: blue; }
}
""");
    }

    [Fact]
    public void Nesting_MultiLevelNested()
    {
        AssertHighlighter("css",
"""
.card {
  .header {
    .title { font-size: 2em; }
  }
}
""",
"""
<span class="hljs-selector-class">.card</span> {
  <span class="hljs-selector-class">.header</span> {
    <span class="hljs-selector-class">.title</span> { <span class="hljs-attribute">font-size</span>: <span class="hljs-number">2em</span>; }
  }
}
""");
    }

    [Fact]
    public void Nesting_MediaNested()
    {
        AssertHighlighter("css",
"""
.card {
  color: red;
  @media (min-width: 768px) {
    color: blue;
  }
}
""",
"""
<span class="hljs-selector-class">.card</span> {
  <span class="hljs-attribute">color</span>: red;
  <span class="hljs-keyword">@media</span> (<span class="hljs-attribute">min-width</span>: <span class="hljs-number">768px</span>) {
    <span class="hljs-attribute">color</span>: blue;
  }
}
""");
    }

    [Fact]
    public void Nesting_ContainerNested()
    {
        AssertHighlighter("css",
"""
.card {
  color: red;
  @container (min-width: 400px) {
    color: blue;
  }
}
""",
"""
<span class="hljs-selector-class">.card</span> {
  <span class="hljs-attribute">color</span>: red;
  <span class="hljs-keyword">@container</span> (<span class="hljs-attribute">min-width</span>: <span class="hljs-number">400px</span>) {
    <span class="hljs-attribute">color</span>: blue;
  }
}
""");
    }

    [Fact]
    public void Nesting_IsNested()
    {
        AssertHighlighter("css",
"""
.card {
  :is(h1, h2) { font-weight: bold; }
}
""",
"""
<span class="hljs-selector-class">.card</span> {
  <span class="hljs-selector-pseudo">:is</span>(<span class="hljs-selector-tag">h1</span>, <span class="hljs-selector-tag">h2</span>) { <span class="hljs-attribute">font-weight</span>: bold; }
}
""");
    }

    [Fact]
    public void String_DoubleQuoted()
    {
        AssertHighlighter("css",
"""
p::before { content: "hello"; }
""",
"""
<span class="hljs-selector-tag">p</span><span class="hljs-selector-pseudo">::before</span> { <span class="hljs-attribute">content</span>: <span class="hljs-string">&quot;hello&quot;</span>; }
""");
    }

    [Fact]
    public void String_SingleQuoted()
    {
        AssertHighlighter("css",
"""
p::before { content: 'hello'; }
""",
"""
<span class="hljs-selector-tag">p</span><span class="hljs-selector-pseudo">::before</span> { <span class="hljs-attribute">content</span>: <span class="hljs-string">&#x27;hello&#x27;</span>; }
""");
    }

    [Fact]
    public void String_WithEscapes()
    {
        AssertHighlighter("css",
"""
p::before { content: "line1\Aline2"; }
""",
"""
<span class="hljs-selector-tag">p</span><span class="hljs-selector-pseudo">::before</span> { <span class="hljs-attribute">content</span>: <span class="hljs-string">&quot;line1\Aline2&quot;</span>; }
""");
    }

    [Fact]
    public void String_Empty()
    {
        AssertHighlighter("css",
"""
p::before { content: ""; }
""",
"""
<span class="hljs-selector-tag">p</span><span class="hljs-selector-pseudo">::before</span> { <span class="hljs-attribute">content</span>: <span class="hljs-string">&quot;&quot;</span>; }
""");
    }

    [Fact]
    public void Comment_Block()
    {
        AssertHighlighter("css",
"""
/* hello */
""",
"""
<span class="hljs-comment">/* hello */</span>
""");
    }

    [Fact]
    public void Comment_MultiLineBlock()
    {
        AssertHighlighter("css",
"""
/*
 * hello
 */
""",
"""
<span class="hljs-comment">/*
 * hello
 */</span>
""");
    }

    [Fact]
    public void Comment_Inline()
    {
        AssertHighlighter("css",
"""
p { color: red; /* primary */ }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red; <span class="hljs-comment">/* primary */</span> }
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("css",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("css",
"""
/* just a comment */
""",
"""
<span class="hljs-comment">/* just a comment */</span>
""");
    }

    [Fact]
    public void SpecialEdge_EmptyRule()
    {
        AssertHighlighter("css",
"""
p {}
""",
"""
<span class="hljs-selector-tag">p</span> {}
""");
    }

    [Fact]
    public void SpecialEdge_NoSemicolon()
    {
        AssertHighlighter("css",
"""
p { color: red }
""",
"""
<span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red }
""");
    }
}
