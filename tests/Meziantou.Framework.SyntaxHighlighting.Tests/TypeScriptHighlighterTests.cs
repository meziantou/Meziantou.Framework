namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class TypeScriptHighlighterTests
{

    [Fact]
    public void Keyword_Var()
    {
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
"""
if (x) {} else {}
""",
"""
<span class="hljs-keyword">if</span> (x) {} <span class="hljs-keyword">else</span> {}
""");
    }

    [Fact]
    public void Keyword_Switch()
    {
        AssertHighlighter("typescript",
"""
switch (x) { case 1: break; }
""",
"""
<span class="hljs-keyword">switch</span> (x) { <span class="hljs-keyword">case</span> <span class="hljs-number">1</span>: <span class="hljs-keyword">break</span>; }
""");
    }

    [Fact]
    public void Keyword_For()
    {
        AssertHighlighter("typescript",
"""
for (let i = 0; i < 10; i++) {}
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-keyword">let</span> i = <span class="hljs-number">0</span>; i &lt; <span class="hljs-number">10</span>; i++) {}
""");
    }

    [Fact]
    public void Keyword_ForOf()
    {
        AssertHighlighter("typescript",
"""
for (const v of arr) {}
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-keyword">const</span> v <span class="hljs-keyword">of</span> arr) {}
""");
    }

    [Fact]
    public void Keyword_ForIn()
    {
        AssertHighlighter("typescript",
"""
for (const k in obj) {}
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-keyword">const</span> k <span class="hljs-keyword">in</span> obj) {}
""");
    }

    [Fact]
    public void Keyword_While()
    {
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
"""
x instanceof Foo;
""",
"""
x <span class="hljs-keyword">instanceof</span> <span class="hljs-title class_">Foo</span>;
""");
    }

    [Fact]
    public void Keyword_Void()
    {
        AssertHighlighter("typescript",
"""
void 0;
""",
"""
<span class="hljs-built_in">void</span> <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Keyword_Yield()
    {
        AssertHighlighter("typescript",
"""
function* g() { yield 1; }
""",
"""
<span class="hljs-keyword">function</span>* <span class="hljs-title function_">g</span>(<span class="hljs-params"></span>) { <span class="hljs-keyword">yield</span> <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Keyword_Await()
    {
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
"""
class A extends B {}
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> <span class="hljs-keyword">extends</span> <span class="hljs-title class_ inherited__">B</span> {}
""");
    }

    [Fact]
    public void Keyword_Implements()
    {
        AssertHighlighter("typescript",
"""
class A implements B {}
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> <span class="hljs-keyword">implements</span> B {}
""");
    }

    [Fact]
    public void Keyword_Static()
    {
        AssertHighlighter("typescript",
"""
class A { static x = 1; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">static</span> x = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Keyword_Abstract()
    {
        AssertHighlighter("typescript",
"""
abstract class A {}
""",
"""
<span class="hljs-keyword">abstract</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> {}
""");
    }

    [Fact]
    public void Keyword_Readonly()
    {
        AssertHighlighter("typescript",
"""
class A { readonly x = 1; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">readonly</span> x = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Keyword_Override()
    {
        AssertHighlighter("typescript",
"""
class B extends A { override foo() {} }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">B</span> <span class="hljs-keyword">extends</span> <span class="hljs-title class_ inherited__">A</span> { <span class="hljs-keyword">override</span> <span class="hljs-title function_">foo</span>(<span class="hljs-params"></span>) {} }
""");
    }

    [Fact]
    public void Keyword_Declare()
    {
        AssertHighlighter("typescript",
"""
declare const x: number;
""",
"""
<span class="hljs-keyword">declare</span> <span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>;
""");
    }

    [Fact]
    public void Keyword_Namespace()
    {
        AssertHighlighter("typescript",
"""
namespace Foo {}
""",
"""
<span class="hljs-keyword">namespace</span> <span class="hljs-title class_">Foo</span> {}
""");
    }

    [Fact]
    public void Keyword_Module()
    {
        AssertHighlighter("typescript",
"""
module Foo {}
""",
"""
<span class="hljs-variable language_">module</span> <span class="hljs-title class_">Foo</span> {}
""");
    }

    [Fact]
    public void Keyword_Interface()
    {
        AssertHighlighter("typescript",
"""
interface Foo {}
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Foo</span> {}
""");
    }

    [Fact]
    public void Keyword_Type()
    {
        AssertHighlighter("typescript",
"""
type Foo = string;
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Foo</span> = <span class="hljs-built_in">string</span>;
""");
    }

    [Fact]
    public void Keyword_Enum()
    {
        AssertHighlighter("typescript",
"""
enum Color { Red }
""",
"""
<span class="hljs-keyword">enum</span> <span class="hljs-title class_">Color</span> { <span class="hljs-title class_">Red</span> }
""");
    }

    [Fact]
    public void Keyword_ConstEnum()
    {
        AssertHighlighter("typescript",
"""
const enum Color { Red }
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-keyword">enum</span> <span class="hljs-title class_">Color</span> { <span class="hljs-title class_">Red</span> }
""");
    }

    [Fact]
    public void Keyword_Keyof()
    {
        AssertHighlighter("typescript",
"""
type K = keyof T;
""",
"""
<span class="hljs-keyword">type</span> K = keyof T;
""");
    }

    [Fact]
    public void Keyword_Infer()
    {
        AssertHighlighter("typescript",
"""
type U<X> = X extends Array<infer I> ? I : never;
""",
"""
<span class="hljs-keyword">type</span> U&lt;X&gt; = X <span class="hljs-keyword">extends</span> <span class="hljs-title class_">Array</span>&lt;infer I&gt; ? I : <span class="hljs-built_in">never</span>;
""");
    }

    [Fact]
    public void Keyword_In()
    {
        AssertHighlighter("typescript",
"""
type T<X> = { [K in keyof X]: X[K] };
""",
"""
<span class="hljs-keyword">type</span> T&lt;X&gt; = { [K <span class="hljs-keyword">in</span> keyof X]: X[K] };
""");
    }

    [Fact]
    public void Keyword_AsCast()
    {
        AssertHighlighter("typescript",
"""
const x = y as number;
""",
"""
<span class="hljs-keyword">const</span> x = y <span class="hljs-keyword">as</span> <span class="hljs-built_in">number</span>;
""");
    }

    [Fact]
    public void Keyword_AsConst()
    {
        AssertHighlighter("typescript",
"""
const x = [1, 2] as const;
""",
"""
<span class="hljs-keyword">const</span> x = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>] <span class="hljs-keyword">as</span> <span class="hljs-keyword">const</span>;
""");
    }

    [Fact]
    public void Keyword_Satisfies()
    {
        AssertHighlighter("typescript",
"""
const x = {} satisfies Foo;
""",
"""
<span class="hljs-keyword">const</span> x = {} <span class="hljs-keyword">satisfies</span> <span class="hljs-title class_">Foo</span>;
""");
    }

    [Fact]
    public void Keyword_Is()
    {
        AssertHighlighter("typescript",
"""
function isString(x: unknown): x is string { return typeof x === "string"; }
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">isString</span>(<span class="hljs-params"><span class="hljs-attr">x</span>: <span class="hljs-built_in">unknown</span></span>): x is <span class="hljs-built_in">string</span> { <span class="hljs-keyword">return</span> <span class="hljs-keyword">typeof</span> x === <span class="hljs-string">&quot;string&quot;</span>; }
""");
    }

    [Fact]
    public void Keyword_Public()
    {
        AssertHighlighter("typescript",
"""
class A { public x = 1; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">public</span> x = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Keyword_Private()
    {
        AssertHighlighter("typescript",
"""
class A { private x = 1; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">private</span> x = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Keyword_Protected()
    {
        AssertHighlighter("typescript",
"""
class A { protected x = 1; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">protected</span> x = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Literal_True()
    {
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
"""
const n: number = 42;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">n</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">42</span>;
""");
    }

    [Fact]
    public void Number_Float()
    {
        AssertHighlighter("typescript",
"""
const n: number = 3.14;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">n</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">3.14</span>;
""");
    }

    [Fact]
    public void Number_Hex()
    {
        AssertHighlighter("typescript",
"""
const n: number = 0xFF;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">n</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">0xFF</span>;
""");
    }

    [Fact]
    public void Number_Octal()
    {
        AssertHighlighter("typescript",
"""
const n: number = 0o17;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">n</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">0o17</span>;
""");
    }

    [Fact]
    public void Number_Binary()
    {
        AssertHighlighter("typescript",
"""
const n: number = 0b1010;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">n</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">0b1010</span>;
""");
    }

    [Fact]
    public void Number_BigInt()
    {
        AssertHighlighter("typescript",
"""
const n: bigint = 123n;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">n</span>: <span class="hljs-built_in">bigint</span> = <span class="hljs-number">123n</span>;
""");
    }

    [Fact]
    public void Number_Separator()
    {
        AssertHighlighter("typescript",
"""
const n: number = 1_000_000;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">n</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">1_000_000</span>;
""");
    }

    [Fact]
    public void Number_ExponentPositive()
    {
        AssertHighlighter("typescript",
"""
const n: number = 1e10;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">n</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">1e10</span>;
""");
    }

    [Fact]
    public void Number_ExponentNegative()
    {
        AssertHighlighter("typescript",
"""
const n: number = 1.5e-3;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">n</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">1.5e-3</span>;
""");
    }

    [Fact]
    public void String_SingleQuote()
    {
        AssertHighlighter("typescript",
"""
const s: string = 'hello';
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">s</span>: <span class="hljs-built_in">string</span> = <span class="hljs-string">&#x27;hello&#x27;</span>;
""");
    }

    [Fact]
    public void String_DoubleQuote()
    {
        AssertHighlighter("typescript",
"""
const s: string = "hello";
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">s</span>: <span class="hljs-built_in">string</span> = <span class="hljs-string">&quot;hello&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeNewline()
    {
        AssertHighlighter("typescript",
"""
const s: string = "a\nb";
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">s</span>: <span class="hljs-built_in">string</span> = <span class="hljs-string">&quot;a\nb&quot;</span>;
""");
    }

    [Fact]
    public void String_UnicodeEscape()
    {
        AssertHighlighter("typescript",
"""
const s: string = "\u0041";
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">s</span>: <span class="hljs-built_in">string</span> = <span class="hljs-string">&quot;\u0041&quot;</span>;
""");
    }

    [Fact]
    public void Template_Plain()
    {
        AssertHighlighter("typescript",
"""
const s: string = `hello`;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">s</span>: <span class="hljs-built_in">string</span> = <span class="hljs-string">`hello`</span>;
""");
    }

    [Fact]
    public void Template_Interpolation()
    {
        AssertHighlighter("typescript",
"""
const s: string = `hello ${name}`;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">s</span>: <span class="hljs-built_in">string</span> = <span class="hljs-string">`hello <span class="hljs-subst">${name}</span>`</span>;
""");
    }

    [Fact]
    public void Template_Tagged()
    {
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
"""
const s: string = `line1
line2`;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">s</span>: <span class="hljs-built_in">string</span> = <span class="hljs-string">`line1
line2`</span>;
""");
    }

    [Fact]
    public void Regex_Simple()
    {
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
"""
const re = /abc/gi;
""",
"""
<span class="hljs-keyword">const</span> re = <span class="hljs-regexp">/abc/gi</span>;
""");
    }

    [Fact]
    public void Regex_NamedGroup()
    {
        AssertHighlighter("typescript",
"""
const re = /(?<year>\d{4})/;
""",
"""
<span class="hljs-keyword">const</span> re = <span class="hljs-regexp">/(?&lt;year&gt;\d{4})/</span>;
""");
    }

    [Fact]
    public void Regex_Lookbehind()
    {
        AssertHighlighter("typescript",
"""
const re = /(?<=a)b/;
""",
"""
<span class="hljs-keyword">const</span> re = <span class="hljs-regexp">/(?&lt;=a)b/</span>;
""");
    }

    [Fact]
    public void Comment_Line()
    {
        AssertHighlighter("typescript",
"""
// hello
""",
"""
<span class="hljs-comment">// hello</span>
""");
    }

    [Fact]
    public void Comment_Block()
    {
        AssertHighlighter("typescript",
"""
/* hello */
""",
"""
<span class="hljs-comment">/* hello */</span>
""");
    }

    [Fact]
    public void Comment_JSDoc()
    {
        AssertHighlighter("typescript",
"""
/** @param x */
function f(x: number) {}
""",
"""
<span class="hljs-comment">/** <span class="hljs-doctag">@param</span> x */</span>
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"><span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span></span>) {}
""");
    }

    [Fact]
    public void Comment_TripleSlashDirective()
    {
        AssertHighlighter("typescript",
"""
/// <reference path="./types.d.ts" />
""",
"""
<span class="hljs-comment">/// &lt;reference path=&quot;./types.d.ts&quot; /&gt;</span>
""");
    }

    [Fact]
    public void PrimitiveType_Number()
    {
        AssertHighlighter("typescript",
"""
const x: number = 1;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void PrimitiveType_String()
    {
        AssertHighlighter("typescript",
"""
const x: string = "a";
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">string</span> = <span class="hljs-string">&quot;a&quot;</span>;
""");
    }

    [Fact]
    public void PrimitiveType_Boolean()
    {
        AssertHighlighter("typescript",
"""
const x: boolean = true;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">boolean</span> = <span class="hljs-literal">true</span>;
""");
    }

    [Fact]
    public void PrimitiveType_Void()
    {
        AssertHighlighter("typescript",
"""
function f(): void {}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"></span>): <span class="hljs-built_in">void</span> {}
""");
    }

    [Fact]
    public void PrimitiveType_Undefined()
    {
        AssertHighlighter("typescript",
"""
const x: undefined = undefined;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-literal">undefined</span> = <span class="hljs-literal">undefined</span>;
""");
    }

    [Fact]
    public void PrimitiveType_Null()
    {
        AssertHighlighter("typescript",
"""
const x: null = null;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-literal">null</span> = <span class="hljs-literal">null</span>;
""");
    }

    [Fact]
    public void PrimitiveType_Any()
    {
        AssertHighlighter("typescript",
"""
const x: any = 1;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">any</span> = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void PrimitiveType_Unknown()
    {
        AssertHighlighter("typescript",
"""
const x: unknown = 1;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">unknown</span> = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void PrimitiveType_Never()
    {
        AssertHighlighter("typescript",
"""
function f(): never { throw new Error(); }
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"></span>): <span class="hljs-built_in">never</span> { <span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">Error</span>(); }
""");
    }

    [Fact]
    public void PrimitiveType_Object()
    {
        AssertHighlighter("typescript",
"""
const x: object = {};
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">object</span> = {};
""");
    }

    [Fact]
    public void PrimitiveType_Symbol()
    {
        AssertHighlighter("typescript",
"""
const x: symbol = Symbol();
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">symbol</span> = <span class="hljs-title class_">Symbol</span>();
""");
    }

    [Fact]
    public void PrimitiveType_BigInt()
    {
        AssertHighlighter("typescript",
"""
const x: bigint = 1n;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">bigint</span> = <span class="hljs-number">1n</span>;
""");
    }

    [Fact]
    public void TypeAnnotation_Variable()
    {
        AssertHighlighter("typescript",
"""
const x: number = 1;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void TypeAnnotation_FunctionParam()
    {
        AssertHighlighter("typescript",
"""
function f(x: number) {}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"><span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span></span>) {}
""");
    }

    [Fact]
    public void TypeAnnotation_FunctionReturn()
    {
        AssertHighlighter("typescript",
"""
function f(): number { return 1; }
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"></span>): <span class="hljs-built_in">number</span> { <span class="hljs-keyword">return</span> <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void TypeAnnotation_ArrayShorthand()
    {
        AssertHighlighter("typescript",
"""
const a: number[] = [];
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">a</span>: <span class="hljs-built_in">number</span>[] = [];
""");
    }

    [Fact]
    public void TypeAnnotation_ArrayGeneric()
    {
        AssertHighlighter("typescript",
"""
const a: Array<number> = [];
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">a</span>: <span class="hljs-title class_">Array</span>&lt;<span class="hljs-built_in">number</span>&gt; = [];
""");
    }

    [Fact]
    public void TypeAnnotation_ObjectLiteral()
    {
        AssertHighlighter("typescript",
"""
const o: { x: number; y: string } = { x: 1, y: "a" };
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">o</span>: { <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>; <span class="hljs-attr">y</span>: <span class="hljs-built_in">string</span> } = { <span class="hljs-attr">x</span>: <span class="hljs-number">1</span>, <span class="hljs-attr">y</span>: <span class="hljs-string">&quot;a&quot;</span> };
""");
    }

    [Fact]
    public void TypeAnnotation_FunctionType()
    {
        AssertHighlighter("typescript",
"""
const f: (x: number) => number = (x) => x;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">f</span>: <span class="hljs-function">(<span class="hljs-params"><span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span></span>) =&gt;</span> <span class="hljs-built_in">number</span> = <span class="hljs-function">(<span class="hljs-params">x</span>) =&gt;</span> x;
""");
    }

    [Fact]
    public void TypeAnnotation_OptionalProp()
    {
        AssertHighlighter("typescript",
"""
const o: { x?: number } = {};
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">o</span>: { <span class="hljs-attr">x</span>?: <span class="hljs-built_in">number</span> } = {};
""");
    }

    [Fact]
    public void TypeAnnotation_ReadonlyProp()
    {
        AssertHighlighter("typescript",
"""
const o: { readonly x: number } = { x: 1 };
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">o</span>: { <span class="hljs-keyword">readonly</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span> } = { <span class="hljs-attr">x</span>: <span class="hljs-number">1</span> };
""");
    }

    [Fact]
    public void TypeAnnotation_Nullable()
    {
        AssertHighlighter("typescript",
"""
const x: number | null = null;
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span> | <span class="hljs-literal">null</span> = <span class="hljs-literal">null</span>;
""");
    }

    [Fact]
    public void TypeAnnotation_Optional()
    {
        AssertHighlighter("typescript",
"""
function f(x?: number) {}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"><span class="hljs-attr">x</span>?: <span class="hljs-built_in">number</span></span>) {}
""");
    }

    [Fact]
    public void TypeAnnotation_Default()
    {
        AssertHighlighter("typescript",
"""
function f(x: number = 1) {}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"><span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">1</span></span>) {}
""");
    }

    [Fact]
    public void TypeAnnotation_RestTyped()
    {
        AssertHighlighter("typescript",
"""
function f(...args: number[]) {}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params">...<span class="hljs-attr">args</span>: <span class="hljs-built_in">number</span>[]</span>) {}
""");
    }

    [Fact]
    public void UnionIntersection_UnionPrimitive()
    {
        AssertHighlighter("typescript",
"""
type T = string | number;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-built_in">string</span> | <span class="hljs-built_in">number</span>;
""");
    }

    [Fact]
    public void UnionIntersection_UnionLiteral()
    {
        AssertHighlighter("typescript",
"""
type T = "a" | "b" | "c";
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-string">&quot;a&quot;</span> | <span class="hljs-string">&quot;b&quot;</span> | <span class="hljs-string">&quot;c&quot;</span>;
""");
    }

    [Fact]
    public void UnionIntersection_UnionMixed()
    {
        AssertHighlighter("typescript",
"""
type T = string | number | null;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-built_in">string</span> | <span class="hljs-built_in">number</span> | <span class="hljs-literal">null</span>;
""");
    }

    [Fact]
    public void UnionIntersection_Intersection()
    {
        AssertHighlighter("typescript",
"""
type T = A & B;
""",
"""
<span class="hljs-keyword">type</span> T = A &amp; B;
""");
    }

    [Fact]
    public void UnionIntersection_IntersectionThree()
    {
        AssertHighlighter("typescript",
"""
type T = A & B & C;
""",
"""
<span class="hljs-keyword">type</span> T = A &amp; B &amp; C;
""");
    }

    [Fact]
    public void UnionIntersection_Mixed()
    {
        AssertHighlighter("typescript",
"""
type T = (A & B) | C;
""",
"""
<span class="hljs-keyword">type</span> T = (A &amp; B) | C;
""");
    }

    [Fact]
    public void TypeAlias_Simple()
    {
        AssertHighlighter("typescript",
"""
type Foo = string;
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Foo</span> = <span class="hljs-built_in">string</span>;
""");
    }

    [Fact]
    public void TypeAlias_Object()
    {
        AssertHighlighter("typescript",
"""
type Point = { x: number; y: number };
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Point</span> = { <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>; <span class="hljs-attr">y</span>: <span class="hljs-built_in">number</span> };
""");
    }

    [Fact]
    public void TypeAlias_Generic()
    {
        AssertHighlighter("typescript",
"""
type Box<T> = { value: T };
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Box</span>&lt;T&gt; = { <span class="hljs-attr">value</span>: T };
""");
    }

    [Fact]
    public void TypeAlias_GenericConstrained()
    {
        AssertHighlighter("typescript",
"""
type Box<T extends string> = { value: T };
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Box</span>&lt;T <span class="hljs-keyword">extends</span> <span class="hljs-built_in">string</span>&gt; = { <span class="hljs-attr">value</span>: T };
""");
    }

    [Fact]
    public void TypeAlias_GenericDefault()
    {
        AssertHighlighter("typescript",
"""
type Box<T = number> = { value: T };
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Box</span>&lt;T = <span class="hljs-built_in">number</span>&gt; = { <span class="hljs-attr">value</span>: T };
""");
    }

    [Fact]
    public void TypeAlias_Conditional()
    {
        AssertHighlighter("typescript",
"""
type T<X> = X extends string ? true : false;
""",
"""
<span class="hljs-keyword">type</span> T&lt;X&gt; = X <span class="hljs-keyword">extends</span> <span class="hljs-built_in">string</span> ? <span class="hljs-literal">true</span> : <span class="hljs-literal">false</span>;
""");
    }

    [Fact]
    public void TypeAlias_Mapped()
    {
        AssertHighlighter("typescript",
"""
type T<X> = { [K in keyof X]: X[K] };
""",
"""
<span class="hljs-keyword">type</span> T&lt;X&gt; = { [K <span class="hljs-keyword">in</span> keyof X]: X[K] };
""");
    }

    [Fact]
    public void TypeAlias_MappedReadonly()
    {
        AssertHighlighter("typescript",
"""
type T<X> = { readonly [K in keyof X]: X[K] };
""",
"""
<span class="hljs-keyword">type</span> T&lt;X&gt; = { <span class="hljs-keyword">readonly</span> [K <span class="hljs-keyword">in</span> keyof X]: X[K] };
""");
    }

    [Fact]
    public void TypeAlias_MappedOptional()
    {
        AssertHighlighter("typescript",
"""
type T<X> = { [K in keyof X]?: X[K] };
""",
"""
<span class="hljs-keyword">type</span> T&lt;X&gt; = { [K <span class="hljs-keyword">in</span> keyof X]?: X[K] };
""");
    }

    [Fact]
    public void TypeAlias_MappedAs()
    {
        AssertHighlighter("typescript",
"""
type T<X> = { [K in keyof X as `get${Capitalize<string & K>}`]: () => X[K] };
""",
"""
<span class="hljs-keyword">type</span> T&lt;X&gt; = { [K <span class="hljs-keyword">in</span> keyof X <span class="hljs-keyword">as</span> <span class="hljs-string">`get<span class="hljs-subst">${Capitalize&lt;<span class="hljs-built_in">string</span> &amp; K&gt;}</span>`</span>]: <span class="hljs-function">() =&gt;</span> X[K] };
""");
    }

    [Fact]
    public void TypeAlias_TemplateLiteral()
    {
        AssertHighlighter("typescript",
"""
type Greet = `Hello ${string}`;
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Greet</span> = <span class="hljs-string">`Hello <span class="hljs-subst">${<span class="hljs-built_in">string</span>}</span>`</span>;
""");
    }

    [Fact]
    public void TypeAlias_IndexedAccess()
    {
        AssertHighlighter("typescript",
"""
type V = T["x"];
""",
"""
<span class="hljs-keyword">type</span> V = T[<span class="hljs-string">&quot;x&quot;</span>];
""");
    }

    [Fact]
    public void TypeAlias_Keyof()
    {
        AssertHighlighter("typescript",
"""
type Keys = keyof Foo;
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Keys</span> = keyof <span class="hljs-title class_">Foo</span>;
""");
    }

    [Fact]
    public void TypeAlias_TypeofValue()
    {
        AssertHighlighter("typescript",
"""
type T = typeof x;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-keyword">typeof</span> x;
""");
    }

    [Fact]
    public void Interface_Empty()
    {
        AssertHighlighter("typescript",
"""
interface Foo {}
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Foo</span> {}
""");
    }

    [Fact]
    public void Interface_Properties()
    {
        AssertHighlighter("typescript",
"""
interface Foo { x: number; y: string; }
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>; <span class="hljs-attr">y</span>: <span class="hljs-built_in">string</span>; }
""");
    }

    [Fact]
    public void Interface_Method()
    {
        AssertHighlighter("typescript",
"""
interface Foo { foo(): void; }
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-title function_">foo</span>(): <span class="hljs-built_in">void</span>; }
""");
    }

    [Fact]
    public void Interface_MethodArrow()
    {
        AssertHighlighter("typescript",
"""
interface Foo { foo: () => void; }
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-attr">foo</span>: <span class="hljs-function">() =&gt;</span> <span class="hljs-built_in">void</span>; }
""");
    }

    [Fact]
    public void Interface_Optional()
    {
        AssertHighlighter("typescript",
"""
interface Foo { x?: number; }
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-attr">x</span>?: <span class="hljs-built_in">number</span>; }
""");
    }

    [Fact]
    public void Interface_Readonly()
    {
        AssertHighlighter("typescript",
"""
interface Foo { readonly x: number; }
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-keyword">readonly</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>; }
""");
    }

    [Fact]
    public void Interface_Extends()
    {
        AssertHighlighter("typescript",
"""
interface Foo extends Bar {}
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Foo</span> <span class="hljs-keyword">extends</span> <span class="hljs-title class_">Bar</span> {}
""");
    }

    [Fact]
    public void Interface_ExtendsMulti()
    {
        AssertHighlighter("typescript",
"""
interface Foo extends Bar, Baz {}
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Foo</span> <span class="hljs-keyword">extends</span> <span class="hljs-title class_">Bar</span>, <span class="hljs-title class_">Baz</span> {}
""");
    }

    [Fact]
    public void Interface_Generic()
    {
        AssertHighlighter("typescript",
"""
interface Box<T> { value: T; }
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Box</span>&lt;T&gt; { <span class="hljs-attr">value</span>: T; }
""");
    }

    [Fact]
    public void Interface_GenericConstrained()
    {
        AssertHighlighter("typescript",
"""
interface Box<T extends Foo> { value: T; }
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Box</span>&lt;T <span class="hljs-keyword">extends</span> <span class="hljs-title class_">Foo</span>&gt; { <span class="hljs-attr">value</span>: T; }
""");
    }

    [Fact]
    public void Interface_IndexSignature()
    {
        AssertHighlighter("typescript",
"""
interface Foo { [key: string]: number; }
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Foo</span> { [<span class="hljs-attr">key</span>: <span class="hljs-built_in">string</span>]: <span class="hljs-built_in">number</span>; }
""");
    }

    [Fact]
    public void Interface_CallSignature()
    {
        AssertHighlighter("typescript",
"""
interface Foo { (x: number): number; }
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Foo</span> { (<span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>): <span class="hljs-built_in">number</span>; }
""");
    }

    [Fact]
    public void Interface_ConstructSignature()
    {
        AssertHighlighter("typescript",
"""
interface Foo { new (x: number): Foo; }
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-title function_">new</span> (<span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>): <span class="hljs-title class_">Foo</span>; }
""");
    }

    [Fact]
    public void Interface_Merge()
    {
        AssertHighlighter("typescript",
"""
interface Foo { x: number; }
interface Foo { y: string; }
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>; }
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-attr">y</span>: <span class="hljs-built_in">string</span>; }
""");
    }

    [Fact]
    public void Enum_Numeric()
    {
        AssertHighlighter("typescript",
"""
enum Color { Red, Green, Blue }
""",
"""
<span class="hljs-keyword">enum</span> <span class="hljs-title class_">Color</span> { <span class="hljs-title class_">Red</span>, <span class="hljs-title class_">Green</span>, <span class="hljs-title class_">Blue</span> }
""");
    }

    [Fact]
    public void Enum_NumericExplicit()
    {
        AssertHighlighter("typescript",
"""
enum Color { Red = 1, Green = 2, Blue = 4 }
""",
"""
<span class="hljs-keyword">enum</span> <span class="hljs-title class_">Color</span> { <span class="hljs-title class_">Red</span> = <span class="hljs-number">1</span>, <span class="hljs-title class_">Green</span> = <span class="hljs-number">2</span>, <span class="hljs-title class_">Blue</span> = <span class="hljs-number">4</span> }
""");
    }

    [Fact]
    public void Enum_String()
    {
        AssertHighlighter("typescript",
"""
enum Color { Red = "red", Green = "green" }
""",
"""
<span class="hljs-keyword">enum</span> <span class="hljs-title class_">Color</span> { <span class="hljs-title class_">Red</span> = <span class="hljs-string">&quot;red&quot;</span>, <span class="hljs-title class_">Green</span> = <span class="hljs-string">&quot;green&quot;</span> }
""");
    }

    [Fact]
    public void Enum_Mixed()
    {
        AssertHighlighter("typescript",
"""
enum Mixed { A = 1, B = "b" }
""",
"""
<span class="hljs-keyword">enum</span> <span class="hljs-title class_">Mixed</span> { A = <span class="hljs-number">1</span>, B = <span class="hljs-string">&quot;b&quot;</span> }
""");
    }

    [Fact]
    public void Enum_Const()
    {
        AssertHighlighter("typescript",
"""
const enum Color { Red, Green }
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-keyword">enum</span> <span class="hljs-title class_">Color</span> { <span class="hljs-title class_">Red</span>, <span class="hljs-title class_">Green</span> }
""");
    }

    [Fact]
    public void Enum_Declare()
    {
        AssertHighlighter("typescript",
"""
declare enum Color { Red, Green }
""",
"""
<span class="hljs-keyword">declare</span> <span class="hljs-keyword">enum</span> <span class="hljs-title class_">Color</span> { <span class="hljs-title class_">Red</span>, <span class="hljs-title class_">Green</span> }
""");
    }

    [Fact]
    public void Enum_Computed()
    {
        AssertHighlighter("typescript",
"""
enum FileAccess { None = 0, Read = 1 << 1 }
""",
"""
<span class="hljs-keyword">enum</span> <span class="hljs-title class_">FileAccess</span> { <span class="hljs-title class_">None</span> = <span class="hljs-number">0</span>, <span class="hljs-title class_">Read</span> = <span class="hljs-number">1</span> &lt;&lt; <span class="hljs-number">1</span> }
""");
    }

    [Fact]
    public void Generic_Function()
    {
        AssertHighlighter("typescript",
"""
function f<T>(x: T): T { return x; }
""",
"""
<span class="hljs-keyword">function</span> f&lt;T&gt;(<span class="hljs-attr">x</span>: T): T { <span class="hljs-keyword">return</span> x; }
""");
    }

    [Fact]
    public void Generic_Arrow()
    {
        AssertHighlighter("typescript",
"""
const f = <T>(x: T): T => x;
""",
"""
<span class="hljs-keyword">const</span> f = &lt;T&gt;(<span class="hljs-attr">x</span>: T): <span class="hljs-function"><span class="hljs-params">T</span> =&gt;</span> x;
""");
    }

    [Fact]
    public void Generic_Constraint()
    {
        AssertHighlighter("typescript",
"""
function f<T extends string>(x: T) {}
""",
"""
<span class="hljs-keyword">function</span> f&lt;T <span class="hljs-keyword">extends</span> <span class="hljs-built_in">string</span>&gt;(<span class="hljs-attr">x</span>: T) {}
""");
    }

    [Fact]
    public void Generic_Default()
    {
        AssertHighlighter("typescript",
"""
function f<T = number>(x: T) {}
""",
"""
<span class="hljs-keyword">function</span> f&lt;T = <span class="hljs-built_in">number</span>&gt;(<span class="hljs-attr">x</span>: T) {}
""");
    }

    [Fact]
    public void Generic_Multiple()
    {
        AssertHighlighter("typescript",
"""
function f<T, U>(x: T, y: U) {}
""",
"""
<span class="hljs-keyword">function</span> f&lt;T, U&gt;(<span class="hljs-attr">x</span>: T, <span class="hljs-attr">y</span>: U) {}
""");
    }

    [Fact]
    public void Generic_CallSite()
    {
        AssertHighlighter("typescript",
"""
f<number>(1);
""",
"""
f&lt;<span class="hljs-built_in">number</span>&gt;(<span class="hljs-number">1</span>);
""");
    }

    [Fact]
    public void Generic_KeyofConstraint()
    {
        AssertHighlighter("typescript",
"""
function get<T, K extends keyof T>(obj: T, key: K): T[K] { return obj[key]; }
""",
"""
<span class="hljs-keyword">function</span> get&lt;T, K <span class="hljs-keyword">extends</span> keyof T&gt;(<span class="hljs-attr">obj</span>: T, <span class="hljs-attr">key</span>: K): T[K] { <span class="hljs-keyword">return</span> obj[key]; }
""");
    }

    [Fact]
    public void Generic_ClassGeneric()
    {
        AssertHighlighter("typescript",
"""
class Box<T> { value: T; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Box</span>&lt;T&gt; { <span class="hljs-attr">value</span>: T; }
""");
    }

    [Fact]
    public void Generic_InterfaceGeneric()
    {
        AssertHighlighter("typescript",
"""
interface Box<T> { value: T; }
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Box</span>&lt;T&gt; { <span class="hljs-attr">value</span>: T; }
""");
    }

    [Fact]
    public void Generic_TypeAliasGeneric()
    {
        AssertHighlighter("typescript",
"""
type Box<T> = { value: T };
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Box</span>&lt;T&gt; = { <span class="hljs-attr">value</span>: T };
""");
    }

    [Fact]
    public void Generic_NestedGeneric()
    {
        AssertHighlighter("typescript",
"""
const a: Array<Map<string, number>> = [];
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">a</span>: <span class="hljs-title class_">Array</span>&lt;<span class="hljs-title class_">Map</span>&lt;<span class="hljs-built_in">string</span>, <span class="hljs-built_in">number</span>&gt;&gt; = [];
""");
    }

    [Fact]
    public void ClassTS_Public()
    {
        AssertHighlighter("typescript",
"""
class A { public x: number = 1; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">public</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void ClassTS_Private()
    {
        AssertHighlighter("typescript",
"""
class A { private x: number = 1; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">private</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void ClassTS_Protected()
    {
        AssertHighlighter("typescript",
"""
class A { protected x: number = 1; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">protected</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void ClassTS_Readonly()
    {
        AssertHighlighter("typescript",
"""
class A { readonly x: number = 1; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">readonly</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void ClassTS_ParameterProperty()
    {
        AssertHighlighter("typescript",
"""
class A { constructor(public x: number, private y: string) {} }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-title function_">constructor</span>(<span class="hljs-params"><span class="hljs-keyword">public</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>, <span class="hljs-keyword">private</span> <span class="hljs-attr">y</span>: <span class="hljs-built_in">string</span></span>) {} }
""");
    }

    [Fact]
    public void ClassTS_Abstract()
    {
        AssertHighlighter("typescript",
"""
abstract class A { abstract foo(): void; }
""",
"""
<span class="hljs-keyword">abstract</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">abstract</span> <span class="hljs-title function_">foo</span>(): <span class="hljs-built_in">void</span>; }
""");
    }

    [Fact]
    public void ClassTS_Implements()
    {
        AssertHighlighter("typescript",
"""
class A implements Foo, Bar {}
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> <span class="hljs-keyword">implements</span> <span class="hljs-title class_">Foo</span>, <span class="hljs-title class_">Bar</span> {}
""");
    }

    [Fact]
    public void ClassTS_Override()
    {
        AssertHighlighter("typescript",
"""
class B extends A { override foo(): void {} }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">B</span> <span class="hljs-keyword">extends</span> <span class="hljs-title class_ inherited__">A</span> { <span class="hljs-keyword">override</span> <span class="hljs-title function_">foo</span>(): <span class="hljs-built_in">void</span> {} }
""");
    }

    [Fact]
    public void ClassTS_AccessorDefinite()
    {
        AssertHighlighter("typescript",
"""
class A { x!: number; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { x!: <span class="hljs-built_in">number</span>; }
""");
    }

    [Fact]
    public void ClassTS_Optional()
    {
        AssertHighlighter("typescript",
"""
class A { x?: number; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-attr">x</span>?: <span class="hljs-built_in">number</span>; }
""");
    }

    [Fact]
    public void ClassTS_PrivateField()
    {
        AssertHighlighter("typescript",
"""
class A { #x = 1; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { #x = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void ClassTS_Generic()
    {
        AssertHighlighter("typescript",
"""
class Box<T> { constructor(public value: T) {} }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Box</span>&lt;T&gt; { <span class="hljs-title function_">constructor</span>(<span class="hljs-params"><span class="hljs-keyword">public</span> <span class="hljs-attr">value</span>: T</span>) {} }
""");
    }

    [Fact]
    public void ClassTS_GetterTyped()
    {
        AssertHighlighter("typescript",
"""
class A { get x(): number { return 1; } }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">get</span> <span class="hljs-title function_">x</span>(): <span class="hljs-built_in">number</span> { <span class="hljs-keyword">return</span> <span class="hljs-number">1</span>; } }
""");
    }

    [Fact]
    public void ClassTS_SetterTyped()
    {
        AssertHighlighter("typescript",
"""
class A { set x(v: number) {} }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">set</span> <span class="hljs-title function_">x</span>(<span class="hljs-params"><span class="hljs-attr">v</span>: <span class="hljs-built_in">number</span></span>) {} }
""");
    }

    [Fact]
    public void ClassTS_StaticMethod()
    {
        AssertHighlighter("typescript",
"""
class A { static foo(): void {} }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">static</span> <span class="hljs-title function_">foo</span>(): <span class="hljs-built_in">void</span> {} }
""");
    }

    [Fact]
    public void ClassTS_ThisType()
    {
        AssertHighlighter("typescript",
"""
class Builder { add(): this { return this; } }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Builder</span> { <span class="hljs-title function_">add</span>(): <span class="hljs-variable language_">this</span> { <span class="hljs-keyword">return</span> <span class="hljs-variable language_">this</span>; } }
""");
    }

    [Fact]
    public void Namespace_Simple()
    {
        AssertHighlighter("typescript",
"""
namespace Foo { export const x = 1; }
""",
"""
<span class="hljs-keyword">namespace</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-keyword">export</span> <span class="hljs-keyword">const</span> x = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Namespace_Nested()
    {
        AssertHighlighter("typescript",
"""
namespace A { export namespace B { export const x = 1; } }
""",
"""
<span class="hljs-keyword">namespace</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">export</span> <span class="hljs-keyword">namespace</span> <span class="hljs-title class_">B</span> { <span class="hljs-keyword">export</span> <span class="hljs-keyword">const</span> x = <span class="hljs-number">1</span>; } }
""");
    }

    [Fact]
    public void Namespace_Function()
    {
        AssertHighlighter("typescript",
"""
namespace Math { export function double(x: number): number { return x * 2; } }
""",
"""
<span class="hljs-keyword">namespace</span> <span class="hljs-title class_">Math</span> { <span class="hljs-keyword">export</span> <span class="hljs-keyword">function</span> <span class="hljs-title function_">double</span>(<span class="hljs-params"><span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span></span>): <span class="hljs-built_in">number</span> { <span class="hljs-keyword">return</span> x * <span class="hljs-number">2</span>; } }
""");
    }

    [Fact]
    public void Namespace_Interface()
    {
        AssertHighlighter("typescript",
"""
namespace Foo { export interface Bar { x: number; } }
""",
"""
<span class="hljs-keyword">namespace</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-keyword">export</span> <span class="hljs-keyword">interface</span> <span class="hljs-title class_">Bar</span> { <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>; } }
""");
    }

    [Fact]
    public void Namespace_TypeAlias()
    {
        AssertHighlighter("typescript",
"""
namespace Foo { export type Bar = string; }
""",
"""
<span class="hljs-keyword">namespace</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-keyword">export</span> <span class="hljs-keyword">type</span> <span class="hljs-title class_">Bar</span> = <span class="hljs-built_in">string</span>; }
""");
    }

    [Fact]
    public void Namespace_ModuleKeyword()
    {
        AssertHighlighter("typescript",
"""
module Foo { export const x = 1; }
""",
"""
<span class="hljs-variable language_">module</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-keyword">export</span> <span class="hljs-keyword">const</span> x = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Ambient_DeclareConst()
    {
        AssertHighlighter("typescript",
"""
declare const x: number;
""",
"""
<span class="hljs-keyword">declare</span> <span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>;
""");
    }

    [Fact]
    public void Ambient_DeclareLet()
    {
        AssertHighlighter("typescript",
"""
declare let x: number;
""",
"""
<span class="hljs-keyword">declare</span> <span class="hljs-keyword">let</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>;
""");
    }

    [Fact]
    public void Ambient_DeclareVar()
    {
        AssertHighlighter("typescript",
"""
declare var x: number;
""",
"""
<span class="hljs-keyword">declare</span> <span class="hljs-keyword">var</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>;
""");
    }

    [Fact]
    public void Ambient_DeclareFunction()
    {
        AssertHighlighter("typescript",
"""
declare function f(x: number): number;
""",
"""
<span class="hljs-keyword">declare</span> <span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"><span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span></span>): <span class="hljs-built_in">number</span>;
""");
    }

    [Fact]
    public void Ambient_DeclareClass()
    {
        AssertHighlighter("typescript",
"""
declare class Foo { x: number; }
""",
"""
<span class="hljs-keyword">declare</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>; }
""");
    }

    [Fact]
    public void Ambient_DeclareInterface()
    {
        AssertHighlighter("typescript",
"""
declare interface Foo { x: number; }
""",
"""
<span class="hljs-keyword">declare</span> <span class="hljs-keyword">interface</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>; }
""");
    }

    [Fact]
    public void Ambient_DeclareNamespace()
    {
        AssertHighlighter("typescript",
"""
declare namespace Foo { const x: number; }
""",
"""
<span class="hljs-keyword">declare</span> <span class="hljs-keyword">namespace</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>; }
""");
    }

    [Fact]
    public void Ambient_DeclareModule()
    {
        AssertHighlighter("typescript",
"""
declare module "foo" { export const x: number; }
""",
"""
<span class="hljs-keyword">declare</span> <span class="hljs-variable language_">module</span> <span class="hljs-string">&quot;foo&quot;</span> { <span class="hljs-keyword">export</span> <span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>; }
""");
    }

    [Fact]
    public void Ambient_DeclareGlobal()
    {
        AssertHighlighter("typescript",
"""
declare global { interface Window { x: number; } }
""",
"""
<span class="hljs-keyword">declare</span> <span class="hljs-variable language_">global</span> { <span class="hljs-keyword">interface</span> <span class="hljs-title class_">Window</span> { <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>; } }
""");
    }

    [Fact]
    public void Cast_As()
    {
        AssertHighlighter("typescript",
"""
const x = y as number;
""",
"""
<span class="hljs-keyword">const</span> x = y <span class="hljs-keyword">as</span> <span class="hljs-built_in">number</span>;
""");
    }

    [Fact]
    public void Cast_AsUnknown()
    {
        AssertHighlighter("typescript",
"""
const x = y as unknown as number;
""",
"""
<span class="hljs-keyword">const</span> x = y <span class="hljs-keyword">as</span> <span class="hljs-built_in">unknown</span> <span class="hljs-keyword">as</span> <span class="hljs-built_in">number</span>;
""");
    }

    [Fact]
    public void Cast_AsConst()
    {
        AssertHighlighter("typescript",
"""
const x = [1, 2, 3] as const;
""",
"""
<span class="hljs-keyword">const</span> x = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>] <span class="hljs-keyword">as</span> <span class="hljs-keyword">const</span>;
""");
    }

    [Fact]
    public void Cast_AsConstObj()
    {
        AssertHighlighter("typescript",
"""
const x = { kind: "foo" } as const;
""",
"""
<span class="hljs-keyword">const</span> x = { <span class="hljs-attr">kind</span>: <span class="hljs-string">&quot;foo&quot;</span> } <span class="hljs-keyword">as</span> <span class="hljs-keyword">const</span>;
""");
    }

    [Fact]
    public void Cast_Satisfies()
    {
        AssertHighlighter("typescript",
"""
const x = { a: 1 } satisfies Record<string, number>;
""",
"""
<span class="hljs-keyword">const</span> x = { <span class="hljs-attr">a</span>: <span class="hljs-number">1</span> } <span class="hljs-keyword">satisfies</span> <span class="hljs-title class_">Record</span>&lt;<span class="hljs-built_in">string</span>, <span class="hljs-built_in">number</span>&gt;;
""");
    }

    [Fact]
    public void Cast_NonNull()
    {
        AssertHighlighter("typescript",
"""
const x = y!;
""",
"""
<span class="hljs-keyword">const</span> x = y!;
""");
    }

    [Fact]
    public void Cast_NonNullChain()
    {
        AssertHighlighter("typescript",
"""
const x = y!.z;
""",
"""
<span class="hljs-keyword">const</span> x = y!.<span class="hljs-property">z</span>;
""");
    }

    [Fact]
    public void Cast_DefiniteAssignment()
    {
        AssertHighlighter("typescript",
"""
let x!: number;
""",
"""
<span class="hljs-keyword">let</span> x!: <span class="hljs-built_in">number</span>;
""");
    }

    [Fact]
    public void UtilityType_Partial()
    {
        AssertHighlighter("typescript",
"""
type T = Partial<Foo>;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">Partial</span>&lt;<span class="hljs-title class_">Foo</span>&gt;;
""");
    }

    [Fact]
    public void UtilityType_Required()
    {
        AssertHighlighter("typescript",
"""
type T = Required<Foo>;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">Required</span>&lt;<span class="hljs-title class_">Foo</span>&gt;;
""");
    }

    [Fact]
    public void UtilityType_Readonly()
    {
        AssertHighlighter("typescript",
"""
type T = Readonly<Foo>;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">Readonly</span>&lt;<span class="hljs-title class_">Foo</span>&gt;;
""");
    }

    [Fact]
    public void UtilityType_Pick()
    {
        AssertHighlighter("typescript",
"""
type T = Pick<Foo, "x" | "y">;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">Pick</span>&lt;<span class="hljs-title class_">Foo</span>, <span class="hljs-string">&quot;x&quot;</span> | <span class="hljs-string">&quot;y&quot;</span>&gt;;
""");
    }

    [Fact]
    public void UtilityType_Omit()
    {
        AssertHighlighter("typescript",
"""
type T = Omit<Foo, "x">;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">Omit</span>&lt;<span class="hljs-title class_">Foo</span>, <span class="hljs-string">&quot;x&quot;</span>&gt;;
""");
    }

    [Fact]
    public void UtilityType_Record()
    {
        AssertHighlighter("typescript",
"""
type T = Record<string, number>;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">Record</span>&lt;<span class="hljs-built_in">string</span>, <span class="hljs-built_in">number</span>&gt;;
""");
    }

    [Fact]
    public void UtilityType_Exclude()
    {
        AssertHighlighter("typescript",
"""
type T = Exclude<string | number, string>;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">Exclude</span>&lt;<span class="hljs-built_in">string</span> | <span class="hljs-built_in">number</span>, <span class="hljs-built_in">string</span>&gt;;
""");
    }

    [Fact]
    public void UtilityType_Extract()
    {
        AssertHighlighter("typescript",
"""
type T = Extract<string | number, string>;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">Extract</span>&lt;<span class="hljs-built_in">string</span> | <span class="hljs-built_in">number</span>, <span class="hljs-built_in">string</span>&gt;;
""");
    }

    [Fact]
    public void UtilityType_NonNullable()
    {
        AssertHighlighter("typescript",
"""
type T = NonNullable<string | null>;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">NonNullable</span>&lt;<span class="hljs-built_in">string</span> | <span class="hljs-literal">null</span>&gt;;
""");
    }

    [Fact]
    public void UtilityType_Parameters()
    {
        AssertHighlighter("typescript",
"""
type T = Parameters<typeof f>;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">Parameters</span>&lt;<span class="hljs-keyword">typeof</span> f&gt;;
""");
    }

    [Fact]
    public void UtilityType_ReturnType()
    {
        AssertHighlighter("typescript",
"""
type T = ReturnType<typeof f>;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">ReturnType</span>&lt;<span class="hljs-keyword">typeof</span> f&gt;;
""");
    }

    [Fact]
    public void UtilityType_Awaited()
    {
        AssertHighlighter("typescript",
"""
type T = Awaited<Promise<number>>;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">Awaited</span>&lt;<span class="hljs-title class_">Promise</span>&lt;<span class="hljs-built_in">number</span>&gt;&gt;;
""");
    }

    [Fact]
    public void UtilityType_Uppercase()
    {
        AssertHighlighter("typescript",
"""
type T = Uppercase<"hello">;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">Uppercase</span>&lt;<span class="hljs-string">&quot;hello&quot;</span>&gt;;
""");
    }

    [Fact]
    public void UtilityType_Lowercase()
    {
        AssertHighlighter("typescript",
"""
type T = Lowercase<"HELLO">;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">Lowercase</span>&lt;<span class="hljs-string">&quot;HELLO&quot;</span>&gt;;
""");
    }

    [Fact]
    public void UtilityType_Capitalize()
    {
        AssertHighlighter("typescript",
"""
type T = Capitalize<"hello">;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">Capitalize</span>&lt;<span class="hljs-string">&quot;hello&quot;</span>&gt;;
""");
    }

    [Fact]
    public void UtilityType_Uncapitalize()
    {
        AssertHighlighter("typescript",
"""
type T = Uncapitalize<"Hello">;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">Uncapitalize</span>&lt;<span class="hljs-string">&quot;Hello&quot;</span>&gt;;
""");
    }

    [Fact]
    public void UtilityType_InstanceType()
    {
        AssertHighlighter("typescript",
"""
type T = InstanceType<typeof Foo>;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">InstanceType</span>&lt;<span class="hljs-keyword">typeof</span> <span class="hljs-title class_">Foo</span>&gt;;
""");
    }

    [Fact]
    public void UtilityType_ConstructorParameters()
    {
        AssertHighlighter("typescript",
"""
type T = ConstructorParameters<typeof Foo>;
""",
"""
<span class="hljs-keyword">type</span> T = <span class="hljs-title class_">ConstructorParameters</span>&lt;<span class="hljs-keyword">typeof</span> <span class="hljs-title class_">Foo</span>&gt;;
""");
    }

    [Fact]
    public void Tuple_Basic()
    {
        AssertHighlighter("typescript",
"""
const t: [number, string] = [1, "a"];
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">t</span>: [<span class="hljs-built_in">number</span>, <span class="hljs-built_in">string</span>] = [<span class="hljs-number">1</span>, <span class="hljs-string">&quot;a&quot;</span>];
""");
    }

    [Fact]
    public void Tuple_Optional()
    {
        AssertHighlighter("typescript",
"""
const t: [number, string?] = [1];
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">t</span>: [<span class="hljs-built_in">number</span>, <span class="hljs-built_in">string</span>?] = [<span class="hljs-number">1</span>];
""");
    }

    [Fact]
    public void Tuple_Rest()
    {
        AssertHighlighter("typescript",
"""
const t: [number, ...string[]] = [1, "a", "b"];
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">t</span>: [<span class="hljs-built_in">number</span>, ...<span class="hljs-built_in">string</span>[]] = [<span class="hljs-number">1</span>, <span class="hljs-string">&quot;a&quot;</span>, <span class="hljs-string">&quot;b&quot;</span>];
""");
    }

    [Fact]
    public void Tuple_Labeled()
    {
        AssertHighlighter("typescript",
"""
const t: [x: number, y: string] = [1, "a"];
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">t</span>: [<span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>, <span class="hljs-attr">y</span>: <span class="hljs-built_in">string</span>] = [<span class="hljs-number">1</span>, <span class="hljs-string">&quot;a&quot;</span>];
""");
    }

    [Fact]
    public void Tuple_Readonly()
    {
        AssertHighlighter("typescript",
"""
const t: readonly [number, string] = [1, "a"];
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">t</span>: <span class="hljs-keyword">readonly</span> [<span class="hljs-built_in">number</span>, <span class="hljs-built_in">string</span>] = [<span class="hljs-number">1</span>, <span class="hljs-string">&quot;a&quot;</span>];
""");
    }

    [Fact]
    public void Tuple_Empty()
    {
        AssertHighlighter("typescript",
"""
const t: [] = [];
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-attr">t</span>: [] = [];
""");
    }

    [Fact]
    public void Decorator_Class()
    {
        AssertHighlighter("typescript",
"""
@sealed
class Foo {}
""",
"""
<span class="hljs-meta">@sealed</span>
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Foo</span> {}
""");
    }

    [Fact]
    public void Decorator_WithArgs()
    {
        AssertHighlighter("typescript",
"""
@inject("svc")
class Foo {}
""",
"""
<span class="hljs-meta">@inject</span>(<span class="hljs-string">&quot;svc&quot;</span>)
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Foo</span> {}
""");
    }

    [Fact]
    public void Decorator_Method()
    {
        AssertHighlighter("typescript",
"""
class Foo { @log foo() {} }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-meta">@log</span> <span class="hljs-title function_">foo</span>(<span class="hljs-params"></span>) {} }
""");
    }

    [Fact]
    public void Decorator_Property()
    {
        AssertHighlighter("typescript",
"""
class Foo { @observable x: number = 1; }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-meta">@observable</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Decorator_Parameter()
    {
        AssertHighlighter("typescript",
"""
class Foo { foo(@inject("svc") x: any) {} }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-title function_">foo</span>(<span class="hljs-params"><span class="hljs-meta">@inject</span>(<span class="hljs-string">&quot;svc&quot;</span>) <span class="hljs-attr">x</span>: <span class="hljs-built_in">any</span></span>) {} }
""");
    }

    [Fact]
    public void Decorator_Accessor()
    {
        AssertHighlighter("typescript",
"""
class Foo { @observable get x() { return 1; } }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-meta">@observable</span> <span class="hljs-keyword">get</span> <span class="hljs-title function_">x</span>() { <span class="hljs-keyword">return</span> <span class="hljs-number">1</span>; } }
""");
    }

    [Fact]
    public void TypePredicate_Is()
    {
        AssertHighlighter("typescript",
"""
function isString(x: unknown): x is string { return typeof x === "string"; }
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">isString</span>(<span class="hljs-params"><span class="hljs-attr">x</span>: <span class="hljs-built_in">unknown</span></span>): x is <span class="hljs-built_in">string</span> { <span class="hljs-keyword">return</span> <span class="hljs-keyword">typeof</span> x === <span class="hljs-string">&quot;string&quot;</span>; }
""");
    }

    [Fact]
    public void TypePredicate_Asserts()
    {
        AssertHighlighter("typescript",
"""
function assert(x: unknown): asserts x { if (!x) throw new Error(); }
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">assert</span>(<span class="hljs-params"><span class="hljs-attr">x</span>: <span class="hljs-built_in">unknown</span></span>): asserts x { <span class="hljs-keyword">if</span> (!x) <span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">Error</span>(); }
""");
    }

    [Fact]
    public void TypePredicate_AssertsIs()
    {
        AssertHighlighter("typescript",
"""
function assertString(x: unknown): asserts x is string { if (typeof x !== "string") throw new Error(); }
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">assertString</span>(<span class="hljs-params"><span class="hljs-attr">x</span>: <span class="hljs-built_in">unknown</span></span>): asserts x is <span class="hljs-built_in">string</span> { <span class="hljs-keyword">if</span> (<span class="hljs-keyword">typeof</span> x !== <span class="hljs-string">&quot;string&quot;</span>) <span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">Error</span>(); }
""");
    }

    [Fact]
    public void Function_Declaration()
    {
        AssertHighlighter("typescript",
"""
function foo(x: number): number { return x; }
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">foo</span>(<span class="hljs-params"><span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span></span>): <span class="hljs-built_in">number</span> { <span class="hljs-keyword">return</span> x; }
""");
    }

    [Fact]
    public void Function_Arrow()
    {
        AssertHighlighter("typescript",
"""
const f = (x: number): number => x;
""",
"""
<span class="hljs-keyword">const</span> f = (<span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>): <span class="hljs-function"><span class="hljs-params">number</span> =&gt;</span> x;
""");
    }

    [Fact]
    public void Function_Async()
    {
        AssertHighlighter("typescript",
"""
async function f(): Promise<number> { return 1; }
""",
"""
<span class="hljs-keyword">async</span> <span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"></span>): <span class="hljs-title class_">Promise</span>&lt;<span class="hljs-built_in">number</span>&gt; { <span class="hljs-keyword">return</span> <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Function_AsyncArrow()
    {
        AssertHighlighter("typescript",
"""
const f = async (): Promise<number> => 1;
""",
"""
<span class="hljs-keyword">const</span> f = <span class="hljs-title function_">async</span> (): <span class="hljs-title class_">Promise</span>&lt;<span class="hljs-built_in">number</span>&gt; =&gt; <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Function_Generator()
    {
        AssertHighlighter("typescript",
"""
function* g(): Generator<number> { yield 1; }
""",
"""
<span class="hljs-keyword">function</span>* <span class="hljs-title function_">g</span>(): <span class="hljs-title class_">Generator</span>&lt;<span class="hljs-built_in">number</span>&gt; { <span class="hljs-keyword">yield</span> <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Function_Overload()
    {
        AssertHighlighter("typescript",
"""
function f(x: string): string;
function f(x: number): number;
function f(x: any): any { return x; }
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"><span class="hljs-attr">x</span>: <span class="hljs-built_in">string</span></span>): <span class="hljs-built_in">string</span>;
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"><span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span></span>): <span class="hljs-built_in">number</span>;
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"><span class="hljs-attr">x</span>: <span class="hljs-built_in">any</span></span>): <span class="hljs-built_in">any</span> { <span class="hljs-keyword">return</span> x; }
""");
    }

    [Fact]
    public void Function_OptionalParam()
    {
        AssertHighlighter("typescript",
"""
function f(x: number, y?: string): void {}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"><span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>, <span class="hljs-attr">y</span>?: <span class="hljs-built_in">string</span></span>): <span class="hljs-built_in">void</span> {}
""");
    }

    [Fact]
    public void Function_DefaultParam()
    {
        AssertHighlighter("typescript",
"""
function f(x: number = 1): void {}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"><span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">1</span></span>): <span class="hljs-built_in">void</span> {}
""");
    }

    [Fact]
    public void Module_ImportDefault()
    {
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
"""
import { x } from 'mod';
""",
"""
<span class="hljs-keyword">import</span> { x } <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;mod&#x27;</span>;
""");
    }

    [Fact]
    public void Module_ImportType()
    {
        AssertHighlighter("typescript",
"""
import type { Foo } from 'mod';
""",
"""
<span class="hljs-keyword">import</span> <span class="hljs-keyword">type</span> { <span class="hljs-title class_">Foo</span> } <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;mod&#x27;</span>;
""");
    }

    [Fact]
    public void Module_ImportNamedType()
    {
        AssertHighlighter("typescript",
"""
import { type Foo, bar } from 'mod';
""",
"""
<span class="hljs-keyword">import</span> { <span class="hljs-keyword">type</span> <span class="hljs-title class_">Foo</span>, bar } <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;mod&#x27;</span>;
""");
    }

    [Fact]
    public void Module_ImportEquals()
    {
        AssertHighlighter("typescript",
"""
import foo = require('foo');
""",
"""
<span class="hljs-keyword">import</span> foo = <span class="hljs-built_in">require</span>(<span class="hljs-string">&#x27;foo&#x27;</span>);
""");
    }

    [Fact]
    public void Module_ImportNamespace()
    {
        AssertHighlighter("typescript",
"""
import * as ns from 'mod';
""",
"""
<span class="hljs-keyword">import</span> * <span class="hljs-keyword">as</span> ns <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;mod&#x27;</span>;
""");
    }

    [Fact]
    public void Module_ExportType()
    {
        AssertHighlighter("typescript",
"""
export type { Foo };
""",
"""
<span class="hljs-keyword">export</span> <span class="hljs-keyword">type</span> { <span class="hljs-title class_">Foo</span> };
""");
    }

    [Fact]
    public void Module_ExportNamedType()
    {
        AssertHighlighter("typescript",
"""
export { type Foo, bar };
""",
"""
<span class="hljs-keyword">export</span> { <span class="hljs-keyword">type</span> <span class="hljs-title class_">Foo</span>, bar };
""");
    }

    [Fact]
    public void Module_ExportInterface()
    {
        AssertHighlighter("typescript",
"""
export interface Foo { x: number; }
""",
"""
<span class="hljs-keyword">export</span> <span class="hljs-keyword">interface</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span>; }
""");
    }

    [Fact]
    public void Module_ExportType_2()
    {
        AssertHighlighter("typescript",
"""
export type Foo = number;
""",
"""
<span class="hljs-keyword">export</span> <span class="hljs-keyword">type</span> <span class="hljs-title class_">Foo</span> = <span class="hljs-built_in">number</span>;
""");
    }

    [Fact]
    public void Module_ExportEnum()
    {
        AssertHighlighter("typescript",
"""
export enum Color { Red, Green }
""",
"""
<span class="hljs-keyword">export</span> <span class="hljs-keyword">enum</span> <span class="hljs-title class_">Color</span> { <span class="hljs-title class_">Red</span>, <span class="hljs-title class_">Green</span> }
""");
    }

    [Fact]
    public void Module_ExportNamespace()
    {
        AssertHighlighter("typescript",
"""
export namespace Foo { export const x = 1; }
""",
"""
<span class="hljs-keyword">export</span> <span class="hljs-keyword">namespace</span> <span class="hljs-title class_">Foo</span> { <span class="hljs-keyword">export</span> <span class="hljs-keyword">const</span> x = <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Module_ExportEquals()
    {
        AssertHighlighter("typescript",
"""
export = Foo;
""",
"""
<span class="hljs-keyword">export</span> = <span class="hljs-title class_">Foo</span>;
""");
    }

    [Fact]
    public void Module_ReExportType()
    {
        AssertHighlighter("typescript",
"""
export type { Foo } from 'mod';
""",
"""
<span class="hljs-keyword">export</span> <span class="hljs-keyword">type</span> { <span class="hljs-title class_">Foo</span> } <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;mod&#x27;</span>;
""");
    }

    [Fact]
    public void Destructure_ObjectTyped()
    {
        AssertHighlighter("typescript",
"""
const { a, b }: { a: number; b: string } = obj;
""",
"""
<span class="hljs-keyword">const</span> { a, b }: { <span class="hljs-attr">a</span>: <span class="hljs-built_in">number</span>; <span class="hljs-attr">b</span>: <span class="hljs-built_in">string</span> } = obj;
""");
    }

    [Fact]
    public void Destructure_ArrayTyped()
    {
        AssertHighlighter("typescript",
"""
const [a, b]: [number, string] = arr;
""",
"""
<span class="hljs-keyword">const</span> [a, b]: [<span class="hljs-built_in">number</span>, <span class="hljs-built_in">string</span>] = arr;
""");
    }

    [Fact]
    public void Destructure_ParamObject()
    {
        AssertHighlighter("typescript",
"""
function f({ a, b }: { a: number; b: string }) {}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params">{ a, b }: { a: <span class="hljs-built_in">number</span>; b: <span class="hljs-built_in">string</span> }</span>) {}
""");
    }

    [Fact]
    public void Destructure_ParamArray()
    {
        AssertHighlighter("typescript",
"""
function f([a, b]: [number, string]) {}
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params">[a, b]: [<span class="hljs-built_in">number</span>, <span class="hljs-built_in">string</span>]</span>) {}
""");
    }

    [Fact]
    public void Operator_OptionalChain()
    {
        AssertHighlighter("typescript",
"""
a?.b?.c;
""",
"""
a?.<span class="hljs-property">b</span>?.<span class="hljs-property">c</span>;
""");
    }

    [Fact]
    public void Operator_Nullish()
    {
        AssertHighlighter("typescript",
"""
const x = a ?? b;
""",
"""
<span class="hljs-keyword">const</span> x = a ?? b;
""");
    }

    [Fact]
    public void Operator_NonNullChain()
    {
        AssertHighlighter("typescript",
"""
a!.b!.c;
""",
"""
a!.<span class="hljs-property">b</span>!.<span class="hljs-property">c</span>;
""");
    }

    [Fact]
    public void Operator_Spread()
    {
        AssertHighlighter("typescript",
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
        AssertHighlighter("typescript",
"""
const a = { ...b };
""",
"""
<span class="hljs-keyword">const</span> a = { ...b };
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("typescript",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyWhitespace()
    {
        AssertHighlighter("typescript",
"""


""",
"""


""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("typescript",
"""
// just a comment
""",
"""
<span class="hljs-comment">// just a comment</span>
""");
    }

    [Fact]
    public void SpecialEdge_Shebang()
    {
        AssertHighlighter("typescript",
"""
#!/usr/bin/env ts-node
console.log("hi");
""",
"""
<span class="hljs-meta">#!/usr/bin/env ts-node</span>
<span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(<span class="hljs-string">&quot;hi&quot;</span>);
""");
    }

    [Fact]
    public void SpecialEdge_UseStrict()
    {
        AssertHighlighter("typescript",
"""
"use strict";
const x = 1;
""",
"""
<span class="hljs-meta">&quot;use strict&quot;</span>;
<span class="hljs-keyword">const</span> x = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void SpecialEdge_TripleSlashRef()
    {
        AssertHighlighter("typescript",
"""
/// <reference path="./foo.d.ts" />
""",
"""
<span class="hljs-comment">/// &lt;reference path=&quot;./foo.d.ts&quot; /&gt;</span>
""");
    }
}
