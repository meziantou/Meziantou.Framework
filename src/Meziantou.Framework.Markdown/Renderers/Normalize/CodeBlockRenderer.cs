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
                renderer.Write(' ').Write(fencedCodeBlock.Arguments);
            }

            // The HTML attributes of the block are not written, as they cannot be rendered as Markdown
            renderer.WriteLine();

            // Always close the fence: a block closed by the end of its container, or unclosed, would otherwise swallow what follows
            renderer.WriteLeafRawLines(obj, true);
            renderer.Write(fencedCodeBlock.FencedChar, fencedCharCount);
        }
        else
        {
            renderer.WriteLeafRawLines(obj, false, true);
        }

        renderer.FinishBlock(renderer.Options.EmptyLineAfterCodeBlock);
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
            else if (c == '\\' && (i + 1 == info.Length || info[i + 1].IsAsciiPunctuation()))
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
