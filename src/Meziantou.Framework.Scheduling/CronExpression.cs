namespace Meziantou.Framework.Scheduling;

public sealed class CronExpression : IRecurrenceRule, IParsable<CronExpression>, ISpanParsable<CronExpression>
{
    public static CronExpression Parse(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return Parse(expression.AsSpan());
    }

    public static bool TryParse([NotNullWhen(true)] string? expression, [NotNullWhen(true)] out CronExpression? cronExpression)
    {
        if (expression is null)
        {
            cronExpression = null;
            return false;
        }

        return TryParse(expression.AsSpan(), out cronExpression);
    }

    public static CronExpression Parse(ReadOnlySpan<char> expression)
    {
        if (TryParse(expression, out var cronExpression))
            return cronExpression;

        throw new FormatException($"The cron expression '{expression}' is not valid.");
    }

    public static bool TryParse(ReadOnlySpan<char> expression, [NotNullWhen(true)] out CronExpression? cronExpression)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<DateTime> GetNextOccurrences(DateTime startDate)
    {
        throw new NotImplementedException();
    }

    static CronExpression IParsable<CronExpression>.Parse(string s, IFormatProvider? provider) => Parse(s);
    static bool IParsable<CronExpression>.TryParse(string? s, IFormatProvider? provider, out CronExpression result) => TryParse(s, out result);
    static CronExpression ISpanParsable<CronExpression>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);
    static bool ISpanParsable<CronExpression>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out CronExpression result) => TryParse(s, out result);
}
