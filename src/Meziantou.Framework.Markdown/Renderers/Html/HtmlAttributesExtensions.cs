// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Globalization;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Html;

/// <summary>
/// Extensions for a <see cref="MarkdownObject"/> to allow accessing <see cref="HtmlAttributes"/>
/// </summary>
public static class HtmlAttributesExtensions
{
    private static readonly object Key = typeof (HtmlAttributes);

    /// <summary>
    /// Tries the get <see cref="HtmlAttributes"/> stored on a <see cref="MarkdownObject"/>.
    /// </summary>
    /// <param name="obj">The markdown object.</param>
    /// <returns>The attached html attributes or null if not found</returns>
    public static HtmlAttributes? TryGetAttributes(this IMarkdownObject obj)
    {
        return obj.GetData(Key) as HtmlAttributes;
    }

    /// <summary>
    /// Gets or creates the <see cref="HtmlAttributes"/> stored on a <see cref="MarkdownObject"/>
    /// </summary>
    /// <param name="obj">The markdown object.</param>
    /// <returns>The attached html attributes</returns>
    public static HtmlAttributes GetAttributes(this IMarkdownObject obj)
    {
        var attributes = obj.GetData(Key) as HtmlAttributes;
        if (attributes is null)
        {
            attributes = new HtmlAttributes();
            obj.SetAttributes(attributes);
        }
        return attributes;
    }

    /// <summary>
    /// Sets <see cref="HtmlAttributes" /> to the <see cref="MarkdownObject" />
    /// </summary>
    /// <param name="obj">The markdown object.</param>
    /// <param name="attributes">The attributes to attach.</param>
    public static void SetAttributes(this IMarkdownObject obj, HtmlAttributes attributes)
    {
        obj.SetData(Key, attributes);
    }
}
