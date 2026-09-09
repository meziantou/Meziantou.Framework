# Meziantou.Framework.Language.Json

An immutable JSON syntax tree that keeps every character of the text it was parsed from — comments, whitespace,
trailing commas and all — and lets you edit it without reformatting anything you did not touch.

It is modelled on Roslyn. If you have used `Microsoft.CodeAnalysis`, everything here will look familiar: a
`SyntaxTree` over a `SourceText`, nodes and tokens with spans, trivia, `SyntaxKind`, visitors, rewriters, and
annotations.

Parsing never throws and never gives up. Whatever the text says, the tree reproduces it exactly, and anything wrong
with it is reported through `GetDiagnostics()`.

```csharp
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Json;

var tree = JsonSyntaxTree.ParseText("""
{
  // the name of the thing
  "name": "value",
  "items": [1, 2,],
}
""");

// Comments and trailing commas are accepted, so there is nothing to report here.
Console.WriteLine(tree.GetDiagnostics().Count); // 0

// And the text comes back exactly as it went in.
Console.WriteLine(tree.GetRoot().ToFullString() == tree.GetText().Text); // True
```

## The shape of a tree

```
JsonDocumentSyntax
├── Values : SyntaxList<JsonValueSyntax>      (one, for a well-formed document)
│   └── JsonObjectSyntax
│       ├── OpenBraceToken
│       ├── Members : SeparatedSyntaxList<JsonMemberSyntax>
│       │   └── JsonMemberSyntax  →  NameToken, ColonToken, Value
│       └── CloseBraceToken
└── EndOfFileToken
```

A value is a `JsonObjectSyntax`, `JsonArraySyntax`, `JsonStringSyntax`, `JsonNumberSyntax`, `JsonLiteralSyntax`
(`true`, `false`, `null`), or `JsonSkippedTextSyntax` for text the parser could not use.

**The commas belong to the list, not to what they follow.** `JsonObjectSyntax.Members` and `JsonArraySyntax.Elements`
are separated lists: the elements and the commas between them share one sequence. `Count` counts elements and
`SeparatorCount` counts commas, so a trailing comma is simply a list where the two are equal:

```csharp
var elements = ((JsonArraySyntax)JsonSyntaxTree.ParseText("[1, 2,]").GetRoot().Value!).Elements;

elements.Count;                // 2
elements.SeparatorCount;       // 2
elements.HasTrailingSeparator; // true
elements[0];                   // the JsonNumberSyntax for 1 -- the element itself, not a wrapper
elements.GetSeparator(1);      // the trailing comma
```

## Which trivia belongs to which token

Whitespace and comments up to and including the end of a line belong to the token that *ends* that line; everything
after belongs to the token that *follows*. That is what makes a comment on its own line attach to the thing it
describes rather than to the thing above it:

```csharp
var tree = JsonSyntaxTree.ParseText("""
{
  // the name of the thing
  "name": "value"
}
""");

var obj = (JsonObjectSyntax)tree.GetRoot().Value!;

obj.OpenBraceToken.TrailingTrivia.ToFullString();   // "\n"
obj.Members[0].NameToken.LeadingTrivia.ToFullString(); // "  // the name of the thing\n  "
```

## Editing without reformatting

An edit rebuilds only the path from the changed node up to the root. Everything else is carried over as-is, so the
rest of the document is untouched — and the result is the same type you started with, so no cast is needed.

```csharp
var tree = JsonSyntaxTree.ParseText("""{ "version": "1.0.0", "other": 1 }""");
var version = ((JsonObjectSyntax)tree.GetRoot().Value!).GetMember("version")!;

JsonDocumentSyntax updated = tree.GetRoot().ReplaceNode(version.Value, SyntaxFactory.JsonString("2.0.0"));

Console.WriteLine(updated.ToFullString()); // { "version": "2.0.0", "other": 1 }
```

There are `WithX` methods on every node too, and `SyntaxFactory` to build nodes from scratch.

## Finding a node again after an edit

An annotation is a marker you attach to a node and find again in the tree an edit produced, wherever it ended up:

```csharp
var marker = new SyntaxAnnotation();
var root = tree.GetRoot();
var member = ((JsonObjectSyntax)root.Value!).Members[0];

var marked = root.ReplaceNode(member, member.WithAdditionalAnnotations(marker));
var edited = marked.ReplaceNode(/* something else entirely */);

var found = edited.GetAnnotatedNodes(marker).Single();
```

## Diagnostics

Nothing is thrown for bad input; it is reported instead, with a location you can turn into line and character
positions.

```csharp
foreach (var diagnostic in JsonSyntaxTree.ParseText("{\"a\": }").GetDiagnostics())
{
    var line = diagnostic.Location.GetLineSpan().Start;
    Console.WriteLine($"{diagnostic.Id} ({line.Line},{line.Character}): {diagnostic.Message}");
}
```

| Id | Reported for |
| --- | --- |
| `JSON0001` | An unterminated block comment |
| `JSON0002` | An unterminated string |
| `JSON0003` | A bad escape sequence |
| `JSON0004` | A malformed number |
| `JSON0005` | An unexpected token or comma |
| `JSON0006` | A missing `{`, `}`, `[`, `]`, or `:` |
| `JSON0007` | A missing value |
| `JSON0008` | A missing property name |
| `JSON0009` | A missing comma |
| `JSON0010` | Data after the root value |
| `JSON0011` | A line break inside a string |

## Walking a tree

`JsonSyntaxWalker` visits a node and everything below it; `JsonSyntaxVisitor` dispatches on kind without recursing;
`JsonSyntaxRewriter` builds a new tree from an old one.

```csharp
private sealed class StringCounter : JsonSyntaxWalker
{
    public int Count { get; private set; }

    public override void VisitJsonString(JsonStringSyntax node)
    {
        Count++;
        base.VisitJsonString(node);
    }
}
```

Or, without a visitor: `root.DescendantNodes()`, `DescendantTokens()`, `DescendantTrivia()`, `FindToken(position)`,
`FindNode(span)`.

## JSONPath

```csharp
using Meziantou.Framework.Json;

var value = JsonPath.Parse("$.items[1]").EvaluateValue(tree);
```

## Editing by text

```csharp
var tree = JsonSyntaxTree.ParseText("""{"a":1}""");
var updated = tree.WithChanges(new TextChange(new TextSpan(5, 1), "2"));

Console.WriteLine(updated.GetRoot().ToFullString()); // {"a":2}
```

## Coming from version 3

Version 4 moved the tree onto the same model Roslyn uses, which changed most of the API.

| Version 3 | Version 4 |
| --- | --- |
| `JsonSyntaxKind` | `SyntaxKind` |
| `node.Kind` | `node.Kind()` |
| `JsonSyntaxToken`, `JsonSyntaxTrivia`, `JsonSyntaxNodeOrToken` (classes) | `SyntaxToken`, `SyntaxTrivia`, `SyntaxNodeOrToken` from `Meziantou.Framework.Language` (structs) |
| `JsonArrayElementSyntax` | gone — `Elements[i]` is the value itself |
| `JsonMemberSyntax.CommaToken` | gone — `Members.GetSeparator(i)` |
| `tree.Root`, `tree.Text`, `tree.SourceText`, `tree.Diagnostics` | `tree.GetRoot()`, `tree.GetText()`, `tree.GetDiagnostics()` |
| `new JsonStringSyntax(token)` and the other constructors | `SyntaxFactory.JsonString(token)` and friends |
| `SyntaxFactory.Object/Member/Array/String/Number` | `SyntaxFactory.JsonObject/JsonMember/JsonArray/JsonString/JsonNumber` |
| `SyntaxFactory.StringToken(value)` | `SyntaxFactory.Literal(value)` |
| `root.ReplaceNode(...)` returning `JsonDocumentSyntax` after re-parsing | `root.ReplaceNode(...)` returning the type it was given, rebuilding only what changed |
| `VisitObject`, `VisitMember`, … | `VisitJsonObject`, `VisitJsonMember`, … |
