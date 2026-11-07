---
title: Editing trees
description: Replace, insert and remove nodes without reformatting the rest of the text, build new nodes, edit by text, and get the text changes.
---

Trees are immutable: an edit returns a new root and leaves the old one as it was. The new root shares every node the
edit did not touch with the old one, so the rest of the text, its layout and its comments are kept exactly.

## Replace a node

[ReplaceNode](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.ReplaceNode) is an extension method on any node. Call it on the root, with a node of that root and its replacement. It
returns the same type as the root, so editing a document gives back a document:

<!-- ref:../../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs;region=XmlSyntaxTree_Editing;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = XmlSyntaxTree.ParseText("<root><a>1</a><b>2</b></root>");
var root = (XmlElementSyntax)tree.GetRoot().Nodes[0];
var b = root.Content.OfType<XmlElementSyntax>().Single(element => element.Name == "b");

XmlDocumentSyntax updated = tree.GetRoot().ReplaceNode(b, b.WithInnerText("two"));

Assert.Equal("<root><a>1</a><b>two</b></root>", updated.ToFullString());

// <a> was not touched, so it is the very same node.
Assert.True(root.Content[0].IsIncrementallyIdenticalTo(((XmlElementSyntax)updated.Nodes[0]).Content[0]));
```
<!-- endref -->

[IsIncrementallyIdenticalTo](cref:Meziantou.Framework.Language.SyntaxNode.IsIncrementallyIdenticalTo) says whether two nodes are the very same node, which is the case for every part of the old
tree that the edit did not touch.

Every node has a `With…` method for each of its parts, such as `WithName`, `WithValue` or `WithInnerText`, which
returns a copy of the node with that part changed. Use them to build the replacement from the node you replace.

[ReplaceNode](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.ReplaceNode) throws an `ArgumentException` when the node is not part of the root: after an edit, look for the nodes in
the new root, not in the old one. [ReplaceNodes](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.ReplaceNodes) replaces several nodes in one edit, and [ReplaceToken](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.ReplaceToken) and
[ReplaceTrivia](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.ReplaceTrivia) replace a token or a trivia.

## Keep the comments

A replacement keeps nothing of the node it replaces, not even the trivia in front of it, because the replacement may
want its own. `node.WithTriviaFrom(oldNode)` surrounds a node with the trivia that surrounds another one. Here, in a
regular expression in extended mode, the spaces in front of `b` are its trivia:

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_ReplaceNode;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new RegexParseOptions(RegexDialect.Net) { PatternOptions = RegexPatternOptions.IgnorePatternWhitespace };
var tree = RegexSyntaxTree.ParseText("a   b # keep this\n", options);
var second = tree.GetRoot().DescendantNodes().OfType<RegexLiteralSyntax>().Last();
var replacement = SyntaxFactory.Literal('z', RegexDialect.Net);

Assert.Equal("az # keep this\n", tree.GetRoot().ReplaceNode(second, replacement).ToFullString());

Assert.Equal("a   z # keep this\n", tree.GetRoot().ReplaceNode(second, replacement.WithTriviaFrom(second)).ToFullString());
```
<!-- endref -->

[WithLeadingTrivia](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.WithLeadingTrivia) and [WithTrailingTrivia](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.WithTrailingTrivia) set the trivia of a node or a token.

## Insert nodes

[InsertNodesBefore](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.InsertNodesBefore) and [InsertNodesAfter](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.InsertNodesAfter) add nodes to the list that contains another node, and [ReplaceNode](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.ReplaceNode) with a
list of nodes replaces one node by several. The nodes of most languages also have `Add…` methods, such as
[AddMembers](cref:Meziantou.Framework.Language.Json.JsonObjectSyntax.AddMembers) on a JSON object.

