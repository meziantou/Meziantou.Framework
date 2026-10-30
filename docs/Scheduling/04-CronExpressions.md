---
title: Cron expressions
description: Parse cron expressions with 5, 6 or 7 fields, compute their next occurrences, and schedule a job with them.
---

## Parse an expression

`CronExpression.Parse` reads an expression and throws a `FormatException` when it is not valid. `TryParse` returns
`false` instead:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/CronExpressionTests.cs;region=CronExpression_TryParse;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.True(CronExpression.TryParse("*/5 * * * *", out var cron));
Assert.Equal("*/5 * * * *", cron.ToString());

// The minutes go from 0 to 59
Assert.False(CronExpression.TryParse("60 * * * *", out _));
Assert.Throws<FormatException>(() => CronExpression.Parse("60 * * * *"));
```
<!-- endref -->

On .NET 7 and later, `CronExpression` implements `IParsable<CronExpression>` and `ISpanParsable<CronExpression>`, so
generic code that parses values accepts it. `ToString` returns the text of the expression, without the spaces and tabs
around it.

## Formats

An expression has 5, 6 or 7 fields, separated by spaces or tabs:

| Fields | Format |
| --- | --- |
| 5 | `minute hour day-of-month month day-of-week`; the second is 0 |
| 6 | `second minute hour day-of-month month day-of-week` |
| 7 | `second minute hour day-of-month month day-of-week year` |

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/CronExpressionTests.cs;region=CronExpression_Formats;format=md-fenced-code-block;dedent=true -->
```csharp
// 5 fields: minute hour day-of-month month day-of-week
var weekdays = CronExpression.Parse("30 9 * * MON-FRI");

// 6 fields start with the seconds, 7 fields end with the year
Assert.Equal(weekdays, CronExpression.Parse("0 30 9 * * 1-5"));
Assert.Equal(weekdays, CronExpression.Parse("0 30 9 * * MON-FRI *"));

// Predefined schedules
Assert.Equal(CronExpression.Parse("0 0 * * *"), CronExpression.Parse("@daily"));

var occurrences = weekdays.GetNextOccurrences(new DateTime(2024, 01, 05, 12, 00, 00)).Take(2);
Assert.Equal(
    [
        new DateTime(2024, 01, 08, 09, 30, 00), // Monday
        new DateTime(2024, 01, 09, 09, 30, 00),
    ],
    occurrences);
```
<!-- endref -->

- The 6-field format starts with the seconds; it has no year field.
- The days of the week are numbered as in Unix cron, in every format: `0` or `7` is Sunday, `1` is Monday.
- Only spaces and tabs can separate the fields or surround the expression: a line feed or a non-breaking space makes it
  invalid.

| Field | Values |
| --- | --- |
| second | `0-59` |
| minute | `0-59` |
| hour | `0-23` |
| day-of-month | `1-31` |
| month | `1-12` or `JAN-DEC` |
| day-of-week | `0-7` or `SUN-SAT`; `1-7` is every day |
| year | `1970-9999` |

`*`, `?` or no year field matches every year, including the years before 1970, while `*/n` starts at 1970.

## Operators

| Syntax | Meaning | Example |
| --- | --- | --- |
| `*` or `?` | Any value; `?` only as the whole field | `*` |
| `a,b,c` | A list of values, ranges or steps | `*/15,7` |
| `a-b` | A range | `MON-FRI` |
| `*/n` | Every *n* values from the minimum of the field | `*/15` |
| `a-b/n` | Every *n* values of a range | `9-17/2` |
| `a/n` | Every *n* values from `a` to the maximum of the field | `5/10` |

- A step does not wrap around the end of the field: `*/90` in the minute field is `0`, and `5/90` is `5`.
- A range whose start is after its end wraps around the end of the field, except in the year field: `22-2` in the hour
  field is `22,23,0,1,2`, `FRI-MON` is `5,6,0,1`, `NOV-FEB` is `11,12,1,2`.
- In the day-of-week field, the maximum is `7`, so `1/2` is `1-7/2` and includes Sunday.

## Special values

| Field | Syntax | Meaning |
| --- | --- | --- |
| day-of-month | `L` | The last day of the month |
| day-of-month | `L-n` | *n* days before the last day of the month, with *n* from 0 to 30 |
| day-of-month | `LW` | The last weekday of the month |
| day-of-month | `nW` | The weekday nearest to day *n*, without leaving the month |
| day-of-week | `nL` | The last day *n* of the month: `5L` is the last Friday |
| day-of-week | `n#m` | The *m*-th day *n* of the month, with *m* from 1 to 5: `TUE#2` is the second Tuesday |

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/CronExpressionTests.cs;region=CronExpression_SpecialValues;format=md-fenced-code-block;dedent=true -->
```csharp
var start = new DateTime(2024, 02, 01);

// The last day of the month
Assert.Equal(new DateTime(2024, 02, 29, 18, 00, 00), CronExpression.Parse("0 18 L * *").GetNextOccurrence(start));

// The weekday nearest to the 17th (Saturday, February 17, 2024)
Assert.Equal(new DateTime(2024, 02, 16, 09, 00, 00), CronExpression.Parse("0 9 17W * *").GetNextOccurrence(start));

// The last Friday of the month
Assert.Equal(new DateTime(2024, 02, 23, 09, 00, 00), CronExpression.Parse("0 9 * * 5L").GetNextOccurrence(start));

// The second Tuesday of the month
Assert.Equal(new DateTime(2024, 02, 13, 09, 00, 00), CronExpression.Parse("0 9 * * TUE#2").GetNextOccurrence(start));

// Every 2 hours from 22:00 to 02:00: a range can wrap around the end of the field
Assert.Equal(
    [new DateTime(2024, 02, 01, 00, 00, 00), new DateTime(2024, 02, 01, 02, 00, 00), new DateTime(2024, 02, 01, 22, 00, 00)],
    CronExpression.Parse("0 22-2/2 * * *").GetNextOccurrences(start).Take(3));
```
<!-- endref -->

