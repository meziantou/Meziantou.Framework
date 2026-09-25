namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>Reads the numbers, dates, and times TOML writes as bare words, following the grammar of the specification.</summary>
/// <remarks>
/// Nothing here is delegated to a culture-aware or lenient .NET parser: <see cref="DateTimeOffset.TryParse(string?, out DateTimeOffset)"/>
/// accepts <c>1/2/2020</c> and <c>Jan 1 2020</c>, and <see cref="long.TryParse(string?, out long)"/> accepts leading zeros.
/// The grammar is checked first, and only text it accepts is converted.
/// </remarks>
internal static class ScalarParser
{
    public enum Result
    {
        /// <summary>The text is not shaped like this kind of value at all.</summary>
        NoMatch,

        Success,

        /// <summary>The text is shaped like this kind of value but breaks one of its rules, such as a month of 13.</summary>
        Invalid,

        /// <summary>The text is valid TOML, but the value does not fit the .NET type that holds it.</summary>
        Unsupported,
    }

    /// <summary>Reads an integer: decimal with an optional sign, or <c>0x</c>, <c>0o</c>, <c>0b</c> without one.</summary>
    public static Result ParseInteger(string text, out long value)
    {
        value = 0;
        if (text.Length > 2 && text[0] == '0' && text[1] is 'x' or 'o' or 'b')
        {
            var (radix, isDigit) = text[1] switch
            {
                'x' => (16, (Func<char, bool>)char.IsAsciiHexDigit),
                'o' => (8, static c => c is >= '0' and <= '7'),
                _ => (2, static c => c is '0' or '1'),
            };

            var index = 2;
            if (!ScanDigitGroup(text, ref index, isDigit) || index != text.Length)
                return Result.NoMatch;

            ulong result = 0;
            foreach (var character in text.AsSpan(2))
            {
                if (character == '_')
                    continue;

                result = (result * (ulong)radix) + (ulong)HexValue(character);
                if (result > long.MaxValue)
                    return Result.Unsupported;
            }

            value = (long)result;
            return Result.Success;
        }

        var position = 0;
        if (!ScanDecimalInteger(text, ref position) || position != text.Length)
            return Result.NoMatch;

        return long.TryParse(RemoveUnderscores(text), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value) ? Result.Success : Result.Unsupported;
    }

