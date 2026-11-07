namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class ErlangHighlighterTests
{
    [Fact]
    public void ModuleHeader()
    {
        AssertHighlighter("erlang",
"""
-module(hello).
-export([hello/0, add/2]).
-import(lists, [map/2]).
-include_lib("kernel/include/file.hrl").
-behaviour(gen_server).
-author("Jane Doe").
-vsn(1).
-compile([export_all, nowarn_export_all]).
""",
"""
<span class="hljs-keyword">-module</span><span class="hljs-params">(hello)</span>.
<span class="hljs-keyword">-export</span><span class="hljs-params">([hello/<span class="hljs-number">0</span>, add/<span class="hljs-number">2</span>])</span>.
<span class="hljs-keyword">-import</span><span class="hljs-params">(lists, [map/<span class="hljs-number">2</span>])</span>.
<span class="hljs-keyword">-include_lib</span><span class="hljs-params">(<span class="hljs-string">&quot;kernel/include/file.hrl&quot;</span>)</span>.
<span class="hljs-keyword">-behaviour</span><span class="hljs-params">(gen_server)</span>.
<span class="hljs-keyword">-author</span><span class="hljs-params">(<span class="hljs-string">&quot;Jane Doe&quot;</span>)</span>.
<span class="hljs-keyword">-vsn</span><span class="hljs-params">(<span class="hljs-number">1</span>)</span>.
<span class="hljs-keyword">-compile</span><span class="hljs-params">([export_all, nowarn_export_all])</span>.
""");
    }

    [Fact]
    public void FunctionSimple()
    {
        AssertHighlighter("erlang",
"""
hello() ->
    io:format("Hello, world!~n").

add(A, B) ->
    A + B.
""",
"""
<span class="hljs-function"><span class="hljs-title">hello</span><span class="hljs-params">()</span> -&gt;</span>
    io:format(<span class="hljs-string">&quot;Hello, world!~n&quot;</span>).

<span class="hljs-function"><span class="hljs-title">add</span><span class="hljs-params">(A, B)</span> -&gt;</span>
    A + B.
""");
    }

    [Fact]
    public void FunctionClauses()
    {
        AssertHighlighter("erlang",
"""
factorial(0) -> 1;
factorial(N) when N > 0 ->
    N * factorial(N - 1).
""",
"""
<span class="hljs-function"><span class="hljs-title">factorial</span><span class="hljs-params">(<span class="hljs-number">0</span>)</span> -&gt;</span> <span class="hljs-number">1</span>;
<span class="hljs-function"><span class="hljs-title">factorial</span><span class="hljs-params">(N)</span> <span class="hljs-keyword">when</span> N &gt; <span class="hljs-number">0</span> -&gt;</span>
    N * factorial(N - <span class="hljs-number">1</span>).
""");
    }

    [Fact]
    public void Guards()
    {
        AssertHighlighter("erlang",
"""
classify(X) when is_integer(X), X > 0 -> positive;
classify(X) when X < 0; X =:= -1 -> negative;
classify(_) -> zero.
""",
"""
<span class="hljs-function"><span class="hljs-title">classify</span><span class="hljs-params">(X)</span> <span class="hljs-keyword">when</span> is_integer(X), X &gt; <span class="hljs-number">0</span> -&gt;</span> positive;
<span class="hljs-function"><span class="hljs-title">classify</span><span class="hljs-params">(X)</span> <span class="hljs-keyword">when</span> X &lt; <span class="hljs-number">0</span>; X =:= -<span class="hljs-number">1</span> -&gt;</span> negative;
<span class="hljs-function"><span class="hljs-title">classify</span><span class="hljs-params">(_)</span> -&gt;</span> zero.
""");
    }

    [Fact]
    public void CaseExpr()
    {
        AssertHighlighter("erlang",
"""
check(Value) ->
    case Value of
        {ok, Result} -> Result;
        {error, Reason} -> erlang:error(Reason);
        _ -> undefined
    end.
""",
"""
<span class="hljs-function"><span class="hljs-title">check</span><span class="hljs-params">(Value)</span> -&gt;</span>
    <span class="hljs-keyword">case</span> Value <span class="hljs-keyword">of</span>
        {ok, Result} -&gt; Result;
        {error, Reason} -&gt; erlang:error(Reason);
        _ -&gt; undefined
    <span class="hljs-keyword">end</span>.
""");
    }

    [Fact]
    public void IfExpr()
    {
        AssertHighlighter("erlang",
"""
max(A, B) ->
    if
        A > B -> A;
        true -> B
    end.
""",
"""
<span class="hljs-function"><span class="hljs-title">max</span><span class="hljs-params">(A, B)</span> -&gt;</span>
    <span class="hljs-keyword">if</span>
        A &gt; B -&gt; A;
        <span class="hljs-literal">true</span> -&gt; B
    <span class="hljs-keyword">end</span>.
""");
    }

    [Fact]
    public void ReceiveExpr()
    {
        AssertHighlighter("erlang",
"""
loop(State) ->
    receive
        {From, get} ->
            From ! {self(), State},
            loop(State);
        stop ->
            ok
    after 5000 ->
        timeout
    end.
""",
"""
<span class="hljs-function"><span class="hljs-title">loop</span><span class="hljs-params">(State)</span> -&gt;</span>
    <span class="hljs-keyword">receive</span>
        {From, get} -&gt;
            From ! {self(), State},
            loop(State);
        stop -&gt;
            ok
    <span class="hljs-keyword">after</span> <span class="hljs-number">5000</span> -&gt;
        timeout
    <span class="hljs-keyword">end</span>.
""");
    }

    [Fact]
    public void TryCatch()
    {
        AssertHighlighter("erlang",
"""
safe_div(A, B) ->
    try A / B of
        Result -> {ok, Result}
    catch
        error:badarith -> {error, divide_by_zero};
        throw:Term -> Term;
        exit:Reason:Stacktrace -> {exit, Reason, Stacktrace}
    after
        cleanup()
    end.
""",
"""
<span class="hljs-function"><span class="hljs-title">safe_div</span><span class="hljs-params">(A, B)</span> -&gt;</span>
    <span class="hljs-keyword">try</span> A / B <span class="hljs-keyword">of</span>
        Result -&gt; {ok, Result}
    <span class="hljs-keyword">catch</span>
        error:badarith -&gt; {error, divide_by_zero};
        throw:Term -&gt; Term;
        exit:Reason:Stacktrace -&gt; {exit, Reason, Stacktrace}
    <span class="hljs-keyword">after</span>
        cleanup()
    <span class="hljs-keyword">end</span>.
""");
    }

    [Fact]
    public void Funs()
    {
        AssertHighlighter("erlang",
"""
Double = fun(X) -> X * 2 end,
lists:map(fun(X) -> X + 1 end, [1, 2, 3]),
F = fun lists:reverse/1,
G = fun helper/2,
Named = fun Fact(0) -> 1; Fact(N) -> N * Fact(N - 1) end.
""",
"""
Double = <span class="hljs-keyword">fun</span>(X) -&gt; X * <span class="hljs-number">2</span> <span class="hljs-keyword">end</span>,
lists:map(<span class="hljs-keyword">fun</span>(X) -&gt; X + <span class="hljs-number">1</span> <span class="hljs-keyword">end</span>, [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>]),
F = <span class="hljs-keyword">fun</span> lists:reverse/<span class="hljs-number">1</span>,
G = <span class="hljs-keyword">fun</span> helper/<span class="hljs-number">2</span>,
Named = <span class="hljs-keyword">fun</span> Fact(<span class="hljs-number">0</span>) -&gt; <span class="hljs-number">1</span>; Fact(N) -&gt; N * Fact(N - <span class="hljs-number">1</span>) <span class="hljs-keyword">end</span>.
""");
    }

    [Fact]
    public void Records()
    {
        AssertHighlighter("erlang",
"""
-record(person, {name = "" :: string(), age = 0 :: integer()}).

new_person(Name) ->
    #person{name = Name, age = 30}.

get_name(P) ->
    P#person.name.

update(P) ->
    P#person{age = P#person.age + 1}.
""",
"""
<span class="hljs-keyword">-record</span><span class="hljs-params">(person, {name = <span class="hljs-string">&quot;&quot;</span> :: string(), age = <span class="hljs-number">0</span> :: integer()})</span>.

<span class="hljs-function"><span class="hljs-title">new_person</span><span class="hljs-params">(Name)</span> -&gt;</span>
    #person{name = Name, age = <span class="hljs-number">30</span>}.

<span class="hljs-function"><span class="hljs-title">get_name</span><span class="hljs-params">(P)</span> -&gt;</span>
    P#person.name.

<span class="hljs-function"><span class="hljs-title">update</span><span class="hljs-params">(P)</span> -&gt;</span>
    P#person{age = P#person.age + <span class="hljs-number">1</span>}.
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("erlang",
"""
N1 = 42,
N2 = -17,
N3 = 3.14,
N4 = 1.0e10,
N5 = 6.02e-23,
N6 = 16#FF,
N7 = 2#1010,
N8 = 1_000_000,
N9 = 16#DEAD_BEEF,
N10 = 36#ZZ.
""",
"""
N1 = <span class="hljs-number">42</span>,
N2 = -<span class="hljs-number">17</span>,
N3 = <span class="hljs-number">3.14</span>,
N4 = <span class="hljs-number">1.0e10</span>,
N5 = <span class="hljs-number">6.02e-23</span>,
N6 = <span class="hljs-number">16#FF</span>,
N7 = <span class="hljs-number">2#1010</span>,
N8 = <span class="hljs-number">1_000_000</span>,
N9 = <span class="hljs-number">16#DEAD_BEEF</span>,
N10 = <span class="hljs-number">36</span>#ZZ.
""");
    }

    [Fact]
    public void Chars()
    {
        AssertHighlighter("erlang",
"""
C1 = $a,
C2 = $\n,
C3 = $\s,
C4 = $\\,
C5 = $\123,
C6 = $ ,
C7 = $".
""",
"""
C1 = <span class="hljs-string">$a</span>,
C2 = <span class="hljs-string">$\n</span>,
C3 = <span class="hljs-string">$\s</span>,
C4 = <span class="hljs-string">$\\</span>,
C5 = <span class="hljs-string">$\123</span>,
C6 = <span class="hljs-string">$ </span>,
C7 = <span class="hljs-string">$&quot;</span>.
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("erlang",
"""
S1 = "hello",
S2 = "escape \" quote \n newline",
S3 = "tab\there",
A1 = 'quoted atom',
A2 = 'it''s',
B = <<"binary">>,
B2 = <<1, 2, 3>>,
B3 = <<X:8, Rest/binary>>.
""",
"""
S1 = <span class="hljs-string">&quot;hello&quot;</span>,
S2 = <span class="hljs-string">&quot;escape \&quot; quote \n newline&quot;</span>,
S3 = <span class="hljs-string">&quot;tab\there&quot;</span>,
A1 = &#x27;quoted atom&#x27;,
A2 = &#x27;it&#x27;&#x27;s&#x27;,
B = &lt;&lt;<span class="hljs-string">&quot;binary&quot;</span>&gt;&gt;,
B2 = &lt;&lt;<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>&gt;&gt;,
B3 = &lt;&lt;X:<span class="hljs-number">8</span>, Rest/binary&gt;&gt;.
""");
    }

    [Fact]
    public void TripleQuote()
    {
        AssertHighlighter("erlang",
""""
-doc """
Returns the sum.
Uses "quotes" inside.
""".
f() ->
    S = """
      Multi-line
      string
      """,
    S.
"""",
"""
<span class="hljs-keyword">-doc</span> <span class="hljs-string">&quot;&quot;&quot;
Returns the sum.
Uses &quot;quotes&quot; inside.
&quot;&quot;&quot;</span>.
<span class="hljs-function"><span class="hljs-title">f</span><span class="hljs-params">()</span> -&gt;</span>
    S = <span class="hljs-string">&quot;&quot;&quot;
      Multi-line
      string
      &quot;&quot;&quot;</span>,
    S.
""");
    }

    [Fact]
    public void TripleQuoteExtra()
    {
        AssertHighlighter("erlang",
"""""
X = """"
Contains """ inside
"""".
""""",
"""
X = <span class="hljs-string">&quot;&quot;&quot;&quot;
Contains &quot;&quot;&quot; inside
&quot;&quot;&quot;&quot;</span>.
""");
    }

    [Fact]
    public void TripleQuoteInsideLongerRun()
    {
        AssertHighlighter("erlang",
""""""
X = """""a""""a"""a.
"""""",
"""
X = <span class="hljs-string">&quot;&quot;</span><span class="hljs-string">&quot;&quot;&quot;a&quot;&quot;&quot;</span><span class="hljs-string">&quot;a&quot;</span><span class="hljs-string">&quot;&quot;</span>a.
""");
    }

    [Fact]
    public void Sigils()
    {
        AssertHighlighter("erlang",
""""
A = ~"binary string",
B = ~b"with \n escape",
C = ~B"no escape",
D = ~s(parens),
E = ~S[brackets],
F = ~{braces},
G = ~<angle>,
H = ~/slash/,
I = ~|pipe|,
J = ~'apos',
K = ~`backtick`,
L = ~#hash#,
M = ~"""
Triple sigil
""".
"""",
"""
A = <span class="hljs-string">~&quot;binary string&quot;</span>,
B = <span class="hljs-string">~b&quot;with \n escape&quot;</span>,
C = <span class="hljs-string">~B&quot;no escape&quot;</span>,
D = <span class="hljs-string">~s(parens)</span>,
E = <span class="hljs-string">~S[brackets]</span>,
F = <span class="hljs-string">~{braces}</span>,
G = <span class="hljs-string">~&lt;angle&gt;</span>,
H = <span class="hljs-string">~/slash/</span>,
I = <span class="hljs-string">~|pipe|</span>,
J = <span class="hljs-string">~&#x27;apos&#x27;</span>,
K = <span class="hljs-string">~`backtick`</span>,
L = <span class="hljs-string">~#hash#</span>,
M = <span class="hljs-string">~&quot;&quot;&quot;
Triple sigil
&quot;&quot;&quot;</span>.
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("erlang",
"""
% A comment
%% Double comment
%%% Section header
f() -> ok. % trailing
% TODO: implement
""",
"""
<span class="hljs-comment">% A comment</span>
<span class="hljs-comment">%% Double comment</span>
<span class="hljs-comment">%%% Section header</span>
<span class="hljs-function"><span class="hljs-title">f</span><span class="hljs-params">()</span> -&gt;</span> ok. <span class="hljs-comment">% trailing</span>
<span class="hljs-comment">% <span class="hljs-doctag">TODO:</span> implement</span>
""");
    }

    [Fact]
    public void Macros()
    {
        AssertHighlighter("erlang",
"""
-define(TIMEOUT, 5000).
-define(SQUARE(X), (X) * (X)).
-ifdef(DEBUG).
-define(LOG(Msg), io:format("~p~n", [Msg])).
-else.
-define(LOG(Msg), ok).
-endif.
f() -> ?TIMEOUT + ?MODULE_STRING.
""",
"""
<span class="hljs-keyword">-define</span><span class="hljs-params">(TIMEOUT, <span class="hljs-number">5000</span>)</span>.
<span class="hljs-keyword">-define</span><span class="hljs-params">(SQUARE(X), (X) * (X))</span>.
<span class="hljs-keyword">-ifdef</span><span class="hljs-params">(DEBUG)</span>.
<span class="hljs-keyword">-define</span><span class="hljs-params">(LOG(Msg), io:format(<span class="hljs-string">&quot;~p~n&quot;</span>, [Msg]))</span>.
<span class="hljs-keyword">-else</span>.
<span class="hljs-keyword">-define</span><span class="hljs-params">(LOG(Msg), ok)</span>.
<span class="hljs-keyword">-endif</span>.
<span class="hljs-function"><span class="hljs-title">f</span><span class="hljs-params">()</span> -&gt;</span> ?TIMEOUT + ?MODULE_STRING.
""");
    }

    [Fact]
    public void Specs()
    {
        AssertHighlighter("erlang",
"""
-spec add(integer(), integer()) -> integer().
-type color() :: red | green | blue.
-opaque id() :: non_neg_integer().
-export_type([color/0]).
-callback init(Args :: term()) -> {ok, State :: term()}.
""",
"""
<span class="hljs-keyword">-spec</span> add<span class="hljs-params">(integer(), integer())</span> -&gt; integer<span class="hljs-params">()</span>.
<span class="hljs-keyword">-type</span> color<span class="hljs-params">()</span> :: red | green | blue.
<span class="hljs-keyword">-opaque</span> id<span class="hljs-params">()</span> :: non_neg_integer<span class="hljs-params">()</span>.
<span class="hljs-keyword">-export_type</span><span class="hljs-params">([color/<span class="hljs-number">0</span>])</span>.
<span class="hljs-keyword">-callback</span> init<span class="hljs-params">(Args :: term())</span> -&gt; {ok, State :: term<span class="hljs-params">()</span>}.
""");
    }

    [Fact]
    public void GenServer()
    {
        AssertHighlighter("erlang",
"""
-module(counter).
-behaviour(gen_server).
-export([start_link/0, init/1, handle_call/3, handle_cast/2]).

start_link() ->
    gen_server:start_link({local, ?MODULE}, ?MODULE, [], []).

init([]) ->
    {ok, 0}.

handle_call(get, _From, Count) ->
    {reply, Count, Count};
handle_call(_Request, _From, State) ->
    {reply, ignored, State}.

handle_cast(increment, Count) ->
    {noreply, Count + 1}.
""",
"""
<span class="hljs-keyword">-module</span><span class="hljs-params">(counter)</span>.
<span class="hljs-keyword">-behaviour</span><span class="hljs-params">(gen_server)</span>.
<span class="hljs-keyword">-export</span><span class="hljs-params">([start_link/<span class="hljs-number">0</span>, init/<span class="hljs-number">1</span>, handle_call/<span class="hljs-number">3</span>, handle_cast/<span class="hljs-number">2</span>])</span>.

<span class="hljs-function"><span class="hljs-title">start_link</span><span class="hljs-params">()</span> -&gt;</span>
    gen_server:start_link({local, ?MODULE}, ?MODULE, [], []).

<span class="hljs-function"><span class="hljs-title">init</span><span class="hljs-params">([])</span> -&gt;</span>
    {ok, <span class="hljs-number">0</span>}.

<span class="hljs-function"><span class="hljs-title">handle_call</span><span class="hljs-params">(get, _From, Count)</span> -&gt;</span>
    {reply, Count, Count};
<span class="hljs-function"><span class="hljs-title">handle_call</span><span class="hljs-params">(_Request, _From, State)</span> -&gt;</span>
    {reply, ignored, State}.

<span class="hljs-function"><span class="hljs-title">handle_cast</span><span class="hljs-params">(increment, Count)</span> -&gt;</span>
    {noreply, Count + <span class="hljs-number">1</span>}.
""");
    }

    [Fact]
    public void ListComprehension()
    {
        AssertHighlighter("erlang",
"""
squares(L) -> [X * X || X <- L, X > 0].
pairs() -> [{X, Y} || X <- [1, 2], Y <- [a, b]].
bins() -> << <<B>> || <<B>> <= <<1, 2, 3>> >>.
""",
"""
<span class="hljs-function"><span class="hljs-title">squares</span><span class="hljs-params">(L)</span> -&gt;</span> [X * X || X &lt;- L, X &gt; <span class="hljs-number">0</span>].
<span class="hljs-function"><span class="hljs-title">pairs</span><span class="hljs-params">()</span> -&gt;</span> [{X, Y} || X &lt;- [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>], Y &lt;- [a, b]].
<span class="hljs-function"><span class="hljs-title">bins</span><span class="hljs-params">()</span> -&gt;</span> &lt;&lt; &lt;&lt;B&gt;&gt; || &lt;&lt;B&gt;&gt; &lt;= &lt;&lt;<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>&gt;&gt; &gt;&gt;.
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("erlang",
"""
f(A, B) ->
    A == B, A /= B, A =:= B, A =/= B,
    A =< B, A >= B, A < B, A > B,
    A andalso B, A orelse B,
    not A, A and B, A or B, A xor B,
    A band B, A bor B, A bxor B, bnot A, A bsl 2, A bsr 2,
    A div B, A rem B,
    [1] ++ [2] -- [1],
    Pid ! msg.
""",
"""
<span class="hljs-function"><span class="hljs-title">f</span><span class="hljs-params">(A, B)</span> -&gt;</span>
    A == B, A /= B, A =:= B, A =/= B,
    A =&lt; B, A &gt;= B, A &lt; B, A &gt; B,
    A <span class="hljs-keyword">andalso</span> B, A <span class="hljs-keyword">orelse</span> B,
    <span class="hljs-keyword">not</span> A, A <span class="hljs-keyword">and</span> B, A <span class="hljs-keyword">or</span> B, A <span class="hljs-keyword">xor</span> B,
    A <span class="hljs-keyword">band</span> B, A <span class="hljs-keyword">bor</span> B, A <span class="hljs-keyword">bxor</span> B, <span class="hljs-keyword">bnot</span> A, A <span class="hljs-keyword">bsl</span> <span class="hljs-number">2</span>, A <span class="hljs-keyword">bsr</span> <span class="hljs-number">2</span>,
    A <span class="hljs-keyword">div</span> B, A <span class="hljs-keyword">rem</span> B,
    [<span class="hljs-number">1</span>] ++ [<span class="hljs-number">2</span>] -- [<span class="hljs-number">1</span>],
    Pid ! msg.
""");
    }

    [Fact]
    public void Maps()
    {
        AssertHighlighter("erlang",
"""
M = #{name => "x", age => 1},
M2 = M#{age := 2},
#{name := Name} = M.
""",
"""
M = #{name =&gt; <span class="hljs-string">&quot;x&quot;</span>, age =&gt; <span class="hljs-number">1</span>},
M2 = M#{age := <span class="hljs-number">2</span>},
#{name := Name} = M.
""");
    }

    [Fact]
    public void MaybeExpr()
    {
        AssertHighlighter("erlang",
"""
f() ->
    maybe
        {ok, A} ?= a(),
        {ok, B} ?= b(A),
        A + B
    else
        {error, _} = E -> E
    end.
""",
"""
<span class="hljs-function"><span class="hljs-title">f</span><span class="hljs-params">()</span> -&gt;</span>
    <span class="hljs-keyword">maybe</span>
        {ok, A} ?= a(),
        {ok, B} ?= b(A),
        A + B
    <span class="hljs-keyword">else</span>
        {error, _} = E -&gt; E
    <span class="hljs-keyword">end</span>.
""");
    }

    [Fact]
    public void BeginBlock()
    {
        AssertHighlighter("erlang",
"""
f() ->
    begin
        X = 1,
        X + 1
    end.
""",
"""
<span class="hljs-function"><span class="hljs-title">f</span><span class="hljs-params">()</span> -&gt;</span>
    <span class="hljs-keyword">begin</span>
        X = <span class="hljs-number">1</span>,
        X + <span class="hljs-number">1</span>
    <span class="hljs-keyword">end</span>.
""");
    }

    [Fact]
    public void Variables()
    {
        AssertHighlighter("erlang",
"""
f(_Ignored, _, Var, VarName2) ->
    {Var, VarName2}.
""",
"""
<span class="hljs-function"><span class="hljs-title">f</span><span class="hljs-params">(_Ignored, _, Var, VarName2)</span> -&gt;</span>
    {Var, VarName2}.
""");
    }

    [Fact]
    public void AtomWithEnd()
    {
        AssertHighlighter("erlang",
"""
f(X) ->
    case X of
        pending -> append;
        sender -> ok
    end.
""",
"""
<span class="hljs-function"><span class="hljs-title">f</span><span class="hljs-params">(X)</span> -&gt;</span>
    <span class="hljs-keyword">case</span> X <span class="hljs-keyword">of</span>
        pending -&gt; append;
        sender -&gt; ok
    <span class="hljs-keyword">end</span>.
""");
    }

    [Fact]
    public void RemoteCalls()
    {
        AssertHighlighter("erlang",
"""
f() ->
    lists:foldl(fun(X, Acc) -> X + Acc end, 0, [1, 2, 3]),
    io_lib:format("~s", ["x"]),
    'my module':'my fun'(1),
    ets:new(table, [set, named_table]).
""",
"""
<span class="hljs-function"><span class="hljs-title">f</span><span class="hljs-params">()</span> -&gt;</span>
    lists:foldl(<span class="hljs-keyword">fun</span>(X, Acc) -&gt; X + Acc <span class="hljs-keyword">end</span>, <span class="hljs-number">0</span>, [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>]),
    io_lib:format(<span class="hljs-string">&quot;~s&quot;</span>, [<span class="hljs-string">&quot;x&quot;</span>]),
    &#x27;my module&#x27;:&#x27;my <span class="hljs-keyword">fun</span>&#x27;(1),
    ets:new(table, [set, named_table]).
""");
    }

    [Fact]
    public void OnLoad()
    {
        AssertHighlighter("erlang",
"""
-on_load(init/0).
-nifs([f/1]).
-moduledoc "Short doc.".
-doc false.
""",
"""
<span class="hljs-keyword">-on_load</span><span class="hljs-params">(init/<span class="hljs-number">0</span>)</span>.
<span class="hljs-keyword">-nifs</span><span class="hljs-params">([f/<span class="hljs-number">1</span>])</span>.
<span class="hljs-keyword">-moduledoc</span> <span class="hljs-string">&quot;Short doc.&quot;</span>.
<span class="hljs-keyword">-doc</span> false.
""");
    }

    [Fact]
    public void GuardSequences()
    {
        AssertHighlighter("erlang",
"""
valid(X, Y) when is_atom(X) andalso (Y =:= 1 orelse Y > 10) -> true;
valid(#{key := V}, _) when V >= 0, V =< 100; V == -1 -> true;
valid(_, _) -> false.
""",
"""
<span class="hljs-function"><span class="hljs-title">valid</span><span class="hljs-params">(X, Y)</span> <span class="hljs-keyword">when</span> is_atom(X) <span class="hljs-keyword">andalso</span> (Y =:= <span class="hljs-number">1</span> <span class="hljs-keyword">orelse</span> Y &gt; <span class="hljs-number">10</span>) -&gt;</span> <span class="hljs-literal">true</span>;
<span class="hljs-function"><span class="hljs-title">valid</span><span class="hljs-params">(#{key := V}, _)</span> <span class="hljs-keyword">when</span> V &gt;= <span class="hljs-number">0</span>, V =&lt; <span class="hljs-number">100</span>; V == -<span class="hljs-number">1</span> -&gt;</span> <span class="hljs-literal">true</span>;
<span class="hljs-function"><span class="hljs-title">valid</span><span class="hljs-params">(_, _)</span> -&gt;</span> <span class="hljs-literal">false</span>.
""");
    }

    [Fact]
    public void NestedParameters()
    {
        AssertHighlighter("erlang",
"""
-define(MAX(A, B), (if (A) > (B) -> (A); true -> (B) end)).
apply_twice(F, X) -> F(F(X)).
""",
"""
<span class="hljs-keyword">-define</span><span class="hljs-params">(MAX(A, B), (<span class="hljs-keyword">if</span> (A) &gt; (B) -&gt; (A); <span class="hljs-literal">true</span> -&gt; (B) <span class="hljs-keyword">end</span>))</span>.
<span class="hljs-function"><span class="hljs-title">apply_twice</span><span class="hljs-params">(F, X)</span> -&gt;</span> F(F(X)).
""");
    }

    [Fact]
    public void IllegalOps()
    {
        AssertHighlighter("erlang",
"""
f() -> X += 1.
""",
"""
<span class="hljs-function"><span class="hljs-title">f</span><span class="hljs-params">()</span> -&gt;</span> X += <span class="hljs-number">1</span>.
""");
    }

    [Fact]
    public void Unterminated()
    {
        AssertHighlighter("erlang",
"""
f() -> "unterminated
g() -> ok.
""",
"""
<span class="hljs-function"><span class="hljs-title">f</span><span class="hljs-params">()</span> -&gt;</span> <span class="hljs-string">&quot;unterminated
g() -&gt; ok.</span>
""");
    }
}
