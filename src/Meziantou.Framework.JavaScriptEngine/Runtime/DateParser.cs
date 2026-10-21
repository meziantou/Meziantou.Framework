using System.Runtime.InteropServices;
using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// Date.parse: the Date Time String Format (https://tc39.es/ecma262/#sec-date-time-string-format), then the formats of
/// toString and toUTCString and the usual legacy formats, with the heuristics of V8 ("Jan 1 2000 10:00 GMT+0100",
/// "1/2/2000", "2000-1-2 10:00:00"...). It never uses the culture-dependent parsing of .NET.
/// </summary>
internal static class DateParser
{
    private const int None = int.MinValue;

    private static readonly string[] MonthPrefixes = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    public static double Parse(string value, NamedTimeZone timeZone)
    {
        if (TryParseIso(value, timeZone, out var result))
            return result;

        return ParseLegacy(value, timeZone);
    }

    /// <summary>The Date Time String Format: date-only forms are UTC, date-time forms without offset are local time.</summary>
    private static bool TryParseIso(string value, NamedTimeZone timeZone, out double result)
    {
        result = double.NaN;
        var position = 0;
        double year;
        if (position < value.Length && value[position] is '+' or '-')
        {
            var negative = value[position] == '-';
            position++;
            if (!TryReadDigits(value, ref position, 6, out var expandedYear))
                return false;

            // -000000 is not a valid year
            if (negative && expandedYear == 0)
                return false;

            year = negative ? -expandedYear : expandedYear;
        }
        else
        {
            if (!TryReadDigits(value, ref position, 4, out var y))
                return false;

            year = y;
        }

        var month = 1;
        var day = 1;
        if (position < value.Length && value[position] == '-')
        {
            position++;
            if (!TryReadDigits(value, ref position, 2, out month) || month is < 1 or > 12)
                return false;

            if (position < value.Length && value[position] == '-')
            {
                position++;
                if (!TryReadDigits(value, ref position, 2, out day) || day is < 1 or > 31)
                    return false;
            }
        }

        if (position == value.Length)
        {
            result = DateOperations.TimeClip(DateOperations.MakeDate(DateOperations.MakeDay(year, month - 1, day), 0));
            return true;
        }

        if (value[position] != 'T')
            return false;

        position++;
        if (!TryReadDigits(value, ref position, 2, out var hour) || !TryReadChar(value, ref position, ':') || !TryReadDigits(value, ref position, 2, out var minute))
            return false;

        var second = 0;
        var millisecond = 0;
        if (position < value.Length && value[position] == ':')
        {
            position++;
            if (!TryReadDigits(value, ref position, 2, out second))
                return false;

            if (position < value.Length && value[position] == '.')
            {
                position++;
                var start = position;
                while (position < value.Length && char.IsAsciiDigit(value[position]))
                {
                    position++;
                }

                if (position == start)
                    return false;

                millisecond = ReadMilliseconds(value.AsSpan(start, position - start));
            }
        }

        if (hour > 24 || minute > 59 || second > 59 || (hour == 24 && (minute != 0 || second != 0 || millisecond != 0)))
            return false;

        var hasOffset = false;
        var offsetMinutes = 0;
        if (position < value.Length)
        {
            if (value[position] == 'Z')
            {
                position++;
                hasOffset = true;
            }
            else if (value[position] is '+' or '-')
            {
                var sign = value[position] == '-' ? -1 : 1;
                position++;
                if (!TryReadDigits(value, ref position, 2, out var offsetHours) || !TryReadChar(value, ref position, ':') || !TryReadDigits(value, ref position, 2, out var offsetMinute))
                    return false;

                if (offsetHours > 23 || offsetMinute > 59)
                    return false;

                hasOffset = true;
                offsetMinutes = sign * ((offsetHours * 60) + offsetMinute);
            }

            if (position != value.Length)
                return false;
        }

        var date = DateOperations.MakeDate(DateOperations.MakeDay(year, month - 1, day), DateOperations.MakeTime(hour, minute, second, millisecond));
        date = hasOffset ? date - (offsetMinutes * DateOperations.MsPerMinute) : DateOperations.Utc(date, timeZone);
        result = DateOperations.TimeClip(date);
        return true;
    }

    private static bool TryReadDigits(string value, ref int position, int count, out int result)
    {
        result = 0;
        if (position + count > value.Length)
            return false;

        for (var i = 0; i < count; i++)
        {
            var c = value[position + i];
            if (!char.IsAsciiDigit(c))
                return false;

            result = (result * 10) + (c - '0');
        }

        position += count;
        return true;
    }

