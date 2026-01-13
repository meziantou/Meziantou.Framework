namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal abstract class MergeToolStrategyBase : SnapshotUpdateStrategy
{
    public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;
    public override bool MustReportError(InlineSnapshotSettings settings, string path) => true;

    protected static MergeToolResult LaunchMergeTool(InlineSnapshotSettings settings, string currentFilePath, string newFilePath)
    {
        var process = InlineSnapshotTesting.MergeTool.Launch(settings.MergeTools, currentFilePath, newFilePath);
        return process ?? throw new InlineSnapshotException("Cannot start the merge tool");
    }
}
