---
title: Recurrence rules
description: Parse RFC 5545 recurrence rules, enumerate their occurrences, write common schedules and modify a rule.
---

## Parse a rule

[RecurrenceRule.Parse](cref:Meziantou.Framework.Scheduling.RecurrenceRule.Parse) and [RecurrenceRule.TryParse](cref:Meziantou.Framework.Scheduling.RecurrenceRule.TryParse) read the value of an `RRULE` property, such as
`FREQ=WEEKLY;BYDAY=MO,WE;COUNT=10`. The parser follows RFC 5545 section 3.3.10 strictly, and [TryParse](cref:Meziantou.Framework.Scheduling.RecurrenceRule.TryParse) tells why a rule
is rejected:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/RecurrenceRuleTests.cs;region=RecurrenceRule_InvalidRule;format=md-fenced-code-block;dedent=true -->
```csharp
// BYMONTHDAY cannot be used with FREQ=WEEKLY
Assert.False(RecurrenceRule.TryParse("FREQ=WEEKLY;BYMONTHDAY=1", out _, out var error));
Assert.Equal("BYMONTHDAY cannot be used when FREQ is WEEKLY.", error);

// Parse throws a FormatException that contains the reason
var exception = Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=DAILY;CONUT=3"));
Assert.StartsWith("RRule value 'FREQ=DAILY;CONUT=3' is invalid: ", exception.Message);
```
<!-- endref -->

A rule is rejected when:

- a rule part is unknown or misspelled, such as `CONUT=3` or ` COUNT=3`. The extension rule parts of RFC 2445, whose
  name starts with `X-`, are accepted and ignored by the evaluation;
- the RFC forbids the combination, such as `BYMONTHDAY` with `FREQ=WEEKLY`, `BYSETPOS` without another `BY…` rule part,
  or `COUNT` with `UNTIL`;
- a number does not have the form the RFC defines: `BYHOUR=07` and `BYMONTHDAY=+01` are valid, but `BYHOUR=+7`,
  `BYHOUR=007`, `COUNT=+5` and `BYMONTH=January` are not;
- the value includes the property name, as in `RRULE:FREQ=DAILY`, or is empty.

The RFC 7529 `RSCALE` and `SKIP` rule parts are accepted with the `GREGORIAN` and `OMIT` values, which are the
behavior of the library.

## The rule parts

| Rule part | Meaning | Values |
| --- | --- | --- |
| `FREQ` | The frequency: `SECONDLY`, `MINUTELY`, `HOURLY`, `DAILY`, `WEEKLY`, `MONTHLY` or `YEARLY` | Required |
| `INTERVAL` | Every *n* periods of the frequency: `FREQ=WEEKLY;INTERVAL=2` is every other week | 1 by default |
| `COUNT` | The number of occurrences | Not with `UNTIL` |
| `UNTIL` | The last date of the series, included | Not with `COUNT` |
| `BYSECOND`, `BYMINUTE`, `BYHOUR` | The seconds, minutes and hours | 0-60 (60 is read as 59), 0-59, 0-23 |
| `BYDAY` | The days of the week, with an optional position in the month or the year: `MO`, `1MO` (first Monday), `-1FR` (last Friday) | `MO` to `SU` |
| `BYMONTHDAY` | The days of the month, negative from the end: `-1` is the last day | 1 to 31, -1 to -31; not with `WEEKLY` |
| `BYYEARDAY` | The days of the year | 1 to 366, -1 to -366; not with `DAILY`, `WEEKLY` or `MONTHLY` |
| `BYWEEKNO` | The weeks of the year | 1 to 53, -1 to -53; `YEARLY` only |
| `BYMONTH` | The months | 1 to 12 |
| `BYSETPOS` | Keeps the *n*-th occurrences of each period among the ones the other rule parts select: `-1` is the last one | 1 to 366, -1 to -366; with another `BY…` rule part |
| `WKST` | The first day of the week, for `BYWEEKNO` and for weekly intervals | `MO` by default |

