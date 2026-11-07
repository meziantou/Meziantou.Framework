namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class ErlangReplHighlighterTests
{
    [Fact]
    public void Expressions()
    {
        AssertHighlighter("erlang-repl",
"""
1> X = 5.
5
2> X + 1.
6
""",
"""
<span class="hljs-meta prompt_">1&gt; </span>X = <span class="hljs-number">5</span>.
<span class="hljs-number">5</span>
<span class="hljs-meta prompt_">2&gt; </span>X + <span class="hljs-number">1</span>.
<span class="hljs-number">6</span>
""");
    }

    [Fact]
    public void ListComprehension()
    {
        AssertHighlighter("erlang-repl",
"""
3> [Y * 2 || Y <- [1, 2, 3]].
[2,4,6]
""",
"""
<span class="hljs-meta prompt_">3&gt; </span>[Y * <span class="hljs-number">2</span> || Y &lt;- [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>]].
[<span class="hljs-number">2</span>,<span class="hljs-number">4</span>,<span class="hljs-number">6</span>]
""");
    }

    [Fact]
    public void Fun()
    {
        AssertHighlighter("erlang-repl",
"""
1> Double = fun(N) -> N * 2 end.
#Fun<erl_eval.44.65746770>
2> Double(21).
42
""",
"""
<span class="hljs-meta prompt_">1&gt; </span>Double = <span class="hljs-keyword">fun</span>(N) -&gt; N * <span class="hljs-number">2</span> <span class="hljs-keyword">end</span>.
#Fun&lt;erl_eval.<span class="hljs-number">44.65746770</span>&gt;
<span class="hljs-meta prompt_">2&gt; </span>Double(<span class="hljs-number">21</span>).
<span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void IoFormat()
    {
        AssertHighlighter("erlang-repl",
"""
1> io:format("Hello, ~s!~n", ["World"]).
Hello, World!
ok
""",
"""
<span class="hljs-meta prompt_">1&gt; </span>io:format(<span class="hljs-string">&quot;Hello, ~s!~n&quot;</span>, [<span class="hljs-string">&quot;World&quot;</span>]).
Hello, World!
ok
""");
    }

    [Fact]
    public void CompileAndCall()
    {
        AssertHighlighter("erlang-repl",
"""
1> c(hello).
{ok,hello}
2> hello:hello_world().
hello, world
ok
""",
"""
<span class="hljs-meta prompt_">1&gt; </span>c(hello).
{ok,hello}
<span class="hljs-meta prompt_">2&gt; </span>hello:hello_world().
hello, world
ok
""");
    }

    [Fact]
    public void SpawnAndSend()
    {
        AssertHighlighter("erlang-repl",
"""
1> Pid = spawn(fun() -> receive {From, Msg} -> From ! Msg end end).
<0.85.0>
2> Pid ! {self(), hello}.
{<0.80.0>,hello}
""",
"""
<span class="hljs-meta prompt_">1&gt; </span>Pid = <span class="hljs-built_in">spawn</span>(<span class="hljs-keyword">fun</span>() -&gt; <span class="hljs-keyword">receive</span> {From, Msg} -&gt; From ! Msg <span class="hljs-keyword">end</span> <span class="hljs-keyword">end</span>).
&lt;<span class="hljs-number">0.85</span>.<span class="hljs-number">0</span>&gt;
<span class="hljs-meta prompt_">2&gt; </span>Pid ! {<span class="hljs-built_in">self</span>(), hello}.
{&lt;<span class="hljs-number">0.80</span>.<span class="hljs-number">0</span>&gt;,hello}
""");
    }

    [Fact]
    public void Case()
    {
        AssertHighlighter("erlang-repl",
"""
1> case 1 of 1 -> one; _ -> other end.
one
""",
"""
<span class="hljs-meta prompt_">1&gt; </span><span class="hljs-keyword">case</span> <span class="hljs-number">1</span> <span class="hljs-keyword">of</span> <span class="hljs-number">1</span> -&gt; one; _ -&gt; other <span class="hljs-keyword">end</span>.
one
""");
    }

    [Fact]
    public void Exception()
    {
        AssertHighlighter("erlang-repl",
"""
1> 1/0.
** exception error: an error occurred when evaluating an arithmetic expression
     in operator  '/'/2
        called as 1 / 0
""",
"""
<span class="hljs-meta prompt_">1&gt; </span><span class="hljs-number">1</span>/<span class="hljs-number">0</span>.
** exception error: an error occurred <span class="hljs-keyword">when</span> evaluating an arithmetic expression
     in operator  <span class="hljs-string">&#x27;/&#x27;</span>/<span class="hljs-number">2</span>
        called as <span class="hljs-number">1</span> / <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("erlang-repl",
"""
1> % a comment
1> X = 1. % trailing
1
""",
"""
<span class="hljs-meta prompt_">1&gt; </span><span class="hljs-comment">% a comment</span>
<span class="hljs-meta prompt_">1&gt; </span>X = <span class="hljs-number">1</span>. <span class="hljs-comment">% trailing</span>
<span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Macros()
    {
        AssertHighlighter("erlang-repl",
"""
1> ?MODULE.
* 1:1: undefined macro 'MODULE'
2> ?FOO::Bar
""",
"""
<span class="hljs-meta prompt_">1&gt; </span>?MODULE.
* <span class="hljs-number">1</span>:<span class="hljs-number">1</span>: undefined macro <span class="hljs-string">&#x27;MODULE&#x27;</span>
<span class="hljs-meta prompt_">2&gt; </span>?FOO::Bar
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("erlang-repl",
"""
1> 16#FF + 2#1010 + 1_000 + 3.14e-2.
1265.0314
""",
"""
<span class="hljs-meta prompt_">1&gt; </span><span class="hljs-number">16#FF</span> + <span class="hljs-number">2#1010</span> + <span class="hljs-number">1_000</span> + <span class="hljs-number">3.14e-2</span>.
<span class="hljs-number">1265.0314</span>
""");
    }

    [Fact]
    public void Records()
    {
        AssertHighlighter("erlang-repl",
"""
1> rr("person.hrl").
[person]
2> P = #person{name = "Alice", age = 30}.
#person{name = "Alice",age = 30}
""",
"""
<span class="hljs-meta prompt_">1&gt; </span>rr(<span class="hljs-string">&quot;person.hrl&quot;</span>).
[person]
<span class="hljs-meta prompt_">2&gt; </span>P = #person{name = <span class="hljs-string">&quot;Alice&quot;</span>, age = <span class="hljs-number">30</span>}.
#person{name = <span class="hljs-string">&quot;Alice&quot;</span>,age = <span class="hljs-number">30</span>}
""");
    }

    [Fact]
    public void Maps()
    {
        AssertHighlighter("erlang-repl",
"""
1> M = #{a => 1, b => 2}.
#{a => 1,b => 2}
2> maps:get(a, M).
1
""",
"""
<span class="hljs-meta prompt_">1&gt; </span>M = #{a =&gt; <span class="hljs-number">1</span>, b =&gt; <span class="hljs-number">2</span>}.
#{a =&gt; <span class="hljs-number">1</span>,b =&gt; <span class="hljs-number">2</span>}
<span class="hljs-meta prompt_">2&gt; </span>maps:get(a, M).
<span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Binaries()
    {
        AssertHighlighter("erlang-repl",
"""
1> <<1, 2, 3>>.
<<1,2,3>>
2> <<"abc">>.
<<"abc">>
""",
"""
<span class="hljs-meta prompt_">1&gt; </span>&lt;&lt;<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>&gt;&gt;.
&lt;&lt;<span class="hljs-number">1</span>,<span class="hljs-number">2</span>,<span class="hljs-number">3</span>&gt;&gt;
<span class="hljs-meta prompt_">2&gt; </span>&lt;&lt;<span class="hljs-string">&quot;abc&quot;</span>&gt;&gt;.
&lt;&lt;<span class="hljs-string">&quot;abc&quot;</span>&gt;&gt;
""");
    }

    [Fact]
    public void TryCatch()
    {
        AssertHighlighter("erlang-repl",
"""
1> try throw(oops) catch throw:X -> {caught, X} after ok end.
{caught,oops}
""",
"""
<span class="hljs-meta prompt_">1&gt; </span><span class="hljs-keyword">try</span> throw(oops) <span class="hljs-keyword">catch</span> throw:X -&gt; {caught, X} <span class="hljs-keyword">after</span> ok <span class="hljs-keyword">end</span>.
{caught,oops}
""");
    }

    [Fact]
    public void ShortCircuitOperators()
    {
        AssertHighlighter("erlang-repl",
"""
1> true andalso false orelse true.
true
""",
"""
<span class="hljs-meta prompt_">1&gt; </span>true <span class="hljs-keyword">andalso</span> false <span class="hljs-keyword">orelse</span> true.
true
""");
    }

    [Fact]
    public void ArithmeticAndBitwiseOperators()
    {
        AssertHighlighter("erlang-repl",
"""
1> 5 div 2 + 5 rem 2 band 3 bor 4 bxor 1 bsl 2 bsr 1.
6
""",
"""
<span class="hljs-meta prompt_">1&gt; </span><span class="hljs-number">5</span> <span class="hljs-keyword">div</span> <span class="hljs-number">2</span> + <span class="hljs-number">5</span> <span class="hljs-keyword">rem</span> <span class="hljs-number">2</span> <span class="hljs-keyword">band</span> <span class="hljs-number">3</span> <span class="hljs-keyword">bor</span> <span class="hljs-number">4</span> <span class="hljs-keyword">bxor</span> <span class="hljs-number">1</span> <span class="hljs-keyword">bsl</span> <span class="hljs-number">2</span> <span class="hljs-keyword">bsr</span> <span class="hljs-number">1</span>.
<span class="hljs-number">6</span>
""");
    }

    [Fact]
    public void MultilineInput()
    {
        AssertHighlighter("erlang-repl",
"""
1> F = fun(X) ->
1>     X * 2
1> end.
#Fun<erl_eval.44.97283095>
""",
"""
<span class="hljs-meta prompt_">1&gt; </span>F = <span class="hljs-keyword">fun</span>(X) -&gt;
<span class="hljs-meta prompt_">1&gt; </span>    X * <span class="hljs-number">2</span>
<span class="hljs-meta prompt_">1&gt; </span><span class="hljs-keyword">end</span>.
#Fun&lt;erl_eval.<span class="hljs-number">44.97283095</span>&gt;
""");
    }

    [Fact]
    public void StringEscapes()
    {
        AssertHighlighter("erlang-repl",
"""
1> "a \"quoted\" string".
"a \"quoted\" string"
""",
"""
<span class="hljs-meta prompt_">1&gt; </span><span class="hljs-string">&quot;a \&quot;quoted\&quot; string&quot;</span>.
<span class="hljs-string">&quot;a \&quot;quoted\&quot; string&quot;</span>
""");
    }

    [Fact]
    public void QuotedAtom()
    {
        AssertHighlighter("erlang-repl",
"""
1> 'hello world'.
'hello world'
""",
"""
<span class="hljs-meta prompt_">1&gt; </span><span class="hljs-string">&#x27;hello world&#x27;</span>.
<span class="hljs-string">&#x27;hello world&#x27;</span>
""");
    }

    [Fact]
    public void UnterminatedString()
    {
        AssertHighlighter("erlang-repl",
"""
1> "never closed.
1> X = 1.
""",
"""
<span class="hljs-meta prompt_">1&gt; </span><span class="hljs-string">&quot;never closed.
1&gt; X = 1.</span>
""");
    }

    [Fact]
    public void AtomsStartingWithOk()
    {
        AssertHighlighter("erlang-repl",
"""
1> okay.
okay
2> token.
token
""",
"""
<span class="hljs-meta prompt_">1&gt; </span>okay.
okay
<span class="hljs-meta prompt_">2&gt; </span>token.
token
""");
    }

    [Fact]
    public void KeywordAfterOk_IsPartOfTheAtom()
    {
        AssertHighlighter("erlang-repl",
"""
1> okend.
2> receive_it.
""",
"""
<span class="hljs-meta prompt_">1&gt; </span>okend.
<span class="hljs-meta prompt_">2&gt; </span>receive_it.
""");
    }

    [Fact]
    public void Variables()
    {
        AssertHighlighter("erlang-repl",
"""
1> MyVar_1 = 10, _Ignored = 2.
10
""",
"""
<span class="hljs-meta prompt_">1&gt; </span>MyVar_1 = <span class="hljs-number">10</span>, _Ignored = <span class="hljs-number">2</span>.
<span class="hljs-number">10</span>
""");
    }

    [Fact]
    public void RemoteCall()
    {
        AssertHighlighter("erlang-repl",
"""
1> lists:map(fun(X) -> X + 1 end, [1,2]).
[2,3]
""",
"""
<span class="hljs-meta prompt_">1&gt; </span>lists:map(<span class="hljs-keyword">fun</span>(X) -&gt; X + <span class="hljs-number">1</span> <span class="hljs-keyword">end</span>, [<span class="hljs-number">1</span>,<span class="hljs-number">2</span>]).
[<span class="hljs-number">2</span>,<span class="hljs-number">3</span>]
""");
    }

    [Fact]
    public void DistributedNodePrompt_IsNotAPrompt()
    {
        AssertHighlighter("erlang-repl",
"""
(node@host)1> node().
'node@host'
""",
"""
(node@host)<span class="hljs-number">1</span>&gt; node().
<span class="hljs-string">&#x27;node@host&#x27;</span>
""");
    }

    [Fact]
    public void PromptWithoutSpace_IsNotAPrompt()
    {
        AssertHighlighter("erlang-repl",
"""
1>X.
""",
"""
<span class="hljs-number">1</span>&gt;X.
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("erlang-repl",
"""
1> "héllo".
"héllo"
""",
"""
<span class="hljs-meta prompt_">1&gt; </span><span class="hljs-string">&quot;héllo&quot;</span>.
<span class="hljs-string">&quot;héllo&quot;</span>
""");
    }

    [Fact]
    public void EmptyInput()
    {
        AssertHighlighter("erlang-repl",
"",
"");
    }
}
