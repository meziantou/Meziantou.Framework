namespace Meziantou.Framework.Language.Markdown.Tests;

public sealed class CommonMarkSpecTests
{
    [Theory]
    [MemberData(nameof(CommonMarkSpec.ExampleNumbers), MemberType = typeof(CommonMarkSpec))]
    public void TreeReproducesTheText(int number)
    {
        var example = CommonMarkSpec.Get(number);

        var tree = MarkdownSyntaxTree.ParseText(example.Markdown);

        Assert.Equal(example.Markdown, tree.GetRoot().ToFullString());
    }

    [Theory]
    [MemberData(nameof(CommonMarkSpec.ExampleNumbers), MemberType = typeof(CommonMarkSpec))]
    public void RendersTheHtmlOfTheSpecification(int number)
    {
        var example = CommonMarkSpec.Get(number);

        var html = MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(example.Markdown));

        Assert.Equal(example.Html, html, $"Example {number} ({example.Section})");
    }
}
