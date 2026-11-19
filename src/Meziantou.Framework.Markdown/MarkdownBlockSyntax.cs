using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Parsing;
using Meziantou.Framework.Markdown.Syntax.InternalSyntax;
using Green = Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Markdown;

/// <summary>A block of a document: a paragraph, a heading, a list, a block quote, a code block...</summary>
public abstract class MarkdownBlockSyntax : MarkdownSyntaxNode
{
    private protected MarkdownBlockSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the generic attributes of the block, which the HTML renderer writes on its element.</summary>
    /// <returns>The attributes, or <see cref="MarkdownGenericAttributes.Empty"/> when the block has none.</returns>
    /// <remarks>
    /// The attributes are read with <see cref="MarkdownExtensions.GenericAttributes"/>, from every <c>{...}</c> that is for the
    /// block: the ones at the end of the first line of a heading or of a fenced block, at the end of a paragraph, or on the line
    /// before the block. As in the HTML renderer, the id of the last ones wins, the classes are added, and a property is kept
    /// from the first attributes that have it. The attributes of all the blocks of a document are read once, the first time the
    /// attributes of one of them are read. Change them with <see cref="WithGenericAttributes(MarkdownGenericAttributes?)"/>,
    /// <see cref="MarkdownSyntaxTree.WithGenericAttributes(MarkdownBlockSyntax, MarkdownGenericAttributes?)"/> or
    /// <see cref="MarkdownSyntaxTreeEditor.SetGenericAttributes(MarkdownBlockSyntax, MarkdownGenericAttributes?)"/>.
    /// </remarks>
    public MarkdownGenericAttributes GetGenericAttributes() => FirstAncestorOrSelf<MarkdownDocumentSyntax>()?.GetGenericAttributes(this).Attributes ?? MarkdownGenericAttributes.Empty;

    // The attributes of a block, from the {...} that are for it, in the order of the text
    internal static MarkdownGenericAttributes MergeGenericAttributes(IReadOnlyList<MarkdownGenericAttributesSyntax> found)
    {
        string? id = null;
        List<string>? blockLineClasses = null;
        var classes = new List<string>();
        var properties = new List<KeyValuePair<string, string?>>();
        foreach (var attributes in found)
        {
            id = attributes.Id ?? id;
            if (attributes.IsBlockLine)
            {
                blockLineClasses = [.. attributes.Classes];
            }
            else
            {
                classes.AddRange(attributes.Classes);
            }

            var existingCount = properties.Count;
            foreach (var property in attributes.Properties)
            {
                if (properties.FindIndex(0, existingCount, existing => string.Equals(existing.Key, property.Key, StringComparison.Ordinal)) < 0)
                {
                    properties.Add(property);
                }
            }
        }

        // The classes of the first line of a block come first, as the renderer writes them
        if (blockLineClasses is not null)
        {
            classes.InsertRange(0, blockLineClasses);
        }

        return id is null && classes.Count == 0 && properties.Count == 0 ? MarkdownGenericAttributes.Empty : new MarkdownGenericAttributes(id, classes, properties);
    }

