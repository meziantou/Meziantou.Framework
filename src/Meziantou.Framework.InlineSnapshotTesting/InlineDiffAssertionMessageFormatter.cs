namespace Meziantou.Framework.InlineSnapshotTesting;

internal sealed class InlineDiffAssertionMessageFormatter : AssertionMessageFormatter
{
    private InlineDiffAssertionMessageFormatter()
    {
    }

    public static AssertionMessageFormatter Instance { get; } = new InlineDiffAssertionMessageFormatter();

    public override string FormatMessage(string? expected, string? actual)
    {
        expected ??= "";
        actual ??= "";

        var sb = new StringBuilder();
        sb.AppendLine("- Snapshot");
        sb.AppendLine("+ Received");
        sb.AppendLine();
        sb.AppendLine();

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
