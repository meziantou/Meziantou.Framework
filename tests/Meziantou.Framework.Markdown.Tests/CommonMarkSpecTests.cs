namespace Meziantou.Framework.Markdown.Tests;

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

    // The extensions whose syntax no CommonMark example uses, as the extensions of the "advanced" set of the 1.x library
    [Theory]
    [MemberData(nameof(CommonMarkSpec.ExampleNumbers), MemberType = typeof(CommonMarkSpec))]
    public void ExtensionsThatKeepCommonMarkKeepTheHtmlOfTheSpecification(int number)
    {
        var example = CommonMarkSpec.Get(number);
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.EmphasisExtras | MarkdownExtensions.Mathematics | MarkdownExtensions.Diagrams | MarkdownExtensions.Abbreviations | MarkdownExtensions.Footnotes | MarkdownExtensions.GenericAttributes };

        var tree = MarkdownSyntaxTree.ParseText(example.Markdown, options);

        Assert.Equal(example.Markdown, tree.GetRoot().ToFullString());
        Assert.Equal(example.Html, MarkdownHtmlRenderer.ToHtml(tree), $"Example {number} ({example.Section})");
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

    [Theory]
    [MemberData(nameof(CommonMarkSpec.ExampleNumbers), MemberType = typeof(CommonMarkSpec))]
    public void FormattingKeepsTheHtmlOfTheSpecification(int number)
    {
        var example = CommonMarkSpec.Get(number);

        CommonMarkSpec.AssertFormattingKeepsTheHtml(example.Markdown, MarkdownParseOptions.Default, $"Example {number} ({example.Section})");
        CommonMarkSpec.AssertFormattingKeepsTheHtml(example.Markdown, MarkdownParseOptions.GitHubFlavored, $"Example {number} ({example.Section}), with GFM");
    }

    [Theory]
    [MemberData(nameof(CommonMarkSpec.GfmExampleNumbers), MemberType = typeof(CommonMarkSpec))]
    public void FormattingKeepsTheHtmlOfTheGfmExtensions(int number)
    {
        var example = CommonMarkSpec.GetGfm(number);

        CommonMarkSpec.AssertFormattingKeepsTheHtml(example.Markdown, MarkdownParseOptions.GitHubFlavored, $"Example {number} ({example.Section})");
    }

    private static readonly HashSet<int> ExamplesChangedByTheExtensions = [170, 171, 172, 173, 176, 178, 602, 606, 608, 611, 612];
}
