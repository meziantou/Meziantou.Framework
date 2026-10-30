---
title: Texts and thresholds
description: The text RelativeDate writes for each distance between a date and now.
---

The text depends on the distance between the date and now. The texts below are the English ones, which the invariant
culture uses too:

<!-- ref:../../tests/Meziantou.Framework.RelativeDate.Tests/RelativeDateTests.cs;region=RelativeDate_Thresholds;format=md-fenced-code-block;dedent=true -->
```csharp
var timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));
string Format(TimeSpan offset)
{
    var date = RelativeDate.Get(timeProvider.GetUtcNow() + offset, timeProvider);
    return date.ToString(format: null, CultureInfo.InvariantCulture);
}

Assert.Equal("now", Format(TimeSpan.Zero));
Assert.Equal("30 seconds ago", Format(TimeSpan.FromSeconds(-30)));
Assert.Equal("a minute ago", Format(TimeSpan.FromSeconds(-90)));
Assert.Equal("10 minutes ago", Format(TimeSpan.FromMinutes(-10)));
Assert.Equal("an hour ago", Format(TimeSpan.FromMinutes(-50)));
Assert.Equal("5 hours ago", Format(TimeSpan.FromHours(-5)));
Assert.Equal("yesterday", Format(TimeSpan.FromHours(-30)));
Assert.Equal("3 days ago", Format(TimeSpan.FromDays(-3)));
Assert.Equal("one month ago", Format(TimeSpan.FromDays(-45)));
Assert.Equal("3 months ago", Format(TimeSpan.FromDays(-100)));
Assert.Equal("one year ago", Format(TimeSpan.FromDays(-400)));
Assert.Equal("2 years ago", Format(TimeSpan.FromDays(-1000)));

// Dates in the future
Assert.Equal("in 2 hours", Format(TimeSpan.FromHours(2)));
Assert.Equal("tomorrow", Format(TimeSpan.FromHours(30)));
Assert.Equal("in 3 days", Format(TimeSpan.FromDays(3)));
```
<!-- endref -->

| Distance | Past | Future |
| --- | --- | --- |
| Exactly 0 | `now` | |
| Less than 2 seconds | `one second ago` | `in one second` |
| 2 to 60 seconds | `X seconds ago` | `in X seconds` |
| 1 to 2 minutes | `a minute ago` | `in a minute` |
| 2 to 45 minutes | `X minutes ago` | `in X minutes` |
| 45 to 120 minutes | `an hour ago` | `in an hour` |
| 2 to 24 hours | `X hours ago` | `in X hours` |
| 24 to 48 hours | `yesterday` | `tomorrow` |
| 2 to 30 days | `X days ago` | `in X days` |
| 30 to 60 days | `one month ago` | `in one month` |
| 60 to 365 days | `X months ago` | `in X months` |
| 365 to 730 days | `one year ago` | `in one year` |
| 730 days or more | `X years ago` | `in X years` |

- The numbers are whole units, rounded down: 90 seconds is `a minute ago`, and 5 hours and 50 minutes is
  `5 hours ago`.
- A month is 30 days and a year is 365 days. The text does not depend on the calendar: `yesterday` means 24 to 48
  hours ago, not the previous calendar day.
- The number is formatted with the culture of the text.
