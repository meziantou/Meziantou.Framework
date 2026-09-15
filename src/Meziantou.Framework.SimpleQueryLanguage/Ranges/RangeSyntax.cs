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
    /// <c>@today</c>, <c>@today-3d</c>, <c>@today+1w</c>. The value is the start of that day in UTC.
    /// </summary>
    public static ScalarParser<T> WithRelativeDates<T>(ScalarParser<T> scalarParser, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scalarParser);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (!IsDateType<T>())
            return scalarParser;

        return TryParse;

        bool TryParse(string value, [MaybeNullWhen(false)] out T result)
        {
            if (TryParseRelativeDate(value, timeProvider, out result))
                return true;

            return scalarParser(value, out result);
        }
    }

    private static bool TryParseRelativeDate<T>(string text, TimeProvider timeProvider, [MaybeNullWhen(false)] out T value)
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

        // Reject an offset past the range of dates rather than overflowing. Bounding the day count first keeps the tick arithmetic within a long.
        var start = StartOfDay(timeProvider.GetUtcNow());
        if (Math.Abs(days) > DateTimeOffset.MaxValue.UtcTicks / TimeSpan.TicksPerDay)
            return false;

        var ticks = start.UtcTicks + (days * TimeSpan.TicksPerDay);
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
            return false;

        value = ConvertDate<T>(new DateTimeOffset(ticks, TimeSpan.Zero));
        return true;
    }

    public static RangeSyntax<T>? TryParse<T>(string text, ScalarParser<T> tryParse, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (TryExpandRangeVariables<T>(text, timeProvider, out var result))
            return result;

        return Parse(text, tryParse);
    }

    private static bool TryExpandRangeVariables<T>(string text, TimeProvider timeProvider, [MaybeNullWhen(false)] out RangeSyntax<T> value)
    {
        // Every keyword below expands to a range of dates, so it cannot be represented for any other type.
        // Returning false lets the caller fall back to the type's own parser instead of failing the cast.
        if (!IsDateType<T>())
        {
            value = default;
            return false;
        }

        var span = text.AsSpan().Trim();
        var utcNow = timeProvider.GetUtcNow();

        if (span.Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            var start = StartOfDay(utcNow);
            value = Between(start, start.AddDays(1));
            return true;
        }
        else if (span.Equals("yesterday", StringComparison.OrdinalIgnoreCase))
        {
            var end = StartOfDay(utcNow);
            value = Between(end.AddDays(-1), end);
            return true;
        }
        else if (IsKeyword(span, "this week"))
        {
            var start = StartOfWeek(utcNow);
            value = Between(start, start.AddDays(7));
            return true;
        }
        else if (IsKeyword(span, "this month"))
        {
            var start = StartOfMonth(utcNow);
            value = Between(start, start.AddMonths(1));
            return true;
        }
        else if (IsKeyword(span, "last month"))
        {
            var end = StartOfMonth(utcNow);
            value = Between(end.AddMonths(-1), end);
            return true;
        }
        else if (IsKeyword(span, "this year"))
        {
            var start = StartOfYear(utcNow);
            value = Between(start, start.AddYears(1));
            return true;
        }
        else if (IsKeyword(span, "last year"))
        {
            var end = StartOfYear(utcNow);
            value = Between(end.AddYears(-1), end);
            return true;
        }

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

        static RangeSyntax<T> Between(DateTimeOffset lowerBound, DateTimeOffset upperBound)
        {
            return new BinaryRangeSyntax<T>(ConvertDate<T>(lowerBound), lowerBoundIncluded: true, ConvertDate<T>(upperBound), upperBoundIncluded: false);
        }
    }

    /// <summary>Gets whether <typeparamref name="T"/> is a date type, or a nullable one: a boxed date unboxes to its Nullable&lt;T&gt;.</summary>
    private static bool IsDateType<T>()
    {
        var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly);
    }

    private static T ConvertDate<T>(DateTimeOffset value)
    {
        var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (type == typeof(DateTimeOffset))
            return (T)(object)value;

        if (type == typeof(DateOnly))
            return (T)(object)DateOnly.FromDateTime(value.UtcDateTime);

        // UtcDateTime yields DateTimeKind.Utc, matching what ValueConverter produces for an explicit date
        return (T)(object)value.UtcDateTime;
    }

    private static DateTimeOffset StartOfDay(DateTimeOffset dt)
    {
        var utc = dt.UtcDateTime;
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset StartOfMonth(DateTimeOffset dt)
    {
        var utc = dt.UtcDateTime;
        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset StartOfYear(DateTimeOffset dt)
    {
        var utc = dt.UtcDateTime;
        return new DateTimeOffset(utc.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset StartOfWeek(DateTimeOffset dt)
    {
        var start = StartOfDay(dt);
        var diff = start.DayOfWeek - DayOfWeek.Monday;
        if (diff < 0)
        {
            diff += 7;
        }

        return start.AddDays(-diff);
    }
}
