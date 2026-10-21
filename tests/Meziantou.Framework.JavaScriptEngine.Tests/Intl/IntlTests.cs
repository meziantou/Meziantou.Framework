namespace Meziantou.Framework.JavaScriptEngine.Tests.Intl;

public sealed class IntlTests
{
    private static string Evaluate(string code, JsEngineOptions? options = null)
    {
        using var engine = new JsEngine(options);
        var realm = engine.CreateRealm();
        var result = realm.Execute(code);
        using var scope = engine.Enter();
        return JsOperations.ToString(result);
    }

    [Theory]
    [InlineData("EN-us", "en-US")]
    [InlineData("cmn-hans-cn-u-ca-t-ca-x-t-u", "zh-Hans-CN-t-ca-u-ca-x-t-u")]
    [InlineData("sh", "sr-Latn")]
    [InlineData("hy-SU", "hy-AM")]
    [InlineData("und-Armn-SU", "und-Armn-AM")]
    [InlineData("ja-latn-hepburn-heploc", "ja-Latn-alalc97")]
    [InlineData("art-lojban", "jbo")]
    [InlineData("sl-rozaj-biske-1994", "sl-1994-biske-rozaj")]
    [InlineData("und-u-ca-islamicc-kn-yes-tz-eire", "und-u-ca-islamic-civil-kn-tz-iedub")]
    [InlineData("und-u-rg-cn71", "und-u-rg-twzzzz")]
    [InlineData("DE-T-M0-DIN-K0-QWERTZ", "de-t-k0-qwertz-m0-din")]
    [InlineData("en-t-iw", "en-t-he")]
    [InlineData("en-x-u-foo-a-bar", "en-x-u-foo-a-bar")]
    [InlineData("en-b-ccc-a-bbb", "en-a-bbb-b-ccc")]
    public void GetCanonicalLocales(string tag, string expected)
    {
        Assert.Equal(expected, Evaluate($"Intl.getCanonicalLocales('{tag}')[0]"));
    }

    [Theory]
    [InlineData("en-t")]
    [InlineData("en-u-c0")]
    [InlineData("i-default")]
    [InlineData("en-fonipa-fonipa")]
    [InlineData("en-a-x-a")]
    [InlineData("root")]
    [InlineData("en_US")]
    public void GetCanonicalLocales_Invalid(string tag)
    {
        Assert.Equal("RangeError", Evaluate($"try {{ Intl.getCanonicalLocales('{tag}'); 'none' }} catch (e) {{ e.name }}"));
    }

    [Fact]
    public void Locale()
    {
        Assert.Equal("ja-Jpan-JP-hepburn-u-ca-japanese-co-search-hc-h24-kf-false-kn-nu-jpanfin", Evaluate("""
            new Intl.Locale("de-latn-de-fonipa-1996-u-ca-gregory-co-phonebk-hc-h23-kf-true-kn-false-nu-latn", {
                language: "ja", script: "jpan", region: "jp", variants: "Hepburn", calendar: "japanese", collation: "search",
                hourCycle: "h24", caseFirst: "false", numeric: "true", numberingSystem: "jpanfin",
            }).toString()
            """));
        Assert.Equal("en-Latn-US|zh-TW|h12|rtl|7|6,7", Evaluate("""
            [
                new Intl.Locale("en").maximize().toString(),
                new Intl.Locale("zh-Hant-TW").minimize().toString(),
                new Intl.Locale("en-US").getHourCycles().join(),
                new Intl.Locale("ar").getTextInfo().direction,
                new Intl.Locale("en-US").getWeekInfo().firstDay,
                new Intl.Locale("en-US").getWeekInfo().weekend.join(),
            ].join("|")
            """));
    }

    [Theory]
    [InlineData(0.1, "0.1000000000000000055511151231257827021181583404541015625")]
    [InlineData(1234.5, "1234.5")]
    [InlineData(-0.0, "-0")]
    [InlineData(1e21, "1000000000000000000000")]
    public void IntlDecimal_FromDouble(double value, string exact)
    {
        var x = Meziantou.Framework.JavaScriptEngine.Intl.IntlDecimal.FromDouble(value);
        Assert.Equal(value, x.ToDouble());
        Assert.Equal(exact, (x.IsNegative ? "-" : "") + FormatExact(x));
    }

    private static string FormatExact(Meziantou.Framework.JavaScriptEngine.Intl.IntlDecimal x)
    {
        var digits = x.Coefficient.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (x.Exponent >= 0)
            return digits + new string('0', x.Exponent);

        digits = digits.PadLeft(-x.Exponent + 1, '0');
        return digits[..^(-x.Exponent)] + "." + digits[^(-x.Exponent)..];
    }

