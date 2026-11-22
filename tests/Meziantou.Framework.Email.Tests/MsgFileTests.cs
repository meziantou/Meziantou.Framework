using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.Email.Internals.Cfb;

namespace Meziantou.Framework.Email.Tests;

/// <summary>Reads the .msg files of TestData/Msg and the files the writer writes.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
public sealed class MsgFileTests
{
    static MsgFileTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static TheoryData<string> Files => [.. Directory.EnumerateFiles(GetPath(""), "*.msg").Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    internal static string GetPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "Msg", fileName);

    [Theory]
    [MemberData(nameof(Files))]
    public void Rewritten_ReadsTheSame(string fileName)
    {
        // The library writes the item it read, and reads the file it writes as the original file
        var item = MsgFile.Load(File.ReadAllBytes(GetPath(fileName)));
        var bytes = MsgFile.ToByteArray(MapiMessageBuilder.FromMessage(item));
        Assert.Equal(Describe(item), Describe(MsgFile.Load(bytes)));
    }

    [Fact]
    public void TypedItems()
    {
        var appointment = Assert.IsType<MapiAppointment>(MsgFile.Load(GetPath("poi-msgClassAppointment.msg")));
        Assert.NotNull(appointment.Start);
        Assert.True(appointment.End >= appointment.Start);

        var contact = Assert.IsType<MapiContact>(MsgFile.Load(GetPath("poi-msgClassContact.msg")));
        Assert.False(string.IsNullOrEmpty(contact.DisplayName));

        Assert.IsType<MapiTask>(MsgFile.Load(GetPath("poi-msgClassTask.msg")));
    }

    [Fact]
    public void NamesOfRecipients_UseTheCodePageOfTheMessage()
    {
        // An ANSI file in the code page 950 (Traditional Chinese): the recipient is the sender
        var message = MsgFile.Load(GetPath("poi-chinese-traditional.msg"));
        Assert.Equal("Tests Chang@FT (張毓倫)", message.SenderName);
        Assert.Equal(message.SenderName, message.Recipients.Single().DisplayName);
    }

    [Fact]
    public void NamedProperties_AreRead()
    {
        // The categories are a multi-valued named property (PS_PUBLIC_STRINGS, "Keywords")
        var message = MsgFile.Load(GetPath("poi-keywords.msg"));
        Assert.NotEmpty(message.Categories);
    }

