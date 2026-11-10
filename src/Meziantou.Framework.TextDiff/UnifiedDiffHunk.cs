using System.Globalization;
using System.Text;

namespace Meziantou.Framework;

/// <summary>A hunk of a unified diff: a group of changes with their lines of context, introduced by an <c>@@</c> line.</summary>
public sealed class UnifiedDiffHunk
{
    internal UnifiedDiffHunk(int oldStart, int oldLineCount, int newStart, int newLineCount, string? sectionHeading, IReadOnlyList<UnifiedDiffLine> lines)
    {
        OldStart = oldStart;
        OldLineCount = oldLineCount;
        NewStart = newStart;
        NewLineCount = newLineCount;
        SectionHeading = sectionHeading;
        Lines = lines;
    }

    /// <summary>
    /// Gets the 1-based number of the first line of the hunk in the old text. When <see cref="OldLineCount"/> is 0, it is the
    /// number of the line after which the lines are inserted, and 0 inserts them at the start of the text.
    /// </summary>
    public int OldStart { get; }

    /// <summary>Gets the number of lines of the old text that the hunk covers: its context and deleted lines.</summary>
    public int OldLineCount { get; }

    /// <summary>
    /// Gets the 1-based number of the first line of the hunk in the new text. When <see cref="NewLineCount"/> is 0, it is the
    /// number of the line after which the lines were deleted.
    /// </summary>
    public int NewStart { get; }

    /// <summary>Gets the number of lines of the new text that the hunk covers: its context and inserted lines.</summary>
    public int NewLineCount { get; }

    /// <summary>
    /// Gets the text written after the closing <c>@@</c>, such as the name of the function <c>git diff</c> writes, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    public string? SectionHeading { get; }

    /// <summary>Gets the lines of the hunk, in order.</summary>
    public IReadOnlyList<UnifiedDiffLine> Lines { get; }

    /// <summary>Returns the hunk in the unified format: its <c>@@</c> line followed by its lines.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        AppendTo(builder);
        return builder.ToString();
    }

    internal void AppendTo(StringBuilder builder)
    {
        builder.Append("@@ -");
        AppendRange(builder, OldStart, OldLineCount);
        builder.Append(" +");
        AppendRange(builder, NewStart, NewLineCount);
        builder.Append(" @@");
        if (!string.IsNullOrEmpty(SectionHeading))
        {
            builder.Append(' ').Append(SectionHeading);
        }

        builder.Append('\n');
        foreach (var line in Lines)
        {
            builder.Append(UnifiedDiff.GetPrefix(line.Operation)).Append(line.Text);
            if (!UnifiedDiff.EndsWithLineFeed(line.Text))
            {
                builder.Append('\n').Append(UnifiedDiff.NoNewLineMarker).Append('\n');
            }
        }
    }

    // A count of 1 is omitted, as diff -u does
    private static void AppendRange(StringBuilder builder, int start, int count)
    {
        builder.Append(start.ToString(CultureInfo.InvariantCulture));
        if (count is not 1)
        {
            builder.Append(',').Append(count.ToString(CultureInfo.InvariantCulture));
        }
    }
}
