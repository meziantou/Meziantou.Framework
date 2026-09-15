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
/// <item><description>BYSECOND - Limits occurrences to specific seconds (0-60; 60 denotes a leap second and is normalized to 59)</description></item>
/// <item><description>BYMINUTE - Limits occurrences to specific minutes (0-59)</description></item>
/// <item><description>BYHOUR - Limits occurrences to specific hours (0-23)</description></item>
/// <item><description>BYDAY - Limits occurrences to specific days of the week</description></item>
/// <item><description>BYMONTHDAY - Limits occurrences to specific days of the month (1-31, -1 to -31)</description></item>
/// <item><description>BYYEARDAY - Limits occurrences to specific days of the year (1-366, -1 to -366)</description></item>
/// <item><description>BYWEEKNO - Limits occurrences to specific weeks of the year (1-53, -1 to -53), only when FREQ is YEARLY</description></item>
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
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int? Occurrences
    {
        get => field;
        set
        {
            if (value.HasValue)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value.Value);
            }

            field = value;
        }
    }

    /// <summary>The interval between occurrences. Must be greater than or equal to 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int Interval
    {
        get => field;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 1;

    /// <summary>The first day of the week for the recurrence rule.</summary>
    public DayOfWeek WeekStart { get; set; } = DefaultFirstDayOfWeek;

    /// <summary>Limits occurrences to specific seconds (0-59). A parsed BYSECOND value of 60 denotes a
    /// leap second, which <see cref="DateTime"/> cannot represent, and is normalized to 59.</summary>
    public IList<int>? BySeconds { get; set; }

    /// <summary>Limits occurrences to specific minutes (0-59).</summary>
    public IList<int>? ByMinutes { get; set; }

    /// <summary>Limits occurrences to specific hours (0-23).</summary>
    public IList<int>? ByHours { get; set; }

    /// <summary>Limits occurrences to specific months (1-12).</summary>
    public IList<int> ByMonths { get; set; } = [];

    /// <summary>Limits occurrences to specific days of the month (1-31, -1 to -31).</summary>
    public IList<int> ByMonthDays { get; set; } = [];

    /// <summary>Limits occurrences to specific positions in the recurrence set.</summary>
    public IList<int>? BySetPositions { get; set; }

    /// <summary>Gets a value indicating whether the recurrence rule never ends.</summary>
    public bool IsForever => !Occurrences.HasValue && !EndDate.HasValue;

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

            if (!TryParseFrequency(values, out var frequency))
            {
                error = "Unknown Frequency (FREQ).";
                return false;
            }

            // RFC 5545 section 3.3.10 lists the rule parts that MUST NOT be used with some frequencies
            if (values.ContainsKey("BYWEEKNO") && frequency is not Frequency.Yearly)
            {
                error = "BYWEEKNO can only be used when FREQ is YEARLY.";
                return false;
            }

            if (values.ContainsKey("BYYEARDAY") && frequency is Frequency.Daily or Frequency.Weekly or Frequency.Monthly)
            {
                error = "BYYEARDAY cannot be used when FREQ is DAILY, WEEKLY, or MONTHLY.";
                return false;
            }

            // Set specific properties
            RecurrenceRule result;
            switch (frequency)
            {
                case Frequency.Secondly:
                    result = new SecondlyRecurrenceRule
                    {
                        ByWeekDays = ParseByDay(values),
                        ByYearDays = ParseByYearDay(values),
                    };
                    break;
                case Frequency.Minutely:
                    result = new MinutelyRecurrenceRule
                    {
                        ByWeekDays = ParseByDay(values),
                        ByYearDays = ParseByYearDay(values),
                    };
                    break;
                case Frequency.Hourly:
                    result = new HourlyRecurrenceRule
                    {
                        ByWeekDays = ParseByDay(values),
                        ByYearDays = ParseByYearDay(values),
                    };
                    break;
                case Frequency.Daily:
                    result = new DailyRecurrenceRule
                    {
                        ByWeekDays = ParseByDay(values),
                    };
                    break;
                case Frequency.Weekly:
                    result = new WeeklyRecurrenceRule
                    {
                        ByWeekDays = ParseByDay(values),
                    };
                    break;
                case Frequency.Monthly:
                    result = new MonthlyRecurrenceRule
                    {
                        ByWeekDays = ParseByDayWithOffset(values),
                    };
                    break;
                default:
                    var yearlyRecurrence = new YearlyRecurrenceRule
                    {
                        ByWeekDays = ParseByDayWithOffset(values),
                        ByMonthDays = ParseByMonthDays(values),
                        BySetPositions = ParseBySetPos(values),
                        ByMonths = ParseByMonth(values),
                        ByYearDays = ParseByYearDay(values),
                        ByWeekNumbers = ParseByWeekNo(values),
                    };

                    if (!IsEmpty(yearlyRecurrence.ByWeekNumbers) && yearlyRecurrence.ByWeekDays.Any(day => day.Ordinal.HasValue))
                    {
                        error = "BYDAY cannot have a numeric value when BYWEEKNO is specified.";
                        return false;
                    }

                    result = yearlyRecurrence;
                    break;
            }

            // Set general properties
            if (TryGetValue(values, "INTERVAL", out var intervalText))
            {
                if (!TryParseInt32(intervalText.AsSpan(), out var interval))
                {
                    error = $"INTERVAL value '{intervalText}' is invalid.";
                    return false;
                }

                if (interval < 1)
                {
                    error = $"INTERVAL value '{interval.ToString(CultureInfo.InvariantCulture)}' is invalid. Must be greater than or equal to 1.";
                    return false;
                }

                result.Interval = interval;
            }

            if (TryGetValue(values, "COUNT", out var countText))
            {
                if (!TryParseInt32(countText.AsSpan(), out var occurrences))
                {
                    error = $"COUNT value '{countText}' is invalid.";
                    return false;
                }

                if (occurrences < 0)
                {
                    error = $"COUNT value '{occurrences.ToString(CultureInfo.InvariantCulture)}' is invalid. Must be greater than or equal to 0.";
                    return false;
                }

                result.Occurrences = occurrences;
            }

            if (TryGetValue(values, "UNTIL", out var until))
            {
                if (!Utilities.TryParseDateTime(until, out var endDate))
                {
                    error = $"UNTIL value '{until}' is invalid.";
                    return false;
                }

                result.EndDate = endDate;
            }

            result.BySetPositions = ParseBySetPos(values);
            result.WeekStart = ParseWeekStart(values);
            result.BySeconds = ParseBySeconds(values);
            result.ByMinutes = ParseByMinutes(values);
            result.ByHours = ParseByHours(values);
            result.ByMonths = ParseByMonth(values);
            result.ByMonthDays = ParseByMonthDays(values);

            recurrenceRule = result;
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

    /// <summary>Gets the value of a rule part. The grammar of every rule part requires a value, so an empty one is rejected rather than ignored.</summary>
    private static bool TryGetValue(Dictionary<string, string> values, string name, [NotNullWhen(true)] out string? value)
    {
        if (!values.TryGetValue(name, out value))
            return false;

        if (value.Length is 0)
            throw new FormatException($"{name} value cannot be empty.");

        return true;
    }

    private static bool TryParseFrequency(Dictionary<string, string> values, out Frequency frequency)
    {
        frequency = Frequency.None;
        if (!values.TryGetValue("FREQ", out var value))
            return false;

        frequency = value.ToUpperInvariant() switch
        {
            "SECONDLY" => Frequency.Secondly,
            "MINUTELY" => Frequency.Minutely,
            "HOURLY" => Frequency.Hourly,
            "DAILY" => Frequency.Daily,
            "WEEKLY" => Frequency.Weekly,
            "MONTHLY" => Frequency.Monthly,
            "YEARLY" => Frequency.Yearly,
            _ => Frequency.None,
        };

        return frequency is not Frequency.None;
    }

    /// <summary>Parses an integer as the RFC 5545 grammar writes it: an optional sign followed by digits.</summary>
    private static bool TryParseInt32(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        if (text.IsEmpty)
            return false;

        var negative = false;
        var index = 0;
        if (text[0] is '+' or '-')
        {
            negative = text[0] is '-';
            index = 1;
            if (text.Length is 1)
                return false;
        }

        long result = 0;
        for (; index < text.Length; index++)
        {
            var c = text[index];
            if (c is < '0' or > '9')
                return false;

            result = (result * 10) + (c - '0');
            if (result > (long)int.MaxValue + 1)
                return false;
        }

        if (negative)
        {
            result = -result;
        }

        if (result is < int.MinValue or > int.MaxValue)
            return false;

        value = (int)result;
        return true;
    }

    private static List<int>? ParseBySetPos(Dictionary<string, string> values)
    {
        if (!TryGetValue(values, "BYSETPOS", out var str))
            return null;

        var setPositions = SplitToInt32List(str.AsSpan(), "BYSETPOS");
        foreach (var setPosition in setPositions)
        {
            if (setPosition is (>= 1 and <= 366) or (<= -1 and >= -366))
                continue;

            throw new FormatException($"BYSETPOS value '{setPosition.ToString(CultureInfo.InvariantCulture)}' is invalid. Must be between 1 and 366 or between -366 and -1.");
        }

        return setPositions;
    }

    private static List<int> ParseByMonthDays(Dictionary<string, string> values)
    {
        if (!TryGetValue(values, "BYMONTHDAY", out var str))
            return [];

        var monthDays = SplitToInt32List(str.AsSpan(), "BYMONTHDAY");
        foreach (var monthDay in monthDays)
        {
            if (monthDay is (>= 1 and <= 31) or (<= -1 and >= -31))
                continue;

            throw new FormatException($"Monthday '{monthDay.ToString(CultureInfo.InvariantCulture)}' is invalid.");
        }

        return monthDays;
    }

    private static List<int> ParseByMonth(Dictionary<string, string> values)
    {
        if (!TryGetValue(values, "BYMONTH", out var str))
            return [];

        var months = SplitToMonthList(str.AsSpan());
        foreach (var month in months)
        {
            if (month is < 1 or > 12)
                throw new FormatException($"BYMONTH value '{month.ToString(CultureInfo.InvariantCulture)}' is invalid.");
        }

        return months;
    }

    private static List<int> ParseByYearDay(Dictionary<string, string> values)
    {
        if (!TryGetValue(values, "BYYEARDAY", out var str))
            return [];

        var yearDays = SplitToInt32List(str.AsSpan(), "BYYEARDAY");
        foreach (var yearDay in yearDays)
        {
            if (yearDay is (>= 1 and <= 366) or (<= -1 and >= -366))
                continue;

            throw new FormatException($"Year day '{yearDay.ToString(CultureInfo.InvariantCulture)}' is invalid.");
        }

        return yearDays;
    }

    private static List<int>? ParseByWeekNo(Dictionary<string, string> values)
    {
        if (!TryGetValue(values, "BYWEEKNO", out var str))
            return null;

        var weekNumbers = SplitToInt32List(str.AsSpan(), "BYWEEKNO");
        foreach (var weekNumber in weekNumbers)
        {
            if (weekNumber is (>= 1 and <= 53) or (<= -1 and >= -53))
                continue;

            throw new FormatException($"BYWEEKNO value '{weekNumber.ToString(CultureInfo.InvariantCulture)}' is invalid. Must be between 1 and 53 or between -53 and -1.");
        }

        return weekNumbers;
    }

    private static DayOfWeek ParseWeekStart(Dictionary<string, string> values)
    {
        if (TryGetValue(values, "WKST", out var str))
            return ParseDayOfWeek(str.AsSpan());

        return DefaultFirstDayOfWeek;
    }

    private static List<int>? ParseBySeconds(Dictionary<string, string> values)
    {
        if (!TryGetValue(values, "BYSECOND", out var str))
            return null;

        var seconds = SplitToInt32List(str.AsSpan(), "BYSECOND");
        for (var i = 0; i < seconds.Count; i++)
        {
            var second = seconds[i];
            if (second is < 0 or > 60)
                throw new FormatException($"Second '{second.ToString(CultureInfo.InvariantCulture)}' is invalid. Must be between 0 and 60.");

            // RFC 5545 allows 60 to denote a leap second. DateTime cannot represent one, so it
            // is normalized to the last representable second of the minute.
            if (second is 60)
                seconds[i] = 59;
        }

        // Normalizing may have produced a duplicate (BYSECOND=59,60), which would yield the
        // same occurrence twice.
        return seconds.Distinct().ToList();
    }

    private static List<int>? ParseByMinutes(Dictionary<string, string> values)
    {
        if (!TryGetValue(values, "BYMINUTE", out var str))
            return null;

        var minutes = SplitToInt32List(str.AsSpan(), "BYMINUTE");
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
        if (!TryGetValue(values, "BYHOUR", out var str))
            return null;

        var hours = SplitToInt32List(str.AsSpan(), "BYHOUR");
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
        if (!TryGetValue(values, "BYDAY", out var str))
            return [];

        var result = new List<ByDay>();
        var remaining = str.AsSpan();
        while (true)
        {
            var commaIndex = remaining.IndexOf(',');
            var part = commaIndex >= 0 ? remaining[..commaIndex] : remaining;
            result.Add(ParseDayOfWeekWithOffset(part));
            if (commaIndex < 0)
                break;

            remaining = remaining[(commaIndex + 1)..];
        }

        return [.. result];
    }

    private static DayOfWeek[] ParseByDay(Dictionary<string, string> values)
    {
        if (!TryGetValue(values, "BYDAY", out var str))
            return [];

        var result = new List<DayOfWeek>();
        var remaining = str.AsSpan();
        while (true)
        {
            var commaIndex = remaining.IndexOf(',');
            var part = commaIndex >= 0 ? remaining[..commaIndex] : remaining;
            result.Add(ParseDayOfWeek(part));
            if (commaIndex < 0)
                break;

            remaining = remaining[(commaIndex + 1)..];
        }

        return [.. result];
    }

    private static ByDay ParseDayOfWeekWithOffset(ReadOnlySpan<char> str)
    {
        // weekdaynum = [[plus / minus] ordwk] weekday, where ordwk is 1 to 53
        var index = 0;
        if (!str.IsEmpty && str[0] is '+' or '-')
        {
            index = 1;
        }

        while (index < str.Length && str[index] is >= '0' and <= '9')
        {
            index++;
        }

        if (index is 0)
            return new ByDay(ParseDayOfWeek(str));

        if (!TryParseInt32(str[..index], out var ordinal) || ordinal is 0 or > 53 or < -53)
            throw new FormatException($"Day of week '{str}' is invalid. The ordinal must be between 1 and 53 or between -53 and -1.");

        return new ByDay(ParseDayOfWeek(str[index..]), ordinal);
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

    private protected static bool IsEmpty<T>([NotNullWhen(false)] IList<T>? list)
    {
        return list is null || list.Count is 0;
    }

    private static List<int> SplitToInt32List(ReadOnlySpan<char> text, string partName)
    {
        var list = new List<int>();
        var remaining = text;
        while (true)
        {
            var commaIndex = remaining.IndexOf(',');
            var part = commaIndex >= 0 ? remaining[..commaIndex] : remaining;
            if (!TryParseInt32(part, out var value))
                throw new FormatException($"{partName} value '{part}' is invalid.");

            list.Add(value);
            if (commaIndex < 0)
                break;

            remaining = remaining[(commaIndex + 1)..];
        }

        return list;
    }

    private static List<int> SplitToMonthList(ReadOnlySpan<char> text)
    {
        var list = new List<int>();
        var remaining = text;
        while (true)
        {
            var commaIndex = remaining.IndexOf(',');
            var part = commaIndex >= 0 ? remaining[..commaIndex] : remaining;
            if (TryParseInt32(part, out var monthValue))
            {
                list.Add(monthValue);
            }
            else if (TryParseMonthName(part, out var month))
            {
                list.Add(month);
            }
            else
            {
                throw new FormatException($"BYMONTH value '{part}' is invalid.");
            }

            if (commaIndex < 0)
                break;

            remaining = remaining[(commaIndex + 1)..];
        }

        return list;
    }

    private static bool TryParseMonthName(ReadOnlySpan<char> text, out int month)
    {
        for (var value = Month.January; value <= Month.December; value++)
        {
            if (text.Equals(value.ToString().AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                month = (int)value;
                return true;
            }
        }

        month = 0;
        return false;
    }

    /// <summary>Gets all occurrences of the recurrence starting from the specified date.</summary>
    /// <param name="startDate">The date to start generating occurrences from.</param>
    /// <returns>An enumerable sequence of occurrence dates.</returns>
    public virtual IEnumerable<DateTime> GetNextOccurrences(DateTime startDate)
    {
        if (Occurrences is 0)
            yield break;

        var count = 0;
        foreach (var next in GetNextOccurrencesInternal(startDate, EndDate))
        {
            if (EndDate.HasValue && next > EndDate.Value)
                yield break;

            yield return next;

            count++;
            if (Occurrences.HasValue && count >= Occurrences.Value)
                yield break;
        }
    }

    /// <summary>Gets all occurrences of the recurrence, reading <paramref name="startDate"/> as a wall-clock time in <paramref name="timeZone"/>.</summary>
    /// <param name="startDate">The wall-clock time to start generating occurrences from. Its <see cref="DateTime.Kind"/> is ignored.</param>
    /// <param name="timeZone">The time zone the recurrence is expressed in, as an iCalendar DTSTART;TZID= would.</param>
    /// <returns>An enumerable sequence of occurrences, each carrying the UTC offset in effect at that occurrence.</returns>
    /// <remarks>
    /// <para>The occurrences keep their wall-clock time across a daylight saving transition, so their UTC offset changes.
    /// A local time made invalid or ambiguous by a transition is resolved as RFC 5545 section 3.3.5 requires: an ambiguous
    /// time keeps its first occurrence, and an invalid time is read with the UTC offset in effect before the gap.</para>
    /// <para>As a consequence, a sub-daily recurrence repeats an instant across a forward transition and skips the instants
    /// of the repeated hour across a backward one.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> is <see langword="null"/>.</exception>
    public virtual IEnumerable<DateTimeOffset> GetNextOccurrences(DateTime startDate, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return Iterate(this, DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified), timeZone);

        static IEnumerable<DateTimeOffset> Iterate(RecurrenceRule rule, DateTime wallClockStart, TimeZoneInfo timeZone)
        {
            if (rule.Occurrences is 0)
                yield break;

            var count = 0;
            DateTime? lastInstant = null;
            foreach (var next in rule.GetNextOccurrencesInternal(wallClockStart, GetWallClockEndBound(rule.EndDate)))
            {
                var occurrence = Utilities.ToDateTimeOffset(next, timeZone);

                // A duplicate is not part of the recurrence set, so it does not count towards COUNT either.
                // Dropping it first also leaves the values below increasing, which is what lets UNTIL stop
                // the enumeration instead of only filtering it.
                if (Utilities.IsDuplicate(lastInstant, occurrence))
                    continue;

                if (rule.EndDate.HasValue && IsAfterEndDate(next, occurrence, rule.EndDate.Value))
                    yield break;

                lastInstant = occurrence.UtcDateTime;
                yield return occurrence;

                count++;
                if (rule.Occurrences.HasValue && count >= rule.Occurrences.Value)
                    yield break;
            }
        }

        // The wall clock of an instant is less than one day away from its UTC value, so this bound never stops the
        // enumeration before an occurrence that the exact comparison of IsAfterEndDate keeps.
        static DateTime? GetWallClockEndBound(DateTime? endDate)
        {
            if (endDate is not { } value || value.Kind is DateTimeKind.Unspecified)
                return endDate;

            var utc = value.ToUniversalTime();
            return utc.Ticks < DateTime.MaxValue.Ticks - TimeSpan.TicksPerDay ? utc.AddDays(1) : DateTime.MaxValue;
        }

        static bool IsAfterEndDate(DateTime wallClock, DateTimeOffset occurrence, DateTime endDate) => endDate.Kind switch
        {
            // A UTC UNTIL bounds the recurrence by instant, not by wall clock.
            DateTimeKind.Utc => occurrence.UtcDateTime > endDate,
            DateTimeKind.Local => occurrence.UtcDateTime > endDate.ToUniversalTime(),

            // A floating UNTIL is a wall-clock reading in the recurrence's own time zone.
            _ => wallClock > endDate,
        };
    }

    /// <summary>Gets all occurrences of the recurrence, starting from the instant <paramref name="startDate"/> denotes.</summary>
    /// <param name="startDate">The instant to start generating occurrences from. It is reduced to a wall-clock time in <paramref name="timeZone"/>.</param>
    /// <param name="timeZone">The time zone the recurrence is expressed in, as an iCalendar DTSTART;TZID= would.</param>
    /// <returns>An enumerable sequence of occurrences, each carrying the UTC offset in effect at that occurrence.</returns>
    /// <remarks>Reducing an instant to a wall-clock time is lossy in the hour repeated by a backward transition, where both
    /// readings denote the same wall clock. Use the <see cref="DateTime"/> overload to control which one is meant.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> is <see langword="null"/>.</exception>
    public IEnumerable<DateTimeOffset> GetNextOccurrences(DateTimeOffset startDate, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return GetNextOccurrences(TimeZoneInfo.ConvertTime(startDate, timeZone).DateTime, timeZone);
    }

    /// <summary>When implemented in a derived class, generates the internal sequence of occurrence dates.</summary>
    /// <param name="startDate">The date to start generating occurrences from.</param>
    /// <returns>An enumerable sequence of occurrence dates.</returns>
    protected abstract IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate);

    /// <summary>Generates the sequence of occurrence dates, in increasing order and without duplicates.</summary>
    /// <param name="startDate">The date to start generating occurrences from.</param>
    /// <param name="endBound">A wall-clock value after which no occurrence is needed, so that a rule whose periods stop matching still ends.</param>
    /// <returns>An enumerable sequence of occurrence dates.</returns>
    private protected virtual IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate, DateTime? endBound)
    {
        return GetNextOccurrencesInternal(startDate);
    }

    /// <summary>Gets the RFC 5545 string representation of this recurrence rule.</summary>
    public abstract string Text { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return Text;
    }
}
