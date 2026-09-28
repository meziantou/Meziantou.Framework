// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics;

using Meziantou.Framework.Markdown.Extensions.Tables;
using Meziantou.Framework.Markdown.Extensions.TaskLists;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Extensions.Globalization;

/// <summary>
/// Extension to add support for RTL content.
/// </summary>
public class GlobalizationExtension : IMarkdownExtension
{

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        // Make sure we don't have a delegate twice
        pipeline.DocumentProcessed -= Pipeline_DocumentProcessed;
        pipeline.DocumentProcessed += Pipeline_DocumentProcessed;
    }

    private void Pipeline_DocumentProcessed(MarkdownDocument document)
    {
        var cache = new Dictionary<MarkdownObject, bool>(ReferenceEqualityComparer.Instance);
        var chain = new List<MarkdownObject>();
        foreach (var node in document.Descendants())
        {
            if (node is TableRow || node is TableCell || node is ListItemBlock)
                continue;

            if (ShouldBeRightToLeft(node, cache, chain))
            {
                var attributes = node.GetAttributes();
                attributes.AddPropertyIfNotExist("dir", "rtl");

                if (node is Table)
                {
                    attributes.AddPropertyIfNotExist("align", "right");
                }
            }
        }
    }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {

    }

    // The direction of a node is the direction of its first child (the inline container for a leaf block). The chain of
    // first children is followed with a loop and the result is cached for every node of the chain: a recursive walk
    // overflows the stack on deeply nested inlines, and computing it again for each node is quadratic.
    private static bool ShouldBeRightToLeft(MarkdownObject item, Dictionary<MarkdownObject, bool> cache, List<MarkdownObject> chain)
    {
        chain.Clear();
        MarkdownObject? current = item;
        bool result;
        while (true)
        {
            if (current is not null && cache.TryGetValue(current, out result))
                break;

            if (current is IEnumerable<MarkdownObject> container)
            {
                MarkdownObject? firstChild = null;
                foreach (var child in container)
                {
                    // TaskList items contain an "X", which will cause
                    // the function to always return false.
                    if (child is TaskList)
                        continue;

                    firstChild = child;
                    break;
                }

                if (firstChild is not null)
                {
                    chain.Add(current);
                    current = firstChild;
                    continue;
                }
            }
            else if (current is LeafBlock leaf)
            {
                chain.Add(current);
                current = leaf.Inline;
                continue;
            }
            else if (current is LiteralInline literal)
            {
                chain.Add(current);
                result = StartsWithRtlCharacter(literal.Content);
                break;
            }

            if (current is not null)
            {
                chain.Add(current);
            }

            result = StartsWithRtlParagraph(current);
            break;
        }

        foreach (var node in chain)
        {
            cache[node] = result;
        }

        return result;
    }

    private static bool StartsWithRtlParagraph(MarkdownObject? item)
    {
        if (item is null)
            return false;

        foreach (var paragraph in item.Descendants<ParagraphBlock>())
        {
            foreach (var inline in paragraph.Inline!)
            {
                if (inline is LiteralInline literal)
                {
                    return StartsWithRtlCharacter(literal.Content);
                }
            }
        }

        return false;
    }

    private static bool StartsWithRtlCharacter(StringSlice slice)
    {
        for (int i = slice.Start; i <= slice.End; i++)
        {
            char c = slice[i];
            if (c < 128)
            {
                if (CharHelper.IsAlpha(c))
                {
                    return false;
                }

                continue;
            }

            int rune = c;
            if (char.IsHighSurrogate(c) && i < slice.End && char.IsLowSurrogate(slice[i + 1]))
            {
                Debug.Assert(char.IsSurrogatePair(c, slice[i + 1]));
                rune = char.ConvertToUtf32(c, slice[i + 1]);
                i++;
            }

            if (CharHelper.IsRightToLeft(rune))
                return true;

            if (CharHelper.IsLeftToRight(rune))
                return false;
        }

        return false;
    }
}
