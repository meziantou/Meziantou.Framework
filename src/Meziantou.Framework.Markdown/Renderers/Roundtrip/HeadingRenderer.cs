// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Extensions.GenericAttributes;
using Meziantou.Framework.Markdown.Helpers;
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
        // With the generic attributes, the attributes that were changed after the parsing replace the ones written in the text
        var changedAttributes = HeadingAttributesTrivia.GetChanged(obj);
        var attributes = changedAttributes?.Format(obj);

        if (changedAttributes is null)
        {
            renderer.RenderLinesBefore(obj);
        }
        else
        {
            WriteLinesBefore(renderer, obj, changedAttributes);
        }

        if (obj.IsSetext)
        {
            var headingChar = obj.Level == 1 ? '=' : '-';

            renderer.WriteLeafInline(obj);
            WriteAttributes(renderer, attributes);
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
            if (changedAttributes is null)
            {
                renderer.Write(obj.TriviaAfter);
            }
            else
            {
                WriteTriviaAfter(renderer, obj, changedAttributes, attributes);
            }

            renderer.WriteLine(obj.NewLine);
        }

        renderer.RenderLinesAfter(obj);
    }

    // Writes the lines before the heading, except the ones that only have attributes
    private static void WriteLinesBefore(RoundtripRenderer renderer, HeadingBlock obj, HeadingAttributesTrivia changedAttributes)
    {
        if (obj.LinesBefore is null)
        {
            return;
        }

        foreach (var line in obj.LinesBefore)
        {
            if (changedAttributes.LinesBefore?.Exists(text => ReferenceEquals(text, line.Text)) is true)
            {
                continue;
            }

            renderer.Write(line);
            renderer.WriteLine(line.NewLine);
        }
    }

    // Writes the trivia after an ATX heading, with the attributes in place of the ones written in the text. Without them, the
    // attributes go after the closing sequence, before the spaces that end the line.
    private static void WriteTriviaAfter(RoundtripRenderer renderer, HeadingBlock obj, HeadingAttributesTrivia changedAttributes, string? attributes)
    {
        var trivia = obj.TriviaAfter;
        var text = changedAttributes.TriviaAfterAttributes;
        if (!text.IsEmpty && ReferenceEquals(text.Text, trivia.Text) && text.Start >= trivia.Start && text.End <= trivia.End)
        {
            renderer.Write(new StringSlice(trivia.Text, trivia.Start, text.Start - 1));
            renderer.Write(attributes);
            renderer.Write(new StringSlice(trivia.Text, text.End + 1, trivia.End));
            return;
        }

        var end = trivia.End;
        while (end >= trivia.Start && trivia.Text[end].IsSpaceOrTab())
        {
            end--;
        }

        renderer.Write(new StringSlice(trivia.Text, trivia.Start, end));
        WriteAttributes(renderer, attributes);
        renderer.Write(new StringSlice(trivia.Text, end + 1, trivia.End));
    }

    private static void WriteAttributes(RoundtripRenderer renderer, string? attributes)
    {
        if (attributes is not null)
        {
            renderer.Write(' ');
            renderer.Write(attributes);
        }
    }
}
