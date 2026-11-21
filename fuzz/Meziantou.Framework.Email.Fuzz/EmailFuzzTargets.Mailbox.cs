using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Email.Fuzz;

/// <summary>
/// The mbox reader reads any input or throws a <see cref="EmailException"/>; the messages it reads, written in an mboxrd file
/// and read back, have the same bytes (with LF line breaks). The .emlx reader reads any input or throws a
/// <see cref="EmailException"/>, and a message it writes reads back. The Mork reader reads any database that starts with its
/// header, and a database written and read back has the same tables and rows.
/// </summary>
public static partial class EmailFuzzTargets
{
    private const string MailboxSeedPrefix = "TestData/Mailbox/";

    private static readonly MimeReadOptions MailboxMimeOptions = new() { MaxDepth = 16, MaxParts = 256, MaxSize = 1024 * 1024 };

    private static readonly string[] MailboxTokens =
    [
        "From ", ">From ", ">>From ", "\nFrom a@b Mon Jan  1 00:00:00 2024\n", "Content-Length: ", "\n\n", "\r\n",
        "<?xml version=\"1.0\"?>", "<plist version=\"1.0\">", "<dict>", "</dict>", "<key>flags</key>", "<integer>", "</integer>", "<string>", "<array>", "<true/>", "<data>", "<date>",
        "// <!-- <mdb:mork:z v=\"1.4\"/> -->\n", "< <(a=c)>", "(80=", "(^80=", "(^80^81)", "[1:^80", "{1:^80 {(k^81:c)(s=9)}", "@$${1{@", "@$$}1}@", "@$$}~~}@", "$C3$A9", "\\)", "\\\n",
    ];

    private static IReadOnlyList<FuzzTarget> MailboxTargets { get; } =
    [
        new("mbox-read", MboxRead) { Seeds = () => GetMailboxSeeds(".mbox", prefix: true), Dictionary = MailboxTokens },
        new("emlx-read", EmlxRead) { Seeds = () => GetMailboxSeeds(".emlx", prefix: false), Dictionary = MailboxTokens },
        new("mork-read", MorkRead) { Seeds = () => GetMailboxSeeds(".msf", prefix: false), Dictionary = MailboxTokens },
    ];

    private static void MboxRead(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        var format = FuzzInput.Pick(Enum.GetValues<MboxFormat>(), data[0]);
        var options = new MboxReadOptions { Format = format, MessageOptions = MailboxMimeOptions };
        List<MboxMessage> messages;
        try
        {
            messages = [.. MboxReader.ReadMessages(new MemoryStream(data[1..].ToArray()), options)];
            foreach (var message in messages)
            {
                FuzzAssert.True(message.Offset >= 0 && message.Offset < data.Length, "The offset of a message is outside the file");
                _ = message.Message.Subject;
            }
        }
        catch (EmailException)
        {
            return;
        }

        // The messages written in an mboxrd file read back with the same bytes, with LF line breaks
        using var stream = new MemoryStream();
        using (var writer = new MboxWriter(stream, MboxFormat.Mboxrd, leaveOpen: true))
        {
            foreach (var message in messages)
            {
                writer.Write(message.Content.Span, message.Sender, message.Date ?? DateTimeOffset.UnixEpoch);
            }
        }

        stream.Position = 0;
        var copies = MboxReader.ReadMessages(stream, new MboxReadOptions { MessageOptions = new MimeReadOptions() }).ToList();
        FuzzAssert.Equal(messages.Count, copies.Count, "The mbox file written has a different number of messages");
        for (var i = 0; i < messages.Count; i++)
        {
            FuzzAssert.True(copies[i].Content.Span.SequenceEqual(NormalizeMboxLines(messages[i].Content.Span)), "A message written in an mbox file reads back differently");
        }
    }

    private static void EmlxRead(ReadOnlySpan<byte> data)
    {
        EmlxMessage message;
        try
        {
            message = EmlxFile.Load(data.ToArray(), MailboxMimeOptions);
            _ = message.Flags;
            _ = message.DateReceived;
            _ = message.AttachmentCount;
        }
        catch (EmailException)
        {
            return;
        }

        using var stream = new MemoryStream();
        EmlxFile.Save(message.Message, stream, message.Flags);
        var copy = EmlxFile.Load(stream.ToArray());
        FuzzAssert.Equal(message.Flags, copy.Flags, "The flags of a message written in an .emlx file read back differently");
        FuzzAssert.Equal(message.Message.Subject, copy.Message.Subject, "The subject of a message written in an .emlx file reads back differently");
    }

    private static void MorkRead(ReadOnlySpan<byte> data)
    {
        MorkDocument document;
        try
        {
            document = MorkDocument.Load(data);
        }
        catch (EmailException)
        {
            return;
        }

        _ = ThunderbirdSummary.ReadMessages(document).Count();

        using var stream = new MemoryStream();
        document.Save(stream);
        var copy = MorkDocument.Load(stream.ToArray());
        FuzzAssert.Equal(DescribeMork(document), DescribeMork(copy), "A Mork database written and read back differs");
    }

    private static string DescribeMork(MorkDocument document)
    {
        var builder = new StringBuilder();
        foreach (var table in document.Tables)
        {
            builder.Append("table ").Append(table.Id).Append(':').Append(table.Scope).Append(' ').Append(table.Kind).Append('\n');
            foreach (var row in table.Rows)
            {
                builder.Append("  ").Append(row.Id).Append(':').Append(row.Scope).Append('\n');
            }
        }

        // The writer writes the rows of the tables first: the order of the rows of the database can change
        foreach (var row in document.Rows.OrderBy(row => row.Scope, StringComparer.Ordinal).ThenBy(row => row.Id, StringComparer.Ordinal))
        {
            builder.Append(row.Id).Append(':').Append(row.Scope);
            foreach (var (column, value) in row.Cells.OrderBy(cell => cell.Key, StringComparer.Ordinal))
            {
                builder.Append(' ').Append(column).Append('=').Append(value);
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Returns the bytes of a message as the mbox writer writes them: each line ends with LF.</summary>
    private static byte[] NormalizeMboxLines(ReadOnlySpan<byte> content)
    {
        using var result = new MemoryStream();
        var position = 0;
        while (position < content.Length)
        {
            var end = content[position..].IndexOf((byte)'\n');
            var line = end < 0 ? content[position..] : content.Slice(position, end);
            if (line is [.., (byte)'\r'])
            {
                line = line[..^1];
            }

            result.Write(line);
            result.WriteByte((byte)'\n');
            position = end < 0 ? content.Length : position + end + 1;
        }

        return result.ToArray();
    }

    private static IEnumerable<FuzzSeed> GetMailboxSeeds(string extension, bool prefix)
    {
        var assembly = typeof(EmailFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(MailboxSeedPrefix, StringComparison.Ordinal) && name.EndsWith(extension, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var memory = new MemoryStream();
            if (prefix)
            {
                // The format of mbox-read, from the name of the file
                var format = Enum.GetValues<MboxFormat>().LastOrDefault(value => name.Contains(value.ToString().ToLowerInvariant(), StringComparison.Ordinal));
                memory.WriteByte((byte)format);
            }

            stream.CopyTo(memory);
            yield return new FuzzSeed(name[MailboxSeedPrefix.Length..], memory.ToArray());
        }
    }
}
