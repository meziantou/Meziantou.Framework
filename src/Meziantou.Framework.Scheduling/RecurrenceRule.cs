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
/// <item><description>BYMONTHDAY - Limits occurrences to specific days of the month (1-31, -1 to -31), not when FREQ is WEEKLY</description></item>
/// <item><description>BYYEARDAY - Limits occurrences to specific days of the year (1-366, -1 to -366), not when FREQ is DAILY, WEEKLY or MONTHLY</description></item>
/// <item><description>BYWEEKNO - Limits occurrences to specific weeks of the year (1-53, -1 to -53), only when FREQ is YEARLY</description></item>
/// <item><description>BYMONTH - Limits occurrences to specific months (1-12)</description></item>
/// <item><description>BYSETPOS - Limits occurrences to specific positions in the recurrence set (1-366, -1 to -366), only with another BYxxx rule part</description></item>
/// <item><description>RSCALE and SKIP (RFC 7529) - Only their GREGORIAN and OMIT values, which are the default behavior</description></item>
/// </list>
/// <para>An unknown rule part makes the rule invalid.</para>
/// </remarks>
public abstract class RecurrenceRule : IRecurrenceRule
{
    /// <summary>The default first day of the week (Monday).</summary>
    public static readonly DayOfWeek DefaultFirstDayOfWeek = DayOfWeek.Monday;

    /// <summary>The string representation of the default first day of the week.</summary>
    public const string DefaultFirstDayOfWeekString = "MO";

