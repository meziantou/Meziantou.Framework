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
        Assert.Equal("en-Latn-US|zh-TW|h12,h23|rtl|7|6,7", Evaluate("""
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
