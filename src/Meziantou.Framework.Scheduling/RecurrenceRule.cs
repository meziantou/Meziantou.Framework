namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a recurrence rule as defined in RFC 5545 for recurring events.</summary>
/// <example>
/// <code>
/// var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20000131T140000Z;BYMONTH=1");
/// var nextOccurrences = rrule.GetNextOccurrences(DateTime.Now).Take(50).ToArray();
/// </code>
/// </example>
/// <remarks>
/// <para>Supports the following RFC 5545 recurrence rule properties:</para>
/// <list type="bullet">
/// <item><description>FREQ (Secondly, Minutely, Hourly, Daily, Weekly, Monthly, Yearly)</description></item>
/// <item><description>INTERVAL - The interval between occurrences</description></item>
/// <item><description>COUNT - Maximum number of occurrences</description></item>
/// <item><description>UNTIL - End date for the recurrence</description></item>
/// <item><description>WKST - The day on which the workweek starts</description></item>
/// <item><description>BYSECOND - Limits occurrences to specific seconds (0-60)</description></item>
/// <item><description>BYMINUTE - Limits occurrences to specific minutes (0-59)</description></item>
/// <item><description>BYHOUR - Limits occurrences to specific hours (0-23)</description></item>
/// <item><description>BYDAY - Limits occurrences to specific days of the week</description></item>
/// <item><description>BYMONTHDAY - Limits occurrences to specific days of the month (1-31, -1 to -31)</description></item>
/// <item><description>BYYEARDAY - Limits occurrences to specific days of the year (1-366, -1 to -366)</description></item>
/// <item><description>BYMONTH - Limits occurrences to specific months (1-12)</description></item>
/// <item><description>BYSETPOS - Limits occurrences to specific positions in the recurrence set</description></item>
/// </list>
/// </remarks>
public abstract class RecurrenceRule : IRecurrenceRule
{
    /// <summary>The default first day of the week (Monday).</summary>
    public static readonly DayOfWeek DefaultFirstDayOfWeek = DayOfWeek.Monday;

    /// <summary>The string representation of the default first day of the week.</summary>
    public const string DefaultFirstDayOfWeekString = "MO";

    /// <summary>End date (inclusive)</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>The number of occurrences before the recurrence ends.</summary>
    public int? Occurrences { get; set; }

    /// <summary>The interval between occurrences.</summary>
    public int Interval { get; set; } = 1;

    /// <summary>The first day of the week for the recurrence rule.</summary>
    public DayOfWeek WeekStart { get; set; } = DefaultFirstDayOfWeek;

    /// <summary>Limits occurrences to specific seconds (0-60, where 60 represents leap seconds).</summary>
    public IList<int>? BySeconds { get; set; }

    /// <summary>Limits occurrences to specific minutes (0-59).</summary>
    public IList<int>? ByMinutes { get; set; }

    /// <summary>Limits occurrences to specific hours (0-23).</summary>
    public IList<int>? ByHours { get; set; }

    /// <summary>Limits occurrences to specific months (1-12).</summary>
    public IList<Month> ByMonths { get; set; } = [];

    /// <summary>Limits occurrences to specific days of the month (1-31, -1 to -31).</summary>
    public IList<int> ByMonthDays { get; set; } = [];

    /// <summary>Limits occurrences to specific positions in the recurrence set.</summary>
    public IList<int>? BySetPositions { get; set; }

    /// <summary>Gets a value indicating whether the recurrence rule has an end condition.</summary>
    public bool IsForever => Occurrences.HasValue || EndDate.HasValue;

    /// <summary>Parses a recurrence rule string according to RFC 5545 format.</summary>
    /// <param name="rrule">The recurrence rule string to parse.</param>
    /// <returns>A <see cref="RecurrenceRule"/> instance representing the parsed rule.</returns>
    /// <exception cref="FormatException">Thrown when the recurrence rule format is invalid.</exception>
    public static RecurrenceRule Parse(string rrule)
    {
        if (!TryParse(rrule, out var recurrenceRule, out var error))
            throw new FormatException($"RRule value '{rrule}' is invalid: " + error);

        return recurrenceRule;
    }

