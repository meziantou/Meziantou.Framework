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

    [Theory]
    [MemberData(nameof(CommonMarkSpec.ExampleNumbers), MemberType = typeof(CommonMarkSpec))]
    public void TreeReproducesTheTextWithTheExtensions(int number)
    {
        var example = CommonMarkSpec.Get(number);

        var tree = MarkdownSyntaxTree.ParseText(example.Markdown, MarkdownParseOptions.GitHubFlavored);

        Assert.Equal(example.Markdown, tree.GetRoot().ToFullString());
    }

    [Theory]
    [MemberData(nameof(CommonMarkSpec.ExampleNumbers), MemberType = typeof(CommonMarkSpec))]
    public void ExtensionsKeepTheHtmlOfTheSpecification(int number)
    {
        var example = CommonMarkSpec.Get(number);

        var html = MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(example.Markdown, MarkdownParseOptions.GitHubFlavored));

        // The examples whose text is an extension: ~~, www., an email address, or a tag that the tag filter escapes
        if (ExamplesChangedByTheExtensions.Contains(number))
        {
            Assert.NotEqual(example.Html, html);
        }
        else
        {
            Assert.Equal(example.Html, html, $"Example {number} ({example.Section})");
        }
    }

    [Theory]
    [MemberData(nameof(CommonMarkSpec.GfmExampleNumbers), MemberType = typeof(CommonMarkSpec))]
    public void TreeReproducesTheTextOfTheGfmExtensions(int number)
    {
        var example = CommonMarkSpec.GetGfm(number);

        var tree = MarkdownSyntaxTree.ParseText(example.Markdown, MarkdownParseOptions.GitHubFlavored);

        Assert.Equal(example.Markdown, tree.GetRoot().ToFullString());
    }

    [Theory]
    [MemberData(nameof(CommonMarkSpec.GfmExampleNumbers), MemberType = typeof(CommonMarkSpec))]
    public void RendersTheHtmlOfTheGfmExtensions(int number)
    {
        var example = CommonMarkSpec.GetGfm(number);

        var html = MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(example.Markdown, MarkdownParseOptions.GitHubFlavored));

        Assert.Equal(example.Html, html, $"Example {number} ({example.Section})");
    }

    private static readonly HashSet<int> ExamplesChangedByTheExtensions = [170, 171, 172, 173, 176, 178, 602, 606, 608, 611, 612];
}
