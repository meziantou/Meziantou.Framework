// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Renderers.Html.Inlines;

/// <summary>
/// A HTML renderer for a <see cref="HtmlInline"/>.
/// </summary>
/// <seealso cref="HtmlObjectRenderer{HtmlInline}" />
public class HtmlInlineRenderer : HtmlObjectRenderer<HtmlInline>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(HtmlRenderer renderer, HtmlInline obj)
    {
        if (renderer.EnableHtmlForInline)
        {
            renderer.Write(obj.Tag);
        }
        else if (renderer.IsWritingImageAlt)
        {
            // The alt text of an image is the plain text of its description: raw HTML is kept as text, as in commonmark.js and cmark
            renderer.WriteEscape(obj.Tag);
        }
    }
}
