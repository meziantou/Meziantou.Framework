// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.Abbreviations;

/// <summary>
/// Extension to allow abbreviations.
/// </summary>
/// <seealso cref="IMarkdownExtension" />
public class AbbreviationExtension : IMarkdownExtension
{
    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        pipeline.BlockParsers.AddIfNotAlready<AbbreviationParser>();
    }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer htmlRenderer && !htmlRenderer.ObjectRenderers.Contains<HtmlAbbreviationRenderer>())
        {
            // Must be inserted before CodeBlockRenderer
            htmlRenderer.ObjectRenderers.Insert(0, new HtmlAbbreviationRenderer());
        }
        else if (renderer is RoundtripRenderer roundtripRenderer)
        {
            roundtripRenderer.ObjectRenderers.AddIfNotAlready(new RoundtripAbbreviationRenderer());
            roundtripRenderer.ObjectRenderers.AddIfNotAlready(new RoundtripAbbreviationInlineRenderer());
        }
    }
}