    /// <summary>Returns this block with the generic attributes <paramref name="attributes"/>, or itself when it has them.</summary>
    /// <param name="attributes">The attributes of the block, or <see langword="null"/> to remove them.</param>
    /// <returns>A copy of the block, in no tree, whose <see cref="GetGenericAttributes"/> returns <paramref name="attributes"/> once it replaces this block.</returns>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="MarkdownSyntaxTree.WithGenericAttributes(MarkdownBlockSyntax, MarkdownGenericAttributes?)"/>, which
    /// parses the document again, this method only changes the block, so it can be called for many blocks, such as in a
    /// <see cref="MarkdownSyntaxRewriter"/>: replace this block with the copy, with
    /// <see cref="SyntaxNodeExtensions.ReplaceNode{TRoot}(TRoot, SyntaxNode, SyntaxNode)"/>, or return the copy from the rewriter.
    /// The block has to be in a tree, so that its attributes and the options of the tree are known.
    /// </para>
    /// <para>
    /// The attributes are written in the block, where Markdig reads them: at the end of the first line of a heading, of a fenced
    /// code block, of a math block and of a custom container, and at the end of the last line of a paragraph or of the text of a
    /// setext heading. The <c>{...}</c> that are for the block are removed: they have to be in the block, at the end of a line that
    /// does not continue the text of a paragraph. To check that the copy is the block that parsing its text gives, with the attributes,
    /// and that the attributes do not change how the text around it is read, only the block at the top of the document that contains
    /// the block is parsed again, with the blocks before and after it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="attributes"/> cannot be written, such as a class with whitespace or an attribute name that starts with a digit.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The block is in no tree, or its tree is parsed without <see cref="MarkdownExtensions.GenericAttributes"/>, or the attributes
    /// cannot be written in the block: the block is of another kind, such as a list or a block quote, whose attributes are on a line
    /// before it, it has attributes on a line before it or before a line break of its text, or its text would not read them as its
    /// attributes, such as a heading that has a <c>{</c>, or the attributes would change how the blocks around it are read. Change them with
    /// <see cref="MarkdownSyntaxTree.WithGenericAttributes(MarkdownBlockSyntax, MarkdownGenericAttributes?)"/>.
    /// </exception>
    public MarkdownBlockSyntax WithGenericAttributes(MarkdownGenericAttributes? attributes)
    {
        if (FirstAncestorOrSelf<MarkdownDocumentSyntax>() is not { SyntaxTree: MarkdownSyntaxTree tree } document)
            throw new InvalidOperationException("The block is in no tree: its attributes cannot be read.");

        if (!tree.Options.Extensions.HasFlag(MarkdownExtensions.GenericAttributes))
            throw new InvalidOperationException("The tree is parsed without generic attributes.");

        attributes ??= MarkdownGenericAttributes.Empty;
        var markdown = attributes.IsEmpty ? null : attributes.ToMarkdown(nameof(attributes));
        var (current, nodes) = document.GetGenericAttributes(this);
        if (current.Equals(attributes))
            return this;

        if (this is not (MarkdownHeadingSyntax or MarkdownParagraphSyntax or MarkdownCodeBlockSyntax { IsFenced: true } or MarkdownMathBlockSyntax or MarkdownCustomContainerSyntax))
            throw CannotWriteAttributes();

        // The attributes are removed from the last to the first, so that the index of the ones before does not change
        var children = MarkdownNodeEditor.GetChildren(this);
        var childNodes = ChildNodesAndTokens();
        foreach (var node in nodes.OrderByDescending(node => node.SpanStart))
        {
            var index = node.Parent == this ? IndexOf(childNodes, node) : -1;
            if (index <= 0 || node.ChildTokens().Any(token => token.RawKind == (int)SyntaxKind.LineEndingToken))
                throw CannotWriteAttributes();

            MarkdownNodeEditor.RemoveOnLine(children, index, index);
        }

        if (markdown is not null)
        {
            // The attributes of the first line of a block are read by the block, and the other ones by the inlines, after whitespace
            var (index, mark) = this switch
            {
                MarkdownHeadingSyntax { IsSetext: true } => (MarkdownNodeEditor.IndexOfToken(children, SyntaxKind.SetextHeadingUnderlineToken) - 1, 2),
                MarkdownHeadingSyntax => (children.Count - 1, 3),
                MarkdownParagraphSyntax => (children.Count - 1, 2),
                MarkdownCodeBlockSyntax => (GetFirstLineEnd(children, SyntaxKind.CodeFenceToken), 3),
                MarkdownMathBlockSyntax => (GetFirstLineEnd(children, SyntaxKind.MathFenceToken), 3),
                _ => (GetFirstLineEnd(children, SyntaxKind.CustomContainerFenceToken), 3),
            };

            if (index < 0)
                throw CannotWriteAttributes();

            var token = new Green.SyntaxTokenWithValue<int>((int)SyntaxKind.AttributesToken, markdown, mark, markdown, leadingTrivia: null, trailingTrivia: null, isMissing: false);
            MarkdownNodeEditor.InsertOnLineAfter(children, index, new MarkdownNode(SyntaxKind.GenericAttributes, [token]));
        }

        var updated = MarkdownNodeEditor.WithChildren(this, children);

        // The lines of the block can change how the blocks around it are read, such as a paragraph that the first line of a block
        // interrupts, or a block quote that is an alert. The block at the top of the document that contains the block is parsed with the
        // blocks before and after it, and the definitions of the document, with the block and with the copy: everything is read the
        // same way except the attributes, the block has the attributes, and they are read as they are written in the copy. The
        // blocks parsed without the text before them are read as they are in the document, such as the lines of a paragraph that
        // continues a block before it, which are not read as an indented code block.
        var blocks = GetEnclosingBlocks(document, tree);
        var (start, end) = (blocks[0].FullSpan.Start, blocks[^1].FullSpan.End);
        var text = tree.GetText().Text;
        var prefix = start > 0 ? "\n" : "";
        var offset = SpanStart - start + prefix.Length;
        var originalRoot = tree.ParsePart(string.Concat(prefix, text.AsSpan(start, end - start)));
        var updatedRoot = tree.ParsePart(string.Concat(prefix, text.AsSpan(start, FullSpan.Start - start), updated.ToFullString(), text.AsSpan(FullSpan.End, end - FullSpan.End)));
        if (FindBlock(originalRoot, offset, RawKind) is not { } original || FindBlock(updatedRoot, offset, RawKind) is not { } parsed
            || !GetShape(blocks, attributes: false).SequenceEqual(GetShape(originalRoot.ChildNodes().Cast<MarkdownSyntaxNode>(), attributes: false))
            || !GetShape(blocks, attributes: true).SequenceEqual(GetShape(originalRoot.ChildNodes().Cast<MarkdownSyntaxNode>(), attributes: true))
            || !original.GetGenericAttributes().Equals(current)
            || !parsed.GetGenericAttributes().Equals(attributes)
            || !GetShape(originalRoot, attributes: false).SequenceEqual(GetShape(updatedRoot, attributes: false))
            || !GetShape(parsed, attributes: true).SequenceEqual(GetShape(updated, attributes: true)))
        {
            throw CannotWriteAttributes();
        }

        return updated;

        InvalidOperationException CannotWriteAttributes() => new($"The attributes of the {Kind()} cannot be written in it: change them with {nameof(MarkdownSyntaxTree)}.{nameof(MarkdownSyntaxTree.WithGenericAttributes)}.");
    }

