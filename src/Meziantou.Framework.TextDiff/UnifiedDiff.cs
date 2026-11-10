using System.Globalization;

namespace Meziantou.Framework;

/// <summary>Writes, reads and applies diffs in the unified format of <c>diff -u</c> and <c>git diff</c>.</summary>
public static class UnifiedDiff
{
    internal const string NoNewLineMarker = "\\ No newline at end of file";

    /// <summary>Compares <paramref name="oldText"/> and <paramref name="newText"/> line by line and returns the result as a unified diff.</summary>
    /// <param name="oldText">The original text.</param>
    /// <param name="newText">The modified text.</param>
    /// <param name="options">The file names, the number of lines of context and the algorithm.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The diff. Call <see cref="UnifiedDiffFile.ToString"/> to write it.</returns>
    /// <remarks>
    /// Lines are separated by <c>"\n"</c> only, as <c>diff</c> does: a <c>"\r"</c> before it is part of the line, so the
    /// diff applies to the old text exactly. The comparison is ordinal.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="oldText"/> or <paramref name="newText"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option of <paramref name="options"/> is out of range.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public static UnifiedDiffFile Create(string oldText, string newText, UnifiedDiffOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);

        options ??= new UnifiedDiffOptions();
        ArgumentNullException.ThrowIfNull(options.OldFileName, nameof(options));
        ArgumentNullException.ThrowIfNull(options.NewFileName, nameof(options));
        if (options.ContextLines < 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.ContextLines, $"'{nameof(UnifiedDiffOptions.ContextLines)}' must not be negative.");

        var diffOptions = TextDiff.ValidateAndGetOptions(new TextDiffOptions
        {
            Algorithm = options.Algorithm,
            Chunker = LineFeedChunker.Instance,
            Timeout = options.Timeout,
        });

        var result = TextDiff.ComputeDiff(oldText, newText, diffOptions, cancellationToken);

        // One item per line, with the 1-based number of the line in each text
        var lines = new List<(UnifiedDiffLine Line, int OldLine, int NewLine)>(result.Entries.Count);
        var oldLine = 1;
        var newLine = 1;
        foreach (var entry in result.Entries)
        {
            lines.Add(entry.Operation switch
            {
                TextDiffOperation.Equal => (new UnifiedDiffLine(TextDiffOperation.Equal, entry.OldText!), oldLine++, newLine++),
                TextDiffOperation.Delete => (new UnifiedDiffLine(TextDiffOperation.Delete, entry.OldText!), oldLine++, newLine),
                _ => (new UnifiedDiffLine(TextDiffOperation.Insert, entry.NewText!), oldLine, newLine++),
            });
        }

        var hunks = new List<UnifiedDiffHunk>();
        var contextLines = options.ContextLines;
        var index = 0;
        while (true)
        {
            var firstChange = FindChange(lines, index);
            if (firstChange < 0)
                break;

            // Changes separated by at most 2 * contextLines unchanged lines share a hunk
            var lastChange = firstChange;
            while (true)
            {
                var nextChange = FindChange(lines, lastChange + 1);
                if (nextChange < 0 || nextChange - lastChange - 1 > 2L * contextLines)
                    break;

                lastChange = nextChange;
            }

            var start = (int)Math.Max(index, (long)firstChange - contextLines);
            var end = (int)Math.Min(lines.Count, (long)lastChange + contextLines + 1);
            var hunkLines = new UnifiedDiffLine[end - start];
            var oldCount = 0;
            var newCount = 0;
            for (var i = start; i < end; i++)
            {
                var line = lines[i].Line;
                hunkLines[i - start] = line;
                if (line.Operation is not TextDiffOperation.Insert)
                {
                    oldCount++;
                }

                if (line.Operation is not TextDiffOperation.Delete)
                {
                    newCount++;
                }
            }

            // An empty range gives the number of the line before it
            var oldStart = oldCount is 0 ? lines[start].OldLine - 1 : lines[start].OldLine;
            var newStart = newCount is 0 ? lines[start].NewLine - 1 : lines[start].NewLine;
            hunks.Add(new UnifiedDiffHunk(oldStart, oldCount, newStart, newCount, sectionHeading: null, hunkLines));
            index = end;
        }

