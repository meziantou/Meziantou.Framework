namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class JuliaHighlighterTests
{
    [Fact]
    public void Functions()
    {
        AssertHighlighter("julia",
"""
function greet(name::String)
    println("Hello, $name!")
end

add(x, y) = x + y

function mysum(xs::AbstractVector{T}) where {T<:Number}
    s = zero(T)
    for x in xs
        s += x
    end
    return s
end
""",
"""
<span class="hljs-keyword">function</span> greet(name::<span class="hljs-built_in">String</span>)
    println(<span class="hljs-string">&quot;Hello, <span class="hljs-variable">$name</span>!&quot;</span>)
<span class="hljs-keyword">end</span>

add(x, y) = x + y

<span class="hljs-keyword">function</span> mysum(xs::<span class="hljs-built_in">AbstractVector</span>{T}) <span class="hljs-keyword">where</span> {T&lt;:<span class="hljs-built_in">Number</span>}
    s = zero(T)
    <span class="hljs-keyword">for</span> x <span class="hljs-keyword">in</span> xs
        s += x
    <span class="hljs-keyword">end</span>
    <span class="hljs-keyword">return</span> s
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Types()
    {
        AssertHighlighter("julia",
"""
abstract type Shape end

struct Circle <: Shape
    radius::Float64
end

mutable struct Point{T}
    x::T
    y::T
end

primitive type MyInt8 8 end

const Vec2 = Tuple{Float64, Float64}
""",
"""
<span class="hljs-keyword">abstract type</span> Shape <span class="hljs-keyword">end</span>

<span class="hljs-keyword">struct</span> Circle &lt;: Shape
    radius::<span class="hljs-built_in">Float64</span>
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">mutable struct</span> Point{T}
    x::T
    y::T
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">primitive type</span> MyInt8 <span class="hljs-number">8</span> <span class="hljs-keyword">end</span>

<span class="hljs-keyword">const</span> Vec2 = <span class="hljs-built_in">Tuple</span>{<span class="hljs-built_in">Float64</span>, <span class="hljs-built_in">Float64</span>}
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("julia",
"""
a = 42
b = 1_000_000
c = 3.14
d = .5
e = 1.
f = 1.5e10
g = 2.5e-3
h = 1.5f0
i = 0x1F
j = 0b1010
k = 0o777
l = 0x1p0
m = 0x1.8p3
n = 2im
o = 3.0 + 4.0im
""",
"""
a = <span class="hljs-number">42</span>
b = <span class="hljs-number">1_000_000</span>
c = <span class="hljs-number">3.14</span>
d = <span class="hljs-number">.5</span>
e = <span class="hljs-number">1.</span>
f = <span class="hljs-number">1.5e10</span>
g = <span class="hljs-number">2.5e-3</span>
h = <span class="hljs-number">1.5f0</span>
i = <span class="hljs-number">0x1F</span>
j = <span class="hljs-number">0b1010</span>
k = <span class="hljs-number">0o777</span>
l = <span class="hljs-number">0x1p0</span>
m = <span class="hljs-number">0x1.8p3</span>
n = <span class="hljs-number">2</span><span class="hljs-literal">im</span>
o = <span class="hljs-number">3.0</span> + <span class="hljs-number">4.0</span><span class="hljs-literal">im</span>
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("julia",
""""
s1 = "hello"
s2 = "escape \" quote \n newline \t tab"
s3 = "interpolation $name and $(x + 1)"
s4 = """
Multi-line
string with $var and "quotes"
"""
s5 = raw"no \escape $here"
s6 = r"^\d+$"i
s7 = b"bytes"
s8 = v"1.2.3"
s9 = "nested $(join(["a", "b"], ", "))"
"""",
"""
s1 = <span class="hljs-string">&quot;hello&quot;</span>
s2 = <span class="hljs-string">&quot;escape \&quot; quote \n newline \t tab&quot;</span>
s3 = <span class="hljs-string">&quot;interpolation <span class="hljs-variable">$name</span> and <span class="hljs-subst">$(x + <span class="hljs-number">1</span>)</span>&quot;</span>
s4 = <span class="hljs-string">&quot;&quot;&quot;
Multi-line
string with <span class="hljs-variable">$var</span> and &quot;quotes&quot;
&quot;&quot;&quot;</span>
s5 = <span class="hljs-string">raw&quot;no \escape <span class="hljs-variable">$here</span>&quot;</span>
s6 = <span class="hljs-string">r&quot;^\d+$&quot;i</span>
s7 = <span class="hljs-string">b&quot;bytes&quot;</span>
s8 = <span class="hljs-string">v&quot;1.2.3&quot;</span>
s9 = <span class="hljs-string">&quot;nested <span class="hljs-subst">$(join([<span class="hljs-string">&quot;a&quot;</span>, <span class="hljs-string">&quot;b&quot;</span>], <span class="hljs-string">&quot;, &quot;</span>))</span>&quot;</span>
""");
    }

    [Fact]
    public void Chars()
    {
        AssertHighlighter("julia",
"""
c1 = 'a'
c2 = '\n'
c3 = '\''
c4 = '\u00e9'
c6 = 'é'
c5 = 'π'
transpose = A'
product = A' * B'
""",
"""
c1 = <span class="hljs-string">&#x27;a&#x27;</span>
c2 = <span class="hljs-string">&#x27;\n&#x27;</span>
c3 = <span class="hljs-string">&#x27;\&#x27;&#x27;</span>
c4 = <span class="hljs-string">&#x27;\u00e9&#x27;</span>
c6 = <span class="hljs-string">&#x27;é&#x27;</span>
c5 = <span class="hljs-string">&#x27;π&#x27;</span>
transpose = A&#x27;
product = A&#x27; * B&#x27;
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("julia",
"""
# A line comment
x = 1 # trailing comment
#= A block
   comment =#
#= inline =# y = 2
# TODO: fix
""",
"""
<span class="hljs-comment"># A line comment</span>
x = <span class="hljs-number">1</span> <span class="hljs-comment"># trailing comment</span>
<span class="hljs-comment">#= A block
   comment =#</span>
<span class="hljs-comment">#= inline =#</span> y = <span class="hljs-number">2</span>
<span class="hljs-comment"># TODO: fix</span>
""");
    }

    [Fact]
    public void Macros()
    {
        AssertHighlighter("julia",
"""
@time sum(1:100)
@assert x > 0 "x must be positive"
@inbounds for i in 1:n
    a[i] = b[i]
end
@show x y
@. y = a * x + b
@testset "Arithmetic" begin
    @test 1 + 1 == 2
end
""",
"""
<span class="hljs-meta">@time</span> sum(<span class="hljs-number">1</span>:<span class="hljs-number">100</span>)
<span class="hljs-meta">@assert</span> x &gt; <span class="hljs-number">0</span> <span class="hljs-string">&quot;x must be positive&quot;</span>
<span class="hljs-meta">@inbounds</span> <span class="hljs-keyword">for</span> i <span class="hljs-keyword">in</span> <span class="hljs-number">1</span>:n
    a[i] = b[i]
<span class="hljs-keyword">end</span>
<span class="hljs-meta">@show</span> x y
<span class="hljs-meta">@.</span> y = a * x + b
<span class="hljs-meta">@testset</span> <span class="hljs-string">&quot;Arithmetic&quot;</span> <span class="hljs-keyword">begin</span>
    <span class="hljs-meta">@test</span> <span class="hljs-number">1</span> + <span class="hljs-number">1</span> == <span class="hljs-number">2</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("julia",
"""
if x > 0
    println("positive")
elseif x < 0
    println("negative")
else
    println("zero")
end

while i <= 10
    i += 1
    i == 5 && continue
    i == 8 && break
end

result = x > 0 ? "pos" : "non-pos"
""",
"""
<span class="hljs-keyword">if</span> x &gt; <span class="hljs-number">0</span>
    println(<span class="hljs-string">&quot;positive&quot;</span>)
<span class="hljs-keyword">elseif</span> x &lt; <span class="hljs-number">0</span>
    println(<span class="hljs-string">&quot;negative&quot;</span>)
<span class="hljs-keyword">else</span>
    println(<span class="hljs-string">&quot;zero&quot;</span>)
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">while</span> i &lt;= <span class="hljs-number">10</span>
    i += <span class="hljs-number">1</span>
    i == <span class="hljs-number">5</span> &amp;&amp; <span class="hljs-keyword">continue</span>
    i == <span class="hljs-number">8</span> &amp;&amp; <span class="hljs-keyword">break</span>
<span class="hljs-keyword">end</span>

result = x &gt; <span class="hljs-number">0</span> ? <span class="hljs-string">&quot;pos&quot;</span> : <span class="hljs-string">&quot;non-pos&quot;</span>
""");
    }

    [Fact]
    public void TryCatch()
    {
        AssertHighlighter("julia",
"""
try
    error("oops")
catch e
    @warn "caught" exception=e
finally
    cleanup()
end
""",
"""
<span class="hljs-keyword">try</span>
    error(<span class="hljs-string">&quot;oops&quot;</span>)
<span class="hljs-keyword">catch</span> e
    <span class="hljs-meta">@warn</span> <span class="hljs-string">&quot;caught&quot;</span> exception=e
<span class="hljs-keyword">finally</span>
    cleanup()
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Modules()
    {
        AssertHighlighter("julia",
"""
module MyModule

using LinearAlgebra
using Statistics: mean, std
import Base: show, +
export myfunc

baremodule Bare
end

end # module
""",
"""
<span class="hljs-keyword">module</span> MyModule

<span class="hljs-keyword">using</span> LinearAlgebra
<span class="hljs-keyword">using</span> Statistics: mean, std
<span class="hljs-keyword">import</span> Base: show, +
<span class="hljs-keyword">export</span> myfunc

<span class="hljs-keyword">baremodule</span> Bare
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">end</span> <span class="hljs-comment"># module</span>
""");
    }

    [Fact]
    public void Literals()
    {
        AssertHighlighter("julia",
"""
a = nothing
b = missing
c = true && false
d = Inf
e = NaN
f = π
g = ℯ
h = pi * 2
i = stdout
j = VERSION
""",
"""
a = <span class="hljs-literal">nothing</span>
b = <span class="hljs-literal">missing</span>
c = <span class="hljs-literal">true</span> &amp;&amp; <span class="hljs-literal">false</span>
d = <span class="hljs-literal">Inf</span>
e = <span class="hljs-literal">NaN</span>
f = <span class="hljs-literal">π</span>
g = <span class="hljs-literal">ℯ</span>
h = <span class="hljs-literal">pi</span> * <span class="hljs-number">2</span>
i = <span class="hljs-literal">stdout</span>
j = <span class="hljs-literal">VERSION</span>
""");
    }

    [Fact]
    public void UnicodeIdentifiers()
    {
        AssertHighlighter("julia",
"""
α = 0.5
β₁ = 2α
δx = x - x₀
∑ = sum
ϵ = 1e-10
function Δ(x)
    x^2
end
""",
"""
α = <span class="hljs-number">0.5</span>
β₁ = <span class="hljs-number">2</span>α
δx = x - x₀
∑ = sum
ϵ = <span class="hljs-number">1e-10</span>
<span class="hljs-keyword">function</span> Δ(x)
    x^<span class="hljs-number">2</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Broadcasting()
    {
        AssertHighlighter("julia",
"""
y = sin.(x) .+ cos.(x)
z = x .* y
A .= 0
f.(xs; kw=1)
""",
"""
y = sin.(x) .+ cos.(x)
z = x .* y
A .= <span class="hljs-number">0</span>
f.(xs; kw=<span class="hljs-number">1</span>)
""");
    }

    [Fact]
    public void DoBlocks()
    {
        AssertHighlighter("julia",
"""
map(1:10) do x
    x^2
end

open("file.txt", "w") do io
    write(io, "data")
end
""",
"""
map(<span class="hljs-number">1</span>:<span class="hljs-number">10</span>) <span class="hljs-keyword">do</span> x
    x^<span class="hljs-number">2</span>
<span class="hljs-keyword">end</span>

open(<span class="hljs-string">&quot;file.txt&quot;</span>, <span class="hljs-string">&quot;w&quot;</span>) <span class="hljs-keyword">do</span> io
    write(io, <span class="hljs-string">&quot;data&quot;</span>)
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Comprehensions()
    {
        AssertHighlighter("julia",
"""
squares = [x^2 for x in 1:10 if iseven(x)]
d = Dict(k => v for (k, v) in zip(ks, vs))
gen = (i * j for i in 1:3, j in 1:3)
""",
"""
squares = [x^<span class="hljs-number">2</span> <span class="hljs-keyword">for</span> x <span class="hljs-keyword">in</span> <span class="hljs-number">1</span>:<span class="hljs-number">10</span> <span class="hljs-keyword">if</span> iseven(x)]
d = <span class="hljs-built_in">Dict</span>(k =&gt; v <span class="hljs-keyword">for</span> (k, v) <span class="hljs-keyword">in</span> zip(ks, vs))
gen = (i * j <span class="hljs-keyword">for</span> i <span class="hljs-keyword">in</span> <span class="hljs-number">1</span>:<span class="hljs-number">3</span>, j <span class="hljs-keyword">in</span> <span class="hljs-number">1</span>:<span class="hljs-number">3</span>)
""");
    }

    [Fact]
    public void Commands()
    {
        AssertHighlighter("julia",
"""
run(`ls -la $dir`)
cmd = `echo "hello"`
""",
"""
run(<span class="hljs-string">`ls -la <span class="hljs-variable">$dir</span>`</span>)
cmd = <span class="hljs-string">`echo &quot;hello&quot;`</span>
""");
    }

    [Fact]
    public void MultipleDispatch()
    {
        AssertHighlighter("julia",
"""
area(c::Circle) = π * c.radius^2
area(r::Rectangle) = r.width * r.height
Base.show(io::IO, p::Point) = print(io, "Point($(p.x), $(p.y))")
function (p::Polynomial)(x)
    sum(c * x^(i-1) for (i, c) in enumerate(p.coeffs))
end
""",
"""
area(c::Circle) = <span class="hljs-literal">π</span> * c.radius^<span class="hljs-number">2</span>
area(r::Rectangle) = r.width * r.height
Base.show(io::<span class="hljs-built_in">IO</span>, p::Point) = print(io, <span class="hljs-string">&quot;Point(<span class="hljs-subst">$(p.x)</span>, <span class="hljs-subst">$(p.y)</span>)&quot;</span>)
<span class="hljs-keyword">function</span> (p::Polynomial)(x)
    sum(c * x^(i-<span class="hljs-number">1</span>) <span class="hljs-keyword">for</span> (i, c) <span class="hljs-keyword">in</span> enumerate(p.coeffs))
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void TypeParams()
    {
        AssertHighlighter("julia",
"""
f(x::Vector{<:Real}) = sum(x)
g(::Type{T}) where T = T
h(x::Union{Int, Nothing}) = x
struct Wrapper{T <: AbstractFloat}
    value::T
end
""",
"""
f(x::<span class="hljs-built_in">Vector</span>{&lt;:<span class="hljs-built_in">Real</span>}) = sum(x)
g(::<span class="hljs-built_in">Type</span>{T}) <span class="hljs-keyword">where</span> T = T
h(x::<span class="hljs-built_in">Union</span>{<span class="hljs-built_in">Int</span>, <span class="hljs-built_in">Nothing</span>}) = x
<span class="hljs-keyword">struct</span> Wrapper{T &lt;: <span class="hljs-built_in">AbstractFloat</span>}
    value::T
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void LetQuote()
    {
        AssertHighlighter("julia",
"""
let a = 1, b = 2
    a + b
end

ex = quote
    x = 1
    x + 1
end
sym = :symbol
expr = :(a + b)
""",
"""
<span class="hljs-keyword">let</span> a = <span class="hljs-number">1</span>, b = <span class="hljs-number">2</span>
    a + b
<span class="hljs-keyword">end</span>

ex = <span class="hljs-keyword">quote</span>
    x = <span class="hljs-number">1</span>
    x + <span class="hljs-number">1</span>
<span class="hljs-keyword">end</span>
sym = :symbol
expr = :(a + b)
""");
    }

    [Fact]
    public void KeywordArgs()
    {
        AssertHighlighter("julia",
"""
function plot(x, y; color="red", width::Int=2, kwargs...)
    nothing
end
plot(1, 2; color="blue")
""",
"""
<span class="hljs-keyword">function</span> plot(x, y; color=<span class="hljs-string">&quot;red&quot;</span>, width::<span class="hljs-built_in">Int</span>=<span class="hljs-number">2</span>, kwargs...)
    <span class="hljs-literal">nothing</span>
<span class="hljs-keyword">end</span>
plot(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>; color=<span class="hljs-string">&quot;blue&quot;</span>)
""");
    }

    [Fact]
    public void RangesIndexing()
    {
        AssertHighlighter("julia",
"""
a[1:end]
a[begin:end-1]
a[2:2:end]
b = collect(1:0.5:3)
v[end]
""",
"""
a[<span class="hljs-number">1</span>:<span class="hljs-keyword">end</span>]
a[<span class="hljs-keyword">begin</span>:<span class="hljs-keyword">end</span>-<span class="hljs-number">1</span>]
a[<span class="hljs-number">2</span>:<span class="hljs-number">2</span>:<span class="hljs-keyword">end</span>]
b = collect(<span class="hljs-number">1</span>:<span class="hljs-number">0.5</span>:<span class="hljs-number">3</span>)
v[<span class="hljs-keyword">end</span>]
""");
    }

    [Fact]
    public void LocalGlobal()
    {
        AssertHighlighter("julia",
"""
global counter = 0
function inc()
    global counter
    local tmp = counter
    counter = tmp + 1
end
""",
"""
<span class="hljs-keyword">global</span> counter = <span class="hljs-number">0</span>
<span class="hljs-keyword">function</span> inc()
    <span class="hljs-keyword">global</span> counter
    <span class="hljs-keyword">local</span> tmp = counter
    counter = tmp + <span class="hljs-number">1</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void CcallExample()
    {
        AssertHighlighter("julia",
"""
t = ccall(:clock, Int32, ())
ccall((:strlen, "libc"), Csize_t, (Cstring,), "hello")
""",
"""
t = <span class="hljs-keyword">ccall</span>(:clock, <span class="hljs-built_in">Int32</span>, ())
<span class="hljs-keyword">ccall</span>((:strlen, <span class="hljs-string">&quot;libc&quot;</span>), <span class="hljs-built_in">Csize_t</span>, (<span class="hljs-built_in">Cstring</span>,), <span class="hljs-string">&quot;hello&quot;</span>)
""");
    }

    [Fact]
    public void StringWithDollarEscape()
    {
        AssertHighlighter("julia",
"""
s = "price: \$5"
t = "$(a)$(b)"
u = "$a$b"
""",
"""
s = <span class="hljs-string">&quot;price: \$5&quot;</span>
t = <span class="hljs-string">&quot;<span class="hljs-subst">$(a)</span><span class="hljs-subst">$(b)</span>&quot;</span>
u = <span class="hljs-string">&quot;<span class="hljs-variable">$a</span><span class="hljs-variable">$b</span>&quot;</span>
""");
    }

    [Fact]
    public void NestedInterpolation()
    {
        AssertHighlighter("julia",
"""
println("sum = $(sum(x .^ 2)) and $(f(g(1), (2 + 3)))")
msg = "value: $(isnothing(v) ? "none" : string(v))"
""",
"""
println(<span class="hljs-string">&quot;sum = <span class="hljs-subst">$(sum(x .^ <span class="hljs-number">2</span>))</span> and <span class="hljs-subst">$(f(g(<span class="hljs-number">1</span>), (<span class="hljs-number">2</span> + <span class="hljs-number">3</span>)))</span>&quot;</span>)
msg = <span class="hljs-string">&quot;value: <span class="hljs-subst">$(isnothing(v) ? <span class="hljs-string">&quot;none&quot;</span> : string(v))</span>&quot;</span>
""");
    }

    [Fact]
    public void EscapedChars()
    {
        AssertHighlighter("julia",
"""
chars = ['\t', '\\', '\"', '\0', '\x41', '\177', ' ']
A'\B'
x = A' \ b'
""",
"""
chars = [<span class="hljs-string">&#x27;\t&#x27;</span>, <span class="hljs-string">&#x27;\\&#x27;</span>, <span class="hljs-string">&#x27;\&quot;&#x27;</span>, <span class="hljs-string">&#x27;\0&#x27;</span>, <span class="hljs-string">&#x27;\x41&#x27;</span>, <span class="hljs-string">&#x27;\177&#x27;</span>, <span class="hljs-string">&#x27; &#x27;</span>]
A&#x27;\B&#x27;
x = A&#x27; \ b&#x27;
""");
    }

    [Fact]
    public void Illegal()
    {
        AssertHighlighter("julia",
"""
x = 1 </ 2
""",
"""
x = <span class="hljs-number">1</span> &lt;/ <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void LargeWordBeforeQuote()
    {
        AssertHighlighter("julia",
"""
aVeryLongIdentifierName_with_numbers123 = "value"
html"<b>bold</b>"
""",
"""
aVeryLongIdentifierName_with_numbers123 = <span class="hljs-string">&quot;value&quot;</span>
<span class="hljs-string">html&quot;&lt;b&gt;bold&lt;/b&gt;&quot;</span>
""");
    }
}
