using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// The date formats of Mermaid, which are the formats of dayjs: <c>YYYY</c>, <c>YY</c>, <c>M</c>, <c>MM</c>,
/// <c>MMM</c>, <c>MMMM</c>, <c>D</c>, <c>DD</c>, <c>Do</c>, <c>H</c>, <c>HH</c>, <c>h</c>, <c>hh</c>, <c>k</c>,
/// <c>kk</c>, <c>m</c>, <c>mm</c>, <c>s</c>, <c>ss</c>, <c>SSS</c>, <c>A</c>, <c>a</c>, <c>X</c> (Unix seconds),
/// <c>x</c> (Unix milliseconds), and, for the formatting only, <c>d</c>, <c>dd</c>, <c>ddd</c>, <c>dddd</c> and
/// <c>Q</c>. Text between brackets and the other characters are literal. The names are English. A date parses when it
/// formats back to the same text, as the strict mode of dayjs; the missing parts are those of 1970-01-01.
/// </summary>
internal sealed class GanttDateFormat
{
    private static readonly string[] Tokens = ["YYYY", "YY", "MMMM", "MMM", "MM", "M", "DD", "Do", "D", "dddd", "ddd", "dd", "d", "HH", "H", "hh", "h", "kk", "k", "mm", "m", "ss", "s", "SSS", "A", "a", "X", "x", "Q"];

    internal static readonly string[] MonthNames = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    internal static readonly string[] DayNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private readonly List<(bool IsToken, string Value)> _parts;

    public GanttDateFormat(string format)
    {
        _parts = Tokenize(format);
    }

    private static List<(bool IsToken, string Value)> Tokenize(string format)
    {
        var parts = new List<(bool IsToken, string Value)>();
        var literal = new StringBuilder();
        var i = 0;
        while (i < format.Length)
        {
            if (format[i] == '[')
            {
                var end = format.IndexOf("]", i + 1, StringComparison.Ordinal);
                if (end > i)
                {
                    literal.Append(format, i + 1, end - i - 1);
                    i = end + 1;
                    continue;
                }
            }

            var token = Array.Find(Tokens, candidate => string.CompareOrdinal(format, i, candidate, 0, candidate.Length) == 0);
            if (token is null)
            {
                literal.Append(format[i]);
                i++;
                continue;
            }

            if (literal.Length > 0)
            {
                parts.Add((false, literal.ToString()));
                literal.Clear();
            }

            parts.Add((true, token));
            i += token.Length;
        }

        if (literal.Length > 0)
        {
            parts.Add((false, literal.ToString()));
        }

        return parts;
    }

    public string Format(DateTime date)
    {
        var builder = new StringBuilder();
        foreach (var (isToken, value) in _parts)
        {
            if (!isToken)
            {
                builder.Append(value);
                continue;
            }

            var hour12 = date.Hour % 12 is 0 ? 12 : date.Hour % 12;
            builder.Append(value switch
            {
                "YYYY" => date.Year.ToString("0000", CultureInfo.InvariantCulture),
                "YY" => (date.Year % 100).ToString("00", CultureInfo.InvariantCulture),
                "MMMM" => MonthNames[date.Month - 1],
                "MMM" => MonthNames[date.Month - 1][..3],
                "MM" => date.Month.ToString("00", CultureInfo.InvariantCulture),
                "M" => date.Month.ToString(CultureInfo.InvariantCulture),
                "DD" => date.Day.ToString("00", CultureInfo.InvariantCulture),
                "Do" => date.Day.ToString(CultureInfo.InvariantCulture) + Ordinal(date.Day),
                "D" => date.Day.ToString(CultureInfo.InvariantCulture),
                "dddd" => DayNames[(int)date.DayOfWeek],
                "ddd" => DayNames[(int)date.DayOfWeek][..3],
                "dd" => DayNames[(int)date.DayOfWeek][..2],
                "d" => ((int)date.DayOfWeek).ToString(CultureInfo.InvariantCulture),
                "HH" => date.Hour.ToString("00", CultureInfo.InvariantCulture),
                "H" => date.Hour.ToString(CultureInfo.InvariantCulture),
                "hh" => hour12.ToString("00", CultureInfo.InvariantCulture),
                "h" => hour12.ToString(CultureInfo.InvariantCulture),
                "kk" => (date.Hour is 0 ? 24 : date.Hour).ToString("00", CultureInfo.InvariantCulture),
                "k" => (date.Hour is 0 ? 24 : date.Hour).ToString(CultureInfo.InvariantCulture),
                "mm" => date.Minute.ToString("00", CultureInfo.InvariantCulture),
                "m" => date.Minute.ToString(CultureInfo.InvariantCulture),
                "ss" => date.Second.ToString("00", CultureInfo.InvariantCulture),
                "s" => date.Second.ToString(CultureInfo.InvariantCulture),
                "SSS" => date.Millisecond.ToString("000", CultureInfo.InvariantCulture),
                "A" => date.Hour < 12 ? "AM" : "PM",
                "a" => date.Hour < 12 ? "am" : "pm",
                "X" => ((long)Math.Floor((date - UnixEpoch).TotalSeconds)).ToString(CultureInfo.InvariantCulture),
                "x" => ((long)Math.Floor((date - UnixEpoch).TotalMilliseconds)).ToString(CultureInfo.InvariantCulture),
                "Q" => (((date.Month - 1) / 3) + 1).ToString(CultureInfo.InvariantCulture),
                _ => value,
            });
        }

        return builder.ToString();
    }