The predefined schedules are `@yearly` (or `@annually`), `@monthly`, `@weekly`, `@daily` (or `@midnight`) and `@hourly`.

Month names, day names, special values and predefined schedules are case-insensitive. Only ASCII letters are folded, so a
look-alike character such as `ı` (dotless i) is not accepted.

## Day of month and day of week

When both the day-of-month and the day-of-week fields are restricted, a date must match **both**. This differs from the
traditional Unix cron, which matches a date that satisfies either field: `0 0 1 * 1` only matches a Monday that is the first
day of a month, not every first day of a month and every Monday.

## Next occurrences

`GetNextOccurrences(startDate)` enumerates the dates that match the expression, on or after `startDate`, and
`GetNextOccurrence(startDate)` returns the first one, or `null` when there is none. Unlike a recurrence rule, the start
date is where the search starts, and it is included when it matches. To get the occurrence after a previous one, start
one second after it:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/CronExpressionTests.cs;region=CronExpression_NextOccurrence;format=md-fenced-code-block;dedent=true -->
```csharp
var cron = CronExpression.Parse("0 */15 * * * *");
var now = new DateTime(2024, 01, 01, 10, 05, 00, DateTimeKind.Utc); // DateTime.UtcNow

var next = cron.GetNextOccurrence(now);
Assert.NotNull(next);
Assert.Equal(new DateTime(2024, 01, 01, 10, 15, 00, DateTimeKind.Utc), next.Value);

// next matches the expression, so starting from it returns it again
Assert.Equal(next, cron.GetNextOccurrence(next.Value));

var following = cron.GetNextOccurrence(next.Value.AddSeconds(1));
Assert.Equal(new DateTime(2024, 01, 01, 10, 30, 00, DateTimeKind.Utc), following);
```
<!-- endref -->

Occurrences are whole seconds: a start date with a fraction of a second starts at the next whole second.

## Schedule a job

The `DateTime` overloads compare wall-clock values and ignore daylight saving time, even for a `Local` start date: an
occurrence can be in an hour that the clock skips, or be returned once for an hour that is repeated. To wait for the next
run of a job, use the overloads that take a time zone, and compute the delay between two instants:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/CronExpressionTests.cs;region=CronExpression_Delay;format=md-fenced-code-block;dedent=true -->
```csharp
var cron = CronExpression.Parse("0 */15 * * * *");
var now = new DateTimeOffset(2024, 01, 01, 10, 05, 00, TimeSpan.Zero); // DateTimeOffset.UtcNow

// The instant of the next occurrence, evaluated in UTC
var next = cron.GetNextOccurrence(now, TimeZoneInfo.Utc);
Assert.NotNull(next);

var delay = next.Value - now;
Assert.Equal(TimeSpan.FromMinutes(10), delay);
```
<!-- endref -->

Pass the time zone in which the schedule is written, such as the time zone of the user or of the business: see
[Time zones](05-TimeZones.md).

## Equality

Two expressions are equal (`Equals`, `==`) when their fields select the same values, whatever their text:
`*/15 * * * *` equals `0,15,30,45 * * * *`, and `@daily` equals `0 0 * * *`. Expressions written with different special
values, such as `0 0 29 2 *` and `0 0 L 2 *`, are not equal, even when they match the same dates. A `CronExpression` holds
no time zone.
