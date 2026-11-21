using System.Buffers.Binary;
using Meziantou.Framework.Email.Internals.Cfb;
using Meziantou.Framework.Email.Internals.Msg;
using Meziantou.Framework.Email.Internals.Tnef;

namespace Meziantou.Framework.Email;

/// <summary>Detects the format of a file from its content: the signatures of the binary formats and the first lines of the text formats.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Email.Tests/EmailFormatDetectorTests.cs" ref:region="EmailFormatDetector_Usage" ref:dedent="true">
/// var format = EmailFormatDetector.Detect(path);
/// Assert.Equal(EmailFileFormat.Msg, format);
///
/// Assert.Equal(EmailFileFormat.VCard, EmailFormatDetector.Detect("BEGIN:VCARD\r\nVERSION:4.0\r\nFN:Alice\r\nEND:VCARD\r\n"u8));
/// Assert.Equal(EmailFileFormat.Eml, EmailFormatDetector.Detect("From: alice@example.com\r\nSubject: Hello\r\n\r\nHi"u8));
/// Assert.Equal(EmailFileFormat.Unknown, EmailFormatDetector.Detect("Hello world"u8));
/// </code>
/// </example>
public static class EmailFormatDetector
{
    /// <summary>The number of bytes the text formats are detected from.</summary>
    private const int HeaderSize = 64 * 1024;

    // The largest compound file read to tell an Outlook item from another compound file, such as a Word document
    private const long MaxCompoundFileSize = 1L << 30;

