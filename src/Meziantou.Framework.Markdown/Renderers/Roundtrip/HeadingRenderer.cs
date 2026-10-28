// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Extensions.GenericAttributes;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Roundtrip;

/// <summary>
/// An Roundtrip renderer for a <see cref="HeadingBlock"/>.
/// </summary>
/// <seealso cref="RoundtripObjectRenderer{HeadingBlock}" />
public class HeadingRenderer : RoundtripObjectRenderer<HeadingBlock>
{
    private static readonly string[] HeadingTexts = [
        "#",
        "##",
        "###",
        "####",
        "#####",
        "######",
    ];

    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(RoundtripRenderer renderer, HeadingBlock obj)
    {
        // The changed attributes replace the ones of the text, or are written after the content
        var changedAttributes = renderer.GetChangedAttributes(obj);
        renderer.RenderLinesBefore(obj);

        if (obj.IsSetext)
        {
            var headingChar = obj.Level == 1 ? '=' : '-';

            renderer.WriteLeafInline(obj);
            if (changedAttributes is { NeedsPlace: true })
            {
                renderer.WriteAttributesAfterText(obj, changedAttributes, isAtx: false, StringSlice.Empty);
            }

            renderer.WriteLine(obj.SetextNewline);
            renderer.Write(obj.TriviaBefore);
            renderer.Write(headingChar, obj.HeaderCharCount);

            // The trivia after the underline is the end of its line
            renderer.Write(obj.TriviaAfter);
            renderer.WriteLine(obj.NewLine);
        }
        else
        {
            renderer.Write(obj.TriviaBefore);

            if (obj.Level is > 0 and <= 6)
            {
                renderer.Write(HeadingTexts[obj.Level - 1]);
            }
            else
            {
                renderer.Write('#', obj.Level);
            }

            renderer.Write(obj.TriviaAfterAtxHeaderChar);
            renderer.WriteLeafInline(obj);
            WriteTriviaAfter(renderer, obj, changedAttributes);
            renderer.WriteLine(obj.NewLine);
        }

        renderer.RenderLinesAfter(obj);
    }

    // Writes the trivia after an ATX heading: the closing sequence and the spaces that end the line, and the attributes that the
    // parser read from the first '{' of the line
    private static void WriteTriviaAfter(RoundtripRenderer renderer, HeadingBlock obj, ChangedAttributes? changedAttributes)
    {
        var trivia = obj.TriviaAfter;
        if (changedAttributes is null)
        {
            renderer.Write(trivia);
            return;
        }

        var source = changedAttributes.Trivia?.TriviaAfterAttributes ?? StringSlice.Empty;
        if (!source.IsEmpty && ReferenceEquals(source.Text, trivia.Text) && source.Start >= trivia.Start && source.End <= trivia.End)
        {
            renderer.Write(new StringSlice(trivia.Text, trivia.Start, source.Start - 1));
            renderer.Write(AttributesTrivia.ReplaceAttributes(obj, changedAttributes.Take(), source.AsSpan()));
            renderer.Write(new StringSlice(trivia.Text, source.End + 1, trivia.End));
            return;
        }

        if (!changedAttributes.NeedsPlace)
        {
            renderer.Write(trivia);
            return;
        }

        // The extensions added after the generic attributes have parsers that do not read them
        if (obj.Parser is not IAttributesParseable { TryParseAttributes: not null })
        {
            AttributesTrivia.ThrowCannotWrite(obj, "the parser of the heading does not read attributes, as the generic attributes were enabled before its extension");
        }

        // The attributes go before the spaces that end the line
        var end = trivia.End;
        while (end >= trivia.Start && trivia.Text[end].IsSpaceOrTab())
        {
            end--;
        }

        renderer.WriteAttributesAfterText(obj, changedAttributes, isAtx: true, new StringSlice(trivia.Text ?? "", trivia.Start, end));
        renderer.Write(new StringSlice(trivia.Text ?? "", end + 1, trivia.End));
    }
}
