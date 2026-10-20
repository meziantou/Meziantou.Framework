using System.Globalization;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Scheduling.Fuzz;

public static partial class SchedulingFuzzTargets
{
    private static string[] RecurrenceRuleTokens =>
    [
        "FREQ=", "SECONDLY", "MINUTELY", "HOURLY", "DAILY", "WEEKLY", "MONTHLY", "YEARLY", ";INTERVAL=", ";COUNT=", ";UNTIL=", ";WKST=",
        ";BYSECOND=", ";BYMINUTE=", ";BYHOUR=", ";BYDAY=", ";BYMONTHDAY=", ";BYYEARDAY=", ";BYWEEKNO=", ";BYMONTH=", ";BYSETPOS=",
        ";RSCALE=GREGORIAN", ";SKIP=OMIT", ";X-NAME=", "RRULE:", "MO", "TU", "WE", "TH", "FR", "SA", "SU", "-1", "+1", "-1SU", "+53MO", "53", "-53", "366", "-366",
        "60", "59", "23", "31", "-31", "29", "2", "12", ",", ";", "=", "20240101T000000Z", "20240101", "20240101T000000", "2024-01-01T00:00:00+05:00",
        "99991231T235959Z", "99991231", "00010101T000000", "00010101", "2147483647", "T240000", "T235960",
    ];

    // The cultures the humanizer describes a rule in. Invariant globalization mode may not create the named ones.
    private static readonly (CultureInfo Culture, bool IsSupported)[] HumanizerCultures = GetHumanizerCultures();

    private static (CultureInfo Culture, bool IsSupported)[] GetHumanizerCultures()
    {
        var result = new List<(CultureInfo, bool)> { (CultureInfo.InvariantCulture, true) };
        foreach (var (name, isSupported) in (ReadOnlySpan<(string, bool)>)[("en-US", true), ("fr-FR", true), ("fr-CA", true), ("de-DE", false)])
        {
            try
            {
#pragma warning disable MFGS0001 // The humanizer reads the language from the name of the culture, not from its ICU data
                result.Add((CultureInfo.GetCultureInfo(name), isSupported));
#pragma warning restore MFGS0001
            }
            catch (CultureNotFoundException)
            {
            }
        }

        return [.. result];
    }