    [Theory]
    [InlineData("1.5", 0, 3, "halfExpand", "1.5")]
    [InlineData("1.0005", 0, 3, "halfExpand", "1.001")]
    [InlineData("1.0005", 0, 3, "halfEven", "1")]
    [InlineData("1.0015", 0, 3, "halfEven", "1.002")]
    [InlineData("-1.0005", 0, 3, "halfCeil", "1")]
    [InlineData("-1.0005", 0, 3, "floor", "1.001")]
    [InlineData("123456789012345678901234567890.5", 2, 2, "trunc", "123456789012345678901234567890.50")]
    public void FormatNumericToString(string value, int minimumFractionDigits, int maximumFractionDigits, string roundingMode, string expected)
    {
        var digits = new Meziantou.Framework.JavaScriptEngine.Intl.DigitOptions
        {
            MinimumFractionDigits = minimumFractionDigits,
            MaximumFractionDigits = maximumFractionDigits,
            RoundingMode = roundingMode,
        };

        var result = digits.FormatNumericToString(Meziantou.Framework.JavaScriptEngine.Intl.IntlDecimal.Parse(value));
        Assert.Equal(expected, result.FormattedString);
    }

    [Fact]
    public void PluralRules()
    {
        Assert.Equal("one|other|few|many|many|zero,one,two,few,many,other|other", Evaluate("""
            [
                new Intl.PluralRules("en").select(1),
                new Intl.PluralRules("en", { minimumFractionDigits: 1 }).select(1),
                new Intl.PluralRules("en", { type: "ordinal" }).select(23),
                new Intl.PluralRules("fr").select(1e6),
                new Intl.PluralRules("fr", { notation: "compact" }).select(1.5e6),
                new Intl.PluralRules("ar").resolvedOptions().pluralCategories.join(),
                new Intl.PluralRules("en").selectRange(1, 5),
            ].join("|")
            """));
    }

    [Theory]
    [InlineData("en-US", "{}", "1234567.891", "1,234,567.891")]
    [InlineData("de-DE", "{}", "1234567.891", "1.234.567,891")]
    [InlineData("fr-FR", "{}", "1234567.891", "1\u202F234\u202F567,891")]
    [InlineData("en-IN", "{}", "123456789", "12,34,56,789")]
    [InlineData("ar-EG", "{}", "1234.5", "\u0661\u066C\u0662\u0663\u0664\u066B\u0665")]
    [InlineData("en-US", "{ style: 'currency', currency: 'EUR' }", "-1234.5", "-\u20AC1,234.50")]
    [InlineData("en-US", "{ style: 'currency', currency: 'USD', currencySign: 'accounting' }", "-1234.5", "($1,234.50)")]
    [InlineData("en-US", "{ style: 'currency', currency: 'JPY', currencyDisplay: 'code' }", "1234.5", "JPY\u00A01,235")]
    [InlineData("en-US", "{ style: 'currency', currency: 'USD', currencyDisplay: 'name' }", "1", "1.00 US dollars")]
    [InlineData("de-DE", "{ style: 'currency', currency: 'EUR' }", "1234.5", "1.234,50\u00A0\u20AC")]
    [InlineData("en-US", "{ style: 'percent' }", "0.256", "26%")]
    [InlineData("en-US", "{ style: 'unit', unit: 'kilometer-per-hour', unitDisplay: 'long' }", "50", "50 kilometers per hour")]
    [InlineData("en-US", "{ notation: 'compact' }", "1234567", "1.2M")]
    [InlineData("en-US", "{ notation: 'compact', compactDisplay: 'long' }", "1234567", "1.2 million")]
    [InlineData("en-US", "{ notation: 'scientific' }", "123456", "1.235E5")]
    [InlineData("en-US", "{ notation: 'engineering' }", "0.000123", "123E-6")]
    [InlineData("en-US", "{ signDisplay: 'exceptZero' }", "-0", "0")]
    [InlineData("en-US", "{ maximumFractionDigits: 20 }", "0.1", "0.10000000000000000555")]
    [InlineData("en-US", "{ maximumSignificantDigits: 2, roundingMode: 'floor' }", "-123", "-130")]
    [InlineData("en-US", "{ minimumFractionDigits: 2, maximumFractionDigits: 2, roundingIncrement: 5 }", "1.234", "1.25")]
    [InlineData("en-US", "{}", "'1e1000'", "\u221E")]
    [InlineData("en-US", "{}", "'123456789012345678901234567890'", "123,456,789,012,345,678,901,234,567,890")]
    [InlineData("en-US", "{}", "12345678901234567890n", "12,345,678,901,234,567,890")]
    [InlineData("es-MX", "{ style: 'currency', currency: 'EUR', notation: 'compact' }", "1234.5", "EUR\u00A01.2\u00A0k")]
    [InlineData("en-US", "{ numberingSystem: 'arab' }", "-1234.5", "\u061C-\u0661\u066C\u0662\u0663\u0664\u066B\u0665")]
    [InlineData("de-DE", "{ style: 'unit', unit: 'percent' }", "5", "5\u00A0%")]
    [InlineData("fr-FR", "{ notation: 'compact', compactDisplay: 'long' }", "1000", "mille")]
    [InlineData("fr-FR", "{ notation: 'compact', compactDisplay: 'long' }", "1234567", "1,2 million")]
    [InlineData("ja-JP", "{ notation: 'compact' }", "1e21", "100,000\u4EAC")]
    [InlineData("en-IN", "{ style: 'currency', currency: 'USD', currencySign: 'accounting' }", "98765432", "$98,765,432.00")]
    [InlineData("en-US", "{ style: 'currency', currency: 'USD', currencyDisplay: 'code' }", "NaN", "USDNaN")]
    public void NumberFormat(string locale, string options, string value, string expected)
    {
        Assert.Equal(expected, Evaluate($"new Intl.NumberFormat('{locale}', {options}).format({value})"));
    }

