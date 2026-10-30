---
title: Languages and custom texts
description: Write relative dates in the included languages, add a language, or change the texts.
---

## Choose the language

The texts are included in Dutch, English, French, German, Italian, Japanese, Korean, Portuguese, Simplified Chinese,
Spanish and Turkish. Pass the culture to [ToString](cref:Meziantou.Framework.RelativeDate.ToString):

<!-- ref:../../tests/Meziantou.Framework.RelativeDate.Tests/RelativeDateTests.cs;region=RelativeDate_Cultures;format=md-fenced-code-block;dedent=true -->
```csharp
var timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));
var date = RelativeDate.Get(timeProvider.GetUtcNow().AddDays(-3), timeProvider);

Assert.Equal("3 days ago", date.ToString(format: null, CultureInfo.GetCultureInfo("en-US")));
Assert.Equal("il y a 3 jours", date.ToString(format: null, CultureInfo.GetCultureInfo("fr-FR")));
Assert.Equal("vor 3 Tagen", date.ToString(format: null, CultureInfo.GetCultureInfo("de")));
Assert.Equal("hace 3 días", date.ToString(format: null, CultureInfo.GetCultureInfo("es")));

// A language without translations uses the English texts
Assert.Equal("3 days ago", date.ToString(format: null, CultureInfo.GetCultureInfo("sv-SE")));

// string.Format and interpolated strings pass their culture to RelativeDate
Assert.Equal("Modifié il y a 3 jours", string.Create(CultureInfo.GetCultureInfo("fr-FR"), $"Modifié {date}"));
```
<!-- endref -->

- A specific culture uses the texts of its language: `fr-FR` and `fr-CA` use the French texts.
- A culture without texts uses the English ones, as does the invariant culture.
- Without a culture, the text uses `CultureInfo.CurrentUICulture`. In ASP.NET Core, the request localization middleware
  sets it for each request.
- The format provider can be a `CultureInfo`, or any `IFormatProvider` that returns a `CultureInfo` from
  `GetFormat(typeof(CultureInfo))`.

The translations are .NET satellite resource assemblies. If your project sets the `SatelliteResourceLanguages`
MSBuild property to keep only some languages, add the languages you display, or they fall back to English.

## Change the texts

The texts come from [LocalizationProvider.Current](cref:Meziantou.Framework.LocalizationProvider.Current), an [ILocalizationProvider](cref:Meziantou.Framework.ILocalizationProvider) used by every [RelativeDate](cref:Meziantou.Framework.RelativeDate). Its
`GetString(name, culture)` method returns the text of a resource for a culture.

To change some texts, write a provider that returns your texts and asks the previous provider for the others:

<!-- ref:../../tests/Meziantou.Framework.RelativeDate.Tests/RelativeDateTests.cs;region=RelativeDate_CustomTextsProvider;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class CasualEnglishLocalizationProvider(ILocalizationProvider inner) : ILocalizationProvider
{
    public string GetString(string name, CultureInfo? culture)
    {
        var text = (culture?.TwoLetterISOLanguageName, name) switch
        {
            ("en", "Now") => "just now",
            ("en", "OneSecondAgo" or "ManySecondsAgo") => "a few seconds ago",
            _ => null,
        };

        return text ?? inner.GetString(name, culture);
    }
}
```
<!-- endref -->

Set it once, at the start of the application:

<!-- ref:../../tests/Meziantou.Framework.RelativeDate.Tests/RelativeDateTests.cs;region=RelativeDate_UseCustomTexts;format=md-fenced-code-block;dedent=true -->
```csharp
// Wrap the current provider, so the built-in texts stay available
LocalizationProvider.Current = new CasualEnglishLocalizationProvider(LocalizationProvider.Current);

var timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));
var date = RelativeDate.Get(timeProvider.GetUtcNow().AddSeconds(-20), timeProvider);

Assert.Equal("a few seconds ago", date.ToString(format: null, CultureInfo.GetCultureInfo("en-US")));
Assert.Equal("il y a 20 secondes", date.ToString(format: null, CultureInfo.GetCultureInfo("fr-FR")));
```
<!-- endref -->

[LocalizationProvider.Current](cref:Meziantou.Framework.LocalizationProvider.Current) is global, and setting it to `null` throws an `ArgumentNullException`. Keep the previous
provider to restore it: the built-in provider is not public, so [LocalizationProvider.Current](cref:Meziantou.Framework.LocalizationProvider.Current), read before you replace
it, is the only way to reach it. In tests that change the provider, restore it in a `finally` block and do not run
these tests in parallel with the others.

The same approach adds a language: return your translations when `culture` is that language, and the previous
provider's texts otherwise.

### Resource names

| Name | English text |
| --- | --- |
| `Now` | `now` |
| `OneSecondAgo`, `InOneSecond` | `one second ago`, `in one second` |
| `ManySecondsAgo`, `InManySeconds` | `{0} seconds ago`, `in {0} seconds` |
| `AMinuteAgo`, `InAMinute` | `a minute ago`, `in a minute` |
| `ManyMinutesAgo`, `InManyMinutes` | `{0} minutes ago`, `in {0} minutes` |
| `AnHourAgo`, `InAnHour` | `an hour ago`, `in an hour` |
| `ManyHoursAgo`, `InManyHours` | `{0} hours ago`, `in {0} hours` |
| `Yesterday`, `Tomorrow` | `yesterday`, `tomorrow` |
| `ManyDaysAgo`, `InManyDays` | `{0} days ago`, `in {0} days` |
| `OneMonthAgo`, `InOneMonth` | `one month ago`, `in one month` |
| `ManyMonthsAgo`, `InManyMonths` | `{0} months ago`, `in {0} months` |
| `OneYearAgo`, `InOneYear` | `one year ago`, `in one year` |
| `ManyYearsAgo`, `InManyYears` | `{0} years ago`, `in {0} years` |

`{0}` is replaced by the number, formatted with the culture. A text without `{0}` is used as is.
