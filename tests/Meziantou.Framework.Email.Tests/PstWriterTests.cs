using System.Text;

namespace Meziantou.Framework.Email.Tests;

public sealed class PstWriterTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 10, 30, 0, DateTimeKind.Utc);

    public static TheoryData<PstFormat, PstEncryption> Formats
    {
        get
        {
            var data = new TheoryData<PstFormat, PstEncryption>();
            foreach (var format in Enum.GetValues<PstFormat>())
            {
                foreach (var encryption in Enum.GetValues<PstEncryption>())
                {
                    data.Add(format, encryption);
                }
            }

            return data;
        }
    }

    [Fact]
    public void Create_Snippet()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pst");
        try
        {
            #region PstWriter_Create
            using (var writer = PstWriter.Create(path, new PstWriterOptions { DisplayName = "Archive" }))
            {
                var inbox = writer.RootFolder.CreateFolder("Inbox", "IPF.Note");
                var message = new MapiMessageBuilder
                {
                    Subject = "Hello",
                    Body = "Hello from Meziantou.Framework.Email",
                    SenderName = "Alice",
                    SenderEmailAddress = "alice@example.com",
                    SenderAddressType = "SMTP",
                    ClientSubmitTime = new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc),
                };
                message.Recipients.Add(new MapiRecipientBuilder("Bob", "bob@example.com"));
                message.Attachments.Add(MapiAttachmentBuilder.FromContent("notes.txt", "Some notes"u8.ToArray(), "text/plain"));
                inbox.AddItem(message);

                var contacts = writer.RootFolder.CreateFolder("Contacts", "IPF.Contact");
                contacts.AddItem(new MapiContactBuilder { GivenName = "Bob", Surname = "Smith", Email1Address = "bob@example.com" });
            }

            using var file = PstFile.Open(path);
            var item = file.GetFolder("Inbox")!.EnumerateItems().Single();
            Assert.Equal("Hello", item.Subject);
            Assert.Equal("bob@example.com", item.Recipients.Single().EmailAddress);
            Assert.Equal("notes.txt", item.Attachments.Single().Name);
            #endregion
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Copy_Snippet()
    {
        var sourcePath = PstOracleTests.GetPath("tika-variousBodyTypes.pst");
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pst");
        try
        {
            #region PstMessageBuilder_FromMessage
            // Copy the messages of a folder to a new file: properties, recipients, attachments and embedded messages
            using (var source = PstFile.Open(sourcePath))
            using (var writer = PstWriter.Create(path))
            {
                var destination = writer.RootFolder.CreateFolder("Imported", "IPF.Note");
                foreach (var message in source.GetFolder("Inbox/tmp")!.EnumerateItems())
                {
                    destination.AddItem(MapiMessageBuilder.FromMessage(message));
                }
            }
            #endregion

            using var copy = PstFile.Open(path);
            Assert.Equal(4, copy.GetFolder("Imported")!.ContentCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Note_RoundTrip()
    {
        using var stream = new MemoryStream();

        #region MapiNoteBuilder_Usage
        using (var writer = PstWriter.Create(stream, leaveOpen: true))
        {
            var notes = writer.RootFolder.CreateFolder("Notes", "IPF.StickyNote");
            notes.AddItem(new MapiNoteBuilder
            {
                Text = "Call Bob\r\nAbout the quarterly report",
                Color = MapiNoteColor.Green,
                Width = 200,
                Height = 166,
            });
        }

        using var file = PstFile.Open(stream);
        var note = Assert.IsType<MapiNote>(file.GetFolder("Notes")!.EnumerateItems().Single());
        Assert.Equal("Call Bob", note.Subject);
        Assert.Equal("Call Bob\r\nAbout the quarterly report", note.Body);
        Assert.Equal(MapiNoteColor.Green, note.Color);
        Assert.Equal(200, note.Width);
        #endregion

        Assert.Equal(166, note.Height);
        Assert.Null(note.Left);
        Assert.Empty(file.Validate());

        // An .msg file of the note, and its copy
        var bytes = MsgFile.ToByteArray(MapiMessageBuilder.FromMessage(note));
        var copy = Assert.IsType<MapiNote>(MsgFile.Load(bytes));
        Assert.Equal("IPM.StickyNote", copy.MessageClass);
        Assert.Equal("Call Bob", copy.Subject);
        Assert.Equal(MapiNoteColor.Green, copy.Color);
        Assert.Equal(166, copy.Height);
    }

    [Fact]
    public void Journal_RoundTrip()
    {
        #region MapiJournalBuilder_Usage
        var entry = new MapiJournalBuilder
        {
            Subject = "Call with Bob",
            JournalType = "Phone call",
            JournalTypeDescription = "Phone call",
            Start = new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc),
            End = new DateTime(2026, 3, 2, 9, 25, 0, DateTimeKind.Utc),
            Companies = ["Contoso"],
            Contacts = ["Bob Smith"],
        };
        Assert.Equal(TimeSpan.FromMinutes(25), entry.Duration);

        var item = Assert.IsType<MapiJournal>(MsgFile.Load(MsgFile.ToByteArray(entry)));
        Assert.Equal("Phone call", item.JournalType);
        Assert.Equal(TimeSpan.FromMinutes(25), item.Duration);
        Assert.Equal(["Contoso"], item.Companies);
        #endregion

        Assert.Equal(new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc), item.Start);
        Assert.Equal(new DateTime(2026, 3, 2, 9, 25, 0, DateTimeKind.Utc), item.End);
        Assert.Equal(["Bob Smith"], item.Contacts);
        Assert.False(item.DocumentPrinted);

        entry.DocumentSaved = true;
        var saved = Assert.IsType<MapiJournal>(MsgFile.Load(MsgFile.ToByteArray(entry)));
        Assert.Equal("IPM.Activity", saved.MessageClass);
        Assert.Equal("Phone call", saved.JournalTypeDescription);
        Assert.True(saved.DocumentSaved);
        Assert.False(saved.DocumentPrinted);

        // An end before the start has no duration
        entry.End = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Null(entry.Duration);
    }

    [Fact]
    public void WriteTypedItems_Snippet()
    {
        using var stream = new MemoryStream();

        #region PstWriter_TypedItems
        using (var writer = PstWriter.Create(stream, leaveOpen: true))
        {
            var calendar = writer.RootFolder.CreateFolder("Calendar", "IPF.Appointment");
            calendar.AddItem(new MapiAppointmentBuilder
            {
                Subject = "Team meeting",
                Start = new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc),
                End = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc),
                Location = "Room 1",
                Reminder = TimeSpan.FromMinutes(15),
                Recurrence = new MapiRecurrencePattern
                {
                    Frequency = MapiRecurrenceFrequency.Weekly,
                    PatternType = MapiRecurrencePatternType.Week,
                    DaysOfWeek = MapiDaysOfWeek.Monday,
                    StartDate = new DateTime(2026, 3, 2),
                    StartTimeOffset = TimeSpan.FromHours(9),
                    EndTimeOffset = TimeSpan.FromHours(10),
                },
            });

            var contacts = writer.RootFolder.CreateFolder("Contacts", "IPF.Contact");
            contacts.AddItem(new MapiContactBuilder { GivenName = "Bob", Surname = "Smith", Email1Address = "bob@example.com", MobileTelephoneNumber = "+1 555 0100" });
            var team = new MapiDistributionListBuilder { Name = "Team" };
            team.Members.Add(new MapiDistributionListMember { DisplayName = "Bob", EmailAddress = "bob@example.com" });
            contacts.AddItem(team);

            var tasks = writer.RootFolder.CreateFolder("Tasks", "IPF.Task");
            tasks.AddItem(new MapiTaskBuilder { Subject = "Review", Status = MapiTaskStatus.InProgress, PercentComplete = 0.25, DueDate = new DateTime(2026, 3, 6, 0, 0, 0, DateTimeKind.Utc) });
        }
        #endregion

        using var file = PstFile.Open(stream.ToArray());
        Assert.Equal("Team meeting", file.GetFolder("Calendar")!.EnumerateItems().Single().Subject);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void Write_ReadBack(PstFormat format, PstEncryption encryption)
    {
        var bytes = WriteSample(format, encryption);
        using var file = PstFile.Open(bytes);
        Assert.Equal(format, file.Format);
        Assert.Equal(encryption, file.Encryption);
        Assert.Equal("Sample store", file.Store.DisplayName);
        Assert.Empty(file.Validate());
        Assert.Equal("Top of Personal Folders", file.Store.IpmSubtree?.DisplayName);
        Assert.Equal("Deleted Items", file.Store.DeletedItems?.DisplayName);

        var inbox = file.GetFolder("Inbox");
        Assert.NotNull(inbox);
        Assert.Equal("IPF.Note", inbox.ContainerClass);
        Assert.Equal(3, inbox.ContentCount);
        Assert.Equal(1, inbox.UnreadCount);
        var messages = inbox.EnumerateItems().ToList();
        Assert.HasCount(3, messages);

        var first = messages[0];
        Assert.Equal("First message", first.Subject);
        Assert.Equal("Hello world", first.Body);
        Assert.Equal("<p>Hello <b>world</b></p>", first.BodyHtml);
        Assert.Equal(@"{\rtf1\ansi Hello world}", Encoding.ASCII.GetString(first.GetRtfBody()!));
        Assert.Equal("Alice", first.SenderName);
        Assert.Equal("alice@example.com", first.SenderEmailAddress);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), first.ClientSubmitTime);
        Assert.Equal(MapiImportance.High, first.Importance);
        Assert.Equal("<1@example.com>", first.InternetMessageId);
        Assert.Equal(["Blue", "Red"], first.Categories);
        Assert.Equal("Bob; Carol", first.DisplayTo);
        Assert.Equal("Dave", first.DisplayCc);
        Assert.True(first.IsRead);
        Assert.Equal(3, first.Recipients.Count);
        Assert.Equal(MapiRecipientType.Cc, first.Recipients[2].RecipientType);
        Assert.Equal("dave@example.com", first.Recipients[2].SmtpAddress);

        Assert.Equal(3, first.Attachments.Count);
        var text = first.Attachments[0];
        Assert.Equal("hello.txt", text.Name);
        Assert.Equal(".txt", text.Extension);
        Assert.Equal("text/plain", text.MimeType);
        Assert.Equal(MapiAttachmentMethod.ByValue, text.Method);
        Assert.Equal("Hello attachment"u8.ToArray(), text.GetContent());

        var large = first.Attachments[1];
        Assert.Equal(LargeContent(), large.GetContent());
        using (var stream = large.OpenRead())
        using (var copy = new MemoryStream())
        {
            stream.CopyTo(copy);
            Assert.Equal(LargeContent(), copy.ToArray());
        }

        var embedded = first.Attachments[2].GetEmbeddedMessage();
        Assert.NotNull(embedded);
        Assert.True(embedded.IsEmbedded);
        Assert.Equal("Embedded message", embedded.Subject);
        Assert.Equal("inner.bin", embedded.Attachments.Single().Name);
        Assert.Equal(new byte[] { 1, 2, 3 }, embedded.Attachments.Single().GetContent());

        Assert.Equal("Unread message", messages[1].Subject);
        Assert.False(messages[1].IsRead);
        Assert.Equal(new string('x', 10_000), messages[2].Body);

        var custom = messages[0].Properties;
        Assert.Equal(42, custom.GetValueOrDefault<int>(new MapiNamedProperty(MapiPropertySets.PublicStrings, "x-custom")));
        Assert.Equal(new[] { 1L, 2L }, custom.GetValueOrDefault<long[]>(0x6701));

        Assert.Equal("Archive", inbox.Folders.Single().DisplayName);
        Assert.Single(inbox.EnumerateAssociatedItems());

        var contact = Assert.IsType<MapiContact>(file.GetFolder("Contacts")!.EnumerateItems().Single(item => item is MapiContact));
        Assert.Equal("Bob", contact.GivenName);
        Assert.Equal("Smith", contact.Surname);
        Assert.Equal("Bob Smith", contact.DisplayName);
        Assert.Equal("Smith, Bob", contact.FileAs);
        Assert.Equal("bob@example.com", contact.Email1Address);
        Assert.Equal("Contoso", contact.CompanyName);
        Assert.Equal("+1 555 0100", contact.MobileTelephoneNumber);
        Assert.Equal("Paris", contact.HomeAddress.City);
        Assert.Equal("Seattle", contact.BusinessAddress.City);
        Assert.Equal(new DateTime(1990, 5, 6, 0, 0, 0, DateTimeKind.Utc), contact.Birthday);

        var list = Assert.IsType<MapiDistributionList>(file.GetFolder("Contacts")!.EnumerateItems().Single(item => item is MapiDistributionList));
        Assert.Equal("Team", list.Name);
        Assert.Equal(["bob@example.com", "carol@example.com"], list.Members.Select(member => member.EmailAddress));
        Assert.Equal(["Bob", "Carol"], list.Members.Select(member => member.DisplayName));

        var appointment = Assert.IsType<MapiAppointment>(file.GetFolder("Calendar")!.EnumerateItems().Single());
        Assert.Equal("Weekly meeting", appointment.Subject);
        Assert.Equal(new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc), appointment.Start);
        Assert.Equal(new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc), appointment.End);
        Assert.Equal(TimeSpan.FromHours(1), appointment.Duration);
        Assert.Equal("Room 1", appointment.Location);
        Assert.Equal(MapiBusyStatus.Tentative, appointment.BusyStatus);
        Assert.True(appointment.ReminderSet);
        Assert.Equal(TimeSpan.FromMinutes(15), appointment.ReminderDelta);
        Assert.True(appointment.IsRecurring);
        var recurrence = appointment.GetRecurrence()!;
        Assert.Equal(MapiRecurrenceFrequency.Weekly, recurrence.Frequency);
        Assert.Equal(MapiDaysOfWeek.Monday | MapiDaysOfWeek.Thursday, recurrence.DaysOfWeek);
        Assert.Equal(MapiRecurrenceEndType.EndAfterOccurrences, recurrence.EndType);
        Assert.Equal(8, recurrence.OccurrenceCount);
        Assert.Equal(TimeSpan.FromHours(9), recurrence.StartTimeOffset);
        Assert.Equal("Moved meeting", recurrence.Exceptions.Single().Subject);
        Assert.Equal("Room 2", recurrence.Exceptions.Single().Location);

        var task = Assert.IsType<MapiTask>(file.GetFolder("Tasks")!.EnumerateItems().Single());
        Assert.Equal("Write the documentation", task.Subject);
        Assert.Equal(MapiTaskStatus.InProgress, task.Status);
        Assert.Equal(0.5, task.PercentComplete);
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), task.DueDate);
        Assert.Equal("Alice", task.Owner);
        Assert.Equal(TimeSpan.FromHours(2), task.EstimatedEffort);
        Assert.False(task.IsComplete);

        var many = file.GetFolder("Many");
        Assert.NotNull(many);
        Assert.HasCount(300, many.EnumerateItems());
        Assert.Equal(Enumerable.Range(0, 300).Select(i => "Message " + i), many.EnumerateItems().Select(item => item.Subject));
        Assert.Equal(150, many.Folders.Count);
    }

    public static TheoryData<string, PstFormat, PstEncryption> CommittedSamples => new()
    {
        { "writer-unicode-permute.pst", PstFormat.Unicode, PstEncryption.Permute },
        { "writer-unicode-cyclic.pst", PstFormat.Unicode, PstEncryption.Cyclic },
        { "writer-ansi-small.pst", PstFormat.Ansi, PstEncryption.Permute },
    };

    /// <summary>
    /// The files of the writer in TestData are the input of the oracle (expected.json): they must be the files the writer writes.
    /// Set the PST_UPDATE_TESTDATA environment variable to update them, then run TestData/Pst/Oracle/dump.mjs.
    /// </summary>
    [Theory]
    [MemberData(nameof(CommittedSamples))]
    public void WriterSample_MatchesCommittedFile(string fileName, PstFormat format, PstEncryption encryption)
    {
        // pst-extractor reads the tables of an ANSI file only when they fit in one block: the ANSI sample is small
        var bytes = format == PstFormat.Ansi ? WriteSmallSample(format, encryption) : WriteSample(format, encryption);
        if (Environment.GetEnvironmentVariable("PST_UPDATE_TESTDATA") is "1" or "true")
        {
            File.WriteAllBytes(Path.Combine(GetSourceDirectory(), "TestData", "Pst", fileName), bytes);
        }

        Assert.Equal(File.ReadAllBytes(PstOracleTests.GetPath(fileName)), bytes);
    }

    private static string GetSourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    [Fact]
    public void Write_IsDeterministic()
    {
        Assert.Equal(WriteSample(PstFormat.Unicode, PstEncryption.Permute), WriteSample(PstFormat.Unicode, PstEncryption.Permute));
    }

    [Theory]
    [MemberData(nameof(PstOracleTests.Files), MemberType = typeof(PstOracleTests))]
    public void Copy_MatchesOracle(string fileName)
    {
        using var source = PstFile.Open(PstOracleTests.GetPath(fileName));
        using var stream = new MemoryStream();
        using (var writer = PstWriter.Create(stream, new PstWriterOptions { DisplayName = source.Store.DisplayName ?? "", StoreId = Guid.Empty, Now = Now }, leaveOpen: true))
        {
            CopyFolder(source.Store.IpmSubtree ?? source.RootFolder, writer.RootFolder);
        }

        using var copy = PstFile.Open(stream.ToArray());
        Assert.Empty(copy.Validate());
        var expected = DumpItems(source.Store.IpmSubtree ?? source.RootFolder);
        var actual = DumpItems(copy.RootFolder.Folders.First(folder => folder.Parent?.Parent is null && folder.DisplayName == copy.Store.IpmSubtree?.DisplayName));
        Assert.Equal(expected, actual);
    }

    private static void CopyFolder(PstFolder source, PstWriterFolder destination)
    {
        foreach (var item in source.EnumerateItems())
        {
            destination.AddItem(MapiMessageBuilder.FromMessage(item));
        }

        foreach (var child in source.Folders)
        {
            if (child.IsSearchFolder || child.DisplayName == "Deleted Items")
            {
                continue;
            }

            CopyFolder(child, destination.CreateFolder(child.DisplayName ?? "", child.ContainerClass));
        }
    }

    private static List<string> DumpItems(PstFolder folder)
    {
        var result = new List<string>();
        foreach (var item in folder.EnumerateItems())
        {
            result.Add($"{folder.DisplayName}: {item.GetType().Name} {item.MessageClass} {item.Subject} {item.SenderName} {item.Body?.Length} {item.BodyHtml?.Length} {item.HasRtfBody} {item.Recipients.Count} {item.Attachments.Count} {item.ClientSubmitTime:O}");
            foreach (var attachment in item.Attachments)
            {
                result.Add($"  {attachment.Name} {attachment.Method} {attachment.GetContent().Length} {attachment.GetEmbeddedMessage()?.MessageClass}");
            }

            if (item is MapiAppointment appointment)
            {
                result.Add($"  {appointment.Start:O} {appointment.End:O} {appointment.GetRecurrence()?.Exceptions.Count}");
            }

            if (item is MapiDistributionList list)
            {
                result.Add("  " + string.Join(", ", list.Members));
            }
        }

        foreach (var child in folder.Folders)
        {
            if (!child.IsSearchFolder && child.DisplayName != "Deleted Items")
            {
                result.AddRange(DumpItems(child));
            }
        }

        return result;
    }

    private static byte[] LargeContent() => [.. Enumerable.Range(0, 100_000).Select(i => (byte)(i * 7))];

    internal static byte[] WriteSmallSample(PstFormat format, PstEncryption encryption)
    {
        using var stream = new MemoryStream();
        using (var writer = PstWriter.Create(stream, new PstWriterOptions { Format = format, Encryption = encryption, DisplayName = "Small store", StoreId = new Guid("6a1c0f1e-5b8e-4c55-8a71-2f0b9b5f4d21"), Now = Now }, leaveOpen: true))
        {
            var inbox = writer.RootFolder.CreateFolder("Inbox", "IPF.Note");
            var message = new MapiMessageBuilder { Subject = "Hello é", Body = "Body text", BodyHtml = "<b>Hello</b>", SenderName = "Alice", SenderEmailAddress = "alice@example.com", ClientSubmitTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc) };
            message.Recipients.Add(new MapiRecipientBuilder("Bob", "bob@example.com"));
            message.Attachments.Add(MapiAttachmentBuilder.FromContent("a.txt", "abc"u8.ToArray(), "text/plain"));
            message.Attachments.Add(MapiAttachmentBuilder.FromMessage(new MapiMessageBuilder { Subject = "Inner" }));
            inbox.AddItem(message);
            writer.RootFolder.CreateFolder("Contacts", "IPF.Contact").AddItem(new MapiContactBuilder { GivenName = "Bob", Surname = "Smith", Email1Address = "bob@example.com", CompanyName = "Contoso" });
            writer.RootFolder.CreateFolder("Calendar", "IPF.Appointment").AddItem(new MapiAppointmentBuilder { Subject = "Meeting", Start = new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc), End = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc), Location = "Room" });
            writer.RootFolder.CreateFolder("Tasks", "IPF.Task").AddItem(new MapiTaskBuilder { Subject = "Task", Status = MapiTaskStatus.InProgress, PercentComplete = 0.5, Owner = "Alice" });

            // pst-extractor skips the last entry of the name-to-ID map: the last named property is one it does not read
            var note = new MapiMessageBuilder("IPM.StickyNote") { Subject = "Note" };
            note.Properties.Set(new MapiNamedProperty(MapiPropertySets.PublicStrings, "x-last"), MapiPropertyType.Unicode, "last");
            writer.RootFolder.CreateFolder("Notes", "IPF.StickyNote").AddItem(note);
        }

        return stream.ToArray();
    }

    internal static byte[] WriteSample(PstFormat format, PstEncryption encryption)
    {
        using var stream = new MemoryStream();
        using (var writer = PstWriter.Create(stream, new PstWriterOptions { Format = format, Encryption = encryption, DisplayName = "Sample store", StoreId = new Guid("0f9e9a35-6cbb-4d2a-9b5d-3b8f9f3c6e11"), Now = Now }, leaveOpen: true))
        {
            var inbox = writer.RootFolder.CreateFolder("Inbox", "IPF.Note");
            var message = new MapiMessageBuilder
            {
                Subject = "First message",
                Body = "Hello world",
                BodyHtml = "<p>Hello <b>world</b></p>",
                SenderName = "Alice",
                SenderEmailAddress = "alice@example.com",
                SenderAddressType = "SMTP",
                ClientSubmitTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                Importance = MapiImportance.High,
                InternetMessageId = "<1@example.com>",
                Categories = ["Blue", "Red"],
            };
            message.SetRtfBody(@"{\rtf1\ansi Hello world}"u8);
            message.Recipients.Add(new MapiRecipientBuilder("Bob", "bob@example.com"));
            message.Recipients.Add(new MapiRecipientBuilder("Carol", "carol@example.com"));
            message.Recipients.Add(new MapiRecipientBuilder("Dave", "dave@example.com", MapiRecipientType.Cc));
            message.Attachments.Add(MapiAttachmentBuilder.FromContent("hello.txt", "Hello attachment"u8.ToArray(), "text/plain"));
            var large = new MapiAttachmentBuilder { FileName = "large.bin" };
            large.SetContent(new MemoryStream(LargeContent()));
            message.Attachments.Add(large);
            var embedded = new MapiMessageBuilder { Subject = "Embedded message", Body = "Inner" };
            embedded.Attachments.Add(MapiAttachmentBuilder.FromContent("inner.bin", [1, 2, 3]));
            message.Attachments.Add(MapiAttachmentBuilder.FromMessage(embedded));
            message.Properties.Set(new MapiNamedProperty(MapiPropertySets.PublicStrings, "x-custom"), MapiPropertyType.Integer32, 42);
            message.Properties.Set(new MapiPropertyTag(0x6701, MapiPropertyType.MultipleInteger64), new[] { 1L, 2L });
            inbox.AddItem(message);
            inbox.AddItem(new MapiMessageBuilder { Subject = "Unread message", Flags = MapiMessageFlags.None });
            inbox.AddItem(new MapiMessageBuilder { Subject = "Long body", Body = new string('x', 10_000) });
            inbox.AddItem(new MapiMessageBuilder("IPM.Microsoft.FolderDesign.NamedView") { IsAssociated = true, Subject = "View" });
            inbox.CreateFolder("Archive");

            var contacts = writer.RootFolder.CreateFolder("Contacts", "IPF.Contact");
            var contact = new MapiContactBuilder { GivenName = "Bob", Surname = "Smith", Email1Address = "bob@example.com", CompanyName = "Contoso", MobileTelephoneNumber = "+1 555 0100", Birthday = new DateTime(1990, 5, 6, 0, 0, 0, DateTimeKind.Utc) };
            contact.SetHomeAddress(new MapiPostalAddress { City = "Paris", Country = "France" });
            contact.SetBusinessAddress(new MapiPostalAddress { City = "Seattle" });
            contacts.AddItem(contact);
            var list = new MapiDistributionListBuilder { Name = "Team" };
            list.Members.Add(new MapiDistributionListMember { DisplayName = "Bob", EmailAddress = "bob@example.com" });
            list.Members.Add(new MapiDistributionListMember { DisplayName = "Carol", EmailAddress = "carol@example.com" });
            contacts.AddItem(list);

            var calendar = writer.RootFolder.CreateFolder("Calendar", "IPF.Appointment");
            var recurrence = new MapiRecurrencePattern
            {
                Frequency = MapiRecurrenceFrequency.Weekly,
                PatternType = MapiRecurrencePatternType.Week,
                DaysOfWeek = MapiDaysOfWeek.Monday | MapiDaysOfWeek.Thursday,
                EndType = MapiRecurrenceEndType.EndAfterOccurrences,
                OccurrenceCount = 8,
                StartDate = new DateTime(2026, 3, 2),
                EndDate = new DateTime(2026, 3, 26),
                StartTimeOffset = TimeSpan.FromHours(9),
                EndTimeOffset = TimeSpan.FromHours(10),
            };
            recurrence.DeletedInstanceDates.Add(new DateTime(2026, 3, 5));
            recurrence.ModifiedInstanceDates.Add(new DateTime(2026, 3, 5));
            recurrence.Exceptions.Add(new MapiRecurrenceException
            {
                OriginalStartDate = new DateTime(2026, 3, 5, 9, 0, 0),
                StartDateTime = new DateTime(2026, 3, 5, 14, 0, 0),
                EndDateTime = new DateTime(2026, 3, 5, 15, 0, 0),
                Subject = "Moved meeting",
                Location = "Room 2",
            });
            calendar.AddItem(new MapiAppointmentBuilder
            {
                Subject = "Weekly meeting",
                Start = new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc),
                End = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc),
                Location = "Room 1",
                BusyStatus = MapiBusyStatus.Tentative,
                Reminder = TimeSpan.FromMinutes(15),
                Recurrence = recurrence,
            });

            var tasks = writer.RootFolder.CreateFolder("Tasks", "IPF.Task");
            tasks.AddItem(new MapiTaskBuilder
            {
                Subject = "Write the documentation",
                Status = MapiTaskStatus.InProgress,
                PercentComplete = 0.5,
                DueDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                Owner = "Alice",
                EstimatedEffort = TimeSpan.FromHours(2),
            });

            // Enough items and folders for the tables to need a row matrix in a subnode and several heap blocks
            var many = writer.RootFolder.CreateFolder("Many");
            for (var i = 0; i < 300; i++)
            {
                many.AddItem(new MapiMessageBuilder { Subject = "Message " + i });
            }

            for (var i = 0; i < 150; i++)
            {
                many.CreateFolder("Folder " + i);
            }
        }

        return stream.ToArray();
    }
}
