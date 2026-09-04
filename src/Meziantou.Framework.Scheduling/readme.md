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
and `DTSTART;TZID=America/New_York:20240102T080000` a wall-clock value together with `Event.TimeZone`,
which `TimeZoneInfo.FindSystemTimeZoneById` resolves from the identifier. A `VTIMEZONE` component is not
used to build the time zone, so an identifier the platform does not know is reported as an error rather
than silently dropped.

An event property the model does not have, such as `X-MICROSOFT-CDO-BUSYSTATUS`, goes to
`Event.AdditionalProperties`; the components the model does not represent — `VTODO`, `VJOURNAL`,
`VFREEBUSY`, `VALARM` and `VTIMEZONE` — are skipped.

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

When using the 5-field format, seconds are implicitly set to `0`.

### Field ranges

- second: `0-59`
- minute: `0-59`
- hour: `0-23`
- day-of-month: `1-31`
- month: `1-12` or `JAN-DEC`
- day-of-week: `0-6` or `SUN-SAT` (`0` = Sunday)
- year (optional): `1970-2099`

### Operators and special values

For all fields:

- `*` or `?`: any value
- `a,b,c`: list
- `a-b`: range
- `*/n`: step from field minimum
- `a-b/n`: stepped range
- `a/n`: step starting at `a`

Day-of-month field additionally supports:

- `L`: last day of month
- `L-n`: nth day before end of month (for example `L-2`)
- `LW`: last weekday of month
- `nW`: nearest weekday to day `n`

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

- Parsing is case-insensitive for month/day names and predefined schedules.
- `day-of-month` and `day-of-week` are combined with **AND** semantics. A date must satisfy both fields to match.