## Common rules

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/RecurrenceRuleTests.cs;region=RecurrenceRule_CommonRules;format=md-fenced-code-block;dedent=true -->
```csharp
var dtStart = new DateTime(2024, 01, 01, 09, 00, 00); // a Monday

IEnumerable<DateTime> FirstThree(string rrule) => RecurrenceRule.Parse(rrule).GetNextOccurrences(dtStart).Take(3);

// Every weekday
Assert.Equal(
    [new DateTime(2024, 01, 01, 09, 00, 00), new DateTime(2024, 01, 02, 09, 00, 00), new DateTime(2024, 01, 03, 09, 00, 00)],
    FirstThree("FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR"));

// The first Monday of each month
Assert.Equal(
    [new DateTime(2024, 01, 01, 09, 00, 00), new DateTime(2024, 02, 05, 09, 00, 00), new DateTime(2024, 03, 04, 09, 00, 00)],
    FirstThree("FREQ=MONTHLY;BYDAY=1MO"));

// The last Friday of each month
Assert.Equal(
    [new DateTime(2024, 01, 26, 09, 00, 00), new DateTime(2024, 02, 23, 09, 00, 00), new DateTime(2024, 03, 29, 09, 00, 00)],
    FirstThree("FREQ=MONTHLY;BYDAY=-1FR"));

// The last weekday of each month
Assert.Equal(
    [new DateTime(2024, 01, 31, 09, 00, 00), new DateTime(2024, 02, 29, 09, 00, 00), new DateTime(2024, 03, 29, 09, 00, 00)],
    FirstThree("FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1"));

// The 15th and the last day of each month
Assert.Equal(
    [new DateTime(2024, 01, 15, 09, 00, 00), new DateTime(2024, 01, 31, 09, 00, 00), new DateTime(2024, 02, 15, 09, 00, 00)],
    FirstThree("FREQ=MONTHLY;BYMONTHDAY=15,-1"));

// Three times a day: the minutes and seconds come from the start date
Assert.Equal(
    [new DateTime(2024, 01, 01, 09, 00, 00), new DateTime(2024, 01, 01, 12, 00, 00), new DateTime(2024, 01, 01, 17, 00, 00)],
    FirstThree("FREQ=DAILY;BYHOUR=9,12,17"));

// February 29: the years without one are skipped
Assert.Equal(
    [new DateTime(2024, 02, 29, 09, 00, 00), new DateTime(2028, 02, 29, 09, 00, 00), new DateTime(2032, 02, 29, 09, 00, 00)],
    FirstThree("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=29"));
```
<!-- endref -->

An occurrence takes the components that the rule does not set from the start date: the time of every occurrence above is
the time of `dtStart`, 09:00:00. The same goes for the fraction of a second, which a rule cannot express:
`FREQ=DAILY;BYHOUR=9,10` from `08:15:30.5` gives `09:15:30.5` and `10:15:30.5`.

A date that does not exist, such as February 30 or February 29 of a common year, is skipped.

## The start of the series

The date passed to [GetNextOccurrences](cref:Meziantou.Framework.Scheduling.RecurrenceRule.GetNextOccurrences) is the start of the series, the `DTSTART` of the event, not the date to search
from. `COUNT` counts the occurrences from it, and `INTERVAL` and the periods are aligned on it. Passing `DateTime.Now`
starts a new series at every call, so a rule such as `FREQ=WEEKLY;INTERVAL=2;COUNT=10` would give other dates each time.

To get the occurrences of a series that are after a given date, enumerate from the start of the series and skip the
earlier ones:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/RecurrenceRuleTests.cs;region=RecurrenceRule_Upcoming;format=md-fenced-code-block;dedent=true -->
```csharp
var rule = RecurrenceRule.Parse("FREQ=WEEKLY;INTERVAL=2;COUNT=10");
var dtStart = new DateTime(2024, 01, 01, 09, 00, 00); // The DTSTART of the event
var now = new DateTime(2024, 03, 01); // DateTime.Now

var upcoming = rule.GetNextOccurrences(dtStart).SkipWhile(occurrence => occurrence < now).Take(5);
Assert.Equal(
    [
        new DateTime(2024, 03, 11, 09, 00, 00),
        new DateTime(2024, 03, 25, 09, 00, 00),
        new DateTime(2024, 04, 08, 09, 00, 00),
        new DateTime(2024, 04, 22, 09, 00, 00),
        new DateTime(2024, 05, 06, 09, 00, 00),
    ],
    upcoming);
```
<!-- endref -->

[GetNextOccurrence](cref:Meziantou.Framework.Scheduling.RecurrenceRuleExtensions.GetNextOccurrence) returns the first occurrence, or `null` when the series has none. For the next occurrence after a
date, filter the occurrences the same way:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/RecurrenceRuleTests.cs;region=RecurrenceRule_NextOccurrence;format=md-fenced-code-block;dedent=true -->
```csharp
var rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=1MO;COUNT=3");
var dtStart = new DateTime(2024, 01, 01, 09, 00, 00);

// The first occurrence of the series
Assert.Equal(new DateTime(2024, 01, 01, 09, 00, 00), rule.GetNextOccurrence(dtStart));

// The next occurrence after a date: enumerate the series and skip the earlier occurrences
var now = new DateTime(2024, 01, 15); // DateTime.Now
DateTime? next = rule.GetNextOccurrences(dtStart).Where(occurrence => occurrence > now).Cast<DateTime?>().FirstOrDefault();
Assert.Equal(new DateTime(2024, 02, 05, 09, 00, 00), next);

// null once the series is over
var afterTheEnd = new DateTime(2024, 06, 01);
Assert.Null(rule.GetNextOccurrences(dtStart).Where(occurrence => occurrence > afterTheEnd).Cast<DateTime?>().FirstOrDefault());
```
<!-- endref -->

## End the series

`COUNT` ends the series after a number of occurrences, `UNTIL` after a date. A rule with neither never ends: [IsForever](cref:Meziantou.Framework.Scheduling.RecurrenceRule.IsForever)
returns `true`.

`UNTIL` can be a date or a date-time:

- A date, `UNTIL=20240110`, includes the whole day:

  <!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/RecurrenceRuleTests.cs;region=RecurrenceRule_Until;format=md-fenced-code-block;dedent=true -->
  ```csharp
  // A date designates the whole day, so the occurrence of January 10 is included
  var rule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20240110");
  Assert.Equal(
      [
          new DateTime(2024, 01, 08, 09, 00, 00),
          new DateTime(2024, 01, 09, 09, 00, 00),
          new DateTime(2024, 01, 10, 09, 00, 00),
      ],
      rule.GetNextOccurrences(new DateTime(2024, 01, 08, 09, 00, 00)));

  Assert.Equal(new DateTime(2024, 01, 10), rule.EndDate);
  Assert.Equal(DateTimeKind.Unspecified, rule.EndDate?.Kind);
  Assert.False(rule.IsForever);
  ```
  <!-- endref -->

- A floating date-time, `UNTIL=20240110T100000`, is a wall-clock time, like a date. Both give an [EndDate](cref:Meziantou.Framework.Scheduling.RecurrenceRule.EndDate) whose `Kind`
  is `Unspecified`.
- A UTC date-time, `UNTIL=20240110T100000Z`, or one with an offset, is an instant: [EndDate](cref:Meziantou.Framework.Scheduling.RecurrenceRule.EndDate) is a `Utc` value.

An instant bounds the occurrences of a start date whose `Kind` is `Utc` or `Local`, or of a `DateTimeOffset`, by
instant; it bounds the occurrences of an `Unspecified` start date by wall clock. A wall-clock `UNTIL` always bounds them
by wall clock. `UNTIL` has a precision of one second, so an occurrence within the second of `UNTIL`, such as one that
takes the fraction of a second of the start date, is included.

## Modify a rule

The properties of a parsed rule can be changed, and `Text` writes the rule back:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/RecurrenceRuleTests.cs;region=RecurrenceRule_Modify;format=md-fenced-code-block;dedent=true -->
```csharp
var rule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9");
rule.Interval = 2;
rule.Occurrences = 3;
rule.ByMinutes = [30];

Assert.Equal("FREQ=DAILY;INTERVAL=2;COUNT=3;BYHOUR=9;BYMINUTE=30", rule.Text);
Assert.Equal(
    [
        new DateTime(2024, 01, 01, 09, 30, 00),
        new DateTime(2024, 01, 03, 09, 30, 00),
        new DateTime(2024, 01, 05, 09, 30, 00),
    ],
    rule.GetNextOccurrences(new DateTime(2024, 01, 01)));
```
<!-- endref -->

| Property | Rule part |
| --- | --- |
| [Interval](cref:Meziantou.Framework.Scheduling.RecurrenceRule.Interval) | `INTERVAL`; at least 1 |
| [Occurrences](cref:Meziantou.Framework.Scheduling.RecurrenceRule.Occurrences) | `COUNT`; not negative |
| [EndDate](cref:Meziantou.Framework.Scheduling.RecurrenceRule.EndDate) | `UNTIL` |
| [WeekStart](cref:Meziantou.Framework.Scheduling.RecurrenceRule.WeekStart) | `WKST` |
| [BySeconds](cref:Meziantou.Framework.Scheduling.RecurrenceRule.BySeconds), [ByMinutes](cref:Meziantou.Framework.Scheduling.RecurrenceRule.ByMinutes), [ByHours](cref:Meziantou.Framework.Scheduling.RecurrenceRule.ByHours), [ByMonths](cref:Meziantou.Framework.Scheduling.RecurrenceRule.ByMonths), [ByMonthDays](cref:Meziantou.Framework.Scheduling.RecurrenceRule.ByMonthDays), [BySetPositions](cref:Meziantou.Framework.Scheduling.RecurrenceRule.BySetPositions) | The `BY…` rule parts |

- [Occurrences](cref:Meziantou.Framework.Scheduling.RecurrenceRule.Occurrences) and [EndDate](cref:Meziantou.Framework.Scheduling.RecurrenceRule.EndDate) cannot both be set: setting one while the other has a value throws an
  `InvalidOperationException`.
- Setting [EndDate](cref:Meziantou.Framework.Scheduling.RecurrenceRule.EndDate) replaces a date with a date-time, which is an exact bound.
- The values of the `BY…` lists are checked when the enumeration of the occurrences starts, which throws an
  `InvalidOperationException` for a value such as `ByHours = [24]`.
- The rule is read when the enumeration starts: changing it does not change an enumeration in progress.
- `Text`, and `ToString`, write the rule with the invariant culture. The `X-` rule parts are written at the end.

## Weeks of the year

`BYWEEKNO` numbers the weeks as RFC 5545 does: week 1 is the first week, starting on `WKST`, that has at least 4 days of
the year. A `FREQ=YEARLY` rule with `BYWEEKNO` is evaluated on these week-numbering years, which can start at the end of
December and end at the beginning of January, so `INTERVAL` and `BYSETPOS` apply to them:
`FREQ=YEARLY;BYWEEKNO=1;BYSETPOS=-1` from January 1, 2024 gives January 7, 2024, January 5, 2025 and January 4, 2026.
`BYMONTH`, `BYMONTHDAY` and `BYYEARDAY` still apply to the calendar date of each day.
