namespace Meziantou.Framework;

/// <summary>Computes the differences between two texts.</summary>
public static class TextDiff
{
    private static readonly IEqualityComparer<string> OrdinalWhitespaceComparer = new WhitespaceTrimmingComparer(StringComparer.Ordinal);
    private static readonly IEqualityComparer<string> OrdinalIgnoreCaseWhitespaceComparer = new WhitespaceTrimmingComparer(StringComparer.OrdinalIgnoreCase);


    /// <summary>Computes the differences between <paramref name="oldText"/> and <paramref name="newText"/>.</summary>
    /// <param name="oldText">The original text.</param>
    /// <param name="newText">The modified text.</param>
    /// <param name="options">The chunking and comparison options. Defaults to a line diff using
    /// <see cref="TextDiffAlgorithm.Myers"/> with no option ignored.</param>
    /// <returns>The chunks of both texts in order, each tagged with what happened to it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="oldText"/> or <paramref name="newText"/> is <see langword="null"/>.</exception>
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


    /// <summary>
    /// Computes the differences between <paramref name="oldText"/> and <paramref name="newText"/>, refining each
    /// changed chunk with the next chunker in <paramref name="chunkers"/>.
    /// </summary>
    /// <param name="oldText">The original text.</param>
    /// <param name="newText">The modified text.</param>
    /// <param name="chunkers">
    /// The chunking levels, coarsest first — for example <c>[TextChunker.Lines, TextChunker.Words]</c>. At least
    /// two are required; use <see cref="ComputeDiff"/> for a single level. <see cref="TextDiffOptions.Chunker"/>
    /// is not used by this overload.
    /// </param>
    /// <param name="options">The comparison options. Defaults to <see cref="TextDiffAlgorithm.Myers"/> with no
    /// option ignored.</param>
    /// <returns>The chunks produced by the first chunker, each of which may carry a finer diff of its own.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="oldText"/>, <paramref name="newText"/> or
    /// <paramref name="chunkers"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="chunkers"/> holds fewer than two chunkers, or one of
    /// them is <see langword="null"/>.</exception>
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

            // The deleted and inserted chunks of a block are contiguous, so track the ranges rather than
            // copying the chunks into a list per block.
            var deletedStart = left;
            while (left < leftLength && (right >= rightLength || leftModified[left]))
            {
                left++;
            }

            var deletedCount = left - deletedStart;

            var insertedStart = right;
            while (right < rightLength && (left >= leftLength || rightModified[right]))
            {
                right++;
            }

            var insertedCount = right - insertedStart;

            if (!hasInnerLevel)
            {
                for (var i = 0; i < deletedCount; i++)
                {
                    entries.Add(new TextDiffHierarchyEntry(TextDiffHierarchyOperation.Delete, oldChunks[deletedStart + i], null));
                }

                for (var i = 0; i < insertedCount; i++)
                {
                    entries.Add(new TextDiffHierarchyEntry(TextDiffHierarchyOperation.Insert, null, newChunks[insertedStart + i]));
                }

                continue;
            }

            var pairedCount = Math.Min(deletedCount, insertedCount);
            for (var i = 0; i < pairedCount; i++)
            {
                var deleted = oldChunks[deletedStart + i];
                var inserted = newChunks[insertedStart + i];
                var children = ComputeHierarchyDiffCore(deleted, inserted, chunkers, chunkerIndex + 1, options, comparer);
                entries.Add(new TextDiffHierarchyEntry(TextDiffHierarchyOperation.Replace, deleted, inserted, children.Entries));
            }

            for (var i = pairedCount; i < deletedCount; i++)
            {
                entries.Add(new TextDiffHierarchyEntry(TextDiffHierarchyOperation.Delete, oldChunks[deletedStart + i], null));
            }

            for (var i = pairedCount; i < insertedCount; i++)
            {
                entries.Add(new TextDiffHierarchyEntry(TextDiffHierarchyOperation.Insert, null, newChunks[insertedStart + i]));
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
