namespace Meziantou.Framework.Scheduling.Tests;

public sealed class CronExpressionTests
{
    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("* *")]
    [InlineData("* * *")]
    [InlineData("* * * *")]
    [InlineData("* * * * * * * *")]
    [InlineData("a * * * * *")]
    [InlineData("*/0 * * * *")]
    [InlineData("0 */0 * * *")]
    [InlineData("0 0 */0 * *")]
    [InlineData("0 0 0 1 1 * 2030-2020")]
    [InlineData("0 0 0W * *")]
    [InlineData("0 0 32W * *")]
    [InlineData("0 0 99W * *")]
    [InlineData("0 0 L-31 * *")]
    [InlineData("0 0 L-40 * *")]
    [InlineData("0 0 1, * *")]
    [InlineData("0 0 ,1 * *")]
    [InlineData("0 0 * * ?,1")]
    [InlineData("0 0 * * 8")]
    [InlineData("0 0 * * 8L")]
    [InlineData("0 0 * * 8#1")]
    [InlineData("0 0 0 1 1 * 1969")]
    [InlineData("0 0 0 1 1 * 10000")]
    [InlineData("0 0 0 1 1 * 2150-2100")]
    [InlineData("0 0 * * *\n")]
    [InlineData("\r\n0 0 * * *")]
    [InlineData("0\n0 * * * *")]
    [InlineData("0 0 * * * ")]
    [InlineData(" 0 0 * * *")]
    [InlineData("0 0 * * * *")]
    [InlineData("0 0 * * * *")]
    [InlineData("@daily\n")]
    [InlineData("\v@daily")]
    public void CronExpression_Parse_InvalidExpression(string expression)
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse(expression));
        Assert.Throws<FormatException>(() => CronExpression.Parse(expression.AsSpan()));

        Assert.False(CronExpression.TryParse(expression, out _));
        Assert.False(CronExpression.TryParse(expression.AsSpan(), out _));
    }

    // Only ASCII letters are folded: characters whose Unicode case mapping is an ASCII letter are not names
    [Theory]
    [InlineData("0 0 * ſep *")]
    [InlineData("0 0 * ſEP *")]
    [InlineData("0 0 * * ſun")]
    [InlineData("0 0 * * ſAT")]
    [InlineData("0 0 * * FRı")]
    [InlineData("0 0 * * frıL")]
    [InlineData("0 0 * * ſun#1")]
    [InlineData("0 0 * * MON-FRİ")]
    [InlineData("0 0 * JΑN *")]
    [InlineData("@daıly")]
    [InlineData("@mıdnight")]
    [InlineData("@weeKly")]
    [InlineData("@ｄaily")]
    public void CronExpression_Parse_NonAsciiLookAlikeLetters_AreRejected(string expression)
    {
        Assert.False(CronExpression.TryParse(expression, out _));
        Assert.Throws<FormatException>(() => CronExpression.Parse(expression));
    }

    [Fact]
    public void CronExpression_Parse_NullExpression()
    {
        Assert.Throws<ArgumentNullException>(() => CronExpression.Parse((string)null!));
        Assert.False(CronExpression.TryParse((string?)null, out _));

        Assert.Throws<FormatException>(() => CronExpression.Parse(ReadOnlySpan<char>.Empty));
        Assert.False(CronExpression.TryParse(ReadOnlySpan<char>.Empty, out _));
    }

    [Theory]
    [InlineData("* * * * *", "2024-01-01T00:00:00", "2024-01-01T00:01:00", "2024-01-01T00:02:00")]
    [InlineData("0 * * * *", "2024-01-01T00:00:00", "2024-01-01T01:00:00", "2024-01-01T02:00:00")]
    [InlineData("0 0 * * *", "2024-01-01T00:00:00", "2024-01-02T00:00:00", "2024-01-03T00:00:00")]
    [InlineData("0 0 1 * *", "2024-01-01T00:00:00", "2024-02-01T00:00:00", "2024-03-01T00:00:00")]
    [InlineData("0 */6 * * *", "2024-01-01T00:00:00", "2024-01-01T06:00:00", "2024-01-01T12:00:00")]
    [InlineData("0 2-1 * * *", "2024-01-01T00:00:00", "2024-01-01T01:00:00", "2024-01-01T02:00:00")]
    public void EvaluateCronExpression_Basic(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    [Theory]
    [InlineData("* * * * * *", "2024-01-01T00:00:00", "2024-01-01T00:00:01", "2024-01-01T00:00:02")]
    [InlineData("0 * * * * *", "2024-01-01T00:00:00", "2024-01-01T00:01:00", "2024-01-01T00:02:00")]
    [InlineData("0 0 * * * *", "2024-01-01T00:00:00", "2024-01-01T01:00:00", "2024-01-01T02:00:00")]
    [InlineData("0 0 0 * * *", "2024-01-01T00:00:00", "2024-01-02T00:00:00", "2024-01-03T00:00:00")]
    [InlineData("0 0 12 * * *", "2024-01-01T12:00:00", "2024-01-02T12:00:00", "2024-01-03T12:00:00")]
    [InlineData("* * * * * *", "2024-01-01T00:00:00", "2024-01-01T00:00:01", "2024-01-01T00:00:02", "2024-01-01T00:00:03")]
    [InlineData("0 * * * * *", "2024-01-01T00:00:00", "2024-01-01T00:01:00", "2024-01-01T00:02:00", "2024-01-01T00:03:00")]
    [InlineData("0 */2 * * * *", "2024-01-01T00:00:00", "2024-01-01T00:02:00", "2024-01-01T00:04:00")]
    [InlineData("0 1/2 * * * *", "2024-01-01T00:01:00", "2024-01-01T00:03:00", "2024-01-01T00:05:00", "2024-01-01T00:07:00")]
    [InlineData("0 */3 * * * *", "2024-01-01T00:00:00", "2024-01-01T00:03:00", "2024-01-01T00:06:00")]
    [InlineData("0 */5 * * * *", "2024-01-01T00:00:00", "2024-01-01T00:05:00", "2024-01-01T00:10:00")]
    [InlineData("0 */10 * * * *", "2024-01-01T00:00:00", "2024-01-01T00:10:00", "2024-01-01T00:20:00", "2024-01-01T00:30:00")]
    [InlineData("0 */15 * * * *", "2024-01-01T00:00:00", "2024-01-01T00:15:00", "2024-01-01T00:30:00")]
    [InlineData("0 */30 * * * *", "2024-01-01T00:00:00", "2024-01-01T00:30:00", "2024-01-01T01:00:00")]
    [InlineData("0 15,30,45 * * * *", "2024-01-01T00:15:00", "2024-01-01T00:30:00", "2024-01-01T00:45:00")]
    [InlineData("0 0 */2 * * *", "2024-01-01T00:00:00", "2024-01-01T02:00:00", "2024-01-01T04:00:00")]
    [InlineData("0 0 0/2 * * *", "2024-01-01T00:00:00", "2024-01-01T02:00:00", "2024-01-01T04:00:00")]
    [InlineData("0 0 1/2 * * *", "2024-01-01T01:00:00", "2024-01-01T03:00:00", "2024-01-01T05:00:00")]
    [InlineData("0 0 */3 * * *", "2024-01-01T00:00:00", "2024-01-01T03:00:00", "2024-01-01T06:00:00")]
    [InlineData("0 0 */4 * * *", "2024-01-01T00:00:00", "2024-01-01T04:00:00", "2024-01-01T08:00:00")]
    [InlineData("0 0 */6 * * *", "2024-01-01T00:00:00", "2024-01-01T06:00:00", "2024-01-01T12:00:00")]
    [InlineData("0 0 */8 * * *", "2024-01-01T00:00:00", "2024-01-01T08:00:00", "2024-01-01T16:00:00")]
    [InlineData("0 0 */12 * * *", "2024-01-01T00:00:00", "2024-01-01T12:00:00", "2024-01-02T00:00:00")]
    [InlineData("0 0 1 * * *", "2024-01-01T01:00:00", "2024-01-02T01:00:00", "2024-01-03T01:00:00", "2024-01-04T01:00:00")]
    [InlineData("0 0 6 * * *", "2024-01-01T06:00:00", "2024-01-02T06:00:00", "2024-01-03T06:00:00")]
    [InlineData("0 0 12 * * SUN", "2024-01-07T12:00:00", "2024-01-14T12:00:00", "2024-01-21T12:00:00")]
    [InlineData("0 0 12 * * MON", "2024-01-01T12:00:00", "2024-01-08T12:00:00", "2024-01-15T12:00:00")]
    [InlineData("0 0 12 * * TUE", "2024-01-02T12:00:00", "2024-01-09T12:00:00", "2024-01-16T12:00:00")]
    [InlineData("0 0 12 * * WED", "2024-01-03T12:00:00", "2024-01-10T12:00:00", "2024-01-17T12:00:00")]
    [InlineData("0 0 12 * * THU", "2024-01-04T12:00:00", "2024-01-11T12:00:00", "2024-01-18T12:00:00")]
    [InlineData("0 0 12 * * FRI", "2024-01-05T12:00:00", "2024-01-12T12:00:00", "2024-01-19T12:00:00")]
    [InlineData("0 0 12 * * SAT", "2024-01-06T12:00:00", "2024-01-13T12:00:00", "2024-01-20T12:00:00")]
    [InlineData("0 0 12 * * MON-FRI", "2024-01-01T12:00:00", "2024-01-02T12:00:00", "2024-01-03T12:00:00")]
    [InlineData("0 0 12 * * SUN,SAT", "2024-01-06T12:00:00", "2024-01-07T12:00:00", "2024-01-13T12:00:00")]
    [InlineData("0 0 12 */7 * *", "2024-01-01T12:00:00", "2024-01-08T12:00:00", "2024-01-15T12:00:00")]
    [InlineData("0 0 12 1 * *", "2024-01-01T12:00:00", "2024-02-01T12:00:00", "2024-03-01T12:00:00")]
    [InlineData("0 0 12 2 * *", "2024-01-02T12:00:00", "2024-02-02T12:00:00", "2024-03-02T12:00:00")]
    [InlineData("0 0 12 15 * *", "2024-01-15T12:00:00", "2024-02-15T12:00:00", "2024-03-15T12:00:00")]
    [InlineData("0 0 12 1/2 * *", "2024-01-01T12:00:00", "2024-01-03T12:00:00", "2024-01-05T12:00:00")]
    [InlineData("0 0 12 1/4 * *", "2024-01-01T12:00:00", "2024-01-05T12:00:00", "2024-01-09T12:00:00")]
    [InlineData("0 0 12 L * *", "2024-01-31T12:00:00", "2024-02-29T12:00:00", "2024-03-31T12:00:00")]
    [InlineData("0 0 12 L-2 * *", "2024-01-29T12:00:00", "2024-02-27T12:00:00", "2024-03-29T12:00:00")]
    [InlineData("0 0 12 LW * *", "2024-01-31T12:00:00", "2024-02-29T12:00:00", "2024-03-29T12:00:00")]
    [InlineData("0 0 12 * * 1L", "2024-01-29T12:00:00", "2024-02-26T12:00:00", "2024-03-25T12:00:00")]
    [InlineData("0 0 12 * * 2L", "2024-01-30T12:00:00", "2024-02-27T12:00:00", "2024-03-26T12:00:00")]
    [InlineData("0 0 12 * * 6L", "2024-01-27T12:00:00", "2024-02-24T12:00:00", "2024-03-30T12:00:00")]
    [InlineData("0 0 12 1W * *", "2024-01-01T12:00:00", "2024-02-01T12:00:00", "2024-03-01T12:00:00")]
    [InlineData("0 0 12 15W * *", "2024-01-15T12:00:00", "2024-02-15T12:00:00", "2024-03-15T12:00:00")]
    [InlineData("0 0 12 * * 2#1", "2024-01-02T12:00:00", "2024-02-06T12:00:00", "2024-03-05T12:00:00")]
    [InlineData("0 0 12 * * 6#1", "2024-01-06T12:00:00", "2024-02-03T12:00:00", "2024-03-02T12:00:00")]
    [InlineData("0 0 12 * * 2#2", "2024-01-09T12:00:00", "2024-02-13T12:00:00", "2024-03-12T12:00:00")]
    [InlineData("0 0 12 * * 5#3", "2024-01-19T12:00:00", "2024-02-16T12:00:00", "2024-03-15T12:00:00")]
    [InlineData("0 0 12 * JAN *", "2024-01-01T12:00:00", "2024-01-02T12:00:00", "2024-01-03T12:00:00")]
    [InlineData("0 0 12 * JUN *", "2024-06-01T12:00:00", "2024-06-02T12:00:00", "2024-06-03T12:00:00")]
    [InlineData("0 0 12 * JAN,JUN *", "2024-01-01T12:00:00", "2024-01-02T12:00:00", "2024-01-03T12:00:00")]
    [InlineData("0 0 12 * DEC *", "2024-12-01T12:00:00", "2024-12-02T12:00:00", "2024-12-03T12:00:00")]
    [InlineData("0 0 12 * JAN,FEB,MAR,APR *", "2024-01-01T12:00:00", "2024-01-02T12:00:00", "2024-01-03T12:00:00")]
    [InlineData("0 0 12 * 9-12 *", "2024-09-01T12:00:00", "2024-09-02T12:00:00", "2024-09-03T12:00:00")]
    public void EvaluateCronExpression_WithSeconds(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    [Theory]
    [InlineData("0 0 12 * * MON", "2024-01-01T12:00:00", "2024-01-08T12:00:00", "2024-01-15T12:00:00")]
    [InlineData("0 0 12 * * TUE", "2024-01-02T12:00:00", "2024-01-09T12:00:00", "2024-01-16T12:00:00")]
    [InlineData("0 0 12 * * SUN", "2024-01-07T12:00:00", "2024-01-14T12:00:00", "2024-01-21T12:00:00")]
    [InlineData("0 0 12 * * SAT", "2024-01-06T12:00:00", "2024-01-13T12:00:00", "2024-01-20T12:00:00")]
    [InlineData("0 0 12 * * MON-FRI", "2024-01-01T12:00:00", "2024-01-02T12:00:00", "2024-01-03T12:00:00")]
    [InlineData("0 0 12 * * 7", "2024-01-07T12:00:00", "2024-01-14T12:00:00", "2024-01-21T12:00:00")]
    [InlineData("0 0 12 * * 5-7", "2024-01-05T12:00:00", "2024-01-06T12:00:00", "2024-01-07T12:00:00", "2024-01-12T12:00:00")]
    [InlineData("0 0 12 * * 7-1", "2024-01-01T12:00:00", "2024-01-07T12:00:00", "2024-01-08T12:00:00")]
    [InlineData("0 0 12 * * 0-7/2", "2024-01-02T12:00:00", "2024-01-04T12:00:00", "2024-01-06T12:00:00", "2024-01-07T12:00:00")]
    [InlineData("0 0 12 * * 7L", "2024-01-28T12:00:00", "2024-02-25T12:00:00", "2024-03-31T12:00:00")]
    [InlineData("0 0 12 * * 7#2", "2024-01-14T12:00:00", "2024-02-11T12:00:00", "2024-03-10T12:00:00")]
    [InlineData("0 0 12 * * 1/2", "2024-01-01T12:00:00", "2024-01-03T12:00:00", "2024-01-05T12:00:00", "2024-01-07T12:00:00", "2024-01-08T12:00:00")]
    [InlineData("0 0 12 * * MON/2", "2024-01-01T12:00:00", "2024-01-03T12:00:00", "2024-01-05T12:00:00", "2024-01-07T12:00:00", "2024-01-08T12:00:00")]
    [InlineData("0 0 12 * * 1-7/2", "2024-01-01T12:00:00", "2024-01-03T12:00:00", "2024-01-05T12:00:00", "2024-01-07T12:00:00", "2024-01-08T12:00:00")]
    [InlineData("0 0 12 * * 6/2", "2024-01-06T12:00:00", "2024-01-13T12:00:00", "2024-01-20T12:00:00")]
    [InlineData("0 0 12 * * */2", "2024-01-02T12:00:00", "2024-01-04T12:00:00", "2024-01-06T12:00:00", "2024-01-07T12:00:00", "2024-01-09T12:00:00")]
    [InlineData("0 0 12 * * 0/2", "2024-01-02T12:00:00", "2024-01-04T12:00:00", "2024-01-06T12:00:00", "2024-01-07T12:00:00", "2024-01-09T12:00:00")]
    public void EvaluateCronExpression_DayOfWeek(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    [Theory]
    [InlineData("0 0 12 * JAN *", "2024-01-01T12:00:00", "2024-01-02T12:00:00", "2024-01-03T12:00:00")]
    [InlineData("0 0 12 * DEC *", "2024-12-01T12:00:00", "2024-12-02T12:00:00", "2024-12-03T12:00:00")]
    [InlineData("0 0 12 * 9-12 *", "2024-09-01T12:00:00", "2024-09-02T12:00:00", "2024-09-03T12:00:00")]
    public void EvaluateCronExpression_Month(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    [Theory]
    [InlineData("0 0 12 1 1 * 2025", "2025-01-01T12:00:00")]
    [InlineData("0 0 12 * * * 2024", "2024-01-01T12:00:00", "2024-01-02T12:00:00", "2024-01-03T12:00:00")]
    [InlineData("0 0 12 * * * 2025,2026", "2025-01-01T12:00:00", "2025-01-02T12:00:00", "2025-01-03T12:00:00")]
    [InlineData("0 0 12 1 1 * 2098/50", "2098-01-01T12:00:00", "2148-01-01T12:00:00", "2198-01-01T12:00:00")]
    [InlineData("0 0 12 1 1 * 2100-2500/100", "2100-01-01T12:00:00", "2200-01-01T12:00:00", "2300-01-01T12:00:00", "2400-01-01T12:00:00", "2500-01-01T12:00:00")]
    public void EvaluateCronExpression_WithYear(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    [Theory]
    [InlineData("@yearly", "2024-01-01T00:00:00", "2025-01-01T00:00:00", "2026-01-01T00:00:00")]
    [InlineData("@annually", "2024-01-01T00:00:00", "2025-01-01T00:00:00", "2026-01-01T00:00:00")]
    [InlineData("@monthly", "2024-01-01T00:00:00", "2024-02-01T00:00:00", "2024-03-01T00:00:00")]
    [InlineData("@weekly", "2024-01-07T00:00:00", "2024-01-14T00:00:00", "2024-01-21T00:00:00")]
    [InlineData("@daily", "2024-01-01T00:00:00", "2024-01-02T00:00:00", "2024-01-03T00:00:00")]
    [InlineData("@midnight", "2024-01-01T00:00:00", "2024-01-02T00:00:00", "2024-01-03T00:00:00")]
    [InlineData("@hourly", "2024-01-01T00:00:00", "2024-01-01T01:00:00", "2024-01-01T02:00:00")]
    public void EvaluateCronExpression_PredefinedSchedules(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    [Theory]
    [InlineData("0 0 31 2 *")]
    [InlineData("0 0 30 2 *")]
    public void EvaluateCronExpression_InvalidDayOfMonth_NoMatches(string expression)
    {
        var cron = CronExpression.Parse(expression);
        var occurrences = cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)).Take(2).ToList();
        Assert.Empty(occurrences);
    }

    [Theory]
    [InlineData("0 9 15W * *", "2024-01-15T09:00:00", "2024-02-15T09:00:00", "2024-03-15T09:00:00")]
    [InlineData("0 9 1W * *", "2024-01-01T09:00:00", "2024-02-01T09:00:00", "2024-03-01T09:00:00")]
    [InlineData("0 9 31W * *", "2024-01-31T09:00:00", "2024-03-29T09:00:00", "2024-05-31T09:00:00")]
    public void EvaluateCronExpression_NearestWeekday(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    [Theory]
    [InlineData("0 9 * * 1#5", "2024-01-29T09:00:00", "2024-04-29T09:00:00", "2024-07-29T09:00:00")]
    [InlineData("0 9 * * 6#2", "2024-01-13T09:00:00", "2024-02-10T09:00:00", "2024-03-09T09:00:00")]
    public void EvaluateCronExpression_NthWeekdayOfMonth(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    [Theory]
    [InlineData("0,30 * * * *", "2024-01-01T00:00:00", "2024-01-01T00:30:00", "2024-01-01T01:00:00")]
    [InlineData("0 0,12 * * *", "2024-01-01T00:00:00", "2024-01-01T12:00:00", "2024-01-02T00:00:00")]
    public void EvaluateCronExpression_WithLists(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    [Theory]
    [InlineData("15-45 * * * *", "2024-01-01T00:15:00", "2024-01-01T00:16:00", "2024-01-01T00:17:00")]
    [InlineData("0 9-17 * * *", "2024-01-01T09:00:00", "2024-01-01T10:00:00", "2024-01-01T11:00:00")]
    public void EvaluateCronExpression_WithRanges(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    [Theory]
    [InlineData("0 22-2 * * *", "2024-01-01T00:00:00", "2024-01-01T01:00:00", "2024-01-01T02:00:00", "2024-01-01T22:00:00", "2024-01-01T23:00:00", "2024-01-02T00:00:00")]
    [InlineData("0 22-2/2 * * *", "2024-01-01T00:00:00", "2024-01-01T02:00:00", "2024-01-01T22:00:00", "2024-01-02T00:00:00")]
    [InlineData("58-1 0 0 * * *", "2024-01-01T00:00:00", "2024-01-01T00:00:01", "2024-01-01T00:00:58", "2024-01-01T00:00:59", "2024-01-02T00:00:00")]
    [InlineData("58-1 * * * *", "2024-01-01T00:00:00", "2024-01-01T00:01:00", "2024-01-01T00:58:00", "2024-01-01T00:59:00", "2024-01-01T01:00:00")]
    [InlineData("0 0 30-2 * *", "2024-01-01T00:00:00", "2024-01-02T00:00:00", "2024-01-30T00:00:00", "2024-01-31T00:00:00", "2024-02-01T00:00:00", "2024-02-02T00:00:00", "2024-03-01T00:00:00")]
    [InlineData("0 0 1 NOV-FEB *", "2024-01-01T00:00:00", "2024-02-01T00:00:00", "2024-11-01T00:00:00", "2024-12-01T00:00:00", "2025-01-01T00:00:00")]
    [InlineData("0 0 * * FRI-MON", "2024-01-01T00:00:00", "2024-01-05T00:00:00", "2024-01-06T00:00:00", "2024-01-07T00:00:00", "2024-01-08T00:00:00", "2024-01-12T00:00:00")]
    [InlineData("0 0 * * 6-0", "2024-01-06T00:00:00", "2024-01-07T00:00:00", "2024-01-13T00:00:00")]
    public void EvaluateCronExpression_WithWrapAroundRanges(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    [Theory]
    [InlineData("*/15 * * * *", "2024-01-01T00:00:00", "2024-01-01T00:15:00", "2024-01-01T00:30:00")]
    [InlineData("0 */3 * * *", "2024-01-01T00:00:00", "2024-01-01T03:00:00", "2024-01-01T06:00:00")]
    [InlineData("*/15,7 * * * *", "2024-01-01T00:00:00", "2024-01-01T00:07:00", "2024-01-01T00:15:00", "2024-01-01T00:30:00")]
    [InlineData("7,*/15 * * * *", "2024-01-01T00:00:00", "2024-01-01T00:07:00", "2024-01-01T00:15:00", "2024-01-01T00:30:00")]
    [InlineData("*,5 * * * *", "2024-01-01T00:00:00", "2024-01-01T00:01:00", "2024-01-01T00:02:00")]
    [InlineData("0 0 * * 1,*", "2024-01-01T00:00:00", "2024-01-02T00:00:00", "2024-01-03T00:00:00")]
    public void EvaluateCronExpression_WithStepValues(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    [Theory]
    [InlineData("0 0 l * *", "2024-01-31T00:00:00", "2024-02-29T00:00:00", "2024-03-31T00:00:00")]
    [InlineData("0 0 l-2 * *", "2024-01-29T00:00:00", "2024-02-27T00:00:00", "2024-03-29T00:00:00")]
    [InlineData("0 0 lw * *", "2024-01-31T00:00:00", "2024-02-29T00:00:00", "2024-03-29T00:00:00")]
    [InlineData("0 0 lW * *", "2024-01-31T00:00:00", "2024-02-29T00:00:00", "2024-03-29T00:00:00")]
    [InlineData("0 0 15w * *", "2024-01-15T00:00:00", "2024-02-15T00:00:00", "2024-03-15T00:00:00")]
    [InlineData("0 0 * * 5l", "2024-01-26T00:00:00", "2024-02-23T00:00:00", "2024-03-29T00:00:00")]
    [InlineData("0 0 * * fril", "2024-01-26T00:00:00", "2024-02-23T00:00:00", "2024-03-29T00:00:00")]
    [InlineData("0 0 * * tue#1", "2024-01-02T00:00:00", "2024-02-06T00:00:00", "2024-03-05T00:00:00")]
    [InlineData("0 0 1 jan,jun *", "2024-01-01T00:00:00", "2024-06-01T00:00:00", "2025-01-01T00:00:00")]
    public void EvaluateCronExpression_SpecialValuesAreCaseInsensitive(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    [Theory]
    [InlineData("0 0 L-0 * *", "2024-01-31T00:00:00", "2024-02-29T00:00:00")]
    [InlineData("0 0 L-30 * *", "2024-01-01T00:00:00", "2024-03-01T00:00:00", "2024-05-01T00:00:00")]
    [InlineData("0 0 31W * *", "2024-01-31T00:00:00", "2024-03-29T00:00:00")]
    public void EvaluateCronExpression_DayOfMonthSpecialValueBounds(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    [Theory]
    [InlineData("0\t0\t*\t*\t*")]
    [InlineData("0 \t 0\t\t* * *")]
    [InlineData("\t0 0 * * *\t")]
    [InlineData(" \t@daily \t")]
    public void EvaluateCronExpression_TabSeparatedFields(string expression)
    {
        var cron = CronExpression.Parse(expression);
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), new DateTime(2024, 1, 1), new DateTime(2024, 1, 2));
    }

    [Theory]
    [InlineData("0 */15 * * * *", "0 */15 * * * *")]
    [InlineData(" \t0  0\t* * *\t ", "0  0\t* * *")]
    [InlineData("@Daily", "@Daily")]
    [InlineData(" @hourly ", "@hourly")]
    [InlineData("0 0 * jan mon-fri", "0 0 * jan mon-fri")]
    public void CronExpression_ToString_ReturnsTheParsedText(string expression, string expected)
    {
        var cron = CronExpression.Parse(expression);

        Assert.Equal(expected, cron.ToString());
        Assert.Equal(cron, CronExpression.Parse(cron.ToString()));
    }

    [Theory]
    [InlineData("0 0 * * *", "0 0 * * *")]
    [InlineData("*/15 * * * *", "0,15,30,45 * * * *")]
    [InlineData("*/15 * * * *", "0-59/15 * * * *")]
    [InlineData("@daily", "0 0 * * *")]
    [InlineData("@midnight", "@DAILY")]
    [InlineData("@yearly", "0 0 1 JAN ?")]
    [InlineData("0 0 * * *", "0 0 0 * * *")]
    [InlineData("0 0 * * *", "0 0 0 * * * *")]
    [InlineData("0 0 * * *", "0 0 0 ? * ? ?")]
    [InlineData("0-59 0 * * *", "* 0 * * *")]
    [InlineData("0 0 1-31 1-12 0-6", "0 0 * * *")]
    [InlineData("0 0 * * 1-7", "0 0 * * *")]
    [InlineData("0 0 * * 7", "0 0 * * SUN")]
    [InlineData("0 0 * * 0L", "0 0 * * 7L")]
    [InlineData("0 0 * * MON", "0 0 * * 1#1,1#2,1#3,1#4,1#5")]
    [InlineData("0 0 * * MON", "0 0 * * 1,1L,1#3")]
    [InlineData("0 0 L * *", "0 0 L-0,l * *")]
    [InlineData("0 0 15W,LW * *", "0 0 LW,15w,15W * *")]
    [InlineData("0 0 0 1 1 * 2024", "0 0 0 1 1 * 2024,2024-2024")]
    [InlineData("0 0 0 1 1 * 2098-2102", "0 0 0 1 1 * 2098,2099,2100,2101,2102")]
    public void CronExpression_Equals_SameFields(string left, string right)
    {
        var leftExpression = CronExpression.Parse(left);
        var rightExpression = CronExpression.Parse(right);

        Assert.Equal(leftExpression, rightExpression);
        Assert.True(rightExpression.Equals((object)leftExpression));
        Assert.True(leftExpression == rightExpression);
        Assert.False(leftExpression != rightExpression);
        Assert.Equal(leftExpression.GetHashCode(), rightExpression.GetHashCode());
    }

    [Theory]
    [InlineData("0 0 * * *", "0 0 * * 1")]
    [InlineData("0 0 * * *", "0 0 * * * *")]
    [InlineData("0 0 * * *", "1 0 0 * * *")]
    [InlineData("0 0 1 * *", "0 0 1W * *")]
    [InlineData("0 0 29 2 *", "0 0 L 2 *")]
    [InlineData("0 0 L * *", "0 0 LW * *")]
    [InlineData("0 0 L-1 * *", "0 0 L-2 * *")]
    [InlineData("0 0 * * 1L", "0 0 * * 1#5")]
    [InlineData("0 0 * * 1#1", "0 0 * * 2#1")]
    [InlineData("0 0 * * 1#1,1#2,1#3,1#4", "0 0 * * 1")]
    [InlineData("0 0 0 1 1 * *", "0 0 0 1 1 * 1970-9999")]
    [InlineData("0 0 0 1 1 * 2024", "0 0 0 1 1 * 2150")]
    [InlineData("0 0 0 1 1 * 2024", "0 0 0 1 1 * 2024,9999")]
    public void CronExpression_Equals_DifferentFields(string left, string right)
    {
        var leftExpression = CronExpression.Parse(left);
        var rightExpression = CronExpression.Parse(right);

        Assert.NotEqual(leftExpression, rightExpression);
        Assert.False(rightExpression.Equals((object)leftExpression));
        Assert.False(leftExpression == rightExpression);
        Assert.True(leftExpression != rightExpression);
    }

    [Fact]
    public void CronExpression_Equals_Null()
    {
        var cron = CronExpression.Parse("0 0 * * *");

        Assert.NotEqual(null, cron);
        Assert.False(cron.Equals((object?)null));
        Assert.False(cron.Equals("0 0 * * *"));
        Assert.False(cron == null);
        Assert.True(cron != null);
        Assert.True((CronExpression?)null == null);
    }

    [Fact]
    public void CronExpression_RepeatedSpecialValues_AreStoredOnce()
    {
        const int Count = 10_000;
        var expression = "0 0 " + string.Join(',', Enumerable.Repeat("L-30", Count)) + " * " + string.Join(',', Enumerable.Repeat("1#5", Count));

        var cron = CronExpression.Parse(expression);

        // The fifth Monday is never the first day of a month, so the enumeration scans every month until year 9999
        Assert.Equal(CronExpression.Parse("0 0 L-30 * 1#5"), cron);
        Assert.Empty(cron.GetNextOccurrences(new DateTime(2024, 1, 1)));
    }

    [Theory]
    [InlineData("* * * * *", "2025-01-15T09:00:00.5000000", "2025-01-15T09:01:00", "2025-01-15T09:02:00")]
    [InlineData("0 0 * * *", "2025-01-15T00:00:00.5000000", "2025-01-16T00:00:00", "2025-01-17T00:00:00")]
    [InlineData("* * * * * *", "2025-01-15T09:00:00.0000001", "2025-01-15T09:00:01", "2025-01-15T09:00:02")]
    [InlineData("* * * * * *", "2025-01-15T09:00:59.9999999", "2025-01-15T09:01:00", "2025-01-15T09:01:01")]
    public void GetNextOccurrences_StartWithFractionalSecond_StartsAtTheNextWholeSecond(string expression, string startDate, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var start = DateTime.Parse(startDate, CultureInfo.InvariantCulture);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(start), expectedDates);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    public void GetNextOccurrences_PreservesTheKindOfTheStartDate(DateTimeKind kind)
    {
        var cron = CronExpression.Parse("*/20 * * * * *");

        var occurrences = cron.GetNextOccurrences(new DateTime(2025, 1, 15, 9, 0, 0, 500, kind)).Take(4).ToArray();

        AssertOccurrencesStartWith(occurrences, new DateTime(2025, 1, 15, 9, 0, 20), new DateTime(2025, 1, 15, 9, 0, 40), new DateTime(2025, 1, 15, 9, 1, 0), new DateTime(2025, 1, 15, 9, 1, 20));
        Assert.All(occurrences, occurrence => occurrence.Kind == kind);
    }

    [Fact]
    public void GetNextOccurrences_DoesNotStopAfterAFixedNumberOfOccurrences()
    {
        var cron = CronExpression.Parse("* * * * * *");

        var occurrence = cron.GetNextOccurrences(new DateTime(2024, 1, 1)).Skip(200_000).First();

        Assert.Equal(new DateTime(2024, 1, 3, 7, 33, 20), occurrence);
    }

    [Fact]
    public void GetNextOccurrences_EndsAtDateTimeMaxValue()
    {
        var everySecond = CronExpression.Parse("* * * * * *");
        var lastMinuteOfYear = CronExpression.Parse("59 23 31 12 *");

        var occurrences = everySecond.GetNextOccurrences(new DateTime(9999, 12, 31, 23, 59, 58)).ToArray();

        Assert.HasCount(2, occurrences);
        AssertOccurrencesStartWith(occurrences, new DateTime(9999, 12, 31, 23, 59, 58), new DateTime(9999, 12, 31, 23, 59, 59));
        Assert.Empty(everySecond.GetNextOccurrences(DateTime.MaxValue));
        Assert.Empty(everySecond.GetNextOccurrences(new DateTime(9999, 12, 31, 23, 59, 59, 1)));

        var lastOccurrences = lastMinuteOfYear.GetNextOccurrences(new DateTime(9999, 6, 1)).ToArray();
        Assert.HasCount(1, lastOccurrences);
        Assert.Equal(new DateTime(9999, 12, 31, 23, 59, 0), lastOccurrences[0]);
    }

    [Theory]
    [InlineData("0 0 30 2 *")]
    [InlineData("0 0 31 4 *")]
    [InlineData("0 0 31 2,4,6,9,11 *")]
    [InlineData("0 0 1 * * * 2020")]
    [InlineData("0 0 0 29 2 * 2025-2027")]
    public void GetNextOccurrences_NeverMatchingExpression_Ends(string expression)
    {
        var cron = CronExpression.Parse(expression);

        Assert.Empty(cron.GetNextOccurrences(new DateTime(2025, 1, 1)));
    }

    [Fact]
    public void GetNextOccurrences_YearField_EndsAfterItsLastYear()
    {
        var cron = CronExpression.Parse("0 0 0 1 1 * 2044-2096/6,2098-2099");

        var occurrences = cron.GetNextOccurrences(new DateTime(2090, 1, 1)).ToArray();

        Assert.HasCount(3, occurrences);
        AssertOccurrencesStartWith(occurrences, new DateTime(2092, 1, 1), new DateTime(2098, 1, 1), new DateTime(2099, 1, 1));
    }

    [Fact]
    public void GetNextOccurrences_YearField_AcceptsYearsUntil9999()
    {
        AssertOccurrencesStartWith(CronExpression.Parse("0 0 12 1 1 * 2150").GetNextOccurrences(new DateTime(2024, 1, 1)), new DateTime(2150, 1, 1, 12, 0, 0));
        AssertOccurrencesStartWith(CronExpression.Parse("0 0 0 1 1 * 2044/6").GetNextOccurrences(new DateTime(2097, 1, 1)), new DateTime(2098, 1, 1), new DateTime(2104, 1, 1), new DateTime(2110, 1, 1));
        AssertOccurrencesStartWith(CronExpression.Parse("0 0 0 1 1 * 2099-2101,5000").GetNextOccurrences(new DateTime(2024, 1, 1)), new DateTime(2099, 1, 1), new DateTime(2100, 1, 1), new DateTime(2101, 1, 1), new DateTime(5000, 1, 1));

        var everyThousandYears = CronExpression.Parse("0 0 0 1 1 * */2000").GetNextOccurrences(new DateTime(1900, 1, 1)).ToArray();
        Assert.HasCount(5, everyThousandYears);
        AssertOccurrencesStartWith(everyThousandYears, new DateTime(1970, 1, 1), new DateTime(3970, 1, 1), new DateTime(5970, 1, 1), new DateTime(7970, 1, 1), new DateTime(9970, 1, 1));

        var lastYears = CronExpression.Parse("59 59 23 31 12 * 9998-9999").GetNextOccurrences(new DateTime(2024, 1, 1)).ToArray();
        Assert.HasCount(2, lastYears);
        AssertOccurrencesStartWith(lastYears, new DateTime(9998, 12, 31, 23, 59, 59), new DateTime(9999, 12, 31, 23, 59, 59));
    }

    [Fact]
    public void GetNextOccurrences_WithoutYearField_ContinuesAfter2099()
    {
        var cron = CronExpression.Parse("0 0 1 1 *");

        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2099, 1, 1)), new DateTime(2099, 1, 1), new DateTime(2100, 1, 1), new DateTime(2101, 1, 1));
    }

    [Theory]
    [InlineData("0 0 L * *", "2024-01-31T00:00:00", "2024-02-29T00:00:00", "2024-03-31T00:00:00")]
    [InlineData("0 0 L-5 * *", "2024-01-26T00:00:00", "2024-02-24T00:00:00", "2024-03-26T00:00:00")]
    [InlineData("0 0 * * 0L", "2024-01-28T00:00:00", "2024-02-25T00:00:00", "2024-03-31T00:00:00")]
    [InlineData("0 0 * * 5L", "2024-01-26T00:00:00", "2024-02-23T00:00:00", "2024-03-29T00:00:00")]
    public void EvaluateCronExpression_LastDay(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    [Theory]
    [InlineData("0 0 LW * *", "2024-01-31T00:00:00", "2024-02-29T00:00:00", "2024-03-29T00:00:00")]
    [InlineData("0 0 1W * *", "2024-01-01T00:00:00", "2024-02-01T00:00:00", "2024-03-01T00:00:00")]
    [InlineData("0 0 15W * *", "2024-01-15T00:00:00", "2024-02-15T00:00:00", "2024-03-15T00:00:00")]
    public void EvaluateCronExpression_Weekday(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    [Theory]
    [InlineData("0 0 0 15 2 * 2024", "2024-02-15T00:00:00")]
    [InlineData("0 0 0 29 2 * 2024", "2024-02-29T00:00:00")]
    [InlineData("0 0 0 29 2 * 2025")]
    public void EvaluateCronExpression_LeapYear(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        if (expectedDates.Length > 0)
        {
            AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
        }
        else
        {
            var occurrences = cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)).Take(1).ToList();
            Assert.Empty(occurrences);
        }
    }

    [Fact]
    public void GetNextOccurrences_NullTimeZone_ThrowsBeforeEnumeration()
    {
        var cron = CronExpression.Parse("0 9 * * *");

        Assert.Throws<ArgumentNullException>(() => cron.GetNextOccurrences(new DateTime(2024, 01, 01), timeZone: null!));
    }

    // Synthetic time zones work when globalization is invariant, and keep the tests independent of the time zone database.
    private static TimeZoneInfo CreateTestTimeZone(TimeSpan baseUtcOffset, TimeSpan daylightDelta)
    {
        // Daylight saving time starts on the last Sunday of March at 02:00 and ends on the last Sunday of October at 03:00 daylight time
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), month: 3, week: 5, DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), month: 10, week: 5, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, daylightDelta, daylightStart, daylightEnd);
        return TimeZoneInfo.CreateCustomTimeZone("Test/Zone", baseUtcOffset, "Test", "Test Standard", "Test Daylight", [rule]);
    }

    private static TimeZoneInfo TestParis => CreateTestTimeZone(TimeSpan.FromHours(1), TimeSpan.FromHours(1));

    private static TimeZoneInfo TestNewYork
    {
        get
        {
            var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), month: 3, week: 2, DayOfWeek.Sunday);
            var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), month: 11, week: 1, DayOfWeek.Sunday);
            var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2007, 1, 1), DateTime.MaxValue.Date, TimeSpan.FromHours(1), daylightStart, daylightEnd);
            return TimeZoneInfo.CreateCustomTimeZone("Test/New_York", TimeSpan.FromHours(-5), "Test Eastern", "EST", "EDT", [rule]);
        }
    }

    [Fact]
    public void GetNextOccurrences_TimeZone_AcrossAHalfHourGap_KeepsTheOccurrencesAfterAMovedOne()
    {
        var cron = CronExpression.Parse("0,20,40 * * * *");
        var timeZone = CreateTestTimeZone(new TimeSpan(10, 30, 00), TimeSpan.FromMinutes(30));

        var occurrences = cron.GetNextOccurrences(new DateTime(2026, 03, 29, 01, 40, 00), timeZone).Take(5);

        // The gap skips 02:00 to 02:30: 02:00 and 02:20 move to 02:30 and 02:50, around 02:40
        AssertOccurrences(occurrences,
            new DateTimeOffset(2026, 03, 29, 01, 40, 00, new TimeSpan(10, 30, 00)),
            new DateTimeOffset(2026, 03, 29, 02, 30, 00, TimeSpan.FromHours(11)),
            new DateTimeOffset(2026, 03, 29, 02, 40, 00, TimeSpan.FromHours(11)),
            new DateTimeOffset(2026, 03, 29, 02, 50, 00, TimeSpan.FromHours(11)),
            new DateTimeOffset(2026, 03, 29, 03, 00, 00, TimeSpan.FromHours(11)));
    }

    [Fact]
    public void GetNextOccurrences_TimeZone_Hourly_AcrossSpringForward_ReturnsTheInstantOnce()
    {
        var cron = CronExpression.Parse("0 * * * *");

        var occurrences = cron.GetNextOccurrences(new DateTime(2026, 03, 29, 01, 00, 00), TestParis).Take(3);

        AssertOccurrences(occurrences,
            new DateTimeOffset(2026, 03, 29, 01, 00, 00, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 03, 29, 03, 00, 00, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 03, 29, 04, 00, 00, TimeSpan.FromHours(2)));
    }

    [Theory]
    [InlineData("45 1 * * *", "2026-11-02T01:45:00-05:00")]
    [InlineData("* * * * *", "2026-11-01T02:00:00-05:00")]
    public void GetNextOccurrences_DateTimeOffsetStart_InTheSecondPassOfARepeatedHour_ReturnsNothingBeforeIt(string expression, string expected)
    {
        var cron = CronExpression.Parse(expression);
        var startDate = new DateTimeOffset(2026, 11, 01, 01, 40, 00, TimeSpan.FromHours(-5));
        var expectedOccurrence = DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture);

        AssertOccurrences(cron.GetNextOccurrences(startDate, TestNewYork).Take(1), expectedOccurrence);
        AssertOccurrences(((IRecurrenceRule)cron).GetNextOccurrences(startDate, TestNewYork).Take(1), expectedOccurrence);
        Assert.Equal(expectedOccurrence, ((IRecurrenceRule)cron).GetNextOccurrence(startDate, TestNewYork));
    }

    [Fact]
    public void GetNextOccurrences_DateTimeOffsetStart_AfterAForwardTransition_IncludesAMovedOccurrence()
    {
        var cron = CronExpression.Parse("40 2 * * *");

        // 02:40 does not exist and is read as 03:40+02:00, which is after the start although 02:40 is before 03:30
        var occurrences = cron.GetNextOccurrences(new DateTimeOffset(2026, 03, 29, 03, 30, 00, TimeSpan.FromHours(2)), TestParis).Take(2);

        AssertOccurrences(occurrences,
            new DateTimeOffset(2026, 03, 29, 03, 40, 00, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 03, 30, 02, 40, 00, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void GetNextOccurrences_TimeZone_AtTheLimits_DoesNotThrow()
    {
        Assert.Empty(CronExpression.Parse("0 23 31 12 *").GetNextOccurrences(new DateTime(9999, 12, 31), TestNewYork).Take(2).ToList());
        Assert.Null(CronExpression.Parse("0 * * * *").GetNextOccurrence(new DateTime(9999, 12, 31, 23, 00, 00), TestNewYork));
        Assert.Empty(CronExpression.Parse("* * * * *").GetNextOccurrences(DateTimeOffset.MaxValue, TestParis).ToList());

        // January 1 of year 1 at midnight is before the minimum instant, so the first occurrence is a year later
        AssertOccurrences(
            CronExpression.Parse("0 0 1 1 *").GetNextOccurrences(DateTime.MinValue, TestParis).Take(1),
            new DateTimeOffset(0002, 01, 01, 00, 00, 00, TimeSpan.FromHours(1)));
        AssertOccurrences(
            CronExpression.Parse("0 0 1 1 *").GetNextOccurrences(DateTimeOffset.MinValue, TestParis).Take(1),
            new DateTimeOffset(0002, 01, 01, 00, 00, 00, TimeSpan.FromHours(1)));
    }

#if !INVARIANT_GLOBALIZATION_MODE_ENABLED
    // An IANA identifier does not resolve on Windows when globalization is invariant.
    private static TimeZoneInfo NewYork => TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Fact]
    public void GetNextOccurrences_TimeZone_AcrossSpringForward_KeepsTheWallClockTime()
    {
        var cron = CronExpression.Parse("0 9 * * *");

        var occurrences = cron.GetNextOccurrences(new DateTime(2024, 03, 09, 00, 00, 00), NewYork).Take(3).ToArray();

        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 03, 09, 09, 00, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 03, 10, 09, 00, 00, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2024, 03, 11, 09, 00, 00, TimeSpan.FromHours(-4)));
    }

    [Fact]
    public void GetNextOccurrences_TimeZone_AcrossFallBack_KeepsTheWallClockTime()
    {
        var cron = CronExpression.Parse("0 9 * * *");

        var occurrences = cron.GetNextOccurrences(new DateTime(2024, 11, 02, 00, 00, 00), NewYork).Take(3).ToArray();

        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 11, 02, 09, 00, 00, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2024, 11, 03, 09, 00, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 11, 04, 09, 00, 00, TimeSpan.FromHours(-5)));
    }

    [Fact]
    public void GetNextOccurrences_TimeZone_InvalidLocalTime_UsesTheOffsetBeforeTheGap()
    {
        var cron = CronExpression.Parse("30 2 * * *");

        var occurrences = cron.GetNextOccurrences(new DateTime(2024, 03, 10, 00, 00, 00), NewYork).Take(1).ToArray();

        AssertOccurrences(occurrences, new DateTimeOffset(2024, 03, 10, 03, 30, 00, TimeSpan.FromHours(-4)));
        Assert.Equal(new DateTime(2024, 03, 10, 07, 30, 00, DateTimeKind.Utc), occurrences[0].UtcDateTime);
    }

    [Fact]
    public void GetNextOccurrences_TimeZone_AmbiguousLocalTime_UsesTheFirstOccurrence()
    {
        var cron = CronExpression.Parse("30 1 * * *");

        var occurrences = cron.GetNextOccurrences(new DateTime(2024, 11, 03, 00, 00, 00), NewYork).Take(1).ToArray();

        AssertOccurrences(occurrences, new DateTimeOffset(2024, 11, 03, 01, 30, 00, TimeSpan.FromHours(-4)));
        Assert.Equal(new DateTime(2024, 11, 03, 05, 30, 00, DateTimeKind.Utc), occurrences[0].UtcDateTime);
    }

    [Fact]
    public void GetNextOccurrences_TimeZone_Hourly_AcrossSpringForward_DropsTheInstantRepeatedByTheGap()
    {
        var cron = CronExpression.Parse("0 * * * *");

        var occurrences = cron.GetNextOccurrences(new DateTime(2024, 03, 10, 00, 00, 00), NewYork).Take(5).ToArray();

        // 02:00 does not exist and is read at the -05:00 offset in effect before the gap, which is the instant
        // 03:00 already denotes. RFC 5545 section 3.8.5.3 keeps only one of the two.
        AssertOccurrences(occurrences,
            new DateTimeOffset(2024, 03, 10, 00, 00, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 03, 10, 01, 00, 00, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2024, 03, 10, 03, 00, 00, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2024, 03, 10, 04, 00, 00, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2024, 03, 10, 05, 00, 00, TimeSpan.FromHours(-4)));

        Assert.HasCount(occurrences.Length, occurrences.Select(occurrence => occurrence.UtcDateTime).Distinct());
    }

    [Fact]
    public void GetNextOccurrences_TimeZoneId_MatchesTheTimeZoneInfoOverload()
    {
        var cron = CronExpression.Parse("0 9 * * *");
        var startDate = new DateTime(2024, 03, 09, 00, 00, 00);

        var expected = cron.GetNextOccurrences(startDate, NewYork).Take(4).ToArray();
        var actual = cron.GetNextOccurrences(startDate, "America/New_York").Take(4).ToArray();

        AssertOccurrences(actual, expected);
    }

    [Fact]
    public void GetNextOccurrences_DateTimeOffsetStart_NewYork_InTheSecondPassOfARepeatedHour_ReturnsNothingBeforeIt()
    {
        var cron = CronExpression.Parse("45 1 * * *");

        var occurrences = cron.GetNextOccurrences(new DateTimeOffset(2026, 11, 01, 01, 40, 00, TimeSpan.FromHours(-5)), NewYork).Take(1);

        AssertOccurrences(occurrences, new DateTimeOffset(2026, 11, 02, 01, 45, 00, TimeSpan.FromHours(-5)));
    }

    [Fact]
    public void GetNextOccurrences_TimeZone_AtTheLimitsOfTheSystemTimeZones_DoesNotThrow()
    {
        Assert.Empty(CronExpression.Parse("0 23 31 12 *").GetNextOccurrences(new DateTime(9999, 12, 31), NewYork).Take(2).ToList());
        Assert.Null(CronExpression.Parse("0 * * * *").GetNextOccurrence(new DateTime(9999, 12, 31, 23, 00, 00), NewYork));
        Assert.NotEmpty(CronExpression.Parse("0 0 1 1 *").GetNextOccurrences(DateTime.MinValue, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo")).Take(1).ToList());
    }
#endif

    private static void AssertOccurrences(IEnumerable<DateTimeOffset> occurrences, params DateTimeOffset[] expectedOccurrences)
    {
        var actualList = occurrences.ToList();
        Assert.HasCount(expectedOccurrences.Length, actualList);
        for (var i = 0; i < expectedOccurrences.Length; i++)
        {
            // DateTimeOffset.Equals compares the instants, so an occurrence with the wrong offset would
            // still be equal to the expected one. The wall clock and the offset are compared instead.
            Assert.Equal(expectedOccurrences[i].DateTime, actualList[i].DateTime);
            Assert.Equal(expectedOccurrences[i].Offset, actualList[i].Offset);
        }
    }

    private static void AssertOccurrencesStartWith(IEnumerable<DateTime> occurrences, params DateTime[] expectedOccurrences)
    {
        var actualList = occurrences.Take(expectedOccurrences.Length).ToList();
        Assert.HasCount(expectedOccurrences.Length, actualList);
        for (var i = 0; i < expectedOccurrences.Length; i++)
        {
            Assert.Equal(expectedOccurrences[i], actualList[i]);
        }
    }
}
