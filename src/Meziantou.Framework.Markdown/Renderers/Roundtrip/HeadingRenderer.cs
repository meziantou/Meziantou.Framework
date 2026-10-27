// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.IO;
using System.Text;
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
        // With the generic attributes, the attributes that were changed after the parsing replace the first ones written in the
        // text, and the others are written as empty attributes. Without any, they go after the content.
        var changedAttributes = HeadingAttributesTrivia.GetChanged(obj);
        string? attributes = null;
        var isWritten = false;
        if (changedAttributes is null)
        {
            renderer.RenderLinesBefore(obj);
        }
        else
        {
            attributes = changedAttributes.Format(obj);
            isWritten = WriteLinesBefore(renderer, obj, changedAttributes, attributes);
        }

        if (obj.IsSetext)
        {
            var headingChar = obj.Level == 1 ? '=' : '-';

            if (changedAttributes is { HasInlineAttributes: true } && !isWritten)
            {
                WriteInlineWithAttributes(renderer, obj, attributes);
            }
            else if (changedAttributes is not null && !isWritten && attributes is not null)
            {
                WriteInlineThenAttributes(renderer, obj, attributes);
            }
            else
            {
                renderer.WriteLeafInline(obj);
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
            if (changedAttributes is null)
            {
                renderer.WriteLeafInline(obj);
                renderer.Write(obj.TriviaAfter);
            }
            else
            {
                WriteAtxContent(renderer, obj, changedAttributes, isWritten ? null : attributes, isWritten);
            }

            renderer.WriteLine(obj.NewLine);
        }

        renderer.RenderLinesAfter(obj);
    }

    // Writes the lines before the heading. The lines that only have attributes get the attributes of the heading instead, so
    // that the containers keep the same lines. Returns whether the attributes were written.
    private static bool WriteLinesBefore(RoundtripRenderer renderer, HeadingBlock obj, HeadingAttributesTrivia changedAttributes, string? attributes)
    {
        if (obj.LinesBefore is null)
        {
            return false;
        }

        var written = false;
        foreach (var line in obj.LinesBefore)
        {
            if (changedAttributes.LinesBefore?.Exists(text => ReferenceEquals(text, line.Text)) is true)
            {
                renderer.Write(ReplaceAttributes(line.AsSpan(), written ? null : attributes));
                written = true;
            }
            else
            {
                renderer.Write(line);
            }

            renderer.WriteLine(line.NewLine);
        }

        return written;
    }

    // Replaces the attributes of a text that only has attributes, one line or more. The first line gets the attributes, and the
    // other lines get empty attributes, which keep them lines with only attributes.
    private static string ReplaceAttributes(ReadOnlySpan<char> text, string? attributes)
    {
        var sb = new StringBuilder(text.Length);
        var isFirst = true;
        while (true)
        {
            var end = text.IndexOfAny('\r', '\n');
            var line = end < 0 ? text : text[..end];
            var withoutLeading = line.TrimStart(" \t");
            var content = withoutLeading.TrimEnd(" \t");
            sb.Append(line[..(line.Length - withoutLeading.Length)]);
            if (!content.IsEmpty)
            {
                sb.Append(isFirst && attributes is not null ? attributes : "{}");
                isFirst = false;
            }

            sb.Append(withoutLeading[content.Length..]);
            if (end < 0)
            {
                return sb.ToString();
            }

            var newLineLength = text[end] is '\r' && end + 1 < text.Length && text[end + 1] is '\n' ? 2 : 1;
            sb.Append(text.Slice(end, newLineLength));
            text = text[(end + newLineLength)..];
        }
    }

    // Writes the content of an ATX heading and its trivia after. The attributes replace the ones of the trivia after or of the
    // content, else they go after the closing sequence, before the spaces that end the line: the parser reads them from the first
    // '{' of the line, then finds the closing sequence before them. When the content has a '{', the parser reads them as inline
    // attributes instead, so they go before the closing sequence, which it then finds at the end of the line.
    private static void WriteAtxContent(RoundtripRenderer renderer, HeadingBlock obj, HeadingAttributesTrivia changedAttributes, string? attributes, bool isWritten)
    {
        var trivia = obj.TriviaAfter;
        var text = changedAttributes.TriviaAfterAttributes;
        if (!text.IsEmpty && ReferenceEquals(text.Text, trivia.Text) && text.Start >= trivia.Start && text.End <= trivia.End)
        {
            // The parser reads the attributes from the first '{' of the line, so the content before them has none
            renderer.WriteLeafInline(obj);
            renderer.Write(new StringSlice(trivia.Text, trivia.Start, text.Start - 1));
            renderer.Write(HeadingAttributesTrivia.ReplaceAttributes(attributes, text.AsSpan()));
            renderer.Write(new StringSlice(trivia.Text, text.End + 1, trivia.End));
            return;
        }

        if (!isWritten && changedAttributes.HasInlineAttributes)
        {
            WriteInlineWithAttributes(renderer, obj, attributes);
        }
        else if (attributes is null)
        {
            renderer.WriteLeafInline(obj);
        }
        else
        {
            var end = trivia.End;
            while (end >= trivia.Start && trivia.Text[end].IsSpaceOrTab())
            {
                end--;
            }

            WriteInlineThenAttributes(renderer, obj, attributes, new StringSlice(trivia.Text, trivia.Start, end));
            renderer.Write(new StringSlice(trivia.Text, end + 1, trivia.End));
            return;
        }

        renderer.Write(trivia);
    }

    // Writes the content of a heading, whose first attributes are replaced with the specified ones. When the content no longer
    // has them, the attributes go after it.
    private static void WriteInlineWithAttributes(RoundtripRenderer renderer, HeadingBlock obj, string? attributes)
    {
        renderer.PendingHeadingAttributes = (obj, attributes);
        try
        {
            renderer.WriteLeafInline(obj);
            if (renderer.PendingHeadingAttributes is not null)
            {
                WriteAttributes(renderer, attributes);
            }
        }
        finally
        {
            renderer.PendingHeadingAttributes = null;
        }
    }

    // Writes the content of the heading, then the attributes. The content is rendered first, to check that the parser reads the
    // attributes back. They are not written before the content, as the character before its first delimiter run or autolink
    // would change.
    private static void WriteInlineThenAttributes(RoundtripRenderer renderer, HeadingBlock obj, string attributes, StringSlice closingSequence = default)
    {
        var writer = renderer.Writer;
        using var buffer = new StringWriter(writer.FormatProvider);
        renderer.ReplaceWriter(buffer);
        try
        {
            renderer.WriteLeafInline(obj);
        }
        finally
        {
            renderer.ReplaceWriter(writer);
        }

        // The buffer has the indent of the containers already
        var inline = buffer.ToString();
        if (inline.Contains('{', StringComparison.Ordinal))
        {
            HeadingAttributesTrivia.CheckAttributesAfterText(inline + " ", attributes, obj.IsSetext);
            writer.Write(inline);
            WriteAttributes(renderer, attributes);
            renderer.Write(closingSequence);
        }
        else
        {
            HeadingAttributesTrivia.CheckAttributesAfterText(inline + closingSequence.ToString() + " ", attributes, obj.IsSetext);
            writer.Write(inline);
            renderer.Write(closingSequence);
            WriteAttributes(renderer, attributes);
        }
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
