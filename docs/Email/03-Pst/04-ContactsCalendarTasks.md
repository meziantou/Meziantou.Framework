---
title: Contacts, calendar and tasks
description: Read contacts, distribution lists, appointments with their recurrence and exceptions, and tasks.
---

The items that Outlook types are returned as derived types of [MapiMessage](cref:Meziantou.Framework.Email.Mapi.MapiMessage), chosen
from their message class:

| Message class | Type |
| --- | --- |
| `IPM.Contact` | [MapiContact](cref:Meziantou.Framework.Email.Mapi.MapiContact) |
| `IPM.DistList` | [MapiDistributionList](cref:Meziantou.Framework.Email.Mapi.MapiDistributionList) |
| `IPM.Appointment` | [MapiAppointment](cref:Meziantou.Framework.Email.Mapi.MapiAppointment) |
| `IPM.Task` | [MapiTask](cref:Meziantou.Framework.Email.Mapi.MapiTask) |
| any other class, such as `IPM.Note` or `IPM.StickyNote` | [MapiMessage](cref:Meziantou.Framework.Email.Mapi.MapiMessage) |

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
