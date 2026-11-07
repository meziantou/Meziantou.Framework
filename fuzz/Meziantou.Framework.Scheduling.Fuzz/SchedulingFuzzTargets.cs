using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Scheduling.Fuzz;

/// <summary>
/// Recurrence rules, cron expressions and iCalendar objects usually come from outside. Parsing must only report invalid
/// input through <c>TryParse</c> returning <see langword="false"/> or <c>Parse</c> throwing <see cref="FormatException"/>,
/// every overload must agree, and what is parsed must write back to text that parses to the same value. The evaluation of
/// a rule must terminate, even for a rule that never matches, and its occurrences must be increasing, after the start,
/// within COUNT and UNTIL, and satisfy every rule part. The enumerations are bounded by the harness.
/// </summary>
public static partial class SchedulingFuzzTargets
{
    /// <summary>The number of occurrences of a rule that are enumerated.</summary>
    private const int MaxOccurrences = 50;

    // A start date is a notable date picked by an option byte, or one built from the following bytes
    private static readonly DateTime[] NotableStarts =
    [
        new(1997, 09, 02, 09, 00, 00),
        new(2024, 01, 01, 00, 00, 00),
        new(2000, 02, 29, 12, 30, 15),
        new(2024, 03, 10, 02, 30, 00), // In the gap of the forward transition of the test time zones
        new(2024, 11, 03, 01, 30, 00), // In the hour repeated by the backward transition of the test time zones
        new(2024, 01, 31, 23, 59, 59),
        new(2023, 12, 31, 23, 59, 59),
        new(1970, 01, 01, 00, 00, 00),
        new(1600, 02, 29, 00, 00, 00),
        DateTime.MinValue,
        new(1, 1, 1, 23, 59, 59),
        new(1, 1, 7, 12, 0, 0),
        new(9999, 12, 31, 00, 00, 00),
        new(9999, 12, 31, 23, 59, 59),
        new(9999, 12, 25, 12, 00, 00),
        DateTime.MaxValue,
        new(2024, 06, 15, 10, 20, 30, 500),
    ];

    // The time zones a rule is evaluated in: their adjustment rules span every year, so they are valid on every target framework
    private static readonly TimeZoneInfo[] TimeZones =
    [
        TimeZoneInfo.Utc,
        CreateTimeZone("Test/Eastern", TimeSpan.FromHours(-5), TimeSpan.FromHours(1), (3, 2, 2), (11, 1, 2)),
        CreateTimeZone("Test/Sydney", TimeSpan.FromHours(10), TimeSpan.FromHours(1), (10, 1, 2), (4, 1, 3)),
        CreateTimeZone("Test/LordHowe", new TimeSpan(10, 30, 0), TimeSpan.FromMinutes(30), (10, 1, 2), (4, 1, 2)),
        CreateTimeZone("Test/London", TimeSpan.Zero, TimeSpan.FromHours(1), (3, 5, 1), (10, 5, 2)),
        TimeZoneInfo.CreateCustomTimeZone("Test/PlusFourteen", TimeSpan.FromHours(14), "Test/PlusFourteen", "Test/PlusFourteen"),
        TimeZoneInfo.CreateCustomTimeZone("Test/MinusTwelve", TimeSpan.FromHours(-12), "Test/MinusTwelve", "Test/MinusTwelve"),
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("scheduling-rrule-parse", RecurrenceRuleParse) { Seeds = GetRecurrenceRuleParseSeeds, Dictionary = RecurrenceRuleTokens },
        new("scheduling-rrule-occurrences", RecurrenceRuleOccurrences) { Seeds = GetRecurrenceRuleOccurrencesSeeds, Dictionary = RecurrenceRuleTokens },
        new("scheduling-cron", Cron) { Seeds = GetCronSeeds, Dictionary = CronTokens },
        new("scheduling-ics", Calendar) { Seeds = GetCalendarSeeds, Dictionary = CalendarTokens },
        new("scheduling-ics-content-line", CalendarContentLine) { Seeds = GetContentLineSeeds, Dictionary = ContentLineTokens },
        new("scheduling-ics-duration", CalendarDuration) { Seeds = GetDurationSeeds, Dictionary = DurationTokens },
    ];

