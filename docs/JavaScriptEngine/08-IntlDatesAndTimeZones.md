---
title: Intl, dates and time zones
description: Choose the default locale and time zone of the scripts, control their clock, and rely on the same results on every machine.
---

## Set the default locale and time zone

[DefaultLocale](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.DefaultLocale) is the locale of `Intl` and of the `toLocaleString` methods when a script does not give one, and
[TimeZone](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeZone) is the local time zone of `Date`, `Intl.DateTimeFormat` and `Temporal`:

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Locale;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine(new JsEngineOptions { DefaultLocale = "de-DE", TimeZone = "Europe/Berlin" });
var realm = engine.CreateRealm();

// The default locale and time zone of the scripts
Assert.Equal("1.234,5", realm.Execute("(1234.5).toLocaleString()").AsString());
Assert.Equal("29.10.2026, 13:00:00", realm.Execute("new Date(Date.UTC(2026, 9, 29, 12)).toLocaleString()").AsString());
Assert.Equal("de-DE|Europe/Berlin", realm.Execute("const options = new Intl.DateTimeFormat().resolvedOptions(); `${options.locale}|${options.timeZone}`").AsString());

// The scripts can ask for another locale or time zone
Assert.Equal("1,234.5", realm.Execute("new Intl.NumberFormat('en-US').format(1234.5)").AsString());
Assert.Equal("29 octobre 2026", realm.Execute("new Date(Date.UTC(2026, 9, 29, 12)).toLocaleDateString('fr-FR', { dateStyle: 'long', timeZone: 'UTC' })").AsString());
```
<!-- endref -->

- [DefaultLocale](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.DefaultLocale) is a BCP 47 language tag, `en-US` by default. A tag that is not valid, or that matches no locale of
  the data, is replaced by `en-US`.
- [TimeZone](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeZone) is an IANA identifier, such as `Europe/Paris`. When it is null, the engine uses the local time zone of
  the [TimeProvider](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeProvider), and maps a Windows identifier to its IANA identifier; a time zone that is not in the database
  uses its own rules (see [Use a custom time zone](#use-a-custom-time-zone)). An identifier that is not a time zone of
  the database, such as an offset, throws an `InvalidOperationException` when a script first uses the local time zone.
- Set both explicitly on a server: otherwise, the scripts depend on the settings of the machine they run on.

## Control the clock

[TimeProvider](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeProvider) is the clock of `Date.now()`, `new Date()` and `Temporal.Now`, and gives the local time zone when
[TimeZone](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeZone) is not set. Give a fixed clock to make the results of the scripts reproducible, for instance in tests:

<!-- ref:../../samples/Meziantou.Framework.JavaScriptEngine.Samples/FixedTimeProvider.cs;region=JavaScriptEngine_FixedTimeProvider;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>
/// A clock that always gives the same time: <c>Date.now()</c>, <c>new Date()</c> and <c>Temporal.Now</c> use it. The local
/// time zone is the one of the machine, unless one is given.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow, TimeZoneInfo? localTimeZone = null) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;

    public override TimeZoneInfo LocalTimeZone => localTimeZone ?? base.LocalTimeZone;
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Clock;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine(new JsEngineOptions
{
    TimeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 10, 29, 23, 30, 0, TimeSpan.Zero)),
    TimeZone = "Asia/Tokyo",
});
var realm = engine.CreateRealm();

Assert.Equal("2026-10-29T23:30:00.000Z", realm.Execute("new Date().toISOString()").AsString());
Assert.Equal("2026-10-30", realm.Execute("Temporal.Now.plainDateISO().toString()").AsString());
Assert.Equal(8, realm.Execute("new Date().getHours()").AsNumber());
```
<!-- endref -->

The same [TimeProvider](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeProvider) measures the delays of the timeout jobs of the engine (`Atomics.waitAsync`).

## Use a custom time zone

When [TimeZone](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeZone) is not set and the local time zone of the [TimeProvider](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeProvider) is not in the
IANA database, nor a Windows identifier of one of its zones, the engine uses the rules of the `TimeZoneInfo`. This is
the case of a zone created by `TimeZoneInfo.CreateCustomTimeZone`, for instance to test the scripts in a given time zone:

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_CustomTimeZone;format=md-fenced-code-block;dedent=true -->
```csharp
// A time zone that is not in the IANA database: +05:00, and +06:00 from the last Sunday of March to the last Sunday of October
var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
    DateTime.MinValue.Date,
    DateTime.MaxValue.Date,
    TimeSpan.FromHours(1),
    TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), month: 3, week: 5, DayOfWeek.Sunday),
    TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), month: 10, week: 5, DayOfWeek.Sunday));
var timeZone = TimeZoneInfo.CreateCustomTimeZone("Test/Zone", TimeSpan.FromHours(5), "Test zone", "Test standard time", "Test daylight time", [rule]);

using var engine = new JsEngine(new JsEngineOptions
{
    TimeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero), timeZone),
});
var realm = engine.CreateRealm();

Assert.Equal(18, realm.Execute("new Date().getHours()").AsNumber());
Assert.Equal(-300, realm.Execute("new Date(2026, 0, 1).getTimezoneOffset()").AsNumber());
Assert.Equal("Test/Zone", realm.Execute("Temporal.Now.timeZoneId()").AsString());
```
<!-- endref -->

- A zone without daylight saving time is an offset time zone: its identifier is its offset, such as `+05:00`.
- A zone with daylight saving time keeps the identifier of the `TimeZoneInfo`. The engine reads the transitions of its
  adjustment rules, and its last rule repeats every year when it never ends. `Temporal.Now.timeZoneId()` and
  `resolvedOptions().timeZone` return this identifier, but the scripts cannot pass it to `Temporal` or `Intl`: it is
  not a time zone of the database.
- Set [TimeZone](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeZone) to `UTC` to use UTC instead of such a zone.

## Parse dates

`Date.parse` and `new Date(string)` read the date time string format of ECMAScript (`2026-10-29T13:00:00.000+01:00`),
then the formats of `toString` and `toUTCString`, then the legacy formats that V8 reads (`Oct 29 2026 13:00 GMT+0100`,
`10/29/2026`, `2026-10-29 13:00:00`...). Like Node.js, the date time string format also accepts:

- a lowercase `t` separator or `z` designator: `2026-10-29t13:00:00z`;
- an offset without colon: `2026-10-29T13:00:00.000+0100`.

A string without offset is in the local time zone, except a date alone (`2026-10-29`), which is UTC. The parsing does
not depend on the culture of .NET: a string that Node.js does not read, such as `29/10/2026`, is `NaN`.

## The same results on every machine

The engine does not use the globalization of .NET or of the operating system: it embeds its own copy of the data.

- **Locales**: the CLDR data of the locales of the `modern` and `moderate` coverage levels, with their parents. It
  covers `Intl.Collator`, `DateTimeFormat`, `DisplayNames`, `DurationFormat`, `ListFormat`, `Locale`, `NumberFormat`,
  `PluralRules`, `RelativeTimeFormat` and `Segmenter`, and the `toLocaleString` methods.
- **Time zones**: the IANA time zone database, with the history of each zone, for `Date`, `Intl` and `Temporal`.
- **Unicode**: the character data of the regular expressions, of `normalize` and of the case conversions.
  `Intl.Segmenter` splits the words of Chinese, Japanese, Thai, Lao, Khmer and Burmese with the word lists of ICU.

So the results are the same on Windows, Linux and macOS, and in an application that runs in invariant globalization
mode. They change only when the data of the library is updated.
