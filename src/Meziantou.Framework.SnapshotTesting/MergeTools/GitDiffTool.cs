namespace Meziantou.Framework.SnapshotTesting.MergeTools;

internal sealed class GitDiffTool : GitTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath) => Start(currentFilePath, newFilePath, waitForMerge: false);

    internal override MergeToolResult? Start(string currentFilePath, string newFilePath, bool waitForMerge)
    {
        var workingDirectory = Path.GetDirectoryName(currentFilePath);
        var command = GetToolCommand(workingDirectory, "diff.tool", "difftool");
        if (command is null)
            return null;

        // As with git difftool, MERGED is the file being compared and BASE has the same value as MERGED.
        var startInfo = CreateCommandStartInfo(command, workingDirectory,
        [
            new("LOCAL", currentFilePath),
            new("REMOTE", newFilePath),
            new("MERGED", currentFilePath),
            new("BASE", currentFilePath),
        ]);

        return ProcessMergeToolResult.Start(startInfo, onExited: null);
    }

    public override string ToString() => nameof(MergeTool.GitDiffTool);
}
