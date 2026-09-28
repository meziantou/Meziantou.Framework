namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public sealed class LlvmHighlighterTests
{
    [Fact]
    public void Function()
    {
        AssertHighlighter("llvm",
"""
define i32 @add(i32 %a, i32 %b) {
entry:
  %sum = add nsw i32 %a, %b
  ret i32 %sum
}
""",
"""
<span class="hljs-keyword">define</span> <span class="hljs-type">i32</span> <span class="hljs-title">@add</span>(<span class="hljs-type">i32</span> <span class="hljs-variable">%a</span><span class="hljs-punctuation">,</span> <span class="hljs-type">i32</span> <span class="hljs-variable">%b</span>) {
<span class="hljs-symbol">entry:</span>
  <span class="hljs-variable">%sum</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">add</span> <span class="hljs-keyword">nsw</span> <span class="hljs-type">i32</span> <span class="hljs-variable">%a</span><span class="hljs-punctuation">,</span> <span class="hljs-variable">%b</span>
  <span class="hljs-keyword">ret</span> <span class="hljs-type">i32</span> <span class="hljs-variable">%sum</span>
}
""");
    }

    [Fact]
    public void ModuleHeader()
    {
        AssertHighlighter("llvm",
"""
; ModuleID = 'hello.c'
source_filename = "hello.c"
target datalayout = "e-m:e-p270:32:32-p271:32:32-p272:64:64-i64:64-f80:128-n8:16:32:64-S128"
target triple = "x86_64-pc-linux-gnu"

@.str = private unnamed_addr constant [14 x i8] c"Hello, world!\0A\00", align 1

declare i32 @printf(ptr noundef, ...) #1

define dso_local i32 @main() #0 {
  %1 = alloca i32, align 4
  store i32 0, ptr %1, align 4
  %2 = call i32 (ptr, ...) @printf(ptr noundef @.str)
  ret i32 0
}

attributes #0 = { noinline nounwind optnone uwtable }
""",
"""
<span class="hljs-comment">; ModuleID = &#x27;hello.c&#x27;</span>
source_filename <span class="hljs-operator">=</span> <span class="hljs-string">&quot;hello.c&quot;</span>
<span class="hljs-keyword">target</span> <span class="hljs-keyword">datalayout</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;e-m:e-p270:32:32-p271:32:32-p272:64:64-i64:64-f80:128-n8:16:32:64-S128&quot;</span>
<span class="hljs-keyword">target</span> <span class="hljs-keyword">triple</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;x86_64-pc-linux-gnu&quot;</span>

<span class="hljs-title">@.str</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">private</span> <span class="hljs-keyword">unnamed_addr</span> <span class="hljs-keyword">constant</span> [<span class="hljs-number">14</span> <span class="hljs-keyword">x</span> <span class="hljs-type">i8</span>] <span class="hljs-keyword">c</span><span class="hljs-string">&quot;Hello, world!<span class="hljs-char escape_">\0A</span><span class="hljs-char escape_">\00</span>&quot;</span><span class="hljs-punctuation">,</span> <span class="hljs-keyword">align</span> <span class="hljs-number">1</span>

<span class="hljs-keyword">declare</span> <span class="hljs-type">i32</span> <span class="hljs-title">@printf</span>(<span class="hljs-type">ptr</span> noundef<span class="hljs-punctuation">,</span> ...) <span class="hljs-variable">#1</span>

<span class="hljs-keyword">define</span> dso_local <span class="hljs-type">i32</span> <span class="hljs-title">@main</span>() <span class="hljs-variable">#0</span> {
  <span class="hljs-variable">%1</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">alloca</span> <span class="hljs-type">i32</span><span class="hljs-punctuation">,</span> <span class="hljs-keyword">align</span> <span class="hljs-number">4</span>
  <span class="hljs-keyword">store</span> <span class="hljs-type">i32</span> <span class="hljs-number">0</span><span class="hljs-punctuation">,</span> <span class="hljs-type">ptr</span> <span class="hljs-variable">%1</span><span class="hljs-punctuation">,</span> <span class="hljs-keyword">align</span> <span class="hljs-number">4</span>
  <span class="hljs-variable">%2</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">call</span> <span class="hljs-type">i32</span> (<span class="hljs-type">ptr</span><span class="hljs-punctuation">,</span> ...) <span class="hljs-title">@printf</span>(<span class="hljs-type">ptr</span> noundef <span class="hljs-title">@.str</span>)
  <span class="hljs-keyword">ret</span> <span class="hljs-type">i32</span> <span class="hljs-number">0</span>
}

<span class="hljs-keyword">attributes</span> <span class="hljs-variable">#0</span> <span class="hljs-operator">=</span> { <span class="hljs-keyword">noinline</span> <span class="hljs-keyword">nounwind</span> <span class="hljs-keyword">optnone</span> <span class="hljs-keyword">uwtable</span> }
""");
    }

    [Fact]
    public void Branches()
    {
        AssertHighlighter("llvm",
"""
define void @loop(i32 %n) {
entry:
  br label %loop

loop:
  %i = phi i32 [ 0, %entry ], [ %next, %loop ]
  %next = add i32 %i, 1
  %cond = icmp slt i32 %next, %n
  br i1 %cond, label %loop, label %exit

exit:                                             ; preds = %loop
  ret void
}
""",
"""
<span class="hljs-keyword">define</span> <span class="hljs-type">void</span> <span class="hljs-title">@loop</span>(<span class="hljs-type">i32</span> <span class="hljs-variable">%n</span>) {
<span class="hljs-symbol">entry:</span>
  <span class="hljs-keyword">br</span> <span class="hljs-type">label</span> <span class="hljs-variable">%loop</span>
<span class="hljs-symbol">
loop:</span>
  <span class="hljs-variable">%i</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">phi</span> <span class="hljs-type">i32</span> [ <span class="hljs-number">0</span><span class="hljs-punctuation">,</span> <span class="hljs-variable">%entry</span> ]<span class="hljs-punctuation">,</span> [ <span class="hljs-variable">%next</span><span class="hljs-punctuation">,</span> <span class="hljs-variable">%loop</span> ]
  <span class="hljs-variable">%next</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">add</span> <span class="hljs-type">i32</span> <span class="hljs-variable">%i</span><span class="hljs-punctuation">,</span> <span class="hljs-number">1</span>
  <span class="hljs-variable">%cond</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">icmp</span> <span class="hljs-keyword">slt</span> <span class="hljs-type">i32</span> <span class="hljs-variable">%next</span><span class="hljs-punctuation">,</span> <span class="hljs-variable">%n</span>
  <span class="hljs-keyword">br</span> <span class="hljs-type">i1</span> <span class="hljs-variable">%cond</span><span class="hljs-punctuation">,</span> <span class="hljs-type">label</span> <span class="hljs-variable">%loop</span><span class="hljs-punctuation">,</span> <span class="hljs-type">label</span> <span class="hljs-variable">%exit</span>
<span class="hljs-symbol">
exit:</span>                                             <span class="hljs-comment">; preds = %loop</span>
  <span class="hljs-keyword">ret</span> <span class="hljs-type">void</span>
}
""");
    }

    [Fact]
    public void Metadata()
    {
        AssertHighlighter("llvm",
"""
!llvm.dbg.cu = !{!0}
!0 = distinct !DICompileUnit(language: DW_LANG_C99, file: !1, producer: "clang")
!1 = !DIFile(filename: "a.c", directory: "/tmp")
!2 = !{i32 7, !"Dwarf Version", i32 5}
call void @llvm.dbg.declare(metadata ptr %x, metadata !12, metadata !DIExpression()), !dbg !14
""",
"""
<span class="hljs-title">!llvm.dbg.cu</span> <span class="hljs-operator">=</span> !{<span class="hljs-title">!0</span>}
<span class="hljs-title">!0</span> <span class="hljs-operator">=</span> distinct <span class="hljs-title">!DICompileUnit</span>(language: DW_LANG_C99<span class="hljs-punctuation">,</span> file: <span class="hljs-title">!1</span><span class="hljs-punctuation">,</span> producer: <span class="hljs-string">&quot;clang&quot;</span>)
<span class="hljs-title">!1</span> <span class="hljs-operator">=</span> <span class="hljs-title">!DIFile</span>(filename: <span class="hljs-string">&quot;a.c&quot;</span><span class="hljs-punctuation">,</span> directory: <span class="hljs-string">&quot;/tmp&quot;</span>)
<span class="hljs-title">!2</span> <span class="hljs-operator">=</span> !{<span class="hljs-type">i32</span> <span class="hljs-number">7</span><span class="hljs-punctuation">,</span> !<span class="hljs-string">&quot;Dwarf Version&quot;</span><span class="hljs-punctuation">,</span> <span class="hljs-type">i32</span> <span class="hljs-number">5</span>}
<span class="hljs-keyword">call</span> <span class="hljs-type">void</span> <span class="hljs-title">@llvm.dbg.declare</span>(<span class="hljs-type">metadata</span> <span class="hljs-type">ptr</span> <span class="hljs-variable">%x</span><span class="hljs-punctuation">,</span> <span class="hljs-type">metadata</span> <span class="hljs-title">!12</span><span class="hljs-punctuation">,</span> <span class="hljs-type">metadata</span> <span class="hljs-title">!DIExpression</span>())<span class="hljs-punctuation">,</span> <span class="hljs-title">!dbg</span> <span class="hljs-title">!14</span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("llvm",
"""
%x = fadd double 1.5, 0x3FF0000000000000
%y = fadd float -2.0e10, 1.0E-5
%z = add i64 -42, 7
%h = fadd half 0xH3C00
""",
"""
<span class="hljs-variable">%x</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">fadd</span> <span class="hljs-type">double</span> <span class="hljs-number">1.5</span><span class="hljs-punctuation">,</span> <span class="hljs-number">0x3FF0000000000000</span>
<span class="hljs-variable">%y</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">fadd</span> <span class="hljs-type">float</span> <span class="hljs-number">-2.0e10</span><span class="hljs-punctuation">,</span> <span class="hljs-number">1.0E-5</span>
<span class="hljs-variable">%z</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">add</span> <span class="hljs-type">i64</span> <span class="hljs-number">-42</span><span class="hljs-punctuation">,</span> <span class="hljs-number">7</span>
<span class="hljs-variable">%h</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">fadd</span> <span class="hljs-type">half</span> <span class="hljs-number">0xH3C00</span>
""");
    }

    [Fact]
    public void Types()
    {
        AssertHighlighter("llvm",
"""
%struct.Point = type { i32, i32 }
%v = load <4 x float>, ptr %p, align 16
%g = getelementptr inbounds %struct.Point, ptr %s, i64 0, i32 1
%c = bitcast i8* %p to i32*
%big = alloca x86_fp80
""",
"""
<span class="hljs-variable">%struct.Point</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">type</span> { <span class="hljs-type">i32</span><span class="hljs-punctuation">,</span> <span class="hljs-type">i32</span> }
<span class="hljs-variable">%v</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">load</span> &lt;<span class="hljs-number">4</span> <span class="hljs-keyword">x</span> <span class="hljs-type">float</span>&gt;<span class="hljs-punctuation">,</span> <span class="hljs-type">ptr</span> <span class="hljs-variable">%p</span><span class="hljs-punctuation">,</span> <span class="hljs-keyword">align</span> <span class="hljs-number">16</span>
<span class="hljs-variable">%g</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">getelementptr</span> <span class="hljs-keyword">inbounds</span> <span class="hljs-variable">%struct.Point</span><span class="hljs-punctuation">,</span> <span class="hljs-type">ptr</span> <span class="hljs-variable">%s</span><span class="hljs-punctuation">,</span> <span class="hljs-type">i64</span> <span class="hljs-number">0</span><span class="hljs-punctuation">,</span> <span class="hljs-type">i32</span> <span class="hljs-number">1</span>
<span class="hljs-variable">%c</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">bitcast</span> <span class="hljs-type">i8</span>* <span class="hljs-variable">%p</span> <span class="hljs-keyword">to</span> <span class="hljs-type">i32</span>*
<span class="hljs-variable">%big</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">alloca</span> <span class="hljs-type">x86_fp80</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("llvm",
"""
; comment with TODO: something
; ;
;
  ret void ;
""",
"""
<span class="hljs-comment">; comment with <span class="hljs-doctag">TODO:</span> something</span>
<span class="hljs-comment">; ;</span>
<span class="hljs-comment">;</span>
  <span class="hljs-keyword">ret</span> <span class="hljs-type">void</span> <span class="hljs-comment">;</span>
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("llvm",
"""
@s = constant [6 x i8] c"a\22b\5C\00"
@u = constant [3 x i8] c"unterminated
next line
""",
"""
<span class="hljs-title">@s</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">constant</span> [<span class="hljs-number">6</span> <span class="hljs-keyword">x</span> <span class="hljs-type">i8</span>] <span class="hljs-keyword">c</span><span class="hljs-string">&quot;a<span class="hljs-char escape_">\22</span>b<span class="hljs-char escape_">\5C</span><span class="hljs-char escape_">\00</span>&quot;</span>
<span class="hljs-title">@u</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">constant</span> [<span class="hljs-number">3</span> <span class="hljs-keyword">x</span> <span class="hljs-type">i8</span>] <span class="hljs-keyword">c</span><span class="hljs-string">&quot;unterminated
next line</span>
""");
    }

    [Fact]
    public void Labels()
    {
        AssertHighlighter("llvm",
"""
bb1:
if.then:
  br label %bb1
42:
  br label %42
""",
"""
<span class="hljs-symbol">bb1:</span>
<span class="hljs-symbol">if.then:</span>
  <span class="hljs-keyword">br</span> <span class="hljs-type">label</span> <span class="hljs-variable">%bb1</span>
<span class="hljs-symbol">42:</span>
  <span class="hljs-keyword">br</span> <span class="hljs-type">label</span> <span class="hljs-variable">%42</span>
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("llvm", "", "");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("llvm",
"""
; héllo
@"héllo" = global i32 0
""",
"""
<span class="hljs-comment">; héllo</span>
@<span class="hljs-string">&quot;héllo&quot;</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">global</span> <span class="hljs-type">i32</span> <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void AliasLl()
    {
        AssertHighlighter("ll",
"""
define void @f() {
  ret void
}
""",
"""
<span class="hljs-keyword">define</span> <span class="hljs-type">void</span> <span class="hljs-title">@f</span>() {
  <span class="hljs-keyword">ret</span> <span class="hljs-type">void</span>
}
""");
    }
}
