namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class KotlinHighlighterTests
{
    [Fact]
    public void Annotations()
    {
        AssertHighlighter("kotlin",
"""
@Target(AnnotationTarget.CLASS, AnnotationTarget.FUNCTION)
@Retention(AnnotationRetention.RUNTIME)
annotation class Fancy(val why: String)

@Fancy("because") class Foo {
    @get:JvmName("getX")
    @set:JvmName("setX")
    var x: Int = 0

    @field:Inject
    lateinit var service: Service

    @Suppress("UNCHECKED_CAST", "DEPRECATION")
    fun f(@param:NotNull value: String) {}

    @JvmStatic
    @Throws(IOException::class)
    fun load() {}
}

@file:JvmName("Utils")
""",
"""
<span class="hljs-meta">@Target(AnnotationTarget.CLASS, AnnotationTarget.FUNCTION)</span>
<span class="hljs-meta">@Retention(AnnotationRetention.RUNTIME)</span>
<span class="hljs-keyword">annotation</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Fancy</span>(<span class="hljs-keyword">val</span> why: String)

<span class="hljs-meta">@Fancy(<span class="hljs-string">&quot;because&quot;</span>)</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Foo</span> {
    <span class="hljs-meta">@get:JvmName</span>(<span class="hljs-string">&quot;getX&quot;</span>)
    <span class="hljs-meta">@set:JvmName</span>(<span class="hljs-string">&quot;setX&quot;</span>)
    <span class="hljs-keyword">var</span> x: <span class="hljs-built_in">Int</span> = <span class="hljs-number">0</span>

    <span class="hljs-meta">@field:Inject</span>
    <span class="hljs-keyword">lateinit</span> <span class="hljs-keyword">var</span> service: Service

    <span class="hljs-meta">@Suppress(<span class="hljs-string">&quot;UNCHECKED_CAST&quot;</span>, <span class="hljs-string">&quot;DEPRECATION&quot;</span>)</span>
    <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">f</span><span class="hljs-params">(<span class="hljs-meta">@param:NotNull</span> value: <span class="hljs-type">String</span>)</span></span> {}

    <span class="hljs-meta">@JvmStatic</span>
    <span class="hljs-meta">@Throws(IOException::class)</span>
    <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">load</span><span class="hljs-params">()</span></span> {}
}

<span class="hljs-meta">@file:JvmName</span>(<span class="hljs-string">&quot;Utils&quot;</span>)
""");
    }

    [Fact]
    public void BacktickNames()
    {
        AssertHighlighter("kotlin",
"""
class Tests {
    @Test
    fun `returns 42 when input is empty`() {
        assertEquals(42, compute(""))
    }

    fun `simple`(x: Int) = x
}
val `in` = 3
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Tests</span> {
    <span class="hljs-meta">@Test</span>
    <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">`returns 42 when input is empty`</span><span class="hljs-params">()</span></span> {
        assertEquals(<span class="hljs-number">42</span>, compute(<span class="hljs-string">&quot;&quot;</span>))
    }

    <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">`simple`</span><span class="hljs-params">(x: <span class="hljs-type">Int</span>)</span></span> = x
}
<span class="hljs-keyword">val</span> `<span class="hljs-keyword">in</span>` = <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void CharEscapes()
    {
        AssertHighlighter("kotlin",
"""
val a = '\t'
val b = '\\'
val c = '\''
val d = "a\\b"
val e = "\u0041"
""",
"""
<span class="hljs-keyword">val</span> a = <span class="hljs-string">&#x27;\t&#x27;</span>
<span class="hljs-keyword">val</span> b = <span class="hljs-string">&#x27;\\&#x27;</span>
<span class="hljs-keyword">val</span> c = <span class="hljs-string">&#x27;\&#x27;&#x27;</span>
<span class="hljs-keyword">val</span> d = <span class="hljs-string">&quot;a\\b&quot;</span>
<span class="hljs-keyword">val</span> e = <span class="hljs-string">&quot;\u0041&quot;</span>
""");
    }

    [Fact]
    public void ClassBodyMembers()
    {
        AssertHighlighter("kotlin",
"""
class Foo<T : Any>(private val bar: T) : Base<T>(bar), Iface where T : Number {
}
class Empty
class WithBody {
}
private class Private(val x: Int)
internal class Internal
class A: B()
class C :D
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Foo</span>&lt;<span class="hljs-type">T : Any</span>&gt;(<span class="hljs-keyword">private</span> <span class="hljs-keyword">val</span> bar: T) : Base&lt;T&gt;(bar), Iface <span class="hljs-keyword">where</span> T : Number {
}
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Empty</span>
<span class="hljs-keyword">class</span> <span class="hljs-title class_">WithBody</span> {
}
<span class="hljs-keyword">private</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Private</span>(<span class="hljs-keyword">val</span> x: <span class="hljs-built_in">Int</span>)
<span class="hljs-keyword">internal</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Internal</span>
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span>: <span class="hljs-type">B</span>()
<span class="hljs-keyword">class</span> <span class="hljs-title class_">C</span> :<span class="hljs-type">D</span>
""");
    }

    [Fact]
    public void Classes()
    {
        AssertHighlighter("kotlin",
"""
data class User(val name: String, val age: Int = 0)

sealed class Result<out T> {
    data class Success<T>(val value: T) : Result<T>()
    data class Error(val message: String) : Result<Nothing>()
    object Loading : Result<Nothing>()
}

class Person private constructor(val name: String) : Comparable<Person>, Serializable {
    override fun compareTo(other: Person): Int = name.compareTo(other.name)

    companion object {
        const val MAX = 10
        fun create(name: String): Person = Person(name)
    }
}

abstract class Shape {
    abstract fun area(): Double
}

open class Base(p: Int)
class Derived(p: Int) : Base(p)
interface Clickable {
    fun click()
    fun showOff() = println("I'm clickable!")
}
enum class Color(val rgb: Int) {
    RED(0xFF0000), GREEN(0x00FF00), BLUE(0x0000FF)
}
""",
"""
<span class="hljs-keyword">data</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">User</span>(<span class="hljs-keyword">val</span> name: String, <span class="hljs-keyword">val</span> age: <span class="hljs-built_in">Int</span> = <span class="hljs-number">0</span>)

<span class="hljs-keyword">sealed</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Result</span>&lt;<span class="hljs-type">out T</span>&gt; {
    <span class="hljs-keyword">data</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Success</span>&lt;<span class="hljs-type">T</span>&gt;(<span class="hljs-keyword">val</span> value: T) : Result&lt;T&gt;()
    <span class="hljs-keyword">data</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Error</span>(<span class="hljs-keyword">val</span> message: String) : Result&lt;<span class="hljs-built_in">Nothing</span>&gt;()
    <span class="hljs-keyword">object</span> <span class="hljs-title class_">Loading</span> : <span class="hljs-type">Result</span>&lt;<span class="hljs-type">Nothing</span>&gt;()
}

<span class="hljs-keyword">class</span> <span class="hljs-title class_">Person</span> <span class="hljs-keyword">private</span> <span class="hljs-keyword">constructor</span>(<span class="hljs-keyword">val</span> name: String) : Comparable&lt;Person&gt;, Serializable {
    <span class="hljs-keyword">override</span> <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">compareTo</span><span class="hljs-params">(other: <span class="hljs-type">Person</span>)</span></span>: <span class="hljs-built_in">Int</span> = name.compareTo(other.name)

    <span class="hljs-keyword">companion</span> <span class="hljs-keyword">object</span> {
        <span class="hljs-keyword">const</span> <span class="hljs-keyword">val</span> MAX = <span class="hljs-number">10</span>
        <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">create</span><span class="hljs-params">(name: <span class="hljs-type">String</span>)</span></span>: Person = Person(name)
    }
}

<span class="hljs-keyword">abstract</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Shape</span> {
    <span class="hljs-keyword">abstract</span> <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">area</span><span class="hljs-params">()</span></span>: <span class="hljs-built_in">Double</span>
}

<span class="hljs-keyword">open</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Base</span>(p: <span class="hljs-built_in">Int</span>)
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Derived</span>(p: <span class="hljs-built_in">Int</span>) : Base(p)
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Clickable</span> {
    <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">click</span><span class="hljs-params">()</span></span>
    <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">showOff</span><span class="hljs-params">()</span></span> = println(<span class="hljs-string">&quot;I&#x27;m clickable!&quot;</span>)
}
<span class="hljs-keyword">enum</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Color</span>(<span class="hljs-keyword">val</span> rgb: <span class="hljs-built_in">Int</span>) {
    RED(<span class="hljs-number">0xFF0000</span>), GREEN(<span class="hljs-number">0x00FF00</span>), BLUE(<span class="hljs-number">0x0000FF</span>)
}
""");
    }

    [Fact]
    public void Collections()
    {
        AssertHighlighter("kotlin",
"""
val list = listOf(1, 2, 3)
val mutable = mutableListOf<String>()
val map = mapOf("a" to 1, "b" to 2)
val set = setOf<Int>()
val arr = arrayOf(1, 2, 3)
val ints = intArrayOf(1, 2)
val range = 1..10
val until = 0 until 10
""",
"""
<span class="hljs-keyword">val</span> list = listOf(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>)
<span class="hljs-keyword">val</span> mutable = mutableListOf&lt;String&gt;()
<span class="hljs-keyword">val</span> map = mapOf(<span class="hljs-string">&quot;a&quot;</span> to <span class="hljs-number">1</span>, <span class="hljs-string">&quot;b&quot;</span> to <span class="hljs-number">2</span>)
<span class="hljs-keyword">val</span> <span class="hljs-keyword">set</span> = setOf&lt;<span class="hljs-built_in">Int</span>&gt;()
<span class="hljs-keyword">val</span> arr = arrayOf(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>)
<span class="hljs-keyword">val</span> ints = intArrayOf(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>)
<span class="hljs-keyword">val</span> range = <span class="hljs-number">1</span>..<span class="hljs-number">10</span>
<span class="hljs-keyword">val</span> until = <span class="hljs-number">0</span> until <span class="hljs-number">10</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("kotlin",
"""
// line comment TODO: something
/* block comment */
/* outer /* nested */ still comment */
/**
 * KDoc for [Foo].
 * @param name the name
 * @return the result
 * @see Bar
 */
val x = 1 // trailing
""",
"""
<span class="hljs-comment">// line comment <span class="hljs-doctag">TODO:</span> something</span>
<span class="hljs-comment">/* block comment */</span>
<span class="hljs-comment">/* outer <span class="hljs-comment">/* nested */</span> still comment */</span>
<span class="hljs-comment">/**
 * KDoc for [Foo].
 * <span class="hljs-doctag">@param</span> name the name
 * <span class="hljs-doctag">@return</span> the result
 * <span class="hljs-doctag">@see</span> Bar
 */</span>
<span class="hljs-keyword">val</span> x = <span class="hljs-number">1</span> <span class="hljs-comment">// trailing</span>
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("kotlin",
"""
when (x) {
    1 -> print("x == 1")
    2, 3 -> print("x == 2 or 3")
    in 4..10 -> print("in range")
    !in 11..20 -> print("not in range")
    is String -> print(x.length)
    else -> {
        print("otherwise")
    }
}

for (i in 1..10 step 2) println(i)
for (i in 10 downTo 1) {}
for ((index, value) in list.withIndex()) {}
while (x > 0) { x-- }
do { y++ } while (y < 10)

val result = try { parse(s) } catch (e: NumberFormatException) { null } finally { cleanup() }
throw IllegalStateException("bad")
""",
"""
<span class="hljs-keyword">when</span> (x) {
    <span class="hljs-number">1</span> -&gt; print(<span class="hljs-string">&quot;x == 1&quot;</span>)
    <span class="hljs-number">2</span>, <span class="hljs-number">3</span> -&gt; print(<span class="hljs-string">&quot;x == 2 or 3&quot;</span>)
    <span class="hljs-keyword">in</span> <span class="hljs-number">4</span>..<span class="hljs-number">10</span> -&gt; print(<span class="hljs-string">&quot;in range&quot;</span>)
    !<span class="hljs-keyword">in</span> <span class="hljs-number">11</span>..<span class="hljs-number">20</span> -&gt; print(<span class="hljs-string">&quot;not in range&quot;</span>)
    <span class="hljs-keyword">is</span> String -&gt; print(x.length)
    <span class="hljs-keyword">else</span> -&gt; {
        print(<span class="hljs-string">&quot;otherwise&quot;</span>)
    }
}

<span class="hljs-keyword">for</span> (i <span class="hljs-keyword">in</span> <span class="hljs-number">1</span>..<span class="hljs-number">10</span> step <span class="hljs-number">2</span>) println(i)
<span class="hljs-keyword">for</span> (i <span class="hljs-keyword">in</span> <span class="hljs-number">10</span> downTo <span class="hljs-number">1</span>) {}
<span class="hljs-keyword">for</span> ((index, value) <span class="hljs-keyword">in</span> list.withIndex()) {}
<span class="hljs-keyword">while</span> (x &gt; <span class="hljs-number">0</span>) { x-- }
<span class="hljs-keyword">do</span> { y++ } <span class="hljs-keyword">while</span> (y &lt; <span class="hljs-number">10</span>)

<span class="hljs-keyword">val</span> result = <span class="hljs-keyword">try</span> { parse(s) } <span class="hljs-keyword">catch</span> (e: NumberFormatException) { <span class="hljs-literal">null</span> } <span class="hljs-keyword">finally</span> { cleanup() }
<span class="hljs-keyword">throw</span> IllegalStateException(<span class="hljs-string">&quot;bad&quot;</span>)
""");
    }

    [Fact]
    public void Coroutines()
    {
        AssertHighlighter("kotlin",
"""
suspend fun loadData(): List<Item> = withContext(Dispatchers.IO) {
    api.fetchItems()
}

fun main() = runBlocking {
    val job = launch {
        delay(1000L)
        println("World!")
    }
    val deferred = async { compute() }
    println("Hello, ${deferred.await()}")
    job.join()
}
""",
"""
<span class="hljs-keyword">suspend</span> <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">loadData</span><span class="hljs-params">()</span></span>: List&lt;Item&gt; = withContext(Dispatchers.IO) {
    api.fetchItems()
}

<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">main</span><span class="hljs-params">()</span></span> = runBlocking {
    <span class="hljs-keyword">val</span> job = launch {
        delay(<span class="hljs-number">1000L</span>)
        println(<span class="hljs-string">&quot;World!&quot;</span>)
    }
    <span class="hljs-keyword">val</span> deferred = async { compute() }
    println(<span class="hljs-string">&quot;Hello, <span class="hljs-subst">${deferred.await()}</span>&quot;</span>)
    job.join()
}
""");
    }

    [Fact]
    public void Destructuring()
    {
        AssertHighlighter("kotlin",
"""
val (name, age) = person
val (a, b) = Pair(1, "x")
for ((k, v) in map) println("$k -> $v")
""",
"""
<span class="hljs-keyword">val</span> (name, age) = person
<span class="hljs-keyword">val</span> (a, b) = Pair(<span class="hljs-number">1</span>, <span class="hljs-string">&quot;x&quot;</span>)
<span class="hljs-keyword">for</span> ((k, v) <span class="hljs-keyword">in</span> map) println(<span class="hljs-string">&quot;<span class="hljs-variable">$k</span> -&gt; <span class="hljs-variable">$v</span>&quot;</span>)
""");
    }

    [Fact]
    public void DollarEdge()
    {
        AssertHighlighter("kotlin",
""""
val a = "$"
val b = "$1"
val c = "${'$'}{x}"
val d = "cost: $ 5"
val e = "$name.length"
val f = """$a${b}"""
"""",
"""
<span class="hljs-keyword">val</span> a = <span class="hljs-string">&quot;$&quot;</span>
<span class="hljs-keyword">val</span> b = <span class="hljs-string">&quot;$1&quot;</span>
<span class="hljs-keyword">val</span> c = <span class="hljs-string">&quot;<span class="hljs-subst">${<span class="hljs-string">&#x27;$&#x27;</span>}</span>{x}&quot;</span>
<span class="hljs-keyword">val</span> d = <span class="hljs-string">&quot;cost: $ 5&quot;</span>
<span class="hljs-keyword">val</span> e = <span class="hljs-string">&quot;<span class="hljs-variable">$name</span>.length&quot;</span>
<span class="hljs-keyword">val</span> f = <span class="hljs-string">&quot;&quot;&quot;<span class="hljs-variable">$a</span><span class="hljs-subst">${b}</span>&quot;&quot;&quot;</span>
""");
    }

    [Fact]
    public void ExpectActual()
    {
        AssertHighlighter("kotlin",
"""
expect fun platformName(): String
actual fun platformName(): String = "JVM"
external fun nativeCall(): Int
tailrec fun factorial(n: Long, acc: Long = 1): Long = if (n <= 1) acc else factorial(n - 1, acc * n)
""",
"""
<span class="hljs-keyword">expect</span> <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">platformName</span><span class="hljs-params">()</span></span>: String
<span class="hljs-keyword">actual</span> <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">platformName</span><span class="hljs-params">()</span></span>: String = <span class="hljs-string">&quot;JVM&quot;</span>
<span class="hljs-keyword">external</span> <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">nativeCall</span><span class="hljs-params">()</span></span>: <span class="hljs-built_in">Int</span>
<span class="hljs-keyword">tailrec</span> <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">factorial</span><span class="hljs-params">(n: <span class="hljs-type">Long</span>, acc: <span class="hljs-type">Long</span> = <span class="hljs-number">1</span>)</span></span>: <span class="hljs-built_in">Long</span> = <span class="hljs-keyword">if</span> (n &lt;= <span class="hljs-number">1</span>) acc <span class="hljs-keyword">else</span> factorial(n - <span class="hljs-number">1</span>, acc * n)
""");
    }

    [Fact]
    public void FunInterface()
    {
        AssertHighlighter("kotlin",
"""
fun interface IntPredicate {
    fun accept(i: Int): Boolean
}
val isEven = IntPredicate { it % 2 == 0 }
fun interfaceName() = 1
""",
"""
<span class="hljs-keyword">fun</span> <span class="hljs-keyword">interface</span> <span class="hljs-title class_">IntPredicate</span> {
    <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">accept</span><span class="hljs-params">(i: <span class="hljs-type">Int</span>)</span></span>: <span class="hljs-built_in">Boolean</span>
}
<span class="hljs-keyword">val</span> isEven = IntPredicate { it % <span class="hljs-number">2</span> == <span class="hljs-number">0</span> }
<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">interfaceName</span><span class="hljs-params">()</span></span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void FunTypes()
    {
        AssertHighlighter("kotlin",
"""
fun apply(f: (Int) -> Int, x: Int): Int = f(x)
fun compose(f: ((Int) -> Int), g: (Int) -> (Int)): (Int) -> Int = { f(g(it)) }
fun <T, R> T.let2(block: (T) -> R): R = block(this)
fun String.times(n: Int) = repeat(n)
""",
"""
<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">apply</span><span class="hljs-params">(f: (<span class="hljs-type">Int</span>) -&gt; <span class="hljs-type">Int</span>, x: <span class="hljs-type">Int</span>)</span></span>: <span class="hljs-built_in">Int</span> = f(x)
<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">compose</span><span class="hljs-params">(f: ((<span class="hljs-type">Int</span>) -&gt; <span class="hljs-type">Int</span>), g: (<span class="hljs-type">Int</span>) -&gt; (<span class="hljs-type">Int</span>))</span></span>: (<span class="hljs-built_in">Int</span>) -&gt; <span class="hljs-built_in">Int</span> = { f(g(it)) }
<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-type">&lt;T, R&gt;</span> T.<span class="hljs-title">let2</span><span class="hljs-params">(block: (<span class="hljs-type">T</span>) -&gt; <span class="hljs-type">R</span>)</span></span>: R = block(<span class="hljs-keyword">this</span>)
<span class="hljs-function"><span class="hljs-keyword">fun</span> String.<span class="hljs-title">times</span><span class="hljs-params">(n: <span class="hljs-type">Int</span>)</span></span> = repeat(n)
""");
    }

    [Fact]
    public void Functions()
    {
        AssertHighlighter("kotlin",
"""
fun sum(a: Int, b: Int): Int {
    return a + b
}

fun max(a: Int, b: Int) = if (a > b) a else b

private suspend fun fetch(url: String, timeout: Long = 1000L): String? = null

inline fun <reified T : Any> Gson.fromJson(json: String): T = fromJson(json, T::class.java)

fun <T> List<T>.second(): T = this[1]

fun String.isPalindrome(): Boolean = this == this.reversed()

infix fun Int.times(str: String) = str.repeat(this)

operator fun Point.plus(other: Point) = Point(x + other.x, y + other.y)

fun process(callback: (Int, String) -> Unit, vararg items: String) {}

fun nullable(x: String?, y: List<Int?>?): Int? = x?.length

fun withDefault(name: String = "x", count: Int = 3, flag: Boolean = true) {}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">sum</span><span class="hljs-params">(a: <span class="hljs-type">Int</span>, b: <span class="hljs-type">Int</span>)</span></span>: <span class="hljs-built_in">Int</span> {
    <span class="hljs-keyword">return</span> a + b
}

<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">max</span><span class="hljs-params">(a: <span class="hljs-type">Int</span>, b: <span class="hljs-type">Int</span>)</span></span> = <span class="hljs-keyword">if</span> (a &gt; b) a <span class="hljs-keyword">else</span> b

<span class="hljs-keyword">private</span> <span class="hljs-keyword">suspend</span> <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">fetch</span><span class="hljs-params">(url: <span class="hljs-type">String</span>, timeout: <span class="hljs-type">Long</span> = <span class="hljs-number">1000</span>L)</span></span>: String? = <span class="hljs-literal">null</span>

<span class="hljs-keyword">inline</span> <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-type">&lt;<span class="hljs-keyword">reified</span> T : Any&gt;</span> Gson.<span class="hljs-title">fromJson</span><span class="hljs-params">(json: <span class="hljs-type">String</span>)</span></span>: T = fromJson(json, T::<span class="hljs-keyword">class</span>.java)

<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-type">&lt;T&gt;</span> List<span class="hljs-type">&lt;T&gt;</span>.<span class="hljs-title">second</span><span class="hljs-params">()</span></span>: T = <span class="hljs-keyword">this</span>[<span class="hljs-number">1</span>]

<span class="hljs-function"><span class="hljs-keyword">fun</span> String.<span class="hljs-title">isPalindrome</span><span class="hljs-params">()</span></span>: <span class="hljs-built_in">Boolean</span> = <span class="hljs-keyword">this</span> == <span class="hljs-keyword">this</span>.reversed()

<span class="hljs-keyword">infix</span> <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-built_in">Int</span>.<span class="hljs-title">times</span><span class="hljs-params">(str: <span class="hljs-type">String</span>)</span></span> = str.repeat(<span class="hljs-keyword">this</span>)

<span class="hljs-keyword">operator</span> <span class="hljs-function"><span class="hljs-keyword">fun</span> Point.<span class="hljs-title">plus</span><span class="hljs-params">(other: <span class="hljs-type">Point</span>)</span></span> = Point(x + other.x, y + other.y)

<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">process</span><span class="hljs-params">(callback: (<span class="hljs-type">Int</span>, <span class="hljs-type">String</span>) -&gt; <span class="hljs-type">Unit</span>, <span class="hljs-keyword">vararg</span> items: <span class="hljs-type">String</span>)</span></span> {}

<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">nullable</span><span class="hljs-params">(x: <span class="hljs-type">String</span>?, y: <span class="hljs-type">List</span>&lt;<span class="hljs-type">Int</span>?&gt;?)</span></span>: <span class="hljs-built_in">Int</span>? = x?.length

<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">withDefault</span><span class="hljs-params">(name: <span class="hljs-type">String</span> = <span class="hljs-string">&quot;x&quot;</span>, count: <span class="hljs-type">Int</span> = <span class="hljs-number">3</span>, flag: <span class="hljs-type">Boolean</span> = <span class="hljs-literal">true</span>)</span></span> {}
""");
    }

    [Fact]
    public void Generics()
    {
        AssertHighlighter("kotlin",
"""
class Box<T>(val value: T)
interface Source<out T> { fun next(): T }
interface Comparable<in T> { operator fun compareTo(other: T): Int }
fun <T : Comparable<T>> sort(list: List<T>) {}
fun <T> copyWhenGreater(list: List<T>, threshold: T): List<String>
    where T : CharSequence, T : Comparable<T> {
    return list.filter { it > threshold }.map { it.toString() }
}
val map: Map<String, List<Int>> = mapOf()
val star: List<*> = listOf(1)
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Box</span>&lt;<span class="hljs-type">T</span>&gt;(<span class="hljs-keyword">val</span> value: T)
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Source</span>&lt;<span class="hljs-type">out T</span>&gt; { <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">next</span><span class="hljs-params">()</span></span>: T }
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Comparable</span>&lt;<span class="hljs-type">in T</span>&gt; { <span class="hljs-keyword">operator</span> <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">compareTo</span><span class="hljs-params">(other: <span class="hljs-type">T</span>)</span></span>: <span class="hljs-built_in">Int</span> }
<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-type">&lt;T : Comparable&lt;T&gt;&gt;</span> <span class="hljs-title">sort</span><span class="hljs-params">(list: <span class="hljs-type">List</span>&lt;<span class="hljs-type">T</span>&gt;)</span></span> {}
<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-type">&lt;T&gt;</span> <span class="hljs-title">copyWhenGreater</span><span class="hljs-params">(list: <span class="hljs-type">List</span>&lt;<span class="hljs-type">T</span>&gt;, threshold: <span class="hljs-type">T</span>)</span></span>: List&lt;String&gt;
    <span class="hljs-keyword">where</span> T : CharSequence, T : Comparable&lt;T&gt; {
    <span class="hljs-keyword">return</span> list.filter { it &gt; threshold }.map { it.toString() }
}
<span class="hljs-keyword">val</span> map: Map&lt;String, List&lt;<span class="hljs-built_in">Int</span>&gt;&gt; = mapOf()
<span class="hljs-keyword">val</span> star: List&lt;*&gt; = listOf(<span class="hljs-number">1</span>)
""");
    }

    [Fact]
    public void GradleKts()
    {
        AssertHighlighter("kts",
"""
plugins {
    kotlin("jvm") version "1.9.22"
    id("org.jetbrains.kotlin.plugin.serialization") version "1.9.22"
    application
}

group = "com.example"
version = "1.0-SNAPSHOT"

repositories {
    mavenCentral()
}

dependencies {
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-core:1.7.3")
    testImplementation(kotlin("test"))
}

tasks.test {
    useJUnitPlatform()
}

kotlin {
    jvmToolchain(17)
}
""",
"""
plugins {
    kotlin(<span class="hljs-string">&quot;jvm&quot;</span>) version <span class="hljs-string">&quot;1.9.22&quot;</span>
    id(<span class="hljs-string">&quot;org.jetbrains.kotlin.plugin.serialization&quot;</span>) version <span class="hljs-string">&quot;1.9.22&quot;</span>
    application
}

group = <span class="hljs-string">&quot;com.example&quot;</span>
version = <span class="hljs-string">&quot;1.0-SNAPSHOT&quot;</span>

repositories {
    mavenCentral()
}

dependencies {
    implementation(<span class="hljs-string">&quot;org.jetbrains.kotlinx:kotlinx-coroutines-core:1.7.3&quot;</span>)
    testImplementation(kotlin(<span class="hljs-string">&quot;test&quot;</span>))
}

tasks.test {
    useJUnitPlatform()
}

kotlin {
    jvmToolchain(<span class="hljs-number">17</span>)
}
""");
    }

    [Fact]
    public void Hello()
    {
        AssertHighlighter("kt",
"""
package com.example

import kotlin.math.max
import kotlinx.coroutines.*

fun main(args: Array<String>) {
    println("Hello, World!")
}
""",
"""
<span class="hljs-keyword">package</span> com.example

<span class="hljs-keyword">import</span> kotlin.math.max
<span class="hljs-keyword">import</span> kotlinx.coroutines.*

<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">main</span><span class="hljs-params">(args: <span class="hljs-type">Array</span>&lt;<span class="hljs-type">String</span>&gt;)</span></span> {
    println(<span class="hljs-string">&quot;Hello, World!&quot;</span>)
}
""");
    }

    [Fact]
    public void IfExpression()
    {
        AssertHighlighter("kotlin",
"""
val max = if (a > b) {
    print("Choose a")
    a
} else {
    print("Choose b")
    b
}
""",
"""
<span class="hljs-keyword">val</span> max = <span class="hljs-keyword">if</span> (a &gt; b) {
    print(<span class="hljs-string">&quot;Choose a&quot;</span>)
    a
} <span class="hljs-keyword">else</span> {
    print(<span class="hljs-string">&quot;Choose b&quot;</span>)
    b
}
""");
    }

    [Fact]
    public void InitBlocks()
    {
        AssertHighlighter("kotlin",
"""
class InitOrderDemo(name: String) {
    val firstProperty = "First property: $name".also(::println)

    init {
        println("First initializer block that prints $name")
    }

    constructor(name: String, age: Int) : this(name) {
        this.age = age
    }
}
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">InitOrderDemo</span>(name: String) {
    <span class="hljs-keyword">val</span> firstProperty = <span class="hljs-string">&quot;First property: <span class="hljs-variable">$name</span>&quot;</span>.also(::println)

    <span class="hljs-keyword">init</span> {
        println(<span class="hljs-string">&quot;First initializer block that prints <span class="hljs-variable">$name</span>&quot;</span>)
    }

    <span class="hljs-keyword">constructor</span>(name: String, age: <span class="hljs-built_in">Int</span>) : <span class="hljs-keyword">this</span>(name) {
        <span class="hljs-keyword">this</span>.age = age
    }
}
""");
    }

    [Fact]
    public void Labels()
    {
        AssertHighlighter("kotlin",
"""
loop@ for (i in 1..100) {
    for (j in 1..100) {
        if (j == 5) continue@loop
        if (i == 50) break@loop
    }
}

listOf(1, 2, 3).forEach lit@{
    if (it == 3) return@lit
    print(it)
}

fun foo() {
    run {
        return@run
    }
    return
}

class A {
    inner class B {
        fun f() = this@A.hashCode()
    }
}
""",
"""
<span class="hljs-symbol">loop@</span> <span class="hljs-keyword">for</span> (i <span class="hljs-keyword">in</span> <span class="hljs-number">1</span>..<span class="hljs-number">100</span>) {
    <span class="hljs-keyword">for</span> (j <span class="hljs-keyword">in</span> <span class="hljs-number">1</span>..<span class="hljs-number">100</span>) {
        <span class="hljs-keyword">if</span> (j == <span class="hljs-number">5</span>) <span class="hljs-keyword">continue</span><span class="hljs-symbol">@loop</span>
        <span class="hljs-keyword">if</span> (i == <span class="hljs-number">50</span>) <span class="hljs-keyword">break</span><span class="hljs-symbol">@loop</span>
    }
}

listOf(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>).forEach <span class="hljs-symbol">lit@</span>{
    <span class="hljs-keyword">if</span> (it == <span class="hljs-number">3</span>) <span class="hljs-keyword">return</span><span class="hljs-symbol">@lit</span>
    print(it)
}

<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">foo</span><span class="hljs-params">()</span></span> {
    run {
        <span class="hljs-keyword">return</span><span class="hljs-symbol">@run</span>
    }
    <span class="hljs-keyword">return</span>
}

<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> {
    <span class="hljs-keyword">inner</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">B</span> {
        <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">f</span><span class="hljs-params">()</span></span> = <span class="hljs-keyword">this</span><span class="hljs-symbol">@A</span>.hashCode()
    }
}
""");
    }

    [Fact]
    public void Lambdas()
    {
        AssertHighlighter("kotlin",
"""
val sum = { x: Int, y: Int -> x + y }
val square: (Int) -> Int = { it * it }
list.filter { it > 0 }.map { it * 2 }.forEach(::println)
val ref = String::length
button.setOnClickListener { view -> handle(view) }
val f: suspend () -> Unit = {}
""",
"""
<span class="hljs-keyword">val</span> sum = { x: <span class="hljs-built_in">Int</span>, y: <span class="hljs-built_in">Int</span> -&gt; x + y }
<span class="hljs-keyword">val</span> square: (<span class="hljs-built_in">Int</span>) -&gt; <span class="hljs-built_in">Int</span> = { it * it }
list.filter { it &gt; <span class="hljs-number">0</span> }.map { it * <span class="hljs-number">2</span> }.forEach(::println)
<span class="hljs-keyword">val</span> ref = String::length
button.setOnClickListener { view -&gt; handle(view) }
<span class="hljs-keyword">val</span> f: <span class="hljs-keyword">suspend</span> () -&gt; <span class="hljs-built_in">Unit</span> = {}
""");
    }

    [Fact]
    public void MultilineFunSig()
    {
        AssertHighlighter("kotlin",
"""
fun createUser(
    name: String,
    // the age
    age: Int = 18,
    /* email */ email: String? = null,
): User {
    return User(name, age, email)
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">createUser</span><span class="hljs-params">(
    name: <span class="hljs-type">String</span>,
    <span class="hljs-comment">// the age</span>
    age: <span class="hljs-type">Int</span> = <span class="hljs-number">18</span>,
    <span class="hljs-comment">/* email */</span> email: <span class="hljs-type">String</span>? = <span class="hljs-literal">null</span>,
)</span></span>: User {
    <span class="hljs-keyword">return</span> User(name, age, email)
}
""");
    }

    [Fact]
    public void NestedGenerics()
    {
        AssertHighlighter("kotlin",
"""
fun <T : Comparable<T>> sort(list: MutableList<T>) {}
fun <K, V : Map<K, List<V>>> group(x: V) {}
class Tree<T : Comparable<T>>(val value: T)
class Cache<K, V : Map<K, List<V>>> : Base<K>()
inline fun <reified T : Enum<T>> enumOf(name: String): T = enumValueOf(name)
""",
"""
<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-type">&lt;T : Comparable&lt;T&gt;&gt;</span> <span class="hljs-title">sort</span><span class="hljs-params">(list: <span class="hljs-type">MutableList</span>&lt;<span class="hljs-type">T</span>&gt;)</span></span> {}
<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-type">&lt;K, V : Map&lt;K, List&lt;V&gt;&gt;&gt;</span> <span class="hljs-title">group</span><span class="hljs-params">(x: <span class="hljs-type">V</span>)</span></span> {}
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Tree</span>&lt;<span class="hljs-type">T : Comparable&lt;T&gt;</span>&gt;(<span class="hljs-keyword">val</span> value: T)
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Cache</span>&lt;<span class="hljs-type">K, V : Map&lt;K, List&lt;V&gt;&gt;</span>&gt; : <span class="hljs-type">Base</span>&lt;<span class="hljs-type">K</span>&gt;()
<span class="hljs-keyword">inline</span> <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-type">&lt;<span class="hljs-keyword">reified</span> T : Enum&lt;T&gt;&gt;</span> <span class="hljs-title">enumOf</span><span class="hljs-params">(name: <span class="hljs-type">String</span>)</span></span>: T = enumValueOf(name)
""");
    }

    [Fact]
    public void Nullable()
    {
        AssertHighlighter("kotlin",
"""
var name: String? = null
val length = name?.length ?: 0
val upper = name!!.uppercase()
val safe = name?.let { it.uppercase() } ?: "default"
if (x is String && x.isNotEmpty()) println(x)
val y = x as? Int
val z = x as String
""",
"""
<span class="hljs-keyword">var</span> name: String? = <span class="hljs-literal">null</span>
<span class="hljs-keyword">val</span> length = name?.length ?: <span class="hljs-number">0</span>
<span class="hljs-keyword">val</span> upper = name!!.uppercase()
<span class="hljs-keyword">val</span> safe = name?.let { it.uppercase() } ?: <span class="hljs-string">&quot;default&quot;</span>
<span class="hljs-keyword">if</span> (x <span class="hljs-keyword">is</span> String &amp;&amp; x.isNotEmpty()) println(x)
<span class="hljs-keyword">val</span> y = x <span class="hljs-keyword">as</span>? <span class="hljs-built_in">Int</span>
<span class="hljs-keyword">val</span> z = x <span class="hljs-keyword">as</span> String
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("kotlin",
"""
val a = 42
val b = 1_000_000
val c = 123L
val d = 0xFF
val e = 0b1010
val f = 3.14
val g = 3.14f
val h = 1e10
val i = 2.5e-3
val j = 0xFF_EC_DE_5E
val k = 1u
val l = 0.5F
val m = -7
val n = .5
""",
"""
<span class="hljs-keyword">val</span> a = <span class="hljs-number">42</span>
<span class="hljs-keyword">val</span> b = <span class="hljs-number">1_000_000</span>
<span class="hljs-keyword">val</span> c = <span class="hljs-number">123L</span>
<span class="hljs-keyword">val</span> d = <span class="hljs-number">0xFF</span>
<span class="hljs-keyword">val</span> e = <span class="hljs-number">0b1010</span>
<span class="hljs-keyword">val</span> f = <span class="hljs-number">3.14</span>
<span class="hljs-keyword">val</span> g = <span class="hljs-number">3.14f</span>
<span class="hljs-keyword">val</span> h = <span class="hljs-number">1e10</span>
<span class="hljs-keyword">val</span> i = <span class="hljs-number">2.5e-3</span>
<span class="hljs-keyword">val</span> j = <span class="hljs-number">0xFF_EC_DE_5E</span>
<span class="hljs-keyword">val</span> k = 1u
<span class="hljs-keyword">val</span> l = <span class="hljs-number">0.5F</span>
<span class="hljs-keyword">val</span> m = -<span class="hljs-number">7</span>
<span class="hljs-keyword">val</span> n = <span class="hljs-number">.5</span>
""");
    }

    [Fact]
    public void ObjectDeclarations()
    {
        AssertHighlighter("kotlin",
"""
object Singleton {
    fun hello() = "hi"
}
companion object Factory {
    fun create(): Foo = Foo()
}
companion object {
}
data object Loading : State
object Config : Properties(), AutoCloseable {
}
val anon = object : Runnable {
    override fun run() {}
}
""",
"""
<span class="hljs-keyword">object</span> <span class="hljs-title class_">Singleton</span> {
    <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">hello</span><span class="hljs-params">()</span></span> = <span class="hljs-string">&quot;hi&quot;</span>
}
<span class="hljs-keyword">companion</span> <span class="hljs-keyword">object</span> <span class="hljs-title class_">Factory</span> {
    <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">create</span><span class="hljs-params">()</span></span>: Foo = Foo()
}
<span class="hljs-keyword">companion</span> <span class="hljs-keyword">object</span> {
}
<span class="hljs-keyword">data</span> <span class="hljs-keyword">object</span> <span class="hljs-title class_">Loading</span> : <span class="hljs-type">State</span>
<span class="hljs-keyword">object</span> <span class="hljs-title class_">Config</span> : <span class="hljs-type">Properties</span>(), AutoCloseable {
}
<span class="hljs-keyword">val</span> anon = <span class="hljs-keyword">object</span> : Runnable {
    <span class="hljs-keyword">override</span> <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">run</span><span class="hljs-params">()</span></span> {}
}
""");
    }

    [Fact]
    public void Objects()
    {
        AssertHighlighter("kotlin",
"""
object Singleton {
    val name = "single"
}

val listener = object : MouseAdapter() {
    override fun mouseClicked(e: MouseEvent) {}
}

typealias Handler = (String) -> Unit
""",
"""
<span class="hljs-keyword">object</span> <span class="hljs-title class_">Singleton</span> {
    <span class="hljs-keyword">val</span> name = <span class="hljs-string">&quot;single&quot;</span>
}

<span class="hljs-keyword">val</span> listener = <span class="hljs-keyword">object</span> : MouseAdapter() {
    <span class="hljs-keyword">override</span> <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">mouseClicked</span><span class="hljs-params">(e: <span class="hljs-type">MouseEvent</span>)</span></span> {}
}

<span class="hljs-keyword">typealias</span> Handler = (String) -&gt; <span class="hljs-built_in">Unit</span>
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("kotlin",
"""
val a = b + c - d * e / f % g
a += 1; a -= 1; a *= 2; a /= 2
val t = a == b && c != d || !e
val same = a === b
val notSame = a !== b
val cmp = a >= b
val elvis = x ?: return
val bits = x shl 2 or 1 and 3 xor 4
""",
"""
<span class="hljs-keyword">val</span> a = b + c - d * e / f % g
a += <span class="hljs-number">1</span>; a -= <span class="hljs-number">1</span>; a *= <span class="hljs-number">2</span>; a /= <span class="hljs-number">2</span>
<span class="hljs-keyword">val</span> t = a == b &amp;&amp; c != d || !e
<span class="hljs-keyword">val</span> same = a === b
<span class="hljs-keyword">val</span> notSame = a !== b
<span class="hljs-keyword">val</span> cmp = a &gt;= b
<span class="hljs-keyword">val</span> elvis = x ?: <span class="hljs-keyword">return</span>
<span class="hljs-keyword">val</span> bits = x shl <span class="hljs-number">2</span> or <span class="hljs-number">1</span> and <span class="hljs-number">3</span> xor <span class="hljs-number">4</span>
""");
    }

    [Fact]
    public void ParamAnnotationsStrings()
    {
        AssertHighlighter("kotlin",
"""
fun route(@Path("id") id: Long, @Query("q") query: String = "all", limit: Int = 10) {}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">route</span><span class="hljs-params">(<span class="hljs-meta">@Path(<span class="hljs-string">&quot;id&quot;</span>)</span> id: <span class="hljs-type">Long</span>, <span class="hljs-meta">@Query(<span class="hljs-string">&quot;q&quot;</span>)</span> query: <span class="hljs-type">String</span> = <span class="hljs-string">&quot;all&quot;</span>, limit: <span class="hljs-type">Int</span> = <span class="hljs-number">10</span>)</span></span> {}
""");
    }

    [Fact]
    public void Properties()
    {
        AssertHighlighter("kotlin",
"""
class Rectangle(val width: Int, val height: Int) {
    val area: Int
        get() = width * height

    var counter = 0
        private set

    var name: String = ""
        get() = field.uppercase()
        set(value) {
            field = value.trim()
        }

    val lazyValue: String by lazy { "computed" }
    var observed: Int by Delegates.observable(0) { _, old, new -> }
}
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Rectangle</span>(<span class="hljs-keyword">val</span> width: <span class="hljs-built_in">Int</span>, <span class="hljs-keyword">val</span> height: <span class="hljs-built_in">Int</span>) {
    <span class="hljs-keyword">val</span> area: <span class="hljs-built_in">Int</span>
        <span class="hljs-keyword">get</span>() = width * height

    <span class="hljs-keyword">var</span> counter = <span class="hljs-number">0</span>
        <span class="hljs-keyword">private</span> <span class="hljs-keyword">set</span>

    <span class="hljs-keyword">var</span> name: String = <span class="hljs-string">&quot;&quot;</span>
        <span class="hljs-keyword">get</span>() = field.uppercase()
        <span class="hljs-keyword">set</span>(value) {
            field = value.trim()
        }

    <span class="hljs-keyword">val</span> lazyValue: String <span class="hljs-keyword">by</span> lazy { <span class="hljs-string">&quot;computed&quot;</span> }
    <span class="hljs-keyword">var</span> observed: <span class="hljs-built_in">Int</span> <span class="hljs-keyword">by</span> Delegates.observable(<span class="hljs-number">0</span>) { _, old, new -&gt; }
}
""");
    }

    [Fact]
    public void Ranges()
    {
        AssertHighlighter("kotlin",
"""
for (i in 1..10) {}
val r = 0..<size
val d = 1.0..2.5
val c = 'a'..'z'
val x = a..b
val big = 1_000..2_000L
val f = 1.5f
val e = 1e3..2e3
""",
"""
<span class="hljs-keyword">for</span> (i <span class="hljs-keyword">in</span> <span class="hljs-number">1</span>..<span class="hljs-number">10</span>) {}
<span class="hljs-keyword">val</span> r = <span class="hljs-number">0</span>..&lt;size
<span class="hljs-keyword">val</span> d = <span class="hljs-number">1.0</span>..<span class="hljs-number">2.5</span>
<span class="hljs-keyword">val</span> c = <span class="hljs-string">&#x27;a&#x27;</span>..<span class="hljs-string">&#x27;z&#x27;</span>
<span class="hljs-keyword">val</span> x = a..b
<span class="hljs-keyword">val</span> big = <span class="hljs-number">1_000</span>..<span class="hljs-number">2_000L</span>
<span class="hljs-keyword">val</span> f = <span class="hljs-number">1.5f</span>
<span class="hljs-keyword">val</span> e = <span class="hljs-number">1e3</span>..<span class="hljs-number">2e3</span>
""");
    }

    [Fact]
    public void RawStrings()
    {
        AssertHighlighter("kotlin",
""""
val text = """
    |Hello, $name
    |Sum: ${a + b}
    |Path: C:\Users\no\escape
    """.trimMargin()
val json = """{"key": "value"}"""
val regex = """\d+""".toRegex()
"""",
"""
<span class="hljs-keyword">val</span> text = <span class="hljs-string">&quot;&quot;&quot;
    |Hello, <span class="hljs-variable">$name</span>
    |Sum: <span class="hljs-subst">${a + b}</span>
    |Path: C:\Users\no\escape
    &quot;&quot;&quot;</span>.trimMargin()
<span class="hljs-keyword">val</span> json = <span class="hljs-string">&quot;&quot;&quot;{&quot;key&quot;: &quot;value&quot;}&quot;&quot;&quot;</span>
<span class="hljs-keyword">val</span> regex = <span class="hljs-string">&quot;&quot;&quot;\d+&quot;&quot;&quot;</span>.toRegex()
""");
    }

    [Fact]
    public void Shebang()
    {
        AssertHighlighter("kotlin",
"""
#!/usr/bin/env kotlin
println("script")
""",
"""
<span class="hljs-meta">#!/usr/bin/env kotlin</span>
println(<span class="hljs-string">&quot;script&quot;</span>)
""");
    }

    [Fact]
    public void StringTemplates()
    {
        AssertHighlighter("kotlin",
"""
val name = "Kotlin"
val greeting = "Hello, $name!"
val length = "Length: ${name.length}"
val math = "Sum: ${1 + 2}"
val nested = "Outer ${"inner $name"} done"
val escaped = "Tab\tNewline\n\"quote\" \$notATemplate"
val price = "${'$'}9.99"
val c = 'a'
val esc = '\n'
val obj = "${user.name} is ${user.age} years old"
""",
"""
<span class="hljs-keyword">val</span> name = <span class="hljs-string">&quot;Kotlin&quot;</span>
<span class="hljs-keyword">val</span> greeting = <span class="hljs-string">&quot;Hello, <span class="hljs-variable">$name</span>!&quot;</span>
<span class="hljs-keyword">val</span> length = <span class="hljs-string">&quot;Length: <span class="hljs-subst">${name.length}</span>&quot;</span>
<span class="hljs-keyword">val</span> math = <span class="hljs-string">&quot;Sum: <span class="hljs-subst">${<span class="hljs-number">1</span> + <span class="hljs-number">2</span>}</span>&quot;</span>
<span class="hljs-keyword">val</span> nested = <span class="hljs-string">&quot;Outer <span class="hljs-subst">${<span class="hljs-string">&quot;inner <span class="hljs-variable">$name</span>&quot;</span>}</span> done&quot;</span>
<span class="hljs-keyword">val</span> escaped = <span class="hljs-string">&quot;Tab\tNewline\n\&quot;quote\&quot; \$notATemplate&quot;</span>
<span class="hljs-keyword">val</span> price = <span class="hljs-string">&quot;<span class="hljs-subst">${<span class="hljs-string">&#x27;$&#x27;</span>}</span>9.99&quot;</span>
<span class="hljs-keyword">val</span> c = <span class="hljs-string">&#x27;a&#x27;</span>
<span class="hljs-keyword">val</span> esc = <span class="hljs-string">&#x27;\n&#x27;</span>
<span class="hljs-keyword">val</span> obj = <span class="hljs-string">&quot;<span class="hljs-subst">${user.name}</span> is <span class="hljs-subst">${user.age}</span> years old&quot;</span>
""");
    }

    [Fact]
    public void TestClass()
    {
        AssertHighlighter("kotlin",
"""
class CalculatorTest {
    private lateinit var calc: Calculator

    @BeforeEach
    fun setUp() {
        calc = Calculator()
    }

    @Test
    fun `adds two numbers`() {
        assertEquals(4, calc.add(2, 2))
    }
}
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">CalculatorTest</span> {
    <span class="hljs-keyword">private</span> <span class="hljs-keyword">lateinit</span> <span class="hljs-keyword">var</span> calc: Calculator

    <span class="hljs-meta">@BeforeEach</span>
    <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">setUp</span><span class="hljs-params">()</span></span> {
        calc = Calculator()
    }

    <span class="hljs-meta">@Test</span>
    <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">`adds two numbers`</span><span class="hljs-params">()</span></span> {
        assertEquals(<span class="hljs-number">4</span>, calc.add(<span class="hljs-number">2</span>, <span class="hljs-number">2</span>))
    }
}
""");
    }

    [Fact]
    public void ThisSuper()
    {
        AssertHighlighter("kotlin",
"""
class Child : Parent() {
    override fun draw() {
        super.draw()
        this.x = 1
        super<Parent>.foo()
    }
}
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Child</span> : <span class="hljs-type">Parent</span>() {
    <span class="hljs-keyword">override</span> <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">draw</span><span class="hljs-params">()</span></span> {
        <span class="hljs-keyword">super</span>.draw()
        <span class="hljs-keyword">this</span>.x = <span class="hljs-number">1</span>
        <span class="hljs-keyword">super</span>&lt;Parent&gt;.foo()
    }
}
""");
    }

    [Fact]
    public void ValueClass()
    {
        AssertHighlighter("kotlin",
"""
@JvmInline
value class Password(private val s: String)

fun interface KRunnable {
    fun invoke()
}

sealed interface Error
data object Empty : Error
trait Legacy {}
""",
"""
<span class="hljs-meta">@JvmInline</span>
value <span class="hljs-keyword">class</span> <span class="hljs-title class_">Password</span>(<span class="hljs-keyword">private</span> <span class="hljs-keyword">val</span> s: String)

<span class="hljs-keyword">fun</span> <span class="hljs-keyword">interface</span> <span class="hljs-title class_">KRunnable</span> {
    <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">invoke</span><span class="hljs-params">()</span></span>
}

<span class="hljs-keyword">sealed</span> <span class="hljs-keyword">interface</span> <span class="hljs-title class_">Error</span>
<span class="hljs-keyword">data</span> <span class="hljs-keyword">object</span> <span class="hljs-title class_">Empty</span> : <span class="hljs-type">Error</span>
trait <span class="hljs-title class_">Legacy</span> {}
""");
    }
}
