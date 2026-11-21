using System.Security.Cryptography;
using System.Text;

namespace Meziantou.Framework.Email.Tests;

/// <summary>Reads the TNEF streams of TestData/Tnef and the streams the writer writes, and compares them with what MimeKit, an independent reader, reads.</summary>
public sealed class TnefFileTests
{
    static TnefFileTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static TheoryData<string> Files => [.. Directory.EnumerateFiles(GetPath(""), "*.dat").Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    internal static string GetPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "Tnef", fileName);

    [Theory]
    [MemberData(nameof(Files))]
    public void File_MatchesMimeKit(string fileName)
    {
        var bytes = File.ReadAllBytes(GetPath(fileName));
        Assert.Equal(DescribeWithMimeKit(bytes), Describe(TnefFile.Load(bytes)));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Rewritten_MatchesMimeKit(string fileName)
    {
        var item = TnefFile.Load(File.ReadAllBytes(GetPath(fileName)));
        var bytes = TnefFile.ToByteArray(MapiMessageBuilder.FromMessage(item));
        AssertCompliant(bytes);
        Assert.Equal(Describe(item), Describe(TnefFile.Load(bytes)));
        Assert.Equal(DescribeWithMimeKit(bytes), Describe(TnefFile.Load(bytes)));
    }

    [Theory]
    [MemberData(nameof(MsgFileTests.Files), MemberType = typeof(MsgFileTests))]
    public void MsgItem_ToTnef(string fileName)
    {
        // The items of .msg files, with their recipients, attachments, embedded messages and named properties, written as TNEF streams
        var item = MsgFile.Load(MsgFileTests.GetPath(fileName));
        var bytes = TnefFile.ToByteArray(MapiMessageBuilder.FromMessage(item));
        AssertCompliant(bytes);
        var copy = TnefFile.Load(bytes);
        Assert.Equal(item.GetType(), copy.GetType());
        Assert.Equal(MsgFileTests.Describe(item), MsgFileTests.Describe(copy));
        Assert.Equal(item.Categories, copy.Categories);
        Assert.Equal(DescribeWithMimeKit(bytes), Describe(copy));
        Assert.Equal(item.Properties.Count(property => property.NamedProperty is not null), copy.Properties.Count(property => property.NamedProperty is not null));
    }

    [Fact]
    public void Load_Snippet()
    {
        var path = GetPath("poi-quick-winmail.dat");
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            #region TnefFile_Load
            var message = TnefFile.Load(path);

            // The class of the old versions of Microsoft Mail (IPM.Microsoft Mail.Note) is read as its MAPI class
            Assert.Equal("IPM.Note", message.MessageClass);
            foreach (var attachment in message.Attachments)
            {
                File.WriteAllBytes(Path.Combine(directory.FullName, Path.GetFileName(attachment.Name ?? "attachment")), attachment.GetContent());
            }
            #endregion

            Assert.Equal(["quick.doc", "quick.html", "quick.pdf", "quick.txt", "quick.xml"], directory.EnumerateFiles().Select(file => file.Name).Order(StringComparer.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Save_Snippet()
    {
        #region TnefFile_Save
        var message = new MapiMessageBuilder
        {
            Subject = "Meeting notes",
            Body = "The notes are attached.",
            SenderName = "Alice",
            SenderEmailAddress = "alice@example.com",
            SenderAddressType = "SMTP",
        };
        message.Recipients.Add(new MapiRecipientBuilder("Bob", "bob@example.com"));
        message.Attachments.Add(MapiAttachmentBuilder.FromContent("notes.txt", "Notes"u8.ToArray(), "text/plain"));
        var winmail = TnefFile.ToByteArray(message);

        var item = TnefFile.Load(winmail);
        Assert.Equal("Meeting notes", item.Subject);
        Assert.Equal("notes.txt", item.Attachments.Single().Name);
        #endregion

        AssertCompliant(winmail);
        Assert.Equal(DescribeWithMimeKit(winmail), Describe(item));
    }

    [Fact]
    public void MimeMessage_WithWinmailDat()
    {
        var mime = new MimeMessage { Subject = "Forwarded by Exchange" };
        var body = new MimeMultipart("mixed");
        var text = new MimePart();
        text.SetText("See the attachments.");
        body.Parts.Add(text);
        var winmail = new MimePart
        {
            ContentType = new MimeContentType("application", "ms-tnef"),
            ContentDisposition = new MimeContentDisposition(MimeContentDisposition.Attachment) { FileName = "winmail.dat" },
        };
        winmail.SetContent(File.ReadAllBytes(GetPath("poi-quick-winmail.dat")));
        body.Parts.Add(winmail);
        mime.Body = body;

        #region EmailMessage_Tnef
        // The winmail.dat attachment is replaced by the attachments of the Outlook item it contains
        var email = EmailMessage.FromMime(MimeMessage.Load(mime.ToByteArray()));
        Assert.Equal(["quick.doc", "quick.html", "quick.pdf", "quick.txt", "quick.xml"], email.Attachments.Select(attachment => attachment.FileName).Order(StringComparer.Ordinal));
        #endregion

        Assert.Equal("See the attachments.", email.TextBody?.TrimEnd());

        // A malformed stream is kept as an attachment
        winmail.SetContent("not a TNEF stream"u8.ToArray());
        Assert.Equal("winmail.dat", EmailMessage.FromMime(mime).Attachments.Single().FileName);
    }

    [Fact]
    public void EmbeddedMessages_RoundTrip()
    {
        var inner = new MapiMessageBuilder { Subject = "Inner", Body = "The forwarded message" };
        inner.Attachments.Add(MapiAttachmentBuilder.FromContent("inner.txt", "inner"u8.ToArray()));
        var outer = new MapiMessageBuilder { Subject = "Outer", Body = "Body", Categories = ["Red", "Blue"] };
        outer.Attachments.Add(MapiAttachmentBuilder.FromMessage(inner));
        outer.Attachments.Add(MapiAttachmentBuilder.FromContent("data.bin", RandomNumberGenerator.GetBytes(10_001)));

        var bytes = TnefFile.ToByteArray(outer);
        AssertCompliant(bytes);
        var message = TnefFile.Load(bytes);
        Assert.Equal(["Red", "Blue"], message.Categories);
        var embedded = message.Attachments[0].GetEmbeddedMessage()!;
        Assert.Equal("Inner", embedded.Subject);
        Assert.Equal("inner"u8.ToArray(), embedded.Attachments.Single().GetContent());
        Assert.HasCount(10_001, message.Attachments[1].GetContent());
        Assert.Throws<EmailResourceLimitException>(() => TnefFile.Load(bytes, new PstReadOptions { MaxEmbeddedMessageDepth = 0 }).Attachments[0].GetEmbeddedMessage());
    }

    [Fact]
    public void TypedItems_RoundTrip()
    {
        var contact = new MapiContactBuilder { GivenName = "Bob", Surname = "Smith", Email1Address = "bob@example.com" };
        var copy = Assert.IsType<MapiContact>(TnefFile.Load(TnefFile.ToByteArray(contact)));
        Assert.Equal("Bob", copy.GivenName);
        Assert.Equal("bob@example.com", copy.Email1Address);
    }

    [Fact]
    public void InvalidStreams_Throw()
    {
        var bytes = File.ReadAllBytes(GetPath("poi-quick-winmail.dat"));
        Assert.Throws<InvalidEmailDataException>(() => TnefFile.Load("not a TNEF stream"u8));
        Assert.Throws<InvalidEmailDataException>(() => TnefFile.Load(bytes.AsSpan(0, bytes.Length / 2)));

        // A modified byte breaks the checksum of its attribute
        bytes[100] ^= 0xFF;
        Assert.Throws<InvalidEmailDataException>(() => TnefFile.Load(bytes));
        _ = TnefFile.Load(bytes, new PstReadOptions { VerifyChecksums = false });
    }

    private static void AssertCompliant(byte[] bytes)
    {
        // MimeKit checks the signature, the attributes, their lengths and their checksums; it reads the properties in ConvertToMessage (DescribeWithMimeKit)
        using var reader = new MimeKit.Tnef.TnefReader(new MemoryStream(bytes), 0, MimeKit.Tnef.TnefComplianceMode.Strict);
        while (reader.ReadNextAttribute())
        {
        }

        Assert.Equal(MimeKit.Tnef.TnefComplianceStatus.Compliant, reader.ComplianceStatus);
        _ = DescribeWithMimeKit(bytes);
    }

    private static List<string> Describe(MapiMessage message)
    {
        var result = new List<string>
        {
            "subject: " + message.Subject,
            "body: " + Normalize(message.Body),
        };

        foreach (var attachment in message.Attachments.Where(attachment => !attachment.IsEmbeddedMessage).OrderBy(attachment => attachment.Name, StringComparer.Ordinal))
        {
            result.Add($"attachment: {attachment.Name} {Hash(attachment.GetContent())}");
        }

        return result;
    }

    private static List<string> DescribeWithMimeKit(byte[] bytes)
    {
        using var part = new MimeKit.Tnef.TnefPart { Content = new MimeKit.MimeContent(new MemoryStream(bytes)) };
        using var message = part.ConvertToMessage();
        var result = new List<string>
        {
            "subject: " + message.Subject,
            "body: " + Normalize(message.TextBody),
        };

        // The inline images are body parts of a multipart/related, not attachments; MimeKit keeps an embedded message as a TNEF part, which the description of the library skips too
        foreach (var attachment in message.BodyParts.OfType<MimeKit.MimePart>().Where(part => part.FileName is not null && !part.ContentType.IsMimeType("application", "ms-tnef")).OrderBy(attachment => attachment.FileName, StringComparer.Ordinal))
        {
            using var content = new MemoryStream();
            attachment.Content?.DecodeTo(content);
            result.Add($"attachment: {attachment.FileName} {Hash(content.ToArray())}");
        }

        return result;
    }

    private static string? Normalize(string? text) => string.IsNullOrEmpty(text) ? null : text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\0', '\n', ' ');

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data))[..16];
}
