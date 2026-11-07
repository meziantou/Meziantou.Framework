using System.Text;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Tests.Unicode;

/// <summary>The automatic hyphenation (https://drafts.csswg.org/css-text-4/#hyphenation): Liang's algorithm with the patterns of hyph-utf8, as Chrome.</summary>
public sealed class HyphenatorTests
{
    // Formats the word with '-' at its automatic hyphenation opportunities
    private static string Hyphenate(string language, string word, int minWord = 0, int minBefore = 0, int minAfter = 0)
    {
        var hyphenator = HyphenationDictionary.Get(language) ?? throw new InvalidOperationException($"No dictionary for {language}");
        var opportunities = new bool[word.Length];
        var found = hyphenator.FindOpportunities(word, minWord, minBefore, minAfter, opportunities);
        Assert.Equal(found, opportunities.Contains(true));
        var builder = new StringBuilder();
        for (var i = 0; i < word.Length; i++)
        {
            if (opportunities[i])
            {
                builder.Append('-');
            }

            builder.Append(word[i]);
        }

        return builder.ToString();
    }

    [Theory]
    [InlineData("en-us", "implementation", "im-ple-men-ta-tion")]
    [InlineData("en-us", "hyphenation", "hy-phen-ation")]
    [InlineData("en-us", "regulation", "reg-u-la-tion")]
    [InlineData("en-us", "example", "ex-am-ple")]
    [InlineData("en-us", "engines", "en-gines")]
    [InlineData("en-us", "highway", "high-way")]
    [InlineData("en-gb", "implementation", "im-ple-ment-a-tion")]
    [InlineData("de-1996", "Donaudampfschifffahrt", "Do-nau-dampf-schiff-fahrt")]
    [InlineData("de-1996", "Wiedervereinigung", "Wie-der-ver-ei-ni-gung")]
    [InlineData("de-1996", "Rechtsschutzversicherungsgesellschaften", "Rechts-schutz-ver-si-che-rungs-ge-sell-schaf-ten")]
    [InlineData("fr", "anticonstitutionnellement", "an-ti-cons-ti-tu-tion-nel-le-ment")]
    [InlineData("nl", "ziekenhuis", "zie-ken-huis")]
    [InlineData("pt", "hifenização", "hi-fe-ni-za-ção")]
    [InlineData("ru", "автоматический", "ав-то-ма-ти-че-ский")]
    public void Words(string language, string word, string expected) => Assert.Equal(expected, Hyphenate(language, word));

    [Fact]
    public void Exceptions()
    {
        // hyph-en-us.hyp.txt overrides the patterns ("as-so-ciate", "project", "pres-ent")
        Assert.Equal("as-so-ciate", Hyphenate("en-us", "associate"));
        Assert.Equal("as-so-ciates", Hyphenate("en-us", "associates"));
        Assert.Equal("ta-ble", Hyphenate("en-us", "table", minAfter: 0));
    }

