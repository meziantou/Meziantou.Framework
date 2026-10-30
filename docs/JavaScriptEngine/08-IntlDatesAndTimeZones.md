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
  the [TimeProvider](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeProvider), and maps a Windows identifier to its IANA identifier; an unknown time zone is UTC. An identifier
  that is not a time zone of the database, such as an offset, throws an `InvalidOperationException` when a script first
  uses the local time zone.
- Set both explicitly on a server: otherwise, the scripts depend on the settings of the machine they run on.

## Control the clock

[TimeProvider](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeProvider) is the clock of `Date.now()`, `new Date()` and `Temporal.Now`, and gives the local time zone when
[TimeZone](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeZone) is not set. Give a fixed clock to make the results of the scripts reproducible, for instance in tests:

<!-- ref:../../samples/Meziantou.Framework.JavaScriptEngine.Samples/FixedTimeProvider.cs;region=JavaScriptEngine_FixedTimeProvider;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>A clock that always gives the same time: <c>Date.now()</c>, <c>new Date()</c> and <c>Temporal.Now</c> use it.</summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
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
