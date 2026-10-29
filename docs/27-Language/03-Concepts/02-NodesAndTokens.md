---
title: Nodes and tokens
description: The parts of a syntax tree, their kinds, the lists, how invalid text appears in a tree, and how to find a node.
---

## The parts of a tree

| Type | Is | Example in JSON |
| --- | --- | --- |
| `JsonSyntaxTree` (a `SyntaxTree`) | The text, the root, the diagnostics and the path of a document | — |
| `SyntaxNode` | A construct of the language, made of nodes and tokens. Each language has a class per construct | `JsonObjectSyntax`, `JsonMemberSyntax` |
| `SyntaxToken` | A word, a literal, or a piece of punctuation. A struct | `{`, `"name"`, `:`, `42` |
| `SyntaxTrivia` | Whitespace, a line break, or a comment, attached to a token. A struct | `// comment`, `\n` |

A tree gives its text with `GetText()`, its root with `GetRoot()`, and its problems with `GetDiagnostics()`. The root of
each language has its own type, such as `JsonDocumentSyntax`, `XmlDocumentSyntax` or `ShellScriptSyntax`.

A node has a property for each of its parts: `JsonMemberSyntax` has `NameToken`, `ColonToken` and `Value`, and
convenience properties such as `Name` that read them. A token has its `Text`, exactly as written, and `ValueText` and
`Value`, what the text denotes when the language decodes it, such as a number.

Every node, token and trivia knows its `Parent` and its position in the text.

## Kinds

Each language declares a `SyntaxKind` enum, with a value for every node, token and trivia. `Kind()` returns it, and
`IsKind(kind)` compares it:

<!-- ref:../../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=SyntaxKind_CompareKind;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = XmlSyntaxTree.ParseText("<root><item /><item>text</item></root>");

var elementCount = 0;
foreach (var node in tree.GetRoot().DescendantNodes())
{
    if (node.Kind() == SyntaxKind.XmlElement)
    {
        // handle element
        elementCount++;
    }
}

// <item /> is an XmlEmptyElement, so only <root> and the second <item> are counted
Assert.Equal(2, elementCount);
```
<!-- endref -->

The node types are usually enough: test `node is XmlElementSyntax`. The kinds tell apart tokens and trivia, which are
structs, and nodes that share a type, such as the three literals of `JsonLiteralSyntax` (`true`, `false` and `null`).

Every language names its enum `SyntaxKind` and its factory `SyntaxFactory`. In a file that uses two languages, give them
an alias, such as `using JsonKind = Meziantou.Framework.Language.Json.SyntaxKind;`.

`SyntaxFacts`, in each language namespace, answers questions about kinds and text, such as whether a kind is trivia or
the text of a punctuation kind.

## Lists

A node that holds several children of the same type exposes a list:

- `SyntaxList<T>` is a list of nodes, such as the `Values` of a JSON document.
- `SeparatedSyntaxList<T>` is a list of nodes with a separator between them, such as the members of a JSON object and
  their commas. The nodes and the separators share one sequence: `Count` counts the nodes and `SeparatorCount` the
  separators, so a trailing separator is a list where both are equal.
- `SyntaxTokenList` and `SyntaxTriviaList` are lists of tokens and of trivia.

<!-- ref:../../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_SeparatedList;format=md-fenced-code-block;dedent=true -->
```csharp
var elements = ((JsonArraySyntax)JsonSyntaxTree.ParseText("[1, 2,]").GetRoot().Value!).Elements;

Assert.Equal(2, elements.Count);
Assert.Equal(2, elements.SeparatorCount);
Assert.True(elements.HasTrailingSeparator);
Assert.IsType<JsonNumberSyntax>(elements[0]); // the element itself, not a wrapper
Assert.Equal(",", elements.GetSeparator(1).Text); // the trailing comma
```
<!-- endref -->

Indexing a separated list returns the node, not the separator: read the separators with `GetSeparator(index)` or
`GetSeparators()`.

## Invalid text in a tree

The tree of an invalid document still holds every character, and keeps the shape the author most likely meant:

- A **missing token** is a token that should be there and is not, such as the `]` of `[1, 2`. It has no text and an
  empty span, and `IsMissing` is `true`.
- **Skipped text** is text the parser could not use. It is kept in a node of its own, such as `JsonSkippedTextSyntax`,
  or as trivia of the next token, depending on where it is.

`ContainsDiagnostics` and `ContainsSkippedText` say whether a node, or anything below it, has a diagnostic or skipped
text. Check them before you trust a value read from a document that has errors.

## Find a node

To go down the tree:

- `ChildNodes()`, `ChildTokens()` and `ChildNodesAndTokens()` return the direct children.
- `DescendantNodes()`, `DescendantTokens()`, `DescendantTrivia()` and `DescendantNodesAndTokens()` return everything
  below a node, in the order of the text. They take an optional filter, which says whether to go into the children of
  a node, and an optional span, which keeps only what intersects it.

<!-- ref:../../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=XmlSyntaxNode_DescendantNodes;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = XmlSyntaxTree.ParseText("<root><item /></root>");

var kinds = new List<SyntaxKind>();
foreach (var node in tree.GetRoot().DescendantNodes())
{
    kinds.Add(node.Kind());
}

Assert.Equal([SyntaxKind.XmlElement, SyntaxKind.XmlElementStartTag, SyntaxKind.XmlEmptyElement, SyntaxKind.XmlElementEndTag], kinds);
```
<!-- endref -->

To go up, `Ancestors()`, `AncestorsAndSelf()` and `FirstAncestorOrSelf<T>()`. To go sideways, `GetFirstToken()`,
`GetLastToken()`, and `GetNextToken()` and `GetPreviousToken()` on a token.

To find what is at a position, such as the caret of an editor, use `FindToken(position)`, `FindTrivia(position)`, or
`FindNode(span)`, which returns the node that covers a span:

<!-- ref:../../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_FindToken;format=md-fenced-code-block;dedent=true -->
```csharp
var text = """{ "name": "app", "port": 8080 }""";
var root = JsonSyntaxTree.ParseText(text).GetRoot();

// The position of the caret in an editor, here on 8080
var token = root.FindToken(text.IndexOf("8080", StringComparison.Ordinal));
Assert.Equal(SyntaxKind.NumberToken, token.Kind());

var member = token.Parent?.FirstAncestorOrSelf<JsonMemberSyntax>();
Assert.Equal("port", member?.Name);
```
<!-- endref -->

Some languages add their own ways to find a node: XPath for [XML](../05-Xml.md#find-nodes-with-xpath), lookups by
section and key for [INI](../06-Ini.md#read-values) and [TOML](../07-Toml.md#read-values-by-key), and JSONPath for
[JSON](../04-Json.md#query-with-jsonpath).
