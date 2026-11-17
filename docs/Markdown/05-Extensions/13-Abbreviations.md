---
title: Abbreviations
description: Define an abbreviation once, and show its meaning on every occurrence with <abbr>.
---

[Abbreviations](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Abbreviations) explains the abbreviations of a
document without repeating their meaning: a definition, `*[HTML]: Hypertext Markup Language`, gives the meaning, and
each occurrence of the abbreviation in the text is rendered as `<abbr title="Hypertext Markup Language">HTML</abbr>`,
which browsers show as a tooltip.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Abbreviations;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Abbreviations };
var tree = MarkdownSyntaxTree.ParseText("*[HTML]: Hypertext Markup Language\n\nHTML is the language of the web, not XHTML.\n", options);

var abbreviation = tree.GetRoot().DescendantNodes().OfType<MarkdownAbbreviationSyntax>().Single();
Assert.Equal("Hypertext Markup Language", abbreviation.Definition!.Title);

// The definition is not written, and only whole words are abbreviations
Assert.Equal("<p><abbr title=\"Hypertext Markup Language\">HTML</abbr> is the language of the web, not XHTML.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

## Syntax tree

The definition is a
[MarkdownAbbreviationDefinitionSyntax](cref:Meziantou.Framework.Markdown.MarkdownAbbreviationDefinitionSyntax), which
the renderer does not write. Each word of the text that is the abbreviation is a
[MarkdownAbbreviationSyntax](cref:Meziantou.Framework.Markdown.MarkdownAbbreviationSyntax), whose
[Definition](cref:Meziantou.Framework.Markdown.MarkdownAbbreviationSyntax.Definition) gives the definition.
[MarkdownDocumentSyntax.GetAbbreviationDefinition](cref:Meziantou.Framework.Markdown.MarkdownDocumentSyntax.GetAbbreviationDefinition)
finds a definition by its label.

The titles that the abbreviations add are limited to the length of the text, and to at least 100,000 characters, so that
a short text cannot render as a huge one.