    /// <summary>
    /// <c>Parse</c> and every <c>TryParse</c> overload agree; a parsed rule is written as <see cref="RecurrenceRule.Text"/>, which
    /// parses back to a rule with the same parts and the same text, and which only holds ASCII outside the values of the
    /// <c>X-</c> extension parts. The humanizer describes any parsed rule in English and French.
    /// </summary>
    private static void RecurrenceRuleParse(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var parsed = RecurrenceRule.TryParse(text, out var rule, out var error);
        var spanParsed = RecurrenceRule.TryParse(text.AsSpan(), out var spanRule, out var spanError);
        FuzzAssert.Equal(parsed, spanParsed, "TryParse(string) and TryParse(ReadOnlySpan<char>) disagree");
        FuzzAssert.Equal(error, spanError, "TryParse(string) and TryParse(ReadOnlySpan<char>) report different errors");
        FuzzAssert.Equal(parsed, RecurrenceRule.TryParse(text, out _), "TryParse(string) with and without the error disagree");
        FuzzAssert.Equal(parsed, RecurrenceRule.TryParse(text.AsSpan(), out _), "TryParse(ReadOnlySpan<char>) with and without the error disagree");
        if (!parsed)
        {
            FuzzAssert.True(error is not null, "TryParse returned false without an error");
            ExpectFormatException(() => RecurrenceRule.Parse(text), "Parse(string)");
            ExpectFormatException(() => RecurrenceRule.Parse(text.AsSpan()), "Parse(ReadOnlySpan<char>)");
            return;
        }

        FuzzAssert.True(rule is not null && spanRule is not null && error is null, "TryParse returned true with an error or without a rule");
        var ruleText = rule.Text;
        FuzzAssert.Equal(ruleText, rule.ToString(), "ToString() differs from Text");
        FuzzAssert.Equal(ruleText, spanRule.Text, "The rules parsed from a string and from a span differ");
        FuzzAssert.Equal(ruleText, RecurrenceRule.Parse(text).Text, "Parse and TryParse give different rules");
        FuzzAssert.Equal(ruleText, RecurrenceRule.Parse(text.AsSpan()).Text, "Parse(ReadOnlySpan<char>) and TryParse give different rules");

        // The rule parts are ASCII: a name or a value that only a Unicode case mapping turns into a keyword is not one
        CheckRulePartsAreAscii(text, "The input");
        CheckRulePartsAreAscii(ruleText, "Text");

        if (!RecurrenceRule.TryParse(ruleText, out var reparsed, out var reparseError))
            FuzzAssert.Fail($"Text {FuzzAssert.Format(ruleText)} does not parse back: {reparseError}");

        FuzzAssert.Equal(ruleText, reparsed.Text, "Text is not a fixed point");
        FuzzAssert.Equal(rule.GetType(), reparsed.GetType(), "The frequency changes when Text is parsed back");
        FuzzAssert.Equal(rule.Interval, reparsed.Interval, "INTERVAL changes when Text is parsed back");
        FuzzAssert.Equal(rule.Occurrences, reparsed.Occurrences, "COUNT changes when Text is parsed back");
        FuzzAssert.Equal(rule.EndDate, reparsed.EndDate, "UNTIL changes when Text is parsed back");
        FuzzAssert.Equal(rule.EndDate?.Kind, reparsed.EndDate?.Kind, "The kind of UNTIL changes when Text is parsed back");
        FuzzAssert.Equal(rule.IsEndDateDate, reparsed.IsEndDateDate, "A DATE UNTIL changes when Text is parsed back");
        FuzzAssert.Equal(rule.WeekStart, reparsed.WeekStart, "WKST changes when Text is parsed back");
        FuzzAssert.Equal(Describe(rule), Describe(reparsed), "The rule parts change when Text is parsed back");

        foreach (var (culture, isSupported) in HumanizerCultures)
        {
            var humanText = rule.GetHumanText(culture);
            if (isSupported)
            {
                FuzzAssert.True(!string.IsNullOrWhiteSpace(humanText), $"The humanizer has no description in {culture.Name} for {FuzzAssert.Format(ruleText)}");
            }
            else
            {
                FuzzAssert.True(humanText is null, $"The humanizer describes {FuzzAssert.Format(ruleText)} in the unsupported culture {culture.Name}");
            }
        }
    }

