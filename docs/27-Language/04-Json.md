---
title: JSON
description: Parse JSON with comments and trailing commas into a lossless tree, query it with JSONPath, edit it, and read its diagnostics.
---

`Meziantou.Framework.Language.Json` parses JSON into a tree that keeps every character, so you can change a value of a
`package.json` or an `appsettings.json` and keep its comments, its indentation and its key order.

```bash
dotnet add package Meziantou.Framework.Language.Json
```

## Parse a document

`JsonSyntaxTree.ParseText` reads a string. Comments and trailing commas are accepted without a diagnostic:

<!-- ref:../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_Overview;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = JsonSyntaxTree.ParseText("""
{
  // the name of the thing
  "name": "value",
  "items": [1, 2,],
}
""");

// Comments and trailing commas are accepted, so there is nothing to report here.
Assert.Empty(tree.GetDiagnostics());

// And the text comes back exactly as it went in.
Assert.Equal(tree.GetText().Text, tree.GetRoot().ToFullString());
```
<!-- endref -->

## The shape of a tree

A `JsonDocumentSyntax` holds the root value in `Value`. A document with several values, which is an error, keeps them
all in `Values`.

| Node | Is | Holds |
| --- | --- | --- |
| `JsonObjectSyntax` | `{ … }` | `Members`, a separated list of `JsonMemberSyntax`, and `GetMember(name)` |
| `JsonMemberSyntax` | `"name": value` | `NameToken`, `Name`, `ColonToken`, `Value` |
| `JsonArraySyntax` | `[ … ]` | `Elements`, a separated list of values |
| `JsonStringSyntax` | `"text"` | `Value`, the string without its quotes and escapes |
| `JsonNumberSyntax` | `42`, `-1.5e3` | `Text`, the number as written |
| `JsonLiteralSyntax` | `true`, `false`, `null` | `LiteralToken`; its kind tells which literal it is |
| `JsonSkippedTextSyntax` | Text the parser could not use | The skipped tokens |

The commas belong to the lists, not to the values: see [Lists](03-Concepts/02-NodesAndTokens.md#lists). A member name is a
token of its member, not a `JsonStringSyntax`.

Whitespace and comments up to the end of a line belong to the token that ends the line, and the rest to the next
token, so a comment on its own line goes with the member below it. See [Trivia](03-Concepts/03-Trivia.md).

## Query with JSONPath

The package includes extension methods that run a [JSONPath](../04-JsonPath/01-Introduction.md) query on a tree.
`EvaluateValue` returns the node of a single result, and `Evaluate` every result:

<!-- ref:../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_JsonPath;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = JsonSyntaxTree.ParseText("""{ "items": [1, 2, 3] }""");

var value = JsonPath.Parse("$.items[1]").EvaluateValue(tree);
Assert.Equal("2", value?.ToString());
```
<!-- endref -->

`JsonPath` is in the `Meziantou.Framework.Json` namespace. The results are nodes of the tree, so you can replace them.

## Edit a document

Replace a value with a node built by `SyntaxFactory`. The rest of the document is kept as it is:

<!-- ref:../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_Editing;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = JsonSyntaxTree.ParseText("""{ "version": "1.0.0", "other": 1 }""");
var version = ((JsonObjectSyntax)tree.GetRoot().Value!).GetMember("version")!;

JsonDocumentSyntax updated = tree.GetRoot().ReplaceNode(version.Value, SyntaxFactory.JsonString("2.0.0"));

Assert.Equal("""{ "version": "2.0.0", "other": 1 }""", updated.ToFullString());
```
<!-- endref -->

- `SyntaxFactory.JsonString`, `JsonNumber`, `JsonObject`, `JsonMember`, `JsonArray` and `JsonLiteral` build values;
  `JsonString` escapes what JSON requires. `SyntaxFactory.ParseValue` reads a value written as text.
- `JsonMemberSyntax.WithName` renames a member, `JsonStringSyntax.WithValue` and `JsonNumberSyntax.WithText` change a
  value, and `AddMembers` and `AddElements` add to an object or an array.
- Removing a member or an element removes its comma, and `SyntaxRemoveOptions` says what to do with its comments. See
  [Remove a node](03-Concepts/06-EditingTrees.md#remove-a-node).

## Diagnostics

<!-- ref:../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_Diagnostics;format=md-fenced-code-block;dedent=true -->
```csharp
var messages = new List<string>();
foreach (var diagnostic in JsonSyntaxTree.ParseText("{\"a\": }").GetDiagnostics())
{
    var line = diagnostic.Location.GetLineSpan().Start;
    messages.Add($"{diagnostic.Id} ({line.Line},{line.Character}): {diagnostic.Message}");
}

Assert.Equal(["JSON0007 (0,6): Expected a JSON value."], messages);
```
<!-- endref -->

| Id | Reported for |
| --- | --- |
| `JSON0001` | An unterminated block comment |
| `JSON0002` | An unterminated string |
| `JSON0003` | A bad escape sequence |
| `JSON0004` | A malformed number |
| `JSON0005` | An unexpected token or comma |
| `JSON0006` | A missing `{`, `}`, `[`, `]`, or `:` |
| `JSON0007` | A missing value |
| `JSON0008` | A missing or unquoted property name |
| `JSON0009` | A missing comma |
| `JSON0010` | Data after the root value |
| `JSON0011` | An unescaped control character inside a string |
| `JSON0012` | Objects and arrays nested more than 256 deep |
| `JSON0013` | Whitespace JSON does not allow, such as a form feed or a non-breaking space |
| `JSON0014` | A string in single quotes |
| `JSON0015` | A property name used twice in one object (a warning) |

Every diagnostic is an error except `JSON0015`: the JSON specification only says that names should be unique.

### What is accepted

The grammar is [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259), with two extensions accepted without a diagnostic:
`//` and `/* */` comments wherever whitespace may appear, and a trailing comma after the last member or element. A byte
order mark at the start of the text is ignored. Everything else the RFC does not allow is reported, including an empty
document, a raw tab in a string, and `NaN`.

### Mistakes

A common mistake is reported once and read as what it was meant to be, so the rest of the document is read as usual:

- A string without its closing quote ends at the end of its line.
- `{name: 1}` and `{'name': 'value'}` give a member called `name`, with a diagnostic on the quotes.
- `0x1F`, `1.2.3`, `-Infinity` and `+1` are each one malformed number.
- A closing brace or bracket ends every construct it closes: in `{"a": [1, 2}`, the array is reported as missing its `]`,
  and the object ends at the `}`.
- A stray `}` after the root value is reported on its own, and the root value is kept.

## Walk a tree

`JsonSyntaxWalker`, `JsonSyntaxVisitor` and `JsonSyntaxRewriter` have a method per node type, such as `VisitJsonObject`
and `VisitJsonString`. See [Visitors and rewriters](03-Concepts/08-VisitorsAndRewriters.md), whose examples use JSON.
