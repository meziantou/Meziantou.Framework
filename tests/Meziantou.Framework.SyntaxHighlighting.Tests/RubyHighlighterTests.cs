namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class RubyHighlighterTests
{
    [Fact]
    public void Hello()
    {
        AssertHighlighter("ruby",
"""
#!/usr/bin/env ruby
# frozen_string_literal: true

puts "Hello, world!"
""",
"""
<span class="hljs-meta">#!/usr/bin/env ruby</span>
<span class="hljs-comment"># frozen_string_literal: true</span>

puts <span class="hljs-string">&quot;Hello, world!&quot;</span>
""");
    }

    [Fact]
    public void ClassDef()
    {
        AssertHighlighter("ruby",
"""
class Animal
  attr_reader :name, :age

  def initialize(name, age = 0)
    @name = name
    @age = age
  end

  def to_s
    "#{name} (#{age})"
  end
end
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Animal</span>
  <span class="hljs-built_in">attr_reader</span> <span class="hljs-symbol">:name</span>, <span class="hljs-symbol">:age</span>

  <span class="hljs-keyword">def</span> <span class="hljs-title function_">initialize</span>(<span class="hljs-params">name, age = <span class="hljs-number">0</span></span>)
    <span class="hljs-variable">@name</span> = name
    <span class="hljs-variable">@age</span> = age
  <span class="hljs-keyword">end</span>

  <span class="hljs-keyword">def</span> <span class="hljs-title function_">to_s</span>
    <span class="hljs-string">&quot;<span class="hljs-subst">#{name}</span> (<span class="hljs-subst">#{age}</span>)&quot;</span>
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Inheritance()
    {
        AssertHighlighter("ruby",
"""
class Dog < Animal
  include Comparable
  extend Forwardable
  prepend Logging

  def <=>(other)
    age <=> other.age
  end
end

class Admin::User < ApplicationRecord
end

module Foo::Bar
end
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Dog</span> &lt; <span class="hljs-title class_ inherited__">Animal</span>
  <span class="hljs-keyword">include</span> <span class="hljs-title class_">Comparable</span>
  <span class="hljs-keyword">extend</span> <span class="hljs-title class_">Forwardable</span>
  <span class="hljs-keyword">prepend</span> <span class="hljs-title class_">Logging</span>

  <span class="hljs-keyword">def</span> <span class="hljs-title function_">&lt;=&gt;</span>(<span class="hljs-params">other</span>)
    age &lt;=&gt; other.age
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">class</span> <span class="hljs-title class_">Admin::User</span> &lt; <span class="hljs-title class_ inherited__">ApplicationRecord</span>
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">module</span> <span class="hljs-title class_">Foo::Bar</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Singleton()
    {
        AssertHighlighter("ruby",
"""
class << self
  def create(*args, **opts, &block)
    new(*args, **opts, &block)
  end
end

def self.build
  new
end
""",
"""
<span class="hljs-keyword">class</span> &lt;&lt; <span class="hljs-variable language_">self</span>
  <span class="hljs-keyword">def</span> <span class="hljs-title function_">create</span>(<span class="hljs-params">*args, **opts, &amp;block</span>)
    new(*args, **opts, &amp;block)
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">def</span> <span class="hljs-variable language_">self</span>.<span class="hljs-title function_">build</span>
  new
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void MethodNames()
    {
        AssertHighlighter("ruby",
"""
def valid?
  !@errors.any?
end

def save!
  raise "invalid" unless valid?
end

def name=(value)
  @name = value
end

def [](key)
  @data[key]
end

def +(other)
  self.class.new(x + other.x)
end

def ==(other)
  other.is_a?(self.class)
end
""",
"""
<span class="hljs-keyword">def</span> <span class="hljs-title function_">valid?</span>
  !<span class="hljs-variable">@errors</span>.any?
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">def</span> <span class="hljs-title function_">save!</span>
  <span class="hljs-keyword">raise</span> <span class="hljs-string">&quot;invalid&quot;</span> <span class="hljs-keyword">unless</span> valid?
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">def</span> <span class="hljs-title function_">name=</span>(<span class="hljs-params">value</span>)
  <span class="hljs-variable">@name</span> = value
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">def</span> <span class="hljs-title function_">[]</span>(<span class="hljs-params">key</span>)
  <span class="hljs-variable">@data</span>[key]
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">def</span> <span class="hljs-title function_">+</span>(<span class="hljs-params">other</span>)
  <span class="hljs-variable language_">self</span>.class.new(x + other.x)
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">def</span> <span class="hljs-title function_">==</span>(<span class="hljs-params">other</span>)
  other.is_a?(<span class="hljs-variable language_">self</span>.class)
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void EmptyParams()
    {
        AssertHighlighter("ruby",
"""
def foo()
  bar()
end
""",
"""
<span class="hljs-keyword">def</span> <span class="hljs-title function_">foo</span>()
  bar()
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("ruby",
"""
a = 'single #{not} \' quote'
b = "double #{interp} \n \" quote"
c = `ls -la #{dir}`
d = "nested #{"inner #{deep}"} done"
""",
"""
a = <span class="hljs-string">&#x27;single #{not} \&#x27; quote&#x27;</span>
b = <span class="hljs-string">&quot;double <span class="hljs-subst">#{interp}</span> \n \&quot; quote&quot;</span>
c = <span class="hljs-string">`ls -la <span class="hljs-subst">#{dir}</span>`</span>
d = <span class="hljs-string">&quot;nested <span class="hljs-subst">#{<span class="hljs-string">&quot;inner <span class="hljs-subst">#{deep}</span>&quot;</span>}</span> done&quot;</span>
""");
    }

    [Fact]
    public void PercentLiterals()
    {
        AssertHighlighter("ruby",
"""
words = %w[apple banana cherry]
symbols = %i[foo bar baz]
single = %q(it's a 'quote')
double = %Q{Hello #{name}}
angle = %w<a b c>
slash = %q/path/
cmd = %x(echo hi)
bare = %(plain #{x})
pipe = %w|a b|
""",
"""
words = <span class="hljs-string">%w[apple banana cherry]</span>
symbols = <span class="hljs-string">%i[foo bar baz]</span>
single = <span class="hljs-string">%q(it&#x27;s a &#x27;quote&#x27;)</span>
double = <span class="hljs-string">%Q{Hello <span class="hljs-subst">#{name}</span>}</span>
angle = <span class="hljs-string">%w&lt;a b c&gt;</span>
slash = <span class="hljs-string">%q/path/</span>
cmd = <span class="hljs-string">%x(echo hi)</span>
bare = <span class="hljs-string">%(plain <span class="hljs-subst">#{x}</span>)</span>
pipe = <span class="hljs-string">%w|a b|</span>
""");
    }

    [Fact]
    public void Symbols()
    {
        AssertHighlighter("ruby",
"""
status = :active
h = { name: "Bob", age: 3, "key": 1, :old => 2 }
send(:method_name?, :save!, :[], :+, :"quoted sym", :'single')
Foo::Bar::BAZ
obj.respond_to?(:call)
""",
"""
status = <span class="hljs-symbol">:active</span>
h = { <span class="hljs-symbol">name:</span> <span class="hljs-string">&quot;Bob&quot;</span>, <span class="hljs-symbol">age:</span> <span class="hljs-number">3</span>, <span class="hljs-string">&quot;key&quot;</span>: <span class="hljs-number">1</span>, <span class="hljs-symbol">:old</span> =&gt; <span class="hljs-number">2</span> }
send(<span class="hljs-symbol">:method_name?</span>, <span class="hljs-symbol">:save!</span>, <span class="hljs-symbol">:[]</span>, <span class="hljs-symbol">:+</span>, <span class="hljs-symbol">:<span class="hljs-string">&quot;quoted sym&quot;</span></span>, <span class="hljs-symbol">:<span class="hljs-string">&#x27;single&#x27;</span></span>)
<span class="hljs-title class_">Foo</span>::<span class="hljs-title class_">Bar</span>::<span class="hljs-variable constant_">BAZ</span>
obj.respond_to?(<span class="hljs-symbol">:call</span>)
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("ruby",
"""
a = 42
b = 1_000_000
c = 3.14
d = 1e10
e = 1.5e-3
f = 0x1F
g = 0b1010
h = 0o755
i = 0755
j = 3r
k = 2i
l = 1.5ri
m = 0d123
n = -7
o = 10.times
""",
"""
a = <span class="hljs-number">42</span>
b = <span class="hljs-number">1_000_000</span>
c = <span class="hljs-number">3.14</span>
d = <span class="hljs-number">1e10</span>
e = <span class="hljs-number">1.5e-3</span>
f = <span class="hljs-number">0x1F</span>
g = <span class="hljs-number">0b1010</span>
h = <span class="hljs-number">0o755</span>
i = <span class="hljs-number">0755</span>
j = <span class="hljs-number">3r</span>
k = <span class="hljs-number">2i</span>
l = <span class="hljs-number">1.5ri</span>
m = <span class="hljs-number">0d123</span>
n = -<span class="hljs-number">7</span>
o = <span class="hljs-number">10</span>.times
""");
    }

    [Fact]
    public void CharLiterals()
    {
        AssertHighlighter("ruby",
"""
c1 = ?a
c2 = ?\n
c3 = ?\C-a
c4 = ?\M-a
c5 = ?\u0041
c6 = ?\x41
c7 = ?\101
x = cond ? 1 : 2
""",
"""
c1 = <span class="hljs-string">?a</span>
c2 = <span class="hljs-string">?\n</span>
c3 = <span class="hljs-string">?\C-a</span>
c4 = <span class="hljs-string">?\M-a</span>
c5 = <span class="hljs-string">?\u0041</span>
c6 = <span class="hljs-string">?\x41</span>
c7 = <span class="hljs-string">?\101</span>
x = cond ? <span class="hljs-number">1</span> : <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Regex()
    {
        AssertHighlighter("ruby",
"""
if line =~ /^\d+$/
  puts "digits"
end
re = /foo(bar)?/i
m = %r{^/api/#{version}/users}x
n = %r!a/b!
o = %r(paren)
p = %r[bracket]
x = a / b / c
text.gsub(/\s+/, " ")
result = text.match(/(\w+)@(\w+)\.com/)
""",
"""
<span class="hljs-keyword">if</span> line =~ <span class="hljs-regexp">/^\d+$/</span>
  puts <span class="hljs-string">&quot;digits&quot;</span>
<span class="hljs-keyword">end</span>
re = <span class="hljs-regexp">/foo(bar)?/i</span>
m = <span class="hljs-regexp">%r{^/api/<span class="hljs-subst">#{version}</span>/users}x</span>
n = <span class="hljs-regexp">%r!a/b!</span>
o = <span class="hljs-regexp">%r(paren)</span>
p = <span class="hljs-regexp">%r[bracket]</span>
x = a / b / c
text.gsub(<span class="hljs-regexp">/\s+/</span>, <span class="hljs-string">&quot; &quot;</span>)
result = text.match(<span class="hljs-regexp">/(\w+)@(\w+)\.com/</span>)
""");
    }

    [Fact]
    public void RegexUnless()
    {
        AssertHighlighter("ruby",
"""
puts "no" unless /abc/.match?(s)
""",
"""
puts <span class="hljs-string">&quot;no&quot;</span> <span class="hljs-keyword">unless</span> <span class="hljs-regexp">/abc/</span>.match?(s)
""");
    }

    [Fact]
    public void HeredocSquiggly()
    {
        AssertHighlighter("ruby",
"""
text = <<~EOS
  Hello #{name}
  Line with \t escape
EOS
puts text
""",
"""
text = <span class="hljs-string">&lt;&lt;~EOS
  Hello <span class="hljs-subst">#{name}</span>
  Line with \t escape
EOS</span>
puts text
""");
    }

    [Fact]
    public void HeredocDash()
    {
        AssertHighlighter("ruby",
"""
sql = <<-SQL
    SELECT * FROM users
    WHERE id = #{id}
    SQL
""",
"""
sql = <span class="hljs-string">&lt;&lt;-SQL
    SELECT * FROM users
    WHERE id = <span class="hljs-subst">#{id}</span>
    SQL</span>
""");
    }

    [Fact]
    public void HeredocPlain()
    {
        AssertHighlighter("ruby",
"""
x = <<EOT
raw text
EOT
""",
"""
x = <span class="hljs-string">&lt;&lt;EOT
raw text
EOT</span>
""");
    }

    [Fact]
    public void HeredocSingleQuoted()
    {
        AssertHighlighter("ruby",
"""
x = <<~'EOS'
  No #{interpolation} here
EOS
""",
"""
x = <span class="hljs-string">&lt;&lt;~&#x27;EOS&#x27;
  No <span class="hljs-subst">#{interpolation}</span> here
EOS</span>
""");
    }

    [Fact]
    public void HeredocDoubleQuoted()
    {
        AssertHighlighter("ruby",
"""
x = <<~"EOS"
  Yes #{interpolation} here
EOS
""",
"""
x = <span class="hljs-string">&lt;&lt;~&quot;EOS&quot;
  Yes <span class="hljs-subst">#{interpolation}</span> here
EOS</span>
""");
    }

    [Fact]
    public void HeredocMethodCall()
    {
        AssertHighlighter("ruby",
"""
expect(output).to eq(<<~TEXT.strip)
  a
  b
TEXT
foo
""",
"""
expect(output).to eq(<span class="hljs-string">&lt;&lt;~TEXT.strip)
  a
  b
TEXT</span>
foo
""");
    }

    [Fact]
    public void ShiftNotHeredoc()
    {
        AssertHighlighter("ruby",
"""
arr << item
arr <<item
x = 1 << 2
flags = a<<b
""",
"""
arr &lt;&lt; item
arr &lt;&lt;item
x = <span class="hljs-number">1</span> &lt;&lt; <span class="hljs-number">2</span>
flags = a&lt;&lt;b
""");
    }

    [Fact]
    public void Blocks()
    {
        AssertHighlighter("ruby",
"""
[1, 2, 3].each do |n|
  puts n
end
list.map { |x, i| x * i }
hash.each_with_object({}) { |(k, v), memo| memo[k] = v }
a ||= 1
b |= 2
x = a || b
""",
"""
[<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>].each <span class="hljs-keyword">do</span> |<span class="hljs-params">n</span>|
  puts n
<span class="hljs-keyword">end</span>
list.map { |<span class="hljs-params">x, i</span>| x * i }
hash.each_with_object({}) { |<span class="hljs-params">(k, v), memo</span>| memo[k] = v }
a ||= <span class="hljs-number">1</span>
b |= <span class="hljs-number">2</span>
x = a || b
""");
    }

    [Fact]
    public void Lambdas()
    {
        AssertHighlighter("ruby",
"""
square = ->(x) { x * x }
add = lambda { |a, b| a + b }
pr = proc { |x| x }
square.(3)
""",
"""
square = -&gt;(x) { x * x }
add = <span class="hljs-built_in">lambda</span> { |<span class="hljs-params">a, b</span>| a + b }
pr = <span class="hljs-built_in">proc</span> { |<span class="hljs-params">x</span>| x }
square.(<span class="hljs-number">3</span>)
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("ruby",
"""
if x > 0 && y
  :positive
elsif x.zero?
  :zero
else
  :negative
end

case value
when Integer then "int"
when String, Symbol then "str"
in {name: String => name}
  name
else
  nil
end

while i < 10
  i += 1
  next if i.odd?
  break if i > 5
end

until done?
  retry
end

for i in 0..5 do redo end
""",
"""
<span class="hljs-keyword">if</span> x &gt; <span class="hljs-number">0</span> &amp;&amp; y
  <span class="hljs-symbol">:positive</span>
<span class="hljs-keyword">elsif</span> x.zero?
  <span class="hljs-symbol">:zero</span>
<span class="hljs-keyword">else</span>
  <span class="hljs-symbol">:negative</span>
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">case</span> value
<span class="hljs-keyword">when</span> <span class="hljs-title class_">Integer</span> <span class="hljs-keyword">then</span> <span class="hljs-string">&quot;int&quot;</span>
<span class="hljs-keyword">when</span> <span class="hljs-title class_">String</span>, <span class="hljs-title class_">Symbol</span> <span class="hljs-keyword">then</span> <span class="hljs-string">&quot;str&quot;</span>
<span class="hljs-keyword">in</span> {<span class="hljs-symbol">name:</span> <span class="hljs-title class_">String</span> =&gt; name}
  name
<span class="hljs-keyword">else</span>
  <span class="hljs-literal">nil</span>
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">while</span> i &lt; <span class="hljs-number">10</span>
  i += <span class="hljs-number">1</span>
  <span class="hljs-keyword">next</span> <span class="hljs-keyword">if</span> i.odd?
  <span class="hljs-keyword">break</span> <span class="hljs-keyword">if</span> i &gt; <span class="hljs-number">5</span>
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">until</span> done?
  <span class="hljs-keyword">retry</span>
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">for</span> i <span class="hljs-keyword">in</span> <span class="hljs-number">0</span>..<span class="hljs-number">5</span> <span class="hljs-keyword">do</span> <span class="hljs-keyword">redo</span> <span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Exceptions()
    {
        AssertHighlighter("ruby",
"""
begin
  risky
rescue ArgumentError => e
  warn e.message
  raise
rescue StandardError, IOError
  retry
else
  ok
ensure
  cleanup
end
throw :done
""",
"""
<span class="hljs-keyword">begin</span>
  risky
<span class="hljs-keyword">rescue</span> <span class="hljs-title class_">ArgumentError</span> =&gt; e
  warn e.message
  <span class="hljs-keyword">raise</span>
<span class="hljs-keyword">rescue</span> <span class="hljs-title class_">StandardError</span>, <span class="hljs-title class_">IOError</span>
  <span class="hljs-keyword">retry</span>
<span class="hljs-keyword">else</span>
  ok
<span class="hljs-keyword">ensure</span>
  cleanup
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">throw</span> <span class="hljs-symbol">:done</span>
""");
    }

    [Fact]
    public void Variables()
    {
        AssertHighlighter("ruby",
"""
$global = 1
@instance = 2
@@class_var = 3
$stdout.puts $0
$! $@ $~
CONSTANT = 4
MAX_SIZE = 10
""",
"""
<span class="hljs-variable">$global</span> = <span class="hljs-number">1</span>
<span class="hljs-variable">@instance</span> = <span class="hljs-number">2</span>
<span class="hljs-variable">@@class_var</span> = <span class="hljs-number">3</span>
<span class="hljs-variable">$stdout</span>.puts <span class="hljs-variable">$0</span>
<span class="hljs-variable">$!</span> <span class="hljs-variable">$@</span> <span class="hljs-variable">$~</span>
<span class="hljs-variable constant_">CONSTANT</span> = <span class="hljs-number">4</span>
<span class="hljs-variable constant_">MAX_SIZE</span> = <span class="hljs-number">10</span>
""");
    }

    [Fact]
    public void Special()
    {
        AssertHighlighter("ruby",
"""
__FILE__
__LINE__
__ENCODING__
self.foo
super(a)
defined?(foo)
yield x
alias new_name old_name
undef old_method
BEGIN { puts "start" }
END { puts "end" }
""",
"""
<span class="hljs-variable constant_">__FILE__</span>
<span class="hljs-variable constant_">__LINE__</span>
<span class="hljs-variable constant_">__ENCODING__</span>
<span class="hljs-variable language_">self</span>.foo
<span class="hljs-variable language_">super</span>(a)
<span class="hljs-keyword">defined</span>?(foo)
<span class="hljs-keyword">yield</span> x
<span class="hljs-keyword">alias</span> new_name old_name
<span class="hljs-keyword">undef</span> old_method
<span class="hljs-variable constant_">BEGIN</span> { puts <span class="hljs-string">&quot;start&quot;</span> }
<span class="hljs-variable constant_">END</span> { puts <span class="hljs-string">&quot;end&quot;</span> }
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("ruby",
"""
# A regular comment
x = 1 # trailing comment
# TODO: fix this
# @param name [String] the name
# @return [Boolean]
=begin
block comment
@note something
=end
y = 2
""",
"""
<span class="hljs-comment"># A regular comment</span>
x = <span class="hljs-number">1</span> <span class="hljs-comment"># trailing comment</span>
<span class="hljs-comment"># <span class="hljs-doctag">TODO:</span> fix this</span>
<span class="hljs-comment"># <span class="hljs-doctag">@param</span> name [String] the name</span>
<span class="hljs-comment"># <span class="hljs-doctag">@return</span> [Boolean]</span>
<span class="hljs-comment">=begin
block comment
<span class="hljs-doctag">@note</span> something
=end</span>
y = <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void EndMarker()
    {
        AssertHighlighter("ruby",
"""
puts DATA.read
__END__
this is data
def not_code
""",
"""
puts <span class="hljs-variable constant_">DATA</span>.read
<span class="hljs-comment">__END__
this is data
def not_code</span>
""");
    }

    [Fact]
    public void RailsModel()
    {
        AssertHighlighter("ruby",
"""
class User < ApplicationRecord
  has_many :posts, dependent: :destroy
  belongs_to :organization, optional: true
  validates :email, presence: true, uniqueness: { case_sensitive: false }
  before_save :normalize_email
  scope :active, -> { where(active: true) }

  private

  def normalize_email
    self.email = email.downcase.strip
  end
end
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">User</span> &lt; <span class="hljs-title class_ inherited__">ApplicationRecord</span>
  has_many <span class="hljs-symbol">:posts</span>, <span class="hljs-symbol">dependent:</span> <span class="hljs-symbol">:destroy</span>
  belongs_to <span class="hljs-symbol">:organization</span>, <span class="hljs-symbol">optional:</span> <span class="hljs-literal">true</span>
  validates <span class="hljs-symbol">:email</span>, <span class="hljs-symbol">presence:</span> <span class="hljs-literal">true</span>, <span class="hljs-symbol">uniqueness:</span> { <span class="hljs-symbol">case_sensitive:</span> <span class="hljs-literal">false</span> }
  before_save <span class="hljs-symbol">:normalize_email</span>
  scope <span class="hljs-symbol">:active</span>, -&gt; { where(<span class="hljs-symbol">active:</span> <span class="hljs-literal">true</span>) }

  <span class="hljs-keyword">private</span>

  <span class="hljs-keyword">def</span> <span class="hljs-title function_">normalize_email</span>
    <span class="hljs-variable language_">self</span>.email = email.downcase.strip
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void RailsController()
    {
        AssertHighlighter("ruby",
"""
class UsersController < ApplicationController
  before_action :set_user, only: %i[show edit update destroy]

  def index
    @users = User.where(active: true).order(created_at: :desc).page(params[:page])
    render json: @users, status: :ok
  end

  def create
    @user = User.new(user_params)
    if @user.save
      redirect_to @user, notice: "User was created."
    else
      render :new, status: :unprocessable_entity
    end
  end

  private

  def user_params
    params.require(:user).permit(:name, :email)
  end
end
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">UsersController</span> &lt; <span class="hljs-title class_ inherited__">ApplicationController</span>
  before_action <span class="hljs-symbol">:set_user</span>, <span class="hljs-symbol">only:</span> <span class="hljs-string">%i[show edit update destroy]</span>

  <span class="hljs-keyword">def</span> <span class="hljs-title function_">index</span>
    <span class="hljs-variable">@users</span> = <span class="hljs-title class_">User</span>.where(<span class="hljs-symbol">active:</span> <span class="hljs-literal">true</span>).order(<span class="hljs-symbol">created_at:</span> <span class="hljs-symbol">:desc</span>).page(params[<span class="hljs-symbol">:page</span>])
    render <span class="hljs-symbol">json:</span> <span class="hljs-variable">@users</span>, <span class="hljs-symbol">status:</span> <span class="hljs-symbol">:ok</span>
  <span class="hljs-keyword">end</span>

  <span class="hljs-keyword">def</span> <span class="hljs-title function_">create</span>
    <span class="hljs-variable">@user</span> = <span class="hljs-title class_">User</span>.new(user_params)
    <span class="hljs-keyword">if</span> <span class="hljs-variable">@user</span>.save
      redirect_to <span class="hljs-variable">@user</span>, <span class="hljs-symbol">notice:</span> <span class="hljs-string">&quot;User was created.&quot;</span>
    <span class="hljs-keyword">else</span>
      render <span class="hljs-symbol">:new</span>, <span class="hljs-symbol">status:</span> <span class="hljs-symbol">:unprocessable_entity</span>
    <span class="hljs-keyword">end</span>
  <span class="hljs-keyword">end</span>

  <span class="hljs-keyword">private</span>

  <span class="hljs-keyword">def</span> <span class="hljs-title function_">user_params</span>
    params.require(<span class="hljs-symbol">:user</span>).permit(<span class="hljs-symbol">:name</span>, <span class="hljs-symbol">:email</span>)
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void RailsMigration()
    {
        AssertHighlighter("ruby",
"""
class CreateUsers < ActiveRecord::Migration[7.1]
  def change
    create_table :users do |t|
      t.string :name, null: false
      t.references :org, foreign_key: true
      t.timestamps
    end
    add_index :users, :email, unique: true
  end
end
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">CreateUsers</span> &lt; <span class="hljs-title class_ inherited__">ActiveRecord::Migration</span>[<span class="hljs-number">7.1</span>]
  <span class="hljs-keyword">def</span> <span class="hljs-title function_">change</span>
    create_table <span class="hljs-symbol">:users</span> <span class="hljs-keyword">do</span> |<span class="hljs-params">t</span>|
      t.string <span class="hljs-symbol">:name</span>, <span class="hljs-symbol">null:</span> <span class="hljs-literal">false</span>
      t.references <span class="hljs-symbol">:org</span>, <span class="hljs-symbol">foreign_key:</span> <span class="hljs-literal">true</span>
      t.timestamps
    <span class="hljs-keyword">end</span>
    add_index <span class="hljs-symbol">:users</span>, <span class="hljs-symbol">:email</span>, <span class="hljs-symbol">unique:</span> <span class="hljs-literal">true</span>
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void ObjectCreation()
    {
        AssertHighlighter("ruby",
"""
x = Foo.new(1)
y = Foo::Bar.new
z = HTTPServer.new do |s| end
s = Struct.new(:a, :b)
""",
"""
x = <span class="hljs-title class_">Foo</span>.new(<span class="hljs-number">1</span>)
y = <span class="hljs-title class_">Foo</span>::<span class="hljs-title class_">Bar</span>.new
z = <span class="hljs-title class_">HTTPServer</span>.new <span class="hljs-keyword">do</span> |<span class="hljs-params">s</span>| <span class="hljs-keyword">end</span>
s = <span class="hljs-title class_">Struct</span>.new(<span class="hljs-symbol">:a</span>, <span class="hljs-symbol">:b</span>)
""");
    }

    [Fact]
    public void Gemspec()
    {
        AssertHighlighter("ruby",
"""
Gem::Specification.new do |spec|
  spec.name          = "my_gem"
  spec.version       = MyGem::VERSION
  spec.authors       = ["Jane"]
  spec.required_ruby_version = ">= 3.0"
  spec.add_dependency "rack", "~> 2.0"
end
""",
"""
<span class="hljs-title class_">Gem::Specification</span>.new <span class="hljs-keyword">do</span> |<span class="hljs-params">spec</span>|
  spec.name          = <span class="hljs-string">&quot;my_gem&quot;</span>
  spec.version       = <span class="hljs-title class_">MyGem</span>::<span class="hljs-variable constant_">VERSION</span>
  spec.authors       = [<span class="hljs-string">&quot;Jane&quot;</span>]
  spec.required_ruby_version = <span class="hljs-string">&quot;&gt;= 3.0&quot;</span>
  spec.add_dependency <span class="hljs-string">&quot;rack&quot;</span>, <span class="hljs-string">&quot;~&gt; 2.0&quot;</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void IrbSession()
    {
        AssertHighlighter("ruby",
"""
irb(main):001:0> 1 + 2
=> 3
>> "abc".upcase
=> "ABC"
irb(main):002:0> [1, 2].map { |x| x * 2 }
=> [2, 4]
""",
"""
<span class="hljs-meta prompt_">irb(main):001:0&gt;</span> <span class="hljs-number">1</span> + <span class="hljs-number">2</span>
=&gt; <span class="hljs-number">3</span>
<span class="hljs-meta prompt_">&gt;&gt;</span> <span class="hljs-string">&quot;abc&quot;</span>.upcase
=&gt; <span class="hljs-string">&quot;ABC&quot;</span>
<span class="hljs-meta prompt_">irb(main):002:0&gt;</span> [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>].map { |<span class="hljs-params">x</span>| x * <span class="hljs-number">2</span> }
=&gt; [<span class="hljs-number">2</span>, <span class="hljs-number">4</span>]
""");
    }

    [Fact]
    public void IrbObject()
    {
        AssertHighlighter("ruby",
"""
p obj
#<Object:0x000001 @a=1>
""",
"""
p obj
#&lt;Object:0x000001 @a=1&gt;
""");
    }

    [Fact]
    public void KeywordArgs()
    {
        AssertHighlighter("ruby",
"""
def connect(host:, port: 80, **options)
  Net::HTTP.start(host, port, **options)
end
connect(host: "example.com", port: 443)
""",
"""
<span class="hljs-keyword">def</span> <span class="hljs-title function_">connect</span>(<span class="hljs-params"><span class="hljs-symbol">host:</span>, <span class="hljs-symbol">port:</span> <span class="hljs-number">80</span>, **options</span>)
  <span class="hljs-title class_">Net</span>::<span class="hljs-variable constant_">HTTP</span>.start(host, port, **options)
<span class="hljs-keyword">end</span>
connect(<span class="hljs-symbol">host:</span> <span class="hljs-string">&quot;example.com&quot;</span>, <span class="hljs-symbol">port:</span> <span class="hljs-number">443</span>)
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("ruby",
"""
a = b + c - d * e / f % g ** h
x = !y && z || w
m = a <=> b
n = a == b and c != d or not e
arr[0] += 1
x = y&.name
r = (1..10).step(2)
s = (1...10)
""",
"""
a = b + c - d * e / f % g ** h
x = !y &amp;&amp; z || w
m = a &lt;=&gt; b
n = a == b <span class="hljs-keyword">and</span> c != d <span class="hljs-keyword">or</span> <span class="hljs-keyword">not</span> e
arr[<span class="hljs-number">0</span>] += <span class="hljs-number">1</span>
x = y&amp;.name
r = (<span class="hljs-number">1</span>..<span class="hljs-number">10</span>).step(<span class="hljs-number">2</span>)
s = (<span class="hljs-number">1</span>...<span class="hljs-number">10</span>)
""");
    }

    [Fact]
    public void StringFormats()
    {
        AssertHighlighter("ruby",
"""
puts "Value: %d" % [42]
puts format("%.2f", 3.14159)
puts 'a' 'b'
x = "tab\there"
y = "unicode \u00e9"
""",
"""
puts <span class="hljs-string">&quot;Value: %d&quot;</span> % [<span class="hljs-number">42</span>]
puts format(<span class="hljs-string">&quot;%.2f&quot;</span>, <span class="hljs-number">3.14159</span>)
puts <span class="hljs-string">&#x27;a&#x27;</span> <span class="hljs-string">&#x27;b&#x27;</span>
x = <span class="hljs-string">&quot;tab\there&quot;</span>
y = <span class="hljs-string">&quot;unicode \u00e9&quot;</span>
""");
    }

    [Fact]
    public void MethodCallChain()
    {
        AssertHighlighter("ruby",
"""
users.select(&:active?).map(&:name).sort_by { |n| n.downcase }.first(10)
""",
"""
users.select(&amp;<span class="hljs-symbol">:active?</span>).map(&amp;<span class="hljs-symbol">:name</span>).sort_by { |<span class="hljs-params">n</span>| n.downcase }.first(<span class="hljs-number">10</span>)
""");
    }

    [Fact]
    public void MultilineHash()
    {
        AssertHighlighter("ruby",
"""
config = {
  adapter: "postgresql",
  host: ENV.fetch("DB_HOST", "localhost"),
  pool: ENV["POOL"].to_i,
  timeout: 5000,
}
""",
"""
config = {
  <span class="hljs-symbol">adapter:</span> <span class="hljs-string">&quot;postgresql&quot;</span>,
  <span class="hljs-symbol">host:</span> <span class="hljs-variable constant_">ENV</span>.fetch(<span class="hljs-string">&quot;DB_HOST&quot;</span>, <span class="hljs-string">&quot;localhost&quot;</span>),
  <span class="hljs-symbol">pool:</span> <span class="hljs-variable constant_">ENV</span>[<span class="hljs-string">&quot;POOL&quot;</span>].to_i,
  <span class="hljs-symbol">timeout:</span> <span class="hljs-number">5000</span>,
}
""");
    }

    [Fact]
    public void ClassConstants()
    {
        AssertHighlighter("ruby",
"""
class Config
  DEFAULTS = { verbose: false }.freeze
  VERSION = "1.0.0"
  Error = Class.new(StandardError)
end
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Config</span>
  <span class="hljs-variable constant_">DEFAULTS</span> = { <span class="hljs-symbol">verbose:</span> <span class="hljs-literal">false</span> }.freeze
  <span class="hljs-variable constant_">VERSION</span> = <span class="hljs-string">&quot;1.0.0&quot;</span>
  <span class="hljs-title class_">Error</span> = <span class="hljs-title class_">Class</span>.new(<span class="hljs-title class_">StandardError</span>)
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void AttrDefine()
    {
        AssertHighlighter("ruby",
"""
class Foo
  attr_accessor :x, :y
  attr_writer :z
  define_method(:bar) { |a| a }
  private_constant :SECRET
  module_function
  public
  protected
  def x; end
end
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Foo</span>
  <span class="hljs-built_in">attr_accessor</span> <span class="hljs-symbol">:x</span>, <span class="hljs-symbol">:y</span>
  <span class="hljs-built_in">attr_writer</span> <span class="hljs-symbol">:z</span>
  <span class="hljs-built_in">define_method</span>(<span class="hljs-symbol">:bar</span>) { |<span class="hljs-params">a</span>| a }
  <span class="hljs-built_in">private_constant</span> <span class="hljs-symbol">:SECRET</span>
  <span class="hljs-built_in">module_function</span>
  <span class="hljs-keyword">public</span>
  <span class="hljs-keyword">protected</span>
  <span class="hljs-keyword">def</span> <span class="hljs-title function_">x</span>; <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void StructData()
    {
        AssertHighlighter("ruby",
"""
Point = Struct.new(:x, :y) do
  def distance
    Math.sqrt(x**2 + y**2)
  end
end
Coord = Data.define(:lat, :lng)
""",
"""
<span class="hljs-title class_">Point</span> = <span class="hljs-title class_">Struct</span>.new(<span class="hljs-symbol">:x</span>, <span class="hljs-symbol">:y</span>) <span class="hljs-keyword">do</span>
  <span class="hljs-keyword">def</span> <span class="hljs-title function_">distance</span>
    <span class="hljs-title class_">Math</span>.sqrt(x**<span class="hljs-number">2</span> + y**<span class="hljs-number">2</span>)
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
<span class="hljs-title class_">Coord</span> = <span class="hljs-title class_">Data</span>.define(<span class="hljs-symbol">:lat</span>, <span class="hljs-symbol">:lng</span>)
""");
    }

    [Fact]
    public void PatternMatch()
    {
        AssertHighlighter("ruby",
"""
case [1, [2, 3]]
in [Integer => a, [b, *c]]
  puts a
in {status: 200 | 201 => code}
  code
end
config => {db: {user:}}
""",
"""
<span class="hljs-keyword">case</span> [<span class="hljs-number">1</span>, [<span class="hljs-number">2</span>, <span class="hljs-number">3</span>]]
<span class="hljs-keyword">in</span> [<span class="hljs-title class_">Integer</span> =&gt; a, [b, *c]]
  puts a
<span class="hljs-keyword">in</span> {<span class="hljs-symbol">status:</span> <span class="hljs-number">200</span> | <span class="hljs-number">201</span> =&gt; code}
  code
<span class="hljs-keyword">end</span>
config =&gt; {<span class="hljs-symbol">db:</span> {<span class="hljs-symbol">user:</span>}}
""");
    }

    [Fact]
    public void Modules()
    {
        AssertHighlighter("ruby",
"""
module Greeting
  def self.included(base)
    base.extend(ClassMethods)
  end

  module ClassMethods
    def greet = "hi"
  end
end
""",
"""
<span class="hljs-keyword">module</span> <span class="hljs-title class_">Greeting</span>
  <span class="hljs-keyword">def</span> <span class="hljs-variable language_">self</span>.<span class="hljs-title function_">included</span>(<span class="hljs-params">base</span>)
    base.extend(<span class="hljs-title class_">ClassMethods</span>)
  <span class="hljs-keyword">end</span>

  <span class="hljs-keyword">module</span> <span class="hljs-title class_">ClassMethods</span>
    <span class="hljs-keyword">def</span> <span class="hljs-title function_">greet</span> = <span class="hljs-string">&quot;hi&quot;</span>
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void EndlessMethod()
    {
        AssertHighlighter("ruby",
"""
def square(x) = x * x
def full_name = "#{first} #{last}"
""",
"""
<span class="hljs-keyword">def</span> <span class="hljs-title function_">square</span>(<span class="hljs-params">x</span>) = x * x
<span class="hljs-keyword">def</span> <span class="hljs-title function_">full_name</span> = <span class="hljs-string">&quot;<span class="hljs-subst">#{first}</span> <span class="hljs-subst">#{last}</span>&quot;</span>
""");
    }

    [Fact]
    public void Require()
    {
        AssertHighlighter("ruby",
"""
require 'json'
require_relative "../lib/foo"
load "file.rb"
""",
"""
<span class="hljs-keyword">require</span> <span class="hljs-string">&#x27;json&#x27;</span>
require_relative <span class="hljs-string">&quot;../lib/foo&quot;</span>
load <span class="hljs-string">&quot;file.rb&quot;</span>
""");
    }

    [Fact]
    public void DivisionAmbiguity()
    {
        AssertHighlighter("ruby",
"""
total = sum / count
avg = (a + b) / 2
path = "a/b"
x = y /2
""",
"""
total = sum / count
avg = (a + b) / <span class="hljs-number">2</span>
path = <span class="hljs-string">&quot;a/b&quot;</span>
x = y /<span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void NumberedParams()
    {
        AssertHighlighter("ruby",
"""
[1, 2].map { _1 * 2 }
[1, 2].map { it * 2 }
""",
"""
[<span class="hljs-number">1</span>, <span class="hljs-number">2</span>].map { _1 * <span class="hljs-number">2</span> }
[<span class="hljs-number">1</span>, <span class="hljs-number">2</span>].map { it * <span class="hljs-number">2</span> }
""");
    }

    [Fact]
    public void GlobalSpecial()
    {
        AssertHighlighter("ruby",
"""
$stderr.puts "err"
$LOAD_PATH << "lib"
$PROGRAM_NAME
puts $1
""",
"""
<span class="hljs-variable">$stderr</span>.puts <span class="hljs-string">&quot;err&quot;</span>
<span class="hljs-variable">$LOAD_PATH</span> &lt;&lt; <span class="hljs-string">&quot;lib&quot;</span>
<span class="hljs-variable">$PROGRAM_NAME</span>
puts $<span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void MultilineString()
    {
        AssertHighlighter("ruby",
"""
s = "line one
line two #{x}
line three"
""",
"""
s = <span class="hljs-string">&quot;line one
line two <span class="hljs-subst">#{x}</span>
line three&quot;</span>
""");
    }

    [Fact]
    public void NestedInterpolation()
    {
        AssertHighlighter("ruby",
"""
"a #{b.map { |c| "#{c}!" }.join(", ")} d"
""",
"""
<span class="hljs-string">&quot;a <span class="hljs-subst">#{b.map { |<span class="hljs-params">c</span>| <span class="hljs-string">&quot;<span class="hljs-subst">#{c}</span>!&quot;</span> }.join(<span class="hljs-string">&quot;, &quot;</span>)}</span> d&quot;</span>
""");
    }

    [Fact]
    public void BlockCommentCode()
    {
        AssertHighlighter("ruby",
"""
=begin
def foo
  bar
end
=end
""",
"""
<span class="hljs-comment">=begin
def foo
  bar
end
=end</span>
""");
    }

    [Fact]
    public void ParamsWithDefaults()
    {
        AssertHighlighter("ruby",
"""
def initialize(name = "x", *rest, key: :val, &blk)
end
def m(a, (b, c), d)
end
""",
"""
<span class="hljs-keyword">def</span> <span class="hljs-title function_">initialize</span>(<span class="hljs-params">name = <span class="hljs-string">&quot;x&quot;</span>, *rest, <span class="hljs-symbol">key:</span> <span class="hljs-symbol">:val</span>, &amp;blk</span>)
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">def</span> <span class="hljs-title function_">m</span>(<span class="hljs-params">a, (b, c</span>), d)
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Rspec()
    {
        AssertHighlighter("ruby",
"""
RSpec.describe User, type: :model do
  let(:user) { build(:user, email: "a@b.c") }

  it "is valid" do
    expect(user).to be_valid
  end

  context "when email is missing" do
    before { user.email = nil }
    it { is_expected.not_to be_valid }
  end
end
""",
"""
<span class="hljs-title class_">RSpec</span>.describe <span class="hljs-title class_">User</span>, <span class="hljs-symbol">type:</span> <span class="hljs-symbol">:model</span> <span class="hljs-keyword">do</span>
  let(<span class="hljs-symbol">:user</span>) { build(<span class="hljs-symbol">:user</span>, <span class="hljs-symbol">email:</span> <span class="hljs-string">&quot;a@b.c&quot;</span>) }

  it <span class="hljs-string">&quot;is valid&quot;</span> <span class="hljs-keyword">do</span>
    expect(user).to be_valid
  <span class="hljs-keyword">end</span>

  context <span class="hljs-string">&quot;when email is missing&quot;</span> <span class="hljs-keyword">do</span>
    before { user.email = <span class="hljs-literal">nil</span> }
    it { is_expected.not_to be_valid }
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Rake()
    {
        AssertHighlighter("ruby",
"""
namespace :db do
  desc "Seed data"
  task seed: :environment do
    User.create!(name: "admin")
  end
end
""",
"""
namespace <span class="hljs-symbol">:db</span> <span class="hljs-keyword">do</span>
  desc <span class="hljs-string">&quot;Seed data&quot;</span>
  task <span class="hljs-symbol">seed:</span> <span class="hljs-symbol">:environment</span> <span class="hljs-keyword">do</span>
    <span class="hljs-title class_">User</span>.create!(<span class="hljs-symbol">name:</span> <span class="hljs-string">&quot;admin&quot;</span>)
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void MiscOps()
    {
        AssertHighlighter("ruby",
"""
a = b ? c : d
a.b&.c
x = :sym?
y = foo!(bar)
@x ||= compute
""",
"""
a = b ? c : d
a.b&amp;.c
x = <span class="hljs-symbol">:sym?</span>
y = foo!(bar)
<span class="hljs-variable">@x</span> ||= compute
""");
    }

    [Fact]
    public void UpperWords()
    {
        AssertHighlighter("ruby",
"""
HTTP = 1
JSONParser.parse(s)
XMLHttpRequest
ABC_DEF
A1
""",
"""
<span class="hljs-variable constant_">HTTP</span> = <span class="hljs-number">1</span>
<span class="hljs-title class_">JSONParser</span>.parse(s)
<span class="hljs-title class_">XMLHttpRequest</span>
<span class="hljs-variable constant_">ABC_DEF</span>
<span class="hljs-variable constant_">A1</span>
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("ruby",
"""
名前 = "値"
puts "héllo"
""",
"""
名前 = <span class="hljs-string">&quot;値&quot;</span>
puts <span class="hljs-string">&quot;héllo&quot;</span>
""");
    }

    [Fact]
    public void ClassNewBlock()
    {
        AssertHighlighter("ruby",
"""
klass = Class.new do
  def hello; "hi"; end
end
""",
"""
klass = <span class="hljs-title class_">Class</span>.new <span class="hljs-keyword">do</span>
  <span class="hljs-keyword">def</span> <span class="hljs-title function_">hello</span>; <span class="hljs-string">&quot;hi&quot;</span>; <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void SendOps()
    {
        AssertHighlighter("ruby",
"""
obj.send(:+, 1)
obj.method(:==)
[:a, :b].include?(:a)
x = :<=>
""",
"""
obj.send(<span class="hljs-symbol">:+</span>, <span class="hljs-number">1</span>)
obj.method(<span class="hljs-symbol">:==</span>)
[<span class="hljs-symbol">:a</span>, <span class="hljs-symbol">:b</span>].include?(<span class="hljs-symbol">:a</span>)
x = <span class="hljs-symbol">:&lt;=&gt;</span>
""");
    }

    [Fact]
    public void RescueModifier()
    {
        AssertHighlighter("ruby",
"""
value = Integer(str) rescue nil
""",
"""
value = <span class="hljs-title class_">Integer</span>(str) <span class="hljs-keyword">rescue</span> <span class="hljs-literal">nil</span>
""");
    }

    [Fact]
    public void Frozen()
    {
        AssertHighlighter("ruby",
"""
# encoding: utf-8
# frozen_string_literal: true
""",
"""
<span class="hljs-comment"># encoding: utf-8</span>
<span class="hljs-comment"># frozen_string_literal: true</span>
""");
    }

    [Fact]
    public void PercentNested()
    {
        AssertHighlighter("ruby",
"""
x = %w(a (b) c)
y = %q{a {b} c}
""",
"""
x = <span class="hljs-string">%w(a (b) c)</span>
y = <span class="hljs-string">%q{a {b} c}</span>
""");
    }

    [Fact]
    public void HeredocWithIndentTerminator()
    {
        AssertHighlighter("ruby",
"""
def foo
  <<~HTML
    <div>#{content}</div>
  HTML
end
""",
"""
<span class="hljs-keyword">def</span> <span class="hljs-title function_">foo</span>
  <span class="hljs-string">&lt;&lt;~HTML
    &lt;div&gt;<span class="hljs-subst">#{content}</span>&lt;/div&gt;
  HTML</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void HeredocUnterminated()
    {
        AssertHighlighter("ruby",
"""
x = <<~NOPE
  text without end
""",
"""
x = &lt;&lt;~<span class="hljs-variable constant_">NOPE</span>
  text without <span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void GsubBlock()
    {
        AssertHighlighter("ruby",
"""
str.gsub(/(\d+)/) { $1.to_i * 2 }
str.sub!(%r{/+$}, "")
str.split(/,\s*/)
""",
"""
str.gsub(<span class="hljs-regexp">/(\d+)/</span>) { <span class="hljs-variable">$1</span>.to_i * <span class="hljs-number">2</span> }
str.sub!(<span class="hljs-regexp">%r{/+$}</span>, <span class="hljs-string">&quot;&quot;</span>)
str.split(<span class="hljs-regexp">/,\s*/</span>)
""");
    }

    [Fact]
    public void RvmPrompt()
    {
        AssertHighlighter("ruby",
"""
2.7.2 :001 > puts "hi"
""",
"""
<span class="hljs-meta prompt_">2.7.2 :001 &gt;</span> puts <span class="hljs-string">&quot;hi&quot;</span>
""");
    }

    [Fact]
    public void Ranges()
    {
        AssertHighlighter("ruby",
"""
(1..10).each { |i| puts i }
('a'..'z').to_a
""",
"""
(<span class="hljs-number">1</span>..<span class="hljs-number">10</span>).each { |<span class="hljs-params">i</span>| puts i }
(<span class="hljs-string">&#x27;a&#x27;</span>..<span class="hljs-string">&#x27;z&#x27;</span>).to_a
""");
    }

    [Fact]
    public void YardMulti()
    {
        AssertHighlighter("ruby",
"""
##
# Computes something.
#
# @param [Integer] a first
# @option opts [String] :name
# @yield [x] block
# @raise [ArgumentError]
def compute(a, opts = {})
end
""",
"""
<span class="hljs-comment">##</span>
<span class="hljs-comment"># Computes something.</span>
<span class="hljs-comment">#</span>
<span class="hljs-comment"># <span class="hljs-doctag">@param</span> [Integer] a first</span>
<span class="hljs-comment"># <span class="hljs-doctag">@option</span> opts [String] :name</span>
<span class="hljs-comment"># <span class="hljs-doctag">@yield</span> [x] block</span>
<span class="hljs-comment"># <span class="hljs-doctag">@raise</span> [ArgumentError]</span>
<span class="hljs-keyword">def</span> <span class="hljs-title function_">compute</span>(<span class="hljs-params">a, opts = {}</span>)
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void MethodKeywords()
    {
        AssertHighlighter("ruby",
"""
x.nil?
obj.class.name
params.require(:user)
range.end
foo&.then { |v| v }
(1..nil).each
y = x.not
""",
"""
x.nil?
obj.class.name
params.require(<span class="hljs-symbol">:user</span>)
range.end
foo&amp;.then { |<span class="hljs-params">v</span>| v }
(<span class="hljs-number">1</span>..<span class="hljs-literal">nil</span>).each
y = x.not
""");
    }

    [Fact]
    public void Namespaces()
    {
        AssertHighlighter("ruby",
"""
ActiveRecord::Base.transaction do
  ::File.read(path)
  Net::HTTP.get(URI("http://x"))
end
x = Foo::BAR
foo::bar
""",
"""
<span class="hljs-title class_">ActiveRecord</span>::<span class="hljs-title class_">Base</span>.transaction <span class="hljs-keyword">do</span>
  ::<span class="hljs-title class_">File</span>.read(path)
  <span class="hljs-title class_">Net</span>::<span class="hljs-variable constant_">HTTP</span>.get(<span class="hljs-variable constant_">URI</span>(<span class="hljs-string">&quot;http://x&quot;</span>))
<span class="hljs-keyword">end</span>
x = <span class="hljs-title class_">Foo</span>::<span class="hljs-variable constant_">BAR</span>
foo::bar
""");
    }

    [Fact]
    public void NestedRegex()
    {
        AssertHighlighter("ruby",
"""
re = %r{\A\d{3}-\d{4}\z}
re2 = %r(a(b)c)i
re3 = %r[x[yz]]
""",
"""
re = <span class="hljs-regexp">%r{\A\d{3}-\d{4}\z}</span>
re2 = <span class="hljs-regexp">%r(a(b)c)i</span>
re3 = <span class="hljs-regexp">%r[x[yz]]</span>
""");
    }

    [Fact]
    public void SymbolArrays()
    {
        AssertHighlighter("ruby",
"""
%i[a b c]
%I[x#{1} y]
%w[a [b] c]
%w<a <b> c>
""",
"""
<span class="hljs-string">%i[a b c]</span>
<span class="hljs-string">%I[x<span class="hljs-subst">#{<span class="hljs-number">1</span>}</span> y]</span>
<span class="hljs-string">%w[a [b] c]</span>
<span class="hljs-string">%w&lt;a &lt;b&gt; c&gt;</span>
""");
    }

    [Fact]
    public void BlockParamsPipes()
    {
        AssertHighlighter("ruby",
"""
items.each { |item| total |= item }
[1].each do |a, b|
end
list.each {|x| x }
a || b
a | b
a ||= []
""",
"""
items.each { |<span class="hljs-params">item</span>| total |= item }
[<span class="hljs-number">1</span>].each <span class="hljs-keyword">do</span> |<span class="hljs-params">a, b</span>|
<span class="hljs-keyword">end</span>
list.each {|<span class="hljs-params">x</span>| x }
a || b
a | b
a ||= []
""");
    }

    [Fact]
    public void SingletonMethod()
    {
        AssertHighlighter("ruby",
"""
def self.find_by_name(name)
  where(name: name).first
end
def self.[](key) = @map[key]
def selfish; end
undef foo
""",
"""
<span class="hljs-keyword">def</span> <span class="hljs-variable language_">self</span>.<span class="hljs-title function_">find_by_name</span>(<span class="hljs-params">name</span>)
  where(<span class="hljs-symbol">name:</span> name).first
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">def</span> <span class="hljs-variable language_">self</span>.<span class="hljs-title function_">[]</span>(<span class="hljs-params">key</span>) = <span class="hljs-variable">@map</span>[key]
<span class="hljs-keyword">def</span> <span class="hljs-title function_">selfish</span>; <span class="hljs-keyword">end</span>
<span class="hljs-keyword">undef</span> foo
""");
    }
}
