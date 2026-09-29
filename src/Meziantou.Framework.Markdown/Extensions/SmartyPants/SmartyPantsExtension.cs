// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.SmartyPants;

/// <summary>
/// Extension to enable SmartyPants.
/// </summary>
public class SmartyPantsExtension : IMarkdownExtension
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SmartyPantsExtension"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public SmartyPantsExtension(SmartyPantOptions? options)
    {
        Options = options ?? new SmartyPantOptions();
    }

    /// <summary>
    /// Gets the options.
    /// </summary>
    public SmartyPantOptions Options { get; }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.InlineParsers.Contains<SmartyPantsInlineParser>())
        {
            // Insert the parser after the code span parser
            pipeline.InlineParsers.InsertAfter<CodeInlineParser>(new SmartyPantsInlineParser());
        }
    }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer htmlRenderer)
        {
            if (!htmlRenderer.ObjectRenderers.Contains<HtmlSmartyPantRenderer>())
            {
                htmlRenderer.ObjectRenderers.Add(new HtmlSmartyPantRenderer(Options));
            }
        }
        else if (renderer is RoundtripRenderer roundtripRenderer)
        {
            roundtripRenderer.ObjectRenderers.AddIfNotAlready(new RoundtripSmartyPantRenderer());
        }
    }
}