    /// <summary>Parses a recurrence rule string according to RFC 5545 format.</summary>
    /// <param name="rrule">The recurrence rule string to parse.</param>
    /// <returns>A <see cref="RecurrenceRule"/> instance representing the parsed rule.</returns>
    /// <exception cref="FormatException">Thrown when the recurrence rule format is invalid.</exception>
    public static RecurrenceRule Parse(ReadOnlySpan<char> rrule)
    {
        if (!TryParse(rrule, out var recurrenceRule, out var error))
            throw new FormatException($"RRule value '{rrule}' is invalid: " + error);

        return recurrenceRule;
    }

    /// <summary>Attempts to parse a recurrence rule string.</summary>
    /// <param name="rrule">The recurrence rule string to parse.</param>
    /// <param name="recurrenceRule">When successful, contains the parsed recurrence rule.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> rrule, [NotNullWhen(returnValue: true)] out RecurrenceRule? recurrenceRule)
    {
        return TryParse(rrule, out recurrenceRule, out _);
    }

    /// <summary>Attempts to parse a recurrence rule string.</summary>
    /// <param name="rrule">The recurrence rule string to parse.</param>
    /// <param name="recurrenceRule">When successful, contains the parsed recurrence rule.</param>
    /// <param name="error">When parsing fails, contains the error message.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> rrule, [NotNullWhen(returnValue: true)] out RecurrenceRule? recurrenceRule, out string? error)
    {
        recurrenceRule = null;
        error = null;
        if (rrule.IsEmpty)
            return false;

        try
        {
            // Extract parts
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var remaining = rrule;
            while (!remaining.IsEmpty)
            {
                var semicolonIndex = remaining.IndexOf(';');
                var part = semicolonIndex >= 0 ? remaining[..semicolonIndex] : remaining;
                remaining = semicolonIndex >= 0 ? remaining[(semicolonIndex + 1)..] : [];

                if (part.IsEmpty)
                    continue;

                var (name, value) = SplitPart(part);
                if (values.ContainsKey(name))
                {
                    error = $"Duplicate name: '{name}'.";
                    return false;
                }

                if (name.Equals("UNTIL", StringComparison.OrdinalIgnoreCase) && values.ContainsKey("COUNT"))
                {
                    error = "Cannot set UNTIL and COUNT in the same recurrence rule.";
                    return false;
                }

                if (name.Equals("COUNT", StringComparison.OrdinalIgnoreCase) && values.ContainsKey("UNTIL"))
                {
                    error = "Cannot set UNTIL and COUNT in the same recurrence rule.";
                    return false;
                }

                values.Add(name, value);
            }

            // Set specific properties
            var frequency = values.GetValue("FREQ", Frequency.None);
            switch (frequency)
            {
                case Frequency.Secondly:
                    var secondlyRecurrenceRule = new SecondlyRecurrenceRule
                    {
                        ByWeekDays = ParseByDay(values),
                        ByHours = ParseByHours(values) ?? [],
                        ByMinutes = ParseByMinutes(values) ?? [],
                    };
                    recurrenceRule = secondlyRecurrenceRule;
                    break;
                case Frequency.Minutely:
                    var minutelyRecurrenceRule = new MinutelyRecurrenceRule
                    {
                        ByWeekDays = ParseByDay(values),
                        ByHours = ParseByHours(values) ?? [],
                        BySeconds = ParseBySeconds(values) ?? [],
                    };
                    recurrenceRule = minutelyRecurrenceRule;
                    break;
                case Frequency.Hourly:
                    var hourlyRecurrenceRule = new HourlyRecurrenceRule
                    {
                        ByWeekDays = ParseByDay(values),
                        ByMinutes = ParseByMinutes(values) ?? [],
                        BySeconds = ParseBySeconds(values) ?? [],
                    };
                    recurrenceRule = hourlyRecurrenceRule;
                    break;
                case Frequency.Daily:
                    var dailyRecurrenceRule = new DailyRecurrenceRule
                    {
                        ByWeekDays = ParseByDay(values),
                    };
                    recurrenceRule = dailyRecurrenceRule;
                    break;
                case Frequency.Weekly:
                    var weeklyRecurrence = new WeeklyRecurrenceRule
                    {
                        ByWeekDays = ParseByDay(values),
                    };
                    recurrenceRule = weeklyRecurrence;
                    break;
                case Frequency.Monthly:
                    var monthlyRecurrence = new MonthlyRecurrenceRule
                    {
                        ByWeekDays = ParseByDayWithOffset(values),
                    };
                    recurrenceRule = monthlyRecurrence;
                    break;
                case Frequency.Yearly:
                    var yearlyRecurrence = new YearlyRecurrenceRule
                    {
                        ByWeekDays = ParseByDayWithOffset(values),
                        ByMonthDays = ParseByMonthDays(values),
                        BySetPositions = ParseBySetPos(values),
                        ByMonths = ParseByMonth(values),
                        ByYearDays = ParseByYearDay(values),
                    };
                    //yearlyRecurrence.ByWeekNo = ParseByWeekNo(values);
                    recurrenceRule = yearlyRecurrence;
                    break;
                default:
                    error = "Unknown Frequency (FREQ).";
                    return false;
            }

            // Set general properties
            // Set Interval
            recurrenceRule.Interval = values.GetValue("INTERVAL", 1);
            recurrenceRule.Occurrences = values.GetValue("COUNT", null);
            if (values.TryGetNonEmptyValue("UNTIL", out var until))
            {
                recurrenceRule.EndDate = Utilities.ParseDateTime(until);
            }

            recurrenceRule.BySetPositions = ParseBySetPos(values);
            recurrenceRule.WeekStart = ParseWeekStart(values);
            recurrenceRule.BySeconds = ParseBySeconds(values);
            recurrenceRule.ByMinutes = ParseByMinutes(values);
            recurrenceRule.ByHours = ParseByHours(values);
            recurrenceRule.ByMonths = ParseByMonth(values);
            recurrenceRule.ByMonthDays = ParseByMonthDays(values);

            return true;
        }
        catch (FormatException e)
        {
            error = e.Message;
            return false;
        }
    }

