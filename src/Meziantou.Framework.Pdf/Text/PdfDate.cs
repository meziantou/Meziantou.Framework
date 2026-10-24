using System.Globalization;

namespace Meziantou.Framework.Pdf.Text;

/// <summary>Dates (ISO 32000-2, 7.9.4): <c>D:YYYYMMDDHHmmSSOHH'mm</c>, where every field after the year is optional.</summary>
internal static class PdfDate
{
    public static string Format(DateTimeOffset value)
    {
        var text = value.ToString("'D:'yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        if (value.Offset == TimeSpan.Zero)
            return text + "Z";

        var offset = value.Offset;
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        offset = offset.Duration();
        return string.Create(CultureInfo.InvariantCulture, $"{text}{sign}{offset.Hours:00}'{offset.Minutes:00}");
    }

    /// <summary>Parses a date leniently: the prefix is optional, a missing field takes its default value, a field out of range makes the date invalid.</summary>
    public static bool TryParse(string? text, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var span = text.AsSpan().Trim();
        if (span.StartsWith("D:", StringComparison.Ordinal))
        {
            span = span[2..];
        }

        var position = 0;
        if (!TryReadField(span, ref position, 4, out var year))
            return false;

        var month = TryReadField(span, ref position, 2, out var m) ? m : 1;
        var day = TryReadField(span, ref position, 2, out var d) ? d : 1;
        var hour = TryReadField(span, ref position, 2, out var h) ? h : 0;
        var minute = TryReadField(span, ref position, 2, out var min) ? min : 0;
        var second = TryReadField(span, ref position, 2, out var s) ? s : 0;

        var offset = TimeSpan.Zero;
        if (position < span.Length)
        {
            var sign = span[position];
            if (sign is '+' or '-')
            {
                position++;
                var offsetHours = TryReadField(span, ref position, 2, out var oh) ? oh : 0;
                if (position < span.Length && span[position] == '\'')
                {
                    position++;
                }

                var offsetMinutes = TryReadField(span, ref position, 2, out var om) ? om : 0;
                if (offsetHours > 23 || offsetMinutes > 59)
                    return false;

                offset = new TimeSpan(offsetHours, offsetMinutes, 0);
                if (sign == '-')
                {
                    offset = -offset;
                }
            }
        }

        if (year is < 1 or > 9999 || month is < 1 or > 12 || hour > 23 || minute > 59 || second > 59 || day < 1 || day > DateTime.DaysInMonth(year, month))
            return false;

        value = new DateTimeOffset(year, month, day, hour, minute, second, offset);
        return true;
    }

    private static bool TryReadField(ReadOnlySpan<char> span, ref int position, int length, out int value)
    {
        value = 0;
        if (position + length > span.Length)
            return false;

        for (var i = 0; i < length; i++)
        {
            var c = span[position + i];
            if (c is < '0' or > '9')
                return false;

            value = (value * 10) + (c - '0');
        }

        position += length;
        return true;
    }
}
