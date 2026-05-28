namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class XmlHighlighterTests
{

    [Fact]
    public void Prolog_XmlDeclaration()
    {
        AssertHighlighter("xml",
"""
<?xml version="1.0"?>
""",
"""
<span class="hljs-meta">&lt;?xml version=<span class="hljs-string">&quot;1.0&quot;</span>?&gt;</span>
""");
    }

    [Fact]
    public void Prolog_XmlDeclarationEncoded()
    {
        AssertHighlighter("xml",
"""
<?xml version="1.0" encoding="UTF-8"?>
""",
"""
<span class="hljs-meta">&lt;?xml version=<span class="hljs-string">&quot;1.0&quot;</span> encoding=<span class="hljs-string">&quot;UTF-8&quot;</span>?&gt;</span>
""");
    }

    [Fact]
    public void Prolog_XmlDeclarationStandalone()
    {
        AssertHighlighter("xml",
"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
""",
"""
<span class="hljs-meta">&lt;?xml version=<span class="hljs-string">&quot;1.0&quot;</span> encoding=<span class="hljs-string">&quot;UTF-8&quot;</span> standalone=<span class="hljs-string">&quot;yes&quot;</span>?&gt;</span>
""");
    }

    [Fact]
    public void Prolog_XmlDeclarationSingle()
    {
        AssertHighlighter("xml",
"""
<?xml version='1.0' encoding='UTF-8'?>
""",
"""
<span class="hljs-meta">&lt;?xml version=&#x27;1.0&#x27; encoding=&#x27;UTF-8&#x27;?&gt;</span>
""");
    }

    [Fact]
    public void Element_Empty()
    {
        AssertHighlighter("xml",
"""
<root />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">root</span> /&gt;</span>
""");
    }

    [Fact]
    public void Element_EmptyPair()
    {
        AssertHighlighter("xml",
"""
<root></root>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">root</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">root</span>&gt;</span>
""");
    }

    [Fact]
    public void Element_WithText()
    {
        AssertHighlighter("xml",
"""
<title>Hello world</title>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">title</span>&gt;</span>Hello world<span class="hljs-tag">&lt;/<span class="hljs-name">title</span>&gt;</span>
""");
    }

    [Fact]
    public void Element_Nested()
    {
        AssertHighlighter("xml",
"""
<book><title>X</title><author>Y</author></book>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">book</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">title</span>&gt;</span>X<span class="hljs-tag">&lt;/<span class="hljs-name">title</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">author</span>&gt;</span>Y<span class="hljs-tag">&lt;/<span class="hljs-name">author</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">book</span>&gt;</span>
""");
    }

    [Fact]
    public void Element_DeepNested()
    {
        AssertHighlighter("xml",
"""
<library>
  <book>
    <title>X</title>
    <chapters>
      <chapter>1</chapter>
      <chapter>2</chapter>
    </chapters>
  </book>
</library>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">library</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">book</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">title</span>&gt;</span>X<span class="hljs-tag">&lt;/<span class="hljs-name">title</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">chapters</span>&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">chapter</span>&gt;</span>1<span class="hljs-tag">&lt;/<span class="hljs-name">chapter</span>&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">chapter</span>&gt;</span>2<span class="hljs-tag">&lt;/<span class="hljs-name">chapter</span>&gt;</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">chapters</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">book</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">library</span>&gt;</span>
""");
    }

    [Fact]
    public void Element_MixedContent()
    {
        AssertHighlighter("xml",
"""
<p>Hello <b>bold</b> and <i>italic</i> world.</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Hello <span class="hljs-tag">&lt;<span class="hljs-name">b</span>&gt;</span>bold<span class="hljs-tag">&lt;/<span class="hljs-name">b</span>&gt;</span> and <span class="hljs-tag">&lt;<span class="hljs-name">i</span>&gt;</span>italic<span class="hljs-tag">&lt;/<span class="hljs-name">i</span>&gt;</span> world.<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Element_Hyphenated()
    {
        AssertHighlighter("xml",
"""
<my-element>data</my-element>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">my-element</span>&gt;</span>data<span class="hljs-tag">&lt;/<span class="hljs-name">my-element</span>&gt;</span>
""");
    }

    [Fact]
    public void Element_Dotted()
    {
        AssertHighlighter("xml",
"""
<some.thing>data</some.thing>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">some.thing</span>&gt;</span>data<span class="hljs-tag">&lt;/<span class="hljs-name">some.thing</span>&gt;</span>
""");
    }

    [Fact]
    public void Element_NumericSuffix()
    {
        AssertHighlighter("xml",
"""
<h1>title</h1>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">h1</span>&gt;</span>title<span class="hljs-tag">&lt;/<span class="hljs-name">h1</span>&gt;</span>
""");
    }

    [Fact]
    public void Element_WhitespacePreserve()
    {
        AssertHighlighter("xml",
"""
<line>   leading and trailing   </line>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">line</span>&gt;</span>   leading and trailing   <span class="hljs-tag">&lt;/<span class="hljs-name">line</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_SingleDouble()
    {
        AssertHighlighter("xml",
"""
<book id="b-001" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">book</span> <span class="hljs-attr">id</span>=<span class="hljs-string">&quot;b-001&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Attribute_SingleSingle()
    {
        AssertHighlighter("xml",
"""
<book id='b-001' />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">book</span> <span class="hljs-attr">id</span>=<span class="hljs-string">&#x27;b-001&#x27;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Attribute_Multiple()
    {
        AssertHighlighter("xml",
"""
<book id="b-001" lang="en" year="2026" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">book</span> <span class="hljs-attr">id</span>=<span class="hljs-string">&quot;b-001&quot;</span> <span class="hljs-attr">lang</span>=<span class="hljs-string">&quot;en&quot;</span> <span class="hljs-attr">year</span>=<span class="hljs-string">&quot;2026&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Attribute_Empty()
    {
        AssertHighlighter("xml",
"""
<input value="" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">value</span>=<span class="hljs-string">&quot;&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Attribute_WithEntity()
    {
        AssertHighlighter("xml",
"""
<note title="A &amp; B" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">note</span> <span class="hljs-attr">title</span>=<span class="hljs-string">&quot;A <span class="hljs-symbol">&amp;amp;</span> B&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Attribute_NumericValue()
    {
        AssertHighlighter("xml",
"""
<rect x="10" y="20" width="100" height="50" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">rect</span> <span class="hljs-attr">x</span>=<span class="hljs-string">&quot;10&quot;</span> <span class="hljs-attr">y</span>=<span class="hljs-string">&quot;20&quot;</span> <span class="hljs-attr">width</span>=<span class="hljs-string">&quot;100&quot;</span> <span class="hljs-attr">height</span>=<span class="hljs-string">&quot;50&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Attribute_NamespacedXmlLang()
    {
        AssertHighlighter("xml",
"""
<para xml:lang="en">Hello</para>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">para</span> <span class="hljs-attr">xml:lang</span>=<span class="hljs-string">&quot;en&quot;</span>&gt;</span>Hello<span class="hljs-tag">&lt;/<span class="hljs-name">para</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_NamespacedXmlSpace()
    {
        AssertHighlighter("xml",
"""
<para xml:space="preserve">  spaced  </para>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">para</span> <span class="hljs-attr">xml:space</span>=<span class="hljs-string">&quot;preserve&quot;</span>&gt;</span>  spaced  <span class="hljs-tag">&lt;/<span class="hljs-name">para</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_NamespacedCustom()
    {
        AssertHighlighter("xml",
"""
<note xlink:href="#anchor" xlink:title="Anchor" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">note</span> <span class="hljs-attr">xlink:href</span>=<span class="hljs-string">&quot;#anchor&quot;</span> <span class="hljs-attr">xlink:title</span>=<span class="hljs-string">&quot;Anchor&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Attribute_XmlBase()
    {
        AssertHighlighter("xml",
"""
<root xml:base="https://example.com/">data</root>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">root</span> <span class="hljs-attr">xml:base</span>=<span class="hljs-string">&quot;https://example.com/&quot;</span>&gt;</span>data<span class="hljs-tag">&lt;/<span class="hljs-name">root</span>&gt;</span>
""");
    }

    [Fact]
    public void Attribute_XmlId()
    {
        AssertHighlighter("xml",
"""
<para xml:id="p1">Hi</para>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">para</span> <span class="hljs-attr">xml:id</span>=<span class="hljs-string">&quot;p1&quot;</span>&gt;</span>Hi<span class="hljs-tag">&lt;/<span class="hljs-name">para</span>&gt;</span>
""");
    }

    [Fact]
    public void Namespace_Default()
    {
        AssertHighlighter("xml",
"""
<root xmlns="http://example.com/ns">data</root>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">root</span> <span class="hljs-attr">xmlns</span>=<span class="hljs-string">&quot;http://example.com/ns&quot;</span>&gt;</span>data<span class="hljs-tag">&lt;/<span class="hljs-name">root</span>&gt;</span>
""");
    }

    [Fact]
    public void Namespace_Prefixed()
    {
        AssertHighlighter("xml",
"""
<svg:circle xmlns:svg="http://www.w3.org/2000/svg" r="10" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">svg:circle</span> <span class="hljs-attr">xmlns:svg</span>=<span class="hljs-string">&quot;http://www.w3.org/2000/svg&quot;</span> <span class="hljs-attr">r</span>=<span class="hljs-string">&quot;10&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Namespace_Multiple()
    {
        AssertHighlighter("xml",
"""
<root xmlns="http://example.com/default" xmlns:x="http://example.com/x" xmlns:y="http://example.com/y" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">root</span> <span class="hljs-attr">xmlns</span>=<span class="hljs-string">&quot;http://example.com/default&quot;</span> <span class="hljs-attr">xmlns:x</span>=<span class="hljs-string">&quot;http://example.com/x&quot;</span> <span class="hljs-attr">xmlns:y</span>=<span class="hljs-string">&quot;http://example.com/y&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Namespace_AttributeNs()
    {
        AssertHighlighter("xml",
"""
<root xmlns:dc="http://purl.org/dc/elements/1.1/" dc:title="Title" dc:creator="Alice" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">root</span> <span class="hljs-attr">xmlns:dc</span>=<span class="hljs-string">&quot;http://purl.org/dc/elements/1.1/&quot;</span> <span class="hljs-attr">dc:title</span>=<span class="hljs-string">&quot;Title&quot;</span> <span class="hljs-attr">dc:creator</span>=<span class="hljs-string">&quot;Alice&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Namespace_ScopedRedeclare()
    {
        AssertHighlighter("xml",
"""
<a xmlns="urn:outer">
  <b xmlns="urn:inner">data</b>
</a>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">xmlns</span>=<span class="hljs-string">&quot;urn:outer&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">b</span> <span class="hljs-attr">xmlns</span>=<span class="hljs-string">&quot;urn:inner&quot;</span>&gt;</span>data<span class="hljs-tag">&lt;/<span class="hljs-name">b</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
""");
    }

    [Fact]
    public void Comment_Simple()
    {
        AssertHighlighter("xml",
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
        AssertHighlighter("xml",
"""
<!--
  multi
  line
-->
""",
"""
<span class="hljs-comment">&lt;!--
  multi
  line
--&gt;</span>
""");
    }

    [Fact]
    public void Comment_Inline()
    {
        AssertHighlighter("xml",
"""
<p>Hello <!-- inline --> world</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Hello <span class="hljs-comment">&lt;!-- inline --&gt;</span> world<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Comment_WithDashes()
    {
        AssertHighlighter("xml",
"""
<!-- contains - single dash -->
""",
"""
<span class="hljs-comment">&lt;!-- contains - single dash --&gt;</span>
""");
    }

    [Fact]
    public void ProcessingInstruction_Stylesheet()
    {
        AssertHighlighter("xml",
"""
<?xml-stylesheet type="text/xsl" href="style.xsl"?>
""",
"""
<span class="hljs-meta">&lt;?xml-stylesheet type=<span class="hljs-string">&quot;text/xsl&quot;</span> href=<span class="hljs-string">&quot;style.xsl&quot;</span>?&gt;</span>
""");
    }

    [Fact]
    public void ProcessingInstruction_StylesheetCss()
    {
        AssertHighlighter("xml",
"""
<?xml-stylesheet type="text/css" href="style.css"?>
""",
"""
<span class="hljs-meta">&lt;?xml-stylesheet type=<span class="hljs-string">&quot;text/css&quot;</span> href=<span class="hljs-string">&quot;style.css&quot;</span>?&gt;</span>
""");
    }

    [Fact]
    public void ProcessingInstruction_XmlModel()
    {
        AssertHighlighter("xml",
"""
<?xml-model href="schema.rng" schematypens="http://relaxng.org/ns/structure/1.0"?>
""",
"""
<span class="hljs-meta">&lt;?xml-model href=<span class="hljs-string">&quot;schema.rng&quot;</span> schematypens=<span class="hljs-string">&quot;http://relaxng.org/ns/structure/1.0&quot;</span>?&gt;</span>
""");
    }

    [Fact]
    public void ProcessingInstruction_Custom()
    {
        AssertHighlighter("xml",
"""
<?php echo "hi"; ?>
""",
"""
<span class="hljs-meta">&lt;?php echo &quot;hi&quot;; ?&gt;</span>
""");
    }

    [Fact]
    public void CData_Simple()
    {
        AssertHighlighter("xml",
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
        AssertHighlighter("xml",
"""
<script><![CDATA[if (a < b) { foo(); }]]></script>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span>&lt;![CDATA[if (a &lt; b) { foo(); }]]&gt;<span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
""");
    }

    [Fact]
    public void CData_MultiLine()
    {
        AssertHighlighter("xml",
"""
<code><![CDATA[
function f(x) {
  return x < 0 ? -x : x;
}
]]></code>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">code</span>&gt;</span>&lt;![CDATA[
function f(x) {
  return x &lt; 0 ? -x : x;
}
]]&gt;<span class="hljs-tag">&lt;/<span class="hljs-name">code</span>&gt;</span>
""");
    }

    [Fact]
    public void CData_EmptyCdata()
    {
        AssertHighlighter("xml",
"""
<data><![CDATA[]]></data>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">data</span>&gt;</span>&lt;![CDATA[]]&gt;<span class="hljs-tag">&lt;/<span class="hljs-name">data</span>&gt;</span>
""");
    }

    [Fact]
    public void Doctype_External()
    {
        AssertHighlighter("xml",
"""
<!DOCTYPE note SYSTEM "note.dtd">
""",
"""
<span class="hljs-meta">&lt;!DOCTYPE <span class="hljs-keyword">note</span> <span class="hljs-keyword">SYSTEM</span> <span class="hljs-string">&quot;note.dtd&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void Doctype_PublicSystem()
    {
        AssertHighlighter("xml",
"""
<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Strict//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd">
""",
"""
<span class="hljs-meta">&lt;!DOCTYPE <span class="hljs-keyword">html</span> <span class="hljs-keyword">PUBLIC</span> <span class="hljs-string">&quot;-//W3C//DTD XHTML 1.0 Strict//EN&quot;</span> <span class="hljs-string">&quot;http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd&quot;</span>&gt;</span>
""");
    }

    [Fact]
    public void Doctype_InternalSubset()
    {
        AssertHighlighter("xml",
"""
<!DOCTYPE note [
  <!ELEMENT note (to, from, body)>
  <!ELEMENT to (#PCDATA)>
  <!ELEMENT from (#PCDATA)>
  <!ELEMENT body (#PCDATA)>
]>
""",
"""
<span class="hljs-meta">&lt;!DOCTYPE <span class="hljs-keyword">note</span> [
  <span class="hljs-meta">&lt;!ELEMENT <span class="hljs-keyword">note</span> (<span class="hljs-keyword">to</span>, <span class="hljs-keyword">from</span>, <span class="hljs-keyword">body</span>)&gt;</span>
  <span class="hljs-meta">&lt;!ELEMENT <span class="hljs-keyword">to</span> (<span class="hljs-keyword">#PCDATA</span>)&gt;</span>
  <span class="hljs-meta">&lt;!ELEMENT <span class="hljs-keyword">from</span> (<span class="hljs-keyword">#PCDATA</span>)&gt;</span>
  <span class="hljs-meta">&lt;!ELEMENT <span class="hljs-keyword">body</span> (<span class="hljs-keyword">#PCDATA</span>)&gt;</span>
]&gt;</span>
""");
    }

    [Fact]
    public void Doctype_AttlistDecl()
    {
        AssertHighlighter("xml",
"""
<!DOCTYPE note [
  <!ATTLIST note id ID #REQUIRED>
]>
""",
"""
<span class="hljs-meta">&lt;!DOCTYPE <span class="hljs-keyword">note</span> [
  <span class="hljs-meta">&lt;!ATTLIST <span class="hljs-keyword">note</span> <span class="hljs-keyword">id</span> <span class="hljs-keyword">ID</span> <span class="hljs-keyword">#REQUIRED</span>&gt;</span>
]&gt;</span>
""");
    }

    [Fact]
    public void Doctype_EntityDecl()
    {
        AssertHighlighter("xml",
"""
<!DOCTYPE note [
  <!ENTITY copy "&#169;">
  <!ENTITY company "Example Inc.">
]>
""",
"""
<span class="hljs-meta">&lt;!DOCTYPE <span class="hljs-keyword">note</span> [
  <span class="hljs-meta">&lt;!ENTITY <span class="hljs-keyword">copy</span> <span class="hljs-string">&quot;&amp;#169;&quot;</span>&gt;</span>
  <span class="hljs-meta">&lt;!ENTITY <span class="hljs-keyword">company</span> <span class="hljs-string">&quot;Example Inc.&quot;</span>&gt;</span>
]&gt;</span>
""");
    }

    [Fact]
    public void Doctype_ParameterEntity()
    {
        AssertHighlighter("xml",
"""
<!DOCTYPE note [
  <!ENTITY % common "id ID #REQUIRED">
]>
""",
"""
<span class="hljs-meta">&lt;!DOCTYPE <span class="hljs-keyword">note</span> [
  <span class="hljs-meta">&lt;!ENTITY % <span class="hljs-keyword">common</span> <span class="hljs-string">&quot;id ID #REQUIRED&quot;</span>&gt;</span>
]&gt;</span>
""");
    }

    [Fact]
    public void Doctype_NotationDecl()
    {
        AssertHighlighter("xml",
"""
<!DOCTYPE note [
  <!NOTATION jpeg PUBLIC "JPG 1.0">
]>
""",
"""
<span class="hljs-meta">&lt;!DOCTYPE <span class="hljs-keyword">note</span> [
  <span class="hljs-meta">&lt;!NOTATION <span class="hljs-keyword">jpeg</span> <span class="hljs-keyword">PUBLIC</span> <span class="hljs-string">&quot;JPG 1.0&quot;</span>&gt;</span>
]&gt;</span>
""");
    }

    [Fact]
    public void Entity_Amp()
    {
        AssertHighlighter("xml",
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
        AssertHighlighter("xml",
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
        AssertHighlighter("xml",
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
        AssertHighlighter("xml",
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
        AssertHighlighter("xml",
"""
<p>It&apos;s here</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>It<span class="hljs-symbol">&amp;apos;</span>s here<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Entity_NumericDec()
    {
        AssertHighlighter("xml",
"""
<p>&#65;&#66;&#67;</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-symbol">&amp;#65;</span><span class="hljs-symbol">&amp;#66;</span><span class="hljs-symbol">&amp;#67;</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Entity_NumericHex()
    {
        AssertHighlighter("xml",
"""
<p>&#x41;&#x42;&#x43;</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-symbol">&amp;#x41;</span><span class="hljs-symbol">&amp;#x42;</span><span class="hljs-symbol">&amp;#x43;</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Entity_Custom()
    {
        AssertHighlighter("xml",
"""
<p>&copyright; 2026 &company;</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-symbol">&amp;copyright;</span> 2026 <span class="hljs-symbol">&amp;company;</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Svg_Basic()
    {
        AssertHighlighter("xml",
"""
<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
  <circle cx="50" cy="50" r="40" fill="red" />
</svg>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">svg</span> <span class="hljs-attr">xmlns</span>=<span class="hljs-string">&quot;http://www.w3.org/2000/svg&quot;</span> <span class="hljs-attr">width</span>=<span class="hljs-string">&quot;100&quot;</span> <span class="hljs-attr">height</span>=<span class="hljs-string">&quot;100&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">circle</span> <span class="hljs-attr">cx</span>=<span class="hljs-string">&quot;50&quot;</span> <span class="hljs-attr">cy</span>=<span class="hljs-string">&quot;50&quot;</span> <span class="hljs-attr">r</span>=<span class="hljs-string">&quot;40&quot;</span> <span class="hljs-attr">fill</span>=<span class="hljs-string">&quot;red&quot;</span> /&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">svg</span>&gt;</span>
""");
    }

    [Fact]
    public void Svg_Path()
    {
        AssertHighlighter("xml",
"""
<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
  <path d="M12 2L2 22h20z" fill="currentColor" />
</svg>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">svg</span> <span class="hljs-attr">viewBox</span>=<span class="hljs-string">&quot;0 0 24 24&quot;</span> <span class="hljs-attr">xmlns</span>=<span class="hljs-string">&quot;http://www.w3.org/2000/svg&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">path</span> <span class="hljs-attr">d</span>=<span class="hljs-string">&quot;M12 2L2 22h20z&quot;</span> <span class="hljs-attr">fill</span>=<span class="hljs-string">&quot;currentColor&quot;</span> /&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">svg</span>&gt;</span>
""");
    }

    [Fact]
    public void Svg_Transform()
    {
        AssertHighlighter("xml",
"""
<svg xmlns="http://www.w3.org/2000/svg">
  <g transform="translate(10, 20) rotate(45)">
    <rect x="0" y="0" width="50" height="50" />
  </g>
</svg>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">svg</span> <span class="hljs-attr">xmlns</span>=<span class="hljs-string">&quot;http://www.w3.org/2000/svg&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">g</span> <span class="hljs-attr">transform</span>=<span class="hljs-string">&quot;translate(10, 20) rotate(45)&quot;</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">rect</span> <span class="hljs-attr">x</span>=<span class="hljs-string">&quot;0&quot;</span> <span class="hljs-attr">y</span>=<span class="hljs-string">&quot;0&quot;</span> <span class="hljs-attr">width</span>=<span class="hljs-string">&quot;50&quot;</span> <span class="hljs-attr">height</span>=<span class="hljs-string">&quot;50&quot;</span> /&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">g</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">svg</span>&gt;</span>
""");
    }

    [Fact]
    public void Svg_Gradient()
    {
        AssertHighlighter("xml",
"""
<svg xmlns="http://www.w3.org/2000/svg">
  <defs>
    <linearGradient id="grad" x1="0%" y1="0%" x2="100%" y2="0%">
      <stop offset="0%" stop-color="red" />
      <stop offset="100%" stop-color="blue" />
    </linearGradient>
  </defs>
  <rect fill="url(#grad)" width="200" height="100" />
</svg>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">svg</span> <span class="hljs-attr">xmlns</span>=<span class="hljs-string">&quot;http://www.w3.org/2000/svg&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">defs</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">linearGradient</span> <span class="hljs-attr">id</span>=<span class="hljs-string">&quot;grad&quot;</span> <span class="hljs-attr">x1</span>=<span class="hljs-string">&quot;0%&quot;</span> <span class="hljs-attr">y1</span>=<span class="hljs-string">&quot;0%&quot;</span> <span class="hljs-attr">x2</span>=<span class="hljs-string">&quot;100%&quot;</span> <span class="hljs-attr">y2</span>=<span class="hljs-string">&quot;0%&quot;</span>&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">stop</span> <span class="hljs-attr">offset</span>=<span class="hljs-string">&quot;0%&quot;</span> <span class="hljs-attr">stop-color</span>=<span class="hljs-string">&quot;red&quot;</span> /&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">stop</span> <span class="hljs-attr">offset</span>=<span class="hljs-string">&quot;100%&quot;</span> <span class="hljs-attr">stop-color</span>=<span class="hljs-string">&quot;blue&quot;</span> /&gt;</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">linearGradient</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">defs</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">rect</span> <span class="hljs-attr">fill</span>=<span class="hljs-string">&quot;url(#grad)&quot;</span> <span class="hljs-attr">width</span>=<span class="hljs-string">&quot;200&quot;</span> <span class="hljs-attr">height</span>=<span class="hljs-string">&quot;100&quot;</span> /&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">svg</span>&gt;</span>
""");
    }

    [Fact]
    public void Svg_WithXlink()
    {
        AssertHighlighter("xml",
"""
<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink">
  <use xlink:href="#icon-x" />
</svg>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">svg</span> <span class="hljs-attr">xmlns</span>=<span class="hljs-string">&quot;http://www.w3.org/2000/svg&quot;</span> <span class="hljs-attr">xmlns:xlink</span>=<span class="hljs-string">&quot;http://www.w3.org/1999/xlink&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">use</span> <span class="hljs-attr">xlink:href</span>=<span class="hljs-string">&quot;#icon-x&quot;</span> /&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">svg</span>&gt;</span>
""");
    }

    [Fact]
    public void RssAtom_RssFeed()
    {
        AssertHighlighter("xml",
"""
<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0">
  <channel>
    <title>My Blog</title>
    <link>https://example.com</link>
    <description>A blog</description>
    <item>
      <title>Post 1</title>
      <link>https://example.com/p1</link>
      <pubDate>Mon, 26 May 2026 12:00:00 GMT</pubDate>
    </item>
  </channel>
</rss>
""",
"""
<span class="hljs-meta">&lt;?xml version=<span class="hljs-string">&quot;1.0&quot;</span> encoding=<span class="hljs-string">&quot;UTF-8&quot;</span>?&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">rss</span> <span class="hljs-attr">version</span>=<span class="hljs-string">&quot;2.0&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">channel</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">title</span>&gt;</span>My Blog<span class="hljs-tag">&lt;/<span class="hljs-name">title</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">link</span>&gt;</span>https://example.com<span class="hljs-tag">&lt;/<span class="hljs-name">link</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">description</span>&gt;</span>A blog<span class="hljs-tag">&lt;/<span class="hljs-name">description</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">item</span>&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">title</span>&gt;</span>Post 1<span class="hljs-tag">&lt;/<span class="hljs-name">title</span>&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">link</span>&gt;</span>https://example.com/p1<span class="hljs-tag">&lt;/<span class="hljs-name">link</span>&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">pubDate</span>&gt;</span>Mon, 26 May 2026 12:00:00 GMT<span class="hljs-tag">&lt;/<span class="hljs-name">pubDate</span>&gt;</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">item</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">channel</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">rss</span>&gt;</span>
""");
    }

    [Fact]
    public void RssAtom_AtomFeed()
    {
        AssertHighlighter("xml",
"""
<?xml version="1.0" encoding="UTF-8"?>
<feed xmlns="http://www.w3.org/2005/Atom">
  <title>My Blog</title>
  <id>tag:example.com,2026:blog</id>
  <updated>2026-05-26T12:00:00Z</updated>
  <entry>
    <title>Post 1</title>
    <id>tag:example.com,2026:p1</id>
    <updated>2026-05-26T12:00:00Z</updated>
    <summary>First post.</summary>
  </entry>
</feed>
""",
"""
<span class="hljs-meta">&lt;?xml version=<span class="hljs-string">&quot;1.0&quot;</span> encoding=<span class="hljs-string">&quot;UTF-8&quot;</span>?&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">feed</span> <span class="hljs-attr">xmlns</span>=<span class="hljs-string">&quot;http://www.w3.org/2005/Atom&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">title</span>&gt;</span>My Blog<span class="hljs-tag">&lt;/<span class="hljs-name">title</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">id</span>&gt;</span>tag:example.com,2026:blog<span class="hljs-tag">&lt;/<span class="hljs-name">id</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">updated</span>&gt;</span>2026-05-26T12:00:00Z<span class="hljs-tag">&lt;/<span class="hljs-name">updated</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">entry</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">title</span>&gt;</span>Post 1<span class="hljs-tag">&lt;/<span class="hljs-name">title</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">id</span>&gt;</span>tag:example.com,2026:p1<span class="hljs-tag">&lt;/<span class="hljs-name">id</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">updated</span>&gt;</span>2026-05-26T12:00:00Z<span class="hljs-tag">&lt;/<span class="hljs-name">updated</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">summary</span>&gt;</span>First post.<span class="hljs-tag">&lt;/<span class="hljs-name">summary</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">entry</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">feed</span>&gt;</span>
""");
    }

    [Fact]
    public void Soap_Envelope()
    {
        AssertHighlighter("xml",
"""
<?xml version="1.0"?>
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
  <soap:Header>
    <auth:Token xmlns:auth="urn:auth">abc123</auth:Token>
  </soap:Header>
  <soap:Body>
    <m:GetPrice xmlns:m="urn:example">
      <m:Item>Widget</m:Item>
    </m:GetPrice>
  </soap:Body>
</soap:Envelope>
""",
"""
<span class="hljs-meta">&lt;?xml version=<span class="hljs-string">&quot;1.0&quot;</span>?&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">soap:Envelope</span> <span class="hljs-attr">xmlns:soap</span>=<span class="hljs-string">&quot;http://schemas.xmlsoap.org/soap/envelope/&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">soap:Header</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">auth:Token</span> <span class="hljs-attr">xmlns:auth</span>=<span class="hljs-string">&quot;urn:auth&quot;</span>&gt;</span>abc123<span class="hljs-tag">&lt;/<span class="hljs-name">auth:Token</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">soap:Header</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">soap:Body</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">m:GetPrice</span> <span class="hljs-attr">xmlns:m</span>=<span class="hljs-string">&quot;urn:example&quot;</span>&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">m:Item</span>&gt;</span>Widget<span class="hljs-tag">&lt;/<span class="hljs-name">m:Item</span>&gt;</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">m:GetPrice</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">soap:Body</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">soap:Envelope</span>&gt;</span>
""");
    }

    [Fact]
    public void Soap_Fault()
    {
        AssertHighlighter("xml",
"""
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
  <soap:Body>
    <soap:Fault>
      <faultcode>soap:Server</faultcode>
      <faultstring>Internal error</faultstring>
    </soap:Fault>
  </soap:Body>
</soap:Envelope>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">soap:Envelope</span> <span class="hljs-attr">xmlns:soap</span>=<span class="hljs-string">&quot;http://schemas.xmlsoap.org/soap/envelope/&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">soap:Body</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">soap:Fault</span>&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">faultcode</span>&gt;</span>soap:Server<span class="hljs-tag">&lt;/<span class="hljs-name">faultcode</span>&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">faultstring</span>&gt;</span>Internal error<span class="hljs-tag">&lt;/<span class="hljs-name">faultstring</span>&gt;</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">soap:Fault</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">soap:Body</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">soap:Envelope</span>&gt;</span>
""");
    }

    [Fact]
    public void Xslt_Stylesheet()
    {
        AssertHighlighter("xml",
"""
<?xml version="1.0"?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:template match="/">
    <html>
      <body>
        <h1><xsl:value-of select="title" /></h1>
      </body>
    </html>
  </xsl:template>
</xsl:stylesheet>
""",
"""
<span class="hljs-meta">&lt;?xml version=<span class="hljs-string">&quot;1.0&quot;</span>?&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">xsl:stylesheet</span> <span class="hljs-attr">version</span>=<span class="hljs-string">&quot;1.0&quot;</span> <span class="hljs-attr">xmlns:xsl</span>=<span class="hljs-string">&quot;http://www.w3.org/1999/XSL/Transform&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">xsl:template</span> <span class="hljs-attr">match</span>=<span class="hljs-string">&quot;/&quot;</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">html</span>&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">body</span>&gt;</span>
        <span class="hljs-tag">&lt;<span class="hljs-name">h1</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">xsl:value-of</span> <span class="hljs-attr">select</span>=<span class="hljs-string">&quot;title&quot;</span> /&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">h1</span>&gt;</span>
      <span class="hljs-tag">&lt;/<span class="hljs-name">body</span>&gt;</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">html</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">xsl:template</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">xsl:stylesheet</span>&gt;</span>
""");
    }

    [Fact]
    public void Xslt_ForEach()
    {
        AssertHighlighter("xml",
"""
<xsl:for-each select="items/item">
  <li><xsl:value-of select="@name" /></li>
</xsl:for-each>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">xsl:for-each</span> <span class="hljs-attr">select</span>=<span class="hljs-string">&quot;items/item&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">li</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">xsl:value-of</span> <span class="hljs-attr">select</span>=<span class="hljs-string">&quot;@name&quot;</span> /&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">li</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">xsl:for-each</span>&gt;</span>
""");
    }

    [Fact]
    public void Xslt_IfChoose()
    {
        AssertHighlighter("xml",
"""
<xsl:choose>
  <xsl:when test="@active='true'">
    <span class="active"><xsl:value-of select="name" /></span>
  </xsl:when>
  <xsl:otherwise>
    <span><xsl:value-of select="name" /></span>
  </xsl:otherwise>
</xsl:choose>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">xsl:choose</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">xsl:when</span> <span class="hljs-attr">test</span>=<span class="hljs-string">&quot;@active=&#x27;true&#x27;&quot;</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">span</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;active&quot;</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">xsl:value-of</span> <span class="hljs-attr">select</span>=<span class="hljs-string">&quot;name&quot;</span> /&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">span</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">xsl:when</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">xsl:otherwise</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">span</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">xsl:value-of</span> <span class="hljs-attr">select</span>=<span class="hljs-string">&quot;name&quot;</span> /&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">span</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">xsl:otherwise</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">xsl:choose</span>&gt;</span>
""");
    }

    [Fact]
    public void Xsd_Schema()
    {
        AssertHighlighter("xml",
"""
<?xml version="1.0"?>
<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
  <xs:element name="note" type="xs:string" />
  <xs:complexType name="address">
    <xs:sequence>
      <xs:element name="street" type="xs:string" />
      <xs:element name="city" type="xs:string" />
    </xs:sequence>
  </xs:complexType>
</xs:schema>
""",
"""
<span class="hljs-meta">&lt;?xml version=<span class="hljs-string">&quot;1.0&quot;</span>?&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">xs:schema</span> <span class="hljs-attr">xmlns:xs</span>=<span class="hljs-string">&quot;http://www.w3.org/2001/XMLSchema&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">xs:element</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;note&quot;</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;xs:string&quot;</span> /&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">xs:complexType</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;address&quot;</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">xs:sequence</span>&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">xs:element</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;street&quot;</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;xs:string&quot;</span> /&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">xs:element</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;city&quot;</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;xs:string&quot;</span> /&gt;</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">xs:sequence</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">xs:complexType</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">xs:schema</span>&gt;</span>
""");
    }

    [Fact]
    public void Xsd_Restriction()
    {
        AssertHighlighter("xml",
"""
<xs:simpleType name="zip">
  <xs:restriction base="xs:string">
    <xs:pattern value="\d{5}(-\d{4})?" />
  </xs:restriction>
</xs:simpleType>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">xs:simpleType</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;zip&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">xs:restriction</span> <span class="hljs-attr">base</span>=<span class="hljs-string">&quot;xs:string&quot;</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">xs:pattern</span> <span class="hljs-attr">value</span>=<span class="hljs-string">&quot;\d{5}(-\d{4})?&quot;</span> /&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">xs:restriction</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">xs:simpleType</span>&gt;</span>
""");
    }

    [Fact]
    public void Rdf_Basic()
    {
        AssertHighlighter("xml",
"""
<?xml version="1.0"?>
<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"
         xmlns:dc="http://purl.org/dc/elements/1.1/">
  <rdf:Description rdf:about="https://example.com/page">
    <dc:title>Example</dc:title>
    <dc:creator>Alice</dc:creator>
  </rdf:Description>
</rdf:RDF>
""",
"""
<span class="hljs-meta">&lt;?xml version=<span class="hljs-string">&quot;1.0&quot;</span>?&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">rdf:RDF</span> <span class="hljs-attr">xmlns:rdf</span>=<span class="hljs-string">&quot;http://www.w3.org/1999/02/22-rdf-syntax-ns#&quot;</span>
         <span class="hljs-attr">xmlns:dc</span>=<span class="hljs-string">&quot;http://purl.org/dc/elements/1.1/&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">rdf:Description</span> <span class="hljs-attr">rdf:about</span>=<span class="hljs-string">&quot;https://example.com/page&quot;</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">dc:title</span>&gt;</span>Example<span class="hljs-tag">&lt;/<span class="hljs-name">dc:title</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">dc:creator</span>&gt;</span>Alice<span class="hljs-tag">&lt;/<span class="hljs-name">dc:creator</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">rdf:Description</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">rdf:RDF</span>&gt;</span>
""");
    }

    [Fact]
    public void MathMl_Equation()
    {
        AssertHighlighter("xml",
"""
<math xmlns="http://www.w3.org/1998/Math/MathML">
  <mrow>
    <mi>x</mi>
    <mo>=</mo>
    <mfrac>
      <mrow><mo>-</mo><mi>b</mi></mrow>
      <mrow><mn>2</mn><mi>a</mi></mrow>
    </mfrac>
  </mrow>
</math>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">math</span> <span class="hljs-attr">xmlns</span>=<span class="hljs-string">&quot;http://www.w3.org/1998/Math/MathML&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">mrow</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">mi</span>&gt;</span>x<span class="hljs-tag">&lt;/<span class="hljs-name">mi</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">mo</span>&gt;</span>=<span class="hljs-tag">&lt;/<span class="hljs-name">mo</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">mfrac</span>&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">mrow</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">mo</span>&gt;</span>-<span class="hljs-tag">&lt;/<span class="hljs-name">mo</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">mi</span>&gt;</span>b<span class="hljs-tag">&lt;/<span class="hljs-name">mi</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">mrow</span>&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">mrow</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">mn</span>&gt;</span>2<span class="hljs-tag">&lt;/<span class="hljs-name">mn</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">mi</span>&gt;</span>a<span class="hljs-tag">&lt;/<span class="hljs-name">mi</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">mrow</span>&gt;</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">mfrac</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">mrow</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">math</span>&gt;</span>
""");
    }

    [Fact]
    public void Plist_AppleProperty()
    {
        AssertHighlighter("xml",
"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>CFBundleName</key>
    <string>MyApp</string>
    <key>CFBundleVersion</key>
    <string>1.0</string>
  </dict>
</plist>
""",
"""
<span class="hljs-meta">&lt;?xml version=<span class="hljs-string">&quot;1.0&quot;</span> encoding=<span class="hljs-string">&quot;UTF-8&quot;</span>?&gt;</span>
<span class="hljs-meta">&lt;!DOCTYPE <span class="hljs-keyword">plist</span> <span class="hljs-keyword">PUBLIC</span> <span class="hljs-string">&quot;-//Apple//DTD PLIST 1.0//EN&quot;</span> <span class="hljs-string">&quot;http://www.apple.com/DTDs/PropertyList-1.0.dtd&quot;</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">plist</span> <span class="hljs-attr">version</span>=<span class="hljs-string">&quot;1.0&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">dict</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">key</span>&gt;</span>CFBundleName<span class="hljs-tag">&lt;/<span class="hljs-name">key</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">string</span>&gt;</span>MyApp<span class="hljs-tag">&lt;/<span class="hljs-name">string</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">key</span>&gt;</span>CFBundleVersion<span class="hljs-tag">&lt;/<span class="hljs-name">key</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">string</span>&gt;</span>1.0<span class="hljs-tag">&lt;/<span class="hljs-name">string</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">dict</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">plist</span>&gt;</span>
""");
    }

    [Fact]
    public void Composite_Catalog()
    {
        AssertHighlighter("xml",
"""
<?xml version="1.0" encoding="UTF-8"?>
<catalog>
  <book id="b-001">
    <title lang="en">XML in a Nutshell</title>
    <author>Alice</author>
    <year>2020</year>
    <price currency="USD">29.99</price>
  </book>
  <book id="b-002">
    <title lang="en">Programming XML</title>
    <author>Bob</author>
    <year>2022</year>
    <price currency="EUR">35.00</price>
  </book>
</catalog>
""",
"""
<span class="hljs-meta">&lt;?xml version=<span class="hljs-string">&quot;1.0&quot;</span> encoding=<span class="hljs-string">&quot;UTF-8&quot;</span>?&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">catalog</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">book</span> <span class="hljs-attr">id</span>=<span class="hljs-string">&quot;b-001&quot;</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">title</span> <span class="hljs-attr">lang</span>=<span class="hljs-string">&quot;en&quot;</span>&gt;</span>XML in a Nutshell<span class="hljs-tag">&lt;/<span class="hljs-name">title</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">author</span>&gt;</span>Alice<span class="hljs-tag">&lt;/<span class="hljs-name">author</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">year</span>&gt;</span>2020<span class="hljs-tag">&lt;/<span class="hljs-name">year</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">price</span> <span class="hljs-attr">currency</span>=<span class="hljs-string">&quot;USD&quot;</span>&gt;</span>29.99<span class="hljs-tag">&lt;/<span class="hljs-name">price</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">book</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">book</span> <span class="hljs-attr">id</span>=<span class="hljs-string">&quot;b-002&quot;</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">title</span> <span class="hljs-attr">lang</span>=<span class="hljs-string">&quot;en&quot;</span>&gt;</span>Programming XML<span class="hljs-tag">&lt;/<span class="hljs-name">title</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">author</span>&gt;</span>Bob<span class="hljs-tag">&lt;/<span class="hljs-name">author</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">year</span>&gt;</span>2022<span class="hljs-tag">&lt;/<span class="hljs-name">year</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">price</span> <span class="hljs-attr">currency</span>=<span class="hljs-string">&quot;EUR&quot;</span>&gt;</span>35.00<span class="hljs-tag">&lt;/<span class="hljs-name">price</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">book</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">catalog</span>&gt;</span>
""");
    }

    [Fact]
    public void Composite_SitemapXml()
    {
        AssertHighlighter("xml",
"""
<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url>
    <loc>https://example.com/</loc>
    <lastmod>2026-05-26</lastmod>
    <changefreq>weekly</changefreq>
    <priority>1.0</priority>
  </url>
</urlset>
""",
"""
<span class="hljs-meta">&lt;?xml version=<span class="hljs-string">&quot;1.0&quot;</span> encoding=<span class="hljs-string">&quot;UTF-8&quot;</span>?&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">urlset</span> <span class="hljs-attr">xmlns</span>=<span class="hljs-string">&quot;http://www.sitemaps.org/schemas/sitemap/0.9&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">url</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">loc</span>&gt;</span>https://example.com/<span class="hljs-tag">&lt;/<span class="hljs-name">loc</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">lastmod</span>&gt;</span>2026-05-26<span class="hljs-tag">&lt;/<span class="hljs-name">lastmod</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">changefreq</span>&gt;</span>weekly<span class="hljs-tag">&lt;/<span class="hljs-name">changefreq</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">priority</span>&gt;</span>1.0<span class="hljs-tag">&lt;/<span class="hljs-name">priority</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">url</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">urlset</span>&gt;</span>
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("xml",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyProlog()
    {
        AssertHighlighter("xml",
"""
<?xml version="1.0"?>
""",
"""
<span class="hljs-meta">&lt;?xml version=<span class="hljs-string">&quot;1.0&quot;</span>?&gt;</span>
""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("xml",
"""
<!-- empty doc -->
""",
"""
<span class="hljs-comment">&lt;!-- empty doc --&gt;</span>
""");
    }

    [Fact]
    public void SpecialEdge_OnlyText()
    {
        AssertHighlighter("xml",
"""
just plain text
""",
"""
just plain text
""");
    }

    [Fact]
    public void SpecialEdge_OnlyCdata()
    {
        AssertHighlighter("xml",
"""
<![CDATA[raw]]>
""",
"""
&lt;![CDATA[raw]]&gt;
""");
    }

    [Fact]
    public void SpecialEdge_TrailingNewline()
    {
        AssertHighlighter("xml",
"""
<root />

""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">root</span> /&gt;</span>

""");
    }

    [Fact]
    public void SpecialEdge_BomLikePrefix()
    {
        AssertHighlighter("xml",
"""
﻿<?xml version="1.0"?>
<root />
""",
"""
﻿<span class="hljs-meta">&lt;?xml version=<span class="hljs-string">&quot;1.0&quot;</span>?&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">root</span> /&gt;</span>
""");
    }
}