    /// <summary>Attempts to parse a recurrence rule string.</summary>
    /// <param name="rrule">The recurrence rule string to parse.</param>
    /// <param name="recurrenceRule">When successful, contains the parsed recurrence rule.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse([NotNullWhen(returnValue: true)] string? rrule, [NotNullWhen(returnValue: true)] out RecurrenceRule? recurrenceRule)
    {
        return TryParse(rrule, out recurrenceRule, out _);
    }

    /// <summary>Attempts to parse a recurrence rule string.</summary>
    /// <param name="rrule">The recurrence rule string to parse.</param>
    /// <param name="recurrenceRule">When successful, contains the parsed recurrence rule.</param>
    /// <param name="error">When parsing fails, contains the error message.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse([NotNullWhen(returnValue: true)] string? rrule, [NotNullWhen(returnValue: true)] out RecurrenceRule? recurrenceRule, out string? error)
    {
        recurrenceRule = null;
        error = null;
        if (rrule is null)
            return false;

        return TryParse(rrule.AsSpan(), out recurrenceRule, out error);
    }

    private static (string Name, string Value) SplitPart(ReadOnlySpan<char> str)
    {
        var index = str.IndexOf('=');
        if (index < 0)
            throw new FormatException($"'{str}' is invalid.");

        var name = str[..index];
        if (name.IsEmpty)
            throw new FormatException($"'{str}' is invalid.");

        var value = str[(index + 1)..];
        return new(name.ToString(), value.ToString());
    }

    private static List<int>? ParseBySetPos(Dictionary<string, string> values)
    {
        if (values.TryGetNonEmptyValue("BYSETPOS", out var str))
            return ParseBySetPos(str.AsSpan());

        return null;
    }

    private static List<int>? ParseBySetPos(ReadOnlySpan<char> str)
    {
        if (str.IsEmpty)
            return null;

        return SplitToInt32List(str);
    }

    private static List<int> ParseByMonthDays(Dictionary<string, string> values)
    {
        if (values.TryGetNonEmptyValue("BYMONTHDAY", out var str))
            return ParseByMonthDays(str.AsSpan());

        return [];
    }

    private static List<int> ParseByMonthDays(ReadOnlySpan<char> str)
    {
        var monthDays = SplitToInt32List(str);
        foreach (var monthDay in monthDays)
        {
            if (monthDay is (>= 1 and <= 31) or (<= -1 and >= -31))
                continue;

            throw new FormatException($"Monthday '{monthDay.ToString(CultureInfo.InvariantCulture)}' is invalid.");
        }

        return monthDays;
    }

