---
title: Getting started
description: Install the package, then compute the occurrences of a recurrence rule and of a cron expression.
---

## Install the package

Add the `Meziantou.Framework.Scheduling` package to your project:

```bash
dotnet add package Meziantou.Framework.Scheduling
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.Scheduling;`.

## The occurrences of a recurrence rule

Parse a rule, then enumerate its occurrences from the start of the series:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/RecurrenceRuleTests.cs;region=RecurrenceRule_TryParse;format=md-fenced-code-block;dedent=true -->
```csharp
var rrule = "FREQ=WEEKLY;BYDAY=MO,WE;COUNT=10";
if (RecurrenceRule.TryParse(rrule, out var rule, out var error))
{
    var dtStart = new DateTime(2024, 01, 01, 09, 00, 00); // The DTSTART of the event
    var occurrences = rule.GetNextOccurrences(dtStart).ToArray();

    Assert.HasCount(10, occurrences);
    Assert.Equal(new DateTime(2024, 01, 01, 09, 00, 00), occurrences[0]); // Monday
    Assert.Equal(new DateTime(2024, 01, 03, 09, 00, 00), occurrences[1]); // Wednesday
    Assert.Equal(new DateTime(2024, 01, 31, 09, 00, 00), occurrences[9]);
}
else
{
    Assert.Fail(error);
}
```
<!-- endref -->

- [TryParse](cref:Meziantou.Framework.Scheduling.RecurrenceRule.TryParse) returns `false` and the reason in `error` when the rule is not valid. [Parse](cref:Meziantou.Framework.Scheduling.RecurrenceRule.Parse) throws a `FormatException`
  instead.
- The date you pass to [GetNextOccurrences](cref:Meziantou.Framework.Scheduling.RecurrenceRule.GetNextOccurrences) is the start of the series, the `DTSTART` of an iCalendar event: the first
  occurrences are computed from it, and the time of the occurrences comes from it. It is not "now": see
  [The start of the series](03-RecurrenceRules.md#the-start-of-the-series).
- `COUNT=10` ends the series after 10 occurrences. A rule without `COUNT` or `UNTIL` never ends: take the occurrences you
  need with `Take` or `TakeWhile`.

## The next run of a cron expression

Parse an expression, then get the occurrences from now:

<!-- ref:../../tests/Meziantou.Framework.Scheduling.Tests/CronExpressionTests.cs;region=CronExpression_Parse;format=md-fenced-code-block;dedent=true -->
```csharp
var cron = CronExpression.Parse("0 */15 * * * *");
var startDate = new DateTime(2024, 01, 01, 10, 05, 00); // DateTime.Now
var occurrences = cron.GetNextOccurrences(startDate).Take(10).ToArray();

Assert.HasCount(10, occurrences);
Assert.Equal(new DateTime(2024, 01, 01, 10, 15, 00), occurrences[0]);
Assert.Equal(new DateTime(2024, 01, 01, 10, 30, 00), occurrences[1]);
```
<!-- endref -->

`0 */15 * * * *` has six fields, the first one being the seconds: second 0 of every 15th minute. The occurrences are on or
after the date you pass; replace the fixed date with `DateTime.Now` in your application.

## Next steps

- [Recurrence rules](03-RecurrenceRules.md): the rule parts and the common schedules.
- [Cron expressions](04-CronExpressions.md): the syntax and the next occurrence.
- [Time zones](05-TimeZones.md): occurrences that follow daylight saving time.
