namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class NimHighlighterTests
{
    [Fact]
    public void Alias()
    {
        AssertHighlighter("nims",
"""
mode = ScriptMode.Verbose
exec "nim c -r main.nim"
""",
"""
mode = <span class="hljs-type">ScriptMode</span>.<span class="hljs-type">Verbose</span>
exec <span class="hljs-string">&quot;nim c -r main.nim&quot;</span>
""");
    }

    [Fact]
    public void Async()
    {
        AssertHighlighter("nim",
"""
import asyncdispatch, httpclient

proc fetch(url: string): Future[string] {.async.} =
  let client = newAsyncHttpClient()
  try:
    result = await client.getContent(url)
  except CatchableError as e:
    echo "error: ", e.msg
  finally:
    client.close()

waitFor fetch("https://nim-lang.org")
""",
"""
<span class="hljs-keyword">import</span> asyncdispatch, httpclient

<span class="hljs-keyword">proc</span> fetch(url: <span class="hljs-type">string</span>): <span class="hljs-type">Future</span>[<span class="hljs-type">string</span>] <span class="hljs-meta">{.async.}</span> =
  <span class="hljs-keyword">let</span> client = newAsyncHttpClient()
  <span class="hljs-keyword">try</span>:
    <span class="hljs-built_in">result</span> = await client.getContent(url)
  <span class="hljs-keyword">except</span> <span class="hljs-type">CatchableError</span> <span class="hljs-keyword">as</span> e:
    echo <span class="hljs-string">&quot;error: &quot;</span>, e.msg
  <span class="hljs-keyword">finally</span>:
    client.close()

waitFor fetch(<span class="hljs-string">&quot;https://nim-lang.org&quot;</span>)
""");
    }

    [Fact]
    public void Chars()
    {
        AssertHighlighter("nim",
"""
let q = '"'
let nl = '\n'
let hex = '\x41'
let bs = '\\'
let ap = '\''
let s = "after"
for i in 1..10: echo i
let f = 2.5e-3'f32
let g = 1_000.000_1
let h = 6.02E+23
""",
"""
<span class="hljs-keyword">let</span> q = <span class="hljs-string">&#x27;&quot;&#x27;</span>
<span class="hljs-keyword">let</span> nl = <span class="hljs-string">&#x27;\n&#x27;</span>
<span class="hljs-keyword">let</span> hex = <span class="hljs-string">&#x27;\x41&#x27;</span>
<span class="hljs-keyword">let</span> bs = <span class="hljs-string">&#x27;\\&#x27;</span>
<span class="hljs-keyword">let</span> ap = <span class="hljs-string">&#x27;\&#x27;&#x27;</span>
<span class="hljs-keyword">let</span> s = <span class="hljs-string">&quot;after&quot;</span>
<span class="hljs-keyword">for</span> i <span class="hljs-keyword">in</span> <span class="hljs-number">1</span>..<span class="hljs-number">10</span>: echo i
<span class="hljs-keyword">let</span> f = <span class="hljs-number">2.5e-3&#x27;f32</span>
<span class="hljs-keyword">let</span> g = <span class="hljs-number">1_000.000_1</span>
<span class="hljs-keyword">let</span> h = <span class="hljs-number">6.02E+23</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("nim",
"""
# line comment
## doc comment
#[ block
   comment #[ nested ]#
   still comment ]#
let x = 1 # trailing
#[ unterminated block
let y = 2
""",
"""
<span class="hljs-comment"># line comment</span>
<span class="hljs-comment">## doc comment</span>
<span class="hljs-comment">#[ block
   comment <span class="hljs-comment">#[ nested ]#</span>
   still comment ]#</span>
<span class="hljs-keyword">let</span> x = <span class="hljs-number">1</span> <span class="hljs-comment"># trailing</span>
<span class="hljs-comment">#[ unterminated block
let y = 2</span>
""");
    }

    [Fact]
    public void Edge()
    {
        AssertHighlighter("nim",
""""
x"""y"""z
abc"def"
"""
unterminated triple
"""",
"""
<span class="hljs-string">x&quot;&quot;&quot;y&quot;&quot;&quot;</span>z
<span class="hljs-string">abc&quot;def&quot;</span>
<span class="hljs-string">&quot;&quot;&quot;
unterminated triple</span>
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("nim", "", "");
    }

    [Fact]
    public void Hello()
    {
        AssertHighlighter("nim",
"""
echo "Hello, World!"
let name = readLine(stdin)
echo "Hi, ", name, "!"
""",
"""
echo <span class="hljs-string">&quot;Hello, World!&quot;</span>
<span class="hljs-keyword">let</span> name = readLine(<span class="hljs-built_in">stdin</span>)
echo <span class="hljs-string">&quot;Hi, &quot;</span>, name, <span class="hljs-string">&quot;!&quot;</span>
""");
    }

    [Fact]
    public void Macro()
    {
        AssertHighlighter("nim",
"""
macro debug(args: varargs[untyped]): untyped =
  result = nnkStmtList.newTree()
  for n in args:
    result.add newCall("echo", newLit(n.repr))

template `!=`(a, b: untyped): untyped =
  not (a == b)
""",
"""
<span class="hljs-keyword">macro</span> debug(args: <span class="hljs-type">varargs</span>[untyped]): untyped =
  <span class="hljs-built_in">result</span> = nnkStmtList.newTree()
  <span class="hljs-keyword">for</span> n <span class="hljs-keyword">in</span> args:
    <span class="hljs-built_in">result</span>.add newCall(<span class="hljs-string">&quot;echo&quot;</span>, newLit(n.repr))

<span class="hljs-keyword">template</span> `!=`(a, b: untyped): untyped =
  <span class="hljs-keyword">not</span> (a == b)
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("nim",
"""
let a = 42
let b = 1_000_000'i64
let c = 0xFF'u8
let d = 0o777
let e = 0b1010_1010
let f = 3.14
let g = 1e10
let h = 12'f32
let i = 0XdeadBEEF
""",
"""
<span class="hljs-keyword">let</span> a = <span class="hljs-number">42</span>
<span class="hljs-keyword">let</span> b = <span class="hljs-number">1_000_000&#x27;i64</span>
<span class="hljs-keyword">let</span> c = <span class="hljs-number">0xFF&#x27;u8</span>
<span class="hljs-keyword">let</span> d = <span class="hljs-number">0o777</span>
<span class="hljs-keyword">let</span> e = <span class="hljs-number">0b1010_1010</span>
<span class="hljs-keyword">let</span> f = <span class="hljs-number">3.14</span>
<span class="hljs-keyword">let</span> g = <span class="hljs-number">1e10</span>
<span class="hljs-keyword">let</span> h = <span class="hljs-number">12&#x27;f32</span>
<span class="hljs-keyword">let</span> i = <span class="hljs-number">0XdeadBEEF</span>
""");
    }

    [Fact]
    public void Pragmas()
    {
        AssertHighlighter("nim",
""""
{.push checks: off.}
proc fast() {.noSideEffect, gcsafe, raises: [].} = discard
{.pop.}
{.emit: """/* C code */""".}
type Foo {.pure, final.} = ref object of RootObj
"""",
"""
<span class="hljs-meta">{.push checks: off.}</span>
<span class="hljs-keyword">proc</span> fast() <span class="hljs-meta">{.noSideEffect, gcsafe, raises: [].}</span> = <span class="hljs-keyword">discard</span>
<span class="hljs-meta">{.pop.}</span>
<span class="hljs-meta">{.emit: &quot;&quot;&quot;/* C code */&quot;&quot;&quot;.}</span>
<span class="hljs-keyword">type</span> <span class="hljs-type">Foo</span> <span class="hljs-meta">{.pure, final.}</span> = <span class="hljs-keyword">ref</span> <span class="hljs-keyword">object</span> <span class="hljs-keyword">of</span> <span class="hljs-type">RootObj</span>
""");
    }

    [Fact]
    public void Proc()
    {
        AssertHighlighter("nim",
"""
import std/[strutils, sequtils]

type
  Person = object
    name: string
    age: Natural

proc greet(p: Person): string {.inline.} =
  ## Returns a greeting.
  result = "Hello, " & p.name

func add*(a, b: int): int = a + b

iterator countTo(n: int): int =
  var i = 0
  while i <= n:
    yield i
    inc i

when isMainModule:
  let p = Person(name: "Nim", age: 15)
  echo greet(p)
  for x in countTo(3): echo x
""",
"""
<span class="hljs-keyword">import</span> std/[strutils, sequtils]

<span class="hljs-keyword">type</span>
  <span class="hljs-type">Person</span> = <span class="hljs-keyword">object</span>
    name: <span class="hljs-type">string</span>
    age: <span class="hljs-type">Natural</span>

<span class="hljs-keyword">proc</span> greet(p: <span class="hljs-type">Person</span>): <span class="hljs-type">string</span> <span class="hljs-meta">{.inline.}</span> =
  <span class="hljs-comment">## Returns a greeting.</span>
  <span class="hljs-built_in">result</span> = <span class="hljs-string">&quot;Hello, &quot;</span> &amp; p.name

<span class="hljs-keyword">func</span> add*(a, b: <span class="hljs-type">int</span>): <span class="hljs-type">int</span> = a + b

<span class="hljs-keyword">iterator</span> countTo(n: <span class="hljs-type">int</span>): <span class="hljs-type">int</span> =
  <span class="hljs-keyword">var</span> i = <span class="hljs-number">0</span>
  <span class="hljs-keyword">while</span> i &lt;= n:
    <span class="hljs-keyword">yield</span> i
    inc i

<span class="hljs-keyword">when</span> isMainModule:
  <span class="hljs-keyword">let</span> p = <span class="hljs-type">Person</span>(name: <span class="hljs-string">&quot;Nim&quot;</span>, age: <span class="hljs-number">15</span>)
  echo greet(p)
  <span class="hljs-keyword">for</span> x <span class="hljs-keyword">in</span> countTo(<span class="hljs-number">3</span>): echo x
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("nim",
""""
let raw = r"C:\path\to""file"
let triple = """multi
line "quoted" string"""
let fmtd = fmt"x = {x}"
let rawTriple = r"""raw
triple"""
let esc = "tab\tnew\nline \"q\""
let ch = 'a'
let unterminated = "abc
let next = 1
"""",
"""
<span class="hljs-keyword">let</span> raw = <span class="hljs-string">r&quot;C:\path\to&quot;&quot;file&quot;</span>
<span class="hljs-keyword">let</span> triple = <span class="hljs-string">&quot;&quot;&quot;multi
line &quot;quoted&quot; string&quot;&quot;&quot;</span>
<span class="hljs-keyword">let</span> fmtd = <span class="hljs-string">fmt&quot;x = {x}&quot;</span>
<span class="hljs-keyword">let</span> rawTriple = <span class="hljs-string">r&quot;&quot;&quot;raw
triple&quot;&quot;&quot;</span>
<span class="hljs-keyword">let</span> esc = <span class="hljs-string">&quot;tab\tnew\nline \&quot;q\&quot;&quot;</span>
<span class="hljs-keyword">let</span> ch = <span class="hljs-string">&#x27;a&#x27;</span>
<span class="hljs-keyword">let</span> unterminated = <span class="hljs-string">&quot;abc
let next = 1</span>
""");
    }

    [Fact]
    public void Types()
    {
        AssertHighlighter("nim",
"""
var s: seq[string] = @[]
var t: array[3, int]
var o: openarray[float64]
var tbl = initTable[string, int]()
type Color = enum red, green, blue
type Shape = concept x
  x.area is float
defer: close(f)
discard cast[ptr int](addr x)
""",
"""
<span class="hljs-keyword">var</span> s: <span class="hljs-type">seq</span>[<span class="hljs-type">string</span>] = @[]
<span class="hljs-keyword">var</span> t: <span class="hljs-type">array</span>[<span class="hljs-number">3</span>, <span class="hljs-type">int</span>]
<span class="hljs-keyword">var</span> o: <span class="hljs-type">openarray</span>[<span class="hljs-type">float64</span>]
<span class="hljs-keyword">var</span> tbl = initTable[<span class="hljs-type">string</span>, <span class="hljs-type">int</span>]()
<span class="hljs-keyword">type</span> <span class="hljs-type">Color</span> = <span class="hljs-keyword">enum</span> red, green, blue
<span class="hljs-keyword">type</span> <span class="hljs-type">Shape</span> = <span class="hljs-keyword">concept</span> x
  x.area <span class="hljs-keyword">is</span> <span class="hljs-type">float</span>
<span class="hljs-keyword">defer</span>: close(f)
<span class="hljs-keyword">discard</span> <span class="hljs-keyword">cast</span>[<span class="hljs-keyword">ptr</span> <span class="hljs-type">int</span>](<span class="hljs-keyword">addr</span> x)
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("nim",
"""
let π = 3.14 # grec
echo "héllo wörld ✓"
let Ünïcode = 1
""",
"""
<span class="hljs-keyword">let</span> π = <span class="hljs-number">3.14</span> <span class="hljs-comment"># grec</span>
echo <span class="hljs-string">&quot;héllo wörld ✓&quot;</span>
<span class="hljs-keyword">let</span> Ünïcode = <span class="hljs-number">1</span>
""");
    }
}
