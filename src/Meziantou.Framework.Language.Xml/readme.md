# Meziantou.Framework.Language.Xml

An immutable XML syntax tree that keeps every character of the text it was parsed from — comments, whitespace,
attribute quoting and all — and lets you edit it without reformatting anything you did not touch.

It is modelled on Roslyn. If you have used `Microsoft.CodeAnalysis`, everything here will look familiar: a
`SyntaxTree` over a `SourceText`, nodes and tokens with spans, trivia, `SyntaxKind`, visitors, rewriters, and
annotations. On top of that it speaks XPath, so a node can be found the way XML people expect to find one.

Parsing never throws and never gives up. Whatever the text says, the tree reproduces it exactly, and anything wrong
with it is reported through `GetDiagnostics()`.

```csharp
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Xml;

const string Xml = "<root><item version='1.0.0' /></root>";

var tree = XmlSyntaxTree.ParseText(Xml);

// The text comes back exactly as it went in.
Console.WriteLine(tree.GetRoot().ToFullString() == tree.GetText().Text); // True

// Find a node with XPath, and edit it without touching anything else.
var updated = tree.GetRoot().ReplaceNode("//item/@version", node => ((XmlAttributeSyntax)node).WithValue("2.0.0"));

Console.WriteLine(updated.ToFullString()); // <root><item version='2.0.0' /></root>
```

## The shape of a tree

```
XmlDocumentSyntax
├── Nodes : SyntaxList<XmlNodeSyntax>
│   └── XmlElementSyntax
│       ├── StartTag : XmlElementStartTagSyntax  →  LessThanToken, NameToken, Attributes, GreaterThanToken
│       ├── Content  : SyntaxList<XmlNodeSyntax>
│       └── EndTag   : XmlElementEndTagSyntax?   →  LessThanSlashToken, NameToken, GreaterThanToken
└── EndOfFileToken
```

A node in a document or in an element's content is an `XmlElementSyntax`, `XmlEmptyElementSyntax`, `XmlTextSyntax`,
`XmlCommentSyntax`, `XmlCDataSectionSyntax`, `XmlDeclarationSyntax`, `XmlProcessingInstructionSyntax`,
`XmlDocumentTypeSyntax`, or `XmlSkippedTextSyntax` for text the parser could not use.

**An element written as one self-closing tag is its own node type.** `<item/>` is an `XmlEmptyElementSyntax`, which
has attributes but no content and no end tag; `<item></item>` is an `XmlElementSyntax`. Both carry `Name`,
`Attributes`, and `GetAttribute(name)`.

**Everything is a token, including the punctuation.** `<`, `>`, `/>`, `=`, and the quotes around an attribute value
are each a token of their own, so an edit rebuilds the tree rather than splicing text:

```csharp
var attribute = (XmlAttributeSyntax)tree.GetRoot().SelectSingleSyntaxNode("//item/@version")!;

attribute.NameToken.Text;       // version
attribute.StartQuoteToken.Text; // '
attribute.ValueToken.Text;      // 1.0.0
attribute.Value;                // 1.0.0
```

## Which whitespace is trivia

**Inside a tag, whitespace is trivia; between tags it is content.** That is what XML says: the space in
`<book id = '1'>` carries no meaning, while the newline between two elements is character data an application may
care about.

```csharp
var tree = XmlSyntaxTree.ParseText("<root>\n  <book id = '1' />\n</root>");
var book = (XmlEmptyElementSyntax)tree.GetRoot().SelectSingleSyntaxNode("//book")!;

book.Attributes[0].NameToken.LeadingTrivia.ToFullString();   // " "
book.Attributes[0].EqualsToken.LeadingTrivia.ToFullString(); // " "

var root = (XmlElementSyntax)tree.GetRoot().Nodes[0];
((XmlTextSyntax)root.Content[0]).Text;                       // "\n  "
```

## Editing without reformatting

An edit rebuilds only the path from the changed node up to the root. Everything else is carried over as-is, so the
rest of the document is untouched — and the result is the same type you started with, so no cast is needed.

```csharp
var tree = XmlSyntaxTree.ParseText("<root><a>1</a><b>2</b></root>");
var root = (XmlElementSyntax)tree.GetRoot().Nodes[0];
var b = root.Content.OfType<XmlElementSyntax>().Single(element => element.Name == "b");

XmlDocumentSyntax updated = tree.GetRoot().ReplaceNode(b, b.WithInnerText("two"));

Console.WriteLine(updated.ToFullString()); // <root><a>1</a><b>two</b></root>

// <a> was not touched, so it is the very same node.
Console.WriteLine(root.Content[0].IsIncrementallyIdenticalTo(((XmlElementSyntax)updated.Nodes[0]).Content[0])); // True
```

There is a `WithX` method for every slot of every node, `SyntaxFactory` to build nodes from scratch, and
`RemoveNode` with `SyntaxRemoveOptions` to take one out.

Nothing is carried over onto a replacement, including the trivia in front of the node being replaced. Ask for it with
`WithTriviaFrom` when you want it.

## Finding a node

With XPath, including namespaces:

```csharp
var tree = XmlSyntaxTree.ParseText("<root xmlns:pkg='urn:test'><pkg:item pkg:version='1.0.0' /></root>");
var namespaces = new XmlNamespaceManager(new NameTable());
namespaces.AddNamespace("pkg", "urn:test");

var item = tree.GetRoot().SelectSingleSyntaxNode("//pkg:item", namespaces);
var version = tree.GetRoot().SelectSingleSyntaxNode("//pkg:item/@pkg:version", namespaces);
```