    private static bool TryReadChar(string value, ref int position, char c)
    {
        if (position < value.Length && value[position] == c)
        {
            position++;
            return true;
        }

        return false;
    }

    /// <summary>The milliseconds of a fraction of a second (the first three digits).</summary>
    private static int ReadMilliseconds(ReadOnlySpan<char> digits)
    {
        var result = 0;
        for (var i = 0; i < 3; i++)
        {
            result = (result * 10) + (i < digits.Length ? digits[i] - '0' : 0);
        }

        return result;
    }

    /// <summary>The legacy formats, like the DateParser of V8.</summary>
    private static double ParseLegacy(string value, NamedTimeZone timeZone)
    {
        var tokenizer = new Tokenizer(value);
        var day = new DayComposer();
        var time = new TimeComposer();
        var tz = new TimeZoneComposer();
        var hasReadNumber = false;
        while (true)
        {
            var token = tokenizer.Next();
            if (token.Kind == TokenKind.End)
                break;

            switch (token.Kind)
            {
                case TokenKind.Number:
                {
                    hasReadNumber = true;
                    var n = token.Number;
                    if (tokenizer.SkipSymbol(':'))
                    {
                        if (tokenizer.SkipSymbol(':'))
                        {
                            // "n::"
                            if (!time.IsEmpty)
                                return double.NaN;

                            time.Add(n);
                            time.Add(0);
                        }
                        else
                        {
                            // "n:"
                            if (!time.Add(n))
                                return double.NaN;

                            if (tokenizer.Peek().IsSymbol('.'))
                            {
                                tokenizer.Next();
                            }
                        }
                    }
                    else if (tokenizer.Peek().IsSymbol('.') && time.IsExpecting(n))
                    {
                        tokenizer.Next();
                        time.Add(n);
                        var next = tokenizer.Peek();
                        if (next.Kind != TokenKind.Number)
                            return double.NaN;

                        tokenizer.Next();
                        time.AddFinal(ReadMilliseconds(next.Text));
                    }
                    else if (tz.IsExpecting(n))
                    {
                        tz.Minute = n;
                    }
                    else if (time.IsExpecting(n))
                    {
                        time.AddFinal(n);

                        // The time must be followed by the end, a white space, "Z" or a sign
                        var next = tokenizer.Peek();
                        if (next.Kind is not (TokenKind.End or TokenKind.WhiteSpace) && !next.IsSign && !next.IsWord("z"))
                            return double.NaN;
                    }
                    else
                    {
                        if (!day.Add(n, token.Text.Length))
                            return double.NaN;

                        tokenizer.SkipSymbol('-');
                    }

                    break;
                }

                case TokenKind.Word:
                {
                    var keyword = GetKeyword(token.Text, out var keywordValue);
                    if (keyword == Keyword.AmPm && !time.IsEmpty)
                    {
                        time.HourOffset = keywordValue;
                    }
                    else if (keyword == Keyword.Month)
                    {
                        if (day.NamedMonth != None)
                            return double.NaN;

                        day.NamedMonth = keywordValue;
                        tokenizer.SkipSymbol('-');
                    }
                    else if (keyword == Keyword.TimeZone && hasReadNumber)
                    {
                        tz.Set(keywordValue);
                    }
                    else
                    {
                        // Garbage words are illegal once a number has been read, and must be separated from the first number
                        if (hasReadNumber || tokenizer.Peek().Kind == TokenKind.Number)
                            return double.NaN;
                    }

                    break;
                }

                case TokenKind.Symbol when token.IsSign && (tz.IsUtc || !time.IsEmpty):
                {
                    // A UTC offset, after a time or "GMT"
                    tz.Sign = token.Symbol == '-' ? -1 : 1;
                    var n = 0;
                    var length = 0;
                    if (tokenizer.Peek().Kind == TokenKind.Number)
                    {
                        var next = tokenizer.Next();
                        n = next.Number;
                        length = next.Text.Length;
                    }

                    hasReadNumber = true;
                    if (tokenizer.Peek().IsSymbol(':'))
                    {
                        tz.Hour = n;
                        tz.Minute = None;
                    }
                    else if (length is 1 or 2)
                    {
                        tz.Hour = n;
                        tz.Minute = 0;
                    }
                    else if (length is 3 or 4)
                    {
                        tz.Hour = n / 100;
                        tz.Minute = n % 100;
                    }
                    else
                    {
                        return double.NaN;
                    }

                    break;
                }

                case TokenKind.Symbol when (token.IsSign || token.Symbol == ')') && hasReadNumber:
                    return double.NaN;

                default:
                    // The other characters and the white spaces are ignored
                    break;
            }
        }

        if (!day.TryWrite(out var year, out var month, out var dayOfMonth) || !time.TryWrite(out var hour, out var minute, out var second, out var millisecond))
            return double.NaN;

        var date = DateOperations.MakeDate(DateOperations.MakeDay(year, month, dayOfMonth), DateOperations.MakeTime(hour, minute, second, millisecond));
        if (tz.Sign != None)
        {
            var hours = tz.Hour == None ? 0 : tz.Hour;
            var minutes = tz.Minute == None ? 0 : tz.Minute;
            date -= tz.Sign * ((hours * 60) + minutes) * DateOperations.MsPerMinute;
        }
        else
        {
            date = DateOperations.Utc(date, timeZone);
        }

        return DateOperations.TimeClip(date);
    }

