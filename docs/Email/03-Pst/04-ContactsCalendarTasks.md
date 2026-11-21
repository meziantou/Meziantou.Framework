---
title: Contacts, calendar and tasks
description: Read contacts, distribution lists, appointments with their recurrence and exceptions, tasks, sticky notes and journal entries.
---

The items that Outlook types are returned as derived types of [MapiMessage](cref:Meziantou.Framework.Email.Mapi.MapiMessage), chosen
from their message class:

| Message class | Type |
| --- | --- |
| `IPM.Contact` | [MapiContact](cref:Meziantou.Framework.Email.Mapi.MapiContact) |
| `IPM.DistList` | [MapiDistributionList](cref:Meziantou.Framework.Email.Mapi.MapiDistributionList) |
| `IPM.Appointment` | [MapiAppointment](cref:Meziantou.Framework.Email.Mapi.MapiAppointment) |
| `IPM.Schedule.Meeting.Request`, `.Canceled`, `.Resp.Pos`, `.Resp.Tent`, `.Resp.Neg` | [MapiMeetingMessage](cref:Meziantou.Framework.Email.Mapi.MapiMeetingMessage), an appointment: see [meeting invitations](../08-ContactsAndCalendars/02-ICalendar.md) |
| `IPM.Task` | [MapiTask](cref:Meziantou.Framework.Email.Mapi.MapiTask) |
| `IPM.StickyNote` | [MapiNote](cref:Meziantou.Framework.Email.Mapi.MapiNote) |
| `IPM.Activity` | [MapiJournal](cref:Meziantou.Framework.Email.Mapi.MapiJournal) |
| any other class, such as `IPM.Note` or `IPM.Post` | [MapiMessage](cref:Meziantou.Framework.Email.Mapi.MapiMessage) |

The classes derived from these ones (`IPM.Contact.MyForm`) get the same types. Every property, typed or not, is available
from [Properties](cref:Meziantou.Framework.Email.Mapi.MapiMessage.Properties): see [MAPI properties](05-Properties.md).

## Contacts and distribution lists

A [MapiContact](cref:Meziantou.Framework.Email.Mapi.MapiContact) gives the names, the email addresses, the telephone numbers and the
postal addresses of a contact. The [Members](cref:Meziantou.Framework.Email.Mapi.MapiDistributionList.Members) of a
[MapiDistributionList](cref:Meziantou.Framework.Email.Mapi.MapiDistributionList) are its addresses:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/PstFileTests.cs;region=PstContact_Read;format=md-fenced-code-block;dedent=true -->
```csharp
using var file = PstFile.Open(path);
foreach (var item in file.GetFolder("Contacts")!.EnumerateItems())
{
    switch (item)
    {
        case MapiContact contact:
            Assert.Equal("contact", contact.GivenName);
            Assert.Equal("contact1@rjohnson.id.au", contact.Email1Address);
            break;

        case MapiDistributionList list:
            Assert.Equal("test dist list", list.Name);
            Assert.Equal("dist1@rjohnson.id.au", list.Members[1].EmailAddress);
            break;
    }
}
```
<!-- endref -->

## Appointments

A [MapiAppointment](cref:Meziantou.Framework.Email.Mapi.MapiAppointment) gives the
[Start](cref:Meziantou.Framework.Email.Mapi.MapiAppointment.Start) and the [End](cref:Meziantou.Framework.Email.Mapi.MapiAppointment.End) of an
appointment, in UTC, its location, its attendees and its reminder.
[GetRecurrence](cref:Meziantou.Framework.Email.Mapi.MapiAppointment.GetRecurrence) returns the recurrence of a recurring appointment:
its pattern, its end, and the occurrences that are deleted or modified
([Exceptions](cref:Meziantou.Framework.Email.Mapi.MapiRecurrencePattern.Exceptions)):

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/PstFileTests.cs;region=PstAppointment_Read;format=md-fenced-code-block;dedent=true -->
```csharp
using var file = PstFile.Open(path);
var appointment = file.GetFolder("Calendar")!.EnumerateItems().OfType<MapiAppointment>().Single();
Assert.Equal("Test appointment", appointment.Subject);
Assert.Equal(new DateTime(2016, 8, 2, 15, 0, 0, DateTimeKind.Utc), appointment.Start);

// The recurrence: every Tuesday, with deleted and moved occurrences
var recurrence = appointment.GetRecurrence()!;
Assert.Equal(MapiRecurrenceFrequency.Weekly, recurrence.Frequency);
Assert.Equal(MapiDaysOfWeek.Tuesday, recurrence.DaysOfWeek);
Assert.HasCount(3, recurrence.DeletedInstanceDates);
Assert.HasCount(2, recurrence.Exceptions);
Assert.Equal(new DateTime(2016, 8, 23, 9, 0, 0), recurrence.Exceptions[0].StartDateTime);

// The RTF body, decompressed from the compressed RTF property
var rtf = Encoding.ASCII.GetString(appointment.GetRtfBody()!);
Assert.StartsWith(@"{\rtf1", rtf, StringComparison.Ordinal);
```
<!-- endref -->

The dates of a recurrence are in the time zone of the appointment, which
[TimeZoneDescription](cref:Meziantou.Framework.Email.Mapi.MapiAppointment.TimeZoneDescription) describes.

## Tasks

A [MapiTask](cref:Meziantou.Framework.Email.Mapi.MapiTask) gives the [Status](cref:Meziantou.Framework.Email.Mapi.MapiTask.Status), the
completion, the start and due dates, the owner and the efforts of a task, and
[GetRecurrence](cref:Meziantou.Framework.Email.Mapi.MapiTask.GetRecurrence) its recurrence.

## Sticky notes

A [MapiNote](cref:Meziantou.Framework.Email.Mapi.MapiNote) is a sticky note ([MS-OXONOTE]): its text is the
[Body](cref:Meziantou.Framework.Email.Mapi.MapiMessage.Body), its subject is the first line of the text, and it has a
[Color](cref:Meziantou.Framework.Email.Mapi.MapiNote.Color) and the size and position of its window.
[MapiNoteBuilder](cref:Meziantou.Framework.Email.Mapi.MapiNoteBuilder) writes one, and its
[Text](cref:Meziantou.Framework.Email.Mapi.MapiNoteBuilder.Text) sets the subject as Outlook does:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/PstWriterTests.cs;region=MapiNoteBuilder_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

## Journal entries

A [MapiJournal](cref:Meziantou.Framework.Email.Mapi.MapiJournal) is a journal entry ([MS-OXOJRNL]), the record of an
activity: its [type](cref:Meziantou.Framework.Email.Mapi.MapiJournal.JournalType) (a phone call, a meeting, the editing of
a document...), its start, end and duration, the companies and contacts it is associated with, and what was done to the
document. [MapiJournalBuilder](cref:Meziantou.Framework.Email.Mapi.MapiJournalBuilder) writes one, and computes the
duration from the start and the end:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/PstWriterTests.cs;region=MapiJournalBuilder_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

[MS-OXONOTE]: https://learn.microsoft.com/openspecs/exchange_server_protocols/ms-oxonote
[MS-OXOJRNL]: https://learn.microsoft.com/openspecs/exchange_server_protocols/ms-oxojrnl
