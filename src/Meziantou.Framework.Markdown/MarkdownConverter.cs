namespace Meziantou.Framework.Markdown;

/// <summary>
/// Converts Markdown text to HTML or to plain text in one call: it parses the text with <see cref="MarkdownSyntaxTree.ParseText(string, MarkdownParseOptions?, string?)"/>,
/// then renders the tree with <see cref="MarkdownHtmlRenderer"/> or <see cref="MarkdownPlainTextRenderer"/>.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs" ref:region="MarkdownConverter_Usage" ref:dedent="true">
/// Assert.Equal("&lt;p&gt;Some &lt;strong&gt;bold&lt;/strong&gt; text&lt;/p&gt;\n", MarkdownConverter.ToHtml("Some **bold** text"));
/// Assert.Equal("&lt;p&gt;&lt;del&gt;Old&lt;/del&gt; new&lt;/p&gt;\n", MarkdownConverter.ToHtml("~~Old~~ new", MarkdownParseOptions.GitHubFlavored));
/// Assert.Equal("Some bold text\n", MarkdownConverter.ToPlainText("Some **bold** text"));
/// </code>
/// </example>
public static class MarkdownConverter
{
    /// <summary>Converts CommonMark text to HTML.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="markdown"/> is <see langword="null"/>.</exception>
    public static string ToHtml(string markdown) => ToHtml(markdown, parseOptions: null, rendererOptions: null);

    /// <summary>Converts Markdown text to HTML.</summary>
    /// <param name="markdown">The text.</param>
    /// <param name="parseOptions">The extensions to read, or <see langword="null"/> for <see cref="MarkdownParseOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="markdown"/> is <see langword="null"/>.</exception>
    public static string ToHtml(string markdown, MarkdownParseOptions? parseOptions) => ToHtml(markdown, parseOptions, rendererOptions: null);

    /// <summary>Converts Markdown text to HTML.</summary>
    /// <param name="markdown">The text.</param>
    /// <param name="parseOptions">The extensions to read, or <see langword="null"/> for <see cref="MarkdownParseOptions.Default"/>.</param>
    /// <param name="rendererOptions">How to write the HTML, or <see langword="null"/> for <see cref="MarkdownHtmlRendererOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="markdown"/> is <see langword="null"/>.</exception>
    public static string ToHtml(string markdown, MarkdownParseOptions? parseOptions, MarkdownHtmlRendererOptions? rendererOptions)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        return MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(markdown, parseOptions), rendererOptions);
    }

    /// <summary>Converts CommonMark text to plain text.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="markdown"/> is <see langword="null"/>.</exception>
    public static string ToPlainText(string markdown) => ToPlainText(markdown, parseOptions: null, rendererOptions: null);

    /// <summary>Converts Markdown text to plain text.</summary>
    /// <param name="markdown">The text.</param>
    /// <param name="parseOptions">The extensions to read, or <see langword="null"/> for <see cref="MarkdownParseOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="markdown"/> is <see langword="null"/>.</exception>
    public static string ToPlainText(string markdown, MarkdownParseOptions? parseOptions) => ToPlainText(markdown, parseOptions, rendererOptions: null);

    /// <summary>Converts Markdown text to plain text.</summary>
    /// <param name="markdown">The text.</param>
    /// <param name="parseOptions">The extensions to read, or <see langword="null"/> for <see cref="MarkdownParseOptions.Default"/>.</param>
    /// <param name="rendererOptions">How to write the text, or <see langword="null"/> for <see cref="MarkdownHtmlRendererOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="markdown"/> is <see langword="null"/>.</exception>
    public static string ToPlainText(string markdown, MarkdownParseOptions? parseOptions, MarkdownHtmlRendererOptions? rendererOptions)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        return MarkdownPlainTextRenderer.ToPlainText(MarkdownSyntaxTree.ParseText(markdown, parseOptions), rendererOptions);
    }
}
