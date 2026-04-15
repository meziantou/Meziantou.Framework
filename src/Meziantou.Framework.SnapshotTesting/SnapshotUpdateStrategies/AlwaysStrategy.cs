namespace Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;

internal sealed class AlwaysStrategy : SnapshotUpdateStrategy
{
    public override bool ReuseTemporaryFile => false;

    public override bool CanUpdateSnapshot(SnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;
    public override bool MustReportError(SnapshotSettings settings, string path) => true;

    public override void UpdateFile(SnapshotSettings settings, string currentFilePath, string newFilePath)
    {
        MoveFile(newFilePath, currentFilePath);
    }
}
