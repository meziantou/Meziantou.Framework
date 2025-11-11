namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a recurrence rule as defined in RFC 5545 for recurring events.</summary>
/// <example>
/// <code>
/// var rrule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20000131T140000Z;BYMONTH=1");
/// var nextOccurrences = rrule.GetNextOccurrences(DateTime.Now).Take(50).ToArray();
/// </code>
/// </example>
public abstract class RecurrenceRule
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
            throw new FormatException("RRule format is invalid: " + error);

        return recurrenceRule;
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

        try
        {
            // Extract parts
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parts = rrule.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var (name, value) = SplitPart(part);
                if (values.ContainsKey(name))
                {
                    error = $"Duplicate name: '{name}'.";
                    return false;
                }

                if (string.Equals("UNTIL", name, StringComparison.OrdinalIgnoreCase) && values.ContainsKey("COUNT"))
                {
                    error = "Cannot set UNTIL and COUNT in the same recurrence rule.";
                    return false;
                }

                if (string.Equals("COUNT", name, StringComparison.OrdinalIgnoreCase) && values.ContainsKey("UNTIL"))
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
                case Frequency.Daily:
                    var dailyRecurrenceRule = new DailyRecurrenceRule
                    {
                        ByMonthDays = ParseByMonthDays(values),
                        ByMonths = ParseByMonth(values),
                        ByWeekDays = ParseByDay(values),
                    };
                    recurrenceRule = dailyRecurrenceRule;
                    break;
                case Frequency.Weekly:
                    var weeklyRecurrence = new WeeklyRecurrenceRule
                    {
                        ByMonths = ParseByMonth(values),
                        ByWeekDays = ParseByDay(values),
                    };
                    recurrenceRule = weeklyRecurrence;
                    break;
                case Frequency.Monthly:
                    var monthlyRecurrence = new MonthlyRecurrenceRule
                    {
                        ByWeekDays = ParseByDayWithOffset(values),
                        ByMonthDays = ParseByMonthDays(values),
                        ByMonths = ParseByMonth(values),
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
            recurrenceRule.Occurrences = values.GetValue("COUNT", (int?)null);
            var until = values.GetValue("UNTIL", (string?)null);
            if (until is not null)
            {
                recurrenceRule.EndDate = Utilities.ParseDateTime(until);
            }

            recurrenceRule.BySetPositions = ParseBySetPos(values);
            recurrenceRule.WeekStart = ParseWeekStart(values);

            return true;
        }
        catch (FormatException e)
        {
            error = e.Message;
            return false;
        }
    }

    private static (string Name, string Value) SplitPart(string str)
    {
        var index = str.IndexOf('=', StringComparison.Ordinal);
        if (index < 0)
            throw new FormatException($"'{str}' is invalid.");

        var name = str[..index];
        if (string.IsNullOrEmpty(name))
            throw new FormatException($"'{str}' is invalid.");

        var value = str[(index + 1)..];
        return (name, value);
    }

    private static List<int>? ParseBySetPos(IDictionary<string, string> values)
    {
        return ParseBySetPos(values.GetValue("BYSETPOS", (string?)null));
    }

    private static List<int>? ParseBySetPos(string? str)
    {
        if (string.IsNullOrEmpty(str))
            return null;

        return SplitToInt32List(str);
    }

    private static List<int> ParseByMonthDays(IDictionary<string, string> values)
    {
        return ParseByMonthDays(values.GetValue("BYMONTHDAY", (string?)null));
    }

    private static List<int> ParseByMonthDays(string? str)
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

    private static List<Month> ParseByMonth(IDictionary<string, string> values)
    {
        return ParseByMonth(values.GetValue("BYMONTH", (string?)null));
    }

    private static List<Month> ParseByMonth(string? str)
    {
        var months = SplitToMonthList(str);
        foreach (var month in months)
        {
            if (!Enum.IsDefined(month))
            {
                throw new FormatException("BYMONTH is invalid.");
            }
        }

        return months;
    }

    private static List<int> ParseByYearDay(IDictionary<string, string> values)
    {
        return ParseByYearDay(values.GetValue("BYYEARDAY", (string?)null));
    }

    private static List<int> ParseByYearDay(string? str)
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

    private static DayOfWeek ParseWeekStart(IDictionary<string, string> values)
    {
        var str = values.GetValue("WKST", DefaultFirstDayOfWeekString);
        if (string.IsNullOrEmpty(str))
            return DefaultFirstDayOfWeek;

        return ParseDayOfWeek(str);
    }

    private static ByDay[] ParseByDayWithOffset(IDictionary<string, string> values)
    {
        return ParseByDayWithOffset(values.GetValue("BYDAY", (string?)null));
    }

    private static ByDay[] ParseByDayWithOffset(string? str)
    {
        return SplitToStringArray(str).Select(ParseDayOfWeekWithOffset).ToArray();
    }

    private static DayOfWeek[] ParseByDay(IDictionary<string, string> values)
    {
        return ParseByDay(values.GetValue("BYDAY", (string?)null));
    }

    private static DayOfWeek[] ParseByDay(string? str)
    {
        return SplitToStringArray(str).Select(ParseDayOfWeek).ToArray();
    }

    private static ByDay ParseDayOfWeekWithOffset(string str)
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

    private static DayOfWeek ParseDayOfWeek(string str)
    {
        return str.ToUpperInvariant() switch
        {
            "SU" => DayOfWeek.Sunday,
            "MO" => DayOfWeek.Monday,
            "TU" => DayOfWeek.Tuesday,
            "WE" => DayOfWeek.Wednesday,
            "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday,
            "SA" => DayOfWeek.Saturday,
            _ => throw new FormatException($"Day of week '{str}' is invalid."),
        };
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

        return result ?? Enumerable.Empty<T>();
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

    private static IEnumerable<string> SplitToList(string? text)
    {
        if (text is null)
            yield break;

        foreach (var str in text.Split(','))
        {
            var trim = str.Trim();
            if (trim.Length != 0)
                yield return trim;
        }
    }

    private static string[] SplitToStringArray(string? text)
    {
        return SplitToList(text).ToArray();
    }

    private static List<int> SplitToInt32List(string? text)
    {
        var list = new List<int>();
        foreach (var str in SplitToList(text))
        {
            if (str.Length != 0 && int.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var i))
            {
                list.Add(i);
            }
        }

        return list;
    }

    private static List<Month> SplitToMonthList(string? text)
    {
        var list = new List<Month>();
        foreach (var str in SplitToList(text))
        {
            if (str.Length != 0 && Enum.TryParse<Month>(str, ignoreCase: true, out var month))
            {
                list.Add(month);
            }
        }

        return list;
    }

    /// <summary>Gets the next occurrence of the recurrence starting from the specified date.</summary>
    /// <param name="startDate">The date to start searching for the next occurrence.</param>
    /// <returns>The next occurrence date, or <see langword="null"/> if there are no more occurrences.</returns>
    public DateTime? GetNextOccurrence(DateTime startDate)
    {
        return GetNextOccurrences(startDate).FirstOrDefault();
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
