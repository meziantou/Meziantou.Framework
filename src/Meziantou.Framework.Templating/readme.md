# Meziantou.Framework.Templating

A powerful and flexible .NET template engine that allows you to create text templates with embedded C# code. The template syntax is customizable, and code sections use C# for maximum flexibility and type safety.

## Features

- **Embedded C# code** - Write C# code directly in your templates
- **Customizable syntax** - Configure the code block delimiters to match your needs
- **Dynamic compilation** - Templates are compiled to IL for optimal performance
- **Type-safe parameters** - Add strongly-typed or dynamic parameters to your templates
- **Custom output types** - Use any output writer type for your templates
- **Using directives** - Import namespaces and types for use in templates
- **Class member blocks** - Add methods, fields, and properties to the generated template class
- **Debug support** - Generate debug symbols for template debugging
- **Unloadable** - Dispose a template to unload the assembly it was compiled into

## Usage

### Basic Template

The simplest way to use a template is to load text and run it:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_Basic;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("Hello World!");
var result = template.Run();
Assert.Equal("Hello World!", result);
```
<!-- endref -->

### Templates with Parameters

Add parameters to make your templates dynamic:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_Parameters;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("Hello <%=Name%>!");
template.Arguments.Add(new TemplateArgument("Name", typeof(string)));
var result = template.Run("Meziantou");
Assert.Equal("Hello Meziantou!", result);
```
<!-- endref -->

### Using Named Parameters

You can also use named parameters with dictionaries:

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

An argument that is missing from the dictionary gets the default value of its type (`null`, `0`,
`false`, …). Set `ThrowOnMissingArgument` to get a `TemplateException` naming the argument instead,
so a typo in a key does not silently produce a wrong document:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_ThrowOnMissingArgument;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template { ThrowOnMissingArgument = true };
template.Arguments.Add(new TemplateArgument("Name", typeof(string)));
template.Load("Hello <%=Name%>!");

var exception = Assert.Throws<TemplateException>(() => template.Run(new Dictionary<string, object?>(StringComparer.Ordinal)));
Assert.Equal("No value was provided for the argument 'Name'.", exception.Message);
```
<!-- endref -->

### Code Blocks

Templates support four types of code blocks:

#### 1. Evaluation blocks (`<%=...%>`)

Evaluate expressions and write their result to the output:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_EvaluationBlock;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("2 + 2 = <%= 2 + 2 %>");
var result = template.Run();
Assert.Equal("2 + 2 = 4", result);
```
<!-- endref -->

#### 2. Statement blocks (`<%...%>`)

Execute C# statements:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_StatementBlock;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("Numbers: <% for(int i = 1; i <= 5; i++) { %><%= i %><% } %>");
var result = template.Run();
Assert.Equal("Numbers: 12345", result);
```
<!-- endref -->

#### 3. Class member blocks (`<%+...%>`)

Add members to the generated template class:

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

#### 4. Mixed content

Combine text, evaluation blocks, and statement blocks:

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

### Directives

Templates support directives using the `@` prefix inside a code block (for example `<%@ using System.Linq %>` or `{{@ outputextension .cs }}` when using custom delimiters).

Directives are parsed as `DirectiveBlock` instances. You can enumerate them by filtering `Template.Blocks` (for example `template.Blocks.OfType<DirectiveBlock>()`), and the following built-in directives are handled case-insensitively by default:

- `using` - Adds a using directive
- `inherits` - Sets `BaseClassFullTypeName`
- `implements` - Adds one or more interfaces to the generated class declaration
- `reference` - Adds an assembly reference (`path` and optional `alias=...` for `extern alias`)
- `include` - Adds a C# source file reference to include in the compilation

Directives that are not built in (such as `outputextension`) are still available in `Template.Blocks` and can be handled by overriding `DirectiveBlock.ApplyDirective()`.

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

You can also add references programmatically:

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

## Advanced Usage

### Custom Delimiters

Change the code block delimiters to match your preferred syntax:

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

### Adding Using Directives

Templates import `System`, `System.Collections.Generic`, `System.Linq`, `System.Text` and `System.Threading.Tasks` by default:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_DefaultUsings;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("<%= Enumerable.Range(1, 5).Sum() %>");
var result = template.Run();
Assert.Equal("15", result);
```
<!-- endref -->

