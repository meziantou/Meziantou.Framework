using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Tests.Unicode;

/// <summary>The conformance tests of the Unicode Character Database: LineBreakTest.txt (UAX #14), GraphemeBreakTest.txt and WordBreakTest.txt (UAX #29).</summary>
public sealed class UnicodeSegmentationConformanceTests
{
    [Fact]
    public void LineBreakTest()
    {
        var failures = new List<string>();
        var count = 0;
        var breaker = new LineBreaker();
        foreach (var (line, text, expected) in Load("LineBreakTest.txt"))
        {
            count++;
            var opportunities = new LineBreakOpportunity[text.Length];
            breaker.FindOpportunities(text, LineBreakOptions.UnicodeDefault, opportunities);
            var actual = new bool[text.Length];
            for (var i = 0; i < text.Length; i++)
            {
                actual[i] = opportunities[i] >= LineBreakOpportunity.Allowed;
            }

            if (!Matches(text, expected, actual))
            {
                failures.Add(line);
            }
        }

        Assert.True(count > 10_000, "The test file is empty");
        Assert.True(failures.Count == 0, $"{failures.Count} of {count} tests fail:\n" + string.Join('\n', failures.Take(50)));
    }

    [Fact]
    public void GraphemeBreakTest()
    {
        var failures = new List<string>();
        var count = 0;
        foreach (var (line, text, expected) in Load("GraphemeBreakTest.txt"))
        {
            count++;
            var actual = new bool[text.Length];
            GraphemeClusters.GetBoundaries(text, actual);
            var clusters = new List<int>();
            for (var index = 0; index < text.Length; index = GraphemeClusters.GetNextBoundary(text, index))
            {
                clusters.Add(index);
            }

            var expectedClusters = Enumerable.Range(0, text.Length).Where(i => i == 0 || expected[i]).ToList();
            var previous = Enumerable.Range(1, text.Length).Select(i => GraphemeClusters.GetPreviousBoundary(text, i)).Distinct().ToList();
            if (!Matches(text, expected, actual) || !clusters.SequenceEqual(expectedClusters) || !previous.SequenceEqual(expectedClusters) || GraphemeClusters.Count(text) != expectedClusters.Count)
            {
                failures.Add(line);
            }
        }

        Assert.True(count > 500, "The test file is empty");
        Assert.True(failures.Count == 0, $"{failures.Count} of {count} tests fail:\n" + string.Join('\n', failures.Take(50)));
    }

    [Fact]
    public void WordBreakTest()
    {
        var failures = new List<string>();
        var count = 0;
        foreach (var (line, text, expected) in Load("WordBreakTest.txt"))
        {
            count++;
            var actual = new bool[text.Length];
            WordBoundaries.GetBoundaries(text, actual, tailored: false);
            if (!Matches(text, expected, actual))
            {
                failures.Add(line);
            }
        }

        Assert.True(count > 1_000, "The test file is empty");
        Assert.True(failures.Count == 0, $"{failures.Count} of {count} tests fail:\n" + string.Join('\n', failures.Take(50)));
    }

    [Theory]
    [InlineData("희진DJ", new[] { 0, 2 })]
    [InlineData("DJ희진", new[] { 0, 2 })]
    [InlineData("Hello, world", new[] { 0, 5, 6, 7 })]
    [InlineData("can't 3.14", new[] { 0, 5, 6 })]
    public void WordBoundariesTailoring(string text, int[] expected)
    {
        var boundaries = new bool[text.Length];
        WordBoundaries.GetBoundaries(text, boundaries);
        Assert.Equal(expected, Enumerable.Range(0, text.Length).Where(i => boundaries[i]).ToArray());
    }

    [Theory]
    [InlineData("word", true)]
    [InlineData("42", true)]
    [InlineData("\u4E2D", true)]
    [InlineData(" ", false)]
    [InlineData(">>", false)]
    [InlineData("+", false)]
    public void WordBoundariesIsWordLike(string segment, bool expected) => Assert.Equal(expected, WordBoundaries.IsWordLike(segment));

    // Compares the breaks between the code points (the first position is sot, the end of the text is not represented)
    private static bool Matches(string text, bool[] expected, bool[] actual)
    {
        for (var i = 1; i < text.Length; i++)
        {
            if (char.IsLowSurrogate(text[i]) && char.IsHighSurrogate(text[i - 1]))
                continue;

            if (expected[i] != actual[i])
                return false;
        }

        return true;
    }

    // Each line is "÷ 0023 × 0308 ÷ ..." (÷: break, ×: no break) followed by a comment; expected[i] is the break before text[i]
    private static IEnumerable<(string Line, string Text, bool[] Expected)> Load(string name)
    {
        foreach (var line in UnicodeConformanceFiles.ReadLines(name))
        {
            var hash = line.IndexOf('#', StringComparison.Ordinal);
            var data = (hash >= 0 ? line[..hash] : line).Trim();
            if (data.Length == 0)
                continue;

            var builder = new StringBuilder();
            var breaks = new List<bool>();
            var tokens = data.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i + 1 < tokens.Length; i += 2)
            {
                var isBreak = tokens[i] == "÷";
                var codePoint = int.Parse(tokens[i + 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                breaks.Add(isBreak);
                if (codePoint > 0xFFFF)
                {
                    breaks.Add(false);
                }

                builder.Append(char.ConvertFromUtf32(codePoint));
            }

            yield return (line, builder.ToString(), breaks.ToArray());
        }
    }
}