    /// <summary>
    /// The first bytes select a start date and a time zone, the rest is the rule. The first occurrences are increasing, after
    /// the start, take its kind and its fractional second, satisfy every rule part and the defaults RFC 5545 takes from the
    /// start, and are aligned on the interval. COUNT and UNTIL keep a prefix of the occurrences of the rule without them, the
    /// rule parsed back from its text has the same occurrences, and the evaluations in a time zone are consistent with the
    /// wall-clock one.
    /// </summary>
    private static void RecurrenceRuleOccurrences(ReadOnlySpan<byte> data)
    {
        var start = ReadStart(ref data, allowLocal: false);
        var timeZone = FuzzInput.Pick(TimeZones, FuzzInput.ReadByte(ref data));
        var text = FuzzText.Decode(data);
        if (!RecurrenceRule.TryParse(text, out var rule))
            return;

        var description = $"{FuzzAssert.Format(rule.Text)} from {Format(start)}";
        var occurrences = rule.GetNextOccurrences(start).Take(MaxOccurrences).ToList();
        CheckRecurrenceRuleOccurrences(rule, start, occurrences, description);

        // The rule parsed back from its text is the same rule
        var reparsed = RecurrenceRule.Parse(rule.Text);
        var reparsedOccurrences = reparsed.GetNextOccurrences(start).Take(MaxOccurrences).ToList();
        FuzzAssert.True(occurrences.SequenceEqual(reparsedOccurrences), $"{description}: the rule parsed back from its text has other occurrences{Environment.NewLine}Expected: {Format(occurrences)}{Environment.NewLine}Actual:   {Format(reparsedOccurrences)}");

        // COUNT and UNTIL keep a prefix of the occurrences
        if (rule.Occurrences is { } count)
        {
            var unbounded = RecurrenceRule.Parse(text);
            unbounded.Occurrences = null;
            var expected = unbounded.GetNextOccurrences(start).Take(Math.Min(count, MaxOccurrences)).ToList();
            FuzzAssert.True(occurrences.SequenceEqual(expected), $"{description}: COUNT does not keep the first occurrences of the rule without it{Environment.NewLine}Expected: {Format(expected)}{Environment.NewLine}Actual:   {Format(occurrences)}");
        }

        if (rule.EndBound is { } bound)
        {
            var unbounded = RecurrenceRule.Parse(text);
            unbounded.EndDate = null;
            var expected = unbounded.GetNextOccurrences(start).Take(MaxOccurrences).TakeWhile(occurrence => occurrence.Ticks <= bound.Ticks).ToList();
            FuzzAssert.True(occurrences.SequenceEqual(expected), $"{description}: UNTIL does not keep the occurrences of the rule without it up to {Format(bound)}{Environment.NewLine}Expected: {Format(expected)}{Environment.NewLine}Actual:   {Format(occurrences)}");
        }

        // The evaluation in a time zone reads the start as a wall-clock time
        var wallClockStart = DateTime.SpecifyKind(start, DateTimeKind.Unspecified);
        var wallClockOccurrences = start.Kind is DateTimeKind.Unspecified ? occurrences : rule.GetNextOccurrences(wallClockStart).Take(MaxOccurrences).ToList();
        var zoned = rule.GetNextOccurrences(start, timeZone).Take(MaxOccurrences).ToList();
        CheckZonedOccurrences(zoned, timeZone, description + " in " + timeZone.Id);
        FuzzAssert.True(rule.Occurrences is null || zoned.Count <= rule.Occurrences, $"{description} in {timeZone.Id}: more occurrences than COUNT");
        if (!timeZone.SupportsDaylightSavingTime && rule.EndDate is not { Kind: DateTimeKind.Utc })
        {
            CheckFixedOffsetOccurrences(zoned, wallClockOccurrences, timeZone.BaseUtcOffset, description + " in " + timeZone.Id);
        }

        if (ReferenceEquals(timeZone, TimeZoneInfo.Utc))
        {
            var expected = wallClockOccurrences.Select(occurrence => new DateTimeOffset(occurrence.Ticks, TimeSpan.Zero)).ToList();
            FuzzAssert.True(zoned.SequenceEqual(expected), $"{description}: the occurrences in UTC differ from the wall-clock occurrences{Environment.NewLine}Expected: {Format(expected)}{Environment.NewLine}Actual:   {Format(zoned)}");
        }

        // The DateTimeOffset overload starts at an instant and returns no occurrence before it
        if (TryCreateInstant(wallClockStart, timeZone, out var instant))
        {
            var fromInstant = rule.GetNextOccurrences(instant, timeZone).Take(MaxOccurrences).ToList();
            CheckZonedOccurrences(fromInstant, timeZone, description + " from the instant " + Format(instant) + " in " + timeZone.Id);
            FuzzAssert.True(fromInstant.All(occurrence => occurrence >= instant), $"{description}: an occurrence precedes the instant {Format(instant)} in {timeZone.Id}: {Format(fromInstant)}");
        }
    }

    private static bool TryCreateInstant(DateTime wallClock, TimeZoneInfo timeZone, out DateTimeOffset instant)
    {
        var offset = timeZone.IsInvalidTime(wallClock) ? timeZone.BaseUtcOffset : timeZone.GetUtcOffset(wallClock);
        var utcTicks = wallClock.Ticks - offset.Ticks;
        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
        {
            instant = default;
            return false;
        }

        instant = new DateTimeOffset(wallClock, offset);
        return true;
    }

