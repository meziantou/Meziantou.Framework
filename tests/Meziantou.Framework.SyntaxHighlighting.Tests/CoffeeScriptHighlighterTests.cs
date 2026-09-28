namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class CoffeeScriptHighlighterTests
{
    [Fact]
    public void Alias1()
    {
        AssertHighlighter("coffee",
"""
square = (x) -> x * x
""",
"""
<span class="hljs-function"><span class="hljs-title">square</span> = <span class="hljs-params">(x)</span> -&gt;</span> x * x
""");
    }

    [Fact]
    public void Alias2()
    {
        AssertHighlighter("cson",
"""
name: "package"
version: "1.0.0"
""",
"""
name: <span class="hljs-string">&quot;package&quot;</span>
version: <span class="hljs-string">&quot;1.0.0&quot;</span>
""");
    }

    [Fact]
    public void Alias3()
    {
        AssertHighlighter("iced",
"""
await setTimeout defer(), 100
""",
"""
<span class="hljs-keyword">await</span> <span class="hljs-built_in">setTimeout</span> defer(), <span class="hljs-number">100</span>
""");
    }

    [Fact]
    public void Async()
    {
        AssertHighlighter("coffeescript",
"""
fetchData = (url) ->
  try
    response = await fetch url
    data = await response.json()
  catch error
    console.error error
  finally
    done()

gen = ->
  yield 1
  yield 2

import { readFile } from 'fs'
export default square
""",
"""
<span class="hljs-function"><span class="hljs-title">fetchData</span> = <span class="hljs-params">(url)</span> -&gt;</span>
  <span class="hljs-keyword">try</span>
    response = <span class="hljs-keyword">await</span> fetch url
    data = <span class="hljs-keyword">await</span> response.json()
  <span class="hljs-keyword">catch</span> error
    console.error error
  <span class="hljs-keyword">finally</span>
    done()
<span class="hljs-function">
<span class="hljs-title">gen</span> = -&gt;</span>
  <span class="hljs-keyword">yield</span> <span class="hljs-number">1</span>
  <span class="hljs-keyword">yield</span> <span class="hljs-number">2</span>

<span class="hljs-keyword">import</span> { readFile } <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;fs&#x27;</span>
<span class="hljs-keyword">export</span> <span class="hljs-keyword">default</span> square
""");
    }

    [Fact]
    public void Class()
    {
        AssertHighlighter("coffeescript",
"""
class Animal
  constructor: (@name) ->

  move: (meters) ->
    alert @name + " moved #{meters}m."

class Snake extends Animal
  move: ->
    alert "Slithering..."
    super 5

sam = new Snake "Sammy the Python"
sam.move()
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Animal</span>
  constructor: <span class="hljs-function"><span class="hljs-params">(@name)</span> -&gt;</span>

  move: <span class="hljs-function"><span class="hljs-params">(meters)</span> -&gt;</span>
    alert @name + <span class="hljs-string">&quot; moved <span class="hljs-subst">#{meters}</span>m.&quot;</span>

<span class="hljs-keyword">class</span> <span class="hljs-title class_">Snake</span> <span class="hljs-keyword">extends</span> <span class="hljs-title class_ inherited__">Animal</span>
  move: <span class="hljs-function">-&gt;</span>
    alert <span class="hljs-string">&quot;Slithering...&quot;</span>
    super <span class="hljs-number">5</span>

sam = <span class="hljs-keyword">new</span> Snake <span class="hljs-string">&quot;Sammy the Python&quot;</span>
sam.move()
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("coffeescript",
"""
# line comment
###
Block comment
###
x = 1 # trailing
###
unterminated block
y = 2
""",
"""
<span class="hljs-comment"># line comment</span>
<span class="hljs-comment">###
Block comment
###</span>
x = <span class="hljs-number">1</span> <span class="hljs-comment"># trailing</span>
<span class="hljs-comment">###
unterminated block
y = 2</span>
""");
    }

    [Fact]
    public void Control()
    {
        AssertHighlighter("coffeescript",
"""
mood = greatlyImproved if singing

if happy and knowsIt
  clapsHands()
  chaChaCha()
else
  showIt()

date = if friday then sue else jill

eat food for food in ['toast', 'cheese', 'wine'] when food isnt 'cheese'

countdown = (num for num in [10..1] by 2)

loop
  break unless running

until done
  work()

switch day
  when "Mon" then go work
  when "Sat", "Sun" then relax()
  else go relax
""",
"""
mood = greatlyImproved <span class="hljs-keyword">if</span> singing

<span class="hljs-keyword">if</span> happy <span class="hljs-keyword">and</span> knowsIt
  clapsHands()
  chaChaCha()
<span class="hljs-keyword">else</span>
  showIt()

date = <span class="hljs-keyword">if</span> friday <span class="hljs-keyword">then</span> sue <span class="hljs-keyword">else</span> jill

eat food <span class="hljs-keyword">for</span> food <span class="hljs-keyword">in</span> [<span class="hljs-string">&#x27;toast&#x27;</span>, <span class="hljs-string">&#x27;cheese&#x27;</span>, <span class="hljs-string">&#x27;wine&#x27;</span>] <span class="hljs-keyword">when</span> food <span class="hljs-keyword">isnt</span> <span class="hljs-string">&#x27;cheese&#x27;</span>

countdown = (num <span class="hljs-keyword">for</span> num <span class="hljs-keyword">in</span> [<span class="hljs-number">10</span>..<span class="hljs-number">1</span>] <span class="hljs-keyword">by</span> <span class="hljs-number">2</span>)

<span class="hljs-keyword">loop</span>
  <span class="hljs-keyword">break</span> <span class="hljs-keyword">unless</span> running

<span class="hljs-keyword">until</span> done
  work()

<span class="hljs-keyword">switch</span> day
  <span class="hljs-keyword">when</span> <span class="hljs-string">&quot;Mon&quot;</span> <span class="hljs-keyword">then</span> go work
  <span class="hljs-keyword">when</span> <span class="hljs-string">&quot;Sat&quot;</span>, <span class="hljs-string">&quot;Sun&quot;</span> <span class="hljs-keyword">then</span> relax()
  <span class="hljs-keyword">else</span> go relax
""");
    }

    [Fact]
    public void Edge()
    {
        AssertHighlighter("coffeescript",
"""
s = "unterminated
t = 1
x = typeof y is 'undefined'
z = delete obj.key
throw new Error "boom" unless ok
""",
"""
s = <span class="hljs-string">&quot;unterminated
t = 1
x = typeof y is &#x27;undefined&#x27;
z = delete obj.key
throw new Error &quot;</span>boom<span class="hljs-string">&quot; unless ok</span>
""");
    }

    [Fact]
    public void Embedded()
    {
        AssertHighlighter("coffeescript",
"""
hi = `function() {
  return [document.title, "Hello JavaScript"].join(": ");
}`
more = ```
  var x = 1;
```
""",
"""
hi = `<span class="language-javascript"><span class="hljs-keyword">function</span>(<span class="hljs-params"></span>) {
  <span class="hljs-keyword">return</span> [<span class="hljs-variable language_">document</span>.<span class="hljs-property">title</span>, <span class="hljs-string">&quot;Hello JavaScript&quot;</span>].<span class="hljs-title function_">join</span>(<span class="hljs-string">&quot;: &quot;</span>);
}</span>`
more = ```<span class="language-javascript">
  <span class="hljs-keyword">var</span> x = <span class="hljs-number">1</span>;
</span>```
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("coffeescript", "", "");
    }

    [Fact]
    public void Functions()
    {
        AssertHighlighter("coffeescript",
"""
square = (x) -> x * x
cube   = (x) -> square(x) * x
fill = (container, liquid = "coffee") ->
  "Filling the #{container} with #{liquid}..."
noop = ->
bound = (e) =>
  @handle e
race = (winner, runners...) ->
  print winner, runners
""",
"""
<span class="hljs-function"><span class="hljs-title">square</span> = <span class="hljs-params">(x)</span> -&gt;</span> x * x
<span class="hljs-function"><span class="hljs-title">cube</span>   = <span class="hljs-params">(x)</span> -&gt;</span> square(x) * x
<span class="hljs-function"><span class="hljs-title">fill</span> = <span class="hljs-params">(container, liquid = <span class="hljs-string">&quot;coffee&quot;</span>)</span> -&gt;</span>
  <span class="hljs-string">&quot;Filling the <span class="hljs-subst">#{container}</span> with <span class="hljs-subst">#{liquid}</span>...&quot;</span>
<span class="hljs-function"><span class="hljs-title">noop</span> = -&gt;</span>
<span class="hljs-function"><span class="hljs-title">bound</span> = <span class="hljs-params">(e)</span> =&gt;</span>
  @handle e
<span class="hljs-function"><span class="hljs-title">race</span> = <span class="hljs-params">(winner, runners...)</span> -&gt;</span>
  <span class="hljs-built_in">print</span> winner, runners
""");
    }

    [Fact]
    public void Hello()
    {
        AssertHighlighter("coffeescript",
"""
console.log "Hello, World!"
name = prompt "Name?"
alert "Hi, #{name}!"
""",
"""
console.log <span class="hljs-string">&quot;Hello, World!&quot;</span>
name = prompt <span class="hljs-string">&quot;Name?&quot;</span>
alert <span class="hljs-string">&quot;Hi, <span class="hljs-subst">#{name}</span>!&quot;</span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("coffeescript",
"""
a = 42
b = 3.14
c = 0xFF
d = 0b1010
e = 1e10
f = -5
g = 10 /2
r = [1..10]
x = [1...n]
h = list[2..]
i = .5 + 1.5e-3
""",
"""
a = <span class="hljs-number">42</span>
b = <span class="hljs-number">3.14</span>
c = <span class="hljs-number">0xFF</span>
d = <span class="hljs-number">0b1010</span>
e = <span class="hljs-number">1e10</span>
f = <span class="hljs-number">-5</span>
g = <span class="hljs-number">10</span> /<span class="hljs-number">2</span>
r = [<span class="hljs-number">1</span>..<span class="hljs-number">10</span>]
x = [<span class="hljs-number">1</span>...n]
h = list[<span class="hljs-number">2</span>..]
i = <span class="hljs-number">.5</span> + <span class="hljs-number">1.5e-3</span>
""");
    }

    [Fact]
    public void Objects()
    {
        AssertHighlighter("coffeescript",
"""
kids =
  brother:
    name: "Max"
    age:  11
  sister:
    name: "Ida"
    age:  9
settings = {default: yes, enabled: on, debug: off, verbose: no}
{a, b} = obj
[first, rest...] = list
""",
"""
kids =
  brother:
    name: <span class="hljs-string">&quot;Max&quot;</span>
    age:  <span class="hljs-number">11</span>
  sister:
    name: <span class="hljs-string">&quot;Ida&quot;</span>
    age:  <span class="hljs-number">9</span>
settings = {default: <span class="hljs-literal">yes</span>, enabled: <span class="hljs-literal">on</span>, debug: <span class="hljs-literal">off</span>, verbose: <span class="hljs-literal">no</span>}
{a, b} = obj
[first, rest...] = list
""");
    }

    [Fact]
    public void Regex()
    {
        AssertHighlighter("coffeescript",
"""
re = /^\d+$/g
x = 10 / 2 / 5
y = a / b
OPERATOR = /// ^ (
  ?: [-=]>             # function
   | [-+*/%<>&|^!?=]=  # compound assign
) ///
empty = //g
""",
"""
re = <span class="hljs-regexp">/^\d+$/g</span>
x = <span class="hljs-number">10</span> / <span class="hljs-number">2</span> / <span class="hljs-number">5</span>
y = a / b
OPERATOR = <span class="hljs-regexp">/// ^ (
  ?: [-=]&gt;             <span class="hljs-comment"># function</span>
   | [-+*/%&lt;&gt;&amp;|^!?=]=  <span class="hljs-comment"># compound assign</span>
) ///</span>
empty = //g
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("coffeescript",
""""
single = 'no #{interp} here'
double = "with #{1 + 2} interp"
block = '''
  multi
  line
'''
html = """
  <strong>
    #{name}
  </strong>
"""
esc = "quote \" and \n"
"""",
"""
single = <span class="hljs-string">&#x27;no #{interp} here&#x27;</span>
double = <span class="hljs-string">&quot;with <span class="hljs-subst">#{<span class="hljs-number">1</span> + <span class="hljs-number">2</span>}</span> interp&quot;</span>
block = <span class="hljs-string">&#x27;&#x27;&#x27;
  multi
  line
&#x27;&#x27;&#x27;</span>
html = <span class="hljs-string">&quot;&quot;&quot;
  &lt;strong&gt;
    <span class="hljs-subst">#{name}</span>
  &lt;/strong&gt;
&quot;&quot;&quot;</span>
esc = <span class="hljs-string">&quot;quote \&quot; and \n&quot;</span>
""");
    }
}
