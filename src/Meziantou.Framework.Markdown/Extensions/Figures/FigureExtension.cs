// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Extensions.Footers;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.Figures;

/// <summary>
/// Extension to allow usage of figures and figure captions.
/// </summary>
/// <seealso cref="IMarkdownExtension" />
public class FigureExtension : IMarkdownExtension
{
    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.BlockParsers.Contains<FigureBlockParser>())
        {
            // The Figure extension must come before the Footer extension
            if (pipeline.BlockParsers.Contains<FooterBlockParser>())
            {
                pipeline.BlockParsers.InsertBefore<FooterBlockParser>(new FigureBlockParser());
            }
            else
            {
                pipeline.BlockParsers.Insert(0, new FigureBlockParser());
            }
        }
    }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer htmlRenderer)
        {
            htmlRenderer.ObjectRenderers.AddIfNotAlready<HtmlFigureRenderer>();
            htmlRenderer.ObjectRenderers.AddIfNotAlready<HtmlFigureCaptionRenderer>();
        }
        else if (renderer is RoundtripRenderer roundtripRenderer)
        {
            roundtripRenderer.ObjectRenderers.AddIfNotAlready<RoundtripFigureRenderer>();
            roundtripRenderer.ObjectRenderers.AddIfNotAlready<RoundtripFigureCaptionRenderer>();
        }
    }
}