    [Fact]
    public void Save_Snippet()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".msg");
        try
        {
            #region MsgFile_Save
            var message = new MapiMessageBuilder
            {
                Subject = "Quarterly report",
                Body = "The report is attached.",
                SenderName = "Alice",
                SenderEmailAddress = "alice@example.com",
                SenderAddressType = "SMTP",
            };
            message.Recipients.Add(new MapiRecipientBuilder("Bob", "bob@example.com"));
            message.Attachments.Add(MapiAttachmentBuilder.FromContent("report.csv", "Quarter,Total\nQ1,42\n"u8.ToArray(), "text/csv"));
            MsgFile.Save(message, path);

            var item = MsgFile.Load(path);
            Assert.Equal("Quarterly report", item.Subject);
            Assert.Equal("bob@example.com", item.Recipients.Single().EmailAddress);
            Assert.Equal("report.csv", item.Attachments.Single().Name);
            #endregion
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Template_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".oft");
        try
        {
            #region MsgFile_Template
            var template = new MapiMessageBuilder
            {
                Subject = "Weekly status",
                Body = "Done:\r\nNext:\r\n",
            };
            MsgFile.Save(template, path, template: true);

            var bytes = File.ReadAllBytes(path);
            Assert.True(MsgFile.IsTemplate(bytes));
            Assert.Equal(EmailFileFormat.OutlookTemplate, EmailFormatDetector.Detect(bytes));
            Assert.Equal("Weekly status", MsgFile.Load(bytes).Subject);
            #endregion
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void File_IsNotTemplate(string fileName)
    {
        var bytes = File.ReadAllBytes(GetPath(fileName));
        Assert.False(MsgFile.IsTemplate(bytes));
        Assert.Equal(EmailFileFormat.Msg, EmailFormatDetector.Detect(bytes));
    }

    [Fact]
    public void Writer_ClassIdentifier()
    {
        // [MS-OXMSG]: the root storage of an .msg file has the class identifier CLSID_MailMessage, as Outlook writes it
        var bytes = MsgFile.ToByteArray(new MapiMessageBuilder { Subject = "x" });
        var file = Meziantou.Framework.Email.Internals.Cfb.CompoundFile.Read(bytes, maxStreamSize: 0);
        Assert.Equal(new Guid("00020D0B-0000-0000-C000-000000000046"), file.Root.Clsid);
        Assert.False(MsgFile.IsTemplate(bytes));
    }

    [Fact]
    public void PstItem_ToMsg()
    {
        var pstPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Pst", "libyal-outlook.pst");
        var msgPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".msg");
        try
        {
            #region MsgFile_FromPst
            using (var file = PstFile.Open(pstPath))
            {
                var item = file.GetFolder("Inbox")!.EnumerateItems().First();
                MsgFile.Save(MapiMessageBuilder.FromMessage(item), msgPath);
            }

            var message = MsgFile.Load(msgPath);
            Assert.Equal("Welcome to Microsoft Office Outlook 2003", message.Subject);
            Assert.HasCount(11, message.Attachments);
            #endregion
        }
        finally
        {
            File.Delete(msgPath);
        }
    }

    [Fact]
    public void EmbeddedMessages_RoundTrip()
    {
        var inner = new MapiMessageBuilder { Subject = "Inner", Body = "The forwarded message" };
        inner.Attachments.Add(MapiAttachmentBuilder.FromContent("inner.txt", "inner"u8.ToArray()));
        var middle = new MapiMessageBuilder { Subject = "Middle" };
        middle.Attachments.Add(MapiAttachmentBuilder.FromMessage(inner));
        var outer = new MapiMessageBuilder { Subject = "Outer", Body = new string('x', 10_000) };
        outer.Attachments.Add(MapiAttachmentBuilder.FromMessage(middle));
        outer.Attachments.Add(MapiAttachmentBuilder.FromContent("large.bin", RandomNumberGenerator.GetBytes(100_000)));
        outer.Categories = ["Red", "Blue"];

        var bytes = MsgFile.ToByteArray(outer);
        var message = MsgFile.Load(bytes);
        Assert.Equal(["Red", "Blue"], message.Categories);
        Assert.Equal(new string('x', 10_000), message.Body);
        var middleMessage = message.Attachments[0].GetEmbeddedMessage()!;
        Assert.Equal("Middle", middleMessage.Subject);
        var innerMessage = middleMessage.Attachments[0].GetEmbeddedMessage()!;
        Assert.Equal("Inner", innerMessage.Subject);
        Assert.Equal("inner"u8.ToArray(), innerMessage.Attachments[0].GetContent());
        Assert.HasCount(100_000, message.Attachments[1].GetContent());
    }

    [Fact]
    public void EmbeddedMessages_DepthLimit()
    {
        var message = new MapiMessageBuilder { Subject = "0" };
        var current = message;
        for (var i = 1; i <= 5; i++)
        {
            var child = new MapiMessageBuilder { Subject = i.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            current.Attachments.Add(MapiAttachmentBuilder.FromMessage(child));
            current = child;
        }

        var item = MsgFile.Load(MsgFile.ToByteArray(message), new PstReadOptions { MaxEmbeddedMessageDepth = 3 });
        var embedded = item.Attachments[0].GetEmbeddedMessage()!.Attachments[0].GetEmbeddedMessage()!.Attachments[0].GetEmbeddedMessage()!;
        Assert.Equal("3", embedded.Subject);
        Assert.Throws<EmailResourceLimitException>(() => embedded.Attachments[0].GetEmbeddedMessage());
    }

    [Fact]
    public void NotAnMsgFile_Throws()
    {
        Assert.Throws<InvalidEmailDataException>(() => MsgFile.Load("not a compound file"u8.ToArray()));
        Assert.Throws<InvalidEmailDataException>(() => MsgFile.Load(File.ReadAllBytes(GetPath("poi-quick.msg")).AsMemory(0, 1024)));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 50)]
    [InlineData(3, 400)]
    public void CompoundFile_WrittenFile_ReadsTheSame(int seed, int entryCount)
    {
        // Nested storages and streams of every size class: empty, in the mini stream, in sectors
        var random = new Random(seed);
        var root = new CfbWriterStorage("Root Entry");
        var storages = new List<CfbWriterStorage> { root };
        for (var i = 0; i < entryCount; i++)
        {
            var parent = storages[random.Next(storages.Count)];
            var name = "e" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + new string('x', random.Next(20));
            if (random.Next(4) == 0)
            {
                storages.Add(parent.AddStorage(name));
            }
            else
            {
                var data = new byte[random.Next(3) switch { 0 => 0, 1 => random.Next(1, 4096), _ => random.Next(4096, 20_000) }];
                random.NextBytes(data);
                parent.AddStream(name, data);
            }
        }

        using var stream = new MemoryStream();
        CompoundFileWriter.Write(root, stream);
        var bytes = stream.ToArray();

        AssertSameContent(root, Internals.Cfb.CompoundFile.Read(bytes, long.MaxValue).Root);
    }

    [Fact]
    public void CompoundFile_LargeFile_UsesTheDifat()
    {
        // 8 MB of sectors need more than the 109 FAT sectors the header lists
        var root = new CfbWriterStorage("Root Entry");
        var data = new byte[8 * 1024 * 1024];
        new Random(4).NextBytes(data);
        root.AddStream("large", data);
        root.AddStream("small", [1, 2, 3]);
        using var stream = new MemoryStream();
        CompoundFileWriter.Write(root, stream);
        var bytes = stream.ToArray();
        Assert.NotEqual(0xFFFFFFFE, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(68)));

        AssertSameContent(root, Internals.Cfb.CompoundFile.Read(bytes, long.MaxValue).Root);
    }

    private static void AssertSameContent(CfbWriterStorage expected, Internals.Cfb.CfbEntry actual)
    {
        var names = expected.Storages.Select(storage => storage.Name).Concat(expected.Streams.Select(stream => stream.Name)).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(names, actual.Children.Keys.Order(StringComparer.Ordinal).ToList());
        foreach (var (name, data) in expected.Streams)
        {
            Assert.Equal(data, actual.GetChild(name)!.ReadStream());
        }

        foreach (var storage in expected.Storages)
        {
            AssertSameContent(storage, actual.GetChild(storage.Name)!);
        }
    }

    internal static List<string> Describe(MapiMessage message)
    {
        var result = new List<string>
        {
            "class: " + message.MessageClass,
            "subject: " + message.Subject,
            "sender: " + message.SenderName + " <" + message.SenderEmailAddress + ">",
            "body: " + Normalize(message.Body),
            "html: " + Normalize(message.BodyHtml),
        };

        foreach (var recipient in message.Recipients)
        {
            result.Add($"recipient: {recipient.RecipientType} {recipient.DisplayName} <{recipient.EmailAddress}>");
        }

        foreach (var attachment in message.Attachments)
        {
            result.Add(attachment.GetEmbeddedMessage() is { } embedded
                ? "message: " + string.Join(" | ", Describe(embedded))
                : $"attachment: {attachment.Name} {Hash(attachment.GetContent())}");
        }

        return result;
    }

    private static string? Normalize(string? text) => string.IsNullOrEmpty(text) ? null : text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\0', '\n', ' ');

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data))[..16];
}
