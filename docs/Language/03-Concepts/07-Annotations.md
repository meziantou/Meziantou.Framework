---
title: Annotations
description: Mark a node, a token or a trivia, and find it again in the tree an edit produced.
---

An edit returns a new tree: the nodes you held belong to the old one. A `SyntaxAnnotation` is a marker that you attach to
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

1. Create an annotation, and attach it with `WithAdditionalAnnotations`, which returns a copy of the node.
2. Put the copy in the tree with `ReplaceNode`.
3. Edit the tree as you like.
4. Find the node with `GetAnnotatedNodes(annotation)` on the new root.

Two annotations are equal only when they are the same instance, even when their kind and data match. To find
annotations without holding the instances, give them a kind: `GetAnnotatedNodes(kind)` finds every node marked with
an annotation of that kind, such as `new SyntaxAnnotation("rename", "item-42")`. `Data` carries a string of your own,
such as the identifier of the item you are processing.

## Members

| Member | Does |
| --- | --- |
| `WithAdditionalAnnotations` | Returns a copy of a node, a token or a trivia with more annotations |
| `WithoutAnnotations` | Returns a copy without the given annotations, or without those of a kind |
| `HasAnnotation`, `HasAnnotations(kind)`, `GetAnnotations()` | Read the annotations of a node, a token or a trivia |
| `GetAnnotatedNodes`, `GetAnnotatedTokens`, `GetAnnotatedTrivia`, `GetAnnotatedNodesAndTokens` | Find what carries an annotation, or an annotation of a kind, below a node |
| `ContainsAnnotations` | Says whether a node, or anything below it, has an annotation |
| `CopyAnnotationsTo` | Copies the annotations of a node to another, such as its replacement |

A replacement does not inherit the annotations of the node it replaces: copy them with `CopyAnnotationsTo` when the
replacement must be found again.
