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
            if (scalarParser(leftText, out var left) && scalarParser(rightText, out var right))
                return new BinaryRangeSyntax<T>(left, lowerBoundIncluded: true, right, upperBoundIncluded: true);
        }

        return null;
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
        // A nullable date is supported too: a boxed date unboxes to its Nullable<T>.
        var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (type != typeof(DateTime) && type != typeof(DateTimeOffset) && type != typeof(DateOnly))
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
            return new BinaryRangeSyntax<T>(ConvertValue(lowerBound), lowerBoundIncluded: true, ConvertValue(upperBound), upperBoundIncluded: false);
        }

        static T ConvertValue(DateTimeOffset value)
        {
            var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (type == typeof(DateTimeOffset))
                return (T)(object)value;

            if (type == typeof(DateOnly))
                return (T)(object)DateOnly.FromDateTime(value.UtcDateTime);

            // UtcDateTime yields DateTimeKind.Utc, matching what ValueConverter produces for an explicit date
            return (T)(object)value.UtcDateTime;
        }
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
