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
            renderer.Write(link.Url);
            return;
        }

        if (link.IsAutoLink && !link.IsImage && GetLiteralText(link) is { } text)
        {
            if (IsNextToDelimiter(link.PreviousSibling, link.Parent, before: true) || IsNextToDelimiter(link.NextSibling, link.Parent, before: false))
            {
                // The brackets of an expanded link would change whether the delimiters next to it open or close emphasis
                renderer.Write(renderer.EscapeTablePipes ? text.Replace("|", "\\|", StringComparison.Ordinal) : text);
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
            if (link.FirstChild is LiteralInline literal && literal.Content.Length == link.Label.Length && literal.Content.Match(link.Label))
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
                renderer.Write('[').Write(renderer.EscapeTablePipes ? link.Label.Replace("|", "\\|", StringComparison.Ordinal) : link.Label).Write(']');
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

        // A destination with spaces or unbalanced parentheses, or an empty one, needs angle brackets
        var depth = 0;
        var needsBrackets = url.Length == 0 || url[0] == '<';
        foreach (var c in url)
        {
            if (c <= ' ' || (c == ')' && --depth < 0))
            {
                needsBrackets = true;
                break;
            }

            if (c == '(')
            {
                depth++;
            }
        }

        needsBrackets |= depth != 0;
        var escaped = Escape(url, needsBrackets ? "<>" : "", decodedEntities);
        return needsBrackets ? "<" + escaped + ">" : escaped;
    }

    /// <summary>
    /// Writes a link title, escaped so that it is parsed back to the same text.
    /// </summary>
    /// <param name="renderer">The renderer.</param>
    /// <param name="title">The title.</param>
    /// <param name="decodedEntities">Whether the parser decoded the entities of the title, so that ampersands must be encoded.</param>
    internal static void WriteTitle(NormalizeRenderer renderer, string title, bool decodedEntities = true)
    {
        var escaped = "\"" + Escape(title, "\"", decodedEntities) + "\"";
        renderer.Write(renderer.EscapeTablePipes ? escaped.Replace("|", "\\|", StringComparison.Ordinal) : escaped);
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
