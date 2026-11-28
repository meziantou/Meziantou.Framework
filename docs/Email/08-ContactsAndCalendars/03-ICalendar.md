---
title: iCalendar
description: Read and write iCalendar objects (.ics), convert their times with their time zones, expand their recurrence rules, and convert them to and from the appointments and tasks of Outlook.
---

An iCalendar object (`.ics`, [RFC 5545]) is a calendar of events, to-dos and time zones.
[VCalendar](cref:Meziantou.Framework.Email.Calendar.VCalendar) reads and writes it:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/CalendarTests.cs;region=VCalendar_Usage;format=md-fenced-code-block;dedent=true -->
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

A calendar is a tree of [CalendarComponent](cref:Meziantou.Framework.Email.Calendar.CalendarComponent): each one has its
properties, in their order, and its components. [VEvent](cref:Meziantou.Framework.Email.Calendar.VEvent),
[VTodo](cref:Meziantou.Framework.Email.Calendar.VTodo) and [VTimeZone](cref:Meziantou.Framework.Email.Calendar.VTimeZone) read and write
their properties through it (summary, description, location, start, end, duration, attendees, categories, status,
progress...), so that a calendar written back keeps the properties and the components they do not know, such as the
alarms (`VALARM`). A calendar read and written without changes keeps its properties as they were; the writer folds the
lines longer than 75 bytes. A line break (CRLF, LF or a lone CR) cannot end a line early: the writer writes it as `\n`
in all the values, the ones set with [SetValue](cref:Meziantou.Framework.Email.Calendar.CalendarComponent.SetValue)
included, and as `^n` in the parameters ([RFC 6868]). The reader decodes the values in quoted-printable (vCard 2.1 and
vCalendar 1.0) as the vCard reader does: a `=` at the end of a line continues the value on the next line only in these
values, and stays in the other values.

## Dates and time zones

[CalendarDateTime](cref:Meziantou.Framework.Email.Calendar.CalendarDateTime) is a date (an all-day event), a time in UTC, a local
time in a time zone (`TZID`) or a floating time.
[ToUtc](cref:Meziantou.Framework.Email.Calendar.CalendarDateTime.ToUtc) converts a local time with the definition of its time zone
in the calendar ([VTimeZone.GetUtcOffset](cref:Meziantou.Framework.Email.Calendar.VTimeZone.GetUtcOffset)): the observances
`STANDARD` and `DAYLIGHT`, their offsets and the rules of their onsets. The library does not use the time zones of the
operating system: a time zone the calendar does not define gives no time in UTC.

## Recurrences

[RecurrenceRule](cref:Meziantou.Framework.Email.Calendar.RecurrenceRule) reads and writes the rules of `RRULE`, and
[GetOccurrences](cref:Meziantou.Framework.Email.Calendar.RecurrenceRule.GetOccurrences) returns their occurrences, as many as
they are enumerated:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/CalendarTests.cs;region=RecurrenceRule_GetOccurrences;format=md-fenced-code-block;dedent=true -->
```csharp
var rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=-1FR;COUNT=3")!;
var occurrences = rule.GetOccurrences(new DateTime(2026, 1, 30, 17, 0, 0)).ToList();
Assert.Equal([new DateTime(2026, 1, 30, 17, 0, 0), new DateTime(2026, 2, 27, 17, 0, 0), new DateTime(2026, 3, 27, 17, 0, 0)], occurrences);
```
<!-- endref -->

- The rules of a frequency of a day or more are supported, with `INTERVAL`, `COUNT`, `UNTIL`, `BYMONTH`, `BYMONTHDAY`,
  `BYDAY` (with the positions in the month or the year), `BYSETPOS` and `WKST`. The other parts (`BYHOUR`, `BYMINUTE`,
  `BYSECOND`, `BYYEARDAY`, `BYWEEKNO`) are kept in [OtherParts](cref:Meziantou.Framework.Email.Calendar.RecurrenceRule.OtherParts)
  and written back; their occurrences throw a `NotSupportedException`.
- The start (`DTSTART`) is the first occurrence, as [RFC 5545] 3.8.5.3 requires, even when the rule does not give it.
- [VEvent.GetOccurrences](cref:Meziantou.Framework.Email.Calendar.VEvent.GetOccurrences) adds the dates of `RDATE`, also those before the start, and removes the
  dates of `EXDATE`. The occurrences are in the basis of the start: in UTC, local in its time zone, or floating.

## Appointments and tasks of Outlook

[VCalendar.AddAppointment](cref:Meziantou.Framework.Email.Calendar.VCalendar.AddAppointment) adds a
[MapiAppointment](cref:Meziantou.Framework.Email.Mapi.MapiAppointment) as an event: its times in UTC, its attendees, its
reminder as an alarm, and its recurrence as a rule, the dates of its deleted occurrences as `EXDATE`, and an event per
modified occurrence (`RECURRENCE-ID`). The pattern of Outlook is in local times: the offset of the first occurrence
converts them to UTC, so the occurrences after a change of daylight saving time are one hour off.
[VEvent.ToMapiAppointmentBuilder](cref:Meziantou.Framework.Email.Calendar.VEvent.ToMapiAppointmentBuilder) creates the builder of an
appointment, with a recurrence when Outlook can represent the rule (daily, weekly, monthly on a day or on the nth day of
the week, yearly). [VCalendar.AddTask](cref:Meziantou.Framework.Email.Calendar.VCalendar.AddTask) and
[VTodo.ToMapiTaskBuilder](cref:Meziantou.Framework.Email.Calendar.VTodo.ToMapiTaskBuilder) convert the tasks.

