---
title: Passing arguments
description: Declare the arguments of a template and pass them by position or by name, including values of your own types.
---

A template receives values through arguments. Each argument is a parameter of the generated C# method: declare it with
its name and its type, then pass the values when you run the template.

## Pass values by position

`Run(params object?[])` passes the values in the order of [Arguments](cref:Meziantou.Framework.Templating.Template.Arguments):

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_Parameters;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("Hello <%=Name%>!");
template.Arguments.Add(new TemplateArgument("Name", typeof(string)));
var result = template.Run("Meziantou");
Assert.Equal("Hello Meziantou!", result);
```
<!-- endref -->

The number and the types of the values must match the arguments: otherwise, the call throws the exception of the .NET
reflection API (`TargetParameterCountException`, `ArgumentException`).

## Pass values by name

`Run(IReadOnlyDictionary<string, object?>)` matches the values to the arguments by name, with the comparer of the
dictionary. The keys that match no argument are ignored:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_NamedParameters;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("Hello <%=Name%>!");
var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
{
    { "Name", "Meziantou" },
};
foreach (var argument in arguments)
{
    template.Arguments.Add(new TemplateArgument(argument.Key, argument.Value?.GetType()));
}

var result = template.Run(arguments);
Assert.Equal("Hello Meziantou!", result);
```
<!-- endref -->

An argument that has no value in the dictionary gets the default value of its type (`null`, `0`, `false`, …). Set
[ThrowOnMissingArgument](cref:Meziantou.Framework.Templating.Template.ThrowOnMissingArgument) to get a [TemplateException](cref:Meziantou.Framework.Templating.TemplateException) that names the argument instead, so that a typo in a key does not
silently produce a wrong document:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_ThrowOnMissingArgument;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template { ThrowOnMissingArgument = true };
template.Arguments.Add(new TemplateArgument("Name", typeof(string)));
template.Load("Hello <%=Name%>!");

var exception = Assert.Throws<TemplateException>(() => template.Run(new Dictionary<string, object?>(StringComparer.Ordinal)));
Assert.Equal("No value was provided for the argument 'Name'.", exception.Message);
```
<!-- endref -->

A `null` value in the dictionary is a value: it does not throw.

## Pass values of your own types

The template is compiled on its own, so it only knows the types of the assemblies it references. To pass a value of a
type of your application, reference the assembly that declares the type:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_InvoiceType;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed record Invoice(string Customer, int ItemCount);
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_ModelArgument;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.AssemblyReferences.Add(typeof(Invoice));
template.Arguments.Add(new TemplateArgument("Invoice", typeof(Invoice)));
template.Load("Invoice for <%= Invoice.Customer %>: <%= Invoice.ItemCount %> items");

var result = template.Run(new Invoice("Contoso", 3));
Assert.Equal("Invoice for Contoso: 3 items", result);
```
<!-- endref -->

The generated code refers to the type of an argument by its full name, so the type must be:

- **public**, because the generated method is public;
- **not nested** in another type and **not generic**, because their full names (`Outer+Inner`, ``List`1[...]``) are not
  valid C#. Wrap such values in a type of your own, whose properties can have any type.

`string`, the numeric types, `DateTime`, arrays of them and the other types of the core library need no reference.

## Arguments without a type

An argument declared without a type is `dynamic`: the template accepts any value, and resolves its members when it
runs:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_DynamicParameters;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("Value: <%=Value%>");
template.Arguments.Add(new TemplateArgument("Value", type: null)); // No type specified = dynamic
var result = template.Run(42);
Assert.Equal("Value: 42", result);
```
<!-- endref -->

Prefer typed arguments. With `dynamic`, a mistake in a member name is reported when the template runs instead of when it
is compiled, each call is slower, and the compiled template can never be unloaded. See
[Compilation and memory](08-CompilationAndMemory.md).
