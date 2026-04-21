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

            if (i < entries.Count - 1)
            {
                sb.Append(prefix).AppendLine(text);
            }
            else
            {
                sb.Append(prefix).Append(text);
            }
        }

        return sb.ToString();
    }
}
