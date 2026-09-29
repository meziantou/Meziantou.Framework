// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;

namespace Meziantou.Framework.Markdown.Extensions.Alerts;

/// <summary>
/// Extension for adding alerts to a Markdown pipeline.
/// </summary>
public class AlertExtension : IMarkdownExtension
{
    /// <summary>
    /// Gets or sets the delegate to render the kind of the alert.
    /// </summary>
    public Action<HtmlRenderer, StringSlice>? RenderKind { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether alerts can be nested inside other blocks (e.g. inside a blockquote or a list item).
    /// Alerts are never allowed inside another alert block regardless of this setting.
    /// Default is <c>false</c>.
    /// </summary>
    public bool AllowNestedAlerts { get; set; }

    /// <inheritdoc />
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        var inlineParser = pipeline.InlineParsers.Find<AlertInlineParser>();
        if (inlineParser == null)
        {
            pipeline.InlineParsers.InsertBefore<LinkInlineParser>(new AlertInlineParser() { AllowNestedAlerts = AllowNestedAlerts });
        }
        else
        {
            inlineParser.AllowNestedAlerts = AllowNestedAlerts;
        }
    }

    /// <inheritdoc />
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        var blockRenderer = renderer.ObjectRenderers.FindExact<AlertBlockRenderer>();
        if (blockRenderer == null)
        {
            renderer.ObjectRenderers.InsertBefore<QuoteBlockRenderer>(new AlertBlockRenderer()
            {
                RenderKind = RenderKind ?? AlertBlockRenderer.DefaultRenderKind
            });
        }

        // AlertBlock is a QuoteBlock, so its renderer must come first
        if (renderer is Renderers.Roundtrip.RoundtripRenderer roundtripRenderer && !roundtripRenderer.ObjectRenderers.Contains<RoundtripAlertBlockRenderer>())
        {
            roundtripRenderer.ObjectRenderers.InsertBefore<Renderers.Roundtrip.QuoteBlockRenderer>(new RoundtripAlertBlockRenderer());
        }
    }
}