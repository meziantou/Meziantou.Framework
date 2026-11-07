---
title: Reading scripts
description: Find the commands of a script, read their names and arguments, reach the expressions, and read the comments.
---

## The shape of a script

A [ShellScriptSyntax](cref:Meziantou.Framework.Language.Shell.ShellScriptSyntax) holds a [ShellStatementListSyntax](cref:Meziantou.Framework.Language.Shell.ShellStatementListSyntax), whose `Statements` are the statements of the script.
A statement is a command, a pipeline, a command list (`a && b`), or a compound statement such as `if`, `for` or a
function definition, which holds statement lists of its own.

The separators belong to the lists: a statement list, a pipeline and a command list are separated lists, where [Count](cref:Meziantou.Framework.Language.SeparatedSyntaxList{TNode}.Count)
counts the statements and [SeparatorCount](cref:Meziantou.Framework.Language.SeparatedSyntaxList{TNode}.SeparatorCount) the `;`, `|` or `&&` between them. See
[Lists](../03-Concepts/02-NodesAndTokens.md#lists).

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxVisitorTests.cs;region=ShellSyntaxNode_DescendantNodes;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = ShellSyntaxTree.ParseText("ls -l", ShellDialect.Bash);

var kinds = new List<SyntaxKind>();
foreach (var node in tree.GetRoot().DescendantNodes())
{
    kinds.Add(node.Kind());
}

Assert.Equal([SyntaxKind.StatementList, SyntaxKind.Command, SyntaxKind.Word, SyntaxKind.LiteralWordPart, SyntaxKind.Word, SyntaxKind.LiteralWordPart], kinds);
```
<!-- endref -->

## Commands and words

A [ShellCommandSyntax](cref:Meziantou.Framework.Language.Shell.ShellCommandSyntax) is a simple command. Its [Elements](cref:Meziantou.Framework.Language.Shell.ShellCommandSyntax.Elements) are its parts in the order of the text, and named properties
sort them:

| Property | Holds |
| --- | --- |
| [Name](cref:Meziantou.Framework.Language.Shell.ShellCommandSyntax.Name), [NameValue](cref:Meziantou.Framework.Language.Shell.ShellCommandSyntax.NameValue) | The word of the command name, and its value |
| [Arguments](cref:Meziantou.Framework.Language.Shell.ShellCommandSyntax.Arguments) | The words after the name |
| [Assignments](cref:Meziantou.Framework.Language.Shell.ShellCommandSyntax.Assignments) | The `NAME=value` in front of the command |
| [Redirections](cref:Meziantou.Framework.Language.Shell.ShellCommandSyntax.Redirections) | The `>file`, `2>&1`, `<<EOF` of the command |

A [ShellWordSyntax](cref:Meziantou.Framework.Language.Shell.ShellWordSyntax) is made of parts: literal text, quoted strings, variable references, command substitutions, globs.
Its [Value](cref:Meziantou.Framework.Language.Shell.ShellWordSyntax.Value) is the word with its quotes and escapes resolved, or `null` when it depends on an expansion, such as a
variable:

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxTreeTests.cs;region=ShellSyntaxTree_ParseCommand;format=md-fenced-code-block;dedent=true -->
```csharp
var command = (ShellCommandSyntax)ShellSyntaxTree.ParseCommand("git commit -m 'wip'", ShellDialect.Bash);

Assert.Equal("git", command.NameValue);
Assert.Equal("wip", command.Arguments[2].Value);
```
<!-- endref -->

To find every command, filter the descendants by type or by kind:

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxVisitorTests.cs;region=SyntaxKind_CompareKind;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = ShellSyntaxTree.ParseText("ls -l | wc -l && echo done", ShellDialect.Bash);

var commandCount = 0;
foreach (var node in tree.GetRoot().DescendantNodes())
{
    if (node.Kind() == SyntaxKind.Command)
    {
        // handle a simple command
        commandCount++;
    }
}

Assert.Equal(3, commandCount);
```
<!-- endref -->

## Expressions

What an expression is differs between shells, so there is no entry point to parse one. Reach them through the nodes that
contain them:

- [PosixArithmeticExpansionSyntax.Expression](cref:Meziantou.Framework.Language.Shell.PosixArithmeticExpansionSyntax.Expression) for `$(( ))`;
- [PosixDelimitedExpressionStatementSyntax.Expression](cref:Meziantou.Framework.Language.Shell.PosixDelimitedExpressionStatementSyntax.Expression) for `(( ))` and `[[ ]]`;
- [PowerShellExpressionStatementSyntax.Expression](cref:Meziantou.Framework.Language.Shell.PowerShellExpressionStatementSyntax.Expression) for a PowerShell expression statement.

Precedence is in the tree: a binary expression holds its operands, which can be binary expressions themselves.

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxTreeTests.cs;region=ShellSyntaxTree_Expressions;format=md-fenced-code-block;dedent=true -->
```csharp
var arithmetic = ShellSyntaxTree.ParseText("echo $((1 + 2 * 3))", ShellDialect.Bash)
    .GetRoot().DescendantNodes().OfType<PosixArithmeticExpansionSyntax>().Single();

// 1 + (2 * 3): precedence is in the tree, not left to the caller.
var binary = (ShellBinaryExpressionSyntax)arithmetic.Expression;
Assert.Equal("+", binary.OperatorText);
Assert.IsType<ShellBinaryExpressionSyntax>(binary.Right);
```
<!-- endref -->

## Comments

Comments are trivia: they do not change the structure, and [DescendantComments()](cref:Meziantou.Framework.Language.Shell.ShellSyntaxNode.DescendantComments) returns them:

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

## Walk a script

[ShellSyntaxWalker](cref:Meziantou.Framework.Language.Shell.ShellSyntaxWalker) has a method per node type, such as [VisitCommand](cref:Meziantou.Framework.Language.Shell.ShellSyntaxVisitor.VisitCommand):

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxVisitorTests.cs;region=ShellSyntaxWalker_SimpleCommandCounter;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class SimpleCommandCounter : ShellSyntaxWalker
{
    public int Count { get; private set; }

    public override void VisitCommand(ShellCommandSyntax node)
    {
        Count++;
        base.VisitCommand(node);
    }
}
```
<!-- endref -->
