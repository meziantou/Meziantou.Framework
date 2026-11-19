using Meziantou.Framework.Language;

namespace Meziantou.Framework.Markdown;

/// <summary>
/// Writes the HTML of a node for a <see cref="MarkdownHtmlNodeRenderer"/>: text and attribute values escaped, URLs changed
/// as the options of the renderer change them, the attributes of the node, and its content.
/// </summary>
/// <remarks>
/// <see cref="MarkdownPlainTextRenderer"/> uses the node renderers too: then <see cref="IsPlainText"/> is
/// <see langword="true"/>, the text is written as it is, and the HTML, the attributes and the URLs are not written.
/// </remarks>
public sealed class MarkdownHtmlWriter
{
    private readonly MarkdownHtmlRenderer.Writer _writer;

    internal MarkdownHtmlWriter(MarkdownHtmlRenderer.Writer writer)
    {
        _writer = writer;
    }

    /// <summary>Gets the options of the renderer.</summary>
    public MarkdownHtmlRendererOptions Options => _writer.RendererOptions;

    /// <summary>Gets a value indicating whether the document is written as plain text, by <see cref="MarkdownPlainTextRenderer"/>.</summary>
    public bool IsPlainText => _writer.PlainText;

    /// <summary>Writes text, with the characters of HTML escaped. Plain text writes it as it is.</summary>
    public void WriteText(ReadOnlySpan<char> text) => _writer.AppendEscaped(text);

    /// <summary>Writes HTML as it is, such as tags. Plain text does not write it.</summary>
    /// <remarks>
    /// The HTML is not checked: <see cref="MarkdownParseOptions.DisableHtml"/>, <see cref="MarkdownExtensions.TagFilter"/>
    /// and <see cref="MarkdownHtmlRendererOptions.AttributeFilter"/> apply to the Markdown, not to the HTML of node renderers.
    /// Escape the text that comes from the document with <see cref="WriteText"/> or <see cref="WriteAttributeValue"/>.
    /// </remarks>
    public void WriteRawHtml(ReadOnlySpan<char> html) => _writer.AppendMarkup(html);

    /// <summary>Writes the value of an attribute, between its quotes, with the characters of HTML escaped. Plain text does not write it.</summary>
    public void WriteAttributeValue(ReadOnlySpan<char> value) => _writer.AppendAttributeValue(value);

    /// <summary>
    /// Writes a URL as the value of an attribute, as the renderer writes the URLs of the links: made absolute with
    /// <see cref="MarkdownHtmlRendererOptions.BaseUrl"/>, changed by <see cref="MarkdownHtmlRendererOptions.LinkRewriter"/>,
    /// percent-encoded, written empty when its scheme is not one of <see cref="MarkdownHtmlRendererOptions.AllowedUrlSchemes"/>,
    /// then escaped. Plain text does not write it.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="url"/> is <see langword="null"/>.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "The URL is written to the HTML as the Markdown has it")]
    public void WriteUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        _writer.AppendUrl(url);
    }

    /// <summary>
    /// Writes the attributes of the element of a node, each after a space, to write between the name of the tag and its
    /// <c>&gt;</c>: its id, its classes and the other attributes of <see cref="MarkdownExtensions.GenericAttributes"/>, the
    /// attributes of <see cref="MarkdownExtensions.Bootstrap"/> and of <see cref="MarkdownExtensions.Globalization"/>, and the
    /// id of <see cref="MarkdownHtmlRendererOptions.PragmaLines"/>. Plain text does not write them.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="classes">The classes of the element, before the classes of the generic attributes, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    public void WriteAttributes(MarkdownSyntaxNode node, string? classes = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        _writer.AppendNodeAttributes(node, classes);
    }

    /// <summary>Writes blocks, offering each one to the node renderers.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="blocks"/> is <see langword="null"/>.</exception>
    public void WriteBlocks(IEnumerable<MarkdownBlockSyntax> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        foreach (var block in blocks)
        {
            _writer.WriteBlock(block);
        }
    }

    /// <summary>
    /// Writes inlines, such as the <c>Inlines</c> of a paragraph or of a heading, offering each inline node to the node
    /// renderers. The tokens are written as the renderer writes them: text escaped, line endings and line breaks.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="inlines"/> is <see langword="null"/>.</exception>
    public void WriteInlines(IEnumerable<SyntaxNodeOrToken> inlines)
    {
        ArgumentNullException.ThrowIfNull(inlines);
        _writer.WriteInlineSequence(inlines);
    }

    /// <summary>
    /// Writes a block or an inline as the renderer writes it without node renderers, to add HTML around it. The nodes in it
    /// are still offered to the node renderers.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="node"/> is not a block or an inline.</exception>
    public void WriteDefault(MarkdownSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is not (MarkdownBlockSyntax or MarkdownInlineSyntax))
        {
            throw new ArgumentException($"The node {node.GetType().Name} is not a block or an inline.", nameof(node));
        }

        _writer.WriteDefault(node);
    }

    /// <summary>Starts a new line, unless the output is empty or already ends with a line ending, as the renderer does between blocks.</summary>
    public void EnsureNewLine() => _writer.EnsureNewLine();
}
