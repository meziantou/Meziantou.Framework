// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.Footnotes;

/// <summary>
/// Extension to allow footnotes.
/// </summary>
/// <seealso cref="IMarkdownExtension" />
public class FootnoteExtension : IMarkdownExtension
{
    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.BlockParsers.Contains<FootnoteParser>())
        {
            // Insert the parser before any other parsers
            pipeline.BlockParsers.Insert(0, new FootnoteParser());
        }
    }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer htmlRenderer)
        {
            htmlRenderer.ObjectRenderers.AddIfNotAlready(new HtmlFootnoteGroupRenderer());
            htmlRenderer.ObjectRenderers.AddIfNotAlready(new HtmlFootnoteLinkRenderer());
        }
        else if (renderer is RoundtripRenderer roundtripRenderer)
        {
            roundtripRenderer.ObjectRenderers.AddIfNotAlready(new RoundtripFootnoteDocumentRenderer());
            roundtripRenderer.ObjectRenderers.AddIfNotAlready(new RoundtripFootnoteGroupRenderer());
            roundtripRenderer.ObjectRenderers.AddIfNotAlready(new RoundtripFootnoteRenderer());
            roundtripRenderer.ObjectRenderers.AddIfNotAlready(new RoundtripFootnoteLinkRenderer());
        }
    }
}
