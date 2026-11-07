---
title: Editing patterns
description: Replace a part of a pattern, build a pattern by code, rename a group with a rewriter, and compare two patterns.
---

## Replace a part of a pattern

[ReplaceNode](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.ReplaceNode) returns a new pattern in which only the replaced node differs. A replacement takes nothing from the node it
replaces: use [WithTriviaFrom](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.WithTriviaFrom) to keep the whitespace and comments of extended mode in front of it:

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

[ReplaceToken](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.ReplaceToken), [ReplaceTrivia](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.ReplaceTrivia), [RemoveNode](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.RemoveNode) and the `With…` method of each part of a node work the same way. See
[Editing trees](../03-Concepts/06-EditingTrees.md).

To edit by text, use [WithChanges](cref:Meziantou.Framework.Language.Regex.RegexSyntaxTree.WithChanges), which parses the pattern again. A tree read by [ParseJavaScriptLiteral](cref:Meziantou.Framework.Language.Regex.RegexSyntaxTree.ParseJavaScriptLiteral) stays a
literal: its slashes and its flags are kept.

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_WithChanges;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = RegexSyntaxTree.ParseText("ab+c", RegexDialect.Net);
var updated = tree.WithChanges(new TextChange(new TextSpan(2, 1), "*"));

Assert.Equal("ab*c", updated.GetText().Text);
```
<!-- endref -->

## Build a pattern

[SyntaxFactory](cref:Meziantou.Framework.Language.Regex.SyntaxFactory) creates nodes, and escapes a character only when the dialect requires it:

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_SyntaxFactory;format=md-fenced-code-block;dedent=true -->
```csharp
var pattern = SyntaxFactory.Sequence(
    SyntaxFactory.Anchor(RegexAnchorKind.Caret),
    SyntaxFactory.Quantified(SyntaxFactory.ClassEscape('d'), min: 1, max: 3),
    SyntaxFactory.Literal('.', RegexDialect.Net));

Assert.Equal(@"^\d{1,3}\.", pattern.ToFullString());
```
<!-- endref -->

[SyntaxFactory.LiteralText](cref:Meziantou.Framework.Language.Regex.SyntaxFactory.LiteralText) builds the literals of a whole string, escaped for a dialect.

## Rewrite a pattern

A rewriter visits every node, whatever its dialect, and returns the original node when nothing below it changed. This one
renames a named group:

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_Rewriter;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class RenameGroup(string oldName, string newName) : RegexSyntaxRewriter
{
    public override SyntaxNode? VisitNamedGroup(RegexNamedGroupSyntax node)
    {
        if (node.Name != oldName)
        {
            return base.VisitNamedGroup(node);
        }

        // WithTriviaFrom keeps whatever stood in front of the old name.
        var renamed = SyntaxFactory.Token(SyntaxKind.NameToken, newName).WithTriviaFrom(node.NameToken);

        return node.WithNameToken(renamed);
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_RewriterUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = RegexSyntaxTree.ParseText(@"(?<year>\d{4})-(?<month>\d{2})", RegexDialect.Net);

var updated = new RenameGroup("year", "y").Visit(tree.GetRoot());

Assert.Equal(@"(?<y>\d{4})-(?<month>\d{2})", updated?.ToFullString());
```
<!-- endref -->

Renaming a group does not rename the backreferences to it, such as `\k<year>`: rewrite them in the same rewriter.

## Find a part again after an edit

An annotation marks a node so that you can find it in the pattern an edit produces:

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_Annotations;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = RegexSyntaxTree.ParseText("a|b", RegexDialect.Net);
var marker = new SyntaxAnnotation();
var branch = tree.GetRoot().Alternation.Branches[1];

var marked = tree.GetRoot().ReplaceNode(branch, branch.WithAdditionalAnnotations(marker));
var edited = marked.ReplaceNode(marked.Alternation.Branches[0], SyntaxFactory.LiteralText("xy", RegexDialect.Net));

var found = edited.GetAnnotatedNodes(marker).Single();
Assert.Equal("b", found.ToFullString());
```
<!-- endref -->

## Compare patterns

[IsEquivalentTo](cref:Meziantou.Framework.Language.Regex.RegexSyntaxTree.IsEquivalentTo) compares two trees by their structure, ignoring the whitespace and comments of extended mode. Trees of
different dialects, or parsed with different options, are never equivalent, since the same characters read differently:

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_IsEquivalentTo;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new RegexParseOptions(RegexDialect.Net) { PatternOptions = RegexPatternOptions.IgnorePatternWhitespace };
var spaced = RegexSyntaxTree.ParseText("a  b   # note\n", options);
var tight = RegexSyntaxTree.ParseText("ab", options);

Assert.True(spaced.IsEquivalentTo(tight));
```
<!-- endref -->

[GetChanges](cref:Meziantou.Framework.Language.Regex.RegexSyntaxTree.GetChanges) returns what differs between two trees, with the text they have in common at the start and at the end of a
change left out.
