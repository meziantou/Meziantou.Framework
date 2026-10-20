# Meziantou.Framework.Templating.Html

A .NET library for generating HTML emails using a template engine with built-in encoding and section support.

## Usage

This library extends the [Meziantou.Framework.Templating](https://www.nuget.org/packages/Meziantou.Framework.Templating) engine with HTML-specific features, making it easy to create dynamic HTML emails with proper encoding, sections for metadata (like email titles), and support for embedded content identifiers.

### Basic Template

<!-- ref:../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_Basic;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Load("Hello {{# \"Meziantou\" }}!");

var result = template.Run(out var metadata);
Assert.Equal("Hello Meziantou!", result);
```
<!-- endref -->

### Template Syntax

The template uses `{{` and `}}` delimiters for code blocks. Here are the available directives:

#### Expression Evaluation (`#`)

Evaluate and output a C# expression. Declare the arguments the template uses with `Arguments`:

<!-- ref:../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_Expression;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Arguments.Add(new TemplateArgument("userName", typeof(string)));
template.Load("Hello {{# userName }}!");

var result = template.Run(out _, new Dictionary<string, object?>(StringComparer.Ordinal) { ["userName"] = "John" });
Assert.Equal("Hello John!", result);
```
<!-- endref -->

#### HTML Encoding (`#html`)

Encode HTML content to prevent XSS attacks:

<!-- ref:../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_HtmlEncode;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Load("Hello {{#html \"<Meziantou>\" }}!");

var result = template.Run(out _);
Assert.Equal("Hello &lt;Meziantou&gt;!", result);
```
<!-- endref -->

#### HTML Attribute Encoding (`#attr`)

Encode values for use in HTML attributes:

<!-- ref:../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_AttributeEncode;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Load("Hello <a href=\"{{#attr \"Sample&Sample\"}}\">Meziantou</a>!");

var result = template.Run(out _);
Assert.Equal("Hello <a href=\"Sample&amp;Sample\">Meziantou</a>!", result);
```
<!-- endref -->

#### URL Encoding (`#url`)

Encode values for use in URLs:

<!-- ref:../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_UrlEncode;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Load("Hello <a href=\"http://www.localhost.com/{{#url \"Sample&Url\" }}\">Meziantou</a>!");

var result = template.Run(out _);
Assert.Equal("Hello <a href=\"http://www.localhost.com/Sample%26Url\">Meziantou</a>!", result);
```
<!-- endref -->

#### Content Identifier (`cid`)

Generate content identifiers for embedded resources in emails:

<!-- ref:../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_ContentIdentifier;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Load("<img src=\"{{cid logo.png}}\" />");

var result = template.Run(out var metadata);
Assert.Equal("<img src=\"cid:logo.png\" />", result);
Assert.NotNull(metadata);
Assert.Equal("logo.png", Assert.Single(metadata.ContentIdentifiers));
```
<!-- endref -->

#### HTML Code Blocks (`html`)

Write HTML-encoded C# code (useful when your template contains HTML entities):

<!-- ref:../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_HtmlCodeBlock;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Load("{{html for(int i = 0; i &lt; 3; i++) { }}{{#i}} {{ } }}");

var result = template.Run(out _);
Assert.Equal("0 1 2 ", result);
```
<!-- endref -->

### Sections

Sections allow you to capture parts of the template output for use as metadata (e.g., email subject):

<!-- ref:../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_Section;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Load("Hello {{@begin_section title}}{{# \"Meziantou\" }}{{@end_section}}!");

var result = template.Run(out var metadata);
Assert.Equal("Hello Meziantou!", result);
Assert.NotNull(metadata);
Assert.Equal("Meziantou", metadata.Title);
```
<!-- endref -->

### Metadata

The `HtmlEmailMetadata` class contains extracted information from the template:

- `Title` - Content from the "title" section (typically used for email subject)
- `ContentIdentifiers` - List of content identifiers (CIDs) referenced in the template

<!-- ref:../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_Metadata;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Arguments.Add(new TemplateArgument("userName", typeof(string)));
template.Load("""
    {{@begin_section title}}Welcome Email{{@end_section}}
    <html>
      <body>
        <img src="{{cid logo.png}}" />
        <h1>Hello {{#html userName}}!</h1>
      </body>
    </html>
    """);

var result = template.Run(out var metadata,
    new Dictionary<string, object?>(StringComparer.Ordinal) { ["userName"] = "John" });

Assert.NotNull(metadata);
Assert.Equal("Welcome Email", metadata.Title);
Assert.Equal("logo.png", Assert.Single(metadata.ContentIdentifiers));
```
<!-- endref -->

### Passing Parameters

You can pass parameters to templates by name or by position. A template without arguments is run with `template.Run(out var metadata)`, as in the first example.

<!-- ref:../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_Parameters;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Arguments.Add(new TemplateArgument("name", typeof(string)));
template.Arguments.Add(new TemplateArgument("age", typeof(int)));
template.Load("{{#html name}} is {{# age }}");

// Using a dictionary
var result1 = template.Run(out _,
    new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "John", ["age"] = 30 });
Assert.Equal("John is 30", result1);

// Using positional parameters
var result2 = template.Run(out _, "John", 30);
Assert.Equal("John is 30", result2);
```
<!-- endref -->

### Memory Usage

Building a template compiles it into an assembly, which is loaded in a collectible `AssemblyLoadContext` owned by the template. Dispose the template to unload that assembly; otherwise it is unloaded once the template becomes unreachable and the garbage collector runs. Set `OutputType` to get a template that can be unloaded (see below):

<!-- ref:../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_TypedOutput;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate { OutputType = typeof(HtmlEmailOutput) };
template.Arguments.Add(new TemplateArgument("userName", typeof(string)));
template.Load("Hello {{#html userName}}!");

var result = template.Run(out _, "<John>");
Assert.Equal("Hello &lt;John&gt;!", result);
```
<!-- endref -->

> [!IMPORTANT]
> `HtmlEmailTemplate` compiles to `dynamic` code unless `OutputType` is set, and a template that uses `dynamic` is **never** unloaded, even when disposed. See [Memory Usage](../Meziantou.Framework.Templating/readme.md#memory-usage) for the details. A server that keeps compiling templates — one per tenant, per file, or on every file change — should set `OutputType` to `typeof(HtmlEmailOutput)` and give every argument a type.
