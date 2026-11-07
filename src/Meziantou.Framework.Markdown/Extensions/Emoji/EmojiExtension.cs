// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.Emoji;

/// <summary>
/// Extension to allow emoji shortcodes and smileys replacement.
/// </summary>
/// <seealso cref="IMarkdownExtension" />
public class EmojiExtension : IMarkdownExtension
{
    /// <summary>
    /// Initializes a new instance of the EmojiExtension class.
    /// </summary>
    public EmojiExtension(EmojiMapping emojiMapping)
    {
        EmojiMapping = emojiMapping;
    }

    /// <summary>
    /// Gets the emoji mapping.
    /// </summary>
    public EmojiMapping EmojiMapping { get; }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.InlineParsers.Contains<EmojiParser>())
        {
            // Insert before emphasis so emoji shortcodes are parsed early, while preserving
            // precedence for structural parsers registered earlier in the pipeline.
            pipeline.InlineParsers.InsertBefore<EmphasisInlineParser>(new EmojiParser(EmojiMapping));
        }
    }

    /// <summary>
    /// Configures this extension for the specified pipeline stage.
    /// </summary>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        // EmojiInline is a LiteralInline, so its renderer must come first
        if (renderer is RoundtripRenderer roundtripRenderer && !roundtripRenderer.ObjectRenderers.Contains<RoundtripEmojiInlineRenderer>())
        {
            roundtripRenderer.ObjectRenderers.InsertBefore<Renderers.Roundtrip.Inlines.LiteralInlineRenderer>(new RoundtripEmojiInlineRenderer());
        }
    }
}
