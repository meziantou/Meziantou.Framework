namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class HtmlHighlighterTests
{

    [Fact]
    public void Doctype_Html5()
    {
        AssertHighlighter("html",
"""
<!DOCTYPE html>
""",
"""
<span class="hljs-meta">&lt;!DOCTYPE <span class="hljs-keyword">html</span>&gt;</span>
""");
    }

    [Fact]
    public void Doctype_Html5Lowercase()
    {
        AssertHighlighter("html",
"""
<!doctype html>
""",
"""
<span class="hljs-meta">&lt;!doctype <span class="hljs-keyword">html</span>&gt;</span>
""");
    }

    [Fact]
    public void Doctype_Xhtml()
    {
        AssertHighlighter("html",
"""
<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Strict//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd">
""",
"""
<span class="hljs-meta">&lt;!DOCTYPE <span class="hljs-keyword">html</span> <span class="hljs-keyword">PUBLIC</span> <span class="hljs-string">&quot;-//W3C//DTD XHTML 1.0 Strict//EN&quot;</span> <span class="hljs-string">&quot;http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void Doctype_Html4()
    {
        AssertHighlighter("html",
"""
<!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML 4.01//EN" "http://www.w3.org/TR/html4/strict.dtd">
""",
"""
<span class="hljs-meta">&lt;!DOCTYPE <span class="hljs-keyword">HTML</span> <span class="hljs-keyword">PUBLIC</span> <span class="hljs-string">&quot;-//W3C//DTD HTML 4.01//EN&quot;</span> <span class="hljs-string">&quot;http://www.w3.org/TR/html4/strict.dtd&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void Tag_Empty()
    {
        AssertHighlighter("html",
"""
<div></div>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">div</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
""");
    }

    [Fact]
    public void Tag_WithText()
    {
        AssertHighlighter("html",
"""
<p>hello</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>hello<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Tag_Nested()
    {
        AssertHighlighter("html",
"""
<div><p>hello</p></div>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">div</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>hello<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
""");
    }

    [Fact]
    public void Tag_NestedDeep()
    {
        AssertHighlighter("html",
"""
<section><article><h1>Title</h1><p>Body</p></article></section>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">section</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">article</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">h1</span>&gt;</span>Title<span class="hljs-tag">&lt;/<span class="hljs-name">h1</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Body<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">article</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">section</span>&gt;</span>
""");
    }

    [Fact]
    public void Tag_VoidBr()
    {
        AssertHighlighter("html",
"""
<br>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">br</span>&gt;</span>
""");
    }

    [Fact]
    public void Tag_VoidHr()
    {
        AssertHighlighter("html",
"""
<hr>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">hr</span>&gt;</span>
""");
    }

    [Fact]
    public void Tag_VoidImg()
    {
        AssertHighlighter("html",
"""
<img src="logo.png">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">img</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;logo.png&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void Tag_VoidInput()
    {
        AssertHighlighter("html",
"""
<input type="text">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;text&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void Tag_VoidMeta()
    {
        AssertHighlighter("html",
"""
<meta charset="utf-8">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">meta</span> <span class="hljs-attr">charset</span>=<span class="hljs-string">&quot;utf-8&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void Tag_VoidLink()
    {
        AssertHighlighter("html",
"""
<link rel="stylesheet" href="style.css">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">link</span> <span class="hljs-attr">rel</span>=<span class="hljs-string">&quot;stylesheet&quot;</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;style.css&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void Tag_SelfClosing()
    {
        AssertHighlighter("html",
"""
<br />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">br</span> /&gt;</span>
""");
    }

    [Fact]
    public void Tag_SelfClosingImg()
    {
        AssertHighlighter("html",
"""
<img src="logo.png" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">img</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;logo.png&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Tag_UpperCase()
    {
        AssertHighlighter("html",
"""
<DIV>HELLO</DIV>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">DIV</span>&gt;</span>HELLO<span class="hljs-tag">&lt;/<span class="hljs-name">DIV</span>&gt;</span>
""");
    }

    [Fact]
    public void Tag_MixedCase()
    {
        AssertHighlighter("html",
"""
<Section>Content</Section>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">Section</span>&gt;</span>Content<span class="hljs-tag">&lt;/<span class="hljs-name">Section</span>&gt;</span>
""");
    }

    [Fact]
    public void Tag_Siblings()
    {
        AssertHighlighter("html",
"""
<h1>Title</h1>
<p>Body</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">h1</span>&gt;</span>Title<span class="hljs-tag">&lt;/<span class="hljs-name">h1</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Body<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_SingleDouble()
    {
        AssertHighlighter("html",
"""
<div class="foo"></div>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;foo&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_SingleSingle()
    {
        AssertHighlighter("html",
"""
<div class='foo'></div>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&#x27;foo&#x27;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_Unquoted()
    {
        AssertHighlighter("html",
"""
<div class=foo></div>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">class</span>=<span class="hljs-string">foo</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_Multiple()
    {
        AssertHighlighter("html",
"""
<a href="https://example.com" target="_blank" rel="noopener">link</a>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;https://example.com&quot;</span> <span class="hljs-attr">target</span>=<span class="hljs-string">&quot;_blank&quot;</span> <span class="hljs-attr">rel</span>=<span class="hljs-string">&quot;noopener&quot;</span>&gt;</span>link<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_Empty()
    {
        AssertHighlighter("html",
"""
<input value="">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">value</span>=<span class="hljs-string">&quot;&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_BooleanDisabled()
    {
        AssertHighlighter("html",
"""
<button disabled>Click</button>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">button</span> <span class="hljs-attr">disabled</span>&gt;</span>Click<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_BooleanRequired()
    {
        AssertHighlighter("html",
"""
<input required>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">required</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_BooleanReadonly()
    {
        AssertHighlighter("html",
"""
<input readonly>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">readonly</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_BooleanChecked()
    {
        AssertHighlighter("html",
"""
<input type="checkbox" checked>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;checkbox&quot;</span> <span class="hljs-attr">checked</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_BooleanSelected()
    {
        AssertHighlighter("html",
"""
<option selected>One</option>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">option</span> <span class="hljs-attr">selected</span>&gt;</span>One<span class="hljs-tag">&lt;/<span class="hljs-name">option</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_Hyphenated()
    {
        AssertHighlighter("html",
"""
<div data-id="42"></div>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">data-id</span>=<span class="hljs-string">&quot;42&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_DataMultiple()
    {
        AssertHighlighter("html",
"""
<div data-id="42" data-name="alice" data-active="true"></div>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">data-id</span>=<span class="hljs-string">&quot;42&quot;</span> <span class="hljs-attr">data-name</span>=<span class="hljs-string">&quot;alice&quot;</span> <span class="hljs-attr">data-active</span>=<span class="hljs-string">&quot;true&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_Aria()
    {
        AssertHighlighter("html",
"""
<button aria-label="Close" aria-pressed="false">X</button>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">button</span> <span class="hljs-attr">aria-label</span>=<span class="hljs-string">&quot;Close&quot;</span> <span class="hljs-attr">aria-pressed</span>=<span class="hljs-string">&quot;false&quot;</span>&gt;</span>X<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_AriaRole()
    {
        AssertHighlighter("html",
"""
<div role="navigation">...</div>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">role</span>=<span class="hljs-string">&quot;navigation&quot;</span>&gt;</span>...<span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_Event()
    {
        AssertHighlighter("html",
"""
<button onclick="handleClick()">Click</button>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">button</span> <span class="hljs-attr">onclick</span>=<span class="hljs-string">&quot;handleClick()&quot;</span>&gt;</span>Click<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_WithEntity()
    {
        AssertHighlighter("html",
"""
<a title="A &amp; B">link</a>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">title</span>=<span class="hljs-string">&quot;A <span class="hljs-symbol">&amp;amp;</span> B&quot;</span>&gt;</span>link<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_WithSpaces()
    {
        AssertHighlighter("html",
"""
<div class="foo bar baz"></div>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;foo bar baz&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_NumericValue()
    {
        AssertHighlighter("html",
"""
<input type="number" min="0" max="100" step="1">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;number&quot;</span> <span class="hljs-attr">min</span>=<span class="hljs-string">&quot;0&quot;</span> <span class="hljs-attr">max</span>=<span class="hljs-string">&quot;100&quot;</span> <span class="hljs-attr">step</span>=<span class="hljs-string">&quot;1&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_NumericUnquoted()
    {
        AssertHighlighter("html",
"""
<input tabindex=-1>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">tabindex</span>=<span class="hljs-string">-1</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_StyleInline()
    {
        AssertHighlighter("html",
"""
<div style="color: red; margin: 10px;">styled</div>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">style</span>=<span class="hljs-string">&quot;color: red; margin: 10px;&quot;</span>&gt;</span>styled<span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_ColonNamespaced()
    {
        AssertHighlighter("html",
"""
<svg xmlns:xlink="http://www.w3.org/1999/xlink"></svg>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">svg</span> <span class="hljs-attr">xmlns:xlink</span>=<span class="hljs-string">&quot;http://www.w3.org/1999/xlink&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">svg</span>&gt;</span>
""");
    }

    [Fact]
    public void InputType_Text()
    {
        AssertHighlighter("html",
"""
<input type="text" name="username">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;text&quot;</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;username&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void InputType_Password()
    {
        AssertHighlighter("html",
"""
<input type="password" name="pwd">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;password&quot;</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;pwd&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void InputType_Email()
    {
        AssertHighlighter("html",
"""
<input type="email" name="email">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;email&quot;</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;email&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void InputType_Number()
    {
        AssertHighlighter("html",
"""
<input type="number" name="age" min="0">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;number&quot;</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;age&quot;</span> <span class="hljs-attr">min</span>=<span class="hljs-string">&quot;0&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void InputType_Checkbox()
    {
        AssertHighlighter("html",
"""
<input type="checkbox" name="agree" checked>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;checkbox&quot;</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;agree&quot;</span> <span class="hljs-attr">checked</span>&gt;</span>
""");
    }

    [Fact]
    public void InputType_Radio()
    {
        AssertHighlighter("html",
"""
<input type="radio" name="size" value="m">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;radio&quot;</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;size&quot;</span> <span class="hljs-attr">value</span>=<span class="hljs-string">&quot;m&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void InputType_Date()
    {
        AssertHighlighter("html",
"""
<input type="date" name="dob">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;date&quot;</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;dob&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void InputType_Color()
    {
        AssertHighlighter("html",
"""
<input type="color" value="#ff0000">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;color&quot;</span> <span class="hljs-attr">value</span>=<span class="hljs-string">&quot;#ff0000&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void InputType_Range()
    {
        AssertHighlighter("html",
"""
<input type="range" min="0" max="100">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;range&quot;</span> <span class="hljs-attr">min</span>=<span class="hljs-string">&quot;0&quot;</span> <span class="hljs-attr">max</span>=<span class="hljs-string">&quot;100&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void InputType_File()
    {
        AssertHighlighter("html",
"""
<input type="file" accept="image/*" multiple>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;file&quot;</span> <span class="hljs-attr">accept</span>=<span class="hljs-string">&quot;image/*&quot;</span> <span class="hljs-attr">multiple</span>&gt;</span>
""");
    }

    [Fact]
    public void InputType_Hidden()
    {
        AssertHighlighter("html",
"""
<input type="hidden" name="token" value="abc123">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;hidden&quot;</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;token&quot;</span> <span class="hljs-attr">value</span>=<span class="hljs-string">&quot;abc123&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void InputType_Search()
    {
        AssertHighlighter("html",
"""
<input type="search" placeholder="Search...">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;search&quot;</span> <span class="hljs-attr">placeholder</span>=<span class="hljs-string">&quot;Search...&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void Comment_Simple()
    {
        AssertHighlighter("html",
"""
<!-- a comment -->
""",
"""
<span class="hljs-comment">&lt;!-- a comment --&gt;</span>
""");
    }

    [Fact]
    public void Comment_MultiLine()
    {
        AssertHighlighter("html",
"""
<!--
  multi
  line
  comment
-->
""",
"""
<span class="hljs-comment">&lt;!--
  multi
  line
  comment
--&gt;</span>
""");
    }

    [Fact]
    public void Comment_Inline()
    {
        AssertHighlighter("html",
"""
<p>Hello <!-- inline --> World</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Hello <span class="hljs-comment">&lt;!-- inline --&gt;</span> World<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Comment_Conditional()
    {
        AssertHighlighter("html",
"""
<!--[if IE]><p>You are using IE</p><![endif]-->
""",
"""
<span class="hljs-comment">&lt;!--[if IE]&gt;&lt;p&gt;You are using IE&lt;/p&gt;&lt;![endif]--&gt;</span>
""");
    }

    [Fact]
    public void Entity_Amp()
    {
        AssertHighlighter("html",
"""
<p>A &amp; B</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>A <span class="hljs-symbol">&amp;amp;</span> B<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Entity_Lt()
    {
        AssertHighlighter("html",
"""
<p>1 &lt; 2</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>1 <span class="hljs-symbol">&amp;lt;</span> 2<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Entity_Gt()
    {
        AssertHighlighter("html",
"""
<p>2 &gt; 1</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>2 <span class="hljs-symbol">&amp;gt;</span> 1<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Entity_Quot()
    {
        AssertHighlighter("html",
"""
<p>She said &quot;hi&quot;</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>She said <span class="hljs-symbol">&amp;quot;</span>hi<span class="hljs-symbol">&amp;quot;</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Entity_Apos()
    {
        AssertHighlighter("html",
"""
<p>It&apos;s here</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>It<span class="hljs-symbol">&amp;apos;</span>s here<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Entity_Nbsp()
    {
        AssertHighlighter("html",
"""
<p>a&nbsp;b</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>a<span class="hljs-symbol">&amp;nbsp;</span>b<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Entity_Copy()
    {
        AssertHighlighter("html",
"""
<p>&copy; 2026</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-symbol">&amp;copy;</span> 2026<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Entity_Numeric()
    {
        AssertHighlighter("html",
"""
<p>&#65;</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-symbol">&amp;#65;</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Entity_NumericHex()
    {
        AssertHighlighter("html",
"""
<p>&#x41;</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-symbol">&amp;#x41;</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void CData_Simple()
    {
        AssertHighlighter("html",
"""
<![CDATA[some <raw> content & stuff]]>
""",
"""
&lt;![CDATA[some &lt;raw&gt; content &amp; stuff]]&gt;
""");
    }

    [Fact]
    public void CData_InElement()
    {
        AssertHighlighter("html",
"""
<script><![CDATA[if (a < b) { foo(); }]]></script>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span><span class="language-javascript">&lt;![<span class="hljs-variable constant_">CDATA</span>[<span class="hljs-keyword">if</span> (a &lt; b) { <span class="hljs-title function_">foo</span>(); }]]&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
""");
    }

    [Fact]
    public void Semantic_Header()
    {
        AssertHighlighter("html",
"""
<header><h1>Site Title</h1></header>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">header</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">h1</span>&gt;</span>Site Title<span class="hljs-tag">&lt;/<span class="hljs-name">h1</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">header</span>&gt;</span>
""");
    }

    [Fact]
    public void Semantic_Nav()
    {
        AssertHighlighter("html",
"""
<nav><a href="/">Home</a> <a href="/about">About</a></nav>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">nav</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;/&quot;</span>&gt;</span>Home<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span> <span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;/about&quot;</span>&gt;</span>About<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">nav</span>&gt;</span>
""");
    }

    [Fact]
    public void Semantic_Main()
    {
        AssertHighlighter("html",
"""
<main><p>Main content</p></main>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">main</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Main content<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">main</span>&gt;</span>
""");
    }

    [Fact]
    public void Semantic_Article()
    {
        AssertHighlighter("html",
"""
<article><h2>Title</h2><p>Body</p></article>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">article</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">h2</span>&gt;</span>Title<span class="hljs-tag">&lt;/<span class="hljs-name">h2</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Body<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">article</span>&gt;</span>
""");
    }

    [Fact]
    public void Semantic_Section()
    {
        AssertHighlighter("html",
"""
<section><h2>Section</h2></section>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">section</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">h2</span>&gt;</span>Section<span class="hljs-tag">&lt;/<span class="hljs-name">h2</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">section</span>&gt;</span>
""");
    }

    [Fact]
    public void Semantic_Aside()
    {
        AssertHighlighter("html",
"""
<aside><p>Sidebar</p></aside>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">aside</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Sidebar<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">aside</span>&gt;</span>
""");
    }

    [Fact]
    public void Semantic_Footer()
    {
        AssertHighlighter("html",
"""
<footer><p>&copy; 2026</p></footer>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">footer</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-symbol">&amp;copy;</span> 2026<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">footer</span>&gt;</span>
""");
    }

    [Fact]
    public void Semantic_Figure()
    {
        AssertHighlighter("html",
"""
<figure><img src="a.jpg" alt="A"><figcaption>Caption</figcaption></figure>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">figure</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">img</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;a.jpg&quot;</span> <span class="hljs-attr">alt</span>=<span class="hljs-string">&quot;A&quot;</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">figcaption</span>&gt;</span>Caption<span class="hljs-tag">&lt;/<span class="hljs-name">figcaption</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">figure</span>&gt;</span>
""");
    }

    [Fact]
    public void Semantic_Address()
    {
        AssertHighlighter("html",
"""
<address>contact@example.com</address>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">address</span>&gt;</span>contact@example.com<span class="hljs-tag">&lt;/<span class="hljs-name">address</span>&gt;</span>
""");
    }

    [Fact]
    public void Semantic_Time()
    {
        AssertHighlighter("html",
"""
<time datetime="2026-05-26">May 26</time>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">time</span> <span class="hljs-attr">datetime</span>=<span class="hljs-string">&quot;2026-05-26&quot;</span>&gt;</span>May 26<span class="hljs-tag">&lt;/<span class="hljs-name">time</span>&gt;</span>
""");
    }

    [Fact]
    public void Semantic_Mark()
    {
        AssertHighlighter("html",
"""
<p>This is <mark>important</mark>.</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>This is <span class="hljs-tag">&lt;<span class="hljs-name">mark</span>&gt;</span>important<span class="hljs-tag">&lt;/<span class="hljs-name">mark</span>&gt;</span>.<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Form_Simple()
    {
        AssertHighlighter("html",
"""
<form action="/submit" method="post"><input type="text" name="q"><button type="submit">Go</button></form>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">form</span> <span class="hljs-attr">action</span>=<span class="hljs-string">&quot;/submit&quot;</span> <span class="hljs-attr">method</span>=<span class="hljs-string">&quot;post&quot;</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;text&quot;</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;q&quot;</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">button</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;submit&quot;</span>&gt;</span>Go<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">form</span>&gt;</span>
""");
    }

    [Fact]
    public void Form_Label()
    {
        AssertHighlighter("html",
"""
<label for="email">Email</label><input id="email" type="email">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">label</span> <span class="hljs-attr">for</span>=<span class="hljs-string">&quot;email&quot;</span>&gt;</span>Email<span class="hljs-tag">&lt;/<span class="hljs-name">label</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">id</span>=<span class="hljs-string">&quot;email&quot;</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;email&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void Form_Fieldset()
    {
        AssertHighlighter("html",
"""
<fieldset><legend>Personal</legend><input type="text"></fieldset>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">fieldset</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">legend</span>&gt;</span>Personal<span class="hljs-tag">&lt;/<span class="hljs-name">legend</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;text&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">fieldset</span>&gt;</span>
""");
    }

    [Fact]
    public void Form_Select()
    {
        AssertHighlighter("html",
"""
<select name="size"><option value="s">Small</option><option value="m" selected>Medium</option></select>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">select</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;size&quot;</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">option</span> <span class="hljs-attr">value</span>=<span class="hljs-string">&quot;s&quot;</span>&gt;</span>Small<span class="hljs-tag">&lt;/<span class="hljs-name">option</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">option</span> <span class="hljs-attr">value</span>=<span class="hljs-string">&quot;m&quot;</span> <span class="hljs-attr">selected</span>&gt;</span>Medium<span class="hljs-tag">&lt;/<span class="hljs-name">option</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">select</span>&gt;</span>
""");
    }

    [Fact]
    public void Form_OptGroup()
    {
        AssertHighlighter("html",
"""
<select><optgroup label="Colors"><option>Red</option><option>Blue</option></optgroup></select>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">select</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">optgroup</span> <span class="hljs-attr">label</span>=<span class="hljs-string">&quot;Colors&quot;</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">option</span>&gt;</span>Red<span class="hljs-tag">&lt;/<span class="hljs-name">option</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">option</span>&gt;</span>Blue<span class="hljs-tag">&lt;/<span class="hljs-name">option</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">optgroup</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">select</span>&gt;</span>
""");
    }

    [Fact]
    public void Form_Textarea()
    {
        AssertHighlighter("html",
"""
<textarea name="msg" rows="4" cols="40">Default text</textarea>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">textarea</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;msg&quot;</span> <span class="hljs-attr">rows</span>=<span class="hljs-string">&quot;4&quot;</span> <span class="hljs-attr">cols</span>=<span class="hljs-string">&quot;40&quot;</span>&gt;</span>Default text<span class="hljs-tag">&lt;/<span class="hljs-name">textarea</span>&gt;</span>
""");
    }

    [Fact]
    public void Form_ButtonTypes()
    {
        AssertHighlighter("html",
"""
<button type="submit">OK</button> <button type="reset">Reset</button> <button type="button">Cancel</button>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">button</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;submit&quot;</span>&gt;</span>OK<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span> <span class="hljs-tag">&lt;<span class="hljs-name">button</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;reset&quot;</span>&gt;</span>Reset<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span> <span class="hljs-tag">&lt;<span class="hljs-name">button</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;button&quot;</span>&gt;</span>Cancel<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span>
""");
    }

    [Fact]
    public void Form_EncType()
    {
        AssertHighlighter("html",
"""
<form enctype="multipart/form-data" method="post" action="/upload"><input type="file" name="f"></form>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">form</span> <span class="hljs-attr">enctype</span>=<span class="hljs-string">&quot;multipart/form-data&quot;</span> <span class="hljs-attr">method</span>=<span class="hljs-string">&quot;post&quot;</span> <span class="hljs-attr">action</span>=<span class="hljs-string">&quot;/upload&quot;</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;file&quot;</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;f&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">form</span>&gt;</span>
""");
    }

    [Fact]
    public void Form_Datalist()
    {
        AssertHighlighter("html",
"""
<input list="browsers" name="browser"><datalist id="browsers"><option value="Chrome"><option value="Firefox"></datalist>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">list</span>=<span class="hljs-string">&quot;browsers&quot;</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;browser&quot;</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">datalist</span> <span class="hljs-attr">id</span>=<span class="hljs-string">&quot;browsers&quot;</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">option</span> <span class="hljs-attr">value</span>=<span class="hljs-string">&quot;Chrome&quot;</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">option</span> <span class="hljs-attr">value</span>=<span class="hljs-string">&quot;Firefox&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">datalist</span>&gt;</span>
""");
    }

    [Fact]
    public void Media_Img()
    {
        AssertHighlighter("html",
"""
<img src="logo.png" alt="Logo" width="200" height="100">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">img</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;logo.png&quot;</span> <span class="hljs-attr">alt</span>=<span class="hljs-string">&quot;Logo&quot;</span> <span class="hljs-attr">width</span>=<span class="hljs-string">&quot;200&quot;</span> <span class="hljs-attr">height</span>=<span class="hljs-string">&quot;100&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void Media_ImgSrcset()
    {
        AssertHighlighter("html",
"""
<img src="a.png" srcset="a-2x.png 2x, a-3x.png 3x" alt="A">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">img</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;a.png&quot;</span> <span class="hljs-attr">srcset</span>=<span class="hljs-string">&quot;a-2x.png 2x, a-3x.png 3x&quot;</span> <span class="hljs-attr">alt</span>=<span class="hljs-string">&quot;A&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void Media_Picture()
    {
        AssertHighlighter("html",
"""
<picture><source media="(min-width: 800px)" srcset="big.jpg"><img src="small.jpg" alt=""></picture>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">picture</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">source</span> <span class="hljs-attr">media</span>=<span class="hljs-string">&quot;(min-width: 800px)&quot;</span> <span class="hljs-attr">srcset</span>=<span class="hljs-string">&quot;big.jpg&quot;</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">img</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;small.jpg&quot;</span> <span class="hljs-attr">alt</span>=<span class="hljs-string">&quot;&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">picture</span>&gt;</span>
""");
    }

    [Fact]
    public void Media_Video()
    {
        AssertHighlighter("html",
"""
<video src="movie.mp4" controls autoplay muted loop></video>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">video</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;movie.mp4&quot;</span> <span class="hljs-attr">controls</span> <span class="hljs-attr">autoplay</span> <span class="hljs-attr">muted</span> <span class="hljs-attr">loop</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">video</span>&gt;</span>
""");
    }

    [Fact]
    public void Media_VideoSource()
    {
        AssertHighlighter("html",
"""
<video controls><source src="movie.mp4" type="video/mp4"><source src="movie.webm" type="video/webm"></video>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">video</span> <span class="hljs-attr">controls</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">source</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;movie.mp4&quot;</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;video/mp4&quot;</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">source</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;movie.webm&quot;</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;video/webm&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">video</span>&gt;</span>
""");
    }

    [Fact]
    public void Media_Audio()
    {
        AssertHighlighter("html",
"""
<audio src="song.mp3" controls></audio>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">audio</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;song.mp3&quot;</span> <span class="hljs-attr">controls</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">audio</span>&gt;</span>
""");
    }

    [Fact]
    public void Media_Track()
    {
        AssertHighlighter("html",
"""
<video controls><track kind="subtitles" src="subs.vtt" srclang="en" label="English" default></video>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">video</span> <span class="hljs-attr">controls</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">track</span> <span class="hljs-attr">kind</span>=<span class="hljs-string">&quot;subtitles&quot;</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;subs.vtt&quot;</span> <span class="hljs-attr">srclang</span>=<span class="hljs-string">&quot;en&quot;</span> <span class="hljs-attr">label</span>=<span class="hljs-string">&quot;English&quot;</span> <span class="hljs-attr">default</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">video</span>&gt;</span>
""");
    }

    [Fact]
    public void Media_Iframe()
    {
        AssertHighlighter("html",
"""
<iframe src="https://example.com" width="600" height="400" loading="lazy"></iframe>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">iframe</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;https://example.com&quot;</span> <span class="hljs-attr">width</span>=<span class="hljs-string">&quot;600&quot;</span> <span class="hljs-attr">height</span>=<span class="hljs-string">&quot;400&quot;</span> <span class="hljs-attr">loading</span>=<span class="hljs-string">&quot;lazy&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">iframe</span>&gt;</span>
""");
    }

    [Fact]
    public void Table_Simple()
    {
        AssertHighlighter("html",
"""
<table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">table</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">tr</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">th</span>&gt;</span>A<span class="hljs-tag">&lt;/<span class="hljs-name">th</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">th</span>&gt;</span>B<span class="hljs-tag">&lt;/<span class="hljs-name">th</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">tr</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">tr</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">td</span>&gt;</span>1<span class="hljs-tag">&lt;/<span class="hljs-name">td</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">td</span>&gt;</span>2<span class="hljs-tag">&lt;/<span class="hljs-name">td</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">tr</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">table</span>&gt;</span>
""");
    }

    [Fact]
    public void Table_WithHead()
    {
        AssertHighlighter("html",
"""
<table><thead><tr><th>H</th></tr></thead><tbody><tr><td>B</td></tr></tbody><tfoot><tr><td>F</td></tr></tfoot></table>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">table</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">thead</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">tr</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">th</span>&gt;</span>H<span class="hljs-tag">&lt;/<span class="hljs-name">th</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">tr</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">thead</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">tbody</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">tr</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">td</span>&gt;</span>B<span class="hljs-tag">&lt;/<span class="hljs-name">td</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">tr</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">tbody</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">tfoot</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">tr</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">td</span>&gt;</span>F<span class="hljs-tag">&lt;/<span class="hljs-name">td</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">tr</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">tfoot</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">table</span>&gt;</span>
""");
    }

    [Fact]
    public void Table_WithSpan()
    {
        AssertHighlighter("html",
"""
<table><tr><td colspan="2" rowspan="3">Big</td></tr></table>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">table</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">tr</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">td</span> <span class="hljs-attr">colspan</span>=<span class="hljs-string">&quot;2&quot;</span> <span class="hljs-attr">rowspan</span>=<span class="hljs-string">&quot;3&quot;</span>&gt;</span>Big<span class="hljs-tag">&lt;/<span class="hljs-name">td</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">tr</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">table</span>&gt;</span>
""");
    }

    [Fact]
    public void Table_Caption()
    {
        AssertHighlighter("html",
"""
<table><caption>Stats</caption><tr><td>1</td></tr></table>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">table</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">caption</span>&gt;</span>Stats<span class="hljs-tag">&lt;/<span class="hljs-name">caption</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">tr</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">td</span>&gt;</span>1<span class="hljs-tag">&lt;/<span class="hljs-name">td</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">tr</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">table</span>&gt;</span>
""");
    }

    [Fact]
    public void Table_ColGroup()
    {
        AssertHighlighter("html",
"""
<table><colgroup><col span="2" style="background: red"></colgroup><tr><td>1</td><td>2</td></tr></table>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">table</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">colgroup</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">col</span> <span class="hljs-attr">span</span>=<span class="hljs-string">&quot;2&quot;</span> <span class="hljs-attr">style</span>=<span class="hljs-string">&quot;background: red&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">colgroup</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">tr</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">td</span>&gt;</span>1<span class="hljs-tag">&lt;/<span class="hljs-name">td</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">td</span>&gt;</span>2<span class="hljs-tag">&lt;/<span class="hljs-name">td</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">tr</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">table</span>&gt;</span>
""");
    }

    [Fact]
    public void CustomElement_Empty()
    {
        AssertHighlighter("html",
"""
<my-component></my-component>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">my-component</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">my-component</span>&gt;</span>
""");
    }

    [Fact]
    public void CustomElement_WithAttrs()
    {
        AssertHighlighter("html",
"""
<user-card name="Alice" avatar="/a.png"></user-card>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">user-card</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;Alice&quot;</span> <span class="hljs-attr">avatar</span>=<span class="hljs-string">&quot;/a.png&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">user-card</span>&gt;</span>
""");
    }

    [Fact]
    public void CustomElement_NestedDashes()
    {
        AssertHighlighter("html",
"""
<x-foo-bar-baz>content</x-foo-bar-baz>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">x-foo-bar-baz</span>&gt;</span>content<span class="hljs-tag">&lt;/<span class="hljs-name">x-foo-bar-baz</span>&gt;</span>
""");
    }

    [Fact]
    public void CustomElement_Is()
    {
        AssertHighlighter("html",
"""
<button is="my-button">Click</button>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">button</span> <span class="hljs-attr">is</span>=<span class="hljs-string">&quot;my-button&quot;</span>&gt;</span>Click<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span>
""");
    }

    [Fact]
    public void Template_Element()
    {
        AssertHighlighter("html",
"""
<template id="card"><div class="card"></div></template>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">template</span> <span class="hljs-attr">id</span>=<span class="hljs-string">&quot;card&quot;</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;card&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">template</span>&gt;</span>
""");
    }

    [Fact]
    public void Template_Slot()
    {
        AssertHighlighter("html",
"""
<slot name="header">Default header</slot>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">slot</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;header&quot;</span>&gt;</span>Default header<span class="hljs-tag">&lt;/<span class="hljs-name">slot</span>&gt;</span>
""");
    }

    [Fact]
    public void Template_SlotDefault()
    {
        AssertHighlighter("html",
"""
<slot></slot>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">slot</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">slot</span>&gt;</span>
""");
    }

    [Fact]
    public void InteractiveElement_Details()
    {
        AssertHighlighter("html",
"""
<details><summary>More</summary><p>Hidden content</p></details>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">details</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">summary</span>&gt;</span>More<span class="hljs-tag">&lt;/<span class="hljs-name">summary</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Hidden content<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">details</span>&gt;</span>
""");
    }

    [Fact]
    public void InteractiveElement_Dialog()
    {
        AssertHighlighter("html",
"""
<dialog id="d" open><p>Hello</p><button>Close</button></dialog>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">dialog</span> <span class="hljs-attr">id</span>=<span class="hljs-string">&quot;d&quot;</span> <span class="hljs-attr">open</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Hello<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">button</span>&gt;</span>Close<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">dialog</span>&gt;</span>
""");
    }

    [Fact]
    public void InteractiveElement_Progress()
    {
        AssertHighlighter("html",
"""
<progress value="70" max="100">70%</progress>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">progress</span> <span class="hljs-attr">value</span>=<span class="hljs-string">&quot;70&quot;</span> <span class="hljs-attr">max</span>=<span class="hljs-string">&quot;100&quot;</span>&gt;</span>70%<span class="hljs-tag">&lt;/<span class="hljs-name">progress</span>&gt;</span>
""");
    }

    [Fact]
    public void InteractiveElement_Meter()
    {
        AssertHighlighter("html",
"""
<meter value="6" min="0" max="10">6 out of 10</meter>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">meter</span> <span class="hljs-attr">value</span>=<span class="hljs-string">&quot;6&quot;</span> <span class="hljs-attr">min</span>=<span class="hljs-string">&quot;0&quot;</span> <span class="hljs-attr">max</span>=<span class="hljs-string">&quot;10&quot;</span>&gt;</span>6 out of 10<span class="hljs-tag">&lt;/<span class="hljs-name">meter</span>&gt;</span>
""");
    }

    [Fact]
    public void Inline_Svg()
    {
        AssertHighlighter("html",
"""
<svg width="100" height="100" xmlns="http://www.w3.org/2000/svg"><circle cx="50" cy="50" r="40" fill="red" /></svg>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">svg</span> <span class="hljs-attr">width</span>=<span class="hljs-string">&quot;100&quot;</span> <span class="hljs-attr">height</span>=<span class="hljs-string">&quot;100&quot;</span> <span class="hljs-attr">xmlns</span>=<span class="hljs-string">&quot;http://www.w3.org/2000/svg&quot;</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">circle</span> <span class="hljs-attr">cx</span>=<span class="hljs-string">&quot;50&quot;</span> <span class="hljs-attr">cy</span>=<span class="hljs-string">&quot;50&quot;</span> <span class="hljs-attr">r</span>=<span class="hljs-string">&quot;40&quot;</span> <span class="hljs-attr">fill</span>=<span class="hljs-string">&quot;red&quot;</span> /&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">svg</span>&gt;</span>
""");
    }

    [Fact]
    public void Inline_SvgPath()
    {
        AssertHighlighter("html",
"""
<svg viewBox="0 0 24 24"><path d="M12 2L2 22h20z" fill="currentColor" /></svg>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">svg</span> <span class="hljs-attr">viewBox</span>=<span class="hljs-string">&quot;0 0 24 24&quot;</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">path</span> <span class="hljs-attr">d</span>=<span class="hljs-string">&quot;M12 2L2 22h20z&quot;</span> <span class="hljs-attr">fill</span>=<span class="hljs-string">&quot;currentColor&quot;</span> /&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">svg</span>&gt;</span>
""");
    }

    [Fact]
    public void Inline_SvgUse()
    {
        AssertHighlighter("html",
"""
<svg><use href="#icon-x" /></svg>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">svg</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">use</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;#icon-x&quot;</span> /&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">svg</span>&gt;</span>
""");
    }

    [Fact]
    public void Inline_MathMl()
    {
        AssertHighlighter("html",
"""
<math><msup><mi>x</mi><mn>2</mn></msup></math>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">math</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">msup</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">mi</span>&gt;</span>x<span class="hljs-tag">&lt;/<span class="hljs-name">mi</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">mn</span>&gt;</span>2<span class="hljs-tag">&lt;/<span class="hljs-name">mn</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">msup</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">math</span>&gt;</span>
""");
    }

    [Fact]
    public void Embedded_Script()
    {
        AssertHighlighter("html",
"""
<script>console.log("hi");</script>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span><span class="language-javascript"><span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(<span class="hljs-string">&quot;hi&quot;</span>);</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
""");
    }

    [Fact]
    public void Embedded_ScriptSrc()
    {
        AssertHighlighter("html",
"""
<script src="app.js" defer></script>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">script</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;app.js&quot;</span> <span class="hljs-attr">defer</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
""");
    }

    [Fact]
    public void Embedded_ScriptModule()
    {
        AssertHighlighter("html",
"""
<script type="module" src="app.js"></script>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">script</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;module&quot;</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;app.js&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
""");
    }

    [Fact]
    public void Embedded_ScriptInlineJs()
    {
        AssertHighlighter("html",
"""
<script>
  function greet(name) {
    return "Hello " + name;
  }
</script>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span><span class="language-javascript">
  <span class="hljs-keyword">function</span> <span class="hljs-title function_">greet</span>(<span class="hljs-params">name</span>) {
    <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;Hello &quot;</span> + name;
  }
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
""");
    }

    [Fact]
    public void Embedded_ScriptJson()
    {
        AssertHighlighter("html",
"""
<script type="application/json">{"name": "alice"}</script>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">script</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;application/json&quot;</span>&gt;</span><span class="language-javascript">{<span class="hljs-string">&quot;name&quot;</span>: <span class="hljs-string">&quot;alice&quot;</span>}</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
""");
    }

    [Fact]
    public void Embedded_Style()
    {
        AssertHighlighter("html",
"""
<style>p { color: red; }</style>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">style</span>&gt;</span><span class="language-css"><span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red; }</span><span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>
""");
    }

    [Fact]
    public void Embedded_StyleScoped()
    {
        AssertHighlighter("html",
"""
<style>
  .card { padding: 1rem; }
  .card:hover { background: gray; }
</style>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">style</span>&gt;</span><span class="language-css">
  <span class="hljs-selector-class">.card</span> { <span class="hljs-attribute">padding</span>: <span class="hljs-number">1rem</span>; }
  <span class="hljs-selector-class">.card</span><span class="hljs-selector-pseudo">:hover</span> { <span class="hljs-attribute">background</span>: gray; }
</span><span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>
""");
    }

    [Fact]
    public void Embedded_NoScript()
    {
        AssertHighlighter("html",
"""
<noscript>JavaScript is required.</noscript>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">noscript</span>&gt;</span>JavaScript is required.<span class="hljs-tag">&lt;/<span class="hljs-name">noscript</span>&gt;</span>
""");
    }

    [Fact]
    public void HeadElements_Title()
    {
        AssertHighlighter("html",
"""
<title>My Page</title>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">title</span>&gt;</span>My Page<span class="hljs-tag">&lt;/<span class="hljs-name">title</span>&gt;</span>
""");
    }

    [Fact]
    public void HeadElements_MetaCharset()
    {
        AssertHighlighter("html",
"""
<meta charset="utf-8">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">meta</span> <span class="hljs-attr">charset</span>=<span class="hljs-string">&quot;utf-8&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void HeadElements_MetaViewport()
    {
        AssertHighlighter("html",
"""
<meta name="viewport" content="width=device-width, initial-scale=1">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">meta</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;viewport&quot;</span> <span class="hljs-attr">content</span>=<span class="hljs-string">&quot;width=device-width, initial-scale=1&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void HeadElements_MetaOpenGraph()
    {
        AssertHighlighter("html",
"""
<meta property="og:title" content="My Page">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">meta</span> <span class="hljs-attr">property</span>=<span class="hljs-string">&quot;og:title&quot;</span> <span class="hljs-attr">content</span>=<span class="hljs-string">&quot;My Page&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void HeadElements_MetaTwitter()
    {
        AssertHighlighter("html",
"""
<meta name="twitter:card" content="summary_large_image">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">meta</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;twitter:card&quot;</span> <span class="hljs-attr">content</span>=<span class="hljs-string">&quot;summary_large_image&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void HeadElements_LinkRelStylesheet()
    {
        AssertHighlighter("html",
"""
<link rel="stylesheet" href="style.css">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">link</span> <span class="hljs-attr">rel</span>=<span class="hljs-string">&quot;stylesheet&quot;</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;style.css&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void HeadElements_LinkRelIcon()
    {
        AssertHighlighter("html",
"""
<link rel="icon" type="image/png" href="favicon.png">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">link</span> <span class="hljs-attr">rel</span>=<span class="hljs-string">&quot;icon&quot;</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;image/png&quot;</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;favicon.png&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void HeadElements_LinkPreload()
    {
        AssertHighlighter("html",
"""
<link rel="preload" href="font.woff2" as="font" type="font/woff2" crossorigin>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">link</span> <span class="hljs-attr">rel</span>=<span class="hljs-string">&quot;preload&quot;</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;font.woff2&quot;</span> <span class="hljs-attr">as</span>=<span class="hljs-string">&quot;font&quot;</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;font/woff2&quot;</span> <span class="hljs-attr">crossorigin</span>&gt;</span>
""");
    }

    [Fact]
    public void HeadElements_Base()
    {
        AssertHighlighter("html",
"""
<base href="https://example.com/">
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">base</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;https://example.com/&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void Composite_MinimalPage()
    {
        AssertHighlighter("html",
"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Hi</title>
</head>
<body>
  <p>Hello</p>
</body>
</html>
""",
"""
<span class="hljs-meta">&lt;!DOCTYPE <span class="hljs-keyword">html</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">html</span> <span class="hljs-attr">lang</span>=<span class="hljs-string">&quot;en&quot;</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">head</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">meta</span> <span class="hljs-attr">charset</span>=<span class="hljs-string">&quot;utf-8&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">title</span>&gt;</span>Hi<span class="hljs-tag">&lt;/<span class="hljs-name">title</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">head</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">body</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Hello<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">body</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">html</span>&gt;</span>
""");
    }

    [Fact]
    public void Composite_WithStyleScript()
    {
        AssertHighlighter("html",
"""
<!DOCTYPE html>
<html>
<head>
  <style>p { color: red; }</style>
</head>
<body>
  <p>Hi</p>
  <script>console.log("ready");</script>
</body>
</html>
""",
"""
<span class="hljs-meta">&lt;!DOCTYPE <span class="hljs-keyword">html</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">html</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">head</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">style</span>&gt;</span><span class="language-css"><span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red; }</span><span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">head</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">body</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Hi<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span><span class="language-javascript"><span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(<span class="hljs-string">&quot;ready&quot;</span>);</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">body</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">html</span>&gt;</span>
""");
    }

    [Fact]
    public void Composite_Card()
    {
        AssertHighlighter("html",
"""
<article class="card">
  <header>
    <h2>Title</h2>
  </header>
  <p>Body of the card.</p>
  <footer>
    <button>OK</button>
  </footer>
</article>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">article</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;card&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">header</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">h2</span>&gt;</span>Title<span class="hljs-tag">&lt;/<span class="hljs-name">h2</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">header</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Body of the card.<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">footer</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">button</span>&gt;</span>OK<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">footer</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">article</span>&gt;</span>
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("html",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyText()
    {
        AssertHighlighter("html",
"""
just plain text
""",
"""
just plain text
""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("html",
"""
<!-- only -->
""",
"""
<span class="hljs-comment">&lt;!-- only --&gt;</span>
""");
    }

    [Fact]
    public void SpecialEdge_TextThenTag()
    {
        AssertHighlighter("html",
"""
before<br>after
""",
"""
before<span class="hljs-tag">&lt;<span class="hljs-name">br</span>&gt;</span>after
""");
    }

    [Fact]
    public void SpecialEdge_OrphanLt()
    {
        AssertHighlighter("html",
"""
<p>a &lt; b</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>a <span class="hljs-symbol">&amp;lt;</span> b<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void SpecialEdge_UnclosedVoid()
    {
        AssertHighlighter("html",
"""
<br><br><br>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">br</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">br</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">br</span>&gt;</span>
""");
    }

    [Fact]
    public void SpecialEdge_XmlProlog()
    {
        AssertHighlighter("html",
"""
<?xml version="1.0" encoding="UTF-8"?>
<root><a/></root>
""",
"""
<span class="hljs-meta">&lt;?xml version=<span class="hljs-string">&quot;1.0&quot;</span> encoding=<span class="hljs-string">&quot;UTF-8&quot;</span>?&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">root</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">a</span>/&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">root</span>&gt;</span>
""");
    }
}
