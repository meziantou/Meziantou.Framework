---
title: Encoding values
description: Encode each value for the place it is written to in the HTML, and write template code in an HTML editor.
---

A value written into HTML must be encoded for the place it is written to: otherwise, a name such as `<b>` or `A & B`
breaks the layout of the email, and a value that comes from a user can inject HTML. `HtmlEmailTemplate` has one block
per place:

| Place in the HTML | Block |
| --- | --- |
| Text of an element: `<p>…</p>` | `{{#html value}}` |
| Value of an attribute: `alt="…"`, `href="…"` | `{{#attr value}}` |
| A part of a URL: a path segment, a query string value | `{{#url value}}` |
| HTML that you built and trust | `{{# value}}` |

A `null` value writes nothing.

## Encode text

`{{#html value}}` encodes the characters that have a meaning in HTML:

<!-- ref:../../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_HtmlEncode;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Load("Hello {{#html \"<Meziantou>\" }}!");

var result = template.Run(out _);
Assert.Equal("Hello &lt;Meziantou&gt;!", result);
```
<!-- endref -->

The characters outside ASCII are written as numeric character references. Browsers and mail clients show the original
characters:

<!-- ref:../../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_NonAsciiCharacters;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Arguments.Add(new TemplateArgument("name", typeof(string)));
template.Load("<p>Hello {{#html name}}!</p>");

var result = template.Run(out _, "Zoé");
Assert.Equal("<p>Hello Zo&#xE9;!</p>", result);
```
<!-- endref -->

## Encode an attribute

`{{#attr value}}` encodes every character except the ASCII letters and digits, so the value is safe in a quoted
attribute and in an unquoted one:

<!-- ref:../../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_AttributeEncode;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Load("Hello <a href=\"{{#attr \"Sample&Sample\"}}\">Meziantou</a>!");

var result = template.Run(out _);
Assert.Equal("Hello <a href=\"Sample&amp;Sample\">Meziantou</a>!", result);
```
<!-- endref -->

Use it to write a complete URL in an `href` or a `src` attribute too: the encoded characters are decoded when the HTML
is read.

## Encode a part of a URL

`{{#url value}}` encodes a value for a part of a URL, such as a query string value:

<!-- ref:../../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_UrlEncodeValue;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Arguments.Add(new TemplateArgument("query", typeof(string)));
template.Load("<a href=\"https://example.com/search?q={{#url query}}\">Search</a>");

var result = template.Run(out _, "fish & chips");
Assert.Equal("<a href=\"https://example.com/search?q=fish%20%26%20chips\">Search</a>", result);
```
<!-- endref -->

It encodes `:`, `/`, `?` and `=` too, so it is not suited to a complete URL: `https://example.com/` would become
`https%3A%2F%2Fexample.com%2F`. Write the fixed part of the URL in the template, and encode the variable parts only.

## Write code in an HTML editor

An HTML editor encodes the `<`, `>` and `&` characters of the code blocks it shows as text. A `{{html ... }}` block
decodes its code before compiling it, so a template edited in such an editor still compiles:

<!-- ref:../../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_HtmlCodeBlock;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Load("{{html for(int i = 0; i &lt; 3; i++) { }}{{#i}} {{ } }}");

var result = template.Run(out _);
Assert.Equal("0 1 2 ", result);
```
<!-- endref -->

The other blocks are not decoded: write their code with the plain characters.
