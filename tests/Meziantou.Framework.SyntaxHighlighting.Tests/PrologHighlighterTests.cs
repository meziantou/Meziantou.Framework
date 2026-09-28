namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class PrologHighlighterTests
{    [Fact]
    public void FactsAndRules()
    {
        AssertHighlighter("prolog",
"""
% Family relationships
parent(tom, bob).
parent(bob, ann).
parent(bob, pat).

grandparent(X, Z) :-
    parent(X, Y),
    parent(Y, Z).

?- grandparent(tom, Who).
""",
"""
<span class="hljs-comment">% Family relationships</span>
parent(tom, bob).
parent(bob, ann).
parent(bob, pat).

grandparent(<span class="hljs-symbol">X</span>, <span class="hljs-symbol">Z</span>) :-
    parent(<span class="hljs-symbol">X</span>, <span class="hljs-symbol">Y</span>),
    parent(<span class="hljs-symbol">Y</span>, <span class="hljs-symbol">Z</span>).

?- grandparent(tom, <span class="hljs-symbol">Who</span>).
""");
    }

    [Fact]
    public void Lists_ClauseEndingDotIsNotPartOfTheNumber()
    {
        AssertHighlighter("prolog",
"""
append([], L, L).
append([H|T], L, [H|R]) :-
    append(T, L, R).

len([], 0).
len([_|T], N) :- len(T, N0), N is N0 + 1.

member(X, [X|_]) :- !.
member(X, [_|T]) :- member(X, T).
""",
"""
append([], <span class="hljs-symbol">L</span>, <span class="hljs-symbol">L</span>).
append([<span class="hljs-symbol">H</span>|<span class="hljs-symbol">T</span>], <span class="hljs-symbol">L</span>, [<span class="hljs-symbol">H</span>|<span class="hljs-symbol">R</span>]) :-
    append(<span class="hljs-symbol">T</span>, <span class="hljs-symbol">L</span>, <span class="hljs-symbol">R</span>).

len([], <span class="hljs-number">0</span>).
len([<span class="hljs-symbol">_</span>|<span class="hljs-symbol">T</span>], <span class="hljs-symbol">N</span>) :- len(<span class="hljs-symbol">T</span>, <span class="hljs-symbol">N0</span>), <span class="hljs-symbol">N</span> is <span class="hljs-symbol">N0</span> + <span class="hljs-number">1</span>.

member(<span class="hljs-symbol">X</span>, [<span class="hljs-symbol">X</span>|<span class="hljs-symbol">_</span>]) :- !.
member(<span class="hljs-symbol">X</span>, [<span class="hljs-symbol">_</span>|<span class="hljs-symbol">T</span>]) :- member(<span class="hljs-symbol">X</span>, <span class="hljs-symbol">T</span>).
""");
    }

    [Fact]
    public void Directives()
    {
        AssertHighlighter("prolog",
"""
:- module(utils, [max/3, greet/1]).
:- use_module(library(lists)).
:- dynamic counter/1.

max(X, Y, X) :- X >= Y, !.
max(_, Y, Y).
""",
"""
:- module(utils, [max/<span class="hljs-number">3</span>, greet/<span class="hljs-number">1</span>]).
:- use_module(library(lists)).
:- dynamic counter/<span class="hljs-number">1</span>.

max(<span class="hljs-symbol">X</span>, <span class="hljs-symbol">Y</span>, <span class="hljs-symbol">X</span>) :- <span class="hljs-symbol">X</span> &gt;= <span class="hljs-symbol">Y</span>, !.
max(<span class="hljs-symbol">_</span>, <span class="hljs-symbol">Y</span>, <span class="hljs-symbol">Y</span>).
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("prolog",
"""
greet(Name) :-
    format("Hello, ~w!~n", [Name]),
    atom_codes('hello world', Codes),
    X = `back\`tick`,
    Y = 'it''s',
    Z = "esc \"q\"".
""",
"""
greet(<span class="hljs-symbol">Name</span>) :-
    format(<span class="hljs-string">&quot;Hello, ~w!~n&quot;</span>, [<span class="hljs-symbol">Name</span>]),
    atom_codes(<span class="hljs-string">&#x27;hello world&#x27;</span>, <span class="hljs-symbol">Codes</span>),
    <span class="hljs-symbol">X</span> = <span class="hljs-string">`back\`tick`</span>,
    <span class="hljs-symbol">Y</span> = <span class="hljs-string">&#x27;it&#x27;</span><span class="hljs-string">&#x27;s&#x27;</span>,
    <span class="hljs-symbol">Z</span> = <span class="hljs-string">&quot;esc \&quot;q\&quot;&quot;</span>.
""");
    }

    [Fact]
    public void Numbers_CharacterCodes()
    {
        AssertHighlighter("prolog",
"""
n(42). n(-7). n(3.14). n(1.0e10). n(0x1F). n(0b1010). n(0o17).
c(0'a). c(0' ). c(0'\'). c(0'\s). c(0'\n). c(0'%). c(0''').
""",
"""
n(<span class="hljs-number">42</span>). n(<span class="hljs-number">-7</span>). n(<span class="hljs-number">3.14</span>). n(<span class="hljs-number">1.0e10</span>). n(<span class="hljs-number">0x1F</span>). n(<span class="hljs-number">0b1010</span>). n(<span class="hljs-number">0o17</span>).
c(<span class="hljs-string">0&#x27;a</span>). c(<span class="hljs-string">0&#x27; </span>). c(<span class="hljs-string">0&#x27;\&#x27;</span>). c(<span class="hljs-string">0&#x27;\s</span>). c(<span class="hljs-string">0&#x27;\n</span>). c(<span class="hljs-string">0&#x27;%</span>). c(<span class="hljs-string">0&#x27;&#x27;&#x27;</span>).
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("prolog",
"""
% line comment
/* block
   comment */
foo :- bar. % trailing
/* unterminated
baz.

""",
"""
<span class="hljs-comment">% line comment</span>
<span class="hljs-comment">/* block
   comment */</span>
foo :- bar. <span class="hljs-comment">% trailing</span>
<span class="hljs-comment">/* unterminated
baz.
</span>
""");
    }

    [Fact]
    public void DefiniteClauseGrammar()
    {
        AssertHighlighter("prolog",
"""
greeting --> [hello], name.
name --> [world].
name --> [prolog].

digits([D|T]) --> digit(D), digits(T).
digits([D]) --> digit(D).
digit(D) --> [D], { code_type(D, digit) }.
""",
"""
greeting --&gt; [hello], name.
name --&gt; [world].
name --&gt; [prolog].

digits([<span class="hljs-symbol">D</span>|<span class="hljs-symbol">T</span>]) --&gt; digit(<span class="hljs-symbol">D</span>), digits(<span class="hljs-symbol">T</span>).
digits([<span class="hljs-symbol">D</span>]) --&gt; digit(<span class="hljs-symbol">D</span>).
digit(<span class="hljs-symbol">D</span>) --&gt; [<span class="hljs-symbol">D</span>], { code_type(<span class="hljs-symbol">D</span>, digit) }.
""");
    }

    [Fact]
    public void IfThenElseAndCut()
    {
        AssertHighlighter("prolog",
"""
classify(X, Class) :-
    (   X < 0
    ->  Class = negative
    ;   X =:= 0
    ->  Class = zero
    ;   Class = positive
    ).

loop(N) :- N > 0, !, writeln(N), N1 is N - 1, loop(N1).
loop(_).

safe_div(X, Y, Z) :- catch(Z is X / Y, error(E, _), (print_message(error, E), fail)).
""",
"""
classify(<span class="hljs-symbol">X</span>, <span class="hljs-symbol">Class</span>) :-
    (   <span class="hljs-symbol">X</span> &lt; <span class="hljs-number">0</span>
    -&gt;  <span class="hljs-symbol">Class</span> = negative
    ;   <span class="hljs-symbol">X</span> =:= <span class="hljs-number">0</span>
    -&gt;  <span class="hljs-symbol">Class</span> = zero
    ;   <span class="hljs-symbol">Class</span> = positive
    ).

loop(<span class="hljs-symbol">N</span>) :- <span class="hljs-symbol">N</span> &gt; <span class="hljs-number">0</span>, !, writeln(<span class="hljs-symbol">N</span>), <span class="hljs-symbol">N1</span> is <span class="hljs-symbol">N</span> - <span class="hljs-number">1</span>, loop(<span class="hljs-symbol">N1</span>).
loop(<span class="hljs-symbol">_</span>).

safe_div(<span class="hljs-symbol">X</span>, <span class="hljs-symbol">Y</span>, <span class="hljs-symbol">Z</span>) :- catch(<span class="hljs-symbol">Z</span> is <span class="hljs-symbol">X</span> / <span class="hljs-symbol">Y</span>, error(<span class="hljs-symbol">E</span>, <span class="hljs-symbol">_</span>), (print_message(error, <span class="hljs-symbol">E</span>), fail)).
""");
    }

    [Fact]
    public void Variables()
    {
        AssertHighlighter("prolog",
"""
test(_Ignored, _, Var, CamelCase, X1) :- Var = CamelCase, X1 == _Ignored.
""",
"""
test(<span class="hljs-symbol">_Ignored</span>, <span class="hljs-symbol">_</span>, <span class="hljs-symbol">Var</span>, <span class="hljs-symbol">CamelCase</span>, <span class="hljs-symbol">X1</span>) :- <span class="hljs-symbol">Var</span> = <span class="hljs-symbol">CamelCase</span>, <span class="hljs-symbol">X1</span> == <span class="hljs-symbol">_Ignored</span>.
""");
    }

    [Fact]
    public void Unterminated()
    {
        AssertHighlighter("prolog",
"""
a :- write('unterminated
b.
c :- write("also
d.

""",
"""
a :- write(<span class="hljs-string">&#x27;unterminated
b.
c :- write(&quot;also
d.
</span>
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("prolog",
"""
bonjour('été', "héllo ✓"). % commentaire
""",
"""
bonjour(<span class="hljs-string">&#x27;été&#x27;</span>, <span class="hljs-string">&quot;héllo ✓&quot;</span>). <span class="hljs-comment">% commentaire</span>
""");
    }
}