    // The rule parts of RFC 5545 section 3.3.10, and the RSCALE and SKIP rule parts of RFC 7529 section 4.1
    private static readonly HashSet<string> RulePartNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "FREQ", "UNTIL", "COUNT", "INTERVAL", "BYSECOND", "BYMINUTE", "BYHOUR", "BYDAY", "BYMONTHDAY", "BYYEARDAY", "BYWEEKNO", "BYMONTH", "BYSETPOS", "WKST", "RSCALE", "SKIP",
    };

    /// <summary>End date (inclusive)</summary>
    /// <remarks>
    /// <para>A <see cref="DateTimeKind.Utc"/> or <see cref="DateTimeKind.Local"/> value denotes an instant, and a
    /// <see cref="DateTimeKind.Unspecified"/> value a wall-clock reading. Parsing a UTC UNTIL, or one carrying an offset,
    /// produces a <see cref="DateTimeKind.Utc"/> value; a floating date-time or a date produces an
    /// <see cref="DateTimeKind.Unspecified"/> value, a date being stored as its first instant.</para>
    /// <para>An instant bounds the occurrences generated from a <see cref="DateTimeKind.Utc"/> or <see cref="DateTimeKind.Local"/>
    /// start date, or from a <see cref="DateTimeOffset"/>, by instant, and a wall-clock reading bounds every occurrence by wall clock.</para>
    /// <para>A parsed date designates the whole day, so it keeps every occurrence on that day, whatever its time. Setting this
    /// property stores a date-time: the value is then an exact bound.</para>
    /// <para>UNTIL and COUNT cannot be used in the same recurrence rule, so <see cref="Occurrences"/> must be <see langword="null"/> to set a value.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The value is not <see langword="null"/> and <see cref="Occurrences"/> is set.</exception>
    public DateTime? EndDate
    {
        get => field;
        set
        {
            if (value.HasValue && Occurrences.HasValue)
                throw new InvalidOperationException($"Cannot set {nameof(EndDate)} when {nameof(Occurrences)} is set: UNTIL and COUNT cannot be used in the same recurrence rule.");

            field = value;
            IsEndDateDate = false;
        }
    }

    /// <summary>Gets a value indicating whether <see cref="EndDate"/> was parsed from a DATE value, so it is written back as one and includes the whole day.</summary>
    internal bool IsEndDateDate { get; private set; }

    /// <summary>Gets the last value an occurrence can take: <see cref="EndDate"/>, or the last tick of its day when it was parsed from a DATE value.</summary>
    internal DateTime? EndBound => EndDate is { } endDate && IsEndDateDate ? new DateTime(endDate.Date.Ticks + TimeSpan.TicksPerDay - 1, endDate.Kind) : EndDate;

    /// <summary>Gets the UNTIL value as written in <see cref="Text"/>.</summary>
    internal string? EndDateText => EndDate is { } endDate ? Utilities.EndDateToString(endDate, IsEndDateDate) : null;

    /// <summary>The number of occurrences before the recurrence ends.</summary>
    /// <remarks>UNTIL and COUNT cannot be used in the same recurrence rule, so <see cref="EndDate"/> must be <see langword="null"/> to set a value.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    /// <exception cref="InvalidOperationException">The value is not <see langword="null"/> and <see cref="EndDate"/> is set.</exception>
    public int? Occurrences
    {
        get => field;
        set
        {
            if (value.HasValue)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value.Value);
                if (EndDate.HasValue)
                    throw new InvalidOperationException($"Cannot set {nameof(Occurrences)} when {nameof(EndDate)} is set: UNTIL and COUNT cannot be used in the same recurrence rule.");
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
    /// <exception cref="ArgumentOutOfRangeException">The value is not a day of the week.</exception>
    public DayOfWeek WeekStart
    {
        get => field;
        set
        {
            if (value is < DayOfWeek.Sunday or > DayOfWeek.Saturday)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not a day of the week.");

            field = value;
        }
    } = DefaultFirstDayOfWeek;

    /// <summary>Limits occurrences to specific seconds (0-59). A BYSECOND value of 60 denotes a
    /// leap second, which <see cref="DateTime"/> cannot represent, and is normalized to 59.</summary>
    /// <remarks>The values are validated when the occurrences are enumerated.</remarks>
    public IList<int>? BySeconds { get; set; }

    /// <summary>Limits occurrences to specific minutes (0-59).</summary>
    /// <remarks>The values are validated when the occurrences are enumerated.</remarks>
    public IList<int>? ByMinutes { get; set; }

    /// <summary>Limits occurrences to specific hours (0-23).</summary>
    /// <remarks>The values are validated when the occurrences are enumerated.</remarks>
    public IList<int>? ByHours { get; set; }

    /// <summary>Limits occurrences to specific months (1-12).</summary>
    /// <remarks>The values are validated when the occurrences are enumerated.</remarks>
    public IList<int> ByMonths { get; set; } = [];

    /// <summary>Limits occurrences to specific days of the month (1-31, -1 to -31). It cannot be used when the frequency is weekly.</summary>
    /// <remarks>The values are validated when the occurrences are enumerated.</remarks>
    public IList<int> ByMonthDays { get; set; } = [];

    /// <summary>Limits occurrences to specific positions in the recurrence set (1-366, -1 to -366). It can only be used with another BYxxx rule part.</summary>
    /// <remarks>The values are validated when the occurrences are enumerated.</remarks>
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

                // RFC 5545 section 3.3.10 has no extension rule part, so an unknown or misspelled name cannot be ignored
                if (!RulePartNames.Contains(name))
                {
                    error = $"Unknown rule part: '{name}'.";
                    return false;
                }

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

            // RFC 7529 section 4.1: the rule is evaluated with the Gregorian calendar, and an invalid date is omitted
            if (TryGetValue(values, "RSCALE", out var calendarScale) && !calendarScale.Equals("GREGORIAN", StringComparison.OrdinalIgnoreCase))
            {
                error = $"RSCALE value '{calendarScale}' is not supported. Only GREGORIAN is supported.";
                return false;
            }

            if (TryGetValue(values, "SKIP", out var skip))
            {
                if (!values.ContainsKey("RSCALE"))
                {
                    error = "SKIP can only be used when RSCALE is specified.";
                    return false;
                }

                if (!skip.Equals("OMIT", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"SKIP value '{skip}' is not supported. Only OMIT is supported.";
                    return false;
                }
            }

            // RFC 5545 section 3.3.10 lists the rule parts that MUST NOT be used with some frequencies. The ones that
            // the rule can hold are checked by GetValidationError.
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
                    result = new YearlyRecurrenceRule
                    {
                        ByWeekDays = ParseByDayWithOffset(values),
                        ByYearDays = ParseByYearDay(values),
                        ByWeekNumbers = ParseByWeekNo(values),
                    };
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
                result.IsEndDateDate = until.Length is 8;
            }

            result.BySetPositions = ParseBySetPos(values);
            result.WeekStart = ParseWeekStart(values);
            result.BySeconds = ParseBySeconds(values);
            result.ByMinutes = ParseByMinutes(values);
            result.ByHours = ParseByHours(values);
            result.ByMonths = ParseByMonth(values);
            result.ByMonthDays = ParseByMonthDays(values);

            error = result.GetValidationError();
            if (error is not null)
                return false;

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

        return SplitToInt32List(str.AsSpan(), "BYSETPOS");
    }

    private static List<int> ParseByMonthDays(Dictionary<string, string> values)
    {
        if (!TryGetValue(values, "BYMONTHDAY", out var str))
            return [];

        return SplitToInt32List(str.AsSpan(), "BYMONTHDAY");
    }

    private static List<int> ParseByMonth(Dictionary<string, string> values)
    {
        if (!TryGetValue(values, "BYMONTH", out var str))
            return [];

        return SplitToMonthList(str.AsSpan());
    }

    private static List<int> ParseByYearDay(Dictionary<string, string> values)
    {
        if (!TryGetValue(values, "BYYEARDAY", out var str))
            return [];

        return SplitToInt32List(str.AsSpan(), "BYYEARDAY");
    }

    private static List<int>? ParseByWeekNo(Dictionary<string, string> values)
    {
        if (!TryGetValue(values, "BYWEEKNO", out var str))
            return null;

        return SplitToInt32List(str.AsSpan(), "BYWEEKNO");
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
            // RFC 5545 allows 60 to denote a leap second. DateTime cannot represent one, so it
            // is normalized to the last representable second of the minute.
            if (seconds[i] is 60)
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

        return SplitToInt32List(str.AsSpan(), "BYMINUTE");
    }

    private static List<int>? ParseByHours(Dictionary<string, string> values)
    {
        if (!TryGetValue(values, "BYHOUR", out var str))
            return null;

        return SplitToInt32List(str.AsSpan(), "BYHOUR");
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

        // The range of the ordinal is checked by GetValidationError
        if (!TryParseInt32(str[..index], out var ordinal))
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

    /// <summary>Appends a comma-separated list of integers as the RFC 5545 grammar writes them, whatever the current culture.</summary>
    private protected static void AppendValues(StringBuilder sb, IEnumerable<int> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                sb.Append(',');
            }

            sb.Append(value.ToString(CultureInfo.InvariantCulture));
            first = false;
        }
    }

    /// <summary>Throws when a value of the rule cannot be written as a valid recurrence rule, which the public mutable lists allow.</summary>
    private void EnsureValid()
    {
        if (GetValidationError() is { } error)
            throw new InvalidOperationException("The recurrence rule is invalid: " + error);
    }

    /// <summary>Gets a description of the first value that RFC 5545 section 3.3.10 does not allow, or <see langword="null"/> when the rule is valid.</summary>
    /// <remarks>It is shared by the parser and the evaluation, so a rule built by setting properties obeys the same rules as a parsed one.</remarks>
    private string? GetValidationError()
    {
        var frequency = Frequency.None;
        IList<DayOfWeek>? weekDays = null;
        IList<ByDay>? byDays = null;
        IList<int>? yearDays = null;
        IList<int>? weekNumbers = null;
        switch (this)
        {
            case SecondlyRecurrenceRule rule:
                (frequency, weekDays, yearDays) = (Frequency.Secondly, rule.ByWeekDays, rule.ByYearDays);
                break;
            case MinutelyRecurrenceRule rule:
                (frequency, weekDays, yearDays) = (Frequency.Minutely, rule.ByWeekDays, rule.ByYearDays);
                break;
            case HourlyRecurrenceRule rule:
                (frequency, weekDays, yearDays) = (Frequency.Hourly, rule.ByWeekDays, rule.ByYearDays);
                break;
            case DailyRecurrenceRule rule:
                (frequency, weekDays) = (Frequency.Daily, rule.ByWeekDays);
                break;
            case WeeklyRecurrenceRule rule:
                (frequency, weekDays) = (Frequency.Weekly, rule.ByWeekDays);
                break;
            case MonthlyRecurrenceRule rule:
                (frequency, byDays) = (Frequency.Monthly, rule.ByWeekDays);
                break;
            case YearlyRecurrenceRule rule:
                (frequency, byDays, yearDays, weekNumbers) = (Frequency.Yearly, rule.ByWeekDays, rule.ByYearDays, rule.ByWeekNumbers);
                break;
        }

        var error = GetRangeError(BySeconds, "BYSECOND", min: 0, max: 60, allowNegative: false)
            ?? GetRangeError(ByMinutes, "BYMINUTE", min: 0, max: 59, allowNegative: false)
            ?? GetRangeError(ByHours, "BYHOUR", min: 0, max: 23, allowNegative: false)
            ?? GetRangeError(ByMonths, "BYMONTH", min: 1, max: 12, allowNegative: false)
            ?? GetRangeError(ByMonthDays, "BYMONTHDAY", min: 1, max: 31, allowNegative: true)
            ?? GetRangeError(yearDays, "BYYEARDAY", min: 1, max: 366, allowNegative: true)
            ?? GetRangeError(weekNumbers, "BYWEEKNO", min: 1, max: 53, allowNegative: true)
            ?? GetRangeError(BySetPositions, "BYSETPOS", min: 1, max: 366, allowNegative: true);
        if (error is not null)
            return error;

        if (weekDays is not null)
        {
            foreach (var weekDay in weekDays)
            {
                if (weekDay is < DayOfWeek.Sunday or > DayOfWeek.Saturday)
                    return $"BYDAY value '{weekDay}' is not a day of the week.";
            }
        }

        if (byDays is not null)
        {
            foreach (var byDay in byDays)
            {
                if (byDay.DayOfWeek is < DayOfWeek.Sunday or > DayOfWeek.Saturday)
                    return $"BYDAY value '{byDay.DayOfWeek}' is not a day of the week.";

                if (byDay.Ordinal is { } ordinal && ordinal is 0 or > 53 or < -53)
                    return $"BYDAY value '{byDay}' is invalid. The ordinal must be between 1 and 53 or between -53 and -1.";
            }
        }

        if (frequency is Frequency.Weekly && !IsEmpty(ByMonthDays))
            return "BYMONTHDAY cannot be used when FREQ is WEEKLY.";

        if (!IsEmpty(weekNumbers) && byDays is not null && byDays.Any(day => day.Ordinal.HasValue))
            return "BYDAY cannot have a numeric value when BYWEEKNO is specified.";

        // The rule parts of an unknown derived type are unknown, so it cannot be told whether BYSETPOS has another rule part to apply to
        if (frequency is not Frequency.None && !IsEmpty(BySetPositions) &&
            IsEmpty(BySeconds) && IsEmpty(ByMinutes) && IsEmpty(ByHours) && IsEmpty(ByMonths) && IsEmpty(ByMonthDays) &&
            IsEmpty(weekDays) && IsEmpty(byDays) && IsEmpty(yearDays) && IsEmpty(weekNumbers))
        {
            return "BYSETPOS can only be used in conjunction with another BYxxx rule part.";
        }

        return null;

        static string? GetRangeError(IList<int>? values, string partName, int min, int max, bool allowNegative)
        {
            if (values is null)
                return null;

            foreach (var value in values)
            {
                if ((value >= min && value <= max) || (allowNegative && value <= -min && value >= -max))
                    continue;

                var range = allowNegative
                    ? $"between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)} or between -{max.ToString(CultureInfo.InvariantCulture)} and -{min.ToString(CultureInfo.InvariantCulture)}"
                    : $"between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}";
                return $"{partName} value '{value.ToString(CultureInfo.InvariantCulture)}' is invalid. Must be {range}.";
            }

            return null;
        }
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
    /// <remarks>A rule cannot express a fraction of a second, so every occurrence takes the fractional second of <paramref name="startDate"/>,
    /// as it takes the other time components the rule does not specify.</remarks>
    /// <exception cref="InvalidOperationException">A rule part holds a value that RFC 5545 does not allow, such as a <see cref="ByHours"/> value of 24. It is thrown when the enumeration starts.</exception>
    public virtual IEnumerable<DateTime> GetNextOccurrences(DateTime startDate)
    {
        return GetNextOccurrences(startDate, offset: null);
    }

    /// <summary>Gets all occurrences of the recurrence, reading <paramref name="startDate"/> as a wall-clock time at <paramref name="offset"/> when one is specified.</summary>
    /// <remarks>A fixed offset turns an instant UNTIL into a wall-clock time at that offset, so the occurrences are bounded by instant.</remarks>
    internal IEnumerable<DateTime> GetNextOccurrences(DateTime startDate, TimeSpan? offset)
    {
        EnsureValid();
        if (Occurrences is 0)
            yield break;

        DateTime? endDate;
        if (offset is { } value && EndBound is { Kind: not DateTimeKind.Unspecified } instant)
        {
            var ticks = instant.ToUniversalTime().Ticks + value.Ticks;
            if (ticks < DateTime.MinValue.Ticks)
                yield break;

            endDate = new DateTime(Math.Min(ticks, DateTime.MaxValue.Ticks), DateTimeKind.Unspecified);
        }
        else
        {
            endDate = GetEndBound(EndBound, startDate.Kind);
        }

        var count = 0;
        foreach (var next in GetNextOccurrencesInternal(startDate, endDate))
        {
            if (endDate.HasValue && next > endDate.Value)
                yield break;

            yield return next;

            count++;
            if (Occurrences.HasValue && count >= Occurrences.Value)
                yield break;
        }

        // The occurrences have the kind of the start date. When both values denote instants, the bound is expressed in that
        // kind so the comparison is between instants; a wall-clock value on either side leaves nothing to convert.
        static DateTime? GetEndBound(DateTime? endDate, DateTimeKind startKind)
        {
            return endDate switch
            {
                null => null,
                { Kind: DateTimeKind.Unspecified } => endDate,
                { } value when startKind is DateTimeKind.Utc => value.ToUniversalTime(),
                { } value when startKind is DateTimeKind.Local => value.ToLocalTime(),
                _ => endDate,
            };
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
    /// <para>A time inside the gap of a forward transition therefore moves forward by the length of the gap, which can place
    /// its instance after the instances of the times that follow it: the occurrences are still returned in increasing
    /// order. An instance that denotes the same instant as another one, as the hour skipped by a forward transition does
    /// for an hourly recurrence, is ignored as RFC 5545 section 3.8.5.3 requires. COUNT counts the instances in the order
    /// the recurrence generates their wall-clock times, and a duplicate does not count again. Across a backward transition,
    /// the instants of the repeated hour are skipped.</para>
    /// <para>An occurrence outside the range of <see cref="DateTimeOffset"/> is not returned: the enumeration ends at the
    /// first one after the range.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A rule part holds a value that RFC 5545 does not allow, such as a <see cref="ByHours"/> value of 24. It is thrown when the enumeration starts.</exception>
    public virtual IEnumerable<DateTimeOffset> GetNextOccurrences(DateTime startDate, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return Iterate(this, DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified), timeZone);

        static IEnumerable<DateTimeOffset> Iterate(RecurrenceRule rule, DateTime wallClockStart, TimeZoneInfo timeZone)
        {
            rule.EnsureValid();
            if (rule.Occurrences is 0)
                yield break;

            var endDate = rule.EndBound;
            Func<DateTime, DateTimeOffset, bool>? isAfterEnd = endDate.HasValue ? (wallClock, occurrence) => IsAfterEndDate(wallClock, occurrence, endDate.Value) : null;
            var wallClockOccurrences = rule.GetNextOccurrencesInternal(wallClockStart, GetWallClockEndBound(endDate));
            foreach (var occurrence in Utilities.ToDateTimeOffsets(wallClockOccurrences, timeZone, rule.Occurrences, isAfterEnd))
            {
                yield return occurrence;
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
    /// <remarks>
    /// <para>The recurrence starts at the wall-clock reading of <paramref name="startDate"/>, as it would with a DTSTART;TZID=
    /// holding that reading, but no occurrence before <paramref name="startDate"/> is returned.</para>
    /// <para>Reducing an instant to a wall-clock time is lossy in the hour repeated by a backward transition, where both
    /// readings denote the same wall clock and RFC 5545 section 3.3.5 means the first one. When <paramref name="startDate"/>
    /// is the second one, the instances of the repeated hour denote instants before it and are not returned, although they
    /// still count towards COUNT. Use the <see cref="DateTime"/> overload to start from the first reading.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> is <see langword="null"/>.</exception>
    public IEnumerable<DateTimeOffset> GetNextOccurrences(DateTimeOffset startDate, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return Utilities.SkipBefore(GetNextOccurrences(Utilities.ToWallClockClamped(startDate, timeZone), timeZone), startDate);
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
