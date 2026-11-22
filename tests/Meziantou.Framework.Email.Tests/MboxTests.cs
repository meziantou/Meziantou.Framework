using System.Text;

namespace Meziantou.Framework.Email.Tests;

/// <summary>Tests of the mailbox formats: mbox (compared with the mbox parser of MimeKit), Maildir, .emlx and the Mork summaries of Thunderbird.</summary>
public sealed class MboxTests
{
    private static readonly string[] ExpectedBody =
    [
        "Hello Bob,",
        "From the start of a line.",
        ">From a quoted line.",
        ">>From a line quoted twice.",
        "The word From in the middle: no escape.",
    ];

    public static TheoryData<string, MboxFormat> Files => new()
    {
        { "mboxo.mbox", MboxFormat.Mboxo },
        { "mboxrd.mbox", MboxFormat.Mboxrd },
        { "mboxrd-crlf.mbox", MboxFormat.Mboxrd },
        { "mboxcl.mbox", MboxFormat.Mboxcl },
        { "mboxcl2.mbox", MboxFormat.Mboxcl2 },
    };

    internal static string GetPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "Mailbox", fileName);

    [Theory]
    [MemberData(nameof(Files))]
    public void File_MatchesMimeKit(string fileName, MboxFormat format)
    {
        var actual = MboxReader.ReadMessages(GetPath(fileName), new MboxReadOptions { Format = format })
            .Select(message => $"{message.Offset} {message.FromLine} | {message.Message.Subject} | {message.Message.MessageId}")
            .ToList();

        var expected = new List<string>();
        using (var stream = File.OpenRead(GetPath(fileName)))
        {
            var parser = new MimeKit.MimeParser(new MimeKit.ParserOptions { RespectContentLength = format is MboxFormat.Mboxcl or MboxFormat.Mboxcl2 }, stream, MimeKit.MimeFormat.Mbox);
            while (!parser.IsEndOfStream)
            {
                using var message = parser.ParseMessage();
                expected.Add($"{parser.MboxMarkerOffset} {parser.MboxMarker?.TrimEnd()} | {message.Subject} | {message.MessageId}");
            }
        }

        Assert.Equal(expected, actual);
        Assert.Equal(["First", "Café", "Last, without a final line break"], MboxReader.ReadMessages(GetPath(fileName), new MboxReadOptions { Format = format }).Select(message => message.Message.Subject));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void File_UnescapesTheLines(string fileName, MboxFormat format)
    {
        var messages = MboxReader.ReadMessages(GetPath(fileName), new MboxReadOptions { Format = format }).ToList();
        var first = messages[0].Message.TextBody!.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        if (format is MboxFormat.Mboxo or MboxFormat.Mboxcl)
        {
            // The escaping of mboxo cannot be reversed: ">From " is read as "From "
            Assert.Equal(["Hello Bob,", "From the start of a line.", "From a quoted line.", ">>From a line quoted twice.", "The word From in the middle: no escape."], first);
        }
        else
        {
            Assert.Equal(ExpectedBody, first);
        }

        Assert.Equal("alice@example.com", messages[0].Sender);
        Assert.Equal(new DateTimeOffset(2025, 7, 1, 8, 52, 37, TimeSpan.Zero), messages[0].Date);

        // A message ends with a line break in an mbox file, even when it had none
        Assert.Equal("The last line.", messages[2].Message.TextBody?.TrimEnd('\r', '\n'));
        Assert.Equal("data.bin", messages[1].Message.Attachments.OfType<MimePart>().Single().FileName);
    }

    [Theory]
    [InlineData(MboxFormat.Mboxo)]
    [InlineData(MboxFormat.Mboxrd)]
    [InlineData(MboxFormat.Mboxcl)]
    [InlineData(MboxFormat.Mboxcl2)]
    public void Write_ReadBack(MboxFormat format)
    {
        var messages = MboxReader.ReadMessages(GetPath("mboxrd.mbox")).Select(message => message.Message).ToList();
        using var stream = new MemoryStream();
        using (var writer = new MboxWriter(stream, format, leaveOpen: true))
        {
            foreach (var message in messages)
            {
                writer.Write(message);
            }
        }

        stream.Position = 0;
        var copies = MboxReader.ReadMessages(stream, new MboxReadOptions { Format = format }).ToList();
        Assert.Equal(messages.Select(message => message.Subject), copies.Select(copy => copy.Message.Subject));
        Assert.Equal(messages.Select(message => message.TextBody?.ReplaceLineEndings("\n")), copies.Select(copy => format is MboxFormat.Mboxo or MboxFormat.Mboxcl ? null : copy.Message.TextBody?.ReplaceLineEndings("\n")).Select((body, i) => body ?? messages[i].TextBody?.ReplaceLineEndings("\n")));
        Assert.Equal("alice@example.com", copies[0].Sender);
        Assert.Equal(messages[0].Date, copies[0].Date?.ToOffset(messages[0].Date!.Value.Offset));

        // MimeKit finds the same messages
        stream.Position = 0;
        var parser = new MimeKit.MimeParser(new MimeKit.ParserOptions { RespectContentLength = format is MboxFormat.Mboxcl or MboxFormat.Mboxcl2 }, stream, MimeKit.MimeFormat.Mbox);
        var subjects = new List<string?>();
        while (!parser.IsEndOfStream)
        {
            using var message = parser.ParseMessage();
            subjects.Add(message.Subject);
        }

        Assert.Equal(messages.Select(message => message.Subject), subjects);
    }

    [Fact]
    public void Read_Snippet()
    {
        var path = GetPath("mboxrd.mbox");

        #region MboxReader_ReadMessages
        foreach (var entry in MboxReader.ReadMessages(path, new MboxReadOptions { Format = MboxFormat.Mboxrd }))
        {
            // The envelope sender and the date of the "From " line, then the message, read when it is accessed
            var sender = entry.Sender;
            var subject = entry.Message.Subject;
            Assert.NotNull(sender);
            Assert.NotNull(subject);
        }
        #endregion
    }

    [Fact]
    public void Write_Snippet()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mbox");
        try
        {
            #region MboxWriter_Write
            using (var writer = MboxWriter.Create(path, MboxFormat.Mboxrd))
            {
                var message = new MimeMessage { Subject = "Hello", From = [new MailboxAddress("Alice", "alice@example.com")] };
                var body = new MimePart();
                body.SetText("From the start of a line, escaped as >From in the file.");
                message.Body = body;
                writer.Write(message, date: new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));
            }

            var entry = MboxReader.ReadMessages(path).Single();
            Assert.Equal("From alice@example.com Thu Jan 15 09:00:00 2026", entry.FromLine);
            Assert.StartsWith("From the start of a line", entry.Message.TextBody, StringComparison.Ordinal);
            #endregion

            Assert.Contains("\n>From the start", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_Invalid()
    {
        Assert.Empty(MboxReader.ReadMessages(new MemoryStream()));
        Assert.Throws<InvalidEmailDataException>(() => MboxReader.ReadMessages(new MemoryStream("Subject: not an mbox\n"u8.ToArray())).ToList());
        Assert.Throws<InvalidEmailDataException>(() => MboxReader.ReadMessages(new MemoryStream("From a Mon Jan  1 00:00:00 2024\nContent-Length: 1000\n\nshort\n"u8.ToArray()), new MboxReadOptions { Format = MboxFormat.Mboxcl2 }).ToList());
        Assert.Throws<EmailResourceLimitException>(() => MboxReader.ReadMessages(File.OpenRead(GetPath("mboxrd.mbox")), new MboxReadOptions { MessageOptions = new MimeReadOptions { MaxSize = 100 } }).ToList());
    }

    [Fact]
    public void Maildir_Snippet()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = directory.FullName;

            #region MaildirFolder_Usage
            var folder = MaildirFolder.Create(path);
            var message = new MimeMessage { Subject = "Hello" };
            var body = new MimePart();
            body.SetText("Hello from a Maildir folder.");
            message.Body = body;

            // A message without flags goes in "new", a message with flags in "cur" with the flags in its name
            var added = folder.AddMessage(message);
            Assert.True(added.IsNew);
            var read = folder.SetFlags(added, MaildirFlags.Seen | MaildirFlags.Replied);
            Assert.EndsWith("2,RS", read.FullPath, StringComparison.Ordinal);

            var sent = folder.CreateFolder("Sent");
            sent.AddMessage(message, MaildirFlags.Seen);
            Assert.Equal(["Sent"], folder.EnumerateFolders().Select(subfolder => subfolder.Name));
            Assert.Equal("Hello", folder.EnumerateMessages().Single().Load().Subject);
            #endregion

            var single = folder.EnumerateMessages().Single();
            Assert.False(single.IsNew);
            Assert.Equal(MaildirFlags.Seen | MaildirFlags.Replied, single.Flags);
            Assert.Equal(added.Key, single.Key);
            Assert.Throws<ArgumentException>(() => folder.Delete(sent.EnumerateMessages().Single()));
            folder.Delete(single);
            Assert.Empty(folder.EnumerateMessages());
            Assert.Throws<DirectoryNotFoundException>(() => MaildirFolder.Open(Path.Combine(path, "missing")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Maildir_ReadsTheNamesOfOtherTools()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var folder = MaildirFolder.Create(directory.FullName);
            File.WriteAllText(Path.Combine(directory.FullName, "cur", "1751359957.M1P2Q3.host;2,FS"), "Subject: Seen and flagged\n\nBody\n");
            File.WriteAllText(Path.Combine(directory.FullName, "new", "1751359958.M1P2Q4.host"), "Subject: New\n\nBody\n");
            File.WriteAllText(Path.Combine(directory.FullName, "cur", ".hidden"), "");
            var messages = folder.EnumerateMessages().ToList();
            Assert.Equal(["New", "Seen and flagged"], messages.Select(message => message.Load().Subject));
            Assert.Equal(MaildirFlags.Flagged | MaildirFlags.Seen, messages[1].Flags);
            Assert.Equal("1751359957.M1P2Q3.host", messages[1].Key);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Maildir_EnumeratesTheMessagesInTheOrderTheyWereAdded()
    {
        // The microseconds of the names have 6 digits, so that the names sort in the order of the messages
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var folder = MaildirFolder.Create(directory.FullName);
            var subjects = Enumerable.Range(0, 300).Select(i => "Message " + i.ToString(CultureInfo.InvariantCulture)).ToList();
            foreach (var subject in subjects)
            {
                folder.AddMessage(Encoding.ASCII.GetBytes("Subject: " + subject + "\n\nBody\n"));
            }

            Assert.Equal(subjects, folder.EnumerateMessages().Select(message => message.Load().Subject));
            Assert.All(folder.EnumerateMessages(), message => Assert.Matches(@"^\d+\.M\d{6}P", message.Key));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Emlx_Snippet()
    {
        var path = GetPath("message.emlx");

        #region EmlxFile_Usage
        var emlx = EmlxFile.Load(path);
        Assert.Equal("First", emlx.Message.Subject);
        Assert.True(emlx.Flags.HasFlag(EmlxFlags.Read));
        Assert.Equal(new DateTimeOffset(2025, 7, 1, 8, 52, 37, TimeSpan.Zero), emlx.DateReceived);
        Assert.Equal("4242", emlx.Properties["remote-id"]);

        using var stream = new MemoryStream();
        EmlxFile.Save(emlx.Message, stream, EmlxFlags.Read | EmlxFlags.Flagged);
        #endregion

        var copy = EmlxFile.Load(stream.ToArray());
        Assert.Equal("First", copy.Message.Subject);
        Assert.Equal(EmlxFlags.Read | EmlxFlags.Flagged, copy.Flags);
        Assert.Equal(emlx.Message.Date, copy.DateReceived?.ToOffset(emlx.Message.Date!.Value.Offset));
        Assert.Equal(1, emlx.RawFlags & 1);
        Assert.Equal((int)((8590195713 >> 10) & 0x3F), emlx.AttachmentCount);
    }

    [Fact]
    public void Emlx_Invalid()
    {
        Assert.Throws<InvalidEmailDataException>(() => EmlxFile.Load("Subject: not an emlx\n\n"u8.ToArray()));
        Assert.Throws<InvalidEmailDataException>(() => EmlxFile.Load("1000\nSubject: short\n\n"u8.ToArray()));
        Assert.Throws<InvalidEmailDataException>(() => EmlxFile.Load("14\nSubject: x\n\n\n<plist><dict><key>a</key>"u8.ToArray()));

        // A property list that declares an entity is not expanded
        var xml = "14\nSubject: x\n\n\n<?xml version=\"1.0\"?><!DOCTYPE plist [<!ENTITY e \"expanded\">]><plist><dict><key>a</key><string>&e;</string></dict></plist>"u8.ToArray();
        Assert.Throws<InvalidEmailDataException>(() => EmlxFile.Load(xml));
    }

    [Fact]
    public void Mork_Snippet()
    {
        var path = GetPath("summary.msf");

        #region MorkDocument_Usage
        var document = MorkDocument.Load(path);
        var table = document.FindTable("ns:msg:db:table:kind:msgs")!;
        Assert.Equal("First", table.Rows[0]["subject"]);
        Assert.Equal("Café", table.Rows[1]["subject"]);
        #endregion

        // The committed group updates the flags of the first row, the aborted group does not
        Assert.Equal("10011", table.Rows[0]["flags"]);
        Assert.Equal("Last, without a final line break", table.Rows[2]["subject"]);
        Assert.Equal("Dave (work) <dave@example.com>", table.Rows[2]["ccList"]);
        Assert.Equal("3", document.Rows.Single(row => row.Scope == "Ns:msg:db:row:scope:dbfolderinfo:all")["numMsgs"]);

        // A document written and read back has the same tables and rows
        using var stream = new MemoryStream();
        document.Save(stream);
        var copy = MorkDocument.Load(stream.ToArray());
        Assert.Equal(Describe(document), Describe(copy));
    }

    [Fact]
    public void ThunderbirdSummary_Snippet()
    {
        var path = GetPath("summary.msf");

        #region ThunderbirdSummary_Usage
        var messages = ThunderbirdSummary.ReadMessages(path);
        var first = messages[0];
        Assert.Equal("First", first.Subject);
        Assert.Equal("first@example.com", first.MessageId);
        Assert.Equal(new DateTimeOffset(2025, 7, 1, 8, 52, 37, TimeSpan.Zero), first.Date);
        Assert.True(first.Flags.HasFlag(ThunderbirdMessageFlags.Read));

        // The position of the message in the mbox file of the folder
        Assert.Equal(421, messages[1].Offset);
        #endregion

        Assert.Equal(ThunderbirdMessageFlags.Read | ThunderbirdMessageFlags.HasRe | ThunderbirdMessageFlags.New, first.Flags);
        Assert.Equal(0x1a5, first.Size);
        using var stream = new MemoryStream();
        ThunderbirdSummary.Create(messages).Save(stream);
        var copy = ThunderbirdSummary.ReadMessages(MorkDocument.Load(stream.ToArray())).ToList();
        Assert.Equal(messages.Select(Describe), copy.Select(Describe));
    }

    [Fact]
    public void Mork_Invalid()
    {
        Assert.Throws<InvalidEmailDataException>(() => MorkDocument.Load("not mork"u8));
        var document = MorkDocument.Load("// <!-- <mdb:mork:z v=\"1.4\"/> -->\n<(80=unterminated"u8);
        Assert.Empty(document.Tables);
        Assert.Throws<ArgumentException>(() => document.GetOrAddRow("scope", "not hex"));
    }

    private static List<string> Describe(MorkDocument document)
    {
        var result = new List<string>();
        foreach (var table in document.Tables)
        {
            result.Add($"table {table.Id}:{table.Scope} {table.Kind}");
            result.AddRange(table.Rows.Select(row => "  " + Describe(row)));
        }

        result.AddRange(document.Rows.Select(Describe));
        return result;
    }

    private static string Describe(MorkRow row) => $"{row.Id}:{row.Scope} " + string.Join(" ", row.Cells.OrderBy(cell => cell.Key, StringComparer.Ordinal).Select(cell => cell.Key + "=" + cell.Value));

    private static string Describe(ThunderbirdMessageSummary message) => string.Join("|", message.Key, message.Subject, message.Sender, message.Recipients, message.CcList, message.MessageId, message.Date, message.Flags, message.Size, message.LineCount, message.Offset);
}
