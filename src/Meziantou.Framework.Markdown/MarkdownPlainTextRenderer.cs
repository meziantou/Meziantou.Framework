namespace Meziantou.Framework.Markdown;

/// <summary>
/// Renders a Markdown syntax tree as plain text, as <c>MarkdownConverter.ToPlainText</c> of Meziantou.Framework.Markdown 1.x
/// does: the text of the HTML, without its tags and its HTML, and not escaped.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs" ref:region="MarkdownPlainTextRenderer_Usage" ref:dedent="true">
/// var tree = MarkdownSyntaxTree.ParseText("# Title\n\nSome **bold** text and [a link](https://example.com).\n\n- First\n- Second");
///
/// Assert.Equal("Title\nSome bold text and a link.\nFirst\nSecond\n", MarkdownPlainTextRenderer.ToPlainText(tree));
/// </code>
/// </example>
public static class MarkdownPlainTextRenderer
{
    /// <summary>Renders the document as plain text.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public static string ToPlainText(MarkdownSyntaxTree tree) => ToPlainText(tree, options: null);

    /// <summary>Renders the document as plain text.</summary>
    /// <param name="tree">The document.</param>
    /// <param name="options">
    /// How to write the text, or <see langword="null"/> for <see cref="MarkdownHtmlRendererOptions.Default"/>: only the options
    /// that write text are used, such as the characters of SmartyPants, which are written decoded.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public static string ToPlainText(MarkdownSyntaxTree tree, MarkdownHtmlRendererOptions? options) => MarkdownHtmlRenderer.Render(tree, options, plainText: true);
}
