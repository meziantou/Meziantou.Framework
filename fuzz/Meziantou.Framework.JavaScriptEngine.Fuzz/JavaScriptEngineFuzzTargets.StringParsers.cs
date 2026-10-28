using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.JavaScriptEngine.Fuzz;

/// <summary>
/// The builtins that parse strings (Temporal, <c>Date.parse</c>, the language tags of Intl) accept a string or reject it
/// with a RangeError or a TypeError, and accept the strings they serialize: a Temporal value reads its
/// <c>toString()</c> as the same value, <c>Date.parse</c> reads the strings of a Date as its time, and a language tag is
/// canonicalized once.
/// </summary>
public static partial class JavaScriptEngineFuzzTargets
{
    private static string[] StringParserTokens => StringParserData.Tokens;

    /// <summary>Parses the text (after an option byte that chooses the builtin) with a builtin, and parses its serialization.</summary>
    private static void StringParsers(ReadOnlySpan<byte> data)
    {
        var parser = FuzzInput.Pick(StringParserData.Scripts, FuzzInput.ReadByte(ref data));
        var input = FuzzText.Decode(data);
        var script = $$"""
            (() => {
              let value;
              try {
                value = {{parser.Parse}};
              } catch (e) {
                return e instanceof RangeError || e instanceof TypeError ? "rejected" : "FAIL: " + String(e);
              }

              {{parser.Check}}
            })()
            """;

        var outcome = RunScript(realm =>
        {
            using (realm.Engine.Enter())
            {
                realm.GlobalObject.SetProperty("input", JsValue.FromString(input));
            }

            return realm.Evaluate(script);
        });

        FuzzAssert.Equal(ScriptOutcomeKind.Value, outcome.Kind, $"{parser.Name} does not return: {outcome.Text}");
        FuzzAssert.False(outcome.Text.Contains("FAIL:", StringComparison.Ordinal), $"{parser.Name}: {outcome.Text}");
    }

    private static IEnumerable<FuzzSeed> GetStringParserSeeds()
    {
        var index = 0;
        for (var parser = 0; parser < StringParserData.Scripts.Length; parser++)
        {
            foreach (var text in StringParserData.Scripts[parser].Seeds)
            {
                yield return FuzzSeed.FromText($"{StringParserData.Scripts[parser].Name}-{index++}", [(byte)parser], text);
            }
        }
    }

    /// <summary>The scripts and the seeds of the builtins, in a class of their own so that they are initialized when first used.</summary>
    private static class StringParserData
    {
        private static readonly string[] InstantSeeds = ["2026-10-21T15:04:05Z", "2026-10-21T15:04:05.123456789+05:30", "1970-01-01T00:00Z", "+275760-09-13T00:00:00Z", "-271821-04-20T00:00:00Z", "2026-10-21t15:04:05z", "2026-10-21 15:04:05,5-08:00", "20261021T150405Z", "2026-10-21T15:04:05Z[Europe/Paris]", "2026-10-21T15:04:05+01:00[!Europe/Paris][u-ca=iso8601]", "2026-02-30T00:00Z", "2026-10-21T24:00:00Z"];

        private static readonly string[] DateSeeds = ["2026-10-21", "+002026-10-21", "-000001-01-01", "20261021", "2026-10-21T15:04", "2026-10-21[u-ca=gregory]", "2026-10-21[u-ca=hebrew][!u-ca=iso8601]", "2026-10-21T15:04:05Z[UTC]", "2026-13-01", "-000000-01-01", "2026-10-21[foo=bar]", "2026-10-21[!foo=bar]"];

        private static readonly string[] TimeSeeds = ["15:04:05", "T15:04", "15:04:05.123456789", "150405", "23:59:60", "24:00", "15:04:05+05:30", "15:04:05Z", "T1504", "12:34:56[u-ca=iso8601]", "15", "2026-10-21T15:04:05"];

        private static readonly string[] DateTimeSeeds = ["2026-10-21T15:04:05", "2026-10-21 15:04:05.5", "2026-10-21T15:04:05-05:00[America/New_York]", "+275760-09-13T00:00", "2026-10-21T15:04:05[u-ca=japanese]", "2026-10-21T15:04:05.1234567891", "2026-10-21"];

        private static readonly string[] YearMonthSeeds = ["2026-10", "202610", "+002026-10", "2026-10-21", "2026-10[u-ca=iso8601]", "2026-10-01[u-ca=chinese]", "-271821-04"];