    private static List<Month> ParseByMonth(Dictionary<string, string> values)
    {
        if (values.TryGetNonEmptyValue("BYMONTH", out var str))
            return ParseByMonth(str.AsSpan());

        return [];
    }

    private static List<Month> ParseByMonth(ReadOnlySpan<char> str)
    {
        var months = SplitToMonthList(str);
        foreach (var month in months)
        {
            if (!Enum.IsDefined(month))
                throw new FormatException($"BYMONTH value '{month}' is invalid.");
        }

        return months;
    }

    private static List<int> ParseByYearDay(Dictionary<string, string> values)
    {
        if (values.TryGetNonEmptyValue("BYYEARDAY", out var str))
            return ParseByYearDay(str.AsSpan());

        return [];
    }

    private static List<int> ParseByYearDay(ReadOnlySpan<char> str)
    {
        var yearDays = SplitToInt32List(str);
        foreach (var yearDay in yearDays)
        {
            if (yearDay is (>= 1 and <= 366) or (<= -1 and >= -366))
                continue;
            throw new FormatException($"Year day '{yearDay.ToString(CultureInfo.InvariantCulture)}' is invalid.");
        }

        return yearDays;
    }

    private static DayOfWeek ParseWeekStart(Dictionary<string, string> values)
    {
        if (values.TryGetNonEmptyValue("WKST", out var str))
            return ParseDayOfWeek(str);

        return DefaultFirstDayOfWeek;
    }

    private static List<int>? ParseBySeconds(Dictionary<string, string> values)
    {
        if (values.TryGetNonEmptyValue("BYSECOND", out var str))
            return ParseBySeconds(str.AsSpan());

        return null;
    }

    private static List<int>? ParseBySeconds(ReadOnlySpan<char> str)
    {
        if (str.IsEmpty)
            return null;

        var seconds = SplitToInt32List(str);
        foreach (var second in seconds)
        {
            if (second is >= 0 and <= 60)
                continue;

            throw new FormatException($"Second '{second.ToString(CultureInfo.InvariantCulture)}' is invalid. Must be between 0 and 60.");
        }

        return seconds;
    }

    private static List<int>? ParseByMinutes(Dictionary<string, string> values)
    {
        if (values.TryGetNonEmptyValue("BYMINUTE", out var str))
            return ParseByMinutes(str.AsSpan());

        return null;
    }

    private static List<int>? ParseByMinutes(ReadOnlySpan<char> str)
    {
        if (str.IsEmpty)
            return null;

        var minutes = SplitToInt32List(str);
        foreach (var minute in minutes)
        {
            if (minute is >= 0 and <= 59)
                continue;

            throw new FormatException($"Minute '{minute.ToString(CultureInfo.InvariantCulture)}' is invalid. Must be between 0 and 59.");
        }

        return minutes;
    }

    private static List<int>? ParseByHours(Dictionary<string, string> values)
    {
        if (values.TryGetNonEmptyValue("BYHOUR", out var str))
            return ParseByHours(str.AsSpan());

        return null;
    }

    private static List<int>? ParseByHours(ReadOnlySpan<char> str)
    {
        if (str.IsEmpty)
            return null;

        var hours = SplitToInt32List(str);
        foreach (var hour in hours)
        {
            if (hour is >= 0 and <= 23)
                continue;

            throw new FormatException($"Hour '{hour.ToString(CultureInfo.InvariantCulture)}' is invalid. Must be between 0 and 23.");
        }

        return hours;
    }

    private static ByDay[] ParseByDayWithOffset(Dictionary<string, string> values)
    {
        if (values.TryGetNonEmptyValue("BYDAY", out var str))
            return ParseByDayWithOffset(str);

        return [];
    }

    private static ByDay[] ParseByDayWithOffset(ReadOnlySpan<char> str)
    {
        var result = new List<ByDay>();
        var remaining = str;
        while (!remaining.IsEmpty)
        {
            var commaIndex = remaining.IndexOf(',');
            var part = commaIndex >= 0 ? remaining[..commaIndex] : remaining;
            remaining = commaIndex >= 0 ? remaining[(commaIndex + 1)..] : [];

            if (!part.IsEmpty)
                result.Add(ParseDayOfWeekWithOffset(part));
        }
        return result.ToArray();
    }

