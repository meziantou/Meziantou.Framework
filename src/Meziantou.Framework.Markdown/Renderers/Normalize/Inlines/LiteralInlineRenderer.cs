// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Renderers.Normalize.Inlines;

/// <summary>
/// A Normalize renderer for a <see cref="LiteralInline"/>.
/// </summary>
/// <seealso cref="NormalizeObjectRenderer{LiteralInline}" />
public class LiteralInlineRenderer : NormalizeObjectRenderer<LiteralInline>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(NormalizeRenderer renderer, LiteralInline obj)
    {
        if (obj.IsFirstCharacterEscaped && obj.Content.Length > 0 && obj.Content[obj.Content.Start].IsAsciiPunctuation()
            && !((renderer.EscapeTablePipes || renderer.EscapeLiteralPipes) && obj.Content.CurrentChar is '|' or '\\')
            && !(obj.Content.CurrentChar == '`' && IsInLink(obj)))
        {
            renderer.Write('\\');
        }
        else if (!obj.IsFirstCharacterEscaped && obj.Content.Length > 0 && obj.Content.CurrentChar is '(' or '[' && obj.PreviousSibling is LinkInline { IsShortcut: true })
        {
            // A destination or a label after the text of a shortcut reference link would be part of the link
            renderer.Write('\\');
        }
        else if (!obj.IsFirstCharacterEscaped && obj.Content.Length > 0 && obj.Content.CurrentChar == ':'
            && obj.PreviousSibling is LinkInline { IsImage: false, PreviousSibling: null, Parent.Parent: null } link
            && (!link.IsAutoLink || (renderer.Options.ExpandAutoLinks && LinkInlineRenderer.IsExpandedAutoLink(link))))
        {
            // A link at the start of a paragraph followed by a colon would be the label of a link reference definition, even one
            // whose destination is on the next line. An autolink written as its URL would take the backslash.
            renderer.Write('\\');
        }
        if ((renderer.EscapeTablePipes || renderer.EscapeLiteralPipes) && obj.Content.AsSpan().IndexOfAny('\\', '|') >= 0)
        {
            renderer.Write(EscapeLessThan(renderer, obj, obj.Content.ToString().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal)));
        }
        else if (obj.Content.AsSpan().Contains('<') || (obj.Content.AsSpan().Contains('`') && IsInLink(obj)))
        {
            renderer.Write(EscapeLessThan(renderer, obj, obj.Content.ToString()));
        }
        else
        {
            renderer.Write(ref obj.Content);
        }

        // A backslash at the end of a line is a hard line break, so escape a literal one, unless it is escaped already
        if (obj.NextSibling is LineBreakInline { IsHard: false } && obj.Content.Length > 0 && obj.Content[obj.Content.End] == '\\'
            && !(obj.IsFirstCharacterEscaped && obj.Content.Length == 1) && !(renderer.EscapeTablePipes || renderer.EscapeLiteralPipes))
        {
            renderer.Write('\\');
        }
    }

    // A '<' that is text can start an autolink or raw HTML once the inlines after it are written differently, such as the
    // destination of a link ("<a:[](<b>)>" written "<a:[](b)>"), or the label after a link text, so it is escaped when a
    // scheme, an email address or a tag can follow it. With the autolinks, a '<' in a word is not escaped: the backslash could be part of a URL before it.
    private static string EscapeLessThan(NormalizeRenderer renderer, LiteralInline literal, string text)
    {
        // The text of a shortcut or collapsed reference link is its label, which is compared with its backslashes
        if (literal.Parent is LinkInline { Label: { } label } && literal.PreviousSibling is null && literal.NextSibling is null && literal.Content.Length == label.Length && literal.Content.Match(label))
        {
            return text;
        }

        var isInLink = IsInLink(literal);
        var hasAutoLinks = renderer.Pipeline?.InlineParsers.Contains<Extensions.AutoLinks.AutoLinkParser>() ?? false;
        System.Text.StringBuilder? builder = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            // A backtick of a link text could pair with one of its label, or close a code span opened before the link, where a
            // backslash does not escape it, so it is written as an entity
            if (c == '`' && isInLink)
            {
                builder ??= new System.Text.StringBuilder(text, 0, i, text.Length + 8);
                builder.Append("&#96;");
                continue;
            }

            if (c == '<' && !(i == 0 && literal.IsFirstCharacterEscaped)
                && (isInLink || ((!hasAutoLinks || CanEscapeInWord(literal, text, i)) && GetNextCharacter(literal, text, i) is > ' ' and < (char)0x7F and not '<' and not '>' and not '\\' && CanStartAutoLinkOrHtml(literal, text, i))))
            {
                builder ??= new System.Text.StringBuilder(text, 0, i, text.Length + 4);
                builder.Append('\\');
            }

            builder?.Append(c);
        }

        return builder?.ToString() ?? text;
    }

    // Whether a backslash before the character cannot be part of a URL of the autolinks: the word before it is not a URL
    private static bool CanEscapeInWord(LiteralInline literal, string text, int index)
    {
        var start = index;
        while (start > 0 && !text[start - 1].IsWhitespace())
        {
            start--;
        }

        if (start == 0 && !StartsWord(literal))
        {
            return false;
        }

        var word = text.AsSpan(start, index - start);
        return !word.Contains(':') && !word.Contains("www.", StringComparison.OrdinalIgnoreCase) && !word.Contains('@');
    }

    // Whether the literal starts a word: it is the first inline of its leaf block or of a link text, or it follows whitespace
    private static bool StartsWord(LiteralInline literal)
    {
        return GetPreviousInline(literal) switch
        {
            null or LineBreakInline => true,
            LiteralInline { Content.Length: > 0 } previous => previous.Content[previous.Content.End].IsWhitespace(),
            _ => false,
        };

        // The inline written before this one: the last inline of its previous sibling, or the inline before its parent
        static Inline? GetPreviousInline(Inline inline)
        {
            while (inline.PreviousSibling is null)
            {
                // The text of a link starts after its bracket
                if (inline.Parent is not { Parent: not null } parent || parent is LinkInline)
                {
                    return null;
                }

                inline = parent;
            }

            var sibling = inline.PreviousSibling;
            while (sibling is ContainerInline { LastChild: { } last } and not LinkInline)
            {
                sibling = last;
            }

            return sibling;
        }
    }

    // Whether the text after the '<' could be an autolink (a scheme or an email address) or an HTML tag, once the inlines after
    // the literal are written differently
    private static bool CanStartAutoLinkOrHtml(LiteralInline literal, string text, int index)
    {
        // The text of the literals after this one counts, as it is written as is. Without a whitespace or a '>' in them, the
        // inline after them, which can be written differently, decides.
        var following = text.Substring(index + 1);
        Inline? next = literal.NextSibling;
        for (; next is LiteralInline nextLiteral; next = next.NextSibling)
        {
            following = string.Concat(following, nextLiteral.IsFirstCharacterEscaped ? "\\" : "", nextLiteral.Content.ToString());
        }

        var end = following.AsSpan().IndexOfAny(" \t\n<>");
        if (end < 0)
        {
            if (next is not null)
            {
                return true;
            }

            for (Inline? inline = literal.Parent; inline is not null; inline = inline.Parent)
            {
                if (inline.NextSibling is not null)
                {
                    return true;
                }
            }

            return false;
        }

        var rest = following.AsSpan(0, end);
        if (rest.ContainsAny(':', '@'))
        {
            return true;
        }

        var c = rest.IsEmpty ? following[end] : rest[0];
        if (c is '/' or '!' or '?')
        {
            return true;
        }

        if (!char.IsAsciiLetter(c))
        {
            return false;
        }

        // The name of a tag is followed by whitespace, '/' or '>'
        var nameLength = 1;
        while (nameLength < rest.Length && (char.IsAsciiLetterOrDigit(rest[nameLength]) || rest[nameLength] == '-'))
        {
            nameLength++;
        }

        return nameLength == rest.Length || rest[nameLength] == '/';
    }

    // The character written after the one at the index, as far as the literals after this one tell
    private static char GetNextCharacter(LiteralInline literal, string text, int index)
    {
        if (index + 1 < text.Length)
        {
            return text[index + 1];
        }

        return literal.NextSibling is LiteralInline { Content.Length: > 0 } next ? (next.IsFirstCharacterEscaped ? '\\' : next.Content[next.Content.Start]) : '\0';
    }

    // Whether the inline is in the text of a link, whose literals are written by this renderer
    internal static bool IsInLink(Inline inline)
    {
        for (var parent = inline.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is LinkInline { IsAutoLink: false })
            {
                return true;
            }
        }

        return false;
    }
}
