namespace Meziantou.Framework.SimpleQueryLanguage.Ranges;

internal static class RangeSyntax
{
    public static RangeSyntax<T>? Parse<T>(string? text, ScalarParser<T> scalarParser)
    {
        if (text is null)
            return null;

        ArgumentNullException.ThrowIfNull(scalarParser);

        if (scalarParser(text, out var simpleOperand))
            return new UnaryRangeSyntax<T>(KeyValueOperator.EqualTo, simpleOperand);

        var indexOfDotDot = text.IndexOf("..", StringComparison.Ordinal);
        if (indexOfDotDot > 0)
        {
            var leftText = text.AsSpan(0, indexOfDotDot).Trim().ToString();
            var rightText = text.AsSpan(indexOfDotDot + 2).Trim().ToString();

            // A '*' bound leaves that side open: 10..* is >=10 and *..10 is <=10. Both open is not a range.
            return (leftText, rightText) switch
            {
                (UnboundedValue, UnboundedValue) => null,
                (UnboundedValue, _) => scalarParser(rightText, out var right) ? new UnaryRangeSyntax<T>(KeyValueOperator.LessThanOrEqual, right) : null,
                (_, UnboundedValue) => scalarParser(leftText, out var left) ? new UnaryRangeSyntax<T>(KeyValueOperator.GreaterThanOrEqual, left) : null,
                _ => scalarParser(leftText, out var left) && scalarParser(rightText, out var right) ? new BinaryRangeSyntax<T>(left, lowerBoundIncluded: true, right, upperBoundIncluded: true) : null,
            };
        }

        return null;
    }

    private const string UnboundedValue = "*";

    private const string TodayAnchor = "@today";

    /// <summary>
    /// Makes <paramref name="scalarParser"/> also accept a date relative to today when <typeparamref name="T"/> is a date:
    /// <c>@today</c>, <c>@today-3d</c>, <c>@today+1w</c>. The value is the start of that day in <paramref name="timeZone"/>.
    /// </summary>
    public static ScalarParser<T> WithRelativeDates<T>(ScalarParser<T> scalarParser, TimeProvider timeProvider, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(scalarParser);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(timeZone);

        if (!IsDateType<T>())
            return scalarParser;

        return TryParse;

        bool TryParse(string value, [MaybeNullWhen(false)] out T result)
        {
            if (TryParseRelativeDate(value, timeProvider, timeZone, out result))
                return true;

            return scalarParser(value, out result);
        }
    }

