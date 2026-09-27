// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

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
            if (fencedCodeBlock.Info != null)
            {
                renderer.Write(fencedCodeBlock.Info);
            }
            if (!string.IsNullOrEmpty(fencedCodeBlock.Arguments))
            {
                renderer.Write(' ').Write(fencedCodeBlock.Arguments);
            }

            /* TODO do we need this causes a empty space and would render html attributes to markdown.
            var attributes = obj.TryGetAttributes();
            if (attributes != null)
            {
                renderer.Write(' ');
                renderer.Write(attributes);
            }
            */
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
}
