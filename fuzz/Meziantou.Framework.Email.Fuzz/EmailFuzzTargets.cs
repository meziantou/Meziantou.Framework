using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email.Fuzz;

/// <summary>
/// <see cref="PstFile"/> rejects an invalid file with an <see cref="EmailException"/> and never throws anything else, and
/// <see cref="PstFile.Validate"/> reports problems instead of throwing. The writer writes files that read back with the content it
/// was given and that <see cref="PstFile.Validate"/> accepts, and copying a file that reads gives a file with the same content.
/// </summary>
public static partial class EmailFuzzTargets
{
    private const string TestDataPrefix = "TestData/Pst/";

    private static readonly string[] Tokens =
    [
        // The header, the pages, the blocks and the heaps
        "!BDN", "SM", "SO", BinaryToken([0x17, 0x00]), BinaryToken([0x0E, 0x00]), BinaryToken([0x24, 0x00]),
        BinaryToken([0x80, 0x80]), BinaryToken([0x81, 0x81]), BinaryToken([0x84, 0x84]), BinaryToken([0x83, 0x83]), BinaryToken([0x86, 0x86]),
        BinaryToken([0x01, 0x01]), BinaryToken([0x01, 0x02]), BinaryToken([0x02, 0x00]), BinaryToken([0x02, 0x01]),
        BinaryToken([0xEC, 0xBC]), BinaryToken([0xEC, 0x7C]), BinaryToken([0xB5, 0x02, 0x06]), BinaryToken([0xB5, 0x04, 0x04]), BinaryToken([0x7C]),
        BinaryToken([0x20, 0x00, 0x00, 0x00]), BinaryToken([0x40, 0x00, 0x00, 0x00]),
        // Special nodes
        BinaryToken([0x21, 0x00, 0x00, 0x00]), BinaryToken([0x61, 0x00, 0x00, 0x00]), BinaryToken([0x22, 0x01, 0x00, 0x00]), BinaryToken([0x92, 0x06, 0x00, 0x00]), BinaryToken([0x71, 0x06, 0x00, 0x00]),
        // Property tags
        BinaryToken([0x1F, 0x00, 0x37, 0x00]), BinaryToken([0x1F, 0x00, 0x1A, 0x00]), BinaryToken([0x1F, 0x00, 0x01, 0x30]), BinaryToken([0x02, 0x01, 0x01, 0x37]), BinaryToken([0x0D, 0x00, 0x01, 0x37]),
        BinaryToken([0x02, 0x01, 0x09, 0x10]), BinaryToken([0x03, 0x00, 0xF2, 0x67]), BinaryToken([0x02, 0x10]), BinaryToken([0x1F, 0x10]),
        // Compressed RTF and recurrences
        "LZFu", "MELA", "{\\rtf1\\ansi\\mac\\deff0\\deftab720", BinaryToken([0x04, 0x30, 0x04, 0x30]), BinaryToken([0x06, 0x30, 0x00, 0x00]), BinaryToken([0x09, 0x30, 0x00, 0x00]),
        BinaryToken([0x0A, 0x20]), BinaryToken([0x0B, 0x20]), BinaryToken([0x0C, 0x20]), BinaryToken([0x0D, 0x20]), BinaryToken([0x21, 0x20, 0x00, 0x00]), BinaryToken([0x23, 0x20, 0x00, 0x00]),
    ];

    private static readonly PstReadOptions ReadOptions = new() { MaxPropertySize = 4 * 1024 * 1024, MaxTreeDepth = 16, MaxEmbeddedMessageDepth = 8, CacheSize = 32 };

    public static IReadOnlyList<FuzzTarget> All => [.. PstTargets, .. MimeTargets, .. MsgTargets, .. TnefTargets, .. MailboxTargets, .. CalendarTargets, .. OabTargets, .. RtfTargets, .. DetectTargets];

    private static IReadOnlyList<FuzzTarget> PstTargets { get; } =
    [
        new("pst-read", Read) { Seeds = GetFileSeeds, Dictionary = Tokens },
        new("pst-roundtrip", RoundTrip) { Seeds = GetFileSeeds, Dictionary = Tokens },
        new("pst-write", Write) { Seeds = GetWriteSeeds, Dictionary = Tokens },
        new("pst-lzfu", LzfuTarget) { Seeds = GetLzfuSeeds, Dictionary = Tokens },
        new("pst-recurrence", Recurrence) { Seeds = GetRecurrenceSeeds, Dictionary = Tokens },
    ];