        private static readonly string[] MonthDaySeeds = ["10-21", "--10-21", "1021", "--1021", "2026-02-29", "02-30", "1972-02-29[u-ca=iso8601]", "2026-10-21[u-ca=islamic-civil]"];

        private static readonly string[] ZonedDateTimeSeeds = ["2026-10-21T15:04:05+02:00[Europe/Paris]", "2026-10-21T15:04:05Z[UTC]", "2026-03-29T02:30:00[Europe/Paris]", "2026-10-21T15:04:05[+05:30]", "2026-10-21T15:04:05-00:00[Etc/GMT+5]", "2026-10-21T15:04:05[America/Argentina/Buenos_Aires][u-ca=gregory]", "2026-10-21[Asia/Kolkata]", "2026-10-21T15:04:05+01:00[Europe/London]"];

        private static readonly string[] DurationSeeds = ["P1Y2M3W4DT5H6M7.123456789S", "PT0S", "-P1D", "+PT1H", "P1D", "PT1.5H", "PT0.000000001S", "P9999999999999999999D", "PT36H", "pt1m", "P", "PT", "P1Y1H", "PT1,5S"];

        private static readonly string[] DateParseSeeds = ["2026-10-21", "2026-10-21T15:04:05.123Z", "+275760-09-13T00:00:00.000Z", "-271821-04-20T00:00:00.000Z", "Wed Oct 21 2026 15:04:05 GMT+0000 (Coordinated Universal Time)", "Wed, 21 Oct 2026 15:04:05 GMT", "Oct 21, 2026", "21 October 2026 3:04 PM", "2026/10/21 15:04:05", "10/21/2026", "Thu Jan 01 -1000 00:00:00 GMT+0000", "2026-10-21T15:04:05+05:30", "2026-10-21T24:00:00Z", "December 17, 1995 03:24:00 PST", "1e3", "-000000-01-01T00:00:00Z", "2026-W43-3"];

        private static readonly string[] LocaleSeeds = ["en", "en-US", "zh-Hant-TW", "de-latn-de-fonipa-1996-u-ca-gregory-co-phonebk-hc-h23-kf-true", "sr-Cyrl-RS", "und", "x-private", "en-u-ca-japanese-nu-arab", "en-t-ja-m0-names", "en-a-foo-b-bar", "art-lojban", "zh-min-nan", "i-klingon", "EN-us-POSIX", "sgn-BE-FR", "aar", "ja-JP-u-ca-japanese-x-linux", "en-US-u-va-posix", "root", "en_US", "es-419", "und-Latn-AQ", "cel-gaulish", "hy-arevela"];

        private static readonly string[] FormatSeeds = ["en-US", "ar-EG-u-nu-arab", "th-TH-u-nu-thai-ca-buddhist", "ja-JP-u-ca-japanese", "fr-CH", "de-DE-u-co-phonebk", "zh-Hans-CN-u-nu-hanidec", "hi-IN-u-nu-deva", "en-GB-u-hc-h12", "und"];

        // A Temporal value is read back from its toString()
        private const string TemporalCheck = """
            const text = value.toString();
            const again = Temporal.{0}.from(text).toString();
            return again === text ? "ok:" + text : "FAIL: " + text + " reads as " + again;
            """;