    private static void CheckRecurrenceRuleOccurrences(RecurrenceRule rule, DateTime start, List<DateTime> occurrences, string description)
    {
        FuzzAssert.True(rule.Occurrences is null || occurrences.Count <= rule.Occurrences, $"{description}: more occurrences than COUNT");
        var frequency = GetFrequency(rule);
        var bound = rule.EndBound;
        for (var i = 0; i < occurrences.Count; i++)
        {
            var occurrence = occurrences[i];
            var what = $"{description}: the occurrence {Format(occurrence)}";
            FuzzAssert.Equal(start.Kind, occurrence.Kind, $"{what} does not have the kind of the start");
            FuzzAssert.Equal(start.Ticks % TimeSpan.TicksPerSecond, occurrence.Ticks % TimeSpan.TicksPerSecond, $"{what} does not have the fractional second of the start");
            FuzzAssert.True(occurrence >= start, $"{what} precedes the start");
            FuzzAssert.True(i is 0 || occurrences[i - 1] < occurrence, $"{what} does not follow {Format(occurrences[Math.Max(i - 1, 0)])}");
            FuzzAssert.True(bound is null || occurrence.Ticks <= bound.Value.Ticks, $"{what} is after UNTIL");
            CheckRuleParts(rule, frequency, start, occurrence, what);
        }
    }

