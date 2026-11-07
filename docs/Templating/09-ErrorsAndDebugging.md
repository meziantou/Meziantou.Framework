---
title: Errors and debugging
description: Find the cause of compilation and runtime errors in a template, and inspect the code generated from it.
---

## Compilation errors

When the code of a template does not compile, [Build](cref:Meziantou.Framework.Templating.Template.Build) (or the first [Run](cref:Meziantou.Framework.Templating.Template.Run)) throws a [TemplateException](cref:Meziantou.Framework.Templating.TemplateException). Its message
lists the diagnostics of the C# compiler:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_ErrorHandling;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("<%=InvalidExpression%>");

var exception = Assert.Throws<TemplateException>(() => template.Build(CancellationToken.None));
Assert.Contains("InvalidExpression", exception.Message);
```
<!-- endref -->

The positions of the diagnostics are positions in the template, not in the generated code. Set [SourceFileName](cref:Meziantou.Framework.Templating.Template.SourceFileName) to the
path of the template file: the diagnostics start with it, followed by the line and the column of the error, like the
errors of a C# file:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_SourceFileName;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template { SourceFileName = "welcome.txt" };
template.Load("""
    Hello
    <%= Nmae %>
    """);

var exception = Assert.Throws<TemplateException>(() => template.Build(CancellationToken.None));
Assert.Contains("welcome.txt(2,5)", exception.Message);
Assert.Contains("CS0103", exception.Message);
```
<!-- endref -->

## Errors at run time

An exception thrown by the code of a template reaches the caller of [Run](cref:Meziantou.Framework.Templating.Template.Run) wrapped in a `TargetInvocationException`,
because the compiled code is invoked through reflection. The original exception is its `InnerException`:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_RuntimeError;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("<% throw new InvalidOperationException(\"Out of stock\"); %>");

var exception = Assert.Throws<TargetInvocationException>(() => template.Run());
var innerException = Assert.IsType<InvalidOperationException>(exception.InnerException);
Assert.Equal("Out of stock", innerException.Message);
```
<!-- endref -->

When [SourceFileName](cref:Meziantou.Framework.Templating.Template.SourceFileName) is set, the stack trace of the inner exception gives the lines of the template file.

The library throws a [TemplateException](cref:Meziantou.Framework.Templating.TemplateException) itself in a few cases: a missing argument when [ThrowOnMissingArgument](cref:Meziantou.Framework.Templating.Template.ThrowOnMissingArgument) is set
(see [Passing arguments](04-PassingArguments.md#pass-values-by-name)), and a section that is never ended in an HTML email
(see [Subject and embedded images](06-HtmlEmails/03-SubjectAndEmbeddedImages.md#capture-the-subject)).

## Inspect the generated code

[SourceCode](cref:Meziantou.Framework.Templating.Template.SourceCode) returns the C# code generated from the template. It is set when the template is built, including when the
build fails, so you can read the code that the diagnostics refer to:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_SourceCode;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("Hello <%=Name%>");
template.Arguments.Add(new TemplateArgument("Name", typeof(string)));
template.Build(CancellationToken.None);

// The generated C# class
var sourceCode = template.SourceCode;
Assert.Contains("public static void Run(dynamic __output__, System.String Name)", sourceCode);
```
<!-- endref -->

## Debug a template

Set [Debug](cref:Meziantou.Framework.Templating.Template.Debug) to compile the template without optimizations, and with the `DEBUG` symbol instead of `RELEASE`, so that
`#if DEBUG` blocks of the template are compiled:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_Debug;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template
{
    Debug = true,
};
template.Load("<%=Value%>");
template.Arguments.Add(new TemplateArgument("Value", typeof(int)));
var result = template.Run(42);
Assert.Equal("42", result);
```
<!-- endref -->

Debug symbols are generated whatever the value of [Debug](cref:Meziantou.Framework.Templating.Template.Debug), and they map the generated code to the lines of the
template.