        return new UnifiedDiffFile(options.OldFileName, options.NewFileName, hunks);
    }

    /// <summary>Reads the files of a unified diff, such as the output of <c>diff -u</c> or <c>git diff</c>.</summary>
    /// <param name="patch">The text of the diff.</param>
    /// <returns>The files of the diff, in order. A <c>---</c> line followed by a <c>+++</c> line starts a file.</returns>
    /// <remarks>
    /// The lines outside the files, such as the <c>diff --git</c> and <c>index</c> lines of git, are ignored. The text after
    /// a tab in a <c>---</c> or <c>+++</c> line, a timestamp, is not part of the name. Binary patches and the renames
    /// and modes of git are not read.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="patch"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">A hunk is malformed: an invalid <c>@@</c> line, a line with an unknown prefix,
    /// or fewer lines than its header gives.</exception>
    public static IReadOnlyList<UnifiedDiffFile> Parse(string patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var lines = SplitLines(patch);
        var files = new List<UnifiedDiffFile>();
        var index = 0;
        while (index < lines.Count)
        {
            if (!lines[index].StartsWith("--- ", StringComparison.Ordinal) || index + 1 >= lines.Count || !lines[index + 1].StartsWith("+++ ", StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            var oldFileName = ParseFileName(lines[index]);
            var newFileName = ParseFileName(lines[index + 1]);
            index += 2;

            var hunks = new List<UnifiedDiffHunk>();
            while (index < lines.Count && lines[index].StartsWith("@@ ", StringComparison.Ordinal))
            {
                hunks.Add(ParseHunk(lines, ref index));
            }

            files.Add(new UnifiedDiffFile(oldFileName, newFileName, hunks));
        }

        return files;
    }

    /// <summary>Applies a unified diff that holds one file to <paramref name="text"/>.</summary>
    /// <param name="text">The text to patch.</param>
    /// <param name="patch">The text of the diff.</param>
    /// <returns>The patched text.</returns>
    /// <remarks>See <see cref="UnifiedDiffFile.Apply"/> for how the hunks are matched.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="patch"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="patch"/> does not hold exactly one file.</exception>
    /// <exception cref="FormatException"><paramref name="patch"/> is malformed.</exception>
    /// <exception cref="UnifiedDiffApplyException">A hunk does not match the text.</exception>
    public static string Apply(string text, string patch)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(patch);

        var files = Parse(patch);
        if (files.Count is not 1)
            throw new ArgumentException($"The patch must hold exactly one file, but it holds {files.Count}.", nameof(patch));

        return files[0].Apply(text);
    }

    internal static char GetPrefix(TextDiffOperation operation) => operation switch
    {
        TextDiffOperation.Delete => '-',
        TextDiffOperation.Insert => '+',
        _ => ' ',
    };

    /// <summary>Splits a text after each <c>'\n'</c>; an empty text has no line.</summary>
    internal static List<string> SplitLines(ReadOnlySpan<char> text)
    {
        var lines = new List<string>();
        while (!text.IsEmpty)
        {
            var end = text.IndexOf('\n') + 1;
            if (end is 0)
            {
                end = text.Length;
            }

            lines.Add(text[..end].ToString());
            text = text[end..];
        }

        return lines;
    }

    private static int FindChange(List<(UnifiedDiffLine Line, int OldLine, int NewLine)> lines, int start)
    {
        for (var i = start; i < lines.Count; i++)
        {
            if (lines[i].Line.Operation is not TextDiffOperation.Equal)
                return i;
        }

        return -1;
    }

    private static string ParseFileName(string line)
    {
        var name = TrimLineEnd(line.AsSpan(4));
        var tab = name.IndexOf('\t');
        if (tab >= 0)
        {
            name = name[..tab].TrimEnd('\r');
        }

        return name.ToString();
    }

    private static ReadOnlySpan<char> TrimLineEnd(ReadOnlySpan<char> line) => line.TrimEnd('\n').TrimEnd('\r');

    private static UnifiedDiffHunk ParseHunk(List<string> lines, ref int index)
    {
        var headerLineNumber = index + 1;
        var header = TrimLineEnd(lines[index].AsSpan());
        index++;

        // @@ -oldStart[,oldCount] +newStart[,newCount] @@[ heading]
        var rest = header[3..];
        if (!TryParseRange(ref rest, '-', out var oldStart, out var oldCount)
            || !TrySkip(ref rest, " ")
            || !TryParseRange(ref rest, '+', out var newStart, out var newCount)
            || !TrySkip(ref rest, " @@"))
        {
            throw new FormatException($"Line {headerLineNumber}: invalid hunk header '{header}'.");
        }

        string? sectionHeading = null;
        if (!rest.IsEmpty && rest[0] == ' ')
        {
            rest = rest[1..];
        }

        if (!rest.IsEmpty)
        {
            sectionHeading = rest.ToString();
        }

        var hunkLines = new List<UnifiedDiffLine>();
        var oldRemaining = oldCount;
        var newRemaining = newCount;
        while (oldRemaining > 0 || newRemaining > 0 || (index < lines.Count && lines[index] is ['\\', ..]))
        {
            if (index >= lines.Count)
                throw new FormatException($"Line {headerLineNumber}: the hunk ends before its {oldCount} old and {newCount} new lines.");

            var line = lines[index];
            var lineNumber = index + 1;
            index++;

            // A line feed is missing only at the end of the patch: the line still ends there
            var content = EndsWithLineFeed(line) ? line : line + "\n";
            var prefix = content[0];
            TextDiffOperation operation;
            switch (prefix)
            {
                case ' ':
                    operation = TextDiffOperation.Equal;
                    content = content[1..];
                    break;

                case '\n':
                case '\r' when content is "\r\n":
                    // Some tools strip the space of an empty line of context
                    operation = TextDiffOperation.Equal;
                    break;

                case '-':
                    operation = TextDiffOperation.Delete;
                    content = content[1..];
                    break;

                case '+':
                    operation = TextDiffOperation.Insert;
                    content = content[1..];
                    break;

                case '\\':
                    if (hunkLines.Count is 0 || !EndsWithLineFeed(hunkLines[^1].Text))
                        throw new FormatException($"Line {lineNumber}: '{NoNewLineMarker}' does not follow a line.");

                    var previous = hunkLines[^1];
                    hunkLines[^1] = new UnifiedDiffLine(previous.Operation, previous.Text[..^1]);
                    continue;

                default:
                    throw new FormatException($"Line {lineNumber}: the line of a hunk must start with ' ', '-', '+' or '\\'.");
            }

            if (operation is not TextDiffOperation.Insert)
            {
                if (oldRemaining is 0)
                    throw new FormatException($"Line {lineNumber}: the hunk has more old lines than its header gives ({oldCount}).");

                oldRemaining--;
            }

            if (operation is not TextDiffOperation.Delete)
            {
                if (newRemaining is 0)
                    throw new FormatException($"Line {lineNumber}: the hunk has more new lines than its header gives ({newCount}).");

                newRemaining--;
            }

            hunkLines.Add(new UnifiedDiffLine(operation, content));
        }

        return new UnifiedDiffHunk(oldStart, oldCount, newStart, newCount, sectionHeading, hunkLines);
    }

    internal static bool EndsWithLineFeed(string value) => value is [.., '\n'];

    private static bool TrySkip(ref ReadOnlySpan<char> value, string expected)
    {
        if (!value.StartsWith(expected, StringComparison.Ordinal))
            return false;

        value = value[expected.Length..];
        return true;
    }

    private static bool TryParseRange(ref ReadOnlySpan<char> value, char sign, out int start, out int count)
    {
        start = 0;
        count = 1;
        if (value.IsEmpty || value[0] != sign)
            return false;

        value = value[1..];
        if (!TryParseNumber(ref value, out start))
            return false;

        if (!value.IsEmpty && value[0] == ',')
        {
            value = value[1..];
            if (!TryParseNumber(ref value, out count))
                return false;
        }

        return true;
    }

    private static bool TryParseNumber(ref ReadOnlySpan<char> value, out int number)
    {
        var length = value.IndexOfAnyExceptInRange('0', '9');
        if (length < 0)
        {
            length = value.Length;
        }

        if (length is 0 || !int.TryParse(value[..length], NumberStyles.None, CultureInfo.InvariantCulture, out number))
        {
            number = 0;
            return false;
        }

        value = value[length..];
        return true;
    }

    /// <summary>Splits a text after each <c>'\n'</c>, the line separator of the unified format.</summary>
    private sealed class LineFeedChunker : TextChunker
    {
        public static LineFeedChunker Instance { get; } = new();

        public override IEnumerable<string> Chunk(ReadOnlySpan<char> value) => SplitLines(value);
    }
}