    private static DayOfWeek[] ParseByDay(Dictionary<string, string> values)
    {
        if (values.TryGetNonEmptyValue("BYDAY", out var str))
            return ParseByDay(str);

        return [];
    }

    private static DayOfWeek[] ParseByDay(ReadOnlySpan<char> str)
    {
        var result = new List<DayOfWeek>();
        var remaining = str;
        while (!remaining.IsEmpty)
        {
            var commaIndex = remaining.IndexOf(',');
            var part = commaIndex >= 0 ? remaining[..commaIndex] : remaining;
            remaining = commaIndex >= 0 ? remaining[(commaIndex + 1)..] : [];

            if (!part.IsEmpty)
            {
                result.Add(ParseDayOfWeek(part));
            }
        }
        return [.. result];
    }

    private static ByDay ParseDayOfWeekWithOffset(ReadOnlySpan<char> str)
    {
        for (var i = 0; i < str.Length; i++)
        {
            var c = str[i];
            if (c is (>= '0' and <= '9') or '+' or '-')
                continue;

            if (i == 0)
            {
                break;
            }
            else
            {
                return new ByDay(ParseDayOfWeek(str[i..]), int.Parse(str[..i], CultureInfo.InvariantCulture));
            }
        }

        return new ByDay(ParseDayOfWeek(str));
    }

    private static DayOfWeek ParseDayOfWeek(ReadOnlySpan<char> str)
    {
        if (str.Length != 2)
            throw new FormatException($"Day of week '{str}' is invalid.");

        if (str.Equals("SU", StringComparison.OrdinalIgnoreCase))
            return DayOfWeek.Sunday;
        if (str.Equals("MO", StringComparison.OrdinalIgnoreCase))
            return DayOfWeek.Monday;
        if (str.Equals("TU", StringComparison.OrdinalIgnoreCase))
            return DayOfWeek.Tuesday;
        if (str.Equals("WE", StringComparison.OrdinalIgnoreCase))
            return DayOfWeek.Wednesday;
        if (str.Equals("TH", StringComparison.OrdinalIgnoreCase))
            return DayOfWeek.Thursday;
        if (str.Equals("FR", StringComparison.OrdinalIgnoreCase))
            return DayOfWeek.Friday;
        if (str.Equals("SA", StringComparison.OrdinalIgnoreCase))
            return DayOfWeek.Saturday;

        throw new FormatException($"Day of week '{str}' is invalid.");
    }

    private protected static IEnumerable<T> FilterBySetPosition<T>(IList<T> source, IList<int>? setPositions)
    {
        if (setPositions is null || !setPositions.Any())
            return source;

        var result = new List<T>();
        foreach (var setPosition in setPositions)
        {
            int index;
            if (setPosition > 0)
            {
                index = setPosition - 1;
            }
            else
            {
                index = source.Count + setPosition;
            }

            if (index >= 0 && index < source.Count)
            {
                result.Add(source[index]);
            }
        }

        return result;
    }

    private protected static bool IsEmpty<T>([NotNullWhen(false)] IList<T>? list)
    {
        return list is null || list.Count == 0;
    }

    private protected static IEnumerable<T> Intersect<T>(params IEnumerable<T>?[] enumerables)
    {
        IEnumerable<T>? result = null;
        foreach (var enumerable in enumerables)
        {
            if (enumerable is null)
                continue;

            if (result is null)
            {
                result = enumerable;
            }
            else
            {
                result = result.Intersect(enumerable);
            }
        }

        return result ?? [];
    }

