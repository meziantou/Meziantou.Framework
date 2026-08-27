using System.Diagnostics;
using Meziantou.Framework.SnapshotTesting.Utils;

namespace Meziantou.Framework.SnapshotTesting.MergeTools;

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

                // Exited never fires unless the process is asked to raise it, and without it the copy below
                // is never deleted and the Process handle is never released.
                process.EnableRaisingEvents = true;
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
