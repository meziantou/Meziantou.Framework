---
title: Trivia
description: Whitespace and comments, which token they belong to, and the difference between Span and FullSpan.
---

Trivia is the text that does not change what a document means: whitespace, line breaks and comments. It is not a node of
its own: each token carries the trivia in front of it, [LeadingTrivia](cref:Meziantou.Framework.Language.SyntaxToken.LeadingTrivia), and the trivia after it, [TrailingTrivia](cref:Meziantou.Framework.Language.SyntaxToken.TrailingTrivia).

## Span and FullSpan

A node or a token has two spans, and two ways to be written as text:

| Member | Covers |
| --- | --- |
| [Span](cref:Meziantou.Framework.Language.SyntaxNode.Span), [ToString()](cref:Meziantou.Framework.Language.SyntaxNode.ToString) | The node, without the trivia of its first and last tokens |
| [FullSpan](cref:Meziantou.Framework.Language.SyntaxNode.FullSpan), [ToFullString()](cref:Meziantou.Framework.Language.SyntaxNode.ToFullString) | The node and all its trivia |

<!-- ref:../../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_SpanAndFullSpan;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = JsonSyntaxTree.ParseText("""
    {
      // the port
      "port": 8080
    }
    """);
var member = ((JsonObjectSyntax)tree.GetRoot().Value!).Members[0];

// Span and ToString() leave out the trivia, FullSpan and ToFullString() include it
Assert.Equal("\"port\": 8080", member.ToString());
Assert.Equal("  // the port\n  \"port\": 8080\n", member.ToFullString());
Assert.Equal(member.ToString(), tree.GetText().ToString(member.Span));
Assert.Equal(member.ToFullString(), tree.GetText().ToString(member.FullSpan));

var trivia = new List<SyntaxKind>();
foreach (var item in member.GetLeadingTrivia())
{
    trivia.Add(item.Kind());
}

Assert.Equal([SyntaxKind.WhitespaceTrivia, SyntaxKind.SingleLineCommentTrivia, SyntaxKind.EndOfLineTrivia, SyntaxKind.WhitespaceTrivia], trivia);
```
<!-- endref -->

Use [Span](cref:Meziantou.Framework.Language.SyntaxNode.Span) to point at a node, to underline it in an editor or report its position, and [FullSpan](cref:Meziantou.Framework.Language.SyntaxNode.FullSpan) to replace or remove
it with its comments. [GetLeadingTrivia()](cref:Meziantou.Framework.Language.SyntaxNode.GetLeadingTrivia) and [GetTrailingTrivia()](cref:Meziantou.Framework.Language.SyntaxNode.GetTrailingTrivia) return the trivia of the first and of the last
token of a node.

## Which token a trivia belongs to

Each language decides where whitespace is trivia and which token it goes to:

- **JSON and TOML**: up to and including the end of a line, whitespace and comments belong to the token that ends the
  line; what follows belongs to the next token. So a comment on its own line goes with the entry it describes, not
  with the one above it.
- **INI**: comment lines, and the whitespace around keys, section names and values, are trivia.
- **CSS**: whitespace and comments are trivia. Whitespace still has a meaning between two selectors, where it is the
  descendant combinator. See [CSS](../08-Css.md#selectors).
- **XML**: whitespace inside a tag is trivia; whitespace between tags is text, an [XmlTextSyntax](cref:Meziantou.Framework.Language.Xml.XmlTextSyntax) node, because it is
  content an application may read. See [XML](../05-Xml.md#whitespace-and-text).
- **Regular expressions**: a pattern has no trivia, except `(?#…)` comments, and whitespace and `#` comments in
  extended mode. See [Regular expressions](../09-Regex/03-OptionsAndCaptures.md#comments-and-extended-mode).
- **Shell scripts**: whitespace and comments are trivia.

This JSON document shows the rule for a comment on its own line:

<!-- ref:../../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_Trivia;format=md-fenced-code-block;dedent=true -->
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

## Read the comments

[DescendantTrivia()](cref:Meziantou.Framework.Language.SyntaxNode.DescendantTrivia) returns every trivia below a node, in the order of the text; filter it by kind. CSS, regular
expressions and shell scripts also have a `DescendantComments()` extension method:

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxTreeTests.cs;region=ShellSyntaxTree_Comments;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = ShellSyntaxTree.ParseText("# build\nmake # all targets\n", ShellDialect.Bash);

var comments = new List<string>();
foreach (var comment in tree.GetRoot().DescendantComments())
{
    comments.Add($"{comment.Span.Start}: {comment}");
}

Assert.Equal(["0: # build", "13: # all targets"], comments);
```
<!-- endref -->

## Trivia and edits

An edit keeps the trivia of what it does not touch. What happens to the trivia of what it replaces or removes is up to
you: see [Keep the comments](06-EditingTrees.md#keep-the-comments).
