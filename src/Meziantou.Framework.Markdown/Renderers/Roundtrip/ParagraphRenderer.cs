// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Renderers.Roundtrip;

/// <summary>
/// A Roundtrip renderer for a <see cref="ParagraphBlock"/>.
/// </summary>
/// <seealso cref="RoundtripObjectRenderer{ParagraphBlock}" />
[DebuggerDisplay("renderer.Writer.ToString()")]
public class ParagraphRenderer : RoundtripObjectRenderer<ParagraphBlock>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(RoundtripRenderer renderer, ParagraphBlock paragraph)
    {
        // The parser of a table that follows a paragraph on the next line attaches the attributes of the paragraph to the table
        var changedAttributes = renderer.GetChangedAttributes(paragraph);
        if (changedAttributes is not null && paragraph.Parent is { } parent && parent.IndexOf(paragraph) is var index && index + 1 < parent.Count && parent[index + 1] is Extensions.Tables.Table table && paragraph.LinesAfter is not { Count: > 0 } && table.LinesBefore is not { Count: > 0 })
        {
            Extensions.GenericAttributes.AttributesTrivia.ThrowCannotWrite(paragraph, "the table that follows the paragraph has them too");
        }

        renderer.RenderLinesBefore(paragraph);
        renderer.Write(paragraph.TriviaBefore);
        if (changedAttributes is not { NeedsPlace: true })
        {
            renderer.WriteLeafInline(paragraph);
        }
        else
        {
            // The changed attributes go at the end of the text, before the line break that ends it
            var lineBreak = paragraph.Inline?.LastChild as LineBreakInline;
            for (var inline = paragraph.Inline?.FirstChild; inline is not null && inline != lineBreak; inline = inline.NextSibling)
            {
                renderer.Write(inline);
            }

            // The marker of an alert is alone on the first line of its quote, with the paragraph that has no text
            if (paragraph.Parent is Extensions.Alerts.AlertBlock alert && paragraph.Line == alert.Line)
            {
                Extensions.GenericAttributes.AttributesTrivia.ThrowCannotWrite(paragraph, "they would follow the marker of the alert");
            }

            renderer.WriteAttributesAfterText(paragraph, changedAttributes, isAtx: false, StringSlice.Empty);
            if (lineBreak is not null)
            {
                renderer.Write(lineBreak);
            }
        }

        // The paragraph typically has LineBreakInlines as closing inline nodes
        renderer.RenderLinesAfter(paragraph);
    }
}
