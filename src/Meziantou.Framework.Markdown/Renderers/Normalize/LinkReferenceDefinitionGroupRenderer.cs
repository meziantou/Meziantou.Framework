// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Normalize;

/// <summary>
/// Represents the LinkReferenceDefinitionGroupRenderer type.
/// </summary>
public class LinkReferenceDefinitionGroupRenderer : NormalizeObjectRenderer<LinkReferenceDefinitionGroup>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(NormalizeRenderer renderer, LinkReferenceDefinitionGroup obj)
    {
        renderer.EnsureLine();
        renderer.WriteChildren(obj);

        // The definitions are parsed from a paragraph, which the next lines would continue, unless the next paragraph can only
        // be a continuation, as in the source. The line can already be ended when the last definitions are not written.
        if (!renderer.IsLastInContainer)
        {
            renderer.EnsureLine();
            if (renderer.GetNextSibling(obj) is not ParagraphBlock next || !renderer.IsParagraphContinuationOnly(next))
            {
                renderer.WriteLine();
            }
        }
    }
}
