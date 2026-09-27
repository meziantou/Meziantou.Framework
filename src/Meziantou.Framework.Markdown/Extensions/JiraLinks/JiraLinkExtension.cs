// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Normalize;
using Meziantou.Framework.Markdown.Renderers.Normalize.Inlines;

namespace Meziantou.Framework.Markdown.Extensions.JiraLinks;

/// <summary>
/// Simple inline parser extension to find, and
/// automatically add links to JIRA issue numbers.
/// </summary>
public class JiraLinkExtension : IMarkdownExtension
{
    private readonly JiraLinkOptions _options;

    /// <summary>
    /// Initializes a new instance of the JiraLinkExtension class.
    /// </summary>
    public JiraLinkExtension(JiraLinkOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.InlineParsers.Contains<JiraLinkInlineParser>())
        {
            // Insert the parser before the link inline parser
            pipeline.InlineParsers.InsertBefore<LinkInlineParser>(new JiraLinkInlineParser(_options));
        }
    }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        // No HTML renderer required, since JiraLink type derives from InlineLink (which already has an HTML renderer)

        if (renderer is NormalizeRenderer normalizeRenderer && !normalizeRenderer.ObjectRenderers.Contains<NormalizeJiraLinksRenderer>())
        {
            normalizeRenderer.ObjectRenderers.InsertBefore<LinkInlineRenderer>(new NormalizeJiraLinksRenderer());
        }
    }
}