    private static TimeZoneInfo CreateTimeZone(string id, TimeSpan baseUtcOffset, TimeSpan daylightDelta, (int Month, int Week, int Hour) start, (int Month, int Week, int Hour) end)
    {
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, start.Hour, 0, 0), start.Month, start.Week, DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, end.Hour, 0, 0), end.Month, end.Week, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, daylightDelta, daylightStart, daylightEnd);
        return TimeZoneInfo.CreateCustomTimeZone(id, baseUtcOffset, id, id + " Standard", id + " Daylight", [rule]);
    }

    /// <summary>Reads a start date: a byte selects a notable date or a date built from the next 8 bytes, and its kind (<see cref="DateTimeKind.Unspecified"/>, <see cref="DateTimeKind.Utc"/> or, when allowed, <see cref="DateTimeKind.Local"/>).</summary>
    private static DateTime ReadStart(ref ReadOnlySpan<byte> data, bool allowLocal)
    {
        var mode = FuzzInput.ReadByte(ref data);
        var kind = ((mode >> 5) & 0x03) switch
        {
            1 => DateTimeKind.Utc,
            2 when allowLocal => DateTimeKind.Local,
            _ => DateTimeKind.Unspecified,
        };

        DateTime value;
        if ((mode & 0x80) is 0)
        {
            value = NotableStarts[(mode & 0x1F) % NotableStarts.Length];
        }
        else
        {
            var year = 1 + (((FuzzInput.ReadByte(ref data) << 8) | FuzzInput.ReadByte(ref data)) % 9999);
            var month = 1 + (FuzzInput.ReadByte(ref data) % 12);
            var day = 1 + (FuzzInput.ReadByte(ref data) % DateTime.DaysInMonth(year, month));
            var hour = FuzzInput.ReadByte(ref data) % 24;
            var minute = FuzzInput.ReadByte(ref data) % 60;
            var second = FuzzInput.ReadByte(ref data) % 60;
            var fraction = FuzzInput.ReadByte(ref data) * 39_215L % TimeSpan.TicksPerSecond;
            value = new DateTime(year, month, day, hour, minute, second).AddTicks(fraction);
        }

        return DateTime.SpecifyKind(value, kind);
    }

    /// <summary>Checks the occurrences of an evaluation in a time zone: increasing instants, each carrying the offset of the time zone at that instant.</summary>
    private static void CheckZonedOccurrences(List<DateTimeOffset> occurrences, TimeZoneInfo timeZone, string what)
    {
        for (var i = 0; i < occurrences.Count; i++)
        {
            var occurrence = occurrences[i];
            FuzzAssert.Equal(timeZone.GetUtcOffset(occurrence), occurrence.Offset, $"{what}: the occurrence {Format(occurrence)} does not carry the offset of {timeZone.Id}");
            if (i > 0)
            {
                FuzzAssert.True(occurrences[i - 1].UtcTicks < occurrence.UtcTicks, $"{what}: the occurrences are not increasing: {Format(occurrences[i - 1])} then {Format(occurrence)}");
            }
        }
    }

    /// <summary>Checks that the evaluation in a time zone without daylight saving time gives the wall-clock occurrences, without those outside the range of <see cref="DateTimeOffset"/>.</summary>
    /// <param name="zoned">The occurrences in the time zone, at most <see cref="MaxOccurrences"/> of them.</param>
    /// <param name="wallClocks">The wall-clock occurrences, at most <see cref="MaxOccurrences"/> of them.</param>
    private static void CheckFixedOffsetOccurrences(List<DateTimeOffset> zoned, List<DateTime> wallClocks, TimeSpan offset, string what)
    {
        var expected = new List<DateTime>();
        foreach (var wallClock in wallClocks)
        {
            var utcTicks = wallClock.Ticks - offset.Ticks;
            if (utcTicks < DateTime.MinValue.Ticks)
                continue;

            // The enumeration ends at the first occurrence after the range
            if (utcTicks > DateTime.MaxValue.Ticks)
                break;

            expected.Add(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified));
        }

        var actual = zoned.Select(occurrence => occurrence.DateTime).ToList();
        var count = Math.Min(expected.Count, actual.Count);
        FuzzAssert.True(expected.Take(count).SequenceEqual(actual.Take(count)), $"{what}: the occurrences in a fixed-offset time zone are not the wall-clock occurrences{Environment.NewLine}Expected: {Format(expected)}{Environment.NewLine}Actual:   {Format(actual)}");
        if (wallClocks.Count < MaxOccurrences && expected.Count == wallClocks.Count)
        {
            FuzzAssert.Equal(expected.Count, actual.Count, $"{what}: the number of occurrences in a fixed-offset time zone differs from the wall-clock occurrences");
        }
    }

    private static string Format(DateTime value) => value.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture) + value.Kind switch
    {
        DateTimeKind.Utc => "Z",
        DateTimeKind.Local => " (local)",
        _ => "",
    };

    private static string Format(DateTimeOffset value) => value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture);

    private static string Format(IEnumerable<DateTime> values) => "[" + string.Join(", ", values.Select(Format)) + "]";

    private static string Format(IEnumerable<DateTimeOffset> values) => "[" + string.Join(", ", values.Select(Format)) + "]";

    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the string literals of an embedded unit test file, in order, with their escape sequences decoded.</summary>
    private static List<string> GetStringLiterals(string fileName)
    {
        var source = FuzzResources.ReadText(typeof(SchedulingFuzzTargets).Assembly, fileName);
        return GetStringLiteralsFromSource(source);
    }

    private static List<string> GetStringLiteralsFromSource(string source)
    {
        var result = new List<string>();
        foreach (Match match in StringLiteralRegex().Matches(source))
        {
            if (match.Groups["verbatim"].Success)
            {
                result.Add(match.Groups["verbatim"].Value.Replace("\"\"", "\"", StringComparison.Ordinal));
            }
            else
            {
                result.Add(Unescape(match.Groups["regular"].Value));
            }
        }

        return result;
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
            return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] is not '\\' || i + 1 >= value.Length)
            {
                sb.Append(value[i]);
                continue;
            }

            i++;
            switch (value[i])
            {
                case 'n':
                    sb.Append('\n');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case 'v':
                    sb.Append('\v');
                    break;
                case '0':
                    sb.Append('\0');
                    break;
                case 'u' when i + 4 < value.Length && int.TryParse(value.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code):
                    sb.Append((char)code);
                    i += 4;
                    break;
                default:
                    sb.Append(value[i]);
                    break;
            }
        }

        return sb.ToString();
    }

    // A regular string literal that is not interpolated nor a character literal, or a verbatim string literal
    [GeneratedRegex("""(?<![\w$@'])"(?<regular>(?:[^"\\\r\n]|\\.)*)"|(?<![\w$])@"(?<verbatim>(?:[^"]|"")*)"(?!")""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringLiteralRegex();
}
