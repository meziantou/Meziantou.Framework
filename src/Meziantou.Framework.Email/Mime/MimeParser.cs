namespace Meziantou.Framework.Email.Internals;

/// <summary>Parses messages ([RFC 5322]) and their MIME structure ([RFC 2045], [RFC 2046]), leniently: a malformed message gives a best-effort tree, not an exception.</summary>
internal static class MimeParser
{
    public static MimeMessage ParseMessage(ReadOnlyMemory<byte> data, MimeReadOptions options, int depth)
    {
        var position = 0;
        var headers = ReadHeaders(data, ref position, allowMboxFromLine: depth == 0);
        var message = new MimeMessage(parsed: true);
        var contentHeaders = new List<MimeHeader>();
        foreach (var header in headers)
        {
            if (header.Name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                contentHeaders.Add(header);
            }
            else
            {
                message.Headers.Add(header);
            }
        }

        message.Body = ParseEntity(contentHeaders, data[position..], new MimeContentType("text", "plain"), options, depth);
        return message;
    }

    /// <summary>Parses an entity: its headers, then its content, such as the content of an S/MIME signature or encryption.</summary>
    public static MimeEntity ParseEntity(ReadOnlyMemory<byte> data, MimeReadOptions options)
    {
        var position = 0;
        var headers = ReadHeaders(data, ref position, allowMboxFromLine: false);
        return ParseEntity(headers, data[position..], new MimeContentType("text", "plain"), options, depth: 1);
    }

