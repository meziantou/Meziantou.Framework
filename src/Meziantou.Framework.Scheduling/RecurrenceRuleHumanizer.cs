namespace Meziantou.Framework.Scheduling;

/// <summary>Provides functionality to convert recurrence rules to human-readable text.</summary>
internal abstract class RecurrenceRuleHumanizer
{
    /// <summary>The maximum number of times of day listed explicitly before falling back to a per-component description.</summary>
    private const int MaxListedTimes = 10;

    /// <summary>The English culture information.</summary>
    protected static readonly CultureInfo EnglishCultureInfo = GetCulture("en");

    /// <summary>The French culture information.</summary>
    protected static readonly CultureInfo FrenchCultureInfo = GetCulture("fr");

    /// <summary>Gets the supported humanizers by culture.</summary>
    public static IDictionary<CultureInfo, RecurrenceRuleHumanizer> SupportedHumanizers { get; }

    static RecurrenceRuleHumanizer()
    {
        SupportedHumanizers = new Dictionary<CultureInfo, RecurrenceRuleHumanizer>
        {
            { CultureInfo.InvariantCulture, new RecurrenceRuleHumanizerEnglish() },
        };

        SupportedHumanizers.TryAdd(EnglishCultureInfo, new RecurrenceRuleHumanizerEnglish());
        SupportedHumanizers.TryAdd(FrenchCultureInfo, new RecurrenceRuleHumanizerFrench());
    }

    private static CultureInfo GetCulture(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch
        {
            return CultureInfo.InvariantCulture;
        }
    }

    /// <summary>Converts a recurrence rule to human-readable text using the current UI culture.</summary>
    /// <param name="rrule">The recurrence rule to convert.</param>
    /// <returns>A human-readable string representation of the recurrence rule.</returns>
    public static string? GetText(RecurrenceRule rrule)
    {
        return GetText(rrule, cultureInfo: null);
    }

    /// <summary>Converts a recurrence rule to human-readable text using the specified culture.</summary>
    /// <param name="rrule">The recurrence rule to convert.</param>
    /// <param name="cultureInfo">The culture to use for formatting, or <see langword="null"/> to use the current UI culture.</param>
    /// <returns>A human-readable string representation of the recurrence rule.</returns>
    public static string? GetText(RecurrenceRule rrule, CultureInfo? cultureInfo)
    {
        ArgumentNullException.ThrowIfNull(rrule);

        cultureInfo ??= CultureInfo.CurrentUICulture;

        if (!SupportedHumanizers.TryGetValue(cultureInfo, out var humanizer))
        {
            if (!cultureInfo.IsNeutralCulture)
            {
                return GetText(rrule, cultureInfo.Parent);
            }
        }

        if (humanizer is not null)
        {
            var parts = RuleParts.Create(rrule);
            if (parts is not null)
                return humanizer.GetText(parts);
        }

        return null;
    }

    /// <summary>Converts the parts of a recurrence rule to human-readable text.</summary>
    /// <param name="rule">The parts of the recurrence rule.</param>
    /// <returns>A human-readable string representation.</returns>
    protected abstract string GetText(RuleParts rule);

