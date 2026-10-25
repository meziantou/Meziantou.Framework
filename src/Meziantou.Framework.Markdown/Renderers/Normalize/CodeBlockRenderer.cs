// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Globalization;
using System.Text;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers.Normalize.Inlines;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Normalize;

/// <summary>
/// An Normalize renderer for a <see cref="CodeBlock"/> and <see cref="FencedCodeBlock"/>.
/// </summary>
/// <seealso cref="NormalizeObjectRenderer{CodeBlock}" />
public class CodeBlockRenderer : NormalizeObjectRenderer<CodeBlock>
{
    /// <summary>
    /// Gets or sets the output attributes on pre.
    /// </summary>
    public bool OutputAttributesOnPre { get; set; }

    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(NormalizeRenderer renderer, CodeBlock obj)
    {
        if (obj is FencedCodeBlock fencedCodeBlock)
        {
            int fencedCharCount = GetFencedCharCount(fencedCodeBlock);

            renderer.Write(fencedCodeBlock.FencedChar, fencedCharCount);
            if (fencedCodeBlock.Info is { Length: > 0 } info)
            {
                // The info string must not lengthen the fence
                if (info[0] == fencedCodeBlock.FencedChar)
                {
                    renderer.Write(' ');
                }

                renderer.Write(EscapeInfo(info, fencedCodeBlock.FencedChar));
            }
            if (!string.IsNullOrEmpty(fencedCodeBlock.Arguments))
            {
                // The whitespace around the arguments would be trimmed, as well as the Unicode whitespace in them, and a line ending
                // would end the line, so they are written as entities, which the parser decodes
                var arguments = fencedCodeBlock.Arguments;
                var start = arguments.Length - arguments.AsSpan().TrimStart().Length;
                var end = arguments.AsSpan().TrimEnd().Length;
                renderer.Write(' ');
                for (var i = 0; i < arguments.Length; i++)
                {
                    var c = arguments[i];
                    if (NeedsEntity(arguments, i, start, end))
                    {
                        renderer.Write("&#" + ((int)c).ToString(CultureInfo.InvariantCulture) + ";");
                    }
                    else if (c == '\\' && i + 1 < arguments.Length && (arguments[i + 1].IsAsciiPunctuation() || NeedsEntity(arguments, i + 1, start, end)))
                    {
                        // The backslash is text, so it must not escape the next character, or the ampersand of its entity
                        renderer.Write("\\\\");
                    }
                    else if (c == '&' && LinkInlineRenderer.IsEntityStart(arguments.AsSpan(i + 1)))
                    {
                        renderer.Write("&amp;");
                    }
                    else
                    {
                        renderer.Write(c);
                    }
                }

                static bool NeedsEntity(string arguments, int index, int start, int end)
                {
                    var c = arguments[index];
                    return index < start || index >= end || (c > (char)0x7F && char.IsWhiteSpace(c)) || char.IsControl(c);
                }
            }

            // The HTML attributes of the block are not written, as they cannot be rendered as Markdown
            renderer.WriteLine();

            // Always close the fence: a block closed by the end of its container, or unclosed, would otherwise swallow what follows
            renderer.WriteLeafRawLines(obj, true);
            renderer.Write(fencedCodeBlock.FencedChar, fencedCharCount);
        }
        else if (renderer.FencedCodeBlocks.Contains(obj))
        {
            // A fence longer than the backticks of the content, which is rendered as the indented code block
            var fencedCharCount = 3;
            for (var i = 0; i < obj.Lines.Count; i++)
            {
                var run = 0;
                foreach (var c in obj.Lines.Lines[i].Slice.AsSpan())
                {
                    run = c == '`' ? run + 1 : 0;
                    fencedCharCount = Math.Max(fencedCharCount, run + 1);
                }
            }

            renderer.Write('`', fencedCharCount);
            renderer.WriteLine();
            renderer.WriteLeafRawLines(obj, true);
            renderer.Write('`', fencedCharCount);
        }
        else
        {
            renderer.WriteLeafRawLines(obj, false, true);
        }

        renderer.FinishBlock(renderer.Options.EmptyLineAfterCodeBlock || NormalizeRenderer.IsInLooseListItem(obj));
    }

    private static int GetFencedCharCount(FencedCodeBlock fencedCodeBlock)
    {
        var fencedChar = fencedCodeBlock.FencedChar;
        if (fencedChar is not ('`' or '~'))
        {
            // Other fences (e.g. math blocks) accept a fixed number of characters
            return Math.Max(fencedCodeBlock.OpeningFencedCharCount, fencedCodeBlock.ClosingFencedCharCount);
        }

        // The fence must be longer than any content line that would close it
        var count = Math.Max(3, fencedCodeBlock.OpeningFencedCharCount);
        var lines = fencedCodeBlock.Lines;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines.Lines[i].Slice.AsSpan().TrimStart(" \t");
            var run = line.Length - line.TrimStart(fencedChar).Length;
            if (run >= count && line[run..].IsWhiteSpace())
            {
                count = run + 1;
            }
        }

        return count;
    }

    // The info string is unescaped and its entities are decoded, and it ends at the first whitespace
    private static string EscapeInfo(string info, char fencedChar)
    {
        StringBuilder? builder = null;
        for (var i = 0; i < info.Length; i++)
        {
            var c = info[i];
            string? replacement = null;
            if (char.IsWhiteSpace(c) || (c == '`' && fencedChar == '`'))
            {
                replacement = "&#" + ((int)c).ToString(CultureInfo.InvariantCulture) + ";";
            }
            else if (c == '&' && LinkInlineRenderer.IsEntityStart(info.AsSpan(i + 1)))
            {
                replacement = "&amp;";
            }
            else if (c == '\\' && (i + 1 == info.Length || info[i + 1].IsAsciiPunctuation() || char.IsWhiteSpace(info[i + 1])))
            {
                replacement = "\\\\";
            }

            if (replacement is not null)
            {
                builder ??= new StringBuilder(info, 0, i, info.Length + 8);
                builder.Append(replacement);
            }
            else
            {
                builder?.Append(c);
            }
        }

        return builder?.ToString() ?? info;
    }
}
