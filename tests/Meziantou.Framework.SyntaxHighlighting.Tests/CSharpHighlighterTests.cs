namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class CSharpHighlighterTests
{

    [Fact]
    public void Keyword_Class()
    {
        AssertHighlighter("csharp",
"""
class Foo { }
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title">Foo</span> { }
""");
    }

    [Fact]
    public void Keyword_Struct()
    {
        AssertHighlighter("csharp",
"""
struct Point { }
""",
"""
<span class="hljs-keyword">struct</span> Point { }
""");
    }

    [Fact]
    public void Keyword_Interface()
    {
        AssertHighlighter("csharp",
"""
interface IFoo { }
""",
"""
<span class="hljs-keyword">interface</span> <span class="hljs-title">IFoo</span> { }
""");
    }

    [Fact]
    public void Keyword_Enum()
    {
        AssertHighlighter("csharp",
"""
enum Color { Red, Green, Blue }
""",
"""
<span class="hljs-built_in">enum</span> Color { Red, Green, Blue }
""");
    }

    [Fact]
    public void Keyword_EnumByte()
    {
        AssertHighlighter("csharp",
"""
enum Color : byte { Red = 1, Green = 2 }
""",
"""
<span class="hljs-built_in">enum</span> Color : <span class="hljs-built_in">byte</span> { Red = <span class="hljs-number">1</span>, Green = <span class="hljs-number">2</span> }
""");
    }

    [Fact]
    public void Keyword_EnumFlags()
    {
        AssertHighlighter("csharp",
"""
[Flags]
public enum FileAccess { None = 0, Read = 1, Write = 2, All = Read | Write }
""",
"""
[<span class="hljs-meta">Flags</span>]
<span class="hljs-keyword">public</span> <span class="hljs-built_in">enum</span> FileAccess { None = <span class="hljs-number">0</span>, Read = <span class="hljs-number">1</span>, Write = <span class="hljs-number">2</span>, All = Read | Write }
""");
    }

    [Fact]
    public void Keyword_Record()
    {
        AssertHighlighter("csharp",
"""
record Foo(int X, int Y);
""",
"""
record Foo(int X, int Y);
""");
    }

    [Fact]
    public void Keyword_RecordClass()
    {
        AssertHighlighter("csharp",
"""
record class Foo(int X);
""",
"""
record class Foo(int X);
""");
    }

    [Fact]
    public void Keyword_RecordStruct()
    {
        AssertHighlighter("csharp",
"""
record struct Point(int X, int Y);
""",
"""
record struct Point(int X, int Y);
""");
    }

    [Fact]
    public void Keyword_ReadonlyRecordStruct()
    {
        AssertHighlighter("csharp",
"""
readonly record struct Money(decimal Amount, string Currency);
""",
"""
<span class="hljs-function"><span class="hljs-keyword">readonly</span> <span class="hljs-keyword">record</span> <span class="hljs-keyword">struct</span> <span class="hljs-title">Money</span>(<span class="hljs-params"><span class="hljs-built_in">decimal</span> Amount, <span class="hljs-built_in">string</span> Currency</span>)</span>;
""");
    }

    [Fact]
    public void Keyword_Namespace()
    {
        AssertHighlighter("csharp",
"""
namespace MyApp { }
""",
"""
<span class="hljs-keyword">namespace</span> <span class="hljs-title">MyApp</span> { }
""");
    }

    [Fact]
    public void Keyword_FileScopedNamespace()
    {
        AssertHighlighter("csharp",
"""
namespace MyApp;

public class Foo { }
""",
"""
<span class="hljs-keyword">namespace</span> <span class="hljs-title">MyApp</span>;

<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Foo</span> { }
""");
    }

    [Fact]
    public void Keyword_NestedNamespace()
    {
        AssertHighlighter("csharp",
"""
namespace A.B.C
{
    public class Foo { }
}
""",
"""
<span class="hljs-keyword">namespace</span> <span class="hljs-title">A.B.C</span>
{
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Foo</span> { }
}
""");
    }

    [Fact]
    public void Keyword_Using()
    {
        AssertHighlighter("csharp",
"""
using System;
""",
"""
<span class="hljs-keyword">using</span> System;
""");
    }

    [Fact]
    public void Keyword_UsingStatic()
    {
        AssertHighlighter("csharp",
"""
using static System.Math;
""",
"""
<span class="hljs-keyword">using</span> <span class="hljs-keyword">static</span> System.Math;
""");
    }

    [Fact]
    public void Keyword_UsingAlias()
    {
        AssertHighlighter("csharp",
"""
using StringList = System.Collections.Generic.List<string>;
""",
"""
<span class="hljs-keyword">using</span> StringList = System.Collections.Generic.List&lt;<span class="hljs-built_in">string</span>&gt;;
""");
    }

    [Fact]
    public void Keyword_UsingAliasGeneric()
    {
        AssertHighlighter("csharp",
"""
using IntCallback = System.Func<int, int>;
""",
"""
<span class="hljs-keyword">using</span> IntCallback = System.Func&lt;<span class="hljs-built_in">int</span>, <span class="hljs-built_in">int</span>&gt;;
""");
    }

    [Fact]
    public void Keyword_GlobalUsing()
    {
        AssertHighlighter("csharp",
"""
global using System;
""",
"""
<span class="hljs-keyword">global</span> <span class="hljs-keyword">using</span> System;
""");
    }

    [Fact]
    public void Keyword_GlobalUsingStatic()
    {
        AssertHighlighter("csharp",
"""
global using static System.Console;
""",
"""
<span class="hljs-keyword">global</span> <span class="hljs-keyword">using</span> <span class="hljs-keyword">static</span> System.Console;
""");
    }

    [Fact]
    public void Keyword_TypeofExpr()
    {
        AssertHighlighter("csharp",
"""
var t = typeof(string);
""",
"""
<span class="hljs-keyword">var</span> t = <span class="hljs-keyword">typeof</span>(<span class="hljs-built_in">string</span>);
""");
    }

    [Fact]
    public void Keyword_NameofExpr()
    {
        AssertHighlighter("csharp",
"""
var name = nameof(MyClass);
""",
"""
<span class="hljs-keyword">var</span> name = <span class="hljs-keyword">nameof</span>(MyClass);
""");
    }

    [Fact]
    public void Keyword_SizeofExpr()
    {
        AssertHighlighter("csharp",
"""
var bytes = sizeof(int);
""",
"""
<span class="hljs-keyword">var</span> bytes = <span class="hljs-keyword">sizeof</span>(<span class="hljs-built_in">int</span>);
""");
    }

    [Fact]
    public void Keyword_Checked()
    {
        AssertHighlighter("csharp",
"""
int result = checked(x + y);
""",
"""
<span class="hljs-built_in">int</span> result = checked(x + y);
""");
    }

    [Fact]
    public void Keyword_Unchecked()
    {
        AssertHighlighter("csharp",
"""
int result = unchecked(x + y);
""",
"""
<span class="hljs-built_in">int</span> result = <span class="hljs-keyword">unchecked</span>(x + y);
""");
    }

    [Fact]
    public void Keyword_Default()
    {
        AssertHighlighter("csharp",
"""
var x = default(int);
""",
"""
<span class="hljs-keyword">var</span> x = <span class="hljs-literal">default</span>(<span class="hljs-built_in">int</span>);
""");
    }

    [Fact]
    public void Keyword_DefaultLiteral()
    {
        AssertHighlighter("csharp",
"""
int x = default;
""",
"""
<span class="hljs-built_in">int</span> x = <span class="hljs-literal">default</span>;
""");
    }

    [Fact]
    public void Modifier_Access()
    {
        AssertHighlighter("csharp",
"""
public class A { }
internal class B { }
private class C { }
protected class D { }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">A</span> { }
<span class="hljs-keyword">internal</span> <span class="hljs-keyword">class</span> <span class="hljs-title">B</span> { }
<span class="hljs-keyword">private</span> <span class="hljs-keyword">class</span> <span class="hljs-title">C</span> { }
<span class="hljs-keyword">protected</span> <span class="hljs-keyword">class</span> <span class="hljs-title">D</span> { }
""");
    }

    [Fact]
    public void Modifier_ProtectedInternal()
    {
        AssertHighlighter("csharp",
"""
protected internal class A { }
""",
"""
<span class="hljs-keyword">protected</span> <span class="hljs-keyword">internal</span> <span class="hljs-keyword">class</span> <span class="hljs-title">A</span> { }
""");
    }

    [Fact]
    public void Modifier_PrivateProtected()
    {
        AssertHighlighter("csharp",
"""
private protected class A { }
""",
"""
<span class="hljs-keyword">private</span> <span class="hljs-keyword">protected</span> <span class="hljs-keyword">class</span> <span class="hljs-title">A</span> { }
""");
    }

    [Fact]
    public void Modifier_Sealed()
    {
        AssertHighlighter("csharp",
"""
public sealed class Foo { }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">sealed</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Foo</span> { }
""");
    }

    [Fact]
    public void Modifier_Abstract()
    {
        AssertHighlighter("csharp",
"""
public abstract class Foo { public abstract void Run(); }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">abstract</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Foo</span> { <span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">abstract</span> <span class="hljs-keyword">void</span> <span class="hljs-title">Run</span>()</span>; }
""");
    }

    [Fact]
    public void Modifier_Partial()
    {
        AssertHighlighter("csharp",
"""
public partial class Foo { }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">partial</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Foo</span> { }
""");
    }

    [Fact]
    public void Modifier_StaticClass()
    {
        AssertHighlighter("csharp",
"""
public static class Helpers { }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Helpers</span> { }
""");
    }

    [Fact]
    public void Modifier_FileLocal()
    {
        AssertHighlighter("csharp",
"""
file class InternalHelper { }
""",
"""
<span class="hljs-keyword">file</span> <span class="hljs-keyword">class</span> <span class="hljs-title">InternalHelper</span> { }
""");
    }

    [Fact]
    public void Modifier_Readonly()
    {
        AssertHighlighter("csharp",
"""
public readonly struct Money { }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">readonly</span> <span class="hljs-keyword">struct</span> Money { }
""");
    }

    [Fact]
    public void Modifier_RefStruct()
    {
        AssertHighlighter("csharp",
"""
public ref struct Span2 { }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">ref</span> <span class="hljs-keyword">struct</span> Span2 { }
""");
    }

    [Fact]
    public void Modifier_RefReadonlyStruct()
    {
        AssertHighlighter("csharp",
"""
public readonly ref struct Span3 { }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">readonly</span> <span class="hljs-keyword">ref</span> <span class="hljs-keyword">struct</span> Span3 { }
""");
    }

    [Fact]
    public void Modifier_Required()
    {
        AssertHighlighter("csharp",
"""
public class User { public required string Name { get; init; } }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">User</span> { <span class="hljs-keyword">public</span> <span class="hljs-keyword">required</span> <span class="hljs-built_in">string</span> Name { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">init</span>; } }
""");
    }

    [Fact]
    public void Modifier_Unsafe()
    {
        AssertHighlighter("csharp",
"""
public unsafe class Native { }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">unsafe</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Native</span> { }
""");
    }

    [Fact]
    public void Modifier_Extern()
    {
        AssertHighlighter("csharp",
"""
public extern alias NetCore;
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">extern</span> <span class="hljs-keyword">alias</span> NetCore;
""");
    }

    [Fact]
    public void Modifier_Volatile()
    {
        AssertHighlighter("csharp",
"""
private volatile int _flag;
""",
"""
<span class="hljs-keyword">private</span> <span class="hljs-keyword">volatile</span> <span class="hljs-built_in">int</span> _flag;
""");
    }

    [Fact]
    public void Field_Simple()
    {
        AssertHighlighter("csharp",
"""
private int _count;
""",
"""
<span class="hljs-keyword">private</span> <span class="hljs-built_in">int</span> _count;
""");
    }

    [Fact]
    public void Field_WithInitializer()
    {
        AssertHighlighter("csharp",
"""
private int _count = 0;
""",
"""
<span class="hljs-keyword">private</span> <span class="hljs-built_in">int</span> _count = <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Field_StaticReadonly()
    {
        AssertHighlighter("csharp",
"""
public static readonly int MaxRetries = 3;
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> <span class="hljs-keyword">readonly</span> <span class="hljs-built_in">int</span> MaxRetries = <span class="hljs-number">3</span>;
""");
    }

    [Fact]
    public void Field_Const()
    {
        AssertHighlighter("csharp",
"""
public const string Greeting = "Hello";
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">const</span> <span class="hljs-built_in">string</span> Greeting = <span class="hljs-string">&quot;Hello&quot;</span>;
""");
    }

    [Fact]
    public void Field_Multiple()
    {
        AssertHighlighter("csharp",
"""
private int _x, _y, _z;
""",
"""
<span class="hljs-keyword">private</span> <span class="hljs-built_in">int</span> _x, _y, _z;
""");
    }

    [Fact]
    public void Property_Auto()
    {
        AssertHighlighter("csharp",
"""
public string Name { get; set; }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-built_in">string</span> Name { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">set</span>; }
""");
    }

    [Fact]
    public void Property_AutoInit()
    {
        AssertHighlighter("csharp",
"""
public string Name { get; init; }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-built_in">string</span> Name { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">init</span>; }
""");
    }

    [Fact]
    public void Property_AutoRequired()
    {
        AssertHighlighter("csharp",
"""
public required string Name { get; init; }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">required</span> <span class="hljs-built_in">string</span> Name { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">init</span>; }
""");
    }

    [Fact]
    public void Property_AutoWithDefault()
    {
        AssertHighlighter("csharp",
"""
public int Count { get; set; } = 0;
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-built_in">int</span> Count { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">set</span>; } = <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Property_ReadOnlyAuto()
    {
        AssertHighlighter("csharp",
"""
public DateTime CreatedAt { get; }
""",
"""
<span class="hljs-keyword">public</span> DateTime CreatedAt { <span class="hljs-keyword">get</span>; }
""");
    }

    [Fact]
    public void Property_PrivateSet()
    {
        AssertHighlighter("csharp",
"""
public Guid Id { get; private set; }
""",
"""
<span class="hljs-keyword">public</span> Guid Id { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">private</span> <span class="hljs-keyword">set</span>; }
""");
    }

    [Fact]
    public void Property_ExpressionBodied()
    {
        AssertHighlighter("csharp",
"""
public string FullName => $"{First} {Last}";
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-built_in">string</span> FullName =&gt; <span class="hljs-string">$&quot;<span class="hljs-subst">{First}</span> <span class="hljs-subst">{Last}</span>&quot;</span>;
""");
    }

    [Fact]
    public void Property_FullBody()
    {
        AssertHighlighter("csharp",
"""
public int Count
{
    get { return _count; }
    set { _count = value; }
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-built_in">int</span> Count
{
    <span class="hljs-keyword">get</span> { <span class="hljs-keyword">return</span> _count; }
    <span class="hljs-keyword">set</span> { _count = <span class="hljs-keyword">value</span>; }
}
""");
    }

    [Fact]
    public void Property_ExpressionGetSet()
    {
        AssertHighlighter("csharp",
"""
public int Count
{
    get => _count;
    set => _count = value;
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-built_in">int</span> Count
{
    <span class="hljs-keyword">get</span> =&gt; _count;
    <span class="hljs-keyword">set</span> =&gt; _count = <span class="hljs-keyword">value</span>;
}
""");
    }

    [Fact]
    public void Property_PartialProperty()
    {
        AssertHighlighter("csharp",
"""
public partial class Foo
{
    public partial string Bar { get; set; }
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">partial</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Foo</span>
{
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">partial</span> <span class="hljs-built_in">string</span> Bar { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">set</span>; }
}
""");
    }

    [Fact]
    public void Property_FieldKeyword()
    {
        AssertHighlighter("csharp",
"""
public int Count
{
    get => field;
    set => field = value < 0 ? 0 : value;
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-built_in">int</span> Count
{
    <span class="hljs-keyword">get</span> =&gt; <span class="hljs-keyword">field</span>;
    <span class="hljs-keyword">set</span> =&gt; <span class="hljs-keyword">field</span> = <span class="hljs-keyword">value</span> &lt; <span class="hljs-number">0</span> ? <span class="hljs-number">0</span> : <span class="hljs-keyword">value</span>;
}
""");
    }

    [Fact]
    public void Method_Simple()
    {
        AssertHighlighter("csharp",
"""
public int Add(int a, int b) { return a + b; }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-built_in">int</span> <span class="hljs-title">Add</span>(<span class="hljs-params"><span class="hljs-built_in">int</span> a, <span class="hljs-built_in">int</span> b</span>)</span> { <span class="hljs-keyword">return</span> a + b; }
""");
    }

    [Fact]
    public void Method_ExpressionBodied()
    {
        AssertHighlighter("csharp",
"""
public int Add(int a, int b) => a + b;
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-built_in">int</span> <span class="hljs-title">Add</span>(<span class="hljs-params"><span class="hljs-built_in">int</span> a, <span class="hljs-built_in">int</span> b</span>)</span> =&gt; a + b;
""");
    }

    [Fact]
    public void Method_Void()
    {
        AssertHighlighter("csharp",
"""
public void Run() { }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">void</span> <span class="hljs-title">Run</span>()</span> { }
""");
    }

    [Fact]
    public void Method_Static()
    {
        AssertHighlighter("csharp",
"""
public static int Square(int x) => x * x;
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> <span class="hljs-built_in">int</span> <span class="hljs-title">Square</span>(<span class="hljs-params"><span class="hljs-built_in">int</span> x</span>)</span> =&gt; x * x;
""");
    }

    [Fact]
    public void Method_Async()
    {
        AssertHighlighter("csharp",
"""
public async Task<int> GetAsync() => await client.GetIntAsync();
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">async</span> Task&lt;<span class="hljs-built_in">int</span>&gt; <span class="hljs-title">GetAsync</span>()</span> =&gt; <span class="hljs-keyword">await</span> client.GetIntAsync();
""");
    }

    [Fact]
    public void Method_AsyncVoid()
    {
        AssertHighlighter("csharp",
"""
private async void OnClick() { await DoWorkAsync(); }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">private</span> <span class="hljs-keyword">async</span> <span class="hljs-keyword">void</span> <span class="hljs-title">OnClick</span>()</span> { <span class="hljs-keyword">await</span> DoWorkAsync(); }
""");
    }

    [Fact]
    public void Method_AsyncTask()
    {
        AssertHighlighter("csharp",
"""
public async Task RunAsync() { await Task.Delay(100); }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">async</span> Task <span class="hljs-title">RunAsync</span>()</span> { <span class="hljs-keyword">await</span> Task.Delay(<span class="hljs-number">100</span>); }
""");
    }

    [Fact]
    public void Method_AsyncValueTask()
    {
        AssertHighlighter("csharp",
"""
public async ValueTask<int> GetAsync() => 42;
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">async</span> ValueTask&lt;<span class="hljs-built_in">int</span>&gt; <span class="hljs-title">GetAsync</span>()</span> =&gt; <span class="hljs-number">42</span>;
""");
    }

    [Fact]
    public void Method_Generic()
    {
        AssertHighlighter("csharp",
"""
public T Identity<T>(T value) => value;
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> T <span class="hljs-title">Identity</span>&lt;<span class="hljs-title">T</span>&gt;(<span class="hljs-params">T <span class="hljs-keyword">value</span></span>)</span> =&gt; <span class="hljs-keyword">value</span>;
""");
    }

    [Fact]
    public void Method_GenericMulti()
    {
        AssertHighlighter("csharp",
"""
public TOut Map<TIn, TOut>(TIn input, Func<TIn, TOut> selector) => selector(input);
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> TOut <span class="hljs-title">Map</span>&lt;<span class="hljs-title">TIn</span>, <span class="hljs-title">TOut</span>&gt;(<span class="hljs-params">TIn input, Func&lt;TIn, TOut&gt; selector</span>)</span> =&gt; selector(input);
""");
    }

    [Fact]
    public void Method_GenericConstraint()
    {
        AssertHighlighter("csharp",
"""
public T Identity<T>(T value) where T : class => value;
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> T <span class="hljs-title">Identity</span>&lt;<span class="hljs-title">T</span>&gt;(<span class="hljs-params">T <span class="hljs-keyword">value</span></span>) <span class="hljs-keyword">where</span> T : <span class="hljs-keyword">class</span></span> =&gt; <span class="hljs-keyword">value</span>;
""");
    }

    [Fact]
    public void Method_GenericConstraintNew()
    {
        AssertHighlighter("csharp",
"""
public T Create<T>() where T : new() => new T();
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> T <span class="hljs-title">Create</span>&lt;<span class="hljs-title">T</span>&gt;() <span class="hljs-keyword">where</span> T : <span class="hljs-keyword">new</span>()</span> =&gt; <span class="hljs-keyword">new</span> T();
""");
    }

    [Fact]
    public void Method_GenericConstraintStruct()
    {
        AssertHighlighter("csharp",
"""
public T? AsNullable<T>(T value) where T : struct => value;
""",
"""
<span class="hljs-keyword">public</span> T? AsNullable&lt;T&gt;(T <span class="hljs-keyword">value</span>) <span class="hljs-keyword">where</span> T : <span class="hljs-keyword">struct</span> =&gt; <span class="hljs-keyword">value</span>;
""");
    }

    [Fact]
    public void Method_GenericConstraintNotNull()
    {
        AssertHighlighter("csharp",
"""
public T Pass<T>(T value) where T : notnull => value;
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> T <span class="hljs-title">Pass</span>&lt;<span class="hljs-title">T</span>&gt;(<span class="hljs-params">T <span class="hljs-keyword">value</span></span>) <span class="hljs-keyword">where</span> T : <span class="hljs-keyword">notnull</span></span> =&gt; <span class="hljs-keyword">value</span>;
""");
    }

    [Fact]
    public void Method_GenericConstraintUnmanaged()
    {
        AssertHighlighter("csharp",
"""
public unsafe void Write<T>(T* p) where T : unmanaged { }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">unsafe</span> <span class="hljs-keyword">void</span> <span class="hljs-title">Write</span>&lt;<span class="hljs-title">T</span>&gt;(<span class="hljs-params">T* p</span>) <span class="hljs-keyword">where</span> T : <span class="hljs-keyword">unmanaged</span></span> { }
""");
    }

    [Fact]
    public void Method_GenericConstraintMultiple()
    {
        AssertHighlighter("csharp",
"""
public T Process<T>(T value) where T : IComparable<T>, IEquatable<T>, new() => value;
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> T <span class="hljs-title">Process</span>&lt;<span class="hljs-title">T</span>&gt;(<span class="hljs-params">T <span class="hljs-keyword">value</span></span>) <span class="hljs-keyword">where</span> T : IComparable&lt;T&gt;, IEquatable&lt;T&gt;, <span class="hljs-keyword">new</span>()</span> =&gt; <span class="hljs-keyword">value</span>;
""");
    }

    [Fact]
    public void Method_GenericConstraintAllowsRefStruct()
    {
        AssertHighlighter("csharp",
"""
public void Process<T>(T value) where T : allows ref struct { }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">void</span> <span class="hljs-title">Process</span>&lt;<span class="hljs-title">T</span>&gt;(<span class="hljs-params">T <span class="hljs-keyword">value</span></span>) <span class="hljs-keyword">where</span> T : <span class="hljs-keyword">allows</span> <span class="hljs-keyword">ref</span> <span class="hljs-keyword">struct</span></span> { }
""");
    }

    [Fact]
    public void Method_Extension()
    {
        AssertHighlighter("csharp",
"""
public static class StringExt
{
    public static bool IsEmpty(this string s) => s.Length == 0;
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> <span class="hljs-keyword">class</span> <span class="hljs-title">StringExt</span>
{
    <span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> <span class="hljs-built_in">bool</span> <span class="hljs-title">IsEmpty</span>(<span class="hljs-params"><span class="hljs-keyword">this</span> <span class="hljs-built_in">string</span> s</span>)</span> =&gt; s.Length == <span class="hljs-number">0</span>;
}
""");
    }

    [Fact]
    public void Method_OptionalParam()
    {
        AssertHighlighter("csharp",
"""
public void Greet(string name, string greeting = "Hello") { }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">void</span> <span class="hljs-title">Greet</span>(<span class="hljs-params"><span class="hljs-built_in">string</span> name, <span class="hljs-built_in">string</span> greeting = <span class="hljs-string">&quot;Hello&quot;</span></span>)</span> { }
""");
    }

    [Fact]
    public void Method_ParamsArray()
    {
        AssertHighlighter("csharp",
"""
public int Sum(params int[] values) => values.Sum();
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-built_in">int</span> <span class="hljs-title">Sum</span>(<span class="hljs-params"><span class="hljs-keyword">params</span> <span class="hljs-built_in">int</span>[] values</span>)</span> =&gt; values.Sum();
""");
    }

    [Fact]
    public void Method_ParamsSpan()
    {
        AssertHighlighter("csharp",
"""
public int Sum(params ReadOnlySpan<int> values) { int s = 0; foreach (var v in values) s += v; return s; }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-built_in">int</span> <span class="hljs-title">Sum</span>(<span class="hljs-params"><span class="hljs-keyword">params</span> ReadOnlySpan&lt;<span class="hljs-built_in">int</span>&gt; values</span>)</span> { <span class="hljs-built_in">int</span> s = <span class="hljs-number">0</span>; <span class="hljs-keyword">foreach</span> (<span class="hljs-keyword">var</span> v <span class="hljs-keyword">in</span> values) s += v; <span class="hljs-keyword">return</span> s; }
""");
    }

    [Fact]
    public void Method_ParamsIEnumerable()
    {
        AssertHighlighter("csharp",
"""
public int Sum(params IEnumerable<int> values) => values.Sum();
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-built_in">int</span> <span class="hljs-title">Sum</span>(<span class="hljs-params"><span class="hljs-keyword">params</span> IEnumerable&lt;<span class="hljs-built_in">int</span>&gt; values</span>)</span> =&gt; values.Sum();
""");
    }

    [Fact]
    public void Method_OutParam()
    {
        AssertHighlighter("csharp",
"""
public bool TryParse(string s, out int result) { return int.TryParse(s, out result); }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-built_in">bool</span> <span class="hljs-title">TryParse</span>(<span class="hljs-params"><span class="hljs-built_in">string</span> s, <span class="hljs-keyword">out</span> <span class="hljs-built_in">int</span> result</span>)</span> { <span class="hljs-keyword">return</span> <span class="hljs-built_in">int</span>.TryParse(s, <span class="hljs-keyword">out</span> result); }
""");
    }

    [Fact]
    public void Method_OutDeclareInline()
    {
        AssertHighlighter("csharp",
"""
if (int.TryParse(s, out var n)) Console.WriteLine(n);
""",
"""
<span class="hljs-keyword">if</span> (<span class="hljs-built_in">int</span>.TryParse(s, <span class="hljs-keyword">out</span> <span class="hljs-keyword">var</span> n)) Console.WriteLine(n);
""");
    }

    [Fact]
    public void Method_RefParam()
    {
        AssertHighlighter("csharp",
"""
public void Swap(ref int a, ref int b) { (a, b) = (b, a); }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">void</span> <span class="hljs-title">Swap</span>(<span class="hljs-params"><span class="hljs-keyword">ref</span> <span class="hljs-built_in">int</span> a, <span class="hljs-keyword">ref</span> <span class="hljs-built_in">int</span> b</span>)</span> { (a, b) = (b, a); }
""");
    }

    [Fact]
    public void Method_InParam()
    {
        AssertHighlighter("csharp",
"""
public double Length(in Vector3 v) => Math.Sqrt(v.X * v.X);
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-built_in">double</span> <span class="hljs-title">Length</span>(<span class="hljs-params"><span class="hljs-keyword">in</span> Vector3 v</span>)</span> =&gt; Math.Sqrt(v.X * v.X);
""");
    }

    [Fact]
    public void Method_RefReadonlyParam()
    {
        AssertHighlighter("csharp",
"""
public double Length(ref readonly Vector3 v) => 0;
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-built_in">double</span> <span class="hljs-title">Length</span>(<span class="hljs-params"><span class="hljs-keyword">ref</span> <span class="hljs-keyword">readonly</span> Vector3 v</span>)</span> =&gt; <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Method_ScopedRef()
    {
        AssertHighlighter("csharp",
"""
public void Use(scoped ref int x) { x = 0; }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">void</span> <span class="hljs-title">Use</span>(<span class="hljs-params"><span class="hljs-keyword">scoped</span> <span class="hljs-keyword">ref</span> <span class="hljs-built_in">int</span> x</span>)</span> { x = <span class="hljs-number">0</span>; }
""");
    }

    [Fact]
    public void Method_LocalFunction()
    {
        AssertHighlighter("csharp",
"""
public void Run()
{
    static int Square(int x) => x * x;
    Console.WriteLine(Square(5));
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">void</span> <span class="hljs-title">Run</span>()</span>
{
    <span class="hljs-function"><span class="hljs-keyword">static</span> <span class="hljs-built_in">int</span> <span class="hljs-title">Square</span>(<span class="hljs-params"><span class="hljs-built_in">int</span> x</span>)</span> =&gt; x * x;
    Console.WriteLine(Square(<span class="hljs-number">5</span>));
}
""");
    }

    [Fact]
    public void Constructor_Default()
    {
        AssertHighlighter("csharp",
"""
public class Foo { public Foo() { } }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Foo</span> { <span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-title">Foo</span>()</span> { } }
""");
    }

    [Fact]
    public void Constructor_WithArgs()
    {
        AssertHighlighter("csharp",
"""
public class User
{
    public User(string name) { Name = name; }
    public string Name { get; }
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">User</span>
{
    <span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-title">User</span>(<span class="hljs-params"><span class="hljs-built_in">string</span> name</span>)</span> { Name = name; }
    <span class="hljs-keyword">public</span> <span class="hljs-built_in">string</span> Name { <span class="hljs-keyword">get</span>; }
}
""");
    }

    [Fact]
    public void Constructor_BaseCall()
    {
        AssertHighlighter("csharp",
"""
public class Manager : Employee
{
    public Manager(string name) : base(name) { }
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Manager</span> : <span class="hljs-title">Employee</span>
{
    <span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-title">Manager</span>(<span class="hljs-params"><span class="hljs-built_in">string</span> name</span>) : <span class="hljs-title">base</span>(<span class="hljs-params">name</span>)</span> { }
}
""");
    }

    [Fact]
    public void Constructor_ThisCall()
    {
        AssertHighlighter("csharp",
"""
public class Foo
{
    public Foo() : this(0) { }
    public Foo(int x) { }
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Foo</span>
{
    <span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-title">Foo</span>() : <span class="hljs-title">this</span>(<span class="hljs-params"><span class="hljs-number">0</span></span>)</span> { }
    <span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-title">Foo</span>(<span class="hljs-params"><span class="hljs-built_in">int</span> x</span>)</span> { }
}
""");
    }

    [Fact]
    public void Constructor_Static()
    {
        AssertHighlighter("csharp",
"""
public class Cache
{
    static Cache() { Items = new(); }
    public static List<int> Items { get; }
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Cache</span>
{
    <span class="hljs-function"><span class="hljs-keyword">static</span> <span class="hljs-title">Cache</span>()</span> { Items = <span class="hljs-keyword">new</span>(); }
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> List&lt;<span class="hljs-built_in">int</span>&gt; Items { <span class="hljs-keyword">get</span>; }
}
""");
    }

    [Fact]
    public void Constructor_Primary()
    {
        AssertHighlighter("csharp",
"""
public class User(string name, int age) { public string Display => $"{name} ({age})"; }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">User</span>(<span class="hljs-params"><span class="hljs-built_in">string</span> name, <span class="hljs-built_in">int</span> age</span>)</span> { <span class="hljs-keyword">public</span> <span class="hljs-built_in">string</span> Display =&gt; <span class="hljs-string">$&quot;<span class="hljs-subst">{name}</span> (<span class="hljs-subst">{age}</span>)&quot;</span>; }
""");
    }

    [Fact]
    public void Constructor_PrimaryStruct()
    {
        AssertHighlighter("csharp",
"""
public readonly struct Point(double x, double y)
{
    public double X => x;
    public double Y => y;
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">readonly</span> <span class="hljs-keyword">struct</span> <span class="hljs-title">Point</span>(<span class="hljs-params"><span class="hljs-built_in">double</span> x, <span class="hljs-built_in">double</span> y</span>)</span>
{
    <span class="hljs-keyword">public</span> <span class="hljs-built_in">double</span> X =&gt; x;
    <span class="hljs-keyword">public</span> <span class="hljs-built_in">double</span> Y =&gt; y;
}
""");
    }

    [Fact]
    public void Constructor_PrimaryWithBase()
    {
        AssertHighlighter("csharp",
"""
public class Manager(string name, int level) : Employee(name)
{
    public int Level => level;
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Manager</span>(<span class="hljs-params"><span class="hljs-built_in">string</span> name, <span class="hljs-built_in">int</span> level</span>) : <span class="hljs-title">Employee</span>(<span class="hljs-params">name</span>)</span>
{
    <span class="hljs-keyword">public</span> <span class="hljs-built_in">int</span> Level =&gt; level;
}
""");
    }

    [Fact]
    public void Constructor_PartialCtor()
    {
        AssertHighlighter("csharp",
"""
public partial class Foo
{
    public partial Foo(int x);
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">partial</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Foo</span>
{
    <span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">partial</span> <span class="hljs-title">Foo</span>(<span class="hljs-params"><span class="hljs-built_in">int</span> x</span>)</span>;
}
""");
    }

    [Fact]
    public void Operator_Overload()
    {
        AssertHighlighter("csharp",
"""
public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount, a.Currency);
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> Money <span class="hljs-keyword">operator</span> +(Money a, Money b) =&gt; <span class="hljs-keyword">new</span>(a.Amount + b.Amount, a.Currency);
""");
    }

    [Fact]
    public void Operator_Unary()
    {
        AssertHighlighter("csharp",
"""
public static Money operator -(Money m) => new(-m.Amount, m.Currency);
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> Money <span class="hljs-keyword">operator</span> -(Money m) =&gt; <span class="hljs-keyword">new</span>(-m.Amount, m.Currency);
""");
    }

    [Fact]
    public void Operator_Comparison()
    {
        AssertHighlighter("csharp",
"""
public static bool operator <(Money a, Money b) => a.Amount < b.Amount;
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> <span class="hljs-built_in">bool</span> <span class="hljs-keyword">operator</span> &lt;(Money a, Money b) =&gt; a.Amount &lt; b.Amount;
""");
    }

    [Fact]
    public void Operator_EqualsHash()
    {
        AssertHighlighter("csharp",
"""
public override bool Equals(object? obj) => obj is Foo f && _x == f._x;
public override int GetHashCode() => _x.GetHashCode();
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">override</span> <span class="hljs-built_in">bool</span> <span class="hljs-title">Equals</span>(<span class="hljs-params"><span class="hljs-built_in">object</span>? obj</span>)</span> =&gt; obj <span class="hljs-keyword">is</span> Foo f &amp;&amp; _x == f._x;
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">override</span> <span class="hljs-built_in">int</span> <span class="hljs-title">GetHashCode</span>()</span> =&gt; _x.GetHashCode();
""");
    }

    [Fact]
    public void Operator_ImplicitConv()
    {
        AssertHighlighter("csharp",
"""
public static implicit operator int(Money m) => (int)m.Amount;
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> <span class="hljs-keyword">implicit</span> <span class="hljs-keyword">operator</span> <span class="hljs-title">int</span>(<span class="hljs-params">Money m</span>)</span> =&gt; (<span class="hljs-built_in">int</span>)m.Amount;
""");
    }

    [Fact]
    public void Operator_ExplicitConv()
    {
        AssertHighlighter("csharp",
"""
public static explicit operator decimal(Money m) => m.Amount;
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> <span class="hljs-keyword">explicit</span> <span class="hljs-keyword">operator</span> <span class="hljs-title">decimal</span>(<span class="hljs-params">Money m</span>)</span> =&gt; m.Amount;
""");
    }

    [Fact]
    public void Operator_CheckedExplicit()
    {
        AssertHighlighter("csharp",
"""
public static explicit operator checked int(BigNumber n) => checked((int)n.Value);
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> <span class="hljs-keyword">explicit</span> <span class="hljs-keyword">operator</span> checked <span class="hljs-title">int</span>(<span class="hljs-params">BigNumber n</span>)</span> =&gt; checked((<span class="hljs-built_in">int</span>)n.Value);
""");
    }

    [Fact]
    public void Operator_StaticAbstract()
    {
        AssertHighlighter("csharp",
"""
public interface IAdditive<T> where T : IAdditive<T>
{
    static abstract T operator +(T a, T b);
    static abstract T Zero { get; }
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">interface</span> <span class="hljs-title">IAdditive</span>&lt;<span class="hljs-title">T</span>&gt; <span class="hljs-keyword">where</span> <span class="hljs-title">T</span> : <span class="hljs-title">IAdditive</span>&lt;<span class="hljs-title">T</span>&gt;
{
    <span class="hljs-keyword">static</span> <span class="hljs-keyword">abstract</span> T <span class="hljs-keyword">operator</span> +(T a, T b);
    <span class="hljs-keyword">static</span> <span class="hljs-keyword">abstract</span> T Zero { <span class="hljs-keyword">get</span>; }
}
""");
    }

    [Fact]
    public void Indexer_Simple()
    {
        AssertHighlighter("csharp",
"""
public class Vec { public int this[int i] => 0; }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Vec</span> { <span class="hljs-keyword">public</span> <span class="hljs-built_in">int</span> <span class="hljs-keyword">this</span>[<span class="hljs-built_in">int</span> i] =&gt; <span class="hljs-number">0</span>; }
""");
    }

    [Fact]
    public void Indexer_Multidim()
    {
        AssertHighlighter("csharp",
"""
public class Grid { public int this[int x, int y] { get => 0; set { } } }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Grid</span> { <span class="hljs-keyword">public</span> <span class="hljs-built_in">int</span> <span class="hljs-keyword">this</span>[<span class="hljs-built_in">int</span> x, <span class="hljs-built_in">int</span> y] { <span class="hljs-keyword">get</span> =&gt; <span class="hljs-number">0</span>; <span class="hljs-keyword">set</span> { } } }
""");
    }

    [Fact]
    public void Indexer_PartialIndexer()
    {
        AssertHighlighter("csharp",
"""
public partial class Foo
{
    public partial int this[int i] { get; set; }
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">partial</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Foo</span>
{
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">partial</span> <span class="hljs-built_in">int</span> <span class="hljs-keyword">this</span>[<span class="hljs-built_in">int</span> i] { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">set</span>; }
}
""");
    }

    [Fact]
    public void Event_Field()
    {
        AssertHighlighter("csharp",
"""
public event EventHandler? Clicked;
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">event</span> EventHandler? Clicked;
""");
    }

    [Fact]
    public void Event_AddRemove()
    {
        AssertHighlighter("csharp",
"""
public event EventHandler Clicked
{
    add { _h += value; }
    remove { _h -= value; }
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">event</span> EventHandler Clicked
{
    <span class="hljs-keyword">add</span> { _h += <span class="hljs-keyword">value</span>; }
    <span class="hljs-keyword">remove</span> { _h -= <span class="hljs-keyword">value</span>; }
}
""");
    }

    [Fact]
    public void Event_GenericArgs()
    {
        AssertHighlighter("csharp",
"""
public event EventHandler<UserChangedEventArgs>? UserChanged;
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">event</span> EventHandler&lt;UserChangedEventArgs&gt;? UserChanged;
""");
    }

    [Fact]
    public void Delegate_Simple()
    {
        AssertHighlighter("csharp",
"""
public delegate int Operation(int a, int b);
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-built_in">delegate</span> <span class="hljs-built_in">int</span> <span class="hljs-title">Operation</span>(<span class="hljs-params"><span class="hljs-built_in">int</span> a, <span class="hljs-built_in">int</span> b</span>)</span>;
""");
    }

    [Fact]
    public void Delegate_Generic()
    {
        AssertHighlighter("csharp",
"""
public delegate TResult Selector<T, TResult>(T input);
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-built_in">delegate</span> TResult <span class="hljs-title">Selector</span>&lt;<span class="hljs-title">T</span>, <span class="hljs-title">TResult</span>&gt;(<span class="hljs-params">T input</span>)</span>;
""");
    }

    [Fact]
    public void Attribute_Simple()
    {
        AssertHighlighter("csharp",
"""
[Obsolete]
public void Old() { }
""",
"""
[<span class="hljs-meta">Obsolete</span>]
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">void</span> <span class="hljs-title">Old</span>()</span> { }
""");
    }

    [Fact]
    public void Attribute_WithArgs()
    {
        AssertHighlighter("csharp",
"""
[Obsolete("Use NewMethod instead", true)]
public void Old() { }
""",
"""
[<span class="hljs-meta">Obsolete(<span class="hljs-string">&quot;Use NewMethod instead&quot;</span>, true)</span>]
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">void</span> <span class="hljs-title">Old</span>()</span> { }
""");
    }

    [Fact]
    public void Attribute_Multiple()
    {
        AssertHighlighter("csharp",
"""
[Serializable]
[DebuggerDisplay("{Name}")]
public class User { }
""",
"""
[<span class="hljs-meta">Serializable</span>]
[<span class="hljs-meta">DebuggerDisplay(<span class="hljs-string">&quot;{Name}&quot;</span>)</span>]
<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">User</span> { }
""");
    }

    [Fact]
    public void Attribute_TargetAssembly()
    {
        AssertHighlighter("csharp",
"""
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
""",
"""
[<span class="hljs-meta">assembly: System.Reflection.AssemblyVersion(<span class="hljs-string">&quot;1.0.0.0&quot;</span>)</span>]
""");
    }

    [Fact]
    public void Attribute_TargetField()
    {
        AssertHighlighter("csharp",
"""
[field: NonSerialized]
public int Value { get; set; }
""",
"""
[<span class="hljs-meta">field: NonSerialized</span>]
<span class="hljs-keyword">public</span> <span class="hljs-built_in">int</span> Value { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">set</span>; }
""");
    }

    [Fact]
    public void Attribute_Generic()
    {
        AssertHighlighter("csharp",
"""
public class TypeOfAttribute<T> : Attribute { }
[TypeOf<string>]
public class Foo { }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">TypeOfAttribute</span>&lt;<span class="hljs-title">T</span>&gt; : <span class="hljs-title">Attribute</span> { }
[<span class="hljs-meta">TypeOf&lt;string&gt;</span>]
<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Foo</span> { }
""");
    }

    [Fact]
    public void Attribute_Experimental()
    {
        AssertHighlighter("csharp",
"""
[Experimental("MyApp001")]
public class FeatureFlagged { }
""",
"""
[<span class="hljs-meta">Experimental(<span class="hljs-string">&quot;MyApp001&quot;</span>)</span>]
<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">FeatureFlagged</span> { }
""");
    }

    [Fact]
    public void String_Simple()
    {
        AssertHighlighter("csharp",
"""
var s = "hello";
""",
"""
<span class="hljs-keyword">var</span> s = <span class="hljs-string">&quot;hello&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeQuote()
    {
        AssertHighlighter("csharp",
"""
var s = "She said \"hi\".";
""",
"""
<span class="hljs-keyword">var</span> s = <span class="hljs-string">&quot;She said \&quot;hi\&quot;.&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeBackslash()
    {
        AssertHighlighter("csharp",
"""
var s = "a\\b";
""",
"""
<span class="hljs-keyword">var</span> s = <span class="hljs-string">&quot;a\\b&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeNewline()
    {
        AssertHighlighter("csharp",
"""
var s = "line1\nline2";
""",
"""
<span class="hljs-keyword">var</span> s = <span class="hljs-string">&quot;line1\nline2&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeUnicode()
    {
        AssertHighlighter("csharp",
"""
var s = "\u0041";
""",
"""
<span class="hljs-keyword">var</span> s = <span class="hljs-string">&quot;\u0041&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeEscChar()
    {
        AssertHighlighter("csharp",
"""
var s = "\e[31mred\e[0m";
""",
"""
<span class="hljs-keyword">var</span> s = <span class="hljs-string">&quot;\e[31mred\e[0m&quot;</span>;
""");
    }

    [Fact]
    public void String_Verbatim()
    {
        AssertHighlighter("csharp",
"""
var path = @"C:\Users\alice";
""",
"""
<span class="hljs-keyword">var</span> path = <span class="hljs-string">@&quot;C:\Users\alice&quot;</span>;
""");
    }

    [Fact]
    public void String_VerbatimMultiLine()
    {
        AssertHighlighter("csharp",
"""
var sql = @"SELECT *
          FROM users";
""",
"""
<span class="hljs-keyword">var</span> sql = <span class="hljs-string">@&quot;SELECT *
          FROM users&quot;</span>;
""");
    }

    [Fact]
    public void String_Interpolation()
    {
        AssertHighlighter("csharp",
"""
var msg = $"Hello {name}";
""",
"""
<span class="hljs-keyword">var</span> msg = <span class="hljs-string">$&quot;Hello <span class="hljs-subst">{name}</span>&quot;</span>;
""");
    }

    [Fact]
    public void String_InterpolationExpr()
    {
        AssertHighlighter("csharp",
"""
var msg = $"Total: {price * quantity:C2}";
""",
"""
<span class="hljs-keyword">var</span> msg = <span class="hljs-string">$&quot;Total: <span class="hljs-subst">{price * quantity:C2}</span>&quot;</span>;
""");
    }

    [Fact]
    public void String_InterpolationVerbatim()
    {
        AssertHighlighter("csharp",
"""
var msg = $@"Path: {dir}\file.txt";
""",
"""
<span class="hljs-keyword">var</span> msg = <span class="hljs-string">$@&quot;Path: <span class="hljs-subst">{dir}</span>\file.txt&quot;</span>;
""");
    }

    [Fact]
    public void String_InterpolationVerbatimReversed()
    {
        AssertHighlighter("csharp",
"""
var msg = @$"Path: {dir}\file.txt";
""",
"""
<span class="hljs-keyword">var</span> msg = @<span class="hljs-string">$&quot;Path: <span class="hljs-subst">{dir}</span>\file.txt&quot;</span>;
""");
    }

    [Fact]
    public void String_Utf8Literal()
    {
        AssertHighlighter("csharp",
"""
ReadOnlySpan<byte> utf8 = "Hello"u8;
""",
"""
ReadOnlySpan&lt;<span class="hljs-built_in">byte</span>&gt; utf8 = <span class="hljs-string">&quot;Hello&quot;</span>u8;
""");
    }

    [Fact]
    public void String_Utf8VarLiteral()
    {
        AssertHighlighter("csharp",
"""
var bytes = "Hello"u8;
""",
"""
<span class="hljs-keyword">var</span> bytes = <span class="hljs-string">&quot;Hello&quot;</span>u8;
""");
    }

    [Fact]
    public void String_RawSingleLine()
    {
        AssertHighlighter("csharp",
""""
var s = """no escape needed for " or \""";
"""",
"""
<span class="hljs-keyword">var</span> s = <span class="hljs-string">&quot;&quot;&quot;no escape needed for &quot; or \&quot;&quot;&quot;</span>;
""");
    }

    [Fact]
    public void String_RawMultiLine()
    {
        AssertHighlighter("csharp",
""""
var s = """
    line one
    line two
    """;
"""",
"""
<span class="hljs-keyword">var</span> s = <span class="hljs-string">&quot;&quot;&quot;
    line one
    line two
    &quot;&quot;&quot;</span>;
""");
    }

    [Fact]
    public void String_RawInterpolation()
    {
        AssertHighlighter("csharp",
""""
var json = $"""
    {
      "name": "{name}"
    }
    """;
"""",
"""
<span class="hljs-keyword">var</span> json = <span class="hljs-string">$&quot;&quot;&quot;
    <span class="hljs-subst">{
      <span class="hljs-string">&quot;name&quot;</span>: <span class="hljs-string">&quot;{name}&quot;</span>
    }</span>
    &quot;&quot;&quot;</span>;
""");
    }

    [Fact]
    public void String_RawInterpolationDouble()
    {
        AssertHighlighter("csharp",
""""
var s = $$"""
    { "name": "{{name}}" }
    """;
"""",
"""
<span class="hljs-keyword">var</span> s = <span class="hljs-string">$$&quot;&quot;&quot;
    { &quot;name&quot;: &quot;<span class="hljs-subst">{{name}}</span>&quot; }
    &quot;&quot;&quot;</span>;
""");
    }

    [Fact]
    public void String_RawInterpolationSimple()
    {
        AssertHighlighter("csharp",
""""
var x = $"""a{item}b""";
"""",
"""
<span class="hljs-keyword">var</span> x = <span class="hljs-string">$&quot;&quot;&quot;a<span class="hljs-subst">{item}</span>b&quot;&quot;&quot;</span>;
""");
    }

    [Fact]
    public void String_RawInterpolationDoubleSimple()
    {
        AssertHighlighter("csharp",
""""
var x = $$"""a{{item}}b{not interpolation}""";
"""",
"""
<span class="hljs-keyword">var</span> x = <span class="hljs-string">$$&quot;&quot;&quot;a<span class="hljs-subst">{{item}}</span>b{not interpolation}&quot;&quot;&quot;</span>;
""");
    }

    [Fact]
    public void String_RawInterpolationExtraQuotes()
    {
        AssertHighlighter("csharp",
""""""
var x = $""""a{item}b"""";
"""""",
"""
<span class="hljs-keyword">var</span> x = <span class="hljs-string">$&quot;&quot;&quot;&quot;a<span class="hljs-subst">{item}</span>b&quot;&quot;&quot;&quot;</span>;
""");
    }

    [Fact]
    public void String_CharLiteral()
    {
        AssertHighlighter("csharp",
"""
var c = 'A';
""",
"""
<span class="hljs-keyword">var</span> c = <span class="hljs-string">&#x27;A&#x27;</span>;
""");
    }

    [Fact]
    public void String_CharEscape()
    {
        AssertHighlighter("csharp",
"""
var nl = '\n';
""",
"""
<span class="hljs-keyword">var</span> nl = <span class="hljs-string">&#x27;\n&#x27;</span>;
""");
    }

    [Fact]
    public void Number_Integer()
    {
        AssertHighlighter("csharp",
"""
var n = 42;
""",
"""
<span class="hljs-keyword">var</span> n = <span class="hljs-number">42</span>;
""");
    }

    [Fact]
    public void Number_Long()
    {
        AssertHighlighter("csharp",
"""
var n = 42L;
""",
"""
<span class="hljs-keyword">var</span> n = <span class="hljs-number">42L</span>;
""");
    }

    [Fact]
    public void Number_Uint()
    {
        AssertHighlighter("csharp",
"""
var n = 42U;
""",
"""
<span class="hljs-keyword">var</span> n = <span class="hljs-number">42U</span>;
""");
    }

    [Fact]
    public void Number_Ulong()
    {
        AssertHighlighter("csharp",
"""
var n = 42UL;
""",
"""
<span class="hljs-keyword">var</span> n = <span class="hljs-number">42U</span>L;
""");
    }

    [Fact]
    public void Number_Float()
    {
        AssertHighlighter("csharp",
"""
var n = 3.14f;
""",
"""
<span class="hljs-keyword">var</span> n = <span class="hljs-number">3.14f</span>;
""");
    }

    [Fact]
    public void Number_Double()
    {
        AssertHighlighter("csharp",
"""
var n = 3.14;
""",
"""
<span class="hljs-keyword">var</span> n = <span class="hljs-number">3.14</span>;
""");
    }

    [Fact]
    public void Number_Decimal()
    {
        AssertHighlighter("csharp",
"""
var n = 3.14m;
""",
"""
<span class="hljs-keyword">var</span> n = <span class="hljs-number">3.14</span>m;
""");
    }

    [Fact]
    public void Number_Hex()
    {
        AssertHighlighter("csharp",
"""
var n = 0xDEADBEEF;
""",
"""
<span class="hljs-keyword">var</span> n = <span class="hljs-number">0xDEADBEEF</span>;
""");
    }

    [Fact]
    public void Number_Binary()
    {
        AssertHighlighter("csharp",
"""
var n = 0b1010_1100;
""",
"""
<span class="hljs-keyword">var</span> n = <span class="hljs-number">0b1010</span>_1100;
""");
    }

    [Fact]
    public void Number_DigitSeparator()
    {
        AssertHighlighter("csharp",
"""
var n = 1_000_000;
""",
"""
<span class="hljs-keyword">var</span> n = <span class="hljs-number">1</span>_000_000;
""");
    }

    [Fact]
    public void Number_Exponent()
    {
        AssertHighlighter("csharp",
"""
var n = 1.5e10;
""",
"""
<span class="hljs-keyword">var</span> n = <span class="hljs-number">1.5e10</span>;
""");
    }

    [Fact]
    public void Number_Negative()
    {
        AssertHighlighter("csharp",
"""
var n = -42;
""",
"""
<span class="hljs-keyword">var</span> n = <span class="hljs-number">-42</span>;
""");
    }

    [Fact]
    public void Pattern_IsType()
    {
        AssertHighlighter("csharp",
"""
if (obj is string s) Console.WriteLine(s);
""",
"""
<span class="hljs-keyword">if</span> (obj <span class="hljs-keyword">is</span> <span class="hljs-built_in">string</span> s) Console.WriteLine(s);
""");
    }

    [Fact]
    public void Pattern_IsConstant()
    {
        AssertHighlighter("csharp",
"""
if (x is 0) return;
""",
"""
<span class="hljs-keyword">if</span> (x <span class="hljs-keyword">is</span> <span class="hljs-number">0</span>) <span class="hljs-keyword">return</span>;
""");
    }

    [Fact]
    public void Pattern_IsNull()
    {
        AssertHighlighter("csharp",
"""
if (s is null) throw new ArgumentNullException();
""",
"""
<span class="hljs-keyword">if</span> (s <span class="hljs-keyword">is</span> <span class="hljs-literal">null</span>) <span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> ArgumentNullException();
""");
    }

    [Fact]
    public void Pattern_IsNotNull()
    {
        AssertHighlighter("csharp",
"""
if (s is not null) Console.WriteLine(s);
""",
"""
<span class="hljs-keyword">if</span> (s <span class="hljs-keyword">is</span> <span class="hljs-keyword">not</span> <span class="hljs-literal">null</span>) Console.WriteLine(s);
""");
    }

    [Fact]
    public void Pattern_IsRelational()
    {
        AssertHighlighter("csharp",
"""
if (age is >= 18 and < 65) Console.WriteLine("working age");
""",
"""
<span class="hljs-keyword">if</span> (age <span class="hljs-keyword">is</span> &gt;= <span class="hljs-number">18</span> <span class="hljs-keyword">and</span> &lt; <span class="hljs-number">65</span>) Console.WriteLine(<span class="hljs-string">&quot;working age&quot;</span>);
""");
    }

    [Fact]
    public void Pattern_IsLogicalOr()
    {
        AssertHighlighter("csharp",
"""
if (status is "open" or "pending") Process();
""",
"""
<span class="hljs-keyword">if</span> (status <span class="hljs-keyword">is</span> <span class="hljs-string">&quot;open&quot;</span> <span class="hljs-keyword">or</span> <span class="hljs-string">&quot;pending&quot;</span>) Process();
""");
    }

    [Fact]
    public void Pattern_IsProperty()
    {
        AssertHighlighter("csharp",
"""
if (user is { IsActive: true, Age: > 18 }) Allow();
""",
"""
<span class="hljs-keyword">if</span> (user <span class="hljs-keyword">is</span> { IsActive: <span class="hljs-literal">true</span>, Age: &gt; <span class="hljs-number">18</span> }) Allow();
""");
    }

    [Fact]
    public void Pattern_IsPositional()
    {
        AssertHighlighter("csharp",
"""
if (point is (0, 0)) Console.WriteLine("origin");
""",
"""
<span class="hljs-keyword">if</span> (<span class="hljs-function">point <span class="hljs-title">is</span> (<span class="hljs-params"><span class="hljs-number">0</span>, <span class="hljs-number">0</span></span>)) Console.<span class="hljs-title">WriteLine</span>(<span class="hljs-params"><span class="hljs-string">&quot;origin&quot;</span></span>)</span>;
""");
    }

    [Fact]
    public void Pattern_IsList()
    {
        AssertHighlighter("csharp",
"""
if (arr is [1, 2, 3]) Match();
""",
"""
<span class="hljs-keyword">if</span> (arr <span class="hljs-keyword">is</span> [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>]) Match();
""");
    }

    [Fact]
    public void Pattern_IsListSlice()
    {
        AssertHighlighter("csharp",
"""
if (arr is [1, .., 9]) Match();
""",
"""
<span class="hljs-keyword">if</span> (arr <span class="hljs-keyword">is</span> [<span class="hljs-number">1</span>, .., <span class="hljs-number">9</span>]) Match();
""");
    }

    [Fact]
    public void Pattern_IsListVarSlice()
    {
        AssertHighlighter("csharp",
"""
if (arr is [var first, .., var last]) Use(first, last);
""",
"""
<span class="hljs-keyword">if</span> (arr <span class="hljs-keyword">is</span> [<span class="hljs-keyword">var</span> first, .., <span class="hljs-keyword">var</span> last]) Use(first, last);
""");
    }

    [Fact]
    public void Pattern_IsVar()
    {
        AssertHighlighter("csharp",
"""
if (Compute() is var result) Console.WriteLine(result);
""",
"""
<span class="hljs-keyword">if</span> (Compute() <span class="hljs-keyword">is</span> <span class="hljs-keyword">var</span> result) Console.WriteLine(result);
""");
    }

    [Fact]
    public void Pattern_IsDiscard()
    {
        AssertHighlighter("csharp",
"""
if (obj is _) Match();
""",
"""
<span class="hljs-keyword">if</span> (obj <span class="hljs-keyword">is</span> _) Match();
""");
    }

    [Fact]
    public void Pattern_IsCombined()
    {
        AssertHighlighter("csharp",
"""
if (obj is User { Age: > 18 } u and not null) Allow(u);
""",
"""
<span class="hljs-keyword">if</span> (obj <span class="hljs-keyword">is</span> User { Age: &gt; <span class="hljs-number">18</span> } u <span class="hljs-keyword">and</span> <span class="hljs-keyword">not</span> <span class="hljs-literal">null</span>) Allow(u);
""");
    }

    [Fact]
    public void SwitchStmt_Classic()
    {
        AssertHighlighter("csharp",
"""
switch (x)
{
    case 1: Console.WriteLine("one"); break;
    case 2:
    case 3: Console.WriteLine("two or three"); break;
    default: Console.WriteLine("other"); break;
}
""",
"""
<span class="hljs-keyword">switch</span> (x)
{
    <span class="hljs-keyword">case</span> <span class="hljs-number">1</span>: Console.WriteLine(<span class="hljs-string">&quot;one&quot;</span>); <span class="hljs-keyword">break</span>;
    <span class="hljs-keyword">case</span> <span class="hljs-number">2</span>:
    <span class="hljs-keyword">case</span> <span class="hljs-number">3</span>: Console.WriteLine(<span class="hljs-string">&quot;two or three&quot;</span>); <span class="hljs-keyword">break</span>;
    <span class="hljs-literal">default</span>: Console.WriteLine(<span class="hljs-string">&quot;other&quot;</span>); <span class="hljs-keyword">break</span>;
}
""");
    }

    [Fact]
    public void SwitchStmt_WithPatterns()
    {
        AssertHighlighter("csharp",
"""
switch (obj)
{
    case string s: Console.WriteLine($"string: {s}"); break;
    case int n when n > 0: Console.WriteLine($"positive int"); break;
    case null: Console.WriteLine("null"); break;
    default: Console.WriteLine("other"); break;
}
""",
"""
<span class="hljs-keyword">switch</span> (obj)
{
    <span class="hljs-keyword">case</span> <span class="hljs-built_in">string</span> s: Console.WriteLine(<span class="hljs-string">$&quot;string: <span class="hljs-subst">{s}</span>&quot;</span>); <span class="hljs-keyword">break</span>;
    <span class="hljs-keyword">case</span> <span class="hljs-built_in">int</span> n <span class="hljs-keyword">when</span> n &gt; <span class="hljs-number">0</span>: Console.WriteLine(<span class="hljs-string">$&quot;positive int&quot;</span>); <span class="hljs-keyword">break</span>;
    <span class="hljs-keyword">case</span> <span class="hljs-literal">null</span>: Console.WriteLine(<span class="hljs-string">&quot;null&quot;</span>); <span class="hljs-keyword">break</span>;
    <span class="hljs-literal">default</span>: Console.WriteLine(<span class="hljs-string">&quot;other&quot;</span>); <span class="hljs-keyword">break</span>;
}
""");
    }

    [Fact]
    public void SwitchExpr_Type()
    {
        AssertHighlighter("csharp",
"""
var name = shape switch { Circle c => "circle", Square s => "square", _ => "?" };
""",
"""
<span class="hljs-keyword">var</span> name = shape <span class="hljs-keyword">switch</span> { Circle c =&gt; <span class="hljs-string">&quot;circle&quot;</span>, Square s =&gt; <span class="hljs-string">&quot;square&quot;</span>, _ =&gt; <span class="hljs-string">&quot;?&quot;</span> };
""");
    }

    [Fact]
    public void SwitchExpr_Constant()
    {
        AssertHighlighter("csharp",
"""
var msg = status switch { 0 => "ok", 1 => "warn", _ => "error" };
""",
"""
<span class="hljs-keyword">var</span> msg = status <span class="hljs-keyword">switch</span> { <span class="hljs-number">0</span> =&gt; <span class="hljs-string">&quot;ok&quot;</span>, <span class="hljs-number">1</span> =&gt; <span class="hljs-string">&quot;warn&quot;</span>, _ =&gt; <span class="hljs-string">&quot;error&quot;</span> };
""");
    }

    [Fact]
    public void SwitchExpr_PropertyPattern()
    {
        AssertHighlighter("csharp",
"""
var price = order switch
{
    { IsExpress: true, Weight: < 5 } => 9.99m,
    { IsExpress: true }              => 19.99m,
    _                                => 4.99m
};
""",
"""
<span class="hljs-keyword">var</span> price = order <span class="hljs-keyword">switch</span>
{
    { IsExpress: <span class="hljs-literal">true</span>, Weight: &lt; <span class="hljs-number">5</span> } =&gt; <span class="hljs-number">9.99</span>m,
    { IsExpress: <span class="hljs-literal">true</span> }              =&gt; <span class="hljs-number">19.99</span>m,
    _                                =&gt; <span class="hljs-number">4.99</span>m
};
""");
    }

    [Fact]
    public void SwitchExpr_TuplePattern()
    {
        AssertHighlighter("csharp",
"""
var quadrant = (x, y) switch
{
    (> 0, > 0) => "Q1",
    (< 0, > 0) => "Q2",
    (< 0, < 0) => "Q3",
    (> 0, < 0) => "Q4",
    _          => "axis"
};
""",
"""
<span class="hljs-keyword">var</span> quadrant = (x, y) <span class="hljs-keyword">switch</span>
{
    (&gt; <span class="hljs-number">0</span>, &gt; <span class="hljs-number">0</span>) =&gt; <span class="hljs-string">&quot;Q1&quot;</span>,
    (&lt; <span class="hljs-number">0</span>, &gt; <span class="hljs-number">0</span>) =&gt; <span class="hljs-string">&quot;Q2&quot;</span>,
    (&lt; <span class="hljs-number">0</span>, &lt; <span class="hljs-number">0</span>) =&gt; <span class="hljs-string">&quot;Q3&quot;</span>,
    (&gt; <span class="hljs-number">0</span>, &lt; <span class="hljs-number">0</span>) =&gt; <span class="hljs-string">&quot;Q4&quot;</span>,
    _          =&gt; <span class="hljs-string">&quot;axis&quot;</span>
};
""");
    }

    [Fact]
    public void SwitchExpr_ListPattern()
    {
        AssertHighlighter("csharp",
"""
var kind = arr switch
{
    []          => "empty",
    [_]         => "single",
    [_, _]      => "pair",
    [_, ..]     => "many"
};
""",
"""
<span class="hljs-keyword">var</span> kind = arr <span class="hljs-keyword">switch</span>
{
    []          =&gt; <span class="hljs-string">&quot;empty&quot;</span>,
    [<span class="hljs-meta">_</span>]         =&gt; <span class="hljs-string">&quot;single&quot;</span>,
    [<span class="hljs-meta">_, _</span>]      =&gt; <span class="hljs-string">&quot;pair&quot;</span>,
    [<span class="hljs-meta">_, ..</span>]     =&gt; <span class="hljs-string">&quot;many&quot;</span>
};
""");
    }

    [Fact]
    public void SwitchExpr_WhenClause()
    {
        AssertHighlighter("csharp",
"""
var label = (kind, count) switch
{
    ("apple", > 0) => "have apples",
    (_, 0)         => "none",
    _              => "other"
};
""",
"""
<span class="hljs-keyword">var</span> label = (kind, count) <span class="hljs-keyword">switch</span>
{
    (<span class="hljs-string">&quot;apple&quot;</span>, &gt; <span class="hljs-number">0</span>) =&gt; <span class="hljs-string">&quot;have apples&quot;</span>,
    (_, <span class="hljs-number">0</span>)         =&gt; <span class="hljs-string">&quot;none&quot;</span>,
    _              =&gt; <span class="hljs-string">&quot;other&quot;</span>
};
""");
    }

    [Fact]
    public void Tuple_Construct()
    {
        AssertHighlighter("csharp",
"""
var p = (1, 2);
""",
"""
<span class="hljs-keyword">var</span> p = (<span class="hljs-number">1</span>, <span class="hljs-number">2</span>);
""");
    }

    [Fact]
    public void Tuple_Named()
    {
        AssertHighlighter("csharp",
"""
var p = (X: 1, Y: 2);
""",
"""
<span class="hljs-keyword">var</span> p = (X: <span class="hljs-number">1</span>, Y: <span class="hljs-number">2</span>);
""");
    }

    [Fact]
    public void Tuple_TypeNamed()
    {
        AssertHighlighter("csharp",
"""
(int X, int Y) p = (1, 2);
""",
"""
(<span class="hljs-built_in">int</span> X, <span class="hljs-built_in">int</span> Y) p = (<span class="hljs-number">1</span>, <span class="hljs-number">2</span>);
""");
    }

    [Fact]
    public void Tuple_Return()
    {
        AssertHighlighter("csharp",
"""
public (int min, int max) Range() => (0, 100);
""",
"""
<span class="hljs-keyword">public</span> (<span class="hljs-built_in">int</span> min, <span class="hljs-built_in">int</span> max) Range() =&gt; (<span class="hljs-number">0</span>, <span class="hljs-number">100</span>);
""");
    }

    [Fact]
    public void Tuple_Deconstruct()
    {
        AssertHighlighter("csharp",
"""
var (x, y) = point;
""",
"""
<span class="hljs-keyword">var</span> (x, y) = point;
""");
    }

    [Fact]
    public void Tuple_DeconstructTyped()
    {
        AssertHighlighter("csharp",
"""
(int x, int y) = point;
""",
"""
(<span class="hljs-built_in">int</span> x, <span class="hljs-built_in">int</span> y) = point;
""");
    }

    [Fact]
    public void Tuple_DeconstructDiscard()
    {
        AssertHighlighter("csharp",
"""
var (_, y) = point;
""",
"""
<span class="hljs-keyword">var</span> (_, y) = point;
""");
    }

    [Fact]
    public void Tuple_NestedDeconstruct()
    {
        AssertHighlighter("csharp",
"""
var ((a, b), c) = ((1, 2), 3);
""",
"""
<span class="hljs-keyword">var</span> ((a, b), c) = ((<span class="hljs-number">1</span>, <span class="hljs-number">2</span>), <span class="hljs-number">3</span>);
""");
    }

    [Fact]
    public void Lambda_Simple()
    {
        AssertHighlighter("csharp",
"""
Func<int, int> sq = x => x * x;
""",
"""
Func&lt;<span class="hljs-built_in">int</span>, <span class="hljs-built_in">int</span>&gt; sq = x =&gt; x * x;
""");
    }

    [Fact]
    public void Lambda_Parens()
    {
        AssertHighlighter("csharp",
"""
Func<int, int, int> add = (a, b) => a + b;
""",
"""
Func&lt;<span class="hljs-built_in">int</span>, <span class="hljs-built_in">int</span>, <span class="hljs-built_in">int</span>&gt; <span class="hljs-keyword">add</span> = (a, b) =&gt; a + b;
""");
    }

    [Fact]
    public void Lambda_Body()
    {
        AssertHighlighter("csharp",
"""
Action<int> run = x => { Console.WriteLine(x); };
""",
"""
Action&lt;<span class="hljs-built_in">int</span>&gt; run = x =&gt; { Console.WriteLine(x); };
""");
    }

    [Fact]
    public void Lambda_Typed()
    {
        AssertHighlighter("csharp",
"""
var add = (int a, int b) => a + b;
""",
"""
<span class="hljs-keyword">var</span> <span class="hljs-keyword">add</span> = (<span class="hljs-built_in">int</span> a, <span class="hljs-built_in">int</span> b) =&gt; a + b;
""");
    }

    [Fact]
    public void Lambda_ExplicitReturn()
    {
        AssertHighlighter("csharp",
"""
var f = int (int x) => x * 2;
""",
"""
<span class="hljs-keyword">var</span> f = <span class="hljs-built_in">int</span> (<span class="hljs-built_in">int</span> x) =&gt; x * <span class="hljs-number">2</span>;
""");
    }

    [Fact]
    public void Lambda_DefaultParam()
    {
        AssertHighlighter("csharp",
"""
var greet = (string name = "world") => $"Hello {name}";
""",
"""
<span class="hljs-keyword">var</span> greet = (<span class="hljs-built_in">string</span> name = <span class="hljs-string">&quot;world&quot;</span>) =&gt; <span class="hljs-string">$&quot;Hello <span class="hljs-subst">{name}</span>&quot;</span>;
""");
    }

    [Fact]
    public void Lambda_ParamsLambda()
    {
        AssertHighlighter("csharp",
"""
var sum = (params int[] xs) => xs.Sum();
""",
"""
<span class="hljs-keyword">var</span> sum = (<span class="hljs-keyword">params</span> <span class="hljs-built_in">int</span>[] xs) =&gt; xs.Sum();
""");
    }

    [Fact]
    public void Lambda_StaticLambda()
    {
        AssertHighlighter("csharp",
"""
Func<int, int> sq = static x => x * x;
""",
"""
Func&lt;<span class="hljs-built_in">int</span>, <span class="hljs-built_in">int</span>&gt; sq = <span class="hljs-keyword">static</span> x =&gt; x * x;
""");
    }

    [Fact]
    public void Lambda_AsyncLambda()
    {
        AssertHighlighter("csharp",
"""
Func<Task> run = async () => await DoAsync();
""",
"""
Func&lt;Task&gt; run = <span class="hljs-keyword">async</span> () =&gt; <span class="hljs-keyword">await</span> DoAsync();
""");
    }

    [Fact]
    public void Lambda_AttributedLambda()
    {
        AssertHighlighter("csharp",
"""
var f = [Conditional("DEBUG")] (int x) => x;
""",
"""
<span class="hljs-keyword">var</span> f = [Conditional(<span class="hljs-string">&quot;DEBUG&quot;</span>)] (<span class="hljs-built_in">int</span> x) =&gt; x;
""");
    }

    [Fact]
    public void Param_NamedFrom()
    {
        AssertHighlighter("csharp",
"""
void A(int from) { }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">void</span> <span class="hljs-title">A</span>(<span class="hljs-params"><span class="hljs-built_in">int</span> from</span>)</span> { }
""");
    }

    [Fact]
    public void Param_NamedWhere()
    {
        AssertHighlighter("csharp",
"""
void A(int where) { }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">void</span> <span class="hljs-title">A</span>(<span class="hljs-params"><span class="hljs-built_in">int</span> where</span>)</span> { }
""");
    }

    [Fact]
    public void LocalVar_NamedFrom()
    {
        AssertHighlighter("csharp",
"""
int from = 1;
""",
"""
<span class="hljs-built_in">int</span> from = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void MemberAccess_From()
    {
        AssertHighlighter("csharp",
"""
var x = obj.from;
""",
"""
<span class="hljs-keyword">var</span> x = obj.from;
""");
    }

    [Fact]
    public void NullConditional_Where()
    {
        AssertHighlighter("csharp",
"""
var x = obj?.where;
""",
"""
<span class="hljs-keyword">var</span> x = obj?.where;
""");
    }

    [Fact]
    public void Linq_QuerySimple()
    {
        AssertHighlighter("csharp",
"""
var q = from u in users where u.IsActive select u.Name;
""",
"""
<span class="hljs-keyword">var</span> q = <span class="hljs-keyword">from</span> u <span class="hljs-keyword">in</span> users <span class="hljs-keyword">where</span> u.IsActive <span class="hljs-keyword">select</span> u.Name;
""");
    }

    [Fact]
    public void Linq_QueryJoin()
    {
        AssertHighlighter("csharp",
"""
var q = from u in users
        join o in orders on u.Id equals o.UserId
        select new { u.Name, o.Total };
""",
"""
<span class="hljs-keyword">var</span> q = <span class="hljs-keyword">from</span> u <span class="hljs-keyword">in</span> users
        <span class="hljs-keyword">join</span> o <span class="hljs-keyword">in</span> orders <span class="hljs-keyword">on</span> u.Id <span class="hljs-keyword">equals</span> o.UserId
        <span class="hljs-keyword">select</span> <span class="hljs-keyword">new</span> { u.Name, o.Total };
""");
    }

    [Fact]
    public void Linq_QueryGroup()
    {
        AssertHighlighter("csharp",
"""
var q = from u in users
        group u by u.Country into g
        select new { Country = g.Key, Count = g.Count() };
""",
"""
<span class="hljs-keyword">var</span> q = <span class="hljs-keyword">from</span> u <span class="hljs-keyword">in</span> users
        <span class="hljs-keyword">group</span> u <span class="hljs-keyword">by</span> u.Country <span class="hljs-keyword">into</span> g
        <span class="hljs-keyword">select</span> <span class="hljs-keyword">new</span> { Country = g.Key, Count = g.Count() };
""");
    }

    [Fact]
    public void Linq_QueryOrder()
    {
        AssertHighlighter("csharp",
"""
var q = from u in users orderby u.Age descending, u.Name select u;
""",
"""
<span class="hljs-keyword">var</span> q = <span class="hljs-keyword">from</span> u <span class="hljs-keyword">in</span> users <span class="hljs-keyword">orderby</span> u.Age <span class="hljs-keyword">descending</span>, u.Name <span class="hljs-keyword">select</span> u;
""");
    }

    [Fact]
    public void Linq_QueryLet()
    {
        AssertHighlighter("csharp",
"""
var q = from u in users let total = u.Orders.Sum(o => o.Total) where total > 100 select new { u.Name, total };
""",
"""
<span class="hljs-keyword">var</span> q = <span class="hljs-keyword">from</span> u <span class="hljs-keyword">in</span> users <span class="hljs-keyword">let</span> total = u.Orders.Sum(o =&gt; o.Total) <span class="hljs-keyword">where</span> total &gt; <span class="hljs-number">100</span> <span class="hljs-keyword">select</span> <span class="hljs-keyword">new</span> { u.Name, total };
""");
    }

    [Fact]
    public void Linq_MethodSyntax()
    {
        AssertHighlighter("csharp",
"""
var names = users.Where(u => u.IsActive).Select(u => u.Name).ToList();
""",
"""
<span class="hljs-keyword">var</span> names = users.Where(u =&gt; u.IsActive).Select(u =&gt; u.Name).ToList();
""");
    }

    [Fact]
    public void Linq_MethodChain()
    {
        AssertHighlighter("csharp",
"""
var total = items
    .Where(x => x.Active)
    .Select(x => x.Price)
    .Sum();
""",
"""
<span class="hljs-keyword">var</span> total = items
    .Where(x =&gt; x.Active)
    .Select(x =&gt; x.Price)
    .Sum();
""");
    }

    [Fact]
    public void Async_AwaitExpression()
    {
        AssertHighlighter("csharp",
"""
var data = await httpClient.GetStringAsync(url);
""",
"""
<span class="hljs-keyword">var</span> data = <span class="hljs-keyword">await</span> httpClient.GetStringAsync(url);
""");
    }

    [Fact]
    public void Async_AwaitForeach()
    {
        AssertHighlighter("csharp",
"""
await foreach (var item in source.WithCancellation(ct)) Process(item);
""",
"""
<span class="hljs-keyword">await</span> <span class="hljs-keyword">foreach</span> (<span class="hljs-keyword">var</span> item <span class="hljs-keyword">in</span> source.WithCancellation(ct)) Process(item);
""");
    }

    [Fact]
    public void Async_AwaitUsing()
    {
        AssertHighlighter("csharp",
"""
await using var conn = new SqlConnection(cs);
""",
"""
<span class="hljs-keyword">await</span> <span class="hljs-keyword">using</span> <span class="hljs-keyword">var</span> conn = <span class="hljs-keyword">new</span> SqlConnection(cs);
""");
    }

    [Fact]
    public void Async_AsyncEnumerable()
    {
        AssertHighlighter("csharp",
"""
public async IAsyncEnumerable<int> RangeAsync(int n)
{
    for (int i = 0; i < n; i++)
    {
        await Task.Yield();
        yield return i;
    }
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">async</span> IAsyncEnumerable&lt;<span class="hljs-built_in">int</span>&gt; <span class="hljs-title">RangeAsync</span>(<span class="hljs-params"><span class="hljs-built_in">int</span> n</span>)</span>
{
    <span class="hljs-keyword">for</span> (<span class="hljs-built_in">int</span> i = <span class="hljs-number">0</span>; i &lt; n; i++)
    {
        <span class="hljs-keyword">await</span> Task.Yield();
        <span class="hljs-keyword">yield</span> <span class="hljs-keyword">return</span> i;
    }
}
""");
    }

    [Fact]
    public void Async_ConfigureAwait()
    {
        AssertHighlighter("csharp",
"""
await Task.Delay(100).ConfigureAwait(false);
""",
"""
<span class="hljs-keyword">await</span> Task.Delay(<span class="hljs-number">100</span>).ConfigureAwait(<span class="hljs-literal">false</span>);
""");
    }

    [Fact]
    public void CollectionExpression_Empty()
    {
        AssertHighlighter("csharp",
"""
int[] arr = [];
""",
"""
<span class="hljs-built_in">int</span>[] arr = [];
""");
    }

    [Fact]
    public void CollectionExpression_IntArr()
    {
        AssertHighlighter("csharp",
"""
int[] arr = [1, 2, 3];
""",
"""
<span class="hljs-built_in">int</span>[] arr = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>];
""");
    }

    [Fact]
    public void CollectionExpression_List()
    {
        AssertHighlighter("csharp",
"""
List<string> names = ["alice", "bob"];
""",
"""
List&lt;<span class="hljs-built_in">string</span>&gt; names = [<span class="hljs-string">&quot;alice&quot;</span>, <span class="hljs-string">&quot;bob&quot;</span>];
""");
    }

    [Fact]
    public void CollectionExpression_Span()
    {
        AssertHighlighter("csharp",
"""
ReadOnlySpan<int> nums = [1, 2, 3, 4];
""",
"""
ReadOnlySpan&lt;<span class="hljs-built_in">int</span>&gt; nums = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>, <span class="hljs-number">4</span>];
""");
    }

    [Fact]
    public void CollectionExpression_Spread()
    {
        AssertHighlighter("csharp",
"""
int[] combined = [..a, ..b, 99];
""",
"""
<span class="hljs-built_in">int</span>[] combined = [..a, ..b, <span class="hljs-number">99</span>];
""");
    }

    [Fact]
    public void CollectionExpression_Dictionary()
    {
        AssertHighlighter("csharp",
"""
Dictionary<string, int> ages = new() { ["alice"] = 30, ["bob"] = 25 };
""",
"""
Dictionary&lt;<span class="hljs-built_in">string</span>, <span class="hljs-built_in">int</span>&gt; ages = <span class="hljs-keyword">new</span>() { [<span class="hljs-string">&quot;alice&quot;</span>] = <span class="hljs-number">30</span>, [<span class="hljs-string">&quot;bob&quot;</span>] = <span class="hljs-number">25</span> };
""");
    }

    [Fact]
    public void Range_FromTo()
    {
        AssertHighlighter("csharp",
"""
var sub = arr[1..4];
""",
"""
<span class="hljs-keyword">var</span> sub = arr[<span class="hljs-number">1.</span><span class="hljs-number">.4</span>];
""");
    }

    [Fact]
    public void Range_FromStart()
    {
        AssertHighlighter("csharp",
"""
var head = arr[..3];
""",
"""
<span class="hljs-keyword">var</span> head = arr[.<span class="hljs-number">.3</span>];
""");
    }

    [Fact]
    public void Range_ToEnd()
    {
        AssertHighlighter("csharp",
"""
var tail = arr[2..];
""",
"""
<span class="hljs-keyword">var</span> tail = arr[<span class="hljs-number">2.</span>.];
""");
    }

    [Fact]
    public void Range_IndexEnd()
    {
        AssertHighlighter("csharp",
"""
var last = arr[^1];
""",
"""
<span class="hljs-keyword">var</span> last = arr[^<span class="hljs-number">1</span>];
""");
    }

    [Fact]
    public void Range_RangeIndex()
    {
        AssertHighlighter("csharp",
"""
var lastTwo = arr[^2..];
""",
"""
<span class="hljs-keyword">var</span> lastTwo = arr[^<span class="hljs-number">2.</span>.];
""");
    }

    [Fact]
    public void Range_RangeAll()
    {
        AssertHighlighter("csharp",
"""
var copy = arr[..];
""",
"""
<span class="hljs-keyword">var</span> copy = arr[..];
""");
    }

    [Fact]
    public void Nullable_ValueTypeNullable()
    {
        AssertHighlighter("csharp",
"""
int? x = null;
""",
"""
<span class="hljs-built_in">int</span>? x = <span class="hljs-literal">null</span>;
""");
    }

    [Fact]
    public void Nullable_RefNullable()
    {
        AssertHighlighter("csharp",
"""
#nullable enable
public string? Name { get; set; }
""",
"""
<span class="hljs-meta">#nullable enable</span>
<span class="hljs-keyword">public</span> <span class="hljs-built_in">string</span>? Name { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">set</span>; }
""");
    }

    [Fact]
    public void Nullable_NullForgiving()
    {
        AssertHighlighter("csharp",
"""
var len = name!.Length;
""",
"""
<span class="hljs-keyword">var</span> len = name!.Length;
""");
    }

    [Fact]
    public void Nullable_NullCoalesce()
    {
        AssertHighlighter("csharp",
"""
var value = maybeNull ?? defaultValue;
""",
"""
<span class="hljs-keyword">var</span> <span class="hljs-keyword">value</span> = maybeNull ?? defaultValue;
""");
    }

    [Fact]
    public void Nullable_NullCoalesceAssign()
    {
        AssertHighlighter("csharp",
"""
cache ??= ComputeCache();
""",
"""
cache ??= ComputeCache();
""");
    }

    [Fact]
    public void Nullable_NullConditional()
    {
        AssertHighlighter("csharp",
"""
var len = name?.Length;
""",
"""
<span class="hljs-keyword">var</span> len = name?.Length;
""");
    }

    [Fact]
    public void Nullable_NullConditionalCall()
    {
        AssertHighlighter("csharp",
"""
observer?.Notify(e);
""",
"""
observer?.Notify(e);
""");
    }

    [Fact]
    public void Nullable_NullConditionalIndex()
    {
        AssertHighlighter("csharp",
"""
var first = arr?[0];
""",
"""
<span class="hljs-keyword">var</span> first = arr?[<span class="hljs-number">0</span>];
""");
    }

    [Fact]
    public void Nullable_NullableDirective()
    {
        AssertHighlighter("csharp",
"""
#nullable enable
public string? Name { get; set; }
#nullable disable
public string OldName { get; set; }
""",
"""
<span class="hljs-meta">#nullable enable</span>
<span class="hljs-keyword">public</span> <span class="hljs-built_in">string</span>? Name { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">set</span>; }
<span class="hljs-meta">#nullable disable</span>
<span class="hljs-keyword">public</span> <span class="hljs-built_in">string</span> OldName { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">set</span>; }
""");
    }

    [Fact]
    public void ExceptionHandling_TryCatch()
    {
        AssertHighlighter("csharp",
"""
try { Risk(); } catch (Exception ex) { Log(ex); }
""",
"""
<span class="hljs-keyword">try</span> { Risk(); } <span class="hljs-keyword">catch</span> (Exception ex) { Log(ex); }
""");
    }

    [Fact]
    public void ExceptionHandling_TryCatchFilter()
    {
        AssertHighlighter("csharp",
"""
try { Risk(); } catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { /* 404 */ }
""",
"""
<span class="hljs-keyword">try</span> { Risk(); } <span class="hljs-keyword">catch</span> (HttpRequestException ex) <span class="hljs-keyword">when</span> (ex.StatusCode == HttpStatusCode.NotFound) { <span class="hljs-comment">/* 404 */</span> }
""");
    }

    [Fact]
    public void ExceptionHandling_TryFinally()
    {
        AssertHighlighter("csharp",
"""
try { Risk(); } finally { Cleanup(); }
""",
"""
<span class="hljs-keyword">try</span> { Risk(); } <span class="hljs-keyword">finally</span> { Cleanup(); }
""");
    }

    [Fact]
    public void ExceptionHandling_TryCatchMulti()
    {
        AssertHighlighter("csharp",
"""
try { Risk(); }
catch (IOException ex) { Log("io", ex); }
catch (Exception ex)   { Log("any", ex); throw; }
""",
"""
<span class="hljs-keyword">try</span> { Risk(); }
<span class="hljs-keyword">catch</span> (IOException ex) { Log(<span class="hljs-string">&quot;io&quot;</span>, ex); }
<span class="hljs-keyword">catch</span> (Exception ex)   { Log(<span class="hljs-string">&quot;any&quot;</span>, ex); <span class="hljs-keyword">throw</span>; }
""");
    }

    [Fact]
    public void ExceptionHandling_ThrowStmt()
    {
        AssertHighlighter("csharp",
"""
throw new ArgumentNullException(nameof(name));
""",
"""
<span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> ArgumentNullException(<span class="hljs-keyword">nameof</span>(name));
""");
    }

    [Fact]
    public void ExceptionHandling_ThrowExpression()
    {
        AssertHighlighter("csharp",
"""
var name = input ?? throw new ArgumentNullException(nameof(input));
""",
"""
<span class="hljs-keyword">var</span> name = input ?? <span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> ArgumentNullException(<span class="hljs-keyword">nameof</span>(input));
""");
    }

    [Fact]
    public void ExceptionHandling_Rethrow()
    {
        AssertHighlighter("csharp",
"""
try { Risk(); } catch { throw; }
""",
"""
<span class="hljs-keyword">try</span> { Risk(); } <span class="hljs-keyword">catch</span> { <span class="hljs-keyword">throw</span>; }
""");
    }

    [Fact]
    public void Record_Positional()
    {
        AssertHighlighter("csharp",
"""
public record User(string Name, int Age);
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">record</span> <span class="hljs-title">User</span>(<span class="hljs-params"><span class="hljs-built_in">string</span> Name, <span class="hljs-built_in">int</span> Age</span>)</span>;
""");
    }

    [Fact]
    public void Record_PositionalStruct()
    {
        AssertHighlighter("csharp",
"""
public readonly record struct Point(double X, double Y);
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">readonly</span> <span class="hljs-keyword">record</span> <span class="hljs-keyword">struct</span> <span class="hljs-title">Point</span>(<span class="hljs-params"><span class="hljs-built_in">double</span> X, <span class="hljs-built_in">double</span> Y</span>)</span>;
""");
    }

    [Fact]
    public void Record_WithBody()
    {
        AssertHighlighter("csharp",
"""
public record User(string Name)
{
    public string Display => Name.ToUpperInvariant();
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">record</span> <span class="hljs-title">User</span>(<span class="hljs-params"><span class="hljs-built_in">string</span> Name</span>)</span>
{
    <span class="hljs-keyword">public</span> <span class="hljs-built_in">string</span> Display =&gt; Name.ToUpperInvariant();
}
""");
    }

    [Fact]
    public void Record_Inheritance()
    {
        AssertHighlighter("csharp",
"""
public record Person(string Name);
public record Employee(string Name, string Title) : Person(Name);
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">record</span> <span class="hljs-title">Person</span>(<span class="hljs-params"><span class="hljs-built_in">string</span> Name</span>)</span>;
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">record</span> <span class="hljs-title">Employee</span>(<span class="hljs-params"><span class="hljs-built_in">string</span> Name, <span class="hljs-built_in">string</span> Title</span>) : <span class="hljs-title">Person</span>(<span class="hljs-params">Name</span>)</span>;
""");
    }

    [Fact]
    public void Record_WithExpression()
    {
        AssertHighlighter("csharp",
"""
var older = user with { Age = user.Age + 1 };
""",
"""
<span class="hljs-keyword">var</span> older = user <span class="hljs-keyword">with</span> { Age = user.Age + <span class="hljs-number">1</span> };
""");
    }

    [Fact]
    public void Record_PrimaryInit()
    {
        AssertHighlighter("csharp",
"""
public record Foo(int X) { public int Y { get; init; } = 0; }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">record</span> <span class="hljs-title">Foo</span>(<span class="hljs-params"><span class="hljs-built_in">int</span> X</span>)</span> { <span class="hljs-keyword">public</span> <span class="hljs-built_in">int</span> Y { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">init</span>; } = <span class="hljs-number">0</span>; }
""");
    }

    [Fact]
    public void Loop_For()
    {
        AssertHighlighter("csharp",
"""
for (int i = 0; i < 10; i++) Console.WriteLine(i);
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-built_in">int</span> i = <span class="hljs-number">0</span>; i &lt; <span class="hljs-number">10</span>; i++) Console.WriteLine(i);
""");
    }

    [Fact]
    public void Loop_ForEach()
    {
        AssertHighlighter("csharp",
"""
foreach (var x in items) Process(x);
""",
"""
<span class="hljs-keyword">foreach</span> (<span class="hljs-keyword">var</span> x <span class="hljs-keyword">in</span> items) Process(x);
""");
    }

    [Fact]
    public void Loop_ForEachTyped()
    {
        AssertHighlighter("csharp",
"""
foreach (User u in users) Process(u);
""",
"""
<span class="hljs-keyword">foreach</span> (User u <span class="hljs-keyword">in</span> users) Process(u);
""");
    }

    [Fact]
    public void Loop_While()
    {
        AssertHighlighter("csharp",
"""
while (queue.Count > 0) queue.Dequeue();
""",
"""
<span class="hljs-keyword">while</span> (queue.Count &gt; <span class="hljs-number">0</span>) queue.Dequeue();
""");
    }

    [Fact]
    public void Loop_DoWhile()
    {
        AssertHighlighter("csharp",
"""
do { Pop(); } while (stack.Count > 0);
""",
"""
<span class="hljs-keyword">do</span> { Pop(); } <span class="hljs-keyword">while</span> (stack.Count &gt; <span class="hljs-number">0</span>);
""");
    }

    [Fact]
    public void Loop_Break()
    {
        AssertHighlighter("csharp",
"""
foreach (var x in items)
{
    if (x.IsBad) break;
    Process(x);
}
""",
"""
<span class="hljs-keyword">foreach</span> (<span class="hljs-keyword">var</span> x <span class="hljs-keyword">in</span> items)
{
    <span class="hljs-keyword">if</span> (x.IsBad) <span class="hljs-keyword">break</span>;
    Process(x);
}
""");
    }

    [Fact]
    public void Loop_Continue()
    {
        AssertHighlighter("csharp",
"""
foreach (var x in items)
{
    if (!x.IsValid) continue;
    Process(x);
}
""",
"""
<span class="hljs-keyword">foreach</span> (<span class="hljs-keyword">var</span> x <span class="hljs-keyword">in</span> items)
{
    <span class="hljs-keyword">if</span> (!x.IsValid) <span class="hljs-keyword">continue</span>;
    Process(x);
}
""");
    }

    [Fact]
    public void ControlFlow_IfElse()
    {
        AssertHighlighter("csharp",
"""
if (x > 0)
{
    Positive();
}
else if (x < 0)
{
    Negative();
}
else
{
    Zero();
}
""",
"""
<span class="hljs-keyword">if</span> (x &gt; <span class="hljs-number">0</span>)
{
    Positive();
}
<span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> (x &lt; <span class="hljs-number">0</span>)
{
    Negative();
}
<span class="hljs-keyword">else</span>
{
    Zero();
}
""");
    }

    [Fact]
    public void ControlFlow_Ternary()
    {
        AssertHighlighter("csharp",
"""
var s = (x > 0) ? "pos" : "non-pos";
""",
"""
<span class="hljs-keyword">var</span> s = (x &gt; <span class="hljs-number">0</span>) ? <span class="hljs-string">&quot;pos&quot;</span> : <span class="hljs-string">&quot;non-pos&quot;</span>;
""");
    }

    [Fact]
    public void ControlFlow_GotoLabel()
    {
        AssertHighlighter("csharp",
"""
start:
    if (count++ < 10) goto start;
""",
"""
start:
    <span class="hljs-keyword">if</span> (count++ &lt; <span class="hljs-number">10</span>) <span class="hljs-keyword">goto</span> start;
""");
    }

    [Fact]
    public void ControlFlow_GotoCase()
    {
        AssertHighlighter("csharp",
"""
switch (x)
{
    case 1: goto case 2;
    case 2: Console.WriteLine("two"); break;
}
""",
"""
<span class="hljs-keyword">switch</span> (x)
{
    <span class="hljs-keyword">case</span> <span class="hljs-number">1</span>: <span class="hljs-keyword">goto</span> <span class="hljs-keyword">case</span> <span class="hljs-number">2</span>;
    <span class="hljs-keyword">case</span> <span class="hljs-number">2</span>: Console.WriteLine(<span class="hljs-string">&quot;two&quot;</span>); <span class="hljs-keyword">break</span>;
}
""");
    }

    [Fact]
    public void ControlFlow_YieldReturn()
    {
        AssertHighlighter("csharp",
"""
public IEnumerable<int> Range(int n)
{
    for (int i = 0; i < n; i++) yield return i;
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> IEnumerable&lt;<span class="hljs-built_in">int</span>&gt; <span class="hljs-title">Range</span>(<span class="hljs-params"><span class="hljs-built_in">int</span> n</span>)</span>
{
    <span class="hljs-keyword">for</span> (<span class="hljs-built_in">int</span> i = <span class="hljs-number">0</span>; i &lt; n; i++) <span class="hljs-keyword">yield</span> <span class="hljs-keyword">return</span> i;
}
""");
    }

    [Fact]
    public void ControlFlow_YieldBreak()
    {
        AssertHighlighter("csharp",
"""
public IEnumerable<int> First(IEnumerable<int> xs, int max)
{
    int n = 0;
    foreach (var x in xs) { if (n++ >= max) yield break; yield return x; }
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">public</span> IEnumerable&lt;<span class="hljs-built_in">int</span>&gt; <span class="hljs-title">First</span>(<span class="hljs-params">IEnumerable&lt;<span class="hljs-built_in">int</span>&gt; xs, <span class="hljs-built_in">int</span> max</span>)</span>
{
    <span class="hljs-built_in">int</span> n = <span class="hljs-number">0</span>;
    <span class="hljs-keyword">foreach</span> (<span class="hljs-keyword">var</span> x <span class="hljs-keyword">in</span> xs) { <span class="hljs-keyword">if</span> (n++ &gt;= max) <span class="hljs-keyword">yield</span> <span class="hljs-keyword">break</span>; <span class="hljs-keyword">yield</span> <span class="hljs-keyword">return</span> x; }
}
""");
    }

    [Fact]
    public void Using_Statement()
    {
        AssertHighlighter("csharp",
"""
using (var f = File.OpenRead(path)) { f.Read(buf, 0, buf.Length); }
""",
"""
<span class="hljs-keyword">using</span> (<span class="hljs-keyword">var</span> f = File.OpenRead(path)) { f.Read(buf, <span class="hljs-number">0</span>, buf.Length); }
""");
    }

    [Fact]
    public void Using_Declaration()
    {
        AssertHighlighter("csharp",
"""
using var f = File.OpenRead(path);
f.Read(buf, 0, buf.Length);
""",
"""
<span class="hljs-keyword">using</span> <span class="hljs-keyword">var</span> f = File.OpenRead(path);
f.Read(buf, <span class="hljs-number">0</span>, buf.Length);
""");
    }

    [Fact]
    public void Using_MultipleResources()
    {
        AssertHighlighter("csharp",
"""
using (var a = Open()) using (var b = Open()) { Use(a, b); }
""",
"""
<span class="hljs-keyword">using</span> (<span class="hljs-keyword">var</span> a = Open()) <span class="hljs-keyword">using</span> (<span class="hljs-keyword">var</span> b = Open()) { Use(a, b); }
""");
    }

    [Fact]
    public void GenericVariance_In()
    {
        AssertHighlighter("csharp",
"""
public interface IComparer<in T> { int Compare(T x, T y); }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">interface</span> <span class="hljs-title">IComparer</span>&lt;<span class="hljs-keyword">in</span> <span class="hljs-title">T</span>&gt; { <span class="hljs-function"><span class="hljs-built_in">int</span> <span class="hljs-title">Compare</span>(<span class="hljs-params">T x, T y</span>)</span>; }
""");
    }

    [Fact]
    public void GenericVariance_Out()
    {
        AssertHighlighter("csharp",
"""
public interface IEnumerable<out T> { IEnumerator<T> GetEnumerator(); }
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">interface</span> <span class="hljs-title">IEnumerable</span>&lt;<span class="hljs-keyword">out</span> <span class="hljs-title">T</span>&gt; { <span class="hljs-function">IEnumerator&lt;T&gt; <span class="hljs-title">GetEnumerator</span>()</span>; }
""");
    }

    [Fact]
    public void Concurrency_LockStatement()
    {
        AssertHighlighter("csharp",
"""
lock (_sync) { _counter++; }
""",
"""
<span class="hljs-keyword">lock</span> (_sync) { _counter++; }
""");
    }

    [Fact]
    public void Concurrency_LockObject()
    {
        AssertHighlighter("csharp",
"""
private readonly Lock _gate = new();
lock (_gate) { _counter++; }
""",
"""
<span class="hljs-keyword">private</span> <span class="hljs-keyword">readonly</span> Lock _gate = <span class="hljs-keyword">new</span>();
<span class="hljs-keyword">lock</span> (_gate) { _counter++; }
""");
    }

    [Fact]
    public void Concurrency_InterlockedIncrement()
    {
        AssertHighlighter("csharp",
"""
Interlocked.Increment(ref _counter);
""",
"""
Interlocked.Increment(<span class="hljs-keyword">ref</span> _counter);
""");
    }

    [Fact]
    public void Concurrency_Volatile()
    {
        AssertHighlighter("csharp",
"""
private volatile bool _stopRequested;
""",
"""
<span class="hljs-keyword">private</span> <span class="hljs-keyword">volatile</span> <span class="hljs-built_in">bool</span> _stopRequested;
""");
    }

    [Fact]
    public void Unsafe_PointerDeref()
    {
        AssertHighlighter("csharp",
"""
unsafe int Deref(int* p) => *p;
""",
"""
<span class="hljs-function"><span class="hljs-keyword">unsafe</span> <span class="hljs-built_in">int</span> <span class="hljs-title">Deref</span>(<span class="hljs-params"><span class="hljs-built_in">int</span>* p</span>)</span> =&gt; *p;
""");
    }

    [Fact]
    public void Unsafe_Fixed()
    {
        AssertHighlighter("csharp",
"""
unsafe void Pin(int[] arr)
{
    fixed (int* p = arr) { *p = 0; }
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">unsafe</span> <span class="hljs-keyword">void</span> <span class="hljs-title">Pin</span>(<span class="hljs-params"><span class="hljs-built_in">int</span>[] arr</span>)</span>
{
    <span class="hljs-keyword">fixed</span> (<span class="hljs-built_in">int</span>* p = arr) { *p = <span class="hljs-number">0</span>; }
}
""");
    }

    [Fact]
    public void Unsafe_Stackalloc()
    {
        AssertHighlighter("csharp",
"""
Span<int> buffer = stackalloc int[64];
""",
"""
Span&lt;<span class="hljs-built_in">int</span>&gt; buffer = <span class="hljs-keyword">stackalloc</span> <span class="hljs-built_in">int</span>[<span class="hljs-number">64</span>];
""");
    }

    [Fact]
    public void Unsafe_FunctionPointer()
    {
        AssertHighlighter("csharp",
"""
unsafe void Use(delegate*<int, int> fn)
{
    int r = fn(10);
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">unsafe</span> <span class="hljs-keyword">void</span> <span class="hljs-title">Use</span>(<span class="hljs-params"><span class="hljs-built_in">delegate</span>*&lt;<span class="hljs-built_in">int</span>, <span class="hljs-built_in">int</span>&gt; fn</span>)</span>
{
    <span class="hljs-built_in">int</span> r = fn(<span class="hljs-number">10</span>);
}
""");
    }

    [Fact]
    public void Unsafe_FunctionPointerCdecl()
    {
        AssertHighlighter("csharp",
"""
unsafe delegate* unmanaged[Cdecl]<int, int> fn;
""",
"""
<span class="hljs-keyword">unsafe</span> <span class="hljs-built_in">delegate</span>* <span class="hljs-keyword">unmanaged</span>[Cdecl]&lt;<span class="hljs-built_in">int</span>, <span class="hljs-built_in">int</span>&gt; fn;
""");
    }

    [Fact]
    public void Preprocessor_IfDirective()
    {
        AssertHighlighter("csharp",
"""
#if DEBUG
Console.WriteLine("debug");
#endif
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">if</span> DEBUG</span>
Console.WriteLine(<span class="hljs-string">&quot;debug&quot;</span>);
<span class="hljs-meta">#<span class="hljs-keyword">endif</span></span>
""");
    }

    [Fact]
    public void Preprocessor_IfElseDirective()
    {
        AssertHighlighter("csharp",
"""
#if NET8_0_OR_GREATER
UseNet8();
#elif NET6_0_OR_GREATER
UseNet6();
#else
UseLegacy();
#endif
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">if</span> NET8_0_OR_GREATER</span>
UseNet8();
<span class="hljs-meta">#<span class="hljs-keyword">elif</span> NET6_0_OR_GREATER</span>
UseNet6();
<span class="hljs-meta">#<span class="hljs-keyword">else</span></span>
UseLegacy();
<span class="hljs-meta">#<span class="hljs-keyword">endif</span></span>
""");
    }

    [Fact]
    public void Preprocessor_Define()
    {
        AssertHighlighter("csharp",
"""
#define TRACE
#undef DEBUG
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">define</span> TRACE</span>
<span class="hljs-meta">#<span class="hljs-keyword">undef</span> DEBUG</span>
""");
    }

    [Fact]
    public void Preprocessor_Region()
    {
        AssertHighlighter("csharp",
"""
#region Helpers
private void Log() { }
#endregion
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">region</span> Helpers</span>
<span class="hljs-function"><span class="hljs-keyword">private</span> <span class="hljs-keyword">void</span> <span class="hljs-title">Log</span>()</span> { }
<span class="hljs-meta">#<span class="hljs-keyword">endregion</span></span>
""");
    }

    [Fact]
    public void Preprocessor_Pragma()
    {
        AssertHighlighter("csharp",
"""
#pragma warning disable CA1822
private int Compute() => 0;
#pragma warning restore CA1822
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">pragma</span> <span class="hljs-keyword">warning</span> disable CA1822</span>
<span class="hljs-function"><span class="hljs-keyword">private</span> <span class="hljs-built_in">int</span> <span class="hljs-title">Compute</span>()</span> =&gt; <span class="hljs-number">0</span>;
<span class="hljs-meta">#<span class="hljs-keyword">pragma</span> <span class="hljs-keyword">warning</span> restore CA1822</span>
""");
    }

    [Fact]
    public void Preprocessor_NullableEnable()
    {
        AssertHighlighter("csharp",
"""
#nullable enable
public string? Maybe { get; }
""",
"""
<span class="hljs-meta">#nullable enable</span>
<span class="hljs-keyword">public</span> <span class="hljs-built_in">string</span>? Maybe { <span class="hljs-keyword">get</span>; }
""");
    }

    [Fact]
    public void Preprocessor_NullableContext()
    {
        AssertHighlighter("csharp",
"""
#nullable enable warnings
public string Name { get; }
""",
"""
<span class="hljs-meta">#nullable enable warnings</span>
<span class="hljs-keyword">public</span> <span class="hljs-built_in">string</span> Name { <span class="hljs-keyword">get</span>; }
""");
    }

    [Fact]
    public void Preprocessor_LineHidden()
    {
        AssertHighlighter("csharp",
"""
#line hidden
var x = 1;
#line default
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">line</span> hidden</span>
<span class="hljs-keyword">var</span> x = <span class="hljs-number">1</span>;
<span class="hljs-meta">#<span class="hljs-keyword">line</span> default</span>
""");
    }

    [Fact]
    public void Preprocessor_Error()
    {
        AssertHighlighter("csharp",
"""
#if !NETSTANDARD
#error This file is netstandard-only.
#endif
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">if</span> !NETSTANDARD</span>
<span class="hljs-meta">#<span class="hljs-keyword">error</span> This file is netstandard-only.</span>
<span class="hljs-meta">#<span class="hljs-keyword">endif</span></span>
""");
    }

    [Fact]
    public void Preprocessor_Warning()
    {
        AssertHighlighter("csharp",
"""
#warning This needs review
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">warning</span> This needs review</span>
""");
    }

    [Fact]
    public void TopLevel_HelloWorld()
    {
        AssertHighlighter("csharp",
"""
Console.WriteLine("Hello, World!");
""",
"""
Console.WriteLine(<span class="hljs-string">&quot;Hello, World!&quot;</span>);
""");
    }

    [Fact]
    public void TopLevel_WithArgs()
    {
        AssertHighlighter("csharp",
"""
foreach (var arg in args) Console.WriteLine(arg);
""",
"""
<span class="hljs-keyword">foreach</span> (<span class="hljs-keyword">var</span> arg <span class="hljs-keyword">in</span> <span class="hljs-keyword">args</span>) Console.WriteLine(arg);
""");
    }

    [Fact]
    public void TopLevel_WithLocalFunc()
    {
        AssertHighlighter("csharp",
"""
var result = Square(5);
Console.WriteLine(result);

static int Square(int x) => x * x;
""",
"""
<span class="hljs-keyword">var</span> result = Square(<span class="hljs-number">5</span>);
Console.WriteLine(result);

<span class="hljs-function"><span class="hljs-keyword">static</span> <span class="hljs-built_in">int</span> <span class="hljs-title">Square</span>(<span class="hljs-params"><span class="hljs-built_in">int</span> x</span>)</span> =&gt; x * x;
""");
    }

    [Fact]
    public void Comment_Line()
    {
        AssertHighlighter("csharp",
"""
// this is a line comment
""",
"""
<span class="hljs-comment">// this is a line comment</span>
""");
    }

    [Fact]
    public void Comment_Block()
    {
        AssertHighlighter("csharp",
"""
/* this is a block comment */
""",
"""
<span class="hljs-comment">/* this is a block comment */</span>
""");
    }

    [Fact]
    public void Comment_BlockMultiLine()
    {
        AssertHighlighter("csharp",
"""
/*
 * Multi-line
 * comment
 */
""",
"""
<span class="hljs-comment">/*
 * Multi-line
 * comment
 */</span>
""");
    }

    [Fact]
    public void Comment_XmlDoc()
    {
        AssertHighlighter("csharp",
"""
/// <summary>
/// Adds two integers.
/// </summary>
/// <param name="a">First operand.</param>
/// <param name="b">Second operand.</param>
public int Add(int a, int b) => a + b;
""",
"""
<span class="hljs-comment"><span class="hljs-doctag">///</span> <span class="hljs-doctag">&lt;summary&gt;</span></span>
<span class="hljs-comment"><span class="hljs-doctag">///</span> Adds two integers.</span>
<span class="hljs-comment"><span class="hljs-doctag">///</span> <span class="hljs-doctag">&lt;/summary&gt;</span></span>
<span class="hljs-comment"><span class="hljs-doctag">///</span> <span class="hljs-doctag">&lt;param name=&quot;a&quot;&gt;</span>First operand.<span class="hljs-doctag">&lt;/param&gt;</span></span>
<span class="hljs-comment"><span class="hljs-doctag">///</span> <span class="hljs-doctag">&lt;param name=&quot;b&quot;&gt;</span>Second operand.<span class="hljs-doctag">&lt;/param&gt;</span></span>
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-built_in">int</span> <span class="hljs-title">Add</span>(<span class="hljs-params"><span class="hljs-built_in">int</span> a, <span class="hljs-built_in">int</span> b</span>)</span> =&gt; a + b;
""");
    }

    [Fact]
    public void Comment_XmlDocReturn()
    {
        AssertHighlighter("csharp",
"""
/// <returns>The sum of the two operands.</returns>
public int Add(int a, int b) => a + b;
""",
"""
<span class="hljs-comment"><span class="hljs-doctag">///</span> <span class="hljs-doctag">&lt;returns&gt;</span>The sum of the two operands.<span class="hljs-doctag">&lt;/returns&gt;</span></span>
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-built_in">int</span> <span class="hljs-title">Add</span>(<span class="hljs-params"><span class="hljs-built_in">int</span> a, <span class="hljs-built_in">int</span> b</span>)</span> =&gt; a + b;
""");
    }

    [Fact]
    public void Composite_ModernConsoleApp()
    {
        AssertHighlighter("csharp",
"""
using System;
using System.Linq;

var names = (args is { Length: > 0 } ? args : ["world"]);
foreach (var name in names)
{
    Console.WriteLine($"Hello, {name}!");
}
""",
"""
<span class="hljs-keyword">using</span> System;
<span class="hljs-keyword">using</span> System.Linq;

<span class="hljs-keyword">var</span> names = (<span class="hljs-keyword">args</span> <span class="hljs-keyword">is</span> { Length: &gt; <span class="hljs-number">0</span> } ? <span class="hljs-keyword">args</span> : [<span class="hljs-string">&quot;world&quot;</span>]);
<span class="hljs-keyword">foreach</span> (<span class="hljs-keyword">var</span> name <span class="hljs-keyword">in</span> names)
{
    Console.WriteLine(<span class="hljs-string">$&quot;Hello, <span class="hljs-subst">{name}</span>!&quot;</span>);
}
""");
    }

    [Fact]
    public void Composite_AsyncWebClient()
    {
        AssertHighlighter("csharp",
"""
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace MyApp;

public sealed class ApiClient(HttpClient http)
{
    public async Task<string> FetchAsync(string url, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
""",
"""
<span class="hljs-keyword">using</span> System;
<span class="hljs-keyword">using</span> System.Net.Http;
<span class="hljs-keyword">using</span> System.Threading.Tasks;

<span class="hljs-keyword">namespace</span> <span class="hljs-title">MyApp</span>;

<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">sealed</span> <span class="hljs-keyword">class</span> <span class="hljs-title">ApiClient</span>(<span class="hljs-params">HttpClient http</span>)</span>
{
    <span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">async</span> Task&lt;<span class="hljs-built_in">string</span>&gt; <span class="hljs-title">FetchAsync</span>(<span class="hljs-params"><span class="hljs-built_in">string</span> url, CancellationToken ct = <span class="hljs-literal">default</span></span>)</span>
    {
        <span class="hljs-keyword">using</span> <span class="hljs-keyword">var</span> response = <span class="hljs-keyword">await</span> http.GetAsync(url, ct).ConfigureAwait(<span class="hljs-literal">false</span>);
        response.EnsureSuccessStatusCode();
        <span class="hljs-keyword">return</span> <span class="hljs-keyword">await</span> response.Content.ReadAsStringAsync(ct).ConfigureAwait(<span class="hljs-literal">false</span>);
    }
}
""");
    }

    [Fact]
    public void Composite_RecordWithPatterns()
    {
        AssertHighlighter("csharp",
"""
public record Shape;
public record Circle(double Radius) : Shape;
public record Square(double Side) : Shape;
public record Triangle(double Base, double Height) : Shape;

public static double Area(Shape s) => s switch
{
    Circle   { Radius: var r }              => Math.PI * r * r,
    Square   { Side: var x }                => x * x,
    Triangle { Base: var b, Height: var h } => b * h / 2,
    _                                       => throw new ArgumentOutOfRangeException(nameof(s))
};
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">record</span> <span class="hljs-title">Shape</span>;
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">record</span> <span class="hljs-title">Circle</span>(<span class="hljs-params"><span class="hljs-built_in">double</span> Radius</span>) : Shape</span>;
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">record</span> <span class="hljs-title">Square</span>(<span class="hljs-params"><span class="hljs-built_in">double</span> Side</span>) : Shape</span>;
<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">record</span> <span class="hljs-title">Triangle</span>(<span class="hljs-params"><span class="hljs-built_in">double</span> Base, <span class="hljs-built_in">double</span> Height</span>) : Shape</span>;

<span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> <span class="hljs-built_in">double</span> <span class="hljs-title">Area</span>(<span class="hljs-params">Shape s</span>)</span> =&gt; s <span class="hljs-keyword">switch</span>
{
    Circle   { Radius: <span class="hljs-keyword">var</span> r }              =&gt; Math.PI * r * r,
    Square   { Side: <span class="hljs-keyword">var</span> x }                =&gt; x * x,
    Triangle { Base: <span class="hljs-keyword">var</span> b, Height: <span class="hljs-keyword">var</span> h } =&gt; b * h / <span class="hljs-number">2</span>,
    _                                       =&gt; <span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> ArgumentOutOfRangeException(<span class="hljs-keyword">nameof</span>(s))
};
""");
    }

    [Fact]
    public void Composite_GenericMath()
    {
        AssertHighlighter("csharp",
"""
public static class Stats
{
    public static T Sum<T>(IEnumerable<T> values) where T : INumber<T>
    {
        T total = T.Zero;
        foreach (var v in values) total += v;
        return total;
    }
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Stats</span>
{
    <span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> T <span class="hljs-title">Sum</span>&lt;<span class="hljs-title">T</span>&gt;(<span class="hljs-params">IEnumerable&lt;T&gt; values</span>) <span class="hljs-keyword">where</span> T : INumber&lt;T&gt;</span>
    {
        T total = T.Zero;
        <span class="hljs-keyword">foreach</span> (<span class="hljs-keyword">var</span> v <span class="hljs-keyword">in</span> values) total += v;
        <span class="hljs-keyword">return</span> total;
    }
}
""");
    }

    [Fact]
    public void Composite_JsonRecord()
    {
        AssertHighlighter("csharp",
"""
using System.Text.Json.Serialization;

public sealed record User
{
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("active")]
    public bool IsActive { get; init; } = true;
}
""",
"""
<span class="hljs-keyword">using</span> System.Text.Json.Serialization;

<span class="hljs-keyword">public</span> <span class="hljs-keyword">sealed</span> <span class="hljs-keyword">record</span> <span class="hljs-title">User</span>
{
    [<span class="hljs-meta">JsonPropertyName(<span class="hljs-string">&quot;id&quot;</span>)</span>]
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">required</span> Guid Id { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">init</span>; }

    [<span class="hljs-meta">JsonPropertyName(<span class="hljs-string">&quot;name&quot;</span>)</span>]
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">required</span> <span class="hljs-built_in">string</span> Name { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">init</span>; }

    [<span class="hljs-meta">JsonPropertyName(<span class="hljs-string">&quot;active&quot;</span>)</span>]
    <span class="hljs-keyword">public</span> <span class="hljs-built_in">bool</span> IsActive { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">init</span>; } = <span class="hljs-literal">true</span>;
}
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("csharp",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("csharp",
"""
// nothing here
""",
"""
<span class="hljs-comment">// nothing here</span>
""");
    }

    [Fact]
    public void SpecialEdge_OnlyUsing()
    {
        AssertHighlighter("csharp",
"""
using System;
""",
"""
<span class="hljs-keyword">using</span> System;
""");
    }

    [Fact]
    public void SpecialEdge_OnlyNamespace()
    {
        AssertHighlighter("csharp",
"""
namespace MyApp;
""",
"""
<span class="hljs-keyword">namespace</span> <span class="hljs-title">MyApp</span>;
""");
    }

    [Fact]
    public void SpecialEdge_TrailingNewline()
    {
        AssertHighlighter("csharp",
"""
class Foo { }

""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title">Foo</span> { }

""");
    }
}
