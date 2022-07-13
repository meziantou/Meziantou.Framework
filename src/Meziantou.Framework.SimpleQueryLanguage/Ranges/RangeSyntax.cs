namespace Meziantou.Framework.SimpleQueryLanguage.Ranges;

internal static class RangeSyntax
{
    internal static DateTime UtcNow { get; set; }

    private static DateTime GetUtcNow()
    {
        return UtcNow == default ? DateTime.UtcNow : UtcNow;
    }

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
            var leftText = text.Substring(0, indexOfDotDot).Trim();
            var rightText = text.Substring(indexOfDotDot + 2).Trim();
            if (scalarParser(leftText, out var left) && scalarParser(rightText, out var right))
                return new BinaryRangeSyntax<T>(left, lowerBoundIncluded: true, right, upperBoundIncluded: true);
        }

        return null;
    }

    public static RangeSyntax<T>? TryParse<T>(string text, ScalarParser<T> tryParse)
    {
        if (TryExpandRangeVariables<T>(text, out var result))
            return result;

        return Parse(text, tryParse);
    }

    private static bool TryExpandRangeVariables<T>(string text, [MaybeNullWhen(false)] out RangeSyntax<T> value)
    {
        var span = text.AsSpan().Trim();
        if (span.Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            var now = GetUtcNow();
            var start = new DateTime(now.Year, now.Month, now.Day);
            var end = start.AddDays(1);
            value = new BinaryRangeSyntax<T>(ConvertValue(start), lowerBoundIncluded: true, ConvertValue(end), upperBoundIncluded: false);
            return true;

        }
        else if (span.Equals("yesterday", StringComparison.OrdinalIgnoreCase))
        {
            var now = GetUtcNow();
            var end = new DateTime(now.Year, now.Month, now.Day);
            var start = end.AddDays(-1);
            value = new BinaryRangeSyntax<T>(ConvertValue(start), lowerBoundIncluded: true, ConvertValue(end), upperBoundIncluded: false);
            return true;
        }
        else if (span.Trim().Equals("this week", StringComparison.OrdinalIgnoreCase))
        {
            var now = GetUtcNow();
            var start = StartOfWeek(now);
            var end = start.AddDays(7);
            value = new BinaryRangeSyntax<T>(ConvertValue(start), lowerBoundIncluded: true, ConvertValue(end), upperBoundIncluded: false);
            return true;
        }
        else if (span.Trim().Equals("this month", StringComparison.OrdinalIgnoreCase))
        {
            var now = GetUtcNow();
            var start = new DateTime(now.Year, now.Month, 1);
            var end = start.AddMonths(1);
            value = new BinaryRangeSyntax<T>(ConvertValue(start), lowerBoundIncluded: true, ConvertValue(end), upperBoundIncluded: false);
            return true;
        }
        else if (span.Trim().Equals("last month", StringComparison.OrdinalIgnoreCase))
        {
            var now = GetUtcNow();
            var end = new DateTime(now.Year, now.Month, 1);
            var start = end.AddMonths(-1);
            value = new BinaryRangeSyntax<T>(ConvertValue(start), lowerBoundIncluded: true, ConvertValue(end), upperBoundIncluded: false);
            return true;
        }
        else if (span.Trim().Equals("this year", StringComparison.OrdinalIgnoreCase))
        {
            var now = GetUtcNow();
            var start = new DateTime(now.Year, 1, 1);
            var end = new DateTime(now.Year + 1, 1, 1);
            value = new BinaryRangeSyntax<T>(ConvertValue(start), lowerBoundIncluded: true, ConvertValue(end), upperBoundIncluded: false);
            return true;
        }
        else if (span.Trim().Equals("last year", StringComparison.OrdinalIgnoreCase))
        {
            var now = GetUtcNow();
            var end = new DateTime(now.Year, 1, 1);
            var start = new DateTime(now.Year - 1, 1, 1);
            value = new BinaryRangeSyntax<T>(ConvertValue(start), lowerBoundIncluded: true, ConvertValue(end), upperBoundIncluded: false);
            return true;
        }

        value = default;
        return false;

        static T ConvertValue(object value)
        {
            if (typeof(T) == typeof(DateTimeOffset) && value is DateTime dateTime)
            {
                return (T)(object)new DateTimeOffset(dateTime, TimeSpan.Zero);
            }
            else if (typeof(T) == typeof(DateOnly) && value is DateTime dateTime2)
            {
                return (T)(object)DateOnly.FromDateTime(dateTime2);
            }

            return (T)value;
        }
    }

    private static DateTimeOffset StartOfWeek(DateTimeOffset dt)
    {
        var diff = dt.DayOfWeek - DayOfWeek.Monday;
        if (diff < 0)
        {
            diff += 7;
        }

        dt = dt.AddDays(-1 * diff);
        return new DateTimeOffset(dt.Year, dt.Month, dt.Day, 0, 0, 0, TimeSpan.Zero);
    }
}
