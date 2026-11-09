using System.Net;
using System.Text;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals;

/// <summary>Decodes and encodes the texts of the labels of Mermaid.</summary>
internal static class MermaidText
{
    /// <summary>
    /// Decodes a label: the entity codes of Mermaid (<c>#quot;</c>, <c>#9829;</c>), the HTML entities, the <c>&lt;br&gt;</c>
    /// line breaks, and the HTML emphasis (<c>&lt;b&gt;</c>, <c>&lt;i&gt;</c>...), which becomes markdown. The other HTML tags are removed.
    /// </summary>
    public static DiagramText Decode(string raw, bool isMarkdown = false)
    {
        var builder = new StringBuilder(raw.Length);
        var hasEmphasis = false;
        var segments = new List<(bool IsTag, string Value)>();
        var position = 0;
        while (position < raw.Length)
        {
            var tagStart = raw.IndexOfOrdinal('<', position);
            if (tagStart < 0 || TryReadTag(raw, tagStart) is not { } tag)
            {
                var end = tagStart < 0 ? raw.Length : tagStart + 1;
                segments.Add((false, raw[position..end]));
                position = end;
                continue;
            }

            if (tagStart > position)
            {
                segments.Add((false, raw[position..tagStart]));
            }

            segments.Add((true, tag.Name));
            hasEmphasis |= tag.Name is "b" or "strong" or "i" or "em";
            position = tag.End;
        }

        var markdown = isMarkdown || hasEmphasis;
        foreach (var (isTag, value) in segments)
        {
            if (!isTag)
            {
                var text = DecodeEntities(value);
                builder.Append(markdown && !isMarkdown ? EscapeMarkdown(text) : text);
                continue;
            }

            switch (value)
            {
                case "br":
                    builder.Append('\n');
                    break;
                case "b" or "strong" or "/b" or "/strong":
                    builder.Append("**");
                    break;
                case "i" or "em" or "/i" or "/em":
                    builder.Append('*');
                    break;
            }
        }

        return new DiagramText(builder.ToString(), markdown ? DiagramTextFormat.Markdown : DiagramTextFormat.Plain);
    }

    /// <summary>Decodes a text that is not HTML: the entity codes and the <c>&lt;br&gt;</c> line breaks; the other tags stay as they are.</summary>
    public static string DecodePlain(string raw)
    {
        var builder = new StringBuilder(raw.Length);
        var position = 0;
        while (position < raw.Length)
        {
            var tagStart = raw.IndexOfOrdinal('<', position);
            if (tagStart < 0)
            {
                builder.Append(DecodeEntities(raw[position..]));
                break;
            }

            builder.Append(DecodeEntities(raw[position..tagStart]));
            if (TryReadTag(raw, tagStart) is { Name: "br" } tag)
            {
                builder.Append('\n');
                position = tag.End;
            }
            else
            {
                builder.Append('<');
                position = tagStart + 1;
            }
        }

        return builder.ToString();
    }

    /// <summary>Quotes a text that is read with <see cref="DecodeEntities"/>, such as a tooltip or an address: the line breaks become entity codes, not <c>&lt;br&gt;</c>.</summary>
    public static string QuoteAttribute(string value)
    {
        var builder = new StringBuilder("\"");
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '%' when i + 1 < value.Length && value[i + 1] == '%':
                    builder.Append("#37;");
                    break;
                case '"':
                    builder.Append("#quot;");
                    break;
                case '#':
                    builder.Append("#35;");
                    break;
                case '&':
                    builder.Append("#amp;");
                    break;
                case '\n' or '\r' or '\u2028' or '\u2029' or '\u0085':
                    builder.Append('#').Append(((int)c).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';');
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    /// <summary>Encodes a text that <see cref="DecodePlain"/> reads back: the characters it changes become entity codes, and the line feeds <c>&lt;br&gt;</c>.</summary>
    public static string EncodePlain(string text)
    {
        var quoted = EncodeQuoted(new DiagramText(text));
        return quoted[1..^1];
    }

    /// <summary>Decodes the entity codes of Mermaid (<c>#name;</c>, <c>#123;</c>) and the HTML entities.</summary>
    public static string DecodeEntities(string text)
    {
        if (text.IndexOf('#', StringComparison.Ordinal) < 0 && text.IndexOf('&', StringComparison.Ordinal) < 0)
            return text;

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '#' or '&')
            {
                var end = i + 1;
                if (end < text.Length && text[end] == '#' && c == '&')
                {
                    end++;
                }

                while (end < text.Length && end - i < 40 && char.IsAsciiLetterOrDigit(text[end]))
                {
                    end++;
                }

                if (end < text.Length && text[end] == ';' && end > i + 1)
                {
                    var name = text[(i + 1)..end];
                    var entity = c == '#' ? (char.IsAsciiDigit(name[0]) ? "&#" + name + ";" : "&" + name + ";") : "&" + name + ";";
                    var decoded = WebUtility.HtmlDecode(entity);
                    if (!string.Equals(decoded, entity, StringComparison.Ordinal))
                    {
                        builder.Append(decoded);
                        i = end;
                        continue;
                    }
                }
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>Encodes a text for a quoted label: the characters that the decoding changes become entity codes.</summary>
    public static string EncodeQuoted(DiagramText text)
    {
        // The lines of a markdown string are read trimmed
        var value = text.Format is DiagramTextFormat.Markdown ? string.Join('\n', text.Text.Split('\n').Select(line => line.Trim())) : text.Text;
        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                // %% starts a comment or a directive anywhere
                case '%' when i + 1 < value.Length && value[i + 1] == '%':
                    builder.Append("#37;");
                    break;
                case '"':
                    builder.Append("#quot;");
                    break;
                case '#':
                    builder.Append("#35;");
                    break;
                case '&':
                    builder.Append("#amp;");
                    break;
                case '<':
                    builder.Append("#lt;");
                    break;
                case '>':
                    builder.Append("#gt;");
                    break;
                case '`':
                    builder.Append("#96;");
                    break;
                case '\n' when text.Format is DiagramTextFormat.Plain:
                    builder.Append("<br>");
                    break;
                case '\r':
                    builder.Append("#13;");
                    break;
                case '\t':
                    builder.Append("#9;");
                    break;
                default:
                    if (char.IsControl(c) || c is '\u2028' or '\u2029' or '\u0085')
                    {
                        builder.Append('#').Append(((int)c).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';');
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return text.Format is DiagramTextFormat.Markdown ? "\"`" + builder + "`\"" : "\"" + builder + "\"";
    }

    private static string EscapeMarkdown(string text)
    {
        if (text.AsSpan().IndexOfAny('\\', '*', '_') < 0)
            return text;

        var builder = new StringBuilder(text.Length + 4);
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>Reads an HTML tag: <c>&lt;name&gt;</c>, <c>&lt;/name&gt;</c> or <c>&lt;name/&gt;</c>, with attributes.</summary>
    private static (string Name, int End)? TryReadTag(string text, int start)
    {
        var i = start + 1;
        var closing = i < text.Length && text[i] == '/';
        if (closing)
        {
            i++;
        }

        var nameStart = i;
        while (i < text.Length && char.IsAsciiLetterOrDigit(text[i]))
        {
            i++;
        }

        if (i == nameStart || !char.IsAsciiLetter(text[nameStart]))
            return null;

        var name = text[nameStart..i].ToLowerInvariant();
        var end = text.IndexOfOrdinal('>', i);
        if (end < 0 || text.AsSpan(i, end - i).Contains('<'))
            return null;

        return ((closing ? "/" : string.Empty) + name, end + 1);
    }
}
