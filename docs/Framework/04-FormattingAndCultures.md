---
title: Formatting and cultures
description: Format values with the invariant culture, run code with another culture, compare cultures, and write ordinals and country flags.
---

## Format with the invariant culture

`ToString()` uses the current culture: `1234.5` becomes `1234,5` in French. Values written to files, logs, URLs or
protocols must be formatted the same way everywhere:

<!-- ref:../../tests/Meziantou.Framework.Tests/CultureInfoUtilitiesTests.cs;region=FormattableExtensions_ToStringInvariant;format=md-fenced-code-block;dedent=true -->
```csharp
// Numbers, dates and any IFormattable, formatted with the invariant culture
Assert.Equal("1234.5", 1234.5.ToStringInvariant());
Assert.Equal("1,234.50", 1234.5m.ToStringInvariant("N2"));
Assert.Equal("2024-03-15", new DateOnly(2024, 3, 15).ToStringInvariant("yyyy-MM-dd"));
Assert.Equal("00:30:00", TimeSpan.FromMinutes(30).ToStringInvariant());
```
<!-- endref -->

`ToStringInvariant` exists for the numeric types (`int`, `long`, `double`, `decimal`, `Half` and the others), for
`DateTime`, `DateOnly` and `TimeOnly`, and for any `IFormattable`, with or without a format string.

## Append invariant values

<!-- ref:../../tests/Meziantou.Framework.Tests/StringBuilderExtensionsTests.cs;region=StringBuilderExtensions_AppendInvariant;format=md-fenced-code-block;dedent=true -->
```csharp
var sb = new StringBuilder();

// The values are formatted with the invariant culture, whatever the current culture
sb.AppendInvariant(1234.5).Append(' ');
sb.AppendInvariant($"{0.25:P0} of {1000:N0}").Append(' ');
sb.AppendFormatInvariant("{0:F2}", 3.14159);

Assert.Equal("1234.5 25 % of 1,000 3.14", sb.ToString());
```
<!-- endref -->

- `AppendInvariant` appends a number, a nullable number, an `IFormattable` or an object, formatted with the invariant
  culture.
- With an interpolated string, `AppendInvariant` formats every hole with the invariant culture.
- `AppendFormatInvariant` is `AppendFormat` with the invariant culture.

## Run code with another culture

<!-- ref:../../tests/Meziantou.Framework.Tests/CultureInfoUtilitiesTests.cs;region=CultureInfoUtilities_UseCulture;format=md-fenced-code-block;dedent=true -->
```csharp
// Run code with another current culture, then restore the previous one
var formatted = CultureInfoUtilities.UseCulture("fr-FR", () => 1234.5.ToString("N1", CultureInfo.CurrentCulture));
Assert.Equal("1\u202f234,5", formatted);

// Compare cultures by language
var canadianFrench = CultureInfo.GetCultureInfo("fr-CA");
Assert.Equal("fr", canadianFrench.GetNeutralCulture().Name);
Assert.True(canadianFrench.NeutralEquals(CultureInfo.GetCultureInfo("fr-FR")));
```
<!-- endref -->

- `CultureInfoUtilities.UseCulture` sets the current culture and the current UI culture of the thread, runs the
  delegate, and restores the previous cultures, even when the delegate throws. It takes a culture or a culture name,
  and an `Action` or a `Func<T>`.
- The delegate is synchronous. With an `async` lambda, the cultures are restored at the first `await` that does not
  complete synchronously.
- `SetCurrentThreadCulture` sets both cultures without restoring them.
- `GetCulture` returns a culture from its name or its LCID, or `null` when it does not exist.
- `GetNeutralCulture` returns the neutral culture (`fr` for `fr-CA`), and `NeutralEquals` tells whether two cultures
  share the same neutral culture.

Named cultures are not available when the application runs in invariant globalization mode.

## Ordinals

<!-- ref:../../tests/Meziantou.Framework.Tests/CultureInfoUtilitiesTests.cs;region=NumberExtensions_Ordinals;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal("1st", NumberExtensions.ToEnglishOrdinal(1, CultureInfo.InvariantCulture));
Assert.Equal("22nd", NumberExtensions.ToEnglishOrdinal(22, CultureInfo.InvariantCulture));
Assert.Equal("13th", NumberExtensions.ToEnglishOrdinal(13, CultureInfo.InvariantCulture));

Assert.Equal("1er", NumberExtensions.ToFrenchOrdinal(1, CultureInfo.InvariantCulture));
Assert.Equal("2e", NumberExtensions.ToFrenchOrdinal(2, CultureInfo.InvariantCulture));
```
<!-- endref -->

`ToEnglishOrdinal` and `ToFrenchOrdinal` add the English or French ordinal suffix to a positive number. Zero and
negative numbers are returned without a suffix. The format provider formats the number; without it, the current culture
is used.

## Country flags

<!-- ref:../../tests/Meziantou.Framework.Tests/CountryTests.cs;region=Country_GetUnicodeFlag;format=md-fenced-code-block;dedent=true -->
```csharp
var region = new RegionInfo("US");
string flag = Country.GetUnicodeFlag(region);
Assert.Equal("\U0001F1FA\U0001F1F8", flag); // US flag emoji

string flag2 = Country.GetUnicodeFlag("FR");
Assert.Equal("\U0001F1EB\U0001F1F7", flag2); // FR flag emoji
```
<!-- endref -->

`Country.GetUnicodeFlag` returns the flag emoji of a country, from a `RegionInfo` or a two-letter ISO region code. The
emoji is made of two regional indicator symbols: how it is displayed depends on the fonts of the system.
