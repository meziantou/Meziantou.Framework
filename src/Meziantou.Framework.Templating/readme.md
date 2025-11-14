# Meziantou.Framework.Templating

A powerful and flexible .NET template engine that allows you to create text templates with embedded C# code. The template syntax is customizable, and code sections use C# for maximum flexibility and type safety.

## Features

- **Embedded C# code** - Write C# code directly in your templates
- **Customizable syntax** - Configure the code block delimiters to match your needs
- **Dynamic compilation** - Templates are compiled to IL for optimal performance
- **Type-safe parameters** - Add strongly-typed or dynamic parameters to your templates
- **Custom output types** - Use any output writer type for your templates
- **Using directives** - Import namespaces and types for use in templates
- **Debug support** - Generate debug symbols for template debugging

## Usage

### Basic Template

The simplest way to use a template is to load text and run it:

```csharp
using Meziantou.Framework.Templating;

var template = new Template();
template.Load("Hello World!");
var result = template.Run();
// result: "Hello World!"
```

### Templates with Parameters

Add parameters to make your templates dynamic:

```csharp
var template = new Template();
template.Load("Hello <%=Name%>!");
template.AddArgument("Name", typeof(string));
var result = template.Run("Meziantou");
// result: "Hello Meziantou!"
```

### Using Named Parameters

You can also use named parameters with dictionaries:

```csharp
var template = new Template();
template.Load("Hello <%=Name%>!");
var arguments = new Dictionary<string, object>
{
    { "Name", "Meziantou" }
};
template.AddArguments(arguments);
var result = template.Run(arguments);
// result: "Hello Meziantou!"
```

### Code Blocks

Templates support three types of code blocks:

#### 1. Evaluation blocks (`<%=...%>`)

Evaluate expressions and write their result to the output:

```csharp
var template = new Template();
template.Load("2 + 2 = <%= 2 + 2 %>");
var result = template.Run();
// result: "2 + 2 = 4"
```

#### 2. Statement blocks (`<%...%>`)

Execute C# statements:

```csharp
var template = new Template();
template.Load("Numbers: <% for(int i = 1; i <= 5; i++) { %><%= i %><% } %>");
var result = template.Run();
// result: "Numbers: 12345"
```

#### 3. Mixed content

Combine text, evaluation blocks, and statement blocks:

```csharp
var template = new Template();
template.Load(@"
<% for(int i = 1; i <= 3; i++) { %>
  Item <%= i %>: <%= i * 10 %>
<% } %>
");
var result = template.Run();
// result:
//   Item 1: 10
//   Item 2: 20
//   Item 3: 30
```

## Advanced Usage

### Custom Delimiters

Change the code block delimiters to match your preferred syntax:

```csharp
var template = new Template
{
    StartCodeBlockDelimiter = "{{",
    EndCodeBlockDelimiter = "}}"
};
template.Load("Hello {{=Name}}!");
template.AddArgument("Name", typeof(string));
var result = template.Run("World");
// result: "Hello World!"
```

### Adding Using Directives

Import namespaces to use types without fully-qualified names:

```csharp
var template = new Template();
template.AddUsing("System.Linq");
template.Load("<%= Enumerable.Range(1, 5).Sum() %>");
var result = template.Run();
// result: "15"
```

You can also import types with aliases:

```csharp
var template = new Template();
template.AddUsing(typeof(System.Text.StringBuilder), "SB");
template.Load("<% var sb = new SB(); sb.Append(\"Hello\"); %><%= sb.ToString() %>");
var result = template.Run();
// result: "Hello"
```

### Custom Output Type

Use a custom output type for specialized scenarios:

```csharp
var template = new Template();
template.OutputType = typeof(Output);
template.Load("<%__output__.Write(\"Custom output\");%>");
var result = template.Run();
// result: "Custom output"
```

### Dynamic Parameters

Use dynamic parameters when types are not known at compile time:

```csharp
var template = new Template();
template.Load("Value: <%=Value%>");
template.AddArgument("Value"); // No type specified = dynamic
var result = template.Run(42);
// result: "Value: 42"
```

### Accessing the Output Parameter

You can directly use the output parameter in your code blocks:

```csharp
var template = new Template();
template.Load("<% for(int i = 0; i < 3; i++) __output__.Write(i); %>");
var result = template.Run();
// result: "012"
```

### Building Templates Separately

Templates can be built (compiled) separately from execution:

```csharp
var template = new Template();
template.Load("Hello <%=Name%>");
template.AddArgument("Name", typeof(string));

// Compile the template
template.Build(CancellationToken.None);

// Run multiple times (compiled code is reused)
var result1 = template.Run("Alice");
var result2 = template.Run("Bob");
```

### Debug Mode

Enable debug mode to generate debug symbols for troubleshooting:

```csharp
var template = new Template
{
    Debug = true
};
template.Load("<%=Value%>");
template.AddArgument("Value", typeof(int));
var result = template.Run(42);
```

### Accessing Generated Source Code

After building a template, you can inspect the generated C# source code:

```csharp
var template = new Template();
template.Load("Hello <%=Name%>");
template.AddArgument("Name", typeof(string));
template.Build(CancellationToken.None);

Console.WriteLine(template.SourceCode);
// Prints the generated C# class
```

## Template Syntax

The default template syntax uses `<%` and `%>` delimiters:

| Syntax | Description | Example |
|--------|-------------|---------|
| `<%= expression %>` | Evaluates an expression and writes it to output | `<%= 2 + 2 %>` |
| `<% statement %>` | Executes a C# statement | `<% var x = 10; %>` |
| Text | Any text outside code blocks is written as-is | `Hello World` |

## Error Handling

The library throws `TemplateException` for template-related errors:

```csharp
try
{
    var template = new Template();
    template.Load("<%=InvalidExpression%>");
    template.Build(CancellationToken.None);
}
catch (TemplateException ex)
{
    Console.WriteLine($"Template error: {ex.Message}");
}
```

## Additional Resources

- [Blog post about Meziantou.Framework.Templating](https://www.meziantou.net/creating-a-template-engine-in-csharp.htm?WT.mc_id=DT-MVP-5003978)
