---
title: Subject and embedded images
description: Capture the subject of an email in the template, and list the images to attach to the message.
---

An email needs more than its HTML body: a subject, and the images that the body shows. [HtmlEmailTemplate](cref:Meziantou.Framework.Templating.HtmlEmailTemplate) returns them
in an [HtmlEmailMetadata](cref:Meziantou.Framework.Templating.HtmlEmailMetadata), so the template stays the single place that describes the email.

## Capture the subject

Write the subject between `{{@begin_section title}}` and `{{@end_section}}`. The content is written to the HTML, for
instance in the `<title>` element, and `metadata.Title` returns it:

<!-- ref:../../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_Section;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Load("Hello {{@begin_section title}}{{# \"Meziantou\" }}{{@end_section}}!");

var result = template.Run(out var metadata);
Assert.Equal("Hello Meziantou!", result);
Assert.NotNull(metadata);
Assert.Equal("Meziantou", metadata.Title);
```
<!-- endref -->

[Title](cref:Meziantou.Framework.Templating.HtmlEmailMetadata.Title) is plain text, ready to use as the subject of the message: the HTML that the section contains is decoded.

<!-- ref:../../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_TitleIsPlainText;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Arguments.Add(new TemplateArgument("product", typeof(string)));
template.Load("<title>{{@begin_section title}}Your {{#html product}} order{{@end_section}}</title>");

var result = template.Run(out var metadata, "Fish & Chips");
Assert.Equal("<title>Your Fish &amp; Chips order</title>", result);
Assert.NotNull(metadata);
Assert.Equal("Your Fish & Chips order", metadata.Title);
```
<!-- endref -->

- A section that is never ended makes the run fail with a [TemplateException](cref:Meziantou.Framework.Templating.TemplateException) that names it:

<!-- ref:../../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_UnclosedSection;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Load("<title>{{@begin_section title}}Welcome</title>");

var exception = Assert.Throws<TemplateException>(() => template.Run(out _));
Assert.Equal("The template is invalid: no end_section directive for 'title'.", exception.Message);
```
<!-- endref -->

- `{{@end_section}}` ends the last section that was started; `{{@end_section title}}` ends the section with that name.
- [Title](cref:Meziantou.Framework.Templating.HtmlEmailMetadata.Title) is `null` when the template has no `title` section. Sections with other names are written to the HTML, but
  [HtmlEmailMetadata](cref:Meziantou.Framework.Templating.HtmlEmailMetadata) does not return their content.

## Embed images

An image embedded in an email is attached to the message and referenced by its content identifier, with a `cid:` URL.
`{{cid name}}` writes this URL and adds the name to `metadata.ContentIdentifiers`:

<!-- ref:../../../tests/Meziantou.Framework.Templating.Html.Tests/EmailTemplateTest.cs;region=HtmlEmailTemplate_ContentIdentifier;format=md-fenced-code-block;dedent=true -->
```csharp
using var template = new HtmlEmailTemplate();
template.Load("<img src=\"{{cid logo.png}}\" />");

var result = template.Run(out var metadata);
Assert.Equal("<img src=\"cid:logo.png\" />", result);
Assert.NotNull(metadata);
Assert.Equal("logo.png", Assert.Single(metadata.ContentIdentifiers));
```
<!-- endref -->

- The name is text, not a C# expression: `{{cid logo.png}}` refers to the identifier `logo.png`.
- [ContentIdentifiers](cref:Meziantou.Framework.Templating.HtmlEmailMetadata.ContentIdentifiers) lists the names in the order they appear, once per `cid` block.
- The name is URL-encoded in the HTML. Prefer names made of letters, digits, dots and dashes, which are written as is.

The library does not send emails. When you build the message, attach each image of [ContentIdentifiers](cref:Meziantou.Framework.Templating.HtmlEmailMetadata.ContentIdentifiers) with this
content identifier, for instance as a `LinkedResource` whose `ContentId` is the name, with `System.Net.Mail`.
