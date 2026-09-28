namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class JuliaReplHighlighterTests
{
    [Fact]
    public void Expression()
    {
        AssertHighlighter("julia-repl",
"""
julia> 1 + 1
2
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> <span class="hljs-number">1</span> + <span class="hljs-number">1</span>
</span>2
""");
    }

    [Fact]
    public void Println()
    {
        AssertHighlighter("julia-repl",
"""
julia> println("Hello, World!")
Hello, World!
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> println(<span class="hljs-string">&quot;Hello, World!&quot;</span>)
</span>Hello, World!
""");
    }

    [Fact]
    public void FunctionDefinitionOnContinuationLines()
    {
        AssertHighlighter("julia-repl",
"""
julia> function foo(x)
           x + 1
       end
foo (generic function with 1 method)

julia> foo(2)
3
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> <span class="hljs-keyword">function</span> foo(x)
           x + <span class="hljs-number">1</span>
       <span class="hljs-keyword">end</span>
</span>foo (generic function with 1 method)

<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> foo(<span class="hljs-number">2</span>)
</span>3
""");
    }

    [Fact]
    public void CodeRightAfterPrompt()
    {
        AssertHighlighter("julia-repl",
"""
julia>x = 1
1
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia">x = <span class="hljs-number">1</span>
</span>1
""");
    }

    [Fact]
    public void MatrixOutput()
    {
        AssertHighlighter("julia-repl",
"""
julia> A = [1 2; 3 4]
2×2 Matrix{Int64}:
 1  2
 3  4
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> A = [<span class="hljs-number">1</span> <span class="hljs-number">2</span>; <span class="hljs-number">3</span> <span class="hljs-number">4</span>]
</span>2×2 Matrix{Int64}:
 1  2
 3  4
""");
    }

    [Fact]
    public void Broadcast()
    {
        AssertHighlighter("julia-repl",
"""
julia> sin.(A) .+ 1
2×2 Matrix{Float64}:
 1.84147  1.9093
 1.14112  0.243198
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> sin.(A) .+ <span class="hljs-number">1</span>
</span>2×2 Matrix{Float64}:
 1.84147  1.9093
 1.14112  0.243198
""");
    }

    [Fact]
    public void StringInterpolation()
    {
        AssertHighlighter("julia-repl",
"""
julia> name = "Julia"; "Hello, $name! $(1 + 2)"
"Hello, Julia! 3"
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> name = <span class="hljs-string">&quot;Julia&quot;</span>; <span class="hljs-string">&quot;Hello, <span class="hljs-variable">$name</span>! <span class="hljs-subst">$(<span class="hljs-number">1</span> + <span class="hljs-number">2</span>)</span>&quot;</span>
</span>&quot;Hello, Julia! 3&quot;
""");
    }

    [Fact]
    public void ErrorAndStacktrace()
    {
        AssertHighlighter("julia-repl",
"""
julia> sqrt(-1)
ERROR: DomainError with -1.0:
sqrt was called with a negative real argument but will only return a complex result if called with a complex argument. Try sqrt(Complex(x)).
Stacktrace:
 [1] throw_complex_domainerror(f::Symbol, x::Float64)
   @ Base.Math ./math.jl:33
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> sqrt(-<span class="hljs-number">1</span>)
</span>ERROR: DomainError with -1.0:
sqrt was called with a negative real argument but will only return a complex result if called with a complex argument. Try sqrt(Complex(x)).
Stacktrace:
 [1] throw_complex_domainerror(f::Symbol, x::Float64)
   @ Base.Math ./math.jl:33
""");
    }

    [Fact]
    public void TripleQuotedStringOnContinuationLines()
    {
        AssertHighlighter("julia-repl",
""""
julia> s = """
           first
           second
           """
"first\nsecond\n"
"""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> s = <span class="hljs-string">&quot;&quot;&quot;
           first
           second
           &quot;&quot;&quot;</span>
</span>&quot;first\nsecond\n&quot;
""");
    }

    [Fact]
    public void Struct()
    {
        AssertHighlighter("julia-repl",
"""
julia> struct Point{T<:Real}
           x::T
           y::T
       end

julia> Point(1, 2)
Point{Int64}(1, 2)
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> <span class="hljs-keyword">struct</span> Point{T&lt;:<span class="hljs-built_in">Real</span>}
           x::T
           y::T
       <span class="hljs-keyword">end</span>
</span>
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> Point(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>)
</span>Point{Int64}(1, 2)
""");
    }

    [Fact]
    public void Macro()
    {
        AssertHighlighter("julia-repl",
"""
julia> @time sum(rand(1000))
  0.000010 seconds (1 allocation: 7.938 KiB)
500.123
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> <span class="hljs-meta">@time</span> sum(rand(<span class="hljs-number">1000</span>))
</span>  0.000010 seconds (1 allocation: 7.938 KiB)
500.123
""");
    }

    [Fact]
    public void Using()
    {
        AssertHighlighter("julia-repl",
"""
julia> using LinearAlgebra

julia> det([1 2; 3 4])
-2.0
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> <span class="hljs-keyword">using</span> LinearAlgebra
</span>
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> det([<span class="hljs-number">1</span> <span class="hljs-number">2</span>; <span class="hljs-number">3</span> <span class="hljs-number">4</span>])
</span>-2.0
""");
    }

    [Fact]
    public void Comprehension()
    {
        AssertHighlighter("julia-repl",
"""
julia> [x^2 for x in 1:5 if isodd(x)]
3-element Vector{Int64}:
  1
  9
 25
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> [x^<span class="hljs-number">2</span> <span class="hljs-keyword">for</span> x <span class="hljs-keyword">in</span> <span class="hljs-number">1</span>:<span class="hljs-number">5</span> <span class="hljs-keyword">if</span> isodd(x)]
</span>3-element Vector{Int64}:
  1
  9
 25
""");
    }

    [Fact]
    public void DoBlock()
    {
        AssertHighlighter("julia-repl",
"""
julia> map([1, 2, 3]) do x
           x * 2
       end
3-element Vector{Int64}:
 2
 4
 6
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> map([<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>]) <span class="hljs-keyword">do</span> x
           x * <span class="hljs-number">2</span>
       <span class="hljs-keyword">end</span>
</span>3-element Vector{Int64}:
 2
 4
 6
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("julia-repl",
"""
julia> α = π / 2
1.5707963267948966

julia> √4
2.0
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> α = <span class="hljs-literal">π</span> / <span class="hljs-number">2</span>
</span>1.5707963267948966

<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> √<span class="hljs-number">4</span>
</span>2.0
""");
    }

    [Fact]
    public void SemicolonSuppressesOutput()
    {
        AssertHighlighter("julia-repl",
"""
julia> x = 5;

julia> x
5
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> x = <span class="hljs-number">5</span>;
</span>
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> x
</span>5
""");
    }

    [Fact]
    public void OutputIndentedBySixSpaces_IsCode()
    {
        AssertHighlighter("julia-repl",
"""
julia> print("      indented")
      indented
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> print(<span class="hljs-string">&quot;      indented&quot;</span>)
      indented</span>
""");
    }

    [Fact]
    public void BlankLine_EndsCode()
    {
        AssertHighlighter("julia-repl",
"""
julia> function f()

           1
       end
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> <span class="hljs-keyword">function</span> f()
</span>
           1
       end
""");
    }

    [Fact]
    public void TabIndentedLine_IsOutput()
    {
        AssertHighlighter("julia-repl",
"julia> f(x) = x\n\t\tcontinued",
"<span class=\"hljs-meta prompt_\">julia&gt;</span><span class=\"language-julia\"> f(x) = x\n</span>\t\tcontinued");
    }

    [Fact]
    public void OtherReplModes_AreOutput()
    {
        AssertHighlighter("julia-repl",
"""
help?> sum
search: sum sum! summary

  sum(f, itr; [init])
""",
"""
help?&gt; sum
search: sum sum! summary

  sum(f, itr; [init])
""");
    }

    [Fact]
    public void PkgMode_IsOutput()
    {
        AssertHighlighter("julia-repl",
"""
(@v1.10) pkg> add Example
   Resolving package versions...
""",
"""
(@v1.10) pkg&gt; add Example
   Resolving package versions...
""");
    }

    [Fact]
    public void PromptWithoutCode()
    {
        AssertHighlighter("julia-repl",
"julia>\njulia> ",
"<span class=\"hljs-meta prompt_\">julia&gt;</span><span class=\"language-julia\">\n</span><span class=\"hljs-meta prompt_\">julia&gt;</span><span class=\"language-julia\"> </span>");
    }

    [Fact]
    public void UnterminatedString_DoesNotLeakIntoNextPrompt()
    {
        AssertHighlighter("julia-repl",
"""
julia> s = "never closed
ERROR: syntax: incomplete: invalid string syntax

julia> x = 1
1
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> s = <span class="hljs-string">&quot;never closed
</span></span>ERROR: syntax: incomplete: invalid string syntax

<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> x = <span class="hljs-number">1</span>
</span>1
""");
    }

    [Fact]
    public void UnterminatedComment_DoesNotLeakIntoNextPrompt()
    {
        AssertHighlighter("julia-repl",
"""
julia> #= comment
ERROR: syntax: incomplete

julia> x = 1
1
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> <span class="hljs-comment">#= comment
</span></span>ERROR: syntax: incomplete

<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> x = <span class="hljs-number">1</span>
</span>1
""");
    }

    [Fact]
    public void JldoctestAlias()
    {
        AssertHighlighter("jldoctest",
"""
julia> a = 1
1

julia> a + 1
2
""",
"""
<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> a = <span class="hljs-number">1</span>
</span>1

<span class="hljs-meta prompt_">julia&gt;</span><span class="language-julia"> a + <span class="hljs-number">1</span>
</span>2
""");
    }

    [Fact]
    public void IndentedPrompt_IsOutput()
    {
        AssertHighlighter("julia-repl",
"""
  julia> x = 1
""",
"""
  julia&gt; x = 1
""");
    }

    [Fact]
    public void PromptInsideALine_IsOutput()
    {
        AssertHighlighter("julia-repl",
"""
x julia> y
""",
"""
x julia&gt; y
""");
    }

    [Fact]
    public void EmptyInput()
    {
        AssertHighlighter("julia-repl",
"",
"");
    }
}
