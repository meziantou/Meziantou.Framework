using System.Globalization;

namespace Meziantou.Framework.Scheduling;

public abstract class RecurrenceRule
{
    public static readonly DayOfWeek DefaultFirstDayOfWeek = DayOfWeek.Monday;
    public const string DefaultFirstDayOfWeekString = "MO";

    /// <summary>
    /// End date (inclusive)
    /// </summary>
    public DateTime? EndDate { get; set; }
    public int? Occurrences { get; set; }
    public int Interval { get; set; } = 1;
    public DayOfWeek WeekStart { get; set; } = DefaultFirstDayOfWeek;
    public IList<int>? BySetPositions { get; set; }

    public bool IsForever => Occurrences.HasValue || EndDate.HasValue;

    public static RecurrenceRule Parse(string rrule)
    {
        if (!TryParse(rrule, out var recurrenceRule, out var error))
            throw new FormatException("RRule format is invalid: " + error);

        return recurrenceRule;
    }

    public static bool TryParse([NotNullWhen(returnValue: true)] string? rrule, [NotNullWhen(returnValue: true)] out RecurrenceRule? recurrenceRule)
    {
        return TryParse(rrule, out recurrenceRule, out _);
    }

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
            if ((monthDay >= 1 && monthDay <= 31) || (monthDay <= -1 && monthDay >= -31))
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
            if (!Enum.IsDefined(typeof(Month), month))
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
            if ((yearDay >= 1 && yearDay <= 366) || (yearDay <= -1 && yearDay >= -366))
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
            if ((c >= '0' && c <= '9') || c == '+' || c == '-')
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

    public DateTime? GetNextOccurrence(DateTime startDate)
    {
        return GetNextOccurrences(startDate).FirstOrDefault();
    }

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

    protected abstract IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate);

    public abstract string Text { get; }

    public override string ToString()
    {
        return Text;
    }
}