`SelectNodes` returns `XPathNavigator`s, `SelectSyntaxNodes` returns the syntax nodes behind them, and
`CreateNavigator()` gives an `XPathNavigator` over the whole document. Or, without XPath:
`root.DescendantNodes()`, `DescendantTokens()`, `DescendantTrivia()`, `FindToken(position)`, `FindNode(span)`.

## Finding a node again after an edit

An annotation is a marker you attach to a node and find again in the tree an edit produced, wherever it ended up:

```csharp
var marker = new SyntaxAnnotation();
var marked = root.ReplaceNode(a, a.WithAdditionalAnnotations(marker));
var edited = marked.ReplaceNode(/* something else entirely */);

var found = edited.GetAnnotatedNodes(marker).Single();
```

## Diagnostics

Nothing is thrown for bad input; it is reported instead, with a location you can turn into line and character
positions.

```csharp
foreach (var diagnostic in XmlSyntaxTree.ParseText("<root>\n  <item>\n</root>").GetDiagnostics())
{
    var line = diagnostic.Location.GetLineSpan().Start;
    Console.WriteLine($"{diagnostic.Id} ({line.Line},{line.Character}): {diagnostic.Message}");
}
```

| Id | Reported for |
| --- | --- |
| `XML0001` | An element with no end tag |
| `XML0002` | An unexpected or mismatched end tag |
| `XML0007` | An unterminated XML declaration |
| `XML0008` | An unterminated comment |
| `XML0009` | An unterminated CDATA section |
| `XML0010` | An invalid start tag or attribute |
| `XML0011` | An unterminated document type declaration |
| `XML0012` | An unterminated processing instruction |

## Walking a tree

`XmlSyntaxWalker` visits a node and everything below it; `XmlSyntaxVisitor` dispatches on kind without recursing;
`XmlSyntaxRewriter` builds a new tree from an old one.

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

## Formatting

`Formatter.Format` lays a document out again, with `XmlFormattingOptions` controlling indentation and line breaks.
It is the one thing here that does reformat, and it is opt-in.

## Editing by text

```csharp
var tree = XmlSyntaxTree.ParseText("<root><a /></root>");
var updated = tree.WithChanges(new TextChange(new TextSpan(6, 5), "<b />"));

Console.WriteLine(updated.GetRoot().ToFullString()); // <root><b /></root>
```

## Coming from version 3

Version 4 moved the tree onto the same model Roslyn uses, which changed most of the API.

| Version 3 | Version 4 |
| --- | --- |
| `XmlSyntaxKind` | `SyntaxKind` |
| `node.Kind` | `node.Kind()` |
| `XmlSyntaxToken`, `XmlSyntaxTrivia`, `XmlSyntaxNodeOrToken` (classes) | `SyntaxToken`, `SyntaxTrivia`, `SyntaxNodeOrToken` from `Meziantou.Framework.Language` (structs) |
| `XmlSyntaxAnnotation` | `SyntaxAnnotation` from `Meziantou.Framework.Language` |
| `tree.Root`, `tree.Text`, `tree.SourceText`, `tree.Diagnostics` | `tree.GetRoot()`, `tree.GetText()`, `tree.GetDiagnostics()` |
| `document.ChildNodes` | `document.Nodes` |
| `element.IsSelfClosing` | a self-closing element is an `XmlEmptyElementSyntax` |
| `XmlEndTagSyntax` | `XmlElementEndTagSyntax`, reached through `element.EndTag` |
| `element.Tokens`, `node.Tokens` | a named property per token, or `ChildTokens()` |
| `element.StartTagText` | `element.StartTag`, a node of its own |
| `new XmlTextSyntax(text)` and the other constructors | `SyntaxFactory.XmlText(text)` and friends |
| `SyntaxFactory.Element/Attribute/Text/Comment/…` | `SyntaxFactory.XmlElement/XmlAttribute/XmlText/XmlComment/…` |
| `SyntaxFactory.Element(name, attributes, content, isSelfClosing)` | `XmlElement(name, attributes, content)` or `XmlEmptyElement(name, attributes)` |
| `token.WithText(text)` | `SyntaxFactory.Token(kind, text).WithTriviaFrom(token)` |
| `node.WithLeadingTrivia(...)` doing string surgery | the shared `WithLeadingTrivia`, which sets the first token's trivia |
| `root.ReplaceNode(...)` re-parsing the whole document | `root.ReplaceNode(...)` rebuilding only what changed |
| `XmlSyntaxRewriter` overrides returning `XmlSyntaxNode?` | returning `SyntaxNode?` |
| `XmlSyntaxVisitor.DefaultVisit` recursing | it does nothing; derive from `XmlSyntaxWalker` to walk a tree |

New in version 4: every character is a token, so `Span` and `FullSpan` differ where a token carries trivia;
`WithX` on every slot; `SyntaxAnnotation`; `RemoveNode`/`SyntaxRemoveOptions`; `XmlSyntaxWalker`; `FindToken`/
`FindNode`; and structural sharing — an edit keeps every node it did not touch.

## Notes

Replacing a node, token or trivium rebuilds only the spine from it to the root; `WithChanges` reparses, because an
edit expressed as text can change how everything after it reads.

Nodes are shared between the trees an edit produces, so holding several versions of a document costs little more
than holding one.

This is a parser, not a validator. It reads the shape of a document without resolving entities or applying a schema.
Names do follow XML's own `NameStartChar` and `NameChar` productions, so a name may hold a combining mark, a middle
dot, or a character from outside the basic plane, and a Unicode letter XML leaves out — `ª`, say — does not start one.
