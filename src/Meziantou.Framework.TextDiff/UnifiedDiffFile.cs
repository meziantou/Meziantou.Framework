using System.Buffers;
using System.Text;

namespace Meziantou.Framework;

/// <summary>The changes of one file in a unified diff: a <c>---</c> line, a <c>+++</c> line and the hunks that follow.</summary>
public sealed class UnifiedDiffFile
{
    internal UnifiedDiffFile(string oldFileName, string newFileName, IReadOnlyList<UnifiedDiffHunk> hunks)
    {
        OldFileName = oldFileName;
        NewFileName = newFileName;
        Hunks = hunks;
    }

    /// <summary>Gets the name written after <c>---</c>, without the timestamp that may follow a tab.</summary>
    public string OldFileName { get; }

    /// <summary>Gets the name written after <c>+++</c>, without the timestamp that may follow a tab.</summary>
    public string NewFileName { get; }

    /// <summary>Gets the hunks, in order. It is empty when the two texts are equal.</summary>
    public IReadOnlyList<UnifiedDiffHunk> Hunks { get; }

    /// <summary>Applies the hunks to <paramref name="text"/>, the old text, and returns the new text.</summary>
    /// <param name="text">The text to patch.</param>
    /// <returns>The patched text.</returns>
    /// <remarks>
    /// The context and deleted lines of each hunk must match the text exactly. When a hunk does not match at the line its
    /// header gives, it is applied at the closest line where it matches, after the previous hunk, as <c>patch</c> does when
    /// lines were added or removed elsewhere in the text. The lines of the text are separated by <c>"\n"</c> only.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnifiedDiffApplyException">A hunk does not match the text.</exception>
    public string Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // The lines of the text are compared and copied where they are, rather than split into a string per line.
        // lineStarts[i] is where line i starts, and lineStarts[lineCount] is the end of the text.
        var lineCount = UnifiedDiff.CountLines(text);
        var lineStarts = ArrayPool<int>.Shared.Rent(lineCount + 1);
        try
        {
            var lineStart = 0;
            for (var i = 0; i < lineCount; i++)
            {
                lineStarts[i] = lineStart;
                var lineLength = text.AsSpan(lineStart).IndexOf('\n') + 1;
                lineStart = lineLength is 0 ? text.Length : lineStart + lineLength;
            }

            lineStarts[lineCount] = text.Length;
            var lines = new TextLines(text, lineStarts, lineCount);

            // Find where each hunk applies first, so the patched text is written once, in a string of the right length
            var hunkRanges = new (int Start, int End)[Hunks.Count];
            var length = (long)text.Length;
            var position = 0;
            var offset = 0;
            var oldLines = new List<string>();
            for (var hunkIndex = 0; hunkIndex < Hunks.Count; hunkIndex++)
            {
                var hunk = Hunks[hunkIndex];
                oldLines.Clear();
                foreach (var line in hunk.Lines)
                {
                    if (line.Operation is not TextDiffOperation.Insert)
                    {
                        oldLines.Add(line.Text);
                    }
                    else
                    {
                        length += line.Text.Length;
                    }
                }

                // An empty range gives the line before it
                var expected = (hunk.OldLineCount is 0 ? hunk.OldStart : hunk.OldStart - 1) + offset;
                var start = FindHunk(lines, oldLines, position, expected);
                if (start < 0)
                    throw new UnifiedDiffApplyException(hunkIndex, $"Hunk {hunkIndex + 1} (@@ -{hunk.OldStart},{hunk.OldLineCount} @@) does not match the text.");

                // The context lines match the text, so they keep the length; the deleted lines go away
                position = start + oldLines.Count;
                foreach (var line in hunk.Lines)
                {
                    if (line.Operation is TextDiffOperation.Delete)
                    {
                        length -= line.Text.Length;
                    }
                }

                hunkRanges[hunkIndex] = (start, position);
                offset += start - expected;
            }

            // The exception StringBuilder throws past its maximum capacity
            if (length > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(text), "The patched text is longer than the maximum length of a string.");

            return string.Create((int)length, (Hunks, Lines: lines, HunkRanges: hunkRanges), static (output, state) =>
            {
                var position = 0;
                for (var hunkIndex = 0; hunkIndex < state.Hunks.Count; hunkIndex++)
                {
                    var (start, end) = state.HunkRanges[hunkIndex];
                    Write(ref output, state.Lines.GetRange(position, start));
                    foreach (var line in state.Hunks[hunkIndex].Lines)
                    {
                        if (line.Operation is not TextDiffOperation.Delete)
                        {
                            Write(ref output, line.Text);
                        }
                    }

                    position = end;
                }

                Write(ref output, state.Lines.GetRange(position, state.Lines.Count));
            });
        }
        finally
        {
            ArrayPool<int>.Shared.Return(lineStarts);
        }
    }

    /// <summary>
    /// Returns the file in the unified format: the <c>---</c> and <c>+++</c> lines followed by the hunks, or an empty
    /// string when there is no hunk, as <c>diff -u</c> writes nothing for equal texts.
    /// </summary>
    public override string ToString()
    {
        if (Hunks.Count is 0)
            return "";

        var builder = new StringBuilder();
        builder.Append("--- ").Append(OldFileName).Append('\n');
        builder.Append("+++ ").Append(NewFileName).Append('\n');
        foreach (var hunk in Hunks)
        {
            hunk.AppendTo(builder);
        }

        return builder.ToString();
    }

    private static void Write(ref Span<char> output, ReadOnlySpan<char> value)
    {
        value.CopyTo(output);
        output = output[value.Length..];
    }

    // Returns the start closest to the expected one where the old lines match, or -1
    private static int FindHunk(TextLines lines, List<string> oldLines, int minimum, int expected)
    {
        var maximum = lines.Count - oldLines.Count;
        if (maximum < minimum)
            return -1;

        expected = Math.Clamp(expected, minimum, maximum);
        for (var distance = 0; ; distance++)
        {
            var after = expected + distance;
            var before = expected - distance;
            var afterInRange = after <= maximum;
            var beforeInRange = before >= minimum;
            if (!afterInRange && !beforeInRange)
                return -1;

            if (afterInRange && Matches(lines, oldLines, after))
                return after;

            if (distance > 0 && beforeInRange && Matches(lines, oldLines, before))
                return before;
        }
    }

    private static bool Matches(TextLines lines, List<string> oldLines, int start)
    {
        for (var i = 0; i < oldLines.Count; i++)
        {
            if (!lines.GetRange(start + i, start + i + 1).SequenceEqual(oldLines[i]))
                return false;
        }

        return true;
    }

    private readonly struct TextLines(string text, int[] lineStarts, int count)
    {
        public int Count { get; } = count;

        /// <summary>Returns the text of the lines from <paramref name="start"/> to <paramref name="end"/> (excluded).</summary>
        public ReadOnlySpan<char> GetRange(int start, int end) => text.AsSpan(lineStarts[start], lineStarts[end] - lineStarts[start]);
    }
}
