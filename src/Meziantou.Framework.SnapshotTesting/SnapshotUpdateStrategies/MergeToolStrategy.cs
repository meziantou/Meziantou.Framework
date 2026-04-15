namespace Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;

internal sealed class MergeToolStrategy : MergeToolStrategyBase
{
    public override bool CanUpdateSnapshot(SnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;
    public override bool MustReportError(SnapshotSettings settings, string path) => true;
    public override void UpdateFile(SnapshotSettings settings, string currentFilePath, string newFilePath)
    {
        using (LaunchMergeTool(settings, currentFilePath, newFilePath))
        {
        }
    }
}