Import other namespaces to use their types without fully-qualified names. Importing a namespace that is already imported has no effect:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_Usings;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Usings.Add("System.IO");
template.Load("<%= Path.GetExtension(\"report.txt\") %>");
var result = template.Run();
Assert.Equal(".txt", result);
```
<!-- endref -->

You can also import types with aliases:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_UsingAlias;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Usings.Add("SB = System.Text.StringBuilder");
template.Load("<% var sb = new SB(); sb.Append(\"Hello\"); %><%= sb.ToString() %>");
var result = template.Run();
Assert.Equal("Hello", result);
```
<!-- endref -->

### Custom Output Type

Use a custom output type for specialized scenarios:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_CustomOutputType;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.OutputType = typeof(Output);
template.Load("<%__output__.Write(\"Custom output\");%>");
var result = template.Run();
Assert.Equal("Custom output", result);
```
<!-- endref -->

### Dynamic Parameters

Use dynamic parameters when types are not known at compile time. Note that a template using `dynamic` can never be unloaded, see [Memory Usage](#memory-usage):

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_DynamicParameters;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("Value: <%=Value%>");
template.Arguments.Add(new TemplateArgument("Value", type: null)); // No type specified = dynamic
var result = template.Run(42);
Assert.Equal("Value: 42", result);
```
<!-- endref -->

### Building Templates Separately

Templates can be built (compiled) separately from execution:

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

### Debug Mode

Enable debug mode to generate debug symbols for troubleshooting:

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

### Accessing Generated Source Code

After building a template, you can inspect the generated C# source code:

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

## Template Syntax

The default template syntax uses `<%` and `%>` delimiters:

| Syntax | Description | Example |
|--------|-------------|---------|
| `<%= expression %>` | Evaluates an expression and writes it to output | `<%= 2 + 2 %>` |
| `<% statement %>` | Executes a C# statement | `<% var x = 10; %>` |
| `<%+ member %>` | Declares a class member in the generated template class | `<%+private static int Get() => 42; %>` |
| Text | Any text outside code blocks is written as-is | `Hello World` |

## Memory Usage

Building a template compiles it into an assembly, which is loaded in a collectible `AssemblyLoadContext` owned by the template. Dispose the template to unload that assembly; otherwise it is unloaded once the template becomes unreachable and the garbage collector runs. A disposed template cannot be built or run again.

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_Dispose;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template { OutputType = typeof(Output) };
template.Load("Hello <%=Name%>!");
template.Arguments.Add(new TemplateArgument("Name", typeof(string)));
var result = template.Run("Meziantou");
Assert.Equal("Hello Meziantou!", result);
```
<!-- endref -->

This matters for processes that compile many templates, such as a server that compiles one template per tenant, per file, or on every file change. A one-shot process does not need to dispose anything.

> [!IMPORTANT]
> A template that uses `dynamic` is **never** unloaded, even when disposed. Executing a dynamic call site registers the type that contains it in a process-wide cache held by the C# runtime binder, and that cache keeps the generated assembly alive for the lifetime of the process.
>
> A template uses `dynamic` when `OutputType` is `null` (the default), when a `TemplateArgument` is declared without a type, or when its code blocks use `dynamic` themselves.

Set `OutputType` and give every argument a type to get an unloadable template:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_Unloadable;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template { OutputType = typeof(Output) };
template.Load("Hello <%=Name%>!");
template.Arguments.Add(new TemplateArgument("Name", typeof(string)));
```
<!-- endref -->

Declaring the types is worth it on its own: the template binds its calls at compile time, so it runs faster and reports errors when it is built instead of when it is run.

## Error Handling

The library throws `TemplateException` for template-related errors:

<!-- ref:../../tests/Meziantou.Framework.Templating.Tests/TemplateTest.cs;region=Template_ErrorHandling;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new Template();
template.Load("<%=InvalidExpression%>");

var exception = Assert.Throws<TemplateException>(() => template.Build(CancellationToken.None));
Assert.Contains("InvalidExpression", exception.Message);
```
<!-- endref -->
