namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class ScssHighlighterTests
{

    [Fact]
    public void Selector_Class()
    {
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
"""
$primary: blue;
""",
"""
<span class="hljs-variable">$primary</span>: blue;
""");
    }

    [Fact]
    public void Variable_Use()
    {
        AssertHighlighter("scss",
"""
.a { color: $primary; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-variable">$primary</span>; }
""");
    }

    [Fact]
    public void Variable_String()
    {
        AssertHighlighter("scss",
"""
$name: "alice";
""",
"""
<span class="hljs-variable">$name</span>: <span class="hljs-string">&quot;alice&quot;</span>;
""");
    }

    [Fact]
    public void Variable_Number()
    {
        AssertHighlighter("scss",
"""
$size: 16px;
""",
"""
<span class="hljs-variable">$size</span>: <span class="hljs-number">16px</span>;
""");
    }

    [Fact]
    public void Variable_List()
    {
        AssertHighlighter("scss",
"""
$palette: red, green, blue;
""",
"""
<span class="hljs-variable">$palette</span>: red, green, blue;
""");
    }

    [Fact]
    public void Variable_ListBrackets()
    {
        AssertHighlighter("scss",
"""
$palette: [red green blue];
""",
"""
<span class="hljs-variable">$palette</span>: [red green blue];
""");
    }

    [Fact]
    public void Variable_Map()
    {
        AssertHighlighter("scss",
"""
$colors: (primary: blue, secondary: red);
""",
"""
<span class="hljs-variable">$colors</span>: (primary: blue, secondary: red);
""");
    }

    [Fact]
    public void Variable_Default()
    {
        AssertHighlighter("scss",
"""
$primary: blue !default;
""",
"""
<span class="hljs-variable">$primary</span>: blue !default;
""");
    }

    [Fact]
    public void Variable_Global()
    {
        AssertHighlighter("scss",
"""
.a { $local: red !global; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-variable">$local</span>: red !global; }
""");
    }

    [Fact]
    public void Interpolation_Selector()
    {
        AssertHighlighter("scss",
"""
$name: foo;
.#{$name} { color: red; }
""",
"""
<span class="hljs-variable">$name</span>: foo;
.#{<span class="hljs-variable">$name</span>} { <span class="hljs-attribute">color</span>: red; }
""");
    }

    [Fact]
    public void Interpolation_Property()
    {
        AssertHighlighter("scss",
"""
$side: top;
.a { margin-#{$side}: 10px; }
""",
"""
<span class="hljs-variable">$side</span>: top;
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">margin</span>-#{<span class="hljs-variable">$side</span>}: <span class="hljs-number">10px</span>; }
""");
    }

    [Fact]
    public void Interpolation_PropertyName()
    {
        AssertHighlighter("scss",
"""
$prop: color;
.a { #{$prop}: red; }
""",
"""
<span class="hljs-variable">$prop</span>: color;
<span class="hljs-selector-class">.a</span> { #{<span class="hljs-variable">$prop</span>}: red; }
""");
    }

    [Fact]
    public void Interpolation_String()
    {
        AssertHighlighter("scss",
"""
$name: foo;
.a::before { content: "hello #{$name}"; }
""",
"""
<span class="hljs-variable">$name</span>: foo;
<span class="hljs-selector-class">.a</span><span class="hljs-selector-pseudo">::before</span> { <span class="hljs-attribute">content</span>: <span class="hljs-string">&quot;hello #{$name}&quot;</span>; }
""");
    }

    [Fact]
    public void Interpolation_Url()
    {
        AssertHighlighter("scss",
"""
$base: "https://cdn.example.com";
.a { background: url("#{$base}/bg.png"); }
""",
"""
<span class="hljs-variable">$base</span>: <span class="hljs-string">&quot;https://cdn.example.com&quot;</span>;
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">background</span>: <span class="hljs-built_in">url</span>(<span class="hljs-string">&quot;#{$base}/bg.png&quot;</span>); }
""");
    }

    [Fact]
    public void Interpolation_Expression()
    {
        AssertHighlighter("scss",
"""
.a { width: #{10 + 5}px; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: #{<span class="hljs-number">10</span> + <span class="hljs-number">5</span>}px; }
""");
    }

    [Fact]
    public void ParentSelector_Hover()
    {
        AssertHighlighter("scss",
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
    public void ParentSelector_CompoundClass()
    {
        AssertHighlighter("scss",
"""
.btn {
  &.active { color: red; }
}
""",
"""
<span class="hljs-selector-class">.btn</span> {
  &amp;<span class="hljs-selector-class">.active</span> { <span class="hljs-attribute">color</span>: red; }
}
""");
    }

    [Fact]
    public void ParentSelector_Suffix()
    {
        AssertHighlighter("scss",
"""
.btn {
  &-primary { background: blue; }
}
""",
"""
<span class="hljs-selector-class">.btn</span> {
  &amp;-primary { <span class="hljs-attribute">background</span>: blue; }
}
""");
    }

    [Fact]
    public void ParentSelector_NestedDeep()
    {
        AssertHighlighter("scss",
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
    &amp; <span class="hljs-selector-class">.title</span> { <span class="hljs-attribute">font-size</span>: <span class="hljs-number">2em</span>; }
  }
}
""");
    }

    [Fact]
    public void ParentSelector_BemElement()
    {
        AssertHighlighter("scss",
"""
.block {
  &__element { color: red; }
  &--modifier { font-weight: bold; }
}
""",
"""
<span class="hljs-selector-class">.block</span> {
  &amp;__element { <span class="hljs-attribute">color</span>: red; }
  &amp;<span class="hljs-attr">--modifier</span> { <span class="hljs-attribute">font-weight</span>: bold; }
}
""");
    }

    [Fact]
    public void Mixin_DefineSimple()
    {
        AssertHighlighter("scss",
"""
@mixin rounded {
  border-radius: 5px;
}
""",
"""
<span class="hljs-keyword">@mixin</span> rounded {
  <span class="hljs-attribute">border-radius</span>: <span class="hljs-number">5px</span>;
}
""");
    }

    [Fact]
    public void Mixin_Include()
    {
        AssertHighlighter("scss",
"""
.btn {
  @include rounded;
}
""",
"""
<span class="hljs-selector-class">.btn</span> {
  <span class="hljs-keyword">@include</span> rounded;
}
""");
    }

    [Fact]
    public void Mixin_IncludeParens()
    {
        AssertHighlighter("scss",
"""
.btn {
  @include rounded();
}
""",
"""
<span class="hljs-selector-class">.btn</span> {
  <span class="hljs-keyword">@include</span> rounded();
}
""");
    }

    [Fact]
    public void Mixin_DefineParams()
    {
        AssertHighlighter("scss",
"""
@mixin rounded($radius) {
  border-radius: $radius;
}
""",
"""
<span class="hljs-keyword">@mixin</span> rounded(<span class="hljs-variable">$radius</span>) {
  <span class="hljs-attribute">border-radius</span>: <span class="hljs-variable">$radius</span>;
}
""");
    }

    [Fact]
    public void Mixin_IncludeParams()
    {
        AssertHighlighter("scss",
"""
.btn {
  @include rounded(10px);
}
""",
"""
<span class="hljs-selector-class">.btn</span> {
  <span class="hljs-keyword">@include</span> rounded(<span class="hljs-number">10px</span>);
}
""");
    }

    [Fact]
    public void Mixin_DefaultValue()
    {
        AssertHighlighter("scss",
"""
@mixin rounded($radius: 5px) {
  border-radius: $radius;
}
""",
"""
<span class="hljs-keyword">@mixin</span> rounded(<span class="hljs-variable">$radius</span>: <span class="hljs-number">5px</span>) {
  <span class="hljs-attribute">border-radius</span>: <span class="hljs-variable">$radius</span>;
}
""");
    }

    [Fact]
    public void Mixin_NamedArgs()
    {
        AssertHighlighter("scss",
"""
.btn {
  @include rounded($radius: 10px);
}
""",
"""
<span class="hljs-selector-class">.btn</span> {
  <span class="hljs-keyword">@include</span> rounded(<span class="hljs-variable">$radius</span>: <span class="hljs-number">10px</span>);
}
""");
    }

    [Fact]
    public void Mixin_RestParams()
    {
        AssertHighlighter("scss",
"""
@mixin shadow($shadows...) {
  box-shadow: $shadows;
}
""",
"""
<span class="hljs-keyword">@mixin</span> shadow(<span class="hljs-variable">$shadows</span>...) {
  <span class="hljs-attribute">box-shadow</span>: <span class="hljs-variable">$shadows</span>;
}
""");
    }

    [Fact]
    public void Mixin_Content()
    {
        AssertHighlighter("scss",
"""
@mixin hover {
  &:hover { @content; }
}
""",
"""
<span class="hljs-keyword">@mixin</span> <span class="hljs-attribute">hover</span> {
  &amp;<span class="hljs-selector-pseudo">:hover</span> { <span class="hljs-keyword">@content</span>; }
}
""");
    }

    [Fact]
    public void Mixin_UseContent()
    {
        AssertHighlighter("scss",
"""
.btn {
  @include hover { color: red; }
}
""",
"""
<span class="hljs-selector-class">.btn</span> {
  <span class="hljs-keyword">@include</span> <span class="hljs-attribute">hover</span> { <span class="hljs-attribute">color</span>: red; }
}
""");
    }

    [Fact]
    public void Function_Define()
    {
        AssertHighlighter("scss",
"""
@function double($x) {
  @return $x * 2;
}
""",
"""
<span class="hljs-keyword">@function</span> double(<span class="hljs-variable">$x</span>) {
  <span class="hljs-keyword">@return</span> <span class="hljs-variable">$x</span> * <span class="hljs-number">2</span>;
}
""");
    }

    [Fact]
    public void Function_Call()
    {
        AssertHighlighter("scss",
"""
.a { width: double(10px); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-built_in">double</span>(<span class="hljs-number">10px</span>); }
""");
    }

    [Fact]
    public void Function_MultiParam()
    {
        AssertHighlighter("scss",
"""
@function add($a, $b) {
  @return $a + $b;
}
""",
"""
<span class="hljs-keyword">@function</span> add(<span class="hljs-variable">$a</span>, <span class="hljs-variable">$b</span>) {
  <span class="hljs-keyword">@return</span> <span class="hljs-variable">$a</span> + <span class="hljs-variable">$b</span>;
}
""");
    }

    [Fact]
    public void Function_DefaultParam()
    {
        AssertHighlighter("scss",
"""
@function pad($x: 10px) {
  @return $x * 2;
}
""",
"""
<span class="hljs-keyword">@function</span> pad(<span class="hljs-variable">$x</span>: <span class="hljs-number">10px</span>) {
  <span class="hljs-keyword">@return</span> <span class="hljs-variable">$x</span> * <span class="hljs-number">2</span>;
}
""");
    }

    [Fact]
    public void Extend_Class()
    {
        AssertHighlighter("scss",
"""
.btn-primary {
  @extend .btn;
  background: blue;
}
""",
"""
<span class="hljs-selector-class">.btn-primary</span> {
  <span class="hljs-keyword">@extend</span> .btn;
  <span class="hljs-attribute">background</span>: blue;
}
""");
    }

    [Fact]
    public void Extend_Placeholder()
    {
        AssertHighlighter("scss",
"""
%base {
  padding: 1rem;
}
.btn { @extend %base; }
""",
"""
%base {
  <span class="hljs-attribute">padding</span>: <span class="hljs-number">1rem</span>;
}
<span class="hljs-selector-class">.btn</span> { <span class="hljs-keyword">@extend</span> %base; }
""");
    }

    [Fact]
    public void Extend_OptionalExtend()
    {
        AssertHighlighter("scss",
"""
.btn { @extend .missing !optional; }
""",
"""
<span class="hljs-selector-class">.btn</span> { <span class="hljs-keyword">@extend</span> .missing !optional; }
""");
    }

    [Fact]
    public void Placeholder_Define()
    {
        AssertHighlighter("scss",
"""
%base {
  padding: 1rem;
  margin: 0;
}
""",
"""
%base {
  <span class="hljs-attribute">padding</span>: <span class="hljs-number">1rem</span>;
  <span class="hljs-attribute">margin</span>: <span class="hljs-number">0</span>;
}
""");
    }

    [Fact]
    public void Placeholder_Use()
    {
        AssertHighlighter("scss",
"""
.btn { @extend %base; }
""",
"""
<span class="hljs-selector-class">.btn</span> { <span class="hljs-keyword">@extend</span> %base; }
""");
    }

    [Fact]
    public void ControlFlow_IfSimple()
    {
        AssertHighlighter("scss",
"""
@if $theme == dark {
  body { background: black; }
}
""",
"""
<span class="hljs-keyword">@if</span> <span class="hljs-variable">$theme</span> == dark {
  <span class="hljs-selector-tag">body</span> { <span class="hljs-attribute">background</span>: black; }
}
""");
    }

    [Fact]
    public void ControlFlow_IfElse()
    {
        AssertHighlighter("scss",
"""
@if $theme == dark {
  body { background: black; }
} @else {
  body { background: white; }
}
""",
"""
<span class="hljs-keyword">@if</span> <span class="hljs-variable">$theme</span> == dark {
  <span class="hljs-selector-tag">body</span> { <span class="hljs-attribute">background</span>: black; }
} <span class="hljs-keyword">@else</span> {
  <span class="hljs-selector-tag">body</span> { <span class="hljs-attribute">background</span>: white; }
}
""");
    }

    [Fact]
    public void ControlFlow_IfElseIf()
    {
        AssertHighlighter("scss",
"""
@if $theme == dark {
  body { background: black; }
} @else if $theme == light {
  body { background: white; }
} @else {
  body { background: gray; }
}
""",
"""
<span class="hljs-keyword">@if</span> <span class="hljs-variable">$theme</span> == dark {
  <span class="hljs-selector-tag">body</span> { <span class="hljs-attribute">background</span>: black; }
} <span class="hljs-keyword">@else</span> if <span class="hljs-variable">$theme</span> == light {
  <span class="hljs-selector-tag">body</span> { <span class="hljs-attribute">background</span>: white; }
} <span class="hljs-keyword">@else</span> {
  <span class="hljs-selector-tag">body</span> { <span class="hljs-attribute">background</span>: gray; }
}
""");
    }

    [Fact]
    public void ControlFlow_ForThrough()
    {
        AssertHighlighter("scss",
"""
@for $i from 1 through 5 {
  .col-#{$i} { width: 20% * $i; }
}
""",
"""
<span class="hljs-keyword">@for</span> <span class="hljs-variable">$i</span> from <span class="hljs-number">1</span> through <span class="hljs-number">5</span> {
  <span class="hljs-selector-class">.col-</span>#{<span class="hljs-variable">$i</span>} { <span class="hljs-attribute">width</span>: <span class="hljs-number">20%</span> * <span class="hljs-variable">$i</span>; }
}
""");
    }

    [Fact]
    public void ControlFlow_ForTo()
    {
        AssertHighlighter("scss",
"""
@for $i from 0 to 5 {
  .col-#{$i} { width: 20% * $i; }
}
""",
"""
<span class="hljs-keyword">@for</span> <span class="hljs-variable">$i</span> from <span class="hljs-number">0</span> to <span class="hljs-number">5</span> {
  <span class="hljs-selector-class">.col-</span>#{<span class="hljs-variable">$i</span>} { <span class="hljs-attribute">width</span>: <span class="hljs-number">20%</span> * <span class="hljs-variable">$i</span>; }
}
""");
    }

    [Fact]
    public void ControlFlow_EachList()
    {
        AssertHighlighter("scss",
"""
@each $color in red, green, blue {
  .text-#{$color} { color: $color; }
}
""",
"""
<span class="hljs-keyword">@each</span> <span class="hljs-variable">$color</span> in red, green, blue {
  <span class="hljs-selector-class">.text-</span>#{<span class="hljs-variable">$color</span>} { <span class="hljs-attribute">color</span>: <span class="hljs-variable">$color</span>; }
}
""");
    }

    [Fact]
    public void ControlFlow_EachMap()
    {
        AssertHighlighter("scss",
"""
@each $name, $color in (primary: blue, secondary: red) {
  .text-#{$name} { color: $color; }
}
""",
"""
<span class="hljs-keyword">@each</span> <span class="hljs-variable">$name</span>, <span class="hljs-variable">$color</span> in (<span class="hljs-attribute">primary</span>: blue, <span class="hljs-attribute">secondary</span>: red) {
  <span class="hljs-selector-class">.text-</span>#{<span class="hljs-variable">$name</span>} { <span class="hljs-attribute">color</span>: <span class="hljs-variable">$color</span>; }
}
""");
    }

    [Fact]
    public void ControlFlow_EachDestructure()
    {
        AssertHighlighter("scss",
"""
@each $name, $color, $size in $themes {
  .a-#{$name} { color: $color; font-size: $size; }
}
""",
"""
<span class="hljs-keyword">@each</span> <span class="hljs-variable">$name</span>, <span class="hljs-variable">$color</span>, <span class="hljs-variable">$size</span> in <span class="hljs-variable">$themes</span> {
  <span class="hljs-selector-class">.a-</span>#{<span class="hljs-variable">$name</span>} { <span class="hljs-attribute">color</span>: <span class="hljs-variable">$color</span>; <span class="hljs-attribute">font-size</span>: <span class="hljs-variable">$size</span>; }
}
""");
    }

    [Fact]
    public void ControlFlow_While()
    {
        AssertHighlighter("scss",
"""
$i: 1;
@while $i < 5 {
  .col-#{$i} { width: 20% * $i; }
  $i: $i + 1;
}
""",
"""
<span class="hljs-variable">$i</span>: <span class="hljs-number">1</span>;
<span class="hljs-keyword">@while</span> <span class="hljs-variable">$i</span> &lt; <span class="hljs-number">5</span> {
  <span class="hljs-selector-class">.col-</span>#{<span class="hljs-variable">$i</span>} { <span class="hljs-attribute">width</span>: <span class="hljs-number">20%</span> * <span class="hljs-variable">$i</span>; }
  <span class="hljs-variable">$i</span>: <span class="hljs-variable">$i</span> + <span class="hljs-number">1</span>;
}
""");
    }

    [Fact]
    public void Module_Use()
    {
        AssertHighlighter("scss",
"""
@use 'colors';
""",
"""
<span class="hljs-keyword">@use</span> <span class="hljs-string">&#x27;colors&#x27;</span>;
""");
    }

    [Fact]
    public void Module_UseAs()
    {
        AssertHighlighter("scss",
"""
@use 'colors' as c;
""",
"""
<span class="hljs-keyword">@use</span> <span class="hljs-string">&#x27;colors&#x27;</span> as c;
""");
    }

    [Fact]
    public void Module_UseAsStar()
    {
        AssertHighlighter("scss",
"""
@use 'colors' as *;
""",
"""
<span class="hljs-keyword">@use</span> <span class="hljs-string">&#x27;colors&#x27;</span> as *;
""");
    }

    [Fact]
    public void Module_UseWith()
    {
        AssertHighlighter("scss",
"""
@use 'theme' with ($primary: blue, $secondary: red);
""",
"""
<span class="hljs-keyword">@use</span> <span class="hljs-string">&#x27;theme&#x27;</span> with (<span class="hljs-variable">$primary</span>: blue, <span class="hljs-variable">$secondary</span>: red);
""");
    }

    [Fact]
    public void Module_Forward()
    {
        AssertHighlighter("scss",
"""
@forward 'colors';
""",
"""
<span class="hljs-keyword">@forward</span> <span class="hljs-string">&#x27;colors&#x27;</span>;
""");
    }

    [Fact]
    public void Module_ForwardShow()
    {
        AssertHighlighter("scss",
"""
@forward 'colors' show $primary, $secondary;
""",
"""
<span class="hljs-keyword">@forward</span> <span class="hljs-string">&#x27;colors&#x27;</span> show <span class="hljs-variable">$primary</span>, <span class="hljs-variable">$secondary</span>;
""");
    }

    [Fact]
    public void Module_ForwardHide()
    {
        AssertHighlighter("scss",
"""
@forward 'colors' hide $internal;
""",
"""
<span class="hljs-keyword">@forward</span> <span class="hljs-string">&#x27;colors&#x27;</span> hide <span class="hljs-variable">$internal</span>;
""");
    }

    [Fact]
    public void Module_ForwardAs()
    {
        AssertHighlighter("scss",
"""
@forward 'colors' as color-*;
""",
"""
<span class="hljs-keyword">@forward</span> <span class="hljs-string">&#x27;colors&#x27;</span> as color-*;
""");
    }

    [Fact]
    public void Module_BuiltInModule()
    {
        AssertHighlighter("scss",
"""
@use 'sass:math';
.a { width: math.div(10px, 2); }
""",
"""
<span class="hljs-keyword">@use</span> <span class="hljs-string">&#x27;sass:math&#x27;</span>;
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: math.<span class="hljs-built_in">div</span>(<span class="hljs-number">10px</span>, <span class="hljs-number">2</span>); }
""");
    }

    [Fact]
    public void Module_ImportLegacy()
    {
        AssertHighlighter("scss",
"""
@import 'colors';
""",
"""
<span class="hljs-keyword">@import</span> <span class="hljs-string">&#x27;colors&#x27;</span>;
""");
    }

    [Fact]
    public void BuiltinFunction_Lighten()
    {
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
"""
.a { color: darken(#fff, 20%); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">darken</span>(<span class="hljs-number">#fff</span>, <span class="hljs-number">20%</span>); }
""");
    }

    [Fact]
    public void BuiltinFunction_Mix()
    {
        AssertHighlighter("scss",
"""
.a { color: mix(#f00, #00f, 50%); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">mix</span>(<span class="hljs-number">#f00</span>, <span class="hljs-number">#00f</span>, <span class="hljs-number">50%</span>); }
""");
    }

    [Fact]
    public void BuiltinFunction_Saturate()
    {
        AssertHighlighter("scss",
"""
.a { color: saturate(#888, 50%); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">saturate</span>(<span class="hljs-number">#888</span>, <span class="hljs-number">50%</span>); }
""");
    }

    [Fact]
    public void BuiltinFunction_Rgba()
    {
        AssertHighlighter("scss",
"""
.a { color: rgba(red, 0.5); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">rgba</span>(red, <span class="hljs-number">0.5</span>); }
""");
    }

    [Fact]
    public void BuiltinFunction_MapGet()
    {
        AssertHighlighter("scss",
"""
.a { color: map-get($colors, primary); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">map-get</span>(<span class="hljs-variable">$colors</span>, primary); }
""");
    }

    [Fact]
    public void BuiltinFunction_MapMerge()
    {
        AssertHighlighter("scss",
"""
$c: map-merge($a, $b);
""",
"""
<span class="hljs-variable">$c</span>: <span class="hljs-built_in">map-merge</span>(<span class="hljs-variable">$a</span>, <span class="hljs-variable">$b</span>);
""");
    }

    [Fact]
    public void BuiltinFunction_Length()
    {
        AssertHighlighter("scss",
"""
.a { z-index: length($palette); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">z-index</span>: <span class="hljs-built_in">length</span>(<span class="hljs-variable">$palette</span>); }
""");
    }

    [Fact]
    public void BuiltinFunction_Nth()
    {
        AssertHighlighter("scss",
"""
.a { color: nth($palette, 1); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">nth</span>(<span class="hljs-variable">$palette</span>, <span class="hljs-number">1</span>); }
""");
    }

    [Fact]
    public void BuiltinFunction_Percentage()
    {
        AssertHighlighter("scss",
"""
.a { width: percentage(0.5); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-built_in">percentage</span>(<span class="hljs-number">0.5</span>); }
""");
    }

    [Fact]
    public void BuiltinFunction_IfFn()
    {
        AssertHighlighter("scss",
"""
.a { color: if(true, red, blue); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-built_in">if</span>(true, red, blue); }
""");
    }

    [Fact]
    public void BuiltinFunction_TypeOf()
    {
        AssertHighlighter("scss",
"""
.a { z-index: type-of(1); }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">z-index</span>: <span class="hljs-built_in">type-of</span>(<span class="hljs-number">1</span>); }
""");
    }

    [Fact]
    public void Operation_Add()
    {
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
"""
.a { width: 10px * 2; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: <span class="hljs-number">10px</span> * <span class="hljs-number">2</span>; }
""");
    }

    [Fact]
    public void Operation_MathDiv()
    {
        AssertHighlighter("scss",
"""
@use "sass:math";
.a { width: math.div(10px, 2); }
""",
"""
<span class="hljs-keyword">@use</span> <span class="hljs-string">&quot;sass:math&quot;</span>;
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">width</span>: math.<span class="hljs-built_in">div</span>(<span class="hljs-number">10px</span>, <span class="hljs-number">2</span>); }
""");
    }

    [Fact]
    public void Operation_WithVariable()
    {
        AssertHighlighter("scss",
"""
$base: 16px;
.a { font-size: $base * 1.5; }
""",
"""
<span class="hljs-variable">$base</span>: <span class="hljs-number">16px</span>;
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">font-size</span>: <span class="hljs-variable">$base</span> * <span class="hljs-number">1.5</span>; }
""");
    }

    [Fact]
    public void Operation_BoolAnd()
    {
        AssertHighlighter("scss",
"""
@if $a and $b { .x { color: red; } }
""",
"""
<span class="hljs-keyword">@if</span> <span class="hljs-variable">$a</span> <span class="hljs-keyword">and</span> <span class="hljs-variable">$b</span> { <span class="hljs-selector-class">.x</span> { <span class="hljs-attribute">color</span>: red; } }
""");
    }

    [Fact]
    public void Operation_BoolOr()
    {
        AssertHighlighter("scss",
"""
@if $a or $b { .x { color: red; } }
""",
"""
<span class="hljs-keyword">@if</span> <span class="hljs-variable">$a</span> <span class="hljs-keyword">or</span> <span class="hljs-variable">$b</span> { <span class="hljs-selector-class">.x</span> { <span class="hljs-attribute">color</span>: red; } }
""");
    }

    [Fact]
    public void Operation_BoolNot()
    {
        AssertHighlighter("scss",
"""
@if not $a { .x { color: red; } }
""",
"""
<span class="hljs-keyword">@if</span> <span class="hljs-keyword">not</span> <span class="hljs-variable">$a</span> { <span class="hljs-selector-class">.x</span> { <span class="hljs-attribute">color</span>: red; } }
""");
    }

    [Fact]
    public void Operation_Equal()
    {
        AssertHighlighter("scss",
"""
@if $theme == dark { .a { color: white; } }
""",
"""
<span class="hljs-keyword">@if</span> <span class="hljs-variable">$theme</span> == dark { <span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: white; } }
""");
    }

    [Fact]
    public void Operation_NotEqual()
    {
        AssertHighlighter("scss",
"""
@if $theme != light { .a { color: white; } }
""",
"""
<span class="hljs-keyword">@if</span> <span class="hljs-variable">$theme</span> != light { <span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: white; } }
""");
    }

    [Fact]
    public void AtRule_Media()
    {
        AssertHighlighter("scss",
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
    public void AtRule_MediaPrefers()
    {
        AssertHighlighter("scss",
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
    public void AtRule_Supports()
    {
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
"""
@container (min-width: 400px) {
  .a { color: red; }
}
""",
"""
<span class="hljs-keyword">@container</span> (<span class="hljs-attribute">min-width</span>: <span class="hljs-number">400px</span>) {
  <span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: red; }
}
""");
    }

    [Fact]
    public void AtRule_Keyframes()
    {
        AssertHighlighter("scss",
"""
@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}
""",
"""
<span class="hljs-keyword">@keyframes</span> spin {
  from { <span class="hljs-attribute">transform</span>: <span class="hljs-built_in">rotate</span>(<span class="hljs-number">0deg</span>); }
  to { <span class="hljs-attribute">transform</span>: <span class="hljs-built_in">rotate</span>(<span class="hljs-number">360deg</span>); }
}
""");
    }

    [Fact]
    public void AtRule_FontFace()
    {
        AssertHighlighter("scss",
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
    public void AtRule_Layer()
    {
        AssertHighlighter("scss",
"""
@layer base { .a { color: red; } }
""",
"""
<span class="hljs-keyword">@layer</span> base { <span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: red; } }
""");
    }

    [Fact]
    public void AtRule_AtRoot()
    {
        AssertHighlighter("scss",
"""
.parent {
  @at-root .child { color: red; }
}
""",
"""
<span class="hljs-selector-class">.parent</span> {
  <span class="hljs-keyword">@at-root</span> .child { <span class="hljs-attribute">color</span>: red; }
}
""");
    }

    [Fact]
    public void AtRule_Error()
    {
        AssertHighlighter("scss",
"""
@error "something broke";
""",
"""
<span class="hljs-keyword">@error</span> <span class="hljs-string">&quot;something broke&quot;</span>;
""");
    }

    [Fact]
    public void AtRule_Warn()
    {
        AssertHighlighter("scss",
"""
@warn "deprecated";
""",
"""
<span class="hljs-keyword">@warn</span> <span class="hljs-string">&quot;deprecated&quot;</span>;
""");
    }

    [Fact]
    public void AtRule_Debug()
    {
        AssertHighlighter("scss",
"""
@debug $value;
""",
"""
<span class="hljs-keyword">@debug</span> <span class="hljs-variable">$value</span>;
""");
    }

    [Fact]
    public void String_DoubleQuoted()
    {
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
"""
.a::before { content: 'hello'; }
""",
"""
<span class="hljs-selector-class">.a</span><span class="hljs-selector-pseudo">::before</span> { <span class="hljs-attribute">content</span>: <span class="hljs-string">&#x27;hello&#x27;</span>; }
""");
    }

    [Fact]
    public void String_Unquoted()
    {
        AssertHighlighter("scss",
"""
.a { font-family: sans-serif; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">font-family</span>: sans-serif; }
""");
    }

    [Fact]
    public void Comment_Line()
    {
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
    public void Nesting_PropertyNested()
    {
        AssertHighlighter("scss",
"""
.a {
  font: {
    family: sans-serif;
    size: 16px;
    weight: bold;
  }
}
""",
"""
<span class="hljs-selector-class">.a</span> {
  <span class="hljs-attribute">font</span>: {
    family: sans-serif;
    size: <span class="hljs-number">16px</span>;
    weight: bold;
  }
}
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("scss",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
"""
$x: 1;
""",
"""
<span class="hljs-variable">$x</span>: <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Unit_Dvh()
    {
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
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
        AssertHighlighter("scss",
"""
.a { height: 2lh; }
""",
"""
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">height</span>: <span class="hljs-number">2lh</span>; }
""");
    }
}
