---
title: Getting started
description: Install the package, format a date in a culture, and use a time provider in tests.
---

## Install the package

Add the `Meziantou.Framework.RelativeDate` package to your project:

<InstallNuGetPackage Id="Meziantou.Framework.RelativeDate" />

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework;` and
`using System.Globalization;`. They check their results with assertions such as `Assert.Equal`, which show what each
call returns.

## Format a date

[RelativeDate.Get](cref:Meziantou.Framework.RelativeDate.Get) takes the date, and [ToString](cref:Meziantou.Framework.RelativeDate.ToString) writes it relative to now:

<!-- ref:../../tests/Meziantou.Framework.RelativeDate.Tests/RelativeDateTests.cs;region=RelativeDate_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
DateTime dateTime = DateTime.UtcNow.AddMinutes(-5);
var relativeDate = RelativeDate.Get(dateTime).ToString(); // Uses the current UI culture
var relativeDateInEnglish = RelativeDate.Get(dateTime).ToString(format: null, CultureInfo.GetCultureInfo("en-US"));
var relativeDateInFrench = RelativeDate.Get(dateTime).ToString(format: null, CultureInfo.GetCultureInfo("fr-FR"));

Assert.Equal("5 minutes ago", relativeDateInEnglish);
Assert.Equal("il y a 5 minutes", relativeDateInFrench);
```
<!-- endref -->

`ToString(format, formatProvider)` takes the culture of the text as its format provider. Without a provider (`null`, or
[ToString()](cref:Meziantou.Framework.RelativeDate.ToString)), the text uses `CultureInfo.CurrentUICulture`, the culture of the user interface, and not
`CultureInfo.CurrentCulture`. The only formats are `null`, an empty string and `"G"`, which all give the same text;
any other format throws a `FormatException`.

[RelativeDate](cref:Meziantou.Framework.RelativeDate) implements `IFormattable`, so `string.Format` and interpolated strings with a culture pass that culture
to it. See [Languages and custom texts](04-LanguagesAndCustomTexts.md).

## Dates and their kind

[RelativeDate](cref:Meziantou.Framework.RelativeDate) needs to know the instant a date represents:

<!-- ref:../../tests/Meziantou.Framework.RelativeDate.Tests/RelativeDateTests.cs;region=RelativeDate_DateKinds;format=md-fenced-code-block;dedent=true -->
```csharp
var timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));

// A DateTimeOffset is an instant: its offset does not matter
var fromOffset = RelativeDate.Get(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.FromHours(2)), timeProvider);
Assert.Equal("2 hours ago", fromOffset.ToString(format: null, CultureInfo.InvariantCulture));

// A UTC DateTime
var fromUtc = RelativeDate.Get(new DateTime(2024, 6, 15, 11, 0, 0, DateTimeKind.Utc), timeProvider);
Assert.Equal("an hour ago", fromUtc.ToString(format: null, CultureInfo.InvariantCulture));

// A DateTime of unspecified kind is rejected: it could be local or UTC
var unspecified = new DateTime(2024, 6, 15, 11, 0, 0, DateTimeKind.Unspecified);
Assert.Throws<ArgumentException>(() => _ = RelativeDate.Get(unspecified, timeProvider));
```
<!-- endref -->

- A `DateTimeOffset` is an instant, whatever its offset.
- A `DateTime` must have the `Utc` or the `Local` kind. A local `DateTime` is read in the local time zone of the time
  provider (`TimeZoneInfo.Local` with the system time provider).
- A `DateTime` with the `Unspecified` kind throws an `ArgumentException`. Dates read from a database or parsed from
  text often have this kind: call `DateTime.SpecifyKind` with the kind they really have before you pass them.

Two [RelativeDate](cref:Meziantou.Framework.RelativeDate) values are equal, and compare, by their instant. They implement `IComparable<RelativeDate>` and the
comparison operators, so you can sort them.

## Use a time provider

By default, now is `TimeProvider.System`. Pass another `TimeProvider` to [RelativeDate.Get](cref:Meziantou.Framework.RelativeDate.Get) to control the clock, for
instance a `FakeTimeProvider` from the `Microsoft.Extensions.TimeProvider.Testing` package in a test:

<!-- ref:../../tests/Meziantou.Framework.RelativeDate.Tests/RelativeDateTests.cs;region=RelativeDate_Example;format=md-fenced-code-block;dedent=true -->
```csharp
// Basic usage
var pastDate = RelativeDate.Get(DateTime.UtcNow.AddHours(-2));
Assert.Equal("2 hours ago", pastDate.ToString(null, CultureInfo.InvariantCulture));

// With localization
var date = RelativeDate.Get(DateTime.UtcNow.AddMinutes(-30));
Assert.Equal("il y a 30 minutes", date.ToString(null, CultureInfo.GetCultureInfo("fr")));
Assert.Equal("30 minutes ago", date.ToString(null, CultureInfo.InvariantCulture));

// With custom TimeProvider for testing
var fakeTimeProvider = new FakeTimeProvider();
fakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero));
var testDate = RelativeDate.Get(new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc), fakeTimeProvider);
Assert.Equal("2 hours ago", testDate.ToString(null, CultureInfo.InvariantCulture));

var futureDate = RelativeDate.Get(new DateTime(2024, 1, 4, 12, 0, 0, DateTimeKind.Utc), fakeTimeProvider);
Assert.Equal("in 3 days", futureDate.ToString(null, CultureInfo.InvariantCulture));
```
<!-- endref -->

In an application, pass the `TimeProvider` that the rest of the code uses, often the one registered in dependency
injection, so the texts and the other time-dependent code agree on the current time.

## Next steps

- [Texts and thresholds](03-TextsAndThresholds.md): which text a date gives.
- [Languages and custom texts](04-LanguagesAndCustomTexts.md): the cultures and how to change the texts.
