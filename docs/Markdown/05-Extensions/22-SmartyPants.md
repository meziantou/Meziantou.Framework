---
title: Typographic punctuation
description: Write straight quotes, dashes and ellipses as their typographic characters, such as “ ” – — and ….
---

A keyboard types straight quotes and hyphens, while print uses curly quotes, dashes and ellipses.
[SmartyPants](cref:Meziantou.Framework.Markdown.MarkdownExtensions.SmartyPants) writes them as their typographic
characters: quotes, `<<` and `>>`, `...` at the end of a word, `--` and `---` become `&ldquo;`, `&laquo;`, `&hellip;`,
`&ndash;`, `&mdash;`...

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_SmartyPants;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.SmartyPants };
var tree = MarkdownSyntaxTree.ParseText("\"Wait...\" -- it is 'done'\n", options);

Assert.Equal("<p>&ldquo;Wait&hellip;&rdquo; &ndash; it is &lsquo;done&rsquo;</p>\n", MarkdownHtmlRenderer.ToHtml(tree));

// The renderer can write other text, such as the characters themselves
var french = new MarkdownHtmlRendererOptions
{
    SmartyPants = new Dictionary<MarkdownSmartyPant, string>
    {
        [MarkdownSmartyPant.LeftDoubleQuote] = "\u00AB\u00A0",
        [MarkdownSmartyPant.RightDoubleQuote] = "\u00A0\u00BB",
    },
};
Assert.Equal("<p>\u00AB\u00A0Wait&hellip;\u00A0\u00BB &ndash; it is &lsquo;done&rsquo;</p>\n", MarkdownHtmlRenderer.ToHtml(tree, french));
```
<!-- endref -->

## Details

- A quote is typographic when it opens or closes as `_` would, and only when it is paired, so `l'ami` keeps its
  apostrophe.
- Each one is a token of kind [SmartyPantToken](cref:Meziantou.Framework.Markdown.SyntaxKind.SmartyPantToken), whose
  value is a [MarkdownSmartyPant](cref:Meziantou.Framework.Markdown.MarkdownSmartyPant).
- [MarkdownHtmlRendererOptions.SmartyPants](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.SmartyPants)
  changes the HTML that the renderer writes for them, for instance for the quotes of another language.
