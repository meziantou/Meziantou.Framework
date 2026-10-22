using System.Globalization;
using System.IO.Compression;
using Meziantou.Framework.JavaScriptEngine.Intl;
using Meziantou.Framework.JavaScriptEngine.Intl.Data;
using Meziantou.Framework.JavaScriptEngine.Intl.Data.Collation;
using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine.Tests.Intl;

public sealed class CldrDataTests
{
    private static CldrData Data => CldrData.Default;

    private static CldrLocale GetLocale(string name) => Data.GetLocale(name) ?? throw new InvalidOperationException("Unknown locale " + name);

    private static List<string> ReadTestData(string name)
    {
        using var stream = typeof(CldrDataTests).Assembly.GetManifestResourceStream("Intl.TestData." + name + ".gz") ?? throw new InvalidOperationException("Missing test data " + name);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    [Fact]
    public void SizeBudget()
    {
        long total = 0;
        foreach (var name in (string[])[CldrData.LocalesResourceName, CldrData.SupplementalResourceName, CldrData.CollationResourceName])
        {
            using var stream = typeof(CldrData).Assembly.GetManifestResourceStream(name)!;
            TestContext.Current.TestOutputHelper?.WriteLine($"{name}: {stream.Length} bytes");
            total += stream.Length;
        }

        var locales = Data.LocalesFile;
        foreach (var category in Enum.GetValues<CldrCategory>())
        {
            var size = Data.Locales.GetChunkIndexes(category).Sum(locales.GetCompressedChunkLength);
            TestContext.Current.TestOutputHelper?.WriteLine($"  {category}: {size} bytes");
            Assert.True(size > 0, category.ToString());
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"Total: {total} bytes, {locales.ChunkCount} chunks in locales.bin");
        Assert.True(total <= 8 * 1024 * 1024, $"The CLDR data is {total} bytes");
    }

    [Fact]
    public void Versions()
    {
        // The data of Intl and the Windows time zones of the time zone database come from the same CLDR release
        Assert.Equal(TimeZoneDatabase.EmbeddedCldrVersion, "release-" + CldrData.EmbeddedCldrVersion.Replace('.', '-'));
        Assert.True(CldrData.EmbeddedCldrJsonVersion == CldrData.EmbeddedCldrVersion || CldrData.EmbeddedCldrJsonVersion.StartsWith(CldrData.EmbeddedCldrVersion + ".", StringComparison.Ordinal), $"cldr-json {CldrData.EmbeddedCldrJsonVersion} is not CLDR {CldrData.EmbeddedCldrVersion}");
        Assert.Equal(CldrData.EmbeddedUcaVersion, Data.Collation.UcaVersion);
        Assert.Equal(CldrData.EmbeddedUcaVersion, Data.Collation.Root.UcaVersion);
        Assert.Same(CldrData.Default, CldrData.Default);
    }

    [Fact]
    public void ChunksAreLoadedOnDemand()
    {
        var data = new CldrData(
            CldrChunkedFile.FromResource(CldrData.LocalesResourceName),
            CldrChunkedFile.FromResource(CldrData.SupplementalResourceName),
            CldrChunkedFile.FromResource(CldrData.CollationResourceName));
        Assert.Equal(0, CountLoadedChunks(data.LocalesFile));
        Assert.Equal(0, CountLoadedChunks(data.SupplementalFile));
        Assert.Equal(0, CountLoadedChunks(data.CollationFile));

        var locale = data.GetLocale("fr-CA")!;
        Assert.Equal(",", locale.GetNumberSymbols("latn").Decimal);

        // The index, and the Numbers chunks of "fr" and of the root
        Assert.True(CountLoadedChunks(data.LocalesFile) <= 3, $"{CountLoadedChunks(data.LocalesFile)} chunks are loaded");
        Assert.Equal(0, CountLoadedChunks(data.SupplementalFile));
        Assert.Equal(0, CountLoadedChunks(data.CollationFile));

        static int CountLoadedChunks(CldrChunkedFile file)
        {
            var count = 0;
            for (var i = 0; i < file.ChunkCount; i++)
            {
                if (file.IsChunkLoaded(i))
                {
                    count++;
                }
            }

            return count;
        }
    }

    [Fact]
    public void AvailableLocales()
    {
        var locales = Data.Locales.AvailableLocales;
        Assert.True(locales.Count > 500, $"{locales.Count} locales");
        Assert.Contains("en", locales);
        Assert.Contains("en-US", locales);
        Assert.Contains("fr-CA", locales);
        Assert.Contains("zh-Hant", locales);
        Assert.Contains("zh-TW", locales);
        Assert.DoesNotContain("und", locales);
        Assert.Equal(locales.Order(StringComparer.Ordinal), locales);

        Assert.True(Data.IsAvailableLocale("fr-FR"));
        Assert.True(Data.IsAvailableLocale("FR-ca"));
        Assert.False(Data.IsAvailableLocale("und"));
        Assert.False(Data.IsAvailableLocale("xx"));
    }

    [Theory]
    [InlineData("fr-FR", "fr")]
    [InlineData("en-US", "en")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-Hant-TW", "zh-Hant")]
    [InlineData("zh-HK", "zh-Hant-HK")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("sr-ME", "sr-Latn-ME")]
    [InlineData("en-AU", "en-AU")]
    public void GetLocale_Alias(string name, string expected)
    {
        Assert.Equal(expected, GetLocale(name).Name);
    }

    [Theory]
    [InlineData("fr-CA", "fr-CA")]
    [InlineData("fr-XX", "fr")]
    [InlineData("en-XX-u-ca-buddhist", "en")]
    [InlineData("de-CH-1996", "de-CH")]
    [InlineData("zh-MO", "zh-Hant-MO")]
    [InlineData("zh-Hant-XX", "zh-Hant")]
    [InlineData("sr-Latn-XX", "sr-Latn")]
    [InlineData("az-Cyrl-XX", "und")]
    [InlineData("en-Shaw", "und")]
    [InlineData("xx", "und")]
    [InlineData("und", "und")]
    public void FindLocale(string tag, string expected)
    {
        Assert.Equal(expected, Data.FindLocale(tag).Name);
    }

    [Fact]
    public void Inheritance()
    {
        Assert.Equal("en-001", GetLocale("en-AU").Parent!.Name);
        Assert.Equal("en", GetLocale("en-001").Parent!.Name);
        Assert.Equal("und", GetLocale("zh-Hant").Parent!.Name);
        Assert.True(GetLocale("und").IsRoot);

        // en-AU has nothing of its own for the decimal pattern: it inherits it
        Assert.Equal("#,##0.###", GetLocale("en-AU").GetNumberPattern("latn", "decimalFormats", "standard"));

        // en-001 does not inherit the short names of the American metazones of en ("no value" in CLDR)
        Assert.Equal("EST", GetLocale("en").GetString(CldrCategory.TimeZoneNames, "metazone/America_Eastern/short/standard"));
        Assert.Null(GetLocale("en-GB").GetString(CldrCategory.TimeZoneNames, "metazone/America_Eastern/short/standard"));
        Assert.False(GetLocale("en-GB").Contains(CldrCategory.TimeZoneNames, "metazone/America_Eastern/short/standard"));
        Assert.Equal("Eastern Standard Time", GetLocale("en-GB").GetString(CldrCategory.TimeZoneNames, "metazone/America_Eastern/long/standard"));
        Assert.DoesNotContain("short", GetLocale("en-GB").GetKeys(CldrCategory.TimeZoneNames, "metazone/America_Eastern"));
        Assert.Contains("short", GetLocale("en").GetKeys(CldrCategory.TimeZoneNames, "metazone/America_Eastern"));
    }

    [Theory]
    [InlineData("en", "latn", ".", ",", "-", "%")]
    [InlineData("fr", "latn", ",", "\u202F", "-", "%")]
    [InlineData("de-CH", "latn", ".", "'", "-", "%")]
    [InlineData("ja", "latn", ".", ",", "-", "%")]
    [InlineData("ar-EG", "arab", "\u066B", "\u066C", "\u061C-", "\u066A\u061C")]
    [InlineData("ar", "arab", "\u066B", "\u066C", "\u061C-", "\u066A\u061C")]
    public void NumberSymbols(string locale, string numberingSystem, string decimalSeparator, string group, string minus, string percent)
    {
        var symbols = GetLocale(locale).GetNumberSymbols(numberingSystem);
        Assert.Equal(decimalSeparator, symbols.Decimal);
        Assert.Equal(group, symbols.Group);
        Assert.Equal(minus, symbols.MinusSign);
        Assert.Equal(percent, symbols.PercentSign);
    }

    [Fact]
    public void NumberSymbols_FallBackToLatn()
    {
        var symbols = GetLocale("en").GetNumberSymbols("thai");
        Assert.Equal(".", symbols.Decimal);
        Assert.Equal("NaN", symbols.NaN);
        Assert.Equal("\u221E", symbols.Infinity);
    }

    [Fact]
    public void DefaultNumberingSystem()
    {
        Assert.Equal("latn", GetLocale("en").GetDefaultNumberingSystem());
        Assert.Equal("arab", GetLocale("ar-EG").GetDefaultNumberingSystem());
        Assert.Equal("latn", GetLocale("ar-MA").GetDefaultNumberingSystem());
    }

    [Fact]
    public void NumberPatterns()
    {
        Assert.Equal("#,##0.###", GetLocale("en").GetNumberPattern("latn", "decimalFormats", "standard"));
        Assert.Equal("#,##0\u00A0%", GetLocale("fr").GetNumberPattern("latn", "percentFormats", "standard"));
        Assert.Equal("#,##0%", GetLocale("de-CH").GetNumberPattern("latn", "percentFormats", "standard"));
        Assert.Equal("\u00A4\u00A0#,##0.00;\u00A4-#,##0.00", GetLocale("de-CH").GetNumberPattern("latn", "currencyFormats", "standard"));
        Assert.Equal("\u00A4#,##0.00;(\u00A4#,##0.00)", GetLocale("en").GetNumberPattern("latn", "currencyFormats", "accounting"));
        Assert.Equal("0\u4E07", GetLocale("ja").GetNumberPattern("latn", "decimalFormats", "short/decimalFormat/10000-count-other"));
        Assert.Equal("0K", GetLocale("en").GetNumberPattern("latn", "decimalFormats", "short/decimalFormat/1000-count-one"));
        Assert.Equal("0 thousand", GetLocale("en").GetNumberPattern("latn", "decimalFormats", "long/decimalFormat/1000-count-one"));
        Assert.Equal("#E0", GetLocale("en").GetNumberPattern("latn", "scientificFormats", "standard"));
        Assert.Equal("1", GetLocale("en").GetString(CldrCategory.Numbers, "minimumGroupingDigits"));
        Assert.Equal("2", GetLocale("es").GetString(CldrCategory.Numbers, "minimumGroupingDigits"));
    }

    [Fact]
    public void Currencies()
    {
        Assert.Equal("$", GetLocale("en").GetString(CldrCategory.Currencies, "USD/symbol"));
        Assert.Equal("USD", GetLocale("en-AU").GetString(CldrCategory.Currencies, "USD/symbol"));
        Assert.Equal("$US", GetLocale("fr").GetString(CldrCategory.Currencies, "USD/symbol"));
        Assert.Equal("$", GetLocale("fr").GetString(CldrCategory.Currencies, "USD/symbol-alt-narrow"));
        Assert.Equal("euros", GetLocale("fr").GetString(CldrCategory.Currencies, "EUR/displayName-count-other"));
        Assert.Equal("US dollars", GetLocale("en").GetString(CldrCategory.Currencies, "USD/displayName-count-other"));
    }

    [Fact]
    public void Units()
    {
        var en = GetLocale("en");
        Assert.Equal("{0} kilometer", en.GetString(CldrCategory.Units, "long/kilometer/unitPattern-count-one"));
        Assert.Equal("{0} km", en.GetString(CldrCategory.Units, "short/kilometer/unitPattern-count-one"));
        Assert.Equal("{0}/{1}", en.GetString(CldrCategory.Units, "short/per/compoundUnitPattern"));
        Assert.Equal("h:mm:ss", en.GetString(CldrCategory.Units, "durationUnit-type-hms/durationUnitPattern"));

        // The 45 units of ECMA-402, and the 5 compound units of two of them that CLDR has
        var units = en.GetKeys(CldrCategory.Units, "long");
        Assert.Equal(51, units.Count);
        Assert.Contains("kilometer-per-hour", units);
        Assert.Equal("{0} km/h", en.GetString(CldrCategory.Units, "short/kilometer-per-hour/unitPattern-count-one"));
        Assert.Contains("per", units);
        Assert.Contains("fluid-ounce", units);
        Assert.Contains("mile-scandinavian", units);
        Assert.Contains("percent", units);
        Assert.DoesNotContain("length-meter", units);
    }

    [Fact]
    public void Gregorian()
    {
        Assert.Equal("January", GetLocale("en").GetString(CldrCategory.Gregorian, "months/format/wide/1"));
        Assert.Equal("dimanche", GetLocale("fr").GetString(CldrCategory.Gregorian, "days/format/wide/sun"));
        Assert.Equal("y\u5E74M\u6708d\u65E5EEEE", GetLocale("ja").GetString(CldrCategory.Gregorian, "dateFormats/full"));
        Assert.Equal("h:mm\u202Fa", GetLocale("en").GetString(CldrCategory.Gregorian, "timeFormats/short"));
        Assert.Equal("HH:mm", GetLocale("fr").GetString(CldrCategory.Gregorian, "timeFormats/short"));
        Assert.Equal("AM", GetLocale("en").GetString(CldrCategory.Gregorian, "dayPeriods/format/abbreviated/am"));

        var skeletons = GetLocale("en-AU").GetKeys(CldrCategory.Gregorian, "dateTimeFormats/availableFormats");
        Assert.Contains("yMMMd", skeletons);
        Assert.Contains("Hms", skeletons);
        Assert.Contains("d", GetLocale("en").GetKeys(CldrCategory.Gregorian, "dateTimeFormats/intervalFormats/yMMMd"));
    }

    [Fact]
    public void Calendars()
    {
        Assert.Equal("\u4EE4\u548C", GetLocale("ja").GetString(CldrCategory.Calendars, "japanese/eras/eraAbbr/236"));
        Assert.Equal("Gy\u5E74M\u6708d\u65E5EEEE", GetLocale("ja").GetString(CldrCategory.Calendars, "japanese/dateFormats/full"));
        Assert.Equal("y=jpanyear", GetLocale("ja").GetString(CldrCategory.Calendars, "japanese/dateFormats/full-numbers"));
        Assert.Equal("Tishri", GetLocale("en").GetString(CldrCategory.Calendars, "hebrew/months/format/wide/1"));
        Assert.Equal("BE", GetLocale("en").GetString(CldrCategory.Calendars, "buddhist/eras/eraAbbr/0"));
        Assert.NotNull(GetLocale("fr").GetString(CldrCategory.Calendars, "generic/dateFormats/short"));
        foreach (var calendar in (string[])["buddhist", "chinese", "coptic", "dangi", "ethiopic", "ethiopic-amete-alem", "generic", "hebrew", "indian", "islamic", "islamic-civil", "islamic-rgsa", "islamic-tbla", "islamic-umalqura", "iso8601", "japanese", "persian", "roc"])
        {
            Assert.True(GetLocale("en").Contains(CldrCategory.Calendars, calendar), calendar);
        }
    }

    [Fact]
    public void TimeZoneNames()
    {
        var fr = GetLocale("fr");
        Assert.Equal("UTC{0}", fr.GetString(CldrCategory.TimeZoneNames, "gmtFormat"));
        Assert.Equal("heure normale d\u2019Europe centrale", fr.GetString(CldrCategory.TimeZoneNames, "metazone/Europe_Central/long/standard"));
        Assert.Equal("Londres", fr.GetString(CldrCategory.TimeZoneNames, "zone/Europe/London/exemplarCity"));
        Assert.Equal("zone", fr.GetString(CldrCategory.TimeZoneNames, "zone/Europe/London/_type"));
        Assert.Equal("GMT{0}", GetLocale("en").GetString(CldrCategory.TimeZoneNames, "gmtFormat"));
        Assert.Equal("+HH:mm;-HH:mm", GetLocale("en").GetString(CldrCategory.TimeZoneNames, "hourFormat"));
    }

    [Fact]
    public void Fields()
    {
        var en = GetLocale("en");
        Assert.Equal("in {0} year", en.GetString(CldrCategory.Fields, "year/relativeTime-type-future/relativeTimePattern-count-one"));
        Assert.Equal("{0} years ago", en.GetString(CldrCategory.Fields, "year/relativeTime-type-past/relativeTimePattern-count-other"));
        Assert.Equal("yesterday", en.GetString(CldrCategory.Fields, "day/relative-type--1"));
        Assert.Equal("hier", GetLocale("fr").GetString(CldrCategory.Fields, "day/relative-type--1"));
    }

    [Fact]
    public void DisplayNames()
    {
        Assert.Equal("French", GetLocale("en").GetString(CldrCategory.DisplayNames, "languages/fr"));
        Assert.Equal("British English", GetLocale("en").GetString(CldrCategory.DisplayNames, "languages/en-GB"));
        Assert.Equal("\u00C9tats-Unis", GetLocale("fr").GetString(CldrCategory.DisplayNames, "territories/US"));
        Assert.Equal("Latin", GetLocale("en").GetString(CldrCategory.DisplayNames, "scripts/Latn"));
        Assert.Equal("Gregorian Calendar", GetLocale("en").GetString(CldrCategory.DisplayNames, "types/calendar/gregorian"));
        Assert.Equal("{0} ({1})", GetLocale("en").GetString(CldrCategory.DisplayNames, "localeDisplayPattern/localePattern"));
        Assert.Equal("Fran\u00E7ais", GetLocale("fr").GetString(CldrCategory.DisplayNames, "languages/fr")?.Replace('f', 'F'));
    }

    [Fact]
    public void Misc()
    {
        Assert.Equal("{0}, and {1}", GetLocale("en").GetString(CldrCategory.Misc, "listPatterns/listPattern-type-standard/end"));
        Assert.Equal("{0} et {1}", GetLocale("fr").GetString(CldrCategory.Misc, "listPatterns/listPattern-type-standard/2"));
        Assert.Equal("right-to-left", GetLocale("ar").GetString(CldrCategory.Misc, "layout/orientation/characterOrder"));
        Assert.Equal("left-to-right", GetLocale("en").GetString(CldrCategory.Misc, "layout/orientation/characterOrder"));
    }

    [Theory]
    [InlineData("en", "1", "one")]
    [InlineData("en", "2", "other")]
    [InlineData("en", "0", "other")]
    [InlineData("en", "1.0", "other")]
    [InlineData("fr", "0", "one")]
    [InlineData("fr", "1", "one")]
    [InlineData("fr", "1.5", "one")]
    [InlineData("fr", "2", "other")]
    [InlineData("fr", "1000000", "many")]
    [InlineData("fr", "1c6", "many")]
    [InlineData("fr", "1.1c6", "many")]
    [InlineData("fr", "1000001", "other")]
    [InlineData("ar", "0", "zero")]
    [InlineData("ar", "1", "one")]
    [InlineData("ar", "2", "two")]
    [InlineData("ar", "3", "few")]
    [InlineData("ar", "110", "few")]
    [InlineData("ar", "11", "many")]
    [InlineData("ar", "100", "other")]
    [InlineData("ar", "0.5", "other")]
    [InlineData("ru", "1", "one")]
    [InlineData("ru", "21", "one")]
    [InlineData("ru", "11", "many")]
    [InlineData("ru", "2", "few")]
    [InlineData("ru", "5", "many")]
    [InlineData("ru", "1.5", "other")]
    [InlineData("pl", "1", "one")]
    [InlineData("pl", "2", "few")]
    [InlineData("pl", "22", "few")]
    [InlineData("pl", "12", "many")]
    [InlineData("pl", "5", "many")]
    [InlineData("pl", "1.5", "other")]
    [InlineData("pt-PT", "0", "other")]
    [InlineData("pt", "0", "one")]
    [InlineData("pt-BR", "0", "one")]
    [InlineData("xx", "1", "other")]
    [InlineData("en", "12345678901234567890123", "other")]
    [InlineData("en", "1.0000000000000000000000001", "other")]
    public void PluralRules_Cardinal(string locale, string number, string expected)
    {
        var rules = Data.Supplemental.GetPluralRules(locale, PluralRuleType.Cardinal);
        Assert.Equal(expected, rules.Select(PluralOperands.Parse(number)));
    }

    [Theory]
    [InlineData("en", "1", "one")]
    [InlineData("en", "2", "two")]
    [InlineData("en", "3", "few")]
    [InlineData("en", "4", "other")]
    [InlineData("en", "11", "other")]
    [InlineData("en", "21", "one")]
    [InlineData("fr", "1", "one")]
    [InlineData("fr", "2", "other")]
    public void PluralRules_Ordinal(string locale, string number, string expected)
    {
        var rules = Data.Supplemental.GetPluralRules(locale, PluralRuleType.Ordinal);
        Assert.Equal(expected, rules.Select(PluralOperands.Parse(number)));
    }

    [Fact]
    public void PluralRules_Categories()
    {
        Assert.Equal(["zero", "one", "two", "few", "many", "other"], Data.Supplemental.GetPluralRules("ar", PluralRuleType.Cardinal).Categories);
        Assert.Equal(["one", "other"], Data.Supplemental.GetPluralRules("en", PluralRuleType.Cardinal).Categories);
        Assert.Equal(["one", "two", "few", "other"], Data.Supplemental.GetPluralRules("en", PluralRuleType.Ordinal).Categories);
        Assert.Equal(["other"], Data.Supplemental.GetPluralRules("ja", PluralRuleType.Cardinal).Categories);
    }

    [Fact]
    public void PluralOperands_Parse()
    {
        var operands = PluralOperands.Parse("-1.2300");
        Assert.Equal(1UL, operands.I);
        Assert.Equal(4, operands.V);
        Assert.Equal(2, operands.W);
        Assert.Equal(2300UL, operands.F);
        Assert.Equal(23UL, operands.T);
        Assert.Equal(0, operands.E);

        operands = PluralOperands.Parse("1.2c3");
        Assert.Equal(1200UL, operands.I);
        Assert.Equal(0, operands.V);
        Assert.Equal(3, operands.E);

        Assert.False(PluralOperands.TryParse("1.", out _));
        Assert.False(PluralOperands.TryParse("abc", out _));
    }

    // Every sample of the rules (e.g. "@integer 0, 2~16, 100 @decimal 0.0~1.5") must select its category
    [Fact]
    public void PluralRules_Samples()
    {
        var count = 0;
        foreach (var type in (PluralRuleType[])[PluralRuleType.Cardinal, PluralRuleType.Ordinal])
        {
            var section = Data.Supplemental.GetSection(type is PluralRuleType.Cardinal ? "plurals" : "ordinals")!;
            for (var i = 0; i < section.Count; i++)
            {
                var rules = Data.Supplemental.GetPluralRules(section.GetKey(i), type);
                for (var j = 0; j < rules.Categories.Count; j++)
                {
                    foreach (var sample in rules.Samples[j].Split((char[])[' ', ','], StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (sample.StartsWith('@', StringComparison.Ordinal) || sample is "\u2026")
                            continue;

                        foreach (var value in sample.Split('~'))
                        {
                            Assert.Equal(rules.Categories[j], rules.Select(PluralOperands.Parse(value)));
                            count++;
                        }
                    }
                }
            }
        }

        Assert.True(count > 5000, $"{count} samples");
    }

    [Fact]
    public void PluralRanges()
    {
        Assert.Equal("other", Data.Supplemental.GetPluralRange("fr", "one", "other"));
        Assert.Equal("one", Data.Supplemental.GetPluralRange("fr", "one", "one"));
        Assert.Equal("other", Data.Supplemental.GetPluralRange("en", "one", "other"));
        Assert.Null(Data.Supplemental.GetPluralRange("xx", "one", "other"));
    }

    [Theory]
    [InlineData("und", "en-Latn-US")]
    [InlineData("en", "en-Latn-US")]
    [InlineData("zh-TW", "zh-Hant-TW")]
    [InlineData("zh", "zh-Hans-CN")]
    [InlineData("und-Hant", "zh-Hant-TW")]
    [InlineData("und-Adlm-BF", "ff-Adlm-BF")]
    [InlineData("zh-Zzzz-SG", "zh-Hans-SG")]
    [InlineData("und-TW", "zh-Hant-TW")]
    [InlineData("sr-ME", "sr-Latn-ME")]
    [InlineData("fr-CA", "fr-Latn-CA")]
    public void LikelySubtags_Add(string source, string expected)
    {
        Assert.True(Data.Supplemental.LikelySubtags.TryAddLikelySubtags(LanguageId.Parse(source), out var result));
        Assert.Equal(expected, result.ToString());
    }

    // The test data of CLDR: common/testData/localeIdentifiers/likelySubtags.txt
    [Fact]
    public void LikelySubtags_TestData()
    {
        var likely = Data.Supplemental.LikelySubtags;
        var count = 0;
        var errors = new List<string>();
        foreach (var line in ReadTestData("likelySubtags.txt"))
        {
            if (line.Length == 0 || line.StartsWith('#', StringComparison.Ordinal))
                continue;

            var fields = line.Split(';').Select(field => field.Trim()).ToArray();
            var source = LanguageId.Parse(fields[0]);
            var expectedMax = fields[1];
            var expectedFavorScript = fields[2].Length == 0 ? expectedMax : fields[2];
            var expectedFavorRegion = fields.Length > 3 && fields[3].Length > 0 ? fields[3] : expectedFavorScript;
            count++;

            var actualMax = likely.TryAddLikelySubtags(source, out var max) ? max.ToString() : "FAIL";
            if (actualMax != expectedMax)
            {
                errors.Add($"{fields[0]}: add {actualMax}, expected {expectedMax}");
                continue;
            }

            if (expectedMax is "FAIL")
                continue;

            var actualFavorScript = likely.RemoveLikelySubtags(source, favorRegion: false).ToString();
            if (actualFavorScript != expectedFavorScript)
            {
                errors.Add($"{fields[0]}: remove (favor script) {actualFavorScript}, expected {expectedFavorScript}");
            }

            var actualFavorRegion = likely.RemoveLikelySubtags(source, favorRegion: true).ToString();
            if (actualFavorRegion != expectedFavorRegion)
            {
                errors.Add($"{fields[0]}: remove (favor region) {actualFavorRegion}, expected {expectedFavorRegion}");
            }
        }

        Assert.True(count > 1000, $"{count} test cases");
        Assert.Empty(errors.Take(20));
    }

    [Fact]
    public void Aliases()
    {
        Assert.True(Data.Supplemental.TryGetAlias(LocaleAliasType.Language, "iw", out var replacement));
        Assert.Equal("he", replacement);
        Assert.True(Data.Supplemental.TryGetAlias(LocaleAliasType.Language, "art-lojban", out replacement));
        Assert.Equal("jbo", replacement);
        Assert.True(Data.Supplemental.TryGetAlias(LocaleAliasType.Territory, "SU", out replacement));
        Assert.StartsWith("RU ", replacement);
        Assert.True(Data.Supplemental.TryGetAlias(LocaleAliasType.Script, "Qaai", out replacement));
        Assert.Equal("Zinh", replacement);
        Assert.True(Data.Supplemental.TryGetAlias(LocaleAliasType.Variant, "heploc", out replacement));
        Assert.Equal("alalc97", replacement);
        Assert.True(Data.Supplemental.TryGetAlias(LocaleAliasType.Zone, "America/Montreal", out replacement));
        Assert.Equal("America/Toronto", replacement);
        Assert.False(Data.Supplemental.TryGetAlias(LocaleAliasType.Language, "fr", out _));

        Assert.Equal("ethiopic-amete-alem", Data.Supplemental.GetString("bcp47", "u/ca/ethioaa/_alias"));
        Assert.Equal("calendar", Data.Supplemental.GetString("bcp47", "u/ca/_alias"));
        Assert.True(Data.Supplemental.GetSection("bcp47")!.TryGetNode("u/co/phonebk", out _));
    }

    [Fact]
    public void Metazones()
    {
        var instant = (CalendarMath.DaysFromCivil(2026, 1, 1) * 86_400) + 0L;
        Assert.Equal("Europe_Central", Data.Supplemental.GetMetazone("Europe/Paris", instant));
        Assert.Equal("America_Eastern", Data.Supplemental.GetMetazone("America/New_York", instant));
        Assert.Equal("Argentina", Data.Supplemental.GetMetazone("America/Buenos_Aires", instant));
        Assert.Equal("Argentina", Data.Supplemental.GetMetazone("America/Argentina/La_Rioja", instant));
        Assert.Equal("Europe_Western", Data.Supplemental.GetMetazone("Africa/Algiers", CalendarMath.DaysFromCivil(1970, 1, 1) * 86_400));
        Assert.Equal("Europe_Central", Data.Supplemental.GetMetazone("Africa/Algiers", instant));
        Assert.Null(Data.Supplemental.GetMetazone("Europe/Unknown", instant));
        Assert.Equal("Europe/Paris", Data.Supplemental.GetMetazoneZone("Europe_Central", "001"));
        Assert.Equal("Europe/Berlin", Data.Supplemental.GetMetazoneZone("Europe_Central", "DE"));
        Assert.Equal("Europe/Berlin", Data.Supplemental.GetPrimaryZone("DE"));
    }

    [Fact]
    public void TimeData()
    {
        var us = Data.Supplemental.GetTimeData(new LanguageId("en", "Latn", "US"));
        Assert.Equal("h", us.Preferred);
        Assert.Equal(["h", "hb", "H", "hB"], us.Allowed);
        Assert.Equal("H", Data.Supplemental.GetTimeData(new LanguageId("fr", "Latn", "FR")).Preferred);
        Assert.Equal("H", Data.Supplemental.GetTimeData(new LanguageId("fr", "Latn", "CA")).Preferred);
        Assert.Equal("h", Data.Supplemental.GetTimeData(new LanguageId("en", "Latn", "CA")).Preferred);
        Assert.Equal("H", Data.Supplemental.GetTimeData(new LanguageId("xx", "Latn", "ZZ")).Preferred);
    }

    [Fact]
    public void WeekData()
    {
        Assert.Equal(new WeekData(7, 1, 6, 7), Data.Supplemental.GetWeekData("US"));
        Assert.Equal(new WeekData(1, 4, 6, 7), Data.Supplemental.GetWeekData("FR"));
        Assert.Equal(new WeekData(6, 1, 4, 5), Data.Supplemental.GetWeekData("AF"));
        Assert.Equal(new WeekData(1, 1, 6, 7), Data.Supplemental.GetWeekData("ZZ"));
    }

    [Fact]
    public void CurrencyDigits()
    {
        Assert.Equal(new CurrencyDigits(2, 0, 2, 0), Data.Supplemental.GetCurrencyDigits("USD"));
        Assert.Equal(new CurrencyDigits(0, 0, 0, 0), Data.Supplemental.GetCurrencyDigits("JPY"));
        Assert.Equal(new CurrencyDigits(2, 0, 2, 5), Data.Supplemental.GetCurrencyDigits("CHF"));
        Assert.Equal(new CurrencyDigits(3, 0, 3, 0), Data.Supplemental.GetCurrencyDigits("BHD"));
        Assert.Equal(new CurrencyDigits(2, 0, 2, 0), Data.Supplemental.GetCurrencyDigits("XXX"));
    }

    [Fact]
    public void NumberingSystems()
    {
        Assert.Equal("0123456789", Data.Supplemental.GetNumberingSystemDigits("latn"));
        Assert.Equal("\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668\u0669", Data.Supplemental.GetNumberingSystemDigits("arab"));
        Assert.Null(Data.Supplemental.GetNumberingSystemDigits("roman"));
        var numeric = Data.Supplemental.GetNumericNumberingSystems();
        Assert.Contains("thai", numeric);
        Assert.DoesNotContain("roman", numeric);
    }

    [Fact]
    public void CalendarPreferences()
    {
        Assert.Equal("buddhist", Data.Supplemental.GetCalendarPreferences("TH")[0]);
        Assert.Equal(["gregorian"], Data.Supplemental.GetCalendarPreferences("FR"));
        Assert.Equal("ce", Data.Supplemental.GetString("calendarData", "gregorian/eras/1/_code"));
    }

    [Fact]
    public void DayPeriodRules()
    {
        var rules = Data.Supplemental.GetDayPeriodRules("en");
        Assert.Contains(new DayPeriodRule("midnight", 0, 0, 0), rules);
        Assert.Contains(new DayPeriodRule("noon", 720, 0, 0), rules);
        Assert.Contains(new DayPeriodRule("morning1", null, 0, 720), rules);
        Assert.Empty(Data.Supplemental.GetDayPeriodRules("xx"));
    }

    [Fact]
    public void CollationRoot()
    {
        var root = Data.Collation.Root;
        Assert.True(root.Count > 30_000);
        Assert.True(root.TryGetMapping('a', out var a));
        Assert.True(root.TryGetMapping('b', out var b));
        Assert.True(a[0].Primary < b[0].Primary);
        Assert.True(root.TryGetMapping('A', out var upperA));
        Assert.Equal(a[0].Primary, upperA[0].Primary);
        Assert.True(a[0].Tertiary < upperA[0].Tertiary);
        Assert.True(root.TryGetMapping(' ', out var space));
        Assert.True(space[0].IsVariable);

        // Implicit weights
        Assert.False(root.TryGetMapping(0x4E00, out _));
        Assert.Equal((CollationElement.Create(0xFB40, 0x20, 0x02, isVariable: false), CollationElement.Create(0xCE00, 0, 0, isVariable: false)), root.GetImplicitElements(0x4E00));
        Assert.Equal(0xFB80, root.GetImplicitElements(0x3400).First.Primary);
        Assert.Equal(0xFB84, root.GetImplicitElements(0x20000).First.Primary);
        Assert.Equal(0xFB00, root.GetImplicitElements(0x17000).First.Primary);
        Assert.Equal(0x8000, root.GetImplicitElements(0x17000).Second.Primary);
        Assert.Equal(0xFBCA, root.GetImplicitElements(0x50000).First.Primary);
    }

    // The conformance test of the CLDR root collation (common/uca/CollationTest_CLDR_NON_IGNORABLE_SHORT.txt): each
    // line sorts after the previous one, comparing the levels 1 to 3 then the code points (identical level, where the
    // merge separator U+FFFE sorts before every other code point, as in ICU)
    [Fact]
    public void CollationRoot_ConformanceTest()
    {
        var root = Data.Collation.Root;
        int[]? previous = null;
        var previousLine = "";
        var count = 0;
        var errors = new List<string>();
        foreach (var line in ReadTestData("CollationTest_CLDR_NON_IGNORABLE_SHORT.txt"))
        {
            if (line.Length == 0 || line.StartsWith('#', StringComparison.Ordinal))
                continue;

            var text = new StringBuilder();
            foreach (var item in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var codePoint = int.Parse(item, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                text.Append(codePoint is >= 0xD800 and <= 0xDFFF ? ((char)codePoint).ToString() : char.ConvertFromUtf32(codePoint));
            }

            var key = GetSortKey(root, text.ToString());
            if (previous is not null && previous.AsSpan().SequenceCompareTo(key) > 0)
            {
                errors.Add($"{line} sorts before {previousLine}");
            }

            previous = key;
            previousLine = line;
            count++;
        }

        Assert.True(count > 100_000, $"{count} lines");
        Assert.Empty(errors.Take(20));

        static int[] GetSortKey(CollationRootTable root, string text)
        {
            var elements = new List<CollationElement>();
            root.AppendCollationElements(text, elements);
            var key = new List<int>();
            key.AddRange(elements.Where(element => element.Primary != 0).Select(element => element.Primary));
            key.Add(0);
            key.AddRange(elements.Where(element => element.Secondary != 0).Select(element => element.Secondary));
            key.Add(0);
            key.AddRange(elements.Where(element => element.Tertiary != 0).Select(element => element.Tertiary));
            key.Add(0);
            var nfd = new List<int>();
            CollationRootTable.AppendNfd(text, nfd);
            key.AddRange(nfd.Select(codePoint => codePoint is 0xFFFE ? -1 : codePoint));
            return [.. key];
        }
    }

    [Fact]
    public void CollationTailorings()
    {
        var collation = Data.Collation;
        Assert.Contains("de", collation.TailoredLocales);
        Assert.Contains("und", collation.TailoredLocales);

        var count = 0;
        foreach (var locale in collation.TailoredLocales)
        {
            foreach (var type in collation.GetCollationTypes(LanguageId.Parse(locale)))
            {
                var rules = collation.GetRules(LanguageId.Parse(locale), type);
                Assert.NotNull(rules);
                _ = CollationTailoring.Parse(rules);
                count++;
            }
        }

        Assert.True(count > 100, $"{count} tailorings");

        var de = LanguageId.Parse("de");
        Assert.Equal(["emoji", "eor", "phonebook", "search", "standard"], collation.GetCollationTypes(de));
        Assert.Equal("", collation.GetRules(de, "standard"));
        Assert.Contains("&AE<<\u00E4<<<\u00C4", collation.GetRules(de, "phonebook")!);
        Assert.Null(collation.GetRules(de, "pinyin"));
        Assert.DoesNotContain("[import", collation.GetRules(de, "search")!);

        Assert.Equal("pinyin", collation.GetDefaultCollation(LanguageId.Parse("zh")));
        Assert.Equal("stroke", collation.GetDefaultCollation(LanguageId.Parse("zh-Hant")));
        Assert.Equal("stroke", collation.GetDefaultCollation(LanguageId.Parse("zh-TW")));
        Assert.Equal("stroke", collation.GetDefaultCollation(LanguageId.Parse("yue")));
        Assert.Equal("standard", collation.GetDefaultCollation(LanguageId.Parse("fr")));
        Assert.Equal(["fr-CA", "und"], collation.GetFallbackChain(LanguageId.Parse("fr-CA")));
        Assert.Equal(["sr-Latn", "sr", "und"], collation.GetFallbackChain(LanguageId.Parse("sr-Latn-RS")));
        Assert.Equal(["az", "und"], collation.GetFallbackChain(LanguageId.Parse("az-Cyrl")));
        Assert.Equal(["zh-Hant", "zh", "und"], collation.GetFallbackChain(LanguageId.Parse("zh-TW")));
        Assert.Equal(["zh-Hant", "zh", "und"], collation.GetFallbackChain(LanguageId.Parse("yue")));
        Assert.Equal(["es", "und"], collation.GetFallbackChain(LanguageId.Parse("es-MX")));
        Assert.Equal(["no", "und"], collation.GetFallbackChain(LanguageId.Parse("nb")));
        Assert.Equal(["pinyin", "standard", "stroke", "unihan", "zhuyin"], collation.GetCollationTypes(LanguageId.Parse("zh-Hant")).Where(type => type is not ("emoji" or "eor" or "search")).ToArray());
        Assert.Equal("[backwards 2]", collation.GetRules(LanguageId.Parse("fr-CA"), "standard"));
    }

    // The collator with the root collation: each line of the conformance tests of CLDR sorts after the previous one
    // when comparing the levels 1 to 3 (the variable characters are ignored with the shifted alternate)
    [Theory]
    [InlineData("CollationTest_CLDR_NON_IGNORABLE_SHORT.txt", false)]
    [InlineData("CollationTest_CLDR_SHIFTED_SHORT.txt", true)]
    public void Collator_ConformanceTest(string file, bool shifted)
    {
        var collator = TailoredCollation.Root.GetCollator(new CollatorSettings(3, CaseLevel: false, CollationCaseFirst.Off, shifted, Numeric: false));
        string? previous = null;
        var previousLine = "";
        var count = 0;
        var errors = new List<string>();
        foreach (var line in ReadTestData(file))
        {
            if (line.Length == 0 || line.StartsWith('#', StringComparison.Ordinal))
                continue;

            var text = new StringBuilder();
            foreach (var item in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var codePoint = int.Parse(item, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                text.Append(codePoint is >= 0xD800 and <= 0xDFFF ? ((char)codePoint).ToString() : char.ConvertFromUtf32(codePoint));
            }

            var current = text.ToString();
            if (previous is not null && (collator.Compare(previous, current) > 0 || collator.CompareWithoutFastPath(previous, current) > 0))
            {
                errors.Add($"{line} sorts before {previousLine}");
            }

            previous = current;
            previousLine = line;
            count++;
        }

        Assert.True(count > 100_000, $"{count} lines");
        Assert.Empty(errors.Take(20));
    }

    // Every tailoring of the data builds, and the fast path of the comparison (common prefix, precomputed collation
    // elements of the characters below U+0300) gives the results of the general path
    [Fact]
    public void Collator_Tailorings()
    {
        var collation = Data.Collation;
        var seed = 42u;
        const string Pool = "aAbcChHlLnNoOsSzZ019 -.'\u00E4\u00C4\u00E5\u00E6\u00F1\u00E7\u00F6\u00FC\u00DF\u0131\u0130\u0301\u0308\u00B7\u0430\u4E00\uAC00\u3042\u30FC";
        var count = 0;
        foreach (var locale in collation.TailoredLocales)
        {
            foreach (var type in collation.GetCollationTypes(LanguageId.Parse(locale)))
            {
                var tailoring = TailoredCollation.Get(locale, type);
                Assert.NotNull(tailoring);
                foreach (var settings in (CollatorSettings[])[new(3, CaseLevel: false, CollationCaseFirst.Off, Shifted: false, Numeric: false), new(1, CaseLevel: true, CollationCaseFirst.Upper, Shifted: true, Numeric: true)])
                {
                    var collator = tailoring.GetCollator(settings);
                    for (var i = 0; i < 50; i++)
                    {
                        var x = RandomString(ref seed);
                        var y = Next(ref seed, 3) == 0 ? x + RandomString(ref seed) : RandomString(ref seed);
                        if (Next(ref seed, 2) == 0)
                        {
                            (x, y) = (y, x);
                        }

                        Assert.Equal(Math.Sign(collator.CompareWithoutFastPath(x, y)), Math.Sign(collator.Compare(x, y)), $"{locale} {type} {settings}: '{x}' '{y}'");
                        Assert.Equal(-Math.Sign(collator.Compare(x, y)), Math.Sign(collator.Compare(y, x)));
                    }
                }

                count++;
            }
        }

        Assert.True(count > 100, $"{count} tailorings");

        static string RandomString(ref uint seed)
        {
            var length = 1 + Next(ref seed, 5);
            var builder = new StringBuilder();
            for (var i = 0; i < length; i++)
            {
                builder.Append(Pool[Next(ref seed, Pool.Length)]);
            }

            return builder.ToString();
        }

        // A deterministic pseudo-random sequence (LCG)
        static int Next(ref uint seed, int maxValue)
        {
            seed = (seed * 1103515245) + 12345;
            return (int)((seed >> 8) % (uint)maxValue);
        }
    }

    [Fact]
    public void Collator_ScriptGroups()
    {
        var root = RootCollation.Instance;
        var groups = root.ScriptRuns.Select(run => run.Group).ToList();
        Assert.Equal(["space", "punct", "symbol", "currency", "digit", "Latn", "Grek"], groups.Take(7).ToArray());
        Assert.Contains("Hani", groups);
        Assert.Contains("Hrkt", groups);

        // [reorder Cyrl]: the Cyrillic letters before the Latin ones, the digits and the symbols do not move
        var reorder = root.GetReorderTable(["Cyrl"])!;
        var a = CollationWeights.Primary(root.Map.GetElements(root.Map.GetEntry('a'))[0]) >> 16;
        var be = CollationWeights.Primary(root.Map.GetElements(root.Map.GetEntry('\u0431'))[0]) >> 16;
        var zero = CollationWeights.Primary(root.Map.GetElements(root.Map.GetEntry('0'))[0]) >> 16;
        Assert.True(reorder[be] < reorder[a]);
        Assert.Equal(zero, reorder[zero]);
        Assert.Null(root.GetReorderTable([]));
    }

    [Fact]
    public void CollationRuleParser()
    {
        var tailoring = CollationTailoring.Parse("[backwards 2][caseFirst upper]&N<\u00F1<<<\u00D1 &a<*b-df &[before 1]c<'#'|x/y &[last regular]<z [reorder Grek Latn] # comment");
        Assert.True(tailoring.Backwards);
        Assert.Equal("upper", tailoring.CaseFirst);
        Assert.Equal(["Grek", "Latn"], tailoring.Reorder);
        Assert.Equal(4, tailoring.Chains.Count);

        Assert.Equal("N", tailoring.Chains[0].Reset);
        Assert.Equal([new CollationRelation(CollationStrength.Primary, "\u00F1"), new CollationRelation(CollationStrength.Tertiary, "\u00D1")], tailoring.Chains[0].Relations);

        Assert.Equal(["b", "c", "d", "f"], tailoring.Chains[1].Relations.Select(relation => relation.Text));

        Assert.Equal(CollationStrength.Primary, tailoring.Chains[2].Before);
        Assert.Equal(new CollationRelation(CollationStrength.Primary, "x", "#", "y"), tailoring.Chains[2].Relations[0]);

        Assert.Equal("last regular", tailoring.Chains[3].ResetPosition);
        Assert.Equal("z", tailoring.Chains[3].Relations[0].Text);

        // The escapes of the XML rules are unescaped in a quoted text too
        Assert.Equal("\u3000x", CollationTailoring.Parse("&a='\\u3000x'").Chains[0].Relations[0].Text);

        Assert.Throws<FormatException>(() => CollationTailoring.Parse("<a"));
        Assert.Throws<FormatException>(() => CollationTailoring.Parse("&a<"));
        Assert.Throws<FormatException>(() => CollationTailoring.Parse("[import de]"));
    }
}
