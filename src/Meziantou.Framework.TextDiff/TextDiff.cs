using System.Text;

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
                entries.Add(new TextDiffEntry(TextDiffOperation.Equal, oldChunks[lineLeft].AsMemory()));
                lineLeft++;
                lineRight++;
            }
            else
            {
                while (lineLeft < diff.LeftLength && (lineRight >= diff.RightLength || diff.LeftModified[lineLeft]))
                {
                    entries.Add(new TextDiffEntry(TextDiffOperation.Delete, oldChunks[lineLeft].AsMemory()));
                    lineLeft++;
                    hasDifferences = true;
                }

                while (lineRight < diff.RightLength && (lineLeft >= diff.LeftLength || diff.RightModified[lineRight]))
                {
                    entries.Add(new TextDiffEntry(TextDiffOperation.Insert, newChunks[lineRight].AsMemory()));
                    lineRight++;
                    hasDifferences = true;
                }
            }
        }

        return new TextDiffResult(entries, hasDifferences);
    }

    private static string NormalizeLineEndings(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                sb.Append('\n');
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
            }
            else if (c is '\u0085' or '\u2028' or '\u2029')
            {
                sb.Append('\n');
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
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
        public bool Equals(string? x, string? y) => inner.Equals(x?.Trim(), y?.Trim());

        public int GetHashCode(string obj) => inner.GetHashCode(obj.Trim());
    }
}
