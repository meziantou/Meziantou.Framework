using System.Diagnostics;
using Meziantou.Framework.InlineSnapshotTesting.Utils;

namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;

internal sealed class GitMergeTool : GitTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath)
    {
        var workingDirectory = FullPath.FromPath(currentFilePath).Parent;
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

                // Exited is only raised when EnableRaisingEvents is set. Without it the handler below never ran,
                // so every merge left a full copy of the source file behind under the temp directory.
                process.EnableRaisingEvents = true;

                var cleanedUp = 0;
                process.Exited += (sender, args) => DeleteOriginalCopy();

                // The tool may already have exited before the handler was attached.
                if (process.HasExited)
                {
                    DeleteOriginalCopy();
                }

                return new ProcessMergeToolResult(process);

                void DeleteOriginalCopy()
                {
                    // Exited and the HasExited check above can both reach this.
                    if (Interlocked.Exchange(ref cleanedUp, 1) is not 0)
                        return;

                    try
                    {
                        var fi = new FileInfo(originalCopy);
                        fi.TrySetReadOnly(false);
                        fi.Delete();
                    }
                    catch
                    {
                    }
                }
            }
        }

        return null;
    }
}
