# Meziantou.Framework.Language.Markdown

A prototype of a CommonMark parser built on `Meziantou.Framework.Language`: the tree keeps every character of the text
it was parsed from, so a document is written back exactly, and an edit only changes the text of what it replaces. It is
not published yet.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Overview;format=md-fenced-code-block;dedent=true -->
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