The `UID` of an event is kept in the global object identifier of the appointment, as Exchange stores it ([MS-OXCICAL]):
[MapiAppointment.Uid](cref:Meziantou.Framework.Email.Mapi.MapiAppointment.Uid) is the `UID` an event had, or the
hexadecimal identifier Outlook created, and [Sequence](cref:Meziantou.Framework.Email.Mapi.MapiAppointment.Sequence) is
its `SEQUENCE`.

## Meeting invitations

An invitation is a calendar with a method (iTIP, [RFC 5546]), sent in an email as a `text/calendar` part (iMIP,
[RFC 6047]). Outlook stores it as a meeting message ([MS-OXOCAL]), a
[MapiMeetingMessage](cref:Meziantou.Framework.Email.Mapi.MapiMeetingMessage):

| Method | Meeting message | Class |
| --- | --- | --- |
| `REQUEST` | An invitation or an update | `IPM.Schedule.Meeting.Request` |
| `CANCEL` | A cancellation | `IPM.Schedule.Meeting.Canceled` |
| `REPLY` with `PARTSTAT=ACCEPTED` | An acceptance | `IPM.Schedule.Meeting.Resp.Pos` |
| `REPLY` with `PARTSTAT=TENTATIVE` | A tentative acceptance | `IPM.Schedule.Meeting.Resp.Tent` |
| `REPLY` with `PARTSTAT=DECLINED` | A refusal | `IPM.Schedule.Meeting.Resp.Neg` |

[VCalendar.ToMapiMeetingMessageBuilder](cref:Meziantou.Framework.Email.Calendar.VCalendar.ToMapiMeetingMessageBuilder)
converts an invitation to a [MapiMeetingMessageBuilder](cref:Meziantou.Framework.Email.Mapi.MapiMeetingMessageBuilder),
and [VCalendar.AddMeetingMessage](cref:Meziantou.Framework.Email.Calendar.VCalendar.AddMeetingMessage) converts a meeting
message back:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/CalendarTests.cs;region=MapiMeetingMessageBuilder_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var invitation = VCalendar.Parse("""
    BEGIN:VCALENDAR
    VERSION:2.0
    PRODID:-//Example//Example//EN
    METHOD:REQUEST
    BEGIN:VEVENT
    UID:meeting-42@example.com
    SEQUENCE:1
    DTSTAMP:20260301T080000Z
    DTSTART:20260302T090000Z
    DTEND:20260302T100000Z
    SUMMARY:Project review
    LOCATION:Room 1
    ORGANIZER;CN=Alice:mailto:alice@example.com
    ATTENDEE;CN=Bob;ROLE=REQ-PARTICIPANT:mailto:bob@example.com
    END:VEVENT
    END:VCALENDAR
    """)[0];

// The invitation as a meeting request of Outlook
var request = Assert.IsType<MapiMeetingMessage>(MsgFile.Load(MsgFile.ToByteArray(invitation.ToMapiMeetingMessageBuilder())));
Assert.Equal(MapiMeetingMessageType.Request, request.MeetingMessageType);
Assert.Equal("IPM.Schedule.Meeting.Request", request.MessageClass);
Assert.Equal("meeting-42@example.com", request.Uid);
Assert.Equal("alice@example.com", request.SenderEmailAddress);
Assert.Equal("bob@example.com", request.Recipients.Single().EmailAddress);

// And back as an invitation
var calendar = new VCalendar();
var meeting = calendar.AddMeetingMessage(request);
Assert.Equal("REQUEST", calendar.Method);
Assert.Equal("meeting-42@example.com", meeting.Uid);
Assert.Equal(1, meeting.Sequence);
```
<!-- endref -->

The organizer of an invitation is the sender of the meeting message, and its attendees are the recipients. A response
is sent by an attendee to the organizer: its sender is the attendee, and its recipient the organizer.
[EmailMessage.CalendarBody](cref:Meziantou.Framework.Email.EmailMessage.CalendarBody) is the `text/calendar` part of an
email: [ToMapiBuilder](cref:Meziantou.Framework.Email.EmailMessage.ToMapiBuilder) converts an email with an invitation to
a meeting message, and [FromMapi](cref:Meziantou.Framework.Email.EmailMessage.FromMapi) converts a meeting message to an
email with its invitation, which [ToMime](cref:Meziantou.Framework.Email.EmailMessage.ToMime) writes as an alternative
with its method (`text/calendar; method=REQUEST`).

[RFC 5545]: https://www.rfc-editor.org/rfc/rfc5545
[RFC 5546]: https://www.rfc-editor.org/rfc/rfc5546
[RFC 6047]: https://www.rfc-editor.org/rfc/rfc6047
[RFC 6868]: https://www.rfc-editor.org/rfc/rfc6868
[MS-OXOCAL]: https://learn.microsoft.com/openspecs/exchange_server_protocols/ms-oxocal
[MS-OXCICAL]: https://learn.microsoft.com/openspecs/exchange_server_protocols/ms-oxcical
