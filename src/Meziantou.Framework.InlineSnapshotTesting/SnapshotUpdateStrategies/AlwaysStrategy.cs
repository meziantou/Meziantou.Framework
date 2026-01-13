namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed class AlwaysStrategy : SnapshotUpdateStrategy
{
    public override bool ReuseTemporaryFile => false;

    public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;
    public override bool MustReportError(InlineSnapshotSettings settings, string path) => true;

    public override void UpdateFile(InlineSnapshotSettings settings, string currentFilePath, string newFilePath)
    {
        MoveFile(newFilePath, currentFilePath);
    }
}
