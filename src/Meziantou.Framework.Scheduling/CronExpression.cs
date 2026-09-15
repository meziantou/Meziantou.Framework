#if NETCOREAPP3_0_OR_GREATER
using System.Numerics;
#endif
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Scheduling;

public sealed class CronExpression : IRecurrenceRule
#if NET7_0_OR_GREATER
    , IParsable<CronExpression>, ISpanParsable<CronExpression>
#endif
{
    // The last year representable by DateTime
    private const int MaxYear = 9999;

    private readonly CronField _seconds;
    private readonly CronField _minutes;
    private readonly CronField _hours;
    private readonly CronField _dayOfMonth;
    private readonly CronField _month;
    private readonly CronField _dayOfWeek;
    private readonly CronField _year;

    private CronExpression(CronField seconds, CronField minutes, CronField hours, CronField dayOfMonth, CronField month, CronField dayOfWeek, CronField year)
    {
        _seconds = seconds;
        _minutes = minutes;
        _hours = hours;
        _dayOfMonth = dayOfMonth;
        _month = month;
        _dayOfWeek = dayOfWeek;
        _year = year;
    }

    public static CronExpression Parse(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return Parse(expression.AsSpan());
    }

    public static bool TryParse([NotNullWhen(true)] string? expression, [NotNullWhen(true)] out CronExpression? cronExpression)
    {
        if (expression is null)
        {
            cronExpression = null;
            return false;
        }

        return TryParse(expression.AsSpan(), out cronExpression);
    }

    public static CronExpression Parse(ReadOnlySpan<char> expression)
    {
        if (TryParse(expression, out var cronExpression))
            return cronExpression;

        throw new FormatException($"The cron expression '{expression}' is not valid.");
    }

    public static bool TryParse(ReadOnlySpan<char> expression, [NotNullWhen(true)] out CronExpression? cronExpression)
    {
        cronExpression = null;

        if (expression.IsEmpty)
            return false;

        expression = expression.Trim();

        // Handle predefined schedules
        if (expression.Length > 0 && expression[0] == '@')
        {
            return TryParsePredefined(expression, out cronExpression);
        }

        // Split by spaces and tabs
        Span<Range> ranges = stackalloc Range[7];
        var count = 0;
        var index = 0;
        while (index < expression.Length)
        {
            if (IsFieldSeparator(expression[index]))
            {
                index++;
                continue;
            }

            if (count == ranges.Length)
                return false;

            var fieldStart = index;
            while (index < expression.Length && !IsFieldSeparator(expression[index]))
            {
                index++;
            }

            ranges[count] = new Range(fieldStart, index);
            count++;
        }

        if (count < 5)
            return false;

        CronField seconds;
        CronField minutes;
        CronField hours;
        CronField dayOfMonth;
        CronField month;
        CronField dayOfWeek;
        CronField year;

        if (count is 5)
        {
            // Standard: min hour dom month dow
            seconds = CronField.CreateValue(CronFieldKind.Seconds, 0);

            if (!TryParseField(expression[ranges[0]], CronFieldKind.Minutes, out minutes))
                return false;
            if (!TryParseField(expression[ranges[1]], CronFieldKind.Hours, out hours))
                return false;
            if (!TryParseField(expression[ranges[2]], CronFieldKind.DayOfMonth, out dayOfMonth))
                return false;
            if (!TryParseField(expression[ranges[3]], CronFieldKind.Month, out month))
                return false;
            if (!TryParseField(expression[ranges[4]], CronFieldKind.DayOfWeek, out dayOfWeek))
                return false;

            year = CronField.CreateAll(CronFieldKind.Year);
        }
        else if (count is 6)
        {
            // With seconds: sec min hour dom month dow
            if (!TryParseField(expression[ranges[0]], CronFieldKind.Seconds, out seconds))
                return false;
            if (!TryParseField(expression[ranges[1]], CronFieldKind.Minutes, out minutes))
                return false;
            if (!TryParseField(expression[ranges[2]], CronFieldKind.Hours, out hours))
                return false;
            if (!TryParseField(expression[ranges[3]], CronFieldKind.DayOfMonth, out dayOfMonth))
                return false;
            if (!TryParseField(expression[ranges[4]], CronFieldKind.Month, out month))
                return false;
            if (!TryParseField(expression[ranges[5]], CronFieldKind.DayOfWeek, out dayOfWeek))
                return false;

            year = CronField.CreateAll(CronFieldKind.Year);
        }
        else // count == 7
        {
            // With seconds and year: sec min hour dom month dow year
            if (!TryParseField(expression[ranges[0]], CronFieldKind.Seconds, out seconds))
                return false;
            if (!TryParseField(expression[ranges[1]], CronFieldKind.Minutes, out minutes))
                return false;
            if (!TryParseField(expression[ranges[2]], CronFieldKind.Hours, out hours))
                return false;
            if (!TryParseField(expression[ranges[3]], CronFieldKind.DayOfMonth, out dayOfMonth))
                return false;
            if (!TryParseField(expression[ranges[4]], CronFieldKind.Month, out month))
                return false;
            if (!TryParseField(expression[ranges[5]], CronFieldKind.DayOfWeek, out dayOfWeek))
                return false;
            if (!TryParseField(expression[ranges[6]], CronFieldKind.Year, out year))
                return false;
        }

        cronExpression = new CronExpression(seconds, minutes, hours, dayOfMonth, month, dayOfWeek, year);
        return true;
    }

    private static bool IsFieldSeparator(char c) => c is ' ' or '\t';

    private static bool TryParsePredefined(ReadOnlySpan<char> expression, [NotNullWhen(true)] out CronExpression? cronExpression)
    {
        cronExpression = null;

        if (expression.Equals("@yearly", StringComparison.OrdinalIgnoreCase) ||
            expression.Equals("@annually", StringComparison.OrdinalIgnoreCase))
        {
            // 0 0 1 1 *
            cronExpression = new CronExpression(
                CronField.CreateValue(CronFieldKind.Seconds, 0),
                CronField.CreateValue(CronFieldKind.Minutes, 0),
                CronField.CreateValue(CronFieldKind.Hours, 0),
                CronField.CreateValue(CronFieldKind.DayOfMonth, 1),
                CronField.CreateValue(CronFieldKind.Month, 1),
                CronField.CreateAll(CronFieldKind.DayOfWeek),
                CronField.CreateAll(CronFieldKind.Year));
            return true;
        }

        if (expression.Equals("@monthly", StringComparison.OrdinalIgnoreCase))
        {
            // 0 0 1 * *
            cronExpression = new CronExpression(
                CronField.CreateValue(CronFieldKind.Seconds, 0),
                CronField.CreateValue(CronFieldKind.Minutes, 0),
                CronField.CreateValue(CronFieldKind.Hours, 0),
                CronField.CreateValue(CronFieldKind.DayOfMonth, 1),
                CronField.CreateAll(CronFieldKind.Month),
                CronField.CreateAll(CronFieldKind.DayOfWeek),
                CronField.CreateAll(CronFieldKind.Year));
            return true;
        }

        if (expression.Equals("@weekly", StringComparison.OrdinalIgnoreCase))
        {
            // 0 0 * * 0 (Sunday)
            cronExpression = new CronExpression(
                CronField.CreateValue(CronFieldKind.Seconds, 0),
                CronField.CreateValue(CronFieldKind.Minutes, 0),
                CronField.CreateValue(CronFieldKind.Hours, 0),
                CronField.CreateAll(CronFieldKind.DayOfMonth),
                CronField.CreateAll(CronFieldKind.Month),
                CronField.CreateValue(CronFieldKind.DayOfWeek, 0),
                CronField.CreateAll(CronFieldKind.Year));
            return true;
        }

        if (expression.Equals("@daily", StringComparison.OrdinalIgnoreCase) ||
            expression.Equals("@midnight", StringComparison.OrdinalIgnoreCase))
        {
            // 0 0 * * *
            cronExpression = new CronExpression(
                CronField.CreateValue(CronFieldKind.Seconds, 0),
                CronField.CreateValue(CronFieldKind.Minutes, 0),
                CronField.CreateValue(CronFieldKind.Hours, 0),
                CronField.CreateAll(CronFieldKind.DayOfMonth),
                CronField.CreateAll(CronFieldKind.Month),
                CronField.CreateAll(CronFieldKind.DayOfWeek),
                CronField.CreateAll(CronFieldKind.Year));
            return true;
        }

        if (expression.Equals("@hourly", StringComparison.OrdinalIgnoreCase))
        {
            // 0 * * * *
            cronExpression = new CronExpression(
                CronField.CreateValue(CronFieldKind.Seconds, 0),
                CronField.CreateValue(CronFieldKind.Minutes, 0),
                CronField.CreateAll(CronFieldKind.Hours),
                CronField.CreateAll(CronFieldKind.DayOfMonth),
                CronField.CreateAll(CronFieldKind.Month),
                CronField.CreateAll(CronFieldKind.DayOfWeek),
                CronField.CreateAll(CronFieldKind.Year));
            return true;
        }

        return false;
    }

    private static bool TryParseField(ReadOnlySpan<char> field, CronFieldKind kind, out CronField result)
    {
        result = default;

        if (field.IsEmpty)
            return false;

        // Handle ? (any), which is only valid as the whole field
        if (field is "?")
        {
            result = CronField.CreateAll(kind);
            return true;
        }

        // Handle list (comma-separated)
        var builder = new CronFieldBuilder(kind);
        var remaining = field;
        while (true)
        {
            var commaIndex = remaining.IndexOf(',');
            var part = commaIndex >= 0 ? remaining[..commaIndex] : remaining;
            if (!TryParseFieldPart(part, kind, ref builder))
                return false;

            if (commaIndex < 0)
                break;

            remaining = remaining[(commaIndex + 1)..];
        }

        result = builder.Build();
        return true;
    }

    private static bool TryParseFieldPart(ReadOnlySpan<char> part, CronFieldKind kind, ref CronFieldBuilder builder)
    {
        if (part.IsEmpty)
            return false;

        // Handle * (all)
        if (part is "*")
        {
            builder.SetAll();
            return true;
        }

        // Handle */step
        if (part.Length > 2 && part[0] == '*' && part[1] == '/')
        {
            if (!TryParseInt(part[2..], out var allStep) || allStep <= 0)
                return false;

            builder.AddRange(GetMinValue(kind), GetMaxValue(kind), allStep);
            return true;
        }

        // Handle special day of month cases: L, LW, L-n, nW
        if (kind is CronFieldKind.DayOfMonth)
        {
            if (part.Equals("L", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddSpecial(new CronFieldValue { Kind = CronValueKind.Last });
                return true;
            }

            if (part.Equals("LW", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddSpecial(new CronFieldValue { Kind = CronValueKind.LastWeekday });
                return true;
            }

            if (part.Length > 2 && part[0] is 'L' or 'l' && part[1] == '-')
            {
                if (!TryParseInt(part[2..], out var offset) || offset > 30)
                    return false;

                builder.AddSpecial(new CronFieldValue { Kind = CronValueKind.LastOffset, Value = offset });
                return true;
            }

            // Handle nW (nearest weekday)
            if (part.Length > 1 && part[^1] is 'W' or 'w')
            {
                if (!TryParseInt(part[..^1], out var day) || day is < 1 or > 31)
                    return false;

                builder.AddSpecial(new CronFieldValue { Kind = CronValueKind.NearestWeekday, Value = day });
                return true;
            }
        }

        // Handle special day of week cases: nL (last occurrence), n#m (nth occurrence)
        if (kind is CronFieldKind.DayOfWeek)
        {
            // Handle nL (last day of week in month)
            if (part.Length >= 2 && part[^1] is 'L' or 'l')
            {
                if (!TryParseDayOfWeek(part[..^1], out var dow))
                    return false;

                builder.AddSpecial(new CronFieldValue { Kind = CronValueKind.LastDayOfWeek, Value = dow % 7 });
                return true;
            }

            // Handle n#m (nth occurrence of day)
            var hashIndex = part.IndexOf('#');
            if (hashIndex > 0 && hashIndex < part.Length - 1)
            {
                if (!TryParseDayOfWeek(part[..hashIndex], out var dow))
                    return false;
                if (!TryParseInt(part[(hashIndex + 1)..], out var nth) || nth < 1 || nth > 5)
                    return false;

                builder.AddSpecial(new CronFieldValue { Kind = CronValueKind.NthDayOfWeek, Value = dow % 7, NthValue = nth });
                return true;
            }
        }

        // Handle value or range with optional step: n, start-end, start-end/step or start/step
        var rangePart = part;
        var step = 1;
        var slashIndex = part.IndexOf('/');
        if (slashIndex >= 0)
        {
            if (!TryParseInt(part[(slashIndex + 1)..], out step) || step <= 0)
                return false;

            rangePart = part[..slashIndex];
        }

        int start;
        int end;
        var dashIndex = rangePart.IndexOf('-');
        if (dashIndex >= 0)
        {
            if (!TryParseValue(rangePart[..dashIndex], kind, out start))
                return false;
            if (!TryParseValue(rangePart[(dashIndex + 1)..], kind, out end))
                return false;

            // A reversed range wraps around the end of the field, except for years which do not cycle
            if (end < start && kind is CronFieldKind.Year)
                return false;
        }
        else
        {
            if (!TryParseValue(rangePart, kind, out start))
                return false;

            // n/step means n, n+step, n+2*step, ... up to the maximum value of the field
            end = slashIndex >= 0 ? GetMaxValue(kind) : start;
        }

        builder.AddRange(start, end, step);
        return true;
    }

    private static bool TryParseValue(ReadOnlySpan<char> value, CronFieldKind kind, out int result)
    {
        if (kind is CronFieldKind.Month)
        {
            if (TryParseMonth(value, out result))
                return true;
        }

        if (kind is CronFieldKind.DayOfWeek)
        {
            if (TryParseDayOfWeek(value, out result))
                return true;
        }

        if (TryParseInt(value, out result))
        {
            var min = GetMinValue(kind);
            var max = GetMaxValue(kind);
            return result >= min && result <= max;
        }

        return false;
    }

    private static bool TryParseMonth(ReadOnlySpan<char> value, out int result)
    {
        if (value.Equals("JAN", StringComparison.OrdinalIgnoreCase)) { result = 1; return true; }
        if (value.Equals("FEB", StringComparison.OrdinalIgnoreCase)) { result = 2; return true; }
        if (value.Equals("MAR", StringComparison.OrdinalIgnoreCase)) { result = 3; return true; }
        if (value.Equals("APR", StringComparison.OrdinalIgnoreCase)) { result = 4; return true; }
        if (value.Equals("MAY", StringComparison.OrdinalIgnoreCase)) { result = 5; return true; }
        if (value.Equals("JUN", StringComparison.OrdinalIgnoreCase)) { result = 6; return true; }
        if (value.Equals("JUL", StringComparison.OrdinalIgnoreCase)) { result = 7; return true; }
        if (value.Equals("AUG", StringComparison.OrdinalIgnoreCase)) { result = 8; return true; }
        if (value.Equals("SEP", StringComparison.OrdinalIgnoreCase)) { result = 9; return true; }
        if (value.Equals("OCT", StringComparison.OrdinalIgnoreCase)) { result = 10; return true; }
        if (value.Equals("NOV", StringComparison.OrdinalIgnoreCase)) { result = 11; return true; }
        if (value.Equals("DEC", StringComparison.OrdinalIgnoreCase)) { result = 12; return true; }

        return TryParseInt(value, out result) && result >= 1 && result <= 12;
    }

    private static bool TryParseDayOfWeek(ReadOnlySpan<char> value, out int result)
    {
        if (value.Equals("SUN", StringComparison.OrdinalIgnoreCase)) { result = 0; return true; }
        if (value.Equals("MON", StringComparison.OrdinalIgnoreCase)) { result = 1; return true; }
        if (value.Equals("TUE", StringComparison.OrdinalIgnoreCase)) { result = 2; return true; }
        if (value.Equals("WED", StringComparison.OrdinalIgnoreCase)) { result = 3; return true; }
        if (value.Equals("THU", StringComparison.OrdinalIgnoreCase)) { result = 4; return true; }
        if (value.Equals("FRI", StringComparison.OrdinalIgnoreCase)) { result = 5; return true; }
        if (value.Equals("SAT", StringComparison.OrdinalIgnoreCase)) { result = 6; return true; }

        // 0 and 7 both denote Sunday. 7 is kept as is so that a range such as 1-7 or 0-7/2 ends on it; the field stores it as 0.
        return TryParseInt(value, out result) && result >= 0 && result <= 7;
    }

    private static bool TryParseInt(ReadOnlySpan<char> value, out int result)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    private static int GetMinValue(CronFieldKind kind) => kind switch
    {
        CronFieldKind.Seconds => 0,
        CronFieldKind.Minutes => 0,
        CronFieldKind.Hours => 0,
        CronFieldKind.DayOfMonth => 1,
        CronFieldKind.Month => 1,
        CronFieldKind.DayOfWeek => 0,
        CronFieldKind.Year => 1970,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static int GetMaxValue(CronFieldKind kind) => kind switch
    {
        CronFieldKind.Seconds => 59,
        CronFieldKind.Minutes => 59,
        CronFieldKind.Hours => 23,
        CronFieldKind.DayOfMonth => 31,
        CronFieldKind.Month => 12,
        CronFieldKind.DayOfWeek => 6,
        CronFieldKind.Year => 2099,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // Values of a field are stored as bits relative to this offset. Days and months use their value as the
    // bit index, so that a day-of-month field can be combined with a day mask without shifting.
    private static int GetOffset(CronFieldKind kind) => kind is CronFieldKind.Year ? GetMinValue(kind) : 0;

    public IEnumerable<DateTime> GetNextOccurrences(DateTime startDate)
    {
        var kind = startDate.Kind;
        var ticks = startDate.Ticks;

        // Occurrences are whole seconds, so a start with a fractional second begins at the next whole second
        var fraction = ticks % TimeSpan.TicksPerSecond;
        if (fraction is not 0)
        {
            if (DateTime.MaxValue.Ticks - ticks < TimeSpan.TicksPerSecond - fraction)
                yield break;

            ticks += TimeSpan.TicksPerSecond - fraction;
        }

        var cursor = new OccurrenceCursor(new DateTime(ticks, kind));
        if (!TryMoveToOccurrence(ref cursor))
            yield break;

        while (true)
        {
            yield return cursor.ToDateTime(kind);

            // Fast path: the next occurrence is in the same minute, so the other fields still match
            if (cursor.Second < 59)
            {
                var second = _seconds.GetNext(cursor.Second + 1);
                if (second >= 0)
                {
                    cursor.Second = second;
                    continue;
                }
            }

            cursor.Second++;
            if (!TryMoveToOccurrence(ref cursor))
                yield break;
        }
    }

    /// <summary>Gets all occurrences of the expression, reading <paramref name="startDate"/> as a wall-clock time in <paramref name="timeZone"/>.</summary>
    /// <param name="startDate">The wall-clock time to start generating occurrences from. Its <see cref="DateTime.Kind"/> is ignored.</param>
    /// <param name="timeZone">The time zone the expression is evaluated in.</param>
    /// <returns>An enumerable sequence of occurrences, each carrying the UTC offset in effect at that occurrence.</returns>
    /// <remarks>
    /// <para>The occurrences keep their wall-clock time across a daylight saving transition, so their UTC offset changes.
    /// A local time made invalid or ambiguous by a transition is resolved as RFC 5545 section 3.3.5 requires: an ambiguous
    /// time keeps its first occurrence, and an invalid time is read with the UTC offset in effect before the gap.</para>
    /// <para>As a consequence, an hourly expression repeats an instant across a forward transition and skips the instants
    /// of the repeated hour across a backward one.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> is <see langword="null"/>.</exception>
    public IEnumerable<DateTimeOffset> GetNextOccurrences(DateTime startDate, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return Utilities.ToDateTimeOffsets(GetNextOccurrences(DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified)), timeZone);
    }

    /// <summary>Gets all occurrences of the expression, starting from the instant <paramref name="startDate"/> denotes.</summary>
    /// <param name="startDate">The instant to start generating occurrences from. It is reduced to a wall-clock time in <paramref name="timeZone"/>.</param>
    /// <param name="timeZone">The time zone the expression is evaluated in.</param>
    /// <returns>An enumerable sequence of occurrences, each carrying the UTC offset in effect at that occurrence.</returns>
    /// <remarks>Reducing an instant to a wall-clock time is lossy in the hour repeated by a backward transition, where both
    /// readings denote the same wall clock. Use the <see cref="DateTime"/> overload to control which one is meant.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> is <see langword="null"/>.</exception>
    public IEnumerable<DateTimeOffset> GetNextOccurrences(DateTimeOffset startDate, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return GetNextOccurrences(TimeZoneInfo.ConvertTime(startDate, timeZone).DateTime, timeZone);
    }

    // Moves the cursor to the first occurrence at or after its current position. The search ends when
    // no field can match anymore, or when it goes beyond the last year representable by DateTime.
    private bool TryMoveToOccurrence(ref OccurrenceCursor cursor)
    {
        while (true)
        {
            // Propagate the carries of the previous step
            if (cursor.Second > 59)
            {
                cursor.Second = 0;
                cursor.Minute++;
            }

            if (cursor.Minute > 59)
            {
                cursor.Minute = 0;
                cursor.Hour++;
            }

            if (cursor.Hour > 23)
            {
                cursor.Hour = 0;
                cursor.Day++;
            }

            if (cursor.Month > 12)
            {
                cursor.Month = 1;
                cursor.Year++;
            }

            if (cursor.Year > MaxYear)
                return false;

            // Check year
            var year = _year.GetNext(cursor.Year);
            if (year < 0 || year > MaxYear)
                return false;

            if (year != cursor.Year)
            {
                cursor.Year = year;
                cursor.Month = 1;
                cursor.ResetDay();
            }

            // Check month
            var month = _month.GetNext(cursor.Month);
            if (month < 0)
            {
                cursor.Year++;
                cursor.Month = 1;
                cursor.ResetDay();
                continue;
            }

            if (month != cursor.Month)
            {
                cursor.Month = month;
                cursor.ResetDay();
            }

            // Check day of month and day of week
            if (cursor.DayMaskYear != cursor.Year || cursor.DayMaskMonth != cursor.Month)
            {
                cursor.DayMask = GetDayMask(cursor.Year, cursor.Month);
                cursor.DayMaskYear = cursor.Year;
                cursor.DayMaskMonth = cursor.Month;
            }

            var remainingDays = cursor.DayMask >> cursor.Day;
            if (remainingDays is 0)
            {
                cursor.Month++;
                cursor.ResetDay();
                continue;
            }

            var day = cursor.Day + TrailingZeroCount(remainingDays);
            if (day != cursor.Day)
            {
                cursor.Day = day;
                cursor.ResetTime();
            }

            // Check hour
            var hour = _hours.GetNext(cursor.Hour);
            if (hour < 0)
            {
                cursor.Day++;
                cursor.ResetTime();
                continue;
            }

            if (hour != cursor.Hour)
            {
                cursor.Hour = hour;
                cursor.Minute = 0;
                cursor.Second = 0;
            }

            // Check minute
            var minute = _minutes.GetNext(cursor.Minute);
            if (minute < 0)
            {
                cursor.Hour++;
                cursor.Minute = 0;
                cursor.Second = 0;
                continue;
            }

            if (minute != cursor.Minute)
            {
                cursor.Minute = minute;
                cursor.Second = 0;
            }

            // Check second
            var second = _seconds.GetNext(cursor.Second);
            if (second < 0)
            {
                cursor.Minute++;
                cursor.Second = 0;
                continue;
            }

            cursor.Second = second;
            return true;
        }
    }

    // Gets the days of the month matching both the day-of-month and the day-of-week fields. Bit n is set when day n matches.
    private ulong GetDayMask(int year, int month)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var mask = (ulong.MaxValue >> (63 - daysInMonth)) & ~1UL;
        if (_dayOfMonth.IsAll && _dayOfWeek.IsAll)
            return mask;

        var firstDayOfWeek = (int)new DateTime(year, month, 1).DayOfWeek;
        if (!_dayOfMonth.IsAll)
        {
            mask &= GetDayOfMonthMask(daysInMonth, firstDayOfWeek);
        }

        if (!_dayOfWeek.IsAll)
        {
            mask &= GetDayOfWeekMask(daysInMonth, firstDayOfWeek);
        }

        return mask;
    }

    private ulong GetDayOfMonthMask(int daysInMonth, int firstDayOfWeek)
    {
        var mask = _dayOfMonth.Bits;
        foreach (var special in _dayOfMonth.Specials)
        {
            var day = special.Kind switch
            {
                CronValueKind.Last => daysInMonth,
                CronValueKind.LastOffset => daysInMonth - special.Value,
                CronValueKind.LastWeekday => GetLastWeekday(daysInMonth, firstDayOfWeek),
                CronValueKind.NearestWeekday => GetNearestWeekday(special.Value, daysInMonth, firstDayOfWeek),
                _ => -1,
            };

            if (day >= 1)
            {
                mask |= 1UL << day;
            }
        }

        return mask;
    }

    private ulong GetDayOfWeekMask(int daysInMonth, int firstDayOfWeek)
    {
        // Rotate the weekly pattern so that bit n matches the (n+1)-th day of the month, then repeat it over 5 weeks
        var week = _dayOfWeek.Bits & 0x7F;
        var rotated = ((week >> firstDayOfWeek) | (week << (7 - firstDayOfWeek))) & 0x7F;
        var mask = (rotated | (rotated << 7) | (rotated << 14) | (rotated << 21) | (rotated << 28)) << 1;

        foreach (var special in _dayOfWeek.Specials)
        {
            var day = special.Kind switch
            {
                CronValueKind.LastDayOfWeek => daysInMonth - ((GetDayOfWeek(daysInMonth, firstDayOfWeek) - special.Value + 7) % 7),
                CronValueKind.NthDayOfWeek => 1 + ((special.Value - firstDayOfWeek + 7) % 7) + (7 * (special.NthValue - 1)),
                _ => -1,
            };

            if (day >= 1 && day <= daysInMonth)
            {
                mask |= 1UL << day;
            }
        }

        return mask;
    }

    private static int GetDayOfWeek(int day, int firstDayOfWeek) => (firstDayOfWeek + day - 1) % 7;

    private static int GetLastWeekday(int daysInMonth, int firstDayOfWeek)
    {
        return GetDayOfWeek(daysInMonth, firstDayOfWeek) switch
        {
            (int)DayOfWeek.Saturday => daysInMonth - 1,
            (int)DayOfWeek.Sunday => daysInMonth - 2,
            _ => daysInMonth,
        };
    }

    private static int GetNearestWeekday(int targetDay, int daysInMonth, int firstDayOfWeek)
    {
        // If the target day doesn't exist in this month, there is no match
        if (targetDay > daysInMonth)
            return -1;

        return GetDayOfWeek(targetDay, firstDayOfWeek) switch
        {
            // Move to Friday if possible, otherwise Monday
            (int)DayOfWeek.Saturday => targetDay > 1 ? targetDay - 1 : targetDay + 2,

            // Move to Monday if possible, otherwise Friday
            (int)DayOfWeek.Sunday => targetDay < daysInMonth ? targetDay + 1 : targetDay - 2,
            _ => targetDay,
        };
    }

    private static int TrailingZeroCount(ulong value)
    {
#if NETCOREAPP3_0_OR_GREATER
        return BitOperations.TrailingZeroCount(value);
#else
        // De Bruijn sequence lookup. The value is never 0.
        return DeBruijnBitPositions[(int)(unchecked((value & (~value + 1)) * 0x03F79D71B4CB0A89UL) >> 58)];
#endif
    }

#if !NETCOREAPP3_0_OR_GREATER
    private static ReadOnlySpan<byte> DeBruijnBitPositions =>
    [
        0, 1, 48, 2, 57, 49, 28, 3, 61, 58, 50, 42, 38, 29, 17, 4,
        62, 55, 59, 36, 53, 51, 43, 22, 45, 39, 33, 30, 24, 18, 12, 5,
        63, 47, 56, 27, 60, 41, 37, 16, 54, 35, 52, 21, 44, 32, 23, 11,
        46, 26, 40, 15, 34, 20, 31, 10, 25, 14, 19, 9, 13, 8, 7, 6,
    ];
#endif
#if NET7_0_OR_GREATER
    static CronExpression IParsable<CronExpression>.Parse(string s, IFormatProvider? provider) => Parse(s);
    static bool IParsable<CronExpression>.TryParse(string? s, IFormatProvider? provider, [NotNullWhen(true)] out CronExpression? result) => TryParse(s, out result);
    static CronExpression ISpanParsable<CronExpression>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);
    static bool ISpanParsable<CronExpression>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [NotNullWhen(true)] out CronExpression? result) => TryParse(s, out result);
#endif

    private enum CronFieldKind
    {
        Seconds,
        Minutes,
        Hours,
        DayOfMonth,
        Month,
        DayOfWeek,
        Year,
    }

    private enum CronValueKind
    {
        Last,
        LastOffset,
        LastWeekday,
        NearestWeekday,
        LastDayOfWeek,
        NthDayOfWeek,
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct CronFieldValue
    {
        public CronValueKind Kind { get; init; }
        public int Value { get; init; }
        public int NthValue { get; init; }
    }

    [StructLayout(LayoutKind.Auto)]
    private struct OccurrenceCursor
    {
        public int Year;
        public int Month;
        public int Day;
        public int Hour;
        public int Minute;
        public int Second;

        public int DayMaskYear;
        public int DayMaskMonth;
        public ulong DayMask;

        private int _dateTicksYear;
        private int _dateTicksMonth;
        private int _dateTicksDay;
        private long _dateTicks;

        public OccurrenceCursor(DateTime value)
        {
            Year = value.Year;
            Month = value.Month;
            Day = value.Day;
            Hour = value.Hour;
            Minute = value.Minute;
            Second = value.Second;
        }

        public DateTime ToDateTime(DateTimeKind kind)
        {
            // Consecutive occurrences are often on the same day, so the ticks of the date are cached
            if (_dateTicksDay != Day || _dateTicksMonth != Month || _dateTicksYear != Year)
            {
                _dateTicks = new DateTime(Year, Month, Day).Ticks;
                _dateTicksYear = Year;
                _dateTicksMonth = Month;
                _dateTicksDay = Day;
            }

            return new DateTime(_dateTicks + (((Hour * 3600L) + (Minute * 60L) + Second) * TimeSpan.TicksPerSecond), kind);
        }

        public void ResetDay()
        {
            Day = 1;
            ResetTime();
        }

        public void ResetTime()
        {
            Hour = 0;
            Minute = 0;
            Second = 0;
        }
    }

    // Values are stored as a bitmask: bit n is set when the value (offset + n) is part of the field.
    // Only the year field (1970-2099, 130 values) needs more than one 64-bit word and a non-zero offset.
    [StructLayout(LayoutKind.Auto)]
    private struct CronFieldBuilder
    {
        private readonly CronFieldKind _kind;
        private ulong _word0;
        private ulong _word1;
        private ulong _word2;
        private bool _isAll;
        private List<CronFieldValue>? _specials;

        public CronFieldBuilder(CronFieldKind kind)
        {
            _kind = kind;
        }

        public void SetAll() => _isAll = true;

        public void AddSpecial(CronFieldValue value) => (_specials ??= []).Add(value);

        // Adds start, start+step, ... up to end. When end is lower than start, the range wraps around
        // the end of the field: 22-2 in the hour field means 22, 23, 0, 1, 2.
        public void AddRange(int start, int end, int step)
        {
            var min = GetMinValue(_kind);
            var max = GetMaxValue(_kind);
            var size = max - min + 1;
            long last = end < start ? end + size : end;
            for (long i = start; i <= last; i += step)
            {
                var value = (int)(i > max ? i - size : i);
                var index = value - GetOffset(_kind);
                var bit = 1UL << (index & 63);
                switch (index >> 6)
                {
                    case 0:
                        _word0 |= bit;
                        break;

                    case 1:
                        _word1 |= bit;
                        break;

                    default:
                        _word2 |= bit;
                        break;
                }
            }
        }

        public readonly CronField Build()
        {
            if (_isAll)
                return CronField.CreateAll(_kind);

            return new CronField(_kind, _word0, _word1, _word2, _specials?.ToArray() ?? []);
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct CronField
    {
        private const int MaxIndex = 3 * 64;

        private readonly int _offset;
        private readonly ulong _word1;
        private readonly ulong _word2;

        public CronField(CronFieldKind kind, ulong word0, ulong word1, ulong word2, CronFieldValue[] specials)
        {
            _offset = GetOffset(kind);
            Bits = word0;
            _word1 = word1;
            _word2 = word2;
            Specials = specials;
        }

        private CronField(CronFieldKind kind)
        {
            _offset = GetOffset(kind);
            IsAll = true;
            Specials = [];
        }

        public bool IsAll { get; }

        // Bits of the values lower than offset + 64, which covers every field except the year
        public ulong Bits { get; }

        public CronFieldValue[] Specials { get; }

        public static CronField CreateAll(CronFieldKind kind) => new(kind);

        public static CronField CreateValue(CronFieldKind kind, int value)
        {
            var builder = new CronFieldBuilder(kind);
            builder.AddRange(value, value, 1);
            return builder.Build();
        }

        // Gets the smallest value of the field greater than or equal to value, or -1 if there is none.
        public int GetNext(int value) => IsAll ? value : GetNextFromBits(value);

        private int GetNextFromBits(int value)
        {
            var index = Math.Max(value - _offset, 0);
            while (index < MaxIndex)
            {
                var word = (index >> 6) switch
                {
                    0 => Bits,
                    1 => _word1,
                    _ => _word2,
                };

                var bits = word & (ulong.MaxValue << (index & 63));
                if (bits is not 0)
                    return _offset + (index & ~63) + TrailingZeroCount(bits);

                index = (index | 63) + 1;
            }

            return -1;
        }
    }
}
