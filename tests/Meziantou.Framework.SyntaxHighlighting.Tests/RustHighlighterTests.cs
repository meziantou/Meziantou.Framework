namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class RustHighlighterTests
{
    [Fact]
    public void Function_Main()
    {
        AssertHighlighter("rust",
"""
fn main() {
    println!("Hello, world!");
}
""",
"""
<span class="hljs-keyword">fn</span> <span class="hljs-title function_">main</span>() {
    <span class="hljs-built_in">println!</span>(<span class="hljs-string">&quot;Hello, world!&quot;</span>);
}
""");
    }

    [Fact]
    public void Let_Mut()
    {
        AssertHighlighter("rust",
"""
let mut x: i32 = 5;
let y = x + 1;
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-keyword">mut </span><span class="hljs-variable">x</span>: <span class="hljs-type">i32</span> = <span class="hljs-number">5</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">y</span> = x + <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void If_Let_While_Let()
    {
        AssertHighlighter("rust",
"""
if let Some(x) = opt {
    consume(x);
}
while let Some(top) = stack.pop() {}
""",
"""
<span class="hljs-keyword">if</span> <span class="hljs-keyword">let</span> <span class="hljs-literal">Some</span>(x) = opt {
    <span class="hljs-title function_ invoke__">consume</span>(x);
}
<span class="hljs-keyword">while</span> <span class="hljs-keyword">let</span> <span class="hljs-literal">Some</span>(top) = stack.<span class="hljs-title function_ invoke__">pop</span>() {}
""");
    }

    [Fact]
    public void For_In()
    {
        AssertHighlighter("rust",
"""
for i in 0..10 {
    total += i;
}
""",
"""
<span class="hljs-keyword">for</span> <span class="hljs-variable">i</span> <span class="hljs-keyword">in</span> <span class="hljs-number">0</span>..<span class="hljs-number">10</span> {
    total += i;
}
""");
    }

    [Fact]
    public void Struct_Declaration()
    {
        AssertHighlighter("rust",
"""
#[derive(Debug, Clone)]
pub struct Point {
    x: f64,
    y: f64,
}
""",
"""
<span class="hljs-meta">#[derive(Debug, Clone)]</span>
<span class="hljs-keyword">pub</span> <span class="hljs-keyword">struct</span> <span class="hljs-title class_">Point</span> {
    x: <span class="hljs-type">f64</span>,
    y: <span class="hljs-type">f64</span>,
}
""");
    }

    [Fact]
    public void Impl_Trait_For()
    {
        AssertHighlighter("rust",
"""
impl<T: Display> fmt::Display for Wrapper<T> {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "[{}]", self.0)
    }
}
""",
"""
<span class="hljs-keyword">impl</span>&lt;T: Display&gt; fmt::Display <span class="hljs-keyword">for</span> <span class="hljs-title class_">Wrapper</span>&lt;T&gt; {
    <span class="hljs-keyword">fn</span> <span class="hljs-title function_">fmt</span>(&amp;<span class="hljs-keyword">self</span>, f: &amp;<span class="hljs-keyword">mut</span> fmt::Formatter) <span class="hljs-punctuation">-&gt;</span> fmt::<span class="hljs-type">Result</span> {
        <span class="hljs-built_in">write!</span>(f, <span class="hljs-string">&quot;[{}]&quot;</span>, <span class="hljs-keyword">self</span>.<span class="hljs-number">0</span>)
    }
}
""");
    }

    [Fact]
    public void Enum_Match()
    {
        AssertHighlighter("rust",
"""
enum Shape { Circle(f64), Square(f64) }

match shape {
    Shape::Circle(r) => r * r,
    _ => 0.0,
}
""",
"""
<span class="hljs-keyword">enum</span> <span class="hljs-title class_">Shape</span> { <span class="hljs-title function_ invoke__">Circle</span>(<span class="hljs-type">f64</span>), <span class="hljs-title function_ invoke__">Square</span>(<span class="hljs-type">f64</span>) }

<span class="hljs-keyword">match</span> shape {
    Shape::<span class="hljs-title function_ invoke__">Circle</span>(r) =&gt; r * r,
    _ =&gt; <span class="hljs-number">0.0</span>,
}
""");
    }

    [Fact]
    public void Lifetimes()
    {
        AssertHighlighter("rust",
"""
fn longest<'a>(x: &'a str, y: &'a str) -> &'a str {
    if x.len() > y.len() { x } else { y }
}
""",
"""
<span class="hljs-keyword">fn</span> <span class="hljs-title function_">longest</span>&lt;<span class="hljs-symbol">&#x27;a</span>&gt;(x: &amp;<span class="hljs-symbol">&#x27;a</span> <span class="hljs-type">str</span>, y: &amp;<span class="hljs-symbol">&#x27;a</span> <span class="hljs-type">str</span>) <span class="hljs-punctuation">-&gt;</span> &amp;<span class="hljs-symbol">&#x27;a</span> <span class="hljs-type">str</span> {
    <span class="hljs-keyword">if</span> x.<span class="hljs-title function_ invoke__">len</span>() &gt; y.<span class="hljs-title function_ invoke__">len</span>() { x } <span class="hljs-keyword">else</span> { y }
}
""");
    }

    [Fact]
    public void Labels()
    {
        AssertHighlighter("rust",
"""
'outer: loop {
    break 'outer;
}
""",
"""
<span class="hljs-symbol">&#x27;outer</span>: <span class="hljs-keyword">loop</span> {
    <span class="hljs-keyword">break</span> <span class="hljs-symbol">&#x27;outer</span>;
}
""");
    }

    [Fact]
    public void Char_Literals()
    {
        AssertHighlighter("rust",
"""
let c = 'a';
let q = '\'';
let n = '\n';
let b = b'x';
let u = '\u{1F600}';
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-variable">c</span> = <span class="hljs-string">&#x27;a&#x27;</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">q</span> = <span class="hljs-string">&#x27;<span class="hljs-char escape_">\&#x27;</span>&#x27;</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">n</span> = <span class="hljs-string">&#x27;<span class="hljs-char escape_">\n</span>&#x27;</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">b</span> = <span class="hljs-string">b&#x27;x&#x27;</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">u</span> = <span class="hljs-string">&#x27;<span class="hljs-char escape_">\u{1F600}</span>&#x27;</span>;
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("rust",
"""
let s = "tab\tquote\"";
let b = b"bytes";
let m = "multi
line";
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-variable">s</span> = <span class="hljs-string">&quot;tab\tquote\&quot;&quot;</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">b</span> = <span class="hljs-string">b&quot;bytes&quot;</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">m</span> = <span class="hljs-string">&quot;multi
line&quot;</span>;
""");
    }

    [Fact]
    public void Raw_Strings()
    {
        AssertHighlighter("rust",
"""
let r = r"C:\path";
let h = r#"a "quoted" word"#;
let bh = br##"x"#y"##;
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-variable">r</span> = <span class="hljs-string">r&quot;C:\path&quot;</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">h</span> = <span class="hljs-string">r#&quot;a &quot;quoted&quot; word&quot;#</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">bh</span> = <span class="hljs-string">br##&quot;x&quot;#y&quot;##</span>;
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("rust",
"""
let a = 0b1010_1010u8;
let b = 0o777;
let c = 0xFF_FFi64;
let d = 1_000_000;
let e = 2.5e-3f64;
let f = 1usize;
let g = t.0;
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-variable">a</span> = <span class="hljs-number">0b1010_1010u8</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">b</span> = <span class="hljs-number">0o777</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">c</span> = <span class="hljs-number">0xFF_FFi64</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">d</span> = <span class="hljs-number">1_000_000</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">e</span> = <span class="hljs-number">2.5e-3f64</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">f</span> = <span class="hljs-number">1usize</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">g</span> = t.<span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("rust",
"""
/* outer /* inner */ still comment */
let x = 1; // TODO: remove
/// Doc comment
//! Inner doc
""",
"""
<span class="hljs-comment">/* outer <span class="hljs-comment">/* inner */</span> still comment */</span>
<span class="hljs-keyword">let</span> <span class="hljs-variable">x</span> = <span class="hljs-number">1</span>; <span class="hljs-comment">// <span class="hljs-doctag">TODO:</span> remove</span>
<span class="hljs-comment">/// Doc comment</span>
<span class="hljs-comment">//! Inner doc</span>
""");
    }

    [Fact]
    public void Attributes()
    {
        AssertHighlighter("rust",
"""
#![allow(dead_code)]
#[cfg(feature = "std")]
use std::collections::HashMap;
""",
"""
<span class="hljs-meta">#![allow(dead_code)]</span>
<span class="hljs-meta">#[cfg(feature = <span class="hljs-string">&quot;std&quot;</span>)]</span>
<span class="hljs-keyword">use</span> std::collections::HashMap;
""");
    }

    [Fact]
    public void Option_Result()
    {
        AssertHighlighter("rust",
"""
fn parse(s: &str) -> Result<Option<u32>, String> {
    match s.parse::<u32>() {
        Ok(v) => Ok(Some(v)),
        Err(e) => Err(e.to_string()),
    }
}
""",
"""
<span class="hljs-keyword">fn</span> <span class="hljs-title function_">parse</span>(s: &amp;<span class="hljs-type">str</span>) <span class="hljs-punctuation">-&gt;</span> <span class="hljs-type">Result</span>&lt;<span class="hljs-type">Option</span>&lt;<span class="hljs-type">u32</span>&gt;, <span class="hljs-type">String</span>&gt; {
    <span class="hljs-keyword">match</span> s.parse::&lt;<span class="hljs-type">u32</span>&gt;() {
        <span class="hljs-literal">Ok</span>(v) =&gt; <span class="hljs-literal">Ok</span>(<span class="hljs-literal">Some</span>(v)),
        <span class="hljs-literal">Err</span>(e) =&gt; <span class="hljs-literal">Err</span>(e.<span class="hljs-title function_ invoke__">to_string</span>()),
    }
}
""");
    }

    [Fact]
    public void Self_Path()
    {
        AssertHighlighter("rust",
"""
impl Point {
    pub fn new() -> Self {
        Self::default()
    }
}
""",
"""
<span class="hljs-keyword">impl</span> <span class="hljs-title class_">Point</span> {
    <span class="hljs-keyword">pub</span> <span class="hljs-keyword">fn</span> <span class="hljs-title function_">new</span>() <span class="hljs-punctuation">-&gt;</span> <span class="hljs-keyword">Self</span> {
        <span class="hljs-keyword">Self</span>::<span class="hljs-title function_ invoke__">default</span>()
    }
}
""");
    }

    [Fact]
    public void Trait_Declaration()
    {
        AssertHighlighter("rust",
"""
pub trait Shape: Debug {
    fn area(&self) -> f64;
}
""",
"""
<span class="hljs-keyword">pub</span> <span class="hljs-keyword">trait</span> <span class="hljs-title class_">Shape</span>: <span class="hljs-built_in">Debug</span> {
    <span class="hljs-keyword">fn</span> <span class="hljs-title function_">area</span>(&amp;<span class="hljs-keyword">self</span>) <span class="hljs-punctuation">-&gt;</span> <span class="hljs-type">f64</span>;
}
""");
    }

    [Fact]
    public void Type_Alias()
    {
        AssertHighlighter("rust",
"""
type Result<T> = std::result::Result<T, Error>;
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Result</span>&lt;T&gt; = std::result::<span class="hljs-type">Result</span>&lt;T, Error&gt;;
""");
    }

    [Fact]
    public void Macros()
    {
        AssertHighlighter("rust",
"""
macro_rules! square {
    ($x:expr) => { $x * $x };
}
let v = vec![1, 2, 3];
assert_eq!(v.len(), 3);
""",
"""
<span class="hljs-built_in">macro_rules!</span> square {
    ($x:expr) =&gt; { $x * $x };
}
<span class="hljs-keyword">let</span> <span class="hljs-variable">v</span> = <span class="hljs-built_in">vec!</span>[<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>];
<span class="hljs-built_in">assert_eq!</span>(v.<span class="hljs-title function_ invoke__">len</span>(), <span class="hljs-number">3</span>);
""");
    }

    [Fact]
    public void Closures_Async()
    {
        AssertHighlighter("rust",
"""
let add = |a, b| a + b;
async fn fetch() -> u8 { data.await }
let f = move || drop(x);
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-variable">add</span> = |a, b| a + b;
<span class="hljs-keyword">async</span> <span class="hljs-keyword">fn</span> <span class="hljs-title function_">fetch</span>() <span class="hljs-punctuation">-&gt;</span> <span class="hljs-type">u8</span> { data.<span class="hljs-keyword">await</span> }
<span class="hljs-keyword">let</span> <span class="hljs-variable">f</span> = <span class="hljs-keyword">move</span> || <span class="hljs-title function_ invoke__">drop</span>(x);
""");
    }

    [Fact]
    public void Generic_Types()
    {
        AssertHighlighter("rust",
"""
let v: Vec<String> = Vec::new();
let b: Box<dyn Fn(i32) -> i32>;
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-variable">v</span>: <span class="hljs-type">Vec</span>&lt;<span class="hljs-type">String</span>&gt; = <span class="hljs-type">Vec</span>::<span class="hljs-title function_ invoke__">new</span>();
<span class="hljs-keyword">let</span> <span class="hljs-variable">b</span>: <span class="hljs-type">Box</span>&lt;<span class="hljs-keyword">dyn</span> <span class="hljs-title function_ invoke__">Fn</span>(<span class="hljs-type">i32</span>) <span class="hljs-punctuation">-&gt;</span> <span class="hljs-type">i32</span>&gt;;
""");
    }

    [Fact]
    public void Unsafe_Extern()
    {
        AssertHighlighter("rust",
"""
unsafe extern "C" fn callback(ptr: *const u8) {}
""",
"""
<span class="hljs-keyword">unsafe</span> <span class="hljs-keyword">extern</span> <span class="hljs-string">&quot;C&quot;</span> <span class="hljs-keyword">fn</span> <span class="hljs-title function_">callback</span>(ptr: *<span class="hljs-keyword">const</span> <span class="hljs-type">u8</span>) {}
""");
    }

    [Fact]
    public void Const_Static()
    {
        AssertHighlighter("rust",
"""
const MAX: usize = 100;
static mut COUNTER: u32 = 0;
""",
"""
<span class="hljs-keyword">const</span> MAX: <span class="hljs-type">usize</span> = <span class="hljs-number">100</span>;
<span class="hljs-keyword">static</span> <span class="hljs-keyword">mut</span> COUNTER: <span class="hljs-type">u32</span> = <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Where_Clause()
    {
        AssertHighlighter("rust",
"""
fn print<T>(t: T) where T: ToString + Clone {}
""",
"""
<span class="hljs-keyword">fn</span> <span class="hljs-title function_">print</span>&lt;T&gt;(t: T) <span class="hljs-keyword">where</span> T: <span class="hljs-built_in">ToString</span> + <span class="hljs-built_in">Clone</span> {}
""");
    }

    [Fact]
    public void Modules()
    {
        AssertHighlighter("rust",
"""
pub mod network {
    pub(crate) use super::*;
}
""",
"""
<span class="hljs-keyword">pub</span> <span class="hljs-keyword">mod</span> network {
    <span class="hljs-keyword">pub</span>(<span class="hljs-keyword">crate</span>) <span class="hljs-keyword">use</span> super::*;
}
""");
    }

    [Fact]
    public void Raw_Identifier()
    {
        AssertHighlighter("rust",
"""
let r#type = 1;
r#match(x);
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-variable">r#type</span> = <span class="hljs-number">1</span>;
<span class="hljs-title function_ invoke__">r#match</span>(x);
""");
    }

    [Fact]
    public void Invoke_KeywordPrefixedName()
    {
        AssertHighlighter("rust",
"""
let formatted = format(x);
let lettuce = iffy(y);
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-variable">formatted</span> = <span class="hljs-title function_ invoke__">format</span>(x);
<span class="hljs-keyword">let</span> <span class="hljs-variable">lettuce</span> = <span class="hljs-title function_ invoke__">iffy</span>(y);
""");
    }

    [Fact]
    public void Keyword_InsideIdentifier()
    {
        AssertHighlighter("rust",
"""
let _self = 1;
let ofn = x;
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-variable">_self</span> = <span class="hljs-number">1</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">ofn</span> = x;
""");
    }

    [Fact]
    public void Unterminated_RawString()
    {
        AssertHighlighter("rust",
"""
let s = r#"never closed
let x = 1;
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-variable">s</span> = <span class="hljs-string">r#&quot;never closed
let x = 1;</span>
""");
    }

    [Fact]
    public void C_Strings()
    {
        AssertHighlighter("rust",
"""
let c = c"hello";
let cr = cr#"raw"#;
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-variable">c</span> = <span class="hljs-string">c&quot;hello&quot;</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">cr</span> = <span class="hljs-string">cr#&quot;raw&quot;#</span>;
""");
    }

    [Fact]
    public void Struct_Instance()
    {
        AssertHighlighter("rust",
"""
let p = Point { x: 1.0, y: -2.0 };
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-variable">p</span> = Point { x: <span class="hljs-number">1.0</span>, y: -<span class="hljs-number">2.0</span> };
""");
    }

    [Fact]
    public void Char_Escapes()
    {
        AssertHighlighter("rust",
"""
let bs = '\\';
let dq = '"';
let nul = '\0';
let hex = b'\x7f';
let u = '\u{1F600}';
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-variable">bs</span> = <span class="hljs-string">&#x27;<span class="hljs-char escape_">\\</span>&#x27;</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">dq</span> = <span class="hljs-string">&#x27;&quot;&#x27;</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">nul</span> = <span class="hljs-string">&#x27;<span class="hljs-char escape_">\0</span>&#x27;</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">hex</span> = <span class="hljs-string">b&#x27;<span class="hljs-char escape_">\x7f</span>&#x27;</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">u</span> = <span class="hljs-string">&#x27;<span class="hljs-char escape_">\u{1F600}</span>&#x27;</span>;
""");
    }

    [Fact]
    public void Let_Patterns()
    {
        AssertHighlighter("rust",
"""
let Point { x, y } = p;
let Shape::Circle(r) = s;
let (a, b) = pair;
""",
"""
<span class="hljs-keyword">let</span> Point { x, y } = p;
<span class="hljs-keyword">let</span> Shape::<span class="hljs-title function_ invoke__">Circle</span>(r) = s;
<span class="hljs-keyword">let</span> (a, b) = pair;
""");
    }

    [Fact]
    public void Keyword_FollowedByParenthesis()
    {
        AssertHighlighter("rust",
"""
pub(crate) fn f() -> (u8, u8) {
    return (1, 2);
}
let r = match (a, b) { _ => Ok(()) };
""",
"""
<span class="hljs-keyword">pub</span>(<span class="hljs-keyword">crate</span>) <span class="hljs-keyword">fn</span> <span class="hljs-title function_">f</span>() <span class="hljs-punctuation">-&gt;</span> (<span class="hljs-type">u8</span>, <span class="hljs-type">u8</span>) {
    <span class="hljs-keyword">return</span> (<span class="hljs-number">1</span>, <span class="hljs-number">2</span>);
}
<span class="hljs-keyword">let</span> <span class="hljs-variable">r</span> = <span class="hljs-keyword">match</span> (a, b) { _ =&gt; <span class="hljs-literal">Ok</span>(()) };
""");
    }
}
