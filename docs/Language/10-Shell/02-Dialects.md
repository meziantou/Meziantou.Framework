---
title: Dialects
description: Choose the shell a script is written for, and learn what each one supports.
---

A script is always parsed in a dialect. Pass one of the [ShellDialect](cref:Meziantou.Framework.Language.Shell.ShellDialect) values to [ParseText](cref:Meziantou.Framework.Language.Shell.ShellSyntaxTree.ParseText) or [ParseCommand](cref:Meziantou.Framework.Language.Shell.ShellSyntaxTree.ParseCommand), or in
[ShellParseOptions](cref:Meziantou.Framework.Language.Shell.ShellParseOptions):

| [ShellDialect](cref:Meziantou.Framework.Language.Shell.ShellDialect) | [Family](cref:Meziantou.Framework.Language.Shell.ShellDialect.Family) | Reads |
| --- | --- | --- |
| [Sh](cref:Meziantou.Framework.Language.Shell.ShellDialect.Sh) | [Posix](cref:Meziantou.Framework.Language.Shell.ShellDialectFamily.Posix) | POSIX shell scripts, strictly |
| [Bash](cref:Meziantou.Framework.Language.Shell.ShellDialect.Bash) | [Posix](cref:Meziantou.Framework.Language.Shell.ShellDialectFamily.Posix) | bash: `[[ ]]`, `(( ))`, `<<<`, arrays, `function`, `<(…)`, `coproc`, `select`, arithmetic `**` |
| [Zsh](cref:Meziantou.Framework.Language.Shell.ShellDialect.Zsh) | [Posix](cref:Meziantou.Framework.Language.Shell.ShellDialectFamily.Posix) | zsh: the bash set, plus `foreach`/`end`, `repeat`, `always`, anonymous functions, `=(…)`, glob qualifiers, and brace groups that close without a separator |
| [PowerShell](cref:Meziantou.Framework.Language.Shell.ShellDialect.PowerShell) | [PowerShell](cref:Meziantou.Framework.Language.Shell.ShellDialect.PowerShell) | Windows PowerShell 5.1 |
| [PowerShellCore](cref:Meziantou.Framework.Language.Shell.ShellDialect.PowerShellCore) | [PowerShell](cref:Meziantou.Framework.Language.Shell.ShellDialect.PowerShell) | PowerShell 7 and later: `&&` and `\|\|`, the `? :` ternary, `??` and `??=`, `clean` blocks |
| [Cmd](cref:Meziantou.Framework.Language.Shell.ShellDialect.Cmd) | [Cmd](cref:Meziantou.Framework.Language.Shell.ShellDialect.Cmd) | cmd.exe batch files |

The dialects of a family share a parser. [Features](cref:Meziantou.Framework.Language.Shell.ShellDialect.Features) lists what a dialect supports, as [ShellDialectFeatures](cref:Meziantou.Framework.Language.Shell.ShellDialectFeatures) flags, and
[HasFeature](cref:Meziantou.Framework.Language.Shell.ShellDialect.HasFeature) tests one. [ShellDialect.TryParse](cref:Meziantou.Framework.Language.Shell.ShellDialect.TryParse) reads a dialect from its name, and [Name](cref:Meziantou.Framework.Language.Shell.ShellDialect.Name) gives it:

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxTreeTests.cs;region=ShellSyntaxTree_Dialects;format=md-fenced-code-block;dedent=true -->
```csharp
var powerShell = ShellSyntaxTree.ParseText("Get-ChildItem -Recurse | Where-Object { $_.Length -gt 1kb }", ShellDialect.PowerShellCore);
Assert.Empty(powerShell.GetDiagnostics());

var cmd = ShellSyntaxTree.ParseText("@echo off\r\nif exist out (rmdir /s /q out)\r\n", ShellDialect.Cmd);
Assert.Empty(cmd.GetDiagnostics());

// [[ ]] is bash and zsh only: in sh, it is an ordinary command name
Assert.True(ShellDialect.Bash.HasFeature(ShellDialectFeatures.ExtendedTest));
Assert.False(ShellDialect.Sh.HasFeature(ShellDialectFeatures.ExtendedTest));

// Read a dialect from its name
Assert.True(ShellDialect.TryParse("pwsh", out var dialect));
Assert.Equal(ShellDialect.PowerShellCore, dialect);
```
<!-- endref -->

A construct is parsed as one only in the dialects that have it. POSIX defines `$((1+2))`, so every POSIX dialect parses
it as an arithmetic expansion, while the `((1+2))` arithmetic command is bash and zsh only, and stays plain text in sh.

## One tree for every dialect

Every dialect builds the same kind of tree, with node types shared where the shells agree, such as [ShellCommandSyntax](cref:Meziantou.Framework.Language.Shell.ShellCommandSyntax),
[ShellPipelineSyntax](cref:Meziantou.Framework.Language.Shell.ShellPipelineSyntax) or [ShellWordSyntax](cref:Meziantou.Framework.Language.Shell.ShellWordSyntax), and node types of their own where they do not:

| Prefix | Nodes |
| --- | --- |
| `Shell…` | Shared by every dialect: commands, pipelines, command lists, words, quoted strings, redirections, assignments, expressions |
| `Posix…` | sh, bash and zsh: `for`, `while`, `case`, functions, here-documents, arithmetic expansions, process substitutions, arrays |
| `Zsh…` | zsh only: `foreach` |
| `PowerShell…` | PowerShell: functions, types, attributes, member access, sub-expressions, `switch` clauses, `do` loops |
| `Cmd…` | cmd: `if`, `for`, `goto`, labels, `set`, parenthesized blocks, `%variable%` references |

[ShellSyntaxVisitor](cref:Meziantou.Framework.Language.Shell.ShellSyntaxVisitor), [ShellSyntaxWalker](cref:Meziantou.Framework.Language.Shell.ShellSyntaxWalker) and [ShellSyntaxRewriter](cref:Meziantou.Framework.Language.Shell.ShellSyntaxRewriter) cover the node types of every dialect, so one walker
handles any script.
