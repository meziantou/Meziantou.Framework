using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Scheduling.Fuzz;

/// <summary>
/// A straightforward reading of the cron syntax <see cref="CronExpression"/> documents, kept apart from its bit-mask
/// implementation: each field is a set of values, plus the special values of the day fields, and a date-time matches when
/// every field matches. The day of the month and the day of the week must both match.
/// </summary>
internal sealed class CronReference
{
    private const int MinYear = 1970;
    private const int MaxYear = 9999;

    private static readonly string[] MonthNames = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
    private static readonly string[] DayOfWeekNames = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];

    private readonly Field[] _fields;

    private CronReference(Field[] fields)
    {
        _fields = fields;
    }

    private Field Seconds => _fields[0];

    private Field Minutes => _fields[1];

    private Field Hours => _fields[2];

    private Field DaysOfMonth => _fields[3];

    private Field Months => _fields[4];

    private Field DaysOfWeek => _fields[5];

    private Field Years => _fields[6];

    public static bool TryParse(string text, out CronReference? result)
    {
        result = null;
        var trimmed = text.Trim(' ', '\t');
        if (trimmed.Length is 0)
            return false;

        if (trimmed[0] is '@')
        {
            var schedule = ToUpperAscii(trimmed) switch
            {
                "@YEARLY" or "@ANNUALLY" => "0 0 0 1 1 * *",
                "@MONTHLY" => "0 0 0 1 * * *",
                "@WEEKLY" => "0 0 0 * * 0 *",
                "@DAILY" or "@MIDNIGHT" => "0 0 0 * * * *",
                "@HOURLY" => "0 0 * * * * *",
                _ => null,
            };

            return schedule is not null && TryParse(schedule, out result);
        }

        var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 5 or > 7)
            return false;

        if (parts.Length is 5)
        {
            parts = ["0", .. parts, "*"];
        }
        else if (parts.Length is 6)
        {
            parts = [.. parts, "*"];
        }

        var fields = new Field[7];
        for (var i = 0; i < 7; i++)
        {
            if (!Field.TryParse(parts[i], (FieldKind)i, out fields[i]))
                return false;
        }

        result = new CronReference(fields);
        return true;
    }

    /// <summary>Gets an expression selecting the same values, with every field written as a list of its values.</summary>
    public string ToCanonicalText() => string.Join(" ", _fields.Select(field => field.ToCanonicalText()));

    public bool Matches(DateTime value) => Seconds.Contains(value.Second) && Minutes.Contains(value.Minute) && Hours.Contains(value.Hour) && MatchesDate(value);

    public bool MatchesDate(DateTime value)
    {
        if (!Months.Contains(value.Month) || !Years.Contains(value.Year))
            return false;

        var daysInMonth = DateTime.DaysInMonth(value.Year, value.Month);
        return MatchesDayOfMonth(value, daysInMonth) && MatchesDayOfWeek(value, daysInMonth);
    }

    /// <summary>Gets the occurrences from <paramref name="start"/> (rounded up to a whole second) up to <paramref name="end"/>, by scanning every day.</summary>
    public IEnumerable<DateTime> GetOccurrences(DateTime start, DateTime end)
    {
        var fraction = start.Ticks % TimeSpan.TicksPerSecond;
        if (fraction is not 0)
        {
            if (DateTime.MaxValue.Ticks - start.Ticks < TimeSpan.TicksPerSecond - fraction)
                yield break;

            start = start.AddTicks(TimeSpan.TicksPerSecond - fraction);
        }

        for (var day = start.Date; day <= end; day = day.AddDays(1))
        {
            if (MatchesDate(day))
            {
                for (var hour = 0; hour < 24; hour++)
                {
                    if (!Hours.Contains(hour))
                        continue;

                    for (var minute = 0; minute < 60; minute++)
                    {
                        if (!Minutes.Contains(minute))
                            continue;

                        for (var second = 0; second < 60; second++)
                        {
                            if (!Seconds.Contains(second))
                                continue;

                            var value = new DateTime(day.Ticks + new TimeSpan(hour, minute, second).Ticks, start.Kind);
                            if (value > end)
                                yield break;

                            if (value >= start)
                                yield return value;
                        }
                    }
                }
            }

            if (day == DateTime.MaxValue.Date)
                yield break;
        }
    }

    private bool MatchesDayOfMonth(DateTime value, int daysInMonth)
    {
        var field = DaysOfMonth;
        if (field.IsAll || field.Contains(value.Day))
            return true;

        var day = value.Day;
        if (field.LastDayOffsets.Any(offset => daysInMonth - offset == day))
            return true;

        if (field.LastWeekday && GetNearestWeekday(daysInMonth, value, daysInMonth, preferBefore: true) == day)
            return true;

        return field.NearestWeekdays.Any(target => target <= daysInMonth && GetNearestWeekday(target, value, daysInMonth, preferBefore: false) == day);
    }

    /// <summary>Gets the weekday nearest to a day of the month, without leaving the month.</summary>
    private static int GetNearestWeekday(int target, DateTime value, int daysInMonth, bool preferBefore)
    {
        var dayOfWeek = new DateTime(value.Year, value.Month, target).DayOfWeek;
        return dayOfWeek switch
        {
            DayOfWeek.Saturday => target > 1 ? target - 1 : target + 2,
            DayOfWeek.Sunday when preferBefore => target - 2,
            DayOfWeek.Sunday => target < daysInMonth ? target + 1 : target - 2,
            _ => target,
        };
    }

    private bool MatchesDayOfWeek(DateTime value, int daysInMonth)
    {
        var field = DaysOfWeek;
        var dayOfWeek = (int)value.DayOfWeek;
        if (field.IsAll || field.Contains(dayOfWeek))
            return true;

        if (field.LastDaysOfWeek.Contains(dayOfWeek) && value.Day + 7 > daysInMonth)
            return true;

        return field.NthDaysOfWeek.Contains((dayOfWeek, ((value.Day - 1) / 7) + 1));
    }

    private enum FieldKind
    {
        Seconds,
        Minutes,
        Hours,
        DayOfMonth,
        Month,
        DayOfWeek,
        Year,
    }

    private sealed class Field
    {
        private readonly FieldKind _kind;
        private readonly SortedSet<int> _values = [];

        private Field(FieldKind kind)
        {
            _kind = kind;
        }

        public bool IsAll { get; private set; }

        public SortedSet<int> LastDayOffsets { get; } = [];

        public bool LastWeekday { get; private set; }

        public SortedSet<int> NearestWeekdays { get; } = [];

        public SortedSet<int> LastDaysOfWeek { get; } = [];

        public SortedSet<(int DayOfWeek, int Nth)> NthDaysOfWeek { get; } = [];

        private int Min => _kind switch
        {
            FieldKind.DayOfMonth or FieldKind.Month => 1,
            FieldKind.Year => MinYear,
            _ => 0,
        };

        private int Max => _kind switch
        {
            FieldKind.Seconds or FieldKind.Minutes => 59,
            FieldKind.Hours => 23,
            FieldKind.DayOfMonth => 31,
            FieldKind.Month => 12,
            FieldKind.DayOfWeek => 6,
            _ => MaxYear,
        };

        public bool Contains(int value) => IsAll || _values.Contains(value);

        public static bool TryParse(string text, FieldKind kind, out Field field)
        {
            field = new Field(kind);
            if (text is "?" or "*")
            {
                field.IsAll = true;
                return true;
            }

            foreach (var part in text.Split(','))
            {
                if (!field.TryAddPart(part))
                    return false;
            }

            // A field holding every value matches anything, whatever its special values
            if (field._kind is not FieldKind.Year && Enumerable.Range(field.Min, field.Max - field.Min + 1).All(field._values.Contains))
            {
                field.IsAll = true;
            }

            return true;
        }

        private bool TryAddPart(string part)
        {
            if (part.Length is 0)
                return false;

            if (part is "*")
            {
                IsAll = true;
                return true;
            }

            var upper = ToUpperAscii(part);
            if (_kind is FieldKind.DayOfMonth)
            {
                if (upper is "L")
                {
                    LastDayOffsets.Add(0);
                    return true;
                }

                if (upper is "LW")
                {
                    LastWeekday = true;
                    return true;
                }

                if (upper.StartsWith("L-", StringComparison.Ordinal) && upper.Length > 2)
                {
                    if (!TryParseNumber(upper[2..], out var offset) || offset > 30)
                        return false;

                    LastDayOffsets.Add(offset);
                    return true;
                }

                if (upper[^1] is 'W' && upper.Length > 1)
                {
                    if (!TryParseNumber(upper[..^1], out var day) || day is < 1 or > 31)
                        return false;

                    NearestWeekdays.Add(day);
                    return true;
                }
            }

            if (_kind is FieldKind.DayOfWeek)
            {
                if (upper[^1] is 'L' && upper.Length >= 2)
                {
                    if (!TryParseDayOfWeek(upper[..^1], out var dayOfWeek))
                        return false;

                    LastDaysOfWeek.Add(dayOfWeek % 7);
                    return true;
                }

                var hash = upper.IndexOf('#', StringComparison.Ordinal);
                if (hash > 0 && hash < upper.Length - 1)
                {
                    if (!TryParseDayOfWeek(upper[..hash], out var dayOfWeek) || !TryParseNumber(upper[(hash + 1)..], out var nth) || nth is < 1 or > 5)
                        return false;

                    NthDaysOfWeek.Add((dayOfWeek % 7, nth));
                    return true;
                }
            }

            var step = 1;
            var range = upper;
            var slash = upper.IndexOf('/', StringComparison.Ordinal);
            if (slash >= 0)
            {
                if (!TryParseNumber(upper[(slash + 1)..], out step) || step is 0)
                    return false;

                range = upper[..slash];
            }

            int first;
            int last;
            if (range is "*" && slash >= 0)
            {
                first = Min;
                last = Max;
            }
            else
            {
                var dash = range.IndexOf('-', StringComparison.Ordinal);
                if (dash >= 0)
                {
                    if (!TryParseValue(range[..dash], out first) || !TryParseValue(range[(dash + 1)..], out last))
                        return false;

                    if (last < first && _kind is FieldKind.Year)
                        return false;
                }
                else
                {
                    if (!TryParseValue(range, out first))
                        return false;

                    last = slash < 0 ? first : _kind is FieldKind.DayOfWeek ? 7 : Max;
                }
            }

            // A reversed range wraps around the end of the field; 7 is another Sunday
            var size = Max - Min + 1;
            var end = last < first ? (long)last + size : last;
            for (long value = first; value <= end; value += step)
            {
                var normalized = value > Max ? value - size : value;
                _values.Add((int)(_kind is FieldKind.DayOfWeek ? normalized % 7 : normalized));
            }

            return true;
        }

        private bool TryParseValue(string text, out int value)
        {
            if (_kind is FieldKind.Month)
            {
                var index = Array.IndexOf(MonthNames, text);
                if (index >= 0)
                {
                    value = index + 1;
                    return true;
                }
            }

            if (_kind is FieldKind.DayOfWeek)
                return TryParseDayOfWeek(text, out value);

            return TryParseNumber(text, out value) && value >= Min && value <= Max;
        }

        private static bool TryParseDayOfWeek(string text, out int value)
        {
            value = Array.IndexOf(DayOfWeekNames, text);
            if (value >= 0)
                return true;

            return TryParseNumber(text, out value) && value is >= 0 and <= 7;
        }

        private static bool TryParseNumber(string text, out int value)
        {
            value = 0;
            if (text.Length is 0)
                return false;

            long result = 0;
            foreach (var c in text)
            {
                if (c is < '0' or > '9')
                    return false;

                result = (result * 10) + (c - '0');
                if (result > int.MaxValue)
                    return false;
            }

            value = (int)result;
            return true;
        }

        public string ToCanonicalText()
        {
            if (IsAll)
                return "*";

            var items = new List<string>();
            if (_kind is FieldKind.Year)
            {
                // The years are written as ranges, which keeps the text short
                int? rangeStart = null;
                var previous = 0;
                foreach (var year in _values)
                {
                    if (rangeStart is not null && year == previous + 1)
                    {
                        previous = year;
                        continue;
                    }

                    if (rangeStart is { } value)
                    {
                        items.Add(FormatRange(value, previous));
                    }

                    rangeStart = year;
                    previous = year;
                }

                if (rangeStart is { } lastStart)
                {
                    items.Add(FormatRange(lastStart, previous));
                }
            }
            else
            {
                items.AddRange(_values.Select(value => value.ToString(CultureInfo.InvariantCulture)));
            }

            items.AddRange(LastDayOffsets.Select(offset => offset is 0 ? "L" : "L-" + offset.ToString(CultureInfo.InvariantCulture)));
            if (LastWeekday)
            {
                items.Add("LW");
            }

            items.AddRange(NearestWeekdays.Select(day => day.ToString(CultureInfo.InvariantCulture) + "W"));
            items.AddRange(LastDaysOfWeek.Select(day => day.ToString(CultureInfo.InvariantCulture) + "L"));
            items.AddRange(NthDaysOfWeek.Select(item => item.DayOfWeek.ToString(CultureInfo.InvariantCulture) + "#" + item.Nth.ToString(CultureInfo.InvariantCulture)));
            return string.Join(",", items);

            static string FormatRange(int start, int end) => start == end ? start.ToString(CultureInfo.InvariantCulture) : start.ToString(CultureInfo.InvariantCulture) + "-" + end.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Upper-cases the ASCII letters only: no other character is a letter of a cron keyword.</summary>
    private static string ToUpperAscii(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(c is >= 'a' and <= 'z' ? (char)(c - ('a' - 'A')) : c);
        }

        return sb.ToString();
    }
}
