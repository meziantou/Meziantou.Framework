# Meziantou.Framework.Templating.Html

A .NET library for generating HTML emails using a template engine with built-in encoding and section support.

## Usage

This library extends the [Meziantou.Framework.Templating](https://www.nuget.org/packages/Meziantou.Framework.Templating) engine with HTML-specific features, making it easy to create dynamic HTML emails with proper encoding, sections for metadata (like email titles), and support for embedded content identifiers.

### Basic Template

```csharp
using Meziantou.Framework.Templating;

var template = new HtmlEmailTemplate();
template.Load("Hello {{# \"Meziantou\" }}!");

var result = template.Run(out var metadata);
// Output: Hello Meziantou!
```

### Template Syntax

The template uses `{{` and `}}` delimiters for code blocks. Here are the available directives:

#### Expression Evaluation (`#`)

Evaluate and output a C# expression:

```csharp
var template = new HtmlEmailTemplate();
template.Load("Hello {{# userName }}!");

var result = template.Run(out _, new Dictionary<string, object?> { ["userName"] = "John" });
// Output: Hello John!
```

#### HTML Encoding (`#html`)

Encode HTML content to prevent XSS attacks:

```csharp
var template = new HtmlEmailTemplate();
template.Load("Hello {{#html \"<Meziantou>\" }}!");

var result = template.Run(out _);
// Output: Hello &lt;Meziantou&gt;!
```

#### HTML Attribute Encoding (`#attr`)

Encode values for use in HTML attributes:

```csharp
var template = new HtmlEmailTemplate();
template.Load("Hello <a href=\"{{#attr \"Sample&Sample\"}}\">Meziantou</a>!");

var result = template.Run(out _);
// Output: Hello <a href="Sample&amp;Sample">Meziantou</a>!
```

#### URL Encoding (`#url`)

Encode values for use in URLs:

```csharp
var template = new HtmlEmailTemplate();
template.Load("Hello <a href=\"http://www.localhost.com/{{#url \"Sample&Url\" }}\">Meziantou</a>!");

var result = template.Run(out _);
// Output: Hello <a href="http://www.localhost.com/Sample%26Url">Meziantou</a>!
```

#### Content Identifier (`cid`)

Generate content identifiers for embedded resources in emails:

```csharp
var template = new HtmlEmailTemplate();
template.Load("<img src=\"{{cid logo.png}}\" />");

var result = template.Run(out var metadata);
// Output: <img src="cid:logo.png" />
// metadata.ContentIdentifiers contains ["logo.png"]
```

#### HTML Code Blocks (`html`)

Write HTML-encoded C# code (useful when your template contains HTML entities):

```csharp
var template = new HtmlEmailTemplate();
template.Load("{{html for(int i = 0; i &lt; 3; i++) { }}{{#i}} {{ } }}");

var result = template.Run(out _);
// Output: 0 1 2
```

### Sections

Sections allow you to capture parts of the template output for use as metadata (e.g., email subject):

```csharp
var template = new HtmlEmailTemplate();
template.Load("Hello {{@begin section title}}{{# \"Meziantou\" }}{{@end section}}!");

var result = template.Run(out var metadata);
// Output: Hello Meziantou!
// metadata.Title: "Meziantou"
```

### Metadata

The `HtmlEmailMetadata` class contains extracted information from the template:

- `Title` - Content from the "title" section (typically used for email subject)
- `ContentIdentifiers` - List of content identifiers (CIDs) referenced in the template

```csharp
var template = new HtmlEmailTemplate();
template.Load(@"
{{@begin section title}}Welcome Email{{@end section}}
<html>
  <body>
    <img src=""{{cid logo.png}}"" />
    <h1>Hello {{#html userName}}!</h1>
  </body>
</html>");

var result = template.Run(out var metadata,
    new Dictionary<string, object?> { ["userName"] = "John" });

// metadata.Title: "Welcome Email"
// metadata.ContentIdentifiers: ["logo.png"]
```

### Passing Parameters

You can pass parameters to templates in multiple ways:

```csharp
// Using a dictionary
var result = template.Run(out var metadata,
    new Dictionary<string, object?> { ["name"] = "John", ["age"] = 30 });

// Using positional parameters
var result = template.Run(out var metadata, "John", 30);

// No parameters
var result = template.Run(out var metadata);
```
