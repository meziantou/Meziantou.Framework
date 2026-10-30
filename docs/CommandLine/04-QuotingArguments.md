---
title: Quoting arguments
description: Build a command line that a Windows program or cmd.exe splits back into the original arguments.
---

A Windows program receives its command line as a single string, which it splits into arguments. An argument that
contains a space, a quote or a backslash before a quote must be quoted and escaped, or the program receives different
arguments. `cmd.exe` adds its own special characters, such as `&` and `|`: an unescaped `&` runs a second command.

## Quote arguments

<!-- ref:../../tests/Meziantou.Framework.CommandLineTests/CommandLineBuilderTests.cs;region=CommandLineBuilder_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Quote a single argument for standard Windows applications
var arg = CommandLineBuilder.WindowsQuotedArgument(@"path with spaces\file.txt");
Assert.Equal(@"""path with spaces\file.txt""", arg);

// Quote multiple arguments
var args = CommandLineBuilder.WindowsQuotedArguments("arg1", "path with spaces", "normal");
Assert.Equal(@"arg1 ""path with spaces"" normal", args);

// Quote for cmd.exe (handles special characters like &, |, ^, etc.)
var cmdArg = CommandLineBuilder.WindowsCmdArgument(@"malicious argument"" & whoami");
Assert.Equal(@"^""malicious argument\^"" ^& whoami^""", cmdArg);

var cmdArgs = CommandLineBuilder.WindowsCmdArguments("echo", "Hello & Goodbye");
Assert.Equal(@"echo ^""Hello ^& Goodbye^""", cmdArgs);
```
<!-- endref -->

- `WindowsQuotedArgument` quotes one argument for a program that splits its command line with the standard Windows
  rules, and `WindowsQuotedArguments` quotes several and joins them with spaces.
- `WindowsCmdArgument` and `WindowsCmdArguments` also escape the characters that `cmd.exe` interprets: `(`, `)`, `%`,
  `!`, `^`, `"`, `<`, `>`, `&` and `|`. Use them when the command line goes through `cmd.exe`, such as a batch file or
  `cmd.exe /c`.

`WindowsQuotedArgument(null)` and `WindowsCmdArgument(null)` return `null`.

## The escaping rules

<!-- ref:../../tests/Meziantou.Framework.CommandLineTests/CommandLineBuilderTests.cs;region=CommandLineBuilder_EscapingRules;format=md-fenced-code-block;dedent=true -->
```csharp
// An argument without spaces, tabs, line feeds or quotes is returned as is
Assert.Equal("simple", CommandLineBuilder.WindowsQuotedArgument("simple"));

// An empty argument is quoted, so the program still receives it
Assert.Equal(@"""""", CommandLineBuilder.WindowsQuotedArgument(""));

// Quotes are escaped with a backslash
Assert.Equal(@"""say \""hi\""""", CommandLineBuilder.WindowsQuotedArgument(@"say ""hi"""));

// Backslashes are doubled only when they precede a quote, including the closing quote
Assert.Equal(@"""C:\Program Files\\""", CommandLineBuilder.WindowsQuotedArgument(@"C:\Program Files\"));
```
<!-- endref -->

- An argument is quoted only when it is empty or contains a space, a tab, a line feed, a vertical tab or a quote.
- Inside quotes, a quote is preceded by a backslash. Backslashes are doubled only when they precede a quote, including
  the closing quote, so a folder path that ends with a backslash keeps its last backslash.

For `cmd.exe`, every special character is prefixed with `^`, including the quotes added around the argument:

<!-- ref:../../tests/Meziantou.Framework.CommandLineTests/CommandLineBuilderTests.cs;region=CommandLineBuilder_CmdEscaping;format=md-fenced-code-block;dedent=true -->
```csharp
// cmd.exe special characters are prefixed with ^, including the quotes that delimit the argument
Assert.Equal(@"^""100^% ^(tested^)^""", CommandLineBuilder.WindowsCmdArgument("100% (tested)"));

// An argument without any special character is returned as is
Assert.Equal("report.txt", CommandLineBuilder.WindowsCmdArgument("report.txt"));
```
<!-- endref -->

## When to use it

Use these methods when an API takes the whole command line as one string: a `ProcessStartInfo.Arguments` value, a
command line written to a script or a configuration file, or a command line shown to the user.

The rules are the ones of Windows. On Linux and macOS, a process receives an array of arguments and no quoting is
needed: pass the arguments one by one, for instance with `ProcessStartInfo.ArgumentList`. To run processes, the
`Meziantou.Framework.ProcessWrapper` package takes the arguments as a list: see
[Getting started with ProcessWrapper](../ProcessWrapper/02-GettingStarted.md).
