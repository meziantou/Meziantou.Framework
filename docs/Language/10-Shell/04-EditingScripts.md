---
title: Editing scripts
description: Replace a word or a command, quote arguments for a shell, rename a command everywhere, and compare scripts.
---

## Replace a word

[ReplaceNode](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.ReplaceNode) returns a new script in which only the replaced node differs. A replacement takes nothing from the node it
replaces: use [WithTriviaFrom](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.WithTriviaFrom) to keep the whitespace and comments around it:

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxTreeTests.cs;region=ShellSyntaxTree_ReplaceNode;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = ShellSyntaxTree.ParseText("echo   old    # keep this", ShellDialect.Bash);
var command = (ShellCommandSyntax)tree.GetRoot().Statements.Statements[0];
var argument = command.Arguments[0];

var updated = tree.GetRoot().ReplaceNode(argument, SyntaxFactory.Word("new", ShellDialect.Bash).WithTriviaFrom(argument));

Assert.Equal("echo   new    # keep this", updated.ToFullString());
```
<!-- endref -->

[ShellWordSyntax.WithText](cref:Meziantou.Framework.Language.Shell.ShellWordSyntax.WithText) rewrites a word as a single literal and keeps its own trivia. [ReplaceToken](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.ReplaceToken), [ReplaceTrivia](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.ReplaceTrivia),
[RemoveNode](cref:Meziantou.Framework.Language.SyntaxNodeExtensions.RemoveNode) and the `With…` method of each part of a node work the same way. See
[Editing trees](../03-Concepts/06-EditingTrees.md).

To edit by text, use [WithChanges](cref:Meziantou.Framework.Language.Shell.ShellSyntaxTree.WithChanges), which parses the script again:

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxTreeTests.cs;region=ShellSyntaxTree_WithChanges;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = ShellSyntaxTree.ParseText("echo old", ShellDialect.Bash);
var updated = tree.WithChanges(new TextChange(new TextSpan(5, 3), "new"));

Assert.Equal("echo new", updated.GetRoot().ToFullString());
```
<!-- endref -->

## Build a command

[SyntaxFactory.Command](cref:Meziantou.Framework.Language.Shell.SyntaxFactory.Command) builds a command from strings, and quotes each argument only when the dialect requires it:

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxTreeTests.cs;region=ShellSyntaxTree_SyntaxFactory;format=md-fenced-code-block;dedent=true -->
```csharp
var command = SyntaxFactory.Command(ShellDialect.Bash, "echo", "two words", "plain");

Assert.Equal("echo 'two words' plain", command.ToFullString());
```
<!-- endref -->

[SyntaxFactory.Word](cref:Meziantou.Framework.Language.Shell.SyntaxFactory.Word) and [QuotedString](cref:Meziantou.Framework.Language.Shell.SyntaxFactory.QuotedString) build a single argument the same way. Each dialect has its own quoting rules, so
build the command for the shell that will run it.

## Rename a command

A rewriter visits every node, whatever its dialect, and returns the original node when nothing below it changed. This one
renames a command and keeps the comments and indentation in front of it:

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxTreeTests.cs;region=ShellSyntaxTree_Rewriter;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class RenameCommand(string oldName, string newName) : ShellSyntaxRewriter
{
    public override SyntaxNode? VisitCommand(ShellCommandSyntax node)
    {
        if (node.NameValue != oldName || node.Name is null)
        {
            return base.VisitCommand(node);
        }

        // WithText keeps the word's own leading trivia, so the comment and indentation in front of the
        // command are not lost. A node built from scratch carries no trivia and would drop them.
        var renamed = node.Name.WithText(newName);

        return node.WithElements(new SyntaxList<ShellSyntaxNode>(
            node.Elements.Select(child => ReferenceEquals(child, node.Name) ? renamed : child)));
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxTreeTests.cs;region=ShellSyntaxTree_RewriterUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = ShellSyntaxTree.ParseText("if true; then\n  # build it\n  make all\nfi\n", ShellDialect.Bash);

var updated = new RenameCommand("make", "ninja").Visit(tree.GetRoot());

Assert.Equal("if true; then\n  # build it\n  ninja all\nfi\n", updated?.ToFullString());
```
<!-- endref -->

## Find a command again after an edit

An annotation marks a node so that you can find it in the script an edit produces:

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxTreeTests.cs;region=ShellSyntaxTree_Annotations;format=md-fenced-code-block;dedent=true -->
```csharp
var root = ShellSyntaxTree.ParseText("make build\necho done\n", ShellDialect.Bash).GetRoot();
var command = (ShellCommandSyntax)root.Statements.Statements[0];

var marker = new SyntaxAnnotation();
var marked = root.ReplaceNode(command, command.WithAdditionalAnnotations(marker));

// Something else entirely: replace the argument of the second command
var argument = ((ShellCommandSyntax)marked.Statements.Statements[1]).Arguments[0];
var edited = marked.ReplaceNode(argument, SyntaxFactory.Word("finished", ShellDialect.Bash).WithTriviaFrom(argument));

var found = edited.GetAnnotatedNodes(marker).Single();
Assert.Equal("make build", found.ToString());
Assert.Equal("make build\necho finished\n", edited.ToFullString());
```
<!-- endref -->

## Compare scripts

[IsEquivalentTo](cref:Meziantou.Framework.Language.Shell.ShellSyntaxTree.IsEquivalentTo) compares two trees by their structure, ignoring whitespace and comments:

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxTreeTests.cs;region=ShellSyntaxTree_IsEquivalentTo;format=md-fenced-code-block;dedent=true -->
```csharp
var a = ShellSyntaxTree.ParseText("echo   a  # note", ShellDialect.Bash);
var b = ShellSyntaxTree.ParseText("echo a", ShellDialect.Bash);

Assert.True(a.IsEquivalentTo(b));
```
<!-- endref -->

[GetChanges](cref:Meziantou.Framework.Language.Shell.ShellSyntaxTree.GetChanges) returns what differs between two trees, with the text they have in common at the start and at the end of a
change left out.
