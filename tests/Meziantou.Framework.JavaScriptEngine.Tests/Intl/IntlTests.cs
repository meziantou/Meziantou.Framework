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

    [Fact]
    public void DefaultLocale()
    {
        Assert.Equal("fr|1\u202F234,5", Evaluate("[new Intl.NumberFormat().resolvedOptions().locale, (1234.5).toLocaleString()].join('|')", new JsEngineOptions { DefaultLocale = "fr" }));
        Assert.Equal("en-US", Evaluate("new Intl.NumberFormat().resolvedOptions().locale", new JsEngineOptions { DefaultLocale = "zz-invalid-" }));
    }

    // The expected orders are the ones of ICU (Node.js)
    [Theory]
    [InlineData("de", "{collation: 'phonebk'}", "\u00E4b,af,ae,\u00C4,ad", "ad,ae,\u00C4,\u00E4b,af")]
    [InlineData("de", "{}", "\u00E4b,af,ae,\u00C4,ad", "\u00C4,\u00E4b,ad,ae,af")]
    [InlineData("sv", "{}", "\u00E4,z,\u00E5,\u00F6,a,o", "a,o,z,\u00E5,\u00E4,\u00F6")]
    [InlineData("es", "{collation: 'trad'}", "n,\u00F1,o,ch,cz,d", "cz,ch,d,n,\u00F1,o")]
    [InlineData("zh", "{}", "\u4E2D,\u554A,\u963F,\u4E00,\u5927", "\u963F,\u554A,\u5927,\u4E00,\u4E2D")]
    [InlineData("zh-TW", "{}", "\u4E2D,\u554A,\u963F,\u4E00,\u5927", "\u4E00,\u5927,\u4E2D,\u963F,\u554A")]
    [InlineData("ja", "{}", "\u30A2,\u3042,\u30A1,b,A,\u4E00", "A,b,\u30A1,\u30A2,\u3042,\u4E00")]
    [InlineData("ko", "{}", "\uB098,\uAC00,\u4F3D,\uB2E4", "\uAC00,\u4F3D,\uB098,\uB2E4")]
    [InlineData("ru", "{}", "\u0431,\u0430,b,a", "\u0430,\u0431,a,b")]
    [InlineData("en", "{}", "\u0431,\u0430,b,a", "a,b,\u0430,\u0431")]
    [InlineData("en", "{numeric: true}", "a10,a9,a1,a01b,a100", "a1,a01b,a9,a10,a100")]
    [InlineData("en", "{caseFirst: 'upper'}", "a,A,b,B", "A,a,B,b")]
    [InlineData("en", "{ignorePunctuation: true}", "a-c,ab,a c,ac", "ab,a-c,a c,ac")]
    [InlineData("fr-CA", "{}", "cote,cot\u00E9,c\u00F4te,c\u00F4t\u00E9", "cote,c\u00F4te,cot\u00E9,c\u00F4t\u00E9")]
    [InlineData("fr", "{}", "cote,cot\u00E9,c\u00F4te,c\u00F4t\u00E9", "cote,cot\u00E9,c\u00F4te,c\u00F4t\u00E9")]
    [InlineData("da", "{}", "aa,\u00E5,z,\u00E6,\u00F8,A,a", "A,a,z,\u00E6,\u00F8,\u00E5,aa")]
    [InlineData("tr", "{}", "\u0131,i,I,\u0130,h,j", "h,\u0131,I,i,\u0130,j")]
    public void Collator_Sort(string locale, string options, string values, string expected)
    {
        Assert.Equal(expected, Evaluate($"'{values}'.split(',').sort(new Intl.Collator('{locale}', {options}).compare).join()"));
    }

    [Fact]
    public void Collator()
    {
        Assert.Equal("de-u-co-phonebk|search|variant|false|default|true|upper|da|upper|true|-1,0,1|0|0", Evaluate("""
            [
                new Intl.Collator("de-u-co-phonebk").resolvedOptions().locale,
                new Intl.Collator("de", { usage: "search", numeric: true, caseFirst: "upper" }).resolvedOptions().usage,
                new Intl.Collator("de", { usage: "search" }).resolvedOptions().sensitivity,
                new Intl.Collator("de", { usage: "search" }).resolvedOptions().ignorePunctuation,
                new Intl.Collator("de", { usage: "search", collation: "phonebk" }).resolvedOptions().collation,
                new Intl.Collator("en-u-kn").resolvedOptions().numeric,
                new Intl.Collator("en-u-kf-upper").resolvedOptions().caseFirst,
                new Intl.Collator("da").resolvedOptions().locale,
                new Intl.Collator("da").resolvedOptions().caseFirst,
                new Intl.Collator("th").resolvedOptions().ignorePunctuation,
                ["a", "b", "c"].map(x => new Intl.Collator("en").compare(x, "b")).join(),
                new Intl.Collator("en", { sensitivity: "base" }).compare("a", "\u00C1"),
                new Intl.Collator("en").compare("\u00C5", "A\u030A"),
            ].join("|")
            """));
        Assert.Equal("1|0|-1|1|1|pinyin,stroke|true|false|-1", Evaluate("""
            [
                new Intl.Collator("en", { sensitivity: "case" }).compare("A", "a"),
                new Intl.Collator("en", { sensitivity: "case" }).compare("a", "\u00E1"),
                new Intl.Collator("en", { sensitivity: "accent" }).compare("a", "\u00E1"),
                new Intl.Collator("de", { usage: "search" }).compare("\u00C4", "AE"),
                new Intl.Collator("de", { collation: "phonebk" }).compare("\u00C4", "AE"),
                new Intl.Locale("zh-Hant").getCollations().filter(c => c === "pinyin" || c === "stroke").join(),
                Intl.Collator.supportedLocalesOf(["bs-Cyrl"]).length === 1,
                new Intl.Collator("da", { usage: "search" }).resolvedOptions().caseFirst,
                new Intl.Collator("ar", { caseFirst: "upper" }).compare("8", "\u064E8"),
            ].join("|")
            """));
    }

    [Fact]
    public void LocaleCompare()
    {
        Assert.Equal("-1|1|0|-1|0", Evaluate("""
            [
                "a".localeCompare("b"),
                "\u00E4".localeCompare("z", "sv"),
                "a".localeCompare("A", "en", { sensitivity: "base" }),
                "\u00E4".localeCompare("z", "de"),
                "\u0041\u030A".localeCompare("\u00C5"),
            ].join("|")
            """));
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
