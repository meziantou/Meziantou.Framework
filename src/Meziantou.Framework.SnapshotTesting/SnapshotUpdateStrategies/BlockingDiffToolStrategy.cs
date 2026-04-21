namespace Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;

internal sealed class BlockingDiffToolStrategy : MergeToolStrategyBase
{
    public override bool ReuseTemporaryFile => false;

    public override bool CanUpdateSnapshot(SnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;

    public override bool MustReportError(SnapshotSettings settings, string path) => true;

    public override void UpdateFile(SnapshotSettings settings, string currentFilePath, string newFilePath)
    {
        using var process = LaunchMergeTool(settings, currentFilePath, newFilePath);
        process.WaitForExit();
    }
}
