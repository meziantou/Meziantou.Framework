---
title: Template syntax
description: Write text, values, statements, members and directives in a template, control line breaks and change the delimiters.
---

A template is text with code blocks. By default, a code block starts with `<%` and ends with `%>`; the character after
`<%` gives its kind:

| Block | Purpose | Example |
| --- | --- | --- |
| Text | Written to the output as is | `Hello World` |
| `<%= expression %>` | Writes the value of a C# expression | `<%= 2 + 2 %>` |
| `<% statements %>` | Runs C# statements | `<% var x = 10; %>` |
| `<%+ member %>` | Declares a member of the generated class | `<%+ private static int Get() => 42; %>` |
| `<%@ name value %>` | A directive: imports a namespace, references an assembly, … | `<%@ using System.IO %>` |

The code is C#, compiled with the latest version of the language that the compiler supports.

## Write a value

`<%= expression %>` writes the value of the expression:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_EvaluationBlock;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("2 + 2 = <%= 2 + 2 %>");
var result = template.Run();
Assert.Equal("2 + 2 = 4", result);
```
<!-- endref -->

- A `null` value writes nothing.
- Nothing is encoded: a value that contains `<` or `&` is written as is. To generate HTML, use
  [HTML emails](06-HtmlEmails/01-Overview.md), whose blocks encode values.
- Numbers and dates are formatted with the culture of the writer. [Run](cref:Meziantou.Framework.Templating.Template.Run) writes to a `StringWriter` that uses the current
  culture of the thread; to get the same text on every machine, write to a writer that uses the invariant culture:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_RunWithWriter;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Arguments.Add(new TemplateArgument("Total", typeof(decimal)));
template.Load("Total: <%= Total %>");

using var writer = new StringWriter(CultureInfo.InvariantCulture);
template.Run(writer, 1234.5m);
Assert.Equal("Total: 1234.5", writer.ToString());
```
<!-- endref -->

## Run statements

The code of `<% ... %>` blocks is pasted, in order, into the body of the method that renders the template, between the
instructions that write the text. A block can open a loop or a condition and a later block can close it:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_MixedContent;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("""
    <% for(int i = 1; i <= 3; i++) { %>
      Item <%= i %>: <%= i * 10 %>
    <% } %>
    """);
var result = template.Run();
Assert.Equal("""
      Item 1: 10
      Item 2: 20
      Item 3: 30

    """, result);
```
<!-- endref -->

A statement block can also write to the output directly: the output is a parameter named `__output__` (the
[OutputParameterName](cref:Meziantou.Framework.Templating.Template.OutputParameterName) property), and `__output__.Write(value)` writes a value.

## Declare members

`<%+ ... %>` adds fields, properties or methods to the class generated for the template. The template code runs in a
static method, so the members it uses must be `static`:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_ClassMemberBlock;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("""
    <%+private static string Prefix() => "Item"; %>
    <%= Prefix() %> 1
    """);
var result = template.Run();
Assert.Equal("Item 1", result);
```
<!-- endref -->

The `+` must follow the delimiter immediately: `<% + ... %>` is a statement block.

## Directives

`<%@ name value %>` configures the compilation of the template. The name is case-insensitive:

| Directive | Effect |
| --- | --- |
| `using` | Imports a namespace or declares an alias, like `Usings.Add` |
| `reference` | References an assembly by path; `alias=Name path` declares an extern alias |
| `include` | Compiles a C# source file with the template |
| `inherits` | Sets the base class of the generated class ([BaseClassFullTypeName](cref:Meziantou.Framework.Templating.Template.BaseClassFullTypeName)) |
| `implements` | Adds interfaces, separated by commas, to the generated class |

[Using code and libraries](05-UsingCodeAndLibraries.md) shows each of them. Other directives write nothing and change
nothing, but your code can read them from [Blocks](cref:Meziantou.Framework.Templating.Template.Blocks); the [command-line tool](07-CommandLineTool.md) reads
`outputextension` this way.

## Line breaks

A statement block, a member block or a directive that is alone on its line removes the line break that follows it, so
the lines that only hold code do not leave empty lines in the output:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_LineOnlyBlocks;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("""
    <ul>
    <% foreach (var item in new[] { "Apples", "Pears" }) { %>
      <li><%= item %></li>
    <% } %>
    </ul>
    """);

var result = template.Run();
Assert.Equal("""
    <ul>
      <li>Apples</li>
      <li>Pears</li>
    </ul>
    """, result);
```
<!-- endref -->

- The spaces after the block are removed with the line break, but the spaces before it are kept: put these blocks at the
  start of their line.
- An expression block (`<%= ... %>`) alone on its line keeps its line, because it writes something.
- A block that is followed by text on the same line keeps the line break.

## Blocks that are not closed

A `<%` without a matching `%>` is not an error: the rest of the template, from the `<%`, is written as text. If a part of
your template appears in the output verbatim, look for a missing `%>`.

## Change the delimiters

When the default delimiters appear in the text of your template, choose other ones. Set them before calling [Load](cref:Meziantou.Framework.Templating.Template.Load):

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_CustomDelimiters;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template
{
    StartCodeBlockDelimiter = "{{",
    EndCodeBlockDelimiter = "}}",
};
template.Load("Hello {{=Name}}!");
template.Arguments.Add(new TemplateArgument("Name", typeof(string)));
var result = template.Run("World");
Assert.Equal("Hello World!", result);
```
<!-- endref -->

The character that gives the kind of block (`=`, `+`, `@`) follows the start delimiter, whatever it is. There is no
escape sequence: to write a delimiter as text, write it from an expression that builds it from two strings, or choose
delimiters that the text does not contain.
