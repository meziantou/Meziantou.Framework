using System.Numerics;

namespace Meziantou.Framework.Csv.Serialization.Converters;

/// <summary>
/// Parses the ISO 8601 round-trip format (<c>O</c>), which the converters write by default, faster than the general parsers of the BCL.
/// A value in another format is not parsed: the converters then use the general parsers.
/// </summary>
internal static class Iso8601
{
    public static bool TryParse(ReadOnlySpan<char> value, out DateTime result) => TryParseUtcOrUnspecified(value, out result);

    public static bool TryParse(ReadOnlySpan<byte> value, out DateTime result) => TryParseUtcOrUnspecified(value, out result);

    // yyyy-MM-ddTHH:mm:ss.fffffff, with an optional Z. A value with an offset is converted to the local time: the general parser does it.
    private static bool TryParseUtcOrUnspecified<TChar>(ReadOnlySpan<TChar> value, out DateTime result)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = default;
        DateTimeKind kind;
        if (value.Length == 27)
        {
            kind = DateTimeKind.Unspecified;
        }
        else if (value.Length == 28 && IsChar(value[27], 'Z'))
        {
            kind = DateTimeKind.Utc;
        }
        else
        {
            return false;
        }

        if (!TryParseTicks(value[..27], out var ticks))
            return false;

        result = new DateTime(ticks, kind);
        return true;
    }

    // yyyy-MM-ddTHH:mm:ss.fffffff
    private static bool TryParseTicks<TChar>(ReadOnlySpan<TChar> value, out long ticks)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        ticks = 0;
        if (value.Length != 27 || !IsChar(value[4], '-') || !IsChar(value[7], '-') || !IsChar(value[10], 'T') || !IsChar(value[13], ':') || !IsChar(value[16], ':') || !IsChar(value[19], '.'))
            return false;

        if (!TryParseDigits(value[..4], out var year) || !TryParseDigits(value.Slice(5, 2), out var month) || !TryParseDigits(value.Slice(8, 2), out var day) ||
            !TryParseDigits(value.Slice(11, 2), out var hour) || !TryParseDigits(value.Slice(14, 2), out var minute) || !TryParseDigits(value.Slice(17, 2), out var second) ||
            !TryParseDigits(value.Slice(20, 7), out var fraction))
        {
            return false;
        }

        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 59)
            return false;

        ticks = new DateTime(year, month, day, hour, minute, second).Ticks + fraction;
        return true;
    }

    public static bool TryParse(ReadOnlySpan<char> value, out DateTimeOffset result) => TryParseWithOffset(value, out result);

    public static bool TryParse(ReadOnlySpan<byte> value, out DateTimeOffset result) => TryParseWithOffset(value, out result);

    // yyyy-MM-ddTHH:mm:ss.fffffff, followed by Z or by an offset (+HH:mm or -HH:mm). Only a value with an explicit offset: without one,
    // DateTimeOffset.Parse uses the local offset of the machine.
    private static bool TryParseWithOffset<TChar>(ReadOnlySpan<TChar> value, out DateTimeOffset result)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = default;
        long offsetTicks;
        if (value.Length == 28 && IsChar(value[27], 'Z'))
        {
            offsetTicks = 0;
        }
        else if (value.Length == 33 && (IsChar(value[27], '+') || IsChar(value[27], '-')) && IsChar(value[30], ':') &&
            TryParseDigits(value.Slice(28, 2), out var offsetHours) && TryParseDigits(value.Slice(31, 2), out var offsetMinutes) && offsetMinutes <= 59)
        {
            // DateTimeOffset accepts offsets up to 14 hours
            offsetTicks = ((offsetHours * 60L) + offsetMinutes) * TimeSpan.TicksPerMinute;
            if (offsetTicks > 14 * TimeSpan.TicksPerHour)
                return false;

            if (IsChar(value[27], '-'))
            {
                offsetTicks = -offsetTicks;
            }
        }
        else
        {
            return false;
        }

        if (!TryParseTicks(value[..27], out var ticks))
            return false;

        // The UTC time must be a valid DateTime too
        var utcTicks = ticks - offsetTicks;
        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
            return false;

        result = new DateTimeOffset(ticks, new TimeSpan(offsetTicks));
        return true;
    }

    // yyyy-MM-dd
    public static bool TryParse<TChar>(ReadOnlySpan<TChar> value, out DateOnly result)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = default;
        if (value.Length != 10 || !IsChar(value[4], '-') || !IsChar(value[7], '-'))
            return false;

        if (!TryParseDigits(value[..4], out var year) || !TryParseDigits(value.Slice(5, 2), out var month) || !TryParseDigits(value.Slice(8, 2), out var day))
            return false;

        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            return false;

        result = new DateOnly(year, month, day);
        return true;
    }

    // HH:mm:ss, with an optional fraction of 1 to 7 digits
    public static bool TryParse<TChar>(ReadOnlySpan<TChar> value, out TimeOnly result)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = default;
        if (value.Length < 8 || !IsChar(value[2], ':') || !IsChar(value[5], ':'))
            return false;

        if (!TryParseDigits(value[..2], out var hour) || !TryParseDigits(value.Slice(3, 2), out var minute) || !TryParseDigits(value.Slice(6, 2), out var second))
            return false;

        if (hour > 23 || minute > 59 || second > 59)
            return false;

        long ticks = 0;
        if (value.Length > 8)
        {
            var fraction = value[9..];
            if (!IsChar(value[8], '.') || fraction.Length is 0 or > 7 || !TryParseDigits(fraction, out var digits))
                return false;

            ticks = digits;
            for (var i = fraction.Length; i < 7; i++)
            {
                ticks *= 10;
            }
        }

        result = new TimeOnly((((hour * 60L) + minute) * 60 + second) * TimeSpan.TicksPerSecond + ticks);
        return true;
    }

    private static bool IsChar<TChar>(TChar value, char expected)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        return value == TChar.CreateTruncating(expected);
    }

    private static bool TryParseDigits<TChar>(ReadOnlySpan<TChar> value, out int result)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = 0;
        foreach (var c in value)
        {
            var digit = int.CreateTruncating(c) - '0';
            if ((uint)digit > 9)
                return false;

            result = (result * 10) + digit;
        }

        return true;
    }
}
