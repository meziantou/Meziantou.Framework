namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class ClojureReplHighlighterTests
{
    [Fact]
    public void Expression()
    {
        AssertHighlighter("clojure-repl",
"""
user=> (+ 1 2)
3
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-name"><span class="hljs-built_in">+</span></span> <span class="hljs-number">1</span> <span class="hljs-number">2</span>)</span>
3
""");
    }

    [Fact]
    public void Println()
    {
        AssertHighlighter("clojure-repl",
"""
user=> (println "Hello, World!")
Hello, World!
nil
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-name">println</span> <span class="hljs-string">&quot;Hello, World!&quot;</span>)</span>
Hello, World!
nil
""");
    }

    [Fact]
    public void FunctionDefinition()
    {
        AssertHighlighter("clojure-repl",
"""
user=> (defn square [x] (* x x))
#'user/square
user=> (square 4)
16
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-keyword">defn</span> <span class="hljs-title">square</span> [x] (<span class="hljs-name"><span class="hljs-built_in">*</span></span> x x))</span>
#&#x27;user/square
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-name">square</span> <span class="hljs-number">4</span>)</span>
16
""");
    }

    [Fact]
    public void ContinuationPrompts()
    {
        AssertHighlighter("clojure-repl",
"""
user=> (defn greet
  #_=> [name]
  #_=> (str "Hello, " name))
#'user/greet
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-keyword">defn</span> <span class="hljs-title">greet</span></span>
<span class="hljs-meta prompt_">  #_=&gt;</span><span class="language-clojure"> [name]</span>
<span class="hljs-meta prompt_">  #_=&gt;</span><span class="language-clojure"> (<span class="hljs-name"><span class="hljs-built_in">str</span></span> <span class="hljs-string">&quot;Hello, &quot;</span> name))</span>
#&#x27;user/greet
""");
    }

    [Fact]
    public void Map()
    {
        AssertHighlighter("clojure-repl",
"""
user=> (def m {:a 1 :b 2})
#'user/m
user=> (get m :a)
1
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-keyword">def</span> <span class="hljs-title">m</span> {<span class="hljs-symbol">:a</span> <span class="hljs-number">1</span> <span class="hljs-symbol">:b</span> <span class="hljs-number">2</span>})</span>
#&#x27;user/m
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-name"><span class="hljs-built_in">get</span></span> m <span class="hljs-symbol">:a</span>)</span>
1
""");
    }

    [Fact]
    public void NamespacePrompt()
    {
        AssertHighlighter("clojure-repl",
"""
my.app.core=> (require '[clojure.string :as str])
nil
my.app.core=> (str/upper-case "abc")
"ABC"
""",
"""
<span class="hljs-meta prompt_">my.app.core=&gt;</span><span class="language-clojure"> (<span class="hljs-name">require</span> &#x27;[clojure.string <span class="hljs-symbol">:as</span> str])</span>
nil
<span class="hljs-meta prompt_">my.app.core=&gt;</span><span class="language-clojure"> (<span class="hljs-name">str/upper-case</span> <span class="hljs-string">&quot;abc&quot;</span>)</span>
&quot;ABC&quot;
""");
    }

    [Fact]
    public void HyphenatedNamespacePrompt()
    {
        AssertHighlighter("clojure-repl",
"""
my-app.core-test=> (run-tests)
""",
"""
<span class="hljs-meta prompt_">my-app.core-test=&gt;</span><span class="language-clojure"> (<span class="hljs-name">run-tests</span>)</span>
""");
    }

    [Fact]
    public void PromptWithoutNamespace()
    {
        AssertHighlighter("clojure-repl",
"""
=> (inc 1)
2
""",
"""
<span class="hljs-meta prompt_">=&gt;</span><span class="language-clojure"> (<span class="hljs-name"><span class="hljs-built_in">inc</span></span> <span class="hljs-number">1</span>)</span>
2
""");
    }

    [Fact]
    public void Vector()
    {
        AssertHighlighter("clojure-repl",
"""
user=> (map inc [1 2 3])
(2 3 4)
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-name"><span class="hljs-built_in">map</span></span> inc [<span class="hljs-number">1</span> <span class="hljs-number">2</span> <span class="hljs-number">3</span>])</span>
(2 3 4)
""");
    }

    [Fact]
    public void Let()
    {
        AssertHighlighter("clojure-repl",
"""
user=> (let [x 1 y 2] (+ x y))
3
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-name"><span class="hljs-built_in">let</span></span> [x <span class="hljs-number">1</span> y <span class="hljs-number">2</span>] (<span class="hljs-name"><span class="hljs-built_in">+</span></span> x y))</span>
3
""");
    }

    [Fact]
    public void Error()
    {
        AssertHighlighter("clojure-repl",
"""
user=> (/ 1 0)
Execution error (ArithmeticException) at user/eval1 (REPL:1).
Divide by zero
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (/ <span class="hljs-number">1</span> <span class="hljs-number">0</span>)</span>
Execution error (ArithmeticException) at user/eval1 (REPL:1).
Divide by zero
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("clojure-repl",
"""
user=> ; a comment
user=> (+ 1 2) ; trailing
3
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> <span class="hljs-comment">; a comment</span></span>
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-name"><span class="hljs-built_in">+</span></span> <span class="hljs-number">1</span> <span class="hljs-number">2</span>) <span class="hljs-comment">; trailing</span></span>
3
""");
    }

    [Fact]
    public void KeywordsAndCharacters()
    {
        AssertHighlighter("clojure-repl",
"""
user=> [\a \newline :kw ::ns-kw 'sym]
[\a \newline :kw :user/ns-kw sym]
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> [<span class="hljs-character">\a</span> <span class="hljs-character">\newline</span> <span class="hljs-symbol">:kw</span> <span class="hljs-symbol">::ns-kw</span> &#x27;sym]</span>
[\a \newline :kw :user/ns-kw sym]
""");
    }

    [Fact]
    public void Regex()
    {
        AssertHighlighter("clojure-repl",
"""
user=> (re-find #"\d+" "abc123")
"123"
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-name"><span class="hljs-built_in">re-find</span></span> <span class="hljs-regex">#&quot;\d+&quot;</span> <span class="hljs-string">&quot;abc123&quot;</span>)</span>
&quot;123&quot;
""");
    }

    [Fact]
    public void AnonymousFunction()
    {
        AssertHighlighter("clojure-repl",
"""
user=> (#(* % 2) 21)
42
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (#(<span class="hljs-name"><span class="hljs-built_in">*</span></span> % <span class="hljs-number">2</span>) <span class="hljs-number">21</span>)</span>
42
""");
    }

    [Fact]
    public void JavaInterop()
    {
        AssertHighlighter("clojure-repl",
"""
user=> (.toUpperCase "abc")
"ABC"
user=> (Math/sqrt 16)
4.0
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-name">.toUpperCase</span> <span class="hljs-string">&quot;abc&quot;</span>)</span>
&quot;ABC&quot;
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-name">Math/sqrt</span> <span class="hljs-number">16</span>)</span>
4.0
""");
    }

    [Fact]
    public void DocOutput()
    {
        AssertHighlighter("clojure-repl",
"""
user=> (doc map)
-------------------------
clojure.core/map
([f] [f coll])
  Returns a lazy sequence
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-name">doc</span> map)</span>
-------------------------
clojure.core/map
([f] [f coll])
  Returns a lazy sequence
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("clojure-repl",
"""
user=> [42 -1.5 22/7 1e10 0xFF 36rZZ 42N 1.5M]
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> [<span class="hljs-number">42</span> <span class="hljs-number">-1.5</span> <span class="hljs-number">22/7</span> <span class="hljs-number">1e10</span> <span class="hljs-number">0xFF</span> <span class="hljs-number">36rZZ</span> <span class="hljs-number">42N</span> <span class="hljs-number">1.5M</span>]</span>
""");
    }

    [Fact]
    public void Deref()
    {
        AssertHighlighter("clojure-repl",
"""
user=> @(atom 1)
1
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> @(<span class="hljs-name"><span class="hljs-built_in">atom</span></span> <span class="hljs-number">1</span>)</span>
1
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("clojure-repl",
"""
user=> (str "héllo" "☕")
"héllo☕"
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-name"><span class="hljs-built_in">str</span></span> <span class="hljs-string">&quot;héllo&quot;</span> <span class="hljs-string">&quot;☕&quot;</span>)</span>
&quot;héllo☕&quot;
""");
    }

    [Fact]
    public void PromptWithoutCode()
    {
        AssertHighlighter("clojure-repl",
"user=>\nuser=> ",
"<span class=\"hljs-meta prompt_\">user=&gt;</span>\n<span class=\"hljs-meta prompt_\">user=&gt;</span><span class=\"language-clojure\"> </span>");
    }

    [Fact]
    public void UnterminatedString_DoesNotLeakIntoNextForm()
    {
        AssertHighlighter("clojure-repl",
"""
user=> (str "never closed)
user=> (+ 1 2)
3
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-name"><span class="hljs-built_in">str</span></span> <span class="hljs-string">&quot;never closed)</span></span>
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-name"><span class="hljs-built_in">+</span></span> <span class="hljs-number">1</span> <span class="hljs-number">2</span>)</span>
3
""");
    }

    [Fact]
    public void UnbalancedForm_NextFormStartsFromRoot()
    {
        AssertHighlighter("clojure-repl",
"""
user=> (defn f [x]
user=> (+ 1 2)
3
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-keyword">defn</span> <span class="hljs-title">f</span> [x]</span>
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-name"><span class="hljs-built_in">+</span></span> <span class="hljs-number">1</span> <span class="hljs-number">2</span>)</span>
3
""");
    }

    [Fact]
    public void BlankLinesBeforeContinuationPrompt_AreNotPartOfIt()
    {
        AssertHighlighter("clojure-repl",
"""
user=> (defn f [x]


  #_=> x)
""",
"""
<span class="hljs-meta prompt_">user=&gt;</span><span class="language-clojure"> (<span class="hljs-keyword">defn</span> <span class="hljs-title">f</span> [x]</span>


<span class="hljs-meta prompt_">  #_=&gt;</span><span class="language-clojure"> x)</span>
""");
    }

    [Fact]
    public void IndentedPrompt_IsOutput()
    {
        AssertHighlighter("clojure-repl",
"""
  user=> (inc 1)
""",
"""
  user=&gt; (inc 1)
""");
    }

    [Fact]
    public void PromptInsideALine_IsOutput()
    {
        AssertHighlighter("clojure-repl",
"""
x user=> y
""",
"""
x user=&gt; y
""");
    }

    [Fact]
    public void EmptyInput()
    {
        AssertHighlighter("clojure-repl",
"",
"");
    }
}