Some languages lay out what you insert for you: INI and TOML start each new entry on a line of its own, and their
documents have methods that set a value the way the surrounding entries are written. See [INI](../06-Ini.md#edit-values)
and [TOML](../07-Toml.md#edit-values).

## Remove a node

[RemoveNode](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.RemoveNode) takes a [SyntaxRemoveOptions](cref:Meziantou.Framework.Language.SyntaxRemoveOptions) that says what to do with the trivia around the node. The trivia you keep moves
to what now stands where the node was. Removing an element of a separated list removes its separator too:

<!-- ref:../../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_RemoveNode;format=md-fenced-code-block;dedent=true -->
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

| Option | Keeps |
| --- | --- |
| [KeepNoTrivia](cref:Meziantou.Framework.Language.SyntaxRemoveOptions.KeepNoTrivia) | Nothing: the comment in front of the node goes with it |
| [KeepLeadingTrivia](cref:Meziantou.Framework.Language.SyntaxRemoveOptions.KeepLeadingTrivia) | The trivia in front of the node |
| [KeepTrailingTrivia](cref:Meziantou.Framework.Language.SyntaxRemoveOptions.KeepTrailingTrivia) | The trivia after the node |
| [KeepExteriorTrivia](cref:Meziantou.Framework.Language.SyntaxRemoveOptions.KeepExteriorTrivia) | Both |
| [KeepEndOfLine](cref:Meziantou.Framework.Language.SyntaxRemoveOptions.KeepEndOfLine) | One line break of what was removed, so the lines on either side do not run together. Combine it with the others. |

Nothing is reformatted: what you keep is kept as it is. [RemoveNodes](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.RemoveNodes) removes several nodes at once; when you name a
node and something inside it, the node is removed once.

## Build nodes

Each language has a `SyntaxFactory` class that builds nodes and tokens. Its methods that take .NET values write the text
the language needs, quoting and escaping as required, so the node reads back as the value you gave:

<!-- ref:../../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_SyntaxFactory;format=md-fenced-code-block;dedent=true -->
```csharp
var document = SyntaxFactory.JsonDocument(
    SyntaxFactory.JsonObject(
        SyntaxFactory.JsonMember("name", SyntaxFactory.JsonString("app")),
        SyntaxFactory.JsonMember("ports", SyntaxFactory.JsonArray(SyntaxFactory.JsonNumber("80"), SyntaxFactory.JsonNumber("443")))));

Assert.Equal("""{"name":"app","ports":[80,443]}""", document.ToFullString());
```
<!-- endref -->

A node built by code has the whitespace its factory writes, which may be none, as above: give it trivia with
[WithTriviaFrom](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.WithTriviaFrom), [WithLeadingTrivia](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.WithLeadingTrivia) and [WithTrailingTrivia](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.WithTrailingTrivia), or format an XML document with the XML formatter. Several factories also have
`Parse…` methods, such as `ParseValue`, which read a fragment exactly as you write it.

## Edit by text

When you have a text change rather than a node, such as the edit an editor sends, [WithChanges](cref:Meziantou.Framework.Language.SourceText.WithChanges) applies it to the text
of the tree and parses the result:

<!-- ref:../../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_EditingByText;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = JsonSyntaxTree.ParseText("""{"a":1}""");
var updated = tree.WithChanges(new TextChange(new TextSpan(5, 1), "2"));

Assert.Equal("""{"a":2}""", updated.GetRoot().ToFullString());
```
<!-- endref -->

Unlike the node edits, [WithChanges](cref:Meziantou.Framework.Language.SourceText.WithChanges) parses again: a change of a few characters can change how the rest of the text
reads, as an opening quote does.

## Make a tree from a root

The edit methods return a node. To get a tree, with its text and diagnostics, call `tree.WithRoot(root)`, which keeps
the path of the tree, or the `Create` method of the tree class of your language. See
[Diagnostics after an edit](05-Diagnostics.md#diagnostics-after-an-edit).

## Get the text changes

`GetChanges(oldTree)` returns the [TextChange](cref:Meziantou.Framework.Language.TextChange)s that turn the text of an old tree into the text of a new one, for an
editor or a tool that applies edits rather than replacing the whole file. `GetChangedSpans(oldTree)` returns the spans of
the new tree that differ:

<!-- ref:../../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_GetChanges;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = JsonSyntaxTree.ParseText("""{ "version": "1.0.0", "name": "app" }""");
var version = ((JsonObjectSyntax)tree.GetRoot().Value!).GetMember("version")!;

var updatedRoot = tree.GetRoot().ReplaceNode(version.Value, SyntaxFactory.JsonString("2.0.0"));
var updatedTree = tree.WithRoot(updatedRoot);

// The text changes from the old tree to the new one, for an editor that applies edits
var change = Assert.Single(updatedTree.GetChanges(tree));
Assert.Equal(new TextSpan(13, 7), change.Span); // "1.0.0", quotes included
Assert.Equal("\"2.0.0\"", change.NewText);
```
<!-- endref -->

The changes of a JSON tree cover the tokens that differ. Regular expressions and shell scripts trim the text the two
versions have in common at the start and the end of a change.

## Compare trees

[IsEquivalentTo](cref:Meziantou.Framework.Language.SyntaxNode.IsEquivalentTo) compares two nodes or two trees by their structure and text, whatever their positions. Regular
expressions and shell scripts ignore the trivia when they compare, so two scripts that differ only by their whitespace
or comments are equivalent:

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxTreeTests.cs;region=ShellSyntaxTree_IsEquivalentTo;format=md-fenced-code-block;dedent=true -->
```csharp
var a = ShellSyntaxTree.ParseText("echo   a  # note", ShellDialect.Bash);
var b = ShellSyntaxTree.ParseText("echo a", ShellDialect.Bash);

Assert.True(a.IsEquivalentTo(b));
```
<!-- endref -->

## Find a node again

After an edit, the nodes you hold belong to the old tree. To find one of them in the new tree, mark it with an
annotation before the edit: see [Annotations](07-Annotations.md).
