// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Renderers.Normalize.Inlines;

/// <summary>
/// A Normalize renderer for a <see cref="LineBreakInline"/>.
/// </summary>
/// <seealso cref="NormalizeObjectRenderer{LineBreakInline}" />
public class LineBreakInlineRenderer : NormalizeObjectRenderer<LineBreakInline>
{
    /// <summary>
    /// Gets or sets a value indicating whether to render soft line breaks as hard line breaks (a backslash at the end of the line)
    /// </summary>
    public bool RenderAsHardlineBreak { get; set; }

    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(NormalizeRenderer renderer, LineBreakInline obj)
    {
        // A block that interrupts a paragraph, such as a table, can leave the line break before it at the end of the
        // paragraph, which is not rendered: written, it would add a blank line, or a backslash
        if (obj.NextSibling is null && obj.Parent is { Parent: null })
        {
            return;
        }

        if (obj.IsHard)
        {
            renderer.Write(obj.IsBackslash ? "\\" : "  ");
        }
        else if (RenderAsHardlineBreak)
        {
            renderer.Write('\\');
        }
        renderer.WriteLine();
    }
}
