namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class NodeReplHighlighterTests
{
    [Fact]
    public void Expression()
    {
        AssertHighlighter("node-repl",
"""
> 1 + 1
2
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-number">1</span> + <span class="hljs-number">1</span></span>
2
""");
    }

    [Fact]
    public void ConsoleLog()
    {
        AssertHighlighter("node-repl",
"""
> console.log('Hello, World!')
Hello, World!
undefined
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(<span class="hljs-string">&#x27;Hello, World!&#x27;</span>)</span>
Hello, World!
undefined
""");
    }

    [Fact]
    public void StatementsWithOutput()
    {
        AssertHighlighter("node-repl",
"""
> const x = 42
undefined
> x * 2
84
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-keyword">const</span> x = <span class="hljs-number">42</span></span>
undefined
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript">x * <span class="hljs-number">2</span></span>
84
""");
    }

    [Fact]
    public void FunctionDefinition()
    {
        AssertHighlighter("node-repl",
"""
> function add(a, b) {
... return a + b;
... }
undefined
> add(1, 2)
3
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-keyword">function</span> <span class="hljs-title function_">add</span>(<span class="hljs-params">a, b</span>) {</span>
<span class="hljs-meta prompt_">...</span> <span class="language-javascript"><span class="hljs-keyword">return</span> a + b;</span>
<span class="hljs-meta prompt_">...</span> <span class="language-javascript">}</span>
undefined
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-title function_">add</span>(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>)</span>
3
""");
    }

    [Fact]
    public void ArrowFunction()
    {
        AssertHighlighter("node-repl",
"""
> const square = (x) => x * x;
undefined
> [1, 2, 3].map(square)
[ 1, 4, 9 ]
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-keyword">const</span> <span class="hljs-title function_">square</span> = (<span class="hljs-params">x</span>) =&gt; x * x;</span>
undefined
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript">[<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>].<span class="hljs-title function_">map</span>(square)</span>
[ 1, 4, 9 ]
""");
    }

    [Fact]
    public void ObjectLiteral()
    {
        AssertHighlighter("node-repl",
"""
> const person = { name: 'Alice', age: 30 }
undefined
> person
{ name: 'Alice', age: 30 }
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-keyword">const</span> person = { <span class="hljs-attr">name</span>: <span class="hljs-string">&#x27;Alice&#x27;</span>, <span class="hljs-attr">age</span>: <span class="hljs-number">30</span> }</span>
undefined
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript">person</span>
{ name: &#x27;Alice&#x27;, age: 30 }
""");
    }

    [Fact]
    public void TemplateLiteral()
    {
        AssertHighlighter("node-repl",
"""
> `Hello ${name}!`
'Hello Bob!'
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-string">`Hello <span class="hljs-subst">${name}</span>!`</span></span>
&#x27;Hello Bob!&#x27;
""");
    }

    [Fact]
    public void TemplateLiteralSpanningContinuationLines()
    {
        AssertHighlighter("node-repl",
"""
> const s = `first
... second ${x}
... third`
undefined
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-keyword">const</span> s = <span class="hljs-string">`first</span></span>
<span class="hljs-meta prompt_">...</span> <span class="language-javascript"><span class="hljs-string">second <span class="hljs-subst">${x}</span></span></span>
<span class="hljs-meta prompt_">...</span> <span class="language-javascript"><span class="hljs-string">third`</span></span>
undefined
""");
    }

    [Fact]
    public void UncaughtErrors()
    {
        AssertHighlighter("node-repl",
"""
> foo()
Uncaught ReferenceError: foo is not defined
> null.x
Uncaught TypeError: Cannot read properties of null (reading 'x')
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-title function_">foo</span>()</span>
Uncaught ReferenceError: foo is not defined
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-literal">null</span>.<span class="hljs-property">x</span></span>
Uncaught TypeError: Cannot read properties of null (reading &#x27;x&#x27;)
""");
    }

    [Fact]
    public void Require()
    {
        AssertHighlighter("node-repl",
"""
> const fs = require('fs')
undefined
> fs.readFileSync('a.txt', 'utf8')
'hello\n'
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-keyword">const</span> fs = <span class="hljs-built_in">require</span>(<span class="hljs-string">&#x27;fs&#x27;</span>)</span>
undefined
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript">fs.<span class="hljs-title function_">readFileSync</span>(<span class="hljs-string">&#x27;a.txt&#x27;</span>, <span class="hljs-string">&#x27;utf8&#x27;</span>)</span>
&#x27;hello\n&#x27;
""");
    }

    [Fact]
    public void TopLevelAwait()
    {
        AssertHighlighter("node-repl",
"""
> await Promise.resolve(42)
42
> await fetch('https://example.com').then(r => r.status)
200
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-keyword">await</span> <span class="hljs-title class_">Promise</span>.<span class="hljs-title function_">resolve</span>(<span class="hljs-number">42</span>)</span>
42
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-keyword">await</span> <span class="hljs-title function_">fetch</span>(<span class="hljs-string">&#x27;https://example.com&#x27;</span>).<span class="hljs-title function_">then</span>(<span class="hljs-function"><span class="hljs-params">r</span> =&gt;</span> r.<span class="hljs-property">status</span>)</span>
200
""");
    }

    [Fact]
    public void ClassDefinition()
    {
        AssertHighlighter("node-repl",
"""
> class Point {
...   constructor(x, y) { this.x = x; this.y = y; }
... }
undefined
> new Point(1, 2)
Point { x: 1, y: 2 }
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-keyword">class</span> <span class="hljs-title class_">Point</span> {</span>
<span class="hljs-meta prompt_">...</span> <span class="language-javascript">  <span class="hljs-title function_">constructor</span>(<span class="hljs-params">x, y</span>) { <span class="hljs-variable language_">this</span>.<span class="hljs-property">x</span> = x; <span class="hljs-variable language_">this</span>.<span class="hljs-property">y</span> = y; }</span>
<span class="hljs-meta prompt_">...</span> <span class="language-javascript">}</span>
undefined
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-keyword">new</span> <span class="hljs-title class_">Point</span>(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>)</span>
Point { x: 1, y: 2 }
""");
    }

    [Fact]
    public void ForLoop()
    {
        AssertHighlighter("node-repl",
"""
> for (let i = 0; i < 3; i++) {
...   console.log(i);
... }
0
1
2
undefined
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-keyword">for</span> (<span class="hljs-keyword">let</span> i = <span class="hljs-number">0</span>; i &lt; <span class="hljs-number">3</span>; i++) {</span>
<span class="hljs-meta prompt_">...</span> <span class="language-javascript">  <span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(i);</span>
<span class="hljs-meta prompt_">...</span> <span class="language-javascript">}</span>
0
1
2
undefined
""");
    }

    [Fact]
    public void AsyncFunction()
    {
        AssertHighlighter("node-repl",
"""
> async function main() {
...   const res = await fetch(url);
...   return res.json();
... }
undefined
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-keyword">async</span> <span class="hljs-keyword">function</span> <span class="hljs-title function_">main</span>(<span class="hljs-params"></span>) {</span>
<span class="hljs-meta prompt_">...</span> <span class="language-javascript">  <span class="hljs-keyword">const</span> res = <span class="hljs-keyword">await</span> <span class="hljs-title function_">fetch</span>(url);</span>
<span class="hljs-meta prompt_">...</span> <span class="language-javascript">  <span class="hljs-keyword">return</span> res.<span class="hljs-title function_">json</span>();</span>
<span class="hljs-meta prompt_">...</span> <span class="language-javascript">}</span>
undefined
""");
    }

    [Fact]
    public void Regex()
    {
        AssertHighlighter("node-repl",
"""
> /ab+c/gi.test('abbc')
true
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript">/ab+c/gi.<span class="hljs-title function_">test</span>(<span class="hljs-string">&#x27;abbc&#x27;</span>)</span>
true
""");
    }

    [Fact]
    public void DotCommands()
    {
        AssertHighlighter("node-repl",
"""
> .help
.break    Sometimes you get stuck, this gets you out
.exit     Exit the REPL
> .exit
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript">.<span class="hljs-property">help</span></span>
.break    Sometimes you get stuck, this gets you out
.exit     Exit the REPL
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript">.<span class="hljs-property">exit</span></span>
""");
    }

    [Fact]
    public void Underscore()
    {
        AssertHighlighter("node-repl",
"""
> 3 + 4
7
> _ * 2
14
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-number">3</span> + <span class="hljs-number">4</span></span>
7
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript">_ * <span class="hljs-number">2</span></span>
14
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("node-repl",
"""
> const café = '☕'
undefined
> café
'☕'
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-keyword">const</span> café = <span class="hljs-string">&#x27;☕&#x27;</span></span>
undefined
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript">café</span>
&#x27;☕&#x27;
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("node-repl",
"> 0xFF + 1e3 + 10n + 0b101 + 1_000\n",
"<span class=\"hljs-meta prompt_\">&gt;</span> <span class=\"language-javascript\"><span class=\"hljs-number\">0xFF</span> + <span class=\"hljs-number\">1e3</span> + <span class=\"hljs-number\">10n</span> + <span class=\"hljs-number\">0b101</span> + <span class=\"hljs-number\">1_000</span></span>\n");
    }

    [Fact]
    public void PromptWithoutCode()
    {
        AssertHighlighter("node-repl",
"""
>
> x = 1
>
""",
"""
<span class="hljs-meta prompt_">&gt;</span>
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript">x = <span class="hljs-number">1</span></span>
<span class="hljs-meta prompt_">&gt;</span>
""");
    }

    [Fact]
    public void PromptWithTrailingSpace()
    {
        AssertHighlighter("node-repl",
"> \n> x = 1",
"<span class=\"hljs-meta prompt_\">&gt;</span> \n<span class=\"hljs-meta prompt_\">&gt;</span> <span class=\"language-javascript\">x = <span class=\"hljs-number\">1</span></span>");
    }

    [Fact]
    public void BlankLines()
    {
        AssertHighlighter("node-repl",
"""
> x = 1

> y = 2

2
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript">x = <span class="hljs-number">1</span></span>

<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript">y = <span class="hljs-number">2</span></span>

2
""");
    }

    [Fact]
    public void ContinuationPromptWithoutCode()
    {
        AssertHighlighter("node-repl",
"""
> if (x) {
...
... }
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-keyword">if</span> (x) {</span>
<span class="hljs-meta prompt_">...</span>
<span class="hljs-meta prompt_">...</span> <span class="language-javascript">}</span>
""");
    }

    [Fact]
    public void OutputStartingWithDotsAfterPromptLine_IsAContinuation()
    {
        AssertHighlighter("node-repl",
"""
> console.log('... loading')
... loading
undefined
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(<span class="hljs-string">&#x27;... loading&#x27;</span>)</span>
<span class="hljs-meta prompt_">...</span> <span class="language-javascript">loading</span>
undefined
""");
    }

    [Fact]
    public void OutputStartingWithDotsAfterOutput_StaysPlain()
    {
        AssertHighlighter("node-repl",
"""
> console.log('Loading\n... done')
Loading
... done
undefined
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(<span class="hljs-string">&#x27;Loading\n... done&#x27;</span>)</span>
Loading
... done
undefined
""");
    }

    [Fact]
    public void UnterminatedString_DoesNotLeakIntoNextStatement()
    {
        AssertHighlighter("node-repl",
"""
> 'it's'
Uncaught SyntaxError: Unexpected identifier 's'
> x = 1
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-string">&#x27;it&#x27;</span>s<span class="hljs-string">&#x27;</span></span>
Uncaught SyntaxError: Unexpected identifier &#x27;s&#x27;
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript">x = <span class="hljs-number">1</span></span>
""");
    }

    [Fact]
    public void UnterminatedTemplateLiteral_EndsAtNextStatement()
    {
        AssertHighlighter("node-repl",
"""
> `never closed
... still open
> x = 1
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-string">`never closed</span></span>
<span class="hljs-meta prompt_">...</span> <span class="language-javascript"><span class="hljs-string">still open</span></span>
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript">x = <span class="hljs-number">1</span></span>
""");
    }

    [Fact]
    public void UnterminatedComment_EndsAtNextStatement()
    {
        AssertHighlighter("node-repl",
"""
> /* comment
... still
> x = 1
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-comment">/* comment</span></span>
<span class="hljs-meta prompt_">...</span> <span class="language-javascript"><span class="hljs-comment">still</span></span>
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript">x = <span class="hljs-number">1</span></span>
""");
    }

    [Fact]
    public void OutputStartingWithPrompt_IsAPrompt()
    {
        AssertHighlighter("node-repl",
"""
> '> not a prompt'
'> not a prompt'
""",
"""
<span class="hljs-meta prompt_">&gt;</span> <span class="language-javascript"><span class="hljs-string">&#x27;&gt; not a prompt&#x27;</span></span>
&#x27;&gt; not a prompt&#x27;
""");
    }

    [Fact]
    public void IndentedPrompt_IsOutput()
    {
        AssertHighlighter("node-repl",
"""
  > x = 1
""",
"""
  &gt; x = 1
""");
    }

    [Fact]
    public void PromptInsideALine_IsOutput()
    {
        AssertHighlighter("node-repl",
"""
x > y
""",
"""
x &gt; y
""");
    }

    [Fact]
    public void TabAfterPrompt_IsOutput()
    {
        AssertHighlighter("node-repl",
">\tx = 1",
"&gt;\tx = 1");
    }

    [Fact]
    public void EmptyInput()
    {
        AssertHighlighter("node-repl",
"",
"");
    }
}
