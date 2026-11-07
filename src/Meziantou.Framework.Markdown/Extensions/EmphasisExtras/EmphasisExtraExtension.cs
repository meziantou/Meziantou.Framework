// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics;
using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html.Inlines;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Extensions.EmphasisExtras;

/// <summary>
/// Extension for strikethrough, subscript, superscript, inserted and marked.
/// </summary>
/// <seealso cref="IMarkdownExtension" />
public class EmphasisExtraExtension : IMarkdownExtension
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmphasisExtraExtension"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public EmphasisExtraExtension(EmphasisExtraOptions options = EmphasisExtraOptions.Default)
    {
        Options = options;
    }

    /// <summary>
    /// Gets the options.
    /// </summary>
    public EmphasisExtraOptions Options { get; }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        var parser = pipeline.InlineParsers.FindExact<EmphasisInlineParser>();
        if (parser != null)
        {
            var hasTilde = false;
            var hasSup = false;
            var hasPlus = false;
            var hasEqual = false;

            var requireTilde = (Options.HasFlag(EmphasisExtraOptions.Strikethrough) ||
                                Options.HasFlag(EmphasisExtraOptions.Subscript));

            var requireSup = Options.HasFlag(EmphasisExtraOptions.Superscript);
            var requirePlus = Options.HasFlag(EmphasisExtraOptions.Inserted);
            var requireEqual = Options.HasFlag(EmphasisExtraOptions.Marked);

            foreach (var emphasis in parser.EmphasisDescriptors)
            {
                if (requireTilde && emphasis.Character == '~')
                {
                    hasTilde = true;
                }
                if (requireSup && emphasis.Character == '^')
                {
                    hasSup = true;
                }
                if (requirePlus && emphasis.Character == '+')
                {
                    hasPlus = true;
                }
                if (requireEqual && emphasis.Character == '=')
                {
                    hasEqual = true;
                }
            }

            if (requireTilde && !hasTilde)
            {
                int minimumCount = Options.HasFlag(EmphasisExtraOptions.Subscript) ? 1 : 2;
                int maximumCount = Options.HasFlag(EmphasisExtraOptions.Strikethrough) ? 2 : 1;
                parser.EmphasisDescriptors.Add(new EmphasisDescriptor('~', minimumCount, maximumCount, true));
            }
            if (requireSup && !hasSup)
            {
                parser.EmphasisDescriptors.Add(new EmphasisDescriptor('^', 1, 1, true));
            }
            if (requirePlus && !hasPlus)
            {
                parser.EmphasisDescriptors.Add(new EmphasisDescriptor('+', 2, 2, true));
            }
            if (requireEqual && !hasEqual)
            {
                parser.EmphasisDescriptors.Add(new EmphasisDescriptor('=', 2, 2, true));
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
            // Extend the rendering here.
            var emphasisRenderer = htmlRenderer.ObjectRenderers.FindExact<EmphasisInlineRenderer>();
            if (emphasisRenderer != null)
            {
                var previousTag = emphasisRenderer.GetTag;
                emphasisRenderer.GetTag = inline => GetTag(inline) ?? previousTag(inline);
            }
        }
    }

    private static string? GetTag(EmphasisInline emphasisInline)
    {
        var c = emphasisInline.DelimiterChar;
        switch (c)
        {
            case '~':
                Debug.Assert(emphasisInline.DelimiterCount <= 2);
                return emphasisInline.DelimiterCount == 2 ? "del" : "sub";
            case '^':
                return "sup";
            case '+':
                return "ins";
            case '=':
                return "mark";
        }

        return null;
    }
}
