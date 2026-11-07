// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Normalize;

/// <summary>
/// A Normalize renderer for a <see cref="ParagraphBlock"/>.
/// </summary>
/// <seealso cref="NormalizeObjectRenderer{ParagraphBlock}" />
public class ParagraphRenderer : NormalizeObjectRenderer<ParagraphBlock>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(NormalizeRenderer renderer, ParagraphBlock obj)
    {
        // The definitions parsed from the start of a paragraph are moved to the group of the document, which can be after the
        // rest of the paragraph. When its first line would start another block, such as an HTML tag alone on its line, the
        // definitions are written before it, so that the line continues them as in the source.
        if (renderer.Definitions is { Count: > 0 } && obj.Parent is MarkdownDocument or QuoteBlock or ListItemBlock
            && renderer.IsParagraphContinuationOnly(obj) && renderer.WriteDefinitionsOf(obj))
        {
            renderer.ContinuesParagraph = true;
        }

        renderer.WriteParagraphInline(obj);

        // The definitions written before the paragraph are not written again at the end of the document
        if (renderer.GetNextSibling(obj) is LinkReferenceDefinitionGroup group && renderer.GetNextSibling(group) is null && !renderer.HasDefinitionsToWrite(group))
        {
            return;
        }

        renderer.FinishBlock(!renderer.CompactParagraph);
    }
}
