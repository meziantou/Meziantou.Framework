namespace Meziantou.Framework.Markdown;

/// <summary>
/// A syntax that the parser reads in addition to CommonMark and to <see cref="MarkdownExtensions"/>: a
/// <see cref="MarkdownInlineExtension"/>, a <see cref="MarkdownDelimiterExtension"/>, a <see cref="MarkdownFencedBlockExtension"/>
/// or a <see cref="MarkdownParagraphExtension"/>. Add it to <see cref="MarkdownParseOptions.SyntaxExtensions"/>.
/// </summary>
/// <remarks>
/// The constructs of an extension are <see cref="MarkdownExtensionInlineSyntax"/> or <see cref="MarkdownExtensionBlockSyntax"/>
/// nodes, whose name is the <see cref="Name"/> of the extension. One instance parses every document, possibly on several
/// threads at once: keep no state between calls, and use <see cref="CreateState"/> for the state of a document.
/// </remarks>
public abstract class MarkdownSyntaxExtension
{
    private protected MarkdownSyntaxExtension(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>Gets the name of the extension, which the nodes of its constructs have: unique in <see cref="MarkdownParseOptions.SyntaxExtensions"/>.</summary>
    public string Name { get; }

    /// <summary>
    /// Creates the state of the extension for one document, such as the definitions that its blocks record and that its
    /// inlines read. The parser calls it the first time a context of the extension asks for its state, at most once per
    /// document.
    /// </summary>
    /// <remarks>
    /// The block extensions run before the inline extensions: every fenced block and paragraph of the document is read before
    /// the first inline. The cells of a grid table are documents of their own, with their own state.
    /// </remarks>
    /// <returns>The state, or <see langword="null"/> for none.</returns>
    public virtual object? CreateState() => null;
}