    /// <summary>Reads everything a file contains, and checks it.</summary>
    private static void Read(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        try
        {
            using var file = PstFile.Open(bytes, ReadOptions);
            Describe(file.RootFolder, [], readContents: true);
        }
        catch (EmailException)
        {
        }

        try
        {
            using var file = PstFile.Open(bytes, ReadOptions);
            foreach (var issue in file.Validate())
            {
                FuzzAssert.False(string.IsNullOrEmpty(issue.Message), "An issue has no message");
            }
        }
        catch (EmailException)
        {
        }
    }

    /// <summary>Copies the items of a file that reads to a new file, which must contain the same items.</summary>
    private static void RoundTrip(ReadOnlySpan<byte> data)
    {
        try
        {
            using var source = PstFile.Open(data.ToArray(), ReadOptions);
            var root = source.Store.IpmSubtree ?? source.RootFolder;
            var expected = Describe(root, [], readContents: true);

            using var stream = new MemoryStream();
            using (var writer = PstWriter.Create(stream, new PstWriterOptions { StoreId = Guid.Empty, Now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }, leaveOpen: true))
            {
                Copy(root, writer.RootFolder, []);
            }

            using var copy = PstFile.Open(stream.ToArray());
            var issues = copy.Validate();
            FuzzAssert.True(issues.Count == 0, "The copy is not valid: " + string.Join("; ", issues));
            FuzzAssert.Equal(string.Join('\n', expected), string.Join('\n', Describe(copy.Store.IpmSubtree!, [], readContents: true)), "The copy differs from the source");
        }
        catch (EmailException)
        {
        }
        catch (InvalidOperationException)
        {
            // A limit of the writer: more than 255 columns in a table, more than 32,767 named properties...
        }
    }

    /// <summary>Writes the folders and the items the input describes, and reads them back.</summary>
    private static void Write(ReadOnlySpan<byte> data)
    {
        var format = FuzzInput.Pick(Enum.GetValues<PstFormat>(), FuzzInput.ReadByte(ref data));
        var encryption = FuzzInput.Pick(Enum.GetValues<PstEncryption>(), FuzzInput.ReadByte(ref data));
        var ansi = format == PstFormat.Ansi;
        var expected = new List<string>();

        using var stream = new MemoryStream();
        using (var writer = PstWriter.Create(stream, new PstWriterOptions { Format = format, Encryption = encryption, StoreId = Guid.Empty, Now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }, leaveOpen: true))
        {
            var folders = new List<(PstWriterFolder Folder, string Path)> { (writer.RootFolder, "") };
            var operations = 0;
            while (!data.IsEmpty && operations++ < 64)
            {
                var operation = FuzzInput.ReadByte(ref data);
                var (folder, path) = FuzzInput.Pick(folders, FuzzInput.ReadByte(ref data));
                var text = ReadString(ref data, ansi);
                switch (operation % 5)
                {
                    case 0 when folder.Folders.Count < 8 && path.Split('/').Length < 6:
                        var child = folder.CreateFolder(text);
                        folders.Add((child, path + "/" + Clean(text)));
                        break;
                    case 1:
                        var message = new MapiMessageBuilder { Subject = text, Body = ReadString(ref data, ansi) };
                        message.Recipients.Add(new MapiRecipientBuilder(text, "user@example.com", (MapiRecipientType)(1 + (operation % 3))));
                        folder.AddItem(message);
                        expected.Add($"{path}: IPM.Note|{Subject(text)}|1|0");
                        break;
                    case 2:
                        var attachment = new MapiMessageBuilder { Subject = text };
                        var content = FuzzInput.ReadUntil(ref data, 0xFF).ToArray();
                        attachment.Attachments.Add(MapiAttachmentBuilder.FromContent("file.bin", content));
                        attachment.Attachments.Add(MapiAttachmentBuilder.FromMessage(new MapiMessageBuilder { Subject = text }));
                        folder.AddItem(attachment);
                        expected.Add($"{path}: IPM.Note|{Subject(text)}|0|2");
                        expected.Add($"  file.bin|{content.Length}|{Convert.ToHexString(content)}");
                        expected.Add($"  {Subject(text)}|0|IPM.Note");
                        break;
                    case 3:
                        folder.AddItem(new MapiContactBuilder { GivenName = text, Surname = "S", Email1Address = "c@example.com" });
                        expected.Add($"{path}: IPM.Contact|{Subject(text.Length == 0 ? "S" : text + " S")}|0|0");
                        break;
                    case 4:
                        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(FuzzInput.ReadByte(ref data) * 37);
                        folder.AddItem(new MapiAppointmentBuilder { Subject = text, Start = start, End = start.AddHours(1), Location = text });
                        expected.Add($"{path}: IPM.Appointment|{Subject(text)}|0|0");
                        break;
                }
            }
        }

        using var file = PstFile.Open(stream.ToArray());
        var issues = file.Validate();
        FuzzAssert.True(issues.Count == 0, "The file is not valid: " + string.Join("; ", issues));
        var actual = new List<string>();
        DescribeWritten(file.Store.IpmSubtree!, "", actual);
        FuzzAssert.Equal(string.Join('\n', expected.Order(StringComparer.Ordinal)), string.Join('\n', actual.Order(StringComparer.Ordinal)), "The file does not contain the items written to it");
    }

