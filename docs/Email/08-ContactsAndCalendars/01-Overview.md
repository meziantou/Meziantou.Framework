---
title: Overview
description: The contact and calendar formats the library reads and writes, and how they convert to and from the items of Outlook.
---

Mail clients exchange contacts and calendars as text files, which the library reads and writes:

| Format | Files | Types | Page |
| --- | --- | --- | --- |
| vCard 2.1, 3.0, 4.0 | `.vcf` | [VCard](cref:Meziantou.Framework.Email.Contacts.VCard), in the [Meziantou.Framework.Email.Contacts](cref:Meziantou.Framework.Email.Contacts) namespace | [vCard](02-VCard.md) |
| iCalendar | `.ics` | [VCalendar](cref:Meziantou.Framework.Email.Calendar.VCalendar), [VEvent](cref:Meziantou.Framework.Email.Calendar.VEvent), [VTodo](cref:Meziantou.Framework.Email.Calendar.VTodo), [RecurrenceRule](cref:Meziantou.Framework.Email.Calendar.RecurrenceRule), in the [Meziantou.Framework.Email.Calendar](cref:Meziantou.Framework.Email.Calendar) namespace | [iCalendar](03-ICalendar.md) |

Both convert to and from the items of Outlook: a vCard to a contact, an event to an appointment, a to-do to a task, and
an invitation (an iCalendar object with a method, such as `REQUEST`) to a meeting message. The items can then be written
in an [Outlook data file](../03-Pst/01-Overview.md) or an [.msg file](../05-Msg/01-Overview.md).
