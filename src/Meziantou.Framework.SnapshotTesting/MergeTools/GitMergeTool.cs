using System.Diagnostics;
using Meziantou.Framework.SnapshotTesting.Utils;

namespace Meziantou.Framework.SnapshotTesting.MergeTools;

internal sealed class GitMergeTool : GitTool
{
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "ProcessMergeToolResult owns and disposes the Process instance.")]
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

                // Exited never fires unless the process is asked to raise it, and without it the copy below is
                // never deleted and the Process handle is never released. It has to be set before the process
                // starts: a tool that exits immediately would otherwise be gone before the subscription is in
                // place, and the copy would leak anyway.
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo(filename, args),
                    EnableRaisingEvents = true,
                };

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

                try
                {
                    process.Start();
                }
                catch
                {
                    process.Dispose();
                    throw;
                }

                // Ownership moves to the result, which disposes it, as does the Exited handler above.
                return new ProcessMergeToolResult(process);
            }
        }

        return null;
    }
}
