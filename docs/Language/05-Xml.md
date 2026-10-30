---
title: XML
description: Parse XML into a lossless tree, find nodes with XPath, edit attributes and elements, format a document, and read its diagnostics.
---

`Meziantou.Framework.Language.Xml` parses XML into a tree that keeps every character, attribute quotes included, so you
can change a version in a `.csproj` or a `.nuspec` and keep the rest of the file as it is. It finds nodes with XPath, and
formats a document on request.

```bash
dotnet add package Meziantou.Framework.Language.Xml
```

## Parse, find and edit

`XmlSyntaxTree.ParseText` reads a string. `ReplaceNode` with an XPath expression finds a node and replaces it with what
your function returns:

<!-- ref:../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=XmlSyntaxTree_Overview;format=md-fenced-code-block;dedent=true -->
```csharp
const string Xml = "<root><item version='1.0.0' /></root>";

var tree = XmlSyntaxTree.ParseText(Xml);

// The text comes back exactly as it went in.
Assert.Equal(tree.GetText().Text, tree.GetRoot().ToFullString());

// Find a node with XPath, and edit it without touching anything else.
var updated = tree.GetRoot().ReplaceNode("//item/@version", node => ((XmlAttributeSyntax)node).WithValue("2.0.0"));

Assert.Equal("<root><item version='2.0.0' /></root>", updated.ToFullString());
```
<!-- endref -->

`ReplaceNode` throws an `InvalidOperationException` when the expression matches nothing.

## The shape of a tree

An `XmlDocumentSyntax` holds its top-level nodes in `Nodes`. A node of a document or of the content of an element is one
of:

| Node | Is |
| --- | --- |
| `XmlElementSyntax` | `<item>…</item>`: `StartTag`, `Content`, `EndTag` |
| `XmlEmptyElementSyntax` | `<item />`: attributes, but no content and no end tag |
| `XmlTextSyntax` | Character data, whitespace between tags included |
| `XmlCommentSyntax`, `XmlCDataSectionSyntax`, `XmlProcessingInstructionSyntax` | `<!-- -->`, `<![CDATA[ ]]>`, `<?target data?>` |
| `XmlDeclarationSyntax`, `XmlDocumentTypeSyntax` | `<?xml … ?>`, `<!DOCTYPE …>` |
| `XmlSkippedTextSyntax` | Text the parser could not use |

`<item/>` and `<item></item>` are different node types. Both have `Name`, `Attributes` and `GetAttribute(name)`. Every
character is a token, including `<`, `/>`, `=` and the quotes of an attribute value:

<!-- ref:../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=XmlSyntaxTree_Tokens;format=md-fenced-code-block;dedent=true -->
```csharp
var attribute = (XmlAttributeSyntax)tree.GetRoot().SelectSingleSyntaxNode("//item/@version")!;

Assert.Equal("version", attribute.NameToken.Text);
Assert.Equal("'", attribute.StartQuoteToken.Text);
Assert.Equal("1.0.0", attribute.ValueToken.Text);
Assert.Equal("1.0.0", attribute.Value);
```
<!-- endref -->

## Whitespace and text

Inside a tag, whitespace is trivia. Between tags, it is text: an `XmlTextSyntax` in the content of the element, because
it is character data an application may read:

<!-- ref:../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=XmlSyntaxTree_Trivia;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = XmlSyntaxTree.ParseText("<root>\n  <book id = '1' />\n</root>");
var book = (XmlEmptyElementSyntax)tree.GetRoot().SelectSingleSyntaxNode("//book")!;

Assert.Equal(" ", book.Attributes[0].NameToken.LeadingTrivia.ToFullString());
Assert.Equal(" ", book.Attributes[0].EqualsToken.LeadingTrivia.ToFullString());

var root = (XmlElementSyntax)tree.GetRoot().Nodes[0];
Assert.Equal("\n  ", ((XmlTextSyntax)root.Content[0]).Text);
```
<!-- endref -->

## Values

The tree keeps the text as written, references included. `XmlAttributeSyntax.Value` and `XmlTextSyntax.Value` are what
an XML processor reads: character references and the five predefined entities are resolved, line breaks become `\n`, and
an attribute value turns each tab and line break into a space. Other entities are left as written, since resolving them
means reading the DTD.

<!-- ref:../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=XmlSyntaxTree_Values;format=md-fenced-code-block;dedent=true -->
```csharp
var root = XmlSyntaxTree.ParseText("<note title='Tom &amp; Jerry'>1 &lt; 2</note>").GetRoot();
var note = (XmlElementSyntax)root.Nodes[0];
var title = note.GetAttribute("title")!;

