namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class OcamlHighlighterTests
{
    [Fact]
    public void Hello()
    {
        AssertHighlighter("ocaml",
"""
let () = print_endline "Hello, world!"
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-literal">()</span> = print_endline <span class="hljs-string">&quot;Hello, world!&quot;</span>
""");
    }

    [Fact]
    public void LetBindings()
    {
        AssertHighlighter("ocaml",
"""
let x = 42
let rec fact n = if n <= 1 then 1 else n * fact (n - 1)
let f x y = x + y and g = fun x -> x * 2
let nonrec t = t
let open List in map succ [1; 2; 3]
""",
"""
<span class="hljs-keyword">let</span> x = <span class="hljs-number">42</span>
<span class="hljs-keyword">let</span> <span class="hljs-keyword">rec</span> fact n = <span class="hljs-keyword">if</span> n &lt;= <span class="hljs-number">1</span> <span class="hljs-keyword">then</span> <span class="hljs-number">1</span> <span class="hljs-keyword">else</span> n * fact (n - <span class="hljs-number">1</span>)
<span class="hljs-keyword">let</span> f x y = x + y <span class="hljs-keyword">and</span> g = <span class="hljs-keyword">fun</span> x -&gt; x * <span class="hljs-number">2</span>
<span class="hljs-keyword">let</span> <span class="hljs-keyword">nonrec</span> t = t
<span class="hljs-keyword">let</span> <span class="hljs-keyword">open</span> <span class="hljs-type">List</span> <span class="hljs-keyword">in</span> map succ [<span class="hljs-number">1</span>; <span class="hljs-number">2</span>; <span class="hljs-number">3</span>]
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("ocaml",
"""
(* a comment *)
(** doc comment
    @param x the value
    TODO: fix *)
(* nested (* inner *) still comment *) let x = 1
(*) weird *)
let a = ( * ) 2 3
(* unterminated "string" in comment *)
""",
"""
<span class="hljs-comment">(* a comment *)</span>
<span class="hljs-comment">(** doc comment
    @param x the value
    <span class="hljs-doctag">TODO:</span> fix *)</span>
<span class="hljs-comment">(* nested <span class="hljs-comment">(* inner *)</span> still comment *)</span> <span class="hljs-keyword">let</span> x = <span class="hljs-number">1</span>
<span class="hljs-comment">(*) weird *)</span>
<span class="hljs-keyword">let</span> a = ( * ) <span class="hljs-number">2</span> <span class="hljs-number">3</span>
<span class="hljs-comment">(* unterminated &quot;string&quot; in comment *)</span>
""");
    }

    [Fact]
    public void TypeDefinitions()
    {
        AssertHighlighter("ocaml",
"""
type 'a tree = Leaf | Node of 'a tree * 'a * 'a tree
type ('k, 'v) map = ('k * 'v) list
type point = { x : float; mutable y : float }
type t = int option
type shape = [ `Circle of float | `Square of float ]
let area : shape -> float = function
  | `Circle r -> 3.14 *. r *. r
  | `Square s -> s *. s
type _ expr = Int : int -> int expr | Bool : bool -> bool expr
let id : 'a. 'a -> 'a = fun x -> x
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-symbol">&#x27;a</span> tree = <span class="hljs-type">Leaf</span> | <span class="hljs-type">Node</span> <span class="hljs-keyword">of</span> <span class="hljs-symbol">&#x27;a</span> tree * <span class="hljs-symbol">&#x27;a</span> * <span class="hljs-symbol">&#x27;a</span> tree
<span class="hljs-keyword">type</span> (<span class="hljs-symbol">&#x27;k</span>, <span class="hljs-symbol">&#x27;v</span>) map = (<span class="hljs-symbol">&#x27;k</span> * <span class="hljs-symbol">&#x27;v</span>) <span class="hljs-built_in">list</span>
<span class="hljs-keyword">type</span> point = { x : <span class="hljs-built_in">float</span>; <span class="hljs-keyword">mutable</span> y : <span class="hljs-built_in">float</span> }
<span class="hljs-keyword">type</span> t = <span class="hljs-built_in">int</span> <span class="hljs-built_in">option</span>
<span class="hljs-keyword">type</span> shape = [ <span class="hljs-type">`Circle</span> <span class="hljs-keyword">of</span> <span class="hljs-built_in">float</span> | <span class="hljs-type">`Square</span> <span class="hljs-keyword">of</span> <span class="hljs-built_in">float</span> ]
<span class="hljs-keyword">let</span> area : shape -&gt; <span class="hljs-built_in">float</span> = <span class="hljs-keyword">function</span>
  | <span class="hljs-type">`Circle</span> r -&gt; <span class="hljs-number">3.14</span> *. r *. r
  | <span class="hljs-type">`Square</span> s -&gt; s *. s
<span class="hljs-keyword">type</span> _ expr = <span class="hljs-type">Int</span> : <span class="hljs-built_in">int</span> -&gt; <span class="hljs-built_in">int</span> expr | <span class="hljs-type">Bool</span> : <span class="hljs-built_in">bool</span> -&gt; <span class="hljs-built_in">bool</span> expr
<span class="hljs-keyword">let</span> id : <span class="hljs-symbol">&#x27;a</span>. <span class="hljs-symbol">&#x27;a</span> -&gt; <span class="hljs-symbol">&#x27;a</span> = <span class="hljs-keyword">fun</span> x -&gt; x
""");
    }

    [Fact]
    public void TypeVariables()
    {
        AssertHighlighter("ocaml",
"""
let f : 'a -> 'b -> 'a = fun x _ -> x
type 'foo_bar t = 'foo_bar list
type ('a, 'b) either = Left of 'a | Right of 'b
let x = 'a'b
let y : '_weak1 list = []
val map : ('a -> 'b) -> 'a list -> 'b list
""",
"""
<span class="hljs-keyword">let</span> f : <span class="hljs-symbol">&#x27;a</span> -&gt; <span class="hljs-symbol">&#x27;b</span> -&gt; <span class="hljs-symbol">&#x27;a</span> = <span class="hljs-keyword">fun</span> x _ -&gt; x
<span class="hljs-keyword">type</span> <span class="hljs-symbol">&#x27;foo_bar</span> t = <span class="hljs-symbol">&#x27;foo_bar</span> <span class="hljs-built_in">list</span>
<span class="hljs-keyword">type</span> (<span class="hljs-symbol">&#x27;a</span>, <span class="hljs-symbol">&#x27;b</span>) either = <span class="hljs-type">Left</span> <span class="hljs-keyword">of</span> <span class="hljs-symbol">&#x27;a</span> | <span class="hljs-type">Right</span> <span class="hljs-keyword">of</span> <span class="hljs-symbol">&#x27;b</span>
<span class="hljs-keyword">let</span> x = <span class="hljs-string">&#x27;a&#x27;</span>b
<span class="hljs-keyword">let</span> y : <span class="hljs-symbol">&#x27;_weak1</span> <span class="hljs-built_in">list</span> = <span class="hljs-literal">[]</span>
<span class="hljs-keyword">val</span> map : (<span class="hljs-symbol">&#x27;a</span> -&gt; <span class="hljs-symbol">&#x27;b</span>) -&gt; <span class="hljs-symbol">&#x27;a</span> <span class="hljs-built_in">list</span> -&gt; <span class="hljs-symbol">&#x27;b</span> <span class="hljs-built_in">list</span>
""");
    }

    [Fact]
    public void CharLiterals()
    {
        AssertHighlighter("ocaml",
"""
let c = 'a'
let nl = '\n'
let q = '\''
let bs = '\\'
let dq = '"'
let d = '\065'
let h = '\x41'
let u = '_'
let sp = ' '
let f' = 1 and g'' = 2 and h'x = 3
let x = f' + g''
let ab'c = 'b'
""",
"""
<span class="hljs-keyword">let</span> c = <span class="hljs-string">&#x27;a&#x27;</span>
<span class="hljs-keyword">let</span> nl = <span class="hljs-string">&#x27;\n&#x27;</span>
<span class="hljs-keyword">let</span> q = <span class="hljs-string">&#x27;\&#x27;&#x27;</span>
<span class="hljs-keyword">let</span> bs = <span class="hljs-string">&#x27;\\&#x27;</span>
<span class="hljs-keyword">let</span> dq = <span class="hljs-string">&#x27;&quot;&#x27;</span>
<span class="hljs-keyword">let</span> d = <span class="hljs-string">&#x27;\065&#x27;</span>
<span class="hljs-keyword">let</span> h = <span class="hljs-string">&#x27;\x41&#x27;</span>
<span class="hljs-keyword">let</span> u = <span class="hljs-string">&#x27;_&#x27;</span>
<span class="hljs-keyword">let</span> sp = <span class="hljs-string">&#x27; &#x27;</span>
<span class="hljs-keyword">let</span> f&#x27; = <span class="hljs-number">1</span> <span class="hljs-keyword">and</span> g&#x27;&#x27; = <span class="hljs-number">2</span> <span class="hljs-keyword">and</span> h&#x27;x = <span class="hljs-number">3</span>
<span class="hljs-keyword">let</span> x = f&#x27; + g&#x27;&#x27;
<span class="hljs-keyword">let</span> ab&#x27;c = <span class="hljs-string">&#x27;b&#x27;</span>
""");
    }

    [Fact]
    public void PrimedIdentifiers()
    {
        AssertHighlighter("ocaml",
"""
let x' = 1
let x'' = x' + 1
let List' = 2
let Foo'Bar = 3
let _' = 4
let a1'b2 = 5
""",
"""
<span class="hljs-keyword">let</span> x&#x27; = <span class="hljs-number">1</span>
<span class="hljs-keyword">let</span> x&#x27;&#x27; = x&#x27; + <span class="hljs-number">1</span>
<span class="hljs-keyword">let</span> <span class="hljs-type">List&#x27;</span> = <span class="hljs-number">2</span>
<span class="hljs-keyword">let</span> <span class="hljs-type">Foo&#x27;Bar</span> = <span class="hljs-number">3</span>
<span class="hljs-keyword">let</span> _&#x27; = <span class="hljs-number">4</span>
<span class="hljs-keyword">let</span> a1&#x27;b2 = <span class="hljs-number">5</span>
""");
    }

    [Fact]
    public void PrimedIdentifiers_InsideWords()
    {
        AssertHighlighter("ocaml",
"""
let x1a'b = 1a' + foo.bar' + ab_c'd e'
let y = List.map' f l
let _ = 1x'
let z = f'(x)
""",
"""
<span class="hljs-keyword">let</span> x1a&#x27;b = <span class="hljs-number">1</span>a&#x27; + foo.bar&#x27; + ab_c&#x27;d e&#x27;
<span class="hljs-keyword">let</span> y = <span class="hljs-type">List</span>.map&#x27; f l
<span class="hljs-keyword">let</span> _ = <span class="hljs-number">1</span>x&#x27;
<span class="hljs-keyword">let</span> z = f&#x27;(x)
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("ocaml",
"""
let s = "hello \"world\"\n"
let multi = "line one
line two"
let cont = "abc\
   def"
let q = {|quoted "string" with 'apostrophe' and (* not a comment *)|}
let q2 = {id|another |} still |id}
let q3 = {foo_bar|x|foo_bar} let y = 1
let q4 = {| multi
line |}
let r = { x = 1; y = 2 }
let s = {r with x = 3}
""",
"""
<span class="hljs-keyword">let</span> s = <span class="hljs-string">&quot;hello \&quot;world\&quot;\n&quot;</span>
<span class="hljs-keyword">let</span> multi = <span class="hljs-string">&quot;line one
line two&quot;</span>
<span class="hljs-keyword">let</span> cont = <span class="hljs-string">&quot;abc\
   def&quot;</span>
<span class="hljs-keyword">let</span> q = <span class="hljs-string">{|quoted &quot;string&quot; with &#x27;apostrophe&#x27; and (* not a comment *)|}</span>
<span class="hljs-keyword">let</span> q2 = <span class="hljs-string">{id|another |} still |id}</span>
<span class="hljs-keyword">let</span> q3 = <span class="hljs-string">{foo_bar|x|foo_bar}</span> <span class="hljs-keyword">let</span> y = <span class="hljs-number">1</span>
<span class="hljs-keyword">let</span> q4 = <span class="hljs-string">{| multi
line |}</span>
<span class="hljs-keyword">let</span> r = { x = <span class="hljs-number">1</span>; y = <span class="hljs-number">2</span> }
<span class="hljs-keyword">let</span> s = {r <span class="hljs-keyword">with</span> x = <span class="hljs-number">3</span>}
""");
    }

    [Fact]
    public void QuotedStrings_RawContent()
    {
        AssertHighlighter("ocaml",
"""
let s = {|a\n\t "|} and t = {x|{|y|}|x}
let u = {||}
""",
"""
<span class="hljs-keyword">let</span> s = <span class="hljs-string">{|a\n\t &quot;|}</span> <span class="hljs-keyword">and</span> t = <span class="hljs-string">{x|{|y|}|x}</span>
<span class="hljs-keyword">let</span> u = <span class="hljs-string">{||}</span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("ocaml",
"""
let a = 42
let b = 1_000_000
let c = 0xFF_FF
let d = 0o777
let e = 0b1010_1010
let f = 3.14
let g = 1e10
let h = 1.5e-3
let i = 1.
let j = 42L
let k = 42l
let l = 42n
let m = 0x1Fn
let n = -5
let o = 1_000.000_1
""",
"""
<span class="hljs-keyword">let</span> a = <span class="hljs-number">42</span>
<span class="hljs-keyword">let</span> b = <span class="hljs-number">1_000_000</span>
<span class="hljs-keyword">let</span> c = <span class="hljs-number">0xFF_FF</span>
<span class="hljs-keyword">let</span> d = <span class="hljs-number">0o777</span>
<span class="hljs-keyword">let</span> e = <span class="hljs-number">0b1010_1010</span>
<span class="hljs-keyword">let</span> f = <span class="hljs-number">3.14</span>
<span class="hljs-keyword">let</span> g = <span class="hljs-number">1e10</span>
<span class="hljs-keyword">let</span> h = <span class="hljs-number">1.5e-3</span>
<span class="hljs-keyword">let</span> i = <span class="hljs-number">1.</span>
<span class="hljs-keyword">let</span> j = <span class="hljs-number">42L</span>
<span class="hljs-keyword">let</span> k = <span class="hljs-number">42l</span>
<span class="hljs-keyword">let</span> l = <span class="hljs-number">42n</span>
<span class="hljs-keyword">let</span> m = <span class="hljs-number">0x1Fn</span>
<span class="hljs-keyword">let</span> n = -<span class="hljs-number">5</span>
<span class="hljs-keyword">let</span> o = <span class="hljs-number">1_000.000_1</span>
""");
    }

    [Fact]
    public void Modules()
    {
        AssertHighlighter("ocaml",
"""
module M = struct
  let x = 1
end
module type S = sig
  type t
  val compare : t -> t -> int
end
module F (X : S) : S with type t = X.t = struct
  include X
end
module StringMap = Map.Make(String)
open! Core
let () = List.iter (fun x -> Printf.printf "%d\n" x) [1; 2]
let _ = Stdlib.( + ) 1 2
""",
"""
<span class="hljs-keyword">module</span> <span class="hljs-type">M</span> = <span class="hljs-keyword">struct</span>
  <span class="hljs-keyword">let</span> x = <span class="hljs-number">1</span>
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">module</span> <span class="hljs-keyword">type</span> <span class="hljs-type">S</span> = <span class="hljs-keyword">sig</span>
  <span class="hljs-keyword">type</span> t
  <span class="hljs-keyword">val</span> compare : t -&gt; t -&gt; <span class="hljs-built_in">int</span>
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">module</span> <span class="hljs-type">F</span> (<span class="hljs-type">X</span> : <span class="hljs-type">S</span>) : <span class="hljs-type">S</span> <span class="hljs-keyword">with</span> <span class="hljs-keyword">type</span> t = <span class="hljs-type">X</span>.t = <span class="hljs-keyword">struct</span>
  <span class="hljs-keyword">include</span> <span class="hljs-type">X</span>
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">module</span> <span class="hljs-type">StringMap</span> = <span class="hljs-type">Map</span>.<span class="hljs-type">Make</span>(<span class="hljs-type">String</span>)
<span class="hljs-keyword">open!</span> <span class="hljs-type">Core</span>
<span class="hljs-keyword">let</span> <span class="hljs-literal">()</span> = <span class="hljs-type">List</span>.iter (<span class="hljs-keyword">fun</span> x -&gt; <span class="hljs-type">Printf</span>.printf <span class="hljs-string">&quot;%d\n&quot;</span> x) [<span class="hljs-number">1</span>; <span class="hljs-number">2</span>]
<span class="hljs-keyword">let</span> _ = <span class="hljs-type">Stdlib</span>.( + ) <span class="hljs-number">1</span> <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void PatternMatching()
    {
        AssertHighlighter("ocaml",
"""
let rec length = function
  | [] -> 0
  | _ :: tl -> 1 + length tl
let describe x = match x with
  | Some v when v > 0 -> "positive"
  | Some _ -> "non-positive"
  | None -> "none"
let arr = [| 1; 2; 3 |] and empty = [||] and unit = ()
let l = []
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-keyword">rec</span> length = <span class="hljs-keyword">function</span>
  | <span class="hljs-literal">[]</span> -&gt; <span class="hljs-number">0</span>
  | _ :: tl -&gt; <span class="hljs-number">1</span> + length tl
<span class="hljs-keyword">let</span> describe x = <span class="hljs-keyword">match</span> x <span class="hljs-keyword">with</span>
  | <span class="hljs-type">Some</span> v <span class="hljs-keyword">when</span> v &gt; <span class="hljs-number">0</span> -&gt; <span class="hljs-string">&quot;positive&quot;</span>
  | <span class="hljs-type">Some</span> _ -&gt; <span class="hljs-string">&quot;non-positive&quot;</span>
  | <span class="hljs-type">None</span> -&gt; <span class="hljs-string">&quot;none&quot;</span>
<span class="hljs-keyword">let</span> arr = [| <span class="hljs-number">1</span>; <span class="hljs-number">2</span>; <span class="hljs-number">3</span> |] <span class="hljs-keyword">and</span> empty = <span class="hljs-literal">[||]</span> <span class="hljs-keyword">and</span> <span class="hljs-built_in">unit</span> = <span class="hljs-literal">()</span>
<span class="hljs-keyword">let</span> l = <span class="hljs-literal">[]</span>
""");
    }

    [Fact]
    public void LabeledArguments()
    {
        AssertHighlighter("ocaml",
"""
let f ~x ?(y = 0) ?z () = x + y
let g = f ~x:1 ~y:2 ()
let h ~f:(fn : int -> int) = fn 1
let _ = List.map ~f:(fun x -> x + 1) l
""",
"""
<span class="hljs-keyword">let</span> f ~x ?(y = <span class="hljs-number">0</span>) ?z <span class="hljs-literal">()</span> = x + y
<span class="hljs-keyword">let</span> g = f ~x:<span class="hljs-number">1</span> ~y:<span class="hljs-number">2</span> <span class="hljs-literal">()</span>
<span class="hljs-keyword">let</span> h ~f:(fn : <span class="hljs-built_in">int</span> -&gt; <span class="hljs-built_in">int</span>) = fn <span class="hljs-number">1</span>
<span class="hljs-keyword">let</span> _ = <span class="hljs-type">List</span>.map ~f:(<span class="hljs-keyword">fun</span> x -&gt; x + <span class="hljs-number">1</span>) l
""");
    }

    [Fact]
    public void Objects()
    {
        AssertHighlighter("ocaml",
"""
class point x_init = object (self)
  val mutable x = x_init
  val! y = 0
  method get_x = x
  method! move d = x <- x + d
  method private secret = ()
  inherit! base
  initializer print_endline "created"
end
class virtual shape = object
  method virtual area : float
end
let p = new point 1 in p#get_x
""",
"""
<span class="hljs-keyword">class</span> point x_init = <span class="hljs-keyword">object</span> (self)
  <span class="hljs-keyword">val</span> <span class="hljs-keyword">mutable</span> x = x_init
  <span class="hljs-keyword">val!</span> y = <span class="hljs-number">0</span>
  <span class="hljs-keyword">method</span> get_x = x
  <span class="hljs-keyword">method!</span> move d = x &lt;- x + d
  <span class="hljs-keyword">method</span> <span class="hljs-keyword">private</span> secret = <span class="hljs-literal">()</span>
  <span class="hljs-keyword">inherit!</span> base
  <span class="hljs-keyword">initializer</span> print_endline <span class="hljs-string">&quot;created&quot;</span>
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">class</span> <span class="hljs-keyword">virtual</span> shape = <span class="hljs-keyword">object</span>
  <span class="hljs-keyword">method</span> <span class="hljs-keyword">virtual</span> area : <span class="hljs-built_in">float</span>
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">let</span> p = <span class="hljs-keyword">new</span> point <span class="hljs-number">1</span> <span class="hljs-keyword">in</span> p#get_x
""");
    }

    [Fact]
    public void Exceptions()
    {
        AssertHighlighter("ocaml",
"""
exception Not_found_custom of string
let safe_div a b =
  try a / b with
  | Division_by_zero -> 0
  | e -> raise e
external length : string -> int = "%string_length"
let () = assert (1 = 1)
let lz = lazy (1 + 2)
""",
"""
<span class="hljs-keyword">exception</span> <span class="hljs-type">Not_found_custom</span> <span class="hljs-keyword">of</span> <span class="hljs-built_in">string</span>
<span class="hljs-keyword">let</span> safe_div a b =
  <span class="hljs-keyword">try</span> a / b <span class="hljs-keyword">with</span>
  | <span class="hljs-type">Division_by_zero</span> -&gt; <span class="hljs-number">0</span>
  | e -&gt; raise e
<span class="hljs-keyword">external</span> length : <span class="hljs-built_in">string</span> -&gt; <span class="hljs-built_in">int</span> = <span class="hljs-string">&quot;%string_length&quot;</span>
<span class="hljs-keyword">let</span> <span class="hljs-literal">()</span> = <span class="hljs-keyword">assert</span> (<span class="hljs-number">1</span> = <span class="hljs-number">1</span>)
<span class="hljs-keyword">let</span> lz = <span class="hljs-keyword">lazy</span> (<span class="hljs-number">1</span> + <span class="hljs-number">2</span>)
""");
    }

    [Fact]
    public void Loops()
    {
        AssertHighlighter("ocaml",
"""
for i = 0 to 10 do
  print_int i
done;
for i = 10 downto 0 do () done;
while !r > 0 do decr r done;
begin
  r := !r + 1
end
""",
"""
<span class="hljs-keyword">for</span> i = <span class="hljs-number">0</span> <span class="hljs-keyword">to</span> <span class="hljs-number">10</span> <span class="hljs-keyword">do</span>
  print_int i
<span class="hljs-keyword">done</span>;
<span class="hljs-keyword">for</span> i = <span class="hljs-number">10</span> <span class="hljs-keyword">downto</span> <span class="hljs-number">0</span> <span class="hljs-keyword">do</span> <span class="hljs-literal">()</span> <span class="hljs-keyword">done</span>;
<span class="hljs-keyword">while</span> !r &gt; <span class="hljs-number">0</span> <span class="hljs-keyword">do</span> decr r <span class="hljs-keyword">done</span>;
<span class="hljs-keyword">begin</span>
  r := !r + <span class="hljs-number">1</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("ocaml",
"""
let ( >>= ) m f = bind m f
let x = a >>= fun b -> return b
let y = a // b
let z = a lsl 2 lor b land 0xFF lxor c asr 1 mod 3
let w = a <> b && c != d || not e
let ( let* ) = Option.bind
let v = let* a = Some 1 in Some (a + 1)
let r = ref 0
let s = x |> f |> g @@ h
""",
"""
<span class="hljs-keyword">let</span> ( &gt;&gt;= ) m f = bind m f
<span class="hljs-keyword">let</span> x = a &gt;&gt;= <span class="hljs-keyword">fun</span> b -&gt; return b
<span class="hljs-keyword">let</span> y = a // b
<span class="hljs-keyword">let</span> z = a <span class="hljs-keyword">lsl</span> <span class="hljs-number">2</span> <span class="hljs-keyword">lor</span> b <span class="hljs-keyword">land</span> <span class="hljs-number">0xFF</span> <span class="hljs-keyword">lxor</span> c <span class="hljs-keyword">asr</span> <span class="hljs-number">1</span> <span class="hljs-keyword">mod</span> <span class="hljs-number">3</span>
<span class="hljs-keyword">let</span> w = a &lt;&gt; b &amp;&amp; c != d || not e
<span class="hljs-keyword">let</span> ( <span class="hljs-keyword">let</span>* ) = <span class="hljs-type">Option</span>.bind
<span class="hljs-keyword">let</span> v = <span class="hljs-keyword">let</span>* a = <span class="hljs-type">Some</span> <span class="hljs-number">1</span> <span class="hljs-keyword">in</span> <span class="hljs-type">Some</span> (a + <span class="hljs-number">1</span>)
<span class="hljs-keyword">let</span> r = <span class="hljs-built_in">ref</span> <span class="hljs-number">0</span>
<span class="hljs-keyword">let</span> s = x |&gt; f |&gt; g @@ h
""");
    }

    [Fact]
    public void BuiltInTypes()
    {
        AssertHighlighter("ocaml",
"""
let (a : int) = 1 and (b : float) = 1. and (c : string) = "" and (d : bool) = true
let e : char = 'c' and f : unit = () and g : bytes = Bytes.empty
let h : int32 = 1l and i : int64 = 1L and j : nativeint = 1n
let k : exn = Exit and l : int lazy_t = lazy 1
let m : in_channel = stdin and n : out_channel = stdout
let o : int array = [||] and p : int list = [] and q : int option = None
let r : int ref = ref 0
""",
"""
<span class="hljs-keyword">let</span> (a : <span class="hljs-built_in">int</span>) = <span class="hljs-number">1</span> <span class="hljs-keyword">and</span> (b : <span class="hljs-built_in">float</span>) = <span class="hljs-number">1.</span> <span class="hljs-keyword">and</span> (c : <span class="hljs-built_in">string</span>) = <span class="hljs-string">&quot;&quot;</span> <span class="hljs-keyword">and</span> (d : <span class="hljs-built_in">bool</span>) = <span class="hljs-literal">true</span>
<span class="hljs-keyword">let</span> e : <span class="hljs-built_in">char</span> = <span class="hljs-string">&#x27;c&#x27;</span> <span class="hljs-keyword">and</span> f : <span class="hljs-built_in">unit</span> = <span class="hljs-literal">()</span> <span class="hljs-keyword">and</span> g : <span class="hljs-built_in">bytes</span> = <span class="hljs-type">Bytes</span>.empty
<span class="hljs-keyword">let</span> h : <span class="hljs-built_in">int32</span> = <span class="hljs-number">1l</span> <span class="hljs-keyword">and</span> i : <span class="hljs-built_in">int64</span> = <span class="hljs-number">1L</span> <span class="hljs-keyword">and</span> j : <span class="hljs-built_in">nativeint</span> = <span class="hljs-number">1n</span>
<span class="hljs-keyword">let</span> k : <span class="hljs-built_in">exn</span> = <span class="hljs-type">Exit</span> <span class="hljs-keyword">and</span> l : <span class="hljs-built_in">int</span> <span class="hljs-built_in">lazy_t</span> = <span class="hljs-keyword">lazy</span> <span class="hljs-number">1</span>
<span class="hljs-keyword">let</span> m : <span class="hljs-built_in">in_channel</span> = stdin <span class="hljs-keyword">and</span> n : <span class="hljs-built_in">out_channel</span> = stdout
<span class="hljs-keyword">let</span> o : <span class="hljs-built_in">int</span> <span class="hljs-built_in">array</span> = <span class="hljs-literal">[||]</span> <span class="hljs-keyword">and</span> p : <span class="hljs-built_in">int</span> <span class="hljs-built_in">list</span> = <span class="hljs-literal">[]</span> <span class="hljs-keyword">and</span> q : <span class="hljs-built_in">int</span> <span class="hljs-built_in">option</span> = <span class="hljs-type">None</span>
<span class="hljs-keyword">let</span> r : <span class="hljs-built_in">int</span> <span class="hljs-built_in">ref</span> = <span class="hljs-built_in">ref</span> <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void Camlp4Keywords()
    {
        AssertHighlighter("ocaml",
"""
let parser_ = parser [< 'x >] -> x
let value = 1
""",
"""
<span class="hljs-keyword">let</span> parser_ = <span class="hljs-keyword">parser</span> [&lt; <span class="hljs-symbol">&#x27;x</span> &gt;] -&gt; x
<span class="hljs-keyword">let</span> <span class="hljs-keyword">value</span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Attributes()
    {
        AssertHighlighter("ocaml",
"""
type t = { a : int } [@@deriving show, eq]
let x = [%sexp_of: int] 1
let[@inline] f x = x
[@@@warning "-32"]
""",
"""
<span class="hljs-keyword">type</span> t = { a : <span class="hljs-built_in">int</span> } [@@deriving show, eq]
<span class="hljs-keyword">let</span> x = [%sexp_of: <span class="hljs-built_in">int</span>] <span class="hljs-number">1</span>
<span class="hljs-keyword">let</span>[@inline] f x = x
[@@@warning <span class="hljs-string">&quot;-32&quot;</span>]
""");
    }

    [Fact]
    public void PolymorphicVariants()
    {
        AssertHighlighter("ocaml",
"""
let color = `Red
let c = `RGB (1, 2, 3)
type t = [> `A | `B of int ]
let f = function `A -> 1 | `B n -> n
""",
"""
<span class="hljs-keyword">let</span> color = <span class="hljs-type">`Red</span>
<span class="hljs-keyword">let</span> c = <span class="hljs-type">`RGB</span> (<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>)
<span class="hljs-keyword">type</span> t = [&gt; <span class="hljs-type">`A</span> | <span class="hljs-type">`B</span> <span class="hljs-keyword">of</span> <span class="hljs-built_in">int</span> ]
<span class="hljs-keyword">let</span> f = <span class="hljs-keyword">function</span> <span class="hljs-type">`A</span> -&gt; <span class="hljs-number">1</span> | <span class="hljs-type">`B</span> n -&gt; n
""");
    }

    [Fact]
    public void Gadts()
    {
        AssertHighlighter("ocaml",
"""
type _ t =
  | Int : int -> int t
  | Add : (int -> int -> int) t
let rec eval : type a. a t -> a = function
  | Int n -> n
  | Add -> ( + )
""",
"""
<span class="hljs-keyword">type</span> _ t =
  | <span class="hljs-type">Int</span> : <span class="hljs-built_in">int</span> -&gt; <span class="hljs-built_in">int</span> t
  | <span class="hljs-type">Add</span> : (<span class="hljs-built_in">int</span> -&gt; <span class="hljs-built_in">int</span> -&gt; <span class="hljs-built_in">int</span>) t
<span class="hljs-keyword">let</span> <span class="hljs-keyword">rec</span> eval : <span class="hljs-keyword">type</span> a. a t -&gt; a = <span class="hljs-keyword">function</span>
  | <span class="hljs-type">Int</span> n -&gt; n
  | <span class="hljs-type">Add</span> -&gt; ( + )
""");
    }

    [Fact]
    public void Effects()
    {
        AssertHighlighter("ocaml",
"""
effect E : int
let _ = match f () with
  | effect E, k -> continue k 1
  | v -> v
""",
"""
effect <span class="hljs-type">E</span> : <span class="hljs-built_in">int</span>
<span class="hljs-keyword">let</span> _ = <span class="hljs-keyword">match</span> f <span class="hljs-literal">()</span> <span class="hljs-keyword">with</span>
  | effect <span class="hljs-type">E</span>, k -&gt; continue k <span class="hljs-number">1</span>
  | v -&gt; v
""");
    }

    [Fact]
    public void Program()
    {
        AssertHighlighter("ocaml",
"""
let rec map f = function
  | [] -> []
  | x :: xs -> let y = f x in y :: map f xs

let () =
  let open Printf in
  let lst = map (fun x -> x * 2) [1; 2; 3] in
  List.iter (printf "%d ") lst;
  print_newline ()
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-keyword">rec</span> map f = <span class="hljs-keyword">function</span>
  | <span class="hljs-literal">[]</span> -&gt; <span class="hljs-literal">[]</span>
  | x :: xs -&gt; <span class="hljs-keyword">let</span> y = f x <span class="hljs-keyword">in</span> y :: map f xs

<span class="hljs-keyword">let</span> <span class="hljs-literal">()</span> =
  <span class="hljs-keyword">let</span> <span class="hljs-keyword">open</span> <span class="hljs-type">Printf</span> <span class="hljs-keyword">in</span>
  <span class="hljs-keyword">let</span> lst = map (<span class="hljs-keyword">fun</span> x -&gt; x * <span class="hljs-number">2</span>) [<span class="hljs-number">1</span>; <span class="hljs-number">2</span>; <span class="hljs-number">3</span>] <span class="hljs-keyword">in</span>
  <span class="hljs-type">List</span>.iter (printf <span class="hljs-string">&quot;%d &quot;</span>) lst;
  print_newline <span class="hljs-literal">()</span>
""");
    }

    [Fact]
    public void BangSuffix()
    {
        AssertHighlighter("ocaml",
"""
let x = !r
let y = a!=b
if x then! y
""",
"""
<span class="hljs-keyword">let</span> x = !r
<span class="hljs-keyword">let</span> y = a!=b
<span class="hljs-keyword">if</span> x then! y
""");
    }

    [Fact]
    public void IllegalLexemes()
    {
        AssertHighlighter("ocaml",
"""
let x = a // b >> c
let y = 1
""",
"""
<span class="hljs-keyword">let</span> x = a // b &gt;&gt; c
<span class="hljs-keyword">let</span> y = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void UnterminatedComment()
    {
        AssertHighlighter("ocaml",
"""
let x = 1 (* unterminated
let y = 2
""",
"""
<span class="hljs-keyword">let</span> x = <span class="hljs-number">1</span> <span class="hljs-comment">(* unterminated
let y = 2</span>
""");
    }

    [Fact]
    public void UnterminatedString()
    {
        AssertHighlighter("ocaml",
"""
let x = "unterminated
let y = 2
""",
"""
<span class="hljs-keyword">let</span> x = <span class="hljs-string">&quot;unterminated
let y = 2</span>
""");
    }

    [Fact]
    public void UnterminatedQuotedString()
    {
        AssertHighlighter("ocaml",
"""
let x = {|unterminated
let y = 2
""",
"""
<span class="hljs-keyword">let</span> x = <span class="hljs-string">{|unterminated
let y = 2</span>
""");
    }

    [Fact]
    public void UnterminatedCharLiteral()
    {
        AssertHighlighter("ocaml",
"""
let x = 'a
let y = 'b c'
""",
"""
<span class="hljs-keyword">let</span> x = <span class="hljs-symbol">&#x27;a</span>
<span class="hljs-keyword">let</span> y = <span class="hljs-symbol">&#x27;b</span> c&#x27;
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("ocaml",
"""
let café = 1
let naïve = "é"
(* commentaire é *)
""",
"""
<span class="hljs-keyword">let</span> café = <span class="hljs-number">1</span>
<span class="hljs-keyword">let</span> naïve = <span class="hljs-string">&quot;é&quot;</span>
<span class="hljs-comment">(* commentaire é *)</span>
""");
    }

    [Fact]
    public void Alias_Ml()
    {
        AssertHighlighter("ml",
"""
let x = 'a' (* ml *)
""",
"""
<span class="hljs-keyword">let</span> x = <span class="hljs-string">&#x27;a&#x27;</span> <span class="hljs-comment">(* ml *)</span>
""");
    }

    // Finding an identifier containing an apostrophe used to be quadratic on a long run of word characters.
    [Fact]
    public async Task LongWordRun_CompletesInReasonableTime()
    {
        var code = "\"" + new string('a', 300_000) + "\" x'";
        var budget = TimeSpan.FromSeconds(10);

        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "ocaml", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'ocaml' did not finish within {budget.TotalSeconds:F0}s.");
        Assert.EndsWith("&quot;</span> x&#x27;", await highlight, StringComparison.Ordinal);
    }
}
