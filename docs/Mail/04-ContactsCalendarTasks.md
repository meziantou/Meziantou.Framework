---
title: Contacts, calendar and tasks
description: Read contacts, distribution lists, appointments with their recurrence and exceptions, and tasks.
---

The items that Outlook types are returned as derived types of [PstMessage](cref:Meziantou.Framework.Mail.PstMessage), chosen
from their message class:

| Message class | Type |
| --- | --- |
| `IPM.Contact` | [PstContact](cref:Meziantou.Framework.Mail.PstContact) |
| `IPM.DistList` | [PstDistributionList](cref:Meziantou.Framework.Mail.PstDistributionList) |
| `IPM.Appointment` | [PstAppointment](cref:Meziantou.Framework.Mail.PstAppointment) |
| `IPM.Task` | [PstTask](cref:Meziantou.Framework.Mail.PstTask) |
| any other class, such as `IPM.Note` or `IPM.StickyNote` | [PstMessage](cref:Meziantou.Framework.Mail.PstMessage) |

The classes derived from these ones (`IPM.Contact.MyForm`) get the same types. Every property, typed or not, is available
from [Properties](cref:Meziantou.Framework.Mail.PstMessage.Properties): see [MAPI properties](05-Properties.md).

## Contacts and distribution lists

A [PstContact](cref:Meziantou.Framework.Mail.PstContact) gives the names, the email addresses, the telephone numbers and the
postal addresses of a contact. The [Members](cref:Meziantou.Framework.Mail.PstDistributionList.Members) of a
[PstDistributionList](cref:Meziantou.Framework.Mail.PstDistributionList) are its addresses:

<!-- ref:../../tests/Meziantou.Framework.Mail.Tests/PstFileTests.cs;region=PstContact_Read;format=md-fenced-code-block;dedent=true -->
```csharp
using var file = PstFile.Open(path);
foreach (var item in file.GetFolder("Contacts")!.EnumerateItems())
{
    switch (item)
    {
        case PstContact contact:
            Assert.Equal("contact", contact.GivenName);
            Assert.Equal("contact1@rjohnson.id.au", contact.Email1Address);
            break;

        case PstDistributionList list:
            Assert.Equal("test dist list", list.Name);
            Assert.Equal("dist1@rjohnson.id.au", list.Members[1].EmailAddress);
            break;
    }
}
```
<!-- endref -->

## Appointments

A [PstAppointment](cref:Meziantou.Framework.Mail.PstAppointment) gives the
[Start](cref:Meziantou.Framework.Mail.PstAppointment.Start) and the [End](cref:Meziantou.Framework.Mail.PstAppointment.End) of an
appointment, in UTC, its location, its attendees and its reminder.
[GetRecurrence](cref:Meziantou.Framework.Mail.PstAppointment.GetRecurrence) returns the recurrence of a recurring appointment:
its pattern, its end, and the occurrences that are deleted or modified
([Exceptions](cref:Meziantou.Framework.Mail.PstRecurrencePattern.Exceptions)):

<!-- ref:../../tests/Meziantou.Framework.Mail.Tests/PstFileTests.cs;region=PstAppointment_Read;format=md-fenced-code-block;dedent=true -->
```csharp
using var file = PstFile.Open(path);
var appointment = file.GetFolder("Calendar")!.EnumerateItems().OfType<PstAppointment>().Single();
Assert.Equal("Test appointment", appointment.Subject);
Assert.Equal(new DateTime(2016, 8, 2, 15, 0, 0, DateTimeKind.Utc), appointment.Start);

// The recurrence: every Tuesday, with deleted and moved occurrences
var recurrence = appointment.GetRecurrence()!;
Assert.Equal(PstRecurrenceFrequency.Weekly, recurrence.Frequency);
Assert.Equal(PstDaysOfWeek.Tuesday, recurrence.DaysOfWeek);
Assert.HasCount(3, recurrence.DeletedInstanceDates);
Assert.HasCount(2, recurrence.Exceptions);
Assert.Equal(new DateTime(2016, 8, 23, 9, 0, 0), recurrence.Exceptions[0].StartDateTime);

// The RTF body, decompressed from the compressed RTF property
var rtf = Encoding.ASCII.GetString(appointment.GetRtfBody()!);
Assert.StartsWith(@"{\rtf1", rtf, StringComparison.Ordinal);
```
<!-- endref -->

The dates of a recurrence are in the time zone of the appointment, which
[TimeZoneDescription](cref:Meziantou.Framework.Mail.PstAppointment.TimeZoneDescription) describes.

## Tasks

A [PstTask](cref:Meziantou.Framework.Mail.PstTask) gives the [Status](cref:Meziantou.Framework.Mail.PstTask.Status), the
completion, the start and due dates, the owner and the efforts of a task, and
[GetRecurrence](cref:Meziantou.Framework.Mail.PstTask.GetRecurrence) its recurrence.
