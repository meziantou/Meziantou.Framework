---
title: YAML front matter
description: Read the YAML metadata at the top of a document, such as its title, and keep it out of the HTML.
---

## Read the front matter

Many tools put metadata at the top of a Markdown file, between two `---` lines: the title of the page, its date, its
tags. Without the extension, CommonMark renders these lines as text and a horizontal rule. [UseYamlFrontMatter](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseYamlFrontMatter) reads
them as a [YamlFrontMatterBlock](cref:Meziantou.Framework.Markdown.Extensions.Yaml.YamlFrontMatterBlock) (namespace [Meziantou.Framework.Markdown.Extensions.Yaml](cref:Meziantou.Framework.Markdown.Extensions.Yaml)), which is not rendered:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestYamlFrontMatterExtension.cs;region=MarkdownExtensions_UseYamlFrontMatter;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseYamlFrontMatter()
    .Build();

var markdown = "---\ntitle: Getting started\ntags: [setup]\n---\n# Install the package";
var document = MarkdownConverter.Parse(markdown, pipeline);

// The front matter is a block of the document, without the --- lines
var frontMatter = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();
Assert.NotNull(frontMatter);
Assert.Equal("title: Getting started\ntags: [setup]", frontMatter.Lines.ToString());

// It is not written to the HTML
Assert.Equal("<h1>Install the package</h1>\n", document.ToHtml(pipeline));
```
<!-- endref -->

The library does not parse the YAML itself: pass the text of [Lines](cref:Meziantou.Framework.Markdown.Syntax.LeafBlock.Lines) to the YAML library of your choice.

## Rules

- The front matter must start at the very beginning of the document, with a `---` line: no blank line or space before.
- It ends with a `---` or `...` line.
- When there is no closing line, the lines are parsed as Markdown.

Set [AllowInMiddleOfDocument](cref:Meziantou.Framework.Markdown.Extensions.Yaml.YamlFrontMatterExtension.AllowInMiddleOfDocument) to accept a front matter block anywhere in the document:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestYamlFrontMatterExtension.cs;region=YamlFrontMatterExtension_AllowInMiddleOfDocument;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .Use(new YamlFrontMatterExtension { AllowInMiddleOfDocument = true })
    .Build();

var html = MarkdownConverter.ToHtml("First part\n---\ntitle: Second part\n---\nSecond part", pipeline);
Assert.Equal("<p>First part</p>\n<p>Second part</p>\n", html);
```
<!-- endref -->
