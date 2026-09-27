// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Normalize;

/// <summary>
/// An Normalize renderer for a <see cref="HeadingBlock"/>.
/// </summary>
/// <seealso cref="NormalizeObjectRenderer{HeadingBlock}" />
public class HeadingRenderer : NormalizeObjectRenderer<HeadingBlock>
{
    private static readonly string[] HeadingTexts = [
        "#",
        "##",
        "###",
        "####",
        "#####",
        "######",
    ];

    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(NormalizeRenderer renderer, HeadingBlock obj)
    {
        var text = renderer.RenderLeafInline(obj);
        if (obj.Level is 1 or 2 && text.Contains('\n', StringComparison.Ordinal))
        {
            // An ATX heading is a single line, so a heading with line breaks is a setext heading
            renderer.WriteParagraphInline(obj, text);
            renderer.WriteLine();
            renderer.Write(obj.Level == 1 ? '=' : '-', 3);
        }
        else
        {
            if (obj.Level is > 0 and <= 6)
            {
                renderer.Write(HeadingTexts[obj.Level - 1]);
            }
            else
            {
                renderer.Write('#', obj.Level);
            }

            renderer.Write(' ');
            renderer.Write(text);

            // A closing sequence keeps the number signs that end the content
            if (text is [.., '#'])
            {
                renderer.Write(" #");
            }
        }

        renderer.FinishBlock(renderer.Options.EmptyLineAfterHeading);
    }
}
