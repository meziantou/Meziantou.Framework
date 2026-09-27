// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Renderers.Normalize.Inlines;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Normalize;

/// <summary>
/// Represents the LinkReferenceDefinitionRenderer type.
/// </summary>
public class LinkReferenceDefinitionRenderer : NormalizeObjectRenderer<LinkReferenceDefinition>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(NormalizeRenderer renderer, LinkReferenceDefinition linkDef)
    {
        renderer.EnsureLine();
        renderer.Write('[');
        renderer.Write(linkDef.Label);
        renderer.Write("]: ");

        // The entities of a definition are kept, and decoded when a link uses it
        LinkInlineRenderer.WriteDestination(renderer, linkDef.Url, decodedEntities: false);

        if (linkDef.Title != null)
        {
            renderer.Write(' ');
            LinkInlineRenderer.WriteTitle(renderer, linkDef.Title, decodedEntities: false);
        }
        renderer.FinishBlock(false);
    }
}
