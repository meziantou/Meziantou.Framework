#if MEZIANTOU_INLINE_SNAPSHOT_TESTING
namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;
#else
namespace Meziantou.Framework.SnapshotTesting.MergeTools;
#endif

internal sealed class GitDiffTool : GitTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath) => Start(currentFilePath, newFilePath, waitForMerge: false);

    internal override MergeToolResult? Start(string currentFilePath, string newFilePath, bool waitForMerge)
    {
        // The command runs in the directory of the file, so a relative path would no longer point to the file there.
        var currentFullPath = FullPath.FromPath(currentFilePath);
        var newFullPath = FullPath.FromPath(newFilePath);
        var workingDirectory = currentFullPath.Parent;
        var command = GetToolCommand(workingDirectory, "diff.tool", "difftool");
        if (command is null)
            return null;

        // As with git difftool, MERGED is the file being compared and BASE has the same value as MERGED.
        var startInfo = CreateCommandStartInfo(command, workingDirectory,
        [
            new("LOCAL", currentFullPath),
            new("REMOTE", newFullPath),
            new("MERGED", currentFullPath),
            new("BASE", currentFullPath),
        ]);

        return StartCommand(startInfo, command, onExited: null);
    }

    public override string ToString() => nameof(MergeTool.GitDiffTool);
}
