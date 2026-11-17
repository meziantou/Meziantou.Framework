# Meziantou.Framework.Markdown

A CommonMark and GitHub Flavored Markdown parser for .NET, with the extensions of Markdig, an HTML renderer and a
formatter. Its syntax tree keeps every character of the text it was parsed from, so a document is written back exactly,
and an edit only changes the text of what it replaces.

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownConverter_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal("<p>Some <strong>bold</strong> text</p>\n", MarkdownConverter.ToHtml("Some **bold** text"));
Assert.Equal("<p><del>Old</del> new</p>\n", MarkdownConverter.ToHtml("~~Old~~ new", MarkdownParseOptions.GitHubFlavored));
Assert.Equal("Some bold text\n", MarkdownConverter.ToPlainText("Some **bold** text"));
```
<!-- endref -->

Version 2.0 replaces the parser of version 1.x, a fork of Markdig: see the migration guide in the documentation.

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Overview;format=md-fenced-code-block;dedent=true -->
```csharp
var text = """
    > A quote with *emphasis*
    > on two lines
    """;
var tree = MarkdownSyntaxTree.ParseText(text);

// Every character is in the tree: the markers of the quote on its second line are trivia
Assert.Equal(text, tree.GetRoot().ToFullString());
Assert.Equal("<blockquote>\n<p>A quote with <em>emphasis</em>\non two lines</p>\n</blockquote>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->
