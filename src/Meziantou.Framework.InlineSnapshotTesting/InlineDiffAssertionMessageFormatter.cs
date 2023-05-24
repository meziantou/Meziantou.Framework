using System.Text;
using DiffPlex.DiffBuilder.Model;
using DiffPlex.DiffBuilder;

namespace Meziantou.Framework.InlineSnapshotTesting;

internal sealed class InlineDiffAssertionMessageFormatter : AssertionMessageFormatter
{
    private InlineDiffAssertionMessageFormatter()
    {
    }

    public static AssertionMessageFormatter Instance { get; } = new InlineDiffAssertionMessageFormatter();

    public override string FormatMessage(string? expected, string? actual)
    {
        var diff = InlineDiffBuilder.Diff(expected ?? "", actual ?? "", ignoreWhiteSpace: false, ignoreCase: false);

        var sb = new StringBuilder();
        // Add some documentation
        sb.AppendLine("- Snapshot");
        sb.AppendLine("+ Received");
        sb.AppendLine();
        sb.AppendLine();

        foreach (var line in diff.Lines)
        {
            switch (line.Type)
            {
                case ChangeType.Inserted:
                    sb.Append("+ ");
                    break;

                case ChangeType.Deleted:
                    sb.Append("- ");
                    break;

                default:
                    sb.Append("  ");
                    break;
            }

            sb.Append(line.Text).AppendLine();
        }

        return sb.ToString();
    }
}
