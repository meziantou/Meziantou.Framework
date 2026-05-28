namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class JsonHighlighterTests
{

    [Fact]
    public void Literal_True()
    {
        AssertHighlighter("json",
"""
true
""",
"""
<span class="hljs-literal"><span class="hljs-keyword">true</span></span>
""");
    }

    [Fact]
    public void Literal_False()
    {
        AssertHighlighter("json",
"""
false
""",
"""
<span class="hljs-literal"><span class="hljs-keyword">false</span></span>
""");
    }

    [Fact]
    public void Literal_Null()
    {
        AssertHighlighter("json",
"""
null
""",
"""
<span class="hljs-literal"><span class="hljs-keyword">null</span></span>
""");
    }

    [Fact]
    public void Number_Zero()
    {
        AssertHighlighter("json",
"""
0
""",
"""
<span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void Number_Integer()
    {
        AssertHighlighter("json",
"""
42
""",
"""
<span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void Number_NegativeInteger()
    {
        AssertHighlighter("json",
"""
-42
""",
"""
<span class="hljs-number">-42</span>
""");
    }

    [Fact]
    public void Number_LargeInteger()
    {
        AssertHighlighter("json",
"""
12345678901234
""",
"""
<span class="hljs-number">12345678901234</span>
""");
    }

    [Fact]
    public void Number_Float()
    {
        AssertHighlighter("json",
"""
3.14
""",
"""
<span class="hljs-number">3.14</span>
""");
    }

    [Fact]
    public void Number_NegativeFloat()
    {
        AssertHighlighter("json",
"""
-3.14
""",
"""
<span class="hljs-number">-3.14</span>
""");
    }

    [Fact]
    public void Number_ZeroFloat()
    {
        AssertHighlighter("json",
"""
0.5
""",
"""
<span class="hljs-number">0.5</span>
""");
    }

    [Fact]
    public void Number_ExponentPositive()
    {
        AssertHighlighter("json",
"""
1e10
""",
"""
<span class="hljs-number">1e10</span>
""");
    }

    [Fact]
    public void Number_ExponentNegative()
    {
        AssertHighlighter("json",
"""
1.5e-3
""",
"""
<span class="hljs-number">1.5e-3</span>
""");
    }

    [Fact]
    public void Number_ExponentExplicit()
    {
        AssertHighlighter("json",
"""
2.5e+4
""",
"""
<span class="hljs-number">2.5e+4</span>
""");
    }

    [Fact]
    public void Number_ExponentUpper()
    {
        AssertHighlighter("json",
"""
1E5
""",
"""
<span class="hljs-number">1E5</span>
""");
    }

    [Fact]
    public void Number_Tiny()
    {
        AssertHighlighter("json",
"""
0.00001
""",
"""
<span class="hljs-number">0.00001</span>
""");
    }

    [Fact]
    public void String_Empty()
    {
        AssertHighlighter("json",
"""
""
""",
"""
<span class="hljs-string">&quot;&quot;</span>
""");
    }

    [Fact]
    public void String_Simple()
    {
        AssertHighlighter("json",
"""
"hello"
""",
"""
<span class="hljs-string">&quot;hello&quot;</span>
""");
    }

    [Fact]
    public void String_WithSpaces()
    {
        AssertHighlighter("json",
"""
"hello world"
""",
"""
<span class="hljs-string">&quot;hello world&quot;</span>
""");
    }

    [Fact]
    public void String_EscapeQuote()
    {
        AssertHighlighter("json",
"""
"she said \"hi\""
""",
"""
<span class="hljs-string">&quot;she said \&quot;hi\&quot;&quot;</span>
""");
    }

    [Fact]
    public void String_EscapeBackslash()
    {
        AssertHighlighter("json",
"""
"a\\b"
""",
"""
<span class="hljs-string">&quot;a\\b&quot;</span>
""");
    }

    [Fact]
    public void String_EscapeSlash()
    {
        AssertHighlighter("json",
"""
"a\/b"
""",
"""
<span class="hljs-string">&quot;a\/b&quot;</span>
""");
    }

    [Fact]
    public void String_EscapeBackspace()
    {
        AssertHighlighter("json",
"""
"a\bb"
""",
"""
<span class="hljs-string">&quot;a\bb&quot;</span>
""");
    }

    [Fact]
    public void String_EscapeFormFeed()
    {
        AssertHighlighter("json",
"""
"a\fb"
""",
"""
<span class="hljs-string">&quot;a\fb&quot;</span>
""");
    }

    [Fact]
    public void String_EscapeNewline()
    {
        AssertHighlighter("json",
"""
"a\nb"
""",
"""
<span class="hljs-string">&quot;a\nb&quot;</span>
""");
    }

    [Fact]
    public void String_EscapeReturn()
    {
        AssertHighlighter("json",
"""
"a\rb"
""",
"""
<span class="hljs-string">&quot;a\rb&quot;</span>
""");
    }

    [Fact]
    public void String_EscapeTab()
    {
        AssertHighlighter("json",
"""
"a\tb"
""",
"""
<span class="hljs-string">&quot;a\tb&quot;</span>
""");
    }

    [Fact]
    public void String_UnicodeEscape()
    {
        AssertHighlighter("json",
"""
"\u0041"
""",
"""
<span class="hljs-string">&quot;\u0041&quot;</span>
""");
    }

    [Fact]
    public void String_UnicodeBmp()
    {
        AssertHighlighter("json",
"""
"\u4e2d"
""",
"""
<span class="hljs-string">&quot;\u4e2d&quot;</span>
""");
    }

    [Fact]
    public void String_UnicodeSurrogate()
    {
        AssertHighlighter("json",
"""
"\uD83D\uDE00"
""",
"""
<span class="hljs-string">&quot;\uD83D\uDE00&quot;</span>
""");
    }

    [Fact]
    public void String_Numeric()
    {
        AssertHighlighter("json",
"""
"42"
""",
"""
<span class="hljs-string">&quot;42&quot;</span>
""");
    }

    [Fact]
    public void String_Whitespace()
    {
        AssertHighlighter("json",
"""
"   "
""",
"""
<span class="hljs-string">&quot;   &quot;</span>
""");
    }

    [Fact]
    public void String_Punctuation()
    {
        AssertHighlighter("json",
"""
"hello, world!"
""",
"""
<span class="hljs-string">&quot;hello, world!&quot;</span>
""");
    }

    [Fact]
    public void String_Long()
    {
        AssertHighlighter("json",
"""
"The quick brown fox jumps over the lazy dog"
""",
"""
<span class="hljs-string">&quot;The quick brown fox jumps over the lazy dog&quot;</span>
""");
    }

    [Fact]
    public void Array_Empty()
    {
        AssertHighlighter("json",
"""
[]
""",
"""
<span class="hljs-punctuation">[</span><span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void Array_SingleInt()
    {
        AssertHighlighter("json",
"""
[1]
""",
"""
<span class="hljs-punctuation">[</span><span class="hljs-number">1</span><span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void Array_MultipleInts()
    {
        AssertHighlighter("json",
"""
[1, 2, 3]
""",
"""
<span class="hljs-punctuation">[</span><span class="hljs-number">1</span><span class="hljs-punctuation">,</span> <span class="hljs-number">2</span><span class="hljs-punctuation">,</span> <span class="hljs-number">3</span><span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void Array_MultipleStrings()
    {
        AssertHighlighter("json",
"""
["a", "b", "c"]
""",
"""
<span class="hljs-punctuation">[</span><span class="hljs-string">&quot;a&quot;</span><span class="hljs-punctuation">,</span> <span class="hljs-string">&quot;b&quot;</span><span class="hljs-punctuation">,</span> <span class="hljs-string">&quot;c&quot;</span><span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void Array_MixedTypes()
    {
        AssertHighlighter("json",
"""
[1, "two", true, null, 3.14]
""",
"""
<span class="hljs-punctuation">[</span><span class="hljs-number">1</span><span class="hljs-punctuation">,</span> <span class="hljs-string">&quot;two&quot;</span><span class="hljs-punctuation">,</span> <span class="hljs-literal"><span class="hljs-keyword">true</span></span><span class="hljs-punctuation">,</span> <span class="hljs-literal"><span class="hljs-keyword">null</span></span><span class="hljs-punctuation">,</span> <span class="hljs-number">3.14</span><span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void Array_NestedArray()
    {
        AssertHighlighter("json",
"""
[[1, 2], [3, 4]]
""",
"""
<span class="hljs-punctuation">[</span><span class="hljs-punctuation">[</span><span class="hljs-number">1</span><span class="hljs-punctuation">,</span> <span class="hljs-number">2</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">,</span> <span class="hljs-punctuation">[</span><span class="hljs-number">3</span><span class="hljs-punctuation">,</span> <span class="hljs-number">4</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void Array_NestedDeep()
    {
        AssertHighlighter("json",
"""
[[[1]]]
""",
"""
<span class="hljs-punctuation">[</span><span class="hljs-punctuation">[</span><span class="hljs-punctuation">[</span><span class="hljs-number">1</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void Array_ArrayOfObjects()
    {
        AssertHighlighter("json",
"""
[{"x": 1}, {"x": 2}]
""",
"""
<span class="hljs-punctuation">[</span><span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;x&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1</span><span class="hljs-punctuation">}</span><span class="hljs-punctuation">,</span> <span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;x&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">2</span><span class="hljs-punctuation">}</span><span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void Array_WithBooleans()
    {
        AssertHighlighter("json",
"""
[true, false]
""",
"""
<span class="hljs-punctuation">[</span><span class="hljs-literal"><span class="hljs-keyword">true</span></span><span class="hljs-punctuation">,</span> <span class="hljs-literal"><span class="hljs-keyword">false</span></span><span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void Array_WithNulls()
    {
        AssertHighlighter("json",
"""
[null, null]
""",
"""
<span class="hljs-punctuation">[</span><span class="hljs-literal"><span class="hljs-keyword">null</span></span><span class="hljs-punctuation">,</span> <span class="hljs-literal"><span class="hljs-keyword">null</span></span><span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void Array_OnlyEmpty()
    {
        AssertHighlighter("json",
"""
[[], []]
""",
"""
<span class="hljs-punctuation">[</span><span class="hljs-punctuation">[</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">,</span> <span class="hljs-punctuation">[</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void Array_MultiLine()
    {
        AssertHighlighter("json",
"""
[
  1,
  2,
  3
]
""",
"""
<span class="hljs-punctuation">[</span>
  <span class="hljs-number">1</span><span class="hljs-punctuation">,</span>
  <span class="hljs-number">2</span><span class="hljs-punctuation">,</span>
  <span class="hljs-number">3</span>
<span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void Object_Empty()
    {
        AssertHighlighter("json",
"""
{}
""",
"""
<span class="hljs-punctuation">{</span><span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Object_SingleProp()
    {
        AssertHighlighter("json",
"""
{"name": "alice"}
""",
"""
<span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;name&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;alice&quot;</span><span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Object_MultipleProps()
    {
        AssertHighlighter("json",
"""
{"name": "alice", "age": 30}
""",
"""
<span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;name&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;alice&quot;</span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;age&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">30</span><span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Object_NestedObject()
    {
        AssertHighlighter("json",
"""
{"a": {"b": {"c": 1}}}
""",
"""
<span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;a&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;b&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;c&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1</span><span class="hljs-punctuation">}</span><span class="hljs-punctuation">}</span><span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Object_ObjectWithArray()
    {
        AssertHighlighter("json",
"""
{"items": [1, 2, 3]}
""",
"""
<span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;items&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">[</span><span class="hljs-number">1</span><span class="hljs-punctuation">,</span> <span class="hljs-number">2</span><span class="hljs-punctuation">,</span> <span class="hljs-number">3</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Object_AllTypes()
    {
        AssertHighlighter("json",
"""
{"s": "x", "n": 1, "f": 1.5, "t": true, "fl": false, "nl": null, "a": [], "o": {}}
""",
"""
<span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;s&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;x&quot;</span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;n&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1</span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;f&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1.5</span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;t&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-literal"><span class="hljs-keyword">true</span></span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;fl&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-literal"><span class="hljs-keyword">false</span></span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;nl&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-literal"><span class="hljs-keyword">null</span></span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;a&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">[</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;o&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">{</span><span class="hljs-punctuation">}</span><span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Object_NumericKey()
    {
        AssertHighlighter("json",
"""
{"42": "answer"}
""",
"""
<span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;42&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;answer&quot;</span><span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Object_EmptyKey()
    {
        AssertHighlighter("json",
"""
{"": "empty"}
""",
"""
<span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;empty&quot;</span><span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Object_SpecialCharKey()
    {
        AssertHighlighter("json",
"""
{"hello-world": 1, "foo.bar": 2}
""",
"""
<span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;hello-world&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1</span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;foo.bar&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">2</span><span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Object_UnicodeKey()
    {
        AssertHighlighter("json",
"""
{"\u4e2d": "chinese"}
""",
"""
<span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;\u4e2d&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;chinese&quot;</span><span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Object_MultiLine()
    {
        AssertHighlighter("json",
"""
{
  "name": "alice",
  "age": 30
}
""",
"""
<span class="hljs-punctuation">{</span>
  <span class="hljs-attr">&quot;name&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;alice&quot;</span><span class="hljs-punctuation">,</span>
  <span class="hljs-attr">&quot;age&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">30</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Object_MultiLineNested()
    {
        AssertHighlighter("json",
"""
{
  "user": {
    "name": "alice"
  }
}
""",
"""
<span class="hljs-punctuation">{</span>
  <span class="hljs-attr">&quot;user&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">{</span>
    <span class="hljs-attr">&quot;name&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;alice&quot;</span>
  <span class="hljs-punctuation">}</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Composite_PackageJson()
    {
        AssertHighlighter("json",
"""
{"name": "demo", "version": "1.0.0", "dependencies": {"foo": "^1.0.0"}}
""",
"""
<span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;name&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;demo&quot;</span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;version&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;1.0.0&quot;</span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;dependencies&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;foo&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;^1.0.0&quot;</span><span class="hljs-punctuation">}</span><span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Composite_GeoJson()
    {
        AssertHighlighter("json",
"""
{"type": "Point", "coordinates": [125.6, 10.1]}
""",
"""
<span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;type&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-string">&quot;Point&quot;</span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;coordinates&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">[</span><span class="hljs-number">125.6</span><span class="hljs-punctuation">,</span> <span class="hljs-number">10.1</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Composite_ArrayOfMixed()
    {
        AssertHighlighter("json",
"""
[{"id": 1, "tags": ["a", "b"]}, {"id": 2, "tags": []}]
""",
"""
<span class="hljs-punctuation">[</span><span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;id&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1</span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;tags&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">[</span><span class="hljs-string">&quot;a&quot;</span><span class="hljs-punctuation">,</span> <span class="hljs-string">&quot;b&quot;</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">}</span><span class="hljs-punctuation">,</span> <span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;id&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">2</span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;tags&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">[</span><span class="hljs-punctuation">]</span><span class="hljs-punctuation">}</span><span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void Composite_DeepNesting()
    {
        AssertHighlighter("json",
"""
{"a": {"b": {"c": {"d": {"e": 1}}}}}
""",
"""
<span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;a&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;b&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;c&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;d&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;e&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1</span><span class="hljs-punctuation">}</span><span class="hljs-punctuation">}</span><span class="hljs-punctuation">}</span><span class="hljs-punctuation">}</span><span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Composite_MixedNumbers()
    {
        AssertHighlighter("json",
"""
{"int": 1, "neg": -1, "float": 1.5, "exp": 1e10, "tiny": 0.001}
""",
"""
<span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;int&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1</span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;neg&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">-1</span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;float&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1.5</span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;exp&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1e10</span><span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;tiny&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">0.001</span><span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Whitespace_CompactObject()
    {
        AssertHighlighter("json",
"""
{"a":1,"b":2}
""",
"""
<span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;a&quot;</span><span class="hljs-punctuation">:</span><span class="hljs-number">1</span><span class="hljs-punctuation">,</span><span class="hljs-attr">&quot;b&quot;</span><span class="hljs-punctuation">:</span><span class="hljs-number">2</span><span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Whitespace_SpacedObject()
    {
        AssertHighlighter("json",
"""
{ "a" : 1 , "b" : 2 }
""",
"""
<span class="hljs-punctuation">{</span> <span class="hljs-attr">&quot;a&quot;</span> <span class="hljs-punctuation">:</span> <span class="hljs-number">1</span> <span class="hljs-punctuation">,</span> <span class="hljs-attr">&quot;b&quot;</span> <span class="hljs-punctuation">:</span> <span class="hljs-number">2</span> <span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void Whitespace_CompactArray()
    {
        AssertHighlighter("json",
"""
[1,2,3]
""",
"""
<span class="hljs-punctuation">[</span><span class="hljs-number">1</span><span class="hljs-punctuation">,</span><span class="hljs-number">2</span><span class="hljs-punctuation">,</span><span class="hljs-number">3</span><span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void Whitespace_SpacedArray()
    {
        AssertHighlighter("json",
"""
[ 1 , 2 , 3 ]
""",
"""
<span class="hljs-punctuation">[</span> <span class="hljs-number">1</span> <span class="hljs-punctuation">,</span> <span class="hljs-number">2</span> <span class="hljs-punctuation">,</span> <span class="hljs-number">3</span> <span class="hljs-punctuation">]</span>
""");
    }

    [Fact]
    public void Whitespace_TabIndented()
    {
        AssertHighlighter("json",
"""
{
	"a": 1
}
""",
"""
<span class="hljs-punctuation">{</span>
	<span class="hljs-attr">&quot;a&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1</span>
<span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("json",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyWhitespace()
    {
        AssertHighlighter("json",
"""


""",
"""


""");
    }

    [Fact]
    public void SpecialEdge_LeadingWhitespace()
    {
        AssertHighlighter("json",
"""
   {"a": 1}
""",
"""
   <span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;a&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1</span><span class="hljs-punctuation">}</span>
""");
    }

    [Fact]
    public void SpecialEdge_TrailingNewline()
    {
        AssertHighlighter("json",
"""
{"a": 1}

""",
"""
<span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;a&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1</span><span class="hljs-punctuation">}</span>

""");
    }

    [Fact]
    public void SpecialEdge_SingleNumber()
    {
        AssertHighlighter("json",
"""
42
""",
"""
<span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void SpecialEdge_SingleString()
    {
        AssertHighlighter("json",
"""
"hello"
""",
"""
<span class="hljs-string">&quot;hello&quot;</span>
""");
    }

    [Fact]
    public void SpecialEdge_SingleNull()
    {
        AssertHighlighter("json",
"""
null
""",
"""
<span class="hljs-literal"><span class="hljs-keyword">null</span></span>
""");
    }
}
