namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class AsciiDocHighlighterTests
{
    [Fact]
    public void EmptyDocument()
    {
        AssertHighlighter("asciidoc",
"""

""",
"""

""");
    }

    [Fact]
    public void DocumentHeader()
    {
        AssertHighlighter("asciidoc",
"""
= Document Title
Author Name <author@example.com>
v1.0, 2024-01-01
:toc: left
:icons: font
:source-highlighter: highlight.js

This is the preamble.
""",
"""
<span class="hljs-section">= Document Title</span>
Author Name &lt;author@example.com&gt;
v1.0, 2024-01-01
<span class="hljs-meta">:toc:</span> left
<span class="hljs-meta">:icons:</span> font
<span class="hljs-meta">:source-highlighter:</span> highlight.js

This is the preamble.
""");
    }

    [Fact]
    public void Headings()
    {
        AssertHighlighter("asciidoc",
"""
= Level 0

== Level 1

=== Level 2 ===

==== Level 3

===== Level 4
====== Level 5
======= Not a heading
""",
"""
<span class="hljs-section">= Level 0</span>

<span class="hljs-section">== Level 1</span>

<span class="hljs-section">=== Level 2 ===</span>

<span class="hljs-section">==== Level 3</span>

<span class="hljs-section">===== Level 4</span>
<span class="hljs-section">====== Level 5</span>
======= Not a heading
""");
    }

    [Fact]
    public void SetextHeadings()
    {
        AssertHighlighter("asciidoc",
"""
Document Title
==============

Section
-------

Sub
~~~~

Not [a] title
-------------
""",
"""
<span class="hljs-section">Document Title
==============</span>

<span class="hljs-section">Section
-------</span>

<span class="hljs-section">Sub
~~~~</span>

Not [a] title
-------------
""");
    }

    [Fact]
    public void DocumentAttributes()
    {
        AssertHighlighter("asciidoc",
"""
:author: Jane Doe
:email: jane@example.com
:revnumber: 1.2
:!sectnums:
:experimental:
Hello {author}, see {email}.
""",
"""
<span class="hljs-meta">:author:</span> Jane Doe
<span class="hljs-meta">:email:</span> jane@example.com
<span class="hljs-meta">:revnumber:</span> 1.2
<span class="hljs-meta">:!sectnums:</span>
<span class="hljs-meta">:experimental:</span>
Hello {author}, see {email}.
""");
    }

    [Fact]
    public void BlockAttributesAndDelimitedBlocks()
    {
        AssertHighlighter("asciidoc",
"""
[source,csharp]
----
var x = 1;
Console.WriteLine(x);
----

[NOTE]
====
An admonition block.
With *bold*.
====

[quote, Albert Einstein]
____
Imagination is more important than knowledge.
____
""",
"""
<span class="hljs-meta">[source,csharp]</span>
<span class="hljs-code">----
var x = 1;
Console.WriteLine(x);
----</span>

<span class="hljs-meta">[NOTE]</span>
====
An admonition block.
With *bold*.
====

<span class="hljs-meta">[quote, Albert Einstein]</span>
<span class="hljs-quote">____
Imagination is more important than knowledge.
____</span>
""");
    }

    [Fact]
    public void ListingAndLiteralBlocks()
    {
        AssertHighlighter("asciidoc",
"""
....
literal block
  indented
....

----
code
----
""",
"""
<span class="hljs-code">....
literal block
  indented
....</span>

<span class="hljs-code">----
code
----</span>
""");
    }

    [Fact]
    public void UnterminatedListingBlock()
    {
        AssertHighlighter("asciidoc",
"""
----
code that never ends
more code
""",
"""
<span class="hljs-code">----
code that never ends
more code</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("asciidoc",
"""
// a line comment
Text // not a comment

////
A block comment
spanning lines
////

After.
""",
"""
<span class="hljs-comment">// a line comment</span>
Text // not a comment

<span class="hljs-comment">////
A block comment
spanning lines
////</span>

After.
""");
    }

    [Fact]
    public void UnterminatedBlockComment()
    {
        AssertHighlighter("asciidoc",
"""
////
never closed
text
""",
"""
<span class="hljs-comment">////
never closed
text</span>
""");
    }

    [Fact]
    public void BlockTitles()
    {
        AssertHighlighter("asciidoc",
"""
.Optional Title
This paragraph has a title.

.A table
|===
| Col 1 | Col 2
| a | b
|===
""",
"""
<span class="hljs-title">.Optional Title</span>
This paragraph has a title.

<span class="hljs-title">.A table</span>
|===
| Col 1 | Col 2
| a | b
|===
""");
    }

    [Fact]
    public void Lists()
    {
        AssertHighlighter("asciidoc",
"""
* item one
* item two
** nested item
*** deeper

- dash item
-- nested dash

. ordered one
. ordered two
.. nested ordered

CPU:: The brain
RAM:: Memory
Term with spaces:: definition
""",
"""
<span class="hljs-bullet">* </span>item one
<span class="hljs-bullet">* </span>item two
<span class="hljs-bullet">** </span>nested item
<span class="hljs-bullet">*** </span>deeper

<span class="hljs-bullet">- </span>dash item
<span class="hljs-bullet">-- </span>nested dash

<span class="hljs-bullet">. </span>ordered one
<span class="hljs-bullet">. </span>ordered two
<span class="hljs-bullet">.. </span>nested ordered

<span class="hljs-bullet">CPU:: </span>The brain
<span class="hljs-bullet">RAM:: </span>Memory
<span class="hljs-bullet">Term with spaces:: </span>definition
""");
    }

    [Fact]
    public void Admonitions()
    {
        AssertHighlighter("asciidoc",
"""
NOTE: This is a note.

TIP: A tip.

IMPORTANT: Important.

WARNING: Careful!

CAUTION: Hot surface.

NOTE:no space
""",
"""
<span class="hljs-symbol">NOTE: </span>This is a note.

<span class="hljs-symbol">TIP: </span>A tip.

<span class="hljs-symbol">IMPORTANT: </span>Important.

<span class="hljs-symbol">WARNING: </span>Careful!

<span class="hljs-symbol">CAUTION: </span>Hot surface.

NOTE:no space
""");
    }

    [Fact]
    public void Strong()
    {
        AssertHighlighter("asciidoc",
"""
This is *bold* text and **un**constrained bold.
A *multi word bold* phrase.
*start of line bold*
x*not bold*y
Two *bold* and *more* here.
""",
"""
This is <span class="hljs-strong">*bold*</span> text and <span class="hljs-strong">**un**</span>constrained bold.
A <span class="hljs-strong">*multi word bold*</span> phrase.
<span class="hljs-strong">*start of line bold*</span>
x*not bold*y
Two <span class="hljs-strong">*bold*</span> and <span class="hljs-strong">*more*</span> here.
""");
    }

    [Fact]
    public void Strong_MultiLine()
    {
        AssertHighlighter("asciidoc",
"""
This is **bold
over lines** end.
And *constrained
multi line* too.
""",
"""
This is <span class="hljs-strong">**bold
over lines**</span> end.
And <span class="hljs-strong">*constrained
multi line*</span> too.
""");
    }

    [Fact]
    public void Emphasis()
    {
        AssertHighlighter("asciidoc",
"""
This is _italic_ text and __un__constrained.
snake_case_identifier stays plain.
A _multi word_ phrase.
The 'legacy emphasis' form.
Don't break apostrophes.
""",
"""
This is <span class="hljs-emphasis">_italic_</span> text and <span class="hljs-emphasis">__un__</span>constrained.
snake_case_identifier stays plain.
A <span class="hljs-emphasis">_multi word_</span> phrase.
The <span class="hljs-emphasis">&#x27;legacy emphasis&#x27;</span> form.
Don&#x27;t break apostrophes.
""");
    }

    [Fact]
    public void Emphasis_MultiLine()
    {
        AssertHighlighter("asciidoc",
"""
This is __emphasis
over lines__ end.
And _constrained
multi line_ too.
""",
"""
This is <span class="hljs-emphasis">__emphasis
over lines__</span> end.
And <span class="hljs-emphasis">_constrained
multi line_</span> too.
""");
    }

    [Fact]
    public void LegacyQuotedEmphasis_EndsAtBlankLine()
    {
        AssertHighlighter("asciidoc",
"""
Before 'unterminated emphasis
continues here

after the blank line.
""",
"""
Before <span class="hljs-emphasis">&#x27;unterminated emphasis
continues here

</span>after the blank line.
""");
    }

    [Fact]
    public void EscapedFormattingMarks()
    {
        AssertHighlighter("asciidoc",
"""
Escaped \*not bold\* and \_not italic\_ and \`not code\`.
Double escaped \\**not bold** here.
Double escaped \\__not em__ here.
Guard: a:*b* c;_d_ e}`f`
""",
"""
Escaped \*not bold\* and \_not italic\_ and \`not code\`.
Double escaped \\**not bold** here.
Double escaped \\__not em__ here.
Guard: a:*b* c;_d_ e}`f`
""");
    }

    [Fact]
    public void InlineCode()
    {
        AssertHighlighter("asciidoc",
"""
Use `npm install` to install.
Run +literal+ text.
A ``double backtick`` code.
A ``unterminated

next paragraph.
""",
"""
Use <span class="hljs-code">`npm install`</span> to install.
Run <span class="hljs-code">+literal+</span> text.
A <span class="hljs-code">``double backtick``</span> code.
A <span class="hljs-code">``unterminated

</span>next paragraph.
""");
    }

    [Fact]
    public void SmartQuotes()
    {
        AssertHighlighter("asciidoc",
"""
A ``smart quoted'' phrase and `single smart' one.
""",
"""
A <span class="hljs-string">``smart quoted&#x27;&#x27;</span> phrase and <span class="hljs-string">`single smart&#x27;</span> one.
""");
    }

    [Fact]
    public void IndentedLiteralLines()
    {
        AssertHighlighter("asciidoc",
"""
Paragraph.

  indented literal line
	tab indented line

Back.
""",
"""
Paragraph.

<span class="hljs-code">  indented literal line</span>
<span class="hljs-code">	tab indented line</span>

Back.
""");
    }

    [Fact]
    public void HorizontalRule()
    {
        AssertHighlighter("asciidoc",
"""
Before

'''

After
''''
""",
"""
Before

&#x27;&#x27;&#x27;

After
&#x27;&#x27;&#x27;&#x27;
""");
    }

    [Fact]
    public void LinksAndImages()
    {
        AssertHighlighter("asciidoc",
"""
See https://asciidoctor.org[Asciidoctor] for details.
Visit link:https://example.com/page.html[the page].
An image: image:logo.png[Logo] inline.
Block image:

image::diagram.png[Diagram, 300, 200]

A bare URL https://example.com here.
ftp://files.example.com[FTP] and irc://irc.freenode.org[IRC].
""",
"""
See <span class="hljs-link">https://asciidoctor.org</span>[<span class="hljs-string">Asciidoctor</span>] for details.
Visit link:<span class="hljs-link">https://example.com/page.html</span>[<span class="hljs-string">the page</span>].
An image: image:<span class="hljs-link">logo.png</span>[<span class="hljs-string">Logo</span>] inline.
Block image:

image::<span class="hljs-link">diagram.png</span>[<span class="hljs-string">Diagram, 300, 200</span>]

A bare URL https://example.com here.
<span class="hljs-link">ftp://files.example.com</span>[<span class="hljs-string">FTP</span>] and <span class="hljs-link">irc://irc.freenode.org</span>[<span class="hljs-string">IRC</span>].
""");
    }

    [Fact]
    public void PassthroughBlock_EmbedsXml()
    {
        AssertHighlighter("asciidoc",
"""
++++
<div class="custom">
  <p>Raw HTML</p>
</div>
++++
""",
"""
++++
<span class="language-xml"><span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;custom&quot;</span>&gt;</span></span>
  <span class="language-xml"><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span></span>Raw HTML<span class="language-xml"><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span></span>
<span class="language-xml"><span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span></span>
++++
""");
    }

    [Fact]
    public void ExampleAndSidebarBlocks()
    {
        AssertHighlighter("asciidoc",
"""
====
Example content *bold*
====

****
Sidebar content
****
""",
"""
====
Example content *bold*
====

****
Sidebar content
****
""");
    }

    [Fact]
    public void Table()
    {
        AssertHighlighter("asciidoc",
"""
[cols="1,2"]
|===
|Name |Description

|foo
|The *foo* thing
|===
""",
"""
<span class="hljs-meta">[cols=&quot;1,2&quot;]</span>
|===
|Name |Description

|foo
|The <span class="hljs-strong">*foo*</span> thing
|===
""");
    }

    [Fact]
    public void Directives()
    {
        AssertHighlighter("asciidoc",
"""
include::chapter1.adoc[]
include::{includedir}/snippet.adoc[tag=main]

ifdef::env-github[]
GitHub only.
endif::[]
""",
"""
include::chapter1.adoc[]
include::{includedir}/snippet.adoc[tag=main]

ifdef::env-github[]
GitHub only.
endif::[]
""");
    }

    [Fact]
    public void CrossReferencesAndAnchors()
    {
        AssertHighlighter("asciidoc",
"""
See <<section-id>> and <<other,Other Section>>.
xref:file.adoc#anchor[Link text]
[[anchor-id]]
[#custom-id.role]
A footnote.footnote:[The note.]
""",
"""
See &lt;&lt;section-id&gt;&gt; and &lt;&lt;other,Other Section&gt;&gt;.
xref:file.adoc#anchor[Link text]
<span class="hljs-meta">[[anchor-id]]</span>
<span class="hljs-meta">[#custom-id.role]</span>
A footnote.footnote:[The note.]
""");
    }

    [Fact]
    public void SourceBlockWithCallouts()
    {
        AssertHighlighter("asciidoc",
"""
[source,java]
----
public class Hello { // <1>
    public static void main(String[] args) {} // <2>
}
----
<1> Class declaration
<2> Main method
""",
"""
<span class="hljs-meta">[source,java]</span>
<span class="hljs-code">----
public class Hello { // &lt;1&gt;
    public static void main(String[] args) {} // &lt;2&gt;
}
----</span>
&lt;1&gt; Class declaration
&lt;2&gt; Main method
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("asciidoc",
"""
= Café résumé
Le *gras* et _l'italique_ — naïve.
* élément
""",
"""
<span class="hljs-section">= Café résumé</span>
Le <span class="hljs-strong">*gras*</span> et <span class="hljs-emphasis">_l&#x27;italique_</span> — naïve.
<span class="hljs-bullet">* </span>élément
""");
    }

    [Fact]
    public void ConstrainedMarksInsideWords()
    {
        AssertHighlighter("asciidoc",
"""
2*3 and
4*5 is math.
a_b and
c_d too.
""",
"""
2*3 and
4*5 is math.
a_b and
c_d too.
""");
    }

    [Fact]
    public void FormattingMarkCandidates()
    {
        AssertHighlighter("asciidoc",
"""
a *b c *d* e
x *a
y *b c* z
\*a *b* and \_c _d_
`a ``b'' c `d' e
see http://a[b http://c[d] e
_a
_b
_c_ d
*a *b *c
""",
"""
a <span class="hljs-strong">*b c *d*</span> e
x <span class="hljs-strong">*a
y *b c*</span> z
\*a <span class="hljs-strong">*b*</span> and \_c <span class="hljs-emphasis">_d_</span>
<span class="hljs-string">`a ``b&#x27;</span>&#x27; c <span class="hljs-string">`d&#x27;</span> e
see http://a[b <span class="hljs-link">http://c</span>[<span class="hljs-string">d</span>] e
<span class="hljs-emphasis">_a
_b
_c_</span> d
*a *b *c
""");
    }

    [Fact]
    public void Crlf()
    {
        AssertHighlighter("asciidoc", "= Title\r\n\r\n*bold* text\r\n", "<span class=\"hljs-section\">= Title</span>\r\n\r\n<span class=\"hljs-strong\">*bold*</span> text\r\n");
    }

    [Fact]
    public void RealWorld_AdocAlias()
    {
        AssertHighlighter("adoc",
"""
= Getting Started
:toc:
:sectnums:

== Installation

Install the package with:

[source,shell]
----
$ dotnet add package Meziantou.Framework
----

NOTE: Requires .NET 8 or later.

=== Configuration

. Open `appsettings.json`.
. Add the *Logging* section.
. Restart the app.

TIP: See https://learn.microsoft.com[the docs] for more.

== Usage

The `Parse` method returns a _result_ object.
""",
"""
<span class="hljs-section">= Getting Started</span>
<span class="hljs-meta">:toc:</span>
<span class="hljs-meta">:sectnums:</span>

<span class="hljs-section">== Installation</span>

Install the package with:

<span class="hljs-meta">[source,shell]</span>
<span class="hljs-code">----
$ dotnet add package Meziantou.Framework
----</span>

<span class="hljs-symbol">NOTE: </span>Requires .NET 8 or later.

<span class="hljs-section">=== Configuration</span>

<span class="hljs-bullet">. </span>Open <span class="hljs-code">`appsettings.json`</span>.
<span class="hljs-bullet">. </span>Add the <span class="hljs-strong">*Logging*</span> section.
<span class="hljs-bullet">. </span>Restart the app.

<span class="hljs-symbol">TIP: </span>See <span class="hljs-link">https://learn.microsoft.com</span>[<span class="hljs-string">the docs</span>] for more.

<span class="hljs-section">== Usage</span>

The <span class="hljs-code">`Parse`</span> method returns a <span class="hljs-emphasis">_result_</span> object.
""");
    }
}
