namespace Meziantou.Framework.Scheduling;

/// <summary>Provides functionality to convert recurrence rules to human-readable text.</summary>
public abstract class RecurrenceRuleHumanizer
{
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
            if (rrule is DailyRecurrenceRule dailyRecurrenceRule)
                return humanizer.GetText(dailyRecurrenceRule, cultureInfo);

            if (rrule is WeeklyRecurrenceRule weeklyRecurrenceRule)
                return humanizer.GetText(weeklyRecurrenceRule, cultureInfo);

            if (rrule is MonthlyRecurrenceRule monthlyRecurrenceRule)
                return humanizer.GetText(monthlyRecurrenceRule, cultureInfo);

            if (rrule is YearlyRecurrenceRule yearlyRecurrenceRule)
                return humanizer.GetText(yearlyRecurrenceRule, cultureInfo);
        }

        return null;
    }

    /// <summary>Converts a daily recurrence rule to human-readable text.</summary>
    /// <param name="rrule">The daily recurrence rule.</param>
    /// <param name="cultureInfo">The culture to use for formatting.</param>
    /// <returns>A human-readable string representation.</returns>
    protected abstract string GetText(DailyRecurrenceRule rrule, CultureInfo cultureInfo);

    /// <summary>Converts a weekly recurrence rule to human-readable text.</summary>
    /// <param name="rrule">The weekly recurrence rule.</param>
    /// <param name="cultureInfo">The culture to use for formatting.</param>
    /// <returns>A human-readable string representation.</returns>
    protected abstract string GetText(WeeklyRecurrenceRule rrule, CultureInfo cultureInfo);

    /// <summary>Converts a monthly recurrence rule to human-readable text.</summary>
    /// <param name="rrule">The monthly recurrence rule.</param>
    /// <param name="cultureInfo">The culture to use for formatting.</param>
    /// <returns>A human-readable string representation.</returns>
    protected abstract string GetText(MonthlyRecurrenceRule rrule, CultureInfo cultureInfo);

    /// <summary>Converts a yearly recurrence rule to human-readable text.</summary>
    /// <param name="rrule">The yearly recurrence rule.</param>
    /// <param name="cultureInfo">The culture to use for formatting.</param>
    /// <returns>A human-readable string representation.</returns>
    protected abstract string GetText(YearlyRecurrenceRule rrule, CultureInfo cultureInfo);

    protected static void ListToHumanText<T>(StringBuilder sb, CultureInfo cultureInfo, IList<T> list, string separator, string lastSeparator)
    {
        if (list is null)
            return;

        for (var i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                if (i < list.Count - 1)
                {
                    sb.Append(separator);
                }
                else
                {
                    sb.Append(lastSeparator);
                }
            }

            sb.AppendFormat(cultureInfo, "{0}", list[i]);
        }
    }

    protected static string ListToHumanText<T>(CultureInfo cultureInfo, IList<T> list, string separator, string lastSeparator)
    {
        var sb = new StringBuilder();
        ListToHumanText(sb, cultureInfo, list, separator, lastSeparator);
        return sb.ToString();
    }

    protected static bool IsWeekday(ICollection<DayOfWeek> daysOfWeek)
    {
        return daysOfWeek.Count == 5 &&
               daysOfWeek.Contains(DayOfWeek.Monday) &&
               daysOfWeek.Contains(DayOfWeek.Tuesday) &&
               daysOfWeek.Contains(DayOfWeek.Wednesday) &&
               daysOfWeek.Contains(DayOfWeek.Thursday) &&
               daysOfWeek.Contains(DayOfWeek.Friday);
    }

    protected static bool IsWeekendDay(ICollection<DayOfWeek> daysOfWeek)
    {
        return daysOfWeek.Count == 2 &&
               daysOfWeek.Contains(DayOfWeek.Sunday) &&
               daysOfWeek.Contains(DayOfWeek.Saturday);
    }

    protected static bool IsFullWeek(ICollection<DayOfWeek> daysOfWeek)
    {
        return daysOfWeek.Count == 7 &&
               daysOfWeek.Contains(DayOfWeek.Monday) &&
               daysOfWeek.Contains(DayOfWeek.Tuesday) &&
               daysOfWeek.Contains(DayOfWeek.Wednesday) &&
               daysOfWeek.Contains(DayOfWeek.Thursday) &&
               daysOfWeek.Contains(DayOfWeek.Friday) &&
               daysOfWeek.Contains(DayOfWeek.Saturday) &&
               daysOfWeek.Contains(DayOfWeek.Sunday);
    }
}
