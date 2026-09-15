namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed class MergeToolStrategy : MergeToolStrategyBase
{
    public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;
    public override bool MustReportError(InlineSnapshotSettings settings, string path) => true;
    public override void UpdateFile(InlineSnapshotSettings settings, string targetFile, string tempFile)
    {
        // Releasing the result does not stop the merge tool, and the cleanup it registered, such as deleting the
        // temporary copy of the source file, still runs when the tool exits.
        using var result = TryLaunchMergeTool(settings, targetFile, tempFile, waitForMerge: false);
        if (result is null)
        {
            TryDeleteFile(tempFile);
        }
    }
}
