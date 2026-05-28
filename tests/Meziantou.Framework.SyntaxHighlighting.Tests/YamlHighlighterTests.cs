namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class YamlHighlighterTests
{

    [Fact]
    public void Scalar_PlainString()
    {
        AssertHighlighter("yaml",
"""
name: alice
""",
"""
<span class="hljs-attr">name:</span> <span class="hljs-string">alice</span>
""");
    }

    [Fact]
    public void Scalar_SingleQuoted()
    {
        AssertHighlighter("yaml",
"""
name: 'alice'
""",
"""
<span class="hljs-attr">name:</span> <span class="hljs-string">&#x27;alice&#x27;</span>
""");
    }

    [Fact]
    public void Scalar_DoubleQuoted()
    {
        AssertHighlighter("yaml",
"""
name: "alice"
""",
"""
<span class="hljs-attr">name:</span> <span class="hljs-string">&quot;alice&quot;</span>
""");
    }

    [Fact]
    public void Scalar_EscapeNewline()
    {
        AssertHighlighter("yaml",
"""
msg: "line1\nline2"
""",
"""
<span class="hljs-attr">msg:</span> <span class="hljs-string">&quot;line1\nline2&quot;</span>
""");
    }

    [Fact]
    public void Scalar_EscapeTab()
    {
        AssertHighlighter("yaml",
"""
msg: "a\tb"
""",
"""
<span class="hljs-attr">msg:</span> <span class="hljs-string">&quot;a\tb&quot;</span>
""");
    }

    [Fact]
    public void Scalar_UnicodeEscape()
    {
        AssertHighlighter("yaml",
"""
msg: "\u0041"
""",
"""
<span class="hljs-attr">msg:</span> <span class="hljs-string">&quot;\u0041&quot;</span>
""");
    }

    [Fact]
    public void Scalar_MultiWord()
    {
        AssertHighlighter("yaml",
"""
title: The quick brown fox
""",
"""
<span class="hljs-attr">title:</span> <span class="hljs-string">The</span> <span class="hljs-string">quick</span> <span class="hljs-string">brown</span> <span class="hljs-string">fox</span>
""");
    }

    [Fact]
    public void Scalar_NumericString()
    {
        AssertHighlighter("yaml",
"""
version: "1.0"
""",
"""
<span class="hljs-attr">version:</span> <span class="hljs-string">&quot;1.0&quot;</span>
""");
    }

    [Fact]
    public void Scalar_EmptyValue()
    {
        AssertHighlighter("yaml",
"""
name:
""",
"""
<span class="hljs-attr">name:</span>
""");
    }

    [Fact]
    public void Scalar_SpecialChars()
    {
        AssertHighlighter("yaml",
"""
path: "/usr/local/bin"
""",
"""
<span class="hljs-attr">path:</span> <span class="hljs-string">&quot;/usr/local/bin&quot;</span>
""");
    }

    [Fact]
    public void Number_Integer()
    {
        AssertHighlighter("yaml",
"""
age: 30
""",
"""
<span class="hljs-attr">age:</span> <span class="hljs-number">30</span>
""");
    }

    [Fact]
    public void Number_Negative()
    {
        AssertHighlighter("yaml",
"""
temp: -10
""",
"""
<span class="hljs-attr">temp:</span> <span class="hljs-number">-10</span>
""");
    }

    [Fact]
    public void Number_Zero()
    {
        AssertHighlighter("yaml",
"""
count: 0
""",
"""
<span class="hljs-attr">count:</span> <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void Number_Float()
    {
        AssertHighlighter("yaml",
"""
pi: 3.14
""",
"""
<span class="hljs-attr">pi:</span> <span class="hljs-number">3.14</span>
""");
    }

    [Fact]
    public void Number_NegativeFloat()
    {
        AssertHighlighter("yaml",
"""
temp: -3.14
""",
"""
<span class="hljs-attr">temp:</span> <span class="hljs-number">-3.14</span>
""");
    }

    [Fact]
    public void Number_ExponentPositive()
    {
        AssertHighlighter("yaml",
"""
big: 1e10
""",
"""
<span class="hljs-attr">big:</span> <span class="hljs-number">1e10</span>
""");
    }

    [Fact]
    public void Number_ExponentNegative()
    {
        AssertHighlighter("yaml",
"""
small: 1.5e-3
""",
"""
<span class="hljs-attr">small:</span> <span class="hljs-number">1.5e-3</span>
""");
    }

    [Fact]
    public void Number_Hex()
    {
        AssertHighlighter("yaml",
"""
mask: 0xFF
""",
"""
<span class="hljs-attr">mask:</span> <span class="hljs-number">0xFF</span>
""");
    }

    [Fact]
    public void Number_Octal()
    {
        AssertHighlighter("yaml",
"""
mode: 0o755
""",
"""
<span class="hljs-attr">mode:</span> <span class="hljs-string">0o755</span>
""");
    }

    [Fact]
    public void Number_Infinity()
    {
        AssertHighlighter("yaml",
"""
inf: .inf
""",
"""
<span class="hljs-attr">inf:</span> <span class="hljs-string">.inf</span>
""");
    }

    [Fact]
    public void Number_NegativeInfinity()
    {
        AssertHighlighter("yaml",
"""
ninf: -.inf
""",
"""
<span class="hljs-attr">ninf:</span> <span class="hljs-string">-.inf</span>
""");
    }

    [Fact]
    public void Number_NaN()
    {
        AssertHighlighter("yaml",
"""
nan: .nan
""",
"""
<span class="hljs-attr">nan:</span> <span class="hljs-string">.nan</span>
""");
    }

    [Fact]
    public void Boolean_TrueLower()
    {
        AssertHighlighter("yaml",
"""
flag: true
""",
"""
<span class="hljs-attr">flag:</span> <span class="hljs-literal">true</span>
""");
    }

    [Fact]
    public void Boolean_TrueTitle()
    {
        AssertHighlighter("yaml",
"""
flag: True
""",
"""
<span class="hljs-attr">flag:</span> <span class="hljs-literal">True</span>
""");
    }

    [Fact]
    public void Boolean_TrueUpper()
    {
        AssertHighlighter("yaml",
"""
flag: TRUE
""",
"""
<span class="hljs-attr">flag:</span> <span class="hljs-literal">TRUE</span>
""");
    }

    [Fact]
    public void Boolean_FalseLower()
    {
        AssertHighlighter("yaml",
"""
flag: false
""",
"""
<span class="hljs-attr">flag:</span> <span class="hljs-literal">false</span>
""");
    }

    [Fact]
    public void Boolean_FalseTitle()
    {
        AssertHighlighter("yaml",
"""
flag: False
""",
"""
<span class="hljs-attr">flag:</span> <span class="hljs-literal">False</span>
""");
    }

    [Fact]
    public void Boolean_FalseUpper()
    {
        AssertHighlighter("yaml",
"""
flag: FALSE
""",
"""
<span class="hljs-attr">flag:</span> <span class="hljs-literal">FALSE</span>
""");
    }

    [Fact]
    public void Boolean_YesLower()
    {
        AssertHighlighter("yaml",
"""
flag: yes
""",
"""
<span class="hljs-attr">flag:</span> <span class="hljs-literal">yes</span>
""");
    }

    [Fact]
    public void Boolean_NoLower()
    {
        AssertHighlighter("yaml",
"""
flag: no
""",
"""
<span class="hljs-attr">flag:</span> <span class="hljs-literal">no</span>
""");
    }

    [Fact]
    public void Boolean_OnLower()
    {
        AssertHighlighter("yaml",
"""
flag: on
""",
"""
<span class="hljs-attr">flag:</span> <span class="hljs-string">on</span>
""");
    }

    [Fact]
    public void Boolean_OffLower()
    {
        AssertHighlighter("yaml",
"""
flag: off
""",
"""
<span class="hljs-attr">flag:</span> <span class="hljs-string">off</span>
""");
    }

    [Fact]
    public void Null_Lower()
    {
        AssertHighlighter("yaml",
"""
value: null
""",
"""
<span class="hljs-attr">value:</span> <span class="hljs-literal">null</span>
""");
    }

    [Fact]
    public void Null_Title()
    {
        AssertHighlighter("yaml",
"""
value: Null
""",
"""
<span class="hljs-attr">value:</span> <span class="hljs-literal">Null</span>
""");
    }

    [Fact]
    public void Null_Upper()
    {
        AssertHighlighter("yaml",
"""
value: NULL
""",
"""
<span class="hljs-attr">value:</span> <span class="hljs-literal">NULL</span>
""");
    }

    [Fact]
    public void Null_Tilde()
    {
        AssertHighlighter("yaml",
"""
value: ~
""",
"""
<span class="hljs-attr">value:</span> <span class="hljs-string">~</span>
""");
    }

    [Fact]
    public void Mapping_SingleKey()
    {
        AssertHighlighter("yaml",
"""
name: alice
""",
"""
<span class="hljs-attr">name:</span> <span class="hljs-string">alice</span>
""");
    }

    [Fact]
    public void Mapping_MultipleKeys()
    {
        AssertHighlighter("yaml",
"""
name: alice
age: 30
active: true
""",
"""
<span class="hljs-attr">name:</span> <span class="hljs-string">alice</span>
<span class="hljs-attr">age:</span> <span class="hljs-number">30</span>
<span class="hljs-attr">active:</span> <span class="hljs-literal">true</span>
""");
    }

    [Fact]
    public void Mapping_Nested()
    {
        AssertHighlighter("yaml",
"""
user:
  name: alice
  age: 30
""",
"""
<span class="hljs-attr">user:</span>
  <span class="hljs-attr">name:</span> <span class="hljs-string">alice</span>
  <span class="hljs-attr">age:</span> <span class="hljs-number">30</span>
""");
    }

    [Fact]
    public void Mapping_DeepNested()
    {
        AssertHighlighter("yaml",
"""
a:
  b:
    c:
      d: 1
""",
"""
<span class="hljs-attr">a:</span>
  <span class="hljs-attr">b:</span>
    <span class="hljs-attr">c:</span>
      <span class="hljs-attr">d:</span> <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Mapping_QuotedKey()
    {
        AssertHighlighter("yaml",
"""
"name": alice
""",
"""
<span class="hljs-attr">&quot;name&quot;:</span> <span class="hljs-string">alice</span>
""");
    }

    [Fact]
    public void Mapping_ComplexKey()
    {
        AssertHighlighter("yaml",
"""
? - a
  - b
: value
""",
"""
<span class="hljs-string">?</span> <span class="hljs-bullet">-</span> <span class="hljs-string">a</span>
  <span class="hljs-bullet">-</span> <span class="hljs-string">b</span>
<span class="hljs-string">:</span> <span class="hljs-string">value</span>
""");
    }

    [Fact]
    public void Mapping_FlowEmpty()
    {
        AssertHighlighter("yaml",
"""
data: {}
""",
"""
<span class="hljs-attr">data:</span> {}
""");
    }

    [Fact]
    public void Mapping_FlowSingle()
    {
        AssertHighlighter("yaml",
"""
data: {a: 1}
""",
"""
<span class="hljs-attr">data:</span> {<span class="hljs-attr">a:</span> <span class="hljs-number">1</span>}
""");
    }

    [Fact]
    public void Mapping_FlowMultiple()
    {
        AssertHighlighter("yaml",
"""
data: {a: 1, b: 2, c: 3}
""",
"""
<span class="hljs-attr">data:</span> {<span class="hljs-attr">a:</span> <span class="hljs-number">1</span>, <span class="hljs-attr">b:</span> <span class="hljs-number">2</span>, <span class="hljs-attr">c:</span> <span class="hljs-number">3</span>}
""");
    }

    [Fact]
    public void Mapping_NumericKey()
    {
        AssertHighlighter("yaml",
"""
42: answer
""",
"""
<span class="hljs-attr">42:</span> <span class="hljs-string">answer</span>
""");
    }

    [Fact]
    public void Sequence_BlockSingle()
    {
        AssertHighlighter("yaml",
"""
- item
""",
"""
<span class="hljs-bullet">-</span> <span class="hljs-string">item</span>
""");
    }

    [Fact]
    public void Sequence_BlockMultiple()
    {
        AssertHighlighter("yaml",
"""
- one
- two
- three
""",
"""
<span class="hljs-bullet">-</span> <span class="hljs-string">one</span>
<span class="hljs-bullet">-</span> <span class="hljs-string">two</span>
<span class="hljs-bullet">-</span> <span class="hljs-string">three</span>
""");
    }

    [Fact]
    public void Sequence_BlockNested()
    {
        AssertHighlighter("yaml",
"""
- - a
  - b
- - c
  - d
""",
"""
<span class="hljs-bullet">-</span> <span class="hljs-bullet">-</span> <span class="hljs-string">a</span>
  <span class="hljs-bullet">-</span> <span class="hljs-string">b</span>
<span class="hljs-bullet">-</span> <span class="hljs-bullet">-</span> <span class="hljs-string">c</span>
  <span class="hljs-bullet">-</span> <span class="hljs-string">d</span>
""");
    }

    [Fact]
    public void Sequence_BlockOfMaps()
    {
        AssertHighlighter("yaml",
"""
- name: alice
  age: 30
- name: bob
  age: 25
""",
"""
<span class="hljs-bullet">-</span> <span class="hljs-attr">name:</span> <span class="hljs-string">alice</span>
  <span class="hljs-attr">age:</span> <span class="hljs-number">30</span>
<span class="hljs-bullet">-</span> <span class="hljs-attr">name:</span> <span class="hljs-string">bob</span>
  <span class="hljs-attr">age:</span> <span class="hljs-number">25</span>
""");
    }

    [Fact]
    public void Sequence_FlowEmpty()
    {
        AssertHighlighter("yaml",
"""
list: []
""",
"""
<span class="hljs-attr">list:</span> []
""");
    }

    [Fact]
    public void Sequence_FlowSingle()
    {
        AssertHighlighter("yaml",
"""
list: [1]
""",
"""
<span class="hljs-attr">list:</span> [<span class="hljs-number">1</span>]
""");
    }

    [Fact]
    public void Sequence_FlowMultiple()
    {
        AssertHighlighter("yaml",
"""
list: [1, 2, 3]
""",
"""
<span class="hljs-attr">list:</span> [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>]
""");
    }

    [Fact]
    public void Sequence_FlowStrings()
    {
        AssertHighlighter("yaml",
"""
list: [a, b, c]
""",
"""
<span class="hljs-attr">list:</span> [<span class="hljs-string">a</span>, <span class="hljs-string">b</span>, <span class="hljs-string">c</span>]
""");
    }

    [Fact]
    public void Sequence_FlowMixed()
    {
        AssertHighlighter("yaml",
"""
list: [1, "two", true, null]
""",
"""
<span class="hljs-attr">list:</span> [<span class="hljs-number">1</span>, <span class="hljs-string">&quot;two&quot;</span>, <span class="hljs-literal">true</span>, <span class="hljs-literal">null</span>]
""");
    }

    [Fact]
    public void Sequence_FlowNested()
    {
        AssertHighlighter("yaml",
"""
matrix: [[1, 2], [3, 4]]
""",
"""
<span class="hljs-attr">matrix:</span> [[<span class="hljs-number">1</span>, <span class="hljs-number">2</span>], [<span class="hljs-number">3</span>, <span class="hljs-number">4</span>]]
""");
    }

    [Fact]
    public void BlockScalar_Literal()
    {
        AssertHighlighter("yaml",
"""
text: |
  line1
  line2
""",
"""
<span class="hljs-attr">text:</span> <span class="hljs-string">|
  line1
  line2</span>
""");
    }

    [Fact]
    public void BlockScalar_LiteralStrip()
    {
        AssertHighlighter("yaml",
"""
text: |-
  line1
  line2
""",
"""
<span class="hljs-attr">text:</span> <span class="hljs-string">|-
  line1
  line2</span>
""");
    }

    [Fact]
    public void BlockScalar_LiteralKeep()
    {
        AssertHighlighter("yaml",
"""
text: |+
  line1
  line2
""",
"""
<span class="hljs-attr">text:</span> <span class="hljs-string">|+
  line1
  line2</span>
""");
    }

    [Fact]
    public void BlockScalar_Folded()
    {
        AssertHighlighter("yaml",
"""
text: >
  line1
  line2
""",
"""
<span class="hljs-attr">text:</span> <span class="hljs-string">&gt;
  line1
  line2</span>
""");
    }

    [Fact]
    public void BlockScalar_FoldedStrip()
    {
        AssertHighlighter("yaml",
"""
text: >-
  line1
  line2
""",
"""
<span class="hljs-attr">text:</span> <span class="hljs-string">&gt;-
  line1
  line2</span>
""");
    }

    [Fact]
    public void BlockScalar_FoldedKeep()
    {
        AssertHighlighter("yaml",
"""
text: >+
  line1
  line2
""",
"""
<span class="hljs-attr">text:</span> <span class="hljs-string">&gt;+
  line1
  line2</span>
""");
    }

    [Fact]
    public void BlockScalar_LiteralIndent()
    {
        AssertHighlighter("yaml",
"""
text: |2
    line1
    line2
""",
"""
<span class="hljs-attr">text:</span> <span class="hljs-string">|2</span>
    <span class="hljs-string">line1</span>
    <span class="hljs-string">line2</span>
""");
    }

    [Fact]
    public void Anchor_Define()
    {
        AssertHighlighter("yaml",
"""
default: &defaults
  name: alice
  age: 30
""",
"""
<span class="hljs-attr">default:</span> <span class="hljs-meta">&amp;defaults</span>
  <span class="hljs-attr">name:</span> <span class="hljs-string">alice</span>
  <span class="hljs-attr">age:</span> <span class="hljs-number">30</span>
""");
    }

    [Fact]
    public void Anchor_Alias()
    {
        AssertHighlighter("yaml",
"""
a: &x 1
b: *x
""",
"""
<span class="hljs-attr">a:</span> <span class="hljs-string">&amp;x</span> <span class="hljs-number">1</span>
<span class="hljs-attr">b:</span> <span class="hljs-meta">*x</span>
""");
    }

    [Fact]
    public void Anchor_MergeKey()
    {
        AssertHighlighter("yaml",
"""
base: &base
  a: 1
derived:
  <<: *base
  b: 2
""",
"""
<span class="hljs-attr">base:</span> <span class="hljs-meta">&amp;base</span>
  <span class="hljs-attr">a:</span> <span class="hljs-number">1</span>
<span class="hljs-attr">derived:</span>
  <span class="hljs-string">&lt;&lt;:</span> <span class="hljs-meta">*base</span>
  <span class="hljs-attr">b:</span> <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Anchor_InSequence()
    {
        AssertHighlighter("yaml",
"""
- &first
  name: alice
- *first
""",
"""
<span class="hljs-bullet">-</span> <span class="hljs-meta">&amp;first</span>
  <span class="hljs-attr">name:</span> <span class="hljs-string">alice</span>
<span class="hljs-bullet">-</span> <span class="hljs-meta">*first</span>
""");
    }

    [Fact]
    public void Tag_BangBangStr()
    {
        AssertHighlighter("yaml",
"""
value: !!str 42
""",
"""
<span class="hljs-attr">value:</span> <span class="hljs-type">!!str</span> <span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void Tag_BangBangInt()
    {
        AssertHighlighter("yaml",
"""
value: !!int "42"
""",
"""
<span class="hljs-attr">value:</span> <span class="hljs-type">!!int</span> <span class="hljs-string">&quot;42&quot;</span>
""");
    }

    [Fact]
    public void Tag_BangBangFloat()
    {
        AssertHighlighter("yaml",
"""
value: !!float "3.14"
""",
"""
<span class="hljs-attr">value:</span> <span class="hljs-type">!!float</span> <span class="hljs-string">&quot;3.14&quot;</span>
""");
    }

    [Fact]
    public void Tag_BangBangBool()
    {
        AssertHighlighter("yaml",
"""
value: !!bool "true"
""",
"""
<span class="hljs-attr">value:</span> <span class="hljs-type">!!bool</span> <span class="hljs-string">&quot;true&quot;</span>
""");
    }

    [Fact]
    public void Tag_BangBangNull()
    {
        AssertHighlighter("yaml",
"""
value: !!null ""
""",
"""
<span class="hljs-attr">value:</span> <span class="hljs-type">!!null</span> <span class="hljs-string">&quot;&quot;</span>
""");
    }

    [Fact]
    public void Tag_BangBangMap()
    {
        AssertHighlighter("yaml",
"""
value: !!map
  a: 1
""",
"""
<span class="hljs-attr">value:</span> <span class="hljs-type">!!map</span>
  <span class="hljs-attr">a:</span> <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Tag_BangBangSeq()
    {
        AssertHighlighter("yaml",
"""
value: !!seq
  - 1
  - 2
""",
"""
<span class="hljs-attr">value:</span> <span class="hljs-type">!!seq</span>
  <span class="hljs-bullet">-</span> <span class="hljs-number">1</span>
  <span class="hljs-bullet">-</span> <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Tag_BangBangBinary()
    {
        AssertHighlighter("yaml",
"""
image: !!binary aGVsbG8=
""",
"""
<span class="hljs-attr">image:</span> <span class="hljs-type">!!binary</span> <span class="hljs-string">aGVsbG8=</span>
""");
    }

    [Fact]
    public void Tag_Custom()
    {
        AssertHighlighter("yaml",
"""
point: !point
  x: 1
  y: 2
""",
"""
<span class="hljs-attr">point:</span> <span class="hljs-type">!point</span>
  <span class="hljs-attr">x:</span> <span class="hljs-number">1</span>
  <span class="hljs-attr">y:</span> <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Comment_FullLine()
    {
        AssertHighlighter("yaml",
"""
# just a comment
""",
"""
<span class="hljs-comment"># just a comment</span>
""");
    }

    [Fact]
    public void Comment_Inline()
    {
        AssertHighlighter("yaml",
"""
name: alice  # the name
""",
"""
<span class="hljs-attr">name:</span> <span class="hljs-string">alice</span>  <span class="hljs-comment"># the name</span>
""");
    }

    [Fact]
    public void Comment_AboveKey()
    {
        AssertHighlighter("yaml",
"""
# the user
name: alice
""",
"""
<span class="hljs-comment"># the user</span>
<span class="hljs-attr">name:</span> <span class="hljs-string">alice</span>
""");
    }

    [Fact]
    public void Comment_MultipleLines()
    {
        AssertHighlighter("yaml",
"""
# line 1
# line 2
# line 3
""",
"""
<span class="hljs-comment"># line 1</span>
<span class="hljs-comment"># line 2</span>
<span class="hljs-comment"># line 3</span>
""");
    }

    [Fact]
    public void Document_Separator()
    {
        AssertHighlighter("yaml",
"""
---
name: alice
""",
"""
<span class="hljs-meta">---</span>
<span class="hljs-attr">name:</span> <span class="hljs-string">alice</span>
""");
    }

    [Fact]
    public void Document_SeparatorEnd()
    {
        AssertHighlighter("yaml",
"""
name: alice
...
""",
"""
<span class="hljs-attr">name:</span> <span class="hljs-string">alice</span>
<span class="hljs-string">...</span>
""");
    }

    [Fact]
    public void Document_Multiple()
    {
        AssertHighlighter("yaml",
"""
---
name: alice
---
name: bob
""",
"""
<span class="hljs-meta">---</span>
<span class="hljs-attr">name:</span> <span class="hljs-string">alice</span>
<span class="hljs-meta">---</span>
<span class="hljs-attr">name:</span> <span class="hljs-string">bob</span>
""");
    }

    [Fact]
    public void Document_WithDirective()
    {
        AssertHighlighter("yaml",
"""
%YAML 1.2
---
name: alice
""",
"""
<span class="hljs-string">%YAML</span> <span class="hljs-number">1.2</span>
<span class="hljs-meta">---</span>
<span class="hljs-attr">name:</span> <span class="hljs-string">alice</span>
""");
    }

    [Fact]
    public void Document_WithTagDir()
    {
        AssertHighlighter("yaml",
"""
%TAG !e! tag:example.com,2000:
---
name: alice
""",
"""
<span class="hljs-string">%TAG</span> <span class="hljs-type">!e</span><span class="hljs-string">!</span> <span class="hljs-string">tag:example.com,2000:</span>
<span class="hljs-meta">---</span>
<span class="hljs-attr">name:</span> <span class="hljs-string">alice</span>
""");
    }

    [Fact]
    public void Composite_KubernetesPod()
    {
        AssertHighlighter("yaml",
"""
apiVersion: v1
kind: Pod
metadata:
  name: my-pod
spec:
  containers:
  - name: nginx
    image: nginx:1.21
""",
"""
<span class="hljs-attr">apiVersion:</span> <span class="hljs-string">v1</span>
<span class="hljs-attr">kind:</span> <span class="hljs-string">Pod</span>
<span class="hljs-attr">metadata:</span>
  <span class="hljs-attr">name:</span> <span class="hljs-string">my-pod</span>
<span class="hljs-attr">spec:</span>
  <span class="hljs-attr">containers:</span>
  <span class="hljs-bullet">-</span> <span class="hljs-attr">name:</span> <span class="hljs-string">nginx</span>
    <span class="hljs-attr">image:</span> <span class="hljs-string">nginx:1.21</span>
""");
    }

    [Fact]
    public void Composite_GitHubAction()
    {
        AssertHighlighter("yaml",
"""
name: CI
on: [push]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3
""",
"""
<span class="hljs-attr">name:</span> <span class="hljs-string">CI</span>
<span class="hljs-attr">on:</span> [<span class="hljs-string">push</span>]
<span class="hljs-attr">jobs:</span>
  <span class="hljs-attr">build:</span>
    <span class="hljs-attr">runs-on:</span> <span class="hljs-string">ubuntu-latest</span>
    <span class="hljs-attr">steps:</span>
    <span class="hljs-bullet">-</span> <span class="hljs-attr">uses:</span> <span class="hljs-string">actions/checkout@v3</span>
""");
    }

    [Fact]
    public void Composite_DockerCompose()
    {
        AssertHighlighter("yaml",
"""
version: "3.8"
services:
  web:
    image: nginx
    ports:
    - "80:80"
""",
"""
<span class="hljs-attr">version:</span> <span class="hljs-string">&quot;3.8&quot;</span>
<span class="hljs-attr">services:</span>
  <span class="hljs-attr">web:</span>
    <span class="hljs-attr">image:</span> <span class="hljs-string">nginx</span>
    <span class="hljs-attr">ports:</span>
    <span class="hljs-bullet">-</span> <span class="hljs-string">&quot;80:80&quot;</span>
""");
    }

    [Fact]
    public void Composite_AllScalarTypes()
    {
        AssertHighlighter("yaml",
"""
str: hello
int: 42
float: 3.14
bool: true
null_val: null
list: [1, 2]
map: {a: 1}
""",
"""
<span class="hljs-attr">str:</span> <span class="hljs-string">hello</span>
<span class="hljs-attr">int:</span> <span class="hljs-number">42</span>
<span class="hljs-attr">float:</span> <span class="hljs-number">3.14</span>
<span class="hljs-attr">bool:</span> <span class="hljs-literal">true</span>
<span class="hljs-attr">null_val:</span> <span class="hljs-literal">null</span>
<span class="hljs-attr">list:</span> [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>]
<span class="hljs-attr">map:</span> {<span class="hljs-attr">a:</span> <span class="hljs-number">1</span>}
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("yaml",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyWhitespace()
    {
        AssertHighlighter("yaml",
"""


""",
"""


""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("yaml",
"""
# just a comment
""",
"""
<span class="hljs-comment"># just a comment</span>
""");
    }

    [Fact]
    public void SpecialEdge_OnlySeparator()
    {
        AssertHighlighter("yaml",
"""
---
""",
"""
<span class="hljs-meta">---</span>
""");
    }

    [Fact]
    public void SpecialEdge_TrailingNewline()
    {
        AssertHighlighter("yaml",
"""
name: alice

""",
"""
<span class="hljs-attr">name:</span> <span class="hljs-string">alice</span>

""");
    }
}
