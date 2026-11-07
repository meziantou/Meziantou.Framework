---
title: Compilation and memory
description: Compile a template once, run it from several threads, and unload the compiled code when you no longer need it.
---

## When a template is compiled

[Build](cref:Meziantou.Framework.Templating.Template.Build) compiles the template: it generates a C# class from the blocks, compiles it with the C# compiler and loads the
resulting assembly. [Run](cref:Meziantou.Framework.Templating.Template.Run) calls [Build](cref:Meziantou.Framework.Templating.Template.Build) the first time, so you only call [Build](cref:Meziantou.Framework.Templating.Template.Build) to compile in advance, for instance
when your application starts, or to report the errors of a template before using it:

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

Compiling takes much longer than running: keep the [Template](cref:Meziantou.Framework.Templating.Template) instance and run it as many times as you need, instead of
loading the same text again.

Once the template is built:

- [Load](cref:Meziantou.Framework.Templating.Template.Load) throws an `InvalidOperationException`, and so does any change to [Arguments](cref:Meziantou.Framework.Templating.Template.Arguments), [Usings](cref:Meziantou.Framework.Templating.Template.Usings),
  [AssemblyReferences](cref:Meziantou.Framework.Templating.Template.AssemblyReferences), [IncludedSourceFiles](cref:Meziantou.Framework.Templating.Template.IncludedSourceFiles), [ImplementedInterfaces](cref:Meziantou.Framework.Templating.Template.ImplementedInterfaces) and [Blocks](cref:Meziantou.Framework.Templating.Template.Blocks). To change a template, create a
  new [Template](cref:Meziantou.Framework.Templating.Template).
- When the build fails, the template is not built: correct the text, call [Load](cref:Meziantou.Framework.Templating.Template.Load) again, and build again.

## Run from several threads

A template can be run from several threads at once. Each run writes to its own writer, and when several threads run a
template that is not built yet, it is compiled once.

## Unload the compiled code

Each template is compiled into its own assembly, loaded in a collectible `AssemblyLoadContext` that the template owns.
Dispose the template to unload that assembly; otherwise, it is unloaded once the template becomes unreachable and the
garbage collector runs. A disposed template cannot be built or run again: it throws an `ObjectDisposedException`.

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_Dispose;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template { OutputType = typeof(Output) };
template.Load("Hello <%=Name%>!");
template.Arguments.Add(new TemplateArgument("Name", typeof(string)));
var result = template.Run("Meziantou");
Assert.Equal("Hello Meziantou!", result);
```
<!-- endref -->

This matters for applications that compile templates as they run, such as a server that compiles one template per
tenant, per file, or on every change of a file. A process that compiles a few templates and exits does not need to
dispose them.

## Avoid dynamic code

A template that uses `dynamic` is **never** unloaded, even when disposed: running a `dynamic` call registers the type
that contains it in a process-wide cache of the C# runtime binder, which keeps the generated assembly alive until the
process exits. A template uses `dynamic` when:

- [OutputType](cref:Meziantou.Framework.Templating.Template.OutputType) is `null`, which is the default;
- an argument is declared without a type;
- its code blocks use `dynamic` themselves.

Set [OutputType](cref:Meziantou.Framework.Templating.Template.OutputType) and give every argument a type to get a template that can be unloaded:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_Unloadable;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template { OutputType = typeof(Output) };
template.Load("Hello <%=Name%>!");
template.Arguments.Add(new TemplateArgument("Name", typeof(string)));
```
<!-- endref -->

[OutputType](cref:Meziantou.Framework.Templating.Template.OutputType) is the type of the output that the template writes to. Use [Output](cref:Meziantou.Framework.Templating.Output) with [Template](cref:Meziantou.Framework.Templating.Template), and
[HtmlEmailOutput](cref:Meziantou.Framework.Templating.HtmlEmailOutput) with [HtmlEmailTemplate](cref:Meziantou.Framework.Templating.HtmlEmailTemplate) (see [HTML emails](06-HtmlEmails/01-Overview.md#use-it-in-a-server)).
Declaring the types is worth it on its own: the template binds its calls when it is compiled, so it runs faster and
reports errors when it is built instead of when it runs.
