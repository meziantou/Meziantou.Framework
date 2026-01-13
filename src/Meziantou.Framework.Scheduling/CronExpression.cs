using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Scheduling;

public sealed class CronExpression : IRecurrenceRule, IParsable<CronExpression>, ISpanParsable<CronExpression>
{
    private readonly CronField _seconds;
    private readonly CronField _minutes;
    private readonly CronField _hours;
    private readonly CronField _dayOfMonth;
    private readonly CronField _month;
    private readonly CronField _dayOfWeek;
    private readonly CronField _year;
    private readonly bool _hasSeconds;
    private readonly bool _hasYear;

    private CronExpression(CronField seconds, CronField minutes, CronField hours, CronField dayOfMonth, CronField month, CronField dayOfWeek, CronField year, bool hasSeconds, bool hasYear)
    {
        _seconds = seconds;
        _minutes = minutes;
        _hours = hours;
        _dayOfMonth = dayOfMonth;
        _month = month;
        _dayOfWeek = dayOfWeek;
        _year = year;
        _hasSeconds = hasSeconds;
        _hasYear = hasYear;
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

        // Split by whitespace
        Span<Range> ranges = stackalloc Range[8];
        var count = expression.Split(ranges, ' ', StringSplitOptions.RemoveEmptyEntries);

        if (count < 5 || count > 7)
            return false;

        CronField seconds;
        CronField minutes;
        CronField hours;
        CronField dayOfMonth;
        CronField month;
        CronField dayOfWeek;
        CronField year;
        bool hasSeconds;
        bool hasYear;

        if (count is 5)
        {
            // Standard: min hour dom month dow
            hasSeconds = false;
            hasYear = false;
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
            hasSeconds = true;
            hasYear = false;

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
            hasSeconds = true;
            hasYear = true;

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

        cronExpression = new CronExpression(seconds, minutes, hours, dayOfMonth, month, dayOfWeek, year, hasSeconds, hasYear);
        return true;
    }

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
                CronField.CreateAll(CronFieldKind.Year),
                hasSeconds: false,
                hasYear: false);
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
                CronField.CreateAll(CronFieldKind.Year),
                hasSeconds: false,
                hasYear: false);
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
                CronField.CreateAll(CronFieldKind.Year),
                hasSeconds: false,
                hasYear: false);
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
                CronField.CreateAll(CronFieldKind.Year),
                hasSeconds: false,
                hasYear: false);
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
                CronField.CreateAll(CronFieldKind.Year),
                hasSeconds: false,
                hasYear: false);
            return true;
        }

        return false;
    }

    private static bool TryParseField(ReadOnlySpan<char> field, CronFieldKind kind, out CronField result)
    {
        result = default;

        if (field.IsEmpty)
            return false;

        // Handle ? (any)
        if (field.Length is 1 && field[0] == '?')
        {
            result = CronField.CreateAll(kind);
            return true;
        }

        // Handle * (all)
        if (field.Length is 1 && field[0] == '*')
        {
            result = CronField.CreateAll(kind);
            return true;
        }

        // Handle */step
        if (field.Length > 2 && field[0] == '*' && field[1] == '/')
        {
            if (!TryParseInt(field[2..], out var step) || step <= 0)
                return false;

            result = CronField.CreateStep(kind, GetMinValue(kind), GetMaxValue(kind), step);
            return true;
        }

        // Handle list (comma-separated)
        if (field.Contains(','))
        {
            var values = new List<CronFieldValue>();
            var remaining = field;
            while (!remaining.IsEmpty)
            {
                var commaIndex = remaining.IndexOf(',');
                ReadOnlySpan<char> part;
                if (commaIndex >= 0)
                {
                    part = remaining[..commaIndex];
                    remaining = remaining[(commaIndex + 1)..];
                }
                else
                {
                    part = remaining;
                    remaining = [];
                }

                if (!TryParseFieldPart(part, kind, out var partValues))
                    return false;
                values.AddRange(partValues);
            }

            result = CronField.CreateList(kind, values);
            return true;
        }

        // Parse single part
        if (!TryParseFieldPart(field, kind, out var singleValues))
            return false;

        result = CronField.CreateList(kind, singleValues);
        return true;
    }

    private static bool TryParseFieldPart(ReadOnlySpan<char> part, CronFieldKind kind, out List<CronFieldValue> values)
    {
        values = [];

        if (part.IsEmpty)
            return false;

        // Handle special day of month cases: L, LW, L-n
        if (kind is CronFieldKind.DayOfMonth)
        {
            if (part.Equals("L", StringComparison.OrdinalIgnoreCase))
            {
                values.Add(new CronFieldValue { Kind = CronValueKind.Last });
                return true;
            }

            if (part.Equals("LW", StringComparison.OrdinalIgnoreCase))
            {
                values.Add(new CronFieldValue { Kind = CronValueKind.LastWeekday });
                return true;
            }

            if (part.Length > 2 && part[0] == 'L' && part[1] == '-')
            {
                if (!TryParseInt(part[2..], out var offset) || offset < 0)
                    return false;
                values.Add(new CronFieldValue { Kind = CronValueKind.LastOffset, Value = offset });
                return true;
            }

            // Handle nW (nearest weekday)
            if (part.Length > 1 && (part[^1] == 'W' || part[^1] == 'w'))
            {
                if (!TryParseInt(part[..^1], out var day))
                    return false;
                values.Add(new CronFieldValue { Kind = CronValueKind.NearestWeekday, Value = day });
                return true;
            }
        }

        // Handle special day of week cases: nL (last occurrence), n#m (nth occurrence)
        if (kind is CronFieldKind.DayOfWeek)
        {
            // Handle nL (last day of week in month)
            if (part.Length >= 2 && (part[^1] == 'L' || part[^1] == 'l'))
            {
                if (!TryParseDayOfWeek(part[..^1], out var dow))
                    return false;
                values.Add(new CronFieldValue { Kind = CronValueKind.LastDayOfWeek, Value = dow });
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
                values.Add(new CronFieldValue { Kind = CronValueKind.NthDayOfWeek, Value = dow, NthValue = nth });
                return true;
            }
        }

        // Handle range with optional step: start-end or start-end/step or start/step
        var slashIndex = part.IndexOf('/');
        if (slashIndex > 0)
        {
            var rangePart = part[..slashIndex];
            var stepPart = part[(slashIndex + 1)..];

            if (!TryParseInt(stepPart, out var step) || step <= 0)
                return false;

            // Check if range or single value
            var dashIndex = rangePart.IndexOf('-');
            if (dashIndex > 0)
            {
                // Range with step: start-end/step
                if (!TryParseValue(rangePart[..dashIndex], kind, out var start))
                    return false;
                if (!TryParseValue(rangePart[(dashIndex + 1)..], kind, out var end))
                    return false;

                for (var i = start; i <= end; i += step)
                {
                    values.Add(new CronFieldValue { Kind = CronValueKind.Value, Value = i });
                }
                return true;
            }
            else
            {
                // Single value with step: n/step (means n, n+step, n+2*step, ... up to max)
                if (!TryParseValue(rangePart, kind, out var start))
                    return false;

                var max = GetMaxValue(kind);
                for (var i = start; i <= max; i += step)
                {
                    values.Add(new CronFieldValue { Kind = CronValueKind.Value, Value = i });
                }
                return true;
            }
        }

        // Handle range: start-end
        var rangeDashIndex = part.IndexOf('-');
        if (rangeDashIndex > 0)
        {
            if (!TryParseValue(part[..rangeDashIndex], kind, out var start))
                return false;
            if (!TryParseValue(part[(rangeDashIndex + 1)..], kind, out var end))
                return false;

            for (var i = start; i <= end; i++)
            {
                values.Add(new CronFieldValue { Kind = CronValueKind.Value, Value = i });
            }
            return true;
        }

        // Single value
        if (!TryParseValue(part, kind, out var value))
            return false;

        values.Add(new CronFieldValue { Kind = CronValueKind.Value, Value = value });
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

        return TryParseInt(value, out result) && result >= 0 && result <= 6;
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

    public IEnumerable<DateTime> GetNextOccurrences(DateTime startDate)
    {
        var current = startDate;

        // Limit to prevent infinite loops for invalid expressions
        var maxIterations = 100000;
        var iterations = 0;

        while (iterations < maxIterations)
        {
            var next = GetNextOccurrence(current);
            if (next is null)
                yield break;

            yield return next.Value;
            current = next.Value.AddSeconds(1);
            iterations++;
        }
    }

    private DateTime? GetNextOccurrence(DateTime from)
    {
        var current = from;
        try
        {
            while (true)
            {
                // Check year
                if (!_year.Matches(current.Year))
                {
                    var nextYear = _year.GetNext(current.Year);
                    if (nextYear is null || nextYear.Value > 2099)
                        return null;

                    current = new DateTime(nextYear.Value, 1, 1, 0, 0, 0, from.Kind);
                    continue;
                }

                // Check month
                if (!_month.Matches(current.Month))
                {
                    var nextMonth = _month.GetNext(current.Month);
                    if (nextMonth is null)
                    {
                        current = new DateTime(current.Year + 1, 1, 1, 0, 0, 0, from.Kind);
                        continue;
                    }

                    current = new DateTime(current.Year, nextMonth.Value, 1, 0, 0, 0, from.Kind);
                    continue;
                }

                // Check day of month and day of week
                if (!MatchesDay(current))
                {
                    var nextDay = GetNextMatchingDay(current);
                    if (nextDay is null)
                    {
                        // Move to next month
                        if (current.Month is 12)
                        {
                            current = new DateTime(current.Year + 1, 1, 1, 0, 0, 0, from.Kind);
                        }
                        else
                        {
                            current = new DateTime(current.Year, current.Month + 1, 1, 0, 0, 0, from.Kind);
                        }
                        continue;
                    }

                    current = new DateTime(current.Year, current.Month, nextDay.Value, 0, 0, 0, from.Kind);
                    continue;
                }

                // Check hour
                if (!_hours.Matches(current.Hour))
                {
                    var nextHour = _hours.GetNext(current.Hour);
                    if (nextHour is null)
                    {
                        current = current.Date.AddDays(1);
                        continue;
                    }

                    current = new DateTime(current.Year, current.Month, current.Day, nextHour.Value, 0, 0, from.Kind);
                    continue;
                }

                // Check minute
                if (!_minutes.Matches(current.Minute))
                {
                    var nextMinute = _minutes.GetNext(current.Minute);
                    if (nextMinute is null)
                    {
                        current = new DateTime(current.Year, current.Month, current.Day, current.Hour, 0, 0, from.Kind).AddHours(1);
                        continue;
                    }

                    current = new DateTime(current.Year, current.Month, current.Day, current.Hour, nextMinute.Value, 0, from.Kind);
                    continue;
                }

                // Check second
                if (!_seconds.Matches(current.Second))
                {
                    var nextSecond = _seconds.GetNext(current.Second);
                    if (nextSecond is null)
                    {
                        current = new DateTime(current.Year, current.Month, current.Day, current.Hour, current.Minute, 0, from.Kind).AddMinutes(1);
                        continue;
                    }

                    current = new DateTime(current.Year, current.Month, current.Day, current.Hour, current.Minute, nextSecond.Value, from.Kind);
                    continue;
                }

                return current;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Greater than DateTime.MaxValue
        }

        return null;
    }

    private bool MatchesDay(DateTime date)
    {
        return _dayOfMonth.MatchesDay(date.Day, date) && _dayOfWeek.MatchesDayOfWeek(date);
    }

    private int? GetNextMatchingDay(DateTime date)
    {
        var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);

        for (var day = date.Day; day <= daysInMonth; day++)
        {
            var testDate = new DateTime(date.Year, date.Month, day);
            if (_dayOfMonth.MatchesDay(day, testDate) && _dayOfWeek.MatchesDayOfWeek(testDate))
            {
                return day;
            }
        }

        return null;
    }

    static CronExpression IParsable<CronExpression>.Parse(string s, IFormatProvider? provider) => Parse(s);
    static bool IParsable<CronExpression>.TryParse(string? s, IFormatProvider? provider, out CronExpression result) => TryParse(s, out result);
    static CronExpression ISpanParsable<CronExpression>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);
    static bool ISpanParsable<CronExpression>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out CronExpression result) => TryParse(s, out result);

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
        Value,
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
    private readonly struct CronField
    {
        private readonly CronFieldKind _kind;
        private readonly bool _isAll;
        private readonly List<CronFieldValue>? _values;

        private CronField(CronFieldKind kind, bool isAll, List<CronFieldValue>? values)
        {
            _kind = kind;
            _isAll = isAll;
            _values = values;
        }

        public static CronField CreateAll(CronFieldKind kind) => new(kind, isAll: true, values: null);

        public static CronField CreateValue(CronFieldKind kind, int value) =>
            new(kind, isAll: false, values: [new CronFieldValue { Kind = CronValueKind.Value, Value = value }]);

        public static CronField CreateStep(CronFieldKind kind, int start, int end, int step)
        {
            var values = new List<CronFieldValue>();
            for (var i = start; i <= end; i += step)
            {
                values.Add(new CronFieldValue { Kind = CronValueKind.Value, Value = i });
            }
            return new CronField(kind, isAll: false, values: values);
        }

        public static CronField CreateList(CronFieldKind kind, List<CronFieldValue> values) =>
            new(kind, isAll: false, values: values);

        public bool Matches(int value)
        {
            if (_isAll)
                return true;

            if (_values is null)
                return false;

            foreach (var v in _values)
            {
                if (v.Kind is CronValueKind.Value && v.Value == value)
                    return true;
            }

            return false;
        }

        public bool MatchesDay(int day, DateTime date)
        {
            if (_isAll)
                return true;

            if (_values is null)
                return false;

            var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);

            foreach (var v in _values)
            {
                switch (v.Kind)
                {
                    case CronValueKind.Value:
                        if (v.Value == day)
                            return true;
                        break;

                    case CronValueKind.Last:
                        if (day == daysInMonth)
                            return true;
                        break;

                    case CronValueKind.LastOffset:
                        if (day == daysInMonth - v.Value)
                            return true;
                        break;

                    case CronValueKind.LastWeekday:
                        var lastWeekday = GetLastWeekday(date.Year, date.Month);
                        if (day == lastWeekday)
                            return true;
                        break;

                    case CronValueKind.NearestWeekday:
                        var nearestWeekday = GetNearestWeekday(date.Year, date.Month, v.Value);
                        if (day == nearestWeekday)
                            return true;
                        break;
                }
            }

            return false;
        }

        public bool MatchesDayOfWeek(DateTime date)
        {
            if (_isAll)
                return true;

            if (_values is null)
                return false;

            var dayOfWeek = (int)date.DayOfWeek;

            foreach (var v in _values)
            {
                switch (v.Kind)
                {
                    case CronValueKind.Value:
                        if (v.Value == dayOfWeek)
                            return true;
                        break;

                    case CronValueKind.LastDayOfWeek:
                        // Last occurrence of day v.Value in the month
                        if (dayOfWeek == v.Value && IsLastOccurrenceOfDayInMonth(date))
                            return true;
                        break;

                    case CronValueKind.NthDayOfWeek:
                        // Nth occurrence of day v.Value in the month
                        if (dayOfWeek == v.Value && GetDayOfWeekOccurrence(date) == v.NthValue)
                            return true;
                        break;
                }
            }

            return false;
        }

        private static int GetLastWeekday(int year, int month)
        {
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var lastDay = new DateTime(year, month, daysInMonth);

            while (lastDay.DayOfWeek is DayOfWeek.Saturday || lastDay.DayOfWeek is DayOfWeek.Sunday)
            {
                lastDay = lastDay.AddDays(-1);
            }

            return lastDay.Day;
        }

        private static int GetNearestWeekday(int year, int month, int targetDay)
        {
            var daysInMonth = DateTime.DaysInMonth(year, month);

            // If the target day doesn't exist in this month, return -1 (no match)
            if (targetDay > daysInMonth)
                return -1;

            var date = new DateTime(year, month, targetDay);

            if (date.DayOfWeek is DayOfWeek.Saturday)
            {
                // Move to Friday if possible, otherwise Monday
                if (targetDay > 1)
                    return targetDay - 1;
                else
                    return targetDay + 2; // Monday
            }

            if (date.DayOfWeek is DayOfWeek.Sunday)
            {
                // Move to Monday if possible, otherwise Friday
                if (targetDay < daysInMonth)
                    return targetDay + 1;
                else
                    return targetDay - 2; // Friday
            }

            return targetDay;
        }

        private static bool IsLastOccurrenceOfDayInMonth(DateTime date)
        {
            // Check if there's another occurrence of this day of week in the remaining month
            var nextSameDay = date.AddDays(7);
            return nextSameDay.Month != date.Month;
        }

        private static int GetDayOfWeekOccurrence(DateTime date)
        {
            return (date.Day - 1) / 7 + 1;
        }

        public int? GetNext(int current)
        {
            if (_isAll)
            {
                var max = GetMaxValue(_kind);
                if (current < max)
                    return current;
                return null;
            }

            if (_values is null)
                return null;

            int? result = null;
            foreach (var v in _values)
            {
                if (v.Kind is CronValueKind.Value && v.Value >= current)
                {
                    if (result is null || v.Value < result.Value)
                    {
                        result = v.Value;
                    }
                }
            }

            return result;
        }
    }
}