    /// <summary>Checks that an occurrence satisfies every rule part, and the parts RFC 5545 section 3.3.10 takes from the start when they are missing.</summary>
    private static void CheckRuleParts(RecurrenceRule rule, Frequency frequency, DateTime start, DateTime occurrence, string what)
    {
        var (weekDays, byDays, yearDays, weekNumbers) = GetDayParts(rule);
        var daysInMonth = DateTime.DaysInMonth(occurrence.Year, occurrence.Month);
        var daysInYear = DateTime.IsLeapYear(occurrence.Year) ? 366 : 365;

        FuzzAssert.True(rule.ByMonths.Count is 0 || rule.ByMonths.Contains(occurrence.Month), $"{what} is not in BYMONTH");
        FuzzAssert.True(rule.ByMonthDays.Count is 0 || rule.ByMonthDays.Any(day => day > 0 ? day == occurrence.Day : daysInMonth + 1 + day == occurrence.Day), $"{what} is not in BYMONTHDAY");
        FuzzAssert.True(yearDays is null || yearDays.Count is 0 || yearDays.Any(day => day > 0 ? day == occurrence.DayOfYear : daysInYear + 1 + day == occurrence.DayOfYear), $"{what} is not in BYYEARDAY");
        FuzzAssert.True(weekDays is null || weekDays.Count is 0 || weekDays.Contains(occurrence.DayOfWeek), $"{what} is not in BYDAY");
        FuzzAssert.True(byDays is null || byDays.Count is 0 || byDays.Any(day => MatchesByDay(day, frequency, rule.ByMonths.Count > 0, occurrence)), $"{what} is not in BYDAY");
        FuzzAssert.True(weekNumbers is null || weekNumbers.Count is 0 || MatchesWeekNumber(weekNumbers, rule.WeekStart, occurrence), $"{what} is not in BYWEEKNO");
        FuzzAssert.True(rule.ByHours is null || rule.ByHours.Count is 0 || rule.ByHours.Contains(occurrence.Hour), $"{what} is not in BYHOUR");
        FuzzAssert.True(rule.ByMinutes is null || rule.ByMinutes.Count is 0 || rule.ByMinutes.Contains(occurrence.Minute), $"{what} is not in BYMINUTE");
        FuzzAssert.True(rule.BySeconds is null || rule.BySeconds.Count is 0 || rule.BySeconds.Contains(occurrence.Second), $"{what} is not in BYSECOND");

        // The time parts the rule does not specify are those of the start
        if (frequency >= Frequency.Daily && IsEmpty(rule.ByHours))
        {
            FuzzAssert.Equal(start.Hour, occurrence.Hour, $"{what} does not have the hour of the start");
        }

        if (frequency >= Frequency.Hourly && IsEmpty(rule.ByMinutes))
        {
            FuzzAssert.Equal(start.Minute, occurrence.Minute, $"{what} does not have the minute of the start");
        }

        if (frequency >= Frequency.Minutely && IsEmpty(rule.BySeconds))
        {
            FuzzAssert.Equal(start.Second, occurrence.Second, $"{what} does not have the second of the start");
        }

        // So are the day parts
        var hasDayPart = !IsEmpty(weekDays) || !IsEmpty(byDays) || !IsEmpty(yearDays) || !IsEmpty(weekNumbers) || rule.ByMonthDays.Count > 0;
        switch (frequency)
        {
            case Frequency.Weekly when IsEmpty(weekDays):
                FuzzAssert.Equal(start.DayOfWeek, occurrence.DayOfWeek, $"{what} is not on the day of the week of the start");
                break;

            case Frequency.Monthly when !hasDayPart:
                FuzzAssert.Equal(start.Day, occurrence.Day, $"{what} is not on the day of the month of the start");
                break;

            case Frequency.Yearly when !hasDayPart:
                FuzzAssert.Equal(start.Day, occurrence.Day, $"{what} is not on the day of the month of the start");
                FuzzAssert.True(rule.ByMonths.Count > 0 || start.Month == occurrence.Month, $"{what} is not in the month of the start");
                break;
        }

        // The periods are aligned on the start and the interval
        var interval = rule.Interval;
        var periods = frequency switch
        {
            Frequency.Secondly => (occurrence.Ticks / TimeSpan.TicksPerSecond) - (start.Ticks / TimeSpan.TicksPerSecond),
            Frequency.Minutely => (occurrence.Ticks / TimeSpan.TicksPerMinute) - (start.Ticks / TimeSpan.TicksPerMinute),
            Frequency.Hourly => (occurrence.Ticks / TimeSpan.TicksPerHour) - (start.Ticks / TimeSpan.TicksPerHour),
            Frequency.Daily => (occurrence.Ticks / TimeSpan.TicksPerDay) - (start.Ticks / TimeSpan.TicksPerDay),
            Frequency.Weekly => (GetStartOfWeek(occurrence, rule.WeekStart) - GetStartOfWeek(start, rule.WeekStart)) / 7,
            Frequency.Monthly => ((occurrence.Year * 12L) + occurrence.Month) - ((start.Year * 12L) + start.Month),
            _ when !IsEmpty(weekNumbers) => 0, // A week-numbering year can start in the previous calendar year
            _ => occurrence.Year - start.Year,
        };

        FuzzAssert.True(periods % interval is 0, $"{what} is not in a period aligned on the start every {interval.ToString(CultureInfo.InvariantCulture)} periods");
    }

    private static bool IsEmpty<T>(ICollection<T>? values) => values is null || values.Count is 0;

    private static long GetStartOfWeek(DateTime value, DayOfWeek weekStart)
    {
        var dayNumber = value.Ticks / TimeSpan.TicksPerDay;
        return dayNumber - (((int)value.DayOfWeek - (int)weekStart + 7) % 7);
    }

    private static bool MatchesByDay(ByDay byDay, Frequency frequency, bool hasMonths, DateTime occurrence)
    {
        if (byDay.DayOfWeek != occurrence.DayOfWeek)
            return false;

        if (byDay.Ordinal is not { } ordinal)
            return true;

        // A numbered BYDAY refers to the month in a MONTHLY rule, and to the year in a YEARLY rule unless BYMONTH narrows it
        int index;
        int count;
        if (frequency is Frequency.Monthly || hasMonths)
        {
            index = occurrence.Day;
            count = DateTime.DaysInMonth(occurrence.Year, occurrence.Month);
        }
        else
        {
            index = occurrence.DayOfYear;
            count = DateTime.IsLeapYear(occurrence.Year) ? 366 : 365;
        }

        return ordinal > 0 ? (index - 1) / 7 + 1 == ordinal : (count - index) / 7 + 1 == -ordinal;
    }