    private static void LzfuTarget(ReadOnlySpan<byte> data)
    {
        try
        {
            Lzfu.Decompress(data, 1024 * 1024);
        }
        catch (EmailException)
        {
        }

        var compressed = Lzfu.Compress(data);
        FuzzAssert.True(Lzfu.Decompress(compressed, data.Length).AsSpan().SequenceEqual(data), "Decompressing the compressed data does not give the data");
        FuzzAssert.True(Lzfu.Decompress(Lzfu.Store(data), data.Length).AsSpan().SequenceEqual(data), "Reading the stored data does not give the data");
    }

    private static void Recurrence(ReadOnlySpan<byte> data)
    {
        MapiRecurrencePattern pattern;
        try
        {
            pattern = MapiRecurrencePattern.Parse(data);
        }
        catch (EmailException)
        {
            return;
        }

        var bytes = pattern.ToByteArray();
        var again = MapiRecurrencePattern.Parse(bytes);
        FuzzAssert.True(again.ToByteArray().AsSpan().SequenceEqual(bytes), "Writing the parsed recurrence again gives other bytes");
        FuzzAssert.Equal(pattern.Exceptions.Count, again.Exceptions.Count, "The number of exceptions changed");
    }

    /// <summary>Returns a description of the items of a folder and its subfolders, which reads every part of them.</summary>
    private static List<string> Describe(PstFolder folder, HashSet<uint> visited, bool readContents)
    {
        var result = new List<string>();
        if (!visited.Add(folder.NodeId) || visited.Count > 1000)
        {
            return result;
        }

        foreach (var item in folder.EnumerateItems().Take(1000))
        {
            DescribeItem(item, result, "", 0);
        }

        foreach (var child in folder.Folders)
        {
            if (!child.IsSearchFolder && child.DisplayName != "Deleted Items")
            {
                result.Add("folder " + child.DisplayName + " " + child.ContainerClass);
                result.AddRange(Describe(child, visited, readContents));
            }
        }

        return result;
    }

