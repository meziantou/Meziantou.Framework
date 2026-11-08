using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.TextDiffFuzz;

/// <summary>
/// Every diff rebuilds both texts, whatever the algorithm, the chunker, the comparison options and the work limits. Its
/// equal entries hold chunks that compare equal, and it reports differences exactly when the chunks of the texts differ.
/// A work limit never makes a Myers diff smaller than the minimal one, and an edit cost limit at or above the minimal
/// number of edits gives the minimal diff.
/// </summary>
public static partial class TextDiffFuzzTargets
{
    // Option byte: bits 0-1 the algorithm, bits 2-3 the chunker, bits 4-6 IgnoreCase, IgnoreWhitespace, IgnoreEndOfLine
    private const byte NoEditCostLimit = byte.MaxValue;
    private const byte TextSeparator = 0;

    private static readonly TextDiffAlgorithm[] Algorithms = [TextDiffAlgorithm.Myers, TextDiffAlgorithm.Patience, TextDiffAlgorithm.Histogram, TextDiffAlgorithm.HuntSzymanski];
    private static readonly TextChunker[] Chunkers = [TextChunker.Lines, TextChunker.Words, TextChunker.Characters, TextChunker.Lines];

    private static readonly string[] Tokens =
    [
        "\n", "\r\n", "\r", "\u0085", "\u2028", "\u2029", " ", "  ", "\t", "\u00A0", "a", "A", "b", "line", "LINE", "\uD83D\uDE00", "\uD800", "e\u0301",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("textdiff-diff", Diff) { Seeds = GetSeeds, Dictionary = Tokens },
        new("textdiff-hierarchy", Hierarchy) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    /// <summary>Diffs two texts without a limit, with an edit cost limit and with an expired timeout, and checks every result.</summary>
    private static void Diff(ReadOnlySpan<byte> data)
    {
        var options = ReadOptions(ref data, out var maxEditCost);
        var oldText = FuzzText.Decode(FuzzInput.ReadUntil(ref data, TextSeparator));
        var newText = FuzzText.Decode(data);

        var unlimited = TextDiff.ComputeDiff(oldText, newText, options);
        Check(oldText, newText, options, unlimited, "unlimited");

        options.MaxEditCost = maxEditCost;
        var limited = TextDiff.ComputeDiff(oldText, newText, options);
        Check(oldText, newText, options, limited, "MaxEditCost");
        FuzzAssert.Equal(unlimited.HasDifferences, limited.HasDifferences, "MaxEditCost changes HasDifferences");
        if (options.Algorithm is TextDiffAlgorithm.Myers)
        {
            var minimalEdits = CountEdits(unlimited);
            FuzzAssert.True(CountEdits(limited) >= minimalEdits, "MaxEditCost gives a diff smaller than the minimal one");
            if (maxEditCost >= minimalEdits)
            {
                FuzzAssert.True(unlimited.Entries.SequenceEqual(limited.Entries), "A MaxEditCost at or above the edit distance changes the diff");
            }
        }

        options.MaxEditCost = null;
        options.Timeout = TimeSpan.Zero;
        var expired = TextDiff.ComputeDiff(oldText, newText, options);
        Check(oldText, newText, options, expired, "Timeout");
        FuzzAssert.Equal(unlimited.HasDifferences, expired.HasDifferences, "An expired Timeout changes HasDifferences");
        if (options.Algorithm is TextDiffAlgorithm.Myers)
        {
            FuzzAssert.True(CountEdits(expired) >= CountEdits(unlimited), "An expired Timeout gives a diff smaller than the minimal one");
        }
    }

    /// <summary>Diffs two texts line by line, then word by word, then character by character, and checks that the result rebuilds both texts.</summary>
    private static void Hierarchy(ReadOnlySpan<byte> data)
    {
        var options = ReadOptions(ref data, out var maxEditCost);
        options.MaxEditCost = maxEditCost;
        var oldText = FuzzText.Decode(FuzzInput.ReadUntil(ref data, TextSeparator));
        var newText = FuzzText.Decode(data);
        var expectedOld = options.IgnoreEndOfLine ? oldText.ReplaceLineEndings("\n") : oldText;
        var expectedNew = options.IgnoreEndOfLine ? newText.ReplaceLineEndings("\n") : newText;

        TextChunker[] chunkers = [TextChunker.Lines, TextChunker.Words, TextChunker.Characters];
        foreach (var timeout in (TimeSpan?[])[null, TimeSpan.Zero])
        {
            options.Timeout = timeout;
            var result = TextDiff.ComputeHierarchyDiff(oldText, newText, chunkers, options);
            var (rebuiltOld, rebuiltNew) = Rebuild(result.Entries);
            FuzzAssert.Equal(expectedOld, rebuiltOld, $"The hierarchical diff does not rebuild the old text (Timeout: {timeout})");
            FuzzAssert.Equal(expectedNew, rebuiltNew, $"The hierarchical diff does not rebuild the new text (Timeout: {timeout})");
        }
    }

    private static TextDiffOptions ReadOptions(ref ReadOnlySpan<byte> data, out int? maxEditCost)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var editCost = FuzzInput.ReadByte(ref data);
        maxEditCost = editCost is NoEditCostLimit ? null : editCost;
        return new TextDiffOptions
        {
            Algorithm = Algorithms[flags & 3],
            Chunker = Chunkers[(flags >> 2) & 3],
            IgnoreCase = (flags & 0x10) is not 0,
            IgnoreWhitespace = (flags & 0x20) is not 0,
            IgnoreEndOfLine = (flags & 0x40) is not 0,
        };
    }

