namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class DHighlighterTests
{
    [Fact]
    public void Class()
    {
        AssertHighlighter("d",
"""
module app.shapes;

import std.math : PI;

interface Shape
{
    double area() const;
}

final class Circle : Shape
{
    private double radius;

    this(double radius) pure nothrow @safe
    {
        this.radius = radius;
    }

    override double area() const @nogc
    {
        return PI * radius * radius;
    }
}

struct Point(T)
{
    T x, y;
}
""",
"""
<span class="hljs-keyword">module</span> app.shapes;

<span class="hljs-keyword">import</span> std.math : PI;

<span class="hljs-keyword">interface</span> Shape
{
    <span class="hljs-built_in">double</span> area() <span class="hljs-keyword">const</span>;
}

<span class="hljs-keyword">final</span> <span class="hljs-keyword">class</span> Circle : Shape
{
    <span class="hljs-keyword">private</span> <span class="hljs-built_in">double</span> radius;

    <span class="hljs-keyword">this</span>(<span class="hljs-built_in">double</span> radius) <span class="hljs-keyword">pure</span> <span class="hljs-keyword">nothrow</span> <span class="hljs-keyword">@safe</span>
    {
        <span class="hljs-keyword">this</span>.radius = radius;
    }

    <span class="hljs-keyword">override</span> <span class="hljs-built_in">double</span> area() <span class="hljs-keyword">const</span> <span class="hljs-keyword">@nogc</span>
    {
        <span class="hljs-keyword">return</span> PI * radius * radius;
    }
}

<span class="hljs-keyword">struct</span> Point(T)
{
    T x, y;
}
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("d",
"""
// line comment
/* block
   comment */
/+ nesting /+ inner +/ still comment +/
/// ddoc comment
/** ddoc block */
int x; /+ unterminated
int y;
""",
"""
<span class="hljs-comment">// line comment</span>
<span class="hljs-comment">/* block
   comment */</span>
<span class="hljs-comment">/+ nesting <span class="hljs-comment">/+ inner +/</span> still comment +/</span>
<span class="hljs-comment">/// ddoc comment</span>
<span class="hljs-comment">/** ddoc block */</span>
<span class="hljs-keyword">int</span> x; <span class="hljs-comment">/+ unterminated
int y;</span>
""");
    }

    [Fact]
    public void Edge()
    {
        AssertHighlighter("d",
"""
string s = "unterminated
int z = 1;
""",
"""
<span class="hljs-built_in">string</span> s = <span class="hljs-string">&quot;unterminated
int z = 1;</span>
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("d", "", "");
    }

    [Fact]
    public void Hello()
    {
        AssertHighlighter("d",
"""
import std.stdio;

void main()
{
    writeln("Hello, World!");
}
""",
"""
<span class="hljs-keyword">import</span> std.stdio;

<span class="hljs-keyword">void</span> main()
{
    writeln(<span class="hljs-string">&quot;Hello, World!&quot;</span>);
}
""");
    }

    [Fact]
    public void Misc()
    {
        AssertHighlighter("d",
"""
#!/usr/bin/env rdmd
#line 42 "file.d"
@property int value() { return _value; }
@disable this();
unittest
{
    assert(add(1, 2) == 3);
}
scope(exit) writeln("done");
foreach_reverse (i; 0 .. 10) {}
__gshared int counter;
enum Color { red, green, blue }
auto dg = delegate int(int x) => x * 2;
version (Windows) {} else debug {}
""",
"""
<span class="hljs-meta">#!/usr/bin/env rdmd</span>
<span class="hljs-meta">#line 42 &quot;file.d&quot;</span>
<span class="hljs-keyword">@property</span> <span class="hljs-keyword">int</span> value() { <span class="hljs-keyword">return</span> _value; }
<span class="hljs-keyword">@disable</span> <span class="hljs-keyword">this</span>();
<span class="hljs-keyword">unittest</span>
{
    <span class="hljs-keyword">assert</span>(add(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>) == <span class="hljs-number">3</span>);
}
<span class="hljs-keyword">scope</span>(exit) writeln(<span class="hljs-string">&quot;done&quot;</span>);
<span class="hljs-keyword">foreach_reverse</span> (i; <span class="hljs-number">0</span> .. <span class="hljs-number">10</span>) {}
<span class="hljs-keyword">__gshared</span> <span class="hljs-keyword">int</span> counter;
<span class="hljs-keyword">enum</span> Color { red, green, blue }
<span class="hljs-keyword">auto</span> dg = <span class="hljs-built_in">delegate</span> <span class="hljs-keyword">int</span>(<span class="hljs-keyword">int</span> x) =&gt; x * <span class="hljs-number">2</span>;
<span class="hljs-keyword">version</span> (Windows) {} <span class="hljs-keyword">else</span> <span class="hljs-keyword">debug</span> {}
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("d",
"""
int a = 42;
long b = 1_000_000L;
uint c = 0xFFu;
auto d = 0b1010_1010;
double e = 3.14;
float f = 1.5f;
real g = 6.02e23L;
auto h = 0x1.8p3;
auto i = 2i;
auto j = .5;
auto k = 1e-10;
ulong l = 42UL;
auto m = 1..10;
auto n = arr[0..$];
auto o = 5.seconds;
auto p = 1.5fi;
auto q = 1.;
auto r = 0x1F_FFUL;
auto s = 1_0.2_5e-1_0f;
""",
"""
<span class="hljs-keyword">int</span> a = <span class="hljs-number">42</span>;
<span class="hljs-built_in">long</span> b = <span class="hljs-number">1_000_000L</span>;
<span class="hljs-built_in">uint</span> c = <span class="hljs-number">0xFFu</span>;
<span class="hljs-keyword">auto</span> d = <span class="hljs-number">0b1010_1010</span>;
<span class="hljs-built_in">double</span> e = <span class="hljs-number">3.14</span>;
<span class="hljs-built_in">float</span> f = <span class="hljs-number">1.5f</span>;
<span class="hljs-built_in">real</span> g = <span class="hljs-number">6.02e23L</span>;
<span class="hljs-keyword">auto</span> h = <span class="hljs-number">0x1.8p3</span>;
<span class="hljs-keyword">auto</span> i = <span class="hljs-number">2i</span>;
<span class="hljs-keyword">auto</span> j = .<span class="hljs-number">5</span>;
<span class="hljs-keyword">auto</span> k = <span class="hljs-number">1e-10</span>;
<span class="hljs-built_in">ulong</span> l = <span class="hljs-number">42UL</span>;
<span class="hljs-keyword">auto</span> m = <span class="hljs-number">1</span>..<span class="hljs-number">10</span>;
<span class="hljs-keyword">auto</span> n = arr[<span class="hljs-number">0</span>..$];
<span class="hljs-keyword">auto</span> o = <span class="hljs-number">5</span>.seconds;
<span class="hljs-keyword">auto</span> p = <span class="hljs-number">1.5fi</span>;
<span class="hljs-keyword">auto</span> q = <span class="hljs-number">1.</span>;
<span class="hljs-keyword">auto</span> r = <span class="hljs-number">0x1F_FFUL</span>;
<span class="hljs-keyword">auto</span> s = <span class="hljs-number">1_0.2_5e-1_0f</span>;
""");
    }

    [Fact]
    public void Ranges()
    {
        AssertHighlighter("d",
"""
import std.algorithm, std.range;

auto result = iota(1, 100)
    .filter!(x => x % 2 == 0)
    .map!(x => x * x)
    .take(10)
    .array;
""",
"""
<span class="hljs-keyword">import</span> std.algorithm, std.range;

<span class="hljs-keyword">auto</span> result = iota(<span class="hljs-number">1</span>, <span class="hljs-number">100</span>)
    .filter!(x =&gt; x % <span class="hljs-number">2</span> == <span class="hljs-number">0</span>)
    .map!(x =&gt; x * x)
    .take(<span class="hljs-number">10</span>)
    .array;
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("d",
"""
string a = "escaped \"quote\" and \n \x41 \u00e9 \U0001F600 \&amp;";
wstring b = "wide"w;
dstring c = "dchars"d;
auto r = r"C:\no\escapes";
auto bt = `backtick \n raw`;
auto h = x"0A 0B FF";
auto q = q"(delimited)";
auto t = q"{token}";
char ch = 'a';
char esc = '\n';
char bad = 'ab';
""",
"""
<span class="hljs-built_in">string</span> a = <span class="hljs-string">&quot;escaped \&quot;quote\&quot; and \n \x41 \u00e9 \U0001F600 \&amp;amp;&quot;</span>;
<span class="hljs-built_in">wstring</span> b = <span class="hljs-string">&quot;wide&quot;w</span>;
<span class="hljs-built_in">dstring</span> c = <span class="hljs-string">&quot;dchars&quot;d</span>;
<span class="hljs-keyword">auto</span> r = <span class="hljs-string">r&quot;C:\no\escapes&quot;</span>;
<span class="hljs-keyword">auto</span> bt = <span class="hljs-string">`backtick \n raw`</span>;
<span class="hljs-keyword">auto</span> h = <span class="hljs-string">x&quot;0A 0B FF&quot;</span>;
<span class="hljs-keyword">auto</span> q = <span class="hljs-string">q&quot;(delimited)&quot;</span>;
<span class="hljs-keyword">auto</span> t = <span class="hljs-string">q&quot;{token}&quot;</span>;
<span class="hljs-built_in">char</span> ch = <span class="hljs-string">&#x27;a&#x27;</span>;
<span class="hljs-built_in">char</span> esc = <span class="hljs-string">&#x27;\n&#x27;</span>;
<span class="hljs-built_in">char</span> bad = <span class="hljs-string">&#x27;ab&#x27;</span>;
""");
    }

    [Fact]
    public void Templates()
    {
        AssertHighlighter("d",
"""
auto add(T)(T a, T b) if (is(T : long))
{
    return a + b;
}

template Tuple(T...)
{
    alias Tuple = T;
}

mixin template Foo()
{
    int x = 5;
}

static if (__traits(compiles, add(1, 2)))
    pragma(msg, "ok");
""",
"""
<span class="hljs-keyword">auto</span> add(T)(T a, T b) <span class="hljs-keyword">if</span> (<span class="hljs-keyword">is</span>(T : <span class="hljs-built_in">long</span>))
{
    <span class="hljs-keyword">return</span> a + b;
}

<span class="hljs-keyword">template</span> Tuple(T...)
{
    <span class="hljs-keyword">alias</span> Tuple = T;
}

<span class="hljs-keyword">mixin</span> <span class="hljs-keyword">template</span> Foo()
{
    <span class="hljs-keyword">int</span> x = <span class="hljs-number">5</span>;
}

<span class="hljs-keyword">static</span> <span class="hljs-keyword">if</span> (<span class="hljs-keyword">__traits</span>(compiles, add(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>)))
    <span class="hljs-keyword">pragma</span>(msg, <span class="hljs-string">&quot;ok&quot;</span>);
""");
    }
}
