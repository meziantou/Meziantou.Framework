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
