// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Globalization;
using System.Text;
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
                if (listItem[0] is HtmlBlock && GetIndentation(listItem[0]) > 0)
                {
                    // The indentation of the HTML block would be taken as the spaces after the marker, so start it on the next line
                    renderer.WriteLine();
                }

                renderer.WriteChildren(listItem);
            }
            renderer.PopIndent();
            // An open HTML block already ends with the blank line between the items
            if (i + 1 < listBlock.Count && listBlock.IsLoose && !HtmlBlockRenderer.EndsWithOpenHtmlBlock(listItem))
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
            marker = $"{GetBulletCharacter(renderer, listBlock)}";
        }
        else
        {
            var number = 0;
            if (listBlock.BulletType is '1' or 'a' or 'A' or 'i' or 'I')
            {
                if (listBlock.OrderedStart != null)
                {
                    _ = int.TryParse(listBlock.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
                }

                number += index;
            }

            // The lists of letters and roman numbers come from the list extras
            var text = listBlock.BulletType switch
            {
                'a' or 'A' when number is >= 1 and <= 26 => ((char)(listBlock.BulletType + number - 1)).ToString(),
                'i' or 'I' when number is >= 1 and <= 3999 => ToRoman(number, listBlock.BulletType == 'i'),
                _ => number.ToString(CultureInfo.InvariantCulture),
            };

            marker = $"{text}{GetBulletCharacter(renderer, listBlock)}";
        }

        // The indented lines of the block after the list would continue the last item, unless its content is indented further.
        // An empty item ends at the blank line after the list.
        var indentation = GetIndentation(GetNextSibling(listBlock));
        if (indentation > 0 && listBlock[^1] is ListItemBlock { Count: > 0 } last)
        {
            // The content of an item starting with an indented block is one space after the marker
            var maxSpaces = GetIndentation(last[0]) > 0 ? 1 : 4;

            // Indent all the items when the spaces after the marker are not enough, unless the list starts on the line of another
            // marker, or would continue the list before it
            var indent = Math.Max(0, indentation + 1 - marker.Length - maxSpaces);
            if (indent > 3 || (indent > 0 && (renderer.GetListMarkersBefore(listBlock).Length > 0 || GetPreviousSibling(listBlock) is ListBlock)))
            {
                indent = 0;
            }

            var spaces = index == listBlock.Count - 1 ? Math.Clamp(Math.Max(5, indentation + 1) - indent - marker.Length, 1, maxSpaces) : 1;
            return new string(' ', indent) + marker + new string(' ', spaces);
        }

        return marker + " ";
    }

    private static string ToRoman(int number, bool lowerCase)
    {
        ReadOnlySpan<int> values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        ReadOnlySpan<string> symbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var builder = new StringBuilder();
        for (var i = 0; i < values.Length; i++)
        {
            while (number >= values[i])
            {
                builder.Append(symbols[i]);
                number -= values[i];
            }
        }

        var result = builder.ToString();
        return lowerCase ? result.ToLowerInvariant() : result;
    }

    // Gets the bullet, or the delimiter of an ordered list. A list right after a list of the same kind uses another one,
    // so that they are not merged.
    private static char GetBulletCharacter(NormalizeRenderer renderer, ListBlock listBlock)
    {
        var character = listBlock.IsOrdered ? listBlock.OrderedDelimiter : renderer.Options.ListItemCharacter ?? listBlock.BulletType;
        if (GetPreviousSibling(listBlock) is ListBlock previous && previous.IsOrdered == listBlock.IsOrdered && GetBulletCharacter(renderer, previous) == character)
        {
            return character switch
            {
                '.' => ')',
                ')' => '.',
                '-' => '*',
                _ => '-',
            };
        }

        return character;
    }

    // Gets the indentation of the first line of a block that keeps it
    private static int GetIndentation(Block? block)
    {
        var line = block switch
        {
            FencedCodeBlock => default,
            CodeBlock { Lines.Count: > 0 } codeBlock => codeBlock.Lines.Lines[0].Slice.AsSpan(),
            HtmlBlock { Lines.Count: > 0 } htmlBlock => htmlBlock.Lines.Lines[0].Slice.AsSpan(),
            _ => default,
        };

        var indentation = block is CodeBlock and not FencedCodeBlock ? 4 : 0;
        foreach (var c in line)
        {
            if (c == ' ')
            {
                indentation++;
            }
            else if (c == '\t')
            {
                indentation += 4 - (indentation % 4);
            }
            else
            {
                break;
            }
        }

        return indentation;
    }

    private static Block? GetPreviousSibling(Block block)
    {
        var index = block.Parent?.IndexOf(block) ?? -1;
        return index > 0 ? block.Parent![index - 1] : null;
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
