---
title: HTML emails
description: Generate the HTML of an email with encoded values, a subject and embedded images.
---

`Meziantou.Framework.Templating.Html` adds [HtmlEmailTemplate](cref:Meziantou.Framework.Templating.HtmlEmailTemplate), a template for the HTML body of an email. It uses `{{` and
`}}` as delimiters, which do not clash with HTML, and adds blocks that encode values, capture the subject of the message
and list its embedded images.

## Install the package

<InstallNuGetPackage Id="Meziantou.Framework.Templating.Html" />

The package depends on `Meziantou.Framework.Templating`, and its types are in the same namespace.

## Write an email

<!-- ref:../../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_Metadata;format=md-fenced-code-block;dedent=true -->
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

- `{{#html userName}}` writes the value of `userName`, HTML-encoded.
- `{{cid logo.png}}` writes the URL of an image attached to the email, and adds `logo.png` to
  `metadata.ContentIdentifiers`.
- The `title` section gives the subject of the email in `metadata.Title`. Its content stays in the HTML.
- `Run(out metadata, ...)` returns the HTML, and the metadata through its `out` parameter. It takes the values of the
  arguments by name, as above, by position, or none. The overloads that take a `TextWriter` write the HTML to it.

## Syntax

[HtmlEmailTemplate](cref:Meziantou.Framework.Templating.HtmlEmailTemplate) supports every block of [Template syntax](../03-TemplateSyntax.md), with `{{` and `}}`
instead of `<%` and `%>`, and adds the following ones:

| Block | Writes |
| --- | --- |
| `{{# expression }}` | The value, not encoded, like `{{= expression }}` |
| `{{#html expression }}` | The value, encoded for the text of an element |
| `{{#attr expression }}` | The value, encoded for an attribute |
| `{{#url expression }}` | The value, encoded for a part of a URL |
| `{{html statements }}` | Nothing: runs C# statements written HTML-encoded (`&lt;` for `<`) |
| `{{cid name }}` | `cid:name`, the URL of an embedded image |
| `{{@begin_section name }}` … `{{@end_section }}` | Nothing: the content between them is written and captured |

The keywords are case-sensitive and must be followed by a space: `{{#htmlName}}` is a `#` block that writes the variable
`htmlName` without encoding it.

## Use it in a server

[HtmlEmailTemplate](cref:Meziantou.Framework.Templating.HtmlEmailTemplate) compiles to `dynamic` code unless [OutputType](cref:Meziantou.Framework.Templating.Template.OutputType) is set, and a template that uses `dynamic` is never
unloaded, even when disposed. An application that compiles templates as it runs, for instance one per tenant or on every
change of a file, should set [OutputType](cref:Meziantou.Framework.Templating.Template.OutputType) and give every argument a type:

<!-- ref:../../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_TypedOutput;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate { OutputType = typeof(HtmlEmailOutput) };
template.Arguments.Add(new TemplateArgument("userName", typeof(string)));
template.Load("Hello {{#html userName}}!");

var result = template.Run(out _, "<John>");
Assert.Equal("Hello &lt;John&gt;!", result);
```
<!-- endref -->

See [Compilation and memory](../08-CompilationAndMemory.md) for the details.

## Pages

- [Encoding values](02-EncodingValues.md): choose the encoding for each place of the HTML, and write code in an HTML
  editor.
- [Subject and embedded images](03-SubjectAndEmbeddedImages.md): capture the subject of the email and list the images
  to attach.
