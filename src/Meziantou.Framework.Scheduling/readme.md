# Meziantou.Framework.Scheduling

This package supports 2 schedule formats:

- Recurrence rules (RRULE) as defined in RFC5545 and RFC2445
- Cron expressions

## Recurrence rules (RRULE)

Parse recurrence rules:

````c#
var rrule = "FREQ=DAILY;UNTIL=20000131T140000Z;BYMONTH=1";
if (RecurrenceRule.TryParse(rrule, out var rule, out var error))
{
    var nextOccurrences = rule.GetNextOccurrences(DateTime.Now).Take(50).ToArray();
}
````

Convert a recurrence rule to human-readable text:

````c#
var culture = CultureInfo.GetCultureInfo("en-US");
RecurrenceRule.Parse("FREQ=DAILY").GetHumanText(culture); // every day
RecurrenceRule.Parse("FREQ=WEEKLY;INTERVAL=3;BYDAY=TU;UNTIL=20150101").GetHumanText(culture); // every 3 weeks on Tuesday until January 1, 2015
````

Supported languages for human-readable text:

- English (`en`, `en-*`, and invariant culture)
- French (`fr`, `fr-*`)

### Time zones

`GetNextOccurrences` also accepts a time zone. The occurrences keep their wall-clock time across a daylight
saving transition, so each one carries the UTC offset in effect at that moment:

````c#
var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9");
var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

foreach (var occurrence in rrule.GetNextOccurrences(new DateTime(2024, 03, 09), timeZone).Take(3))
{
    Console.WriteLine(occurrence);
}

// 2024-03-09 09:00:00 -05:00
// 2024-03-10 09:00:00 -04:00   <- the offset changes, the wall clock does not
// 2024-03-11 09:00:00 -04:00
````

The start date is read as a wall-clock time in that time zone, and a `DateTimeOffset` overload accepts an
instant instead. A time zone identifier can be passed directly, which `TimeZones.Find` resolves:

````c#
var occurrences = rrule.GetNextOccurrences(startDate, "America/New_York");
````

