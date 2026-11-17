---
title: YAML front matter
description: Read the YAML metadata at the start of a document, such as a title or tags, and keep it out of the HTML.
---

Static site generators and documentation tools put the metadata of a page, such as its title, its date or its tags, in a
YAML block at the start of the file.
[YamlFrontMatter](cref:Meziantou.Framework.Markdown.MarkdownExtensions.YamlFrontMatter) reads this block as a front
matter instead of a thematic break and a heading, and the renderer does not write it.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_YamlFrontMatter;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.YamlFrontMatter };
var tree = MarkdownSyntaxTree.ParseText("---\ntitle: Hello\ntags: [a, b]\n---\n# Hello\n", options);

var frontMatter = Assert.IsType<MarkdownFrontMatterSyntax>(tree.GetRoot().Blocks[0]);
Assert.Equal("title: Hello\ntags: [a, b]\n", frontMatter.Yaml);

// The renderer does not write the front matter
Assert.Equal("<h1>Hello</h1>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

The front matter is between `---` and `---` or `...` at the start of the document. It is a
[MarkdownFrontMatterSyntax](cref:Meziantou.Framework.Markdown.MarkdownFrontMatterSyntax), whose
[Yaml](cref:Meziantou.Framework.Markdown.MarkdownFrontMatterSyntax.Yaml) property gives the YAML to give to a YAML
parser.
