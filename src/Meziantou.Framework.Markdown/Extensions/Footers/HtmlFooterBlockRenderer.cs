// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;

namespace Meziantou.Framework.Markdown.Extensions.Footers;

/// <summary>
/// A HTML renderer for a <see cref="FooterBlock"/>.
/// </summary>
/// <seealso cref="HtmlObjectRenderer{FooterBlock}" />
public class HtmlFooterBlockRenderer : HtmlObjectRenderer<FooterBlock>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(HtmlRenderer renderer, FooterBlock footer)
    {
        renderer.EnsureLine();
        renderer.Write("<footer").WriteAttributes(footer).Write(">");
        var implicitParagraph = renderer.ImplicitParagraph;
        renderer.ImplicitParagraph = true;
        renderer.WriteChildren(footer);
        renderer.ImplicitParagraph = implicitParagraph;
        renderer.WriteLine("</footer>");
    }
}
