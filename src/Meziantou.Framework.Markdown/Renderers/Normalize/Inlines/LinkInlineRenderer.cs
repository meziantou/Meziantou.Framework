// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Text;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Renderers.Normalize.Inlines;

/// <summary>
/// A Normalize renderer for a <see cref="LinkInline"/>.
/// </summary>
/// <seealso cref="NormalizeObjectRenderer{LinkInline}" />
public class LinkInlineRenderer : NormalizeObjectRenderer<LinkInline>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(NormalizeRenderer renderer, LinkInline link)
    {
        if (link.IsAutoLink && !renderer.Options.ExpandAutoLinks)
        {
            renderer.Write(GetAutoLinkSource(link, GetLiteralText(link)));
            return;
        }

        if (link.IsAutoLink && !link.IsImage && GetLiteralText(link) is { } text)
        {
            if (!IsExpandedAutoLink(link))
            {
                // The brackets of an expanded link would change whether the delimiters next to it open or close emphasis
                var source = GetAutoLinkSource(link, text);
                renderer.Write(renderer.EscapeTablePipes ? source.Replace("|", "\\|", StringComparison.Ordinal) : source);
                return;
            }

            // The text of an autolink is raw, so it could contain the syntax of a link, or of any other inline
            var destination = EscapeDestination(link.Url);
            var markdown = string.Concat("[", renderer.EscapeLinkText(text, destination), "](", destination, ")");
            renderer.Write(renderer.EscapeTablePipes ? markdown.Replace("|", "\\|", StringComparison.Ordinal) : markdown);
            return;
        }

        if (link.IsImage)
        {
            renderer.Write('!');
        }
        renderer.Write('[');
        renderer.WriteChildren(link);
        renderer.Write(']');

        if (link.Label != null)
        {
            if (link.FirstChild is LiteralInline { NextSibling: null } literal && literal.Content.Length == link.Label.Length && literal.Content.Match(link.Label))
            {
                // collapsed reference and shortcut links
                if (!link.IsShortcut)
                {
                    renderer.Write("[]");
                }
            }
            else
            {
                // full link
                var label = EscapeLabel(link.Label);
                renderer.Write('[').Write(renderer.EscapeTablePipes ? label.Replace("|", "\\|", StringComparison.Ordinal) : label).Write(']');
            }
        }
        // A link with a dynamic URL, such as a reference to a heading, stays a shortcut reference
        else if (link.Url is not null || link.GetDynamicUrl is null)
        {
            renderer.Write('(');
            WriteDestination(renderer, link.Url);
            if (link.Title is { Length: > 0 } title)
            {
                renderer.Write(' ');
                WriteTitle(renderer, title);
            }

            renderer.Write(')');
        }
    }

    // Gets the text that is parsed back as the autolink: its text, which is its URL without the scheme of a mailto: or tel:
    // link, or without the scheme that the parser adds to a www. link
    private static string GetAutoLinkSource(LinkInline link, string? text)
    {
        if (text is null || link.Url is null)
        {
            return link.Url ?? text ?? string.Empty;
        }

        var isWwwLink = text.StartsWith("www.", StringComparison.Ordinal) && (link.Url == "http://" + text || link.Url == "https://" + text);
        return isWwwLink || !link.Url.EndsWith(text, StringComparison.Ordinal) ? text : link.Url;
    }

    // Gets a value indicating whether the autolink is written as a link, whose text escapes the backticks
    internal static bool IsExpandedAutoLink(LinkInline link)
    {
        return !IsNextToDelimiter(link.PreviousSibling, link.Parent, before: true) && !IsNextToDelimiter(link.NextSibling, link.Parent, before: false);
    }

    private static bool IsNextToDelimiter(Inline? sibling, ContainerInline? parent, bool before)
    {
        return sibling switch
        {
            null => parent is EmphasisInline,
            EmphasisInline => true,
            LiteralInline { Content.Length: > 0 } literal => (before ? literal.Content[literal.Content.End] : literal.Content[literal.Content.Start]) is '*' or '_' or '~' or '^' or '=' or '+',
            _ => false,
        };
    }

    private static string? GetLiteralText(LinkInline link)
    {
        var builder = new StringBuilder();
        for (var inline = link.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            if (inline is not LiteralInline literal)
            {
                return null;
            }

            builder.Append(literal.Content.AsSpan());
        }

        return builder.ToString();
    }

    /// <summary>
    /// Escapes a link label, which is stored without its backslash escapes. A backslash of a label is always escaped in the
    /// source, as the parser only accepts a backslash before a bracket or another backslash.
    /// </summary>
    internal static string EscapeLabel(string label) => Escape(label, "[]\\", decodedEntities: false);

    /// <summary>
    /// Writes a link destination, escaped so that it is parsed back to the same URL.
    /// </summary>
    /// <param name="renderer">The renderer.</param>
    /// <param name="url">The destination.</param>
    /// <param name="decodedEntities">Whether the parser decoded the entities of the destination, so that ampersands must be encoded.</param>
    internal static void WriteDestination(NormalizeRenderer renderer, string? url, bool decodedEntities = true)
    {
        var escaped = EscapeDestination(url, decodedEntities);
        renderer.Write(renderer.EscapeTablePipes ? escaped.Replace("|", "\\|", StringComparison.Ordinal) : escaped);
    }

    internal static string EscapeDestination(string? url, bool decodedEntities = true)
    {
        url ??= string.Empty;

        // A destination with spaces or ASCII control characters, or an empty one, needs angle brackets. The unbalanced
        // parentheses of another one are escaped: in angle brackets, they would be balanced for a link around this one, such as
        // the text before [](<)>).
        var depth = 0;
        var needsBrackets = url.Length == 0;
        var unbalanced = url.Length > 0 && url[0] == '<';
        foreach (var c in url)
        {
            // The parser takes the other Unicode spaces, such as a no-break space, for the end of a destination
            if (c <= ' ' || char.IsControl(c) || (c > (char)0x7F && c.IsWhitespace()))
            {
                needsBrackets = true;
                break;
            }

            if (c == ')' && --depth < 0)
            {
                unbalanced = true;
            }
            else if (c == '(')
            {
                depth++;
            }
        }

        unbalanced |= depth != 0;
        var escaped = needsBrackets ? "<" + Escape(url, "<>", decodedEntities) + ">" : Escape(url, unbalanced ? "()<" : "", decodedEntities);

        // A destination cannot contain a line ending, or another control character, so they are written as entities, which the
        // parser decodes
        if (decodedEntities && (url.AsSpan().ContainsAnyInRange('\0', (char)0x1F) || url.AsSpan().ContainsAnyInRange((char)0x7F, (char)0x9F)))
        {
            var builder = new StringBuilder(escaped.Length + 8);
            foreach (var c in escaped)
            {
                if (char.IsControl(c) && c is not '\t')
                {
                    // A backslash before the line ending is text, which would escape the ampersand of the entity
                    if (builder.Length > 0 && builder[^1] == '\\')
                    {
                        builder.Append('\\');
                    }

                    builder.Append("&#x").Append(((int)c).ToString("X", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
                }
                else
                {
                    builder.Append(c);
                }
            }

            escaped = builder.ToString();
        }

        return escaped;
    }

    /// <summary>
    /// Writes a link title, escaped so that it is parsed back to the same text.
    /// </summary>
    /// <param name="renderer">The renderer.</param>
    /// <param name="title">The title.</param>
    /// <param name="decodedEntities">Whether the parser decoded the entities of the title, so that ampersands must be encoded.</param>
    internal static void WriteTitle(NormalizeRenderer renderer, string title, bool decodedEntities = true)
    {
        var inner = Escape(title, "\"", decodedEntities);

        // A blank line would end the paragraph, so the line endings of a title with one are written as entities
        if (decodedEntities && HasBlankLine(inner))
        {
            var builder = new StringBuilder(inner.Length + 16);
            foreach (var c in inner)
            {
                if (c is '\n' or '\r')
                {
                    // A backslash before the line ending is text, which would escape the ampersand of the entity
                    if (builder.Length > 0 && builder[^1] == '\\')
                    {
                        builder.Append('\\');
                    }

                    builder.Append(c == '\n' ? "&#10;" : "&#13;");
                }
                else
                {
                    builder.Append(c);
                }
            }

            inner = builder.ToString();
        }

        var escaped = "\"" + inner + "\"";
        renderer.Write(renderer.EscapeTablePipes ? escaped.Replace("|", "\\|", StringComparison.Ordinal) : escaped);
    }

    private static bool HasBlankLine(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        return lines.Length > 1 && lines.Any(line => line.AsSpan().Trim(" \t").IsEmpty);
    }

    // Escapes the specified characters, the backslashes that would escape the next character, and the ampersands that would start an entity
    private static string Escape(string text, string characters, bool decodedEntities)
    {
        StringBuilder? builder = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (decodedEntities && c == '&' && IsEntityStart(text.AsSpan(i + 1)))
            {
                // The entity is decoded once, even where a backslash escape is not honored
                builder ??= new StringBuilder(text, 0, i, text.Length + 8);
                builder.Append("&amp;");
                continue;
            }

            if (characters.Contains(c, StringComparison.Ordinal) || (c == '\\' && (i + 1 == text.Length || text[i + 1].IsAsciiPunctuation())))
            {
                builder ??= new StringBuilder(text, 0, i, text.Length + 8);
                builder.Append('\\');
            }

            builder?.Append(c);
        }

        return builder?.ToString() ?? text;
    }

    internal static bool IsEntityStart(ReadOnlySpan<char> text)
    {
        var length = text.Length > 0 && text[0] == '#' ? 1 : 0;
        while (length < text.Length && char.IsAsciiLetterOrDigit(text[length]))
        {
            length++;
        }

        return length > 0 && length < text.Length && text[length] == ';';
    }
}
