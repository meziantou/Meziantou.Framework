using Meziantou.Framework.WebPlatform.Css.Layout;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The line breaks of a balanced flex container (https://drafts.csswg.org/css-flexbox-2/#algo-balance).</summary>
public sealed class FlexLineBalancerTests
{
    [Theory]

    // The greedy breaking gives 3 + 1 items
    [InlineData("25 25 25 25", 10, 100, 1, "2 4")]

    // A zero-sized item that fits does not create a line; it does not fit next to an overflowing item
    [InlineData("100 0", 0, 100, 1, "2")]
    [InlineData("150 0", 0, 100, 1, "1 2")]
    [InlineData("150 0 0 0", 10, 100, 1, "1 4")]

    // The negative sizes are floored at zero
    [InlineData("150 -100", 0, 100, 1, "1 2")]

    // The minimum number of lines, limited by the number of items; the most items on the first lines
    [InlineData("20 20 20 20 20 20 20 20 20", 20, 800, 4, "3 5 7 9")]
    [InlineData("20 20 20 20 20 20 20 20 20", 20, 800, 6, "2 4 6 7 8 9")]
    [InlineData("20 20 20", 20, 20, 10000, "1 2 3")]
    [InlineData("50 50 50 50", 0, 200, 1, "4")]
    [InlineData("40 40 40", 20, double.PositiveInfinity, 2, "2 3")]
    [InlineData("100 30 30", 20, double.PositiveInfinity, 2, "1 3")]

    // The lines of the greedy breaking are 80, 20, 100, 100 and 70: the first four items are balanced as 50 and 50, and the
    // item of 10 joins the last one (WPT css/css-flexbox/balance/balance-005.html)
    [InlineData("20 20 20 20 100 20 20 20 10 70", 10, 100, 1, "2 4 5 8 10")]

    // The items around an item that fills a line are balanced independently (WPT css/css-flexbox/balance/balance-start-bias-001.html)
    [InlineData("25 25 25 25 25 25 25 100 25 25 25 25 25 25 25 25", 10, 100, 1, "3 5 7 8 11 14 16")]
    public void Balance(string sizes, double gap, double available, int minimumLineCount, string expected)
    {
        var values = sizes.Split(' ').Select(static value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(expected, string.Join(' ', FlexLineBalancer.Balance(values, gap, available, minimumLineCount)!));
    }

    [Fact]
    public void Balance_GivesThePartitionWithTheMinimumErrorAndTheMostItemsOnTheFirstLines()
    {
        // The partitions of small sets of items are enumerated: the result has the smallest sum of the squares of the sizes of
        // the lines, and is the last one in the order of the breaks among the ones with that sum
        var state = 12345u;
        double[] choices = [0, 0, 10, 10, 20, 25, 30, 50, 70, 100, 150];
        for (var iteration = 0; iteration < 3000; iteration++)
        {
            var sizes = new double[1 + Next(ref state, 9)];
            for (var i = 0; i < sizes.Length; i++)
            {
                sizes[i] = choices[Next(ref state, choices.Length)];
            }

            var gap = Next(ref state, 3) * 10;
            var available = Next(ref state, 5) == 0 ? double.PositiveInfinity : (1 + Next(ref state, 15)) * 10;
            var minimumLineCount = 1 + Next(ref state, 7);

            var expected = BalanceByEnumeration(sizes, gap, available, minimumLineCount);
            var actual = FlexLineBalancer.Balance(sizes, gap, available, minimumLineCount);
            Assert.True(expected.SequenceEqual(actual!), $"sizes: {string.Join(' ', sizes)}; gap: {gap}; available: {available}; lines: {minimumLineCount}; expected: {string.Join(' ', expected)}; actual: {string.Join(' ', actual!)}");
        }
    }

    [Fact]
    public void Balance_ManyItems()
    {
        // 20,000 items on at least 3 lines in an unlimited size, and on the lines of a limited size
        var state = 42u;
        var sizes = new double[20_000];
        for (var i = 0; i < sizes.Length; i++)
        {
            sizes[i] = Next(ref state, 100);
        }

        var total = sizes.Sum();
        var ends = FlexLineBalancer.Balance(sizes, gap: 0, double.PositiveInfinity, minimumLineCount: 3)!;
        Assert.Equal(3, ends.Length);
        Assert.InRange(sizes.AsSpan(0, ends[0]).ToArray().Sum(), (total / 3) - 100, (total / 3) + 100);

        ends = FlexLineBalancer.Balance(sizes, gap: 5, available: 1000, minimumLineCount: 1)!;
        var start = 0;
        foreach (var end in ends)
        {
            Assert.InRange(sizes.AsSpan(start, end - start).ToArray().Sum() + (5 * (end - start - 1)), 0, 1000);
            start = end;
        }

        Assert.Equal(sizes.Length, start);

        // Every item can start many lines when the number of lines is far from the number of items and from the number of lines
        // of the greedy breaking: the container is not balanced when there are too many possible breaks
        ends = FlexLineBalancer.Balance(sizes.AsSpan(0, 4000), gap: 0, double.PositiveInfinity, minimumLineCount: 2000)!;
        Assert.Equal(2000, ends.Length);
        Assert.Equal(4000, ends[^1]);
        Assert.Null(FlexLineBalancer.Balance(sizes, gap: 0, double.PositiveInfinity, minimumLineCount: 10_000));
    }

    /// <summary>A reproducible sequence of numbers below <paramref name="count"/> (xorshift).</summary>
    private static int Next(ref uint state, int count)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (int)(state % (uint)count);
    }

    private static int[] BalanceByEnumeration(double[] sizes, double gap, double available, int minimumLineCount)
    {
        var floored = sizes.Select(static size => Math.Max(0, size)).ToArray();

        // The number of lines of the greedy breaking
        var lineCount = 0;
        for (var i = 0; i < floored.Length;)
        {
            var end = i + 1;
            while (end < floored.Length && GetSize(i, end + 1) <= available)
            {
                end++;
            }

            lineCount++;
            i = end;
        }

        lineCount = Math.Max(lineCount, Math.Min(minimumLineCount, floored.Length));
        int[]? best = null;
        var bestCost = double.PositiveInfinity;
        Enumerate(new int[lineCount], 0, 0, 0);
        return best!;

        double GetSize(int start, int end) => floored[start..end].Sum() + (gap * (end - start - 1));

        void Enumerate(int[] ends, int line, int start, double cost)
        {
            if (line == lineCount - 1)
            {
                ends[line] = floored.Length;
                var size = GetSize(start, floored.Length);
                if (floored.Length - start > 1 && size > available)
                    return;

                // The partitions are enumerated in the increasing order of their breaks
                if (cost + (size * size) <= bestCost)
                {
                    bestCost = cost + (size * size);
                    best = [.. ends];
                }

                return;
            }

            for (var end = start + 1; end <= floored.Length - (lineCount - 1 - line); end++)
            {
                var size = GetSize(start, end);
                if (end - start > 1 && size > available)
                    break;

                ends[line] = end;
                Enumerate(ends, line + 1, end, cost + (size * size));
            }
        }
    }
}