    private static int IndexOf(ChildSyntaxList children, MarkdownGenericAttributesSyntax node)
    {
        var index = 0;
        foreach (var child in children)
        {
            if (child.AsNode() == node)
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    // The child that ends the first line of a fenced block: its info string, or its fence
    private static int GetFirstLineEnd(List<GreenNode?> children, SyntaxKind fence)
    {
        var index = MarkdownNodeEditor.IndexOfToken(children, fence);
        return index >= 0 && index + 1 < children.Count && children[index + 1] is { IsToken: true, RawKind: (int)SyntaxKind.InfoStringToken } ? index + 1 : index;
    }

    // The block at the top of the document that contains this block, with the blocks before and after it. A YAML front matter that
    // is not closed is read as the blocks it is made of, until a line closes it: the line of the attributes, once they are removed,
    // can close it, so the blocks from the start of the document are parsed.
    private List<MarkdownBlockSyntax> GetEnclosingBlocks(MarkdownDocumentSyntax document, MarkdownSyntaxTree tree)
    {
        var fromStart = tree.Options.Extensions.HasFlag(MarkdownExtensions.YamlFrontMatter) && !tree.IsCell && BlockParser.StartsWithFrontMatterFence(tree.GetText().Text) && document.Blocks is not [MarkdownFrontMatterSyntax, ..];

        SyntaxNode top = this;
        while (top.Parent is { } parent && parent != document)
        {
            top = parent;
        }

        // The children of the document are in the order of the text
        var children = document.ChildNodesAndTokens();
        var low = 0;
        var high = children.Count - 1;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (children[middle].FullSpan.End <= top.FullSpan.Start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        var blocks = new List<MarkdownBlockSyntax>(3);
        for (var i = low - 1; i >= 0; i--)
        {
            if (children[i].AsNode() is MarkdownBlockSyntax previous)
            {
                blocks.Add(previous);
                if (!fromStart)
                {
                    break;
                }
            }
        }

        blocks.Reverse();

        blocks.Add((MarkdownBlockSyntax)top);
        for (var i = low + 1; i < children.Count; i++)
        {
            if (children[i].AsNode() is MarkdownBlockSyntax next)
            {
                blocks.Add(next);
                break;
            }
        }

        return blocks;
    }

    // The outermost block of the kind that starts at the position
    private static MarkdownBlockSyntax? FindBlock(MarkdownDocumentSyntax root, int position, int rawKind)
        => root.DescendantNodes(node => node.FullSpan.Start <= position && position <= node.FullSpan.End).OfType<MarkdownBlockSyntax>().FirstOrDefault(block => block.SpanStart == position && block.RawKind == rawKind);

    // The shape of the nodes, one after the other, with the nodes themselves
    private static IEnumerable<(int Kind, int Count, string? Text, object? Value)> GetShape(IEnumerable<MarkdownSyntaxNode> nodes, bool attributes)
    {
        foreach (var node in nodes)
        {
            if (!attributes)
            {
                yield return (node.RawKind, node.ChildNodesAndTokens().Count(item => item.AsNode() is not MarkdownGenericAttributesSyntax), null, null);
            }

            foreach (var item in GetShape(node, attributes))
            {
                yield return item;
            }
        }
    }

    // The nodes and the tokens of a node, without their trivia, in the order of the text: a node with the number of its children,
    // which gives the structure, and a token with its text and its value. Either the generic attributes, or everything else.
    private static IEnumerable<(int Kind, int Count, string? Text, object? Value)> GetShape(MarkdownSyntaxNode root, bool attributes)
    {
        foreach (var child in root.DescendantNodesAndTokens(node => node is not MarkdownGenericAttributesSyntax || attributes))
        {
            var inAttributes = child.AsNode() is MarkdownGenericAttributesSyntax || child.Parent is MarkdownGenericAttributesSyntax;
            if (inAttributes != attributes)
            {
                continue;
            }

            if (child.AsNode(out var node))
            {
                yield return (node.RawKind, node.ChildNodesAndTokens().Count(item => attributes || item.AsNode() is not MarkdownGenericAttributesSyntax), null, null);
            }
            else
            {
                var token = child.AsToken();
                yield return (token.RawKind, -1, token.Text, token.Value);
            }
        }
    }
}
