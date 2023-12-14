using System.Diagnostics;

namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;

internal sealed class GitDiffTool : GitTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath)
    {
        var workingDirectory = Path.GetDirectoryName(currentFilePath);
        var toolName = GetGitConfiguration(workingDirectory, "diff.tool");
        if (toolName is not null)
        {
            var cmd = GetGitConfiguration(workingDirectory, $"difftool.{toolName}.cmd");
            if (cmd is not null)
            {
                var (filename, args) = ParseCommandFromConfiguration(cmd
                        .Replace("$LOCAL", currentFilePath, StringComparison.Ordinal)
                        .Replace("$REMOTE", newFilePath, StringComparison.Ordinal));

                var process = Process.Start(filename, args);
                return new ProcessMergeToolResult(process);
            }
        }

        return null;
    }
}
