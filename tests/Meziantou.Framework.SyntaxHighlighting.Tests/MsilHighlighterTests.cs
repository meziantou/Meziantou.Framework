namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class MsilHighlighterTests
{
    [Fact]
    public void Assembly()
    {
        AssertHighlighter("msil",
"""
.assembly _
{
    .hash algorithm 0x00008004 // SHA1
    .ver 0:0:0:0
}
""",
"""
<span class="hljs-meta">.assembly</span> _
{
    <span class="hljs-meta">.hash</span> <span class="hljs-meta">algorithm</span> <span class="hljs-number">0x00008004</span> <span class="hljs-comment">// SHA1</span>
    <span class="hljs-meta">.ver</span> <span class="hljs-number">0</span>:<span class="hljs-number">0</span>:<span class="hljs-number">0</span>:<span class="hljs-number">0</span>
}
""");
    }

    [Fact]
    public void MethodBody()
    {
        AssertHighlighter("il",
"""
.method public hidebysig specialname static
    int32 get_Counter () cil managed
{
    .maxstack 8

    IL_0000: ldsfld int32 ISample::'<Counter>k__BackingField'
    IL_0005: ret
}
""",
"""
<span class="hljs-meta">.method</span> <span class="hljs-keyword">public</span> <span class="hljs-keyword">hidebysig</span> <span class="hljs-keyword">specialname</span> <span class="hljs-keyword">static</span>
    <span class="hljs-built_in">int32</span> get_Counter () <span class="hljs-keyword">cil</span> <span class="hljs-keyword">managed</span>
{
    <span class="hljs-meta">.maxstack</span> <span class="hljs-number">8</span>

    <span class="hljs-symbol">IL_0000:</span> <span class="hljs-keyword">ldsfld</span> <span class="hljs-built_in">int32</span> ISample::<span class="hljs-string">&#x27;&lt;Counter&gt;k__BackingField&#x27;</span>
    <span class="hljs-symbol">IL_0005:</span> <span class="hljs-keyword">ret</span>
}
""");
    }

    [Fact]
    public void CustomAttributeBlob()
    {
        AssertHighlighter("cil",
"""
.custom instance void [System.Runtime]System.Diagnostics.DebuggableAttribute::.ctor(valuetype [System.Runtime]System.Diagnostics.DebuggableAttribute/DebuggingModes) = (
    01 00 07 01 00 00 00 00
)
""",
"""
<span class="hljs-meta">.custom</span> <span class="hljs-keyword">instance</span> <span class="hljs-built_in">void</span> [System.Runtime]System.Diagnostics.DebuggableAttribute::<span class="hljs-meta">.ctor</span>(<span class="hljs-keyword">valuetype</span> [System.Runtime]System.Diagnostics.DebuggableAttribute/DebuggingModes) = (
    <span class="hljs-number">01</span> <span class="hljs-number">00</span> <span class="hljs-number">07</span> <span class="hljs-number">01</span> <span class="hljs-number">00</span> <span class="hljs-number">00</span> <span class="hljs-number">00</span> <span class="hljs-number">00</span>
)
""");
    }

    [Fact]
    public void LocalsAndExceptionHandling()
    {
        AssertHighlighter("msil",
"""
.method public static int32 Parse(string s) cil managed
{
    .maxstack 2
    .locals init ([0] int32 result)
    .try
    {
        ldarg.0
        call int32 [System.Runtime]System.Int32::Parse(string)
        stloc.0
        leave.s IL_0010
    }
    catch [System.Runtime]System.FormatException
    {
        pop
        ldc.i4.m1
        stloc.0
        leave.s IL_0010
    }
    IL_0010: ldloc.0
    ret
}
""",
"""
<span class="hljs-meta">.method</span> <span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> <span class="hljs-built_in">int32</span> Parse(<span class="hljs-built_in">string</span> s) <span class="hljs-keyword">cil</span> <span class="hljs-keyword">managed</span>
{
    <span class="hljs-meta">.maxstack</span> <span class="hljs-number">2</span>
    <span class="hljs-meta">.locals</span> <span class="hljs-keyword">init</span> ([<span class="hljs-number">0</span>] <span class="hljs-built_in">int32</span> result)
    <span class="hljs-meta">.try</span>
    {
        <span class="hljs-keyword">ldarg.0</span>
        <span class="hljs-keyword">call</span> <span class="hljs-built_in">int32</span> [System.Runtime]System.Int32::Parse(<span class="hljs-built_in">string</span>)
        <span class="hljs-keyword">stloc.0</span>
        <span class="hljs-keyword">leave.s</span> IL_0010
    }
    catch [System.Runtime]System.FormatException
    {
        <span class="hljs-keyword">pop</span>
        <span class="hljs-keyword">ldc.i4.m1</span>
        <span class="hljs-keyword">stloc.0</span>
        <span class="hljs-keyword">leave.s</span> IL_0010
    }
    <span class="hljs-symbol">IL_0010:</span> <span class="hljs-keyword">ldloc.0</span>
    <span class="hljs-keyword">ret</span>
}
""");
    }

    [Fact]
    public void LabelsAndBranches()
    {
        AssertHighlighter("msil",
"""
IL_0000: ldarg.0
IL_0001: brfalse.s IL_0007
IL_0003: ldc.i4.1
IL_0004: ret
IL_0007: ldc.i4.0 // false
IL_0008: ret
""",
"""
<span class="hljs-symbol">IL_0000:</span> <span class="hljs-keyword">ldarg.0</span>
<span class="hljs-symbol">IL_0001:</span> <span class="hljs-keyword">brfalse.s</span> IL_0007
<span class="hljs-symbol">IL_0003:</span> <span class="hljs-keyword">ldc.i4.1</span>
<span class="hljs-symbol">IL_0004:</span> <span class="hljs-keyword">ret</span>
<span class="hljs-symbol">IL_0007:</span> <span class="hljs-keyword">ldc.i4.0</span> <span class="hljs-comment">// false</span>
<span class="hljs-symbol">IL_0008:</span> <span class="hljs-keyword">ret</span>
""");
    }

    [Fact]
    public void FieldsAndProperties()
    {
        AssertHighlighter("msil",
"""
.class public auto ansi beforefieldinit Sample extends [System.Runtime]System.Object
{
    .field private initonly string '<Name>k__BackingField'
    .property instance string Name()
    {
        .get instance string Sample::get_Name()
    }
}
""",
"""
<span class="hljs-meta">.class</span> <span class="hljs-keyword">public</span> <span class="hljs-keyword">auto</span> <span class="hljs-keyword">ansi</span> <span class="hljs-keyword">beforefieldinit</span> Sample <span class="hljs-keyword">extends</span> [System.Runtime]System.Object
{
    <span class="hljs-meta">.field</span> <span class="hljs-keyword">private</span> <span class="hljs-keyword">initonly</span> <span class="hljs-built_in">string</span> <span class="hljs-string">&#x27;&lt;Name&gt;k__BackingField&#x27;</span>
    <span class="hljs-meta">.property</span> <span class="hljs-keyword">instance</span> <span class="hljs-built_in">string</span> Name()
    {
        <span class="hljs-meta">.get</span> <span class="hljs-keyword">instance</span> <span class="hljs-built_in">string</span> Sample::get_Name()
    }
}
""");
    }

    [Fact]
    public void StringsAndNumbers()
    {
        AssertHighlighter("msil",
"""
ldstr "Hello, \"world\""
ldc.i4 0x7FFFFFFF
ldc.r8 3.14
ldc.i4.s -1
""",
"""
<span class="hljs-keyword">ldstr</span> <span class="hljs-string">&quot;Hello, \&quot;world\&quot;&quot;</span>
<span class="hljs-keyword">ldc.i4</span> <span class="hljs-number">0x7FFFFFFF</span>
<span class="hljs-keyword">ldc.r8</span> <span class="hljs-number">3.14</span>
<span class="hljs-keyword">ldc.i4.s</span> <span class="hljs-number">-1</span>
""");
    }
}
