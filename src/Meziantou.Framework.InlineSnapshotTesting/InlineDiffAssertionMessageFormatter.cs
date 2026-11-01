namespace Meziantou.Framework.InlineSnapshotTesting;

internal sealed class InlineDiffAssertionMessageFormatter : AssertionMessageFormatter
{
    private InlineDiffAssertionMessageFormatter()
    {
    }

    public static AssertionMessageFormatter Instance { get; } = new InlineDiffAssertionMessageFormatter();

    public override string FormatMessage(string? expected, string? actual)
    {
        var sb = new StringBuilder();
        sb.AppendLine("- Snapshot");
        sb.AppendLine("+ Received");
        sb.AppendLine();
        sb.AppendLine();

        if (expected is null || actual is null)
        {
            // A null value has no line, so it does not appear in the diff. Otherwise, a missing snapshot and an empty
            // snapshot would show the same "- " line. The note says which side is null, in words that cannot be
            // mistaken for the content of a snapshot.
            AppendNullDiff(sb, expected, actual);
            return sb.ToString();
        }

        var result = TextDiff.ComputeDiff(expected, actual);
        var entries = result.Entries;
        HashSet<string>? deletedLines = null;
        HashSet<string>? insertedLines = null;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var prefix = entry.Operation switch
            {
                TextDiffOperation.Equal => "  ",
                TextDiffOperation.Delete => "- ",
                TextDiffOperation.Insert => "+ ",
                _ => throw new InvalidOperationException($"Unexpected operation: {entry.Operation}"),
            };

            // Trim the trailing newline included by TextChunker.Lines
            var text = entry.Text.TrimEnd('\r', '\n');
            switch (entry.Operation)
            {
                case TextDiffOperation.Delete:
                    (deletedLines ??= new(StringComparer.Ordinal)).Add(text);
                    break;

                case TextDiffOperation.Insert:
                    (insertedLines ??= new(StringComparer.Ordinal)).Add(text);
                    break;
            }

            if (i < entries.Count - 1)
            {
                sb.Append(prefix).AppendLine(text);
            }
            else
            {
                sb.Append(prefix).Append(text);
            }
        }

        if (HasLinesDifferingOnlyByTrailingWhitespace(deletedLines, insertedLines))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append("Note: some lines differ only by trailing whitespace, which is not visible above.");
        }

        return sb.ToString();
    }

    private static void AppendNullDiff(StringBuilder sb, string? expected, string? actual)
    {
        var (value, prefix) = expected is null ? (actual, "+ ") : (expected, "- ");
        if (value is not null)
        {
            foreach (var line in value.ReplaceLineEndings("\n").Split('\n'))
            {
                sb.Append(prefix).AppendLine(line);
            }

            sb.AppendLine();
        }

        var note = (expected, actual) switch
        {
            (null, null) => "Note: the snapshot and the received value are both null.",
            (null, "") => "Note: there is no snapshot (null). The received value is an empty string.",
            (null, _) => "Note: there is no snapshot (null).",
            ("", _) => "Note: the received value is null. The snapshot is an empty string.",
            _ => "Note: the received value is null.",
        };

        sb.Append(note);
    }

    private static bool HasLinesDifferingOnlyByTrailingWhitespace(HashSet<string>? deletedLines, HashSet<string>? insertedLines)
    {
        if (deletedLines is null || insertedLines is null)
            return false;

        var trimmedDeletedLines = deletedLines.Select(line => line.TrimEnd()).ToHashSet(StringComparer.Ordinal);
        foreach (var line in insertedLines)
        {
            if (!deletedLines.Contains(line) && trimmedDeletedLines.Contains(line.TrimEnd()))
                return true;
        }

        return false;
    }
}
