---
title: Values and dates
description: How values are parsed, the date keywords and relative dates, the current time in tests, and parsing your own values.
---

The handlers that take a `TValue`, in both builders, parse the value of the term before calling your handler. A value
that cannot be parsed matches nothing.

## Parsed types

| Type | Accepted values |
| --- | --- |
| `int`, `long`, `short`, `byte` and their signed or unsigned versions, `Int128`, `UInt128`, `BigInteger`, `nint` | Integers, with the invariant culture. |
| `double`, `float`, `Half`, `decimal` | Numbers with a `.` decimal separator and optional `,` thousands separators. |
| `DateTime`, `DateTimeOffset` | Dates and times with the invariant culture, such as `2026-03-18` or `2026-03-18T10:30`. A value without an offset is read as UTC, and the result is in UTC. |
| `DateOnly`, `TimeOnly` | Dates and times with the invariant culture. |
| Enums | A name, case-insensitive, or a number. |
| `Nullable<T>` of these types | The same as `T`. |
| Other types | Their static `TryParse(string, IFormatProvider, out T)` with the invariant culture, or else their `TryParse(string, out T)`, or else `Convert.ChangeType`. |

Values never depend on the culture of the machine: `1.5` is one and a half everywhere.

## Date keywords

When `TValue` is `DateTime`, `DateTimeOffset` or `DateOnly`, or their nullable versions, these keywords are ranges:

| Keyword | Range |
| --- | --- |
| `today` | The current day. |
| `yesterday` | The previous day. |
| `this_week` or `"this week"` | From Monday to Sunday of the current week. |
| `this_month` or `"this month"` | The current month. |
| `last_month` or `"last month"` | The previous month. |
| `this_year` or `"this year"` | The current year. |
| `last_year` or `"last year"` | The previous year. |

Days, weeks, months and years are in UTC, and the keywords are case-insensitive. They work with [AddRangeHandler](cref:Meziantou.Framework.SimpleQueryLanguage.QueryBuilder{T}.AddRangeHandler) and
with [ExpressionQueryBuilder\<T\>.AddHandler\<TValue\>](cref:Meziantou.Framework.SimpleQueryLanguage.ExpressionQueryBuilder{T}.AddHandler). The other handlers of [QueryBuilder\<T\>](cref:Meziantou.Framework.SimpleQueryLanguage.QueryBuilder{T}) receive a single date, not
a range, so they do not accept them.

## Relative dates

`@today` is the start of the current day in UTC. Follow it with an offset in days (`d`) or weeks (`w`): `@today-3d`,
`@today+1w`. A relative date is a value like any other date, so it works with every operator and as a bound of a range:
`created>=@today-1w`, `created:@today-4w..@today-2w`.

## The current time

The keywords and relative dates use the current time when `Build` runs. Pass a `TimeProvider` to the constructor of
the builder to choose it, for instance to test a query:

<!-- ref:../../tests/Meziantou.Framework.SimpleQueryLanguage.Tests/QueryBuilderTests.cs;region=QueryBuilder_RelativeDates;format=md-fenced-code-block;dedent=true -->
```csharp
// Wednesday, March 18, 2026
var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 18, 10, 0, 0, TimeSpan.Zero));
var queryBuilder = new QueryBuilder<Issue>(timeProvider);
queryBuilder.AddRangeHandler<DateTime>("created", (issue, range) => range.IsInRange(issue.CreatedAt));

var issues = new[]
{
    new Issue("Monday", "alice", IsOpen: true, Comments: 0, new DateTime(2026, 3, 16, 9, 0, 0, DateTimeKind.Utc)),
    new Issue("March 1", "alice", IsOpen: true, Comments: 0, new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc)),
    new Issue("February 20", "alice", IsOpen: true, Comments: 0, new DateTime(2026, 2, 20, 9, 0, 0, DateTimeKind.Utc)),
    new Issue("New Year's Eve", "alice", IsOpen: true, Comments: 0, new DateTime(2025, 12, 31, 9, 0, 0, DateTimeKind.Utc)),
};

string[] Search(string query) => [.. issues.Where(queryBuilder.Build(query).Evaluate).Select(issue => issue.Title)];

// Weeks start on Monday
Assert.Equal(["Monday"], Search("created:this_week"));
Assert.Equal(["Monday", "March 1"], Search("created:\"this month\""));
Assert.Equal(["February 20"], Search("created:last_month"));
Assert.Equal(["New Year's Eve"], Search("created:last_year"));

// @today is the start of the current day in UTC
Assert.Equal(["Monday"], Search("created>=@today-1w"));
Assert.Equal(["March 1", "February 20"], Search("created:@today-4w..@today-2w"));
```
<!-- endref -->

`FakeTimeProvider` comes from the `Microsoft.Extensions.TimeProvider.Testing` package. Without a `TimeProvider`, the
builders use `TimeProvider.System`.

A query built yesterday still means yesterday's `today`: build the query again when it is kept for a long time.

## Parse your own values

Pass a [ScalarParser\<TValue\>](cref:Meziantou.Framework.SimpleQueryLanguage.ScalarParser{T}) to a handler to accept values of your own. It works like a `TryParse` method, and is
also used for the bounds of a range:

<!-- ref:../../tests/Meziantou.Framework.SimpleQueryLanguage.Tests/QueryBuilderTests.cs;region=QueryBuilder_CustomParser;format=md-fenced-code-block;dedent=true -->
```csharp
var queryBuilder = new QueryBuilder<Issue>();
queryBuilder.AddRangeHandler<int>(
    "comments",
    (issue, range) => range.IsInRange(issue.Comments),
    (string value, out int result) =>
    {
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            result = 0;
            return true;
        }

        return int.TryParse(value, CultureInfo.InvariantCulture, out result);
    });

var issue = new Issue("Crash when saving", "bob", IsOpen: true, Comments: 0, new DateTime(2026, 2, 3));
Assert.True(queryBuilder.Build("comments:none").Evaluate(issue));
Assert.True(queryBuilder.Build("comments:none..2").Evaluate(issue));
Assert.False(queryBuilder.Build("comments>none").Evaluate(issue));
```
<!-- endref -->

For a date type, the keywords and relative dates are tried before your parser.