    private static void Check(string oldText, string newText, TextDiffOptions options, TextDiffResult result, string variant)
    {
        var expectedOld = options.IgnoreEndOfLine ? oldText.ReplaceLineEndings("\n") : oldText;
        var expectedNew = options.IgnoreEndOfLine ? newText.ReplaceLineEndings("\n") : newText;

        var rebuiltOld = new StringBuilder();
        var rebuiltNew = new StringBuilder();
        var hasEdits = false;
        foreach (var entry in result.Entries)
        {
            switch (entry.Operation)
            {
                case TextDiffOperation.Equal:
                    FuzzAssert.True(entry.OldText is not null && entry.NewText is not null, $"An equal entry misses a side ({variant})");
                    if (!options.IgnoreCase && !options.IgnoreWhitespace)
                    {
                        FuzzAssert.Equal(entry.OldText, entry.NewText, $"An equal entry holds different chunks ({variant})");
                    }

                    rebuiltOld.Append(entry.OldText);
                    rebuiltNew.Append(entry.NewText);
                    break;

                case TextDiffOperation.Delete:
                    FuzzAssert.True(entry.OldText is not null && entry.NewText is null, $"A deleted entry has the wrong sides ({variant})");
                    rebuiltOld.Append(entry.OldText);
                    hasEdits = true;
                    break;

                case TextDiffOperation.Insert:
                    FuzzAssert.True(entry.OldText is null && entry.NewText is not null, $"An inserted entry has the wrong sides ({variant})");
                    rebuiltNew.Append(entry.NewText);
                    hasEdits = true;
                    break;

                default:
                    FuzzAssert.Fail($"Unknown operation {entry.Operation} ({variant})");
                    break;
            }
        }

        FuzzAssert.Equal(expectedOld, rebuiltOld.ToString(), $"The diff does not rebuild the old text ({variant})");
        FuzzAssert.Equal(expectedNew, rebuiltNew.ToString(), $"The diff does not rebuild the new text ({variant})");
        FuzzAssert.Equal(hasEdits, result.HasDifferences, $"HasDifferences disagrees with the entries ({variant})");
        if (!options.IgnoreCase && !options.IgnoreWhitespace)
        {
            FuzzAssert.Equal(!string.Equals(expectedOld, expectedNew, StringComparison.Ordinal), result.HasDifferences, $"HasDifferences disagrees with the texts ({variant})");
        }
    }

    private static (string OldText, string NewText) Rebuild(IReadOnlyList<TextDiffHierarchyEntry> entries)
    {
        var oldText = new StringBuilder();
        var newText = new StringBuilder();
        foreach (var entry in entries)
        {
            if (entry.Operation is not TextDiffHierarchyOperation.Insert)
            {
                oldText.Append(entry.OldText);
            }

            if (entry.Operation is not TextDiffHierarchyOperation.Delete)
            {
                newText.Append(entry.NewText);
            }

            if (entry.Children.Count > 0)
            {
                FuzzAssert.Equal(TextDiffHierarchyOperation.Replace, entry.Operation, "Only a replaced entry has children");
                var (childOld, childNew) = Rebuild(entry.Children);
                FuzzAssert.Equal(entry.OldText, childOld, "The children do not rebuild the old chunk");
                FuzzAssert.Equal(entry.NewText, childNew, "The children do not rebuild the new chunk");
            }
        }

        return (oldText.ToString(), newText.ToString());
    }

    private static int CountEdits(TextDiffResult result) => result.Entries.Count(entry => entry.Operation is not TextDiffOperation.Equal);

    // Each pair of consecutive string literals of the unit tests, which are mostly an old and a new text, with a few
    // combinations of options
    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        byte[][] prefixes =
        [
            [0x00, NoEditCostLimit], // Myers, lines
            [0x00, 1],               // Myers, lines, MaxEditCost = 1
            [0x09, 2],               // Patience, characters
            [0x76, NoEditCostLimit], // Histogram, words, all the comparison options
            [0x03, NoEditCostLimit], // HuntSzymanski, lines
        ];

        var texts = GetTestStrings().ToList();
        for (var i = 0; i + 1 < texts.Count; i++)
        {
            var prefix = prefixes[i % prefixes.Length];
            yield return new FuzzSeed(string.Create(CultureInfo.InvariantCulture, $"{i:D3}"), (byte[])[.. prefix, .. FuzzText.Encode(texts[i]), TextSeparator, .. FuzzText.Encode(texts[i + 1])]);
        }
    }

    private static IEnumerable<string> GetTestStrings()
    {
        var source = FuzzResources.ReadText(typeof(TextDiffFuzzTargets).Assembly, "TextDiffTests.cs");
        foreach (Match match in StringLiteralRegex().Matches(source))
        {
            var value = Unescape(match.Groups["value"].Value);
            if (!value.Contains('\0', StringComparison.Ordinal))
                yield return value;
        }
    }

    private static string Unescape(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                result.Append(value[i]);
                continue;
            }

            i++;
            switch (value[i])
            {
                case 'n':
                    result.Append('\n');
                    break;
                case 'r':
                    result.Append('\r');
                    break;
                case 't':
                    result.Append('\t');
                    break;
                case 'u' when i + 4 < value.Length:
                    result.Append((char)int.Parse(value.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default:
                    result.Append(value[i]);
                    break;
            }
        }

        return result.ToString();
    }

    [GeneratedRegex(@"(?<![@$])""(?<value>(?:[^""\\\r\n]|\\.)*)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringLiteralRegex();
}