    [Fact]
    public void NumberFormat_FormatToParts()
    {
        Assert.Equal("integer:\u0665\u0660|percentSign:\u066A|literal:\u061C", Evaluate("new Intl.NumberFormat('ar-EG', { style: 'percent' }).formatToParts(0.5).map(p => p.type + ':' + p.value).join('|')"));
        Assert.Equal("literal:\u200F|approximatelySign:~|literal:\u200E|plusSign:+|integer:1|decimal:.|fraction:00|literal:\u00A0\u200F|currency:CHF", Evaluate("new Intl.NumberFormat('he-IL', { style: 'currency', currency: 'CHF', currencySign: 'accounting', signDisplay: 'always' }).formatRangeToParts(1, 1.0001).map(p => p.type + ':' + p.value).join('|')"));
    }

    [Fact]
    public void NumberFormat_Range()
    {
        Assert.Equal("$3 \u2013 $5|~$3|+$2.90\u20133.10|3\u20135", Evaluate("""
            [
                new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 0 }).formatRange(3, 5),
                new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 0 }).formatRange(2.9, 3.1),
                new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", signDisplay: "always" }).formatRange(2.9, 3.1),
                new Intl.NumberFormat("en-US").formatRange(3, 5),
            ].join("|")
            """));
    }

    [Fact]
    public void ToLocaleString()
    {
        Assert.Equal("1,234.5|1.234,5|1,234|1,234.5,x", Evaluate("""
            [
                (1234.5).toLocaleString(),
                (1234.5).toLocaleString("de"),
                (1234n).toLocaleString(),
                [1234.5, "x", null].toLocaleString().slice(0, -1),
            ].join("|")
            """));
    }

    [Theory]
    [InlineData("en", "{}", -1, "day", "1 day ago")]
    [InlineData("en", "{ numeric: 'auto' }", -1, "day", "yesterday")]
    [InlineData("en", "{ numeric: 'auto' }", -0, "days", "today")]
    [InlineData("en", "{ numeric: 'auto', style: 'short' }", 1, "quarter", "next qtr.")]
    [InlineData("es", "{ numeric: 'auto' }", -2, "day", "anteayer")]
    [InlineData("en", "{}", 1234.5, "seconds", "in 1,234.5 seconds")]
    [InlineData("pl", "{}", 5, "year", "za 5 lat")]
    [InlineData("en-u-nu-arab", "{ style: 'narrow' }", 2, "hour", "in \u0662h")]
    public void RelativeTimeFormat(string locale, string options, double value, string unit, string expected)
    {
        Assert.Equal(expected, Evaluate($"new Intl.RelativeTimeFormat('{locale}', {options}).format({value.ToString(System.Globalization.CultureInfo.InvariantCulture)}, '{unit}')"));
    }

    [Fact]
    public void RelativeTimeFormat_FormatToParts()
    {
        Assert.Equal("integer:1:day|group:,:day|integer:234:day|literal: days ago:", Evaluate("new Intl.RelativeTimeFormat('en').formatToParts(-1234, 'day').map(p => p.type + ':' + p.value + ':' + (p.unit ?? '')).join('|')"));
    }

    [Theory]
    [InlineData("en", "{}", "['a', 'b', 'c']", "a, b, and c")]
    [InlineData("en", "{ type: 'disjunction' }", "['a', 'b']", "a or b")]
    [InlineData("en", "{ type: 'unit', style: 'narrow' }", "['1h', '2m', '3s']", "1h 2m 3s")]
    [InlineData("es", "{}", "['agua', 'hielo', 'hilo']", "agua, hielo e hilo")]
    [InlineData("es", "{ type: 'disjunction' }", "['siete', '8']", "siete u 8")]
    [InlineData("es", "{ type: 'disjunction' }", "['siete', '110']", "siete o 110")]
    [InlineData("he", "{}", "['\u05D0', 'b']", "\u05D0 \u05D5-b")]
    [InlineData("he", "{}", "['\u05D0', '\u05D1']", "\u05D0 \u05D5\u05D1")]
    [InlineData("en", "{}", "[]", "")]
    public void ListFormat(string locale, string options, string list, string expected)
    {
        Assert.Equal(expected, Evaluate($"new Intl.ListFormat('{locale}', {options}).format({list})"));
    }

