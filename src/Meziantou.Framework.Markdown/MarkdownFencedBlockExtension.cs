namespace Meziantou.Framework.Markdown;

/// <summary>
/// A block between an opening line and a closing line, such as <c>!!! note</c> and <c>!!!</c>. The construct is a
/// <see cref="MarkdownExtensionBlockSyntax"/>: the opening fence, the rest of its line (the info), the content, and the
/// closing fence.
/// </summary>
/// <remarks>
/// <para>
/// The parser calls <see cref="TryOpen"/> on a line indented by 3 spaces or less whose first character is one of
/// <see cref="TriggerCharacters"/>, before the built-in blocks. The block ends at the line for which <see cref="TryClose"/>
/// returns a length, or at the end of its container, as a fenced code block. A closing fence closes the blocks in the content
/// too, even a code block.
/// </para>
/// <para>
/// One instance parses every document, possibly on several threads at once: keep no state between calls, and use
/// <see cref="MarkdownSyntaxExtension.CreateState"/> for the state of a document.
/// </para>
/// </remarks>
public abstract class MarkdownFencedBlockExtension : MarkdownSyntaxExtension
{
    /// <summary>Initializes a new instance of the <see cref="MarkdownFencedBlockExtension"/> class.</summary>
    /// <param name="name">The name of the extension, which the nodes of its blocks have.</param>
    /// <param name="triggerCharacters">The characters that an opening fence can start with: not whitespace.</param>
    /// <param name="content">What the lines between the fences are.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="triggerCharacters"/> is empty, or <paramref name="triggerCharacters"/> has whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="triggerCharacters"/> is <see langword="null"/>.</exception>
    protected MarkdownFencedBlockExtension(string name, string triggerCharacters, MarkdownFencedBlockContent content)
        : base(name)
    {
        MarkdownInlineExtension.ValidateTriggerCharacters(triggerCharacters);
        TriggerCharacters = triggerCharacters;
        Content = content;
    }

    /// <summary>Gets the characters that an opening fence can start with.</summary>
    public string TriggerCharacters { get; }

    /// <summary>Gets what the lines between the fences are.</summary>
    public MarkdownFencedBlockContent Content { get; }

    /// <summary>Gets a value indicating whether the block can start on the line after a paragraph, as a fenced code block can. When <see langword="false"/>, the line continues the paragraph.</summary>
    public bool CanInterruptParagraph { get; init; } = true;

    /// <summary>Gets a value indicating whether the info, the text after the opening fence, is read as inlines, such as emphasis: <see cref="MarkdownExtensionBlockSyntax.Inlines"/>.</summary>
    public bool ParseInfoAsInlines { get; init; }

    /// <summary>Reads the opening fence of a block.</summary>
    /// <param name="line">The line, from its first character that is not whitespace, without its line ending.</param>
    /// <returns>The length of the opening fence, the start of <paramref name="line"/>, or 0 when the line does not open a block.</returns>
    public abstract int TryOpen(ReadOnlySpan<char> line);

    /// <summary>Reads the closing fence of a block. The rest of the line after the fence must be whitespace.</summary>
    /// <param name="line">The line, from its first character that is not whitespace, without its line ending.</param>
    /// <param name="openingFence">The opening fence of the block.</param>
    /// <returns>The length of the closing fence, the start of <paramref name="line"/>, or 0 when the line does not close the block.</returns>
    public abstract int TryClose(ReadOnlySpan<char> line, ReadOnlySpan<char> openingFence);

    /// <summary>Called when the block is closed, during the reading of the blocks, to record what the block defines in the state of the document.</summary>
    /// <param name="context">The info and the lines of the block, and the state of the document.</param>
    public virtual void OnClosed(ref MarkdownFencedBlockContext context)
    {
    }
}
