namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class ScalaHighlighterTests
{
    [Fact]
    public void Annotations()
    {
        AssertHighlighter("scala",
"""
@deprecated("use bar", "1.0")
def foo(): Unit = ()

@tailrec
final def loop(n: Int, acc: Int): Int = if (n == 0) acc else loop(n - 1, acc + n)

@SerialVersionUID(1L)
class Data extends Serializable

@main def run(): Unit = ()
""",
"""
<span class="hljs-meta">@deprecated</span>(<span class="hljs-string">&quot;use bar&quot;</span>, <span class="hljs-string">&quot;1.0&quot;</span>)
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">foo</span></span>(): <span class="hljs-type">Unit</span> = ()

<span class="hljs-meta">@tailrec</span>
<span class="hljs-keyword">final</span> <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">loop</span></span>(n: <span class="hljs-type">Int</span>, acc: <span class="hljs-type">Int</span>): <span class="hljs-type">Int</span> = <span class="hljs-keyword">if</span> (n == <span class="hljs-number">0</span>) acc <span class="hljs-keyword">else</span> loop(n - <span class="hljs-number">1</span>, acc + n)

<span class="hljs-meta">@SerialVersionUID</span>(<span class="hljs-number">1L</span>)
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Data</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">Serializable</span></span>

<span class="hljs-meta">@main</span> <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">run</span></span>(): <span class="hljs-type">Unit</span> = ()
""");
    }

    [Fact]
    public void CaseClassCopy()
    {
        AssertHighlighter("scala",
"""
case class User(id: Long, name: String, email: Option[String] = None)
val u = User(1L, "Ann")
val u2 = u.copy(name = "Bob")
val User(id, name, _) = u2
""",
"""
<span class="hljs-keyword">case</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">User</span>(<span class="hljs-params">id: <span class="hljs-type">Long</span>, name: <span class="hljs-type">String</span>, email: <span class="hljs-type">Option</span>[<span class="hljs-type">String</span>] = <span class="hljs-type">None</span></span>)</span>
<span class="hljs-keyword">val</span> u = <span class="hljs-type">User</span>(<span class="hljs-number">1L</span>, <span class="hljs-string">&quot;Ann&quot;</span>)
<span class="hljs-keyword">val</span> u2 = u.copy(name = <span class="hljs-string">&quot;Bob&quot;</span>)
<span class="hljs-keyword">val</span> <span class="hljs-type">User</span>(id, name, _) = u2
""");
    }

    [Fact]
    public void Chars()
    {
        AssertHighlighter("scala",
"""
val a = 'a'
val nl = '\n'
val quote = '\''
val backslash = '\\'
val uni = '\u0041'
val symbols = List('a, 'b)
val mixed = f('x', 'y)
val notChar = 'abc'
""",
"""
<span class="hljs-keyword">val</span> a = <span class="hljs-string">&#x27;a&#x27;</span>
<span class="hljs-keyword">val</span> nl = <span class="hljs-string">&#x27;\n&#x27;</span>
<span class="hljs-keyword">val</span> quote = <span class="hljs-string">&#x27;\&#x27;&#x27;</span>
<span class="hljs-keyword">val</span> backslash = <span class="hljs-string">&#x27;\\&#x27;</span>
<span class="hljs-keyword">val</span> uni = <span class="hljs-string">&#x27;\u0041&#x27;</span>
<span class="hljs-keyword">val</span> symbols = <span class="hljs-type">List</span>(&#x27;a, &#x27;b)
<span class="hljs-keyword">val</span> mixed = f(<span class="hljs-string">&#x27;x&#x27;</span>, &#x27;y)
<span class="hljs-keyword">val</span> notChar = &#x27;abc&#x27;
""");
    }

    [Fact]
    public void ClassWithComment()
    {
        AssertHighlighter("scala",
"""
class Foo /* comment */ (x: Int) // trailing
class Bar[T /* type */](y: T)
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Foo</span> <span class="hljs-comment">/* comment */</span> (<span class="hljs-params">x: <span class="hljs-type">Int</span></span>) <span class="hljs-comment">// trailing</span></span>
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Bar</span>[<span class="hljs-type">T</span> <span class="hljs-comment">/* type */</span>](<span class="hljs-params">y: <span class="hljs-type">T</span></span>)</span>
""");
    }

    [Fact]
    public void Classes()
    {
        AssertHighlighter("scala",
"""
class Point(val x: Int, val y: Int) extends Shape with Serializable {
  def distance(other: Point): Double = math.sqrt(dx * dx + dy * dy)
}

case class Person(name: String, age: Int)

abstract class Animal {
  def speak: String
}

sealed trait Expr
case class Num(value: Int) extends Expr
case class Add(left: Expr, right: Expr) extends Expr
case object Zero extends Expr

trait Greeter {
  def greet(name: String): Unit = println(s"Hello, $name")
}

class Stack[A] {
  private var elements: List[A] = Nil
  def push(x: A): Unit = elements = x :: elements
  def pop(): A = { val h = elements.head; elements = elements.tail; h }
}

final class Box[+T](value: T)
class Container[T <: AnyVal](items: Seq[T])
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Point</span>(<span class="hljs-params">val x: <span class="hljs-type">Int</span>, val y: <span class="hljs-type">Int</span></span>) <span class="hljs-keyword">extends</span> <span class="hljs-title">Shape</span> <span class="hljs-keyword">with</span> <span class="hljs-title">Serializable</span> </span>{
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">distance</span></span>(other: <span class="hljs-type">Point</span>): <span class="hljs-type">Double</span> = math.sqrt(dx * dx + dy * dy)
}

<span class="hljs-keyword">case</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Person</span>(<span class="hljs-params">name: <span class="hljs-type">String</span>, age: <span class="hljs-type">Int</span></span>)</span>

<span class="hljs-keyword">abstract</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Animal</span> </span>{
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">speak</span></span>: <span class="hljs-type">String</span>
}

<span class="hljs-keyword">sealed</span> <span class="hljs-class"><span class="hljs-keyword">trait</span> <span class="hljs-title">Expr</span></span>
<span class="hljs-keyword">case</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Num</span>(<span class="hljs-params">value: <span class="hljs-type">Int</span></span>) <span class="hljs-keyword">extends</span> <span class="hljs-title">Expr</span></span>
<span class="hljs-keyword">case</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Add</span>(<span class="hljs-params">left: <span class="hljs-type">Expr</span>, right: <span class="hljs-type">Expr</span></span>) <span class="hljs-keyword">extends</span> <span class="hljs-title">Expr</span></span>
<span class="hljs-keyword">case</span> <span class="hljs-class"><span class="hljs-keyword">object</span> <span class="hljs-title">Zero</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">Expr</span></span>

<span class="hljs-class"><span class="hljs-keyword">trait</span> <span class="hljs-title">Greeter</span> </span>{
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">greet</span></span>(name: <span class="hljs-type">String</span>): <span class="hljs-type">Unit</span> = println(<span class="hljs-string">s&quot;Hello, <span class="hljs-subst">$name</span>&quot;</span>)
}

<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Stack</span>[<span class="hljs-type">A</span>] </span>{
  <span class="hljs-keyword">private</span> <span class="hljs-keyword">var</span> elements: <span class="hljs-type">List</span>[<span class="hljs-type">A</span>] = <span class="hljs-type">Nil</span>
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">push</span></span>(x: <span class="hljs-type">A</span>): <span class="hljs-type">Unit</span> = elements = x :: elements
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">pop</span></span>(): <span class="hljs-type">A</span> = { <span class="hljs-keyword">val</span> h = elements.head; elements = elements.tail; h }
}

<span class="hljs-keyword">final</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Box</span>[+<span class="hljs-type">T</span>](<span class="hljs-params">value: <span class="hljs-type">T</span></span>)</span>
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Container</span>[<span class="hljs-type">T</span> &lt;: <span class="hljs-type">AnyVal</span>](<span class="hljs-params">items: <span class="hljs-type">Seq</span>[<span class="hljs-type">T</span>]</span>)</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("scala",
"""
// line comment TODO: fix
/* block comment */
/**
 * Scaladoc
 * @param x the value
 * @return something
 */
val x = 1 // trailing
""",
"""
<span class="hljs-comment">// line comment <span class="hljs-doctag">TODO:</span> fix</span>
<span class="hljs-comment">/* block comment */</span>
<span class="hljs-comment">/**
 * Scaladoc
 * @param x the value
 * @return something
 */</span>
<span class="hljs-keyword">val</span> x = <span class="hljs-number">1</span> <span class="hljs-comment">// trailing</span>
""");
    }

    [Fact]
    public void EndMarkers()
    {
        AssertHighlighter("scala",
"""
end if
  end while
end extension
end
endpoint = 1
""",
"""
<span class="hljs-keyword">end</span> <span class="hljs-keyword">if</span>
  <span class="hljs-keyword">end</span> <span class="hljs-keyword">while</span>
<span class="hljs-keyword">end</span> <span class="hljs-keyword">extension</span>
<span class="hljs-keyword">end</span>
endpoint = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void EnumsScala3()
    {
        AssertHighlighter("scala",
"""
enum Color:
  case Red, Green, Blue

enum Planet(mass: Double, radius: Double):
  case Mercury extends Planet(3.303e+23, 2.4397e6)
  case Earth extends Planet(5.976e+24, 6.37814e6)

  def surfaceGravity: Double = G * mass / (radius * radius)
end Planet

enum Option[+T]:
  case Some(x: T)
  case None
""",
"""
<span class="hljs-keyword">enum</span> <span class="hljs-type">Color</span>:
  <span class="hljs-keyword">case</span> <span class="hljs-type">Red</span>, <span class="hljs-type">Green</span>, <span class="hljs-type">Blue</span>

<span class="hljs-keyword">enum</span> <span class="hljs-type">Planet</span>(mass: <span class="hljs-type">Double</span>, radius: <span class="hljs-type">Double</span>):
  <span class="hljs-keyword">case</span> <span class="hljs-type">Mercury</span> <span class="hljs-keyword">extends</span> <span class="hljs-type">Planet</span>(<span class="hljs-number">3.303e+23</span>, <span class="hljs-number">2.4397e6</span>)
  <span class="hljs-keyword">case</span> <span class="hljs-type">Earth</span> <span class="hljs-keyword">extends</span> <span class="hljs-type">Planet</span>(<span class="hljs-number">5.976e+24</span>, <span class="hljs-number">6.37814e6</span>)

  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">surfaceGravity</span></span>: <span class="hljs-type">Double</span> = <span class="hljs-type">G</span> * mass / (radius * radius)
<span class="hljs-keyword">end</span> <span class="hljs-type">Planet</span>

<span class="hljs-keyword">enum</span> <span class="hljs-type">Option</span>[+<span class="hljs-type">T</span>]:
  <span class="hljs-keyword">case</span> <span class="hljs-type">Some</span>(x: <span class="hljs-type">T</span>)
  <span class="hljs-keyword">case</span> <span class="hljs-type">None</span>
""");
    }

    [Fact]
    public void Exceptions()
    {
        AssertHighlighter("scala",
"""
try {
  riskyOperation()
} catch {
  case e: IOException => println("IO error")
  case NonFatal(e) => throw new RuntimeException(e)
} finally {
  cleanup()
}
""",
"""
<span class="hljs-keyword">try</span> {
  riskyOperation()
} <span class="hljs-keyword">catch</span> {
  <span class="hljs-keyword">case</span> e: <span class="hljs-type">IOException</span> =&gt; println(<span class="hljs-string">&quot;IO error&quot;</span>)
  <span class="hljs-keyword">case</span> <span class="hljs-type">NonFatal</span>(e) =&gt; <span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-type">RuntimeException</span>(e)
} <span class="hljs-keyword">finally</span> {
  cleanup()
}
""");
    }

    [Fact]
    public void ExtensionMethods()
    {
        AssertHighlighter("scala",
"""
extension (c: Circle)
  def circumference: Double = c.radius * math.Pi * 2

extension [T](xs: List[T])
  def second: T = xs.tail.head
  def third: T = xs.tail.tail.head
end extension

extension (s: String) def shout: String = s.toUpperCase
""",
"""
<span class="hljs-keyword">extension</span> (c: <span class="hljs-type">Circle</span>)
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">circumference</span></span>: <span class="hljs-type">Double</span> = c.radius * math.<span class="hljs-type">Pi</span> * <span class="hljs-number">2</span>

<span class="hljs-keyword">extension</span> [<span class="hljs-type">T</span>](xs: <span class="hljs-type">List</span>[<span class="hljs-type">T</span>])
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">second</span></span>: <span class="hljs-type">T</span> = xs.tail.head
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">third</span></span>: <span class="hljs-type">T</span> = xs.tail.tail.head
<span class="hljs-keyword">end</span> <span class="hljs-keyword">extension</span>

<span class="hljs-keyword">extension</span> (s: <span class="hljs-type">String</span>) <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">shout</span></span>: <span class="hljs-type">String</span> = s.toUpperCase
""");
    }

    [Fact]
    public void ForComprehension()
    {
        AssertHighlighter("scala",
"""
val result = for
  a <- Some(1)
  b <- Some(2)
yield a + b
""",
"""
<span class="hljs-keyword">val</span> result = <span class="hljs-keyword">for</span>
  a &lt;- <span class="hljs-type">Some</span>(<span class="hljs-number">1</span>)
  b &lt;- <span class="hljs-type">Some</span>(<span class="hljs-number">2</span>)
<span class="hljs-keyword">yield</span> a + b
""");
    }

    [Fact]
    public void Functions()
    {
        AssertHighlighter("scala",
"""
def add(a: Int, b: Int): Int = a + b
def greet(name: String = "World"): Unit = println(name)
def curried(a: Int)(b: Int): Int = a + b
def generic[T](x: T): T = x
def varargs(xs: Int*): Int = xs.sum
def byName(x: => Int): Int = x
implicit def intToString(x: Int): String = x.toString
private def helper = 42
override def toString: String = "custom"
def +(other: Vec): Vec = Vec(x + other.x)
def unary_- : Vec = Vec(-x)
""",
"""
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">add</span></span>(a: <span class="hljs-type">Int</span>, b: <span class="hljs-type">Int</span>): <span class="hljs-type">Int</span> = a + b
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">greet</span></span>(name: <span class="hljs-type">String</span> = <span class="hljs-string">&quot;World&quot;</span>): <span class="hljs-type">Unit</span> = println(name)
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">curried</span></span>(a: <span class="hljs-type">Int</span>)(b: <span class="hljs-type">Int</span>): <span class="hljs-type">Int</span> = a + b
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">generic</span></span>[<span class="hljs-type">T</span>](x: <span class="hljs-type">T</span>): <span class="hljs-type">T</span> = x
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">varargs</span></span>(xs: <span class="hljs-type">Int</span>*): <span class="hljs-type">Int</span> = xs.sum
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">byName</span></span>(x: =&gt; <span class="hljs-type">Int</span>): <span class="hljs-type">Int</span> = x
<span class="hljs-keyword">implicit</span> <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">intToString</span></span>(x: <span class="hljs-type">Int</span>): <span class="hljs-type">String</span> = x.toString
<span class="hljs-keyword">private</span> <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">helper</span> </span>= <span class="hljs-number">42</span>
<span class="hljs-keyword">override</span> <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">toString</span></span>: <span class="hljs-type">String</span> = <span class="hljs-string">&quot;custom&quot;</span>
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">+</span></span>(other: <span class="hljs-type">Vec</span>): <span class="hljs-type">Vec</span> = <span class="hljs-type">Vec</span>(x + other.x)
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">unary_-</span> </span>: <span class="hljs-type">Vec</span> = <span class="hljs-type">Vec</span>(-x)
""");
    }

    [Fact]
    public void Futures()
    {
        AssertHighlighter("scala",
"""
import scala.concurrent.Future
import scala.concurrent.ExecutionContext.Implicits.global

val f: Future[Int] = Future {
  Thread.sleep(100)
  42
}
f.onComplete {
  case Success(value) => println(s"Got $value")
  case Failure(ex) => println(ex.getMessage)
}
""",
"""
<span class="hljs-keyword">import</span> scala.concurrent.<span class="hljs-type">Future</span>
<span class="hljs-keyword">import</span> scala.concurrent.<span class="hljs-type">ExecutionContext</span>.<span class="hljs-type">Implicits</span>.global

<span class="hljs-keyword">val</span> f: <span class="hljs-type">Future</span>[<span class="hljs-type">Int</span>] = <span class="hljs-type">Future</span> {
  <span class="hljs-type">Thread</span>.sleep(<span class="hljs-number">100</span>)
  <span class="hljs-number">42</span>
}
f.onComplete {
  <span class="hljs-keyword">case</span> <span class="hljs-type">Success</span>(value) =&gt; println(<span class="hljs-string">s&quot;Got <span class="hljs-subst">$value</span>&quot;</span>)
  <span class="hljs-keyword">case</span> <span class="hljs-type">Failure</span>(ex) =&gt; println(ex.getMessage)
}
""");
    }

    [Fact]
    public void GenericsVariance()
    {
        AssertHighlighter("scala",
"""
trait Function1[-T, +R] {
  def apply(t: T): R
}
def sum[N: Numeric](xs: List[N]): N = xs.sum
def upper[T >: Nothing <: Any](x: T): T = x
class Cell[T](init: T)
""",
"""
<span class="hljs-class"><span class="hljs-keyword">trait</span> <span class="hljs-title">Function1</span>[-<span class="hljs-type">T</span>, +<span class="hljs-type">R</span>] </span>{
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">apply</span></span>(t: <span class="hljs-type">T</span>): <span class="hljs-type">R</span>
}
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">sum</span></span>[<span class="hljs-type">N</span>: <span class="hljs-type">Numeric</span>](xs: <span class="hljs-type">List</span>[<span class="hljs-type">N</span>]): <span class="hljs-type">N</span> = xs.sum
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">upper</span></span>[<span class="hljs-type">T</span> &gt;: <span class="hljs-type">Nothing</span> &lt;: <span class="hljs-type">Any</span>](x: <span class="hljs-type">T</span>): <span class="hljs-type">T</span> = x
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Cell</span>[<span class="hljs-type">T</span>](<span class="hljs-params">init: <span class="hljs-type">T</span></span>)</span>
""");
    }

    [Fact]
    public void GivenUsing()
    {
        AssertHighlighter("scala",
"""
trait Ord[T]:
  def compare(x: T, y: T): Int

given intOrd: Ord[Int] with
  def compare(x: Int, y: Int): Int = x - y

given Ord[String] = new Ord[String]:
  def compare(x: String, y: String) = x.compareTo(y)

def max[T](x: T, y: T)(using ord: Ord[T]): T =
  if ord.compare(x, y) > 0 then x else y

def printAll(xs: List[Int])(using Ord[Int]): Unit = ()
max(1, 2)(using intOrd)
import scala.math.Ordering.given
""",
"""
<span class="hljs-class"><span class="hljs-keyword">trait</span> <span class="hljs-title">Ord</span>[<span class="hljs-type">T</span>]</span>:
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">compare</span></span>(x: <span class="hljs-type">T</span>, y: <span class="hljs-type">T</span>): <span class="hljs-type">Int</span>

<span class="hljs-keyword">given</span> intOrd: <span class="hljs-type">Ord</span>[<span class="hljs-type">Int</span>] <span class="hljs-keyword">with</span>
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">compare</span></span>(x: <span class="hljs-type">Int</span>, y: <span class="hljs-type">Int</span>): <span class="hljs-type">Int</span> = x - y

<span class="hljs-keyword">given</span> <span class="hljs-type">Ord</span>[<span class="hljs-type">String</span>] = <span class="hljs-keyword">new</span> <span class="hljs-type">Ord</span>[<span class="hljs-type">String</span>]:
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">compare</span></span>(x: <span class="hljs-type">String</span>, y: <span class="hljs-type">String</span>) = x.compareTo(y)

<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">max</span></span>[<span class="hljs-type">T</span>](x: <span class="hljs-type">T</span>, y: <span class="hljs-type">T</span>)(<span class="hljs-keyword">using</span> ord: <span class="hljs-type">Ord</span>[<span class="hljs-type">T</span>]): <span class="hljs-type">T</span> =
  <span class="hljs-keyword">if</span> ord.compare(x, y) &gt; <span class="hljs-number">0</span> <span class="hljs-keyword">then</span> x <span class="hljs-keyword">else</span> y

<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">printAll</span></span>(xs: <span class="hljs-type">List</span>[<span class="hljs-type">Int</span>])(<span class="hljs-keyword">using</span> <span class="hljs-type">Ord</span>[<span class="hljs-type">Int</span>]): <span class="hljs-type">Unit</span> = ()
max(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>)(<span class="hljs-keyword">using</span> intOrd)
<span class="hljs-keyword">import</span> scala.math.<span class="hljs-type">Ordering</span>.<span class="hljs-keyword">given</span>
""");
    }

    [Fact]
    public void Hello()
    {
        AssertHighlighter("scala",
"""
package com.example

import scala.collection.mutable
import scala.concurrent.{Future, ExecutionContext}

object HelloWorld {
  def main(args: Array[String]): Unit = {
    println("Hello, world!")
  }
}
""",
"""
<span class="hljs-keyword">package</span> com.example

<span class="hljs-keyword">import</span> scala.collection.mutable
<span class="hljs-keyword">import</span> scala.concurrent.{<span class="hljs-type">Future</span>, <span class="hljs-type">ExecutionContext</span>}

<span class="hljs-class"><span class="hljs-keyword">object</span> <span class="hljs-title">HelloWorld</span> </span>{
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">main</span></span>(args: <span class="hljs-type">Array</span>[<span class="hljs-type">String</span>]): <span class="hljs-type">Unit</span> = {
    println(<span class="hljs-string">&quot;Hello, world!&quot;</span>)
  }
}
""");
    }

    [Fact]
    public void HelloScala3()
    {
        AssertHighlighter("scala",
"""
@main def hello(): Unit =
  println("Hello, Scala 3!")
""",
"""
<span class="hljs-meta">@main</span> <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">hello</span></span>(): <span class="hljs-type">Unit</span> =
  println(<span class="hljs-string">&quot;Hello, Scala 3!&quot;</span>)
""");
    }

    [Fact]
    public void ImplicitsScala2()
    {
        AssertHighlighter("scala",
"""
implicit val ec: ExecutionContext = ExecutionContext.global
implicit class RichInt(val self: Int) extends AnyVal {
  def squared: Int = self * self
}
def sort[T](xs: List[T])(implicit ord: Ordering[T]): List[T] = xs.sorted
""",
"""
<span class="hljs-keyword">implicit</span> <span class="hljs-keyword">val</span> ec: <span class="hljs-type">ExecutionContext</span> = <span class="hljs-type">ExecutionContext</span>.global
<span class="hljs-keyword">implicit</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">RichInt</span>(<span class="hljs-params">val self: <span class="hljs-type">Int</span></span>) <span class="hljs-keyword">extends</span> <span class="hljs-title">AnyVal</span> </span>{
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">squared</span></span>: <span class="hljs-type">Int</span> = self * self
}
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">sort</span></span>[<span class="hljs-type">T</span>](xs: <span class="hljs-type">List</span>[<span class="hljs-type">T</span>])(<span class="hljs-keyword">implicit</span> ord: <span class="hljs-type">Ordering</span>[<span class="hljs-type">T</span>]): <span class="hljs-type">List</span>[<span class="hljs-type">T</span>] = xs.sorted
""");
    }

    [Fact]
    public void Interpolation()
    {
        AssertHighlighter("scala",
""""
val name = "Scala"
val s1 = s"Hello, $name!"
val s2 = s"1 + 1 = ${1 + 1}"
val f1 = f"$height%2.2f meters"
val raw1 = raw"a\nb"
val multi = s"""Name: $name
  |Age: ${person.age}""".stripMargin
val json = json"""{"a": $x}"""
val esc = s"cost: $$5 and \t tab"
"""",
"""
<span class="hljs-keyword">val</span> name = <span class="hljs-string">&quot;Scala&quot;</span>
<span class="hljs-keyword">val</span> s1 = <span class="hljs-string">s&quot;Hello, <span class="hljs-subst">$name</span>!&quot;</span>
<span class="hljs-keyword">val</span> s2 = <span class="hljs-string">s&quot;1 + 1 = <span class="hljs-subst">${1 + 1}</span>&quot;</span>
<span class="hljs-keyword">val</span> f1 = <span class="hljs-string">f&quot;<span class="hljs-subst">$height</span>%2.2f meters&quot;</span>
<span class="hljs-keyword">val</span> raw1 = <span class="hljs-string">raw&quot;a\nb&quot;</span>
<span class="hljs-keyword">val</span> multi = <span class="hljs-string">s&quot;&quot;&quot;Name: <span class="hljs-subst">$name</span>
  |Age: <span class="hljs-subst">${person.age}</span>&quot;&quot;&quot;</span>.stripMargin
<span class="hljs-keyword">val</span> json = <span class="hljs-string">json&quot;&quot;&quot;{&quot;a&quot;: <span class="hljs-subst">$x</span>}&quot;&quot;&quot;</span>
<span class="hljs-keyword">val</span> esc = <span class="hljs-string">s&quot;cost: $<span class="hljs-subst">$5</span> and \t tab&quot;</span>
""");
    }

    [Fact]
    public void InterpolationMultiline()
    {
        AssertHighlighter("scala",
"""""""
val sql = sql"""SELECT * FROM users WHERE id = $id"""
val msg = s"""Hello "$name"!"""
val empty = s""
val emptyTriple = s""""""
val single = s"\"quoted\" $x"
""""""",
"""
<span class="hljs-keyword">val</span> sql = <span class="hljs-string">sql&quot;&quot;&quot;SELECT * FROM users WHERE id = <span class="hljs-subst">$id</span>&quot;&quot;&quot;</span>
<span class="hljs-keyword">val</span> msg = <span class="hljs-string">s&quot;&quot;&quot;Hello &quot;<span class="hljs-subst">$name</span>&quot;!&quot;&quot;&quot;</span>
<span class="hljs-keyword">val</span> empty = <span class="hljs-string">s&quot;&quot;</span>
<span class="hljs-keyword">val</span> emptyTriple = <span class="hljs-string">s&quot;&quot;&quot;&quot;&quot;&quot;</span>
<span class="hljs-keyword">val</span> single = <span class="hljs-string">s&quot;\&quot;quoted\&quot; <span class="hljs-subst">$x</span>&quot;</span>
""");
    }

    [Fact]
    public void LambdasCollections()
    {
        AssertHighlighter("scala",
"""
val nums = List(1, 2, 3, 4)
val doubled = nums.map(_ * 2)
val evens = nums.filter(x => x % 2 == 0)
val sum = nums.foldLeft(0)((acc, x) => acc + x)
val pairs = for {
  x <- nums
  y <- List("a", "b")
  if x > 1
} yield (x, y)
val m = Map("a" -> 1, "b" -> 2)
nums.foreach { n =>
  println(n)
}
""",
"""
<span class="hljs-keyword">val</span> nums = <span class="hljs-type">List</span>(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>, <span class="hljs-number">4</span>)
<span class="hljs-keyword">val</span> doubled = nums.map(_ * <span class="hljs-number">2</span>)
<span class="hljs-keyword">val</span> evens = nums.filter(x =&gt; x % <span class="hljs-number">2</span> == <span class="hljs-number">0</span>)
<span class="hljs-keyword">val</span> sum = nums.foldLeft(<span class="hljs-number">0</span>)((acc, x) =&gt; acc + x)
<span class="hljs-keyword">val</span> pairs = <span class="hljs-keyword">for</span> {
  x &lt;- nums
  y &lt;- <span class="hljs-type">List</span>(<span class="hljs-string">&quot;a&quot;</span>, <span class="hljs-string">&quot;b&quot;</span>)
  <span class="hljs-keyword">if</span> x &gt; <span class="hljs-number">1</span>
} <span class="hljs-keyword">yield</span> (x, y)
<span class="hljs-keyword">val</span> m = <span class="hljs-type">Map</span>(<span class="hljs-string">&quot;a&quot;</span> -&gt; <span class="hljs-number">1</span>, <span class="hljs-string">&quot;b&quot;</span> -&gt; <span class="hljs-number">2</span>)
nums.foreach { n =&gt;
  println(n)
}
""");
    }

    [Fact]
    public void LazyVals()
    {
        AssertHighlighter("scala",
"""
lazy val expensive: Int = compute()
var mutable = 0
val immutable = "x"
final val Constant = 3
""",
"""
<span class="hljs-keyword">lazy</span> <span class="hljs-keyword">val</span> expensive: <span class="hljs-type">Int</span> = compute()
<span class="hljs-keyword">var</span> mutable = <span class="hljs-number">0</span>
<span class="hljs-keyword">val</span> immutable = <span class="hljs-string">&quot;x&quot;</span>
<span class="hljs-keyword">final</span> <span class="hljs-keyword">val</span> <span class="hljs-type">Constant</span> = <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("scala",
"""
val a = 42
val b = 3.14
val c = 1e10
val d = 0xFF
val e = 100L
val f = 2.5f
val g = 1_000_000
val h = -7
val i = .5
val j = 1.5e-3
""",
"""
<span class="hljs-keyword">val</span> a = <span class="hljs-number">42</span>
<span class="hljs-keyword">val</span> b = <span class="hljs-number">3.14</span>
<span class="hljs-keyword">val</span> c = <span class="hljs-number">1e10</span>
<span class="hljs-keyword">val</span> d = <span class="hljs-number">0xFF</span>
<span class="hljs-keyword">val</span> e = <span class="hljs-number">100L</span>
<span class="hljs-keyword">val</span> f = <span class="hljs-number">2.5f</span>
<span class="hljs-keyword">val</span> g = <span class="hljs-number">1_000_000</span>
<span class="hljs-keyword">val</span> h = <span class="hljs-number">-7</span>
<span class="hljs-keyword">val</span> i = <span class="hljs-number">.5</span>
<span class="hljs-keyword">val</span> j = <span class="hljs-number">1.5e-3</span>
""");
    }

    [Fact]
    public void NumericLiterals()
    {
        AssertHighlighter("scala",
"""
val a = 1_000_000
val b = 100L
val c = 0xFF_FFL
val d = 2.5f
val e = 3.0d
val f = 1e3F
val g = 1.
val h = 0x1F
val i = 5.toString
val j = 1_0.2_5
""",
"""
<span class="hljs-keyword">val</span> a = <span class="hljs-number">1_000_000</span>
<span class="hljs-keyword">val</span> b = <span class="hljs-number">100L</span>
<span class="hljs-keyword">val</span> c = <span class="hljs-number">0xFF_FFL</span>
<span class="hljs-keyword">val</span> d = <span class="hljs-number">2.5f</span>
<span class="hljs-keyword">val</span> e = <span class="hljs-number">3.0d</span>
<span class="hljs-keyword">val</span> f = <span class="hljs-number">1e3F</span>
<span class="hljs-keyword">val</span> g = <span class="hljs-number">1</span>.
<span class="hljs-keyword">val</span> h = <span class="hljs-number">0x1F</span>
<span class="hljs-keyword">val</span> i = <span class="hljs-number">5</span>.toString
<span class="hljs-keyword">val</span> j = <span class="hljs-number">1_0.2_5</span>
""");
    }

    [Fact]
    public void ObjectsCompanions()
    {
        AssertHighlighter("scala",
"""
object Person {
  def apply(name: String): Person = new Person(name, 0)
  val Default = Person("nobody")
}

object Main extends App {
  println("running")
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">object</span> <span class="hljs-title">Person</span> </span>{
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">apply</span></span>(name: <span class="hljs-type">String</span>): <span class="hljs-type">Person</span> = <span class="hljs-keyword">new</span> <span class="hljs-type">Person</span>(name, <span class="hljs-number">0</span>)
  <span class="hljs-keyword">val</span> <span class="hljs-type">Default</span> = <span class="hljs-type">Person</span>(<span class="hljs-string">&quot;nobody&quot;</span>)
}

<span class="hljs-class"><span class="hljs-keyword">object</span> <span class="hljs-title">Main</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">App</span> </span>{
  println(<span class="hljs-string">&quot;running&quot;</span>)
}
""");
    }

    [Fact]
    public void OpaqueInline()
    {
        AssertHighlighter("scala",
"""
object Logarithms:
  opaque type Logarithm = Double

  object Logarithm:
    def apply(d: Double): Logarithm = math.log(d)

inline def debug(inline msg: String): Unit = println(msg)
transparent inline def choose(b: Boolean): Any = if b then 1 else "one"
val x = foo.inline
""",
"""
<span class="hljs-class"><span class="hljs-keyword">object</span> <span class="hljs-title">Logarithms</span></span>:
  opaque <span class="hljs-class"><span class="hljs-keyword">type</span> <span class="hljs-title">Logarithm</span> </span>= <span class="hljs-type">Double</span>

  <span class="hljs-class"><span class="hljs-keyword">object</span> <span class="hljs-title">Logarithm</span></span>:
    <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">apply</span></span>(d: <span class="hljs-type">Double</span>): <span class="hljs-type">Logarithm</span> = math.log(d)

<span class="hljs-keyword">inline</span> <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">debug</span></span>(<span class="hljs-keyword">inline</span> msg: <span class="hljs-type">String</span>): <span class="hljs-type">Unit</span> = println(msg)
<span class="hljs-keyword">transparent</span> <span class="hljs-keyword">inline</span> <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">choose</span></span>(b: <span class="hljs-type">Boolean</span>): <span class="hljs-type">Any</span> = <span class="hljs-keyword">if</span> b <span class="hljs-keyword">then</span> <span class="hljs-number">1</span> <span class="hljs-keyword">else</span> <span class="hljs-string">&quot;one&quot;</span>
<span class="hljs-keyword">val</span> x = foo.inline
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("scala",
"""
val a = b + c - d * e / f % g
val t = a == b && c != d || !e
val l = 1 :: 2 :: Nil
val m = xs ++ ys
val arrow = (x: Int) => x + 1
val gen = for (x <- xs) yield x
a += 1
""",
"""
<span class="hljs-keyword">val</span> a = b + c - d * e / f % g
<span class="hljs-keyword">val</span> t = a == b &amp;&amp; c != d || !e
<span class="hljs-keyword">val</span> l = <span class="hljs-number">1</span> :: <span class="hljs-number">2</span> :: <span class="hljs-type">Nil</span>
<span class="hljs-keyword">val</span> m = xs ++ ys
<span class="hljs-keyword">val</span> arrow = (x: <span class="hljs-type">Int</span>) =&gt; x + <span class="hljs-number">1</span>
<span class="hljs-keyword">val</span> gen = <span class="hljs-keyword">for</span> (x &lt;- xs) <span class="hljs-keyword">yield</span> x
a += <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void PackageObjects()
    {
        AssertHighlighter("scala",
"""
package object utils {
  val Pi = 3.14159
}
package com.example.app
import java.util.{List => JList, _}
export scalaUtils.*
""",
"""
<span class="hljs-keyword">package</span> <span class="hljs-class"><span class="hljs-keyword">object</span> <span class="hljs-title">utils</span> </span>{
  <span class="hljs-keyword">val</span> <span class="hljs-type">Pi</span> = <span class="hljs-number">3.14159</span>
}
<span class="hljs-keyword">package</span> com.example.app
<span class="hljs-keyword">import</span> java.util.{<span class="hljs-type">List</span> =&gt; <span class="hljs-type">JList</span>, _}
<span class="hljs-keyword">export</span> scalaUtils.*
""");
    }

    [Fact]
    public void PatternMatching()
    {
        AssertHighlighter("scala",
"""
def describe(x: Any): String = x match {
  case 0 => "zero"
  case i: Int if i > 0 => "positive int"
  case s: String => s"string: $s"
  case Person(name, age) => s"$name is $age"
  case head :: tail => "list"
  case (a, b) => "tuple"
  case _ => "unknown"
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">describe</span></span>(x: <span class="hljs-type">Any</span>): <span class="hljs-type">String</span> = x <span class="hljs-keyword">match</span> {
  <span class="hljs-keyword">case</span> <span class="hljs-number">0</span> =&gt; <span class="hljs-string">&quot;zero&quot;</span>
  <span class="hljs-keyword">case</span> i: <span class="hljs-type">Int</span> <span class="hljs-keyword">if</span> i &gt; <span class="hljs-number">0</span> =&gt; <span class="hljs-string">&quot;positive int&quot;</span>
  <span class="hljs-keyword">case</span> s: <span class="hljs-type">String</span> =&gt; <span class="hljs-string">s&quot;string: <span class="hljs-subst">$s</span>&quot;</span>
  <span class="hljs-keyword">case</span> <span class="hljs-type">Person</span>(name, age) =&gt; <span class="hljs-string">s&quot;<span class="hljs-subst">$name</span> is <span class="hljs-subst">$age</span>&quot;</span>
  <span class="hljs-keyword">case</span> head :: tail =&gt; <span class="hljs-string">&quot;list&quot;</span>
  <span class="hljs-keyword">case</span> (a, b) =&gt; <span class="hljs-string">&quot;tuple&quot;</span>
  <span class="hljs-keyword">case</span> _ =&gt; <span class="hljs-string">&quot;unknown&quot;</span>
}
""");
    }

    [Fact]
    public void SbtBuild()
    {
        AssertHighlighter("scala",
"""
ThisBuild / scalaVersion := "3.3.1"
ThisBuild / organization := "com.example"

lazy val root = (project in file("."))
  .settings(
    name := "hello",
    libraryDependencies ++= Seq(
      "org.typelevel" %% "cats-core" % "2.10.0",
      "org.scalatest" %% "scalatest" % "3.2.17" % Test
    )
  )
""",
"""
<span class="hljs-type">ThisBuild</span> / scalaVersion := <span class="hljs-string">&quot;3.3.1&quot;</span>
<span class="hljs-type">ThisBuild</span> / organization := <span class="hljs-string">&quot;com.example&quot;</span>

<span class="hljs-keyword">lazy</span> <span class="hljs-keyword">val</span> root = (project in file(<span class="hljs-string">&quot;.&quot;</span>))
  .settings(
    name := <span class="hljs-string">&quot;hello&quot;</span>,
    libraryDependencies ++= <span class="hljs-type">Seq</span>(
      <span class="hljs-string">&quot;org.typelevel&quot;</span> %% <span class="hljs-string">&quot;cats-core&quot;</span> % <span class="hljs-string">&quot;2.10.0&quot;</span>,
      <span class="hljs-string">&quot;org.scalatest&quot;</span> %% <span class="hljs-string">&quot;scalatest&quot;</span> % <span class="hljs-string">&quot;3.2.17&quot;</span> % <span class="hljs-type">Test</span>
    )
  )
""");
    }

    [Fact]
    public void Scala3Syntax()
    {
        AssertHighlighter("scala",
"""
object Main:
  def main(args: Array[String]): Unit =
    val x = 10
    if x > 5 then
      println("big")
    else
      println("small")
    end if
    for i <- 1 to 3 do println(i)
    while x > 0 do x -= 1
  end main
end Main

class Counter:
  private var count = 0
  def increment(): Unit = count += 1
end Counter
""",
"""
<span class="hljs-class"><span class="hljs-keyword">object</span> <span class="hljs-title">Main</span></span>:
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">main</span></span>(args: <span class="hljs-type">Array</span>[<span class="hljs-type">String</span>]): <span class="hljs-type">Unit</span> =
    <span class="hljs-keyword">val</span> x = <span class="hljs-number">10</span>
    <span class="hljs-keyword">if</span> x &gt; <span class="hljs-number">5</span> <span class="hljs-keyword">then</span>
      println(<span class="hljs-string">&quot;big&quot;</span>)
    <span class="hljs-keyword">else</span>
      println(<span class="hljs-string">&quot;small&quot;</span>)
    <span class="hljs-keyword">end</span> <span class="hljs-keyword">if</span>
    <span class="hljs-keyword">for</span> i &lt;- <span class="hljs-number">1</span> to <span class="hljs-number">3</span> <span class="hljs-keyword">do</span> println(i)
    <span class="hljs-keyword">while</span> x &gt; <span class="hljs-number">0</span> <span class="hljs-keyword">do</span> x -= <span class="hljs-number">1</span>
  <span class="hljs-keyword">end</span> main
<span class="hljs-keyword">end</span> <span class="hljs-type">Main</span>

<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Counter</span></span>:
  <span class="hljs-keyword">private</span> <span class="hljs-keyword">var</span> count = <span class="hljs-number">0</span>
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">increment</span></span>(): <span class="hljs-type">Unit</span> = count += <span class="hljs-number">1</span>
<span class="hljs-keyword">end</span> <span class="hljs-type">Counter</span>
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("scala",
""""
val s = "Hello \"world\"\n"
val c = 'a'
val sym = 'symbol
val multi = """line one
  |line "two"
  |line three""".stripMargin
val empty = ""
"""",
"""
<span class="hljs-keyword">val</span> s = <span class="hljs-string">&quot;Hello \&quot;world\&quot;\n&quot;</span>
<span class="hljs-keyword">val</span> c = <span class="hljs-string">&#x27;a&#x27;</span>
<span class="hljs-keyword">val</span> sym = &#x27;symbol
<span class="hljs-keyword">val</span> multi = <span class="hljs-string">&quot;&quot;&quot;line one
  |line &quot;two&quot;
  |line three&quot;&quot;&quot;</span>.stripMargin
<span class="hljs-keyword">val</span> empty = <span class="hljs-string">&quot;&quot;</span>
""");
    }

    [Fact]
    public void ThisSuper()
    {
        AssertHighlighter("scala",
"""
class B extends A {
  override def f(): Int = super.f() + this.g()
  def this(x: Int) = this()
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">B</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">A</span> </span>{
  <span class="hljs-keyword">override</span> <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">f</span></span>(): <span class="hljs-type">Int</span> = <span class="hljs-keyword">super</span>.f() + <span class="hljs-keyword">this</span>.g()
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">this</span></span>(x: <span class="hljs-type">Int</span>) = <span class="hljs-keyword">this</span>()
}
""");
    }

    [Fact]
    public void TraitsMixins()
    {
        AssertHighlighter("scala",
"""
trait Logger {
  def log(msg: String): Unit
}
class Service extends BaseService with Logger with Serializable {
  override def log(msg: String): Unit = println(msg)
}
val svc = new Service with Extra
""",
"""
<span class="hljs-class"><span class="hljs-keyword">trait</span> <span class="hljs-title">Logger</span> </span>{
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">log</span></span>(msg: <span class="hljs-type">String</span>): <span class="hljs-type">Unit</span>
}
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Service</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">BaseService</span> <span class="hljs-keyword">with</span> <span class="hljs-title">Logger</span> <span class="hljs-keyword">with</span> <span class="hljs-title">Serializable</span> </span>{
  <span class="hljs-keyword">override</span> <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">log</span></span>(msg: <span class="hljs-type">String</span>): <span class="hljs-type">Unit</span> = println(msg)
}
<span class="hljs-keyword">val</span> svc = <span class="hljs-keyword">new</span> <span class="hljs-type">Service</span> <span class="hljs-keyword">with</span> <span class="hljs-type">Extra</span>
""");
    }

    [Fact]
    public void TypeMembers()
    {
        AssertHighlighter("scala",
"""
type Callback = Int => Unit
type Pair[A] = (A, A)
trait Container {
  type Elem
  def get: Elem
}
type Union = Int | String
type Inter = Readable & Writable
""",
"""
<span class="hljs-class"><span class="hljs-keyword">type</span> <span class="hljs-title">Callback</span> </span>= <span class="hljs-type">Int</span> =&gt; <span class="hljs-type">Unit</span>
<span class="hljs-class"><span class="hljs-keyword">type</span> <span class="hljs-title">Pair</span>[<span class="hljs-type">A</span>] </span>= (<span class="hljs-type">A</span>, <span class="hljs-type">A</span>)
<span class="hljs-class"><span class="hljs-keyword">trait</span> <span class="hljs-title">Container</span> </span>{
  <span class="hljs-class"><span class="hljs-keyword">type</span> <span class="hljs-title">Elem</span></span>
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">get</span></span>: <span class="hljs-type">Elem</span>
}
<span class="hljs-class"><span class="hljs-keyword">type</span> <span class="hljs-title">Union</span> </span>= <span class="hljs-type">Int</span> | <span class="hljs-type">String</span>
<span class="hljs-class"><span class="hljs-keyword">type</span> <span class="hljs-title">Inter</span> </span>= <span class="hljs-type">Readable</span> &amp; <span class="hljs-type">Writable</span>
""");
    }

    [Fact]
    public void UsingDirectives()
    {
        AssertHighlighter("scala",
"""
//> using scala 3.3.1
//> using dep com.lihaoyi::os-lib:0.9.1
//> using options -deprecation -feature

@main def app(): Unit = println(os.pwd)
""",
"""
<span class="hljs-comment">//&gt;</span> <span class="hljs-keyword">using</span> <span class="hljs-type">scala</span> <span class="hljs-string">3.3.1</span>
<span class="hljs-comment">//&gt;</span> <span class="hljs-keyword">using</span> <span class="hljs-type">dep</span> <span class="hljs-string">com.lihaoyi::os-lib:0.9.1</span>
<span class="hljs-comment">//&gt;</span> <span class="hljs-keyword">using</span> <span class="hljs-type">options</span> <span class="hljs-string">-deprecation</span> <span class="hljs-string">-feature</span>

<span class="hljs-meta">@main</span> <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">app</span></span>(): <span class="hljs-type">Unit</span> = println(os.pwd)
""");
    }
}
