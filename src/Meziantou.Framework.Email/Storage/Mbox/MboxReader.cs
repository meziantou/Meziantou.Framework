using System.Globalization;
using System.Text;
using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email.Storage;

/// <summary>Reads the messages of an mbox file, the format of the mailboxes of Unix mail tools and of Thunderbird: messages one after the other, each one after a line that starts with <c>From </c>. The messages are read one at a time.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../../tests/Meziantou.Framework.Email.Tests/MboxTests.cs" ref:region="MboxReader_ReadMessages" ref:dedent="true">
/// foreach (var entry in MboxReader.ReadMessages(path, new MboxReadOptions { Format = MboxFormat.Mboxrd }))
/// {
///     // The envelope sender and the date of the "From " line, then the message, read when it is accessed
///     var sender = entry.Sender;
///     var subject = entry.Message.Subject;
///     Assert.NotNull(sender);
///     Assert.NotNull(subject);
/// }
/// </code>
/// </example>
public static class MboxReader
{
    private static ReadOnlySpan<byte> FromPrefix => "From "u8;

    /// <summary>Reads the messages of an mbox file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default options.</param>
    /// <returns>The messages, read as they are enumerated.</returns>
    /// <exception cref="InvalidEmailDataException">The file does not start with a <c>From </c> line, or a <c>Content-Length</c> header does not end a message.</exception>
    /// <exception cref="EmailResourceLimitException">A message is larger than <see cref="MimeReadOptions.MaxSize"/>.</exception>
    public static IEnumerable<MboxMessage> ReadMessages(string path, MboxReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        return ReadMessagesFromFile(path, options);
    }

    /// <summary>Reads the messages of an mbox stream, until its end.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default options.</param>
    /// <returns>The messages, read as they are enumerated.</returns>
    /// <exception cref="InvalidEmailDataException">The stream does not start with a <c>From </c> line, or a <c>Content-Length</c> header does not end a message.</exception>
    /// <exception cref="EmailResourceLimitException">A message is larger than <see cref="MimeReadOptions.MaxSize"/>.</exception>
    public static IEnumerable<MboxMessage> ReadMessages(Stream stream, MboxReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Read(stream, options ?? MboxReadOptions.Default);
    }

    private static IEnumerable<MboxMessage> ReadMessagesFromFile(string path, MboxReadOptions? options)
    {
        using var stream = File.OpenRead(path);
        foreach (var message in Read(stream, options ?? MboxReadOptions.Default))
        {
            yield return message;
        }
    }

    private static IEnumerable<MboxMessage> Read(Stream stream, MboxReadOptions options)
    {
        var reader = new LineReader(stream);
        var maxSize = options.MessageOptions.MaxSize;

        // The file starts with the From line of its first message; an empty file has no message
        ReadOnlySpan<byte> line;
        string? fromLine;
        long offset;
        while (true)
        {
            offset = reader.Position;
            if (!reader.ReadLine(out line, maxSize))
            {
                yield break;
            }

            if (line.StartsWith(FromPrefix))
            {
                fromLine = DecodeFromLine(line);
                break;
            }

            if (!IsBlank(line))
            {
                throw new InvalidEmailDataException("The content is not an mbox file: it does not start with a 'From ' line.");
            }
        }

        while (fromLine is not null)
        {
            using var content = new MemoryStream();
            string? next = null;
            long nextOffset = 0;
            var useContentLength = options.Format is MboxFormat.Mboxcl or MboxFormat.Mboxcl2;
            long? contentLength = null;
            var inHeaders = true;
            while (true)
            {
                if (inHeaders && useContentLength && contentLength is not null)
                {
                    // The headers are read: the body is the next Content-Length bytes
                    if (content.Length + contentLength.Value > maxSize)
                    {
                        throw new EmailResourceLimitException($"A message is larger than the limit of {maxSize} bytes.");
                    }

                    var bodyStart = content.Length;
                    if (reader.CopyTo(content, contentLength.Value) < contentLength.Value)
                    {
                        throw new InvalidEmailDataException("The Content-Length header of a message is larger than the rest of the file.");
                    }

                    if (options.Format == MboxFormat.Mboxcl)
                    {
                        UnescapeBody(content, bodyStart, MboxFormat.Mboxo);
                    }

                    // The separator: blank lines, then the From line of the next message or the end of the file
                    while (true)
                    {
                        nextOffset = reader.Position;
                        if (!reader.ReadLine(out line, maxSize))
                        {
                            break;
                        }

                        if (line.StartsWith(FromPrefix))
                        {
                            next = DecodeFromLine(line);
                            break;
                        }

                        if (!IsBlank(line))
                        {
                            throw new InvalidEmailDataException("The Content-Length header of a message does not end the message before a 'From ' line.");
                        }
                    }

                    break;
                }

                var lineOffset = reader.Position;
                if (!reader.ReadLine(out line, maxSize))
                {
                    break;
                }

                if (line.StartsWith(FromPrefix) && !(inHeaders && useContentLength && content.Length == 0))
                {
                    next = DecodeFromLine(line);
                    nextOffset = lineOffset;
                    break;
                }

                if (inHeaders && IsBlank(line))
                {
                    inHeaders = false;
                    content.Write(line);
                    if (useContentLength)
                    {
                        contentLength = FindContentLength(content);
                        inHeaders = contentLength is not null;
                    }

                    continue;
                }

                WriteUnescaped(content, line, options.Format);

                if (content.Length > maxSize)
                {
                    throw new EmailResourceLimitException($"A message is larger than the limit of {maxSize} bytes.");
                }
            }

            // The blank line before the next From line separates the messages
            var bytes = content.ToArray();
            var length = bytes.Length;
            if (next is not null || options.Format is MboxFormat.Mboxcl or MboxFormat.Mboxcl2 || length > 0)
            {
                length = RemoveSeparator(bytes, contentLength is null);
            }

            yield return new MboxMessage(fromLine, offset, bytes.AsMemory(0, length), options.MessageOptions);
            fromLine = next;
            offset = nextOffset;
        }
    }

