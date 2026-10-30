---
title: Introduction
description: Generate text, source code and HTML emails from templates whose code blocks are C#.
---

`Meziantou.Framework.Templating` turns a text template into text. You write the text as it must appear, and put C# code
in code blocks (`<%= ... %>` and `<% ... %>` by default). The library compiles the template with the C# compiler into an
assembly, then runs it with the arguments you pass.

## Packages

| Package | Use it to |
| --- | --- |
| `Meziantou.Framework.Templating` | Load, compile and run templates in your application |
| `Meziantou.Framework.Templating.Html` | Generate HTML emails: encode values, capture the subject, list the embedded images |
| `Meziantou.Framework.Templating.Tool` | Render a template file from the command line |

## What you can do with it

- **Generate any text**: source code, configuration files, reports, with loops and conditions written in C#. See
  [Template syntax](03-TemplateSyntax.md).
- **Pass typed arguments**, including your own types, by position or by name. See
  [Passing arguments](04-PassingArguments.md).
- **Use .NET libraries** from a template: import namespaces, reference assemblies, include C# files and share helpers
  through a base class. See [Using code and libraries](05-UsingCodeAndLibraries.md).
- **Write HTML emails** that encode every value for its context, and get the subject and the embedded images of the
  message. See [HTML emails](06-HtmlEmails/01-Overview.md).
- **Generate files from scripts and builds** with the command-line tool, which also reads T4-style templates. See
  [Command-line tool](07-CommandLineTool.md).

## Principles

- **A template is a C# program.** Its code runs in your process, with the permissions of your application. Only run
  templates that you trust: never compile a template written by a user of your application.
- **Compile once, run many times.** Compiling is the expensive step and happens once per template; each run only
  executes the compiled code. See [Compilation and memory](08-CompilationAndMemory.md).
- **Errors point to the template.** Compiler errors and the stack traces of exceptions give the lines of the template,
  not of the generated code. See [Errors and debugging](09-ErrorsAndDebugging.md).
- **You control the memory.** Each template is compiled into its own assembly, which is unloaded when you dispose the
  template, as long as the template declares its types.

## Namespace

Every public type is in the [Meziantou.Framework.Templating](cref:Meziantou.Framework.Templating!N:Meziantou.Framework.Templating) namespace, including the HTML email types of
`Meziantou.Framework.Templating.Html`.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and render your first template.
- [Template syntax](03-TemplateSyntax.md): the blocks a template is made of.
- [Limitations](10-Limitations.md): what the library does not do.
