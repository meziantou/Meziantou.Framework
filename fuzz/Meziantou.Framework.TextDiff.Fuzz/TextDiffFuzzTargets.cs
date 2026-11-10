using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.TextDiffFuzz;

/// <summary>
/// Every diff rebuilds both texts, whatever the algorithm, the chunker, the comparison options and the work limits. Its
/// equal entries hold chunks that compare equal, and it reports differences exactly when the chunks of the texts differ.
/// A work limit never makes a Myers diff smaller than the minimal one, and an edit cost limit at or above the minimal
/// number of edits gives the minimal diff. Move detection only tags entries, and pairs equal blocks. A unified diff
/// applies to the old text and gives the new one, and a parsed unified diff writes back to a text that parses the same.
/// A merge rebuilds the three texts, and takes the only changed side.
/// </summary>
public static partial class TextDiffFuzzTargets
{
    // Option byte: bits 0-1 the algorithm, bits 2-3 the chunker, bits 4-6 IgnoreCase, IgnoreWhitespace, IgnoreEndOfLine,
    // bit 7 a culture-aware comparison that ignores accents
    private const byte NoEditCostLimit = byte.MaxValue;
    private const byte TextSeparator = 0;

    private static readonly TextDiffAlgorithm[] Algorithms = [TextDiffAlgorithm.Myers, TextDiffAlgorithm.Patience, TextDiffAlgorithm.Histogram, TextDiffAlgorithm.HuntSzymanski];
    private static readonly TextChunker[] Chunkers = [TextChunker.Lines, TextChunker.Words, TextChunker.Characters, TextChunker.Graphemes];

    private static readonly string[] Tokens =
    [
        "\n", "\r\n", "\r", "\u0085", "\u2028", "\u2029", " ", "  ", "\t", "\u00A0", "a", "A", "b", "line", "LINE", "\uD83D\uDE00", "\uD800", "e\u0301", "\u00E9",
    ];

