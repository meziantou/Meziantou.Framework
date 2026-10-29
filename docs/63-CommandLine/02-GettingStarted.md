---
title: Getting started
description: Install the package, read named and positional arguments, and detect a request for help.
---

## Install the package

Add the `Meziantou.Framework.CommandLine` package to a project that targets .NET 10 or later:

```bash
dotnet add package Meziantou.Framework.CommandLine
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework;`.

## Read the arguments

`CommandLineParser.Current` holds the arguments of the current process. Parse another array, such as the arguments of a
test, with `Parse`:

<!-- ref:../../tests/Meziantou.Framework.CommandLineTests/CommandLineParserTests.cs;region=CommandLineParser_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Parse current process arguments
var currentParser = CommandLineParser.Current;
Assert.NotNull(currentParser);

// Or parse custom arguments
var parser = new CommandLineParser();
parser.Parse(["/name=John", "/verbose", "input.txt"]);

// Check if an argument exists
Assert.True(parser.HasArgument("verbose"));

// Get named argument value
Assert.Equal("John", parser.GetArgument("name"));

// Get positional argument (the index in the array of arguments)
Assert.Equal("input.txt", parser.GetArgument(2));

// Check if help was requested: detects -?, /?, -help, /help, --help
Assert.False(parser.HelpRequested);
```
<!-- endref -->

- `HasArgument` tells whether a named argument is present, with or without a value.
- `GetArgument(string)` returns the value of a named argument, and `GetArgument(int)` the argument at a position. Both
  return `null` when the argument does not exist.
- `HelpRequested` is `true` when one of `-?`, `/?`, `-help`, `/help` or `--help` is present.

`CommandLineParser.Current` does not include the path of the executable: position 0 is the first argument, as in the
`args` array of the entry point.

## Next steps

- [Parsing arguments](03-ParsingArguments.md): the argument formats and how positions are counted.
- [Quoting arguments](04-QuotingArguments.md): build a command line for another program.
- [Prompts](05-Prompts.md): ask the user to confirm an action.
