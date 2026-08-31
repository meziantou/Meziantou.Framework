namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed class MergeToolStrategy : MergeToolStrategyBase
{
    public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;
    public override bool MustReportError(InlineSnapshotSettings settings, string path) => true;
    public override void UpdateFile(InlineSnapshotSettings settings, string currentFilePath, string newFilePath)
    {
        using var process = TryLaunchMergeTool(settings, currentFilePath, newFilePath);
        if (process is null)
        {
            TryDeleteFile(newFilePath);
        }
    }
}
