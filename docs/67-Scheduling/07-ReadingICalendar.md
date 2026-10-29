---
title: Reading iCalendar
description: Parse iCalendar files, read their events, dates, time zones and recurrence rules, and keep the properties the model does not have.
---

## Parse a calendar

`InternetCalendar.Parse` reads an iCalendar object from a `string`, a `ReadOnlySpan<char>`, a `TextReader` or a UTF-8
`Stream`. `TryParse` returns `false` and the reason instead of throwing:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/InternetCalendarTests.cs;region=InternetCalendar_Parse;format=md-fenced-code-block;dedent=true -->
```csharp
var content = """
    BEGIN:VCALENDAR
    VERSION:2.0
    PRODID:-//Example//Example//EN
    BEGIN:VEVENT
    UID:1@example.com
    DTSTAMP:20240101T000000Z
    DTSTART:20240102T080000Z
    SUMMARY:Team meeting
    END:VEVENT
    END:VCALENDAR
    """;

var calendar = InternetCalendar.Parse(content);
var @event = Assert.Single(calendar.Events);
Assert.Equal("Team meeting", @event.Summary);
Assert.Equal(new DateTime(2024, 01, 02, 08, 00, 00, DateTimeKind.Utc), @event.Start);

var invalidContent = "BEGIN:VCALENDAR";
Assert.False(InternetCalendar.TryParse(invalidContent, out _, out var error));
Assert.Equal("The VCALENDAR component is not terminated by END:VCALENDAR", error);
```
<!-- endref -->

The calendar is read as a whole: a single invalid property, such as an invalid `RRULE`, `STATUS`, `DURATION` or
date-time, rejects it, and `TryParse` tells which.

`Parse(Stream)` removes the line folds before decoding the UTF-8 content, so a line folded in the middle of a character
is decoded correctly. A stream that starts with a UTF-16 or UTF-32 byte order mark is decoded with that encoding.

## Events

`InternetCalendar.Events` holds the `VEVENT` components. An `Event` has:

| Property | iCalendar property |
| --- | --- |
| `Id` | `UID` |
| `Summary`, `Description` | `SUMMARY`, `DESCRIPTION` |
| `Start`, `End`, `IsAllDay`, `TimeZone` | `DTSTART`, `DTEND` or `DURATION` |
| `RecurrenceRule` | The first `RRULE` |
| `Status` | `STATUS`: `Tentative`, `Confirmed`, `Cancelled`, or `null` without one |
| `Organizer`, `Attendees` | `ORGANIZER`, `ATTENDEE`: an `Address` and the `Parameters`, such as `CN`, `ROLE`, `PARTSTAT` or `RSVP` |
| `Created`, `LastModified`, `DateTimeStamp` | `CREATED`, `LAST-MODIFIED`, `DTSTAMP`, always `Utc` values |
| `AdditionalProperties`, `RawProperties` | The other properties: see [Other properties](#other-properties) |

The parameters of the properties the model reads are kept in the matching collections, such as `SummaryParameters`
(`SUMMARY;LANGUAGE=fr:Bonjour`) or `StartParameters`. The `VALUE` and `TZID` parameters of a date-time are not stored
there: the model represents them.

The `VTODO`, `VJOURNAL`, `VFREEBUSY` and `VALARM` components are skipped.

## Dates and time zones

A date-time has one of the three forms of RFC 5545:

| Value | `Start` | `TimeZone` |
| --- | --- | --- |
| `DTSTART:20240102T080000Z` | A `Utc` value | `null` |
| `DTSTART:20240102T080000` | A floating, `Unspecified` value | `null` |
| `DTSTART;TZID=America/New_York:20240102T080000` | The wall-clock time, `Unspecified` | The time zone |
| `DTSTART;VALUE=DATE:20240102` | The first instant of the day; `IsAllDay` is `true` | `null` |

The identifier of a `TZID` parameter is resolved, in order:

1. as a time zone of the platform;
2. from the `VTIMEZONE` component of the calendar that has this identifier, which gives a `TimeZoneInfo` whose `Id` is
   the identifier, so the event is written back with it;
3. as the IANA identifier at the end of a prefixed identifier, such as `/mozilla.org/20050126_1/America/New_York`.

An identifier that cannot be resolved does not reject the calendar: `DTSTART` and `DTEND` are read as floating values,
and `TimeZone` is `null`. An event whose `DTSTART` and `DTEND` have different `TZID` values is rejected, because an
`Event` has a single time zone.

A leap second (`235960`) is read as the 59th second.

## Recurring events

`RecurrenceRule` holds the rule of the event. Enumerate its occurrences from `Start`, in the time zone of the event:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/InternetCalendarTests.cs;region=InternetCalendar_RecurringEvent;format=md-fenced-code-block;dedent=true -->
```csharp
var calendar = InternetCalendar.Parse("""
    BEGIN:VCALENDAR
    VERSION:2.0
    PRODID:-//Example//Example//EN
    BEGIN:VEVENT
    UID:2@example.com
    DTSTAMP:20240101T000000Z
    DTSTART;TZID=Europe/Paris:20240325T090000
    DTEND;TZID=Europe/Paris:20240325T100000
    RRULE:FREQ=WEEKLY;COUNT=3
    SUMMARY:Stand-up
    END:VEVENT
    END:VCALENDAR
    """);

var @event = Assert.Single(calendar.Events);
Assert.NotNull(@event.RecurrenceRule);
Assert.NotNull(@event.TimeZone);

// Start is a wall-clock time in the time zone of the event
var occurrences = @event.RecurrenceRule.GetNextOccurrences(@event.Start, @event.TimeZone);
Assert.Equal(
    [
        new DateTimeOffset(2024, 03, 25, 09, 00, 00, TimeSpan.FromHours(1)),
        new DateTimeOffset(2024, 04, 01, 09, 00, 00, TimeSpan.FromHours(2)),
        new DateTimeOffset(2024, 04, 08, 09, 00, 00, TimeSpan.FromHours(2)),
    ],
    occurrences);
```
<!-- endref -->

For an event without a time zone, use the overload without one: see [Time zones](05-TimeZones.md#without-a-time-zone).
RFC 5545 discourages more than one `RRULE`: a second one is kept in `RawProperties`.

## Duration

An event with a `DURATION` rather than a `DTEND` gets its `End` computed as RFC 5545 section 3.3.6 describes: days and
weeks are added to the wall-clock time of the start, while hours, minutes and seconds are added to the instant. So `P1D`
from 10:00 in New York on March 9, 2024 ends at 10:00 the next day, although that day lasts 23 hours, while `PT24H` ends
at 11:00. An event with both `DURATION` and `DTEND` is rejected.

## Other properties

The properties the model does not have are kept, so that a calendar can be read and written back without losing them:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/InternetCalendarTests.cs;region=InternetCalendar_UnknownProperties;format=md-fenced-code-block;dedent=true -->
```csharp
var calendar = InternetCalendar.Parse("""
    BEGIN:VCALENDAR
    VERSION:2.0
    PRODID:-//Example//Example//EN
    BEGIN:VEVENT
    UID:1@example.com
    DTSTAMP:20240101T000000Z
    DTSTART:20240102T080000Z
    LOCATION:Room 101
    X-MICROSOFT-CDO-BUSYSTATUS:OOF
    CATEGORIES:WORK,PLANNING
    END:VEVENT
    END:VCALENDAR
    """);

var @event = Assert.Single(calendar.Events);

// A single TEXT value without parameters
Assert.Equal("Room 101", @event.AdditionalProperties["LOCATION"]);
Assert.Equal("OOF", @event.AdditionalProperties["X-MICROSOFT-CDO-BUSYSTATUS"]);

// Anything else is kept verbatim, and written back as is
var categories = Assert.Single(@event.RawProperties);
Assert.Equal("CATEGORIES", categories.Name);
Assert.Equal("WORK,PLANNING", categories.Value);
```
<!-- endref -->

- `AdditionalProperties` holds a property whose value is a single text value, without parameter, that occurs once:
  `LOCATION`, `CLASS`, or an `X-` property such as `X-MICROSOFT-CDO-BUSYSTATUS`. Its value is unescaped.
- `RawProperties` holds every other property as an `InternetCalendarProperty`, whose `Name`, `Parameters` and `Value`
  are kept as written: a property with parameters (`EXDATE;TZID=Europe/Paris:20240104T100000`), a repeated one, one whose
  value is not text (`URL`, `GEO`, `EXDATE`), or a list (`CATEGORIES`).
- The calendar has the same two collections, for the properties of the `VCALENDAR` component.

A parameter value keeps its quotes, as a list of values (`CN="Doe, Jane"`), with the RFC 6868 encoding decoded: `^^` is
`^`, `^n` a line feed and `^'` a `"`.