        public static readonly (string Name, string Parse, string Check, string[] Seeds)[] Scripts =
        [
            ("Temporal.Instant", "Temporal.Instant.from(input)", string.Format(System.Globalization.CultureInfo.InvariantCulture, TemporalCheck, "Instant"), InstantSeeds),
            ("Temporal.PlainDate", "Temporal.PlainDate.from(input)", string.Format(System.Globalization.CultureInfo.InvariantCulture, TemporalCheck, "PlainDate"), DateSeeds),
            ("Temporal.PlainTime", "Temporal.PlainTime.from(input)", string.Format(System.Globalization.CultureInfo.InvariantCulture, TemporalCheck, "PlainTime"), TimeSeeds),
            ("Temporal.PlainDateTime", "Temporal.PlainDateTime.from(input)", string.Format(System.Globalization.CultureInfo.InvariantCulture, TemporalCheck, "PlainDateTime"), DateTimeSeeds),
            ("Temporal.PlainYearMonth", "Temporal.PlainYearMonth.from(input)", string.Format(System.Globalization.CultureInfo.InvariantCulture, TemporalCheck, "PlainYearMonth"), YearMonthSeeds),
            ("Temporal.PlainMonthDay", "Temporal.PlainMonthDay.from(input)", string.Format(System.Globalization.CultureInfo.InvariantCulture, TemporalCheck, "PlainMonthDay"), MonthDaySeeds),
            ("Temporal.ZonedDateTime", "Temporal.ZonedDateTime.from(input)", string.Format(System.Globalization.CultureInfo.InvariantCulture, TemporalCheck, "ZonedDateTime"), ZonedDateTimeSeeds),
            ("Temporal.Duration", "Temporal.Duration.from(input)", string.Format(System.Globalization.CultureInfo.InvariantCulture, TemporalCheck, "Duration"), DurationSeeds),

            // https://tc39.es/ecma262/#sec-date.parse: the strings of a Date whose milliseconds are zero read as its time
            ("Date.parse", "Date.parse(input)", """
                if (Number.isNaN(value))
                  return "nan";

                if (!Number.isInteger(value) || Math.abs(value) > 8.64e15)
                  return "FAIL: Date.parse returns " + value;

                const date = new Date(value);
                if (Date.parse(date.toISOString()) !== value)
                  return "FAIL: " + date.toISOString() + " reads as " + Date.parse(date.toISOString());

                // Like V8, the legacy formats do not read a year before 0 or after 9999 ("Tue Apr 20 -271821 ...")
                const seconds = new Date(value - (((value % 1000) + 1000) % 1000));
                const year = seconds.getUTCFullYear();
                for (const text of year >= 0 && year <= 9999 ? [seconds.toString(), seconds.toUTCString()] : []) {
                  if (Date.parse(text) !== seconds.getTime())
                    return "FAIL: " + text + " reads as " + Date.parse(text) + ", not " + seconds.getTime();
                }

                return "ok:" + value;
                """, DateParseSeeds),

            // A language tag is canonicalized once, as Intl.getCanonicalLocales does, and so are the tags of maximize() and minimize()
            ("Intl.Locale", "new Intl.Locale(input)", """
                const text = value.toString();
                const again = new Intl.Locale(text).toString();
                if (again !== text)
                  return "FAIL: " + text + " canonicalizes to " + again;

                const canonical = Intl.getCanonicalLocales(input)[0];
                if (canonical !== text)
                  return "FAIL: Intl.getCanonicalLocales gives " + canonical + ", Intl.Locale gives " + text;

                for (const other of [value.maximize().toString(), value.minimize().toString()]) {
                  if (new Intl.Locale(other).toString() !== other)
                    return "FAIL: " + other + " canonicalizes to " + new Intl.Locale(other).toString();
                }

                return "ok:" + text;
                """, LocaleSeeds),

            // The locale negotiation of the formatters resolves to a language tag
            ("Intl formatters", "[new Intl.NumberFormat(input), new Intl.DateTimeFormat(input), new Intl.Collator(input), new Intl.PluralRules(input)]", """
                value[0].format(1234.5);
                value[1].format(0);
                value[2].compare("a", "b");
                value[3].select(1);
                for (const formatter of value) {
                  const locale = formatter.resolvedOptions().locale;
                  if (new Intl.Locale(locale).toString() !== locale)
                    return "FAIL: the resolved locale " + locale + " is not canonical";
                }

                return "ok:" + value[0].resolvedOptions().locale;
                """, FormatSeeds),
        ];

        public static readonly string[] Tokens =
        [
            "T", "Z", "z", "t", " ", ":", "-", "+", ".", ",", "[", "]", "[!", "=", "u-ca=", "u-ca=iso8601", "u-ca=gregory", "u-ca=japanese", "u-ca=hebrew", "u-ca=chinese", "u-ca=islamic-umalqura",
            "UTC", "Europe/Paris", "America/New_York", "Etc/GMT+5", "+05:30", "-00:00", "+14:00", "2026", "+002026", "-271821", "+275760", "10", "21", "29", "60", "24:00", "15:04:05", ".123456789",
            "P", "PT", "Y", "M", "W", "D", "H", "S", "1Y", "2M", "3W", "4D", "5H", "6M", "7.5S",
            "GMT", "UTC+0", "Mon", "Wed", "Oct", "October", "AM", "PM", "PST", "(Coordinated Universal Time)",
            "-u-", "-t-", "-x-", "-a-", "-nu-", "-co-", "-hc-", "-kf-", "-va-", "posix", "latn", "arab", "Hant", "Hans", "TW", "419", "und", "root", "i-", "x-private", "fonipa", "1996",
        ];
    }
}
