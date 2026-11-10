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

        var lines = UnifiedDiff.SplitLines(text);
        var output = new StringBuilder(text.Length);
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
            }

            // An empty range gives the line before it
            var expected = (hunk.OldLineCount is 0 ? hunk.OldStart : hunk.OldStart - 1) + offset;
            var start = FindHunk(lines, oldLines, position, expected);
            if (start < 0)
                throw new UnifiedDiffApplyException(hunkIndex, $"Hunk {hunkIndex + 1} (@@ -{hunk.OldStart},{hunk.OldLineCount} @@) does not match the text.");

            for (var i = position; i < start; i++)
            {
                output.Append(lines[i]);
            }

            foreach (var line in hunk.Lines)
            {
                if (line.Operation is not TextDiffOperation.Delete)
                {
                    output.Append(line.Text);
                }
            }

            position = start + oldLines.Count;
            offset += start - expected;
        }

        for (var i = position; i < lines.Count; i++)
        {
            output.Append(lines[i]);
        }

        return output.ToString();
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

    // Returns the start closest to the expected one where the old lines match, or -1
    private static int FindHunk(List<string> lines, List<string> oldLines, int minimum, int expected)
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

    private static bool Matches(List<string> lines, List<string> oldLines, int start)
    {
        for (var i = 0; i < oldLines.Count; i++)
        {
            if (!string.Equals(lines[start + i], oldLines[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
