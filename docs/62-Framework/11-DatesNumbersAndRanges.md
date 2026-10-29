---
title: Dates, numbers and ranges
description: Find the start or end of a week, month or year, check that a value is in a range, copy a sign, and generate random values.
---

## Start and end of a period

<!-- ref:../../tests/Meziantou.Framework.Tests/DateTimeUtilitiesTests.cs;region=DateTimeExtensions_StartAndEnd;format=md-fenced-code-block;dedent=true -->
```csharp
var date = new DateTime(2026, 10, 29, 14, 30, 15, 250); // A Thursday

// StartOfWeek keeps the time of day
Assert.Equal(new DateTime(2026, 10, 26, 14, 30, 15, 250), date.StartOfWeek(DayOfWeek.Monday));

// StartOfMonth and StartOfYear return midnight, unless keepTime is true
Assert.Equal(new DateTime(2026, 10, 1), date.StartOfMonth());
Assert.Equal(new DateTime(2026, 10, 1, 14, 30, 15, 250), date.StartOfMonth(keepTime: true));
Assert.Equal(new DateTime(2026, 1, 1), date.StartOfYear());

// EndOfMonth returns midnight of the last day of the month
Assert.Equal(new DateTime(2026, 10, 31), date.EndOfMonth());

// TruncateMilliseconds removes everything below the second
Assert.Equal(new DateTime(2026, 10, 29, 14, 30, 15), date.TruncateMilliseconds());
```
<!-- endref -->

| Method | Returns |
| --- | --- |
| `StartOfWeek(DayOfWeek)` | The last given day of the week on or before the date, at the same time of day |
| `StartOfWeek()` | The same, with the first day of the week of the current culture |
| `StartOfMonth()`, `StartOfYear()` | The first day of the month or the year, at midnight |
| `StartOfMonth(keepTime: true)`, `StartOfYear(keepTime: true)` | The first day of the month or the year, at the same time of day |
| `EndOfMonth()` | The last day of the month, at midnight |
| `TruncateMilliseconds()` | The date without its fraction of a second |

- `StartOfWeek` keeps the time of day: use `.Date` on the result to get midnight.
- `EndOfMonth` returns midnight of the last day, not the last moment of the month. To select the dates of a month, compare
  with the start of the next month instead: `date >= start && date < start.AddMonths(1)`.
- The results keep the `Kind` of the date (`Utc`, `Local` or `Unspecified`).
- `StartOfWeek()` depends on the current culture: Monday in most of Europe, Sunday in the United States. Pass the day
  explicitly when the result must not depend on the user's settings.

## Ranges

`Range<T>` holds two bounds, `From` and `To`, of any type that implements `IComparable<T>`, and checks whether a value or
another range is between them. `Range.Create` infers the type of the bounds:

<!-- ref:../../tests/Meziantou.Framework.Tests/RangeTests.cs;region=Range_Boundaries;format=md-fenced-code-block;dedent=true -->
```csharp
var range = Range.Create(1, 10);

Assert.True(range.IsInRangeInclusive(10));       // 1 <= value <= 10
Assert.False(range.IsInRangeExclusive(10));      // 1 < value < 10
Assert.True(range.IsInRangeLowerInclusive(1));   // 1 <= value < 10
Assert.False(range.IsInRangeLowerInclusive(10));
Assert.True(range.IsInRangeUpperInclusive(10));  // 1 < value <= 10

// A range is in another range when both of its bounds are
Assert.True(range.IsInRangeInclusive(Range.Create(2, 5)));
Assert.False(range.IsInRangeInclusive(Range.Create(5, 15)));
```
<!-- endref -->

| Method | Lower bound | Upper bound |
| --- | --- | --- |
| `IsInRangeInclusive` | Included | Included |
| `IsInRangeExclusive` | Excluded | Excluded |
| `IsInRangeLowerInclusive` | Included | Excluded |
| `IsInRangeUpperInclusive` | Excluded | Included |

- Each method has an overload that takes a `Range<T>`: it returns `true` when both bounds of that range pass the check.
- For a reference type, such as `string`, a `null` bound is not checked: the range is open on that side.
- `Range<T>` is a read-only struct. Two ranges are equal when their bounds are equal.

`Meziantou.Framework.Range` and `Meziantou.Framework.Range<T>` have the same name as `System.Range`, the type of the
`..` operator. When both namespaces are imported, qualify the name or add a `using` alias.

## Copy the sign of a number

`MakeSameSignAs(sign)` returns the absolute value of the number with the sign of another one: `5.MakeSameSignAs(-1)` is
`-5`, and `(-5).MakeSameSignAs(3)` is `5`. It exists for `int`, `long`, `float`, `double` and `decimal`.

For `int`, `long` and `decimal`, the result is 0 when `sign` is 0. For `float` and `double`, it follows
`Math.CopySign`: a zero `sign` is positive or negative zero, and gives a positive or negative result.

## Random values

`RandomExtensions` adds methods to `Random` for the types it does not cover, and for picking values from a list:

<!-- ref:../../tests/Meziantou.Framework.Tests/RandomExtensionsTests.cs;region=RandomExtensions_Values;format=md-fenced-code-block;dedent=true -->
```csharp
// A seed gives the same values on every run; use Random.Shared otherwise
var random = new Random(42);

// The minimum is inclusive and the maximum is exclusive: a die roll is between 1 and 6
var die = random.NextInt32(1, 7);
Assert.InRange(die, 1, 6);

string[] colors = ["red", "green", "blue"];
Assert.Contains(random.NextFromList(colors), colors);

var code = random.NextString(length: 6, chars: "ABCDEFGHJKLMNPQRSTUVWXYZ23456789");
Assert.HasCount(6, code);

var date = random.NextDateTime(new DateTime(2026, 1, 1), new DateTime(2027, 1, 1));
Assert.Equal(2026, date.Year);
```
<!-- endref -->

| Method | Returns |
| --- | --- |
| `NextBoolean()` | `true` or `false` |
| `NextByte`, `NextSByte`, `NextInt16`, `NextInt32`, `NextInt64`, `NextUInt16`, `NextUInt32`, `NextUInt64` | An integer, from `min` (included) to `max` (excluded) |
| `NextSingle`, `NextDouble`, `NextDecimal` | A number from `min` (included) to `max` (excluded) |
| `NextDateTime(min, max)` | A date from `min` (included) to `max` (excluded) |
| `NextFromList(list)` | A random element of an array, a list, a collection, a span or a memory |
| `NextString(length, chars)` | A string of `length` characters taken from `chars` |
| `NextString(minLength, maxLength, chars)` | The same, with a length from `minLength` to `maxLength`, both included |

- The `min` and `max` parameters are optional: they default to the whole range of the type, or to 0 and 1 for
  `NextSingle` and `NextDouble`.
- `NextInt64` and `NextDecimal` scale the result of `Random.NextDouble`, so they cannot return every value of a large
  range. `NextUInt64` uses the 64 bits of a random value.
- These methods are not suited for security. Use `System.Security.Cryptography.RandomNumberGenerator` for passwords,
  tokens or keys.
