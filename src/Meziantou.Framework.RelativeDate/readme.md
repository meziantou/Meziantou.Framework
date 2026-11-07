# Meziantou.Framework.RelativeDate

`Meziantou.Framework.RelativeDate` allows to get a relative date similar to "5 minutes ago". It supports both local and UTC dates as well as dates with offset (DateTimeOffset). Also, culture to use can be specified explicitly. If it is not, current thread's current UI culture is used. It supports Dutch, English, French, German, Italian, Japanese, Korean, Portuguese, Simplified Chinese, Spanish and Turkish. `RelativeDate` is in the `Meziantou.Framework` namespace, and `CultureInfo` in `System.Globalization`.

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
