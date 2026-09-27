// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Normalize;

/// <summary>
/// A Normalize renderer for a <see cref="QuoteBlock"/>.
/// </summary>
/// <seealso cref="NormalizeObjectRenderer{QuoteBlock}" />
public class QuoteBlockRenderer : NormalizeObjectRenderer<QuoteBlock>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(NormalizeRenderer renderer, QuoteBlock obj)
    {
        var quoteIndent = renderer.Options.SpaceAfterQuoteBlock ? obj.QuoteChar + " " : obj.QuoteChar.ToString();
        renderer.PushIndent(quoteIndent);
        if (obj.Count == 0)
        {
            // Emit the quote prefix and any pending list marker even without children.
            renderer.Write("");
        }
        else
        {
            // The blocks of the quote are separated even when the quote is in a tight list
            var compact = renderer.CompactParagraph;
            renderer.CompactParagraph = false;
            renderer.WriteChildren(obj);
            renderer.CompactParagraph = compact;
        }

        var next = GetNextSibling(obj);
        if (renderer.CompactParagraph && next is ParagraphBlock && IsLastBlockParagraph(obj))
        {
            // Without a blank line, which would make the list loose, an empty quote line ends the paragraph
            renderer.WriteLine();
            renderer.Write(string.Empty);
        }

        renderer.PopIndent();

        if (renderer.CompactParagraph && next is QuoteBlock)
        {
            // Adjacent quotes would merge
            renderer.EnsureLine();
            renderer.WriteLine();
        }
        else
        {
            renderer.FinishBlock(true);
        }
    }

    private static Block? GetNextSibling(Block block)
    {
        if (block.Parent is not { } parent)
        {
            return null;
        }

        var index = parent.IndexOf(block);
        return index >= 0 && index + 1 < parent.Count ? parent[index + 1] : null;
    }

    private static bool IsLastBlockParagraph(ContainerBlock container)
    {
        var block = container.LastChild;
        while (block is ContainerBlock child)
        {
            block = child.LastChild;
        }

        return block is ParagraphBlock;
    }
}
