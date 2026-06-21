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
}
