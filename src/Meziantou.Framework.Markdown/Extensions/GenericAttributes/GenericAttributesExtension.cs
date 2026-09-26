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
    // Attributes that only describe the content. A deny-list is not enough: besides event handlers and URL attributes,
    // client-side frameworks run the value of their own attributes (x-init, hx-get, v-html, data-bind...), and style
    // can cover the page with an invisible link.
    private static readonly HashSet<string> SafeAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "align",
        "dir",
        "height",
        "lang",
        "role",
        "title",
        "width",
    };

    /// <summary>
    /// Gets or sets the predicate that decides whether an attribute parsed from the Markdown (other than the id and the
    /// classes) is written to the HTML. The default value is <see cref="IsSafeAttributeName"/>.
    /// </summary>
    public Func<string, bool> AttributeFilter { get; set; } = IsSafeAttributeName;

    /// <summary>
    /// Returns <see langword="true"/> for the attributes that only describe the content: <c>align</c>, <c>dir</c>,
    /// <c>height</c>, <c>lang</c>, <c>role</c>, <c>title</c>, <c>width</c> and <c>aria-*</c>.
    /// Other attributes are rejected, including event handlers, attributes that hold a URL, <c>style</c>, and the
    /// <c>data-*</c> and directive attributes that client-side frameworks execute. Use <see cref="AttributeFilter"/> to
    /// allow more attributes when the Markdown is trusted.
    /// </summary>
    /// <param name="name">The name of the attribute.</param>
    /// <returns><see langword="true"/> if the attribute can be written to the HTML output.</returns>
    public static bool IsSafeAttributeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return SafeAttributeNames.Contains(name) || name.StartsWith("aria-", StringComparison.OrdinalIgnoreCase);
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
