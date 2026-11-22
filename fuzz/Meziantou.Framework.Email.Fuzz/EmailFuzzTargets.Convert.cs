using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Email.Fuzz;

/// <summary>
/// The converter converts any mbox file: through a Maildir folder and back, the messages are the same, with the LF line breaks
/// the mbox writer writes; to an
/// Outlook data file, every message is written or reported in the errors, and the file is valid; to a directory of .eml
/// files, every message is copied as it is to its own file; to a directory of .msg files, every file is a valid item.
/// </summary>
public static partial class EmailFuzzTargets
{
    // Computed: the tokens are a field of another file, which can be initialized after the fields of this one
    private static IReadOnlyList<FuzzTarget> ConvertTargets =>
    [
        new("mailbox-convert", MailboxConvert) { Seeds = GetMailboxConvertSeeds, Dictionary = MailboxTokens },
    ];

    private static void MailboxConvert(ReadOnlySpan<byte> data)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mailbox-convert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source");
            File.WriteAllBytes(source, data.ToArray());
            if (MailboxConverter.DetectFormat(source) != MailboxFormat.Mbox)
            {
                return;
            }

            // A message read, without a Status header (Thunderbird), gets one in an mbox file
            var messages = MboxReader.ReadMessages(source).Select(message => message.Content.ToArray()).ToList();
            var expected = messages.Select(content => ToMboxLines(MailboxConverter.GetMboxReadState(content) is true && MimeMessage.Load(content).Headers.Find("Status") is null ? [.. "Status: RO\n"u8, .. content] : content)).Order(StringComparer.Ordinal).ToList();

            var maildir = Path.Combine(directory, "maildir");
            var result = MailboxConverter.Convert(source, maildir, MailboxFormat.Maildir);
            FuzzAssert.Equal(0, result.Errors.Count, "The conversion to a Maildir folder has errors: " + string.Join("; ", result.Errors));
            FuzzAssert.Equal(expected.Count, result.MessageCount, "The Maildir folder does not have every message");
            var seen = MaildirFolder.Open(maildir).EnumerateFolders().SelectMany(folder => folder.Folder.EnumerateMessages()).Count(message => message.Flags.HasFlag(MaildirFlags.Seen));
            FuzzAssert.Equal(messages.Count(content => MailboxConverter.GetMboxReadState(content) is true), seen, "The Maildir folder does not have the read state of the messages");

            var mbox = Path.Combine(directory, "mbox");
            MailboxConverter.Convert(maildir, mbox, MailboxFormat.Mbox);
            var actual = MboxReader.ReadMessages(Path.Combine(mbox, "source")).Select(message => ToMboxLines(message.Content.Span)).Order(StringComparer.Ordinal).ToList();
            FuzzAssert.Equal(string.Join("\n", expected), string.Join("\n", actual), "The messages differ after a conversion to a Maildir folder and back");

            var pst = Path.Combine(directory, "mailbox.pst");
            var pstResult = MailboxConverter.Convert(source, pst, MailboxFormat.Pst);
            FuzzAssert.Equal(expected.Count, pstResult.MessageCount + pstResult.Errors.Count, "The Outlook data file does not have every message");
            using (var file = PstFile.Open(pst))
            {
                var issues = file.Validate();
                FuzzAssert.True(issues.Count == 0, "The Outlook data file is not valid: " + string.Join("; ", issues));
            }

            // A directory of .eml files has the messages as they are, one per file
            var eml = Path.Combine(directory, "eml");
            var emlResult = MailboxConverter.Convert(source, eml, MailboxFormat.Eml);
            FuzzAssert.Equal(messages.Count, emlResult.MessageCount + emlResult.Errors.Count, "The directory of .eml files does not have every message");
            var files = Directory.EnumerateFiles(eml, "*", SearchOption.AllDirectories).ToList();
            FuzzAssert.Equal(emlResult.MessageCount, files.Count, "The directory of .eml files does not have a file per message");
            var sourceContents = messages.Select(Convert.ToHexString).ToList();
            foreach (var path in files)
            {
                FuzzAssert.True(sourceContents.Remove(Convert.ToHexString(File.ReadAllBytes(path))), "An .eml file is not one of the messages of the mbox file");
            }

            // A directory of .msg files has a valid Outlook item per message
            var msg = Path.Combine(directory, "msg");
            var msgResult = MailboxConverter.Convert(source, msg, MailboxFormat.Msg);
            FuzzAssert.Equal(messages.Count, msgResult.MessageCount + msgResult.Errors.Count, "The directory of .msg files does not have every message");
            foreach (var path in Directory.EnumerateFiles(msg, "*", SearchOption.AllDirectories))
            {
                MsgFile.Load(path);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Returns the lines of a message as the mbox writer writes them: the line breaks are LF, and the last line ends with one.</summary>
    private static string ToMboxLines(ReadOnlySpan<byte> data)
    {
        // The last line ends with a line break, then its carriage returns are removed with the others
        ReadOnlySpan<byte> content = data.IsEmpty || data[^1] == '\n' ? data : [.. data, (byte)'\n'];
        var result = new List<byte>(content.Length);
        for (var i = 0; i < content.Length; i++)
        {
            // The carriage returns at the end of a line are line breaks (CRLF, or CR CR LF), which mbox writes as LF
            var next = i;
            while (next < content.Length && content[next] == '\r')
            {
                next++;
            }

            if (next > i && next < content.Length && content[next] == '\n')
            {
                i = next - 1;
                continue;
            }

            result.Add(content[i]);
        }

        return Convert.ToHexString([.. result]);
    }

    private static IEnumerable<FuzzSeed> GetMailboxConvertSeeds()
    {
        var assembly = typeof(EmailFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(MailboxSeedPrefix, StringComparison.Ordinal) && name.EndsWith(".mbox", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return new FuzzSeed(name[MailboxSeedPrefix.Length..], memory.ToArray());
        }
    }
}