A local time that a transition makes invalid or ambiguous is resolved as RFC 5545 section 3.3.5 requires:
an ambiguous time keeps its first occurrence, and an invalid time is read with the UTC offset in effect
before the gap, so `02:30` on a spring-forward day surfaces as `03:30` at the new offset. This is what
[errata 4271](https://www.rfc-editor.org/errata/eid4271) settles for recurrence instances; only an invalid
*date*, such as February 30, is dropped from the recurrence set.

Reading a gap that way maps it onto the instants of the hour that follows it, so a sub-hourly recurrence
would otherwise repeat them. Those duplicates are ignored, per RFC 5545 section 3.8.5.3, and do not count
towards `COUNT`. Across a backward transition the repeated hour is visited once, so its second pass is not
produced.

`UNTIL` is honoured as an instant when it is a UTC value, and as a wall-clock reading when it is floating.

`CronExpression` supports the same overloads.

### UNTIL

A UTC `UNTIL` (`20240110T100000Z`), or one carrying an offset, is parsed as a `Utc` `EndDate` and denotes an instant.
A floating `UNTIL` (`20240110T100000`) or a date (`20240110`) is parsed as an `Unspecified` `EndDate` and denotes a
wall-clock reading, a date being its first instant; a date is written back as a date.

Without a time zone, an instant bounds the occurrences of a `Utc` or `Local` start date by instant, and the occurrences
of an `Unspecified` start date by wall clock. A wall-clock `UNTIL` always bounds them by wall clock.

## iCalendar

`InternetCalendar` reads and writes events in the iCalendar format.

### Reading

`InternetCalendar.Parse` reads an iCalendar object from a string, a `ReadOnlySpan<char>`, a `TextReader` or
a UTF-8 `Stream`, and `TryParse` reports why the content was rejected instead of throwing:

````c#
var calendar = InternetCalendar.Parse(File.ReadAllText("invite.ics"));
foreach (var @event in calendar.Events)
{
    Console.WriteLine($"{@event.Start:g} {@event.Summary}");
}

if (!InternetCalendar.TryParse(content, out var parsed, out var error))
{
    Console.WriteLine(error);
}
````

The parser unfolds content lines, decodes `TEXT` values and reads the three date-time forms of RFC 5545
section 3.3.5: `20240102T080000Z` becomes a `Utc` value, `20240102T080000` a floating (`Unspecified`) one,
and `DTSTART;TZID=America/New_York:20240102T080000` a wall-clock value together with `Event.TimeZone`.
A leap second (`235960`) is read as the 59th second.

The identifier of a `TZID` parameter is resolved, in order:

1. as a time zone of the platform (`TimeZoneInfo.FindSystemTimeZoneById`);
2. from the `VTIMEZONE` component of the calendar with that `TZID`, which builds a custom `TimeZoneInfo` whose
   `Id` is the identifier, so the event is written back with it. Every onset of the sub-components (`DTSTART`,
   `RDATE`, bounded or open-ended `RRULE`) is expanded, and each year becomes one adjustment rule holding its
   standard offset and at most one daylight saving period, including a period spanning the new year and a change of
   the standard offset (the latter needs .NET 6 or later). An open-ended `STANDARD`/`DAYLIGHT` pair expressible as a
   floating (`BYDAY=-1SU`) or fixed (`BYMONTHDAY=22`) transition becomes a single open-ended rule; any other
   open-ended recurrence is expanded for 300 years. A year whose offset changes more often than that, such as a
   daylight saving period suspended during Ramadan, cannot be expressed by a `TimeZoneInfo`, so its shortest periods
   take the offset of the one before;
3. as the IANA identifier ending a prefixed one, such as `/mozilla.org/20050126_1/America/New_York`.

An identifier that still cannot be resolved does not reject the calendar: `DTSTART` and `DTEND` are read as
floating values and `Event.TimeZone` stays `null`. Only `DTSTART` and `DTEND` determine `Event.TimeZone`;
`CREATED`, `LAST-MODIFIED` and `DTSTAMP` are always read as `Utc` values, converted from their `TZID` when they
carry one, and taken as UTC when they are floating.

A `DATE` value, `DTSTART;VALUE=DATE:20240101` (or a date without the parameter), sets `Event.IsAllDay` and is
read as the first instant of the day, without a time zone. An event without `STATUS` has a `null` `Event.Status`.

An event property the model does not have, such as `X-MICROSOFT-CDO-BUSYSTATUS:OOF`, goes to
`Event.AdditionalProperties` when its `TEXT` value writes it back unchanged. Any other one — a property with
parameters (`EXDATE;TZID=Europe/Paris:20240104T100000`), a repeated one, or a structured value
(`GEO:37.38;-122.08`, `CATEGORIES:WORK,MEETING`) — goes to `Event.RawProperties`, which keeps its name,
parameters and value verbatim. Calendar properties go to `InternetCalendar.AdditionalProperties` and
`InternetCalendar.RawProperties` the same way. The components the model does not represent — `VTODO`,
`VJOURNAL`, `VFREEBUSY` and `VALARM` — are skipped.

### Properties written

- Content lines longer than 75 UTF-8 octets are folded, never inside a character.
- `CREATED`, `LAST-MODIFIED` and `DTSTAMP` are written in UTC (an `Unspecified` value is taken as UTC), and are
  omitted when not set, as are `DTEND` and `STATUS`. RFC 5545 requires `DTSTAMP`, so set `Event.DateTimeStamp`
  to produce a conforming event; the library does not use the current time, which keeps the output deterministic.
- `Event.IsAllDay` writes the date part of `Start` and `End` as `DTSTART;VALUE=DATE:`/`DTEND;VALUE=DATE:`,
  ignoring `Event.TimeZone`.
- An attendee or an organizer without an address is not written.
- A time zone identifier containing `:`, `;` or `,`, such as `(UTC+01:00) Amsterdam, Berlin`, is quoted in the
  `TZID` parameter and escaped in the `VTIMEZONE` `TZID` property. An identifier containing a `"` or a control
  character cannot be written, and `ToIcs` throws an `InvalidOperationException` before writing anything.
- `AdditionalProperties` values are escaped as `TEXT`. `RawProperties` are written verbatim; an
  `InternetCalendarProperty` validates its name, parameters and value when it is created, so it cannot inject
  a line:

  ````c#
  @event.RawProperties.Add(new InternetCalendarProperty("EXDATE", [new("TZID", "Europe/Paris")], "20240104T100000"));
  @event.RawProperties.Add(new InternetCalendarProperty("GEO", "37.386013;-122.082932"));
  ````

- A property of either collection named after one the writer emits itself — `BEGIN`, `END`, `VERSION` and
  `PRODID` for the calendar; `UID`, `STATUS`, `ORGANIZER`, `ATTENDEE`, `CREATED`, `LAST-MODIFIED`, `DTSTAMP`,
  `DTSTART`, `DTEND`, `RRULE`, `SUMMARY` and `DESCRIPTION` as well for an event — is not written.

### Writing

`InternetCalendar` writes events in the iCalendar format. Setting `Event.TimeZone` writes the start and end
as `DTSTART;TZID=`/`DTEND;TZID=` and emits a matching `VTIMEZONE` component:

````c#
var calendar = new InternetCalendar();
calendar.Events.Add(new Event
{
    Start = new DateTime(2024, 01, 02, 08, 00, 00),
    End = new DateTime(2024, 01, 02, 09, 00, 00),
    TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"),
});

var ics = calendar.ToIcs();
````

````text
BEGIN:VTIMEZONE
TZID:America/New_York
BEGIN:DAYLIGHT
DTSTART:20070311T020000
TZOFFSETFROM:-0500
TZOFFSETTO:-0400
RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=2SU
END:DAYLIGHT
BEGIN:STANDARD
DTSTART:20071104T020000
TZOFFSETFROM:-0400
TZOFFSETTO:-0500
RRULE:FREQ=YEARLY;BYMONTH=11;BYDAY=1SU
END:STANDARD
END:VTIMEZONE
...
DTSTART;TZID=America/New_York:20240102T080000
````

A `Start` or `End` whose `Kind` is `Unspecified` is taken as a wall-clock reading in that time zone; a `Utc`
or `Local` value denotes an instant and is converted to it. The component describes the adjustment rule in
effect for the events, expanded to an open-ended yearly recurrence.

## Cron expressions

The library also provides `CronExpression` to parse and evaluate cron schedules.

````c#
var cron = CronExpression.Parse("0 */15 * * * *");
var occurrences = cron.GetNextOccurrences(DateTime.Now).Take(10).ToArray();
````

### Supported formats

- 5 fields: `minute hour day-of-month month day-of-week`
- 6 fields: `second minute hour day-of-month month day-of-week`
- 7 fields: `second minute hour day-of-month month day-of-week year`

Fields are separated by spaces or tabs. When using the 5-field format, seconds are implicitly set to `0`.

### Field ranges

- second: `0-59`
- minute: `0-59`
- hour: `0-23`
- day-of-month: `1-31`
- month: `1-12` or `JAN-DEC`
- day-of-week: `0-7` or `SUN-SAT` (`0` and `7` = Sunday, so `1-7` means every day)
- year (optional): `1970-2099`

### Operators and special values

For all fields:

- `*` or `?`: any value
- `a,b,c`: list. Each item can be a value, a range, a step, `*` or `*/n` (for example `*/15,7`). A `*` item means any value. `?` is only valid as the whole field.
- `a-b`: range
- `*/n`: step from field minimum
- `a-b/n`: stepped range
- `a/n`: step starting at `a`

A range whose start is greater than its end wraps around the end of the field, except in the year field where it is invalid:

- `22-2` in the hour field means `22,23,0,1,2`
- `22-2/2` in the hour field means `22,0,2`
- `FRI-MON` in the day-of-week field means `5,6,0,1`
- `NOV-FEB` in the month field means `11,12,1,2`

Day-of-month field additionally supports:

- `L`: last day of month
- `L-n`: nth day before end of month (for example `L-2`, `n` in `0-30`)
- `LW`: last weekday of month
- `nW`: nearest weekday to day `n` (`n` in `1-31`), without leaving the month

Day-of-week field additionally supports:

- `nL`: last occurrence of weekday `n` in month
- `n#m`: m-th occurrence of weekday `n` in month (`m` in `1-5`)

### Predefined schedules

- `@yearly` / `@annually`
- `@monthly`
- `@weekly`
- `@daily` / `@midnight`
- `@hourly`

### Notes

- Parsing is case-insensitive for month/day names, special values (`L`, `W`), and predefined schedules.
- Occurrences are whole seconds. A start date with a fractional second starts at the next whole second.
- `day-of-month` and `day-of-week` are combined with **AND** semantics. A date must satisfy both fields to match.
