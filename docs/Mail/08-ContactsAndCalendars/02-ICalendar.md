---
title: iCalendar
description: Read and write iCalendar objects (.ics), convert their times with their time zones, expand their recurrence rules, and convert them to and from the appointments and tasks of Outlook.
---

An iCalendar object (`.ics`, [RFC 5545]) is a calendar of events, to-dos and time zones.
[VCalendar](cref:Meziantou.Framework.Mail.VCalendar) reads and writes it:

<!-- ref:../../../tests/Meziantou.Framework.Mail.Tests/CalendarTests.cs;region=VCalendar_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var calendar = VCalendar.Load(path).Single();
var meeting = calendar.Events.First();
Assert.Equal("Weekly meeting", meeting.Summary);
Assert.Equal("Room 1; building A", meeting.Location);

// The start is 9:00 in Paris: the time zone of the calendar converts it to UTC
Assert.Equal(new DateTime(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc), meeting.Start!.Value.ToUtc(calendar));
Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0), meeting.GetOccurrences().First());

var created = new VCalendar();
var ev = created.Add(new VEvent
{
    Uid = "launch@example.com",
    Summary = "Launch",
    Start = CalendarDateTime.FromUtc(new DateTime(2026, 6, 1, 14, 0, 0, DateTimeKind.Utc)),
    Duration = TimeSpan.FromHours(1),
    RecurrenceRule = RecurrenceRule.Parse("FREQ=WEEKLY;COUNT=4"),
});
var text = created.ToString();
```
<!-- endref -->

## Components and properties

A calendar is a tree of [CalendarComponent](cref:Meziantou.Framework.Mail.CalendarComponent): each one has its
properties, in their order, and its components. [VEvent](cref:Meziantou.Framework.Mail.VEvent),
[VTodo](cref:Meziantou.Framework.Mail.VTodo) and [VTimeZone](cref:Meziantou.Framework.Mail.VTimeZone) read and write
their properties through it (summary, description, location, start, end, duration, attendees, categories, status,
progress...), so that a calendar written back keeps the properties and the components they do not know, such as the
alarms (`VALARM`). A calendar read and written without changes keeps its properties as they were; the writer folds the
lines longer than 75 bytes.

## Dates and time zones

[CalendarDateTime](cref:Meziantou.Framework.Mail.CalendarDateTime) is a date (an all-day event), a time in UTC, a local
time in a time zone (`TZID`) or a floating time.
[ToUtc](cref:Meziantou.Framework.Mail.CalendarDateTime.ToUtc) converts a local time with the definition of its time zone
in the calendar ([VTimeZone.GetUtcOffset](cref:Meziantou.Framework.Mail.VTimeZone.GetUtcOffset)): the observances
`STANDARD` and `DAYLIGHT`, their offsets and the rules of their onsets. The library does not use the time zones of the
operating system: a time zone the calendar does not define gives no time in UTC.

## Recurrences

[RecurrenceRule](cref:Meziantou.Framework.Mail.RecurrenceRule) reads and writes the rules of `RRULE`, and
[GetOccurrences](cref:Meziantou.Framework.Mail.RecurrenceRule.GetOccurrences) returns their occurrences, as many as
they are enumerated:

<!-- ref:../../../tests/Meziantou.Framework.Mail.Tests/CalendarTests.cs;region=RecurrenceRule_GetOccurrences;format=md-fenced-code-block;dedent=true -->
```csharp
var rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=-1FR;COUNT=3")!;
var occurrences = rule.GetOccurrences(new DateTime(2026, 1, 30, 17, 0, 0)).ToList();
Assert.Equal([new DateTime(2026, 1, 30, 17, 0, 0), new DateTime(2026, 2, 27, 17, 0, 0), new DateTime(2026, 3, 27, 17, 0, 0)], occurrences);
```
<!-- endref -->

- The rules of a frequency of a day or more are supported, with `INTERVAL`, `COUNT`, `UNTIL`, `BYMONTH`, `BYMONTHDAY`,
  `BYDAY` (with the positions in the month or the year), `BYSETPOS` and `WKST`. The other parts (`BYHOUR`, `BYMINUTE`,
  `BYSECOND`, `BYYEARDAY`, `BYWEEKNO`) are kept in [OtherParts](cref:Meziantou.Framework.Mail.RecurrenceRule.OtherParts)
  and written back; their occurrences throw a `NotSupportedException`.
- The start (`DTSTART`) is the first occurrence, as [RFC 5545] 3.8.5.3 requires, even when the rule does not give it.
- [VEvent.GetOccurrences](cref:Meziantou.Framework.Mail.VEvent.GetOccurrences) adds the dates of `RDATE` and removes the
  dates of `EXDATE`. The occurrences are in the basis of the start: in UTC, local in its time zone, or floating.

## Appointments and tasks of Outlook

[VCalendar.AddAppointment](cref:Meziantou.Framework.Mail.VCalendar.AddAppointment) adds a
[PstAppointment](cref:Meziantou.Framework.Mail.PstAppointment) as an event: its times in UTC, its attendees, its
reminder as an alarm, and its recurrence as a rule, the dates of its deleted occurrences as `EXDATE`, and an event per
modified occurrence (`RECURRENCE-ID`). The pattern of Outlook is in local times: the offset of the first occurrence
converts them to UTC, so the occurrences after a change of daylight saving time are one hour off.
[VEvent.ToPstAppointmentBuilder](cref:Meziantou.Framework.Mail.VEvent.ToPstAppointmentBuilder) creates the builder of an
appointment, with a recurrence when Outlook can represent the rule (daily, weekly, monthly on a day or on the nth day of
the week, yearly). [VCalendar.AddTask](cref:Meziantou.Framework.Mail.VCalendar.AddTask) and
[VTodo.ToPstTaskBuilder](cref:Meziantou.Framework.Mail.VTodo.ToPstTaskBuilder) convert the tasks.

[RFC 5545]: https://www.rfc-editor.org/rfc/rfc5545
