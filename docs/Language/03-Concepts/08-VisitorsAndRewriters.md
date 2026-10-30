---
title: Visitors and rewriters
description: Walk a tree with a walker, dispatch on the type of a node with a visitor, and build a new tree with a rewriter.
---

Each language has three base classes, named after it, with a `Visit…` method per node type:

| Class | Does | Use it to |
| --- | --- | --- |
| [JsonSyntaxWalker](cref:Meziantou.Framework.Language.Json.JsonSyntaxWalker) | Visits a node and everything below it | Collect or count things, check a document |
| [JsonSyntaxVisitor](cref:Meziantou.Framework.Language.Json.JsonSyntaxVisitor), [JsonSyntaxVisitor\<TResult\>](cref:Meziantou.Framework.Language.Json.JsonSyntaxVisitor{TResult}) | Calls the `Visit…` method of the type of a node, and does not go into its children | Compute a result from a node, choosing what to visit yourself |
| [JsonSyntaxRewriter](cref:Meziantou.Framework.Language.Json.JsonSyntaxRewriter) | Visits every node and builds a new tree from the nodes you return | Change every node of a kind in one pass |

Replace `Json` with your language: [XmlSyntaxWalker](cref:Meziantou.Framework.Language.Xml.XmlSyntaxWalker), [CssSyntaxRewriter](cref:Meziantou.Framework.Language.Css.CssSyntaxRewriter), [ShellSyntaxVisitor](cref:Meziantou.Framework.Language.Shell.ShellSyntaxVisitor), and so on.

## Walk a tree

Derive from the walker and override the methods of the nodes you need. Call the base method to go on into the children:

<!-- ref:../../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxWalker_StringCounter;format=md-fenced-code-block;dedent=true -->
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

<!-- ref:../../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_WalkerUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var counter = new StringCounter();
counter.Visit(JsonSyntaxTree.ParseText("""{ "a": "x", "b": ["y", 1, "z"] }""").GetRoot());

// Member names are tokens of a JsonMemberSyntax, not JsonStringSyntax values
Assert.Equal(3, counter.Count);
```
<!-- endref -->

By default, a walker visits the nodes only. Pass [SyntaxWalkerDepth.Token](cref:Meziantou.Framework.Language.SyntaxWalkerDepth.Token) or [SyntaxWalkerDepth.Trivia](cref:Meziantou.Framework.Language.SyntaxWalkerDepth.Trivia) to its
constructor to visit the tokens, or the tokens and their trivia, too, with [VisitToken](cref:Meziantou.Framework.Language.SyntaxWalker.VisitToken) and [VisitTrivia](cref:Meziantou.Framework.Language.SyntaxWalker.VisitTrivia).

[SyntaxWalker](cref:Meziantou.Framework.Language.SyntaxWalker), in [Meziantou.Framework.Language](cref:Meziantou.Framework.Language), walks the tree of any language. It has no method per node type: it
visits nodes, tokens and trivia, which is enough for code shared by several languages:

<!-- ref:../../../tests/Meziantou.Framework.Language.Tests/SyntaxTreeInvariantTests.cs;region=SyntaxWalker_TokenCounter;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class TokenCounter() : SyntaxWalker(SyntaxWalkerDepth.Token)
{
    public int Count { get; private set; }

    protected override void VisitToken(SyntaxToken token) => Count++;
}
```
<!-- endref -->

When you need a few nodes rather than a full walk, `DescendantNodes().OfType<T>()` is shorter. See
[Find a node](02-NodesAndTokens.md#find-a-node).

## Rewrite a tree

A rewriter returns a node from each `Visit…` method. Return a new node to replace the one visited, or call the base
method to rewrite its children:

<!-- ref:../../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxRewriter_RenameMembers;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class RenameMembers : JsonSyntaxRewriter
{
    public override SyntaxNode? VisitJsonMember(JsonMemberSyntax node)
    {
        // Rewrite the value first, so the members of nested objects are renamed too
        var member = (JsonMemberSyntax)base.VisitJsonMember(node)!;

        return member.WithName(member.Name.ToUpperInvariant());
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Language.Json.Tests/JsonSyntaxTreeTests.cs;region=JsonSyntaxTree_RewriterUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = JsonSyntaxTree.ParseText("""{ "a": 1, "b": { "c": 2 } }""");

var rewritten = new RenameMembers().Visit(tree.GetRoot());

Assert.Equal("""{ "A": 1, "B": { "C": 2 } }""", rewritten?.ToFullString());
```
<!-- endref -->

A rewriter keeps the nodes it does not change: when nothing below a node changed, the node is returned as it was, with
its text. `Visit` on the root returns the new root; on a node further down, it rewrites that part only.

The `Visit…` methods of a rewriter return `SyntaxNode?`. Cast the result of `Visit` to the type you expect.

## Visit without recursing

A visitor calls the method that matches the type of a node, and that is all: its [DefaultVisit](cref:Meziantou.Framework.Language.Json.JsonSyntaxVisitor.DefaultVisit) does nothing. Use it
when you decide which children to visit, or derive from [JsonSyntaxVisitor\<TResult\>](cref:Meziantou.Framework.Language.Json.JsonSyntaxVisitor{TResult}) to return a value from each
method.