    private static void DescribeItem(MapiMessage item, List<string> result, string indent, int depth)
    {
        result.Add($"{indent}{item.GetType().Name}|{item.MessageClass}|{item.Subject}|{item.Body?.Length}|{item.BodyHtml?.Length}|{item.Recipients.Count}|{item.Attachments.Count}|{item.ClientSubmitTime:O}|{item.Categories.Count}");
        if (item.HasRtfBody)
        {
            result.Add(indent + "rtf " + item.GetRtfBody()?.Length);
        }

        foreach (var recipient in item.Recipients)
        {
            result.Add($"{indent}  {recipient.DisplayName}|{recipient.EmailAddress}|{recipient.RecipientType}");
        }

        foreach (var attachment in item.Attachments)
        {
            result.Add($"{indent}  {attachment.Name}|{attachment.Method}|{attachment.GetContent().Length}");
            if (attachment.GetEmbeddedMessage() is { } embedded && depth < 4)
            {
                DescribeItem(embedded, result, indent + "    ", depth + 1);
            }
        }

        switch (item)
        {
            case MapiAppointment appointment:
                result.Add($"{indent}  {appointment.Start:O}|{appointment.End:O}|{appointment.Location}|{appointment.GetRecurrence()?.ToByteArray().Length}");
                break;
            case MapiContact contact:
                result.Add($"{indent}  {contact.GivenName}|{contact.Surname}|{contact.Email1Address}");
                break;
            case MapiDistributionList list:
                result.Add($"{indent}  {string.Join(",", list.Members)}");
                break;
            case MapiTask task:
                result.Add($"{indent}  {task.Status}|{task.PercentComplete}|{task.GetRecurrence()?.ToByteArray().Length}");
                break;
        }
    }

    private static void DescribeWritten(PstFolder folder, string path, List<string> result)
    {
        foreach (var item in folder.EnumerateItems())
        {
            result.Add($"{path}: {item.MessageClass}|{item.Subject}|{item.Recipients.Count}|{item.Attachments.Count}");
            foreach (var attachment in item.Attachments)
            {
                if (attachment.GetEmbeddedMessage() is { } embedded)
                {
                    result.Add($"  {embedded.Subject}|{embedded.Attachments.Count}|{embedded.MessageClass}");
                }
                else
                {
                    var content = attachment.GetContent();
                    result.Add($"  {attachment.Name}|{content.Length}|{Convert.ToHexString(content)}");
                }
            }
        }

        foreach (var child in folder.Folders)
        {
            if (child.DisplayName != "Deleted Items")
            {
                DescribeWritten(child, path + "/" + child.DisplayName, result);
            }
        }
    }

    private static void Copy(PstFolder source, PstWriterFolder destination, HashSet<uint> visited)
    {
        if (!visited.Add(source.NodeId) || visited.Count > 1000)
        {
            return;
        }

        foreach (var item in source.EnumerateItems().Take(1000))
        {
            destination.AddItem(MapiMessageBuilder.FromMessage(item));
        }

        foreach (var child in source.Folders)
        {
            if (!child.IsSearchFolder && child.DisplayName != "Deleted Items")
            {
                Copy(child, destination.CreateFolder(child.DisplayName ?? "", child.ContainerClass), visited);
            }
        }
    }

    /// <summary>Reads a string: any text in a Unicode file, printable ASCII characters in an ANSI file, whose strings are in a code page.</summary>
    private static string ReadString(ref ReadOnlySpan<byte> data, bool ansi)
    {
        var bytes = FuzzInput.ReadUntil(ref data, 0);
        if (bytes.Length > 64)
        {
            bytes = bytes[..64];
        }

        // A folder name must not contain '/', which separates the names of a path
        var text = ansi ? string.Concat(bytes.ToArray().Select(b => (char)(0x20 + (b % 0x5E)))) : FuzzText.Decode(bytes);
        return text.Replace('/', '_');
    }

    /// <summary>The reader removes the null characters that end a string.</summary>
    private static string Clean(string value) => value.TrimEnd('\0');

    /// <summary>The subject as the reader returns it: a subject that starts with 0x01 has the length of its prefix in its second character ([MS-OXCMSG] 2.2.1.46).</summary>
    private static string Subject(string value)
    {
        value = Clean(value);
        return value.Length >= 2 && value[0] == '\u0001' ? value[2..] : value;
    }

