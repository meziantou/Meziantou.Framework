namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class ThriftHighlighterTests
{
    [Fact]
    public void Tutorial()
    {
        AssertHighlighter("thrift",
"""
/**
 * Thrift tutorial
 */
include "shared.thrift"

namespace java tutorial
namespace py tutorial

typedef i32 MyInteger

const i32 INT32CONSTANT = 9853
const map<string,string> MAPCONSTANT = {'hello':'world', 'goodnight':'moon'}

enum Operation {
  ADD = 1,
  SUBTRACT = 2,
  MULTIPLY = 3,
  DIVIDE = 4
}

struct Work {
  1: i32 num1 = 0,
  2: i32 num2,
  3: Operation op,
  4: optional string comment,
}

exception InvalidOperation {
  1: i32 whatOp,
  2: string why
}

service Calculator extends shared.SharedService {
   void ping(),
   i32 add(1:i32 num1, 2:i32 num2),
   i32 calculate(1:i32 logid, 2:Work w) throws (1:InvalidOperation ouch),
   oneway void zip()
}
""",
"""
<span class="hljs-comment">/**
 * Thrift tutorial
 */</span>
include <span class="hljs-string">&quot;shared.thrift&quot;</span>

<span class="hljs-keyword">namespace</span> java tutorial
<span class="hljs-keyword">namespace</span> py tutorial

<span class="hljs-keyword">typedef</span> <span class="hljs-type">i32</span> MyInteger

<span class="hljs-keyword">const</span> <span class="hljs-type">i32</span> INT32CONSTANT = <span class="hljs-number">9853</span>
<span class="hljs-keyword">const</span> <span class="hljs-type">map</span>&lt;<span class="hljs-type">string</span>,<span class="hljs-type">string</span>&gt; MAPCONSTANT = {&#x27;hello&#x27;:&#x27;world&#x27;, &#x27;goodnight&#x27;:&#x27;moon&#x27;}

<span class="hljs-class"><span class="hljs-keyword">enum</span> <span class="hljs-title">Operation</span> </span>{
  ADD = <span class="hljs-number">1</span>,
  SUBTRACT = <span class="hljs-number">2</span>,
  MULTIPLY = <span class="hljs-number">3</span>,
  DIVIDE = <span class="hljs-number">4</span>
}

<span class="hljs-class"><span class="hljs-keyword">struct</span> <span class="hljs-title">Work</span> </span>{
  <span class="hljs-number">1</span>: <span class="hljs-type">i32</span> num1 = <span class="hljs-number">0</span>,
  <span class="hljs-number">2</span>: <span class="hljs-type">i32</span> num2,
  <span class="hljs-number">3</span>: Operation op,
  <span class="hljs-number">4</span>: <span class="hljs-keyword">optional</span> <span class="hljs-type">string</span> comment,
}

<span class="hljs-class"><span class="hljs-keyword">exception</span> <span class="hljs-title">InvalidOperation</span> </span>{
  <span class="hljs-number">1</span>: <span class="hljs-type">i32</span> whatOp,
  <span class="hljs-number">2</span>: <span class="hljs-type">string</span> why
}

<span class="hljs-class"><span class="hljs-keyword">service</span> <span class="hljs-title">Calculator</span> extends shared.SharedService </span>{
   <span class="hljs-keyword">void</span> ping(),
   <span class="hljs-type">i32</span> add(<span class="hljs-number">1</span>:<span class="hljs-type">i32</span> num1, <span class="hljs-number">2</span>:<span class="hljs-type">i32</span> num2),
   <span class="hljs-type">i32</span> calculate(<span class="hljs-number">1</span>:<span class="hljs-type">i32</span> logid, <span class="hljs-number">2</span>:Work w) throws (<span class="hljs-number">1</span>:InvalidOperation ouch),
   <span class="hljs-keyword">oneway</span> <span class="hljs-keyword">void</span> zip()
}
""");
    }

    [Fact]
    public void ContainerTypes()
    {
        AssertHighlighter("thrift",
"""
struct Containers {
  1: required list<string> names;
  2: set<i64> ids;
  3: map<string, list<map<i32, binary>>> nested;
  4: bool enabled = true;
  5: double ratio = 1.5; // comment
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">struct</span> <span class="hljs-title">Containers</span> </span>{
  <span class="hljs-number">1</span>: <span class="hljs-keyword">required</span> <span class="hljs-type">list</span>&lt;<span class="hljs-type">string</span>&gt; names;
  <span class="hljs-number">2</span>: <span class="hljs-type">set</span>&lt;<span class="hljs-type">i64</span>&gt; ids;
  <span class="hljs-number">3</span>: <span class="hljs-type">map</span>&lt;<span class="hljs-type">string</span>, <span class="hljs-type">list</span>&lt;<span class="hljs-type">map</span>&lt;<span class="hljs-type">i32</span>, <span class="hljs-type">binary</span>&gt;&gt;&gt; nested;
  <span class="hljs-number">4</span>: <span class="hljs-type">bool</span> enabled = <span class="hljs-literal">true</span>;
  <span class="hljs-number">5</span>: <span class="hljs-type">double</span> ratio = <span class="hljs-number">1.5</span>; <span class="hljs-comment">// comment</span>
}
""");
    }

    [Fact]
    public void UnterminatedStringAndComment()
    {
        AssertHighlighter("thrift",
"""
struct Foo
{
  1: string s = "abc
}
/* unterminated comment
""",
"""
<span class="hljs-class"><span class="hljs-keyword">struct</span> <span class="hljs-title">Foo</span>
</span>{
  <span class="hljs-number">1</span>: <span class="hljs-type">string</span> s = <span class="hljs-string">&quot;abc
}
/* unterminated comment</span>
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("thrift", "", "");
    }
}
