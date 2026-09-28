namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class GoHighlighterTests
{
    [Fact]
    public void HelloWorld()
    {
        AssertHighlighter("go",
"""
package main

import "fmt"

func main() {
    fmt.Println("Hello, world!")
}
""",
"""
<span class="hljs-keyword">package</span> main

<span class="hljs-keyword">import</span> <span class="hljs-string">&quot;fmt&quot;</span>

<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-title">main</span><span class="hljs-params">()</span></span> {
    fmt.Println(<span class="hljs-string">&quot;Hello, world!&quot;</span>)
}
""");
    }

    [Fact]
    public void Imports()
    {
        AssertHighlighter("go",
"""
import (
    "context"
    str "strings"
    _ "embed"
)
""",
"""
<span class="hljs-keyword">import</span> (
    <span class="hljs-string">&quot;context&quot;</span>
    str <span class="hljs-string">&quot;strings&quot;</span>
    _ <span class="hljs-string">&quot;embed&quot;</span>
)
""");
    }

    [Fact]
    public void Variables()
    {
        AssertHighlighter("go",
"""
var x int = 5
var (
    a, b = 1, 2
    name string
)
y := x + 1
""",
"""
<span class="hljs-keyword">var</span> x <span class="hljs-type">int</span> = <span class="hljs-number">5</span>
<span class="hljs-keyword">var</span> (
    a, b = <span class="hljs-number">1</span>, <span class="hljs-number">2</span>
    name <span class="hljs-type">string</span>
)
y := x + <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Constants_Iota()
    {
        AssertHighlighter("go",
"""
const (
    A = iota
    B
    C
)
const Pi float64 = 3.14159
""",
"""
<span class="hljs-keyword">const</span> (
    A = <span class="hljs-literal">iota</span>
    B
    C
)
<span class="hljs-keyword">const</span> Pi <span class="hljs-type">float64</span> = <span class="hljs-number">3.14159</span>
""");
    }

    [Fact]
    public void Literals()
    {
        AssertHighlighter("go",
"""
ok := true || false
var p *int = nil
""",
"""
ok := <span class="hljs-literal">true</span> || <span class="hljs-literal">false</span>
<span class="hljs-keyword">var</span> p *<span class="hljs-type">int</span> = <span class="hljs-literal">nil</span>
""");
    }

    [Fact]
    public void Numbers_Decimal()
    {
        AssertHighlighter("go",
"""
a := 42
b := 1_000_000
c := 0755
d := -7
""",
"""
a := <span class="hljs-number">42</span>
b := <span class="hljs-number">1_000_000</span>
c := <span class="hljs-number">0755</span>
d := <span class="hljs-number">-7</span>
""");
    }

    [Fact]
    public void Numbers_Float()
    {
        AssertHighlighter("go",
"""
a := 3.14
b := 1.
c := .5
d := 1e10
e := 6.022_140e+23
f := 1E-3
""",
"""
a := <span class="hljs-number">3.14</span>
b := <span class="hljs-number">1.</span>
c := <span class="hljs-number">.5</span>
d := <span class="hljs-number">1e10</span>
e := <span class="hljs-number">6.022_140e+23</span>
f := <span class="hljs-number">1E-3</span>
""");
    }

    [Fact]
    public void Numbers_Hex()
    {
        AssertHighlighter("go",
"""
a := 0xFF
b := 0X_1F
c := 0x1p-2
d := 0x1.8p1
e := 0x.8p0
""",
"""
a := <span class="hljs-number">0xFF</span>
b := <span class="hljs-number">0X_1F</span>
c := <span class="hljs-number">0x1p-2</span>
d := <span class="hljs-number">0x1.8p1</span>
e := <span class="hljs-number">0x.8p0</span>
""");
    }

    [Fact]
    public void Numbers_Octal()
    {
        AssertHighlighter("go",
"""
a := 0o755
b := 0O17
""",
"""
a := <span class="hljs-number">0o755</span>
b := <span class="hljs-number">0O17</span>
""");
    }

    [Fact]
    public void Numbers_Binary()
    {
        AssertHighlighter("go",
"""
a := 0b1010
b := 0B_1111_0000
""",
"""
a := <span class="hljs-number">0b1010</span>
b := <span class="hljs-number">0B_1111_0000</span>
""");
    }

    [Fact]
    public void Numbers_Imaginary()
    {
        AssertHighlighter("go",
"""
a := 1i
b := 2.5i
c := 0x1p2i
d := 1e3i
""",
"""
a := <span class="hljs-number">1i</span>
b := <span class="hljs-number">2.5i</span>
c := <span class="hljs-number">0x1p2i</span>
d := <span class="hljs-number">1e3i</span>
""");
    }

    [Fact]
    public void Strings_Interpreted()
    {
        AssertHighlighter("go",
"""
s := "tab\there \"quoted\" \u00e9"
""",
"""
s := <span class="hljs-string">&quot;tab\there \&quot;quoted\&quot; \u00e9&quot;</span>
""");
    }

    [Fact]
    public void Strings_Raw()
    {
        AssertHighlighter("go",
"""
s := `C:\path\no\escape`
t := `line 1
line 2 "quoted"`
""",
"""
s := <span class="hljs-string">`C:\path\no\escape`</span>
t := <span class="hljs-string">`line 1
line 2 &quot;quoted&quot;`</span>
""");
    }

    [Fact]
    public void Runes()
    {
        AssertHighlighter("go",
"""
r := 'a'
n := '\n'
q := '\''
u := '\u00e9'
""",
"""
r := <span class="hljs-string">&#x27;a&#x27;</span>
n := <span class="hljs-string">&#x27;\n&#x27;</span>
q := <span class="hljs-string">&#x27;\&#x27;&#x27;</span>
u := <span class="hljs-string">&#x27;\u00e9&#x27;</span>
""");
    }

    [Fact]
    public void Comments_Line()
    {
        AssertHighlighter("go",
"""
// Package foo does things.
x := 1 // trailing
""",
"""
<span class="hljs-comment">// Package foo does things.</span>
x := <span class="hljs-number">1</span> <span class="hljs-comment">// trailing</span>
""");
    }

    [Fact]
    public void Comments_Block()
    {
        AssertHighlighter("go",
"""
/* block
   comment */
x := /* inline */ 1
""",
"""
<span class="hljs-comment">/* block
   comment */</span>
x := <span class="hljs-comment">/* inline */</span> <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Comments_DocTag()
    {
        AssertHighlighter("go",
"""
// TODO: remove this
/* FIXME: and this */
""",
"""
<span class="hljs-comment">// <span class="hljs-doctag">TODO:</span> remove this</span>
<span class="hljs-comment">/* <span class="hljs-doctag">FIXME:</span> and this */</span>
""");
    }

    [Fact]
    public void Function_Params()
    {
        AssertHighlighter("go",
"""
func add(a, b int) int {
    return a + b
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-title">add</span><span class="hljs-params">(a, b <span class="hljs-type">int</span>)</span></span> <span class="hljs-type">int</span> {
    <span class="hljs-keyword">return</span> a + b
}
""");
    }

    [Fact]
    public void Function_MultipleReturns()
    {
        AssertHighlighter("go",
"""
func divide(a, b float64) (float64, error) {
    if b == 0 {
        return 0, errors.New("division by zero")
    }
    return a / b, nil
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-title">divide</span><span class="hljs-params">(a, b <span class="hljs-type">float64</span>)</span></span> (<span class="hljs-type">float64</span>, <span class="hljs-type">error</span>) {
    <span class="hljs-keyword">if</span> b == <span class="hljs-number">0</span> {
        <span class="hljs-keyword">return</span> <span class="hljs-number">0</span>, errors.New(<span class="hljs-string">&quot;division by zero&quot;</span>)
    }
    <span class="hljs-keyword">return</span> a / b, <span class="hljs-literal">nil</span>
}
""");
    }

    [Fact]
    public void Function_NamedReturns()
    {
        AssertHighlighter("go",
"""
func split(sum int) (x, y int) {
    x = sum * 4 / 9
    y = sum - x
    return
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-title">split</span><span class="hljs-params">(sum <span class="hljs-type">int</span>)</span></span> (x, y <span class="hljs-type">int</span>) {
    x = sum * <span class="hljs-number">4</span> / <span class="hljs-number">9</span>
    y = sum - x
    <span class="hljs-keyword">return</span>
}
""");
    }

    [Fact]
    public void Function_Variadic()
    {
        AssertHighlighter("go",
"""
func sum(nums ...int) int {
    total := 0
    for _, n := range nums {
        total += n
    }
    return total
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-title">sum</span><span class="hljs-params">(nums ...<span class="hljs-type">int</span>)</span></span> <span class="hljs-type">int</span> {
    total := <span class="hljs-number">0</span>
    <span class="hljs-keyword">for</span> _, n := <span class="hljs-keyword">range</span> nums {
        total += n
    }
    <span class="hljs-keyword">return</span> total
}
""");
    }

    [Fact]
    public void Method_PointerReceiver()
    {
        AssertHighlighter("go",
"""
func (s *Server) Start(ctx context.Context) error {
    return nil
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-params">(s *Server)</span> <span class="hljs-title">Start</span><span class="hljs-params">(ctx context.Context)</span></span> <span class="hljs-type">error</span> {
    <span class="hljs-keyword">return</span> <span class="hljs-literal">nil</span>
}
""");
    }

    [Fact]
    public void Method_ValueReceiver()
    {
        AssertHighlighter("go",
"""
func (p Point) String() string {
    return fmt.Sprintf("(%d, %d)", p.X, p.Y)
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-params">(p Point)</span> <span class="hljs-title">String</span><span class="hljs-params">()</span></span> <span class="hljs-type">string</span> {
    <span class="hljs-keyword">return</span> fmt.Sprintf(<span class="hljs-string">&quot;(%d, %d)&quot;</span>, p.X, p.Y)
}
""");
    }

    [Fact]
    public void Method_GenericReceiver()
    {
        AssertHighlighter("go",
"""
func (s *Stack[T]) Push(v T) {
    s.items = append(s.items, v)
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-params">(s *Stack[T])</span> <span class="hljs-title">Push</span><span class="hljs-params">(v T)</span></span> {
    s.items = <span class="hljs-built_in">append</span>(s.items, v)
}
""");
    }

    [Fact]
    public void Method_UnnamedReceiver()
    {
        AssertHighlighter("go",
"""
func (*Handler) ServeHTTP(w http.ResponseWriter, r *http.Request) {}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-params">(*Handler)</span> <span class="hljs-title">ServeHTTP</span><span class="hljs-params">(w http.ResponseWriter, r *http.Request)</span></span> {}
""");
    }

    [Fact]
    public void Function_Generic()
    {
        AssertHighlighter("go",
"""
func Map[T, U any](s []T, f func(T) U) []U {
    r := make([]U, 0, len(s))
    return r
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-title">Map</span>[T, U <span class="hljs-type">any</span>]<span class="hljs-params">(s []T, f <span class="hljs-keyword">func</span>(T) U)</span></span> []U {
    r := <span class="hljs-built_in">make</span>([]U, <span class="hljs-number">0</span>, <span class="hljs-built_in">len</span>(s))
    <span class="hljs-keyword">return</span> r
}
""");
    }

    [Fact]
    public void Function_GenericConstraint()
    {
        AssertHighlighter("go",
"""
type Number interface {
    ~int | ~int64 | ~float64
}

func Max[T comparable](a, b T) T { return a }
""",
"""
<span class="hljs-keyword">type</span> Number <span class="hljs-keyword">interface</span> {
    ~<span class="hljs-type">int</span> | ~<span class="hljs-type">int64</span> | ~<span class="hljs-type">float64</span>
}

<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-title">Max</span>[T <span class="hljs-type">comparable</span>]<span class="hljs-params">(a, b T)</span></span> T { <span class="hljs-keyword">return</span> a }
""");
    }

    [Fact]
    public void Function_Literal()
    {
        AssertHighlighter("go",
"""
add := func(a, b int) int { return a + b }
defer func() {
    if r := recover(); r != nil {
        log.Println(r)
    }
}()
""",
"""
add := <span class="hljs-function"><span class="hljs-keyword">func</span><span class="hljs-params">(a, b <span class="hljs-type">int</span>)</span></span> <span class="hljs-type">int</span> { <span class="hljs-keyword">return</span> a + b }
<span class="hljs-keyword">defer</span> <span class="hljs-function"><span class="hljs-keyword">func</span><span class="hljs-params">()</span></span> {
    <span class="hljs-keyword">if</span> r := <span class="hljs-built_in">recover</span>(); r != <span class="hljs-literal">nil</span> {
        log.Println(r)
    }
}()
""");
    }

    [Fact]
    public void Function_Type()
    {
        AssertHighlighter("go",
"""
type HandlerFunc func(ResponseWriter, *Request)
var f func(int) string
""",
"""
<span class="hljs-keyword">type</span> HandlerFunc <span class="hljs-function"><span class="hljs-keyword">func</span><span class="hljs-params">(ResponseWriter, *Request)</span></span>
<span class="hljs-keyword">var</span> f <span class="hljs-function"><span class="hljs-keyword">func</span><span class="hljs-params">(<span class="hljs-type">int</span>)</span></span> <span class="hljs-type">string</span>
""");
    }

    [Fact]
    public void Function_NoBody()
    {
        AssertHighlighter("go",
"""
func externalAsm(x int) int
""",
"""
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-title">externalAsm</span><span class="hljs-params">(x <span class="hljs-type">int</span>)</span></span> <span class="hljs-type">int</span>
""");
    }

    [Fact]
    public void Struct()
    {
        AssertHighlighter("go",
"""
type User struct {
    ID    int    `json:"id"`
    Name  string `json:"name,omitempty"`
    Email *string
}
""",
"""
<span class="hljs-keyword">type</span> User <span class="hljs-keyword">struct</span> {
    ID    <span class="hljs-type">int</span>    <span class="hljs-string">`json:&quot;id&quot;`</span>
    Name  <span class="hljs-type">string</span> <span class="hljs-string">`json:&quot;name,omitempty&quot;`</span>
    Email *<span class="hljs-type">string</span>
}
""");
    }

    [Fact]
    public void StructLiteral()
    {
        AssertHighlighter("go",
"""
u := User{ID: 1, Name: "Alice"}
p := &Point{X: 1, Y: 2}
""",
"""
u := User{ID: <span class="hljs-number">1</span>, Name: <span class="hljs-string">&quot;Alice&quot;</span>}
p := &amp;Point{X: <span class="hljs-number">1</span>, Y: <span class="hljs-number">2</span>}
""");
    }

    [Fact]
    public void Interface()
    {
        AssertHighlighter("go",
"""
type Reader interface {
    Read(p []byte) (n int, err error)
}
""",
"""
<span class="hljs-keyword">type</span> Reader <span class="hljs-keyword">interface</span> {
    Read(p []<span class="hljs-type">byte</span>) (n <span class="hljs-type">int</span>, err <span class="hljs-type">error</span>)
}
""");
    }

    [Fact]
    public void EmbeddedInterface()
    {
        AssertHighlighter("go",
"""
type ReadWriter interface {
    Reader
    Writer
}
""",
"""
<span class="hljs-keyword">type</span> ReadWriter <span class="hljs-keyword">interface</span> {
    Reader
    Writer
}
""");
    }

    [Fact]
    public void Map_Slice()
    {
        AssertHighlighter("go",
"""
m := map[string][]int{"a": {1, 2}}
s := []byte("hi")
arr := [3]int{1, 2, 3}
""",
"""
m := <span class="hljs-keyword">map</span>[<span class="hljs-type">string</span>][]<span class="hljs-type">int</span>{<span class="hljs-string">&quot;a&quot;</span>: {<span class="hljs-number">1</span>, <span class="hljs-number">2</span>}}
s := []<span class="hljs-type">byte</span>(<span class="hljs-string">&quot;hi&quot;</span>)
arr := [<span class="hljs-number">3</span>]<span class="hljs-type">int</span>{<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>}
""");
    }

    [Fact]
    public void Make_Append_Len()
    {
        AssertHighlighter("go",
"""
s := make([]int, 0, 10)
s = append(s, 1)
n := len(s) + cap(s)
delete(m, "k")
clear(m)
x := min(1, 2) + max(3, 4)
""",
"""
s := <span class="hljs-built_in">make</span>([]<span class="hljs-type">int</span>, <span class="hljs-number">0</span>, <span class="hljs-number">10</span>)
s = <span class="hljs-built_in">append</span>(s, <span class="hljs-number">1</span>)
n := <span class="hljs-built_in">len</span>(s) + <span class="hljs-built_in">cap</span>(s)
<span class="hljs-built_in">delete</span>(m, <span class="hljs-string">&quot;k&quot;</span>)
<span class="hljs-built_in">clear</span>(m)
x := <span class="hljs-built_in">min</span>(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>) + <span class="hljs-built_in">max</span>(<span class="hljs-number">3</span>, <span class="hljs-number">4</span>)
""");
    }

    [Fact]
    public void Channels_Goroutines()
    {
        AssertHighlighter("go",
"""
ch := make(chan int, 1)
go func() { ch <- 1 }()
v := <-ch
var recv <-chan int
close(ch)
""",
"""
ch := <span class="hljs-built_in">make</span>(<span class="hljs-keyword">chan</span> <span class="hljs-type">int</span>, <span class="hljs-number">1</span>)
<span class="hljs-keyword">go</span> <span class="hljs-function"><span class="hljs-keyword">func</span><span class="hljs-params">()</span></span> { ch &lt;- <span class="hljs-number">1</span> }()
v := &lt;-ch
<span class="hljs-keyword">var</span> recv &lt;-<span class="hljs-keyword">chan</span> <span class="hljs-type">int</span>
<span class="hljs-built_in">close</span>(ch)
""");
    }

    [Fact]
    public void Select()
    {
        AssertHighlighter("go",
"""
select {
case msg := <-ch:
    fmt.Println(msg)
case <-time.After(time.Second):
    return
default:
}
""",
"""
<span class="hljs-keyword">select</span> {
<span class="hljs-keyword">case</span> msg := &lt;-ch:
    fmt.Println(msg)
<span class="hljs-keyword">case</span> &lt;-time.After(time.Second):
    <span class="hljs-keyword">return</span>
<span class="hljs-keyword">default</span>:
}
""");
    }

    [Fact]
    public void Switch_Fallthrough()
    {
        AssertHighlighter("go",
"""
switch x := f(); x {
case 1, 2:
    fallthrough
case 3:
    break
default:
    goto end
}
end:
""",
"""
<span class="hljs-keyword">switch</span> x := f(); x {
<span class="hljs-keyword">case</span> <span class="hljs-number">1</span>, <span class="hljs-number">2</span>:
    <span class="hljs-keyword">fallthrough</span>
<span class="hljs-keyword">case</span> <span class="hljs-number">3</span>:
    <span class="hljs-keyword">break</span>
<span class="hljs-keyword">default</span>:
    <span class="hljs-keyword">goto</span> end
}
end:
""");
    }

    [Fact]
    public void TypeSwitch()
    {
        AssertHighlighter("go",
"""
switch v := i.(type) {
case int:
    return v * 2
case string, error:
    return 0
}
""",
"""
<span class="hljs-keyword">switch</span> v := i.(<span class="hljs-keyword">type</span>) {
<span class="hljs-keyword">case</span> <span class="hljs-type">int</span>:
    <span class="hljs-keyword">return</span> v * <span class="hljs-number">2</span>
<span class="hljs-keyword">case</span> <span class="hljs-type">string</span>, <span class="hljs-type">error</span>:
    <span class="hljs-keyword">return</span> <span class="hljs-number">0</span>
}
""");
    }

    [Fact]
    public void For_Loops()
    {
        AssertHighlighter("go",
"""
for i := 0; i < 10; i++ {
    continue
}
for k, v := range m {
}
for {
    break
}
for i := range 10 {}
""",
"""
<span class="hljs-keyword">for</span> i := <span class="hljs-number">0</span>; i &lt; <span class="hljs-number">10</span>; i++ {
    <span class="hljs-keyword">continue</span>
}
<span class="hljs-keyword">for</span> k, v := <span class="hljs-keyword">range</span> m {
}
<span class="hljs-keyword">for</span> {
    <span class="hljs-keyword">break</span>
}
<span class="hljs-keyword">for</span> i := <span class="hljs-keyword">range</span> <span class="hljs-number">10</span> {}
""");
    }

    [Fact]
    public void If_Else_Err()
    {
        AssertHighlighter("go",
"""
if err := do(); err != nil {
    return fmt.Errorf("do: %w", err)
} else if x > 0 {
}
""",
"""
<span class="hljs-keyword">if</span> err := do(); err != <span class="hljs-literal">nil</span> {
    <span class="hljs-keyword">return</span> fmt.Errorf(<span class="hljs-string">&quot;do: %w&quot;</span>, err)
} <span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> x &gt; <span class="hljs-number">0</span> {
}
""");
    }

    [Fact]
    public void Panic_Recover()
    {
        AssertHighlighter("go",
"""
defer func() {
    if r := recover(); r != nil {
        panic(r)
    }
}()
""",
"""
<span class="hljs-keyword">defer</span> <span class="hljs-function"><span class="hljs-keyword">func</span><span class="hljs-params">()</span></span> {
    <span class="hljs-keyword">if</span> r := <span class="hljs-built_in">recover</span>(); r != <span class="hljs-literal">nil</span> {
        <span class="hljs-built_in">panic</span>(r)
    }
}()
""");
    }

    [Fact]
    public void TypeConversion()
    {
        AssertHighlighter("go",
"""
f := float64(i)
u := uint8(255)
r := []rune(s)
var e error = MyErr{}
""",
"""
f := <span class="hljs-type">float64</span>(i)
u := <span class="hljs-type">uint8</span>(<span class="hljs-number">255</span>)
r := []<span class="hljs-type">rune</span>(s)
<span class="hljs-keyword">var</span> e <span class="hljs-type">error</span> = MyErr{}
""");
    }

    [Fact]
    public void Complex()
    {
        AssertHighlighter("go",
"""
c := complex(1, 2)
r, im := real(c), imag(c)
var z complex128
""",
"""
c := <span class="hljs-built_in">complex</span>(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>)
r, im := <span class="hljs-built_in">real</span>(c), <span class="hljs-built_in">imag</span>(c)
<span class="hljs-keyword">var</span> z <span class="hljs-type">complex128</span>
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("go",
"""
héllo := "世界"
func Grüß() {}
""",
"""
héllo := <span class="hljs-string">&quot;世界&quot;</span>
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-title">Grüß</span><span class="hljs-params">()</span></span> {}
""");
    }

    [Fact]
    public void BuildTag()
    {
        AssertHighlighter("go",
"""
//go:build linux && amd64
// +build linux

//go:generate stringer -type=Pill
package main
""",
"""
<span class="hljs-comment">//go:build linux &amp;&amp; amd64</span>
<span class="hljs-comment">// +build linux</span>

<span class="hljs-comment">//go:generate stringer -type=Pill</span>
<span class="hljs-keyword">package</span> main
""");
    }

    [Fact]
    public void Illegal_HtmlClose()
    {
        AssertHighlighter("go",
"""
x := 1
</div>
y := 2
""",
"""
x := <span class="hljs-number">1</span>
&lt;/div&gt;
y := <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Unterminated_String()
    {
        AssertHighlighter("go",
"""
s := "abc
t := 1
""",
"""
s := <span class="hljs-string">&quot;abc
t := 1</span>
""");
    }

    [Fact]
    public void Unterminated_RawString()
    {
        AssertHighlighter("go",
"""
s := `abc
t := 1
""",
"""
s := <span class="hljs-string">`abc
t := 1</span>
""");
    }

    [Fact]
    public void Unterminated_Comment()
    {
        AssertHighlighter("go",
"""
x := 1 /* never
closed
""",
"""
x := <span class="hljs-number">1</span> <span class="hljs-comment">/* never
closed</span>
""");
    }

    [Fact]
    public void Generics_TypeDecl()
    {
        AssertHighlighter("go",
"""
type List[T any] struct {
    head *node[T]
}
type Pair[K comparable, V any] struct{ Key K; Val V }
""",
"""
<span class="hljs-keyword">type</span> List[T <span class="hljs-type">any</span>] <span class="hljs-keyword">struct</span> {
    head *node[T]
}
<span class="hljs-keyword">type</span> Pair[K <span class="hljs-type">comparable</span>, V <span class="hljs-type">any</span>] <span class="hljs-keyword">struct</span>{ Key K; Val V }
""");
    }

    [Fact]
    public void Labels()
    {
        AssertHighlighter("go",
"""
outer:
    for {
        break outer
    }
""",
"""
outer:
    <span class="hljs-keyword">for</span> {
        <span class="hljs-keyword">break</span> outer
    }
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("go",
"""
x &^= y
z := a<<2 | b>>1
ok := !(a && b)
""",
"""
x &amp;^= y
z := a&lt;&lt;<span class="hljs-number">2</span> | b&gt;&gt;<span class="hljs-number">1</span>
ok := !(a &amp;&amp; b)
""");
    }

    [Fact]
    public void ErrorsIs()
    {
        AssertHighlighter("go",
"""
var ErrNotFound = errors.New("not found")
if errors.Is(err, ErrNotFound) {}
""",
"""
<span class="hljs-keyword">var</span> ErrNotFound = errors.New(<span class="hljs-string">&quot;not found&quot;</span>)
<span class="hljs-keyword">if</span> errors.Is(err, ErrNotFound) {}
""");
    }

    [Fact]
    public void Composite()
    {
        AssertHighlighter("go",
"""
package main

import (
	"fmt"
	"net/http"
)

type Server struct {
	Addr string
}

// ListenAndServe starts the server.
func (s *Server) ListenAndServe() error {
	mux := http.NewServeMux()
	mux.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) {
		fmt.Fprintf(w, "Hello, %s!", r.URL.Path[1:])
	})
	return http.ListenAndServe(s.Addr, mux)
}

func main() {
	s := &Server{Addr: ":8080"}
	if err := s.ListenAndServe(); err != nil {
		panic(err)
	}
}
""",
"""
<span class="hljs-keyword">package</span> main

<span class="hljs-keyword">import</span> (
	<span class="hljs-string">&quot;fmt&quot;</span>
	<span class="hljs-string">&quot;net/http&quot;</span>
)

<span class="hljs-keyword">type</span> Server <span class="hljs-keyword">struct</span> {
	Addr <span class="hljs-type">string</span>
}

<span class="hljs-comment">// ListenAndServe starts the server.</span>
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-params">(s *Server)</span> <span class="hljs-title">ListenAndServe</span><span class="hljs-params">()</span></span> <span class="hljs-type">error</span> {
	mux := http.NewServeMux()
	mux.HandleFunc(<span class="hljs-string">&quot;/&quot;</span>, <span class="hljs-function"><span class="hljs-keyword">func</span><span class="hljs-params">(w http.ResponseWriter, r *http.Request)</span></span> {
		fmt.Fprintf(w, <span class="hljs-string">&quot;Hello, %s!&quot;</span>, r.URL.Path[<span class="hljs-number">1</span>:])
	})
	<span class="hljs-keyword">return</span> http.ListenAndServe(s.Addr, mux)
}

<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-title">main</span><span class="hljs-params">()</span></span> {
	s := &amp;Server{Addr: <span class="hljs-string">&quot;:8080&quot;</span>}
	<span class="hljs-keyword">if</span> err := s.ListenAndServe(); err != <span class="hljs-literal">nil</span> {
		<span class="hljs-built_in">panic</span>(err)
	}
}
""");
    }

    [Fact]
    public void Function_GenericSliceConstraint()
    {
        AssertHighlighter("go",
"""
func Index[S ~[]E, E comparable](s S, v E) int {
    return -1
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-title">Index</span>[S ~[]E, E <span class="hljs-type">comparable</span>]<span class="hljs-params">(s S, v E)</span></span> <span class="hljs-type">int</span> {
    <span class="hljs-keyword">return</span> <span class="hljs-number">-1</span>
}
""");
    }

    [Fact]
    public void Function_CallbackParam()
    {
        AssertHighlighter("go",
"""
func Walk(root string, fn func(path string, err error) error) error {
    return nil
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-title">Walk</span><span class="hljs-params">(root <span class="hljs-type">string</span>, fn <span class="hljs-keyword">func</span>(path <span class="hljs-type">string</span>, err <span class="hljs-type">error</span>) <span class="hljs-type">error</span>)</span></span> <span class="hljs-type">error</span> {
    <span class="hljs-keyword">return</span> <span class="hljs-literal">nil</span>
}
""");
    }

    [Fact]
    public void Function_NestedFuncResult()
    {
        AssertHighlighter("go",
"""
func adder() func(int) int {
    return nil
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-title">adder</span><span class="hljs-params">()</span></span> <span class="hljs-function"><span class="hljs-keyword">func</span><span class="hljs-params">(<span class="hljs-type">int</span>)</span></span> <span class="hljs-type">int</span> {
    <span class="hljs-keyword">return</span> <span class="hljs-literal">nil</span>
}
""");
    }

    [Fact]
    public void Method_ReceiverOnFunctionType()
    {
        AssertHighlighter("go",
"""
func (f HandlerFunc) ServeHTTP(w ResponseWriter, r *Request) {
    f(w, r)
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-params">(f HandlerFunc)</span> <span class="hljs-title">ServeHTTP</span><span class="hljs-params">(w ResponseWriter, r *Request)</span></span> {
    f(w, r)
}
""");
    }

    [Fact]
    public void FunctionType_NotAReceiver()
    {
        AssertHighlighter("go",
"""
var less func (a, b int) bool
""",
"""
<span class="hljs-keyword">var</span> less <span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-params">(a, b <span class="hljs-type">int</span>)</span></span> <span class="hljs-type">bool</span>
""");
    }

    [Fact]
    public void Function_ChanResult()
    {
        AssertHighlighter("go",
"""
func gen() chan(int) {}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-title">gen</span><span class="hljs-params">()</span></span> <span class="hljs-keyword">chan</span>(<span class="hljs-type">int</span>) {}
""");
    }

    [Fact]
    public void Function_FuncParamMethodLike()
    {
        AssertHighlighter("go",
"""
func (x int) mapper(y int) {}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">func</span> <span class="hljs-params">(x <span class="hljs-type">int</span>)</span> <span class="hljs-title">mapper</span><span class="hljs-params">(y <span class="hljs-type">int</span>)</span></span> {}
""");
    }
}