    private static readonly string[] PatchTokens =
    [
        "--- ", "+++ ", "@@ -", " +", " @@", ",", "0", "1", "2", "10", "\\ No newline at end of file\n", "\n", "\r\n", "\t", " ", "-", "+", "a\n", "diff --git ",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("textdiff-diff", Diff) { Seeds = GetSeeds, Dictionary = Tokens },
        new("textdiff-hierarchy", Hierarchy) { Seeds = GetSeeds, Dictionary = Tokens },
        new("textdiff-unified-roundtrip", UnifiedRoundTrip) { Seeds = GetSeeds, Dictionary = Tokens },
        new("textdiff-unified-parse", UnifiedParse) { Seeds = GetPatchSeeds, Dictionary = PatchTokens },
        new("textdiff-merge", Merge) { Seeds = GetMergeSeeds, Dictionary = Tokens },
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
        options.DetectMoves = true;
        var moves = TextDiff.ComputeDiff(oldText, newText, options);
        Check(oldText, newText, options, moves, "DetectMoves");
        CheckMoves(unlimited, moves, options);
        options.DetectMoves = false;

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

    /// <summary>Writes the unified diff of two texts, reads it back and applies it to the old text.</summary>
    private static void UnifiedRoundTrip(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var contextLines = FuzzInput.ReadByte(ref data) % 8;
        var oldText = FuzzText.Decode(FuzzInput.ReadUntil(ref data, TextSeparator));
        var newText = FuzzText.Decode(data);

        var diff = UnifiedDiff.Create(oldText, newText, new UnifiedDiffOptions { Algorithm = Algorithms[flags & 3], ContextLines = contextLines });
        FuzzAssert.Equal(newText, diff.Apply(oldText), "The unified diff does not give the new text");
        FuzzAssert.Equal(string.Equals(oldText, newText, StringComparison.Ordinal), diff.Hunks.Count is 0, "A diff of equal texts has hunks, or a diff of different texts has none");

        var patch = diff.ToString();
        var files = UnifiedDiff.Parse(patch);
        if (diff.Hunks.Count is 0)
        {
            FuzzAssert.Equal(0, files.Count, "An empty diff parses to a file");
            return;
        }

        FuzzAssert.Equal(1, files.Count, "The unified diff does not parse to one file");
        CheckSameFile(diff, files[0]);
        FuzzAssert.Equal(newText, UnifiedDiff.Apply(oldText, patch), "The parsed unified diff does not give the new text");
    }

    /// <summary>Reads an arbitrary unified diff, writes each file back and reads it again, then applies it to a text.</summary>
    private static void UnifiedParse(ReadOnlySpan<byte> data)
    {
        var patch = FuzzText.Decode(FuzzInput.ReadUntil(ref data, TextSeparator));
        var text = FuzzText.Decode(data);

        IReadOnlyList<UnifiedDiffFile> files;
        try
        {
            files = UnifiedDiff.Parse(patch);
        }
        catch (FormatException)
        {
            return;
        }

        foreach (var file in files)
        {
            foreach (var hunk in file.Hunks)
            {
                FuzzAssert.Equal(hunk.OldLineCount, hunk.Lines.Count(line => line.Operation is not TextDiffOperation.Insert), "The old line count of a hunk disagrees with its lines");
                FuzzAssert.Equal(hunk.NewLineCount, hunk.Lines.Count(line => line.Operation is not TextDiffOperation.Delete), "The new line count of a hunk disagrees with its lines");
            }

            if (file.Hunks.Count > 0)
            {
                var reparsed = UnifiedDiff.Parse(file.ToString());
                FuzzAssert.Equal(1, reparsed.Count, "A written file does not parse to one file");
                CheckSameFile(file, reparsed[0]);
            }

            try
            {
                var patched = file.Apply(text);
                FuzzAssert.True(patched is not null, "Apply returns null");
            }
            catch (UnifiedDiffApplyException exception)
            {
                FuzzAssert.True(exception.HunkIndex >= 0 && exception.HunkIndex < file.Hunks.Count, "The index of the failing hunk is out of range");
            }
        }
    }

    /// <summary>Merges three texts and checks the regions, and that a merge with an unchanged side takes the other side.</summary>
    private static void Merge(ReadOnlySpan<byte> data)
    {
        var options = ReadOptions(ref data, out var maxEditCost);
        options.MaxEditCost = maxEditCost;
        var baseText = FuzzText.Decode(FuzzInput.ReadUntil(ref data, TextSeparator));
        var oursText = FuzzText.Decode(FuzzInput.ReadUntil(ref data, TextSeparator));
        var theirsText = FuzzText.Decode(data);
        var mergeOptions = new TextMergeOptions { DiffOptions = options, ConflictStyle = (options.Algorithm is TextDiffAlgorithm.Myers) ? TextMergeConflictStyle.Diff3 : TextMergeConflictStyle.Merge };

        var result = TextMerge.Merge(baseText, oursText, theirsText, mergeOptions);
        FuzzAssert.Equal(Normalize(baseText, options), string.Concat(result.Chunks.Select(chunk => chunk.BaseText)), "The merge regions do not rebuild the base");
        FuzzAssert.Equal(Normalize(oursText, options), string.Concat(result.Chunks.Select(chunk => chunk.OursText)), "The merge regions do not rebuild ours");
        FuzzAssert.Equal(Normalize(theirsText, options), string.Concat(result.Chunks.Select(chunk => chunk.TheirsText)), "The merge regions do not rebuild theirs");
        FuzzAssert.Equal(result.Chunks.Any(chunk => chunk.Kind is TextMergeChunkKind.Conflict), result.HasConflicts, "HasConflicts disagrees with the regions");
        if (!result.HasConflicts)
        {
            FuzzAssert.Equal(string.Concat(result.Chunks.Select(chunk => chunk.Text)), result.Text, "The merged text is not the text of the regions");
        }

        // Ignored differences keep the text of ours
        if (!IsOrdinal(options) || options.IgnoreCase)
            return;

        FuzzAssert.Equal(Normalize(oursText, options), TextMerge.Merge(baseText, oursText, baseText, mergeOptions).Text, "A merge with unchanged theirs does not give ours");
        FuzzAssert.Equal(Normalize(theirsText, options), TextMerge.Merge(baseText, baseText, theirsText, mergeOptions).Text, "A merge with unchanged ours does not give theirs");
        FuzzAssert.Equal(Normalize(oursText, options), TextMerge.Merge(baseText, oursText, oursText, mergeOptions).Text, "A merge of the same change does not give it");
    }

    private static void CheckSameFile(UnifiedDiffFile expected, UnifiedDiffFile actual)
    {
        FuzzAssert.Equal(expected.OldFileName, actual.OldFileName, "The old file name does not round trip");
        FuzzAssert.Equal(expected.NewFileName, actual.NewFileName, "The new file name does not round trip");
        FuzzAssert.Equal(expected.Hunks.Count, actual.Hunks.Count, "The hunk count does not round trip");
        for (var i = 0; i < expected.Hunks.Count; i++)
        {
            var expectedHunk = expected.Hunks[i];
            var actualHunk = actual.Hunks[i];
            FuzzAssert.Equal(expectedHunk.ToString(), actualHunk.ToString(), "A hunk does not round trip");
            FuzzAssert.Equal(expectedHunk.OldStart, actualHunk.OldStart, "OldStart does not round trip");
            FuzzAssert.Equal(expectedHunk.NewStart, actualHunk.NewStart, "NewStart does not round trip");
            FuzzAssert.Equal(expectedHunk.SectionHeading, actualHunk.SectionHeading, "SectionHeading does not round trip");
            FuzzAssert.True(expectedHunk.Lines.SequenceEqual(actualHunk.Lines), "The lines of a hunk do not round trip");
        }
    }

    private static void CheckMoves(TextDiffResult withoutMoves, TextDiffResult withMoves, TextDiffOptions options)
    {
        FuzzAssert.Equal(withoutMoves.Entries.Count, withMoves.Entries.Count, "DetectMoves changes the entries");
        for (var i = 0; i < withoutMoves.Entries.Count; i++)
        {
            var entry = withMoves.Entries[i];
            FuzzAssert.True(withoutMoves.Entries[i] == new TextDiffEntry(entry.Operation, entry.OldText, entry.NewText), "DetectMoves changes an entry");
            FuzzAssert.True(entry.MoveId is null || entry.Operation is not TextDiffOperation.Equal, "An equal entry is part of a move");
        }

        var moveIds = withMoves.Entries.Where(entry => entry.MoveId is not null && entry.Operation is TextDiffOperation.Delete).Select(entry => entry.MoveId!.Value).Distinct().ToList();
        FuzzAssert.Equal(moveIds.Count, withMoves.Entries.Where(entry => entry.MoveId is not null).Select(entry => entry.MoveId).Distinct().Count(), "A move has no deleted chunk");
        for (var i = 0; i < moveIds.Count; i++)
        {
            FuzzAssert.Equal(i, moveIds[i], "The move identifiers are not 0, 1, 2... in the order of the deleted blocks");
            var deleted = withMoves.Entries.Where(entry => entry.MoveId == i && entry.Operation is TextDiffOperation.Delete).Select(entry => entry.OldText!).ToList();
            var inserted = withMoves.Entries.Where(entry => entry.MoveId == i && entry.Operation is TextDiffOperation.Insert).Select(entry => entry.NewText!).ToList();
            FuzzAssert.Equal(deleted.Count, inserted.Count, "A move has a different number of deleted and inserted chunks");
            FuzzAssert.True(deleted.Any(text => !string.IsNullOrWhiteSpace(text)), "A move holds only whitespace");
            if (IsOrdinal(options) && !options.IgnoreCase)
            {
                FuzzAssert.True(deleted.SequenceEqual(inserted, StringComparer.Ordinal), "A move pairs different chunks");
            }
        }
    }

    private static bool IsOrdinal(TextDiffOptions options) => !options.IgnoreWhitespace && options.CompareOptions is CompareOptions.None && options.Culture is null;

    private static string Normalize(string text, TextDiffOptions options) => options.IgnoreEndOfLine ? text.ReplaceLineEndings("\n") : text;

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
            CompareOptions = (flags & 0x80) is not 0 ? CompareOptions.IgnoreNonSpace : CompareOptions.None,
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
                    if (!options.IgnoreCase && IsOrdinal(options))
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
        if (!options.IgnoreCase && IsOrdinal(options))
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

    // The patches of the unit tests, each followed by the string literal after it
    private static IEnumerable<FuzzSeed> GetPatchSeeds()
    {
        var texts = GetTestStrings().ToList();
        for (var i = 0; i < texts.Count; i++)
        {
            if (!texts[i].Contains("@@", StringComparison.Ordinal))
                continue;

            var text = i + 1 < texts.Count ? texts[i + 1] : "";
            yield return new FuzzSeed(string.Create(CultureInfo.InvariantCulture, $"{i:D3}"), (byte[])[.. FuzzText.Encode(texts[i]), TextSeparator, .. FuzzText.Encode(text)]);
        }

        // The patches built by concatenating literals, as the documentation example does
        var source = FuzzResources.ReadText(typeof(TextDiffFuzzTargets).Assembly, "TextDiffTests.cs");
        var patch = string.Concat(StringLiteralRegex().Matches(source[source.IndexOf("diff --git a/greeting.txt", StringComparison.Ordinal)..]).Take(8).Select(match => Unescape(match.Groups["value"].Value)));
        yield return new FuzzSeed("greeting", (byte[])[.. FuzzText.Encode(patch), TextSeparator, .. FuzzText.Encode("Hello\nWorld\n")]);
    }

    // Each triple of consecutive string literals of the unit tests: a base, ours and theirs
    private static IEnumerable<FuzzSeed> GetMergeSeeds()
    {
        byte[][] prefixes =
        [
            [0x00, NoEditCostLimit], // Myers, lines
            [0x04, NoEditCostLimit], // Myers, words
            [0x12, NoEditCostLimit], // Histogram, lines, IgnoreCase
            [0x01, 1],               // Patience, lines, MaxEditCost = 1
        ];

        var texts = GetTestStrings().ToList();
        for (var i = 0; i + 2 < texts.Count; i++)
        {
            var prefix = prefixes[i % prefixes.Length];
            yield return new FuzzSeed(string.Create(CultureInfo.InvariantCulture, $"{i:D3}"), (byte[])[.. prefix, .. FuzzText.Encode(texts[i]), TextSeparator, .. FuzzText.Encode(texts[i + 1]), TextSeparator, .. FuzzText.Encode(texts[i + 2])]);
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
