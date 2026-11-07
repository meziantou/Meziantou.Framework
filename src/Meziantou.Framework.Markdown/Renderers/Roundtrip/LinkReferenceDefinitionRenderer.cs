// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Roundtrip;

/// <summary>
/// Represents the LinkReferenceDefinitionRenderer type.
/// </summary>
public class LinkReferenceDefinitionRenderer : RoundtripObjectRenderer<LinkReferenceDefinition>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(RoundtripRenderer renderer, LinkReferenceDefinition linkDef)
    {
        renderer.RenderLinesBefore(linkDef);

        renderer.Write(linkDef.TriviaBefore);
        renderer.Write('[');
        // The label, the title and the whitespace around the url can span lines, which start with the indent of the containers
        renderer.WriteLines(linkDef.LabelWithTrivia);
        renderer.Write("]:");

        renderer.WriteLines(linkDef.TriviaBeforeUrl);
        if (linkDef.UrlHasPointyBrackets)
        {
            renderer.Write('<');
        }
        renderer.Write(linkDef.UnescapedUrl);
        if (linkDef.UrlHasPointyBrackets)
        {
            renderer.Write('>');
        }

        renderer.WriteLines(linkDef.TriviaBeforeTitle);
        if (linkDef.Title != null)
        {
            var open = linkDef.TitleEnclosingCharacter;
            var close = linkDef.TitleEnclosingCharacter;
            if (linkDef.TitleEnclosingCharacter == '(')
            {
                close = ')';
            }
            renderer.Write(open);
            renderer.WriteLines(linkDef.UnescapedTitle);
            renderer.Write(close);
        }
        renderer.Write(linkDef.TriviaAfter);
        renderer.Write(linkDef.NewLine.AsString());

        renderer.RenderLinesAfter(linkDef);
    }
}
