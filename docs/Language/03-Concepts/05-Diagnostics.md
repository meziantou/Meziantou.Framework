---
title: Diagnostics
description: Read the problems a parser reports, with their identifier, severity, message and position.
---

A parser never throws on invalid text: it reports each problem as a `Diagnostic` and keeps going. `GetDiagnostics()` on
a tree returns them:

<!-- ref:../../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=Diagnostic_GetDiagnostics;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = XmlSyntaxTree.ParseText("<root><child></root>");

var messages = new List<string>();
foreach (var diagnostic in tree.GetDiagnostics())
{
    messages.Add($"{diagnostic.Id} at {diagnostic.Location}: {diagnostic.Message}");
}

Assert.Equal(["XML0001 at (0,6)-(0,13): Missing end tag for 'child'."], messages);
```
<!-- endref -->

## What a diagnostic holds

| Property | Holds |
| --- | --- |
| `Id` | A stable identifier, prefixed by the language: `JSON0007`, `XML0001`, `CSS0011`, … Each language page lists its identifiers. |
| `Severity` | `Error` or `Warning` (`DiagnosticSeverity` also has `Info` and `Hidden`) |
| `Message` | A sentence for a person, which may change between versions: test the `Id` in code, not the message |
| `Location` | Where the problem is: a span of the tree's text |

`ToString()` writes the identifier, the position and the message.

## Errors and warnings

An error is text the language does not allow. A warning is text the language allows but that is likely a mistake, or
that the parser does not know because it may be newer than the parser, such as a duplicate JSON property name or an
unknown CSS at-rule:

<!-- ref:../../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_DiagnosticSeverity;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = JsonSyntaxTree.ParseText("""{ "a": 1, "a": 2 }""");

var diagnostic = Assert.Single(tree.GetDiagnostics());
Assert.Equal("JSON0015", diagnostic.Id);
Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);

// A warning does not make the document invalid
var hasErrors = tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error);
Assert.False(hasErrors);
```
<!-- endref -->

To check that a document is valid, look for errors, not for an empty list.

## Positions

`diagnostic.Location` is bound to the text of the tree, so it gives lines and columns. They are zero-based:

<!-- ref:../../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=Language_Diagnostics;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = XmlSyntaxTree.ParseText("<root>\n  <item>\n</root>");

var diagnostic = Assert.Single(tree.GetDiagnostics());
Assert.Equal("XML0001 at (1,2)-(1,8): Missing end tag for 'item'.", $"{diagnostic.Id} at {diagnostic.Location}: {diagnostic.Message}");

var lineSpan = diagnostic.Location.GetLineSpan();
Assert.Equal(1, lineSpan.Start.Line);      // zero-based
Assert.Equal(2, lineSpan.Start.Character); // zero-based

// The location is bound to the tree's own text
Assert.Same(tree.GetText(), diagnostic.Location.SourceText);
```
<!-- endref -->

See [Source text and positions](04-SourceTextAndPositions.md#lines-and-columns).

## Diagnostics of a node

`node.ContainsDiagnostics` says whether a node, or anything below it, has a diagnostic, and `node.GetDiagnostics()`
returns them. A diagnostic belongs to the node it is about, and moves with it when you edit the tree.

A few problems depend on the whole document rather than on one node, such as a key defined twice in TOML: the tree
works them out when you call `GetDiagnostics()`. See the page of your language.

## Diagnostics after an edit

A tree you create from an edited root, with `WithRoot` or `Create`, reports the diagnostics its nodes carry: a node you
removed takes its diagnostics with it. CSS and TOML go further and parse the text of the new root again, so they also
report a node built by code that does not read back as itself. To check the text of any edited tree, parse
`root.ToFullString()` again.