    private static IEnumerable<FuzzSeed> GetFileSeeds()
    {
        var assembly = typeof(EmailFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(TestDataPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return new FuzzSeed(name[TestDataPrefix.Length..], memory.ToArray());
        }

        // A file of each format written by the library; the files of 4K pages grow by 16 MB, so the empty space is removed
        foreach (var format in Enum.GetValues<PstFormat>())
        {
            using var stream = new MemoryStream();
            using (var writer = PstWriter.Create(stream, new PstWriterOptions { Format = format, StoreId = Guid.Empty, Now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }, leaveOpen: true))
            {
                var message = new MapiMessageBuilder { Subject = "Hello", Body = "Body" };
                message.Recipients.Add(new MapiRecipientBuilder("Bob", "bob@example.com"));
                message.Attachments.Add(MapiAttachmentBuilder.FromContent("a.txt", "abc"u8.ToArray()));
                writer.RootFolder.CreateFolder("Inbox").AddItem(message);
            }

            var bytes = stream.ToArray();
            var length = bytes.AsSpan().LastIndexOfAnyExcept((byte)0) + 1;
            yield return new FuzzSeed("writer-" + format, bytes.AsSpan(0, length).ToArray());
        }
    }

    private static IEnumerable<FuzzSeed> GetWriteSeeds()
    {
        yield return new FuzzSeed("empty", new byte[] { 1, 1 });
        yield return new FuzzSeed("folders-messages", (byte[])[1, 1, 0, 0, .. "Inbox"u8, 0, 1, 1, .. "Hello"u8, 0, .. "Body"u8, 0, 2, 1, .. "Files"u8, 0, 1, 2, 3, 0xFF, 3, 1, .. "Bob"u8, 0, 4, 1, .. "Meeting"u8, 0, 7]);
        yield return new FuzzSeed("ansi-cyclic", (byte[])[0, 2, 0, 0, .. "Inbox"u8, 0, 1, 1, .. "Hello"u8, 0, .. "Body"u8, 0]);
        yield return new FuzzSeed("4k", (byte[])[2, 0, 1, 0, .. "Hello"u8, 0, .. "Body"u8, 0]);
    }

    private static IEnumerable<FuzzSeed> GetLzfuSeeds()
    {
        yield return new FuzzSeed("example1", Convert.FromHexString("2D0000002B0000004C5A4675F1C5C7A703000A007263706731323542320AF32068656C090020627705B06C647D0A800FA0"));
        yield return new FuzzSeed("example2", Convert.FromHexString("1A0000001C0000004C5A4675E2D44B51410004205758595A0D6E7D010EB0"));
        yield return new FuzzSeed("stored", Lzfu.Store(Encoding.ASCII.GetBytes("{\\rtf1 Hello}")));
    }

    private static IEnumerable<FuzzSeed> GetRecurrenceSeeds()
    {
        var pattern = new MapiRecurrencePattern
        {
            Frequency = MapiRecurrenceFrequency.Weekly,
            PatternType = MapiRecurrencePatternType.Week,
            DaysOfWeek = MapiDaysOfWeek.Monday,
            StartDate = new DateTime(2026, 1, 5),
            StartTimeOffset = TimeSpan.FromHours(9),
            EndTimeOffset = TimeSpan.FromHours(10),
        };
        pattern.DeletedInstanceDates.Add(new DateTime(2026, 1, 12));
        pattern.ModifiedInstanceDates.Add(new DateTime(2026, 1, 12));
        pattern.Exceptions.Add(new MapiRecurrenceException { OriginalStartDate = new DateTime(2026, 1, 12, 9, 0, 0), StartDateTime = new DateTime(2026, 1, 12, 11, 0, 0), EndDateTime = new DateTime(2026, 1, 12, 12, 0, 0), Subject = "Moved", Location = "Room", BusyStatus = MapiBusyStatus.Free });
        yield return new FuzzSeed("weekly", pattern.ToByteArray());
        yield return new FuzzSeed("monthly-nth", new MapiRecurrencePattern { Frequency = MapiRecurrenceFrequency.Monthly, PatternType = MapiRecurrencePatternType.MonthNth, DaysOfWeek = MapiDaysOfWeek.Friday, WeekOfMonth = 5 }.ToByteArray());
        yield return new FuzzSeed("task", new MapiRecurrencePattern { Frequency = MapiRecurrenceFrequency.Daily, PatternType = MapiRecurrencePatternType.Day, Period = 1440 }.ToByteArray(appointment: false));
    }

    private static string BinaryToken(byte[] bytes) => string.Concat(bytes.Select(b => b < 0x80 ? ((char)b).ToString() : ((char)(0xDC00 + b)).ToString()));
}
