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
