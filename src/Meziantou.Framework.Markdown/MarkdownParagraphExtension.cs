namespace Meziantou.Framework.Markdown;

/// <summary>
/// Turns the lines of a paragraph into a block, such as the lines <c>Alice: Hello</c> of a chat transcript. The construct is a
/// <see cref="MarkdownExtensionBlockSyntax"/> whose parts are markers, text and inlines, as an inline extension reports them.
/// </summary>
/// <remarks>
/// <para>
/// The parser calls <see cref="TryConvert"/> when a paragraph is closed, after its link reference definitions are read, and
/// before the inlines of the document are read: a converter can record definitions in its state for the inline extensions.
/// A converter can take some lines of the paragraph: the lines before them stay a paragraph, and the lines after them are
/// offered to the converters again. A paragraph that is a setext heading is not offered.
/// </para>
/// <para>
/// One instance parses every document, possibly on several threads at once: keep no state between calls, and use
/// <see cref="MarkdownSyntaxExtension.CreateState"/> for the state of a document.
/// </para>
/// </remarks>
public abstract class MarkdownParagraphExtension : MarkdownSyntaxExtension
{
    /// <summary>Initializes a new instance of the <see cref="MarkdownParagraphExtension"/> class.</summary>
    /// <param name="name">The name of the extension, which the nodes of its blocks have.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    protected MarkdownParagraphExtension(string name)
        : base(name)
    {
    }

    /// <summary>Reads lines of a paragraph.</summary>
    /// <param name="context">The lines, where the block reports its parts and its lines.</param>
    /// <returns>
    /// <see langword="true"/> when some lines are a block, after calling <see cref="MarkdownParagraphConvertContext.Complete"/>;
    /// <see langword="false"/> to let the next converter read the lines, or to keep the paragraph.
    /// </returns>
    public abstract bool TryConvert(ref MarkdownParagraphConvertContext context);
}
