// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Renderers;

namespace Meziantou.Framework.Markdown.Extensions.Hardlines;

/// <summary>
/// Extension to generate hardline break for softline breaks.
/// </summary>
/// <seealso cref="IMarkdownExtension" />
public class SoftlineBreakAsHardlineExtension : IMarkdownExtension
{
    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        // Simply modify the LineBreakInlineParser
        var parser = pipeline.InlineParsers.Find<LineBreakInlineParser>();
        if (parser != null)
        {
            parser.EnableSoftAsHard = true;
        }
    }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }
}
