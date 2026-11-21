using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.Email.Internals.Cfb;

namespace Meziantou.Framework.Email.Tests;

/// <summary>Reads the .msg files of TestData/Msg and the files the writer writes, and compares them with what MsgReader, an independent reader, reads.</summary>
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
    public void File_MatchesMsgReader(string fileName)
    {
        var bytes = File.ReadAllBytes(GetPath(fileName));
        using var expected = new MsgReader.Outlook.Storage.Message(new MemoryStream(bytes));
        Assert.Equal(DescribeWithMsgReader(expected), Describe(MsgFile.Load(bytes)));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Rewritten_MatchesMsgReader(string fileName)
    {
        // The library writes the item it read, and MsgReader reads the file it writes as the library does
        var item = MsgFile.Load(File.ReadAllBytes(GetPath(fileName)));
        var bytes = MsgFile.ToByteArray(MapiMessageBuilder.FromMessage(item));
        var actual = Describe(MsgFile.Load(bytes));
        Assert.Equal(Describe(item), actual);
        using var oracle = new MsgReader.Outlook.Storage.Message(new MemoryStream(bytes));
        Assert.Equal(DescribeWithMsgReader(oracle), actual);
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
        using var oracle = new MsgReader.Outlook.Storage.Message(GetPath("poi-keywords.msg"));
        Assert.NotEmpty(message.Categories);
        Assert.Equal(oracle.Categories, message.Categories);
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

            using var oracle = new MsgReader.Outlook.Storage.Message(path);
            Assert.Equal(DescribeWithMsgReader(oracle), Describe(item));
        }
        finally
        {
            File.Delete(path);
        }
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

            using var oracle = new MsgReader.Outlook.Storage.Message(msgPath);
            Assert.Equal(DescribeWithMsgReader(oracle), Describe(message));
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

        using var oracle = new MsgReader.Outlook.Storage.Message(new MemoryStream(bytes));
        Assert.Equal(DescribeWithMsgReader(oracle), Describe(message));
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
    public void CompoundFile_WrittenFile_ReadByOpenMcdf(int seed, int entryCount)
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

        using var oracle = OpenMcdf.RootStorage.Open(new MemoryStream(bytes), OpenMcdf.StorageModeFlags.None);
        AssertSameContent(root, oracle, Internals.Cfb.CompoundFile.Read(bytes, long.MaxValue).Root);
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

        using var oracle = OpenMcdf.RootStorage.Open(new MemoryStream(bytes), OpenMcdf.StorageModeFlags.None);
        AssertSameContent(root, oracle, Internals.Cfb.CompoundFile.Read(bytes, long.MaxValue).Root);
    }

    [Theory]
    [InlineData(OpenMcdf.Version.V3)]
    [InlineData(OpenMcdf.Version.V4)]
    public void CompoundFile_ReadsFilesOfOpenMcdf(OpenMcdf.Version version)
    {
        using var stream = new MemoryStream();
        using (var file = OpenMcdf.RootStorage.Create(stream, version, OpenMcdf.StorageModeFlags.LeaveOpen))
        {
            // The streams of a storage are created one storage after the other: OpenMcdf loses entries when they are interleaved
            foreach (var i in Enumerable.Range(0, 40).OrderBy(i => i % 2))
            {
                var parent = i % 2 == 0 ? file : (file.TryOpenStorage("Storage", out var existing) ? existing : file.CreateStorage("Storage"));
                using var child = parent.CreateStream("Stream" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                child.Write(Enumerable.Range(0, (i + 1) * 300).Select(value => (byte)value).ToArray());
            }

            file.Flush(consolidate: true);
        }

        var root = Internals.Cfb.CompoundFile.Read(stream.ToArray(), long.MaxValue).Root;
        for (var i = 0; i < 40; i++)
        {
            var parent = i % 2 == 0 ? root : root.GetChild("Storage")!;
            Assert.Equal(Enumerable.Range(0, (i + 1) * 300).Select(value => (byte)value).ToArray(), parent.GetChild("stream" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))!.ReadStream());
        }
    }

    private static void AssertSameContent(CfbWriterStorage expected, OpenMcdf.Storage oracle, Internals.Cfb.CfbEntry actual)
    {
        var names = expected.Storages.Select(storage => storage.Name).Concat(expected.Streams.Select(stream => stream.Name)).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(names, oracle.EnumerateEntries().Select(entry => entry.Name).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(names, actual.Children.Keys.Order(StringComparer.Ordinal).ToList());
        foreach (var (name, data) in expected.Streams)
        {
            // OpenMcdf finds the entries by a search in the tree: the order of the tree is checked
            using var content = new MemoryStream();
            using (var oracleStream = oracle.OpenStream(name))
            {
                oracleStream.CopyTo(content);
            }

            Assert.Equal(data, content.ToArray());
            Assert.Equal(data, actual.GetChild(name)!.ReadStream());
        }

        foreach (var storage in expected.Storages)
        {
            AssertSameContent(storage, oracle.OpenStorage(storage.Name), actual.GetChild(storage.Name)!);
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
            // MsgReader decodes the 8-bit names of the recipients with the code page 1252, not with the code page of the message: the names are compared in NamesOfRecipients_UseTheCodePageOfTheMessage
            result.Add($"recipient: {recipient.RecipientType} <{recipient.EmailAddress}>");
        }

        foreach (var attachment in message.Attachments)
        {
            result.Add(attachment.GetEmbeddedMessage() is { } embedded
                ? "message: " + string.Join(" | ", Describe(embedded))
                : $"attachment: {attachment.Name} {Hash(attachment.GetContent())}");
        }

        return result;
    }

    internal static List<string> DescribeWithMsgReader(MsgReader.Outlook.Storage.Message message)
    {
        var result = new List<string>
        {
            "class: " + message.GetMapiProperty("001A"),
            "subject: " + message.Subject,
            "sender: " + message.GetMapiProperty("0C1A") + " <" + message.GetMapiProperty("0C1F") + ">",
            "body: " + Normalize(message.GetMapiProperty("1000") as string),
            "html: " + Normalize(message.GetMapiProperty("1013") switch { string html => html, byte[] html => (message.InternetCodePage ?? Encoding.UTF8).GetString(html), _ => null }),
        };

        foreach (var recipient in message.Recipients)
        {
            result.Add($"recipient: {recipient.Type switch { MsgReader.Outlook.RecipientType.Cc => MapiRecipientType.Cc, MsgReader.Outlook.RecipientType.Bcc => MapiRecipientType.Bcc, _ => MapiRecipientType.To }} <{recipient.GetMapiProperty("3003")}>");
        }

        foreach (var attachment in message.Attachments)
        {
            result.Add(attachment switch
            {
                MsgReader.Outlook.Storage.Message embedded => "message: " + string.Join(" | ", DescribeWithMsgReader(embedded)),
                MsgReader.Outlook.Storage.Attachment file => $"attachment: {file.FileName} {Hash(file.Data ?? [])}",
                _ => "other",
            });
        }

        return result;
    }

    private static string? Normalize(string? text) => string.IsNullOrEmpty(text) ? null : text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\0', '\n', ' ');

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data))[..16];
}
