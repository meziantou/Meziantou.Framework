#if NETCOREAPP3_0_OR_GREATER
using System.Numerics;
#endif
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a cron expression.</summary>
/// <remarks>
/// Two expressions are equal when their fields select the same values, whatever their text: <c>*/15 * * * *</c> is equal to
/// <c>0,15,30,45 * * * *</c>, and <c>@daily</c> to <c>0 0 * * *</c>. Expressions that are written with different special
/// values, such as <c>0 0 29 2 *</c> and <c>0 0 L 2 *</c>, are not equal even when they happen to match the same dates.
/// An expression holds no time zone: it is evaluated in the time zone given to <c>GetNextOccurrences</c>.
/// </remarks>
public sealed class CronExpression : IRecurrenceRule, IEquatable<CronExpression>
#if NET7_0_OR_GREATER
    , IParsable<CronExpression>, ISpanParsable<CronExpression>
#endif
{
    // The first year an explicit year field can contain
    private const int MinYear = 1970;

    // The last year representable by DateTime
    private const int MaxYear = 9999;

    // The special values of the day-of-month field are stored as bits: bits 0-30 are L-0 to L-30 (L is L-0),
    // bit 31 is LW, and bits 32-62 are 1W to 31W.
    private const int DayOfMonthLastOffsetBit = 0;
    private const int DayOfMonthLastWeekdayBit = 31;

    // The special values of the day-of-week field are stored as bits: bits 0-6 are 0L to 6L,
    // and bits 7-41 are 0#1 to 6#5, five bits per day of the week.
    private const int DayOfWeekLastBit = 0;
    private const int DayOfWeekNthBit = 7;

    private static readonly string[] MonthNames = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
    private static readonly string[] DayOfWeekNames = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];

    private readonly string _expression;
    private readonly CronField _seconds;
    private readonly CronField _minutes;
    private readonly CronField _hours;
    private readonly CronField _dayOfMonth;
    private readonly CronField _month;
    private readonly CronField _dayOfWeek;
    private readonly CronField _year;

    private CronExpression(string expression, CronField seconds, CronField minutes, CronField hours, CronField dayOfMonth, CronField month, CronField dayOfWeek, CronField year)
    {
        _expression = expression;
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

        // Only the characters that separate the fields are trimmed, so that the fields and the ends of the expression accept the same whitespace
        expression = TrimFieldSeparators(expression);

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

            year = CronField.All;
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

            year = CronField.All;
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

        cronExpression = new CronExpression(expression.ToString(), seconds, minutes, hours, dayOfMonth, month, dayOfWeek, year);
        return true;
    }

    private static bool IsFieldSeparator(char c) => c is ' ' or '\t';

    private static ReadOnlySpan<char> TrimFieldSeparators(ReadOnlySpan<char> value)
    {
        var start = 0;
        while (start < value.Length && IsFieldSeparator(value[start]))
        {
            start++;
        }

        var end = value.Length;
        while (end > start && IsFieldSeparator(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }

    // Compares the text with an upper-case ASCII literal. Only ASCII letters are folded, so that no culture or Unicode case
    // mapping can match a look-alike character, such as 'ſ' for 'S' or 'ı' for 'I', on any target framework.
    private static bool EqualsAsciiIgnoreCase(ReadOnlySpan<char> value, string upperCaseText)
    {
        if (value.Length != upperCaseText.Length)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is >= 'a' and <= 'z')
            {
                c = (char)(c - ('a' - 'A'));
            }

            if (c != upperCaseText[i])
                return false;
        }

        return true;
    }

    private static bool TryParsePredefined(ReadOnlySpan<char> expression, [NotNullWhen(true)] out CronExpression? cronExpression)
    {
        cronExpression = null;

        var text = expression.ToString();
        if (EqualsAsciiIgnoreCase(expression, "@YEARLY") ||
            EqualsAsciiIgnoreCase(expression, "@ANNUALLY"))
        {
            // 0 0 1 1 *
            cronExpression = new CronExpression(
                text,
                CronField.CreateValue(CronFieldKind.Seconds, 0),
                CronField.CreateValue(CronFieldKind.Minutes, 0),
                CronField.CreateValue(CronFieldKind.Hours, 0),
                CronField.CreateValue(CronFieldKind.DayOfMonth, 1),
                CronField.CreateValue(CronFieldKind.Month, 1),
                CronField.All,
                CronField.All);
            return true;
        }

        if (EqualsAsciiIgnoreCase(expression, "@MONTHLY"))
        {
            // 0 0 1 * *
            cronExpression = new CronExpression(
                text,
                CronField.CreateValue(CronFieldKind.Seconds, 0),
                CronField.CreateValue(CronFieldKind.Minutes, 0),
                CronField.CreateValue(CronFieldKind.Hours, 0),
                CronField.CreateValue(CronFieldKind.DayOfMonth, 1),
                CronField.All,
                CronField.All,
                CronField.All);
            return true;
        }

        if (EqualsAsciiIgnoreCase(expression, "@WEEKLY"))
        {
            // 0 0 * * 0 (Sunday)
            cronExpression = new CronExpression(
                text,
                CronField.CreateValue(CronFieldKind.Seconds, 0),
                CronField.CreateValue(CronFieldKind.Minutes, 0),
                CronField.CreateValue(CronFieldKind.Hours, 0),
                CronField.All,
                CronField.All,
                CronField.CreateValue(CronFieldKind.DayOfWeek, 0),
                CronField.All);
            return true;
        }

        if (EqualsAsciiIgnoreCase(expression, "@DAILY") ||
            EqualsAsciiIgnoreCase(expression, "@MIDNIGHT"))
        {
            // 0 0 * * *
            cronExpression = new CronExpression(
                text,
                CronField.CreateValue(CronFieldKind.Seconds, 0),
                CronField.CreateValue(CronFieldKind.Minutes, 0),
                CronField.CreateValue(CronFieldKind.Hours, 0),
                CronField.All,
                CronField.All,
                CronField.All,
                CronField.All);
            return true;
        }

        if (EqualsAsciiIgnoreCase(expression, "@HOURLY"))
        {
            // 0 * * * *
            cronExpression = new CronExpression(
                text,
                CronField.CreateValue(CronFieldKind.Seconds, 0),
                CronField.CreateValue(CronFieldKind.Minutes, 0),
                CronField.All,
                CronField.All,
                CronField.All,
                CronField.All,
                CronField.All);
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
            result = CronField.All;
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
            // L is the same as L-0
            if (EqualsAsciiIgnoreCase(part, "L"))
            {
                builder.AddSpecial(DayOfMonthLastOffsetBit);
                return true;
            }

            if (EqualsAsciiIgnoreCase(part, "LW"))
            {
                builder.AddSpecial(DayOfMonthLastWeekdayBit);
                return true;
            }

            if (part.Length > 2 && part[0] is 'L' or 'l' && part[1] == '-')
            {
                if (!TryParseInt(part[2..], out var offset) || offset > 30)
                    return false;

                builder.AddSpecial(DayOfMonthLastOffsetBit + offset);
                return true;
            }

            // Handle nW (nearest weekday)
            if (part.Length > 1 && part[^1] is 'W' or 'w')
            {
                if (!TryParseInt(part[..^1], out var day) || day is < 1 or > 31)
                    return false;

                builder.AddSpecial(DayOfMonthLastWeekdayBit + day);
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

                builder.AddSpecial(DayOfWeekLastBit + (dow % 7));
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

                builder.AddSpecial(GetNthDayOfWeekBit(dow % 7, nth));
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

            // n/step means n, n+step, n+2*step, ... up to the maximum value of the field. The day-of-week field
            // ends on 7, the other value of Sunday, so that n/step is the same as n-7/step.
            end = slashIndex < 0 ? start : kind is CronFieldKind.DayOfWeek ? 7 : GetMaxValue(kind);
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
        if (TryParseName(value, MonthNames, out result))
        {
            result++;
            return true;
        }

        return TryParseInt(value, out result) && result >= 1 && result <= 12;
    }

    private static bool TryParseDayOfWeek(ReadOnlySpan<char> value, out int result)
    {
        if (TryParseName(value, DayOfWeekNames, out result))
            return true;

        // 0 and 7 both denote Sunday. 7 is kept as is so that a range such as 1-7 or 0-7/2 ends on it; the field stores it as 0.
        return TryParseInt(value, out result) && result >= 0 && result <= 7;
    }

    // Gets the index of the name in the list
    private static bool TryParseName(ReadOnlySpan<char> value, string[] names, out int result)
    {
        for (var i = 0; i < names.Length; i++)
        {
            if (EqualsAsciiIgnoreCase(value, names[i]))
            {
                result = i;
                return true;
            }
        }

        result = 0;
        return false;
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
        CronFieldKind.Year => MinYear,
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
        CronFieldKind.Year => MaxYear,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Returns the text of the expression, as it was parsed without its leading and trailing spaces and tabs.</summary>
    /// <returns>The text of the expression.</returns>
    public override string ToString()
    {
        return _expression;
    }

    /// <summary>Determines whether this expression selects the same values as another one in every field.</summary>
    /// <param name="other">The expression to compare with.</param>
    /// <returns><see langword="true"/> if both expressions select the same values; otherwise, <see langword="false"/>.</returns>
    /// <remarks>The text of the expressions is not compared: <c>*/15 * * * *</c> is equal to <c>0,15,30,45 * * * *</c>.</remarks>
    public bool Equals([NotNullWhen(true)] CronExpression? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return _seconds.FieldEquals(other._seconds)
            && _minutes.FieldEquals(other._minutes)
            && _hours.FieldEquals(other._hours)
            && _dayOfMonth.FieldEquals(other._dayOfMonth)
            && _month.FieldEquals(other._month)
            && _dayOfWeek.FieldEquals(other._dayOfWeek)
            && _year.FieldEquals(other._year);
    }

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return Equals(obj as CronExpression);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        _seconds.AddToHashCode(ref hash);
        _minutes.AddToHashCode(ref hash);
        _hours.AddToHashCode(ref hash);
        _dayOfMonth.AddToHashCode(ref hash);
        _month.AddToHashCode(ref hash);
        _dayOfWeek.AddToHashCode(ref hash);
        _year.AddToHashCode(ref hash);
        return hash.ToHashCode();
    }

    /// <summary>Determines whether two expressions select the same values in every field.</summary>
    public static bool operator ==(CronExpression? left, CronExpression? right) => Equals(left, right);

    /// <summary>Determines whether two expressions select different values in at least one field.</summary>
    public static bool operator !=(CronExpression? left, CronExpression? right) => !Equals(left, right);

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
    /// <para>A time inside the gap of a forward transition therefore moves forward by the length of the gap, which can place
    /// its occurrence after the occurrences of the times that follow it: the occurrences are still returned in increasing
    /// order. An occurrence that denotes the same instant as another one, as the hour skipped by a forward transition does
    /// for an hourly expression, is returned once. Across a backward transition, the instants of the repeated hour are
    /// skipped.</para>
    /// <para>An occurrence outside the range of <see cref="DateTimeOffset"/> is not returned: the enumeration ends at the
    /// first one after the range.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> is <see langword="null"/>.</exception>
    public IEnumerable<DateTimeOffset> GetNextOccurrences(DateTime startDate, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return Utilities.ToDateTimeOffsets(GetNextOccurrences(DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified)), timeZone);
    }

    /// <summary>Gets all occurrences of the expression, starting from the instant <paramref name="startDate"/> denotes.</summary>
    /// <param name="startDate">The instant to start generating occurrences from.</param>
    /// <param name="timeZone">The time zone the expression is evaluated in.</param>
    /// <returns>An enumerable sequence of occurrences, each carrying the UTC offset in effect at that occurrence.</returns>
    /// <remarks>
    /// <para>Every occurrence at or after <paramref name="startDate"/> is returned, and none before it. This includes an
    /// occurrence whose wall-clock time is before the reading of <paramref name="startDate"/>, but which a forward
    /// transition moves after it, and excludes the first reading of a wall-clock time in the hour repeated by a backward
    /// transition when <paramref name="startDate"/> is in its second pass.</para>
    /// <para>The resolution of invalid and ambiguous local times is the same as for the <see cref="DateTime"/> overload.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> is <see langword="null"/>.</exception>
    public IEnumerable<DateTimeOffset> GetNextOccurrences(DateTimeOffset startDate, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        // An expression has no anchor, so starting from an earlier wall-clock time only produces occurrences that are skipped
        return Utilities.SkipBefore(GetNextOccurrences(Utilities.GetEarliestWallClock(startDate, timeZone), timeZone), startDate);
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

        // Each special value is a distinct bit, so a repeated item is evaluated once and there are at most 63 of them
        var specials = _dayOfMonth.Specials;
        while (specials is not 0)
        {
            var bit = TrailingZeroCount(specials);
            specials &= specials - 1;

            var day = bit switch
            {
                < DayOfMonthLastWeekdayBit => daysInMonth - (bit - DayOfMonthLastOffsetBit),
                DayOfMonthLastWeekdayBit => GetLastWeekday(daysInMonth, firstDayOfWeek),
                _ => GetNearestWeekday(bit - DayOfMonthLastWeekdayBit, daysInMonth, firstDayOfWeek),
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

        // Each special value is a distinct bit, so a repeated item is evaluated once and there are at most 42 of them
        var specials = _dayOfWeek.Specials;
        while (specials is not 0)
        {
            var bit = TrailingZeroCount(specials);
            specials &= specials - 1;

            int day;
            if (bit < DayOfWeekNthBit)
            {
                var dayOfWeek = bit - DayOfWeekLastBit;
                day = daysInMonth - ((GetDayOfWeek(daysInMonth, firstDayOfWeek) - dayOfWeek + 7) % 7);
            }
            else
            {
                var dayOfWeek = (bit - DayOfWeekNthBit) / 5;
                var nth = ((bit - DayOfWeekNthBit) % 5) + 1;
                day = 1 + ((dayOfWeek - firstDayOfWeek + 7) % 7) + (7 * (nth - 1));
            }

            if (day <= daysInMonth)
            {
                mask |= 1UL << day;
            }
        }

        return mask;
    }

    private static int GetNthDayOfWeekBit(int dayOfWeek, int nth) => DayOfWeekNthBit + (dayOfWeek * 5) + (nth - 1);

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


    // Values are stored as a bitmask: bit n is set when the value n is part of the field. The year field
    // (1970-9999, 8030 values) uses an array of 64-bit words instead, where bit n is set for the year 1970 + n.
    [StructLayout(LayoutKind.Auto)]
    private struct CronFieldBuilder
    {
        private const int YearWordCount = ((MaxYear - MinYear) >> 6) + 1;

        private readonly CronFieldKind _kind;
        private ulong _bits;
        private ulong[]? _yearWords;
        private ulong _specials;
        private bool _isAll;

        public CronFieldBuilder(CronFieldKind kind)
        {
            _kind = kind;
        }

        public void SetAll() => _isAll = true;

        // A special value is a bit, so that repeating it does not make the field larger
        public void AddSpecial(int bit) => _specials |= 1UL << bit;

        // Adds start, start+step, ... up to end. When end is lower than start, the range wraps around
        // the end of the field: 22-2 in the hour field means 22, 23, 0, 1, 2. Years never wrap.
        public void AddRange(int start, int end, int step)
        {
            if (_kind is CronFieldKind.Year)
            {
                var words = _yearWords ??= new ulong[YearWordCount];
                if (step is 1)
                {
                    SetYearRange(words, start - MinYear, end - MinYear);
                    return;
                }

                for (long i = start; i <= end; i += step)
                {
                    var index = (int)i - MinYear;
                    words[index >> 6] |= 1UL << (index & 63);
                }

                return;
            }

            var min = GetMinValue(_kind);
            var max = GetMaxValue(_kind);
            var size = max - min + 1;
            long last = end < start ? end + size : end;
            for (long i = start; i <= last; i += step)
            {
                var value = (int)(i > max ? i - size : i);
                _bits |= 1UL << value;
            }
        }

        private static void SetYearRange(ulong[] words, int fromIndex, int toIndex)
        {
            var fromWord = fromIndex >> 6;
            var toWord = toIndex >> 6;
            for (var wordIndex = fromWord; wordIndex <= toWord; wordIndex++)
            {
                var mask = ulong.MaxValue;
                if (wordIndex == fromWord)
                {
                    mask &= ulong.MaxValue << (fromIndex & 63);
                }

                if (wordIndex == toWord)
                {
                    mask &= ulong.MaxValue >> (63 - (toIndex & 63));
                }

                words[wordIndex] |= mask;
            }
        }

        // Builds the field in a canonical form, so that fields selecting the same values are equal
        public readonly CronField Build()
        {
            if (_isAll)
                return CronField.All;

            if (_kind is CronFieldKind.Year)
            {
                // Only the words up to the last year are kept, so that a field of years close to 1970 stays small
                var words = _yearWords ?? [];
                var length = words.Length;
                while (length > 0 && words[length - 1] is 0)
                {
                    length--;
                }

                var trimmedWords = new ulong[length];
                Array.Copy(words, trimmedWords, length);
                return CronField.CreateYear(trimmedWords);
            }

            var bits = _bits;
            var specials = _specials;
            if (_kind is CronFieldKind.DayOfWeek)
            {
                for (var dayOfWeek = 0; dayOfWeek < 7; dayOfWeek++)
                {
                    // n#1 to n#5 are every n
                    var nthBits = 0x1FUL << GetNthDayOfWeekBit(dayOfWeek, 1);
                    if ((specials & nthBits) == nthBits)
                    {
                        bits |= 1UL << dayOfWeek;
                    }

                    // nL and n#m are included in n
                    if ((bits & (1UL << dayOfWeek)) is not 0)
                    {
                        specials &= ~(nthBits | (1UL << (DayOfWeekLastBit + dayOfWeek)));
                    }
                }
            }

            // Every value of the field is the same as any value, whatever the special values add
            var min = GetMinValue(_kind);
            var max = GetMaxValue(_kind);
            var allBits = (ulong.MaxValue >> (63 - max)) & (ulong.MaxValue << min);
            if (bits == allBits)
                return CronField.All;

            return new CronField(bits, specials);
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct CronField
    {
        // The years of a year field, null for the other fields
        private readonly ulong[]? _yearWords;

        public CronField(ulong bits, ulong specials)
        {
            Bits = bits;
            Specials = specials;
        }

        private CronField(bool isAll, ulong[]? yearWords)
        {
            IsAll = isAll;
            _yearWords = yearWords;
        }

        public bool IsAll { get; }

        // The values of the field, for every field except the year
        public ulong Bits { get; }

        // The special values of the day-of-month and day-of-week fields
        public ulong Specials { get; }

        public static CronField All => new(isAll: true, yearWords: null);

        public static CronField CreateYear(ulong[] yearWords) => new(isAll: false, yearWords);

        public static CronField CreateValue(CronFieldKind kind, int value)
        {
            var builder = new CronFieldBuilder(kind);
            builder.AddRange(value, value, 1);
            return builder.Build();
        }

        // Gets the smallest value of the field greater than or equal to value, or -1 if there is none.
        public int GetNext(int value)
        {
            if (IsAll)
                return value;

            if (_yearWords is not null)
                return GetNextYear(_yearWords, value);

            if (value >= 64)
                return -1;

            var bits = Bits & (ulong.MaxValue << value);
            return bits is 0 ? -1 : TrailingZeroCount(bits);
        }

        private static int GetNextYear(ulong[] yearWords, int value)
        {
            var index = Math.Max(value - MinYear, 0);
            var firstWord = index >> 6;
            for (var wordIndex = firstWord; wordIndex < yearWords.Length; wordIndex++)
            {
                var word = yearWords[wordIndex];
                if (wordIndex == firstWord)
                {
                    word &= ulong.MaxValue << (index & 63);
                }

                if (word is not 0)
                    return MinYear + (wordIndex << 6) + TrailingZeroCount(word);
            }

            return -1;
        }

        public bool FieldEquals(CronField other)
        {
            if (IsAll != other.IsAll || Bits != other.Bits || Specials != other.Specials)
                return false;

            if (_yearWords is null || other._yearWords is null)
                return _yearWords is null && other._yearWords is null;

            return _yearWords.AsSpan().SequenceEqual(other._yearWords);
        }

        public void AddToHashCode(ref HashCode hash)
        {
            hash.Add(IsAll);
            hash.Add(Bits);
            hash.Add(Specials);
            if (_yearWords is not null)
            {
                foreach (var word in _yearWords)
                {
                    hash.Add(word);
                }
            }
        }
    }
}
