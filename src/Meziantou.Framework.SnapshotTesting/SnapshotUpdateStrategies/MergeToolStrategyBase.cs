namespace Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;

internal abstract class MergeToolStrategyBase : SnapshotUpdateStrategy
{
    public override bool CanUpdateSnapshot(SnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;
    public override bool MustReportError(SnapshotSettings settings, string path) => true;

    protected static MergeToolResult LaunchMergeTool(SnapshotSettings settings, string currentFilePath, string newFilePath)
    {
        var process = SnapshotTesting.MergeTool.Launch(settings.MergeTools, currentFilePath, newFilePath);
        return process ?? throw new SnapshotException("Cannot start the merge tool");
    }
}
