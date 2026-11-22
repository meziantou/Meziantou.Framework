---
title: Writing files
description: Create a file with folders, emails, attachments, contacts, appointments and tasks, or copy items from another file.
---

[PstWriter.Create](cref:Meziantou.Framework.Email.Storage.PstWriter.Create) creates a file in a path or in a seekable stream.
[RootFolder](cref:Meziantou.Framework.Email.Storage.PstWriter.RootFolder) is the top of the folder hierarchy, where
[CreateFolder](cref:Meziantou.Framework.Email.Storage.PstWriterFolder.CreateFolder) creates the folders, and
[AddItem](cref:Meziantou.Framework.Email.Storage.PstWriterFolder.AddItem) writes an item described by a
[MapiMessageBuilder](cref:Meziantou.Framework.Email.Mapi.MapiMessageBuilder):

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/PstWriterTests.cs;region=PstWriter_Create;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

The items are written when they are added. The folders, the indexes and the header are written by
[Complete](cref:Meziantou.Framework.Email.Storage.PstWriter.Complete), which disposing the writer calls. A content set with
[SetContent](cref:Meziantou.Framework.Email.Mapi.MapiAttachmentBuilder.SetContent) from a stream is read block by block, so a large
attachment is not loaded in memory.

## Contacts, appointments and tasks

[MapiContactBuilder](cref:Meziantou.Framework.Email.Mapi.MapiContactBuilder),
[MapiDistributionListBuilder](cref:Meziantou.Framework.Email.Mapi.MapiDistributionListBuilder),
[MapiAppointmentBuilder](cref:Meziantou.Framework.Email.Mapi.MapiAppointmentBuilder) and
[MapiTaskBuilder](cref:Meziantou.Framework.Email.Mapi.MapiTaskBuilder) set the properties of the items Outlook types. Put them in
folders of the matching class (`IPF.Contact`, `IPF.Appointment`, `IPF.Task`), where Outlook shows them:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/PstWriterTests.cs;region=PstWriter_TypedItems;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

[Properties](cref:Meziantou.Framework.Email.Mapi.MapiMessageBuilder.Properties) sets any other property, tagged or named.

## Copying items

The writer creates new files: it does not modify an existing file.
[MapiMessageBuilder.FromMessage](cref:Meziantou.Framework.Email.Mapi.MapiMessageBuilder.FromMessage) copies an item read from a file,
with its properties, its recipients and its attachments, including the embedded messages, to write it to another file:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/PstWriterTests.cs;region=PstMessageBuilder_FromMessage;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

The large attachments are read from the source when the item is written, so the source must stay open until then.

## Options

[PstWriterOptions](cref:Meziantou.Framework.Email.Storage.PstWriterOptions) sets the format and the encryption of the file, the
display name of the store, and the code page of the strings of an ANSI file. Set
[StoreId](cref:Meziantou.Framework.Email.Storage.PstWriterOptions.StoreId) and [Now](cref:Meziantou.Framework.Email.Storage.PstWriterOptions.Now)
to write the same bytes from the same content.

## Compatibility

The tests of the library check the files it writes with its own reader, with
[PstFile.Validate](cref:Meziantou.Framework.Email.Storage.PstFile.Validate), and with an independent reader (pst-extractor). The
structures follow [MS-PST], and the parts it does not describe, such as the positions of the allocation maps, follow the
files Outlook writes. Open a file in Outlook, or check it with the Inbox Repair Tool (`scanpst.exe`), before relying on it
for an important migration.
