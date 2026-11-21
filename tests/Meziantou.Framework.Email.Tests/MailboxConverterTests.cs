namespace Meziantou.Framework.Email.Tests;

/// <summary>Converts the mailboxes of TestData between Outlook data files, folders of mbox files and Maildir folders, and reads the mbox files the converter writes with MimeKit, an independent reader.</summary>
public sealed class MailboxConverterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mailbox-" + Guid.NewGuid().ToString("N"));

    public MailboxConverterTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Usage_Snippet()
    {
        var pstPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Pst", "libyal-outlook.pst");
        var mboxPath = Path.Combine(_directory, "Thunderbird");
        var maildirPath = Path.Combine(_directory, "Maildir");

        #region MailboxConverter_Usage
        // An Outlook data file to the folders of Thunderbird, then to a Maildir folder
        var result = MailboxConverter.Convert(pstPath, mboxPath, MailboxFormat.Mbox);
        Assert.Empty(result.Errors);
        Assert.True(File.Exists(Path.Combine(mboxPath, "Inbox")));

        var copy = MailboxConverter.Convert(mboxPath, maildirPath, MailboxFormat.Maildir);
        Assert.Equal(result.MessageCount, copy.MessageCount);
        Assert.Equal(MailboxFormat.Maildir, MailboxConverter.DetectFormat(maildirPath));
        #endregion

        Assert.Equal(DescribePst(pstPath, includeNonMail: false), DescribeMbox(mboxPath));
        Assert.Equal(DescribeMbox(mboxPath), DescribeMaildir(maildirPath));
    }

    [Theory]
    [InlineData("libyal-outlook.pst")]
    [InlineData("tika-variousBodyTypes.pst")]
    [InlineData("java-libpst-dist-list.pst")]
    public void Pst_ToMbox_ToPst(string fileName)
    {
        var pstPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Pst", fileName);
        var mboxPath = Path.Combine(_directory, "mbox");
        var result = MailboxConverter.Convert(pstPath, mboxPath, MailboxFormat.Mbox);
        Assert.Empty(result.Errors);
        var expected = DescribePst(pstPath, includeNonMail: false);
        Assert.Equal(expected, DescribeMbox(mboxPath));

        // MimeKit reads the mbox files the converter writes, with the same messages
        Assert.Equal(expected, DescribeMboxWithMimeKit(mboxPath));

        var copyPath = Path.Combine(_directory, "copy.pst");
        var back = MailboxConverter.Convert(mboxPath, copyPath, MailboxFormat.Pst);
        Assert.Empty(back.Errors);
        Assert.Equal(result.MessageCount, back.MessageCount);
        Assert.Equal(expected, DescribePst(copyPath, includeNonMail: false));
        using var copy = PstFile.Open(copyPath);
        Assert.Empty(copy.Validate());
    }

    [Fact]
    public void Pst_ToPst_KeepsEveryItem()
    {
        var pstPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Pst", "java-libpst-dist-list.pst");
        var copyPath = Path.Combine(_directory, "copy.pst");
        var result = MailboxConverter.Convert(pstPath, copyPath, MailboxFormat.Pst);
        Assert.Equal(0, result.SkippedItemCount);
        Assert.Equal(DescribePst(pstPath, includeNonMail: true), DescribePst(copyPath, includeNonMail: true));
    }

    [Fact]
    public void NonMailItems()
    {
        // The contacts, appointments and distribution lists are skipped, or written as messages
        var pstPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Pst", "java-libpst-dist-list.pst");
        var skipped = MailboxConverter.Convert(pstPath, Path.Combine(_directory, "skipped"), MailboxFormat.Maildir);
        Assert.True(skipped.SkippedItemCount > 0);
        var included = MailboxConverter.Convert(pstPath, Path.Combine(_directory, "included"), MailboxFormat.Maildir, new MailboxConversionOptions { IncludeNonMailItems = true });
        Assert.Equal(0, included.SkippedItemCount);
        Assert.Equal(skipped.MessageCount + skipped.SkippedItemCount, included.MessageCount);
    }

    [Theory]
    [InlineData("mboxrd.mbox", MboxFormat.Mboxrd)]
    [InlineData("mboxo.mbox", MboxFormat.Mboxo)]
    [InlineData("mboxcl2.mbox", MboxFormat.Mboxcl2)]
    public void Mbox_ToMaildir_ToMbox_ByteForByte(string fileName, MboxFormat format)
    {
        // The messages of mbox and Maildir folders are copied byte for byte
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "Mailbox", fileName);
        var options = new MailboxConversionOptions { MboxFormat = format };
        var maildir = Path.Combine(_directory, "maildir");
        MailboxConverter.Convert(source, maildir, MailboxFormat.Maildir, options);
        var mbox = Path.Combine(_directory, "mbox");
        MailboxConverter.Convert(maildir, mbox, MailboxFormat.Mbox, options);

        var expected = MboxReader.ReadMessages(source, new MboxReadOptions { Format = format }).Select(message => Convert.ToHexString(message.Content.Span)).ToList();
        var folder = Path.GetFileNameWithoutExtension(fileName);
        var actual = MboxReader.ReadMessages(Path.Combine(mbox, folder), new MboxReadOptions { Format = format }).Select(message => Convert.ToHexString(message.Content.Span)).ToList();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FolderTree_ReadFlags()
    {
        // A Maildir++ tree: the root is the inbox, ".Archive.2026" a subfolder of "Archive"
        var root = MaildirFolder.Create(Path.Combine(_directory, "source"));
        root.AddMessage(CreateMessage("Read"), MaildirFlags.Seen);
        root.AddMessage(CreateMessage("Unread"));
        root.CreateFolder("Archive").AddMessage(CreateMessage("Archived"), MaildirFlags.Seen);
        root.CreateFolder("Archive.2026").AddMessage(CreateMessage("Archived in 2026"));

        var mbox = Path.Combine(_directory, "mbox");
        var result = MailboxConverter.Convert(root.FullPath, mbox, MailboxFormat.Mbox);
        Assert.Equal(3, result.FolderCount);
        Assert.Equal(4, result.MessageCount);
        Assert.True(File.Exists(Path.Combine(mbox, "Inbox")));
        Assert.True(File.Exists(Path.Combine(mbox, "Archive")));
        Assert.True(File.Exists(Path.Combine(mbox, "Archive.sbd", "2026")));
        Assert.Equal([("Read", "RO"), ("Unread", null)], MboxReader.ReadMessages(Path.Combine(mbox, "Inbox")).Select(message => (message.Message.Subject!, message.Message.Headers["Status"])).OrderBy(pair => pair.Item1, StringComparer.Ordinal));

        var pst = Path.Combine(_directory, "mailbox.pst");
        MailboxConverter.Convert(root.FullPath, pst, MailboxFormat.Pst);
        using (var file = PstFile.Open(pst))
        {
            Assert.Equal([("Read", true), ("Unread", false)], file.GetFolder("Inbox")!.EnumerateItems().Select(item => (item.Subject!, item.IsRead)).OrderBy(pair => pair.Item1, StringComparer.Ordinal));
            Assert.Equal("Archived in 2026", file.GetFolder("Archive/2026")!.EnumerateItems().Single().Subject);
        }

        var maildir = Path.Combine(_directory, "maildir");
        MailboxConverter.Convert(pst, maildir, MailboxFormat.Maildir);
        var copy = MaildirFolder.Open(maildir);
        // The writer of Outlook data files creates the Deleted Items folder Outlook requires
        Assert.Equal(["Archive", "Archive.2026", "Deleted Items"], copy.EnumerateFolders().Select(folder => folder.Name));
        Assert.Equal([("Read", true), ("Unread", false)], copy.EnumerateMessages().Select(message => (message.Load().Subject!, message.Flags.HasFlag(MaildirFlags.Seen))).OrderBy(pair => pair.Item1, StringComparer.Ordinal));
    }

    [Fact]
    public void InvalidPaths()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "Mailbox", "mboxrd.mbox");
        var existing = Path.Combine(_directory, "existing");
        File.WriteAllText(existing, "x");
        Assert.Throws<IOException>(() => MailboxConverter.Convert(source, existing, MailboxFormat.Maildir));
        Assert.Throws<InvalidOperationException>(() => MailboxConverter.Convert(existing, Path.Combine(_directory, "out"), MailboxFormat.Maildir));
        Assert.Null(MailboxConverter.DetectFormat(Path.Combine(_directory, "missing")));
        Assert.Equal(MailboxFormat.Mbox, MailboxConverter.DetectFormat(source));
        Assert.Equal(MailboxFormat.Pst, MailboxConverter.DetectFormat(Path.Combine(AppContext.BaseDirectory, "TestData", "Pst", "libyal-outlook.pst")));
    }

    private static MimeMessage CreateMessage(string subject) => new EmailMessage { Subject = subject, From = new MailboxAddress("Alice", "alice@example.com"), TextBody = subject }.ToMime();

    // The messages of each folder, by subject, of the items that are emails
    private static List<string> DescribePst(string path, bool includeNonMail)
    {
        var result = new List<string>();
        using var file = PstFile.Open(path);
        void Walk(PstFolder folder, string name)
        {
            if (folder.IsSearchFolder)
            {
                return;
            }

            foreach (var item in folder.EnumerateItems())
            {
                if (includeNonMail || item.GetType() == typeof(MapiMessage) || item is MapiMeetingMessage)
                {
                    result.Add(name + ": " + Normalize(item.Subject) + (includeNonMail ? " " + item.MessageClass : ""));
                }
            }

            foreach (var child in folder.Folders)
            {
                Walk(child, name.Length == 0 ? child.DisplayName! : name + "/" + child.DisplayName);
            }
        }

        var root = file.Store.IpmSubtree ?? file.RootFolder;
        foreach (var item in root.EnumerateItems())
        {
            if (includeNonMail || item.GetType() == typeof(MapiMessage) || item is MapiMeetingMessage)
            {
                result.Add("Inbox: " + Normalize(item.Subject) + (includeNonMail ? " " + item.MessageClass : ""));
            }
        }

        foreach (var child in root.Folders)
        {
            Walk(child, child.DisplayName!);
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static List<string> DescribeMbox(string directory)
    {
        var result = new List<string>();
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var folder = Path.GetRelativePath(directory, file).Replace(".sbd" + Path.DirectorySeparatorChar, "/", StringComparison.Ordinal);
            foreach (var message in MboxReader.ReadMessages(file))
            {
                result.Add(folder + ": " + Normalize(message.Message.Subject));
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static List<string> DescribeMboxWithMimeKit(string directory)
    {
        var result = new List<string>();
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var folder = Path.GetRelativePath(directory, file).Replace(".sbd" + Path.DirectorySeparatorChar, "/", StringComparison.Ordinal);
            if (new FileInfo(file).Length == 0)
            {
                // An empty folder, which MimeKit does not read
                continue;
            }

            using var stream = File.OpenRead(file);
            var parser = new MimeKit.MimeParser(stream, MimeKit.MimeFormat.Mbox);
            while (!parser.IsEndOfStream)
            {
                using var message = parser.ParseMessage();
                result.Add(folder + ": " + Normalize(message.Subject));
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static List<string> DescribeMaildir(string path)
    {
        var root = MaildirFolder.Open(path);
        var result = root.EnumerateMessages().Select(message => "Inbox: " + Normalize(message.Load().Subject)).ToList();
        foreach (var (name, folder) in root.EnumerateFolders())
        {
            result.AddRange(folder.EnumerateMessages().Select(message => name.Replace('.', '/') + ": " + Normalize(message.Load().Subject)));
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    // A subject read back from a MIME header has its whitespace folded
    private static string Normalize(string? subject) => string.Join(' ', (subject ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
