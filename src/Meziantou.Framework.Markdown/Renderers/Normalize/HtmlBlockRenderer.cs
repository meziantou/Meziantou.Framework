// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Normalize;

/// <summary>
/// Represents the HtmlBlockRenderer type.
/// </summary>
public class HtmlBlockRenderer : NormalizeObjectRenderer<HtmlBlock>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(NormalizeRenderer renderer, HtmlBlock obj)
    {
        renderer.WriteLeafRawLines(obj, true, false);

        // These HTML blocks end at a blank line, which must not be dropped
        if (obj.Type is HtmlBlockType.InterruptingBlock or HtmlBlockType.NonInterruptingBlock && !renderer.IsLastInContainer)
        {
            renderer.WriteLine();
        }
    }

    // Whether the last block of the container is an HTML block that has not met its end condition, and would continue
    // on the following lines, even blank ones
    internal static bool EndsWithOpenHtmlBlock(ContainerBlock container)
    {
        var block = container.LastChild;
        while (block is ContainerBlock child)
        {
            block = child.LastChild;
        }

        if (block is not HtmlBlock { Lines.Count: > 0 } htmlBlock)
        {
            return false;
        }

        var lastLine = htmlBlock.Lines.Lines[htmlBlock.Lines.Count - 1].Slice.AsSpan();
        return htmlBlock.Type switch
        {
            HtmlBlockType.Comment => !lastLine.Contains("-->", StringComparison.Ordinal),
            HtmlBlockType.CData => !lastLine.Contains("]]>", StringComparison.Ordinal),
            HtmlBlockType.ProcessingInstruction => !lastLine.Contains("?>", StringComparison.Ordinal),
            HtmlBlockType.DocumentType => !lastLine.Contains('>'),
            HtmlBlockType.ScriptPreOrStyle => !lastLine.Contains("</script>", StringComparison.OrdinalIgnoreCase)
                && !lastLine.Contains("</pre>", StringComparison.OrdinalIgnoreCase)
                && !lastLine.Contains("</style>", StringComparison.OrdinalIgnoreCase)
                && !lastLine.Contains("</textarea>", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }
}
