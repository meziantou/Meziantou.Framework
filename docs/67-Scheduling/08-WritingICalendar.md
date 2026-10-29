---
title: Writing iCalendar
description: Create events, write them as an iCalendar file with their time zones and recurrence rules, and add properties of your own.
---

## Write a calendar

Fill an `InternetCalendar` with events, then call `ToIcs`:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/InternetCalendarTests.cs;region=InternetCalendar_WriteEvent;format=md-fenced-code-block;dedent=true -->
```csharp
var calendar = new InternetCalendar();
calendar.Events.Add(new Event
{
    Id = "4c6a8f2e@example.com",
    DateTimeStamp = new DateTime(2024, 01, 01, 00, 00, 00, DateTimeKind.Utc),
    Start = new DateTime(2024, 01, 08, 09, 00, 00, DateTimeKind.Utc),
    End = new DateTime(2024, 01, 08, 09, 30, 00, DateTimeKind.Utc),
    Summary = "Weekly meeting",
    Status = EventStatus.Confirmed,
    RecurrenceRule = RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=MO;COUNT=10"),
    Organizer = new Organizer { Address = new InternetCalendarUserAddress("john@example.com") },
    Attendees =
    {
        new Attendee { Address = new InternetCalendarUserAddress("jane@example.com") },
    },
});

// The content lines end with CRLF, as RFC 5545 requires
Assert.Equal("""
    BEGIN:VCALENDAR
    VERSION:2.0
    PRODID:-//Meziantou//Meziantou.Framework.Scheduling//EN
    BEGIN:VEVENT
    UID:4c6a8f2e@example.com
    STATUS:CONFIRMED
    ORGANIZER:mailto:john@example.com
    ATTENDEE:mailto:jane@example.com
    DTSTAMP:20240101T000000Z
    DTSTART:20240108T090000Z
    DTEND:20240108T093000Z
    RRULE:FREQ=WEEKLY;COUNT=10;BYDAY=MO
    SUMMARY:Weekly meeting
    END:VEVENT
    END:VCALENDAR

    """, calendar.ToIcs().ReplaceLineEndings("\n"));
```
<!-- endref -->

- `ToIcs()` returns a `string`; `ToIcs(TextWriter)` and `ToIcs(Stream)` write to a writer or, in UTF-8, to a stream,
  such as a file or an HTTP response.
- RFC 5545 requires `DTSTAMP` in an event. Set `DateTimeStamp`, for instance to `DateTime.UtcNow`: the library never
  reads the clock, so its output only depends on the model.
- `CREATED`, `LAST-MODIFIED`, `DTSTAMP`, `DTSTART`, `DTEND`, `STATUS` and `DESCRIPTION` are not written when they are not
  set. An organizer or an attendee without an `Address` is not written.
- `InternetCalendarUserAddress` takes an email address, written as a `mailto:` URI, or an absolute `Uri`.

The calendar can be read back:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/InternetCalendarTests.cs;region=InternetCalendar_Stream;format=md-fenced-code-block;dedent=true -->
```csharp
var calendar = new InternetCalendar();
calendar.Events.Add(new Event { Summary = "Lunch", Start = new DateTime(2024, 01, 02, 12, 00, 00, DateTimeKind.Utc) });

// Write UTF-8 to a stream, such as a file or an HTTP response
using var stream = new MemoryStream();
calendar.ToIcs(stream);

// Read it back
stream.Position = 0;
var parsed = InternetCalendar.Parse(stream);
Assert.Equal("Lunch", Assert.Single(parsed.Events).Summary);
```
<!-- endref -->

## Dates and time zones

How `Start` and `End` are written depends on the event:

| Event | Written as |
| --- | --- |
| `IsAllDay` is `true` | `DTSTART;VALUE=DATE:20240610`, the date only |
| `TimeZone` is set | `DTSTART;TZID=Europe/Paris:20240610T090000`, and a `VTIMEZONE` component |
| The `Kind` is `Utc` or `Local` | `DTSTART:20240610T070000Z`, in UTC |
| The `Kind` is `Unspecified` | `DTSTART:20240610T090000`, a floating time |

