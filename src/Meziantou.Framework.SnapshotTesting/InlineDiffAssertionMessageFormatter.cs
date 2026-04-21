namespace Meziantou.Framework.SnapshotTesting;

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

        var diff = TextDiff.ComputeDiff(expected, actual);
        foreach (var entry in diff.Entries)
        {
            sb.Append(entry.Operation switch
            {
                TextDiffOperation.Equal => "  ",
                TextDiffOperation.Delete => "- ",
                TextDiffOperation.Insert => "+ ",
                _ => throw new InvalidOperationException("Unknown operation"),
            });
            sb.Append(entry.Text);
        }

        if (sb.Length > 0)
        {
            if (sb[^1] == '\n')
            {
                if (sb.Length > 1 && sb[^2] == '\r')
                {
                    sb.Length -= 2;
                }
                else
                {
                    sb.Length--;
                }
            }
            else if (sb[^1] == '\r')
            {
                sb.Length--;
            }
        }

        return sb.ToString();
    }
}
