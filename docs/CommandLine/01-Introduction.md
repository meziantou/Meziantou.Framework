---
title: Introduction
description: Three small helpers for console applications, an argument parser, an argument quoter for Windows, and a yes/no prompt.
---

`Meziantou.Framework.CommandLine` contains three small helpers for console applications. It is not a framework for
command-line applications: there are no commands, no type conversion and no generated help. Use it when you need to read
a few arguments, or build a command line for another program, without adding more structure to your application.

## What you can do with it

- **Read arguments**: named arguments such as `/name=value` or `-verbose`, positional arguments, and the standard help
  switches. See [Parsing arguments](03-ParsingArguments.md).
- **Quote arguments for Windows**: build a command line that a Windows program, or `cmd.exe`, splits back into the
  original arguments, whatever characters they contain. See [Quoting arguments](04-QuotingArguments.md).
- **Ask yes/no questions** in the console, with an optional default answer. See [Prompts](05-Prompts.md).

## Namespace

All the types are in the [Meziantou.Framework](cref:Meziantou.Framework.CommandLine!N:Meziantou.Framework) namespace:

| Type | Contains |
| --- | --- |
| [CommandLineParser](cref:Meziantou.Framework.CommandLineParser) | The parser and the arguments it read |
| [CommandLineBuilder](cref:Meziantou.Framework.CommandLineBuilder) | The methods that quote and escape arguments for Windows programs and `cmd.exe` |
| [Prompt](cref:Meziantou.Framework.Prompt) | The yes/no prompt |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and read the arguments of your application.
