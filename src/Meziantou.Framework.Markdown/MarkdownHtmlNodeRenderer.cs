namespace Meziantou.Framework.Markdown;

/// <summary>
/// Writes the HTML of some nodes instead of <see cref="MarkdownHtmlRenderer"/>, such as the code blocks of a language or the
/// custom containers of a name. Add it to <see cref="MarkdownHtmlRendererOptions.NodeRenderers"/>.
/// </summary>
/// <remarks>
/// <para>
/// The renderer offers each block and each inline to the node renderers, in order, before writing it: the first one that
/// returns <see langword="true"/> writes the node. The parts of a block that are written with it, such as the items of a
/// list, the rows and the cells of a table, and the terms and the definitions of a definition list, are not offered.
/// </para>
/// <para>
/// One instance writes every document, possibly on several threads at once: keep no state between calls.
/// </para>
/// </remarks>
public abstract class MarkdownHtmlNodeRenderer
{
    /// <summary>Writes a node, or lets the next node renderer or the built-in rendering write it.</summary>
    /// <param name="node">The block or the inline to write.</param>
    /// <param name="writer">Where to write the node, only during this call.</param>
    /// <returns><see langword="true"/> when the node is written; <see langword="false"/> to let the next node renderer, or the built-in rendering, write it.</returns>
    public abstract bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer);
}
