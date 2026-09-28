namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class LuaHighlighterTests
{
    [Fact]
    public void Hello()
    {
        AssertHighlighter("lua",
"""
print("Hello, world!")
""",
"""
<span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;Hello, world!&quot;</span>)
""");
    }

    [Fact]
    public void Locals()
    {
        AssertHighlighter("lua",
"""
local x = 10
local name, age = "Bob", 42
local t = {}
local flag = true
local nothing = nil
""",
"""
<span class="hljs-keyword">local</span> x = <span class="hljs-number">10</span>
<span class="hljs-keyword">local</span> name, age = <span class="hljs-string">&quot;Bob&quot;</span>, <span class="hljs-number">42</span>
<span class="hljs-keyword">local</span> t = {}
<span class="hljs-keyword">local</span> flag = <span class="hljs-literal">true</span>
<span class="hljs-keyword">local</span> nothing = <span class="hljs-literal">nil</span>
""");
    }

    [Fact]
    public void Functions()
    {
        AssertHighlighter("lua",
"""
function greet(name)
  return "Hello, " .. name
end

local function add(a, b)
  return a + b
end

local square = function(x) return x * x end
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">greet</span><span class="hljs-params">(name)</span></span>
  <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;Hello, &quot;</span> .. name
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">local</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">add</span><span class="hljs-params">(a, b)</span></span>
  <span class="hljs-keyword">return</span> a + b
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">local</span> square = <span class="hljs-function"><span class="hljs-keyword">function</span><span class="hljs-params">(x)</span></span> <span class="hljs-keyword">return</span> x * x <span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Methods()
    {
        AssertHighlighter("lua",
"""
function Account:deposit(v)
  self.balance = self.balance + v
end

function M.util.helper(a, ...)
  local args = {...}
  return #args
end

obj:method(1, 2)
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Account:deposit</span><span class="hljs-params">(v)</span></span>
  <span class="hljs-built_in">self</span>.balance = <span class="hljs-built_in">self</span>.balance + v
<span class="hljs-keyword">end</span>

<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">M.util.helper</span><span class="hljs-params">(a, ...)</span></span>
  <span class="hljs-keyword">local</span> args = {...}
  <span class="hljs-keyword">return</span> #args
<span class="hljs-keyword">end</span>

obj:method(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>)
""");
    }

    [Fact]
    public void Tables()
    {
        AssertHighlighter("lua",
"""
local point = { x = 1, y = 2 }
local list = { "a", "b", "c" }
local mixed = { [1] = "one", ["key"] = "value", nested = { a = true } }
print(point.x, list[1], mixed["key"])
""",
"""
<span class="hljs-keyword">local</span> point = { x = <span class="hljs-number">1</span>, y = <span class="hljs-number">2</span> }
<span class="hljs-keyword">local</span> list = { <span class="hljs-string">&quot;a&quot;</span>, <span class="hljs-string">&quot;b&quot;</span>, <span class="hljs-string">&quot;c&quot;</span> }
<span class="hljs-keyword">local</span> mixed = { [<span class="hljs-number">1</span>] = <span class="hljs-string">&quot;one&quot;</span>, [<span class="hljs-string">&quot;key&quot;</span>] = <span class="hljs-string">&quot;value&quot;</span>, nested = { a = <span class="hljs-literal">true</span> } }
<span class="hljs-built_in">print</span>(point.x, list[<span class="hljs-number">1</span>], mixed[<span class="hljs-string">&quot;key&quot;</span>])
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("lua",
"""
if x > 0 then
  print("positive")
elseif x == 0 then
  print("zero")
else
  print("negative")
end

while i < 10 do
  i = i + 1
end

repeat
  i = i - 1
until i <= 0

for i = 1, 10, 2 do
  if i == 5 then break end
end

for k, v in pairs(t) do
  print(k, v)
end

for i, v in ipairs(list) do goto continue end
::continue::
""",
"""
<span class="hljs-keyword">if</span> x &gt; <span class="hljs-number">0</span> <span class="hljs-keyword">then</span>
  <span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;positive&quot;</span>)
<span class="hljs-keyword">elseif</span> x == <span class="hljs-number">0</span> <span class="hljs-keyword">then</span>
  <span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;zero&quot;</span>)
<span class="hljs-keyword">else</span>
  <span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;negative&quot;</span>)
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">while</span> i &lt; <span class="hljs-number">10</span> <span class="hljs-keyword">do</span>
  i = i + <span class="hljs-number">1</span>
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">repeat</span>
  i = i - <span class="hljs-number">1</span>
<span class="hljs-keyword">until</span> i &lt;= <span class="hljs-number">0</span>

<span class="hljs-keyword">for</span> i = <span class="hljs-number">1</span>, <span class="hljs-number">10</span>, <span class="hljs-number">2</span> <span class="hljs-keyword">do</span>
  <span class="hljs-keyword">if</span> i == <span class="hljs-number">5</span> <span class="hljs-keyword">then</span> <span class="hljs-keyword">break</span> <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">for</span> k, v <span class="hljs-keyword">in</span> <span class="hljs-built_in">pairs</span>(t) <span class="hljs-keyword">do</span>
  <span class="hljs-built_in">print</span>(k, v)
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">for</span> i, v <span class="hljs-keyword">in</span> <span class="hljs-built_in">ipairs</span>(list) <span class="hljs-keyword">do</span> <span class="hljs-keyword">goto</span> continue <span class="hljs-keyword">end</span>
::continue::
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("lua",
"""
-- single line comment
local x = 1 -- trailing comment
-- TODO: something
--[[ block
comment ]]
--[==[ level two
with ]] inside
]==]
print("after")
""",
"""
<span class="hljs-comment">-- single line comment</span>
<span class="hljs-keyword">local</span> x = <span class="hljs-number">1</span> <span class="hljs-comment">-- trailing comment</span>
<span class="hljs-comment">-- <span class="hljs-doctag">TODO:</span> something</span>
<span class="hljs-comment">--[[ block
comment ]]</span>
<span class="hljs-comment">--[==[ level two
with ]] inside
]==]</span>
<span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;after&quot;</span>)
""");
    }

    [Fact]
    public void LongStrings()
    {
        AssertHighlighter("lua",
"""
local s = [[
multi-line
string ]]
local t = [==[
contains ]] and ]=] but ends here ]==]
local u = [=[one]=]
print(s, t, u)
""",
"""
<span class="hljs-keyword">local</span> s = <span class="hljs-string">[[
multi-line
string ]]</span>
<span class="hljs-keyword">local</span> t = <span class="hljs-string">[==[
contains ]] and ]=] but ends here ]==]</span>
<span class="hljs-keyword">local</span> u = <span class="hljs-string">[=[one]=]</span>
<span class="hljs-built_in">print</span>(s, t, u)
""");
    }

    [Fact]
    public void NestedLongBracket()
    {
        AssertHighlighter("lua",
"""
local s = [[ a [[ b ]] c ]]
--[[ x [[ y ]] z ]]
print(1)
""",
"""
<span class="hljs-keyword">local</span> s = <span class="hljs-string">[[ a [[ b ]]</span> c ]]
<span class="hljs-comment">--[[ x [[ y ]]</span> z ]]
<span class="hljs-built_in">print</span>(<span class="hljs-number">1</span>)
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("lua",
"""
local a = 'single \'quoted\''
local b = "double \"quoted\" \n\t"
local c = "escape \65 \x41 \z"
local d = "unterminated
local e = 1
""",
"""
<span class="hljs-keyword">local</span> a = <span class="hljs-string">&#x27;single \&#x27;quoted\&#x27;&#x27;</span>
<span class="hljs-keyword">local</span> b = <span class="hljs-string">&quot;double \&quot;quoted\&quot; \n\t&quot;</span>
<span class="hljs-keyword">local</span> c = <span class="hljs-string">&quot;escape \65 \x41 \z&quot;</span>
<span class="hljs-keyword">local</span> d = <span class="hljs-string">&quot;unterminated
local e = 1</span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("lua",
"""
local a = 42
local b = 3.14
local c = 1e10
local d = 2.5E-3
local e = 0xFF
local f = .5
local g = -7
local h = 0x1p4
local i = 10 - 3
""",
"""
<span class="hljs-keyword">local</span> a = <span class="hljs-number">42</span>
<span class="hljs-keyword">local</span> b = <span class="hljs-number">3.14</span>
<span class="hljs-keyword">local</span> c = <span class="hljs-number">1e10</span>
<span class="hljs-keyword">local</span> d = <span class="hljs-number">2.5E-3</span>
<span class="hljs-keyword">local</span> e = <span class="hljs-number">0xFF</span>
<span class="hljs-keyword">local</span> f = <span class="hljs-number">.5</span>
<span class="hljs-keyword">local</span> g = <span class="hljs-number">-7</span>
<span class="hljs-keyword">local</span> h = <span class="hljs-number">0x1</span>p4
<span class="hljs-keyword">local</span> i = <span class="hljs-number">10</span> - <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("lua",
"""
local a = x + y - z * w / v % u ^ 2
local b = s .. t
local c = #list
local d = not a and b or c
local e = a ~= b
local f = a // b
local g = a & b | c ~ d << 1 >> 2
""",
"""
<span class="hljs-keyword">local</span> a = x + y - z * w / v % u ^ <span class="hljs-number">2</span>
<span class="hljs-keyword">local</span> b = s .. t
<span class="hljs-keyword">local</span> c = #list
<span class="hljs-keyword">local</span> d = <span class="hljs-keyword">not</span> a <span class="hljs-keyword">and</span> b <span class="hljs-keyword">or</span> c
<span class="hljs-keyword">local</span> e = a ~= b
<span class="hljs-keyword">local</span> f = a // b
<span class="hljs-keyword">local</span> g = a &amp; b | c ~ d &lt;&lt; <span class="hljs-number">1</span> &gt;&gt; <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Builtins()
    {
        AssertHighlighter("lua",
"""
print(type(x), tostring(1), tonumber("2"))
local m = setmetatable({}, { __index = base })
local ok, err = pcall(fn, arg)
assert(ok, err)
error("boom")
local n = select("#", ...)
""",
"""
<span class="hljs-built_in">print</span>(<span class="hljs-built_in">type</span>(x), <span class="hljs-built_in">tostring</span>(<span class="hljs-number">1</span>), <span class="hljs-built_in">tonumber</span>(<span class="hljs-string">&quot;2&quot;</span>))
<span class="hljs-keyword">local</span> m = <span class="hljs-built_in">setmetatable</span>({}, { <span class="hljs-built_in">__index</span> = base })
<span class="hljs-keyword">local</span> ok, err = <span class="hljs-built_in">pcall</span>(fn, <span class="hljs-built_in">arg</span>)
<span class="hljs-built_in">assert</span>(ok, err)
<span class="hljs-built_in">error</span>(<span class="hljs-string">&quot;boom&quot;</span>)
<span class="hljs-keyword">local</span> n = <span class="hljs-built_in">select</span>(<span class="hljs-string">&quot;#&quot;</span>, ...)
""");
    }

    [Fact]
    public void Libraries()
    {
        AssertHighlighter("lua",
"""
local s = string.format("%d", 1)
local u = string.upper("x")
table.insert(t, 1)
table.sort(t, function(a, b) return a < b end)
local r = math.floor(math.random() * 10)
local f = io.open("file.txt", "r")
local now = os.time()
local co = coroutine.create(function() coroutine.yield(1) end)
""",
"""
<span class="hljs-keyword">local</span> s = <span class="hljs-built_in">string</span>.<span class="hljs-built_in">format</span>(<span class="hljs-string">&quot;%d&quot;</span>, <span class="hljs-number">1</span>)
<span class="hljs-keyword">local</span> u = <span class="hljs-built_in">string</span>.<span class="hljs-built_in">upper</span>(<span class="hljs-string">&quot;x&quot;</span>)
<span class="hljs-built_in">table</span>.<span class="hljs-built_in">insert</span>(t, <span class="hljs-number">1</span>)
<span class="hljs-built_in">table</span>.<span class="hljs-built_in">sort</span>(t, <span class="hljs-function"><span class="hljs-keyword">function</span><span class="hljs-params">(a, b)</span></span> <span class="hljs-keyword">return</span> a &lt; b <span class="hljs-keyword">end</span>)
<span class="hljs-keyword">local</span> r = <span class="hljs-built_in">math</span>.<span class="hljs-built_in">floor</span>(<span class="hljs-built_in">math</span>.<span class="hljs-built_in">random</span>() * <span class="hljs-number">10</span>)
<span class="hljs-keyword">local</span> f = <span class="hljs-built_in">io</span>.<span class="hljs-built_in">open</span>(<span class="hljs-string">&quot;file.txt&quot;</span>, <span class="hljs-string">&quot;r&quot;</span>)
<span class="hljs-keyword">local</span> now = <span class="hljs-built_in">os</span>.<span class="hljs-built_in">time</span>()
<span class="hljs-keyword">local</span> co = <span class="hljs-built_in">coroutine</span>.<span class="hljs-built_in">create</span>(<span class="hljs-function"><span class="hljs-keyword">function</span><span class="hljs-params">()</span></span> <span class="hljs-built_in">coroutine</span>.<span class="hljs-built_in">yield</span>(<span class="hljs-number">1</span>) <span class="hljs-keyword">end</span>)
""");
    }

    [Fact]
    public void StringMethods()
    {
        AssertHighlighter("lua",
"""
local s = ("hello"):upper()
local n = s:len()
for word in s:gmatch("%a+") do print(word) end
local r = s:gsub("l", "L")
""",
"""
<span class="hljs-keyword">local</span> s = (<span class="hljs-string">&quot;hello&quot;</span>):<span class="hljs-built_in">upper</span>()
<span class="hljs-keyword">local</span> n = s:<span class="hljs-built_in">len</span>()
<span class="hljs-keyword">for</span> word <span class="hljs-keyword">in</span> s:<span class="hljs-built_in">gmatch</span>(<span class="hljs-string">&quot;%a+&quot;</span>) <span class="hljs-keyword">do</span> <span class="hljs-built_in">print</span>(word) <span class="hljs-keyword">end</span>
<span class="hljs-keyword">local</span> r = s:<span class="hljs-built_in">gsub</span>(<span class="hljs-string">&quot;l&quot;</span>, <span class="hljs-string">&quot;L&quot;</span>)
""");
    }

    [Fact]
    public void Metatables()
    {
        AssertHighlighter("lua",
"""
local Vector = {}
Vector.__index = Vector

function Vector.new(x, y)
  local self = setmetatable({}, Vector)
  self.x = x
  self.y = y
  return self
end

function Vector.__add(a, b)
  return Vector.new(a.x + b.x, a.y + b.y)
end

function Vector:__tostring()
  return "(" .. self.x .. ", " .. self.y .. ")"
end
""",
"""
<span class="hljs-keyword">local</span> Vector = {}
Vector.<span class="hljs-built_in">__index</span> = Vector

<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Vector.new</span><span class="hljs-params">(x, y)</span></span>
  <span class="hljs-keyword">local</span> <span class="hljs-built_in">self</span> = <span class="hljs-built_in">setmetatable</span>({}, Vector)
  <span class="hljs-built_in">self</span>.x = x
  <span class="hljs-built_in">self</span>.y = y
  <span class="hljs-keyword">return</span> <span class="hljs-built_in">self</span>
<span class="hljs-keyword">end</span>

<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Vector.__add</span><span class="hljs-params">(a, b)</span></span>
  <span class="hljs-keyword">return</span> Vector.new(a.x + b.x, a.y + b.y)
<span class="hljs-keyword">end</span>

<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Vector:__tostring</span><span class="hljs-params">()</span></span>
  <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;(&quot;</span> .. <span class="hljs-built_in">self</span>.x .. <span class="hljs-string">&quot;, &quot;</span> .. <span class="hljs-built_in">self</span>.y .. <span class="hljs-string">&quot;)&quot;</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Modules()
    {
        AssertHighlighter("lua",
"""
local M = {}

local json = require("json")
local util = require "util"

function M.run()
  return json.encode({})
end

return M
""",
"""
<span class="hljs-keyword">local</span> M = {}

<span class="hljs-keyword">local</span> json = <span class="hljs-built_in">require</span>(<span class="hljs-string">&quot;json&quot;</span>)
<span class="hljs-keyword">local</span> util = <span class="hljs-built_in">require</span> <span class="hljs-string">&quot;util&quot;</span>

<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">M.run</span><span class="hljs-params">()</span></span>
  <span class="hljs-keyword">return</span> json.encode({})
<span class="hljs-keyword">end</span>

<span class="hljs-keyword">return</span> M
""");
    }

    [Fact]
    public void Varargs()
    {
        AssertHighlighter("lua",
"""
function sum(...)
  local total = 0
  for _, v in ipairs({...}) do
    total = total + v
  end
  return total
end
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">sum</span><span class="hljs-params">(...)</span></span>
  <span class="hljs-keyword">local</span> total = <span class="hljs-number">0</span>
  <span class="hljs-keyword">for</span> _, v <span class="hljs-keyword">in</span> <span class="hljs-built_in">ipairs</span>({...}) <span class="hljs-keyword">do</span>
    total = total + v
  <span class="hljs-keyword">end</span>
  <span class="hljs-keyword">return</span> total
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Closures()
    {
        AssertHighlighter("lua",
"""
local function counter()
  local count = 0
  return function()
    count = count + 1
    return count
  end
end
""",
"""
<span class="hljs-keyword">local</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">counter</span><span class="hljs-params">()</span></span>
  <span class="hljs-keyword">local</span> count = <span class="hljs-number">0</span>
  <span class="hljs-keyword">return</span> <span class="hljs-function"><span class="hljs-keyword">function</span><span class="hljs-params">()</span></span>
    count = count + <span class="hljs-number">1</span>
    <span class="hljs-keyword">return</span> count
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void FunctionCommentParams()
    {
        AssertHighlighter("lua",
"""
function f(a, --[[ inline ]] b) -- trailing
  return a
end
function g(x -- the x
  , y)
end
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">f</span><span class="hljs-params">(a, <span class="hljs-comment">--[[ inline ]]</span> b)</span></span> <span class="hljs-comment">-- trailing</span>
  <span class="hljs-keyword">return</span> a
<span class="hljs-keyword">end</span>
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">g</span><span class="hljs-params">(x <span class="hljs-comment">-- the x</span>
  , y)</span></span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Attribs()
    {
        AssertHighlighter("lua",
"""
local x <const> = 5
local f <close> = io.open("f")
""",
"""
<span class="hljs-keyword">local</span> x &lt;const&gt; = <span class="hljs-number">5</span>
<span class="hljs-keyword">local</span> f &lt;<span class="hljs-built_in">close</span>&gt; = <span class="hljs-built_in">io</span>.<span class="hljs-built_in">open</span>(<span class="hljs-string">&quot;f&quot;</span>)
""");
    }

    [Fact]
    public void GotoLabels()
    {
        AssertHighlighter("lua",
"""
for i = 1, 3 do
  for j = 1, 3 do
    if j == 2 then goto next end
  end
  ::next::
end
""",
"""
<span class="hljs-keyword">for</span> i = <span class="hljs-number">1</span>, <span class="hljs-number">3</span> <span class="hljs-keyword">do</span>
  <span class="hljs-keyword">for</span> j = <span class="hljs-number">1</span>, <span class="hljs-number">3</span> <span class="hljs-keyword">do</span>
    <span class="hljs-keyword">if</span> j == <span class="hljs-number">2</span> <span class="hljs-keyword">then</span> <span class="hljs-keyword">goto</span> <span class="hljs-built_in">next</span> <span class="hljs-keyword">end</span>
  <span class="hljs-keyword">end</span>
  ::<span class="hljs-built_in">next</span>::
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void IntegerDivision()
    {
        AssertHighlighter("lua",
"""
local q = 7 // 2
local r = 7 % 2
""",
"""
<span class="hljs-keyword">local</span> q = <span class="hljs-number">7</span> // <span class="hljs-number">2</span>
<span class="hljs-keyword">local</span> r = <span class="hljs-number">7</span> % <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void MultipleAssignment()
    {
        AssertHighlighter("lua",
"""
local a, b, c = 1, 2
a, b = b, a
""",
"""
<span class="hljs-keyword">local</span> a, b, c = <span class="hljs-number">1</span>, <span class="hljs-number">2</span>
a, b = b, a
""");
    }

    [Fact]
    public void OopClass()
    {
        AssertHighlighter("lua",
"""
local Animal = {}
Animal.__index = Animal

function Animal.new(name)
  return setmetatable({ name = name }, Animal)
end

function Animal:speak()
  print(self.name .. " makes a sound")
end
""",
"""
<span class="hljs-keyword">local</span> Animal = {}
Animal.<span class="hljs-built_in">__index</span> = Animal

<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Animal.new</span><span class="hljs-params">(name)</span></span>
  <span class="hljs-keyword">return</span> <span class="hljs-built_in">setmetatable</span>({ name = name }, Animal)
<span class="hljs-keyword">end</span>

<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Animal:speak</span><span class="hljs-params">()</span></span>
  <span class="hljs-built_in">print</span>(<span class="hljs-built_in">self</span>.name .. <span class="hljs-string">&quot; makes a sound&quot;</span>)
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Shebang()
    {
        AssertHighlighter("lua",
"""
#!/usr/bin/env lua
print(arg[0])
""",
"""
#!/usr/bin/env lua
<span class="hljs-built_in">print</span>(<span class="hljs-built_in">arg</span>[<span class="hljs-number">0</span>])
""");
    }

    [Fact]
    public void Love2d()
    {
        AssertHighlighter("lua",
"""
function love.load()
  player = { x = 0, y = 0, speed = 200 }
end

function love.update(dt)
  if love.keyboard.isDown("right") then
    player.x = player.x + player.speed * dt
  end
end
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">love.load</span><span class="hljs-params">()</span></span>
  player = { x = <span class="hljs-number">0</span>, y = <span class="hljs-number">0</span>, speed = <span class="hljs-number">200</span> }
<span class="hljs-keyword">end</span>

<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">love.update</span><span class="hljs-params">(dt)</span></span>
  <span class="hljs-keyword">if</span> love.keyboard.isDown(<span class="hljs-string">&quot;right&quot;</span>) <span class="hljs-keyword">then</span>
    player.x = player.x + player.speed * dt
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("lua",
"""
local s = "héllo"
local 名前 = 1
""",
"""
<span class="hljs-keyword">local</span> s = <span class="hljs-string">&quot;héllo&quot;</span>
<span class="hljs-keyword">local</span> 名前 = <span class="hljs-number">1</span>
""");
    }
}
