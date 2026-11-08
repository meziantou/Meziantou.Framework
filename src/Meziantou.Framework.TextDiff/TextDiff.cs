namespace Meziantou.Framework;

/// <summary>Computes the differences between two texts.</summary>
public static class TextDiff
{
    private static readonly IEqualityComparer<string> OrdinalWhitespaceComparer = new WhitespaceTrimmingComparer(StringComparison.Ordinal);
    private static readonly IEqualityComparer<string> OrdinalIgnoreCaseWhitespaceComparer = new WhitespaceTrimmingComparer(StringComparison.OrdinalIgnoreCase);


    /// <summary>Computes the differences between <paramref name="oldText"/> and <paramref name="newText"/>.</summary>
    /// <param name="oldText">The original text.</param>
    /// <param name="newText">The modified text.</param>
    /// <param name="options">The chunking and comparison options. Defaults to a line diff using
    /// <see cref="TextDiffAlgorithm.Myers"/> with no option ignored.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The chunks of both texts in order, each tagged with what happened to it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="oldText"/> or <paramref name="newText"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option of <paramref name="options"/> is out of range.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public static TextDiffResult ComputeDiff(string oldText, string newText, TextDiffOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);

        options ??= new TextDiffOptions();
        ValidateOptions(options);

        var processedOld = options.IgnoreEndOfLine ? NormalizeLineEndings(oldText) : oldText;
        var processedNew = options.IgnoreEndOfLine ? NormalizeLineEndings(newText) : newText;

        var oldChunks = ToArray(options.Chunker.Chunk(processedOld));
        var newChunks = ToArray(options.Chunker.Chunk(processedNew));

        var comparer = BuildComparer(options);
        var budget = DiffBudget.Create(options, cancellationToken);
        var diff = DiffAlgorithmDispatcher.Compute(options.Algorithm, oldChunks, newChunks, comparer, budget);

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
    /// option ignored. <see cref="TextDiffOptions.Timeout"/> covers all the levels.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The chunks produced by the first chunker, each of which may carry a finer diff of its own.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="oldText"/>, <paramref name="newText"/> or
    /// <paramref name="chunkers"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="chunkers"/> holds fewer than two chunkers, or one of
    /// them is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option of <paramref name="options"/> is out of range.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public static TextDiffHierarchyResult ComputeHierarchyDiff(string oldText, string newText, IReadOnlyList<TextChunker> chunkers, TextDiffOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        ArgumentNullException.ThrowIfNull(chunkers);

        options ??= new TextDiffOptions();
        ValidateOptions(options);

        var chunkerArray = ValidateChunkers(chunkers);
        var processedOld = options.IgnoreEndOfLine ? NormalizeLineEndings(oldText) : oldText;
        var processedNew = options.IgnoreEndOfLine ? NormalizeLineEndings(newText) : newText;

        var comparer = BuildComparer(options);
        var budget = DiffBudget.Create(options, cancellationToken);
        return ComputeHierarchyDiffCore(processedOld, processedNew, chunkerArray, chunkerIndex: 0, options, comparer, budget);
    }

    private static void ValidateOptions(TextDiffOptions options)
    {
        if (!Enum.IsDefined(options.Algorithm))
            throw new ArgumentOutOfRangeException(nameof(options), options.Algorithm, $"'{nameof(TextDiffOptions.Algorithm)}' is not a valid {nameof(TextDiffAlgorithm)} value.");

        if (options.Timeout is { } timeout && timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(options), timeout, $"'{nameof(TextDiffOptions.Timeout)}' must be positive or {nameof(Timeout)}.{nameof(Timeout.InfiniteTimeSpan)}.");

        if (options.MaxEditCost is < 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxEditCost, $"'{nameof(TextDiffOptions.MaxEditCost)}' must not be negative.");
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
                // The comparison options can make two different chunks equal, so keep both sides.
                entries.Add(new TextDiffEntry(TextDiffOperation.Equal, oldChunks[lineLeft], newChunks[lineRight]));
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
        IEqualityComparer<string> comparer,
        DiffBudget budget)
    {
        var chunker = chunkers[chunkerIndex];
        var oldChunks = ToArray(chunker.Chunk(oldText));
        var newChunks = ToArray(chunker.Chunk(newText));
        var diff = DiffAlgorithmDispatcher.Compute(options.Algorithm, oldChunks, newChunks, comparer, budget);
        return BuildHierarchyResult(oldChunks, newChunks, diff, chunkers, chunkerIndex, options, comparer, budget);
    }

    private static TextDiffHierarchyResult BuildHierarchyResult(
        string[] oldChunks,
        string[] newChunks,
        DiffComputationResult diff,
        TextChunker[] chunkers,
        int chunkerIndex,
        TextDiffOptions options,
        IEqualityComparer<string> comparer,
        DiffBudget budget)
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
                var children = ComputeHierarchyDiffCore(deleted, inserted, chunkers, chunkerIndex + 1, options, comparer, budget);
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

    private sealed class WhitespaceTrimmingComparer(StringComparison comparison) : IEqualityComparer<string>
    {
        private readonly StringComparison _comparison = comparison;

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