    [Fact]
    public void ListFormat_FormatToParts()
    {
        Assert.Equal("element:a|literal:, |element:b|literal:, and |element:c|TypeError", Evaluate("""
            [
                ...new Intl.ListFormat('en').formatToParts(['a', 'b', 'c']).map(p => p.type + ':' + p.value),
                (() => { try { new Intl.ListFormat().format(['a', 1]); } catch (e) { return e.name; } })(),
            ].join('|')
            """));
    }

    [Theory]
    [InlineData("en", "{ type: 'language' }", "en-GB", "British English")]
    [InlineData("en", "{ type: 'language', style: 'short' }", "en-GB", "UK English")]
    [InlineData("en", "{ type: 'language', languageDisplay: 'standard' }", "en-GB", "English (United Kingdom)")]
    [InlineData("en", "{ type: 'language' }", "zh-Hant-TW", "Chinese (Traditional, Taiwan)")]
    [InlineData("en", "{ type: 'language' }", "en-MM", "English (Myanmar [Burma])")]
    [InlineData("en", "{ type: 'language' }", "sh", "Serbian (Latin)")]
    [InlineData("en", "{ type: 'language' }", "xyz-US", "xyz (United States)")]
    [InlineData("en", "{ type: 'language', fallback: 'none' }", "xyz-US", "undefined")]
    [InlineData("zh", "{ type: 'language' }", "en-Latn-US", "\u82F1\u8BED\uFF08\u62C9\u4E01\u6587\uFF0C\u7F8E\u56FD\uFF09")]
    [InlineData("en", "{ type: 'region' }", "419", "Latin America")]
    [InlineData("en", "{ type: 'region', style: 'short' }", "hk", "Hong Kong")]
    [InlineData("en", "{ type: 'region' }", "SU", "Russia")]
    [InlineData("en", "{ type: 'region' }", "XY", "XY")]
    [InlineData("en", "{ type: 'script' }", "latn", "Latin")]
    [InlineData("fr", "{ type: 'currency' }", "usd", "dollar des \u00C9tats-Unis")]
    [InlineData("en", "{ type: 'calendar' }", "gregory", "Gregorian Calendar")]
    [InlineData("en", "{ type: 'calendar' }", "islamicc", "Hijri Calendar (tabular, civil epoch)")]
    [InlineData("en", "{ type: 'calendar', fallback: 'none' }", "abcde", "undefined")]
    [InlineData("en", "{ type: 'dateTimeField', style: 'short' }", "weekOfYear", "wk.")]
    public void DisplayNames(string locale, string options, string code, string expected)
    {
        Assert.Equal(expected, Evaluate($"String(new Intl.DisplayNames('{locale}', {options}).of('{code}'))"));
    }

    [Theory]
    [InlineData("language", "en-u-ca-gregory")]
    [InlineData("language", "root")]
    [InlineData("language", "en_GB")]
    [InlineData("region", "ABC")]
    [InlineData("script", "Lat")]
    [InlineData("calendar", "abc_def")]
    [InlineData("currency", "US")]
    [InlineData("dateTimeField", "weekOfMonth")]
    public void DisplayNames_InvalidCode(string type, string code)
    {
        Assert.Equal("RangeError", Evaluate($"try {{ new Intl.DisplayNames('en', {{ type: '{type}' }}).of('{code}'); 'none' }} catch (e) {{ e.name }}"));
    }

    [Fact]
    public void DefaultLocale()
    {
        Assert.Equal("fr|1\u202F234,5", Evaluate("[new Intl.NumberFormat().resolvedOptions().locale, (1234.5).toLocaleString()].join('|')", new JsEngineOptions { DefaultLocale = "fr" }));
        Assert.Equal("en-US", Evaluate("new Intl.NumberFormat().resolvedOptions().locale", new JsEngineOptions { DefaultLocale = "zz-invalid-" }));
    }

    [Fact]
    public void SupportedValuesOf()
    {
        Assert.Equal("true|true|true", Evaluate("""
            [
                Intl.supportedValuesOf("calendar").includes("gregory"),
                Intl.supportedValuesOf("numberingSystem").includes("arab"),
                Intl.supportedValuesOf("timeZone").includes("Europe/Paris"),
            ].join("|")
            """));
    }
}
