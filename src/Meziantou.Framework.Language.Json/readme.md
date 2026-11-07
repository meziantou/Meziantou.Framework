# Meziantou.Framework.Language.Json

An immutable JSON syntax tree that keeps every character of the text it was parsed from — comments, whitespace,
trailing commas and all — and lets you edit it without reformatting anything you did not touch.

It is modelled on Roslyn. If you have used `Microsoft.CodeAnalysis`, everything here will look familiar: a
`SyntaxTree` over a `SourceText`, nodes and tokens with spans, trivia, `SyntaxKind`, visitors, rewriters, and
annotations.

Parsing never throws and never gives up. Whatever the text says, the tree reproduces it exactly, and anything wrong
with it is reported through `GetDiagnostics()`.

The types are in the `Meziantou.Framework.Language.Json` namespace, and the shared ones (`SyntaxAnnotation`, `TextChange`, …) in `Meziantou.Framework.Language`.

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

<!-- ref:../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_SeparatedList;format=md-fenced-code-block;dedent=true -->
```csharp
var elements = ((JsonArraySyntax)JsonSyntaxTree.ParseText("[1, 2,]").GetRoot().Value!).Elements;

Assert.Equal(2, elements.Count);
Assert.Equal(2, elements.SeparatorCount);
Assert.True(elements.HasTrailingSeparator);
Assert.IsType<JsonNumberSyntax>(elements[0]); // the element itself, not a wrapper
Assert.Equal(",", elements.GetSeparator(1).Text); // the trailing comma
```
<!-- endref -->

## Which trivia belongs to which token

Whitespace and comments up to and including the end of a line belong to the token that *ends* that line; everything
after belongs to the token that *follows*. That is what makes a comment on its own line attach to the thing it
describes rather than to the thing above it:

<!-- ref:../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_Trivia;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = JsonSyntaxTree.ParseText("""
{
  // the name of the thing
  "name": "value"
}
""");

var obj = (JsonObjectSyntax)tree.GetRoot().Value!;

Assert.Equal("\n", obj.OpenBraceToken.TrailingTrivia.ToFullString());
Assert.Equal("  // the name of the thing\n  ", obj.Members[0].NameToken.LeadingTrivia.ToFullString());
```
<!-- endref -->

## Editing without reformatting

An edit rebuilds only the path from the changed node up to the root. Everything else is carried over as-is, so the
rest of the document is untouched — and the result is the same type you started with, so no cast is needed.

<!-- ref:../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_Editing;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = JsonSyntaxTree.ParseText("""{ "version": "1.0.0", "other": 1 }""");
var version = ((JsonObjectSyntax)tree.GetRoot().Value!).GetMember("version")!;

JsonDocumentSyntax updated = tree.GetRoot().ReplaceNode(version.Value, SyntaxFactory.JsonString("2.0.0"));

Assert.Equal("""{ "version": "2.0.0", "other": 1 }""", updated.ToFullString());
```
<!-- endref -->

There are `WithX` methods on every node too, and `SyntaxFactory` to build nodes from scratch.

## Removing a node

Removing an element takes its comma with it. What to do with the trivia around it is a choice you have to make,
because the default quietly throws away any comment describing it:

<!-- ref:../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_RemoveNode;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = JsonSyntaxTree.ParseText("""
{
  // the one that matters
  "a": 1,
  "b": 2
}
""");
var members = ((JsonObjectSyntax)tree.GetRoot().Value!).Members;

var withoutTrivia = tree.GetRoot().RemoveNode(members[0], SyntaxRemoveOptions.KeepNoTrivia);
Assert.Equal("""
{
  "b": 2
}
""", withoutTrivia!.ToFullString());

