namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class LessHighlighterTests
{

    [Fact]
    public void Selector_Class()
    {
        AssertHighlighter("less",
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
        AssertHighlighter("less",
"""
#foo { color: red; }
""",
"""
<span class="hljs-selector-id">#foo</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Selector_Type()
    {
        AssertHighlighter("less",
"""
div { color: red; }
""",
"""
<span class="hljs-selector-tag">div</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Selector_Multiple()
    {
        AssertHighlighter("less",
"""
h1, h2, h3 { color: red; }
""",
"""
<span class="hljs-selector-tag">h1</span>, <span class="hljs-selector-tag">h2</span>, <span class="hljs-selector-tag">h3</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Selector_Combinators()
    {
        AssertHighlighter("less",
"""
div > p + span { color: red; }
""",
"""
<span class="hljs-selector-tag">div</span> &gt; <span class="hljs-selector-tag">p</span> + <span class="hljs-selector-tag">span</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Pseudo_Hover()
    {
        AssertHighlighter("less",
"""
a:hover { color: red; }
""",
"""
<span class="hljs-selector-tag">a</span><span class="hljs-selector-pseudo">:hover</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Pseudo_Has()
    {
        AssertHighlighter("less",
"""
article:has(img) { padding: 1rem; }
""",
"""
<span class="hljs-selector-tag">article</span><span class="hljs-selector-pseudo">:has</span>(img) { <span class="hljs-attribute">padding</span>: <span class="hljs-number">1rem</span>; }
""");
    }

    [Fact]
    public void Pseudo_Is()
    {
        AssertHighlighter("less",
"""
:is(h1, h2) { font-weight: bold; }
""",
"""
<span class="hljs-selector-pseudo">:is</span>(h1, h2) { <span class="hljs-attribute">font-weight</span>: bold; }
""");
    }

    [Fact]
    public void PseudoElement_Before()
    {
        AssertHighlighter("less",
"""
a::before { content: ">"; }
""",
"""
<span class="hljs-selector-tag">a</span><span class="hljs-selector-pseudo">::before</span> { <span class="hljs-attribute">content</span>: <span class="hljs-string">&quot;&gt;&quot;</span>; }
""");
    }

    [Fact]
    public void PseudoElement_Placeholder()
    {
        AssertHighlighter("less",
"""
input::placeholder { color: gray; }
""",
"""
<span class="hljs-selector-tag">input</span><span class="hljs-selector-pseudo">::placeholder</span> { <span class="hljs-attribute">color</span>: gray; }
""");
    }

    [Fact]
    public void Variable_Declare()
    {
        AssertHighlighter("less",
"""
@primary: blue;
""",
"""
<span class="hljs-variable">@primary:</span> blue;
""");
    }

    [Fact]
    public void Variable_Use()
    {
        AssertHighlighter("less",
"""
.a { color: @primary; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-variable">@primary</span>; }
""");
    }

    [Fact]
    public void Variable_String()
    {
        AssertHighlighter("less",
"""
@name: "alice";
""",
"""
<span class="hljs-variable">@name:</span> <span class="hljs-string">&quot;alice&quot;</span>;
""");
    }

    [Fact]
    public void Variable_Number()
    {
        AssertHighlighter("less",
"""
@size: 16px;
""",
"""
<span class="hljs-variable">@size:</span> <span class="hljs-number">16px</span>;
""");
    }

    [Fact]
    public void Variable_List()
    {
        AssertHighlighter("less",
"""
@palette: red, green, blue;
""",
"""
<span class="hljs-variable">@palette:</span> red, green, blue;
""");
    }

    [Fact]
    public void Variable_Map()
    {
        AssertHighlighter("less",
"""
@colors: { primary: blue; secondary: red; };
""",
"""
<span class="hljs-variable">@colors:</span> { primary: blue; secondary: red; };
""");
    }

    [Fact]
    public void Variable_MapAccess()
    {
        AssertHighlighter("less",
"""
.a { color: @colors[primary]; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-variable">@colors</span>[primary]; }
""");
    }

    [Fact]
    public void Variable_AtAtVariable()
    {
        AssertHighlighter("less",
"""
@var: "name";
@@var: red;
""",
"""
<span class="hljs-variable">@var:</span> <span class="hljs-string">&quot;name&quot;</span>;
@<span class="hljs-variable">@var:</span> red;
""");
    }

    [Fact]
    public void Variable_PropertyAccess()
    {
        AssertHighlighter("less",
"""
.a { color: red; b { color: $color; } }
""",
"""
.a { color: red; b { color: $color; } }
""");
    }

    [Fact]
    public void Interpolation_Selector()
    {
        AssertHighlighter("less",
"""
@name: foo;
.@{name} { color: red; }
""",
"""
<span class="hljs-variable">@name:</span> foo;
<span class="hljs-selector-class">.@{name}</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Interpolation_Property()
    {
        AssertHighlighter("less",
"""
@side: top;
.a { margin-@{side}: 10px; }
""",
"""
<span class="hljs-variable">@side:</span> top;
<span class="hljs-selector-class">.a</span> { <span class="hljs-selector-tag">margin-</span><span class="hljs-variable">@{side}</span>: <span class="hljs-number">10px</span>; }
""");
    }

    [Fact]
    public void Interpolation_Url()
    {
        AssertHighlighter("less",
"""
@base: "https://cdn.example.com";
.a { background: url("@{base}/bg.png"); }
""",
"""
<span class="hljs-variable">@base:</span> <span class="hljs-string">&quot;https://cdn.example.com&quot;</span>;
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">background</span>: url(<span class="hljs-string">&quot;@{base}/bg.png&quot;</span>); }
""");
    }

    [Fact]
    public void Interpolation_String()
    {
        AssertHighlighter("less",
"""
@name: foo;
.a::before { content: "hello @{name}"; }
""",
"""
<span class="hljs-variable">@name:</span> foo;
<span class="hljs-selector-class">.a</span><span class="hljs-selector-pseudo">::before</span> { <span class="hljs-attribute">content</span>: <span class="hljs-string">&quot;hello @{name}&quot;</span>; }
""");
    }

    [Fact]
    public void Interpolation_MultiLevel()
    {
        AssertHighlighter("less",
"""
@prefix: my; @name: btn;
.@{prefix}-@{name} { color: red; }
""",
"""
<span class="hljs-variable">@prefix:</span> my; <span class="hljs-variable">@name:</span> btn;
<span class="hljs-selector-class">.@{prefix}</span><span class="hljs-selector-tag">-</span><span class="hljs-variable">@{name}</span> { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Mixin_Define()
    {
        AssertHighlighter("less",
"""
.rounded {
  border-radius: 5px;
}
""",
"""
<span class="hljs-selector-class">.rounded</span> {
  <span class="hljs-attribute">border-radius</span>: <span class="hljs-number">5px</span>;
}
""");
    }

    [Fact]
    public void Mixin_Use()
    {
        AssertHighlighter("less",
"""
.btn {
  .rounded;
  color: red;
}
""",
"""
<span class="hljs-selector-class">.btn</span> {
  <span class="hljs-selector-class">.rounded</span>;
  <span class="hljs-attribute">color</span>: red;
}
""");
    }

    [Fact]
    public void Mixin_ParameterizedDefine()
    {
        AssertHighlighter("less",
"""
.rounded(@radius) {
  border-radius: @radius;
}
""",
"""
<span class="hljs-selector-class">.rounded</span>(<span class="hljs-variable">@radius</span>) {
  <span class="hljs-attribute">border-radius</span>: <span class="hljs-variable">@radius</span>;
}
""");
    }

    [Fact]
    public void Mixin_ParameterizedUse()
    {
        AssertHighlighter("less",
"""
.btn {
  .rounded(10px);
}
""",
"""
<span class="hljs-selector-class">.btn</span> {
  <span class="hljs-selector-class">.rounded</span>(<span class="hljs-number">10px</span>);
}
""");
    }

    [Fact]
    public void Mixin_DefaultValue()
    {
        AssertHighlighter("less",
"""
.rounded(@radius: 5px) {
  border-radius: @radius;
}
""",
"""
<span class="hljs-selector-class">.rounded</span>(<span class="hljs-variable">@radius</span>: <span class="hljs-number">5px</span>) {
  <span class="hljs-attribute">border-radius</span>: <span class="hljs-variable">@radius</span>;
}
""");
    }

    [Fact]
    public void Mixin_NamedArgs()
    {
        AssertHighlighter("less",
"""
.btn {
  .rounded(@radius: 10px);
}
""",
"""
<span class="hljs-selector-class">.btn</span> {
  <span class="hljs-selector-class">.rounded</span>(<span class="hljs-variable">@radius</span>: <span class="hljs-number">10px</span>);
}
""");
    }

    [Fact]
    public void Mixin_MultipleParams()
    {
        AssertHighlighter("less",
"""
.box(@width, @height) {
  width: @width;
  height: @height;
}
""",
"""
<span class="hljs-selector-class">.box</span>(<span class="hljs-variable">@width</span>, <span class="hljs-variable">@height</span>) {
  <span class="hljs-attribute">width</span>: <span class="hljs-variable">@width</span>;
  <span class="hljs-attribute">height</span>: <span class="hljs-variable">@height</span>;
}
""");
    }

    [Fact]
    public void Mixin_RestParams()
    {
        AssertHighlighter("less",
"""
.shadow(@args...) {
  box-shadow: @args;
}
""",
"""
<span class="hljs-selector-class">.shadow</span>(<span class="hljs-variable">@args</span>...) {
  <span class="hljs-attribute">box-shadow</span>: <span class="hljs-variable">@args</span>;
}
""");
    }

    [Fact]
    public void Mixin_WhenGuard()
    {
        AssertHighlighter("less",
"""
.mixin(@a) when (@a > 0) {
  color: red;
}
""",
"""
<span class="hljs-selector-class">.mixin</span>(<span class="hljs-variable">@a</span>) <span class="hljs-keyword">when</span> (<span class="hljs-variable">@a</span> &gt; <span class="hljs-number">0</span>) {
  <span class="hljs-attribute">color</span>: red;
}
""");
    }

    [Fact]
    public void Mixin_WhenNot()
    {
        AssertHighlighter("less",
"""
.mixin(@a) when not (@a = 0) {
  color: red;
}
""",
"""
<span class="hljs-selector-class">.mixin</span>(<span class="hljs-variable">@a</span>) <span class="hljs-keyword">when</span> <span class="hljs-keyword">not</span> (<span class="hljs-variable">@a</span> = <span class="hljs-number">0</span>) {
  <span class="hljs-attribute">color</span>: red;
}
""");
    }

    [Fact]
    public void Mixin_WhenDefault()
    {
        AssertHighlighter("less",
"""
.mixin(@a) when (default()) {
  color: gray;
}
""",
"""
<span class="hljs-selector-class">.mixin</span>(<span class="hljs-variable">@a</span>) <span class="hljs-keyword">when</span> (<span class="hljs-built_in">default</span>()) {
  <span class="hljs-attribute">color</span>: gray;
}
""");
    }

    [Fact]
    public void Mixin_NamespaceCall()
    {
        AssertHighlighter("less",
"""
#namespace > .mixin(10px);
""",
"""
<span class="hljs-selector-id">#namespace</span> &gt; <span class="hljs-selector-class">.mixin</span>(<span class="hljs-number">10px</span>);
""");
    }

    [Fact]
    public void Mixin_CallParens()
    {
        AssertHighlighter("less",
"""
.btn {
  .rounded();
}
""",
"""
<span class="hljs-selector-class">.btn</span> {
  <span class="hljs-selector-class">.rounded</span>();
}
""");
    }

    [Fact]
    public void ParentSelector_Hover()
    {
        AssertHighlighter("less",
"""
.btn {
  color: red;
  &:hover { color: blue; }
}
""",
"""
<span class="hljs-selector-class">.btn</span> {
  <span class="hljs-attribute">color</span>: red;
  <span class="hljs-selector-tag">&amp;</span><span class="hljs-selector-pseudo">:hover</span> { <span class="hljs-attribute">color</span>: blue; }
}
""");
    }

    [Fact]
    public void ParentSelector_CompoundClass()
    {
        AssertHighlighter("less",
"""
.btn {
  &.active { color: red; }
}
""",
"""
<span class="hljs-selector-class">.btn</span> {
  <span class="hljs-selector-tag">&amp;</span><span class="hljs-selector-class">.active</span> { <span class="hljs-attribute">color</span>: red; }
}
""");
    }

    [Fact]
    public void ParentSelector_Suffix()
    {
        AssertHighlighter("less",
"""
.btn {
  &-primary { background: blue; }
}
""",
"""
<span class="hljs-selector-class">.btn</span> {
  <span class="hljs-selector-tag">&amp;</span><span class="hljs-selector-tag">-primary</span> { <span class="hljs-attribute">background</span>: blue; }
}
""");
    }

    [Fact]
    public void ParentSelector_Multiple()
    {
        AssertHighlighter("less",
"""
.a, .b {
  & + & { margin-top: 1rem; }
}
""",
"""
<span class="hljs-selector-class">.a</span>, <span class="hljs-selector-class">.b</span> {
  <span class="hljs-selector-tag">&amp;</span> + <span class="hljs-selector-tag">&amp;</span> { <span class="hljs-attribute">margin-top</span>: <span class="hljs-number">1rem</span>; }
}
""");
    }

    [Fact]
    public void ParentSelector_NestedDeep()
    {
        AssertHighlighter("less",
"""
.card {
  .header {
    & .title { font-size: 2em; }
  }
}
""",
"""
<span class="hljs-selector-class">.card</span> {
  <span class="hljs-selector-class">.header</span> {
    <span class="hljs-selector-tag">&amp;</span> <span class="hljs-selector-class">.title</span> { <span class="hljs-attribute">font-size</span>: <span class="hljs-number">2em</span>; }
  }
}
""");
    }

    [Fact]
    public void Operation_Add()
    {
        AssertHighlighter("less",
"""
.a { width: 10px + 5px; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">10px</span> + <span class="hljs-number">5px</span>; }
""");
    }

    [Fact]
    public void Operation_Subtract()
    {
        AssertHighlighter("less",
"""
.a { width: 10px - 5px; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">10px</span> - <span class="hljs-number">5px</span>; }
""");
    }

    [Fact]
    public void Operation_Multiply()
    {
        AssertHighlighter("less",
"""
.a { width: 10px * 2; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">10px</span> * <span class="hljs-number">2</span>; }
""");
    }

    [Fact]
    public void Operation_Divide()
    {
        AssertHighlighter("less",
"""
.a { width: 10px / 2; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">10px</span> / <span class="hljs-number">2</span>; }
""");
    }

    [Fact]
    public void Operation_WithVariable()
    {
        AssertHighlighter("less",
"""
@base: 16px;
.a { font-size: @base * 1.5; }
""",
"""
<span class="hljs-variable">@base:</span> <span class="hljs-number">16px</span>;
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">font-size</span>: <span class="hljs-variable">@base</span> * <span class="hljs-number">1.5</span>; }
""");
    }

    [Fact]
    public void Operation_ColorAdd()
    {
        AssertHighlighter("less",
"""
.a { color: #888 + #111; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-number">#888</span> + <span class="hljs-number">#111</span>; }
""");
    }

    [Fact]
    public void Operation_Unit()
    {
        AssertHighlighter("less",
"""
.a { width: unit(10, px); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-built_in">unit</span>(<span class="hljs-number">10</span>, px); }
""");
    }

    [Fact]
    public void BuiltinFunction_Lighten()
    {
        AssertHighlighter("less",
"""
.a { color: lighten(#000, 20%); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">lighten</span>(<span class="hljs-number">#000</span>, <span class="hljs-number">20%</span>); }
""");
    }

    [Fact]
    public void BuiltinFunction_Darken()
    {
        AssertHighlighter("less",
"""
.a { color: darken(#fff, 20%); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">darken</span>(<span class="hljs-number">#fff</span>, <span class="hljs-number">20%</span>); }
""");
    }

    [Fact]
    public void BuiltinFunction_Saturate()
    {
        AssertHighlighter("less",
"""
.a { color: saturate(#888, 50%); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">saturate</span>(<span class="hljs-number">#888</span>, <span class="hljs-number">50%</span>); }
""");
    }

    [Fact]
    public void BuiltinFunction_Fade()
    {
        AssertHighlighter("less",
"""
.a { color: fade(#000, 50%); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">fade</span>(<span class="hljs-number">#000</span>, <span class="hljs-number">50%</span>); }
""");
    }

    [Fact]
    public void BuiltinFunction_Mix()
    {
        AssertHighlighter("less",
"""
.a { color: mix(#f00, #00f, 50%); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">mix</span>(<span class="hljs-number">#f00</span>, <span class="hljs-number">#00f</span>, <span class="hljs-number">50%</span>); }
""");
    }

    [Fact]
    public void BuiltinFunction_Percentage()
    {
        AssertHighlighter("less",
"""
.a { width: percentage(0.5); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-built_in">percentage</span>(<span class="hljs-number">0.5</span>); }
""");
    }

    [Fact]
    public void BuiltinFunction_Round()
    {
        AssertHighlighter("less",
"""
.a { width: round(1.6px); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-built_in">round</span>(<span class="hljs-number">1.6px</span>); }
""");
    }

    [Fact]
    public void BuiltinFunction_Ceil()
    {
        AssertHighlighter("less",
"""
.a { width: ceil(1.2px); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-built_in">ceil</span>(<span class="hljs-number">1.2px</span>); }
""");
    }

    [Fact]
    public void BuiltinFunction_Floor()
    {
        AssertHighlighter("less",
"""
.a { width: floor(1.8px); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-built_in">floor</span>(<span class="hljs-number">1.8px</span>); }
""");
    }

    [Fact]
    public void BuiltinFunction_IfFn()
    {
        AssertHighlighter("less",
"""
.a { color: if((1 < 2), red, blue); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">if</span>((<span class="hljs-number">1</span> &lt; <span class="hljs-number">2</span>), red, blue); }
""");
    }

    [Fact]
    public void AtRule_Import()
    {
        AssertHighlighter("less",
"""
@import "base.less";
""",
"""
<span class="hljs-keyword">@import</span> <span class="hljs-string">&quot;base.less&quot;</span>;
""");
    }

    [Fact]
    public void AtRule_ImportReference()
    {
        AssertHighlighter("less",
"""
@import (reference) "base.less";
""",
"""
<span class="hljs-keyword">@import</span> (reference) <span class="hljs-string">&quot;base.less&quot;</span>;
""");
    }

    [Fact]
    public void AtRule_ImportOnce()
    {
        AssertHighlighter("less",
"""
@import (once) "base.less";
""",
"""
<span class="hljs-keyword">@import</span> (once) <span class="hljs-string">&quot;base.less&quot;</span>;
""");
    }

    [Fact]
    public void AtRule_ImportOptional()
    {
        AssertHighlighter("less",
"""
@import (optional) "missing.less";
""",
"""
<span class="hljs-keyword">@import</span> (optional) <span class="hljs-string">&quot;missing.less&quot;</span>;
""");
    }

    [Fact]
    public void AtRule_Media()
    {
        AssertHighlighter("less",
"""
@media (min-width: 768px) {
  .a { color: red; }
}
""",
"""
<span class="hljs-keyword">@media</span> (<span class="hljs-attribute">min-width</span>: <span class="hljs-number">768px</span>) {
  <span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: red; }
}
""");
    }

    [Fact]
    public void AtRule_Supports()
    {
        AssertHighlighter("less",
"""
@supports (display: grid) {
  .a { display: grid; }
}
""",
"""
<span class="hljs-keyword">@supports</span> (<span class="hljs-attribute">display</span>: <span class="hljs-attribute">grid</span>) {
  <span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">display</span>: grid; }
}
""");
    }

    [Fact]
    public void AtRule_Container()
    {
        AssertHighlighter("less",
"""
@container (min-width: 400px) {
  .a { color: red; }
}
""",
"""
<span class="hljs-variable">@container</span> (<span class="hljs-attribute">min-width</span>: <span class="hljs-number">400px</span>) {
  <span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: red; }
}
""");
    }

    [Fact]
    public void AtRule_Keyframes()
    {
        AssertHighlighter("less",
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
    public void AtRule_FontFace()
    {
        AssertHighlighter("less",
"""
@font-face {
  font-family: "MyFont";
  src: url("font.woff2");
}
""",
"""
<span class="hljs-keyword">@font-face</span> {
  <span class="hljs-attribute">font-family</span>: <span class="hljs-string">&quot;MyFont&quot;</span>;
  <span class="hljs-attribute">src</span>: url(<span class="hljs-string">&quot;font.woff2&quot;</span>);
}
""");
    }

    [Fact]
    public void String_DoubleQuoted()
    {
        AssertHighlighter("less",
"""
.a::before { content: "hello"; }
""",
"""
<span class="hljs-selector-class">.a</span><span class="hljs-selector-pseudo">::before</span> { <span class="hljs-attribute">content</span>: <span class="hljs-string">&quot;hello&quot;</span>; }
""");
    }

    [Fact]
    public void String_SingleQuoted()
    {
        AssertHighlighter("less",
"""
.a::before { content: 'hello'; }
""",
"""
<span class="hljs-selector-class">.a</span><span class="hljs-selector-pseudo">::before</span> { <span class="hljs-attribute">content</span>: <span class="hljs-string">&#x27;hello&#x27;</span>; }
""");
    }

    [Fact]
    public void String_Escaped()
    {
        AssertHighlighter("less",
"""
.a { filter: ~"alpha(opacity=50)"; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">filter</span>: <span class="hljs-string">~&quot;alpha(opacity=50)&quot;</span>; }
""");
    }

    [Fact]
    public void Comment_Line()
    {
        AssertHighlighter("less",
"""
// line comment
""",
"""
<span class="hljs-comment">// line comment</span>
""");
    }

    [Fact]
    public void Comment_Block()
    {
        AssertHighlighter("less",
"""
/* block comment */
""",
"""
<span class="hljs-comment">/* block comment */</span>
""");
    }

    [Fact]
    public void Comment_MultiLine()
    {
        AssertHighlighter("less",
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
    public void Nesting_Simple()
    {
        AssertHighlighter("less",
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
    public void Nesting_MultiLevel()
    {
        AssertHighlighter("less",
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
        AssertHighlighter("less",
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
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("less",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("less",
"""
// just a comment
""",
"""
<span class="hljs-comment">// just a comment</span>
""");
    }

    [Fact]
    public void SpecialEdge_EmptyRule()
    {
        AssertHighlighter("less",
"""
.a {}
""",
"""
<span class="hljs-selector-class">.a</span> {}
""");
    }

    [Fact]
    public void SpecialEdge_StandaloneVar()
    {
        AssertHighlighter("less",
"""
@x: 1;
""",
"""
<span class="hljs-variable">@x:</span> <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Unit_Dvh()
    {
        AssertHighlighter("less",
"""
.a { height: 100dvh; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">height</span>: <span class="hljs-number">100dvh</span>; }
""");
    }

    [Fact]
    public void Unit_Dvw()
    {
        AssertHighlighter("less",
"""
.a { width: 100dvw; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">100dvw</span>; }
""");
    }

    [Fact]
    public void Unit_Svh()
    {
        AssertHighlighter("less",
"""
.a { height: 100svh; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">height</span>: <span class="hljs-number">100svh</span>; }
""");
    }

    [Fact]
    public void Unit_Svw()
    {
        AssertHighlighter("less",
"""
.a { width: 100svw; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">100svw</span>; }
""");
    }

    [Fact]
    public void Unit_Lvh()
    {
        AssertHighlighter("less",
"""
.a { height: 100lvh; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">height</span>: <span class="hljs-number">100lvh</span>; }
""");
    }

    [Fact]
    public void Unit_Lvw()
    {
        AssertHighlighter("less",
"""
.a { width: 100lvw; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">100lvw</span>; }
""");
    }

    [Fact]
    public void Unit_Cqw()
    {
        AssertHighlighter("less",
"""
.a { width: 50cqw; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">50cqw</span>; }
""");
    }

    [Fact]
    public void Unit_Cqh()
    {
        AssertHighlighter("less",
"""
.a { height: 50cqh; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">height</span>: <span class="hljs-number">50cqh</span>; }
""");
    }

    [Fact]
    public void Unit_Cqi()
    {
        AssertHighlighter("less",
"""
.a { width: 50cqi; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">50cqi</span>; }
""");
    }

    [Fact]
    public void Unit_Cqb()
    {
        AssertHighlighter("less",
"""
.a { height: 50cqb; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">height</span>: <span class="hljs-number">50cqb</span>; }
""");
    }

    [Fact]
    public void Unit_Cqmin()
    {
        AssertHighlighter("less",
"""
.a { width: 50cqmin; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">50cqmin</span>; }
""");
    }

    [Fact]
    public void Unit_Cqmax()
    {
        AssertHighlighter("less",
"""
.a { width: 50cqmax; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">50cqmax</span>; }
""");
    }

    [Fact]
    public void Unit_Fr()
    {
        AssertHighlighter("less",
"""
.grid { grid-template-columns: 1fr 2fr; }
""",
"""
<span class="hljs-selector-class">.grid</span> { <span class="hljs-attribute">grid-template-columns</span>: <span class="hljs-number">1fr</span> <span class="hljs-number">2fr</span>; }
""");
    }

    [Fact]
    public void Unit_Lh()
    {
        AssertHighlighter("less",
"""
.a { height: 2lh; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">height</span>: <span class="hljs-number">2lh</span>; }
""");
    }
}
