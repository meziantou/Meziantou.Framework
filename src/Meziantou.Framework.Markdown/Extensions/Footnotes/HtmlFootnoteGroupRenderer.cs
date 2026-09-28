// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;

namespace Meziantou.Framework.Markdown.Extensions.Footnotes;

/// <summary>
/// A HTML renderer for a <see cref="FootnoteGroup"/>.
/// </summary>
/// <seealso cref="HtmlObjectRenderer{FootnoteGroup}" />
public class HtmlFootnoteGroupRenderer : HtmlObjectRenderer<FootnoteGroup>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HtmlFootnoteGroupRenderer"/> class.
    /// </summary>
    public HtmlFootnoteGroupRenderer()
    {
        GroupClass = "footnotes";
    }

    /// <summary>
    /// Gets or sets the CSS group class used when rendering the &lt;div&gt; of this instance.
    /// </summary>
    public string GroupClass { get; set; }

    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(HtmlRenderer renderer, FootnoteGroup footnotes)
    {
        renderer.EnsureLine();
        if (!renderer.EnableHtmlForBlock)
        {
            // Without HTML for blocks (e.g. plain text), only the content of the footnotes is written
            for (int i = 0; i < footnotes.Count; i++)
            {
                renderer.WriteChildren((Footnote)footnotes[i]);
                renderer.EnsureLine();
            }

            return;
        }

        renderer.WriteLine($"<div class=\"{GroupClass}\">");
        renderer.WriteLine("<hr />");
        renderer.WriteLine("<ol>");

        for (int i = 0; i < footnotes.Count; i++)
        {
            var footnote = (Footnote)footnotes[i];
            renderer.WriteLine($"<li id=\"fn:{footnote.Order}\">");
            renderer.WriteChildren(footnote);
            renderer.WriteLine("</li>");
        }
        renderer.WriteLine("</ol>");
        renderer.WriteLine("</div>");
    }
}