    /// <summary>Checks the week number of a date, in its week-numbering year: week 1 is the first week with at least 4 days of the year, weeks starting on <paramref name="weekStart"/>.</summary>
    private static bool MatchesWeekNumber(IList<int> weekNumbers, DayOfWeek weekStart, DateTime occurrence)
    {
        var year = occurrence.Year;
        if (year is <= 1 or >= 9999)
            return true;

        var dayNumber = occurrence.Ticks / TimeSpan.TicksPerDay;
        if (dayNumber < GetStartOfWeekNumberingYear(year, weekStart))
        {
            year--;
        }
        else if (dayNumber >= GetStartOfWeekNumberingYear(year + 1, weekStart))
        {
            year++;
        }

        var firstDay = GetStartOfWeekNumberingYear(year, weekStart);
        var weekCount = (GetStartOfWeekNumberingYear(year + 1, weekStart) - firstDay) / 7;
        var weekNumber = ((dayNumber - firstDay) / 7) + 1;
        return weekNumbers.Any(number => number > 0 ? number == weekNumber : weekCount + 1 + number == weekNumber);

        static long GetStartOfWeekNumberingYear(int year, DayOfWeek weekStart) => GetStartOfWeek(new DateTime(year, 1, 4), weekStart);
    }

    private static Frequency GetFrequency(RecurrenceRule rule) => rule switch
    {
        SecondlyRecurrenceRule => Frequency.Secondly,
        MinutelyRecurrenceRule => Frequency.Minutely,
        HourlyRecurrenceRule => Frequency.Hourly,
        DailyRecurrenceRule => Frequency.Daily,
        WeeklyRecurrenceRule => Frequency.Weekly,
        MonthlyRecurrenceRule => Frequency.Monthly,
        YearlyRecurrenceRule => Frequency.Yearly,
        _ => throw new FuzzAssertionException($"Unexpected rule type {rule.GetType()}"),
    };

    private static (IList<DayOfWeek>? WeekDays, IList<ByDay>? ByDays, IList<int>? YearDays, IList<int>? WeekNumbers) GetDayParts(RecurrenceRule rule) => rule switch
    {
        SecondlyRecurrenceRule value => (value.ByWeekDays, null, value.ByYearDays, null),
        MinutelyRecurrenceRule value => (value.ByWeekDays, null, value.ByYearDays, null),
        HourlyRecurrenceRule value => (value.ByWeekDays, null, value.ByYearDays, null),
        DailyRecurrenceRule value => (value.ByWeekDays, null, null, null),
        WeeklyRecurrenceRule value => (value.ByWeekDays, null, null, null),
        MonthlyRecurrenceRule value => (null, value.ByWeekDays, null, null),
        YearlyRecurrenceRule value => (null, value.ByWeekDays, value.ByYearDays, value.ByWeekNumbers),
        _ => throw new FuzzAssertionException($"Unexpected rule type {rule.GetType()}"),
    };

