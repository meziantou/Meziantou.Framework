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
    protected static List<(int Hour, int Minute, int? Second)>? GetListedTimes(RuleParts rule)
    {
        if (rule.ByHours.Count is 0 || rule.ByMinutes.Count is 0)
            return null;

        if ((long)rule.ByHours.Count * rule.ByMinutes.Count * Math.Max(1, rule.BySeconds.Count) > MaxListedTimes)
            return null;

        var result = new List<(int Hour, int Minute, int? Second)>();
        foreach (var hour in rule.ByHours)
        {
            foreach (var minute in rule.ByMinutes)
            {
                if (rule.BySeconds.Count is 0)
                {
                    result.Add((hour, minute, null));
                }
                else
                {
                    foreach (var second in rule.BySeconds)
                    {
                        result.Add((hour, minute, second));
                    }
                }
            }
        }

        return result;
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
        private RuleParts(Frequency frequency, RecurrenceRule rrule, IEnumerable<ByDay>? byDays, IList<int>? byMonthDays, IList<int>? byMonths, IList<int>? byYearDays)
        {
            Frequency = frequency;
            Interval = rrule.Interval;
            Occurrences = rrule.Occurrences;
            EndDate = rrule.EndDate;
            ByDays = byDays is null ? [] : [.. byDays];
            ByMonthDays = byMonthDays ?? [];
            ByMonths = byMonths ?? [];
            ByYearDays = byYearDays ?? [];
            BySetPositions = rrule.BySetPositions ?? [];
            ByHours = rrule.ByHours ?? [];
            ByMinutes = rrule.ByMinutes ?? [];
            BySeconds = rrule.BySeconds ?? [];
        }

        public Frequency Frequency { get; }

        public int Interval { get; }

        public int? Occurrences { get; }

        public DateTime? EndDate { get; }

        public IList<ByDay> ByDays { get; }

        public IList<int> ByMonthDays { get; }

        public IList<int> ByMonths { get; }

        public IList<int> ByYearDays { get; }

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
                SecondlyRecurrenceRule rule => new RuleParts(Frequency.Secondly, rrule, ToByDays(rule.ByWeekDays), rrule.ByMonthDays, rrule.ByMonths, byYearDays: null),
                MinutelyRecurrenceRule rule => new RuleParts(Frequency.Minutely, rrule, ToByDays(rule.ByWeekDays), rrule.ByMonthDays, rrule.ByMonths, byYearDays: null),
                HourlyRecurrenceRule rule => new RuleParts(Frequency.Hourly, rrule, ToByDays(rule.ByWeekDays), rrule.ByMonthDays, rrule.ByMonths, byYearDays: null),
                DailyRecurrenceRule rule => new RuleParts(Frequency.Daily, rrule, ToByDays(rule.ByWeekDays), rrule.ByMonthDays, rrule.ByMonths, byYearDays: null),
                WeeklyRecurrenceRule rule => new RuleParts(Frequency.Weekly, rrule, ToByDays(rule.ByWeekDays), rrule.ByMonthDays, rrule.ByMonths, byYearDays: null),
                MonthlyRecurrenceRule rule => new RuleParts(Frequency.Monthly, rrule, rule.ByWeekDays, rrule.ByMonthDays, rrule.ByMonths, byYearDays: null),
                YearlyRecurrenceRule rule => new RuleParts(Frequency.Yearly, rrule, rule.ByWeekDays, rule.ByMonthDays, rule.ByMonths, rule.ByYearDays),
                _ => null,
            };
        }

        private static IEnumerable<ByDay>? ToByDays(IList<DayOfWeek>? daysOfWeek)
        {
            return daysOfWeek?.Select(day => new ByDay(day));
        }
    }
}
