// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Renderers.Html.Inlines;

/// <summary>
/// A HTML renderer for a <see cref="DelimiterInline"/>.
/// </summary>
/// <seealso cref="HtmlObjectRenderer{DelimiterInline}" />
public class DelimiterInlineRenderer : HtmlObjectRenderer<DelimiterInline>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(HtmlRenderer renderer, DelimiterInline obj)
    {
        renderer.WriteEscape(obj.ToLiteral());
        renderer.WriteChildren(obj);
    }
}
