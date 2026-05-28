namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class JavaScriptHighlighterTests
{

    [Fact]
    public void Keyword_Var()
    {
        AssertHighlighter("javascript",
"""
var x = 1;
""",
"""
<span class="hljs-keyword">var</span> x = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Keyword_Let()
    {
        AssertHighlighter("javascript",
"""
let x = 1;
""",
"""
<span class="hljs-keyword">let</span> x = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Keyword_Const()
    {
        AssertHighlighter("javascript",
"""
const x = 1;
""",
"""
<span class="hljs-keyword">const</span> x = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Keyword_Function()
    {
        AssertHighlighter("javascript",
"""
function foo() {}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">foo</span>(<span class="hljs-params"></span>) {}
""");
    }

    [Fact]
    public void Keyword_Return()
    {
        AssertHighlighter("javascript",
"""
function f() { return 1; }
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"></span>) { <span class="hljs-keyword">return</span> <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Keyword_If()
    {
        AssertHighlighter("javascript",
"""
if (x) {}
""",
"""
<span class="hljs-keyword">if</span> (x) {}
""");
    }

    [Fact]
    public void Keyword_IfElse()
    {
        AssertHighlighter("javascript",
"""
if (x) {} else {}
""",
"""
<span class="hljs-keyword">if</span> (x) {} <span class="hljs-keyword">else</span> {}
""");
    }

    [Fact]
    public void Keyword_ElseIf()
    {
        AssertHighlighter("javascript",
"""
if (x) {} else if (y) {}
""",
"""
<span class="hljs-keyword">if</span> (x) {} <span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> (y) {}
""");
    }

    [Fact]
    public void Keyword_Switch()
    {
        AssertHighlighter("javascript",
"""
switch (x) { case 1: break; }
""",
"""
<span class="hljs-keyword">switch</span> (x) { <span class="hljs-keyword">case</span> <span class="hljs-number">1</span>: <span class="hljs-keyword">break</span>; }
""");
    }

    [Fact]
    public void Keyword_Case()
    {
        AssertHighlighter("javascript",
"""
switch (x) { case 1: break; default: break; }
""",
"""
<span class="hljs-keyword">switch</span> (x) { <span class="hljs-keyword">case</span> <span class="hljs-number">1</span>: <span class="hljs-keyword">break</span>; <span class="hljs-attr">default</span>: <span class="hljs-keyword">break</span>; }
""");
    }

    [Fact]
    public void Keyword_Default()
    {
        AssertHighlighter("javascript",
"""
switch (x) { default: break; }
""",
"""
<span class="hljs-keyword">switch</span> (x) { <span class="hljs-attr">default</span>: <span class="hljs-keyword">break</span>; }
""");
    }

    [Fact]
    public void Keyword_Break()
    {
        AssertHighlighter("javascript",
"""
for (;;) { break; }
""",
"""
<span class="hljs-keyword">for</span> (;;) { <span class="hljs-keyword">break</span>; }
""");
    }

    [Fact]
    public void Keyword_Continue()
    {
        AssertHighlighter("javascript",
"""
for (;;) { continue; }
""",
"""
<span class="hljs-keyword">for</span> (;;) { <span class="hljs-keyword">continue</span>; }
""");
    }

    [Fact]
    public void Keyword_For()
    {
        AssertHighlighter("javascript",
"""
for (let i = 0; i < 10; i++) {}
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-keyword">let</span> i = <span class="hljs-number">0</span>; i &lt; <span class="hljs-number">10</span>; i++) {}
""");
    }

    [Fact]
    public void Keyword_While()
    {
        AssertHighlighter("javascript",
"""
while (x) {}
""",
"""
<span class="hljs-keyword">while</span> (x) {}
""");
    }

    [Fact]
    public void Keyword_DoWhile()
    {
        AssertHighlighter("javascript",
"""
do {} while (x);
""",
"""
<span class="hljs-keyword">do</span> {} <span class="hljs-keyword">while</span> (x);
""");
    }

    [Fact]
    public void Keyword_Try()
    {
        AssertHighlighter("javascript",
"""
try {} catch (e) {}
""",
"""
<span class="hljs-keyword">try</span> {} <span class="hljs-keyword">catch</span> (e) {}
""");
    }

    [Fact]
    public void Keyword_Catch()
    {
        AssertHighlighter("javascript",
"""
try {} catch (e) {}
""",
"""
<span class="hljs-keyword">try</span> {} <span class="hljs-keyword">catch</span> (e) {}
""");
    }

    [Fact]
    public void Keyword_Finally()
    {
        AssertHighlighter("javascript",
"""
try {} catch (e) {} finally {}
""",
"""
<span class="hljs-keyword">try</span> {} <span class="hljs-keyword">catch</span> (e) {} <span class="hljs-keyword">finally</span> {}
""");
    }

    [Fact]
    public void Keyword_Throw()
    {
        AssertHighlighter("javascript",
"""
throw new Error("x");
""",
"""
<span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">Error</span>(<span class="hljs-string">&quot;x&quot;</span>);
""");
    }

    [Fact]
    public void Keyword_New()
    {
        AssertHighlighter("javascript",
"""
const x = new Foo();
""",
"""
<span class="hljs-keyword">const</span> x = <span class="hljs-keyword">new</span> <span class="hljs-title class_">Foo</span>();
""");
    }

    [Fact]
    public void Keyword_Delete()
    {
        AssertHighlighter("javascript",
"""
delete obj.x;
""",
"""
<span class="hljs-keyword">delete</span> obj.<span class="hljs-property">x</span>;
""");
    }

    [Fact]
    public void Keyword_Typeof()
    {
        AssertHighlighter("javascript",
"""
typeof x;
""",
"""
<span class="hljs-keyword">typeof</span> x;
""");
    }

    [Fact]
    public void Keyword_Instanceof()
    {
        AssertHighlighter("javascript",
"""
x instanceof Foo;
""",
"""
x <span class="hljs-keyword">instanceof</span> <span class="hljs-title class_">Foo</span>;
""");
    }

    [Fact]
    public void Keyword_In()
    {
        AssertHighlighter("javascript",
"""
for (const k in obj) {}
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-keyword">const</span> k <span class="hljs-keyword">in</span> obj) {}
""");
    }

    [Fact]
    public void Keyword_Of()
    {
        AssertHighlighter("javascript",
"""
for (const v of arr) {}
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-keyword">const</span> v <span class="hljs-keyword">of</span> arr) {}
""");
    }

    [Fact]
    public void Keyword_Void()
    {
        AssertHighlighter("javascript",
"""
void 0;
""",
"""
<span class="hljs-keyword">void</span> <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Keyword_Yield()
    {
        AssertHighlighter("javascript",
"""
function* g() { yield 1; }
""",
"""
<span class="hljs-keyword">function</span>* <span class="hljs-title function_">g</span>(<span class="hljs-params"></span>) { <span class="hljs-keyword">yield</span> <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Keyword_YieldStar()
    {
        AssertHighlighter("javascript",
"""
function* g() { yield* h(); }
""",
"""
<span class="hljs-keyword">function</span>* <span class="hljs-title function_">g</span>(<span class="hljs-params"></span>) { <span class="hljs-keyword">yield</span>* <span class="hljs-title function_">h</span>(); }
""");
    }

    [Fact]
    public void Keyword_Await()
    {
        AssertHighlighter("javascript",
"""
async function f() { await p; }
""",
"""
<span class="hljs-keyword">async</span> <span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"></span>) { <span class="hljs-keyword">await</span> p; }
""");
    }

    [Fact]
    public void Keyword_Async()
    {
        AssertHighlighter("javascript",
"""
async function f() {}
""",
"""
<span class="hljs-keyword">async</span> <span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"></span>) {}
""");
    }

    [Fact]
    public void Keyword_This()
    {
        AssertHighlighter("javascript",
"""
function f() { return this; }
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"></span>) { <span class="hljs-keyword">return</span> <span class="hljs-variable language_">this</span>; }
""");
    }

    [Fact]
    public void Keyword_Super()
    {
        AssertHighlighter("javascript",
"""
class A extends B { f() { super.f(); } }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> <span class="hljs-keyword">extends</span> <span class="hljs-title class_ inherited__">B</span> { <span class="hljs-title function_">f</span>(<span class="hljs-params"></span>) { <span class="hljs-variable language_">super</span>.<span class="hljs-title function_">f</span>(); } }
""");
    }

    [Fact]
    public void Keyword_Class()
    {
        AssertHighlighter("javascript",
"""
class MyClass {}
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">MyClass</span> {}
""");
    }

    [Fact]
    public void Keyword_Extends()
    {
        AssertHighlighter("javascript",
"""
class A extends B {}
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> <span class="hljs-keyword">extends</span> <span class="hljs-title class_ inherited__">B</span> {}
""");
    }

    [Fact]
    public void Keyword_Static()
    {
        AssertHighlighter("javascript",
"""
class A { static x = 1; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">static</span> x = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Keyword_Get()
    {
        AssertHighlighter("javascript",
"""
class A { get x() { return 1; } }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">get</span> <span class="hljs-title function_">x</span>() { <span class="hljs-keyword">return</span> <span class="hljs-number">1</span>; } }
""");
    }

    [Fact]
    public void Keyword_Set()
    {
        AssertHighlighter("javascript",
"""
class A { set x(v) {} }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">set</span> <span class="hljs-title function_">x</span>(<span class="hljs-params">v</span>) {} }
""");
    }

    [Fact]
    public void Keyword_Debugger()
    {
        AssertHighlighter("javascript",
"""
debugger;
""",
"""
<span class="hljs-keyword">debugger</span>;
""");
    }

    [Fact]
    public void Keyword_With()
    {
        AssertHighlighter("javascript",
"""
with (x) {}
""",
"""
<span class="hljs-title function_">with</span> (x) {}
""");
    }

    [Fact]
    public void Literal_True()
    {
        AssertHighlighter("javascript",
"""
const a = true;
""",
"""
<span class="hljs-keyword">const</span> a = <span class="hljs-literal">true</span>;
""");
    }

    [Fact]
    public void Literal_False()
    {
        AssertHighlighter("javascript",
"""
const a = false;
""",
"""
<span class="hljs-keyword">const</span> a = <span class="hljs-literal">false</span>;
""");
    }

    [Fact]
    public void Literal_Null()
    {
        AssertHighlighter("javascript",
"""
const a = null;
""",
"""
<span class="hljs-keyword">const</span> a = <span class="hljs-literal">null</span>;
""");
    }

    [Fact]
    public void Literal_Undefined()
    {
        AssertHighlighter("javascript",
"""
const a = undefined;
""",
"""
<span class="hljs-keyword">const</span> a = <span class="hljs-literal">undefined</span>;
""");
    }

    [Fact]
    public void Literal_NaN()
    {
        AssertHighlighter("javascript",
"""
const a = NaN;
""",
"""
<span class="hljs-keyword">const</span> a = <span class="hljs-title class_">NaN</span>;
""");
    }

    [Fact]
    public void Literal_Infinity()
    {
        AssertHighlighter("javascript",
"""
const a = Infinity;
""",
"""
<span class="hljs-keyword">const</span> a = <span class="hljs-title class_">Infinity</span>;
""");
    }

    [Fact]
    public void Number_Integer()
    {
        AssertHighlighter("javascript",
"""
const n = 42;
""",
"""
<span class="hljs-keyword">const</span> n = <span class="hljs-number">42</span>;
""");
    }

    [Fact]
    public void Number_Zero()
    {
        AssertHighlighter("javascript",
"""
const n = 0;
""",
"""
<span class="hljs-keyword">const</span> n = <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Number_Float()
    {
        AssertHighlighter("javascript",
"""
const n = 3.14;
""",
"""
<span class="hljs-keyword">const</span> n = <span class="hljs-number">3.14</span>;
""");
    }

    [Fact]
    public void Number_FloatLeadingDot()
    {
        AssertHighlighter("javascript",
"""
const n = .5;
""",
"""
<span class="hljs-keyword">const</span> n = <span class="hljs-number">.5</span>;
""");
    }

    [Fact]
    public void Number_FloatTrailingDot()
    {
        AssertHighlighter("javascript",
"""
const n = 1.;
""",
"""
<span class="hljs-keyword">const</span> n = <span class="hljs-number">1.</span>;
""");
    }

    [Fact]
    public void Number_Hex()
    {
        AssertHighlighter("javascript",
"""
const n = 0xFF;
""",
"""
<span class="hljs-keyword">const</span> n = <span class="hljs-number">0xFF</span>;
""");
    }

    [Fact]
    public void Number_HexLower()
    {
        AssertHighlighter("javascript",
"""
const n = 0xabcdef;
""",
"""
<span class="hljs-keyword">const</span> n = <span class="hljs-number">0xabcdef</span>;
""");
    }

    [Fact]
    public void Number_Octal()
    {
        AssertHighlighter("javascript",
"""
const n = 0o17;
""",
"""
<span class="hljs-keyword">const</span> n = <span class="hljs-number">0o17</span>;
""");
    }

    [Fact]
    public void Number_Binary()
    {
        AssertHighlighter("javascript",
"""
const n = 0b1010;
""",
"""
<span class="hljs-keyword">const</span> n = <span class="hljs-number">0b1010</span>;
""");
    }

    [Fact]
    public void Number_BigInt()
    {
        AssertHighlighter("javascript",
"""
const n = 123n;
""",
"""
<span class="hljs-keyword">const</span> n = <span class="hljs-number">123n</span>;
""");
    }

    [Fact]
    public void Number_BigIntHex()
    {
        AssertHighlighter("javascript",
"""
const n = 0xffn;
""",
"""
<span class="hljs-keyword">const</span> n = <span class="hljs-number">0xffn</span>;
""");
    }

    [Fact]
    public void Number_Separator()
    {
        AssertHighlighter("javascript",
"""
const n = 1_000_000;
""",
"""
<span class="hljs-keyword">const</span> n = <span class="hljs-number">1_000_000</span>;
""");
    }

    [Fact]
    public void Number_ExponentPositive()
    {
        AssertHighlighter("javascript",
"""
const n = 1e10;
""",
"""
<span class="hljs-keyword">const</span> n = <span class="hljs-number">1e10</span>;
""");
    }

    [Fact]
    public void Number_ExponentNegative()
    {
        AssertHighlighter("javascript",
"""
const n = 1.5e-3;
""",
"""
<span class="hljs-keyword">const</span> n = <span class="hljs-number">1.5e-3</span>;
""");
    }

    [Fact]
    public void Number_ExponentExplicit()
    {
        AssertHighlighter("javascript",
"""
const n = 2.5e+4;
""",
"""
<span class="hljs-keyword">const</span> n = <span class="hljs-number">2.5e+4</span>;
""");
    }

    [Fact]
    public void String_SingleQuote()
    {
        AssertHighlighter("javascript",
"""
const s = 'hello';
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">&#x27;hello&#x27;</span>;
""");
    }

    [Fact]
    public void String_DoubleQuote()
    {
        AssertHighlighter("javascript",
"""
const s = "hello";
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">&quot;hello&quot;</span>;
""");
    }

    [Fact]
    public void String_EmptySingle()
    {
        AssertHighlighter("javascript",
"""
const s = '';
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">&#x27;&#x27;</span>;
""");
    }

    [Fact]
    public void String_EmptyDouble()
    {
        AssertHighlighter("javascript",
"""
const s = "";
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">&quot;&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeNewline()
    {
        AssertHighlighter("javascript",
"""
const s = "line1\nline2";
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">&quot;line1\nline2&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeTab()
    {
        AssertHighlighter("javascript",
"""
const s = "a\tb";
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">&quot;a\tb&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeBackslash()
    {
        AssertHighlighter("javascript",
"""
const s = "a\\b";
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">&quot;a\\b&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeQuote()
    {
        AssertHighlighter("javascript",
"""
const s = "she said \"hi\"";
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">&quot;she said \&quot;hi\&quot;&quot;</span>;
""");
    }

    [Fact]
    public void String_UnicodeEscape()
    {
        AssertHighlighter("javascript",
"""
const s = "\u0041";
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">&quot;\u0041&quot;</span>;
""");
    }

    [Fact]
    public void String_UnicodeBraces()
    {
        AssertHighlighter("javascript",
"""
const s = "\u{1F600}";
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">&quot;\u{1F600}&quot;</span>;
""");
    }

    [Fact]
    public void String_HexEscape()
    {
        AssertHighlighter("javascript",
"""
const s = "\x41";
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">&quot;\x41&quot;</span>;
""");
    }

    [Fact]
    public void Template_Plain()
    {
        AssertHighlighter("javascript",
"""
const s = `hello`;
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">`hello`</span>;
""");
    }

    [Fact]
    public void Template_Interpolation()
    {
        AssertHighlighter("javascript",
"""
const s = `hello ${name}`;
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">`hello <span class="hljs-subst">${name}</span>`</span>;
""");
    }

    [Fact]
    public void Template_MultiInterp()
    {
        AssertHighlighter("javascript",
"""
const s = `${a} and ${b}`;
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">`<span class="hljs-subst">${a}</span> and <span class="hljs-subst">${b}</span>`</span>;
""");
    }

    [Fact]
    public void Template_Nested()
    {
        AssertHighlighter("javascript",
"""
const s = `a ${`b ${c}`} d`;
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">`a <span class="hljs-subst">${<span class="hljs-string">`b <span class="hljs-subst">${c}</span>`</span>}</span> d`</span>;
""");
    }

    [Fact]
    public void Template_Tagged()
    {
        AssertHighlighter("javascript",
"""
const s = tag`hello ${name}`;
""",
"""
<span class="hljs-keyword">const</span> s = tag<span class="hljs-string">`hello <span class="hljs-subst">${name}</span>`</span>;
""");
    }

    [Fact]
    public void Template_MultiLine()
    {
        AssertHighlighter("javascript",
"""
const s = `line1
line2`;
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">`line1
line2`</span>;
""");
    }

    [Fact]
    public void Template_WithExpression()
    {
        AssertHighlighter("javascript",
"""
const s = `result: ${1 + 2}`;
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">`result: <span class="hljs-subst">${<span class="hljs-number">1</span> + <span class="hljs-number">2</span>}</span>`</span>;
""");
    }

    [Fact]
    public void Regex_Simple()
    {
        AssertHighlighter("javascript",
"""
const re = /abc/;
""",
"""
<span class="hljs-keyword">const</span> re = <span class="hljs-regexp">/abc/</span>;
""");
    }

    [Fact]
    public void Regex_Flags()
    {
        AssertHighlighter("javascript",
"""
const re = /abc/gi;
""",
"""
<span class="hljs-keyword">const</span> re = <span class="hljs-regexp">/abc/gi</span>;
""");
    }

    [Fact]
    public void Regex_AllFlags()
    {
        AssertHighlighter("javascript",
"""
const re = /abc/gimsuy;
""",
"""
<span class="hljs-keyword">const</span> re = <span class="hljs-regexp">/abc/gim</span>suy;
""");
    }

    [Fact]
    public void Regex_CharClass()
    {
        AssertHighlighter("javascript",
"""
const re = /[a-zA-Z0-9_]+/;
""",
"""
<span class="hljs-keyword">const</span> re = <span class="hljs-regexp">/[a-zA-Z0-9_]+/</span>;
""");
    }

    [Fact]
    public void Regex_Digits()
    {
        AssertHighlighter("javascript",
"""
const re = /\d+/;
""",
"""
<span class="hljs-keyword">const</span> re = <span class="hljs-regexp">/\d+/</span>;
""");
    }

    [Fact]
    public void Regex_Anchors()
    {
        AssertHighlighter("javascript",
"""
const re = /^abc$/;
""",
"""
<span class="hljs-keyword">const</span> re = <span class="hljs-regexp">/^abc$/</span>;
""");
    }

    [Fact]
    public void Regex_Group()
    {
        AssertHighlighter("javascript",
"""
const re = /(abc|def)/;
""",
"""
<span class="hljs-keyword">const</span> re = <span class="hljs-regexp">/(abc|def)/</span>;
""");
    }

    [Fact]
    public void Regex_NamedGroup()
    {
        AssertHighlighter("javascript",
"""
const re = /(?<year>\d{4})/;
""",
"""
<span class="hljs-keyword">const</span> re = <span class="hljs-regexp">/(?&lt;year&gt;\d{4})/</span>;
""");
    }

    [Fact]
    public void Regex_Lookahead()
    {
        AssertHighlighter("javascript",
"""
const re = /a(?=b)/;
""",
"""
<span class="hljs-keyword">const</span> re = <span class="hljs-regexp">/a(?=b)/</span>;
""");
    }

    [Fact]
    public void Regex_Lookbehind()
    {
        AssertHighlighter("javascript",
"""
const re = /(?<=a)b/;
""",
"""
<span class="hljs-keyword">const</span> re = <span class="hljs-regexp">/(?&lt;=a)b/</span>;
""");
    }

    [Fact]
    public void Regex_Escapes()
    {
        AssertHighlighter("javascript",
"""
const re = /\.\\\//;
""",
"""
<span class="hljs-keyword">const</span> re = <span class="hljs-regexp">/\.\\\//</span>;
""");
    }

    [Fact]
    public void Comment_Line()
    {
        AssertHighlighter("javascript",
"""
// hello
""",
"""
<span class="hljs-comment">// hello</span>
""");
    }

    [Fact]
    public void Comment_LineInline()
    {
        AssertHighlighter("javascript",
"""
const x = 1; // trailing
""",
"""
<span class="hljs-keyword">const</span> x = <span class="hljs-number">1</span>; <span class="hljs-comment">// trailing</span>
""");
    }

    [Fact]
    public void Comment_Block()
    {
        AssertHighlighter("javascript",
"""
/* hello */
""",
"""
<span class="hljs-comment">/* hello */</span>
""");
    }

    [Fact]
    public void Comment_BlockMultiLine()
    {
        AssertHighlighter("javascript",
"""
/*
 * line
 */
""",
"""
<span class="hljs-comment">/*
 * line
 */</span>
""");
    }

    [Fact]
    public void Comment_JSDoc()
    {
        AssertHighlighter("javascript",
"""
/** @param {string} x */
function f(x) {}
""",
"""
<span class="hljs-comment">/** <span class="hljs-doctag">@param</span> {<span class="hljs-type">string</span>} x */</span>
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params">x</span>) {}
""");
    }

    [Fact]
    public void Comment_JSDocReturns()
    {
        AssertHighlighter("javascript",
"""
/**
 * @returns {number}
 */
function f() { return 1; }
""",
"""
<span class="hljs-comment">/**
 * <span class="hljs-doctag">@returns</span> {<span class="hljs-type">number</span>}
 */</span>
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"></span>) { <span class="hljs-keyword">return</span> <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Operator_Add()
    {
        AssertHighlighter("javascript",
"""
const x = a + b;
""",
"""
<span class="hljs-keyword">const</span> x = a + b;
""");
    }

    [Fact]
    public void Operator_Subtract()
    {
        AssertHighlighter("javascript",
"""
const x = a - b;
""",
"""
<span class="hljs-keyword">const</span> x = a - b;
""");
    }

    [Fact]
    public void Operator_Multiply()
    {
        AssertHighlighter("javascript",
"""
const x = a * b;
""",
"""
<span class="hljs-keyword">const</span> x = a * b;
""");
    }

    [Fact]
    public void Operator_Divide()
    {
        AssertHighlighter("javascript",
"""
const x = a / b;
""",
"""
<span class="hljs-keyword">const</span> x = a / b;
""");
    }

    [Fact]
    public void Operator_Modulo()
    {
        AssertHighlighter("javascript",
"""
const x = a % b;
""",
"""
<span class="hljs-keyword">const</span> x = a % b;
""");
    }

    [Fact]
    public void Operator_Power()
    {
        AssertHighlighter("javascript",
"""
const x = a ** b;
""",
"""
<span class="hljs-keyword">const</span> x = a ** b;
""");
    }

    [Fact]
    public void Operator_Increment()
    {
        AssertHighlighter("javascript",
"""
a++;
""",
"""
a++;
""");
    }

    [Fact]
    public void Operator_Decrement()
    {
        AssertHighlighter("javascript",
"""
a--;
""",
"""
a--;
""");
    }

    [Fact]
    public void Operator_Equal()
    {
        AssertHighlighter("javascript",
"""
a == b;
""",
"""
a == b;
""");
    }

    [Fact]
    public void Operator_StrictEqual()
    {
        AssertHighlighter("javascript",
"""
a === b;
""",
"""
a === b;
""");
    }

    [Fact]
    public void Operator_NotEqual()
    {
        AssertHighlighter("javascript",
"""
a != b;
""",
"""
a != b;
""");
    }

    [Fact]
    public void Operator_StrictNotEqual()
    {
        AssertHighlighter("javascript",
"""
a !== b;
""",
"""
a !== b;
""");
    }

    [Fact]
    public void Operator_Less()
    {
        AssertHighlighter("javascript",
"""
a < b;
""",
"""
a &lt; b;
""");
    }

    [Fact]
    public void Operator_Greater()
    {
        AssertHighlighter("javascript",
"""
a > b;
""",
"""
a &gt; b;
""");
    }

    [Fact]
    public void Operator_LessEq()
    {
        AssertHighlighter("javascript",
"""
a <= b;
""",
"""
a &lt;= b;
""");
    }

    [Fact]
    public void Operator_GreaterEq()
    {
        AssertHighlighter("javascript",
"""
a >= b;
""",
"""
a &gt;= b;
""");
    }

    [Fact]
    public void Operator_LogicalAnd()
    {
        AssertHighlighter("javascript",
"""
a && b;
""",
"""
a &amp;&amp; b;
""");
    }

    [Fact]
    public void Operator_LogicalOr()
    {
        AssertHighlighter("javascript",
"""
a || b;
""",
"""
a || b;
""");
    }

    [Fact]
    public void Operator_LogicalNot()
    {
        AssertHighlighter("javascript",
"""
!a;
""",
"""
!a;
""");
    }

    [Fact]
    public void Operator_Nullish()
    {
        AssertHighlighter("javascript",
"""
a ?? b;
""",
"""
a ?? b;
""");
    }

    [Fact]
    public void Operator_OptionalChain()
    {
        AssertHighlighter("javascript",
"""
a?.b;
""",
"""
a?.<span class="hljs-property">b</span>;
""");
    }

    [Fact]
    public void Operator_OptionalCall()
    {
        AssertHighlighter("javascript",
"""
a?.();
""",
"""
a?.();
""");
    }

    [Fact]
    public void Operator_OptionalIndex()
    {
        AssertHighlighter("javascript",
"""
a?.[0];
""",
"""
a?.[<span class="hljs-number">0</span>];
""");
    }

    [Fact]
    public void Operator_SpreadArray()
    {
        AssertHighlighter("javascript",
"""
const a = [...b];
""",
"""
<span class="hljs-keyword">const</span> a = [...b];
""");
    }

    [Fact]
    public void Operator_SpreadObject()
    {
        AssertHighlighter("javascript",
"""
const a = {...b};
""",
"""
<span class="hljs-keyword">const</span> a = {...b};
""");
    }

    [Fact]
    public void Operator_RestParam()
    {
        AssertHighlighter("javascript",
"""
function f(...args) {}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params">...args</span>) {}
""");
    }

    [Fact]
    public void Operator_Ternary()
    {
        AssertHighlighter("javascript",
"""
const x = a ? b : c;
""",
"""
<span class="hljs-keyword">const</span> x = a ? b : c;
""");
    }

    [Fact]
    public void Operator_Assign()
    {
        AssertHighlighter("javascript",
"""
a = 1;
""",
"""
a = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Operator_AddAssign()
    {
        AssertHighlighter("javascript",
"""
a += 1;
""",
"""
a += <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Operator_SubAssign()
    {
        AssertHighlighter("javascript",
"""
a -= 1;
""",
"""
a -= <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Operator_PowerAssign()
    {
        AssertHighlighter("javascript",
"""
a **= 2;
""",
"""
a **= <span class="hljs-number">2</span>;
""");
    }

    [Fact]
    public void Operator_AndAssign()
    {
        AssertHighlighter("javascript",
"""
a &&= b;
""",
"""
a &amp;&amp;= b;
""");
    }

    [Fact]
    public void Operator_OrAssign()
    {
        AssertHighlighter("javascript",
"""
a ||= b;
""",
"""
a ||= b;
""");
    }

    [Fact]
    public void Operator_NullishAssign()
    {
        AssertHighlighter("javascript",
"""
a ??= b;
""",
"""
a ??= b;
""");
    }

    [Fact]
    public void Operator_BitwiseAnd()
    {
        AssertHighlighter("javascript",
"""
a & b;
""",
"""
a &amp; b;
""");
    }

    [Fact]
    public void Operator_BitwiseOr()
    {
        AssertHighlighter("javascript",
"""
a | b;
""",
"""
a | b;
""");
    }

    [Fact]
    public void Operator_BitwiseXor()
    {
        AssertHighlighter("javascript",
"""
a ^ b;
""",
"""
a ^ b;
""");
    }

    [Fact]
    public void Operator_BitwiseNot()
    {
        AssertHighlighter("javascript",
"""
~a;
""",
"""
~a;
""");
    }

    [Fact]
    public void Operator_ShiftLeft()
    {
        AssertHighlighter("javascript",
"""
a << 1;
""",
"""
a &lt;&lt; <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Operator_ShiftRight()
    {
        AssertHighlighter("javascript",
"""
a >> 1;
""",
"""
a &gt;&gt; <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Operator_ShiftRightUnsigned()
    {
        AssertHighlighter("javascript",
"""
a >>> 1;
""",
"""
a &gt;&gt;&gt; <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Operator_Comma()
    {
        AssertHighlighter("javascript",
"""
const a = (1, 2);
""",
"""
<span class="hljs-keyword">const</span> a = (<span class="hljs-number">1</span>, <span class="hljs-number">2</span>);
""");
    }

    [Fact]
    public void Destructure_ObjectBasic()
    {
        AssertHighlighter("javascript",
"""
const { a, b } = obj;
""",
"""
<span class="hljs-keyword">const</span> { a, b } = obj;
""");
    }

    [Fact]
    public void Destructure_ObjectRename()
    {
        AssertHighlighter("javascript",
"""
const { a: x, b: y } = obj;
""",
"""
<span class="hljs-keyword">const</span> { <span class="hljs-attr">a</span>: x, <span class="hljs-attr">b</span>: y } = obj;
""");
    }

    [Fact]
    public void Destructure_ObjectDefault()
    {
        AssertHighlighter("javascript",
"""
const { a = 1 } = obj;
""",
"""
<span class="hljs-keyword">const</span> { a = <span class="hljs-number">1</span> } = obj;
""");
    }

    [Fact]
    public void Destructure_ObjectRest()
    {
        AssertHighlighter("javascript",
"""
const { a, ...rest } = obj;
""",
"""
<span class="hljs-keyword">const</span> { a, ...rest } = obj;
""");
    }

    [Fact]
    public void Destructure_ArrayBasic()
    {
        AssertHighlighter("javascript",
"""
const [a, b] = arr;
""",
"""
<span class="hljs-keyword">const</span> [a, b] = arr;
""");
    }

    [Fact]
    public void Destructure_ArrayDefault()
    {
        AssertHighlighter("javascript",
"""
const [a = 1, b = 2] = arr;
""",
"""
<span class="hljs-keyword">const</span> [a = <span class="hljs-number">1</span>, b = <span class="hljs-number">2</span>] = arr;
""");
    }

    [Fact]
    public void Destructure_ArrayRest()
    {
        AssertHighlighter("javascript",
"""
const [a, ...rest] = arr;
""",
"""
<span class="hljs-keyword">const</span> [a, ...rest] = arr;
""");
    }

    [Fact]
    public void Destructure_ArraySkip()
    {
        AssertHighlighter("javascript",
"""
const [, , c] = arr;
""",
"""
<span class="hljs-keyword">const</span> [, , c] = arr;
""");
    }

    [Fact]
    public void Destructure_Nested()
    {
        AssertHighlighter("javascript",
"""
const { a: { b } } = obj;
""",
"""
<span class="hljs-keyword">const</span> { <span class="hljs-attr">a</span>: { b } } = obj;
""");
    }

    [Fact]
    public void Destructure_ParamObject()
    {
        AssertHighlighter("javascript",
"""
function f({ a, b }) {}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params">{ a, b }</span>) {}
""");
    }

    [Fact]
    public void Destructure_ParamArray()
    {
        AssertHighlighter("javascript",
"""
function f([a, b]) {}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params">[a, b]</span>) {}
""");
    }

    [Fact]
    public void Function_Declaration()
    {
        AssertHighlighter("javascript",
"""
function foo() {}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">foo</span>(<span class="hljs-params"></span>) {}
""");
    }

    [Fact]
    public void Function_WithArgs()
    {
        AssertHighlighter("javascript",
"""
function foo(a, b) { return a + b; }
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">foo</span>(<span class="hljs-params">a, b</span>) { <span class="hljs-keyword">return</span> a + b; }
""");
    }

    [Fact]
    public void Function_Expression()
    {
        AssertHighlighter("javascript",
"""
const foo = function () {};
""",
"""
<span class="hljs-keyword">const</span> foo = <span class="hljs-keyword">function</span> (<span class="hljs-params"></span>) {};
""");
    }

    [Fact]
    public void Function_NamedExpression()
    {
        AssertHighlighter("javascript",
"""
const foo = function bar() {};
""",
"""
<span class="hljs-keyword">const</span> foo = <span class="hljs-keyword">function</span> <span class="hljs-title function_">bar</span>(<span class="hljs-params"></span>) {};
""");
    }

    [Fact]
    public void Function_Arrow()
    {
        AssertHighlighter("javascript",
"""
const f = () => 1;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-title function_">f</span> = (<span class="hljs-params"></span>) =&gt; <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Function_ArrowBody()
    {
        AssertHighlighter("javascript",
"""
const f = () => { return 1; };
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-title function_">f</span> = (<span class="hljs-params"></span>) =&gt; { <span class="hljs-keyword">return</span> <span class="hljs-number">1</span>; };
""");
    }

    [Fact]
    public void Function_ArrowSingleArg()
    {
        AssertHighlighter("javascript",
"""
const f = x => x + 1;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-title function_">f</span> = x =&gt; x + <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Function_ArrowParenArgs()
    {
        AssertHighlighter("javascript",
"""
const f = (a, b) => a + b;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-title function_">f</span> = (<span class="hljs-params">a, b</span>) =&gt; a + b;
""");
    }

    [Fact]
    public void Function_Async()
    {
        AssertHighlighter("javascript",
"""
async function f() {}
""",
"""
<span class="hljs-keyword">async</span> <span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"></span>) {}
""");
    }

    [Fact]
    public void Function_AsyncArrow()
    {
        AssertHighlighter("javascript",
"""
const f = async () => {};
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-title function_">f</span> = <span class="hljs-keyword">async</span> (<span class="hljs-params"></span>) =&gt; {};
""");
    }

    [Fact]
    public void Function_Generator()
    {
        AssertHighlighter("javascript",
"""
function* g() { yield 1; }
""",
"""
<span class="hljs-keyword">function</span>* <span class="hljs-title function_">g</span>(<span class="hljs-params"></span>) { <span class="hljs-keyword">yield</span> <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Function_AsyncGenerator()
    {
        AssertHighlighter("javascript",
"""
async function* g() { yield 1; }
""",
"""
<span class="hljs-keyword">async</span> <span class="hljs-keyword">function</span>* <span class="hljs-title function_">g</span>(<span class="hljs-params"></span>) { <span class="hljs-keyword">yield</span> <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Function_DefaultParams()
    {
        AssertHighlighter("javascript",
"""
function f(a = 1, b = 2) {}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params">a = <span class="hljs-number">1</span>, b = <span class="hljs-number">2</span></span>) {}
""");
    }

    [Fact]
    public void Function_RestParams()
    {
        AssertHighlighter("javascript",
"""
function f(...args) {}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params">...args</span>) {}
""");
    }

    [Fact]
    public void Function_IIFE()
    {
        AssertHighlighter("javascript",
"""
(function () { return 1; })();
""",
"""
(<span class="hljs-keyword">function</span> (<span class="hljs-params"></span>) { <span class="hljs-keyword">return</span> <span class="hljs-number">1</span>; })();
""");
    }

    [Fact]
    public void Function_IIFEArrow()
    {
        AssertHighlighter("javascript",
"""
(() => 1)();
""",
"""
(<span class="hljs-function">() =&gt;</span> <span class="hljs-number">1</span>)();
""");
    }

    [Fact]
    public void Class_Empty()
    {
        AssertHighlighter("javascript",
"""
class Foo {}
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Foo</span> {}
""");
    }

    [Fact]
    public void Class_Extends()
    {
        AssertHighlighter("javascript",
"""
class A extends B {}
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> <span class="hljs-keyword">extends</span> <span class="hljs-title class_ inherited__">B</span> {}
""");
    }

    [Fact]
    public void Class_Constructor()
    {
        AssertHighlighter("javascript",
"""
class A { constructor() {} }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-title function_">constructor</span>(<span class="hljs-params"></span>) {} }
""");
    }

    [Fact]
    public void Class_ConstructorArgs()
    {
        AssertHighlighter("javascript",
"""
class A { constructor(x, y) { this.x = x; } }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-title function_">constructor</span>(<span class="hljs-params">x, y</span>) { <span class="hljs-variable language_">this</span>.<span class="hljs-property">x</span> = x; } }
""");
    }

    [Fact]
    public void Class_Method()
    {
        AssertHighlighter("javascript",
"""
class A { foo() {} }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-title function_">foo</span>(<span class="hljs-params"></span>) {} }
""");
    }

    [Fact]
    public void Class_AsyncMethod()
    {
        AssertHighlighter("javascript",
"""
class A { async foo() {} }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">async</span> <span class="hljs-title function_">foo</span>(<span class="hljs-params"></span>) {} }
""");
    }

    [Fact]
    public void Class_StaticField()
    {
        AssertHighlighter("javascript",
"""
class A { static x = 1; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">static</span> x = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Class_InstanceField()
    {
        AssertHighlighter("javascript",
"""
class A { x = 1; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { x = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Class_StaticMethod()
    {
        AssertHighlighter("javascript",
"""
class A { static foo() {} }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">static</span> <span class="hljs-title function_">foo</span>(<span class="hljs-params"></span>) {} }
""");
    }

    [Fact]
    public void Class_PrivateField()
    {
        AssertHighlighter("javascript",
"""
class A { #x = 1; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { #x = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Class_PrivateMethod()
    {
        AssertHighlighter("javascript",
"""
class A { #foo() {} }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { #<span class="hljs-title function_">foo</span>(<span class="hljs-params"></span>) {} }
""");
    }

    [Fact]
    public void Class_Getter()
    {
        AssertHighlighter("javascript",
"""
class A { get x() { return 1; } }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">get</span> <span class="hljs-title function_">x</span>() { <span class="hljs-keyword">return</span> <span class="hljs-number">1</span>; } }
""");
    }

    [Fact]
    public void Class_Setter()
    {
        AssertHighlighter("javascript",
"""
class A { set x(v) {} }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">set</span> <span class="hljs-title function_">x</span>(<span class="hljs-params">v</span>) {} }
""");
    }

    [Fact]
    public void Class_StaticBlock()
    {
        AssertHighlighter("javascript",
"""
class A { static { console.log("init"); } }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">static</span> { <span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(<span class="hljs-string">&quot;init&quot;</span>); } }
""");
    }

    [Fact]
    public void Class_Expression()
    {
        AssertHighlighter("javascript",
"""
const A = class {};
""",
"""
<span class="hljs-keyword">const</span> A = <span class="hljs-keyword">class</span> {};
""");
    }

    [Fact]
    public void Class_NamedExpression()
    {
        AssertHighlighter("javascript",
"""
const A = class B {};
""",
"""
<span class="hljs-keyword">const</span> A = <span class="hljs-keyword">class</span> <span class="hljs-title class_">B</span> {};
""");
    }

    [Fact]
    public void Module_ImportDefault()
    {
        AssertHighlighter("javascript",
"""
import x from 'mod';
""",
"""
<span class="hljs-keyword">import</span> x <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;mod&#x27;</span>;
""");
    }

    [Fact]
    public void Module_ImportNamed()
    {
        AssertHighlighter("javascript",
"""
import { x } from 'mod';
""",
"""
<span class="hljs-keyword">import</span> { x } <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;mod&#x27;</span>;
""");
    }

    [Fact]
    public void Module_ImportMultiple()
    {
        AssertHighlighter("javascript",
"""
import { x, y } from 'mod';
""",
"""
<span class="hljs-keyword">import</span> { x, y } <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;mod&#x27;</span>;
""");
    }

    [Fact]
    public void Module_ImportAlias()
    {
        AssertHighlighter("javascript",
"""
import { x as y } from 'mod';
""",
"""
<span class="hljs-keyword">import</span> { x <span class="hljs-keyword">as</span> y } <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;mod&#x27;</span>;
""");
    }

    [Fact]
    public void Module_ImportNamespace()
    {
        AssertHighlighter("javascript",
"""
import * as x from 'mod';
""",
"""
<span class="hljs-keyword">import</span> * <span class="hljs-keyword">as</span> x <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;mod&#x27;</span>;
""");
    }

    [Fact]
    public void Module_ImportMixed()
    {
        AssertHighlighter("javascript",
"""
import x, { y } from 'mod';
""",
"""
<span class="hljs-keyword">import</span> x, { y } <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;mod&#x27;</span>;
""");
    }

    [Fact]
    public void Module_ImportSideEffect()
    {
        AssertHighlighter("javascript",
"""
import 'mod';
""",
"""
<span class="hljs-keyword">import</span> <span class="hljs-string">&#x27;mod&#x27;</span>;
""");
    }

    [Fact]
    public void Module_ImportDynamic()
    {
        AssertHighlighter("javascript",
"""
const m = await import('mod');
""",
"""
<span class="hljs-keyword">const</span> m = <span class="hljs-keyword">await</span> <span class="hljs-keyword">import</span>(<span class="hljs-string">&#x27;mod&#x27;</span>);
""");
    }

    [Fact]
    public void Module_ExportDefault()
    {
        AssertHighlighter("javascript",
"""
export default 42;
""",
"""
<span class="hljs-keyword">export</span> <span class="hljs-keyword">default</span> <span class="hljs-number">42</span>;
""");
    }

    [Fact]
    public void Module_ExportDefaultFn()
    {
        AssertHighlighter("javascript",
"""
export default function () {};
""",
"""
<span class="hljs-keyword">export</span> <span class="hljs-keyword">default</span> <span class="hljs-keyword">function</span> (<span class="hljs-params"></span>) {};
""");
    }

    [Fact]
    public void Module_ExportNamed()
    {
        AssertHighlighter("javascript",
"""
export { x };
""",
"""
<span class="hljs-keyword">export</span> { x };
""");
    }

    [Fact]
    public void Module_ExportConst()
    {
        AssertHighlighter("javascript",
"""
export const x = 1;
""",
"""
<span class="hljs-keyword">export</span> <span class="hljs-keyword">const</span> x = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Module_ExportFunction()
    {
        AssertHighlighter("javascript",
"""
export function foo() {}
""",
"""
<span class="hljs-keyword">export</span> <span class="hljs-keyword">function</span> <span class="hljs-title function_">foo</span>(<span class="hljs-params"></span>) {}
""");
    }

    [Fact]
    public void Module_ExportClass()
    {
        AssertHighlighter("javascript",
"""
export class Foo {}
""",
"""
<span class="hljs-keyword">export</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Foo</span> {}
""");
    }

    [Fact]
    public void Module_ReExport()
    {
        AssertHighlighter("javascript",
"""
export { x } from 'mod';
""",
"""
<span class="hljs-keyword">export</span> { x } <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;mod&#x27;</span>;
""");
    }

    [Fact]
    public void Module_ReExportAll()
    {
        AssertHighlighter("javascript",
"""
export * from 'mod';
""",
"""
<span class="hljs-keyword">export</span> * <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;mod&#x27;</span>;
""");
    }

    [Fact]
    public void Module_ReExportAs()
    {
        AssertHighlighter("javascript",
"""
export * as ns from 'mod';
""",
"""
<span class="hljs-keyword">export</span> * <span class="hljs-keyword">as</span> ns <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;mod&#x27;</span>;
""");
    }

    [Fact]
    public void ControlFlow_Label()
    {
        AssertHighlighter("javascript",
"""
outer: for (;;) { break outer; }
""",
"""
<span class="hljs-attr">outer</span>: <span class="hljs-keyword">for</span> (;;) { <span class="hljs-keyword">break</span> outer; }
""");
    }

    [Fact]
    public void ControlFlow_ContinueLabel()
    {
        AssertHighlighter("javascript",
"""
outer: for (;;) { continue outer; }
""",
"""
<span class="hljs-attr">outer</span>: <span class="hljs-keyword">for</span> (;;) { <span class="hljs-keyword">continue</span> outer; }
""");
    }

    [Fact]
    public void ControlFlow_TryCatchFinally()
    {
        AssertHighlighter("javascript",
"""
try { f(); } catch (e) { g(e); } finally { h(); }
""",
"""
<span class="hljs-keyword">try</span> { <span class="hljs-title function_">f</span>(); } <span class="hljs-keyword">catch</span> (e) { <span class="hljs-title function_">g</span>(e); } <span class="hljs-keyword">finally</span> { <span class="hljs-title function_">h</span>(); }
""");
    }

    [Fact]
    public void ControlFlow_TryWithoutBinding()
    {
        AssertHighlighter("javascript",
"""
try { f(); } catch { g(); }
""",
"""
<span class="hljs-keyword">try</span> { <span class="hljs-title function_">f</span>(); } <span class="hljs-keyword">catch</span> { <span class="hljs-title function_">g</span>(); }
""");
    }

    [Fact]
    public void ControlFlow_NestedIf()
    {
        AssertHighlighter("javascript",
"""
if (a) { if (b) { c(); } }
""",
"""
<span class="hljs-keyword">if</span> (a) { <span class="hljs-keyword">if</span> (b) { <span class="hljs-title function_">c</span>(); } }
""");
    }

    [Fact]
    public void ControlFlow_SwitchMulti()
    {
        AssertHighlighter("javascript",
"""
switch (x) { case 1: case 2: f(); break; default: g(); }
""",
"""
<span class="hljs-keyword">switch</span> (x) { <span class="hljs-keyword">case</span> <span class="hljs-number">1</span>: <span class="hljs-keyword">case</span> <span class="hljs-number">2</span>: <span class="hljs-title function_">f</span>(); <span class="hljs-keyword">break</span>; <span class="hljs-attr">default</span>: <span class="hljs-title function_">g</span>(); }
""");
    }

    [Fact]
    public void Async_PromiseNew()
    {
        AssertHighlighter("javascript",
"""
const p = new Promise((r) => r(1));
""",
"""
<span class="hljs-keyword">const</span> p = <span class="hljs-keyword">new</span> <span class="hljs-title class_">Promise</span>(<span class="hljs-function">(<span class="hljs-params">r</span>) =&gt;</span> <span class="hljs-title function_">r</span>(<span class="hljs-number">1</span>));
""");
    }

    [Fact]
    public void Async_PromiseThen()
    {
        AssertHighlighter("javascript",
"""
p.then((v) => v + 1);
""",
"""
p.<span class="hljs-title function_">then</span>(<span class="hljs-function">(<span class="hljs-params">v</span>) =&gt;</span> v + <span class="hljs-number">1</span>);
""");
    }

    [Fact]
    public void Async_PromiseChain()
    {
        AssertHighlighter("javascript",
"""
p.then((v) => v).catch((e) => e).finally(() => 0);
""",
"""
p.<span class="hljs-title function_">then</span>(<span class="hljs-function">(<span class="hljs-params">v</span>) =&gt;</span> v).<span class="hljs-title function_">catch</span>(<span class="hljs-function">(<span class="hljs-params">e</span>) =&gt;</span> e).<span class="hljs-title function_">finally</span>(<span class="hljs-function">() =&gt;</span> <span class="hljs-number">0</span>);
""");
    }

    [Fact]
    public void Async_PromiseResolve()
    {
        AssertHighlighter("javascript",
"""
Promise.resolve(1);
""",
"""
<span class="hljs-title class_">Promise</span>.<span class="hljs-title function_">resolve</span>(<span class="hljs-number">1</span>);
""");
    }

    [Fact]
    public void Async_PromiseReject()
    {
        AssertHighlighter("javascript",
"""
Promise.reject(new Error("x"));
""",
"""
<span class="hljs-title class_">Promise</span>.<span class="hljs-title function_">reject</span>(<span class="hljs-keyword">new</span> <span class="hljs-title class_">Error</span>(<span class="hljs-string">&quot;x&quot;</span>));
""");
    }

    [Fact]
    public void Async_PromiseAll()
    {
        AssertHighlighter("javascript",
"""
Promise.all([a, b]);
""",
"""
<span class="hljs-title class_">Promise</span>.<span class="hljs-title function_">all</span>([a, b]);
""");
    }

    [Fact]
    public void Async_PromiseAllSettled()
    {
        AssertHighlighter("javascript",
"""
Promise.allSettled([a, b]);
""",
"""
<span class="hljs-title class_">Promise</span>.<span class="hljs-title function_">allSettled</span>([a, b]);
""");
    }

    [Fact]
    public void Async_PromiseAny()
    {
        AssertHighlighter("javascript",
"""
Promise.any([a, b]);
""",
"""
<span class="hljs-title class_">Promise</span>.<span class="hljs-title function_">any</span>([a, b]);
""");
    }

    [Fact]
    public void Async_PromiseRace()
    {
        AssertHighlighter("javascript",
"""
Promise.race([a, b]);
""",
"""
<span class="hljs-title class_">Promise</span>.<span class="hljs-title function_">race</span>([a, b]);
""");
    }

    [Fact]
    public void Async_AwaitExpression()
    {
        AssertHighlighter("javascript",
"""
async function f() { const v = await p; }
""",
"""
<span class="hljs-keyword">async</span> <span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"></span>) { <span class="hljs-keyword">const</span> v = <span class="hljs-keyword">await</span> p; }
""");
    }

    [Fact]
    public void Async_ForAwaitOf()
    {
        AssertHighlighter("javascript",
"""
async function f() { for await (const v of stream) {} }
""",
"""
<span class="hljs-keyword">async</span> <span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"></span>) { <span class="hljs-keyword">for</span> <span class="hljs-title function_">await</span> (<span class="hljs-keyword">const</span> v <span class="hljs-keyword">of</span> stream) {} }
""");
    }

    [Fact]
    public void Async_TopLevelAwait()
    {
        AssertHighlighter("javascript",
"""
const v = await p;
""",
"""
<span class="hljs-keyword">const</span> v = <span class="hljs-keyword">await</span> p;
""");
    }

    [Fact]
    public void BuiltIn_MathPi()
    {
        AssertHighlighter("javascript",
"""
const v = Math.PI;
""",
"""
<span class="hljs-keyword">const</span> v = <span class="hljs-title class_">Math</span>.<span class="hljs-property">PI</span>;
""");
    }

    [Fact]
    public void BuiltIn_MathFloor()
    {
        AssertHighlighter("javascript",
"""
const v = Math.floor(1.5);
""",
"""
<span class="hljs-keyword">const</span> v = <span class="hljs-title class_">Math</span>.<span class="hljs-title function_">floor</span>(<span class="hljs-number">1.5</span>);
""");
    }

    [Fact]
    public void BuiltIn_MathMax()
    {
        AssertHighlighter("javascript",
"""
const v = Math.max(1, 2, 3);
""",
"""
<span class="hljs-keyword">const</span> v = <span class="hljs-title class_">Math</span>.<span class="hljs-title function_">max</span>(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>);
""");
    }

    [Fact]
    public void BuiltIn_JsonParse()
    {
        AssertHighlighter("javascript",
"""
const v = JSON.parse(s);
""",
"""
<span class="hljs-keyword">const</span> v = <span class="hljs-title class_">JSON</span>.<span class="hljs-title function_">parse</span>(s);
""");
    }

    [Fact]
    public void BuiltIn_JsonStringify()
    {
        AssertHighlighter("javascript",
"""
const s = JSON.stringify(v);
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-title class_">JSON</span>.<span class="hljs-title function_">stringify</span>(v);
""");
    }

    [Fact]
    public void BuiltIn_ConsoleLog()
    {
        AssertHighlighter("javascript",
"""
console.log("hi");
""",
"""
<span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(<span class="hljs-string">&quot;hi&quot;</span>);
""");
    }

    [Fact]
    public void BuiltIn_ConsoleError()
    {
        AssertHighlighter("javascript",
"""
console.error("oops");
""",
"""
<span class="hljs-variable language_">console</span>.<span class="hljs-title function_">error</span>(<span class="hljs-string">&quot;oops&quot;</span>);
""");
    }

    [Fact]
    public void BuiltIn_ArrayFrom()
    {
        AssertHighlighter("javascript",
"""
const a = Array.from(iter);
""",
"""
<span class="hljs-keyword">const</span> a = <span class="hljs-title class_">Array</span>.<span class="hljs-title function_">from</span>(iter);
""");
    }

    [Fact]
    public void BuiltIn_ArrayIsArray()
    {
        AssertHighlighter("javascript",
"""
const b = Array.isArray(x);
""",
"""
<span class="hljs-keyword">const</span> b = <span class="hljs-title class_">Array</span>.<span class="hljs-title function_">isArray</span>(x);
""");
    }

    [Fact]
    public void BuiltIn_ArrayOf()
    {
        AssertHighlighter("javascript",
"""
const a = Array.of(1, 2);
""",
"""
<span class="hljs-keyword">const</span> a = <span class="hljs-title class_">Array</span>.<span class="hljs-title function_">of</span>(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>);
""");
    }

    [Fact]
    public void BuiltIn_ObjectKeys()
    {
        AssertHighlighter("javascript",
"""
const ks = Object.keys(obj);
""",
"""
<span class="hljs-keyword">const</span> ks = <span class="hljs-title class_">Object</span>.<span class="hljs-title function_">keys</span>(obj);
""");
    }

    [Fact]
    public void BuiltIn_ObjectValues()
    {
        AssertHighlighter("javascript",
"""
const vs = Object.values(obj);
""",
"""
<span class="hljs-keyword">const</span> vs = <span class="hljs-title class_">Object</span>.<span class="hljs-title function_">values</span>(obj);
""");
    }

    [Fact]
    public void BuiltIn_ObjectEntries()
    {
        AssertHighlighter("javascript",
"""
const es = Object.entries(obj);
""",
"""
<span class="hljs-keyword">const</span> es = <span class="hljs-title class_">Object</span>.<span class="hljs-title function_">entries</span>(obj);
""");
    }

    [Fact]
    public void BuiltIn_ObjectAssign()
    {
        AssertHighlighter("javascript",
"""
const c = Object.assign({}, a, b);
""",
"""
<span class="hljs-keyword">const</span> c = <span class="hljs-title class_">Object</span>.<span class="hljs-title function_">assign</span>({}, a, b);
""");
    }

    [Fact]
    public void BuiltIn_ObjectFreeze()
    {
        AssertHighlighter("javascript",
"""
Object.freeze(obj);
""",
"""
<span class="hljs-title class_">Object</span>.<span class="hljs-title function_">freeze</span>(obj);
""");
    }

    [Fact]
    public void BuiltIn_MapNew()
    {
        AssertHighlighter("javascript",
"""
const m = new Map();
""",
"""
<span class="hljs-keyword">const</span> m = <span class="hljs-keyword">new</span> <span class="hljs-title class_">Map</span>();
""");
    }

    [Fact]
    public void BuiltIn_SetNew()
    {
        AssertHighlighter("javascript",
"""
const s = new Set();
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-keyword">new</span> <span class="hljs-title class_">Set</span>();
""");
    }

    [Fact]
    public void BuiltIn_WeakMapNew()
    {
        AssertHighlighter("javascript",
"""
const m = new WeakMap();
""",
"""
<span class="hljs-keyword">const</span> m = <span class="hljs-keyword">new</span> <span class="hljs-title class_">WeakMap</span>();
""");
    }

    [Fact]
    public void BuiltIn_WeakSetNew()
    {
        AssertHighlighter("javascript",
"""
const s = new WeakSet();
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-keyword">new</span> <span class="hljs-title class_">WeakSet</span>();
""");
    }

    [Fact]
    public void BuiltIn_DateNow()
    {
        AssertHighlighter("javascript",
"""
const t = Date.now();
""",
"""
<span class="hljs-keyword">const</span> t = <span class="hljs-title class_">Date</span>.<span class="hljs-title function_">now</span>();
""");
    }

    [Fact]
    public void BuiltIn_DateNew()
    {
        AssertHighlighter("javascript",
"""
const d = new Date();
""",
"""
<span class="hljs-keyword">const</span> d = <span class="hljs-keyword">new</span> <span class="hljs-title class_">Date</span>();
""");
    }

    [Fact]
    public void BuiltIn_GlobalThis()
    {
        AssertHighlighter("javascript",
"""
const g = globalThis;
""",
"""
<span class="hljs-keyword">const</span> g = globalThis;
""");
    }

    [Fact]
    public void BuiltIn_StringRaw()
    {
        AssertHighlighter("javascript",
"""
const s = String.raw`a\nb`;
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-title class_">String</span>.<span class="hljs-property">raw</span><span class="hljs-string">`a\nb`</span>;
""");
    }

    [Fact]
    public void BuiltIn_NumberParse()
    {
        AssertHighlighter("javascript",
"""
const n = Number.parseInt("42", 10);
""",
"""
<span class="hljs-keyword">const</span> n = <span class="hljs-title class_">Number</span>.<span class="hljs-built_in">parseInt</span>(<span class="hljs-string">&quot;42&quot;</span>, <span class="hljs-number">10</span>);
""");
    }

    [Fact]
    public void Error_GenericError()
    {
        AssertHighlighter("javascript",
"""
throw new Error("x");
""",
"""
<span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">Error</span>(<span class="hljs-string">&quot;x&quot;</span>);
""");
    }

    [Fact]
    public void Error_TypeError()
    {
        AssertHighlighter("javascript",
"""
throw new TypeError("bad");
""",
"""
<span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">TypeError</span>(<span class="hljs-string">&quot;bad&quot;</span>);
""");
    }

    [Fact]
    public void Error_ReferenceError()
    {
        AssertHighlighter("javascript",
"""
throw new ReferenceError("bad");
""",
"""
<span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">ReferenceError</span>(<span class="hljs-string">&quot;bad&quot;</span>);
""");
    }

    [Fact]
    public void Error_SyntaxError()
    {
        AssertHighlighter("javascript",
"""
throw new SyntaxError("bad");
""",
"""
<span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">SyntaxError</span>(<span class="hljs-string">&quot;bad&quot;</span>);
""");
    }

    [Fact]
    public void Error_RangeError()
    {
        AssertHighlighter("javascript",
"""
throw new RangeError("bad");
""",
"""
<span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">RangeError</span>(<span class="hljs-string">&quot;bad&quot;</span>);
""");
    }

    [Fact]
    public void Error_CustomError()
    {
        AssertHighlighter("javascript",
"""
class MyError extends Error {}
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">MyError</span> <span class="hljs-keyword">extends</span> <span class="hljs-title class_ inherited__">Error</span> {}
""");
    }

    [Fact]
    public void Symbol_Create()
    {
        AssertHighlighter("javascript",
"""
const s = Symbol("x");
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-title class_">Symbol</span>(<span class="hljs-string">&quot;x&quot;</span>);
""");
    }

    [Fact]
    public void Symbol_Iterator()
    {
        AssertHighlighter("javascript",
"""
const it = obj[Symbol.iterator]();
""",
"""
<span class="hljs-keyword">const</span> it = obj[<span class="hljs-title class_">Symbol</span>.<span class="hljs-property">iterator</span>]();
""");
    }

    [Fact]
    public void Symbol_AsyncIterator()
    {
        AssertHighlighter("javascript",
"""
const it = obj[Symbol.asyncIterator]();
""",
"""
<span class="hljs-keyword">const</span> it = obj[<span class="hljs-title class_">Symbol</span>.<span class="hljs-property">asyncIterator</span>]();
""");
    }

    [Fact]
    public void Symbol_IteratorMethod()
    {
        AssertHighlighter("javascript",
"""
class A { [Symbol.iterator]() { return this; } }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { [<span class="hljs-title class_">Symbol</span>.<span class="hljs-property">iterator</span>]() { <span class="hljs-keyword">return</span> <span class="hljs-variable language_">this</span>; } }
""");
    }

    [Fact]
    public void Symbol_For()
    {
        AssertHighlighter("javascript",
"""
const s = Symbol.for("key");
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-title class_">Symbol</span>.<span class="hljs-title function_">for</span>(<span class="hljs-string">&quot;key&quot;</span>);
""");
    }

    [Fact]
    public void Reflect_ProxyNew()
    {
        AssertHighlighter("javascript",
"""
const p = new Proxy(target, handler);
""",
"""
<span class="hljs-keyword">const</span> p = <span class="hljs-keyword">new</span> <span class="hljs-title class_">Proxy</span>(target, handler);
""");
    }

    [Fact]
    public void Reflect_ReflectGet()
    {
        AssertHighlighter("javascript",
"""
const v = Reflect.get(obj, "x");
""",
"""
<span class="hljs-keyword">const</span> v = <span class="hljs-title class_">Reflect</span>.<span class="hljs-title function_">get</span>(obj, <span class="hljs-string">&quot;x&quot;</span>);
""");
    }

    [Fact]
    public void Reflect_ReflectSet()
    {
        AssertHighlighter("javascript",
"""
Reflect.set(obj, "x", 1);
""",
"""
<span class="hljs-title class_">Reflect</span>.<span class="hljs-title function_">set</span>(obj, <span class="hljs-string">&quot;x&quot;</span>, <span class="hljs-number">1</span>);
""");
    }

    [Fact]
    public void Reflect_ReflectHas()
    {
        AssertHighlighter("javascript",
"""
Reflect.has(obj, "x");
""",
"""
<span class="hljs-title class_">Reflect</span>.<span class="hljs-title function_">has</span>(obj, <span class="hljs-string">&quot;x&quot;</span>);
""");
    }

    [Fact]
    public void Object_Empty()
    {
        AssertHighlighter("javascript",
"""
const o = {};
""",
"""
<span class="hljs-keyword">const</span> o = {};
""");
    }

    [Fact]
    public void Object_Properties()
    {
        AssertHighlighter("javascript",
"""
const o = { a: 1, b: 2 };
""",
"""
<span class="hljs-keyword">const</span> o = { <span class="hljs-attr">a</span>: <span class="hljs-number">1</span>, <span class="hljs-attr">b</span>: <span class="hljs-number">2</span> };
""");
    }

    [Fact]
    public void Object_Shorthand()
    {
        AssertHighlighter("javascript",
"""
const o = { a, b };
""",
"""
<span class="hljs-keyword">const</span> o = { a, b };
""");
    }

    [Fact]
    public void Object_Computed()
    {
        AssertHighlighter("javascript",
"""
const o = { [key]: 1 };
""",
"""
<span class="hljs-keyword">const</span> o = { [key]: <span class="hljs-number">1</span> };
""");
    }

    [Fact]
    public void Object_MethodShorthand()
    {
        AssertHighlighter("javascript",
"""
const o = { foo() { return 1; } };
""",
"""
<span class="hljs-keyword">const</span> o = { <span class="hljs-title function_">foo</span>(<span class="hljs-params"></span>) { <span class="hljs-keyword">return</span> <span class="hljs-number">1</span>; } };
""");
    }

    [Fact]
    public void Object_Getter()
    {
        AssertHighlighter("javascript",
"""
const o = { get x() { return 1; } };
""",
"""
<span class="hljs-keyword">const</span> o = { <span class="hljs-keyword">get</span> <span class="hljs-title function_">x</span>() { <span class="hljs-keyword">return</span> <span class="hljs-number">1</span>; } };
""");
    }

    [Fact]
    public void Object_Setter()
    {
        AssertHighlighter("javascript",
"""
const o = { set x(v) {} };
""",
"""
<span class="hljs-keyword">const</span> o = { <span class="hljs-keyword">set</span> <span class="hljs-title function_">x</span>(<span class="hljs-params">v</span>) {} };
""");
    }

    [Fact]
    public void Object_SpreadInside()
    {
        AssertHighlighter("javascript",
"""
const o = { ...a, b: 1 };
""",
"""
<span class="hljs-keyword">const</span> o = { ...a, <span class="hljs-attr">b</span>: <span class="hljs-number">1</span> };
""");
    }

    [Fact]
    public void Array_Empty()
    {
        AssertHighlighter("javascript",
"""
const a = [];
""",
"""
<span class="hljs-keyword">const</span> a = [];
""");
    }

    [Fact]
    public void Array_Numbers()
    {
        AssertHighlighter("javascript",
"""
const a = [1, 2, 3];
""",
"""
<span class="hljs-keyword">const</span> a = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>];
""");
    }

    [Fact]
    public void Array_Mixed()
    {
        AssertHighlighter("javascript",
"""
const a = [1, "two", true, null];
""",
"""
<span class="hljs-keyword">const</span> a = [<span class="hljs-number">1</span>, <span class="hljs-string">&quot;two&quot;</span>, <span class="hljs-literal">true</span>, <span class="hljs-literal">null</span>];
""");
    }

    [Fact]
    public void Array_Nested()
    {
        AssertHighlighter("javascript",
"""
const a = [[1, 2], [3, 4]];
""",
"""
<span class="hljs-keyword">const</span> a = [[<span class="hljs-number">1</span>, <span class="hljs-number">2</span>], [<span class="hljs-number">3</span>, <span class="hljs-number">4</span>]];
""");
    }

    [Fact]
    public void Array_Spread()
    {
        AssertHighlighter("javascript",
"""
const a = [...b, 1, 2];
""",
"""
<span class="hljs-keyword">const</span> a = [...b, <span class="hljs-number">1</span>, <span class="hljs-number">2</span>];
""");
    }

    [Fact]
    public void Array_Holes()
    {
        AssertHighlighter("javascript",
"""
const a = [1, , 3];
""",
"""
<span class="hljs-keyword">const</span> a = [<span class="hljs-number">1</span>, , <span class="hljs-number">3</span>];
""");
    }

    [Fact]
    public void SpecialEdge_Shebang()
    {
        AssertHighlighter("javascript",
"""
#!/usr/bin/env node
console.log("hi");
""",
"""
<span class="hljs-meta">#!/usr/bin/env node</span>
<span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(<span class="hljs-string">&quot;hi&quot;</span>);
""");
    }

    [Fact]
    public void SpecialEdge_TrailingComma()
    {
        AssertHighlighter("javascript",
"""
const a = [1, 2,];
""",
"""
<span class="hljs-keyword">const</span> a = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>,];
""");
    }

    [Fact]
    public void SpecialEdge_NoSemicolons()
    {
        AssertHighlighter("javascript",
"""
const a = 1
const b = 2
""",
"""
<span class="hljs-keyword">const</span> a = <span class="hljs-number">1</span>
<span class="hljs-keyword">const</span> b = <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void SpecialEdge_UseStrict()
    {
        AssertHighlighter("javascript",
"""
"use strict";
const a = 1;
""",
"""
<span class="hljs-meta">&quot;use strict&quot;</span>;
<span class="hljs-keyword">const</span> a = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void SpecialEdge_EmptyFile()
    {
        AssertHighlighter("javascript",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyWhitespace()
    {
        AssertHighlighter("javascript",
"""


""",
"""


""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("javascript",
"""
// just a comment
""",
"""
<span class="hljs-comment">// just a comment</span>
""");
    }
}
