using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mail;

/// <summary>Writes messages in an mbox file. The lines end with LF, as in the mbox files of Unix mail tools.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Mail.Tests/MboxTests.cs" ref:region="MboxWriter_Write" ref:dedent="true">
/// using (var writer = MboxWriter.Create(path, MboxFormat.Mboxrd))
/// {
///     var message = new MimeMessage { Subject = "Hello", From = [new MailboxAddress("Alice", "alice@example.com")] };
///     var body = new MimePart();
///     body.SetText("From the start of a line, escaped as &gt;From in the file.");
///     message.Body = body;
///     writer.Write(message, date: new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));
/// }
///
/// var entry = MboxReader.ReadMessages(path).Single();
/// Assert.Equal("From alice@example.com Thu Jan 15 09:00:00 2026", entry.FromLine);
/// Assert.StartsWith("From the start of a line", entry.Message.TextBody, StringComparison.Ordinal);
/// </code>
/// </example>
public sealed class MboxWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private bool _disposed;

    /// <summary>Creates a writer that writes at the current position of a stream, such as the end of an existing file.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="format">The variant of the format.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave the stream open when the writer is disposed.</param>
    public MboxWriter(Stream stream, MboxFormat format = MboxFormat.Mboxrd, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _leaveOpen = leaveOpen;
        Format = format;
    }

    /// <summary>Gets the variant of the format.</summary>
    public MboxFormat Format { get; }

    /// <summary>Creates a file, or appends to an existing one.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="format">The variant of the format.</param>
    /// <param name="append"><see langword="true"/> to add the messages at the end of an existing file.</param>
    /// <returns>The writer.</returns>
    public static MboxWriter Create(string path, MboxFormat format = MboxFormat.Mboxrd, bool append = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new MboxWriter(new FileStream(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write), format);
    }

    /// <summary>Writes a message: its <c>From </c> line, its lines escaped as the format requires, then a blank line.</summary>
    /// <param name="message">The message.</param>
    /// <param name="sender">The address of the envelope sender, or <see langword="null"/> for the address of the first <c>From</c> address of the message.</param>
    /// <param name="date">The date of the <c>From </c> line, or <see langword="null"/> for the date of the message, or the current date.</param>
    public void Write(MimeMessage message, string? sender = null, DateTimeOffset? date = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(_disposed, this);
        WriteContent(message.ToByteArray(), sender ?? (message.From.Count > 0 ? message.From[0].Address : null), date ?? message.Date ?? DateTimeOffset.UtcNow);
    }

    /// <summary>Writes the bytes of a message.</summary>
    /// <param name="content">The bytes of the message, without a <c>From </c> line.</param>
    /// <param name="sender">The address of the envelope sender.</param>
    /// <param name="date">The date of the <c>From </c> line.</param>
    public void Write(ReadOnlySpan<byte> content, string? sender, DateTimeOffset date)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WriteContent(content, sender, date);
    }

    /// <summary>Flushes the stream.</summary>
    public void Flush() => _stream.Flush();

    /// <summary>Flushes the stream, and closes it unless the writer leaves it open.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Flush();
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }

    private void WriteContent(ReadOnlySpan<byte> content, string? sender, DateTimeOffset date)
    {
        using var output = new MemoryStream(content.Length + 128);
        output.Write(Encoding.Latin1.GetBytes(MboxFromLine.Format(MboxFromLine.CleanSender(sender), date)));
        output.WriteByte((byte)'\n');

        // The headers, without a Content-Length header in the formats that add one
        var lines = SplitLines(content);
        var index = 0;
        var useContentLength = Format is MboxFormat.Mboxcl or MboxFormat.Mboxcl2;
        var hasBody = false;
        for (; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.IsEmpty)
            {
                index++;
                hasBody = true;
                break;
            }

            if (useContentLength && IsContentLength(line))
            {
                // A folded continuation of the header is removed with it
                while (index + 1 < lines.Count && lines[index + 1] is { IsEmpty: false } next && (next.Span[0] == ' ' || next.Span[0] == '\t'))
                {
                    index++;
                }

                continue;
            }

            // A line that is not a header (a malformed message) can start with "From " too
            WriteLine(output, line.Span, escape: true);
        }

        using var body = new MemoryStream();
        for (; index < lines.Count; index++)
        {
            WriteLine(body, lines[index].Span, escape: true);
        }

        if (useContentLength)
        {
            output.Write(Encoding.ASCII.GetBytes("Content-Length: " + body.Length.ToString(CultureInfo.InvariantCulture) + "\n"));
        }

        // The blank line between the headers and the body, which a message without a body may not have
        if (hasBody || useContentLength)
        {
            output.WriteByte((byte)'\n');
        }

        body.Position = 0;
        body.CopyTo(output);

        // A blank line separates the message from the next one
        output.WriteByte((byte)'\n');
        output.Position = 0;
        output.CopyTo(_stream);
    }

    private void WriteLine(MemoryStream output, ReadOnlySpan<byte> line, bool escape)
    {
        if (escape && NeedsEscape(line))
        {
            output.WriteByte((byte)'>');
        }

        output.Write(line);
        output.WriteByte((byte)'\n');
    }

    private bool NeedsEscape(ReadOnlySpan<byte> line) => Format switch
    {
        MboxFormat.Mboxrd => line.TrimStart((byte)'>').StartsWith("From "u8),
        MboxFormat.Mboxo or MboxFormat.Mboxcl => line.StartsWith("From "u8),
        _ => false,
    };

    private static bool IsContentLength(ReadOnlyMemory<byte> line)
    {
        var colon = line.Span.IndexOf((byte)':');
        return colon > 0 && Encoding.ASCII.GetString(line.Span[..colon]).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Splits the content in lines, without their line breaks (LF or CRLF). A last line without a line break is a line.</summary>
    private static List<ReadOnlyMemory<byte>> SplitLines(ReadOnlySpan<byte> content)
    {
        var data = content.ToArray().AsMemory();
        var result = new List<ReadOnlyMemory<byte>>();
        var position = 0;
        while (position < data.Length)
        {
            var end = data.Span[position..].IndexOf((byte)'\n');
            var next = end < 0 ? data.Length : position + end + 1;
            var line = data[position..(end < 0 ? data.Length : position + end)];
            if (line.Span is [.., (byte)'\r'])
            {
                line = line[..^1];
            }

            result.Add(line);
            position = next;
        }

        return result;
    }
}
