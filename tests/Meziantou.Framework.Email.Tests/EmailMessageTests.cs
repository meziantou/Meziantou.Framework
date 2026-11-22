namespace Meziantou.Framework.Email.Tests;

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
                EmailMessage.FromMapi(item).ToMime().WriteTo(emlPath);
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
    [MemberData(nameof(MimeMessageTests.Files), MemberType = typeof(MimeMessageTests))]
    public void Mime_RoundTrip(string fileName)
    {
        var email = EmailMessage.FromMime(MimeMessage.Load(File.ReadAllBytes(MimeMessageTests.GetPath(fileName))));
        var copy = EmailMessage.FromMime(MimeMessage.Load(email.ToMime().ToByteArray()));
        Assert.Equal(Describe(email), Describe(copy));
    }

    [Theory]
    [MemberData(nameof(MimeMessageTests.Files), MemberType = typeof(MimeMessageTests))]
    public void Pst_RoundTrip(string fileName)
    {
        // An .eml file, written to a PST file and read back
        var email = EmailMessage.FromMime(MimeMessage.Load(File.ReadAllBytes(MimeMessageTests.GetPath(fileName))));
        using var stream = new MemoryStream();
        using (var writer = PstWriter.Create(stream, leaveOpen: true))
        {
            writer.RootFolder.CreateFolder("Inbox").AddItem(email.ToMapiBuilder());
        }

        using var file = PstFile.Open(stream.ToArray());
        var copy = EmailMessage.FromMapi(file.GetFolder("Inbox")!.EnumerateItems().Single());
        Assert.Equal(Describe(email, rtf: false), Describe(copy, rtf: false));
    }

    [Fact]
    public void SentOnBehalf_ReplyTo()
    {
        // The author (From), the user who sent the message for them (Sender) and the reply address are kept in Outlook items
        var email = new EmailMessage
        {
            Subject = "On behalf",
            From = new MailboxAddress("Manager", "manager@example.com"),
            Sender = new MailboxAddress("Assistant", "assistant@example.com"),
            TextBody = "Text",
        };
        email.ReplyTo.Add(new MailboxAddress("Team", "team@example.com"));
        email.ReplyTo.Add(new MailboxAddress(null, "archive@example.com"));

        var item = MsgFile.Load(MsgFile.ToByteArray(email.ToMapiBuilder()));
        Assert.Equal("assistant@example.com", item.SenderEmailAddress);
        Assert.Equal("manager@example.com", item.SentRepresentingSmtpAddress);
        Assert.Equal(["Team <team@example.com>", "archive@example.com <archive@example.com>"], item.ReplyRecipients.Select(recipient => recipient.ToString()));
        Assert.Equal("Team; archive@example.com", item.Properties.GetValueOrDefault<string>(0x0050));

        var copy = EmailMessage.FromMapi(item);
        Assert.Equal(email.From, copy.From);
        Assert.Equal(email.Sender, copy.Sender);
        Assert.Equal(email.ReplyTo, copy.ReplyTo);
        var mime = MimeMessage.Load(copy.ToMime().ToByteArray());
        Assert.Equal("assistant@example.com", mime.Sender?.Address);
        Assert.Equal(["team@example.com", "archive@example.com"], mime.ReplyTo.Select(mailbox => mailbox.Address));

        // Without a Sender, the author is the sender
        var direct = EmailMessage.FromMapi(MsgFile.Load(MsgFile.ToByteArray(new EmailMessage { Subject = "Direct", From = email.From }.ToMapiBuilder())));
        Assert.Equal(email.From, direct.From);
        Assert.Null(direct.Sender);
    }

    internal static List<string> Describe(EmailMessage email, bool rtf = true)
    {
        var result = new List<string>
        {
            $"{email.Subject}|{email.From}|{email.Date?.UtcDateTime:O}|{email.MessageId}|{email.InReplyTo}|{string.Join(' ', email.References)}|{email.Importance}",
            "sender: " + email.Sender,
            "reply-to: " + string.Join(", ", email.ReplyTo),
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
