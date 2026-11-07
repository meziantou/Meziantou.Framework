namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class DartHighlighterTests
{
    [Fact]
    public void HelloWorld()
    {
        AssertHighlighter("dart",
"""
void main() {
  print('Hello, World!');
}
""",
"""
<span class="hljs-keyword">void</span> main() {
  <span class="hljs-built_in">print</span>(<span class="hljs-string">&#x27;Hello, World!&#x27;</span>);
}
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("dart",
"""
// Line comment
/* Block comment */
/* Nested? /* inner */ outer */
int x = 1; // trailing TODO: fix
/**/
""",
"""
<span class="hljs-comment">// Line comment</span>
<span class="hljs-comment">/* Block comment */</span>
<span class="hljs-comment">/* Nested? /* inner */</span> outer */
<span class="hljs-built_in">int</span> x = <span class="hljs-number">1</span>; <span class="hljs-comment">// trailing <span class="hljs-doctag">TODO:</span> fix</span>
<span class="hljs-comment">/**/</span>
""");
    }

    [Fact]
    public void DocComments()
    {
        AssertHighlighter("dart",
"""
/// Returns the sum of [a] and [b].
///
/// Throws an [ArgumentError] if either is `null`.
/// See also: **bold** and _italic_ text.
int add(int a, int b) => a + b;
""",
"""
<span class="hljs-comment">/// <span class="language-markdown">Returns the sum of [a] and [b].</span></span>
<span class="hljs-comment">///</span>
<span class="hljs-comment">/// <span class="language-markdown">Throws an [ArgumentError] if either is <span class="hljs-code">`null`</span>.</span></span>
<span class="hljs-comment">/// <span class="language-markdown">See also: <span class="hljs-strong">**bold**</span> and <span class="hljs-emphasis">_italic_</span> text.</span></span>
<span class="hljs-built_in">int</span> add(<span class="hljs-built_in">int</span> a, <span class="hljs-built_in">int</span> b) =&gt; a + b;
""");
    }

    [Fact]
    public void BlockDocComment()
    {
        AssertHighlighter("dart",
"""
/**
 * A block doc comment with `code` and [links].
 * TODO: remove this
 */
class Legacy {}
""",
"""
<span class="hljs-comment">/**
 * <span class="language-markdown">A block doc comment with <span class="hljs-code">`code`</span> and [links].</span>
 * <span class="hljs-doctag">TODO:</span><span class="language-markdown"> remove this</span>
 */</span>
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Legacy</span> </span>{}
""");
    }

    [Fact]
    public void Variables()
    {
        AssertHighlighter("dart",
"""
var name = 'Bob';
final int count = 3;
const pi = 3.14159;
late String description;
String? nickname;
int? maybe = null;
dynamic anything = 42;
Object obj = 'text';
""",
"""
<span class="hljs-keyword">var</span> name = <span class="hljs-string">&#x27;Bob&#x27;</span>;
<span class="hljs-keyword">final</span> <span class="hljs-built_in">int</span> count = <span class="hljs-number">3</span>;
<span class="hljs-keyword">const</span> pi = <span class="hljs-number">3.14159</span>;
<span class="hljs-keyword">late</span> <span class="hljs-built_in">String</span> description;
<span class="hljs-built_in">String?</span> nickname;
<span class="hljs-built_in">int?</span> maybe = <span class="hljs-keyword">null</span>;
<span class="hljs-built_in">dynamic</span> anything = <span class="hljs-number">42</span>;
<span class="hljs-built_in">Object</span> obj = <span class="hljs-string">&#x27;text&#x27;</span>;
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("dart",
"""
var a = 42;
var b = -17;
var c = 0xFF;
var d = 0XdeadBEEF;
var e = 3.14;
var f = 1.5e10;
var g = 2E-3;
var h = 1_000_000;
var i = 0xFF_FF;
var j = .5;
var k = 1.;
""",
"""
<span class="hljs-keyword">var</span> a = <span class="hljs-number">42</span>;
<span class="hljs-keyword">var</span> b = -<span class="hljs-number">17</span>;
<span class="hljs-keyword">var</span> c = <span class="hljs-number">0xFF</span>;
<span class="hljs-keyword">var</span> d = <span class="hljs-number">0XdeadBEEF</span>;
<span class="hljs-keyword">var</span> e = <span class="hljs-number">3.14</span>;
<span class="hljs-keyword">var</span> f = <span class="hljs-number">1.5e10</span>;
<span class="hljs-keyword">var</span> g = <span class="hljs-number">2E-3</span>;
<span class="hljs-keyword">var</span> h = <span class="hljs-number">1_000_000</span>;
<span class="hljs-keyword">var</span> i = <span class="hljs-number">0xFF_FF</span>;
<span class="hljs-keyword">var</span> j = .<span class="hljs-number">5</span>;
<span class="hljs-keyword">var</span> k = <span class="hljs-number">1</span>.;
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("dart",
"""
var s1 = 'Single quotes';
var s2 = "Double quotes";
var s3 = 'It\'s escaped \n \t \\ \$ \u{1F600}';
var s4 = 'Interpolated $name and ${name.toUpperCase()}';
var s5 = "Expression: ${a + b} and ${true} ${null} ${this}";
var s6 = 'Adjacent ' 'strings';
""",
"""
<span class="hljs-keyword">var</span> s1 = <span class="hljs-string">&#x27;Single quotes&#x27;</span>;
<span class="hljs-keyword">var</span> s2 = <span class="hljs-string">&quot;Double quotes&quot;</span>;
<span class="hljs-keyword">var</span> s3 = <span class="hljs-string">&#x27;It\&#x27;s escaped \n \t \\ \$ \u{1F600}&#x27;</span>;
<span class="hljs-keyword">var</span> s4 = <span class="hljs-string">&#x27;Interpolated <span class="hljs-subst">$name</span> and <span class="hljs-subst">${name.toUpperCase()}</span>&#x27;</span>;
<span class="hljs-keyword">var</span> s5 = <span class="hljs-string">&quot;Expression: <span class="hljs-subst">${a + b}</span> and <span class="hljs-subst">${<span class="hljs-keyword">true</span>}</span> <span class="hljs-subst">${<span class="hljs-keyword">null</span>}</span> <span class="hljs-subst">${<span class="hljs-keyword">this</span>}</span>&quot;</span>;
<span class="hljs-keyword">var</span> s6 = <span class="hljs-string">&#x27;Adjacent &#x27;</span> <span class="hljs-string">&#x27;strings&#x27;</span>;
""");
    }

    [Fact]
    public void MultilineStrings()
    {
        AssertHighlighter("dart",
""""
var s1 = '''
You can create
multi-line strings like this one with $name.
''';
var s2 = """This is also a
multi-line string ${1 + 2}.""";
"""",
"""
<span class="hljs-keyword">var</span> s1 = <span class="hljs-string">&#x27;&#x27;&#x27;
You can create
multi-line strings like this one with <span class="hljs-subst">$name</span>.
&#x27;&#x27;&#x27;</span>;
<span class="hljs-keyword">var</span> s2 = <span class="hljs-string">&quot;&quot;&quot;This is also a
multi-line string <span class="hljs-subst">${<span class="hljs-number">1</span> + <span class="hljs-number">2</span>}</span>.&quot;&quot;&quot;</span>;
""");
    }

    [Fact]
    public void RawStrings()
    {
        AssertHighlighter("dart",
""""
var r1 = r'In a raw string, not even \n gets special treatment. $name';
var r2 = r"Raw ${double}";
var r3 = r'''
Raw multi-line \n $x
''';
var r4 = r"""raw triple""";
"""",
"""
<span class="hljs-keyword">var</span> r1 = <span class="hljs-string">r&#x27;In a raw string, not even \n gets special treatment. $name&#x27;</span>;
<span class="hljs-keyword">var</span> r2 = <span class="hljs-string">r&quot;Raw ${double}&quot;</span>;
<span class="hljs-keyword">var</span> r3 = <span class="hljs-string">r&#x27;&#x27;&#x27;
Raw multi-line \n $x
&#x27;&#x27;&#x27;</span>;
<span class="hljs-keyword">var</span> r4 = <span class="hljs-string">r&quot;&quot;&quot;raw triple&quot;&quot;&quot;</span>;
""");
    }

    [Fact]
    public void NestedInterpolation()
    {
        AssertHighlighter("dart",
"""
var s = 'Outer ${'inner ${deep} text'} end';
var t = "Map: ${map['key']} and ${list[0]}";
var u = '${items.map((e) => e.name).join(', ')}';
""",
"""
<span class="hljs-keyword">var</span> s = <span class="hljs-string">&#x27;Outer <span class="hljs-subst">${<span class="hljs-string">&#x27;inner <span class="hljs-subst">${deep}</span> text&#x27;</span>}</span> end&#x27;</span>;
<span class="hljs-keyword">var</span> t = <span class="hljs-string">&quot;Map: <span class="hljs-subst">${map[<span class="hljs-string">&#x27;key&#x27;</span>]}</span> and <span class="hljs-subst">${list[<span class="hljs-number">0</span>]}</span>&quot;</span>;
<span class="hljs-keyword">var</span> u = <span class="hljs-string">&#x27;<span class="hljs-subst">${items.map((e) =&gt; e.name).join(<span class="hljs-string">&#x27;, &#x27;</span>)}</span>&#x27;</span>;
""");
    }

    [Fact]
    public void Functions()
    {
        AssertHighlighter("dart",
"""
int fibonacci(int n) {
  if (n == 0 || n == 1) return n;
  return fibonacci(n - 1) + fibonacci(n - 2);
}

void enableFlags({bool? bold, bool hidden = false, required String label}) {}

String say(String from, String msg, [String? device]) => '$from says $msg';

var loudify = (String msg) => '!!! ${msg.toUpperCase()} !!!';
""",
"""
<span class="hljs-built_in">int</span> fibonacci(<span class="hljs-built_in">int</span> n) {
  <span class="hljs-keyword">if</span> (n == <span class="hljs-number">0</span> || n == <span class="hljs-number">1</span>) <span class="hljs-keyword">return</span> n;
  <span class="hljs-keyword">return</span> fibonacci(n - <span class="hljs-number">1</span>) + fibonacci(n - <span class="hljs-number">2</span>);
}

<span class="hljs-keyword">void</span> enableFlags({<span class="hljs-built_in">bool?</span> bold, <span class="hljs-built_in">bool</span> hidden = <span class="hljs-keyword">false</span>, <span class="hljs-keyword">required</span> <span class="hljs-built_in">String</span> label}) {}

<span class="hljs-built_in">String</span> say(<span class="hljs-built_in">String</span> from, <span class="hljs-built_in">String</span> msg, [<span class="hljs-built_in">String?</span> device]) =&gt; <span class="hljs-string">&#x27;<span class="hljs-subst">$from</span> says <span class="hljs-subst">$msg</span>&#x27;</span>;

<span class="hljs-keyword">var</span> loudify = (<span class="hljs-built_in">String</span> msg) =&gt; <span class="hljs-string">&#x27;!!! <span class="hljs-subst">${msg.toUpperCase()}</span> !!!&#x27;</span>;
""");
    }

    [Fact]
    public void Classes()
    {
        AssertHighlighter("dart",
"""
class Point {
  final double x;
  final double y;

  const Point(this.x, this.y);

  Point.origin()
      : x = 0,
        y = 0;

  factory Point.fromJson(Map<String, dynamic> json) {
    return Point(json['x'] as double, json['y'] as double);
  }

  double get magnitude => sqrt(x * x + y * y);

  set magnitude(double value) {}

  @override
  String toString() => 'Point($x, $y)';

  Point operator +(Point other) => Point(x + other.x, y + other.y);
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Point</span> </span>{
  <span class="hljs-keyword">final</span> <span class="hljs-built_in">double</span> x;
  <span class="hljs-keyword">final</span> <span class="hljs-built_in">double</span> y;

  <span class="hljs-keyword">const</span> Point(<span class="hljs-keyword">this</span>.x, <span class="hljs-keyword">this</span>.y);

  Point.origin()
      : x = <span class="hljs-number">0</span>,
        y = <span class="hljs-number">0</span>;

  <span class="hljs-keyword">factory</span> Point.fromJson(<span class="hljs-built_in">Map</span>&lt;<span class="hljs-built_in">String</span>, <span class="hljs-built_in">dynamic</span>&gt; json) {
    <span class="hljs-keyword">return</span> Point(json[<span class="hljs-string">&#x27;x&#x27;</span>] <span class="hljs-keyword">as</span> <span class="hljs-built_in">double</span>, json[<span class="hljs-string">&#x27;y&#x27;</span>] <span class="hljs-keyword">as</span> <span class="hljs-built_in">double</span>);
  }

  <span class="hljs-built_in">double</span> <span class="hljs-keyword">get</span> magnitude =&gt; sqrt(x * x + y * y);

  <span class="hljs-keyword">set</span> magnitude(<span class="hljs-built_in">double</span> value) {}

  <span class="hljs-meta">@override</span>
  <span class="hljs-built_in">String</span> toString() =&gt; <span class="hljs-string">&#x27;Point(<span class="hljs-subst">$x</span>, <span class="hljs-subst">$y</span>)&#x27;</span>;

  Point <span class="hljs-keyword">operator</span> +(Point other) =&gt; Point(x + other.x, y + other.y);
}
""");
    }

    [Fact]
    public void Inheritance()
    {
        AssertHighlighter("dart",
"""
abstract class Shape {
  double get area;
}

class Circle extends Shape implements Comparable<Circle> {
  Circle(this.radius);
  final double radius;
  @override
  double get area => pi * radius * radius;
}

class MyWidget extends StatefulWidget with DiagnosticableTreeMixin {}

class _MyState extends State<MyWidget> with SingleTickerProviderStateMixin {}
""",
"""
<span class="hljs-keyword">abstract</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Shape</span> </span>{
  <span class="hljs-built_in">double</span> <span class="hljs-keyword">get</span> area;
}

<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Circle</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">Shape</span> <span class="hljs-keyword">implements</span> <span class="hljs-title">Comparable</span>&lt;<span class="hljs-title">Circle</span>&gt; </span>{
  Circle(<span class="hljs-keyword">this</span>.radius);
  <span class="hljs-keyword">final</span> <span class="hljs-built_in">double</span> radius;
  <span class="hljs-meta">@override</span>
  <span class="hljs-built_in">double</span> <span class="hljs-keyword">get</span> area =&gt; pi * radius * radius;
}

<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">MyWidget</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">StatefulWidget</span> <span class="hljs-keyword">with</span> <span class="hljs-title">DiagnosticableTreeMixin</span> </span>{}

<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">_MyState</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">State</span>&lt;<span class="hljs-title">MyWidget</span>&gt; <span class="hljs-keyword">with</span> <span class="hljs-title">SingleTickerProviderStateMixin</span> </span>{}
""");
    }

    [Fact]
    public void ClassModifiers()
    {
        AssertHighlighter("dart",
"""
sealed class Shape {}
base class Vehicle {}
final class Car extends Vehicle {}
interface class Flyer {}
abstract interface class Walker {}
mixin class Swimmer {}
abstract base class Animal {}
""",
"""
<span class="hljs-keyword">sealed</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Shape</span> </span>{}
<span class="hljs-keyword">base</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Vehicle</span> </span>{}
<span class="hljs-keyword">final</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Car</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">Vehicle</span> </span>{}
<span class="hljs-class"><span class="hljs-keyword">interface</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Flyer</span> </span>{}
<span class="hljs-keyword">abstract</span> <span class="hljs-class"><span class="hljs-keyword">interface</span> <span class="hljs-keyword">class</span> <span class="hljs-title">Walker</span> </span>{}
<span class="hljs-keyword">mixin</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Swimmer</span> </span>{}
<span class="hljs-keyword">abstract</span> <span class="hljs-keyword">base</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Animal</span> </span>{}
""");
    }

    [Fact]
    public void Mixins()
    {
        AssertHighlighter("dart",
"""
mixin Musical {
  bool canPlayPiano = false;
  void entertainMe() {}
}

mixin Performer on Musician {}

class Maestro extends Person with Musical, Aggressive, Demented {}
""",
"""
<span class="hljs-keyword">mixin</span> Musical {
  <span class="hljs-built_in">bool</span> canPlayPiano = <span class="hljs-keyword">false</span>;
  <span class="hljs-keyword">void</span> entertainMe() {}
}

<span class="hljs-keyword">mixin</span> Performer <span class="hljs-keyword">on</span> Musician {}

<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Maestro</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">Person</span> <span class="hljs-keyword">with</span> <span class="hljs-title">Musical</span>, <span class="hljs-title">Aggressive</span>, <span class="hljs-title">Demented</span> </span>{}
""");
    }

    [Fact]
    public void Enums()
    {
        AssertHighlighter("dart",
"""
enum Color { red, green, blue }

enum Vehicle implements Comparable<Vehicle> {
  car(tires: 4, passengers: 5),
  bicycle(tires: 2, passengers: 1);

  const Vehicle({required this.tires, required this.passengers});

  final int tires;
  final int passengers;

  @override
  int compareTo(Vehicle other) => tires - other.tires;
}
""",
"""
<span class="hljs-keyword">enum</span> Color { red, green, blue }

<span class="hljs-keyword">enum</span> Vehicle <span class="hljs-keyword">implements</span> <span class="hljs-built_in">Comparable</span>&lt;Vehicle&gt; {
  car(tires: <span class="hljs-number">4</span>, passengers: <span class="hljs-number">5</span>),
  bicycle(tires: <span class="hljs-number">2</span>, passengers: <span class="hljs-number">1</span>);

  <span class="hljs-keyword">const</span> Vehicle({<span class="hljs-keyword">required</span> <span class="hljs-keyword">this</span>.tires, <span class="hljs-keyword">required</span> <span class="hljs-keyword">this</span>.passengers});

  <span class="hljs-keyword">final</span> <span class="hljs-built_in">int</span> tires;
  <span class="hljs-keyword">final</span> <span class="hljs-built_in">int</span> passengers;

  <span class="hljs-meta">@override</span>
  <span class="hljs-built_in">int</span> compareTo(Vehicle other) =&gt; tires - other.tires;
}
""");
    }

    [Fact]
    public void Extensions()
    {
        AssertHighlighter("dart",
"""
extension NumberParsing on String {
  int parseInt() => int.parse(this);
}

extension type IdNumber(int id) {
  operator <(IdNumber other) => id < other.id;
}
""",
"""
<span class="hljs-keyword">extension</span> NumberParsing <span class="hljs-keyword">on</span> <span class="hljs-built_in">String</span> {
  <span class="hljs-built_in">int</span> parseInt() =&gt; <span class="hljs-built_in">int</span>.parse(<span class="hljs-keyword">this</span>);
}

<span class="hljs-keyword">extension</span> type IdNumber(<span class="hljs-built_in">int</span> id) {
  <span class="hljs-keyword">operator</span> &lt;(IdNumber other) =&gt; id &lt; other.id;
}
""");
    }

    [Fact]
    public void NullSafety()
    {
        AssertHighlighter("dart",
"""
String? name;
int length = name?.length ?? 0;
name ??= 'default';
String nonNull = name!;
late final String lazy;
List<int?> list = [1, null];
Map<String, int>? map;
var x = obj?.method()?.prop;
""",
"""
<span class="hljs-built_in">String?</span> name;
<span class="hljs-built_in">int</span> length = name?.length ?? <span class="hljs-number">0</span>;
name ??= <span class="hljs-string">&#x27;default&#x27;</span>;
<span class="hljs-built_in">String</span> nonNull = name!;
<span class="hljs-keyword">late</span> <span class="hljs-keyword">final</span> <span class="hljs-built_in">String</span> lazy;
<span class="hljs-built_in">List</span>&lt;<span class="hljs-built_in">int?</span>&gt; list = [<span class="hljs-number">1</span>, <span class="hljs-keyword">null</span>];
<span class="hljs-built_in">Map</span>&lt;<span class="hljs-built_in">String</span>, <span class="hljs-built_in">int</span>&gt;? map;
<span class="hljs-keyword">var</span> x = obj?.method()?.prop;
""");
    }

    [Fact]
    public void Records()
    {
        AssertHighlighter("dart",
"""
(int, String) record = (1, 'a');
({int a, bool b}) named = (a: 1, b: true);
var (x, y) = (1, 2);
final (:name, :age) = person;
(double lat, double lon) location() => (1.0, 2.0);
print(record.$1);
""",
"""
(<span class="hljs-built_in">int</span>, <span class="hljs-built_in">String</span>) record = (<span class="hljs-number">1</span>, <span class="hljs-string">&#x27;a&#x27;</span>);
({<span class="hljs-built_in">int</span> a, <span class="hljs-built_in">bool</span> b}) named = (a: <span class="hljs-number">1</span>, b: <span class="hljs-keyword">true</span>);
<span class="hljs-keyword">var</span> (x, y) = (<span class="hljs-number">1</span>, <span class="hljs-number">2</span>);
<span class="hljs-keyword">final</span> (:name, :age) = person;
(<span class="hljs-built_in">double</span> lat, <span class="hljs-built_in">double</span> lon) location() =&gt; (<span class="hljs-number">1.0</span>, <span class="hljs-number">2.0</span>);
<span class="hljs-built_in">print</span>(record.$<span class="hljs-number">1</span>);
""");
    }

    [Fact]
    public void Patterns()
    {
        AssertHighlighter("dart",
"""
switch (shape) {
  case Square(length: var l):
    return l * l;
  case Circle(:var radius) when radius > 0:
    return pi * radius * radius;
  case [int a, int b, ...var rest]:
    break;
  case {'name': String name}:
    break;
  default:
    break;
}

var area = switch (shape) {
  Square(length: var l) => l * l,
  Circle(radius: var r) => pi * r * r,
  _ => 0,
};

if (json case {'user': [String name, int age]}) {
  print('$name is $age');
}
""",
"""
<span class="hljs-keyword">switch</span> (shape) {
  <span class="hljs-keyword">case</span> Square(length: <span class="hljs-keyword">var</span> l):
    <span class="hljs-keyword">return</span> l * l;
  <span class="hljs-keyword">case</span> Circle(:<span class="hljs-keyword">var</span> radius) <span class="hljs-keyword">when</span> radius &gt; <span class="hljs-number">0</span>:
    <span class="hljs-keyword">return</span> pi * radius * radius;
  <span class="hljs-keyword">case</span> [<span class="hljs-built_in">int</span> a, <span class="hljs-built_in">int</span> b, ...<span class="hljs-keyword">var</span> rest]:
    <span class="hljs-keyword">break</span>;
  <span class="hljs-keyword">case</span> {<span class="hljs-string">&#x27;name&#x27;</span>: <span class="hljs-built_in">String</span> name}:
    <span class="hljs-keyword">break</span>;
  <span class="hljs-keyword">default</span>:
    <span class="hljs-keyword">break</span>;
}

<span class="hljs-keyword">var</span> area = <span class="hljs-keyword">switch</span> (shape) {
  Square(length: <span class="hljs-keyword">var</span> l) =&gt; l * l,
  Circle(radius: <span class="hljs-keyword">var</span> r) =&gt; pi * r * r,
  _ =&gt; <span class="hljs-number">0</span>,
};

<span class="hljs-keyword">if</span> (json <span class="hljs-keyword">case</span> {<span class="hljs-string">&#x27;user&#x27;</span>: [<span class="hljs-built_in">String</span> name, <span class="hljs-built_in">int</span> age]}) {
  <span class="hljs-built_in">print</span>(<span class="hljs-string">&#x27;<span class="hljs-subst">$name</span> is <span class="hljs-subst">$age</span>&#x27;</span>);
}
""");
    }

    [Fact]
    public void Async()
    {
        AssertHighlighter("dart",
"""
Future<void> fetchData() async {
  try {
    final response = await http.get(Uri.parse('https://example.com'));
    print(response.body);
  } on SocketException catch (e) {
    print(e);
  } catch (e, stackTrace) {
    rethrow;
  } finally {
    client.close();
  }
}

Stream<int> countStream(int to) async* {
  for (int i = 1; i <= to; i++) {
    yield i;
  }
}

Iterable<int> naturals(int n) sync* {
  yield* other(n);
}

await for (final value in stream) {}
""",
"""
Future&lt;<span class="hljs-keyword">void</span>&gt; fetchData() <span class="hljs-keyword">async</span> {
  <span class="hljs-keyword">try</span> {
    <span class="hljs-keyword">final</span> response = <span class="hljs-keyword">await</span> http.get(<span class="hljs-built_in">Uri</span>.parse(<span class="hljs-string">&#x27;https://example.com&#x27;</span>));
    <span class="hljs-built_in">print</span>(response.body);
  } <span class="hljs-keyword">on</span> SocketException <span class="hljs-keyword">catch</span> (e) {
    <span class="hljs-built_in">print</span>(e);
  } <span class="hljs-keyword">catch</span> (e, stackTrace) {
    <span class="hljs-keyword">rethrow</span>;
  } <span class="hljs-keyword">finally</span> {
    client.close();
  }
}

Stream&lt;<span class="hljs-built_in">int</span>&gt; countStream(<span class="hljs-built_in">int</span> to) <span class="hljs-keyword">async</span>* {
  <span class="hljs-keyword">for</span> (<span class="hljs-built_in">int</span> i = <span class="hljs-number">1</span>; i &lt;= to; i++) {
    <span class="hljs-keyword">yield</span> i;
  }
}

<span class="hljs-built_in">Iterable</span>&lt;<span class="hljs-built_in">int</span>&gt; naturals(<span class="hljs-built_in">int</span> n) <span class="hljs-keyword">sync</span>* {
  <span class="hljs-keyword">yield</span>* other(n);
}

<span class="hljs-keyword">await</span> <span class="hljs-keyword">for</span> (<span class="hljs-keyword">final</span> value <span class="hljs-keyword">in</span> stream) {}
""");
    }

    [Fact]
    public void Annotations()
    {
        AssertHighlighter("dart",
"""
@override
@deprecated
@Deprecated('Use newMethod instead')
@JsonSerializable(explicitToJson: true)
@pragma('vm:entry-point')
@immutable
class Annotated {}
""",
"""
<span class="hljs-meta">@override</span>
<span class="hljs-meta">@deprecated</span>
<span class="hljs-meta">@Deprecated</span>(<span class="hljs-string">&#x27;Use newMethod instead&#x27;</span>)
<span class="hljs-meta">@JsonSerializable</span>(explicitToJson: <span class="hljs-keyword">true</span>)
<span class="hljs-meta">@pragma</span>(<span class="hljs-string">&#x27;vm:entry-point&#x27;</span>)
<span class="hljs-meta">@immutable</span>
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Annotated</span> </span>{}
""");
    }

    [Fact]
    public void Imports()
    {
        AssertHighlighter("dart",
"""
import 'dart:async';
import 'dart:math' as math;
import 'package:flutter/material.dart';
import 'package:lib/lib.dart' show foo, bar hide baz;
import 'package:big/big.dart' deferred as big;
export 'src/utils.dart';
part 'model.g.dart';
part of 'model.dart';
library my_library;
""",
"""
<span class="hljs-keyword">import</span> <span class="hljs-string">&#x27;dart:async&#x27;</span>;
<span class="hljs-keyword">import</span> <span class="hljs-string">&#x27;dart:math&#x27;</span> <span class="hljs-keyword">as</span> math;
<span class="hljs-keyword">import</span> <span class="hljs-string">&#x27;package:flutter/material.dart&#x27;</span>;
<span class="hljs-keyword">import</span> <span class="hljs-string">&#x27;package:lib/lib.dart&#x27;</span> <span class="hljs-keyword">show</span> foo, bar <span class="hljs-keyword">hide</span> baz;
<span class="hljs-keyword">import</span> <span class="hljs-string">&#x27;package:big/big.dart&#x27;</span> <span class="hljs-keyword">deferred</span> <span class="hljs-keyword">as</span> big;
<span class="hljs-keyword">export</span> <span class="hljs-string">&#x27;src/utils.dart&#x27;</span>;
<span class="hljs-keyword">part</span> <span class="hljs-string">&#x27;model.g.dart&#x27;</span>;
<span class="hljs-keyword">part</span> of <span class="hljs-string">&#x27;model.dart&#x27;</span>;
<span class="hljs-keyword">library</span> my_library;
""");
    }

    [Fact]
    public void Generics()
    {
        AssertHighlighter("dart",
"""
List<String> names = <String>[];
Map<String, List<int>> scores = {};
Set<int> unique = {1, 2, 3};
T first<T>(List<T> items) => items[0];
class Cache<K extends Object, V> {}
typedef Compare<T> = int Function(T a, T b);
typedef IntList = List<int>;
""",
"""
<span class="hljs-built_in">List</span>&lt;<span class="hljs-built_in">String</span>&gt; names = &lt;<span class="hljs-built_in">String</span>&gt;[];
<span class="hljs-built_in">Map</span>&lt;<span class="hljs-built_in">String</span>, <span class="hljs-built_in">List</span>&lt;<span class="hljs-built_in">int</span>&gt;&gt; scores = {};
<span class="hljs-built_in">Set</span>&lt;<span class="hljs-built_in">int</span>&gt; unique = {<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>};
T first&lt;T&gt;(<span class="hljs-built_in">List</span>&lt;T&gt; items) =&gt; items[<span class="hljs-number">0</span>];
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Cache</span>&lt;<span class="hljs-title">K</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">Object</span>, <span class="hljs-title">V</span>&gt; </span>{}
<span class="hljs-keyword">typedef</span> Compare&lt;T&gt; = <span class="hljs-built_in">int</span> <span class="hljs-built_in">Function</span>(T a, T b);
<span class="hljs-keyword">typedef</span> IntList = <span class="hljs-built_in">List</span>&lt;<span class="hljs-built_in">int</span>&gt;;
""");
    }

    [Fact]
    public void Collections()
    {
        AssertHighlighter("dart",
"""
var list = [1, 2, 3];
var spread = [0, ...list, ...?nullableList];
var conditional = [if (promoActive) 'Outlet', for (var i in list) '#$i'];
var map = {'key': 'value', 1: true};
var set = <String>{};
""",
"""
<span class="hljs-keyword">var</span> list = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>];
<span class="hljs-keyword">var</span> spread = [<span class="hljs-number">0</span>, ...list, ...?nullableList];
<span class="hljs-keyword">var</span> conditional = [<span class="hljs-keyword">if</span> (promoActive) <span class="hljs-string">&#x27;Outlet&#x27;</span>, <span class="hljs-keyword">for</span> (<span class="hljs-keyword">var</span> i <span class="hljs-keyword">in</span> list) <span class="hljs-string">&#x27;#<span class="hljs-subst">$i</span>&#x27;</span>];
<span class="hljs-keyword">var</span> map = {<span class="hljs-string">&#x27;key&#x27;</span>: <span class="hljs-string">&#x27;value&#x27;</span>, <span class="hljs-number">1</span>: <span class="hljs-keyword">true</span>};
<span class="hljs-keyword">var</span> <span class="hljs-keyword">set</span> = &lt;<span class="hljs-built_in">String</span>&gt;{};
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("dart",
"""
var a = b ~/ c;
var d = e is String;
var f = g is! int;
var h = i as num;
a += 1; b -= 2; c *= 3; d /= 4; e ~/= 5;
var cascade = Paint()
  ..color = Colors.black
  ..strokeWidth = 5.0;
var nullCascade = obj?..doThis()..doThat();
var bits = a & b | c ^ ~d << 2 >> 1 >>> 3;
""",
"""
<span class="hljs-keyword">var</span> a = b ~/ c;
<span class="hljs-keyword">var</span> d = e <span class="hljs-keyword">is</span> <span class="hljs-built_in">String</span>;
<span class="hljs-keyword">var</span> f = g <span class="hljs-keyword">is</span>! <span class="hljs-built_in">int</span>;
<span class="hljs-keyword">var</span> h = i <span class="hljs-keyword">as</span> <span class="hljs-built_in">num</span>;
a += <span class="hljs-number">1</span>; b -= <span class="hljs-number">2</span>; c *= <span class="hljs-number">3</span>; d /= <span class="hljs-number">4</span>; e ~/= <span class="hljs-number">5</span>;
<span class="hljs-keyword">var</span> cascade = Paint()
  ..color = Colors.black
  ..strokeWidth = <span class="hljs-number">5.0</span>;
<span class="hljs-keyword">var</span> nullCascade = obj?..doThis()..doThat();
<span class="hljs-keyword">var</span> bits = a &amp; b | c ^ ~d &lt;&lt; <span class="hljs-number">2</span> &gt;&gt; <span class="hljs-number">1</span> &gt;&gt;&gt; <span class="hljs-number">3</span>;
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("dart",
"""
for (var i = 0; i < 5; i++) {
  if (i.isEven) continue;
}
for (final item in items) {}
while (!done) {}
do {
  x--;
} while (x > 0);
assert(text != null, 'text must not be null');
outer: for (var i in list) { break outer; }
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-keyword">var</span> i = <span class="hljs-number">0</span>; i &lt; <span class="hljs-number">5</span>; i++) {
  <span class="hljs-keyword">if</span> (i.isEven) <span class="hljs-keyword">continue</span>;
}
<span class="hljs-keyword">for</span> (<span class="hljs-keyword">final</span> item <span class="hljs-keyword">in</span> items) {}
<span class="hljs-keyword">while</span> (!done) {}
<span class="hljs-keyword">do</span> {
  x--;
} <span class="hljs-keyword">while</span> (x &gt; <span class="hljs-number">0</span>);
<span class="hljs-keyword">assert</span>(text != <span class="hljs-keyword">null</span>, <span class="hljs-string">&#x27;text must not be null&#x27;</span>);
outer: <span class="hljs-keyword">for</span> (<span class="hljs-keyword">var</span> i <span class="hljs-keyword">in</span> list) { <span class="hljs-keyword">break</span> outer; }
""");
    }

    [Fact]
    public void FlutterWidget()
    {
        AssertHighlighter("dart",
"""
class Counter extends StatefulWidget {
  const Counter({super.key, required this.title});

  final String title;

  @override
  State<Counter> createState() => _CounterState();
}

class _CounterState extends State<Counter> {
  int _count = 0;

  void _increment() {
    setState(() {
      _count++;
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(widget.title)),
      body: Center(child: Text('Count: $_count')),
      floatingActionButton: FloatingActionButton(
        onPressed: _increment,
        child: const Icon(Icons.add),
      ),
    );
  }
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Counter</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">StatefulWidget</span> </span>{
  <span class="hljs-keyword">const</span> Counter({<span class="hljs-keyword">super</span>.key, <span class="hljs-keyword">required</span> <span class="hljs-keyword">this</span>.title});

  <span class="hljs-keyword">final</span> <span class="hljs-built_in">String</span> title;

  <span class="hljs-meta">@override</span>
  State&lt;Counter&gt; createState() =&gt; _CounterState();
}

<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">_CounterState</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">State</span>&lt;<span class="hljs-title">Counter</span>&gt; </span>{
  <span class="hljs-built_in">int</span> _count = <span class="hljs-number">0</span>;

  <span class="hljs-keyword">void</span> _increment() {
    setState(() {
      _count++;
    });
  }

  <span class="hljs-meta">@override</span>
  Widget build(BuildContext context) {
    <span class="hljs-keyword">return</span> Scaffold(
      appBar: AppBar(title: Text(widget.title)),
      body: Center(child: Text(<span class="hljs-string">&#x27;Count: <span class="hljs-subst">$_count</span>&#x27;</span>)),
      floatingActionButton: FloatingActionButton(
        onPressed: _increment,
        child: <span class="hljs-keyword">const</span> Icon(Icons.add),
      ),
    );
  }
}
""");
    }

    [Fact]
    public void Builtins()
    {
        AssertHighlighter("dart",
"""
Duration d = Duration(seconds: 5);
DateTime now = DateTime.now();
Iterable<int> it = [];
Iterator<int> iter;
StringBuffer sb = StringBuffer();
RegExp re = RegExp(r'\d+');
Symbol sym = #foo;
Type t = int;
Uri uri = Uri.parse('x');
Never fail() => throw Exception();
Null nothing;
num n = 1;
Function fn = () {};
window.alert('x');
document.querySelector('#id');
""",
"""
<span class="hljs-built_in">Duration</span> d = <span class="hljs-built_in">Duration</span>(seconds: <span class="hljs-number">5</span>);
<span class="hljs-built_in">DateTime</span> now = <span class="hljs-built_in">DateTime</span>.now();
<span class="hljs-built_in">Iterable</span>&lt;<span class="hljs-built_in">int</span>&gt; it = [];
<span class="hljs-built_in">Iterator</span>&lt;<span class="hljs-built_in">int</span>&gt; iter;
<span class="hljs-built_in">StringBuffer</span> sb = <span class="hljs-built_in">StringBuffer</span>();
<span class="hljs-built_in">RegExp</span> re = <span class="hljs-built_in">RegExp</span>(<span class="hljs-string">r&#x27;\d+&#x27;</span>);
<span class="hljs-built_in">Symbol</span> sym = #foo;
<span class="hljs-built_in">Type</span> t = <span class="hljs-built_in">int</span>;
<span class="hljs-built_in">Uri</span> uri = <span class="hljs-built_in">Uri</span>.parse(<span class="hljs-string">&#x27;x&#x27;</span>);
<span class="hljs-built_in">Never</span> fail() =&gt; <span class="hljs-keyword">throw</span> Exception();
<span class="hljs-built_in">Null</span> nothing;
<span class="hljs-built_in">num</span> n = <span class="hljs-number">1</span>;
<span class="hljs-built_in">Function</span> fn = () {};
<span class="hljs-built_in">window</span>.alert(<span class="hljs-string">&#x27;x&#x27;</span>);
<span class="hljs-built_in">document</span>.<span class="hljs-built_in">querySelector</span>(<span class="hljs-string">&#x27;#id&#x27;</span>);
""");
    }

    [Fact]
    public void KeywordsMisc()
    {
        AssertHighlighter("dart",
"""
covariant external static
var x = const [1, 2];
new Foo();
Object.hash(a, b);
external void nativeMethod();
""",
"""
<span class="hljs-keyword">covariant</span> <span class="hljs-keyword">external</span> <span class="hljs-keyword">static</span>
<span class="hljs-keyword">var</span> x = <span class="hljs-keyword">const</span> [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>];
<span class="hljs-keyword">new</span> Foo();
<span class="hljs-built_in">Object</span>.hash(a, b);
<span class="hljs-keyword">external</span> <span class="hljs-keyword">void</span> nativeMethod();
""");
    }

    [Fact]
    public void PrivateNamesEndingWithAKeyword()
    {
        AssertHighlighter("dart",
"""
var _in = 1;
void _show() {}
bool _isNew = true;
var _this = this;
final _print = print;
""",
"""
<span class="hljs-keyword">var</span> _in = <span class="hljs-number">1</span>;
<span class="hljs-keyword">void</span> _show() {}
<span class="hljs-built_in">bool</span> _isNew = <span class="hljs-keyword">true</span>;
<span class="hljs-keyword">var</span> _this = <span class="hljs-keyword">this</span>;
<span class="hljs-keyword">final</span> _print = <span class="hljs-built_in">print</span>;
""");
    }

    [Fact]
    public void ClassEdgeCases()
    {
        AssertHighlighter("dart",
"""
class Foo<T extends Comparable<T>> extends Bar<T> implements Baz {}
class A{}
class B extends C {
}
interface
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Foo</span>&lt;<span class="hljs-title">T</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">Comparable</span>&lt;<span class="hljs-title">T</span>&gt;&gt; <span class="hljs-keyword">extends</span> <span class="hljs-title">Bar</span>&lt;<span class="hljs-title">T</span>&gt; <span class="hljs-keyword">implements</span> <span class="hljs-title">Baz</span> </span>{}
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">A</span></span>{}
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">B</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">C</span> </span>{
}
<span class="hljs-class"><span class="hljs-keyword">interface</span></span>
""");
    }

    [Fact]
    public void LambdaArrow()
    {
        AssertHighlighter("dart",
"""
var f = (x) => x * 2;
list.forEach((item) => print(item));
""",
"""
<span class="hljs-keyword">var</span> f = (x) =&gt; x * <span class="hljs-number">2</span>;
list.forEach((item) =&gt; <span class="hljs-built_in">print</span>(item));
""");
    }

    [Fact]
    public void StringEdgeCases()
    {
        AssertHighlighter("dart",
"""
var a = '';
var b = "";
var c = '$';
var d = 'price: \$5';
var e = 'unterminated
var f = 1;
var g = "${}";
""",
"""
<span class="hljs-keyword">var</span> a = <span class="hljs-string">&#x27;&#x27;</span>;
<span class="hljs-keyword">var</span> b = <span class="hljs-string">&quot;&quot;</span>;
<span class="hljs-keyword">var</span> c = <span class="hljs-string">&#x27;$&#x27;</span>;
<span class="hljs-keyword">var</span> d = <span class="hljs-string">&#x27;price: \$5&#x27;</span>;
<span class="hljs-keyword">var</span> e = <span class="hljs-string">&#x27;unterminated
var f = 1;
var g = &quot;<span class="hljs-subst">${}</span>&quot;;</span>
""");
    }

    [Fact]
    public void NullableTypes()
    {
        AssertHighlighter("dart",
"""
List<String?>? items;
Map<String?, int?> m;
String? get name => _name;
""",
"""
<span class="hljs-built_in">List</span>&lt;<span class="hljs-built_in">String?</span>&gt;? items;
<span class="hljs-built_in">Map</span>&lt;<span class="hljs-built_in">String?</span>, <span class="hljs-built_in">int?</span>&gt; m;
<span class="hljs-built_in">String?</span> <span class="hljs-keyword">get</span> name =&gt; _name;
""");
    }

    [Fact]
    public void GettersSetters()
    {
        AssertHighlighter("dart",
"""
class Temp {
  double _celsius = 0;
  double get fahrenheit => _celsius * 9 / 5 + 32;
  set fahrenheit(double f) => _celsius = (f - 32) * 5 / 9;
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Temp</span> </span>{
  <span class="hljs-built_in">double</span> _celsius = <span class="hljs-number">0</span>;
  <span class="hljs-built_in">double</span> <span class="hljs-keyword">get</span> fahrenheit =&gt; _celsius * <span class="hljs-number">9</span> / <span class="hljs-number">5</span> + <span class="hljs-number">32</span>;
  <span class="hljs-keyword">set</span> fahrenheit(<span class="hljs-built_in">double</span> f) =&gt; _celsius = (f - <span class="hljs-number">32</span>) * <span class="hljs-number">5</span> / <span class="hljs-number">9</span>;
}
""");
    }

    [Fact]
    public void MemberNamedLikeAKeyword()
    {
        AssertHighlighter("dart",
"""
final response = await http.get(Uri.parse(url));
dialog.show();
overlay.hide();
paint..set = 1;
obj?.get(key);
var list = [...var, ...other];
this.x = super.y;
""",
"""
<span class="hljs-keyword">final</span> response = <span class="hljs-keyword">await</span> http.get(<span class="hljs-built_in">Uri</span>.parse(url));
dialog.show();
overlay.hide();
paint..set = <span class="hljs-number">1</span>;
obj?.get(key);
<span class="hljs-keyword">var</span> list = [...<span class="hljs-keyword">var</span>, ...other];
<span class="hljs-keyword">this</span>.x = <span class="hljs-keyword">super</span>.y;
""");
    }

    [Fact]
    public void BlockDocComment_SingleLine()
    {
        AssertHighlighter("dart",
"""
/** A one-line doc with `code`. */
int x = 0;
/***/
""",
"""
<span class="hljs-comment">/** <span class="language-markdown">A one-line doc with <span class="hljs-code">`code`</span>. </span>*/</span>
<span class="hljs-built_in">int</span> x = <span class="hljs-number">0</span>;
<span class="hljs-comment">/***/</span>
""");
    }

    [Fact]
    public void BlockDocComment_IndentedCodeBlockLastsUntilTheEnd()
    {
        AssertHighlighter("dart",
"""
/**
 * Example:
 *
 *     var x = compute();
 *
 * - first item
 * - second **bold** item
 */
void compute() {}
""",
"""
<span class="hljs-comment">/**
 * <span class="language-markdown">Example:</span>
 *
 * <span class="language-markdown"><span class="hljs-code">    var x = compute();</span></span>
 *
 * <span class="language-markdown"><span class="hljs-code">- first item</span></span>
 * <span class="language-markdown"><span class="hljs-code">- second **bold** item</span></span>
 */</span>
<span class="hljs-keyword">void</span> compute() {}
""");
    }

    [Fact]
    public void DocComments_IndentedCodeBlockLastsUntilTheEnd()
    {
        AssertHighlighter("dart",
"""
/// Example:
///
///     var x = compute();
///
/// - first item
/// - second **bold** item
void compute() {}
""",
"""
<span class="hljs-comment">/// <span class="language-markdown">Example:</span></span>
<span class="hljs-comment">///</span>
<span class="hljs-comment">/// <span class="language-markdown"><span class="hljs-code">    var x = compute();</span></span></span>
<span class="hljs-comment">///</span>
<span class="hljs-comment">/// <span class="language-markdown"><span class="hljs-code">- first item</span></span></span>
<span class="hljs-comment">/// <span class="language-markdown"><span class="hljs-code">- second **bold** item</span></span></span>
<span class="hljs-keyword">void</span> compute() {}
""");
    }

    [Fact]
    public void DocComments_FencedCodeBlock()
    {
        AssertHighlighter("dart",
"""
/// Computes a value.
///
/// ```dart
/// var y = compute();
/// ```
///
/// - first item with `code`
/// - second **bold** item
void compute() {}
""",
"""
<span class="hljs-comment">/// <span class="language-markdown">Computes a value.</span></span>
<span class="hljs-comment">///</span>
<span class="hljs-comment">/// <span class="language-markdown"><span class="hljs-code">```dart</span></span></span>
<span class="hljs-comment">/// <span class="language-markdown"><span class="hljs-code">var y = compute();</span></span></span>
<span class="hljs-comment">/// <span class="language-markdown"><span class="hljs-code">```</span></span></span>
<span class="hljs-comment">///</span>
<span class="hljs-comment">/// <span class="language-markdown"><span class="hljs-bullet">-</span> first item with <span class="hljs-code">`code`</span></span></span>
<span class="hljs-comment">/// <span class="language-markdown"><span class="hljs-bullet">-</span> second <span class="hljs-strong">**bold**</span> item</span></span>
<span class="hljs-keyword">void</span> compute() {}
""");
    }

    [Fact]
    public void BlockDocComment_FencedCodeBlock()
    {
        AssertHighlighter("dart",
"""
/**
 * Computes a value.
 *
 * ```dart
 * var y = compute();
 * ```
 *
 * - first item with `code`
 * - second **bold** item
 */
void compute() {}
""",
"""
<span class="hljs-comment">/**
 * <span class="language-markdown">Computes a value.</span>
 *
 * <span class="language-markdown"><span class="hljs-code">```dart</span></span>
 * <span class="language-markdown"><span class="hljs-code">var y = compute();</span></span>
 * <span class="language-markdown"><span class="hljs-code">```</span></span>
 *
 * <span class="language-markdown"><span class="hljs-bullet">-</span> first item with <span class="hljs-code">`code`</span></span>
 * <span class="language-markdown"><span class="hljs-bullet">-</span> second <span class="hljs-strong">**bold**</span> item</span>
 */</span>
<span class="hljs-keyword">void</span> compute() {}
""");
    }
}