    /// <summary>Describes the list rule parts of a rule, to compare two rules.</summary>
    private static string Describe(RecurrenceRule rule)
    {
        var (weekDays, byDays, yearDays, weekNumbers) = GetDayParts(rule);
        return string.Join("|", (string[])
        [
            Join(rule.BySeconds), Join(rule.ByMinutes), Join(rule.ByHours), Join(rule.ByMonths), Join(rule.ByMonthDays), Join(rule.BySetPositions),
            Join(yearDays), Join(weekNumbers), weekDays is null ? "" : string.Join(",", weekDays), byDays is null ? "" : string.Join(",", byDays),
        ]);

        static string Join(IEnumerable<int>? values) => values is null ? "" : string.Join(",", values.Select(value => value.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Checks that the rule parts of a rule are ASCII, except the values of the <c>X-</c> extension parts, which are free text.</summary>
    private static void CheckRulePartsAreAscii(string text, string what)
    {
        foreach (var part in text.Split(';'))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            var name = separator < 0 ? part : part[..separator];
            var checkedText = name.StartsWith("X-", StringComparison.OrdinalIgnoreCase) ? name : part;
            FuzzAssert.True(checkedText.All(char.IsAscii), $"{what} of an accepted rule has a rule part that is not ASCII: {FuzzAssert.Format(part)}");
        }
    }

    private static void ExpectFormatException(Action action, string what)
    {
        try
        {
            action();
        }
        catch (FormatException)
        {
            return;
        }

        FuzzAssert.Fail($"{what} does not throw a FormatException for an input TryParse rejects");
    }

    private static IEnumerable<FuzzSeed> GetRecurrenceRuleParseSeeds()
    {
        var index = 0;
        foreach (var rule in GetRecurrenceRuleSeedTexts())
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"rrule-{index++}"), rule);
        }
    }

    private static IEnumerable<FuzzSeed> GetRecurrenceRuleOccurrencesSeeds()
    {
        // The unit tests mostly start on 1997-09-02T09:00, the first notable start, in the time zone selected by the second byte
        var index = 0;
        foreach (var rule in GetRecurrenceRuleSeedTexts())
        {
            if (rule.Contains("FREQ", StringComparison.OrdinalIgnoreCase))
            {
                yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"rrule-{index}"), [0x00, (byte)(index % TimeZones.Length)], rule);
                index++;
            }
        }

        yield return FuzzSeed.FromText("rrule-utc-start", [0x20, 0x00], "FREQ=DAILY;UNTIL=19971224T000000Z");
        yield return FuzzSeed.FromText("rrule-built-start", [0x80, 0x07, 0xE8, 0x02, 0x1C, 0x17, 0x3B, 0x3B, 0x80, 0x01], "FREQ=HOURLY;INTERVAL=7;BYDAY=MO,FR;BYMONTH=2,3");
        yield return FuzzSeed.FromText("rrule-max-start", [0x0F, 0x01], "FREQ=SECONDLY;BYSECOND=59");
        yield return FuzzSeed.FromText("rrule-min-start", [0x09, 0x05], "FREQ=MINUTELY;INTERVAL=13;BYHOUR=0,1");
        yield return FuzzSeed.FromText("rrule-never", [0x00, 0x00], "FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=30");
        yield return FuzzSeed.FromText("rrule-large-interval", [0x00, 0x00], "FREQ=SECONDLY;INTERVAL=2147483647;COUNT=2147483647");
        yield return FuzzSeed.FromText("rrule-weekno", [0x01, 0x02], "FREQ=YEARLY;BYWEEKNO=-1,1,53;BYDAY=MO,SU;WKST=SU");
        yield return FuzzSeed.FromText("rrule-setpos", [0x01, 0x03], "FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1,1;BYHOUR=9,17;BYMINUTE=0,30");
    }

    private static IEnumerable<string> GetRecurrenceRuleSeedTexts()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in (string[])["RecurrenceRuleTests.cs", "RecurrenceRuleTests.RFC2445.cs", "RecurrenceRuleTests.RFC5545.cs", "RecurrenceRuleTests.ToStringEn.cs", "RecurrenceRuleTests.ToStringFr.cs"])
        {
            foreach (var literal in GetStringLiterals(file))
            {
                if (literal.Contains('=', StringComparison.Ordinal) && !literal.Contains(' ', StringComparison.Ordinal))
                {
                    result.Add(literal);
                }
            }
        }

        foreach (var literal in GetStringLiterals("InternetCalendarTests.cs"))
        {
            if (literal.StartsWith("RRULE:", StringComparison.Ordinal) && literal.Length > 6)
            {
                result.Add(literal[6..]);
            }
        }

        return result.Order(StringComparer.Ordinal);
    }
}
