---
title: Heading identifiers
description: Give each heading an id made from its text, to link to the sections of a document.
---

To link to a section of a page, its heading needs an `id`.
[AutoIdentifiers](cref:Meziantou.Framework.Markdown.MarkdownExtensions.AutoIdentifiers) gives each heading an `id` made
from its text, as GitHub and documentation sites do, so that `# Getting started` can be linked as `#getting-started`.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_AutoIdentifiers;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.AutoIdentifiers };
var tree = MarkdownSyntaxTree.ParseText("# Getting started\n\nSee [Getting started].\n\n# Getting started\n", options);

// The second heading with the same text gets a suffix, and the link without definition links to the last heading
var headings = tree.GetRoot().Blocks.OfType<MarkdownHeadingSyntax>().ToList();
Assert.Equal("getting-started", headings[0].Identifier);
Assert.Equal("getting-started-1", headings[1].Identifier);
Assert.Equal("<p>See <a href=\"#getting-started-1\">Getting started</a>.</p>", MarkdownHtmlRenderer.ToHtml(tree).Split('\n')[1]);
```
<!-- endref -->

## Details

- [MarkdownHeadingSyntax.Identifier](cref:Meziantou.Framework.Markdown.MarkdownHeadingSyntax.Identifier) gives the
  identifier, unless the [generic attributes](15-GenericAttributes.md) of the heading give one.
- Headings with the same text get a suffix: `-1`, `-2`...
- A reference link without definition whose label is the first line of a heading, such as `[Getting started]`, links to
  it: [MarkdownLinkSyntax.Heading](cref:Meziantou.Framework.Markdown.MarkdownLinkSyntax.Heading) gives the heading.

## Options

[AutoIdentifierOptions](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.AutoIdentifierOptions) chooses whether
these links are made, whether the identifier only has ASCII characters (`Ærø` becomes `aeroe`), and whether it is made
as GitHub makes it.

[GitHub](cref:Meziantou.Framework.Markdown.MarkdownAutoIdentifierOptions.GitHub) keeps the characters that GitHub keeps,
so that the anchors copied from GitHub resolve: the letters, the combining marks, the decimal digits, the connector
punctuation such as `_`, and `-`, in lower case, and each space becomes `-`. `# हिन्दी भाषा` has the identifier
`हिन्दी-भाषा`, with its vowel signs, and the letters outside the Basic Multilingual Plane are kept.