// The text as written, and the value an XML processor reads
Assert.Equal("Tom &amp; Jerry", title.ValueToken.Text);
Assert.Equal("Tom & Jerry", title.Value);
Assert.Equal("1 < 2", ((XmlTextSyntax)note.Content[0]).Value);

// WithValue escapes what it has to
var updated = root.ReplaceNode(title, title.WithValue("Tom & Jerry's"));
Assert.Equal("<note title='Tom &amp; Jerry&apos;s'>1 &lt; 2</note>", updated.ToFullString());
```
<!-- endref -->

`WithValue` escapes what XML requires, and writes a tab or a line break as a character reference, so the value it sets
is the value read back.

## Find nodes with XPath

`SelectSyntaxNodes` and `SelectSingleSyntaxNode` return the nodes an XPath expression selects:

<!-- ref:../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=XmlSyntaxTree_SelectSyntaxNodes;format=md-fenced-code-block;dedent=true -->
```csharp
var root = XmlSyntaxTree.ParseText("""
    <Project>
      <ItemGroup>
        <PackageReference Include="Meziantou.Framework.Language.Xml" Version="4.0.0" />
        <PackageReference Include="Meziantou.Framework.Language.Json" Version="4.0.0" />
      </ItemGroup>
    </Project>
    """).GetRoot();

var packages = new List<string>();
foreach (var node in root.SelectSyntaxNodes("//PackageReference/@Include"))
{
    packages.Add(((XmlAttributeSyntax)node).Value);
}

Assert.Equal(["Meziantou.Framework.Language.Xml", "Meziantou.Framework.Language.Json"], packages);
```
<!-- endref -->

Pass an `XmlNamespaceManager` to use prefixes:

<!-- ref:../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=XmlSyntaxTree_XPathWithNamespaces;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = XmlSyntaxTree.ParseText("<root xmlns:pkg='urn:test'><pkg:item pkg:version='1.0.0' /></root>");
var namespaces = new XmlNamespaceManager(new NameTable());
namespaces.AddNamespace("pkg", "urn:test");

var item = tree.GetRoot().SelectSingleSyntaxNode("//pkg:item", namespaces);
var version = tree.GetRoot().SelectSingleSyntaxNode("//pkg:item/@pkg:version", namespaces);

Assert.IsType<XmlEmptyElementSyntax>(item);
Assert.Equal("1.0.0", ((XmlAttributeSyntax)version!).Value);
```
<!-- endref -->

`SelectNodes` and `SelectSingleNode` return `XPathNavigator`s instead, and `CreateNavigator()` returns a navigator over
a node. An `XmlDocumentSyntax` is an `IXPathNavigable`, so it can be passed to the .NET APIs that take one.

## Edit a document

Every node has a `With…` method for each of its parts: `XmlAttributeSyntax.WithValue` and `WithName`,
`XmlElementSyntax.WithInnerText`, `WithName` and `WithContent`, and so on. `SyntaxFactory` builds new nodes:

<!-- ref:../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=SyntaxFactory_XmlDocument;format=md-fenced-code-block;dedent=true -->
```csharp
var node = SyntaxFactory.XmlEmptyElement("package", [SyntaxFactory.XmlAttribute("version", "1.0.0")]);
var document = SyntaxFactory.XmlDocument(node);
Assert.Equal("<package version=\"1.0.0\" />", document.ToFullString());
```
<!-- endref -->

A replacement does not take the trivia of the node it replaces: use `WithTriviaFrom` to keep it. See
[Editing trees](03-Concepts/06-EditingTrees.md).

## Format a document

`Formatter.Format` lays a document out again: each element whose content is only elements, comments and processing
instructions gets each child on its own line, indented one level deeper. It is the only API of these packages that
reformats, and it never changes text: an element that contains text or CDATA, and the elements under
`xml:space="preserve"`, are kept as they are.

<!-- ref:../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=Formatter_Format;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = XmlSyntaxTree.ParseText("<root><item/></root>");
var formatted = Formatter.Format(tree);
Assert.Equal("<root>\n  <item />\n</root>", formatted.ToFullString());
```
<!-- endref -->

`XmlFormattingOptions` sets the layout:

<!-- ref:../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=XmlFormattingOptions_Format;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = XmlSyntaxTree.ParseText("<root><item id=\"1\"/></root>");
var options = new XmlFormattingOptions { IndentationSize = 4, NewLine = "\r\n", SpaceAroundEquals = true };
var formatted = Formatter.Format(tree.GetRoot(), options);
Assert.Equal("<root>\r\n    <item id = \"1\" />\r\n</root>", formatted.ToFullString());
```
<!-- endref -->

