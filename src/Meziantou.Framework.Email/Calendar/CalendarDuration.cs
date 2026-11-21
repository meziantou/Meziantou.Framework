using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Email.Calendar;

/// <summary>Reads and writes the durations of iCalendar ([RFC 5545] 3.3.6): <c>P1W</c>, <c>PT1H30M</c>, <c>-P1D</c>.</summary>
internal static class CalendarDuration
{
    public static TimeSpan? Parse(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var negative = value[0] == '-';
        if (value[0] is '+' or '-')
        {
            value = value[1..];
        }

        if (value.Length < 2 || value[0] is not ('P' or 'p'))
        {
            return null;
        }

        var result = TimeSpan.Zero;
        var number = 0L;
        var hasNumber = false;
        var inTime = false;
        try
        {
            for (var i = 1; i < value.Length; i++)
            {
                var c = char.ToUpperInvariant(value[i]);
                if (char.IsAsciiDigit(c))
                {
                    number = checked((number * 10) + (c - '0'));
                    hasNumber = true;
                    continue;
                }

                if (c == 'T' && !hasNumber && !inTime)
                {
                    inTime = true;
                    continue;
                }

                if (!hasNumber)
                {
                    return null;
                }

                result += (c, inTime) switch
                {
                    ('W', false) => TimeSpan.FromDays(number * 7),
                    ('D', false) => TimeSpan.FromDays(number),
                    ('H', true) => TimeSpan.FromHours(number),
                    ('M', true) => TimeSpan.FromMinutes(number),
                    ('S', true) => TimeSpan.FromSeconds(number),
                    _ => throw new FormatException(),
                };

                number = 0;
                hasNumber = false;
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentOutOfRangeException)
        {
            return null;
        }

        return hasNumber ? null : negative ? -result : result;
    }

    public static string Format(TimeSpan value)
    {
        var builder = new StringBuilder();
        if (value < TimeSpan.Zero)
        {
            builder.Append('-');
            value = value.Duration();
        }

        builder.Append('P');
        if (value.Days > 0)
        {
            builder.Append(value.Days.ToString(CultureInfo.InvariantCulture)).Append('D');
        }

        if (value.Hours > 0 || value.Minutes > 0 || value.Seconds > 0 || value == TimeSpan.Zero)
        {
            builder.Append('T');
            if (value.Hours > 0)
            {
                builder.Append(value.Hours.ToString(CultureInfo.InvariantCulture)).Append('H');
            }

            if (value.Minutes > 0)
            {
                builder.Append(value.Minutes.ToString(CultureInfo.InvariantCulture)).Append('M');
            }

            if (value.Seconds > 0 || value == TimeSpan.Zero)
            {
                builder.Append(value.Seconds.ToString(CultureInfo.InvariantCulture)).Append('S');
            }
        }

        return builder.ToString();
    }
}
