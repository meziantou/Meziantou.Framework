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
    [InlineData("* * * * * * *")]
    [InlineData("a * * * * *")]
    public void CronExpression_Parse_InvalidExpression(string expression)
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse(expression));
        Assert.Throws<FormatException>(() => CronExpression.Parse(expression.AsSpan()));

        Assert.False(CronExpression.TryParse(expression, out _));
        Assert.False(CronExpression.TryParse(expression.AsSpan(), out _));
    }

    [Fact]
    public void CronExpression_Parse_NullExpression()
    {
        Assert.Throws<ArgumentNullException>(() => CronExpression.Parse((string?)null!));
        Assert.Throws<ArgumentNullException>(() => CronExpression.Parse((ReadOnlySpan<char>)null!));
        Assert.False(CronExpression.TryParse((string?)null!, out _));
        Assert.False(CronExpression.TryParse((ReadOnlySpan<char>)null!, out _));
    }

    [Theory]
    [InlineData("* * * * *", "2024-01-01T00:00:00", "2024-01-01T00:01:00", "2024-01-01T00:02:00", "2024-01-01T00:03:00")]
    [InlineData("*/1 * * * *", "2024-01-01T00:00:00", "2024-01-01T00:01:00", "2024-01-01T00:02:00", "2024-01-01T00:03:00")]
    public void EvaluateCronExpression(string expression, params string[] expectedOccurrences)
    {
        var cron = CronExpression.Parse(expression);
        var expectedDates = expectedOccurrences.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        AssertOccurrencesStartWith(cron.GetNextOccurrences(new DateTime(2024, 1, 1, 0, 0, 0)), expectedDates);
    }

    private static void AssertOccurrencesStartWith(IEnumerable<DateTime> occurrences, params DateTime[] expectedOccurrences)
    {
        AssertOccurrences(occurrences, checkEnd: false, maxOccurences: null, expectedOccurrences);
    }

    private static void AssertOccurrences(IEnumerable<DateTime> occurrences, params DateTime[] expectedOccurrences)
    {
        AssertOccurrences(occurrences, checkEnd: false, expectedOccurrences.Length, expectedOccurrences);
    }

    private static void AssertOccurrences(IEnumerable<DateTime> occurrences, bool checkEnd, int? maxOccurences, params DateTime[] expectedOccurrences)
    {
        var occurrenceCount = 0;
        using var enumerator1 = occurrences.GetEnumerator();
        using (var enumerator2 = ((IEnumerable<DateTime>)expectedOccurrences).GetEnumerator())
        {
            while (enumerator1.MoveNext() && enumerator2.MoveNext())
            {
                occurrenceCount++;
                Assert.Equal(enumerator2.Current, enumerator1.Current);
            }
        }

        if (maxOccurences.HasValue)
        {
            while (enumerator1.MoveNext())
            {
                Assert.True(occurrenceCount <= maxOccurences.Value);
                occurrenceCount++;
            }
        }
        else
        {
            if (checkEnd)
            {
                Assert.False(enumerator1.MoveNext());
            }
        }
    }
}