    [Theory]
    [InlineData("en", "en-us")]
    [InlineData("EN", "en-us")]
    [InlineData("en-US", "en-us")]
    [InlineData("en-us-x-private", "en-us")]
    [InlineData("en-PR", "en-us")]
    [InlineData("en-GB", "en-gb")]
    [InlineData("en-AU", "en-gb")]
    [InlineData("de", "de-1996")]
    [InlineData("De-dE", "de-1996")]
    [InlineData("de-CH", "de-1996")]
    [InlineData("de-CH-1901", "de-ch-1901")]
    [InlineData("de-LI-1901", "de-ch-1901")]
    [InlineData("pt-BR", "pt")]
    [InlineData("fr-CA", "fr")]
    [InlineData("no", "nb")]
    [InlineData("nb-NO", "nb")]
    [InlineData("nn", "nn")]
    [InlineData("mn", "mn-cyrl")]
    [InlineData("sr", "sr-cyrl")]
    [InlineData("sr-Latn-RS", "sr-latn")]
    [InlineData("el-GR", "el")]
    [InlineData("und-Deva", "hi")]
    [InlineData("mr", "mr")]
    public void Languages(string language, string locale) => Assert.Equal(locale, HyphenationDictionary.Get(language)?.Locale);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("zh")]
    [InlineData("zh-Latn-pinyin")]
    [InlineData("ja")]
    [InlineData("cs")] // GPL patterns, not bundled
    [InlineData("hu")] // MPL/GPL/LGPL patterns, not bundled
    [InlineData("x-unknown")]
    public void Languages_NoDictionary(string? language) => Assert.Null(HyphenationDictionary.Get(language));

    [Fact]
    public void Dictionaries_AreShared()
    {
        Assert.Same(HyphenationDictionary.Get("en"), HyphenationDictionary.Get("en-US"));
        Assert.True(HyphenationDictionary.Get("de")!.HyphenatesCapitalizedWords);
        Assert.False(HyphenationDictionary.Get("en")!.HyphenatesCapitalizedWords);
        Assert.False(HyphenationDictionary.Get("en-gb")!.HyphenatesCapitalizedWords);
    }

    [Fact]
    public void CapitalizedWords()
    {
        // Chrome does not hyphenate the capitalized words in English (https://github.com/w3c/csswg-drafts/issues/5157)
        Assert.Equal("Implementation", Hyphenate("en", "Implementation"));
        Assert.Equal("im-ple-men-ta-tion", Hyphenate("en", "implementation"));
        Assert.Equal("Do-nau-dampf-schiff-fahrt", Hyphenate("de", "Donaudampfschifffahrt"));

        // The other uppercase letters are matched with their lowercase letters
        Assert.Equal("iM-PLE-MEN-TA-TION", Hyphenate("en", "iMPLEMENTATION"));
    }

    [Fact]
    public void Punctuation()
    {
        // The leading opening punctuation and quotes, and the trailing punctuation, are not part of the word
        Assert.Equal("(ex-am-ple", Hyphenate("en", "(example"));
        Assert.Equal("ex-am-ple.", Hyphenate("en", "example."));
        Assert.Equal("\"ex-am-ple\",", Hyphenate("en", "\"example\","));
        Assert.Equal("ex-am-ple ", Hyphenate("en", "example "));
    }

    [Fact]
    public void NotHyphenated()
    {
        // A character that is not a letter of the patterns
        Assert.Equal("imple2mentation", Hyphenate("en", "imple2mentation"));
        Assert.Equal("2020-12-12", Hyphenate("en", "2020-12-12"));
        Assert.Equal("imple\u00ADmentation", Hyphenate("en", "imple\u00ADmentation"));

        // Too short (5 characters at least) or too long (62 at most)
        Assert.Equal("tab", Hyphenate("en", "tab"));
        var longWord = string.Concat(Enumerable.Repeat("implementation", 5));
        Assert.Equal(longWord, Hyphenate("en", longWord));
    }

    [Theory]
    // https://drafts.csswg.org/css-text-4/#hyphenate-limit-chars-property (WPT css/css-text/hyphens/hyphenate-limit-chars-001.html)
    [InlineData(0, 0, 0, "ex-am-ple")]
    [InlineData(8, 0, 0, "example")]
    [InlineData(0, 2, 2, "ex-am-ple")]
    [InlineData(0, 3, 2, "exam-ple")]
    [InlineData(0, 4, 2, "exam-ple")]
    [InlineData(0, 5, 2, "example")]
    [InlineData(0, 2, 3, "ex-am-ple")]
    [InlineData(0, 2, 4, "ex-ample")]
    [InlineData(0, 3, 4, "example")]
    [InlineData(0, 3, 0, "exam-ple")] // the minimum after the hyphen is the one before the hyphen
    [InlineData(0, 1, 1, "ex-am-ple")] // the patterns need 2 characters before and 3 after the hyphen
    public void Limits(int word, int before, int after, string expected) => Assert.Equal(expected, Hyphenate("en-us", "example", word, before, after));

    [Fact]
    public void AllDictionaries_Load()
    {
        foreach (var language in (string[])["af", "as", "be", "bg", "bn", "cy", "da", "de-1996", "de-ch-1901", "el", "en-gb", "en-us", "es", "et", "eu", "fr", "ga", "gl", "gu", "hi", "hr", "it", "ka", "kn", "la", "lt", "ml", "mn-cyrl", "mr", "nb", "nl", "nn", "or", "pa", "pt", "ru", "sk", "sl", "sq", "sr-cyrl", "sr-latn", "sv", "ta", "te", "tk", "uk"])
        {
            var hyphenator = HyphenationDictionary.Get(language);
            Assert.NotNull(hyphenator);
            Assert.Equal(language, hyphenator.Locale);
        }
    }
}