    // The usual header fields of an email message: a message has at least one of them
    private static readonly HashSet<string> MessageFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bcc", "Cc", "Content-Type", "Date", "Delivered-To", "From", "In-Reply-To", "Message-ID", "MIME-Version", "Received", "References", "Reply-To", "Return-Path", "Sender", "Subject", "To",
    };

    /// <summary>Detects the format of a file from its content.</summary>
    /// <param name="data">The content of the file. The first 64 KB are enough, except for an Outlook item (<c>.msg</c>): it is told from the other compound files by its streams, so it needs the whole file.</param>
    /// <returns>The format, or <see cref="EmailFileFormat.Unknown"/>.</returns>
    public static EmailFileFormat Detect(ReadOnlySpan<byte> data)
    {
        if (CompoundFile.HasSignature(data))
        {
            return DetectCompoundFile(data.ToArray());
        }

        return DetectHeader(data);
    }

    /// <summary>Detects the format of a file from its content, read from the current position of a stream.</summary>
    /// <param name="stream">The stream. When it can seek, its position is restored.</param>
    /// <returns>The format, or <see cref="EmailFileFormat.Unknown"/>.</returns>
    /// <exception cref="IOException">The stream cannot be read.</exception>
    public static EmailFileFormat Detect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var start = stream.CanSeek ? stream.Position : 0;
        try
        {
            var buffer = new byte[HeaderSize];
            var length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            var header = buffer.AsSpan(0, length);
            if (!CompoundFile.HasSignature(header))
            {
                return DetectHeader(header);
            }

            // A compound file is read whole, to look at its streams
            using var memory = new MemoryStream();
            memory.Write(header);
            var chunk = new byte[81920];
            int read;
            while ((read = stream.Read(chunk)) > 0)
            {
                if (memory.Length + read > MaxCompoundFileSize)
                {
                    return EmailFileFormat.Unknown;
                }

                memory.Write(chunk, 0, read);
            }

            return DetectCompoundFile(memory.ToArray());
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = start;
            }
        }
    }

    /// <summary>Detects the format of a file, or of a folder: a folder that has the <c>cur</c>, <c>new</c> and <c>tmp</c> subfolders is a Maildir folder.</summary>
    /// <param name="path">The path of the file or of the folder.</param>
    /// <returns>The format, or <see cref="EmailFileFormat.Unknown"/>.</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static EmailFileFormat Detect(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Directory.Exists(path))
        {
            return Directory.Exists(Path.Combine(path, "cur")) && Directory.Exists(Path.Combine(path, "new")) && Directory.Exists(Path.Combine(path, "tmp"))
                ? EmailFileFormat.Maildir
                : EmailFileFormat.Unknown;
        }

        using var stream = File.OpenRead(path);
        return Detect(stream);
    }

    private static EmailFileFormat DetectCompoundFile(byte[] data)
    {
        try
        {
            // [MS-OXMSG] 2.4: an Outlook item has a property stream at the root of the compound file
            var file = CompoundFile.Read(data, maxStreamSize: 0);
            if (file.Root.GetChild(MsgNames.Properties) is not { IsStream: true })
            {
                return EmailFileFormat.Unknown;
            }

            return file.Root.Clsid == MsgNames.TemplateClsid ? EmailFileFormat.OutlookTemplate : EmailFileFormat.Msg;
        }
        catch (EmailException)
        {
            return EmailFileFormat.Unknown;
        }
    }

    private static EmailFileFormat DetectHeader(ReadOnlySpan<byte> data)
    {
        // [MS-PST] 2.2.2.6: the magic of the header and the magic of the client
        if (data.Length >= 10 && data.StartsWith("!BDN"u8))
        {
            return data[8..10] switch
            {
                [(byte)'S', (byte)'M'] => EmailFileFormat.Pst,
                [(byte)'S', (byte)'O'] => EmailFileFormat.Ost,
                _ => EmailFileFormat.Unknown,
            };
        }

        if (TnefParser.HasSignature(data))
        {
            return EmailFileFormat.Tnef;
        }

        // [MS-OXOAB] 2.3: the version 32 of the Full Details file, then the size of its schema
        if (data.Length >= 24 && BinaryPrimitives.ReadUInt32LittleEndian(data) == 0x20 && BinaryPrimitives.ReadUInt32LittleEndian(data[12..]) is >= 12 and < 0x10000000)
        {
            return EmailFileFormat.OfflineAddressBook;
        }

        if (data.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            data = data[3..];
        }

        if (data.StartsWith("From "u8))
        {
            return EmailFileFormat.Mbox;
        }

        var text = data.TrimStart(" \t\r\n"u8);
        if (text.StartsWith("// <!-- <mdb:mork"u8))
        {
            return EmailFileFormat.Mork;
        }

        if (StartsWithIgnoreCase(text, "BEGIN:VCARD"u8))
        {
            return EmailFileFormat.VCard;
        }

        if (StartsWithIgnoreCase(text, "BEGIN:VCALENDAR"u8))
        {
            return EmailFileFormat.ICalendar;
        }

        // An Apple Mail message starts with the size of the message, on its own line
        var digits = 0;
        while (digits < data.Length && digits < 20 && char.IsAsciiDigit((char)data[digits]))
        {
            digits++;
        }

        if (digits > 0)
        {
            var end = digits;
            while (end < data.Length && data[end] is (byte)' ' or (byte)'\t' or (byte)'\r')
            {
                end++;
            }

            return end < data.Length && data[end] == '\n' && IsMessageHeader(data[(end + 1)..]) ? EmailFileFormat.Emlx : EmailFileFormat.Unknown;
        }

        return IsMessageHeader(data) ? EmailFileFormat.Eml : EmailFileFormat.Unknown;
    }

    /// <summary>Returns whether the data starts with the header fields of an email message, one of them a usual field of a message.</summary>
    private static bool IsMessageHeader(ReadOnlySpan<byte> data)
    {
        var knownField = false;
        var fields = 0;
        while (data.Length > 0 && fields < 200)
        {
            var newLine = data.IndexOf((byte)'\n');
            var line = newLine < 0 ? data : data[..newLine];
            if (line.Length > 0 && line[^1] == '\r')
            {
                line = line[..^1];
            }

            if (line.Length == 0)
            {
                break;
            }

            if (line[0] is (byte)' ' or (byte)'\t')
            {
                // A folded line continues the previous field
                if (fields == 0)
                {
                    return false;
                }
            }
            else
            {
                // RFC 5322 2.2: a field name is printable US-ASCII, without the colon, followed by a colon
                var colon = line.IndexOf((byte)':');
                if (colon <= 0 || line[..colon].ContainsAnyExceptInRange((byte)33, (byte)126))
                {
                    return false;
                }

                fields++;
                knownField |= MessageFields.Contains(System.Text.Encoding.ASCII.GetString(line[..colon]));
            }

            if (newLine < 0)
            {
                break;
            }

            data = data[(newLine + 1)..];
        }

        return knownField;
    }

    private static bool StartsWithIgnoreCase(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix)
    {
        if (data.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (char.ToUpperInvariant((char)data[i]) != prefix[i])
            {
                return false;
            }
        }

        return true;
    }
}
