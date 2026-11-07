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
            var marker = GetMarker(renderer, listBlock, i);
            renderer.PushHangingIndent(marker);
            if (listItem.Count == 0)
            {
                // An empty item cannot interrupt a paragraph: the first one, right after a paragraph in a tight list, only had
                // link reference definitions, which are written in it
                if (!(i == 0 && renderer.GetPreviousSibling(listBlock) is ParagraphBlock && listBlock.Parent is ListItemBlock { Parent: ListBlock { IsLoose: false } }
                    && renderer.WriteDefinitionsOf(listItem)))
                {
                    renderer.Write(""); // trigger writing of indent
                }
            }
            else
            {
                if ((listItem[0] is HtmlBlock && GetIndentation(listItem[0]) > 0) || IsThematicBreakAfterMarker(renderer.GetListMarkersBefore(listBlock) + marker, listItem[0]))
                {
                    // The indentation of the HTML block would be taken as the spaces after the marker, and an indented code block
                    // made of the bullet character would make a thematic break of the line, so start it on the next line
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

    // Whether the markers written on the line, followed by the first line of an indented code block, are a thematic break
    private static bool IsThematicBreakAfterMarker(string markers, Block block)
    {
        if (block is not CodeBlock { Lines.Count: > 0 } code || block is FencedCodeBlock)
        {
            return false;
        }

        // The line after each marker is the line of a nested list item
        var text = string.Concat(markers, code.Lines.Lines[0].Slice.AsSpan());
        for (var i = 0; i < markers.Length; i++)
        {
            if (markers[i] != ' ' && (i == 0 || markers[i - 1] == ' '))
            {
                var line = text.AsSpan(i).Trim(" \t");
                if (line is ['-' or '*' or '_', ..] && line.Count(line[0]) >= 3 && !line.ContainsAnyExcept(line[0], ' ', '\t'))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Gets the marker, including the following spaces, of the item at the specified index
    internal static string GetMarker(NormalizeRenderer renderer, ListBlock listBlock, int index)
    {
        var marker = GetMarkerText(renderer, listBlock, index);

        // The indented lines of the block after the list would continue the last item, unless its content is indented further.
        // An empty item ends at the blank line after the list.
        var next = renderer.GetNextSibling(listBlock);

        // The parser continues an ordered list with an item indented by 4 spaces, such as an indented code block "3." after it
        if (listBlock.IsOrdered && next is CodeBlock { Lines.Count: > 0 } orderedCode and not FencedCodeBlock && StartsWithOrderedMarker(orderedCode.Lines.Lines[0].Slice.AsSpan()))
        {
            renderer.FencedCodeBlocks.Add(orderedCode);
        }

        // A code block written fenced has no indentation
        var indentation = next is CodeBlock code && renderer.FencedCodeBlocks.Contains(code) ? 0 : GetIndentation(next);
        if (indentation > 0 && listBlock[^1] is ListItemBlock { Count: > 0 } last)
        {
            // The content of an item starting with an indented block is one space after the marker
            var maxSpaces = GetIndentation(last[0]) > 0 ? 1 : 4;

            // When the content of the last item cannot be indented further than an indented code block after the list, the
            // code block is written fenced
            var lastMarker = index == listBlock.Count - 1 ? marker : GetMarkerText(renderer, listBlock, listBlock.Count - 1);
            var (lastIndent, lastSpaces) = GetIndentAndSpaces(renderer, listBlock, lastMarker, indentation, maxSpaces, isLast: true);
            if (next is CodeBlock nextCode and not FencedCodeBlock && lastIndent + lastMarker.Length + lastSpaces <= indentation)
            {
                renderer.FencedCodeBlocks.Add(nextCode);
                return marker + " ";
            }

            var (indent, spaces) = GetIndentAndSpaces(renderer, listBlock, marker, indentation, maxSpaces, index == listBlock.Count - 1);
            return new string(' ', indent) + marker + new string(' ', spaces);
        }

        // In a tight list, no blank line ends an empty last item, whose content starts one space after its marker: the items are
        // indented so that the code block after the list is not in it, or the code block is written fenced
        if (indentation > 0 && listBlock[^1] is ListItemBlock { Count: 0 } && next is CodeBlock emptyItemCode and not FencedCodeBlock
            && listBlock.Parent is ListItemBlock { Parent: ListBlock { IsLoose: false } })
        {
            var lastMarker = index == listBlock.Count - 1 ? marker : GetMarkerText(renderer, listBlock, listBlock.Count - 1);
            var indent = Math.Max(0, indentation - lastMarker.Length);
            if (indent > 3 || (indent > 0 && (renderer.GetListMarkersBefore(listBlock).Length > 0 || renderer.GetPreviousSibling(listBlock) is ListBlock)))
            {
                renderer.FencedCodeBlocks.Add(emptyItemCode);
                return marker + " ";
            }

            return new string(' ', indent) + marker + " ";
        }

        return marker + " ";
    }

    private static (int Indent, int Spaces) GetIndentAndSpaces(NormalizeRenderer renderer, ListBlock listBlock, string marker, int indentation, int maxSpaces, bool isLast)
    {
        // Indent all the items when the spaces after the marker are not enough, unless the list starts on the line of another
        // marker, or would continue the list before it. An empty item, followed by the blank line after its list, ends there.
        var indent = Math.Max(0, indentation + 1 - marker.Length - maxSpaces);
        if (indent > 3 || (indent > 0 && (renderer.GetListMarkersBefore(listBlock).Length > 0 || renderer.GetPreviousSibling(listBlock) is ListBlock { LastChild: not ListItemBlock { Count: 0 } })))
        {
            indent = 0;
        }

        var spaces = isLast ? Math.Clamp(Math.Max(5, indentation + 1) - indent - marker.Length, 1, maxSpaces) : 1;
        return (indent, spaces);
    }

    // Gets the marker, without the following spaces, of the item at the specified index
    private static string GetMarkerText(NormalizeRenderer renderer, ListBlock listBlock, int index)
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

        return marker;
    }

    private static bool StartsWithOrderedMarker(ReadOnlySpan<char> line)
    {
        var digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits]))
        {
            digits++;
        }

        return digits is > 0 and <= 9 && digits < line.Length && line[digits] is '.' or ')';
    }

    private static bool IsDelimiterRowItem(NormalizeRenderer renderer, ListBlock list)
    {
        if (list.Count == 0 || list[0] is not ListItemBlock { Count: > 0 } first || first[0] is not ParagraphBlock paragraph
            || renderer.Pipeline?.BlockParsers.Contains<Extensions.Tables.PipeTableBlockParser>() is not true)
        {
            return false;
        }

        var text = renderer.RenderLeafInline(paragraph).AsSpan();
        var end = text.IndexOfAny('\r', '\n');
        var line = end < 0 ? text : text[..end];
        return line.Contains('|') && !line.ContainsAnyExcept("-|: ");
    }

    // Whether the list is the first block of an item of a list written with the specified bullet
    private static bool IsFirstOnLineOfListWithBullet(Dictionary<ListBlock, char> characters, ListBlock list, char bullet)
    {
        return list.Parent is ListItemBlock { Count: > 0, Parent: ListBlock parent } item && item[0] == list && characters.TryGetValue(parent, out var parentBullet) && parentBullet == bullet;
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
    // so that they are not merged. The characters of a run of adjacent lists are computed forward, without recursion, and
    // kept, so that each list is computed once.
    private static char GetBulletCharacter(NormalizeRenderer renderer, ListBlock listBlock)
    {
        var characters = renderer.BulletCharacters;
        if (characters.TryGetValue(listBlock, out var result))
        {
            return result;
        }

        var first = listBlock;
        while (renderer.GetPreviousSibling(first) is ListBlock previous && previous.IsOrdered == first.IsOrdered && !characters.ContainsKey(previous))
        {
            first = previous;
        }

        for (var list = first; ; list = (ListBlock)renderer.GetNextSibling(list)!)
        {
            result = list.IsOrdered ? list.OrderedDelimiter : renderer.Options.ListItemCharacter ?? list.BulletType;
            if (renderer.GetPreviousSibling(list) is ListBlock previous && previous.IsOrdered == list.IsOrdered && characters[previous] == result)
            {
                result = result switch
                {
                    '.' => ')',
                    ')' => '.',
                    '-' => '*',
                    _ => '-',
                };
            }

            // Three lists starting on the same line with the same bullet, such as "* * *", would be a thematic break
            if (result is '*' or '-' && IsFirstOnLineOfListWithBullet(characters, list, result) && IsFirstOnLineOfListWithBullet(characters, (ListBlock)list.Parent!.Parent!, result))
            {
                result = result == '*' ? '-' : '*';
            }

            // The block parser of the pipe tables takes a line of dashes, pipes, colons and spaces after a paragraph for a
            // delimiter row, such as an item "- |"
            if (result == '-' && renderer.GetPreviousSibling(list) is ParagraphBlock && IsDelimiterRowItem(renderer, list))
            {
                result = '*';
            }

            characters[list] = result;
            if (list == listBlock)
            {
                return result;
            }
        }
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
}