With a time zone, an `Unspecified` value is a wall-clock time in that time zone, and a `Utc` or `Local` value is an instant,
converted to it. The calendar gets a `VTIMEZONE` component that describes the time zone, which calendar applications need
to read the times:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/InternetCalendarTests.cs;region=InternetCalendar_WriteWithTimeZone;format=md-fenced-code-block;dedent=true -->
```csharp
var calendar = new InternetCalendar();
calendar.Events.Add(new Event
{
    Start = new DateTime(2024, 01, 02, 08, 00, 00),
    End = new DateTime(2024, 01, 02, 09, 00, 00),
    TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"),
});

var ics = calendar.ToIcs();
Assert.Contains("BEGIN:VTIMEZONE\r\nTZID:America/New_York\r\n", ics);
Assert.Contains("DTSTART;TZID=America/New_York:20240102T080000\r\n", ics);
```
<!-- endref -->

An all-day event ignores the time zone. As in RFC 5545, its `End` is excluded: an event on June 10, 11 and 12 ends on
June 13:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/InternetCalendarTests.cs;region=InternetCalendar_AllDayEvent;format=md-fenced-code-block;dedent=true -->
```csharp
var @event = new Event
{
    Summary = "Conference",
    IsAllDay = true,
    Start = new DateTime(2024, 06, 10),
    End = new DateTime(2024, 06, 13), // exclusive: the event lasts June 10, 11 and 12
};
@event.AdditionalProperties["LOCATION"] = "Paris";

var calendar = new InternetCalendar();
calendar.Events.Add(@event);

var ics = calendar.ToIcs();
Assert.Contains("DTSTART;VALUE=DATE:20240610\r\n", ics);
Assert.Contains("DTEND;VALUE=DATE:20240613\r\n", ics);
Assert.Contains("LOCATION:Paris\r\n", ics);
```
<!-- endref -->

`CREATED`, `LAST-MODIFIED` and `DTSTAMP` are always written in UTC: a `Local` value is converted, and an `Unspecified`
value is taken as UTC.

The `UNTIL` of the recurrence rule is written with the form of `DTSTART`, as RFC 5545 requires, without changing the rule:
a date for an all-day event, a floating time for a floating start, and UTC for any other start.

## Properties of your own

- `AdditionalProperties` writes a property from a name and a value. The value of a text property, such as `LOCATION` or
  an `X-` property, is escaped; the value of another property, such as `URL` or `GEO`, is written as is.
- `RawProperties` writes an `InternetCalendarProperty` as is, with its parameters, after the `AdditionalProperties`:

  <!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/InternetCalendarTests.cs;region=InternetCalendar_RawProperties;format=md-fenced-code-block;dedent=true -->
  ```csharp
  var @event = new Event();
  @event.RawProperties.Add(new InternetCalendarProperty("EXDATE", [new("TZID", "Europe/Paris")], "20240104T100000"));
  @event.RawProperties.Add(new InternetCalendarProperty("GEO", "37.386013;-122.082932"));
  ```
  <!-- endref -->

- A property named after one the writer emits itself, such as `UID`, `DTSTART` or `SUMMARY`, is not written. The
  exception is `RRULE` in `RawProperties`, which is written after the rule of `RecurrenceRule`.
- A `TZID` parameter of any written property gets a `VTIMEZONE` component too, when the identifier resolves.

The parameters of `Organizer`, `Attendees` and the `…Parameters` collections of the event are written with their
property:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/InternetCalendarTests.cs;region=InternetCalendar_AttendeeParameters;format=md-fenced-code-block;dedent=true -->
```csharp
var @event = new Event();
@event.Attendees.Add(new Attendee
{
    Address = new InternetCalendarUserAddress("jane@example.com"),
    Parameters = { new("CN", "\"Doe, Jane\""), new("PARTSTAT", "ACCEPTED"), new("RSVP", "TRUE") },
});
```
<!-- endref -->

A value that is a list of values, such as `"Doe, Jane"` with its quotes, is written as such; any other value is written
between quotes when it needs them, with `^`, line feeds and `"` encoded as RFC 6868 defines.

## Safe output

- Lines longer than 75 bytes are folded, never inside a character.
- Text values drop the control characters other than tabs; line feeds are escaped as `\n`.
- `InternetCalendarProperty`, `Organizer.Parameters`, `Attendee.Parameters` and the parameter collections of `Event`
  check names, parameters and values when they are created or added, so a value cannot inject a line into the file.
- The calendar is formatted before anything is written: a value that cannot be written, such as a `Status` that is not a
  member of `EventStatus`, throws an `InvalidOperationException`, and nothing reaches the `TextWriter` or the `Stream`.
