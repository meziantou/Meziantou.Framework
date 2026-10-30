---
title: Nodes and tokens
description: The parts of a syntax tree, their kinds, the lists, how invalid text appears in a tree, and how to find a node.
---

## The parts of a tree

| Type | Is | Example in JSON |
| --- | --- | --- |
| [JsonSyntaxTree](cref:Meziantou.Framework.Language.Json.JsonSyntaxTree) (a [SyntaxTree](cref:Meziantou.Framework.Language.SyntaxTree)) | The text, the root, the diagnostics and the path of a document | — |
| [SyntaxNode](cref:Meziantou.Framework.Language.SyntaxNode) | A construct of the language, made of nodes and tokens. Each language has a class per construct | [JsonObjectSyntax](cref:Meziantou.Framework.Language.Json.JsonObjectSyntax), [JsonMemberSyntax](cref:Meziantou.Framework.Language.Json.JsonMemberSyntax) |
| [SyntaxToken](cref:Meziantou.Framework.Language.SyntaxToken) | A word, a literal, or a piece of punctuation. A struct | `{`, `"name"`, `:`, `42` |
| [SyntaxTrivia](cref:Meziantou.Framework.Language.SyntaxTrivia) | Whitespace, a line break, or a comment, attached to a token. A struct | `// comment`, `\n` |

A tree gives its text with [GetText()](cref:Meziantou.Framework.Language.SyntaxTree.GetText), its root with [GetRoot()](cref:Meziantou.Framework.Language.SyntaxTree.GetRoot), and its problems with [GetDiagnostics()](cref:Meziantou.Framework.Language.SyntaxTree.GetDiagnostics). The root of
each language has its own type, such as [JsonDocumentSyntax](cref:Meziantou.Framework.Language.Json.JsonDocumentSyntax), [XmlDocumentSyntax](cref:Meziantou.Framework.Language.Xml.XmlDocumentSyntax) or [ShellScriptSyntax](cref:Meziantou.Framework.Language.Shell.ShellScriptSyntax).

A node has a property for each of its parts: [JsonMemberSyntax](cref:Meziantou.Framework.Language.Json.JsonMemberSyntax) has [NameToken](cref:Meziantou.Framework.Language.Json.JsonMemberSyntax.NameToken), [ColonToken](cref:Meziantou.Framework.Language.Json.JsonMemberSyntax.ColonToken) and `Value`, and
convenience properties such as [Name](cref:Meziantou.Framework.Language.Json.JsonMemberSyntax.Name) that read them. A token has its [Text](cref:Meziantou.Framework.Language.SyntaxToken.Text), exactly as written, and [ValueText](cref:Meziantou.Framework.Language.SyntaxToken.ValueText) and
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
structs, and nodes that share a type, such as the three literals of [JsonLiteralSyntax](cref:Meziantou.Framework.Language.Json.JsonLiteralSyntax) (`true`, `false` and `null`).

Every language names its enum `SyntaxKind` and its factory `SyntaxFactory`. In a file that uses two languages, give them
an alias, such as `using JsonKind = Meziantou.Framework.Language.Json.SyntaxKind;`.

`SyntaxFacts`, in each language namespace, answers questions about kinds and text, such as whether a kind is trivia or
the text of a punctuation kind.

## Lists

A node that holds several children of the same type exposes a list:

- [SyntaxList\<T\>](cref:Meziantou.Framework.Language.SyntaxList{TNode}) is a list of nodes, such as the [Values](cref:Meziantou.Framework.Language.Json.JsonDocumentSyntax.Values) of a JSON document.
- [SeparatedSyntaxList\<T\>](cref:Meziantou.Framework.Language.SeparatedSyntaxList{TNode}) is a list of nodes with a separator between them, such as the members of a JSON object and
  their commas. The nodes and the separators share one sequence: [Count](cref:Meziantou.Framework.Language.SeparatedSyntaxList{TNode}.Count) counts the nodes and [SeparatorCount](cref:Meziantou.Framework.Language.SeparatedSyntaxList{TNode}.SeparatorCount) the
  separators, so a trailing separator is a list where both are equal.
- [SyntaxTokenList](cref:Meziantou.Framework.Language.SyntaxTokenList) and [SyntaxTriviaList](cref:Meziantou.Framework.Language.SyntaxTriviaList) are lists of tokens and of trivia.

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
[GetSeparators()](cref:Meziantou.Framework.Language.SeparatedSyntaxList{TNode}.GetSeparators).

## Invalid text in a tree

The tree of an invalid document still holds every character, and keeps the shape the author most likely meant:

- A **missing token** is a token that should be there and is not, such as the `]` of `[1, 2`. It has no text and an
  empty span, and [IsMissing](cref:Meziantou.Framework.Language.SyntaxToken.IsMissing) is `true`.
- **Skipped text** is text the parser could not use. It is kept in a node of its own, such as [JsonSkippedTextSyntax](cref:Meziantou.Framework.Language.Json.JsonSkippedTextSyntax),
  or as trivia of the next token, depending on where it is.

[ContainsDiagnostics](cref:Meziantou.Framework.Language.SyntaxNode.ContainsDiagnostics) and [ContainsSkippedText](cref:Meziantou.Framework.Language.SyntaxNode.ContainsSkippedText) say whether a node, or anything below it, has a diagnostic or skipped
text. Check them before you trust a value read from a document that has errors.

## Find a node

To go down the tree:

- [ChildNodes()](cref:Meziantou.Framework.Language.SyntaxNode.ChildNodes), [ChildTokens()](cref:Meziantou.Framework.Language.SyntaxNode.ChildTokens) and [ChildNodesAndTokens()](cref:Meziantou.Framework.Language.SyntaxNode.ChildNodesAndTokens) return the direct children.
- [DescendantNodes()](cref:Meziantou.Framework.Language.SyntaxNode.DescendantNodes), [DescendantTokens()](cref:Meziantou.Framework.Language.SyntaxNode.DescendantTokens), [DescendantTrivia()](cref:Meziantou.Framework.Language.SyntaxNode.DescendantTrivia) and [DescendantNodesAndTokens()](cref:Meziantou.Framework.Language.SyntaxNode.DescendantNodesAndTokens) return everything
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

To go up, [Ancestors()](cref:Meziantou.Framework.Language.SyntaxNode.Ancestors), [AncestorsAndSelf()](cref:Meziantou.Framework.Language.SyntaxNode.AncestorsAndSelf) and [FirstAncestorOrSelf\<T\>()](cref:Meziantou.Framework.Language.SyntaxNode.FirstAncestorOrSelf). To go sideways, [GetFirstToken()](cref:Meziantou.Framework.Language.SyntaxNode.GetFirstToken),
[GetLastToken()](cref:Meziantou.Framework.Language.SyntaxNode.GetLastToken), and [GetNextToken()](cref:Meziantou.Framework.Language.SyntaxToken.GetNextToken) and [GetPreviousToken()](cref:Meziantou.Framework.Language.SyntaxToken.GetPreviousToken) on a token.

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