var withLeadingTrivia = tree.GetRoot().RemoveNode(members[0], SyntaxRemoveOptions.KeepLeadingTrivia);
Assert.Equal("""
{
  // the one that matters
    "b": 2
}
""", withLeadingTrivia!.ToFullString());
```
<!-- endref -->

Anything kept moves onto whatever now stands where the node was — the thing after it, or the thing before it when
nothing follows. It is preserved, not tidied up: nothing here reformats.

`SyntaxRemoveOptions` also has `KeepTrailingTrivia`, `KeepExteriorTrivia` (both), and `KeepEndOfLine`, which keeps one
line break out of what was removed so the lines either side do not run together. `RemoveNodes` takes several at once;
naming both a node and something inside it removes the node, once.

## Finding a node again after an edit

An annotation is a marker you attach to a node and find again in the tree an edit produced, wherever it ended up:

<!-- ref:../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_Annotations;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = JsonSyntaxTree.ParseText("""{ "a": 1, "b": 2 }""");
var marker = new SyntaxAnnotation();
var root = tree.GetRoot();
var member = ((JsonObjectSyntax)root.Value!).Members[0];

var marked = root.ReplaceNode(member, member.WithAdditionalAnnotations(marker));

// Something else entirely: replace the value of "b", which rebuilds the path from it up to the root
var b = ((JsonObjectSyntax)marked.Value!).GetMember("b")!;
var edited = marked.ReplaceNode(b.Value, SyntaxFactory.JsonString("two"));

var found = edited.GetAnnotatedNodes(marker).Single();
Assert.Equal("\"a\": 1", found.ToString());
```
<!-- endref -->

## Diagnostics

Nothing is thrown for bad input; it is reported instead, with a location you can turn into line and character
positions.

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

Everything is an error except `JSON0015`: RFC 8259 only says names *should* be unique.

### What is accepted

The grammar is [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259), with two extensions that are accepted without a
diagnostic: `//` and `/* */` comments wherever whitespace may appear, and a trailing comma after the last member or
element. A byte order mark at the very start of the text is ignored, as the RFC allows. Anything else the RFC does not
allow is reported, including an empty document, a raw tab inside a string, and `NaN`.

### Recovering from mistakes

The common mistakes are reported once and parsed into the tree they were meant to be, so one error does not bury the
rest of the document under others:

- A string ends at the end of its line when its closing quote is missing, instead of running on to the next quote.
- `{name: 1}` and `{'name': 'value'}` produce a member called `name`, with a diagnostic on the quotes.
- `0x1F`, `1.2.3`, `-Infinity`, and `+1` are each a single malformed number rather than a number and a stray word.
- A closing brace or bracket ends every construct it closes over: in `{"a": [1, 2}` the array is reported as missing
  its `]`, and the object still ends at the `}`.
- A stray `}` after the root value is reported on its own, and the root value is kept.

## Walking a tree

`JsonSyntaxWalker` visits a node and everything below it; `JsonSyntaxVisitor` dispatches on kind without recursing;
`JsonSyntaxRewriter` builds a new tree from an old one.

<!-- ref:../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxWalker_StringCounter;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_WalkerUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var counter = new StringCounter();
counter.Visit(JsonSyntaxTree.ParseText("""{ "a": "x", "b": ["y", 1, "z"] }""").GetRoot());

// Member names are tokens of a JsonMemberSyntax, not JsonStringSyntax values
Assert.Equal(3, counter.Count);
```
<!-- endref -->

Or, without a visitor: `root.DescendantNodes()`, `DescendantTokens()`, `DescendantTrivia()`, `FindToken(position)`,
`FindNode(span)`.

## JSONPath

`JsonPath` comes from [`Meziantou.Framework.JsonPath`](https://www.nuget.org/packages/Meziantou.Framework.JsonPath/), in the `Meziantou.Framework.Json` namespace.

<!-- ref:../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_JsonPath;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = JsonSyntaxTree.ParseText("""{ "items": [1, 2, 3] }""");

var value = JsonPath.Parse("$.items[1]").EvaluateValue(tree);
Assert.Equal("2", value?.ToString());
```
<!-- endref -->

## Editing by text

<!-- ref:../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_EditingByText;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = JsonSyntaxTree.ParseText("""{"a":1}""");
var updated = tree.WithChanges(new TextChange(new TextSpan(5, 1), "2"));

Assert.Equal("""{"a":2}""", updated.GetRoot().ToFullString());
```
<!-- endref -->

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