    private protected static List<DateTime>? ResultByWeekDaysInMonth(DateTime startOfMonth, IList<ByDay> byWeekDays)
    {
        List<DateTime>? resultByDays = null;
        if (!IsEmpty(byWeekDays))
        {
            resultByDays = [];

            // 1) Find all dates that match the day of month constraint
            var potentialResults = new Dictionary<ByDay, IList<DateTime>>();
            foreach (var byDay in byWeekDays)
            {
                potentialResults.Add(byDay, new List<DateTime>());
            }

            for (var day = startOfMonth; day.Month == startOfMonth.Month; day = day.AddDays(1))
            {
                foreach (var byDay in byWeekDays)
                {
                    if (byDay.DayOfWeek == day.DayOfWeek)
                    {
                        potentialResults[byDay].Add(day);
                    }
                }
            }

            // 2) Filter by ordinal
            foreach (var potentialResult in potentialResults)
            {
                if (potentialResult.Key.Ordinal.HasValue)
                {
                    int index;
                    if (potentialResult.Key.Ordinal > 0)
                    {
                        index = potentialResult.Key.Ordinal.Value - 1;
                    }
                    else
                    {
                        index = potentialResult.Value.Count + potentialResult.Key.Ordinal.Value;
                    }

                    if (index >= 0 && index < potentialResult.Value.Count)
                    {
                        resultByDays.Add(potentialResult.Value[index]);
                    }
                }
                else
                {
                    resultByDays.AddRange(potentialResult.Value);
                }
            }

            resultByDays.Sort();
            //resultByDays = FilterBySetPosition(resultByDays, BySetPositions).ToList();
        }

        return resultByDays;
    }

    private protected static List<DateTime>? ResultByMonthDays(DateTime startOfMonth, IList<int> byMonthDays)
    {
        List<DateTime>? resultByMonthDays = null;
        if (!IsEmpty(byMonthDays))
        {
            resultByMonthDays = [];
            var daysInMonth = DateTime.DaysInMonth(startOfMonth.Year, startOfMonth.Month);
            foreach (var day in byMonthDays)
            {
                if (day >= 1 && day <= daysInMonth)
                {
                    resultByMonthDays.Add(startOfMonth.AddDays(day - 1));
                }
                else if (day <= -1 && day >= -daysInMonth)
                {
                    resultByMonthDays.Add(startOfMonth.AddDays(daysInMonth + day));
                }
            }

            resultByMonthDays.Sort();
            //resultByMonthDays = FilterBySetPosition(resultByMonthDays, BySetPositions).ToList();
        }

        return resultByMonthDays;
    }

    private static List<int> SplitToInt32List(ReadOnlySpan<char> text)
    {
        var list = new List<int>();
        if (text.IsEmpty)
            return list;

        var remaining = text;
        while (!remaining.IsEmpty)
        {
            var commaIndex = remaining.IndexOf(',');
            var part = commaIndex >= 0 ? remaining[..commaIndex] : remaining;
            remaining = commaIndex >= 0 ? remaining[(commaIndex + 1)..] : [];

            var trimmed = part.Trim();
            if (!trimmed.IsEmpty && int.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var i))
            {
                list.Add(i);
            }
        }

        return list;
    }

    private static List<Month> SplitToMonthList(ReadOnlySpan<char> text)
    {
        var list = new List<Month>();
        if (text.IsEmpty)
            return list;

        var remaining = text;
        while (!remaining.IsEmpty)
        {
            var commaIndex = remaining.IndexOf(',');
            var part = commaIndex >= 0 ? remaining[..commaIndex] : remaining;
            remaining = commaIndex >= 0 ? remaining[(commaIndex + 1)..] : [];

            var trimmed = part.Trim();
            if (!trimmed.IsEmpty && Enum.TryParse<Month>(trimmed, ignoreCase: true, out var month))
            {
                list.Add(month);
            }
        }

        return list;
    }

    /// <summary>Gets all occurrences of the recurrence starting from the specified date.</summary>
    /// <param name="startDate">The date to start generating occurrences from.</param>
    /// <returns>An enumerable sequence of occurrence dates.</returns>
    public virtual IEnumerable<DateTime> GetNextOccurrences(DateTime startDate)
    {
        if (Occurrences == 0)
            yield break;

        var count = 0;
        foreach (var next in GetNextOccurrencesInternal(startDate))
        {
            if (EndDate.HasValue && next > EndDate.Value)
                yield break;

            yield return next;

            count++;
            if (Occurrences.HasValue && count >= Occurrences.Value)
                yield break;
        }
    }

    /// <summary>When implemented in a derived class, generates the internal sequence of occurrence dates.</summary>
    /// <param name="startDate">The date to start generating occurrences from.</param>
    /// <returns>An enumerable sequence of occurrence dates.</returns>
    protected abstract IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate);

    /// <summary>Gets the RFC 5545 string representation of this recurrence rule.</summary>
    public abstract string Text { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return Text;
    }
}
