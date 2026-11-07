namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public sealed class WasmHighlighterTests
{
    [Fact]
    public void Module()
    {
        AssertHighlighter("wasm",
"""
(module
  (func $add (param $lhs i32) (param $rhs i32) (result i32)
    local.get $lhs
    local.get $rhs
    i32.add)
  (export "add" (func $add))
)
""",
"""
<span class="hljs-punctuation">(</span><span class="hljs-keyword">module</span>
  <span class="hljs-punctuation">(</span><span class="hljs-keyword">func</span> <span class="hljs-title function_">$add</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">param</span> <span class="hljs-variable">$lhs</span> <span class="hljs-type">i32</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">param</span> <span class="hljs-variable">$rhs</span> <span class="hljs-type">i32</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">result</span> <span class="hljs-type">i32</span><span class="hljs-punctuation">)</span>
    <span class="hljs-keyword">local.get</span> <span class="hljs-variable">$lhs</span>
    <span class="hljs-keyword">local.get</span> <span class="hljs-variable">$rhs</span>
    <span class="hljs-keyword">i32.add</span><span class="hljs-punctuation">)</span>
  <span class="hljs-punctuation">(</span><span class="hljs-keyword">export</span> <span class="hljs-string">&quot;add&quot;</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">func</span> <span class="hljs-title function_">$add</span><span class="hljs-punctuation">))</span>
<span class="hljs-punctuation">)</span>
""");
    }

    [Fact]
    public void Imports()
    {
        AssertHighlighter("wasm",
"""
(module
  (import "console" "log" (func $log (param i32)))
  (memory (export "mem") 1)
  (data (i32.const 0) "Hello, world!\n")
  (global $g (mut i32) (i32.const 42))
  (table 2 funcref)
  (elem (i32.const 0) $f1 $f2)
  (func (export "run")
    i32.const 13
    call $log))
""",
"""
<span class="hljs-punctuation">(</span><span class="hljs-keyword">module</span>
  <span class="hljs-punctuation">(</span><span class="hljs-keyword">import</span> <span class="hljs-string">&quot;console&quot;</span> <span class="hljs-string">&quot;log&quot;</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">func</span> <span class="hljs-title function_">$log</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">param</span> <span class="hljs-type">i32</span><span class="hljs-punctuation">)))</span>
  <span class="hljs-punctuation">(</span><span class="hljs-keyword">memory</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">export</span> <span class="hljs-string">&quot;mem&quot;</span><span class="hljs-punctuation">)</span> <span class="hljs-number">1</span><span class="hljs-punctuation">)</span>
  <span class="hljs-punctuation">(</span><span class="hljs-keyword">data</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">i32.const</span> <span class="hljs-number">0</span><span class="hljs-punctuation">)</span> <span class="hljs-string">&quot;Hello, world!\n&quot;</span><span class="hljs-punctuation">)</span>
  <span class="hljs-punctuation">(</span><span class="hljs-keyword">global</span> <span class="hljs-variable">$g</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">mut</span> <span class="hljs-type">i32</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">i32.const</span> <span class="hljs-number">42</span><span class="hljs-punctuation">))</span>
  <span class="hljs-punctuation">(</span><span class="hljs-keyword">table</span> <span class="hljs-number">2</span> funcref<span class="hljs-punctuation">)</span>
  <span class="hljs-punctuation">(</span><span class="hljs-keyword">elem</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">i32.const</span> <span class="hljs-number">0</span><span class="hljs-punctuation">)</span> <span class="hljs-variable">$f1</span> <span class="hljs-variable">$f2</span><span class="hljs-punctuation">)</span>
  <span class="hljs-punctuation">(</span><span class="hljs-keyword">func</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">export</span> <span class="hljs-string">&quot;run&quot;</span><span class="hljs-punctuation">)</span>
    <span class="hljs-keyword">i32.const</span> <span class="hljs-number">13</span>
    <span class="hljs-keyword">call</span> <span class="hljs-title function_">$log</span><span class="hljs-punctuation">))</span>
""");
    }

    [Fact]
    public void Loop()
    {
        AssertHighlighter("wasm",
"""
(func $fac (param $n i64) (result i64)
  (local $acc i64)
  i64.const 1
  local.set $acc
  (block $done
    (loop $again
      local.get $n
      i64.eqz
      br_if $done
      local.get $acc
      local.get $n
      i64.mul
      local.set $acc
      local.get $n
      i64.const 1
      i64.sub
      local.set $n
      br $again))
  local.get $acc)
""",
"""
<span class="hljs-punctuation">(</span><span class="hljs-keyword">func</span> <span class="hljs-title function_">$fac</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">param</span> <span class="hljs-variable">$n</span> <span class="hljs-type">i64</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">result</span> <span class="hljs-type">i64</span><span class="hljs-punctuation">)</span>
  <span class="hljs-punctuation">(</span><span class="hljs-keyword">local</span> <span class="hljs-variable">$acc</span> <span class="hljs-type">i64</span><span class="hljs-punctuation">)</span>
  <span class="hljs-keyword">i64.const</span> <span class="hljs-number">1</span>
  <span class="hljs-keyword">local.set</span> <span class="hljs-variable">$acc</span>
  <span class="hljs-punctuation">(</span><span class="hljs-keyword">block</span> <span class="hljs-variable">$done</span>
    <span class="hljs-punctuation">(</span><span class="hljs-keyword">loop</span> <span class="hljs-variable">$again</span>
      <span class="hljs-keyword">local.get</span> <span class="hljs-variable">$n</span>
      <span class="hljs-keyword">i64.eqz</span>
      <span class="hljs-keyword">br_if</span> <span class="hljs-variable">$done</span>
      <span class="hljs-keyword">local.get</span> <span class="hljs-variable">$acc</span>
      <span class="hljs-keyword">local.get</span> <span class="hljs-variable">$n</span>
      <span class="hljs-keyword">i64.mul</span>
      <span class="hljs-keyword">local.set</span> <span class="hljs-variable">$acc</span>
      <span class="hljs-keyword">local.get</span> <span class="hljs-variable">$n</span>
      <span class="hljs-keyword">i64.const</span> <span class="hljs-number">1</span>
      <span class="hljs-keyword">i64.sub</span>
      <span class="hljs-keyword">local.set</span> <span class="hljs-variable">$n</span>
      <span class="hljs-keyword">br</span> <span class="hljs-variable">$again</span><span class="hljs-punctuation">))</span>
  <span class="hljs-keyword">local.get</span> <span class="hljs-variable">$acc</span><span class="hljs-punctuation">)</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("wasm",
"""
;; line comment
(; block (; nested ;) comment ;)
(module ;; TODO: fill
)
""",
"""
<span class="hljs-comment">;; line comment</span>
<span class="hljs-comment">(; block <span class="hljs-comment">(; nested ;)</span> comment ;)</span>
<span class="hljs-punctuation">(</span><span class="hljs-keyword">module</span> <span class="hljs-comment">;; <span class="hljs-doctag">TODO:</span> fill</span>
<span class="hljs-punctuation">)</span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("wasm",
"""
(f64.const 0x1.8p3) (f32.const -1.5e10) (i32.const 1_000_000) (f64.const inf) (f64.const nan) (f64.const nan:0x7F) (f64.const 0x1.EFp1) (i64.const 0xFF_FF)
""",
"""
<span class="hljs-punctuation">(</span><span class="hljs-keyword">f64.const</span> <span class="hljs-number">0x1.8p3</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">f32.const</span> <span class="hljs-number">-1.5e10</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">i32.const</span> <span class="hljs-number">1_000_000</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">f64.const</span> <span class="hljs-number">inf</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">f64.const</span> <span class="hljs-number">nan</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">f64.const</span> <span class="hljs-number">nan:0x7F</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">f64.const</span> <span class="hljs-number">0x1.EFp1</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">i64.const</span> <span class="hljs-number">0xFF_FF</span><span class="hljs-punctuation">)</span>
""");
    }

    [Fact]
    public void Memarg()
    {
        AssertHighlighter("wasm",
"""
i32.load offset=4 align=2
i64.store8 offset = 8
""",
"""
<span class="hljs-keyword">i32.load</span> <span class="hljs-keyword">offset</span><span class="hljs-operator">=</span><span class="hljs-number">4</span> <span class="hljs-keyword">align</span><span class="hljs-operator">=</span><span class="hljs-number">2</span>
<span class="hljs-keyword">i64.store8</span> <span class="hljs-keyword">offset</span> <span class="hljs-operator">=</span> <span class="hljs-number">8</span>
""");
    }

    [Fact]
    public void OldNames()
    {
        AssertHighlighter("wasm",
"""
get_local 0
set_local 1
tee_local 2
f32.convert_s/i32
i32.trunc_s/f64
i32.wrap/i64
f64.promote/f32
i32.reinterpret/f32
""",
"""
<span class="hljs-keyword">get_local</span> <span class="hljs-number">0</span>
<span class="hljs-keyword">set_local</span> <span class="hljs-number">1</span>
<span class="hljs-keyword">tee_local</span> <span class="hljs-number">2</span>
<span class="hljs-keyword">f32.convert_s/i32</span>
<span class="hljs-keyword">i32.trunc_s/f64</span>
<span class="hljs-keyword">i32.wrap/i64</span>
<span class="hljs-keyword">f64.promote/f32</span>
<span class="hljs-keyword">i32.reinterpret/f32</span>
""");
    }

    [Fact]
    public void CallIndirect()
    {
        AssertHighlighter("wasm",
"""
call_indirect (type $t) (i32.const 0)
call $foo.bar
func $x
""",
"""
<span class="hljs-keyword">call_indirect</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">type</span> <span class="hljs-variable">$t</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">i32.const</span> <span class="hljs-number">0</span><span class="hljs-punctuation">)</span>
<span class="hljs-keyword">call</span> <span class="hljs-title function_">$foo.bar</span>
<span class="hljs-keyword">func</span> <span class="hljs-title function_">$x</span>
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("wasm",
"""
(data (i32.const 8) "a\"b\\c\n")
"unterminated
next line
""",
"""
<span class="hljs-punctuation">(</span><span class="hljs-keyword">data</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">i32.const</span> <span class="hljs-number">8</span><span class="hljs-punctuation">)</span> <span class="hljs-string">&quot;a\&quot;b\\c\n&quot;</span><span class="hljs-punctuation">)</span>
<span class="hljs-string">&quot;unterminated
next line</span>
""");
    }

    [Fact]
    public void UnterminatedBlock()
    {
        AssertHighlighter("wasm",
"""
(module (; never closed
(func)
""",
"""
<span class="hljs-punctuation">(</span><span class="hljs-keyword">module</span> <span class="hljs-comment">(; never closed
(func)</span>
""");
    }

    [Fact]
    public void Types()
    {
        AssertHighlighter("wasm",
"""
(param i32 i64 f32 f64) (result v128) i32x4.add myi32
""",
"""
<span class="hljs-punctuation">(</span><span class="hljs-keyword">param</span> <span class="hljs-type">i32</span> <span class="hljs-type">i64</span> <span class="hljs-type">f32</span> <span class="hljs-type">f64</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">result</span> v128<span class="hljs-punctuation">)</span> <span class="hljs-type">i32</span>x4.add my<span class="hljs-type">i32</span>
""");
    }

    [Fact]
    public void If()
    {
        AssertHighlighter("wasm",
"""
(if (result i32) (i32.eqz (local.get 0))
  (then (i32.const 1))
  (else (i32.const 2)))
select drop unreachable nop return memory.grow memory.size global.get global.set
""",
"""
<span class="hljs-punctuation">(</span><span class="hljs-keyword">if</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">result</span> <span class="hljs-type">i32</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">i32.eqz</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">local.get</span> <span class="hljs-number">0</span><span class="hljs-punctuation">))</span>
  <span class="hljs-punctuation">(</span><span class="hljs-keyword">then</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">i32.const</span> <span class="hljs-number">1</span><span class="hljs-punctuation">))</span>
  <span class="hljs-punctuation">(</span><span class="hljs-keyword">else</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">i32.const</span> <span class="hljs-number">2</span><span class="hljs-punctuation">)))</span>
<span class="hljs-keyword">select</span> <span class="hljs-keyword">drop</span> <span class="hljs-keyword">unreachable</span> <span class="hljs-keyword">nop</span> <span class="hljs-keyword">return</span> <span class="hljs-keyword">memory.grow</span> <span class="hljs-keyword">memory.size</span> <span class="hljs-keyword">global.get</span> <span class="hljs-keyword">global.set</span>
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("wasm", "", "");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("wasm",
"""
(module (export "héllo" (func $é)))
""",
"""
<span class="hljs-punctuation">(</span><span class="hljs-keyword">module</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">export</span> <span class="hljs-string">&quot;héllo&quot;</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">func</span> <span class="hljs-title function_">$é</span><span class="hljs-punctuation">)))</span>
""");
    }

    [Fact]
    public void AliasWat()
    {
        AssertHighlighter("wat",
"""
(module (func $f (result i32) i32.const 1))
""",
"""
<span class="hljs-punctuation">(</span><span class="hljs-keyword">module</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">func</span> <span class="hljs-title function_">$f</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">result</span> <span class="hljs-type">i32</span><span class="hljs-punctuation">)</span> <span class="hljs-keyword">i32.const</span> <span class="hljs-number">1</span><span class="hljs-punctuation">))</span>
""");
    }

    [Fact]
    public void AliasWast()
    {
        AssertHighlighter("wast",
"""
(assert_return (invoke "add" (i32.const 1) (i32.const 2)) (i32.const 3))
""",
"""
<span class="hljs-punctuation">(</span>assert_return <span class="hljs-punctuation">(</span>invoke <span class="hljs-string">&quot;add&quot;</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">i32.const</span> <span class="hljs-number">1</span><span class="hljs-punctuation">)</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">i32.const</span> <span class="hljs-number">2</span><span class="hljs-punctuation">))</span> <span class="hljs-punctuation">(</span><span class="hljs-keyword">i32.const</span> <span class="hljs-number">3</span><span class="hljs-punctuation">))</span>
""");
    }
}