| Option | Default | Sets |
| --- | --- | --- |
| `IndentationSize` | 2 | The number of spaces per level |
| `UseTabs` | `false` | Whether each level is a tab |
| `NewLine` | `"\n"` | The line break between the nodes the formatter puts on their own line |
| `PreserveSingleLineElements` | `false` | Whether an element written on one line is kept as it is |
| `AttributeWrapping` | `false` | Whether each attribute of an element that has more than one goes on its own line |
| `SpaceAroundEquals` | `false` | Whether `=` has a space on both sides |
| `SpaceBeforeSelfClosingSlash` | `true` | Whether a self-closing tag ends with ` />` rather than `/>` |

`XmlDocumentSyntax.NormalizeWhitespace()` formats a document with the default options.

## Diagnostics

The checks are those of a non-validating XML processor: the well-formedness constraints of XML 1.0 (Fifth Edition) and of
Namespaces in XML 1.0. Any `1.x` version is accepted. The parser does not validate a document against a DTD or a schema.

<!-- ref:../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=XmlSyntaxTree_Diagnostics;format=md-fenced-code-block;dedent=true -->
```csharp
var messages = new List<string>();
foreach (var diagnostic in XmlSyntaxTree.ParseText("<root>\n  <item>\n</root>").GetDiagnostics())
{
    var line = diagnostic.Location.GetLineSpan().Start;
    messages.Add($"{diagnostic.Id} ({line.Line},{line.Character}): {diagnostic.Message}");
}

Assert.Equal(["XML0001 (1,2): Missing end tag for 'item'."], messages);
```
<!-- endref -->

| Id | Reported for |
| --- | --- |
| `XML0001` | An element with no end tag |
| `XML0002` | An unexpected, mismatched, or malformed end tag |
| `XML0003` | A character XML does not allow, such as U+0001, U+FFFE, or a lone surrogate |
| `XML0004` | A malformed or invalid character reference, an `&` that starts no reference, or an undeclared entity |
| `XML0005` | A `<` in an attribute value, or `]]>` in character data |
| `XML0006` | A duplicate attribute, including two that differ only by a prefix bound to the same namespace |
| `XML0007` | An unterminated XML declaration |
| `XML0008` | An unterminated comment |
| `XML0009` | An unterminated CDATA section |
| `XML0010` | An invalid start tag or attribute: text a tag cannot use, a missing `>`, a missing or unquoted value, or attributes not separated by whitespace |
| `XML0011` | An unterminated document type declaration |
| `XML0012` | An unterminated processing instruction |
| `XML0013` | An invalid or misplaced XML declaration |
| `XML0014` | A document that does not have exactly one root element, or holds text, a CDATA section, or a document type declaration where it cannot |
| `XML0015` | A comment containing `--` |
| `XML0016` | A processing instruction with no target, the reserved target `xml`, or no whitespace after its target |
| `XML0017` | A namespace error: an undeclared prefix, a name that is not a qualified name, or a reserved prefix misused |
| `XML0018` | A malformed document type declaration |

An entity reference counts as declared when the internal subset declares it, or when the document has an external subset
or a parameter entity reference, which could declare it where the parser cannot see.

### Mistakes

The parser keeps the shape the author most likely meant, so one mistake gives one diagnostic:

- An end tag that matches an element further up closes the elements left open in between, each reported as missing its
  end tag. An end tag that matches no open element is kept as `XmlSkippedTextSyntax`.
- A start tag without its `>` still opens its element, with a missing `GreaterThanToken`.
- Text a tag cannot use, as in `<a @ b="1">`, is kept as skipped text trivia, and the rest of the attributes are read as
  usual.
- An attribute value that meets what looks like the next tag before its closing quote stops there.
- A `<` that starts no markup, as in `a < b`, is skipped on its own.
- An unterminated XML declaration, document type declaration, or end tag stops at the next `<`.

## Walk a tree

`XmlSyntaxWalker`, `XmlSyntaxVisitor` and `XmlSyntaxRewriter` have a method per node type, such as `VisitElement` and
`VisitAttribute`. `VisitElement` is not called for an `XmlEmptyElementSyntax`, which has `VisitEmptyElement`:

<!-- ref:../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=XmlSyntaxVisitor_ElementCounter;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class ElementCounter : XmlSyntaxWalker
{
    public int Count { get; private set; }

    public override void VisitElement(XmlElementSyntax node)
    {
        Count++;
        base.VisitElement(node);
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=XmlSyntaxTree_WalkerUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var counter = new ElementCounter();
counter.Visit(XmlSyntaxTree.ParseText("<root><a><b></b></a><c /></root>").GetRoot());

// <c /> is an XmlEmptyElementSyntax, so VisitElement does not count it
Assert.Equal(3, counter.Count);
```
<!-- endref -->
