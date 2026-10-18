using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Tests.Unicode;

/// <summary>The conformance tests of the Unicode Bidirectional Algorithm (UAX #9): BidiTest.txt and BidiCharacterTest.txt.</summary>
public sealed class BidiConformanceTests
{
    [Fact]
    public void BidiTest()
    {
        var failures = new List<string>();
        var count = 0;
        var resolver = new BidiResolver();
        string[] expectedLevels = [];
        int[] expectedOrder = [];
        foreach (var rawLine in File.ReadLines(TestPaths.SpecData("unicode", "BidiTest.txt")))
        {
            var hash = rawLine.IndexOf('#', StringComparison.Ordinal);
            var line = (hash >= 0 ? rawLine[..hash] : rawLine).Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("@Levels:", StringComparison.Ordinal))
            {
                expectedLevels = line["@Levels:".Length..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                continue;
            }

            if (line.StartsWith("@Reorder:", StringComparison.Ordinal))
            {
                expectedOrder = line["@Reorder:".Length..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                continue;
            }

            if (line[0] == '@')
                continue;

            var fields = line.Split(';');
            var classes = fields[0].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<BidiClass>).ToArray();
            var bitset = int.Parse(fields[1].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            foreach (var (bit, direction) in new[] { (1, BidiParagraphDirection.AutoLeftToRight), (2, BidiParagraphDirection.LeftToRight), (4, BidiParagraphDirection.RightToLeft) })
            {
                if ((bitset & bit) == 0)
                    continue;

                count++;
                resolver.Resolve([], classes, direction);
                if (!Check(resolver, expectedLevels, expectedOrder, out var actual))
                {
                    failures.Add($"{fields[0].Trim()} ({direction}): expected levels {string.Join(' ', expectedLevels)} order {string.Join(' ', expectedOrder)}, actual {actual}");
                }
            }
        }

        Assert.True(count > 100_000, "The test file is empty");
        Assert.True(failures.Count == 0, $"{failures.Count} of {count} tests fail:\n" + string.Join('\n', failures.Take(50)));
    }

    [Fact]
    public void BidiCharacterTest()
    {
        var failures = new List<string>();
        var count = 0;
        var resolver = new BidiResolver();
        foreach (var rawLine in File.ReadLines(TestPaths.SpecData("unicode", "BidiCharacterTest.txt")))
        {
            var hash = rawLine.IndexOf('#', StringComparison.Ordinal);
            var line = (hash >= 0 ? rawLine[..hash] : rawLine).Trim();
            if (line.Length == 0)
                continue;

            count++;
            var fields = line.Split(';');
            var codePoints = fields[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => int.Parse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
            var direction = fields[1] switch
            {
                "0" => BidiParagraphDirection.LeftToRight,
                "1" => BidiParagraphDirection.RightToLeft,
                _ => BidiParagraphDirection.AutoLeftToRight,
            };
            var expectedParagraphLevel = int.Parse(fields[2], CultureInfo.InvariantCulture);
            var expectedLevels = fields[3].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var expectedOrder = fields[4].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();

            // The levels of the test are per code point; the resolver indexes UTF-16 code units
            var builder = new StringBuilder();
            var starts = new int[codePoints.Length];
            for (var i = 0; i < codePoints.Length; i++)
            {
                starts[i] = builder.Length;
                builder.Append(char.ConvertFromUtf32(codePoints[i]));
            }

            var text = builder.ToString();
            resolver.Resolve(text, direction);
            var lineLevels = new byte[text.Length];
            resolver.GetLineLevels(0, text.Length, lineLevels);
            var levels = starts.Select(start => lineLevels[start]).ToArray();
            if (resolver.Paragraphs[0].Level != expectedParagraphLevel || !Check(levels, expectedLevels, expectedOrder, out var actual))
            {
                failures.Add($"{line}: paragraph level {resolver.Paragraphs[0].Level}, {Check(levels, expectedLevels, expectedOrder, out actual)} {actual}");
            }

            // The surrogate pairs keep the level of their high surrogate
            for (var i = 0; i < codePoints.Length; i++)
            {
                if (codePoints[i] > 0xFFFF && lineLevels[starts[i] + 1] != lineLevels[starts[i]])
                {
                    failures.Add($"{line}: surrogate pair {i}");
                }
            }
        }

        Assert.True(count > 10_000, "The test file is empty");
        Assert.True(failures.Count == 0, $"{failures.Count} of {count} tests fail:\n" + string.Join('\n', failures.Take(50)));
    }

    private static bool Check(BidiResolver resolver, string[] expectedLevels, int[] expectedOrder, out string actual)
    {
        var levels = new byte[resolver.Length];
        resolver.GetLineLevels(0, resolver.Length, levels);
        return Check(levels, expectedLevels, expectedOrder, out actual);
    }

    // Compares the levels (except the characters removed by X9, "x") and the visual order of the characters that are not removed
    private static bool Check(byte[] levels, string[] expectedLevels, int[] expectedOrder, out string actual)
    {
        var success = levels.Length == expectedLevels.Length;
        var kept = new List<int>();
        for (var i = 0; i < levels.Length && success; i++)
        {
            if (expectedLevels[i] == "x")
                continue;

            kept.Add(i);
            success &= levels[i] == int.Parse(expectedLevels[i], CultureInfo.InvariantCulture);
        }

        var keptLevels = kept.Select(i => levels[i]).ToArray();
        var order = new int[keptLevels.Length];
        BidiResolver.ReorderVisual(keptLevels, order);
        var visual = order.Select(i => kept[i]).ToArray();
        success &= visual.SequenceEqual(expectedOrder);

        // The visual runs give the same order
        var runs = new List<BidiRun>();
        BidiResolver.GetVisualRuns(keptLevels, runs);
        var runOrder = new List<int>();
        foreach (var run in runs)
        {
            for (var k = 0; k < run.End - run.Start; k++)
            {
                runOrder.Add(kept[run.IsRightToLeft ? run.End - 1 - k : run.Start + k]);
            }
        }

        success &= runOrder.SequenceEqual(expectedOrder);
        actual = $"levels {string.Join(' ', levels)} order {string.Join(' ', visual)}";
        return success;
    }
}
