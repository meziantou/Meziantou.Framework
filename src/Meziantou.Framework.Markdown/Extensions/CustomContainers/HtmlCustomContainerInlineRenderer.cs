// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;

namespace Meziantou.Framework.Markdown.Extensions.CustomContainers;

/// <summary>
/// A HTML renderer for a <see cref="CustomContainerInline"/>.
/// </summary>
/// <seealso cref="HtmlObjectRenderer{CustomContainerInline}" />
public class HtmlCustomContainerInlineRenderer : HtmlObjectRenderer<CustomContainerInline>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(HtmlRenderer renderer, CustomContainerInline obj)
    {
        renderer.Write("<span").WriteAttributes(obj).Write('>');
        renderer.WriteChildren(obj);
        renderer.Write("</span>");
    }
}