    private static int RemoveSeparator(byte[] bytes, bool hasSeparator)
    {
        var length = bytes.Length;
        if (!hasSeparator)
        {
            return length;
        }

        // An empty message is only the separator
        if (bytes is [(byte)'\n'] or [(byte)'\r', (byte)'\n'])
        {
            return 0;
        }

        if (length >= 2 && bytes[length - 1] == '\n' && bytes[length - 2] == '\n')
        {
            return length - 1;
        }

        if (length >= 4 && bytes.AsSpan(length - 4).SequenceEqual("\r\n\r\n"u8))
        {
            return length - 2;
        }

        return length;
    }

    private static long? FindContentLength(MemoryStream headers)
    {
        var text = Encoding.Latin1.GetString(headers.GetBuffer(), 0, (int)headers.Length);
        foreach (var line in text.Split('\n'))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && line.AsSpan(0, colon).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(line.AsSpan(colon + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static void WriteUnescaped(MemoryStream content, ReadOnlySpan<byte> line, MboxFormat format)
    {
        content.Write(IsEscaped(line, format) ? line[1..] : line);
    }

    /// <summary>Removes the escaping of the lines of a body already in the stream.</summary>
    private static void UnescapeBody(MemoryStream content, long start, MboxFormat format)
    {
        var body = content.GetBuffer().AsSpan((int)start, (int)(content.Length - start)).ToArray();
        content.SetLength(start);
        content.Position = start;
        var position = 0;
        while (position < body.Length)
        {
            var end = body.AsSpan(position).IndexOf((byte)'\n');
            var next = end < 0 ? body.Length : position + end + 1;
            WriteUnescaped(content, body.AsSpan(position, next - position), format);
            position = next;
        }
    }

    /// <summary>Gets whether a line is a line of the message that starts with <c>From </c>, escaped: <c>&gt;From </c> in mboxo, any number of <c>&gt;</c> before <c>From </c> in mboxrd.</summary>
    internal static bool IsEscaped(ReadOnlySpan<byte> line, MboxFormat format)
    {
        if (format is MboxFormat.Mboxcl2 || line.Length == 0 || line[0] != '>')
        {
            return false;
        }

        var rest = format == MboxFormat.Mboxrd ? line.TrimStart((byte)'>') : line[1..];
        return rest.StartsWith(FromPrefix);
    }

    private static bool IsBlank(ReadOnlySpan<byte> line) => line.TrimEnd("\r\n"u8).IsEmpty;

    private static string DecodeFromLine(ReadOnlySpan<byte> line) => Encoding.Latin1.GetString(line.TrimEnd("\r\n"u8));
}
