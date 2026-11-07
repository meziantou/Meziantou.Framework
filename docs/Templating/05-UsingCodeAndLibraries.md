---
title: Using code and libraries
description: Import namespaces, reference assemblies, include C# files and share helpers between templates.
---

A template is compiled on its own, with a small set of references and namespaces. This page shows how to give it access
to more code. Each setting exists as a property of [Template](cref:Meziantou.Framework.Templating.Template), for the code that loads the template, and as a directive,
for the author of the template.

## Import namespaces

Templates import `System`, `System.Collections.Generic`, `System.Linq`, `System.Text` and `System.Threading.Tasks` by
default:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_DefaultUsings;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("<%= Enumerable.Range(1, 5).Sum() %>");
var result = template.Run();
Assert.Equal("15", result);
```
<!-- endref -->

Add a namespace to [Usings](cref:Meziantou.Framework.Templating.Template.Usings) to use its types without their full name. Importing a namespace that is already imported has
no effect:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_Usings;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Usings.Add("System.IO");
template.Load("<%= Path.GetExtension(\"report.txt\") %>");
var result = template.Run();
Assert.Equal(".txt", result);
```
<!-- endref -->

An entry can also declare an alias:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_UsingAlias;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Usings.Add("SB = System.Text.StringBuilder");
template.Load("<% var sb = new SB(); sb.Append(\"Hello\"); %><%= sb.ToString() %>");
var result = template.Run();
Assert.Equal("Hello", result);
```
<!-- endref -->

In the template, the `using` directive does the same:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_UsingDirective;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("""
    <%@ using System.IO %>
    <%= Path.GetFileNameWithoutExtension("report.txt") %>
    """);

var result = template.Run();
Assert.Equal("report", result);
```
<!-- endref -->

## Reference assemblies

By default, a template references the core library, LINQ, the collections, the console, the
`Meziantou.Framework.Templating` assembly and the assembly of its [OutputType](cref:Meziantou.Framework.Templating.Template.OutputType). To use the types of another assembly,
add it to [AssemblyReferences](cref:Meziantou.Framework.Templating.Template.AssemblyReferences), from a type, an assembly, a module or a path. An alias declares an `extern alias`:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_AssemblyReferences;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.AssemblyReferences.Add(typeof(string));
template.AssemblyReferences.Add(typeof(Template).Assembly, "TemplatingAlias");
template.AssemblyReferences.Add(typeof(Template).Module);
template.AssemblyReferences.Add(@"c:\libs\other-library.dll");
template.IncludedSourceFiles.Add(@"c:\templates\shared-helpers.cs");
```
<!-- endref -->

[IncludedSourceFiles](cref:Meziantou.Framework.Templating.Template.IncludedSourceFiles) adds C# files to the compilation of the template. In the template, the `reference` and `include`
directives do the same; the value of `reference` can start with `alias=Name`:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_Directives;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("""
    <%@ reference alias=MyAlias c:\libs\my-library.dll %>
    <%@ include c:\templates\shared-helpers.cs %>
    """);

var directives = template.Blocks.OfType<DirectiveBlock>().ToList();
Assert.Equal("reference", directives[0].Name);
Assert.Equal(@"alias=MyAlias c:\libs\my-library.dll", directives[0].Value);
Assert.Equal("include", directives[1].Name);
Assert.Equal(@"c:\templates\shared-helpers.cs", directives[1].Value);
```
<!-- endref -->

- The paths are read when the template is compiled. A relative path is resolved from the current directory of the
  process: prefer full paths.
- An included file is compiled as C#, not as a template: it can declare helper classes for the template, but it writes
  nothing.
- The types of arguments need a reference too. See [Passing arguments](04-PassingArguments.md#pass-values-of-your-own-types).

## Share helpers between templates

A member block (`<%+ ... %>`) declares a helper for one template. To share helpers between templates, declare them in a
public base class:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_ReportTemplateBase;format=md-fenced-code-block;dedent=true -->
```csharp
public abstract class ReportTemplateBase
{
    protected static string Bullet(string text) => "- " + text;
}
```
<!-- endref -->

Set it as the base class of the generated class, and reference its assembly:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_BaseClass;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.AssemblyReferences.Add(typeof(ReportTemplateBase));
template.BaseClassFullTypeName = typeof(ReportTemplateBase).FullName;
template.Load("<%= Bullet(\"Apples\") %>");

var result = template.Run();
Assert.Equal("- Apples", result);
```
<!-- endref -->

- The template code runs in a static method: the helpers must be `static`, and can be `protected`.
- The base class must be public, not nested and not generic, for the same reason as the
  [types of arguments](04-PassingArguments.md#pass-values-of-your-own-types).
- In the template, the `inherits` directive sets the base class, and `implements` adds interfaces to the generated
  class ([ImplementedInterfaces](cref:Meziantou.Framework.Templating.Template.ImplementedInterfaces)).

## Read your own directives

A directive that is not built in has no effect, but it is kept in [Blocks](cref:Meziantou.Framework.Templating.Template.Blocks) with its name and its value. Use it to store
settings in the template itself, such as the extension of the file to generate:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_CustomDirective;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("""
    <%@ outputextension .cs %>
    public class Generated { }
    """);

var directive = template.Blocks.OfType<DirectiveBlock>()
    .Single(block => string.Equals(block.Name, "outputextension", StringComparison.OrdinalIgnoreCase));
Assert.Equal(".cs", directive.Value);
Assert.Equal("public class Generated { }", template.Run());
```
<!-- endref -->

The built-in directives are applied when the template is compiled. To give a directive of your own an effect at that
time, derive from [Template](cref:Meziantou.Framework.Templating.Template), override [CreateDirectiveBlock](cref:Meziantou.Framework.Templating.Template.CreateDirectiveBlock), and return a [DirectiveBlock](cref:Meziantou.Framework.Templating.DirectiveBlock) that overrides
[ApplyDirective](cref:Meziantou.Framework.Templating.DirectiveBlock.ApplyDirective).
