namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed class MergeToolStrategy : MergeToolStrategyBase
{
    public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path) => true;
    public override bool MustReportError(InlineSnapshotSettings settings, string path) => true;
    public override void UpdateFile(InlineSnapshotSettings settings, string targetFile, string tempFile)
    {
        LaunchMergeTool(settings, tempFile, targetFile).Dispose();
    }
}
