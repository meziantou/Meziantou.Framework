namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class ElixirHighlighterTests
{
    [Fact]
    public void Module()
    {
        AssertHighlighter("elixir",
""""
defmodule MyApp.Greeter do
  @moduledoc """
  Greets people.
  """

  @default_name "World"

  def hello(name \\ @default_name) do
    "Hello, #{name}!"
  end

  defp secret, do: :ok
end
"""",
"""
<span class="hljs-class"><span class="hljs-keyword">defmodule</span> <span class="hljs-title">MyApp.Greeter</span></span> <span class="hljs-keyword">do</span>
  <span class="hljs-variable">@moduledoc</span> <span class="hljs-string">&quot;&quot;&quot;
  Greets people.
  &quot;&quot;&quot;</span>

  <span class="hljs-variable">@default_name</span> <span class="hljs-string">&quot;World&quot;</span>

  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">hello</span></span>(name \\ <span class="hljs-variable">@default_name</span>) <span class="hljs-keyword">do</span>
    <span class="hljs-string">&quot;Hello, <span class="hljs-subst">#{name}</span>!&quot;</span>
  <span class="hljs-keyword">end</span>

  <span class="hljs-function"><span class="hljs-keyword">defp</span> <span class="hljs-title">secret</span></span>, <span class="hljs-symbol">do:</span> <span class="hljs-symbol">:ok</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Functions()
    {
        AssertHighlighter("elixir",
"""
def add(a, b), do: a + b
defp private_fun(x) when is_integer(x) and x > 0, do: x
defmacro unless(clause, do: expression) do
  quote do
    if(!unquote(clause), do: unquote(expression))
  end
end
defmacrop internal(x), do: x
def valid?(x), do: true
def update!(x), do: x
defguard is_even(value) when is_integer(value) and rem(value, 2) == 0
""",
"""
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">add</span></span>(a, b), <span class="hljs-symbol">do:</span> a + b
<span class="hljs-function"><span class="hljs-keyword">defp</span> <span class="hljs-title">private_fun</span></span>(x) <span class="hljs-keyword">when</span> is_integer(x) <span class="hljs-keyword">and</span> x &gt; <span class="hljs-number">0</span>, <span class="hljs-symbol">do:</span> x
<span class="hljs-function"><span class="hljs-keyword">defmacro</span> <span class="hljs-title">unless</span></span>(clause, <span class="hljs-symbol">do:</span> expression) <span class="hljs-keyword">do</span>
  <span class="hljs-keyword">quote</span> <span class="hljs-keyword">do</span>
    <span class="hljs-keyword">if</span>(!<span class="hljs-keyword">unquote</span>(clause), <span class="hljs-symbol">do:</span> <span class="hljs-keyword">unquote</span>(expression))
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
<span class="hljs-function"><span class="hljs-keyword">defmacrop</span> <span class="hljs-title">internal</span></span>(x), <span class="hljs-symbol">do:</span> x
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">valid?</span></span>(x), <span class="hljs-symbol">do:</span> <span class="hljs-literal">true</span>
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">update!</span></span>(x), <span class="hljs-symbol">do:</span> x
<span class="hljs-keyword">defguard</span> is_even(value) <span class="hljs-keyword">when</span> is_integer(value) <span class="hljs-keyword">and</span> rem(value, <span class="hljs-number">2</span>) == <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void DocHeredoc()
    {
        AssertHighlighter("elixir",
""""
@doc """
Returns the sum of #{inspect(a)} and b.

## Examples

    iex> add(1, 2)
    3
"""
@spec add(integer, integer) :: integer
def add(a, b), do: a + b
"""",
"""
<span class="hljs-variable">@doc</span> <span class="hljs-string">&quot;&quot;&quot;
Returns the sum of <span class="hljs-subst">#{inspect(a)}</span> and b.

## Examples

    iex&gt; add(1, 2)
    3
&quot;&quot;&quot;</span>
<span class="hljs-variable">@spec</span> add(integer, integer) :: integer
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">add</span></span>(a, b), <span class="hljs-symbol">do:</span> a + b
""");
    }

    [Fact]
    public void HeredocSingle()
    {
        AssertHighlighter("elixir",
"""
@doc '''
Charlist heredoc
'''
x = '''
abc #{1 + 2}
'''
""",
"""
<span class="hljs-variable">@doc</span> <span class="hljs-string">&#x27;&#x27;&#x27;
Charlist heredoc
&#x27;&#x27;&#x27;</span>
x = <span class="hljs-string">&#x27;&#x27;&#x27;
abc <span class="hljs-subst">#{<span class="hljs-number">1</span> + <span class="hljs-number">2</span>}</span>
&#x27;&#x27;&#x27;</span>
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("elixir",
"""
"hello"
"escape \" quote \n newline \t tab \\ backslash"
"interpolation #{user.name} and #{1 + 2}"
"nested #{"inner #{deep}"} done"
'charlist'
'char\'list #{x}'
"unicode A \x{1F600}"
""",
"""
<span class="hljs-string">&quot;hello&quot;</span>
<span class="hljs-string">&quot;escape \&quot; quote \n newline \t tab \\ backslash&quot;</span>
<span class="hljs-string">&quot;interpolation <span class="hljs-subst">#{user.name}</span> and <span class="hljs-subst">#{<span class="hljs-number">1</span> + <span class="hljs-number">2</span>}</span>&quot;</span>
<span class="hljs-string">&quot;nested <span class="hljs-subst">#{<span class="hljs-string">&quot;inner <span class="hljs-subst">#{deep}</span>&quot;</span>}</span> done&quot;</span>
<span class="hljs-string">&#x27;charlist&#x27;</span>
<span class="hljs-string">&#x27;char\&#x27;list <span class="hljs-subst">#{x}</span>&#x27;</span>
<span class="hljs-string">&quot;unicode A \x{1F600}&quot;</span>
""");
    }

    [Fact]
    public void Atoms()
    {
        AssertHighlighter("elixir",
"""
:ok
:error
:"quoted atom"
:'single quoted'
:foo?
:bar!
:+
:<<>>
:===
:[]
Foo.Bar
:"with #{interp}"
x = :atom_with_underscore
MyModule.function()
""",
"""
<span class="hljs-symbol">:ok</span>
<span class="hljs-symbol">:error</span>
<span class="hljs-symbol">:<span class="hljs-string">&quot;quoted atom&quot;</span></span>
<span class="hljs-symbol">:<span class="hljs-string">&#x27;single quoted&#x27;</span></span>
<span class="hljs-symbol">:foo?</span>
<span class="hljs-symbol">:bar!</span>
<span class="hljs-symbol">:+</span>
<span class="hljs-symbol">:&lt;&lt;&gt;&gt;</span>
<span class="hljs-symbol">:===</span>
<span class="hljs-symbol">:[]</span>
<span class="hljs-title class_">Foo</span>.<span class="hljs-title class_">Bar</span>
<span class="hljs-symbol">:<span class="hljs-string">&quot;with <span class="hljs-subst">#{interp}</span>&quot;</span></span>
x = <span class="hljs-symbol">:atom_with_underscore</span>
<span class="hljs-title class_">MyModule</span>.function()
""");
    }

    [Fact]
    public void KeywordLists()
    {
        AssertHighlighter("elixir",
"""
opts = [name: "x", age: 42, valid?: true]
map = %{a: 1, b: 2}
if x, do: y, else: z
call(foo, bar: :baz, key: "value")
%{"string" => 1, :atom => 2}
""",
"""
opts = [<span class="hljs-symbol">name:</span> <span class="hljs-string">&quot;x&quot;</span>, <span class="hljs-symbol">age:</span> <span class="hljs-number">42</span>, <span class="hljs-symbol">valid?:</span> <span class="hljs-literal">true</span>]
map = %{<span class="hljs-symbol">a:</span> <span class="hljs-number">1</span>, <span class="hljs-symbol">b:</span> <span class="hljs-number">2</span>}
<span class="hljs-keyword">if</span> x, <span class="hljs-symbol">do:</span> y, <span class="hljs-symbol">else:</span> z
call(foo, <span class="hljs-symbol">bar:</span> <span class="hljs-symbol">:baz</span>, <span class="hljs-symbol">key:</span> <span class="hljs-string">&quot;value&quot;</span>)
%{<span class="hljs-string">&quot;string&quot;</span> =&gt; <span class="hljs-number">1</span>, <span class="hljs-symbol">:atom</span> =&gt; <span class="hljs-number">2</span>}
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("elixir",
"""
42
-17
1_000_000
3.14
-0.5
1.0e10
6.02e-23
1_000.000_1
0x1F
0xFF_FF
0o755
0b1010_1010
?a
?\n
""",
"""
<span class="hljs-number">42</span>
<span class="hljs-number">-17</span>
<span class="hljs-number">1_000_000</span>
<span class="hljs-number">3.14</span>
<span class="hljs-number">-0.5</span>
<span class="hljs-number">1.0e10</span>
<span class="hljs-number">6.02e-23</span>
<span class="hljs-number">1_000.000_1</span>
<span class="hljs-number">0x1F</span>
<span class="hljs-number">0xFF_FF</span>
<span class="hljs-number">0o755</span>
<span class="hljs-number">0b1010_1010</span>
?a
?\n
""");
    }

    [Fact]
    public void Sigils()
    {
        AssertHighlighter("elixir",
"""
~r/hello\s+world/i
~r{^\d+$}
~r(foo|bar)u
~R/no #{interp}/
~s(string with #{interp} and \) escaped)
~S(no #{interp} here)
~w[foo bar baz]a
~c'charlist'
~D[2024-01-01]
~T[12:00:00]
~N[2024-01-01 12:00:00]
~U[2024-01-01 12:00:00Z]
~s<angle>
~s|pipe|
~s"quote"
~s'apos'
~s{brace}
~s[bracket]
""",
"""
<span class="hljs-regex">~r/hello<span class="hljs-char escape_">\s</span>+world/i</span>
<span class="hljs-regex">~r{^<span class="hljs-char escape_">\d</span>+$}</span>
<span class="hljs-regex">~r(foo|bar)u</span>
<span class="hljs-regex">~R/no #{interp}/</span>
<span class="hljs-string">~s(string with <span class="hljs-subst">#{interp}</span> and <span class="hljs-char escape_">\)</span> escaped)</span>
<span class="hljs-string">~S(no #{interp} here)</span>
<span class="hljs-string">~w[foo bar baz]a</span>
<span class="hljs-string">~c&#x27;charlist&#x27;</span>
<span class="hljs-string">~D[2024-01-01]</span>
<span class="hljs-string">~T[12:00:00]</span>
<span class="hljs-string">~N[2024-01-01 12:00:00]</span>
<span class="hljs-string">~U[2024-01-01 12:00:00Z]</span>
<span class="hljs-string">~s&lt;angle&gt;</span>
<span class="hljs-string">~s|pipe|</span>
<span class="hljs-string">~s&quot;quote&quot;</span>
<span class="hljs-string">~s&#x27;apos&#x27;</span>
<span class="hljs-string">~s{brace}</span>
<span class="hljs-string">~s[bracket]</span>
""");
    }

    [Fact]
    public void HeredocSigils()
    {
        AssertHighlighter("elixir",
""""
~s"""
multi-line #{interp}
sigil
"""
~S"""
raw #{not_interp}
"""
~r"""
^\d+
$
"""x
~H"""
<div class={@class}>{@name}</div>
"""
~w'''
a b c
'''
"""",
"""
<span class="hljs-string">~s&quot;&quot;&quot;
multi-line <span class="hljs-subst">#{interp}</span>
sigil
&quot;&quot;&quot;</span>
<span class="hljs-string">~S&quot;&quot;&quot;
raw #{not_interp}
&quot;&quot;&quot;</span>
<span class="hljs-regex">~r&quot;&quot;&quot;
^<span class="hljs-char escape_">\d</span>+
$
&quot;&quot;&quot;x</span>
<span class="hljs-string">~H&quot;&quot;&quot;
&lt;div class={@class}&gt;{@name}&lt;/div&gt;
&quot;&quot;&quot;</span>
<span class="hljs-string">~w&#x27;&#x27;&#x27;
a b c
&#x27;&#x27;&#x27;</span>
""");
    }

    [Fact]
    public void Pipes()
    {
        AssertHighlighter("elixir",
"""
"hello world"
|> String.split()
|> Enum.map(&String.capitalize/1)
|> Enum.join(" ")
|> IO.puts()

list
|> Enum.filter(fn x -> rem(x, 2) == 0 end)
|> Enum.reduce(0, &(&1 + &2))
""",
"""
<span class="hljs-string">&quot;hello world&quot;</span>
|&gt; <span class="hljs-title class_">String</span>.split()
|&gt; <span class="hljs-title class_">Enum</span>.map(&amp;<span class="hljs-title class_">String</span>.capitalize/<span class="hljs-number">1</span>)
|&gt; <span class="hljs-title class_">Enum</span>.join(<span class="hljs-string">&quot; &quot;</span>)
|&gt; <span class="hljs-title class_">IO</span>.puts()

list
|&gt; <span class="hljs-title class_">Enum</span>.filter(<span class="hljs-keyword">fn</span> x -&gt; rem(x, <span class="hljs-number">2</span>) == <span class="hljs-number">0</span> <span class="hljs-keyword">end</span>)
|&gt; <span class="hljs-title class_">Enum</span>.reduce(<span class="hljs-number">0</span>, &amp;(&amp;<span class="hljs-number">1</span> + &amp;<span class="hljs-number">2</span>))
""");
    }

    [Fact]
    public void ModuleAttributes()
    {
        AssertHighlighter("elixir",
"""
@behaviour GenServer
@impl true
@type t :: %__MODULE__{name: String.t(), age: non_neg_integer()}
@typep internal :: atom
@callback init(args :: term) :: {:ok, state :: term}
@compile {:inline, foo: 1}
@@not_real
$global
""",
"""
<span class="hljs-variable">@behaviour</span> <span class="hljs-title class_">GenServer</span>
<span class="hljs-variable">@impl</span> <span class="hljs-literal">true</span>
<span class="hljs-variable">@type</span> t :: %__MODULE__{<span class="hljs-symbol">name:</span> <span class="hljs-title class_">String</span>.t(), <span class="hljs-symbol">age:</span> non_neg_integer()}
<span class="hljs-variable">@typep</span> internal :: atom
<span class="hljs-variable">@callback</span> init(args :: term) :: {<span class="hljs-symbol">:ok</span>, state :: term}
<span class="hljs-variable">@compile</span> {<span class="hljs-symbol">:inline</span>, <span class="hljs-symbol">foo:</span> <span class="hljs-number">1</span>}
<span class="hljs-variable">@@not_real</span>
<span class="hljs-variable">$global</span>
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("elixir",
"""
case File.read(path) do
  {:ok, content} -> content
  {:error, reason} when reason in [:enoent] -> nil
  _ -> raise "unexpected"
end

cond do
  x > 10 -> :big
  true -> :small
end

with {:ok, a} <- fetch(:a),
     {:ok, b} <- fetch(:b) do
  a + b
else
  :error -> nil
end

try do
  risky()
rescue
  e in RuntimeError -> reraise e, __STACKTRACE__
catch
  :exit, _ -> :exited
after
  cleanup()
end

receive do
  {:msg, data} -> data
after
  1_000 -> :timeout
end

unless false, do: :yes
""",
"""
<span class="hljs-keyword">case</span> <span class="hljs-title class_">File</span>.read(path) <span class="hljs-keyword">do</span>
  {<span class="hljs-symbol">:ok</span>, content} -&gt; content
  {<span class="hljs-symbol">:error</span>, reason} <span class="hljs-keyword">when</span> reason <span class="hljs-keyword">in</span> [<span class="hljs-symbol">:enoent</span>] -&gt; <span class="hljs-literal">nil</span>
  _ -&gt; <span class="hljs-keyword">raise</span> <span class="hljs-string">&quot;unexpected&quot;</span>
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">cond</span> <span class="hljs-keyword">do</span>
  x &gt; <span class="hljs-number">10</span> -&gt; <span class="hljs-symbol">:big</span>
  <span class="hljs-literal">true</span> -&gt; <span class="hljs-symbol">:small</span>
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">with</span> {<span class="hljs-symbol">:ok</span>, a} &lt;- fetch(<span class="hljs-symbol">:a</span>),
     {<span class="hljs-symbol">:ok</span>, b} &lt;- fetch(<span class="hljs-symbol">:b</span>) <span class="hljs-keyword">do</span>
  a + b
<span class="hljs-keyword">else</span>
  <span class="hljs-symbol">:error</span> -&gt; <span class="hljs-literal">nil</span>
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">try</span> <span class="hljs-keyword">do</span>
  risky()
<span class="hljs-keyword">rescue</span>
  e <span class="hljs-keyword">in</span> <span class="hljs-title class_">RuntimeError</span> -&gt; <span class="hljs-keyword">reraise</span> e, __STACKTRACE__
<span class="hljs-keyword">catch</span>
  <span class="hljs-symbol">:exit</span>, _ -&gt; <span class="hljs-symbol">:exited</span>
<span class="hljs-keyword">after</span>
  cleanup()
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">receive</span> <span class="hljs-keyword">do</span>
  {<span class="hljs-symbol">:msg</span>, data} -&gt; data
<span class="hljs-keyword">after</span>
  <span class="hljs-number">1_000</span> -&gt; <span class="hljs-symbol">:timeout</span>
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">unless</span> <span class="hljs-literal">false</span>, <span class="hljs-symbol">do:</span> <span class="hljs-symbol">:yes</span>
""");
    }

    [Fact]
    public void Comprehension()
    {
        AssertHighlighter("elixir",
"""
for x <- 1..10, rem(x, 2) == 0, into: %{}, do: {x, x * x}
for <<c <- "hello">>, c in ?a..?z, do: <<c + 1>>
""",
"""
<span class="hljs-keyword">for</span> x &lt;- <span class="hljs-number">1</span>..<span class="hljs-number">10</span>, rem(x, <span class="hljs-number">2</span>) == <span class="hljs-number">0</span>, <span class="hljs-symbol">into:</span> %{}, <span class="hljs-symbol">do:</span> {x, x * x}
<span class="hljs-keyword">for</span> &lt;&lt;c &lt;- <span class="hljs-string">&quot;hello&quot;</span>&gt;&gt;, c <span class="hljs-keyword">in</span> ?a..?z, <span class="hljs-symbol">do:</span> &lt;&lt;c + <span class="hljs-number">1</span>&gt;&gt;
""");
    }

    [Fact]
    public void Structs()
    {
        AssertHighlighter("elixir",
"""
defmodule User do
  defstruct name: nil, age: 0, email: ""
  @enforce_keys [:name]
end

user = %User{name: "Jane"}
%User{user | age: 30}
%{user | name: "Bob"}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">defmodule</span> <span class="hljs-title">User</span></span> <span class="hljs-keyword">do</span>
  <span class="hljs-keyword">defstruct</span> <span class="hljs-symbol">name:</span> <span class="hljs-literal">nil</span>, <span class="hljs-symbol">age:</span> <span class="hljs-number">0</span>, <span class="hljs-symbol">email:</span> <span class="hljs-string">&quot;&quot;</span>
  <span class="hljs-variable">@enforce_keys</span> [<span class="hljs-symbol">:name</span>]
<span class="hljs-keyword">end</span>

user = %<span class="hljs-title class_">User</span>{<span class="hljs-symbol">name:</span> <span class="hljs-string">&quot;Jane&quot;</span>}
%<span class="hljs-title class_">User</span>{user | <span class="hljs-symbol">age:</span> <span class="hljs-number">30</span>}
%{user | <span class="hljs-symbol">name:</span> <span class="hljs-string">&quot;Bob&quot;</span>}
""");
    }

    [Fact]
    public void Protocols()
    {
        AssertHighlighter("elixir",
"""
defprotocol Size do
  @doc "Calculates the size"
  def size(data)
end

defimpl Size, for: BitString do
  def size(string), do: byte_size(string)
end

defimpl Size, for: Map do
  def size(map), do: map_size(map)
end
""",
"""
<span class="hljs-class"><span class="hljs-keyword">defprotocol</span> <span class="hljs-title">Size</span></span> <span class="hljs-keyword">do</span>
  <span class="hljs-variable">@doc</span> <span class="hljs-string">&quot;Calculates the size&quot;</span>
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">size</span></span>(data)
<span class="hljs-keyword">end</span>

<span class="hljs-class"><span class="hljs-keyword">defimpl</span> <span class="hljs-title">Size</span></span>, <span class="hljs-symbol">for:</span> <span class="hljs-title class_">BitString</span> <span class="hljs-keyword">do</span>
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">size</span></span>(string), <span class="hljs-symbol">do:</span> byte_size(string)
<span class="hljs-keyword">end</span>

<span class="hljs-class"><span class="hljs-keyword">defimpl</span> <span class="hljs-title">Size</span></span>, <span class="hljs-symbol">for:</span> <span class="hljs-title class_">Map</span> <span class="hljs-keyword">do</span>
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">size</span></span>(map), <span class="hljs-symbol">do:</span> map_size(map)
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void AliasImport()
    {
        AssertHighlighter("elixir",
"""
alias MyApp.{Repo, User}
import Ecto.Query, only: [from: 2]
require Logger
use GenServer, restart: :transient
Logger.info("started")
""",
"""
<span class="hljs-keyword">alias</span> <span class="hljs-title class_">MyApp</span>.{<span class="hljs-title class_">Repo</span>, <span class="hljs-title class_">User</span>}
<span class="hljs-keyword">import</span> <span class="hljs-title class_">Ecto</span>.<span class="hljs-title class_">Query</span>, <span class="hljs-symbol">only:</span> [<span class="hljs-symbol">from:</span> <span class="hljs-number">2</span>]
<span class="hljs-keyword">require</span> <span class="hljs-title class_">Logger</span>
<span class="hljs-keyword">use</span> <span class="hljs-title class_">GenServer</span>, <span class="hljs-symbol">restart:</span> <span class="hljs-symbol">:transient</span>
<span class="hljs-title class_">Logger</span>.info(<span class="hljs-string">&quot;started&quot;</span>)
""");
    }

    [Fact]
    public void AnonymousFunctions()
    {
        AssertHighlighter("elixir",
"""
add = fn a, b -> a + b end
add.(1, 2)
square = &(&1 * &1)
Enum.map([1, 2, 3], &Integer.to_string/1)
fun = fn
  {:ok, v} -> v
  :error -> nil
end
""",
"""
add = <span class="hljs-keyword">fn</span> a, b -&gt; a + b <span class="hljs-keyword">end</span>
add.(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>)
square = &amp;(&amp;<span class="hljs-number">1</span> * &amp;<span class="hljs-number">1</span>)
<span class="hljs-title class_">Enum</span>.map([<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>], &amp;<span class="hljs-title class_">Integer</span>.to_string/<span class="hljs-number">1</span>)
fun = <span class="hljs-keyword">fn</span>
  {<span class="hljs-symbol">:ok</span>, v} -&gt; v
  <span class="hljs-symbol">:error</span> -&gt; <span class="hljs-literal">nil</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Binaries()
    {
        AssertHighlighter("elixir",
"""
<<1, 2, 3>>
<<head::binary-size(4), rest::binary>> = data
<<x::8, y::16-little>>
<<"abc", 0>>
""",
"""
&lt;&lt;<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>&gt;&gt;
&lt;&lt;head::binary-size(<span class="hljs-number">4</span>), rest::binary&gt;&gt; = data
&lt;&lt;x::<span class="hljs-number">8</span>, y::<span class="hljs-number">16</span>-little&gt;&gt;
&lt;&lt;<span class="hljs-string">&quot;abc&quot;</span>, <span class="hljs-number">0</span>&gt;&gt;
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("elixir",
"""
# A comment
x = 1 # trailing comment
# TODO: fix this
# "not a string" and :not_atom
"# not a comment"
""",
"""
<span class="hljs-comment"># A comment</span>
x = <span class="hljs-number">1</span> <span class="hljs-comment"># trailing comment</span>
<span class="hljs-comment"># <span class="hljs-doctag">TODO:</span> fix this</span>
<span class="hljs-comment"># &quot;not a string&quot; and :not_atom</span>
<span class="hljs-string">&quot;# not a comment&quot;</span>
""");
    }

    [Fact]
    public void GenServer()
    {
        AssertHighlighter("elixir",
"""
defmodule Stack do
  use GenServer

  # Client

  def start_link(default) when is_list(default) do
    GenServer.start_link(__MODULE__, default)
  end

  # Server (callbacks)

  @impl true
  def init(stack) do
    {:ok, stack}
  end

  @impl true
  def handle_call(:pop, _from, [head | tail]) do
    {:reply, head, tail}
  end

  @impl true
  def handle_cast({:push, element}, state) do
    {:noreply, [element | state]}
  end
end
""",
"""
<span class="hljs-class"><span class="hljs-keyword">defmodule</span> <span class="hljs-title">Stack</span></span> <span class="hljs-keyword">do</span>
  <span class="hljs-keyword">use</span> <span class="hljs-title class_">GenServer</span>

  <span class="hljs-comment"># Client</span>

  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">start_link</span></span>(default) <span class="hljs-keyword">when</span> is_list(default) <span class="hljs-keyword">do</span>
    <span class="hljs-title class_">GenServer</span>.start_link(__MODULE__, default)
  <span class="hljs-keyword">end</span>

  <span class="hljs-comment"># Server (callbacks)</span>

  <span class="hljs-variable">@impl</span> <span class="hljs-literal">true</span>
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">init</span></span>(stack) <span class="hljs-keyword">do</span>
    {<span class="hljs-symbol">:ok</span>, stack}
  <span class="hljs-keyword">end</span>

  <span class="hljs-variable">@impl</span> <span class="hljs-literal">true</span>
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">handle_call</span></span>(<span class="hljs-symbol">:pop</span>, _from, [head | tail]) <span class="hljs-keyword">do</span>
    {<span class="hljs-symbol">:reply</span>, head, tail}
  <span class="hljs-keyword">end</span>

  <span class="hljs-variable">@impl</span> <span class="hljs-literal">true</span>
  <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">handle_cast</span></span>({<span class="hljs-symbol">:push</span>, element}, state) <span class="hljs-keyword">do</span>
    {<span class="hljs-symbol">:noreply</span>, [element | state]}
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("elixir",
"""
a ++ b -- c
a <> b
a == b and c != d or not e
a === b
a =~ ~r/x/
x in [1, 2]
a |> b
a && b || c
!a
^pinned = value
a..b//2
a <~> b
x::integer
""",
"""
a ++ b -- c
a &lt;&gt; b
a == b <span class="hljs-keyword">and</span> c != d <span class="hljs-keyword">or</span> <span class="hljs-keyword">not</span> e
a === b
a =~ <span class="hljs-regex">~r/x/</span>
x <span class="hljs-keyword">in</span> [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>]
a |&gt; b
a &amp;&amp; b || c
!a
^pinned = value
a..b//<span class="hljs-number">2</span>
a &lt;~&gt; b
x::integer
""");
    }

    [Fact]
    public void OneLineModule()
    {
        AssertHighlighter("elixir",
"""
defmodule Foo, do: def bar, do: :baz
""",
"""
<span class="hljs-class"><span class="hljs-keyword">defmodule</span> <span class="hljs-title">Foo</span></span>, <span class="hljs-symbol">do:</span> <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">bar</span></span>, <span class="hljs-symbol">do:</span> <span class="hljs-symbol">:baz</span>
""");
    }

    [Fact]
    public void ModuleSemicolon()
    {
        AssertHighlighter("elixir",
"""
defmodule A; def x, do: 1
""",
"""
<span class="hljs-class"><span class="hljs-keyword">defmodule</span> <span class="hljs-title">A</span></span>; <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">x</span></span>, <span class="hljs-symbol">do:</span> <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void DefNoParens()
    {
        AssertHighlighter("elixir",
"""
def zero, do: 0
def hello name do
  name
end
""",
"""
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">zero</span></span>, <span class="hljs-symbol">do:</span> <span class="hljs-number">0</span>
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">hello</span></span> name <span class="hljs-keyword">do</span>
  name
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void NestedModules()
    {
        AssertHighlighter("elixir",
"""
defmodule Outer do
  defmodule Inner do
    def f, do: Outer.Inner.g()
  end
end
""",
"""
<span class="hljs-class"><span class="hljs-keyword">defmodule</span> <span class="hljs-title">Outer</span></span> <span class="hljs-keyword">do</span>
  <span class="hljs-class"><span class="hljs-keyword">defmodule</span> <span class="hljs-title">Inner</span></span> <span class="hljs-keyword">do</span>
    <span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">f</span></span>, <span class="hljs-symbol">do:</span> <span class="hljs-title class_">Outer</span>.<span class="hljs-title class_">Inner</span>.g()
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Exceptions()
    {
        AssertHighlighter("elixir",
"""
defmodule MyError do
  defexception message: "default message"
end
raise MyError, message: "custom"
raise ArgumentError
throw :value
exit(:normal)
""",
"""
<span class="hljs-class"><span class="hljs-keyword">defmodule</span> <span class="hljs-title">MyError</span></span> <span class="hljs-keyword">do</span>
  <span class="hljs-keyword">defexception</span> <span class="hljs-symbol">message:</span> <span class="hljs-string">&quot;default message&quot;</span>
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">raise</span> <span class="hljs-title class_">MyError</span>, <span class="hljs-symbol">message:</span> <span class="hljs-string">&quot;custom&quot;</span>
<span class="hljs-keyword">raise</span> <span class="hljs-title class_">ArgumentError</span>
throw <span class="hljs-symbol">:value</span>
exit(<span class="hljs-symbol">:normal</span>)
""");
    }

    [Fact]
    public void Doctest()
    {
        AssertHighlighter("elixir",
""""
@doc ~S"""
Converts a string.

    iex> MyModule.convert("#{x}")
    "x"
"""
"""",
"""
<span class="hljs-variable">@doc</span> <span class="hljs-string">~S&quot;&quot;&quot;
Converts a string.

    iex&gt; MyModule.convert(&quot;#{x}&quot;)
    &quot;x&quot;
&quot;&quot;&quot;</span>
""");
    }

    [Fact]
    public void ProcessDict()
    {
        AssertHighlighter("elixir",
"""
send(self(), {:hello, "world"})
pid = spawn(fn -> IO.puts("hi") end)
Process.flag(:trap_exit, true)
Task.async(fn -> 1 end) |> Task.await()
""",
"""
send(self(), {<span class="hljs-symbol">:hello</span>, <span class="hljs-string">&quot;world&quot;</span>})
pid = spawn(<span class="hljs-keyword">fn</span> -&gt; <span class="hljs-title class_">IO</span>.puts(<span class="hljs-string">&quot;hi&quot;</span>) <span class="hljs-keyword">end</span>)
<span class="hljs-title class_">Process</span>.flag(<span class="hljs-symbol">:trap_exit</span>, <span class="hljs-literal">true</span>)
<span class="hljs-title class_">Task</span>.async(<span class="hljs-keyword">fn</span> -&gt; <span class="hljs-number">1</span> <span class="hljs-keyword">end</span>) |&gt; <span class="hljs-title class_">Task</span>.await()
""");
    }

    [Fact]
    public void EscapeEdge()
    {
        AssertHighlighter("elixir",
"""
"a\\"
"tab\there"
'it\'s'
~s(a\(b\)c)
~r/a\/b/
~w|a\|b|
""",
"""
<span class="hljs-string">&quot;a\\&quot;</span>
<span class="hljs-string">&quot;tab\there&quot;</span>
<span class="hljs-string">&#x27;it\&#x27;s&#x27;</span>
<span class="hljs-string">~s(a<span class="hljs-char escape_">\(</span>b<span class="hljs-char escape_">\)</span>c)</span>
<span class="hljs-regex">~r/a<span class="hljs-char escape_">\/</span>b/</span>
<span class="hljs-string">~w|a<span class="hljs-char escape_">\|</span>b|</span>
""");
    }

    [Fact]
    public void Typespecs()
    {
        AssertHighlighter("elixir",
"""
@spec fetch(map(), key :: atom()) :: {:ok, term()} | :error
@type option :: {:timeout, timeout()} | {:name, atom()}
@opaque t :: %__MODULE__{}
""",
"""
<span class="hljs-variable">@spec</span> fetch(map(), key :: atom()) :: {<span class="hljs-symbol">:ok</span>, term()} | <span class="hljs-symbol">:error</span>
<span class="hljs-variable">@type</span> option :: {<span class="hljs-symbol">:timeout</span>, timeout()} | {<span class="hljs-symbol">:name</span>, atom()}
<span class="hljs-variable">@opaque</span> t :: %__MODULE__{}
""");
    }

    [Fact]
    public void VariablesUnderscore()
    {
        AssertHighlighter("elixir",
"""
_ignored = 1
_ = compute()
__MODULE__
__ENV__.file
__DIR__
x1 = y_2
""",
"""
_ignored = <span class="hljs-number">1</span>
_ = compute()
__MODULE__
__ENV__.file
__DIR__
x1 = y_2
""");
    }

    [Fact]
    public void UnterminatedString()
    {
        AssertHighlighter("elixir",
"""
x = "unterminated
y = 1
""",
"""
x = <span class="hljs-string">&quot;unterminated
y = 1</span>
""");
    }

    [Fact]
    public void HeexWithQuotes()
    {
        AssertHighlighter("elixir",
""""
def render(assigns) do
  ~H"""
  <div class="card" id={@id}>
    <p>Hello "#{@name}"</p>
  </div>
  """
end
"""",
"""
<span class="hljs-function"><span class="hljs-keyword">def</span> <span class="hljs-title">render</span></span>(assigns) <span class="hljs-keyword">do</span>
  <span class="hljs-string">~H&quot;&quot;&quot;
  &lt;div class=&quot;card&quot; id={@id}&gt;
    &lt;p&gt;Hello &quot;#{@name}&quot;&lt;/p&gt;
  &lt;/div&gt;
  &quot;&quot;&quot;</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void SigilHeredocQuotes()
    {
        AssertHighlighter("elixir",
""""
~s"""
He said "hi" and #{name}
"""
~S'''
it's 'quoted'
'''
"""",
"""
<span class="hljs-string">~s&quot;&quot;&quot;
He said &quot;hi&quot; and <span class="hljs-subst">#{name}</span>
&quot;&quot;&quot;</span>
<span class="hljs-string">~S&#x27;&#x27;&#x27;
it&#x27;s &#x27;quoted&#x27;
&#x27;&#x27;&#x27;</span>
""");
    }

    [Fact]
    public void SigilModifiers()
    {
        AssertHighlighter("elixir",
"""
~w(foo bar)a
~w[one two]c
~W(a b)s
~r/abc/iu
~s(text)
""",
"""
<span class="hljs-string">~w(foo bar)a</span>
<span class="hljs-string">~w[one two]c</span>
<span class="hljs-string">~W(a b)s</span>
<span class="hljs-regex">~r/abc/iu</span>
<span class="hljs-string">~s(text)</span>
""");
    }

    [Fact]
    public void DefinitionsExtra()
    {
        AssertHighlighter("elixir",
"""
defmodule MyError do
  defexception [:message]
  defoverridable init: 1
  defdelegate size(map), to: Map
  defguardp is_pos(x) when x > 0
end
""",
"""
<span class="hljs-class"><span class="hljs-keyword">defmodule</span> <span class="hljs-title">MyError</span></span> <span class="hljs-keyword">do</span>
  <span class="hljs-keyword">defexception</span> [<span class="hljs-symbol">:message</span>]
  <span class="hljs-keyword">defoverridable</span> <span class="hljs-symbol">init:</span> <span class="hljs-number">1</span>
  <span class="hljs-function"><span class="hljs-keyword">defdelegate</span> <span class="hljs-title">size</span></span>(map), <span class="hljs-symbol">to:</span> <span class="hljs-title class_">Map</span>
  <span class="hljs-keyword">defguardp</span> is_pos(x) <span class="hljs-keyword">when</span> x &gt; <span class="hljs-number">0</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void KeywordLikeIdents()
    {
        AssertHighlighter("elixir",
"""
do_something()
end_time = 1
if_true = 2
Enum.end
foo.do
MyApp.Do.run()
""",
"""
do_something()
end_time = <span class="hljs-number">1</span>
if_true = <span class="hljs-number">2</span>
<span class="hljs-title class_">Enum</span>.<span class="hljs-keyword">end</span>
foo.do
<span class="hljs-title class_">MyApp</span>.<span class="hljs-title class_">Do</span>.run()
""");
    }
}
