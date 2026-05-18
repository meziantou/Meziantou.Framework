namespace Meziantou.Framework;

public static class TextDiff
{
    private static readonly IEqualityComparer<string> OrdinalWhitespaceComparer = new WhitespaceTrimmingComparer(StringComparer.Ordinal);
    private static readonly IEqualityComparer<string> OrdinalIgnoreCaseWhitespaceComparer = new WhitespaceTrimmingComparer(StringComparer.OrdinalIgnoreCase);

    public static TextDiffResult ComputeDiff(string oldText, string newText, TextDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);

        options ??= new TextDiffOptions();

        var processedOld = options.IgnoreEndOfLine ? NormalizeLineEndings(oldText) : oldText;
        var processedNew = options.IgnoreEndOfLine ? NormalizeLineEndings(newText) : newText;

        var chunker = options.Chunker ?? TextChunker.Lines;
        var oldChunks = ToArray(chunker.Chunk(processedOld));
        var newChunks = ToArray(chunker.Chunk(processedNew));

        var comparer = BuildComparer(options);
        var diff = DiffAlgorithmDispatcher.Compute(options.Algorithm, oldChunks, newChunks, comparer);

        return BuildResult(oldChunks, newChunks, diff);
    }

    public static TextDiffHierarchyResult ComputeHierarchyDiff(string oldText, string newText, IReadOnlyList<TextChunker> chunkers, TextDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        ArgumentNullException.ThrowIfNull(chunkers);

        options ??= new TextDiffOptions();

        var chunkerArray = ValidateChunkers(chunkers);
        var processedOld = options.IgnoreEndOfLine ? NormalizeLineEndings(oldText) : oldText;
        var processedNew = options.IgnoreEndOfLine ? NormalizeLineEndings(newText) : newText;

        var comparer = BuildComparer(options);
        return ComputeHierarchyDiffCore(processedOld, processedNew, chunkerArray, chunkerIndex: 0, options, comparer);
    }

    private static IEqualityComparer<string> BuildComparer(TextDiffOptions options)
    {
        if (!options.IgnoreWhitespace)
            return options.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        return options.IgnoreCase ? OrdinalIgnoreCaseWhitespaceComparer : OrdinalWhitespaceComparer;
    }

    private static TextDiffResult BuildResult(string[] oldChunks, string[] newChunks, DiffComputationResult diff)
    {
        var leftModified = diff.LeftModified;
        var rightModified = diff.RightModified;
        var leftLength = leftModified.Length;
        var rightLength = rightModified.Length;
        var entries = new List<TextDiffEntry>(leftLength + rightLength);
        var hasDifferences = false;

        var lineLeft = 0;
        var lineRight = 0;

        while (lineLeft < leftLength || lineRight < rightLength)
        {
            while (lineLeft < leftLength
                && lineRight < rightLength
                && !leftModified[lineLeft]
                && !rightModified[lineRight])
            {
                entries.Add(new TextDiffEntry(TextDiffOperation.Equal, oldChunks[lineLeft]));
                lineLeft++;
                lineRight++;
            }

            if (lineLeft >= leftLength && lineRight >= rightLength)
                break;

            hasDifferences = true;

            while (lineLeft < leftLength && (lineRight >= rightLength || leftModified[lineLeft]))
            {
                entries.Add(new TextDiffEntry(TextDiffOperation.Delete, oldChunks[lineLeft]));
                lineLeft++;
            }

            while (lineRight < rightLength && (lineLeft >= leftLength || rightModified[lineRight]))
            {
                entries.Add(new TextDiffEntry(TextDiffOperation.Insert, newChunks[lineRight]));
                lineRight++;
            }
        }

        return new TextDiffResult(entries, hasDifferences);
    }

    private static TextDiffHierarchyResult ComputeHierarchyDiffCore(
        string oldText,
        string newText,
        TextChunker[] chunkers,
        int chunkerIndex,
        TextDiffOptions options,
        IEqualityComparer<string> comparer)
    {
        var chunker = chunkers[chunkerIndex];
        var oldChunks = ToArray(chunker.Chunk(oldText));
        var newChunks = ToArray(chunker.Chunk(newText));
        var diff = DiffAlgorithmDispatcher.Compute(options.Algorithm, oldChunks, newChunks, comparer);
        return BuildHierarchyResult(oldChunks, newChunks, diff, chunkers, chunkerIndex, options, comparer);
    }

    private static TextDiffHierarchyResult BuildHierarchyResult(
        string[] oldChunks,
        string[] newChunks,
        DiffComputationResult diff,
        TextChunker[] chunkers,
        int chunkerIndex,
        TextDiffOptions options,
        IEqualityComparer<string> comparer)
    {
        var leftModified = diff.LeftModified;
        var rightModified = diff.RightModified;
        var leftLength = leftModified.Length;
        var rightLength = rightModified.Length;
        var entries = new List<TextDiffHierarchyEntry>(leftLength + rightLength);
        var hasDifferences = false;
        var hasInnerLevel = chunkerIndex + 1 < chunkers.Length;

        var left = 0;
        var right = 0;
        while (left < leftLength || right < rightLength)
        {
            while (left < leftLength
                && right < rightLength
                && !leftModified[left]
                && !rightModified[right])
            {
                entries.Add(new TextDiffHierarchyEntry(TextDiffHierarchyOperation.Equal, oldChunks[left], newChunks[right]));
                left++;
                right++;
            }

            if (left >= leftLength && right >= rightLength)
                break;

            hasDifferences = true;

            var deletedChunks = new List<string>();
            while (left < leftLength && (right >= rightLength || leftModified[left]))
            {
                deletedChunks.Add(oldChunks[left]);
                left++;
            }

            var insertedChunks = new List<string>();
            while (right < rightLength && (left >= leftLength || rightModified[right]))
            {
                insertedChunks.Add(newChunks[right]);
                right++;
            }

            if (!hasInnerLevel)
            {
                for (var i = 0; i < deletedChunks.Count; i++)
                {
                    entries.Add(new TextDiffHierarchyEntry(TextDiffHierarchyOperation.Delete, deletedChunks[i], null));
                }

                for (var i = 0; i < insertedChunks.Count; i++)
                {
                    entries.Add(new TextDiffHierarchyEntry(TextDiffHierarchyOperation.Insert, null, insertedChunks[i]));
                }

                continue;
            }

            var pairedCount = Math.Min(deletedChunks.Count, insertedChunks.Count);
            for (var i = 0; i < pairedCount; i++)
            {
                var children = ComputeHierarchyDiffCore(deletedChunks[i], insertedChunks[i], chunkers, chunkerIndex + 1, options, comparer);
                entries.Add(new TextDiffHierarchyEntry(TextDiffHierarchyOperation.Replace, deletedChunks[i], insertedChunks[i], children.Entries));
            }

            for (var i = pairedCount; i < deletedChunks.Count; i++)
            {
                entries.Add(new TextDiffHierarchyEntry(TextDiffHierarchyOperation.Delete, deletedChunks[i], null));
            }

            for (var i = pairedCount; i < insertedChunks.Count; i++)
            {
                entries.Add(new TextDiffHierarchyEntry(TextDiffHierarchyOperation.Insert, null, insertedChunks[i]));
            }
        }

        return new TextDiffHierarchyResult(entries, hasDifferences);
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.ReplaceLineEndings("\n");
    }

    private static string[] ToArray(IEnumerable<string> source)
    {
        if (source is string[] array)
            return array;

        if (source is List<string> list)
            return list.ToArray();

        return source.ToArray();
    }

    private static TextChunker[] ValidateChunkers(IReadOnlyList<TextChunker> chunkers)
    {
        if (chunkers.Count < 2)
            throw new ArgumentException("At least 2 chunkers must be provided.", nameof(chunkers));

        var chunkerArray = new TextChunker[chunkers.Count];
        for (var i = 0; i < chunkers.Count; i++)
        {
            chunkerArray[i] = chunkers[i] ?? throw new ArgumentException("Chunkers cannot contain null values.", nameof(chunkers));
        }

        return chunkerArray;
    }

    private sealed class WhitespaceTrimmingComparer(StringComparer inner) : IEqualityComparer<string>
    {
        private readonly StringComparison _comparison = inner == StringComparer.OrdinalIgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        public bool Equals(string? x, string? y)
        {
            if (x is null)
                return y is null;

            if (y is null)
                return false;

            return x.AsSpan().Trim().Equals(y.AsSpan().Trim(), _comparison);
        }

        public int GetHashCode(string obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
            var trimmed = obj.AsSpan().Trim();
            return string.GetHashCode(trimmed, _comparison);
        }
    }
}