    protected static string JoinList(IList<string> items, string separator, string lastSeparator)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(i < items.Count - 1 ? separator : lastSeparator);
            }

            sb.Append(items[i]);
        }

        return sb.ToString();
    }

    protected static string ToInvariantString(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    protected static string ToTwoDigitString(int value)
    {
        return value.ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>Gets the times of day described by BYHOUR, BYMINUTE and BYSECOND when they are fully specified and few enough to be listed.</summary>
    protected static List<(int Hour, int Minute, int? Second)>? GetListedTimes(IList<int> hours, IList<int> minutes, IList<int> seconds)
    {
        if (hours.Count is 0 || minutes.Count is 0)
            return null;

        if ((long)hours.Count * minutes.Count * Math.Max(1, seconds.Count) > MaxListedTimes)
            return null;

        var result = new List<(int Hour, int Minute, int? Second)>();
        foreach (var hour in hours)
        {
            foreach (var minute in minutes)
            {
                if (seconds.Count is 0)
                {
                    result.Add((hour, minute, null));
                }
                else
                {
                    foreach (var second in seconds)
                    {
                        result.Add((hour, minute, second));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Gets the BYSETPOS values as positions among the days of a month or a year, such as "the last weekday", when the rule can be described that way.</summary>
    /// <remarks>
    /// <para>BYSETPOS selects among every instance of a period: its days, each expanded by every time of day. The positions can
    /// only be described as day positions when the period is the one the days are counted in (a month, or a year that is not
    /// split across several months), and when every position selects the same time of day. That time of day is returned too.</para>
    /// </remarks>
    /// <returns>The day positions, or <see langword="null"/> when BYSETPOS must be described as positions among the occurrences.</returns>
    protected static DaySetPositions? GetDaySetPositions(RuleParts rule)
    {
        if (rule.BySetPositions.Count is 0 || rule.ByDays.Count is 0 || rule.HasOrdinalDays)
            return null;

        if (rule.ByMonthDays.Count > 0 || rule.ByYearDays.Count > 0 || rule.ByWeekNumbers.Count > 0)
            return null;

        // In a YEARLY rule the set is the whole year, so "the last weekday of January and February" would be wrong
        if (rule.Frequency is not (Frequency.Monthly or Frequency.Yearly) || (rule.Frequency is Frequency.Yearly && rule.ByMonths.Count > 1))
            return null;

        var minuteCount = Math.Max(1, rule.ByMinutes.Count);
        var secondCount = Math.Max(1, rule.BySeconds.Count);
        var timesPerDay = (long)Math.Max(1, rule.ByHours.Count) * minuteCount * secondCount;

        long? timeIndex = null;
        var dayPositions = new List<int>();
        foreach (var position in rule.BySetPositions)
        {
            if (position is 0)
                continue;

            long dayPosition;
            long index;
            if (position > 0)
            {
                dayPosition = ((position - 1L) / timesPerDay) + 1;
                index = (position - 1L) % timesPerDay;
            }
            else
            {
                var fromEnd = -(long)position - 1;
                dayPosition = -((fromEnd / timesPerDay) + 1);
                index = timesPerDay - 1 - (fromEnd % timesPerDay);
            }

            if (timeIndex is not null && timeIndex != index)
                return null;

            timeIndex = index;
            if (!dayPositions.Contains((int)dayPosition))
            {
                dayPositions.Add((int)dayPosition);
            }
        }

        if (timeIndex is not { } selectedTime)
            return null;

        return new DaySetPositions(
            dayPositions,
            SelectTime(rule.ByHours, selectedTime / ((long)minuteCount * secondCount)),
            SelectTime(rule.ByMinutes, selectedTime / secondCount % minuteCount),
            SelectTime(rule.BySeconds, selectedTime % secondCount));

        static IList<int> SelectTime(IList<int> values, long index) => values.Count is 0 ? values : [values[(int)index]];
    }

    /// <summary>Gets a value indicating whether the week start changes the occurrences in a way the rest of the text does not already convey.</summary>
    /// <remarks>
    /// <para>The week start numbers the weeks of BYWEEKNO. In a WEEKLY rule, it also decides which of the listed days share a
    /// week, which matters when weeks are skipped or when BYSETPOS selects among the days of a week. Two week starts that
    /// begin the week with the same listed day group the days identically, so a week start is only reported when it groups
    /// them differently from the default one.</para>
    /// </remarks>
    protected static bool IsWeekStartSignificant(RuleParts rule)
    {
        if (rule.WeekStart == RecurrenceRule.DefaultFirstDayOfWeek)
            return false;

        if (rule.ByWeekNumbers.Count > 0)
            return true;

        if (rule.Frequency is not Frequency.Weekly || rule.ByDays.Count < 2 || (rule.Interval is 1 && rule.BySetPositions.Count is 0))
            return false;

        var daysOfWeek = rule.DaysOfWeek;
        return GetFirstDayOfWeek(daysOfWeek, rule.WeekStart) != GetFirstDayOfWeek(daysOfWeek, RecurrenceRule.DefaultFirstDayOfWeek);

        static DayOfWeek GetFirstDayOfWeek(List<DayOfWeek> daysOfWeek, DayOfWeek weekStart)
        {
            return daysOfWeek.OrderBy(day => (day - weekStart + 7) % 7).First();
        }
    }

    /// <summary>Sorts values whose order is meaningless and removes the duplicates.</summary>
    private static List<int> SortValues(IEnumerable<int>? values)
    {
        return values is null ? [] : [.. values.Distinct().OrderBy(value => value)];
    }

    /// <summary>Sorts positions in chronological order (1, 2, …, -2, -1) and removes the duplicates.</summary>
    private static List<int> SortPositions(IEnumerable<int>? values)
    {
        return values is null ? [] : [.. values.Distinct().OrderBy(value => value < 0).ThenBy(value => value)];
    }

    /// <summary>The positions among the days of a period selected by BYSETPOS, with the time of day they select.</summary>
    protected sealed class DaySetPositions
    {
        public DaySetPositions(IList<int> dayPositions, IList<int> hours, IList<int> minutes, IList<int> seconds)
        {
            DayPositions = dayPositions;
            Hours = hours;
            Minutes = minutes;
            Seconds = seconds;
        }

        public IList<int> DayPositions { get; }

        public IList<int> Hours { get; }

        public IList<int> Minutes { get; }

        public IList<int> Seconds { get; }
    }

    protected static bool IsWeekday(ICollection<DayOfWeek> daysOfWeek)
    {
        return daysOfWeek.Count is 5 &&
               daysOfWeek.Contains(DayOfWeek.Monday) &&
               daysOfWeek.Contains(DayOfWeek.Tuesday) &&
               daysOfWeek.Contains(DayOfWeek.Wednesday) &&
               daysOfWeek.Contains(DayOfWeek.Thursday) &&
               daysOfWeek.Contains(DayOfWeek.Friday);
    }

    protected static bool IsWeekendDay(ICollection<DayOfWeek> daysOfWeek)
    {
        return daysOfWeek.Count is 2 &&
               daysOfWeek.Contains(DayOfWeek.Sunday) &&
               daysOfWeek.Contains(DayOfWeek.Saturday);
    }

    protected static bool IsFullWeek(ICollection<DayOfWeek> daysOfWeek)
    {
        return daysOfWeek.Count is 7 &&
               daysOfWeek.Contains(DayOfWeek.Monday) &&
               daysOfWeek.Contains(DayOfWeek.Tuesday) &&
               daysOfWeek.Contains(DayOfWeek.Wednesday) &&
               daysOfWeek.Contains(DayOfWeek.Thursday) &&
               daysOfWeek.Contains(DayOfWeek.Friday) &&
               daysOfWeek.Contains(DayOfWeek.Saturday) &&
               daysOfWeek.Contains(DayOfWeek.Sunday);
    }

    /// <summary>A frequency-independent view of the parts of a recurrence rule.</summary>
    protected sealed class RuleParts
    {
        private RuleParts(Frequency frequency, RecurrenceRule rrule, IEnumerable<ByDay>? byDays, IList<int>? byYearDays, IList<int>? byWeekNumbers)
        {
            Frequency = frequency;
            Interval = rrule.Interval;
            Occurrences = rrule.Occurrences;
            EndDate = rrule.EndDate;
            IsEndDateDate = rrule.IsEndDateDate;
            WeekStart = rrule.WeekStart;
            ByDays = byDays is null ? [] : [.. byDays.Distinct()];
            ByMonthDays = SortPositions(rrule.ByMonthDays);
            ByMonths = SortValues(rrule.ByMonths);
            ByYearDays = SortPositions(byYearDays);
            ByWeekNumbers = SortPositions(byWeekNumbers);
            BySetPositions = SortPositions(rrule.BySetPositions);
            ByHours = SortValues(rrule.ByHours);
            ByMinutes = SortValues(rrule.ByMinutes);
            BySeconds = SortValues(rrule.BySeconds);
        }

        public Frequency Frequency { get; }

        public int Interval { get; }

        public int? Occurrences { get; }

        public DateTime? EndDate { get; }

        /// <summary>Gets a value indicating whether <see cref="EndDate"/> is a DATE, which has no time of day.</summary>
        public bool IsEndDateDate { get; }

        public DayOfWeek WeekStart { get; }

        public IList<ByDay> ByDays { get; }

        public IList<int> ByMonthDays { get; }

        public IList<int> ByMonths { get; }

        public IList<int> ByYearDays { get; }

        public IList<int> ByWeekNumbers { get; }

        public IList<int> BySetPositions { get; }

        public IList<int> ByHours { get; }

        public IList<int> ByMinutes { get; }

        public IList<int> BySeconds { get; }

        public bool HasOrdinalDays => ByDays.Any(day => day.Ordinal.HasValue);

        public List<DayOfWeek> DaysOfWeek => [.. ByDays.Select(day => day.DayOfWeek)];

        public static RuleParts? Create(RecurrenceRule rrule)
        {
            return rrule switch
            {
                SecondlyRecurrenceRule rule => new RuleParts(Frequency.Secondly, rrule, ToByDays(rule.ByWeekDays), rule.ByYearDays, byWeekNumbers: null),
                MinutelyRecurrenceRule rule => new RuleParts(Frequency.Minutely, rrule, ToByDays(rule.ByWeekDays), rule.ByYearDays, byWeekNumbers: null),
                HourlyRecurrenceRule rule => new RuleParts(Frequency.Hourly, rrule, ToByDays(rule.ByWeekDays), rule.ByYearDays, byWeekNumbers: null),
                DailyRecurrenceRule rule => new RuleParts(Frequency.Daily, rrule, ToByDays(rule.ByWeekDays), byYearDays: null, byWeekNumbers: null),
                WeeklyRecurrenceRule rule => new RuleParts(Frequency.Weekly, rrule, ToByDays(rule.ByWeekDays), byYearDays: null, byWeekNumbers: null),
                MonthlyRecurrenceRule rule => new RuleParts(Frequency.Monthly, rrule, rule.ByWeekDays, byYearDays: null, byWeekNumbers: null),
                YearlyRecurrenceRule rule => new RuleParts(Frequency.Yearly, rrule, rule.ByWeekDays, rule.ByYearDays, rule.ByWeekNumbers),
                _ => null,
            };
        }

        private static IEnumerable<ByDay>? ToByDays(IList<DayOfWeek>? daysOfWeek)
        {
            return daysOfWeek?.Select(day => new ByDay(day));
        }
    }
}
