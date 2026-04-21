namespace Meziantou.Framework.SnapshotTesting;

internal sealed class InlineDiffAssertionMessageFormatter : AssertionMessageFormatter
{
    private static readonly string[] LineSeparators = ["\r\n", "\n", "\r"];
    private static readonly TextDiffOptions DiffOptions = new() { Chunker = DiffChunker.Instance };

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

            var lines = entry.Text.Split(LineSeparators, StringSplitOptions.None);
            foreach (var line in lines)
            {
                sb.Append(prefix).AppendLine(line);
            }
        }

        sb.Remove(sb.Length - Environment.NewLine.Length, Environment.NewLine.Length);

        return sb.ToString();
    }

    private sealed class DiffChunker : TextChunker
    {
        public static DiffChunker Instance { get; } = new();

        public override IEnumerable<string> Chunk(ReadOnlySpan<char> value)
        {
            return value.ToString().Split(LineSeparators, StringSplitOptions.None);
        }
    }
}
