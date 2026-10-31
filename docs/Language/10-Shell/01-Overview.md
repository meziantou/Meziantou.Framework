---
title: Overview
description: Parse a shell script or a single command into a lossless tree, and read its diagnostics.
---

`Meziantou.Framework.Language.Shell` parses sh, bash, zsh, PowerShell and cmd scripts into a tree that keeps every
character, comments, blank lines and line continuations included. Use it to analyze the commands of a script, check a
CI step before running it, or rename a command without touching the rest.

It is a parser: it does not run anything, and does not expand variables.

<InstallNuGetPackage Id="Meziantou.Framework.Language.Shell" />

| Page | Covers |
| --- | --- |
| [Dialects](02-Dialects.md) | The shells, and what each one supports |
| [Reading scripts](03-ReadingScripts.md) | Commands, words, expressions and comments |
| [Editing scripts](04-EditingScripts.md) | Replacing nodes, quoting, building commands, and rewriting |

## Parse a script

[ShellSyntaxTree.ParseText](cref:Meziantou.Framework.Language.Shell.ShellSyntaxTree.ParseText) takes the script and its dialect. Invalid scripts give diagnostics, not exceptions:

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxTreeTests.cs;region=ShellSyntaxTree_Parsing;format=md-fenced-code-block;dedent=true -->
```csharp
const string Script = """
    # deploy the app
    set -euo pipefail

    for target in web api; do
      if [[ -d "src/$target" ]]; then
        dotnet publish "src/$target" -c Release | tee "logs/$target.log"
      fi
    done
    """;

var tree = ShellSyntaxTree.ParseText(Script, ShellDialect.Bash);

// Nothing is lost: the tree reproduces the input byte for byte.
Assert.Equal(Script, tree.GetRoot().ToFullString());
Assert.Empty(tree.GetDiagnostics());

// Invalid input produces diagnostics instead of exceptions.
var invalid = ShellSyntaxTree.ParseText("echo 'unterminated", ShellDialect.Bash);
var diagnostic = Assert.Single(invalid.GetDiagnostics());
Assert.Equal("SHELL0003 at (0,5)-(0,6): Unterminated single-quoted string.", $"{diagnostic.Id} at {diagnostic.Location}: {diagnostic.Message}");
```
<!-- endref -->

To read a single command, such as the value of a `run:` field, use [ParseCommand](cref:Meziantou.Framework.Language.Shell.ShellSyntaxTree.ParseCommand). It returns the statement rather than
a tree, and reports content after the first statement:

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxTreeTests.cs;region=ShellSyntaxTree_ParseCommand;format=md-fenced-code-block;dedent=true -->
```csharp
var command = (ShellCommandSyntax)ShellSyntaxTree.ParseCommand("git commit -m 'wip'", ShellDialect.Bash);

Assert.Equal("git", command.NameValue);
Assert.Equal("wip", command.Arguments[2].Value);
```
<!-- endref -->

## Limit the nesting

[ShellParseOptions.MaxRecursionDepth](cref:Meziantou.Framework.Language.Shell.ShellParseOptions.MaxRecursionDepth), 128 by default, bounds how deeply the parser goes into nested constructs, so
deeply nested input cannot overflow the stack. What nests deeper is reported as `SHELL0100` and kept as skipped text:

<!-- ref:../../../tests/Meziantou.Framework.Language.Shell.Tests/ShellSyntaxTreeTests.cs;region=ShellSyntaxTree_ParseOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var script = "echo $(echo $(echo nested))";
var options = new ShellParseOptions(ShellDialect.Bash) { MaxRecursionDepth = 64 };
var tree = ShellSyntaxTree.ParseText(script, options);

Assert.Empty(tree.GetDiagnostics());
```
<!-- endref -->

The limit bounds nesting, not length. A chain of operators or members, such as `$x.a.b.c…`, is accepted at any length;
building, walking and editing its tree do not recurse either.

## Diagnostics

A syntax error is reported, and parsing goes on at the next statement or at the keyword that closes the construct it is
in, so one mistake does not hide the rest of the script. The identifiers are shared by every dialect; the message says
what was found.

| Id | Reported for |
| --- | --- |
| `SHELL0001` | A missing command, as in `a \|` at the end of a line, or `if true; then fi` outside zsh |
| `SHELL0002` | An unexpected token, such as `fi` without `if`, a command right after `}` on the same line, or `;;` outside `case` |
| `SHELL0003` | An unterminated string |
| `SHELL0004` | A redirection without a target |
| `SHELL0005` to `SHELL0007`, `SHELL0010` | An unterminated `${`, `$(` or backquote, `$((` or `((`, `[[` |
| `SHELL0008`, `SHELL0009` | A missing `)` or `}` |
| `SHELL0011` | A here-document without its closing delimiter (a warning: shells read it to the end of the input) |
| `SHELL0012`, `SHELL0013` | A missing keyword or character, a missing name |
| `SHELL0014` | A function body that is not a compound command, outside zsh |
| `SHELL0015` | An invalid `[[ ]]` expression |
| `SHELL0020` to `SHELL0027` | PowerShell: block comments, member names, here-strings, missing expressions, `using` placement, reserved keywords, missing parameter arguments, invalid assignment targets |
| `SHELL0040`, `SHELL0041` | cmd: an incomplete `if` condition, a missing comparison operator |
| `SHELL0100`, `SHELL0101` | Nesting deeper than [MaxRecursionDepth](cref:Meziantou.Framework.Language.Shell.ShellParseOptions.MaxRecursionDepth); content after the statement [ParseCommand](cref:Meziantou.Framework.Language.Shell.ShellSyntaxTree.ParseCommand) read |
