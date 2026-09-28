// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;

namespace Meziantou.Framework.Markdown.Extensions.Figures;

/// <summary>
/// A HTML renderer for a <see cref="Figure"/>.
/// </summary>
/// <seealso cref="HtmlObjectRenderer{Figure}" />
public class HtmlFigureRenderer : HtmlObjectRenderer<Figure>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(HtmlRenderer renderer, Figure obj)
    {
        renderer.EnsureLine();
        if (renderer.EnableHtmlForBlock)
        {
            renderer.Write("<figure").WriteAttributes(obj).WriteLine(">");
        }

        renderer.WriteChildren(obj);
        if (renderer.EnableHtmlForBlock)
        {
            renderer.WriteLine("</figure>");
        }

        renderer.EnsureLine();
    }
}
