---
title: Time zones
description: Compute occurrences in a time zone, keep their wall-clock time across daylight saving time changes, and start from an instant.
---

A schedule is usually written in the time zone of a person or a business: a meeting at 09:00 in New York stays at 09:00
when New York switches to summer time, so its UTC offset changes. Pass the time zone to compute such occurrences.

## Occurrences in a time zone

`GetNextOccurrences(DateTime, TimeZoneInfo)` reads the start date as a wall-clock time in the time zone, and returns
`DateTimeOffset` values that carry the offset in effect at each occurrence:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/RecurrenceRuleTests.cs;region=RecurrenceRule_TimeZone;format=md-fenced-code-block;dedent=true -->
```csharp
var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9");
var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

var occurrences = rrule.GetNextOccurrences(new DateTime(2024, 03, 09), timeZone)
    .Take(3)
    .Select(occurrence => occurrence.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));

Assert.Equal(
    [
        "2024-03-09 09:00:00 -05:00",
        "2024-03-10 09:00:00 -04:00", // the offset changes, the wall clock does not
        "2024-03-11 09:00:00 -04:00",
    ],
    occurrences);
```
<!-- endref -->

- The `Kind` of the start date is ignored: it is a wall-clock time in the time zone, as `DTSTART;TZID=America/New_York:`
  is in an iCalendar file.
- `CronExpression` has the same overloads:

  <!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/CronExpressionTests.cs;region=CronExpression_TimeZone;format=md-fenced-code-block;dedent=true -->
  ```csharp
  var cron = CronExpression.Parse("0 9 * * *");
  var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

  var occurrences = cron.GetNextOccurrences(new DateTime(2024, 03, 30), timeZone).Take(3);

  // 09:00 in Paris, before and after the switch to summer time
  Assert.Equal(
      [
          new DateTimeOffset(2024, 03, 30, 09, 00, 00, TimeSpan.FromHours(1)),
          new DateTimeOffset(2024, 03, 31, 09, 00, 00, TimeSpan.FromHours(2)),
          new DateTimeOffset(2024, 04, 01, 09, 00, 00, TimeSpan.FromHours(2)),
      ],
      occurrences);

  // A time zone identifier works too
  Assert.Equal(occurrences, cron.GetNextOccurrences(new DateTime(2024, 03, 30), "Europe/Paris").Take(3));
  ```
  <!-- endref -->

## Time zone identifiers

The overloads that take a `string` look the time zone up by its identifier:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/RecurrenceRuleTests.cs;region=RecurrenceRule_TimeZoneId;format=md-fenced-code-block;dedent=true -->
```csharp
var rrule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9");
var startDate = new DateTime(2024, 03, 09);

var occurrences = rrule.GetNextOccurrences(startDate, "America/New_York");
Assert.Equal(new DateTimeOffset(2024, 03, 10, 09, 00, 00, TimeSpan.FromHours(-4)), occurrences.ElementAt(1));
```
<!-- endref -->

- On .NET 6 and later, IANA identifiers (`America/New_York`) and Windows identifiers (`Eastern Standard Time`) are
  accepted on every platform.
- On .NET Framework, the runtime cannot convert between them, so an IANA identifier does not resolve on Windows: find the
  `TimeZoneInfo` yourself and use the overload that takes one.
- An identifier that does not resolve throws a `TimeZoneNotFoundException`.

## Daylight saving time changes

A wall-clock time that a change of the clock makes invalid or ambiguous is resolved as RFC 5545 section 3.3.5 requires:

- **A time that does not exist**, because the clock skips it, is read with the offset in effect before the change, so it
  moves forward by the length of the skipped interval:

  <!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/RecurrenceRuleTests.cs;region=RecurrenceRule_TimeZoneGap;format=md-fenced-code-block;dedent=true -->
  ```csharp
  // 02:30 does not exist in Paris on March 31, 2024: the clocks jump from 02:00 to 03:00
  var rule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=2;BYMINUTE=30");
  var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

  var occurrences = rule.GetNextOccurrences(new DateTime(2024, 03, 30), timeZone).Take(3);

  Assert.Equal(
      [
          new DateTimeOffset(2024, 03, 30, 02, 30, 00, TimeSpan.FromHours(1)),
          new DateTimeOffset(2024, 03, 31, 03, 30, 00, TimeSpan.FromHours(2)), // read with the offset before the gap
          new DateTimeOffset(2024, 04, 01, 02, 30, 00, TimeSpan.FromHours(2)),
      ],
      occurrences);
  ```
  <!-- endref -->

- **A time that exists twice**, because the clock goes back, is the first one: the repeated hour is visited once.
- Only a date that does not exist, such as February 30, is dropped from the occurrences.

Moving a skipped time forward can place it after the occurrences that follow it: `FREQ=MINUTELY;INTERVAL=40` from 00:00 in
Paris on March 29, 2026 gives 01:20+01:00, 03:00+02:00 (from 02:00), 03:20+02:00, 03:40+02:00 (from 02:40) and
04:00+02:00. The occurrences are still returned in increasing order. An occurrence that denotes the same instant as
another one, as 02:00 and 03:00 do for an hourly rule on that day, is returned once, and counts once for `COUNT`.

## Start from an instant

The `DateTimeOffset` overloads start from an instant, such as `DateTimeOffset.UtcNow`, rather than from a wall-clock time:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/RecurrenceRuleTests.cs;region=RecurrenceRule_TimeZoneInstant;format=md-fenced-code-block;dedent=true -->
```csharp
var rule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9;BYMINUTE=0;BYSECOND=0");
var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

// An instant: 10:00 in Tokyo. The 09:00 occurrence of that day is before it, so it is not returned
var start = new DateTimeOffset(2024, 01, 01, 01, 00, 00, TimeSpan.Zero);
var next = rule.GetNextOccurrence(start, timeZone);

Assert.Equal(new DateTimeOffset(2024, 01, 02, 09, 00, 00, TimeSpan.FromHours(9)), next);
```
<!-- endref -->

- The series starts at the wall-clock time of the instant in the time zone, but no occurrence before the instant is
  returned.
- During the hour that a change of the clock repeats, two instants have the same wall-clock time. When the start is the
  second one, the occurrences of the repeated hour are before it, so they are not returned; they still count for `COUNT`.
  Use the `DateTime` overload to start from the first one.
- An occurrence outside the range of `DateTimeOffset`, near year 1 or year 9999, is not returned.

## Without a time zone

The `DateTime` overloads without a time zone work on wall-clock values and ignore daylight saving time, even when the
`Kind` of the start date is `Local`. `GetNextOccurrence(DateTimeOffset)`, without a time zone, computes the occurrence
from the wall-clock time of the start date and gives it the offset of the start date. Use them for schedules that are not
tied to a time zone, or in UTC.

The `UNTIL` of a recurrence rule is compared as an instant when it is in UTC, and as a wall-clock time when it is
floating: see [End the series](03-RecurrenceRules.md#end-the-series).
