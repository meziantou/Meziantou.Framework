// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Renderers.Roundtrip.Inlines;

/// <summary>
/// A Normalize renderer for a <see cref="LinkInline"/>.
/// </summary>
/// <seealso cref="RoundtripObjectRenderer{LinkInline}" />
public class LinkInlineRenderer : RoundtripObjectRenderer<LinkInline>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(RoundtripRenderer renderer, LinkInline link)
    {
        if (link.IsImage)
        {
            renderer.Write('!');
        }
        // link text. The label, the title and the whitespace around the url can span lines, which start with the indent of the
        // containers.
        renderer.Write('[');
        renderer.WriteChildren(link);
        renderer.Write(']');

        if (link.Label != null)
        {
            if (link.LocalLabel == LocalLabel.Local || link.LocalLabel == LocalLabel.Empty)
            {
                renderer.Write('[');
                if (link.LocalLabel == LocalLabel.Local)
                {
                    renderer.WriteLines(link.LabelWithTrivia);
                }
                renderer.Write(']');
            }
        }
        else
        {
            if (link.Url != null)
            {
                renderer.Write('(');
                renderer.WriteLines(link.TriviaBeforeUrl);
                if (link.UrlHasPointyBrackets)
                {
                    renderer.Write('<');
                }
                if (link.UnescapedUrl.IsEmpty)
                {
                    renderer.Write(link.Url);
                }
                else
                {
                    renderer.Write(link.UnescapedUrl);
                }
                if (link.UrlHasPointyBrackets)
                {
                    renderer.Write('>');
                }
                renderer.WriteLines(link.TriviaAfterUrl);

                if (!string.IsNullOrEmpty(link.Title))
                {
                    var open = link.TitleEnclosingCharacter;
                    var close = link.TitleEnclosingCharacter;
                    if (link.TitleEnclosingCharacter == '(')
                    {
                        close = ')';
                    }
                    renderer.Write(open);
                    renderer.WriteLines(link.UnescapedTitle);
                    renderer.Write(close);
                    renderer.WriteLines(link.TriviaAfterTitle);
                }

                renderer.Write(')');
            }
        }
    }
}
