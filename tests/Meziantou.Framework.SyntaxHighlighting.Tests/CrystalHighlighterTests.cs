namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class CrystalHighlighterTests
{
    [Fact]
    public void Alias()
    {
        AssertHighlighter("cr",
"""
puts "cr alias"
""",
"""
puts <span class="hljs-string">&quot;cr alias&quot;</span>
""");
    }

    [Fact]
    public void Blocks()
    {
        AssertHighlighter("crystal",
"""
[1, 2, 3].each do |x|
  puts x
end
arr.map { |x| x * 2 }.select(&.even?)
spawn do
  ch.send(1)
end
select
when v = ch.receive
  puts v
end
begin
  raise "oops"
rescue ex : Exception
  puts ex.message
ensure
  cleanup
end
""",
"""
[<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>].each <span class="hljs-keyword">do</span> |x|
  puts x
<span class="hljs-keyword">end</span>
arr.map { |x| x * <span class="hljs-number">2</span> }.<span class="hljs-keyword">select</span>(&amp;.even?)
spawn <span class="hljs-keyword">do</span>
  ch.send(<span class="hljs-number">1</span>)
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">select</span>
<span class="hljs-keyword">when</span> v = ch.receive
  puts v
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">begin</span>
  raise <span class="hljs-string">&quot;oops&quot;</span>
<span class="hljs-keyword">rescue</span> ex : Exception
  puts ex.message
<span class="hljs-keyword">ensure</span>
  cleanup
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Class()
    {
        AssertHighlighter("crystal",
"""
require "json"

# A person
class Person
  include JSON::Serializable

  property name : String
  getter age : Int32 = 0

  def initialize(@name : String, @age : Int32)
  end

  def greet(other : Person) : String
    "Hello #{other.name}, I'm #{@name}"
  end

  def self.create(name) : self
    new(name, 42)
  end

  def adult?
    @age >= 18
  end
end

struct Point
  getter x : Float64, y : Float64
end

class Admin < Person
end
""",
"""
<span class="hljs-keyword">require</span> <span class="hljs-string">&quot;json&quot;</span>

<span class="hljs-comment"># A person</span>
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Person</span></span>
  <span class="hljs-keyword">include</span> JSON::Serializable

  property name : String
  getter age : Int32 = <span class="hljs-number">0</span>

  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">initialize</span></span>(<span class="hljs-variable">@name</span> : String, <span class="hljs-variable">@age</span> : Int32)
  <span class="hljs-keyword">end</span>

  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">greet</span></span>(other : Person) : String
    <span class="hljs-string">&quot;Hello <span class="hljs-subst">#{other.name}</span>, I&#x27;m <span class="hljs-subst">#{<span class="hljs-variable">@name</span>}</span>&quot;</span>
  <span class="hljs-keyword">end</span>

  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">self</span></span>.create(name) : <span class="hljs-keyword">self</span>
    new(name, <span class="hljs-number">42</span>)
  <span class="hljs-keyword">end</span>

  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">adult?</span></span>
    <span class="hljs-variable">@age</span> &gt;= <span class="hljs-number">18</span>
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>

<span class="hljs-class"><span class="hljs-keyword">struct</span> <span class="hljs-title">Point</span></span>
  getter x : Float64, y : Float64
<span class="hljs-keyword">end</span>

<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Admin</span> &lt; <span class="hljs-title">Person</span></span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("crystal",
"""
# comment
x = 1 # trailing
# TODO: fix this
""",
"""
<span class="hljs-comment"># comment</span>
x = <span class="hljs-number">1</span> <span class="hljs-comment"># trailing</span>
<span class="hljs-comment"># <span class="hljs-doctag">TODO:</span> fix this</span>
""");
    }

    [Fact]
    public void Edge()
    {
        AssertHighlighter("crystal",
"""
str = "unterminated
next_line = 1
def
x = %(unterminated
""",
"""
str = <span class="hljs-string">&quot;unterminated
next_line = 1
def
x = %(unterminated</span>
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("crystal", "", "");
    }

    [Fact]
    public void Hello()
    {
        AssertHighlighter("crystal",
"""
puts "Hello, World!"
name = gets.try &.chomp
puts "Hi, #{name}!"
""",
"""
puts <span class="hljs-string">&quot;Hello, World!&quot;</span>
name = gets.try &amp;.chomp
puts <span class="hljs-string">&quot;Hi, <span class="hljs-subst">#{name}</span>!&quot;</span>
""");
    }

    [Fact]
    public void Heredoc()
    {
        AssertHighlighter("crystal",
"""
a = <<-EOS
  word
  more words here
  EOS
b = 2
""",
"""
a = <span class="hljs-string">&lt;&lt;-EOS
  word
  more words here
  EOS</span>
b = <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Interp()
    {
        AssertHighlighter("crystal",
"""
puts "sum: #{items.map { |i| i.price }.sum}"
puts "nested: #{"inner #{deep}"}"
puts "hash: #{h[:key]}"
""",
"""
puts <span class="hljs-string">&quot;sum: <span class="hljs-subst">#{items.map { |i| i.price }.sum}</span>&quot;</span>
puts <span class="hljs-string">&quot;nested: <span class="hljs-subst">#{<span class="hljs-string">&quot;inner <span class="hljs-subst">#{deep}</span>&quot;</span>}</span>&quot;</span>
puts <span class="hljs-string">&quot;hash: <span class="hljs-subst">#{h[<span class="hljs-symbol">:key</span>]}</span>&quot;</span>
""");
    }

    [Fact]
    public void Lib()
    {
        AssertHighlighter("crystal",
"""
@[Link("m")]
lib LibM
  fun pow(x : Float64, y : Float64) : Float64
end

enum Color
  Red
  Green
end

union MyUnion
  x : Int32
end

annotation MyAnnotation
end

@[JSON::Field(key: "full_name")]
property name : String
""",
"""
<span class="hljs-meta">@[Link(<span class="hljs-string">&quot;m&quot;</span>)]</span>
<span class="hljs-class"><span class="hljs-keyword">lib</span> <span class="hljs-title">LibM</span></span>
  <span class="hljs-function"><span class="hljs-keyword">fun</span> <span class="hljs-title">pow</span></span>(x : Float64, y : Float64) : Float64
<span class="hljs-keyword">end</span>

<span class="hljs-class"><span class="hljs-keyword">enum</span> <span class="hljs-title">Color</span></span>
  Red
  Green
<span class="hljs-keyword">end</span>

<span class="hljs-class"><span class="hljs-keyword">union</span> <span class="hljs-title">MyUnion</span></span>
  x : Int32
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">annotation</span> <span class="hljs-title">MyAnnotation</span>
<span class="hljs-keyword">end</span>

<span class="hljs-meta">@[JSON::Field(key: <span class="hljs-string">&quot;full_name&quot;</span>)]</span>
property name : String
""");
    }

    [Fact]
    public void Macros()
    {
        AssertHighlighter("crystal",
"""
macro define_method(name, content)
  def {{name.id}}
    {{content}}
  end
end

{% for name in %w(foo bar) %}
  def {{name.id}}; end
{% end %}

{% if flag?(:linux) %}
  puts "linux"
{% else %}
  puts "other"
{% end %}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">macro</span> <span class="hljs-title">define_method</span></span>(name, content)
  <span class="hljs-function"><span class="hljs-keyword">def</span> {{<span class="hljs-title">name</span></span>.id}}
    <span class="hljs-template-variable">{{content}}</span>
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>

<span class="hljs-template-variable">{% <span class="hljs-keyword">for</span> name in <span class="hljs-string">%w(foo bar)</span> %}</span>
  <span class="hljs-function"><span class="hljs-keyword">def</span> {{<span class="hljs-title">name</span></span>.id}}; <span class="hljs-keyword">end</span>
<span class="hljs-template-variable">{% <span class="hljs-keyword">end</span> %}</span>

<span class="hljs-template-variable">{% <span class="hljs-keyword">if</span> flag?(<span class="hljs-symbol">:linux</span>) %}</span>
  puts <span class="hljs-string">&quot;linux&quot;</span>
<span class="hljs-template-variable">{% <span class="hljs-keyword">else</span> %}</span>
  puts <span class="hljs-string">&quot;other&quot;</span>
<span class="hljs-template-variable">{% <span class="hljs-keyword">end</span> %}</span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("crystal",
"""
a = 1_000_000
b = 0x1F_u8
c = 0b1010_i64
d = 0o755
e = 1.5e10_f32
f = 3.14
g = 12_i128
h = 1e-3
i = 0
j = 1..10
k = 100_u8
""",
"""
a = <span class="hljs-number">1_000_000</span>
b = <span class="hljs-number">0x1F_u8</span>
c = <span class="hljs-number">0b1010_i64</span>
d = <span class="hljs-number">0o755</span>
e = <span class="hljs-number">1.5e10_f32</span>
f = <span class="hljs-number">3.14</span>
g = <span class="hljs-number">12_i128</span>
h = <span class="hljs-number">1e-3</span>
i = <span class="hljs-number">0</span>
j = <span class="hljs-number">1</span>..<span class="hljs-number">10</span>
k = <span class="hljs-number">100_u8</span>
""");
    }

    [Fact]
    public void Regex()
    {
        AssertHighlighter("crystal",
"""
if line =~ /^\d+ (\w+)$/i
  puts $1
end
r = %r{https?://[^/]+}
s = %r(a(b)c)
x = a / b / c
case str
when /foo/ then 1
end
""",
"""
<span class="hljs-keyword">if</span> line =~ <span class="hljs-regexp">/^\d+ (\w+)$/i</span>
  puts <span class="hljs-variable">$1</span>
<span class="hljs-keyword">end</span>
r = <span class="hljs-regexp">%r{https?://[^/]+}</span>
s = <span class="hljs-regexp">%r(a(b)c)</span>
x = a / b / c
<span class="hljs-keyword">case</span> str
<span class="hljs-keyword">when</span> <span class="hljs-regexp">/foo/</span> <span class="hljs-keyword">then</span> <span class="hljs-number">1</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("crystal",
"""
a = "tab\t #{1 + 2} end"
b = 'c'
c = `ls -la`
d = %(parens (nested) string)
e = %w[one two three]
f = %q{no #{interp} here}
g = %i<sym1 sym2>
h = %Q|pipes #{x}|
text = <<-EOS
  hello
  #{name}
  EOS
raw = <<-'RAW'
  no #{interp}
  RAW
after = 1
""",
"""
a = <span class="hljs-string">&quot;tab\t <span class="hljs-subst">#{<span class="hljs-number">1</span> + <span class="hljs-number">2</span>}</span> end&quot;</span>
b = <span class="hljs-string">&#x27;c&#x27;</span>
c = <span class="hljs-string">`ls -la`</span>
d = <span class="hljs-string">%(parens (nested) string)</span>
e = <span class="hljs-string">%w[one two three]</span>
f = <span class="hljs-string">%q{no #{interp} here}</span>
g = <span class="hljs-string">%i&lt;sym1 sym2&gt;</span>
h = <span class="hljs-string">%Q|pipes <span class="hljs-subst">#{x}</span>|</span>
text = <span class="hljs-string">&lt;&lt;-EOS
  hello
  <span class="hljs-subst">#{name}</span>
  EOS</span>
raw = <span class="hljs-string">&lt;&lt;-&#x27;RAW&#x27;
  no #{interp}
  RAW</span>
after = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Symbols()
    {
        AssertHighlighter("crystal",
"""
h = {:a => 1, "b" => 2, c: 3}
t = {name: "x", age: 1}
s = :"quoted symbol"
op = :+
q = :valid?
n = Foo::Bar::Baz.new
v = x ? 1 : 2
def foo(x : Int32, y : String? = nil) : Nil
end
m = ::Top::Level
""",
"""
h = {<span class="hljs-symbol">:a</span> =&gt; <span class="hljs-number">1</span>, <span class="hljs-string">&quot;b&quot;</span> =&gt; <span class="hljs-number">2</span>, <span class="hljs-symbol">c:</span> <span class="hljs-number">3</span>}
t = {<span class="hljs-symbol">name:</span> <span class="hljs-string">&quot;x&quot;</span>, <span class="hljs-symbol">age:</span> <span class="hljs-number">1</span>}
s = <span class="hljs-symbol">:<span class="hljs-string">&quot;quoted symbol&quot;</span></span>
op = <span class="hljs-symbol">:+</span>
q = <span class="hljs-symbol">:valid?</span>
n = Foo::Bar::Baz.new
v = x ? <span class="hljs-number">1</span> : <span class="hljs-number">2</span>
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">foo</span></span>(x : Int32, y : String? = <span class="hljs-literal">nil</span>) : Nil
<span class="hljs-keyword">end</span>
m = ::Top::Level
""");
    }

    [Fact]
    public void Variables()
    {
        AssertHighlighter("crystal",
"""
@@count = 0
@name = "x"
$global = 1
$~
__FILE__
__LINE__
x = y.as(String)
z = y.is_a?(Int32)
w = y.nil?
v = y.responds_to?(:size)
""",
"""
<span class="hljs-variable">@@count</span> = <span class="hljs-number">0</span>
<span class="hljs-variable">@name</span> = <span class="hljs-string">&quot;x&quot;</span>
<span class="hljs-variable">$global</span> = <span class="hljs-number">1</span>
<span class="hljs-variable">$~</span>
<span class="hljs-keyword">__FILE__</span>
<span class="hljs-keyword">__LINE__</span>
x = y.<span class="hljs-keyword">as</span>(String)
z = y.is_a?(Int32)
w = y.<span class="hljs-literal">nil</span>?
v = y.responds_to?(<span class="hljs-symbol">:size</span>)
""");
    }
}
