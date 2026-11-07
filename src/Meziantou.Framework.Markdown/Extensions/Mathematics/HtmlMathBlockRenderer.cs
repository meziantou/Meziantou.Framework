// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;

namespace Meziantou.Framework.Markdown.Extensions.Mathematics;

/// <summary>
/// A HTML renderer for a <see cref="MathBlock"/>.
/// </summary>
/// <seealso cref="HtmlObjectRenderer{T}" />
public class HtmlMathBlockRenderer : HtmlObjectRenderer<MathBlock>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(HtmlRenderer renderer, MathBlock obj)
    {
        renderer.EnsureLine();
        if (renderer.EnableHtmlForBlock)
        {
            renderer.Write("<div").WriteAttributes(obj).WriteLine(">");
            renderer.WriteLine("\\[");
        }

        renderer.WriteLeafRawLines(obj, true, renderer.EnableHtmlEscape);

        if (renderer.EnableHtmlForBlock)
        {
            renderer.Write("\\]");
            renderer.WriteLine("</div>");
        }
    }
}
