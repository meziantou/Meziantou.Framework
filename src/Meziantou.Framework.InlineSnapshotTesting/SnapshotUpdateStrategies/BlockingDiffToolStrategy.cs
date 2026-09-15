namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed class BlockingDiffToolStrategy : MergeToolStrategyBase
{
    public override bool ReuseTemporaryFile => false;

    public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;

    public override bool MustReportError(InlineSnapshotSettings settings, string path) => true;

    public override void UpdateFile(InlineSnapshotSettings settings, string targetFile, string tempFile)
    {
        using var result = TryLaunchMergeTool(settings, targetFile, tempFile, waitForMerge: true);
        result?.WaitForExit();
        if (result is { WaitsForMerge: false })
        {
            // The launcher may have handed the files over to a running IDE and exited while the diff is still open.
            DeleteFileOnProcessExit(tempFile);
            return;
        }

        TryDeleteFile(tempFile);
    }
}