    private static MimeEntity ParseEntity(List<MimeHeader> headers, ReadOnlyMemory<byte> content, MimeContentType defaultContentType, MimeReadOptions options, int depth)
    {
        var contentTypeHeader = headers.Find(header => header.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
        var contentType = contentTypeHeader is null ? defaultContentType : MimeContentType.Parse(contentTypeHeader.RawValue);
        MimeEntity entity;
        if (contentType.IsMimeType("multipart", "*") && depth < options.MaxDepth)
        {
            // A multipart without a boundary has no parts: its content is its preamble
            entity = string.IsNullOrEmpty(contentType.Boundary) ? new MimeMultipart(parsed: true) { Preamble = content } : ParseMultipart(content, contentType, options, depth);
        }
        else if ((contentType.IsMimeType("message", "rfc822") || contentType.IsMimeType("message", "global")) && depth < options.MaxDepth)
        {
            var encoding = headers.Find(header => header.Name.Equals("Content-Transfer-Encoding", StringComparison.OrdinalIgnoreCase))?.Value.Trim().ToLowerInvariant();
            var messageContent = encoding switch
            {
                "base64" => MimeEncoding.DecodeBase64(content.Span),
                "quoted-printable" => MimeEncoding.DecodeQuotedPrintable(content.Span),
                _ => content,
            };

            entity = new MimeMessagePart(ParseMessage(messageContent, options, depth + 1), parsed: true);
        }
        else
        {
            entity = new MimePart(content);
        }

        foreach (var header in headers)
        {
            entity.Headers.Add(header);
        }

        entity.DefaultContentType = defaultContentType;
        return entity;
    }

    private static MimeMultipart ParseMultipart(ReadOnlyMemory<byte> content, MimeContentType contentType, MimeReadOptions options, int depth)
    {
        var multipart = new MimeMultipart(parsed: true);
        var delimiter = System.Text.Encoding.UTF8.GetBytes("--" + contentType.Boundary);
        var partDefault = contentType.IsMimeType("multipart", "digest") ? new MimeContentType("message", "rfc822") : new MimeContentType("text", "plain");
        var span = content.Span;
        var position = 0;
        var partStart = -1;
        var closed = false;
        while (position < span.Length && !closed)
        {
            var lineStart = position;
            var lineEnd = span[position..].IndexOf((byte)'\n');
            var next = lineEnd < 0 ? span.Length : position + lineEnd + 1;
            var line = span[lineStart..(lineEnd < 0 ? span.Length : position + lineEnd)];
            position = next;
            if (!line.StartsWith(delimiter))
            {
                continue;
            }

            var rest = line[delimiter.Length..];
            var isClose = rest.StartsWith("--"u8);
            if (isClose)
            {
                rest = rest[2..];
            }

            if (!rest.TrimEnd(" \t\r"u8).IsEmpty)
            {
                continue;
            }

            // The line break before a delimiter belongs to the delimiter ([RFC 2046] 5.1.1)
            var contentEnd = TrimLineBreak(span, lineStart);
            if (partStart < 0)
            {
                multipart.Preamble = content[..contentEnd];
            }
            else
            {
                AddPart(multipart, content[partStart..Math.Max(partStart, contentEnd)], partDefault, options, depth);
            }

            partStart = next;
            closed = isClose;
        }

        if (partStart < 0)
        {
            // No delimiter: the whole content is the preamble, written back as it is
            multipart.Preamble = content;
            multipart.IsClosed = false;
        }
        else if (!closed)
        {
            AddPart(multipart, content[partStart..], partDefault, options, depth);
            multipart.IsClosed = false;
        }
        else if (position < span.Length)
        {
            multipart.Epilogue = content[position..];
        }

        return multipart;
    }

    private static void AddPart(MimeMultipart multipart, ReadOnlyMemory<byte> part, MimeContentType defaultContentType, MimeReadOptions options, int depth)
    {
        if (multipart.Parts.Count >= options.MaxParts)
        {
            throw new EmailResourceLimitException($"A multipart has more than {options.MaxParts} parts.");
        }

        var position = 0;
        var headers = ReadHeaders(part, ref position, allowMboxFromLine: false);
        multipart.Parts.Add(ParseEntity(headers, part[position..], defaultContentType, options, depth + 1));
    }

    private static int TrimLineBreak(ReadOnlySpan<byte> span, int end)
    {
        if (end > 0 && span[end - 1] == '\n')
        {
            end--;
            if (end > 0 && span[end - 1] == '\r')
            {
                end--;
            }
        }

        return end;
    }

    /// <summary>Reads the header fields until the empty line, which <paramref name="position"/> moves after. A line that is not a header field ends the headers, and starts the body.</summary>
    public static List<MimeHeader> ReadHeaders(ReadOnlyMemory<byte> data, ref int position, bool allowMboxFromLine)
    {
        var span = data.Span;
        var headers = new List<MimeHeader>();
        var headerStart = -1;
        var headerEnd = -1;
        var first = true;
        while (position < span.Length)
        {
            var lineEnd = span[position..].IndexOf((byte)'\n');
            var next = lineEnd < 0 ? span.Length : position + lineEnd + 1;
            var line = span[position..(lineEnd < 0 ? span.Length : position + lineEnd)];
            if (line.Length > 0 && line[^1] == '\r')
            {
                line = line[..^1];
            }

            if (line.IsEmpty)
            {
                AddHeader(headers, data, headerStart, headerEnd);
                position = next;
                return headers;
            }

            if (line[0] is (byte)' ' or (byte)'\t' && headerStart >= 0)
            {
                headerEnd = next;
                position = next;
                continue;
            }

            var colon = line.IndexOf((byte)':');
            if (colon > 0 && IsFieldName(line[..colon]))
            {
                AddHeader(headers, data, headerStart, headerEnd);
                headerStart = position;
                headerEnd = next;
                position = next;
                first = false;
                continue;
            }

            if (first && allowMboxFromLine && line.StartsWith("From "u8))
            {
                // The separator of an mbox file before the message
                position = next;
                first = false;
                continue;
            }

            // Not a header field: the body starts here
            break;
        }

        AddHeader(headers, data, headerStart, headerEnd);
        return headers;
    }

    private static bool IsFieldName(ReadOnlySpan<byte> name)
    {
        // Some messages have spaces between the name and the colon
        name = name.TrimEnd(" \t"u8);
        if (name.IsEmpty)
        {
            return false;
        }

        foreach (var c in name)
        {
            if (c is <= 32 or >= 127)
            {
                return false;
            }
        }

        return true;
    }

    private static void AddHeader(List<MimeHeader> headers, ReadOnlyMemory<byte> data, int start, int end)
    {
        if (start < 0)
        {
            return;
        }

        var raw = data[start..end].Span;
        var colon = raw.IndexOf((byte)':');
        var name = System.Text.Encoding.ASCII.GetString(raw[..colon].TrimEnd(" \t"u8));

        // Unfold: remove the line breaks; the whitespace that follows them stays
        var value = raw[(colon + 1)..];
        var unfolded = new byte[value.Length];
        var length = 0;
        foreach (var b in value)
        {
            if (b is not ((byte)'\r' or (byte)'\n'))
            {
                unfolded[length++] = b;
            }
        }

        var text = MimeEncoding.DecodeUtf8OrLatin1(unfolded.AsSpan(0, length)).Trim();
        headers.Add(new MimeHeader(name, text, raw.ToArray()));
    }
}
