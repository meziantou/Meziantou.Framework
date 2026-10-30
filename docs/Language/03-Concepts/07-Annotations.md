---
title: Annotations
description: Mark a node, a token or a trivia, and find it again in the tree an edit produced.
---

An edit returns a new tree: the nodes you held belong to the old one. A [SyntaxAnnotation](cref:Meziantou.Framework.Language.SyntaxAnnotation) is a marker that you attach to
a node and that stays on it through the edits, wherever the node ends up:

<!-- ref:../../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_Annotations;format=md-fenced-code-block;dedent=true -->
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

1. Create an annotation, and attach it with [WithAdditionalAnnotations](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.WithAdditionalAnnotations), which returns a copy of the node.
2. Put the copy in the tree with [ReplaceNode](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.ReplaceNode).
3. Edit the tree as you like.
4. Find the node with `GetAnnotatedNodes(annotation)` on the new root.

Two annotations are equal only when they are the same instance, even when their kind and data match. To find
annotations without holding the instances, give them a kind: `GetAnnotatedNodes(kind)` finds every node marked with
an annotation of that kind, such as `new SyntaxAnnotation("rename", "item-42")`. [Data](cref:Meziantou.Framework.Language.SyntaxAnnotation.Data) carries a string of your own,
such as the identifier of the item you are processing.

## Members

| Member | Does |
| --- | --- |
| [WithAdditionalAnnotations](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.WithAdditionalAnnotations) | Returns a copy of a node, a token or a trivia with more annotations |
| [WithoutAnnotations](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.WithoutAnnotations) | Returns a copy without the given annotations, or without those of a kind |
| [HasAnnotation](cref:Meziantou.Framework.Language.SyntaxNode.HasAnnotation), `HasAnnotations(kind)`, [GetAnnotations()](cref:Meziantou.Framework.Language.SyntaxNode.GetAnnotations) | Read the annotations of a node, a token or a trivia |
| [GetAnnotatedNodes](cref:Meziantou.Framework.Language.SyntaxNode.GetAnnotatedNodes), [GetAnnotatedTokens](cref:Meziantou.Framework.Language.SyntaxNode.GetAnnotatedTokens), [GetAnnotatedTrivia](cref:Meziantou.Framework.Language.SyntaxNode.GetAnnotatedTrivia), [GetAnnotatedNodesAndTokens](cref:Meziantou.Framework.Language.SyntaxNode.GetAnnotatedNodesAndTokens) | Find what carries an annotation, or an annotation of a kind, below a node |
| [ContainsAnnotations](cref:Meziantou.Framework.Language.SyntaxNode.ContainsAnnotations) | Says whether a node, or anything below it, has an annotation |
| [CopyAnnotationsTo](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.CopyAnnotationsTo) | Copies the annotations of a node to another, such as its replacement |

A replacement does not inherit the annotations of the node it replaces: copy them with [CopyAnnotationsTo](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.CopyAnnotationsTo) when the
replacement must be found again.