    /// <summary>Reads a float: a decimal integer part with a fraction, an exponent, or both, or one of <c>inf</c> and <c>nan</c>.</summary>
    public static Result ParseFloat(string text, out double value)
    {
        value = 0;
        var unsigned = text is ['+' or '-', ..] ? text.AsSpan(1) : text.AsSpan();
        if (unsigned is "inf")
        {
            value = text[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
            return Result.Success;
        }

        if (unsigned is "nan")
        {
            value = double.NaN;
            return Result.Success;
        }

        var position = 0;
        if (!ScanDecimalInteger(text, ref position))
            return Result.NoMatch;

        var hasFraction = false;
        if (position < text.Length && text[position] == '.')
        {
            position++;
            if (!ScanDigitGroup(text, ref position, char.IsAsciiDigit))
                return Result.NoMatch;

            hasFraction = true;
        }

        var hasExponent = false;
        if (position < text.Length && text[position] is 'e' or 'E')
        {
            position++;
            if (position < text.Length && text[position] is '+' or '-')
            {
                position++;
            }

            if (!ScanDigitGroup(text, ref position, char.IsAsciiDigit))
                return Result.NoMatch;

            hasExponent = true;
        }

        if (position != text.Length || !(hasFraction || hasExponent))
            return Result.NoMatch;

        value = double.Parse(RemoveUnderscores(text), NumberStyles.Float, CultureInfo.InvariantCulture);
        return Result.Success;
    }

    /// <summary>Reads an offset date-time, a local date-time, a local date, or a local time.</summary>
    /// <param name="text">The text to read.</param>
    /// <param name="kind">The token kind the text spells when it is shaped like a date or a time.</param>
    /// <param name="value">A <see cref="DateTimeOffset"/>, <see cref="DateTime"/>, <see cref="DateOnly"/>, or <see cref="TimeOnly"/>.</param>
    /// <param name="omitsSeconds">Whether the time leaves out its seconds, which only TOML 1.1 allows.</param>
    /// <param name="reason">Why a valid date or time is <see cref="Result.Unsupported"/>.</param>
    /// <remarks>
    /// An offset further from UTC than .NET allows (±14:00) still names an instant, so the value is that instant in UTC
    /// rather than an error.
    /// </remarks>
    public static Result ParseDateTime(string text, out SyntaxKind kind, out object? value, out bool omitsSeconds, out string? reason)
    {
        kind = SyntaxKind.None;
        value = null;
        omitsSeconds = false;
        reason = null;

        var hasDate = text.Length >= 5 && IsDigits(text, 0, 4) && text[4] == '-';
        var hasTimeOnly = !hasDate && text.Length >= 3 && IsDigits(text, 0, 2) && text[2] == ':';
        if (!hasDate && !hasTimeOnly)
            return Result.NoMatch;

        int year = 0, month = 0, day = 0;
        var position = 0;
        if (hasDate)
        {
            if (text.Length < 10 || !IsDigits(text, 5, 2) || text[7] != '-' || !IsDigits(text, 8, 2))
                return Invalid(ref kind, SyntaxKind.LocalDateToken);

            year = ReadNumber(text, 0, 4);
            month = ReadNumber(text, 5, 2);
            day = ReadNumber(text, 8, 2);
            if (month is < 1 or > 12 || day < 1 || day > (year == 0 ? DaysInMonthOfYearZero(month) : DateTime.DaysInMonth(year, month)))
                return Invalid(ref kind, SyntaxKind.LocalDateToken);

            if (text.Length == 10)
            {
                kind = SyntaxKind.LocalDateToken;
                if (year == 0)
                    return Unsupported(out reason, "the year 0 is before the first year .NET represents");

                value = new DateOnly(year, month, day);
                return Result.Success;
            }

            if (text[10] is not ('T' or 't' or ' '))
                return Invalid(ref kind, SyntaxKind.LocalDateTimeToken);

            position = 11;
        }

        // hour ":" minute [ ":" second [ "." fraction ] ]
        if (text.Length < position + 5 || !IsDigits(text, position, 2) || text[position + 2] != ':' || !IsDigits(text, position + 3, 2))
            return Invalid(ref kind, hasDate ? SyntaxKind.LocalDateTimeToken : SyntaxKind.LocalTimeToken);

        var hour = ReadNumber(text, position, 2);
        var minute = ReadNumber(text, position + 3, 2);
        position += 5;

        var second = 0;
        long fractionTicks = 0;
        if (position < text.Length && text[position] == ':')
        {
            if (!IsDigits(text, position + 1, 2))
                return Invalid(ref kind, hasDate ? SyntaxKind.LocalDateTimeToken : SyntaxKind.LocalTimeToken);

            second = ReadNumber(text, position + 1, 2);
            position += 3;
            if (position < text.Length && text[position] == '.')
            {
                position++;
                var fractionStart = position;
                while (position < text.Length && char.IsAsciiDigit(text[position]))
                {
                    position++;
                }

                if (position == fractionStart)
                    return Invalid(ref kind, hasDate ? SyntaxKind.LocalDateTimeToken : SyntaxKind.LocalTimeToken);

                // The specification asks for extra precision to be truncated rather than rounded.
                for (var i = 0; i < 7; i++)
                {
                    var index = fractionStart + i;
                    fractionTicks = (fractionTicks * 10) + (index < position ? text[index] - '0' : 0);
                }
            }
        }
        else
        {
            omitsSeconds = true;
        }

        var offsetMinutes = 0;
        var hasOffset = false;
        if (position < text.Length)
        {
            if (!hasDate)
                return Invalid(ref kind, SyntaxKind.LocalTimeToken);

            if (text[position] is 'Z' or 'z' && position + 1 == text.Length)
            {
                hasOffset = true;
            }
            else if (text[position] is '+' or '-' && text.Length == position + 6 && IsDigits(text, position + 1, 2) && text[position + 3] == ':' && IsDigits(text, position + 4, 2))
            {
                var offsetHour = ReadNumber(text, position + 1, 2);
                var offsetMinute = ReadNumber(text, position + 4, 2);
                if (offsetHour > 23 || offsetMinute > 59)
                    return Invalid(ref kind, SyntaxKind.OffsetDateTimeToken);

                offsetMinutes = ((offsetHour * 60) + offsetMinute) * (text[position] == '-' ? -1 : 1);
                hasOffset = true;
            }
            else
            {
                return Invalid(ref kind, SyntaxKind.OffsetDateTimeToken);
            }
        }

        kind = hasOffset ? SyntaxKind.OffsetDateTimeToken : hasDate ? SyntaxKind.LocalDateTimeToken : SyntaxKind.LocalTimeToken;
        if (hour > 23 || minute > 59 || second > 60)
            return Result.Invalid;

        if (second == 60)
            return Unsupported(out reason, "a leap second cannot be represented in .NET");

        if (!hasDate)
        {
            value = new TimeOnly(hour, minute, second).Add(TimeSpan.FromTicks(fractionTicks));
            return Result.Success;
        }

        if (year == 0)
            return Unsupported(out reason, "the year 0 is before the first year .NET represents");

        var dateTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified).AddTicks(fractionTicks);
        if (!hasOffset)
        {
            value = dateTime;
            return Result.Success;
        }

        try
        {
            var offset = TimeSpan.FromMinutes(offsetMinutes);
            value = Math.Abs(offsetMinutes) <= 14 * 60
                ? new DateTimeOffset(dateTime, offset)
                : new DateTimeOffset(DateTime.SpecifyKind(dateTime - offset, DateTimeKind.Unspecified), TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Unsupported(out reason, "the instant is outside the range of DateTimeOffset");
        }

        return Result.Success;

        static Result Invalid(ref SyntaxKind kind, SyntaxKind guess)
        {
            kind = guess;
            return Result.Invalid;
        }

        static Result Unsupported(out string reason, string message)
        {
            reason = message;
            return Result.Unsupported;
        }
    }

    /// <summary>Reads <c>0</c>, or a digit other than zero followed by digits, with an optional sign in front.</summary>
    private static bool ScanDecimalInteger(string text, ref int position)
    {
        if (position < text.Length && text[position] is '+' or '-')
        {
            position++;
        }

        if (position < text.Length && text[position] == '0')
        {
            position++;
            return true;
        }

        return ScanDigitGroup(text, ref position, char.IsAsciiDigit);
    }

    /// <summary>Reads digits, allowing one underscore between two of them.</summary>
    private static bool ScanDigitGroup(string text, ref int position, Func<char, bool> isDigit)
    {
        if (position >= text.Length || !isDigit(text[position]))
            return false;

        position++;
        while (position < text.Length)
        {
            if (isDigit(text[position]))
            {
                position++;
            }
            else if (text[position] == '_' && position + 1 < text.Length && isDigit(text[position + 1]))
            {
                position += 2;
            }
            else
            {
                break;
            }
        }

        return true;
    }

    private static string RemoveUnderscores(string text) => text.Contains('_', StringComparison.Ordinal) ? text.Replace("_", "", StringComparison.Ordinal) : text;

    private static bool IsDigits(string text, int start, int count)
    {
        if (start + count > text.Length)
            return false;

        for (var i = start; i < start + count; i++)
        {
            if (!char.IsAsciiDigit(text[i]))
                return false;
        }

        return true;
    }

    private static int ReadNumber(string text, int start, int count)
    {
        var result = 0;
        for (var i = start; i < start + count; i++)
        {
            result = (result * 10) + (text[i] - '0');
        }

        return result;
    }

    /// <summary>Year 0 is a leap year in the proleptic Gregorian calendar RFC 3339 uses, though .NET has no such year.</summary>
    private static int DaysInMonthOfYearZero(int month) => month == 2 ? 29 : DateTime.DaysInMonth(2000, month);

    private static int HexValue(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        _ => value - 'A' + 10,
    };
}
