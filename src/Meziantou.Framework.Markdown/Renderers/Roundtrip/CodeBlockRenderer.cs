// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Roundtrip;

/// <summary>
/// An Roundtrip renderer for a <see cref="CodeBlock"/> and <see cref="FencedCodeBlock"/>.
/// </summary>
/// <seealso cref="RoundtripObjectRenderer{CodeBlock}" />
public class CodeBlockRenderer : RoundtripObjectRenderer<CodeBlock>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(RoundtripRenderer renderer, CodeBlock obj)
    {
        renderer.RenderLinesBefore(obj);
        if (obj is FencedCodeBlock fencedCodeBlock)
        {
            renderer.Write(obj.TriviaBefore);
            renderer.Write(fencedCodeBlock.FencedChar, fencedCodeBlock.OpeningFencedCharCount);

            if (!fencedCodeBlock.TriviaAfterFencedChar.IsEmpty)
            {
                renderer.Write(fencedCodeBlock.TriviaAfterFencedChar);
            }
            if (fencedCodeBlock.Info != null)
            {
                renderer.Write(fencedCodeBlock.UnescapedInfo);
            }
            if (!fencedCodeBlock.TriviaAfterInfo.IsEmpty)
            {
                renderer.Write(fencedCodeBlock.TriviaAfterInfo);
            }
            if (!string.IsNullOrEmpty(fencedCodeBlock.Arguments))
            {
                renderer.Write(fencedCodeBlock.UnescapedArguments);
            }
            if (!fencedCodeBlock.TriviaAfterArguments.IsEmpty)
            {
                renderer.Write(fencedCodeBlock.TriviaAfterArguments);
            }

            // The HTML attributes of the block are not written, as they cannot be rendered as Markdown
            renderer.WriteLine(fencedCodeBlock.InfoNewLine);

            renderer.WriteLeafRawLines(obj);

            renderer.Write(fencedCodeBlock.TriviaBeforeClosingFence);
            renderer.Write(fencedCodeBlock.FencedChar, fencedCodeBlock.ClosingFencedCharCount);

            // The spaces after the closing fence are on the same line
            renderer.Write(obj.TriviaAfter);
            if (fencedCodeBlock.ClosingFencedCharCount > 0)
            {
                // See example 207: "> ```\nfoo\n```"
                renderer.WriteLine(obj.NewLine);
            }
        }
        else
        {
            var indents = new string[obj.CodeBlockLines.Count];
            for (int i = 0; i < obj.CodeBlockLines.Count; i++)
            {
                indents[i] = obj.CodeBlockLines[i].TriviaBefore.ToString();
            }
            renderer.PushIndent(indents);
            WriteLeafRawLines(renderer, obj);
            renderer.PopIndent();

            // ignore block newline, as last line references it
        }

        renderer.RenderLinesAfter(obj);
    }

    /// <summary>
    /// Performs the write leaf raw lines operation.
    /// </summary>
    public static void WriteLeafRawLines(RoundtripRenderer renderer, LeafBlock leafBlock)
    {
        if (leafBlock.Lines.Lines != null)
        {
            var lines = leafBlock.Lines;
            var slices = lines.Lines;
            for (int i = 0; i < lines.Count; i++)
            {
                ref StringSlice slice = ref slices[i].Slice;
                renderer.Write(ref slice);
                renderer.WriteLine(slice.NewLine);
            }
        }
    }
}
