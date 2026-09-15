#if MEZIANTOU_INLINE_SNAPSHOT_TESTING
namespace Meziantou.Framework.InlineSnapshotTesting.MergeTools;
#else
namespace Meziantou.Framework.SnapshotTesting.MergeTools;
#endif

internal sealed class GitMergeTool : GitTool
{
    public override MergeToolResult? Start(string currentFilePath, string newFilePath) => Start(currentFilePath, newFilePath, waitForMerge: false);

    internal override MergeToolResult? Start(string currentFilePath, string newFilePath, bool waitForMerge)
    {
        var workingDirectory = Path.GetDirectoryName(currentFilePath);
        var command = GetToolCommand(workingDirectory, "merge.tool", "mergetool");
        if (command is null)
            return null;

        // The variables have the meaning git gives them: LOCAL is "ours", the verified file as it is now, REMOTE is
        // "theirs", the new snapshot, and MERGED is the file the tool writes the result to. The verified file as it is
        // now is also the common ancestor of both sides, so BASE is the same copy as LOCAL. The copy keeps LOCAL and
        // BASE intact while the tool writes MERGED.
        var originalCopy = CopyFileToTemp(currentFilePath);
        try
        {
            var startInfo = CreateCommandStartInfo(command, workingDirectory,
            [
                new("LOCAL", originalCopy),
                new("REMOTE", newFilePath),
                new("BASE", originalCopy),
                new("MERGED", currentFilePath),
            ]);

            // git waits for the command to exit before it deletes its own copies, so a merge tool command is
            // expected to block until the merge is done.
            return ProcessMergeToolResult.Start(startInfo, onExited: () => DeleteTemporaryCopy(originalCopy));
        }
        catch
        {
            DeleteTemporaryCopy(originalCopy);
            throw;
        }
    }

    public override string ToString() => nameof(MergeTool.GitMergeTool);
}
