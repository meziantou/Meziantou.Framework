namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class PythonHighlighterTests
{
    [Fact]
    public void Hello()
    {
        AssertHighlighter("python",
"""
def main():
    print("Hello, world!")

if __name__ == "__main__":
    main()
""",
"""
<span class="hljs-keyword">def</span> <span class="hljs-title function_">main</span>():
    <span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;Hello, world!&quot;</span>)

<span class="hljs-keyword">if</span> __name__ == <span class="hljs-string">&quot;__main__&quot;</span>:
    main()
""");
    }

    [Fact]
    public void Imports()
    {
        AssertHighlighter("python",
"""
import os
import sys as system
from collections import OrderedDict, defaultdict
from . import sibling
from ..pkg.mod import thing as t
""",
"""
<span class="hljs-keyword">import</span> os
<span class="hljs-keyword">import</span> sys <span class="hljs-keyword">as</span> system
<span class="hljs-keyword">from</span> collections <span class="hljs-keyword">import</span> OrderedDict, defaultdict
<span class="hljs-keyword">from</span> . <span class="hljs-keyword">import</span> sibling
<span class="hljs-keyword">from</span> ..pkg.mod <span class="hljs-keyword">import</span> thing <span class="hljs-keyword">as</span> t
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("python",
"""
# a comment
x = 1  # trailing TODO: fix
# type: ignore
y = []  # type: List[int]
z = {}  # type: Dict[str, int] # extra
""",
"""
<span class="hljs-comment"># a comment</span>
x = <span class="hljs-number">1</span>  <span class="hljs-comment"># trailing <span class="hljs-doctag">TODO:</span> fix</span>
<span class="hljs-comment"># type: ignore</span>
y = []  <span class="hljs-comment"># type: <span class="hljs-type">List</span>[<span class="hljs-built_in">int</span>]</span>
z = {}  <span class="hljs-comment"># type: <span class="hljs-type">Dict</span>[<span class="hljs-built_in">str</span>, <span class="hljs-built_in">int</span>] # extra</span>
""");
    }

    [Fact]
    public void StringsBasic()
    {
        AssertHighlighter("python",
"""
a = "double"
b = 'single'
c = "esc\"aped\n"
d = 'it\'s'
e = ""
f = ''
""",
"""
a = <span class="hljs-string">&quot;double&quot;</span>
b = <span class="hljs-string">&#x27;single&#x27;</span>
c = <span class="hljs-string">&quot;esc\&quot;aped\n&quot;</span>
d = <span class="hljs-string">&#x27;it\&#x27;s&#x27;</span>
e = <span class="hljs-string">&quot;&quot;</span>
f = <span class="hljs-string">&#x27;&#x27;</span>
""");
    }

    [Fact]
    public void StringsPrefixes()
    {
        AssertHighlighter("python",
"""
a = u"unicode"
b = r"raw\d+"
c = b"bytes\x00"
d = br"raw bytes"
e = Rb'x'
f = BR"y"
g = rb'\n'
h = U'u'
""",
"""
a = <span class="hljs-string">u&quot;unicode&quot;</span>
b = <span class="hljs-string">r&quot;raw\d+&quot;</span>
c = <span class="hljs-string">b&quot;bytes\x00&quot;</span>
d = <span class="hljs-string">br&quot;raw bytes&quot;</span>
e = <span class="hljs-string">Rb&#x27;x&#x27;</span>
f = <span class="hljs-string">BR&quot;y&quot;</span>
g = <span class="hljs-string">rb&#x27;\n&#x27;</span>
h = <span class="hljs-string">U&#x27;u&#x27;</span>
""");
    }

    [Fact]
    public void StringsTriple()
    {
        AssertHighlighter("python",
""""
doc = """
Multi-line
  string "with" quotes
"""
single = '''
Another
'''
raw = r"""\d+"""
byt = b'''bytes'''
"""",
"""
doc = <span class="hljs-string">&quot;&quot;&quot;
Multi-line
  string &quot;with&quot; quotes
&quot;&quot;&quot;</span>
single = <span class="hljs-string">&#x27;&#x27;&#x27;
Another
&#x27;&#x27;&#x27;</span>
raw = <span class="hljs-string">r&quot;&quot;&quot;\d+&quot;&quot;&quot;</span>
byt = <span class="hljs-string">b&#x27;&#x27;&#x27;bytes&#x27;&#x27;&#x27;</span>
""");
    }

    [Fact]
    public void DocstringDoctest()
    {
        AssertHighlighter("python",
""""
def add(a, b):
    """Add numbers.

    >>> add(1, 2)
    3
    ... continued
    """
    return a + b
"""",
"""
<span class="hljs-keyword">def</span> <span class="hljs-title function_">add</span>(<span class="hljs-params">a, b</span>):
    <span class="hljs-string">&quot;&quot;&quot;Add numbers.

    &gt;&gt;&gt; add(1, 2)
    3
    ... continued
    &quot;&quot;&quot;</span>
    <span class="hljs-keyword">return</span> a + b
""");
    }

    [Fact]
    public void Fstrings()
    {
        AssertHighlighter("python",
"""
name = "x"
a = f"Hello {name}!"
b = f'{value!r:>10}'
c = F"{x=}"
d = f"{{literal}} {x}"
e = f"{d["key"]}"
f2 = f'{d["key"]}'
g = fr"raw {x}\d"
h = rf'{x}'
""",
"""
name = <span class="hljs-string">&quot;x&quot;</span>
a = <span class="hljs-string">f&quot;Hello <span class="hljs-subst">{name}</span>!&quot;</span>
b = <span class="hljs-string">f&#x27;<span class="hljs-subst">{value!r:&gt;<span class="hljs-number">10</span>}</span>&#x27;</span>
c = <span class="hljs-string">F&quot;<span class="hljs-subst">{x=}</span>&quot;</span>
d = <span class="hljs-string">f&quot;{{literal}} <span class="hljs-subst">{x}</span>&quot;</span>
e = <span class="hljs-string">f&quot;<span class="hljs-subst">{d[<span class="hljs-string">&quot;key&quot;</span>]}</span>&quot;</span>
f2 = <span class="hljs-string">f&#x27;<span class="hljs-subst">{d[<span class="hljs-string">&quot;key&quot;</span>]}</span>&#x27;</span>
g = <span class="hljs-string">fr&quot;raw <span class="hljs-subst">{x}</span>\d&quot;</span>
h = <span class="hljs-string">rf&#x27;<span class="hljs-subst">{x}</span>&#x27;</span>
""");
    }

    [Fact]
    public void FstringFormatSpec()
    {
        AssertHighlighter("python",
"""
a = f"{value:.2f}"
b = f"{value:{width}.{precision}}"
c = f"{x:>{w}}"
d = f"{ {'a': 1}['a'] }"
e = f"{n:,}"
f2 = f"{dt:%Y-%m-%d}"
""",
"""
a = <span class="hljs-string">f&quot;<span class="hljs-subst">{value:<span class="hljs-number">.2</span>f}</span>&quot;</span>
b = <span class="hljs-string">f&quot;<span class="hljs-subst">{value:{width}.{precision}}</span>&quot;</span>
c = <span class="hljs-string">f&quot;<span class="hljs-subst">{x:&gt;{w}}</span>&quot;</span>
d = <span class="hljs-string">f&quot;<span class="hljs-subst">{ {<span class="hljs-string">&#x27;a&#x27;</span>: <span class="hljs-number">1</span>}[<span class="hljs-string">&#x27;a&#x27;</span>] }</span>&quot;</span>
e = <span class="hljs-string">f&quot;<span class="hljs-subst">{n:,}</span>&quot;</span>
f2 = <span class="hljs-string">f&quot;<span class="hljs-subst">{dt:%Y-%m-%d}</span>&quot;</span>
""");
    }

    [Fact]
    public void FstringTriple()
    {
        AssertHighlighter("python",
""""
msg = f"""
Name: {user.name}
Age: {user.age + 1}
{{escaped}}
"""
msg2 = f'''{x}'''
"""",
"""
msg = <span class="hljs-string">f&quot;&quot;&quot;
Name: <span class="hljs-subst">{user.name}</span>
Age: <span class="hljs-subst">{user.age + <span class="hljs-number">1</span>}</span>
{{escaped}}
&quot;&quot;&quot;</span>
msg2 = <span class="hljs-string">f&#x27;&#x27;&#x27;<span class="hljs-subst">{x}</span>&#x27;&#x27;&#x27;</span>
""");
    }

    [Fact]
    public void FstringNested()
    {
        AssertHighlighter("python",
"""
a = f"{f'{x}'}"
b = f"{'yes' if ok else 'no'}"
c = f"{len(items)} items"
d = f"{x # comment
}"
""",
"""
a = <span class="hljs-string">f&quot;<span class="hljs-subst">{<span class="hljs-string">f&#x27;<span class="hljs-subst">{x}</span>&#x27;</span>}</span>&quot;</span>
b = <span class="hljs-string">f&quot;<span class="hljs-subst">{<span class="hljs-string">&#x27;yes&#x27;</span> <span class="hljs-keyword">if</span> ok <span class="hljs-keyword">else</span> <span class="hljs-string">&#x27;no&#x27;</span>}</span>&quot;</span>
c = <span class="hljs-string">f&quot;<span class="hljs-subst">{<span class="hljs-built_in">len</span>(items)}</span> items&quot;</span>
d = <span class="hljs-string">f&quot;<span class="hljs-subst">{x # comment
}</span>&quot;</span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("python",
"""
a = 42
b = 1_000_000
c = 0x_FF
d = 0o755
e = 0b1010_1010
f = 3.14
g = .5
h = 10.
i = 1e10
j = 1.5E-3
k = 2j
l = 3.14J
m = 1_0.0_1e1_0
n = 0
o = 00
p = 10L
q = 0XDEAD
r = 1e5j
""",
"""
a = <span class="hljs-number">42</span>
b = <span class="hljs-number">1_000_000</span>
c = <span class="hljs-number">0x_FF</span>
d = <span class="hljs-number">0o755</span>
e = <span class="hljs-number">0b1010_1010</span>
f = <span class="hljs-number">3.14</span>
g = <span class="hljs-number">.5</span>
h = <span class="hljs-number">10.</span>
i = <span class="hljs-number">1e10</span>
j = <span class="hljs-number">1.5E-3</span>
k = <span class="hljs-number">2j</span>
l = <span class="hljs-number">3.14J</span>
m = <span class="hljs-number">1_0.0_1e1_0</span>
n = <span class="hljs-number">0</span>
o = <span class="hljs-number">00</span>
p = <span class="hljs-number">10L</span>
q = <span class="hljs-number">0XDEAD</span>
r = <span class="hljs-number">1e5j</span>
""");
    }

    [Fact]
    public void NumbersEdge()
    {
        AssertHighlighter("python",
"""
x = 1if y else 2
z = 0..hex()
f(.5)
a[1:2]
b = -1
c = +2.0
d = 1__0
e = 0x
""",
"""
x = <span class="hljs-number">1</span><span class="hljs-keyword">if</span> y <span class="hljs-keyword">else</span> <span class="hljs-number">2</span>
z = <span class="hljs-number">0.</span>.hex()
f(<span class="hljs-number">.5</span>)
a[<span class="hljs-number">1</span>:<span class="hljs-number">2</span>]
b = -<span class="hljs-number">1</span>
c = +<span class="hljs-number">2.0</span>
d = 1__0
e = 0x
""");
    }

    [Fact]
    public void Decorators()
    {
        AssertHighlighter("python",
"""
@property
def name(self):
    return self._name

@app.route("/users/<id>", methods=["GET"])
def user(id):
    pass

@dataclass(frozen=True, order=1)
class Point:
    x: int = 0
    y: int = 0

    @staticmethod  # comment
    def origin(): ...
""",
"""
<span class="hljs-meta">@property</span>
<span class="hljs-keyword">def</span> <span class="hljs-title function_">name</span>(<span class="hljs-params">self</span>):
    <span class="hljs-keyword">return</span> <span class="hljs-variable language_">self</span>._name

<span class="hljs-meta">@app.route(<span class="hljs-params"><span class="hljs-string">&quot;/users/&lt;id&gt;&quot;</span>, methods=[<span class="hljs-string">&quot;GET&quot;</span>]</span>)</span>
<span class="hljs-keyword">def</span> <span class="hljs-title function_">user</span>(<span class="hljs-params"><span class="hljs-built_in">id</span></span>):
    <span class="hljs-keyword">pass</span>

<span class="hljs-meta">@dataclass(<span class="hljs-params">frozen=<span class="hljs-literal">True</span>, order=<span class="hljs-number">1</span></span>)</span>
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Point</span>:
    x: <span class="hljs-built_in">int</span> = <span class="hljs-number">0</span>
    y: <span class="hljs-built_in">int</span> = <span class="hljs-number">0</span>

<span class="hljs-meta">    @staticmethod  </span><span class="hljs-comment"># comment</span>
    <span class="hljs-keyword">def</span> <span class="hljs-title function_">origin</span>(): ...
""");
    }

    [Fact]
    public void DecoratorIndented()
    {
        AssertHighlighter("python",
"""
class A:
    @functools.lru_cache(maxsize=None)
    def f(self): pass
  @x
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span>:
<span class="hljs-meta">    @functools.lru_cache(<span class="hljs-params">maxsize=<span class="hljs-literal">None</span></span>)</span>
    <span class="hljs-keyword">def</span> <span class="hljs-title function_">f</span>(<span class="hljs-params">self</span>): <span class="hljs-keyword">pass</span>
<span class="hljs-meta">  @x</span>
""");
    }

    [Fact]
    public void MatchCase()
    {
        AssertHighlighter("python",
"""
match command.split():
    case [action]:
        pass
    case ["go", direction]:
        go(direction)
    case Point(x=0, y=0):
        print("Origin")
    case {"key": value}:
        pass
    case -1 | 0:
        pass
    case _:
        print("Other")
""",
"""
<span class="hljs-keyword">match</span> command.split():
    <span class="hljs-keyword">case</span> [action]:
        <span class="hljs-keyword">pass</span>
    <span class="hljs-keyword">case</span> [<span class="hljs-string">&quot;go&quot;</span>, direction]:
        go(direction)
    <span class="hljs-keyword">case</span> Point(x=<span class="hljs-number">0</span>, y=<span class="hljs-number">0</span>):
        <span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;Origin&quot;</span>)
    <span class="hljs-keyword">case</span> {<span class="hljs-string">&quot;key&quot;</span>: value}:
        <span class="hljs-keyword">pass</span>
    <span class="hljs-keyword">case</span> -<span class="hljs-number">1</span> | <span class="hljs-number">0</span>:
        <span class="hljs-keyword">pass</span>
    <span class="hljs-keyword">case</span> _:
        <span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;Other&quot;</span>)
""");
    }

    [Fact]
    public void MatchAsName()
    {
        AssertHighlighter("python",
"""
import re
match = re.match(r"\d+", text)
if match:
    print(match.group(0))
m = pattern.match(s)
result = [match for match in matches]
case = 1
match (x):
    case 1: pass
""",
"""
<span class="hljs-keyword">import</span> re
match = re.match(<span class="hljs-string">r&quot;\d+&quot;</span>, text)
<span class="hljs-keyword">if</span> match:
    <span class="hljs-built_in">print</span>(match.group(<span class="hljs-number">0</span>))
m = pattern.match(s)
result = [match <span class="hljs-keyword">for</span> match <span class="hljs-keyword">in</span> matches]
case = <span class="hljs-number">1</span>
<span class="hljs-keyword">match</span> (x):
    <span class="hljs-keyword">case</span> <span class="hljs-number">1</span>: <span class="hljs-keyword">pass</span>
""");
    }

    [Fact]
    public void TypeHints()
    {
        AssertHighlighter("python",
"""
from typing import Optional, List, Dict, Any, Callable, Union

def greet(name: str, times: int = 1) -> str:
    return name * times

def process(items: List[Dict[str, Any]]) -> Optional[int]:
    ...

x: Union[int, None] = None
cb: Callable[[int], str]
v: typing.Optional[int] = None
""",
"""
<span class="hljs-keyword">from</span> typing <span class="hljs-keyword">import</span> <span class="hljs-type">Optional</span>, <span class="hljs-type">List</span>, <span class="hljs-type">Dict</span>, <span class="hljs-type">Any</span>, <span class="hljs-type">Callable</span>, <span class="hljs-type">Union</span>

<span class="hljs-keyword">def</span> <span class="hljs-title function_">greet</span>(<span class="hljs-params">name: <span class="hljs-built_in">str</span>, times: <span class="hljs-built_in">int</span> = <span class="hljs-number">1</span></span>) -&gt; <span class="hljs-built_in">str</span>:
    <span class="hljs-keyword">return</span> name * times

<span class="hljs-keyword">def</span> <span class="hljs-title function_">process</span>(<span class="hljs-params">items: <span class="hljs-type">List</span>[<span class="hljs-type">Dict</span>[<span class="hljs-built_in">str</span>, <span class="hljs-type">Any</span>]]</span>) -&gt; <span class="hljs-type">Optional</span>[<span class="hljs-built_in">int</span>]:
    ...

x: <span class="hljs-type">Union</span>[<span class="hljs-built_in">int</span>, <span class="hljs-literal">None</span>] = <span class="hljs-literal">None</span>
cb: <span class="hljs-type">Callable</span>[[<span class="hljs-built_in">int</span>], <span class="hljs-built_in">str</span>]
v: typing.<span class="hljs-type">Optional</span>[<span class="hljs-built_in">int</span>] = <span class="hljs-literal">None</span>
""");
    }

    [Fact]
    public void TypeAlias()
    {
        AssertHighlighter("python",
"""
type Point = tuple[float, float]
type ListOrSet[T] = list[T] | set[T]
def first[T](items: list[T]) -> T:
    return items[0]
class Box[T]:
    pass
""",
"""
<span class="hljs-built_in">type</span> Point = <span class="hljs-built_in">tuple</span>[<span class="hljs-built_in">float</span>, <span class="hljs-built_in">float</span>]
<span class="hljs-built_in">type</span> ListOrSet[T] = <span class="hljs-built_in">list</span>[T] | <span class="hljs-built_in">set</span>[T]
<span class="hljs-keyword">def</span> <span class="hljs-title function_">first</span>[T](<span class="hljs-params">items: <span class="hljs-built_in">list</span>[T]</span>) -&gt; T:
    <span class="hljs-keyword">return</span> items[<span class="hljs-number">0</span>]
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Box</span>[T]:
    <span class="hljs-keyword">pass</span>
""");
    }

    [Fact]
    public void GenericDef()
    {
        AssertHighlighter("python",
"""
def first[T](items: list[T]) -> T:
    pass
def pair[K: str, V: (int, float), *Ts, **P](k: K, v: dict[K, V] = {}) -> None: ...
def nested[T: list[dict[str, int]]]():
    pass
""",
"""
<span class="hljs-keyword">def</span> <span class="hljs-title function_">first</span>[T](<span class="hljs-params">items: <span class="hljs-built_in">list</span>[T]</span>) -&gt; T:
    <span class="hljs-keyword">pass</span>
<span class="hljs-keyword">def</span> <span class="hljs-title function_">pair</span>[K: <span class="hljs-built_in">str</span>, V: (<span class="hljs-built_in">int</span>, <span class="hljs-built_in">float</span>), *Ts, **P](<span class="hljs-params">k: K, v: <span class="hljs-built_in">dict</span>[K, V] = {}</span>) -&gt; <span class="hljs-literal">None</span>: ...
<span class="hljs-keyword">def</span> <span class="hljs-title function_">nested</span>[T: <span class="hljs-built_in">list</span>[<span class="hljs-built_in">dict</span>[<span class="hljs-built_in">str</span>, <span class="hljs-built_in">int</span>]]]():
    <span class="hljs-keyword">pass</span>
""");
    }

    [Fact]
    public void AsyncAwait()
    {
        AssertHighlighter("python",
"""
import asyncio

async def fetch(url):
    async with session.get(url) as resp:
        return await resp.json()

async def main():
    async for item in aiter():
        print(item)
    await asyncio.gather(*tasks)
""",
"""
<span class="hljs-keyword">import</span> asyncio

<span class="hljs-keyword">async</span> <span class="hljs-keyword">def</span> <span class="hljs-title function_">fetch</span>(<span class="hljs-params">url</span>):
    <span class="hljs-keyword">async</span> <span class="hljs-keyword">with</span> session.get(url) <span class="hljs-keyword">as</span> resp:
        <span class="hljs-keyword">return</span> <span class="hljs-keyword">await</span> resp.json()

<span class="hljs-keyword">async</span> <span class="hljs-keyword">def</span> <span class="hljs-title function_">main</span>():
    <span class="hljs-keyword">async</span> <span class="hljs-keyword">for</span> item <span class="hljs-keyword">in</span> aiter():
        <span class="hljs-built_in">print</span>(item)
    <span class="hljs-keyword">await</span> asyncio.gather(*tasks)
""");
    }

    [Fact]
    public void Walrus()
    {
        AssertHighlighter("python",
"""
if (n := len(a)) > 10:
    print(f"List is too long ({n} elements)")
while (chunk := f.read(1024)):
    process(chunk)
""",
"""
<span class="hljs-keyword">if</span> (n := <span class="hljs-built_in">len</span>(a)) &gt; <span class="hljs-number">10</span>:
    <span class="hljs-built_in">print</span>(<span class="hljs-string">f&quot;List is too long (<span class="hljs-subst">{n}</span> elements)&quot;</span>)
<span class="hljs-keyword">while</span> (chunk := f.read(<span class="hljs-number">1024</span>)):
    process(chunk)
""");
    }

    [Fact]
    public void Classes()
    {
        AssertHighlighter("python",
"""
class Animal:
    pass

class Dog(Animal):
    def __init__(self, name, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.name = name

    def __repr__(self):
        return f"Dog({self.name!r})"

class Multi(Base, Mixin):
    pass

class Meta(metaclass=ABCMeta):
    pass

class  Spaced ( Base ) :
    pass
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Animal</span>:
    <span class="hljs-keyword">pass</span>

<span class="hljs-keyword">class</span> <span class="hljs-title class_">Dog</span>(<span class="hljs-title class_ inherited__">Animal</span>):
    <span class="hljs-keyword">def</span> <span class="hljs-title function_">__init__</span>(<span class="hljs-params">self, name, *args, **kwargs</span>):
        <span class="hljs-built_in">super</span>().__init__(*args, **kwargs)
        <span class="hljs-variable language_">self</span>.name = name

    <span class="hljs-keyword">def</span> <span class="hljs-title function_">__repr__</span>(<span class="hljs-params">self</span>):
        <span class="hljs-keyword">return</span> <span class="hljs-string">f&quot;Dog(<span class="hljs-subst">{self.name!r}</span>)&quot;</span>

<span class="hljs-keyword">class</span> <span class="hljs-title class_">Multi</span>(Base, Mixin):
    <span class="hljs-keyword">pass</span>

<span class="hljs-keyword">class</span> <span class="hljs-title class_">Meta</span>(metaclass=ABCMeta):
    <span class="hljs-keyword">pass</span>

<span class="hljs-keyword">class</span>  <span class="hljs-title class_">Spaced</span> ( <span class="hljs-title class_ inherited__">Base</span> ) :
    <span class="hljs-keyword">pass</span>
""");
    }

    [Fact]
    public void Functions()
    {
        AssertHighlighter("python",
"""
def f():
    pass

def g(a, b=2, *args, c: int = 3, **kwargs) -> None:
    return None

def h( ):
    pass

def k(a=(1, 2), b=[3], c={"x": 4}):
    pass

def multiline(
    a,  # first
    b,
):
    pass

lambda x, y=1: x + y
""",
"""
<span class="hljs-keyword">def</span> <span class="hljs-title function_">f</span>():
    <span class="hljs-keyword">pass</span>

<span class="hljs-keyword">def</span> <span class="hljs-title function_">g</span>(<span class="hljs-params">a, b=<span class="hljs-number">2</span>, *args, c: <span class="hljs-built_in">int</span> = <span class="hljs-number">3</span>, **kwargs</span>) -&gt; <span class="hljs-literal">None</span>:
    <span class="hljs-keyword">return</span> <span class="hljs-literal">None</span>

<span class="hljs-keyword">def</span> <span class="hljs-title function_">h</span>( ):
    <span class="hljs-keyword">pass</span>

<span class="hljs-keyword">def</span> <span class="hljs-title function_">k</span>(<span class="hljs-params">a=(<span class="hljs-params"><span class="hljs-number">1</span>, <span class="hljs-number">2</span></span>), b=[<span class="hljs-number">3</span>], c={<span class="hljs-string">&quot;x&quot;</span>: <span class="hljs-number">4</span>}</span>):
    <span class="hljs-keyword">pass</span>

<span class="hljs-keyword">def</span> <span class="hljs-title function_">multiline</span>(<span class="hljs-params">
    a,  <span class="hljs-comment"># first</span>
    b,
</span>):
    <span class="hljs-keyword">pass</span>

<span class="hljs-keyword">lambda</span> x, y=<span class="hljs-number">1</span>: x + y
""");
    }

    [Fact]
    public void FunctionUnicode()
    {
        AssertHighlighter("python",
"""
def grüß(naïve):
    return naïve

class Ünïcode:
    pass
""",
"""
<span class="hljs-keyword">def</span> <span class="hljs-title function_">grüß</span>(<span class="hljs-params">naïve</span>):
    <span class="hljs-keyword">return</span> naïve

<span class="hljs-keyword">class</span> <span class="hljs-title class_">Ünïcode</span>:
    <span class="hljs-keyword">pass</span>
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("python",
"""
for i in range(10):
    if i % 2 == 0:
        continue
    elif i > 7:
        break
    else:
        pass
while True:
    try:
        x = 1 / 0
    except (ZeroDivisionError, ValueError) as e:
        raise RuntimeError("bad") from e
    finally:
        del x
""",
"""
<span class="hljs-keyword">for</span> i <span class="hljs-keyword">in</span> <span class="hljs-built_in">range</span>(<span class="hljs-number">10</span>):
    <span class="hljs-keyword">if</span> i % <span class="hljs-number">2</span> == <span class="hljs-number">0</span>:
        <span class="hljs-keyword">continue</span>
    <span class="hljs-keyword">elif</span> i &gt; <span class="hljs-number">7</span>:
        <span class="hljs-keyword">break</span>
    <span class="hljs-keyword">else</span>:
        <span class="hljs-keyword">pass</span>
<span class="hljs-keyword">while</span> <span class="hljs-literal">True</span>:
    <span class="hljs-keyword">try</span>:
        x = <span class="hljs-number">1</span> / <span class="hljs-number">0</span>
    <span class="hljs-keyword">except</span> (ZeroDivisionError, ValueError) <span class="hljs-keyword">as</span> e:
        <span class="hljs-keyword">raise</span> RuntimeError(<span class="hljs-string">&quot;bad&quot;</span>) <span class="hljs-keyword">from</span> e
    <span class="hljs-keyword">finally</span>:
        <span class="hljs-keyword">del</span> x
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("python",
"""
a = b and c or not d
x is None
y is not None
z in items
not_in = w not in items
r = a @ b
a //= 2; a **= 3; a >>= 1
x = y if cond else z
assert x, "message"
""",
"""
a = b <span class="hljs-keyword">and</span> c <span class="hljs-keyword">or</span> <span class="hljs-keyword">not</span> d
x <span class="hljs-keyword">is</span> <span class="hljs-literal">None</span>
y <span class="hljs-keyword">is</span> <span class="hljs-keyword">not</span> <span class="hljs-literal">None</span>
z <span class="hljs-keyword">in</span> items
not_in = w <span class="hljs-keyword">not</span> <span class="hljs-keyword">in</span> items
r = a @ b
a //= <span class="hljs-number">2</span>; a **= <span class="hljs-number">3</span>; a &gt;&gt;= <span class="hljs-number">1</span>
x = y <span class="hljs-keyword">if</span> cond <span class="hljs-keyword">else</span> z
<span class="hljs-keyword">assert</span> x, <span class="hljs-string">&quot;message&quot;</span>
""");
    }

    [Fact]
    public void Globals()
    {
        AssertHighlighter("python",
"""
def f():
    global counter
    nonlocal total
    counter += 1
    yield counter
    yield from other()
""",
"""
<span class="hljs-keyword">def</span> <span class="hljs-title function_">f</span>():
    <span class="hljs-keyword">global</span> counter
    <span class="hljs-keyword">nonlocal</span> total
    counter += <span class="hljs-number">1</span>
    <span class="hljs-keyword">yield</span> counter
    <span class="hljs-keyword">yield</span> <span class="hljs-keyword">from</span> other()
""");
    }

    [Fact]
    public void Builtins()
    {
        AssertHighlighter("python",
"""
print(len(items), max(a), min(b), sorted(c, key=abs))
isinstance(x, (int, float))
d = dict(zip(keys, values))
open("f.txt").read()
__import__("os")
print(__name__, __file__, __debug__)
x = NotImplemented
y = Ellipsis
""",
"""
<span class="hljs-built_in">print</span>(<span class="hljs-built_in">len</span>(items), <span class="hljs-built_in">max</span>(a), <span class="hljs-built_in">min</span>(b), <span class="hljs-built_in">sorted</span>(c, key=<span class="hljs-built_in">abs</span>))
<span class="hljs-built_in">isinstance</span>(x, (<span class="hljs-built_in">int</span>, <span class="hljs-built_in">float</span>))
d = <span class="hljs-built_in">dict</span>(<span class="hljs-built_in">zip</span>(keys, values))
<span class="hljs-built_in">open</span>(<span class="hljs-string">&quot;f.txt&quot;</span>).read()
<span class="hljs-built_in">__import__</span>(<span class="hljs-string">&quot;os&quot;</span>)
<span class="hljs-built_in">print</span>(__name__, __file__, <span class="hljs-literal">__debug__</span>)
x = <span class="hljs-literal">NotImplemented</span>
y = <span class="hljs-literal">Ellipsis</span>
""");
    }

    [Fact]
    public void Attributes()
    {
        AssertHighlighter("python",
"""
self.id = 1
obj.type = "x"
"".join(parts)
"{}".format(x)
np.max(arr)
df.min()
os.open(path)
x.print()
typing.List[int]
foo.None
self.list.append(x)
""",
"""
<span class="hljs-variable language_">self</span>.id = <span class="hljs-number">1</span>
obj.type = <span class="hljs-string">&quot;x&quot;</span>
<span class="hljs-string">&quot;&quot;</span>.join(parts)
<span class="hljs-string">&quot;{}&quot;</span>.format(x)
np.max(arr)
df.min()
os.open(path)
x.print()
typing.<span class="hljs-type">List</span>[<span class="hljs-built_in">int</span>]
foo.None
<span class="hljs-variable language_">self</span>.list.append(x)
""");
    }

    [Fact]
    public void WithStatement()
    {
        AssertHighlighter("python",
"""
with open("a") as f, open("b") as g:
    data = f.read()
""",
"""
<span class="hljs-keyword">with</span> <span class="hljs-built_in">open</span>(<span class="hljs-string">&quot;a&quot;</span>) <span class="hljs-keyword">as</span> f, <span class="hljs-built_in">open</span>(<span class="hljs-string">&quot;b&quot;</span>) <span class="hljs-keyword">as</span> g:
    data = f.read()
""");
    }

    [Fact]
    public void Repl()
    {
        AssertHighlighter("python",
"""
>>> x = 1
>>> def f():
...     return 2
...
>>> f()
2
""",
"""
<span class="hljs-meta">&gt;&gt;&gt; </span>x = <span class="hljs-number">1</span>
<span class="hljs-meta">&gt;&gt;&gt; </span><span class="hljs-keyword">def</span> <span class="hljs-title function_">f</span>():
<span class="hljs-meta">... </span>    <span class="hljs-keyword">return</span> <span class="hljs-number">2</span>
...
<span class="hljs-meta">&gt;&gt;&gt; </span>f()
<span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Illegal()
    {
        AssertHighlighter("python",
"""
x = a ? b : c
y = lambda: x => 1
z = </tag>
""",
"""
x = a ? b : c
y = <span class="hljs-keyword">lambda</span>: x =&gt; <span class="hljs-number">1</span>
z = &lt;/tag&gt;
""");
    }

    [Fact]
    public void Comprehensions()
    {
        AssertHighlighter("python",
"""
squares = [x**2 for x in range(10) if x % 2]
d = {k: v for k, v in pairs}
s = {x for x in items}
g = (x for x in items)
""",
"""
squares = [x**<span class="hljs-number">2</span> <span class="hljs-keyword">for</span> x <span class="hljs-keyword">in</span> <span class="hljs-built_in">range</span>(<span class="hljs-number">10</span>) <span class="hljs-keyword">if</span> x % <span class="hljs-number">2</span>]
d = {k: v <span class="hljs-keyword">for</span> k, v <span class="hljs-keyword">in</span> pairs}
s = {x <span class="hljs-keyword">for</span> x <span class="hljs-keyword">in</span> items}
g = (x <span class="hljs-keyword">for</span> x <span class="hljs-keyword">in</span> items)
""");
    }

    [Fact]
    public void Slicing()
    {
        AssertHighlighter("python",
"""
a[1:2]
a[::2]
a[:-1]
a[...]
b = a[1:2, ::3]
""",
"""
a[<span class="hljs-number">1</span>:<span class="hljs-number">2</span>]
a[::<span class="hljs-number">2</span>]
a[:-<span class="hljs-number">1</span>]
a[...]
b = a[<span class="hljs-number">1</span>:<span class="hljs-number">2</span>, ::<span class="hljs-number">3</span>]
""");
    }

    [Fact]
    public void StringConcat()
    {
        AssertHighlighter("python",
"""
x = ("abc"
     "def")
y = "a" "b"
z = 'a' 'b'
""",
"""
x = (<span class="hljs-string">&quot;abc&quot;</span>
     <span class="hljs-string">&quot;def&quot;</span>)
y = <span class="hljs-string">&quot;a&quot;</span> <span class="hljs-string">&quot;b&quot;</span>
z = <span class="hljs-string">&#x27;a&#x27;</span> <span class="hljs-string">&#x27;b&#x27;</span>
""");
    }

    [Fact]
    public void IfString()
    {
        AssertHighlighter("python",
"""
if"x" in y: pass
if f"{x}": pass
or"abc"
x = y or"z"
elif"a": pass
""",
"""
<span class="hljs-keyword">if</span><span class="hljs-string">&quot;x&quot;</span> <span class="hljs-keyword">in</span> y: <span class="hljs-keyword">pass</span>
<span class="hljs-keyword">if</span> <span class="hljs-string">f&quot;<span class="hljs-subst">{x}</span>&quot;</span>: <span class="hljs-keyword">pass</span>
<span class="hljs-keyword">or</span><span class="hljs-string">&quot;abc&quot;</span>
x = y <span class="hljs-keyword">or</span><span class="hljs-string">&quot;z&quot;</span>
eli<span class="hljs-string">f&quot;a&quot;</span>: <span class="hljs-keyword">pass</span>
""");
    }

    [Fact]
    public void SelfUsage()
    {
        AssertHighlighter("python",
"""
class A:
    def m(self, other):
        return self.x + other.x
    @classmethod
    def c(cls):
        return cls()
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span>:
    <span class="hljs-keyword">def</span> <span class="hljs-title function_">m</span>(<span class="hljs-params">self, other</span>):
        <span class="hljs-keyword">return</span> <span class="hljs-variable language_">self</span>.x + other.x
<span class="hljs-meta">    @classmethod</span>
    <span class="hljs-keyword">def</span> <span class="hljs-title function_">c</span>(<span class="hljs-params">cls</span>):
        <span class="hljs-keyword">return</span> cls()
""");
    }

    [Fact]
    public void ExceptionsGroup()
    {
        AssertHighlighter("python",
"""
try:
    pass
except* ValueError as eg:
    pass
""",
"""
<span class="hljs-keyword">try</span>:
    <span class="hljs-keyword">pass</span>
<span class="hljs-keyword">except</span>* ValueError <span class="hljs-keyword">as</span> eg:
    <span class="hljs-keyword">pass</span>
""");
    }

    [Fact]
    public void Dunder()
    {
        AssertHighlighter("python",
"""
class A:
    __slots__ = ("a",)
    def __eq__(self, other): return NotImplemented
    __all__ = ["x"]
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span>:
    __slots__ = (<span class="hljs-string">&quot;a&quot;</span>,)
    <span class="hljs-keyword">def</span> <span class="hljs-title function_">__eq__</span>(<span class="hljs-params">self, other</span>): <span class="hljs-keyword">return</span> <span class="hljs-literal">NotImplemented</span>
    __all__ = [<span class="hljs-string">&quot;x&quot;</span>]
""");
    }

    [Fact]
    public void BackslashContinuation()
    {
        AssertHighlighter("python",
"""
total = a + \
    b
x = "long \
string"
""",
"""
total = a + \
    b
x = <span class="hljs-string">&quot;long \
string&quot;</span>
""");
    }

    [Fact]
    public void Unterminated()
    {
        AssertHighlighter("python",
"""
x = "abc
y = 1
z = 'def
""",
"""
x = <span class="hljs-string">&quot;abc
y = 1
z = &#x27;def</span>
""");
    }

    [Fact]
    public void KeywordLikeIdentifiers()
    {
        AssertHighlighter("python",
"""
import_ = 1
class_ = 2
printed = 3
is_valid = True
if_x = 4
for_ = 5
""",
"""
import_ = <span class="hljs-number">1</span>
class_ = <span class="hljs-number">2</span>
printed = <span class="hljs-number">3</span>
is_valid = <span class="hljs-literal">True</span>
if_x = <span class="hljs-number">4</span>
for_ = <span class="hljs-number">5</span>
""");
    }

    [Fact]
    public void GlobalCode()
    {
        AssertHighlighter("python",
""""
#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Module docstring."""

__version__ = "1.0.0"
"""",
"""
<span class="hljs-comment">#!/usr/bin/env python3</span>
<span class="hljs-comment"># -*- coding: utf-8 -*-</span>
<span class="hljs-string">&quot;&quot;&quot;Module docstring.&quot;&quot;&quot;</span>

__version__ = <span class="hljs-string">&quot;1.0.0&quot;</span>
""");
    }

    [Fact]
    public void BytesAndEscapes()
    {
        AssertHighlighter("python",
"""
b'\x00\xff'
'\N{DASH}'
'\u1234'
"\t\\"
""",
"""
<span class="hljs-string">b&#x27;\x00\xff&#x27;</span>
<span class="hljs-string">&#x27;\N{DASH}&#x27;</span>
<span class="hljs-string">&#x27;\u1234&#x27;</span>
<span class="hljs-string">&quot;\t\\&quot;</span>
""");
    }

    [Fact]
    public void NumbersInParams()
    {
        AssertHighlighter("python",
"""
def f(a=1, b=0x10, c=1.5e3, d="s", e=None, f=True):
    pass
""",
"""
<span class="hljs-keyword">def</span> <span class="hljs-title function_">f</span>(<span class="hljs-params">a=<span class="hljs-number">1</span>, b=<span class="hljs-number">0x10</span>, c=<span class="hljs-number">1.5e3</span>, d=<span class="hljs-string">&quot;s&quot;</span>, e=<span class="hljs-literal">None</span>, f=<span class="hljs-literal">True</span></span>):
    <span class="hljs-keyword">pass</span>
""");
    }

    [Fact]
    public void NestedParams()
    {
        AssertHighlighter("python",
"""
def f(a=(1, (2, 3)), b=()):
    pass
foo(1, (2))
""",
"""
<span class="hljs-keyword">def</span> <span class="hljs-title function_">f</span>(<span class="hljs-params">a=(<span class="hljs-params"><span class="hljs-number">1</span>, (<span class="hljs-params"><span class="hljs-number">2</span>, <span class="hljs-number">3</span></span>)</span>), b=(<span class="hljs-params"></span>)</span>):
    <span class="hljs-keyword">pass</span>
foo(<span class="hljs-number">1</span>, (<span class="hljs-number">2</span>))
""");
    }

    [Fact]
    public void LongProgram()
    {
        AssertHighlighter("python",
"""
import json
from dataclasses import dataclass, field


@dataclass
class Config:
    name: str
    values: list[int] = field(default_factory=list)

    def to_json(self) -> str:
        return json.dumps({"name": self.name, "values": self.values}, indent=2)


def load(path: str) -> Config:
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    return Config(**data)


if __name__ == "__main__":
    cfg = load("config.json")
    print(f"Loaded {cfg.name!r} with {len(cfg.values)} values")
""",
"""
<span class="hljs-keyword">import</span> json
<span class="hljs-keyword">from</span> dataclasses <span class="hljs-keyword">import</span> dataclass, field


<span class="hljs-meta">@dataclass</span>
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Config</span>:
    name: <span class="hljs-built_in">str</span>
    values: <span class="hljs-built_in">list</span>[<span class="hljs-built_in">int</span>] = field(default_factory=<span class="hljs-built_in">list</span>)

    <span class="hljs-keyword">def</span> <span class="hljs-title function_">to_json</span>(<span class="hljs-params">self</span>) -&gt; <span class="hljs-built_in">str</span>:
        <span class="hljs-keyword">return</span> json.dumps({<span class="hljs-string">&quot;name&quot;</span>: <span class="hljs-variable language_">self</span>.name, <span class="hljs-string">&quot;values&quot;</span>: <span class="hljs-variable language_">self</span>.values}, indent=<span class="hljs-number">2</span>)


<span class="hljs-keyword">def</span> <span class="hljs-title function_">load</span>(<span class="hljs-params">path: <span class="hljs-built_in">str</span></span>) -&gt; Config:
    <span class="hljs-keyword">with</span> <span class="hljs-built_in">open</span>(path, encoding=<span class="hljs-string">&quot;utf-8&quot;</span>) <span class="hljs-keyword">as</span> f:
        data = json.load(f)
    <span class="hljs-keyword">return</span> Config(**data)


<span class="hljs-keyword">if</span> __name__ == <span class="hljs-string">&quot;__main__&quot;</span>:
    cfg = load(<span class="hljs-string">&quot;config.json&quot;</span>)
    <span class="hljs-built_in">print</span>(<span class="hljs-string">f&quot;Loaded <span class="hljs-subst">{cfg.name!r}</span> with <span class="hljs-subst">{<span class="hljs-built_in">len</span>(cfg.values)}</span> values&quot;</span>)
""");
    }
}
