// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Extensions.GenericAttributes;

/// <summary>
/// Extension that allows to attach HTML attributes to the previous <see cref="Inline"/> or current <see cref="Block"/>.
/// This extension should be enabled last after enabling other extensions.
/// </summary>
/// <seealso cref="IMarkdownExtension" />
public class GenericAttributesExtension : IMarkdownExtension
{
    private static readonly HashSet<string> UrlOrDocumentAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "action",
        "background",
        "codebase",
        "data",
        "dynsrc",
        "formaction",
        "href",
        "lowsrc",
        "manifest",
        "ping",
        "poster",
        "src",
        "srcdoc",
        "srcset",
        "xlink:href",
        "xmlns",
    };

    /// <summary>
    /// Gets or sets the predicate that decides whether an attribute parsed from the Markdown (other than the id and the
    /// classes) is written to the HTML. The default value is <see cref="IsSafeAttributeName"/>.
    /// </summary>
    public Func<string, bool> AttributeFilter { get; set; } = IsSafeAttributeName;

    /// <summary>
    /// Returns <see langword="false"/> for the attributes that can run script or load a resource: event handlers
    /// (<c>on*</c>), and attributes that hold a URL or a document such as <c>href</c>, <c>src</c> or <c>srcdoc</c>.
    /// Other attributes, such as <c>style</c>, <c>title</c> or <c>data-*</c>, are allowed.
    /// </summary>
    /// <param name="name">The name of the attribute.</param>
    /// <returns><see langword="true"/> if the attribute can be written to the HTML output.</returns>
    public static bool IsSafeAttributeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            return false;

        if (name.StartsWith("xmlns:", StringComparison.OrdinalIgnoreCase))
            return false;

        return !UrlOrDocumentAttributeNames.Contains(name);
    }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        var inlineParser = pipeline.InlineParsers.Find<GenericAttributesParser>();
        if (inlineParser is null)
        {
            inlineParser = new GenericAttributesParser();
            pipeline.InlineParsers.Insert(0, inlineParser);
        }

        inlineParser.AttributeFilter = AttributeFilter;

        // Plug into all IAttributesParseable
        foreach (var parser in pipeline.BlockParsers)
        {
            if (parser is IAttributesParseable attributesParseable)
            {
                attributesParseable.TryParseAttributes = TryProcessAttributesForHeading;
            }
        }
    }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }

    private bool TryProcessAttributesForHeading(BlockProcessor processor, ref StringSlice line, IBlock block)
    {
        // Try to find if there is any attributes { in the info string on the first line of a FencedCodeBlock
        if (line.Start < line.End)
        {
            int indexOfAttributes = line.IndexOf('{');
            if (indexOfAttributes >= 0)
            {
                // Work on a copy
                var copy = line;
                copy.Start = indexOfAttributes;
                var startOfAttributes = copy.Start;
                if (GenericAttributesParser.TryParse(ref copy, out HtmlAttributes? attributes))
                {
                    GenericAttributesParser.RemoveFilteredProperties(attributes, AttributeFilter);

                    var htmlAttributes = block.GetAttributes();
                    attributes.CopyTo(htmlAttributes);

                    // Update position for HtmlAttributes
                    htmlAttributes.Line = processor.LineIndex;
                    htmlAttributes.Column = startOfAttributes - processor.CurrentLineStartPosition; // This is not accurate with tabs!
                    htmlAttributes.Span.Start = startOfAttributes;
                    htmlAttributes.Span.End = copy.Start - 1;

                    line.End = indexOfAttributes - 1;
                    return true;
                }
            }
        }
        return false;
    }
}
