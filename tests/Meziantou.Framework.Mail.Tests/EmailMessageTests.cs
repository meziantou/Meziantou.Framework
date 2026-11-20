namespace Meziantou.Framework.Mail.Tests;

public sealed class EmailMessageTests
{
    [Fact]
    public void Convert_Snippet()
    {
        var pstPath = PstOracleTests.GetPath("libyal-outlook.pst");
        var emlPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".eml");
        try
        {
            #region EmailMessage_Convert
            // An item of an Outlook data file, saved as an .eml file
            using (var file = PstFile.Open(pstPath))
            {
                var item = file.GetFolder("Inbox")!.EnumerateItems().First();
                EmailMessage.FromPst(item).ToMime().WriteTo(emlPath);
            }

            // The .eml file, read back
            var email = EmailMessage.FromMime(MimeMessage.Load(emlPath));
            Assert.Equal("Welcome to Microsoft Office Outlook 2003", email.Subject);
            Assert.HasCount(11, email.Attachments);
            #endregion
        }
        finally
        {
            File.Delete(emlPath);
        }
    }

    [Theory]
    [MemberData(nameof(MimeOracleTests.Files), MemberType = typeof(MimeOracleTests))]
    public void Mime_RoundTrip(string fileName)
    {
        var email = EmailMessage.FromMime(MimeMessage.Load(File.ReadAllBytes(MimeOracleTests.GetPath(fileName))));
        var copy = EmailMessage.FromMime(MimeMessage.Load(email.ToMime().ToByteArray()));
        Assert.Equal(Describe(email), Describe(copy));
    }

    [Theory]
    [MemberData(nameof(MimeOracleTests.Files), MemberType = typeof(MimeOracleTests))]
    public void Pst_RoundTrip(string fileName)
    {
        // An .eml file, written to a PST file and read back
        var email = EmailMessage.FromMime(MimeMessage.Load(File.ReadAllBytes(MimeOracleTests.GetPath(fileName))));
        using var stream = new MemoryStream();
        using (var writer = PstWriter.Create(stream, leaveOpen: true))
        {
            writer.RootFolder.CreateFolder("Inbox").AddItem(email.ToPstBuilder());
        }

        using var file = PstFile.Open(stream.ToArray());
        var copy = EmailMessage.FromPst(file.GetFolder("Inbox")!.EnumerateItems().Single());
        Assert.Equal(Describe(email, rtf: false), Describe(copy, rtf: false));
    }

    [Theory]
    [MemberData(nameof(PstOracleTests.Files), MemberType = typeof(PstOracleTests))]
    public void PstItems_ToMime_ReadByMimeKit(string fileName)
    {
        // The items of the PST samples, written as MIME messages, read the same with the library and with MimeKit
        using var file = PstFile.Open(PstOracleTests.GetPath(fileName));
        foreach (var item in Enumerate(file.RootFolder).Take(50))
        {
            var bytes = EmailMessage.FromPst(item).ToMime().ToByteArray();
            Assert.Equal(MimeOracleTests.DescribeWithMimeKit(MimeKit.MimeMessage.Load(new MemoryStream(bytes))), MimeOracleTests.Describe(MimeMessage.Load(bytes)));
        }
    }

    private static IEnumerable<PstMessage> Enumerate(PstFolder folder)
    {
        foreach (var item in folder.EnumerateItems())
        {
            yield return item;
        }

        foreach (var child in folder.Folders)
        {
            foreach (var item in Enumerate(child))
            {
                yield return item;
            }
        }
    }

    internal static List<string> Describe(EmailMessage email, bool rtf = true)
    {
        var result = new List<string>
        {
            $"{email.Subject}|{email.From}|{email.Date?.UtcDateTime:O}|{email.MessageId}|{email.InReplyTo}|{string.Join(' ', email.References)}|{email.Importance}",
            "to: " + string.Join(", ", email.To),
            "cc: " + string.Join(", ", email.Cc),
            "bcc: " + string.Join(", ", email.Bcc),
            "categories: " + string.Join(", ", email.Categories),
            "text: " + email.TextBody?.ReplaceLineEndings("\n").TrimEnd(),
            "html: " + email.HtmlBody?.ReplaceLineEndings("\n").TrimEnd(),
        };

        if (rtf)
        {
            result.Add("rtf: " + email.RtfBody);
        }

        foreach (var attachment in email.Attachments)
        {
            result.Add($"attachment: {attachment.FileName}|{attachment.ContentType}|{attachment.ContentId}|{attachment.IsInline}|{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(attachment.Content.Span))}");
            if (attachment.EmbeddedMessage is { } embedded)
            {
                result.AddRange(Describe(embedded, rtf).Select(line => "  " + line));
            }
        }

        return result;
    }
}
