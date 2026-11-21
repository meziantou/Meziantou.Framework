using System.Globalization;

namespace Meziantou.Framework.Email.Internals;

/// <summary>Parses and formats the dates of the headers ([RFC 5322] 3.3, with the obsolete syntax of 4.3).</summary>
internal static class MimeDate
{
    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    public static string Format(DateTimeOffset value)
    {
        var offset = value.Offset;
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        offset = offset.Duration();
        return value.ToString("ddd, d MMM yyyy HH':'mm':'ss ", CultureInfo.InvariantCulture) + sign + offset.Hours.ToString("00", CultureInfo.InvariantCulture) + offset.Minutes.ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>Parses a date such as <c>Tue, 1 Jul 2003 10:52:37 +0200</c>, leniently.</summary>
    public static DateTimeOffset? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Words and numbers, without the comments and the punctuation
        var tokens = new List<string>();
        var tokenizer = new MimeTokenizer(value);
        while (true)
        {
            tokenizer.SkipCfws();
            if (tokenizer.IsAtEnd)
            {
                break;
            }

            if (tokenizer.Current is ',')
            {
                tokenizer.Position++;
                continue;
            }

            var token = tokenizer.ReadToken(",");
            if (token.Length == 0)
            {
                tokenizer.Position++;
                continue;
            }

            tokens.Add(token);
        }

        int? day = null;
        int? month = null;
        int? year = null;
        TimeSpan? time = null;
        TimeSpan offset = TimeSpan.Zero;
        foreach (var token in tokens)
        {
            if (token.Contains(':', StringComparison.Ordinal) && time is null)
            {
                var parts = token.Split(':');
                if (parts.Length is 2 or 3 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hour) && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minute))
                {
                    var second = 0;
                    if (parts.Length == 3 && !int.TryParse(parts[2].Split('.')[0], NumberStyles.None, CultureInfo.InvariantCulture, out second))
                    {
                        second = 0;
                    }

                    if (hour < 24 && minute < 60 && second <= 60)
                    {
                        time = new TimeSpan(hour, minute, Math.Min(second, 59));
                    }
                }

                continue;
            }

            if (token.Length == 5 && token[0] is '+' or '-' && int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var zone))
            {
                var hours = zone / 100;
                var minutes = zone % 100;
                if (hours <= 14 && minutes < 60)
                {
                    offset = new TimeSpan(hours, minutes, 0) * (token[0] == '-' ? -1 : 1);
                }

                continue;
            }

            var monthIndex = Array.FindIndex(Months, m => token.StartsWith(m, StringComparison.OrdinalIgnoreCase));
            if (monthIndex >= 0 && month is null && token.Length >= 3 && char.IsLetter(token[0]))
            {
                month = monthIndex + 1;
                continue;
            }

            if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                if (day is null && number is >= 1 and <= 31 && token.Length <= 2)
                {
                    day = number;
                }
                else if (year is null)
                {
                    // Two-digit and three-digit years ([RFC 5322] 4.3)
                    year = token.Length switch
                    {
                        <= 2 => number < 50 ? 2000 + number : 1900 + number,
                        3 => 1900 + number,
                        _ => number,
                    };
                }

                continue;
            }

            if (GetObsoleteZone(token) is { } obsolete)
            {
                offset = obsolete;
            }
        }

        if (day is null || month is null || year is null || year < 1 || year > 9999 || day > DateTime.DaysInMonth(year.Value, month.Value))
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(year.Value, month.Value, day.Value, 0, 0, 0, offset) + (time ?? TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static TimeSpan? GetObsoleteZone(string token) => token.ToUpperInvariant() switch
    {
        "UT" or "UTC" or "GMT" or "Z" => TimeSpan.Zero,
        "EST" => TimeSpan.FromHours(-5),
        "EDT" => TimeSpan.FromHours(-4),
        "CST" => TimeSpan.FromHours(-6),
        "CDT" => TimeSpan.FromHours(-5),
        "MST" => TimeSpan.FromHours(-7),
        "MDT" => TimeSpan.FromHours(-6),
        "PST" => TimeSpan.FromHours(-8),
        "PDT" => TimeSpan.FromHours(-7),
        _ => null,
    };
}
