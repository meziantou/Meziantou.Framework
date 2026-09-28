namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class PyconHighlighterTests
{
    [Fact]
    public void Expression()
    {
        AssertHighlighter("pycon",
"""
>>> 1 + 1
2
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-number">1</span> + <span class="hljs-number">1</span></span>
2
""");
    }

    [Fact]
    public void Print()
    {
        AssertHighlighter("pycon",
"""
>>> print("Hello, World!")
Hello, World!
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;Hello, World!&quot;</span>)</span>
Hello, World!
""");
    }

    [Fact]
    public void StatementsWithoutOutput()
    {
        AssertHighlighter("pycon",
"""
>>> x = 42
>>> y = x * 2
>>> y
84
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">x = <span class="hljs-number">42</span></span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">y = x * <span class="hljs-number">2</span></span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">y</span>
84
""");
    }

    [Fact]
    public void FunctionDefinition()
    {
        AssertHighlighter("pycon",
"""
>>> def greet(name):
...     return f"Hello, {name}!"
...
>>> greet("World")
'Hello, World!'
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-keyword">def</span> <span class="hljs-title function_">greet</span>(<span class="hljs-params">name</span>):</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">    <span class="hljs-keyword">return</span> <span class="hljs-string">f&quot;Hello, <span class="hljs-subst">{name}</span>!&quot;</span></span>
<span class="hljs-meta prompt_">...</span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">greet(<span class="hljs-string">&quot;World&quot;</span>)</span>
&#x27;Hello, World!&#x27;
""");
    }

    [Fact]
    public void ForLoop()
    {
        AssertHighlighter("pycon",
"""
>>> for i in range(3):
...     print(i)
...
0
1
2
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-keyword">for</span> i <span class="hljs-keyword">in</span> <span class="hljs-built_in">range</span>(<span class="hljs-number">3</span>):</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">    <span class="hljs-built_in">print</span>(i)</span>
<span class="hljs-meta prompt_">...</span>
0
1
2
""");
    }

    [Fact]
    public void ClassDefinition()
    {
        AssertHighlighter("pycon",
"""
>>> class Point:
...     def __init__(self, x, y):
...         self.x = x
...         self.y = y
...     def __repr__(self):
...         return f"Point({self.x}, {self.y})"
...
>>> Point(1, 2)
Point(1, 2)
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-keyword">class</span> <span class="hljs-title class_">Point</span>:</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">    <span class="hljs-keyword">def</span> <span class="hljs-title function_">__init__</span>(<span class="hljs-params">self, x, y</span>):</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">        <span class="hljs-variable language_">self</span>.x = x</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">        <span class="hljs-variable language_">self</span>.y = y</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">    <span class="hljs-keyword">def</span> <span class="hljs-title function_">__repr__</span>(<span class="hljs-params">self</span>):</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">        <span class="hljs-keyword">return</span> <span class="hljs-string">f&quot;Point(<span class="hljs-subst">{self.x}</span>, <span class="hljs-subst">{self.y}</span>)&quot;</span></span>
<span class="hljs-meta prompt_">...</span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">Point(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>)</span>
Point(1, 2)
""");
    }

    [Fact]
    public void Traceback()
    {
        AssertHighlighter("pycon",
"""
>>> 1 / 0
Traceback (most recent call last):
  File "<stdin>", line 1, in <module>
ZeroDivisionError: division by zero
>>> x = 1
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-number">1</span> / <span class="hljs-number">0</span></span>
Traceback (most recent call last):
  File &quot;&lt;stdin&gt;&quot;, line 1, in &lt;module&gt;
ZeroDivisionError: division by zero
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">x = <span class="hljs-number">1</span></span>
""");
    }

    [Fact]
    public void TracebackWithQuotes()
    {
        AssertHighlighter("pycon",
"""
>>> int("abc")
Traceback (most recent call last):
  File "<stdin>", line 1, in <module>
ValueError: invalid literal for int() with base 10: 'abc'
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-built_in">int</span>(<span class="hljs-string">&quot;abc&quot;</span>)</span>
Traceback (most recent call last):
  File &quot;&lt;stdin&gt;&quot;, line 1, in &lt;module&gt;
ValueError: invalid literal for int() with base 10: &#x27;abc&#x27;
""");
    }

    [Fact]
    public void StringSpanningContinuationLines()
    {
        AssertHighlighter("pycon",
""""
>>> s = """first line
... second line"""
>>> print(s)
first line
second line
"""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">s = <span class="hljs-string">&quot;&quot;&quot;first line</span></span>
<span class="hljs-meta prompt_">...</span> <span class="language-python"><span class="hljs-string">second line&quot;&quot;&quot;</span></span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-built_in">print</span>(s)</span>
first line
second line
""");
    }

    [Fact]
    public void MultilineBrackets()
    {
        AssertHighlighter("pycon",
"""
>>> data = {
...     "name": "Alice",
...     "age": 30,
... }
>>> data["name"]
'Alice'
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">data = {</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">    <span class="hljs-string">&quot;name&quot;</span>: <span class="hljs-string">&quot;Alice&quot;</span>,</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">    <span class="hljs-string">&quot;age&quot;</span>: <span class="hljs-number">30</span>,</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">}</span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">data[<span class="hljs-string">&quot;name&quot;</span>]</span>
&#x27;Alice&#x27;
""");
    }

    [Fact]
    public void PromptWithoutCode()
    {
        AssertHighlighter("pycon",
"""
>>>
>>> x = 1
>>>
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">x = <span class="hljs-number">1</span></span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span>
""");
    }

    [Fact]
    public void BlankLines()
    {
        AssertHighlighter("pycon",
"""
>>> x = 1

>>> y = 2

2
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">x = <span class="hljs-number">1</span></span>

<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">y = <span class="hljs-number">2</span></span>

2
""");
    }

    [Fact]
    public void ImportAndComment()
    {
        AssertHighlighter("pycon",
"""
>>> import math  # the math module
>>> math.pi
3.141592653589793
>>> from collections import Counter
>>> Counter("hello")
Counter({'l': 2, 'h': 1, 'e': 1, 'o': 1})
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-keyword">import</span> math  <span class="hljs-comment"># the math module</span></span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">math.pi</span>
3.141592653589793
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-keyword">from</span> collections <span class="hljs-keyword">import</span> Counter</span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">Counter(<span class="hljs-string">&quot;hello&quot;</span>)</span>
Counter({&#x27;l&#x27;: 2, &#x27;h&#x27;: 1, &#x27;e&#x27;: 1, &#x27;o&#x27;: 1})
""");
    }

    [Fact]
    public void ListComprehension()
    {
        AssertHighlighter("pycon",
"""
>>> [x ** 2 for x in range(5) if x % 2 == 0]
[0, 4, 16]
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">[x ** <span class="hljs-number">2</span> <span class="hljs-keyword">for</span> x <span class="hljs-keyword">in</span> <span class="hljs-built_in">range</span>(<span class="hljs-number">5</span>) <span class="hljs-keyword">if</span> x % <span class="hljs-number">2</span> == <span class="hljs-number">0</span>]</span>
[0, 4, 16]
""");
    }

    [Fact]
    public void OutputLooksLikeCode()
    {
        AssertHighlighter("pycon",
"""
>>> print("def f(): return None")
def f(): return None
>>> True
True
>>> None
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;def f(): return None&quot;</span>)</span>
def f(): return None
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-literal">True</span></span>
True
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-literal">None</span></span>
""");
    }

    [Fact]
    public void OutputStartingWithDotsAfterPromptLine_IsAContinuation()
    {
        AssertHighlighter("pycon",
"""
>>> print("... loading")
... loading
>>> print("...")
...
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;... loading&quot;</span>)</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">loading</span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;...&quot;</span>)</span>
<span class="hljs-meta prompt_">...</span>
""");
    }

    [Fact]
    public void OutputStartingWithDotsAfterOutput_StaysPlain()
    {
        AssertHighlighter("pycon",
"""
>>> print("Loading\n... done")
Loading
... done
>>> x = 1
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;Loading\n... done&quot;</span>)</span>
Loading
... done
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">x = <span class="hljs-number">1</span></span>
""");
    }

    [Fact]
    public void PromptWithTrailingSpace()
    {
        AssertHighlighter("pycon",
">>> \n>>> x = 1",
"<span class=\"hljs-meta prompt_\">&gt;&gt;&gt;</span> \n<span class=\"hljs-meta prompt_\">&gt;&gt;&gt;</span> <span class=\"language-python\">x = <span class=\"hljs-number\">1</span></span>");
    }

    [Fact]
    public void PythonReplAlias()
    {
        AssertHighlighter("python-repl",
"""
>>> import sys
>>> sys.version_info.major
3
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-keyword">import</span> sys</span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">sys.version_info.major</span>
3
""");
    }

    [Fact]
    public void OutputStartingWithPrompt_IsAPrompt()
    {
        AssertHighlighter("pycon",
"""
>>> print(">>> not a prompt")
>>> not a prompt
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;&gt;&gt;&gt; not a prompt&quot;</span>)</span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-keyword">not</span> a prompt</span>
""");
    }

    [Fact]
    public void UnterminatedString_DoesNotLeakIntoNextStatement()
    {
        AssertHighlighter("pycon",
"""
>>> print('it's')
  File "<stdin>", line 1
    print('it's')
               ^
SyntaxError: unterminated string literal (detected at line 1)
>>> x = 1
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-built_in">print</span>(<span class="hljs-string">&#x27;it&#x27;</span>s<span class="hljs-string">&#x27;)</span></span>
  File &quot;&lt;stdin&gt;&quot;, line 1
    print(&#x27;it&#x27;s&#x27;)
               ^
SyntaxError: unterminated string literal (detected at line 1)
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">x = <span class="hljs-number">1</span></span>
""");
    }

    [Fact]
    public void UnterminatedTripleQuotedString_EndsAtNextStatement()
    {
        AssertHighlighter("pycon",
""""
>>> s = """never closed
... still open
>>> x = 1
"""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">s = <span class="hljs-string">&quot;&quot;&quot;never closed</span></span>
<span class="hljs-meta prompt_">...</span> <span class="language-python"><span class="hljs-string">still open</span></span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">x = <span class="hljs-number">1</span></span>
""");
    }

    [Fact]
    public void Decorator()
    {
        AssertHighlighter("pycon",
"""
>>> @functools.cache
... def fib(n):
...     return n if n < 2 else fib(n - 1) + fib(n - 2)
...
>>> fib(30)
832040
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-meta">@functools.cache</span></span>
<span class="hljs-meta prompt_">...</span> <span class="language-python"><span class="hljs-keyword">def</span> <span class="hljs-title function_">fib</span>(<span class="hljs-params">n</span>):</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">    <span class="hljs-keyword">return</span> n <span class="hljs-keyword">if</span> n &lt; <span class="hljs-number">2</span> <span class="hljs-keyword">else</span> fib(n - <span class="hljs-number">1</span>) + fib(n - <span class="hljs-number">2</span>)</span>
<span class="hljs-meta prompt_">...</span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">fib(<span class="hljs-number">30</span>)</span>
832040
""");
    }

    [Fact]
    public void Async()
    {
        AssertHighlighter("pycon",
"""
>>> import asyncio
>>> async def main():
...     await asyncio.sleep(1)
...     return "done"
...
>>> asyncio.run(main())
'done'
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-keyword">import</span> asyncio</span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-keyword">async</span> <span class="hljs-keyword">def</span> <span class="hljs-title function_">main</span>():</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">    <span class="hljs-keyword">await</span> asyncio.sleep(<span class="hljs-number">1</span>)</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">    <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;done&quot;</span></span>
<span class="hljs-meta prompt_">...</span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">asyncio.run(main())</span>
&#x27;done&#x27;
""");
    }

    [Fact]
    public void HelpOutput()
    {
        AssertHighlighter("pycon",
"""
>>> help(len)
Help on built-in function len in module builtins:

len(obj, /)
    Return the number of items in a container.
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-built_in">help</span>(<span class="hljs-built_in">len</span>)</span>
Help on built-in function len in module builtins:

len(obj, /)
    Return the number of items in a container.
""");
    }

    [Fact]
    public void WithStatement()
    {
        AssertHighlighter("pycon",
"""
>>> with open("file.txt") as f:
...     for line in f:
...         print(line, end="")
...
hello
world
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-keyword">with</span> <span class="hljs-built_in">open</span>(<span class="hljs-string">&quot;file.txt&quot;</span>) <span class="hljs-keyword">as</span> f:</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">    <span class="hljs-keyword">for</span> line <span class="hljs-keyword">in</span> f:</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">        <span class="hljs-built_in">print</span>(line, end=<span class="hljs-string">&quot;&quot;</span>)</span>
<span class="hljs-meta prompt_">...</span>
hello
world
""");
    }

    [Fact]
    public void TryExcept()
    {
        AssertHighlighter("pycon",
"""
>>> try:
...     raise ValueError("bad")
... except ValueError as e:
...     print(e)
...
bad
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-keyword">try</span>:</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">    <span class="hljs-keyword">raise</span> ValueError(<span class="hljs-string">&quot;bad&quot;</span>)</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python"><span class="hljs-keyword">except</span> ValueError <span class="hljs-keyword">as</span> e:</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">    <span class="hljs-built_in">print</span>(e)</span>
<span class="hljs-meta prompt_">...</span>
bad
""");
    }

    [Fact]
    public void IndentedPrompt_IsOutput()
    {
        AssertHighlighter("pycon",
"""
    >>> x = 1
    ... y
""",
"""
    &gt;&gt;&gt; x = 1
    ... y
""");
    }

    [Fact]
    public void PromptInsideALine_IsOutput()
    {
        AssertHighlighter("pycon",
"""
x >>> y
""",
"""
x &gt;&gt;&gt; y
""");
    }

    [Fact]
    public void TabAfterPrompt_IsOutput()
    {
        AssertHighlighter("pycon",
">>>\tx = 1",
"&gt;&gt;&gt;\tx = 1");
    }

    [Fact]
    public void DoctestStyle()
    {
        AssertHighlighter("pycon",
"""
>>> factorial(5)
120
>>> [factorial(n) for n in range(6)]
[1, 1, 2, 6, 24, 120]
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">factorial(<span class="hljs-number">5</span>)</span>
120
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">[factorial(n) <span class="hljs-keyword">for</span> n <span class="hljs-keyword">in</span> <span class="hljs-built_in">range</span>(<span class="hljs-number">6</span>)]</span>
[1, 1, 2, 6, 24, 120]
""");
    }

    [Fact]
    public void NumbersAndOperators()
    {
        AssertHighlighter("pycon",
"""
>>> 0x1F + 0o17 + 0b101 + 1_000_000 + 3.14e-2 + 2j
(1000059.0314+2j)
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-number">0x1F</span> + <span class="hljs-number">0o17</span> + <span class="hljs-number">0b101</span> + <span class="hljs-number">1_000_000</span> + <span class="hljs-number">3.14e-2</span> + <span class="hljs-number">2j</span></span>
(1000059.0314+2j)
""");
    }

    [Fact]
    public void FString()
    {
        AssertHighlighter("pycon",
"""
>>> name = "Bob"
>>> f"{name!r:>10}"
"     'Bob'"
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">name = <span class="hljs-string">&quot;Bob&quot;</span></span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-string">f&quot;<span class="hljs-subst">{name!r:&gt;<span class="hljs-number">10</span>}</span>&quot;</span></span>
&quot;     &#x27;Bob&#x27;&quot;
""");
    }

    [Fact]
    public void Lambda()
    {
        AssertHighlighter("pycon",
"""
>>> square = lambda x: x * x
>>> list(map(square, [1, 2, 3]))
[1, 4, 9]
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">square = <span class="hljs-keyword">lambda</span> x: x * x</span>
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-built_in">list</span>(<span class="hljs-built_in">map</span>(square, [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>]))</span>
[1, 4, 9]
""");
    }

    [Fact]
    public void KeyboardInterrupt()
    {
        AssertHighlighter("pycon",
"""
>>> while True:
...     pass
...
^CTraceback (most recent call last):
  File "<stdin>", line 1, in <module>
KeyboardInterrupt
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-keyword">while</span> <span class="hljs-literal">True</span>:</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">    <span class="hljs-keyword">pass</span></span>
<span class="hljs-meta prompt_">...</span>
^CTraceback (most recent call last):
  File &quot;&lt;stdin&gt;&quot;, line 1, in &lt;module&gt;
KeyboardInterrupt
""");
    }

    [Fact]
    public void EllipsisLiteral()
    {
        AssertHighlighter("pycon",
"""
>>> ...
Ellipsis
>>> x = ...
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">...</span>
Ellipsis
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python">x = ...</span>
""");
    }

    [Fact]
    public void MatchStatement()
    {
        AssertHighlighter("pycon",
"""
>>> match command.split():
...     case [action]:
...         print(action)
...     case _:
...         pass
...
""",
"""
<span class="hljs-meta prompt_">&gt;&gt;&gt;</span> <span class="language-python"><span class="hljs-keyword">match</span> command.split():</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">    <span class="hljs-keyword">case</span> [action]:</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">        <span class="hljs-built_in">print</span>(action)</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">    <span class="hljs-keyword">case</span> _:</span>
<span class="hljs-meta prompt_">...</span> <span class="language-python">        <span class="hljs-keyword">pass</span></span>
<span class="hljs-meta prompt_">...</span>
""");
    }
}
