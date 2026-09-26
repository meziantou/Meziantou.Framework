// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.Diagnostics;
using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html.Inlines;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Extensions.Citations;

/// <summary>
/// Extension for cite ""...""
/// </summary>
/// <seealso cref="IMarkdownExtension" />
public class CitationExtension : IMarkdownExtension
{
    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        var parser = pipeline.InlineParsers.FindExact<EmphasisInlineParser>();
        if (parser != null && !parser.HasEmphasisChar('"'))
        {
            parser.EmphasisDescriptors.Add(new EmphasisDescriptor('"', 2, 2, false));
        }
    }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer htmlRenderer)
        {
            // Extend the rendering here.
            var emphasisRenderer = renderer.ObjectRenderers.FindExact<EmphasisInlineRenderer>();
            if (emphasisRenderer != null)
            {
                // TODO: Use an ordered list instead as we don't know if this specific GetTag has been already added
                var previousTag = emphasisRenderer.GetTag;
                emphasisRenderer.GetTag = inline => GetTag(inline) ?? previousTag(inline);
            }
        }
    }

    private static string? GetTag(EmphasisInline emphasisInline)
    {
        Debug.Assert(emphasisInline.DelimiterCount <= 2);
        return emphasisInline.DelimiterCount == 2 && emphasisInline.DelimiterChar == '"' ? "cite" : null;
    }
}
