using System.Diagnostics;
using Meziantou.Framework.InlineSnapshotTesting.Utils;

namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;

internal sealed class GitMergeTool : GitTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath)
    {
        var workingDirectory = Path.GetDirectoryName(currentFilePath);
        var toolName = GetGitConfiguration(workingDirectory, "merge.tool");
        if (toolName is not null)
        {
            var cmd = GetGitConfiguration(workingDirectory, $"mergetool.{toolName}.cmd");
            if (cmd is not null)
            {
                var originalCopy = CopyFileToTemp(currentFilePath);
                var (filename, args) = ParseCommandFromConfiguration(cmd
                         .Replace("$LOCAL", originalCopy, StringComparison.Ordinal)
                         .Replace("$REMOTE", newFilePath, StringComparison.Ordinal)
                         .Replace("$BASE", currentFilePath, StringComparison.Ordinal)
                         .Replace("$MERGED", currentFilePath, StringComparison.Ordinal));

                var process = Process.Start(filename, args);
                process.Exited += (sender, args) =>
                {
                    try
                    {
                        var fi = new FileInfo(originalCopy);
                        fi.TrySetReadOnly(false);
                        fi.Delete();
                    }
                    catch
                    {
                    }

                    process.Dispose();
                };
                return new ProcessMergeToolResult(process);
            }
        }

        return null;
    }
}
