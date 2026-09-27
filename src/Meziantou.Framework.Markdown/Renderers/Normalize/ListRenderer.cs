// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Globalization;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Normalize;

/// <summary>
/// A Normalize renderer for a <see cref="ListBlock"/>.
/// </summary>
/// <seealso cref="NormalizeObjectRenderer{ListBlock}" />
public class ListRenderer : NormalizeObjectRenderer<ListBlock>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(NormalizeRenderer renderer, ListBlock listBlock)
    {
        renderer.EnsureLine();
        var compact = renderer.CompactParagraph;
        renderer.CompactParagraph = !listBlock.IsLoose;
        for (var i = 0; i < listBlock.Count; i++)
        {
            var item = listBlock[i];
            var listItem = (ListItemBlock) item;
            renderer.EnsureLine();
            renderer.PushHangingIndent(GetMarker(renderer, listBlock, i));
            if (listItem.Count == 0)
            {
                renderer.Write(""); // trigger writing of indent
            }
            else
            {
                renderer.WriteChildren(listItem);
            }
            renderer.PopIndent();
            if (i + 1 < listBlock.Count && listBlock.IsLoose)
            {
                renderer.EnsureLine();
                renderer.WriteLine();
            }
        }
        renderer.CompactParagraph = compact;

        // An open HTML block would include the blank line
        if (HtmlBlockRenderer.EndsWithOpenHtmlBlock(listBlock))
        {
            renderer.EnsureLine();
        }
        else
        {
            renderer.FinishBlock(true);
        }
    }

    // Gets the marker, including the following spaces, of the item at the specified index
    internal static string GetMarker(NormalizeRenderer renderer, ListBlock listBlock, int index)
    {
        string marker;
        if (!listBlock.IsOrdered)
        {
            marker = $"{renderer.Options.ListItemCharacter ?? listBlock.BulletType}";
        }
        else
        {
            var number = 0;
            if (listBlock.BulletType == '1')
            {
                if (listBlock.OrderedStart != null)
                {
                    _ = int.TryParse(listBlock.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
                }

                number += index;
            }

            marker = $"{number.ToString(CultureInfo.InvariantCulture)}{listBlock.OrderedDelimiter}";
        }

        // The indented lines of the block after the list would continue the last item, unless its content is indented further
        if (index == listBlock.Count - 1 && StartsWithIndentation(GetNextSibling(listBlock)) && !StartsWithIndentation(listBlock[index] is ListItemBlock { Count: > 0 } item ? item[0] : null))
        {
            return marker.PadRight(Math.Max(5, marker.Length + 1));
        }

        return marker + " ";
    }

    private static bool StartsWithIndentation(Block? block)
    {
        return block switch
        {
            FencedCodeBlock => false,
            CodeBlock => true,
            HtmlBlock { Lines.Count: > 0 } htmlBlock => htmlBlock.Lines.Lines[0].Slice.AsSpan() is [' ' or '\t', ..],
            _ => false,
        };
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
}
