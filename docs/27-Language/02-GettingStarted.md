---
title: Getting started
description: Install a language package, parse a document, read a value, report its problems, and change it without touching the rest.
---

This page uses JSON. Every language works the same way: replace `Json` by the name of your language in the type names.

## Install the package

Add the package of the language you parse to a project that targets .NET 10 or later:

```bash
dotnet add package Meziantou.Framework.Language.Json
```

It brings `Meziantou.Framework.Language`, the package every language shares.

## Parse a document

`JsonSyntaxTree.ParseText` parses a string. The tree keeps every character, comments and trailing commas included, so
writing it back gives the input exactly:

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

`GetText()` returns the text the tree was parsed from, and `GetRoot()` the root node: a `JsonDocumentSyntax`.
`ToFullString()` writes a node back as text, with its whitespace and comments. To write it to a file or a stream, pass a
`TextWriter` to `WriteTo`.

Pass the path of the file as the second argument of `ParseText`: the tree keeps it in `FilePath`, so you can report
problems with the file they come from.

## Read a value

The nodes have a property for each of their parts. Here, the root holds an object, whose members have a name and a
value:

<!-- ref:../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_ParseText;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = JsonSyntaxTree.ParseText("""{ "name": "value" }""");
var name = ((JsonObjectSyntax)tree.GetRoot().Value!).GetMember("name");
Assert.Equal("value", ((JsonStringSyntax)name!.Value).Value);
```
<!-- endref -->

See [Nodes and tokens](03-Concepts/02-NodesAndTokens.md) to walk a tree and find the node at a position.

## Report the problems

Parsing never throws: invalid text gives a tree too, and `GetDiagnostics()` says what is wrong and where. Lines and
columns are zero-based:

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

See [Diagnostics](03-Concepts/05-Diagnostics.md).

## Change a value

Trees are immutable: `ReplaceNode` returns a new root, in which only the replaced node differs. The rest of the text,
spaces and comments included, is kept as it was:

<!-- ref:../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_Editing;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = JsonSyntaxTree.ParseText("""{ "version": "1.0.0", "other": 1 }""");
var version = ((JsonObjectSyntax)tree.GetRoot().Value!).GetMember("version")!;

JsonDocumentSyntax updated = tree.GetRoot().ReplaceNode(version.Value, SyntaxFactory.JsonString("2.0.0"));

Assert.Equal("""{ "version": "2.0.0", "other": 1 }""", updated.ToFullString());
```
<!-- endref -->

`SyntaxFactory` builds new nodes, and writes them so that they read back as the value you gave. See
[Editing trees](03-Concepts/06-EditingTrees.md).

## Next steps

- [Concepts](03-Concepts/01-Overview.md): the model every language shares.
- The page of your language: [JSON](04-Json.md), [XML](05-Xml.md), [INI](06-Ini.md), [TOML](07-Toml.md),
  [CSS](08-Css.md), [Regular expressions](09-Regex/01-Overview.md), [Shell scripts](10-Shell/01-Overview.md).