    private enum Keyword
    {
        None,
        Month,
        AmPm,
        TimeZone,
    }

    private static Keyword GetKeyword(string word, out int value)
    {
        value = 0;
        var lower = word.ToLowerInvariant();

        // A month name matches with its first three letters ("Jan", "January")
        if (lower.Length >= 3)
        {
            var index = Array.IndexOf(MonthPrefixes, lower[..3]);
            if (index >= 0)
            {
                value = index + 1;
                return Keyword.Month;
            }
        }

        switch (lower)
        {
            case "am":
                value = 0;
                return Keyword.AmPm;
            case "pm":
                value = 12;
                return Keyword.AmPm;
            case "ut" or "utc" or "z" or "gmt":
                value = 0;
                return Keyword.TimeZone;
            case "edt":
                value = -4;
                return Keyword.TimeZone;
            case "est" or "cdt":
                value = -5;
                return Keyword.TimeZone;
            case "cst" or "mdt":
                value = -6;
                return Keyword.TimeZone;
            case "mst" or "pdt":
                value = -7;
                return Keyword.TimeZone;
            case "pst":
                value = -8;
                return Keyword.TimeZone;
            default:
                return Keyword.None;
        }
    }

    private enum TokenKind
    {
        End,
        Number,
        Word,
        Symbol,
        WhiteSpace,
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Token(TokenKind Kind, string Text, int Number, char Symbol)
    {
        public bool IsSign => Kind == TokenKind.Symbol && Symbol is '+' or '-';

        public bool IsSymbol(char c) => Kind == TokenKind.Symbol && Symbol == c;

        public bool IsWord(string word) => Kind == TokenKind.Word && string.Equals(Text, word, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Tokenizer(string value)
    {
        private int _position;
        private Token? _peeked;

        public Token Peek() => _peeked ??= Read();

        public Token Next()
        {
            var result = Peek();
            _peeked = null;
            return result;
        }

        public bool SkipSymbol(char c)
        {
            if (Peek().IsSymbol(c))
            {
                _peeked = null;
                return true;
            }

            return false;
        }

        private Token Read()
        {
            while (true)
            {
                if (_position >= value.Length)
                    return new Token(TokenKind.End, "", 0, '\0');

                var c = value[_position];
                var start = _position;
                if (char.IsAsciiDigit(c))
                {
                    long number = 0;
                    while (_position < value.Length && char.IsAsciiDigit(value[_position]))
                    {
                        if (number <= int.MaxValue)
                        {
                            number = (number * 10) + (value[_position] - '0');
                        }

                        _position++;
                    }

                    // An out of range number is never valid in a date
                    return new Token(TokenKind.Number, value[start.._position], number > int.MaxValue ? int.MaxValue : (int)number, '\0');
                }

                if (char.IsAsciiLetter(c))
                {
                    while (_position < value.Length && char.IsAsciiLetter(value[_position]))
                    {
                        _position++;
                    }

                    return new Token(TokenKind.Word, value[start.._position], 0, '\0');
                }

                if (IsWhiteSpace(c))
                {
                    while (_position < value.Length && IsWhiteSpace(value[_position]))
                    {
                        _position++;
                    }

                    return new Token(TokenKind.WhiteSpace, " ", 0, '\0');
                }

                if (c == '(')
                {
                    // A comment, possibly nested
                    var depth = 0;
                    do
                    {
                        if (value[_position] == '(')
                        {
                            depth++;
                        }
                        else if (value[_position] == ')')
                        {
                            depth--;
                        }

                        _position++;
                    }
                    while (_position < value.Length && depth > 0);
                    continue;
                }

                _position++;
                return new Token(TokenKind.Symbol, value[start.._position], 0, c);
            }
        }

        private static bool IsWhiteSpace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\v' or '\f' or '\u00A0' or '\uFEFF' or '\u2028' or '\u2029' || (c > 127 && char.IsWhiteSpace(c));
    }

    private sealed class DayComposer
    {
        private readonly int[] _components = new int[3];
        private readonly int[] _lengths = new int[3];
        private int _count;

        public int NamedMonth { get; set; } = None;

        public bool Add(int value, int length)
        {
            if (_count == _components.Length)
                return false;

            _lengths[_count] = length;
            _components[_count++] = value;
            return true;
        }

        public bool TryWrite(out double year, out double month, out double day)
        {
            year = month = day = 0;
            if (_count < 1)
                return false;

            // The missing components are 1 (the year 1 is then 2001, like V8)
            for (var i = _count; i < _components.Length; i++)
            {
                _components[i] = 1;
                _lengths[i] = 1;
            }

            int y;
            int m;
            int d;
            int yearLength;
            if (NamedMonth == None)
            {
                if (!IsDay(_components[0]))
                {
                    // YMD
                    y = _components[0];
                    yearLength = _lengths[0];
                    m = _components[1];
                    d = _components[2];
                }
                else
                {
                    // MDY
                    m = _components[0];
                    d = _components[1];
                    y = _components[2];
                    yearLength = _lengths[2];
                }
            }
            else
            {
                m = NamedMonth;
                if (!IsDay(_components[0]))
                {
                    // YMD, MYD or YDM
                    y = _components[0];
                    yearLength = _lengths[0];
                    d = _components[1];
                }
                else
                {
                    // DMY, MDY or DYM
                    d = _components[0];
                    y = _components[1];
                    yearLength = _lengths[1];
                }
            }

            // A year of one or two digits is 19xx or 20xx; "0001" (toString) is the year 1
            if (yearLength > 2)
            {
            }
            else if (y is >= 0 and <= 49)
            {
                y += 2000;
            }
            else if (y is >= 50 and <= 99)
            {
                y += 1900;
            }

            if (m is < 1 or > 12 || !IsDay(d))
                return false;

            year = y;
            month = m - 1;
            day = d;
            return true;
        }

        private static bool IsDay(int value) => value is >= 1 and <= 31;
    }

    private sealed class TimeComposer
    {
        private readonly int[] _components = new int[4];
        private int _count;

        public int HourOffset { get; set; } = None;

        public bool IsEmpty => _count == 0;

        public bool IsExpecting(int n) => (_count == 1 && n is >= 0 and <= 59) || (_count == 2 && n is >= 0 and <= 59) || (_count == 3 && n is >= 0 and <= 999);

        public bool Add(int value)
        {
            if (_count == _components.Length)
                return false;

            _components[_count++] = value;
            return true;
        }

        public void AddFinal(int value)
        {
            Add(value);
            while (_count < _components.Length)
            {
                _components[_count++] = 0;
            }
        }

        public bool TryWrite(out int hour, out int minute, out int second, out int millisecond)
        {
            while (_count < _components.Length)
            {
                _components[_count++] = 0;
            }

            hour = _components[0];
            minute = _components[1];
            second = _components[2];
            millisecond = _components[3];
            if (HourOffset != None)
            {
                if (hour is < 0 or > 12)
                    return false;

                hour = (hour % 12) + HourOffset;
            }

            if (hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 59 || millisecond is < 0 or > 999)
            {
                // The hour 24 is allowed for the end of the day
                if (hour != 24 || minute != 0 || second != 0 || millisecond != 0)
                    return false;
            }

            return true;
        }
    }

    private sealed class TimeZoneComposer
    {
        public int Sign { get; set; } = None;

        public int Hour { get; set; } = None;

        public int Minute { get; set; } = None;

        public bool IsUtc => Hour == 0 && Minute == 0;

        public bool IsExpecting(int n) => Hour != None && Minute == None && n is >= 0 and <= 59;

        public void Set(int offsetHours)
        {
            Sign = offsetHours < 0 ? -1 : 1;
            Hour = Math.Abs(offsetHours);
            Minute = 0;
        }
    }
}