    private static string Ordinal(int day) => (day % 100) switch
    {
        11 or 12 or 13 => "th",
        _ => (day % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        },
    };

    /// <summary>Parses a date, in the strict mode of dayjs: the date must format back to the same text.</summary>
    public bool TryParse(string text, out DateTime date)
    {
        date = default;
        if (!TryParseParts(text, out var parsed))
            return false;

        if (Format(parsed) != text)
            return false;

        date = parsed;
        return true;
    }

    private bool TryParseParts(string text, out DateTime date)
    {
        date = default;
        int? year = null;
        var month = 1;
        var day = 1;
        var hour = 0;
        var minute = 0;
        var second = 0;
        var millisecond = 0;
        bool? afternoon = null;
        double? unixMilliseconds = null;
        var position = 0;
        foreach (var (isToken, value) in _parts)
        {
            if (!isToken)
            {
                // As dayjs, a literal skips as many characters
                position += value.Length;
                if (position > text.Length)
                    return false;

                continue;
            }

            switch (value)
            {
                case "YYYY":
                    if (!ReadDigits(text, ref position, 4, 4, out var fullYear))
                        return false;

                    year = fullYear;
                    break;
                case "YY":
                    if (!ReadDigits(text, ref position, 2, 2, out var shortYear))
                        return false;

                    year = shortYear + (shortYear > 68 ? 1900 : 2000);
                    break;
                case "MMMM" or "MMM":
                    {
                        var word = ReadWord(text, ref position);
                        var index = Array.FindIndex(MonthNames, name => value is "MMMM" ? string.Equals(name, word, StringComparison.Ordinal) : string.Equals(name[..3], word, StringComparison.Ordinal));
                        if (index < 0)
                            return false;

                        month = index + 1;
                        break;
                    }

                case "MM" or "M":
                    if (!ReadDigits(text, ref position, value.Length is 2 ? 2 : 1, 2, out month))
                        return false;

                    break;
                case "DD" or "D":
                    if (!ReadDigits(text, ref position, value.Length is 2 ? 2 : 1, 2, out day))
                        return false;

                    break;
                case "Do":
                    if (!ReadDigits(text, ref position, 1, 2, out day))
                        return false;

                    position = Math.Min(text.Length, position + 2);
                    break;
                case "HH" or "H" or "kk" or "k":
                    if (!ReadDigits(text, ref position, value.Length is 2 ? 2 : 1, 2, out hour))
                        return false;

                    if (value[0] is 'k' && hour is 24)
                    {
                        hour = 0;
                    }

                    break;
                case "hh" or "h":
                    if (!ReadDigits(text, ref position, value.Length is 2 ? 2 : 1, 2, out hour))
                        return false;

                    break;
                case "mm" or "m":
                    if (!ReadDigits(text, ref position, value.Length is 2 ? 2 : 1, 2, out minute))
                        return false;

                    break;
                case "ss" or "s":
                    if (!ReadDigits(text, ref position, value.Length is 2 ? 2 : 1, 2, out second))
                        return false;

                    break;
                case "SSS":
                    if (!ReadDigits(text, ref position, 3, 3, out millisecond))
                        return false;

                    break;
                case "A" or "a":
                    {
                        var word = ReadWord(text, ref position);
                        if (word is "AM" or "am")
                        {
                            afternoon = false;
                        }
                        else if (word is "PM" or "pm")
                        {
                            afternoon = true;
                        }
                        else
                        {
                            return false;
                        }

                        break;
                    }

                case "X" or "x":
                    {
                        var start = position;
                        if (position < text.Length && text[position] is '-' or '+')
                        {
                            position++;
                        }

                        var digitsStart = position;
                        while (position < text.Length && char.IsAsciiDigit(text[position]) && position - digitsStart < 15)
                        {
                            position++;
                        }

                        if (position == digitsStart)
                            return false;

                        var number = long.Parse(text.AsSpan(start, position - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                        unixMilliseconds = value is "X" ? number * 1000.0 : number;
                        break;
                    }

                default:
                    // A token that is only formatted, such as a day of the week: its value is checked by the strict comparison
                    ReadWord(text, ref position);
                    break;
            }
        }

        if (afternoon is { } pm)
        {
            if (hour is < 1 or > 12)
                return false;

            hour = (hour % 12) + (pm ? 12 : 0);
        }

        if (unixMilliseconds is { } milliseconds)
        {
            if (Math.Abs(milliseconds) > 2.5e14)
                return false;

            date = UnixEpoch.AddMilliseconds(milliseconds);
            return date.Year is >= 1 and <= 9999;
        }

        var y = year ?? 1970;
        if (y is < 1 or > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(y, month) || hour > 23 || minute > 59 || second > 59)
            return false;

        date = new DateTime(y, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified);
        return true;
    }

    private static bool ReadDigits(string text, ref int position, int minimum, int maximum, out int value)
    {
        value = 0;
        var start = position;
        while (position < text.Length && position - start < maximum && char.IsAsciiDigit(text[position]))
        {
            value = (value * 10) + (text[position] - '0');
            position++;
        }

        return position - start >= minimum;
    }

    private static string ReadWord(string text, ref int position)
    {
        var start = position;
        while (position < text.Length && char.IsAsciiLetter(text[position]))
        {
            position++;
        }

        return text[start..position];
    }

    /// <summary>Parses the ISO dates that the JavaScript <c>Date</c> reads, which Mermaid accepts when the format does not match: <c>2014-01-06</c>, <c>2014-01-06T10:30</c>, <c>2014-01-06T10:30:15</c>, <c>2014-01-06T10:30:15.250</c>.</summary>
    public static bool TryParseIso(string text, out DateTime date)
    {
        date = default;
        if (text.Length is not (10 or 16 or 19 or 23) || text[4] != '-' || text[7] != '-')
            return false;

        if (!TryNumber(text, 0, 4, out var year) || !TryNumber(text, 5, 2, out var month) || !TryNumber(text, 8, 2, out var day))
            return false;

        int hour = 0, minute = 0, second = 0, millisecond = 0;
        if (text.Length >= 16 && (text[10] != 'T' || text[13] != ':' || !TryNumber(text, 11, 2, out hour) || !TryNumber(text, 14, 2, out minute)))
            return false;

        if (text.Length >= 19 && (text[16] != ':' || !TryNumber(text, 17, 2, out second)))
            return false;

        if (text.Length is 23 && (text[19] != '.' || !TryNumber(text, 20, 3, out millisecond)))
            return false;

        if (year is < 1 or > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 59)
            return false;

        date = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified);
        return true;

        static bool TryNumber(string text, int start, int length, out int value)
        {
            value = 0;
            for (var i = start; i < start + length; i++)
            {
                if (!char.IsAsciiDigit(text[i]))
                    return false;

                value = (value * 10) + (text[i] - '0');
            }

            return true;
        }
    }

    public static string FormatIso(DateTime date) => date.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);
}