    private static bool TryParseRelativeDate<T>(string text, TimeProvider timeProvider, TimeZoneInfo timeZone, [MaybeNullWhen(false)] out T value)
    {
        value = default;

        var span = text.AsSpan().Trim();
        if (!span.StartsWith(TodayAnchor, StringComparison.OrdinalIgnoreCase))
            return false;

        var days = 0L;
        var offset = span[TodayAnchor.Length..];
        if (!offset.IsEmpty)
        {
            // A sign, at least one digit, and a unit
            if (offset.Length < 3 || offset[0] is not ('+' or '-') || !int.TryParse(offset[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
                return false;

            var unitInDays = offset[^1] switch
            {
                'd' or 'D' => 1,
                'w' or 'W' => 7,
                _ => 0,
            };

            if (unitInDays is 0)
                return false;

            days = (offset[0] is '-' ? -1L : 1L) * count * unitInDays;
        }

        // Reject an offset past the range of dates rather than overflowing
        var today = Today(timeProvider, timeZone);
        var dayNumber = today.DayNumber + days;
        if (dayNumber < DateOnly.MinValue.DayNumber || dayNumber > DateOnly.MaxValue.DayNumber)
            return false;

        return TryCreateDate(DateOnly.FromDayNumber((int)dayNumber), timeZone, out value);
    }

    public static RangeSyntax<T>? TryParse<T>(string text, ScalarParser<T> tryParse, TimeProvider timeProvider, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(timeZone);

        if (TryExpandRangeVariables<T>(text, timeProvider, timeZone, out var result))
            return result;

        return Parse(text, tryParse);
    }

    private static bool TryExpandRangeVariables<T>(string text, TimeProvider timeProvider, TimeZoneInfo timeZone, [MaybeNullWhen(false)] out RangeSyntax<T> value)
    {
        // Every keyword below expands to a range of dates, so it cannot be represented for any other type.
        // Returning false lets the caller fall back to the type's own parser instead of failing the cast.
        if (!IsDateType<T>())
        {
            value = default;
            return false;
        }

        var span = text.AsSpan().Trim();
        var today = Today(timeProvider, timeZone);

        if (span.Equals("today", StringComparison.OrdinalIgnoreCase))
            return Between(today, days: 0, months: 0, years: 0, length: (0, 0, 1), out value);

        if (span.Equals("yesterday", StringComparison.OrdinalIgnoreCase))
            return Between(today, days: -1, months: 0, years: 0, length: (0, 0, 1), out value);

        if (IsKeyword(span, "this week"))
        {
            var daysSinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            return Between(today, days: -daysSinceMonday, months: 0, years: 0, length: (0, 0, 7), out value);
        }

        var startOfMonth = new DateOnly(today.Year, today.Month, 1);
        if (IsKeyword(span, "this month"))
            return Between(startOfMonth, days: 0, months: 0, years: 0, length: (0, 1, 0), out value);

        if (IsKeyword(span, "last month"))
            return Between(startOfMonth, days: 0, months: -1, years: 0, length: (0, 1, 0), out value);

        var startOfYear = new DateOnly(today.Year, 1, 1);
        if (IsKeyword(span, "this year"))
            return Between(startOfYear, days: 0, months: 0, years: 0, length: (1, 0, 0), out value);

        if (IsKeyword(span, "last year"))
            return Between(startOfYear, days: 0, months: 0, years: -1, length: (1, 0, 0), out value);

        value = default;
        return false;

        // Two-word keywords also accept an underscore, which needs no quotes: created:this_week
        static bool IsKeyword(ReadOnlySpan<char> text, string keyword)
        {
            var separatorIndex = keyword.IndexOf(' ', StringComparison.Ordinal);
            return text.Length == keyword.Length
                && text[separatorIndex] is ' ' or '_'
                && text[..separatorIndex].Equals(keyword.AsSpan(0, separatorIndex), StringComparison.OrdinalIgnoreCase)
                && text[(separatorIndex + 1)..].Equals(keyword.AsSpan(separatorIndex + 1), StringComparison.OrdinalIgnoreCase);
        }

        // The range starts at the beginning of 'anchor' moved by the given amounts, and ends 'length' later, both in the time zone.
        // A range that would start or end outside the supported dates is not a keyword.
        bool Between(DateOnly anchor, int days, int months, int years, (int Years, int Months, int Days) length, [MaybeNullWhen(false)] out RangeSyntax<T> result)
        {
            result = default;
            if (!TryAdd(anchor, years, months, days, out var lowerDate) || !TryAdd(lowerDate, length.Years, length.Months, length.Days, out var upperDate))
                return false;

            if (!TryCreateDate<T>(lowerDate, timeZone, out var lowerBound) || !TryCreateDate<T>(upperDate, timeZone, out var upperBound))
                return false;

            result = new BinaryRangeSyntax<T>(lowerBound, lowerBoundIncluded: true, upperBound, upperBoundIncluded: false);
            return true;
        }

        static bool TryAdd(DateOnly date, int years, int months, int days, out DateOnly result)
        {
            result = default;
            var dayNumber = (long)date.DayNumber + days;
            var monthNumber = ((long)date.Year * 12) + date.Month - 1 + months + (years * 12L);
            if (monthNumber < 12 || monthNumber >= 10000L * 12)
                return false;

            // Months and years only move dates that are the first of a month or a year, so the day always exists
            if (months is not 0 || years is not 0)
            {
                result = new DateOnly((int)(monthNumber / 12), (int)(monthNumber % 12) + 1, date.Day);
                return true;
            }

            if (dayNumber < DateOnly.MinValue.DayNumber || dayNumber > DateOnly.MaxValue.DayNumber)
                return false;

            result = DateOnly.FromDayNumber((int)dayNumber);
            return true;
        }
    }

    /// <summary>Gets whether <typeparamref name="T"/> is a date type, or a nullable one: a boxed date unboxes to its Nullable&lt;T&gt;.</summary>
    private static bool IsDateType<T>()
    {
        var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly);
    }

    private static DateOnly Today(TimeProvider timeProvider, TimeZoneInfo timeZone)
    {
        // Computed from ticks: converting an instant close to the bounds of DateTimeOffset could overflow
        var utcNow = timeProvider.GetUtcNow();
        var localTicks = Math.Clamp(utcNow.UtcTicks + GetUtcOffset(timeZone, utcNow.UtcTicks).Ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks);
        return DateOnly.FromDateTime(new DateTime(localTicks, DateTimeKind.Unspecified));
    }

    /// <summary>Gets the offset of <paramref name="timeZone"/> at an instant, given by its UTC ticks.</summary>
    private static TimeSpan GetUtcOffset(TimeZoneInfo timeZone, long utcTicks)
    {
        try
        {
            return timeZone.GetUtcOffset(new DateTime(utcTicks, DateTimeKind.Utc));
        }
        catch (ArgumentOutOfRangeException)
        {
            // TimeZoneInfo can overflow while applying a daylight saving time rule close to the bounds of DateTime
            return timeZone.BaseUtcOffset;
        }
    }

    /// <summary>
    /// Converts the start of <paramref name="date"/> in <paramref name="timeZone"/> to <typeparamref name="T"/>.
    /// A <see cref="DateOnly"/> is the date itself; a <see cref="DateTimeOffset"/> is the instant the day starts, with the
    /// zone's offset; a <see cref="DateTime"/> is that instant in UTC, matching what <see cref="ValueConverter"/> produces for an explicit date.
    /// </summary>
    private static bool TryCreateDate<T>(DateOnly date, TimeZoneInfo timeZone, [MaybeNullWhen(false)] out T value)
    {
        var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (type == typeof(DateOnly))
        {
            value = (T)(object)date;
            return true;
        }

        if (!TryGetStartOfDay(date, timeZone, out var start))
        {
            value = default;
            return false;
        }

        value = type == typeof(DateTimeOffset) ? (T)(object)start : (T)(object)start.UtcDateTime;
        return true;
    }

    /// <summary>Gets the first instant of <paramref name="date"/> in <paramref name="timeZone"/>, or <see langword="false"/> when it cannot be represented.</summary>
    private static bool TryGetStartOfDay(DateOnly date, TimeZoneInfo timeZone, out DateTimeOffset value)
    {
        value = default;
        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified).Ticks;

        // Offsets are within 14 hours, so the day starts within 14 hours of midnight read as UTC
        var window = TimeSpan.FromHours(14).Ticks;
        var low = Math.Max(DateTimeOffset.MinValue.UtcTicks, midnight - window);
        var high = Math.Min(DateTimeOffset.MaxValue.UtcTicks, midnight + window);
        var lowerLimit = low;

        // Most days start at midnight, with the offset in effect at that time. The offset is checked from the instant: for
        // some rules, TimeZoneInfo does not give the same offset for a local time and for the instant it converts to.
        try
        {
            if (!timeZone.IsInvalidTime(new DateTime(midnight, DateTimeKind.Unspecified)))
            {
                var midnightDateTime = new DateTime(midnight, DateTimeKind.Unspecified);
                var offset = timeZone.IsAmbiguousTime(midnightDateTime)
                    ? timeZone.GetAmbiguousTimeOffsets(midnightDateTime).Max() // Midnight happens twice: the day starts at the first one
                    : timeZone.GetUtcOffset(midnightDateTime);

                // Out of the supported instants, the offset of the local time may still disagree with the offsets of the
                // instants: the search below decides whether the day starts before the first instant that can be represented
                var utcTicks = midnight - offset.Ticks;
                if (utcTicks >= DateTimeOffset.MinValue.UtcTicks && utcTicks <= DateTimeOffset.MaxValue.UtcTicks && GetUtcOffset(timeZone, utcTicks) == offset)
                {
                    value = new DateTimeOffset(midnightDateTime, offset);
                    return true;
                }
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Rules close to the bounds of DateTime can overflow: search the instant instead
        }

        // Otherwise, such as when clocks skip midnight, the day starts at the first instant whose local time is not before
        // midnight. The local time only goes back across a midnight that happens twice, so a binary search finds it.
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (middle + GetUtcOffset(timeZone, middle).Ticks < midnight)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        // The day may start before the first instant that can be represented
        var startOffset = GetUtcOffset(timeZone, low);
        var startTicks = low + startOffset.Ticks;
        if (startTicks < midnight || startTicks > DateTime.MaxValue.Ticks || (low == lowerLimit && startTicks > midnight))
            return false;

        value = new DateTimeOffset(new DateTime(startTicks, DateTimeKind.Unspecified), startOffset);
        return true;
    }
}
