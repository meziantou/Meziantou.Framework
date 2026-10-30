---
title: Parsing arguments
description: The formats of named arguments, how positions are counted, and how help switches are detected.
---

## Named and positional arguments

<!-- ref:../../tests/Meziantou.Framework.CommandLineTests/CommandLineParserTests.cs;region=CommandLineParser_ArgumentFormats;format=md-fenced-code-block;dedent=true -->
```csharp
var parser = new CommandLineParser();
parser.Parse(["-output:result.txt", "/Level=3", "/force", "input.txt"]);

// Names are case-insensitive, and the value follows the first ':' or '='
Assert.Equal("result.txt", parser.GetArgument("OUTPUT"));
Assert.Equal("3", parser.GetArgument("level"));

// A named argument without a value has an empty value
Assert.True(parser.HasArgument("force"));
Assert.Equal("", parser.GetArgument("force"));

// Missing arguments return null
Assert.Null(parser.GetArgument("verbose"));
Assert.Null(parser.GetArgument(10));

// Every argument keeps its position, named arguments included (without their prefix)
Assert.Equal("input.txt", parser.GetArgument(3));
Assert.Equal("force", parser.GetArgument(2));
```
<!-- endref -->

An argument that starts with `-` or `/` is a named argument. The parser accepts these formats:

| Format | Name | Value |
| --- | --- | --- |
| `/name=value` or `-name=value` | `name` | `value` |
| `/name:value` or `-name:value` | `name` | `value` |
| `/name` or `-name` | `name` | an empty string |

- Names are case-insensitive: `GetArgument("OUTPUT")` finds `-output:result.txt`.
- The value starts after the first `:` or `=`, so it can contain these characters: `/path=C:\temp` has the value
  `C:\temp`. Spaces before the separator are removed from the name; the value is kept as is.
- When the same name appears several times, the last value wins.
- Any other argument is positional.

## Positions

`GetArgument(int)` returns the argument at an index of the array, whatever its kind. A named argument also has a
position: `GetArgument(2)` returns `force` for `/force`, without its prefix. Count the named arguments when you compute
the position of a positional argument, or put the positional arguments first.

Empty arguments are skipped but keep their index, so the positions of the following arguments match the array.

## Prefixes

The prefix is a single character, and `/` is a prefix on every operating system:

<!-- ref:../../tests/Meziantou.Framework.CommandLineTests/CommandLineParserTests.cs;region=CommandLineParser_Prefixes;format=md-fenced-code-block;dedent=true -->
```csharp
var parser = new CommandLineParser();
parser.Parse(["--verbose", "/tmp/file.txt"]);

// The prefix is a single '-' or '/': the second '-' is part of the name
Assert.False(parser.HasArgument("verbose"));
Assert.True(parser.HasArgument("-verbose"));

// A Unix absolute path starts with '/', so it is read as a named argument, and loses its '/' at its position too
Assert.True(parser.HasArgument("tmp/file.txt"));
Assert.Equal("tmp/file.txt", parser.GetArgument(1));
```
<!-- endref -->

- `--name` is the named argument `-name`, not `name`. `--help` is the only argument with two dashes that the parser
  recognizes.
- A Unix absolute path, such as `/tmp/file.txt`, is read as a named argument, and its position holds it without the
  leading `/`. An application that takes such paths cannot read them with `CommandLineParser`: read them from the
  `args` array of the entry point.

## Help switches

<!-- ref:../../tests/Meziantou.Framework.CommandLineTests/CommandLineParserTests.cs;region=CommandLineParser_HelpArguments;format=md-fenced-code-block;dedent=true -->
```csharp
var parser = new CommandLineParser();
parser.Parse(["--help"]);

// Help arguments set HelpRequested, and are not named or positional arguments
Assert.True(parser.HelpRequested);
Assert.False(parser.HasArgument("help"));
Assert.Null(parser.GetArgument(0));
```
<!-- endref -->

`-?`, `/?`, `-help`, `/help` and `--help`, in any case, set `HelpRequested`. They are neither named nor positional
arguments: `HasArgument("help")` is `false` and their position is empty.

## The arguments of the current process

`CommandLineParser.Current` parses `Environment.GetCommandLineArgs()` without its first element, the path of the
executable, once. Its positions match the `args` array of the entry point.
