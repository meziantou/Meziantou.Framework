---
title: Getting started
description: Install the package, render a template with an argument, loop in a template and reuse a compiled template.
---

## Install the package

Add the `Meziantou.Framework.Templating` package to a project:

```bash
dotnet add package Meziantou.Framework.Templating
```

The examples of this documentation omit the `using` directives: add `using Meziantou.Framework.Templating;`. They are
tests: an `Assert.Equal` shows the text the template produces.

## Render a template

Load the template, declare its arguments, and run it with their values:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_Parameters;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("Hello <%=Name%>!");
template.Arguments.Add(new TemplateArgument("Name", typeof(string)));
var result = template.Run("Meziantou");
Assert.Equal("Hello Meziantou!", result);
```
<!-- endref -->

- [Load](cref:Meziantou.Framework.Templating.Template.Load) reads the template. `<%= Name %>` writes the value of the C# expression `Name`; the rest is text, written as
  is.
- [Arguments](cref:Meziantou.Framework.Templating.Template.Arguments) declares the values the template receives. Each argument becomes a C# parameter, with its name and its
  type.
- [Run](cref:Meziantou.Framework.Templating.Template.Run) compiles the template the first time it is called, runs it, and returns the text. The values are passed in the
  order of the arguments.
- [Template](cref:Meziantou.Framework.Templating.Template) is disposable: disposing it unloads the compiled code. See
  [Compilation and memory](08-CompilationAndMemory.md).

## Loop and test conditions

The code of `<% ... %>` blocks runs as C# statements, so the text between them can be repeated or skipped:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_StatementBlock;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("Numbers: <% for(int i = 1; i <= 5; i++) { %><%= i %><% } %>");
var result = template.Run();
Assert.Equal("Numbers: 12345", result);
```
<!-- endref -->

The other blocks, and how a template handles line breaks, are described in [Template syntax](03-TemplateSyntax.md).

## Reuse a compiled template

Compiling a template takes much longer than running it. Keep the [Template](cref:Meziantou.Framework.Templating.Template) instance and run it as many times as you
need; [Build](cref:Meziantou.Framework.Templating.Template.Build) compiles it in advance, for instance when your application starts:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_Build;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("Hello <%=Name%>");
template.Arguments.Add(new TemplateArgument("Name", typeof(string)));

// Compile the template
template.Build(CancellationToken.None);

// Run multiple times (compiled code is reused)
var result1 = template.Run("Alice");
var result2 = template.Run("Bob");
Assert.Equal("Hello Alice", result1);
Assert.Equal("Hello Bob", result2);
```
<!-- endref -->

## Next steps

- [Template syntax](03-TemplateSyntax.md) and [Passing arguments](04-PassingArguments.md).
- [HTML emails](06-HtmlEmails/01-Overview.md): encode values and get the subject of an email.
- [Command-line tool](07-CommandLineTool.md): render a template file without writing code.
