namespace Meziantou.Framework.SnapshotTesting;

internal sealed class InlineDiffAssertionMessageFormatter : AssertionMessageFormatter
{
    private static readonly TextDiffOptions DiffOptions = new() { Chunker = TextChunker.Lines };

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

        var diff = TextDiff.ComputeDiff(expected, actual, DiffOptions);
        foreach (var entry in diff.Entries)
        {
            var prefix = entry.Operation switch
            {
                TextDiffOperation.Equal => "  ",
                TextDiffOperation.Delete => "- ",
                TextDiffOperation.Insert => "+ ",
                _ => throw new InvalidOperationException("Unknown operation"),
            };

            sb.Append(prefix).AppendLine(entry.Text.TrimEnd('\r', '\n'));
        }

        sb.Remove(sb.Length - Environment.NewLine.Length, Environment.NewLine.Length);

        return sb.ToString();
    }
}
