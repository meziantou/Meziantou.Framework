using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Email.Fuzz;

/// <summary>
/// The converter converts any mbox file: through a Maildir folder and back, the messages are the same, with the LF line breaks
/// the mbox writer writes; to an
/// Outlook data file, every message is written or reported in the errors, and the file is valid.
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

            var expected = MboxReader.ReadMessages(source).Select(message => ToMboxLines(message.Content.Span)).Order(StringComparer.Ordinal).ToList();

            var maildir = Path.Combine(directory, "maildir");
            var result = MailboxConverter.Convert(source, maildir, MailboxFormat.Maildir);
            FuzzAssert.Equal(0, result.Errors.Count, "The conversion to a Maildir folder has errors: " + string.Join("; ", result.Errors));
            FuzzAssert.Equal(expected.Count, result.MessageCount, "The Maildir folder does not have every message");

            var mbox = Path.Combine(directory, "mbox");
            MailboxConverter.Convert(maildir, mbox, MailboxFormat.Mbox);
            var actual = MboxReader.ReadMessages(Path.Combine(mbox, "source")).Select(message => ToMboxLines(message.Content.Span)).Order(StringComparer.Ordinal).ToList();
            FuzzAssert.Equal(string.Join("\n", expected), string.Join("\n", actual), "The messages differ after a conversion to a Maildir folder and back");

            var pst = Path.Combine(directory, "mailbox.pst");
            var pstResult = MailboxConverter.Convert(source, pst, MailboxFormat.Pst);
            FuzzAssert.Equal(expected.Count, pstResult.MessageCount + pstResult.Errors.Count, "The Outlook data file does not have every message");
            using var file = PstFile.Open(pst);
            var issues = file.Validate();
            FuzzAssert.True(issues.Count == 0, "The Outlook data file is not valid: " + string.Join("; ", issues));
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
