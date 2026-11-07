namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class JavaHighlighterTests
{
    [Fact]
    public void Annotations()
    {
        AssertHighlighter("java",
"""
@Entity
@Table(name = "users", schema = "public")
@SuppressWarnings({"unchecked", "rawtypes"})
public class User {
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(nullable = false, length = 100)
    private String name;

    @JsonProperty("user_name")
    public String getName(@NotNull @Size(max = 10) String prefix) {
        return prefix + name;
    }

    @Override
    public boolean equals(Object o) { return false; }
}

@interface MyAnnotation {
    String value() default "";
    int[] numbers() default {1, 2};
}

@Retention(RetentionPolicy.RUNTIME)
@Target(ElementType.METHOD)
public @interface Timed {}
""",
"""
<span class="hljs-meta">@Entity</span>
<span class="hljs-meta">@Table(name = &quot;users&quot;, schema = &quot;public&quot;)</span>
<span class="hljs-meta">@SuppressWarnings({&quot;unchecked&quot;, &quot;rawtypes&quot;})</span>
<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">User</span> {
    <span class="hljs-meta">@Id</span>
    <span class="hljs-meta">@GeneratedValue(strategy = GenerationType.IDENTITY)</span>
    <span class="hljs-keyword">private</span> Long id;

    <span class="hljs-meta">@Column(nullable = false, length = 100)</span>
    <span class="hljs-keyword">private</span> String name;

    <span class="hljs-meta">@JsonProperty(&quot;user_name&quot;)</span>
    <span class="hljs-keyword">public</span> String <span class="hljs-title function_">getName</span><span class="hljs-params">(<span class="hljs-meta">@NotNull</span> <span class="hljs-meta">@Size(max = 10)</span> String prefix)</span> {
        <span class="hljs-keyword">return</span> prefix + name;
    }

    <span class="hljs-meta">@Override</span>
    <span class="hljs-keyword">public</span> <span class="hljs-type">boolean</span> <span class="hljs-title function_">equals</span><span class="hljs-params">(Object o)</span> { <span class="hljs-keyword">return</span> <span class="hljs-literal">false</span>; }
}

<span class="hljs-meta">@interface</span> MyAnnotation {
    String <span class="hljs-title function_">value</span><span class="hljs-params">()</span> <span class="hljs-keyword">default</span> <span class="hljs-string">&quot;&quot;</span>;
    <span class="hljs-type">int</span>[] <span class="hljs-title function_">numbers</span><span class="hljs-params">()</span> <span class="hljs-keyword">default</span> {<span class="hljs-number">1</span>, <span class="hljs-number">2</span>};
}

<span class="hljs-meta">@Retention(RetentionPolicy.RUNTIME)</span>
<span class="hljs-meta">@Target(ElementType.METHOD)</span>
<span class="hljs-keyword">public</span> <span class="hljs-meta">@interface</span> Timed {}
""");
    }

    [Fact]
    public void ArraysVarargs()
    {
        AssertHighlighter("java",
"""
void log(String format, Object... args) {}
int sum(int... values) { return 0; }
public String[] split(String s) { return null; }
public List<String>[] buckets() { return null; }
""",
"""
<span class="hljs-keyword">void</span> <span class="hljs-title function_">log</span><span class="hljs-params">(String format, Object... args)</span> {}
<span class="hljs-type">int</span> <span class="hljs-title function_">sum</span><span class="hljs-params">(<span class="hljs-type">int</span>... values)</span> { <span class="hljs-keyword">return</span> <span class="hljs-number">0</span>; }
<span class="hljs-keyword">public</span> String[] <span class="hljs-title function_">split</span><span class="hljs-params">(String s)</span> { <span class="hljs-keyword">return</span> <span class="hljs-literal">null</span>; }
<span class="hljs-keyword">public</span> List&lt;String&gt;[] <span class="hljs-title function_">buckets</span><span class="hljs-params">()</span> { <span class="hljs-keyword">return</span> <span class="hljs-literal">null</span>; }
""");
    }

    [Fact]
    public void CastTernary()
    {
        AssertHighlighter("java",
"""
int x = (int) 3.5;
long y = (long) x;
String s = (String) obj;
Object o = flag ? new Foo() : null;
""",
"""
<span class="hljs-type">int</span> <span class="hljs-variable">x</span> <span class="hljs-operator">=</span> (<span class="hljs-type">int</span>) <span class="hljs-number">3.5</span>;
<span class="hljs-type">long</span> <span class="hljs-variable">y</span> <span class="hljs-operator">=</span> (<span class="hljs-type">long</span>) x;
<span class="hljs-type">String</span> <span class="hljs-variable">s</span> <span class="hljs-operator">=</span> (String) obj;
<span class="hljs-type">Object</span> <span class="hljs-variable">o</span> <span class="hljs-operator">=</span> flag ? <span class="hljs-keyword">new</span> <span class="hljs-title class_">Foo</span>() : <span class="hljs-literal">null</span>;
""");
    }

    [Fact]
    public void CharEscapes()
    {
        AssertHighlighter("java",
"""
char a = '\n';
char b = '\\';
char c = 'é';
char d = '\0';
String e = "tab\there";
""",
"""
<span class="hljs-type">char</span> <span class="hljs-variable">a</span> <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;\n&#x27;</span>;
<span class="hljs-type">char</span> <span class="hljs-variable">b</span> <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;\\&#x27;</span>;
<span class="hljs-type">char</span> <span class="hljs-variable">c</span> <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;é&#x27;</span>;
<span class="hljs-type">char</span> <span class="hljs-variable">d</span> <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;\0&#x27;</span>;
<span class="hljs-type">String</span> <span class="hljs-variable">e</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;tab\there&quot;</span>;
""");
    }

    [Fact]
    public void ClassGeneric()
    {
        AssertHighlighter("java",
"""
public abstract class Repository<T extends Entity, ID> implements Iterable<T>, AutoCloseable {
    private final Map<ID, T> items = new HashMap<>();
    protected static final int MAX = 10;

    public Repository() {
        super();
    }

    public Optional<T> findById(ID id) {
        return Optional.ofNullable(items.get(id));
    }

    public <R> List<R> map(Function<? super T, ? extends R> mapper) {
        return items.values().stream().map(mapper).collect(Collectors.toList());
    }

    public Map<String, List<Integer>> group() { return null; }

    @Override
    public Iterator<T> iterator() {
        return items.values().iterator();
    }

    public String[] names() { return new String[0]; }

    int[] numbers(int[] input) { return input; }

    @Override
    public void close() throws Exception {
        items.clear();
    }
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">abstract</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Repository</span>&lt;T <span class="hljs-keyword">extends</span> <span class="hljs-title class_">Entity</span>, ID&gt; <span class="hljs-keyword">implements</span> <span class="hljs-title class_">Iterable</span>&lt;T&gt;, AutoCloseable {
    <span class="hljs-keyword">private</span> <span class="hljs-keyword">final</span> Map&lt;ID, T&gt; items = <span class="hljs-keyword">new</span> <span class="hljs-title class_">HashMap</span>&lt;&gt;();
    <span class="hljs-keyword">protected</span> <span class="hljs-keyword">static</span> <span class="hljs-keyword">final</span> <span class="hljs-type">int</span> <span class="hljs-variable">MAX</span> <span class="hljs-operator">=</span> <span class="hljs-number">10</span>;

    <span class="hljs-keyword">public</span> <span class="hljs-title function_">Repository</span><span class="hljs-params">()</span> {
        <span class="hljs-built_in">super</span>();
    }

    <span class="hljs-keyword">public</span> Optional&lt;T&gt; <span class="hljs-title function_">findById</span><span class="hljs-params">(ID id)</span> {
        <span class="hljs-keyword">return</span> Optional.ofNullable(items.get(id));
    }

    <span class="hljs-keyword">public</span> &lt;R&gt; List&lt;R&gt; <span class="hljs-title function_">map</span><span class="hljs-params">(Function&lt;? <span class="hljs-built_in">super</span> T, ? extends R&gt; mapper)</span> {
        <span class="hljs-keyword">return</span> items.values().stream().map(mapper).collect(Collectors.toList());
    }

    <span class="hljs-keyword">public</span> Map&lt;String, List&lt;Integer&gt;&gt; <span class="hljs-title function_">group</span><span class="hljs-params">()</span> { <span class="hljs-keyword">return</span> <span class="hljs-literal">null</span>; }

    <span class="hljs-meta">@Override</span>
    <span class="hljs-keyword">public</span> Iterator&lt;T&gt; <span class="hljs-title function_">iterator</span><span class="hljs-params">()</span> {
        <span class="hljs-keyword">return</span> items.values().iterator();
    }

    <span class="hljs-keyword">public</span> String[] <span class="hljs-title function_">names</span><span class="hljs-params">()</span> { <span class="hljs-keyword">return</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">String</span>[<span class="hljs-number">0</span>]; }

    <span class="hljs-type">int</span>[] <span class="hljs-title function_">numbers</span><span class="hljs-params">(<span class="hljs-type">int</span>[] input)</span> { <span class="hljs-keyword">return</span> input; }

    <span class="hljs-meta">@Override</span>
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">void</span> <span class="hljs-title function_">close</span><span class="hljs-params">()</span> <span class="hljs-keyword">throws</span> Exception {
        items.clear();
    }
}
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("java",
"""
// line comment TODO: fix
/* block
   comment FIXME: later */
/**
 * Javadoc for {@link Foo}.
 * Contact john@example.com for details.
 * @param name the name
 * @return the value
 * @throws IOException when it fails
 * @deprecated use {@code bar} instead
 */
int x; // trailing
""",
"""
<span class="hljs-comment">// line comment <span class="hljs-doctag">TODO:</span> fix</span>
<span class="hljs-comment">/* block
   comment <span class="hljs-doctag">FIXME:</span> later */</span>
<span class="hljs-comment">/**
 * Javadoc for {<span class="hljs-doctag">@link</span> Foo}.
 * Contact john@example.com for details.
 * <span class="hljs-doctag">@param</span> name the name
 * <span class="hljs-doctag">@return</span> the value
 * <span class="hljs-doctag">@throws</span> IOException when it fails
 * <span class="hljs-doctag">@deprecated</span> use {<span class="hljs-doctag">@code</span> bar} instead
 */</span>
<span class="hljs-type">int</span> x; <span class="hljs-comment">// trailing</span>
""");
    }

    [Fact]
    public void ComparisonAssign()
    {
        AssertHighlighter("java",
"""
if (a == b) {}
boolean eq = a == b;
x = y;
Foo bar = baz == null ? qux : baz;
""",
"""
<span class="hljs-keyword">if</span> (a == b) {}
<span class="hljs-type">boolean</span> <span class="hljs-variable">eq</span> <span class="hljs-operator">=</span> a == b;
x = y;
<span class="hljs-type">Foo</span> <span class="hljs-variable">bar</span> <span class="hljs-operator">=</span> baz == <span class="hljs-literal">null</span> ? qux : baz;
""");
    }

    [Fact]
    public void ConstructorGenericNew()
    {
        AssertHighlighter("java",
"""
List<String> list = new ArrayList<>();
Map<String, Integer> m = new HashMap<String, Integer>();
Foo foo = new Foo();
foo = new Foo(1, "x");
int[] a = new int[10];
""",
"""
List&lt;String&gt; list = <span class="hljs-keyword">new</span> <span class="hljs-title class_">ArrayList</span>&lt;&gt;();
Map&lt;String, Integer&gt; m = <span class="hljs-keyword">new</span> <span class="hljs-title class_">HashMap</span>&lt;String, Integer&gt;();
<span class="hljs-type">Foo</span> <span class="hljs-variable">foo</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">Foo</span>();
foo = <span class="hljs-keyword">new</span> <span class="hljs-title class_">Foo</span>(<span class="hljs-number">1</span>, <span class="hljs-string">&quot;x&quot;</span>);
<span class="hljs-type">int</span>[] a = <span class="hljs-keyword">new</span> <span class="hljs-title class_">int</span>[<span class="hljs-number">10</span>];
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("java",
"""
public int compute(int n) {
    if (n <= 0) {
        return 0;
    } else if (n == 1) {
        return 1;
    } else {
        int total = 0;
        for (int i = 0; i < n; i++) {
            total += i;
        }
        while (total > 100) {
            total /= 2;
        }
        do {
            total--;
        } while (total > 50);
        return total;
    }
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-type">int</span> <span class="hljs-title function_">compute</span><span class="hljs-params">(<span class="hljs-type">int</span> n)</span> {
    <span class="hljs-keyword">if</span> (n &lt;= <span class="hljs-number">0</span>) {
        <span class="hljs-keyword">return</span> <span class="hljs-number">0</span>;
    } <span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> (n == <span class="hljs-number">1</span>) {
        <span class="hljs-keyword">return</span> <span class="hljs-number">1</span>;
    } <span class="hljs-keyword">else</span> {
        <span class="hljs-type">int</span> <span class="hljs-variable">total</span> <span class="hljs-operator">=</span> <span class="hljs-number">0</span>;
        <span class="hljs-keyword">for</span> (<span class="hljs-type">int</span> <span class="hljs-variable">i</span> <span class="hljs-operator">=</span> <span class="hljs-number">0</span>; i &lt; n; i++) {
            total += i;
        }
        <span class="hljs-keyword">while</span> (total &gt; <span class="hljs-number">100</span>) {
            total /= <span class="hljs-number">2</span>;
        }
        <span class="hljs-keyword">do</span> {
            total--;
        } <span class="hljs-keyword">while</span> (total &gt; <span class="hljs-number">50</span>);
        <span class="hljs-keyword">return</span> total;
    }
}
""");
    }

    [Fact]
    public void ElseMethod()
    {
        AssertHighlighter("java",
"""
if (x) foo(); else bar();
else doIt();
""",
"""
<span class="hljs-keyword">if</span> (x) foo(); <span class="hljs-keyword">else</span> bar();
<span class="hljs-keyword">else</span> doIt();
""");
    }

    [Fact]
    public void ElsePrefixedIdentifiers()
    {
        AssertHighlighter("java",
"""
elsewhere x = 1;
else x = 2;
String[] names = new String[2];
char u = '\u0041';
String s = "\u00e9";
foo.new Bar();
int x = a.b.c;
x.else y = 3;
""",
"""
<span class="hljs-type">elsewhere</span> <span class="hljs-variable">x</span> <span class="hljs-operator">=</span> <span class="hljs-number">1</span>;
<span class="hljs-keyword">else</span> x = <span class="hljs-number">2</span>;
String[] names = <span class="hljs-keyword">new</span> <span class="hljs-title class_">String</span>[<span class="hljs-number">2</span>];
<span class="hljs-type">char</span> <span class="hljs-variable">u</span> <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;\u0041&#x27;</span>;
<span class="hljs-type">String</span> <span class="hljs-variable">s</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;\u00e9&quot;</span>;
foo.<span class="hljs-keyword">new</span> <span class="hljs-title class_">Bar</span>();
<span class="hljs-type">int</span> <span class="hljs-variable">x</span> <span class="hljs-operator">=</span> a.b.c;
x.<span class="hljs-keyword">else</span> y = <span class="hljs-number">3</span>;
""");
    }

    [Fact]
    public void Exceptions()
    {
        AssertHighlighter("java",
"""
try (var reader = new BufferedReader(new FileReader(path))) {
    String line = reader.readLine();
} catch (IOException | UncheckedIOException e) {
    throw new RuntimeException(e);
} finally {
    cleanup();
}
assert x > 0 : "x must be positive";
synchronized (lock) {
    counter++;
}
""",
"""
<span class="hljs-keyword">try</span> (<span class="hljs-type">var</span> <span class="hljs-variable">reader</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">BufferedReader</span>(<span class="hljs-keyword">new</span> <span class="hljs-title class_">FileReader</span>(path))) {
    <span class="hljs-type">String</span> <span class="hljs-variable">line</span> <span class="hljs-operator">=</span> reader.readLine();
} <span class="hljs-keyword">catch</span> (IOException | UncheckedIOException e) {
    <span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">RuntimeException</span>(e);
} <span class="hljs-keyword">finally</span> {
    cleanup();
}
<span class="hljs-keyword">assert</span> x &gt; <span class="hljs-number">0</span> : <span class="hljs-string">&quot;x must be positive&quot;</span>;
<span class="hljs-keyword">synchronized</span> (lock) {
    counter++;
}
""");
    }

    [Fact]
    public void Fields()
    {
        AssertHighlighter("java",
"""
private static final long serialVersionUID = 1L;
public volatile boolean running = true;
protected transient Object cache = null;
final String NAME = "x";
int count;
String a, b = "b";
int[] arr = {1, 2, 3};
int[][] matrix = new int[3][3];
String[] names = new String[] {"a", "b"};
""",
"""
<span class="hljs-keyword">private</span> <span class="hljs-keyword">static</span> <span class="hljs-keyword">final</span> <span class="hljs-type">long</span> <span class="hljs-variable">serialVersionUID</span> <span class="hljs-operator">=</span> <span class="hljs-number">1L</span>;
<span class="hljs-keyword">public</span> <span class="hljs-keyword">volatile</span> <span class="hljs-type">boolean</span> <span class="hljs-variable">running</span> <span class="hljs-operator">=</span> <span class="hljs-literal">true</span>;
<span class="hljs-keyword">protected</span> <span class="hljs-keyword">transient</span> <span class="hljs-type">Object</span> <span class="hljs-variable">cache</span> <span class="hljs-operator">=</span> <span class="hljs-literal">null</span>;
<span class="hljs-keyword">final</span> <span class="hljs-type">String</span> <span class="hljs-variable">NAME</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;x&quot;</span>;
<span class="hljs-type">int</span> count;
String a, b = <span class="hljs-string">&quot;b&quot;</span>;
<span class="hljs-type">int</span>[] arr = {<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>};
<span class="hljs-type">int</span>[][] matrix = <span class="hljs-keyword">new</span> <span class="hljs-title class_">int</span>[<span class="hljs-number">3</span>][<span class="hljs-number">3</span>];
String[] names = <span class="hljs-keyword">new</span> <span class="hljs-title class_">String</span>[] {<span class="hljs-string">&quot;a&quot;</span>, <span class="hljs-string">&quot;b&quot;</span>};
""");
    }

    [Fact]
    public void ForEachGenerics()
    {
        AssertHighlighter("java",
"""
for (Map.Entry<String, Integer> e : map.entrySet()) {}
for (final String s : list) {}
""",
"""
<span class="hljs-keyword">for</span> (Map.Entry&lt;String, Integer&gt; e : map.entrySet()) {}
<span class="hljs-keyword">for</span> (<span class="hljs-keyword">final</span> String s : list) {}
""");
    }

    [Fact]
    public void GenericMethodCall()
    {
        AssertHighlighter("java",
"""
Collections.<String>emptyList();
this.<T>foo();
List<String> x = Arrays.asList("a", "b");
""",
"""
Collections.&lt;String&gt;emptyList();
<span class="hljs-built_in">this</span>.&lt;T&gt;foo();
List&lt;String&gt; x = Arrays.asList(<span class="hljs-string">&quot;a&quot;</span>, <span class="hljs-string">&quot;b&quot;</span>);
""");
    }

    [Fact]
    public void GenericsMethods()
    {
        AssertHighlighter("java",
"""
public static <T extends Comparable<T>> T max(List<T> list) {
    T best = list.get(0);
    return best;
}
private <K, V> Map<K, V> toMap(List<K> keys, List<V> values) { return null; }
List<? extends Number> numbers = new ArrayList<>();
Map<String, Map<String, List<Integer>>> deep = new HashMap<>();
Class<?> clazz = String.class;
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> &lt;T <span class="hljs-keyword">extends</span> <span class="hljs-title class_">Comparable</span>&lt;T&gt;&gt; T <span class="hljs-title function_">max</span><span class="hljs-params">(List&lt;T&gt; list)</span> {
    <span class="hljs-type">T</span> <span class="hljs-variable">best</span> <span class="hljs-operator">=</span> list.get(<span class="hljs-number">0</span>);
    <span class="hljs-keyword">return</span> best;
}
<span class="hljs-keyword">private</span> &lt;K, V&gt; Map&lt;K, V&gt; <span class="hljs-title function_">toMap</span><span class="hljs-params">(List&lt;K&gt; keys, List&lt;V&gt; values)</span> { <span class="hljs-keyword">return</span> <span class="hljs-literal">null</span>; }
List&lt;? <span class="hljs-keyword">extends</span> <span class="hljs-title class_">Number</span>&gt; numbers = <span class="hljs-keyword">new</span> <span class="hljs-title class_">ArrayList</span>&lt;&gt;();
Map&lt;String, Map&lt;String, List&lt;Integer&gt;&gt;&gt; deep = <span class="hljs-keyword">new</span> <span class="hljs-title class_">HashMap</span>&lt;&gt;();
Class&lt;?&gt; clazz = String.class;
""");
    }

    [Fact]
    public void Hello()
    {
        AssertHighlighter("jsp",
"""
package com.example;

import java.util.List;
import java.util.Map;
import static java.lang.Math.*;

public class HelloWorld {
    public static void main(String[] args) {
        System.out.println("Hello, World!");
    }
}
""",
"""
<span class="hljs-keyword">package</span> com.example;

<span class="hljs-keyword">import</span> java.util.List;
<span class="hljs-keyword">import</span> java.util.Map;
<span class="hljs-keyword">import</span> <span class="hljs-keyword">static</span> java.lang.Math.*;

<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">HelloWorld</span> {
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> <span class="hljs-keyword">void</span> <span class="hljs-title function_">main</span><span class="hljs-params">(String[] args)</span> {
        System.out.println(<span class="hljs-string">&quot;Hello, World!&quot;</span>);
    }
}
""");
    }

    [Fact]
    public void HexFloatEdge()
    {
        AssertHighlighter("java",
"""
double a = 0x.8p1;
double b = 0xA.p2;
long c = 0L;
long d = 10l;
float e = 1e10f;
int f = 09;
""",
"""
<span class="hljs-type">double</span> <span class="hljs-variable">a</span> <span class="hljs-operator">=</span> <span class="hljs-number">0x.8p1</span>;
<span class="hljs-type">double</span> <span class="hljs-variable">b</span> <span class="hljs-operator">=</span> <span class="hljs-number">0xA.p2</span>;
<span class="hljs-type">long</span> <span class="hljs-variable">c</span> <span class="hljs-operator">=</span> <span class="hljs-number">0L</span>;
<span class="hljs-type">long</span> <span class="hljs-variable">d</span> <span class="hljs-operator">=</span> <span class="hljs-number">10l</span>;
<span class="hljs-type">float</span> <span class="hljs-variable">e</span> <span class="hljs-operator">=</span> <span class="hljs-number">1e10f</span>;
<span class="hljs-type">int</span> <span class="hljs-variable">f</span> <span class="hljs-operator">=</span> 09;
""");
    }

    [Fact]
    public void Illegal()
    {
        AssertHighlighter("java",
"""
int x = 1; # not java
</div>
int y = 2;
""",
"""
<span class="hljs-type">int</span> <span class="hljs-variable">x</span> <span class="hljs-operator">=</span> <span class="hljs-number">1</span>; # not java
&lt;/div&gt;
<span class="hljs-type">int</span> <span class="hljs-variable">y</span> <span class="hljs-operator">=</span> <span class="hljs-number">2</span>;
""");
    }

    [Fact]
    public void ImportJava()
    {
        AssertHighlighter("java",
"""
import java.util.function.Function;
import java.util.stream.Collectors;
import javax.annotation.Nonnull;
import org.junit.jupiter.api.Test;
""",
"""
<span class="hljs-keyword">import</span> java.util.function.Function;
<span class="hljs-keyword">import</span> java.util.stream.Collectors;
<span class="hljs-keyword">import</span> javax.annotation.Nonnull;
<span class="hljs-keyword">import</span> org.junit.jupiter.api.Test;
""");
    }

    [Fact]
    public void InnerAnonymous()
    {
        AssertHighlighter("java",
"""
public class Outer {
    private int value = 10;

    class Inner {
        int get() { return value; }
    }

    static class Nested {}

    void run() {
        Runnable r = new Runnable() {
            @Override
            public void run() {
                System.out.println(Outer.this.value);
            }
        };
        Object o = new Object() { int x = 1; };
    }
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Outer</span> {
    <span class="hljs-keyword">private</span> <span class="hljs-type">int</span> <span class="hljs-variable">value</span> <span class="hljs-operator">=</span> <span class="hljs-number">10</span>;

    <span class="hljs-keyword">class</span> <span class="hljs-title class_">Inner</span> {
        <span class="hljs-type">int</span> <span class="hljs-title function_">get</span><span class="hljs-params">()</span> { <span class="hljs-keyword">return</span> value; }
    }

    <span class="hljs-keyword">static</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Nested</span> {}

    <span class="hljs-keyword">void</span> <span class="hljs-title function_">run</span><span class="hljs-params">()</span> {
        <span class="hljs-type">Runnable</span> <span class="hljs-variable">r</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">Runnable</span>() {
            <span class="hljs-meta">@Override</span>
            <span class="hljs-keyword">public</span> <span class="hljs-keyword">void</span> <span class="hljs-title function_">run</span><span class="hljs-params">()</span> {
                System.out.println(Outer.<span class="hljs-built_in">this</span>.value);
            }
        };
        <span class="hljs-type">Object</span> <span class="hljs-variable">o</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">Object</span>() { <span class="hljs-type">int</span> <span class="hljs-variable">x</span> <span class="hljs-operator">=</span> <span class="hljs-number">1</span>; };
    }
}
""");
    }

    [Fact]
    public void Instanceof()
    {
        AssertHighlighter("java",
"""
if (obj instanceof String s && !s.isEmpty()) {
    System.out.println(s);
}
if (shape instanceof Circle c) return c.radius();
boolean b = x instanceof Integer;
""",
"""
<span class="hljs-keyword">if</span> (obj <span class="hljs-keyword">instanceof</span> String s &amp;&amp; !s.isEmpty()) {
    System.out.println(s);
}
<span class="hljs-keyword">if</span> (shape <span class="hljs-keyword">instanceof</span> Circle c) <span class="hljs-keyword">return</span> c.radius();
<span class="hljs-type">boolean</span> <span class="hljs-variable">b</span> <span class="hljs-operator">=</span> x <span class="hljs-keyword">instanceof</span> Integer;
""");
    }

    [Fact]
    public void InterfaceDefaultGeneric()
    {
        AssertHighlighter("java",
"""
public interface Visitor<R> {
    R visitNumber(Number n);
    R visitAdd(Add a);
    default <U> Visitor<U> andThen(Function<R, U> f) { return null; }
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">interface</span> <span class="hljs-title class_">Visitor</span>&lt;R&gt; {
    R <span class="hljs-title function_">visitNumber</span><span class="hljs-params">(Number n)</span>;
    R <span class="hljs-title function_">visitAdd</span><span class="hljs-params">(Add a)</span>;
    <span class="hljs-keyword">default</span> &lt;U&gt; Visitor&lt;U&gt; <span class="hljs-title function_">andThen</span><span class="hljs-params">(Function&lt;R, U&gt; f)</span> { <span class="hljs-keyword">return</span> <span class="hljs-literal">null</span>; }
}
""");
    }

    [Fact]
    public void InterfaceEnum()
    {
        AssertHighlighter("java",
"""
public interface Shape {
    double area();
    default String describe() { return "Shape with area " + area(); }
    static Shape unit() { return new Square(1); }
}

enum Color {
    RED("r"), GREEN("g"), BLUE("b");

    private final String code;

    Color(String code) {
        this.code = code;
    }

    public String getCode() { return code; }
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">interface</span> <span class="hljs-title class_">Shape</span> {
    <span class="hljs-type">double</span> <span class="hljs-title function_">area</span><span class="hljs-params">()</span>;
    <span class="hljs-keyword">default</span> String <span class="hljs-title function_">describe</span><span class="hljs-params">()</span> { <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;Shape with area &quot;</span> + area(); }
    <span class="hljs-keyword">static</span> Shape <span class="hljs-title function_">unit</span><span class="hljs-params">()</span> { <span class="hljs-keyword">return</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">Square</span>(<span class="hljs-number">1</span>); }
}

<span class="hljs-keyword">enum</span> <span class="hljs-title class_">Color</span> {
    RED(<span class="hljs-string">&quot;r&quot;</span>), GREEN(<span class="hljs-string">&quot;g&quot;</span>), BLUE(<span class="hljs-string">&quot;b&quot;</span>);

    <span class="hljs-keyword">private</span> <span class="hljs-keyword">final</span> String code;

    Color(String code) {
        <span class="hljs-built_in">this</span>.code = code;
    }

    <span class="hljs-keyword">public</span> String <span class="hljs-title function_">getCode</span><span class="hljs-params">()</span> { <span class="hljs-keyword">return</span> code; }
}
""");
    }

    [Fact]
    public void Labels()
    {
        AssertHighlighter("java",
"""
outer:
for (int i = 0; i < 10; i++) {
    inner:
    for (int j = 0; j < 10; j++) {
        if (j == 5) continue outer;
        if (i == 5) break outer;
    }
}
""",
"""
outer:
<span class="hljs-keyword">for</span> (<span class="hljs-type">int</span> <span class="hljs-variable">i</span> <span class="hljs-operator">=</span> <span class="hljs-number">0</span>; i &lt; <span class="hljs-number">10</span>; i++) {
    inner:
    <span class="hljs-keyword">for</span> (<span class="hljs-type">int</span> <span class="hljs-variable">j</span> <span class="hljs-operator">=</span> <span class="hljs-number">0</span>; j &lt; <span class="hljs-number">10</span>; j++) {
        <span class="hljs-keyword">if</span> (j == <span class="hljs-number">5</span>) <span class="hljs-keyword">continue</span> outer;
        <span class="hljs-keyword">if</span> (i == <span class="hljs-number">5</span>) <span class="hljs-keyword">break</span> outer;
    }
}
""");
    }

    [Fact]
    public void LongMethodSig()
    {
        AssertHighlighter("java",
"""
@Transactional(readOnly = true)
public ResponseEntity<List<UserDto>> getUsers(
        @RequestParam(defaultValue = "0") int page,
        @RequestParam(defaultValue = "20") int size) {
    return ResponseEntity.ok(service.findAll(page, size));
}
""",
"""
<span class="hljs-meta">@Transactional(readOnly = true)</span>
<span class="hljs-keyword">public</span> ResponseEntity&lt;List&lt;UserDto&gt;&gt; <span class="hljs-title function_">getUsers</span><span class="hljs-params">(
        <span class="hljs-meta">@RequestParam(defaultValue = &quot;0&quot;)</span> <span class="hljs-type">int</span> page,
        <span class="hljs-meta">@RequestParam(defaultValue = &quot;20&quot;)</span> <span class="hljs-type">int</span> size)</span> {
    <span class="hljs-keyword">return</span> ResponseEntity.ok(service.findAll(page, size));
}
""");
    }

    [Fact]
    public void MainProgram()
    {
        AssertHighlighter("java",
"""
import java.io.*;
import java.util.concurrent.CompletableFuture;

public final class Main {
    private static final Logger LOG = LoggerFactory.getLogger(Main.class);

    public static void main(final String... args) throws InterruptedException {
        var executor = Executors.newFixedThreadPool(4);
        CompletableFuture<Void> future = CompletableFuture.runAsync(() -> {
            LOG.info("Running in {}", Thread.currentThread().getName());
        }, executor);
        future.join();
        executor.shutdown();
    }
}
""",
"""
<span class="hljs-keyword">import</span> java.io.*;
<span class="hljs-keyword">import</span> java.util.concurrent.CompletableFuture;

<span class="hljs-keyword">public</span> <span class="hljs-keyword">final</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Main</span> {
    <span class="hljs-keyword">private</span> <span class="hljs-keyword">static</span> <span class="hljs-keyword">final</span> <span class="hljs-type">Logger</span> <span class="hljs-variable">LOG</span> <span class="hljs-operator">=</span> LoggerFactory.getLogger(Main.class);

    <span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> <span class="hljs-keyword">void</span> <span class="hljs-title function_">main</span><span class="hljs-params">(<span class="hljs-keyword">final</span> String... args)</span> <span class="hljs-keyword">throws</span> InterruptedException {
        <span class="hljs-type">var</span> <span class="hljs-variable">executor</span> <span class="hljs-operator">=</span> Executors.newFixedThreadPool(<span class="hljs-number">4</span>);
        CompletableFuture&lt;Void&gt; future = CompletableFuture.runAsync(() -&gt; {
            LOG.info(<span class="hljs-string">&quot;Running in {}&quot;</span>, Thread.currentThread().getName());
        }, executor);
        future.join();
        executor.shutdown();
    }
}
""");
    }

    [Fact]
    public void MethodChain()
    {
        AssertHighlighter("java",
"""
return builder
    .withName("x")
    .withAge(3)
    .build();
""",
"""
<span class="hljs-keyword">return</span> builder
    .withName(<span class="hljs-string">&quot;x&quot;</span>)
    .withAge(<span class="hljs-number">3</span>)
    .build();
""");
    }

    [Fact]
    public void Modules()
    {
        AssertHighlighter("java",
"""
module com.example.app {
    requires java.base;
    requires transitive java.sql;
    exports com.example.api;
    exports com.example.internal to com.example.test;
    opens com.example.model;
    uses com.example.spi.Service;
    provides com.example.spi.Service with com.example.impl.ServiceImpl;
}
""",
"""
<span class="hljs-keyword">module</span> com.example.app {
    <span class="hljs-keyword">requires</span> java.base;
    <span class="hljs-keyword">requires</span> transitive java.sql;
    <span class="hljs-keyword">exports</span> com.example.api;
    <span class="hljs-keyword">exports</span> com.example.internal to com.example.test;
    opens com.example.model;
    uses com.example.spi.Service;
    provides com.example.spi.Service with com.example.impl.ServiceImpl;
}
""");
    }

    [Fact]
    public void NativeStrictfp()
    {
        AssertHighlighter("java",
"""
public native int nativeMethod();
strictfp double compute() { return 1.0; }
transient int t;
goto label;
const int X = 1;
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">native</span> <span class="hljs-type">int</span> <span class="hljs-title function_">nativeMethod</span><span class="hljs-params">()</span>;
<span class="hljs-keyword">strictfp</span> <span class="hljs-type">double</span> <span class="hljs-title function_">compute</span><span class="hljs-params">()</span> { <span class="hljs-keyword">return</span> <span class="hljs-number">1.0</span>; }
<span class="hljs-keyword">transient</span> <span class="hljs-type">int</span> t;
<span class="hljs-keyword">goto</span> label;
<span class="hljs-keyword">const</span> <span class="hljs-type">int</span> <span class="hljs-variable">X</span> <span class="hljs-operator">=</span> <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void NestedClassDecl()
    {
        AssertHighlighter("java",
"""
public static final class Builder<T> extends AbstractBuilder<T, Builder<T>> {
}
class A extends B implements C, D {}
interface E extends F, G {}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> <span class="hljs-keyword">final</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Builder</span>&lt;T&gt; <span class="hljs-keyword">extends</span> <span class="hljs-title class_">AbstractBuilder</span>&lt;T, Builder&lt;T&gt;&gt; {
}
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> <span class="hljs-keyword">extends</span> <span class="hljs-title class_">B</span> <span class="hljs-keyword">implements</span> <span class="hljs-title class_">C</span>, D {}
<span class="hljs-keyword">interface</span> <span class="hljs-title class_">E</span> <span class="hljs-keyword">extends</span> <span class="hljs-title class_">F</span>, G {}
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("java",
"""
int a = 0;
int b = 42;
int c = 1_000_000;
long d = 123L;
long e = 0xFFFF_FFFFL;
int f = 0x1F;
int g = 0b1010_1010;
int h = 0B11;
int i = 0777;
int i2 = 0_7;
double j = 3.14;
double k = 3.14e10;
double l = 1e-5;
double m = .5;
double n = 5.;
float o = 1.5f;
float p = 2F;
double q = 1.0d;
double r = 0x1.8p1;
double s = 0x1p-3;
double t = 1_234.567_8e+1_0D;
int u = -1;
double v = .5e3f;
""",
"""
<span class="hljs-type">int</span> <span class="hljs-variable">a</span> <span class="hljs-operator">=</span> <span class="hljs-number">0</span>;
<span class="hljs-type">int</span> <span class="hljs-variable">b</span> <span class="hljs-operator">=</span> <span class="hljs-number">42</span>;
<span class="hljs-type">int</span> <span class="hljs-variable">c</span> <span class="hljs-operator">=</span> <span class="hljs-number">1_000_000</span>;
<span class="hljs-type">long</span> <span class="hljs-variable">d</span> <span class="hljs-operator">=</span> <span class="hljs-number">123L</span>;
<span class="hljs-type">long</span> <span class="hljs-variable">e</span> <span class="hljs-operator">=</span> <span class="hljs-number">0xFFFF_FFFFL</span>;
<span class="hljs-type">int</span> <span class="hljs-variable">f</span> <span class="hljs-operator">=</span> <span class="hljs-number">0x1F</span>;
<span class="hljs-type">int</span> <span class="hljs-variable">g</span> <span class="hljs-operator">=</span> <span class="hljs-number">0b1010_1010</span>;
<span class="hljs-type">int</span> <span class="hljs-variable">h</span> <span class="hljs-operator">=</span> <span class="hljs-number">0B11</span>;
<span class="hljs-type">int</span> <span class="hljs-variable">i</span> <span class="hljs-operator">=</span> <span class="hljs-number">0777</span>;
<span class="hljs-type">int</span> <span class="hljs-variable">i2</span> <span class="hljs-operator">=</span> <span class="hljs-number">0_7</span>;
<span class="hljs-type">double</span> <span class="hljs-variable">j</span> <span class="hljs-operator">=</span> <span class="hljs-number">3.14</span>;
<span class="hljs-type">double</span> <span class="hljs-variable">k</span> <span class="hljs-operator">=</span> <span class="hljs-number">3.14e10</span>;
<span class="hljs-type">double</span> <span class="hljs-variable">l</span> <span class="hljs-operator">=</span> <span class="hljs-number">1e-5</span>;
<span class="hljs-type">double</span> <span class="hljs-variable">m</span> <span class="hljs-operator">=</span> <span class="hljs-number">.5</span>;
<span class="hljs-type">double</span> <span class="hljs-variable">n</span> <span class="hljs-operator">=</span> <span class="hljs-number">5.</span>;
<span class="hljs-type">float</span> <span class="hljs-variable">o</span> <span class="hljs-operator">=</span> <span class="hljs-number">1.5f</span>;
<span class="hljs-type">float</span> <span class="hljs-variable">p</span> <span class="hljs-operator">=</span> <span class="hljs-number">2F</span>;
<span class="hljs-type">double</span> <span class="hljs-variable">q</span> <span class="hljs-operator">=</span> <span class="hljs-number">1.0d</span>;
<span class="hljs-type">double</span> <span class="hljs-variable">r</span> <span class="hljs-operator">=</span> <span class="hljs-number">0x1.8p1</span>;
<span class="hljs-type">double</span> <span class="hljs-variable">s</span> <span class="hljs-operator">=</span> <span class="hljs-number">0x1p-3</span>;
<span class="hljs-type">double</span> <span class="hljs-variable">t</span> <span class="hljs-operator">=</span> <span class="hljs-number">1_234.567_8e+1_0D</span>;
<span class="hljs-type">int</span> <span class="hljs-variable">u</span> <span class="hljs-operator">=</span> -<span class="hljs-number">1</span>;
<span class="hljs-type">double</span> <span class="hljs-variable">v</span> <span class="hljs-operator">=</span> <span class="hljs-number">.5e3f</span>;
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("java",
"""
int a = b + c - d * e / f % g;
a += 1; a -= 1; a *= 2; a /= 2; a %= 3;
a <<= 1; a >>= 1; a >>>= 1; a &= 1; a |= 1; a ^= 1;
boolean t = a == b && c != d || !e;
int t2 = cond ? 1 : 2;
int bits = ~x & y | z ^ w;
int shifted = x << 2 >> 1 >>> 3;
i++; --j;
""",
"""
<span class="hljs-type">int</span> <span class="hljs-variable">a</span> <span class="hljs-operator">=</span> b + c - d * e / f % g;
a += <span class="hljs-number">1</span>; a -= <span class="hljs-number">1</span>; a *= <span class="hljs-number">2</span>; a /= <span class="hljs-number">2</span>; a %= <span class="hljs-number">3</span>;
a &lt;&lt;= <span class="hljs-number">1</span>; a &gt;&gt;= <span class="hljs-number">1</span>; a &gt;&gt;&gt;= <span class="hljs-number">1</span>; a &amp;= <span class="hljs-number">1</span>; a |= <span class="hljs-number">1</span>; a ^= <span class="hljs-number">1</span>;
<span class="hljs-type">boolean</span> <span class="hljs-variable">t</span> <span class="hljs-operator">=</span> a == b &amp;&amp; c != d || !e;
<span class="hljs-type">int</span> <span class="hljs-variable">t2</span> <span class="hljs-operator">=</span> cond ? <span class="hljs-number">1</span> : <span class="hljs-number">2</span>;
<span class="hljs-type">int</span> <span class="hljs-variable">bits</span> <span class="hljs-operator">=</span> ~x &amp; y | z ^ w;
<span class="hljs-type">int</span> <span class="hljs-variable">shifted</span> <span class="hljs-operator">=</span> x &lt;&lt; <span class="hljs-number">2</span> &gt;&gt; <span class="hljs-number">1</span> &gt;&gt;&gt; <span class="hljs-number">3</span>;
i++; --j;
""");
    }

    [Fact]
    public void Records()
    {
        AssertHighlighter("java",
"""
public record Point(int x, int y) {
    public Point {
        if (x < 0) throw new IllegalArgumentException("x");
    }

    public static Point origin() { return new Point(0, 0); }
}

record Pair<A, B>(A first, B second) implements Serializable {}

record Empty() {}

record Person(String name, /* age */ int age) // comment
{
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">record</span> <span class="hljs-title class_">Point</span><span class="hljs-params">(<span class="hljs-type">int</span> x, <span class="hljs-type">int</span> y)</span> {
    <span class="hljs-keyword">public</span> Point {
        <span class="hljs-keyword">if</span> (x &lt; <span class="hljs-number">0</span>) <span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">IllegalArgumentException</span>(<span class="hljs-string">&quot;x&quot;</span>);
    }

    <span class="hljs-keyword">public</span> <span class="hljs-keyword">static</span> Point <span class="hljs-title function_">origin</span><span class="hljs-params">()</span> { <span class="hljs-keyword">return</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">Point</span>(<span class="hljs-number">0</span>, <span class="hljs-number">0</span>); }
}

<span class="hljs-keyword">record</span> <span class="hljs-title class_">Pair</span>&lt;A, B&gt;(A first, B second) <span class="hljs-keyword">implements</span> <span class="hljs-title class_">Serializable</span> {}

<span class="hljs-keyword">record</span> <span class="hljs-title class_">Empty</span><span class="hljs-params">()</span> {}

<span class="hljs-keyword">record</span> <span class="hljs-title class_">Person</span><span class="hljs-params">(String name, <span class="hljs-comment">/* age */</span> <span class="hljs-type">int</span> age)</span> <span class="hljs-comment">// comment</span>
{
}
""");
    }

    [Fact]
    public void ReturnNew()
    {
        AssertHighlighter("java",
"""
return new Foo();
throw new Bar("x");
return value(1);
""",
"""
<span class="hljs-keyword">return</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">Foo</span>();
<span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">Bar</span>(<span class="hljs-string">&quot;x&quot;</span>);
<span class="hljs-keyword">return</span> value(<span class="hljs-number">1</span>);
""");
    }

    [Fact]
    public void Sealed()
    {
        AssertHighlighter("java",
"""
public sealed interface Shape permits Circle, Square, Rectangle {}

public final class Circle implements Shape {}
public non-sealed class Square implements Shape {}
sealed abstract class Vehicle permits Car, Truck {}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">sealed</span> <span class="hljs-keyword">interface</span> <span class="hljs-title class_">Shape</span> <span class="hljs-keyword">permits</span> Circle, Square, Rectangle {}

<span class="hljs-keyword">public</span> <span class="hljs-keyword">final</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Circle</span> <span class="hljs-keyword">implements</span> <span class="hljs-title class_">Shape</span> {}
<span class="hljs-keyword">public</span> <span class="hljs-keyword">non-sealed</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Square</span> <span class="hljs-keyword">implements</span> <span class="hljs-title class_">Shape</span> {}
<span class="hljs-keyword">sealed</span> <span class="hljs-keyword">abstract</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Vehicle</span> <span class="hljs-keyword">permits</span> Car, Truck {}
""");
    }

    [Fact]
    public void StaticInit()
    {
        AssertHighlighter("java",
"""
static {
    INSTANCE = new Singleton();
}
{
    instanceInit();
}
""",
"""
<span class="hljs-keyword">static</span> {
    INSTANCE = <span class="hljs-keyword">new</span> <span class="hljs-title class_">Singleton</span>();
}
{
    instanceInit();
}
""");
    }

    [Fact]
    public void Streams()
    {
        AssertHighlighter("java",
"""
Map<Boolean, List<Integer>> partition = IntStream.rangeClosed(1, 100)
    .boxed()
    .collect(Collectors.partitioningBy(n -> n % 2 == 0));
double avg = list.stream().mapToInt(Integer::intValue).average().orElse(0.0);
""",
"""
Map&lt;Boolean, List&lt;Integer&gt;&gt; partition = IntStream.rangeClosed(<span class="hljs-number">1</span>, <span class="hljs-number">100</span>)
    .boxed()
    .collect(Collectors.partitioningBy(n -&gt; n % <span class="hljs-number">2</span> == <span class="hljs-number">0</span>));
<span class="hljs-type">double</span> <span class="hljs-variable">avg</span> <span class="hljs-operator">=</span> list.stream().mapToInt(Integer::intValue).average().orElse(<span class="hljs-number">0.0</span>);
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("java",
""""
String s = "Hello \"world\"\n\t\\";
char c = 'a';
char q = '\'';
char u = 'A';
String empty = "";
String text = """
    Hello,
      "World" \
    with \""" escapes
    """;
String x = "unterminated
int y = 1;
"""",
"""
<span class="hljs-type">String</span> <span class="hljs-variable">s</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;Hello \&quot;world\&quot;\n\t\\&quot;</span>;
<span class="hljs-type">char</span> <span class="hljs-variable">c</span> <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;a&#x27;</span>;
<span class="hljs-type">char</span> <span class="hljs-variable">q</span> <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;\&#x27;&#x27;</span>;
<span class="hljs-type">char</span> <span class="hljs-variable">u</span> <span class="hljs-operator">=</span> <span class="hljs-string">&#x27;A&#x27;</span>;
<span class="hljs-type">String</span> <span class="hljs-variable">empty</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;&quot;</span>;
<span class="hljs-type">String</span> <span class="hljs-variable">text</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;&quot;&quot;
    Hello,
      &quot;World&quot; \
    with \&quot;&quot;&quot; escapes
    &quot;&quot;&quot;</span>;
<span class="hljs-type">String</span> <span class="hljs-variable">x</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;unterminated
int y = 1;</span>
""");
    }

    [Fact]
    public void Switch()
    {
        AssertHighlighter("java",
"""
String result = switch (day) {
    case MONDAY, FRIDAY, SUNDAY -> "six";
    case TUESDAY -> {
        yield "seven";
    }
    default -> throw new IllegalStateException("Unexpected: " + day);
};

switch (obj) {
    case Integer i when i > 10 -> System.out.println("big " + i);
    case String s -> System.out.println(s.length());
    case null -> System.out.println("null");
    default -> {}
}

switch (x) {
    case 1:
        foo();
        break;
    default:
        bar();
}
""",
"""
<span class="hljs-type">String</span> <span class="hljs-variable">result</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">switch</span> (day) {
    <span class="hljs-keyword">case</span> MONDAY, FRIDAY, SUNDAY -&gt; <span class="hljs-string">&quot;six&quot;</span>;
    <span class="hljs-keyword">case</span> TUESDAY -&gt; {
        <span class="hljs-keyword">yield</span> <span class="hljs-string">&quot;seven&quot;</span>;
    }
    <span class="hljs-keyword">default</span> -&gt; <span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">IllegalStateException</span>(<span class="hljs-string">&quot;Unexpected: &quot;</span> + day);
};

<span class="hljs-keyword">switch</span> (obj) {
    <span class="hljs-keyword">case</span> Integer i <span class="hljs-keyword">when</span> i &gt; <span class="hljs-number">10</span> -&gt; System.out.println(<span class="hljs-string">&quot;big &quot;</span> + i);
    <span class="hljs-keyword">case</span> String s -&gt; System.out.println(s.length());
    <span class="hljs-keyword">case</span> <span class="hljs-literal">null</span> -&gt; System.out.println(<span class="hljs-string">&quot;null&quot;</span>);
    <span class="hljs-keyword">default</span> -&gt; {}
}

<span class="hljs-keyword">switch</span> (x) {
    <span class="hljs-keyword">case</span> <span class="hljs-number">1</span>:
        foo();
        <span class="hljs-keyword">break</span>;
    <span class="hljs-keyword">default</span>:
        bar();
}
""");
    }

    [Fact]
    public void TestClass()
    {
        AssertHighlighter("java",
"""
class CalculatorTest {
    private Calculator calculator;

    @BeforeEach
    void setUp() {
        calculator = new Calculator();
    }

    @Test
    @DisplayName("1 + 1 = 2")
    void addsTwoNumbers() {
        assertEquals(2, calculator.add(1, 1), "1 + 1 should equal 2");
    }

    @ParameterizedTest
    @ValueSource(ints = {1, 2, 3})
    void positive(int value) {
        assertTrue(value > 0);
    }
}
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">CalculatorTest</span> {
    <span class="hljs-keyword">private</span> Calculator calculator;

    <span class="hljs-meta">@BeforeEach</span>
    <span class="hljs-keyword">void</span> <span class="hljs-title function_">setUp</span><span class="hljs-params">()</span> {
        calculator = <span class="hljs-keyword">new</span> <span class="hljs-title class_">Calculator</span>();
    }

    <span class="hljs-meta">@Test</span>
    <span class="hljs-meta">@DisplayName(&quot;1 + 1 = 2&quot;)</span>
    <span class="hljs-keyword">void</span> <span class="hljs-title function_">addsTwoNumbers</span><span class="hljs-params">()</span> {
        assertEquals(<span class="hljs-number">2</span>, calculator.add(<span class="hljs-number">1</span>, <span class="hljs-number">1</span>), <span class="hljs-string">&quot;1 + 1 should equal 2&quot;</span>);
    }

    <span class="hljs-meta">@ParameterizedTest</span>
    <span class="hljs-meta">@ValueSource(ints = {1, 2, 3})</span>
    <span class="hljs-keyword">void</span> <span class="hljs-title function_">positive</span><span class="hljs-params">(<span class="hljs-type">int</span> value)</span> {
        assertTrue(value &gt; <span class="hljs-number">0</span>);
    }
}
""");
    }

    [Fact]
    public void TextBlockEscape()
    {
        AssertHighlighter("java",
""""
String json = """
    {
      "name": "value",
      "escaped": "\t\n"
    }
    """;
String query = """
    SELECT * FROM users
    WHERE id = ?""";
"""",
"""
<span class="hljs-type">String</span> <span class="hljs-variable">json</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;&quot;&quot;
    {
      &quot;name&quot;: &quot;value&quot;,
      &quot;escaped&quot;: &quot;\t\n&quot;
    }
    &quot;&quot;&quot;</span>;
<span class="hljs-type">String</span> <span class="hljs-variable">query</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;&quot;&quot;
    SELECT * FROM users
    WHERE id = ?&quot;&quot;&quot;</span>;
""");
    }

    [Fact]
    public void ThisSuper()
    {
        AssertHighlighter("java",
"""
public Child(String name) {
    super(name);
    this.name = name;
    this.init();
    super.toString();
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-title function_">Child</span><span class="hljs-params">(String name)</span> {
    <span class="hljs-built_in">super</span>(name);
    <span class="hljs-built_in">this</span>.name = name;
    <span class="hljs-built_in">this</span>.init();
    <span class="hljs-built_in">super</span>.toString();
}
""");
    }

    [Fact]
    public void Throws()
    {
        AssertHighlighter("java",
"""
public void read() throws IOException, SQLException {
    throw new IOException("fail");
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">void</span> <span class="hljs-title function_">read</span><span class="hljs-params">()</span> <span class="hljs-keyword">throws</span> IOException, SQLException {
    <span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">IOException</span>(<span class="hljs-string">&quot;fail&quot;</span>);
}
""");
    }

    [Fact]
    public void UnicodeIdent()
    {
        AssertHighlighter("java",
"""
String café = "coffee";
int naïve = 1;
double $price = 2.5;
int _underscore = 3;
""",
"""
<span class="hljs-type">String</span> <span class="hljs-variable">café</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;coffee&quot;</span>;
<span class="hljs-type">int</span> <span class="hljs-variable">naïve</span> <span class="hljs-operator">=</span> <span class="hljs-number">1</span>;
<span class="hljs-type">double</span> <span class="hljs-variable">$price</span> <span class="hljs-operator">=</span> <span class="hljs-number">2.5</span>;
<span class="hljs-type">int</span> <span class="hljs-variable">_underscore</span> <span class="hljs-operator">=</span> <span class="hljs-number">3</span>;
""");
    }

    [Fact]
    public void UnusualSpacing()
    {
        AssertHighlighter("java",
"""
public class A { void m() { int a1b2 = 3; String $x = "y"; } }
List <String> xs = null;
String [] arr() {}
int
count
=
5;
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">void</span> <span class="hljs-title function_">m</span><span class="hljs-params">()</span> { <span class="hljs-type">int</span> <span class="hljs-variable">a1b2</span> <span class="hljs-operator">=</span> <span class="hljs-number">3</span>; <span class="hljs-type">String</span> <span class="hljs-variable">$x</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;y&quot;</span>; } }
List &lt;String&gt; xs = <span class="hljs-literal">null</span>;
String [] <span class="hljs-title function_">arr</span><span class="hljs-params">()</span> {}
<span class="hljs-type">int</span>
<span class="hljs-variable">count</span>
<span class="hljs-operator">=</span>
<span class="hljs-number">5</span>;
""");
    }

    [Fact]
    public void VarLambda()
    {
        AssertHighlighter("java",
"""
var list = new ArrayList<String>();
var map = Map.of("a", 1);
for (var entry : map.entrySet()) {
    System.out.println(entry.getKey());
}
Runnable r = () -> System.out.println("run");
Function<Integer, Integer> square = x -> x * x;
BiFunction<Integer, Integer, Integer> add = (a, b) -> a + b;
Comparator<String> cmp = (String a, String b) -> a.compareTo(b);
list.forEach(System.out::println);
Supplier<List<String>> s = ArrayList::new;
names.stream().filter(n -> n.startsWith("A")).map(String::toUpperCase).toList();
""",
"""
<span class="hljs-type">var</span> <span class="hljs-variable">list</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">ArrayList</span>&lt;String&gt;();
<span class="hljs-type">var</span> <span class="hljs-variable">map</span> <span class="hljs-operator">=</span> Map.of(<span class="hljs-string">&quot;a&quot;</span>, <span class="hljs-number">1</span>);
<span class="hljs-keyword">for</span> (<span class="hljs-keyword">var</span> entry : map.entrySet()) {
    System.out.println(entry.getKey());
}
<span class="hljs-type">Runnable</span> <span class="hljs-variable">r</span> <span class="hljs-operator">=</span> () -&gt; System.out.println(<span class="hljs-string">&quot;run&quot;</span>);
Function&lt;Integer, Integer&gt; square = x -&gt; x * x;
BiFunction&lt;Integer, Integer, Integer&gt; add = (a, b) -&gt; a + b;
Comparator&lt;String&gt; cmp = (String a, String b) -&gt; a.compareTo(b);
list.forEach(System.out::println);
Supplier&lt;List&lt;String&gt;&gt; s = ArrayList::<span class="hljs-keyword">new</span>;
names.stream().filter(n -&gt; n.startsWith(<span class="hljs-string">&quot;A&quot;</span>)).map(String::toUpperCase).toList();
""");
    }

    [Fact]
    public void YieldPermitWords()
    {
        AssertHighlighter("java",
"""
int yield = 5;
String record = "x";
var var = 1;
""",
"""
<span class="hljs-type">int</span> <span class="hljs-variable">yield</span> <span class="hljs-operator">=</span> <span class="hljs-number">5</span>;
<span class="hljs-type">String</span> <span class="hljs-variable">record</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;x&quot;</span>;
<span class="hljs-type">var</span> <span class="hljs-variable">var</span> <span class="hljs-operator">=</span> <span class="hljs-number">1</span>;
""");
    }
}
