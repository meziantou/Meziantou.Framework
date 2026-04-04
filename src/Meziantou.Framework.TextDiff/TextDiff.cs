namespace Meziantou.Framework;

public static class TextDiff
{
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
        var inner = options.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        if (!options.IgnoreWhitespace)
            return inner;

        return new WhitespaceTrimmingComparer(inner);
    }

    private static TextDiffResult BuildResult(string[] oldChunks, string[] newChunks, DiffComputationResult diff)
    {
        var entries = new List<TextDiffEntry>();
        var hasDifferences = false;

        var lineLeft = 0;
        var lineRight = 0;

        while (lineLeft < diff.LeftLength || lineRight < diff.RightLength)
        {
            if (lineLeft < diff.LeftLength && !diff.LeftModified[lineLeft]
                && lineRight < diff.RightLength && !diff.RightModified[lineRight])
            {
                entries.Add(new TextDiffEntry(TextDiffOperation.Equal, oldChunks[lineLeft]));
                lineLeft++;
                lineRight++;
            }
            else
            {
                while (lineLeft < diff.LeftLength && (lineRight >= diff.RightLength || diff.LeftModified[lineLeft]))
                {
                    entries.Add(new TextDiffEntry(TextDiffOperation.Delete, oldChunks[lineLeft]));
                    lineLeft++;
                    hasDifferences = true;
                }

                while (lineRight < diff.RightLength && (lineLeft >= diff.LeftLength || diff.RightModified[lineRight]))
                {
                    entries.Add(new TextDiffEntry(TextDiffOperation.Insert, newChunks[lineRight]));
                    lineRight++;
                    hasDifferences = true;
                }
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

            return Trim(x.AsSpan()).Equals(Trim(y.AsSpan()), _comparison);
        }

        public int GetHashCode(string obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
            return string.GetHashCode(Trim(obj.AsSpan()), _comparison);
        }

        private static ReadOnlySpan<char> Trim(ReadOnlySpan<char> value)
        {
            var start = 0;
            while (start < value.Length && char.IsWhiteSpace(value[start]))
            {
                start++;
            }

            var end = value.Length - 1;
            while (end >= start && char.IsWhiteSpace(value[end]))
            {
                end--;
            }

            return value[start..(end + 1)];
        }
    }
}
